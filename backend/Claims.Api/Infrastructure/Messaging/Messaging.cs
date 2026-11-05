using System.Text;
using Claims.Api.Domain;
using Claims.Api.Infrastructure.Persistence;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Claims.Api.Infrastructure.Messaging;

public class MessagingOptions
{
    /// <summary>"InProcess" (default, no infrastructure needed) or "Kafka".</summary>
    public string Transport { get; set; } = "InProcess";
    public bool EnableBackgroundDispatch { get; set; } = true;
    public int OutboxPollIntervalMs { get; set; } = 1000;
    public int MaxAttempts { get; set; } = 10;
    public KafkaOptions Kafka { get; set; } = new();

    public bool UsesKafka => string.Equals(Transport, "Kafka", StringComparison.OrdinalIgnoreCase);
}

public class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "claims.claim-events.v1";
    public string ConsumerGroup { get; set; } = "claims-notifications";
}

/// <summary>A consumer of claim events. Must be idempotent: delivery is at-least-once.</summary>
public interface IClaimEventHandler
{
    Task HandleAsync(IClaimEvent evt, CancellationToken ct);
}

/// <summary>Deserialises an event and runs every registered handler in its own DI scope.</summary>
public class ClaimEventDispatcher(IServiceScopeFactory scopes)
{
    public async Task DispatchAsync(string type, string payload, CancellationToken ct)
    {
        var evt = EventSerializer.Deserialize(type, payload);
        using var scope = scopes.CreateScope();
        foreach (var handler in scope.ServiceProvider.GetServices<IClaimEventHandler>())
            await handler.HandleAsync(evt, ct);
    }
}

public interface IEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

/// <summary>Delivers events straight to local handlers. Same semantics as Kafka minus the broker.</summary>
public class InProcessEventPublisher(ClaimEventDispatcher dispatcher) : IEventPublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken ct) =>
        dispatcher.DispatchAsync(message.Type, message.Payload, ct);
}

/// <summary>Publishes to a Kafka topic, keyed by claim id so all events for a claim land on one partition in order.</summary>
public class KafkaEventPublisher(IOptions<MessagingOptions> options) : IEventPublisher, IDisposable
{
    private readonly Lazy<IProducer<string, string>> _producer = new(() =>
        new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.Kafka.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All
        }).Build());

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        var kafkaMessage = new Message<string, string>
        {
            Key = message.ClaimId.ToString(),
            Value = message.Payload,
            Headers = new Headers
            {
                { "event-type", Encoding.UTF8.GetBytes(message.Type) },
                { "event-id", Encoding.UTF8.GetBytes(message.Id.ToString()) }
            }
        };
        await _producer.Value.ProduceAsync(options.Value.Kafka.Topic, kafkaMessage, ct);
    }

    public void Dispose()
    {
        if (!_producer.IsValueCreated) return;
        _producer.Value.Flush(TimeSpan.FromSeconds(5));
        _producer.Value.Dispose();
    }
}

/// <summary>Reads unpublished outbox rows in order and hands them to the configured publisher.</summary>
public class OutboxProcessor(
    IServiceScopeFactory scopes,
    IEventPublisher publisher,
    IOptions<MessagingOptions> options,
    TimeProvider clock,
    ILogger<OutboxProcessor> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<int> ProcessPendingAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
            var maxAttempts = options.Value.MaxAttempts;

            var batch = await db.OutboxMessages
                .Where(m => m.ProcessedAt == null && m.Attempts < maxAttempts)
                .OrderBy(m => m.OccurredAt)
                .Take(50)
                .ToListAsync(ct);

            var published = 0;
            foreach (var message in batch)
            {
                try
                {
                    await publisher.PublishAsync(message, ct);
                    message.MarkProcessed(clock.GetUtcNow().UtcDateTime);
                    published++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    message.MarkFailed(ex.Message);
                    logger.LogWarning(ex, "Publishing outbox message {MessageId} ({Type}) failed (attempt {Attempt})",
                        message.Id, message.Type, message.Attempts);
                    // Stop the batch so later events are not published ahead of this one.
                    break;
                }
            }

            await db.SaveChangesAsync(ct);
            return published;
        }
        finally
        {
            _gate.Release();
        }
    }
}

public class OutboxBackgroundService(
    OutboxProcessor processor,
    IOptions<MessagingOptions> options,
    ILogger<OutboxBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableBackgroundDispatch) return;

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.OutboxPollIntervalMs));
        try
        {
            do
            {
                try
                {
                    await processor.ProcessPendingAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Outbox processing failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}

/// <summary>When Transport=Kafka, consumes the claim events topic and runs local handlers (e.g. notifications).</summary>
public class KafkaConsumerBackgroundService(
    ClaimEventDispatcher dispatcher,
    IOptions<MessagingOptions> options,
    ILogger<KafkaConsumerBackgroundService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.UsesKafka || !options.Value.EnableBackgroundDispatch) return Task.CompletedTask;
        return Task.Factory.StartNew(() => ConsumeLoopAsync(stoppingToken), stoppingToken,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var kafka = options.Value.Kafka;
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = kafka.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();

        consumer.Subscribe(kafka.Topic);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = consumer.Consume(ct);
                var type = Encoding.UTF8.GetString(result.Message.Headers.GetLastBytes("event-type"));

                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await dispatcher.DispatchAsync(type, result.Message.Value, ct);
                        break;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && attempt < 3)
                    {
                        logger.LogWarning(ex, "Handling {Type} failed (attempt {Attempt}); retrying", type, attempt);
                        await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Production: route to a dead-letter topic. Prototype: log and move on.
                        logger.LogError(ex, "Giving up on {Type} at offset {Offset}", type, result.TopicPartitionOffset);
                        break;
                    }
                }

                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        finally
        {
            consumer.Close();
        }
    }
}

public static class MessagingExtensions
{
    public static IServiceCollection AddClaimsMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MessagingOptions>(configuration.GetSection("Messaging"));
        services.AddSingleton<ClaimEventDispatcher>();
        services.AddSingleton<InProcessEventPublisher>();
        services.AddSingleton<KafkaEventPublisher>();
        services.AddSingleton<IEventPublisher>(sp =>
            sp.GetRequiredService<IOptions<MessagingOptions>>().Value.UsesKafka
                ? sp.GetRequiredService<KafkaEventPublisher>()
                : sp.GetRequiredService<InProcessEventPublisher>());
        services.AddSingleton<OutboxProcessor>();
        services.AddHostedService<OutboxBackgroundService>();
        services.AddHostedService<KafkaConsumerBackgroundService>();
        return services;
    }
}

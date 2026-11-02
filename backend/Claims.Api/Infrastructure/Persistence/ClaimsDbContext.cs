using Claims.Api.Domain;
using Claims.Api.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Claims.Api.Infrastructure.Persistence;

public class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<InformationRequest> InformationRequests => Set<InformationRequest>();
    public DbSet<ClaimHistoryEntry> ClaimHistory => Set<ClaimHistoryEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var claim = b.Entity<Claim>();
        claim.HasKey(x => x.Id);
        claim.Property(x => x.Id).ValueGeneratedNever();
        claim.Property(x => x.ClaimNumber).HasMaxLength(32).IsRequired();
        claim.HasIndex(x => x.ClaimNumber).IsUnique();
        claim.Property(x => x.ClaimantId).HasMaxLength(64).IsRequired();
        claim.HasIndex(x => x.ClaimantId);
        claim.Property(x => x.ClaimantName).HasMaxLength(120).IsRequired();
        claim.Property(x => x.ClaimantEmail).HasMaxLength(180).IsRequired();
        claim.Property(x => x.PolicyNumber).HasMaxLength(40).IsRequired();
        claim.Property(x => x.MarketCode).HasMaxLength(4).IsRequired();
        claim.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        claim.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        claim.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        claim.HasIndex(x => new { x.Status, x.AssignedOfficerId });
        claim.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        claim.Property(x => x.DecisionReason).HasMaxLength(1000);
        claim.Property(x => x.AssignedOfficerId).HasMaxLength(64);
        claim.Property(x => x.AssignedOfficerName).HasMaxLength(120);
        claim.Property(x => x.EstimatedAmount).HasPrecision(18, 2);
        claim.Property(x => x.ApprovedAmount).HasPrecision(18, 2);
        claim.Property(x => x.SettledAmount).HasPrecision(18, 2);
        claim.Property(x => x.Version).IsConcurrencyToken();
        claim.Ignore(x => x.DomainEvents);
        claim.Ignore(x => x.IsOpen);
        claim.Ignore(x => x.OpenInformationRequest);
        claim.Ignore(x => x.OutstandingLiability);
        claim.HasMany(x => x.InformationRequests).WithOne().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
        claim.HasMany(x => x.History).WithOne().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);

        var request = b.Entity<InformationRequest>();
        request.HasKey(x => x.Id);
        request.Property(x => x.Id).ValueGeneratedNever();
        request.Property(x => x.Question).HasMaxLength(1000).IsRequired();
        request.Property(x => x.Response).HasMaxLength(2000);
        request.Property(x => x.RequestedById).HasMaxLength(64);
        request.Property(x => x.RequestedByName).HasMaxLength(120);
        request.Ignore(x => x.IsOpen);

        var history = b.Entity<ClaimHistoryEntry>();
        history.HasKey(x => x.Id);
        history.Property(x => x.Id).ValueGeneratedNever();
        history.Property(x => x.Action).HasMaxLength(40);
        history.Property(x => x.ActorId).HasMaxLength(64);
        history.Property(x => x.ActorName).HasMaxLength(120);
        history.Property(x => x.ActorRole).HasConversion<string>().HasMaxLength(16);
        history.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(32);
        history.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(32);
        history.Property(x => x.Note).HasMaxLength(1000);

        var notification = b.Entity<Notification>();
        notification.HasKey(x => x.Id);
        notification.Property(x => x.Id).ValueGeneratedNever();
        notification.Property(x => x.RecipientId).HasMaxLength(64).IsRequired();
        notification.Property(x => x.ClaimNumber).HasMaxLength(32);
        notification.Property(x => x.Title).HasMaxLength(120);
        notification.Property(x => x.Message).HasMaxLength(2000);
        notification.HasIndex(x => new { x.RecipientId, x.CreatedAt });

        var outbox = b.Entity<OutboxMessage>();
        outbox.HasKey(x => x.Id);
        outbox.Property(x => x.Id).ValueGeneratedNever();
        outbox.Property(x => x.Type).HasMaxLength(64).IsRequired();
        outbox.Property(x => x.LastError).HasMaxLength(1000);
        outbox.HasIndex(x => new { x.ProcessedAt, x.OccurredAt });

        var processed = b.Entity<ProcessedEvent>();
        processed.HasKey(x => new { x.EventId, x.Consumer });
        processed.Property(x => x.Consumer).HasMaxLength(64);

        // SQLite returns DateTime with Kind=Unspecified; all stored values are UTC, so mark them as such
        // so the API serialises them with a 'Z' suffix.
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullable = new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
        foreach (var property in b.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime)) property.SetValueConverter(utc);
            else if (property.ClrType == typeof(DateTime?)) property.SetValueConverter(utcNullable);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var claims = CaptureDomainEvents();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        claims.ForEach(c => c.ClearDomainEvents());
        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var claims = CaptureDomainEvents();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        claims.ForEach(c => c.ClearDomainEvents());
        return result;
    }

    /// <summary>Converts pending domain events into outbox rows that commit atomically with the claim change.</summary>
    private List<Claim> CaptureDomainEvents()
    {
        var claims = ChangeTracker.Entries<Claim>()
            .Select(e => e.Entity)
            .Where(c => c.DomainEvents.Count > 0)
            .ToList();

        var alreadyQueued = ChangeTracker.Entries<OutboxMessage>().Select(e => e.Entity.Id).ToHashSet();
        foreach (var evt in claims.SelectMany(c => c.DomainEvents).Where(e => !alreadyQueued.Contains(e.EventId)))
            OutboxMessages.Add(OutboxMessage.From(evt));

        return claims;
    }
}

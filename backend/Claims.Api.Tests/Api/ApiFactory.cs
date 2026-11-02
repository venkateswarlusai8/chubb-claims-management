using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Claims.Api.Infrastructure.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Claims.Api.Tests.Api;

/// <summary>
/// Boots the real API against a throw-away SQLite file with no demo data and background outbox
/// dispatch disabled, so tests can drive event delivery deterministically.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"claims-tests-{Guid.NewGuid():N}.db");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ClaimsDb", $"Data Source={_dbPath}");
        builder.UseSetting("Database:SeedDemoData", "false");
        builder.UseSetting("Messaging:Transport", "InProcess");
        builder.UseSetting("Messaging:EnableBackgroundDispatch", "false");
    }

    public HttpClient ClientFor(string demoUser)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Demo-User", demoUser);
        return client;
    }

    /// <summary>Runs one outbox pass, as the background service would.</summary>
    public Task<int> DispatchEventsAsync() => Services.GetRequiredService<OutboxProcessor>().ProcessPendingAsync();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
            if (File.Exists(file)) File.Delete(file);
    }
}

public static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, ApiFactory.Json);
}

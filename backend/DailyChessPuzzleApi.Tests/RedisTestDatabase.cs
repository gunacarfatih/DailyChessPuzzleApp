using DailyChessPuzzleApi.Services;
using StackExchange.Redis;

namespace DailyChessPuzzleApi.Tests;

internal sealed class RedisTestDatabase : IAsyncDisposable
{
    private readonly ConnectionMultiplexer _connection;

    private RedisTestDatabase(
        ConnectionMultiplexer connection,
        string keyPrefix)
    {
        _connection = connection;
        KeyPrefix = keyPrefix;
    }

    public string KeyPrefix { get; }

    public IDatabase Database =>
        _connection.GetDatabase();

    public IConnectionMultiplexer Connection =>
        _connection;

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("DCP_TEST_REDIS")
        ?? throw new InvalidOperationException(
            "DCP_TEST_REDIS is not configured.");

    public static async Task<RedisTestDatabase> CreateAsync()
    {
        var connection =
            await ConnectionMultiplexer.ConnectAsync(
                ConnectionString);

        try
        {
            await connection.GetDatabase().PingAsync();

            return new RedisTestDatabase(
                connection,
                $"dcp_test:{Guid.NewGuid():N}:");
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public string CreateKey(string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);

        return $"{KeyPrefix}{suffix}";
    }

    public LeaderboardCacheService CreateCacheService()
    {
        return new LeaderboardCacheService(_connection);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (var endpoint in _connection.GetEndPoints())
            {
                var server = _connection.GetServer(endpoint);

                if (!server.IsConnected || server.IsReplica)
                {
                    continue;
                }

                await foreach (var key in server.KeysAsync(
                    database: Database.Database,
                    pattern: $"{KeyPrefix}*"))
                {
                    await Database.KeyDeleteAsync(key);
                }
            }
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }
}
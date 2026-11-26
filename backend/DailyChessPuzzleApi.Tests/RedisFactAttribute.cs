using Xunit;

namespace DailyChessPuzzleApi.Tests;

public sealed class RedisFactAttribute : FactAttribute
{
    public RedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("DCP_TEST_REDIS")))
        {
            Skip = "DCP_TEST_REDIS is not configured.";
        }
    }
}

public sealed class PostgresRedisFactAttribute : FactAttribute
{
    public PostgresRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("DCP_TEST_POSTGRES")))
        {
            Skip = "DCP_TEST_POSTGRES is not configured.";
        }
        else if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("DCP_TEST_REDIS")))
        {
            Skip = "DCP_TEST_REDIS is not configured.";
        }
    }
}
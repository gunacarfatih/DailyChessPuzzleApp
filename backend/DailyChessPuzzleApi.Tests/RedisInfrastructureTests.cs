using Xunit;

namespace DailyChessPuzzleApi.Tests;

[Collection("Postgres integration")]
public sealed class RedisInfrastructureTests
{
    [RedisFact]
    public async Task Redis_connection_should_succeed()
    {
        await using var redis =
            await RedisTestDatabase.CreateAsync();

        var key = redis.CreateKey("connection");

        await redis.Database.StringSetAsync(
            key,
            "connected");

        var value =
            await redis.Database.StringGetAsync(key);

        Assert.Equal("connected", value.ToString());
    }

    [RedisFact]
    public async Task Redis_test_keys_should_be_isolated_and_cleaned()
    {
        await using var first =
            await RedisTestDatabase.CreateAsync();

        var firstKey = first.CreateKey("isolation");

        await first.Database.StringSetAsync(
            firstKey,
            "first");

        string secondKey;

        await using (var second =
            await RedisTestDatabase.CreateAsync())
        {
            secondKey = second.CreateKey("isolation");

            Assert.NotEqual(
                first.KeyPrefix,
                second.KeyPrefix);

            Assert.NotEqual(firstKey, secondKey);

            await second.Database.StringSetAsync(
                secondKey,
                "second");

            Assert.Equal(
                "first",
                (await first.Database
                    .StringGetAsync(firstKey)).ToString());

            Assert.Equal(
                "second",
                (await second.Database
                    .StringGetAsync(secondKey)).ToString());
        }

        // Ikinci testin anahtari temizlenmeli.
        Assert.False(
            await first.Database.KeyExistsAsync(secondKey));

        // Birinci testin anahtari korunmali.
        Assert.True(
            await first.Database.KeyExistsAsync(firstKey));

        Assert.Equal(
            "first",
            (await first.Database
                .StringGetAsync(firstKey)).ToString());
    }
}
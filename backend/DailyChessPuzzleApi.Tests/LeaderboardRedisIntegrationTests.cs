using Xunit;

namespace DailyChessPuzzleApi.Tests;

[Collection("Postgres integration")]
public sealed class LeaderboardRedisIntegrationTests
{
    private const string ClientId = "user:1";

    [RedisFact]
    public async Task Global_and_country_scores_are_stored_independently()
    {
        await using var redis =
            await RedisTestDatabase.CreateAsync();

        var cache = redis.CreateCacheService();

        var globalKey =
            redis.CreateKey("alltime:all:global");

        var turkeyKey =
            redis.CreateKey("alltime:all:TR");

        var germanyKey =
            redis.CreateKey("alltime:all:DE");

        await cache.UpdateScoreAsync(
            globalKey, ClientId, 1450, 2, 2);

        await cache.UpdateScoreAsync(
            turkeyKey, ClientId, 800, 1, 1);

        await cache.UpdateScoreAsync(
            germanyKey, ClientId, 650, 1, 2);

        var global = await cache.GetUserScoreAsync(
            globalKey, ClientId);

        var turkey = await cache.GetUserScoreAsync(
            turkeyKey, ClientId);

        var germany = await cache.GetUserScoreAsync(
            germanyKey, ClientId);

        Assert.NotNull(global);
        Assert.NotNull(turkey);
        Assert.NotNull(germany);

        Assert.Equal((1450, 2), global.Value);
        Assert.Equal((800, 1), turkey.Value);
        Assert.Equal((650, 1), germany.Value);

        var globalTop = Assert.Single(
            await cache.GetTopAsync(globalKey, 10));

        var turkeyTop = Assert.Single(
            await cache.GetTopAsync(turkeyKey, 10));

        var germanyTop = Assert.Single(
            await cache.GetTopAsync(germanyKey, 10));

        Assert.Equal(
            (ClientId, 1450, 2),
            globalTop);

        Assert.Equal(
            (ClientId, 800, 1),
            turkeyTop);

        Assert.Equal(
            (ClientId, 650, 1),
            germanyTop);

        Assert.Equal(
            1L,
            await cache.GetRankAsync(
                globalKey, ClientId));

        Assert.Equal(
            1L,
            await cache.GetRankAsync(
                turkeyKey, ClientId));

        Assert.Equal(
            1L,
            await cache.GetRankAsync(
                germanyKey, ClientId));
    }

    [RedisFact]
    public async Task New_country_score_does_not_modify_previous_country()
    {
        await using var redis =
            await RedisTestDatabase.CreateAsync();

        var cache = redis.CreateCacheService();

        var globalKey =
            redis.CreateKey("alltime:all:global");

        var turkeyKey =
            redis.CreateKey("alltime:all:TR");

        var germanyKey =
            redis.CreateKey("alltime:all:DE");

        // Kullanici Turkiye'de puan kazaniyor.
        await cache.UpdateScoreAsync(
            globalKey, ClientId, 800, 1, 1);

        await cache.UpdateScoreAsync(
            turkeyKey, ClientId, 800, 1, 1);

        Assert.Null(
            await cache.GetUserScoreAsync(
                germanyKey, ClientId));

        // Kullanici Almanya'da yeni puan kazaniyor.
        await cache.UpdateScoreAsync(
            globalKey, ClientId, 1450, 2, 2);

        await cache.UpdateScoreAsync(
            germanyKey, ClientId, 650, 1, 2);

        // Kullanici Almanya'da tekrar puan kazaniyor.
        await cache.UpdateScoreAsync(
            globalKey, ClientId, 2150, 3, 3);

        await cache.UpdateScoreAsync(
            germanyKey, ClientId, 1350, 2, 3);

        var global = await cache.GetUserScoreAsync(
            globalKey, ClientId);

        var turkey = await cache.GetUserScoreAsync(
            turkeyKey, ClientId);

        var germany = await cache.GetUserScoreAsync(
            germanyKey, ClientId);

        Assert.NotNull(global);
        Assert.NotNull(turkey);
        Assert.NotNull(germany);

        Assert.Equal((2150, 3), global.Value);

        // Turkiye'deki eski puan degismemeli.
        Assert.Equal((800, 1), turkey.Value);

        Assert.Equal((1350, 2), germany.Value);

        // Guncelleme yeni bir Redis uyesi olusturmamali.
        Assert.Single(
            await cache.GetTopAsync(globalKey, 10));

        Assert.Single(
            await cache.GetTopAsync(turkeyKey, 10));

        Assert.Single(
            await cache.GetTopAsync(germanyKey, 10));

        var turkeyRevision =
            await redis.Database.HashGetAsync(
                $"{turkeyKey}:revisions",
                ClientId);

        var germanyRevision =
            await redis.Database.HashGetAsync(
                $"{germanyKey}:revisions",
                ClientId);

        Assert.Equal("1", turkeyRevision.ToString());
        Assert.Equal("3", germanyRevision.ToString());
    }

    [RedisFact]
    public async Task Score_without_country_updates_only_global_cache()
    {
        await using var redis =
            await RedisTestDatabase.CreateAsync();

        var cache = redis.CreateCacheService();

        var globalKey =
            redis.CreateKey("alltime:all:global");

        var turkeyKey =
            redis.CreateKey("alltime:all:TR");

        var germanyKey =
            redis.CreateKey("alltime:all:DE");

        var unitedKingdomKey =
            redis.CreateKey("alltime:all:GB");

        await cache.UpdateScoreAsync(
            globalKey, ClientId, 800, 1, 1);

        await cache.UpdateScoreAsync(
            turkeyKey, ClientId, 800, 1, 1);

        await cache.UpdateScoreAsync(
            globalKey, ClientId, 1450, 2, 2);

        await cache.UpdateScoreAsync(
            germanyKey, ClientId, 650, 1, 2);

        // Ulke bilgisi olmayan yeni puan.
        await cache.UpdateScoreAsync(
            globalKey, ClientId, 1950, 3, 3);

        var global = await cache.GetUserScoreAsync(
            globalKey, ClientId);

        var turkey = await cache.GetUserScoreAsync(
            turkeyKey, ClientId);

        var germany = await cache.GetUserScoreAsync(
            germanyKey, ClientId);

        Assert.NotNull(global);
        Assert.NotNull(turkey);
        Assert.NotNull(germany);

        Assert.Equal((1950, 3), global.Value);
        Assert.Equal((800, 1), turkey.Value);
        Assert.Equal((650, 1), germany.Value);

        Assert.Null(
            await cache.GetUserScoreAsync(
                unitedKingdomKey, ClientId));

        Assert.Empty(
            await cache.GetTopAsync(
                unitedKingdomKey, 10));

        Assert.False(
            await cache.ExistsAsync(
                unitedKingdomKey));

        Assert.False(
            await redis.Database.KeyExistsAsync(
                $"{unitedKingdomKey}:members"));

        Assert.False(
            await redis.Database.KeyExistsAsync(
                $"{unitedKingdomKey}:revisions"));
    }

    [RedisFact]
    public async Task Daily_cache_versions_are_independent_by_country()
    {
        await using var redis =
            await RedisTestDatabase.CreateAsync();

        var cache = redis.CreateCacheService();

        var globalKey =
            redis.CreateKey("daily:2026-11-25:global");

        var turkeyKey =
            redis.CreateKey("daily:2026-11-25:TR");

        var germanyKey =
            redis.CreateKey("daily:2026-11-25:DE");

        var globalVersionKey =
            $"{globalKey}:version";

        var turkeyVersionKey =
            $"{turkeyKey}:version";

        var germanyVersionKey =
            $"{germanyKey}:version";

        Assert.Equal(
            0L,
            await cache.GetVersionAsync(
                globalVersionKey));

        Assert.Equal(
            0L,
            await cache.GetVersionAsync(
                turkeyVersionKey));

        Assert.Equal(
            0L,
            await cache.GetVersionAsync(
                germanyVersionKey));

        // Turkiye puani.
        await cache.UpdateDailyScoreAsync(
            globalKey,
            globalVersionKey,
            ClientId,
            800,
            1,
            1);

        await cache.UpdateDailyScoreAsync(
            turkeyKey,
            turkeyVersionKey,
            ClientId,
            800,
            1,
            1);

        Assert.Equal(
            1L,
            await cache.GetVersionAsync(
                globalVersionKey));

        Assert.Equal(
            1L,
            await cache.GetVersionAsync(
                turkeyVersionKey));

        Assert.Equal(
            0L,
            await cache.GetVersionAsync(
                germanyVersionKey));

        // Almanya puani.
        await cache.UpdateDailyScoreAsync(
            globalKey,
            globalVersionKey,
            ClientId,
            1450,
            2,
            2);

        await cache.UpdateDailyScoreAsync(
            germanyKey,
            germanyVersionKey,
            ClientId,
            650,
            1,
            2);

        Assert.Equal(
            2L,
            await cache.GetVersionAsync(
                globalVersionKey));

        Assert.Equal(
            1L,
            await cache.GetVersionAsync(
                turkeyVersionKey));

        Assert.Equal(
            1L,
            await cache.GetVersionAsync(
                germanyVersionKey));

        // Ulkesiz puan.
        await cache.UpdateDailyScoreAsync(
            globalKey,
            globalVersionKey,
            ClientId,
            1950,
            3,
            3);

        Assert.Equal(
            3L,
            await cache.GetVersionAsync(
                globalVersionKey));

        Assert.Equal(
            1L,
            await cache.GetVersionAsync(
                turkeyVersionKey));

        Assert.Equal(
            1L,
            await cache.GetVersionAsync(
                germanyVersionKey));

        var global = await cache.GetUserScoreAsync(
            globalKey, ClientId);

        var turkey = await cache.GetUserScoreAsync(
            turkeyKey, ClientId);

        var germany = await cache.GetUserScoreAsync(
            germanyKey, ClientId);

        Assert.NotNull(global);
        Assert.NotNull(turkey);
        Assert.NotNull(germany);

        Assert.Equal((1950, 3), global.Value);
        Assert.Equal((800, 1), turkey.Value);
        Assert.Equal((650, 1), germany.Value);
    }
}
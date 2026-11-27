using Xunit;

namespace DailyChessPuzzleApi.Tests;

[Collection("Postgres integration")]
public sealed class LeaderboardRedisRevisionTests
{
    private const string ClientId = "user:1";

    [RedisFact]
    public async Task Older_revision_cannot_overwrite_newer_score()
    {
        await using var redis =
            await RedisTestDatabase.CreateAsync();

        var cache = redis.CreateCacheService();

        var key = redis.CreateKey("daily:global");
        var versionKey = $"{key}:version";

        await cache.UpdateDailyScoreAsync(
            key, versionKey, ClientId, 800, 1, 1);

        await cache.UpdateDailyScoreAsync(
            key, versionKey, ClientId, 1500, 3, 3);

        // Gecikmis eski Outbox guncellemesi.
        await cache.UpdateDailyScoreAsync(
            key, versionKey, ClientId, 1100, 2, 2);

        var score = await cache.GetUserScoreAsync(
            key, ClientId);

        Assert.NotNull(score);
        Assert.Equal((1500m, 3), score.Value);

        Assert.Equal(
            2L,
            await cache.GetVersionAsync(versionKey));

        Assert.Equal(
            "3",
            (await redis.Database.HashGetAsync(
                $"{key}:revisions",
                ClientId)).ToString());

        Assert.Single(
            await cache.GetTopAsync(key, 10));
    }

    [RedisFact]
    public async Task Equal_revision_cannot_update_score_or_version()
    {
        await using var redis =
            await RedisTestDatabase.CreateAsync();

        var cache = redis.CreateCacheService();

        var key = redis.CreateKey("daily:global");
        var versionKey = $"{key}:version";

        await cache.UpdateDailyScoreAsync(
            key, versionKey, ClientId, 800, 1, 5);

        // Ayni revision tekrar geliyor.
        await cache.UpdateDailyScoreAsync(
            key, versionKey, ClientId, 800, 1, 5);

        // Ayni revision farkli puanla geliyor.
        await cache.UpdateDailyScoreAsync(
            key, versionKey, ClientId, 9999, 10, 5);

        var score = await cache.GetUserScoreAsync(
            key, ClientId);

        Assert.NotNull(score);
        Assert.Equal((800m, 1), score.Value);

        Assert.Equal(
            1L,
            await cache.GetVersionAsync(versionKey));

        Assert.Equal(
            "5",
            (await redis.Database.HashGetAsync(
                $"{key}:revisions",
                ClientId)).ToString());

        Assert.Single(
            await cache.GetTopAsync(key, 10));
    }

    [RedisFact]
    public async Task Revisions_are_independent_between_countries()
    {
        await using var redis =
            await RedisTestDatabase.CreateAsync();

        var cache = redis.CreateCacheService();

        var globalKey =
            redis.CreateKey("alltime:global");

        var turkeyKey =
            redis.CreateKey("alltime:TR");

        var germanyKey =
            redis.CreateKey("alltime:DE");

        await cache.UpdateScoreAsync(
            globalKey, ClientId, 1450, 2, 10);

        await cache.UpdateScoreAsync(
            turkeyKey, ClientId, 800, 1, 10);

        // DE revision degeri TR'den dusuk olsa bile
        // farkli bir cache oldugu icin kabul edilmeli.
        await cache.UpdateScoreAsync(
            germanyKey, ClientId, 650, 1, 5);

        // TR icin eski revision reddedilmeli.
        await cache.UpdateScoreAsync(
            turkeyKey, ClientId, 100, 1, 9);

        var global = await cache.GetUserScoreAsync(
            globalKey, ClientId);

        var turkey = await cache.GetUserScoreAsync(
            turkeyKey, ClientId);

        var germany = await cache.GetUserScoreAsync(
            germanyKey, ClientId);

        Assert.NotNull(global);
        Assert.NotNull(turkey);
        Assert.NotNull(germany);

        Assert.Equal((1450m, 2), global.Value);
        Assert.Equal((800m, 1), turkey.Value);
        Assert.Equal((650m, 1), germany.Value);

        Assert.Equal(
            "10",
            (await redis.Database.HashGetAsync(
                $"{turkeyKey}:revisions",
                ClientId)).ToString());

        Assert.Equal(
            "5",
            (await redis.Database.HashGetAsync(
                $"{germanyKey}:revisions",
                ClientId)).ToString());
    }

    [RedisFact]
    public async Task Cache_rebuild_preserves_newer_live_revision()
    {
        await using var redis =
            await RedisTestDatabase.CreateAsync();

        var cache = redis.CreateCacheService();

        var turkeyKey =
            redis.CreateKey("daily:TR");

        var germanyKey =
            redis.CreateKey("daily:DE");

        var turkeyVersionKey =
            $"{turkeyKey}:version";

        var germanyVersionKey =
            $"{germanyKey}:version";

        await cache.UpdateDailyScoreAsync(
            turkeyKey,
            turkeyVersionKey,
            ClientId,
            1500,
            2,
            10);

        await cache.UpdateDailyScoreAsync(
            germanyKey,
            germanyVersionKey,
            ClientId,
            650,
            1,
            8);

        // PostgreSQL'den daha eski bir snapshot geliyor.
        await cache.RebuildDailyAsync(
            turkeyKey,
            new List<(
                string ClientId,
                decimal TotalScore,
                int SolvedCount,
                long Revision)>
            {
                (ClientId, 800, 1, 5),
                ("user:2", 700, 1, 6)
            });

        var turkey = await cache.GetUserScoreAsync(
            turkeyKey, ClientId);

        var germany = await cache.GetUserScoreAsync(
            germanyKey, ClientId);

        var secondUser =
            await cache.GetUserScoreAsync(
                turkeyKey, "user:2");

        Assert.NotNull(turkey);
        Assert.NotNull(germany);
        Assert.NotNull(secondUser);

        Assert.Equal((1500m, 2), turkey.Value);
        Assert.Equal((650m, 1), germany.Value);
        Assert.Equal((700m, 1), secondUser.Value);

        Assert.Equal(
            "10",
            (await redis.Database.HashGetAsync(
                $"{turkeyKey}:revisions",
                ClientId)).ToString());

        Assert.True(
            await cache.IsDailyCacheReadyAsync(
                turkeyKey));

        Assert.False(
            await cache.IsDailyCacheReadyAsync(
                germanyKey));

        Assert.Equal(
            1L,
            await cache.GetVersionAsync(
                turkeyVersionKey));

        Assert.Equal(
            1L,
            await cache.GetVersionAsync(
                germanyVersionKey));

        Assert.Equal(
            2,
            (await cache.GetTopAsync(
                turkeyKey, 10)).Count);

        Assert.Single(
            await cache.GetTopAsync(
                germanyKey, 10));
    }

    [RedisFact]
    public async Task Removed_score_cannot_be_restored_by_old_revision()
    {
        await using var redis =
            await RedisTestDatabase.CreateAsync();

        var cache = redis.CreateCacheService();

        var key = redis.CreateKey("daily:TR");
        var versionKey = $"{key}:version";

        await cache.UpdateDailyScoreAsync(
            key, versionKey, ClientId, 800, 1, 5);

        await cache.RemoveDailyScoreAsync(
            key, versionKey, ClientId, 10);

        Assert.Null(
            await cache.GetUserScoreAsync(
                key, ClientId));

        Assert.Empty(
            await cache.GetTopAsync(key, 10));

        // Eski guncelleme kullaniciyi geri ekleyemez.
        await cache.UpdateDailyScoreAsync(
            key, versionKey, ClientId, 800, 1, 5);

        // Esit revision da reddedilmeli.
        await cache.UpdateDailyScoreAsync(
            key, versionKey, ClientId, 900, 1, 10);

        Assert.Null(
            await cache.GetUserScoreAsync(
                key, ClientId));

        Assert.Equal(
            2L,
            await cache.GetVersionAsync(versionKey));

        Assert.Equal(
            "10",
            (await redis.Database.HashGetAsync(
                $"{key}:revisions",
                ClientId)).ToString());

        Assert.False(
            await redis.Database.HashExistsAsync(
                $"{key}:members",
                ClientId));

        // Gercekten yeni revision kabul edilmeli.
        await cache.UpdateDailyScoreAsync(
            key, versionKey, ClientId, 700, 1, 11);

        var restored = await cache.GetUserScoreAsync(
            key, ClientId);

        Assert.NotNull(restored);
        Assert.Equal((700m, 1), restored.Value);

        Assert.Equal(
            3L,
            await cache.GetVersionAsync(versionKey));
    }

    [RedisFact]
    public async Task Cache_rebuild_preserves_removal_tombstone()
    {
        await using var redis =
            await RedisTestDatabase.CreateAsync();

        var cache = redis.CreateCacheService();

        var key = redis.CreateKey("daily:TR");
        var versionKey = $"{key}:version";

        await cache.UpdateDailyScoreAsync(
            key, versionKey, ClientId, 800, 1, 5);

        await cache.RemoveDailyScoreAsync(
            key, versionKey, ClientId, 10);

        // Eski PostgreSQL snapshot'i silinmis
        // kullaniciyi tekrar iceriyor.
        await cache.RebuildDailyAsync(
            key,
            new List<(
                string ClientId,
                decimal TotalScore,
                int SolvedCount,
                long Revision)>
            {
                (ClientId, 800, 1, 5),
                ("user:2", 650, 1, 6)
            });

        Assert.Null(
            await cache.GetUserScoreAsync(
                key, ClientId));

        Assert.Equal(
            "10",
            (await redis.Database.HashGetAsync(
                $"{key}:revisions",
                ClientId)).ToString());

        Assert.False(
            await redis.Database.HashExistsAsync(
                $"{key}:members",
                ClientId));

        var remainingUser = Assert.Single(
            await cache.GetTopAsync(key, 10));

        Assert.Equal(
            ("user:2", 650m, 1),
            remainingUser);

        // Rebuild sonrasinda gecikmis guncelleme.
        await cache.UpdateDailyScoreAsync(
            key, versionKey, ClientId, 900, 1, 9);

        Assert.Null(
            await cache.GetUserScoreAsync(
                key, ClientId));

        Assert.Single(
            await cache.GetTopAsync(key, 10));

        Assert.Equal(
            2L,
            await cache.GetVersionAsync(versionKey));

        Assert.True(
            await cache.IsDailyCacheReadyAsync(key));
    }

    [RedisFact]
    public async Task Custom_range_caches_preserve_country_score_isolation()
    {
        await using var redis =
            await RedisTestDatabase.CreateAsync();

        var cache = redis.CreateCacheService();

        var globalDay1 =
            redis.CreateKey("daily:day1:global");

        var globalDay2 =
            redis.CreateKey("daily:day2:global");

        var globalDay3 =
            redis.CreateKey("daily:day3:global");

        var turkeyDay1 =
            redis.CreateKey("daily:day1:TR");

        var turkeyDay2 =
            redis.CreateKey("daily:day2:TR");

        var turkeyDay3 =
            redis.CreateKey("daily:day3:TR");

        var germanyDay1 =
            redis.CreateKey("daily:day1:DE");

        var germanyDay2 =
            redis.CreateKey("daily:day2:DE");

        var germanyDay3 =
            redis.CreateKey("daily:day3:DE");

        // Birinci gun: Turkiye.
        await cache.UpdateDailyScoreAsync(
            globalDay1,
            $"{globalDay1}:version",
            ClientId, 800, 1, 1);

        await cache.UpdateDailyScoreAsync(
            turkeyDay1,
            $"{turkeyDay1}:version",
            ClientId, 800, 1, 1);

        // Ikinci gun: Almanya.
        await cache.UpdateDailyScoreAsync(
            globalDay2,
            $"{globalDay2}:version",
            ClientId, 650, 1, 2);

        await cache.UpdateDailyScoreAsync(
            germanyDay2,
            $"{germanyDay2}:version",
            ClientId, 650, 1, 2);

        // Ucuncu gun: ulkesiz.
        await cache.UpdateDailyScoreAsync(
            globalDay3,
            $"{globalDay3}:version",
            ClientId, 500, 1, 3);

        var globalRange =
            redis.CreateKey("range:global");

        var turkeyRange =
            redis.CreateKey("range:TR");

        var germanyRange =
            redis.CreateKey("range:DE");

        var cacheDuration =
            TimeSpan.FromMinutes(60);

        await cache.RebuildRangeAsync(
            globalRange,
            new[]
            {
                globalDay1,
                globalDay2,
                globalDay3
            },
            cacheDuration);

        await cache.RebuildRangeAsync(
            turkeyRange,
            new[]
            {
                turkeyDay1,
                turkeyDay2,
                turkeyDay3
            },
            cacheDuration);

        await cache.RebuildRangeAsync(
            germanyRange,
            new[]
            {
                germanyDay1,
                germanyDay2,
                germanyDay3
            },
            cacheDuration);

        var global = await cache.GetUserScoreAsync(
            globalRange, ClientId);

        var turkey = await cache.GetUserScoreAsync(
            turkeyRange, ClientId);

        var germany = await cache.GetUserScoreAsync(
            germanyRange, ClientId);

        Assert.NotNull(global);
        Assert.NotNull(turkey);
        Assert.NotNull(germany);

        Assert.Equal((1950m, 3), global.Value);
        Assert.Equal((800m, 1), turkey.Value);
        Assert.Equal((650m, 1), germany.Value);

        Assert.Equal(
            "3",
            (await redis.Database.HashGetAsync(
                $"{globalRange}:revisions",
                ClientId)).ToString());

        Assert.Equal(
            "1",
            (await redis.Database.HashGetAsync(
                $"{turkeyRange}:revisions",
                ClientId)).ToString());

        Assert.Equal(
            "2",
            (await redis.Database.HashGetAsync(
                $"{germanyRange}:revisions",
                ClientId)).ToString());

        Assert.Single(
            await cache.GetTopAsync(globalRange, 10));

        Assert.Single(
            await cache.GetTopAsync(turkeyRange, 10));

        Assert.Single(
            await cache.GetTopAsync(germanyRange, 10));

        // Range cache anahtarlarinin TTL kontrolu.
        foreach (var rangeKey in new[]
        {
            globalRange,
            turkeyRange,
            germanyRange
        })
        {
            foreach (var key in new[]
            {
                rangeKey,
                $"{rangeKey}:members",
                $"{rangeKey}:revisions"
            })
            {
                var ttl =
                    await redis.Database.KeyTimeToLiveAsync(
                        key);

                Assert.NotNull(ttl);

                Assert.InRange(
                    ttl.Value,
                    TimeSpan.FromMinutes(55),
                    cacheDuration);
            }
        }
    }
}
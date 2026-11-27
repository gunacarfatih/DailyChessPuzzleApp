
using System.Globalization;
using DailyChessPuzzleApi.Services.Interfaces;
using StackExchange.Redis;

namespace DailyChessPuzzleApi.Services
{
    public class LeaderboardCacheService : ILeaderboardCacheService
    {
        private readonly IDatabase _database;

        private const decimal ScoreScale = 10000m;

        private const decimal MaximumExactRedisInteger =
            9007199254740991m;

        // Skor, üye Hash'i, revision Hash'i ve günlük versiyon
        // tek Lua Script çalıştırmasıyla güncellenir.
        private const string UpdateScoreScript = """
            local currentRevision = redis.call('HGET', KEYS[3], ARGV[1])
            local incomingRevision = tonumber(ARGV[4])

            if currentRevision and incomingRevision <= tonumber(currentRevision) then
                return 0
            end

            local oldMember = redis.call('HGET', KEYS[2], ARGV[1])

            if oldMember then
                redis.call('ZREM', KEYS[1], oldMember)
            end

            redis.call('ZADD', KEYS[1], ARGV[3], ARGV[2])
            redis.call('HSET', KEYS[2], ARGV[1], ARGV[2])
            redis.call('HSET', KEYS[3], ARGV[1], ARGV[4])

            if #KEYS >= 4 then
                redis.call('INCR', KEYS[4])
            end

            return 1
            """;

        // Bir kullanıcıyı leaderboard'dan revision kontrolüyle
        // atomik olarak siler.
        // Revision kaydı tombstone olarak tutulur.
        private const string RemoveScoreScript = """
            local currentRevision = redis.call('HGET', KEYS[3], ARGV[1])
            local incomingRevision = tonumber(ARGV[2])

            if currentRevision and incomingRevision <= tonumber(currentRevision) then
                return 0
            end

            local oldMember = redis.call('HGET', KEYS[2], ARGV[1])

            if oldMember then
                redis.call('ZREM', KEYS[1], oldMember)
            end

            redis.call('HDEL', KEYS[2], ARGV[1])
            redis.call('HSET', KEYS[3], ARGV[1], ARGV[2])

            if #KEYS >= 4 then
                redis.call('INCR', KEYS[4])
            end

            return 1
            """;

        // Cache değiştirilirken mevcut cache'teki daha yeni
        // revision'ları korur.
        private const string ReplaceCacheScript = """
            local liveRevisions = redis.call('HGETALL', KEYS[3])

            for i = 1, #liveRevisions, 2 do
                local clientId = liveRevisions[i]
                local liveRevision = tonumber(liveRevisions[i + 1])

                local tempRevisionValue = redis.call('HGET', KEYS[6], clientId)
                local tempRevision = tempRevisionValue and tonumber(tempRevisionValue) or -1

                if liveRevision > tempRevision then
                    local liveMember = redis.call('HGET', KEYS[2], clientId)

                    if liveMember then
                        local liveScore = redis.call('ZSCORE', KEYS[1], liveMember)

                        if liveScore then
                            local oldTempMember = redis.call('HGET', KEYS[5], clientId)

                            if oldTempMember then
                                redis.call('ZREM', KEYS[4], oldTempMember)
                            end

                            redis.call('ZADD', KEYS[4], liveScore, liveMember)
                            redis.call('HSET', KEYS[5], clientId, liveMember)
                        end
                    else
                        -- Canlı revision var ancak üye yoksa
                        -- bu bir tombstone'dur.
                        local oldTempMember = redis.call('HGET', KEYS[5], clientId)

                        if oldTempMember then
                            redis.call('ZREM', KEYS[4], oldTempMember)
                            redis.call('HDEL', KEYS[5], clientId)
                        end
                    end

                    -- En yeni revision korunur.
                    redis.call('HSET', KEYS[6], clientId, liveRevision)
                end
            end

            redis.call('DEL', KEYS[1], KEYS[2], KEYS[3])

            if redis.call('EXISTS', KEYS[4]) == 1 then
                redis.call('RENAME', KEYS[4], KEYS[1])
            end

            if redis.call('EXISTS', KEYS[5]) == 1 then
                redis.call('RENAME', KEYS[5], KEYS[2])
            end

            if redis.call('EXISTS', KEYS[6]) == 1 then
                redis.call('RENAME', KEYS[6], KEYS[3])
            end

            local ttl = tonumber(ARGV[1])

            if ttl > 0 then
                redis.call('EXPIRE', KEYS[1], ttl)
                redis.call('EXPIRE', KEYS[2], ttl)
                redis.call('EXPIRE', KEYS[3], ttl)
            end

            if #KEYS >= 7 then
                redis.call('SET', KEYS[7], '1')
            end

            return 1
            """;

        public LeaderboardCacheService(IConnectionMultiplexer redis)
        {
            _database = redis.GetDatabase();
        }

        public async Task UpdateScoreAsync(
            string key,
            string clientId,
            decimal totalScore,
            int solvedCount,
            long revision)
        {
            await ExecuteScoreUpdateAsync(
                key,
                null,
                clientId,
                totalScore,
                solvedCount,
                revision);
        }

        public async Task UpdateDailyScoreAsync(
            string key,
            string versionKey,
            string clientId,
            decimal totalScore,
            int solvedCount,
            long revision)
        {
            await ExecuteScoreUpdateAsync(
                key,
                versionKey,
                clientId,
                totalScore,
                solvedCount,
                revision);
        }

        public Task RemoveScoreAsync(
            string key,
            string clientId,
            long revision)
        {
            return ExecuteScoreRemovalAsync(
                key,
                null,
                clientId,
                revision);
        }

        public Task RemoveDailyScoreAsync(
            string key,
            string versionKey,
            string clientId,
            long revision)
        {
            return ExecuteScoreRemovalAsync(
                key,
                versionKey,
                clientId,
                revision);
        }

        private async Task ExecuteScoreRemovalAsync(
            string key,
            string? versionKey,
            string clientId,
            long revision)
        {
            if (string.IsNullOrWhiteSpace(clientId) || clientId.Contains('\u001F'))
            {
                throw new ArgumentException(
                    "ClientId cannot be empty.",
                    nameof(clientId));
            }

            if (revision < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(revision));
            }

            string membersKey = GetMemberKey(key);
            string revisionsKey = GetRevisionKey(key);

            RedisKey[] keys;

            if (versionKey == null)
            {
                keys =
                [
                    key,
                    membersKey,
                    revisionsKey
                ];
            }
            else
            {
                keys =
                [
                    key,
                    membersKey,
                    revisionsKey,
                    versionKey
                ];
            }

            RedisValue[] values =
            [
                clientId,
                revision
            ];

            await _database.ScriptEvaluateAsync(
                RemoveScoreScript,
                keys,
                values);
        }

        private async Task ExecuteScoreUpdateAsync(
            string key,
            string? versionKey,
            string clientId,
            decimal totalScore,
            int solvedCount,
            long revision)
        {
            if (totalScore < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalScore));
            }

            if (revision < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(revision));
            }

            string member = CreateMember(
                clientId,
                solvedCount);

            string membersKey = GetMemberKey(key);
            string revisionsKey = GetRevisionKey(key);

            RedisKey[] keys;

            if (versionKey == null)
            {
                keys =
                [
                    key,
                    membersKey,
                    revisionsKey
                ];
            }
            else
            {
                keys =
                [
                    key,
                    membersKey,
                    revisionsKey,
                    versionKey
                ];
            }

            RedisValue[] values =
            [
                clientId,
                member,
                EncodeScore(totalScore),
                revision
            ];

            await _database.ScriptEvaluateAsync(
                UpdateScoreScript,
                keys,
                values);
        }

        public async Task<bool> IsDailyCacheReadyAsync(
            string key)
        {
            return await _database.KeyExistsAsync(
                GetDailyReadyKey(key));
        }

        public async Task RebuildDailyAsync(
            string key,
            List<(
                string ClientId,
                decimal TotalScore,
                int SolvedCount,
                long Revision)> entries)
        {
            await ReplaceCacheAsync(
                key,
                entries,
                readyKey: GetDailyReadyKey(key));
        }

        public async Task<(decimal TotalScore, int SolvedCount)?>
            GetUserScoreAsync(
                string key,
                string clientId)
        {
            string membersKey = GetMemberKey(key);

            RedisValue member = await _database.HashGetAsync(
                membersKey,
                clientId);

            if (!member.HasValue)
            {
                return null;
            }

            double? score = await _database.SortedSetScoreAsync(
                key,
                member.ToString());

            if (!score.HasValue)
            {
                return null;
            }

            var decoded = DecodeMember(
                member.ToString());

            decimal totalScore = DecodeScore(score.Value);

            return (
                totalScore,
                decoded.SolvedCount);
        }

        public async Task<
            List<(
                string ClientId,
                decimal TotalScore,
                int SolvedCount)>>
            GetTopAsync(
                string key,
                int count,
                long offset = 0)
        {
            if (count <= 0)
            {
                return [];
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset));
            }

            long end = checked(offset + count - 1L);

            var entries =
                await _database.SortedSetRangeByRankWithScoresAsync(
                    key,
                    offset,
                    end,
                    Order.Ascending);

            var result = new List<(
                string ClientId,
                decimal TotalScore,
                int SolvedCount)>();

            foreach (var entry in entries)
            {
                var decoded = DecodeMember(
                    entry.Element.ToString());

                decimal totalScore = DecodeScore(
                    entry.Score);

                result.Add((
                    decoded.ClientId,
                    totalScore,
                    decoded.SolvedCount));
            }

            return result;
        }

        // Standart yarışma sıralaması: 1, 2, 2, 4.
        // Eşit puanlı kullanıcılar aynı dereceyi alır.
        public async Task<long?> GetRankAsync(
            string key,
            string clientId)
        {
            const string rankScript = """
                local member = redis.call('HGET', KEYS[2], ARGV[1])
                if not member then return false end
                local score = redis.call('ZSCORE', KEYS[1], member)
                if not score then return false end
                return redis.call('ZCOUNT', KEYS[1], '-inf', '(' .. score) + 1
                """;

            RedisResult result = await _database.ScriptEvaluateAsync(
                rankScript,
                new RedisKey[] { key, GetMemberKey(key) },
                new RedisValue[] { clientId });

            return result.IsNull ? null : (long)result;
        }

        public async Task<bool> ExistsAsync(string key)
        {
            return await _database.KeyExistsAsync(key);
        }

        public async Task<long> GetVersionAsync(
            string versionKey)
        {
            RedisValue value =
                await _database.StringGetAsync(versionKey);

            if (!value.HasValue)
            {
                return 0;
            }

            return long.TryParse(
                value.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long version)
                    ? version
                    : 0;
        }

        public Task<long> IncrementVersionAsync(
            string versionKey)
        {
            return _database.StringIncrementAsync(
                versionKey);
        }

        // Birden fazla günlük leaderboard verisini birleştirir.
        // Her kullanıcı için en yüksek revision değeri korunur.
        // Puanlar decimal olarak toplanır.
        public async Task RebuildRangeAsync(
            string rangeKey,
            IReadOnlyCollection<string> dailyKeys,
            TimeSpan cacheDuration)
        {
            var aggregated = new Dictionary<
                string,
                (
                    decimal TotalScore,
                    int SolvedCount,
                    long Revision)>();

            foreach (string dailyKey in dailyKeys)
            {
                var dailyEntries =
                    await _database.SortedSetRangeByRankWithScoresAsync(
                        dailyKey,
                        0,
                        -1,
                        Order.Ascending);

                string dailyRevisionsKey =
                    GetRevisionKey(dailyKey);

                foreach (var entry in dailyEntries)
                {
                    var decoded = DecodeMember(
                        entry.Element.ToString());

                    decimal totalScore = DecodeScore(
                        entry.Score);

                    RedisValue revisionValue =
                        await _database.HashGetAsync(
                            dailyRevisionsKey,
                            decoded.ClientId);

                    long revision = 0;

                    if (revisionValue.HasValue)
                    {
                        long.TryParse(
                            revisionValue.ToString(),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out revision);
                    }

                    if (aggregated.TryGetValue(
                        decoded.ClientId,
                        out var existing))
                    {
                        aggregated[decoded.ClientId] = (
                            checked(
                                existing.TotalScore + totalScore),

                            checked(
                                existing.SolvedCount +
                                decoded.SolvedCount),

                            Math.Max(
                                existing.Revision,
                                revision));
                    }
                    else
                    {
                        aggregated[decoded.ClientId] = (
                            totalScore,
                            decoded.SolvedCount,
                            revision);
                    }
                }
            }

            var entries = aggregated
                .Select(x => (
                    ClientId: x.Key,
                    TotalScore: x.Value.TotalScore,
                    SolvedCount: x.Value.SolvedCount,
                    Revision: x.Value.Revision))
                .ToList();

            await ReplaceCacheAsync(
                rangeKey,
                entries,
                cacheDuration);
        }

        public async Task RebuildAsync(
            string key,
            List<(
                string ClientId,
                decimal TotalScore,
                int SolvedCount,
                long Revision)> entries)
        {
            await ReplaceCacheAsync(
                key,
                entries);
        }

        private async Task ReplaceCacheAsync(
            string key,
            List<(
                string ClientId,
                decimal TotalScore,
                int SolvedCount,
                long Revision)> entries,
            TimeSpan? cacheDuration = null,
            string? readyKey = null)
        {
            string membersKey = GetMemberKey(key);
            string revisionsKey = GetRevisionKey(key);

            string suffix = Guid.NewGuid().ToString("N");

            string tempKey =
                $"{key}:rebuild:{suffix}";

            string tempMembersKey =
                $"{membersKey}:rebuild:{suffix}";

            string tempRevisionsKey =
                $"{revisionsKey}:rebuild:{suffix}";

            try
            {
                if (entries.Count > 0)
                {
                    var sortedSetEntries =
                        new SortedSetEntry[entries.Count];

                    var hashEntries =
                        new HashEntry[entries.Count];

                    var revisionEntries =
                        new HashEntry[entries.Count];

                    for (int i = 0; i < entries.Count; i++)
                    {
                        var entry = entries[i];

                        if (entry.TotalScore < 0m)
                        {
                            throw new ArgumentOutOfRangeException(
                                nameof(entries),
                                "TotalScore cannot be negative.");
                        }

                        if (entry.Revision < 0)
                        {
                            throw new ArgumentOutOfRangeException(
                                nameof(entries),
                                "Revision cannot be negative.");
                        }

                        string member = CreateMember(
                            entry.ClientId,
                            entry.SolvedCount);

                        sortedSetEntries[i] =
                            new SortedSetEntry(
                                member,
                                EncodeScore(entry.TotalScore));

                        hashEntries[i] =
                            new HashEntry(
                                entry.ClientId,
                                member);

                        revisionEntries[i] =
                            new HashEntry(
                                entry.ClientId,
                                entry.Revision);
                    }

                    await _database.SortedSetAddAsync(
                        tempKey,
                        sortedSetEntries);

                    await _database.HashSetAsync(
                        tempMembersKey,
                        hashEntries);

                    await _database.HashSetAsync(
                        tempRevisionsKey,
                        revisionEntries);
                }

                long ttlSeconds = 0;

                if (cacheDuration.HasValue)
                {
                    if (cacheDuration.Value <= TimeSpan.Zero)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(cacheDuration),
                            "Cache duration must be greater than zero.");
                    }

                    ttlSeconds = checked(
                        (long)Math.Ceiling(
                            cacheDuration.Value.TotalSeconds));
                }

                RedisKey[] keys =
                [
                    key,
                    membersKey,
                    revisionsKey,
                    tempKey,
                    tempMembersKey,
                    tempRevisionsKey
                ];

                if (readyKey != null)
                {
                    keys =
                    [
                        key,
                        membersKey,
                        revisionsKey,
                        tempKey,
                        tempMembersKey,
                        tempRevisionsKey,
                        readyKey
                    ];
                }

                RedisValue[] values =
                [
                    ttlSeconds
                ];

                await _database.ScriptEvaluateAsync(
                    ReplaceCacheScript,
                    keys,
                    values);
            }
            catch
            {
                await _database.KeyDeleteAsync(tempKey);

                await _database.KeyDeleteAsync(
                    tempMembersKey);

                await _database.KeyDeleteAsync(
                    tempRevisionsKey);

                throw;
            }
        }

        // Decimal puanı Redis'in güvenli tam sayı aralığına
        // ölçeklendirir.
        //
        // Örnek:
        // 835.0385 -> -8350385
        private static double EncodeScore(decimal totalScore)
        {
            if (totalScore < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalScore));
            }

            if (totalScore >
                MaximumExactRedisInteger / ScoreScale)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalScore),
                    "Score exceeds Redis exact integer capacity.");
            }

            decimal scaledScore =
                totalScore * ScoreScale;

            if (scaledScore != decimal.Truncate(scaledScore))
            {
                throw new ArgumentException(
                    "Score cannot contain more than four decimal places.",
                    nameof(totalScore));
            }

            long exactScore = checked(
                (long)scaledScore);

            return -(double)exactScore;
        }

        // Redis'teki ölçeklendirilmiş puanı gerçek
        // decimal puana dönüştürür.
        //
        // Örnek:
        // -8350385 -> 835.0385
        private static decimal DecodeScore(double redisScore)
        {
            if (!double.IsFinite(redisScore) ||
                redisScore > 0d ||
                redisScore < -(double)MaximumExactRedisInteger ||
                redisScore != Math.Truncate(redisScore))
            {
                throw new FormatException(
                    "Invalid scaled leaderboard Redis score.");
            }

            long scaledScore = checked(
                (long)(-redisScore));

            return scaledScore / ScoreScale;
        }

        private static string CreateMember(
            string clientId,
            int solvedCount)
        {
            if (string.IsNullOrWhiteSpace(clientId) || clientId.Contains('\u001F'))
            {
                throw new ArgumentException(
                    "ClientId cannot be empty.",
                    nameof(clientId));
            }

            if (solvedCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(solvedCount));
            }

            return $"{clientId}\u001F{solvedCount.ToString(CultureInfo.InvariantCulture)}";
        }

        private static (string ClientId, int SolvedCount) DecodeMember(string member)
        {
            int separatorIndex = member.LastIndexOf('\u001F');
            if (separatorIndex <= 0 ||
                !int.TryParse(member[(separatorIndex + 1)..],
                    NumberStyles.None, CultureInfo.InvariantCulture, out int solvedCount))
            {
                throw new FormatException("Invalid leaderboard Redis member format.");
            }

            return (member[..separatorIndex], solvedCount);
        }

        private static string GetMemberKey(string key)
        {
            return $"{key}:members";
        }

        private static string GetRevisionKey(string key)
        {
            return $"{key}:revisions";
        }

        private static string GetDailyReadyKey(string key)
        {
            return $"{key}:ready";
        }
    }
}

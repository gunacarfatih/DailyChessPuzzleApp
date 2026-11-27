using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.DTOs;
using DailyChessPuzzleApi.Exceptions;
using DailyChessPuzzleApi.Services.Interfaces;
using DailyChessPuzzleApi.Settings;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DailyChessPuzzleApi.Services
{
    public class LeaderboardService : ILeaderboardService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILeaderboardCacheService _cacheService;
        private readonly IPublicIdService _publicIdService;
        private readonly ILogger<LeaderboardService> _logger;
        private readonly LeaderboardSettings _settings;

        private const string CachePrefix =
            "DailyChessPuzzle_leaderboard:v3";

        private const int CachePageSize = 50;

        private static readonly TimeSpan CustomRangeCacheDuration =
            TimeSpan.FromMinutes(60);

        public LeaderboardService(
            ApplicationDbContext context,
            ILeaderboardCacheService cacheService,
            IPublicIdService publicIdService,
            ILogger<LeaderboardService> logger,
            IOptions<LeaderboardSettings> options)
        {
            _context = context;
            _cacheService = cacheService;
            _publicIdService = publicIdService;
            _logger = logger;
            _settings = options.Value;
        }

        public async Task<LeaderboardResponse> GetLeaderboardAsync(
            string? country,
            int? userId,
            int limit,
            DateOnly? startDate,
            DateOnly? endDate,
            string? period,
            CancellationToken cancellationToken = default)
        {
            if (limit <= 0)
            {
                limit = _settings.DefaultLimit;
            }

            if (limit < _settings.MinLimit ||
                limit > _settings.MaxLimit)
            {
                throw new ArgumentException(
                    $"Limit must be between {_settings.MinLimit} and {_settings.MaxLimit}.");
            }

            country = ValidateCountry(country);
            string? requestedPeriod = string.IsNullOrWhiteSpace(period)
                ? null
                : period.Trim().ToLowerInvariant();

            if (requestedPeriod is not null &&
                requestedPeriod is not ("all" or "daily" or "weekly" or "monthly" or "alltime"))
            {
                throw new AppException(400, "INVALID_LEADERBOARD_PERIOD");
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            if (startDate.HasValue != endDate.HasValue)
            {
                throw new ArgumentException(
                    "StartDate and EndDate must be provided together.");
            }

            if (startDate.HasValue && endDate.HasValue)
            {
                if (startDate.Value > endDate.Value)
                {
                    throw new ArgumentException(
                        "StartDate cannot be later than EndDate.");
                }

                if (endDate.Value > today)
                {
                    throw new ArgumentException(
                        "EndDate cannot be in the future.");
                }

                if (endDate.Value.DayNumber - startDate.Value.DayNumber > 91)
                {
                    throw new AppException(400, "LEADERBOARD_RANGE_TOO_LARGE");
                }
            }

            LeaderboardPeriodDto? daily = null;
            if (requestedPeriod is null or "all" or "daily")
            {
                daily = await GetFixedPeriodAsync(
                    "daily",
                    today.ToString("yyyy-MM-dd"),
                    today,
                    today,
                    country,
                    userId,
                    limit,
                    cancellationToken);
            }

            // Haftalık leaderboard: Pazartesi başlangıçlı ISO hafta.
            int daysSinceMonday =
                ((int)today.DayOfWeek + 6) % 7;

            var weeklyStartDate = today.AddDays(-daysSinceMonday);

            int isoYear = ISOWeek.GetYear(
                today.ToDateTime(TimeOnly.MinValue));

            int isoWeek = ISOWeek.GetWeekOfYear(
                today.ToDateTime(TimeOnly.MinValue));

            string weeklyPeriodKey = $"{isoYear}-W{isoWeek:D2}";

            LeaderboardPeriodDto? weekly = null;
            if (requestedPeriod is null or "all" or "weekly")
            {
                weekly = await GetFixedPeriodAsync(
                    "weekly",
                    weeklyPeriodKey,
                    weeklyStartDate,
                    today,
                    country,
                    userId,
                    limit,
                    cancellationToken);
            }

            var monthlyStartDate = new DateOnly(
                today.Year,
                today.Month,
                1);

            LeaderboardPeriodDto? monthly = null;
            if (requestedPeriod is null or "all" or "monthly")
            {
                monthly = await GetFixedPeriodAsync(
                    "monthly",
                    today.ToString("yyyy-MM"),
                    monthlyStartDate,
                    today,
                    country,
                    userId,
                    limit,
                    cancellationToken);
            }

            LeaderboardPeriodDto? allTime = null;
            if (requestedPeriod is null or "all" or "alltime")
            {
                allTime = await GetFixedPeriodAsync(
                    "alltime",
                    "all",
                    null,
                    today,
                    country,
                    userId,
                    limit,
                    cancellationToken);
            }

            LeaderboardPeriodDto? customRange = null;

            if (startDate.HasValue && endDate.HasValue)
            {
                customRange = await GetCustomRangeAsync(
                    startDate.Value,
                    endDate.Value,
                    country,
                    userId,
                    limit,
                    cancellationToken);
            }

            _logger.LogInformation(
                "Leaderboard data retrieved. Period: {Period}, Country: {Country}, UserId: {UserId}, Limit: {Limit}",
                requestedPeriod ?? "all",
                country ?? "Global",
                userId,
                limit);

            return new LeaderboardResponse
            {
                Daily = daily ?? new LeaderboardPeriodDto(),
                Weekly = weekly ?? new LeaderboardPeriodDto(),
                Monthly = monthly ?? new LeaderboardPeriodDto(),
                AllTime = allTime ?? new LeaderboardPeriodDto(),
                CustomRange = customRange
            };
        }

        public Task<LeaderboardEntryDto?> GetUserStatsAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            return GetCurrentUserFromDatabaseAsync(
                "alltime",
                "all",
                country: null,
                userId,
                cancellationToken);
        }

        private async Task<LeaderboardPeriodDto> GetFixedPeriodAsync(
            string period,
            string periodKey,
            DateOnly? startDate,
            DateOnly? endDate,
            string? country,
            int? userId,
            int limit,
            CancellationToken cancellationToken)
        {
            string cacheKey =
                $"{CachePrefix}:{period}:{periodKey}:{country ?? "global"}";

            List<(string ClientId, decimal TotalScore, int SolvedCount)> topEntries;

            try
            {
                bool cacheExists =
                    await _cacheService.ExistsAsync(cacheKey);

                if (!cacheExists)
                {
                    await RebuildCacheAsync(
                        cacheKey,
                        period,
                        periodKey,
                        country,
                        cancellationToken);
                }

                topEntries = await GetTopFromCacheAsync(
                    cacheKey,
                    limit,
                    cancellationToken);

                if (topEntries.Count == 0)
                {
                    await RebuildCacheAsync(
                        cacheKey,
                        period,
                        periodKey,
                        country,
                        cancellationToken);

                    topEntries = await GetTopFromCacheAsync(
                        cacheKey,
                        limit,
                        cancellationToken);
                }
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Redis leaderboard read failed. Falling back to PostgreSQL. CacheKey: {CacheKey}",
                    cacheKey);

                topEntries = await GetTopFromDatabaseAsync(
                    period,
                    periodKey,
                    country,
                    limit,
                    cancellationToken);
            }

            var rankings = await CreateRankingsAsync(
                topEntries,
                cancellationToken);

            LeaderboardEntryDto? currentUser = null;

            if (userId.HasValue)
            {
                currentUser = await GetCurrentUserFromDatabaseAsync(
                    period,
                    periodKey,
                    country,
                    userId.Value,
                    cancellationToken);
            }

            return new LeaderboardPeriodDto
            {
                Period = period,
                StartDate = startDate,
                EndDate = endDate,
                Rankings = rankings,
                CurrentUser = currentUser
            };
        }

        /// <summary>
        /// Redis kayıtlarını sayfalar halinde okur.
        /// Users tablosunda bulunmayan kullanıcıları atlar.
        /// İstenen sayıda kayıtlı kullanıcı bulunana kadar okumaya devam eder.
        /// </summary>
        private async Task<List<(string ClientId, decimal TotalScore, int SolvedCount)>>
            GetTopFromCacheAsync(
                string cacheKey,
                int limit,
                CancellationToken cancellationToken)
        {
            var result =
                new List<(string ClientId, decimal TotalScore, int SolvedCount)>();

            var addedClientIds = new HashSet<string>(
                StringComparer.Ordinal);

            long offset = 0;

            int pageSize = Math.Max(CachePageSize, limit);

            while (result.Count < limit)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = await _cacheService.GetTopAsync(
                    cacheKey,
                    pageSize,
                    offset);

                if (page.Count == 0)
                {
                    break;
                }

                var userIds = page
                    .Select(x => ParseUserId(x.ClientId))
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .Distinct()
                    .ToList();

                var registeredUserIds = await _context.Users
                    .AsNoTracking()
                    .Where(x => userIds.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToHashSetAsync(cancellationToken);

                foreach (var entry in page)
                {
                    if (result.Count >= limit)
                    {
                        break;
                    }

                    int? parsedUserId = ParseUserId(entry.ClientId);

                    if (!parsedUserId.HasValue ||
                        !registeredUserIds.Contains(parsedUserId.Value))
                    {
                        continue;
                    }

                    if (!addedClientIds.Add(entry.ClientId))
                    {
                        continue;
                    }

                    result.Add(entry);
                }

                offset = checked(offset + page.Count);

                if (page.Count < pageSize)
                {
                    break;
                }
            }

            return result;
        }

        private async Task<LeaderboardPeriodDto> GetCustomRangeAsync(
            DateOnly startDate,
            DateOnly endDate,
            string? country,
            int? userId,
            int limit,
            CancellationToken cancellationToken)
        {
            List<(string ClientId, decimal TotalScore, int SolvedCount)> topEntries;

            var dailyKeys = new List<string>();
            var versionKeys = new List<string>();

            try
            {
                for (var date = startDate;
                     date <= endDate;
                     date = date.AddDays(1))
                {
                    string dailyKey =
                        $"{CachePrefix}:daily:{date:yyyy-MM-dd}:{country ?? "global"}";

                    string versionKey = $"{dailyKey}:version";

                    await EnsureDailyCacheReadyAsync(
                        dailyKey,
                        versionKey,
                        date,
                        country,
                        cancellationToken);

                    dailyKeys.Add(dailyKey);
                    versionKeys.Add(versionKey);
                }

                var versions = new List<long>();

                foreach (var versionKey in versionKeys)
                {
                    versions.Add(
                        await _cacheService.GetVersionAsync(versionKey));
                }

                string versionData = string.Join(",", versions);

                string versionFingerprint = Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(versionData)))
                    [..16];

                string rangeKey =
                    $"{CachePrefix}:custom:{startDate:yyyy-MM-dd}:{endDate:yyyy-MM-dd}:{country ?? "global"}:{versionFingerprint}";

                bool cacheExists =
                    await _cacheService.ExistsAsync(rangeKey);

                if (!cacheExists)
                {
                    await _cacheService.RebuildRangeAsync(
                        rangeKey,
                        dailyKeys,
                        CustomRangeCacheDuration);
                }

                topEntries = await GetTopFromCacheAsync(
                    rangeKey,
                    limit,
                    cancellationToken);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Custom range Redis operation failed. Falling back to PostgreSQL. StartDate: {StartDate}, EndDate: {EndDate}",
                    startDate,
                    endDate);

                topEntries = await GetCustomRangeTopFromDatabaseAsync(
                    startDate,
                    endDate,
                    country,
                    limit,
                    cancellationToken);
            }

            var rankings = await CreateRankingsAsync(
                topEntries,
                cancellationToken);

            var currentUser = userId.HasValue
                ? await GetCustomRangeCurrentUserFromDatabaseAsync(
                    startDate,
                    endDate,
                    country,
                    userId.Value,
                    cancellationToken)
                : null;

            return new LeaderboardPeriodDto
            {
                Period = "custom",
                StartDate = startDate,
                EndDate = endDate,
                Rankings = rankings,
                CurrentUser = currentUser
            };
        }

        private async Task EnsureDailyCacheReadyAsync(
            string dailyKey,
            string versionKey,
            DateOnly date,
            string? country,
            CancellationToken cancellationToken)
        {
            bool isReady =
                await _cacheService.IsDailyCacheReadyAsync(dailyKey);

            if (isReady)
            {
                return;
            }

            // Yalnızca Users tablosunda bulunan kullanıcıları cache'e al.
            var entries =
                await (
                    from score in _context.LeaderboardScores.AsNoTracking()
                    join user in _context.Users.AsNoTracking()
                        on score.UserId equals user.Id
                    where
                        score.Period == "daily" &&
                        score.PeriodKey == date.ToString("yyyy-MM-dd") &&
                        score.CountryCode == country
                    select new
                    {
                        score.ClientId,
                        score.TotalScore,
                        score.SolvedCount,
                        score.Revision
                    })
                    .ToListAsync(cancellationToken);

            var cacheEntries = entries
                .Select(x => (
                    x.ClientId,
                    x.TotalScore,
                    x.SolvedCount,
                    x.Revision))
                .ToList();

            await _cacheService.RebuildDailyAsync(
                dailyKey,
                cacheEntries);

            await _cacheService.IncrementVersionAsync(versionKey);

            _logger.LogInformation(
                "Daily leaderboard cache initialized. Date: {Date}, Country: {Country}, UserCount: {UserCount}",
                date,
                country ?? "Global",
                cacheEntries.Count);
        }

        private async Task<List<(string ClientId, decimal TotalScore, int SolvedCount)>>
            GetCustomRangeTopFromDatabaseAsync(
                DateOnly startDate,
                DateOnly endDate,
                string? country,
                int limit,
                CancellationToken cancellationToken)
        {
            // Guest kullanıcıların UserId değeri olmadığı için
            // yalnızca kayıtlı kullanıcılarla eşleşen attempt kayıtları alınır.
            var query =
                from attempt in _context.PuzzleAttempts.AsNoTracking()
                join user in _context.Users.AsNoTracking()
                    on attempt.UserId equals (int?)user.Id
                where
                    attempt.PlayedOn >= startDate &&
                    attempt.PlayedOn <= endDate
                select new
                {
                    UserId = user.Id,
                    attempt.Score,
                    attempt.CountryCode
                };

            if (country != null)
            {
                query = query.Where(x => x.CountryCode == country);
            }

            var entries = await query
                .GroupBy(x => x.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    TotalScore = group.Sum(x => x.Score),
                    SolvedCount = group.Count()
                })
                .OrderByDescending(x => x.TotalScore)
                .ThenBy(x => "user:" + x.UserId)
                .Take(limit)
                .ToListAsync(cancellationToken);

            return entries
                .Select(x => (
                    ClientId: $"user:{x.UserId}",
                    x.TotalScore,
                    x.SolvedCount))
                .ToList();
        }

        private async Task<LeaderboardEntryDto?>
            GetCustomRangeCurrentUserFromDatabaseAsync(
                DateOnly startDate,
                DateOnly endDate,
                string? country,
                int userId,
                CancellationToken cancellationToken)
        {
            var query =
                from attempt in _context.PuzzleAttempts.AsNoTracking()
                join user in _context.Users.AsNoTracking()
                    on attempt.UserId equals (int?)user.Id
                where
                    user.Id == userId &&
                    attempt.PlayedOn >= startDate &&
                    attempt.PlayedOn <= endDate
                select new
                {
                    UserId = user.Id,
                    attempt.Score,
                    attempt.CountryCode
                };

            if (country != null)
            {
                query = query.Where(x => x.CountryCode == country);
            }

            var userEntry = await query
                .GroupBy(x => x.UserId)
                .Select(group => new
                {
                    TotalScore = group.Sum(x => x.Score),
                    SolvedCount = group.Count()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (userEntry == null)
            {
                return null;
            }

            var allEntries =
                from attempt in _context.PuzzleAttempts.AsNoTracking()
                join user in _context.Users.AsNoTracking()
                    on attempt.UserId equals (int?)user.Id
                where
                    attempt.PlayedOn >= startDate &&
                    attempt.PlayedOn <= endDate
                select new
                {
                    UserId = user.Id,
                    attempt.Score,
                    attempt.CountryCode
                };

            if (country != null)
            {
                allEntries = allEntries.Where(
                    x => x.CountryCode == country);
            }

            var groupedEntries = allEntries
                .GroupBy(x => x.UserId)
                .Select(group => new
                {
                    TotalScore = group.Sum(x => x.Score),
                    SolvedCount = group.Count()
                });

            int usersAhead = await groupedEntries.CountAsync(
                x => x.TotalScore > userEntry.TotalScore,
                cancellationToken);

            return await CreateEntryAsync(
                userId,
                userEntry.TotalScore,
                userEntry.SolvedCount,
                usersAhead + 1,
                cancellationToken);
        }

        private async Task<List<LeaderboardEntryDto>> CreateRankingsAsync(
            List<(string ClientId, decimal TotalScore, int SolvedCount)> entries,
            CancellationToken cancellationToken)
        {
            var userIds = entries
                .Select(x => ParseUserId(x.ClientId))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            // Kullanıcı adlarını tek sorguda getirerek N+1 sorgu problemini önle.
            var usernames = await _context.Users
                .AsNoTracking()
                .Where(x => userIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id,
                    x.Username
                })
                .ToDictionaryAsync(
                    x => x.Id,
                    x => x.Username,
                    cancellationToken);

            var rankings = new List<LeaderboardEntryDto>();

            int currentRank = 0;
            int visiblePosition = 0;

            decimal previousScore = -1;

            foreach (var entry in entries)
            {
                int? userId = ParseUserId(entry.ClientId);

                // Geçersiz veya artık kayıtlı olmayan kullanıcıları gösterme.
                if (!userId.HasValue ||
                    !usernames.TryGetValue(userId.Value, out var username))
                {
                    continue;
                }

                visiblePosition++;

                // Eşit puana sahip kullanıcılar aynı sırayı alır.
                if (visiblePosition == 1 ||
                    entry.TotalScore != previousScore)
                {
                    currentRank = visiblePosition;
                }

                rankings.Add(new LeaderboardEntryDto
                {
                    Rank = currentRank,
                    PublicId = _publicIdService.Generate(userId.Value),
                    Username = username,
                    TotalScore = entry.TotalScore,
                    SolvedCount = entry.SolvedCount
                });

                previousScore = entry.TotalScore;
            }

            return rankings;
        }

        private async Task<LeaderboardEntryDto?> CreateEntryAsync(
            int userId,
            decimal totalScore,
            int solvedCount,
            int rank,
            CancellationToken cancellationToken)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Where(x => x.Id == userId)
                .Select(x => new
                {
                    x.Id,
                    x.Username
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                return null;
            }

            return new LeaderboardEntryDto
            {
                Rank = rank,
                PublicId = _publicIdService.Generate(user.Id),
                Username = user.Username,
                TotalScore = totalScore,
                SolvedCount = solvedCount
            };
        }

        private async Task<LeaderboardEntryDto?>
            GetCurrentUserFromDatabaseAsync(
                string period,
                string periodKey,
                string? country,
                int userId,
                CancellationToken cancellationToken)
        {
            string identity = $"user:{userId}";

            // Kullanıcının gerçekten Users tablosunda bulunmasını zorunlu tut.
            var query =
                from score in _context.LeaderboardScores.AsNoTracking()
                join user in _context.Users.AsNoTracking()
                    on score.UserId equals user.Id
                where
                    score.Period == period &&
                    score.PeriodKey == periodKey &&
                    score.CountryCode == country
                select new
                {
                    user.Id,
                    score.ClientId,
                    score.TotalScore,
                    score.SolvedCount
                };

            var userEntry = await query
                .Where(x => x.Id == userId && x.ClientId == identity)
                .Select(x => new
                {
                    x.TotalScore,
                    x.SolvedCount
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (userEntry == null)
            {
                return null;
            }

            int usersAhead = await query.CountAsync(
                x => x.TotalScore > userEntry.TotalScore,
                cancellationToken);

            return await CreateEntryAsync(
                userId,
                userEntry.TotalScore,
                userEntry.SolvedCount,
                usersAhead + 1,
                cancellationToken);
        }

        private async Task<List<(string ClientId, decimal TotalScore, int SolvedCount)>>
            GetTopFromDatabaseAsync(
                string period,
                string periodKey,
                string? country,
                int limit,
                CancellationToken cancellationToken)
        {
            // LeaderboardScores kayıtlarını gerçek kullanıcılarla eşleştir.
            var entries = await (
                from score in _context.LeaderboardScores.AsNoTracking()
                join user in _context.Users.AsNoTracking()
                    on score.UserId equals user.Id
                where
                    score.Period == period &&
                    score.PeriodKey == periodKey &&
                    score.CountryCode == country
                orderby
                    score.TotalScore descending,
                    score.ClientId
                select new
                {
                    score.ClientId,
                    score.TotalScore,
                    score.SolvedCount
                })
                .Take(limit)
                .ToListAsync(cancellationToken);

            return entries
                .Select(x => (
                    x.ClientId,
                    x.TotalScore,
                    x.SolvedCount))
                .ToList();
        }

        private async Task RebuildCacheAsync(
            string cacheKey,
            string period,
            string periodKey,
            string? country,
            CancellationToken cancellationToken)
        {
            // Cache oluşturulurken de silinmiş kullanıcıları dahil etme.
            var entries = await (
                from score in _context.LeaderboardScores.AsNoTracking()
                join user in _context.Users.AsNoTracking()
                    on score.UserId equals user.Id
                where
                    score.Period == period &&
                    score.PeriodKey == periodKey &&
                    score.CountryCode == country
                select new
                {
                    score.ClientId,
                    score.TotalScore,
                    score.SolvedCount,
                    score.Revision
                })
                .ToListAsync(cancellationToken);

            var cacheEntries = entries
                .Select(x => (
                    x.ClientId,
                    x.TotalScore,
                    x.SolvedCount,
                    x.Revision))
                .ToList();

            await _cacheService.RebuildAsync(
                cacheKey,
                cacheEntries);

            _logger.LogInformation(
                "Leaderboard Redis cache rebuilt. CacheKey: {CacheKey}, UserCount: {UserCount}",
                cacheKey,
                cacheEntries.Count);
        }

        private static int? ParseUserId(string clientId)
        {
            const string prefix = "user:";

            if (!clientId.StartsWith(
                    prefix,
                    StringComparison.Ordinal))
            {
                return null;
            }

            return int.TryParse(
                clientId[prefix.Length..],
                out int userId) && userId > 0
                    ? userId
                    : null;
        }

        private static string? ValidateCountry(string? country)
        {
            if (string.IsNullOrWhiteSpace(country))
            {
                return null;
            }

            country = country.Trim().ToUpperInvariant();

            if (country.Length != 2)
            {
                throw new ArgumentException(
                    "Country must be a valid ISO 3166-1 alpha-2 country code.");
            }

            try
            {
                _ = new RegionInfo(country);
            }
            catch (ArgumentException)
            {
                throw new ArgumentException(
                    "Country must be a valid ISO 3166-1 alpha-2 country code.");
            }

            return country;
        }
    }
}

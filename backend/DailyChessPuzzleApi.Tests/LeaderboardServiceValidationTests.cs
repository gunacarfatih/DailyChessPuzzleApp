using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.Exceptions;
using DailyChessPuzzleApi.Services;
using DailyChessPuzzleApi.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DailyChessPuzzleApi.Tests;

public sealed class LeaderboardServiceValidationTests
{
    [Fact]
    public async Task Rejects_unknown_period_with_stable_error_code()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            service.GetLeaderboardAsync(
                country: null,
                userId: null,
                limit: 10,
                startDate: null,
                endDate: null,
                period: "quarterly"));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("INVALID_LEADERBOARD_PERIOD", exception.ErrorCode);
    }

    [Fact]
    public async Task Rejects_custom_ranges_longer_than_92_days()
    {
        var service = CreateService();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            service.GetLeaderboardAsync(
                country: null,
                userId: null,
                limit: 10,
                startDate: today.AddDays(-92),
                endDate: today,
                period: "daily"));

        Assert.Equal("LEADERBOARD_RANGE_TOO_LARGE", exception.ErrorCode);
    }

    [Fact]
    public async Task Rejects_limits_above_configured_maximum()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetLeaderboardAsync(
                country: null,
                userId: null,
                limit: 101,
                startDate: null,
                endDate: null,
                period: "daily"));

        Assert.Contains("between 1 and 100", exception.Message);
    }

    private static LeaderboardService CreateService()
    {
        var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().Options);
        return new LeaderboardService(
            context,
            cacheService: null!,
            publicIdService: null!,
            logger: NullLogger<LeaderboardService>.Instance,
            options: Options.Create(new LeaderboardSettings()));
    }
}

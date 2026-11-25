using System.Text.Json;
using System.Threading.Channels;

using DailyChessPuzzleApi.Hubs;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace DailyChessPuzzleApi.Tests;

[Collection("Postgres integration")]
public sealed class LeaderboardSignalRIntegrationTests
{
    private const string PeriodKey = "2026-11-25";

    private static readonly TimeSpan ReceiveTimeout =
        TimeSpan.FromSeconds(10);

    private static readonly TimeSpan NoMessageTimeout =
        TimeSpan.FromSeconds(1);

    [PostgresRedisFact]
    public async Task SignalR_connection_should_succeed()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(
                database,
                RedisTestDatabase.ConnectionString);

        await using var connection =
            CreateConnection(factory);

        await connection.StartAsync()
            .WaitAsync(ReceiveTimeout);

        Assert.Equal(
            HubConnectionState.Connected,
            connection.State);

        Assert.False(
            string.IsNullOrWhiteSpace(
                connection.ConnectionId));

        await connection.InvokeAsync(
            "JoinLeaderboard",
            "daily",
            (string?)null)
            .WaitAsync(ReceiveTimeout);
    }

    [PostgresRedisFact]
    public async Task Global_notification_reaches_global_and_country_subscribers()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(
                database,
                RedisTestDatabase.ConnectionString);

        await using var global =
            CreateConnection(factory);

        await using var turkey =
            CreateConnection(factory);

        await using var germany =
            CreateConnection(factory);

        var globalMessages = Listen(global);
        var turkeyMessages = Listen(turkey);
        var germanyMessages = Listen(germany);

        await StartAndJoinAsync(global, "daily", null);
        await StartAndJoinAsync(turkey, "daily", "TR");
        await StartAndJoinAsync(germany, "daily", "DE");

        await PublishAsync(
            factory,
            "leaderboard:daily",
            "daily");

        await AssertNotificationAsync(
            globalMessages, "daily");

        await AssertNotificationAsync(
            turkeyMessages, "daily");

        await AssertNotificationAsync(
            germanyMessages, "daily");
    }

    [PostgresRedisFact]
    public async Task Country_notification_reaches_only_matching_country_subscribers()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(
                database,
                RedisTestDatabase.ConnectionString);

        await using var global =
            CreateConnection(factory);

        await using var turkey =
            CreateConnection(factory);

        await using var germany =
            CreateConnection(factory);

        var globalMessages = Listen(global);
        var turkeyMessages = Listen(turkey);
        var germanyMessages = Listen(germany);

        await StartAndJoinAsync(global, "daily", null);
        await StartAndJoinAsync(turkey, "daily", "tr");
        await StartAndJoinAsync(germany, "daily", "de");

        // Turkiye bildirimi.
        await PublishAsync(
            factory,
            "leaderboard:daily:TR",
            "daily");

        await AssertNotificationAsync(
            turkeyMessages, "daily");

        await Task.WhenAll(
            AssertNoNotificationAsync(globalMessages),
            AssertNoNotificationAsync(germanyMessages));

        // Almanya bildirimi.
        await PublishAsync(
            factory,
            "leaderboard:daily:DE",
            "daily");

        await AssertNotificationAsync(
            germanyMessages, "daily");

        await Task.WhenAll(
            AssertNoNotificationAsync(globalMessages),
            AssertNoNotificationAsync(turkeyMessages));
    }

    [PostgresRedisFact]
    public async Task Leaving_country_group_stops_country_and_global_notifications()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(
                database,
                RedisTestDatabase.ConnectionString);

        await using var turkey =
            CreateConnection(factory);

        await using var germany =
            CreateConnection(factory);

        var turkeyMessages = Listen(turkey);
        var germanyMessages = Listen(germany);

        await StartAndJoinAsync(turkey, "daily", "TR");
        await StartAndJoinAsync(germany, "daily", "DE");

        // Ayrilmadan once bildirim alabildigini dogrula.
        await PublishAsync(
            factory,
            "leaderboard:daily:TR",
            "daily");

        await AssertNotificationAsync(
            turkeyMessages, "daily");

        await turkey.InvokeAsync(
            "LeaveLeaderboard",
            "daily",
            "TR")
            .WaitAsync(ReceiveTimeout);

        // Turkiye grubundan ayrildi.
        await PublishAsync(
            factory,
            "leaderboard:daily:TR",
            "daily");

        await AssertNoNotificationAsync(
            turkeyMessages);

        // LeaveLeaderboard global gruptan da cikariyor.
        await PublishAsync(
            factory,
            "leaderboard:daily",
            "daily");

        await AssertNotificationAsync(
            germanyMessages, "daily");

        await AssertNoNotificationAsync(
            turkeyMessages);

        Assert.Equal(
            HubConnectionState.Connected,
            turkey.State);

        // Tekrar katildiginda bildirim alabilmeli.
        await turkey.InvokeAsync(
            "JoinLeaderboard",
            "daily",
            "TR")
            .WaitAsync(ReceiveTimeout);

        await PublishAsync(
            factory,
            "leaderboard:daily:TR",
            "daily");

        await AssertNotificationAsync(
            turkeyMessages, "daily");
    }

    [PostgresRedisFact]
    public async Task Leaderboard_period_subscriptions_are_independent()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(
                database,
                RedisTestDatabase.ConnectionString);

        await using var daily =
            CreateConnection(factory);

        await using var weekly =
            CreateConnection(factory);

        await using var monthly =
            CreateConnection(factory);

        await using var alltime =
            CreateConnection(factory);

        var dailyMessages = Listen(daily);
        var weeklyMessages = Listen(weekly);
        var monthlyMessages = Listen(monthly);
        var alltimeMessages = Listen(alltime);

        await StartAndJoinAsync(daily, "daily", "TR");
        await StartAndJoinAsync(weekly, "weekly", "TR");
        await StartAndJoinAsync(monthly, "monthly", "TR");
        await StartAndJoinAsync(alltime, "alltime", "TR");

        await PublishAsync(
            factory,
            "leaderboard:daily:TR",
            "daily");

        await AssertNotificationAsync(
            dailyMessages, "daily");

        await Task.WhenAll(
            AssertNoNotificationAsync(weeklyMessages),
            AssertNoNotificationAsync(monthlyMessages),
            AssertNoNotificationAsync(alltimeMessages));

        await PublishAsync(
            factory,
            "leaderboard:weekly:TR",
            "weekly");

        await AssertNotificationAsync(
            weeklyMessages, "weekly");

        await Task.WhenAll(
            AssertNoNotificationAsync(dailyMessages),
            AssertNoNotificationAsync(monthlyMessages),
            AssertNoNotificationAsync(alltimeMessages));
    }

    private static HubConnection CreateConnection(
        TestWebApplicationFactory factory)
    {
        var server = factory.Server;

        var hubUri = new Uri(
            server.BaseAddress,
            "/hubs/leaderboard");

        return new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                options.Transports =
                    HttpTransportType.LongPolling;

                options.HttpMessageHandlerFactory =
                    _ => server.CreateHandler();
            })
            .Build();
    }

    private static Channel<JsonElement> Listen(
        HubConnection connection)
    {
        var messages =
            Channel.CreateUnbounded<JsonElement>();

        connection.On<JsonElement>(
            "LeaderboardUpdated",
            payload =>
            {
                messages.Writer.TryWrite(
                    payload.Clone());
            });

        return messages;
    }

    private static async Task StartAndJoinAsync(
        HubConnection connection,
        string period,
        string? countryCode)
    {
        await connection.StartAsync()
            .WaitAsync(ReceiveTimeout);

        await connection.InvokeAsync(
            "JoinLeaderboard",
            period,
            countryCode)
            .WaitAsync(ReceiveTimeout);

        Assert.Equal(
            HubConnectionState.Connected,
            connection.State);
    }

    private static async Task PublishAsync(
        TestWebApplicationFactory factory,
        string groupName,
        string period)
    {
        var hubContext = factory.Services
            .GetRequiredService<
                IHubContext<LeaderboardHub>>();

        await hubContext.Clients
            .Group(groupName)
            .SendAsync(
                "LeaderboardUpdated",
                new
                {
                    period,
                    periodKey = PeriodKey
                })
            .WaitAsync(ReceiveTimeout);
    }

    private static async Task AssertNotificationAsync(
        Channel<JsonElement> messages,
        string expectedPeriod)
    {
        using var cancellation =
            new CancellationTokenSource(
                ReceiveTimeout);

        var payload = await messages.Reader
            .ReadAsync(cancellation.Token);

        Assert.Equal(
            expectedPeriod,
            payload.GetProperty("period")
                .GetString());

        Assert.Equal(
            PeriodKey,
            payload.GetProperty("periodKey")
                .GetString());
    }

    private static async Task AssertNoNotificationAsync(
        Channel<JsonElement> messages)
    {
        using var cancellation =
            new CancellationTokenSource(
                NoMessageTimeout);

        await Assert.ThrowsAnyAsync<
            OperationCanceledException>(
            async () =>
            {
                await messages.Reader.ReadAsync(
                    cancellation.Token);
            });
    }
}
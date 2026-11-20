
using Microsoft.AspNetCore.SignalR.Client;

// API adresi
var hubUrl = Environment.GetEnvironmentVariable("DAILY_CHESS_HUB_URL") ?? "http://localhost:5053/hubs/leaderboard";
var accessToken = Environment.GetEnvironmentVariable("DAILY_CHESS_ACCESS_TOKEN");

var connection = new HubConnectionBuilder()
    .WithUrl(hubUrl, options =>
    {
        options.AccessTokenProvider = () =>
            Task.FromResult(accessToken);
    })
    .WithAutomaticReconnect()
    .Build();

// LeaderboardUpdated olayını dinle.
connection.On<object>("LeaderboardUpdated", data =>
{
    Console.WriteLine();
    Console.WriteLine("=== LEADERBOARD UPDATED ===");
    Console.WriteLine(data);
    Console.WriteLine("===========================");
});

connection.Reconnecting += error =>
{
    Console.WriteLine($"SignalR yeniden bağlanıyor: {error?.Message}");
    return Task.CompletedTask;
};

connection.Reconnected += connectionId =>
{
    Console.WriteLine($"SignalR yeniden bağlandı: {connectionId}");
    return Task.CompletedTask;
};

try
{
    await connection.StartAsync();

    Console.WriteLine("SignalR bağlantısı başarılı!");
    Console.WriteLine($"Connection ID: {connection.ConnectionId}");
    Console.WriteLine("LeaderboardUpdated bildirimi bekleniyor...");
    Console.WriteLine("Çıkmak için ENTER'a bas.");

    Console.ReadLine();
}
catch (Exception ex)
{
    Console.WriteLine($"SignalR bağlantı hatası: {ex.Message}");
}
finally
{
    await connection.DisposeAsync();
}


using Microsoft.AspNetCore.SignalR.Client;

// API adresi
const string hubUrl = "http://localhost:5053/hubs/leaderboard";

// Swagger üzerinden giriş yaptığında aldığın JWT access token'ı buraya yaz.
const string accessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIyIiwiaHR0cDovL3NjaGVtYXMueG1sc29hcC5vcmcvd3MvMjAwNS8wNS9pZGVudGl0eS9jbGFpbXMvbmFtZWlkZW50aWZpZXIiOiIyIiwiaHR0cDovL3NjaGVtYXMueG1sc29hcC5vcmcvd3MvMjAwNS8wNS9pZGVudGl0eS9jbGFpbXMvbmFtZSI6InJmZyIsImVtYWlsIjoicmZnQGdtYWlsLmNvbSIsImV4cCI6MTc5NTAwNDkzNywiaXNzIjoiRGFpbHlDaGVzc1B1enpsZUFwaSIsImF1ZCI6IkRhaWx5Q2hlc3NQdXp6bGVDbGllbnQifQ.iSMVQx6Ey3_LI2tfLS_aNpcUVAZ-KzD1zGMqk4PC0zA";

var connection = new HubConnectionBuilder()
    .WithUrl(hubUrl, options =>
    {
        options.AccessTokenProvider = () =>
            Task.FromResult<string?>(accessToken);
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

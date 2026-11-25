using System.Text;

using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.Services.Interfaces;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Microsoft.IdentityModel.Tokens;

using StackExchange.Redis;

namespace DailyChessPuzzleApi.Tests;

internal sealed class TestWebApplicationFactory
    : WebApplicationFactory<Program>
{
    internal static readonly string JwtSecret =
        new string('s', 64);

    internal const string JwtIssuer =
        "DailyChessPuzzleApi.Tests";

    internal const string JwtAudience =
        "DailyChessPuzzleClient.Tests";

    private const string DisabledRedisConnectionString =
        "127.0.0.1:1,abortConnect=false,connectTimeout=500,connectRetry=0";

    private readonly string _connectionString;

    private readonly string _redisConnectionString;

    private readonly IInterceptor[] _interceptors;

    private readonly string _signalRChannelPrefix =
        $"DailyChessPuzzle.Tests:{Guid.NewGuid():N}";

    public TestWebApplicationFactory(
        PostgresTestDatabase database,
        params IInterceptor[] interceptors)
        : this(
            database,
            DisabledRedisConnectionString,
            interceptors)
    {
    }

    public TestWebApplicationFactory(
        PostgresTestDatabase database,
        string redisConnectionString,
        params IInterceptor[] interceptors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            redisConnectionString);

        _connectionString = database.ConnectionString;
        _redisConnectionString = redisConnectionString;
        _interceptors = interceptors;
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration(
            (_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] =
                            _connectionString,

                        ["ConnectionStrings:Redis"] =
                            _redisConnectionString,

                        ["JwtSettings:Secret"] =
                            JwtSecret,

                        ["JwtSettings:Issuer"] =
                            JwtIssuer,

                        ["JwtSettings:Audience"] =
                            JwtAudience,

                        ["JwtSettings:ExpirationMinutes"] =
                            "60",

                        ["PublicIdSettings:Secret"] =
                            new string('p', 64),

                        ["Logging:Directory"] =
                            string.Empty
                    });
            });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            // SignalR Redis Backplane kanallarini
            // her test sunucusu icin izole et.
            services.PostConfigure<RedisOptions>(options =>
            {
                options.Configuration.ChannelPrefix =
                    RedisChannel.Literal(
                        _signalRChannelPrefix);
            });

            // Gercek SMTP baglantisini engelle.
            services.RemoveAll<IEmailSender>();

            services.AddSingleton<IEmailSender>(
                new TestEmailSender());

            // HTTP isteklerinde olusturulan DbContext'lere
            // test interceptor'larini ekle.
            if (_interceptors.Length > 0)
            {
                services.ConfigureDbContext<ApplicationDbContext>(
                    options =>
                    {
                        options.AddInterceptors(_interceptors);
                    });
            }

            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    var parameters =
                        options.TokenValidationParameters;

                    parameters.IssuerSigningKey =
                        new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(
                                JwtSecret));

                    parameters.ValidIssuer =
                        JwtIssuer;

                    parameters.ValidAudience =
                        JwtAudience;
                });
        });
    }

    private sealed class TestEmailSender : IEmailSender
    {
        public void EnsureConfigured()
        {
        }

        public Task SendVerificationCodeAsync(
            string email,
            string code,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.CompletedTask;
        }
    }
}
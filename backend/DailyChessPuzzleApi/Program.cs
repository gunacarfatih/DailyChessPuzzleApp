
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.HealthChecks;
using DailyChessPuzzleApi.Hubs;
using DailyChessPuzzleApi.Middlewares;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Responses;
using DailyChessPuzzleApi.Services;
using DailyChessPuzzleApi.Services.Interfaces;
using DailyChessPuzzleApi.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ReusableCaching.Extensions;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// UseSerilog is the sole logging provider. Keeping the default console
// provider here can print every application log twice in development.
builder.Logging.ClearProviders();

// --------------------------------------------------
// LEADERBOARD AYARLARI
// --------------------------------------------------

builder.Services.Configure<LeaderboardSettings>(
    builder.Configuration.GetSection("LeaderboardSettings"));

// --------------------------------------------------
// PUBLIC ID AYARLARI
// --------------------------------------------------

var publicIdSettings = builder.Configuration
    .GetSection("PublicIdSettings")
    .Get<PublicIdSettings>()
    ?? throw new InvalidOperationException(
        "PublicIdSettings yapılandırması bulunamadı.");

if (string.IsNullOrWhiteSpace(publicIdSettings.Secret) ||
    Encoding.UTF8.GetByteCount(publicIdSettings.Secret) < 32)
{
    throw new InvalidOperationException(
        "PublicId Secret en az 32 byte uzunluğunda olmalıdır.");
}

builder.Services.Configure<PublicIdSettings>(
    builder.Configuration.GetSection("PublicIdSettings"));

// --------------------------------------------------
// JWT AYARLARI
// --------------------------------------------------

var jwtSettings = builder.Configuration
    .GetSection("JwtSettings")
    .Get<JwtSettings>()
    ?? throw new InvalidOperationException(
        "JwtSettings yapılandırması bulunamadı.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret) ||
    Encoding.UTF8.GetByteCount(jwtSettings.Secret) < 32)
{
    throw new InvalidOperationException(
        "JWT Secret en az 32 byte uzunluğunda olmalıdır.");
}

if (string.IsNullOrWhiteSpace(jwtSettings.Issuer) ||
    string.IsNullOrWhiteSpace(jwtSettings.Audience) ||
    jwtSettings.ExpirationMinutes <= 0)
{
    throw new InvalidOperationException(
        "JWT yapılandırmasındaki Issuer, Audience ve ExpirationMinutes değerlerini kontrol edin.");
}

builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("JwtSettings"));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,

            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,

            ValidateLifetime = true,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.Secret)),

            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // SignalR WebSocket bağlantılarında token query string üzerinden gelebilir.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken =
                    context.Request.Query["access_token"];

                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs/leaderboard"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },

            OnTokenValidated = async context =>
            {
                var userIdValue = context.Principal?
                    .FindFirst(ClaimTypes.NameIdentifier)?
                    .Value;

                var tokenVersionValue = context.Principal?
                    .FindFirst("token_version")?
                    .Value;

                if (!int.TryParse(userIdValue, out var userId) ||
                    !int.TryParse(
                        tokenVersionValue,
                        out var tokenVersion))
                {
                    context.Fail("INVALID_TOKEN_CLAIMS");
                    return;
                }

                var dbContext = context.HttpContext
                    .RequestServices
                    .GetRequiredService<ApplicationDbContext>();

                var currentTokenVersion = await dbContext.Users
                    .AsNoTracking()
                    .Where(user => user.Id == userId)
                    .Select(user => (int?)user.TokenVersion)
                    .SingleOrDefaultAsync(
                        context.HttpContext.RequestAborted);

                if (!currentTokenVersion.HasValue ||
                    currentTokenVersion.Value != tokenVersion)
                {
                    context.Fail("TOKEN_REVOKED");
                }
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            success = false,
            status = StatusCodes.Status429TooManyRequests,
            errorCode = "RATE_LIMIT_EXCEEDED",
            message = "Too many requests. Please try again later."
        }, cancellationToken);
    };

    AddIpPolicy(
    options,
    "auth-register",
    permitLimit: 5,
    window: TimeSpan.FromMinutes(1));

    AddIpPolicy(
        options,
        "auth-login",
        permitLimit: 10,
        window: TimeSpan.FromMinutes(1));

    AddIpPolicy(
        options,
        "auth-refresh",
        permitLimit: 30,
        window: TimeSpan.FromMinutes(1));

    AddIpPolicy(
        options,
        "auth-verification",
        permitLimit: 10,
        window: TimeSpan.FromMinutes(1));

    AddIpPolicy(
        options,
        "auth-google",
        permitLimit: 10,
        window: TimeSpan.FromMinutes(1));

    AddIpPolicy(
        options,
        "auth-sensitive",
        permitLimit: 10,
        window: TimeSpan.FromMinutes(1));
    options.AddPolicy("auth-account-delete", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5, Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0, AutoReplenishment = true
            }));
    AddIpPolicy(options, "puzzle-write", permitLimit: 60, window: TimeSpan.FromMinutes(1));
    AddIpPolicy(options, "puzzle-move", permitLimit: 30, window: TimeSpan.FromMinutes(1));
    AddIpPolicy(options, "puzzle-read", permitLimit: 60, window: TimeSpan.FromMinutes(1));
    AddIpPolicy(options, "leaderboard", permitLimit: 30, window: TimeSpan.FromMinutes(1));
});

static void AddIpPolicy(
    RateLimiterOptions options,
    string name,
    int permitLimit,
    TimeSpan window)
{
    options.AddPolicy(name, context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
}

// --------------------------------------------------
// AUTH SERVİSLERİ
// --------------------------------------------------

builder.Services.AddScoped<
    IPasswordHasher<User>,
    PasswordHasher<User>>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

builder.Services.AddScoped<
    IGoogleTokenValidator,
    GoogleTokenValidator>();

// --------------------------------------------------
// CACHE
// --------------------------------------------------

builder.Services.AddReusableCaching(builder.Configuration);

var redisConnectionString = builder.Configuration
    .GetConnectionString("Redis");

if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException(
        "Redis connection string bulunamadı.");
}

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisOptions = ConfigurationOptions.Parse(redisConnectionString);

    // Redis kapalı olsa bile uygulamanın açılmasına izin ver.
    redisOptions.AbortOnConnectFail = false;

    return ConnectionMultiplexer.Connect(redisOptions);
});

builder.Services.AddSingleton<
    ILeaderboardCacheService,
    LeaderboardCacheService>();

// --------------------------------------------------
// SIGNALR
// --------------------------------------------------

builder.Services
    .AddSignalR()
    .AddStackExchangeRedis(
        redisConnectionString,
        options =>
        {
            options.Configuration.ChannelPrefix =
                RedisChannel.Literal("DailyChessPuzzle");
        });

// --------------------------------------------------
// SERILOG
// --------------------------------------------------

var logDirectory = builder.Configuration["Logging:Directory"];

var logFilePath = string.IsNullOrWhiteSpace(logDirectory)
    ? null
    : Path.Combine(logDirectory, "log-.txt");

builder.Host.UseSerilog((context, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration);

    if (!string.IsNullOrWhiteSpace(logFilePath))
    {
        Directory.CreateDirectory(logDirectory!);
        configuration.WriteTo.File(
            path: logFilePath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 10,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}");
    }
});

// --------------------------------------------------
// DATABASE
// --------------------------------------------------

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

// --------------------------------------------------
// CONTROLLERS VE VALIDATION
// --------------------------------------------------

builder.Services.AddControllers();

// PostgreSQL ve Redis sağlık kontrolleri.
builder.Services.AddHealthChecks()
    .AddCheck<PostgreSqlHealthCheck>(
        name: "PostgreSQL",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "database" },
        timeout: TimeSpan.FromSeconds(5))
    .AddCheck<RedisHealthCheck>(
        name: "Redis",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "ready", "cache" },
        timeout: TimeSpan.FromSeconds(3));

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .SelectMany(x => x.Value!.Errors)
            .Select(x => x.ErrorMessage)
            .ToList();

        var response = ResponseFactory.Failure<object>(
            context.HttpContext,
            "One or more validation errors occurred.",
            StatusCodes.Status400BadRequest,
            "VALIDATION_ERROR",
            errors
        );

        return new BadRequestObjectResult(response);
    };
});

// --------------------------------------------------
// APPLICATION SERVICES
// --------------------------------------------------

builder.Services.AddScoped<IPuzzleService, PuzzleService>();

builder.Services.AddScoped<ILeaderboardService, LeaderboardService>();
builder.Services.AddSingleton<
    ILeaderboardPublisher,
    LeaderboardPublisher>();

// PublicId üretim servisi.
builder.Services.AddSingleton<IPublicIdService, PublicIdService>();

builder.Services.AddScoped<
    ILeaderboardOutboxService,
    LeaderboardOutboxService>();

builder.Services.AddScoped<IDailyPuzzleService, DailyPuzzleService>();

// --------------------------------------------------
// BACKGROUND SERVICES
// --------------------------------------------------

builder.Services.AddHostedService<DailyPuzzleBackgroundService>();

builder.Services.AddSingleton<LeaderboardOutboxSignal>();
builder.Services.AddHostedService<LeaderboardOutboxBackgroundService>();

builder.Services.AddHostedService<RefreshTokenCleanupBackgroundService>();

// --------------------------------------------------
// SWAGGER
// --------------------------------------------------

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    // JWT Bearer Authentication tanımı.
    options.AddSecurityDefinition(
        JwtBearerDefaults.AuthenticationScheme,
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT token değerini girin."
        });

    // Swagger güvenlik gereksinimi.
    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecuritySchemeReference(
                    JwtBearerDefaults.AuthenticationScheme,
                    document
                ),
                new List<string>()
            }
        });
});

builder.Services.AddSingleton<
    IValidateOptions<ScoringSettings>,
    ScoringSettingsValidator>();

builder.Services.AddOptions<ScoringSettings>()
    .Bind(builder.Configuration.GetRequiredSection(
        ScoringSettings.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IScoringService, ScoringService>();

// --------------------------------------------------
// APPLICATION PIPELINE
// --------------------------------------------------

var app = builder.Build();

if (app.Environment.IsProduction())
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await database.Database.MigrateAsync();
}

var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                       Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
};
foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
{
    if (System.Net.IPAddress.TryParse(proxy, out var address))
        forwardedHeaders.KnownProxies.Add(address);
}
app.UseForwardedHeaders(forwardedHeaders);

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

app.MapHub<LeaderboardHub>("/hubs/leaderboard");

app.Run();

public partial class Program
{
}

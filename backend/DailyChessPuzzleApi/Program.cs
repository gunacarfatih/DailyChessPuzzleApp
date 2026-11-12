
using System.Runtime.InteropServices;
using System.Text;

using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.Middlewares;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Responses;
using DailyChessPuzzleApi.Services;
using DailyChessPuzzleApi.Services.Interfaces;
using DailyChessPuzzleApi.Settings;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

using ReusableCaching.Extensions;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------
// LEADERBOARD AYARLARI
// --------------------------------------------------

builder.Services.Configure<LeaderboardSettings>(
    builder.Configuration.GetSection("LeaderboardSettings"));

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
    });

builder.Services.AddAuthorization();

// --------------------------------------------------
// AUTH SERVİSLERİ
// --------------------------------------------------

builder.Services.AddScoped<
    IPasswordHasher<User>,
    PasswordHasher<User>>();

builder.Services.AddScoped<IAuthService, AuthService>();

// --------------------------------------------------
// CACHE
// --------------------------------------------------

builder.Services.AddReusableCaching(builder.Configuration);

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisConnection = builder.Configuration
        .GetConnectionString("Redis");

    if (string.IsNullOrWhiteSpace(redisConnection))
    {
        throw new InvalidOperationException(
            "Redis connection string bulunamadı.");
    }

    var redisOptions = ConfigurationOptions.Parse(redisConnection);

    // Redis kapalı olsa bile uygulamanın açılmasına izin ver.
    redisOptions.AbortOnConnectFail = false;

    return ConnectionMultiplexer.Connect(redisOptions);
});

builder.Services.AddSingleton<
    ILeaderboardCacheService,
    LeaderboardCacheService>();

// --------------------------------------------------
// SERILOG
// --------------------------------------------------

var logDirectory = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    ? "C:/DailyChess/logs"
    : "/var/log/dailychess";

Directory.CreateDirectory(logDirectory);

var logFilePath = Path.Combine(logDirectory, "log-.txt");

builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.File(
            path: logFilePath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 10,
            outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"
        );
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

builder.Services.AddScoped<
    ILeaderboardOutboxService,
    LeaderboardOutboxService>();

builder.Services.AddScoped<IDailyPuzzleService, DailyPuzzleService>();

// --------------------------------------------------
// BACKGROUND SERVICES
// --------------------------------------------------

builder.Services.AddHostedService<DailyPuzzleBackgroundService>();

builder.Services.AddHostedService<LeaderboardOutboxBackgroundService>();

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

// --------------------------------------------------
// APPLICATION PIPELINE
// --------------------------------------------------

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace DailyChessPuzzleApi.Tests;

[CollectionDefinition(
    "Postgres integration",
    DisableParallelization = true)]
public sealed class PostgresIntegrationCollection
{
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    "DCP_TEST_POSTGRES")))
        {
            Skip = "Set DCP_TEST_POSTGRES to a disposable PostgreSQL test database to run integration tests.";
        }
    }
}

internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _schema;
    private readonly string _connectionString;

    public string ConnectionString => _connectionString;

    private PostgresTestDatabase(
        string adminConnectionString,
        string schema,
        string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        _schema = schema;
        _connectionString = connectionString;
    }

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var configuredConnection =
            Environment.GetEnvironmentVariable(
                "DCP_TEST_POSTGRES");

        if (string.IsNullOrWhiteSpace(configuredConnection))
        {
            throw new InvalidOperationException(
                "PostgresFact must skip this test when DCP_TEST_POSTGRES is unset.");
        }

        var schema = $"dcp_test_{Guid.NewGuid():N}";

        var adminBuilder =
            new NpgsqlConnectionStringBuilder(
                configuredConnection);

        await using (
            var adminConnection =
                new NpgsqlConnection(
                    adminBuilder.ConnectionString))
        {
            await adminConnection.OpenAsync();

            await using var createSchema =
                new NpgsqlCommand(
                    $"CREATE SCHEMA \"{schema}\"",
                    adminConnection);

            await createSchema.ExecuteNonQueryAsync();
        }

        var testBuilder =
            new NpgsqlConnectionStringBuilder(
                adminBuilder.ConnectionString)
            {
                SearchPath = schema
            };

        var database = new PostgresTestDatabase(
            adminBuilder.ConnectionString,
            schema,
            testBuilder.ConnectionString);

        await using var context = database.CreateContext();

        await context.Database.EnsureCreatedAsync();

        return database;
    }

    public ApplicationDbContext CreateContext()
    {
        var options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(_connectionString)
                .Options;

        return new ApplicationDbContext(options);
    }

    public async Task SeedDailyPuzzleAsync(
        PuzzleAudience audience)
    {
        await using var context = CreateContext();

        var puzzle = new Puzzle
        {
            LichessPuzzleId = $"daily-{audience}",
            Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            Moves = "e2e4 e7e5",
            Rating = 1200,
            Popularity = 100,
            Themes = "opening",
            IsActive = true
        };

        context.Puzzles.Add(puzzle);

        context.DailyPuzzles.Add(new DailyPuzzle
        {
            Puzzle = puzzle,
            PuzzleDate = DateOnly.FromDateTime(
                DateTime.UtcNow),
            Audience = audience
        });

        await context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var adminConnection =
            new NpgsqlConnection(
                _adminConnectionString);

        await adminConnection.OpenAsync();

        await using var dropSchema =
            new NpgsqlCommand(
                $"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE",
                adminConnection);

        await dropSchema.ExecuteNonQueryAsync();
    }
}
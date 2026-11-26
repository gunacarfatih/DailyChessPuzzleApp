using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DailyChessPuzzleApi.Tests;

internal enum CountryPuzzleRaceOperation
{
    DailyStart,
    DailyCompletion,
    BonusStart,
    BonusCompletion
}

internal sealed class CountryPuzzleRaceInterceptor
    : DbCommandInterceptor
{
    private readonly CountryPuzzleRaceOperation _operation;

    private readonly object _sync = new();

    private DbContext? _countryContext;
    private DbContext? _puzzleContext;

    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CountryPuzzleRaceInterceptor(
        CountryPuzzleRaceOperation operation)
    {
        _operation = operation;
    }

    public int ArrivedCount
    {
        get
        {
            lock (_sync)
            {
                return
                    (_countryContext != null ? 1 : 0) +
                    (_puzzleContext != null ? 1 : 0);
            }
        }
    }

    public override async ValueTask<
        InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;

        if (context is null)
        {
            return result;
        }

        var sql = command.CommandText;

        var isCountryLock =
            Contains(sql, "\"Users\"") &&
            Contains(sql, "FOR UPDATE");

        var isPuzzleOperation =
            !isCountryLock &&
            IsPuzzleOperation(sql);

        if (!isCountryLock && !isPuzzleOperation)
        {
            return result;
        }

        lock (_sync)
        {
            if (isCountryLock)
            {
                if (_countryContext != null)
                {
                    return result;
                }

                _countryContext = context;
            }
            else
            {
                if (_puzzleContext != null)
                {
                    return result;
                }

                _puzzleContext = context;
            }

            if (_countryContext != null &&
                _puzzleContext != null)
            {
                if (ReferenceEquals(
                    _countryContext,
                    _puzzleContext))
                {
                    throw new InvalidOperationException(
                        "Race operations must use separate DbContexts.");
                }

                _release.TrySetResult(true);
            }
        }

        await _release.Task.WaitAsync(
            TimeSpan.FromSeconds(20),
            cancellationToken);

        return result;
    }

    private bool IsPuzzleOperation(string sql)
    {
        return _operation switch
        {
            CountryPuzzleRaceOperation.DailyStart =>
                IsCountryRead(sql),

            CountryPuzzleRaceOperation.DailyCompletion =>
                Contains(sql, "\"PuzzleGameSessions\"") &&
                Contains(sql, "FOR UPDATE"),

            CountryPuzzleRaceOperation.BonusStart =>
                IsCountryRead(sql),

            CountryPuzzleRaceOperation.BonusCompletion =>
                Contains(sql, "\"BonusPuzzleAssignments\"") &&
                Contains(sql, "FOR UPDATE"),

            _ => false
        };
    }

    private static bool IsCountryRead(string sql)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(
            sql,
            @"^\s*SELECT\s+\w+\.""CountryCode""\s+FROM\s+""Users""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool Contains(
        string sql,
        string value)
    {
        return sql.Contains(
            value,
            StringComparison.OrdinalIgnoreCase);
    }
}
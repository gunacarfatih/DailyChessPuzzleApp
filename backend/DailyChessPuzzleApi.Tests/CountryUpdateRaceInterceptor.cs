using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DailyChessPuzzleApi.Tests;

internal sealed class CountryUpdateRaceInterceptor
    : DbCommandInterceptor
{
    private readonly object _sync = new();

    private readonly HashSet<DbContext> _arrivedContexts =
        new();

    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ArrivedCount
    {
        get
        {
            lock (_sync)
            {
                return _arrivedContexts.Count;
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

        var isUserLock =
            command.CommandText.Contains(
                "\"Users\"",
                StringComparison.OrdinalIgnoreCase)
            &&
            command.CommandText.Contains(
                "FOR UPDATE",
                StringComparison.OrdinalIgnoreCase);

        if (!isUserLock)
        {
            return result;
        }

        lock (_sync)
        {
            if (!_arrivedContexts.Add(context))
            {
                return result;
            }

            if (_arrivedContexts.Count == 2)
            {
                _release.TrySetResult(true);
            }
        }

        await _release.Task.WaitAsync(
            TimeSpan.FromSeconds(20),
            cancellationToken);

        return result;
    }
}
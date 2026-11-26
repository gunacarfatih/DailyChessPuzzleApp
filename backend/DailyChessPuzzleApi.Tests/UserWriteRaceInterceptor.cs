using DailyChessPuzzleApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DailyChessPuzzleApi.Tests;

internal sealed class UserWriteRaceInterceptor
    : SaveChangesInterceptor
{
    private readonly object _sync = new();

    private readonly HashSet<DbContext> _arrivedContexts = new();

    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly bool _includeGoogleLink;

    public UserWriteRaceInterceptor(
        bool includeGoogleLink = false)
    {
        _includeGoogleLink = includeGoogleLink;
    }

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

    public override async ValueTask<InterceptionResult<int>>
        SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;

        if (context == null)
        {
            return result;
        }

        var hasUserWrite = context.ChangeTracker
            .Entries<User>()
            .Any(entry =>
                entry.State == EntityState.Added ||
                (_includeGoogleLink &&
                 entry.State == EntityState.Modified &&
                 entry.Property(x => x.GoogleId).IsModified));

        if (!hasUserWrite)
        {
            return result;
        }

        lock (_sync)
        {
            if (!_arrivedContexts.Add(context))
            {
                // Retry sırasında aynı context tekrar beklemez.
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
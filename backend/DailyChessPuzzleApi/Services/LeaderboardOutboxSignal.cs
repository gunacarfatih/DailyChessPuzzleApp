namespace DailyChessPuzzleApi.Services
{
    public sealed class LeaderboardOutboxSignal
    {
        private readonly SemaphoreSlim _signal = new(0, 1);

        public void Notify()
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Bekleyen bir bildirim zaten mevcut.
            }
        }

        public async Task WaitAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(
                timeout,
                cancellationToken);
        }
    }
}
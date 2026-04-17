namespace ClaudeStatDisplay;

internal sealed class DisplayStateStore : IDisposable
{
    private readonly Lock stateLock = new();
    private readonly SemaphoreSlim signal = new(0);

    private DisplayState currentState = DisplayState.Empty;

    public void UpdateState(DisplayState state)
    {
        lock (stateLock)
        {
            currentState = state;
            if (signal.CurrentCount == 0)
            {
                signal.Release();
            }
        }
    }

    public DisplayState GetState()
    {
        lock (stateLock)
        {
            return currentState;
        }
    }

    /// <summary>
    /// 状態が更新されたら最新の <see cref="DisplayState"/> を返す。
    /// <paramref name="timeout"/> が経過した場合（ハートビート）は <see langword="null"/> を返す。
    /// </summary>
    public async Task<DisplayState?> WaitForUpdateAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!await signal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }
        lock (stateLock)
        {
            return currentState;
        }
    }

    public void Dispose() => signal.Dispose();
}

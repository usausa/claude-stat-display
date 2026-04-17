namespace ClaudeStatDisplay;

internal sealed class DisplayStateStore
{
    private readonly Lock stateLock = new();

    private DisplayState currentState = DisplayState.Empty;

    public void UpdateState(DisplayState state)
    {
        lock (stateLock)
        {
            currentState = state;
        }
    }

    public DisplayState GetState()
    {
        lock (stateLock)
        {
            return currentState;
        }
    }
}

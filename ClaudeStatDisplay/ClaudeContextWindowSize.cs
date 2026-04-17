namespace ClaudeStatDisplay;

internal static class ClaudeContextWindowSize
{
    private static readonly Dictionary<string, int> ContextWindowSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "claude-opus-4",     200_000 },
        { "claude-sonnet-4",   200_000 },
        { "claude-haiku-4",    200_000 },
        { "claude-3-7-sonnet", 200_000 },
        { "claude-3-5-sonnet", 200_000 },
        { "claude-3-5-haiku",  200_000 },
        { "claude-3-opus",     200_000 },
        { "claude-3-sonnet",   200_000 },
        { "claude-3-haiku",    200_000 }
    };

    public static int Resolve(string? model)
    {
        if (model is null)
        {
            return 0;
        }

        foreach (var (prefix, size) in ContextWindowSizes)
        {
            if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return size;
            }
        }

        return 0;
    }
}

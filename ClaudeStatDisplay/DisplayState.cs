namespace ClaudeStatDisplay;

internal sealed record UsageInfo(
    int? InputTokens,
    int? OutputTokens,
    int? CacheCreationInputTokens,
    int? CacheReadInputTokens)
{
    public static readonly UsageInfo Empty = new(null, null, null, null);
}

internal sealed record RateLimitInfo(
    string? FiveHourStatus,
    double? FiveHourUtilization,
    DateTimeOffset? FiveHourReset,
    string? SevenDayStatus,
    double? SevenDayUtilization,
    DateTimeOffset? SevenDayReset,
    string? OverageStatus,
    string? OverageDisabledReason)
{
    public static readonly RateLimitInfo Empty = new(null, null, null, null, null, null, null, null);
}

internal sealed record DisplayState(
    string? Model,
    UsageInfo Usage,
    RateLimitInfo RateLimit)
{
    public static readonly DisplayState Empty = new(null, UsageInfo.Empty, RateLimitInfo.Empty);
}

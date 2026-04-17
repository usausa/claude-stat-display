namespace ClaudeStatDisplay;

using System.Globalization;
using System.Text;
using System.Text.Json;

internal sealed class ClaudeProxyMiddleware
{
    public const string UpstreamHeadersKey = "__ClaudeProxy_UpstreamHeaders";

    private readonly RequestDelegate next;

    private readonly DisplayStateStore stateStore;

    private DisplayState lastState = DisplayState.Empty;

    public ClaudeProxyMiddleware(RequestDelegate next, DisplayStateStore stateStore)
    {
        this.next = next;
        this.stateStore = stateStore;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/v1/messages", StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Capture messages
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        var teeStream = new TeeStream(originalBody, buffer);
        context.Response.Body = teeStream;

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        await LogResponseAsync(context, buffer).ConfigureAwait(false);
    }

    private async Task LogResponseAsync(HttpContext context, MemoryStream buffer)
    {
        var contentType = context.Response.ContentType ?? string.Empty;

        buffer.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(buffer, Encoding.UTF8, leaveOpen: true);
        var bodyText = await reader.ReadToEndAsync().ConfigureAwait(false);

        var upstreamHeaders = context.Items[UpstreamHeadersKey] as Dictionary<string, string>;
        var rateLimitInfo = ParseRateLimitHeaders(upstreamHeaders);
        var (usageInfo, model) = ParseResponseBody(bodyText, contentType);

        var current = lastState;
        var merged = new DisplayState(
            model ?? current.Model,
            new UsageInfo(
                usageInfo.InputTokens ?? current.Usage.InputTokens,
                usageInfo.OutputTokens ?? current.Usage.OutputTokens,
                usageInfo.CacheCreationInputTokens ?? current.Usage.CacheCreationInputTokens,
                usageInfo.CacheReadInputTokens ?? current.Usage.CacheReadInputTokens),
            new RateLimitInfo(
                rateLimitInfo.FiveHourStatus ?? current.RateLimit.FiveHourStatus,
                rateLimitInfo.FiveHourUtilization ?? current.RateLimit.FiveHourUtilization,
                rateLimitInfo.FiveHourReset ?? current.RateLimit.FiveHourReset,
                rateLimitInfo.SevenDayStatus ?? current.RateLimit.SevenDayStatus,
                rateLimitInfo.SevenDayUtilization ?? current.RateLimit.SevenDayUtilization,
                rateLimitInfo.SevenDayReset ?? current.RateLimit.SevenDayReset,
                rateLimitInfo.OverageStatus ?? current.RateLimit.OverageStatus,
                rateLimitInfo.OverageDisabledReason ?? current.RateLimit.OverageDisabledReason));

        lastState = merged;
        stateStore.UpdateState(merged);
    }

    private static RateLimitInfo ParseRateLimitHeaders(Dictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return RateLimitInfo.Empty;
        }

        var fiveHourStatus = ParseStringHeader(headers, "anthropic-ratelimit-unified-5h-status");
        var fiveHourReset = ParseUnixTimestampHeader(headers, "anthropic-ratelimit-unified-5h-reset");
        var fiveHourUtilization = ParseDoubleHeader(headers, "anthropic-ratelimit-unified-5h-utilization");
        var sevenDayStatus = ParseStringHeader(headers, "anthropic-ratelimit-unified-7d-status");
        var sevenDayReset = ParseUnixTimestampHeader(headers, "anthropic-ratelimit-unified-7d-reset");
        var sevenDayUtilization = ParseDoubleHeader(headers, "anthropic-ratelimit-unified-7d-utilization");
        var overageStatus = ParseStringHeader(headers, "anthropic-ratelimit-unified-overage-status");
        var overageDisabledReason = ParseStringHeader(headers, "anthropic-ratelimit-unified-overage-disabled-reason");

        return new RateLimitInfo(fiveHourStatus, fiveHourUtilization, fiveHourReset, sevenDayStatus, sevenDayUtilization, sevenDayReset, overageStatus, overageDisabledReason);
    }

    private static string? ParseStringHeader(Dictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out var value) ? value : null;

    private static double? ParseDoubleHeader(Dictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static DateTimeOffset? ParseUnixTimestampHeader(Dictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out var value) && long.TryParse(value, out var result) ? DateTimeOffset.FromUnixTimeSeconds(result) : null;

    private static (UsageInfo Usage, string? Model) ParseResponseBody(string body, string contentType)
    {
        if (String.IsNullOrWhiteSpace(body))
        {
            return (UsageInfo.Empty, null);
        }

        try
        {
            if (contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                return ParseSseBody(body);
            }

            if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return ParseJsonBody(body);
            }
        }
        catch (JsonException)
        {
            // Ignore
        }
        catch (InvalidOperationException)
        {
            // Ignore
        }

        return (UsageInfo.Empty, null);
    }

    private static (UsageInfo Usage, string? Model) ParseSseBody(string body)
    {
        var inputTokens = default(int?);
        var outputTokens = default(int?);
        var cacheCreationInputTokens = default(int?);
        var cacheReadInputTokens = default(int?);
        string? model = null;

        foreach (var line in body.AsSpan().EnumerateLines())
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var json = line[6..];
            if (json is "[DONE]")
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(json.ToString());
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp))
                {
                    continue;
                }

                var eventType = typeProp.GetString();
                if (eventType == "message_start")
                {
                    if (root.TryGetProperty("message", out var message))
                    {
                        if (message.TryGetProperty("model", out var modelProp))
                        {
                            model = modelProp.GetString();
                        }

                        if (message.TryGetProperty("usage", out var usage))
                        {
                            inputTokens = GetInt(usage, "input_tokens");
                            cacheCreationInputTokens = GetInt(usage, "cache_creation_input_tokens");
                            cacheReadInputTokens = GetInt(usage, "cache_read_input_tokens");
                        }
                    }
                }
                else if (eventType == "message_delta")
                {
                    if (root.TryGetProperty("usage", out var usage))
                    {
                        outputTokens = GetInt(usage, "output_tokens");
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore invalid SSE events
            }
        }

        return (new UsageInfo(inputTokens, outputTokens, cacheCreationInputTokens, cacheReadInputTokens), model);
    }

    private static (UsageInfo Usage, string? Model) ParseJsonBody(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var model = root.TryGetProperty("model", out var modelProp) ? modelProp.GetString() : null;

        // /v1/messages response
        if (root.TryGetProperty("usage", out var usage))
        {
            return (new UsageInfo(
                GetInt(usage, "input_tokens"),
                GetInt(usage, "output_tokens"),
                GetInt(usage, "cache_creation_input_tokens"),
                GetInt(usage, "cache_read_input_tokens")), model);
        }

        // /v1/messages/count_tokens response
        if (root.TryGetProperty("input_tokens", out var inputTokensProp) && inputTokensProp.TryGetInt32(out var countTokens))
        {
            return (new UsageInfo(countTokens, null, null, null), model);
        }

        return (UsageInfo.Empty, model);
    }

    private static int GetInt(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) && prop.TryGetInt32(out var value) ? value : 0;
}

namespace ClaudeStatDisplay;

using Yarp.ReverseProxy.Transforms;

internal static class ClaudeProxyExtensions
{
    internal static IReverseProxyBuilder AddClaudeProxyTransforms(this IReverseProxyBuilder builder)
    {
        return builder.AddTransforms(ctx =>
        {
            ctx.AddResponseTransform(transform =>
            {
                if (transform.ProxyResponse is { } proxyResponse)
                {
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (key, values) in proxyResponse.Headers)
                    {
                        if (key.StartsWith("anthropic-", StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("retry-after", StringComparison.OrdinalIgnoreCase))
                        {
                            headers[key] = string.Join(", ", values);
                        }
                    }
                    transform.HttpContext.Items[ClaudeProxyMiddleware.UpstreamHeadersKey] = headers;
                }
                return ValueTask.CompletedTask;
            });
        });
    }
}

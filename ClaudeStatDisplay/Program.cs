using ClaudeStatDisplay;

using Microsoft.Extensions.Hosting.WindowsServices;

using Serilog;

using Yarp.ReverseProxy.Transforms;

//--------------------------------------------------------------------------------
// Configure builder
//--------------------------------------------------------------------------------
Directory.SetCurrentDirectory(AppContext.BaseDirectory);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default
});

// Path
builder.Configuration.SetBasePath(AppContext.BaseDirectory);

// Service
builder.Host
    .UseWindowsService()
    .UseSystemd();

// Logging
builder.Logging.ClearProviders();
builder.Services.AddSerilog(options => options.ReadFrom.Configuration(builder.Configuration));

// Dashboard
builder.Services.AddSingleton<DisplayStateStore>();
builder.Services.AddHostedService<DashboardWorker>();

// Reverse proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transformBuilderContext =>
    {
        transformBuilderContext.AddResponseTransform(transformContext =>
        {
            if (transformContext.ProxyResponse is { } proxyResponse)
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
                transformContext.HttpContext.Items[ClaudeProxyMiddleware.UpstreamHeadersKey] = headers;
            }
            return ValueTask.CompletedTask;
        });
    });

//--------------------------------------------------------------------------------
// Configure the HTTP request pipeline.
//--------------------------------------------------------------------------------
var app = builder.Build();

app.UseMiddleware<ClaudeProxyMiddleware>();
app.MapReverseProxy();

app.Run();

using System.Runtime.InteropServices;

using ClaudeStatDisplay;

using Microsoft.Extensions.Hosting.WindowsServices;

using Serilog;

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
    .AddClaudeProxyTransforms();

//--------------------------------------------------------------------------------
// Configure the HTTP request pipeline.
//--------------------------------------------------------------------------------
var app = builder.Build();

app.UseMiddleware<ClaudeProxyMiddleware>();
app.MapReverseProxy();

// Startup
var log = app.Services.GetRequiredService<ILogger<Program>>();
log.InfoServiceStart();
log.InfoServiceSettingsRuntime(RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription, RuntimeInformation.RuntimeIdentifier);
log.InfoServiceSettingsEnvironment(typeof(Program).Assembly.GetName().Version, Environment.CurrentDirectory);

app.Run();

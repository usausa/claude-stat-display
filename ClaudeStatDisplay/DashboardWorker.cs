namespace ClaudeStatDisplay;

using HidSharp;

using LcdDriver.TrofeoVision;

internal sealed class DashboardWorker : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogLcdDisplayError =
        LoggerMessage.Define(LogLevel.Warning, new EventId(0), "LCD display error.");

    private static readonly Action<ILogger, string, Exception?> LogRenderFailed =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1), "Dashboard render failed: {Error}");

    private const int RetryDelaySeconds = 5;

    private readonly DisplayStateStore imageStore;
    private readonly ILogger<DashboardWorker> logger;

    public DashboardWorker(DisplayStateStore imageStore, ILogger<DashboardWorker> logger)
    {
        this.imageStore = imageStore;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
#pragma warning disable CA1031 // ハードウェア接続リトライループのため、すべての例外を捕捉する必要がある
            try
            {
                await RunDisplayLoopAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogLcdDisplayError(logger, ex);
            }
#pragma warning restore CA1031

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RunDisplayLoopAsync(CancellationToken stoppingToken)
    {
        var hidDevice = DeviceList.Local
            .GetHidDevices(ScreenDevice.VendorId, ScreenDevice.ProductId)
            .FirstOrDefault();

        if (hidDevice is null)
        {
            return;
        }

        using var screen = new ScreenDevice(hidDevice);

        DisplayState? lastRenderedState = null;
        byte[]? currentImage = null;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var state = imageStore.GetState();
            if (state != lastRenderedState)
            {
#pragma warning disable CA1031 // 描画・書き込み失敗時は最後の画像を継続表示するため、すべての例外を捕捉する
                try
                {
                    currentImage = DashboardRenderer.Render(state);
                    lastRenderedState = state;
                }
                catch (Exception ex)
                {
                    LogRenderFailed(logger, ex.Message, null);
                }
#pragma warning restore CA1031
            }

            if (currentImage is not null)
            {
                screen.DrawJpeg(currentImage);
            }
        }
    }
}

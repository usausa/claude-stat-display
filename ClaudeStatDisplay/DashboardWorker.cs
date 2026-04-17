namespace ClaudeStatDisplay;

using HidSharp;

using LcdDriver.TrofeoVision;

internal sealed class DashboardWorker : BackgroundService
{
    private const int RetryDelaySeconds = 5;

    private readonly DisplayStateStore store;
    private readonly ILogger<DashboardWorker> log;

    public DashboardWorker(ILogger<DashboardWorker> log, DisplayStateStore store)
    {
        this.log = log;
        this.store = store;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
#pragma warning disable CA1031
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
                log.WarnLcdDisplayError(ex);
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

        var  lastRenderedState = default(DisplayState?);
        var currentImage = default(byte[]?);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var state = store.GetState();
            if (state != lastRenderedState)
            {
#pragma warning disable CA1031
                try
                {
                    currentImage = DashboardRenderer.Render(state);
                    lastRenderedState = state;
                }
                catch (Exception ex)
                {
                    log.DebugRenderFailed(ex.Message);
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

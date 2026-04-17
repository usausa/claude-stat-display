namespace ClaudeStatDisplay;

using HidSharp;

using LcdDriver.TrofeoVision;

internal sealed class DashboardWorker : BackgroundService
{
    private const int RetryDelaySeconds = 5;

    private readonly ILogger<DashboardWorker> log;

    private readonly DisplayStateStore store;

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
                log.ErrorUnknownException(ex);
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

        var lastRenderedState = DisplayState.Empty;
        var currentImage = DashboardRenderer.Render(lastRenderedState);

        screen.DrawJpeg(currentImage);

        while (!stoppingToken.IsCancellationRequested)
        {
            var state = await store.WaitForUpdateAsync(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            if ((state is not null) && (state != lastRenderedState))
            {
                currentImage = DashboardRenderer.Render(state);
                lastRenderedState = state;
            }

            screen.DrawJpeg(currentImage);
        }
    }
}

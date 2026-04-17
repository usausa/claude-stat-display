namespace ClaudeStatDisplay;

using HidSharp;

using LcdDriver.TrofeoVision;

internal sealed class DashboardWorker : BackgroundService
{
    private const int RetryDelaySeconds = 5;

    private readonly DisplayStateStore store;
    private readonly ILogger<DashboardWorker> logger;

    public DashboardWorker(ILogger<DashboardWorker> logger, DisplayStateStore store)
    {
        this.logger = logger;
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
                logger.ErrorUnknownException(ex);
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
                currentImage = DashboardRenderer.Render(state);
                lastRenderedState = state;
            }

            if (currentImage is not null)
            {
                screen.DrawJpeg(currentImage);
            }
        }
    }
}

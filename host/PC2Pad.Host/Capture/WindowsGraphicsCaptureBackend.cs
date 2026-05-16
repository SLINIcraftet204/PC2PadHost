namespace PC2Pad.Host.Capture;
public sealed class WindowsGraphicsCaptureBackend : ICaptureBackend
{
    public string Name => "Windows Graphics Capture";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Real capture backend kommt in Phase 1.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

namespace PC2Pad.Host.Capture;

public interface ICaptureBackend
{
    string Name { get; }

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

namespace PC2Pad.Host.Capture;

// Placeholder for Phase 1:
// Windows.Graphics.Capture -> D3D11-Textur -> Encoder/WebRTC.
// The MVP is currently using only TestCardMjpegStreamer to ensure that the network, client, and input work properly first.
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

namespace PC2Pad.Host.Capture;

public sealed class RuntimeStreamOptions
{
    private readonly object _lock = new();
    private DesktopStreamOptions _current;

    public RuntimeStreamOptions(IConfiguration configuration)
    {
        _current = DesktopStreamOptions.FromConfiguration(configuration);
    }

    public DesktopStreamOptions Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public DesktopStreamOptions Update(StreamOptionsUpdate update)
    {
        lock (_lock)
        {
            _current = ApplyUpdate(_current, update);
            return _current;
        }
    }

    public DesktopStreamOptions ApplyPreset(string preset)
    {
        var normalizedPreset = (preset ?? string.Empty).Trim().ToLowerInvariant();

        var update = normalizedPreset switch
        {
            "low" or "lan-low" => new StreamOptionsUpdate(
                FramesPerSecond: 30,
                JpegQuality: 62,
                MaxWidth: 854,
                DrawOverlay: false),

            "balanced" or "balance" or "default" => new StreamOptionsUpdate(
                FramesPerSecond: 30,
                JpegQuality: 78,
                MaxWidth: 1280,
                DrawOverlay: false),

            "quality" or "1080p" => new StreamOptionsUpdate(
                FramesPerSecond: 30,
                JpegQuality: 86,
                MaxWidth: 1920,
                DrawOverlay: false),

            "debug" => new StreamOptionsUpdate(
                FramesPerSecond: 30,
                JpegQuality: 78,
                MaxWidth: 1280,
                DrawOverlay: true),

            "fast" or "45fps" => new StreamOptionsUpdate(
                FramesPerSecond: 45,
                JpegQuality: 70,
                MaxWidth: 1280,
                DrawOverlay: false),

            _ => throw new ArgumentException($"Unknown stream preset '{preset}'. Valid presets: low, balanced, quality, fast, debug.", nameof(preset))
        };

        return Update(update);
    }

    private static DesktopStreamOptions ApplyUpdate(DesktopStreamOptions current, StreamOptionsUpdate update)
    {
        var mode = string.IsNullOrWhiteSpace(update.Mode) ? current.Mode : update.Mode.Trim();

        return current with
        {
            Mode = NormalizeMode(mode),
            FramesPerSecond = update.FramesPerSecond is null ? current.FramesPerSecond : Math.Clamp(update.FramesPerSecond.Value, 1, 60),
            JpegQuality = update.JpegQuality is null ? current.JpegQuality : Math.Clamp(update.JpegQuality.Value, 30, 95),
            MonitorIndex = update.MonitorIndex is null ? current.MonitorIndex : Math.Max(0, update.MonitorIndex.Value),
            MaxWidth = update.MaxWidth is null ? current.MaxWidth : Math.Clamp(update.MaxWidth.Value, 320, 3840),
            DrawCursor = update.DrawCursor ?? current.DrawCursor,
            DrawOverlay = update.DrawOverlay ?? current.DrawOverlay
        };
    }

    private static string NormalizeMode(string mode)
    {
        return mode.Equals("test", StringComparison.OrdinalIgnoreCase)
            ? "test"
            : "desktop";
    }
}

public sealed record StreamOptionsUpdate(
    string? Mode = null,
    int? FramesPerSecond = null,
    int? JpegQuality = null,
    int? MonitorIndex = null,
    int? MaxWidth = null,
    bool? DrawCursor = null,
    bool? DrawOverlay = null);

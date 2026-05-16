using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace PC2Pad.Host.Capture;

public sealed class DesktopMjpegStreamer
{
    private readonly RuntimeStreamOptions _runtimeOptions;
    private readonly ILogger<DesktopMjpegStreamer> _logger;

    public DesktopMjpegStreamer(
        RuntimeStreamOptions runtimeOptions,
        ILogger<DesktopMjpegStreamer> logger)
    {
        _runtimeOptions = runtimeOptions;
        _logger = logger;
    }

    public object GetStatus()
    {
        var options = _runtimeOptions.Current;
        var screens = GetScreens();
        var selectedIndex = Math.Clamp(options.MonitorIndex, 0, Math.Max(0, screens.Length - 1));

        return new
        {
            mode = options.Mode,
            endpoint = "/stream/live.mjpeg",
            framesPerSecond = options.FramesPerSecond,
            jpegQuality = options.JpegQuality,
            monitorIndex = options.MonitorIndex,
            selectedMonitorIndex = selectedIndex,
            maxWidth = options.MaxWidth,
            drawCursor = options.DrawCursor,
            drawOverlay = options.DrawOverlay,
            screens = screens.Select((screen, index) => new
            {
                index,
                deviceName = screen.DeviceName,
                primary = screen.Primary,
                bounds = new
                {
                    screen.Bounds.X,
                    screen.Bounds.Y,
                    screen.Bounds.Width,
                    screen.Bounds.Height
                },
                workingArea = new
                {
                    screen.WorkingArea.X,
                    screen.WorkingArea.Y,
                    screen.WorkingArea.Width,
                    screen.WorkingArea.Height
                }
            }).ToArray()
        };
    }

    public DesktopStreamOptions Update(StreamOptionsUpdate update)
    {
        return _runtimeOptions.Update(update);
    }

    public DesktopStreamOptions ApplyPreset(string preset)
    {
        return _runtimeOptions.ApplyPreset(preset);
    }

    public DesktopStreamOptions SelectNextMonitor()
    {
        var screens = GetScreens();
        var current = _runtimeOptions.Current;
        var nextIndex = screens.Length <= 1 ? 0 : (current.MonitorIndex + 1) % screens.Length;
        return _runtimeOptions.Update(new StreamOptionsUpdate(MonitorIndex: nextIndex));
    }

    public async Task WriteDesktopMjpegAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
        context.Response.ContentType = "multipart/x-mixed-replace; boundary=pc2pad";

        var frame = 0L;
        var cancellationToken = context.RequestAborted;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var options = _runtimeOptions.Current;
                var jpg = RenderDesktopJpeg(options, frame++);

                await context.Response.WriteAsync("--pc2pad\r\n", cancellationToken);
                await context.Response.WriteAsync("Content-Type: image/jpeg\r\n", cancellationToken);
                await context.Response.WriteAsync($"Content-Length: {jpg.Length}\r\n\r\n", cancellationToken);
                await context.Response.Body.WriteAsync(jpg, cancellationToken);
                await context.Response.WriteAsync("\r\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Desktop frame capture failed. Sending fallback test card frame.");
                var fallback = TestCardRenderer.RenderJpeg(frame++);
                await context.Response.WriteAsync("--pc2pad\r\n", cancellationToken);
                await context.Response.WriteAsync("Content-Type: image/jpeg\r\n", cancellationToken);
                await context.Response.WriteAsync($"Content-Length: {fallback.Length}\r\n\r\n", cancellationToken);
                await context.Response.Body.WriteAsync(fallback, cancellationToken);
                await context.Response.WriteAsync("\r\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }

            var currentOptions = _runtimeOptions.Current;
            var delayMs = Math.Max(1, 1000 / Math.Clamp(currentOptions.FramesPerSecond, 1, 60));
            await Task.Delay(delayMs, cancellationToken);
        }
    }

    private static byte[] RenderDesktopJpeg(DesktopStreamOptions options, long frame)
    {
        var screen = SelectScreen(options.MonitorIndex);
        var sourceBounds = screen.Bounds;
        var targetSize = CalculateTargetSize(sourceBounds.Size, options.MaxWidth);

        using var sourceBitmap = new Bitmap(sourceBounds.Width, sourceBounds.Height, PixelFormat.Format32bppArgb);
        using (var sourceGraphics = Graphics.FromImage(sourceBitmap))
        {
            sourceGraphics.CopyFromScreen(sourceBounds.Left, sourceBounds.Top, 0, 0, sourceBounds.Size, CopyPixelOperation.SourceCopy);

            if (options.DrawCursor)
            {
                DrawCursor(sourceGraphics, sourceBounds);
            }
        }

        using var outputBitmap = targetSize == sourceBounds.Size
            ? (Bitmap)sourceBitmap.Clone()
            : new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format24bppRgb);

        if (targetSize != sourceBounds.Size)
        {
            using var outputGraphics = Graphics.FromImage(outputBitmap);
            outputGraphics.CompositingQuality = CompositingQuality.HighSpeed;
            outputGraphics.InterpolationMode = InterpolationMode.Bilinear;
            outputGraphics.SmoothingMode = SmoothingMode.None;
            outputGraphics.PixelOffsetMode = PixelOffsetMode.Half;
            outputGraphics.DrawImage(sourceBitmap, 0, 0, targetSize.Width, targetSize.Height);
        }

        DrawOverlay(outputBitmap, frame, screen, options);

        using var ms = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        using var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(options.JpegQuality, 30, 95));
        outputBitmap.Save(ms, codec, encoderParameters);
        return ms.ToArray();
    }

    private static void DrawCursor(Graphics graphics, Rectangle sourceBounds)
    {
        try
        {
            var cursorPosition = Cursor.Position;
            if (!sourceBounds.Contains(cursorPosition))
            {
                return;
            }

            var localPoint = new Point(cursorPosition.X - sourceBounds.Left, cursorPosition.Y - sourceBounds.Top);
            Cursors.Default.Draw(graphics, new Rectangle(localPoint, Cursors.Default.Size));
        }
        catch
        {
            // Cursor drawing is optional. Ignore failures so streaming keeps running.
        }
    }

    private static void DrawOverlay(Bitmap bitmap, long frame, Screen screen, DesktopStreamOptions options)
    {
        if (!options.DrawOverlay)
        {
            return;
        }

        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Consolas", 12, FontStyle.Regular);
        using var background = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
        using var foreground = new SolidBrush(Color.White);

        var text = $"PC2Pad Desktop MJPEG | frame {frame} | {bitmap.Width}x{bitmap.Height} | {DateTimeOffset.Now:HH:mm:ss.fff}";
        var size = graphics.MeasureString(text, font);
        graphics.FillRectangle(background, 8, 8, size.Width + 14, size.Height + 10);
        graphics.DrawString(text, font, foreground, 15, 13);

        var monitorText = $"Monitor: {screen.DeviceName}";
        var monitorSize = graphics.MeasureString(monitorText, font);
        graphics.FillRectangle(background, 8, bitmap.Height - monitorSize.Height - 18, monitorSize.Width + 14, monitorSize.Height + 10);
        graphics.DrawString(monitorText, font, foreground, 15, bitmap.Height - monitorSize.Height - 13);
    }

    private static Size CalculateTargetSize(Size sourceSize, int maxWidth)
    {
        if (maxWidth <= 0 || sourceSize.Width <= maxWidth)
        {
            return sourceSize;
        }

        var ratio = maxWidth / (double)sourceSize.Width;
        return new Size(maxWidth, Math.Max(1, (int)Math.Round(sourceSize.Height * ratio)));
    }

    private static Screen SelectScreen(int monitorIndex)
    {
        var screens = GetScreens();
        if (screens.Length == 0)
        {
            throw new InvalidOperationException("No Windows screens found.");
        }

        var index = Math.Clamp(monitorIndex, 0, screens.Length - 1);
        return screens[index];
    }

    private static Screen[] GetScreens()
    {
        return Screen.AllScreens.Length > 0 ? Screen.AllScreens : [Screen.PrimaryScreen!];
    }
}

public sealed record DesktopStreamOptions
{
    public string Mode { get; init; } = "desktop";
    public int FramesPerSecond { get; init; } = 30;
    public int JpegQuality { get; init; } = 78;
    public int MonitorIndex { get; init; } = 0;
    public int MaxWidth { get; init; } = 1280;
    public bool DrawCursor { get; init; } = true;
    public bool DrawOverlay { get; init; } = true;

    public static DesktopStreamOptions FromConfiguration(IConfiguration configuration)
    {
        return new DesktopStreamOptions
        {
            Mode = configuration.GetValue<string>("PC2Pad:Stream:Mode") ?? "desktop",
            FramesPerSecond = Math.Clamp(configuration.GetValue<int?>("PC2Pad:Stream:FramesPerSecond") ?? 30, 1, 60),
            JpegQuality = Math.Clamp(configuration.GetValue<int?>("PC2Pad:Stream:JpegQuality") ?? 78, 30, 95),
            MonitorIndex = Math.Max(0, configuration.GetValue<int?>("PC2Pad:Stream:MonitorIndex") ?? 0),
            MaxWidth = configuration.GetValue<int?>("PC2Pad:Stream:MaxWidth") ?? 1280,
            DrawCursor = configuration.GetValue<bool?>("PC2Pad:Stream:DrawCursor") ?? true,
            DrawOverlay = configuration.GetValue<bool?>("PC2Pad:Stream:DrawOverlay") ?? true
        };
    }
}

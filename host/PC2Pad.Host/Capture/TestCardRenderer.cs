using System.Drawing;
using System.Drawing.Imaging;

namespace PC2Pad.Host.Capture;

public static class TestCardRenderer
{
    public static byte[] RenderJpeg(long frame, int width = 1280, int height = 720)
    {
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.Clear(Color.FromArgb(18, 18, 24));

        using var titleFont = new Font("Segoe UI", 48, FontStyle.Bold);
        using var textFont = new Font("Segoe UI", 24, FontStyle.Regular);
        using var smallFont = new Font("Consolas", 18, FontStyle.Regular);
        using var brush = new SolidBrush(Color.White);
        using var dimBrush = new SolidBrush(Color.FromArgb(180, 180, 190));
        using var accentBrush = new SolidBrush(Color.FromArgb(80, 160, 255));
        using var pen = new Pen(Color.FromArgb(80, 160, 255), 4);

        var pulse = (int)((Math.Sin(frame / 12.0) + 1.0) * 80);
        using var pulseBrush = new SolidBrush(Color.FromArgb(80 + pulse, 80, 160, 255));

        graphics.FillEllipse(pulseBrush, width - 190, 70, 120, 120);
        graphics.DrawRectangle(pen, 40, 40, width - 80, height - 80);

        graphics.DrawString("PC2Pad", titleFont, brush, 80, 90);
        graphics.DrawString("Test stream - not yet a real game capture", textFont, dimBrush, 85, 170);
        graphics.DrawString($"Frame: {frame}", smallFont, accentBrush, 90, 265);
        graphics.DrawString($"Serverzeit: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}", smallFont, accentBrush, 90, 305);
        graphics.DrawString("If you see this on your phone, the host, LAN, and stream display are working.", textFont, brush, 85, 400);

        using var ms = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        using var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, 82L);
        bitmap.Save(ms, codec, encoderParameters);
        return ms.ToArray();
    }
}

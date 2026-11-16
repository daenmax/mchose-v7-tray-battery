using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MchoseBattery;

internal static class BatteryIconRenderer
{
    // Render at 32px and let Windows downscale. The percentage is drawn large
    // enough that it is still readable once the shell shrinks it to 16px.
    private const int Size = 32;

    public static Icon Create(int? percent, bool charging)
    {
        using var bitmap = new Bitmap(Size, Size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        Color background = percent switch
        {
            null => Color.FromArgb(120, 126, 138),
            <= 15 => Color.FromArgb(226, 76, 64),
            <= 35 => Color.FromArgb(232, 158, 48),
            _ => charging ? Color.FromArgb(56, 150, 235) : Color.FromArgb(56, 176, 106)
        };

        using (var path = RoundedRect(new Rectangle(1, 1, Size - 2, Size - 2), 8))
        using (var brush = new SolidBrush(background))
        {
            g.FillPath(brush, path);
        }

        string text = percent?.ToString() ?? "--";
        float fontSize = text.Length >= 3 ? 14f : 19f;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        using var textBrush = new SolidBrush(Color.White);
        using var shadowBrush = new SolidBrush(Color.FromArgb(70, 0, 0, 0));

        var rect = new RectangleF(0, 1, Size, Size - 1);
        g.DrawString(text, font, shadowBrush, new RectangleF(rect.X + 0.8f, rect.Y + 0.8f, rect.Width, rect.Height), format);
        g.DrawString(text, font, textBrush, rect, format);

        IntPtr handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    public static void DumpPreview(string path, int percent)
    {
        using var icon = Create(percent, false);
        using var bmp = icon.ToBitmap();
        using var scaled = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(bmp, 0, 0, 16, 16);
        }
        using var sheet = new Bitmap(96, 48);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(243, 243, 243));
            g.DrawImage(bmp, 4, 8, 32, 32);
            g.DrawImage(scaled, 44, 16, 16, 16);
            using var scaled32 = new Bitmap(32, 32);
            using (var g2 = Graphics.FromImage(scaled32))
            {
                g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g2.DrawImage(bmp, 0, 0, 24, 24);
            }
            g.DrawImage(scaled32, 66, 12, 24, 24);
        }
        sheet.Save(path);
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        var arc = new Rectangle(rect.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

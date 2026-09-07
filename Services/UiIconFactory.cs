using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DomainAdminConsole.Services;

internal enum UiIconKind
{
    Application,
    Info,
    Refresh,
    Connect,
    FolderOpen,
    Delete,
    Stop,
    Warning,
    Search,
    Rdp,
    Computer,
    Users,
    Settings,
    Printer,
    Key,
    Star,
    World,
    Download,
    Play,
    Network,
    Edit,
    Add,
    Script,
    Log,
    Shield,
    Power,
    Up,
    Back
}

/// <summary>
/// Small vector-like UI icons drawn with GDI+. They do not depend on shell icon
/// indices, icon extraction or DPI-sensitive HICON conversion, so ToolStrip and
/// Button images remain stable on Windows 10/11 and at non-100% scaling.
/// </summary>
internal static class UiIconFactory
{
    public static Image Get(UiIconKind kind, int size = 16)
    {
        size = Math.Max(12, size);
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        bmp.SetResolution(96, 96);

        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        var s = size / 16f;
        var dark = Color.FromArgb(45, 74, 105);
        var blue = Color.FromArgb(0, 120, 215);
        var green = Color.FromArgb(20, 145, 70);
        var red = Color.FromArgb(200, 45, 45);
        var orange = Color.FromArgb(215, 130, 20);
        var yellow = Color.FromArgb(232, 178, 25);

        using var penDark = Pen(dark, 1.45f * s);
        using var penBlue = Pen(blue, 1.55f * s);
        using var penGreen = Pen(green, 1.55f * s);
        using var penRed = Pen(red, 1.55f * s);
        using var penOrange = Pen(orange, 1.55f * s);
        using var brushBlue = new SolidBrush(blue);
        using var brushGreen = new SolidBrush(green);
        using var brushRed = new SolidBrush(red);
        using var brushOrange = new SolidBrush(orange);
        using var brushYellow = new SolidBrush(yellow);
        using var brushLight = new SolidBrush(Color.FromArgb(235, 244, 252));

        switch (kind)
        {
            case UiIconKind.Info:
                g.DrawEllipse(penBlue, R(2.2f, 2.2f, 11.6f, 11.6f, s));
                g.FillEllipse(brushBlue, R(7.15f, 4.2f, 1.7f, 1.7f, s));
                g.DrawLine(penBlue, P(8f, 7.1f, s), P(8f, 11.4f, s));
                break;

            case UiIconKind.Refresh:
                g.DrawArc(penBlue, R(2.4f, 2.8f, 10.6f, 10.0f, s), 35, 270);
                g.DrawLine(penBlue, P(11.2f, 2.9f, s), P(13.4f, 3.1f, s));
                g.DrawLine(penBlue, P(13.4f, 3.1f, s), P(13.0f, 5.4f, s));
                break;

            case UiIconKind.Connect:
                DrawMonitor(g, penDark, brushLight, s);
                g.DrawLine(penGreen, P(8.6f, 8f, s), P(14.1f, 8f, s));
                g.DrawLine(penGreen, P(11.9f, 5.9f, s), P(14.1f, 8f, s));
                g.DrawLine(penGreen, P(11.9f, 10.1f, s), P(14.1f, 8f, s));
                break;

            case UiIconKind.FolderOpen:
                using (var folder = new SolidBrush(Color.FromArgb(244, 190, 70)))
                {
                    var pts = new[]
                    {
                        P(1.4f, 5.0f, s), P(5.8f, 5.0f, s), P(7.1f, 3.6f, s),
                        P(14.1f, 3.6f, s), P(14.1f, 12.7f, s), P(1.4f, 12.7f, s)
                    };
                    g.FillPolygon(folder, pts);
                    g.DrawPolygon(penOrange, pts);
                    g.DrawLine(penOrange, P(1.7f, 7.1f, s), P(13.8f, 7.1f, s));
                }
                break;

            case UiIconKind.Delete:
                g.DrawLine(penRed, P(4f, 4f, s), P(12f, 12f, s));
                g.DrawLine(penRed, P(12f, 4f, s), P(4f, 12f, s));
                break;

            case UiIconKind.Stop:
                g.FillRectangle(brushRed, R(4f, 4f, 8f, 8f, s));
                break;

            case UiIconKind.Warning:
                var tri = new[] { P(8f, 1.6f, s), P(14.2f, 13.4f, s), P(1.8f, 13.4f, s) };
                g.FillPolygon(brushYellow, tri);
                g.DrawPolygon(penOrange, tri);
                g.DrawLine(penDark, P(8f, 5f, s), P(8f, 9.2f, s));
                using (var dotBrush = new SolidBrush(dark))
                    g.FillEllipse(dotBrush, R(7.25f, 10.6f, 1.5f, 1.5f, s));
                break;

            case UiIconKind.Search:
                g.DrawEllipse(penBlue, R(2.2f, 2.2f, 7.7f, 7.7f, s));
                g.DrawLine(penBlue, P(8.4f, 8.4f, s), P(13.4f, 13.4f, s));
                break;

            case UiIconKind.Rdp:
            case UiIconKind.Computer:
                DrawMonitor(g, penDark, brushLight, s);
                if (kind == UiIconKind.Rdp)
                {
                    g.FillEllipse(brushGreen, R(10.6f, 9.6f, 4.1f, 4.1f, s));
                    using var p = Pen(Color.White, 1.2f * s);
                    g.DrawLine(p, P(11.5f, 11.7f, s), P(13.7f, 11.7f, s));
                    g.DrawLine(p, P(12.9f, 10.9f, s), P(13.7f, 11.7f, s));
                    g.DrawLine(p, P(12.9f, 12.5f, s), P(13.7f, 11.7f, s));
                }
                break;

            case UiIconKind.Users:
                g.FillEllipse(brushBlue, R(3.0f, 2.5f, 4.2f, 4.2f, s));
                g.FillEllipse(brushGreen, R(9.1f, 3.4f, 3.4f, 3.4f, s));
                FillPie(g, brushBlue, R(1.8f, 7.0f, 7.0f, 6.3f, s), 180, 180);
                FillPie(g, brushGreen, R(8.1f, 7.5f, 5.8f, 5.3f, s), 180, 180);
                break;

            case UiIconKind.Settings:
                g.DrawEllipse(penDark, R(4.3f, 4.3f, 7.4f, 7.4f, s));
                g.DrawEllipse(penBlue, R(6.4f, 6.4f, 3.2f, 3.2f, s));
                for (var i = 0; i < 8; i++)
                {
                    var a = Math.PI * i / 4.0;
                    var p1 = new PointF((float)((8 + Math.Cos(a) * 5.2) * s), (float)((8 + Math.Sin(a) * 5.2) * s));
                    var p2 = new PointF((float)((8 + Math.Cos(a) * 6.8) * s), (float)((8 + Math.Sin(a) * 6.8) * s));
                    g.DrawLine(penDark, p1, p2);
                }
                break;

            case UiIconKind.Printer:
                g.FillRectangle(brushLight, R(4f, 1.8f, 8f, 4.5f, s));
                DrawRect(g, penDark, R(4f, 1.8f, 8f, 4.5f, s));
                using (var printerBrush = new SolidBrush(Color.FromArgb(220, 230, 238)))
                    g.FillRectangle(printerBrush, R(2.0f, 5.6f, 12f, 6f, s));
                DrawRect(g, penDark, R(2.0f, 5.6f, 12f, 6f, s));
                g.FillRectangle(brushLight, R(4.2f, 9.2f, 7.6f, 4.4f, s));
                DrawRect(g, penDark, R(4.2f, 9.2f, 7.6f, 4.4f, s));
                break;

            case UiIconKind.Key:
                g.DrawEllipse(penOrange, R(1.7f, 4.0f, 6.2f, 6.2f, s));
                g.DrawLine(penOrange, P(7.0f, 8.2f, s), P(14.0f, 8.2f, s));
                g.DrawLine(penOrange, P(11.0f, 8.2f, s), P(11.0f, 10.5f, s));
                g.DrawLine(penOrange, P(13.0f, 8.2f, s), P(13.0f, 9.7f, s));
                break;

            case UiIconKind.Star:
                var star = new PointF[10];
                for (var i = 0; i < 10; i++)
                {
                    var radius = (i % 2 == 0 ? 6.2 : 2.8) * s;
                    var angle = -Math.PI / 2 + i * Math.PI / 5;
                    star[i] = new PointF((float)(8 * s + Math.Cos(angle) * radius), (float)(8 * s + Math.Sin(angle) * radius));
                }
                g.FillPolygon(brushYellow, star);
                g.DrawPolygon(penOrange, star);
                break;

            case UiIconKind.World:
                g.DrawEllipse(penBlue, R(2f, 2f, 12f, 12f, s));
                g.DrawEllipse(penBlue, R(5f, 2f, 6f, 12f, s));
                g.DrawLine(penBlue, P(2.5f, 8f, s), P(13.5f, 8f, s));
                break;

            case UiIconKind.Download:
                g.DrawLine(penGreen, P(8f, 2.2f, s), P(8f, 10.6f, s));
                g.DrawLine(penGreen, P(4.6f, 7.4f, s), P(8f, 10.8f, s));
                g.DrawLine(penGreen, P(11.4f, 7.4f, s), P(8f, 10.8f, s));
                g.DrawLine(penDark, P(3f, 13.2f, s), P(13f, 13.2f, s));
                break;

            case UiIconKind.Play:
                g.FillPolygon(brushGreen, new[] { P(4f, 2.5f, s), P(13f, 8f, s), P(4f, 13.5f, s) });
                break;

            case UiIconKind.Network:
                g.FillEllipse(brushBlue, R(1.5f, 6.3f, 3.2f, 3.2f, s));
                g.FillEllipse(brushBlue, R(11.3f, 2.0f, 3.2f, 3.2f, s));
                g.FillEllipse(brushGreen, R(11.3f, 10.8f, 3.2f, 3.2f, s));
                g.DrawLine(penDark, P(4.4f, 7.5f, s), P(11.5f, 3.7f, s));
                g.DrawLine(penDark, P(4.4f, 8.5f, s), P(11.5f, 12.1f, s));
                break;

            case UiIconKind.Edit:
                g.DrawLine(penBlue, P(3f, 12.5f, s), P(11.5f, 4f, s));
                g.DrawLine(penBlue, P(4.5f, 14f, s), P(13f, 5.5f, s));
                g.DrawLine(penDark, P(2.5f, 13f, s), P(2f, 14.3f, s));
                break;

            case UiIconKind.Add:
                g.DrawLine(penGreen, P(8f, 2.5f, s), P(8f, 13.5f, s));
                g.DrawLine(penGreen, P(2.5f, 8f, s), P(13.5f, 8f, s));
                break;

            case UiIconKind.Script:
                g.FillRectangle(brushLight, R(3f, 1.8f, 10f, 12.4f, s));
                DrawRect(g, penDark, R(3f, 1.8f, 10f, 12.4f, s));
                g.DrawLine(penBlue, P(5f, 6f, s), P(7f, 8f, s));
                g.DrawLine(penBlue, P(7f, 8f, s), P(5f, 10f, s));
                g.DrawLine(penBlue, P(8.5f, 10f, s), P(11f, 10f, s));
                break;

            case UiIconKind.Log:
                g.FillRectangle(brushLight, R(2.5f, 2f, 11f, 12f, s));
                DrawRect(g, penDark, R(2.5f, 2f, 11f, 12f, s));
                g.DrawLine(penBlue, P(5f, 5f, s), P(11.3f, 5f, s));
                g.DrawLine(penBlue, P(5f, 8f, s), P(11.3f, 8f, s));
                g.DrawLine(penBlue, P(5f, 11f, s), P(11.3f, 11f, s));
                break;

            case UiIconKind.Shield:
                var shield = new[] { P(8f, 1.5f, s), P(13f, 3.4f, s), P(12.5f, 9.3f, s), P(8f, 14.2f, s), P(3.5f, 9.3f, s), P(3f, 3.4f, s) };
                g.FillPolygon(brushBlue, shield);
                break;

            case UiIconKind.Power:
                g.DrawArc(penRed, R(2.7f, 3f, 10.6f, 10.6f, s), -48, 276);
                g.DrawLine(penRed, P(8f, 1.5f, s), P(8f, 7.2f, s));
                break;

            case UiIconKind.Up:
                g.DrawLine(penBlue, P(8f, 13.5f, s), P(8f, 3.0f, s));
                g.DrawLine(penBlue, P(8f, 3.0f, s), P(4.2f, 6.8f, s));
                g.DrawLine(penBlue, P(8f, 3.0f, s), P(11.8f, 6.8f, s));
                break;

            case UiIconKind.Back:
                g.DrawLine(penBlue, P(13.2f, 8f, s), P(3.2f, 8f, s));
                g.DrawLine(penBlue, P(3.2f, 8f, s), P(7.0f, 4.2f, s));
                g.DrawLine(penBlue, P(3.2f, 8f, s), P(7.0f, 11.8f, s));
                break;

            default:
                g.FillRectangle(brushLight, R(2f, 2f, 12f, 12f, s));
                DrawRect(g, penDark, R(2f, 2f, 12f, 12f, s));
                DrawRect(g, penBlue, R(4f, 5f, 8f, 6f, s));
                break;
        }

        return bmp;
    }

    private static Pen Pen(Color color, float width)
        => new(color, Math.Max(1f, width)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    private static RectangleF R(float x, float y, float w, float h, float s)
        => new(x * s, y * s, w * s, h * s);

    private static void DrawRect(Graphics g, Pen pen, RectangleF r)
        => g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);

    private static void FillPie(Graphics g, Brush brush, RectangleF r, float startAngle, float sweepAngle)
        => g.FillPie(brush, r.X, r.Y, r.Width, r.Height, startAngle, sweepAngle);

    private static PointF P(float x, float y, float s)
        => new(x * s, y * s);

    private static void DrawMonitor(Graphics g, Pen pen, Brush fill, float s)
    {
        g.FillRectangle(fill, R(1.5f, 2.2f, 11f, 8.4f, s));
        DrawRect(g, pen, R(1.5f, 2.2f, 11f, 8.4f, s));
        g.DrawLine(pen, P(7f, 10.7f, s), P(7f, 13.1f, s));
        g.DrawLine(pen, P(4.5f, 13.2f, s), P(9.5f, 13.2f, s));
    }
}

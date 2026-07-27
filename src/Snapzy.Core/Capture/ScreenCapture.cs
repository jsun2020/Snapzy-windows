using System.Drawing;
using System.Runtime.InteropServices;

namespace Snapzy.Core.Capture;

public static class ScreenCapture
{
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT val, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    private const uint PW_RENDERFULLCONTENT = 2;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public static Bitmap CaptureRect(Rectangle r)
    {
        var bmp = new Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static Rectangle GetFrameBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var fr, Marshal.SizeOf<RECT>()) == 0)
            return Rectangle.FromLTRB(fr.Left, fr.Top, fr.Right, fr.Bottom);
        GetWindowRect(hwnd, out var wr);
        return Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
    }

    public static Bitmap CaptureWindow(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var wr);
        var winRect = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
        var frame = GetFrameBounds(hwnd);
        if (winRect.Width <= 0 || winRect.Height <= 0) return CaptureRect(frame);

        using var full = new Bitmap(winRect.Width, winRect.Height);
        bool ok;
        using (var g = Graphics.FromImage(full))
        {
            var hdc = g.GetHdc();
            ok = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);
        }
        if (!ok) return CaptureRect(frame);

        // Crop PrintWindow output (window-rect space) to the visible extended frame.
        var crop = new Rectangle(frame.X - winRect.X, frame.Y - winRect.Y, frame.Width, frame.Height);
        crop.Intersect(new Rectangle(0, 0, full.Width, full.Height));
        if (crop.Width <= 0 || crop.Height <= 0) return CaptureRect(frame);
        return full.Clone(crop, full.PixelFormat);
    }
}

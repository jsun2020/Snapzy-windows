using System.Drawing;
using System.Runtime.InteropServices;
using Snapzy.Core.Capture;

public class CaptureTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
        int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);

    private const int WS_OVERLAPPED = 0x00000000;
    private const int WS_VISIBLE = 0x10000000;

    [Fact]
    public void CaptureRect_ReturnsBitmapOfRequestedSize()
    {
        using var bmp = ScreenCapture.CaptureRect(new Rectangle(0, 0, 32, 16));
        Assert.Equal(32, bmp.Width);
        Assert.Equal(16, bmp.Height);
    }

    [Fact]
    public void GetTopLevelWindows_FindsOwnTestWindow()
    {
        // The desktop may legitimately have zero titled app windows (all minimized or
        // cloaked), so create our own and enumerate with includeOwnProcess.
        var hwnd = CreateWindowExW(0, "STATIC", "SnapzyEnumProbe", WS_OVERLAPPED | WS_VISIBLE,
            10, 10, 200, 100, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, hwnd);
        try
        {
            var wins = WindowEnumerator.GetTopLevelWindows(includeOwnProcess: true);
            Assert.Contains(wins, w => w.Title == "SnapzyEnumProbe");
            Assert.All(wins, w => Assert.True(w.Bounds.Width > 0 && w.Bounds.Height > 0));
            Assert.DoesNotContain(WindowEnumerator.GetTopLevelWindows(), w => w.Title == "SnapzyEnumProbe");
        }
        finally { DestroyWindow(hwnd); }
    }

    [Fact]
    public void ImageSaver_Png_WritesValidFile()
    {
        var dir = Directory.CreateTempSubdirectory("snapzy-img").FullName;
        try
        {
            using var bmp = new Bitmap(10, 10);
            var path = Path.Combine(dir, "t.png");
            ImageSaver.Save(bmp, path, "png", ffmpegExe: "");
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(0x89, bytes[0]); Assert.Equal(0x50, bytes[1]); // PNG magic
        }
        finally { Directory.Delete(dir, true); }
    }
}

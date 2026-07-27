using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Snapzy.Core.Capture;

public class WindowInfo
{
    public IntPtr Hwnd { get; set; }
    public string Title { get; set; } = "";
    public Rectangle Bounds { get; set; }
}

public static class WindowEnumerator
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int val, int size);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int DWMWA_CLOAKED = 14;

    public static List<WindowInfo> GetTopLevelWindows(bool includeOwnProcess = false)
    {
        var result = new List<WindowInfo>();
        var ownPid = (uint)Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
            if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (!includeOwnProcess && pid == ownPid) return true;
            var len = GetWindowTextLength(hwnd);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            var bounds = ScreenCapture.GetFrameBounds(hwnd);
            if (bounds.Width <= 0 || bounds.Height <= 0) return true;
            result.Add(new WindowInfo { Hwnd = hwnd, Title = sb.ToString(), Bounds = bounds });
            return true;
        }, IntPtr.Zero);
        return result; // EnumWindows yields top-most first
    }
}

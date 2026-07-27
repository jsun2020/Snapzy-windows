using System.Drawing;
using System.Runtime.InteropServices;

namespace Snapzy.Core.Capture;

public class ScrollCaptureResult
{
    public Bitmap? Image { get; set; }
    public int Steps { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Captures a window's full scrollable content by posting wheel messages and
/// stitching client-area captures. Requires the target to honor posted
/// WM_MOUSEWHEEL (most browsers/editors/lists do). Starts from the current
/// scroll position. Runs synchronously - call from a background thread.
/// </summary>
public static class ScrollCapture
{
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out ScreenCapture.RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr RealChildWindowFromPoint(IntPtr parent, POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private const uint WM_MOUSEWHEEL = 0x020A;
    private const int WheelNotches = 3;
    private const int MaxStitchedHeight = 20000;
    private const int PollMs = 250;
    private const int SettleTimeoutMs = 1600; // message delivery + repaint can lag (EDR hooks, slow apps)

    public static ScrollCaptureResult Run(IntPtr hwnd, Action<int>? onStep = null,
        Func<bool>? isCancelled = null, int maxSteps = 60)
    {
        var result = new ScrollCaptureResult();
        try
        {
            SetForegroundWindow(hwnd);
            Thread.Sleep(250);

            if (!GetClientRect(hwnd, out var cr) || cr.Right - cr.Left < 8 || cr.Bottom - cr.Top < 8)
            {
                result.Error = "window has no usable client area";
                return result;
            }
            var origin = new POINT { X = cr.Left, Y = cr.Top };
            ClientToScreen(hwnd, ref origin);
            var clientRect = new Rectangle(origin.X, origin.Y, cr.Right - cr.Left, cr.Bottom - cr.Top);
            var centerX = clientRect.X + clientRect.Width / 2;
            var centerY = clientRect.Y + clientRect.Height / 2;

            // Wheel messages are handled by the child control under the point
            // (e.g. an Edit control), not necessarily the top-level window.
            var centerClient = new POINT { X = (cr.Right - cr.Left) / 2, Y = (cr.Bottom - cr.Top) / 2 };
            var wheelTarget = RealChildWindowFromPoint(hwnd, centerClient);
            if (wheelTarget == IntPtr.Zero) wheelTarget = hwnd;

            Bitmap accumulated = ScreenCapture.CaptureRect(clientRect);
            var prev = (Bitmap)accumulated.Clone();
            var furnitureTrimmed = false;
            for (var step = 1; step <= maxSteps; step++)
            {
                onStep?.Invoke(step);
                if (isCancelled?.Invoke() == true) break;

                var wParam = (IntPtr)unchecked((((-120 * WheelNotches) & 0xFFFF) << 16));
                var lParam = (IntPtr)((centerY << 16) | (centerX & 0xFFFF));
                PostMessage(wheelTarget, WM_MOUSEWHEEL, wParam, lParam);

                // Poll until the content actually moves - message delivery and
                // repaint latency vary between apps.
                Bitmap current;
                (int NewContentOffset, int StaticBottomRows)? match;
                var waited = 0;
                while (true)
                {
                    Thread.Sleep(PollMs);
                    waited += PollMs;
                    current = ScreenCapture.CaptureRect(clientRect);
                    match = ImageStitcher.FindOverlap(prev, current);
                    var moved = match.HasValue &&
                        match.Value.NewContentOffset < current.Height - match.Value.StaticBottomRows;
                    if (moved || waited >= SettleTimeoutMs || isCancelled?.Invoke() == true) break;
                    current.Dispose();
                }
                if (match is null)
                {
                    Log.Info("Scroll capture: stitch lost, stopping with partial result");
                    current.Dispose();
                    break;
                }
                var (offset, furniture) = match.Value;
                if (offset >= current.Height - furniture)
                {
                    current.Dispose();
                    break; // page did not move - reached the end
                }
                if (!furnitureTrimmed && furniture > 0)
                {
                    // Drop the static bottom band (scrollbar/padding) from the first frame.
                    var cropped = ImageStitcher.CropBottom(accumulated, furniture);
                    accumulated.Dispose();
                    accumulated = cropped;
                }
                furnitureTrimmed = true;
                var grown = ImageStitcher.AppendNewRows(accumulated, current, offset, furniture);
                accumulated.Dispose();
                prev.Dispose();
                accumulated = grown;
                prev = current;
                result.Steps = step;
                if (accumulated.Height > MaxStitchedHeight) break;
            }
            prev.Dispose();
            result.Image = accumulated;
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("Scroll capture failed", ex);
            result.Error = ex.Message;
            return result;
        }
    }
}

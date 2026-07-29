using System.Drawing;
using System.Runtime.InteropServices;

namespace Snapzy.Core.Capture;

public class ScrollCaptureResult
{
    public Bitmap? Image { get; set; }
    public int Steps { get; set; }
    public string? Error { get; set; }
    /// <summary>Scroll mechanism that moved the page, or null if none did.</summary>
    public string? Strategy { get; set; }
}

/// <summary>
/// Captures a window's full scrollable content by injecting scroll-downs and
/// stitching client-area captures. Escalates through scroll mechanisms
/// (posted wheel, WM_VSCROLL, PageDown key, SendInput wheel) until one moves
/// the page, since no single mechanism works for every app. Starts from the
/// current scroll position. Runs synchronously - call from a background thread.
/// </summary>
public static class ScrollCapture
{
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out ScreenCapture.RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr RealChildWindowFromPoint(IntPtr parent, POINT point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, char[] buffer, int maxCount);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    private const int SM_CXVSCROLL = 2;
    private const uint GA_ROOT = 2;

    /// <summary>
    /// SetForegroundWindow silently fails without foreground rights, and a
    /// background target ignores (or never receives) wheel messages. Fall back
    /// to the AttachThreadInput dance, then the Alt-key unlock (an injected
    /// keypress grants the caller foreground rights on stricter Win11 builds).
    /// </summary>
    public static bool ForceForeground(IntPtr hwnd)
    {
        SetForegroundWindow(hwnd);
        Thread.Sleep(120);
        if (GetForegroundWindow() == hwnd) return true;
        var fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var myThread = GetCurrentThreadId();
        AttachThreadInput(myThread, fgThread, true);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        AttachThreadInput(myThread, fgThread, false);
        if (GetForegroundWindow() == hwnd) return true;
        SendKey(VK_MENU); // Alt press+release unlocks SetForegroundWindow
        SetForegroundWindow(hwnd);
        Thread.Sleep(120);
        return GetForegroundWindow() == hwnd;
    }

    /// <summary>Window class name, for diagnostics logging.</summary>
    public static string WindowClass(IntPtr hwnd) => ClassOf(hwnd);

    private static string ClassOf(IntPtr hwnd)
    {
        var buf = new char[256];
        var n = GetClassName(hwnd, buf, buf.Length);
        return n > 0 ? new string(buf, 0, n) : "?";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
        public uint Pad0, Pad1; // pad union to MOUSEINPUT size
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public MOUSEINPUT Mouse;
        [FieldOffset(8)] public KEYBDINPUT Keyboard;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_MENU = 0x12;

    private static void SendKey(ushort vk)
    {
        var inputs = new[]
        {
            new INPUT { Type = INPUT_KEYBOARD, Keyboard = new KEYBDINPUT { Vk = vk } },
            new INPUT { Type = INPUT_KEYBOARD, Keyboard = new KEYBDINPUT { Vk = vk, Flags = KEYEVENTF_KEYUP } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private const uint WM_MOUSEWHEEL = 0x020A;
    private const uint WM_VSCROLL = 0x0115;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const int SB_LINEDOWN = 1;
    private const int VK_DOWN = 0x28;
    private const int WheelNotches = 3;
    private const int MaxStitchedHeight = 20000;
    private const int PollMs = 250;
    private const int SettleTimeoutMs = 1600; // message delivery + repaint can lag (EDR hooks, slow apps)

    /// <summary>
    /// Screen-space client area of the window with the vertical-scrollbar
    /// column trimmed (its arrows/thumb are static chrome inside otherwise-
    /// scrolling rows). Returns false when the window has no usable area.
    /// </summary>
    public static bool GetCaptureArea(IntPtr hwnd, out Rectangle clientRect)
    {
        clientRect = Rectangle.Empty;
        if (!GetClientRect(hwnd, out var cr) || cr.Right - cr.Left < 8 || cr.Bottom - cr.Top < 8)
            return false;
        var origin = new POINT { X = cr.Left, Y = cr.Top };
        ClientToScreen(hwnd, ref origin);
        clientRect = new Rectangle(origin.X, origin.Y, cr.Right - cr.Left, cr.Bottom - cr.Top);
        var vscroll = GetSystemMetrics(SM_CXVSCROLL) + 2;
        if (clientRect.Width > vscroll * 4) clientRect.Width -= vscroll;
        return true;
    }

    /// <summary>
    /// Posts one wheel-turn of scroll-down to the window's content view (the
    /// deepest child at the client center). Used by the manual capture's
    /// auto-scroll assist; most apps honor it, and the user can always scroll
    /// by hand when one does not.
    /// </summary>
    public static void PostWheelScroll(IntPtr hwnd, Rectangle clientRect)
    {
        var centerX = clientRect.X + clientRect.Width / 2;
        var centerY = clientRect.Y + clientRect.Height / 2;
        var target = ResolveWheelTarget(hwnd, centerX, centerY);
        PostScroll(0, target, hwnd, centerX, centerY, clientRect);
    }

    private static IntPtr ResolveWheelTarget(IntPtr hwnd, int centerX, int centerY)
    {
        // Wheel messages must reach the child that actually hosts the
        // scrolling content, which can be nested several levels deep.
        // RealChildWindowFromPoint returns only a FIRST-level child; for
        // Chromium browsers that is the "Intermediate D3D Window", which
        // silently discards posted wheel messages (verified: only the
        // deepest child, Chrome_RenderWidgetHostHWND, scrolls). Guard via
        // GA_ROOT and fall back to the old chain.
        var centerPt = new POINT { X = centerX, Y = centerY };
        var target = WindowFromPoint(centerPt);
        if (target != IntPtr.Zero && GetAncestor(target, GA_ROOT) == hwnd) return target;
        GetClientRect(hwnd, out var cr);
        var centerClient = new POINT { X = (cr.Right - cr.Left) / 2, Y = (cr.Bottom - cr.Top) / 2 };
        target = RealChildWindowFromPoint(hwnd, centerClient);
        return target != IntPtr.Zero ? target : hwnd;
    }

    private static readonly string[] StrategyNames =
        { "posted-wheel", "wm-vscroll", "arrow-keys", "sendinput-wheel" };
    private const int LastStrategy = 3;

    /// <summary>
    /// Injects one scroll-down of roughly one wheel turn using the given
    /// strategy. Not every app honors every mechanism (posted legacy wheel is
    /// ignored by pointer-input/DirectManipulation apps; WM_VSCROLL only works
    /// on classic Win32 controls; SendInput is swallowed by some EDR products),
    /// so Run escalates through them until one moves the page.
    /// </summary>
    private static void PostScroll(int strategy, IntPtr wheelTarget, IntPtr hwnd,
        int centerX, int centerY, Rectangle clientRect)
    {
        switch (strategy)
        {
            case 0: // posted WM_MOUSEWHEEL to the deepest child (fast path, most apps)
                var wParam = (IntPtr)unchecked((((-120 * WheelNotches) & 0xFFFF) << 16));
                var lParam = (IntPtr)((centerY << 16) | (centerX & 0xFFFF));
                PostMessage(wheelTarget, WM_MOUSEWHEEL, wParam, lParam);
                break;
            case 1: // classic scrollbar protocol (native edit/list controls)
                for (var i = 0; i < WheelNotches * 3; i++)
                {
                    PostMessage(wheelTarget, WM_VSCROLL, (IntPtr)SB_LINEDOWN, IntPtr.Zero);
                    if (wheelTarget != hwnd) PostMessage(hwnd, WM_VSCROLL, (IntPtr)SB_LINEDOWN, IntPtr.Zero);
                }
                break;
            case 2: // arrow-key burst (browsers/readers honor posted keys).
                    // NOT PageDown: its near-viewport travel leaves so little
                    // overlap that a sticky page header can swallow the probe
                    // strip and lose the stitch; ~8 line-downs matches the
                    // wheel rung's travel and keeps generous overlap.
                for (var i = 0; i < 8; i++)
                {
                    PostMessage(wheelTarget, WM_KEYDOWN, (IntPtr)VK_DOWN, (IntPtr)1);
                    PostMessage(wheelTarget, WM_KEYUP, (IntPtr)VK_DOWN, unchecked((IntPtr)0xC0000001));
                }
                break;
            case 3: // hardware wheel at the content center - hover routing reaches
                    // even pointer-input apps and background windows; the cursor
                    // returns to its parking spot right away so overlays stay out
                    // of the captured frames.
                SetCursorPos(centerX, centerY);
                Thread.Sleep(30);
                var wheel = new[]
                {
                    new INPUT
                    {
                        Type = INPUT_MOUSE,
                        Mouse = new MOUSEINPUT { MouseData = unchecked((uint)(-120 * WheelNotches)), Flags = MOUSEEVENTF_WHEEL },
                    },
                };
                SendInput(1, wheel, Marshal.SizeOf<INPUT>());
                Thread.Sleep(30);
                SetCursorPos(clientRect.Right - 4, clientRect.Y + 4);
                break;
        }
    }

    public static ScrollCaptureResult Run(IntPtr hwnd, Action<int>? onStep = null,
        Func<bool>? isCancelled = null, int maxSteps = 60, int firstStrategy = 0)
    {
        var result = new ScrollCaptureResult();
        var cursorParked = false;
        POINT savedCursor = default;
        try
        {
            var fgVerified = ForceForeground(hwnd);
            Thread.Sleep(250);

            if (!GetCaptureArea(hwnd, out var clientRect))
            {
                result.Error = "window has no usable client area";
                return result;
            }

            // Software cursor-highlighter overlays (locate-pointer halos,
            // presentation pointers) are drawn into the screen pixels and
            // follow the cursor; resting inside the scrolled content they
            // corrupt every captured frame. Park the cursor at the window's
            // top-right corner for the duration of the capture.
            if (GetCursorPos(out savedCursor))
            {
                cursorParked = true;
                SetCursorPos(clientRect.Right - 4, clientRect.Y + 4);
                Thread.Sleep(150); // let hover effects/overlays settle before frame 1
            }

            var centerX = clientRect.X + clientRect.Width / 2;
            var centerY = clientRect.Y + clientRect.Height / 2;

            var wheelTarget = ResolveWheelTarget(hwnd, centerX, centerY);
            Log.Info($"Scroll capture: target={ClassOf(hwnd)} client={clientRect.Width}x{clientRect.Height} " +
                     $"wheelTarget={ClassOf(wheelTarget)} foreground={fgVerified}");

            Bitmap accumulated = ScreenCapture.CaptureRect(clientRect);
            var prev = (Bitmap)accumulated.Clone();
            var furnitureTrimmed = false;
            // Not every app honors the default scroll mechanism; escalate through
            // the ladder until one moves the page, then lock it for the run.
            var strategy = Math.Clamp(firstStrategy, 0, LastStrategy);
            var strategyLocked = false;
            for (var step = 1; step <= maxSteps; step++)
            {
                onStep?.Invoke(step);
                if (isCancelled?.Invoke() == true) break;

                Bitmap current = null!;
                (int NewContentOffset, int StaticBottomRows)? match = null;
                while (true)
                {
                    PostScroll(strategy, wheelTarget, hwnd, centerX, centerY, clientRect);

                    // Poll until the content actually moves - message delivery and
                    // repaint latency vary between apps.
                    var moved = false;
                    var waited = 0;
                    while (true)
                    {
                        Thread.Sleep(PollMs);
                        waited += PollMs;
                        current = ScreenCapture.CaptureRect(clientRect);
                        match = ImageStitcher.FindOverlap(prev, current);
                        moved = match.HasValue &&
                            match.Value.NewContentOffset < current.Height - match.Value.StaticBottomRows;
                        if (moved || waited >= SettleTimeoutMs || isCancelled?.Invoke() == true) break;
                        current.Dispose();
                    }
                    if (moved)
                    {
                        // Smooth-scrolling apps keep animating after the input
                        // is injected; accepting a mid-animation frame lets the
                        // NEXT injection start while this one is still
                        // travelling, so a single step can span almost two
                        // viewports and lose the overlap. Wait until the frame
                        // stabilizes so each step covers one injection's travel.
                        for (var settle = 0; settle < 6; settle++)
                        {
                            Thread.Sleep(150);
                            var again = ScreenCapture.CaptureRect(clientRect);
                            var still = ImageStitcher.FindOverlap(current, again);
                            if (still.HasValue &&
                                still.Value.NewContentOffset >= again.Height - still.Value.StaticBottomRows)
                            {
                                again.Dispose();
                                break;
                            }
                            current.Dispose();
                            current = again;
                        }
                        match = ImageStitcher.FindOverlap(prev, current);
                        moved = match.HasValue &&
                            match.Value.NewContentOffset < current.Height - match.Value.StaticBottomRows;
                    }
                    if (moved && !strategyLocked)
                    {
                        strategyLocked = true;
                        result.Strategy = StrategyNames[strategy];
                        Log.Info($"Scroll capture: page moves via {StrategyNames[strategy]}");
                    }
                    if (moved || strategyLocked || strategy >= LastStrategy ||
                        isCancelled?.Invoke() == true) break;
                    current.Dispose();
                    Log.Info($"Scroll capture: no movement via {StrategyNames[strategy]}, escalating");
                    strategy++;
                    if (!fgVerified) fgVerified = ForceForeground(hwnd);
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
        finally
        {
            if (cursorParked) SetCursorPos(savedCursor.X, savedCursor.Y);
        }
    }
}

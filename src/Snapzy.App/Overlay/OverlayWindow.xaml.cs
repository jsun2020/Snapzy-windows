using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Snapzy.Core.Capture;
using Snapzy.Core.Localization;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingPoint = System.Drawing.Point;

namespace Snapzy.App.Overlay;

// Selection math runs entirely in physical virtual-screen pixels; DIP conversion
// happens only when placing WPF elements. Known v1 limitation: on mixed-DPI
// multi-monitor setups the DIP conversion uses the scale of the monitor the
// overlay window starts on (usually primary).
public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;

    private readonly DrawingRectangle _virtualScreen; // physical px
    private readonly List<WindowInfo> _windows;
    private double _scale = 1.0;

    private SelectionResult? _result;
    private bool _windowMode;
    private bool _dragging;
    private bool _hasSelection;
    private DrawingPoint _dragStart;             // physical
    private DrawingRectangle _selection;         // physical
    private WindowInfo? _hoverWindow;

    private OverlayWindow(bool startInWindowMode)
    {
        InitializeComponent();
        // A CJK IME would otherwise eat the plain-letter shortcuts (A).
        InputMethod.SetIsInputMethodEnabled(this, false);
        _windowMode = startInWindowMode;
        _virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        _windows = WindowEnumerator.GetTopLevelWindows();
        HintsText.Text = Strings.Get("Overlay_Hints");

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, HWND_TOPMOST, _virtualScreen.Left, _virtualScreen.Top,
                _virtualScreen.Width, _virtualScreen.Height, SWP_SHOWWINDOW);
            // WPF's routed MouseUp is unreliable on this layered full-screen window
            // (WM_LBUTTONUP arrives at the hwnd but is never routed), so left-button
            // release is handled at the Win32 level.
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == 0x0202) HandleLeftUp(); // WM_LBUTTONUP
                return IntPtr.Zero;
            });
        };
        DpiChanged += (_, e) => _scale = e.NewDpi.DpiScaleX;
        Loaded += (_, _) =>
        {
            _scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Activate();
            Focus();
            LayoutHints();
            UpdateVisuals();
        };
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        PreviewKeyDown += OnKeyDown;
    }

    public static SelectionResult? ShowAndSelect(bool startInWindowMode = false)
    {
        var w = new OverlayWindow(startInWindowMode);
        w.ShowDialog();
        return w._result;
    }

    // ---- coordinate helpers ----

    private static DrawingPoint CursorPhysical() => System.Windows.Forms.Control.MousePosition;

    private double ToDipX(int physX) => (physX - _virtualScreen.Left) / _scale;
    private double ToDipY(int physY) => (physY - _virtualScreen.Top) / _scale;
    private double ToDipW(int physW) => physW / _scale;

    // ---- input ----

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2 && _hasSelection)
        {
            Confirm(_selection, IntPtr.Zero);
            return;
        }
        _dragging = true;
        _dragStart = CursorPhysical();
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var cur = CursorPhysical();
        if (_dragging)
        {
            var x = Math.Min(_dragStart.X, cur.X);
            var y = Math.Min(_dragStart.Y, cur.Y);
            _selection = new DrawingRectangle(x, y,
                Math.Abs(cur.X - _dragStart.X), Math.Abs(cur.Y - _dragStart.Y));
            _hasSelection = _selection.Width >= 4 && _selection.Height >= 4;
        }
        else if (!_hasSelection)
        {
            _hoverWindow = _windows.FirstOrDefault(w => w.Bounds.Contains(cur));
            if (_windowMode && _hoverWindow is not null)
                _selection = _hoverWindow.Bounds;
        }
        UpdateVisuals();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        HandleLeftUp();
    }

    private void HandleLeftUp()
    {
        if (!_dragging) return;
        _dragging = false;
        if (_selection.Width < 4 || _selection.Height < 4)
        {
            // Click without drag: snap the window under the cursor.
            var cur = CursorPhysical();
            var win = _windows.FirstOrDefault(w => w.Bounds.Contains(cur));
            if (win is not null)
            {
                Confirm(win.Bounds, win.Hwnd);
                return;
            }
            _hasSelection = false;
            _selection = DrawingRectangle.Empty;
        }
        else if (_windowMode && !_hasSelection)
        {
            _selection = DrawingRectangle.Empty;
        }
        UpdateVisuals();
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Escape:
                _result = null;
                Close();
                break;
            case Key.Enter:
                if (_hasSelection) Confirm(_selection, IntPtr.Zero);
                else if (_windowMode && _hoverWindow is not null) Confirm(_hoverWindow.Bounds, _hoverWindow.Hwnd);
                break;
            case Key.A:
                _windowMode = !_windowMode;
                if (!_windowMode && !_hasSelection) _selection = DrawingRectangle.Empty;
                UpdateVisuals();
                break;
            case Key.Left: Nudge(-1, 0, shift); break;
            case Key.Right: Nudge(1, 0, shift); break;
            case Key.Up: Nudge(0, -1, shift); break;
            case Key.Down: Nudge(0, 1, shift); break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Nudge(int dx, int dy, bool resize)
    {
        if (!_hasSelection) return;
        if (resize)
        {
            _selection.Width = Math.Max(4, _selection.Width + dx);
            _selection.Height = Math.Max(4, _selection.Height + dy);
        }
        else
        {
            _selection.X += dx;
            _selection.Y += dy;
        }
        UpdateVisuals();
    }

    private void Confirm(DrawingRectangle rect, IntPtr hwnd)
    {
        rect.Intersect(_virtualScreen);
        if (rect.Width < 1 || rect.Height < 1) { _result = null; Close(); return; }
        _result = new SelectionResult { Rect = rect, Hwnd = hwnd };
        Close();
    }

    // ---- visuals ----

    private void UpdateVisuals()
    {
        var showSel = _hasSelection || _dragging || (_windowMode && !_selection.IsEmpty);
        var sel = _selection;
        var canvasW = ToDipW(_virtualScreen.Width);
        var canvasH = ToDipW(_virtualScreen.Height);

        double x = ToDipX(sel.X), y = ToDipY(sel.Y), w = ToDipW(sel.Width), h = ToDipW(sel.Height);
        if (!showSel) { x = 0; y = 0; w = 0; h = 0; }

        PlaceRect(DimTop, 0, 0, canvasW, y);
        PlaceRect(DimBottom, 0, y + h, canvasW, Math.Max(0, canvasH - y - h));
        PlaceRect(DimLeft, 0, y, x, h);
        PlaceRect(DimRight, x + w, y, Math.Max(0, canvasW - x - w), h);

        if (showSel && w > 0 && h > 0)
        {
            SelBorder.Visibility = Visibility.Visible;
            PlaceRect(SelBorder, x, y, w, h);
            DrawHandles(x, y, w, h);
            Handles.Visibility = Visibility.Visible;

            Hud.Visibility = Visibility.Visible;
            HudText.Text = $"{sel.Width} x {sel.Height}  ({sel.X}, {sel.Y})";
            var cur = CursorPhysical();
            var hudX = Math.Min(ToDipX(cur.X) + 16, canvasW - 160);
            var hudY = Math.Min(ToDipY(cur.Y) + 20, canvasH - 40);
            System.Windows.Controls.Canvas.SetLeft(Hud, Math.Max(0, hudX));
            System.Windows.Controls.Canvas.SetTop(Hud, Math.Max(0, hudY));
        }
        else
        {
            SelBorder.Visibility = Visibility.Collapsed;
            Handles.Visibility = Visibility.Collapsed;
            Hud.Visibility = Visibility.Collapsed;
        }
    }

    private void DrawHandles(double x, double y, double w, double h)
    {
        Handles.Children.Clear();
        double[] xs = { x, x + w / 2, x + w };
        double[] ys = { y, y + h / 2, y + h };
        foreach (var hx in xs)
        {
            foreach (var hy in ys)
            {
                if (hx == x + w / 2 && hy == y + h / 2) continue; // no center dot
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x90, 0xFA)),
                    Stroke = System.Windows.Media.Brushes.White,
                    StrokeThickness = 1,
                };
                System.Windows.Controls.Canvas.SetLeft(dot, hx - 4);
                System.Windows.Controls.Canvas.SetTop(dot, hy - 4);
                Handles.Children.Add(dot);
            }
        }
    }

    private static void PlaceRect(Shape r, double x, double y, double w, double h)
    {
        System.Windows.Controls.Canvas.SetLeft(r, x);
        System.Windows.Controls.Canvas.SetTop(r, y);
        r.Width = Math.Max(0, w);
        r.Height = Math.Max(0, h);
    }

    private void LayoutHints()
    {
        HintsBar.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var barW = HintsBar.DesiredSize.Width;
        // Center over the primary monitor's bottom edge.
        var primary = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var cx = ToDipX(primary.Left + primary.Width / 2) - barW / 2;
        var cy = ToDipY(primary.Bottom) - 64;
        System.Windows.Controls.Canvas.SetLeft(HintsBar, cx);
        System.Windows.Controls.Canvas.SetTop(HintsBar, cy);
    }
}

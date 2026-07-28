using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Snapzy.Core;
using Snapzy.Core.Capture;
using Snapzy.Core.Editing;
using Snapzy.Core.Localization;
using Snapzy.Core.Settings;
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
    private readonly bool _showToolbar;
    private bool _dragging;
    private bool _hasSelection;
    private DrawingPoint _dragStart;             // physical
    private DrawingRectangle _selection;         // physical
    private IntPtr _selHwnd = IntPtr.Zero;       // set when the selection came from a window click
    private WindowInfo? _hoverWindow;
    private bool _wmEnabled;
    private bool _wmDirty;

    private OverlayWindow(bool startInWindowMode, bool showToolbar)
    {
        InitializeComponent();
        // A CJK IME would otherwise eat the plain-letter shortcuts (A).
        InputMethod.SetIsInputMethodEnabled(this, false);
        _windowMode = startInWindowMode;
        _showToolbar = showToolbar;
        _virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        _windows = WindowEnumerator.GetTopLevelWindows();
        HintsText.Text = Strings.Get("Overlay_Hints");
        TbAnnotate.ToolTip = Strings.Get("Overlay_ToolAnnotate");
        TbOcr.ToolTip = Strings.Get("Overlay_ToolOcr");
        TbRecord.ToolTip = Strings.Get("Overlay_ToolRecord");
        TbConfirm.ToolTip = Strings.Get("Overlay_ToolConfirm");
        TbCancel.ToolTip = Strings.Get("Overlay_ToolCancel");
        TbScroll.ToolTip = Strings.Get("Overlay_ToolScroll");
        TbWatermark.ToolTip = Strings.Get("Overlay_ToolWatermark");
        TbOcr.Visibility = Snapzy.Core.Ocr.OcrService.IsAvailable
            ? Visibility.Visible : Visibility.Collapsed;
        // Clicks on the toolbar chrome must not start a new drag selection.
        Toolbar.MouseDown += (_, e) => e.Handled = true;
        Toolbar.MouseUp += (_, e) => e.Handled = true;
        WmBar.MouseDown += (_, e) => e.Handled = true;
        WmBar.MouseUp += (_, e) => e.Handled = true;

        var wm = AppActions.Settings.Watermark;
        _wmEnabled = wm.Enabled;
        WmText.Text = wm.Text;
        WmText.ToolTip = Strings.Get("Overlay_WmTextTip");
        // The window disables the IME for single-letter shortcuts; the
        // watermark text box needs it back for CJK input.
        InputMethod.SetIsInputMethodEnabled(WmText, true);
        foreach (var name in Enum.GetNames<WatermarkPosition>())
            WmPos.Items.Add(new ComboBoxItem { Content = Strings.Get("Wm_" + name), Tag = name });
        WmPos.SelectedIndex = (int)WatermarkLayout.ParsePosition(wm.Position);
        WmText.TextChanged += (_, _) => { _wmDirty = true; UpdateVisuals(); };
        WmPos.SelectionChanged += (_, _) => { _wmDirty = true; UpdateVisuals(); };
        UpdateWmButtonLook();
        Closed += (_, _) => PersistWatermark();

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

    public static SelectionResult? ShowAndSelect(bool startInWindowMode = false, bool showToolbar = false)
    {
        var w = new OverlayWindow(startInWindowMode, showToolbar);
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
            Confirm(_selection, _selHwnd);
            return;
        }
        _dragging = true;
        _selHwnd = IntPtr.Zero;
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
                if (_showToolbar)
                {
                    // Mouse-first flow: keep the window selected and let the
                    // floating toolbar decide what happens next.
                    _selection = win.Bounds;
                    _selHwnd = win.Hwnd;
                    _hasSelection = true;
                    UpdateVisuals();
                    return;
                }
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
        // While typing watermark text, letters/arrows belong to the text box;
        // Enter/Esc just commit and return focus to the overlay.
        if (WmText.IsKeyboardFocusWithin)
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                Keyboard.ClearFocus();
                Focus();
                e.Handled = true;
            }
            return;
        }
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Escape:
                _result = null;
                Close();
                break;
            case Key.Enter:
                if (_hasSelection) Confirm(_selection, _selHwnd);
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

    private void Confirm(DrawingRectangle rect, IntPtr hwnd, OverlayAction action = OverlayAction.Confirm)
    {
        rect.Intersect(_virtualScreen);
        if (rect.Width < 1 || rect.Height < 1) { _result = null; Close(); return; }
        _result = new SelectionResult { Rect = rect, Hwnd = hwnd, Action = action };
        Close();
    }

    // ---- floating toolbar ----

    private void OnTbConfirm(object sender, RoutedEventArgs e) => Confirm(_selection, _selHwnd);
    private void OnTbAnnotate(object sender, RoutedEventArgs e) => Confirm(_selection, _selHwnd, OverlayAction.Annotate);
    private void OnTbOcr(object sender, RoutedEventArgs e) => Confirm(_selection, _selHwnd, OverlayAction.Ocr);
    private void OnTbRecord(object sender, RoutedEventArgs e) => Confirm(_selection, _selHwnd, OverlayAction.Record);
    private void OnTbCancel(object sender, RoutedEventArgs e) { _result = null; Close(); }

    private void OnTbScroll(object sender, RoutedEventArgs e)
    {
        // Long screenshot needs a window to scroll: the clicked window, or
        // the one under the selection's center.
        var hwnd = _selHwnd;
        if (hwnd == IntPtr.Zero)
        {
            var center = new DrawingPoint(
                _selection.X + _selection.Width / 2, _selection.Y + _selection.Height / 2);
            hwnd = _windows.FirstOrDefault(w => w.Bounds.Contains(center))?.Hwnd ?? IntPtr.Zero;
        }
        Confirm(_selection, hwnd, OverlayAction.Scroll);
    }

    private void OnTbWatermark(object sender, RoutedEventArgs e)
    {
        _wmEnabled = !_wmEnabled;
        _wmDirty = true;
        UpdateWmButtonLook();
        UpdateVisuals();
        if (_wmEnabled) WmText.Focus();
    }

    private void UpdateWmButtonLook() =>
        TbWatermark.Foreground = new SolidColorBrush(_wmEnabled
            ? System.Windows.Media.Color.FromRgb(0x2E, 0x90, 0xFA)
            : System.Windows.Media.Color.FromArgb(0xDD, 0x33, 0x33, 0x33));

    private WatermarkPosition CurrentWmPosition() =>
        WatermarkLayout.ParsePosition((WmPos.SelectedItem as ComboBoxItem)?.Tag as string);

    private void PersistWatermark()
    {
        if (!_wmDirty) return;
        try
        {
            var wm = AppActions.Settings.Watermark;
            wm.Enabled = _wmEnabled;
            wm.Text = WmText.Text.Trim();
            wm.Position = CurrentWmPosition().ToString();
            SettingsStore.Save(AppActions.Settings, AppPaths.SettingsFile);
        }
        catch (Exception ex)
        {
            Log.Error("Watermark settings save failed", ex);
        }
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

        if (_showToolbar && _hasSelection && !_dragging && w > 0 && h > 0)
        {
            Toolbar.Visibility = Visibility.Visible;
            Toolbar.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var pos = ToolbarPlacement.Place(x, y, w, h,
                Toolbar.DesiredSize.Width, Toolbar.DesiredSize.Height, canvasW, canvasH);
            System.Windows.Controls.Canvas.SetLeft(Toolbar, pos.X);
            System.Windows.Controls.Canvas.SetTop(Toolbar, pos.Y);
            PlaceWmBar(pos.X, pos.Y, canvasW, canvasH);
            UpdateWmPreview(x, y, w, h);
        }
        else
        {
            Toolbar.Visibility = Visibility.Collapsed;
            WmBar.Visibility = Visibility.Collapsed;
            WmPreview.Visibility = Visibility.Collapsed;
        }
    }

    private void PlaceWmBar(double toolbarX, double toolbarY, double canvasW, double canvasH)
    {
        if (!_wmEnabled)
        {
            WmBar.Visibility = Visibility.Collapsed;
            return;
        }
        WmBar.Visibility = Visibility.Visible;
        WmBar.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var wy = toolbarY + Toolbar.DesiredSize.Height + 6;
        if (wy + WmBar.DesiredSize.Height > canvasH)
            wy = toolbarY - 6 - WmBar.DesiredSize.Height;
        var wx = Math.Max(8, Math.Min(toolbarX, canvasW - WmBar.DesiredSize.Width - 8));
        System.Windows.Controls.Canvas.SetLeft(WmBar, wx);
        System.Windows.Controls.Canvas.SetTop(WmBar, wy);
    }

    private void UpdateWmPreview(double x, double y, double w, double h)
    {
        WmPreview.Children.Clear();
        var text = WmText.Text.Trim();
        if (!_wmEnabled || text.Length == 0 || w < 4 || h < 4)
        {
            WmPreview.Visibility = Visibility.Collapsed;
            return;
        }
        WmPreview.Visibility = Visibility.Visible;
        WmPreview.Clip = new RectangleGeometry(new Rect(x, y, w, h));

        var opts = AppActions.Settings.Watermark;
        var fontPx = opts.FontSize > 0 ? opts.FontSize : WatermarkLayout.AutoFontSize((int)(w * _scale));
        var fontDip = fontPx / _scale;
        var dc = WatermarkRenderer.ParseColor(opts.ColorHex);
        var alpha = (byte)(Math.Clamp(opts.Opacity, 0, 100) * 255 / 100);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, dc.R, dc.G, dc.B));
        brush.Freeze();
        var family = new System.Windows.Media.FontFamily("Microsoft YaHei");

        TextBlock Make() => new()
        {
            Text = text,
            FontFamily = family,
            FontWeight = FontWeights.Bold,
            FontSize = fontDip,
            Foreground = brush,
        };

        var probe = Make();
        probe.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var tw = (float)Math.Max(1, probe.DesiredSize.Width);
        var th = (float)Math.Max(1, probe.DesiredSize.Height);

        var pos = CurrentWmPosition();
        if (pos == WatermarkPosition.Tile)
        {
            var stepX = Math.Max(24f, tw * WatermarkRenderer.TileStepXFactor);
            var stepY = Math.Max(24f, th * WatermarkRenderer.TileStepYFactor);
            var count = 0;
            foreach (var (px, py) in WatermarkLayout.Tile((int)w, (int)h, stepX, stepY))
            {
                if (++count > 300) break;
                var tb = Make();
                tb.RenderTransform = new RotateTransform(WatermarkRenderer.TileAngle);
                System.Windows.Controls.Canvas.SetLeft(tb, x + px);
                System.Windows.Controls.Canvas.SetTop(tb, y + py);
                WmPreview.Children.Add(tb);
            }
        }
        else
        {
            var (ax, ay) = WatermarkLayout.Anchor((int)w, (int)h, tw, th, pos);
            var tb = Make();
            System.Windows.Controls.Canvas.SetLeft(tb, x + ax);
            System.Windows.Controls.Canvas.SetTop(tb, y + ay);
            WmPreview.Children.Add(tb);
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

    // ---- E2E driver hooks (no interactive input available in the test harness) ----

    public static OverlayWindow CreateForDriver(bool showToolbar) => new(false, showToolbar);

    public void DriverSelect(int x, int y, int w, int h)
    {
        _selection = new DrawingRectangle(x, y, w, h);
        _hasSelection = true;
        _dragging = false;
        UpdateVisuals();
    }

    public (bool Visible, double X, double Y, double W, double H) DriverToolbarState() =>
        (Toolbar.Visibility == Visibility.Visible,
         System.Windows.Controls.Canvas.GetLeft(Toolbar),
         System.Windows.Controls.Canvas.GetTop(Toolbar),
         Toolbar.DesiredSize.Width, Toolbar.DesiredSize.Height);

    public void DriverSetWatermark(string text, string position)
    {
        _wmEnabled = true;
        WmText.Text = text;
        for (var i = 0; i < WmPos.Items.Count; i++)
            if ((WmPos.Items[i] as ComboBoxItem)?.Tag as string == position) { WmPos.SelectedIndex = i; break; }
        _wmDirty = false; // driver runs must not touch the user's settings file
        UpdateWmButtonLook();
        UpdateVisuals();
    }

    public (bool BarVisible, int PreviewCount) DriverWatermarkState() =>
        (WmBar.Visibility == Visibility.Visible, WmPreview.Children.Count);

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

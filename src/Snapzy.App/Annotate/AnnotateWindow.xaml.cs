using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Snapzy.Core;
using Snapzy.Core.History;
using Snapzy.Core.Localization;
using WpfPoint = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;
using Path = System.Windows.Shapes.Path;
using Popup = System.Windows.Controls.Primitives.Popup;
using Cursors = System.Windows.Input.Cursors;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;

namespace Snapzy.App.Annotate;

public partial class AnnotateWindow : Window
{
    private static readonly Dictionary<string, AnnotateWindow> OpenWindows = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<Tool, (Color Color, double Width)> ToolMemory = new();
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0xE5, 0x39, 0x35), // red
        Color.FromRgb(0xFB, 0x8C, 0x00), // orange
        Color.FromRgb(0xFD, 0xD8, 0x35), // yellow
        Color.FromRgb(0x43, 0xA0, 0x47), // green
        Color.FromRgb(0x1E, 0x88, 0xE5), // blue
        Color.FromRgb(0x8E, 0x24, 0xAA), // purple
        Color.FromRgb(0xFF, 0xFF, 0xFF), // white
        Color.FromRgb(0x21, 0x21, 0x21), // black
    };

    private readonly string _imagePath;
    private readonly HistoryEntry? _entry;
    private readonly HistoryStore? _store;
    private readonly UndoStack _undo = new();
    private readonly Dictionary<Tool, ToggleButton> _toolButtons = new();

    private Tool _tool = Tool.Arrow;
    private Color _color = Color.FromRgb(0xE5, 0x39, 0x35);
    private double _strokeWidth = 4;
    private bool _dirty;
    private int _imgW, _imgH;

    // drawing state
    private bool _drawing;
    private WpfPoint _drawStart;
    private Shape? _activeShape;
    private Polyline? _activePolyline;

    // selection state
    private FrameworkElement? _selected;
    private Rectangle? _selectionBox;
    private bool _movingSelection;
    private WpfPoint _moveStart;
    private double _moveBaseX, _moveBaseY;

    // pan state
    private bool _spaceDown;
    private bool _panning;
    private WpfPoint _panStart;
    private double _panStartH, _panStartV;

    public AnnotateWindow(string imagePath, HistoryEntry? entry, HistoryStore? store)
    {
        InitializeComponent();
        _imagePath = imagePath;
        _entry = entry;
        _store = store;

        // Keep single-letter tool shortcuts working when a CJK IME is active;
        // text boxes re-enable the IME so CJK annotation text still works.
        InputMethod.SetIsInputMethodEnabled(this, false);

        LoadImage();
        BuildToolbar();
        WireEvents();
        UpdateTitle();
    }

    public static void Open(string imagePath, HistoryEntry? entry, HistoryStore? store)
    {
        if (OpenWindows.TryGetValue(imagePath, out var existing))
        {
            existing.Activate();
            return;
        }
        var w = new AnnotateWindow(imagePath, entry, store);
        OpenWindows[imagePath] = w;
        w.Closed += (_, _) => OpenWindows.Remove(imagePath);
        w.Show();
        w.Activate();
    }

    private void LoadImage()
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.UriSource = new Uri(_imagePath);
        img.EndInit();
        img.Freeze();
        BaseImage.Source = img;
        // Exact 1:1 pixel reproduction in the export (no resampling drift).
        RenderOptions.SetBitmapScalingMode(BaseImage, BitmapScalingMode.NearestNeighbor);
        BaseImage.SnapsToDevicePixels = true;
        _imgW = img.PixelWidth;
        _imgH = img.PixelHeight;
        // 1 canvas unit == 1 source pixel (96dpi render target).
        BaseImage.Width = _imgW;
        BaseImage.Height = _imgH;
        Anno.Width = _imgW;
        Anno.Height = _imgH;
        CanvasRoot.Width = _imgW;
        CanvasRoot.Height = _imgH;
    }

    private void BuildToolbar()
    {
        foreach (var tool in Enum.GetValues<Tool>())
        {
            var btn = new ToggleButton
            {
                Content = Strings.Get("Tool_" + tool),
                ToolTip = Strings.Get("Tool_" + tool),
                Tag = tool,
            };
            btn.Click += (_, _) => SelectTool(tool);
            _toolButtons[tool] = btn;
            ToolButtons.Children.Add(btn);
        }
        BtnCopy.Content = Strings.Get("Annotate_Copy");
        BtnSave.Content = Strings.Get("Annotate_Save");
        BtnSaveAs.Content = Strings.Get("Annotate_SaveAs");

        foreach (var w in new[] { 2.0, 4.0, 6.0 }) WidthCombo.Items.Add(w);
        WidthCombo.SelectedItem = _strokeWidth;

        SelectTool(Tool.Arrow);
        UpdateUndoButtons();
        _undo.Changed += UpdateUndoButtons;
    }

    private void WireEvents()
    {
        Anno.MouseLeftButtonDown += OnCanvasMouseDown;
        Anno.MouseMove += OnCanvasMouseMove;
        Anno.MouseLeftButtonUp += OnCanvasMouseUp;
        Scroll.PreviewMouseWheel += OnWheel;
        Scroll.PreviewMouseLeftButtonDown += OnScrollMouseDown;
        Scroll.PreviewMouseMove += OnScrollMouseMove;
        Scroll.PreviewMouseLeftButtonUp += (_, _) => _panning = false;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += (_, e) => { if (e.Key == Key.Space) { _spaceDown = false; Scroll.Cursor = null; } };
        Loaded += (_, _) => FitToWindow();
        Closing += OnClosingPrompt;
    }

    private void SelectTool(Tool tool)
    {
        _tool = tool;
        foreach (var (t, b) in _toolButtons) b.IsChecked = t == tool;
        if (ToolMemory.TryGetValue(tool, out var mem))
        {
            _color = mem.Color;
            _strokeWidth = mem.Width;
            ColorSwatch.Fill = new SolidColorBrush(_color);
            WidthCombo.SelectedItem = _strokeWidth;
        }
        ClearSelection();
    }

    private void RememberToolStyle()
    {
        ToolMemory[_tool] = (_color, _strokeWidth);
    }

    // ---------- drawing ----------

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_spaceDown) return;
        var p = e.GetPosition(Anno);
        if (_tool == Tool.Select)
        {
            StartSelectOrMove(p, e);
            return;
        }
        CancelPendingCrop();
        if (_tool == Tool.Text)
        {
            BeginTextEntry(p);
            return;
        }
        if (_tool == Tool.Counter)
        {
            AddCounterBadge(p);
            return;
        }
        if (_tool is Tool.Blur or Tool.Pixelate or Tool.Crop)
        {
            _drawing = true;
            _drawStart = p;
            Anno.CaptureMouse();
            _activeShape = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x2E, 0x90, 0xFA)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(0x20, 0x2E, 0x90, 0xFA)),
            };
            Anno.Children.Add(_activeShape);
            return;
        }
        if (_tool is not (Tool.Rect or Tool.Ellipse or Tool.Line or Tool.Arrow or Tool.Freehand)) return;

        _drawing = true;
        _drawStart = p;
        Anno.CaptureMouse();
        var brush = new SolidColorBrush(_color);
        switch (_tool)
        {
            case Tool.Rect:
                _activeShape = new Rectangle { Stroke = brush, StrokeThickness = _strokeWidth };
                break;
            case Tool.Ellipse:
                _activeShape = new Ellipse { Stroke = brush, StrokeThickness = _strokeWidth };
                break;
            case Tool.Line:
                _activeShape = new Line { Stroke = brush, StrokeThickness = _strokeWidth, X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                break;
            case Tool.Arrow:
                _activeShape = new Path { Stroke = brush, Fill = brush, StrokeThickness = _strokeWidth, StrokeLineJoin = PenLineJoin.Round };
                break;
            case Tool.Freehand:
                _activePolyline = new Polyline { Stroke = brush, StrokeThickness = _strokeWidth, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                _activePolyline.Points.Add(p);
                _activeShape = _activePolyline;
                break;
        }
        if (_activeShape is not null) Anno.Children.Add(_activeShape);
    }

    private void OnCanvasMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(Anno);
        if (_movingSelection && _selected is not null)
        {
            var t = GetTranslate(_selected);
            t.X = _moveBaseX + (p.X - _moveStart.X);
            t.Y = _moveBaseY + (p.Y - _moveStart.Y);
            UpdateSelectionBox();
            return;
        }
        if (!_drawing || _activeShape is null) return;
        UpdateActiveShape(p);
    }

    private void UpdateActiveShape(WpfPoint p)
    {
        var x = Math.Min(p.X, _drawStart.X);
        var y = Math.Min(p.Y, _drawStart.Y);
        var w = Math.Abs(p.X - _drawStart.X);
        var h = Math.Abs(p.Y - _drawStart.Y);
        switch (_activeShape)
        {
            case Rectangle or Ellipse:
                Canvas.SetLeft(_activeShape, x);
                Canvas.SetTop(_activeShape, y);
                _activeShape.Width = w;
                _activeShape.Height = h;
                break;
            case Line line:
                line.X2 = p.X;
                line.Y2 = p.Y;
                break;
            case Path path:
                path.Data = BuildArrowGeometry(_drawStart, p, _strokeWidth);
                break;
            case Polyline poly:
                poly.Points.Add(p);
                break;
        }
    }

    public static Geometry BuildArrowGeometry(WpfPoint from, WpfPoint to, double strokeWidth)
    {
        var geo = new StreamGeometry();
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) len = 1;
        var ux = dx / len;
        var uy = dy / len;
        var head = Math.Max(8, strokeWidth * 4);
        var baseX = to.X - ux * head;
        var baseY = to.Y - uy * head;
        var px = -uy;
        var py = ux;
        var halfW = head * 0.5;
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(from, isFilled: false, isClosed: false);
            ctx.LineTo(new WpfPoint(baseX, baseY), isStroked: true, isSmoothJoin: false);
            ctx.BeginFigure(to, isFilled: true, isClosed: true);
            ctx.LineTo(new WpfPoint(baseX + px * halfW, baseY + py * halfW), true, false);
            ctx.LineTo(new WpfPoint(baseX - px * halfW, baseY - py * halfW), true, false);
        }
        geo.Freeze();
        return geo;
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_movingSelection && _selected is not null)
        {
            _movingSelection = false;
            Anno.ReleaseMouseCapture();
            var t = GetTranslate(_selected);
            var dx = t.X - _moveBaseX;
            var dy = t.Y - _moveBaseY;
            if (Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5)
            {
                _undo.Push(new MoveAction(this, _selected, dx, dy));
                MarkDirty();
            }
            return;
        }
        if (!_drawing || _activeShape is null) return;
        _drawing = false;
        Anno.ReleaseMouseCapture();
        var shape = _activeShape;
        _activeShape = null;
        _activePolyline = null;

        var p = e.GetPosition(Anno);
        if (_tool is Tool.Blur or Tool.Pixelate or Tool.Crop)
        {
            Anno.Children.Remove(shape);
            var region = NormalizedRegion(_drawStart, p);
            if (region.Width < 4 || region.Height < 4) return;
            if (_tool == Tool.Crop) ShowCropConfirm(region);
            else AddRegionEffect(region, pixelate: _tool == Tool.Pixelate);
            return;
        }

        // Discard degenerate shapes (simple click without drag)
        var moved = Math.Abs(p.X - _drawStart.X) > 2 || Math.Abs(p.Y - _drawStart.Y) > 2;
        if (!moved && shape is not Polyline)
        {
            Anno.Children.Remove(shape);
            return;
        }
        RememberToolStyle();
        _undo.Push(new AddElementAction(this, shape));
        MarkDirty();
    }

    private Int32Rect NormalizedRegion(WpfPoint a, WpfPoint b)
    {
        var x = (int)Math.Max(0, Math.Min(a.X, b.X));
        var y = (int)Math.Max(0, Math.Min(a.Y, b.Y));
        var w = (int)Math.Min(_imgW - x, Math.Abs(b.X - a.X));
        var h = (int)Math.Min(_imgH - y, Math.Abs(b.Y - a.Y));
        return new Int32Rect(x, y, Math.Max(0, w), Math.Max(0, h));
    }

    // ---------- selection ----------

    private void StartSelectOrMove(WpfPoint p, MouseButtonEventArgs e)
    {
        var hit = e.OriginalSource as FrameworkElement;
        while (hit is not null && hit.Parent != Anno && hit.Parent is FrameworkElement fe) hit = fe;
        if (hit is null || hit.Parent != Anno || hit == _selectionBox)
        {
            ClearSelection();
            return;
        }
        Select(hit);
        _movingSelection = true;
        _moveStart = p;
        var t = GetTranslate(hit);
        _moveBaseX = t.X;
        _moveBaseY = t.Y;
        Anno.CaptureMouse();
    }

    internal void Select(FrameworkElement el)
    {
        ClearSelection();
        _selected = el;
        _selectionBox = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x2E, 0x90, 0xFA)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false,
        };
        Anno.Children.Add(_selectionBox);
        UpdateSelectionBox();
    }

    internal void ClearSelection()
    {
        if (_selectionBox is not null) Anno.Children.Remove(_selectionBox);
        _selectionBox = null;
        _selected = null;
        _movingSelection = false;
    }

    private void UpdateSelectionBox()
    {
        if (_selected is null || _selectionBox is null) return;
        var bounds = _selected.TransformToAncestor(Anno).TransformBounds(
            new Rect(0, 0, Math.Max(1, _selected.ActualWidth), Math.Max(1, _selected.ActualHeight)));
        if (_selected is Shape s && (s is Line or Path or Polyline))
            bounds = s.TransformToAncestor(Anno).TransformBounds(VisualTreeHelper.GetContentBounds(s));
        Canvas.SetLeft(_selectionBox, bounds.X - 3);
        Canvas.SetTop(_selectionBox, bounds.Y - 3);
        _selectionBox.Width = bounds.Width + 6;
        _selectionBox.Height = bounds.Height + 6;
    }

    internal static TranslateTransform GetTranslate(FrameworkElement el)
    {
        if (el.RenderTransform is not TranslateTransform t)
        {
            t = new TranslateTransform();
            el.RenderTransform = t;
        }
        return t;
    }

    // ---------- undo actions ----------

    internal class AddElementAction : IUndoable
    {
        private readonly AnnotateWindow _w;
        private readonly UIElement _el;
        public AddElementAction(AnnotateWindow w, UIElement el) { _w = w; _el = el; }
        public void Undo() { _w.ClearSelection(); _w.Anno.Children.Remove(_el); _w.MarkDirty(); }
        public void Redo() { _w.Anno.Children.Add(_el); _w.MarkDirty(); }
    }

    internal class RemoveElementAction : IUndoable
    {
        private readonly AnnotateWindow _w;
        private readonly UIElement _el;
        private readonly int _index;
        public RemoveElementAction(AnnotateWindow w, UIElement el, int index) { _w = w; _el = el; _index = index; }
        public void Undo() { _w.Anno.Children.Insert(Math.Min(_index, _w.Anno.Children.Count), _el); _w.MarkDirty(); }
        public void Redo() { _w.ClearSelection(); _w.Anno.Children.Remove(_el); _w.MarkDirty(); }
    }

    internal class MoveAction : IUndoable
    {
        private readonly AnnotateWindow _w;
        private readonly FrameworkElement _el;
        private readonly double _dx, _dy;
        public MoveAction(AnnotateWindow w, FrameworkElement el, double dx, double dy) { _w = w; _el = el; _dx = dx; _dy = dy; }
        public void Undo() { var t = GetTranslate(_el); t.X -= _dx; t.Y -= _dy; _w.AfterMove(); }
        public void Redo() { var t = GetTranslate(_el); t.X += _dx; t.Y += _dy; _w.AfterMove(); }
    }

    internal void AfterMove()
    {
        UpdateSelectionBox();
        MarkDirty();
    }

    // ---------- zoom / pan ----------

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        var newScale = Math.Clamp(ZoomTransform.ScaleX * factor, 0.1, 8.0);
        var mouse = e.GetPosition(Scroll);
        var contentX = (Scroll.HorizontalOffset + mouse.X) / ZoomTransform.ScaleX;
        var contentY = (Scroll.VerticalOffset + mouse.Y) / ZoomTransform.ScaleY;
        ZoomTransform.ScaleX = newScale;
        ZoomTransform.ScaleY = newScale;
        Scroll.UpdateLayout();
        Scroll.ScrollToHorizontalOffset(contentX * newScale - mouse.X);
        Scroll.ScrollToVerticalOffset(contentY * newScale - mouse.Y);
    }

    private void OnScrollMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_spaceDown) return;
        _panning = true;
        _panStart = e.GetPosition(Scroll);
        _panStartH = Scroll.HorizontalOffset;
        _panStartV = Scroll.VerticalOffset;
        e.Handled = true;
    }

    private void OnScrollMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_panning || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(Scroll);
        Scroll.ScrollToHorizontalOffset(_panStartH - (p.X - _panStart.X));
        Scroll.ScrollToVerticalOffset(_panStartV - (p.Y - _panStart.Y));
        e.Handled = true;
    }

    private void FitToWindow()
    {
        var vw = Scroll.ViewportWidth;
        var vh = Scroll.ViewportHeight;
        if (vw < 1 || vh < 1 || _imgW < 1) return;
        var scale = Math.Min(1.0, Math.Min(vw / _imgW, vh / _imgH));
        ZoomTransform.ScaleX = scale;
        ZoomTransform.ScaleY = scale;
    }

    // ---------- keyboard ----------

    private static readonly Dictionary<Key, Tool> ToolShortcuts = new()
    {
        [Key.V] = Tool.Select,
        [Key.R] = Tool.Rect,
        [Key.O] = Tool.Ellipse,
        [Key.L] = Tool.Line,
        [Key.A] = Tool.Arrow,
        [Key.P] = Tool.Freehand,
        [Key.T] = Tool.Text,
        [Key.B] = Tool.Blur,
        [Key.X] = Tool.Pixelate,
        [Key.N] = Tool.Counter,
        [Key.C] = Tool.Crop,
    };

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        // While typing in a text annotation, only Ctrl+S passes through (it
        // commits the text via the focus change and saves).
        if (e.OriginalSource is TextBox && !(ctrl && e.Key == Key.S)) return;
        if (e.Key == Key.Space) { _spaceDown = true; Scroll.Cursor = Cursors.Hand; return; }
        if (!ctrl && !shift && ToolShortcuts.TryGetValue(e.Key, out var tool))
        {
            SelectTool(tool);
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.Z) { OnUndo(this, null!); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { OnRedo(this, null!); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.S) { OnSaveAs(this, null!); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { OnSave(this, null!); e.Handled = true; }
        else if (ctrl && e.Key == Key.C) { OnCopy(this, null!); e.Handled = true; }
        else if (ctrl && e.Key == Key.D0) { FitToWindow(); e.Handled = true; }
        else if (e.Key == Key.Delete && _selected is not null)
        {
            var el = _selected;
            var index = Anno.Children.IndexOf(el);
            ClearSelection();
            Anno.Children.Remove(el);
            if (el is Grid g && (string?)g.Tag == "counter")
            {
                RenumberBadges();
                _undo.Push(new RemoveCounterAction(this, el, index));
            }
            else
            {
                _undo.Push(new RemoveElementAction(this, el, index));
            }
            MarkDirty();
            e.Handled = true;
        }
    }

    // ---------- toolbar actions ----------

    private void OnPickColor(object sender, RoutedEventArgs e)
    {
        var popup = new Popup
        {
            PlacementTarget = ColorButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
        };
        var panel = new WrapPanel { Width = 4 * 26 + 8, Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)) };
        foreach (var c in Palette)
        {
            var b = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(c),
                BorderBrush = System.Windows.Media.Brushes.Gray,
            };
            b.Click += (_, _) =>
            {
                _color = c;
                ColorSwatch.Fill = new SolidColorBrush(c);
                RememberToolStyle();
                popup.IsOpen = false;
            };
            panel.Children.Add(b);
        }
        popup.Child = panel;
        popup.IsOpen = true;
    }

    private void OnWidthChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WidthCombo.SelectedItem is double w)
        {
            _strokeWidth = w;
            RememberToolStyle();
        }
    }

    private void OnUndo(object sender, RoutedEventArgs e) => _undo.Undo();
    private void OnRedo(object sender, RoutedEventArgs e) => _undo.Redo();

    private void UpdateUndoButtons()
    {
        BtnUndo.IsEnabled = _undo.CanUndo;
        BtnRedo.IsEnabled = _undo.CanRedo;
    }

    // ---------- text ----------

    private void BeginTextEntry(WpfPoint p) => ShowTextBox(p.X, p.Y, null);

    private void ShowTextBox(double x, double y, TextBlock? editing)
    {
        if (editing is not null) editing.Visibility = Visibility.Hidden;
        var box = new TextBox
        {
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)),
            Foreground = new SolidColorBrush(editing?.Foreground is SolidColorBrush sb ? sb.Color : _color),
            FontSize = editing?.FontSize ?? Math.Max(14, _strokeWidth * 6),
            FontWeight = FontWeights.SemiBold,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x90, 0xFA)),
            BorderThickness = new Thickness(1),
            MinWidth = 40,
            Text = editing?.Text ?? "",
        };
        InputMethod.SetIsInputMethodEnabled(box, true);
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);
        Anno.Children.Add(box);
        box.Loaded += (_, _) => { box.Focus(); box.CaretIndex = box.Text.Length; };

        var done = false;
        void Commit()
        {
            if (done) return;
            done = true;
            Anno.Children.Remove(box);
            var text = box.Text.Trim();
            if (editing is not null)
            {
                editing.Visibility = Visibility.Visible;
                if (text.Length > 0 && text != editing.Text)
                {
                    _undo.Push(new EditTextAction(this, editing, editing.Text, text));
                    editing.Text = text;
                    MarkDirty();
                }
                return;
            }
            if (text.Length == 0) return;
            var tb = MakeTextBlock(text, box.FontSize, ((SolidColorBrush)box.Foreground).Color);
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            Anno.Children.Add(tb);
            RememberToolStyle();
            _undo.Push(new AddElementAction(this, tb));
            MarkDirty();
        }

        box.LostFocus += (_, _) => Commit();
        box.PreviewKeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter) { Commit(); ke.Handled = true; }
            else if (ke.Key == Key.Escape)
            {
                done = true;
                Anno.Children.Remove(box);
                if (editing is not null) editing.Visibility = Visibility.Visible;
                ke.Handled = true;
            }
        };
    }

    private TextBlock MakeTextBlock(string text, double fontSize, Color color)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(color),
        };
        tb.MouseLeftButtonDown += (_, me) =>
        {
            if (me.ClickCount != 2) return;
            var t = GetTranslate(tb);
            ShowTextBox(Canvas.GetLeft(tb) + t.X, Canvas.GetTop(tb) + t.Y, tb);
            me.Handled = true;
        };
        return tb;
    }

    internal class EditTextAction : IUndoable
    {
        private readonly AnnotateWindow _w;
        private readonly TextBlock _tb;
        private readonly string _old, _new;
        public EditTextAction(AnnotateWindow w, TextBlock tb, string oldText, string newText)
        { _w = w; _tb = tb; _old = oldText; _new = newText; }
        public void Undo() { _tb.Text = _old; _w.MarkDirty(); }
        public void Redo() { _tb.Text = _new; _w.MarkDirty(); }
    }

    // ---------- counter ----------

    private void AddCounterBadge(WpfPoint p)
    {
        var n = CounterBadges().Count + 1;
        var d = 14 + _strokeWidth * 4;
        var badge = new Grid { Width = d, Height = d, Tag = "counter" };
        badge.Children.Add(new Ellipse
        {
            Fill = new SolidColorBrush(_color),
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
        });
        badge.Children.Add(new TextBlock
        {
            Text = n.ToString(),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = d * 0.5,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Canvas.SetLeft(badge, p.X - d / 2);
        Canvas.SetTop(badge, p.Y - d / 2);
        Anno.Children.Add(badge);
        RememberToolStyle();
        _undo.Push(new AddCounterAction(this, badge));
        MarkDirty();
    }

    private List<Grid> CounterBadges() =>
        Anno.Children.OfType<Grid>().Where(g => (string?)g.Tag == "counter").ToList();

    internal void RenumberBadges()
    {
        var i = 1;
        foreach (var g in CounterBadges())
            ((TextBlock)g.Children[1]).Text = (i++).ToString();
    }

    internal class AddCounterAction : IUndoable
    {
        private readonly AnnotateWindow _w;
        private readonly UIElement _el;
        public AddCounterAction(AnnotateWindow w, UIElement el) { _w = w; _el = el; }
        public void Undo() { _w.ClearSelection(); _w.Anno.Children.Remove(_el); _w.RenumberBadges(); _w.MarkDirty(); }
        public void Redo() { _w.Anno.Children.Add(_el); _w.RenumberBadges(); _w.MarkDirty(); }
    }

    internal class RemoveCounterAction : IUndoable
    {
        private readonly AnnotateWindow _w;
        private readonly UIElement _el;
        private readonly int _index;
        public RemoveCounterAction(AnnotateWindow w, UIElement el, int index) { _w = w; _el = el; _index = index; }
        public void Undo()
        {
            _w.Anno.Children.Insert(Math.Min(_index, _w.Anno.Children.Count), _el);
            _w.RenumberBadges();
            _w.MarkDirty();
        }
        public void Redo() { _w.ClearSelection(); _w.Anno.Children.Remove(_el); _w.RenumberBadges(); _w.MarkDirty(); }
    }

    // ---------- blur / pixelate ----------

    private void AddRegionEffect(Int32Rect region, bool pixelate)
    {
        var src = (BitmapSource)BaseImage.Source;
        var crop = new CroppedBitmap(src, region);
        var img = new Image { Stretch = Stretch.Fill, Width = region.Width, Height = region.Height };
        if (pixelate)
        {
            const double factor = 12.0;
            img.Source = new TransformedBitmap(crop, new ScaleTransform(1 / factor, 1 / factor));
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        }
        else
        {
            img.Source = crop;
            img.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 14 };
        }
        var host = new Border
        {
            Width = region.Width,
            Height = region.Height,
            ClipToBounds = true,
            Child = img,
        };
        Canvas.SetLeft(host, region.X);
        Canvas.SetTop(host, region.Y);
        Anno.Children.Add(host);
        _undo.Push(new AddElementAction(this, host));
        MarkDirty();
    }

    // ---------- crop ----------

    private Int32Rect? _cropRect;
    private readonly List<UIElement> _pendingCropUi = new();

    private void ShowCropConfirm(Int32Rect region)
    {
        CancelPendingCrop();
        var marquee = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x2E, 0x90, 0xFA)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Width = region.Width,
            Height = region.Height,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(marquee, region.X);
        Canvas.SetTop(marquee, region.Y);

        var apply = new Button { Content = Strings.Get("Crop_Apply"), Padding = new Thickness(8, 2, 8, 2) };
        var cancel = new Button { Content = Strings.Get("Crop_Cancel"), Padding = new Thickness(8, 2, 8, 2) };
        var bar = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        bar.Children.Add(apply);
        bar.Children.Add(cancel);
        Canvas.SetLeft(bar, Math.Max(0, Math.Min(_imgW - 140, region.X + region.Width - 140)));
        Canvas.SetTop(bar, Math.Min(_imgH - 28, region.Y + region.Height + 6));

        apply.Click += (_, _) =>
        {
            var old = _cropRect;
            CancelPendingCrop();
            _undo.Push(new CropAction(this, old, region));
            SetCrop(region);
            MarkDirty();
        };
        cancel.Click += (_, _) => CancelPendingCrop();

        _pendingCropUi.Add(marquee);
        _pendingCropUi.Add(bar);
        Anno.Children.Add(marquee);
        Anno.Children.Add(bar);
    }

    private void CancelPendingCrop()
    {
        foreach (var el in _pendingCropUi) Anno.Children.Remove(el);
        _pendingCropUi.Clear();
    }

    internal void SetCrop(Int32Rect? rect)
    {
        _cropRect = rect;
        CanvasRoot.Clip = rect is { } r
            ? new RectangleGeometry(new Rect(r.X, r.Y, r.Width, r.Height))
            : null;
    }

    internal class CropAction : IUndoable
    {
        private readonly AnnotateWindow _w;
        private readonly Int32Rect? _old, _new;
        public CropAction(AnnotateWindow w, Int32Rect? oldRect, Int32Rect? newRect) { _w = w; _old = oldRect; _new = newRect; }
        public void Undo() { _w.SetCrop(_old); _w.MarkDirty(); }
        public void Redo() { _w.SetCrop(_new); _w.MarkDirty(); }
    }

    // ---------- export ----------

    internal BitmapSource RenderComposite()
    {
        CancelPendingCrop();
        ClearSelection();
        var oldScale = ZoomTransform.ScaleX;
        ZoomTransform.ScaleX = 1;
        ZoomTransform.ScaleY = 1;
        CanvasRoot.UpdateLayout();
        var rtb = new RenderTargetBitmap(_imgW, _imgH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(CanvasRoot);
        ZoomTransform.ScaleX = oldScale;
        ZoomTransform.ScaleY = oldScale;
        CanvasRoot.UpdateLayout();
        return ApplyExportCrop(rtb);
    }

    private BitmapSource ApplyExportCrop(BitmapSource source) =>
        _cropRect is { } r && r.Width > 0 && r.Height > 0 ? new CroppedBitmap(source, r) : source;

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        Focus(); // commits any pending text annotation via LostFocus
        System.Windows.Clipboard.SetImage(RenderComposite());
        AppActions.Tray?.Balloon("Snapzy", Strings.Get("Toast_CopiedToClipboard"));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Focus(); // commits any pending text annotation via LostFocus
        SaveTo(_imagePath);
        _dirty = false;
        UpdateTitle();
    }

    private void OnSaveAs(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG|*.png|JPEG|*.jpg",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_imagePath) + "-annotated.png",
            InitialDirectory = AppPaths.CapturesDir,
        };
        if (dlg.ShowDialog() != true) return;
        SaveTo(dlg.FileName);
    }

    private void SaveTo(string path)
    {
        try
        {
            var composite = RenderComposite();
            BitmapEncoder encoder = System.IO.Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg"
                ? new JpegBitmapEncoder { QualityLevel = 90 }
                : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(composite));
            using var fs = File.Create(path);
            encoder.Save(fs);
            Log.Info($"Annotated image saved: {path}");
        }
        catch (Exception ex)
        {
            Log.Error("Annotate save failed", ex);
            System.Windows.MessageBox.Show(this, ex.Message, "Snapzy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    internal void MarkDirty()
    {
        _dirty = true;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        Title = System.IO.Path.GetFileName(_imagePath) + (_dirty ? " *" : "") + " - " + Strings.Get("Annotate_Title");
    }

    private void OnClosingPrompt(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_dirty) return;
        var pick = System.Windows.MessageBox.Show(this, Strings.Get("Annotate_UnsavedPrompt"),
            Strings.Get("Annotate_Title"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (pick == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (pick == MessageBoxResult.Yes) { SaveTo(_imagePath); }
    }
}

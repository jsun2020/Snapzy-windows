using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Snapzy.Core;
using Snapzy.Core.History;
using Snapzy.Core.Localization;
using Snapzy.Core.Settings;

namespace Snapzy.App.QuickAccess;

public partial class QuickAccessWindow : Window
{
    private static QuickAccessWindow? _current;

    private readonly HistoryEntry _entry;
    private readonly HistoryStore _store;
    private readonly DispatcherTimer _timer;
    private System.Windows.Point _dragOrigin;

    private QuickAccessWindow(HistoryEntry entry, HistoryStore store, AppSettings settings)
    {
        InitializeComponent();
        _entry = entry;
        _store = store;

        BtnCopy.Content = Strings.Get("QA_Copy");
        BtnAnnotate.Content = Strings.Get("QA_Annotate");
        BtnOpen.Content = Strings.Get("QA_Open");
        BtnFolder.Content = Strings.Get("QA_Folder");
        BtnDelete.Content = Strings.Get("QA_Delete");
        BtnAnnotate.IsEnabled = entry.Type == "image";

        Caption.Text = entry.FileName;
        Thumb.Source = ThumbnailLoader.GetThumb(entry, store);

        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 16;
        Top = wa.Bottom - Height - 16;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(3, settings.QuickAccessTimeoutSeconds)),
        };
        _timer.Tick += (_, _) => Close();
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => _timer.Start();
        Closed += (_, _) => { _timer.Stop(); if (_current == this) _current = null; };

        Thumb.MouseLeftButtonDown += (_, e) => _dragOrigin = e.GetPosition(this);
        Thumb.MouseMove += OnThumbMouseMove;

        Opacity = 0;
        Loaded += (_, _) =>
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    public static void ShowFor(HistoryEntry entry, HistoryStore store, AppSettings settings)
    {
        _current?.Close();
        _current = new QuickAccessWindow(entry, store, settings);
        _current.Show();
        _current._timer.Start();
    }

    private void OnThumbMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragOrigin.X) < 4 && Math.Abs(pos.Y - _dragOrigin.Y) < 4) return;
        var path = _store.GetFullPath(_entry);
        if (!File.Exists(path)) return;
        _timer.Stop();
        DragDrop.DoDragDrop(Thumb,
            new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { path }),
            System.Windows.DragDropEffects.Copy);
        _timer.Start();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        var path = _store.GetFullPath(_entry);
        if (_entry.Type == "image")
            CaptureFlow.CopyImageToClipboard(path);
        else
        {
            var files = new System.Collections.Specialized.StringCollection { path };
            System.Windows.Forms.Clipboard.SetFileDropList(files);
        }
        AppActions.Tray?.Balloon("Snapzy", Strings.Get("Toast_CopiedToClipboard"));
        Close();
    }

    private void OnAnnotate(object sender, RoutedEventArgs e)
    {
        AppActions.OpenAnnotate(_store.GetFullPath(_entry));
        Close();
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(_store.GetFullPath(_entry)) { UseShellExecute = true });
        Close();
    }

    private void OnFolder(object sender, RoutedEventArgs e)
    {
        Process.Start("explorer.exe", $"/select,\"{_store.GetFullPath(_entry)}\"");
        Close();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        _store.Delete(_entry.Id);
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

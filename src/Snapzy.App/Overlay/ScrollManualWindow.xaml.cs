using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Snapzy.Core.Capture;
using Snapzy.Core.Localization;

namespace Snapzy.App.Overlay;

/// <summary>
/// Control panel for the user-driven long screenshot: shows tracking status and
/// Auto / Save / Cancel. Excluded from screen capture (so it can never end up
/// in the stitched image) and created WS_EX_NOACTIVATE so clicking its buttons
/// never steals focus from the window the user is scrolling.
/// </summary>
public partial class ScrollManualWindow : Window
{
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    public volatile bool SaveRequested;
    public volatile bool Cancelled;
    public volatile bool AutoScroll;

    public ScrollManualWindow()
    {
        InitializeComponent();
        StatusText.Text = Strings.Get("Scroll_ManualHint");
        BtnAuto.Content = Strings.Get("Scroll_AutoBtn");
        BtnSave.Content = Strings.Get("Scroll_Save");
        BtnCancel.Content = Strings.Get("Scroll_Cancel");
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            // NOTE: the window must stay non-layered (no AllowsTransparency) or
            // the capture exclusion fails silently on layered windows.
            if (Environment.OSVersion.Version.Build >= 19041)
                SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        };
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 16;
            Top = wa.Top + 16;
        };
    }

    public void SetProgress(ManualScrollCapture.TrackState state, int steps, bool full)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = full ? Strings.Get("Scroll_MaxLength")
                : state == ManualScrollCapture.TrackState.Lost ? Strings.Get("Scroll_LostTrack")
                : steps > 0 ? string.Format(Strings.Get("Scroll_TrackingFmt"), steps)
                : Strings.Get("Scroll_ManualHint");
        });
    }

    private void OnAuto(object sender, RoutedEventArgs e)
    {
        AutoScroll = !AutoScroll;
        BtnAuto.Background = AutoScroll
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x0A, 0x84, 0xFF))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    }

    private void OnSave(object sender, RoutedEventArgs e) => SaveRequested = true;

    private void OnCancel(object sender, RoutedEventArgs e) => Cancelled = true;
}

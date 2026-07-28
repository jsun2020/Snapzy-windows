using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Snapzy.Core.Localization;

namespace Snapzy.App.Overlay;

public partial class ScrollProgressWindow : Window
{
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    public volatile bool Cancelled;

    public ScrollProgressWindow()
    {
        InitializeComponent();
        ProgressText.Text = Strings.Get("Scroll_Progress");
        BtnCancel.Content = Strings.Get("Scroll_Cancel");
        SourceInitialized += (_, _) =>
        {
            // The toast overlaps the captured client area of maximized
            // windows; its per-step text change would poison the frame
            // comparison and end up baked into the stitched image. Exclude
            // it from screen capture entirely (Win10 2004+).
            if (Environment.OSVersion.Version.Build >= 19041)
                SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, WDA_EXCLUDEFROMCAPTURE);
        };
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 16;
            Top = wa.Top + 16;
        };
    }

    public void SetStep(int step) =>
        Dispatcher.Invoke(() => ProgressText.Text = Strings.Get("Scroll_Progress") + " " + step);

    private void OnCancel(object sender, RoutedEventArgs e) => Cancelled = true;
}

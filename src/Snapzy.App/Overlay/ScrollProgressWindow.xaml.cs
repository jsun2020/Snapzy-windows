using System.Windows;
using Snapzy.Core.Localization;

namespace Snapzy.App.Overlay;

public partial class ScrollProgressWindow : Window
{
    public volatile bool Cancelled;

    public ScrollProgressWindow()
    {
        InitializeComponent();
        ProgressText.Text = Strings.Get("Scroll_Progress");
        BtnCancel.Content = Strings.Get("Scroll_Cancel");
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

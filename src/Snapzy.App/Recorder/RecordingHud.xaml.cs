using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Snapzy.Core.Localization;
using DrawingRectangle = System.Drawing.Rectangle;
using Color = System.Windows.Media.Color;

namespace Snapzy.App.Recorder;

public partial class RecordingHud : Window
{
    private readonly DrawingRectangle _recordedRect; // physical px
    private readonly Func<Task<bool>> _onPauseResume; // returns true when now paused
    private readonly Action _onStop;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _watch = new();
    private TimeSpan _accumulated = TimeSpan.Zero;
    private bool _paused;

    public RecordingHud(DrawingRectangle recordedRect, Func<Task<bool>> onPauseResume, Action onStop)
    {
        InitializeComponent();
        _recordedRect = recordedRect;
        _onPauseResume = onPauseResume;
        _onStop = onStop;
        BtnPause.Content = Strings.Get("Hud_Pause");
        BtnStop.Content = Strings.Get("Hud_Stop");
        _watch.Start();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => UpdateTimer();
        _timer.Start();
        Loaded += (_, _) => Position();
        Closed += (_, _) => _timer.Stop();
    }

    private void UpdateTimer()
    {
        var elapsed = _accumulated + _watch.Elapsed;
        TimerText.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
        if (!_paused)
            RedDot.Opacity = RedDot.Opacity > 0.5 ? 0.35 : 1.0;
    }

    private void Position()
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var x = _recordedRect.X / dpi;
        var y = _recordedRect.Y / dpi;
        var w = _recordedRect.Width / dpi;
        var h = _recordedRect.Height / dpi;
        var wa = SystemParameters.WorkArea;

        Left = Math.Max(wa.Left, Math.Min(x + (w - ActualWidth) / 2, wa.Right - ActualWidth));
        if (y - ActualHeight - 8 >= wa.Top) Top = y - ActualHeight - 8;          // above the rect
        else if (y + h + 8 + ActualHeight <= wa.Bottom) Top = y + h + 8;         // below the rect
        else { Top = wa.Top + 8; Left = wa.Right - ActualWidth - 8; }            // fallback: top-right
    }

    private async void OnPauseResume(object sender, RoutedEventArgs e)
    {
        try
        {
            var nowPaused = await _onPauseResume();
            _paused = nowPaused;
            if (nowPaused)
            {
                _accumulated += _watch.Elapsed;
                _watch.Reset();
                RedDot.Fill = new SolidColorBrush(Color.FromRgb(0xFB, 0x8C, 0x00));
                RedDot.Opacity = 1.0;
                BtnPause.Content = Strings.Get("Hud_Resume");
            }
            else
            {
                _watch.Start();
                RedDot.Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
                BtnPause.Content = Strings.Get("Hud_Pause");
            }
        }
        catch (Exception ex)
        {
            Snapzy.Core.Log.Error("Pause/resume failed", ex);
        }
    }

    private void OnStop(object sender, RoutedEventArgs e) => _onStop();
}

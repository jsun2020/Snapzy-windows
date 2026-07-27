using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Snapzy.Core;
using Snapzy.Core.Hotkeys;
using Snapzy.Core.Localization;
using Snapzy.Core.Recording;
using Snapzy.Core.Settings;
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Color = System.Windows.Media.Color;

namespace Snapzy.App.SettingsUI;

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _current;

    private readonly AppSettings _settings;
    private readonly Action _onSaved;
    private readonly Dictionary<string, (HotkeyCaptureBox Box, CheckBox Enabled, Border Row)> _hotkeyRows = new();

    // Dictionary keys are stable setting values; combo indexes map through these.
    private static readonly string[] LanguageValues = { "en", "zh-CN" };
    private static readonly string[] ThemeValues = { "light", "dark", "system" };
    private static readonly string[] FormatValues = { "png", "jpg", "webp" };
    private static readonly string[] FsModeValues = { "currentMonitor", "allMonitors" };
    private static readonly int[] FpsValues = { 15, 24, 30, 60 };
    private static readonly string[] OutputValues = { "mp4", "gif", "webp", "both", "mp4+webp" };
    private static readonly int[] RetentionValues = { 0, 7, 30, 90 };

    private SettingsWindow(AppSettings settings, Action onSaved)
    {
        InitializeComponent();
        _settings = settings;
        _onSaved = onSaved;
        ApplyStrings();
        LoadValues();
        Closed += (_, _) => { if (_current == this) _current = null; };
    }

    public static void Open(AppSettings settings, Action onSaved)
    {
        if (_current is not null)
        {
            _current.Activate();
            return;
        }
        _current = new SettingsWindow(settings, onSaved);
        _current.Show();
        _current.Activate();
    }

    private void ApplyStrings()
    {
        Title = Strings.Get("Set_Title");
        TabGeneral.Header = Strings.Get("Set_TabGeneral");
        TabCapture.Header = Strings.Get("Set_TabCapture");
        TabRecording.Header = Strings.Get("Set_TabRecording");
        TabHotkeys.Header = Strings.Get("Set_TabHotkeys");
        TabHistory.Header = Strings.Get("Set_TabHistory");
        TabAbout.Header = Strings.Get("Set_TabAbout");
        BtnSave.Content = Strings.Get("Set_Save");
        BtnCancel.Content = Strings.Get("Set_Cancel");

        LblLanguage.Text = Strings.Get("Set_Language");
        LblTheme.Text = Strings.Get("Set_Theme");
        LblThemeNote.Text = Strings.Get("Set_ThemeNote");
        ChkLaunchAtLogin.Content = Strings.Get("Set_LaunchAtLogin");
        ChkTrayLeftClick.Content = Strings.Get("Set_TrayLeftClick");

        LblFormat.Text = Strings.Get("Set_ImageFormat");
        LblFsMode.Text = Strings.Get("Set_FullscreenMode");
        LblShotActions.Text = Strings.Get("Set_ScreenshotActions");
        ChkShotClipboard.Content = Strings.Get("Set_CopyToClipboard");
        ChkShotQa.Content = Strings.Get("Set_ShowQuickAccess");
        ChkShotAnnotate.Content = Strings.Get("Set_OpenAnnotate");
        LblRecActions.Text = Strings.Get("Set_RecordingActions");
        ChkRecQa.Content = Strings.Get("Set_ShowQuickAccess");
        LblQaTimeout.Text = Strings.Get("Set_QaTimeout");

        LblFps.Text = Strings.Get("Set_Fps");
        LblOutput.Text = Strings.Get("Set_Output");
        ChkCursor.Content = Strings.Get("Set_RecordCursor");
        ChkSystemAudio.Content = Strings.Get("Set_SystemAudio");
        LblMic.Text = Strings.Get("Set_MicDevice");
        BtnRefreshMics.Content = Strings.Get("Set_Refresh");

        BtnRestoreDefaults.Content = Strings.Get("Set_RestoreDefaults");
        LblRetention.Text = Strings.Get("Set_Retention");
        BtnOpenCaptures.Content = Strings.Get("Set_OpenCaptures");
        BtnOpenLogs.Content = Strings.Get("Set_OpenLogs");
        LblVersion.Text = Strings.Get("Set_Version") + ": " +
            (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
    }

    private void LoadValues()
    {
        // Each language displayed in itself; escaped form of Jian Ti Zhong Wen (Simplified Chinese)
        LanguageCombo.Items.Add("English");
        LanguageCombo.Items.Add("简体中文");
        LanguageCombo.SelectedIndex = Math.Max(0, Array.IndexOf(LanguageValues, _settings.Language));

        foreach (var key in new[] { "Set_ThemeLight", "Set_ThemeDark", "Set_ThemeSystem" })
            ThemeCombo.Items.Add(Strings.Get(key));
        ThemeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ThemeValues, _settings.Theme));

        ChkLaunchAtLogin.IsChecked = _settings.LaunchAtLogin;
        ChkTrayLeftClick.IsChecked = _settings.TrayLeftClickAreaCapture;

        foreach (var f in FormatValues) FormatCombo.Items.Add(f.ToUpperInvariant());
        FormatCombo.SelectedIndex = Math.Max(0, Array.IndexOf(FormatValues, _settings.ImageFormat));

        FsModeCombo.Items.Add(Strings.Get("Set_FsCurrentMonitor"));
        FsModeCombo.Items.Add(Strings.Get("Set_FsAllMonitors"));
        FsModeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(FsModeValues, _settings.FullscreenMode));

        ChkShotClipboard.IsChecked = _settings.Screenshot.CopyToClipboard;
        ChkShotQa.IsChecked = _settings.Screenshot.ShowQuickAccess;
        ChkShotAnnotate.IsChecked = _settings.Screenshot.OpenAnnotate;
        ChkRecQa.IsChecked = _settings.Recording.ShowQuickAccess;
        QaTimeoutSlider.Value = _settings.QuickAccessTimeoutSeconds;
        LblQaTimeoutValue.Text = _settings.QuickAccessTimeoutSeconds + "s";

        foreach (var f in FpsValues) FpsCombo.Items.Add(f.ToString());
        FpsCombo.SelectedIndex = Math.Max(0, Array.IndexOf(FpsValues, _settings.RecordingFps));

        OutputCombo.Items.Add("MP4");
        OutputCombo.Items.Add("GIF");
        OutputCombo.Items.Add("WebP");
        OutputCombo.Items.Add("MP4 + GIF");
        OutputCombo.Items.Add("MP4 + WebP");
        OutputCombo.SelectedIndex = Math.Max(0, Array.IndexOf(OutputValues, _settings.RecordingOutput));

        ChkCursor.IsChecked = _settings.RecordCursor;
        ChkSystemAudio.IsChecked = _settings.RecordSystemAudio;
        PopulateMics();

        BuildHotkeyRows();

        RetentionCombo.Items.Add(Strings.Get("Set_RetForever"));
        RetentionCombo.Items.Add(Strings.Get("Set_Ret7"));
        RetentionCombo.Items.Add(Strings.Get("Set_Ret30"));
        RetentionCombo.Items.Add(Strings.Get("Set_Ret90"));
        RetentionCombo.SelectedIndex = Math.Max(0, Array.IndexOf(RetentionValues, _settings.RetentionDays));

        UpdateDiskUsage();
    }

    private void PopulateMics()
    {
        var selected = _settings.MicDevice;
        MicCombo.Items.Clear();
        MicCombo.Items.Add(Strings.Get("Set_MicNone"));
        MicCombo.SelectedIndex = 0;
        if (File.Exists(AppPaths.FfmpegExe))
        {
            foreach (var device in FfmpegDevices.ListDshowAudio(AppPaths.FfmpegExe))
            {
                MicCombo.Items.Add(device);
                if (device == selected) MicCombo.SelectedIndex = MicCombo.Items.Count - 1;
            }
        }
    }

    private void BuildHotkeyRows()
    {
        HotkeyRows.Children.Clear();
        _hotkeyRows.Clear();
        foreach (var (action, binding) in _settings.Hotkeys)
        {
            var label = new TextBlock
            {
                Text = Strings.Get("Action_" + action),
                Width = 180,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var box = new HotkeyCaptureBox { Gesture = binding.Gesture, Width = 160, Margin = new Thickness(8, 0, 8, 0) };
            var enabled = new CheckBox { IsChecked = binding.Enabled, VerticalAlignment = VerticalAlignment.Center };
            box.GestureChanged += UpdateConflictHighlight;
            enabled.Checked += (_, _) => UpdateConflictHighlight();
            enabled.Unchecked += (_, _) => UpdateConflictHighlight();

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(label);
            panel.Children.Add(box);
            panel.Children.Add(enabled);
            var row = new Border { Padding = new Thickness(4), Child = panel };
            _hotkeyRows[action] = (box, enabled, row);
            HotkeyRows.Children.Add(row);
        }
        UpdateConflictHighlight();
    }

    private void UpdateConflictHighlight()
    {
        var map = _hotkeyRows.ToDictionary(
            kv => kv.Key,
            kv => new HotkeyBinding { Gesture = kv.Value.Box.Gesture, Enabled = kv.Value.Enabled.IsChecked == true });
        var dups = HotkeyConflicts.FindDuplicates(map);
        var conflicted = dups.SelectMany(d => new[] { d.A, d.B }).ToHashSet();
        foreach (var (action, row) in _hotkeyRows)
        {
            var isBad = conflicted.Contains(action);
            row.Row.Background = isBad ? new SolidColorBrush(Color.FromArgb(0x50, 0xE5, 0x39, 0x35)) : Brushes.Transparent;
            row.Row.ToolTip = isBad ? Strings.Get("Set_HotkeyConflict") : null;
        }
        BtnSave.IsEnabled = conflicted.Count == 0;
    }

    private void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        var defaults = AppSettings.CreateDefault().Hotkeys;
        foreach (var (action, row) in _hotkeyRows)
        {
            if (!defaults.TryGetValue(action, out var d)) continue;
            row.Box.Gesture = d.Gesture;
            row.Enabled.IsChecked = d.Enabled;
        }
        UpdateConflictHighlight();
    }

    private void OnQaTimeoutChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LblQaTimeoutValue is not null)
            LblQaTimeoutValue.Text = (int)e.NewValue + "s";
    }

    private void OnRefreshMics(object sender, RoutedEventArgs e) => PopulateMics();

    private void OnOpenCaptures(object sender, RoutedEventArgs e) =>
        Process.Start("explorer.exe", AppPaths.CapturesDir);

    private void OnOpenLogs(object sender, RoutedEventArgs e) =>
        Process.Start("explorer.exe", AppPaths.LogsDir);

    private void OnRepoLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e) =>
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });

    private void UpdateDiskUsage()
    {
        try
        {
            var bytes = Directory.Exists(AppPaths.CapturesDir)
                ? new DirectoryInfo(AppPaths.CapturesDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                : 0;
            LblDiskUsage.Text = Strings.Get("Set_DiskUsage") + ": " + (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        }
        catch (IOException) { LblDiskUsage.Text = ""; }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _settings.Language = LanguageValues[Math.Max(0, LanguageCombo.SelectedIndex)];
        _settings.Theme = ThemeValues[Math.Max(0, ThemeCombo.SelectedIndex)];
        _settings.LaunchAtLogin = ChkLaunchAtLogin.IsChecked == true;
        _settings.TrayLeftClickAreaCapture = ChkTrayLeftClick.IsChecked == true;
        _settings.ImageFormat = FormatValues[Math.Max(0, FormatCombo.SelectedIndex)];
        _settings.FullscreenMode = FsModeValues[Math.Max(0, FsModeCombo.SelectedIndex)];
        _settings.Screenshot.CopyToClipboard = ChkShotClipboard.IsChecked == true;
        _settings.Screenshot.ShowQuickAccess = ChkShotQa.IsChecked == true;
        _settings.Screenshot.OpenAnnotate = ChkShotAnnotate.IsChecked == true;
        _settings.Recording.ShowQuickAccess = ChkRecQa.IsChecked == true;
        _settings.QuickAccessTimeoutSeconds = (int)QaTimeoutSlider.Value;
        _settings.RecordingFps = FpsValues[Math.Max(0, FpsCombo.SelectedIndex)];
        _settings.RecordingOutput = OutputValues[Math.Max(0, OutputCombo.SelectedIndex)];
        _settings.RecordCursor = ChkCursor.IsChecked == true;
        _settings.RecordSystemAudio = ChkSystemAudio.IsChecked == true;
        _settings.MicDevice = MicCombo.SelectedIndex <= 0 ? "" : (string)MicCombo.SelectedItem!;
        _settings.RetentionDays = RetentionValues[Math.Max(0, RetentionCombo.SelectedIndex)];
        foreach (var (action, row) in _hotkeyRows)
        {
            _settings.Hotkeys[action].Gesture = row.Box.Gesture;
            _settings.Hotkeys[action].Enabled = row.Enabled.IsChecked == true;
        }

        SettingsStore.Save(_settings, AppPaths.SettingsFile);
        Strings.SetLanguage(_settings.Language);
        StartupShortcut.SetEnabled(_settings.LaunchAtLogin);
        _onSaved();
        Log.Info("Settings saved");
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}

using System.IO;
using System.Windows.Forms;
using Snapzy.Core;
using Snapzy.Core.Localization;

namespace Snapzy.App.Tray;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private bool _recording;

    public TrayIcon()
    {
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Snapzy",
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && AppActions.Settings.TrayLeftClickAreaCapture)
                AppActions.CaptureArea();
        };
        RebuildMenu();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var path = Path.Combine(AppPaths.BaseDir, "Assets", "snapzy.ico");
        if (File.Exists(path)) return new System.Drawing.Icon(path);
        return System.Drawing.SystemIcons.Application;
    }

    public void RebuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(Strings.Get("Tray_CaptureArea"), null, (_, _) => AppActions.CaptureArea());
        menu.Items.Add(Strings.Get("Tray_CaptureFullscreen"), null, (_, _) => AppActions.CaptureFullscreen());
        menu.Items.Add(Strings.Get("Tray_CaptureAreaAnnotate"), null, (_, _) => AppActions.CaptureAreaAnnotate());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.Get(_recording ? "Tray_StopRecording" : "Tray_RecordScreen"), null, (_, _) => AppActions.ToggleRecording());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.Get("Tray_Annotate"), null, (_, _) => AppActions.OpenAnnotate(null));
        menu.Items.Add(Strings.Get("Tray_History"), null, (_, _) => AppActions.OpenHistory());
        menu.Items.Add(Strings.Get("Tray_Settings"), null, (_, _) => AppActions.OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.Get("Tray_Quit"), null, (_, _) => AppActions.Quit());
        _icon.ContextMenuStrip?.Dispose();
        _icon.ContextMenuStrip = menu;
    }

    public void Show() => _icon.Visible = true;

    public void Balloon(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.None);

    public void SetRecording(bool recording)
    {
        _recording = recording;
        _icon.Text = recording ? "Snapzy - " + Strings.Get("Tray_StopRecording") : "Snapzy";
        RebuildMenu();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}

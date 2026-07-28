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
        menu.Items.Add(Item("Tray_CaptureArea", "CaptureArea", (_, _) => AppActions.CaptureArea()));
        menu.Items.Add(Item("Tray_CaptureFullscreen", "CaptureFullscreen", (_, _) => AppActions.CaptureFullscreen()));
        menu.Items.Add(Item("Tray_CaptureAreaAnnotate", "CaptureAreaAnnotate", (_, _) => AppActions.CaptureAreaAnnotate()));
        menu.Items.Add(Item("Tray_ScrollCapture", null, (_, _) => AppActions.CaptureScrolling()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item(_recording ? "Tray_StopRecording" : "Tray_RecordScreen", "RecordToggle", (_, _) => AppActions.ToggleRecording()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Tray_Annotate", "OpenAnnotate", (_, _) => AppActions.OpenAnnotate(null)));
        menu.Items.Add(Item("Tray_History", "OpenHistory", (_, _) => AppActions.OpenHistory()));
        menu.Items.Add(Item("Tray_Settings", null, (_, _) => AppActions.OpenSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Tray_Quit", null, (_, _) => AppActions.Quit()));
        _icon.ContextMenuStrip?.Dispose();
        _icon.ContextMenuStrip = menu;
    }

    private static ToolStripMenuItem Item(string labelKey, string? hotkeyAction, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(Strings.Get(labelKey), null, onClick);
        if (hotkeyAction is not null && AppActions.Settings.HotkeyHint(hotkeyAction) is { } gesture)
            item.ShortcutKeyDisplayString = gesture;
        return item;
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

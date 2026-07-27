using System.IO;
using Snapzy.Core;

namespace Snapzy.App;

/// <summary>
/// Launch-at-login via a shortcut in the user's Startup folder - the only
/// artifact Snapzy ever writes outside its own folder (opt-in, default off).
/// </summary>
public static class StartupShortcut
{
    private static string LnkPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Snapzy.lnk");

    public static bool IsEnabled() => File.Exists(LnkPath);

    public static void SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(LnkPath)) File.Delete(LnkPath);
                return;
            }
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell unavailable");
            dynamic shell = Activator.CreateInstance(shellType)!;
            var shortcut = shell.CreateShortcut(LnkPath);
            shortcut.TargetPath = Path.Combine(AppPaths.BaseDir, "Snapzy.exe");
            shortcut.WorkingDirectory = AppPaths.BaseDir;
            shortcut.Description = "Snapzy screenshot tool";
            shortcut.Save();
        }
        catch (Exception ex)
        {
            Log.Error("Startup shortcut update failed", ex);
        }
    }
}

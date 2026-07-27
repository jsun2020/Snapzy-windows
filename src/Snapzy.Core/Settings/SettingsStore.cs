using System.Text.Json;

namespace Snapzy.Core.Settings;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppSettings Load(string file)
    {
        if (!File.Exists(file)) return AppSettings.CreateDefault();
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), Opts);
            if (s is null) throw new JsonException("null settings");
            foreach (var (k, v) in AppSettings.CreateDefault().Hotkeys)
                s.Hotkeys.TryAdd(k, v); // forward-compat: new actions get defaults
            return s;
        }
        catch (Exception)
        {
            File.Move(file, file + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), overwrite: true);
            return AppSettings.CreateDefault();
        }
    }

    public static void Save(AppSettings settings, string file)
    {
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Opts));
        File.Move(tmp, file, overwrite: true);
    }
}

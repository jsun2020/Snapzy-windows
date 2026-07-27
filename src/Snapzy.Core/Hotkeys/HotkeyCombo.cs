namespace Snapzy.Core.Hotkeys;

public class HotkeyCombo
{
    public uint Modifiers { get; private set; }   // MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8
    public uint VirtualKey { get; private set; }
    private string _keyName = "";

    private static readonly Dictionary<string, uint> Keys = BuildKeyMap();

    private static Dictionary<string, uint> BuildKeyMap()
    {
        var m = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (var c = 'A'; c <= 'Z'; c++) m[c.ToString()] = (uint)c;
        for (var c = '0'; c <= '9'; c++) m[c.ToString()] = (uint)c;
        for (var i = 1; i <= 24; i++) m["F" + i] = (uint)(0x6F + i); // F1=0x70
        m["PrintScreen"] = 0x2C; m["Space"] = 0x20; m["Tab"] = 0x09;
        m["Home"] = 0x24; m["End"] = 0x23; m["PageUp"] = 0x21; m["PageDown"] = 0x22;
        m["Insert"] = 0x2D; m["Delete"] = 0x2E; m["Up"] = 0x26; m["Down"] = 0x28;
        m["Left"] = 0x25; m["Right"] = 0x27;
        return m;
    }

    public static bool TryParse(string gesture, out HotkeyCombo combo)
    {
        combo = new HotkeyCombo();
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        var parts = gesture.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Any(string.IsNullOrEmpty)) return false;
        for (var i = 0; i < parts.Length; i++)
        {
            var isLast = i == parts.Length - 1;
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": combo.Modifiers |= 2; continue;
                case "alt": combo.Modifiers |= 1; continue;
                case "shift": combo.Modifiers |= 4; continue;
                case "win": combo.Modifiers |= 8; continue;
            }
            if (!isLast || !Keys.TryGetValue(parts[i], out var vk)) return false;
            combo.VirtualKey = vk;
            combo._keyName = Keys.Keys.First(k => string.Equals(k, parts[i], StringComparison.OrdinalIgnoreCase));
        }
        return combo.VirtualKey != 0;
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        if ((Modifiers & 4) != 0) parts.Add("Shift");
        if ((Modifiers & 8) != 0) parts.Add("Win");
        parts.Add(_keyName);
        return string.Join("+", parts);
    }
}

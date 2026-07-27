using Snapzy.Core.Settings;

namespace Snapzy.Core.Hotkeys;

public static class HotkeyConflicts
{
    public static List<(string A, string B)> FindDuplicates(IReadOnlyDictionary<string, HotkeyBinding> map)
    {
        var result = new List<(string, string)>();
        var seen = new Dictionary<string, string>(); // normalized gesture -> action
        foreach (var (action, binding) in map)
        {
            if (!binding.Enabled || !HotkeyCombo.TryParse(binding.Gesture, out var combo)) continue;
            var norm = combo.ToString();
            if (seen.TryGetValue(norm, out var other)) result.Add((other, action));
            else seen[norm] = action;
        }
        return result;
    }
}

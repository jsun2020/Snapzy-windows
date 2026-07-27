using System.Runtime.InteropServices;
using System.Windows.Interop;
using Snapzy.Core;
using Snapzy.Core.Hotkeys;
using Snapzy.Core.Settings;

namespace Snapzy.App.Hotkeys;

public sealed class HotkeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly Dictionary<string, Action> _actionMap;
    private readonly Dictionary<int, string> _idToAction = new();
    private readonly HwndSource _source;
    private int _nextId = 1;

    public HotkeyManager(Dictionary<string, Action> actionMap)
    {
        _actionMap = actionMap;
        var p = new HwndSourceParameters("SnapzyHotkeys")
        {
            Width = 0,
            Height = 0,
            ParentWindow = HWND_MESSAGE,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public List<string> RegisterAll(Dictionary<string, HotkeyBinding> bindings)
    {
        var failed = new List<string>();
        foreach (var (action, binding) in bindings)
        {
            if (!binding.Enabled) continue;
            if (!_actionMap.ContainsKey(action)) continue;
            if (!HotkeyCombo.TryParse(binding.Gesture, out var combo))
            {
                failed.Add(action);
                continue;
            }
            var id = _nextId++;
            if (RegisterHotKey(_source.Handle, id, combo.Modifiers | MOD_NOREPEAT, combo.VirtualKey))
                _idToAction[id] = action;
            else
                failed.Add(action);
        }
        return failed;
    }

    public void UnregisterAll()
    {
        foreach (var id in _idToAction.Keys)
            UnregisterHotKey(_source.Handle, id);
        _idToAction.Clear();
    }

    public List<string> Reregister(Dictionary<string, HotkeyBinding> bindings)
    {
        UnregisterAll();
        return RegisterAll(bindings);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _idToAction.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            try
            {
                _actionMap[action]();
            }
            catch (Exception ex)
            {
                Log.Error($"Hotkey action {action} failed", ex);
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}

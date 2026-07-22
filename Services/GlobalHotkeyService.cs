using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using HardwareScope.Models;

namespace HardwareScope.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    public const int GameModeId = 1001;
    public const int OverlayToggleId = 1002;
    public const int OverlayContentId = 1003;
    public const string DefaultGameMode = "Ctrl+Alt+G";
    public const string DefaultOverlayToggle = "Ctrl+Alt+T";
    public const string DefaultOverlayContent = "Ctrl+Alt+N";

    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly IntPtr _windowHandle;
    private readonly HwndSource _source;
    private readonly Action<int> _onPressed;
    private readonly List<int> _registeredIds = [];

    public GlobalHotkeyService(Window window, Action<int> onPressed)
    {
        _onPressed = onPressed;
        _windowHandle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_windowHandle)
            ?? throw new InvalidOperationException("无法初始化全局快捷键窗口句柄。");
        _source.AddHook(WndProc);
    }

    public IReadOnlyList<string> RegisterAll(AppSettings settings)
    {
        UnregisterAll();
        var failures = new List<string>();
        Register(GameModeId, settings.GameModeHotkey, "游戏模式", failures);
        Register(OverlayToggleId, settings.OverlayToggleHotkey, "悬浮窗开关", failures);
        Register(OverlayContentId, settings.OverlayContentHotkey, "切换显示内容", failures);
        return failures;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registeredIds) UnregisterHotKey(_windowHandle, id);
        _registeredIds.Clear();
    }

    public static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join('+', parts);
    }

    public static string ToDisplayText(string value) => value.Replace("+", " + ", StringComparison.Ordinal);

    private void Register(int id, string gestureText, string displayName, List<string> failures)
    {
        if (!TryParse(gestureText, out var modifiers, out var key))
        {
            failures.Add($"{displayName}快捷键格式无效");
            return;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!RegisterHotKey(_windowHandle, id, modifiers | ModNoRepeat, virtualKey))
        {
            failures.Add($"{displayName}快捷键 {ToDisplayText(gestureText)} 已被其他程序占用");
            return;
        }
        _registeredIds.Add(id);
    }

    private static bool TryParse(string text, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;
        foreach (var part in parts[..^1])
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL": modifiers |= ModControl; break;
                case "ALT": modifiers |= ModAlt; break;
                case "SHIFT": modifiers |= ModShift; break;
                case "WIN":
                case "WINDOWS": modifiers |= ModWin; break;
                default: return false;
            }
        }
        return modifiers != 0 && Enum.TryParse(parts[^1], true, out key) && key != Key.None;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey) return IntPtr.Zero;
        handled = true;
        _onPressed(wParam.ToInt32());
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

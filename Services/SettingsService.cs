using System.IO;
using System.Text.Json;
using HardwareScope.Models;
using Microsoft.Win32;

namespace HardwareScope.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "别离检测工具", "settings.json");
    private readonly string _legacySettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HardwareScope", "settings.json");
    private string? _lastSavedJson;

    public AppSettings Load()
    {
        try
        {
            var path = File.Exists(_settingsPath) ? _settingsPath : _legacySettingsPath;
            if (!File.Exists(path)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            if (string.IsNullOrWhiteSpace(settings.OverlayMode)) settings.OverlayMode = "自由拖动";
            if (string.IsNullOrWhiteSpace(settings.OverlayPosition)) settings.OverlayPosition = "右上角";
            settings.OverlayHardwareLabelMode = settings.OverlayHardwareLabelMode == "硬件类型" ? "硬件类型" : "硬件规格";
            if (string.IsNullOrWhiteSpace(settings.GameModeHotkey)) settings.GameModeHotkey = GlobalHotkeyService.DefaultGameMode;
            if (string.IsNullOrWhiteSpace(settings.OverlayToggleHotkey)) settings.OverlayToggleHotkey = GlobalHotkeyService.DefaultOverlayToggle;
            if (string.IsNullOrWhiteSpace(settings.OverlayContentHotkey)) settings.OverlayContentHotkey = GlobalHotkeyService.DefaultOverlayContent;
            if (path == _settingsPath) _lastSavedJson = JsonSerializer.Serialize(settings, JsonOptions);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        if (string.Equals(json, _lastSavedJson, StringComparison.Ordinal)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, json);
        _lastSavedJson = json;
        ApplyStartupSetting(settings.StartWithWindows);
    }

    private static void ApplyStartupSetting(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key is null) return;
        if (key.GetValue("HardwareScope") is not null) key.DeleteValue("HardwareScope", false);
        var command = $"\"{Environment.ProcessPath}\"";
        var current = key.GetValue("别离检测工具")?.ToString();
        if (enabled)
        {
            if (!string.Equals(current, command, StringComparison.Ordinal)) key.SetValue("别离检测工具", command);
        }
        else if (current is not null)
            key.DeleteValue("别离检测工具", false);
    }
}

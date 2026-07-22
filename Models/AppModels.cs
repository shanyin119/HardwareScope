using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HardwareScope.Models;

public sealed class HardwareDevice
{
    public string Category { get; init; } = "其他设备";
    public string Name { get; init; } = "未知设备";
    public string Manufacturer { get; init; } = "未知品牌";
    public string Status { get; init; } = "未知";
    public string DeviceId { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string SearchText => $"{Category} {Name} {Manufacturer} {Status} {DeviceId} {Detail}";
}

public sealed class HardwareGroup : INotifyPropertyChanged
{
    private bool _isExpanded;
    public required string Category { get; init; }
    public List<HardwareDevice> Devices { get; init; } = [];
    public int Count => Devices.Count;
    public string CountText => $"{Count} 项";
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class HardwareSnapshot
{
    public string ComputerName { get; init; } = Environment.MachineName;
    public string ComputerModel { get; set; } = "正在识别";
    public string OperatingSystem { get; set; } = Environment.OSVersion.VersionString;
    public string CpuName { get; set; } = "未检测到";
    public string GpuName { get; set; } = "未检测到";
    public string MemorySummary { get; set; } = "未检测到";
    public string StorageSummary { get; set; } = "未检测到";
    public List<HardwareDevice> Devices { get; } = [];
}

public sealed class TemperatureReading : INotifyPropertyChanged
{
    private double _current;
    private double _minimum = double.MaxValue;
    private double _maximum = double.MinValue;
    private bool _isOverlay;
    private string _overlayHardwareLabelMode = "硬件规格";

    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string HardwareName { get; init; }
    public required string SensorName { get; init; }
    public required string Description { get; init; }
    public bool IsThreshold { get; init; }

    public double Current { get => _current; private set => SetField(ref _current, value); }
    public double Minimum { get => _minimum == double.MaxValue ? 0 : _minimum; private set => SetField(ref _minimum, value); }
    public double Maximum { get => _maximum == double.MinValue ? 0 : _maximum; private set => SetField(ref _maximum, value); }
    public bool IsOverlay { get => _isOverlay; set => SetField(ref _isOverlay, value); }
    public string CurrentText => $"{Current:F1} °C";
    public string MinimumText => $"{Minimum:F1} °C";
    public string MaximumText => $"{Maximum:F1} °C";
    public string DisplayTitle => $"{HardwareName} · {SensorName}";
    public string DisplaySubtitle => $"{Category}温度";
    public string OverlayDisplayTitle => _overlayHardwareLabelMode == "硬件类型"
        ? $"{Category} · {SensorName}"
        : DisplayTitle;
    public string OverlayDisplaySubtitle => _overlayHardwareLabelMode == "硬件类型"
        ? HardwareName
        : DisplaySubtitle;

    public void SetOverlayHardwareLabelMode(string mode)
    {
        var normalized = mode == "硬件类型" ? "硬件类型" : "硬件规格";
        if (_overlayHardwareLabelMode == normalized) return;
        _overlayHardwareLabelMode = normalized;
        Notify(nameof(OverlayDisplayTitle));
        Notify(nameof(OverlayDisplaySubtitle));
    }

    public void Update(double value)
    {
        if (SetField(ref _current, value, nameof(Current))) Notify(nameof(CurrentText));
        if ((_minimum == double.MaxValue || value < _minimum) && SetField(ref _minimum, value, nameof(Minimum)))
            Notify(nameof(MinimumText));
        if ((_maximum == double.MinValue || value > _maximum) && SetField(ref _maximum, value, nameof(Maximum)))
            Notify(nameof(MaximumText));
    }

    public void ResetRange()
    {
        Minimum = Current;
        Maximum = Current;
        Notify(nameof(MinimumText));
        Notify(nameof(MaximumText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(propertyName);
        return true;
    }
    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ExportSectionOption : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public enum HardwareExportFormat
{
    Text,
    Word,
    Excel
}

public sealed class AppSettings
{
    public int UpdateIntervalSeconds { get; set; } = 2;
    public bool MonitorCpu { get; set; } = true;
    public bool MonitorGpu { get; set; } = true;
    public bool MonitorStorage { get; set; } = true;
    public bool MonitorMotherboard { get; set; } = true;
    public bool OverlayEnabled { get; set; }
    public string OverlayPosition { get; set; } = "右上角";
    public string OverlayMode { get; set; } = "自由拖动";
    public string OverlayHardwareLabelMode { get; set; } = "硬件规格";
    public int OverlayFontSize { get; set; } = 14;
    public double OverlayOpacity { get; set; } = 0.94;
    public double OverlayBackgroundOpacity { get; set; } = 0.78;
    public bool OverlayShowMinimum { get; set; } = true;
    public bool OverlayShowMaximum { get; set; } = true;
    public List<string> OverlaySensorIds { get; set; } = [];
    public bool OverlaySelectionInitialized { get; set; }
    public bool OverlayHasCustomPosition { get; set; }
    public double OverlayLeft { get; set; }
    public double OverlayTop { get; set; }
    public bool StartWithWindows { get; set; }
    public string GameModeHotkey { get; set; } = "Ctrl+Alt+G";
    public string OverlayToggleHotkey { get; set; } = "Ctrl+Alt+T";
    public string OverlayContentHotkey { get; set; } = "Ctrl+Alt+N";
}

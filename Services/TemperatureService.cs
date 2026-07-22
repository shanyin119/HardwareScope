using HardwareScope.Models;
using LibreHardwareMonitor.Hardware;
using System.Diagnostics;
using System.Management;
using Microsoft.Win32;

namespace HardwareScope.Services;

public readonly record struct SensorSample(
    string Id,
    string Category,
    string HardwareName,
    string SensorName,
    string Description,
    double Value,
    bool IsThreshold);

public readonly record struct TemperatureDiagnostics(
    bool CpuHardwareDetected,
    bool CpuTemperatureDetected,
    bool PawnIoInstalled,
    bool LowLevelAccessAvailable,
    string CpuHardwareName,
    string CpuSupportNote);

public sealed record TemperatureScanResult(
    List<SensorSample> Samples,
    TemperatureDiagnostics Diagnostics,
    long ElapsedMilliseconds);

public sealed class TemperatureService : IDisposable
{
    private readonly Computer _computer;
    private readonly object _sync = new();
    private readonly bool _cpuHardwareDetected;
    private readonly string _cpuName;
    private readonly bool _pawnIoInstalled;
    private readonly bool _lowLevelAccessAvailable;
    private readonly CpuCompatibilityInfo _cpuCompatibility;
    private readonly Dictionary<IHardware, long> _nextHardwareUpdateAt = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ISensor, SensorMetadata> _sensorMetadata = new(ReferenceEqualityComparer.Instance);
    private DateTime _lastAcpiReadUtc = DateTime.MinValue;
    private List<SensorSample> _cachedAcpiSamples = [];

    public TemperatureService(AppSettings settings)
    {
        var options = TemperatureScanOptions.From(settings);
        HardwareAccessDriverService.TryStartInstalledDriver();
        _pawnIoInstalled = HardwareAccessDriverService.IsInstalled();
        _lowLevelAccessAvailable = HardwareAccessDriverService.IsDriverAvailable();
        _cpuCompatibility = DetectCpuCompatibility();
        _computer = new Computer
        {
            IsCpuEnabled = options.MonitorCpu,
            IsGpuEnabled = options.MonitorGpu,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = options.MonitorMotherboard || options.MonitorCpu,
            IsControllerEnabled = options.MonitorMotherboard,
            IsStorageEnabled = options.MonitorStorage,
            IsNetworkEnabled = false,
            IsPowerMonitorEnabled = options.MonitorMotherboard
        };
        _computer.Open();
        var cpuHardware = FindHardware(_computer.Hardware, HardwareType.Cpu);
        _cpuHardwareDetected = cpuHardware is not null;
        _cpuName = cpuHardware?.Name ?? "处理器";
    }

    public Task<TemperatureScanResult> ReadAsync(AppSettings settings)
    {
        var options = TemperatureScanOptions.From(settings);
        return Task.Run(() => Read(options));
    }

    private TemperatureScanResult Read(TemperatureScanOptions options)
    {
        lock (_sync)
        {
            var stopwatch = Stopwatch.StartNew();
            var samples = new List<SensorSample>(48);
            var now = Environment.TickCount64;

            foreach (var hardware in _computer.Hardware)
                UpdateAndReadHardware(hardware, options, now, samples);
            if (options.MonitorMotherboard) AddAcpiThermalZones(samples);

            var diagnostics = new TemperatureDiagnostics(
                CpuHardwareDetected: _cpuHardwareDetected,
                CpuTemperatureDetected: samples.Any(x => x.Category == "处理器"),
                PawnIoInstalled: _pawnIoInstalled,
                LowLevelAccessAvailable: _lowLevelAccessAvailable,
                CpuHardwareName: _cpuName,
                CpuSupportNote: _cpuCompatibility.SupportNote);
            stopwatch.Stop();
            return new TemperatureScanResult(samples, diagnostics, stopwatch.ElapsedMilliseconds);
        }
    }

    private void AddAcpiThermalZones(List<SensorSample> samples)
    {
        if (DateTime.UtcNow - _lastAcpiReadUtc > TimeSpan.FromSeconds(15))
        {
            _lastAcpiReadUtc = DateTime.UtcNow;
            _cachedAcpiSamples = ReadAcpiThermalZones();
        }
        samples.AddRange(_cachedAcpiSamples);
    }

    private static List<SensorSample> ReadAcpiThermalZones()
    {
        var result = new List<SensorSample>();
        try
        {
            var scope = new ManagementScope(@"\\.\root\WMI");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"));
            using var collection = searcher.Get();
            var index = 0;
            foreach (ManagementObject item in collection)
            {
                var raw = Convert.ToDouble(item["CurrentTemperature"] ?? 0);
                var celsius = (raw / 10.0) - 273.15;
                if (celsius is < 0 or > 120) continue;
                var instanceName = item["InstanceName"]?.ToString() ?? $"热区 {index + 1}";
                result.Add(new SensorSample(
                    $"acpi/thermal-zone/{index++}",
                    "主板",
                    $"Windows ACPI 热区 · {ShortenAcpiName(instanceName)}",
                    "ACPI 热区温度",
                    "由电脑固件通过 Windows ACPI 提供的系统热区温度，可补充反映机身或主板环境，但不等同于 CPU 核心温度。",
                    celsius,
                    false));
            }
        }
        catch
        {
            // 很多台式机不公开 ACPI 热区，缺失时保持静默。
        }
        return result;
    }

    private static string ShortenAcpiName(string value)
    {
        var separator = value.LastIndexOf('\\');
        var name = separator >= 0 ? value[(separator + 1)..] : value;
        return name.Length > 32 ? name[^32..] : name;
    }

    private static IHardware? FindHardware(IEnumerable<IHardware> hardwareItems, HardwareType type)
    {
        foreach (var hardware in hardwareItems)
        {
            if (hardware.HardwareType == type) return hardware;
            var childResult = FindHardware(hardware.SubHardware, type);
            if (childResult is not null) return childResult;
        }
        return null;
    }

    private void UpdateAndReadHardware(
        IHardware hardware,
        TemperatureScanOptions options,
        long now,
        List<SensorSample> samples)
    {
        if (ShouldUpdateHardware(hardware.HardwareType, options) && IsHardwareUpdateDue(hardware, now))
            hardware.Update();
        var hardwareCategory = CategoryOf(hardware.HardwareType);
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Temperature || sensor.Value is null) continue;
            if (!_sensorMetadata.TryGetValue(sensor, out var metadata))
            {
                if (sensor.Name.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase)) continue;
                var category = hardwareCategory == "主板" && IsCpuRelatedSensor(sensor.Name)
                    ? "处理器"
                    : hardwareCategory;
                var hardwareName = category == "处理器" && hardwareCategory != "处理器"
                    ? _cpuName
                    : hardware.Name;
                var isThreshold = IsThresholdSensor(sensor.Name);
                var translatedName = TranslateSensorName(sensor.Name);
                metadata = new SensorMetadata(
                    sensor.Identifier.ToString(),
                    category,
                    hardwareName,
                    translatedName,
                    TemperatureDescriptionService.Describe(category, translatedName, isThreshold),
                    isThreshold);
                _sensorMetadata.Add(sensor, metadata);
            }

            var value = sensor.Value.Value;
            if (value is < -20 or > 150) continue;
            if (!IsCategoryEnabled(metadata.Category, options)) continue;
            samples.Add(new SensorSample(
                metadata.Id,
                metadata.Category,
                metadata.HardwareName,
                metadata.SensorName,
                metadata.Description,
                value,
                metadata.IsThreshold));
        }

        foreach (var child in hardware.SubHardware)
            UpdateAndReadHardware(child, options, now, samples);
    }

    private bool IsHardwareUpdateDue(IHardware hardware, long now)
    {
        var minimumDelay = hardware.HardwareType switch
        {
            HardwareType.Storage => 5000,
            HardwareType.Memory => 5000,
            HardwareType.Motherboard or HardwareType.SuperIO => 2000,
            HardwareType.Cooler or HardwareType.EmbeddedController or HardwareType.Psu or HardwareType.PowerMonitor => 3000,
            _ => 0
        };
        if (minimumDelay == 0) return true;
        if (_nextHardwareUpdateAt.TryGetValue(hardware, out var nextUpdate) && now < nextUpdate) return false;
        _nextHardwareUpdateAt[hardware] = now + minimumDelay;
        return true;
    }

    private static bool ShouldUpdateHardware(HardwareType type, TemperatureScanOptions settings) => type switch
    {
        HardwareType.Cpu => settings.MonitorCpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => settings.MonitorGpu,
        HardwareType.Storage => settings.MonitorStorage,
        HardwareType.Motherboard or HardwareType.SuperIO => settings.MonitorMotherboard || settings.MonitorCpu,
        HardwareType.Memory => true,
        HardwareType.Cooler or HardwareType.EmbeddedController or HardwareType.Psu or HardwareType.PowerMonitor => settings.MonitorMotherboard,
        _ => false
    };

    private static bool IsThresholdSensor(string name) =>
        name.Contains("Critical Temperature", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Warning Temperature", StringComparison.OrdinalIgnoreCase);

    private static bool IsCpuRelatedSensor(string name) =>
        name.Contains("CPU", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Package", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Core", StringComparison.OrdinalIgnoreCase);

    private static bool IsCategoryEnabled(string category, TemperatureScanOptions settings) => category switch
    {
        "处理器" => settings.MonitorCpu,
        "显卡" => settings.MonitorGpu,
        "存储" => settings.MonitorStorage,
        "主板" => settings.MonitorMotherboard,
        _ => true
    };

    private static string CategoryOf(HardwareType type) => type switch
    {
        HardwareType.Cpu => "处理器",
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "显卡",
        HardwareType.Storage => "存储",
        HardwareType.Motherboard or HardwareType.SuperIO => "主板",
        HardwareType.Memory => "内存",
        HardwareType.Cooler => "散热设备",
        HardwareType.EmbeddedController => "主板",
        HardwareType.Psu or HardwareType.PowerMonitor => "电源",
        _ => "其他"
    };

    private static string TranslateSensorName(string name)
    {
        var exact = name switch
        {
            "CPU Package" => "处理器封装温度",
            "CPU Core" => "处理器核心温度",
            "Core Average" => "核心平均温度",
            "Core Max" => "核心最高温度",
            "GPU Core" => "显卡核心温度",
            "GPU Hot Spot" => "显卡热点温度",
            "GPU Memory Junction" => "显存结温",
            "GPU Memory" => "显存温度",
            "Critical Temperature" => "临界温度",
            "Warning Temperature" => "警告温度",
            "Memory" => "内存温度",
            "Temperature" => "温度",
            "CPU" => "处理器温度",
            "System" => "系统温度",
            _ => string.Empty
        };
        if (!string.IsNullOrEmpty(exact)) return exact;

        return name
            .Replace("CPU", "处理器", StringComparison.OrdinalIgnoreCase)
            .Replace("GPU", "显卡", StringComparison.OrdinalIgnoreCase)
            .Replace("Memory Junction", "显存结温", StringComparison.OrdinalIgnoreCase)
            .Replace("Hot Spot", "热点", StringComparison.OrdinalIgnoreCase)
            .Replace("Critical", "临界", StringComparison.OrdinalIgnoreCase)
            .Replace("Warning", "警告", StringComparison.OrdinalIgnoreCase)
            .Replace("Package", "封装", StringComparison.OrdinalIgnoreCase)
            .Replace("Core", "核心", StringComparison.OrdinalIgnoreCase)
            .Replace("Temperature", "温度", StringComparison.OrdinalIgnoreCase)
            .Replace("Average", "平均", StringComparison.OrdinalIgnoreCase)
            .Replace("Max", "最高", StringComparison.OrdinalIgnoreCase);
    }

    private static CpuCompatibilityInfo DetectCpuCompatibility()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var vendor = key?.GetValue("VendorIdentifier")?.ToString() ?? string.Empty;
            var identifier = key?.GetValue("Identifier")?.ToString() ?? string.Empty;
            var family = ParseCpuFamily(identifier);
            if (vendor.Equals("GenuineIntel", StringComparison.OrdinalIgnoreCase))
                return new CpuCompatibilityInfo(vendor, family, "Intel DTS/MSR 通用检测已启用，覆盖支持数字温度传感器的 Core、Xeon 与 Core Ultra 系列。");
            if (vendor.Equals("AuthenticAMD", StringComparison.OrdinalIgnoreCase))
            {
                if (family is 0x0F or 0x17 or 0x19 or 0x1A)
                    return new CpuCompatibilityInfo(vendor, family, "AMD K8、Zen、Zen 2/3/4/5 的专用温度路径已启用。");
                if (family is >= 0x10 and <= 0x16)
                    return new CpuCompatibilityInfo(vendor, family, "该处理器属于旧款 AMD Family 10h–16h；当前安全 PawnIO 通道尚未提供这几代所需的旧式 PCI 温度访问。");
                return new CpuCompatibilityInfo(vendor, family, $"检测到 AMD Family {family:X}h；当前硬件库尚无此系列的专用温度读取器，将保留主板温度作为独立参考。");
            }
            if (!string.IsNullOrWhiteSpace(vendor))
                return new CpuCompatibilityInfo(vendor, family, $"检测到 {vendor} 处理器；当前硬件库没有该厂商的专用核心温度读取器。");
        }
        catch
        {
            // 注册表信息受限时仍继续使用硬件库的通用识别。
        }
        return new CpuCompatibilityInfo(string.Empty, -1, "已启用硬件库的通用 CPU 温度识别。");
    }

    private static int ParseCpuFamily(string identifier)
    {
        var parts = identifier.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("Family", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parts[i + 1], out var family)) return family;
        }
        return -1;
    }

    public void Dispose()
    {
        lock (_sync) _computer.Close();
    }

    private readonly record struct TemperatureScanOptions(
        bool MonitorCpu,
        bool MonitorGpu,
        bool MonitorStorage,
        bool MonitorMotherboard)
    {
        public static TemperatureScanOptions From(AppSettings settings) => new(
            settings.MonitorCpu,
            settings.MonitorGpu,
            settings.MonitorStorage,
            settings.MonitorMotherboard);
    }

    private sealed record SensorMetadata(
        string Id,
        string Category,
        string HardwareName,
        string SensorName,
        string Description,
        bool IsThreshold);

    private readonly record struct CpuCompatibilityInfo(string Vendor, int Family, string SupportNote);
}

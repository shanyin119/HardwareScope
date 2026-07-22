using System.Management;
using HardwareScope.Models;

namespace HardwareScope.Services;

public sealed class HardwareInventoryService
{
    public Task<HardwareSnapshot> ScanAsync() => Task.Run(Scan);

    private HardwareSnapshot Scan()
    {
        var snapshot = new HardwareSnapshot();
        var devices = new Dictionary<string, HardwareDevice>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
        {
            snapshot.ComputerModel = Clean($"{Get(item, "Manufacturer")} {Get(item, "Model")}");
            break;
        }

        foreach (var item in Query("SELECT Caption FROM Win32_OperatingSystem"))
        {
            snapshot.OperatingSystem = Get(item, "Caption", snapshot.OperatingSystem);
            break;
        }

        var cpuNames = new List<string>();
        foreach (var item in Query("SELECT Name, Manufacturer, ProcessorId, Status FROM Win32_Processor"))
        {
            var name = Clean(Get(item, "Name"));
            cpuNames.Add(name);
            Add(devices, new HardwareDevice
            {
                Category = "处理器", Name = name, Manufacturer = Brand(Get(item, "Manufacturer"), name),
                Status = Get(item, "Status", "正常"), DeviceId = Get(item, "ProcessorId"), Detail = "中央处理器"
            });
        }
        snapshot.CpuName = JoinSummary(cpuNames);

        var gpuNames = new List<string>();
        foreach (var item in Query("SELECT Name, AdapterCompatibility, PNPDeviceID, Status, AdapterRAM FROM Win32_VideoController"))
        {
            var name = Clean(Get(item, "Name"));
            if (string.IsNullOrWhiteSpace(name)) continue;
            gpuNames.Add(name);
            Add(devices, new HardwareDevice
            {
                Category = "显卡", Name = name,
                Manufacturer = Brand(Get(item, "AdapterCompatibility"), name),
                Status = Get(item, "Status", "正常"), DeviceId = Get(item, "PNPDeviceID"), Detail = "显示适配器"
            });
        }
        snapshot.GpuName = JoinSummary(gpuNames);

        ulong totalMemory = 0;
        var memoryParts = new List<string>();
        foreach (var item in Query("SELECT Manufacturer, PartNumber, Capacity, Speed, DeviceLocator FROM Win32_PhysicalMemory"))
        {
            var capacity = GetUInt64(item, "Capacity");
            totalMemory += capacity;
            var part = Clean(Get(item, "PartNumber"));
            var manufacturer = MemoryBrand(Get(item, "Manufacturer"), part);
            var speed = Get(item, "Speed");
            var name = Clean($"{manufacturer} {part}");
            memoryParts.Add(name);
            Add(devices, new HardwareDevice
            {
                Category = "内存", Name = name, Manufacturer = manufacturer, Status = "正常",
                DeviceId = Get(item, "DeviceLocator"),
                Detail = $"{Bytes(capacity)}{(string.IsNullOrWhiteSpace(speed) ? "" : $" · {speed} MHz")}"
            });
        }
        snapshot.MemorySummary = totalMemory > 0 ? $"{Bytes(totalMemory)} · {memoryParts.Count} 条" : "未检测到";

        var storageNames = new List<string>();
        ulong totalStorage = 0;
        var storageCount = 0;
        foreach (var item in Query("SELECT Model, Manufacturer, Size, SerialNumber, DeviceID, Status, InterfaceType FROM Win32_DiskDrive"))
        {
            var model = Clean(Get(item, "Model"));
            var size = GetUInt64(item, "Size");
            if (size > 0)
            {
                storageNames.Add($"{model} {Bytes(size)}");
                totalStorage += size;
                storageCount++;
            }
            Add(devices, new HardwareDevice
            {
                Category = "存储", Name = model, Manufacturer = Brand(Get(item, "Manufacturer"), model),
                Status = Get(item, "Status", "正常"), DeviceId = Get(item, "DeviceID"),
                Detail = $"{Bytes(size)} · {Get(item, "InterfaceType", "未知接口")}"
            });
        }
        snapshot.StorageSummary = storageCount > 0 ? $"{Bytes(totalStorage)} · {storageCount} 个磁盘" : JoinSummary(storageNames);

        foreach (var item in Query("SELECT Manufacturer, Product, SerialNumber, Status FROM Win32_BaseBoard"))
        {
            var product = Clean(Get(item, "Product"));
            var manufacturer = Brand(Get(item, "Manufacturer"), product);
            Add(devices, new HardwareDevice
            {
                Category = "主板", Name = product, Manufacturer = manufacturer,
                Status = Get(item, "Status", "正常"), DeviceId = Get(item, "SerialNumber"), Detail = "系统主板"
            });
        }

        foreach (var item in Query("SELECT Name, Manufacturer, PNPDeviceID, NetConnectionStatus, Status FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True"))
        {
            var name = Clean(Get(item, "Name"));
            Add(devices, new HardwareDevice
            {
                Category = "网络", Name = name, Manufacturer = Brand(Get(item, "Manufacturer"), name),
                Status = Get(item, "Status", "正常"), DeviceId = Get(item, "PNPDeviceID"), Detail = "物理网络适配器"
            });
        }

        foreach (var item in Query("SELECT Name, Manufacturer, PNPClass, Status, DeviceID, Description FROM Win32_PnPEntity"))
        {
            var name = Clean(Get(item, "Name"));
            if (string.IsNullOrWhiteSpace(name)) continue;
            Add(devices, new HardwareDevice
            {
                Category = FriendlyCategory(Get(item, "PNPClass")), Name = name,
                Manufacturer = Brand(Get(item, "Manufacturer"), name), Status = Get(item, "Status", "未知"),
                DeviceId = Get(item, "DeviceID"), Detail = Clean(Get(item, "Description"))
            });
        }

        snapshot.Devices.AddRange(devices.Values.OrderBy(d => d.Category).ThenBy(d => d.Name));
        return snapshot;
    }

    private static List<ManagementBaseObject> Query(string query)
    {
        var results = new List<ManagementBaseObject>();
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var collection = searcher.Get();
            foreach (ManagementBaseObject item in collection) results.Add(item);
        }
        catch
        {
            // 某些企业策略会限制 WMI；其他可访问的类别仍会正常显示。
        }
        return results;
    }

    private static string Get(ManagementBaseObject item, string property, string fallback = "")
    {
        try { return item[property]?.ToString()?.Trim() ?? fallback; }
        catch { return fallback; }
    }

    private static ulong GetUInt64(ManagementBaseObject item, string property) =>
        ulong.TryParse(Get(item, property), out var value) ? value : 0;

    private static void Add(Dictionary<string, HardwareDevice> devices, HardwareDevice device)
    {
        var key = string.IsNullOrWhiteSpace(device.DeviceId)
            ? $"{device.Category}|{device.Manufacturer}|{device.Name}"
            : device.DeviceId;
        devices.TryAdd(key, device);
    }

    private static string Brand(string manufacturer, string name)
    {
        var value = Clean(manufacturer);
        if (!string.IsNullOrWhiteSpace(value) && !value.Equals("(Standard system devices)", StringComparison.OrdinalIgnoreCase)) return value;
        string[] known = ["NVIDIA", "AMD", "Intel", "Microsoft", "Samsung", "Kingston", "Crucial", "Corsair", "ASUS", "MSI", "Gigabyte", "Western Digital", "Seagate", "Realtek", "Qualcomm", "MediaTek", "Lenovo", "Dell", "HP", "Acer"];
        return known.FirstOrDefault(x => name.Contains(x, StringComparison.OrdinalIgnoreCase)) ?? "未知品牌";
    }

    private static string MemoryBrand(string manufacturer, string partNumber)
    {
        var value = Clean(manufacturer);
        if (value.Equals("029E", StringComparison.OrdinalIgnoreCase) || partNumber.StartsWith("CM", StringComparison.OrdinalIgnoreCase))
            return "Corsair";
        if (value.Equals("80AD", StringComparison.OrdinalIgnoreCase)) return "SK hynix";
        if (value.Equals("80CE", StringComparison.OrdinalIgnoreCase)) return "Samsung";
        if (value.Equals("0198", StringComparison.OrdinalIgnoreCase)) return "Kingston";
        return Brand(value, partNumber);
    }

    private static string FriendlyCategory(string value) => value.ToLowerInvariant() switch
    {
        "display" => "显示设备", "processor" => "处理器", "net" => "网络", "media" => "音频",
        "monitor" => "显示器", "keyboard" => "键盘", "mouse" => "鼠标", "usb" => "USB 设备",
        "bluetooth" => "蓝牙", "camera" or "image" => "相机", "diskdrive" => "存储",
        "system" => "系统设备", "firmware" => "固件", "battery" => "电池", "hidclass" => "人机接口",
        "audioendpoint" => "音频端点", "audioprocessingobject" => "音频处理组件", "computer" => "计算机",
        "hdc" => "存储控制器", "scsiadapter" => "SCSI 控制器", "securitydevices" => "安全设备",
        "softwarecomponent" => "软件组件", "softwaredevice" => "软件设备", "volume" => "磁盘卷",
        "wdc_sam" => "存储设备", "ports" => "端口", "printer" => "打印机", "sensor" => "传感器",
        "biometric" => "生物识别设备", "cdrom" => "光盘驱动器", "netservice" => "网络服务",
        _ => string.IsNullOrWhiteSpace(value) ? "其他设备" : value
    };

    private static string JoinSummary(IEnumerable<string> values)
    {
        var result = string.Join(" / ", values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        return string.IsNullOrWhiteSpace(result) ? "未检测到" : result;
    }

    private static string Clean(string value) => string.Join(" ", value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    private static string Bytes(ulong bytes)
    {
        if (bytes == 0) return "未知容量";
        var gib = bytes / 1024d / 1024d / 1024d;
        return gib >= 1024 ? $"{gib / 1024d:F2} TB" : $"{gib:F0} GB";
    }
}

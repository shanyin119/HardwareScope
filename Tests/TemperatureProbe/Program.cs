using LibreHardwareMonitor.Hardware;
using HardwareScope.Models;
using HardwareScope.Services;

var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMemoryEnabled = true,
    IsMotherboardEnabled = true,
    IsControllerEnabled = true,
    IsStorageEnabled = true,
    IsNetworkEnabled = false,
    IsPowerMonitorEnabled = false
};
computer.Open();

Console.WriteLine("=== 单次递归刷新 ===");
foreach (var hardware in computer.Hardware) UpdateAndPrint(hardware);

Console.WriteLine("=== 访问器刷新 ===");
computer.Accept(new ProbeVisitor());
foreach (var hardware in computer.Hardware) Print(hardware);
computer.Close();

Console.WriteLine("=== 优化后连续扫描耗时 ===");
var settings = new AppSettings { UpdateIntervalSeconds = 1 };
using (var service = new TemperatureService(settings))
{
    for (var i = 1; i <= 8; i++)
    {
        var result = await service.ReadAsync(settings);
        Console.WriteLine($"第 {i} 次：{result.ElapsedMilliseconds} ms，{result.Samples.Count} 项");
        await Task.Delay(200);
    }
}

static void UpdateAndPrint(IHardware hardware)
{
    hardware.Update();
    PrintCurrent(hardware);
    foreach (var child in hardware.SubHardware) UpdateAndPrint(child);
}

static void Print(IHardware hardware)
{
    PrintCurrent(hardware);
    foreach (var child in hardware.SubHardware) Print(child);
}

static void PrintCurrent(IHardware hardware)
{
    var temperatures = hardware.Sensors
        .Where(x => x.SensorType == SensorType.Temperature && x.Value is not null)
        .Select(x => $"{x.Name}={x.Value:F1}")
        .ToList();
    Console.WriteLine($"{hardware.HardwareType} | {hardware.Name} | 温度数 {temperatures.Count} | {string.Join(", ", temperatures)}");
}

sealed class ProbeVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware) subHardware.Accept(this);
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}

using HardwareScope;
using HardwareScope.Models;
using HardwareScope.Services;

var outputDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TestExports"));
Directory.CreateDirectory(outputDirectory);

var snapshot = await new HardwareInventoryService().ScanAsync();
var temperature = new TemperatureReading
{
    Id = "test/cpu/package",
    Category = "处理器",
    HardwareName = snapshot.CpuName,
    SensorName = "处理器封装温度",
    Description = TemperatureDescriptionService.Describe("处理器", "处理器封装温度", false),
    IsThreshold = false
};
temperature.Update(42.5);
temperature.ResetRange();
temperature.Update(47.2);

var sections = snapshot.Devices.Select(x => x.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
sections.Add(ExportDialog.OverviewSection);
sections.Add(ExportDialog.TemperatureSection);
var service = new HardwareExportService();

await service.ExportAsync(Path.Combine(outputDirectory, "硬件信息测试.txt"), HardwareExportFormat.Text, snapshot, [temperature], sections);
await service.ExportAsync(Path.Combine(outputDirectory, "硬件信息测试.docx"), HardwareExportFormat.Word, snapshot, [temperature], sections);
await service.ExportAsync(Path.Combine(outputDirectory, "硬件信息测试.xlsx"), HardwareExportFormat.Excel, snapshot, [temperature], sections);

Console.WriteLine(outputDirectory);

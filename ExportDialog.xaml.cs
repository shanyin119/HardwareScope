using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using HardwareScope.Models;

namespace HardwareScope;

public partial class ExportDialog : Window
{
    public const string OverviewSection = "__overview";
    public const string TemperatureSection = "__temperatures";
    public ObservableCollection<ExportSectionOption> Sections { get; } = [];

    public ExportDialog(HardwareSnapshot snapshot, int temperatureCount)
    {
        InitializeComponent();
        Sections.Add(new ExportSectionOption
        {
            Key = OverviewSection,
            DisplayName = "电脑概况（电脑型号、系统、处理器、显卡、内存、存储）"
        });

        foreach (var group in snapshot.Devices.GroupBy(x => x.Category).OrderBy(x => x.Key))
        {
            Sections.Add(new ExportSectionOption
            {
                Key = group.Key,
                DisplayName = $"{group.Key}（{group.Count()} 项）"
            });
        }

        Sections.Add(new ExportSectionOption
        {
            Key = TemperatureSection,
            DisplayName = $"温度信息（{temperatureCount} 个传感器，含当前、最低、最高温度）"
        });
        SectionsList.ItemsSource = Sections;
    }

    public HardwareExportFormat SelectedFormat => WordFormatRadio.IsChecked == true
        ? HardwareExportFormat.Word
        : ExcelFormatRadio.IsChecked == true
            ? HardwareExportFormat.Excel
            : HardwareExportFormat.Text;

    public HashSet<string> SelectedSectionKeys => Sections
        .Where(x => x.IsSelected)
        .Select(x => x.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var section in Sections) section.IsSelected = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var section in Sections) section.IsSelected = false;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!Sections.Any(x => x.IsSelected))
        {
            MessageBox.Show(this, "请至少勾选一项需要导出的信息。", "别离检测工具", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}

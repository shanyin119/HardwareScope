using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using HardwareScope.Models;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace HardwareScope.Services;

public sealed class HardwareExportService
{
    public Task ExportAsync(
        string path,
        HardwareExportFormat format,
        HardwareSnapshot snapshot,
        IEnumerable<TemperatureReading> temperatures,
        IReadOnlySet<string> selectedSections)
    {
        var overview = new OverviewData(
            snapshot.ComputerName,
            snapshot.ComputerModel,
            snapshot.OperatingSystem,
            snapshot.CpuName,
            snapshot.GpuName,
            snapshot.MemorySummary,
            snapshot.StorageSummary);
        var devices = snapshot.Devices
            .Where(x => selectedSections.Contains(x.Category))
            .Select(x => new DeviceData(x.Category, x.Name, x.Manufacturer, x.Status, x.DeviceId, x.Detail))
            .ToList();
        var temperatureRows = selectedSections.Contains(ExportDialog.TemperatureSection)
            ? temperatures.Select(x => new TemperatureData(
                x.Category, x.HardwareName, x.SensorName, x.Current, x.Minimum, x.Maximum)).ToList()
            : [];
        var includeOverview = selectedSections.Contains(ExportDialog.OverviewSection);

        return Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            switch (format)
            {
                case HardwareExportFormat.Word:
                    WriteWord(path, overview, devices, temperatureRows, includeOverview);
                    break;
                case HardwareExportFormat.Excel:
                    WriteExcel(path, overview, devices, temperatureRows, includeOverview);
                    break;
                default:
                    WriteText(path, overview, devices, temperatureRows, includeOverview);
                    break;
            }
        });
    }

    private static void WriteText(
        string path,
        OverviewData overview,
        IReadOnlyCollection<DeviceData> devices,
        IReadOnlyCollection<TemperatureData> temperatures,
        bool includeOverview)
    {
        var text = new StringBuilder();
        text.AppendLine("别离检测工具 - 硬件信息报告");
        text.AppendLine($"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine(new string('=', 72));

        if (includeOverview)
        {
            text.AppendLine();
            text.AppendLine("【电脑概况】");
            foreach (var (name, value) in OverviewRows(overview)) text.AppendLine($"{name}：{value}");
        }

        foreach (var group in devices.GroupBy(x => x.Category).OrderBy(x => x.Key))
        {
            text.AppendLine();
            text.AppendLine($"【{group.Key}】（{group.Count()} 项）");
            var index = 1;
            foreach (var item in group)
            {
                text.AppendLine($"{index++}. {item.Name}");
                text.AppendLine($"   品牌 / 制造商：{item.Manufacturer}");
                text.AppendLine($"   状态：{item.Status}");
                text.AppendLine($"   详细信息：{item.Detail}");
                text.AppendLine($"   设备标识：{item.DeviceId}");
            }
        }

        if (temperatures.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"【温度信息】（{temperatures.Count} 个传感器）");
            foreach (var item in temperatures.OrderBy(x => x.Category).ThenBy(x => x.HardwareName).ThenBy(x => x.SensorName))
            {
                text.AppendLine($"{item.HardwareName} · {item.SensorName}：当前 {item.Current:F1} °C，最低 {item.Minimum:F1} °C，最高 {item.Maximum:F1} °C");
            }
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static void WriteWord(
        string path,
        OverviewData overview,
        IReadOnlyCollection<DeviceData> devices,
        IReadOnlyCollection<TemperatureData> temperatures,
        bool includeOverview)
    {
        using (var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new W.Document(new W.Body());
            AddWordStyles(mainPart);
            var body = mainPart.Document.Body!;

            body.Append(WordParagraph("别离检测工具", "ReportTitle"));
            body.Append(WordParagraph("硬件信息报告", "ReportSubtitle"));
            body.Append(WordParagraph($"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}", "Meta"));

            if (includeOverview)
            {
                body.Append(WordParagraph("电脑概况", "Heading1"));
                body.Append(CreateWordTable(
                    ["项目", "信息"],
                    OverviewRows(overview).Select(x => new[] { x.Name, x.Value }),
                    [2100, 7260]));
            }

            foreach (var group in devices.GroupBy(x => x.Category).OrderBy(x => x.Key))
            {
                body.Append(WordParagraph($"{group.Key}（{group.Count()} 项）", "Heading1"));
                body.Append(CreateWordTable(
                    ["硬件名称", "品牌 / 制造商", "状态", "详细信息", "设备标识"],
                    group.Select(x => new[] { x.Name, x.Manufacturer, x.Status, x.Detail, x.DeviceId }),
                    [2400, 1500, 800, 1960, 2700]));
            }

            if (temperatures.Count > 0)
            {
                body.Append(WordParagraph($"温度信息（{temperatures.Count} 个传感器）", "Heading1"));
                body.Append(CreateWordTable(
                    ["设备", "温度项目", "类别", "当前", "最低", "最高"],
                    temperatures.OrderBy(x => x.Category).ThenBy(x => x.HardwareName).ThenBy(x => x.SensorName)
                        .Select(x => new[]
                        {
                            x.HardwareName, x.SensorName, x.Category,
                            $"{x.Current:F1} °C", $"{x.Minimum:F1} °C", $"{x.Maximum:F1} °C"
                        }),
                    [2400, 1700, 900, 1450, 1450, 1460]));
            }

            body.Append(new W.SectionProperties(
                new W.PageSize { Width = 12240, Height = 15840 },
                new W.PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440 }));
            mainPart.Document.Save();
        }
        ValidateOpenXml(path, isWord: true);
    }

    private static void AddWordStyles(MainDocumentPart mainPart)
    {
        var stylePart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylePart.Styles = new W.Styles(
            ParagraphStyle("Normal", "正文", 21, "27324A"),
            ParagraphStyle("ReportTitle", "报告标题", 36, "17233F", bold: true, centered: true, spaceAfter: 100),
            ParagraphStyle("ReportSubtitle", "报告副标题", 25, "6479F3", bold: true, centered: true, spaceAfter: 60),
            ParagraphStyle("Meta", "元数据", 18, "72809C", centered: true, spaceAfter: 280),
            ParagraphStyle("Heading1", "一级标题", 28, "17233F", bold: true, spaceBefore: 300, spaceAfter: 100));
        stylePart.Styles.Save();
    }

    private static W.Style ParagraphStyle(
        string id,
        string name,
        int fontSize,
        string color,
        bool bold = false,
        bool centered = false,
        int spaceBefore = 0,
        int spaceAfter = 80)
    {
        var style = new W.Style { Type = W.StyleValues.Paragraph, StyleId = id, Default = id == "Normal" };
        style.Append(new W.StyleName { Val = name });
        var paragraphProperties = new W.StyleParagraphProperties(
            new W.SpacingBetweenLines { Before = spaceBefore.ToString(), After = spaceAfter.ToString(), Line = "300", LineRule = W.LineSpacingRuleValues.Auto });
        if (centered) paragraphProperties.Append(new W.Justification { Val = W.JustificationValues.Center });
        style.Append(paragraphProperties);
        var runProperties = new W.StyleRunProperties(
            new W.RunFonts { Ascii = "Microsoft YaHei", HighAnsi = "Microsoft YaHei", EastAsia = "Microsoft YaHei" });
        if (bold) runProperties.Append(new W.Bold());
        runProperties.Append(
            new W.Color { Val = color },
            new W.FontSize { Val = fontSize.ToString() },
            new W.FontSizeComplexScript { Val = fontSize.ToString() },
            new W.Languages { Val = "zh-CN", EastAsia = "zh-CN" });
        style.Append(runProperties);
        return style;
    }

    private static W.Paragraph WordParagraph(string text, string styleId) => new(
        new W.ParagraphProperties(new W.ParagraphStyleId { Val = styleId }),
        new W.Run(new W.Text(CleanXml(text)) { Space = SpaceProcessingModeValues.Preserve }));

    private static W.Table CreateWordTable(IEnumerable<string> headers, IEnumerable<string[]> rows, int[] widths)
    {
        var table = new W.Table();
        table.Append(new W.TableProperties(
            new W.TableWidth { Width = "9360", Type = W.TableWidthUnitValues.Dxa },
            new W.TableIndentation { Width = 120, Type = W.TableWidthUnitValues.Dxa },
            new W.TableBorders(
                new W.TopBorder { Val = W.BorderValues.Single, Color = "CAD3E2", Size = 6 },
                new W.LeftBorder { Val = W.BorderValues.Single, Color = "CAD3E2", Size = 6 },
                new W.BottomBorder { Val = W.BorderValues.Single, Color = "CAD3E2", Size = 6 },
                new W.RightBorder { Val = W.BorderValues.Single, Color = "CAD3E2", Size = 6 },
                new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Color = "E2E7F0", Size = 4 },
                new W.InsideVerticalBorder { Val = W.BorderValues.Single, Color = "E2E7F0", Size = 4 }),
            new W.TableLayout { Type = W.TableLayoutValues.Fixed }));
        table.Append(new W.TableGrid(widths.Select(x => new W.GridColumn { Width = x.ToString() })));

        var headerRow = new W.TableRow();
        var headerList = headers.ToList();
        for (var i = 0; i < headerList.Count; i++)
            headerRow.Append(WordCell(headerList[i], widths[i], isHeader: true));
        table.Append(headerRow);

        var alternate = false;
        foreach (var row in rows)
        {
            var tableRow = new W.TableRow();
            for (var i = 0; i < widths.Length; i++)
                tableRow.Append(WordCell(i < row.Length ? row[i] : string.Empty, widths[i], isHeader: false, alternate));
            table.Append(tableRow);
            alternate = !alternate;
        }
        return table;
    }

    private static W.TableCell WordCell(string text, int width, bool isHeader, bool alternate = false)
    {
        var properties = new W.TableCellProperties(
            new W.TableCellWidth { Width = width.ToString(), Type = W.TableWidthUnitValues.Dxa });
        if (isHeader) properties.Append(new W.Shading { Fill = "263A67", Val = W.ShadingPatternValues.Clear });
        else if (alternate) properties.Append(new W.Shading { Fill = "F4F7FB", Val = W.ShadingPatternValues.Clear });
        properties.Append(new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Center });

        var runProperties = new W.RunProperties(
            new W.RunFonts { Ascii = "Microsoft YaHei", HighAnsi = "Microsoft YaHei", EastAsia = "Microsoft YaHei" });
        if (isHeader) runProperties.Append(new W.Bold());
        runProperties.Append(
            new W.Color { Val = isHeader ? "FFFFFF" : "27324A" },
            new W.FontSize { Val = "18" });
        return new W.TableCell(
            properties,
            new W.Paragraph(
                new W.ParagraphProperties(new W.SpacingBetweenLines { Before = "70", After = "70", Line = "260", LineRule = W.LineSpacingRuleValues.Auto }),
                new W.Run(runProperties, new W.Text(CleanXml(text)) { Space = SpaceProcessingModeValues.Preserve })));
    }

    private static void WriteExcel(
        string path,
        OverviewData overview,
        IReadOnlyCollection<DeviceData> devices,
        IReadOnlyCollection<TemperatureData> temperatures,
        bool includeOverview)
    {
        using (var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new S.Workbook();
            workbookPart.Workbook.Append(new S.BookViews(new S.WorkbookView { ActiveTab = 0 }));
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateSpreadsheetStyles();
            stylesPart.Stylesheet.Save();
            var sheets = workbookPart.Workbook.AppendChild(new S.Sheets());
            uint sheetId = 1;

            if (includeOverview)
                AddOverviewSheet(workbookPart, sheets, sheetId++, overview);
            if (devices.Count > 0)
                AddHardwareSheet(workbookPart, sheets, sheetId++, devices);
            if (temperatures.Count > 0)
                AddTemperatureSheet(workbookPart, sheets, sheetId++, temperatures);

            if (sheetId == 1)
                AddMessageSheet(workbookPart, sheets, sheetId, "未选择可导出的内容");
            workbookPart.Workbook.Save();
        }
        ValidateOpenXml(path, isWord: false);
    }

    private static S.Stylesheet CreateSpreadsheetStyles()
    {
        var numberingFormats = new S.NumberingFormats(
            new S.NumberingFormat { NumberFormatId = 164, FormatCode = "0.0 \"°C\"" }) { Count = 1 };
        var fonts = new S.Fonts(
            SpreadsheetFont(10, "27324A"),
            SpreadsheetFont(18, "FFFFFF", bold: true),
            SpreadsheetFont(10, "FFFFFF", bold: true),
            SpreadsheetFont(12, "FFFFFF", bold: true)) { Count = 4 };
        var fills = new S.Fills(
            new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
            new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 }),
            SolidFill("17233F"),
            SolidFill("6479F3"),
            SolidFill("EEF2F8")) { Count = 5 };
        var borders = new S.Borders(
            new S.Border(),
            new S.Border(
                new S.LeftBorder { Style = S.BorderStyleValues.Thin, Color = new S.Color { Rgb = "FFD8DFEA" } },
                new S.RightBorder { Style = S.BorderStyleValues.Thin, Color = new S.Color { Rgb = "FFD8DFEA" } },
                new S.TopBorder { Style = S.BorderStyleValues.Thin, Color = new S.Color { Rgb = "FFD8DFEA" } },
                new S.BottomBorder { Style = S.BorderStyleValues.Thin, Color = new S.Color { Rgb = "FFD8DFEA" } },
                new S.DiagonalBorder())) { Count = 2 };
        var cellStyleFormats = new S.CellStyleFormats(new S.CellFormat()) { Count = 1 };
        var cellFormats = new S.CellFormats(
            new S.CellFormat(),
            new S.CellFormat { FontId = 1, FillId = 2, ApplyFont = true, ApplyFill = true, Alignment = new S.Alignment { Horizontal = S.HorizontalAlignmentValues.Center, Vertical = S.VerticalAlignmentValues.Center } },
            new S.CellFormat { FontId = 2, FillId = 3, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true, Alignment = new S.Alignment { Vertical = S.VerticalAlignmentValues.Center, WrapText = true } },
            new S.CellFormat { FontId = 0, BorderId = 1, ApplyFont = true, ApplyBorder = true, Alignment = new S.Alignment { Vertical = S.VerticalAlignmentValues.Center, WrapText = true } },
            new S.CellFormat { FontId = 3, FillId = 2, ApplyFont = true, ApplyFill = true },
            new S.CellFormat { FontId = 0, BorderId = 1, NumberFormatId = 164, ApplyFont = true, ApplyBorder = true, ApplyNumberFormat = true, Alignment = new S.Alignment { Horizontal = S.HorizontalAlignmentValues.Right, Vertical = S.VerticalAlignmentValues.Center } }) { Count = 6 };
        return new S.Stylesheet(numberingFormats, fonts, fills, borders, cellStyleFormats, cellFormats,
            new S.CellStyles(new S.CellStyle { Name = "常规", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
    }

    private static S.Font SpreadsheetFont(double size, string color, bool bold = false)
    {
        var font = new S.Font();
        if (bold) font.Append(new S.Bold());
        font.Append(
            new S.FontSize { Val = size },
            new S.Color { Rgb = Argb(color) },
            new S.FontName { Val = "Microsoft YaHei" });
        return font;
    }

    private static S.Fill SolidFill(string color) => new(
        new S.PatternFill(new S.ForegroundColor { Rgb = Argb(color) }, new S.BackgroundColor { Indexed = 64 })
        { PatternType = S.PatternValues.Solid });

    private static void AddOverviewSheet(WorkbookPart workbookPart, S.Sheets sheets, uint sheetId, OverviewData overview)
    {
        var part = workbookPart.AddNewPart<WorksheetPart>();
        var data = new S.SheetData();
        data.Append(SpreadsheetRow(1, ("别离检测工具 - 电脑概况", 1U)));
        data.Append(SpreadsheetRow(2, ($"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}", 0U)));
        data.Append(SpreadsheetRow(4, ("项目", 2U), ("信息", 2U)));
        uint rowIndex = 5;
        foreach (var (name, value) in OverviewRows(overview))
            data.Append(SpreadsheetRow(rowIndex++, (name, 3U), (value, 3U)));
        part.Worksheet = new S.Worksheet(
            CreateSheetViews(freezeRow: 4),
            new S.Columns(
                new S.Column { Min = 1, Max = 1, Width = 23, CustomWidth = true },
                new S.Column { Min = 2, Max = 2, Width = 86, CustomWidth = true }),
            data,
            new S.MergeCells(new S.MergeCell { Reference = "A1:B1" }));
        part.Worksheet.Save();
        sheets.Append(new S.Sheet { Id = workbookPart.GetIdOfPart(part), SheetId = sheetId, Name = "电脑概况" });
    }

    private static void AddHardwareSheet(WorkbookPart workbookPart, S.Sheets sheets, uint sheetId, IReadOnlyCollection<DeviceData> devices)
    {
        var part = workbookPart.AddNewPart<WorksheetPart>();
        var data = new S.SheetData();
        data.Append(SpreadsheetRow(1, ("别离检测工具 - 硬件明细", 1U)));
        data.Append(SpreadsheetRow(2, ($"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss} · 共 {devices.Count} 项", 0U)));
        data.Append(SpreadsheetRow(4, ("分类", 2U), ("硬件名称", 2U), ("品牌 / 制造商", 2U), ("状态", 2U), ("详细信息", 2U), ("设备标识", 2U)));
        uint rowIndex = 5;
        foreach (var item in devices.OrderBy(x => x.Category).ThenBy(x => x.Name))
            data.Append(SpreadsheetRow(rowIndex++, (item.Category, 3U), (item.Name, 3U), (item.Manufacturer, 3U), (item.Status, 3U), (item.Detail, 3U), (item.DeviceId, 3U)));
        part.Worksheet = new S.Worksheet(
            CreateSheetViews(freezeRow: 4),
            new S.Columns(
                SpreadsheetColumn(1, 16), SpreadsheetColumn(2, 42), SpreadsheetColumn(3, 25),
                SpreadsheetColumn(4, 12), SpreadsheetColumn(5, 38), SpreadsheetColumn(6, 54)),
            data,
            new S.AutoFilter { Reference = $"A4:F{Math.Max(4, rowIndex - 1)}" },
            new S.MergeCells(new S.MergeCell { Reference = "A1:F1" }));
        part.Worksheet.Save();
        sheets.Append(new S.Sheet { Id = workbookPart.GetIdOfPart(part), SheetId = sheetId, Name = "硬件明细" });
    }

    private static void AddTemperatureSheet(WorkbookPart workbookPart, S.Sheets sheets, uint sheetId, IReadOnlyCollection<TemperatureData> temperatures)
    {
        var part = workbookPart.AddNewPart<WorksheetPart>();
        var data = new S.SheetData();
        data.Append(SpreadsheetRow(1, ("别离检测工具 - 温度信息", 1U)));
        data.Append(SpreadsheetRow(2, ($"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss} · 共 {temperatures.Count} 个传感器", 0U)));
        data.Append(SpreadsheetRow(4, ("类别", 2U), ("设备", 2U), ("温度项目", 2U), ("当前", 2U), ("最低", 2U), ("最高", 2U)));
        uint rowIndex = 5;
        foreach (var item in temperatures.OrderBy(x => x.Category).ThenBy(x => x.HardwareName).ThenBy(x => x.SensorName))
        {
            var row = new S.Row { RowIndex = rowIndex++ };
            row.Append(SpreadsheetTextCell(item.Category, 3), SpreadsheetTextCell(item.HardwareName, 3), SpreadsheetTextCell(item.SensorName, 3));
            row.Append(SpreadsheetNumberCell(item.Current), SpreadsheetNumberCell(item.Minimum), SpreadsheetNumberCell(item.Maximum));
            var columnIndex = 1U;
            foreach (var cell in row.Elements<S.Cell>()) cell.CellReference = $"{ColumnName(columnIndex++)}{row.RowIndex}";
            data.Append(row);
        }
        part.Worksheet = new S.Worksheet(
            CreateSheetViews(freezeRow: 4),
            new S.Columns(
                SpreadsheetColumn(1, 14), SpreadsheetColumn(2, 46), SpreadsheetColumn(3, 28),
                SpreadsheetColumn(4, 14), SpreadsheetColumn(5, 14), SpreadsheetColumn(6, 14)),
            data,
            new S.AutoFilter { Reference = $"A4:F{Math.Max(4, rowIndex - 1)}" },
            new S.MergeCells(new S.MergeCell { Reference = "A1:F1" }));
        part.Worksheet.Save();
        sheets.Append(new S.Sheet { Id = workbookPart.GetIdOfPart(part), SheetId = sheetId, Name = "温度信息" });
    }

    private static void AddMessageSheet(WorkbookPart workbookPart, S.Sheets sheets, uint sheetId, string message)
    {
        var part = workbookPart.AddNewPart<WorksheetPart>();
        part.Worksheet = new S.Worksheet(new S.SheetData(SpreadsheetRow(1, (message, 1U))));
        part.Worksheet.Save();
        sheets.Append(new S.Sheet { Id = workbookPart.GetIdOfPart(part), SheetId = sheetId, Name = "导出结果" });
    }

    private static S.SheetViews CreateSheetViews(uint freezeRow) => new(
        new S.SheetView(
            new S.Pane { VerticalSplit = freezeRow, TopLeftCell = $"A{freezeRow + 1}", ActivePane = S.PaneValues.BottomLeft, State = S.PaneStateValues.Frozen })
        { WorkbookViewId = 0, ShowGridLines = false });

    private static S.Column SpreadsheetColumn(uint index, double width) => new()
    {
        Min = index,
        Max = index,
        Width = width,
        CustomWidth = true
    };

    private static S.Row SpreadsheetRow(uint index, params (string Text, uint Style)[] cells)
    {
        var row = new S.Row { RowIndex = index };
        if (index == 1)
        {
            row.Height = 32;
            row.CustomHeight = true;
        }
        else if (index == 4)
        {
            row.Height = 24;
            row.CustomHeight = true;
        }
        uint columnIndex = 1;
        foreach (var cell in cells)
        {
            var spreadsheetCell = SpreadsheetTextCell(cell.Text, cell.Style);
            spreadsheetCell.CellReference = $"{ColumnName(columnIndex++)}{index}";
            row.Append(spreadsheetCell);
        }
        return row;
    }

    private static string ColumnName(uint index)
    {
        var name = string.Empty;
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }
        return name;
    }

    private static S.Cell SpreadsheetTextCell(string text, uint style) => new()
    {
        DataType = S.CellValues.InlineString,
        StyleIndex = style,
        InlineString = new S.InlineString(new S.Text(CleanXml(text)) { Space = SpaceProcessingModeValues.Preserve })
    };

    private static S.Cell SpreadsheetNumberCell(double value) => new()
    {
        DataType = S.CellValues.Number,
        StyleIndex = 5,
        CellValue = new S.CellValue(value.ToString("0.0", CultureInfo.InvariantCulture))
    };

    private static IEnumerable<(string Name, string Value)> OverviewRows(OverviewData overview)
    {
        yield return ("电脑名称", overview.ComputerName);
        yield return ("电脑型号", overview.ComputerModel);
        yield return ("操作系统", overview.OperatingSystem);
        yield return ("处理器", overview.CpuName);
        yield return ("显卡", overview.GpuName);
        yield return ("内存", overview.MemorySummary);
        yield return ("存储", overview.StorageSummary);
    }

    private static string CleanXml(string? value) => string.Concat((value ?? string.Empty).Where(XmlConvert.IsXmlChar));

    private static string Argb(string rgb) => rgb.Length == 8 ? rgb : $"FF{rgb}";

    private static void ValidateOpenXml(string path, bool isWord)
    {
        var validator = new OpenXmlValidator();
        var errors = isWord
            ? ValidateWord(path, validator)
            : ValidateSpreadsheet(path, validator);
        if (errors.Count > 0)
            throw new InvalidDataException($"导出文件结构校验失败：{errors[0].Description}");
    }

    private static List<ValidationErrorInfo> ValidateWord(string path, OpenXmlValidator validator)
    {
        using var document = WordprocessingDocument.Open(path, false);
        return validator.Validate(document).ToList();
    }

    private static List<ValidationErrorInfo> ValidateSpreadsheet(string path, OpenXmlValidator validator)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        return validator.Validate(document).ToList();
    }

    private sealed record OverviewData(
        string ComputerName,
        string ComputerModel,
        string OperatingSystem,
        string CpuName,
        string GpuName,
        string MemorySummary,
        string StorageSummary);

    private sealed record DeviceData(
        string Category,
        string Name,
        string Manufacturer,
        string Status,
        string DeviceId,
        string Detail);

    private sealed record TemperatureData(
        string Category,
        string HardwareName,
        string SensorName,
        double Current,
        double Minimum,
        double Maximum);
}

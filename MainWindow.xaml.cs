using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareScope.Models;
using HardwareScope.Services;
using Microsoft.Win32;

namespace HardwareScope;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly HardwareInventoryService _inventoryService = new();
    private readonly HardwareExportService _exportService = new();
    private readonly ObservableCollection<HardwareGroup> _hardwareGroups = [];
    private readonly ObservableCollection<TemperatureReading> _temperatures = [];
    private readonly ObservableCollection<TemperatureReading> _overlayReadings = [];
    private readonly Dictionary<string, TemperatureReading> _temperatureById = [];
    private readonly DispatcherTimer _temperatureTimer = new();
    private AppSettings _settings;
    private HardwareSnapshot? _snapshot;
    private TemperatureService? _temperatureService;
    private OverlayWindow? _overlay;
    private TrayIconService? _trayIcon;
    private GlobalHotkeyService? _hotkeyService;
    private bool _temperatureBusy;
    private bool _uiReady;
    private bool _syncingSensors;
    private bool _gameMode;
    private string? _overlayContentCategory;
    private string? _capturingHotkeySetting;
    private Button? _capturingHotkeyButton;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        OverlaySensorsList.ItemsSource = _overlayReadings;
        _temperatureTimer.Tick += async (_, _) => await RefreshTemperaturesAsync();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += (_, _) => UpdateTemperatureTimerInterval();
        IsVisibleChanged += (_, _) => UpdateTemperatureTimerInterval();
        SourceInitialized += (_, _) => ApplyDarkWindowFrame();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var startupBeganUtc = DateTime.UtcNow;
        UpdateStartupProgress(10, "正在加载设置与界面组件…");
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        LoadSettingsIntoUi();
        _uiReady = true;
        ApplyOverlay();
        _trayIcon = new TrayIconService(
            Dispatcher,
            ShowMainWindow,
            ToggleOverlay,
            () => ExitGameMode(true),
            Close);
        try
        {
            _hotkeyService = new GlobalHotkeyService(this, HandleGlobalHotkey);
            RegisterHotkeys(false);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"全局快捷键初始化失败：{ex.Message}";
        }

        UpdateStartupProgress(28, "正在检查 CPU 底层温度通道…");
        StatusText.Text = "正在初始化全部硬件传感器…";
        try
        {
            UpdateStartupProgress(42, "正在初始化硬件传感器库…");
            _temperatureService = await Task.Run(() => new TemperatureService(_settings));
            UpdateStartupProgress(72, "正在读取第一批实时温度…");
            UpdateTemperatureTimerInterval();
            _temperatureTimer.Start();
            await RefreshTemperaturesAsync();
            UpdateStartupProgress(100, _temperatures.Count > 0 ? $"已连接 {_temperatures.Count} 个温度项目" : "温度检测已完成");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"温度传感器初始化失败：{ex.Message}";
            AccessHintText.Text = "温度模块未能启动，请检查硬件驱动是否正常。";
            UpdateStartupProgress(100, "温度模块初始化未完成，已保留其他硬件检测功能");
        }
        var remainingDisplayTime = TimeSpan.FromMilliseconds(2200) - (DateTime.UtcNow - startupBeganUtc);
        if (remainingDisplayTime > TimeSpan.Zero) await Task.Delay(remainingDisplayTime);
        StartupTemperatureOverlay.Visibility = Visibility.Collapsed;

        await ScanHardwareAsync();
    }

    private void UpdateStartupProgress(double value, string stage)
    {
        StartupTemperatureProgress.Value = value;
        StartupTemperatureStageText.Text = stage;
        StartupTemperaturePercentText.Text = $"{value:F0}%";
    }

    private async Task ScanHardwareAsync()
    {
        RescanButton.IsEnabled = false;
        StatusText.Text = "正在扫描电脑上的硬件，请稍候…";
        try
        {
            _snapshot = await _inventoryService.ScanAsync();
            CpuNameText.Text = _snapshot.CpuName;
            GpuNameText.Text = _snapshot.GpuName;
            MemoryText.Text = _snapshot.MemorySummary;
            StorageText.Text = _snapshot.StorageSummary;
            MachineText.Text = $"{_snapshot.ComputerModel}  ·  {_snapshot.OperatingSystem}  ·  电脑名称：{_snapshot.ComputerName}";

            BuildHardwareGroups(HardwareSearchBox.Text);
            var categoryCount = _snapshot.Devices.Select(x => x.Category).Distinct().Count();
            DeviceCountText.Text = _snapshot.Devices.Count.ToString();
            HardwareCategoryCountText.Text = categoryCount.ToString();
            HardwareCountText.Text = $"共 {_snapshot.Devices.Count} 项硬件 · {categoryCount} 个分类";
            StatusText.Text = $"硬件检测完成：发现 {_snapshot.Devices.Count} 项设备";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"硬件扫描失败：{ex.Message}";
        }
        finally
        {
            RescanButton.IsEnabled = true;
        }
    }

    private void BuildHardwareGroups(string? searchText)
    {
        _hardwareGroups.Clear();
        if (_snapshot is null) return;
        var query = searchText?.Trim();
        var source = string.IsNullOrWhiteSpace(query)
            ? _snapshot.Devices
            : _snapshot.Devices.Where(d => d.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var grouping in source.GroupBy(x => x.Category).OrderBy(x => CategoryOrder(x.Key)).ThenBy(x => x.Key))
        {
            _hardwareGroups.Add(new HardwareGroup
            {
                Category = grouping.Key,
                Devices = grouping.OrderBy(x => x.Name).ToList(),
                IsExpanded = !string.IsNullOrWhiteSpace(query)
            });
        }
    }

    private static int CategoryOrder(string category) => category switch
    {
        "处理器" => 0, "显卡" or "显示设备" => 1, "内存" => 2, "主板" => 3,
        "存储" => 4, "显示器" => 5, "网络" => 6, "音频" => 7, "USB 设备" => 8,
        "蓝牙" => 9, "系统设备" => 10, _ => 50
    };

    private async Task RefreshTemperaturesAsync()
    {
        if (_temperatureBusy || _temperatureService is null) return;
        _temperatureBusy = true;
        try
        {
            var scan = await _temperatureService.ReadAsync(_settings);
            var samples = scan.Samples;
            foreach (var sample in samples)
            {
                if (!_temperatureById.TryGetValue(sample.Id, out var reading))
                {
                    reading = new TemperatureReading
                    {
                        Id = sample.Id,
                        Category = sample.Category,
                        HardwareName = sample.HardwareName,
                        SensorName = sample.SensorName,
                        Description = sample.Description,
                        IsThreshold = sample.IsThreshold,
                        IsOverlay = _settings.OverlaySensorIds.Contains(sample.Id)
                    };
                    reading.PropertyChanged += Temperature_PropertyChanged;
                    _temperatures.Add(reading);
                    _temperatureById.Add(sample.Id, reading);
                }
                reading.Update(sample.Value);
            }

            if (!_settings.OverlaySelectionInitialized && _temperatures.Count > 0)
                SelectUsefulOverlayDefaults();

            SensorCountText.Text = _temperatures.Count.ToString();
            SidebarSensorCount.Text = $"{_temperatures.Count} 个在线";
            LastUpdatedText.Text = $"温度更新 {DateTime.Now:HH:mm:ss} · 扫描 {scan.ElapsedMilliseconds} 毫秒";
            UpdateTemperatureDiagnostics(scan.Diagnostics);
            if (_temperatures.Count == 0)
            {
                SidebarSensorCount.Text = "未发现温度";
            }
            ApplyOverlay();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"温度刷新失败：{ex.Message}";
        }
        finally
        {
            _temperatureBusy = false;
        }
    }

    private void UpdateTemperatureDiagnostics(TemperatureDiagnostics diagnostics)
    {
        if (diagnostics.CpuTemperatureDetected)
        {
            AccessHintText.Text = $"已读取 {diagnostics.CpuHardwareName} 的温度。没有物理温度传感器的设备不会显示温度。";
            TemperatureDriverHintText.Text = "勾选“桌面显示”后，该温度会出现在设置所选的桌面角落。最多显示 12 项。";
            SetDriverButtons(Visibility.Collapsed, "安装 CPU 温度支持");
            return;
        }

        if (!diagnostics.PawnIoInstalled)
        {
            AccessHintText.Text = $"{diagnostics.CpuSupportNote} 处理器温度需要读取低层寄存器，Windows 仍需要安全的 PawnIO 内核通道。";
            TemperatureDriverHintText.Text = "CPU 温度尚未启用：可安装官方安全底层支持；显卡、硬盘和 Windows ACPI 热区不受影响。";
            SetDriverButtons(Visibility.Visible, "启用 CPU 深度检测");
        }
        else if (!diagnostics.LowLevelAccessAvailable)
        {
            AccessHintText.Text = $"{diagnostics.CpuSupportNote} PawnIO 已安装，但当前无法连接底层设备；若仍无效，请重新启动 Windows 或修复该驱动。";
            TemperatureDriverHintText.Text = "CPU 底层通道当前不可用；这不是缺少 DLL，可重启系统或点击修复。";
            SetDriverButtons(Visibility.Visible, "修复 CPU 底层支持");
        }
        else
        {
            AccessHintText.Text = $"{diagnostics.CpuSupportNote} 底层通道已连接，但处理器暂未返回有效温度。可能是主板固件或安全软件的兼容性限制。";
            TemperatureDriverHintText.Text = "已连接安全底层驱动，但 CPU 的 MSR 温度值暂不可用；不会用 ACPI 机身热区冒充 CPU 核心温度。";
            SetDriverButtons(Visibility.Visible, "重新检测 CPU 温度");
        }
    }

    private void SetDriverButtons(Visibility visibility, string text)
    {
        InstallSensorDriverButton.Visibility = visibility;
        TemperatureDriverButton.Visibility = visibility;
        InstallSensorDriverButton.Content = text;
        TemperatureDriverButton.Content = text;
    }

    private async void InstallSensorDriver_Click(object sender, RoutedEventArgs e)
    {
        if (HardwareAccessDriverService.IsInstalled() && HardwareAccessDriverService.IsDriverAvailable())
        {
            InstallSensorDriverButton.IsEnabled = false;
            TemperatureDriverButton.IsEnabled = false;
            StatusText.Text = "正在重新初始化 CPU 温度传感器…";
            try
            {
                await RestartTemperatureServiceAsync();
                if (!_temperatures.Any(x => x.Category == "处理器"))
                    MessageBox.Show(this, "底层通道已正常连接，但该处理器仍未返回有效温度值。软件不会用不准确的 ACPI 热区数值冒充 CPU 核心温度。", "CPU 温度暂不可用", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"CPU 温度重新检测失败：{ex.Message}";
            }
            finally
            {
                InstallSensorDriverButton.IsEnabled = true;
                TemperatureDriverButton.IsEnabled = true;
            }
            return;
        }

        var answer = MessageBox.Show(
            this,
            "读取处理器和主板温度需要安装 PawnIO 官方低层硬件驱动。\n\n软件将从官方发布地址下载 PawnIO 2.2，校验文件后打开安装程序。是否继续？",
            "启用 CPU 深度检测",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes) return;

        InstallSensorDriverButton.IsEnabled = false;
        TemperatureDriverButton.IsEnabled = false;
        StatusText.Text = "正在下载并校验官方温度驱动…";
        try
        {
            await HardwareAccessDriverService.DownloadAndRunInstallerAsync();
            if (HardwareAccessDriverService.IsInstalled())
            {
                StatusText.Text = "温度驱动安装完成，正在重新读取处理器传感器…";
                await RestartTemperatureServiceAsync();
                if (!_temperatures.Any(x => x.Category == "处理器"))
                {
                    MessageBox.Show(this, "驱动已安装。若处理器温度仍未出现，请关闭并重新打开别离检测工具，必要时重新启动 Windows。", "安装完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                StatusText.Text = "温度驱动安装未完成";
                MessageBox.Show(this, "未检测到已安装的温度驱动。请重新点击按钮，并在安装程序中完成安装。", "安装未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"温度驱动安装失败：{ex.Message}";
            MessageBox.Show(this, $"温度驱动安装失败：\n{ex.Message}", "别离检测工具", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            InstallSensorDriverButton.IsEnabled = true;
            TemperatureDriverButton.IsEnabled = true;
        }
    }

    private async Task RestartTemperatureServiceAsync()
    {
        _temperatureTimer.Stop();
        _temperatureService?.Dispose();
        foreach (var reading in _temperatures) reading.PropertyChanged -= Temperature_PropertyChanged;
        _temperatures.Clear();
        _temperatureById.Clear();
        _overlayReadings.Clear();
        _temperatureService = await Task.Run(() => new TemperatureService(_settings));
        _temperatureTimer.Start();
        await RefreshTemperaturesAsync();
    }

    private void SelectUsefulOverlayDefaults()
    {
        _syncingSensors = true;
        var defaults = _temperatures
            .Where(r => !r.IsThreshold && (r.SensorName.Contains("封装", StringComparison.OrdinalIgnoreCase)
                     || r.SensorName.Contains("核心", StringComparison.OrdinalIgnoreCase)
                     || r.SensorName.Contains("热点", StringComparison.OrdinalIgnoreCase)))
            .GroupBy(r => r.Category)
            .Select(g => g.First())
            .Take(4)
            .ToList();
        if (defaults.Count == 0) defaults = _temperatures.Where(x => !x.IsThreshold).Take(3).ToList();
        foreach (var reading in defaults) reading.IsOverlay = true;
        _settings.OverlaySensorIds = defaults.Select(x => x.Id).ToList();
        _settings.OverlaySelectionInitialized = true;
        _syncingSensors = false;
        TrySaveSettings();
    }

    private void Temperature_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TemperatureReading.IsOverlay) || _syncingSensors) return;
        var selected = _temperatures.Where(x => x.IsOverlay).ToList();
        if (selected.Count > 12 && sender is TemperatureReading changed)
        {
            _syncingSensors = true;
            changed.IsOverlay = false;
            _syncingSensors = false;
            StatusText.Text = "桌面悬浮窗最多可选择 12 个温度项目";
            return;
        }
        _settings.OverlaySensorIds = selected.Select(x => x.Id).ToList();
        _settings.OverlaySelectionInitialized = true;
        TrySaveSettings();
        ApplyOverlay();
    }

    private void LoadSettingsIntoUi()
    {
        SelectByTag(IntervalComboBox, _settings.UpdateIntervalSeconds.ToString());
        CpuCheckBox.IsChecked = _settings.MonitorCpu;
        GpuCheckBox.IsChecked = _settings.MonitorGpu;
        StorageCheckBox.IsChecked = _settings.MonitorStorage;
        MotherboardCheckBox.IsChecked = _settings.MonitorMotherboard;
        OverlayEnabledCheckBox.IsChecked = _settings.OverlayEnabled;
        SelectByContent(PositionComboBox, _settings.OverlayPosition);
        SelectByContent(OverlayModeComboBox, _settings.OverlayMode);
        SelectByContent(OverlayHardwareLabelModeComboBox, _settings.OverlayHardwareLabelMode);
        FontSizeSlider.Value = _settings.OverlayFontSize;
        OverlayOpacitySlider.Value = _settings.OverlayOpacity;
        BackgroundOpacitySlider.Value = _settings.OverlayBackgroundOpacity;
        ShowMinCheckBox.IsChecked = _settings.OverlayShowMinimum;
        ShowMaxCheckBox.IsChecked = _settings.OverlayShowMaximum;
        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        RefreshHotkeyButtonText();
    }

    private void ReadOverlaySettingsFromUi()
    {
        _settings.OverlayEnabled = OverlayEnabledCheckBox.IsChecked == true;
        _settings.OverlayPosition = SelectedContent(PositionComboBox, "右上角");
        _settings.OverlayMode = SelectedContent(OverlayModeComboBox, "自由拖动");
        _settings.OverlayHardwareLabelMode = SelectedContent(OverlayHardwareLabelModeComboBox, "硬件规格");
        _settings.OverlayFontSize = (int)Math.Round(FontSizeSlider.Value);
        _settings.OverlayOpacity = OverlayOpacitySlider.Value;
        _settings.OverlayBackgroundOpacity = BackgroundOpacitySlider.Value;
        _settings.OverlayShowMinimum = ShowMinCheckBox.IsChecked == true;
        _settings.OverlayShowMaximum = ShowMaxCheckBox.IsChecked == true;
    }

    private void ApplyOverlay()
    {
        var readings = GetOverlayReadings();
        foreach (var reading in readings)
            reading.SetOverlayHardwareLabelMode(_settings.OverlayHardwareLabelMode);
        SynchronizeOverlayReadings(readings);
        if (!_settings.OverlayEnabled && _overlay is null) return;
        EnsureOverlayWindow().Apply(_settings, readings);
    }

    private OverlayWindow EnsureOverlayWindow()
    {
        if (_overlay is not null) return _overlay;
        _overlay = new OverlayWindow();
        _overlay.PositionChanged += Overlay_PositionChanged;
        return _overlay;
    }

    private List<TemperatureReading> GetOverlayReadings()
    {
        var source = _overlayContentCategory is null
            ? _temperatures.Where(x => x.IsOverlay)
            : _temperatures.Where(x => x.Category == _overlayContentCategory);
        return source.Where(x => !x.IsThreshold).Take(12).ToList();
    }

    private void SynchronizeOverlayReadings(IReadOnlyList<TemperatureReading> readings)
    {
        for (var targetIndex = 0; targetIndex < readings.Count; targetIndex++)
        {
            var target = readings[targetIndex];
            if (targetIndex < _overlayReadings.Count && _overlayReadings[targetIndex].Id == target.Id) continue;

            var existingIndex = -1;
            for (var i = targetIndex + 1; i < _overlayReadings.Count; i++)
            {
                if (_overlayReadings[i].Id != target.Id) continue;
                existingIndex = i;
                break;
            }
            if (existingIndex >= 0) _overlayReadings.Move(existingIndex, targetIndex);
            else _overlayReadings.Insert(targetIndex, target);
        }
        while (_overlayReadings.Count > readings.Count) _overlayReadings.RemoveAt(_overlayReadings.Count - 1);
        OverviewOverlayEmptyText.Visibility = readings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTemperatureTimerInterval()
    {
        var seconds = Math.Max(1, _settings.UpdateIntervalSeconds);
        if ((!IsVisible || WindowState == WindowState.Minimized) && !_settings.OverlayEnabled)
            seconds = Math.Max(seconds, 10);
        _temperatureTimer.Interval = TimeSpan.FromSeconds(seconds);
    }

    private void Overlay_PositionChanged(double left, double top)
    {
        _settings.OverlayLeft = left;
        _settings.OverlayTop = top;
        _settings.OverlayHasCustomPosition = true;
        TrySaveSettings();
        StatusText.Text = "悬浮窗位置已记住";
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var previousMonitorCpu = _settings.MonitorCpu;
        var previousMonitorGpu = _settings.MonitorGpu;
        var previousMonitorStorage = _settings.MonitorStorage;
        var previousMonitorMotherboard = _settings.MonitorMotherboard;
        var selectedInterval = IntervalComboBox.SelectedItem as ComboBoxItem;
        _settings.UpdateIntervalSeconds = int.TryParse(selectedInterval?.Tag?.ToString(), out var interval) ? interval : 2;
        _settings.MonitorCpu = CpuCheckBox.IsChecked == true;
        _settings.MonitorGpu = GpuCheckBox.IsChecked == true;
        _settings.MonitorStorage = StorageCheckBox.IsChecked == true;
        _settings.MonitorMotherboard = MotherboardCheckBox.IsChecked == true;
        ReadOverlaySettingsFromUi();
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        UpdateTemperatureTimerInterval();
        TrySaveSettings();
        RegisterHotkeys(false);
        ApplyOverlay();
        StatusText.Text = "设置已保存并立即应用";
        var monitorProfileChanged = previousMonitorCpu != _settings.MonitorCpu
            || previousMonitorGpu != _settings.MonitorGpu
            || previousMonitorStorage != _settings.MonitorStorage
            || previousMonitorMotherboard != _settings.MonitorMotherboard;
        if (monitorProfileChanged) await RestartTemperatureServiceAsync();
        else await RefreshTemperaturesAsync();
    }

    private void ResetOverlayPosition_Click(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        ReadOverlaySettingsFromUi();
        _settings.OverlayHasCustomPosition = false;
        EnsureOverlayWindow().ResetPosition(_settings);
        StatusText.Text = $"悬浮窗位置已重置到{_settings.OverlayPosition}";
    }

    private void GameMode_Click(object sender, RoutedEventArgs e)
    {
        if (_gameMode) ExitGameMode(true);
        else EnterGameMode();
    }

    private void EnterGameMode()
    {
        _gameMode = true;
        _settings.OverlayEnabled = true;
        OverlayEnabledCheckBox.IsChecked = true;
        EnsureOverlayWindow().SetGameMode(true);
        ApplyOverlay();
        UpdateGameModeButtons();
        TrySaveSettings();
        _trayIcon?.SetVisible(true);
        _trayIcon?.ShowNotification(
            "游戏模式已启动",
            $"主界面已收至通知区域。{GlobalHotkeyService.ToDisplayText(_settings.GameModeHotkey)} 可退出游戏模式。",
            2600);
        Hide();
    }

    private void ExitGameMode(bool showMainWindow)
    {
        _gameMode = false;
        _overlay?.SetGameMode(false);
        ApplyOverlay();
        UpdateGameModeButtons();
        _trayIcon?.SetVisible(false);
        if (showMainWindow) ShowMainWindow();
        StatusText.Text = "已退出游戏模式";
    }

    private void ShowMainWindow()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void UpdateGameModeButtons()
    {
        var text = _gameMode ? "退出游戏模式" : "进入游戏模式";
        GameModeButton.Content = text;
        SettingsGameModeButton.Content = _gameMode ? "退出游戏模式" : "立即进入游戏模式";
    }

    private void ToggleOverlay()
    {
        _settings.OverlayEnabled = !_settings.OverlayEnabled;
        OverlayEnabledCheckBox.IsChecked = _settings.OverlayEnabled;
        UpdateTemperatureTimerInterval();
        TrySaveSettings();
        ApplyOverlay();
        var message = _settings.OverlayEnabled ? "温度悬浮窗已显示" : "温度悬浮窗已隐藏";
        StatusText.Text = message;
        if (!IsVisible) _trayIcon?.ShowNotification("别离检测工具", message, 1400);
    }

    private void CycleOverlayContent()
    {
        var categories = _temperatures
            .Where(x => !x.IsThreshold)
            .Select(x => x.Category)
            .Distinct()
            .OrderBy(CategoryOrder)
            .ThenBy(x => x)
            .ToList();
        if (categories.Count == 0)
        {
            StatusText.Text = "尚未发现可切换的温度项目";
            return;
        }

        if (_overlayContentCategory is null)
        {
            _overlayContentCategory = categories[0];
        }
        else
        {
            var index = categories.IndexOf(_overlayContentCategory);
            _overlayContentCategory = index >= 0 && index < categories.Count - 1 ? categories[index + 1] : null;
        }
        ApplyOverlay();
        var label = _overlayContentCategory is null ? "已勾选的温度项目" : $"{_overlayContentCategory}温度";
        StatusText.Text = $"悬浮窗内容：{label}";
        if (!IsVisible) _trayIcon?.ShowNotification("悬浮窗内容已切换", label, 1300);
    }

    private void HandleGlobalHotkey(int id)
    {
        switch (id)
        {
            case GlobalHotkeyService.GameModeId:
                if (_gameMode) ExitGameMode(true);
                else EnterGameMode();
                break;
            case GlobalHotkeyService.OverlayToggleId:
                ToggleOverlay();
                break;
            case GlobalHotkeyService.OverlayContentId:
                CycleOverlayContent();
                break;
        }
    }

    private void CaptureHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string settingName } button) return;
        CancelHotkeyCapture(false);
        _capturingHotkeySetting = settingName;
        _capturingHotkeyButton = button;
        _hotkeyService?.UnregisterAll();
        button.Content = "请按下组合键…";
        StatusText.Text = "请按下包含 Ctrl、Alt、Shift 或 Win 的快捷键；按 Esc 取消";
        Focus();
        Keyboard.Focus(this);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingHotkeySetting is null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CancelHotkeyCapture(true);
            e.Handled = true;
            return;
        }
        if (IsModifierKey(key)) return;

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            StatusText.Text = "快捷键至少需要包含 Ctrl、Alt、Shift 或 Win 中的一个按键";
            e.Handled = true;
            return;
        }

        var gesture = GlobalHotkeyService.Format(key, modifiers);
        if (IsDuplicateHotkey(_capturingHotkeySetting, gesture))
        {
            StatusText.Text = "这个组合键已经用于另一项功能，请换一个组合键";
            e.Handled = true;
            return;
        }

        SetHotkeyValue(_capturingHotkeySetting, gesture);
        _capturingHotkeySetting = null;
        _capturingHotkeyButton = null;
        RefreshHotkeyButtonText();
        TrySaveSettings();
        RegisterHotkeys(true);
        e.Handled = true;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private bool IsDuplicateHotkey(string settingName, string gesture)
    {
        return (settingName != nameof(AppSettings.GameModeHotkey) && string.Equals(_settings.GameModeHotkey, gesture, StringComparison.OrdinalIgnoreCase))
            || (settingName != nameof(AppSettings.OverlayToggleHotkey) && string.Equals(_settings.OverlayToggleHotkey, gesture, StringComparison.OrdinalIgnoreCase))
            || (settingName != nameof(AppSettings.OverlayContentHotkey) && string.Equals(_settings.OverlayContentHotkey, gesture, StringComparison.OrdinalIgnoreCase));
    }

    private void SetHotkeyValue(string settingName, string value)
    {
        switch (settingName)
        {
            case nameof(AppSettings.GameModeHotkey): _settings.GameModeHotkey = value; break;
            case nameof(AppSettings.OverlayToggleHotkey): _settings.OverlayToggleHotkey = value; break;
            case nameof(AppSettings.OverlayContentHotkey): _settings.OverlayContentHotkey = value; break;
        }
    }

    private void CancelHotkeyCapture(bool showStatus)
    {
        if (_capturingHotkeySetting is null) return;
        _capturingHotkeySetting = null;
        _capturingHotkeyButton = null;
        RefreshHotkeyButtonText();
        RegisterHotkeys(false);
        if (showStatus) StatusText.Text = "已取消修改快捷键";
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        CancelHotkeyCapture(false);
        _settings.GameModeHotkey = GlobalHotkeyService.DefaultGameMode;
        _settings.OverlayToggleHotkey = GlobalHotkeyService.DefaultOverlayToggle;
        _settings.OverlayContentHotkey = GlobalHotkeyService.DefaultOverlayContent;
        RefreshHotkeyButtonText();
        TrySaveSettings();
        RegisterHotkeys(true);
    }

    private void RefreshHotkeyButtonText()
    {
        GameModeHotkeyButton.Content = GlobalHotkeyService.ToDisplayText(_settings.GameModeHotkey);
        OverlayToggleHotkeyButton.Content = GlobalHotkeyService.ToDisplayText(_settings.OverlayToggleHotkey);
        OverlayContentHotkeyButton.Content = GlobalHotkeyService.ToDisplayText(_settings.OverlayContentHotkey);
    }

    private void RegisterHotkeys(bool reportSuccess)
    {
        if (_hotkeyService is null) return;
        var failures = _hotkeyService.RegisterAll(_settings);
        if (failures.Count > 0)
        {
            StatusText.Text = string.Join("；", failures);
        }
        else if (reportSuccess)
        {
            StatusText.Text = "快捷键已保存并立即生效";
        }
    }

    private void TrySaveSettings()
    {
        try { _settingsService.Save(_settings); }
        catch (Exception ex) { StatusText.Text = $"设置保存失败：{ex.Message}"; }
    }

    private void ResetRange_Click(object sender, RoutedEventArgs e)
    {
        foreach (var reading in _temperatures) reading.ResetRange();
        ApplyOverlay();
        StatusText.Text = "所有传感器的最低 / 最高温度已重新开始统计";
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e) => await ScanHardwareAsync();

    private async void ExportHardware_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null)
        {
            MessageBox.Show(this, "硬件信息仍在检测中，请稍后再导出。", "别离检测工具", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var exportDialog = new ExportDialog(_snapshot, _temperatures.Count) { Owner = this };
        if (exportDialog.ShowDialog() != true) return;

        var format = exportDialog.SelectedFormat;
        var extension = format switch
        {
            HardwareExportFormat.Word => ".docx",
            HardwareExportFormat.Excel => ".xlsx",
            _ => ".txt"
        };
        var filter = format switch
        {
            HardwareExportFormat.Word => "Word 文档 (*.docx)|*.docx",
            HardwareExportFormat.Excel => "Excel 表格 (*.xlsx)|*.xlsx",
            _ => "文本文件 (*.txt)|*.txt"
        };
        var saveDialog = new SaveFileDialog
        {
            Title = "保存硬件信息",
            Filter = filter,
            DefaultExt = extension,
            AddExtension = true,
            FileName = $"别离检测工具_硬件信息_{DateTime.Now:yyyyMMdd_HHmmss}{extension}"
        };
        if (saveDialog.ShowDialog(this) != true) return;

        StatusText.Text = "正在生成硬件信息文件…";
        try
        {
            await _exportService.ExportAsync(
                saveDialog.FileName,
                format,
                _snapshot,
                _temperatures,
                exportDialog.SelectedSectionKeys);
            StatusText.Text = $"硬件信息已导出：{saveDialog.FileName}";
            MessageBox.Show(this, $"导出完成：\n{saveDialog.FileName}", "别离检测工具", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"导出失败：{ex.Message}";
            MessageBox.Show(this, $"导出失败：\n{ex.Message}", "别离检测工具", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HardwareSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        BuildHardwareGroups(HardwareSearchBox.Text);
        var visible = _hardwareGroups.Sum(x => x.Count);
        HardwareCountText.Text = string.IsNullOrWhiteSpace(HardwareSearchBox.Text)
            ? $"共 {_snapshot?.Devices.Count ?? 0} 项硬件 · {_hardwareGroups.Count} 个分类"
            : $"找到 {visible} 项 · {_hardwareGroups.Count} 个分类";
    }

    private void GroupToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: HardwareGroup group }) group.IsExpanded = !group.IsExpanded;
    }

    private void GroupHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not Border { DataContext: HardwareGroup group }) return;
        if (FindParent<Button>(e.OriginalSource as DependencyObject) is not null) return;
        group.IsExpanded = !group.IsExpanded;
        e.Handled = true;
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match) return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void OverlayPreview_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        ReadOverlaySettingsFromUi();
        UpdateTemperatureTimerInterval();
        ApplyOverlay();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { CommandParameter: string page }) ShowPage(page);
    }

    private void ShowPage(string pageName)
    {
        HardwareGroupsControl.ItemsSource = pageName == nameof(HardwarePage) ? _hardwareGroups : null;
        TemperatureGrid.ItemsSource = pageName == nameof(TemperaturePage) ? _temperatures : null;
        foreach (var page in new UIElement[] { OverviewPage, HardwarePage, TemperaturePage, SettingsPage })
            page.Visibility = page is FrameworkElement element && element.Name == pageName ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SelectByContent(ComboBox box, string content)
    {
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Content?.ToString() == content) ?? box.Items[0];
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == tag) ?? box.Items[0];
    }

    private static string SelectedContent(ComboBox box, string fallback) =>
        (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ApplyDarkWindowFrame()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var darkMode = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));
        var borderColor = 0x001B0E09; // COLORREF：#090E1B
        _ = DwmSetWindowAttribute(handle, 34, ref borderColor, sizeof(int));
        var cornerPreference = 1; // DWMWCP_DONOTROUND，避免顶部圆角露出浅色系统边缘。
        _ = DwmSetWindowAttribute(handle, 33, ref cornerPreference, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _temperatureTimer.Stop();
        TrySaveSettings();
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _temperatureService?.Dispose();
        if (_overlay?.IsVisible == true) _overlay.Close();
    }
}

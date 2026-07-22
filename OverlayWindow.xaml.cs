using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareScope.Models;

namespace HardwareScope;

public partial class OverlayWindow : Window
{
    private const double EdgeRevealSize = 9;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly Dictionary<string, TextBlock> _readingTextBlocks = new(StringComparer.Ordinal);
    private readonly List<string> _renderedSensorIds = [];
    private AppSettings? _settings;
    private bool _positionInitialized;
    private bool _isHiddenAtEdge;
    private string _edge = "右";
    private string _lastMode = string.Empty;
    private double _visibleLeft;
    private double _visibleTop;
    private bool _gameMode;
    private int _backgroundAlpha = -1;

    public event Action<double, double>? PositionChanged;

    public OverlayWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            HideToEdge();
        };
    }

    public void Apply(AppSettings settings, IEnumerable<TemperatureReading> readings)
    {
        _settings = settings;
        if (!settings.OverlayEnabled)
        {
            _hideTimer.Stop();
            Hide();
            return;
        }
        if (!Topmost) Topmost = true;
        if (Math.Abs(Opacity - settings.OverlayOpacity) > 0.001) Opacity = settings.OverlayOpacity;
        var alpha = (byte)Math.Clamp(settings.OverlayBackgroundOpacity * 255, 0, 255);
        if (_backgroundAlpha != alpha)
        {
            OverlayCard.Background = new SolidColorBrush(Color.FromArgb(alpha, 12, 18, 35));
            _backgroundAlpha = alpha;
        }
        OverlayCard.Cursor = settings.OverlayMode == "固定位置" ? Cursors.Arrow : Cursors.SizeAll;
        ModeHintText.Text = _gameMode ? "游戏置顶" : settings.OverlayMode switch
        {
            "固定位置" => "已固定",
            "自动靠边隐藏" => "靠边隐藏",
            _ => "拖动移动"
        };
        RenderReadings(settings, readings);

        if (!IsVisible) Show();
        UpdateLayout();
        if (!_positionInitialized)
        {
            InitializePosition(settings);
        }
        else if (_lastMode != settings.OverlayMode)
        {
            RevealFromEdge();
            if (settings.OverlayMode == "自动靠边隐藏" && !_gameMode)
            {
                SnapToNearestEdge();
                ScheduleHide();
            }
        }
        _lastMode = settings.OverlayMode;
    }

    public void SetGameMode(bool enabled)
    {
        _gameMode = enabled;
        Topmost = true;
        if (enabled)
        {
            _hideTimer.Stop();
            RevealFromEdge();
        }
        else if (_settings?.OverlayMode == "自动靠边隐藏")
        {
            SnapToNearestEdge();
            ScheduleHide();
        }
    }

    public void ResetPosition(AppSettings settings)
    {
        _settings = settings;
        _hideTimer.Stop();
        _isHiddenAtEdge = false;
        PlacePreset(settings.OverlayPosition);
        _positionInitialized = true;
        if (settings.OverlayMode == "自动靠边隐藏" && !_gameMode)
        {
            SnapToNearestEdge();
            ScheduleHide();
        }
        PositionChanged?.Invoke(_visibleLeft, _visibleTop);
    }

    private void RenderReadings(AppSettings settings, IEnumerable<TemperatureReading> readings)
    {
        var selected = readings.Take(12).ToList();
        var structureChanged = selected.Count != _renderedSensorIds.Count
            || (selected.Count == 0 && ReadingsPanel.Children.Count == 0);
        if (!structureChanged)
        {
            for (var i = 0; i < selected.Count; i++)
            {
                if (selected[i].Id == _renderedSensorIds[i]) continue;
                structureChanged = true;
                break;
            }
        }
        if (structureChanged)
        {
            ReadingsPanel.Children.Clear();
            _readingTextBlocks.Clear();
            _renderedSensorIds.Clear();
            if (selected.Count == 0)
            {
                ReadingsPanel.Children.Add(new TextBlock
                {
                    Text = "请在温度监控中选择显示项目",
                    Foreground = new SolidColorBrush(Color.FromRgb(157, 168, 196)),
                    FontSize = Math.Max(11, settings.OverlayFontSize - 1),
                    Margin = new Thickness(0, 2, 0, 1)
                });
                return;
            }
            foreach (var reading in selected)
            {
                var block = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 3, 0, 3)
                };
                _readingTextBlocks.Add(reading.Id, block);
                _renderedSensorIds.Add(reading.Id);
                ReadingsPanel.Children.Add(block);
            }
        }

        foreach (var reading in selected)
        {
            var block = _readingTextBlocks[reading.Id];
            var line = $"{reading.OverlayDisplayTitle}   {reading.Current:F1}°";
            if (settings.OverlayShowMinimum) line += $"  最低 {reading.Minimum:F0}°";
            if (settings.OverlayShowMaximum) line += $"  最高 {reading.Maximum:F0}°";
            if (block.Text != line) block.Text = line;
            if (Math.Abs(block.FontSize - settings.OverlayFontSize) > 0.01) block.FontSize = settings.OverlayFontSize;
        }
    }

    private void InitializePosition(AppSettings settings)
    {
        if (settings.OverlayHasCustomPosition && double.IsFinite(settings.OverlayLeft) && double.IsFinite(settings.OverlayTop))
        {
            var area = SystemParameters.WorkArea;
            Left = Math.Clamp(settings.OverlayLeft, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
            Top = Math.Clamp(settings.OverlayTop, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
            _visibleLeft = Left;
            _visibleTop = Top;
        }
        else
        {
            PlacePreset(settings.OverlayPosition);
        }
        _positionInitialized = true;
        if (settings.OverlayMode == "自动靠边隐藏" && !_gameMode)
        {
            SnapToNearestEdge();
            ScheduleHide();
        }
    }

    private void PlacePreset(string position)
    {
        const double margin = 18;
        var area = SystemParameters.WorkArea;
        Left = position.Contains("右") ? area.Right - ActualWidth - margin : area.Left + margin;
        Top = position.Contains("下") ? area.Bottom - ActualHeight - margin : area.Top + margin;
        _visibleLeft = Left;
        _visibleTop = Top;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _settings?.OverlayMode == "固定位置") return;
        _hideTimer.Stop();
        RevealFromEdge();
        try
        {
            DragMove();
            ClampToWorkArea();
            if (_settings?.OverlayMode == "自动靠边隐藏") SnapToNearestEdge();
            else
            {
                _visibleLeft = Left;
                _visibleTop = Top;
            }
            PositionChanged?.Invoke(_visibleLeft, _visibleTop);
            if (_settings?.OverlayMode == "自动靠边隐藏") ScheduleHide();
        }
        catch (InvalidOperationException)
        {
            // 鼠标在拖动开始前释放时无需处理。
        }
    }

    private void ClampToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
    }

    private void SnapToNearestEdge()
    {
        var area = SystemParameters.WorkArea;
        var distances = new Dictionary<string, double>
        {
            ["左"] = Math.Abs(Left - area.Left),
            ["右"] = Math.Abs(area.Right - (Left + ActualWidth)),
            ["上"] = Math.Abs(Top - area.Top),
            ["下"] = Math.Abs(area.Bottom - (Top + ActualHeight))
        };
        _edge = distances.MinBy(x => x.Value).Key;
        switch (_edge)
        {
            case "左": Left = area.Left; break;
            case "右": Left = area.Right - ActualWidth; break;
            case "上": Top = area.Top; break;
            case "下": Top = area.Bottom - ActualHeight; break;
        }
        ClampToWorkArea();
        _visibleLeft = Left;
        _visibleTop = Top;
    }

    private void HideToEdge()
    {
        if (_gameMode || _settings?.OverlayMode != "自动靠边隐藏" || IsMouseOver || !IsVisible) return;
        var area = SystemParameters.WorkArea;
        _visibleLeft = Left;
        _visibleTop = Top;
        switch (_edge)
        {
            case "左": Left = area.Left - ActualWidth + EdgeRevealSize; break;
            case "右": Left = area.Right - EdgeRevealSize; break;
            case "上": Top = area.Top - ActualHeight + EdgeRevealSize; break;
            case "下": Top = area.Bottom - EdgeRevealSize; break;
        }
        _isHiddenAtEdge = true;
    }

    private void RevealFromEdge()
    {
        if (!_isHiddenAtEdge) return;
        Left = _visibleLeft;
        Top = _visibleTop;
        _isHiddenAtEdge = false;
    }

    private void ScheduleHide()
    {
        _hideTimer.Stop();
        if (!_gameMode && _settings?.OverlayMode == "自动靠边隐藏") _hideTimer.Start();
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _hideTimer.Stop();
        RevealFromEdge();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e) => ScheduleHide();
}

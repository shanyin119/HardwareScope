using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace HardwareScope.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon? _applicationIcon;

    public TrayIconService(
        Dispatcher dispatcher,
        Action showMainWindow,
        Action toggleOverlay,
        Action exitGameMode,
        Action exitApplication)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示主界面", null, (_, _) => dispatcher.Invoke(showMainWindow));
        menu.Items.Add("显示 / 隐藏温度悬浮窗", null, (_, _) => dispatcher.Invoke(toggleOverlay));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出游戏模式", null, (_, _) => dispatcher.Invoke(exitGameMode));
        menu.Items.Add("退出软件", null, (_, _) => dispatcher.Invoke(exitApplication));

        try
        {
            _applicationIcon = string.IsNullOrWhiteSpace(Environment.ProcessPath)
                ? null
                : Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
        }
        catch
        {
            _applicationIcon = null;
        }

        _icon = new Forms.NotifyIcon
        {
            Text = "别离检测工具",
            Icon = _applicationIcon ?? Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = false
        };
        _icon.DoubleClick += (_, _) => dispatcher.Invoke(showMainWindow);
    }

    public void SetVisible(bool visible) => _icon.Visible = visible;

    public void ShowNotification(string title, string message, int duration = 2200)
    {
        _icon.Visible = true;
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _icon.ShowBalloonTip(duration);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _applicationIcon?.Dispose();
    }
}

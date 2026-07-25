using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;

namespace DesktopSystemMonitor.App.Tray;

/// <summary>
/// Owns the tray icon and exposes menu commands. Uses WinForms NotifyIcon
/// because WPF has no native shell notification support.
/// </summary>
public sealed class NotifyIconController : IDisposable
{
    private const string IconResourceName = "DesktopSystemMonitor.App.Assets.DesktopSystemMonitor.ico";

    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _ownedIcon;
    private readonly Forms.ToolStripMenuItem _clickThroughItem;
    private readonly Forms.ToolStripMenuItem _startupItem;
    private readonly Forms.ToolStripMenuItem _layerHeader;
    private readonly Forms.ToolStripMenuItem _layerBottomItem;
    private readonly Forms.ToolStripMenuItem _layerTopItem;
    private readonly Forms.ToolStripMenuItem _layerNormalItem;
    private readonly Forms.ToolStripMenuItem _highLoadProcessesItem;

    public event Action? ExitRequested;
    public event Action<bool>? ClickThroughToggled;
    public event Action<bool>? StartupToggled;
    public event Action<Windows.Window.LayerStrategy>? LayerRequested;
    public event Action? SettingsRequested;
    public event Action? HighLoadProcessesRequested;

    public NotifyIconController()
    {
        _ownedIcon = CreateDefaultIcon();
        _icon = new Forms.NotifyIcon
        {
            Icon = _ownedIcon,
            Visible = true,
            Text = "Desktop System Monitor",
        };
        var menu = new Forms.ContextMenuStrip();

        _clickThroughItem = new Forms.ToolStripMenuItem("クリック透過");
        _clickThroughItem.CheckOnClick = true;
        _clickThroughItem.Click += (_, _) => ClickThroughToggled?.Invoke(_clickThroughItem.Checked);

        _layerHeader = new Forms.ToolStripMenuItem("表示階層");
        _layerBottomItem = new Forms.ToolStripMenuItem("デスクトップ上");
        _layerBottomItem.Click += (_, _) => LayerRequested?.Invoke(Windows.Window.LayerStrategy.BottomMost);
        _layerTopItem = new Forms.ToolStripMenuItem("常に手前");
        _layerTopItem.Click += (_, _) => LayerRequested?.Invoke(Windows.Window.LayerStrategy.TopMost);
        _layerNormalItem = new Forms.ToolStripMenuItem("通常");
        _layerNormalItem.Click += (_, _) => LayerRequested?.Invoke(Windows.Window.LayerStrategy.Normal);
        _layerHeader.DropDownItems.Add(_layerBottomItem);
        _layerHeader.DropDownItems.Add(_layerTopItem);
        _layerHeader.DropDownItems.Add(_layerNormalItem);

        _startupItem = new Forms.ToolStripMenuItem("Windows起動時に自動起動");
        _startupItem.CheckOnClick = true;
        _startupItem.Click += (_, _) => StartupToggled?.Invoke(_startupItem.Checked);

        var settingsItem = new Forms.ToolStripMenuItem("設定...");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        _highLoadProcessesItem = new Forms.ToolStripMenuItem("高負荷プロセス...") { Visible = false };
        _highLoadProcessesItem.Click += (_, _) => HighLoadProcessesRequested?.Invoke();

        var exitItem = new Forms.ToolStripMenuItem("終了");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(_clickThroughItem);
        menu.Items.Add(_layerHeader);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_highLoadProcessesItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
    }

    public void SetHighLoadProcessMenuEnabled(bool enabled) => _highLoadProcessesItem.Visible = enabled;

    /// <summary>
    /// Enables or disables settings that can mutate application state. The
    /// settings and exit commands intentionally remain available.
    /// </summary>
    public void SetEditingEnabled(bool enabled)
    {
        _clickThroughItem.Enabled = enabled;
        _startupItem.Enabled = enabled;
        _layerHeader.Enabled = enabled;
    }

    public void SetState(bool clickThrough, bool startup, Windows.Window.LayerStrategy layer)
    {
        _clickThroughItem.Checked = clickThrough;
        _startupItem.Checked = startup;
        _layerBottomItem.Checked = layer == Windows.Window.LayerStrategy.BottomMost;
        _layerTopItem.Checked = layer == Windows.Window.LayerStrategy.TopMost;
        _layerNormalItem.Checked = layer == Windows.Window.LayerStrategy.Normal;
    }

    private static Icon CreateDefaultIcon()
    {
        using var stream = new MemoryStream(CreateDefaultIconData(), writable: false);
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    internal static byte[] CreateDefaultIconData()
    {
        using Stream stream = typeof(NotifyIconController).Assembly.GetManifestResourceStream(IconResourceName)
            ?? throw new InvalidOperationException($"Embedded icon resource '{IconResourceName}' was not found.");
        using var iconData = new MemoryStream();
        stream.CopyTo(iconData);
        return iconData.ToArray();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _ownedIcon.Dispose();
    }
}

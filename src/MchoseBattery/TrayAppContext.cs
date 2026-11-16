using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MchoseBattery;

internal sealed class TrayAppContext : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "MchoseBattery";

    private readonly AppSettings _settings;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _statusItem;

    private Icon? _icon;
    private IntPtr _iconHandle = IntPtr.Zero;
    private HidBatteryReader.Reading? _lastReading;
    private bool _polling;
    private bool _lowBatteryNotified;
    private int _failures;

    public TrayAppContext()
    {
        _settings = AppSettings.Load();

        _menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("正在读取…") { Enabled = false };
        var refreshItem = new ToolStripMenuItem("立即刷新");
        refreshItem.Click += (_, _) => Refresh();

        var rediscoverItem = new ToolStripMenuItem("重新识别设备");
        rediscoverItem.Click += (_, _) => Refresh(rediscover: true);

        var autoStartItem = new ToolStripMenuItem("开机自动启动", null, (_, _) => { })
        {
            CheckOnClick = true,
            Checked = IsAutoStartEnabled()
        };
        autoStartItem.CheckedChanged += (_, _) => SetAutoStart(autoStartItem.Checked);

        var intervalItem = new ToolStripMenuItem();
        foreach (var (label, seconds) in new[] { ("10 秒", 10), ("30 秒", 30), ("1 分钟", 60), ("5 分钟", 300) })
        {
            var item = new ToolStripMenuItem(label) { Tag = seconds };
            item.Click += (sender, _) =>
            {
                _settings.PollSeconds = seconds;
                _settings.Save();
                _timer.Interval = seconds * 1000;
                SyncIntervalMenu();
            };
            intervalItem.DropDownItems.Add(item);
        }

        void SyncIntervalMenu()
        {
            intervalItem.Text = $"刷新间隔（{FormatSeconds(_settings.PollSeconds)}）";
            foreach (ToolStripMenuItem sibling in intervalItem.DropDownItems)
                sibling.Checked = sibling.Tag is int value && value == _settings.PollSeconds;
        }

        SyncIntervalMenu();

        var aboutItem = new ToolStripMenuItem("关于我们");
        aboutItem.Click += (_, _) => MessageBox.Show(
            "作者：Daen QQ1330166564",
            "关于我们",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitThread();

        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(refreshItem);
        _menu.Items.Add(rediscoverItem);
        _menu.Items.Add(intervalItem);
        _menu.Items.Add(autoStartItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(aboutItem);
        _menu.Items.Add(exitItem);
        _menu.Opening += (_, _) =>
        {
            autoStartItem.Checked = IsAutoStartEnabled();
            SyncIntervalMenu();
        };

        _icon = BatteryIconRenderer.Create(null, false);
        _tray = new NotifyIcon
        {
            Icon = _icon,
            Visible = true,
            Text = "MCHOSE 电量：读取中…",
            ContextMenuStrip = _menu
        };
        _tray.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Refresh();
        };
        _tray.BalloonTipClicked += (_, _) => Refresh();

        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(5, _settings.PollSeconds) * 1000 };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        // Let the shell finish starting before the first HID query.
        var startupTimer = new System.Windows.Forms.Timer { Interval = 400 };
        startupTimer.Tick += (_, _) =>
        {
            startupTimer.Stop();
            startupTimer.Dispose();
            Refresh();
        };
        startupTimer.Start();
    }

    private async void Refresh(bool rediscover = false)
    {
        if (_polling) return;
        _polling = true;
        try
        {
            if (rediscover)
            {
                _settings.KnownDevices.Clear();
                _settings.Save();
            }

            var known = _settings.KnownDevices.ToList();
            var result = await Task.Run(() => HidBatteryReader.Probe(known, forceDiscovery: rediscover));
            var reading = result.Reading;
            if (reading != null)
            {
                _lastReading = reading;
                _failures = 0;
                if (result.Device != null) RememberDevice(result.Device);
            }
            else
            {
                // A sleeping mouse occasionally misses one query; only report it
                // as disconnected after several consecutive failures.
                _failures++;
                if (_failures >= 3) _lastReading = null;
            }
            UpdateUi();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            UpdateUi();
        }
        finally
        {
            _polling = false;
        }
    }

    private void UpdateUi()
    {
        var reading = _lastReading;
        int? percent = reading?.Percent;
        bool charging = reading?.IsCharging ?? false;
        bool stale = reading != null && _failures > 0;

        ReplaceIcon(BatteryIconRenderer.Create(percent, charging));

        if (reading == null)
        {
            string knownName = KnownDisplayName;
            _tray.Text = $"未检测到 {knownName}\n请确认鼠标/接收器已连接";
            _statusItem.Text = $"未检测到 {knownName}";
            return;
        }

        string mode = reading.IsWireless ? "2.4G" : "USB 有线";
        string chargeText = reading.IsCharging ? " · 充电中" : string.Empty;
        _tray.Text = $"{reading.Product}  {reading.Percent}%{chargeText}\n{mode} · 更新于 {reading.Time:HH:mm:ss}"
            + (stale ? " · 本次未响应" : "");
        _statusItem.Text = $"{reading.Product}  {reading.Percent}%{chargeText}";

        if (reading.Percent <= 15 && !reading.IsCharging && !_lowBatteryNotified)
        {
            _lowBatteryNotified = true;
            _tray.ShowBalloonTip(5000, $"{reading.Product} 电量不足",
                $"当前电量 {reading.Percent}%，请及时充电。", ToolTipIcon.Warning);
        }
        else if (reading.Percent > 25 || reading.IsCharging)
        {
            _lowBatteryNotified = false;
        }
    }

    private string KnownDisplayName =>
        _settings.KnownDevices.FirstOrDefault()?.DisplayName ?? "MCHOSE 设备";

    private static string FormatSeconds(int seconds)
    {
        if (seconds >= 60 && seconds % 60 == 0) return $"{seconds / 60} 分钟";
        return $"{seconds} 秒";
    }

    /// <summary>
    /// Stores the identity that just answered so the next launch (and the next
    /// poll) can skip discovery. The USB device path is not stored: it changes
    /// with the port, while VID/PID/usage page stay stable.
    /// </summary>
    private void RememberDevice(CachedDevice device)
    {
        var existing = _settings.KnownDevices.FirstOrDefault(d => d.Matches(device));
        if (existing == null)
        {
            _settings.KnownDevices.Insert(0, device);
            while (_settings.KnownDevices.Count > 8)
                _settings.KnownDevices.RemoveAt(_settings.KnownDevices.Count - 1);
            _settings.Save();
            return;
        }

        existing.LastSeenUtc = device.LastSeenUtc;
        if (!string.IsNullOrWhiteSpace(device.Product) && existing.Product != device.Product)
        {
            existing.Product = device.Product;
            _settings.Save();
        }
    }

    private void ReplaceIcon(Icon icon)
    {
        var oldIcon = _icon;
        _tray.Icon = icon;
        _icon = icon;

        var oldHandle = _iconHandle;
        _iconHandle = icon.Handle;
        oldIcon?.Dispose();
        if (oldHandle != IntPtr.Zero) Native.DestroyIcon(oldHandle);
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) != null;
    }

    private static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe)) key.SetValue(RunValue, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(RunValue, false);
        }
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _timer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        if (_iconHandle != IntPtr.Zero) Native.DestroyIcon(_iconHandle);
        base.ExitThreadCore();
    }
}

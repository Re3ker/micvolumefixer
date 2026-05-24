using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MicVolumeFixer;

public record VolumeChangeEntry(DateTime Time, int OldVolume, int NewVolume, string Suspects)
{
    public string FormattedTime => Time.ToString("HH:mm:ss");
    public string ChangeText => $"{OldVolume}% \u2192 {NewVolume}%  |  {Suspects}";
}

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _checkTimer;
    private readonly List<string> _deviceIds = [];
    private bool _isInitializing = true;
    private bool _reallyClose;
    private bool _startMinimized;
    private bool _monitoring;
    private bool _logExpanded;

    // ── Volume change log ───────────────────────────────────────────────
    private readonly VolumeWatcher _watcher = new();
    private readonly List<VolumeChangeEntry> _changeLog = [];
    private readonly Queue<DateTime> _recentChangeTimes = new();
    private int _warnThresholdCount = 3;
    private int _warnWindowSeconds = 10;
    private readonly DispatcherTimer _warningResetTimer;

    private static readonly string SettingsPath = Path.Combine(
        AppContext.BaseDirectory, "settings.json");
    private static readonly string LogPath = Path.Combine(
        AppContext.BaseDirectory, "mic-changes.log");

    private const string RegRun =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RegName = "MicVolumeFixer";

    public MainWindow() : this(false) { }

    public MainWindow(bool trayMode)
    {
        _startMinimized = trayMode;
        InitializeComponent();

        _checkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _checkTimer.Tick += Timer_Tick;

        _warningResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _warningResetTimer.Tick += (s, e) =>
        {
            _warningResetTimer.Stop();
            warningBanner.Visibility = Visibility.Collapsed;
        };

        _watcher.ExternalChangeDetected += OnExternalVolumeChange;
    }

    // ── Window Events ───────────────────────────────────────────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Set tray icon from embedded ICO resource
        try
        {
            var iconUri = new Uri("pack://application:,,,/microphone.ico");
            TrayIcon.IconSource = new BitmapImage(iconUri);
        }
        catch { }

        RefreshDeviceList();
        LoadSettings();
        _isInitializing = false;
        UpdateCurrentVolDisplay();

        if (_startMinimized)
        {
            WindowState = WindowState.Minimized;
            Hide();
            TrayIcon.Visibility = Visibility.Visible;
        }

        if (_monitoring)
            SetToggleStyle(true);
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyClose && cbMinimizeToTray.IsChecked == true)
        {
            e.Cancel = true;
            Hide();
            TrayIcon.Visibility = Visibility.Visible;
            TrayIcon.ShowNotification(
                "MicVolumeFixer",
                "App is still running in the tray.",
                H.NotifyIcon.Core.NotificationIcon.Info);
            return;
        }

        SaveSettings();
        _checkTimer.Stop();
        _watcher.Dispose();
        TrayIcon.Dispose();
    }

    // ── Custom Title Bar ────────────────────────────────────────────────

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ── Tray Events ─────────────────────────────────────────────────────

    private void TrayIcon_DoubleClick(object sender, RoutedEventArgs e) => ShowFromTray();
    private void TrayShow_Click(object sender, RoutedEventArgs e) => ShowFromTray();

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _reallyClose = true;
        Close();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        TrayIcon.Visibility = Visibility.Collapsed;
    }

    // ── Device List ─────────────────────────────────────────────────────

    private void RefreshDeviceList()
    {
        var devices = AudioManager.GetCaptureDevices();
        var defaultId = AudioManager.GetDefaultCaptureDeviceId();

        cboDevices.SelectionChanged -= CboDevices_SelectionChanged;
        cboDevices.Items.Clear();
        _deviceIds.Clear();

        int selectIdx = 0;
        for (int i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            string name = d.Name;
            if (!string.IsNullOrEmpty(defaultId) && d.Id == defaultId)
            {
                name += "  ★";
                selectIdx = i;
            }
            cboDevices.Items.Add(name);
            _deviceIds.Add(d.Id);
        }

        if (cboDevices.Items.Count > 0)
            cboDevices.SelectedIndex = selectIdx;

        cboDevices.SelectionChanged += CboDevices_SelectionChanged;
    }

    private string SelectedDeviceId()
    {
        int idx = cboDevices.SelectedIndex;
        return idx >= 0 && idx < _deviceIds.Count ? _deviceIds[idx] : "";
    }

    // ── Volume Display ──────────────────────────────────────────────────

    private void UpdateCurrentVolDisplay()
    {
        int vol = AudioManager.GetVolume(SelectedDeviceId());
        volumeKnob.CurrentVolume = vol < 0 ? -1 : vol;
    }

    // ── Settings (JSON file) ────────────────────────────────────────────

    private sealed class AppSettings
    {
        public int TargetVolume { get; set; } = 90;
        public bool MonitoringActive { get; set; }
        public bool MinimizeToTray { get; set; }
        public string SelectedDeviceId { get; set; } = "";
        public int WarnChangeCount { get; set; } = 3;
        public int WarnWindowSeconds { get; set; } = 10;
    }

    private void SaveSettings()
    {
        try
        {
            var settings = new AppSettings
            {
                TargetVolume = volumeKnob.TargetVolume,
                MonitoringActive = _monitoring,
                MinimizeToTray = cbMinimizeToTray.IsChecked == true,
                SelectedDeviceId = cboDevices.SelectedIndex >= 0 && cboDevices.SelectedIndex < _deviceIds.Count
                    ? _deviceIds[cboDevices.SelectedIndex]
                    : "",
                WarnChangeCount = _warnThresholdCount,
                WarnWindowSeconds = _warnWindowSeconds
            };
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            string json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings == null) return;

            // Target volume
            int v = Math.Clamp(settings.TargetVolume, 0, 100);
            volumeKnob.TargetVolume = v;

            // Monitoring active
            if (settings.MonitoringActive)
            {
                _checkTimer.Start();
                _monitoring = true;
                SetToggleStyle(true);
                // Watcher started after device is selected below
            }

            // Minimize to tray
            cbMinimizeToTray.IsChecked = settings.MinimizeToTray;

            // Autostart (read from registry to sync checkbox)
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegRun);
                cbAutostart.IsChecked = key?.GetValue(RegName) != null;
            }
            catch { }

            // Preferred device
            if (!string.IsNullOrEmpty(settings.SelectedDeviceId))
            {
                int idx = _deviceIds.IndexOf(settings.SelectedDeviceId);
                if (idx >= 0)
                    cboDevices.SelectedIndex = idx;
            }

            // Warning thresholds
            if (settings.WarnChangeCount > 0) _warnThresholdCount = settings.WarnChangeCount;
            if (settings.WarnWindowSeconds > 0) _warnWindowSeconds = settings.WarnWindowSeconds;
            txtWarnCount.Text = _warnThresholdCount.ToString();
            txtWarnSeconds.Text = _warnWindowSeconds.ToString();

            // Start watcher now that device is resolved
            if (_monitoring)
                _watcher.StartWatching(SelectedDeviceId(), volumeKnob.TargetVolume);
        }
        catch { }
    }

    // ── UI Event Handlers ───────────────────────────────────────────────

    private void CboDevices_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateCurrentVolDisplay();
        if (_monitoring)
            _watcher.StartWatching(SelectedDeviceId(), volumeKnob.TargetVolume);
        if (!_isInitializing) SaveSettings();
    }

    private void VolumeKnob_TargetVolumeChanged(object? sender, EventArgs e)
    {
        _watcher.UpdateTarget(volumeKnob.TargetVolume);
        if (!_isInitializing) SaveSettings();
    }

    private void BtnToggle_Click(object sender, RoutedEventArgs e)
    {
        _monitoring = !_monitoring;
        if (_monitoring)
        {
            _checkTimer.Start();
            _watcher.StartWatching(SelectedDeviceId(), volumeKnob.TargetVolume);
        }
        else
        {
            _checkTimer.Stop();
            _watcher.StopWatching();
            _recentChangeTimes.Clear();
        }
        SetToggleStyle(_monitoring);
        lblStatus.Text = _monitoring ? "▶  Monitoring active" : "■  Monitoring stopped";
        if (!_isInitializing) SaveSettings();
    }

    private void CbMinimizeToTray_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isInitializing) SaveSettings();
    }

    private void CbAutostart_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            if (cbAutostart.IsChecked == true)
            {
                string exePath = Environment.ProcessPath ?? "";
                using var key = Registry.CurrentUser.OpenSubKey(RegRun, true)
                                ?? Registry.CurrentUser.CreateSubKey(RegRun);
                key.SetValue(RegName, $"\"{exePath}\" --tray");
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegRun, true);
                key?.DeleteValue(RegName, false);
            }
        }
        catch { }
        if (!_isInitializing) SaveSettings();
    }

    // ── Timer Tick (core monitoring loop) ───────────────────────────────

    private void Timer_Tick(object? sender, EventArgs e)
    {
        string id = SelectedDeviceId();
        int current = AudioManager.GetVolume(id);
        int target = volumeKnob.TargetVolume;

        UpdateCurrentVolDisplay();

        if (current < 0)
        {
            lblStatus.Text = "⚠  Could not read device volume";
            return;
        }
        if (current != target)
        {
            bool ok = AudioManager.SetVolume(id, target);
            lblStatus.Text = ok
                ? $"✔  Corrected {current} % → {target} %"
                : "✖  Failed to set volume";
        }
        else
        {
            lblStatus.Text = $"✔  Volume OK  ({current} %)";
        }
    }

    // ── Volume Change Log & Warning ────────────────────────────────────

    private void OnExternalVolumeChange(object? sender, ExternalVolumeChangedArgs args)
    {
        if (!_monitoring) return;

        var suspects = AudioManager.GetActiveCaptureSessionProcessNames(SelectedDeviceId());
        string suspectsStr = suspects.Count > 0 ? string.Join(", ", suspects) : "unknown";

        var entry = new VolumeChangeEntry(args.Timestamp, args.OldVolume, args.NewVolume, suspectsStr);

        _changeLog.Insert(0, entry);
        if (_changeLog.Count > 200) _changeLog.RemoveAt(200);

        UpdateLogPanel();
        AppendToLogFile(entry);

        // Warning threshold: count changes within the rolling window
        var now = DateTime.Now;
        while (_recentChangeTimes.Count > 0 &&
               (now - _recentChangeTimes.Peek()).TotalSeconds > _warnWindowSeconds)
            _recentChangeTimes.Dequeue();
        _recentChangeTimes.Enqueue(now);

        if (_recentChangeTimes.Count >= _warnThresholdCount)
            TriggerWarning(suspectsStr);
    }

    private void TriggerWarning(string suspects)
    {
        string msg = $"Mic volume changed {_recentChangeTimes.Count}\u00d7 in {_warnWindowSeconds}s.  Suspects: {suspects}";
        TrayIcon.ShowNotification("MicVolumeFixer \u2013 Warning", msg,
            H.NotifyIcon.Core.NotificationIcon.Warning);

        warningBanner.Visibility = Visibility.Visible;
        lblWarning.Text = "\u26a0\u2002" + msg;

        _warningResetTimer.Stop();
        _warningResetTimer.Start();
    }

    private void UpdateLogPanel()
    {
        lstLog.ItemsSource = null;
        lstLog.ItemsSource = _changeLog;
        logHeaderText.Text = $"CHANGE LOG  ({_changeLog.Count})";
    }

    private static void AppendToLogFile(VolumeChangeEntry entry)
    {
        try
        {
            string line = $"[{entry.Time:yyyy-MM-dd HH:mm:ss}] {entry.OldVolume}%\u2192{entry.NewVolume}% | Suspects: {entry.Suspects}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch { }
    }

    private void LogHeader_Click(object sender, MouseButtonEventArgs e)
    {
        _logExpanded = !_logExpanded;
        logPanel.Visibility = _logExpanded ? Visibility.Visible : Visibility.Collapsed;
        logToggleArrow.Text = _logExpanded ? "\u25bc" : "\u25ba";
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        _changeLog.Clear();
        UpdateLogPanel();
    }

    private void TxtWarnCount_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (int.TryParse(txtWarnCount.Text, out int val) && val > 0)
        {
            _warnThresholdCount = val;
            SaveSettings();
        }
    }

    private void TxtWarnSeconds_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (int.TryParse(txtWarnSeconds.Text, out int val) && val > 0)
        {
            _warnWindowSeconds = val;
            SaveSettings();
        }
    }

    // ── Toggle Button Styling ───────────────────────────────────────────

    private void SetToggleStyle(bool active)
    {
        if (active)
        {
            btnToggle.Content = "■  Stop Monitoring";
            btnToggle.Background = new SolidColorBrush(Color.FromRgb(26, 61, 38));
            btnToggle.Foreground = new SolidColorBrush(Color.FromRgb(72, 199, 116));
        }
        else
        {
            btnToggle.Content = "▶  Start Monitoring";
            btnToggle.Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));
            btnToggle.Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 248));
        }
    }
}

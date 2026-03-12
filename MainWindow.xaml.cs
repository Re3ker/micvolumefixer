using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MicVolumeFixer;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _checkTimer;
    private readonly List<string> _deviceIds = [];
    private bool _isInitializing = true;
    private bool _reallyClose;
    private bool _startMinimized;
    private bool _monitoring;

    private static readonly string SettingsPath = Path.Combine(
        AppContext.BaseDirectory, "settings.json");

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
        if (vol < 0)
        {
            lblCurrentVol.Text = "– %";
            pbCurrentVol.Width = 0;
        }
        else
        {
            lblCurrentVol.Text = $"{vol} %";
            // The progress bar parent is the clipping Border
            var parent = pbCurrentVol.Parent as FrameworkElement;
            double maxWidth = parent?.ActualWidth ?? 304;
            pbCurrentVol.Width = vol / 100.0 * maxWidth;
        }
    }

    // ── Settings (JSON file) ────────────────────────────────────────────

    private sealed class AppSettings
    {
        public int TargetVolume { get; set; } = 90;
        public bool MonitoringActive { get; set; }
        public bool MinimizeToTray { get; set; }
        public string SelectedDeviceId { get; set; } = "";
    }

    private void SaveSettings()
    {
        try
        {
            var settings = new AppSettings
            {
                TargetVolume = (int)sliderTarget.Value,
                MonitoringActive = _monitoring,
                MinimizeToTray = cbMinimizeToTray.IsChecked == true,
                SelectedDeviceId = cboDevices.SelectedIndex >= 0 && cboDevices.SelectedIndex < _deviceIds.Count
                    ? _deviceIds[cboDevices.SelectedIndex]
                    : ""
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
            sliderTarget.Value = v;
            lblTargetVal.Text = $"{v} %";

            // Monitoring active
            if (settings.MonitoringActive)
            {
                _checkTimer.Start();
                _monitoring = true;
                SetToggleStyle(true);
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
        }
        catch { }
    }

    // ── UI Event Handlers ───────────────────────────────────────────────

    private void CboDevices_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateCurrentVolDisplay();
        if (!_isInitializing) SaveSettings();
    }

    private void SliderTarget_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (lblTargetVal == null) return; // designer/load guard
        lblTargetVal.Text = $"{(int)sliderTarget.Value} %";
        if (!_isInitializing) SaveSettings();
    }

    private void BtnToggle_Click(object sender, RoutedEventArgs e)
    {
        _monitoring = !_monitoring;
        if (_monitoring)
            _checkTimer.Start();
        else
            _checkTimer.Stop();
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
        int target = (int)sliderTarget.Value;

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

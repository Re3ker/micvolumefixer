using System;
using System.Windows;
using NAudio.CoreAudioApi;

namespace MicVolumeFixer;

public sealed class ExternalVolumeChangedArgs : EventArgs
{
    public int OldVolume { get; init; }
    public int NewVolume { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Holds a long-lived subscription to a capture device's endpoint volume
/// and fires <see cref="ExternalChangeDetected"/> when something other than
/// our own correction changes the volume.
/// </summary>
public sealed class VolumeWatcher : IDisposable
{
    private MMDevice? _device;
    private volatile int _lastKnownVolume;
    private volatile int _targetVolume;
    private bool _disposed;

    public event EventHandler<ExternalVolumeChangedArgs>? ExternalChangeDetected;

    /// <summary>
    /// Starts watching the given device. Replaces any previous subscription.
    /// </summary>
    public void StartWatching(string deviceId, int targetVolume)
    {
        StopWatching();
        if (string.IsNullOrEmpty(deviceId)) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _device = enumerator.GetDevice(deviceId);
            _targetVolume = targetVolume;
            _lastKnownVolume = (int)(_device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f + 0.5f);
            _device.AudioEndpointVolume.OnVolumeNotification += OnNotification;
        }
        catch
        {
            _device?.Dispose();
            _device = null;
        }
    }

    /// <summary>
    /// Updates the target volume so the watcher can distinguish our
    /// corrections from external changes.
    /// </summary>
    public void UpdateTarget(int targetVolume)
    {
        _targetVolume = targetVolume;
    }

    /// <summary>
    /// Stops watching and disposes the held device.
    /// </summary>
    public void StopWatching()
    {
        if (_device == null) return;
        try { _device.AudioEndpointVolume.OnVolumeNotification -= OnNotification; }
        catch { }
        _device.Dispose();
        _device = null;
    }

    private void OnNotification(AudioVolumeNotificationData data)
    {
        int newVol = (int)(data.MasterVolume * 100f + 0.5f);
        int oldVol = _lastKnownVolume;
        _lastKnownVolume = newVol;

        // If the new volume matches our target, it is either our own
        // correction or an external app that happened to set the exact
        // target value — either way, nothing to report.
        if (newVol == _targetVolume) return;

        var args = new ExternalVolumeChangedArgs
        {
            OldVolume = oldVol,
            NewVolume = newVol,
            Timestamp = DateTime.Now
        };

        // Marshal to the UI thread; Application.Current may be null during shutdown.
        Application.Current?.Dispatcher.InvokeAsync(() =>
            ExternalChangeDetected?.Invoke(this, args));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatching();
    }
}

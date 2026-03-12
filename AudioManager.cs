using NAudio.CoreAudioApi;

namespace MicVolumeFixer;

public record MicDevice(string Id, string Name);

public static class AudioManager
{
    public static List<MicDevice> GetCaptureDevices()
    {
        var devices = new List<MicDevice>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in collection)
            {
                devices.Add(new MicDevice(device.ID, device.FriendlyName));
                device.Dispose();
            }
        }
        catch { }
        return devices;
    }

    public static string GetDefaultCaptureDeviceId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            return device.ID;
        }
        catch { return ""; }
    }

    public static int GetVolume(string deviceId)
    {
        try
        {
            using var device = GetDeviceById(deviceId);
            if (device == null) return -1;
            float scalar = device.AudioEndpointVolume.MasterVolumeLevelScalar;
            return (int)(scalar * 100f + 0.5f);
        }
        catch { return -1; }
    }

    public static bool SetVolume(string deviceId, int volume)
    {
        try
        {
            using var device = GetDeviceById(deviceId);
            if (device == null) return false;
            float scalar = Math.Clamp(volume / 100f, 0f, 1f);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = scalar;
            return true;
        }
        catch { return false; }
    }

    private static MMDevice? GetDeviceById(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDevice(deviceId);
        }
        catch { return null; }
    }
}

using System.Runtime.InteropServices;

namespace Sidebar;

internal sealed class AudioEndpoint : IDisposable
{
    private IAudioEndpointVolume? endpoint;
    private IAudioEndpointVolume Endpoint => endpoint ?? throw new ObjectDisposedException(nameof(AudioEndpoint));
    public AudioEndpoint()
    {
        object enumerator = new MMDeviceEnumerator(); IMMDevice? device = null;
        try
        {
            Marshal.ThrowExceptionForHR(((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out device));
            Guid volumeId = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref volumeId, 23, IntPtr.Zero, out object volume)); endpoint = (IAudioEndpointVolume)volume;
        }
        finally { if (device is not null) Marshal.ReleaseComObject(device); Marshal.ReleaseComObject(enumerator); }
    }
    public double Volume
    {
        get { Marshal.ThrowExceptionForHR(Endpoint.GetMasterVolumeLevelScalar(out float volume)); return volume * 100; }
        set { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(Endpoint.SetMasterVolumeLevelScalar((float)Math.Clamp(value / 100, 0, 1), ref context)); }
    }
    public bool Muted
    {
        get { Marshal.ThrowExceptionForHR(Endpoint.GetMute(out bool muted)); return muted; }
        set { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(Endpoint.SetMute(value, ref context)); }
    }
    public void Dispose() { if (endpoint is { } value) { endpoint = null; Marshal.ReleaseComObject(value); } }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDevice
    { [PreserveSig] int Activate(ref Guid id, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value); }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint channels);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Atoll.Platform.Windows.Interop;

/// <summary>
/// Watches the master volume / mute of the default audio output through WASAPI
/// <c>IAudioEndpointVolume</c> change notifications — the Windows counterpart of
/// the original's system volume observer that drives the volume HUD. Follows the
/// default device when the user switches outputs (speakers ↔ headphones).
/// </summary>
public sealed class VolumeMonitor : IDisposable
{
    private static readonly Lazy<VolumeMonitor> _shared = new(() => new VolumeMonitor());
    public static VolumeMonitor Shared => _shared.Value;

    /// <summary>Raised on a COM worker thread when volume or mute changes:
    /// (scalar 0..1, muted). Not raised when the default device switches.</summary>
    public event Action<float, bool>? Changed;

    public float Volume { get; private set; }

    /// <summary>Diagnostic: why the last attach failed, or null when attached.</summary>
    public string? AttachError { get; private set; } = "not attached";
    public bool IsMuted { get; private set; }

    private readonly object _gate = new();
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpoint;
    private readonly VolumeCallback _callback;
    private readonly DeviceClient _deviceClient;
    private bool _disposed;

    private VolumeMonitor()
    {
        _callback = new VolumeCallback(this);
        _deviceClient = new DeviceClient(this);
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(_deviceClient);
        }
        catch
        {
            _enumerator = null;
        }
        Attach();
    }

    /// <summary>Sets the master volume (0..1) of the default output.</summary>
    public void SetVolume(float scalar)
    {
        var ctx = Guid.Empty;
        try { _endpoint?.SetMasterVolumeLevelScalar(Math.Clamp(scalar, 0f, 1f), ref ctx); } catch { }
    }

    private void Attach()
    {
        lock (_gate)
        {
            if (_disposed || _enumerator is null) return;
            Detach();
            try
            {
                if (_enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 0 /* eConsole */, out var device) != 0
                    || device is null)
                    return;

                var iid = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var obj) != 0)
                    return;

                _endpoint = (IAudioEndpointVolume)obj;
                var hr = _endpoint.RegisterControlChangeNotify(_callback);
                AttachError = hr == 0 ? null : $"RegisterControlChangeNotify hr=0x{hr:X8}";
                if (_endpoint.GetMasterVolumeLevelScalar(out var level) == 0) Volume = level;
                if (_endpoint.GetMute(out var muted) == 0) IsMuted = muted;
            }
            catch (Exception ex)
            {
                AttachError = ex.GetType().Name + ": " + ex.Message;
                _endpoint = null;
            }
        }
    }

    private void Detach()
    {
        if (_endpoint is null) return;
        try { _endpoint.UnregisterControlChangeNotify(_callback); } catch { }
        try { Marshal.ReleaseComObject(_endpoint); } catch { }
        _endpoint = null;
    }

    private void OnNotify(float level, bool muted)
    {
        // Ignore no-op notifications (e.g. a balance change on one channel).
        if (Math.Abs(level - Volume) < 0.0005f && muted == IsMuted) return;
        Volume = level;
        IsMuted = muted;
        Changed?.Invoke(level, muted);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Detach();
            if (_enumerator is not null)
            {
                try { _enumerator.UnregisterEndpointNotificationCallback(_deviceClient); } catch { }
                try { Marshal.ReleaseComObject(_enumerator); } catch { }
                _enumerator = null;
            }
        }
    }

    // ------------------------------------------------------------------
    // COM callbacks
    // ------------------------------------------------------------------

    [ComVisible(true)]
    public sealed class VolumeCallback : IAudioEndpointVolumeCallback
    {
        private readonly VolumeMonitor _owner;
        internal VolumeCallback(VolumeMonitor owner) => _owner = owner;

        public int OnNotify(IntPtr pNotify)
        {
            // AUDIO_VOLUME_NOTIFICATION_DATA: GUID context (16), BOOL bMuted (4),
            // float fMasterVolume (4), UINT nChannels, float afChannelVolumes[].
            try
            {
                var muted = Marshal.ReadInt32(pNotify, 16) != 0;
                var level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(pNotify, 20));
                _owner.OnNotify(level, muted);
            }
            catch { }
            return 0;
        }
    }

    [ComVisible(true)]
    public sealed class DeviceClient : IMMNotificationClient
    {
        private readonly VolumeMonitor _owner;
        internal DeviceClient(VolumeMonitor owner) => _owner = owner;

        public int OnDefaultDeviceChanged(int flow, int role, string? defaultDeviceId)
        {
            // Re-attach off the notification thread (no blocking calls allowed here).
            if (flow == 0 /* eRender */ && role == 0 /* eConsole */)
                ThreadPool.QueueUserWorkItem(_ => _owner.Attach());
            return 0;
        }

        public int OnDeviceStateChanged(string? deviceId, int newState) => 0;
        public int OnDeviceAdded(string? deviceId) => 0;
        public int OnDeviceRemoved(string? deviceId) => 0;
        public int OnPropertyValueChanged(string? deviceId, PropertyKey key) => 0;
    }

    // ------------------------------------------------------------------
    // Minimal Core Audio COM interop
    // ------------------------------------------------------------------

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComVisible(true), Guid("657804FA-D6AD-4496-8A60-352752AF4F89"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr pNotify);
    }

    [ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string? deviceId, int newState);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string? deviceId);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string? deviceId);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string? deviceId, PropertyKey key);
    }
}

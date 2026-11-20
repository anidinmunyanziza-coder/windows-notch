using System;
using System.Runtime.InteropServices;

namespace Atoll.Platform.Windows.Interop;

/// <summary>
/// Reads the current output peak level of the default audio render endpoint via
/// WASAPI <c>IAudioMeterInformation</c>. This is a lightweight real-audio signal
/// (0..1) used to drive the notch's music equalizer — not an FFT spectrum.
/// </summary>
public sealed class AudioMeter : IDisposable
{
    private static readonly Lazy<AudioMeter> _shared = new(() => new AudioMeter());
    public static AudioMeter Shared => _shared.Value;

    private IAudioMeterInformation? _meter;

    public AudioMeter() => TryAcquire();

    private void TryAcquire()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            // eRender = 0, eConsole = 0.
            if (enumerator.GetDefaultAudioEndpoint(0, 0, out var device) != 0 || device is null)
                return;

            var iid = typeof(IAudioMeterInformation).GUID;
            if (device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var obj) != 0)
                return;

            _meter = obj as IAudioMeterInformation;
        }
        catch
        {
            _meter = null;
        }
    }

    /// <summary>Current output peak in [0,1]; 0 when silent or unavailable.</summary>
    public float Peak
    {
        get
        {
            if (_meter is null)
            {
                TryAcquire();
                if (_meter is null) return 0f;
            }

            try
            {
                return _meter.GetPeakValue(out var peak) == 0 ? peak : 0f;
            }
            catch
            {
                // Endpoint changed/invalidated; drop it and re-acquire next time.
                _meter = null;
                return 0f;
            }
        }
    }

    public void Dispose()
    {
        if (_meter is not null)
        {
            try { Marshal.ReleaseComObject(_meter); } catch { }
            _meter = null;
        }
    }

    // ------------------------------------------------------------------
    // Minimal Core Audio COM interop (only the vtable slots we call).
    // ------------------------------------------------------------------

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float pfPeak);
    }
}

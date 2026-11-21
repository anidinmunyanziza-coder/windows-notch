using System;
using System.Threading;
using Microsoft.Win32;

namespace Atoll.Platform.Windows.Interop;

/// <summary>
/// Detects camera / microphone use — the Windows counterpart of the original's
/// <c>PrivacyIndicatorManager</c>. Windows records every app's capability use
/// under CapabilityAccessManager\ConsentStore\{webcam|microphone}; an app is
/// using the device right now while its <c>LastUsedTimeStop</c> is 0. This is
/// the same data that drives the Windows taskbar's own "in use" indicators.
/// </summary>
public sealed class PrivacyMonitor : IDisposable
{
    private static readonly Lazy<PrivacyMonitor> _shared = new(() => new PrivacyMonitor());
    public static PrivacyMonitor Shared => _shared.Value;

    private const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    /// <summary>Raised on a thread-pool thread when either state flips: (camera, microphone).</summary>
    public event Action<bool, bool>? Changed;

    public bool CameraInUse { get; private set; }
    public bool MicrophoneInUse { get; private set; }

    private readonly Timer _timer;

    private PrivacyMonitor()
    {
        Poll();
        _timer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void Poll()
    {
        var cam = InUse("webcam");
        var mic = InUse("microphone");
        if (cam == CameraInUse && mic == MicrophoneInUse) return;
        CameraInUse = cam;
        MicrophoneInUse = mic;
        Changed?.Invoke(cam, mic);
    }

    private static bool InUse(string capability)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey($@"{Store}\{capability}");
            if (root is null) return false;
            if (AnyActive(root, skip: "NonPackaged")) return true;
            using var nonPackaged = root.OpenSubKey("NonPackaged");
            return nonPackaged is not null && AnyActive(nonPackaged, skip: null);
        }
        catch
        {
            return false;
        }
    }

    private static bool AnyActive(RegistryKey parent, string? skip)
    {
        foreach (var name in parent.GetSubKeyNames())
        {
            if (name == skip) continue;
            using var app = parent.OpenSubKey(name);
            if (app?.GetValue("LastUsedTimeStop") is long stop && stop == 0
                && app.GetValue("LastUsedTimeStart") is long start && start != 0)
                return true;
        }
        return false;
    }

    public void Dispose() => _timer.Dispose();
}

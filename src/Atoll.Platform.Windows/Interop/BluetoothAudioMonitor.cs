using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;

namespace Atoll.Platform.Windows.Interop;

/// <summary>
/// Watches paired Bluetooth audio devices (headphones, earbuds, speakers) and
/// reports when one connects — the Windows counterpart of the original's
/// <c>BluetoothAudioManager</c> connection HUD. Battery comes from the PnP
/// property DEVPKEY_Bluetooth_Battery on the device's container, which Windows
/// fills in shortly after connecting for devices that report it (HFP / BLE).
/// </summary>
public sealed class BluetoothAudioMonitor : IDisposable
{
    private static readonly Lazy<BluetoothAudioMonitor> _shared = new(() => new BluetoothAudioMonitor());
    public static BluetoothAudioMonitor Shared => _shared.Value;

    private const string ClassicProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    private const string CodMajorKey = "System.Devices.Aep.Bluetooth.Cod.Major";
    private const string ContainerKey = "System.Devices.Aep.ContainerId";
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const int CodMajorAudioVideo = 4;

    /// <summary>Raised (thread-pool) the moment an audio device connects,
    /// before the battery wait — used to suppress the volume HUD.</summary>
    public event Action? Connecting;

    /// <summary>Raised (thread-pool) once the HUD should show: (name, battery 0..100 or null).</summary>
    public event Action<string, int?>? Connected;

    private sealed class Entry
    {
        public string Name = "";
        public bool Connected;
        public bool IsAudio;
        public Guid? Container;
    }

    private readonly ConcurrentDictionary<string, Entry> _devices = new();
    private readonly DeviceWatcher? _watcher;
    private volatile bool _enumerated;

    private BluetoothAudioMonitor()
    {
        try
        {
            var aqs = $"System.Devices.Aep.ProtocolId:=\"{ClassicProtocol}\" AND "
                      + "System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";
            _watcher = DeviceInformation.CreateWatcher(aqs,
                new[] { IsConnectedKey, CodMajorKey, ContainerKey },
                DeviceInformationKind.AssociationEndpoint);
            _watcher.Added += (_, d) => _devices[d.Id] = Read(d.Name, d.Properties, new Entry());
            _watcher.Updated += (_, u) => OnUpdated(u);
            _watcher.Removed += (_, u) => _devices.TryRemove(u.Id, out Entry? _);
            _watcher.EnumerationCompleted += (_, _) => _enumerated = true;
            _watcher.Start();
        }
        catch
        {
            _watcher = null; // Bluetooth stack unavailable
        }
    }

    private static Entry Read(string? name, System.Collections.Generic.IReadOnlyDictionary<string, object> props, Entry e)
    {
        if (!string.IsNullOrEmpty(name)) e.Name = name;
        if (props.TryGetValue(IsConnectedKey, out var c) && c is bool connected) e.Connected = connected;
        if (props.TryGetValue(CodMajorKey, out var m) && m is not null)
        {
            try { e.IsAudio = Convert.ToInt32(m) == CodMajorAudioVideo; } catch { }
        }
        if (props.TryGetValue(ContainerKey, out var g) && g is Guid container) e.Container = container;
        return e;
    }

    private void OnUpdated(DeviceInformationUpdate update)
    {
        if (!_devices.TryGetValue(update.Id, out var entry)) return;
        var wasConnected = entry.Connected;
        Read(null, update.Properties, entry);

        if (_enumerated && !wasConnected && entry.Connected && entry.IsAudio)
        {
            Connecting?.Invoke();
            _ = AnnounceAsync(entry);
        }
    }

    private async Task AnnounceAsync(Entry entry)
    {
        int? battery = null;
        // Windows publishes the battery a moment after the link comes up.
        for (var i = 0; i < 5 && battery is null; i++)
        {
            battery = await ReadBatteryAsync(entry.Container);
            if (battery is null) await Task.Delay(500);
        }
        Connected?.Invoke(entry.Name, battery);
    }

    private static async Task<int?> ReadBatteryAsync(Guid? container)
    {
        if (container is null) return null;
        try
        {
            var nodes = await DeviceInformation.FindAllAsync(
                $"System.Devices.ContainerId:=\"{{{container.Value}}}\"",
                new[] { BatteryKey }, DeviceInformationKind.Device);
            foreach (var value in nodes.Select(n => n.Properties.TryGetValue(BatteryKey, out var v) ? v : null))
            {
                if (value is null) continue;
                var pct = Convert.ToInt32(value);
                if (pct is > 0 and <= 100) return pct;
            }
        }
        catch { }
        return null;
    }

    public void Dispose()
    {
        try { _watcher?.Stop(); } catch { }
    }
}

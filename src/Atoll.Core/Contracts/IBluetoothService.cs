using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Atoll.Core.Contracts;

/// <summary>
/// Bluetooth devices. Translated from <c>BluetoothAudioManager</c>.
/// Windows: Windows.Devices.Bluetooth (LE + GATT battery).
/// </summary>
public interface IBluetoothService : IDisposable
{
    event EventHandler<IReadOnlyList<BluetoothDeviceInfo>>? DevicesChanged;
    IReadOnlyList<BluetoothDeviceInfo> ConnectedDevices { get; }
}

public sealed class BluetoothDeviceInfo
{
    public string Id { get; init; } = string.Empty;
    public string? Name { get; init; }
    public bool IsConnected { get; init; }
    public double? BatteryPercent { get; init; }
    public string? DeviceClass { get; init; }
}

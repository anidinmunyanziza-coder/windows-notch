using System;
using System.Threading.Tasks;

namespace Atoll.Core.Contracts;

/// <summary>
/// Battery / power state. Translated from <c>MacBatteryManager</c>,
/// <c>BatteryActivityManager</c>, <c>BatteryPowerReading</c>.
/// </summary>
public interface IPowerService : IDisposable
{
    event EventHandler<PowerState>? Changed;
    PowerState Current { get; }
}

public sealed class PowerState
{
    public double? ChargePercent { get; init; }
    public bool IsCharging { get; init; }
    public bool IsPresent { get; init; }
    public TimeSpan? EstimatedTimeRemaining { get; init; }
}

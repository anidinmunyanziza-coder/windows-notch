using System.Collections.Generic;

namespace Atoll.Core.Models;

/// <summary>
/// One sample of system metrics, translated from <c>StatsManager</c>
/// (<c>MemoryBreakdown</c>, <c>CPUCoreUsage</c>, <c>GPUDeviceMetrics</c>).
/// Values are real measurements; absent values are represented as null
/// rather than fabricated.
/// </summary>
public sealed class SystemMetricsSnapshot
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    // CPU
    public double? CpuTotalPercent { get; set; }
    public IReadOnlyList<double>? CpuPerCorePercent { get; set; }
    public double? CpuTemperatureCelsius { get; set; }
    public long? CpuCurrentFrequencyMhz { get; set; }

    // GPU (one entry per active adapter)
    public IReadOnlyList<GpuDeviceMetrics>? GpuDevices { get; set; }

    // Memory
    public ulong? MemoryTotalBytes { get; set; }
    public ulong? MemoryUsedBytes { get; set; }
    public ulong? MemoryAvailableBytes { get; set; }
    public double? MemoryUsedPercent { get; set; }
    public string? MemoryPressureLevel { get; set; } // Normal / Warning / Critical / Unknown

    // Network
    public long? NetworkBytesSentPerSec { get; set; }
    public long? NetworkBytesReceivedPerSec { get; set; }

    // Disk
    public long? DiskBytesReadPerSec { get; set; }
    public long? DiskBytesWrittenPerSec { get; set; }

    public bool HasAnyData =>
        CpuTotalPercent.HasValue || CpuPerCorePercent?.Count > 0 ||
        GpuDevices?.Count > 0 || MemoryUsedBytes.HasValue ||
        NetworkBytesSentPerSec.HasValue || DiskBytesReadPerSec.HasValue;
}

public sealed class GpuDeviceMetrics
{
    public string Id { get; set; } = string.Empty;
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public bool IsActive { get; set; }
    public double? UtilizationPercent { get; set; }
    public double? TemperatureCelsius { get; set; }
    public int? FanSpeedPercent { get; set; }
    public long? CoreClockMhz { get; set; }
    public long? MemoryClockMhz { get; set; }
    public int? CoreCount { get; set; }
}

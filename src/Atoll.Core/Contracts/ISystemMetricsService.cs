using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atoll.Core.Models;

namespace Atoll.Core.Contracts;

/// <summary>
/// System metrics polling. Translated from <c>StatsManager</c> and its
/// hardware readers (CPU, GPU, memory, network, disk).
/// Windows implementation uses PDH / WMI / GlobalMemoryStatusEx.
/// </summary>
public interface ISystemMetricsService : IDisposable
{
    event EventHandler<SystemMetricsSnapshot>? MetricsCollected;
    SystemMetricsSnapshot? LatestSnapshot { get; }
    StatsCategories EnabledCategories { get; set; }
    TimeSpan PollInterval { get; set; }
    bool IsMonitoring { get; }

    Task StartMonitoringAsync(CancellationToken ct = default);
    Task StopMonitoringAsync(CancellationToken ct = default);
    Task<SystemMetricsSnapshot> CollectOnceAsync(CancellationToken ct = default);
}

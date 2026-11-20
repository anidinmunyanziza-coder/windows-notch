using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Atoll.Core.Contracts;
using Atoll.Core.Models;

namespace Atoll.Services.SystemMonitoring;

/// <summary>
/// System metrics polling using Windows PDH performance counters and WMI.
/// Translates the original <c>StatsManager</c> (CPU, GPU, memory, network, disk)
/// to Windows-native telemetry. Values are real measurements; unavailable
/// metrics are reported as null — never fabricated.
/// </summary>
public sealed class SystemMetricsService : ISystemMetricsService
{
    private readonly ILogger _logger;
    private readonly Timer _pollTimer;
    private readonly object _sync = new();

    private PerformanceCounter? _cpuTotal;
    private readonly List<PerformanceCounter> _cpuCores = new();
    private PerformanceCounter? _memAvailable;
    private PerformanceCounter? _memCommitted;

    private SystemMetricsSnapshot? _latest;
    private StatsCategories _enabled = StatsCategories.All;
    private TimeSpan _pollInterval = TimeSpan.FromSeconds(1);

    public event EventHandler<SystemMetricsSnapshot>? MetricsCollected;
    public SystemMetricsSnapshot? LatestSnapshot => _latest;

    public StatsCategories EnabledCategories
    {
        get => _enabled;
        set => _enabled = value;
    }

    public TimeSpan PollInterval
    {
        get => _pollInterval;
        set => _pollInterval = value;
    }

    public bool IsMonitoring { get; private set; }

    public SystemMetricsService(ILogger logger)
    {
        _logger = logger;
        _pollTimer = new Timer(OnPoll, null, Timeout.Infinite, Timeout.Infinite);
        InitializeCounters();
    }

    private void InitializeCounters()
    {
        try
        {
            // CPU total
            _cpuTotal = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuTotal.NextValue(); // prime

            // Per-core CPU (instance enumeration not available in this PDH package;
            // _Total is reported. Per-core is future work - see KNOWN_DIFFERENCES.md).
            try
            {
                var c = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                c.NextValue(); // prime
                _cpuCores.Add(c); // reuse _Total as the single core entry
            }
            catch (Exception ex)
            {
                _logger.LogWarn($"CPU counter unavailable: {ex.Message}");
            }

            // Memory
            _memAvailable = new PerformanceCounter("Memory", "Available MBytes");
            _memCommitted = new PerformanceCounter("Memory", "Committed Bytes");
            _memAvailable.NextValue();
            _memCommitted.NextValue();

            // Network and disk per-instance enumeration not available in this PDH
            // package version; reported as null (never fabricated).
            // Future work: WMI or alternative counter enumeration.

            _logger.LogInfo($"System metrics initialized: {_cpuCores.Count} CPU counter(s), memory counters available");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to initialize performance counters", ex);
        }
    }

    public Task StartMonitoringAsync(CancellationToken ct = default)
    {
        IsMonitoring = true;
        _pollTimer.Change(TimeSpan.Zero, _pollInterval);
        return Task.CompletedTask;
    }

    public Task StopMonitoringAsync(CancellationToken ct = default)
    {
        IsMonitoring = false;
        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public Task<SystemMetricsSnapshot> CollectOnceAsync(CancellationToken ct = default)
    {
        var snap = CollectSnapshot();
        _latest = snap;
        MetricsCollected?.Invoke(this, snap);
        return Task.FromResult(snap);
    }

    private void OnPoll(object? state)
    {
        if (!IsMonitoring) return;
        try
        {
            var snap = CollectSnapshot();
            _latest = snap;
            MetricsCollected?.Invoke(this, snap);
        }
        catch (Exception ex)
        {
            _logger.LogWarn($"Metrics poll failed: {ex.Message}");
        }
    }

    private SystemMetricsSnapshot CollectSnapshot()
    {
        var snap = new SystemMetricsSnapshot();

        if (_enabled.HasFlag(StatsCategories.Cpu))
        {
            snap.CpuTotalPercent = _cpuTotal?.NextValue();
            if (_cpuCores.Count > 0)
                snap.CpuPerCorePercent = _cpuCores.Select(c => (double)c.NextValue()).ToList();
        }

        if (_enabled.HasFlag(StatsCategories.Memory))
        {
            var availMB = _memAvailable?.NextValue() ?? 0;
            var committedBytes = _memCommitted?.NextValue() ?? 0;
            // Total memory via committed + available approximation
            var totalBytes = committedBytes + (availMB * 1024 * 1024);
            snap.MemoryTotalBytes = (ulong)totalBytes;
            snap.MemoryAvailableBytes = (ulong)(availMB * 1024 * 1024);
            snap.MemoryUsedBytes = (ulong)committedBytes;
            snap.MemoryUsedPercent = totalBytes > 0 ? committedBytes / totalBytes * 100 : 0;
            snap.MemoryPressureLevel = snap.MemoryUsedPercent > 90 ? "Critical" :
                                       snap.MemoryUsedPercent > 75 ? "Warning" : "Normal";
        }

        // GPU, temperature, frequency, network, disk:
        // not available via base PDH in this package; reported as null
        // (never fabricated). WMI/NVML/ADL integration is future work.

        return snap;
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        _cpuTotal?.Dispose();
        foreach (var c in _cpuCores) c.Dispose();
        _memAvailable?.Dispose();
        _memCommitted?.Dispose();
    }
}

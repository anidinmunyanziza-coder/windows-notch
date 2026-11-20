using System.Threading.Tasks;
using Atoll.Core.Contracts;
using Atoll.Core.Models;
using Atoll.Services.SystemMonitoring;
using Xunit;

namespace Atoll.UnitTests;

public class SystemMetricsTests
{
    [Fact]
    public async Task SystemMetricsService_StartStop_DoesNotThrow()
    {
        var svc = new SystemMetricsTestsLogger();
        var service = new SystemMetricsService(svc);
        await service.StartMonitoringAsync();
        Assert.True(service.IsMonitoring);
        await service.StopMonitoringAsync();
        Assert.False(service.IsMonitoring);
        service.Dispose();
    }

    [Fact]
    public async Task SystemMetricsService_CollectOnce_ReturnsRealData()
    {
        var svc = new SystemMetricsTestsLogger();
        var service = new SystemMetricsService(svc);
        var snap = await service.CollectOnceAsync();
        Assert.NotNull(snap);
        // CPU total should be a real value (0-100) or null if counter unavailable
        if (snap.CpuTotalPercent.HasValue)
            Assert.InRange(snap.CpuTotalPercent.Value, 0, 100);
        // Memory used percent should be 0-100 or null
        if (snap.MemoryUsedPercent.HasValue)
            Assert.InRange(snap.MemoryUsedPercent.Value, 0, 100);
        // Never fabricate: GPU/temp/frequency should be null (not available via base PDH)
        Assert.Null(snap.GpuDevices);
        Assert.Null(snap.CpuTemperatureCelsius);
        Assert.Null(snap.CpuCurrentFrequencyMhz);
        service.Dispose();
    }

    private sealed class SystemMetricsTestsLogger : ILogger
    {
        public void Log(LogLevel level, string message, System.Exception? exception = null) { }
        public void LogError(string message, System.Exception? exception = null) { }
        public void LogWarn(string message, System.Exception? exception = null) { }
        public void LogInfo(string message, System.Exception? exception = null) { }
        public void LogDebug(string message, System.Exception? exception = null) { }
        public void Dispose() { }
    }
}

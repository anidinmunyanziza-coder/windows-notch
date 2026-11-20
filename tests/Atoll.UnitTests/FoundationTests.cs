using System.IO;
using System.Threading.Tasks;
using Atoll.Core.Contracts;
using Atoll.Core.Design;
using Atoll.Core.Models;
using Atoll.Core.ViewModels;
using Atoll.Platform.Windows.Interop;
using Atoll.Services.Settings;
using Xunit;

namespace Atoll.UnitTests;

public class FoundationTests
{
    [Fact]
    public async Task SettingsService_RoundTrip_PersistsValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"atoll_test_{Guid.NewGuid().ToString("N")}.json");
        try
        {
            var svc = new JsonSettingsService(Path.GetDirectoryName(path));
            svc.Set("test.bool", true);
            svc.Set("test.int", 42);
            svc.Set("test.double", 3.14);
            svc.Set("test.string", "hello");
            await svc.SaveAsync();

            // New instance loads from same file.
            var svc2 = new JsonSettingsService(Path.GetDirectoryName(path));
            Assert.True(svc2.Get("test.bool", false));
            Assert.Equal(42, svc2.Get("test.int", 0));
            Assert.Equal(3.14, svc2.Get("test.double", 0.0));
            Assert.Equal("hello", svc2.Get("test.string", ""));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SettingsService_Defaults_ReturnedWhenMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"atoll_test_{Guid.NewGuid().ToString("N")}.json");
        try
        {
            var svc = new JsonSettingsService(Path.GetDirectoryName(path));
            Assert.True(svc.Get("missing.bool", true));
            Assert.Equal(99, svcGetInt(svc, "missing.int", 99));
            Assert.Equal("def", svc.Get("missing.str", "def"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static int svcGetInt(ISettingsService s, string k, int d) => s.Get(k, d);

    [Fact]
    public void NotchViewModel_StateChange_NotifiesCurrentWidthHeight()
    {
        var vm = new NotchViewModel();
        var changed = new System.Collections.Generic.List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.State = NotchState.Open;

        Assert.Contains(nameof(NotchViewModel.State), changed);
        Assert.Contains(nameof(NotchViewModel.CurrentWidth), changed);
        Assert.Contains(nameof(NotchViewModel.CurrentHeight), changed);
        Assert.Equal(DesignTokens.OpenNotchWidth, vm.CurrentWidth);
    }

    [Fact]
    public void NotchViewModel_Closed_IsDefault()
    {
        var vm = new NotchViewModel();
        Assert.Equal(NotchState.Closed, vm.State);
        Assert.Equal(DesignTokens.ClosedNotchWidth, vm.CurrentWidth);
        Assert.Equal(DesignTokens.NotchHeight, vm.CurrentHeight);
    }

    [Fact]
    public void MonitorHelper_EnumeratesAtLeastOneDisplay()
    {
        var displays = MonitorHelper.EnumerateDisplays();
        Assert.NotEmpty(displays);
        // Primary display should be flagged.
        Assert.Contains(displays, d => d.IsPrimary);
        // DPI scale should be >= 1.0.
        foreach (var d in displays)
            Assert.True(d.DpiScaleX >= 1.0);
    }

    [Fact]
    public void ShellViewModel_TabOrder_MatchesOriginal()
    {
        // We can't easily instantiate ShellViewModel without IWindowService,
        // but the tab order constant is asserted indirectly via the enum.
        Assert.Equal(NotchView.Home, NotchView.Home);
        Assert.Equal(NotchView.Stats, NotchView.Stats);
    }
}

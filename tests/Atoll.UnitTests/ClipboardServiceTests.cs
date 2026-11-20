using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Atoll.Core.Contracts;
using Atoll.Services.Productivity.Clipboard;
using Xunit;

namespace Atoll.UnitTests;

public class ClipboardServiceTests : IDisposable
{
    private readonly string _tempFolder;

    public ClipboardServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"atoll_clipboard_test_{Guid.NewGuid().ToString("N")}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempFolder, true); } catch { }
    }

    private ClipboardService CreateService() => new ClipboardService(_tempFolder);

    [Fact]
    public async Task ClipboardService_AddCurrent_DoesNotCrash()
    {
        var svc = CreateService();
        await svc.AddCurrentAsync();
        await svc.StopMonitoringAsync(CancellationToken.None);
        svc.Dispose();
    }

    [Fact]
    public async Task ClipboardService_SetAndRemoveItem()
    {
        var svc = CreateService();
        var item = new ClipboardItem { Text = "test clipboard item" };
        await svc.SetItemAsync(item);

        var history = svc.History;
        Assert.Single(history);
        Assert.Equal("test clipboard item", history[0].Text);

        await svc.RemoveItemAsync(item.Id);
        Assert.Empty(svc.History);

        svc.Dispose();
    }

    [Fact]
    public async Task ClipboardService_ClearHistory()
    {
        var svc = CreateService();
        await svc.SetItemAsync(new ClipboardItem { Text = "item 1" });
        await svc.SetItemAsync(new ClipboardItem { Text = "item 2" });
        Assert.Equal(2, svc.History.Count);

        await svc.ClearHistoryAsync(CancellationToken.None);
        Assert.Empty(svc.History);

        svc.Dispose();
    }

    [Fact]
    public void ClipboardService_MaxHistorySize_DefaultIsValid()
    {
        var svc = CreateService();
        Assert.True(svc.MaxHistorySize > 0);
        svc.Dispose();
    }

    [Fact]
    public async Task ClipboardService_MaxHistorySize_TrimsOldestItems()
    {
        var svc = CreateService();
        svc.MaxHistorySize = 2;

        await svc.SetItemAsync(new ClipboardItem { Text = "one" });
        await svc.SetItemAsync(new ClipboardItem { Text = "two" });
        await svc.SetItemAsync(new ClipboardItem { Text = "three" });

        Assert.Equal(2, svc.History.Count);
        // History is newest-first; the oldest item is trimmed.
        Assert.Equal("three", svc.History[0].Text);
        Assert.Equal("two", svc.History[1].Text);

        svc.Dispose();
    }

    [Fact]
    public async Task ClipboardService_PersistsHistoryAcrossInstances()
    {
        var first = CreateService();
        await first.SetItemAsync(new ClipboardItem { Text = "persisted" });
        first.Dispose();

        var second = CreateService();
        Assert.Contains(second.History, i => i.Text == "persisted");
        second.Dispose();
    }
}

using System;
using System.IO;
using System.Linq;
using Atoll.Core.Contracts;
using Atoll.Services.Productivity;
using Xunit;

namespace Atoll.UnitTests;

public class NotesServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"atoll_notes_test_{Guid.NewGuid():N}");

    public NotesServiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch { }
    }

    private NotesService Create() => new(_folder);

    [Fact]
    public void Save_BlankNewNote_IsSkipped()
    {
        var svc = Create();
        Assert.Null(svc.Save(Guid.NewGuid(), "", "", 0, null, keepImage: false));
        Assert.Empty(svc.Notes);
    }

    [Fact]
    public void Save_NewNoteWithoutTitle_BecomesUntitled_AndIsInsertedFirst()
    {
        var svc = Create();
        svc.Save(Guid.NewGuid(), "First", "a", 0, null, false);
        var second = svc.Save(Guid.NewGuid(), "", "body", 2, null, false);

        Assert.NotNull(second);
        Assert.Equal("Untitled Note", second!.Title);
        Assert.Equal(second.Id, svc.Notes[0].Id);
    }

    [Fact]
    public void Save_ExistingNote_UpdatesFieldsAndKeepsCreationDate()
    {
        var svc = Create();
        var id = Guid.NewGuid();
        var created = svc.Save(id, "Title", "one", 0, null, false)!;
        var updated = svc.Save(id, "", "two", 4, null, false)!;

        Assert.Single(svc.Notes);
        Assert.Equal("", updated.Title); // an existing note may be cleared, like the original
        Assert.Equal("two", updated.Content);
        Assert.Equal(4, updated.ColorIndex);
        Assert.Equal(created.CreationDate, updated.CreationDate);
        Assert.True(updated.ModificationDate >= created.ModificationDate);
    }

    [Fact]
    public void Notes_PersistAcrossInstances()
    {
        var id = Guid.NewGuid();
        Create().Save(id, "Keep me", "text", 1, null, false);

        var reloaded = Create().Notes.Single();
        Assert.Equal(id, reloaded.Id);
        Assert.Equal("Keep me", reloaded.Title);
        Assert.Equal(1, reloaded.ColorIndex);
    }

    [Fact]
    public void Image_IsStored_KeptOnEdit_AndDeletedWithNote()
    {
        var svc = Create();
        var id = Guid.NewGuid();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var note = svc.Save(id, "", "", 0, png, keepImage: false)!; // image alone is enough to save
        var path = svc.ImagePath(note);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        note = svc.Save(id, "t", "c", 0, null, keepImage: true)!;
        Assert.Equal(path, svc.ImagePath(note));

        svc.Delete(id);
        Assert.False(File.Exists(path));
        Assert.Empty(svc.Notes);
    }

    [Fact]
    public void Save_WithoutKeepImage_RemovesTheImageFile()
    {
        var svc = Create();
        var id = Guid.NewGuid();
        var path = svc.ImagePath(svc.Save(id, "t", "", 0, new byte[] { 1, 2, 3 }, false)!);
        svc.Save(id, "t", "", 0, null, keepImage: false);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TogglePin_And_Clear()
    {
        var svc = Create();
        var id = Guid.NewGuid();
        svc.Save(id, "a", "", 0, null, false);
        svc.TogglePin(id);
        Assert.True(svc.Notes.Single().IsPinned);

        var changes = 0;
        svc.Changed += (_, _) => changes++;
        svc.Clear();
        Assert.Empty(svc.Notes);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Sorted_PinnedFirst_ThenNewest_WithSearchAndColorFilter()
    {
        var now = DateTimeOffset.Now;
        var old = new NoteItem { Title = "Groceries", Content = "milk", ColorIndex = 3, ModificationDate = now.AddHours(-2) };
        var recent = new NoteItem { Title = "Ideas", Content = "notch", ColorIndex = 1, ModificationDate = now };
        var pinned = new NoteItem { Title = "Pinned", Content = "", ColorIndex = 1, IsPinned = true, ModificationDate = now.AddDays(-3) };
        var all = new[] { old, recent, pinned };

        Assert.Equal(new[] { pinned, recent, old }, NoteQuery.Sorted(all));
        Assert.Equal(new[] { old }, NoteQuery.Sorted(all, search: "MILK"));
        Assert.Equal(new[] { pinned, recent }, NoteQuery.Sorted(all, colorFilter: 1));
    }

    [Theory]
    [InlineData(30, "Just now")]
    [InlineData(5 * 60, "5m")]
    [InlineData(3 * 3600, "3h")]
    public void TimeAgo_MatchesOriginal(int secondsAgo, string expected)
    {
        var now = DateTimeOffset.Now;
        Assert.Equal(expected, NoteQuery.TimeAgo(now.AddSeconds(-secondsAgo), now));
    }
}

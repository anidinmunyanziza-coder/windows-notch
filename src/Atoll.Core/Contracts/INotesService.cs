using System;
using System.Collections.Generic;
using System.Linq;

namespace Atoll.Core.Contracts;

/// <summary>
/// Saved notes. Translated from the original's <c>NoteItem</c> /
/// <c>Defaults[.savedNotes]</c> and the actions in <c>NotchNotesView</c>.
/// </summary>
public interface INotesService
{
    /// <summary>Raised after any change to the saved notes.</summary>
    event EventHandler? Changed;

    /// <summary>Notes in stored order (newest created first).</summary>
    IReadOnlyList<NoteItem> Notes { get; }

    /// <summary>Upserts a note the way the original's <c>persistNote()</c> does:
    /// a new note that is completely empty is not saved; a new note without a
    /// title becomes "Untitled Note"; existing notes keep their creation date and
    /// get a fresh modification date. Returns the saved note, or null if skipped.</summary>
    NoteItem? Save(Guid id, string title, string content, int colorIndex, byte[]? imagePng, bool keepImage);

    void Delete(Guid id);
    void Clear();
    void TogglePin(Guid id);

    /// <summary>Full path of a note's attached image, or null.</summary>
    string? ImagePath(NoteItem note);
}

public sealed class NoteItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTimeOffset CreationDate { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset ModificationDate { get; set; } = DateTimeOffset.Now;

    /// <summary>0 yellow, 1 blue, 2 red, 3 green, 4 purple, 5 orange.</summary>
    public int ColorIndex { get; set; }
    public bool IsPinned { get; set; }
    public string? ImageFileName { get; set; }

    public NoteItem Clone() => (NoteItem)MemberwiseClone();
}

/// <summary>The original's <c>NoteListView.sortedNotes</c>: search + colour
/// filter, pinned first, then most recently modified.</summary>
public static class NoteQuery
{
    public const int ColorCount = 6;

    public static IReadOnlyList<NoteItem> Sorted(IEnumerable<NoteItem> notes, string? search = null, int? colorFilter = null)
    {
        var q = notes;
        if (!string.IsNullOrEmpty(search))
            q = q.Where(n => n.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                          || n.Content.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        if (colorFilter is { } c)
            q = q.Where(n => n.ColorIndex == c);
        return q.OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.ModificationDate).ToList();
    }

    /// <summary>NoteRow.timeAgoString: "Just now", "5m", "3h", else "12 Mar".</summary>
    public static string TimeAgo(DateTimeOffset date, DateTimeOffset now)
    {
        var s = (now - date).TotalSeconds;
        if (s < 60) return "Just now";
        if (s < 3600) return $"{(int)(s / 60)}m";
        if (s < 86400) return $"{(int)(s / 3600)}h";
        return date.ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Atoll.Core.Contracts;

namespace Atoll.Services.Productivity;

/// <summary>
/// Saved notes persisted as JSON (notes.json) with attached images stored as
/// PNG files beside it (NoteImages/note_image_{id}.png) — the same layout the
/// original uses (Defaults[.savedNotes] + Documents/NoteImages).
/// </summary>
public sealed class NotesService : INotesService
{
    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly string _imageDir;
    private List<NoteItem> _notes = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public event EventHandler? Changed;

    public NotesService(string? folderPath = null)
    {
        folderPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atoll");
        Directory.CreateDirectory(folderPath);
        _filePath = Path.Combine(folderPath, "notes.json");
        _imageDir = Path.Combine(folderPath, "NoteImages");
        Load();
    }

    public IReadOnlyList<NoteItem> Notes
    {
        get { lock (_sync) return _notes.Select(n => n.Clone()).ToList(); }
    }

    public NoteItem? Save(Guid id, string title, string content, int colorIndex, byte[]? imagePng, bool keepImage)
    {
        NoteItem saved;
        lock (_sync)
        {
            var existing = _notes.FirstOrDefault(n => n.Id == id);
            var hasImage = imagePng is not null || (keepImage && existing?.ImageFileName is not null);
            if (existing is null && title.Length == 0 && content.Length == 0 && !hasImage)
                return null; // never save a blank new note

            string? fileName = keepImage ? existing?.ImageFileName : null;
            if (imagePng is not null)
            {
                Directory.CreateDirectory(_imageDir);
                fileName = $"note_image_{id}.png";
                File.WriteAllBytes(Path.Combine(_imageDir, fileName), imagePng);
            }
            if (existing?.ImageFileName is { } old && old != fileName)
                TryDelete(old);

            var now = DateTimeOffset.Now;
            if (existing is not null)
            {
                existing.Title = title;
                existing.Content = content;
                existing.ColorIndex = colorIndex;
                existing.ImageFileName = fileName;
                existing.ModificationDate = now;
                saved = existing;
            }
            else
            {
                saved = new NoteItem
                {
                    Id = id,
                    Title = title.Length == 0 ? "Untitled Note" : title,
                    Content = content,
                    CreationDate = now,
                    ModificationDate = now,
                    ColorIndex = colorIndex,
                    ImageFileName = fileName,
                };
                _notes.Insert(0, saved);
            }
            Persist();
            saved = saved.Clone();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    public void Delete(Guid id)
    {
        lock (_sync)
        {
            var note = _notes.FirstOrDefault(n => n.Id == id);
            if (note is null) return;
            if (note.ImageFileName is { } f) TryDelete(f);
            _notes.Remove(note);
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_sync)
        {
            foreach (var n in _notes)
                if (n.ImageFileName is { } f) TryDelete(f);
            _notes.Clear();
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePin(Guid id)
    {
        lock (_sync)
        {
            var note = _notes.FirstOrDefault(n => n.Id == id);
            if (note is null) return;
            note.IsPinned = !note.IsPinned;
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string? ImagePath(NoteItem note)
    {
        if (note.ImageFileName is null) return null;
        var path = Path.Combine(_imageDir, note.ImageFileName);
        return File.Exists(path) ? path : null;
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
                _notes = JsonSerializer.Deserialize<List<NoteItem>>(File.ReadAllText(_filePath), JsonOptions) ?? new();
        }
        catch
        {
            _notes = new(); // corrupt file: start empty rather than crash
        }
    }

    private void Persist()
    {
        try
        {
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_notes, JsonOptions));
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch { }
    }

    private void TryDelete(string fileName)
    {
        try { File.Delete(Path.Combine(_imageDir, fileName)); } catch { }
    }
}

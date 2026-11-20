using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atoll.Core.Contracts;

namespace Atoll.Services.Productivity;

/// <summary>
/// In-memory file shelf — holds dropped file paths so they can be grabbed again
/// and dragged out elsewhere. Translated from the original Shelf / drop.swift.
/// The files themselves are not copied; the shelf keeps references to them.
/// </summary>
public sealed class ShelfService : IShelfService
{
    private readonly List<ShelfItem> _items = new();
    private readonly object _sync = new();

    public event EventHandler<ShelfItem>? ItemAdded;
    public event EventHandler<ShelfItem>? ItemRemoved;
    public event EventHandler? Cleared;

    public IReadOnlyList<ShelfItem> Items
    {
        get { lock (_sync) return _items.ToList(); }
    }

    public bool DynamicShelf { get; set; }

    public Task AddItemAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return Task.CompletedTask;

        ShelfItem item;
        lock (_sync)
        {
            // Skip duplicates by path.
            if (_items.Any(i => string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
                return Task.CompletedTask;

            item = new ShelfItem
            {
                FilePath = filePath,
                DisplayName = Path.GetFileName(filePath),
            };
            _items.Add(item);
        }
        ItemAdded?.Invoke(this, item);
        return Task.CompletedTask;
    }

    public Task RemoveItemAsync(string itemId, CancellationToken ct = default)
    {
        ShelfItem? removed = null;
        lock (_sync)
        {
            var idx = _items.FindIndex(i => i.Id == itemId);
            if (idx >= 0) { removed = _items[idx]; _items.RemoveAt(idx); }
        }
        if (removed is not null) ItemRemoved?.Invoke(this, removed);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        lock (_sync) _items.Clear();
        Cleared?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string itemId, CancellationToken ct = default)
    {
        lock (_sync) return Task.FromResult(_items.Any(i => i.Id == itemId));
    }

    public void Dispose() { }
}

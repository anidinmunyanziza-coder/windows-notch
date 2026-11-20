using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Atoll.Core.Contracts;

/// <summary>
/// File shelf. Translated from <c>components/Shelf</c>, <c>ShelfItem</c>,
/// <c>Bookmark</c>, <c>drop.swift</c>.
/// </summary>
public interface IShelfService : IDisposable
{
    event EventHandler<ShelfItem>? ItemAdded;
    event EventHandler<ShelfItem>? ItemRemoved;
    event EventHandler? Cleared;

    IReadOnlyList<ShelfItem> Items { get; }
    bool DynamicShelf { get; set; }

    Task AddItemAsync(string filePath, CancellationToken ct = default);
    Task RemoveItemAsync(string itemId, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(string itemId, CancellationToken ct = default);
}

public sealed class ShelfItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string FilePath { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? IconUri { get; init; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
}

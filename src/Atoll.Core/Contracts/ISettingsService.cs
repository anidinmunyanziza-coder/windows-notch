using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Atoll.Core.Contracts;

/// <summary>
/// Persisted application settings. Translated from Swift <c>Defaults</c> /
/// <c>UserDefaults</c> (374 keys, 38 sections - see
/// <c>docs/notes/settings-schema.json</c>).
/// Windows implementation: <c>Windows.Storage.ApplicationData.LocalSettings</c>
/// plus local JSON files for larger values.
/// </summary>
public interface ISettingsService : IDisposable
{
    event EventHandler<SettingChangedEventArgs>? SettingChanged;

    T Get<T>(string key, T defaultValue);
    void Set<T>(string key, T value);
    bool Contains(string key);
    void Remove(string key);
    IEnumerable<string> GetAllKeys();
    Task SaveAsync();
    Task LoadAsync();
}

public sealed class SettingChangedEventArgs : EventArgs
{
    public string Key { get; }
    public object? OldValue { get; }
    public object? NewValue { get; }
    public SettingChangedEventArgs(string key, object? oldValue, object? newValue)
    {
        Key = key; OldValue = oldValue; NewValue = newValue;
    }
}

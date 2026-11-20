using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Atoll.Core.Contracts;

namespace Atoll.Services.Settings;

/// <summary>
/// Persisted settings backed by a local JSON file in %LOCALAPPDATA%\Atoll.
/// Translated from Swift <c>Defaults</c>/<c>UserDefaults</c> (374 keys).
/// This keeps the app working unpackaged; a <c>Windows.Storage.ApplicationData</c>
/// LocalSettings implementation can be swapped in behind the same interface.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private readonly Dictionary<string, object?> _values = new();
    private readonly object _lock = new();

    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    public JsonSettingsService(string? folderPath = null)
    {
        folderPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Atoll");
        Directory.CreateDirectory(folderPath);
        _filePath = Path.Combine(folderPath, "settings.json");
        LoadAsync().GetAwaiter().GetResult();
    }

    public T Get<T>(string key, T defaultValue)
    {
        lock (_lock)
        {
            if (!_values.TryGetValue(key, out var raw))
                return defaultValue;
            if (raw is T t)
                return t;
            // JSON numbers deserialize as long/double; convert to the requested
            // numeric type so round-trips of int/double work across saves.
            try
            {
                var converted = Convert.ChangeType(raw, typeof(T), null);
                return converted is T t2 ? t2 : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    public void Set<T>(string key, T value)
    {
        object? old;
        lock (_lock)
        {
            old = _values.TryGetValue(key, out var existing) ? existing : null;
            _values[key] = value;
        }
        SaveUnsafe();
        SettingChanged?.Invoke(this, new SettingChangedEventArgs(key, old, value));
    }

    public bool Contains(string key)
    {
        lock (_lock)
        {
            return _values.ContainsKey(key);
        }
    }

    public void Remove(string key)
    {
        object? old = null;
        lock (_lock)
        {
            if (_values.TryGetValue(key, out old))
                _values.Remove(key);
        }
        if (old is not null) SaveUnsafe();
        SettingChanged?.Invoke(this, new SettingChangedEventArgs(key, old, null));
    }

    public IEnumerable<string> GetAllKeys()
    {
        lock (_lock)
        {
            return new List<string>(_values.Keys);
        }
    }

    public Task SaveAsync()
    {
        SaveUnsafe();
        return Task.CompletedTask;
    }

    public Task LoadAsync()
    {
        var loaded = new Dictionary<string, object?>();
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _options);
                if (dict is not null)
                    foreach (var kvp in dict)
                        loaded[kvp.Key] = JsonElementToObject(kvp.Value);
            }
        }
        catch (Exception ex)
        {
            // Corrupt settings file: start fresh rather than crash.
            Console.WriteLine($"[WARN] settings load failed: {ex.Message}");
        }
        lock (_lock)
        {
            _values.Clear();
            foreach (var kvp in loaded) _values[kvp.Key] = kvp.Value;
        }
        return Task.CompletedTask;
    }

    private void SaveUnsafe()
    {
        try
        {
            var serializable = new Dictionary<string, object?>();
            lock (_lock)
            {
                foreach (var kvp in _values)
                    serializable[kvp.Key] = kvp.Value;
            }
            var json = JsonSerializer.Serialize(serializable, _options);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] settings save failed: {ex.Message}");
        }
    }

    private static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Null => null,
        JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(),
        JsonValueKind.Object => el.EnumerateObject()
            .ToDictionary(o => o.Name, o => JsonElementToObject(o.Value)),
        _ => el.GetRawText()
    };

    public void Dispose() { }
}

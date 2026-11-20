using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Atoll.Core.Contracts;

namespace Atoll.UI.Settings;

/// <summary>
/// Represents a single setting from the extracted defaults schema.
/// </summary>
public sealed class SettingDescriptor
{
    public string Section { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Default { get; set; } = string.Empty;
    public string? SwiftName { get; set; }
}

public sealed class EnumOption
{
    public string Name { get; set; } = string.Empty;
    public List<string> Values { get; set; } = new();
}

/// <summary>
/// Loads the extracted Atoll defaults schema (374 keys, 38 sections) and the
/// enum case table extracted from the original Swift sources, and provides
/// access to settings grouped by section.
/// </summary>
public sealed class SettingsSchema
{
    private readonly List<SettingDescriptor> _settings = new();
    private readonly Dictionary<string, List<string>> _enumOptions =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Sections { get; }
    public IReadOnlyList<SettingDescriptor> AllSettings => _settings;

    public SettingsSchema(string schemaJsonPath)
    {
        var json = File.ReadAllText(schemaJsonPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var descriptors = JsonSerializer.Deserialize<List<SettingDescriptor>>(json, options);
        _settings.AddRange(descriptors ?? new List<SettingDescriptor>());

        var sections = new List<string>();
        foreach (var s in _settings)
        {
            if (!sections.Contains(s.Section))
                sections.Add(s.Section);
        }
        Sections = sections;

        var enumPath = Path.Combine(
            Path.GetDirectoryName(schemaJsonPath) ?? string.Empty,
            "enum-options.json");
        if (File.Exists(enumPath))
            LoadEnumOptions(enumPath);
    }

    private void LoadEnumOptions(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<List<EnumOption>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (items is null) return;

            foreach (var item in items)
            {
                if (item.Values.Count > 0)
                    _enumOptions[item.Name] = item.Values;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] enum-options load failed: {ex.Message}");
        }
    }

    public IReadOnlyList<SettingDescriptor> GetSettingsForSection(string section)
    {
        return _settings.FindAll(s => s.Section == section).AsReadOnly();
    }

    public IReadOnlyList<string> GetEnumOptions(string type)
    {
        return _enumOptions.TryGetValue(type, out var values)
            ? values.AsReadOnly()
            : Array.Empty<string>();
    }

    /// <summary>
    /// Determines the appropriate WPF editor for a setting descriptor.
    /// Complex Swift types (arrays, dictionaries, optional model objects)
    /// have no generic editor and are shown read-only rather than guessed.
    /// </summary>
    public string GetEditorType(SettingDescriptor descriptor)
    {
        var type = descriptor.Type.Trim();

        if (type == "Bool") return "CheckBox";
        if (type == "Bool?") return "BoolComboBox";
        if (type is "Int" or "Int32" or "Double" or "CGFloat" or "TimeInterval")
            return "NumericTextBox";
        if (type is "String") return "TextBox";
        if (type is "Color") return "ColorTextBox";

        if (type.StartsWith('[') || type.Contains(':') || type.EndsWith('?')
            || type is "Date" or "NSColor")
            return "ReadOnly";

        return GetEnumOptions(type).Count > 0 ? "EnumComboBox" : "TextBox";
    }

    /// <summary>
    /// Basic type-to-editor mapping without enum knowledge (kept for tests
    /// and callers that only have the type name).
    /// </summary>
    public static string EditorTypeFor(string type)
    {
        return type switch
        {
            "Bool" => "CheckBox",
            "Int" or "Int32" => "NumericTextBox",
            "Double" or "CGFloat" or "TimeInterval" => "NumericTextBox",
            "String" => "TextBox",
            "Color" => "ColorPicker",
            _ => "EnumComboBox",
        };
    }
}

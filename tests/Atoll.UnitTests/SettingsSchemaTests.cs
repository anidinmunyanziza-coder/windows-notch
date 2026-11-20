using System;
using System.IO;
using System.Linq;
using Atoll.UI.Settings;
using Xunit;

namespace Atoll.UnitTests;

public class SettingsSchemaTests
{
    private static string FindSchema()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "notes", "settings-schema.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("docs/notes/settings-schema.json not found");
    }

    [Fact]
    public void Schema_ContainsAllExtractedKeysAndSections()
    {
        var schema = new SettingsSchema(FindSchema());

        Assert.Equal(374, schema.AllSettings.Count);
        Assert.Equal(38, schema.Sections.Count);
        Assert.Equal(schema.Sections.Count, schema.Sections.Distinct().Count());
    }

    [Fact]
    public void Schema_SectionsExposeTheirSettings()
    {
        var schema = new SettingsSchema(FindSchema());

        var total = schema.Sections.Sum(s => schema.GetSettingsForSection(s).Count);
        Assert.Equal(schema.AllSettings.Count, total);

        var general = schema.GetSettingsForSection("General");
        Assert.NotEmpty(general);
    }

    [Theory]
    [InlineData("Bool", "CheckBox")]
    [InlineData("Int", "NumericTextBox")]
    [InlineData("Int32", "NumericTextBox")]
    [InlineData("Double", "NumericTextBox")]
    [InlineData("CGFloat", "NumericTextBox")]
    [InlineData("TimeInterval", "NumericTextBox")]
    [InlineData("String", "TextBox")]
    [InlineData("Color", "ColorPicker")]
    [InlineData("Enum", "EnumComboBox")]
    public void EditorTypeFor_MapsTypesToEditors(string type, string expected)
    {
        Assert.Equal(expected, SettingsSchema.EditorTypeFor(type));
    }

    [Fact]
    public void Schema_EveryKeyIsUnique()
    {
        var schema = new SettingsSchema(FindSchema());
        var duplicates = schema.AllSettings
            .GroupBy(s => s.Key)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Schema_EditorTypes_CoverEverySettingWithoutThrowing()
    {
        var schema = new SettingsSchema(FindSchema());
        var valid = new HashSet<string>
        {
            "CheckBox", "BoolComboBox", "NumericTextBox", "TextBox",
            "ColorTextBox", "EnumComboBox", "ReadOnly",
        };

        foreach (var descriptor in schema.AllSettings)
        {
            var editor = schema.GetEditorType(descriptor);
            Assert.Contains(editor, valid);
        }
    }

    [Fact]
    public void Schema_EnumsHaveOptionsFromExtractedSwiftCases()
    {
        var schema = new SettingsSchema(FindSchema());

        Assert.NotEmpty(schema.GetEnumOptions("ClipboardDisplayMode"));
        Assert.Contains("separateTab", schema.GetEnumOptions("ClipboardDisplayMode"));
        Assert.Contains("stable", schema.GetEnumOptions("UpdateChannel"));
        Assert.Contains("track", schema.GetEnumOptions("MusicSkipBehavior"));
    }

    [Fact]
    public void Schema_ComplexTypesAreReadOnly()
    {
        var schema = new SettingsSchema(FindSchema());

        var array = new SettingDescriptor { Type = "[TimerPreset]" };
        Assert.Equal("ReadOnly", schema.GetEditorType(array));

        var optional = new SettingDescriptor { Type = "Date?" };
        Assert.Equal("ReadOnly", schema.GetEditorType(optional));
    }
}

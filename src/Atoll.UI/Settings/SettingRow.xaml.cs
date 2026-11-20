using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Atoll.Core.Contracts;

namespace Atoll.UI.Settings;

/// <summary>
/// A single settings row: key name + type-appropriate editor.
/// Values are read through <see cref="ISettingsService"/>; when a key has
/// never been written the schema default from the original Swift
/// <c>Defaults</c> definition is used instead of a fabricated zero.
/// </summary>
public partial class SettingRow : UserControl
{
    private readonly ISettingsService _settings;
    private readonly SettingsSchema _schema;
    private readonly SettingDescriptor _descriptor;
    private readonly string _editorType;
    private bool _updating;

    public SettingRow(ISettingsService settings, SettingsSchema schema, SettingDescriptor descriptor)
    {
        _settings = settings;
        _schema = schema;
        _descriptor = descriptor;
        _editorType = schema.GetEditorType(descriptor);

        InitializeComponent();

        KeyLabel.Text = DeriveDisplayName(descriptor.Key);
        KeyLabel.ToolTip = $"{descriptor.Key}  ·  {descriptor.Type}  ·  default: {descriptor.Default}";

        // Expose the schema key to accessibility tools and UI tests; the
        // visual label is a humanised form of the same key.
        AutomationProperties.SetName(BoolEditor, descriptor.Key);
        AutomationProperties.SetName(TextEditor, descriptor.Key);
        AutomationProperties.SetName(EnumEditor, descriptor.Key);

        ConfigureEditor();
        LoadValue();
    }

    /// <summary>
    /// Humanises a Defaults key (updateChannel → "Update Channel") without
    /// inventing a label that is not present in the schema.
    /// </summary>
    public static string DeriveDisplayName(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;

        var sb = new StringBuilder(key.Length + 8);
        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (i > 0 && char.IsUpper(c))
            {
                var prev = key[i - 1];
                var nextLower = i + 1 < key.Length && char.IsLower(key[i + 1]);
                if (char.IsLower(prev) || (char.IsUpper(prev) && nextLower))
                    sb.Append(' ');
            }
            sb.Append(c);
        }

        var text = sb.ToString();
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private void ConfigureEditor()
    {
        switch (_editorType)
        {
            case "CheckBox":
                BoolEditor.Visibility = Visibility.Visible;
                break;

            case "BoolComboBox":
            case "EnumComboBox":
                EnumEditor.Visibility = Visibility.Visible;
                EnumEditor.Items.Add("(unset)");
                if (_editorType == "BoolComboBox")
                {
                    EnumEditor.Items.Add("true");
                    EnumEditor.Items.Add("false");
                }
                else
                {
                    foreach (var option in _schema.GetEnumOptions(_descriptor.Type))
                        EnumEditor.Items.Add(option);
                }
                break;

            case "NumericTextBox":
            case "TextBox":
            case "ColorTextBox":
                TextEditor.Visibility = Visibility.Visible;
                break;

            case "ReadOnly":
                TextEditor.Visibility = Visibility.Visible;
                TextEditor.IsReadOnly = true;
                TextEditor.Foreground = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A));
                TextEditor.ToolTip =
                    "Complex Swift type. Edit the value in settings.json; " +
                    "no generic editor is available.";
                break;
        }

        if (_editorType == "ColorTextBox")
            ColorSwatch.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------
    // Read
    // ------------------------------------------------------------------

    private void LoadValue()
    {
        _updating = true;
        try
        {
            switch (_editorType)
            {
                case "CheckBox":
                    BoolEditor.IsChecked = _settings.Contains(_descriptor.Key)
                        ? _settings.Get(_descriptor.Key, ParseBoolDefault())
                        : ParseBoolDefault();
                    break;

                case "BoolComboBox":
                    var currentBool = _settings.Contains(_descriptor.Key)
                        ? _settings.Get<object?>(_descriptor.Key, null)
                        : null;
                    EnumEditor.SelectedItem = currentBool switch
                    {
                        bool b => b ? "true" : "false",
                        string s when bool.TryParse(s, out var parsed) => parsed ? "true" : "false",
                        _ => "(unset)",
                    };
                    break;

                case "EnumComboBox":
                    var currentEnum = (_settings.Contains(_descriptor.Key)
                        ? _settings.Get(_descriptor.Key, string.Empty)
                        : null) ?? ParseEnumDefault();
                    currentEnum = NormalizeEnum(currentEnum);
                    if (!string.IsNullOrEmpty(currentEnum) &&
                        !EnumEditor.Items.Contains(currentEnum))
                    {
                        // Preserve values written by the original app or a
                        // newer schema instead of silently dropping them.
                        EnumEditor.Items.Add(currentEnum);
                    }
                    EnumEditor.SelectedItem = string.IsNullOrEmpty(currentEnum)
                        ? "(unset)"
                        : currentEnum;
                    break;

                case "NumericTextBox":
                    TextEditor.Text = _settings.Contains(_descriptor.Key)
                        ? FormatNumber(_settings.Get<object?>(_descriptor.Key, null))
                        : FormatNumber(ParseNumericDefault());
                    break;

                case "TextBox":
                    TextEditor.Text = _settings.Contains(_descriptor.Key)
                        ? _settings.Get(_descriptor.Key, string.Empty)
                        : ParseStringDefault();
                    break;

                case "ColorTextBox":
                    var color = _settings.Contains(_descriptor.Key)
                        ? _settings.Get(_descriptor.Key, string.Empty)
                        : ParseColorDefault();
                    if (string.IsNullOrWhiteSpace(color)) color = ParseColorDefault();
                    TextEditor.Text = color;
                    UpdateSwatch(color);
                    break;

                case "ReadOnly":
                    TextEditor.Text = _settings.Contains(_descriptor.Key)
                        ? _settings.Get(_descriptor.Key, _descriptor.Default).ToString()
                        : _descriptor.Default;
                    break;
            }
        }
        finally
        {
            _updating = false;
        }
    }

    // ------------------------------------------------------------------
    // Write
    // ------------------------------------------------------------------

    private void OnBoolChanged(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _settings.Set(_descriptor.Key, BoolEditor.IsChecked == true);
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || _editorType == "ReadOnly") return;

        switch (_editorType)
        {
            case "NumericTextBox":
                if (TryParseNumber(TextEditor.Text, out var number))
                {
                    if (_descriptor.Type is "Int" or "Int32")
                        _settings.Set(_descriptor.Key, (int)Math.Round(number));
                    else
                        _settings.Set(_descriptor.Key, number);
                    TextEditor.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                }
                else
                {
                    TextEditor.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
                }
                break;

            case "TextBox":
                _settings.Set(_descriptor.Key, TextEditor.Text);
                break;

            case "ColorTextBox":
                var value = TextEditor.Text.Trim();
                if (TryParseColor(value, out var brush))
                {
                    _settings.Set(_descriptor.Key, value);
                    TextEditor.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                    UpdateSwatch(value);
                }
                else
                {
                    TextEditor.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
                }
                break;
        }
    }

    private void OnTextLostFocus(object sender, RoutedEventArgs e)
    {
        if (_editorType is "NumericTextBox" or "ColorTextBox")
            LoadValue(); // revert invalid input to the persisted value
    }

    private void OnEnumChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if (EnumEditor.SelectedItem is not string selected) return;

        if (_editorType == "BoolComboBox")
        {
            switch (selected)
            {
                case "(unset)":
                    _settings.Remove(_descriptor.Key);
                    break;
                case "true":
                    _settings.Set(_descriptor.Key, true);
                    break;
                case "false":
                    _settings.Set(_descriptor.Key, false);
                    break;
            }
            return;
        }

        if (selected == "(unset)")
            _settings.Remove(_descriptor.Key);
        else
            _settings.Set(_descriptor.Key, selected);
    }

    // ------------------------------------------------------------------
    // Defaults (the schema stores Swift expressions, not literals)
    // ------------------------------------------------------------------

    private bool ParseBoolDefault()
        => bool.TryParse(_descriptor.Default.Trim(), out var value) && value;

    private string ParseEnumDefault() => NormalizeEnum(_descriptor.Default);

    private string ParseStringDefault()
    {
        var value = _descriptor.Default.Trim();
        if (value is "nil" or "null") return string.Empty;
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        return value;
    }

    private double ParseNumericDefault()
    {
        // Handles literals ("0.3"), products ("30 * 60"), and falls back to 0
        // for expressions such as Double(SomeType.defaultValue).
        var expr = _descriptor.Default.Trim();
        var product = expr.Split('*', StringSplitOptions.TrimEntries);
        var result = 1.0;
        var any = false;
        foreach (var part in product)
        {
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            {
                result *= f;
                any = true;
            }
            else
            {
                return 0.0;
            }
        }
        return any ? result : 0.0;
    }

    private string ParseColorDefault()
    {
        var value = _descriptor.Default.Trim();
        var name = value.StartsWith("Color.", StringComparison.OrdinalIgnoreCase)
            ? value[6..]
            : value;

        return name.ToLowerInvariant() switch
        {
            "blue" => "#0A84FF",
            "red" => "#FF453A",
            "green" => "#30D158",
            "orange" => "#FF9F0A",
            "yellow" => "#FFD60A",
            "purple" => "#BF5AF2",
            "pink" => "#FF375F",
            "teal" => "#40C8E0",
            "indigo" => "#5E5CE6",
            "mint" => "#63E6E2",
            "gray" or "grey" => "#8E8E93",
            "black" => "#000000",
            "white" => "#FFFFFF",
            _ when value.StartsWith('#') => value,
            _ => "#FFFFFF",
        };
    }

    private static string NormalizeEnum(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.StartsWith('.')) v = v[1..];
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1];
        return v;
    }

    private static string FormatNumber(object? value)
    {
        return value switch
        {
            null => "0",
            double d => d.ToString("0.####", CultureInfo.InvariantCulture),
            float f => f.ToString("0.####", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
        };
    }

    private static bool TryParseNumber(string text, out double value)
        => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryParseColor(string text, out Brush brush)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(text);
            if (converted is Color color)
            {
                brush = new SolidColorBrush(color);
                return true;
            }
        }
        catch
        {
            // fall through
        }
        brush = Brushes.Transparent;
        return false;
    }

    private void UpdateSwatch(string text)
    {
        if (!TryParseColor(text, out var brush)) return;
        ColorSwatch.Background = brush;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Atoll.Core.Contracts;

namespace Atoll.UI.Settings;

/// <summary>
/// Settings window showing all 38 sections / 374 keys from the original
/// Atoll defaults schema. Changes are persisted via ISettingsService.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly SettingsSchema _schema;

    public SettingsWindow(ISettingsService settings)
    {
        _settings = settings;

        // Locate the embedded schema JSON.
        var schemaPath = FindSchemaPath();
        _schema = new SettingsSchema(schemaPath);

        InitializeComponent();

        // Populate sections sidebar. Subscribe before selecting so the first
        // section renders its rows immediately on open.
        SectionList.SelectionChanged += OnSectionChanged;
        SectionList.ItemsSource = _schema.Sections;
        SectionList.SelectedIndex = 0;

        SourceInitialized += (_, _) => TryEnableDarkTitleBar();
    }

    private void TryEnableDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int on = 1;
            // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (19 on Windows 10 builds < 19041).
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch { /* best effort — title bar stays light if DWM rejects it */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private string FindSchemaPath()
    {
        // Prefer the copy next to the executable (csproj Content copy).
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Settings", "settings-schema.json"),
            Path.Combine(AppContext.BaseDirectory, "settings-schema.json"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        // Development fallback: walk up from the binaries to the repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var docs = Path.Combine(dir.FullName, "docs", "notes", "settings-schema.json");
            if (File.Exists(docs)) return docs;

            var source = Path.Combine(dir.FullName, "src", "Atoll.UI", "Settings", "settings-schema.json");
            if (File.Exists(source)) return source;

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "settings-schema.json not found next to the executable or in the repository.");
    }

    private void OnSectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is not string section) return;
        SectionTitle.Text = section;

        var sectionSettings = _schema.GetSettingsForSection(section);
        SectionSubtitle.Text = sectionSettings.Count == 1
            ? "1 setting"
            : $"{sectionSettings.Count} settings";
        SettingsList.Items.Clear();

        foreach (var desc in sectionSettings)
        {
            var row = new SettingRow(_settings, _schema, desc);
            SettingsList.Items.Add(row);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Ensure all settings are persisted.
        _ = _settings.SaveAsync();
        base.OnClosed(e);
    }
}

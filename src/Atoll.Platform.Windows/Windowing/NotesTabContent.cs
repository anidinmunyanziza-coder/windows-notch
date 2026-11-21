using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Atoll.Core.Contracts;
using Atoll.Core.ViewModels;
using Atoll.Platform.Windows.Animation;
using Key = System.Windows.Input.Key;
using ModifierKeys = System.Windows.Input.ModifierKeys;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// The Notes tab — a port of the original's <c>NotchNotesView</c>:
/// a list (<c>NoteListView</c> / <c>NoteRow</c>) and an editor
/// (<c>NoteEditorView</c>) that slide over each other with
/// <c>.spring(response: 0.35, dampingFraction: 0.8)</c>. Notes autosave 1.5 s
/// after the last edit and on leaving the editor. Ctrl+V on the list creates a
/// note from the clipboard (image or text), like the original's ⌘V handler.
/// </summary>
public sealed class NotesTabContent : UserControl
{
    // SwiftUI dark-mode system colours: yellow, blue, red, green, purple, orange.
    public static readonly Color[] NoteColors =
    {
        Color.FromRgb(0xFF, 0xD6, 0x0A), Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0xFF, 0x45, 0x3A),
        Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0xFF, 0x9F, 0x0A),
    };

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic" };
    private static readonly Brush Secondary = Frozen(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Faint = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    private static readonly FontFamily Mdl2 = new("Segoe MDL2 Assets");
    private static readonly FontFamily Ui = new("Segoe UI Variable Display, Segoe UI");

    private readonly INotesService _service;
    private readonly ShellViewModel _shell;
    private readonly Action<bool> _onLayoutChanged;

    private readonly Grid _root = new() { ClipToBounds = true };
    private readonly Grid _listView = new();
    private Grid? _editorView;

    // list state
    private readonly Grid _listBody = new();
    private readonly StackPanel _headerButtons = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _searchArea = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    private TextBox? _searchBox;
    private string _search = "";
    private int? _colorFilter;
    private bool _searchExpanded;

    // editor state
    private Guid? _editingId;
    private bool _isNew;
    private TextBox? _titleBox, _contentBox;
    private int _colorIndex;
    private byte[]? _pendingImage;   // newly attached, not yet written
    private bool _hasStoredImage;    // the saved note's existing image is kept
    private BitmapSource? _editorImage;
    private Grid? _imageHost;
    private TextBlock? _charCount, _doneText;
    private StackPanel? _colorDots;
    private readonly DispatcherTimer _autoSave = new() { Interval = TimeSpan.FromSeconds(1.5) };

    public NotesTabContent(INotesService service, ShellViewModel shell, Action<bool> onLayoutChanged)
    {
        _service = service;
        _shell = shell;
        _onLayoutChanged = onLayoutChanged;
        Focusable = true;

        _autoSave.Tick += (_, _) => { _autoSave.Stop(); Persist(); };

        BuildList();
        _root.Children.Add(_listView);
        Content = _root;

        Loaded += (_, _) => _service.Changed += OnServiceChanged;
        Unloaded += (_, _) =>
        {
            _service.Changed -= OnServiceChanged;
            if (_editingId is not null) Persist();
            _autoSave.Stop();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += (_, _) => { if (!IsKeyboardFocusWithin) Focus(); };
    }

    private bool Setting(string key, bool fallback) => _shell.GetSetting(key, fallback);

    private void OnServiceChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => { if (_editingId is null) RefreshList(); });

    // ==================================================================
    // List
    // ==================================================================

    private void BuildList()
    {
        _listView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _listView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _listView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(16, 6, 16, 6) };
        header.Children.Add(new TextBlock
        {
            Text = "Notes",
            FontFamily = Ui,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _headerButtons.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(_headerButtons);
        Grid.SetRow(header, 0);
        _listView.Children.Add(header);

        Grid.SetRow(_searchArea, 1);
        _listView.Children.Add(_searchArea);

        Grid.SetRow(_listBody, 2);
        _listView.Children.Add(_listBody);

        RefreshList();
    }

    private void RefreshList()
    {
        var notes = _service.Notes;

        // ── header buttons ────────────────────────────────────────────
        _headerButtons.Children.Clear();
        if (Setting("enableNoteSearch", false) || Setting("enableNoteColorFiltering", false))
            _headerButtons.Children.Add(CircleButton("", _searchExpanded ? Brushes.DodgerBlue : Secondary,
                "Search", ToggleSearch));
        if (Setting("enableCreateFromClipboard", true))
            _headerButtons.Children.Add(CircleButton("", Secondary, "Create from Clipboard", CreateFromClipboard));
        if (notes.Count > 0)
            _headerButtons.Children.Add(CircleButton("", Frozen(NoteColors[2]), "Delete all notes", ConfirmClearAll));
        _headerButtons.Children.Add(CircleButton("", Brushes.White, "New note", () => OpenEditor(null), bold: true));

        BuildSearchArea(notes.Count > 0);
        RefreshBody(notes);
    }

    /// <summary>Rebuilds just the rows (keeps the search box and its focus).</summary>
    private void RefreshBody(IReadOnlyList<NoteItem>? notes = null)
    {
        notes ??= _service.Notes;
        _listBody.Children.Clear();
        var sorted = NoteQuery.Sorted(notes, _search, _colorFilter);
        if (sorted.Count == 0)
        {
            var empty = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14),
            };
            empty.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(_search) ? "" : "",
                FontFamily = Mdl2, FontSize = 16,
                Foreground = Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
            empty.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(_search) ? "No notes yet" : "No results found",
                FontFamily = Ui, FontSize = 12, FontWeight = FontWeights.Medium,
                Foreground = Secondary,
                VerticalAlignment = VerticalAlignment.Center,
            });
            _listBody.Children.Add(empty);
            return;
        }

        var compact = sorted.Count > 3; // grid of two columns past three notes
        Panel items = compact ? new UniformGrid { Columns = 2 } : new StackPanel();
        foreach (var note in sorted)
            items.Children.Add(NoteRow(note, compact));

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Border { Padding = new Thickness(12, 2, 12, 16), Child = items },
            PanningMode = PanningMode.VerticalOnly,
        };
        _listBody.Children.Add(scroll);

        // top / bottom fades into the black notch
        _listBody.Children.Add(Fade(top: true));
        _listBody.Children.Add(Fade(top: false));
    }

    private static Rectangle Fade(bool top) => new()
    {
        Height = 16,
        VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom,
        IsHitTestVisible = false,
        Fill = new LinearGradientBrush(
            top ? Color.FromArgb(0xA6, 0, 0, 0) : Colors.Transparent,
            top ? Colors.Transparent : Color.FromArgb(0xA6, 0, 0, 0), 90),
    };

    private void BuildSearchArea(bool hasNotes)
    {
        _searchArea.Children.Clear();
        _searchArea.Visibility = _searchExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (!_searchExpanded || !hasNotes) return;

        if (Setting("enableNoteSearch", false))
        {
            _searchBox = PlainTextBox(12, FontWeights.Normal);
            _searchBox.Text = _search;
            _searchBox.TextChanged += (_, _) => { _search = _searchBox.Text; RefreshBody(); };
            var box = new Grid();
            box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            box.ColumnDefinitions.Add(new ColumnDefinition());
            var glass = new TextBlock { Text = "", FontFamily = Mdl2, FontSize = 10, Foreground = Secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            box.Children.Add(glass);
            Grid.SetColumn(_searchBox, 1);
            box.Children.Add(_searchBox);
            box.Children.Add(Placeholder("Search notes…", 12, _searchBox, column: 1));
            _searchArea.Children.Add(new Border
            {
                Background = Frozen(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(16, 0, 16, 6),
                Child = box,
            });
        }

        if (Setting("enableNoteColorFiltering", false))
        {
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 0) };
            var all = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 2, 6, 2),
                Background = _colorFilter is null ? Frozen(Color.FromArgb(0x4D, 0x0A, 0x84, 0xFF)) : Faint,
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = "All", FontSize = 10, FontWeight = FontWeights.Medium, Foreground = Brushes.White },
                Margin = new Thickness(0, 0, 6, 0),
            };
            all.MouseLeftButtonUp += (_, _) => { _colorFilter = null; RefreshList(); };
            chips.Children.Add(all);
            for (var i = 0; i < NoteColors.Length; i++)
            {
                var index = i;
                var dot = ColorDot(index, 14, _colorFilter == index);
                dot.Margin = new Thickness(0, 0, 6, 0);
                dot.MouseLeftButtonUp += (_, _) => { _colorFilter = _colorFilter == index ? null : index; RefreshList(); };
                chips.Children.Add(dot);
            }
            _searchArea.Children.Add(chips);
        }
    }

    private void ToggleSearch()
    {
        _searchExpanded = !_searchExpanded;
        if (!_searchExpanded) { _search = ""; _colorFilter = null; }
        RefreshList();
        if (_searchExpanded) _searchBox?.Focus();
    }

    private FrameworkElement NoteRow(NoteItem note, bool compact)
    {
        var color = NoteColors[Math.Clamp(note.ColorIndex, 0, NoteColors.Length - 1)];
        var colorBrush = Frozen(color);

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        if (note.IsPinned)
            titleRow.Children.Add(new TextBlock
            {
                Text = "", FontFamily = Mdl2, FontSize = compact ? 7 : 8, Foreground = colorBrush,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
            });
        titleRow.Children.Add(new TextBlock
        {
            Text = note.Title, FontFamily = Ui, FontSize = compact ? 12 : 14, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var preview = new TextBlock
        {
            Text = note.Content.Length == 0 ? "No content" : note.Content,
            FontSize = compact ? 10 : 12,
            Foreground = Frozen(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap,
            MaxHeight = compact ? 14 : 32,
            Margin = new Thickness(0, compact ? 1 : 3, 0, 0),
        };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { titleRow, preview } };

        var bar = new Border
        {
            Width = compact ? 3 : 4, CornerRadius = new CornerRadius(2), Background = colorBrush,
            Margin = new Thickness(0, compact ? 4 : 2, compact ? 8 : 12, compact ? 4 : 2),
        };
        var main = new Grid { Margin = new Thickness(compact ? 8 : 12, compact ? 8 : 10, compact ? 50 : 130, compact ? 8 : 10) };
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        main.ColumnDefinitions.Add(new ColumnDefinition());
        main.Children.Add(bar);
        Grid.SetColumn(texts, 1);
        main.Children.Add(texts);

        // ── trailing: time-ago (or hover actions) + thumbnail ────────────
        var trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, compact ? 2 : 4, 0),
        };
        var timeAgo = new TextBlock
        {
            Text = NoteQuery.TimeAgo(note.CreationDate, DateTimeOffset.Now),
            FontSize = 10, Foreground = Frozen(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            Visibility = compact ? Visibility.Collapsed : Visibility.Visible,
        };
        var actions = ActionCapsule(note, compact);
        actions.Visibility = Visibility.Collapsed;
        trailing.Children.Add(actions);
        trailing.Children.Add(timeAgo);
        if (_service.ImagePath(note) is { } img && LoadBitmap(img, compact ? 64 : 100) is { } thumb)
        {
            var size = compact ? 32 : 50;
            trailing.Children.Add(new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(6),
                BorderBrush = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
                Background = new ImageBrush(thumb) { Stretch = Stretch.UniformToFill },
                Margin = new Thickness(0, 0, compact ? 4 : 8, 0),
            });
        }

        var cardBg = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = cardBg,
            BorderBrush = Frozen(Color.FromArgb(0x33, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(4),
            Cursor = Cursors.Hand,
            Child = new Grid { Children = { main, trailing } },
        };
        card.MouseEnter += (_, _) =>
        {
            cardBg.Color = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
            actions.Visibility = Visibility.Visible;
            timeAgo.Visibility = Visibility.Collapsed;
            FadeIn(actions);
        };
        card.MouseLeave += (_, _) =>
        {
            cardBg.Color = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF);
            actions.Visibility = Visibility.Collapsed;
            timeAgo.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInside(d, actions)) return;
            OpenEditor(note);
        };
        return card;
    }

    private FrameworkElement ActionCapsule(NoteItem note, bool compact)
    {
        var size = compact ? 9.0 : 11.0;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (Setting("enableNotePinning", true))
            panel.Children.Add(SmallAction(note.IsPinned ? "" : "", Brushes.White, size, compact,
                note.IsPinned ? "Unpin" : "Pin", _ => _service.TogglePin(note.Id)));

        panel.Children.Add(SmallAction("", Brushes.White, size, compact, "Copy", glyph =>
        {
            CopyNote(note);
            glyph.Text = "";
            glyph.Foreground = Frozen(NoteColors[3]);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += (_, _) => { t.Stop(); glyph.Text = ""; glyph.Foreground = Brushes.White; };
            t.Start();
        }));
        panel.Children.Add(SmallAction("", Frozen(NoteColors[2]), size, compact, "Delete",
            _ => _service.Delete(note.Id)));

        return new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = Frozen(Color.FromRgb(0x1F, 0x1F, 0x1F)),
            BorderBrush = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(compact ? 6 : 8, compact ? 2 : 3, compact ? 6 : 8, compact ? 2 : 3),
            Margin = new Thickness(0, 4, 4, 4),
            Child = panel,
        };
    }

    private static Button SmallAction(string glyph, Brush fg, double size, bool compact, string tip, Action<TextBlock> onClick)
    {
        var text = new TextBlock { Text = glyph, FontFamily = Mdl2, FontSize = size, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var pad = compact ? 5 : 6;
        var b = new Button
        {
            Content = text, ToolTip = tip, Cursor = Cursors.Hand,
            Width = size + pad * 2 + 2, Height = size + pad * 2 + 2,
            Margin = new Thickness(compact ? 1.5 : 2, 0, compact ? 1.5 : 2, 0),
            Template = CircleTemplate(Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), Frozen(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF))),
        };
        b.Click += (_, e) => { e.Handled = true; onClick(text); };
        return b;
    }

    private void CopyNote(NoteItem note)
    {
        try
        {
            var data = new DataObject();
            data.SetText(note.Content);
            if (_service.ImagePath(note) is { } img && LoadBitmap(img, 0) is { } bmp)
                data.SetImage(bmp);
            Clipboard.SetDataObject(data, true);
        }
        catch { }
    }

    // ── delete all (inline confirm in place of the original's alert) ────

    private void ConfirmClearAll()
    {
        var overlay = new Grid { Background = Frozen(Color.FromArgb(0xD9, 0, 0, 0)) };
        var card = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        card.Children.Add(new TextBlock { Text = "Delete All Notes?", FontFamily = Ui, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        card.Children.Add(new TextBlock
        {
            Text = "This permanently removes every saved note and its attachments.",
            FontSize = 11, Foreground = Secondary, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            MaxWidth = 260, Margin = new Thickness(0, 4, 0, 10),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(PillButton("Cancel", Brushes.White, Faint, () => _root.Children.Remove(overlay)));
        buttons.Children.Add(PillButton("Delete", Brushes.White, Frozen(NoteColors[2]), () =>
        {
            _root.Children.Remove(overlay);
            _service.Clear();
        }));
        card.Children.Add(buttons);
        overlay.Children.Add(card);
        _root.Children.Add(overlay);
        FadeIn(overlay);
    }

    // ==================================================================
    // Editor
    // ==================================================================

    private void OpenEditor(NoteItem? note, string content = "", BitmapSource? image = null)
    {
        _editingId = note?.Id ?? Guid.NewGuid();
        _isNew = note is null;
        _colorIndex = note?.ColorIndex ?? 0;
        _pendingImage = null;
        _hasStoredImage = note?.ImageFileName is not null;
        _editorImage = note is not null && _service.ImagePath(note) is { } p ? LoadBitmap(p, 0) : null;
        if (image is not null) SetEditorImage(image, rebuild: false);

        _editorView = BuildEditor(note?.Title ?? "", note?.Content ?? content);
        _root.Children.Add(_editorView);
        _onLayoutChanged(true);

        // editor in from the trailing edge, list out to the leading edge
        SlideX(_editorView, ActualWidthOr(420), 0, null);
        SlideX(_listView, 0, -ActualWidthOr(420), null);

        if (_isNew)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _contentBox?.Focus());
    }

    private void CloseEditor()
    {
        _autoSave.Stop();
        Persist();
        _editingId = null;
        var editor = _editorView;
        _editorView = null;
        _contentBox = _titleBox = null;
        _onLayoutChanged(false);
        RefreshList();
        SlideX(_listView, -ActualWidthOr(420), 0, null);
        if (editor is not null)
            SlideX(editor, 0, ActualWidthOr(420), () => _root.Children.Remove(editor));
        Focus();
    }

    private Grid BuildEditor(string title, string content)
    {
        var view = new Grid { Background = Brushes.Black };
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── toolbar ───────────────────────────────────────────────────
        var toolbar = new Grid { Margin = new Thickness(16, 6, 16, 4) };
        var back = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        back.Children.Add(new TextBlock { Text = "", FontFamily = Mdl2, FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 4, 0) });
        back.Children.Add(new TextBlock { Text = "Notes", FontFamily = Ui, FontSize = 15, FontWeight = FontWeights.Medium, Foreground = Brushes.White });
        back.MouseLeftButtonUp += (_, _) => CloseEditor();
        toolbar.Children.Add(back);
        _doneText = new TextBlock
        {
            Text = "Done", FontFamily = Ui, FontSize = 15, FontWeight = FontWeights.Bold,
            Foreground = Frozen(NoteColors[_colorIndex]), HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand, Background = Brushes.Transparent,
        };
        _doneText.MouseLeftButtonUp += (_, _) => CloseEditor();
        toolbar.Children.Add(_doneText);
        Grid.SetRow(toolbar, 0);
        view.Children.Add(toolbar);

        // ── title + colour picker ─────────────────────────────────────
        var titleRow = new Grid { Margin = new Thickness(16, 0, 10, 6) };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition());
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _titleBox = PlainTextBox(17, FontWeights.Bold);
        _titleBox.Text = title;
        _titleBox.TextChanged += (_, _) => ScheduleAutoSave();
        titleRow.Children.Add(_titleBox);
        titleRow.Children.Add(Placeholder("Title", 17, _titleBox, column: 0, bold: true));
        _colorDots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        RebuildColorDots();
        Grid.SetColumn(_colorDots, 1);
        titleRow.Children.Add(_colorDots);
        Grid.SetRow(titleRow, 1);
        view.Children.Add(titleRow);

        var divider = new Border { Height = 1, Background = Faint };
        Grid.SetRow(divider, 2);
        view.Children.Add(divider);

        // ── content ───────────────────────────────────────────────────
        var body = new Grid { Background = Frozen(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)) };
        _contentBox = PlainTextBox(13, FontWeights.Normal);
        _contentBox.AcceptsReturn = true;
        _contentBox.AcceptsTab = true;
        _contentBox.TextWrapping = TextWrapping.Wrap;
        _contentBox.VerticalContentAlignment = VerticalAlignment.Top;
        _contentBox.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _contentBox.Padding = new Thickness(8, 8, 8, 22);
        _contentBox.Text = content;
        _contentBox.TextChanged += (_, _) => { UpdateCharCount(); ScheduleAutoSave(); };
        body.Children.Add(_contentBox);
        var placeholder = Placeholder("Start typing...", 13, _contentBox, column: 0);
        placeholder.VerticalAlignment = VerticalAlignment.Top;
        placeholder.Margin = new Thickness(12, 9, 0, 0);
        body.Children.Add(placeholder);

        _imageHost = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 12, 26) };
        body.Children.Add(_imageHost);
        RebuildEditorImage();

        if (Setting("enableNoteCharCount", true))
        {
            _charCount = new TextBlock
            {
                FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 10, FontWeight = FontWeights.Medium,
                Foreground = Frozen(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            };
            body.Children.Add(new Border
            {
                Background = Frozen(Color.FromArgb(0x80, 0, 0, 0)), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false, Child = _charCount,
            });
            UpdateCharCount();
        }
        Grid.SetRow(body, 3);
        view.Children.Add(body);
        return view;
    }

    private void RebuildColorDots()
    {
        if (_colorDots is null) return;
        _colorDots.Children.Clear();
        for (var i = 0; i < NoteColors.Length; i++)
        {
            var index = i;
            var dot = ColorDot(index, 16, _colorIndex == index);
            dot.Margin = new Thickness(4, 0, 4, 0);
            dot.MouseLeftButtonUp += (_, _) =>
            {
                _colorIndex = index;
                if (_doneText is not null) _doneText.Foreground = Frozen(NoteColors[index]);
                RebuildColorDots();
                ScheduleAutoSave();
            };
            _colorDots.Children.Add(dot);
        }
    }

    private void RebuildEditorImage()
    {
        if (_imageHost is null || _contentBox is null) return;
        _imageHost.Children.Clear();
        _contentBox.Padding = new Thickness(8, 8, _editorImage is null ? 8 : 75, 22);
        if (_editorImage is null) return;

        _imageHost.Children.Add(new Border
        {
            Width = 55, Height = 55, CornerRadius = new CornerRadius(10),
            BorderBrush = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            Background = new ImageBrush(_editorImage) { Stretch = Stretch.UniformToFill },
        });
        var remove = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
            Background = Frozen(Color.FromArgb(0x99, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -5, -5, 0), Cursor = Cursors.Hand,
            Child = new TextBlock { Text = "", FontFamily = Mdl2, FontSize = 8, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        remove.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _editorImage = null;
            _pendingImage = null;
            _hasStoredImage = false;
            RebuildEditorImage();
            ScheduleAutoSave();
        };
        _imageHost.Children.Add(remove);
    }

    private void SetEditorImage(BitmapSource image, bool rebuild = true)
    {
        _editorImage = image;
        _pendingImage = EncodePng(image);
        _hasStoredImage = false;
        if (rebuild) { RebuildEditorImage(); ScheduleAutoSave(); }
    }

    private void UpdateCharCount()
    {
        if (_charCount is not null && _contentBox is not null)
            _charCount.Text = $"{_contentBox.Text.Length} chars";
    }

    private void ScheduleAutoSave()
    {
        if (_editingId is null) return;
        _autoSave.Stop();
        _autoSave.Start();
    }

    private void Persist()
    {
        if (_editingId is not { } id || _titleBox is null || _contentBox is null) return;
        var saved = _service.Save(id, _titleBox.Text, _contentBox.Text, _colorIndex, _pendingImage,
            keepImage: _hasStoredImage);
        if (saved is null) return;
        _isNew = false;
        if (_pendingImage is not null) { _pendingImage = null; _hasStoredImage = true; }
    }

    // ==================================================================
    // Clipboard
    // ==================================================================

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _editorView is not null)
        {
            CloseEditor();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

        if (_editorView is null)
        {
            // ⌘V on the list → new note from the clipboard.
            if (e.OriginalSource is TextBox) return; // pasting into search
            CreateFromClipboard();
            e.Handled = true;
        }
        else if (ClipboardImage() is { } img)
        {
            // In the editor an image attaches to the note; text pastes normally.
            SetEditorImage(img);
            e.Handled = true;
        }
    }

    /// <summary>The original's <c>handlePaste</c> / <c>createNoteFromClipboard</c>:
    /// an image file, then image data, then text.</summary>
    private void CreateFromClipboard()
    {
        try
        {
            if (ClipboardImage() is { } img)
            {
                OpenEditor(null, image: img);
                return;
            }
            if (Clipboard.ContainsText())
                OpenEditor(null, content: Clipboard.GetText());
        }
        catch { }
    }

    private static BitmapSource? ClipboardImage()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var file = Clipboard.GetFileDropList().Cast<string>()
                    .FirstOrDefault(f => ImageExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()));
                if (file is not null && LoadBitmap(file, 0) is { } fromFile) return fromFile;
            }
            if (Clipboard.ContainsImage()) return Clipboard.GetImage();
        }
        catch { }
        return null;
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    private double ActualWidthOr(double fallback) => ActualWidth > 0 ? ActualWidth : fallback;

    private static void SlideX(UIElement element, double from, double to, Action? done)
    {
        var tt = new TranslateTransform(from, 0);
        element.RenderTransform = tt;
        var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(0.35))
        {
            EasingFunction = new SpringEasing { DampingFraction = 0.8 },
        };
        if (done is not null) anim.Completed += (_, _) => done();
        tt.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private static void FadeIn(UIElement element) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));

    private static bool IsInside(DependencyObject child, DependencyObject ancestor)
    {
        for (var d = child; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, ancestor)) return true;
        return false;
    }

    private static FrameworkElement ColorDot(int index, double size, bool selected)
    {
        var grid = new Grid { Width = size + 6, Height = size + 6, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        grid.Children.Add(new Ellipse { Width = size, Height = size, Fill = Frozen(NoteColors[index]) });
        if (selected)
            grid.Children.Add(new Ellipse { Width = size + 5, Height = size + 5, Stroke = Brushes.White, StrokeThickness = 2 });
        return grid;
    }

    private static TextBox PlainTextBox(double size, FontWeight weight) => new()
    {
        FontFamily = Ui,
        FontSize = size,
        FontWeight = weight,
        Foreground = Brushes.White,
        CaretBrush = Brushes.White,
        SelectionBrush = Frozen(Color.FromRgb(0x0A, 0x84, 0xFF)),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        VerticalContentAlignment = VerticalAlignment.Center,
        FocusVisualStyle = null,
    };

    private static TextBlock Placeholder(string text, double size, TextBox box, int column, bool bold = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = Ui,
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
            Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        Grid.SetColumn(tb, column);
        box.TextChanged += (_, _) => tb.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        return tb;
    }

    private static Button CircleButton(string glyph, Brush fg, string tip, Action onClick, bool bold = false)
    {
        var b = new Button
        {
            Content = new TextBlock
            {
                Text = glyph, FontFamily = Mdl2, FontSize = 12.5, Foreground = fg,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
            Width = 26, Height = 26, ToolTip = tip, Cursor = Cursors.Hand,
            Margin = new Thickness(4, 0, 0, 0),
            Template = CircleTemplate(Faint, Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF))),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static Button PillButton(string text, Brush fg, Brush bg, Action onClick)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = fg, Margin = new Thickness(14, 4, 14, 4) },
            Margin = new Thickness(4, 0, 4, 0), Cursor = Cursors.Hand,
            Template = CircleTemplate(bg, bg),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static ControlTemplate CircleTemplate(Brush normal, Brush hover)
    {
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "bd" };
        border.SetValue(Border.BackgroundProperty, normal);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(100));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var trigger = new Trigger { Property = IsMouseOverProperty, Value = true };
        trigger.Setters.Add(new Setter(Border.BackgroundProperty, hover, "bd"));
        template.Triggers.Add(trigger);
        return template;
    }

    private static BitmapSource? LoadBitmap(string path, int decodeWidth)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
            bmp.UriSource = new Uri(path);
            if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

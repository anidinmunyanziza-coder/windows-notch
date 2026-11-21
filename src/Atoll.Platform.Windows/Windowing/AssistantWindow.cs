using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Atoll.Core.Contracts;
using Atoll.Platform.Windows.Interop;
using Rect = System.Windows.Rect;
using Key = System.Windows.Input.Key;
using ModifierKeys = System.Windows.Input.ModifierKeys;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// The notch's AI assistant. One black shape, driven every frame by springs,
/// plays the whole interaction:
///   • a droplet buds off the notch edge (a liquid neck stretches and snaps),
///     falls and settles into a circle;
///   • the circle stretches into a text pill — type, press Enter;
///   • the pill pulls back into a living blob that wobbles with a soft,
///     slowly turning aura while the answer is on its way;
///   • the blob grows into a card and the answer streams in, with a field for a
///     follow-up underneath;
///   • Esc (or clicking elsewhere) shrinks it back and it drips up into the
///     notch.
/// Without a stored key the pill first asks for the OpenRouter API key; typing
/// /key changes it later and /clear starts a new conversation.
/// </summary>
public sealed class AssistantWindow : Window
{
    private const double BoxW = 720;
    private const double PillW = 380, PillH = 44;
    private const double CardW = 500, CardMaxH = 420, CardR = 24;
    private const double DropD = 38, BlobD = 30;
    /// <summary>Where the pill comes to rest: this far down the screen.</summary>
    private const double RestFraction = 0.62;
    private const double FallSeconds = 0.72, ReturnSeconds = 0.48;
    private double _boxH = 900;

    private enum Phase { Hidden, Dropping, Input, Thinking, Answer, Leaving }

    private readonly IAssistantService _ai;
    private readonly List<ChatTurn> _history = new();
    private Phase _phase = Phase.Hidden;
    private bool _leavingInward;
    private bool _keyMode;
    private CancellationTokenSource? _cts;
    private IntPtr _hwnd;

    // geometry (window-local DIPs)
    private Point _anchor;           // notch edge point the drop buds from
    private Vector _dir;             // away from the notch
    private Point _pillCentre;
    private double _cardCx, _cardTop;
    private NotchAnchor? _notch;

    // springs
    private double _cx, _cy, _w, _h, _r, _k, _g;
    private double _vcx, _vcy, _vw, _vh, _vr, _vk, _vg;
    private double _cardH = 120;
    private TimeSpan _lastFrame;
    private double _time;
    private bool _rendering;

    // visuals
    private readonly Canvas _root = new();
    private readonly Path _shape = new() { Fill = Brushes.Black, Stroke = Frozen(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1 };
    private readonly Ellipse _glow = new();
    private readonly RotateTransform _glowSpin = new(0, 0.5, 0.5);
    private readonly Grid _pillContent = new() { Opacity = 0 };
    private readonly Grid _cardContent = new() { Opacity = 0 };
    private readonly TextBox _input;
    private readonly PasswordBox _keyBox;
    private readonly TextBlock _placeholder;
    private readonly TextBlock _question;
    private readonly TextBlock _answer;
    private readonly ScrollViewer _answerScroll;
    private readonly TextBox _followUp;
    private readonly TextBlock _followPlaceholder;
    private readonly FrameworkElement _footer;
    private string _answerRaw = "";

    private static readonly FontFamily Ui = new("SF Pro Text, Segoe UI Variable Text, Segoe UI");

    public AssistantWindow(IAssistantService ai)
    {
        _ai = ai;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Width = BoxW;
        Height = _boxH;
        Title = "Atoll Assistant";
        FontFamily = Ui;

        // ── aura + shape ────────────────────────────────────────────────
        _glow.Fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            RelativeTransform = _glowSpin,
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x5A, 0xC8, 0xFA), 0),
                new GradientStop(Color.FromRgb(0xAF, 0x52, 0xDE), 0.3),
                new GradientStop(Color.FromRgb(0xFF, 0x2D, 0x55), 0.55),
                new GradientStop(Color.FromRgb(0xFF, 0x9F, 0x0A), 0.8),
                new GradientStop(Color.FromRgb(0x5A, 0xC8, 0xFA), 1),
            },
        };
        _glow.Effect = new BlurEffect { Radius = 13 };
        _glow.Opacity = 0;
        _glow.IsHitTestVisible = false;
        _shape.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 26, ShadowDepth = 4, Opacity = 0.5 };

        // ── pill content: sparkle · text box ────────────────────────────
        var sparkle = new Path
        {
            Data = Geometry.Parse("M 8,0 C 8.6,4.6 11.4,7.4 16,8 C 11.4,8.6 8.6,11.4 8,16 C 7.4,11.4 4.6,8.6 0,8 C 4.6,7.4 7.4,4.6 8,0 Z"),
            Width = 13, Height = 13, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            Fill = new LinearGradientBrush(Color.FromRgb(0x5A, 0xC8, 0xFA), Color.FromRgb(0xFF, 0x2D, 0x55), 45),
        };
        _input = PlainBox(14);
        _input.Margin = new Thickness(38, 0, 18, 0);
        _keyBox = new PasswordBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White,
            CaretBrush = Brushes.White, FontSize = 14, FontFamily = Ui, PasswordChar = '•',
            VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(38, 0, 18, 0),
            Visibility = Visibility.Collapsed, FocusVisualStyle = null,
        };
        _placeholder = new TextBlock
        {
            Text = "Ask anything…", FontSize = 14, FontFamily = Ui,
            Foreground = Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 18, 0),
            IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _input.TextChanged += (_, _) => UpdatePlaceholder();
        _keyBox.PasswordChanged += (_, _) => UpdatePlaceholder();
        _pillContent.Children.Add(sparkle);
        _pillContent.Children.Add(_input);
        _pillContent.Children.Add(_keyBox);
        _pillContent.Children.Add(_placeholder);

        // ── card content: question · answer · follow-up ─────────────────
        _question = new TextBlock
        {
            FontSize = 12.5, FontWeight = FontWeights.Medium, FontFamily = Ui,
            Foreground = Frozen(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(22, 16, 22, 8),
        };
        _answer = new TextBlock
        {
            FontSize = 14.5, FontFamily = Ui, Foreground = Frozen(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap, LineHeight = 21,
        };
        _answerScroll = new ScrollViewer
        {
            Content = _answer, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Margin = new Thickness(22, 0, 22, 0), PanningMode = PanningMode.VerticalOnly,
        };
        _followUp = PlainBox(14);
        _followPlaceholder = new TextBlock
        {
            Text = "Ask a follow-up…", FontSize = 14, FontFamily = Ui, IsHitTestVisible = false,
            Foreground = Frozen(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
        };
        _followUp.TextChanged += (_, _) =>
            _followPlaceholder.Visibility = _followUp.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var hint = new TextBlock
        {
            Text = "esc", FontSize = 11, FontFamily = Ui, Foreground = Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        var footerGrid = new Grid { Children = { _followUp, _followPlaceholder, hint } };
        _footer = new Border
        {
            BorderBrush = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(22, 0, 22, 0), Height = 46, Child = footerGrid, Opacity = 0,
        };
        _cardContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _cardContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _cardContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_answerScroll, 1);
        Grid.SetRow(_footer, 2);
        _cardContent.Children.Add(_question);
        _cardContent.Children.Add(_answerScroll);
        _cardContent.Children.Add(_footer);

        _root.Children.Add(_glow);
        _root.Children.Add(_shape);
        _root.Children.Add(_pillContent);
        _root.Children.Add(_cardContent);
        Content = _root;

        PreviewKeyDown += OnPreviewKey;
        Deactivated += (_, _) => { if (_phase is not (Phase.Hidden or Phase.Leaving)) Dismiss(); };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            var ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TOOLWINDOW);
        };
    }

    public bool IsShowing => _phase is not (Phase.Hidden or Phase.Leaving);

    // ==================================================================
    // Open / close
    // ==================================================================

    /// <summary>Opens from the given notch, or dismisses if already open.</summary>
    public void Toggle(NotchAnchor? notch)
    {
        if (IsShowing) { Dismiss(); return; }
        if (notch is null) return;
        Open(notch);
    }

    private void Open(NotchAnchor notch)
    {
        _notch = notch;
        _cts?.Cancel();
        Layout(notch);

        _keyMode = !_ai.HasApiKey;
        ResetContent();

        // Start as a tiny bead overlapping the notch edge.
        _cx = _anchor.X - _dir.X * 2; _cy = _anchor.Y - _dir.Y * 2;
        _w = _h = 10; _r = 5; _k = 0; _g = 0;
        _vcx = _vcy = _vw = _vh = _vr = _vk = _vg = 0;
        _phase = Phase.Dropping;
        _leavingInward = false;
        _flightT = 0;
        _drawnSettled = false;

        Left = _winLeft;
        Top = _winTop;
        _shape.Data = null;
        Show();
        PlaceWindow();
        Activate();
        StartRendering();
    }

    /// <summary>Shrinks back to a drop and returns it to the notch.</summary>
    public void Dismiss()
    {
        if (_phase is Phase.Hidden or Phase.Leaving) return;
        _cts?.Cancel();
        _phase = Phase.Leaving;
        _leavingInward = false;
        Fade(_pillContent, 0, 120);
        Fade(_cardContent, 0, 120);
    }

    private void Layout(NotchAnchor n)
    {
        var sx = n.ScaleX; var sy = n.ScaleY;
        var notch = new Rect(n.Notch.X / sx, n.Notch.Y / sy, n.Notch.Width / sx, n.Notch.Height / sy);
        var work = new Rect(n.WorkArea.X / sx, n.WorkArea.Y / sy, n.WorkArea.Width / sx, n.WorkArea.Height / sy);

        // A full-height column: room for the drop to fall into the lower half.
        _boxH = work.Height;
        _workH = work.Height;
        var winTop = work.Top;
        var restY = work.Height * RestFraction;
        double winLeft;
        switch (n.Dock)
        {
            case NotchDock.Left:
                winLeft = work.Left;
                _anchor = new Point(notch.Right - winLeft, notch.Top + notch.Height / 2 - winTop);
                _dir = new Vector(1, 0);
                _pillCentre = new Point(_anchor.X + 40 + PillW / 2, restY);
                _cardCx = _pillCentre.X - PillW / 2 + CardW / 2;
                break;
            case NotchDock.Right:
                winLeft = work.Right - BoxW;
                _anchor = new Point(notch.Left - winLeft, notch.Top + notch.Height / 2 - winTop);
                _dir = new Vector(-1, 0);
                _pillCentre = new Point(_anchor.X - 40 - PillW / 2, restY);
                _cardCx = _pillCentre.X + PillW / 2 - CardW / 2;
                break;
            default:
                winLeft = Math.Clamp(notch.Left + notch.Width / 2 - BoxW / 2, work.Left, work.Right - BoxW);
                _anchor = new Point(notch.Left + notch.Width / 2 - winLeft, notch.Bottom - winTop);
                _dir = new Vector(0, 1);
                _pillCentre = new Point(_anchor.X, restY);
                _cardCx = _pillCentre.X;
                break;
        }
        _cardTop = _pillCentre.Y - PillH / 2;

        // The drop's flight path: straight down from a top notch; from a side
        // notch it leaves the edge sideways and curves down (or up) to rest.
        var start = _anchor + _dir * 4;
        _pathStart = start;
        _pathCtrl = n.Dock == NotchDock.Top
            ? new Point((start.X + _pillCentre.X) / 2, (start.Y + _pillCentre.Y) / 2)
            : new Point(_pillCentre.X, start.Y);
        _winLeft = winLeft;
        _winTop = winTop;
        Height = _boxH;
    }

    private Point _pathStart, _pathCtrl;
    private double _workH;
    private double _flightT;

    private Point PathAt(double s)
    {
        var u = 1 - s;
        return new Point(
            u * u * _pathStart.X + 2 * u * s * _pathCtrl.X + s * s * _pillCentre.X,
            u * u * _pathStart.Y + 2 * u * s * _pathCtrl.Y + s * s * _pillCentre.Y);
    }

    private double _winLeft, _winTop;

    private void PlaceWindow()
    {
        if (_hwnd == IntPtr.Zero || _notch is null) return;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            (int)Math.Round(_winLeft * _notch.ScaleX), (int)Math.Round(_winTop * _notch.ScaleY),
            (int)Math.Round(BoxW * _notch.ScaleX), (int)Math.Round(_boxH * _notch.ScaleY), 0);
    }

    private void ResetContent()
    {
        _input.Text = "";
        _keyBox.Password = "";
        _followUp.Text = "";
        _pillContent.Opacity = 0;
        _cardContent.Opacity = 0;
        _footer.Opacity = 0;
        SetKeyMode(_keyMode, _keyMode ? "Paste your OpenRouter API key, then Enter" : null);
    }

    private void SetKeyMode(bool on, string? prompt)
    {
        _keyMode = on;
        _keyBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _input.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        _placeholder.Text = prompt ?? (on ? "Paste your OpenRouter API key" : "Ask anything…");
        UpdatePlaceholder();
    }

    private void UpdatePlaceholder() =>
        _placeholder.Visibility = (_keyMode ? _keyBox.Password.Length : _input.Text.Length) == 0
            ? Visibility.Visible : Visibility.Collapsed;

    // ==================================================================
    // Keys / submit
    // ==================================================================

    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; return; }
        if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
        e.Handled = true;

        if (_phase == Phase.Input)
        {
            if (_keyMode)
            {
                var key = _keyBox.Password.Trim();
                if (key.Length == 0) return;
                _ai.SetApiKey(key);
                _keyBox.Password = "";
                SetKeyMode(false, "Key saved. Ask anything…");
                Dispatcher.BeginInvoke(() => _input.Focus());
                return;
            }
            Submit(_input.Text);
        }
        else if (_phase == Phase.Answer && _followUp.IsKeyboardFocused)
        {
            Submit(_followUp.Text);
        }
    }

    private async void Submit(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;

        if (text.Equals("/key", StringComparison.OrdinalIgnoreCase))
        {
            _input.Text = "";
            SetKeyMode(true, "Paste a new OpenRouter API key");
            Dispatcher.BeginInvoke(() => _keyBox.Focus());
            return;
        }
        if (text.Equals("/clear", StringComparison.OrdinalIgnoreCase))
        {
            _history.Clear();
            _input.Text = "";
            _placeholder.Text = "New conversation. Ask anything…";
            return;
        }

        _history.Add(new ChatTurn("user", text));
        _question.Text = text;
        _answerRaw = "";
        RenderAnswer();
        _input.Text = "";
        _followUp.Text = "";
        _phase = Phase.Thinking;
        Fade(_pillContent, 0, 140);
        Fade(_cardContent, 0, 140);
        _footer.Opacity = 0;

        var cts = _cts = new CancellationTokenSource();
        var reply = new System.Text.StringBuilder();
        try
        {
            await foreach (var chunk in _ai.StreamReplyAsync(_history, cts.Token))
            {
                if (cts.IsCancellationRequested) return;
                if (reply.Length == 0) ShowCard();
                reply.Append(chunk);
                _answerRaw = reply.ToString();
                RenderAnswer();
            }
            if (cts.IsCancellationRequested) return;
            if (reply.Length == 0) { ShowCard(); _answerRaw = "(No answer.)"; RenderAnswer(); }
            _history.Add(new ChatTurn("assistant", reply.ToString()));
            Fade(_footer, 1, 220);
            _followUp.Focus();
        }
        catch (OperationCanceledException) { }
        catch (AssistantException ex) when (ex.IsAuthError)
        {
            if (cts.IsCancellationRequested) return;
            _history.RemoveAt(_history.Count - 1);
            _phase = Phase.Input;
            SetKeyMode(true, ex.Message);
            Dispatcher.BeginInvoke(() => _keyBox.Focus());
        }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested) return;
            _history.RemoveAt(_history.Count - 1);
            ShowCard();
            _answerRaw = "⚠ " + ex.Message;
            RenderAnswer();
            Fade(_footer, 1, 220);
        }
    }

    private void ShowCard()
    {
        _phase = Phase.Answer;
        _answerScroll.ScrollToTop();
    }

    // ==================================================================
    // Light Markdown → inlines
    // ==================================================================

    private static readonly Regex InlineMarks = new(@"(\*\*[^*]+\*\*|`[^`]+`)", RegexOptions.Compiled);

    private void RenderAnswer()
    {
        _answer.Inlines.Clear();
        var lines = _answerRaw.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i > 0) _answer.Inlines.Add(new LineBreak());
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#"))
            {
                _answer.Inlines.Add(new Run(trimmed.TrimStart('#').Trim()) { FontWeight = FontWeights.SemiBold });
                continue;
            }
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                _answer.Inlines.Add(new Run("  •  ") { Foreground = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)) });
                line = trimmed[2..];
            }
            foreach (var part in InlineMarks.Split(line))
            {
                if (part.Length == 0) continue;
                if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                    _answer.Inlines.Add(new Run(part[2..^2]) { FontWeight = FontWeights.SemiBold });
                else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
                    _answer.Inlines.Add(new Run(part[1..^1])
                    {
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13,
                        Background = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                    });
                else
                    _answer.Inlines.Add(new Run(part));
            }
        }

        // Card grows to fit (up to a max, then the answer scrolls).
        _answer.Measure(new Size(CardW - 44, double.PositiveInfinity));
        var questionH = 16 + 8 + 18;
        _cardH = Math.Clamp(questionH + _answer.DesiredSize.Height + 14 + 46, 120, CardMaxH);
        if (_answer.DesiredSize.Height > _cardH - questionH - 60) _answerScroll.ScrollToEnd();
    }

    // ==================================================================
    // Frame loop
    // ==================================================================

    private void StartRendering()
    {
        if (_rendering) return;
        _rendering = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        _rendering = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _lastFrame).TotalSeconds, 0, 1 / 20.0);
        _lastFrame = now;
        if (dt <= 0) return;
        _time += dt;

        // ── targets for the phase ───────────────────────────────────────
        double tx = _pillCentre.X, ty = _pillCentre.Y, tw, th, tr, tk = 0, tg = 0;
        double resp = 0.42, damp = 0.8;
        switch (_phase)
        {
            case Phase.Dropping:
                if (Fly(dt, forward: true)) return;
                // Landed: squash on impact, then stretch into the pill.
                _phase = Phase.Input;
                _vw += 420; _vh -= 260;
                tw = PillW; th = PillH; tr = PillH / 2;
                break;
            case Phase.Input:
                tw = PillW; th = PillH; tr = PillH / 2; resp = 0.45; damp = 0.74;
                if (_w > PillW * 0.72 && _pillContent.Opacity == 0 && !IsFading(_pillContent))
                {
                    Fade(_pillContent, 1, 200);
                    Dispatcher.BeginInvoke(() => { if (_keyMode) _keyBox.Focus(); else _input.Focus(); });
                }
                break;
            case Phase.Thinking:
                tw = th = BlobD; tr = BlobD / 2; tk = 1; tg = 1; resp = 0.5; damp = 0.7;
                break;
            case Phase.Answer:
                tw = CardW; th = _cardH; tr = CardR; tg = 0;
                var top = Math.Min(_cardTop, _workH - 24 - _cardH);
                tx = _cardCx; ty = top + _cardH / 2; resp = 0.5; damp = 0.8;
                if (_w > CardW * 0.7 && _cardContent.Opacity == 0 && !IsFading(_cardContent))
                    Fade(_cardContent, 1, 220);
                break;
            case Phase.Leaving:
                if (!_leavingInward)
                {
                    // Gather into a drop where it is, then fly home.
                    tx = _cx; ty = _cy;
                    tw = th = DropD * 0.85; tr = tw / 2; resp = 0.3; damp = 0.9;
                    if (_w < DropD * 1.3 && _h < DropD * 1.3)
                    {
                        _leavingInward = true;
                        _flightT = 0;
                        _returnFrom = new Point(_cx, _cy);
                    }
                }
                else
                {
                    if (Fly(dt, forward: false)) return;
                    HideNow();
                    return;
                }
                break;
            default:
                return;
        }

        Spring(ref _cx, ref _vcx, tx, resp, damp, dt);
        Spring(ref _cy, ref _vcy, ty, resp, damp, dt);
        Spring(ref _w, ref _vw, tw, resp, damp, dt);
        Spring(ref _h, ref _vh, th, resp, damp, dt);
        Spring(ref _r, ref _vr, tr, resp, damp, dt);
        Spring(ref _k, ref _vk, tk, 0.6, 0.9, dt);
        Spring(ref _g, ref _vg, tg, 0.7, 0.95, dt);

        var moving = Math.Abs(_vcx) + Math.Abs(_vcy) + Math.Abs(_vw) + Math.Abs(_vh) + Math.Abs(_vr) > 0.3
                     || Math.Abs(_vk) + Math.Abs(_vg) > 0.003 || _k > 0.01 || _g > 0.01;
        if (!moving && _drawnSettled) return;
        _drawnSettled = !moving;
        Draw();
    }

    private bool _drawnSettled;
    private Point _returnFrom;

    /// <summary>Moves the drop along its curved path for one frame. Falling,
    /// it accelerates like a drop and stretches along its motion; going home it
    /// is drawn back into the notch. Returns false when the flight is over.</summary>
    private bool Fly(double dt, bool forward)
    {
        _flightT += dt / (forward ? FallSeconds : ReturnSeconds);
        if (_flightT >= 1) return false;

        var prev = new Point(_cx, _cy);
        double s, size;
        if (forward)
        {
            s = Math.Pow(_flightT, 1.75);                               // gravity: slow start, fast fall
            size = 10 + (DropD - 10) * SmoothStep(0, 0.3, _flightT);   // swells as it buds off
            var p = PathAt(s);
            _cx = p.X; _cy = p.Y;
        }
        else
        {
            s = 1 - Math.Pow(_flightT, 1.6);                           // drawn home, quickening
            var home = PathAt(s);
            var blend = SmoothStep(0, 0.35, _flightT);                   // ease off wherever it gathered
            _cx = _returnFrom.X + (home.X - _returnFrom.X) * blend;
            _cy = _returnFrom.Y + (home.Y - _returnFrom.Y) * blend;
            size = DropD * 0.85 * (1 - SmoothStep(0.55, 1, _flightT)) + 6 * SmoothStep(0.55, 1, _flightT);
        }

        // Teardrop stretch along the direction of travel.
        var v = new Vector(_cx - prev.X, _cy - prev.Y) / Math.Max(dt, 1e-3);
        var stretch = Math.Min(0.38, v.Length / 2600);
        var vertical = Math.Abs(v.Y) >= Math.Abs(v.X);
        _w = size * (vertical ? 1 - stretch * 0.45 : 1 + stretch);
        _h = size * (vertical ? 1 + stretch : 1 - stretch * 0.45);
        _r = Math.Min(_w, _h) / 2;
        _vcx = _vcy = _vw = _vh = _vr = 0;
        Draw();
        return true;
    }

    private static double SmoothStep(double e0, double e1, double x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private bool Near(double x, double y, double tol) => Math.Abs(_cx - x) < tol && Math.Abs(_cy - y) < tol;

    private void Draw()
    {
        var w = Math.Max(2, _w);
        var h = Math.Max(2, _h);
        var rect = new Rect(_cx - w / 2, _cy - h / 2, w, h);
        var r = Math.Clamp(_r, 0, Math.Min(w, h) / 2);

        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(_k > 0.02 ? Blob(rect, _k, _time) : new RectangleGeometry(rect, r, r));
        if (Neck() is { } neck) group.Children.Add(neck);
        _shape.Data = group;

        // aura around the thinking blob
        var gd = Math.Max(w, h) + 16;
        var pulse = 1 + 0.06 * Math.Sin(_time * 2.4);
        _glow.Width = _glow.Height = gd * pulse;
        Canvas.SetLeft(_glow, _cx - gd * pulse / 2);
        Canvas.SetTop(_glow, _cy - gd * pulse / 2);
        _glow.Opacity = Math.Clamp(_g, 0, 1) * 0.9;
        _glowSpin.Angle = _time * 70 % 360;

        // content follows the shape, clipped to it
        foreach (var c in new FrameworkElement[] { _pillContent, _cardContent })
        {
            Canvas.SetLeft(c, rect.Left);
            Canvas.SetTop(c, rect.Top);
            c.Width = rect.Width;
            c.Height = rect.Height;
            c.Clip = new RectangleGeometry(new Rect(0, 0, rect.Width, rect.Height), r, r);
        }
        _pillContent.Visibility = _phase is Phase.Input or Phase.Dropping or Phase.Leaving ? Visibility.Visible : Visibility.Collapsed;
        _cardContent.Visibility = _phase is Phase.Answer or Phase.Leaving or Phase.Thinking ? Visibility.Visible : Visibility.Collapsed;
        _shapeRect = rect;
    }

    private Rect _shapeRect;

    /// <summary>A living blob: a circle whose radius ripples with three slow,
    /// out-of-phase waves (amplitude scaled by <paramref name="k"/>).</summary>
    private static Geometry Blob(Rect rect, double k, double t)
    {
        const int n = 72;
        var c = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        var rx = rect.Width / 2; var ry = rect.Height / 2;
        var pts = new Point[n];
        for (var i = 0; i < n; i++)
        {
            var a = i * 2 * Math.PI / n;
            var f = 1 + k * (0.075 * Math.Sin(3 * a + 1.9 * t) + 0.05 * Math.Sin(5 * a - 1.4 * t) + 0.035 * Math.Sin(2 * a + 2.8 * t));
            pts[i] = new Point(c.X + rx * f * Math.Cos(a), c.Y + ry * f * Math.Sin(a));
        }
        var fig = new PathFigure { StartPoint = pts[0], IsClosed = true };
        // Smooth with midpoint quadratics so the outline has no corners.
        for (var i = 0; i < n; i++)
        {
            var p1 = pts[(i + 1) % n];
            var p2 = pts[(i + 2) % n];
            fig.Segments.Add(new QuadraticBezierSegment(p1, new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2), true));
        }
        fig.StartPoint = new Point((pts[0].X + pts[1].X) / 2, (pts[0].Y + pts[1].Y) / 2);
        return new PathGeometry(new[] { fig });
    }

    /// <summary>The liquid neck joining a drop to the notch edge while it buds off
    /// or drips back (only near the edge).</summary>
    private Geometry? Neck()
    {
        if (_phase is not (Phase.Dropping or Phase.Leaving)) return null;
        const double neckLen = 24;
        var extentAlong = Math.Abs(_dir.X) > 0 ? _w : _h;
        var rel = new Vector(_cx - _anchor.X, _cy - _anchor.Y);
        var along = rel.X * _dir.X + rel.Y * _dir.Y;
        var gap = along - extentAlong / 2;
        if (gap >= neckLen) return null;

        var perp = new Vector(-_dir.Y, _dir.X);
        var size = Math.Min(_w, _h);
        var a = Math.Min(9, size * 0.42);
        var a2 = size * 0.4;
        var waist = Math.Min(a, a2) * Math.Pow(Math.Clamp(1 - Math.Max(gap, 0) / neckLen, 0, 1), 1.3);
        if (waist < 0.8) return null;
        var c = (waist - 0.125 * (a + a2)) / 0.75;

        var u0 = -4.0;
        var u1 = along - extentAlong / 2 + size * 0.5;
        if (u1 <= u0) return null;
        var span = u1 - u0;
        Point P(double u, double v) => _anchor + _dir * u + perp * v;

        var fig = new PathFigure { StartPoint = P(u0, -a), IsClosed = true };
        fig.Segments.Add(new BezierSegment(P(u0 + span * 0.5, -c), P(u1 - span * 0.5, -c), P(u1, -a2), true));
        fig.Segments.Add(new LineSegment(P(u1, a2), true));
        fig.Segments.Add(new BezierSegment(P(u1 - span * 0.5, c), P(u0 + span * 0.5, c), P(u0, a), true));
        return new PathGeometry(new[] { fig });
    }

    private void HideNow()
    {
        StopRendering();
        _phase = Phase.Hidden;
        _shape.Data = null;
        _glow.Opacity = 0;
        Hide();
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    /// <summary>Click-through outside the shape; clicking elsewhere also
    /// deactivates the window, which dismisses it.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_NCHITTEST || _notch is null) return IntPtr.Zero;
        var lp = lParam.ToInt64();
        var x = (short)(lp & 0xFFFF) / _notch.ScaleX - _winLeft;
        var y = (short)((lp >> 16) & 0xFFFF) / _notch.ScaleY - _winTop;
        handled = true;
        return _shapeRect.Contains(new Point(x, y))
            ? new IntPtr(NativeMethods.HTCLIENT)
            : new IntPtr(NativeMethods.HTTRANSPARENT);
    }

    private static void Spring(ref double x, ref double v, double target, double response, double damping, double dt)
    {
        var k = Math.Pow(2 * Math.PI / response, 2);
        var c = 4 * Math.PI * damping / response;
        var steps = Math.Max(1, (int)Math.Ceiling(dt / (1 / 240.0)));
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            v += (-k * (x - target) - c * v) * h;
            x += v * h;
        }
    }

    private readonly HashSet<UIElement> _fading = new();

    private bool IsFading(UIElement el) => _fading.Contains(el);

    private void Fade(UIElement el, double to, int ms)
    {
        _fading.Add(el);
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        a.Completed += (_, _) =>
        {
            _fading.Remove(el);
            el.BeginAnimation(OpacityProperty, null);
            el.Opacity = to;
        };
        el.BeginAnimation(OpacityProperty, a);
    }

    private static TextBox PlainBox(double size) => new()
    {
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Foreground = Brushes.White,
        CaretBrush = Brushes.White,
        SelectionBrush = Frozen(Color.FromRgb(0x0A, 0x84, 0xFF)),
        FontSize = size,
        FontFamily = Ui,
        VerticalContentAlignment = VerticalAlignment.Center,
        FocusVisualStyle = null,
    };

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

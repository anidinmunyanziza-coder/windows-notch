using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Atoll.Core.Contracts;
using Atoll.Core.Design;
using Atoll.Core.Models;
using Atoll.Core.ViewModels;
using Atoll.Platform.Windows.Animation;
using Atoll.Platform.Windows.Interop;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// A single floating, transparent, topmost notch surface on one display.
/// Translates macOS NSWindow (floating panel, top edge positioning,
/// transparency, shadow) to a WPF window layered over Win32.
///
/// Interaction model (from ContentView.swift):
///   - Hover for minimumHoverDuration (0.3s default from original) â†’ open
///   - Click â†’ toggle open/closed
///   - Hover leave â†’ auto-close
///   - Content switches per current tab (ShellViewModel.CurrentView)
///
/// Open/close uses the original spring curves:
///   open  = .spring(response: 0.42, dampingFraction: 1.0)
///   close = .spring(response: 0.45, dampingFraction: 1.0)
/// Corner radii follow cornerRadiusInsets (6/14 closed, 19/24 open) and are
/// driven by the same analytic spring.
/// </summary>
public partial class NotchWindow : Window
{
    /// <summary>Padding (DIPs) around the open notch so the drop shadow and any
    /// spring overshoot are not clipped by the fixed window bounds.</summary>
    private const double ShadowPad = 24.0;

    /// <summary>How far (DIPs) tab content slides up as it reveals on open.</summary>
    private const double ContentRevealSlide = 10.0;

    /// <summary>How far (DIPs) content slides horizontally during a swipe switch.</summary>
    private const double TabSwitchSlide = 34.0;

    /// <summary>Direction of the last tab switch: +1 forward/right, -1 back/left,
    /// 0 for a plain open. Drives the slide direction of the next content reveal.</summary>
    private int _switchDir;

    /// <summary>Accumulated horizontal-wheel delta; one tab switch per ~120 units.</summary>
    private int _hWheelAccum;

    /// <summary>The tab content currently shown, kept so a swipe can cross-slide
    /// it out while the next tab slides in.</summary>
    private UIElement? _currentContent;

    private enum ClosedPillActivity { None, Media, Timer, Charging }

    /// <summary>What the closed pill is currently showing.</summary>
    private ClosedPillActivity _closedActivity = ClosedPillActivity.None;

    // Transient "charging" activity shown briefly on plug-in.
    private bool _wasOnAcPower;
    private DateTime _chargingShownUntil = DateTime.MinValue;
    private int _lastBatteryPct;
    private System.Windows.Threading.DispatcherTimer? _chargingRevertTimer;

    private ClosedPillActivity DetermineClosedActivity()
    {
        if (DateTime.UtcNow < _chargingShownUntil) return ClosedPillActivity.Charging;
        if (_shell is null) return ClosedPillActivity.None;
        if (!string.IsNullOrEmpty(_shell.MediaState?.Title)) return ClosedPillActivity.Media;
        if (_shell.TimerState?.IsActive == true) return ClosedPillActivity.Timer;
        return ClosedPillActivity.None;
    }

    /// <summary>Flashes the charging activity on the closed pill for a few seconds
    /// when the charger is plugged in.</summary>
    private void OnPowerStatusChanged()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var s)) return;
        var onAc = s.ACLineStatus == 1;
        _lastBatteryPct = s.BatteryLifePercent == 255 ? 0 : s.BatteryLifePercent;

        if (onAc && !_wasOnAcPower && (s.BatteryFlag & 128) == 0)
        {
            // Just plugged in on a battery-powered machine.
            _chargingShownUntil = DateTime.UtcNow.AddSeconds(4);
            if (_vm.State != NotchState.Open) UpdateContent();

            _chargingRevertTimer ??= new System.Windows.Threading.DispatcherTimer();
            _chargingRevertTimer.Interval = TimeSpan.FromSeconds(4.2);
            _chargingRevertTimer.Tick -= OnChargingRevert;
            _chargingRevertTimer.Tick += OnChargingRevert;
            _chargingRevertTimer.Start();
        }

        _wasOnAcPower = onAc;
        RefreshBattery();
    }

    private void OnChargingRevert(object? sender, EventArgs e)
    {
        _chargingRevertTimer?.Stop();
        if (_vm.State != NotchState.Open) UpdateContent();
    }

    /// <summary>Scale the closed pill springs to while hovered, for the subtle
    /// bouncy "nudge" the original gives on hover before it opens.</summary>
    private const double HoverNudgeScale = 1.06;

    private readonly ScaleTransform _hoverScale = new(1.0, 1.0);

    private IntPtr _hwnd;
    private readonly DisplayInfo _display;
    private readonly ShellViewModel _shell;
    private readonly NotchViewModel _vm;
    private readonly ILogger? _logger;
    private bool _isClosing;
    private bool _isHovering;
    private int _animationGeneration;
    private EventHandler? _cornerRenderingHandler;
    private readonly System.Windows.Threading.DispatcherTimer _hoverTimer;
    private readonly System.Windows.Threading.DispatcherTimer _closeTimer;
    private readonly System.Windows.Threading.DispatcherTimer _hoverWatch;

    public NotchWindow(NotchViewModel viewModel, ShellViewModel shell, DisplayInfo display,
        ILogger? logger = null)
    {
        _vm = viewModel;
        _shell = shell;
        _display = display;
        _logger = logger;
        DataContext = viewModel;
        InitializeComponent();

        // The window is a fixed, screen-centred container big enough to hold the
        // open notch plus shadow room; only the Surface Border grows/shrinks.
        Surface.Width = viewModel.ClosedWidth;
        Surface.Height = viewModel.ClosedHeight;
        ApplyNotchGeometry(viewModel.ClosedWidth, viewModel.ClosedHeight,
            DesignTokens.ClosedCornerRadiusTop, DesignTokens.ClosedCornerRadiusBottom);

        // Scale from the top-centre so the hover nudge grows the pill downward
        // and sideways without lifting it off the screen's top edge.
        Surface.RenderTransformOrigin = new Point(0.5, 0.0);
        Surface.RenderTransform = _hoverScale;

        var winW = viewModel.OpenWidth + ShadowPad * 2;
        var winH = viewModel.OpenHeight + ShadowPad;

        Width = winW;
        Height = winH;
        Left = DipWorkLeft + (DipWorkWidth - winW) / 2.0;
        Top = DipWorkTop;

        // Hover timer: open after minimumHoverDuration (0.3s default from original).
        _hoverTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_shell.GetSetting("minimumHoverDuration", 0.3))
        };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_isHovering && _shell.GetSetting("openNotchOnHover", true))
                OpenNotch();
        };

        // Original debounces hover exit by 100 ms and retains the notch when
        // the cursor is still inside its frame (screen top edge on macOS).
        _closeTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            if (_vm.State != NotchState.Open) return;

            // Clipboard drag-out keeps the notch open; re-check shortly so it
            // closes once the drop completes and the cursor is outside.
            if (_shell.IsClipboardDragInProgress)
            {
                _closeTimer.Start();
                return;
            }

            if (IsCursorInsideSurface())
            {
                _logger?.LogDebug("Auto-close skipped: cursor inside notch frame");
                return;
            }
            _logger?.LogInfo("Notch auto-closing after hover exit");
            CloseNotch();
        };

        // Cursor watchdog. WPF can synthesise a MouseLeave for a layered
        // window while the pointer is still inside it (observed 1.4 s after a
        // card click). Checking the real cursor keeps the notch open while it
        // is genuinely hovered and still closes it when the pointer leaves.
        _hoverWatch = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _hoverWatch.Tick += (_, _) => OnHoverWatchTick();

        // Win32 attributes: layered, tool window, topmost, no activate.
        var helper = new WindowInteropHelper(this);
        SourceInitialized += (_, _) =>
        {
            _hwnd = helper.Handle;
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            ApplyWin32Styles();
            PositionOnScreen();

            // Seed the AC-power state so a status change at startup doesn't flash
            // the charging activity spuriously.
            if (NativeMethods.GetSystemPowerStatus(out var ps))
            {
                _wasOnAcPower = ps.ACLineStatus == 1;
                _lastBatteryPct = ps.BatteryLifePercent == 255 ? 0 : ps.BatteryLifePercent;
            }
        };

        // React to state / size changes from the view model.
        viewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(NotchViewModel.State):
                    AnimateStateChange();
                    break;
                case nameof(NotchViewModel.OpenWidth):
                case nameof(NotchViewModel.OpenHeight):
                case nameof(NotchViewModel.ClosedWidth):
                case nameof(NotchViewModel.ClosedHeight):
                    PositionOnScreen();
                    break;
            }
        };

        // React to tab changes from the shell.
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.CurrentView))
            {
                UpdateContent();
            }
            else if ((e.PropertyName == nameof(ShellViewModel.MediaState)
                      || e.PropertyName == nameof(ShellViewModel.TimerState))
                     && _vm.State != NotchState.Open)
            {
                // Switch the closed-pill live activity (media / timer / none) when
                // its kind changes — not on every position or countdown tick (each
                // activity's own bindings handle second-by-second updates).
                if (DetermineClosedActivity() != _closedActivity)
                    UpdateContent();
            }
        };

        UpdateContent();

        // Apply settings that affect the notch's look/feel at launch; they can
        // also be changed live (see SetShadowEnabled / SetHoverDuration).
        _shadowEffect = NotchBg.Effect;
        SetShadowEnabled(_shell.GetSetting("enableShadow", true));
    }

    // ------------------------------------------------------------------
    // Live settings
    // ------------------------------------------------------------------

    private System.Windows.Media.Effects.Effect? _shadowEffect;

    /// <summary>Shows or hides the notch drop shadow (Defaults[.enableShadow]).</summary>
    public void SetShadowEnabled(bool enabled) =>
        NotchBg.Effect = enabled ? _shadowEffect : null;

    /// <summary>Sets the hover-to-open delay (Defaults[.minimumHoverDuration]).</summary>
    public void SetHoverDuration(double seconds) =>
        _hoverTimer.Interval = TimeSpan.FromSeconds(Math.Max(0, seconds));

    /// <summary>Rebuilds the tab bar + content, e.g. after a feature toggle changes
    /// which tabs are available.</summary>
    public void RefreshContent() => UpdateContent();

    /// <summary>Resting-pill width: wider for a live activity, compact when idle.</summary>
    private double EffectiveClosedWidth => _closedActivity switch
    {
        ClosedPillActivity.Media => DesignTokens.ClosedMediaNotchWidth,
        ClosedPillActivity.Timer => 250.0,
        ClosedPillActivity.Charging => 230.0,
        _ => _vm.ClosedWidth,
    };

    /// <summary>Springs the closed pill between its idle and media widths.</summary>
    private void AnimateClosedWidth()
    {
        if (_vm.State == NotchState.Open || _hwnd == IntPtr.Zero) return;
        RunSpringAnimation(EffectiveClosedWidth, _vm.ClosedHeight, ClosedRadii,
            DesignTokens.OpenSpringResponse, DesignTokens.OpenSpringDamping);
    }

    // ------------------------------------------------------------------
    // Content
    // ------------------------------------------------------------------
    private void UpdateContent()
    {
        // Show content based on current tab when open; closed notch shows
        // a minimal pill (handled by the Surface border + no content).
        if (_vm.State != NotchState.Open || _shell == null)
        {
            // Closed pill: a compact media activity (album art + equalizer) when a
            // track is playing, otherwise a bare pill.
            _contentGrid?.Children.Clear();
            _currentContent = null;
            _switchDir = 0;

            var prev = _closedActivity;
            var arrivingFromOpen = _outerGrid is not null && ReferenceEquals(ContentHost.Child, _outerGrid);
            _closedActivity = DetermineClosedActivity();
            ContentHost.Child = _closedActivity switch
            {
                ClosedPillActivity.Media => new ClosedMediaActivity { DataContext = _shell },
                ClosedPillActivity.Timer => new ClosedTimerActivity { DataContext = _shell },
                ClosedPillActivity.Charging => new ClosedChargingActivity(_lastBatteryPct),
                _ => null,
            };

            // Blur-replace the pill's contents in when it actually changes.
            if (ContentHost.Child is UIElement activity && (arrivingFromOpen || prev != _closedActivity))
                FadeBlurIn(activity, TimeSpan.FromMilliseconds(260));

            // Expand the resting pill for a live activity, contract when it ends.
            if (prev != _closedActivity)
                AnimateClosedWidth();
            return;
        }

        UIElement content = _shell.CurrentView switch
        {
            NotchView.Home => new HomeTabContent { DataContext = _shell },
            NotchView.Calendar => new CalendarView(),
            NotchView.Shelf => new ShelfTabContent { DataContext = _shell },
            NotchView.Stats => new StatsTabContent { DataContext = _shell },
            NotchView.Timer => new TimerTabContent { DataContext = _shell },
            NotchView.Terminal => new TerminalTabContent(),
            NotchView.Clipboard => new ClipboardTabContent { DataContext = _shell },
            _ => new TextBlock
            {
                Text = _shell.CurrentView.ToString(),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14,
            },
        };

        // Persistent grid holds the content so switching tabs never reparents an
        // already-attached element (that threw and crashed the notch).
        var grid = EnsureContentGrid();
        var reveal = DesignTokens.ViewTransitionDuration;
        var ease = new SpringEasing { DampingFraction = 1.0 };
        var old = _currentContent;
        _currentContent = content;
        grid.Children.Add(content);

        if (_switchDir != 0 && old is not null && grid.Children.Contains(old))
        {
            // Carousel: outgoing tab slides out one way, incoming slides in from
            // the swipe direction.
            var dir = _switchDir;
            _switchDir = 0;
            var dist = grid.ActualWidth > 0 ? grid.ActualWidth : _vm.OpenWidth;

            // Blur-replace on the carousel too: outgoing blurs out, incoming in.
            Slide(old, 0, -dir * dist, 1, 0, reveal, ease, () => grid.Children.Remove(old),
                blurFrom: 0, blurTo: 10);
            Slide(content, dir * dist, 0, 0, 1, reveal, ease, null, blurFrom: 10, blurTo: 0);
        }
        else
        {
            // Plain open: blur + fade in with a short upward slide.
            _switchDir = 0;
            if (old is not null) grid.Children.Remove(old);
            Slide(content, 0, 0, 0, 1, reveal, ease, null, yFrom: ContentRevealSlide,
                blurFrom: 12, blurTo: 0);
        }
    }

    private Grid? _contentGrid;
    private Border? _statusHeader;
    private System.Windows.Shapes.Rectangle? _batteryFill;
    private TextBlock? _batteryText;
    private System.Windows.Threading.DispatcherTimer? _batteryTimer;

    private const double BatteryInnerWidth = 17.0;

    private Grid? _outerGrid;

    private Grid EnsureContentGrid()
    {
        if (_outerGrid is null)
        {
            _contentGrid = new Grid { ClipToBounds = true };

            _outerGrid = new Grid();
            _outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _statusHeader = BuildStatusHeader();
            Grid.SetRow(_statusHeader, 0);
            Grid.SetRow(_contentGrid, 1);
            _outerGrid.Children.Add(_statusHeader);
            _outerGrid.Children.Add(_contentGrid);

            _batteryTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30),
            };
            _batteryTimer.Tick += (_, _) => RefreshBattery();
            _batteryTimer.Start();
        }

        // Re-attach the open layout (the closed state swaps ContentHost.Child out).
        if (!ReferenceEquals(ContentHost.Child, _outerGrid))
            ContentHost.Child = _outerGrid;
        RefreshBattery();
        return _contentGrid!;
    }

    /// <summary>Thin top strip carrying the battery indicator (top-right), as in
    /// the original notch header. The battery is drawn with shapes (a body, a
    /// proportional fill and a terminal nub) so it never depends on a font glyph.</summary>
    private Border BuildStatusHeader()
    {
        _batteryText = new TextBlock
        {
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        // Battery body: outlined rounded rect holding the proportional fill.
        _batteryFill = new System.Windows.Shapes.Rectangle
        {
            RadiusX = 1.0,
            RadiusY = 1.0,
            HorizontalAlignment = HorizontalAlignment.Left,
            Height = 7,
            Width = BatteryInnerWidth,
        };
        var fillHost = new Grid { Margin = new Thickness(1.5, 0, 1.5, 0) };
        fillHost.Children.Add(_batteryFill);

        var body = new Border
        {
            Width = 22,
            Height = 11,
            CornerRadius = new CornerRadius(2.5),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9E)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = fillHost,
        };
        var nub = new System.Windows.Shapes.Rectangle
        {
            Width = 2, Height = 5, RadiusX = 1, RadiusY = 1,
            Fill = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9E)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0.5, 0, 0, 0),
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(HeaderIconButton("", "Clipboard", (_, _) => SwitchToTab(NotchView.Clipboard)));
        panel.Children.Add(HeaderIconButton("", "Pick a colour", (_, _) => PickScreenColor()));
        panel.Children.Add(HeaderIconButton("", "Settings", (_, _) => _shell!.RequestOpenSettings()));
        panel.Children.Add(new Border { Width = 8 }); // gap before battery
        panel.Children.Add(_batteryText);
        panel.Children.Add(body);
        panel.Children.Add(nub);

        return new Border
        {
            Height = 24,
            Padding = new Thickness(14, 5, 16, 0),
            Child = panel,
        };
    }

    private static Button HeaderIconButton(string glyph, string tip, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xCC)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Width = 26,
            Height = 20,
            Margin = new Thickness(1, 0, 1, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = tip,
            Template = HeaderIconTemplate(),
        };
        button.Click += onClick;
        return button;
    }

    private static ControlTemplate? _headerIconTemplate;

    private static ControlTemplate HeaderIconTemplate()
    {
        if (_headerIconTemplate is not null) return _headerIconTemplate;
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var trigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        trigger.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))));
        var ct = new ControlTemplate(typeof(Button)) { VisualTree = border };
        ct.Triggers.Add(trigger);
        ct.Seal();
        _headerIconTemplate = ct;
        return ct;
    }

    /// <summary>Opens the screen colour picker; the sampled hex is copied to the
    /// clipboard.</summary>
    private void PickScreenColor()
    {
        var overlay = new ColorPickOverlay();
        overlay.Picked += hex =>
        {
            try { System.Windows.Clipboard.SetText(hex); } catch { /* clipboard busy */ }
            _logger?.LogInfo($"Picked colour {hex} (copied to clipboard)");
        };
        overlay.Show();
    }

    private void RefreshBattery()
    {
        if (_batteryFill is null || _batteryText is null || _statusHeader is null) return;

        if (!NativeMethods.GetSystemPowerStatus(out var s)
            || (s.BatteryFlag & 128) != 0 || s.BatteryLifePercent == 255)
        {
            // No reading, or a desktop with no battery.
            _statusHeader.Visibility = Visibility.Collapsed;
            return;
        }

        _statusHeader.Visibility = Visibility.Visible;
        int pct = Math.Clamp((int)s.BatteryLifePercent, 0, 100);
        bool charging = s.ACLineStatus == 1 || (s.BatteryFlag & 8) != 0;

        _batteryFill.Width = Math.Max(1.5, BatteryInnerWidth * pct / 100.0);
        _batteryFill.Fill = new SolidColorBrush(
            charging ? Color.FromRgb(0x30, 0xD1, 0x58) :
            pct <= 20 ? Color.FromRgb(0xFF, 0x45, 0x3A) :
            Color.FromRgb(0xF2, 0xF2, 0xF2));
        _batteryText.Text = pct + "%";
    }

    /// <summary>Animates an element's horizontal offset and opacity for the tab
    /// carousel / reveal.</summary>
    private static void Slide(UIElement el, double fromX, double toX, double fromOp, double toOp,
        TimeSpan duration, IEasingFunction ease, Action? onDone, double yFrom = 0,
        double blurFrom = 0, double blurTo = 0)
    {
        var tt = new TranslateTransform(fromX, yFrom);
        el.RenderTransform = tt;
        el.Opacity = fromOp;

        el.BeginAnimation(OpacityProperty,
            new DoubleAnimation(fromOp, toOp, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        if (yFrom != 0)
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(yFrom, 0, duration) { EasingFunction = ease });

        // Blur-replace: animate a Gaussian blur alongside the slide/fade, and
        // drop the effect once it settles at zero so the content stays crisp.
        if (blurFrom > 0 || blurTo > 0)
        {
            var blur = new BlurEffect { Radius = blurFrom, KernelType = KernelType.Gaussian };
            el.Effect = blur;
            var ba = new DoubleAnimation(blurFrom, blurTo, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            if (blurTo <= 0)
                ba.Completed += (_, _) => { if (ReferenceEquals(el.Effect, blur)) el.Effect = null; };
            blur.BeginAnimation(BlurEffect.RadiusProperty, ba);
        }

        var xa = new DoubleAnimation(fromX, toX, duration) { EasingFunction = ease };
        if (onDone is not null) xa.Completed += (_, _) => onDone();
        tt.BeginAnimation(TranslateTransform.XProperty, xa);
    }

    /// <summary>Ordered list of tabs currently available (respecting the per-feature
    /// toggles) â€” the sequence horizontal swipes move through.</summary>
    private System.Collections.Generic.List<NotchView> VisibleTabs()
    {
        (NotchView View, string? Key)[] all =
        {
            (NotchView.Home, null),
            (NotchView.Calendar, "showCalendar"),
            (NotchView.Shelf, null),
            (NotchView.Stats, "enableStatsFeature"),
            (NotchView.Timer, "enableTimerFeature"),
            (NotchView.Clipboard, "enableClipboardManager"),
            (NotchView.Terminal, "enableTerminalFeature"),
        };

        var list = new System.Collections.Generic.List<NotchView>();
        foreach (var (view, key) in all)
            if (key is null || _shell!.GetSetting(key, true))
                list.Add(view);
        return list;
    }

    /// <summary>Switches to the adjacent tab in swipe order. <paramref name="dir"/>
    /// is +1 (right / next) or -1 (left / previous); the list wraps.</summary>
    private void SwitchAdjacentTab(int dir)
    {
        var tabs = VisibleTabs();
        if (tabs.Count < 2) return;

        var idx = tabs.IndexOf(_shell!.CurrentView);
        if (idx < 0) idx = 0;
        var next = ((idx + dir) % tabs.Count + tabs.Count) % tabs.Count;
        if (tabs[next] == _shell.CurrentView) return;

        _switchDir = dir;
        _shell.SwitchTab(tabs[next]);
    }

    /// <summary>Jumps straight to a tab, sliding in the direction of travel.</summary>
    private void SwitchToTab(NotchView view)
    {
        if (_shell is null || _shell.CurrentView == view) return;
        var tabs = VisibleTabs();
        var from = tabs.IndexOf(_shell.CurrentView);
        var to = tabs.IndexOf(view);
        _switchDir = from >= 0 && to >= 0 ? Math.Sign(to - from) : 0;
        _shell.SwitchTab(view);
    }

    private DateTime _lastSwipe = DateTime.MinValue;

    /// <summary>Accumulates horizontal-wheel deltas and switches one tab per swipe
    /// gesture, with a short cooldown so inertial scrolling doesn't race through
    /// every tab.</summary>
    private void OnHorizontalWheel(int delta)
    {
        if (_vm.State != NotchState.Open) return;

        _hWheelAccum += delta;
        const int threshold = 80;
        if (Math.Abs(_hWheelAccum) < threshold) return;

        if ((DateTime.UtcNow - _lastSwipe).TotalMilliseconds < 280)
        {
            _hWheelAccum = 0;
            return;
        }

        var dir = _hWheelAccum > 0 ? 1 : -1;
        _hWheelAccum = 0;
        _lastSwipe = DateTime.UtcNow;
        SwitchAdjacentTab(dir);
    }

    // ------------------------------------------------------------------
    // Interaction: hover-to-open, click-to-toggle
    // ------------------------------------------------------------------

    private double DipScaleX => Math.Max(_display.DpiScaleX, 1e-6);
    private double DipScaleY => Math.Max(_display.DpiScaleY, 1e-6);
    private double DipWorkLeft => _display.WorkArea.Left / DipScaleX;
    private double DipWorkTop => _display.WorkArea.Top / DipScaleY;
    private double DipWorkWidth => _display.WorkArea.Width / DipScaleX;

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);

        // Layered transparent WPF windows can report phantom mouse enter
        // events (observed with the cursor far from the notch). Validate the
        // real screen-space cursor before treating this as hover.
        if (!IsCursorInsideSurface())
        {
            _logger?.LogDebug($"Ignoring phantom mouse enter (cursor={DescribeCursor()})");
            return;
        }

        _closeTimer.Stop();
        _isHovering = true;
        _shell.CancelNotchAutoClose();
        _hoverTimer.Start();

        if (_vm.State == NotchState.Closed)
            SetHoverNudge(true);

        _logger?.LogDebug($"Notch mouse enter (cursor={DescribeCursor()})");
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        var wasHovering = _isHovering;

        _hoverTimer.Stop();

        // Layered-window mouse messages are unreliable: verify the real
        // cursor before treating this as an exit.
        if (IsCursorInsideSurface())
        {
            _isHovering = true;
            _logger?.LogDebug($"Ignoring phantom mouse leave (cursor={DescribeCursor()})");
            return;
        }

        _isHovering = false;
        SetHoverNudge(false);
        _logger?.LogDebug($"Notch mouse leave (wasHovering={wasHovering}, state={_vm.State})");

        // Only a real hover exit may close the notch. WPF can synthesize a
        // leave event when the layered window changes size/activation even
        // though the pointer never entered it.
        if (wasHovering && _vm.State == NotchState.Open)
            _closeTimer.Start();
        else
            _closeTimer.Stop();
    }

    /// <summary>
    /// Springs the closed pill to a slightly larger scale while hovered and back
    /// on exit, using a bouncy (under-damped) curve â€” the original's
    /// <c>Animation.bouncy.speed(1.2)</c> hover response. Applied as a render
    /// transform so it never disturbs layout, the window rect, or hit-testing.
    /// </summary>
    private void SetHoverNudge(bool hovering)
    {
        var target = hovering ? HoverNudgeScale : 1.0;
        var response = DesignTokens.OpenSpringResponse / DesignTokens.HoverSpringSpeed;
        var duration = TimeSpan.FromSeconds(response);
        var ease = new SpringEasing { DampingFraction = 0.7 };

        _hoverScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(_hoverScale.ScaleX, target, duration)
            { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
        _hoverScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(_hoverScale.ScaleY, target, duration)
            { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
    }

    private void OnHoverWatchTick()
    {
        if (_vm.State != NotchState.Open || _hwnd == IntPtr.Zero)
        {
            _hoverWatch.Stop();
            return;
        }

        if (IsCursorInsideSurface())
        {
            if (!_isHovering)
                _logger?.LogDebug($"Hover watchdog: cursor re-entered (cursor={DescribeCursor()})");
            _isHovering = true;
            _closeTimer.Stop();
            _shell.CancelNotchAutoClose();
            return;
        }

        if (_isHovering && !_shell.IsClipboardDragInProgress)
        {
            _isHovering = false;
            _closeTimer.Start();
        }
    }

    // A file dragged over the (closed) notch opens it on the Shelf tab so it can
    // be dropped in. The drag keeps the notch open; leaving/dropping lets the
    // normal hover-leave close take over.
    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        HandleFileDragOver(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        HandleFileDragOver(e);
    }

    private void HandleFileDragOver(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        _closeTimer.Stop();
        _shell.CancelNotchAutoClose();
        if (_vm.State != NotchState.Open)
        {
            OpenNotch();
            _shell.SwitchTab(NotchView.Shelf);
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _closeTimer.Stop();
        _shell.CancelNotchAutoClose();

        // A click on the closed pill opens it. While open, a click that reaches
        // the window (i.e. not on an interactive control) must NOT close the
        // notch â€” otherwise interacting with a tab like the calendar dismisses
        // it. The open notch closes on hover-leave instead.
        if (_vm.State != NotchState.Open)
            OpenNotch();
    }

    /// <summary>
    /// True when the real OS cursor is over the painted Surface (the notch
    /// itself), not merely inside the larger transparent window. Hover/close
    /// logic keys off the visible notch, and the window is now much bigger than
    /// it, so the window rect can no longer stand in for the hit region.
    /// </summary>
    private bool IsCursorInsideSurface()
    {
        if (!NativeMethods.GetCursorPos(out var point)) return false;
        return IsScreenPointInsideSurface(point.X, point.Y);
    }

    /// <summary>
    /// True when a screen-space point (device pixels) falls within the Surface's
    /// current on-screen bounds.
    /// </summary>
    private bool IsScreenPointInsideSurface(double screenX, double screenY)
    {
        if (_hwnd == IntPtr.Zero || !Surface.IsVisible) return false;

        try
        {
            var topLeft = Surface.PointToScreen(new Point(0, 0));
            var w = Surface.ActualWidth * DipScaleX;
            var h = Surface.ActualHeight * DipScaleY;

            return screenX >= topLeft.X && screenX < topLeft.X + w
                && screenY >= topLeft.Y && screenY < topLeft.Y + h;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Makes the transparent padding around the notch click-through: the OS asks
    /// where each point lands (WM_NCHITTEST), and we report HTTRANSPARENT unless
    /// the point is over the Surface, so clicks beside the notch reach the app
    /// behind it and WPF only sees mouse events over the notch itself.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_POWERBROADCAST
            && wParam.ToInt32() == NativeMethods.PBT_APMPOWERSTATUSCHANGE)
        {
            OnPowerStatusChanged();
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_MOUSEHWHEEL)
        {
            // Horizontal scroll (two-finger left/right) switches tabs. Delta is
            // the high word of wParam: positive = right/next, negative = left/prev.
            var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            OnHorizontalWheel(delta);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_NCHITTEST)
        {
            var lp = lParam.ToInt64();
            double screenX = (short)(lp & 0xFFFF);
            double screenY = (short)((lp >> 16) & 0xFFFF);

            handled = true;
            return IsScreenPointInsideSurface(screenX, screenY)
                ? new IntPtr(NativeMethods.HTCLIENT)
                : new IntPtr(NativeMethods.HTTRANSPARENT);
        }

        return IntPtr.Zero;
    }

    private string DescribeCursor()
    {
        if (!NativeMethods.GetCursorPos(out var point)) return "unknown";
        return $"{point.X},{point.Y}";
    }

    private void OpenNotch()
    {
        if (_vm.State == NotchState.Open) return;
        _vm.State = NotchState.Open; // property change drives the spring
    }

    private void CloseNotch()
    {
        if (_vm.State == NotchState.Closed) return;
        _vm.State = NotchState.Closed; // property change drives the spring
    }

    private void ToggleNotch()
    {
        if (_vm.State == NotchState.Open)
            CloseNotch();
        else
            OpenNotch();
    }

    // ------------------------------------------------------------------
    // Spring animation (window rect + corner radii)
    // ------------------------------------------------------------------

    // Notch corner radii as (top shoulder, bottom corner) pairs â€” the two values
    // the macOS NotchShape animates.
    private static (double Top, double Bottom) ClosedRadii =>
        (DesignTokens.ClosedCornerRadiusTop, DesignTokens.ClosedCornerRadiusBottom);

    private static (double Top, double Bottom) OpenRadii =>
        (DesignTokens.OpenCornerRadiusTop, DesignTokens.OpenCornerRadiusBottom);

    /// <summary>
    /// Builds the macOS notch outline for a wÃ—h surface: a flat full-width top
    /// with concave shoulder curves of radius <paramref name="topR"/>, straight
    /// sides inset by topR, and convex bottom corners of radius
    /// <paramref name="botR"/>. Mirrors the original <c>NotchShape</c>.
    /// </summary>
    private static Geometry BuildNotchGeometry(double w, double h, double topR, double botR)
    {
        topR = Math.Max(0, Math.Min(topR, w / 2));
        botR = Math.Max(0, Math.Min(botR, Math.Min(w / 2, h)));

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
            // Top-left concave shoulder.
            c.QuadraticBezierTo(new Point(topR, 0), new Point(topR, topR), true, false);
            // Left side.
            c.LineTo(new Point(topR, h - botR), true, false);
            // Bottom-left convex corner.
            c.QuadraticBezierTo(new Point(topR, h), new Point(topR + botR, h), true, false);
            // Bottom edge.
            c.LineTo(new Point(w - topR - botR, h), true, false);
            // Bottom-right convex corner.
            c.QuadraticBezierTo(new Point(w - topR, h), new Point(w - topR, h - botR), true, false);
            // Right side.
            c.LineTo(new Point(w - topR, topR), true, false);
            // Top-right concave shoulder.
            c.QuadraticBezierTo(new Point(w - topR, 0), new Point(w, 0), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Applies the notch outline to the painted background and clips the
    /// content to it.</summary>
    private double _currentTopR = DesignTokens.ClosedCornerRadiusTop;
    private double _currentBottomR = DesignTokens.ClosedCornerRadiusBottom;

    private void ApplyNotchGeometry(double w, double h, double topR, double botR)
    {
        _currentTopR = topR;
        _currentBottomR = botR;
        var geo = BuildNotchGeometry(w, h, topR, botR);
        NotchBg.Data = geo;
        ContentHost.Clip = geo;
        MorphLayer.Clip = geo;
    }

    private void AnimateStateChange()
    {
        if (_hwnd == IntPtr.Zero)
        {
            UpdateContent();
            return;
        }

        var open = _vm.State == NotchState.Open;

        // The hover nudge is a closed-pill affordance; release it as we open so
        // the open surface sits at its true size.
        SetHoverNudge(false);

        if (open)
        {
            _hoverWatch.Start();
        }
        else
        {
            _hoverWatch.Stop();
            _closeTimer.Stop();
            _hoverTimer.Stop();
            _isHovering = false;
        }

        var targetW = open ? _vm.OpenWidth : EffectiveClosedWidth;
        var targetH = open ? _vm.OpenHeight : _vm.ClosedHeight;

        var response = open ? DesignTokens.OpenSpringResponse : DesignTokens.CloseSpringResponse;
        var damping = open ? DesignTokens.OpenSpringDamping : DesignTokens.CloseSpringDamping;

        // Morph: capture the shared elements (art, spectrum) and ghost the rest of
        // the outgoing view *before* the swap, then fly them into the new view.
        var morph = PrepareMorph(open);

        RunSpringAnimation(targetW, targetH, open ? OpenRadii : ClosedRadii, response, damping);

        UpdateContent();

        if (open && _statusHeader is not null)
            FadeBlurIn(_statusHeader, DesignTokens.ViewTransitionDuration);
        LaunchMorph(morph, open);
    }

    /// <summary>
    /// Animates the Surface size AND its corner radii from a single spring clock
    /// so they move in perfect lockstep.
    ///
    /// Unlike the earlier port, this animates the inner <c>Surface</c> Border â€”
    /// never the top-level window â€” so there is no per-frame HWND resize or
    /// Win32 round-trip. The macOS original works the same way: a fixed window,
    /// with only the content frame springing. Driving the size and all four
    /// corners off one <see cref="Stopwatch"/> + one spring evaluation per frame
    /// keeps them from drifting apart (the previous split between a WPF
    /// <see cref="DoubleAnimation"/> and a separate stopwatch made the corners
    /// lag the frame). The spring progress may exceed 1.0 for an under-damped
    /// spring (dampingFraction &lt; 1), lerping past the target and settling
    /// back â€” the intended bounce; <see cref="ShadowPad"/> leaves room for it.
    /// </summary>
    private void RunSpringAnimation(double toW, double toH, (double Top, double Bottom) toRadii,
        double response, double damping)
    {
        // Cancel any in-flight animation and bump the generation counter.
        StopAnimations();
        var generation = _animationGeneration;

        var fromW = double.IsNaN(Surface.Width) ? Surface.ActualWidth : Surface.Width;
        var fromH = double.IsNaN(Surface.Height) ? Surface.ActualHeight : Surface.Height;
        var fromTopR = _currentTopR;
        var fromBottomR = _currentBottomR;
        var durationSeconds = Math.Max(response, 1e-3);
        var stopwatch = Stopwatch.StartNew();

        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (generation != _animationGeneration)
            {
                CompositionTarget.Rendering -= handler!;
                return;
            }

            var t = Math.Clamp(stopwatch.Elapsed.TotalSeconds / durationSeconds, 0.0, 1.0);
            var p = SpringMath.Progress(t, damping);

            var w = SpringMath.Lerp(fromW, toW, p);
            var h = SpringMath.Lerp(fromH, toH, p);
            Surface.Width = w;
            Surface.Height = h;

            ApplyNotchGeometry(w, h,
                SpringMath.Lerp(fromTopR, toRadii.Top, p),
                SpringMath.Lerp(fromBottomR, toRadii.Bottom, p));

            if (t >= 1.0)
            {
                CompositionTarget.Rendering -= handler!;
                _cornerRenderingHandler = null;

                Surface.Width = toW;
                Surface.Height = toH;
                ApplyNotchGeometry(toW, toH, toRadii.Top, toRadii.Bottom);
            }
        };

        _cornerRenderingHandler = handler;
        CompositionTarget.Rendering += handler;
    }

    private void StopAnimations()
    {
        _animationGeneration++;

        if (_cornerRenderingHandler is not null)
        {
            CompositionTarget.Rendering -= _cornerRenderingHandler;
            _cornerRenderingHandler = null;
        }
    }

    // ------------------------------------------------------------------
    // Window positioning / Win32
    // ------------------------------------------------------------------

    private void ApplyWin32Styles()
    {
        if (_hwnd == IntPtr.Zero) return;

        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

        // No DWM system backdrop: this is a per-pixel-transparent (layered)
        // window, so a backdrop material shows through every transparent pixel
        // around the notch as an opaque grey rectangle. The notch draws its own
        // solid surface, so it needs no system material behind it.
    }

    internal void PositionOnScreen()
    {
        if (_hwnd == IntPtr.Zero || _isClosing) return;

        StopAnimations();

        var open = _vm.State == NotchState.Open;

        // Fixed, screen-centred window sized for the open notch plus shadow room.
        var winW = _vm.OpenWidth + ShadowPad * 2;
        var winH = _vm.OpenHeight + ShadowPad;
        var left = DipWorkLeft + (DipWorkWidth - winW) / 2.0;
        var top = DipWorkTop;

        Width = winW;
        Height = winH;
        Left = left;
        Top = top;

        // The Surface (centred, top-aligned) carries the current notch size.
        var sw = open ? _vm.OpenWidth : EffectiveClosedWidth;
        var sh = open ? _vm.OpenHeight : _vm.ClosedHeight;
        var radii = open ? OpenRadii : ClosedRadii;
        Surface.Width = sw;
        Surface.Height = sh;
        ApplyNotchGeometry(sw, sh, radii.Top, radii.Bottom);

        ApplyWindowRect(left, top, winW, winH);
    }

    private void ApplyWindowRect(double left, double top, double width, double height)
    {
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            (int)Math.Round(left * DipScaleX), (int)Math.Round(top * DipScaleY),
            (int)Math.Round(width * DipScaleX), (int)Math.Round(height * DipScaleY),
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _hoverTimer.Stop();
        _closeTimer.Stop();
        StopAnimations();
        FinishMorph();
        base.OnClosed(e);
    }
}

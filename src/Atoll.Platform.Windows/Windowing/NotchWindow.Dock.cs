using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Atoll.Core.Models;
using Atoll.Platform.Windows.Interop;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>Screen edge the notch is attached to.</summary>
public enum NotchDock { Top, Left, Right }

/// <summary>The notch's on-screen rectangle and work area (device pixels), its
/// edge, and the display's DIP scale — where the assistant's droplet buds from.</summary>
public sealed record NotchAnchor(Rect Notch, Rect WorkArea, NotchDock Dock, double ScaleX, double ScaleY);

/// <summary>
/// Drag-to-dock. Press and drag the closed pill: it lets go of its edge (the
/// shoulders melt and it rounds into a free capsule), follows the pointer with
/// a springy lag, and on release flies to the nearest screen edge, landing as
/// that edge's notch. On the left / right edge the closed notch is a vertical
/// tab (its live activity turned to read down the edge, art and waveform kept
/// upright) and it opens sideways into the normal panel.
///
/// Geometry stays edge-agnostic: everything is built as if attached to the top
/// (along × depth) and transposed for a side dock, so the open / close spring,
/// the morph and hit-testing all work unchanged.
/// </summary>
public partial class NotchWindow
{
    private const double DragThreshold = 6;      // DIPs before a press becomes a drag
    private const double DragBox = 460;          // window size while free-floating

    private NotchDock _dock = NotchDock.Top;
    private double _dockOffset = 0.5;            // side docks: centre as a fraction of work-area height

    /// <summary>0 = attached to an edge, 1 = free capsule (drag); blends the shape.</summary>
    private double _freeBlend;

    private bool IsSideDock => _dock != NotchDock.Top;
    private double DipWorkHeight => _display.WorkArea.Height / DipScaleY;

    public NotchAnchor? GetAnchor()
    {
        if (_hwnd == IntPtr.Zero || !Surface.IsLoaded) return null;
        var tl = Surface.PointToScreen(new Point(0, 0));
        var notch = new Rect(tl.X, tl.Y, Surface.ActualWidth * DipScaleX, Surface.ActualHeight * DipScaleY);
        var wa = _display.WorkArea;
        return new NotchAnchor(notch, new Rect(wa.Left, wa.Top, wa.Width, wa.Height), _dock, DipScaleX, DipScaleY);
    }

    private void ReadDockSettings()
    {
        _dock = _shell.GetSetting("notchDockEdge", "top") switch
        {
            "left" => NotchDock.Left,
            "right" => NotchDock.Right,
            _ => NotchDock.Top,
        };
        _dockOffset = Math.Clamp(_shell.GetSetting("notchDockOffset", 0.5), 0.1, 0.9);
    }

    /// <summary>Physical surface size of the closed notch: a horizontal pill at the
    /// top, the same pill stood on end along a side edge.</summary>
    private (double W, double H) ClosedSurfaceSize =>
        IsSideDock ? (_vm.ClosedHeight, EffectiveClosedWidth) : (EffectiveClosedWidth, _vm.ClosedHeight);

    /// <summary>Lays the fixed window and the Surface out for the current edge.</summary>
    private (double Left, double Top, double W, double H) DockedWindowRect()
    {
        if (!IsSideDock)
        {
            var w = WindowWidth;
            return (DipWorkLeft + (DipWorkWidth - w) / 2.0, DipWorkTop, w, MaxOpenHeight + ShadowPad);
        }

        var winW = _vm.OpenWidth + ShadowPad;
        var winH = Math.Max(MaxOpenHeight, ClosedHudActivity.PillWidth(SneakContentType.Volume, 1, new HudOptions(), _vm.ClosedWidth))
                   + ShadowPad * 2;
        var centreY = DipWorkTop + DipWorkHeight * _dockOffset;
        var top = Math.Clamp(centreY - winH / 2, DipWorkTop, DipWorkTop + DipWorkHeight - winH);
        var left = _dock == NotchDock.Left ? DipWorkLeft : DipWorkLeft + DipWorkWidth - winW;
        return (left, top, winW, winH);
    }

    private void ApplyDockAlignment()
    {
        Surface.Margin = new Thickness(0);
        switch (_dock)
        {
            case NotchDock.Left:
                Surface.HorizontalAlignment = HorizontalAlignment.Left;
                Surface.VerticalAlignment = VerticalAlignment.Center;
                Surface.RenderTransformOrigin = new Point(0, 0.5);
                break;
            case NotchDock.Right:
                Surface.HorizontalAlignment = HorizontalAlignment.Right;
                Surface.VerticalAlignment = VerticalAlignment.Center;
                Surface.RenderTransformOrigin = new Point(1, 0.5);
                break;
            default:
                Surface.HorizontalAlignment = HorizontalAlignment.Center;
                Surface.VerticalAlignment = VerticalAlignment.Top;
                Surface.RenderTransformOrigin = new Point(0.5, 0);
                break;
        }
    }

    /// <summary>Turns the closed live activity to run along a side edge.</summary>
    private void OrientClosedContent()
    {
        if (ContentHost.Child is not FrameworkElement child || ReferenceEquals(child, _outerGrid)) return;
        child.LayoutTransform = IsSideDock ? new RotateTransform(90) : Transform.Identity;
        if (child is ClosedMediaActivity media) media.SetIconsUpright(IsSideDock ? -90 : 0);
    }

    // ------------------------------------------------------------------
    // Shape
    // ------------------------------------------------------------------

    /// <summary>
    /// The notch outline for a physical w×h surface on the current edge. Built
    /// top-attached in (along, depth) space and transposed for a side dock.
    /// <paramref name="free"/> blends toward a detached capsule: first the
    /// shoulders melt away, then the attached corners round off.
    /// </summary>
    private Geometry BuildDockedGeometry(double w, double h, double topR, double botR, double free)
    {
        var side = IsSideDock;
        var along = side ? h : w;
        var depth = side ? w : h;

        free = Math.Clamp(free, 0, 1);
        var shoulder = topR * Math.Clamp(1 - free * 2, 0, 1);
        var attached = Math.Min(depth, along) / 2 * Math.Clamp(free * 2 - 1, 0, 1);
        var outer = botR + (Math.Min(depth, along) / 2 - botR) * free;

        var g = attached > 0.01
            ? BuildRoundedPill(along, depth, attached, outer)
            : BuildNotchGeometry(along, depth, shoulder, outer);

        if (!side) return g;
        var m = _dock == NotchDock.Left
            ? new Matrix(0, 1, 1, 0, 0, 0)          // x' = depth, y' = along
            : new Matrix(0, 1, -1, 0, w, 0);        // mirrored onto the right edge
        var t = g.Clone();
        t.Transform = new MatrixTransform(m);
        t.Freeze();
        return t;
    }

    private static Geometry BuildRoundedPill(double w, double h, double topR, double botR)
    {
        topR = Math.Min(topR, Math.Min(w, h) / 2);
        botR = Math.Min(botR, Math.Min(w, h) / 2);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, topR), true, true);
            c.QuadraticBezierTo(new Point(0, 0), new Point(topR, 0), true, false);
            c.LineTo(new Point(w - topR, 0), true, false);
            c.QuadraticBezierTo(new Point(w, 0), new Point(w, topR), true, false);
            c.LineTo(new Point(w, h - botR), true, false);
            c.QuadraticBezierTo(new Point(w, h), new Point(w - botR, h), true, false);
            c.LineTo(new Point(botR, h), true, false);
            c.QuadraticBezierTo(new Point(0, h), new Point(0, h - botR), true, false);
        }
        g.Freeze();
        return g;
    }

    // ------------------------------------------------------------------
    // Drag
    // ------------------------------------------------------------------

    private bool _dragArmed, _dragging;
    private Point _pressScreen;                   // device pixels
    private Vector _grabOffset;                   // cursor → surface centre, DIPs
    private EventHandler? _dragFrame;

    // spring state (DIPs, screen space for the centre)
    private double _cx, _cy, _cvx, _cvy;
    private double _sw, _sh, _swv, _shv;
    private double _fv;
    private bool _landing;
    private NotchDock _landEdge;
    private double _landOffset;

    /// <summary>A press on the closed pill arms a drag; a release without moving
    /// is an ordinary click (opens the notch).</summary>
    private bool TryArmDrag(MouseButtonEventArgs e)
    {
        if (_vm.State == NotchState.Open || _hiddenForFullscreen || !NativeMethods.GetCursorPos(out var p)) return false;
        _dragArmed = true;
        _pressScreen = new Point(p.X, p.Y);
        _hoverTimer.Stop();
        CaptureMouse();
        e.Handled = true;
        return true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragArmed || _dragging || e.LeftButton != MouseButtonState.Pressed) return;
        if (!NativeMethods.GetCursorPos(out var p)) return;
        var dx = (p.X - _pressScreen.X) / DipScaleX;
        var dy = (p.Y - _pressScreen.Y) / DipScaleY;
        if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold) return;
        BeginDrag();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging) { EndDrag(); e.Handled = true; return; }
        if (!_dragArmed) return;
        _dragArmed = false;
        ReleaseMouseCapture();
        OpenNotch(); // a plain click
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragging && !_landing) EndDrag();
        _dragArmed = false;
    }

    private void BeginDrag()
    {
        _dragging = true;
        _landing = false;
        SetHoverNudge(false);
        _hoverTimer.Stop();
        _closeTimer.Stop();
        StopAnimations();

        // Where the surface is now, in screen DIPs.
        var tl = Surface.PointToScreen(new Point(0, 0));
        _sw = Surface.ActualWidth;
        _sh = Surface.ActualHeight;
        _cx = tl.X / DipScaleX + _sw / 2;
        _cy = tl.Y / DipScaleY + _sh / 2;
        _cvx = _cvy = _swv = _shv = _fv = 0;
        NativeMethods.GetCursorPos(out var p);
        _grabOffset = new Vector(_cx - p.X / DipScaleX, _cy - p.Y / DipScaleY);

        // Free-floating window centred on the surface.
        Surface.HorizontalAlignment = HorizontalAlignment.Center;
        Surface.VerticalAlignment = VerticalAlignment.Center;
        Surface.RenderTransformOrigin = new Point(0.5, 0.5);
        MoveDragWindow();

        // The live activity hides while the pill is in flight.
        ContentHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        _logger?.LogDebug("Notch drag started");

        _dragFrame ??= OnDragFrame;
        CompositionTarget.Rendering += _dragFrame;
        _lastDragFrame = TimeSpan.Zero;
    }

    private TimeSpan _lastDragFrame;

    private void OnDragFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = _lastDragFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _lastDragFrame).TotalSeconds, 0, 1 / 20.0);
        _lastDragFrame = now;
        if (dt <= 0) return;

        double tx, ty, tw, th, tf;
        if (!_landing)
        {
            // Follow the pointer as a free horizontal capsule.
            NativeMethods.GetCursorPos(out var p);
            tx = p.X / DipScaleX + _grabOffset.X;
            ty = p.Y / DipScaleY + _grabOffset.Y;
            tw = EffectiveClosedWidth * 0.82;
            th = _vm.ClosedHeight;
            tf = 1;
            // The grab point eases to the capsule centre so it never drifts off the pointer.
            _grabOffset *= Math.Pow(0.02, dt);
            Spring(ref _cx, ref _cvx, tx, 0.32, 0.78, dt);
            Spring(ref _cy, ref _cvy, ty, 0.32, 0.78, dt);
        }
        else
        {
            var (lw, lh) = LandingSize();
            (tx, ty) = LandingCentre(lw, lh);
            tw = lw; th = lh; tf = 0;
            Spring(ref _cx, ref _cvx, tx, 0.5, 0.8, dt);
            Spring(ref _cy, ref _cvy, ty, 0.5, 0.8, dt);
        }

        Spring(ref _sw, ref _swv, tw, 0.45, 0.72, dt);
        Spring(ref _sh, ref _shv, th, 0.45, 0.72, dt);
        Spring(ref _freeBlend, ref _fv, tf, 0.38, 0.85, dt);

        Surface.Width = Math.Max(4, _sw);
        Surface.Height = Math.Max(4, _sh);
        ApplyNotchGeometry(Surface.Width, Surface.Height, ClosedRadii.Top, ClosedRadii.Bottom);
        MoveDragWindow();

        if (_landing && Settled(tx, ty, tw, th))
            FinishLanding();
    }

    private bool Settled(double tx, double ty, double tw, double th) =>
        Math.Abs(_cx - tx) < 0.5 && Math.Abs(_cy - ty) < 0.5 && Math.Abs(_sw - tw) < 0.5 && Math.Abs(_sh - th) < 0.5
        && Math.Abs(_freeBlend) < 0.01
        && Math.Abs(_cvx) + Math.Abs(_cvy) + Math.Abs(_swv) + Math.Abs(_shv) + Math.Abs(_fv) < 2;

    private void MoveDragWindow()
    {
        if (Width != DragBox) Width = DragBox;
        if (Height != DragBox) Height = DragBox;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            (int)Math.Round((_cx - DragBox / 2) * DipScaleX), (int)Math.Round((_cy - DragBox / 2) * DipScaleY),
            (int)Math.Round(DragBox * DipScaleX), (int)Math.Round(DragBox * DipScaleY),
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
    }

    /// <summary>Release: snap to whichever edge the pointer is nearest.</summary>
    private void EndDrag()
    {
        if (_landing) return;
        _dragArmed = false;
        NativeMethods.GetCursorPos(out var p);
        var x = p.X / DipScaleX;
        var y = p.Y / DipScaleY;

        var dLeft = x - DipWorkLeft;
        var dRight = DipWorkLeft + DipWorkWidth - x;
        var dTop = (y - DipWorkTop) * 1.6; // the top is home: favour it a little
        _landEdge = dTop <= dLeft && dTop <= dRight ? NotchDock.Top : dLeft <= dRight ? NotchDock.Left : NotchDock.Right;
        _landOffset = Math.Clamp((y - DipWorkTop) / Math.Max(1, DipWorkHeight), 0.1, 0.9);

        // Commit the edge now so the shape is built for it while landing. The
        // physical size keeps springing continuously from the free capsule to
        // the docked tab; the capsule is symmetric, so the switch is invisible.
        _dock = _landEdge;
        _dockOffset = _landOffset;
        _landing = true;
        ReleaseMouseCapture();
        _logger?.LogInfo($"Notch docking to {_dock}");
    }

    private (double W, double H) LandingSize() => ClosedSurfaceSize;

    private (double X, double Y) LandingCentre(double w, double h) => _dock switch
    {
        NotchDock.Left => (DipWorkLeft + w / 2, DockCentreY()),
        NotchDock.Right => (DipWorkLeft + DipWorkWidth - w / 2, DockCentreY()),
        _ => (DipWorkLeft + DipWorkWidth / 2, DipWorkTop + h / 2),
    };

    /// <summary>The side-docked window is clamped to the work area, so its centre
    /// (where the surface sits) can differ from the raw offset near the ends.</summary>
    private double DockCentreY()
    {
        var r = DockedWindowRect();
        return r.Top + r.H / 2;
    }

    private void FinishLanding()
    {
        CompositionTarget.Rendering -= _dragFrame;
        _dragging = false;
        _landing = false;
        _freeBlend = 0;

        _shell.SetSetting("notchDockEdge", _dock.ToString().ToLowerInvariant());
        _shell.SetSetting("notchDockOffset", _dockOffset);

        PositionOnScreen();                 // docked layout, same on-screen spot
        ContentHost.BeginAnimation(OpacityProperty, null);
        ContentHost.Opacity = 1;
        _closedActivity = ClosedPillActivity.None; // force the activity to rebuild + blur in
        UpdateContent();
    }
}

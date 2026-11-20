using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Atoll.Core.Design;
using Atoll.Core.Models;

namespace Atoll.Core.ViewModels;

/// <summary>
/// View model for a single floating notch surface on one display.
/// Mirrors <c>DynamicIslandViewModel</c> notchState plus the sizing
/// constants from <c>DynamicIsland/ContentView.swift</c>
/// (closedNotchWidth 150, openNotchWidth 640, notchHeight 32).
/// </summary>
public sealed class NotchViewModel : INotifyPropertyChanged
{
    private NotchState _state = NotchState.Closed;
    private double _closedWidth = DesignTokens.ClosedNotchWidth;
    private double _closedHeight = DesignTokens.NotchHeight;
    private double _openWidth = DesignTokens.OpenNotchWidth;
    private double _openHeight = DesignTokens.OpenNotchHeight;
    private object? _currentContent;
    private bool _isHovering;

    public NotchState State
    {
        get => _state;
        set => SetField(ref _state, value);
    }

    public double ClosedWidth
    {
        get => _closedWidth;
        set => SetField(ref _closedWidth, value);
    }

    public double ClosedHeight
    {
        get => _closedHeight;
        set => SetField(ref _closedHeight, value);
    }

    public double OpenWidth
    {
        get => _openWidth;
        set => SetField(ref _openWidth, value);
    }

    public double OpenHeight
    {
        get => _openHeight;
        set => SetField(ref _openHeight, value);
    }

    /// <summary>
    /// Current width to render: closed or open depending on State.
    /// </summary>
    public double CurrentWidth => State == NotchState.Open ? OpenWidth : ClosedWidth;

    /// <summary>
    /// Current height to render: closed or open depending on State.
    /// </summary>
    public double CurrentHeight => State == NotchState.Open ? OpenHeight : ClosedHeight;

    public object? CurrentContent
    {
        get => _currentContent;
        set => SetField(ref _currentContent, value);
    }

    public bool IsHovering
    {
        get => _isHovering;
        set => SetField(ref _isHovering, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        // CurrentWidth/CurrentHeight depend on State
        if (propertyName == nameof(State))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentWidth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentHeight)));
        }
        return true;
    }
}

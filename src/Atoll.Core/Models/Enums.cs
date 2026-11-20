namespace Atoll.Core.Models;

/// <summary>
/// Presentation state of the floating notch surface.
/// Mirrors <c>DynamicIslandViewModel.notchState</c> (.closed / .open).
/// </summary>
public enum NotchState
{
    Closed,
    Open
}

/// <summary>
/// Notch tabs. Order mirrors <c>DynamicIslandViewCoordinator.tabOrder</c>.
/// </summary>
public enum NotchView
{
    Home,
    Calendar,
    Shelf,
    Timer,
    Stats,
    LlmUsage,
    ColorPicker,
    Notes,
    Clipboard,
    Terminal,
    ExtensionExperience
}

/// <summary>
/// Kinds of transient "sneak peek" / live-activity content shown in the
/// closed notch. Mirrors <c>SneakContentType</c>.
/// </summary>
public enum SneakContentType
{
    Brightness,
    Volume,
    Backlight,
    Music,
    Mic,
    Battery,
    Download,
    Timer,
    Reminder,
    Recording,
    DoNotDisturb,
    BluetoothAudio,
    Privacy,
    LockScreen,
    CapsLock,
    ExtensionLiveActivity
}

/// <summary>
/// How a media skip behaves (from <c>musicSkipBehavior</c>).
/// </summary>
public enum MediaSkipBehavior
{
    Track,
    PlusMinusTenSeconds
}

/// <summary>
/// Stats graph visibility categories.
/// </summary>
[Flags]
public enum StatsCategories
{
    None = 0,
    Cpu = 1 << 0,
    Memory = 1 << 1,
    Gpu = 1 << 2,
    Network = 1 << 3,
    Disk = 1 << 4,
    All = Cpu | Memory | Gpu | Network | Disk
}

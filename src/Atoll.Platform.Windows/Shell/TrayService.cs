using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Atoll.Core.Contracts;

namespace Atoll.Platform.Windows.Shell;

/// <summary>
/// Notification-area icon. Translates the macOS menu bar extra
/// (<c>MenuBarExtra</c> in DynamicIslandApp.swift) to <c>Shell_NotifyIcon</c>.
/// The context menu is supplied by the app layer because it needs access to
/// settings/window services.
/// </summary>
public sealed class TrayService : ITrayService
{
    private const int WM_TRAYICON = 0x8001;   // WM_APP + 1
    private const int WM_CONTEXTMENU = 0x007B;
    private const int NIN_SELECT = 0x0400;
    private const int NIN_KEYSELECT = 0x0401;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIM_SETVERSION = 0x00000004;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    private const int NOTIFYICON_VERSION_4 = 4;
    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x00000010;
    private const int LR_DEFAULTSIZE = 0x00000040;

    private readonly Func<ContextMenu> _menuFactory;
    private readonly ILogger? _logger;
    private readonly HwndSource _source;
    private readonly IntPtr _hIcon;
    private readonly uint _iconId = 1;
    private readonly uint _taskbarCreatedMessage;

    private bool _visible;
    private string _toolTip = "Atoll";
    private bool _disposed;

    public event EventHandler? LeftClicked;
    public event EventHandler? RightClicked;

    public TrayService(Func<ContextMenu> menuFactory, ILogger? logger = null)
    {
        _menuFactory = menuFactory;
        _logger = logger;

        // Use a hidden top-level window rather than HWND_MESSAGE: the shell
        // needs a discoverable window to deliver tray callbacks reliably.
        var parameters = new HwndSourceParameters("Atoll.TraySink")
        {
            WindowStyle = unchecked((int)0x80000000),          // WS_POPUP
            ExtendedWindowStyle = 0x00000080 | 0x08000000,       // TOOLWINDOW | NOACTIVATE
            PositionX = -32000,
            PositionY = -32000,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _hIcon = LoadIcon();
        AddIcon();
    }

    public bool Visible
    {
        get => _visible;
        set => SetVisible(value);
    }

    public string? ToolTip
    {
        get => _toolTip;
        set
        {
            _toolTip = value ?? "Atoll";
            ModifyIcon();
        }
    }

    private void AddIcon()
    {
        var data = CreateData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WM_TRAYICON;
        data.hIcon = _hIcon;
        data.szTip = Truncate(_toolTip, 127);

        if (Shell_NotifyIcon(NIM_ADD, ref data))
        {
            _visible = true;
            _logger?.LogInfo("Tray icon added");
            // Version 4 gives NIN_SELECT / WM_CONTEXTMENU in lParam and
            // cursor coordinates in wParam.
            data.uVersion = NOTIFYICON_VERSION_4;
            if (!Shell_NotifyIcon(NIM_SETVERSION, ref data))
            {
                _logger?.LogWarn(
                    $"Shell_NotifyIcon(NIM_SETVERSION) failed: {Marshal.GetLastWin32Error()}");
            }
        }
        else
        {
            _logger?.LogWarn(
                $"Shell_NotifyIcon(NIM_ADD) failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private void ModifyIcon()
    {
        if (!_visible) return;
        var data = CreateData();
        data.uFlags = NIF_ICON | NIF_TIP;
        data.hIcon = _hIcon;
        data.szTip = Truncate(_toolTip, 127);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private void SetVisible(bool visible)
    {
        if (visible == _visible) return;
        if (visible)
        {
            AddIcon();
        }
        else
        {
            var data = CreateData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _visible = false;
            _logger?.LogInfo("Tray icon removed");
        }
    }

    private NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _source.Handle,
        uID = (int)_iconId,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // The shell broadcasts TaskbarCreated after Explorer restarts; without
        // re-adding, the icon would stay gone (macOS MenuBarExtra is recreated
        // by the system automatically).
        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            _visible = false;
            AddIcon();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg != WM_TRAYICON) return IntPtr.Zero;

        var notification = (int)(lParam.ToInt64() & 0xFFFF);
        switch (notification)
        {
            case NIN_SELECT:
            case NIN_KEYSELECT:
                LeftClicked?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
            case WM_CONTEXTMENU:
                RightClicked?.Invoke(this, EventArgs.Empty);
                if (GetCursorPos(out var pt))
                    _ = ShowContextMenuAsync(pt.X, pt.Y);
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    public Task ShowContextMenuAsync(int screenX, int screenY, CancellationToken ct = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return Task.CompletedTask;

        return dispatcher.InvokeAsync(() =>
        {
            var menu = _menuFactory();
            if (menu is null) return;

            var transform = _source.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var dip = transform.Transform(new Point(screenX, screenY));

            menu.Placement = PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = dip.X;
            menu.VerticalOffset = dip.Y;
            menu.StaysOpen = false;
            menu.IsOpen = true;
        }).Task;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    // ------------------------------------------------------------------
    // Icon: render a dark rounded tile with a white "mountain" glyph,
    // mirroring the original MenuBarExtra systemImage "mountain.2.fill".
    // ------------------------------------------------------------------

    private static IntPtr LoadIcon()
    {
        var path = EnsureIconFile();
        var hIcon = LoadImage(IntPtr.Zero, path, IMAGE_ICON, 0, 0,
            LR_LOADFROMFILE | LR_DEFAULTSIZE);

        if (hIcon == IntPtr.Zero)
        {
            // Fallback: stock application icon so the tray entry still works.
            hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
        }
        return hIcon;
    }

    private static string EnsureIconFile()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Atoll");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "tray.ico");

        var png = RenderPng(32);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);          // reserved
        writer.Write((ushort)1);          // type = icon
        writer.Write((ushort)1);          // count
        writer.Write((byte)32);           // width
        writer.Write((byte)32);           // height
        writer.Write((byte)0);            // palette
        writer.Write((byte)0);            // reserved
        writer.Write((ushort)1);          // planes
        writer.Write((ushort)32);         // bpp
        writer.Write(png.Length);         // size of PNG data
        writer.Write(22);                 // offset of PNG data
        writer.Write(png);

        return path;
    }

    private static byte[] RenderPng(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var tile = new System.Windows.Rect(0.5, 0.5, size - 1, size - 1);
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)),
                new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C)), 1.0),
                tile, 7, 7);

            var mountain = new StreamGeometry();
            using (var ctx = mountain.Open())
            {
                ctx.BeginFigure(new Point(6.5, 24.5), true, true);
                ctx.LineTo(new Point(12.5, 13.5), true, false);
                ctx.LineTo(new Point(16.5, 18.5), true, false);
                ctx.LineTo(new Point(20.0, 14.0), true, false);
                ctx.LineTo(new Point(25.5, 24.5), true, false);
            }
            mountain.Freeze();

            dc.DrawGeometry(
                new SolidColorBrush(Colors.White), null, mountain);

            // Snow cap accent, mirrors the filled SF Symbol's highlight.
            var cap = new StreamGeometry();
            using (var ctx = cap.Open())
            {
                ctx.BeginFigure(new Point(12.5, 13.5), true, true);
                ctx.LineTo(new Point(14.6, 16.8), true, false);
                ctx.LineTo(new Point(12.2, 15.6), true, false);
                ctx.LineTo(new Point(10.6, 17.1), true, false);
            }
            cap.Freeze();
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x9E, 0xC7, 0xFF)), null, cap);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------
    // Win32
    // ------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, int type, int cx, int cy, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInst, IntPtr name);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_visible)
        {
            var data = CreateData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _visible = false;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();

        if (_hIcon != IntPtr.Zero)
            DestroyIcon(_hIcon);
    }
}

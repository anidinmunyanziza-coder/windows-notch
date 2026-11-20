# Captures a top-level window of a process to a PNG using PrintWindow.
# Usage: powershell -File tools/capture-window.ps1 -ProcessId 1234 -TitleLike 'Settings' -Out shot.png
param(
    [int]$ProcessId = 0,
    [string]$TitleLike = '',
    [Parameter(Mandatory = $true)][string]$Out,
    [long]$Handle = 0
)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class AtollWindowCapture
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lp);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; }

    public static List<string> List(uint target)
    {
        var list = new List<string>();
        EnumWindows((h, l) =>
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == target && IsWindowVisible(h))
            {
                var t = new StringBuilder(256);
                GetWindowText(h, t, 256);
                RECT r;
                GetWindowRect(h, out r);
                list.Add(string.Format("{0}|{1}|{2},{3},{4},{5}", h, t, r.L, r.T, r.R, r.B));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
'@

$windows = if ($ProcessId -ne 0) {
    [AtollWindowCapture]::List([uint32]$ProcessId)
} else {
    @()
}

$window = $null
if ($Handle -ne 0) {
    $window = $windows | Where-Object { $_ -like ("{0}|*" -f $Handle) } | Select-Object -First 1
    if (-not $window) {
        # The handle may belong to a popup that is still visible but not
        # enumerated above (e.g. PrintWindow fallback); use it directly.
        $hwnd = [IntPtr]$Handle
    }
} else {
    $window = $windows |
        Where-Object { $_ -match [regex]::Escape($TitleLike) } |
        Select-Object -First 1
}

if (-not $window -and $Handle -eq 0) {
    Write-Error ("No visible window matching '{0}' for process {1}. Available: {2}" -f
        $TitleLike, $ProcessId, ($windows -join '; '))
    exit 1
}

if ($window) {
    $parts = $window -split '\|'
    $hwnd = [IntPtr][int64]$parts[0]
}
$rect = New-Object AtollWindowCapture+RECT
[AtollWindowCapture]::GetWindowRect($hwnd, [ref]$rect) | Out-Null

$width = $rect.R - $rect.L
$height = $rect.B - $rect.T
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
$ok = [AtollWindowCapture]::PrintWindow($hwnd, $hdc, 2)
$graphics.ReleaseHdc($hdc)
$graphics.Dispose()
$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

"ok=$ok size=${width}x${height} path=$Out"

using System.Runtime.InteropServices;

namespace PitWall.Desktop;

internal static partial class NativeMethods
{
    public const int AttachParentProcess = -1;
    public const int SwMaximize = 3;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpShowWindow = 0x0040;
    private const int MdtEffectiveDpi = 0;
    private const uint MonitorDefaultToNearest = 2;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(int processId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hWnd, int command);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(POINT pt, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>Scale factor (1.0 = 96 DPI) of the monitor containing a physical-pixel point.</summary>
    public static double ScaleAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MonitorDefaultToNearest);
        return GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
    }
}

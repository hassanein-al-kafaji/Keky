using System;
using System.Runtime.InteropServices;

namespace KekyNotepad.Editor;

/// <summary>
/// The small set of Win32 calls used by the editor.  Keeping them here makes the
/// flicker-free highlighting code in <see cref="SyntaxHighlighter"/> easy to review.
/// </summary>
internal static class NativeMethods
{
    private const int WM_SETREDRAW = 0x000B;
    private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    private const int EM_LINESCROLL = 0x00B6;

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    public const int WM_NCHITTEST = 0x0084;
    public const int WM_GETMINMAXINFO = 0x0024;

    public const int HTCLIENT = 1;
    public const int HTLEFT = 10;
    public const int HTRIGHT = 11;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15;
    public const int HTBOTTOMLEFT = 16;
    public const int HTBOTTOMRIGHT = 17;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool HideCaret(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowCaret(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool LockWindowUpdate(IntPtr hWnd);

    /// <summary>Scrolls a RichEdit/edit control to the very end (WM_VSCROLL / SB_BOTTOM).</summary>
    public static void ScrollToBottom(IntPtr hWnd) => SendMessage(hWnd, 0x0115, new IntPtr(7), IntPtr.Zero);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? subAppName, string? subIdList);

    /// <summary>Gives a window dark (or normal) scroll bars that follow the app theme.</summary>
    public static void ApplyScrollBarTheme(IntPtr hWnd, bool dark)
    {
        try
        {
            SetWindowTheme(hWnd, dark ? "DarkMode_Explorer" : "Explorer", null);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    public static void SetRedraw(IntPtr hWnd, bool enabled)
        => SendMessage(hWnd, WM_SETREDRAW, enabled ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);

    /// <summary>
    /// Hides the blinking caret while the syntax pass moves the selection around
    /// the document. Without this the caret visibly jumps to the start of the
    /// text and back on every keystroke.
    /// </summary>
    public static void HideCaretSafe(IntPtr hWnd) => HideCaret(hWnd);

    public static void ShowCaretSafe(IntPtr hWnd) => ShowCaret(hWnd);

    public static int GetFirstVisibleLine(IntPtr hWnd)
        => (int)SendMessage(hWnd, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Positive values scroll down, negative values scroll up.</summary>
    public static void ScrollLines(IntPtr hWnd, int delta)
        => SendMessage(hWnd, EM_LINESCROLL, IntPtr.Zero, new IntPtr(delta));

    /// <summary>
    /// Hands the mouse over to the window manager so the borderless window can be
    /// dragged, snapped and double-click maximized like a normal window.
    /// </summary>
    public static void StartWindowDrag(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(hWnd, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
    }

    /// <summary>
    /// Keeps a maximized borderless window inside the monitor work area (that is,
    /// above the taskbar) instead of covering the whole screen.
    /// </summary>
    public static void ConstrainMaximizedBounds(IntPtr hWnd, IntPtr lParam)
    {
        if (hWnd == IntPtr.Zero || lParam == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return;
            }

            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return;
            }

            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            info.ptMaxPosition.X = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left;
            info.ptMaxPosition.Y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top;
            info.ptMaxSize.X = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
            info.ptMaxSize.Y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;
            Marshal.StructureToPtr(info, lParam, false);
        }
        catch
        {
            // Never let cosmetic window sizing break the app.
        }
    }

    /// <summary>
    /// Enables the immersive dark title bar on Windows 10/11. The API is optional,
    /// so every failure is deliberately ignored.
    /// </summary>
    public static void TrySetDarkTitleBar(IntPtr hWnd, bool dark)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hWnd, 20, ref value, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hWnd, 19, ref value, sizeof(int));
            }
        }
        catch
        {
            // Older versions of Windows do not implement this attribute.
        }
    }

    /// <summary>Windows 11 rounded corners, when the OS supports them.</summary>
    public static void TryUseRoundedCorners(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hWnd, 33, ref preference, sizeof(int));
        }
        catch
        {
            // Windows 10 and older simply ignore this call.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}

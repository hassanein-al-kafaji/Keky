using KekyNotepad.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>
/// Runs scrcpy and embeds its window inside this control (SetParent), so the phone
/// screen lives inside the editor instead of floating on the desktop.
/// </summary>
internal sealed class ScrcpyHost : Control
{
    private const int GWL_STYLE = -16;
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_SYSMENU = 0x00080000;
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private const uint WS_MAXIMIZEBOX = 0x00010000;
    private const uint WS_BORDER = 0x00800000;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);

    private double _aspect = 9.0 / 19.5;
    private double _zoom = 1.0;
    private string? _path;
    private string? _serial;
    private bool _screenOff;
    private int _launchHeight;
    private readonly System.Windows.Forms.Timer _resizeTimer = new() { Interval = 600 };
    private readonly System.Windows.Forms.Timer _findTimer = new() { Interval = 40 };
    private readonly List<string> _log = new();
    private Process? _process;
    private IntPtr _child = IntPtr.Zero;
    private int _findAttempts;
    private string _status = Loc.T("Device stopped");
    private AppTheme _theme = AppTheme.Dark;

    public ScrcpyHost()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _findTimer.Tick += FindTimer_Tick;
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            if (_path is not null && IsRunning && Math.Abs(DesiredHeight() - _launchHeight) > 4)
            {
                Start(_path, _serial);
            }
        };
    }

    public event EventHandler? StateChanged;

    public bool IsRunning => _process is { HasExited: false };

    public string Status => _status;

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            Invalidate();
        }
    }

    public void Start(string scrcpyPath, string? serial = null)
    {
        Stop();

        _path = scrcpyPath;
        _serial = serial;
        DeviceName = null;
        _log.Clear();
        _findAttempts = 0;
        var title = $"KekyScrcpy_{Environment.ProcessId}";

        var info = new ProcessStartInfo(scrcpyPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(scrcpyPath) ?? Environment.CurrentDirectory,
        };
        // Make scrcpy's window use real pixels, so it scales its picture with the window
        // instead of being DPI-virtualized (which cropped the phone).
        info.Environment["SDL_WINDOWS_DPI_AWARENESS"] = "permonitorv2";
        info.Environment["SDL_WINDOWS_DPI_SCALING"] = "0";
        info.ArgumentList.Add("--window-title");
        info.ArgumentList.Add(title);
        info.ArgumentList.Add("--window-borderless");
        info.ArgumentList.Add("--window-x");
        info.ArgumentList.Add("-32000");
        info.ArgumentList.Add("--window-y");
        info.ArgumentList.Add("-32000");
        if (_screenOff)
        {
            info.ArgumentList.Add("--turn-screen-off");
        }

        if (!string.IsNullOrEmpty(serial))
        {
            info.ArgumentList.Add("-s");
            info.ArgumentList.Add(serial);
        }

        _launchHeight = DesiredHeight();
        info.ArgumentList.Add("--window-height");
        info.ArgumentList.Add(_launchHeight.ToString());

        try
        {
            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => AddLog(e.Data);
            process.ErrorDataReceived += (_, e) => AddLog(e.Data);
            process.Exited += (_, _) => OnProcessExited(process);
            process.Start();
            ChildProcessJob.Add(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            SetStatus($"Could not start scrcpy: {exception.Message}");
            return;
        }

        SetStatus(Loc.T("Connecting to the device..."));
        _findTimer.Start();
    }

    public void Stop()
    {
        _findTimer.Stop();
        _resizeTimer.Stop();

        var process = _process;
        _process = null;
        _child = IntPtr.Zero;

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // Already gone.
            }

            process.Dispose();
        }

        DeviceName = null;
        SetStatus(Loc.T("Device stopped"));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FitChild();
        EnsureSize();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_child != IntPtr.Zero)
        {
            SetFocus(_child);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var back = new SolidBrush(_theme.EditorBack))
        {
            e.Graphics.FillRectangle(back, ClientRectangle);
        }

        if (_child != IntPtr.Zero)
        {
            return;
        }

        TextRenderer.DrawText(
            e.Graphics,
            _status,
            Font,
            Rectangle.Inflate(ClientRectangle, -16, -16),
            _theme.DisabledFore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _findTimer.Dispose();
            _resizeTimer.Dispose();
            Stop();
        }

        base.Dispose(disposing);
    }

    /// <summary>Turns the phone's own screen off while mirroring (restarts scrcpy).</summary>
    public bool ScreenOff
    {
        get => _screenOff;
        set
        {
            if (_screenOff == value)
            {
                return;
            }

            _screenOff = value;
            Restart();
        }
    }

    /// <summary>Restarts scrcpy for the same device (e.g. after a rotation).</summary>
    public void Restart()
    {
        if (_path is not null && IsRunning)
        {
            Start(_path, _serial);
        }
    }

    public string? DeviceName { get; private set; }

    private void AddLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var marker = line.IndexOf("INFO: Device:", StringComparison.Ordinal);
        if (marker >= 0)
        {
            var name = line[(marker + 13)..].Trim();
            var close = name.IndexOf(']');
            if (name.StartsWith('[') && close > 0)
            {
                var maker = name[1..close];
                var model = name[(close + 1)..].Trim();
                name = model.StartsWith(maker, StringComparison.OrdinalIgnoreCase) ? model : maker + " " + model;
            }

            var paren = name.LastIndexOf(" (Android", StringComparison.Ordinal);
            if (paren > 0)
            {
                name = name[..paren];
            }

            DeviceName = name;
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() => StateChanged?.Invoke(this, EventArgs.Empty));
            }
        }

        lock (_log)
        {
            _log.Add(line.Trim());
            if (_log.Count > 30)
            {
                _log.RemoveAt(0);
            }
        }
    }

    private void OnProcessExited(Process process)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (!ReferenceEquals(process, _process))
                {
                    return; // stopped on purpose
                }

                _findTimer.Stop();
                _child = IntPtr.Zero;
                _process = null;

                string reason;
                lock (_log)
                {
                    reason = string.Join(
                        "\n",
                        _log.Where(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)).TakeLast(2));
                    if (reason.Length == 0)
                    {
                        reason = string.Join("\n", _log.TakeLast(2));
                    }
                }

                SetStatus(reason.Length > 0 ? $"scrcpy stopped\n\n{reason}" : "scrcpy stopped");
            });
        }
        catch (InvalidOperationException)
        {
            // The control is going away.
        }
    }

    private void FindTimer_Tick(object? sender, EventArgs e)
    {
        var process = _process;
        if (process is null || process.HasExited)
        {
            _findTimer.Stop();
            return;
        }

        if (++_findAttempts > 750)
        {
            _findTimer.Stop();
            SetStatus("scrcpy did not open a window. Is the device connected and USB debugging enabled?");
            return;
        }

        var found = FindWindow(process.Id);
        if (found == IntPtr.Zero)
        {
            return;
        }

        _findTimer.Stop();
        Embed(found);
    }

    private static IntPtr FindWindow(int processId)
    {
        var result = IntPtr.Zero;
        EnumWindows(
            (hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out var pid);
                if (pid == (uint)processId && IsWindowVisible(hWnd))
                {
                    result = hWnd;
                    return false;
                }

                return true;
            },
            IntPtr.Zero);
        return result;
    }

    private void Embed(IntPtr window)
    {
        if (GetClientRect(window, out var original) && original.Right > 50 && original.Bottom > 50)
        {
            _aspect = (double)original.Right / original.Bottom;
        }

        var style = (ulong)GetWindowLongPtr(window, GWL_STYLE).ToInt64();
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_POPUP | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_BORDER);
        style |= WS_CHILD | WS_VISIBLE;
        SetWindowLongPtr(window, GWL_STYLE, new IntPtr((long)style));
        SetParent(window, Handle);

        _child = window;
        FitChild();
        SetFocus(window);
        EnsureSize();
        SetStatus(Loc.T("Connected"));
    }

    private void FitChild()
    {
        if (_child == IntPtr.Zero)
        {
            return;
        }

        if (!IsWindow(_child))
        {
            _child = IntPtr.Zero;
            Invalidate();
            return;
        }

        // Never resize scrcpy's window (it would crop instead of scale): just center it.
        // The size is chosen when scrcpy is launched (see EnsureSize).
        var width = 0;
        if (GetClientRect(_child, out var rect))
        {
            width = rect.Right;
        }

        var x = Math.Max(0, (ClientSize.Width - width) / 2);
        SetWindowPos(_child, IntPtr.Zero, x, 0, 0, 0, SWP_NOZORDER | SWP_NOSIZE | SWP_SHOWWINDOW);
    }

    private int DesiredHeight()
    {
        var byHeight = ClientSize.Height * _zoom;
        var byWidth = ClientSize.Width / _aspect;
        return Math.Max(100, (int)Math.Min(byHeight, byWidth));
    }

    /// <summary>Restarts scrcpy (debounced) when the phone should have a different size.</summary>
    private void EnsureSize()
    {
        if (_path is null || !IsRunning || _child == IntPtr.Zero || ClientSize.Height < 50)
        {
            return;
        }

        if (Math.Abs(DesiredHeight() - _launchHeight) > 4)
        {
            _resizeTimer.Stop();
            _resizeTimer.Start();
        }
    }

    /// <summary>Height of the phone as a fraction of the available height (0.3 - 1.0).</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, 0.3, 1.0);
            EnsureSize();
        }
    }

    private void SetStatus(string status)
    {
        _status = status;
        Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

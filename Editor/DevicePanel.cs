using KekyNotepad.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>
/// Fixed full-height panel on the right side. Phones are detected automatically (adb);
/// every connected device gets its own scrcpy view, side by side. The left edge can be dragged.
/// </summary>
internal sealed class DevicePanel : Panel
{
    private const int MinViewWidth = 230;
    private const string WaitingText = "Looking for a phone...\nConnect it with USB and enable USB debugging.";

    private readonly Panel _grip = new();
    private readonly TableLayoutPanel _views = new();
    private readonly Label _empty = new();
    private readonly List<DeviceView> _viewList = new();
    private readonly HashSet<string> _manualStops = new();
    private readonly DeviceMonitor _monitor;
    private AppTheme _theme = AppTheme.Dark;
    private string? _scrcpyPath;
    private string? _resolvedPath;
    private bool _started;
    private int _dragStart;
    private int _dragSize;

    public DevicePanel()
    {
        Dock = DockStyle.Right;
        Width = 420;
        MinimumSize = new Size(160, 0);
        Visible = false;

        _monitor = new DeviceMonitor(this);
        _monitor.DevicesChanged += (_, devices) => SyncDevices(devices);

        _grip.Dock = DockStyle.Left;
        _grip.Width = 5;
        _grip.Cursor = Cursors.SizeWE;
        _grip.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragStart = Cursor.Position.X;
                _dragSize = Width;
            }
        };
        _grip.MouseMove += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                var limit = Parent is null ? 900 : Math.Max(MinimumSize.Width, Parent.ClientSize.Width - 240);
                Width = Math.Clamp(_dragSize - (Cursor.Position.X - _dragStart), MinimumSize.Width, limit);
            }
        };
        _grip.Paint += (_, e) =>
        {
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, 0, _grip.Height);
        };

        _views.Dock = DockStyle.Fill;
        _views.RowCount = 1;
        _views.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _views.Margin = Padding.Empty;

        _empty.Dock = DockStyle.Fill;
        _empty.TextAlign = ContentAlignment.MiddleCenter;
        _empty.Padding = new Padding(16);

        // Controls dock in reverse order of adding; the Fill controls go first.
        Controls.Add(_views);
        Controls.Add(_empty);
        Controls.Add(_grip);
        ShowEmpty(Loc.T(WaitingText));
    }

    public event EventHandler<string>? ScrcpyPathChanged;

    /// <summary>Asks the host to run this command line in the terminal (logcat button).</summary>
    public event EventHandler<string>? LogcatRequested;

    public string? ScrcpyPath
    {
        get => _scrcpyPath;
        set => _scrcpyPath = value;
    }

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            BackColor = value.EditorBack;
            _grip.BackColor = value.ToolBack;
            _views.BackColor = value.EditorBack;
            _empty.BackColor = value.EditorBack;
            _empty.ForeColor = value.DisabledFore;
            foreach (var view in _viewList)
            {
                view.Theme = value;
            }

            _grip.Invalidate();
        }
    }

    /// <summary>Called when the panel is shown: starts looking for phones.</summary>
    public void Open()
    {
        Visible = true;
        if (_started)
        {
            return;
        }

        var path = ResolveScrcpy();
        if (path is null)
        {
            ShowEmpty(Loc.T("scrcpy.exe was not found.\nReopen this tab to choose its location."));
            return;
        }

        _started = true;
        _resolvedPath = path;
        _monitor.Start(path);
        if (!_monitor.HasAdb)
        {
            // No adb next to scrcpy: fall back to one view that lets scrcpy pick the device.
            AddView(null, "Device", path);
        }
    }

    public void Close() => Visible = false;

    public void ApplyLanguage()
    {
        if (_viewList.Count == 0 && _empty.Visible)
        {
            _empty.Text = Loc.T(WaitingText);
        }

        foreach (var view in _viewList)
        {
            view.ApplyLanguage();
        }
    }

    public void Shutdown()
    {
        _monitor.Stop();
        foreach (var view in _viewList.ToList())
        {
            view.Host.Stop();
        }
    }

    private void ShowEmpty(string text)
    {
        _empty.Text = text;
        _empty.Visible = _viewList.Count == 0;
        _views.Visible = _viewList.Count > 0;
    }

    private void SyncDevices(IReadOnlyList<AdbDevice> devices)
    {
        var path = _resolvedPath;
        if (path is null || IsDisposed)
        {
            return;
        }

        var serials = devices.Select(d => d.Serial).ToHashSet();

        foreach (var view in _viewList.Where(v => v.Serial is not null && !serials.Contains(v.Serial)).ToList())
        {
            RemoveView(view);
        }

        _manualStops.RemoveWhere(s => !serials.Contains(s));

        foreach (var device in devices)
        {
            if (_viewList.All(v => v.Serial != device.Serial))
            {
                AddView(device.Serial, device.Name, path);
            }
        }

        if (_viewList.Count == 0)
        {
            ShowEmpty(Loc.T(WaitingText));
        }
    }

    private void AddView(string? serial, string name, string path)
    {
        var view = new DeviceView(serial, name, path, _monitor.AdbPath) { Theme = _theme };
        view.LogcatRequested += (_, _) =>
        {
            if (view.AdbPath is not null)
            {
                var target = view.Serial is null ? string.Empty : $"-s {view.Serial} ";
                LogcatRequested?.Invoke(this, $"\"{view.AdbPath}\" {target}logcat -v time -T 1 *:I");
            }
        };
        view.Stopped += (_, _) =>
        {
            if (view.Serial is not null)
            {
                _manualStops.Add(view.Serial);
            }
        };

        _viewList.Add(view);
        Relayout();
        EnsureWidth();
        ShowEmpty(string.Empty);

        if (serial is null || !_manualStops.Contains(serial))
        {
            view.Host.Start(path, serial);
        }
    }

    private void RemoveView(DeviceView view)
    {
        _viewList.Remove(view);
        _views.Controls.Remove(view);
        view.Host.Stop();
        view.Dispose();
        Relayout();
        ShowEmpty(string.Empty);
    }

    private void Relayout()
    {
        _views.SuspendLayout();
        _views.Controls.Clear();
        _views.ColumnStyles.Clear();
        _views.ColumnCount = Math.Max(1, _viewList.Count);
        for (var i = 0; i < _viewList.Count; i++)
        {
            _views.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / _viewList.Count));
            _views.Controls.Add(_viewList[i], i, 0);
        }

        _views.ResumeLayout(true);
    }

    /// <summary>Makes room so every phone gets a usable width.</summary>
    private void EnsureWidth()
    {
        var needed = (int)Math.Round(MinViewWidth * DeviceDpi / 96.0) * _viewList.Count;
        var limit = Parent is null ? 1600 : Math.Max(MinimumSize.Width, Parent.ClientSize.Width - 200);
        if (Width < needed)
        {
            Width = Math.Min(needed, limit);
        }
    }

    private string? ResolveScrcpy()
    {
        // scrcpy (with adb) ships inside the app folder, so nothing has to be installed.
        var bundled = Path.Combine(AppContext.BaseDirectory, "scrcpy", "scrcpy.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        if (!string.IsNullOrWhiteSpace(_scrcpyPath) && File.Exists(_scrcpyPath))
        {
            return _scrcpyPath;
        }

        var fromPath = FindOnPath("scrcpy.exe");
        if (fromPath is not null)
        {
            return fromPath;
        }

        using var dialog = new OpenFileDialog
        {
            Title = Loc.T("Locate scrcpy.exe"),
            Filter = "scrcpy|scrcpy.exe|Programs|*.exe",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return null;
        }

        _scrcpyPath = dialog.FileName;
        ScrcpyPathChanged?.Invoke(this, dialog.FileName);
        return _scrcpyPath;
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry.
            }
        }

        return null;
    }
}

using KekyNotepad.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>
/// One phone: a header (device name + Start/Stop), a slim toolbar (screenshot, rotate,
/// navigation keys, screen off, Wi-Fi, logcat, install APK) and its scrcpy view.
/// </summary>
internal sealed class DeviceView : Panel
{
    private readonly Panel _header = new();
    private readonly Label _title = new();
    private readonly Button _toggle = new();
    private readonly FlowLayoutPanel _bar = new();
    private readonly List<(IconButton Button, string Text)> _tipSources = new();
    private readonly ToolTip _tips = new() { ShowAlways = true, InitialDelay = 250, ReshowDelay = 100, AutoPopDelay = 6000 };
    private readonly string _path;
    private readonly AdbClient? _adb;
    private IconButton? _screenOffButton;
    private IconButton? _recordButton;
    private System.Diagnostics.Process? _recording;
    private const string RemoteRecording = "/sdcard/keky_recording.mp4";
    private AppTheme _theme = AppTheme.Dark;
    private bool _busy;

    public DeviceView(string? serial, string name, string path, string? adbPath)
    {
        Serial = serial;
        _path = path;
        _adb = adbPath is null ? null : new AdbClient(adbPath, serial);
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        AllowDrop = true;

        _header.Dock = DockStyle.Top;
        _header.Height = 38;

        _title.Text = name;
        _title.AutoSize = false;
        _title.Dock = DockStyle.Fill;
        _title.AutoEllipsis = true;
        _title.TextAlign = ContentAlignment.MiddleLeft;
        _title.Padding = new Padding(12, 0, 6, 0);

        _toggle.Dock = DockStyle.Right;
        _toggle.Width = 80;
        _toggle.FlatStyle = FlatStyle.Flat;
        _toggle.UseVisualStyleBackColor = false;
        _toggle.Click += (_, _) => Toggle();

        _header.Controls.Add(_title);
        _header.Controls.Add(_toggle);
        _title.BringToFront();

        _bar.Dock = DockStyle.Top;
        _bar.AutoSize = true;
        _bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _bar.MinimumSize = new Size(0, 34);
        _bar.WrapContents = true;
        _bar.Padding = new Padding(6, 2, 6, 2);

        if (_adb is not null)
        {
            AddButton("", "Screenshot (saved + copied)", async () => await TakeScreenshot());
            AddButton("", "Rotate screen", async () => await Rotate());
            AddButton("", "Back", () => _ = _adb.KeyAsync(4));
            AddButton("", "Home", () => _ = _adb.KeyAsync(3));
            AddButton("", "Recent apps", () => _ = _adb.KeyAsync(187));
            _screenOffButton = AddButton("", "Turn the phone's own screen off while mirroring", ToggleScreenOff);
            if (!string.IsNullOrEmpty(serial) && !serial.Contains(':'))
            {
                AddButton("", "Switch to Wi-Fi (then unplug the cable)", async () => await GoWireless());
            }

            AddButton("", "Show logcat in the terminal", () => LogcatRequested?.Invoke(this, EventArgs.Empty));
            AddButton("", "Install an APK", async () => await InstallApk());
            AddButton("\uE898", "Send files to the phone (APKs are installed)", async () => await SendFilesDialog());
            _recordButton = AddButton("\uE7C8", "Record the phone screen", () => _ = ToggleRecording());
        }

        Host.Dock = DockStyle.Fill;
        Host.StateChanged += (_, _) => UpdateState();

        Controls.Add(Host);
        Controls.Add(_bar);
        Controls.Add(_header);
        Host.BringToFront();
        UpdateState();
    }

    public string? Serial { get; }

    public ScrcpyHost Host { get; } = new();

    public event EventHandler? Stopped;

    /// <summary>Raised when the logcat button is pressed; the panel forwards it to the terminal.</summary>
    public event EventHandler? LogcatRequested;

    public string? AdbPath => _adb?.AdbPath;

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            BackColor = value.EditorBack;
            _header.BackColor = value.ToolBack;
            _title.BackColor = value.ToolBack;
            _title.ForeColor = value.TabActiveFore;
            _bar.BackColor = value.ToolBack;
            foreach (Control control in _bar.Controls)
            {
                if (control is IconButton button)
                {
                    button.Theme = value;
                }
            }

            Host.Theme = value;
            UpdateState();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tips.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Re-applies the interface language to tooltips and texts.</summary>
    public void ApplyLanguage()
    {
        foreach (var (button, text) in _tipSources)
        {
            _tips.SetToolTip(button, Loc.T(text));
        }

        UpdateState();
    }

    private IconButton AddButton(string glyph, string tip, Action action)
    {
        var button = new IconButton { Glyph = glyph, Size = new Size(30, 30), Margin = new Padding(1, 0, 1, 0) };
        button.Click += (_, _) => action();
        _tipSources.Add((button, tip));
        _tips.SetToolTip(button, Loc.T(tip));
        _bar.Controls.Add(button);
        return button;
    }

    private void Toggle()
    {
        if (Host.IsRunning)
        {
            Host.Stop();
            Stopped?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Host.Start(_path, Serial);
        }
    }

    private void ToggleScreenOff()
    {
        Host.ScreenOff = !Host.ScreenOff;
        if (_screenOffButton is not null)
        {
            _screenOffButton.Active = Host.ScreenOff;
        }
    }

    private async System.Threading.Tasks.Task TakeScreenshot()
    {
        if (_adb is null || !BeginBusy())
        {
            return;
        }

        try
        {
            var bytes = await _adb.ScreenshotAsync();
            if (bytes is null)
            {
                Notify(Loc.T("Could not take a screenshot."), MessageBoxIcon.Warning);
                return;
            }

            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "KekyNotepad");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"phone-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            await File.WriteAllBytesAsync(file, bytes);

            try
            {
                using var stream = new MemoryStream(bytes);
                using var image = Image.FromStream(stream);
                Clipboard.SetImage(new Bitmap(image));
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException or ArgumentException)
            {
                // Clipboard busy: the file is still saved.
            }

            Notify(Loc.F("Screenshot saved and copied:\n{0}", file), MessageBoxIcon.Information);
        }
        finally
        {
            _busy = false;
        }
    }

    private async System.Threading.Tasks.Task Rotate()
    {
        if (_adb is null || !BeginBusy())
        {
            return;
        }

        try
        {
            await _adb.ShellAsync("settings put system accelerometer_rotation 0");
            var (_, current) = await _adb.ShellAsync("settings get system user_rotation");
            var next = int.TryParse(current.Trim(), out var value) && value == 0 ? 1 : 0;
            await _adb.ShellAsync($"settings put system user_rotation {next}");
            await System.Threading.Tasks.Task.Delay(700);

            // The picture changes shape: scrcpy has to restart to fit the new proportions.
            Host.Restart();
        }
        finally
        {
            _busy = false;
        }
    }

    private async System.Threading.Tasks.Task GoWireless()
    {
        if (_adb is null || !BeginBusy())
        {
            return;
        }

        try
        {
            var address = await _adb.WifiAddressAsync();
            if (address is null)
            {
                Notify(Loc.T("Could not find the phone's Wi-Fi address. Is Wi-Fi connected on the phone?"), MessageBoxIcon.Warning);
                return;
            }

            var (okTcp, tcpOutput) = await _adb.RunAsync("tcpip", "5555");
            if (!okTcp)
            {
                Notify(Loc.F("adb tcpip failed:\n{0}", tcpOutput), MessageBoxIcon.Warning);
                return;
            }

            await System.Threading.Tasks.Task.Delay(1500);
            var global = new AdbClient(_adb.AdbPath, null);
            var (okConnect, connectOutput) = await global.RunAsync("connect", $"{address}:5555");
            Notify(
                okConnect
                    ? Loc.F("{0}\n\nYou can unplug the cable now. The Wi-Fi device appears as a new view.", connectOutput)
                    : Loc.F("adb connect failed:\n{0}", connectOutput),
                okConnect ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
        }
    }

    private async System.Threading.Tasks.Task InstallApk()
    {
        if (_adb is null || !BeginBusy())
        {
            return;
        }

        try
        {
            using var dialog = new OpenFileDialog { Title = Loc.T("Install APK"), Filter = Loc.T("Android package") + "|*.apk", CheckFileExists = true };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }

            _title.Text = Loc.T("Installing...");
            var (ok, output) = await _adb.RunAsync("install", "-r", dialog.FileName);
            Notify(ok ? Loc.T("APK installed.") : Loc.F("Install failed:\n{0}", output), ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
            UpdateState();
        }
    }

    // ------------------------------------------------------------ files and recording

    private async System.Threading.Tasks.Task SendFilesDialog()
    {
        using var dialog = new OpenFileDialog { Title = Loc.T("Send files to the phone (APKs are installed)"), Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            await SendFiles(dialog.FileNames);
        }
    }

    /// <summary>APK files are installed; everything else goes to the phone's Download folder.</summary>
    private async System.Threading.Tasks.Task SendFiles(string[] files)
    {
        if (_adb is null || files.Length == 0 || !BeginBusy())
        {
            return;
        }

        try
        {
            var sent = 0;
            var failures = new System.Text.StringBuilder();
            foreach (var file in files)
            {
                var isApk = string.Equals(Path.GetExtension(file), ".apk", StringComparison.OrdinalIgnoreCase);
                var (ok, output) = isApk
                    ? await _adb.RunAsync("install", "-r", file)
                    : await _adb.RunAsync("push", file, "/sdcard/Download/");
                if (ok)
                {
                    sent++;
                }
                else
                {
                    failures.AppendLine($"{Path.GetFileName(file)}: {output}");
                }
            }

            Notify(
                failures.Length == 0
                    ? Loc.F("Sent {0} file(s) to the phone.", sent)
                    : Loc.F("Sent {0} file(s). Failed:\n{1}", sent, failures.ToString().Trim()),
                failures.Length == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
        }
    }

    protected override void OnDragEnter(DragEventArgs drgevent)
    {
        base.OnDragEnter(drgevent);
        drgevent.Effect = _adb is not null && drgevent.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs drgevent)
    {
        base.OnDragDrop(drgevent);
        if (drgevent.Data?.GetData(DataFormats.FileDrop) is string[] files)
        {
            _ = SendFiles(files);
        }
    }

    private async System.Threading.Tasks.Task ToggleRecording()
    {
        if (_adb is null)
        {
            return;
        }

        if (_recording is not null)
        {
            // Ask the phone's screenrecord to finish properly (SIGINT), the exit handler pulls the file.
            await _adb.ShellAsync("kill -2 $(pidof screenrecord)");
            return;
        }

        var process = _adb.StartLongRunning("shell", "screenrecord", "--time-limit", "180", RemoteRecording);
        if (process is null)
        {
            Notify(Loc.T("Could not start recording."), MessageBoxIcon.Warning);
            return;
        }

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(() => _ = FinishRecording());
            }
        };

        _recording = process;
        if (_recordButton is not null)
        {
            _recordButton.Active = true;
            _tips.SetToolTip(_recordButton, Loc.T("Stop recording"));
        }
    }

    private async System.Threading.Tasks.Task FinishRecording()
    {
        var process = _recording;
        _recording = null;
        if (_recordButton is not null)
        {
            _recordButton.Active = false;
            _tips.SetToolTip(_recordButton, Loc.T("Record the phone screen"));
        }

        process?.Dispose();
        if (_adb is null)
        {
            return;
        }

        // Give the phone a moment to finalize the mp4 before pulling it.
        await System.Threading.Tasks.Task.Delay(1200);
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "KekyNotepad");
        Directory.CreateDirectory(folder);
        var local = Path.Combine(folder, $"phone-{DateTime.Now:yyyyMMdd-HHmmss}.mp4");
        var (ok, output) = await _adb.RunAsync("pull", RemoteRecording, local);
        await _adb.ShellAsync("rm " + RemoteRecording);
        Notify(ok ? Loc.F("Recording saved:\n{0}", local) : Loc.F("Could not save the recording.\n{0}", output), ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private bool BeginBusy()
    {
        if (_busy)
        {
            return false;
        }

        _busy = true;
        return true;
    }

    private void Notify(string text, MessageBoxIcon icon)
    {
        if (IsDisposed)
        {
            return;
        }

        var owner = FindForm();
        if (owner is null)
        {
            return;
        }

        owner.BeginInvoke(() => ThemedMessageBox.Show(owner, _theme, text, "Device", MessageBoxButtons.OK, icon));
    }

    private void UpdateState()
    {
        _toggle.Text = Loc.T(Host.IsRunning ? "Stop" : "Start");
        if (!string.IsNullOrWhiteSpace(Host.DeviceName))
        {
            _title.Text = Host.DeviceName;
        }

        _toggle.ForeColor = _theme.MenuFore;
        _toggle.BackColor = _theme.ToolBack;
        _toggle.FlatAppearance.BorderSize = 0;
        _toggle.FlatAppearance.MouseOverBackColor = _theme.MenuHover;
        _toggle.FlatAppearance.MouseDownBackColor = _theme.TabHoverBack;
    }
}

/// <summary>Flat icon button drawn with the Segoe MDL2 Assets glyph font, themed.</summary>
internal sealed class IconButton : Control
{
    private AppTheme _theme = AppTheme.Dark;
    private bool _hover;
    private bool _active;

    public IconButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Cursor = Cursors.Hand;
    }

    public string Glyph { get; set; } = string.Empty;

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            Invalidate();
        }
    }

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(_theme.ToolBack);

        if (_hover || _active)
        {
            using var brush = new SolidBrush(_active ? _theme.TabHoverBack : _theme.MenuHover);
            e.Graphics.FillRectangle(brush, new Rectangle(2, 2, Width - 4, Height - 4));
        }

        using var font = new Font("Segoe MDL2 Assets", 11f, FontStyle.Regular, GraphicsUnit.Point);
        TextRenderer.DrawText(
            e.Graphics,
            Glyph,
            font,
            ClientRectangle,
            _hover || _active ? _theme.TabActiveFore : _theme.TabInactiveFore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

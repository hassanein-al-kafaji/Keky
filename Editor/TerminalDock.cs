using System;
using System.Drawing;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>Terminal docked under the editor, with a draggable top edge.</summary>
internal sealed class TerminalDock : Panel
{
    private readonly Panel _grip = new();
    private readonly TerminalControl _terminal = new();
    private AppTheme _theme = AppTheme.Dark;
    private int _dragStart;
    private int _dragSize;

    public TerminalDock()
    {
        Dock = DockStyle.Bottom;
        Height = 220;
        Visible = false;

        _grip.Dock = DockStyle.Top;
        _grip.Height = 5;
        _grip.Cursor = Cursors.SizeNS;
        _grip.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragStart = Cursor.Position.Y;
                _dragSize = Height;
            }
        };
        _grip.MouseMove += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                var limit = Parent is null ? 600 : Math.Max(100, Parent.ClientSize.Height - 140);
                Height = Math.Clamp(_dragSize - (Cursor.Position.Y - _dragStart), 80, limit);
            }
        };
        _grip.Paint += (_, e) =>
        {
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, _grip.Width, 0);
        };

        _terminal.Dock = DockStyle.Fill;
        Controls.Add(_terminal);
        Controls.Add(_grip);
        _terminal.BringToFront();
    }

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            BackColor = value.EditorBack;
            _grip.BackColor = value.ToolBack;
            _terminal.Theme = value;
            _grip.Invalidate();
        }
    }

    public bool ShowGrip
    {
        get => _grip.Visible;
        set => _grip.Visible = value;
    }

    public void FocusInput() => _terminal.FocusInput();

    public float FontSize
    {
        get => _terminal.FontSize;
        set => _terminal.FontSize = value;
    }

    public void Execute(string command) => _terminal.Execute(command);

    public bool UsePowerShell
    {
        get => _terminal.UsePowerShell;
        set => _terminal.UsePowerShell = value;
    }

    public void SetWorkingDirectory(string? directory) => _terminal.SetWorkingDirectory(directory);

    public void Open()
    {
        Visible = true;
        _terminal.EnsureStarted();
    }

    public void Close() => Visible = false;

    public void Shutdown() => _terminal.Stop();
}

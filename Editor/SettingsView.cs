using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using KekyNotepad.Services;

namespace KekyNotepad.Editor;

/// <summary>Everything the settings page shows; the form fills it in from its own state.</summary>
internal sealed class SettingsSnapshot
{
    public string Language { get; init; } = "en";
    public string ThemeKey { get; init; } = "dark";
    public string FontName { get; init; } = string.Empty;
    public float FontSize { get; init; }
    public int Direction { get; init; }
    public bool WordWrap { get; init; }
    public bool LineNumbers { get; init; }
    public string SyntaxName { get; init; } = string.Empty;
    public bool HasDocument { get; init; }
    public bool DeviceOpen { get; init; }
    public bool PowerShell { get; init; }
    public bool AutoSave { get; init; }
    public string? ScrcpyPath { get; init; }
}

/// <summary>
/// Settings page shown as a tab next to the documents: grouped cards, each row with a title,
/// a short description and its control. It owns no state: every change is reported through
/// <see cref="Command"/> and the form calls <see cref="Show"/> again.
/// </summary>
internal sealed class SettingsView : Panel
{
    private readonly FlowLayoutPanel _flow = new();
    private FlowLayoutPanel _cols = new();
    private FlowLayoutPanel _colA = new();
    private FlowLayoutPanel _colB = new();
    private readonly List<(Control Title, Control Card, int Col)> _sections = new();
    private readonly List<Action<int>> _sizers = new();
    private int _nextCol;
    private int _arrangedWidth = -1;
    private bool _arrangedTwoCol;
    private AppTheme _theme = AppTheme.Dark;
    private SettingsSnapshot _snapshot = new();
    private readonly List<Action<SettingsSnapshot>> _refreshers = new();
    private readonly List<TableLayoutPanel> _tables = new();
    private string? _builtKey;

    public SettingsView()
    {
        AutoScroll = true;
        Dock = DockStyle.Fill;
        Visible = false;
        DoubleBuffered = true;

        _flow.FlowDirection = FlowDirection.TopDown;
        _flow.WrapContents = false;
        _flow.AutoSize = true;
        _flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _flow.Dock = DockStyle.None;
        Controls.Add(_flow);
        Resize += (_, _) =>
        {
            Arrange(false);
            CenterContent();
        };
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && IsHandleCreated)
        {
            // The first layout can run before the panel has its final size: arrange again afterwards.
            BeginInvoke(new Action(() =>
            {
                Arrange(true);
                CenterContent();
            }));
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hWnd, int bar, bool show);

    /// <summary>One column on narrow windows, two columns when the window is wide.</summary>
    private void Arrange(bool force)
    {
        if (_sections.Count == 0 || ClientSize.Width <= 0)
        {
            return;
        }

        var available = ClientSize.Width - Px(56);
        var gap = Px(24);
        var twoCol = available >= Px(1180);
        var width = twoCol
            ? Math.Min(Px(620), (available - gap) / 2)
            : Math.Clamp(available, Px(420), Px(860));

        if (!force && width == _arrangedWidth && twoCol == _arrangedTwoCol)
        {
            return;
        }

        _arrangedWidth = width;
        _arrangedTwoCol = twoCol;
        SuspendLayout();
        _cols.SuspendLayout();
        _colA.SuspendLayout();
        _colB.SuspendLayout();

        _colA.Controls.Clear();
        _colB.Controls.Clear();
        foreach (var (title, card, col) in _sections)
        {
            var target = twoCol && col == 1 ? _colB : _colA;
            target.Controls.Add(title);
            target.Controls.Add(card);
            card.MinimumSize = new Size(width, 0);
            card.MaximumSize = new Size(width, 0);
        }

        _colB.Visible = twoCol;
        _colB.Margin = new Padding(twoCol ? gap : 0, 0, 0, 0);
        foreach (var sizer in _sizers)
        {
            sizer(width);
        }

        _colA.ResumeLayout(true);
        _colB.ResumeLayout(true);
        _cols.ResumeLayout(true);
        ResumeLayout(true);

        // Descriptions re-wrap when the card width changes: re-measure every card, then the page,
        // so no card is cut off and the scroll range matches the real content height.
        foreach (var table in _tables)
        {
            table.PerformLayout();
        }

        // Fix every card to its measured height (auto-size alone left the last row cut off).
        foreach (var (_, card, _) in _sections)
        {
            if (card.Controls.Count > 0 && card.Controls[0] is TableLayoutPanel table)
            {
                var inner = width - card.Padding.Horizontal;
                var height = table.GetPreferredSize(new Size(inner, 0)).Height;
                card.AutoSize = false;
                card.Size = new Size(width, height + card.Padding.Vertical);
            }

            card.PerformLayout();
        }

        _colA.PerformLayout();
        _colB.PerformLayout();
        _cols.PerformLayout();
        _flow.PerformLayout();
        AutoScrollMinSize = new Size(0, _flow.Height);
        if (IsHandleCreated)
        {
            ShowScrollBar(Handle, 0, false); // never a horizontal scrollbar: the content always fits
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.ApplyScrollBarTheme(Handle, _theme.IsDark);
    }

    // WS_EX_COMPOSITED: children are painted into one buffer and shown together, so a rebuilt
    // page never appears half-drawn (empty cards first, content later).
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x02000000;
            return parameters;
        }
    }

    /// <summary>(command name, value, the control that raised it).</summary>
    public event Action<string, int, Control>? Command;

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            _builtKey = null;
            if (IsHandleCreated)
            {
                NativeMethods.ApplyScrollBarTheme(Handle, _theme.IsDark);
            }
            if (Visible)
            {
                Rebuild();
            }
        }
    }

    /// <summary>Shows the page with the given state (re-creates the controls).</summary>
    public void Show(SettingsSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (_builtKey == KeyFor(snapshot))
        {
            // Same structure (language, theme, tabs): just move the values, no re-creation.
            foreach (var refresh in _refreshers)
            {
                refresh(snapshot);
            }
        }
        else
        {
            Rebuild();
        }
    }

    private string KeyFor(SettingsSnapshot snapshot) => $"{Loc.Language}|{_theme.Key}|{snapshot.HasDocument}|{DeviceDpi}";

    private void CenterContent()
    {
        var left = Math.Max(0, (ClientSize.Width - _flow.Width) / 2);
        _flow.Location = new Point(left, _flow.Top);
    }

    private void Rebuild()
    {
        var scroll = -AutoScrollPosition.Y;
        _refreshers.Clear();
        _tables.Clear();
        _sections.Clear();
        _sizers.Clear();
        _arrangedWidth = -1;
        _builtKey = KeyFor(_snapshot);
        // Freeze painting while the page is re-created, then repaint once (no flicker).
        var frozen = IsHandleCreated && Visible && NativeMethods.LockWindowUpdate(Handle);
        SuspendLayout();
        _flow.SuspendLayout();

        // Remove the old page first: flipping RightToLeft while it still exists would rebuild the
        // window handle of every old control (that was the multi-second pause on language change).
        var oldControls = new Control[_flow.Controls.Count];
        _flow.Controls.CopyTo(oldControls, 0);
        _flow.Controls.Clear();
        foreach (var control in oldControls)
        {
            control.Dispose();
        }

        RightToLeft = Loc.IsArabic ? RightToLeft.Yes : RightToLeft.No;
        BackColor = _theme.EditorBack;
        _flow.BackColor = _theme.EditorBack;
        _flow.RightToLeft = RightToLeft;
        _flow.Padding = new Padding(Px(8), Px(28), Px(8), Px(40));

        var s = _snapshot;

        _flow.Controls.Add(new Label
        {
            Text = Loc.T("Settings"),
            AutoSize = true,
            ForeColor = _theme.TabActiveFore,
            BackColor = _theme.EditorBack,
            Font = UiFonts.Get("Segoe UI Semibold", 20f),
            Margin = new Padding(Px(4), 0, 0, Px(4)),
        });

        _cols = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, BackColor = _theme.EditorBack, RightToLeft = RightToLeft };
        _colA = NewColumn();
        _colB = NewColumn();
        _cols.Controls.Add(_colA);
        _cols.Controls.Add(_colB);
        _flow.Controls.Add(_cols);

        // ---- General
        _nextCol = 0;
        var general = Card(Loc.T("General"));
        Row(general, Loc.T("Interface language"), Loc.T("Language of menus, tooltips and dialogs."),
            Choice(new[] { "English", "العربية" }, x => x.Language == "ar" ? 1 : 0, value => Raise("language", value)));
        Row(general, Loc.T("Theme"), Loc.T("Color theme of the whole app."),
            Choice(new[] { Loc.T("Light"), Loc.T("Dark"), Loc.T("Black"), Loc.T("Solarized") }, x => Math.Max(0, Array.FindIndex(AppTheme.All, t => t.Key == x.ThemeKey)), value => Raise("theme", value)), last: true);

        // ---- Editor
        var editor = Card(Loc.T("Editor"));
        var fontDescription = Row(editor, Loc.T("Font"), $"{s.FontName}  ·  {s.FontSize:0.#} pt", ButtonControl(Loc.T("Change..."), "font"));
        _refreshers.Add(x => fontDescription.Text = $"{x.FontName}  ·  {x.FontSize:0.#} pt");
        Row(editor, Loc.T("Font size"), Loc.T("Also Ctrl++ and Ctrl+- anywhere in the editor."), SizeControl(s.FontSize));
        Row(editor, Loc.T("Text direction"), Loc.T("Automatic follows the text, or force one direction for every tab."),
            Choice(new[] { Loc.T("Auto"), Loc.T("Left to right"), Loc.T("Right to left") }, x => x.Direction, value => Raise("direction", value)));
        Row(editor, Loc.T("Word wrap"), Loc.T("Wrap long lines inside the current tab."),
            Choice(new[] { Loc.T("Off"), Loc.T("On") }, x => x.WordWrap ? 1 : 0, value => Raise("wrap", value)));
        Row(editor, Loc.T("Line numbers"), Loc.T("Show a numbered column next to the text."),
            Choice(new[] { Loc.T("Off"), Loc.T("On") }, x => x.LineNumbers ? 1 : 0, value => Raise("numbers", value)), last: !s.HasDocument);
        if (s.HasDocument)
        {
            var syntaxButton = ButtonControl(s.SyntaxName + "   ▾", "syntax");
            Row(editor, Loc.T("Syntax language (this tab)"), Loc.T("Highlighting used for the current tab."), syntaxButton, last: true);
            _refreshers.Add(x => syntaxButton.Text = x.SyntaxName + "   ▾");
        }

        // ---- Device
        _nextCol = 1;
        var device = Card(Loc.T("Device and terminal"));
        Row(device, Loc.T("Terminal shell"), Loc.T("Shell used by the terminal in the Device tab."),
            Choice(new[] { "cmd", "PowerShell" }, x => x.PowerShell ? 1 : 0, value => Raise("shell", value)), last: true);

        // ---- Files
        _nextCol = 0;
        var files = Card(Loc.T("Files"));
        Row(files, Loc.T("Auto-save files"), Loc.T("Saves a changed file about a second after you stop typing."),
            Choice(new[] { Loc.T("Off"), Loc.T("On") }, x => x.AutoSave ? 1 : 0, value => Raise("autosave", value)));
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = _theme.ToolBack, Margin = Padding.Empty };
        _sizers.Add(w => buttons.MaximumSize = new Size(w - Px(40), 0));
        buttons.Controls.Add(ButtonControl(Loc.T("New tab"), "new"));
        buttons.Controls.Add(ButtonControl(Loc.T("Open..."), "open"));
        buttons.Controls.Add(ButtonControl(Loc.T("Save"), "save"));
        buttons.Controls.Add(ButtonControl(Loc.T("Save As..."), "saveas"));
        buttons.Controls.Add(ButtonControl(Loc.T("Find..."), "find"));
        buttons.Controls.Add(ButtonControl(Loc.T("Close tab"), "close"));
        AddFull(files, buttons, last: true);

        // ---- Shortcuts
        _nextCol = 1;
        var shortcuts = Card(Loc.T("Keyboard shortcuts"));
        var items = new[]
        {
            "Ctrl+N|New tab", "Ctrl+O|Open", "Ctrl+S|Save", "Ctrl+Shift+S|Save As", "Ctrl+W|Close tab",
            "Ctrl+F|Find", "Ctrl+H|Find and replace", "Ctrl+,|Settings", "Ctrl+R|Text direction", "Ctrl++ / Ctrl+-|Font size", "Ctrl+0|Default text size",
            "Ctrl+D|Theme", "Ctrl+`|Device and terminal",
        };

        for (var i = 0; i < items.Length; i++)
        {
            var parts = items[i].Split('|');
            ShortcutRow(shortcuts, parts[0], Loc.T(parts[1]), i == items.Length - 1);
        }

        // ---- About
        _nextCol = 0;
        var about = Card(Loc.T("About"));
        var aboutPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = _theme.ToolBack,
            Margin = Padding.Empty,
            Padding = new Padding(Px(8), Px(6), Px(8), Px(6)),
        };

        Label AboutLine(string text, string font, float size, Color color, int spaceAfter)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = color,
                BackColor = _theme.ToolBack,
                Font = UiFonts.Get(font, size),
                Margin = new Padding(0, 0, 0, spaceAfter),
            };
            _sizers.Add(w => label.MaximumSize = new Size(Math.Max(Px(200), w - Px(72)), 0));
            return label;
        }

        aboutPanel.Controls.Add(AboutLine(Loc.T("Keky Notepad"), "Segoe UI Semibold", 17f, _theme.TabActiveFore, Px(2)));
        aboutPanel.Controls.Add(AboutLine(Loc.T("Version") + " 1.0", "Segoe UI", 9.5f, _theme.DisabledFore, Px(14)));
        aboutPanel.Controls.Add(AboutLine(Loc.T("A code editor, a notepad and a phone screen mirror in one app, connected through a single cable."), "Segoe UI", 10.5f, _theme.EditorFore, Px(8)));
        aboutPanel.Controls.Add(AboutLine(Loc.T("Get the most out of your time and be more productive."), "Segoe UI Semibold", 10.5f, _theme.EditorFore, 0));
        AddFull(about, aboutPanel, last: true);

        foreach (var table in _tables)
        {
            table.ResumeLayout(false);
        }

        _flow.ResumeLayout(true);
        ResumeLayout(true);
        Arrange(true);
        CenterContent();
        AutoScrollPosition = new Point(0, scroll);

        if (frozen)
        {
            NativeMethods.LockWindowUpdate(IntPtr.Zero);
            Invalidate(true);
        }
    }

    private void Raise(string name, int value) => Command?.Invoke(name, value, this);

    /// <summary>Creates a titled card and returns the table rows are added to.</summary>
    private TableLayoutPanel Card(string title)
    {
        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = _theme.DisabledFore,
            BackColor = _theme.EditorBack,
            Font = UiFonts.Get("Segoe UI Semibold", 10f),
            Margin = new Padding(Px(6), Px(20), 0, Px(8)),
        };

        var card = new RoundedPanel(_theme)
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 0),
            Padding = new Padding(3),
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = _theme.ToolBack,
            Margin = Padding.Empty,
        };
        table.SuspendLayout();
        _tables.Add(table);
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        card.Controls.Add(table);
        _sections.Add((titleLabel, card, _nextCol));
        return table;
    }

    private FlowLayoutPanel NewColumn() => new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = Padding.Empty,
        BackColor = _theme.EditorBack,
        RightToLeft = RightToLeft,
    };

    private Label Row(TableLayoutPanel table, string title, string description, Control control, bool last = false)
    {
        Label descriptionLabel;
        var text = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = _theme.ToolBack,
            Margin = Padding.Empty,
            Padding = new Padding(Px(16), Px(12), Px(8), Px(12)),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };

        text.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = _theme.EditorFore,
            BackColor = _theme.ToolBack,
            Font = UiFonts.Get("Segoe UI Semibold", 10.5f),
            Margin = Padding.Empty,
        });

        {
            descriptionLabel = new Label
            {
                Text = description,
                AutoSize = true,
                MaximumSize = new Size(Px(380), 0),
                ForeColor = _theme.DisabledFore,
                BackColor = _theme.ToolBack,
                Font = UiFonts.Get("Segoe UI", 9f),
                Margin = new Padding(0, Px(2), 0, 0),
            };
            var captured = descriptionLabel;
            _sizers.Add(w => captured.MaximumSize = new Size(Math.Max(Px(200), (int)(w * 0.5)), 0));
            text.Controls.Add(descriptionLabel);
        }

        control.Anchor = AnchorStyles.None;
        control.Margin = new Padding(Px(8), Px(10), Px(16), Px(10));

        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(text, 0, row);
        table.Controls.Add(control, 1, row);
        if (!last)
        {
            Divider(table);
        }

        return descriptionLabel;
    }

    private void AddFull(TableLayoutPanel table, Control control, bool last)
    {
        control.Margin = new Padding(Px(12), Px(12), Px(12), Px(12));
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 2);
        if (!last)
        {
            Divider(table);
        }
    }

    private void ShortcutRow(TableLayoutPanel table, string keys, string description, bool last)
    {
        var text = new Label
        {
            Text = description,
            AutoSize = true,
            ForeColor = _theme.EditorFore,
            BackColor = _theme.ToolBack,
            Font = UiFonts.Get("Segoe UI", 10f),
            Margin = new Padding(Px(16), Px(9), Px(8), Px(9)),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };

        var chip = new Label
        {
            Text = keys,
            AutoSize = true,
            ForeColor = _theme.EditorFore,
            BackColor = _theme.EditorBack,
            BorderStyle = BorderStyle.FixedSingle,
            Font = UiFonts.Get("Consolas", 9.5f),
            Padding = new Padding(Px(6), Px(2), Px(6), Px(2)),
            Margin = new Padding(Px(8), Px(7), Px(16), Px(7)),
            Anchor = AnchorStyles.None,
            RightToLeft = RightToLeft.No,
        };

        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(text, 0, row);
        table.Controls.Add(chip, 1, row);
        if (!last)
        {
            Divider(table);
        }
    }

    private void Divider(TableLayoutPanel table)
    {
        var line = new Panel { Height = 1, Dock = DockStyle.Fill, BackColor = _theme.Border, Margin = new Padding(Px(16), 0, Px(16), 0) };
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        table.Controls.Add(line, 0, row);
        table.SetColumnSpan(line, 2);
    }

    private Control Choice(string[] options, Func<SettingsSnapshot, int> selected, Action<int> changed)
    {
        var control = new Segmented(options, selected(_snapshot), _theme) { Margin = Padding.Empty };
        control.SelectionChanged += changed;
        _refreshers.Add(x => control.SetSelected(selected(x)));
        return control;
    }

    private Control SizeControl(float size)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = _theme.ToolBack, Margin = Padding.Empty, FlowDirection = FlowDirection.LeftToRight, RightToLeft = RightToLeft.No };
        panel.Controls.Add(ButtonControl("−", "size-"));
        var sizeLabel = new Label
        {
            Text = $"{size:0.#}",
            Width = Px(48),
            Height = Px(32),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = _theme.EditorFore,
            BackColor = _theme.ToolBack,
            Font = UiFonts.Get("Segoe UI Semibold", 10.5f),
            Margin = Padding.Empty,
        };
        panel.Controls.Add(sizeLabel);
        _refreshers.Add(x => sizeLabel.Text = $"{x.FontSize:0.#}");
        panel.Controls.Add(ButtonControl("+", "size+"));
        panel.Controls.Add(ButtonControl(Loc.T("Reset"), "size0"));
        return panel;
    }

    private Button ButtonControl(string text, string command)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(Px(34), Px(32)),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            BackColor = _theme.EditorBack,
            ForeColor = _theme.EditorFore,
            Font = UiFonts.Get("Segoe UI", 10f),
            Padding = new Padding(Px(10), 0, Px(10), 0),
            Margin = new Padding(0, 0, Px(6), Px(4)),
            Cursor = Cursors.Hand,
        };

        button.FlatAppearance.BorderColor = _theme.Border;
        button.FlatAppearance.MouseOverBackColor = _theme.MenuHover;
        button.FlatAppearance.MouseDownBackColor = _theme.TabHoverBack;
        button.Click += (_, _) =>
        {
            switch (command)
            {
                case "size-":
                    Command?.Invoke("size", -1, button);
                    break;
                case "size+":
                    Command?.Invoke("size", 1, button);
                    break;
                case "size0":
                    Command?.Invoke("size", 0, button);
                    break;
                default:
                    Command?.Invoke(command, 0, button);
                    break;
            }
        };
        return button;
    }

    private int Px(float value) => (int)Math.Round(value * DeviceDpi / 96.0);
}

/// <summary>Card container: rounded border and fill that follow the theme.</summary>
internal sealed class RoundedPanel : Panel
{
    private readonly AppTheme _theme;

    public RoundedPanel(AppTheme theme)
    {
        _theme = theme;
        BackColor = theme.ToolBack;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(_theme.EditorBack);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = new GraphicsPath();
        var d = Math.Max(8, (int)Math.Round(10 * DeviceDpi / 96.0));
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using var fill = new SolidBrush(_theme.ToolBack);
        e.Graphics.FillPath(fill, path);
        using var pen = new Pen(_theme.Border);
        e.Graphics.DrawPath(pen, path);
    }
}

/// <summary>A row of mutually exclusive options drawn as one rounded control.</summary>
internal sealed class Segmented : Control
{
    private readonly string[] _options;
    private readonly AppTheme _theme;
    private int _selected;
    private int _hover = -1;

    public Segmented(string[] options, int selected, AppTheme theme)
    {
        _options = options;
        _selected = selected;
        _theme = theme;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Font = UiFonts.Get("Segoe UI", 10f);
        Cursor = Cursors.Hand;
        Size = new Size(MeasureWidth(), Px(30));
    }

    public event Action<int>? SelectionChanged;

    /// <summary>Changes the highlighted option without raising <see cref="SelectionChanged"/>.</summary>
    public void SetSelected(int index)
    {
        if (index != _selected)
        {
            _selected = index;
            Invalidate();
        }
    }

    private int SegmentWidth(int index) =>
        TextRenderer.MeasureText(_options[index], Font).Width + Px(26);

    private int MeasureWidth()
    {
        var total = 0;
        for (var i = 0; i < _options.Length; i++)
        {
            total += SegmentWidth(i);
        }

        return total + 2;
    }

    private int IndexAt(int x)
    {
        var left = 1;
        for (var i = 0; i < _options.Length; i++)
        {
            var width = SegmentWidth(i);
            if (x >= left && x < left + width)
            {
                return i;
            }

            left += width;
        }

        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = IndexAt(e.X);
        if (index != _hover)
        {
            _hover = index;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var index = IndexAt(e.X);
        if (e.Button == MouseButtons.Left && index >= 0 && index != _selected)
        {
            _selected = index;
            Invalidate();
            SelectionChanged?.Invoke(index);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_theme.ToolBack);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var outer = new Rectangle(0, 0, Width - 1, Height - 1);
        using var outerPath = Round(outer, Px(7));
        using (var back = new SolidBrush(_theme.EditorBack))
        {
            g.FillPath(back, outerPath);
        }

        var left = 1;
        for (var i = 0; i < _options.Length; i++)
        {
            var width = SegmentWidth(i);
            var rect = new Rectangle(left, 1, width, Height - 3);
            if (i == _selected)
            {
                using var path = Round(Rectangle.Inflate(rect, -1, -1), Px(6));
                using var fill = new SolidBrush(_theme.Accent);
                g.FillPath(fill, path);
            }
            else if (i == _hover)
            {
                using var path = Round(Rectangle.Inflate(rect, -1, -1), Px(6));
                using var fill = new SolidBrush(_theme.MenuHover);
                g.FillPath(fill, path);
            }

            TextRenderer.DrawText(
                g,
                _options[i],
                Font,
                rect,
                i == _selected ? Color.White : _theme.EditorFore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            left += width;
        }

        using var border = new Pen(_theme.Border);
        g.DrawPath(border, outerPath);
    }

    private static GraphicsPath Round(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private int Px(float value) => (int)Math.Round(value * DeviceDpi / 96.0);
}

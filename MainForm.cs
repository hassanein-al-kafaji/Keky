using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Windows.Forms;
using KekyNotepad.Editor;
using KekyNotepad.Services;

namespace KekyNotepad;

public sealed partial class MainForm : Form
{
    private static string AppName => Loc.T("Keky Notepad");
    private const string FileFilter =
        "Code and text files|*.txt;*.cs;*.py;*.js;*.jsx;*.ts;*.tsx;*.json;*.html;*.css;*.xml;*.md;*.log;*.ini;*.csv|All files (*.*)|*.*";

    private const int TitleBarHeight = TitleTabBar.ToolRowHeight + TitleTabBar.TabRowHeight;
    private const int FrameBorder = 4;
    private const int FindBarHeight = 48;

    private readonly SessionStore _store = new();
    private readonly List<EditorTab> _documents = new();
    private readonly Dictionary<string, ToolStripMenuItem> _languageMenuItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LanguageDefinition> _languages = Languages.All.ToList();
    private readonly System.Windows.Forms.Timer _autosaveTimer = new() { Interval = 1000 };
    private readonly HashSet<string> _pendingAutosave = new(StringComparer.Ordinal);

    private readonly TableLayoutPanel _root = new();
    private readonly TitleTabBar _titleBar = new();
    private readonly ThemedTabControl _tabs = new();
    private readonly Panel _work = new();
    private readonly DevicePanel _device = new();
    private readonly TerminalDock _terminalDock = new();
    private readonly Panel _deviceView = new();
    private readonly ContextMenuStrip _appMenu = new();
    private readonly Panel _findPanel = new();
    private readonly FlowLayoutPanel _findFlow = new();

    private readonly TextBox _findBox = new();
    private readonly Button _findNextButton = new();
    private readonly Button _findCloseButton = new();
    private readonly Label _findLabel = new();

    private readonly ToolStripMenuItem _languageMenu = new("&Language");
    private readonly ToolStripMenuItem _directionMenu = new("Text &direction");
    private readonly ToolStripMenuItem _fontMenu = new("Editor &font");
    private readonly ToolStripMenuItem _directionAutoItem = new("Automatic (Arabic / English)");
    private readonly ToolStripMenuItem _directionLtrItem = new("Always left-to-right");
    private readonly ToolStripMenuItem _directionRtlItem = new("Always right-to-left");
    private readonly ToolStripMenuItem _fontChooseItem = new("Choose font...");
    private readonly ToolStripMenuItem _fontBiggerItem = new("Larger text");
    private readonly ToolStripMenuItem _fontSmallerItem = new("Smaller text");
    private readonly ToolStripMenuItem _fontResetItem = new("Reset text size");
    private readonly ToolStripMenuItem _darkThemeMenuItem = new("Dark mode");
    private readonly ToolStripMenuItem _lightThemeMenuItem = new("Light mode");
    private readonly ToolStripMenuItem _wordWrapMenuItem = new("Word wrap");
    private readonly ToolStripMenuItem _lineNumbersMenuItem = new("Line numbers");
    private readonly ToolStripMenuItem _deviceMenuItem = new("Device panel (scrcpy + terminal)") { ShortcutKeyDisplayString = "Ctrl+`" };
    private readonly ToolStripMenuItem _powerShellMenuItem = new("Terminal uses PowerShell");
    private bool _terminalPowerShell;
    private bool _autoSaveFiles;
    private DateTime _autoSaveBlockedUntil = DateTime.MinValue;
    private string? _scrcpyPath;
    private bool _devicePanelOpen;
    private int _devicePanelWidth = 420;
    private int _deviceTerminalHeight;

    private AppTheme _theme = AppTheme.Light;
    private TextDirectionMode _directionMode = TextDirectionMode.Automatic;
    private string _editorFontFamily = EditorTab.DefaultFontFamily;
    private float _editorFontSize = EditorTab.DefaultFontSize;
    private string? _rightClickText;
    private bool _restoring;
    private bool _closed;
    private bool _showLineNumbers = true;
    private UserSettings? _savedSettings;

    public MainForm()
    {
        Text = AppName;
        LoadAppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1040, 640);
        MinimumSize = new Size(760, 500);
        Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        DoubleBuffered = true;
        KeyPreview = true;
        FormBorderStyle = FormBorderStyle.None;
        Padding = new Padding(FrameBorder);

        // Subscribe before anything can create the window handle, otherwise the
        // rounded corners / dark title bar would never be applied.
        HandleCreated += (_, _) =>
        {
            NativeMethods.TrySetDarkTitleBar(Handle, _theme.IsDark);
            NativeMethods.TryUseRoundedCorners(Handle);
        };

        BuildUi();
        ApplyTheme(AppTheme.Light, persist: false);

        _autosaveTimer.Tick += AutosaveTimer_Tick;
        FormClosing += MainForm_FormClosing;
        Shown += MainForm_Shown;
        Resize += MainForm_Resize;
        DpiChanged += (_, _) => UpdateChromeMetrics();
    }

    private EditorTab? ActiveDocument => _tabs.SelectedTab?.Tag as EditorTab;
    private RichTextBox? ActiveEditor => ActiveDocument?.Editor;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        UpdateChromeMetrics();
        RestoreSession();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autosaveTimer.Stop();
            _autosaveTimer.Dispose();
            _device.Shutdown();
            _terminalDock.Shutdown();
            _appMenu.Dispose();

            foreach (var document in _documents.ToArray())
            {
                document.Dispose();
            }

            _documents.Clear();
        }

        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;

        if (key == Keys.F3 && modifiers == Keys.Shift)
        {
            FindNext(forward: false);
            return true;
        }

        if (key == Keys.F3 && modifiers == Keys.None)
        {
            if (_findBox.TextLength == 0)
            {
                ShowFind();
            }
            else
            {
                FindNext();
            }

            return true;
        }

        if (key == Keys.Escape && _findPanel.Visible)
        {
            HideFind();
            return true;
        }

        if (modifiers == Keys.Control)
        {
            switch (key)
            {
                case Keys.N:
                    NewDocument();
                    return true;

                case Keys.O:
                    OpenFiles();
                    return true;

                case Keys.S:
                    SaveActive(saveAs: false);
                    return true;

                case Keys.W:
                    CloseActiveDocument();
                    return true;

                case Keys.F:
                    ShowFind();
                    return true;

                case Keys.H:
                    ShowFind(replace: true);
                    return true;

                case Keys.Oemcomma:
                    ShowSettings();
                    return true;

                case Keys.R:
                    CycleDirectionMode();
                    return true;

                case Keys.D:
                    ToggleTheme();
                    return true;

                case Keys.Oemplus:
                case Keys.Add:
                    ChangeFontSize(1f);
                    return true;

                case Keys.OemMinus:
                case Keys.Subtract:
                    ChangeFontSize(-1f);
                    return true;

                case Keys.D0:
                case Keys.NumPad0:
                    ResetFontSize();
                    return true;

                case Keys.Tab:
                    CycleDocument(reverse: false);
                    return true;

                case Keys.Oemtilde:
                    ToggleDevicePanel();
                    return true;
            }
        }
        else if (modifiers == (Keys.Control | Keys.Shift))
        {
            switch (key)
            {
                case Keys.S:
                    SaveActive(saveAs: true);
                    return true;

                case Keys.Oemplus:
                case Keys.Add:
                    ChangeFontSize(1f);
                    return true;

                case Keys.Tab:
                    CycleDocument(reverse: true);
                    return true;

                // Dedicated direction keys: Ctrl+Shift+Left = always LTR,
                // Ctrl+Shift+Right = always RTL.
                case Keys.Left:
                    SetDirectionMode(TextDirectionMode.LeftToRight);
                    return true;

                case Keys.Right:
                    SetDirectionMode(TextDirectionMode.RightToLeft);
                    return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_GETMINMAXINFO)
        {
            base.WndProc(ref m);
            NativeMethods.ConstrainMaximizedBounds(Handle, m.LParam);
            return;
        }

        if (m.Msg == NativeMethods.WM_NCHITTEST)
        {
            base.WndProc(ref m);

            if (WindowState == FormWindowState.Normal && (int)m.Result == NativeMethods.HTCLIENT)
            {
                var hit = HitTestResize(m.LParam);
                if (hit != 0)
                {
                    m.Result = new IntPtr(hit);
                }
            }

            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (WindowState == FormWindowState.Maximized || Padding.Left <= 0)
        {
            return;
        }

        using var pen = new Pen(_theme.Border);
        var bounds = ClientRectangle;
        e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
    }

    private int HitTestResize(IntPtr lParam)
    {
        var value = lParam.ToInt64();
        var screenPoint = new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
        var point = PointToClient(screenPoint);

        var border = Math.Max(2, Padding.Left);
        var left = point.X < border;
        var right = point.X >= ClientSize.Width - border;
        var top = point.Y < border;
        var bottom = point.Y >= ClientSize.Height - border;

        if (top && left)
        {
            return NativeMethods.HTTOPLEFT;
        }

        if (top && right)
        {
            return NativeMethods.HTTOPRIGHT;
        }

        if (bottom && left)
        {
            return NativeMethods.HTBOTTOMLEFT;
        }

        if (bottom && right)
        {
            return NativeMethods.HTBOTTOMRIGHT;
        }

        if (left)
        {
            return NativeMethods.HTLEFT;
        }

        if (right)
        {
            return NativeMethods.HTRIGHT;
        }

        if (top)
        {
            return NativeMethods.HTTOP;
        }

        if (bottom)
        {
            return NativeMethods.HTBOTTOM;
        }

        return 0;
    }

    private void BuildUi()
    {
        SuspendLayout();

        _root.Dock = DockStyle.Fill;
        _root.ColumnCount = 1;
        _root.RowCount = 4;
        _root.Margin = Padding.Empty;
        _root.Padding = Padding.Empty;
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleBarHeight)); // title bar + document tabs
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));             // find bar
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));            // editor pages

        _titleBar.Dock = DockStyle.Fill;
        _titleBar.Margin = Padding.Empty;
        _titleBar.Theme = _theme;
        _titleBar.TabSelected += TitleBar_TabSelected;
        _titleBar.TabCloseRequested += TitleBar_TabCloseRequested;
        _titleBar.NewTabRequested += (_, _) => NewDocument();
        _titleBar.MenuRequested += (_, _) => ShowSettings();
        _titleBar.MinimizeRequested += (_, _) => WindowState = FormWindowState.Minimized;
        _titleBar.MaximizeRestoreRequested += (_, _) => ToggleMaximize();
        _titleBar.CloseRequested += (_, _) => Close();
        _titleBar.RenameRequested += TitleBar_RenameRequested;
        _titleBar.TabRenamed += TitleBar_TabRenamed;
        _titleBar.ActionInvoked += TitleBar_ActionInvoked;

        _tabs.Dock = DockStyle.Fill;
        _tabs.Margin = Padding.Empty;
        _tabs.Theme = _theme;
        _tabs.ShowTabStrip = false;
        _tabs.CloseTabRequested += Tabs_CloseTabRequested;
        _tabs.NewTabRequested += (_, _) => NewDocument();
        _tabs.SelectedIndexChanged += Tabs_SelectedIndexChanged;

        BuildAppMenu();
        BuildFindBar();

        _root.Controls.Add(_titleBar, 0, 0);
        _root.Controls.Add(_findPanel, 0, 1);
        _work.Dock = DockStyle.Fill;
        _work.Margin = Padding.Empty;
        _work.Padding = Padding.Empty;
        _device.ScrcpyPathChanged += (_, path) =>
        {
            _scrcpyPath = path;
            SaveSessionState();
        };
        _titleBar.DeviceTabRequested += (_, _) => ToggleDevicePanel();
        _device.LogcatRequested += (_, command) =>
        {
            SetDevicePanel(true);
            _terminalDock.Execute(_terminalPowerShell ? "& " + command : command);
        };

        // Fill control first (index 0); the right-hand panel docks before it.
        // The Device page: terminal fills the left, the phone is a fixed column on the right.
        _deviceView.Dock = DockStyle.Fill;
        _deviceView.Margin = Padding.Empty;
        _deviceView.Visible = false;
        _terminalDock.Dock = DockStyle.Fill;
        _terminalDock.ShowGrip = false;
        _deviceView.Controls.Add(_terminalDock);
        _deviceView.Controls.Add(_device);

        _work.Controls.Add(_tabs);
        _work.Controls.Add(_deviceView);
        InitSettings();
        _root.Controls.Add(_work, 0, 2);

        Controls.Add(_root);

        ResumeLayout(performLayout: true);
    }

    private void ToggleDevicePanel()
    {
        SetDevicePanel(!_device.Visible);
    }

    private void SetDevicePanel(bool open)
    {
        if (open)
        {
            HideSettingsView(false);
            _terminalDock.SetWorkingDirectory(
                string.IsNullOrEmpty(ActiveDocument?.FilePath) ? null : Path.GetDirectoryName(ActiveDocument.FilePath));
            _deviceView.Visible = true;
            _tabs.Visible = false;
            _terminalDock.Open();
            _device.Open();
            _terminalDock.FocusInput();
        }
        else
        {
            _device.Close();
            _terminalDock.Close();
            _deviceView.Visible = false;
            _tabs.Visible = true;
        }

        _titleBar.DeviceOpen = open;
        _deviceMenuItem.Checked = open;
        SaveSessionState();
    }

    private void BuildAppMenu()
    {
        _appMenu.ShowImageMargin = true;
        _appMenu.ShowCheckMargin = true;
        _appMenu.ShowItemToolTips = true;
        _appMenu.Padding = new Padding(0, 4, 0, 4);

        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add(MenuItem("&New tab", "Ctrl+N", (_, _) => NewDocument()));
        fileMenu.DropDownItems.Add(MenuItem("&Open...", "Ctrl+O", (_, _) => OpenFiles()));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(MenuItem("&Save", "Ctrl+S", (_, _) => SaveActive(saveAs: false)));
        fileMenu.DropDownItems.Add(MenuItem("Save &As...", "Ctrl+Shift+S", (_, _) => SaveActive(saveAs: true)));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(MenuItem("&Close tab", "Ctrl+W", (_, _) => CloseActiveDocument()));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(MenuItem("E&xit", "Alt+F4", (_, _) => Close()));

        var editMenu = new ToolStripMenuItem("&Edit");
        editMenu.DropDownItems.Add(MenuItem("&Undo", "Ctrl+Z", (_, _) => ActiveEditor?.Undo()));
        editMenu.DropDownItems.Add(MenuItem("&Redo", "Ctrl+Y", (_, _) => ActiveEditor?.Redo()));
        editMenu.DropDownItems.Add(new ToolStripSeparator());
        editMenu.DropDownItems.Add(MenuItem("Cu&t", "Ctrl+X", (_, _) => ActiveEditor?.Cut()));
        editMenu.DropDownItems.Add(MenuItem("&Copy", "Ctrl+C", (_, _) => ActiveEditor?.Copy()));
        editMenu.DropDownItems.Add(MenuItem("&Paste", "Ctrl+V", (_, _) => ActiveEditor?.Paste()));
        editMenu.DropDownItems.Add(MenuItem("Select &all", "Ctrl+A", (_, _) => ActiveEditor?.SelectAll()));
        editMenu.DropDownItems.Add(new ToolStripSeparator());
        editMenu.DropDownItems.Add(MenuItem("&Find...", "Ctrl+F", (_, _) => ShowFind()));
        editMenu.DropDownItems.Add(MenuItem("Find &next", "F3", (_, _) => FindNext()));

        _directionAutoItem.Click += (_, _) => SetDirectionMode(TextDirectionMode.Automatic);
        _directionLtrItem.Click += (_, _) => SetDirectionMode(TextDirectionMode.LeftToRight);
        _directionRtlItem.Click += (_, _) => SetDirectionMode(TextDirectionMode.RightToLeft);
        _directionMenu.DropDownItems.Add(_directionAutoItem);
        _directionMenu.DropDownItems.Add(_directionLtrItem);
        _directionMenu.DropDownItems.Add(_directionRtlItem);
        _directionMenu.ToolTipText = "Automatic, or force one direction for every tab  (Ctrl+R cycles)";

        _fontChooseItem.Click += (_, _) => ChooseEditorFont();
        _fontBiggerItem.ShortcutKeyDisplayString = "Ctrl++";
        _fontBiggerItem.Click += (_, _) => ChangeFontSize(1f);
        _fontSmallerItem.ShortcutKeyDisplayString = "Ctrl+-";
        _fontSmallerItem.Click += (_, _) => ChangeFontSize(-1f);
        _fontResetItem.ShortcutKeyDisplayString = "Ctrl+0";
        _fontResetItem.Click += (_, _) => ResetFontSize();
        _fontMenu.DropDownItems.Add(_fontChooseItem);
        _fontMenu.DropDownItems.Add(new ToolStripSeparator());
        _fontMenu.DropDownItems.Add(_fontBiggerItem);
        _fontMenu.DropDownItems.Add(_fontSmallerItem);
        _fontMenu.DropDownItems.Add(_fontResetItem);

        _darkThemeMenuItem.CheckOnClick = false;
        _darkThemeMenuItem.Click += (_, _) => ApplyTheme(AppTheme.Dark, persist: true);

        _lightThemeMenuItem.Click += (_, _) => ApplyTheme(AppTheme.Light, persist: true);

        _wordWrapMenuItem.CheckOnClick = false;
        _wordWrapMenuItem.Click += (_, _) => ToggleWordWrap();

        _lineNumbersMenuItem.CheckOnClick = false;
        _lineNumbersMenuItem.Click += (_, _) => ToggleLineNumbers();

        _deviceMenuItem.CheckOnClick = false;
        _deviceMenuItem.Click += (_, _) => ToggleDevicePanel();

        _powerShellMenuItem.CheckOnClick = false;
        _powerShellMenuItem.Click += (_, _) =>
        {
            _terminalPowerShell = !_terminalPowerShell;
            _powerShellMenuItem.Checked = _terminalPowerShell;
            _terminalDock.UsePowerShell = _terminalPowerShell;
            SaveSessionState();
        };

        foreach (var language in _languages)
        {
            var item = new ToolStripMenuItem(language.DisplayName) { Tag = language.Key };
            item.Click += (_, _) => SetLanguage(language.Key);
            _languageMenu.DropDownItems.Add(item);
            _languageMenuItems[language.Key] = item;
        }

        var viewMenu = new ToolStripMenuItem("&View");
        viewMenu.DropDownItems.Add(_languageMenu);
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(_directionMenu);
        viewMenu.DropDownItems.Add(_wordWrapMenuItem);
        viewMenu.DropDownItems.Add(_lineNumbersMenuItem);
        viewMenu.DropDownItems.Add(_deviceMenuItem);
        viewMenu.DropDownItems.Add(_powerShellMenuItem);
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(_fontMenu);
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(_darkThemeMenuItem);
        viewMenu.DropDownItems.Add(_lightThemeMenuItem);

        var helpMenu = new ToolStripMenuItem("&Help");
        helpMenu.DropDownItems.Add(MenuItem("&About", null, (_, _) => ShowAbout()));

        _appMenu.Items.AddRange(new ToolStripItem[] { fileMenu, editMenu, viewMenu, helpMenu });
    }


    private static ToolStripMenuItem MenuItem(string text, string? shortcut, EventHandler click)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += click;

        if (!string.IsNullOrEmpty(shortcut))
        {
            item.ShortcutKeyDisplayString = shortcut;
        }

        return item;
    }

    private static void ConfigureFlatButton(Button button, string text, int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 27;
        button.Margin = new Padding(0, 0, 6, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
    }

    private void UpdateChromeMetrics()
    {
        var scale = DeviceDpi / 96.0;

        _root.RowStyles[0].Height = (float)Math.Round(TitleBarHeight * scale);
        _titleBar.MinimumSize = new Size(0, (int)Math.Round(TitleBarHeight * scale));
        MinimumSize = new Size(
            (int)Math.Round(760 * scale),
            (int)Math.Round(500 * scale));

        if (_findPanel.Visible)
        {
            _root.RowStyles[1].Height = FindPanelHeight();
        }

        ScaleFindBar(scale);
        UpdateFramePadding();
        Invalidate();
    }


    private void UpdateFramePadding()
    {
        var border = (int)Math.Round(FrameBorder * DeviceDpi / 96.0);
        var target = WindowState == FormWindowState.Maximized
            ? Padding.Empty
            : new Padding(border);

        if (Padding != target)
        {
            Padding = target;
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;

        _titleBar.IsMaximized = WindowState == FormWindowState.Maximized;
        UpdateFramePadding();
        Invalidate();
    }

    private void MainForm_Resize(object? sender, EventArgs e)
    {
        _titleBar.IsMaximized = WindowState == FormWindowState.Maximized;
        UpdateFramePadding();
        Invalidate();
    }

    private void TitleBar_TabSelected(object? sender, int index)
    {
        if (_settingsOpen && index == _documents.Count)
        {
            ShowSettings();
            return;
        }

        HideSettingsView(true);

        if (_device.Visible)
        {
            SetDevicePanel(false);
        }

        if (index >= 0 && index < _documents.Count)
        {
            _tabs.SelectedTab = _documents[index].Page;
            ActiveDocument?.Editor.Focus();
        }
    }

    private void TitleBar_TabCloseRequested(object? sender, int index)
    {
        if (_settingsOpen && index == _documents.Count)
        {
            CloseSettings();
            return;
        }

        if (index >= 0 && index < _documents.Count)
        {
            CloseDocument(_documents[index]);
        }
    }

    private void TitleBar_RenameRequested(object? sender, int index)
    {
        if (index >= 0 && index < _documents.Count)
        {
            _titleBar.BeginRename(index, Loc.DisplayTitle(_documents[index].Title));
        }
    }

    private void TitleBar_TabRenamed(object? sender, TabRenamedEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _documents.Count)
        {
            return;
        }

        if (e.Name == Loc.DisplayTitle(_documents[e.Index].Title))
        {
            return;
        }

        _documents[e.Index].Rename(e.Name);
        SaveSessionState();
        RefreshTitleTabs();
        UpdateWindowTitle();
    }

    private void ShowAppMenu()
    {
        UpdateTabUi();

        var bounds = _titleBar.MenuButtonBounds;
        var anchor = _titleBar.PointToScreen(new Point(bounds.Left, bounds.Bottom + 1));
        _appMenu.Show(anchor);
    }

    private void RefreshTitleTabs()
    {
        var items = _documents
            .Select(document => new TitleTabItem(Loc.DisplayTitle(document.Title))
            {
                ToolTip = document.FilePath ?? Loc.DisplayTitle(document.Title),
                IsRtl = document.IsRtl,
                IsDirty = document.IsDirty,
                Tag = document,
            })
            .ToList();

        if (_settingsOpen)
        {
            items.Add(new TitleTabItem(Loc.T("Settings")) { ToolTip = Loc.T("Settings"), Tag = "settings" });
        }

        _titleBar.SetTabs(items, _settingsActive ? _documents.Count : _tabs.SelectedIndex);
    }

    private void RestoreSession()
    {
        var hadSession = File.Exists(_store.MetadataPath);
        var state = _store.LoadSession();

        // Preferences (font, size, theme, direction) live in their own file and win
        // over whatever the session file says.
        var settings = _store.LoadSettings();
        if (settings is not null)
        {
            state.Theme = settings.Theme;
            state.DirectionMode = settings.DirectionMode;
            state.EditorFont = settings.EditorFont;
            state.EditorFontSize = settings.EditorFontSize;
            state.ShowLineNumbers = settings.ShowLineNumbers;
            _devicePanelOpen = settings.DevicePanelOpen;
            _devicePanelWidth = settings.DevicePanelWidth;
            _deviceTerminalHeight = settings.DeviceTerminalHeight;
            _scrcpyPath = settings.ScrcpyPath;
            _terminalPowerShell = settings.TerminalPowerShell;
            Loc.Language = settings.Language ?? Loc.SystemDefault();
            _autoSaveFiles = settings.AutoSaveFiles;
            _savedSettings = settings;
        }

        _showLineNumbers = state.ShowLineNumbers;
        _device.Width = Math.Clamp(_devicePanelWidth, 160, 1200);
        _terminalDock.Height = Math.Clamp(_deviceTerminalHeight, 100, 800);
        _device.ScrcpyPath = _scrcpyPath;
        _terminalDock.UsePowerShell = _terminalPowerShell;
        _powerShellMenuItem.Checked = _terminalPowerShell;

        _restoring = true;
        try
        {
            ApplyTheme(AppTheme.FromKey(state.Theme), persist: false);
            ApplyRestoredSettings(state);

            if (hadSession)
            {
                ApplyWindowBounds(state);
            }

            foreach (var savedTab in state.Tabs)
            {
                if (string.IsNullOrWhiteSpace(savedTab.Id))
                {
                    continue;
                }

                var recovery = _store.ReadRecovery(savedTab.Id);

                string? diskContent = null;
                var encodingKey = string.IsNullOrWhiteSpace(savedTab.Encoding) ? TextFile.Utf8 : savedTab.Encoding;
                var newLine = string.IsNullOrEmpty(savedTab.NewLine) ? TextFile.DefaultNewLine : savedTab.NewLine;

                if (!string.IsNullOrWhiteSpace(savedTab.FilePath) && File.Exists(savedTab.FilePath))
                {
                    try
                    {
                        var file = TextFile.Read(savedTab.FilePath);
                        diskContent = file.Text;

                        if (recovery is null)
                        {
                            encodingKey = file.EncodingKey;
                            newLine = file.NewLine;
                        }
                    }
                    catch
                    {
                        // Keep the recovery copy if the file is unreadable right now.
                    }
                }

                var content = recovery ?? diskContent ?? string.Empty;

                var dirty = recovery is not null &&
                            (diskContent is null ||
                             !string.Equals(recovery, diskContent, StringComparison.Ordinal));

                if (recovery is null && diskContent is null && !string.IsNullOrWhiteSpace(savedTab.FilePath))
                {
                    // The file disappeared. Keep the tab and let the user save it again.
                    dirty = true;
                }

                var title = !string.IsNullOrWhiteSpace(savedTab.Title)
                    ? savedTab.Title
                    : savedTab.FilePath is not null
                        ? Path.GetFileName(savedTab.FilePath)
                        : "Untitled";

                var document = new EditorTab(
                    savedTab.Id,
                    title,
                    _theme,
                    savedTab.Language,
                    _directionMode,
                    savedTab.WordWrap,
                    _editorFontFamily,
                    _editorFontSize);

                document.FileEncodingKey = encodingKey;
                document.NewLine = newLine;
                document.LoadContent(content, dirty, savedTab.Caret);

                if (!string.IsNullOrWhiteSpace(savedTab.FilePath))
                {
                    document.SetFilePath(savedTab.FilePath);
                }

                AttachDocument(document, select: false);
            }

            if (_documents.Count == 0)
            {
                NewDocument();
            }
            else
            {
                var activeIndex = Math.Clamp(state.ActiveTabIndex, 0, _documents.Count - 1);
                _tabs.SelectedTab = _documents[activeIndex].Page;
            }

        }
        finally
        {
            _restoring = false;
        }

        UpdateTabUi();
        UpdateWindowTitle();
        SaveSessionState();
    }

    private void ApplyRestoredSettings(SessionState state)
    {
        _directionMode = state.DirectionMode?.ToLowerInvariant() switch
        {
            "ltr" => TextDirectionMode.LeftToRight,
            "rtl" => TextDirectionMode.RightToLeft,
            _ => TextDirectionMode.Automatic,
        };

        if (!string.IsNullOrWhiteSpace(state.EditorFont))
        {
            _editorFontFamily = state.EditorFont;
        }

        _editorFontSize = state.EditorFontSize is >= 7f and <= 36f
            ? state.EditorFontSize
            : EditorTab.DefaultFontSize;

        // The old default (11.5) was never chosen by the user: move it to the new, larger default.
        if (Math.Abs(_editorFontSize - 11.5f) < 0.01f)
        {
            _editorFontSize = EditorTab.DefaultFontSize;
        }

        _terminalDock.FontSize = _editorFontSize - 1f;

        UpdateDirectionMenu();
        UpdateFontMenu();
    }

    private void ApplyWindowBounds(SessionState state)
    {
        var width = state.WindowWidth >= 600 ? state.WindowWidth : 1040;
        var height = state.WindowHeight >= 400 ? state.WindowHeight : 640;
        var saved = new Rectangle(state.WindowLeft, state.WindowTop, width, height);

        // A saved window can be bigger than the current screen (monitor change,
        // different DPI, taskbar at the bottom). Keep it inside the work area.
        var work = Screen.FromRectangle(saved).WorkingArea;
        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);

        var x = Math.Clamp(saved.Left, work.Left, Math.Max(work.Left, work.Right - width));
        var y = Math.Clamp(saved.Top, work.Top, Math.Max(work.Top, work.Bottom - height));

        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(x, y, width, height);

        if (state.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    private void AttachDocument(EditorTab document, bool select)
    {
        _documents.Add(document);
        _tabs.TabPages.Add(document.Page);
        document.SetLineNumbers(_showLineNumbers);

        document.ContentChanged += Document_ContentChanged;
        document.StateChanged += Document_StateChanged;

        // No context menu on right-click - we copy directly instead.
        document.Editor.MouseDown += Editor_MouseDown;
        document.Editor.MouseUp += Editor_MouseUp;

        if (select)
        {
            _tabs.SelectedTab = document.Page;
        }

        if (!_restoring)
        {
            UpdateTabUi();
            UpdateWindowTitle();
            SaveSessionState();
        }
    }

    private void Document_ContentChanged(object? sender, EventArgs e)
    {
        if (sender is not EditorTab document)
        {
            return;
        }

        _pendingAutosave.Add(document.Id);
        _autosaveTimer.Stop();
        _autosaveTimer.Start();

        UpdateWindowTitle();
    }

    private void Document_StateChanged(object? sender, EventArgs e)
    {
        _tabs.Invalidate();
        UpdateTabUi();
        UpdateWindowTitle();
    }

    private void Tabs_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_restoring)
        {
            return;
        }
        HideSettingsView(true);

        if (_device.Visible)
        {
            SetDevicePanel(false);
        }

        FlushPendingAutosave();
        UpdateTabUi();
        UpdateWindowTitle();
        SaveSessionState();

        var document = ActiveDocument;
        if (document is not null && !_findPanel.Visible)
        {
            BeginInvoke(new Action(() =>
            {
                if (ReferenceEquals(ActiveDocument, document))
                {
                    document.Editor.Focus();
                }
            }));
        }
    }

    private void Tabs_CloseTabRequested(object? sender, TabPage page)
    {
        if (page.Tag is EditorTab document)
        {
            CloseDocument(document);
        }
    }

    private void AutosaveTimer_Tick(object? sender, EventArgs e)
    {
        FlushPendingAutosave();
    }

    private void FlushPendingAutosave()
    {
        _autosaveTimer.Stop();

        if (_pendingAutosave.Count > 0)
        {
            var now = DateTime.Now;
            foreach (var id in _pendingAutosave.ToArray())
            {
                var document = _documents.FirstOrDefault(item => item.Id == id);
                if (document is null)
                {
                    continue;
                }

                _store.WriteRecovery(document.Id, document.Editor.Text);
                document.SetAutosaved(now);
            }

            _pendingAutosave.Clear();
        }

        AutoSaveFilesNow();

        SaveSessionState();
    }

    private void SaveSessionState()
    {
        if (_restoring || _closed || IsDisposed)
        {
            return;
        }

        var state = BuildSessionState();
        _store.SaveSession(state);

        var settings = new UserSettings
        {
            Theme = state.Theme,
            DirectionMode = state.DirectionMode,
            EditorFont = state.EditorFont,
            EditorFontSize = state.EditorFontSize,
            ShowLineNumbers = state.ShowLineNumbers,
            DevicePanelOpen = _device.Visible,
            DevicePanelWidth = _device.Width,
            DeviceTerminalHeight = _terminalDock.Height,
            ScrcpyPath = _scrcpyPath,
            TerminalPowerShell = _terminalPowerShell,
            Language = Loc.Language,
            AutoSaveFiles = _autoSaveFiles,
        };

        if (_savedSettings is null ||
            _savedSettings.DevicePanelOpen != settings.DevicePanelOpen ||
            _savedSettings.DevicePanelWidth != settings.DevicePanelWidth ||
            _savedSettings.DeviceTerminalHeight != settings.DeviceTerminalHeight ||
            _savedSettings.ScrcpyPath != settings.ScrcpyPath ||
            _savedSettings.TerminalPowerShell != settings.TerminalPowerShell ||
            _savedSettings.Language != settings.Language ||
            _savedSettings.AutoSaveFiles != settings.AutoSaveFiles ||
            _savedSettings.ShowLineNumbers != settings.ShowLineNumbers ||
            _savedSettings.Theme != settings.Theme ||
            _savedSettings.DirectionMode != settings.DirectionMode ||
            _savedSettings.EditorFont != settings.EditorFont ||
            Math.Abs(_savedSettings.EditorFontSize - settings.EditorFontSize) > 0.01f)
        {
            _store.SaveSettings(settings);
            _savedSettings = settings;
        }
    }

    private SessionState BuildSessionState()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            bounds = new Rectangle(120, 80, 1040, 640);
        }

        var active = ActiveDocument;

        return new SessionState
        {
            Theme = _theme.Key,
            DirectionMode = _directionMode switch
            {
                TextDirectionMode.LeftToRight => "ltr",
                TextDirectionMode.RightToLeft => "rtl",
                _ => "auto",
            },
            EditorFont = _editorFontFamily,
            EditorFontSize = _editorFontSize,
            ShowLineNumbers = _showLineNumbers,
            ActiveTabIndex = active is null ? 0 : Math.Max(0, _documents.IndexOf(active)),
            WindowLeft = bounds.Left,
            WindowTop = bounds.Top,
            WindowWidth = bounds.Width,
            WindowHeight = bounds.Height,
            Maximized = WindowState == FormWindowState.Maximized,
            Tabs = _documents.Select(document => new SessionTab
            {
                Id = document.Id,
                FilePath = document.FilePath,
                Title = document.Title,
                Language = document.LanguageKey,
                Rtl = document.IsRtl,
                WordWrap = document.WordWrap,
                Encoding = document.FileEncodingKey,
                NewLine = document.NewLine,
                Caret = document.Editor.SelectionStart,
            }).ToList(),
        };
    }

    private void NewDocument()
    {
        var document = new EditorTab(
            Guid.NewGuid().ToString("N"),
            NextUntitledTitle(),
            _theme,
            Languages.Plain.Key,
            _directionMode,
            wordWrap: false,
            fontFamily: _editorFontFamily,
            fontSize: _editorFontSize);

        document.LoadContent(string.Empty, dirty: false, caretPosition: 0);
        AttachDocument(document, select: true);
        document.Editor.Focus();
    }

    private string NextUntitledTitle()
    {
        // Find the first unused "Untitled N" number instead of always incrementing.
        // This keeps numbering stable even after closing tabs.
        var usedNumbers = new HashSet<int>();
        foreach (var document in _documents)
        {
            if (document.FilePath is null &&
                document.Title.StartsWith("Untitled ", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(document.Title.AsSpan("Untitled ".Length), out var num))
            {
                usedNumbers.Add(num);
            }
        }

        var next = 1;
        while (usedNumbers.Contains(next))
        {
            next++;
        }

        return $"Untitled {next}";
    }

    private void CloseActiveDocument()
    {
        var document = ActiveDocument;
        if (document is not null)
        {
            CloseDocument(document);
        }
    }

    private void CloseDocument(EditorTab document)
    {
        if (!_documents.Contains(document))
        {
            return;
        }

        // Skip save prompt for empty, untitled documents that were never modified.
        // This prevents the save dialog from appearing when closing a blank new tab.
        var shouldPromptForSave = document.IsDirty &&
                                  (document.FilePath is not null ||
                                   !string.IsNullOrWhiteSpace(document.Editor.Text));

        if (shouldPromptForSave)
        {
            var result = ThemedMessageBox.Show(
                this,
                _theme,
                Loc.F("Save changes to \"{0}\"?", Loc.DisplayTitle(document.Title)) + "\n" +
                    Loc.T("Your changes will be lost if you don't save them."),
                AppName,
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                new[] { Loc.T("Save"), Loc.T("Don't save"), Loc.T("Cancel") },
                noIsDestructive: true);

            if (result == DialogResult.Cancel)
            {
                return;
            }

            if (result == DialogResult.Yes && !SaveDocument(document, saveAs: false))
            {
                return;
            }
        }

        _pendingAutosave.Remove(document.Id);
        _store.DeleteRecovery(document.Id);

        // Detach event handlers before disposal to prevent calls on a disposed object.
        document.Editor.MouseDown -= Editor_MouseDown;
        document.Editor.MouseUp -= Editor_MouseUp;

        // Closing the active tab moves to its neighbour (the tab that slides into
        // its place) instead of jumping to the first tab.
        var closedIndex = _documents.IndexOf(document);
        if (ReferenceEquals(ActiveDocument, document) && _documents.Count > 1)
        {
            var neighbourIndex = closedIndex + 1 < _documents.Count ? closedIndex + 1 : closedIndex - 1;
            _tabs.SelectedTab = _documents[neighbourIndex].Page;
        }

        _documents.Remove(document);
        _tabs.TabPages.Remove(document.Page);
        document.Dispose();

        if (_documents.Count == 0)
        {
            NewDocument();
        }
        else
        {
            UpdateTabUi();
            UpdateWindowTitle();
            SaveSessionState();
            ActiveDocument?.Editor.Focus();
        }
    }

    private void OpenFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = FileFilter,
            Title = Loc.T("Open file"),
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        foreach (var path in dialog.FileNames)
        {
            OpenFile(path);
        }
    }

    private void OpenFile(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var existing = _documents.FirstOrDefault(document =>
                document.FilePath is not null &&
                string.Equals(
                    Path.GetFullPath(document.FilePath),
                    fullPath,
                    StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _tabs.SelectedTab = existing.Page;
                existing.Editor.Focus();
                return;
            }

            var file = TextFile.Read(fullPath);
            var content = file.Text;
            var language = Languages.FromExtension(fullPath);

            var document = new EditorTab(
                Guid.NewGuid().ToString("N"),
                Path.GetFileName(fullPath),
                _theme,
                language.Key,
                _directionMode,
                wordWrap: false,
                fontFamily: _editorFontFamily,
                fontSize: _editorFontSize);

            document.FileEncodingKey = file.EncodingKey;
            document.NewLine = file.NewLine;
            document.SetFilePath(fullPath);
            document.LoadContent(content, dirty: false, caretPosition: 0);
            AttachDocument(document, select: true);
            document.Editor.Focus();
        }
        catch (Exception exception)
        {
            ThemedMessageBox.Show(
                this,
                _theme,
                Loc.F("Could not open the file:\n{0}\n\n{1}", path, exception.Message),
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void SaveActive(bool saveAs)
    {
        var document = ActiveDocument;
        if (document is not null)
        {
            SaveDocument(document, saveAs);
        }
    }

    private bool SaveDocument(EditorTab document, bool saveAs)
    {
        var path = document.FilePath;

        if (saveAs || string.IsNullOrWhiteSpace(path))
        {
            var language = Languages.Get(document.LanguageKey);
            var extension = language.DefaultExtension;
            var suggestedName = document.FilePath is not null
                ? Path.GetFileName(document.FilePath)
                : Path.HasExtension(document.Title)
                    ? document.Title
                    : document.Title + extension;

            using var dialog = new SaveFileDialog
            {
                Filter = FileFilter,
                Title = saveAs ? "Save As" : "Save",
                FileName = suggestedName,
                DefaultExt = extension.TrimStart('.'),
                AddExtension = true,
                OverwritePrompt = true,
                FilterIndex = 1,
            };

            if (!string.IsNullOrWhiteSpace(document.FilePath))
            {
                var directory = Path.GetDirectoryName(document.FilePath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    dialog.InitialDirectory = directory;
                }
            }

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return false;
            }

            path = dialog.FileName;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            // Keep the file's own encoding and line endings (CRLF / LF, BOM, ANSI).
            document.FileEncodingKey = TextFile.Write(
                path,
                document.Editor.Text,
                document.FileEncodingKey,
                document.NewLine);
        }
        catch (Exception exception)
        {
            ThemedMessageBox.Show(
                this,
                _theme,
                Loc.F("Could not save the file:\n{0}\n\n{1}", path, exception.Message),
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        document.SetFilePath(path);
        document.MarkClean();
        _pendingAutosave.Remove(document.Id);

        // The recovery copy has served its purpose after a real save.
        _store.DeleteRecovery(document.Id);

        UpdateTabUi();
        UpdateWindowTitle();
        SaveSessionState();
        return true;
    }

    private void ToggleTheme()
    {
        ApplyTheme(AppTheme.Toggle(_theme));
    }

    private void SetDirectionMode(TextDirectionMode mode)
    {
        _directionMode = mode;

        foreach (var document in _documents)
        {
            document.SetDirectionMode(mode);
        }

        UpdateDirectionMenu();
        UpdateTabUi();
        SaveSessionState();
    }

    private void CycleDirectionMode()
    {
        var next = _directionMode switch
        {
            TextDirectionMode.Automatic => TextDirectionMode.LeftToRight,
            TextDirectionMode.LeftToRight => TextDirectionMode.RightToLeft,
            _ => TextDirectionMode.Automatic,
        };

        SetDirectionMode(next);
    }

    private void UpdateDirectionMenu()
    {
        _directionAutoItem.Checked = _directionMode == TextDirectionMode.Automatic;
        _directionLtrItem.Checked = _directionMode == TextDirectionMode.LeftToRight;
        _titleBar.DirectionState = _directionMode switch { TextDirectionMode.LeftToRight => 1, TextDirectionMode.RightToLeft => 2, _ => 0 };
        _directionRtlItem.Checked = _directionMode == TextDirectionMode.RightToLeft;
    }

    private void ChooseEditorFont()
    {
        if (ThemedFontDialog.Pick(this, _theme, _editorFontFamily, _editorFontSize, out var family, out var size))
        {
            SetEditorFont(family, size);
        }
    }

    private void SetEditorFont(string family, float size)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            family = EditorTab.DefaultFontFamily;
        }

        size = Math.Clamp(size, 7f, 36f);

        if (string.Equals(_editorFontFamily, family, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(_editorFontSize - size) < 0.05f)
        {
            return;
        }

        _editorFontFamily = family;
        _editorFontSize = size;

        foreach (var document in _documents)
        {
            document.SetBaseFont(family, size);
        }

        _terminalDock.FontSize = size - 1f;
        UpdateFontMenu();
        SaveSessionState();
    }

    private void ChangeFontSize(float delta)
    {
        var next = Math.Clamp(_editorFontSize + delta, 7f, 36f);
        if (Math.Abs(next - _editorFontSize) < 0.05f)
        {
            return;
        }

        SetEditorFont(_editorFontFamily, next);
    }

    private void ResetFontSize()
    {
        SetEditorFont(EditorTab.DefaultFontFamily, EditorTab.DefaultFontSize);
    }

    private void UpdateFontMenu()
    {
        _fontMenu.Text = $"Editor &font — {_editorFontFamily} {_editorFontSize:0.#} pt";
    }

    private void TitleBar_ActionInvoked(object? sender, TitleBarAction action)
    {
        switch (action)
        {
            case TitleBarAction.Undo:
                ActiveEditor?.Undo();
                break;

            case TitleBarAction.Redo:
                ActiveEditor?.Redo();
                break;

            case TitleBarAction.Save:
                SaveActive(saveAs: false);
                break;

            case TitleBarAction.Find:
                ShowFind();
                break;

            case TitleBarAction.ZoomOut:
                ChangeFontSize(-1f);
                break;

            case TitleBarAction.ZoomIn:
                ChangeFontSize(1f);
                break;

            case TitleBarAction.Theme:
                ToggleTheme();
                break;

            case TitleBarAction.Direction:
                CycleDirectionMode();
                break;

            case TitleBarAction.Font:
                ChooseEditorFont();
                break;
        }
    }

    private void Editor_MouseDown(object? sender, MouseEventArgs e)
    {
        _rightClickText = null;

        if (e.Button != MouseButtons.Right || sender is not RichTextBox editor)
        {
            return;
        }

        var document = _documents.FirstOrDefault(item => ReferenceEquals(item.Editor, editor));
        if (document is not null && !ReferenceEquals(ActiveDocument, document))
        {
            _tabs.SelectedTab = document.Page;
        }

        // Capture the selection immediately on right-click before it can be lost.
        // This ensures copy works even if the RichTextBox clears the selection.
        if (editor.SelectionLength > 0)
        {
            _rightClickText = editor.SelectedText;
        }
        else
        {
            // Nothing selected: the paste goes where the user clicked.
            editor.Select(editor.GetCharIndexFromPosition(e.Location), 0);
        }
    }

    private void Editor_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || sender is not RichTextBox editor)
        {
            return;
        }

        var text = _rightClickText;
        _rightClickText = null;

        try
        {
            if (!string.IsNullOrEmpty(text))
            {
                // Right-click on a selection copies it.
                Clipboard.SetText(text);
            }
            else if (Clipboard.ContainsText())
            {
                // Right-click without a selection pastes the clipboard at the caret.
                editor.Paste();
            }
        }
        catch
        {
            // The clipboard can be locked by another process; ignore silently.
        }
    }

    private void ToggleLineNumbers()
    {
        _showLineNumbers = !_showLineNumbers;

        foreach (var document in _documents)
        {
            document.SetLineNumbers(_showLineNumbers);
        }

        UpdateTabUi();
        SaveSessionState();
    }

    private void ToggleWordWrap()
    {
        var document = ActiveDocument;
        if (document is null)
        {
            return;
        }

        document.SetWordWrap(!document.WordWrap);
        UpdateTabUi();
        SaveSessionState();
    }

    private void SetLanguage(string languageKey)
    {
        var document = ActiveDocument;
        if (document is null)
        {
            return;
        }

        document.SetLanguage(languageKey);
        UpdateTabUi();
        SaveSessionState();
    }

    private void ApplyTheme(AppTheme theme, bool persist = true)
    {
        _theme = theme;

        BackColor = theme.Window;
        _root.BackColor = theme.Window;

        _titleBar.Theme = theme;
        _titleBar.BackColor = theme.TabStripBack;

        var renderer = new ThemedRenderer(theme);
        _appMenu.Renderer = renderer;
        ToolStripManager.Renderer = renderer;

        _appMenu.BackColor = theme.MenuBack;
        _appMenu.ForeColor = theme.MenuFore;
        ApplyItemColors(_appMenu.Items, theme.MenuFore);

        _findPanel.BackColor = theme.ToolBack;
        _findFlow.BackColor = theme.ToolBack;
        _findLabel.ForeColor = theme.ToolFore;
        _findBox.BackColor = theme.EditorBack;
        _findBox.ForeColor = theme.EditorFore;
        ThemeButton(_findNextButton, theme);
        ThemeButton(_findCloseButton, theme);
        ThemeFindExtras(theme);

        _tabs.Theme = theme;
        _tabs.BackColor = theme.EditorBack;
        _work.BackColor = theme.EditorBack;
        _device.Theme = theme;
        _settingsView.Theme = theme;
        _terminalDock.Theme = theme;

        foreach (var document in _documents)
        {
            document.ApplyTheme(theme);
        }

        _darkThemeMenuItem.Checked = theme.IsDark;

        if (IsHandleCreated)
        {
            NativeMethods.TrySetDarkTitleBar(Handle, theme.IsDark);
        }

        UpdateTabUi();
        Invalidate(true);

        if (persist)
        {
            SaveSessionState();
        }
    }

    private static void ApplyItemColors(ToolStripItemCollection items, Color foreground)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = foreground;

            if (item is ToolStripMenuItem menuItem)
            {
                ApplyItemColors(menuItem.DropDownItems, foreground);
            }
        }
    }

    private static void ThemeButton(Button button, AppTheme theme)
    {
        button.BackColor = theme.ToolBack;
        button.ForeColor = theme.ToolFore;
        button.FlatAppearance.BorderColor = theme.Border;
        button.FlatAppearance.MouseOverBackColor = theme.MenuHover;
        button.FlatAppearance.MouseDownBackColor = theme.MenuHover;
    }

    private void UpdateTabUi()
    {
        var document = ActiveDocument;

        if (document is null)
        {
            _wordWrapMenuItem.Checked = false;

            foreach (var item in _languageMenuItems.Values)
            {
                item.Checked = false;
            }

            RefreshTitleTabs();
            return;
        }

        _wordWrapMenuItem.Checked = document.WordWrap;
        _lineNumbersMenuItem.Checked = _showLineNumbers;
        UpdateDirectionMenu();

        foreach (var pair in _languageMenuItems)
        {
            pair.Value.Checked = string.Equals(
                pair.Key,
                document.LanguageKey,
                StringComparison.OrdinalIgnoreCase);
        }

        RefreshTitleTabs();
    }

    private void UpdateWindowTitle()
    {
        var document = ActiveDocument;
        var title = document is null
            ? AppName
            : $"{Loc.DisplayTitle(document.Title)}{(document.IsDirty ? " ●" : string.Empty)} — {AppName}";

        if (Text != title)
        {
            Text = title;
        }

        _titleBar.Caption = title;
    }





    private void CycleDocument(bool reverse)
    {
        if (_documents.Count < 2)
        {
            return;
        }

        var active = ActiveDocument;
        var index = active is null ? 0 : _documents.IndexOf(active);
        index = (index + (reverse ? -1 : 1) + _documents.Count) % _documents.Count;
        _tabs.SelectedTab = _documents[index].Page;
    }

    private void MainForm_Shown(object? sender, EventArgs e)
    {
        if (_devicePanelOpen)
        {
            SetDevicePanel(true);
        }

        ActiveDocument?.Editor.Focus();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _autosaveTimer.Stop();

        foreach (var document in _documents)
        {
            if (!document.IsDirty)
            {
                continue;
            }

            // Silent, unconditional recovery: closing the window or shutting Windows
            // down never destroys unsaved text. The user can still save explicitly later.
            _store.WriteRecovery(document.Id, document.Editor.Text);
            document.SetAutosaved(DateTime.Now);
        }

        SaveSessionState();

        // The documents are about to be disposed; nothing may touch them again.
        _closed = true;
        _device.Shutdown();
        _terminalDock.Shutdown();
    }

    private void ShowAbout()
    {
        ThemedMessageBox.Show(
            this,
            _theme,
            $"{AppName} 1.0\n\n" +
            "A code editor, a notepad and a phone screen mirror in one app, connected through a single cable.\n" +
            "Get the most out of your time and be more productive.\n\n" +
            "Ctrl+N  New tab\n" +
            "Ctrl+O  Open\n" +
            "Ctrl+S  Save\n" +
            "Ctrl+Shift+S  Save As\n" +
            "Ctrl+W  Close tab\n" +
            "Ctrl+F  Find\n" +
            "Ctrl+R  Text direction: auto / LTR / RTL\n" +
            "Ctrl+Shift+Left  Always LTR\n" +
            "Ctrl+Shift+Right  Always RTL\n" +
            "Ctrl++ / Ctrl+-  Larger / smaller text\n" +
            "Ctrl+0  Reset text size\n" +
            "Ctrl+D  Toggle dark/light theme\n" +
            "Ctrl+Tab  Next tab\n" +
            "Double-click tab  Rename\n" +
            "Right-click a selection  Copy\n\n" +
            $"Recovery folder:\n{_store.RootDirectory}",
            $"About {AppName}",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private string? ShowRenameDialog(string currentName)
    {
        using var dialog = new Form
        {
            Text = "Rename tab",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(320, 90),
            Font = Font,
        };

        var label = new Label
        {
            Text = "Tab name:",
            AutoSize = true,
            Location = new Point(12, 12),
        };

        var textBox = new TextBox
        {
            Text = currentName,
            Location = new Point(12, 38),
            Size = new Size(296, 27),
            MaxLength = 200,
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(140, 60),
            Size = new Size(75, 26),
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(233, 60),
            Size = new Size(75, 26),
        };

        dialog.Controls.Add(label);
        dialog.Controls.Add(textBox);
        dialog.Controls.Add(okButton);
        dialog.Controls.Add(cancelButton);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        textBox.SelectAll();

        return dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text)
            ? textBox.Text.Trim()
            : null;
    }
}

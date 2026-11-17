using System;
using KekyNotepad.Services;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>Buttons in the title-bar action strip.</summary>
public enum TitleBarAction
{
    Undo,
    Redo,
    Save,
    Find,
    ZoomOut,
    ZoomIn,
    Theme,
    Font,
    Direction,
}

/// <summary>One document tab rendered by <see cref="TitleTabBar"/>.</summary>
public sealed class TitleTabItem
{
    public TitleTabItem(string title)
    {
        Title = title ?? string.Empty;
    }

    public string Title { get; set; }
    public string? ToolTip { get; set; }
    public bool IsRtl { get; set; }
    public bool IsDirty { get; set; }
    public object? Tag { get; set; }

    internal int TabWidth { get; set; }
    internal Rectangle Bounds { get; set; }
    internal Rectangle CloseBounds { get; set; }
}

/// <summary>
/// Two-row window header.
///
///  Row 1 (tools):  logo, action icons, window title, menu and the custom
///                  minimize / maximize / close buttons. Empty space drags the window.
///  Row 2 (tabs):   document tabs and the + button. Tabs shrink to share the
///                  available width, so every tab stays reachable.
///
/// The control is purely a view. It reports clicks through events so the form
/// keeps all of the document and window logic in one place.
/// </summary>
public sealed class TitleTabBar : Control
{
    /// <summary>Height of the tools row at 100% DPI.</summary>
    public const int ToolRowHeight = 40;

    /// <summary>Height of the tab row at 100% DPI.</summary>
    public const int TabRowHeight = 38;

    private enum CaptionGlyph
    {
        Minimize,
        Maximize,
        Restore,
        Close,
    }

    private static readonly TitleBarAction[] ActionOrder =
    {
        TitleBarAction.Undo,
        TitleBarAction.Redo,
        TitleBarAction.Save,
        TitleBarAction.Find,
        TitleBarAction.ZoomOut,
        TitleBarAction.ZoomIn,
        TitleBarAction.Theme,
        TitleBarAction.Font,
        TitleBarAction.Direction,
    };

    private readonly List<TitleTabItem> _tabs = new();
    private readonly Dictionary<TitleBarAction, Rectangle> _actionBounds = new();
    private readonly ToolTip _toolTip = new() { ShowAlways = true };

    private AppTheme _theme = AppTheme.Light;
    private string _caption = string.Empty;
    private int _selectedIndex = -1;
    private int _hoverIndex = -1;
    private bool _hoverCloseGlyph;
    private bool _hoverNewTab;
    private bool _hoverMenu;
    private bool _hoverMinimize;
    private bool _hoverMaximize;
    private bool _hoverCloseWindow;
    private bool _isMaximized;
    private int _scrollOffset;
    private int _doubleClickTab = -1;
    private int _toolHeight;
    private int _tabTop;
    private int _tabHeight;
    private int _tabAreaLeft;
    private int _tabAreaRight;
    private int _contentWidth;
    private int _actionsLeft;
    private int _actionsRight;
    private int _visibleActionCount;
    private TitleBarAction? _hoverAction;
    private TitleBarAction? _pressedAction;
    private string _toolTipText = string.Empty;

    private Rectangle _newTabBounds;
    private Rectangle _deviceBounds;
    private bool _hoverDevice;
    private bool _deviceOpen;
    private Rectangle _menuBounds;
    private Rectangle _minimizeBounds;
    private Rectangle _maximizeBounds;
    private Rectangle _closeBounds;

    public TitleTabBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        // The title bar should never steal keyboard focus from the editor.
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Height = ToolRowHeight + TabRowHeight;
    }

    public event EventHandler<int>? TabSelected;
    public event EventHandler<int>? TabCloseRequested;
    public event EventHandler? NewTabRequested;
    public event EventHandler? DeviceTabRequested;

    /// <summary>True while the device panel (scrcpy + terminal) is shown; highlights the pinned tab.</summary>
    public bool DeviceOpen
    {
        get => _deviceOpen;
        set
        {
            if (_deviceOpen != value)
            {
                _deviceOpen = value;
                Invalidate();
            }
        }
    }
    public event EventHandler? MenuRequested;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? MaximizeRestoreRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler<TitleBarAction>? ActionInvoked;
    public event EventHandler<int>? RenameRequested;

    public event EventHandler<TabRenamedEventArgs>? TabRenamed;

    private Panel? _renameHost;
    private TextBox? _renameBox;
    private int _renameIndex = -1;

    /// <summary>Edits a tab's name in place (double-click): a rounded, themed field over the tab.</summary>
    public void BeginRename(int index, string current)
    {
        EndRename(false);
        if (index < 0 || index >= _tabs.Count)
        {
            return;
        }

        var bounds = _tabs[index].Bounds;
        var host = new Panel
        {
            BackColor = _theme.EditorBack,
            Bounds = new Rectangle(
                bounds.Left + Px(5),
                _tabTop + Px(6),
                Math.Max(bounds.Width - Px(10), Px(120)),
                _tabHeight - Px(10)),
        };

        var box = new TextBox
        {
            Text = current,
            Font = Font,
            BorderStyle = BorderStyle.None,
            BackColor = _theme.EditorBack,
            ForeColor = _theme.TabActiveFore,
            MaxLength = 200,
            RightToLeft = Services.Loc.IsArabic ? RightToLeft.Yes : RightToLeft.No,
        };

        host.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, host.Width - 1, host.Height - 1);
            using var path = RoundedRectangle(rect, Px(7));
            using var fill = new SolidBrush(_theme.EditorBack);
            e.Graphics.FillPath(fill, path);
            using var pen = new Pen(_theme.Accent, Math.Max(1.5f, Px(1.5f)));
            e.Graphics.DrawPath(pen, path);
        };

        void LayoutBox()
        {
            var height = box.PreferredHeight;
            box.SetBounds(Px(10), Math.Max(0, (host.Height - height) / 2), Math.Max(10, host.Width - Px(20)), height);
        }

        host.Resize += (_, _) => LayoutBox();
        host.Controls.Add(box);
        LayoutBox();

        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                EndRename(true);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                EndRename(false);
            }
        };
        box.LostFocus += (_, _) => EndRename(true);

        _renameHost = host;
        _renameBox = box;
        _renameIndex = index;
        Controls.Add(host);
        host.BringToFront();
        box.Focus();
        box.SelectAll();
    }

    private void EndRename(bool commit)
    {
        var box = _renameBox;
        var host = _renameHost;
        if (box is null || host is null)
        {
            return;
        }

        var index = _renameIndex;
        _renameBox = null;
        _renameHost = null;
        _renameIndex = -1;
        var text = box.Text.Trim();
        Controls.Remove(host);
        host.Dispose();
        Invalidate();

        if (commit && text.Length > 0)
        {
            TabRenamed?.Invoke(this, new TabRenamedEventArgs(index, text));
        }
    }

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? AppTheme.Light;
            Invalidate();
        }
    }

    /// <summary>Text shown in the middle of the tools row (current document).</summary>
    public string Caption
    {
        get => _caption;
        set
        {
            value ??= string.Empty;
            if (_caption == value)
            {
                return;
            }

            _caption = value;
            Invalidate();
        }
    }

    public bool IsMaximized
    {
        get => _isMaximized;
        set
        {
            if (_isMaximized == value)
            {
                return;
            }

            _isMaximized = value;
            Invalidate();
        }
    }

    public Rectangle MenuButtonBounds => _menuBounds;

    public void SetTabs(IReadOnlyList<TitleTabItem> items, int selectedIndex)
    {
        _tabs.Clear();

        if (items is not null)
        {
            _tabs.AddRange(items);
        }

        _selectedIndex = selectedIndex >= 0 && selectedIndex < _tabs.Count
            ? selectedIndex
            : (_tabs.Count > 0 ? 0 : -1);

        if (_hoverIndex >= _tabs.Count)
        {
            _hoverIndex = -1;
            _hoverCloseGlyph = false;
        }

        LayoutTabs();
        EnsureVisible(_selectedIndex);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        LayoutTabs();

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var barBrush = new SolidBrush(_theme.TabStripBack))
        {
            graphics.FillRectangle(barBrush, ClientRectangle);
        }

        // Hairline under the whole header; the active tab paints over it so the
        // tab visually joins the editor below.
        using (var baseline = new Pen(_theme.Border))
        {
            graphics.DrawLine(baseline, 0, Height - 1, Width, Height - 1);
        }

        DrawLogo(graphics);
        DrawActions(graphics);
        DrawCaption(graphics);
        DrawMenuButton(graphics);
        DrawCaptionButton(graphics, _minimizeBounds, CaptionGlyph.Minimize, _hoverMinimize);
        DrawCaptionButton(
            graphics,
            _maximizeBounds,
            _isMaximized ? CaptionGlyph.Restore : CaptionGlyph.Maximize,
            _hoverMaximize);
        DrawCaptionButton(graphics, _closeBounds, CaptionGlyph.Close, _hoverCloseWindow);

        // Tabs are clipped so a scrolled tab can never paint outside its row.
        var state = graphics.Save();
        graphics.SetClip(
            new Rectangle(_tabAreaLeft, _tabTop, Math.Max(0, _tabAreaRight - _tabAreaLeft), Height - _tabTop),
            CombineMode.Intersect);

        for (var index = 0; index < _tabs.Count; index++)
        {
            if (index != _selectedIndex)
            {
                DrawTab(graphics, index);
            }
        }

        // The active tab goes last so its outline wins over neighbours.
        if (_selectedIndex >= 0 && _selectedIndex < _tabs.Count)
        {
            DrawTab(graphics, _selectedIndex);
        }

        graphics.Restore(state);

        DrawNewTab(graphics);
        DrawDeviceTab(graphics);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        LayoutTabs();

        var point = e.Location;
        var tabIndex = HitTestTab(point);
        var closeGlyph = tabIndex >= 0 && _tabs[tabIndex].CloseBounds.Contains(point);
        var action = HitTestAction(point);

        var overNewTab = _newTabBounds.Contains(point);
        var overDevice = _deviceBounds.Contains(point);
        var overMenu = _menuBounds.Contains(point);
        var overMinimize = _minimizeBounds.Contains(point);
        var overMaximize = _maximizeBounds.Contains(point);
        var overClose = _closeBounds.Contains(point);

        if (tabIndex != _hoverIndex ||
            closeGlyph != _hoverCloseGlyph ||
            action != _hoverAction ||
            overNewTab != _hoverNewTab ||
            overDevice != _hoverDevice ||
            overMenu != _hoverMenu ||
            overMinimize != _hoverMinimize ||
            overMaximize != _hoverMaximize ||
            overClose != _hoverCloseWindow)
        {
            _hoverIndex = tabIndex;
            _hoverCloseGlyph = closeGlyph;
            _hoverAction = action;
            _hoverNewTab = overNewTab;
            _hoverDevice = overDevice;
            _hoverMenu = overMenu;
            _hoverMinimize = overMinimize;
            _hoverMaximize = overMaximize;
            _hoverCloseWindow = overClose;
            Invalidate();
        }

        Cursor = tabIndex >= 0 || overNewTab || overDevice || overMenu || action is not null ||
                 overMinimize || overMaximize || overClose
            ? Cursors.Hand
            : Cursors.Default;

        UpdateToolTip(tabIndex);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoverIndex == -1 &&
            !_hoverCloseGlyph &&
            !_hoverNewTab &&
            !_hoverDevice &&
            !_hoverMenu &&
            _hoverAction is null &&
            !_hoverMinimize &&
            !_hoverMaximize &&
            !_hoverCloseWindow)
        {
            return;
        }

        _hoverIndex = -1;
        _hoverCloseGlyph = false;
        _hoverNewTab = false;
        _hoverDevice = false;
        _hoverMenu = false;
        _hoverAction = null;
        _hoverMinimize = false;
        _hoverMaximize = false;
        _hoverCloseWindow = false;
        _pressedAction = null;
        Cursor = Cursors.Default;
        UpdateToolTip(-1);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        EndRename(true);
        LayoutTabs();

        var point = e.Location;
        var form = FindForm();

        var pressedAction = HitTestAction(e.Location);
        if (pressedAction is not null)
        {
            _pressedAction = pressedAction;
            Invalidate();
        }

        if (e.Button == MouseButtons.Left)
        {
            _doubleClickTab = e.Clicks >= 2 ? HitTestTab(point) : -1;
            // Buttons and tabs react on mouse up (like real window buttons).
            // Only empty header space starts a drag / double-click.
            if (_closeBounds.Contains(point) ||
                _minimizeBounds.Contains(point) ||
                _maximizeBounds.Contains(point) ||
                _menuBounds.Contains(point) ||
                HitTestAction(point) is not null ||
                _deviceBounds.Contains(point) ||
                (!_newTabBounds.IsEmpty && _newTabBounds.Contains(point)) ||
                HitTestTab(point) >= 0)
            {
                return;
            }

            if (e.Clicks >= 2)
            {
                if (point.Y >= _tabTop)
                {
                    // Double-click on empty tab-row space opens a new tab (like browsers).
                    NewTabRequested?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    MaximizeRestoreRequested?.Invoke(this, EventArgs.Empty);
                }

                return;
            }

            if (form is not null)
            {
                NativeMethods.StartWindowDrag(form.Handle);
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        LayoutTabs();

        var point = e.Location;

        if (e.Button == MouseButtons.Left)
        {
            _pressedAction = null;

            if (_closeBounds.Contains(point))
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_minimizeBounds.Contains(point))
            {
                MinimizeRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_maximizeBounds.Contains(point))
            {
                MaximizeRestoreRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_menuBounds.Contains(point))
            {
                MenuRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            var action = HitTestAction(point);
            if (action is not null)
            {
                ActionInvoked?.Invoke(this, action.Value);
                return;
            }

            if (_deviceBounds.Contains(point))
            {
                DeviceTabRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!_newTabBounds.IsEmpty && _newTabBounds.Contains(point))
            {
                NewTabRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            var tabIndex = HitTestTab(point);
            if (tabIndex >= 0)
            {
                if (_tabs[tabIndex].CloseBounds.Contains(point))
                {
                    TabCloseRequested?.Invoke(this, tabIndex);
                    return;
                }

                if (_doubleClickTab == tabIndex)
                {
                    _doubleClickTab = -1;
                    RenameRequested?.Invoke(this, tabIndex);
                    return;
                }

                TabSelected?.Invoke(this, tabIndex);
            }

            return;
        }

        if (e.Button == MouseButtons.Middle)
        {
            var tabIndex = HitTestTab(point);
            if (tabIndex >= 0)
            {
                TabCloseRequested?.Invoke(this, tabIndex);
            }
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        LayoutTabs();

        var visibleWidth = _tabAreaRight - _tabAreaLeft;
        if (_contentWidth <= visibleWidth)
        {
            base.OnMouseWheel(e);
            return;
        }

        var step = Math.Max(1, Math.Abs(e.Delta) / 120) * Px(70) * Math.Sign(e.Delta);
        var maxOffset = Math.Max(0, _contentWidth - visibleWidth);
        _scrollOffset = Math.Clamp(_scrollOffset - step, 0, maxOffset);
        LayoutTabs();
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutTabs();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ drawing

    private void DrawLogo(Graphics graphics)
    {
        var size = Px(24);
        var badge = new Rectangle(Px(14), (_toolHeight - size) / 2, size, size);

        using (var path = RoundedRectangle(badge, Px(7)))
        using (var brush = new LinearGradientBrush(
                   badge,
                   _theme.Accent,
                   ControlPaint.Dark(_theme.Accent, 0.15f),
                   LinearGradientMode.ForwardDiagonal))
        {
            graphics.FillPath(brush, path);
        }

        using var font = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
        TextRenderer.DrawText(
            graphics,
            "K",
            font,
            badge,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawCaption(Graphics graphics)
    {
        if (string.IsNullOrEmpty(_caption))
        {
            return;
        }

        var left = _actionsRight + Px(18);
        var right = _menuBounds.Left - Px(8);
        if (right - left < Px(120))
        {
            return;
        }

        TextRenderer.DrawText(
            graphics,
            _caption,
            Font,
            new Rectangle(left, 0, right - left, _toolHeight),
            _theme.TabInactiveFore,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
    }

    private void DrawTab(Graphics graphics, int index)
    {
        var tab = _tabs[index];
        var bounds = tab.Bounds;
        if (bounds.Width <= 0 || bounds.Right <= _tabAreaLeft || bounds.Left >= _tabAreaRight)
        {
            return;
        }

        var active = index == _selectedIndex && !_deviceOpen;
        var hovered = index == _hoverIndex;

        if (active)
        {
            // Rounded-top "folder" tab that melts into the editor area.
            var shape = new Rectangle(
                bounds.Left + Px(2),
                _tabTop + Px(4),
                bounds.Width - Px(4),
                Height - _tabTop - Px(4));

            using (var path = TopRoundedRectangle(shape, Px(9)))
            using (var brush = new SolidBrush(_theme.EditorBack))
            using (var outline = new Pen(_theme.Border))
            {
                graphics.FillPath(brush, path);
                graphics.DrawPath(outline, path);
            }

            // Cover the outline's bottom edge so the tab and editor become one surface.
            using (var seam = new SolidBrush(_theme.EditorBack))
            {
                graphics.FillRectangle(seam, shape.Left + 1, Height - 1, shape.Width - 1, 2);
            }

            using var accent = new Pen(_theme.Accent, Math.Max(2f, Px(2f)))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            graphics.DrawLine(
                accent,
                shape.Left + Px(12),
                shape.Top + 1.5f,
                shape.Right - Px(12),
                shape.Top + 1.5f);
        }
        else if (hovered)
        {
            var pill = new Rectangle(
                bounds.Left + Px(3),
                _tabTop + Px(7),
                bounds.Width - Px(6),
                _tabHeight - Px(13));

            using var path = RoundedRectangle(pill, Px(7));
            using var brush = new SolidBrush(_theme.TabHoverBack);
            graphics.FillPath(brush, path);
        }
        else if (index + 1 != _selectedIndex)
        {
            // Thin divider between two resting tabs.
            using var separator = new Pen(_theme.Border);
            graphics.DrawLine(
                separator,
                bounds.Right - 1,
                _tabTop + Px(12),
                bounds.Right - 1,
                Height - Px(10));
        }

        var showClose = active || hovered;
        var foreground = active ? _theme.TabActiveFore : _theme.TabInactiveFore;
        var showDot = tab.IsDirty && bounds.Width >= Px(90);

        var rightReserve = (showClose ? Px(34) : Px(12)) + (showDot ? Px(12) : 0);
        var textRight = Math.Min(bounds.Right, _tabAreaRight);
        var textBounds = new Rectangle(
            bounds.Left + Px(14),
            _tabTop + Px(2),
            Math.Max(0, textRight - bounds.Left - Px(14) - rightReserve),
            _tabHeight - Px(2));

        var flags = TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix |
                    TextFormatFlags.SingleLine;

        if (tab.IsRtl)
        {
            flags |= TextFormatFlags.RightToLeft;
        }

        TextRenderer.DrawText(graphics, tab.Title, Font, textBounds, foreground, flags);

        if (showDot)
        {
            // A fixed-position dot, so it never jumps into the leading edge of
            // right-to-left titles.
            var dotSize = Px(6);
            var dotX = showClose
                ? bounds.Right - Px(9) - Px(20) - Px(8)
                : bounds.Right - Px(14);
            using var dotBrush = new SolidBrush(active ? _theme.Accent : _theme.TabInactiveFore);
            graphics.FillEllipse(
                dotBrush,
                dotX - (dotSize / 2f),
                _tabTop + ((_tabHeight - dotSize) / 2f) + Px(1),
                dotSize,
                dotSize);
        }

        if (showClose)
        {
            DrawTabClose(graphics, tab, foreground, hovered && _hoverCloseGlyph);
        }
    }

    private void DrawTabClose(Graphics graphics, TitleTabItem tab, Color foreground, bool hovered)
    {
        var bounds = tab.CloseBounds;
        if (bounds.Width <= 0)
        {
            return;
        }

        if (hovered)
        {
            using var hoverBrush = new SolidBrush(_theme.CloseHoverBack);
            graphics.FillEllipse(hoverBrush, bounds);
        }

        var centerX = bounds.Left + (bounds.Width / 2f);
        var centerY = bounds.Top + (bounds.Height / 2f);
        var half = Px(3);
        var color = hovered ? Color.White : foreground;

        using var pen = new Pen(color, Math.Max(1f, Px(1.2f)));
        graphics.DrawLine(pen, centerX - half, centerY - half, centerX + half, centerY + half);
        graphics.DrawLine(pen, centerX + half, centerY - half, centerX - half, centerY + half);
    }

    private void DrawNewTab(Graphics graphics)
    {
        var bounds = _newTabBounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        if (_hoverNewTab)
        {
            var pill = Rectangle.Inflate(bounds, -Px(2), -Px(2));
            using var path = RoundedRectangle(pill, Px(8));
            using var hoverBrush = new SolidBrush(_theme.TabHoverBack);
            graphics.FillPath(hoverBrush, path);
        }

        var centerX = bounds.Left + (bounds.Width / 2f);
        var centerY = bounds.Top + (bounds.Height / 2f);
        var half = Px(5);

        if (HasMdl2)
        {
            DrawMdlGlyph(graphics, bounds, "\uE710", _hoverNewTab ? _theme.TabActiveFore : _theme.TabInactiveFore, 10f);
            return;
        }

        using var pen = new Pen(_hoverNewTab ? _theme.TabActiveFore : _theme.TabInactiveFore, Math.Max(1f, Px(1.3f)));
        graphics.DrawLine(pen, centerX - half, centerY, centerX + half, centerY);
        graphics.DrawLine(pen, centerX, centerY - half, centerX, centerY + half);
    }

    /// <summary>Pinned tab at the far right of the tab row: ▶ Device (scrcpy + terminal).</summary>
    private void DrawDeviceTab(Graphics graphics)
    {
        var bounds = _deviceBounds;
        if (bounds.Width <= 0)
        {
            return;
        }

        var shape = new Rectangle(bounds.Left + Px(2), _tabTop + Px(4), bounds.Width - Px(4), Height - _tabTop - Px(4));

        if (_deviceOpen)
        {
            using var path = TopRoundedRectangle(shape, Px(9));
            using var brush = new SolidBrush(_theme.EditorBack);
            using var outline = new Pen(_theme.Border);
            graphics.FillPath(brush, path);
            graphics.DrawPath(outline, path);

            using var seam = new SolidBrush(_theme.EditorBack);
            graphics.FillRectangle(seam, shape.Left + 1, Height - 1, shape.Width - 1, 2);

            using var accent = new Pen(_theme.Accent, Math.Max(2f, Px(2f)))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            graphics.DrawLine(accent, shape.Left + Px(12), shape.Top + 1.5f, shape.Right - Px(12), shape.Top + 1.5f);
        }
        else if (_hoverDevice)
        {
            var pill = new Rectangle(bounds.Left + Px(3), _tabTop + Px(7), bounds.Width - Px(6), _tabHeight - Px(13));
            using var path = RoundedRectangle(pill, Px(7));
            using var brush = new SolidBrush(_theme.TabHoverBack);
            graphics.FillPath(brush, path);
        }

        var foreground = _deviceOpen ? _theme.TabActiveFore : _theme.TabInactiveFore;

        // Play triangle.
        var size = Px(11);
        var triangleLeft = bounds.Left + Px(16);
        var centerY = _tabTop + (_tabHeight / 2f) + Px(1);
        var triangle = new[]
        {
            new PointF(triangleLeft, centerY - (size / 2f)),
            new PointF(triangleLeft, centerY + (size / 2f)),
            new PointF(triangleLeft + size, centerY),
        };

        var previousMode = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var fill = new SolidBrush(_deviceOpen ? _theme.Accent : foreground))
        {
            graphics.FillPolygon(fill, triangle);
        }

        graphics.SmoothingMode = previousMode;

        var textBounds = new Rectangle(triangleLeft + size + Px(8), _tabTop + Px(2), bounds.Right - triangleLeft - size - Px(16), _tabHeight - Px(2));
        TextRenderer.DrawText(
            graphics,
            Loc.T("Device"),
            Font,
            textBounds,
            foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private void DrawMenuButton(Graphics graphics)
    {
        var bounds = _menuBounds;
        if (bounds.Width <= 0)
        {
            return;
        }

        if (_hoverMenu)
        {
            var pill = new Rectangle(bounds.Left + Px(3), Px(6), bounds.Width - Px(6), _toolHeight - Px(12));
            using var path = RoundedRectangle(pill, Px(7));
            using var hoverBrush = new SolidBrush(_theme.TabHoverBack);
            graphics.FillPath(hoverBrush, path);
        }

        // Gear icon (Segoe MDL2 Assets) - opens the settings tab.
        using var gearFont = new Font("Segoe MDL2 Assets", 11.5f, FontStyle.Regular, GraphicsUnit.Point);
        TextRenderer.DrawText(
            graphics,
            "\uE713",
            gearFont,
            bounds,
            _hoverMenu ? _theme.TabActiveFore : _theme.TabInactiveFore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawCaptionButton(Graphics graphics, Rectangle bounds, CaptionGlyph glyph, bool hovered)
    {
        if (bounds.Width <= 0)
        {
            return;
        }

        var isClose = glyph == CaptionGlyph.Close;

        if (hovered)
        {
            using var hoverBrush = new SolidBrush(isClose ? _theme.CloseHoverBack : _theme.TabHoverBack);
            graphics.FillRectangle(hoverBrush, bounds);
        }

        var color = hovered && isClose ? Color.White : _theme.TabActiveFore;
        var centerX = bounds.Left + (bounds.Width / 2f);
        var centerY = bounds.Top + (bounds.Height / 2f);
        var half = Px(5);

        if (HasMdl2)
        {
            var mdl = glyph switch
            {
                CaptionGlyph.Minimize => "\uE921",
                CaptionGlyph.Maximize => "\uE922",
                CaptionGlyph.Restore => "\uE923",
                _ => "\uE8BB",
            };
            DrawMdlGlyph(graphics, bounds, mdl, color, 9f);
            return;
        }

        using var pen = new Pen(color, Math.Max(1f, Px(1f)));

        switch (glyph)
        {
            case CaptionGlyph.Minimize:
                graphics.DrawLine(pen, centerX - half, centerY, centerX + half, centerY);
                break;

            case CaptionGlyph.Maximize:
                graphics.DrawRectangle(pen, centerX - half, centerY - half, half * 2, half * 2);
                break;

            case CaptionGlyph.Restore:
                graphics.DrawRectangle(pen, centerX - half, centerY - half + Px(2), half * 2 - Px(2), half * 2 - Px(2));
                graphics.DrawRectangle(pen, centerX - half + Px(2), centerY - half, half * 2 - Px(2), half * 2 - Px(2));
                break;

            case CaptionGlyph.Close:
                graphics.DrawLine(pen, centerX - half, centerY - half, centerX + half, centerY + half);
                graphics.DrawLine(pen, centerX + half, centerY - half, centerX - half, centerY + half);
                break;
        }
    }

    // ------------------------------------------------------------------- layout

    private void LayoutTabs()
    {
        _toolHeight = Px(ToolRowHeight);
        _tabTop = _toolHeight;
        _tabHeight = Math.Max(Px(24), Height - _tabTop);

        var windowButtonWidth = Px(46);
        var chevronWidth = Px(40);

        var right = Width;
        _closeBounds = new Rectangle(right - windowButtonWidth, 0, windowButtonWidth, _toolHeight);
        _maximizeBounds = new Rectangle(_closeBounds.Left - windowButtonWidth, 0, windowButtonWidth, _toolHeight);
        _minimizeBounds = new Rectangle(_maximizeBounds.Left - windowButtonWidth, 0, windowButtonWidth, _toolHeight);
        _menuBounds = new Rectangle(_minimizeBounds.Left - chevronWidth, 0, chevronWidth, _toolHeight);

        LayoutActions();

        // ---- tab row
        _tabAreaLeft = Px(10);
        var newTabSize = Px(30);
        var deviceWidth = Px(104);
        _deviceBounds = new Rectangle(Width - Px(10) - deviceWidth, _tabTop, deviceWidth, _tabHeight);
        var rowRight = _deviceBounds.Left - Px(8);
        var available = Math.Max(0, rowRight - _tabAreaLeft - newTabSize - Px(6));

        var count = _tabs.Count;
        var idealTotal = 0;
        foreach (var tab in _tabs)
        {
            tab.TabWidth = MeasureTab(tab);
            idealTotal += tab.TabWidth;
        }

        if (count > 0 && idealTotal > available)
        {
            // Not enough room: every tab shrinks equally (like a browser) so all of
            // them stay on screen. Only below a minimum width do tabs start to scroll.
            var minWidth = Px(72);
            var each = Math.Max(minWidth, available / count);
            var remainder = each > minWidth ? available - (each * count) : 0;
            if (remainder < 0)
            {
                remainder = 0;
            }

            for (var index = 0; index < count; index++)
            {
                _tabs[index].TabWidth = each + (index < remainder ? 1 : 0);
            }
        }

        var totalWidth = 0;
        foreach (var tab in _tabs)
        {
            totalWidth += tab.TabWidth;
        }

        _contentWidth = totalWidth;
        _tabAreaRight = _tabAreaLeft + Math.Min(_contentWidth, available);

        var maxOffset = Math.Max(0, _contentWidth - (_tabAreaRight - _tabAreaLeft));
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxOffset);

        var x = _tabAreaLeft - _scrollOffset;
        foreach (var tab in _tabs)
        {
            tab.Bounds = new Rectangle(x, _tabTop, tab.TabWidth, _tabHeight);
            tab.CloseBounds = new Rectangle(
                x + tab.TabWidth - Px(9) - Px(20),
                _tabTop + ((_tabHeight - Px(20)) / 2) + Px(1),
                Px(20),
                Px(20));
            x += tab.TabWidth;
        }

        var newTabLeft = _tabAreaRight + Px(4);
        _newTabBounds = newTabLeft + newTabSize <= Width
            ? new Rectangle(newTabLeft, _tabTop + ((_tabHeight - newTabSize) / 2) + Px(1), newTabSize, newTabSize)
            : Rectangle.Empty;
    }

    private void LayoutActions()
    {
        var buttonWidth = Px(32);
        var groupGap = Px(7);
        var start = Px(52);
        var limit = _menuBounds.Left - Px(8);

        var count = ActionOrder.Length;
        var total = 0;

        // Hide the least used buttons first when the window is very narrow.
        while (count > 0)
        {
            total = MeasureActions(count, buttonWidth, groupGap);
            if (start + total <= limit)
            {
                break;
            }

            count--;
        }

        _visibleActionCount = count;
        _actionBounds.Clear();
        _actionsLeft = start;
        _actionsRight = start + total;

        var x = start;
        for (var index = 0; index < count; index++)
        {
            _actionBounds[ActionOrder[index]] = new Rectangle(x, 0, buttonWidth, _toolHeight);
            x += buttonWidth;

            if (HasGroupGapAfter(index))
            {
                x += groupGap;
            }
        }
    }

    private static int MeasureActions(int count, int buttonWidth, int groupGap)
    {
        var total = 0;

        for (var index = 0; index < count; index++)
        {
            total += buttonWidth;

            if (HasGroupGapAfter(index))
            {
                total += groupGap;
            }
        }

        return total;
    }

    private static bool HasGroupGapAfter(int index) => index == 1 || index == 3;

    private TitleBarAction? HitTestAction(Point point)
    {
        if (_visibleActionCount == 0 ||
            point.Y < 0 ||
            point.Y >= _toolHeight ||
            point.X < _actionsLeft)
        {
            return null;
        }

        for (var index = 0; index < _visibleActionCount; index++)
        {
            var action = ActionOrder[index];
            if (_actionBounds.TryGetValue(action, out var bounds) && bounds.Contains(point))
            {
                return action;
            }
        }

        return null;
    }

    private void DrawActions(Graphics graphics)
    {
        if (_visibleActionCount == 0)
        {
            return;
        }

        for (var index = 0; index < _visibleActionCount; index++)
        {
            var action = ActionOrder[index];
            if (!_actionBounds.TryGetValue(action, out var bounds))
            {
                continue;
            }

            var isHovered = _hoverAction == action;
            var isPressed = _pressedAction == action;

            if (isHovered || isPressed)
            {
                var inner = new Rectangle(
                    bounds.Left + Px(2),
                    Px(6),
                    bounds.Width - Px(4),
                    Math.Max(1, _toolHeight - Px(12)));

                using var path = RoundedRectangle(inner, Px(6));
                using var hoverBrush = new SolidBrush(_theme.TabHoverBack);
                graphics.FillPath(hoverBrush, path);
            }

            var color = (isHovered || isPressed) ? _theme.TabActiveFore : _theme.TabInactiveFore;
            DrawActionGlyph(graphics, bounds, action, color);

            if (index + 1 < _visibleActionCount && HasGroupGapAfter(index))
            {
                using var separator = new Pen(_theme.Border);
                var x = bounds.Right + Px(3);
                graphics.DrawLine(separator, x, Px(12), x, _toolHeight - Px(12));
            }
        }
    }

    private void DrawActionGlyph(Graphics graphics, Rectangle bounds, TitleBarAction action, Color color)
    {
        var centerX = bounds.Left + (bounds.Width / 2f);
        var centerY = bounds.Top + (bounds.Height / 2f);

        switch (action)
        {
            case TitleBarAction.Undo:
                if (HasMdl2) { DrawMdlGlyph(graphics, bounds, "", color); } else { DrawUndoGlyph(graphics, centerX, centerY, color, mirrored: false); }
                break;

            case TitleBarAction.Redo:
                if (HasMdl2) { DrawMdlGlyph(graphics, bounds, "", color); } else { DrawUndoGlyph(graphics, centerX, centerY, color, mirrored: true); }
                break;

            case TitleBarAction.Save:
                if (HasMdl2) { DrawMdlGlyph(graphics, bounds, "\uE74E", color); } else { DrawSaveGlyph(graphics, centerX, centerY, color); }
                break;

            case TitleBarAction.Find:
                if (HasMdl2) { DrawMdlGlyph(graphics, bounds, "\uE721", color); } else { DrawLensGlyph(graphics, centerX, centerY, color, sign: 0); }
                break;

            case TitleBarAction.ZoomOut:
                if (HasMdl2) { DrawMdlGlyph(graphics, bounds, "\uE8E7", color); } else { DrawLensGlyph(graphics, centerX, centerY, color, sign: -1); }
                break;

            case TitleBarAction.ZoomIn:
                if (HasMdl2) { DrawMdlGlyph(graphics, bounds, "\uE8E8", color); } else { DrawLensGlyph(graphics, centerX, centerY, color, sign: 1); }
                break;

            case TitleBarAction.Theme:
                if (HasMdl2) { DrawMdlGlyph(graphics, bounds, _theme.IsDark ? "\uE706" : "\uE708", color); } else { DrawThemeGlyph(graphics, centerX, centerY, color); }
                break;

            case TitleBarAction.Direction:
                DrawDirectionGlyph(graphics, bounds, color);
                break;

            case TitleBarAction.Font:
                if (HasMdl2) { DrawMdlGlyph(graphics, bounds, "\uE8D2", color); } else { DrawFontGlyph(graphics, bounds, color); }
                break;
        }
    }

    /// <summary>0 = automatic, 1 = left-to-right, 2 = right-to-left (drives the direction glyph).</summary>
    public int DirectionState
    {
        get => _directionState;
        set
        {
            if (_directionState != value)
            {
                _directionState = value;
                Invalidate();
            }
        }
    }

    private int _directionState;

    private void DrawDirectionGlyph(Graphics graphics, Rectangle bounds, Color color)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var cx = bounds.Left + (bounds.Width / 2f);
        var cy = bounds.Top + (bounds.Height / 2f);
        var half = Px(8);
        var thickness = Math.Max(1.5f, Px(1.5f));
        using var pen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        // Two text lines aligned to the start side, and an arrow underneath showing the direction.
        var rtl = _directionState == 2;
        var auto = _directionState == 0;
        var left = cx - half;
        var right = cx + half;
        var lineGap = Px(4);
        var top = cy - Px(7);
        for (var i = 0; i < 2; i++)
        {
            var y = top + (i * lineGap);
            var length = i == 0 ? half * 2f : half * 1.3f;
            if (auto)
            {
                graphics.DrawLine(pen, cx - (length / 2f), y, cx + (length / 2f), y);
            }
            else if (rtl)
            {
                graphics.DrawLine(pen, right - length, y, right, y);
            }
            else
            {
                graphics.DrawLine(pen, left, y, left + length, y);
            }
        }

        var ay = cy + Px(5);
        var head = Px(3);
        graphics.DrawLine(pen, left, ay, right, ay);
        if (auto || !rtl)
        {
            graphics.DrawLine(pen, right, ay, right - head, ay - head);
            graphics.DrawLine(pen, right, ay, right - head, ay + head);
        }

        if (auto || rtl)
        {
            graphics.DrawLine(pen, left, ay, left + head, ay - head);
            graphics.DrawLine(pen, left, ay, left + head, ay + head);
        }
    }

    private void DrawFontGlyph(Graphics graphics, Rectangle bounds, Color color)
    {
        using var font = new Font("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);

        TextRenderer.DrawText(
            graphics,
            "Aa",
            font,
            bounds,
            color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private static readonly bool HasMdl2 = IsFontInstalled("Segoe MDL2 Assets");

    private static bool IsFontInstalled(string name)
    {
        foreach (var family in FontFamily.Families)
        {
            if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void DrawMdlGlyph(Graphics graphics, Rectangle bounds, string glyph, Color color, float size = 12f)
    {
        using var font = new Font("Segoe MDL2 Assets", size, FontStyle.Regular, GraphicsUnit.Point);
        TextRenderer.DrawText(
            graphics,
            glyph,
            font,
            bounds,
            color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawUndoGlyph(Graphics graphics, float centerX, float centerY, Color color, bool mirrored)
    {
        var state = graphics.Save();

        if (mirrored)
        {
            graphics.TranslateTransform(centerX * 2f, 0f);
            graphics.ScaleTransform(-1f, 1f);
        }

        var half = Px(7);
        var radius = new RectangleF(centerX - half, centerY - half + Px(1), half * 2, half * 2);

        using var pen = new Pen(color, Math.Max(1f, Px(1.6f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        // Arc across the top, with the arrow head on the left end pointing down.
        graphics.DrawArc(pen, radius, 188f, 164f);

        var arrow = Px(6);
        var tipX = centerX - half + Px(1);
        var tipY = centerY - Px(0.5f);
        graphics.DrawLine(pen, tipX, tipY, tipX + arrow, tipY - Px(1));
        graphics.DrawLine(pen, tipX, tipY, tipX + Px(2), tipY + arrow);

        graphics.Restore(state);
    }

    private void DrawSaveGlyph(Graphics graphics, float centerX, float centerY, Color color)
    {
        var half = Px(7);
        var pen = new Pen(color, Math.Max(1f, Px(1.5f)));

        using (pen)
        using (var brush = new SolidBrush(color))
        {
            graphics.DrawRectangle(
                pen,
                centerX - half,
                centerY - half,
                half * 2,
                half * 2);

            // Shutter at the top and the label at the bottom, like a floppy disk.
            graphics.FillRectangle(brush, centerX - Px(3.5f), centerY - half + Px(1), Px(7), Px(4.5f));
            graphics.DrawRectangle(
                pen,
                centerX - Px(4),
                centerY + Px(0.5f),
                Px(8),
                Px(6.5f));
        }
    }

    private void DrawLensGlyph(Graphics graphics, float centerX, float centerY, Color color, int sign)
    {
        var radius = Px(5.2f);
        var lensX = centerX - Px(1.6f);
        var lensY = centerY - Px(1.6f);
        var pen = new Pen(color, Math.Max(1f, Px(1.4f)));

        using (pen)
        {
            graphics.DrawEllipse(pen, lensX - radius, lensY - radius, radius * 2, radius * 2);

            var handle = Px(3.6f);
            graphics.DrawLine(
                pen,
                lensX + radius * 0.72f,
                lensY + radius * 0.72f,
                lensX + radius + handle,
                lensY + radius + handle);

            if (sign != 0)
            {
                var arm = Px(2.6f);
                graphics.DrawLine(pen, lensX - arm, lensY, lensX + arm, lensY);

                if (sign > 0)
                {
                    graphics.DrawLine(pen, lensX, lensY - arm, lensX, lensY + arm);
                }
            }
        }
    }

    private void DrawThemeGlyph(Graphics graphics, float centerX, float centerY, Color color)
    {
        var radius = Px(6.2f);
        var pen = new Pen(color, Math.Max(1f, Px(1.4f)));

        using (pen)
        using (var brush = new SolidBrush(color))
        {
            graphics.DrawEllipse(pen, centerX - radius, centerY - radius, radius * 2, radius * 2);
            graphics.FillPie(
                brush,
                centerX - radius,
                centerY - radius,
                radius * 2,
                radius * 2,
                -90f,
                180f);
        }
    }

    private static GraphicsPath TopRoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, radius * 2);

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddLine(bounds.Right, bounds.Bottom, bounds.Left, bounds.Bottom);
        path.CloseFigure();

        return path;
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, radius * 2);

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    private void EnsureVisible(int index)
    {
        if (index < 0 || index >= _tabs.Count)
        {
            return;
        }

        LayoutTabs();

        var bounds = _tabs[index].Bounds;

        if (bounds.Right > _tabAreaRight)
        {
            _scrollOffset = Math.Max(0, _scrollOffset + (bounds.Right - _tabAreaRight));
        }
        else if (bounds.Left < _tabAreaLeft)
        {
            _scrollOffset = Math.Max(0, _scrollOffset + (bounds.Left - _tabAreaLeft));
        }

        var maxOffset = Math.Max(0, _contentWidth - (_tabAreaRight - _tabAreaLeft));
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxOffset);

        LayoutTabs();
    }

    private int MeasureTab(TitleTabItem tab)
    {
        var measured = TextRenderer.MeasureText(
            tab.Title,
            Font,
            new Size(int.MaxValue, Height),
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        return Math.Clamp(measured.Width + Px(74), Px(130), Px(240));
    }

    private int HitTestTab(Point point)
    {
        if (point.Y < _tabTop || point.Y >= Height || point.X < _tabAreaLeft || point.X >= _tabAreaRight)
        {
            return -1;
        }

        for (var index = 0; index < _tabs.Count; index++)
        {
            var bounds = _tabs[index].Bounds;
            if (point.X >= bounds.Left && point.X < bounds.Right)
            {
                return index;
            }
        }

        return -1;
    }

    private void UpdateToolTip(int index)
    {
        var text = _hoverAction is { } action
            ? ActionToolTip(action)
            : _hoverNewTab
                ? Loc.T("New tab  (Ctrl+N)")
                : _hoverMenu
                    ? Loc.T("Settings")
                    : _hoverDevice
                        ? Loc.T("Device")
                : index >= 0 && index < _tabs.Count
                    ? _tabs[index].ToolTip ?? _tabs[index].Title
                    : string.Empty;

        if (string.Equals(text, _toolTipText, StringComparison.Ordinal))
        {
            return;
        }

        _toolTipText = text;
        _toolTip.SetToolTip(this, text);
    }

    private static string ActionToolTip(TitleBarAction action) => action switch
    {
        TitleBarAction.Undo => Loc.T("Undo  (Ctrl+Z)"),
        TitleBarAction.Redo => Loc.T("Redo  (Ctrl+Y)"),
        TitleBarAction.Save => Loc.T("Save  (Ctrl+S)"),
        TitleBarAction.Find => Loc.T("Find  (Ctrl+F)"),
        TitleBarAction.ZoomOut => Loc.T("Smaller text  (Ctrl+-)"),
        TitleBarAction.ZoomIn => Loc.T("Larger text  (Ctrl++)"),
        TitleBarAction.Theme => Loc.T("Light / dark theme  (Ctrl+D)"),
        TitleBarAction.Font => Loc.T("Editor font  (Ctrl++ / Ctrl+- / Choose font...)"),
        TitleBarAction.Direction => Loc.T("Text direction: Auto / Left-to-right / Right-to-left  (Ctrl+R)"),
        _ => string.Empty,
    };

    private int Px(float value) => (int)Math.Round(value * DeviceDpi / 96.0);
}

public sealed class TabRenamedEventArgs : EventArgs
{
    public TabRenamedEventArgs(int index, string name)
    {
        Index = index;
        Name = name;
    }

    public int Index { get; }
    public string Name { get; }
}

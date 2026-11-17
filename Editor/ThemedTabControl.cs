using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>
/// A TabControl with owner-painted chrome.
///
/// The native control still performs all of the real TabPage hosting, keyboard
/// navigation and overflow handling. After the native paint we repaint the tab
/// strip, close glyphs and the new-tab button, which is what allows the tab strip
/// itself to follow the dark/light theme (the native strip is always light).
/// </summary>
public sealed class ThemedTabControl : TabControl
{
    private const int WmPaint = 0x000F;
    private const int CloseButtonSize = 18;

    private AppTheme _theme = AppTheme.Light;
    private int _hoverCloseIndex = -1;
    private bool _hoverNewTab;
    private bool _showTabStrip = true;

    public ThemedTabControl()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.ResizeRedraw,
            true);

        SizeMode = TabSizeMode.Fixed;
        DrawMode = TabDrawMode.Normal;
        ShowToolTips = true;
        Padding = new Point(0, 0);

        UpdateTabItemSize();
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

    /// <summary>
    /// When false the native tab row is reduced to a single pixel and only the
    /// page host remains. The window title bar then draws the document tabs.
    /// </summary>
    public bool ShowTabStrip
    {
        get => _showTabStrip;
        set
        {
            if (_showTabStrip == value)
            {
                return;
            }

            _showTabStrip = value;
            Appearance = value ? TabAppearance.Normal : TabAppearance.FlatButtons;
            ShowToolTips = value;
            UpdateTabItemSize();
            Invalidate();
        }
    }

    /// <summary>Raised when the small × on a tab is clicked, or a tab is middle-clicked.</summary>
    public event EventHandler<TabPage>? CloseTabRequested;

    /// <summary>Raised when the + area next to the tabs is clicked.</summary>
    public event EventHandler? NewTabRequested;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg == WmPaint)
        {
            PaintChrome();
        }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateTabItemSize();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateTabItemSize();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_showTabStrip)
        {
            return;
        }

        var closeIndex = HitTestClose(e.Location);
        var overNewTab = GetNewTabButtonRect().Contains(e.Location);

        if (closeIndex != _hoverCloseIndex || overNewTab != _hoverNewTab)
        {
            _hoverCloseIndex = closeIndex;
            _hoverNewTab = overNewTab;
            Invalidate(new Rectangle(0, 0, Width, Math.Max(1, DisplayRectangle.Top)));
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (!_showTabStrip)
        {
            return;
        }

        if (_hoverCloseIndex != -1 || _hoverNewTab)
        {
            _hoverCloseIndex = -1;
            _hoverNewTab = false;
            Invalidate(new Rectangle(0, 0, Width, Math.Max(1, DisplayRectangle.Top)));
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_showTabStrip)
        {
            base.OnMouseUp(e);
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            if (GetNewTabButtonRect().Contains(e.Location))
            {
                NewTabRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            var closeIndex = HitTestClose(e.Location);
            if (closeIndex >= 0)
            {
                CloseTabRequested?.Invoke(this, TabPages[closeIndex]);
                return;
            }
        }
        else if (e.Button == MouseButtons.Middle)
        {
            var tabIndex = HitTestTab(e.Location);
            if (tabIndex >= 0)
            {
                CloseTabRequested?.Invoke(this, TabPages[tabIndex]);
                return;
            }
        }

        base.OnMouseUp(e);
    }

    private void PaintChrome()
    {
        if (!IsHandleCreated || IsDisposed || TabCount == 0)
        {
            return;
        }

        try
        {
            using var graphics = Graphics.FromHwnd(Handle);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var display = DisplayRectangle;

            // Paint the tab strip and the thin frame around the page area. The native
            // control paints these with light system colors even in a dark theme.
            // In headerless mode the band is filled with the editor color so that the
            // hidden native strip is indistinguishable from the page itself.
            var chromeColor = _showTabStrip ? _theme.TabStripBack : _theme.EditorBack;
            using (var stripBrush = new SolidBrush(chromeColor))
            {
                graphics.FillRectangle(stripBrush, 0, 0, Width, display.Top);

                if (display.Left > 0)
                {
                    graphics.FillRectangle(stripBrush, 0, display.Top, display.Left, Height - display.Top);
                }

                if (display.Right < Width)
                {
                    graphics.FillRectangle(stripBrush, display.Right, display.Top, Width - display.Right, Height - display.Top);
                }

                if (display.Bottom < Height)
                {
                    graphics.FillRectangle(stripBrush, 0, display.Bottom, Width, Height - display.Bottom);
                }
            }

            if (!_showTabStrip)
            {
                // The native control still draws a one pixel frame around the page
                // in light system colors. A slightly wider editor colored frame
                // covers it; the inner half is hidden by the docked editor.
                using var frame = new Pen(_theme.EditorBack, 3f);
                graphics.DrawRectangle(
                    frame,
                    display.Left - 1,
                    display.Top - 1,
                    display.Width + 1,
                    display.Height + 1);

                return;
            }

            var stripClip = new Rectangle(0, 0, Width, Math.Max(0, display.Top));
            graphics.SetClip(stripClip);

            for (var index = 0; index < TabCount; index++)
            {
                DrawTab(graphics, index);
            }

            DrawNewTab(graphics);
            graphics.ResetClip();
        }
        catch (Exception)
        {
            // A paint can race with handle destruction during shutdown.
        }
    }

    private void DrawTab(Graphics graphics, int index)
    {
        var page = TabPages[index];
        var bounds = GetTabRect(index);
        var selected = SelectedIndex == index;

        using (var background = new SolidBrush(selected ? _theme.TabActiveBack : _theme.TabInactiveBack))
        {
            graphics.FillRectangle(background, bounds);
        }

        if (selected)
        {
            using var accentPen = new Pen(_theme.Accent, 2f);
            graphics.DrawLine(
                accentPen,
                bounds.Left + 6,
                bounds.Bottom - 2,
                bounds.Right - 6,
                bounds.Bottom - 2);
        }

        var foreground = selected ? _theme.TabActiveFore : _theme.TabInactiveFore;

        var textBounds = new Rectangle(
            bounds.Left + 10,
            bounds.Top,
            Math.Max(0, bounds.Width - 38),
            bounds.Height);

        var flags = TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix;

        if (page.RightToLeft == RightToLeft.Yes)
        {
            flags |= TextFormatFlags.RightToLeft;
        }

        TextRenderer.DrawText(graphics, page.Text, Font, textBounds, foreground, flags);
        DrawCloseGlyph(graphics, bounds, foreground, index == _hoverCloseIndex);
    }

    private void DrawCloseGlyph(Graphics graphics, Rectangle tabBounds, Color foreground, bool hovered)
    {
        var bounds = CloseButtonRect(tabBounds);

        if (hovered)
        {
            using var hoverBrush = new SolidBrush(_theme.MenuHover);
            graphics.FillRectangle(hoverBrush, bounds);
        }

        using var pen = new Pen(hovered ? _theme.Accent : foreground, 1.6f);
        const int pad = 5;
        graphics.DrawLine(pen, bounds.Left + pad, bounds.Top + pad, bounds.Right - pad, bounds.Bottom - pad);
        graphics.DrawLine(pen, bounds.Right - pad, bounds.Top + pad, bounds.Left + pad, bounds.Bottom - pad);
    }

    private void DrawNewTab(Graphics graphics)
    {
        var bounds = GetNewTabButtonRect();
        if (bounds.IsEmpty)
        {
            return;
        }

        if (_hoverNewTab)
        {
            using var hoverBrush = new SolidBrush(_theme.MenuHover);
            graphics.FillRectangle(hoverBrush, bounds);
        }

        var centerX = bounds.Left + (bounds.Width / 2f);
        var centerY = bounds.Top + (bounds.Height / 2f);
        var half = 5f;

        using var pen = new Pen(_theme.TabInactiveFore, 1.8f);
        graphics.DrawLine(pen, centerX - half, centerY, centerX + half, centerY);
        graphics.DrawLine(pen, centerX, centerY - half, centerX, centerY + half);
    }

    private Rectangle GetNewTabButtonRect()
    {
        if (TabCount == 0)
        {
            return new Rectangle(6, 4, 22, Math.Max(20, ItemSize.Height - 8));
        }

        var lastTab = GetTabRect(TabCount - 1);
        if (lastTab.Right <= 0 || lastTab.Left >= Width)
        {
            return Rectangle.Empty;
        }

        const int size = 22;
        var x = lastTab.Right + 8;
        if (x + size > Width - 4)
        {
            return Rectangle.Empty;
        }

        var y = lastTab.Top + ((lastTab.Height - size) / 2);
        return new Rectangle(x, y, size, size);
    }

    private static Rectangle CloseButtonRect(Rectangle tabBounds)
    {
        return new Rectangle(
            tabBounds.Right - CloseButtonSize - 6,
            tabBounds.Top + ((tabBounds.Height - CloseButtonSize) / 2),
            CloseButtonSize,
            CloseButtonSize);
    }

    private int HitTestClose(Point point)
    {
        for (var index = 0; index < TabCount; index++)
        {
            var bounds = GetTabRect(index);
            if (bounds.Contains(point) && CloseButtonRect(bounds).Contains(point))
            {
                return index;
            }
        }

        return -1;
    }

    private int HitTestTab(Point point)
    {
        for (var index = 0; index < TabCount; index++)
        {
            if (GetTabRect(index).Contains(point))
            {
                return index;
            }
        }

        return -1;
    }

    private void UpdateTabItemSize()
    {
        if (IsDisposed)
        {
            return;
        }

        if (!_showTabStrip)
        {
            var hiddenSize = new Size(1, 1);
            if (ItemSize != hiddenSize)
            {
                ItemSize = hiddenSize;
                Invalidate();
            }

            return;
        }

        var measured = TextRenderer.MeasureText("WideFileName.cs", Font);
        var width = Math.Clamp(measured.Width + 56, 140, 280);
        var height = Math.Max(26, Font.Height + 14);

        var newSize = new Size(width, height);
        if (ItemSize != newSize)
        {
            ItemSize = newSize;
            Invalidate();
        }
    }
}

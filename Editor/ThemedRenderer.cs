using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

internal sealed class ThemedColorTable : ProfessionalColorTable
{
    private readonly AppTheme _theme;

    public ThemedColorTable(AppTheme theme)
    {
        _theme = theme;
    }

    public override Color ToolStripDropDownBackground => _theme.MenuBack;
    public override Color ImageMarginGradientBegin => _theme.MenuBack;
    public override Color ImageMarginGradientMiddle => _theme.MenuBack;
    public override Color ImageMarginGradientEnd => _theme.MenuBack;

    public override Color MenuBorder => _theme.Border;
    public override Color MenuItemBorder => _theme.Accent;
    public override Color MenuItemSelected => _theme.MenuHover;
    public override Color MenuItemSelectedGradientBegin => _theme.MenuHover;
    public override Color MenuItemSelectedGradientEnd => _theme.MenuHover;
    public override Color MenuItemPressedGradientBegin => _theme.MenuHover;
    public override Color MenuItemPressedGradientMiddle => _theme.MenuHover;
    public override Color MenuItemPressedGradientEnd => _theme.MenuHover;

    public override Color ToolStripBorder => _theme.Border;
    public override Color ToolStripGradientBegin => _theme.ToolBack;
    public override Color ToolStripGradientMiddle => _theme.ToolBack;
    public override Color ToolStripGradientEnd => _theme.ToolBack;

    public override Color StatusStripGradientBegin => _theme.StatusBack;
    public override Color StatusStripGradientEnd => _theme.StatusBack;

    public override Color MenuStripGradientBegin => _theme.MenuBack;
    public override Color MenuStripGradientEnd => _theme.MenuBack;

    public override Color ButtonSelectedHighlight => _theme.MenuHover;
    public override Color ButtonSelectedGradientBegin => _theme.MenuHover;
    public override Color ButtonSelectedGradientMiddle => _theme.MenuHover;
    public override Color ButtonSelectedGradientEnd => _theme.MenuHover;
    public override Color ButtonPressedGradientBegin => _theme.MenuHover;
    public override Color ButtonPressedGradientMiddle => _theme.MenuHover;
    public override Color ButtonPressedGradientEnd => _theme.MenuHover;
    public override Color ButtonCheckedGradientBegin => _theme.MenuHover;
    public override Color ButtonCheckedGradientMiddle => _theme.MenuHover;
    public override Color ButtonCheckedGradientEnd => _theme.MenuHover;

    public override Color SeparatorDark => _theme.Border;
    public override Color SeparatorLight => _theme.Border;

    public override Color CheckBackground => _theme.MenuHover;
    public override Color CheckSelectedBackground => _theme.Accent;
    public override Color CheckPressedBackground => _theme.Accent;

    public override Color GripDark => _theme.Border;
    public override Color GripLight => _theme.MenuBack;

    public override Color OverflowButtonGradientBegin => _theme.ToolBack;
    public override Color OverflowButtonGradientMiddle => _theme.ToolBack;
    public override Color OverflowButtonGradientEnd => _theme.ToolBack;
}

/// <summary>Flat, theme-aware renderer for MenuStrip, ToolStrip, StatusStrip and drop-downs.</summary>
internal sealed class ThemedRenderer : ToolStripProfessionalRenderer
{
    private readonly AppTheme _theme;

    public ThemedRenderer(AppTheme theme)
        : base(new ThemedColorTable(theme))
    {
        _theme = theme;
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        var color = e.ToolStrip switch
        {
            StatusStrip => _theme.StatusBack,
            MenuStrip => _theme.MenuBack,
            ToolStripDropDown => _theme.MenuBack,
            _ => _theme.ToolBack,
        };

        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.ToolStrip.Size));
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(_theme.Border);

        if (e.ToolStrip is ToolStripDropDown)
        {
            e.Graphics.DrawRectangle(
                pen,
                0,
                0,
                e.ToolStrip.Width - 1,
                e.ToolStrip.Height - 1);
        }
        else if (e.ToolStrip is MenuStrip or ToolStrip)
        {
            e.Graphics.DrawLine(
                pen,
                0,
                e.ToolStrip.Height - 1,
                e.ToolStrip.Width,
                e.ToolStrip.Height - 1);
        }
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_theme.MenuBack);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            return;
        }

        var bounds = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
        using var brush = new SolidBrush(_theme.MenuHover);
        e.Graphics.FillRectangle(brush, bounds);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is ToolStripMenuItem || e.Item.Owner is MenuStrip || e.Item.Owner is ToolStripDropDown)
        {
            e.TextColor = e.Item.Enabled ? _theme.MenuFore : _theme.DisabledFore;
        }
        else
        {
            e.TextColor = e.Item.Enabled ? e.Item.ForeColor : _theme.DisabledFore;
        }

        base.OnRenderItemText(e);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected && !e.Item.Pressed && e.Item is not ToolStripButton { Checked: true })
        {
            return;
        }

        using var brush = new SolidBrush(_theme.MenuHover);
        e.Graphics.FillRectangle(brush, new Rectangle(0, 0, e.Item.Width, e.Item.Height));
    }

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected && !e.Item.Pressed)
        {
            return;
        }

        using var brush = new SolidBrush(_theme.MenuHover);
        e.Graphics.FillRectangle(brush, new Rectangle(0, 0, e.Item.Width, e.Item.Height));
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_theme.Border);
        if (e.Vertical)
        {
            var x = e.Item.Width / 2;
            e.Graphics.DrawLine(pen, x, 4, x, e.Item.Height - 4);
        }
        else
        {
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
        }
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _theme.MenuFore;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var bounds = e.ImageRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var centerX = bounds.Left + (bounds.Width / 2f);
        var centerY = bounds.Top + (bounds.Height / 2f);
        var size = Math.Max(3.5f, Math.Min(bounds.Width, bounds.Height) / 3.2f);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_theme.Accent, Math.Max(1.6f, e.Graphics.DpiX / 96f * 1.6f));
        e.Graphics.DrawLines(
            pen,
            new[]
            {
                new PointF(centerX - size, centerY),
                new PointF(centerX - (size / 3f), centerY + size),
                new PointF(centerX + size, centerY - size),
            });
    }
}

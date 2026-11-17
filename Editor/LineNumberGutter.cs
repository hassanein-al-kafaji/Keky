using System;
using System.Drawing;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>
/// Line-number strip that sits to the left of a RichTextBox. It draws only the
/// lines currently visible, numbers real (logical) lines even with word wrap on,
/// and highlights the line that holds the caret.
/// </summary>
internal sealed class LineNumberGutter : Control
{
    private readonly RichTextBox _editor;
    private AppTheme _theme;
    private string _text = string.Empty;
    private bool _textDirty = true;
    private int _digits = 2;

    public LineNumberGutter(RichTextBox editor, AppTheme theme)
    {
        _editor = editor;
        _theme = theme;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Dock = DockStyle.Left;

        _editor.VScroll += Editor_Changed;
        _editor.SelectionChanged += Editor_Changed;
        _editor.FontChanged += Editor_FontChanged;
        _editor.TextChanged += Editor_TextChanged;
        _editor.Resize += Editor_Changed;

        UpdateWidth();
    }

    /// <summary>Right-to-left text sits on the right, so the numbers go there too.</summary>
    public void SetSide(bool right)
    {
        var dock = right ? DockStyle.Right : DockStyle.Left;
        if (Dock != dock)
        {
            Dock = dock;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor.VScroll -= Editor_Changed;
            _editor.SelectionChanged -= Editor_Changed;
            _editor.FontChanged -= Editor_FontChanged;
            _editor.TextChanged -= Editor_TextChanged;
            _editor.Resize -= Editor_Changed;
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;

        using (var back = new SolidBrush(_theme.EditorBack))
        {
            graphics.FillRectangle(back, ClientRectangle);
        }

        using (var border = new Pen(_theme.Border))
        {
            var gapEdge = Px(14);
            var x = Dock == DockStyle.Right ? gapEdge : Width - gapEdge - 1;
            graphics.DrawLine(border, x, 0, x, Height);
        }

        if (IsGone || !_editor.IsHandleCreated)
        {
            return;
        }

        var text = CurrentText();
        var first = NativeMethods.GetFirstVisibleLine(_editor.Handle);
        var firstIndex = _editor.GetFirstCharIndexFromLine(first);
        if (firstIndex < 0)
        {
            firstIndex = 0;
        }

        var logical = CountNewLines(text, firstIndex) + 1;
        var caretLine = CountNewLines(text, _editor.SelectionStart) + 1;
        var lineHeight = _editor.Font.Height;

        using var font = NumberFont();
        var numberHeight = font.Height;
        var rightEdge = Dock == DockStyle.Right ? Width - Px(7) : Width - Px(14) - Px(7);
        var isFirst = true;

        for (var visual = first; ; visual++)
        {
            var index = _editor.GetFirstCharIndexFromLine(visual);
            if (index < 0)
            {
                break;
            }

            var y = _editor.GetPositionFromCharIndex(index).Y;
            if (y >= Height)
            {
                break;
            }

            var startsLine = index == 0 || (index <= text.Length && text[index - 1] == '\n');
            if (startsLine)
            {
                if (!isFirst)
                {
                    logical++;
                }

                var color = logical == caretLine ? _theme.TabActiveFore : _theme.DisabledFore;
                var bounds = new Rectangle(0, y + ((lineHeight - numberHeight) / 2), rightEdge, numberHeight);
                TextRenderer.DrawText(
                    graphics,
                    logical.ToString(),
                    font,
                    bounds,
                    color,
                    TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }

            isFirst = false;
        }
    }

    // About half the editor size, capped, so the gutter stays slim even when zoomed.
    private Font NumberFont() => new(_editor.Font.FontFamily, Math.Clamp(_editor.Font.SizeInPoints * 0.5f, 7f, 12f), GraphicsUnit.Point);

    private bool IsGone => IsDisposed || Disposing || _editor.IsDisposed || _editor.Disposing;

    private void Editor_Changed(object? sender, EventArgs e)
    {
        if (!IsGone)
        {
            Invalidate();
        }
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (IsGone)
        {
            return;
        }

        _textDirty = true;
        UpdateWidth();
        Invalidate();
    }

    private void Editor_FontChanged(object? sender, EventArgs e)
    {
        if (IsGone)
        {
            return;
        }

        UpdateWidth();
        Invalidate();
    }

    private string CurrentText()
    {
        // TextChanged is not raised for text assigned before the handle exists, so
        // a length mismatch also invalidates the cache.
        if (_textDirty || (_editor.IsHandleCreated && _editor.TextLength != _text.Length))
        {
            _text = _editor.Text;
            _textDirty = false;
        }

        return _text;
    }

    private void UpdateWidth()
    {
        var lines = CountNewLines(CurrentText(), int.MaxValue) + 1;
        _digits = Math.Max(2, lines.ToString().Length);

        using var font = NumberFont();
        var digitWidth = TextRenderer.MeasureText(
            "0000000000",
            font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding).Width / 10;

        var width = (digitWidth * _digits) + Px(14) + Px(14);
        if (Width != width)
        {
            Width = width;
        }
    }

    private static int CountNewLines(string text, int length)
        => text.AsSpan(0, Math.Clamp(length, 0, text.Length)).Count('\n');

    private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96.0);
}

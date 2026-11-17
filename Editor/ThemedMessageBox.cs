using KekyNotepad.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>
/// Message box drawn with the application theme: round icon, a bold first line (the question) with the
/// details under it, and a footer strip with the buttons. Mirrors itself for right-to-left languages.
/// </summary>
internal sealed class ThemedMessageBox : Form
{
    private readonly AppTheme _theme;
    private readonly MessageBoxIcon _icon;
    private readonly bool _rtl;
    private readonly Label _title;
    private readonly Label? _details;
    private readonly Panel _body;
    private readonly Panel _footer;
    private readonly List<Button> _buttonList = new();
    private DialogResult _escapeResult = DialogResult.Cancel;

    private ThemedMessageBox(AppTheme theme, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, string[]? labels, bool noIsDestructive)
    {
        _theme = theme;
        _icon = icon;
        _rtl = Loc.IsArabic;

        Text = caption;
        Font = new Font(_rtl ? "Segoe UI" : "Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = theme.EditorBack;
        ForeColor = theme.EditorFore;
        KeyPreview = true;
        RightToLeft = _rtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = false;

        var lines = text.Replace("\r", string.Empty).Split('\n', 2);
        var multiLine = lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]);
        var textWidth = Px(icon == MessageBoxIcon.None ? 460 : 420);

        _title = new Label
        {
            Text = multiLine ? lines[0] : text,
            AutoSize = true,
            MaximumSize = new Size(textWidth, 0),
            ForeColor = theme.EditorFore,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", multiLine ? 12.5f : 11f, FontStyle.Regular, GraphicsUnit.Point),
        };

        if (multiLine)
        {
            _details = new Label
            {
                Text = lines[1].TrimStart('\n'),
                AutoSize = true,
                MaximumSize = new Size(textWidth, 0),
                ForeColor = theme.DisabledFore,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
            };
        }

        _body = new Panel { Dock = DockStyle.Fill, BackColor = theme.EditorBack };
        _body.Paint += Body_Paint;
        _body.Controls.Add(_title);
        if (_details is not null)
        {
            _body.Controls.Add(_details);
        }

        _footer = new Panel { Dock = DockStyle.Bottom, Height = Px(68), BackColor = theme.MenuBack };
        _footer.Resize += (_, _) => PlaceButtons();
        _footer.Paint += (_, e) =>
        {
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
        };

        Controls.Add(_body);
        Controls.Add(_footer);

        // Buttons are listed in reading order: first = the main action.
        switch (buttons)
        {
            case MessageBoxButtons.OK:
                AddButton(Label(labels, 0, "OK"), DialogResult.OK, true, false);
                _escapeResult = DialogResult.OK;
                break;
            case MessageBoxButtons.YesNo:
                AddButton(Label(labels, 0, "Yes"), DialogResult.Yes, true, false);
                AddButton(Label(labels, 1, "No"), DialogResult.No, false, noIsDestructive);
                _escapeResult = DialogResult.No;
                break;
            case MessageBoxButtons.OKCancel:
                AddButton(Label(labels, 0, "OK"), DialogResult.OK, true, false);
                AddButton(Label(labels, 1, "Cancel"), DialogResult.Cancel, false, false);
                break;
            default: // YesNoCancel
                AddButton(Label(labels, 0, "Yes"), DialogResult.Yes, true, false);
                AddButton(Label(labels, 1, "No"), DialogResult.No, false, noIsDestructive);
                AddButton(Label(labels, 2, "Cancel"), DialogResult.Cancel, false, false);
                break;
        }

        var iconSpace = icon == MessageBoxIcon.None ? 0 : Px(44 + 18);
        var contentWidth = Math.Max(_title.PreferredSize.Width, _details?.PreferredSize.Width ?? 0);
        var buttonsWidth = _buttonList.Count * Px(112) + Px(40);
        var clientWidth = Math.Max(Math.Max(Px(400), buttonsWidth), Px(28) * 2 + iconSpace + contentWidth);
        var textHeight = _title.PreferredSize.Height + (_details is null ? 0 : _details.PreferredSize.Height + Px(8));
        var bodyHeight = Math.Max(Px(icon == MessageBoxIcon.None ? 84 : 112), Math.Max(textHeight, icon == MessageBoxIcon.None ? 0 : Px(44)) + Px(56));
        ClientSize = new Size(clientWidth, bodyHeight + _footer.Height);

        // Place the text next to the icon (mirrored in Arabic).
        var y = (bodyHeight - textHeight) / 2;
        PlaceLabel(_title, y, iconSpace);
        if (_details is not null)
        {
            PlaceLabel(_details, y + _title.PreferredSize.Height + Px(8), iconSpace);
        }

        PlaceButtons();
    }

    public static DialogResult Show(
        IWin32Window? owner,
        AppTheme theme,
        string text,
        string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None,
        string[]? labels = null,
        bool noIsDestructive = false)
    {
        using var box = new ThemedMessageBox(theme, text, caption, buttons, icon, labels, noIsDestructive);
        return box.ShowDialog(owner);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.TrySetDarkTitleBar(Handle, _theme.IsDark);
        NativeMethods.TryUseRoundedCorners(Handle);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = _escapeResult;
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private static string Label(string[]? labels, int index, string fallback) =>
        labels is not null && index < labels.Length ? labels[index] : Loc.T(fallback);

    private void PlaceLabel(Label label, int top, int iconSpace)
    {
        var size = label.PreferredSize;
        var margin = Px(28);
        var left = _rtl
            ? ClientSize.Width - margin - iconSpace - size.Width
            : margin + iconSpace;
        label.Location = new Point(left, top);
    }

    private void AddButton(string caption, DialogResult result, bool primary, bool destructive)
    {
        var danger = Color.FromArgb(230, 90, 90);
        var button = new Button
        {
            Text = caption,
            DialogResult = result,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(Px(104), Px(36)),
            ForeColor = primary ? Color.White : destructive ? danger : _theme.MenuFore,
            BackColor = primary ? _theme.Accent : _theme.TabInactiveBack,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            RightToLeft = RightToLeft,
        };
        button.FlatAppearance.BorderColor = primary ? _theme.Accent : destructive ? Color.FromArgb(120, danger) : _theme.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(_theme.Accent, 0.15f) : _theme.MenuHover;
        button.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(_theme.Accent, 0.1f) : _theme.TabHoverBack;
        _footer.Controls.Add(button);
        _buttonList.Add(button);

        if (primary)
        {
            AcceptButton = button;
        }
    }

    /// <summary>Buttons sit at the end of the footer; the main action comes first when reading.</summary>
    private void PlaceButtons()
    {
        var gap = Px(10);
        var margin = Px(20);
        var top = (_footer.Height - Px(36)) / 2;
        var total = _buttonList.Count * Px(104) + (_buttonList.Count - 1) * gap;

        for (var i = 0; i < _buttonList.Count; i++)
        {
            var offset = i * (Px(104) + gap);
            // LTR: group on the right, main action leftmost of the group. RTL: group on the left,
            // main action rightmost of the group.
            var left = _rtl
                ? margin + total - Px(104) - offset
                : _footer.Width - margin - total + offset;
            _buttonList[i].Location = new Point(left, top);
        }
    }

    private void Body_Paint(object? sender, PaintEventArgs e)
    {
        if (_icon == MessageBoxIcon.None)
        {
            return;
        }

        var (glyph, color) = _icon switch
        {
            MessageBoxIcon.Error => ("✕", Color.FromArgb(220, 70, 70)),
            MessageBoxIcon.Warning => ("!", Color.FromArgb(230, 170, 40)),
            MessageBoxIcon.Information => ("i", _theme.Accent),
            _ => ("?", _theme.Accent),
        };

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var size = Px(44);
        var left = _rtl ? _body.Width - Px(28) - size : Px(28);
        var rect = new Rectangle(left, (_body.Height - size) / 2, size, size);
        using (var brush = new SolidBrush(Color.FromArgb(40, color)))
        {
            e.Graphics.FillEllipse(brush, rect);
        }

        var inner = Rectangle.Inflate(rect, -Px(5), -Px(5));
        using (var brush = new SolidBrush(color))
        {
            e.Graphics.FillEllipse(brush, inner);
        }

        using var font = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Point);
        TextRenderer.DrawText(
            e.Graphics,
            glyph,
            font,
            inner,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96.0);
}

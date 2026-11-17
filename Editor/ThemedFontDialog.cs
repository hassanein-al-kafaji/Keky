using KekyNotepad.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>Font picker drawn with the application theme (family, size, live preview).</summary>
internal sealed class ThemedFontDialog : Form
{
    private static readonly float[] CommonSizes = { 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 48, 72 };

    private readonly AppTheme _theme;
    private readonly List<string> _allFamilies;
    private readonly TextBox _familyBox;
    private readonly ListBox _familyList;
    private readonly ListBox _sizeList;
    private readonly NumericUpDown _sizeBox;
    private readonly Label _preview;
    private bool _syncing;

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? subAppName, string? subIdList);

    private ThemedFontDialog(AppTheme theme, string family, float size)
    {
        _theme = theme;

        using (var installed = new InstalledFontCollection())
        {
            _allFamilies = installed.Families.Select(f => f.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        Text = Loc.T("Font");
        Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = theme.EditorBack;
        ForeColor = theme.EditorFore;
        ClientSize = new Size(Px(520), Px(440));

        var familyLabel = MakeLabel(Loc.T("Font"), Px(20), Px(16));
        _familyBox = new TextBox
        {
            Location = new Point(Px(20), Px(40)),
            Width = Px(290),
            BackColor = theme.ToolBack,
            ForeColor = theme.EditorFore,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _familyList = new ListBox
        {
            Location = new Point(Px(20), Px(72)),
            Size = new Size(Px(290), Px(220)),
            BackColor = theme.ToolBack,
            ForeColor = theme.EditorFore,
            BorderStyle = BorderStyle.FixedSingle,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = Px(24),
            IntegralHeight = false,
        };

        var sizeLabel = MakeLabel(Loc.T("Size"), Px(330), Px(16));
        _sizeBox = new NumericUpDown
        {
            Location = new Point(Px(330), Px(40)),
            Width = Px(170),
            DecimalPlaces = 0,
            Minimum = 6,
            Maximum = 96,
            BackColor = theme.ToolBack,
            ForeColor = theme.EditorFore,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _sizeList = new ListBox
        {
            Location = new Point(Px(330), Px(72)),
            Size = new Size(Px(170), Px(220)),
            BackColor = theme.ToolBack,
            ForeColor = theme.EditorFore,
            BorderStyle = BorderStyle.FixedSingle,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = Px(24),
            IntegralHeight = false,
        };
        foreach (var s in CommonSizes)
        {
            _sizeList.Items.Add(s);
        }

        var previewLabel = MakeLabel(Loc.T("Preview"), Px(20), Px(304));
        _preview = new Label
        {
            Location = new Point(Px(20), Px(328)),
            Size = new Size(Px(480), Px(60)),
            BackColor = theme.ToolBack,
            ForeColor = theme.EditorFore,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "AaBbYyZz  أبجد هوز 123",
            AutoEllipsis = true,
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = Px(52), BackColor = theme.MenuBack };
        var ok = MakeButton(Loc.T("OK"), DialogResult.OK, true);
        var cancel = MakeButton(Loc.T("Cancel"), DialogResult.Cancel, false);
        ok.Location = new Point(Px(520 - 12 - 92 - 6 - 92), Px(9));
        cancel.Location = new Point(Px(520 - 12 - 92), Px(9));
        footer.Controls.Add(ok);
        footer.Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.AddRange(new Control[] { familyLabel, _familyBox, _familyList, sizeLabel, _sizeBox, _sizeList, previewLabel, _preview, footer });

        FillFamilies(string.Empty);
        SelectFamily(family);
        _sizeBox.Value = (decimal)Math.Clamp(Math.Round(size), 6, 96);
        SyncSizeList();
        UpdatePreview();

        _familyBox.TextChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            FillFamilies(_familyBox.Text.Trim());
            if (_familyList.Items.Count > 0 && _familyList.SelectedIndex < 0)
            {
                _syncing = true;
                _familyList.SelectedIndex = 0;
                _syncing = false;
            }

            UpdatePreview();
        };
        _familyList.DrawItem += DrawFamily;
        _familyList.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing || _familyList.SelectedItem is not string name)
            {
                return;
            }

            _syncing = true;
            _familyBox.Text = name;
            _syncing = false;
            UpdatePreview();
        };
        _sizeList.DrawItem += DrawSize;
        _sizeList.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing || _sizeList.SelectedItem is not float value)
            {
                return;
            }

            _sizeBox.Value = (decimal)value;
        };
        _sizeBox.ValueChanged += (_, _) =>
        {
            SyncSizeList();
            UpdatePreview();
        };
    }

    public static bool Pick(IWin32Window? owner, AppTheme theme, string family, float size, out string chosenFamily, out float chosenSize)
    {
        using var dialog = new ThemedFontDialog(theme, family, size);
        if (dialog.ShowDialog(owner) == DialogResult.OK && dialog._familyList.SelectedItem is string name)
        {
            chosenFamily = name;
            chosenSize = (float)dialog._sizeBox.Value;
            return true;
        }

        chosenFamily = family;
        chosenSize = size;
        return false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.TrySetDarkTitleBar(Handle, _theme.IsDark);
        NativeMethods.TryUseRoundedCorners(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_theme.IsDark)
        {
            // Dark scroll bars where the OS supports them.
            SetWindowTheme(_familyList.Handle, "DarkMode_Explorer", null);
            SetWindowTheme(_sizeList.Handle, "DarkMode_Explorer", null);
        }

        _familyBox.Focus();
        _familyBox.SelectAll();
    }

    private void FillFamilies(string filter)
    {
        var selected = _familyList.SelectedItem as string;
        _syncing = true;
        _familyList.BeginUpdate();
        _familyList.Items.Clear();
        foreach (var name in _allFamilies)
        {
            if (filter.Length == 0 || name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            {
                _familyList.Items.Add(name);
            }
        }

        if (selected is not null)
        {
            var index = _familyList.Items.IndexOf(selected);
            if (index >= 0)
            {
                _familyList.SelectedIndex = index;
            }
        }

        _familyList.EndUpdate();
        _syncing = false;
    }

    private void SelectFamily(string family)
    {
        var index = _familyList.Items.IndexOf(family);
        _syncing = true;
        if (index >= 0)
        {
            _familyList.SelectedIndex = index;
            _familyList.TopIndex = Math.Max(0, index - 3);
        }

        _familyBox.Text = family;
        _syncing = false;
    }

    private void SyncSizeList()
    {
        var index = Array.IndexOf(CommonSizes, (float)_sizeBox.Value);
        _syncing = true;
        _sizeList.SelectedIndex = index;
        _syncing = false;
    }

    private void UpdatePreview()
    {
        var name = _familyList.SelectedItem as string;
        if (name is null)
        {
            return;
        }

        try
        {
            var size = Math.Min(28f, (float)_sizeBox.Value);
            var old = _preview.Font;
            _preview.Font = new Font(name, size, FontStyle.Regular, GraphicsUnit.Point);
            if (!ReferenceEquals(old, Font))
            {
                old.Dispose();
            }
        }
        catch (ArgumentException)
        {
            // Family without a regular style; keep the previous preview.
        }
    }

    private void DrawFamily(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }

        DrawRow(e, (string)_familyList.Items[e.Index]);
    }

    private void DrawSize(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }

        DrawRow(e, ((float)_sizeList.Items[e.Index]).ToString("0"));
    }

    private void DrawRow(DrawItemEventArgs e, string text)
    {
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? _theme.Accent : _theme.ToolBack))
        {
            e.Graphics.FillRectangle(back, e.Bounds);
        }

        var bounds = new Rectangle(e.Bounds.X + Px(8), e.Bounds.Y, e.Bounds.Width - Px(12), e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            text,
            Font,
            bounds,
            selected ? Color.White : _theme.EditorFore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    private Label MakeLabel(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(x, y),
        ForeColor = _theme.DisabledFore,
        BackColor = Color.Transparent,
    };

    private Button MakeButton(string caption, DialogResult result, bool primary)
    {
        var button = new Button
        {
            Text = caption,
            DialogResult = result,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(Px(92), Px(34)),
            ForeColor = primary ? Color.White : _theme.MenuFore,
            BackColor = primary ? _theme.Accent : _theme.TabInactiveBack,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = primary ? _theme.Accent : _theme.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(_theme.Accent, 0.15f) : _theme.MenuHover;
        button.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(_theme.Accent, 0.1f) : _theme.TabHoverBack;
        return button;
    }

    private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96.0);
}

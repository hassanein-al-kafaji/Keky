using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Windows.Forms;
using KekyNotepad.Editor;
using KekyNotepad.Services;

namespace KekyNotepad;

/// <summary>Find and replace bar: next / previous, match case, all open tabs, replace and replace all.</summary>
public sealed partial class MainForm
{
    private readonly FlowLayoutPanel _replaceFlow = new();
    private readonly Label _replaceLabel = new();
    private readonly TextBox _replaceBox = new();
    private readonly Button _findPrevButton = new();
    private readonly Button _replaceButton = new();
    private readonly Button _replaceAllButton = new();
    private readonly CheckBox _caseToggle = new();
    private readonly CheckBox _allTabsToggle = new();
    private readonly Label _findStatus = new();
    private readonly ToolTip _findTips = new() { ShowAlways = true };
    private bool _replaceMode;

    private StringComparison FindComparison =>
        _caseToggle.Checked ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;

    private void BuildFindBar()
    {
        _findPanel.Dock = DockStyle.Fill;
        _findPanel.Margin = Padding.Empty;
        _findPanel.Visible = false;

        foreach (var flow in new[] { _findFlow, _replaceFlow })
        {
            flow.Dock = DockStyle.Top;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = false;
            flow.Margin = Padding.Empty;
            flow.RightToLeft = RightToLeft.No;
        }

        _replaceFlow.Visible = false;

        _findLabel.AutoSize = true;
        _replaceLabel.AutoSize = true;

        foreach (var box in new[] { _findBox, _replaceBox })
        {
            box.BorderStyle = BorderStyle.FixedSingle;
        }

        _findBox.KeyDown += FindBox_KeyDown;
        _findBox.TextChanged += (_, _) => _findStatus.Text = string.Empty;
        _replaceBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ReplaceCurrent();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                HideFind();
            }
        };

        foreach (var button in new[] { _findNextButton, _findPrevButton, _findCloseButton, _replaceButton, _replaceAllButton })
        {
            ConfigureFlatButton(button, string.Empty, 80);
        }

        foreach (var toggle in new[] { _caseToggle, _allTabsToggle })
        {
            toggle.Appearance = Appearance.Button;
            toggle.FlatStyle = FlatStyle.Flat;
            toggle.UseVisualStyleBackColor = false;
            toggle.TextAlign = ContentAlignment.MiddleCenter;
            toggle.FlatAppearance.BorderSize = 1;
            toggle.AutoSize = false;
        }

        _findNextButton.Click += (_, _) => FindNext(forward: true);
        _findPrevButton.Click += (_, _) => FindNext(forward: false);
        _findCloseButton.Click += (_, _) => HideFind();
        _replaceButton.Click += (_, _) => ReplaceCurrent();
        _replaceAllButton.Click += (_, _) => ReplaceAll();

        _findStatus.AutoSize = true;

        _findFlow.Controls.Add(_findLabel);
        _findFlow.Controls.Add(_findBox);
        _findFlow.Controls.Add(_findPrevButton);
        _findFlow.Controls.Add(_findNextButton);
        _findFlow.Controls.Add(_caseToggle);
        _findFlow.Controls.Add(_allTabsToggle);
        _findFlow.Controls.Add(_findCloseButton);
        _findFlow.Controls.Add(_findStatus);

        _replaceFlow.Controls.Add(_replaceLabel);
        _replaceFlow.Controls.Add(_replaceBox);
        _replaceFlow.Controls.Add(_replaceButton);
        _replaceFlow.Controls.Add(_replaceAllButton);

        // The last added control docks first, so the replace row goes in first (below).
        _findPanel.Controls.Add(_replaceFlow);
        _findPanel.Controls.Add(_findFlow);

        ApplyFindLanguage();
    }

    private void ApplyFindLanguage()
    {
        _findLabel.Text = Loc.T("Find:");
        _replaceLabel.Text = Loc.T("Replace:");
        _findNextButton.Text = Loc.T("Next");
        _findPrevButton.Text = Loc.T("Previous");
        _findCloseButton.Text = Loc.T("Close");
        _replaceButton.Text = Loc.T("Replace");
        _replaceAllButton.Text = Loc.T("Replace all");
        _caseToggle.Text = "Aa";
        _allTabsToggle.Text = Loc.T("All tabs");
        _findTips.SetToolTip(_caseToggle, Loc.T("Match case"));
        _findTips.SetToolTip(_allTabsToggle, Loc.T("Search in all open tabs"));
        _findTips.SetToolTip(_findPrevButton, Loc.T("Previous match (Shift+F3)"));
        _findTips.SetToolTip(_findNextButton, Loc.T("Next match (F3)"));
        ScaleFindBar(DeviceDpi / 96.0);
    }

    private void ScaleFindBar(double scale)
    {
        int S(double value) => (int)Math.Round(value * scale);

        var rowHeight = S(FindBarHeight);
        _findFlow.Height = rowHeight;
        _replaceFlow.Height = rowHeight;
        _findFlow.Padding = new Padding(S(8), S(7), S(8), S(5));
        _replaceFlow.Padding = new Padding(S(8), S(2), S(8), S(5));

        // The two labels share one width so both text boxes line up.
        var labelWidth = Math.Max(
            TextRenderer.MeasureText(_findLabel.Text, _findLabel.Font).Width,
            TextRenderer.MeasureText(_replaceLabel.Text, _replaceLabel.Font).Width) + S(6);
        foreach (var label in new[] { _findLabel, _replaceLabel })
        {
            label.AutoSize = false;
            label.Width = labelWidth;
            label.Height = S(26);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(0, S(2), S(8), 0);
        }

        foreach (var box in new[] { _findBox, _replaceBox })
        {
            box.Width = S(300);
            box.Margin = new Padding(0, S(3), S(8), 0);
        }

        foreach (var button in new[] { _findNextButton, _findPrevButton, _replaceButton, _replaceAllButton, _findCloseButton })
        {
            var width = Math.Max(S(78), TextRenderer.MeasureText(button.Text, button.Font).Width + S(24));
            button.Size = new Size(width, S(30));
            button.Margin = new Padding(0, 0, S(6), 0);
        }

        _caseToggle.Size = new Size(S(40), S(30));
        _allTabsToggle.Size = new Size(Math.Max(S(86), TextRenderer.MeasureText(_allTabsToggle.Text, _allTabsToggle.Font).Width + S(24)), S(30));
        foreach (var toggle in new[] { _caseToggle, _allTabsToggle })
        {
            toggle.Margin = new Padding(0, 0, S(6), 0);
        }

        _findStatus.Margin = new Padding(S(10), S(7), 0, 0);
    }

    private void ThemeFindExtras(AppTheme theme)
    {
        _replaceFlow.BackColor = theme.ToolBack;
        _replaceLabel.ForeColor = theme.ToolFore;
        _replaceBox.BackColor = theme.EditorBack;
        _replaceBox.ForeColor = theme.EditorFore;
        _findStatus.ForeColor = theme.DisabledFore;
        _findStatus.BackColor = theme.ToolBack;
        ThemeButton(_findPrevButton, theme);
        ThemeButton(_replaceButton, theme);
        ThemeButton(_replaceAllButton, theme);

        foreach (var toggle in new[] { _caseToggle, _allTabsToggle })
        {
            toggle.BackColor = theme.EditorBack;
            toggle.ForeColor = theme.ToolFore;
            toggle.FlatAppearance.BorderColor = theme.Border;
            toggle.FlatAppearance.CheckedBackColor = theme.Accent;
            toggle.FlatAppearance.MouseOverBackColor = theme.MenuHover;
            toggle.FlatAppearance.MouseDownBackColor = theme.TabHoverBack;
        }
    }

    private float FindPanelHeight()
    {
        var rows = _replaceMode ? 2 : 1;
        return (float)Math.Round(FindBarHeight * (DeviceDpi / 96.0) * rows);
    }

    private void ShowFind(bool? replace = null)
    {
        _replaceMode = replace ?? (_findPanel.Visible && _replaceMode);
        _replaceFlow.Visible = _replaceMode;
        _root.RowStyles[1].Height = FindPanelHeight();
        _findPanel.Visible = true;

        // Start from the selected text, like most editors.
        var selected = ActiveDocument?.Editor.SelectedText;
        if (!string.IsNullOrEmpty(selected) && selected.Length < 200 && !selected.Contains('\n'))
        {
            _findBox.Text = selected;
        }

        if (_replaceMode && _findBox.TextLength > 0)
        {
            _replaceBox.Focus();
        }
        else
        {
            _findBox.Focus();
            _findBox.SelectAll();
        }
    }

    private void HideFind()
    {
        _findPanel.Visible = false;
        _root.RowStyles[1].Height = 0f;
        ActiveDocument?.Editor.Focus();
    }

    private void FindBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            FindNext(forward: !e.Shift);
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            HideFind();
        }
    }

    private bool TryFind(string text, string query, int from, bool forward, out int index)
    {
        index = -1;
        if (text.Length == 0 || query.Length == 0)
        {
            return false;
        }

        if (forward)
        {
            if (from > text.Length)
            {
                return false;
            }

            index = text.IndexOf(query, Math.Max(0, from), FindComparison);
        }
        else
        {
            if (from <= 0)
            {
                return false;
            }

            // A match must start before 'from': search backwards from the end of that window.
            var startIndex = Math.Min(text.Length - 1, from - 1 + query.Length - 1);
            index = text.LastIndexOf(query, startIndex, FindComparison);
        }

        return index >= 0;
    }

    private void SelectMatch(EditorTab document, int index, int length)
    {
        if (!ReferenceEquals(ActiveDocument, document))
        {
            _tabs.SelectedTab = document.Page;
        }

        var editor = document.Editor;
        editor.Select(index, length);
        editor.ScrollToCaret();
        editor.Focus();
    }

    private void FindNext(bool forward = true)
    {
        var document = ActiveDocument;
        if (document is null)
        {
            return;
        }

        var query = _findBox.Text;
        if (query.Length == 0)
        {
            _findBox.Focus();
            return;
        }

        var editor = document.Editor;
        var text = editor.Text;
        var start = forward ? editor.SelectionStart + editor.SelectionLength : editor.SelectionStart;

        if (TryFind(text, query, start, forward, out var index))
        {
            _findStatus.Text = string.Empty;
            SelectMatch(document, index, query.Length);
            return;
        }

        if (_allTabsToggle.Checked && _documents.Count > 1)
        {
            var position = _documents.IndexOf(document);
            for (var step = 1; step < _documents.Count; step++)
            {
                var other = _documents[(position + (forward ? step : -step) + (_documents.Count * 2)) % _documents.Count];
                var otherText = other.Editor.Text;
                if (TryFind(otherText, query, forward ? 0 : otherText.Length, forward, out index))
                {
                    _findStatus.Text = string.Empty;
                    SelectMatch(other, index, query.Length);
                    return;
                }
            }
        }

        // Wrap around inside the current tab.
        if (TryFind(text, query, forward ? 0 : text.Length, forward, out index))
        {
            _findStatus.Text = Loc.T("Wrapped around");
            SelectMatch(document, index, query.Length);
            return;
        }

        _findStatus.Text = Loc.T("No results");
        SystemSounds.Beep.Play();
    }

    private void ReplaceCurrent()
    {
        var document = ActiveDocument;
        if (document is null || _findBox.TextLength == 0)
        {
            return;
        }

        var editor = document.Editor;
        if (editor.SelectionLength > 0 && string.Equals(editor.SelectedText, _findBox.Text, FindComparison))
        {
            editor.SelectedText = _replaceBox.Text;
        }

        FindNext(forward: true);
    }

    private void ReplaceAll()
    {
        var query = _findBox.Text;
        if (query.Length == 0)
        {
            _findBox.Focus();
            return;
        }

        var targets = _allTabsToggle.Checked
            ? _documents.ToList()
            : (ActiveDocument is { } active ? new List<EditorTab> { active } : new List<EditorTab>());

        var total = 0;
        foreach (var document in targets)
        {
            total += ReplaceAllIn(document.Editor, query, _replaceBox.Text);
        }

        _findStatus.Text = total == 0 ? Loc.T("No results") : Loc.F("{0} replaced", total);
        if (total == 0)
        {
            SystemSounds.Beep.Play();
        }
    }

    private int ReplaceAllIn(RichTextBox editor, string query, string replacement)
    {
        var text = editor.Text;
        var positions = new List<int>();
        var cursor = 0;
        while (cursor <= text.Length - query.Length)
        {
            var found = text.IndexOf(query, cursor, FindComparison);
            if (found < 0)
            {
                break;
            }

            positions.Add(found);
            cursor = found + query.Length;
        }

        if (positions.Count == 0)
        {
            return 0;
        }

        // Replace from the end so earlier positions stay valid; repaint once at the end.
        var handle = editor.Handle;
        NativeMethods.SetRedraw(handle, false);
        try
        {
            for (var i = positions.Count - 1; i >= 0; i--)
            {
                editor.Select(positions[i], query.Length);
                editor.SelectedText = replacement;
            }
        }
        finally
        {
            NativeMethods.SetRedraw(handle, true);
            editor.Invalidate();
        }

        return positions.Count;
    }
}

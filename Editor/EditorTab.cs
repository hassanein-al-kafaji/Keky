using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>
/// One document: a TabPage plus its RichTextBox. All editor specific behaviour,
/// including theme application, syntax highlighting, fonts and direction changes,
/// lives here.
/// </summary>
public sealed class EditorTab : IDisposable
{
    public const string DefaultFontFamily = "Consolas";
    public const float DefaultFontSize = 14.5f;

    private readonly System.Windows.Forms.Timer _highlightTimer;
    private AppTheme _theme;
    private Font _editorFont;
    private string _fontFamily;
    private float _fontSize;
    private TextDirectionMode _directionMode;
    private bool _isRtl;
    private bool _isLoading;
    private bool _applyingLayout;
    private bool _isHighlighting;
    private bool _directionUpdatePending;
    private bool _hasTokenColors;
    private readonly LineNumberGutter _gutter;

    public EditorTab(
        string id,
        string title,
        AppTheme theme,
        string languageKey,
        TextDirectionMode directionMode,
        bool wordWrap = false,
        string fontFamily = DefaultFontFamily,
        float fontSize = DefaultFontSize)
    {
        Id = id;
        Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title;
        _theme = theme ?? AppTheme.Light;
        LanguageKey = Languages.Get(languageKey).Key;
        _directionMode = directionMode;
        _fontFamily = string.IsNullOrWhiteSpace(fontFamily) ? DefaultFontFamily : fontFamily;
        _fontSize = Math.Clamp(fontSize, 7f, 36f);
        WordWrap = wordWrap;
        _isRtl = directionMode == TextDirectionMode.RightToLeft;
        _editorFont = CreateEditorFont(_fontFamily, _fontSize);

        Editor = new KekyRichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = _editorFont,
            AcceptsTab = true,
            AutoWordSelection = false,
            DetectUrls = false,
            HideSelection = false,
            ShortcutsEnabled = true,
            ScrollBars = wordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both,
            WordWrap = wordWrap,
            BackColor = _theme.EditorBack,
            ForeColor = _theme.EditorFore,
            SelectionBackColor = _theme.EditorSelection,
            RightToLeft = _isRtl ? RightToLeft.Yes : RightToLeft.No,
        };

        Page = new TabPage(Title)
        {
            BackColor = _theme.EditorBack,
            ForeColor = _theme.EditorFore,
            Padding = new Padding(0),
            RightToLeft = _isRtl ? RightToLeft.Yes : RightToLeft.No,
            Tag = this,
        };
        Page.Controls.Add(Editor);

        _gutter = new LineNumberGutter(Editor, _theme);
        _gutter.SetSide(_isRtl);
        Page.Controls.Add(_gutter);
        Editor.BringToFront(); // the Fill control must dock last, after the gutter took its strip

        _highlightTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _highlightTimer.Tick += HighlightTimer_Tick;

        Editor.TextChanged += Editor_TextChanged;
        Editor.SelectionChanged += Editor_SelectionChanged;
        Editor.HandleCreated += Editor_HandleCreated;
    }

    public string Id { get; }
    public TabPage Page { get; }
    public RichTextBox Editor { get; }

    /// <summary>Encoding the file on disk uses (see <see cref="Services.TextFile"/>).</summary>
    public string FileEncodingKey { get; set; } = Services.TextFile.Utf8;

    /// <summary>Line ending the file on disk uses; the editor itself always holds "\n".</summary>
    public string NewLine { get; set; } = Services.TextFile.DefaultNewLine;

    public string? FilePath { get; private set; }
    public string Title { get; private set; }
    public string LanguageKey { get; private set; }
    public bool IsRtl => _isRtl;
    public bool IsDirty { get; private set; }
    public bool WordWrap { get; private set; }
    public DateTime? LastAutosave { get; private set; }
    public TextDirectionMode DirectionMode => _directionMode;
    public string FontFamily => _fontFamily;
    public float FontSize => _fontSize;

    public bool IsHighlighting => _isHighlighting;

    public event EventHandler? ContentChanged;
    public event EventHandler? CaretMoved;
    public event EventHandler? StateChanged;

    public string DisplayTitle => IsDirty ? Title + " ●" : Title;

    public void Rename(string title)
    {
        var clean = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();
        if (string.Equals(Title, clean, StringComparison.Ordinal))
        {
            return;
        }

        Title = clean;
        UpdateTitle();
    }

    public void LoadContent(string text, bool dirty, int caretPosition)
    {
        _isLoading = true;
        try
        {
            Editor.Text = text ?? string.Empty;
            IsDirty = dirty;
            LastAutosave = null;

            var caret = Math.Clamp(caretPosition, 0, Editor.TextLength);
            Editor.Select(caret, 0);

            // Direction/highlighting can raise TextChanged on some systems.
            // Keep _isLoading set so loading never marks the document dirty.
            UpdateDirection(force: true);
            UpdateTitle();
            HighlightNow();
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void SetFilePath(string path)
    {
        FilePath = path;
        Title = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(Title))
        {
            Title = path;
        }

        UpdateTitle();
    }

    public void MarkClean()
    {
        IsDirty = false;
        LastAutosave = null;
        UpdateTitle();
    }

    public void SetAutosaved(DateTime when)
    {
        LastAutosave = when;
    }

    public void SetLanguage(string languageKey)
    {
        var language = Languages.Get(languageKey);
        if (language.Key == LanguageKey)
        {
            return;
        }

        LanguageKey = language.Key;
        HighlightNow();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Changes the editor typeface and point size, keeping the viewport steady.</summary>
    public void SetBaseFont(string family, float size)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            family = DefaultFontFamily;
        }

        size = Math.Clamp(size, 7f, 36f);
        if (string.Equals(_fontFamily, family, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(_fontSize - size) < 0.05f)
        {
            return;
        }

        _fontFamily = family;
        _fontSize = size;
        ApplyFont();
    }

    public void SetDirectionMode(TextDirectionMode mode)
    {
        if (_directionMode == mode)
        {
            UpdateDirection(force: true);
            return;
        }

        _directionMode = mode;
        UpdateDirection(force: true);
    }

    public void SetLineNumbers(bool visible)
    {
        if (_gutter.Visible != visible)
        {
            _gutter.Visible = visible;
        }
    }

    public void SetWordWrap(bool wordWrap)
    {
        if (WordWrap == wordWrap)
        {
            return;
        }

        WordWrap = wordWrap;
        Editor.WordWrap = wordWrap;
        Editor.ScrollBars = wordWrap
            ? RichTextBoxScrollBars.Vertical
            : RichTextBoxScrollBars.Both;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyTheme(AppTheme theme)
    {
        _theme = theme ?? AppTheme.Light;

        // Recoloring a RichTextBox raises TextChanged on some Windows versions,
        // which would wrongly mark a clean document as modified.

        _applyingLayout = true;
        try
        {
            Page.BackColor = _theme.EditorBack;
            Page.ForeColor = _theme.EditorFore;

            Editor.BackColor = _theme.EditorBack;
            Editor.ForeColor = _theme.EditorFore;
            Editor.SelectionBackColor = _theme.EditorSelection;
            _gutter.Theme = _theme;
            if (Editor.IsHandleCreated)
            {
                NativeMethods.ApplyScrollBarTheme(Editor.Handle, _theme.IsDark);
            }
        }
        finally
        {
            _applyingLayout = false;
        }

        HighlightNow();
        Page.Invalidate();
    }

    public void HighlightNow()
    {
        if (!Editor.IsHandleCreated)
        {
            return;
        }

        _highlightTimer.Stop();

        var language = Languages.Get(LanguageKey);
        var willColor = SyntaxHighlighter.CanHighlight(language, Editor.TextLength);

        // Plain text (the usual case for Arabic notes) needs no recoloring at all,
        // so do not touch the control while the user types.
        if (!willColor && !_hasTokenColors)
        {
            return;
        }

        _isHighlighting = true;
        try
        {
            SyntaxHighlighter.Apply(Editor, language, _theme);
            _hasTokenColors = willColor;
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
            // The native handle is being rebuilt; the next tick retries.
        }
        finally
        {
            _isHighlighting = false;
        }
    }

    public void Dispose()
    {
        _highlightTimer.Stop();
        _highlightTimer.Tick -= HighlightTimer_Tick;
        _highlightTimer.Dispose();

        Editor.TextChanged -= Editor_TextChanged;
        Editor.SelectionChanged -= Editor_SelectionChanged;
        Editor.HandleCreated -= Editor_HandleCreated;

        // The gutter listens to the editor, so it must go first.
        _gutter.Dispose();
        Page.Dispose();
        _editorFont.Dispose();
    }

    private static Font CreateEditorFont(string family, float size)
    {
        try
        {
            return new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
        }
        catch
        {
            return new Font(DefaultFontFamily, size, FontStyle.Regular, GraphicsUnit.Point);
        }
    }

    private void ApplyFont()
    {
        var previous = _editorFont;
        var next = CreateEditorFont(_fontFamily, _fontSize);
        _editorFont = next;

        if (!Editor.IsHandleCreated)
        {
            Editor.Font = next;
            previous.Dispose();
            return;
        }

        var caret = Math.Clamp(Editor.SelectionStart, 0, Editor.TextLength);
        var selectionLength = Math.Clamp(Editor.SelectionLength, 0, Editor.TextLength);
        var firstVisibleLine = NativeMethods.GetFirstVisibleLine(Editor.Handle);
        NativeMethods.SetRedraw(Editor.Handle, false);

        _applyingLayout = true;
        try
        {
            Editor.Font = next;
            Editor.Select(caret, selectionLength);

            var current = NativeMethods.GetFirstVisibleLine(Editor.Handle);
            if (current != firstVisibleLine)
            {
                NativeMethods.ScrollLines(Editor.Handle, firstVisibleLine - current);
            }
        }
        finally
        {
            _applyingLayout = false;
            NativeMethods.SetRedraw(Editor.Handle, true);
            Editor.Invalidate();
            previous.Dispose();
        }

        HighlightNow();
    }

    private void UpdateDirection(bool force)
    {
        var rtl = _directionMode switch
        {
            TextDirectionMode.RightToLeft => true,
            TextDirectionMode.LeftToRight => false,
            _ => LooksRightToLeft(Editor.Text),
        };

        var changed = rtl != _isRtl;
        if (!force && !changed)
        {
            return;
        }

        _isRtl = rtl;
        _gutter.SetSide(rtl);

        // Block re-entry during text changes to prevent flicker and caret jumps.
        if (_applyingLayout || _isHighlighting)
        {
            return;
        }

        var hadFocus = Editor.Focused;
        var caret = Math.Clamp(Editor.SelectionStart, 0, Editor.TextLength);
        var selectionLength = Math.Clamp(Editor.SelectionLength, 0, Math.Max(0, Editor.TextLength - caret));

        // Freeze painting of the whole tab while direction changes (handle rebuild + realignment),
        // then repaint once: this removes the flicker.
        var frozen = Page.IsHandleCreated && Page.Visible && NativeMethods.LockWindowUpdate(Page.Handle);

        _applyingLayout = true;
        try
        {
            // Automatic mode must never touch Editor.RightToLeft: changing it
            // rebuilds the RichEdit window handle and the editor loses keyboard
            // focus, which is why typing stopped after the first Arabic letter
            // until the user clicked inside the editor again.
            if (_directionMode != TextDirectionMode.Automatic)
            {
                var controlRtl = Editor.RightToLeft == RightToLeft.Yes;
                if (controlRtl != rtl)
                {
                    Editor.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
                    Page.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
                    Editor.Select(caret, selectionLength);
                }
            }

            // Paragraph alignment gives the right-to-left look without rebuilding
            // the handle, so typing never loses focus. Only touch it when it
            // actually changes; redundant sets make the caret flicker.
            var alignment = rtl ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            if (Editor.SelectionAlignment != alignment)
            {
                Editor.SelectionAlignment = alignment;
            }

            if (hadFocus && !Editor.Focused)
            {
                Editor.Focus();
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
            // The handle is being rebuilt right now; the next tick retries.
        }
        finally
        {
            _applyingLayout = false;
            if (frozen)
            {
                NativeMethods.LockWindowUpdate(IntPtr.Zero);
                Page.Invalidate(true);
            }
        }

        if (changed || force)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Arabic script detection used by <see cref="TextDirectionMode.Automatic"/>.
    /// Counting letters instead of looking only at the first character keeps mixed
    /// documents (Arabic comments inside English code and vice versa) predictable.
    /// </summary>
    private static bool LooksRightToLeft(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var arabic = 0;
        var latin = 0;
        var limit = Math.Min(text.Length, 8000);

        for (var index = 0; index < limit; index++)
        {
            var character = text[index];

            if ((character >= '\u0600' && character <= '\u06FF') ||
                (character >= '\u0750' && character <= '\u077F') ||
                (character >= '\u08A0' && character <= '\u08FF') ||
                (character >= '\uFB50' && character <= '\uFDFF') ||
                (character >= '\uFE70' && character <= '\uFEFF'))
            {
                arabic++;
            }
            else if ((character >= 'A' && character <= 'Z') ||
                     (character >= 'a' && character <= 'z'))
            {
                latin++;
            }

            if (arabic + latin >= 400)
            {
                break;
            }
        }

        return arabic > 0 && arabic >= latin;
    }

    private void Editor_HandleCreated(object? sender, EventArgs e)
    {
        // Do not touch RightToLeft here: rebuilding the handle from its own
        // creation event is what produced the "disposed object" crash.
        // The constructor already initialises direction, and UpdateDirection
        // applies later changes outside the TextChanged event.
        NativeMethods.ApplyScrollBarTheme(Editor.Handle, _theme.IsDark);
        HighlightNow();
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_isLoading || _applyingLayout || _isHighlighting)
        {
            return;
        }

        IsDirty = true;
        LastAutosave = null;
        UpdateTitle();

        if (_directionMode == TextDirectionMode.Automatic)
        {
            // Never rebuild the RichEdit handle from inside TextChanged: changing
            // RightToLeft there crashes with "Cannot access a disposed object".
            // The highlight timer applies the pending direction after typing pauses.
            _directionUpdatePending = true;
        }

        _highlightTimer.Stop();
        _highlightTimer.Start();

        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Editor_SelectionChanged(object? sender, EventArgs e)
    {
        if (_isHighlighting)
        {
            return;
        }

        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    private void HighlightTimer_Tick(object? sender, EventArgs e)
    {
        _highlightTimer.Stop();

        // Direction changes rebuild the RichEdit handle, so they must never run
        // inside the TextChanged event. Applying them here (after typing pauses)
        // keeps Arabic typing stable.
        if (_directionUpdatePending)
        {
            _directionUpdatePending = false;
            UpdateDirection(force: false);
        }

        HighlightNow();
    }

    private void UpdateTitle()
    {
        var display = DisplayTitle;
        var titleChanged = Page.Text != display;

        if (titleChanged)
        {
            Page.Text = display;
        }

        Page.ToolTipText = FilePath ?? Title;

        if (titleChanged)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

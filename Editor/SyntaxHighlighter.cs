using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>
/// Fast, deliberately small syntax highlighting engine.
///
/// The text is recolored through the RichEdit Text Object Model, which never
/// changes the selection. That means the caret stays exactly where the user left
/// it (no jump to the start of the text, which was especially visible with
/// Arabic) and the viewport never scrolls.
/// </summary>
public static class SyntaxHighlighter
{
    /// <summary>Above this size highlighting is skipped to keep typing responsive.</summary>
    public const int MaxHighlightedLength = 250_000;

    private const int MaxMatchesPerRule = 20_000;

    public static bool CanHighlight(LanguageDefinition language, int textLength)
        => language.SupportsHighlighting && textLength <= MaxHighlightedLength;

    public static void Apply(RichTextBox editor, LanguageDefinition language, AppTheme theme)
    {
        if (!editor.IsHandleCreated)
        {
            return;
        }

        var text = editor.Text;
        if (text.Length == 0)
        {
            return;
        }

        using var model = TextObjectModel.TryOpen(editor.Handle);
        if (model is null)
        {
            return;
        }

        var handle = editor.Handle;
        var redrawDisabled = false;

        try
        {
            NativeMethods.SetRedraw(handle, false);
            redrawDisabled = true;

            model.SuspendUndo();

            // Start from a clean slate.
            model.SetColor(0, text.Length, theme.EditorFore);

            if (CanHighlight(language, text.Length))
            {
                HighlightTokens(model, text, language, theme);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological document must never freeze the UI. The base color stays valid.
        }
        finally
        {
            model.Dispose();

            if (redrawDisabled)
            {
                NativeMethods.SetRedraw(handle, true);
                editor.Invalidate();
            }
        }
    }

    private static void HighlightTokens(
        TextObjectModel model,
        string text,
        LanguageDefinition language,
        AppTheme theme)
    {
        // Ranges already painted as strings. A comment that starts inside one of
        // these ranges (for example // inside "https://...") is ignored.
        var stringRanges = new List<(int Start, int End)>();

        foreach (var rule in language.Rules)
        {
            var count = 0;
            foreach (Match match in rule.Regex.Matches(text))
            {
                if (match.Length == 0)
                {
                    continue;
                }

                if (rule.Kind == TokenKind.Comment && IsInsideRanges(stringRanges, match.Index))
                {
                    continue;
                }

                model.SetColor(match.Index, match.Index + match.Length, ColorFor(rule.Kind, theme));

                if (rule.Kind == TokenKind.String)
                {
                    stringRanges.Add((match.Index, match.Index + match.Length));
                }

                count++;
                if (count >= MaxMatchesPerRule)
                {
                    break;
                }
            }
        }
    }

    private static bool IsInsideRanges(List<(int Start, int End)> ranges, int index)
    {
        if (ranges.Count == 0)
        {
            return false;
        }

        var low = 0;
        var high = ranges.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var range = ranges[middle];

            if (index < range.Start)
            {
                high = middle - 1;
            }
            else if (index >= range.End)
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private static Color ColorFor(TokenKind kind, AppTheme theme) => kind switch
    {
        TokenKind.Keyword => theme.Keyword,
        TokenKind.String => theme.String,
        TokenKind.Comment => theme.Comment,
        TokenKind.Number => theme.Number,
        TokenKind.TypeName => theme.TypeName,
        _ => theme.EditorFore,
    };
}

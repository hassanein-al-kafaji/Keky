using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KekyNotepad.Editor;

public enum TokenKind
{
    Keyword,
    String,
    Comment,
    Number,
    TypeName,
}

/// <summary>A single compiled regular expression used by the lightweight highlighter.</summary>
public sealed class HighlightRule
{
    public HighlightRule(string pattern, TokenKind kind, RegexOptions options = RegexOptions.None)
    {
        Kind = kind;
        Regex = new Regex(
            pattern,
            options | RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    public TokenKind Kind { get; }
    public Regex Regex { get; }
}

public sealed class LanguageDefinition
{
    public LanguageDefinition(
        string key,
        string displayName,
        string defaultExtension,
        string[] extensions,
        params HighlightRule[] rules)
    {
        Key = key;
        DisplayName = displayName;
        DefaultExtension = defaultExtension;
        Extensions = extensions;
        Rules = rules;
    }

    public string Key { get; }
    public string DisplayName { get; }
    public string DefaultExtension { get; }
    public IReadOnlyList<string> Extensions { get; }
    public IReadOnlyList<HighlightRule> Rules { get; }
    public bool SupportsHighlighting => Rules.Count > 0;
}

/// <summary>
/// Regex based definitions. They are intentionally small and fast: keywords first,
/// then numbers, strings and finally comments. Comments are applied last and are
/// skipped when they start inside a string, so "http://..." is not treated as a comment.
/// </summary>
public static class Languages
{
    public static LanguageDefinition Plain { get; } = new(
        "plain",
        "Plain Text",
        ".txt",
        new[] { ".txt", ".md", ".log", ".csv", ".ini" });

    public static LanguageDefinition CSharp { get; } = new(
        "csharp",
        "C#",
        ".cs",
        new[] { ".cs", ".csx" },
        new HighlightRule(
            """\b(?:abstract|as|async|await|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|get|global|goto|if|implicit|in|init|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|partial|private|protected|public|readonly|record|ref|return|sbyte|sealed|set|short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|var|virtual|void|volatile|while|with|yield)\b""",
            TokenKind.Keyword),
        new HighlightRule(
            """(?<![\w])(?:0[xX][0-9A-Fa-f_]+|0[bB][01_]+|\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?[fFdDmMuUlL]*)(?![\w])""",
            TokenKind.Number),
        new HighlightRule(
            """(?:\x22\x22\x22[\s\S]*?\x22\x22\x22|@\$?\x22(?:\x22\x22|[^\x22\n])*\x22|\$@\x22(?:\x22\x22|[^\x22\n])*\x22|\$?\x22(?:\\.|[^\x22\\\n])*\x22|'(?:\\.|[^'\\\n])*')""",
            TokenKind.String),
        new HighlightRule(
            """//[^\n]*|/\*[\s\S]*?\*/""",
            TokenKind.Comment));

    public static LanguageDefinition Python { get; } = new(
        "python",
        "Python",
        ".py",
        new[] { ".py", ".pyw" },
        new HighlightRule(
            """\b(?:False|None|True|and|as|assert|async|await|break|class|continue|def|del|elif|else|except|finally|for|from|global|if|import|in|is|lambda|nonlocal|not|or|pass|raise|return|try|while|with|yield|match|case)\b""",
            TokenKind.Keyword),
        new HighlightRule(
            """(?<![\w])(?:0[xX][0-9A-Fa-f_]+|0[bB][01_]+|0[oO][0-7_]+|\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?)(?![\w])""",
            TokenKind.Number),
        new HighlightRule(
            """(?:\x22\x22\x22[\s\S]*?\x22\x22\x22|'''[\s\S]*?'''|\x22(?:\\.|[^\x22\\\n])*\x22|'(?:\\.|[^'\\\n])*')""",
            TokenKind.String),
        new HighlightRule(
            """#[^\n]*""",
            TokenKind.Comment));

    public static LanguageDefinition JavaScript { get; } = new(
        "javascript",
        "JavaScript / TypeScript",
        ".js",
        new[] { ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs" },
        new HighlightRule(
            """\b(?:as|async|await|break|case|catch|class|const|continue|debugger|default|delete|do|else|export|extends|false|finally|for|from|function|get|if|import|in|instanceof|let|new|null|of|return|set|static|super|switch|this|throw|true|try|typeof|undefined|var|void|while|with|yield)\b""",
            TokenKind.Keyword),
        new HighlightRule(
            """(?<![\w$])(?:0[xX][0-9A-Fa-f_]+|0[bB][01_]+|0[oO][0-7_]+|\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?)(?![\w$])""",
            TokenKind.Number),
        new HighlightRule(
            """(?:`(?:\\.|[\s\S])*?`|\x22(?:\\.|[^\x22\\\n])*\x22|'(?:\\.|[^'\\\n])*')""",
            TokenKind.String),
        new HighlightRule(
            """//[^\n]*|/\*[\s\S]*?\*/""",
            TokenKind.Comment));

    public static LanguageDefinition Json { get; } = new(
        "json",
        "JSON",
        ".json",
        new[] { ".json", ".jsonc" },
        new HighlightRule(
            """\b(?:true|false|null)\b""",
            TokenKind.Keyword),
        new HighlightRule(
            """(?<![\w.])-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?(?![\w.])""",
            TokenKind.Number),
        new HighlightRule(
            """\x22(?:\\.|[^\x22\\])*\x22""",
            TokenKind.String),
        new HighlightRule(
            """\x22(?:\\.|[^\x22\\])*\x22(?=\s*:)""",
            TokenKind.TypeName));

    public static IReadOnlyList<LanguageDefinition> All { get; } = new[]
    {
        Plain,
        CSharp,
        Python,
        JavaScript,
        Json,
    };

    public static LanguageDefinition Get(string? key)
        => All.FirstOrDefault(language => string.Equals(language.Key, key, StringComparison.OrdinalIgnoreCase))
           ?? Plain;

    public static LanguageDefinition FromExtension(string? path)
    {
        var extension = Path.GetExtension(path ?? string.Empty);
        return All.FirstOrDefault(language =>
                   language.Extensions.Any(ext => string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase)))
               ?? Plain;
    }
}

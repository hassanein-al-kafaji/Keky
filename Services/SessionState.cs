using System.Collections.Generic;

namespace KekyNotepad.Services;

public sealed class SessionState
{
    public int Version { get; set; } = 1;

    public string Theme { get; set; } = "light";

    /// <summary>"auto", "ltr" or "rtl" — the direction mode shared by every tab.</summary>
    public string DirectionMode { get; set; } = "auto";

    public string EditorFont { get; set; } = "Consolas";

    public float EditorFontSize { get; set; } = 14.5f;

    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>Index into <see cref="Tabs"/>, not into the add-tab page.</summary>
    public int ActiveTabIndex { get; set; }

    public int WindowLeft { get; set; } = 120;
    public int WindowTop { get; set; } = 80;
    public int WindowWidth { get; set; } = 1160;
    public int WindowHeight { get; set; } = 760;
    public bool Maximized { get; set; }

    public List<SessionTab> Tabs { get; set; } = new();
}

public sealed class SessionTab
{
    public string Id { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string Title { get; set; } = "Untitled";
    public string Language { get; set; } = "plain";
    public bool Rtl { get; set; }
    public bool WordWrap { get; set; }
    public int Caret { get; set; }

    /// <summary>Encoding key from <see cref="TextFile"/> (utf-8, utf-8-bom, utf-16le, ...).</summary>
    public string Encoding { get; set; } = TextFile.Utf8;

    /// <summary>Line ending the file is saved with.</summary>
    public string NewLine { get; set; } = TextFile.DefaultNewLine;
}

/// <summary>
/// User preferences kept in their own small file so they survive even if the
/// session file is damaged or deleted.
/// </summary>
public sealed class UserSettings
{
    public string Theme { get; set; } = "light";
    public string DirectionMode { get; set; } = "auto";
    public string EditorFont { get; set; } = "Consolas";
    public float EditorFontSize { get; set; } = 14.5f;
    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>Right-hand device panel (scrcpy + terminal).</summary>
    public bool DevicePanelOpen { get; set; }
    public int DevicePanelWidth { get; set; } = 420;
    public int DeviceTerminalHeight { get; set; }
    public string? ScrcpyPath { get; set; }
    public bool TerminalPowerShell { get; set; }
    public string? Language { get; set; }
    public bool AutoSaveFiles { get; set; }
}

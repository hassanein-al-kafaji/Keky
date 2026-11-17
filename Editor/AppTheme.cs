using System.Drawing;

namespace KekyNotepad.Editor;

/// <summary>
/// All colors used by the shell and the editor. A new instance is created only
/// for the two built-in themes, so switching themes is just a matter of swapping
/// this object and repainting.
/// </summary>
public sealed class AppTheme
{
    public required string Key { get; init; }
    public required bool IsDark { get; init; }

    public required Color Window { get; init; }
    public required Color MenuBack { get; init; }
    public required Color MenuFore { get; init; }
    public required Color MenuHover { get; init; }

    public required Color ToolBack { get; init; }
    public required Color ToolFore { get; init; }

    public required Color EditorBack { get; init; }
    public required Color EditorFore { get; init; }
    public required Color EditorSelection { get; init; }

    public required Color TabStripBack { get; init; }
    public required Color TabActiveBack { get; init; }
    public required Color TabInactiveBack { get; init; }
    public required Color TabHoverBack { get; init; }
    public required Color TabActiveFore { get; init; }
    public required Color TabInactiveFore { get; init; }
    public required Color CloseHoverBack { get; init; }

    public required Color StatusBack { get; init; }
    public required Color StatusFore { get; init; }

    public required Color Border { get; init; }
    public required Color Accent { get; init; }
    public required Color DisabledFore { get; init; }

    public required Color Keyword { get; init; }
    public required Color String { get; init; }
    public required Color Comment { get; init; }
    public required Color Number { get; init; }
    public required Color TypeName { get; init; }

    public static AppTheme Light { get; } = new()
    {
        Key = "light",
        IsDark = false,

        Window = Color.FromArgb(243, 243, 243),
        MenuBack = Color.FromArgb(250, 250, 250),
        MenuFore = Color.FromArgb(32, 32, 32),
        MenuHover = Color.FromArgb(229, 238, 248),

        ToolBack = Color.FromArgb(245, 245, 245),
        ToolFore = Color.FromArgb(32, 32, 32),

        EditorBack = Color.White,
        EditorFore = Color.FromArgb(30, 30, 30),
        EditorSelection = Color.FromArgb(173, 214, 255),

        TabStripBack = Color.FromArgb(236, 236, 236),
        TabActiveBack = Color.White,
        TabInactiveBack = Color.FromArgb(226, 226, 226),
        TabHoverBack = Color.FromArgb(245, 245, 245),
        TabActiveFore = Color.FromArgb(16, 16, 16),
        TabInactiveFore = Color.FromArgb(82, 82, 82),
        CloseHoverBack = Color.FromArgb(196, 43, 28),

        StatusBack = Color.FromArgb(0, 122, 204),
        StatusFore = Color.White,

        Border = Color.FromArgb(200, 200, 200),
        Accent = Color.FromArgb(0, 103, 192),
        DisabledFore = Color.FromArgb(130, 130, 130),

        Keyword = Color.FromArgb(0, 0, 255),
        String = Color.FromArgb(163, 21, 21),
        Comment = Color.FromArgb(0, 128, 0),
        Number = Color.FromArgb(9, 134, 88),
        TypeName = Color.FromArgb(38, 127, 153),
    };

    public static AppTheme Dark { get; } = new()
    {
        Key = "dark",
        IsDark = true,

        Window = Color.FromArgb(30, 30, 30),
        MenuBack = Color.FromArgb(37, 37, 38),
        MenuFore = Color.FromArgb(212, 212, 212),
        MenuHover = Color.FromArgb(42, 45, 46),

        ToolBack = Color.FromArgb(45, 45, 48),
        ToolFore = Color.FromArgb(212, 212, 212),

        EditorBack = Color.FromArgb(12, 12, 12),
        EditorFore = Color.FromArgb(204, 204, 204),
        EditorSelection = Color.FromArgb(38, 79, 120),

        TabStripBack = Color.FromArgb(37, 37, 38),
        TabActiveBack = Color.FromArgb(12, 12, 12),
        TabInactiveBack = Color.FromArgb(45, 45, 48),
        TabHoverBack = Color.FromArgb(58, 58, 62),
        TabActiveFore = Color.White,
        TabInactiveFore = Color.FromArgb(157, 160, 165),
        CloseHoverBack = Color.FromArgb(196, 43, 28),

        StatusBack = Color.FromArgb(0, 122, 204),
        StatusFore = Color.White,

        Border = Color.FromArgb(63, 63, 70),
        Accent = Color.FromArgb(55, 148, 255),
        DisabledFore = Color.FromArgb(111, 111, 111),

        Keyword = Color.FromArgb(86, 156, 214),
        String = Color.FromArgb(206, 145, 120),
        Comment = Color.FromArgb(106, 153, 85),
        Number = Color.FromArgb(181, 206, 168),
        TypeName = Color.FromArgb(78, 201, 176),
    };

    /// <summary>Pure black (OLED friendly): same text colors as the dark theme on #000.</summary>
    public static AppTheme Black { get; } = new()
    {
        Key = "black",
        IsDark = true,

        Window = Color.FromArgb(0, 0, 0),
        MenuBack = Color.FromArgb(18, 18, 18),
        MenuFore = Color.FromArgb(220, 220, 220),
        MenuHover = Color.FromArgb(34, 34, 34),

        ToolBack = Color.FromArgb(22, 22, 22),
        ToolFore = Color.FromArgb(220, 220, 220),

        EditorBack = Color.FromArgb(0, 0, 0),
        EditorFore = Color.FromArgb(224, 224, 224),
        EditorSelection = Color.FromArgb(38, 79, 120),

        TabStripBack = Color.FromArgb(10, 10, 10),
        TabActiveBack = Color.FromArgb(0, 0, 0),
        TabInactiveBack = Color.FromArgb(22, 22, 22),
        TabHoverBack = Color.FromArgb(40, 40, 40),
        TabActiveFore = Color.White,
        TabInactiveFore = Color.FromArgb(150, 150, 150),
        CloseHoverBack = Color.FromArgb(196, 43, 28),

        StatusBack = Color.FromArgb(0, 122, 204),
        StatusFore = Color.White,

        Border = Color.FromArgb(52, 52, 52),
        Accent = Color.FromArgb(55, 148, 255),
        DisabledFore = Color.FromArgb(105, 105, 105),

        Keyword = Color.FromArgb(86, 156, 214),
        String = Color.FromArgb(206, 145, 120),
        Comment = Color.FromArgb(106, 153, 85),
        Number = Color.FromArgb(181, 206, 168),
        TypeName = Color.FromArgb(78, 201, 176),
    };

    /// <summary>Solarized Dark palette.</summary>
    public static AppTheme Solarized { get; } = new()
    {
        Key = "solarized",
        IsDark = true,

        Window = Color.FromArgb(0, 33, 43),
        MenuBack = Color.FromArgb(7, 54, 66),
        MenuFore = Color.FromArgb(147, 161, 161),
        MenuHover = Color.FromArgb(14, 68, 82),

        ToolBack = Color.FromArgb(7, 54, 66),
        ToolFore = Color.FromArgb(147, 161, 161),

        EditorBack = Color.FromArgb(0, 43, 54),
        EditorFore = Color.FromArgb(147, 161, 161),
        EditorSelection = Color.FromArgb(7, 80, 98),

        TabStripBack = Color.FromArgb(0, 36, 46),
        TabActiveBack = Color.FromArgb(0, 43, 54),
        TabInactiveBack = Color.FromArgb(7, 54, 66),
        TabHoverBack = Color.FromArgb(14, 68, 82),
        TabActiveFore = Color.FromArgb(238, 232, 213),
        TabInactiveFore = Color.FromArgb(101, 123, 131),
        CloseHoverBack = Color.FromArgb(220, 50, 47),

        StatusBack = Color.FromArgb(38, 139, 210),
        StatusFore = Color.White,

        Border = Color.FromArgb(14, 68, 82),
        Accent = Color.FromArgb(38, 139, 210),
        DisabledFore = Color.FromArgb(88, 110, 117),

        Keyword = Color.FromArgb(133, 153, 0),
        String = Color.FromArgb(42, 161, 152),
        Comment = Color.FromArgb(88, 110, 117),
        Number = Color.FromArgb(211, 54, 130),
        TypeName = Color.FromArgb(181, 137, 0),
    };

    /// <summary>All themes, in the order the settings page lists them.</summary>
    public static AppTheme[] All => new[] { Light, Dark, Black, Solarized };

    public static AppTheme FromKey(string? key)
    {
        foreach (var theme in All)
        {
            if (string.Equals(key, theme.Key, System.StringComparison.OrdinalIgnoreCase))
            {
                return theme;
            }
        }

        return Light;
    }


    public static AppTheme Toggle(AppTheme theme) => theme.IsDark ? Light : Dark;
}

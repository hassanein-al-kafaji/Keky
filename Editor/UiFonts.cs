using System.Collections.Generic;
using System.Drawing;

namespace KekyNotepad.Editor;

/// <summary>Shared fonts: creating a Font is slow, and pages create many identical ones.</summary>
internal static class UiFonts
{
    private static readonly Dictionary<(string, float, FontStyle), Font> Cache = new();

    public static Font Get(string family, float size, FontStyle style = FontStyle.Regular)
    {
        var key = (family, size, style);
        if (!Cache.TryGetValue(key, out var font))
        {
            font = new Font(family, size, style, GraphicsUnit.Point);
            Cache[key] = font;
        }

        return font;
    }
}

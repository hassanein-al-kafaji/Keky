using System;

namespace KekyNotepad.Editor;

/// <summary>Ctrl + mouse wheel in any editor or in the terminal asks the form to change the text size.</summary>
internal static class ZoomRequests
{
    public static event Action<int>? Requested;

    public static void Raise(int direction) => Requested?.Invoke(direction);

    /// <summary>True when the wheel message carries the Ctrl key (MK_CONTROL).</summary>
    public static bool IsCtrlWheel(IntPtr wParam) => (wParam.ToInt64() & 0x0008) != 0;

    public static int DirectionOf(IntPtr wParam) => ((short)((wParam.ToInt64() >> 16) & 0xFFFF)) > 0 ? 1 : -1;
}

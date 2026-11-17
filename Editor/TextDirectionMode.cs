namespace KekyNotepad.Editor;

/// <summary>How the editor decides the writing direction of a document.</summary>
public enum TextDirectionMode
{
    /// <summary>Detect Arabic versus Latin text for every document.</summary>
    Automatic,

    /// <summary>Always align the text from the left.</summary>
    LeftToRight,

    /// <summary>Always align the text from the right.</summary>
    RightToLeft,
}

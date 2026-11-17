using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace KekyNotepad.Editor;

/// <summary>
/// Minimal wrapper over the RichEdit Text Object Model (ITextDocument). It lets us
/// recolor ranges of text WITHOUT touching the selection, so the caret never jumps
/// and the view never scrolls while the syntax highlighter runs. This is what
/// stops the caret from flying to the start of the text while typing Arabic.
/// </summary>
internal sealed class TextObjectModel : IDisposable
{
    private const int EM_GETOLEINTERFACE = 0x043C;
    private const int TomSuspendUndo = -9999995;
    private const int TomResumeUndo = -9999994;

    private static readonly Guid IidTextDocument = new("8CC497C0-A1DF-11CE-8098-00AA0047BE5D");

    private ITextDocument? _document;
    private bool _undoSuspended;

    private TextObjectModel(ITextDocument document)
    {
        _document = document;
    }

    public static TextObjectModel? TryOpen(IntPtr editorHandle)
    {
        IntPtr ole = IntPtr.Zero;
        IntPtr document = IntPtr.Zero;

        try
        {
            if (SendMessage(editorHandle, EM_GETOLEINTERFACE, IntPtr.Zero, out ole) == IntPtr.Zero ||
                ole == IntPtr.Zero)
            {
                return null;
            }

            var iid = IidTextDocument;
            if (Marshal.QueryInterface(ole, ref iid, out document) != 0 || document == IntPtr.Zero)
            {
                return null;
            }

            return new TextObjectModel((ITextDocument)Marshal.GetObjectForIUnknown(document));
        }
        catch
        {
            return null;
        }
        finally
        {
            if (document != IntPtr.Zero)
            {
                Marshal.Release(document);
            }

            if (ole != IntPtr.Zero)
            {
                Marshal.Release(ole);
            }
        }
    }

    /// <summary>Stops formatting changes from landing on the user's undo stack.</summary>
    public void SuspendUndo()
    {
        if (_document is null || _undoSuspended)
        {
            return;
        }

        _document.Undo(TomSuspendUndo, out _);
        _undoSuspended = true;
    }

    public void SetColor(int start, int end, Color color)
    {
        if (_document is null || end <= start)
        {
            return;
        }

        ITextRange? range = null;
        ITextFont? font = null;

        try
        {
            if (_document.Range(start, end, out range) != 0 || range is null)
            {
                return;
            }

            if (range.GetFont(out font) != 0 || font is null)
            {
                return;
            }

            font.SetForeColor(ColorTranslator.ToWin32(color));
        }
        finally
        {
            if (font is not null)
            {
                Marshal.ReleaseComObject(font);
            }

            if (range is not null)
            {
                Marshal.ReleaseComObject(range);
            }
        }
    }

    public void Dispose()
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            if (_undoSuspended)
            {
                _document.Undo(TomResumeUndo, out _);
            }
        }
        catch
        {
            // Best effort.
        }
        finally
        {
            _undoSuspended = false;
            Marshal.ReleaseComObject(_document);
            _document = null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, out IntPtr lParam);

    // Only the vtable slots we call are declared with real signatures; the rest are
    // placeholders that keep the slot order identical to tom.h.

    [ComImport, Guid("8CC497C0-A1DF-11CE-8098-00AA0047BE5D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITextDocument
    {
        void IDispatch1();
        void IDispatch2();
        void IDispatch3();
        void IDispatch4();
        void GetName();
        void GetSelection();
        void GetStoryCount();
        void GetStoryRanges();
        void GetSaved();
        void SetSaved();
        void GetDefaultTabStop();
        void SetDefaultTabStop();
        void New();
        void Open();
        void Save();
        void Freeze();
        void Unfreeze();
        void BeginEditCollection();
        void EndEditCollection();

        [PreserveSig]
        int Undo(int count, out int done);

        void Redo();

        [PreserveSig]
        int Range(int start, int end, out ITextRange range);
    }

    [ComImport, Guid("8CC497C2-A1DF-11CE-8098-00AA0047BE5D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITextRange
    {
        void IDispatch1();
        void IDispatch2();
        void IDispatch3();
        void IDispatch4();
        void GetText();
        void SetText();
        void GetChar();
        void SetChar();
        void GetDuplicate();
        void GetFormattedText();
        void SetFormattedText();
        void GetStart();
        void SetStart();
        void GetEnd();
        void SetEnd();

        [PreserveSig]
        int GetFont(out ITextFont font);
    }

    [ComImport, Guid("8CC497C3-A1DF-11CE-8098-00AA0047BE5D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITextFont
    {
        void IDispatch1();
        void IDispatch2();
        void IDispatch3();
        void IDispatch4();
        void GetDuplicate();
        void SetDuplicate();
        void CanChange();
        void IsEqual();
        void Reset();
        void GetStyle();
        void SetStyle();
        void GetAllCaps();
        void SetAllCaps();
        void GetAnimation();
        void SetAnimation();
        void GetBackColor();
        void SetBackColor();
        void GetBold();
        void SetBold();
        void GetEmboss();
        void SetEmboss();
        void GetForeColor();

        [PreserveSig]
        int SetForeColor(int color);
    }
}

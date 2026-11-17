using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>
/// RichTextBox with two deliberate differences:
///  * no native right-click menu (the form copies / pastes on right-click instead);
///  * pasting is always plain text, so text copied from Word or a web page can
///    never bring in a foreign font and break the chosen editor font.
/// </summary>
internal sealed class KekyRichTextBox : RichTextBox
{
    private const int WM_CONTEXTMENU = 0x007B;
    private const int WM_PASTE = 0x0302;
    private const int WM_MOUSEWHEEL = 0x020A;

    private const int WM_SETCURSOR = 0x0020;
    private const int HTCLIENT = 1;

    public KekyRichTextBox()
    {
        Cursor = Cursors.IBeam;
    }

    protected override void WndProc(ref Message m)
    {
        // RichEdit flips between I-beam and arrow (over a selection, the left
        // margin, around line breaks). Keep the I-beam steady over the text area;
        // scroll bars keep their normal arrow.
        if (m.Msg == WM_SETCURSOR && (m.LParam.ToInt64() & 0xFFFF) == HTCLIENT)
        {
            Cursor.Current = Cursors.IBeam;
            m.Result = (IntPtr)1;
            return;
        }

        if (m.Msg == WM_MOUSEWHEEL && ZoomRequests.IsCtrlWheel(m.WParam))
        {
            ZoomRequests.Raise(ZoomRequests.DirectionOf(m.WParam));
            return;
        }

        if (m.Msg == WM_CONTEXTMENU)
        {
            return;
        }

        if (m.Msg == WM_PASTE)
        {
            PastePlainText();
            return;
        }

        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.V) || keyData == (Keys.Shift | Keys.Insert))
        {
            PastePlainText();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void PastePlainText()
    {
        if (ReadOnly)
        {
            return;
        }

        try
        {
            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                Paste(DataFormats.GetFormat(DataFormats.UnicodeText));
            }
        }
        catch (ExternalException)
        {
            // The clipboard is locked by another program; try again next time.
        }
    }
}

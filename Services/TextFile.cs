using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace KekyNotepad.Services;

/// <summary>Text of a file plus the encoding and line endings it must be saved with.</summary>
public sealed record TextFileContent(string Text, string EncodingKey, string NewLine);

/// <summary>
/// Reads and writes text files without silently changing them. The editor works
/// with "\n" line breaks (that is what RichTextBox returns), so the original
/// encoding and line-ending style are remembered and restored on save.
/// </summary>
public static class TextFile
{
    public const string Utf8 = "utf-8";
    public const string Utf8Bom = "utf-8-bom";
    public const string Utf16Le = "utf-16le";
    public const string Utf16Be = "utf-16be";
    public const string Ansi = "ansi";

    public const string DefaultNewLine = "\r\n";

    static TextFile()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static TextFileContent Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Decode(memory.ToArray());
    }

    public static TextFileContent Decode(byte[] bytes)
    {
        string key;
        var offset = 0;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            key = Utf8Bom;
            offset = 3;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            key = Utf16Le;
            offset = 2;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            key = Utf16Be;
            offset = 2;
        }
        else
        {
            key = IsValidUtf8(bytes) ? Utf8 : Ansi;
        }

        var raw = GetEncoding(key).GetString(bytes, offset, bytes.Length - offset);

        var newLine = raw.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : raw.Contains('\n')
                ? "\n"
                : raw.Contains('\r')
                    ? "\r"
                    : DefaultNewLine;

        return new TextFileContent(Normalize(raw), key, newLine);
    }

    /// <summary>Writes the file and returns the encoding key that was really used.</summary>
    public static string Write(string path, string editorText, string encodingKey, string newLine)
    {
        var text = Normalize(editorText);
        if (newLine != "\n")
        {
            text = text.Replace("\n", string.IsNullOrEmpty(newLine) ? DefaultNewLine : newLine, StringComparison.Ordinal);
        }

        var key = encodingKey;
        var encoding = GetEncoding(key);

        if (key == Ansi)
        {
            // Characters the code page cannot hold would become "?". Move to UTF-8 instead.
            var bytesAnsi = encoding.GetBytes(text);
            if (!string.Equals(encoding.GetString(bytesAnsi), text, StringComparison.Ordinal))
            {
                key = Utf8;
                encoding = GetEncoding(key);
            }
        }

        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(text);
        var all = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, all, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, all, preamble.Length, body.Length);

        File.WriteAllBytes(path, all);
        return key;
    }

    public static string Normalize(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public static Encoding GetEncoding(string? key)
    {
        switch (key)
        {
            case Utf8Bom:
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            case Utf16Le:
                return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
            case Utf16Be:
                return new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
            case Ansi:
                try
                {
                    var page = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
                    return Encoding.GetEncoding(
                        page == 65001 || page == 0 ? 1252 : page,
                        EncoderFallback.ReplacementFallback,
                        DecoderFallback.ReplacementFallback);
                }
                catch
                {
                    return Encoding.Latin1;
                }
            default:
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}

using KekyNotepad.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>
/// Small built-in terminal: runs cmd.exe with redirected input/output. You type right
/// after the prompt, inside the terminal text itself. Tab completes file and folder
/// names (press again to cycle). Full-screen programs are not supported.
/// </summary>
internal sealed class TerminalControl : Control
{
    private const int MaxChars = 100_000;
    private static readonly Regex AnsiPattern = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07", RegexOptions.Compiled);
    private static readonly Regex PromptPattern = new(@"^(?:PS )?([A-Za-z]:\\.*?|[A-Za-z]:)>\s?$", RegexOptions.Compiled);

    private readonly TerminalBox _output = new();
    private readonly IconButton _clearButton = new();
    private readonly ToolTip _clearTip = new() { ShowAlways = true };
    private readonly List<string> _history = new();
    private readonly StringBuilder _currentLine = new();
    private int _historyIndex;
    private int _inputStart;
    private Process? _shell;
    private string _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _cwd;
    private AppTheme _theme = AppTheme.Dark;
    private InputLanguage? _previousLanguage;

    // Shell output is queued and written in small batches so a flood (logcat) never freezes the UI.
    private readonly List<(string Text, bool Error)> _pending = new();
    private int _pendingChars;
    private readonly object _pendingLock = new();
    private readonly System.Windows.Forms.Timer _flushTimer = new() { Interval = 40 };

    // Tab completion state (cycles through matches while Tab is pressed repeatedly).
    private List<string>? _matches;
    private int _matchIndex;
    private int _tokenStartAbsolute;

    public TerminalControl()
    {
        _cwd = _workingDirectory;
        SetStyle(ControlStyles.ContainerControl, true);
        _flushTimer.Tick += (_, _) => FlushPending();
        _flushTimer.Start();

        _output.BorderStyle = BorderStyle.None;
        _output.Dock = DockStyle.Fill;
        _output.WordWrap = true;
        _output.DetectUrls = false;
        _output.AcceptsTab = false;
        _output.ScrollBars = RichTextBoxScrollBars.Vertical;
        _output.Font = new Font("Consolas", 13.5f, FontStyle.Regular, GraphicsUnit.Point);
        _output.Margin = Padding.Empty;
        _output.BeforePaste = EnsureCaretAtInput;

        // After any resize (window, panel, first layout) keep the input line in view.
        _output.SizeChanged += (_, _) =>
        {
            if (!_output.IsHandleCreated)
            {
                return;
            }

            // Wait for the RichEdit window to finish resizing before scrolling.
            BeginInvoke(() =>
            {
                if (!_output.IsDisposed && _output.SelectionStart >= _inputStart)
                {
                    _output.Select(_output.TextLength, 0);
                    _output.ScrollToCaret();
                    NativeMethods.ScrollToBottom(_output.Handle);
                }
            });
        };

        _output.HandleCreated += (_, _) => NativeMethods.ApplyScrollBarTheme(_output.Handle, _theme.IsDark);
        _output.KeyDown += Output_KeyDown;
        _output.KeyPress += Output_KeyPress;
        _output.MouseUp += Output_MouseUp;
        _output.Enter += (_, _) => SwitchToEnglish();
        _output.Leave += (_, _) => RestoreKeyboardLayout();

        // Safe zone: the text never reaches the bottom edge (3 lines of breathing room).
        Padding = new Padding(8, 6, 4, (_output.Font.Height * 3) + 4);
        Controls.Add(_output);

        _clearButton.Glyph = "\uE74D";
        _clearButton.Size = new Size(30, 30);
        _clearButton.Click += (_, _) =>
        {
            ClearScreen();
            _output.Focus();
        };
        _clearTip.SetToolTip(_clearButton, Loc.T("Clear screen"));
        Controls.Add(_clearButton);
        _clearButton.BringToFront();
        Resize += (_, _) => PlaceClearButton();

        ApplyColors();
    }

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            ApplyColors();
            if (_output.IsHandleCreated)
            {
                NativeMethods.ApplyScrollBarTheme(_output.Handle, _theme.IsDark);
            }
        }
    }

    public bool IsRunning => _shell is { HasExited: false };

    public void SetWorkingDirectory(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            _workingDirectory = directory;
            if (!IsRunning)
            {
                _cwd = directory;
            }
        }
    }

    public void FocusInput()
    {
        _output.Focus();
        _output.Select(_output.TextLength, 0);
    }

    /// <summary>True to use PowerShell instead of cmd.exe (restarts the shell).</summary>
    public bool UsePowerShell
    {
        get => _usePowerShell;
        set
        {
            if (_usePowerShell == value)
            {
                return;
            }

            _usePowerShell = value;
            if (IsRunning)
            {
                _output.Clear();
                _inputStart = 0;
                _currentLine.Clear();
                StartShell();
            }
        }
    }

    private bool _usePowerShell;

    /// <summary>Types a command and runs it, as if the user had entered it.</summary>
    public void Execute(string command)
    {
        EnsureStarted();
        _output.Select(_output.TextLength, 0);
        ReplaceInput(command);
        Submit();
        FocusInput();
    }


    /// <summary>Terminal text size in points (follows the editor size).</summary>
    public float FontSize
    {
        get => _output.Font.SizeInPoints;
        set
        {
            value = Math.Clamp(value, 6f, 40f);
            if (Math.Abs(_output.Font.SizeInPoints - value) < 0.05f)
            {
                return;
            }

            var previous = _output.Font;
            _output.Font = new Font("Consolas", value, FontStyle.Regular, GraphicsUnit.Point);
            previous.Dispose();
            Padding = new Padding(8, 6, 4, (_output.Font.Height * 3) + 4);
            if (_output.IsHandleCreated)
            {
                _output.Select(_output.TextLength, 0);
                _output.ScrollToCaret();
                NativeMethods.ScrollToBottom(_output.Handle);
            }
        }
    }

    public void EnsureStarted()
    {
        if (!IsRunning)
        {
            StartShell();
        }
    }

    public void Stop()
    {
        var shell = _shell;
        _shell = null;
        if (shell is null)
        {
            return;
        }

        try
        {
            if (!shell.HasExited)
            {
                shell.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone.
        }

        shell.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _flushTimer.Dispose();
            Stop();
        }

        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ appearance

    private void PlaceClearButton()
    {
        _clearButton.Location = new Point(Math.Max(0, ClientSize.Width - _clearButton.Width - 22), 4);
        _clearButton.BringToFront();
    }

    private void ApplyColors()
    {
        _clearButton.Theme = _theme;
        BackColor = _theme.EditorBack;
        _output.BackColor = _theme.EditorBack;
        _output.ForeColor = _theme.EditorFore;

        if (_output.IsHandleCreated && _output.TextLength > 0)
        {
            var start = _output.SelectionStart;
            var length = _output.SelectionLength;
            _output.SelectAll();
            _output.SelectionColor = _theme.EditorFore;
            _output.Select(start, length);
        }
    }

    // ------------------------------------------------------------------ keyboard layout

    /// <summary>Commands are English: switch the keyboard to English while typing in the terminal.</summary>
    private void SwitchToEnglish()
    {
        try
        {
            var current = InputLanguage.CurrentInputLanguage;
            if (current.Culture.TwoLetterISOLanguageName == "en")
            {
                return;
            }

            foreach (InputLanguage language in InputLanguage.InstalledInputLanguages)
            {
                if (language.Culture.TwoLetterISOLanguageName == "en")
                {
                    _previousLanguage = current;
                    InputLanguage.CurrentInputLanguage = language;
                    return;
                }
            }
        }
        catch (ArgumentException)
        {
            // Layout not available; leave the keyboard as it is.
        }
    }

    /// <summary>Gives the user back their previous layout (e.g. Arabic) when leaving the terminal.</summary>
    private void RestoreKeyboardLayout()
    {
        var previous = _previousLanguage;
        _previousLanguage = null;
        if (previous is null)
        {
            return;
        }

        try
        {
            InputLanguage.CurrentInputLanguage = previous;
        }
        catch (ArgumentException)
        {
        }
    }

    // ------------------------------------------------------------------ shell

    private void StartShell()
    {
        Stop();

        lock (_pendingLock)
        {
            _pending.Clear();
            _pendingChars = 0;
        }

        var shellName = _usePowerShell ? "powershell.exe" : "cmd.exe";
        var info = new ProcessStartInfo(shellName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = _workingDirectory,
        };
        if (_usePowerShell)
        {
            info.ArgumentList.Add("-NoLogo");
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NoExit");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("[Console]::OutputEncoding=[Text.Encoding]::UTF8; function prompt { 'PS ' + (Get-Location).Path + '> ' }");
        }
        else
        {
            info.ArgumentList.Add("/Q");
            info.ArgumentList.Add("/K");
            info.ArgumentList.Add("chcp 65001>nul & prompt $P$G");
        }

        _cwd = _workingDirectory;

        try
        {
            var shell = Process.Start(info);
            if (shell is null)
            {
                Append("Could not start cmd.exe\n");
                return;
            }

            _shell = shell;
            ChildProcessJob.Add(shell);
            _ = Task.Run(() => Pump(shell, shell.StandardOutput, false));
            _ = Task.Run(() => Pump(shell, shell.StandardError, true));
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            Append($"Could not start cmd.exe: {exception.Message}\n");
        }
    }

    /// <summary>
    /// cmd and PowerShell print an empty line before every prompt (and our own Enter already
    /// started a new line). Once a full prompt has arrived, remove that empty line so the
    /// prompt sits right under the command or its output.
    /// </summary>
    private void RemoveBlankLineBeforePrompt()
    {
        var promptLength = _currentLine.Length;
        if (promptLength == 0 || !PromptPattern.IsMatch(_currentLine.ToString()))
        {
            return;
        }

        var promptStart = _output.TextLength - promptLength;
        if (promptStart < 2)
        {
            return;
        }

        _output.Select(promptStart - 2, 2);
        if (_output.SelectedText == "\n\n")
        {
            _output.Select(promptStart - 1, 1);
            _output.SelectedText = string.Empty;
        }

        _output.Select(_output.TextLength, 0);
    }

    // Padding is not applied by Control's own layout, so apply it here: the text keeps a margin
    // on every side, including a safe zone of 3 lines at the bottom.
    public override Rectangle DisplayRectangle
    {
        get
        {
            var rect = base.DisplayRectangle;
            return new Rectangle(
                rect.Left + Padding.Left,
                rect.Top + Padding.Top,
                Math.Max(0, rect.Width - Padding.Horizontal),
                Math.Max(0, rect.Height - Padding.Vertical));
        }
    }

    private void FlushPending()
    {
        var chunk = new StringBuilder();
        var error = false;
        lock (_pendingLock)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            // At most one batch per tick, all of the same kind (normal / error output).
            error = _pending[0].Error;
            while (_pending.Count > 0 && _pending[0].Error == error && chunk.Length < 12_000)
            {
                chunk.Append(_pending[0].Text);
                _pendingChars -= _pending[0].Text.Length;
                _pending.RemoveAt(0);
            }
        }

        Append(chunk.ToString(), error);
    }

    private void Pump(Process shell, StreamReader reader, bool isError)
    {
        var buffer = new char[2048];
        try
        {
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                var text = new string(buffer, 0, read);
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                lock (_pendingLock)
                {
                    if (!ReferenceEquals(shell, _shell))
                    {
                        return;
                    }

                    _pending.Add((text, isError));
                    _pendingChars += text.Length;
                    while (_pendingChars > 400_000 && _pending.Count > 1)
                    {
                        // The reader is far ahead of the screen: keep only the newest output.
                        _pendingChars -= _pending[0].Text.Length;
                        _pending.RemoveAt(0);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The shell was closed.
        }
    }

    /// <summary>Adds program output at the end; whatever is typed afterwards belongs to the new input.</summary>
    private void Append(string text, bool error = false)
    {
        if (_output.IsDisposed)
        {
            return;
        }

        text = AnsiPattern.Replace(text, string.Empty).Replace("\r\n", "\n").Replace("\r", string.Empty).Replace("\f", string.Empty);
        if (text.Length == 0)
        {
            return;
        }

        TrackPrompt(text);

        if (_output.TextLength > MaxChars)
        {
            var cut = _output.TextLength - (MaxChars / 2);
            _output.Select(0, cut);
            _output.SelectedText = string.Empty;
        }

        _output.Select(_output.TextLength, 0);
        _output.SelectionColor = error ? (_theme.IsDark ? Color.FromArgb(241, 76, 76) : Color.FromArgb(197, 15, 31)) : _theme.EditorFore;
        _output.AppendText(text);
        _output.Select(_output.TextLength, 0);
        _output.SelectionColor = _theme.EditorFore;
        RemoveBlankLineBeforePrompt();
        _output.Select(_output.TextLength, 0);
        _output.ScrollToCaret();
        NativeMethods.ScrollToBottom(_output.Handle);
        _inputStart = _output.TextLength;
    }

    /// <summary>Follows the "C:\folder>" prompt so Tab completion knows the current directory.</summary>
    private void TrackPrompt(string text)
    {
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                _currentLine.Clear();
            }
            else
            {
                _currentLine.Append(ch);
            }
        }

        var line = _currentLine.ToString();
        var match = PromptPattern.Match(line);
        if (match.Success)
        {
            var path = match.Groups[1].Value;
            if (Directory.Exists(path))
            {
                _cwd = path;
            }
        }
    }

    // ------------------------------------------------------------------ input handling

    private void EnsureCaretAtInput()
    {
        if (_output.SelectionStart < _inputStart)
        {
            _output.Select(_output.TextLength, 0);
        }
    }

    private void Output_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar))
        {
            EnsureCaretAtInput();
        }
    }

    private void Output_KeyDown(object? sender, KeyEventArgs e)
    {
        var key = e.KeyCode;
        if (key is not (Keys.Tab or Keys.ShiftKey or Keys.ControlKey or Keys.Menu))
        {
            ResetCompletion();
        }

        var beforeInput = _output.SelectionStart < _inputStart;

        switch (e.KeyData)
        {
            case Keys.Enter:
                e.SuppressKeyPress = true;
                Submit();
                return;

            case Keys.Tab:
            case Keys.Shift | Keys.Tab:
                e.SuppressKeyPress = true;
                Complete(e.Shift);
                return;

            case Keys.Up:
                e.SuppressKeyPress = true;
                StepHistory(-1);
                return;

            case Keys.Down:
                e.SuppressKeyPress = true;
                StepHistory(1);
                return;

            case Keys.Escape:
                e.SuppressKeyPress = true;
                ReplaceInput(string.Empty);
                return;

            case Keys.Home:
                e.SuppressKeyPress = true;
                _output.Select(Math.Max(_inputStart, 0), 0);
                return;

            case Keys.Left:
            case Keys.Control | Keys.Left:
                if (_output.SelectionStart <= _inputStart && _output.SelectionLength == 0)
                {
                    e.SuppressKeyPress = true;
                }

                return;

            case Keys.Back:
            case Keys.Control | Keys.Back:
                if (beforeInput || (_output.SelectionStart == _inputStart && _output.SelectionLength == 0))
                {
                    e.SuppressKeyPress = true;
                }

                return;

            case Keys.Delete:
            case Keys.Control | Keys.X:
                if (beforeInput)
                {
                    e.SuppressKeyPress = true;
                }

                return;

            case Keys.Control | Keys.C:
                if (_output.SelectionLength == 0)
                {
                    // A pipe cannot receive Ctrl+C: restart the shell instead.
                    e.SuppressKeyPress = true;
                    _output.Select(_output.TextLength, 0);
                    Append("^C\n");
                    StartShell();
                }

                return;

            case Keys.Control | Keys.L:
                e.SuppressKeyPress = true;
                ClearScreen();
                return;
        }
    }

    private void Output_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        // Same rule as the editor: right-click copies a selection, or pastes when nothing is selected.
        if (_output.SelectionLength > 0)
        {
            _output.Copy();
            _output.Select(_output.TextLength, 0);
        }
        else
        {
            EnsureCaretAtInput();
            _output.PastePlainText();
        }
    }

    private string CurrentInput()
    {
        var length = _output.TextLength - _inputStart;
        if (length <= 0)
        {
            return string.Empty;
        }

        _output.Select(_inputStart, length);
        var text = _output.SelectedText;
        _output.Select(_output.TextLength, 0);
        return text;
    }

    private void ReplaceInput(string text)
    {
        _output.Select(_inputStart, _output.TextLength - _inputStart);
        _output.SelectionColor = _theme.EditorFore;
        _output.SelectedText = text;
        _output.Select(_output.TextLength, 0);
        _output.ScrollToCaret();
    }

    private void ClearScreen()
    {
        _output.Clear();
        _inputStart = 0;
        _currentLine.Clear();
        SendToShell(string.Empty); // the shell prints a fresh prompt
    }

    private void Submit()
    {
        var typed = CurrentInput().Replace("\r", string.Empty);
        _output.Select(_output.TextLength, 0);
        _output.AppendText("\n");
        _inputStart = _output.TextLength;
        _currentLine.Clear();

        foreach (var line in typed.Split('\n'))
        {
            if (line.Trim().Length > 0 && (_history.Count == 0 || _history[^1] != line))
            {
                _history.Add(line);
            }

            var trimmed = line.Trim();
            if (trimmed.Equals("cls", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                ClearScreen();
                continue;
            }

            SendToShell(line);
        }

        _historyIndex = _history.Count;
        _output.ScrollToCaret();
    }

    private void SendToShell(string line)
    {
        EnsureStarted();
        var shell = _shell;
        if (shell is null)
        {
            return;
        }

        try
        {
            shell.StandardInput.WriteLine(line);
            shell.StandardInput.Flush();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            Append("The shell is not running. Press Enter again to start a new one.\n");
            _shell = null;
        }
    }

    private void StepHistory(int delta)
    {
        if (_history.Count == 0)
        {
            return;
        }

        _historyIndex = Math.Clamp(_historyIndex + delta, 0, _history.Count);
        ReplaceInput(_historyIndex < _history.Count ? _history[_historyIndex] : string.Empty);
    }

    // ------------------------------------------------------------------ Tab completion

    private void ResetCompletion()
    {
        _matches = null;
    }

    private void Complete(bool backwards)
    {
        if (_matches is null)
        {
            var input = CurrentInput();
            var tokenStart = FindTokenStart(input);
            var token = input[tokenStart..];
            var matches = FindMatches(token, tokenStart == 0);
            if (matches.Count == 0)
            {
                SystemSounds.Beep.Play();
                return;
            }

            _tokenStartAbsolute = _inputStart + tokenStart;
            _matches = matches;
            _matchIndex = backwards ? matches.Count - 1 : 0;
        }
        else
        {
            _matchIndex = (_matchIndex + (backwards ? -1 : 1) + _matches.Count) % _matches.Count;
        }

        _output.Select(_tokenStartAbsolute, _output.TextLength - _tokenStartAbsolute);
        _output.SelectionColor = _theme.EditorFore;
        _output.SelectedText = _matches[_matchIndex];
        _output.Select(_output.TextLength, 0);
        _output.ScrollToCaret();

        if (_matches.Count == 1)
        {
            ResetCompletion(); // the next Tab completes what comes after (e.g. inside the folder)
        }
    }

    private static readonly string[] BuiltInCommands =
    {
        "assoc", "cd", "chdir", "cls", "copy", "date", "del", "dir", "echo", "erase", "exit", "md", "mkdir",
        "mklink", "move", "path", "pause", "popd", "prompt", "pushd", "rd", "ren", "rename", "rmdir", "set",
        "start", "time", "title", "type", "ver", "vol",
    };

    private string[]? _commandNames;
    private bool _commandScanStarted;

    /// <summary>Built-in commands plus programs on PATH (scanned once in the background).</summary>
    private IEnumerable<string> CommandNames()
    {
        if (!_commandScanStarted)
        {
            _commandScanStarted = true;
            var powerShell = _usePowerShell;
            _ = Task.Run(() => _commandNames = ScanPath(powerShell));
        }

        return _commandNames ?? BuiltInCommands;
    }

    private static string[] ScanPath(bool powerShell)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".cmd", ".bat", ".com" };
        if (powerShell)
        {
            extensions.Add(".ps1");
        }

        var names = new HashSet<string>(BuiltInCommands, StringComparer.OrdinalIgnoreCase);
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if (extensions.Contains(Path.GetExtension(file)))
                    {
                        names.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or ArgumentException)
            {
                // Unreadable PATH entry: skip it.
            }
        }

        return names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static int FindTokenStart(string input)
    {
        var inQuote = false;
        var start = 0;
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] == '"')
            {
                inQuote = !inQuote;
            }
            else if (input[i] == ' ' && !inQuote)
            {
                start = i + 1;
            }
        }

        return start;
    }

    private List<string> FindMatches(string token, bool firstWord)
    {
        var result = new List<string>();
        var raw = token.Replace("\"", string.Empty);

        var separator = raw.LastIndexOfAny(new[] { '\\', '/' });
        var directoryPart = separator >= 0 ? raw[..(separator + 1)] : string.Empty;
        var prefix = separator >= 0 ? raw[(separator + 1)..] : raw;

        // First word of the line: offer program names (PATH + built-in commands) before files.
        if (firstWord && separator < 0 && prefix.Length > 0 && !token.StartsWith('"'))
        {
            foreach (var name in CommandNames())
            {
                if (name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
                {
                    result.Add(name);
                    if (result.Count >= 100)
                    {
                        break;
                    }
                }
            }
        }

        string baseDirectory;
        try
        {
            baseDirectory = directoryPart.Length == 0
                ? _cwd
                : Path.IsPathRooted(directoryPart) ? directoryPart : Path.Combine(_cwd, directoryPart);
            if (!Directory.Exists(baseDirectory))
            {
                return result;
            }
        }
        catch (ArgumentException)
        {
            return result;
        }

        try
        {
            var entries = new DirectoryInfo(baseDirectory)
                .EnumerateFileSystemInfos()
                .Where(info => info.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(info => info is DirectoryInfo)
                .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase);

            foreach (var info in entries)
            {
                var name = directoryPart + info.Name + (info is DirectoryInfo ? "\\" : string.Empty);
                if (result.Contains(name, StringComparer.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                var quote = token.StartsWith('"') || name.Contains(' ');
                result.Add(quote ? $"\"{name}\"" : name);
                if (result.Count >= 200)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // Folder not readable: nothing to offer.
        }

        return result;
    }

    // ------------------------------------------------------------------ text box

    /// <summary>Rich text box that never shows a native menu and always pastes plain text.</summary>
    private sealed class TerminalBox : RichTextBox
    {
        private const int WM_SETCURSOR = 0x0020;
        private const int WM_CONTEXTMENU = 0x007B;
        private const int WM_PASTE = 0x0302;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int HTCLIENT = 1;

        public Action? BeforePaste { get; set; }

        public void PastePlainText()
        {
            try
            {
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    Paste(DataFormats.GetFormat(DataFormats.UnicodeText));
                }
            }
            catch (ExternalException)
            {
                // The clipboard is locked by another program.
            }
        }

        // Without this Tab moves focus to the next control instead of reaching KeyDown.
        protected override bool IsInputKey(Keys keyData)
        {
            var code = keyData & Keys.KeyCode;
            if (code == Keys.Tab && (keyData & Keys.Control) == 0 && (keyData & Keys.Alt) == 0)
            {
                return true;
            }

            return base.IsInputKey(keyData);
        }

        protected override void WndProc(ref Message m)
        {
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
                BeforePaste?.Invoke();
                PastePlainText();
                return;
            }

            base.WndProc(ref m);
        }
    }
}

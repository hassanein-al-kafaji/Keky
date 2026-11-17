using System;
using System.Linq;
using System.Windows.Forms;
using KekyNotepad.Editor;
using KekyNotepad.Services;

namespace KekyNotepad;

/// <summary>The settings tab: a page shown in the work area like a document tab.</summary>
public sealed partial class MainForm
{
    private readonly SettingsView _settingsView = new();
    private bool _settingsOpen;
    private bool _settingsActive;
    private ContextMenuStrip? _syntaxPopup;

    /// <summary>Taskbar / Alt+Tab icon: the same "K" badge as the title bar (embedded app.ico).</summary>
    private void LoadAppIcon()
    {
        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("KekyNotepad.app.ico");
            if (stream is not null)
            {
                Icon = new System.Drawing.Icon(stream);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or System.IO.IOException)
        {
            // Keep the default icon.
        }
    }

    private void InitSettings()
    {
        _work.Controls.Add(_settingsView);
        _settingsView.Command += SettingsCommand;
        ZoomRequests.Requested += direction => ChangeFontSize(direction);
        Loc.Changed += (_, _) => OnLanguageChanged();
    }

    private void ShowSettings()
    {
        if (_device.Visible)
        {
            SetDevicePanel(false);
        }

        _settingsOpen = true;
        _settingsActive = true;
        _tabs.Visible = false;
        _settingsView.Visible = true;
        _settingsView.BringToFront();
        _settingsView.Show(BuildSettingsSnapshot());
        RefreshTitleTabs();
    }

    private void HideSettingsView(bool showTabs)
    {
        if (!_settingsActive)
        {
            return;
        }

        _settingsActive = false;
        _settingsView.Visible = false;
        if (showTabs)
        {
            _tabs.Visible = !_deviceView.Visible;
        }

        RefreshTitleTabs();
    }

    private void CloseSettings()
    {
        HideSettingsView(true);
        _settingsOpen = false;
        RefreshTitleTabs();
        ActiveDocument?.Editor.Focus();
    }

    private SettingsSnapshot BuildSettingsSnapshot()
    {
        var document = ActiveDocument;
        return new SettingsSnapshot
        {
            Language = Loc.Language,
            ThemeKey = _theme.Key,
            FontName = _editorFontFamily,
            FontSize = _editorFontSize,
            Direction = _directionMode switch
            {
                TextDirectionMode.LeftToRight => 1,
                TextDirectionMode.RightToLeft => 2,
                _ => 0,
            },
            WordWrap = document?.WordWrap ?? false,
            LineNumbers = _showLineNumbers,
            SyntaxName = document is null ? string.Empty : Languages.Get(document.LanguageKey).DisplayName,
            HasDocument = document is not null,
            DeviceOpen = _device.Visible,
            PowerShell = _terminalPowerShell,
            AutoSave = _autoSaveFiles,
            ScrcpyPath = _scrcpyPath,
        };
    }

    private void RefreshSettingsPage()
    {
        if (_settingsActive)
        {
            _settingsView.Show(BuildSettingsSnapshot());
        }
    }

    private void OnLanguageChanged()
    {
        _findLabel.Text = Loc.T("Find:");
        _findNextButton.Text = Loc.T("Next");
        _findCloseButton.Text = Loc.T("Close");
        ApplyFindLanguage();
        _device.ApplyLanguage();
        UpdateWindowTitle();
        _titleBar.Invalidate();
        RefreshTitleTabs();
        RefreshSettingsPage();
        SaveSessionState();
    }

    private void SettingsCommand(string name, int value, Control source)
    {
        switch (name)
        {
            case "language":
                Loc.Language = value == 1 ? "ar" : "en";
                return;

            case "theme":
                ApplyTheme(AppTheme.All[Math.Clamp(value, 0, AppTheme.All.Length - 1)], persist: true);
                break;

            case "font":
                ChooseEditorFont();
                break;

            case "size":
                if (value == 0)
                {
                    ResetFontSize();
                }
                else
                {
                    ChangeFontSize(value);
                }

                break;

            case "direction":
                SetDirectionMode(value switch
                {
                    1 => TextDirectionMode.LeftToRight,
                    2 => TextDirectionMode.RightToLeft,
                    _ => TextDirectionMode.Automatic,
                });
                break;

            case "wrap":
                if (ActiveDocument is { } wrapDocument && wrapDocument.WordWrap != (value == 1))
                {
                    ToggleWordWrap();
                }

                break;

            case "numbers":
                if (_showLineNumbers != (value == 1))
                {
                    ToggleLineNumbers();
                }

                break;

            case "syntax":
                ShowSyntaxPopup(source);
                return;

            case "shell":
                _terminalPowerShell = value == 1;
                _powerShellMenuItem.Checked = _terminalPowerShell;
                _terminalDock.UsePowerShell = _terminalPowerShell;
                SaveSessionState();
                break;

            case "autosave":
                _autoSaveFiles = value == 1;
                SaveSessionState();
                break;

            case "scrcpy":
                BrowseScrcpy();
                break;

            case "new":
                NewDocument();
                return;

            case "open":
                OpenFiles();
                return;

            case "save":
                SaveActive(saveAs: false);
                return;

            case "saveas":
                SaveActive(saveAs: true);
                return;

            case "find":
                HideSettingsView(true);
                ShowFind();
                return;

            case "close":
                CloseActiveDocument();
                return;
        }

        RefreshSettingsPage();
    }

    /// <summary>Saves every modified document that already has a file (when the setting is on).</summary>
    private void AutoSaveFilesNow()
    {
        if (!_autoSaveFiles || DateTime.Now < _autoSaveBlockedUntil)
        {
            return;
        }

        foreach (var document in _documents.ToArray())
        {
            if (!document.IsDirty || string.IsNullOrWhiteSpace(document.FilePath))
            {
                continue;
            }

            if (!SaveDocument(document, saveAs: false))
            {
                // Do not nag with an error dialog every second.
                _autoSaveBlockedUntil = DateTime.Now.AddMinutes(1);
                return;
            }
        }
    }

    private void ShowSyntaxPopup(Control anchor)
    {
        var current = ActiveDocument?.LanguageKey;
        _syntaxPopup?.Dispose();
        var popup = new ContextMenuStrip { Renderer = new ThemedRenderer(_theme), ShowCheckMargin = true, ShowImageMargin = false };
        popup.BackColor = _theme.MenuBack;
        popup.ForeColor = _theme.MenuFore;

        foreach (var language in _languages)
        {
            var item = new ToolStripMenuItem(language.DisplayName)
            {
                Checked = string.Equals(language.Key, current, StringComparison.OrdinalIgnoreCase),
                ForeColor = _theme.MenuFore,
            };
            var key = language.Key;
            item.Click += (_, _) =>
            {
                SetLanguage(key);
                BeginInvoke(new Action(RefreshSettingsPage));
            };
            popup.Items.Add(item);
        }

        _syntaxPopup = popup; // disposed when the next popup is created (never from its own Closed event)
        popup.Show(anchor, new System.Drawing.Point(0, anchor.Height));
    }

    private void BrowseScrcpy()
    {
        using var dialog = new OpenFileDialog
        {
            Title = Loc.T("Locate scrcpy.exe"),
            Filter = "scrcpy|scrcpy.exe|Programs|*.exe",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _scrcpyPath = dialog.FileName;
        _device.ScrcpyPath = _scrcpyPath;
        SaveSessionState();
    }
}

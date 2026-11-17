using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace KekyNotepad.Services;

/// <summary>
/// Persistent crash recovery and session storage.
///
/// Layout:
///   %APPDATA%\KekyNotepad\session.json
///   %APPDATA%\KekyNotepad\tabs\&lt;tab-id&gt;.txt
///
/// One fixed recovery file per tab is always overwritten, never appended, so it
/// cannot grow into a pile of temporary files. The file is deleted only after a
/// successful manual Save/Save As, or when the user explicitly closes its tab.
/// </summary>
public sealed class SessionStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly string _rootDirectory;
    private readonly string _tabsDirectory;

    public SessionStore()
    {
        // KEKYNOTEPAD_DATA points the store somewhere else (used for testing and for
        // running the editor from a USB stick); the normal location is %APPDATA%.
        var overrideDirectory = Environment.GetEnvironmentVariable("KEKYNOTEPAD_DATA");
        _rootDirectory = !string.IsNullOrWhiteSpace(overrideDirectory)
            ? overrideDirectory
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KekyNotepad");
        _tabsDirectory = Path.Combine(_rootDirectory, "tabs");

        try
        {
            Directory.CreateDirectory(_tabsDirectory);
        }
        catch
        {
            // If AppData is unavailable, writes are silently skipped; the editor
            // still works normally for the current run.
        }
    }

    public string RootDirectory => _rootDirectory;
    public string MetadataPath => Path.Combine(_rootDirectory, "session.json");

    public SessionState LoadSession()
    {
        try
        {
            if (!File.Exists(MetadataPath))
            {
                return new SessionState();
            }

            var json = File.ReadAllText(MetadataPath, Encoding.UTF8);
            var state = JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
            if (state is null)
            {
                return new SessionState();
            }

            state.Tabs ??= new();
            return state;
        }
        catch
        {
            return new SessionState();
        }
    }

    public void SaveSession(SessionState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);
            WriteAtomic(MetadataPath, json);
        }
        catch
        {
            // Never let a background save failure interrupt editing.
        }
    }

    public string SettingsPath => Path.Combine(_rootDirectory, "settings.json");

    /// <summary>Returns the saved preferences, or null when none were saved yet.</summary>
    public UserSettings? LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<UserSettings>(
                File.ReadAllText(SettingsPath, Encoding.UTF8),
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void SaveSettings(UserSettings settings)
    {
        try
        {
            WriteAtomic(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Preferences are best effort.
        }
    }

    public string? ReadRecovery(string tabId)
    {
        try
        {
            var path = RecoveryPath(tabId);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch
        {
            return null;
        }
    }

    public void WriteRecovery(string tabId, string content)
    {
        try
        {
            WriteAtomic(RecoveryPath(tabId), content ?? string.Empty);
        }
        catch
        {
            // Recovery is best effort.
        }
    }

    public void DeleteRecovery(string tabId)
    {
        try
        {
            var path = RecoveryPath(tabId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A locked file will be overwritten next time the tab is edited.
        }
    }

    private string RecoveryPath(string tabId)
    {
        var safeId = new string((tabId ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        if (safeId.Length == 0)
        {
            safeId = "unknown";
        }

        return Path.Combine(_tabsDirectory, safeId + ".txt");
    }

    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";

        try
        {
            File.WriteAllText(temporaryPath, content, Utf8NoBom);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }
}

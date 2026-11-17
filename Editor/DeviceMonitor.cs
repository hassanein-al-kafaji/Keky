using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KekyNotepad.Editor;

/// <summary>One phone reported by <c>adb devices -l</c>.</summary>
internal sealed record AdbDevice(string Serial, string Name);

/// <summary>
/// Polls adb every few seconds and reports the connected (authorized) devices, so the
/// panel can start and stop a phone view automatically when a cable is plugged in or out.
/// </summary>
internal sealed class DeviceMonitor : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2500 };
    private readonly Control _owner;
    private string? _adb;
    private bool _busy;
    private string _lastSignature = "\0";

    public DeviceMonitor(Control owner)
    {
        _owner = owner;
        _timer.Tick += async (_, _) => await PollAsync();
    }

    /// <summary>Raised on the UI thread when the device list changes.</summary>
    public event EventHandler<IReadOnlyList<AdbDevice>>? DevicesChanged;

    /// <summary>False when adb.exe could not be found (single-device fallback is used).</summary>
    public bool HasAdb => _adb is not null;

    public string? AdbPath => _adb;

    public void Start(string scrcpyPath)
    {
        _adb = FindAdb(scrcpyPath);
        _lastSignature = "\0";
        if (_adb is null)
        {
            return;
        }

        _timer.Start();
        _ = PollAsync();
    }

    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }

    private static string? FindAdb(string scrcpyPath)
    {
        var beside = Path.Combine(Path.GetDirectoryName(scrcpyPath) ?? string.Empty, "adb.exe");
        if (File.Exists(beside))
        {
            return beside;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "adb.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry.
            }
        }

        return null;
    }

    private async Task PollAsync()
    {
        if (_busy || _adb is null || _owner.IsDisposed)
        {
            return;
        }

        _busy = true;
        try
        {
            var adb = _adb;
            var devices = await Task.Run(() => Query(adb));
            if (devices is null || _owner.IsDisposed)
            {
                return;
            }

            var signature = string.Join("|", devices.ConvertAll(d => d.Serial + "=" + d.Name));
            if (signature != _lastSignature)
            {
                _lastSignature = signature;
                DevicesChanged?.Invoke(this, devices);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private static List<AdbDevice>? Query(string adb)
    {
        try
        {
            var info = new ProcessStartInfo(adb, "devices -l")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(6000))
            {
                process.Kill();
                return null;
            }

            var result = new List<AdbDevice>();
            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("List of", StringComparison.Ordinal) || line.StartsWith('*'))
                {
                    continue;
                }

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[1] != "device")
                {
                    continue; // offline / unauthorized
                }

                string? model = null;
                string? product = null;
                foreach (var part in parts)
                {
                    if (part.StartsWith("model:", StringComparison.Ordinal))
                    {
                        model = part[6..].Replace('_', ' ');
                    }
                    else if (part.StartsWith("product:", StringComparison.Ordinal))
                    {
                        product = part[8..];
                    }
                }

                result.Add(new AdbDevice(parts[0], model ?? product ?? parts[0]));
            }

            return result;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}

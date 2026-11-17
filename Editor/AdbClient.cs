using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace KekyNotepad.Editor;

/// <summary>Small wrapper around adb.exe for one device. All calls are async and never throw.</summary>
internal sealed class AdbClient
{
    private readonly string _adb;
    private readonly string? _serial;

    public AdbClient(string adb, string? serial)
    {
        _adb = adb;
        _serial = serial;
    }

    public string AdbPath => _adb;

    public string? Serial => _serial;

    /// <summary>"-s serial" prefix for use in a shell command line.</summary>
    public string TargetArguments => string.IsNullOrEmpty(_serial) ? string.Empty : $"-s {Quote(_serial)} ";

    public Task<(bool Ok, string Output)> RunAsync(params string[] arguments) =>
        Task.Run(() => Run(arguments));

    public Task<(bool Ok, string Output)> ShellAsync(string command) =>
        RunAsync("shell", command);

    public Task KeyAsync(int keyCode) => ShellAsync($"input keyevent {keyCode}");

    /// <summary>Takes a screenshot and returns the PNG bytes (null on failure).</summary>
    public Task<byte[]?> ScreenshotAsync() => Task.Run(() =>
    {
        try
        {
            using var process = Process.Start(Build(new[] { "exec-out", "screencap", "-p" }));
            if (process is null)
            {
                return null;
            }

            using var memory = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(memory);
            if (!process.WaitForExit(10000))
            {
                process.Kill();
                return null;
            }

            var bytes = memory.ToArray();
            return bytes.Length > 8 && bytes[1] == (byte)'P' ? bytes : null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    });

    /// <summary>Phone's Wi-Fi IPv4 address, or null.</summary>
    public async Task<string?> WifiAddressAsync()
    {
        var (ok, output) = await ShellAsync("ip route");
        if (!ok)
        {
            return null;
        }

        foreach (var line in output.Split('\n'))
        {
            var marker = line.IndexOf(" src ", StringComparison.Ordinal);
            if (marker >= 0 && line.Contains("wlan", StringComparison.Ordinal))
            {
                var rest = line[(marker + 5)..].Trim();
                var end = rest.IndexOf(' ');
                return end > 0 ? rest[..end] : rest;
            }
        }

        return null;
    }

    /// <summary>Starts adb and leaves it running (screen recording). The caller owns the process.</summary>
    public Process? StartLongRunning(params string[] arguments)
    {
        try
        {
            var process = Process.Start(Build(arguments));
            if (process is not null)
            {
                ChildProcessJob.Add(process);
            }

            return process;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private (bool Ok, string Output) Run(string[] arguments)
    {
        try
        {
            using var process = Process.Start(Build(arguments));
            if (process is null)
            {
                return (false, string.Empty);
            }

            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(60000))
            {
                process.Kill();
                return (false, "adb timed out");
            }

            var text = (output + errors.GetAwaiter().GetResult()).Trim();
            return (process.ExitCode == 0, text);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return (false, exception.Message);
        }
    }

    private ProcessStartInfo Build(string[] arguments)
    {
        var info = new ProcessStartInfo(_adb)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        if (!string.IsNullOrEmpty(_serial))
        {
            info.ArgumentList.Add("-s");
            info.ArgumentList.Add(_serial);
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}

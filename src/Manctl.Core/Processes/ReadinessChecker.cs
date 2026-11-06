using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Manctl.Core.Android;
using Manctl.Core.Win32;

namespace Manctl.Core.Processes;

public sealed record ReadinessResult(bool Ready, string? Signal);

/// <summary>Determines when a launched process is actually usable, not just started — replaces the "sleep and hope"
/// pattern that caused flaky agent runs against slow-booting emulators.</summary>
public static class ReadinessChecker
{
    public static async Task<ReadinessResult> WaitAsync(Process process, string waitForType, string? waitForValue, int timeoutMs)
    {
        return waitForType.ToLowerInvariant() switch
        {
            "none" or "" => new ReadinessResult(true, "none"),
            "windowtitle" => await WaitForWindowAsync(process.Id, waitForValue, timeoutMs),
            "port" => await WaitForPortAsync(waitForValue, timeoutMs),
            "stdoutpattern" => await WaitForStdoutPatternAsync(process, waitForValue, timeoutMs),
            "androidboot" => await WaitForAndroidBootAsync(process, waitForValue, timeoutMs),
            _ => new ReadinessResult(false, $"unknown waitForType '{waitForType}'"),
        };
    }

    private static async Task<ReadinessResult> WaitForWindowAsync(int pid, string? titleContains, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var match = WindowManager.ListWindows(onlyVisibleWithTitle: false)
                .FirstOrDefault(w => w.ProcessId == pid && w.IsVisible &&
                    (string.IsNullOrEmpty(titleContains) || w.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase)));

            if (match is not null)
                return new ReadinessResult(true, $"window '{match.Title}' (handle {match.Handle})");

            await Task.Delay(150);
        }
        return new ReadinessResult(false, $"no window matching '{titleContains}' appeared for pid {pid} within {timeoutMs / 1000.0:0.##} seconds. Some programs open their window from another process: use wait_for_window(windowTitle=...) instead.");
    }

    private static async Task<ReadinessResult> WaitForPortAsync(string? hostPort, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(hostPort))
            return new ReadinessResult(false, "waitForValue must be \"port\" or \"host:port\"");

        var (host, port) = ParseHostPort(hostPort);
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(500));
                if (completed == connectTask && client.Connected)
                    return new ReadinessResult(true, $"{host}:{port} accepting connections");
            }
            catch { /* not up yet */ }

            await Task.Delay(200);
        }
        return new ReadinessResult(false, $"{host}:{port} did not accept connections within {timeoutMs}ms");
    }

    private static async Task<ReadinessResult> WaitForStdoutPatternAsync(Process process, string? pattern, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return new ReadinessResult(false, "waitForValue must be a regex pattern");

        if (!process.StartInfo.RedirectStandardOutput)
            return new ReadinessResult(false, "process was not started with stdout redirected; cannot watch for a stdout pattern");

        var regex = new Regex(pattern);
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cts.Token);
                if (line is null)
                    break;
                if (regex.IsMatch(line))
                    return new ReadinessResult(true, $"stdout matched: {line}");
            }
        }
        catch (OperationCanceledException) { /* timed out */ }

        return new ReadinessResult(false, $"pattern '{pattern}' not seen in stdout within {timeoutMs}ms");
    }

    /// <summary>A window appears well before the Android OS has actually finished booting — this polls the real
    /// signal (sys.boot_completed) instead. Assumes the emulator's serial is known in advance (the first instance
    /// launched is conventionally "emulator-5554"); pass a different serial via waitForValue if needed.</summary>
    private static async Task<ReadinessResult> WaitForAndroidBootAsync(Process process, string? waitForValue, int timeoutMs)
    {
        var deviceSerial = string.IsNullOrWhiteSpace(waitForValue) ? "emulator-5554" : waitForValue;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            // The launcher process dying (bad AVD config, missing system image, etc.) means it will never boot —
            // bail out immediately instead of sitting out the rest of the timeout waiting for nothing.
            if (process.HasExited)
            {
                return new ReadinessResult(false,
                    $"emulator process exited (code {SafeExitCode(process)}) before {deviceSerial} reported boot_completed=1");
            }

            var devices = await AdbClient.ListDevicesAsync();
            if (devices.Any(d => d.Serial == deviceSerial && d.State == "device"))
            {
                var boot = await AdbClient.ShellAsync("getprop sys.boot_completed", deviceSerial, timeoutMs: 5000);
                if (boot.Success && boot.StdOut.Trim() == "1")
                    return new ReadinessResult(true, $"{deviceSerial} boot_completed=1");
            }

            await Task.Delay(2000);
        }
        return new ReadinessResult(false, $"{deviceSerial} did not report boot_completed=1 within {timeoutMs}ms");
    }

    private static int? SafeExitCode(Process process)
    {
        try { return process.ExitCode; } catch { return null; }
    }

    private static (string host, int port) ParseHostPort(string spec)
    {
        var idx = spec.IndexOf(':');
        return idx < 0
            ? ("127.0.0.1", int.Parse(spec))
            : (spec[..idx], int.Parse(spec[(idx + 1)..]));
    }
}

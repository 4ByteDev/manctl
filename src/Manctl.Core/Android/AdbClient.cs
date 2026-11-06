using System.Diagnostics;
using System.Text;

namespace Manctl.Core.Android;

public sealed record AdbResult(bool Success, string StdOut, string StdErr, int ExitCode);
public sealed record AdbBytesResult(bool Success, byte[] StdOutBytes, string StdErr, int ExitCode);
public sealed record AndroidDevice(string Serial, string State, string? Model);

/// <summary>Thin wrapper over the `adb` CLI (must be on PATH). Android's own accessibility dump (uiautomator)
/// plays the same role here that UIA plays for native Windows apps — same tree-shaped output further downstream.</summary>
public static class AdbClient
{
    public static Task<AdbResult> RunAsync(string arguments, string? deviceSerial = null, int timeoutMs = 15000) =>
        RunRawAsync(BuildArgs(arguments, deviceSerial), timeoutMs);

    public static Task<AdbResult> ShellAsync(string shellCommand, string? deviceSerial = null, int timeoutMs = 15000) =>
        RunRawAsync(BuildArgs($"shell {shellCommand}", deviceSerial), timeoutMs);

    /// <summary>`adb exec-out` streams the command's stdout back directly — used to grab the uiautomator XML
    /// dump without writing/pulling/deleting a temp file on the device.</summary>
    public static Task<AdbResult> ExecOutAsync(string shellCommand, string? deviceSerial = null, int timeoutMs = 15000) =>
        RunRawAsync(BuildArgs($"exec-out {shellCommand}", deviceSerial), timeoutMs);

    /// <summary>Binary-safe variant of ExecOutAsync — reads stdout as raw bytes via the pipe's BaseStream instead
    /// of a text StreamReader, so PNG/binary payloads (e.g. `screencap -p`) aren't corrupted by UTF-8 decoding.</summary>
    public static async Task<AdbBytesResult> ExecOutBytesAsync(string shellCommand, string? deviceSerial = null, int timeoutMs = 20000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "adb",
            Arguments = BuildArgs($"exec-out {shellCommand}", deviceSerial),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            return new AdbBytesResult(false, [], $"Failed to start adb (is it on PATH?): {ex.Message}", -1);
        }

        var stdoutTask = ReadAllBytesAsync(process.StandardOutput.BaseStream);
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return new AdbBytesResult(false, [], $"adb exec-out {shellCommand} timed out after {timeoutMs}ms", -1);
        }

        return new AdbBytesResult(process.ExitCode == 0, await stdoutTask, await stderrTask, process.ExitCode);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    public static async Task<List<AndroidDevice>> ListDevicesAsync()
    {
        var result = await RunAsync("devices -l");
        var devices = new List<AndroidDevice>();
        if (!result.Success)
            return devices;

        foreach (var rawLine in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.Ordinal))
                continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            var model = parts.FirstOrDefault(p => p.StartsWith("model:", StringComparison.Ordinal))?["model:".Length..];
            devices.Add(new AndroidDevice(parts[0], parts[1], model));
        }
        return devices;
    }

    private static string BuildArgs(string args, string? deviceSerial) =>
        string.IsNullOrEmpty(deviceSerial) ? args : $"-s {deviceSerial} {args}";

    private static async Task<AdbResult> RunRawAsync(string arguments, int timeoutMs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "adb",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            return new AdbResult(false, "", $"Failed to start adb (is it on PATH?): {ex.Message}", -1);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return new AdbResult(false, "", $"adb {arguments} timed out after {timeoutMs}ms", -1);
        }

        return new AdbResult(process.ExitCode == 0, await stdoutTask, await stderrTask, process.ExitCode);
    }
}

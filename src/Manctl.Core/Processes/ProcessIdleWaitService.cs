using System.Diagnostics;
using Manctl.Core.Models;

namespace Manctl.Core.Processes;

public sealed record ProcessIdleResult(string Status, int? ExitCode, double FinalCpuSeconds, double ElapsedSeconds, string? ErrorCode = null);

/// <summary>Blocks until a process exits, or its CPU time stops growing for a sustained window ("finished its
/// active work, or hung — the caller decides which"), or the timeout elapses. Replaces polling run_command's
/// CPU usage in a loop from the agent side.</summary>
public static class ProcessIdleWaitService
{
    public const int MaxTimeoutSeconds = 1800;

    // TotalProcessorTime resolution is coarser than wall-clock; without an epsilon, scheduler jitter alone would
    // keep resetting the idle timer and "idle" would never trigger.
    private const double CpuEpsilonSeconds = 0.05;

    public static async Task<ProcessIdleResult> WaitAsync(int? processId, string? processName, int idleThresholdSeconds, int timeoutSeconds, int checkIntervalMs)
    {
        timeoutSeconds = Math.Clamp(timeoutSeconds, 1, MaxTimeoutSeconds);
        checkIntervalMs = Math.Clamp(checkIntervalMs, 200, 15000);
        idleThresholdSeconds = Math.Max(1, idleThresholdSeconds);

        var process = ResolveProcess(processId, processName);
        if (process is null)
            return new ProcessIdleResult("not_found", null, 0, 0, ErrorCode: ErrorCodes.ProcessNotFound);

        var sw = Stopwatch.StartNew();
        var deadline = TimeSpan.FromSeconds(timeoutSeconds);
        var lastCpu = SafeCpuSeconds(process);
        var lastChangeAt = sw.Elapsed;

        while (sw.Elapsed < deadline)
        {
            if (HasExited(process))
                return new ProcessIdleResult("exited", SafeExitCode(process), lastCpu ?? 0, sw.Elapsed.TotalSeconds);

            var currentCpu = SafeCpuSeconds(process);
            if (currentCpu is not null)
            {
                if (lastCpu is null || currentCpu.Value > lastCpu.Value + CpuEpsilonSeconds)
                {
                    lastCpu = currentCpu;
                    lastChangeAt = sw.Elapsed;
                }
            }

            if ((sw.Elapsed - lastChangeAt).TotalSeconds >= idleThresholdSeconds)
                return new ProcessIdleResult("idle", null, lastCpu ?? 0, sw.Elapsed.TotalSeconds);

            await Task.Delay(checkIntervalMs);
        }

        return new ProcessIdleResult("timeout", null, lastCpu ?? 0, sw.Elapsed.TotalSeconds, ErrorCode: ErrorCodes.Timeout);
    }

    private static Process? ResolveProcess(int? processId, string? processName)
    {
        try
        {
            if (processId is { } pid)
                return Process.GetProcessById(pid);

            if (!string.IsNullOrWhiteSpace(processName))
            {
                var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;
                return Process.GetProcessesByName(name).FirstOrDefault();
            }
        }
        catch
        {
            // fall through to not_found
        }
        return null;
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch { return true; }
    }

    private static double? SafeCpuSeconds(Process process)
    {
        try { return process.TotalProcessorTime.TotalSeconds; }
        catch { return null; }
    }

    private static int? SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch { return null; }
    }
}

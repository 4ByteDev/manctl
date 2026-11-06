using System.ComponentModel;
using System.Diagnostics;
using Manctl.Core.Models;

namespace Manctl.Core.Processes;

/// <summary>Starts/stops helper processes (emulators, test environments) launched into the same interactive
/// session as manctl itself — so their windows actually appear on the user's real desktop — and makes sure a
/// killed process never leaves a stale lock file behind to break the next launch.</summary>
public sealed class ProcessManager
{
    private readonly ProcessStateStore _store = new();

    public async Task<ManagedProcessInfo> StartAsync(
        string profileName,
        string executable,
        string? arguments,
        string? workingDirectory,
        string waitForType,
        string? waitForValue,
        int waitForTimeoutMs,
        string[] lockFilePaths,
        bool restartIfRunning,
        bool runAsAdmin = false)
    {
        var existing = _store.Get(profileName);
        if (existing is not null)
        {
            if (IsAlive(existing.Pid, existing.Executable))
            {
                if (!restartIfRunning)
                {
                    return new ManagedProcessInfo(profileName, existing.Pid, existing.Executable, true, true, "already running (pass restartIfRunning=true to relaunch it)", existing.StartedAtUtc);
                }

                await StopAsync(profileName, pid: null, graceful: true, timeoutMs: 5000);
            }
            else
            {
                // The recorded process is gone (crashed, force-killed outside our API, or PID reused) — this is
                // exactly the case that used to leave stale lock files breaking the next manual launch.
                CleanupLockFiles(existing.LockFilePaths);
                _store.Remove(profileName);
            }
        }

        var needsStdoutRedirect = string.Equals(waitForType, "stdoutPattern", StringComparison.OrdinalIgnoreCase);
        if (runAsAdmin && needsStdoutRedirect)
        {
            return new ManagedProcessInfo(profileName, 0, executable, false, false, null, DateTimeOffset.UtcNow,
                Error: "runAsAdmin requires UseShellExecute, which is incompatible with waitForType=\"stdoutPattern\" (stdout can't be redirected). Use a different readiness type.",
                ErrorCode: ErrorCodes.InvalidArgument);
        }

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? string.Empty : workingDirectory,
            UseShellExecute = runAsAdmin || !needsStdoutRedirect,
            RedirectStandardOutput = needsStdoutRedirect,
            Verb = runAsAdmin ? "runas" : string.Empty,
        };

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            return new ManagedProcessInfo(profileName, 0, executable, false, false, null, DateTimeOffset.UtcNow,
                Error: "The user declined the UAC elevation prompt.", ErrorCode: ErrorCodes.AccessDenied);
        }
        catch (Exception ex)
        {
            var code = ex switch
            {
                Win32Exception { NativeErrorCode: 2 or 3 } => ErrorCodes.FileNotFound, // ERROR_FILE_NOT_FOUND / ERROR_PATH_NOT_FOUND
                Win32Exception { NativeErrorCode: 5 } => ErrorCodes.AccessDenied, // ERROR_ACCESS_DENIED
                _ => ErrorCodes.Unknown,
            };
            return new ManagedProcessInfo(profileName, 0, executable, false, false, null, DateTimeOffset.UtcNow, Error: $"Failed to start: {ex.Message}", ErrorCode: code);
        }

        _store.Upsert(new ProcessRecord(profileName, process.Id, executable, arguments, lockFilePaths, DateTimeOffset.UtcNow));

        var readiness = await ReadinessChecker.WaitAsync(process, waitForType, waitForValue, waitForTimeoutMs);
        if (string.IsNullOrEmpty(waitForType) || waitForType.Equals("none", StringComparison.OrdinalIgnoreCase))
            await Task.Run(() => { try { process.WaitForExit(400); } catch { /* not ours to wait on (elevated launcher) */ } });
        var alive = IsAlive(process.Id, executable);

        string? error = readiness.Ready ? null : readiness.Signal;
        string? errorCode = readiness.Ready ? null : ErrorCodes.Timeout;
        var signal = readiness.Signal;

        if (readiness.Ready && !alive)
        {
            // "Started" is only a success if it did not die on arrival.
            int? exit = null;
            try { if (process.HasExited) exit = process.ExitCode; } catch { /* no access to a launcher/elevated process */ }
            if (exit is not null and not 0)
            {
                error = $"The program started but exited at once with code {exit}.";
                errorCode = ErrorCodes.Unknown;
            }
            else
            {
                signal += " (the process has already finished with exit code 0 — it may have handed over to another process; use wait_for_window or list_windows to find what it opened)";
            }
        }

        return new ManagedProcessInfo(
            profileName, process.Id, executable,
            IsAlive: alive,
            Ready: readiness.Ready && error is null,
            ReadySignal: signal,
            StartedAtUtc: DateTimeOffset.UtcNow,
            Error: error,
            ErrorCode: errorCode);
    }

    public Task<StopResult> StopAsync(string? profileName, int? pid, bool graceful, int timeoutMs)
    {
        var record = profileName is not null ? _store.Get(profileName) : null;
        var targetPid = pid ?? record?.Pid;

        if (targetPid is null)
            return Task.FromResult(new StopResult(false, 0, null, [], Error: "No pid given and no record found for that profile.", ErrorCode: ErrorCodes.InvalidArgument));

        Process? process;
        try
        {
            process = Process.GetProcessById(targetPid.Value);
            if (record is not null && !NameMatches(process, record.Executable))
            {
                // PID was recycled by an unrelated process — nothing of ours to kill, just clean up records.
                process = null;
            }
        }
        catch (ArgumentException)
        {
            process = null;
        }

        if (process is null)
        {
            var cleanedStale = record is not null ? CleanupLockFiles(record.LockFilePaths) : [];
            if (profileName is not null) _store.Remove(profileName);
            return Task.FromResult(new StopResult(true, targetPid.Value, null, cleanedStale, Error: "Process was already not running."));
        }

        int? exitCode;
        try
        {
            if (graceful)
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(timeoutMs))
                    process.Kill(entireProcessTree: true);
            }
            else
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit(5000);
            exitCode = SafeExitCode(process);
        }
        catch (Exception ex)
        {
            var code = ex is Win32Exception { NativeErrorCode: 5 } ? ErrorCodes.AccessDenied : ErrorCodes.Unknown;
            return Task.FromResult(new StopResult(false, targetPid.Value, null, [], Error: ex.Message, ErrorCode: code));
        }

        var lockFilesCleaned = record is not null ? CleanupLockFiles(record.LockFilePaths) : [];
        if (profileName is not null) _store.Remove(profileName);

        return Task.FromResult(new StopResult(true, targetPid.Value, exitCode, lockFilesCleaned));
    }

    public List<ManagedProcessInfo> ListManaged() =>
        [.. _store.GetAll().Select(r =>
        {
            var alive = IsAlive(r.Pid, r.Executable);
            return new ManagedProcessInfo(r.ProfileName, r.Pid, r.Executable, alive, alive, alive ? "running" : "not running", r.StartedAtUtc);
        })];

    private static bool IsAlive(int pid, string executable)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && NameMatches(process, executable);
        }
        catch
        {
            return false;
        }
    }

    private static bool NameMatches(Process process, string executable)
    {
        var expectedName = Path.GetFileNameWithoutExtension(executable);
        return string.IsNullOrEmpty(expectedName) || string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] CleanupLockFiles(string[] lockFilePaths)
    {
        var cleaned = new List<string>();
        foreach (var path in lockFilePaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    cleaned.Add(path);
                }
            }
            catch { /* best effort — don't fail the whole stop over a locked/permission-denied lock file */ }
        }
        return [.. cleaned];
    }

    private static int? SafeExitCode(Process process)
    {
        try { return process.ExitCode; } catch { return null; }
    }
}

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Manctl.Core.Models;

namespace Manctl.Core.Commands;

/// <summary>One-shot/console commands (git status, npm test, ...) — distinct from ProcessManager, which is for
/// long-running window-having helpers with lock-file lifecycle. Output is captured asynchronously via
/// OutputDataReceived (never blocks on a full pipe buffer) and fetched incrementally by offset so polling a
/// long-running command doesn't re-send output the caller already has.</summary>
public sealed class CommandExecutionService
{
    private const int MaxBufferedChars = 500_000;

    private sealed class Session
    {
        public required Process Process;
        public required string Executable;
        public required DateTimeOffset StartedAt;
        public readonly StringBuilder StdOut = new();
        public readonly StringBuilder StdErr = new();
        public readonly object Gate = new();
        public bool StdOutTruncated;
        public bool StdErrTruncated;
    }

    // Long enough for headless analysis tools (Ghidra/IDA batch scripts, big builds) to run to completion inside
    // a single blocking call — no need to fall back to run_command_async + wait_for_file just to get a longer wait.
    public const int MaxTimeoutMs = 1_800_000;

    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public async Task<CommandResult> RunSyncAsync(string executable, string? arguments, string? workingDirectory, int timeoutMs)
    {
        timeoutMs = Math.Clamp(timeoutMs, 100, MaxTimeoutMs);

        var (session, error, errorCode) = StartCore(executable, arguments, workingDirectory);
        if (session is null)
            return new CommandResult(false, null, "", "", false, Error: error, ErrorCode: errorCode);

        var process = session.Process;

        using var cts = new CancellationTokenSource(timeoutMs);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            try { await process.WaitForExitAsync(CancellationToken.None); } catch { /* best effort */ }
        }

        var (stdout, stderr) = ReadSnapshot(session);
        var exitCode = SafeExitCode(process);
        process.Dispose();

        return new CommandResult(!timedOut && exitCode == 0, exitCode, stdout, stderr, timedOut,
            Error: timedOut ? $"Command timed out after {timeoutMs}ms and was killed." : null,
            ErrorCode: timedOut ? ErrorCodes.Timeout : null);
    }

    public CommandHandle Start(string executable, string? arguments, string? workingDirectory)
    {
        var (session, error, errorCode) = StartCore(executable, arguments, workingDirectory);
        if (session is null)
            return new CommandHandle("", 0, Error: error, ErrorCode: errorCode);

        var id = Guid.NewGuid().ToString("N");
        _sessions[id] = session;
        return new CommandHandle(id, session.Process.Id);
    }

    public CommandOutputSnapshot? GetOutput(string id, long stdoutOffset, long stderrOffset)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return null;

        string stdoutSlice, stderrSlice;
        long newStdoutOffset, newStderrOffset;
        bool truncatedOut, truncatedErr;
        lock (session.Gate)
        {
            stdoutSlice = SliceFrom(session.StdOut, stdoutOffset, out newStdoutOffset);
            stderrSlice = SliceFrom(session.StdErr, stderrOffset, out newStderrOffset);
            truncatedOut = session.StdOutTruncated;
            truncatedErr = session.StdErrTruncated;
        }

        var running = !session.Process.HasExited;
        return new CommandOutputSnapshot(id, running, running ? null : SafeExitCode(session.Process),
            stdoutSlice, stderrSlice, newStdoutOffset, newStderrOffset, truncatedOut, truncatedErr);
    }

    public CommandResult Stop(string id, bool graceful, int timeoutMs)
    {
        if (!_sessions.TryRemove(id, out var session))
            return new CommandResult(false, null, "", "", false, Error: "No such command id.", ErrorCode: ErrorCodes.InvalidArgument);

        try
        {
            if (!session.Process.HasExited)
            {
                if (graceful)
                {
                    session.Process.CloseMainWindow();
                    if (!session.Process.WaitForExit(timeoutMs))
                        session.Process.Kill(entireProcessTree: true);
                }
                else
                {
                    session.Process.Kill(entireProcessTree: true);
                }
                session.Process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            var code = ex is Win32Exception { NativeErrorCode: 5 } ? ErrorCodes.AccessDenied : ErrorCodes.Unknown;
            return new CommandResult(false, null, "", "", false, Error: ex.Message, ErrorCode: code);
        }

        var (stdout, stderr) = ReadSnapshot(session);
        return new CommandResult(true, SafeExitCode(session.Process), stdout, stderr, false);
    }

    public List<CommandInfo> List() =>
        [.. _sessions.Select(kv => new CommandInfo(
            kv.Key, kv.Value.Process.Id, kv.Value.Executable, !kv.Value.Process.HasExited, SafeExitCode(kv.Value.Process), kv.Value.StartedAt))];

    private static (Session? Session, string? Error, string? ErrorCode) StartCore(string executable, string? arguments, string? workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? string.Empty : workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var process = new Process { StartInfo = psi };
        var session = new Session { Process = process, Executable = executable, StartedAt = DateTimeOffset.UtcNow };

        process.OutputDataReceived += (_, e) => Append(session, isStdOut: true, e.Data);
        process.ErrorDataReceived += (_, e) => Append(session, isStdOut: false, e.Data);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            process.Dispose();
            var code = ex switch
            {
                Win32Exception { NativeErrorCode: 2 or 3 } => ErrorCodes.FileNotFound,
                Win32Exception { NativeErrorCode: 5 } => ErrorCodes.AccessDenied,
                _ => ErrorCodes.Unknown,
            };
            return (null, $"Failed to start '{executable}': {ex.Message}", code);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return (session, null, null);
    }

    private static void Append(Session session, bool isStdOut, string? line)
    {
        if (line is null)
            return;

        lock (session.Gate)
        {
            var sb = isStdOut ? session.StdOut : session.StdErr;
            if (sb.Length < MaxBufferedChars)
            {
                sb.Append(line).Append('\n');
            }
            else if (isStdOut)
            {
                session.StdOutTruncated = true;
            }
            else
            {
                session.StdErrTruncated = true;
            }
        }
    }

    private static (string stdout, string stderr) ReadSnapshot(Session session)
    {
        lock (session.Gate)
        {
            return (session.StdOut.ToString(), session.StdErr.ToString());
        }
    }

    private static string SliceFrom(StringBuilder sb, long offset, out long newOffset)
    {
        var total = sb.Length;
        if (offset >= total)
        {
            newOffset = total;
            return string.Empty;
        }

        var start = (int)Math.Max(0, offset);
        newOffset = total;
        return sb.ToString(start, total - start);
    }

    private static int? SafeExitCode(Process process)
    {
        try { return process.HasExited ? process.ExitCode : null; }
        catch { return null; }
    }
}

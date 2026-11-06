using System.Diagnostics;
using Manctl.Core.Models;

namespace Manctl.Core.Processes;

public sealed record WaitForFileResult(bool Matched, double ElapsedSeconds, long? FileSizeBytes, string? Error = null, string? ErrorCode = null, string? Hint = null);

/// <summary>Moves the "check if a file showed up / changed / logged something yet" poll loop onto the server —
/// an agent that used to re-send its whole context every 5 seconds to check now makes one blocking call.</summary>
public static class FileWaitService
{
    public const int MaxTimeoutSeconds = 1800;

    public static async Task<WaitForFileResult> WaitAsync(string path, string mode, string? matchText, int timeoutSeconds, int pollIntervalMs)
    {
        timeoutSeconds = Math.Clamp(timeoutSeconds, 1, MaxTimeoutSeconds);
        pollIntervalMs = Math.Clamp(pollIntervalMs, 100, 10000);

        var normalizedMode = mode.ToLowerInvariant();
        if (normalizedMode is not ("exists" or "modified" or "contains_text" or "deleted"))
            return new WaitForFileResult(false, 0, null, Error: $"Unknown mode '{mode}'. Use \"exists\", \"modified\", \"contains_text\" or \"deleted\".", ErrorCode: ErrorCodes.InvalidArgument);

        if (normalizedMode == "contains_text" && string.IsNullOrEmpty(matchText))
            return new WaitForFileResult(false, 0, null, Error: "matchText is required for mode=\"contains_text\".", ErrorCode: ErrorCodes.InvalidArgument);

        var sw = Stopwatch.StartNew();
        var deadline = TimeSpan.FromSeconds(timeoutSeconds);

        // Baseline mtime for "modified" — only meaningful if the file already exists at call time; if it doesn't,
        // its first appearance already counts as "modified" relative to "didn't exist a moment ago".
        var existedAtStart = Exists(path);
        DateTime? baselineMtime = normalizedMode == "modified" && File.Exists(path) ? SafeMtime(path) : null;

        while (sw.Elapsed < deadline)
        {
            switch (normalizedMode)
            {
                case "exists":
                    if (Exists(path))
                        return Done(true, sw, path);
                    break;

                case "modified":
                    if (File.Exists(path))
                    {
                        var mtime = SafeMtime(path);
                        if (baselineMtime is null)
                            return Done(true, sw, path); // appeared after the call started
                        if (mtime is { } m && m > baselineMtime.Value)
                            return Done(true, sw, path);
                    }
                    break;

                case "contains_text":
                    if (File.Exists(path) && await ContainsTextAsync(path, matchText!))
                        return Done(true, sw, path);
                    break;

                case "deleted":
                    if (!Exists(path))
                        return Done(true, sw, path, existedAtStart ? null : "The path did not exist when you called, so it counts as deleted at once. If you expected it to exist, check the path.");
                    break;
            }

            await Task.Delay(pollIntervalMs);
        }

        return new WaitForFileResult(false, sw.Elapsed.TotalSeconds, SafeSize(path), ErrorCode: ErrorCodes.Timeout);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static WaitForFileResult Done(bool matched, Stopwatch sw, string path, string? hint = null) =>
        new(matched, sw.Elapsed.TotalSeconds, SafeSize(path), Hint: hint);

    private static async Task<bool> ContainsTextAsync(string path, string matchText)
    {
        try
        {
            // FileShare.ReadWrite so a log file still being appended to by another process doesn't fail with a
            // sharing violation.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            return content.Contains(matchText, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static DateTime? SafeMtime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); } catch { return null; }
    }

    private static long? SafeSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return null; }
    }
}

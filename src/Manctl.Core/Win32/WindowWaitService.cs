using System.Diagnostics;
using Manctl.Core.Models;

namespace Manctl.Core.Win32;

/// <summary>Window-level analogue of ElementWaitService — polls for a whole window appearing or a specific,
/// already-identified window closing, instead of the agent looping list_windows itself.</summary>
public static class WindowWaitService
{
    public const int MaxTimeoutSeconds = 1800;

    public static async Task<WindowWaitResult> WaitAsync(string? titleContains, string? className, string mode, int timeoutSeconds, int pollIntervalMs)
    {
        if (string.IsNullOrEmpty(titleContains) && string.IsNullOrEmpty(className))
            return new WindowWaitResult(false, 0, null, null, Error: "At least one of titleContains/className is required.", ErrorCode: ErrorCodes.InvalidArgument);

        var normalizedMode = mode.ToLowerInvariant();
        if (normalizedMode is not ("appears" or "closes"))
            return new WindowWaitResult(false, 0, null, null, Error: $"Unknown mode '{mode}'. Use \"appears\" or \"closes\".", ErrorCode: ErrorCodes.InvalidArgument);

        timeoutSeconds = Math.Clamp(timeoutSeconds, 1, MaxTimeoutSeconds);
        pollIntervalMs = Math.Clamp(pollIntervalMs, 100, 5000);

        var sw = Stopwatch.StartNew();
        var deadline = TimeSpan.FromSeconds(timeoutSeconds);

        if (normalizedMode == "appears")
        {
            while (sw.Elapsed < deadline)
            {
                var match = FindMatch(titleContains, className);
                if (match is not null)
                    return new WindowWaitResult(true, sw.Elapsed.TotalSeconds, match.Handle, match.Title);

                await Task.Delay(pollIntervalMs);
            }
            return new WindowWaitResult(false, sw.Elapsed.TotalSeconds, null, null, ErrorCode: ErrorCodes.Timeout);
        }

        // "closes" tracks the specific window handle first matched, not just "does anything still match the
        // filter" — a new window with a similar title opening while the original is still up (or closing right
        // as another one appears) would otherwise give a false read.
        var initial = FindMatch(titleContains, className);
        if (initial is null)
            return new WindowWaitResult(true, 0, null, null,
                Hint: "No window matching that title/class existed when you called, so there was nothing to wait for. If you expected one, the title may be wrong: check list_windows.");

        while (sw.Elapsed < deadline)
        {
            var stillOpen = WindowManager.ListWindows(onlyVisibleWithTitle: false).Any(w => w.Handle == initial.Handle);
            if (!stillOpen)
                return new WindowWaitResult(true, sw.Elapsed.TotalSeconds, initial.Handle, initial.Title);

            await Task.Delay(pollIntervalMs);
        }
        return new WindowWaitResult(false, sw.Elapsed.TotalSeconds, initial.Handle, initial.Title, ErrorCode: ErrorCodes.Timeout);
    }

    private static WindowInfo? FindMatch(string? titleContains, string? className) =>
        WindowManager.ListWindows(onlyVisibleWithTitle: false).FirstOrDefault(w => w.IsVisible &&
            (string.IsNullOrEmpty(titleContains) || w.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(className) || string.Equals(w.ClassName, className, StringComparison.OrdinalIgnoreCase)));
}

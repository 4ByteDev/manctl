using Manctl.Core.Models;

namespace Manctl.Core.Android;

public sealed record TransferResult(bool Ok, string? ErrorCode = null, string? Error = null, string? Hint = null, string? LocalPath = null, string? DevicePath = null, long? Bytes = null);

public sealed record LogcatResult(bool Ok, string? ErrorCode = null, string? Error = null, string? Hint = null, int? Lines = null, string? Text = null, bool? Truncated = null);

public sealed record AndroidMatch(string Text, string Role, int X, int Y);

public sealed record AndroidFindResult(bool Ok, string? ErrorCode = null, string? Error = null, string? Hint = null, bool? Found = null, int? MatchCount = null, List<AndroidMatch>? Matches = null);

/// <summary>File transfer, log reading and text search on an Android device through adb.</summary>
public static class AndroidFileService
{
    private const int MaxLogChars = 30_000;

    public static async Task<TransferResult> PushAsync(string localPath, string devicePath, string? deviceSerial)
    {
        if (!File.Exists(localPath) && !Directory.Exists(localPath))
            return new TransferResult(false, ErrorCodes.FileNotFound, $"'{localPath}' does not exist on this PC.", "Give the full path of an existing file or folder.", localPath, devicePath);

        var r = await AdbClient.RunAsync($"push {Quote(localPath)} {Quote(devicePath)}", deviceSerial, timeoutMs: 300_000);
        return r.Success
            ? new TransferResult(true, LocalPath: localPath, DevicePath: devicePath, Bytes: File.Exists(localPath) ? new FileInfo(localPath).Length : null)
            : Failed(r, localPath, devicePath, "push");
    }

    /// <param name="app">Package name: read a private file of a debuggable app through run-as (the folder /data/data/&lt;app&gt;/ is not readable otherwise).</param>
    public static async Task<TransferResult> PullAsync(string devicePath, string localPath, string? deviceSerial, string? app)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(localPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            return new TransferResult(false, ErrorCodes.AccessDenied, $"Cannot create the folder for '{localPath}': {ex.Message}", null, localPath, devicePath);
        }

        if (!string.IsNullOrWhiteSpace(app))
        {
            var cat = await AdbClient.ExecOutBytesAsync($"run-as {app} cat {Quote(devicePath)}", deviceSerial, timeoutMs: 120_000);
            if (!cat.Success)
                return new TransferResult(false, ErrorCodes.Unknown, $"run-as {app} could not read '{devicePath}': {cat.StdErr.Trim()}",
                    "run-as works only for debuggable apps and for paths inside the app's own folder (/data/data/<app>/...). Check the package name and the path.", localPath, devicePath);
            await File.WriteAllBytesAsync(localPath, cat.StdOutBytes);
            return new TransferResult(true, LocalPath: localPath, DevicePath: devicePath, Bytes: cat.StdOutBytes.LongLength);
        }

        var r = await AdbClient.RunAsync($"pull {Quote(devicePath)} {Quote(localPath)}", deviceSerial, timeoutMs: 300_000);
        if (!r.Success) return Failed(r, localPath, devicePath, "pull");
        return new TransferResult(true, LocalPath: localPath, DevicePath: devicePath, Bytes: File.Exists(localPath) ? new FileInfo(localPath).Length : null);
    }

    /// <param name="lines">How many of the latest lines.</param>
    /// <param name="level">Lowest level to show: V, D, I, W, E or F.</param>
    public static async Task<LogcatResult> LogcatAsync(int lines, string? filter, string? level, bool clear, string? deviceSerial)
    {
        if (clear)
        {
            var c = await AdbClient.RunAsync("logcat -c", deviceSerial);
            return c.Success
                ? new LogcatResult(true, Hint: "The log buffer was cleared. Repeat the action you want to see, then call android_logcat again.")
                : new LogcatResult(false, ErrorCodes.Unknown, AdbWhy(c), AdbHint);
        }

        lines = Math.Clamp(lines, 1, 5000);
        var spec = "";
        if (!string.IsNullOrWhiteSpace(level))
        {
            var l = level.Trim().ToUpperInvariant();
            if (l.Length != 1 || "VDIWEF".IndexOf(l[0]) < 0)
                return new LogcatResult(false, ErrorCodes.InvalidArgument, $"Unknown level '{level}'.", "level is one of V, D, I, W, E, F (the lowest level to show; E = errors and worse).");
            spec = $" *:{l}";
        }

        // Without a text filter adb itself returns exactly the last N lines. With one, read a bigger window and filter.
        var asked = string.IsNullOrEmpty(filter) ? lines : Math.Min(lines * 20, 20000);
        var r = await AdbClient.RunAsync($"logcat -d -v threadtime -t {asked}{spec}", deviceSerial, timeoutMs: 30_000);
        if (!r.Success)
            return new LogcatResult(false, ErrorCodes.Unknown, AdbWhy(r), AdbHint);

        var all = r.StdOut.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.StartsWith("--------- beginning of", StringComparison.Ordinal)).ToList();
        if (!string.IsNullOrEmpty(filter))
            all = all.Where(x => x.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        var picked = all.Skip(Math.Max(0, all.Count - lines)).ToList();

        var text = string.Join("\n", picked);
        var truncated = text.Length > MaxLogChars;
        if (truncated) text = "[... older lines cut ...]\n" + text[^MaxLogChars..];

        return new LogcatResult(true, Lines: picked.Count, Text: text, Truncated: truncated,
            Hint: picked.Count == 0 ? (string.IsNullOrEmpty(filter) ? "The log is empty (it may have been cleared)." : $"No log line contains '{filter}'. Try a shorter filter or more lines.") : null);
    }

    public static async Task<AndroidFindResult> FindAsync(string text, string? deviceSerial)
    {
        var tree = await AndroidUiService.GetUiTreeAsync(deviceSerial, interactiveOnly: false);
        if (tree is null)
            return new AndroidFindResult(false, ErrorCodes.Unknown, "Could not read the Android screen (uiautomator dump failed).", AdbHint);

        if (!AndroidUiService.HasMeaningfulElements(tree))
            return new AndroidFindResult(false, ErrorCodes.NoElements, "The Android screen exposes no named or clickable elements (black or secure screen, still loading, or a game).",
                "An empty result does not mean the screen is empty. Take android_screenshot to see it and tap with android_click(x, y).");

        var flat = new List<UiNode>();
        Flatten(tree, flat);
        var hits = flat.Where(n => !string.IsNullOrEmpty(n.Name) && n.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                                   && n.BoundingBox.Width > 0 && n.BoundingBox.Height > 0)
            .OrderBy(n => n.BoundingBox.Y).ThenBy(n => n.BoundingBox.X).ToList();

        if (hits.Count == 0)
            return new AndroidFindResult(false, ErrorCodes.ElementNotFound, $"The text '{text}' is not on the Android screen (in text or content description).",
                "Check the spelling, scroll if it may be further down (android_swipe), or look with android_screenshot.", Found: false, MatchCount: 0, Matches: []);

        var matches = hits.Take(10).Select(n => new AndroidMatch(n.Name!.Length > 120 ? n.Name[..120] + "..." : n.Name, n.Role, n.BoundingBox.CenterX, n.BoundingBox.CenterY)).ToList();
        return new AndroidFindResult(true, Found: true, MatchCount: hits.Count, Matches: matches,
            Hint: hits.Count > 1 ? $"{hits.Count} elements contain that text. To tap a specific one use android_click(x, y) with the x and y of that match (device pixels)." : null);
    }

    private static void Flatten(UiNode node, List<UiNode> into)
    {
        into.Add(node);
        foreach (var child in node.Children) Flatten(child, into);
    }

    private const string AdbHint = "Is a device connected and unlocked? android_list_devices shows devices and their state; with several devices pass deviceSerial.";

    private static string AdbWhy(AdbResult r) => string.IsNullOrWhiteSpace(r.StdErr) ? (string.IsNullOrWhiteSpace(r.StdOut) ? "adb failed." : r.StdOut.Trim()) : r.StdErr.Trim();

    private static TransferResult Failed(AdbResult r, string local, string device, string verb)
    {
        var why = AdbWhy(r);
        var missing = why.Contains("No such file", StringComparison.OrdinalIgnoreCase) || why.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
        var denied = why.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) || why.Contains("Read-only", StringComparison.OrdinalIgnoreCase);
        var noDevice = why.Contains("no devices", StringComparison.OrdinalIgnoreCase) || why.Contains("device offline", StringComparison.OrdinalIgnoreCase) || why.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) || why.Contains("not found", StringComparison.OrdinalIgnoreCase) && why.Contains("device", StringComparison.OrdinalIgnoreCase);
        var code = missing ? ErrorCodes.FileNotFound : denied ? ErrorCodes.AccessDenied : ErrorCodes.Unknown;
        var hint = noDevice ? AdbHint
            : denied ? "Android blocks that folder. Use /sdcard/ (or /storage/emulated/0/) for plain files; files private to an app can be pulled with android_pull(app=\"<package>\") if the app is debuggable."
            : missing ? $"The path on the {(verb == "pull" ? "device" : "device folder")} was not found. Check it (android_pull / android_push take full device paths like /sdcard/Download/a.apk)."
            : AdbHint;
        return new TransferResult(false, code, why, hint, local, device);
    }

    private static string Quote(string path) => "\"" + path.Replace("\"", "\\\"") + "\"";
}

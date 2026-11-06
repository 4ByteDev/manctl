namespace Manctl.Core.Android;

public static class AndroidInputService
{
    public static Task<AdbResult> TapAsync(string? deviceSerial, int x, int y) =>
        AdbClient.ShellAsync($"input tap {x} {y}", deviceSerial);

    public static Task<AdbResult> SwipeAsync(string? deviceSerial, int x1, int y1, int x2, int y2, int durationMs) =>
        AdbClient.ShellAsync($"input swipe {x1} {y1} {x2} {y2} {durationMs}", deviceSerial);

    /// <summary>`input keyevent KEYCODE_NOPE` exits 0 and only prints an exception, so the exit code alone says
    /// "fine" for a key that was never pressed.</summary>
    public static async Task<AdbResult> KeyEventAsync(string? deviceSerial, string keyCode)
    {
        var r = await AdbClient.ShellAsync($"input keyevent {keyCode}", deviceSerial);
        if (r.Success && LooksLikeAdbComplaint(r.StdOut + r.StdErr))
            return r with { Success = false, StdErr = (r.StdErr + " " + r.StdOut).Trim() };
        return r;
    }

    public static bool LooksLikeAdbComplaint(string text) =>
        text.Contains("Exception", StringComparison.OrdinalIgnoreCase) || text.Contains("Error:", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Unknown keycode", StringComparison.OrdinalIgnoreCase) || text.Contains("usage:", StringComparison.OrdinalIgnoreCase);

    /// <summary>`adb shell input text` only reliably supports ASCII (no IME behind it) — Cyrillic/CJK/emoji will
    /// not type correctly this way; that's an Android platform limitation, not something manctl can paper over
    /// without shipping a custom IME on the device.</summary>
    public static Task<AdbResult> InputTextAsync(string? deviceSerial, string text)
    {
        var escaped = text
            .Replace("\\", "\\\\")
            .Replace(" ", "%s")
            .Replace("'", "\\'")
            .Replace("\"", "\\\"")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("&", "\\&")
            .Replace(";", "\\;")
            .Replace("<", "\\<")
            .Replace(">", "\\>")
            .Replace("|", "\\|");

        return AdbClient.ShellAsync($"input text \"{escaped}\"", deviceSerial);
    }
}

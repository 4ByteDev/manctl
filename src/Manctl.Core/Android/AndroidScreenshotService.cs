namespace Manctl.Core.Android;

/// <summary>`screencap -p` writes a PNG straight to stdout — same "no on-device garbage" principle as the
/// uiautomator dump via /dev/tty, no temp file to push/pull/clean up.</summary>
public static class AndroidScreenshotService
{
    public static async Task<byte[]?> CaptureAsync(string? deviceSerial, int timeoutMs = 20000)
    {
        var result = await AdbClient.ExecOutBytesAsync("screencap -p", deviceSerial, timeoutMs);
        return result.Success && result.StdOutBytes.Length > 0 ? result.StdOutBytes : null;
    }
}

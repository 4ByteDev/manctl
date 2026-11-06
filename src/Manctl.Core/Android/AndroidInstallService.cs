using Manctl.Core.Models;

namespace Manctl.Core.Android;

public sealed record InstallResult(bool Success, string? ApkPath, string StdOut, string StdErr, string? Error = null, string? ErrorCode = null);

/// <summary>Installs the most recently modified file matching a glob in a directory — for the common case where
/// the APK filename embeds a version that changes every build, so the exact path can't be known in advance and
/// installing by a stale guessed path just fails with "file not found".</summary>
public static class AndroidInstallService
{
    public static async Task<InstallResult> InstallLatestMatchingAsync(
        string directory, string pattern, string? deviceSerial, bool reinstall, int timeoutMs)
    {
        if (!Directory.Exists(directory))
            return new InstallResult(false, null, "", "", Error: $"Directory not found: {directory}", ErrorCode: ErrorCodes.FileNotFound);

        var apk = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        if (apk is null)
            return new InstallResult(false, null, "", "", Error: $"No file matching '{pattern}' in {directory}", ErrorCode: ErrorCodes.FileNotFound);

        var flags = reinstall ? "-r " : "";
        var result = await AdbClient.RunAsync($"install {flags}\"{apk.FullName}\"", deviceSerial, timeoutMs);

        var success = result.Success &&
            !result.StdOut.Contains("Failure", StringComparison.OrdinalIgnoreCase) &&
            !result.StdErr.Contains("Failure", StringComparison.OrdinalIgnoreCase);

        return new InstallResult(success, apk.FullName, result.StdOut, result.StdErr,
            Error: success ? null : (result.StdErr.Length > 0 ? result.StdErr : result.StdOut),
            ErrorCode: success ? null : ErrorCodes.Unknown);
    }
}

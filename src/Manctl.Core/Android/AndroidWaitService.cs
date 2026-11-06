using Manctl.Core.Models;

namespace Manctl.Core.Android;

/// <summary>Server-side polling for an Android selector — mirrors ElementWaitService for native windows. Each
/// poll is a full `adb exec-out uiautomator dump`, which is far more expensive than a Windows UIA query, so the
/// poll interval floor is higher than the native equivalent.</summary>
public static class AndroidWaitService
{
    public static async Task<ActionResult> WaitForElementAsync(string? deviceSerial, ElementSelector selector, int timeoutMs, int pollIntervalMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (true)
        {
            var (node, whyCode, whyText) = await AndroidUiService.FindNodeOrWhyAsync(deviceSerial, selector);
            if (node is not null)
                return new ActionResult(true, "found", ResolvedRole: node.Role, ResolvedName: node.Name, ResolvedBoundingBox: node.BoundingBox);

            if (DateTime.UtcNow >= deadline)
            {
                // Plain "not there yet" is a timeout; an unreadable screen or a dead device is a different problem.
                return whyCode == ErrorCodes.ElementNotFound
                    ? new ActionResult(false, "timeout", Error: $"No element matched within {timeoutMs / 1000.0:0.##} seconds.", ErrorCode: ErrorCodes.Timeout)
                    : new ActionResult(false, "timeout", Error: whyText, ErrorCode: whyCode);
            }

            await Task.Delay(Math.Clamp(pollIntervalMs, 300, 5000));
        }
    }
}

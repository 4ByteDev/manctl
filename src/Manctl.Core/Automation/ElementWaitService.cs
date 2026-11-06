using Manctl.Core.Models;

namespace Manctl.Core.Automation;

/// <summary>Server-side polling for a selector — replaces the pattern of an agent looping get_ui_tree/find_element
/// itself, which burns a round-trip per attempt.</summary>
public static class ElementWaitService
{
    public static async Task<ActionResult> WaitForElementAsync(AutomationContext ctx, ElementSelector selector, int timeoutMs, int pollIntervalMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (true)
        {
            ResolvedElement? resolved;
            lock (ctx.Gate)
            {
                resolved = ElementResolver.Resolve(ctx, selector);
            }

            if (resolved is not null)
            {
                var element = resolved.Element;
                string? role = null;
                string? name = null;
                Rect box;
                lock (ctx.Gate)
                {
                    try { role = element.ControlType.ToString(); } catch { /* exotic element, leave null */ }
                    try { name = element.Name; } catch { /* exotic element, leave null */ }
                    box = UiTreeBuilder.SafeRect(element);
                }

                return new ActionResult(true, "found", ResolvedRole: role, ResolvedName: name, ResolvedBoundingBox: box);
            }

            if (DateTime.UtcNow >= deadline)
            {
                (string Code, string Message) why;
                lock (ctx.Gate) { why = ElementResolver.Diagnose(ctx, selector); }
                // "No element matched" after waiting is a plain timeout; the other two are different problems.
                var code = why.Code == ErrorCodes.ElementNotFound ? ErrorCodes.Timeout : why.Code;
                var message = why.Code == ErrorCodes.ElementNotFound ? $"No element matched within {timeoutMs / 1000.0:0.##} seconds." : why.Message;
                return new ActionResult(false, "timeout", Error: message, ErrorCode: code);
            }

            await Task.Delay(Math.Clamp(pollIntervalMs, 50, 2000));
        }
    }
}

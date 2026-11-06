using Manctl.Core.Models;
using Manctl.Core.Win32;

namespace Manctl.Core.Automation;

public static class ActionService
{
    public static ActionResult Click(AutomationContext ctx, ElementSelector selector, MouseButton button, bool doubleClick)
    {
        lock (ctx.Gate)
        {
            var resolved = ElementResolver.Resolve(ctx, selector);
            if (resolved is null)
                return NotFound(ctx, selector);

            var element = resolved.Element;
            var role = SafeRole(element);
            var name = SafeName(element);
            var box = UiTreeBuilder.SafeRect(element);

            try
            {
                if (button == MouseButton.Left && !doubleClick && element.Patterns.Invoke.IsSupported)
                {
                    element.Patterns.Invoke.Pattern.Invoke();
                    return new ActionResult(true, "invoke", ResolvedRole: role, ResolvedName: name, ResolvedBoundingBox: box);
                }
            }
            catch
            {
                // fall through to real mouse input below
            }

            if (box.Width <= 0 || box.Height <= 0)
                return new ActionResult(false, "mouse", Error: "Element has no visible bounding box.", ResolvedRole: role, ResolvedName: name, ErrorCode: ErrorCodes.Unknown);

            if (EnsureFront(resolved.WindowHandle, "mouse") is { } stuckClick) return stuckClick;
            InputSimulator.Click(box.CenterX, box.CenterY, button, doubleClick);
            return new ActionResult(true, "mouse", ResolvedRole: role, ResolvedName: name, ResolvedBoundingBox: box);
        }
    }

    /// <summary>Keys and mouse clicks reach whatever is in front. If the window we were told to use could not be brought
    /// forward, sending them anyway would put them into the WRONG window — so stop and say why.</summary>
    private static ActionResult? EnsureFront(long handle, string method)
    {
        var focus = FocusService.Focus(handle);
        return focus.Ok ? null : new ActionResult(false, method, Error: focus.Error, ErrorCode: ErrorCodes.AccessDenied, Hint: focus.Hint);
    }

    /// <summary>Keys and typed text go to whatever window is in front. The answer names that window, so a wrong
    /// target is visible instead of silent.</summary>
    private static ActionResult SentTo(string method)
    {
        var front = WindowManager.FindWindow(null, null);
        return new ActionResult(true, method, ResolvedRole: "Window", ResolvedName: front?.Title);
    }

    /// <summary>Caller holds the gate. The honest reason nothing was found (no such window / window shows no elements / no match).</summary>
    internal static ActionResult NotFound(AutomationContext ctx, ElementSelector selector)
    {
        var (code, why) = ElementResolver.Diagnose(ctx, selector);
        return new ActionResult(false, "none", Error: why, ErrorCode: code);
    }

    /// <summary>Click at a screen point — for canvases, games and other surfaces that have no UI elements.</summary>
    public static ActionResult ClickAt(AutomationContext ctx, int x, int y, MouseButton button, bool doubleClick, long? windowHandle)
    {
        var bad = ScreenGuard.CheckPoint(x, y, "click point") ?? ScreenGuard.CheckInWindow(x, y, windowHandle);
        if (bad is not null)
            return new ActionResult(false, "none", Error: bad, ErrorCode: ErrorCodes.InvalidArgument,
                Hint: "Pick a point on the screen (screenshot shows where things are). If you mean a spot inside a window, add the window's left and top to your position, or leave windowHandle out.");

        lock (ctx.Gate)
        {
            if (windowHandle is { } h && EnsureFront(h, "mouse") is { } stuckAt) return stuckAt;
            InputSimulator.Click(x, y, button, doubleClick);
            return new ActionResult(true, "mouse", ResolvedBoundingBox: new Rect(x, y, 0, 0));
        }
    }

    /// <summary>type_text without any element criteria: type into whatever currently has keyboard focus
    /// (after bringing the given window forward, if one was named).</summary>
    public static ActionResult TypeIntoFocused(AutomationContext ctx, string text, long? windowHandle, string? windowTitle)
    {
        lock (ctx.Gate)
        {
            if (windowHandle is not null || windowTitle is not null)
            {
                var window = WindowManager.FindWindow(windowHandle, windowTitle);
                if (window is null)
                    return new ActionResult(false, "none", Error: "No matching window found.", ErrorCode: ErrorCodes.WindowNotFound);
                if (EnsureFront(window.Handle, "sendKeys") is { } stuckType) return stuckType;
            }
            InputSimulator.TypeUnicodeText(text);
            return SentTo("sendKeys");
        }
    }

    public static ActionResult TypeText(AutomationContext ctx, ElementSelector selector, string text, bool forceSendKeys)
    {
        lock (ctx.Gate)
        {
            var resolved = ElementResolver.Resolve(ctx, selector);
            if (resolved is null)
                return NotFound(ctx, selector);

            var element = resolved.Element;
            var role = SafeRole(element);
            var name = SafeName(element);
            var box = UiTreeBuilder.SafeRect(element);

            try
            {
                if (!forceSendKeys && element.Patterns.Value.IsSupported && !element.Patterns.Value.Pattern.IsReadOnly)
                {
                    element.Patterns.Value.Pattern.SetValue(text);
                    return new ActionResult(true, "setValue", ResolvedRole: role, ResolvedName: name, ResolvedBoundingBox: box);
                }
            }
            catch
            {
                // fall through to real keyboard input below
            }

            if (EnsureFront(resolved.WindowHandle, "sendKeys") is { } stuckKeys) return stuckKeys;
            try { element.Focus(); } catch { /* best effort; focused window is already correct */ }
            InputSimulator.TypeUnicodeText(text);
            return new ActionResult(true, "sendKeys", ResolvedRole: role, ResolvedName: name, ResolvedBoundingBox: box);
        }
    }

    public static ActionResult KeyPress(AutomationContext ctx, string keys, long? windowHandle)
    {
        lock (ctx.Gate)
        {
            if (windowHandle is { } h && EnsureFront(h, "sendKeys") is { } stuckKey) return stuckKey;

            try
            {
                InputSimulator.SendKeyChord(keys);
                return SentTo("sendKeys");
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "sendKeys", Error: ex.Message, ErrorCode: ErrorCodes.InvalidArgument);
            }
        }
    }

    public static VerifyResult Verify(AutomationContext ctx, ElementSelector selector, string property, string? expected)
    {
        lock (ctx.Gate)
        {
            var resolved = ElementResolver.Resolve(ctx, selector);
            if (resolved is null)
            {
                var (code, why) = ElementResolver.Diagnose(ctx, selector);
                return new VerifyResult(false, property, expected, Actual: null, ErrorCode: code, Error: why);
            }

            var element = resolved.Element;
            var knownProperty = true;
            string? actual = property.ToLowerInvariant() switch
            {
                "name" => SafeName(element),
                "value" => TryGetValue(element),
                "enabled" or "isenabled" => element.IsEnabled.ToString(),
                "toggled" or "istoggled" => TryGetToggleState(element),
                "offscreen" or "isoffscreen" => element.IsOffscreen.ToString(),
                _ => Unknown(),
            };

            string? Unknown() { knownProperty = false; return null; }

            var boolish = property.ToLowerInvariant() is "toggled" or "istoggled" or "enabled" or "isenabled" or "offscreen" or "isoffscreen";
            if (boolish) actual = NormalizeBool(actual);
            var success = expected is null
                ? actual is not null
                : string.Equals(actual, boolish ? NormalizeBool(expected) : expected, StringComparison.OrdinalIgnoreCase);

            // ErrorCode reflects an operational failure (couldn't read the property at all), not "the value
            // didn't match expected" — a mismatch is a legitimate, informative result, not an error.
            return new VerifyResult(success, property, expected, actual, ErrorCode: knownProperty ? null : ErrorCodes.InvalidArgument);
        }
    }

    /// <summary>On/Off/checked/true... all mean the same to an agent; the canonical form is "true"/"false".</summary>
    internal static string? NormalizeBool(string? v) => v?.Trim().ToLowerInvariant() switch
    {
        "true" or "on" or "checked" or "yes" or "1" => "true",
        "false" or "off" or "unchecked" or "no" or "0" => "false",
        var other => other,
    };

    private static string? SafeRole(FlaUI.Core.AutomationElements.AutomationElement element)
    {
        try { return element.ControlType.ToString(); } catch { return null; }
    }

    private static string? SafeName(FlaUI.Core.AutomationElements.AutomationElement element)
    {
        try { return element.Name; } catch { return null; }
    }

    private static string? TryGetValue(FlaUI.Core.AutomationElements.AutomationElement element)
    {
        try { return element.Patterns.Value.IsSupported ? element.Patterns.Value.Pattern.Value.ValueOrDefault : null; }
        catch { return null; }
    }

    private static string? TryGetToggleState(FlaUI.Core.AutomationElements.AutomationElement element)
    {
        try { return element.Patterns.Toggle.IsSupported ? element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault.ToString() : null; }
        catch { return null; }
    }
}

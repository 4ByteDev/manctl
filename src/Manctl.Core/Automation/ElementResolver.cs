using FlaUI.Core.AutomationElements;
using Manctl.Core.Models;
using Manctl.Core.Win32;

namespace Manctl.Core.Automation;

public sealed record ResolvedElement(AutomationElement Element, long WindowHandle, string WindowTitle);

public static class ElementResolver
{
    /// <summary>Resolves a selector to a concrete UIA element plus the top-level window it lives in
    /// (needed later to bring the right window to foreground before any real mouse/keyboard input).
    /// Caller must hold the AutomationContext gate.</summary>
    public static ResolvedElement? Resolve(AutomationContext ctx, ElementSelector selector)
    {
        var window = WindowManager.FindWindow(selector.WindowHandle, selector.WindowTitle);
        if (window is null)
            return null;

        var root = ctx.Automation.FromHandle(new IntPtr(window.Handle));
        if (root is null)
            return null;

        var candidates = root.FindAllDescendants();

        var matches = candidates.Where(el => Matches(el, selector)).ToList();
        if (selector.Index >= matches.Count)
            return null;

        return new ResolvedElement(matches[selector.Index], window.Handle, window.Title);
    }

    /// <summary>Called after Resolve returned null: the honest reason. "No such window" and "the window shows no
    /// elements at all (game/canvas/ImGui)" must not both be reported as a plain "element not found".
    /// Caller must hold the AutomationContext gate.</summary>
    public static (string ErrorCode, string Message) Diagnose(AutomationContext ctx, ElementSelector selector)
    {
        var window = WindowManager.FindWindow(selector.WindowHandle, selector.WindowTitle);
        if (window is null)
        {
            var what = selector.WindowHandle is { } h ? $"handle {h}" : selector.WindowTitle is { } t ? $"'{t}' in the title" : "(the window in front)";
            return (ErrorCodes.WindowNotFound, $"No window with {what}.");
        }

        var root = ctx.Automation.FromHandle(new IntPtr(window.Handle));
        if (root is null || !UiTreeBuilder.HasMeaningfulElements(root))
            return (ErrorCodes.NoElements,
                $"Window '{window.Title}' exposes no UI elements (it is probably a game, canvas or ImGui window), so nothing can be found by name.");

        return (ErrorCodes.ElementNotFound, $"No element matched in window '{window.Title}'.");
    }

    private static bool Matches(AutomationElement element, ElementSelector selector)
    {
        try
        {
            if (!string.IsNullOrEmpty(selector.AutomationId) &&
                !string.Equals(element.AutomationId, selector.AutomationId, StringComparison.Ordinal))
                return false;

            if (!string.IsNullOrEmpty(selector.Role) &&
                !string.Equals(element.ControlType.ToString(), selector.Role, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(selector.Text))
            {
                var name = element.Name ?? string.Empty;
                if (!name.Contains(selector.Text, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}

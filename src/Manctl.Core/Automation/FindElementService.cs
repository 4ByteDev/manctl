using FlaUI.Core.AutomationElements;
using Manctl.Core.Models;

namespace Manctl.Core.Automation;

public sealed record FindElementResult(List<UiNode> Matches, int TotalMatches, string? Error = null, string? ErrorCode = null);

/// <summary>Point query against the UI tree instead of a full get_ui_tree dump — for big/complex apps (IDEs, heavy
/// GUIs) where the whole tree is huge in tokens but the agent only needs one specific element. Each match is
/// returned as a leaf (no Children) — a point query isn't meant to hand back a whole subtree too.</summary>
public static class FindElementService
{
    public static FindElementResult Find(AutomationContext ctx, long? windowHandle, string? name, string? role, string? automationId, int maxResults)
    {
        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(role) && string.IsNullOrEmpty(automationId))
            return new FindElementResult([], 0, Error: "At least one of name/role/automationId is required.", ErrorCode: ErrorCodes.InvalidArgument);

        maxResults = Math.Clamp(maxResults, 1, 200);

        lock (ctx.Gate)
        {
            var root = windowHandle is { } h
                ? ctx.Automation.FromHandle(new IntPtr(h))
                : ctx.Automation.GetDesktop();

            if (root is null)
                return new FindElementResult([], 0, Error: "Could not access the target window.", ErrorCode: ErrorCodes.WindowNotFound);

            var matches = new List<UiNode>();
            var total = 0;
            Walk(root, name, role, automationId, maxResults, matches, ref total);
            if (total > 0)
                return new FindElementResult(matches, total);

            // Nothing matched: was the window even readable?
            if (windowHandle is not null && !UiTreeBuilder.HasMeaningfulElements(root))
                return new FindElementResult([], 0, ErrorCode: ErrorCodes.NoElements,
                    Error: "This window exposes no UI elements (it is probably a game, canvas or ImGui window), so nothing can be found by name.");
            return new FindElementResult([], 0, ErrorCode: ErrorCodes.ElementNotFound,
                Error: windowHandle is null ? "No element matched in any visible window." : "No element matched in that window.");
        }
    }

    private static void Walk(AutomationElement element, string? name, string? role, string? automationId, int maxResults, List<UiNode> matches, ref int total)
    {
        if (SafeBool(() => element.IsOffscreen, false))
            return; // prune invisible subtrees, same as get_ui_tree

        if (IsMatch(element, name, role, automationId))
        {
            total++;
            if (matches.Count < maxResults)
                matches.Add(BuildLeaf(element));
        }

        foreach (var child in SafeChildren(element))
            Walk(child, name, role, automationId, maxResults, matches, ref total);
    }

    private static bool IsMatch(AutomationElement element, string? name, string? role, string? automationId)
    {
        try
        {
            if (!string.IsNullOrEmpty(automationId) && !string.Equals(element.AutomationId, automationId, StringComparison.Ordinal))
                return false;
            if (!string.IsNullOrEmpty(role) && !string.Equals(element.ControlType.ToString(), role, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(name) && !(element.Name ?? string.Empty).Contains(name, StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static UiNode BuildLeaf(AutomationElement element)
    {
        var patterns = new List<string>();
        try
        {
            var p = element.Patterns;
            if (p.Invoke.IsSupported) patterns.Add("Invoke");
            if (p.Value.IsSupported) patterns.Add("Value");
            if (p.Toggle.IsSupported) patterns.Add("Toggle");
            if (p.SelectionItem.IsSupported) patterns.Add("SelectionItem");
            if (p.ExpandCollapse.IsSupported) patterns.Add("ExpandCollapse");
            if (p.RangeValue.IsSupported) patterns.Add("RangeValue");
            if (p.Scroll.IsSupported) patterns.Add("Scroll");
        }
        catch { /* exotic element, leave patterns empty */ }

        var role = SafeString(() => element.ControlType.ToString()) ?? "Unknown";
        var name = SafeString(() => element.Name);
        var automationId = SafeString(() => element.AutomationId);

        string? value = null;
        try { value = element.Patterns.Value.IsSupported ? element.Patterns.Value.Pattern.Value.ValueOrDefault : null; }
        catch { /* exotic element, leave null */ }

        return new UiNode
        {
            Role = role,
            Name = string.IsNullOrEmpty(name) ? null : name,
            Value = value,
            AutomationId = string.IsNullOrEmpty(automationId) ? null : automationId,
            ClassName = SafeString(() => element.ClassName),
            BoundingBox = UiTreeBuilder.SafeRect(element),
            IsEnabled = SafeBool(() => element.IsEnabled, true),
            Patterns = [.. patterns],
            Children = [],
        };
    }

    private static AutomationElement[] SafeChildren(AutomationElement element)
    {
        try { return element.FindAllChildren(); }
        catch { return []; }
    }

    private static string? SafeString(Func<string?> getter)
    {
        try { return getter(); }
        catch { return null; }
    }

    private static bool SafeBool(Func<bool> getter, bool fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }
}

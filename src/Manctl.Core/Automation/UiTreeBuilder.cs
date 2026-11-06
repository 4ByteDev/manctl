using FlaUI.Core.AutomationElements;
using Manctl.Core.Models;

namespace Manctl.Core.Automation;

public static class UiTreeBuilder
{
    /// <summary>Walks the UIA tree from <paramref name="root"/> into a compact JSON-friendly node tree.
    /// Offscreen subtrees are always pruned (dead weight for an agent). When <paramref name="interactiveOnly"/>
    /// is set, leaf nodes that are unnamed and offer no actionable pattern are dropped too.</summary>
    public static UiNode? Build(AutomationElement element, int maxDepth, bool interactiveOnly, int depth = 0)
    {
        var children = new List<UiNode>();

        if (depth < maxDepth)
        {
            foreach (var child in SafeChildren(element))
            {
                if (SafeBool(() => child.IsOffscreen, false))
                    continue;

                var node = Build(child, maxDepth, interactiveOnly, depth + 1);
                if (node != null)
                    children.Add(node);
            }
        }

        var patterns = GetSupportedPatterns(element);
        var role = SafeString(() => element.ControlType.ToString()) ?? "Unknown";
        var name = SafeString(() => element.Name);
        var automationId = SafeString(() => element.AutomationId);
        var isInteractive = patterns.Length > 0;

        if (interactiveOnly && children.Count == 0 && !isInteractive && string.IsNullOrWhiteSpace(name))
            return null;

        return new UiNode
        {
            Role = role,
            Name = string.IsNullOrEmpty(name) ? null : name,
            Value = SafeValue(element),
            AutomationId = string.IsNullOrEmpty(automationId) ? null : automationId,
            ClassName = SafeString(() => element.ClassName),
            BoundingBox = SafeRect(element),
            IsEnabled = SafeBool(() => element.IsEnabled, true),
            Patterns = patterns,
            Children = children,
        };
    }

    /// <summary>True if the window offers at least one element an agent can use: something with a name, or something
    /// that can be invoked/typed into/toggled. The title bar and its system buttons exist on EVERY window (even an
    /// ImGui game window), so they do not count. A window that fails this check is invisible to every element tool.</summary>
    public static bool HasMeaningfulElements(AutomationElement root, int maxVisited = 400)
    {
        var visited = 0;
        return Walk(root, ref visited, maxVisited, isRoot: true);

        static bool Walk(AutomationElement el, ref int visited, int max, bool isRoot)
        {
            if (++visited > max) return true; // a big tree is certainly not empty
            if (!isRoot)
            {
                var role = SafeString(() => el.ControlType.ToString());
                if (role == "TitleBar") return false; // skip the whole title bar subtree
                if (!string.IsNullOrWhiteSpace(SafeString(() => el.Name)) || GetSupportedPatterns(el).Length > 0)
                    return true;
            }
            foreach (var child in SafeChildren(el))
                if (Walk(child, ref visited, max, false)) return true;
            return false;
        }
    }

    private static AutomationElement[] SafeChildren(AutomationElement element)
    {
        try { return element.FindAllChildren(); }
        catch { return []; }
    }

    private static string[] GetSupportedPatterns(AutomationElement element)
    {
        var supported = new List<string>();
        try
        {
            var p = element.Patterns;
            if (p.Invoke.IsSupported) supported.Add("Invoke");
            if (p.Value.IsSupported) supported.Add("Value");
            if (p.Toggle.IsSupported) supported.Add("Toggle");
            if (p.SelectionItem.IsSupported) supported.Add("SelectionItem");
            if (p.ExpandCollapse.IsSupported) supported.Add("ExpandCollapse");
            if (p.RangeValue.IsSupported) supported.Add("RangeValue");
            if (p.Scroll.IsSupported) supported.Add("Scroll");
        }
        catch { /* some frameworks throw on pattern probing for exotic elements */ }
        return [.. supported];
    }

    private static string? SafeValue(AutomationElement element)
    {
        try
        {
            return element.Patterns.Value.IsSupported
                ? element.Patterns.Value.Pattern.Value.ValueOrDefault
                : null;
        }
        catch { return null; }
    }

    internal static Rect SafeRect(AutomationElement element)
    {
        try
        {
            var r = element.BoundingRectangle;
            return new Rect(r.X, r.Y, r.Width, r.Height);
        }
        catch { return new Rect(0, 0, 0, 0); }
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

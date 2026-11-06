using System.Text.RegularExpressions;
using System.Xml.Linq;
using Manctl.Core.Models;

namespace Manctl.Core.Android;

/// <summary>Parses `adb shell uiautomator dump` XML into the same UiNode shape get_ui_tree returns for native
/// Windows apps, so an agent doesn't need Android-specific handling — same role/name/boundingBox/patterns shape.</summary>
public static partial class UiAutomatorXmlParser
{
    [GeneratedRegex(@"\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]")]
    private static partial Regex BoundsRegex();

    /// <summary>When <paramref name="interactiveOnly"/> is true (default), unnamed non-interactive leaves are
    /// pruned — Compose/View hierarchies routinely nest dozens of anonymous FrameLayout/LinearLayout wrappers
    /// around the handful of elements that actually matter, and those are pure token cost with no value.</summary>
    public static UiNode? Parse(string xml, bool interactiveOnly = true)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return null; }

        var firstNode = doc.Root?.Elements("node").FirstOrDefault();
        return firstNode is null ? null : Convert(firstNode, interactiveOnly);
    }

    private static UiNode? Convert(XElement node, bool interactiveOnly)
    {
        var children = node.Elements("node")
            .Select(child => Convert(child, interactiveOnly))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();

        var patterns = new List<string>();
        if (IsTrue(node, "clickable")) patterns.Add("Invoke");
        if (IsTrue(node, "checkable")) patterns.Add("Toggle");
        if (IsTrue(node, "scrollable")) patterns.Add("Scroll");
        if (IsTrue(node, "long-clickable")) patterns.Add("LongPress");

        var className = node.Attribute("class")?.Value;
        var role = className is { Length: > 0 } ? className[(className.LastIndexOf('.') + 1)..] : "Node";
        var name = FirstNonEmpty(node.Attribute("text")?.Value, node.Attribute("content-desc")?.Value);
        var resourceId = node.Attribute("resource-id")?.Value;
        var isInteractive = patterns.Count > 0;

        if (interactiveOnly && children.Count == 0 && !isInteractive && string.IsNullOrEmpty(name))
            return null;

        return new UiNode
        {
            Role = role,
            Name = string.IsNullOrEmpty(name) ? null : name,
            // Android has no separate "value" concept beyond text (already surfaced via Name) — for checkable
            // elements this instead carries the live checked state ("true"/"false"), the closest analogue to
            // Windows' ToggleState, so android_verify has something to read for property="toggled"/"value".
            Value = IsTrue(node, "checkable") ? node.Attribute("checked")?.Value : null,
            AutomationId = string.IsNullOrEmpty(resourceId) ? null : resourceId,
            ClassName = className,
            BoundingBox = ParseBounds(node.Attribute("bounds")?.Value),
            IsEnabled = IsTrue(node, "enabled"),
            Patterns = [.. patterns],
            Children = children,
        };
    }

    private static Rect ParseBounds(string? bounds)
    {
        if (string.IsNullOrEmpty(bounds))
            return new Rect(0, 0, 0, 0);

        var m = BoundsRegex().Match(bounds);
        if (!m.Success)
            return new Rect(0, 0, 0, 0);

        var left = int.Parse(m.Groups[1].Value);
        var top = int.Parse(m.Groups[2].Value);
        var right = int.Parse(m.Groups[3].Value);
        var bottom = int.Parse(m.Groups[4].Value);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static bool IsTrue(XElement node, string attr) => node.Attribute(attr)?.Value == "true";

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrEmpty(v));
}

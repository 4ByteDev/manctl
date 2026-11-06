using Manctl.Core.Models;

namespace Manctl.Core.Android;

public static class AndroidUiService
{
    public static async Task<UiNode?> GetUiTreeAsync(string? deviceSerial, bool interactiveOnly = true)
    {
        var xml = await DumpXmlAsync(deviceSerial);
        return xml is null ? null : UiAutomatorXmlParser.Parse(xml, interactiveOnly);
    }

    /// <summary>True if the screen offers at least one element with a name or an action. A black/secure/loading
    /// screen has only anonymous layout wrappers and fails this check.</summary>
    public static bool HasMeaningfulElements(UiNode tree)
    {
        var flat = new List<UiNode>();
        Flatten(tree, flat);
        return flat.Skip(1).Any(n => !string.IsNullOrWhiteSpace(n.Name) || n.Patterns.Length > 0);
    }

    /// <summary>Find a node, or the honest reason there is none.</summary>
    public static async Task<(UiNode? Node, string? ErrorCode, string? Error)> FindNodeOrWhyAsync(string? deviceSerial, ElementSelector selector)
    {
        var tree = await GetUiTreeAsync(deviceSerial, interactiveOnly: false);
        if (tree is null)
            return (null, ErrorCodes.Unknown,
                "Could not read the Android screen (uiautomator dump failed). Is a device connected and unlocked? android_list_devices shows the state.");

        var flat = new List<UiNode>();
        Flatten(tree, flat);
        var matches = flat.Where(n => Matches(n, selector)).ToList();
        if (selector.Index < matches.Count)
            return (matches[selector.Index], null, null);

        if (!HasMeaningfulElements(tree))
            return (null, ErrorCodes.NoElements,
                "The Android screen exposes no named or clickable elements (black or secure screen, still loading, or a game), so nothing can be found by name.");
        return (null, ErrorCodes.ElementNotFound, "No element matched on the Android screen.");
    }

    public static async Task<UiNode?> FindNodeAsync(string? deviceSerial, ElementSelector selector)
    {
        // Always resolve against the full, unpruned tree — a selector can legitimately target an element that
        // interactiveOnly's pruning would otherwise have dropped (e.g. matching by Role alone on an unnamed node).
        var tree = await GetUiTreeAsync(deviceSerial, interactiveOnly: false);
        if (tree is null)
            return null;

        var flat = new List<UiNode>();
        Flatten(tree, flat);

        var matches = flat.Where(n => Matches(n, selector)).ToList();
        return selector.Index < matches.Count ? matches[selector.Index] : null;
    }

    public static async Task<VerifyResult> VerifyAsync(string? deviceSerial, ElementSelector selector, string property, string? expected)
    {
        var (node, whyCode, whyText) = await FindNodeOrWhyAsync(deviceSerial, selector);
        if (node is null)
            return new VerifyResult(false, property, expected, Actual: null, ErrorCode: whyCode, Error: whyText);

        var knownProperty = true;
        string? actual = property.ToLowerInvariant() switch
        {
            "name" => node.Name,
            "value" or "toggled" or "istoggled" => node.Value,
            "enabled" or "isenabled" => node.IsEnabled.ToString(),
            // uiautomator's dump has no equivalent to UIA's IsOffscreen — every dumped node is part of the
            // current hierarchy, on-screen or not, with no reliable "visible to user" flag to read instead.
            "offscreen" or "isoffscreen" => null,
            _ => Unknown(),
        };

        string? Unknown() { knownProperty = false; return null; }

        var boolish = property.ToLowerInvariant() is "toggled" or "istoggled" or "enabled" or "isenabled";
        if (boolish) actual = Automation.ActionService.NormalizeBool(actual);
        var success = expected is null
            ? actual is not null
            : string.Equals(actual, boolish ? Automation.ActionService.NormalizeBool(expected) : expected, StringComparison.OrdinalIgnoreCase);

        return new VerifyResult(success, property, expected, actual, ErrorCode: knownProperty ? null : ErrorCodes.InvalidArgument);
    }

    public static async Task<ActionResult> ClickAsync(string? deviceSerial, ElementSelector selector)
    {
        var (node, whyCode, whyText) = await FindNodeOrWhyAsync(deviceSerial, selector);
        if (node is null)
            return new ActionResult(false, "none", Error: whyText, ErrorCode: whyCode);

        if (node.BoundingBox.Width <= 0 || node.BoundingBox.Height <= 0)
            return new ActionResult(false, "tap", Error: "Element has no visible bounding box.", ResolvedRole: node.Role, ResolvedName: node.Name, ErrorCode: ErrorCodes.Unknown);

        var result = await AdbClient.ShellAsync($"input tap {node.BoundingBox.CenterX} {node.BoundingBox.CenterY}", deviceSerial);
        return new ActionResult(result.Success, "tap", Error: result.Success ? null : result.StdErr,
            ResolvedRole: node.Role, ResolvedName: node.Name, ResolvedBoundingBox: node.BoundingBox,
            ErrorCode: result.Success ? null : ErrorCodes.Unknown);
    }

    private const int MaxDumpAttempts = 3;
    private const int DumpRetryDelayMs = 1500;

    private static async Task<string?> DumpXmlAsync(string? deviceSerial)
    {
        for (var attempt = 1; attempt <= MaxDumpAttempts; attempt++)
        {
            // /dev/tty makes uiautomator print the XML to exec-out's stdout directly instead of a file on the
            // device that we'd then need to pull and delete — no on-device garbage left behind.
            var result = await AdbClient.ExecOutAsync("uiautomator dump /dev/tty", deviceSerial, timeoutMs: 20000);
            if (result.Success)
            {
                var start = result.StdOut.IndexOf('<');
                var end = result.StdOut.LastIndexOf('>');
                if (start >= 0 && end >= 0 && end > start)
                    return result.StdOut[start..(end + 1)];
            }

            // sys.boot_completed=1 doesn't mean SystemUI/accessibility services are ready yet — a dump attempted
            // right after boot (or mid-screen-transition) can transiently come back empty. One retry avoids
            // surfacing that as a false "dump failed" to the caller instead of the real UI state.
            if (attempt < MaxDumpAttempts)
                await Task.Delay(DumpRetryDelayMs);
        }

        return null;
    }

    private static void Flatten(UiNode node, List<UiNode> into)
    {
        into.Add(node);
        foreach (var child in node.Children)
            Flatten(child, into);
    }

    private static bool Matches(UiNode node, ElementSelector selector)
    {
        if (!string.IsNullOrEmpty(selector.AutomationId) && !string.Equals(node.AutomationId, selector.AutomationId, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrEmpty(selector.Role) && !string.Equals(node.Role, selector.Role, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(selector.Text) && !(node.Name ?? string.Empty).Contains(selector.Text, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}

using FlaUI.Core.AutomationElements;
using Manctl.Core.Models;
using Manctl.Core.Win32;

namespace Manctl.Core.Automation;

public sealed record TextMatch(string Text, string Role, int X, int Y);

/// <summary>read_text answer. Without 'contains': Text holds everything readable, top to bottom, one line per visual
/// row. With 'contains': only the matches (Found / MatchCount / Matches), each with the screen point to click.</summary>
public sealed record ReadTextResult(
    bool Ok,
    string? ErrorCode = null,
    string? Error = null,
    string? Hint = null,
    string? Window = null,
    string? Text = null,
    int? Chars = null,
    bool? Truncated = null,
    bool? Found = null,
    int? MatchCount = null,
    List<TextMatch>? Matches = null);

/// <summary>Reads the text a window shows, the cheap way for an agent to "see" a window: labels, buttons, field values,
/// list and menu items, document text. Repeated texts stay repeated (two OK buttons are two lines); a window that
/// exposes nothing is an error (NoElements), never an empty success; password fields are never read.</summary>
public static class ReadTextService
{
    private sealed record Item(string Text, string Role, Rect Box);

    private static readonly HashSet<string> TextRoles = new(StringComparer.Ordinal)
    {
        "Text", "Button", "CheckBox", "RadioButton", "MenuItem", "ListItem", "TabItem", "TreeItem", "Hyperlink",
        "ComboBox", "DataItem", "HeaderItem", "Edit", "Document", "SplitButton",
    };

    private const int MaxVisited = 6000;
    private const int MaxMatchesReturned = 10;

    public static ReadTextResult Read(AutomationContext ctx, long? windowHandle, string? windowTitle, string? contains, int maxChars)
    {
        maxChars = Math.Clamp(maxChars, 200, 100_000);

        lock (ctx.Gate)
        {
            var window = WindowManager.FindWindow(windowHandle, windowTitle);
            if (window is null)
            {
                var what = windowHandle is { } h ? $"handle {h}" : windowTitle is { } t ? $"'{t}' in the title" : "(the window in front)";
                return new ReadTextResult(false, ErrorCodes.WindowNotFound, $"No window with {what}.");
            }

            var root = ctx.Automation.FromHandle(new IntPtr(window.Handle));
            if (root is null)
                return new ReadTextResult(false, ErrorCodes.WindowNotFound, "Could not access that window.", Window: window.Title);

            if (!UiTreeBuilder.HasMeaningfulElements(root))
                return new ReadTextResult(false, ErrorCodes.NoElements,
                    $"Window '{window.Title}' exposes no readable text (it is probably a game, canvas or ImGui window). That says nothing about what is drawn in it.",
                    "Take a screenshot of the window (or an area; scale 2 enlarges small text) to read it with your own eyes, and use click(x, y) with screen coordinates.",
                    Window: window.Title);

            var items = new List<Item>();
            var visited = 0;
            Collect(root, items, ref visited, isRoot: true);

            // Reading order: top to bottom, left to right.
            var ordered = items.OrderBy(i => i.Box.Y).ThenBy(i => i.Box.X).ToList();

            if (!string.IsNullOrEmpty(contains))
                return Search(window.Title, ordered, contains);

            var lines = new List<string>();
            var lineY = int.MinValue;
            var current = new List<string>();
            foreach (var item in ordered)
            {
                if (current.Count > 0 && Math.Abs(item.Box.Y - lineY) > 8)
                {
                    lines.Add(string.Join("  ", current));
                    current.Clear();
                }
                if (current.Count == 0) lineY = item.Box.Y;
                current.Add(item.Text);
            }
            if (current.Count > 0) lines.Add(string.Join("  ", current));

            var text = string.Join("\n", lines);
            var truncated = text.Length > maxChars;
            if (truncated) text = text[..maxChars] + "\n[... cut: raise maxChars or use contains]";

            if (text.Length == 0)
                return new ReadTextResult(false, ErrorCodes.NoElements,
                    $"Window '{window.Title}' has elements but none with readable text.",
                    "Take a screenshot to see what it shows.", Window: window.Title);

            return new ReadTextResult(true, Window: window.Title, Text: text, Chars: text.Length, Truncated: truncated,
                Hint: ordered.Count < 3 ? "Very little text was readable. Some programs (browsers, games) expose only part of what they show; a screenshot is the sure way to check." : null);
        }
    }

    private static ReadTextResult Search(string windowTitle, List<Item> ordered, string contains)
    {
        var hits = ordered.Where(i => i.Text.Contains(contains, StringComparison.OrdinalIgnoreCase)).ToList();
        var matches = hits.Take(MaxMatchesReturned)
            .Select(i => new TextMatch(i.Text.Length > 120 ? i.Text[..120] + "..." : i.Text, i.Role, i.Box.CenterX, i.Box.CenterY)).ToList();

        if (hits.Count == 0)
            return new ReadTextResult(false, ErrorCodes.ElementNotFound,
                $"The text '{contains}' was not found in what window '{windowTitle}' shows as text.",
                "Check the spelling, or look with read_text (no 'contains') / screenshot: programs like browsers expose only part of their text, so 'not found' is not proof that it is not on screen.",
                Window: windowTitle, Found: false, MatchCount: 0, Matches: []);

        return new ReadTextResult(true, Window: windowTitle, Found: true, MatchCount: hits.Count, Matches: matches,
            Hint: hits.Count > 1
                ? $"{hits.Count} elements contain that text. To use a specific one, click(x, y) with the x and y of that match (they are screen coordinates)."
                : null);
    }

    private static void Collect(AutomationElement el, List<Item> items, ref int visited, bool isRoot)
    {
        if (++visited > MaxVisited) return;

        string role = Safe(() => el.ControlType.ToString()) ?? "Unknown";
        if (!isRoot)
        {
            if (role == "TitleBar") return;                       // the title is already known from the window itself
            if (Safe(() => el.IsOffscreen, false)) return;        // hidden parts are not "shown"
        }

        var children = Safe(() => el.FindAllChildren(), Array.Empty<AutomationElement>())!;

        if (!isRoot)
        {
            var text = OwnText(el, role, children.Length == 0);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var box = Safe(() => el.BoundingRectangle, default(System.Drawing.Rectangle));
                if (box.Width > 0 && box.Height > 0)
                {
                    // A container and its own child often both carry the same name (a button and its label): count it once.
                    var name = Safe(() => el.Name);
                    var childRepeats = !string.IsNullOrEmpty(name) && children.Any(c => Safe(() => c.Name) == name);
                    if (!childRepeats)
                        items.Add(new Item(text.Trim(), role, new Rect(box.X, box.Y, box.Width, box.Height)));
                }
            }
        }

        foreach (var child in children)
            Collect(child, items, ref visited, isRoot: false);
    }

    private static string? OwnText(AutomationElement el, string role, bool isLeaf)
    {
        var name = Safe(() => el.Name);
        if (Safe(() => el.Properties.IsPassword.ValueOrDefault, false))
            return string.IsNullOrWhiteSpace(name) ? "[password field]" : $"{name}: [password field]";

        if (role is "Edit" or "ComboBox")
        {
            var value = Safe(() => el.Patterns.Value.IsSupported ? el.Patterns.Value.Pattern.Value.ValueOrDefault : null);
            if (value is null && role == "Edit") value = TextPatternText(el);
            if (string.IsNullOrEmpty(value) && string.IsNullOrWhiteSpace(name)) return null;
            return string.IsNullOrWhiteSpace(name) ? value : $"{name}: {value}";
        }

        if (role == "Document")
            return TextPatternText(el) ?? name;

        if (role is "CheckBox" or "RadioButton")
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var on = Safe(() => el.Patterns.Toggle.IsSupported ? el.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault.ToString() : null)
                     ?? (role == "RadioButton" ? (Safe(() => el.Patterns.SelectionItem.IsSupported && el.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault, false) ? "On" : "Off") : null);
            return on is null ? name : (on == "On" ? "[x] " : "[ ] ") + name;
        }

        if (string.IsNullOrWhiteSpace(name)) return null;
        return TextRoles.Contains(role) || isLeaf ? name : null;
    }

    private static string? TextPatternText(AutomationElement el)
    {
        // U+FFFC marks an embedded object (image, widget) inside rich text: noise for a reader.
        var t = Safe(() => el.Patterns.Text.IsSupported ? el.Patterns.Text.Pattern.DocumentRange.GetText(4000) : null)?.Replace("￼", "");
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    private static T? Safe<T>(Func<T> getter, T? fallback = default)
    {
        try { return getter(); }
        catch { return fallback; }
    }
}

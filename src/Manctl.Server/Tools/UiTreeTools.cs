using System.ComponentModel;
using Manctl.Core.Automation;
using Manctl.Core.Models;
using Manctl.Core.Win32;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

[McpServerToolType]
public static class UiTreeTools
{
    [McpServerTool(Name = "get_ui_tree"), Description(
        "Shows what is in a window as a tree of elements (role, name, value, position, enabled, supported actions) — far cheaper than a screenshot. " +
        "Call it before deciding what to click. Games, canvas and ImGui apps have almost no elements: use screenshot for those. " +
        "Without windowHandle/windowTitle it looks at the window in front. If the window shows no elements at all the answer is ok:false, errorCode NoElements (not an empty tree).")]
    public static object GetUiTree(
        AutomationContext ctx,
        [Description("\"window\" (default): the elements of one window. \"desktop\": the list of top-level windows, one level deep.")] string scope = "window",
        [Description("Window handle (number from list_windows).")] long? windowHandle = null,
        [Description("Part of the window title (alternative to windowHandle).")] string? windowTitle = null,
        [Description("How deep to go into the tree.")] int maxDepth = 10,
        [Description("true (default) = leave out unnamed, non-interactive parts to save space. false = everything.")] bool interactiveOnly = true)
    {
        var result = UiTreeService.GetTreeChecked(ctx, scope, windowHandle, windowTitle, maxDepth, interactiveOnly);
        if (result.Tree is not null) return result.Tree;
        return new TreeProblem(false, result.ErrorCode ?? ErrorCodes.WindowNotFound, result.Error ?? "No matching window found.", result.Window,
            result.ErrorCode == ErrorCodes.NoElements
                ? "Nothing to list here. Take a screenshot of the window (or an area) to see it, and use click(x, y) with screen coordinates. An empty tree does not mean the window is empty."
                : null);
    }

    /// <summary>get_ui_tree could not give a tree: said clearly, with the window it looked at.</summary>
    public sealed record TreeProblem(bool Ok, string ErrorCode, string Error, WindowInfo? Window, string? Hint);

    [McpServerTool(Name = "find_element"), Description(
        "Looks for elements by name, role or automationId and returns only the matches (with their state: enabled, value, position): " +
        "find_element(name=\"Remember me\", role=\"CheckBox\", windowHandle=9). Cheaper than get_ui_tree when you want one thing. " +
        "totalMatches tells you if there were more than maxResults. Nothing found is ok:false: ElementNotFound (the window is readable, the thing is not there) or NoElements (the window shows no elements at all).")]
    public static object FindElement(
        AutomationContext ctx,
        [Description("Element name (the text you see on it), case-insensitive part of the name.")] string? name = null,
        [Description("Element type, e.g. \"Button\", \"Edit\", \"CheckBox\", \"ComboBox\", \"ListItem\", \"MenuItem\", \"Text\".")] string? role = null,
        [Description("Exact AutomationId, if known (more reliable than the name).")] string? automationId = null,
        [Description("Window handle (number from list_windows). Omit both window fields to search all visible windows (slower).")] long? windowHandle = null,
        [Description("Part of the window title (alternative to windowHandle).")] string? windowTitle = null,
        [Description("At most this many matches are returned.")] int maxResults = 5)
    {
        var handle = windowHandle;
        if (handle is null && windowTitle is not null)
        {
            var window = WindowManager.FindWindow(null, windowTitle);
            if (window is null)
                return new OkResult(false, ErrorCodes.WindowNotFound, $"No window with '{windowTitle}' in the title.");
            handle = window.Handle;
        }
        return FindElementService.Find(ctx, handle, name, role, automationId, maxResults);
    }

    [McpServerTool(Name = "read_text"), Description(
        "Reads the text a window shows. The answer has one \"text\" string, a line per visual row, top to bottom: labels, buttons, field contents, menu and list items, document text (checkboxes as [x] / [ ]). " +
        "The cheap way to 'see' a window: read_text(windowHandle=55). Repeated texts stay repeated (two OK buttons = two lines). Password fields are never read. " +
        "To check for a word use contains: read_text(windowHandle=31, contains=\"Connected\") answers found true/false, matchCount and each match's role and screen point (x, y). " +
        "If the window shows no elements at all (game, canvas, ImGui) you get ok:false, errorCode NoElements: that does NOT mean the window is empty, take a screenshot.")]
    public static ReadTextResult ReadText(
        AutomationContext ctx,
        [Description("Window handle (number from list_windows). Omit both window fields to read the window in front.")] long? windowHandle = null,
        [Description("Part of the window title (alternative to windowHandle).")] string? windowTitle = null,
        [Description("Only look for this text (case-insensitive part of a text) and return the matches with their x, y instead of the whole text.")] string? contains = null,
        [Description("Longest text to return, in characters.")] int maxChars = 8000)
        => ReadTextService.Read(ctx, windowHandle, windowTitle, contains, maxChars);
}

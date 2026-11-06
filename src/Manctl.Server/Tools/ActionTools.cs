using System.ComponentModel;
using Manctl.Core.Automation;
using Manctl.Core.Models;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

[McpServerToolType]
public static class ActionTools
{
    private const string WindowHint = "Window: windowHandle (number from list_windows) or windowTitle (part of the title). Omit both to use the window in front.";

    [McpServerTool(Name = "wait_for_element"), Description(
        "Waits (on the server) until an element appears — use this instead of calling get_ui_tree/find_element in a loop. " +
        "Example: wait_for_element(name=\"Finish\", windowHandle=12, timeoutSeconds=180).")]
    public static Task<ActionResult> WaitForElement(
        AutomationContext ctx,
        [Description("Element name (the text you see on it), case-insensitive part of the name.")] string? name = null,
        [Description("Element type, e.g. \"Button\", \"Edit\", \"CheckBox\", \"ComboBox\", \"ListItem\", \"MenuItem\", \"Text\".")] string? role = null,
        [Description("Exact AutomationId of the element, if known.")] string? automationId = null,
        [Description("Window handle (number from list_windows). " + WindowHint)] long? windowHandle = null,
        [Description("Part of the window title (alternative to windowHandle).")] string? windowTitle = null,
        [Description("If several elements match, pick the Nth (0 = first).")] int index = 0,
        [Description("How long to wait, in seconds.")] double timeoutSeconds = 10,
        [Description("How often to re-check, in seconds.")] double pollSeconds = 0.3)
    {
        var selector = new ElementSelector { WindowHandle = windowHandle, WindowTitle = windowTitle, AutomationId = automationId, Text = name, Role = role, Index = index };
        if (!selector.HasElementCriteria)
            return Task.FromResult(NeedsElement("wait_for_element"));
        return ElementWaitService.WaitForElementAsync(ctx, selector, Ms(timeoutSeconds), Ms(pollSeconds));
    }

    [McpServerTool(Name = "click"), Description(
        "Clicks an element by name — click(name=\"Save\", windowHandle=1234) — OR a point on the screen — click(x=640, y=360). " +
        "Use x,y when the target has no name (games, canvas, ImGui apps); x,y are always SCREEN coordinates, even if windowHandle is given. " +
        "Give either an element (name/role/automationId) or x and y, not both.")]
    public static ActionResult Click(
        AutomationContext ctx,
        [Description("Element name (the text you see on it), case-insensitive part of the name.")] string? name = null,
        [Description("Element type, e.g. \"Button\", \"Edit\", \"CheckBox\", \"ComboBox\", \"ListItem\", \"MenuItem\".")] string? role = null,
        [Description("Exact AutomationId of the element, if known.")] string? automationId = null,
        [Description("Screen X to click (use together with y when the target has no name).")] int? x = null,
        [Description("Screen Y to click.")] int? y = null,
        [Description("Window handle (number from list_windows). " + WindowHint)] long? windowHandle = null,
        [Description("Part of the window title (alternative to windowHandle).")] string? windowTitle = null,
        [Description("If several elements match, pick the Nth (0 = first).")] int index = 0,
        [Description("\"left\" (default), \"right\" or \"middle\".")] string button = "left",
        [Description("true = double-click.")] bool doubleClick = false)
    {
        var selector = new ElementSelector { WindowHandle = windowHandle, WindowTitle = windowTitle, AutomationId = automationId, Text = name, Role = role, Index = index };
        var hasPoint = x is not null || y is not null;

        if (selector.HasElementCriteria && hasPoint)
            return Invalid("Give either an element (name/role/automationId) or a point (x and y), not both.");
        if (hasPoint)
        {
            if (x is null || y is null) return Invalid("A point needs both x and y.");
            return ActionService.ClickAt(ctx, x.Value, y.Value, ParseButton(button), doubleClick, windowHandle);
        }
        if (!selector.HasElementCriteria)
            return Invalid("Say what to click: an element (name, role or automationId) or a point (x and y).");

        return ActionService.Click(ctx, selector, ParseButton(button), doubleClick);
    }

    [McpServerTool(Name = "type_text"), Description(
        "Types text into an element: type_text(text=\"hello\", name=\"Search\", windowHandle=1234). 'text' is what gets typed; 'name' finds the element. " +
        "With no name/role/automationId it types into whatever has keyboard focus (in the given window); the answer names the window that received it in resolvedName.")]
    public static ActionResult TypeText(
        AutomationContext ctx,
        [Description("The text to type.")] string text,
        [Description("Element name (the text you see on it), case-insensitive part of the name.")] string? name = null,
        [Description("Element type, e.g. \"Edit\", \"Document\", \"ComboBox\".")] string? role = null,
        [Description("Exact AutomationId of the element, if known.")] string? automationId = null,
        [Description("Window handle (number from list_windows). " + WindowHint)] long? windowHandle = null,
        [Description("Part of the window title (alternative to windowHandle).")] string? windowTitle = null,
        [Description("If several elements match, pick the Nth (0 = first).")] int index = 0,
        [Description("true = send real key presses even if the element could be set directly.")] bool forceSendKeys = false)
    {
        var selector = new ElementSelector { WindowHandle = windowHandle, WindowTitle = windowTitle, AutomationId = automationId, Text = name, Role = role, Index = index };
        return selector.HasElementCriteria
            ? ActionService.TypeText(ctx, selector, text, forceSendKeys)
            : ActionService.TypeIntoFocused(ctx, text, windowHandle, windowTitle);
    }

    [McpServerTool(Name = "key_press"), Description(
        "Presses a key or key combination: key_press(keys=\"ctrl+s\"), keys=\"enter\", keys=\"alt+f4\", keys=\"win+r\", keys=\"tab\". " +
        "The keys go to the window in front (or the windowHandle you give); the answer names that window in resolvedName, so you can see where they went.")]
    public static ActionResult KeyPress(
        AutomationContext ctx,
        [Description("Keys joined with '+', e.g. \"ctrl+shift+s\", \"enter\", \"f5\".")] string keys,
        [Description("Window handle to bring to the front first (optional).")] long? windowHandle = null)
        => ActionService.KeyPress(ctx, keys, windowHandle);

    [McpServerTool(Name = "verify"), Description(
        "Checks one property of an element and answers ok true/false: verify(property=\"toggled\", expected=\"true\", name=\"Remember me\", windowHandle=9). " +
        "Cheaper than a screenshot to confirm that a click or typing worked.")]
    public static VerifyResult Verify(
        AutomationContext ctx,
        [Description("What to read: \"name\", \"value\", \"enabled\", \"toggled\" (checkbox on/off) or \"offscreen\".")] string property,
        [Description("Expected value as text. For toggled/enabled/offscreen use \"true\" or \"false\". If omitted, ok just means the property could be read.")] string? expected = null,
        [Description("Element name (the text you see on it), case-insensitive part of the name.")] string? name = null,
        [Description("Element type, e.g. \"CheckBox\", \"Edit\", \"Button\".")] string? role = null,
        [Description("Exact AutomationId of the element, if known.")] string? automationId = null,
        [Description("Window handle (number from list_windows).")] long? windowHandle = null,
        [Description("Part of the window title (alternative to windowHandle).")] string? windowTitle = null,
        [Description("If several elements match, pick the Nth (0 = first).")] int index = 0)
    {
        var selector = new ElementSelector { WindowHandle = windowHandle, WindowTitle = windowTitle, AutomationId = automationId, Text = name, Role = role, Index = index };
        if (!selector.HasElementCriteria)
            return new VerifyResult(false, property, expected, null, ErrorCodes.InvalidArgument);
        return ActionService.Verify(ctx, selector, property, expected);
    }

    private static int Ms(double seconds) => (int)Math.Clamp(Math.Round(seconds * 1000), 0, int.MaxValue);

    private static ActionResult Invalid(string message) =>
        new(false, "none", Error: message, ErrorCode: ErrorCodes.InvalidArgument);

    private static ActionResult NeedsElement(string tool) =>
        Invalid($"{tool} needs name, role or automationId to know which element to look for.");

    private static MouseButton ParseButton(string button) => button.ToLowerInvariant() switch
    {
        "right" => MouseButton.Right,
        "middle" => MouseButton.Middle,
        _ => MouseButton.Left,
    };
}

using System.ComponentModel;
using Manctl.Core.Models;
using Manctl.Core.Win32;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

[McpServerToolType]
public static class WindowTools
{
    [McpServerTool(Name = "list_windows"), Description(
        "Lists the visible windows: handle (number to reuse in other tools), title, class, process id, position and size, minimized/foreground. " +
        "Start here when you do not know a window's handle.")]
    public static List<WindowInfo> ListWindows() => WindowManager.ListWindows();

    [McpServerTool(Name = "focus_window"), Description(
        "Brings a window to the front (restores it if minimized): focus_window(windowHandle=1234) or focus_window(windowTitle=\"Notepad\"). " +
        "Windows often refuses to switch windows for a background program (it only flashes the taskbar button), so manctl tries up to seven ways in turn and checks each time. " +
        "ok:true means the window really is in front now (method says how). If all ways fail you get ok:false with the reason and what to try next — follow the hint before telling the user it cannot be done.")]
    public static FocusResult FocusWindow(
        [Description("Window handle (number from list_windows).")] long? windowHandle = null,
        [Description("Part of the window title (alternative to windowHandle).")] string? windowTitle = null)
    {
        if (windowHandle is null && windowTitle is null)
            return new FocusResult(false, ErrorCodes.InvalidArgument, "Say which window: windowHandle or windowTitle.");

        var window = WindowManager.FindWindow(windowHandle, windowTitle);
        if (window is null)
            return new FocusResult(false, ErrorCodes.WindowNotFound, "No matching window found.");

        var outcome = FocusService.Focus(window.Handle);
        return outcome.Ok
            ? new FocusResult(true, Method: outcome.Method)
            : new FocusResult(false, ErrorCodes.AccessDenied, outcome.Error, outcome.Hint, outcome.Method, outcome.FrontWindow);
    }

    [McpServerTool(Name = "get_foreground_window"), Description("Which window is in front right now (same fields as one list_windows entry). A quick check before sending keys or clicks.")]
    public static object GetForegroundWindow() =>
        (object?)WindowManager.FindWindow(null, null) ?? new OkResult(false, ErrorCodes.WindowNotFound, "There is no foreground window.");

    [McpServerTool(Name = "wait_for_window"), Description(
        "Waits (on the server) until a window appears or closes: wait_for_window(windowTitle=\"Setup\", mode=\"appears\", timeoutSeconds=120). " +
        "Answers matched:true plus windowHandle for later calls. If it does not happen in time: ok:false, errorCode:\"Timeout\", matched:false.")]
    public static Task<WindowWaitResult> WaitForWindow(
        [Description("Part of the window title.")] string? windowTitle = null,
        [Description("Exact window class name, if known (more reliable than the title).")] string? className = null,
        [Description("\"appears\" (wait until such a window exists) or \"closes\" (wait until it is gone).")] string mode = "appears",
        [Description("How long to wait, in seconds (max 1800).")] double timeoutSeconds = 60,
        [Description("How often to re-check, in seconds.")] double pollSeconds = 0.5)
        => WindowWaitService.WaitAsync(windowTitle, className, mode, (int)Math.Ceiling(timeoutSeconds), Units.Ms(pollSeconds));

    [McpServerTool(Name = "set_window_state"), Description(
        "Minimizes, maximizes or restores a window: set_window_state(state=\"maximized\", windowHandle=777). " +
        "state: \"minimized\", \"maximized\" or \"normal\" (restore to the usual size). The answer shows the state and bounds the window really ended up with.")]
    public static async Task<WindowChangeResult> SetWindowState(
        [Description("\"minimized\", \"maximized\" or \"normal\".")] string state,
        [Description("Window handle (number from list_windows).")] long? windowHandle = null,
        [Description("Part of the window title (alternative to windowHandle).")] string? windowTitle = null)
    {
        var want = state.Trim().ToLowerInvariant() switch
        {
            "minimized" or "minimize" or "min" => "minimized",
            "maximized" or "maximize" or "max" => "maximized",
            "normal" or "restore" or "restored" => "normal",
            _ => null,
        };
        if (want is null)
            return new WindowChangeResult(false, ErrorCodes.InvalidArgument, $"Unknown state '{state}'.", "state must be \"minimized\", \"maximized\" or \"normal\".");

        var (window, problem) = Find(windowHandle, windowTitle);
        if (window is null) return problem!;

        Func<long, bool> action = want switch { "minimized" => WindowManager.MinimizeWindow, "maximized" => WindowManager.MaximizeWindow, _ => WindowManager.RestoreWindowState };
        var called = action(window.Handle);
        await Task.Delay(250); // let Windows finish the animation before reading the result
        var actual = WindowManager.GetState(window.Handle);
        var bounds = WindowManager.GetWindowRectPhysical(window.Handle);

        if (!called || actual != want)
            return new WindowChangeResult(false, ErrorCodes.Unknown, $"The window is now {actual}, not {want}.",
                "Some windows refuse this (fixed-size dialogs cannot be maximized). list_windows shows the real state.", bounds, actual);
        return new WindowChangeResult(true, Bounds: bounds, State: actual);
    }

    [McpServerTool(Name = "set_window_bounds"), Description(
        "Moves and/or resizes a window in screen pixels. Give any of x, y (top-left corner) and width, height. " +
        "Example, left half of a 1920x1080 screen: set_window_bounds(x=0, y=0, width=960, height=1080, windowHandle=5). " +
        "A maximized or minimized window is restored first. The answer shows the bounds the window really got: " +
        "windows have minimum/maximum sizes, so it can differ from what you asked, and then ok is false.")]
    public static async Task<WindowChangeResult> SetWindowBounds(
        [Description("New left edge, screen pixels.")] int? x = null,
        [Description("New top edge, screen pixels.")] int? y = null,
        [Description("New width in pixels.")] int? width = null,
        [Description("New height in pixels.")] int? height = null,
        [Description("Window handle (number from list_windows).")] long? windowHandle = null,
        [Description("Part of the window title (alternative to windowHandle).")] string? windowTitle = null)
    {
        if (x is null && y is null && width is null && height is null)
            return new WindowChangeResult(false, ErrorCodes.InvalidArgument, "Nothing to change.", "Give at least one of x, y, width, height.");

        var (window, problem) = Find(windowHandle, windowTitle);
        if (window is null) return problem!;

        var handle = window.Handle;
        var current = WindowManager.GetWindowRectPhysical(handle);
        WindowManager.RestoreWindowState(handle);
        var called = true;
        if (x is not null || y is not null)
            called &= WindowManager.MoveWindowTo(handle, x ?? current.X, y ?? current.Y);
        if (width is not null || height is not null)
            called &= WindowManager.ResizeWindowTo(handle, width ?? current.Width, height ?? current.Height);
        await Task.Delay(200);
        var actual = WindowManager.GetWindowRectPhysical(handle);

        var diffs = new List<string>();
        if (x is { } wx && Math.Abs(actual.X - wx) > 2) diffs.Add($"x is {actual.X}, not {wx}");
        if (y is { } wy && Math.Abs(actual.Y - wy) > 2) diffs.Add($"y is {actual.Y}, not {wy}");
        if (width is { } ww && Math.Abs(actual.Width - ww) > 2) diffs.Add($"width is {actual.Width}, not {ww}");
        if (height is { } wh && Math.Abs(actual.Height - wh) > 2) diffs.Add($"height is {actual.Height}, not {wh}");

        if (!called || diffs.Count > 0)
            return new WindowChangeResult(false, ErrorCodes.Unknown, "The window did not end up as asked: " + (diffs.Count > 0 ? string.Join("; ", diffs) : "the Windows call failed") + ".",
                "The window probably has a minimum or maximum size, or lies partly off the screen. The bounds below are what it really has.", actual, WindowManager.GetState(handle));
        return new WindowChangeResult(true, Bounds: actual, State: WindowManager.GetState(handle));
    }

    [McpServerTool(Name = "close_window"), Description(
        "Closes a window politely (like the X button): close_window(windowTitle=\"Untitled - Notepad\"). " +
        "ok is true only if the window is really gone a moment later. If it is still there (a 'save changes?' question, or the program refuses) you get ok:false. " +
        "This is not a kill: to end a program use stop_process.")]
    public static async Task<WindowChangeResult> CloseWindow(
        [Description("Window handle (number from list_windows).")] long? windowHandle = null,
        [Description("Part of the window title (alternative to windowHandle).")] string? windowTitle = null)
    {
        var (window, problem) = Find(windowHandle, windowTitle);
        if (window is null) return problem!;

        if (!WindowManager.CloseWindow(window.Handle))
            return new WindowChangeResult(false, ErrorCodes.Unknown, "The close request could not be sent.", "The window may already be closing. Check list_windows.");

        for (var i = 0; i < 15; i++)
        {
            await Task.Delay(100);
            if (!WindowManager.Exists(window.Handle))
                return new WindowChangeResult(true, State: "closed");
        }
        return new WindowChangeResult(false, ErrorCodes.Timeout, $"The close request was sent, but window '{window.Title}' is still open.",
            "It is probably asking something (e.g. save changes?) or refuses to close. Call list_windows to look for a dialog and answer it; stop_process ends the program by force.",
            WindowManager.GetWindowRectPhysical(window.Handle), WindowManager.GetState(window.Handle));
    }

    private static (WindowInfo? Window, WindowChangeResult? Problem) Find(long? windowHandle, string? windowTitle)
    {
        if (windowHandle is null && windowTitle is null)
            return (null, new WindowChangeResult(false, ErrorCodes.InvalidArgument, "Say which window: windowHandle or windowTitle."));
        var window = WindowManager.FindWindow(windowHandle, windowTitle);
        return window is null
            ? (null, new WindowChangeResult(false, ErrorCodes.WindowNotFound, "No matching window found."))
            : (window, null);
    }
}

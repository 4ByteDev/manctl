using System.ComponentModel;
using Manctl.Core.Models;
using Manctl.Core.Win32;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

[McpServerToolType]
public static class MouseTools
{
    [McpServerTool(Name = "mouse_move"), Description(
        "Moves the mouse pointer to a point on the screen (screen pixels): mouse_move(x=500, y=300). " +
        "Rarely needed: click(x, y) and scroll(x, y) already move the pointer themselves.")]
    public static OkResult MouseMove(
        [Description("Screen X.")] int x,
        [Description("Screen Y.")] int y)
    {
        if (ScreenGuard.CheckPoint(x, y) is { } off) return Off(off);
        InputSimulator.MoveMouseTo(x, y);
        return new OkResult(true);
    }

    [McpServerTool(Name = "scroll"), Description(
        "Turns the mouse wheel. One notch = 120. UP is positive, DOWN is negative: 3 notches down = deltaY=-360. " +
        "Give x and y to scroll over a specific point, e.g. scroll(deltaY=-360, x=500, y=400); otherwise it scrolls where the pointer already is.")]
    public static OkResult Scroll(
        [Description("Vertical amount in multiples of 120. Positive = up, negative = down.")] int deltaY = 0,
        [Description("Horizontal amount in multiples of 120. Positive = right.")] int deltaX = 0,
        [Description("Screen X to scroll at (optional).")] int? x = null,
        [Description("Screen Y to scroll at (optional).")] int? y = null,
        [Description("Window handle to bring to the front first (optional).")] long? windowHandle = null)
    {
        if ((x is null) != (y is null))
            return new OkResult(false, ErrorCodes.InvalidArgument, "Give both x and y, or neither.");
        if (x is { } cx && y is { } cy && ScreenGuard.CheckPoint(cx, cy) is { } off) return Off(off);
        if (windowHandle is { } h && FocusService.Focus(h) is { Ok: false } stuck)
            return new OkResult(false, ErrorCodes.AccessDenied, stuck.Error, stuck.Hint);
        if (x is { } sx && y is { } sy) InputSimulator.MoveMouseTo(sx, sy);
        InputSimulator.Scroll(deltaY, deltaX);
        return new OkResult(true);
    }

    [McpServerTool(Name = "drag"), Description(
        "Presses the mouse button at one screen point, moves to another and releases: drag(fromX=400, fromY=300, toX=520, toY=300). " +
        "For sliders, moving things, selecting. To simply click a point use click(x, y), not drag.")]
    public static OkResult Drag(
        [Description("Start X (screen).")] int fromX,
        [Description("Start Y (screen).")] int fromY,
        [Description("End X (screen).")] int toX,
        [Description("End Y (screen).")] int toY,
        [Description("\"left\" (default), \"right\" or \"middle\".")] string button = "left")
    {
        var bad = ScreenGuard.CheckPoint(fromX, fromY, "start point") ?? ScreenGuard.CheckPoint(toX, toY, "end point");
        if (bad is not null) return Off(bad);
        InputSimulator.Drag(fromX, fromY, toX, toY, ParseButton(button));
        return new OkResult(true);
    }

    private static OkResult Off(string message) =>
        new(false, ErrorCodes.InvalidArgument, message, "Pick a point on the screen. list_monitors shows the screen area; a screenshot shows where things are.");

    private static MouseButton ParseButton(string button) => button.ToLowerInvariant() switch
    {
        "right" => MouseButton.Right,
        "middle" => MouseButton.Middle,
        _ => MouseButton.Left,
    };
}

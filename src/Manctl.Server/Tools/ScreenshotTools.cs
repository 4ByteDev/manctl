using System.ComponentModel;
using Manctl.Core.Models;
using Manctl.Core.Screenshots;
using Manctl.Core.Win32;
using Manctl.Server.Guard;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

[McpServerToolType]
public static class ScreenshotTools
{
    [McpServerTool(Name = "screenshot"), Description(
        "Takes a picture. A picture costs many tokens: prefer get_ui_tree / read text when text is enough, and use this for games, canvas, ImGui apps and visual checks. " +
        "screenshot() = the whole screen; screenshot(windowHandle=55) = one window; screenshot(x=100, y=200, width=300, height=40) = just that area (screen coordinates, the same as click). " +
        "scale makes it cheaper or bigger: scale=0.5 is half size, scale=2 enlarges tiny text. To read small text: pick a small area and scale=2. " +
        "If the picture is one flat color (a black game window, a covered or protected window) a short note says so.")]
    public static CallToolResponse Screenshot(
        [Description("Window handle (number from list_windows) to photograph only that window.")] long? windowHandle = null,
        [Description("Part of the window title (alternative to windowHandle).")] string? windowTitle = null,
        [Description("Area: left edge in SCREEN pixels (use with y, width, height).")] int? x = null,
        [Description("Area: top edge in SCREEN pixels.")] int? y = null,
        [Description("Area: width in pixels.")] int? width = null,
        [Description("Area: height in pixels.")] int? height = null,
        [Description("Size factor from 0.05 to 8. 1 = original, 0.5 = half (cheap), 2 = twice as big (to read tiny text).")] double scale = 1.0,
        [Description("\"png\" (default, sharp) or \"jpeg\" (smaller file).")] string format = "png",
        [Description("JPEG quality 1-100 (only for jpeg).")] int quality = 80)
    {
        var options = new ImageOptions(scale, format, quality);
        if (options.Validate() is { } badOption)
            return Error(ErrorCodes.InvalidArgument, badOption);

        var givenArea = new[] { x, y, width, height }.Count(v => v is not null);
        if (givenArea is not (0 or 4))
            return Error(ErrorCodes.InvalidArgument, "An area needs all four of x, y, width and height (or none of them).");
        if (width is <= 0 || height is <= 0)
            return Error(ErrorCodes.InvalidArgument, "width and height must be bigger than 0.");

        Rect? crop = givenArea == 4 ? new Rect(x!.Value, y!.Value, width!.Value, height!.Value) : null;

        Shot shot;
        Rect captured;
        if (windowHandle is not null || windowTitle is not null)
        {
            var window = WindowManager.FindWindow(windowHandle, windowTitle);
            if (window is null)
                return Error(ErrorCodes.WindowNotFound, "No matching window found.");
            if (window.IsMinimized)
                return ToolErrors.Response(ErrorCodes.InvalidArgument, $"Window '{window.Title}' is minimized, so there is nothing to photograph.",
                    $"Restore it first: set_window_state(state=\"normal\", windowHandle={window.Handle}).", "screenshot");
            captured = window.Bounds;
            shot = ScreenCapture.CaptureWindow(window.Handle, window.Bounds, crop, options);
        }
        else
        {
            captured = ScreenCapture.GetDesktopBounds();
            shot = ScreenCapture.CaptureDesktop(crop, options);
        }

        if (shot.Image is null)
        {
            var c = crop!;
            return ToolErrors.Response(ErrorCodes.InvalidArgument,
                $"The area (x={c.X}, y={c.Y}, {c.Width}x{c.Height}) is outside the picture, which covers screen x={captured.X}..{captured.Right}, y={captured.Y}..{captured.Bottom}.",
                "x and y are SCREEN coordinates, not coordinates inside the window. " +
                $"To take a part inside this picture add its origin: x = {captured.X} + (x inside), y = {captured.Y} + (y inside).",
                "screenshot");
        }

        var response = new CallToolResponse
        {
            Content = [new Content { Type = "image", MimeType = options.MimeType, Data = Convert.ToBase64String(shot.Image) }],
        };
        var notes = new List<string>();
        if (shot.UsedScreenCopy)
            notes.Add("The window's own capture came back blank, so this is a plain copy of the screen where the window is: anything covering the window is in the picture.");
        if (shot.Flat)
            notes.Add("The picture is one flat color. The window may be black by design (a game that draws with the GPU), covered, still loading, or protected against screenshots; the screen may be off or locked.");
        if (notes.Count > 0)
            response.Content.Add(new Content { Type = "text", Text = "Note: " + string.Join(" ", notes) });
        return response;
    }

    private static CallToolResponse Error(string code, string message) => ToolErrors.Response(code, message, null, "screenshot");
}

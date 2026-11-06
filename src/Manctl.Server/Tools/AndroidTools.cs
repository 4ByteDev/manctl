using System.ComponentModel;
using Manctl.Core.Android;
using Manctl.Core.Automation;
using Manctl.Core.Models;
using Manctl.Core.Screenshots;
using Manctl.Server.Guard;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

/// <summary>Everything here works on the Android phone/emulator through adb. Coordinates are DEVICE pixels (the same as
/// android_ui_tree and android_screenshot). deviceSerial is always optional: leave it out when only one device is connected.</summary>
[McpServerToolType]
public static class AndroidTools
{
    private const string Serial = "Device serial from android_list_devices, e.g. \"emulator-5554\". Leave out if only one device is connected.";

    [McpServerTool(Name = "android_list_devices"), Description("Lists the connected Android phones/emulators: serial, state (device = ready; offline/unauthorized = not usable), model.")]
    public static Task<List<AndroidDevice>> AndroidListDevices() => AdbClient.ListDevicesAsync();

    [McpServerTool(Name = "android_ui_tree"), Description(
        "Shows what is on the Android screen as a tree of elements (same format as get_ui_tree): name, role, resource id, position in device pixels, enabled, supported actions. " +
        "Much cheaper than android_screenshot. Call it before tapping.")]
    public static async Task<object> AndroidUiTree(
        [Description("true (default) = leave out unnamed, non-interactive layout wrappers to save space. false = everything.")] bool interactiveOnly = true,
        [Description(Serial)] string? deviceSerial = null)
    {
        var tree = await AndroidUiService.GetUiTreeAsync(deviceSerial, interactiveOnly);
        if (tree is null)
            return new OkResult(false, ErrorCodes.Unknown, "uiautomator dump failed or returned no data.",
                "Is a device connected and unlocked? Call android_list_devices; if there are several, pass deviceSerial.");
        if (!AndroidUiService.HasMeaningfulElements(tree))
            return new OkResult(false, ErrorCodes.NoElements, "The Android screen exposes no named or clickable elements (black or secure screen, still loading, or a game).",
                "Nothing to list. Take android_screenshot to see the screen and tap with android_click(x, y). An empty tree does not mean the screen is empty.");
        return tree;
    }

    [McpServerTool(Name = "android_wait_for_element"), Description(
        "Waits (on the server) until an element appears on the Android screen: android_wait_for_element(name=\"Finish\", timeoutSeconds=60). " +
        "Each check is a full screen dump, so do not poll faster than every 0.8 seconds.")]
    public static Task<ActionResult> AndroidWaitForElement(
        [Description("Element text or description, case-insensitive part of it.")] string? name = null,
        [Description("Element type, e.g. \"Button\", \"EditText\", \"TextView\".")] string? role = null,
        [Description("Exact resource id of the element.")] string? automationId = null,
        [Description("If several elements match, pick the Nth (0 = first).")] int index = 0,
        [Description("How long to wait, in seconds.")] double timeoutSeconds = 10,
        [Description("How often to re-check, in seconds (minimum 0.3).")] double pollSeconds = 0.8,
        [Description(Serial)] string? deviceSerial = null)
    {
        var selector = new ElementSelector { AutomationId = automationId, Text = name, Role = role, Index = index };
        if (!selector.HasElementCriteria)
            return Task.FromResult(new ActionResult(false, "none", Error: "android_wait_for_element needs name, role or automationId.", ErrorCode: ErrorCodes.InvalidArgument));
        return AndroidWaitService.WaitForElementAsync(deviceSerial, selector, Units.Ms(timeoutSeconds), Units.Ms(pollSeconds));
    }

    [McpServerTool(Name = "android_verify"), Description(
        "Checks one property of an Android element and answers ok true/false: android_verify(property=\"toggled\", expected=\"true\", name=\"Remember me\"). " +
        "Properties: \"name\", \"value\", \"enabled\", \"toggled\" (checked). \"offscreen\" is not available on Android.")]
    public static Task<VerifyResult> AndroidVerify(
        [Description("What to read: \"name\", \"value\", \"enabled\" or \"toggled\".")] string property,
        [Description("Expected value as text. For toggled/enabled use \"true\" or \"false\".")] string? expected = null,
        [Description("Element text or description, case-insensitive part of it.")] string? name = null,
        [Description("Element type, e.g. \"CheckBox\", \"Switch\", \"Button\".")] string? role = null,
        [Description("Exact resource id of the element.")] string? automationId = null,
        [Description("If several elements match, pick the Nth (0 = first).")] int index = 0,
        [Description(Serial)] string? deviceSerial = null)
    {
        var selector = new ElementSelector { AutomationId = automationId, Text = name, Role = role, Index = index };
        if (!selector.HasElementCriteria)
            return Task.FromResult(new VerifyResult(false, property, expected, null, ErrorCodes.InvalidArgument));
        return AndroidUiService.VerifyAsync(deviceSerial, selector, property, expected);
    }

    [McpServerTool(Name = "android_screenshot"), Description(
        "Takes a picture of the Android screen. Costs many tokens: prefer android_ui_tree when text is enough. " +
        "android_screenshot() = whole screen; android_screenshot(x=0, y=1000, width=1080, height=300) = just that area (device pixels); " +
        "scale=0.5 makes it half size (cheap), scale=2 enlarges tiny text.")]
    public static async Task<CallToolResponse> AndroidScreenshot(
        [Description("Area: left edge in device pixels (use with y, width, height).")] int? x = null,
        [Description("Area: top edge in device pixels.")] int? y = null,
        [Description("Area: width in pixels.")] int? width = null,
        [Description("Area: height in pixels.")] int? height = null,
        [Description("Size factor from 0.05 to 8. 1 = original, 0.5 = half (cheap), 2 = twice as big.")] double scale = 1.0,
        [Description("\"png\" (default) or \"jpeg\" (smaller).")] string format = "png",
        [Description("JPEG quality 1-100 (only for jpeg).")] int quality = 80,
        [Description(Serial)] string? deviceSerial = null)
    {
        var options = new ImageOptions(scale, format, quality);
        if (options.Validate() is { } bad)
            return ToolErrors.Response(ErrorCodes.InvalidArgument, bad, null, "android_screenshot");
        var givenArea = new[] { x, y, width, height }.Count(v => v is not null);
        if (givenArea is not (0 or 4))
            return ToolErrors.Response(ErrorCodes.InvalidArgument, "An area needs all four of x, y, width and height (or none of them).", null, "android_screenshot");

        var png = await AndroidScreenshotService.CaptureAsync(deviceSerial);
        if (png is null)
        {
            return ToolErrors.Response(ErrorCodes.Unknown,
                "screencap failed or returned no data.",
                "Is a device connected and unlocked? Call android_list_devices to see devices and their state (offline/unauthorized/device); if there are several, pass deviceSerial.",
                "android_screenshot");
        }

        Rect? crop = givenArea == 4 ? new Rect(x!.Value, y!.Value, width!.Value, height!.Value) : null;
        var image = ImageEncoding.Reencode(png, crop, options);
        if (image is null)
            return ToolErrors.Response(ErrorCodes.InvalidArgument, "The area is outside the screen picture.",
                "Take android_screenshot() once without an area to see the size, then pick an area inside it (device pixels).", "android_screenshot");

        var response = new CallToolResponse
        {
            Content = [new Content { Type = "image", MimeType = options.MimeType, Data = Convert.ToBase64String(image) }],
        };
        if (ImageEncoding.IsFlat(png))
            response.Content.Add(new Content { Type = "text", Text = "Note: the picture is one flat color. The screen may be off or locked, or the app blocks screenshots (secure window)." });
        return response;
    }

    [McpServerTool(Name = "android_install_latest_apk"), Description(
        "Installs the newest file matching a pattern from a folder (handy when the build number is in the file name): " +
        "android_install_latest_apk(directory=\"E:\\\\build\\\\apk\") installs the newest *.apk there.")]
    public static Task<InstallResult> AndroidInstallLatestApk(
        [Description("Folder to look in.")] string directory,
        [Description("File name pattern, e.g. \"app-*-debug.apk\". Default \"*.apk\".")] string pattern = "*.apk",
        [Description("true (default) = replace an existing install, keeping its data.")] bool reinstall = true,
        [Description("How long to wait for the install, in seconds.")] double timeoutSeconds = 60,
        [Description(Serial)] string? deviceSerial = null)
        => AndroidInstallService.InstallLatestMatchingAsync(directory, pattern, deviceSerial, reinstall, Units.Ms(timeoutSeconds));

    [McpServerTool(Name = "android_click"), Description(
        "Taps an element by name — android_click(name=\"Login\") — OR a point on the device screen — android_click(x=540, y=1200). " +
        "Use x,y only when the target has no name; x,y are device pixels (same as android_ui_tree). Give either an element or x and y, not both.")]
    public static async Task<ActionResult> AndroidClick(
        [Description("Element text or description, case-insensitive part of it.")] string? name = null,
        [Description("Element type, e.g. \"Button\", \"EditText\", \"TextView\".")] string? role = null,
        [Description("Exact resource id of the element.")] string? automationId = null,
        [Description("Device X to tap (use together with y).")] int? x = null,
        [Description("Device Y to tap.")] int? y = null,
        [Description("If several elements match, pick the Nth (0 = first).")] int index = 0,
        [Description(Serial)] string? deviceSerial = null)
    {
        var selector = new ElementSelector { AutomationId = automationId, Text = name, Role = role, Index = index };
        var hasPoint = x is not null || y is not null;

        if (selector.HasElementCriteria && hasPoint)
            return new ActionResult(false, "none", Error: "Give either an element (name/role/automationId) or a point (x and y), not both.", ErrorCode: ErrorCodes.InvalidArgument);
        if (hasPoint)
        {
            if (x is null || y is null)
                return new ActionResult(false, "none", Error: "A point needs both x and y.", ErrorCode: ErrorCodes.InvalidArgument);
            var tap = await AndroidInputService.TapAsync(deviceSerial, x.Value, y.Value);
            return new ActionResult(tap.Success, "tap", Error: tap.Success ? null : tap.StdErr, ErrorCode: tap.Success ? null : ErrorCodes.Unknown);
        }
        if (!selector.HasElementCriteria)
            return new ActionResult(false, "none", Error: "Say what to tap: an element (name, role or automationId) or a point (x and y).", ErrorCode: ErrorCodes.InvalidArgument);

        return await AndroidUiService.ClickAsync(deviceSerial, selector);
    }

    [McpServerTool(Name = "android_swipe"), Description(
        "Swipes/drags a finger from one device point to another: android_swipe(x1=540, y1=1600, x2=540, y2=600) scrolls a list down. " +
        "A longer durationSeconds is a slow drag, a short one a flick.")]
    public static Task<AdbResult> AndroidSwipe(
        [Description("Start X (device pixels).")] int x1,
        [Description("Start Y.")] int y1,
        [Description("End X.")] int x2,
        [Description("End Y.")] int y2,
        [Description("How long the swipe lasts, in seconds.")] double durationSeconds = 0.3,
        [Description(Serial)] string? deviceSerial = null)
        => AndroidInputService.SwipeAsync(deviceSerial, x1, y1, x2, y2, Units.Ms(durationSeconds));

    [McpServerTool(Name = "android_key_event"), Description(
        "Presses an Android key: android_key_event(keyCode=\"KEYCODE_BACK\"). Others: KEYCODE_HOME, KEYCODE_ENTER, KEYCODE_DPAD_DOWN, KEYCODE_DPAD_CENTER, or a number. " +
        "Note: this is adb 'input keyevent' — it does not reproduce real TV remote focus behavior exactly.")]
    public static Task<AdbResult> AndroidKeyEvent(
        [Description("Key name such as \"KEYCODE_BACK\", or its number.")] string keyCode,
        [Description(Serial)] string? deviceSerial = null)
        => AndroidInputService.KeyEventAsync(deviceSerial, keyCode);

    [McpServerTool(Name = "android_input_text"), Description(
        "Types text into the focused Android field. Only plain ASCII works (no Cyrillic, CJK or emoji) — that is an Android limit of 'input text'.")]
    public static async Task<object> AndroidInputText(
        [Description("Text to type (ASCII only).")] string text,
        [Description(Serial)] string? deviceSerial = null)
    {
        if (text.Any(c => c > 126 || (c < 32 && c != '\n' && c != '\t')))
            return new OkResult(false, ErrorCodes.InvalidArgument, "Only plain ASCII text can be typed on Android: this text has other characters (Cyrillic, emoji, ...).",
                "This is a limit of Android's 'input text'. Type ASCII only, or put the text into an app another way (for example a field filled by the app itself).");
        return await AndroidInputService.InputTextAsync(deviceSerial, text);
    }

    [McpServerTool(Name = "android_find"), Description(
        "Is this text on the Android screen, and where? android_find(text=\"Login\") answers found true/false, matchCount and each match's role and device point (x, y) " +
        "(texts and content descriptions). Cheaper than android_ui_tree. Several matches: tap the one you want with android_click(x, y). " +
        "If the screen exposes no elements (black/secure screen, game) you get ok:false, errorCode NoElements — take android_screenshot instead.")]
    public static Task<AndroidFindResult> AndroidFind(
        [Description("Text to look for, case-insensitive part of it.")] string text,
        [Description(Serial)] string? deviceSerial = null)
        => AndroidFileService.FindAsync(text, deviceSerial);

    [McpServerTool(Name = "android_logcat"), Description(
        "Reads the latest lines of the Android log: android_logcat(lines=100), only errors: level=\"E\", only lines with a word: filter=\"MyApp\". " +
        "To see what one action causes: android_logcat(clear=true), do the action, then android_logcat() again.")]
    public static Task<LogcatResult> AndroidLogcat(
        [Description("How many of the latest lines (1-5000).")] int lines = 200,
        [Description("Only lines containing this text (case-insensitive), e.g. an app or tag name.")] string? filter = null,
        [Description("Lowest level to show: V, D, I, W, E or F (E = errors and worse).")] string? level = null,
        [Description("true = empty the log buffer instead of reading it.")] bool clear = false,
        [Description(Serial)] string? deviceSerial = null)
        => AndroidFileService.LogcatAsync(lines, filter, level, clear, deviceSerial);

    [McpServerTool(Name = "android_pull"), Description(
        "Copies a file or folder FROM the Android device to this PC: android_pull(devicePath=\"/sdcard/Download/a.png\", localPath=\"E:\\\\out\\\\a.png\"). " +
        "For a private file of your own debuggable app add app=\"com.example.app\" (uses run-as), e.g. devicePath=\"files/configs/main.json\".")]
    public static Task<TransferResult> AndroidPull(
        [Description("Full path on the device, e.g. /sdcard/Download/a.png (with app: path inside the app folder).")] string devicePath,
        [Description("Where to save it on this PC (missing folders are created).")] string localPath,
        [Description("Package name of a debuggable app, to read its private files with run-as.")] string? app = null,
        [Description(Serial)] string? deviceSerial = null)
        => AndroidFileService.PullAsync(devicePath, localPath, deviceSerial, app);

    [McpServerTool(Name = "android_push"), Description(
        "Copies a file or folder FROM this PC TO the Android device: android_push(localPath=\"C:\\\\a.apk\", devicePath=\"/sdcard/a.apk\"). " +
        "Use /sdcard/ or /storage/emulated/0/ for plain files; to install an app use android_install_latest_apk.")]
    public static Task<TransferResult> AndroidPush(
        [Description("Full path of the file or folder on this PC.")] string localPath,
        [Description("Full path on the device, e.g. /sdcard/Download/a.apk.")] string devicePath,
        [Description(Serial)] string? deviceSerial = null)
        => AndroidFileService.PushAsync(localPath, devicePath, deviceSerial);
}

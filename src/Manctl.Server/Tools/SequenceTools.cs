using System.ComponentModel;
using Manctl.Core.Automation;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

[McpServerToolType]
public static class SequenceTools
{
    [McpServerTool(Name = "run_sequence"), Description(
        "Does several steps in ONE call (click, type_text, key_press, wait_for_element, verify, mouse_move, drag, scroll) and stops at the first failure. " +
        "Give the window once at the top and it applies to every step. Example: " +
        "run_sequence(windowHandle=42, steps=[{action:\"click\", name:\"Name\"}, {action:\"type_text\", text:\"Ivan\"}, {action:\"key_press\", keys:\"tab\"}, {action:\"click\", name:\"OK\"}]). " +
        "In a step, 'name' finds an element and 'text' is what gets typed; a type_text step without name types into the focused field; a click step with x and y clicks that screen point. " +
        "Blocking the user's mouse/keyboard (lockInput) only works if manctl runs as Administrator: check inputLocked in the answer.")]
    public static Task<SequenceResult> RunSequence(
        AutomationContext ctx,
        [Description("The steps, in order.")] List<SequenceStep> steps,
        [Description("Window handle used by every step that has none of its own.")] long? windowHandle = null,
        [Description("Part of the window title used by every step that has none of its own.")] string? windowTitle = null,
        [Description("true (default) = stop at the first failing step. false = run all steps anyway.")] bool stopOnError = true,
        [Description("true (default) = block the user's own mouse/keyboard while the steps run (needs Administrator, see inputLocked).")] bool lockInput = true)
        => SequenceRunner.RunAsync(ctx, steps, stopOnError, lockInput, windowHandle, windowTitle);

    [McpServerTool(Name = "handle_file_dialog"), Description(
        "Fills in a Windows Open/Save file dialog and presses its button: handle_file_dialog(path=\"C:\\\\out\\\\report.txt\", action=\"save\"). " +
        "Waits for the dialog to appear if it is not open yet.")]
    public static Task<FileDialogResult> HandleFileDialog(
        AutomationContext ctx,
        [Description("Full path of the file to open or save.")] string path,
        [Description("\"open\" or \"save\": which button to press.")] string action,
        [Description("How long to wait for the dialog to appear, in seconds.")] double timeoutSeconds = 15)
        => FileDialogService.HandleAsync(ctx, path, action, (int)Math.Ceiling(timeoutSeconds));
}

using System.ComponentModel;

namespace Manctl.Core.Automation;

/// <summary>One step of a run_sequence batch. Flat and typed rather than a free-form {action, params} bag, so the
/// JSON schema an agent sees is explicit. Field names are the same as the standalone tools: "name" is always how
/// an element is found, "text" is always the text that gets typed.</summary>
public sealed class SequenceStep
{
    [Description("\"click\", \"type_text\", \"key_press\", \"wait_for_element\", \"verify\", \"mouse_move\", \"drag\", or \"scroll\".")]
    public required string Action { get; init; }

    [Description("Window handle for this step. Not needed if windowHandle/windowTitle was given once for the whole sequence.")]
    public long? WindowHandle { get; init; }
    [Description("Part of the window title for this step (alternative to windowHandle).")]
    public string? WindowTitle { get; init; }

    [Description("Element: name (visible text), case-insensitive substring. For click, type_text, wait_for_element, verify.")]
    public string? Name { get; init; }
    [Description("Element: exact AutomationId.")]
    public string? AutomationId { get; init; }
    [Description("Element: control type, e.g. \"Button\", \"Edit\", \"CheckBox\".")]
    public string? Role { get; init; }
    [Description("Element: if several match, pick the Nth (0-based).")]
    public int Index { get; init; } = 0;

    [Description("click/drag: \"left\" (default), \"right\" or \"middle\".")]
    public string Button { get; init; } = "left";
    [Description("click: send a double-click instead of a single click.")]
    public bool DoubleClick { get; init; } = false;

    [Description("type_text: the text to type. (To find an element use name, not text.)")]
    public string? Text { get; init; }
    [Description("type_text: force real keyboard input even if the element supports direct value-setting.")]
    public bool ForceSendKeys { get; init; } = false;

    [Description("key_press: key combo, '+'-separated, e.g. \"ctrl+shift+s\".")]
    public string? Keys { get; init; }

    [Description("wait_for_element: max time to wait, in seconds.")]
    public double TimeoutSeconds { get; init; } = 10;
    [Description("wait_for_element: how often to re-check, in seconds.")]
    public double PollSeconds { get; init; } = 0.3;

    [Description("verify: property to read — name, value, enabled, toggled or offscreen.")]
    public string? Property { get; init; }
    [Description("verify: expected value as text; for toggled/enabled/offscreen use \"true\" or \"false\". If omitted, success just means the property was readable.")]
    public string? Expected { get; init; }

    [Description("click: screen X to click when there is no element name (games, canvas). mouse_move: target X. scroll: X to scroll at.")]
    public int? X { get; init; }
    [Description("click: screen Y (see x). mouse_move: target Y. scroll: Y to scroll at.")]
    public int? Y { get; init; }

    [Description("drag: start X.")]
    public int FromX { get; init; }
    [Description("drag: start Y.")]
    public int FromY { get; init; }
    [Description("drag: end X.")]
    public int ToX { get; init; }
    [Description("drag: end Y.")]
    public int ToY { get; init; }

    [Description("scroll: horizontal amount, multiples of 120.")]
    public int DeltaX { get; init; }
    [Description("scroll: vertical amount, multiples of 120; positive scrolls up, negative down.")]
    public int DeltaY { get; init; }
}

public sealed record SequenceStepResult(int Index, string Action, bool Success, object? Result, string? Error, string? ErrorCode = null);

/// <summary>InputLocked reports whether lockInput was actually engaged — BlockInput requires manctl to be running
/// elevated (as Administrator); on a non-elevated process it fails with ACCESS_DENIED and the sequence still
/// proceeds, but with no real protection against the user's own input interfering. False here (with lockInput
/// requested) means exactly that: the steps ran, but unprotected.</summary>
public sealed record SequenceResult(bool Success, int? StoppedAtStep, List<SequenceStepResult> Steps, bool InputLocked = false, string? Warning = null);

using Manctl.Core.Models;
using Manctl.Core.Win32;

namespace Manctl.Core.Automation;

/// <summary>Dispatches each step to the existing single-action services — no duplicated automation logic, just
/// routing plus stop-on-first-failure bookkeeping.</summary>
public static class SequenceRunner
{
    public static async Task<SequenceResult> RunAsync(
        AutomationContext ctx, List<SequenceStep> steps, bool stopOnError, bool lockInput = true,
        long? defaultWindowHandle = null, string? defaultWindowTitle = null)
    {
        // BlockInput requires manctl to run elevated; on a non-elevated process it fails with ACCESS_DENIED.
        // That failure must stay visible (via InputLocked below), not swallowed — the sequence still runs, but
        // without the protection the caller asked for.
        var inputLocked = lockInput && InputLockService.Lock();

        try
        {
            var results = new List<SequenceStepResult>();
            int? stoppedAt = null;

            for (var i = 0; i < steps.Count; i++)
            {
                SequenceStepResult stepResult;
                try
                {
                    stepResult = await ExecuteStep(ctx, i, steps[i], defaultWindowHandle, defaultWindowTitle);
                }
                catch (Exception ex)
                {
                    stepResult = new SequenceStepResult(i, steps[i].Action, false, null, ex.Message, ErrorCode: ErrorCodes.Unknown);
                }

                results.Add(stepResult);

                if (!stepResult.Success && stopOnError)
                {
                    stoppedAt = i;
                    break;
                }
            }

            return new SequenceResult(results.All(r => r.Success), stoppedAt, results, InputLocked: inputLocked,
                Warning: lockInput && !inputLocked
                    ? "The user's mouse and keyboard were NOT blocked (manctl is not running as Administrator). If the user touches them, steps can go to the wrong place."
                    : null);
        }
        finally
        {
            if (inputLocked)
                InputLockService.Unlock();
        }
    }

    private static async Task<SequenceStepResult> ExecuteStep(AutomationContext ctx, int index, SequenceStep step, long? defaultHandle, string? defaultTitle)
    {
        // A step's own window wins; otherwise the one given once for the whole sequence.
        var handle = step.WindowHandle ?? (step.WindowTitle is null ? defaultHandle : null);
        var title = step.WindowTitle ?? (step.WindowHandle is null ? defaultTitle : null);

        var selector = new ElementSelector
        {
            WindowHandle = handle,
            WindowTitle = title,
            AutomationId = step.AutomationId,
            Text = step.Name,
            Role = step.Role,
            Index = step.Index,
        };

        SequenceStepResult Bad(string message) => new(index, step.Action, false, null, message, ErrorCodes.InvalidArgument);
        SequenceStepResult FromAction(ActionResult r) => new(index, step.Action, r.Success, r, r.Error, r.ErrorCode);

        switch (step.Action.ToLowerInvariant())
        {
            case "click":
                if (selector.HasElementCriteria)
                    return FromAction(ActionService.Click(ctx, selector, ParseButton(step.Button), step.DoubleClick));
                if (step.X is { } cx && step.Y is { } cy)
                    return FromAction(ActionService.ClickAt(ctx, cx, cy, ParseButton(step.Button), step.DoubleClick, handle));
                return Bad("click step needs an element (name, role or automationId) or a point (x and y).");

            case "type_text":
                if (step.Text is null)
                    return Bad("type_text step requires 'text' (the text to type). To find the element use 'name'.");
                return FromAction(selector.HasElementCriteria
                    ? ActionService.TypeText(ctx, selector, step.Text, step.ForceSendKeys)
                    : ActionService.TypeIntoFocused(ctx, step.Text, handle, title));

            case "key_press":
            {
                if (step.Keys is null)
                    return Bad("key_press step requires 'keys', e.g. \"ctrl+s\".");
                return FromAction(ActionService.KeyPress(ctx, step.Keys, handle));
            }

            case "wait_for_element":
            {
                if (!selector.HasElementCriteria)
                    return Bad("wait_for_element step needs name, role or automationId.");
                var r = await ElementWaitService.WaitForElementAsync(ctx, selector, (int)(step.TimeoutSeconds * 1000), (int)(step.PollSeconds * 1000));
                return FromAction(r);
            }

            case "verify":
            {
                if (step.Property is null)
                    return Bad("verify step requires 'property' (name, value, enabled, toggled or offscreen).");
                if (!selector.HasElementCriteria)
                    return Bad("verify step needs name, role or automationId to find the element.");
                var r = ActionService.Verify(ctx, selector, step.Property, step.Expected);
                var error = r.Success ? null : $"Expected {r.Property}={r.Expected ?? "<any>"}, got {r.Actual ?? "<null>"}";
                return new SequenceStepResult(index, step.Action, r.Success, r, error, r.ErrorCode);
            }

            case "mouse_move":
                if (step.X is not { } mx || step.Y is not { } my)
                    return Bad("mouse_move step requires x and y.");
                if (ScreenGuard.CheckPoint(mx, my) is { } offMove) return Bad(offMove);
                InputSimulator.MoveMouseTo(mx, my);
                return new SequenceStepResult(index, step.Action, true, null, null);

            case "drag":
                var offDrag = ScreenGuard.CheckPoint(step.FromX, step.FromY, "start point") ?? ScreenGuard.CheckPoint(step.ToX, step.ToY, "end point");
                if (offDrag is not null) return Bad(offDrag);
                InputSimulator.Drag(step.FromX, step.FromY, step.ToX, step.ToY, ParseButton(step.Button));
                return new SequenceStepResult(index, step.Action, true, null, null);

            case "scroll":
                if (handle is { } sh && FocusService.Focus(sh) is { Ok: false } stuck)
                    return new SequenceStepResult(index, step.Action, false, null, stuck.Error, ErrorCodes.AccessDenied);
                if (step.X is { } sx && step.Y is { } sy)
                {
                    if (ScreenGuard.CheckPoint(sx, sy) is { } offScroll) return Bad(offScroll);
                    InputSimulator.MoveMouseTo(sx, sy);
                }
                InputSimulator.Scroll(step.DeltaY, step.DeltaX);
                return new SequenceStepResult(index, step.Action, true, null, null);

            default:
                return Bad($"Unknown action '{step.Action}'. Use click, type_text, key_press, wait_for_element, verify, mouse_move, drag or scroll.");
        }
    }

    private static MouseButton ParseButton(string button) => button.ToLowerInvariant() switch
    {
        "right" => MouseButton.Right,
        "middle" => MouseButton.Middle,
        _ => MouseButton.Left,
    };
}

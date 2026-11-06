using FlaUI.Core.Definitions;
using Manctl.Core.Models;
using Manctl.Core.Win32;

namespace Manctl.Core.Automation;

public sealed record FileDialogResult(bool Ok, bool DialogFound, string? Error = null, string? ErrorCode = null);

/// <summary>Native Open/Save common file dialogs are a separate system window (class #32770), not part of the
/// calling app's own UI tree — clicking through them via tree-folder navigation is fragile across machines and
/// themes. This sets the filename field directly and invokes the accept button by AutomationId "1" (IDOK in the
/// standard dialog template) — language- and theme-independent, unlike matching localized button text.</summary>
public static class FileDialogService
{
    public static async Task<FileDialogResult> HandleAsync(AutomationContext ctx, string path, string action, int timeoutSeconds)
    {
        var normalizedAction = action.ToLowerInvariant();
        if (normalizedAction is not ("open" or "save"))
            return new FileDialogResult(false, false, Error: $"Unknown action '{action}'. Use \"open\" or \"save\".", ErrorCode: ErrorCodes.InvalidArgument);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            return new FileDialogResult(false, false, Error: $"Directory does not exist: {directory}", ErrorCode: ErrorCodes.FileNotFound);

        var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(timeoutSeconds, 1, 120));
        WindowInfo? dialogWindow = null;
        while (DateTime.UtcNow < deadline)
        {
            dialogWindow = WindowManager.ListWindows(onlyVisibleWithTitle: false)
                .FirstOrDefault(w => w.IsVisible && string.Equals(w.ClassName, "#32770", StringComparison.OrdinalIgnoreCase));
            if (dialogWindow is not null)
                break;
            await Task.Delay(300);
        }

        if (dialogWindow is null)
            return new FileDialogResult(false, false, Error: "No file dialog (class #32770) appeared within the timeout.", ErrorCode: ErrorCodes.WindowNotFound);

        lock (ctx.Gate)
        {
            var root = ctx.Automation.FromHandle(new IntPtr(dialogWindow.Handle));
            if (root is null)
                return new FileDialogResult(false, true, Error: "Could not access the dialog's UI Automation tree.", ErrorCode: ErrorCodes.Unknown);

            // Modern Explorer-style common dialogs (Windows 10/11) name the filename ComboBox's AutomationId
            // "FileNameControlHost", with the actual editable Edit as its child (AutomationId "1001" in current
            // builds). Older dialog variants may use "1148" (classic comdlg32 template) directly on the Edit.
            // These same Explorer-style dialogs also contain many OTHER Edit controls — a search box, and one
            // inline-rename Edit per file in the list view — so grabbing "the first Edit found" in the whole
            // window reliably lands on the wrong one (and can hang: those list-item edits aren't normally active).
            var editField = root.FindAllDescendants(cf => cf.ByAutomationId("FileNameControlHost")).FirstOrDefault();
            if (editField is not null && editField.ControlType != ControlType.Edit)
                editField = editField.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit)).FirstOrDefault() ?? editField;

            if (editField is null)
            {
                editField = root.FindAllDescendants(cf => cf.ByAutomationId("1148")).FirstOrDefault();
                if (editField is not null && editField.ControlType != ControlType.Edit)
                    editField = editField.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit)).FirstOrDefault() ?? editField;
            }

            // Last resort only — can land on the wrong Edit in Explorer-style dialogs, per the comment above.
            editField ??= root.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit)).FirstOrDefault();

            if (editField is null)
                return new FileDialogResult(false, true, Error: "Could not find the filename field in the dialog.", ErrorCode: ErrorCodes.ElementNotFound);

            try
            {
                if (editField.Patterns.Value.IsSupported)
                {
                    editField.Patterns.Value.Pattern.SetValue(path);
                }
                else
                {
                    editField.Focus();
                    InputSimulator.TypeUnicodeText(path);
                }
            }
            catch (Exception ex)
            {
                return new FileDialogResult(false, true, Error: $"Failed to set the filename field: {ex.Message}", ErrorCode: ErrorCodes.Unknown);
            }

            // A real user types the filename then presses Enter — that's the dialog's actual default action,
            // and (unlike invoking the accept button right after a programmatic SetValue) reliably makes an
            // Explorer-style dialog pick up the new value; some builds don't otherwise notice the SetValue at all.
            try
            {
                editField.Focus();
                InputSimulator.SendKeyChord("enter");
            }
            catch
            {
                // fall through to explicitly invoking the accept button below
            }

            Thread.Sleep(400);
            if (!WindowManager.ListWindows(onlyVisibleWithTitle: false).Any(w => w.Handle == dialogWindow.Handle))
                return new FileDialogResult(true, true);

            // Enter alone didn't close it — fall back to the accept button. Standard Windows common-dialog
            // template: it's always control ID 1 (IDOK), regardless of its localized caption or visual theme.
            var acceptButton = root.FindAllDescendants(cf => cf.ByAutomationId("1")).FirstOrDefault();
            if (acceptButton is null)
                return new FileDialogResult(false, true, Error: "Could not find the Open/Save button in the dialog.", ErrorCode: ErrorCodes.ElementNotFound);

            try
            {
                if (acceptButton.Patterns.Invoke.IsSupported)
                    acceptButton.Patterns.Invoke.Pattern.Invoke();
                else
                    acceptButton.Click();
            }
            catch (Exception ex)
            {
                return new FileDialogResult(false, true, Error: $"Failed to invoke the Open/Save button: {ex.Message}", ErrorCode: ErrorCodes.Unknown);
            }

            Thread.Sleep(400);
            var stillOpen = WindowManager.ListWindows(onlyVisibleWithTitle: false).Any(w => w.Handle == dialogWindow.Handle);
            return stillOpen
                ? new FileDialogResult(false, true, Error: "Dialog did not close after confirming — check for a conflicting overwrite/error prompt.", ErrorCode: ErrorCodes.Unknown)
                : new FileDialogResult(true, true);
        }
    }
}

using System.ComponentModel;
using Manctl.Core.Models;
using Manctl.Core.Win32;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

[McpServerToolType]
public static class ClipboardTools
{
    [McpServerTool(Name = "get_clipboard"), Description("Reads the text in the clipboard. Answer: {ok:true, value:\"...\"}; value is null if the clipboard is empty or holds something that is not text.")]
    public static ValueResult GetClipboard() => new(true, ClipboardService.GetText());

    [McpServerTool(Name = "set_clipboard"), Description("Puts text into the clipboard (handy for pasting long text with key_press(keys=\"ctrl+v\")).")]
    public static OkResult SetClipboard([Description("Text to place on the clipboard.")] string text)
    {
        var ok = ClipboardService.SetText(text);
        // The dominant real failure mode is another process holding the clipboard open past our retries.
        return new OkResult(ok, ok ? null : ErrorCodes.AccessDenied, ok ? null : "Could not open the clipboard (another program may be holding it).",
            ok ? null : "Wait a moment and try again.");
    }
}

using System.ComponentModel;
using Manctl.Core.Models;
using Manctl.Core.Win32;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

[McpServerToolType]
public static class InputLockTools
{
    [McpServerTool(Name = "lock_input"), Description(
        "Blocks the real user's physical mouse/keyboard input so a click/type_text chain can't be interrupted by " +
        "the user touching the mouse or keyboard mid-sequence. Auto-releases after 60 seconds even if unlock_input " +
        "is never called — a real input block is never left unbounded. run_sequence already does this itself " +
        "(lockInput=true by default); call this directly only when doing several separate click/type_text calls " +
        "outside run_sequence that need the whole episode protected. REQUIRES manctl to be running elevated (as " +
        "Administrator) — Windows' BlockInput API fails with ACCESS_DENIED for a non-elevated process. If ok:false, " +
        "the block did NOT engage; automation will still proceed unprotected unless you restart manctl elevated.")]
    public static OkResult LockInput()
    {
        var ok = InputLockService.Lock();
        return new OkResult(ok, ok ? null : ErrorCodes.AccessDenied, ok ? null : "BlockInput failed — manctl needs to run elevated (as Administrator) for this to work.");
    }

    [McpServerTool(Name = "unlock_input"), Description("Releases a lock_input block. Safe to call even if nothing is locked.")]
    public static OkResult UnlockInput() => new(InputLockService.Unlock());
}

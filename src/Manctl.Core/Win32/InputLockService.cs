using static Manctl.Core.Win32.NativeMethods;

namespace Manctl.Core.Win32;

/// <summary>Blocks the real user's physical mouse/keyboard input while an agent runs a click/type chain, so a
/// user touching the mouse mid-sequence can't steal input away. A real, unbounded input block is never
/// acceptable — a hardcoded safety timer force-unlocks even if unlock_input is never called.</summary>
public static class InputLockService
{
    public const int SafetyTimeoutMs = 60_000;

    private static readonly object Gate = new();
    private static CancellationTokenSource? _safetyTimer;
    private static bool _locked;

    public static bool Lock()
    {
        lock (Gate)
        {
            if (_locked)
            {
                RestartSafetyTimer();
                return true;
            }

            var ok = BlockInput(true);
            if (ok)
            {
                _locked = true;
                RestartSafetyTimer();
            }
            return ok;
        }
    }

    public static bool Unlock()
    {
        lock (Gate)
        {
            CancelSafetyTimer();
            if (!_locked)
                return true;

            var ok = BlockInput(false);
            _locked = false;
            return ok;
        }
    }

    private static void RestartSafetyTimer()
    {
        CancelSafetyTimer();
        var cts = new CancellationTokenSource();
        _safetyTimer = cts;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(SafetyTimeoutMs, cts.Token); }
            catch (TaskCanceledException) { return; }
            Unlock(); // agent never called unlock_input in time — force it, a stuck lock is not acceptable
        });
    }

    private static void CancelSafetyTimer()
    {
        _safetyTimer?.Cancel();
        _safetyTimer = null;
    }
}

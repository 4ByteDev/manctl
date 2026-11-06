using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Manctl.Core.Models;

namespace Manctl.Core.Win32;

/// <summary>What happened when a window was asked to come to the front.</summary>
/// <param name="Method">The way that finally worked (or the last one tried).</param>
public sealed record FocusOutcome(bool Ok, string Method, string? Error = null, string? Hint = null, string? FrontWindow = null);

/// <summary>Bringing a window to the front is not a plain Windows call: when the caller is not the program the user is
/// working in, Windows often only flashes the taskbar button instead of switching ("foreground lock"). One attempt is
/// therefore not enough. This tries a ladder of known ways, from the gentlest to the most visible, and checks after
/// each one whether the window really is in front. Only when all of them fail does it say why and what to do next.</summary>
public static class FocusService
{
    private const int GA_ROOTOWNER = 3;
    private const int SW_MINIMIZE = 6, SW_RESTORE = 9, SW_SHOWNORMAL = 1;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_SHOWWINDOW = 0x40;
    private const byte VK_F24 = 0x87;
    private const uint KEYEVENTF_KEYUP = 2;
    private const uint SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000, SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001, SPIF_SENDCHANGE = 2;
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0xC00000L;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, int flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SpiGet(uint action, uint param, out uint value, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SpiSet(uint action, uint param, IntPtr value, uint winIni);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(IntPtr h, bool altTab);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    public static FocusOutcome Focus(long handle)
    {
        var hwnd = new IntPtr(handle);
        if (!IsWindow(hwnd))
            return new FocusOutcome(false, "none", "That window no longer exists.", "Call list_windows for the windows that exist now.");

        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        if (InFront(hwnd)) return new FocusOutcome(true, "already in front");

        // Test hook: pretend the first N ways failed (MANCTL_FOCUS_TEST_SKIP=N) or all of them (=all), so every step of
        // the ladder and the failure answer can be tested on a machine where Windows is cooperative.
        var skipSetting = Environment.GetEnvironmentVariable("MANCTL_FOCUS_TEST_SKIP");
        var skip = skipSetting == "all" ? int.MaxValue : int.TryParse(skipSetting, out var n) ? n : 0;

        var steps = new List<(string Name, Action Run)>
        {
            ("attach input and set foreground", () => { AttachAnd(hwnd, () => { BringWindowToTop(hwnd); SetForegroundWindow(hwnd); }); }),
            ("input event, then set foreground", () => { Tap(); SetForegroundWindow(hwnd); }),
            ("lift the foreground lock", () => { WithoutForegroundLock(() => { AllowSetForegroundWindow(-1); SetForegroundWindow(hwnd); }); }),
            ("minimize and restore", () => { ShowWindow(hwnd, SW_MINIMIZE); Thread.Sleep(120); ShowWindow(hwnd, SW_RESTORE); SetForegroundWindow(hwnd); }),
            ("topmost flip", () => { SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW); SetWindowPos(hwnd, new IntPtr(-2), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW); SetForegroundWindow(hwnd); }),
            ("switch to this window", () => SwitchToThisWindow(hwnd, true)),
            ("click on its title bar", () => ClickTitleBar(hwnd)),
        };

        var tried = new List<string>();
        for (var i = 0; i < steps.Count; i++)
        {
            tried.Add(steps[i].Name);
            if (i < skip) continue;
            try { steps[i].Run(); } catch { /* a way that throws just counts as failed */ }
            if (WaitInFront(hwnd, 250)) return new FocusOutcome(true, steps[i].Name);
        }

        return Failure(handle, tried);
    }

    private static FocusOutcome Failure(long handle, List<string> tried)
    {
        var front = WindowManager.FindWindow(null, null);
        var frontText = front is null ? null : $"'{front.Title}'";
        var target = WindowManager.FindWindow(handle, null);

        uint pid = 0;
        GetWindowThreadProcessId(new IntPtr(handle), out pid);
        var targetElevated = IsElevated((int)pid);

        string why;
        string hint;
        if (targetElevated == true && !SelfElevated())
        {
            why = $"The window{(target is null ? "" : $" '{target.Title}'")} belongs to a program running as Administrator, and manctl is not: Windows does not let a normal program bring it forward or send it input.";
            hint = "Run manctl as Administrator (restart it elevated), or ask the user to click on that window once. Until then keys and mouse cannot go to it.";
        }
        else
        {
            why = $"Windows did not let the window come to the front after {tried.Count} different ways (it only flashes in the taskbar). In front now: {frontText ?? "unknown"}.";
            hint = "Do not give up yet: " +
                   "(1) click(name=...) and type_text(name=..., ...) on elements work WITHOUT the window in front (they use UI Automation) — try them. " +
                   "(2) Look at the window with read_text / get_ui_tree / screenshot(windowHandle=...), which also work in the background. " +
                   "(3) Only keys and mouse clicks need it in front: close or minimize the window that is in front (set_window_state) and call focus_window again. " +
                   "(4) Last resort: ask the user, naming the window, to click on it once.";
        }
        return new FocusOutcome(false, tried[^1], why, hint, frontText);
    }

    // ---- the ways ---------------------------------------------------------------------------------------------

    private static void AttachAnd(IntPtr hwnd, Action act)
    {
        var current = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        var attached = AttachThreadInput(current, targetThread, true);
        try { act(); }
        finally { if (attached) AttachThreadInput(current, targetThread, false); }
    }

    /// <summary>A key nobody uses (F24) counts as "user input" for Windows' foreground rules but does nothing visible.</summary>
    private static void Tap()
    {
        keybd_event(VK_F24, 0, 0, UIntPtr.Zero);
        keybd_event(VK_F24, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>The "foreground lock timeout" is the number of milliseconds after the user's last input during which
    /// other programs may not take the front. Set to 0 for a moment (in memory only, not saved) and put back.</summary>
    private static void WithoutForegroundLock(Action act)
    {
        SpiGet(SPI_GETFOREGROUNDLOCKTIMEOUT, 0, out var old, 0);
        SpiSet(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, IntPtr.Zero, SPIF_SENDCHANGE);
        try { act(); }
        finally { SpiSet(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, new IntPtr(old), SPIF_SENDCHANGE); }
    }

    /// <summary>A real click on the title bar, but only if the window has one and the point shows this very window
    /// (never a click into a game or into whatever happens to cover it).</summary>
    private static void ClickTitleBar(IntPtr hwnd)
    {
        if ((GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64() & WS_CAPTION) != WS_CAPTION) return;
        var r = WindowManager.GetWindowRectPhysical(hwnd.ToInt64());
        if (r.Width < 60) return;
        var x = r.X + r.Width / 2;
        var y = r.Y + 12;
        if (ScreenGuard.CheckPoint(x, y) is not null) return;
        var hit = WindowFromPoint(new POINT { X = x, Y = y });
        if (hit == IntPtr.Zero || GetAncestor(hit, GA_ROOTOWNER) != hwnd) return; // something else is on top of the title bar
        InputSimulator.Click(x, y, MouseButton.Left);
    }

    // ---- checks -------------------------------------------------------------------------------------------------

    /// <summary>In front means: it is the foreground window, or the foreground window is its own dialog.</summary>
    private static bool InFront(IntPtr hwnd)
    {
        var fg = GetForegroundWindow();
        return fg == hwnd || (fg != IntPtr.Zero && GetAncestor(fg, GA_ROOTOWNER) == hwnd);
    }

    private static bool WaitInFront(IntPtr hwnd, int ms)
    {
        var sw = Stopwatch.StartNew();
        do
        {
            if (InFront(hwnd)) return true;
            Thread.Sleep(25);
        } while (sw.ElapsedMilliseconds < ms);
        return false;
    }

    private static bool SelfElevated()
    {
        try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    /// <returns>true/false, or null if the process cannot even be inspected (which itself usually means "more privileged").</returns>
    private static bool? IsElevated(int pid)
    {
        if (pid <= 0) return null;
        var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(process, 0x8 /* TOKEN_QUERY */, out var token)) return null;
            try { return GetTokenInformation(token, 20 /* TokenElevation */, out var elevated, sizeof(int), out _) ? elevated != 0 : null; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }
}

using System.Text;
using Manctl.Core.Models;
using static Manctl.Core.Win32.NativeMethods;

namespace Manctl.Core.Win32;

public static class WindowManager
{
    /// <summary>Must be called once at process startup, before any window/COM interaction, so that GetWindowRect,
    /// UIA bounding rects and SendInput coordinates all agree on the same physical-pixel space.</summary>
    public static void EnablePerMonitorDpiAwareness() =>
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

    public static List<WindowInfo> ListWindows(bool onlyVisibleWithTitle = true)
    {
        var results = new List<WindowInfo>();
        var foreground = GetForegroundWindow();

        EnumWindows((hWnd, _) =>
        {
            var visible = IsWindowVisible(hWnd);
            var title = GetTitle(hWnd);

            if (onlyVisibleWithTitle && (!visible || string.IsNullOrWhiteSpace(title)))
                return true;

            GetWindowThreadProcessId(hWnd, out var pid);
            GetWindowRect(hWnd, out var rect);

            results.Add(new WindowInfo(
                Handle: hWnd.ToInt64(),
                Title: title,
                ClassName: GetClassNameOf(hWnd),
                ProcessId: (int)pid,
                Bounds: ToRect(rect),
                IsVisible: visible,
                IsMinimized: IsIconic(hWnd),
                IsForeground: hWnd == foreground));

            return true;
        }, IntPtr.Zero);

        return results;
    }

    public static WindowInfo? FindWindow(long? handle, string? titleContains)
    {
        if (handle is { } h)
            return ListWindows(onlyVisibleWithTitle: false).FirstOrDefault(w => w.Handle == h);

        if (!string.IsNullOrWhiteSpace(titleContains))
            return ListWindows().FirstOrDefault(w => w.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase));

        var fg = GetForegroundWindow();
        return ListWindows(onlyVisibleWithTitle: false).FirstOrDefault(w => w.Handle == fg.ToInt64());
    }

    /// <summary>Restores if minimized and reliably steals foreground focus, verifying the result — Windows normally
    /// blocks SetForegroundWindow from background processes, so this attaches input queues as the standard workaround.</summary>
    public static bool FocusWindow(long handle) => FocusService.Focus(handle).Ok;

    /// <summary>"minimized", "maximized" or "normal" — what the window is really doing right now.</summary>
    public static string GetState(long handle)
    {
        var h = new IntPtr(handle);
        return IsIconic(h) ? "minimized" : IsZoomed(h) ? "maximized" : "normal";
    }

    public static bool Exists(long handle) => IsWindow(new IntPtr(handle));

    public static Rect GetWindowRectPhysical(long handle)
    {
        GetWindowRect(new IntPtr(handle), out var rect);
        return ToRect(rect);
    }

    /// <summary>Moves a window's top-left corner. Like every Win32 call below, this simply returns false on a
    /// stale/invalid handle rather than throwing — a window disappearing mid-automation is an expected race,
    /// not an exceptional one.</summary>
    public static bool MoveWindowTo(long handle, int x, int y) =>
        SetWindowPos(new IntPtr(handle), IntPtr.Zero, x, y, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);

    public static bool ResizeWindowTo(long handle, int width, int height) =>
        SetWindowPos(new IntPtr(handle), IntPtr.Zero, 0, 0, width, height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE);

    public static bool MinimizeWindow(long handle) => ShowWindow(new IntPtr(handle), SW_MINIMIZE);

    public static bool MaximizeWindow(long handle) => ShowWindow(new IntPtr(handle), SW_MAXIMIZE);

    public static bool RestoreWindowState(long handle) => ShowWindow(new IntPtr(handle), SW_RESTORE);

    /// <summary>Posts WM_CLOSE — the normal graceful-close signal (may prompt "save changes?"), not a process kill.
    /// Not a substitute for stop_process: the window can decline/prompt, and the process isn't guaranteed to exit.</summary>
    public static bool CloseWindow(long handle) => PostMessage(new IntPtr(handle), WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    private static Rect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    private static string GetTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length == 0)
            return string.Empty;

        var sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClassNameOf(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}

using Manctl.Core.Models;
using Manctl.Core.Screenshots;

namespace Manctl.Core.Win32;

/// <summary>A click, drag or scroll at a point that is not on any screen "succeeds" in Windows and does nothing — a
/// silent failure for an agent that mixed up coordinate spaces. These checks turn it into a clear error.</summary>
public static class ScreenGuard
{
    /// <returns>null if the point is on the (multi-monitor) screen, otherwise what is wrong.</returns>
    public static string? CheckPoint(int x, int y, string what = "point")
    {
        var b = ScreenCapture.GetDesktopBounds();
        if (x >= b.X && y >= b.Y && x < b.Right && y < b.Bottom) return null;
        return $"The {what} ({x}, {y}) is outside the screen, which covers x={b.X}..{b.Right - 1}, y={b.Y}..{b.Bottom - 1}. x and y are SCREEN coordinates in pixels.";
    }

    /// <returns>null if no window is named or the point lies inside it, otherwise what is wrong.</returns>
    public static string? CheckInWindow(int x, int y, long? windowHandle)
    {
        if (windowHandle is not { } h) return null;
        var window = WindowManager.FindWindow(h, null);
        if (window is null) return null; // a missing window is reported elsewhere
        var w = window.Bounds;
        if (window.IsMinimized || (x >= w.X && y >= w.Y && x < w.Right && y < w.Bottom)) return null;
        return $"The point ({x}, {y}) is outside window {h}, which covers screen x={w.X}..{w.Right - 1}, y={w.Y}..{w.Bottom - 1}. " +
               $"x and y are SCREEN coordinates, not coordinates inside the window: for a spot inside it use x = {w.X} + (distance from its left edge), y = {w.Y} + (distance from its top edge). " +
               "Leave windowHandle out if you really mean a point outside it.";
    }
}

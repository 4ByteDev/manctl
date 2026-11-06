using Manctl.Core.Models;
using static Manctl.Core.Win32.NativeMethods;

namespace Manctl.Core.Win32;

public static class MonitorService
{
    public static List<MonitorInfo> ListMonitors()
    {
        var monitors = new List<MonitorInfo>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
        {
            var info = new MONITORINFOEX { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(hMonitor, ref info))
                return true;

            uint dpiX = 96, dpiY = 96;
            try { GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY); }
            catch { /* pre-8.1 fallback stays at 96 */ }

            monitors.Add(new MonitorInfo(
                ToRect(info.rcMonitor),
                ToRect(info.rcWork),
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                (int)dpiX, (int)dpiY,
                info.szDevice));

            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    private static Rect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}

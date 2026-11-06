using System.Runtime.InteropServices;
using static Manctl.Core.Win32.NativeMethods;

namespace Manctl.Core.Win32;

/// <summary>Raw Win32 clipboard access — deliberately not System.Windows.Forms.Clipboard, which requires an STA
/// thread that a generic MCP tool-call thread isn't guaranteed to be. OpenClipboard commonly fails transiently
/// if another process briefly holds it, so both operations retry a few times.</summary>
public static class ClipboardService
{
    public static string? GetText()
    {
        return WithClipboard(() =>
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
                return null;

            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero)
                return null;

            try { return Marshal.PtrToStringUni(ptr); }
            finally { GlobalUnlock(handle); }
        });
    }

    public static bool SetText(string text)
    {
        var byteCount = (text.Length + 1) * 2; // UTF-16 + null terminator
        var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
        if (hGlobal == IntPtr.Zero)
            return false;

        var ptr = GlobalLock(hGlobal);
        if (ptr == IntPtr.Zero)
        {
            GlobalFree(hGlobal);
            return false;
        }

        Marshal.Copy((text + '\0').ToCharArray(), 0, ptr, text.Length + 1);
        GlobalUnlock(hGlobal);

        var result = WithClipboard(() =>
        {
            EmptyClipboard();
            return SetClipboardData(CF_UNICODETEXT, hGlobal) != IntPtr.Zero;
        });

        // Ownership of hGlobal transfers to the OS clipboard only once SetClipboardData succeeds.
        if (result != true)
            GlobalFree(hGlobal);

        return result == true;
    }

    private static T? WithClipboard<T>(Func<T?> action)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try { return action(); }
                finally { CloseClipboard(); }
            }
            Thread.Sleep(50);
        }
        return default;
    }
}

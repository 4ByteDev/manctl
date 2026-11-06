using System.Drawing;
using System.Drawing.Imaging;
using Manctl.Core.Models;
using static Manctl.Core.Win32.NativeMethods;

namespace Manctl.Core.Screenshots;

/// <summary>A finished picture plus what the caller must know about it: was it one flat color (black window capture
/// of a game, a covered or protected window) and did the window capture have to be replaced by a plain screen copy.</summary>
public sealed record Shot(byte[]? Image, bool Flat, bool UsedScreenCopy);

/// <summary>Screenshot is an explicit opt-in, not the default way to see the screen — get_ui_tree covers most
/// needs at a fraction of the token cost. Physical-pixel space matches get_ui_tree/click/type_text exactly.</summary>
public static class ScreenCapture
{
    /// <returns>Image null if <paramref name="crop"/> doesn't overlap the captured area at all.</returns>
    public static Shot CaptureRegion(Rect region, Rect? crop = null, ImageOptions? options = null)
    {
        using var bmp = new Bitmap(Math.Max(1, region.Width), Math.Max(1, region.Height), PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(region.X, region.Y, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
        }
        return EncodeWithOptionalCrop(bmp, region, crop, options ?? ImageOptions.Default, usedScreenCopy: false);
    }

    /// <summary>Uses PrintWindow with PW_RENDERFULLCONTENT so the capture reflects the window's real content even
    /// if it's occluded by another window. Games and some GPU-drawn windows come back as one flat black rectangle
    /// from PrintWindow; then the plain screen copy of the window's area is used instead (it needs the window to be
    /// visible and not covered), and the result says so.</summary>
    public static Shot CaptureWindow(long windowHandle, Rect windowBounds, Rect? crop = null, ImageOptions? options = null)
    {
        var hWnd = new IntPtr(windowHandle);
        var opts = options ?? ImageOptions.Default;
        using var bmp = new Bitmap(Math.Max(1, windowBounds.Width), Math.Max(1, windowBounds.Height), PixelFormat.Format32bppArgb);

        bool printed;
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            try { printed = PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT); }
            finally { g.ReleaseHdc(hdc); }
        }

        if (printed && !ImageEncoding.IsFlat(bmp))
            return EncodeWithOptionalCrop(bmp, windowBounds, crop, opts, usedScreenCopy: false);

        var copy = CaptureRegion(windowBounds, crop, opts);
        return copy with { UsedScreenCopy = true };
    }

    /// <returns>Image null if <paramref name="crop"/> doesn't overlap the captured area at all.</returns>
    public static Shot CaptureDesktop(Rect? crop = null, ImageOptions? options = null) => CaptureRegion(GetDesktopBounds(), crop, options);

    /// <summary>The whole virtual screen (all monitors) in physical screen pixels.</summary>
    public static Rect GetDesktopBounds() => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN));

    /// <summary>The crop rectangle is given in the same absolute physical-screen space as everything else in
    /// manctl; captureOrigin is where the just-captured bitmap's (0,0) sits in that same space, so the crop is
    /// translated into bitmap-local coordinates and clamped to the bitmap's actual bounds before cropping.</summary>
    private static Shot EncodeWithOptionalCrop(Bitmap bmp, Rect captureOrigin, Rect? crop, ImageOptions options, bool usedScreenCopy)
    {
        if (crop is null)
            return new Shot(ImageEncoding.Encode(bmp, options), ImageEncoding.IsFlat(bmp), usedScreenCopy);

        var localX = crop.X - captureOrigin.X;
        var localY = crop.Y - captureOrigin.Y;

        var left = Math.Max(0, localX);
        var top = Math.Max(0, localY);
        var right = Math.Min(bmp.Width, localX + crop.Width);
        var bottom = Math.Min(bmp.Height, localY + crop.Height);

        if (right <= left || bottom <= top)
            return new Shot(null, false, usedScreenCopy);

        using var cropped = bmp.Clone(new Rectangle(left, top, right - left, bottom - top), bmp.PixelFormat);
        return new Shot(ImageEncoding.Encode(cropped, options), ImageEncoding.IsFlat(cropped), usedScreenCopy);
    }
}

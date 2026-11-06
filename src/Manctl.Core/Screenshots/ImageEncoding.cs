using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Manctl.Core.Models;

namespace Manctl.Core.Screenshots;

/// <summary>How a captured picture is sent back: size (scale), file format and JPEG quality. Fewer pixels means
/// fewer tokens for the agent, so scale &lt; 1 is the cheap "just look" mode and scale &gt; 1 enlarges tiny text.</summary>
public sealed record ImageOptions(double Scale = 1.0, string Format = "png", int Quality = 80)
{
    public static readonly ImageOptions Default = new();

    public bool IsJpeg => string.Equals(Format, "jpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(Format, "jpg", StringComparison.OrdinalIgnoreCase);
    public string MimeType => IsJpeg ? "image/jpeg" : "image/png";

    /// <returns>null when the options are valid, otherwise what is wrong.</returns>
    public string? Validate()
    {
        if (!(Scale >= 0.05 && Scale <= 8)) return "scale must be between 0.05 and 8 (1 = original size, 0.5 = half size, 2 = twice as big).";
        if (!IsJpeg && !string.Equals(Format, "png", StringComparison.OrdinalIgnoreCase)) return "format must be \"png\" or \"jpeg\".";
        if (Quality is < 1 or > 100) return "quality must be between 1 and 100.";
        return null;
    }
}

public static class ImageEncoding
{
    /// <summary>True if every sampled pixel is the same color: a black window capture of a game, a blank/locked
    /// screen, a protected app. Real screens always have some variation.</summary>
    public static bool IsFlat(Bitmap bmp)
    {
        if (bmp.Width < 2 || bmp.Height < 2) return false;
        var first = bmp.GetPixel(0, 0);
        const int grid = 24;
        for (var iy = 0; iy < grid; iy++)
        {
            var y = (int)((long)iy * (bmp.Height - 1) / (grid - 1));
            for (var ix = 0; ix < grid; ix++)
            {
                var x = (int)((long)ix * (bmp.Width - 1) / (grid - 1));
                if (bmp.GetPixel(x, y).ToArgb() != first.ToArgb()) return false;
            }
        }
        return true;
    }

    public static bool IsFlat(byte[] png)
    {
        try { using var ms = new MemoryStream(png); using var b = new Bitmap(ms); return IsFlat(b); }
        catch { return false; }
    }

    public static byte[] Encode(Bitmap source, ImageOptions options)
    {
        var bmp = source;
        Bitmap? scaled = null;
        try
        {
            if (Math.Abs(options.Scale - 1.0) > 0.001)
            {
                var w = Math.Max(1, (int)Math.Round(source.Width * options.Scale));
                var h = Math.Max(1, (int)Math.Round(source.Height * options.Scale));
                scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(scaled);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, w, h));
                bmp = scaled;
            }

            using var ms = new MemoryStream();
            if (options.IsJpeg)
            {
                var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)options.Quality);
                // JPEG has no alpha: flatten onto white so transparent pixels don't turn black.
                using var flat = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(flat)) { g.Clear(Color.White); g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height); }
                flat.Save(ms, codec, ep);
            }
            else
            {
                bmp.Save(ms, ImageFormat.Png);
            }
            return ms.ToArray();
        }
        finally
        {
            scaled?.Dispose();
        }
    }

    /// <summary>Post-processes an already encoded PNG (used for Android screencap): optional crop in image pixels,
    /// then scale and re-encode. Returns null if the crop doesn't overlap the image.</summary>
    public static byte[]? Reencode(byte[] png, Rect? cropInImage, ImageOptions options)
    {
        // Nothing to change: hand the original bytes back untouched.
        if (cropInImage is null && Math.Abs(options.Scale - 1.0) < 0.001 && !options.IsJpeg)
            return png;

        using var input = new MemoryStream(png);
        using var bmp = new Bitmap(input);
        if (cropInImage is null)
            return Encode(bmp, options);

        var left = Math.Max(0, cropInImage.X);
        var top = Math.Max(0, cropInImage.Y);
        var right = Math.Min(bmp.Width, cropInImage.X + cropInImage.Width);
        var bottom = Math.Min(bmp.Height, cropInImage.Y + cropInImage.Height);
        if (right <= left || bottom <= top) return null;
        using var cropped = bmp.Clone(new Rectangle(left, top, right - left, bottom - top), PixelFormat.Format32bppArgb);
        return Encode(cropped, options);
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using CommunityToolkit.HighPerformance;

namespace ModularVests.BundleTool;

/// <summary>
/// Turns a recoloured albedo PNG into the byte layout a Unity Texture2D expects: the full
/// mip chain, largest level first, in the format the source texture already used.
/// </summary>
internal static class Dxt
{
    /// <summary>Unity's Texture2D.m_TextureFormat values we know how to write.</summary>
    public const int FormatDxt1 = 10;
    public const int FormatDxt5 = 12;

    public static bool IsSupported(int textureFormat) =>
        textureFormat is FormatDxt1 or FormatDxt5;

    /// <summary>
    /// Encodes the PNG to <paramref name="textureFormat"/>. <paramref name="mipCount"/> levels are
    /// produced, largest first; one level means no mip chain.
    /// </summary>
    public static byte[] Encode(string pngPath, int textureFormat, int width, int height, int mipCount)
    {
        var pixels = ReadPng(pngPath, width, height);

        var encoder = new BcEncoder
        {
            OutputOptions =
            {
                Format = textureFormat == FormatDxt1 ? CompressionFormat.Bc1 : CompressionFormat.Bc3,
                Quality = CompressionQuality.BestQuality,
                GenerateMipMaps = mipCount > 1,
                MaxMipMapLevel = mipCount,
            },
        };

        var levels = encoder.EncodeToRawBytes(pixels);
        if (levels.Length != mipCount)
        {
            throw new InvalidOperationException(
                $"encoder produced {levels.Length} mip levels, the texture it replaces has {mipCount}");
        }

        var expected = MipChainSize(textureFormat, width, height, mipCount);
        var data = new byte[levels.Sum(level => level.Length)];
        var offset = 0;
        foreach (var level in levels)
        {
            level.CopyTo(data, offset);
            offset += level.Length;
        }

        if (data.Length != expected)
        {
            throw new InvalidOperationException($"encoded {data.Length} bytes, the texture it replaces is {expected}");
        }

        return data;
    }

    /// <summary>
    /// Average colour of a PNG, 0-255 per channel. The build script turns it into the material's
    /// <c>_Color</c>, so that a colour of the line-up reaches the screen at the same level on
    /// every carrier however bright the carrier's own albedo happens to be.
    /// </summary>
    public static (double R, double G, double B) Mean(string pngPath)
    {
        using var bitmap = new Bitmap(pngPath);
        var locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        double r = 0, g = 0, b = 0;
        try
        {
            var row = new byte[bitmap.Width * 4];
            for (var y = 0; y < bitmap.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(locked.Scan0 + (y * locked.Stride), row, 0, row.Length);
                for (var x = 0; x < bitmap.Width; x++)
                {
                    b += row[x * 4];
                    g += row[(x * 4) + 1];
                    r += row[(x * 4) + 2];
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }

        var pixels = (double)bitmap.Width * bitmap.Height;
        return (r / pixels, g / pixels, b / pixels);
    }

    /// <summary>Perceived brightness of a colour, the way the eye weighs the channels.</summary>
    public static double Luma(double r, double g, double b) => (0.2126 * r) + (0.7152 * g) + (0.0722 * b);

    /// <summary>Size of a block-compressed mip chain, the way Unity lays it out.</summary>
    public static int MipChainSize(int textureFormat, int width, int height, int mipCount)
    {
        var blockBytes = textureFormat == FormatDxt1 ? 8 : 16;
        var total = 0;
        for (var level = 0; level < mipCount; level++)
        {
            var w = Math.Max(1, width >> level);
            var h = Math.Max(1, height >> level);
            total += (w + 3) / 4 * ((h + 3) / 4) * blockBytes;
        }

        return total;
    }

    /// <summary>
    /// The PNG as rows of RGBA, in the order Unity stores a texture: bottom row first. The kit's
    /// PNGs are the right way up, which is how they were read out of the game's own textures -
    /// decoding a game texture and flipping it back gives the kit's PNG byte for byte
    /// (<c>orient</c> command).
    /// </summary>
    private static ReadOnlyMemory2D<ColorRgba32> ReadPng(string pngPath, int width, int height)
    {
        using var bitmap = new Bitmap(pngPath);
        if (bitmap.Width != width || bitmap.Height != height)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(pngPath)} is {bitmap.Width}x{bitmap.Height}, the texture it replaces is {width}x{height}");
        }

        var pixels = new ColorRgba32[height, width];
        var locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[width * 4];
            for (var y = 0; y < height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(locked.Scan0 + (y * locked.Stride), row, 0, row.Length);
                for (var x = 0; x < width; x++)
                {
                    // Format32bppArgb is laid out B, G, R, A in memory
                    pixels[height - 1 - y, x] =
                        new ColorRgba32(row[(x * 4) + 2], row[(x * 4) + 1], row[x * 4], row[(x * 4) + 3]);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }

        return pixels;
    }
}

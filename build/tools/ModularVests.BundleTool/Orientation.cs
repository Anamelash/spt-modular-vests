using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace ModularVests.BundleTool;

/// <summary>
/// One-off check: whether the PNGs the recolour kit ships are stored the same way up as the
/// texture inside the bundle. Unity keeps a texture bottom row first; a tool that extracted it
/// may or may not have turned it back over.
/// </summary>
internal static class Orientation
{
    public static int Compare(string bundlePath, long pathId, string pngPath)
    {
        var manager = new AssetsManager();
        var bundleInst = manager.LoadBundleFile(bundlePath, true);
        var dirs = bundleInst.file.BlockAndDirInfo.DirectoryInfos;
        var assetsIndex = Enumerable.Range(0, dirs.Count).First(bundleInst.file.IsAssetsFile);
        var fileInst = manager.LoadAssetsFileFromBundle(bundleInst, assetsIndex, false);
        var texture = manager.GetBaseField(fileInst, fileInst.file.GetAssetInfo(pathId));

        var width = texture["m_Width"].AsInt;
        var height = texture["m_Height"].AsInt;
        var format = texture["m_TextureFormat"].AsInt;
        var raw = TextureBytes(bundleInst, fileInst, texture);

        var decoder = new BcDecoder();
        var decoded = decoder.DecodeRaw(raw, width, height,
            format == Dxt.FormatDxt1 ? CompressionFormat.Bc1 : CompressionFormat.Bc3);

        using var bitmap = new Bitmap(pngPath);
        var png = new ColorRgba32[height, width];
        var locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var row = new byte[width * 4];
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(locked.Scan0 + (y * locked.Stride), row, 0, row.Length);
            for (var x = 0; x < width; x++)
            {
                png[y, x] = new ColorRgba32(row[(x * 4) + 2], row[(x * 4) + 1], row[x * 4], row[(x * 4) + 3]);
            }
        }

        bitmap.UnlockBits(locked);

        Console.WriteLine($"as stored: {Difference(decoded, png, width, height, flip: false):F2}");
        Console.WriteLine($"flipped:   {Difference(decoded, png, width, height, flip: true):F2}");
        return 0;
    }

    private static double Difference(ColorRgba32[] decoded, ColorRgba32[,] png, int width, int height, bool flip)
    {
        double total = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var a = decoded[(y * width) + x];
                var b = png[flip ? height - 1 - y : y, x];
                total += Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);
            }
        }

        return total / (width * (double)height * 3);
    }

    private static byte[] TextureBytes(BundleFileInstance bundleInst, AssetsFileInstance fileInst, AssetTypeValueField texture)
    {
        var inline = texture["image data"].AsByteArray;
        if (inline.Length > 0)
        {
            return inline;
        }

        var stream = texture["m_StreamData"];
        var path = stream["path"].AsString;
        var offset = (long)stream["offset"].AsULong;
        var size = (int)stream["size"].AsUInt;
        var name = path[(path.LastIndexOf('/') + 1)..];

        var dirs = bundleInst.file.BlockAndDirInfo.DirectoryInfos;
        var index = dirs.FindIndex(d => d.Name == name);
        bundleInst.file.GetFileRange(index, out var start, out _);
        var reader = bundleInst.file.DataReader;
        reader.Position = start + offset;
        return reader.ReadBytes(size);
    }
}

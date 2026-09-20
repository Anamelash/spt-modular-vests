using System.Security.Cryptography;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace ModularVests.BundleTool;

internal sealed class CloneRequest
{
    /// <summary>The bundle the clone is made from - a game or WTT bundle, read only.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Where the clone is written.</summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// The clone's bundle key (<c>modularvests/vests/otv_black.bundle</c>). Only the internal
    /// file name is derived from it, so that the clone and its source can be loaded side by side.
    /// </summary>
    public required string BundleKey { get; init; }

    /// <summary>Texture path id -> the PNG that replaces it.</summary>
    public required Dictionary<long, string> Replacements { get; init; }

    /// <summary>
    /// Name the clone's own asset takes (<c>otv_black</c>), so that the game finds it the way it
    /// finds every other item's model: the asset named like the bundle, with an empty rcid.
    /// Empty leaves the donor's names alone, and the item then has to name the asset itself.
    /// </summary>
    public string AssetName { get; init; } = "";

    /// <summary>
    /// For a carrier whose textures live in a bundle of their own: the named assets stop coming
    /// from that bundle and come from our clone of it instead. Only those - everything else the
    /// bundle holds is still read from it, because our clone does not carry it.
    /// </summary>
    public List<Reroute> Reroutes { get; init; } = [];

    /// <summary>
    /// What the material multiplies its albedo by (<c>_Color</c>), replacing the donor's. The
    /// recoloured albedos are darker than the ones the donor's material was drawn for, and by a
    /// different amount on each carrier; this is where that is put right. Null leaves the donor's.
    /// </summary>
    public (double R, double G, double B)? Tint { get; init; }
}

/// <summary>Assets that move from one bundle to another, by the file names inside them.</summary>
internal sealed record Reroute(string FromCab, string ToCab, IReadOnlyCollection<long> PathIds);

internal sealed class CloneResult
{
    public required string Key { get; init; }
    public required string Cab { get; init; }
    public required string SourceCab { get; init; }
    public required long Size { get; init; }
    public required List<string> Dropped { get; init; }
    public required List<string> Replaced { get; init; }
    public required List<string> Tinted { get; init; }

    /// <summary>
    /// Every file the clone points at, by its name inside its own bundle. The build script turns
    /// these into the bundle keys the game loads first (dependencyKeys in bundles.json), and
    /// stops on one it cannot name rather than shipping a bundle with a dependency missing.
    /// </summary>
    public required List<string> Externals { get; init; }
}

/// <summary>
/// Makes a recoloured copy of a game bundle without carrying the original's geometry:
/// the meshes and the maps the recolour does not touch stay in the source bundle and are
/// reached through an external reference, so the clone holds the prefab, the material and
/// one albedo. The game loads the source first (dependencyKeys in bundles.json).
///
/// Every path id is kept as it is - the prefab, the material and the components reference each
/// other exactly as they did - and only the file name inside the bundle is new, because Unity
/// refuses to load two bundles that carry files of the same name.
/// </summary>
internal static class BundleCloner
{
    private const int MeshClassId = 43;
    private const int Texture2DClassId = 28;

    /// <summary>The AssetBundle asset, which every bundle keeps at path id 1.</summary>
    private const long BundleAssetPathId = 1;

    public static CloneResult Clone(CloneRequest request)
    {
        var manager = new AssetsManager();
        var bundleInst = manager.LoadBundleFile(request.SourcePath, true);
        var bundle = bundleInst.file;
        var dirs = bundle.BlockAndDirInfo.DirectoryInfos;

        var assetsIndex = Enumerable.Range(0, dirs.Count).First(bundle.IsAssetsFile);
        var fileInst = manager.LoadAssetsFileFromBundle(bundleInst, assetsIndex, false);
        var file = fileInst.file;
        var sourceCab = dirs[assetsIndex].Name;

        // Everything the recolour does not need: the meshes, and the maps other than the albedo.
        var dropped = new Dictionary<long, string>();
        foreach (var info in file.AssetInfos)
        {
            if (request.Replacements.ContainsKey(info.PathId))
            {
                continue;
            }

            if (info.TypeId is MeshClassId or Texture2DClassId)
            {
                dropped[info.PathId] = NameOf(manager, fileInst, info);
            }
        }

        // Where each reference that has to move ends up: (file it reads now, asset) -> file it
        // reads instead. A file id is the 1-based index into the externals list, 0 being this file.
        var moves = new Dictionary<(int FileId, long PathId), int>();

        var sourceFileId = AddExternal(file, sourceCab);
        foreach (var pathId in dropped.Keys)
        {
            moves[(0, pathId)] = sourceFileId;
        }

        // A carrier whose textures live in a bundle of their own: the albedo now comes from our
        // clone of that bundle. Only the albedo - the normal and specular maps are not in the
        // clone, and moving them with it would leave the material with no maps at all.
        foreach (var reroute in request.Reroutes)
        {
            var from = file.Metadata.Externals.FindIndex(e => e.PathName.EndsWith("/" + reroute.FromCab, StringComparison.Ordinal));
            if (from < 0)
            {
                throw new InvalidOperationException($"{Path.GetFileName(request.SourcePath)} does not reference {reroute.FromCab}");
            }

            var to = AddExternal(file, reroute.ToCab);
            foreach (var pathId in reroute.PathIds)
            {
                moves[(from + 1, pathId)] = to;
            }

            Console.WriteLine($"  {reroute.PathIds.Count} asset(s) taken from {reroute.ToCab} " +
                              $"instead of {reroute.FromCab}");
        }

        // The game finds an item's model by the asset named like the bundle (an empty Prefab.rcid,
        // which is what every vanilla item has), so the clone's own asset is named after it.
        var bundleInfo = file.GetAssetInfo(BundleAssetPathId);
        var mainAssetPathId = request.AssetName.Length == 0
            ? 0
            : manager.GetBaseField(fileInst, bundleInfo)["m_Container"]["Array"][0]["second"]["asset"]["m_PathID"].AsLong;

        var replaced = new List<string>();
        var tinted = new List<string>();
        foreach (var info in file.AssetInfos.ToList())
        {
            if (dropped.ContainsKey(info.PathId))
            {
                continue;
            }

            var field = manager.GetBaseField(fileInst, info);
            var changed = Repoint(field, moves);

            if (request.Replacements.TryGetValue(info.PathId, out var png))
            {
                replaced.Add($"replaced {NameOf(field)} <- {Path.GetFileName(png)} {ReplaceTexture(field, png)}");
                changed = true;
            }

            if (request.Tint is { } tint && field.TypeName == "Material")
            {
                tinted.Add($"tinted {NameOf(field)} _Color {SetTint(field, tint)}");
                changed = true;
            }

            if (info.PathId == mainAssetPathId)
            {
                field["m_Name"].AsString = request.AssetName;
                changed = true;
            }
            else if (info.PathId == BundleAssetPathId && mainAssetPathId != 0)
            {
                field["m_Name"].AsString = request.BundleKey;
                field["m_AssetBundleName"].AsString = request.BundleKey;
                field["m_Container"]["Array"][0]["first"].AsString =
                    $"Assets/ModularVests/Vests/{request.AssetName}.prefab";
                changed = true;
            }

            if (changed)
            {
                info.SetNewData(field);
            }
        }

        foreach (var pathId in dropped.Keys)
        {
            file.Metadata.RemoveAssetInfo(file.GetAssetInfo(pathId));
        }

        // Unity keys loaded bundles by the names of the files inside them: a clone that kept the
        // source's names would refuse to load next to it ("another AssetBundle with the same files").
        var cab = CabFor(request.BundleKey);
        dirs[assetsIndex].Name = cab;
        foreach (var dir in dirs)
        {
            if (dir.Name.StartsWith(sourceCab, StringComparison.Ordinal) && dir.Name != sourceCab)
            {
                dir.Name = cab + dir.Name[sourceCab.Length..];
                dir.SetNewData([]); // the streamed data went with the meshes and the maps
            }
        }

        dirs[assetsIndex].SetNewData(file);
        Write(bundle, request.OutputPath);

        return new CloneResult
        {
            Key = request.BundleKey,
            Cab = cab,
            SourceCab = sourceCab,
            Size = new FileInfo(request.OutputPath).Length,
            Dropped = [.. dropped.Values.Order(StringComparer.Ordinal)],
            Replaced = replaced,
            Tinted = tinted,
            Externals = [.. file.Metadata.Externals.Select(NameOfExternal)],
        };
    }

    /// <summary>The file name a bundle key gets inside the bundle: stable, and unlike any other.</summary>
    public static string CabFor(string bundleKey) =>
        "CAB-" + Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes("modularvests:" + bundleKey)));

    private static string ArchivePath(string cab) => $"archive:/{cab}/{cab}";

    /// <summary>The file name out of an <c>archive:/CAB-.../CAB-...</c> reference.</summary>
    private static string NameOfExternal(AssetsFileExternal external) =>
        external.PathName[(external.PathName.LastIndexOf('/') + 1)..];

    private static int AddExternal(AssetsFile file, string cab)
    {
        var path = ArchivePath(cab);
        var existing = file.Metadata.Externals.FindIndex(e => e.PathName == path);
        if (existing >= 0)
        {
            return existing + 1;
        }

        file.Metadata.Externals.Add(new AssetsFileExternal
        {
            Guid = new GUID128(),
            Type = AssetsFileExternalType.Normal,
            VirtualAssetPathName = "",
            PathName = path,
            OriginalPathName = path,
        });
        return file.Metadata.Externals.Count;
    }

    /// <summary>
    /// Points every reference to a dropped asset at the bundle it stayed in. The whole asset is
    /// walked rather than the fields we know about: the preload table, the material and the
    /// renderers all carry them, and a reference left behind would load as null in the game.
    /// </summary>
    private static bool Repoint(AssetTypeValueField field, Dictionary<(int FileId, long PathId), int> moves)
    {
        if (field.TypeName.StartsWith("PPtr<", StringComparison.Ordinal) && field.Children.Count == 2)
        {
            var fileId = field["m_FileID"];
            var pathId = field["m_PathID"];
            if (moves.TryGetValue((fileId.AsInt, pathId.AsLong), out var to))
            {
                fileId.AsInt = to;
                return true;
            }

            return false;
        }

        var changed = false;
        foreach (var child in field.Children)
        {
            changed |= Repoint(child, moves);
        }

        return changed;
    }

    /// <summary>
    /// Sets the material's albedo multiplier. It is an entry of the saved property sheet, which
    /// every material of the game carries, so it is set in place rather than added.
    /// </summary>
    private static string SetTint(AssetTypeValueField material, (double R, double G, double B) tint)
    {
        var colors = material["m_SavedProperties"]["m_Colors"]["Array"];
        var entry = colors.Children.FirstOrDefault(c => c["first"].AsString == "_Color")
            ?? throw new InvalidOperationException($"{NameOf(material)} has no _Color to set");

        var was = entry["second"]["r"].AsFloat;
        entry["second"]["r"].AsFloat = (float)tint.R;
        entry["second"]["g"].AsFloat = (float)tint.G;
        entry["second"]["b"].AsFloat = (float)tint.B;
        return $"{was:0.###} -> {tint.R:0.###}, {tint.G:0.###}, {tint.B:0.###}";
    }

    /// <summary>
    /// Writes the recoloured albedo into the texture, in the format and mip count it already had,
    /// and inline: the streamed .resS the source kept it in is not part of the clone.
    /// </summary>
    private static string ReplaceTexture(AssetTypeValueField texture, string pngPath)
    {
        var format = texture["m_TextureFormat"].AsInt;
        if (!Dxt.IsSupported(format))
        {
            throw new InvalidOperationException($"{NameOf(texture)}: texture format {format} is not one this tool writes");
        }

        var width = texture["m_Width"].AsInt;
        var height = texture["m_Height"].AsInt;
        var mips = texture["m_MipCount"].AsInt;
        var data = Dxt.Encode(pngPath, format, width, height, mips);

        texture["image data"].AsByteArray = data;
        texture["m_CompleteImageSize"].AsUInt = (uint)data.Length;

        var stream = texture["m_StreamData"];
        stream["offset"].AsULong = 0;
        stream["size"].AsUInt = 0;
        stream["path"].AsString = "";

        return $"({width}x{height}, format {format}, {mips} mips, {data.Length} bytes)";
    }

    private static string NameOf(AssetsManager manager, AssetsFileInstance file, AssetFileInfo info) =>
        NameOf(manager.GetBaseField(file, info));

    private static string NameOf(AssetTypeValueField field)
    {
        var name = field["m_Name"];
        return name.IsDummy ? "(unnamed)" : name.AsString;
    }

    /// <summary>
    /// Writes the bundle the way the game ships them: LZ4, one block. The edits live in replacers,
    /// which only the plain write applies, so the packed copy is made from that.
    /// </summary>
    private static void Write(AssetBundleFile bundle, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var temp = outputPath + ".raw";
        try
        {
            using (var writer = new AssetsFileWriter(temp))
            {
                bundle.Write(writer);
            }

            var unpacked = new AssetBundleFile();
            unpacked.Read(new AssetsFileReader(File.OpenRead(temp)));
            using (var writer = new AssetsFileWriter(outputPath))
            {
                unpacked.Pack(writer, AssetBundleCompressionType.LZ4);
            }

            unpacked.Close();
        }
        finally
        {
            File.Delete(temp);
        }
    }
}

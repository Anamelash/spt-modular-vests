using AssetsTools.NET;
using AssetsTools.NET.Extra;
using ModularVests.BundleTool;

// Recolours a game bundle without repacking the model: see BundleCloner.
// Called from build/build-vest-bundles.ps1; the commands are usable on their own while
// working out what a bundle holds.
//
//   clone --source <bundle> --out <bundle> --key <bundle key>
//         [--name <asset name>] [--report <json>] [--tint <r>,<g>,<b>]
//         [--replace <pathId>=<png>]... [--reroute <fromCab>=<toCab>:<pathId>,...]...
//   mean  --png <file>
//   verify --source <bundle>
//   dump  --source <bundle> [--asset <pathId>]...
//   cab   --key <bundle key> | --source <bundle>

try
{
    return Run(args);
}
catch (Exception e)
{
    Console.Error.WriteLine(e is InvalidOperationException or ArgumentException
        ? $"error: {e.Message}"
        : $"error: {e}");
    return 1;
}

static int Run(string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("usage: clone | verify | mean | dump | orient | cab");
        return 1;
    }

    var options = ParseOptions(args.Skip(1));
    switch (args[0])
    {
        case "clone":
            return Clone(options);
        case "dump":
            return Dump(options);
        case "verify":
            return Verify(options);
        case "mean":
        {
            var (r, g, b) = Dxt.Mean(Single(options, "png"));
            // invariant: the build script reads this back, and must not depend on the locale
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:0.###} {1:0.###} {2:0.###} {3:0.###}", r, g, b, Dxt.Luma(r, g, b)));
            return 0;
        }

        case "orient":
            return Orientation.Compare(Single(options, "source"), long.Parse(Single(options, "asset")), Single(options, "png"));
        case "cab":
            Console.WriteLine(Many(options, "source").FirstOrDefault() is { } bundle
                ? OwnCab(bundle)
                : BundleCloner.CabFor(Single(options, "key")));
            return 0;
        default:
            Console.Error.WriteLine($"unknown command '{args[0]}'");
            return 1;
    }
}

static int Clone(Dictionary<string, List<string>> options)
{
    var source = Single(options, "source");
    var replacements = new Dictionary<long, string>();
    foreach (var pair in Many(options, "replace"))
    {
        var (left, right) = Split(pair);
        replacements[long.Parse(left)] = right;
    }

    // --reroute <fromCab>=<toCab>:<pathId>[,<pathId>...]
    var reroutes = new List<Reroute>();
    foreach (var option in Many(options, "reroute"))
    {
        var (from, rest) = Split(option);
        var colon = rest.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            throw new ArgumentException($"'{option}' names no assets to take from {rest}");
        }

        reroutes.Add(new Reroute(from, rest[..colon],
            rest[(colon + 1)..].Split(',').Select(long.Parse).ToList()));
    }

    (double, double, double)? tint = null;
    if (Many(options, "tint").FirstOrDefault() is { } value)
    {
        var parts = value.Split(',');
        if (parts.Length != 3)
        {
            throw new ArgumentException($"--tint takes three numbers, not '{value}'");
        }

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        tint = (double.Parse(parts[0], culture), double.Parse(parts[1], culture),
            double.Parse(parts[2], culture));
    }

    var result = BundleCloner.Clone(new CloneRequest
    {
        SourcePath = source,
        OutputPath = Single(options, "out"),
        BundleKey = Single(options, "key"),
        AssetName = Many(options, "name").FirstOrDefault() ?? "",
        Replacements = replacements,
        Reroutes = reroutes,
        Tint = tint,
    });

    Console.WriteLine($"{Path.GetFileName(source)} -> {Single(options, "key")}  {result.Size / 1024} KB  ({result.Cab})");
    foreach (var line in result.Replaced.Concat(result.Tinted))
    {
        Console.WriteLine($"  {line}");
    }

    Console.WriteLine($"  left in the source: {result.Dropped.Count} assets - {string.Join(", ", result.Dropped)}");

    var report = Many(options, "report").FirstOrDefault();
    if (report != null)
    {
        File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            }));
    }

    return 0;
}

/// <summary>
/// Reads a clone back and reports anything that would load as null in the game: a reference to
/// an asset that is neither in this bundle nor in one it names, or a texture whose bytes are not
/// the size its own header says.
/// </summary>
static int Verify(Dictionary<string, List<string>> options)
{
    var path = Single(options, "source");
    var manager = new AssetsManager();
    var bundleInst = manager.LoadBundleFile(path, true);
    var dirs = bundleInst.file.BlockAndDirInfo.DirectoryInfos;
    var assetsIndex = Enumerable.Range(0, dirs.Count).First(bundleInst.file.IsAssetsFile);
    var fileInst = manager.LoadAssetsFileFromBundle(bundleInst, assetsIndex, false);
    var file = fileInst.file;

    var present = file.AssetInfos.Select(i => i.PathId).ToHashSet();
    var externals = file.Metadata.Externals.Count;
    var problems = new List<string>();

    foreach (var info in file.AssetInfos)
    {
        var field = manager.GetBaseField(fileInst, info);
        var name = field["m_Name"].IsDummy ? info.PathId.ToString() : field["m_Name"].AsString;
        CheckPointers(field, present, externals, name, problems);

        if (field.TypeName == "Texture2D")
        {
            var expected = Dxt.MipChainSize(field["m_TextureFormat"].AsInt, field["m_Width"].AsInt,
                field["m_Height"].AsInt, field["m_MipCount"].AsInt);
            var actual = field["image data"].AsByteArray.Length;
            var streamed = field["m_StreamData"]["size"].AsUInt;
            if (actual != expected || streamed != 0)
            {
                problems.Add($"{name}: {actual} bytes inline and {streamed} streamed, expected {expected} inline");
            }
        }
    }

    foreach (var problem in problems)
    {
        Console.Error.WriteLine($"  {problem}");
    }

    Console.WriteLine($"{Path.GetFileName(path)}: {file.AssetInfos.Count} assets, {externals} externals, {problems.Count} problems");
    return problems.Count == 0 ? 0 : 1;
}

static void CheckPointers(AssetTypeValueField field, HashSet<long> present, int externals, string owner, List<string> problems)
{
    if (field.TypeName.StartsWith("PPtr<", StringComparison.Ordinal) && field.Children.Count == 2)
    {
        var fileId = field["m_FileID"].AsInt;
        var pathId = field["m_PathID"].AsLong;
        if (pathId == 0)
        {
            return;
        }

        if (fileId == 0 && !present.Contains(pathId))
        {
            problems.Add($"{owner}.{field.FieldName} points at {pathId}, which is not in this bundle");
        }
        else if (fileId > externals)
        {
            problems.Add($"{owner}.{field.FieldName} points at file {fileId}, which is not declared");
        }

        return;
    }

    foreach (var child in field.Children)
    {
        CheckPointers(child, present, externals, owner, problems);
    }
}

/// <summary>The name of the file inside a bundle - what other bundles reference it by.</summary>
static string OwnCab(string path)
{
    var manager = new AssetsManager();
    var bundleInst = manager.LoadBundleFile(path, true);
    var dirs = bundleInst.file.BlockAndDirInfo.DirectoryInfos;
    return dirs[Enumerable.Range(0, dirs.Count).First(bundleInst.file.IsAssetsFile)].Name;
}

static int Dump(Dictionary<string, List<string>> options)
{
    var wanted = Many(options, "asset").Select(long.Parse).ToHashSet();
    var manager = new AssetsManager();
    var bundleInst = manager.LoadBundleFile(Single(options, "source"), true);
    var bundle = bundleInst.file;
    var dirs = bundle.BlockAndDirInfo.DirectoryInfos;

    foreach (var dir in dirs)
    {
        Console.WriteLine($"file {dir.Name}  {dir.DecompressedSize} bytes");
    }

    var assetsIndex = Enumerable.Range(0, dirs.Count).First(bundle.IsAssetsFile);
    var fileInst = manager.LoadAssetsFileFromBundle(bundleInst, assetsIndex, false);
    var file = fileInst.file;

    Console.WriteLine($"unity {file.Metadata.UnityVersion}, type tree {file.Metadata.TypeTreeEnabled}, {file.AssetInfos.Count} assets");
    for (var i = 0; i < file.Metadata.Externals.Count; i++)
    {
        Console.WriteLine($"  external {i + 1}: {file.Metadata.Externals[i].PathName}");
    }

    foreach (var info in file.AssetInfos)
    {
        var field = manager.GetBaseField(fileInst, info);
        var name = field["m_Name"].IsDummy ? "" : field["m_Name"].AsString;
        if (wanted.Count == 0)
        {
            Console.WriteLine($"  {info.PathId,22}  {field.TypeName,-22} {info.ByteSize,9}  {name}");
            continue;
        }

        if (wanted.Contains(info.PathId))
        {
            Console.WriteLine($"=== {info.PathId} {field.TypeName} {name}");
            Print(field, 1);
        }
    }

    return 0;
}

static void Print(AssetTypeValueField field, int depth)
{
    if (depth > 7)
    {
        return;
    }

    var pad = new string(' ', depth * 2);
    foreach (var child in field.Children)
    {
        if (child.TypeName.StartsWith("PPtr<", StringComparison.Ordinal) && child.Children.Count == 2)
        {
            Console.WriteLine($"{pad}{child.FieldName} = ({child["m_FileID"].AsInt}, {child["m_PathID"].AsLong})");
        }
        else if (child.Children.Count == 0)
        {
            Console.WriteLine($"{pad}{child.FieldName} = {child.AsString}");
        }
        else
        {
            Console.WriteLine($"{pad}{child.FieldName}: {child.TypeName}[{child.Children.Count}]");
            Print(child, depth + 1);
        }
    }
}

static Dictionary<string, List<string>> ParseOptions(IEnumerable<string> args)
{
    var options = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    string? current = null;
    foreach (var arg in args)
    {
        if (arg.StartsWith("--", StringComparison.Ordinal))
        {
            current = arg[2..];
            if (!options.ContainsKey(current))
            {
                options[current] = [];
            }
        }
        else if (current != null)
        {
            options[current].Add(arg);
        }
    }

    return options;
}

static string Single(Dictionary<string, List<string>> options, string name) =>
    options.TryGetValue(name, out var values) && values.Count == 1 ? values[0]
        : throw new ArgumentException($"--{name} is required, once");

static List<string> Many(Dictionary<string, List<string>> options, string name) =>
    options.TryGetValue(name, out var values) ? values : [];

static (string Left, string Right) Split(string pair)
{
    var at = pair.IndexOf('=', StringComparison.Ordinal);
    return at < 0 ? throw new ArgumentException($"'{pair}' is not <left>=<right>") : (pair[..at], pair[(at + 1)..]);
}

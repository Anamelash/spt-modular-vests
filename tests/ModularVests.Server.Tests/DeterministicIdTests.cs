using System.Text.RegularExpressions;
using ModularVests.Server.Config;
using ModularVests.Server.Services;
using Xunit;

namespace ModularVests.Server.Tests;

public partial class DeterministicIdTests
{
    [GeneratedRegex("^[0-9a-f]{24}$")]
    private static partial Regex MongoIdPattern();

    [GeneratedRegex("[0-9a-fA-F]{24}")]
    private static partial Regex HexRun();

    [Fact]
    public void Same_key_gives_the_same_id()
    {
        Assert.Equal(DeterministicId.For("vest:6b45"), DeterministicId.For("vest:6b45"));
        Assert.NotEqual(DeterministicId.For("vest:6b45"), DeterministicId.For("vest:6b46"));
    }

    [Fact]
    public void Id_is_a_valid_mongo_id()
    {
        Assert.Matches(MongoIdPattern(), DeterministicId.For("pouch:afak"));
    }

    /// <summary>
    /// Pins the algorithm: if this value ever changes, every shipped item in every profile
    /// is orphaned. Never "fix" this test by updating the constant.
    /// </summary>
    [Fact]
    public void Algorithm_is_frozen()
    {
        // SHA-1("modularvests:vest:6b45"), first 12 bytes
        var expected = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA1.HashData("modularvests:vest:6b45"u8.ToArray()), 0, 12);
        Assert.Equal(expected, DeterministicId.For("vest:6b45"));
        Assert.Equal(FrozenVestId, DeterministicId.For("vest:6b45"));
    }

    private const string FrozenVestId = "dd9d6dc7bfb753bc5da72eb8";

    /// <summary>
    /// The eight pouch slots the prototype shipped became clusters 1 and 2 without a rename:
    /// pouches attached to them in existing profiles stay where they are only while these ids
    /// hold. Never "fix" this test by updating the constants.
    /// </summary>
    [Theory]
    [InlineData(1, "338d2d16174093edcab023a6")]
    [InlineData(2, "b3940b0cd24f897f33084178")]
    [InlineData(3, "f09b97ea5d2aa2fea78e4081")]
    [InlineData(4, "bf5435b99053b48b7090ab29")]
    [InlineData(5, "9a01df0528b8b319277e2227")]
    [InlineData(6, "44eab96d2e1f656a306defe7")]
    [InlineData(7, "5a41a68e88e0587b18d4d416")]
    [InlineData(8, "b743f506da6bef17c66e9fe2")]
    public void Prototype_slot_ids_are_kept(int number, string id)
    {
        Assert.Equal(id, DeterministicId.For(DeterministicId.VestSlotKey("6b45", ClusterGrid.SlotName(number))));
        Assert.Contains(id, ShippedIds().Values);
    }

    [Fact]
    public void Every_pouch_cell_has_its_own_id()
    {
        var vest = ItemsConfig.Parse(File.ReadAllText(TestPaths.ItemsConfig)).Vests[0];
        var ids = Enumerable.Range(1, vest.Clusters * ClusterGrid.CellsPerCluster)
            .Select(n => DeterministicId.For(DeterministicId.VestSlotKey(vest.Key, ClusterGrid.SlotName(n))))
            .ToList();
        Assert.Equal(16, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Shipped_ids_are_unique()
    {
        var ids = ShippedIds();
        Assert.Equal(ids.Count, ids.Values.Distinct().Count());
    }

    /// <summary>No shipped id may appear anywhere in the vanilla or WTT databases.</summary>
    [Fact]
    public void Shipped_ids_do_not_collide_with_game_or_wtt_databases()
    {
        var gameDir = TestPaths.GameDir;
        if (gameDir == null)
        {
            return; // no install: nothing to compare against
        }

        var roots = new[]
        {
            Path.Combine(gameDir, "SPT_Runtime", "SPT_Data", "database"),
            Path.Combine(gameDir, "SPT_Runtime", "user", "mods", "WTT-ContentBackport", "db"),
        };

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            {
                foreach (Match m in HexRun().Matches(File.ReadAllText(file)))
                {
                    known.Add(m.Value);
                }
            }
        }

        Assert.NotEmpty(known);
        var collisions = ShippedIds().Where(kv => known.Contains(kv.Value)).Select(kv => kv.Key).ToList();
        Assert.Empty(collisions);
    }

    /// <summary>key -> id, for every id the shipped config produces.</summary>
    internal static Dictionary<string, string> ShippedIds()
    {
        var config = ItemsConfig.Parse(File.ReadAllText(TestPaths.ItemsConfig));
        var keys = new List<string>();

        foreach (var pouch in config.AllPouches())
        {
            keys.Add(DeterministicId.PouchKey(pouch.Key));
            keys.AddRange(pouch.Grids.Select(g => DeterministicId.PouchGridKey(pouch.Key, g.Name)));
            keys.Add(DeterministicId.AssortKey(DeterministicId.PouchKey(pouch.Key)));
        }

        // the plate slots copied from the 6B45 are keyed by their names
        string[] donorSlots = ["Front_plate", "Back_plate", "Left_side_plate", "Right_side_plate", "Soft_armor_front",
            "Soft_armor_back", "Soft_armor_left", "soft_armor_right", "Collar"];

        // the trader sells the rig assembled: one row per required slot (the soft armor)
        string[] requiredSlots = ["Soft_armor_front", "Soft_armor_back", "Soft_armor_left", "soft_armor_right",
            "Collar"];

        foreach (var vest in config.Vests)
        {
            var vestKey = DeterministicId.VestKey(vest.Key);
            keys.Add(vestKey);
            keys.Add(DeterministicId.AssortKey(vestKey));
            keys.AddRange(Enumerable.Range(1, vest.Clusters * ClusterGrid.CellsPerCluster)
                .Select(n => DeterministicId.VestSlotKey(vest.Key, ClusterGrid.SlotName(n))));
            keys.AddRange(donorSlots.Select(s => DeterministicId.VestSlotKey(vest.Key, s)));
            keys.AddRange(requiredSlots.Select(s => DeterministicId.AssortSlotKey(vestKey, s)));

            // the default preset lists the same parts as the trader row
            keys.Add(DeterministicId.PresetKey(vest.Key));
            keys.Add(DeterministicId.PresetRootKey(vest.Key));
            keys.AddRange(requiredSlots.Select(s => DeterministicId.PresetSlotKey(vest.Key, s)));
        }

        keys.Add(DeterministicId.TraderKey(TraderConfigFile.Parse(File.ReadAllText(TestPaths.TraderConfig)).Key));

        return keys.ToDictionary(k => k, DeterministicId.For);
    }
}

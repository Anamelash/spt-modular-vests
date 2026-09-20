using ModularVests.Client.Bones;
using Xunit;

namespace ModularVests.Server.Tests;

public sealed class BoneStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mv-bones-" + Guid.NewGuid().ToString("N"));

    private string File_ => Path.Combine(_dir, "bones.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Roundtrip()
    {
        var store = new BoneStore(File_);
        store.Load();
        var pose = new BonePose { Position = [0.1f, -0.2f, 0.3f], Rotation = [0f, 90f, -45f] };
        store.Set("rig", "mod_pouch_1", pose);
        Assert.True(store.Dirty);
        store.Save();
        Assert.False(store.Dirty);

        // saving twice goes through the replace path
        store.Set("rig", "mod_pouch_2", pose);
        store.Save();

        var again = new BoneStore(File_);
        again.Load();
        Assert.True(again.TryGet("rig", "mod_pouch_1", out var read));
        Assert.Equal(pose.Position, read.Position);
        Assert.Equal(pose.Rotation, read.Rotation);
        Assert.True(again.TryGet("rig", "mod_pouch_2", out _));
        Assert.False(File.Exists(File_ + ".tmp"));
    }

    [Fact]
    public void A_rig_is_copied_and_replaced_as_a_whole()
    {
        var store = new BoneStore(File_);
        store.Load();
        var pose = new BonePose { Position = [0.1f, -0.2f, 0.3f], Rotation = [0f, 90f, -45f], Space = BonePose.AnchorSpace };
        store.Set("rig", "mod_pouch_1", pose);
        store.Set("rig", "mod_pouch_2", pose);
        store.Set("other", "mod_pouch_1", pose);

        var copy = store.GetRig("rig");
        Assert.Equal(2, copy.Count);
        copy["mod_pouch_1"].Position[0] = 9f; // a copy: the store is untouched
        Assert.True(store.TryGet("rig", "mod_pouch_1", out var kept));
        Assert.Equal(0.1f, kept.Position[0]);

        // the backup of the rig replaces whatever it has now
        var backup = store.GetRig("rig");
        store.Remove("rig", "mod_pouch_1");
        store.Set("rig", "mod_pouch_3", pose);
        store.SetRig("rig", backup);
        Assert.True(store.TryGet("rig", "mod_pouch_1", out _));
        Assert.False(store.TryGet("rig", "mod_pouch_3", out _));
        Assert.True(store.TryGet("other", "mod_pouch_1", out _));
        Assert.Empty(store.GetRig("unknown"));
    }

    [Fact]
    public void Kits_of_one_model_share_a_layout_and_other_rigs_keep_theirs()
    {
        var store = new BoneStore(File_);
        store.Load();
        var pose = new BonePose { Position = [0.1f, 0.2f, 0.3f], Rotation = [0f, 90f, 0f], Space = BonePose.AnchorSpace };
        store.Set("6b45", "mod_pouch_1", pose);
        store.SetRig("iotv_fp", store.GetRig("6b45")); // copied, not shared
        store.Share("iotv_assault", "iotv_fp");
        store.Share("iotv_hm", "iotv_fp");

        // an edit through any kit is the edit of all three, and of nothing else
        var moved = new BonePose { Position = [0.5f, 0.5f, 0.5f], Rotation = [0f, 0f, 0f], Space = BonePose.AnchorSpace };
        store.Set("iotv_hm", "mod_pouch_1", moved);
        foreach (var kit in new[] { "iotv_fp", "iotv_assault", "iotv_hm" })
        {
            Assert.True(store.TryGet(kit, "mod_pouch_1", out var read));
            Assert.Equal(0.5f, read.Position[0]);
        }

        Assert.True(store.TryGet("6b45", "mod_pouch_1", out var own));
        Assert.Equal(0.1f, own.Position[0]);
        Assert.Equal("iotv_fp", store.LayoutOf("iotv_assault"));
        Assert.Equal("6b45", store.LayoutOf("6b45"));

        // and it survives a save
        store.Save();
        var again = new BoneStore(File_);
        again.Load();
        Assert.True(again.TryGet("iotv_assault", "mod_pouch_1", out var reread));
        Assert.Equal(0.5f, reread.Position[0]);
        Assert.Equal("iotv_fp", again.LayoutOf("iotv_hm"));
    }

    [Fact]
    public void A_chained_alias_is_dropped_with_a_warning()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, """{ "a": "b", "b": "c", "c": {} }""");
        var warnings = new List<string>();
        var store = new BoneStore(File_, warnings.Add);
        store.Load();
        Assert.Equal("a", store.LayoutOf("a")); // a -> b -> c is a chain: a gets a layout of its own
        Assert.Equal("c", store.LayoutOf("b"));
        Assert.Single(warnings);
    }

    [Fact]
    public void Missing_file_warns_and_defaults()
    {
        var warnings = new List<string>();
        var store = new BoneStore(File_, warnings.Add);
        store.Load();
        Assert.Single(warnings);
        Assert.False(store.TryGet("rig", "mod_pouch_1", out _));
    }

    [Fact]
    public void Broken_file_warns_and_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "{ this is not json");
        var warnings = new List<string>();
        var store = new BoneStore(File_, warnings.Add);
        store.Load();
        Assert.Single(warnings);
        Assert.False(store.TryGet("rig", "mod_pouch_1", out _));
    }

    [Fact]
    public void Malformed_entry_is_skipped_but_the_rest_loads()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_,
            """{ "rig": { "mod_pouch_1": { "pos": [1, 2], "rot": [0, 0, 0] }, "mod_pouch_2": { "pos": [1, 2, 3], "rot": [0, 0, 0] } } }""");
        var warnings = new List<string>();
        var store = new BoneStore(File_, warnings.Add);
        store.Load();
        Assert.Single(warnings);
        Assert.False(store.TryGet("rig", "mod_pouch_1", out _));
        Assert.True(store.TryGet("rig", "mod_pouch_2", out _));
    }

    private static BonePose Pose(float x) => new() { Position = [x, 0f, 0f], Rotation = [0f, 0f, 0f] };

    [Fact]
    public void Remove_returns_the_slot_to_default()
    {
        var store = new BoneStore(File_);
        store.Set("rig", "mod_pouch_1", Pose(0.1f));
        store.Save();
        store.Remove("rig", "mod_pouch_1");
        Assert.True(store.Dirty);
        Assert.False(store.TryGet("rig", "mod_pouch_1", out _));
    }

    /// <summary>A worn ("dress") pose of an older file is ignored, and gone after the next save.</summary>
    [Fact]
    public void A_dress_pose_of_an_old_file_is_ignored_and_dropped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_,
            """{ "rig": { "mod_pouch_6": { "pos": [0.11, -0.04, -0.18], "rot": [70, 90, -91], "space": "anchor", "dress": { "pos": [1, 2, 3], "rot": [0, 0, 0] } } } }""");
        var warnings = new List<string>();
        var store = new BoneStore(File_, warnings.Add);
        store.Load();
        Assert.Empty(warnings);
        Assert.True(store.TryGet("rig", "mod_pouch_6", out var pose));
        Assert.True(pose.IsAnchored);
        Assert.Equal(0.11f, pose.Position[0]);

        store.Set("rig", "mod_pouch_6", pose);
        store.Save();
        Assert.DoesNotContain("dress", File.ReadAllText(File_));
    }

    private static float[] DefaultAt(int cluster, int position) =>
        BoneStore.DefaultFor(ClusterGrid.SlotName(cluster, position))!.Position;

    private static float[] ClusterCentre(int cluster)
    {
        var cells = Enumerable.Range(1, 4).Select(p => DefaultAt(cluster, p)).ToArray();
        return [cells.Average(c => c[0]), cells.Average(c => c[1])];
    }

    [Fact]
    public void Default_layout_is_mirrored_pairs_of_2x2_clusters()
    {
        var poses = Enumerable.Range(1, 16).Select(n => BoneStore.DefaultFor(ClusterGrid.SlotName(n))!).ToArray();
        Assert.All(poses, p => Assert.Equal(0f, p.Position[2]));
        Assert.All(poses, p => Assert.Equal([0f, 0f, 0f], p.Rotation));

        // no two cells share a spot
        Assert.Equal(16, poses.Select(p => (p.Position[0], p.Position[1])).Distinct().Count());

        // odd clusters on the wearer's left (-x), mirrored by the even ones
        for (var cluster = 1; cluster <= 3; cluster += 2)
        {
            Assert.True(ClusterCentre(cluster)[0] < 0f);
            Assert.Equal(-ClusterCentre(cluster)[0], ClusterCentre(cluster + 1)[0], 4);
            Assert.Equal(ClusterCentre(cluster)[1], ClusterCentre(cluster + 1)[1], 4);

            // a cell and its mirror are reflections of each other
            for (var p = 1; p <= 4; p++)
            {
                var cell = DefaultAt(cluster, p);
                var mirror = DefaultAt(cluster + 1, ClusterGrid.MirrorPosition(p));
                Assert.Equal(-cell[0], mirror[0], 4);
                Assert.Equal(cell[1], mirror[1], 4);
            }
        }

        // the cummerbund pair is lower and wider than the chest pair
        Assert.True(ClusterCentre(3)[1] < ClusterCentre(1)[1]);
        Assert.True(Math.Abs(ClusterCentre(3)[0]) > Math.Abs(ClusterCentre(1)[0]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Default_cells_read_from_the_front(int cluster)
    {
        // seen from the front the viewer's right is the wearer's left (-x): cell 2 is there
        var c1 = DefaultAt(cluster, 1);
        var c2 = DefaultAt(cluster, 2);
        var c3 = DefaultAt(cluster, 3);
        var c4 = DefaultAt(cluster, 4);
        Assert.Equal(BoneStore.DefaultSpacing, c1[0] - c2[0], 4);
        Assert.Equal(c1[1], c2[1], 4);
        Assert.Equal(BoneStore.DefaultRowSpacing, c1[1] - c3[1], 4);
        Assert.Equal(c1[0], c3[0], 4);
        Assert.Equal(c2[0], c4[0], 4);
        Assert.Equal(c3[1], c4[1], 4);
    }

    [Theory]
    [InlineData("mod_pouch_0")]
    [InlineData("mod_equipment")]
    public void No_default_for_a_name_that_is_not_a_cell(string name)
    {
        Assert.Null(BoneStore.DefaultFor(name));
    }

    [Fact]
    public void Shipped_layout_loads_cleanly()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "bones.json");
        var warnings = new List<string>();
        var store = new BoneStore(shipped, warnings.Add);
        store.Load();
        Assert.Empty(warnings);
    }

    /// <summary>The three IOTV Gen4 kits share one layout; the 6B45 has its own.</summary>
    [Fact]
    public void Shipped_iotv_kits_share_a_layout_apart_from_the_6b45()
    {
        var store = new BoneStore(Path.Combine(AppContext.BaseDirectory, "bones.json"));
        store.Load();
        var rig6B45 = ModularVests.Server.Services.DeterministicId.For("vest:6b45");
        var kits = new[] { "iotv_fp", "iotv_assault", "iotv_hm" }
            .Select(k => ModularVests.Server.Services.DeterministicId.For("vest:" + k)).ToArray();

        Assert.All(kits, kit => Assert.Equal(kits[0], store.LayoutOf(kit)));
        Assert.Equal(rig6B45, store.LayoutOf(rig6B45));
        Assert.True(store.TryGet(kits[1], "mod_pouch_1", out _));
    }

    private static string Rig(string key) => ModularVests.Server.Services.DeterministicId.For("vest:" + key);

    /// <summary>
    /// Every rig of the line-up finds a layout: its own or, since a recolour is the same geometry,
    /// its carrier's through an alias. A rig that finds none would put its pouches in the default
    /// grid over the chest instead of where the author placed them.
    /// </summary>
    [Fact]
    public void Every_rig_of_the_line_up_has_a_layout()
    {
        var store = new BoneStore(Path.Combine(AppContext.BaseDirectory, "bones.json"));
        store.Load();
        var config = ModularVests.Server.Config.ItemsConfig.Parse(File.ReadAllText(TestPaths.ItemsConfig));

        var without = new List<string>();
        foreach (var vest in config.AllVests())
        {
            var tpl = Rig(vest.Key);
            var owner = store.LayoutOf(tpl);
            var cells = store.GetRig(owner).Count;
            if (cells != vest.Clusters * ClusterGrid.CellsPerCluster)
            {
                without.Add($"{vest.Key}: {cells} cells placed, {vest.Clusters} clusters");
            }

            // no chains: the rig an alias names must own its layout
            Assert.Equal(owner, store.LayoutOf(owner));
        }

        Assert.Empty(without);
    }

    /// <summary>The colours of one carrier share its layout; each carrier has its own.</summary>
    [Fact]
    public void Shipped_colours_share_their_carriers_layout()
    {
        var store = new BoneStore(Path.Combine(AppContext.BaseDirectory, "bones.json"));
        store.Load();
        (string owner, string[] colours)[] groups =
        [
            ("trooper_multicam", ["trooper_coyote"]),
            ("otv_ucp", ["otv_woodland", "otv_cce", "otv_3c"]),
            ("untar", ["untar_dbdu", "untar_marpat", "untar_wineleaf"]),
            ("thor", ["thor_masgray"]),
        ];
        foreach (var (owner, colours) in groups)
        {
            Assert.Equal(Rig(owner), store.LayoutOf(Rig(owner)));
            Assert.All(colours, c => Assert.Equal(Rig(owner), store.LayoutOf(Rig(c))));
        }

        foreach (var own in new[] { "gladiator_s", "6b43", "thor", "untar" })
        {
            Assert.Equal(16, store.GetRig(Rig(own)).Count);
        }

        // no cummerbund: the chest clusters only
        Assert.Equal(8, store.GetRig(Rig("trooper_multicam")).Count);
        Assert.Equal(8, store.GetRig(Rig("otv_ucp")).Count);
    }
}

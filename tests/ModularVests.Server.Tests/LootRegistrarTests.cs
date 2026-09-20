using ModularVests.Server.Config;
using ModularVests.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;

namespace ModularVests.Server.Tests;

/// <summary>
/// The loot tables are edited by transformers that may run again on every access, so the only
/// thing that matters as much as the weights is that a second run changes nothing.
/// </summary>
public class LootRegistrarTests
{
    private static readonly MongoId Reference = new("5751a25924597722c463c472");
    private static readonly MongoId Ours = new("aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly MongoId AlsoOurs = new("bbbbbbbbbbbbbbbbbbbbbbbb");

    private static ItemsConfig Items() => ItemsConfig.Parse(File.ReadAllText(TestPaths.ItemsConfig));

    private static BotsConfig Bots() => BotsConfig.Parse(File.ReadAllText(TestPaths.BotsConfig));

    private static StaticLootDetails Container(params (MongoId Tpl, float Weight)[] items) =>
        new()
        {
            ItemCountDistribution = [],
            ItemDistribution = items
                .Select(i => new ItemDistribution { Tpl = i.Tpl, RelativeProbability = i.Weight })
                .ToList(),
        };

    [Fact]
    public void A_container_weighs_our_items_against_the_reference()
    {
        var container = Container((Reference, 1000));
        LootRegistrar.AddWeighed(container, Reference, [Ours, AlsoOurs], 0.5, 5);

        var added = container.ItemDistribution.Where(d => d.Tpl == Ours || d.Tpl == AlsoOurs).ToList();
        Assert.Equal(2, added.Count);
        Assert.All(added, d => Assert.Equal(100, d.RelativeProbability)); // 1000 * 0.5 / 5
    }

    /// <summary>A weight always rounds up to something that can actually be rolled.</summary>
    [Fact]
    public void A_weight_is_never_below_one()
    {
        var container = Container((Reference, 2));
        LootRegistrar.AddWeighed(container, Reference, [Ours], 0.01, 5);
        Assert.Equal(1, container.ItemDistribution.Single(d => d.Tpl == Ours).RelativeProbability);
    }

    [Fact]
    public void A_container_without_the_reference_is_left_alone()
    {
        var container = Container((new MongoId("57347c77245977448d35f6e2"), 1000));
        LootRegistrar.AddWeighed(container, Reference, [Ours], 0.5, 5);
        Assert.Single(container.ItemDistribution);
    }

    [Fact]
    public void Adding_to_a_container_twice_is_adding_once()
    {
        var container = Container((Reference, 1000));
        LootRegistrar.AddWeighed(container, Reference, [Ours, AlsoOurs], 0.5, 5);
        var after = container.ItemDistribution.Select(d => (d.Tpl, d.RelativeProbability)).ToList();

        LootRegistrar.AddWeighed(container, Reference, [Ours, AlsoOurs], 0.5, 5);
        Assert.Equal(after, container.ItemDistribution.Select(d => (d.Tpl, d.RelativeProbability)));
    }

    [Fact]
    public void A_bot_pool_weighs_our_items_against_the_reference()
    {
        var pool = new Dictionary<MongoId, double> { [Reference] = 4000 };
        Assert.True(LootRegistrar.AddToPool(pool, Reference, [Ours], 0.2, 5));
        Assert.Equal(160, pool[Ours]);

        // twice is once
        Assert.False(LootRegistrar.AddToPool(pool, Reference, [Ours], 0.2, 5));
        Assert.Equal(160, pool[Ours]);

        // and a pool without the reference is not ours to touch
        var stranger = new Dictionary<MongoId, double>();
        Assert.False(LootRegistrar.AddToPool(stranger, Reference, [Ours], 0.2, 5));
        Assert.Empty(stranger);
    }

    /// <summary>
    /// A spawn point that offers the prototype also offers our assembled rigs, and only the
    /// first run adds them: the keys are the same every time.
    /// </summary>
    [Fact]
    public void A_spawn_point_gains_our_rigs_once()
    {
        var items = Items();
        var vest = items.AllVests().Single(v => v.Key == "6b45");
        var prototype = new MongoId(vest.CloneTpl);
        var tpl = ModConfigs.VestTpl(vest.Key);
        var rig = new LootRegistrar.RegisteredRig(vest.Key, tpl, prototype, Donor(), vest);
        var outfitter = new BotRigOutfitter(items, Bots());
        var trees = new Dictionary<MongoId, List<LootRegistrar.RigTree>>
        {
            [tpl] = new[] { 1, 2, 3 }.Select(n => LootRegistrar.LooseTree(rig, outfitter, n)).ToList(),
        };

        var looseLoot = PointWith(prototype, weight: 300);
        LootRegistrar.ApplyLooseTrees(looseLoot, [rig], [prototype], trees, 0.5, 3);

        var point = looseLoot.Spawnpoints!.Single();
        var keys = point.ItemDistribution!
            .Select(d => d.ComposedKey!.Key!)
            .Where(k => k.StartsWith(DeterministicId.Prefix, StringComparison.Ordinal))
            .ToList();
        Assert.Equal(["modularvests:6b45:1", "modularvests:6b45:2", "modularvests:6b45:3"], keys);
        Assert.All(point.ItemDistribution!.Where(d => keys.Contains(d.ComposedKey!.Key!)),
            d => Assert.Equal(50, d.RelativeProbability)); // 300 * 0.5 / 3

        // a rig on the ground is assembled: a root, its panels and plates, and its pouches
        var roots = point.Template!.Items!.Where(i => i.Template == tpl).ToList();
        Assert.Equal(3, roots.Count);
        Assert.All(roots, root =>
        {
            var children = point.Template.Items!.Where(i => i.ParentId != null && i.ParentId == root.Id).ToList();
            Assert.Contains(children, c => c.SlotId == "Soft_armor_front");
            Assert.Contains(children, c => c.SlotId!.StartsWith(ClusterGrid.SlotPrefix, StringComparison.Ordinal));
        });

        var before = Snapshot(looseLoot);
        LootRegistrar.ApplyLooseTrees(looseLoot, [rig], [prototype], trees, 0.5, 3);
        Assert.Equal(before, Snapshot(looseLoot));
    }

    /// <summary>A point that never held the prototype is not a place for our rigs either.</summary>
    [Fact]
    public void A_spawn_point_without_the_prototype_is_left_alone()
    {
        var items = Items();
        var vest = items.AllVests().Single(v => v.Key == "6b45");
        var prototype = new MongoId(vest.CloneTpl);
        var tpl = ModConfigs.VestTpl(vest.Key);
        var rig = new LootRegistrar.RegisteredRig(vest.Key, tpl, prototype, Donor(), vest);
        var trees = new Dictionary<MongoId, List<LootRegistrar.RigTree>>
        {
            [tpl] = [LootRegistrar.LooseTree(rig, new BotRigOutfitter(items, Bots()), 1)],
        };

        var looseLoot = PointWith(new MongoId("57347c77245977448d35f6e2"), weight: 300);
        var before = Snapshot(looseLoot);
        LootRegistrar.ApplyLooseTrees(looseLoot, [rig], [prototype], trees, 0.5, 3);
        Assert.Equal(before, Snapshot(looseLoot));
    }

    /// <summary>A donor with the slots a 6B45 has: two panels and two plates.</summary>
    private static TemplateItem Donor() => new()
    {
        Id = new MongoId("68948a95d8f2b85fb705e2a6"),
        Properties = new TemplateItemProperties
        {
            Slots =
            [
                Slot("Soft_armor_front", required: true, plate: "5f0c892565703e5c461894e9"),
                Slot("Soft_armor_back", required: true, plate: "5f0c892565703e5c461894e9"),
                Slot("Front_plate", required: false, plate: "656fa8d700d62bcd2e024084"),
                Slot("Back_plate", required: false, plate: "656fa8d700d62bcd2e024084"),
            ],
        },
    };

    private static Slot Slot(string name, bool required, string plate) => new()
    {
        Name = name,
        Required = required,
        Properties = new SlotProperties
        {
            Filters = [new SlotFilter { Plate = new MongoId(plate), Filter = [new MongoId(plate)] }],
        },
    };

    private static LooseLoot PointWith(MongoId tpl, double weight) => new()
    {
        Spawnpoints =
        [
            new Spawnpoint
            {
                LocationId = "test",
                Probability = 1,
                Template = new SpawnpointTemplate
                {
                    Id = "point",
                    Items = [new SptLootItem
                    {
                        Id = new MongoId("cccccccccccccccccccccccc"),
                        Template = tpl,
                        ComposedKey = "prototype",
                    }],
                },
                ItemDistribution =
                [
                    new LooseLootItemDistribution
                    {
                        ComposedKey = new ComposedKey { Key = "prototype" },
                        RelativeProbability = weight,
                    },
                ],
            },
        ],
    };

    private static string Snapshot(LooseLoot looseLoot) => string.Join("|", looseLoot.Spawnpoints!
        .Select(p => string.Join(",", p.Template!.Items!.Select(i => $"{i.Id}:{i.Template}:{i.SlotId}")) +
                     ";" + string.Join(",", p.ItemDistribution!
                         .Select(d => $"{d.ComposedKey!.Key}:{d.RelativeProbability}"))));
}

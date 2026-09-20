using ModularVests.Server.Config;
using ModularVests.Server.Services;
using Xunit;

namespace ModularVests.Server.Tests;

/// <summary>
/// The kit a bot wears. Every run is checked against <see cref="ClusterGrid"/> - a cell is
/// never covered twice, a pouch never leaves its cluster and never lands in a cell that would
/// not accept it - and the rolls are checked against the table in bots.jsonc.
/// </summary>
public class BotRigOutfitterTests
{
    private const int Runs = 10_000;

    private static ItemsConfig Items() => ItemsConfig.Parse(File.ReadAllText(TestPaths.ItemsConfig));

    private static BotsConfig Bots() => BotsConfig.Parse(File.ReadAllText(TestPaths.BotsConfig));

    [Fact]
    public void Shipped_numbers_are_valid() => Assert.Empty(Bots().Validate());

    /// <summary>A kit is a set of pouches that could have been hung by hand.</summary>
    [Fact]
    public void Every_kit_fits_the_rig()
    {
        var items = Items();
        var outfitter = new BotRigOutfitter(items, Bots());
        var byModel = items.Pouches.ToDictionary(p => p.Key);
        var keys = items.AllPouches().Select(p => p.Key).ToHashSet();
        var acceptedAt = Enumerable.Range(1, ClusterGrid.CellsPerCluster)
            .ToDictionary(position => position,
                position => items.PouchesAt(position).Select(p => p.Key).ToHashSet());

        foreach (var vest in items.AllVests())
        {
            var random = new Random(vest.Key.GetHashCode());
            for (var run = 0; run < Runs; run++)
            {
                Check(vest, outfitter.Outfit(vest, random), byModel, keys, acceptedAt);
            }
        }
    }

    /// <summary>
    /// The chest carries the number of magazines the d100 table asked for, split into double
    /// and single pouches the only ways that fit four columns.
    /// </summary>
    [Fact]
    public void Chest_magazines_follow_the_table()
    {
        (int Doubles, int Singles)[] Allowed(int slots) => slots switch
        {
            2 => [(1, 0), (0, 2)],
            4 => [(2, 0), (1, 2), (0, 4)],
            6 => [(3, 0), (2, 2)],
            8 => [(4, 0)],
            _ => [],
        };

        var items = Items();
        var outfitter = new BotRigOutfitter(items, Bots());
        var byModel = items.Pouches.ToDictionary(p => p.Key);
        var vest = items.AllVests().Single(v => v.Key == "6b45");
        var random = new Random(4242);
        for (var run = 0; run < Runs; run++)
        {
            var mags = outfitter.Outfit(vest, random)
                .Select(p => byModel[p.ModelKey])
                .Where(p => p.IsRifleMagPouch)
                .ToList();
            var doubles = mags.Count(p => p.MagSlots == 2);
            var singles = mags.Count(p => p.MagSlots == 1);
            var slots = doubles * 2 + singles;

            Assert.Contains(slots, new[] { 2, 4, 6, 8 });
            Assert.Contains((doubles, singles), Allowed(slots));
        }
    }

    /// <summary>The compositions are the ones the plan lists, and nothing else.</summary>
    [Theory]
    [InlineData(2, "1+0 0+2")]
    [InlineData(4, "2+0 1+2 0+4")]
    [InlineData(6, "3+0 2+2")]
    [InlineData(8, "4+0")]
    public void Compositions_of_a_chest(int slots, string expected) =>
        Assert.Equal(expected, string.Join(" ", BotRigOutfitter.Compositions(slots, 4)
            .Select(c => $"{c.Doubles}+{c.Singles}")));

    /// <summary>The d100 table decides how often each number of magazines comes up.</summary>
    [Fact]
    public void Magazine_counts_follow_the_distribution()
    {
        var items = Items();
        var outfitter = new BotRigOutfitter(items, Bots());
        var byModel = items.Pouches.ToDictionary(p => p.Key);
        var vest = items.AllVests().Single(v => v.Key == "6b45");
        var random = new Random(1);
        var counts = new Dictionary<int, int>();
        for (var run = 0; run < Runs; run++)
        {
            var slots = outfitter.Outfit(vest, random)
                .Select(p => byModel[p.ModelKey])
                .Where(p => p.IsRifleMagPouch)
                .Sum(p => p.MagSlots);
            counts[slots] = counts.GetValueOrDefault(slots) + 1;
        }

        void Near(int slots, double percent) =>
            Assert.InRange(counts.GetValueOrDefault(slots) * 100.0 / Runs, percent - 2, percent + 2);

        Near(2, 10);
        Near(4, 20);
        Near(6, 50);
        Near(8, 20);
    }

    /// <summary>
    /// Magazine pouches fill the chest columns left to right as the wearer sees them: the
    /// wearer's left column is cell 2 of cluster 1, which reads as the right one.
    /// </summary>
    [Fact]
    public void Chest_columns_fill_from_the_wearers_left()
    {
        Assert.Equal([2, 1, 6, 5], BotRigOutfitter.ChestColumnOrder);

        var items = Items();
        var outfitter = new BotRigOutfitter(items, Bots());
        var byModel = items.Pouches.ToDictionary(p => p.Key);
        var vest = items.AllVests().Single(v => v.Key == "6b45");
        var random = new Random(7);
        for (var run = 0; run < Runs; run++)
        {
            var mags = outfitter.Outfit(vest, random)
                .Where(p => byModel[p.ModelKey].IsRifleMagPouch)
                .Select(p => int.Parse(p.SlotName[ClusterGrid.SlotPrefix.Length..]))
                .ToList();
            Assert.Equal(BotRigOutfitter.ChestColumnOrder.Take(mags.Count), mags.Order()
                .OrderBy(slot => Array.IndexOf(BotRigOutfitter.ChestColumnOrder, slot)));
        }
    }

    /// <summary>A cummerbund cluster is empty, covers exactly two cells, or is filled up.</summary>
    [Fact]
    public void Cummerbund_clusters_are_empty_partial_or_full()
    {
        var items = Items();
        var bots = Bots();
        var outfitter = new BotRigOutfitter(items, bots);
        var byModel = items.Pouches.ToDictionary(p => p.Key);
        var vest = items.AllVests().Single(v => v.Key == "6b45");
        var random = new Random(99);
        var seen = new HashSet<int>();
        for (var run = 0; run < Runs; run++)
        {
            var kit = outfitter.Outfit(vest, random);
            foreach (var cluster in new[] { 3, 4 })
            {
                var covered = kit
                    .Where(p => ClusterGrid.ClusterOfSlot(SlotNumber(p)) == cluster)
                    .Sum(p => Area(byModel[p.ModelKey]));
                Assert.Contains(covered, new[] { 0, bots.Cummerbund.PartialArea, ClusterGrid.CellsPerCluster });
                seen.Add(covered);
            }
        }

        // all three outcomes come up over ten thousand runs
        Assert.Equal(3, seen.Count);
    }

    /// <summary>A rig without a cummerbund never touches the cells it does not have.</summary>
    [Fact]
    public void A_two_cluster_rig_keeps_to_its_chest()
    {
        var items = Items();
        var outfitter = new BotRigOutfitter(items, Bots());
        var vest = items.AllVests().Single(v => v.Key == "trooper_multicam");
        Assert.Equal(2, vest.Clusters);

        var random = new Random(11);
        for (var run = 0; run < Runs; run++)
        {
            Assert.All(outfitter.Outfit(vest, random),
                p => Assert.InRange(SlotNumber(p), 1, ClusterGrid.CellsPerCluster * 2));
        }
    }

    /// <summary>
    /// A rig with a colour to match wears it almost always; one with nothing to match strays
    /// far more often. Either way the colours all come from the line-up. Every rig of the
    /// line-up has a colour now, so the second case is a rig with its colour taken away.
    /// </summary>
    [Fact]
    public void Pouch_colours_match_the_rig()
    {
        var items = Items();
        var bots = Bots();
        var outfitter = new BotRigOutfitter(items, bots);
        var colours = items.Colours();

        double StrayShare(VestConfig vest, int seed)
        {
            var random = new Random(seed);
            int total = 0, stray = 0;
            for (var run = 0; run < Runs; run++)
            {
                var kit = outfitter.Outfit(vest, random);
                var worn = kit.Select(p => Colour(p, colours)).ToList();
                Assert.All(worn, c => Assert.Contains(c, colours));
                total += worn.Count;

                // every kit has one colour most of it agrees on; the rest strayed
                var main = worn.GroupBy(c => c).OrderByDescending(g => g.Count()).First();
                stray += worn.Count - main.Count();
            }

            return stray * 100.0 / total;
        }

        var matched = items.AllVests().Single(v => v.Key == "6b45");
        Assert.Equal("emr_summer", matched.PouchColor);
        Assert.InRange(StrayShare(matched, 3), 0, bots.OffColourChance.Matched + 2);

        var unmatched = items.AllVests().Single(v => v.Key == "untar");
        unmatched.PouchColor = "";
        Assert.InRange(StrayShare(unmatched, 5), bots.OffColourChance.Matched + 2, 100);
    }

    private static int SlotNumber(PouchPlacement placement) =>
        int.Parse(placement.SlotName[ClusterGrid.SlotPrefix.Length..]);

    private static int Area(PouchConfig pouch) =>
        Footprint.TryParse(pouch.Footprint, out var f) ? f.Width * f.Height : 0;

    private static string Colour(PouchPlacement placement, List<string> colours) =>
        colours.First(c => placement.PouchKey.EndsWith("_" + c, StringComparison.Ordinal));

    /// <summary>
    /// A kit as the client would see it: inside the rig's clusters, one pouch per cell, and
    /// every pouch in a cell whose filter accepts it.
    /// </summary>
    private static void Check(VestConfig vest, List<PouchPlacement> kit,
        Dictionary<string, PouchConfig> byModel, HashSet<string> keys,
        Dictionary<int, HashSet<string>> acceptedAt)
    {
        var taken = new HashSet<int>();
        foreach (var placement in kit)
        {
            Assert.Contains(placement.PouchKey, keys);
            var number = SlotNumber(placement);
            var cluster = ClusterGrid.ClusterOfSlot(number);
            var position = ClusterGrid.PositionOfSlot(number);
            Assert.InRange(cluster, 1, vest.Clusters);

            var pouch = byModel[placement.ModelKey];
            Assert.True(Footprint.TryParse(pouch.Footprint, out var footprint));
            Assert.True(ClusterGrid.Fits(position, footprint),
                $"{placement.PouchKey} ({pouch.Footprint}) does not fit from cell {position}");
            Assert.Contains(placement.PouchKey, acceptedAt[position]);

            foreach (var cell in ClusterGrid.Occupied(position, footprint))
            {
                Assert.True(taken.Add(ClusterGrid.SlotNumber(cluster, cell)),
                    $"cell {cluster}.{cell} is covered twice");
            }
        }
    }
}

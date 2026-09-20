using Xunit;

namespace ModularVests.Server.Tests;

public class ClusterGridTests
{
    private static readonly Footprint F1x1 = Footprint.F1x1;
    private static readonly Footprint F1x2 = Footprint.F1x2;
    private static readonly Footprint F2x1 = Footprint.F2x1;
    private static readonly Footprint F2x2 = Footprint.F2x2;

    /// <summary>The table of the design: what each cell accepts and what a pouch there covers.</summary>
    [Theory]
    [InlineData(1, "2x2", "2,3,4")]
    [InlineData(1, "2x1", "2")]
    [InlineData(1, "1x2", "3")]
    [InlineData(1, "1x1", "")]
    [InlineData(2, "1x2", "4")]
    [InlineData(2, "1x1", "")]
    [InlineData(3, "2x1", "4")]
    [InlineData(3, "1x1", "")]
    [InlineData(4, "1x1", "")]
    public void Accepted_footprints_cover_the_cells_of_the_table(int position, string footprint, string covered)
    {
        Assert.True(Footprint.TryParse(footprint, out var f));
        Assert.True(ClusterGrid.Fits(position, f));
        Assert.Equal(covered, string.Join(",", ClusterGrid.Covered(position, f)));
    }

    [Theory]
    [InlineData(2, "2x2")]
    [InlineData(2, "2x1")]
    [InlineData(3, "2x2")]
    [InlineData(3, "1x2")]
    [InlineData(4, "2x2")]
    [InlineData(4, "2x1")]
    [InlineData(4, "1x2")]
    public void Footprints_that_leave_the_cluster_do_not_fit(int position, string footprint)
    {
        Assert.True(Footprint.TryParse(footprint, out var f));
        Assert.False(ClusterGrid.Fits(position, f));
        Assert.Empty(ClusterGrid.Covered(position, f));
    }

    [Fact]
    public void Accepted_lists_largest_first()
    {
        Assert.Equal([F2x2, F2x1, F1x2, F1x1], ClusterGrid.Accepted(1));
        Assert.Equal([F1x2, F1x1], ClusterGrid.Accepted(2));
        Assert.Equal([F2x1, F1x1], ClusterGrid.Accepted(3));
        Assert.Equal([F1x1], ClusterGrid.Accepted(4));
    }

    [Theory]
    [InlineData("mod_pouch_1", 1, 1)]
    [InlineData("mod_pouch_4", 1, 4)]
    [InlineData("mod_pouch_5", 2, 1)]
    [InlineData("mod_pouch_8", 2, 4)]
    [InlineData("mod_pouch_10", 3, 2)]
    [InlineData("mod_pouch_16", 4, 4)]
    [InlineData("mod_pouch_17", 5, 1)]
    public void Slot_names_round_trip(string name, int cluster, int position)
    {
        Assert.True(ClusterGrid.TryParse(name, out var c, out var p));
        Assert.Equal((cluster, position), (c, p));
        Assert.Equal(name, ClusterGrid.SlotName(cluster, position));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mod_pouch_")]
    [InlineData("mod_pouch_0")]
    [InlineData("mod_pouch_01")]
    [InlineData("mod_pouch_-1")]
    [InlineData("mod_pouch_+1")]
    [InlineData("mod_pouch_1a")]
    [InlineData("mod_pouch_1 ")]
    [InlineData("MOD_POUCH_1")]
    [InlineData("mod_equipment")]
    [InlineData("mod_pouch_9999999")]
    public void Garbage_names_are_rejected(string? name)
    {
        Assert.False(ClusterGrid.TryParse(name!, out _, out _));
    }

    [Fact]
    public void Mirror_swaps_columns_and_pairs_clusters()
    {
        Assert.Equal([2, 1, 4, 3], Enumerable.Range(1, 4).Select(ClusterGrid.MirrorPosition));
        Assert.Equal([2, 1, 4, 3, 6, 5], Enumerable.Range(1, 6).Select(ClusterGrid.MirrorCluster));
        for (var p = 1; p <= 4; p++)
        {
            Assert.Equal(p, ClusterGrid.MirrorPosition(ClusterGrid.MirrorPosition(p)));
        }
    }

    [Theory]
    [InlineData("1x1")]
    [InlineData("1x2")]
    [InlineData("2x1")]
    [InlineData("2x2")]
    public void Footprint_is_inferred_back_from_the_cells_that_accept_it(string footprint)
    {
        Assert.True(Footprint.TryParse(footprint, out var f));
        Assert.True(ClusterGrid.TryInferFootprint(p => ClusterGrid.Fits(p, f), out var inferred));
        Assert.Equal(f, inferred);
        Assert.Equal(footprint, inferred.ToString());
    }

    [Fact]
    public void Nothing_accepted_infers_nothing()
    {
        Assert.False(ClusterGrid.TryInferFootprint(_ => false, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1x3")]
    [InlineData("2")]
    [InlineData("x")]
    public void Footprint_parsing_rejects_unknown_sizes(string text)
    {
        Assert.False(Footprint.TryParse(text, out _));
    }

    // --- the blocking model: the game's slot blocking plus the one extra rule ---

    /// <summary>
    /// One cluster as the game sees it: what each cell holds and which cells block it
    /// (Slot.BlockerSlots). <see cref="CanAdd"/> is Slot.CheckConditions for a pouch slot, the
    /// covered cells being what GetConflictingSlot returns; <paramref name="extraRule"/> is the
    /// mod's rule against covering a cell somebody else already covers.
    /// </summary>
    private sealed record Cluster(Footprint?[] Contains, List<int>[] Blockers)
    {
        public static Cluster Empty() => new(new Footprint?[5], Enumerable.Range(0, 5).Select(_ => new List<int>()).ToArray());

        public Cluster Copy() => new((Footprint?[])Contains.Clone(), Blockers.Select(b => b.ToList()).ToArray());

        public bool CanAdd(int position, Footprint f, bool extraRule)
        {
            if (Contains[position] != null || !ClusterGrid.Fits(position, f) || Blockers[position].Count > 0)
            {
                return false;
            }

            var covered = ClusterGrid.Covered(position, f);
            if (covered.Any(c => Contains[c] != null))
            {
                return false;
            }

            return !extraRule || covered.All(c => Blockers[c].Count == 0);
        }

        public Cluster Add(int position, Footprint f)
        {
            var next = Copy();
            next.Contains[position] = f;
            foreach (var c in ClusterGrid.Covered(position, f))
            {
                next.Blockers[c].Add(position);
            }

            return next;
        }

        public Cluster Remove(int position)
        {
            var next = Copy();
            foreach (var c in ClusterGrid.Covered(position, Contains[position]!.Value))
            {
                next.Blockers[c].Remove(position);
            }

            next.Contains[position] = null;
            return next;
        }

        /// <summary>Anchor and footprint of every pouch; the state that matters.</summary>
        public string Key => string.Join(" ", Enumerable.Range(1, 4).Select(p => Contains[p]?.ToString() ?? "-"));

        public bool Overlaps()
        {
            var taken = new HashSet<int>();
            for (var p = 1; p <= 4; p++)
            {
                if (Contains[p] is { } f && ClusterGrid.Occupied(p, f).Any(c => !taken.Add(c)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Every state reachable from an empty cluster by attaching and detaching pouches.</summary>
    private static Dictionary<string, Cluster> Reachable(bool extraRule)
    {
        var seen = new Dictionary<string, Cluster>();
        var queue = new Queue<Cluster>();
        var start = Cluster.Empty();
        seen[start.Key] = start;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            var next = new List<Cluster>();
            for (var p = 1; p <= 4; p++)
            {
                if (state.Contains[p] != null)
                {
                    next.Add(state.Remove(p));
                }

                next.AddRange(Footprint.All.Where(f => state.CanAdd(p, f, extraRule)).Select(f => state.Add(p, f)));
            }

            foreach (var n in next.Where(n => seen.TryAdd(n.Key, n)))
            {
                queue.Enqueue(n);
            }
        }

        return seen;
    }

    /// <summary>Every placement of pouches whose rectangles do not intersect.</summary>
    private static HashSet<string> ValidPlacements()
    {
        var result = new HashSet<string>();
        var options = new Footprint?[] { null, F1x1, F1x2, F2x1, F2x2 };
        foreach (var a in options)
        foreach (var b in options)
        foreach (var c in options)
        foreach (var d in options)
        {
            var cluster = Cluster.Empty();
            Footprint?[] all = [null, a, b, c, d];
            if (Enumerable.Range(1, 4).Any(p => all[p] is { } f && !ClusterGrid.Fits(p, f)))
            {
                continue;
            }

            Array.Copy(all, cluster.Contains, 5);
            if (!cluster.Overlaps())
            {
                result.Add(cluster.Key);
            }
        }

        return result;
    }

    [Fact]
    public void With_the_extra_rule_every_reachable_state_is_valid_and_every_valid_one_is_reachable()
    {
        var reachable = Reachable(extraRule: true);
        Assert.All(reachable.Values, s => Assert.False(s.Overlaps(), s.Key));

        // blocker bookkeeping never goes stale: each covered cell is blocked by exactly its coverer
        foreach (var state in reachable.Values)
        {
            for (var cell = 1; cell <= 4; cell++)
            {
                var expected = Enumerable.Range(1, 4)
                    .Where(p => state.Contains[p] is { } f && ClusterGrid.Covered(p, f).Contains(cell))
                    .ToList();
                Assert.Equal(expected, state.Blockers[cell].Order());
            }
        }

        Assert.Equal(ValidPlacements().OrderBy(k => k), reachable.Keys.OrderBy(k => k));
    }

    [Fact]
    public void Without_the_extra_rule_two_pouches_can_cover_the_same_empty_cell()
    {
        var reachable = Reachable(extraRule: false);
        Assert.Contains(reachable.Values, s => s.Overlaps());

        // the case of the design: 1x2 in cell 2 and 2x1 in cell 3 both cover the empty cell 4
        var state = Cluster.Empty().Add(2, F1x2);
        Assert.True(state.CanAdd(3, F2x1, extraRule: false));
        Assert.False(state.CanAdd(3, F2x1, extraRule: true));
    }
}

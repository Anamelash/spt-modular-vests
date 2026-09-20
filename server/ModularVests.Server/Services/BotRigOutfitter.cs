using ModularVests.Server.Config;

namespace ModularVests.Server.Services;

/// <summary>One pouch of a bot's kit: which cell of the rig it hangs in, and which pouch it is.</summary>
/// <param name="SlotName">mod_pouch_N, the anchor cell.</param>
/// <param name="PouchKey">Key of the pouch item, colour and all (magpouch07_coyote).</param>
/// <param name="ModelKey">Key of the pouch model the colour belongs to (magpouch07).</param>
public sealed record PouchPlacement(string SlotName, string PouchKey, string ModelKey);

/// <summary>
/// Hangs a set of pouches on a rig for a bot. Pure: the line-up, the numbers and a
/// <see cref="Random"/> in, a list of cells out - nothing here touches the database, so the
/// whole thing is testable (see the rules in <c>plan-loot-and-bots.md</c>).
///
/// The kit is rolled in three steps, and every cell it fills is checked against
/// <see cref="ClusterGrid"/>: a cell is never covered twice and a pouch never leaves its cluster.
/// 1. the chest carries rifle magazines, left to right as the wearer sees it;
/// 2. what is left of the chest is either filled with other pouches or left empty, one roll;
/// 3. each cummerbund cluster is rolled on its own: empty, a couple of cells, or full.
/// </summary>
public sealed class BotRigOutfitter(ItemsConfig items, BotsConfig bots)
{
    /// <summary>Clusters of a rig that make up the chest; the rest is cummerbund.</summary>
    public const int ChestClusters = 2;

    /// <summary>
    /// The chest columns in the order they are filled: left to right as the WEARER sees it.
    /// Cells read the way an onlooker reads them, so the wearer's left column is cell 2.
    /// </summary>
    public static readonly int[] ChestColumnOrder = [2, 1, 6, 5];

    /// <summary>
    /// Cells of a cummerbund cluster in the order they are filled: bottom row first, and in a
    /// row the column nearest the chest first. Cluster 3 sits on the wearer's left, so its
    /// chest-side column is cells 1/3; cluster 4's is 2/4.
    /// </summary>
    public static int[] CummerbundFillOrder(int cluster) =>
        cluster % 2 == 1 ? [3, 4, 1, 2] : [4, 3, 2, 1];

    /// <summary>The pouches this rig's wearer carries. Empty when the line-up has no pouches.</summary>
    public List<PouchPlacement> Outfit(VestConfig vest, Random random)
    {
        var result = new List<PouchPlacement>();
        var taken = new HashSet<int>();
        var colours = items.Colours();
        if (colours.Count == 0 || items.Pouches.Count == 0)
        {
            return result;
        }

        // Step 0: the bot's base colour. A rig with nothing to match (a blue UNTAR) gets a
        // random one, and then strays from it far more readily.
        var matched = vest.PouchColor.Length > 0 && colours.Contains(vest.PouchColor);
        var baseColour = matched ? vest.PouchColor : colours[random.Next(colours.Count)];
        var strayChance = matched ? bots.OffColourChance.Matched : bots.OffColourChance.Random;

        string RollColour()
        {
            if (colours.Count < 2 || random.Next(100) >= strayChance)
            {
                return baseColour;
            }

            var others = colours.Where(c => c != baseColour).ToList();
            return others[random.Next(others.Count)];
        }

        void Place(PouchConfig model, int slotNumber)
        {
            var cluster = ClusterGrid.ClusterOfSlot(slotNumber);
            foreach (var cell in ClusterGrid.Occupied(ClusterGrid.PositionOfSlot(slotNumber), FootprintOf(model)))
            {
                taken.Add(ClusterGrid.SlotNumber(cluster, cell));
            }

            result.Add(new PouchPlacement(
                ClusterGrid.SlotName(slotNumber), KeyFor(model, RollColour()), model.Key));
        }

        var chestClusters = Math.Min(ChestClusters, vest.Clusters);
        if (chestClusters > 0)
        {
            FillChest(vest, random, taken, Place);
        }

        for (var cluster = ChestClusters + 1; cluster <= vest.Clusters; cluster++)
        {
            FillCummerbund(cluster, random, taken, Place);
        }

        return result;
    }

    /// <summary>Step 1 and 2: rifle magazines across the chest, then the leftover.</summary>
    private void FillChest(VestConfig vest, Random random, HashSet<int> taken,
        Action<PouchConfig, int> place)
    {
        var chestClusters = Math.Min(ChestClusters, vest.Clusters);
        var columns = ChestColumnOrder.Take(chestClusters * ClusterGrid.Columns).ToList();

        var doubles = RifleMagModels(2);
        var singles = RifleMagModels(1);
        var wanted = bots.MagSlotsFor(random.Next(1, 101));
        var composition = Compositions(wanted, columns.Count, doubles.Count > 0, singles.Count > 0);
        if (composition.Count > 0)
        {
            var (twos, ones) = composition[random.Next(composition.Count)];
            var row = new List<PouchConfig>();
            for (var i = 0; i < twos; i++)
            {
                row.Add(doubles[random.Next(doubles.Count)]);
            }

            for (var i = 0; i < ones; i++)
            {
                row.Add(singles[random.Next(singles.Count)]);
            }

            Shuffle(row, random);
            for (var i = 0; i < row.Count; i++)
            {
                place(row[i], columns[i]);
            }
        }

        // Step 2: one roll for the whole leftover - all of it or none of it.
        var cells = Enumerable.Range(1, chestClusters * ClusterGrid.CellsPerCluster).ToList();
        if (cells.Any(c => !taken.Contains(c)) &&
            random.Next(1, 101) > bots.ChestLeftoverEmptyChance)
        {
            FillCells(cells, random, taken, place, budget: null);
        }
    }

    /// <summary>Step 3: one cluster of the cummerbund - empty, a couple of cells, or full.</summary>
    private void FillCummerbund(int cluster, Random random, HashSet<int> taken,
        Action<PouchConfig, int> place)
    {
        var roll = random.Next(1, 101);
        if (roll <= bots.Cummerbund.EmptyUpTo)
        {
            return;
        }

        var first = (cluster - 1) * ClusterGrid.CellsPerCluster;
        var cells = CummerbundFillOrder(cluster).Select(position => first + position).ToList();
        FillCells(cells, random, taken, place,
            roll <= bots.Cummerbund.PartialUpTo ? bots.Cummerbund.PartialArea : null);
    }

    /// <summary>
    /// Fills free cells with pouches that are not magazine pouches, taking the first free cell
    /// of <paramref name="cells"/> each time. A budget caps the cells covered in total; without
    /// one the run goes on until nothing is free. The model is picked evenly among the models
    /// that can cover that cell, then its anchor evenly among the ones that fit.
    /// </summary>
    private void FillCells(List<int> cells, Random random, HashSet<int> taken,
        Action<PouchConfig, int> place, int? budget)
    {
        while (budget is not 0)
        {
            var target = cells.FirstOrDefault(c => !taken.Contains(c), 0);
            if (target == 0)
            {
                return;
            }

            var candidates = OtherModels()
                .Select(model => (model, anchors: AnchorsCovering(target, FootprintOf(model), taken)))
                .Where(c => c.anchors.Count > 0 &&
                            (budget == null || Area(FootprintOf(c.model)) <= budget))
                .ToList();
            if (candidates.Count == 0)
            {
                return;
            }

            var (chosen, anchors) = candidates[random.Next(candidates.Count)];
            place(chosen, anchors[random.Next(anchors.Count)]);
            budget -= Area(FootprintOf(chosen));
        }
    }

    /// <summary>
    /// The cells a pouch of this size could be anchored in so that it covers
    /// <paramref name="target"/> without touching a cell that is already taken.
    /// </summary>
    private static List<int> AnchorsCovering(int target, Footprint footprint, HashSet<int> taken)
    {
        var cluster = ClusterGrid.ClusterOfSlot(target);
        var position = ClusterGrid.PositionOfSlot(target);
        var anchors = new List<int>();
        for (var candidate = 1; candidate <= ClusterGrid.CellsPerCluster; candidate++)
        {
            var occupied = ClusterGrid.Occupied(candidate, footprint);
            if (occupied.Length == 0 || !occupied.Contains(position))
            {
                continue;
            }

            if (occupied.All(cell => !taken.Contains(ClusterGrid.SlotNumber(cluster, cell))))
            {
                anchors.Add(ClusterGrid.SlotNumber(cluster, candidate));
            }
        }

        return anchors;
    }

    /// <summary>
    /// The ways <paramref name="wanted"/> magazine slots split into double and single pouches:
    /// a double covers two, a single one, and each takes a column of its own.
    /// </summary>
    public static List<(int Doubles, int Singles)> Compositions(
        int wanted, int columns, bool hasDoubles = true, bool hasSingles = true)
    {
        var result = new List<(int, int)>();
        for (var doubles = wanted / 2; doubles >= 0; doubles--)
        {
            var singles = wanted - doubles * 2;
            if (doubles + singles > columns ||
                (doubles > 0 && !hasDoubles) ||
                (singles > 0 && !hasSingles))
            {
                continue;
            }

            result.Add((doubles, singles));
        }

        return result;
    }

    /// <summary>Magazine pouch models that hold this many magazines and take a whole column.</summary>
    private List<PouchConfig> RifleMagModels(int magSlots) => items.Pouches
        .Where(p => p.IsRifleMagPouch && p.MagSlots == magSlots && FootprintOf(p) == Footprint.F1x2)
        .ToList();

    /// <summary>Every pouch model that is not a rifle magazine pouch.</summary>
    private List<PouchConfig> OtherModels() =>
        items.Pouches.Where(p => !p.IsRifleMagPouch && Footprint.TryParse(p.Footprint, out _)).ToList();

    /// <summary>The pouch item of this model in this colour; its own key when it has no colours.</summary>
    private static string KeyFor(PouchConfig model, string colour)
    {
        if (model.Variants.Count == 0)
        {
            return model.Key;
        }

        var variant = model.Variants.FirstOrDefault(v => v.Key == colour) ?? model.Variants[0];
        return $"{model.Key}_{variant.Key}";
    }

    private static Footprint FootprintOf(PouchConfig pouch) =>
        Footprint.TryParse(pouch.Footprint, out var footprint) ? footprint : Footprint.F1x1;

    private static int Area(Footprint footprint) => footprint.Width * footprint.Height;

    private static void Shuffle<T>(List<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

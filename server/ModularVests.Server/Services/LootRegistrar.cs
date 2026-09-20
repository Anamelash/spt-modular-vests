using ModularVests.Server.Config;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ModularVests.Server.Services;

/// <summary>
/// Puts the line-up into the loot tables, by data alone: rigs and single pouches into the
/// containers that already hold their prototype, assembled rigs onto the map (loose loot), and
/// single pouches into the backpacks of non-PMC bot types.
///
/// Locations hand out their loot through <c>LazyLoad</c>, whose value may be built again on
/// every access, so the transformers here have to be quick and to leave a table they have
/// already touched alone. They recognise their own work by the item ids: nothing is random.
/// </summary>
[Injectable]
public class LootRegistrar(
    LocationTable locationTable,
    TemplateTable templateTable,
    BotTable botTable,
    ISptLogger<LootRegistrar> logger)
{
    /// <summary>Bot types whose loot pools are built by the PMC generator, not by their own file.</summary>
    private static readonly HashSet<string> PmcTypes = ["pmcbear", "pmcusec", "usec", "bear"];

    /// <summary>Registers every transformer and pool entry; returns a one-line summary.</summary>
    public string Register(ItemsConfig items, BotsConfig bots)
    {
        var rigs = RegisteredRigs(items);
        var pouches = RegisteredPouches(items);

        var maps = 0;
        foreach (var (_, location) in locationTable.GetDictionary())
        {
            if (AddStaticLoot(location, items, bots, rigs, pouches) |
                AddLooseLoot(location, items, bots, rigs))
            {
                maps++;
            }
        }

        var types = AddToBotBackpacks(items, bots, pouches);
        return $"{rigs.Count} rig(s) and {pouches.Count} pouch(es) in the loot of {maps} map(s), " +
               $"pouches in the backpacks of {types} bot type(s)";
    }

    // --- static loot -------------------------------------------------------------------

    /// <summary>
    /// Containers holding a rig's prototype also hold the rig; the containers named in the
    /// config also hold single pouches, weighed against an item already in them.
    /// </summary>
    private bool AddStaticLoot(Location location, ItemsConfig items, BotsConfig bots,
        List<RegisteredRig> rigs, List<MongoId> pouches)
    {
        if (location.StaticLoot == null)
        {
            return false;
        }

        var rigWeight = bots.Loot.StaticRigs.WeightOfPrototype;
        var pouchEntries = bots.Loot.StaticPouches;
        var perColour = Colours(items);

        location.StaticLoot.AddTransformer(staticLoot =>
        {
            if (staticLoot == null)
            {
                return staticLoot;
            }

            foreach (var (_, container) in staticLoot)
            {
                foreach (var rig in rigs)
                {
                    AddWeighed(container, rig.Prototype, [rig.Tpl], rigWeight, SharingPrototype(rigs, rig));
                }
            }

            foreach (var entry in pouchEntries)
            {
                if (staticLoot.TryGetValue(new MongoId(entry.Container), out var container))
                {
                    AddWeighed(container, new MongoId(entry.Like), pouches, entry.Weight, perColour);
                }
            }

            return staticLoot;
        });

        return true;
    }

    /// <summary>
    /// Adds items to a container at a share of a reference item's weight, split over
    /// <paramref name="split"/> (the colours of one pouch model). Nothing happens without the
    /// reference item, and an item already in the container is left as it is.
    /// </summary>
    internal static void AddWeighed(StaticLootDetails container, MongoId reference,
        IReadOnlyCollection<MongoId> tpls, double share, int split)
    {
        var distribution = container.ItemDistribution as List<ItemDistribution>
                           ?? container.ItemDistribution?.ToList();
        if (distribution == null || share <= 0)
        {
            return;
        }

        var referenceWeight = distribution.FirstOrDefault(d => d.Tpl == reference)?.RelativeProbability;
        if (referenceWeight == null)
        {
            return;
        }

        var weight = (float)Math.Max(1, Math.Round(referenceWeight.Value * share / split));
        var added = false;
        foreach (var tpl in tpls.Where(tpl => distribution.All(d => d.Tpl != tpl)))
        {
            distribution.Add(new ItemDistribution { Tpl = tpl, RelativeProbability = weight });
            added = true;
        }

        if (added)
        {
            container.ItemDistribution = distribution;
        }
    }

    // --- loose loot --------------------------------------------------------------------

    /// <summary>
    /// Spawn points that offer a rig's prototype also offer the rig itself, assembled: panels,
    /// plates and a pouch loadout, as one item tree in the spawn point's template. Each rig
    /// brings a few loadouts, all rolled from a seed of its own so that running this twice
    /// produces the same trees and changes nothing.
    /// </summary>
    private bool AddLooseLoot(Location location, ItemsConfig items, BotsConfig bots, List<RegisteredRig> rigs)
    {
        var loadouts = bots.Loot.LooseRigs.LoadoutsPerRig;
        var share = bots.Loot.LooseRigs.WeightOfPrototype;
        if (location.LooseLoot == null || loadouts <= 0 || share <= 0 || rigs.Count == 0)
        {
            return false;
        }

        var outfitter = new BotRigOutfitter(items, bots);
        var trees = rigs.ToDictionary(rig => rig.Tpl, rig => Enumerable.Range(1, loadouts)
            .Select(number => LooseTree(rig, outfitter, number))
            .ToList());

        var prototypes = rigs.Select(rig => rig.Prototype).ToHashSet();
        location.LooseLoot.AddTransformer(looseLoot =>
            ApplyLooseTrees(looseLoot, rigs, prototypes, trees, share, loadouts));
        return true;
    }

    /// <summary>
    /// The loose loot transformer itself: every spawn point that offers a prototype also offers
    /// our trees. Run twice, the second run finds its own keys and changes nothing.
    /// </summary>
    internal static LooseLoot? ApplyLooseTrees(LooseLoot? looseLoot, List<RegisteredRig> rigs,
        HashSet<MongoId> prototypes, Dictionary<MongoId, List<RigTree>> trees, double share, int loadouts)
    {
        if (looseLoot?.Spawnpoints == null)
        {
            return looseLoot;
        }

        foreach (var point in looseLoot.Spawnpoints)
        {
            // The big shared pools run to a thousand roots each and this runs on every access
            // to the map's loose loot, so a point without a prototype costs one pass.
            if (point.Template?.Items == null || point.ItemDistribution == null ||
                !point.Template.Items.Any(item => prototypes.Contains(item.Template)))
            {
                continue;
            }

            var pointItems = point.Template.Items.ToList();
            var distribution = point.ItemDistribution.ToList();
            var weightOfKey = new Dictionary<string, double>();
            foreach (var entry in distribution.Where(d => d.ComposedKey?.Key != null))
            {
                weightOfKey[entry.ComposedKey!.Key!] = entry.RelativeProbability ?? 0;
            }

            var weightOfPrototype = new Dictionary<MongoId, double>();
            foreach (var item in pointItems)
            {
                if (item.ComposedKey == null || !prototypes.Contains(item.Template) ||
                    !weightOfKey.TryGetValue(item.ComposedKey, out var weight))
                {
                    continue;
                }

                weightOfPrototype[item.Template] =
                    Math.Max(weightOfPrototype.GetValueOrDefault(item.Template), weight);
            }

            var changed = false;
            foreach (var rig in rigs)
            {
                if (!weightOfPrototype.TryGetValue(rig.Prototype, out var prototypeWeight) ||
                    prototypeWeight <= 0)
                {
                    continue;
                }

                var weight = Math.Max(1,
                    Math.Round(prototypeWeight * share / loadouts / SharingPrototype(rigs, rig)));
                foreach (var tree in trees[rig.Tpl].Where(t => !weightOfKey.ContainsKey(t.Key)))
                {
                    pointItems.AddRange(tree.Items);
                    distribution.Add(new LooseLootItemDistribution
                    {
                        ComposedKey = new ComposedKey { Key = tree.Key },
                        RelativeProbability = weight,
                    });
                    weightOfKey[tree.Key] = weight;
                    changed = true;
                }
            }

            if (changed)
            {
                point.Template.Items = pointItems;
                point.ItemDistribution = distribution;
            }
        }

        return looseLoot;
    }

    /// <summary>
    /// One rig on the ground: the root, its built-in panels and plates, and a pouch loadout.
    /// Ids come from the key and the number, so the same call always builds the same tree
    /// (the server replaces them all with fresh ones when the point spawns).
    /// </summary>
    internal static RigTree LooseTree(RegisteredRig rig, BotRigOutfitter outfitter, int number)
    {
        var key = $"{DeterministicId.Prefix}{rig.Key}:{number}";
        var rootId = new MongoId(DeterministicId.For($"loose:{rig.Key}:{number}:root"));
        var items = new List<SptLootItem>
        {
            new() { Id = rootId, Template = rig.Tpl, ComposedKey = key },
        };

        void AddChild(string slot, MongoId tpl)
        {
            items.Add(new SptLootItem
            {
                Id = new MongoId(DeterministicId.For($"loose:{rig.Key}:{number}:{slot}")),
                Template = tpl,
                ParentId = rootId,
                SlotId = slot,
            });
        }

        foreach (var (slot, tpl) in BuiltInInserts.For(rig.Template, withPlates: true).Inserts)
        {
            AddChild(slot, tpl);
        }

        // the seed is the rig and the number, never the clock: the tree must come out the same
        foreach (var pouch in outfitter.Outfit(rig.Config, new Random(SeedOf(rig.Key, number))))
        {
            AddChild(pouch.SlotName, ModConfigs.PouchTpl(pouch.PouchKey));
        }

        return new RigTree(key, items);
    }

    /// <summary>A seed that follows from the rig and the loadout number alone.</summary>
    private static int SeedOf(string vestKey, int number) =>
        DeterministicId.For($"loose:{vestKey}:{number}").GetHashCode(StringComparison.Ordinal);

    // --- bot backpacks ----------------------------------------------------------------

    /// <summary>
    /// Single pouches in the backpacks of bot types that build their own loot pool, weighed
    /// against an item already in that pool. A type without the reference item is left alone;
    /// PMCs get theirs from <see cref="Patches.PmcBackpackLootPatch"/> instead.
    /// </summary>
    private int AddToBotBackpacks(ItemsConfig items, BotsConfig bots, List<MongoId> pouches)
    {
        var entry = bots.Loot.BotBackpacks;
        if (pouches.Count == 0 || entry.Weight <= 0 || string.IsNullOrWhiteSpace(entry.Like))
        {
            return 0;
        }

        var reference = new MongoId(entry.Like);
        var split = Colours(items);
        var types = 0;
        foreach (var (role, type) in botTable.Types)
        {
            var pool = type?.BotInventory?.Items?.Backpack;
            if (pool == null || PmcTypes.Contains(role.ToLowerInvariant()))
            {
                continue;
            }

            if (AddToPool(pool, reference, pouches, entry.Weight, split))
            {
                types++;
            }
        }

        return types;
    }

    /// <summary>
    /// Adds items to a weighted pool at a share of a reference item's weight, split over
    /// <paramref name="split"/>. False when the reference item is not in the pool or every item
    /// is already there.
    /// </summary>
    internal static bool AddToPool(Dictionary<MongoId, double> pool, MongoId reference,
        IReadOnlyCollection<MongoId> tpls, double share, int split)
    {
        if (share <= 0 || !pool.TryGetValue(reference, out var referenceWeight))
        {
            return false;
        }

        var weight = Math.Max(1, Math.Round(referenceWeight * share / split));
        var added = false;
        foreach (var tpl in tpls)
        {
            added |= pool.TryAdd(tpl, weight);
        }

        return added;
    }

    // --- what is actually in the database ---------------------------------------------

    /// <summary>A rig of the line-up that is in the database, and the prototype it follows.</summary>
    internal sealed record RegisteredRig(
        string Key, MongoId Tpl, MongoId Prototype, TemplateItem Template, VestConfig Config);

    /// <summary>One assembled rig as a loose loot entry: its pool key and its item tree.</summary>
    internal sealed record RigTree(string Key, List<SptLootItem> Items);

    /// <summary>The rigs that made it into the database, with the prototype their weight follows.</summary>
    private List<RegisteredRig> RegisteredRigs(ItemsConfig items)
    {
        var all = items.AllVests();
        var result = new List<RegisteredRig>();
        foreach (var vest in all)
        {
            var tpl = ModConfigs.VestTpl(vest.Key);
            if (!templateTable.Items.TryGetValue(tpl, out var template))
            {
                continue; // an optional colour whose donor is not installed
            }

            var source = vest.TierFrom.Length > 0
                ? all.FirstOrDefault(v => v.Key == vest.TierFrom) ?? vest
                : vest;
            result.Add(new RegisteredRig(vest.Key, tpl, new MongoId(source.CloneTpl), template, vest));
        }

        if (result.Count != all.Count)
        {
            logger.Info($"[ModularVests] {all.Count - result.Count} rig(s) are not registered " +
                        "and stay out of the loot tables");
        }

        return result;
    }

    private List<MongoId> RegisteredPouches(ItemsConfig items) => items.AllPouches()
        .Select(pouch => ModConfigs.PouchTpl(pouch.Key))
        .Where(templateTable.Items.ContainsKey)
        .ToList();

    /// <summary>How many colours one pouch model comes in: a reference weight is split over them.</summary>
    private static int Colours(ItemsConfig items) => Math.Max(1, items.Colours().Count);

    /// <summary>
    /// How many rigs of the line-up follow the same prototype - the colours of one carrier. They
    /// share the weight the prototype would give one item, or a carrier in five colours would be
    /// five times as common in the loot as the vanilla rig it is weighed against.
    /// </summary>
    internal static int SharingPrototype(IReadOnlyCollection<RegisteredRig> rigs, RegisteredRig rig) =>
        Math.Max(1, rigs.Count(other => other.Prototype == rig.Prototype));
}

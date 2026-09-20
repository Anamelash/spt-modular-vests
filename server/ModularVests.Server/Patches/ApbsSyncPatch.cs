using System.Collections;
using System.Reflection;
using HarmonyLib;
using ModularVests.Server.Config;
using ModularVests.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ModularVests.Server.Patches;

/// <summary>
/// Keeps APBS's tier tables honest about our rigs.
///
/// APBS imports every mod item on its own, after us: a rig with slots is an "armoured rig" and
/// lands in tiers 3 to 7 of every bot at a flat weight, no matter what it is a clone of, and
/// its pouch cells land in the mod tables where a user preset could roll them one cell at a
/// time. This re-weighs each rig the way the vanilla pools are weighed - by the share its
/// prototype has of that tier's armor vests - drops it from tiers where the prototype is
/// absent, and takes the pouch cells out of the mod tables (<see cref="BotPouchService"/> hangs
/// the pouches, with the whole cluster in view).
///
/// APBS is optional, so nothing here references its assembly: the types are found by name and
/// read by reflection. No APBS, or a structure that has changed, and the patch stays out of
/// the way with a line in the log.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class ApbsSyncPatch(ModConfigs configs, TemplateTable templateTable, ISptLogger<ApbsSyncPatch> logger)
    : AbstractPatch
{
    private const string ImportServiceName = "ProgressiveBotSystem.Services.ItemImportService";
    private const string DataLoaderName = "DataLoader";

    private static ApbsSyncPatch _self = null!;

    /// <summary>The APBS import service, or null when APBS is not installed.</summary>
    public static Type? ImportService => AppDomain.CurrentDomain.GetAssemblies()
        .Select(assembly => SafeGetType(assembly, ImportServiceName))
        .FirstOrDefault(type => type != null);

    /// <summary>Whether there is anything to patch at all.</summary>
    public bool IsApbsInstalled => ImportService != null;

    /// <summary>The version of APBS, for the log.</summary>
    public string ApbsVersion => ImportService?.Assembly.GetName().Version?.ToString() ?? "unknown";

    protected override MethodBase? GetTargetMethod()
    {
        _self = this;
        return ImportService == null ? null : AccessTools.Method(ImportService, "OnLoadAsync");
    }

    /// <summary>
    /// The import is asynchronous and APBS repeats it whenever its web app reloads the config,
    /// so the work is chained onto the task rather than done here.
    /// </summary>
    [PatchPostfix]
    public static void Postfix(ref Task __result, object __instance)
    {
        var instance = __instance;
        __result = __result.ContinueWith(_ => _self.Sync(instance));
    }

    private void Sync(object importService)
    {
        var items = configs.Items;
        var bots = configs.Bots;
        if (items == null || bots == null)
        {
            return;
        }

        try
        {
            var tiers = TiersOf(importService);
            if (tiers == null)
            {
                logger.Warning("[ModularVests] APBS is installed but its tier tables are not where " +
                               "they used to be; its own weights for our rigs are left as they are");
                return;
            }

            var all = items.AllVests();

            // The colours of one carrier share what the prototype's weight gives one rig.
            var sharing = all.GroupBy(vest => PrototypeOf(items, vest))
                .ToDictionary(group => group.Key, group => group.Count());

            var rigs = all
                .Select(vest => (
                    Tpl: ModConfigs.VestTpl(vest.Key),
                    Prototype: new MongoId(PrototypeOf(items, vest)),
                    Sharing: sharing[PrototypeOf(items, vest)]))
                .Where(rig => templateTable.Items.ContainsKey(rig.Tpl))
                .ToList();

            var ours = all.Select(vest => ModConfigs.VestTpl(vest.Key)).ToHashSet();
            var tally = new Tally();
            foreach (DictionaryEntry tier in tiers)
            {
                if (SyncTier(tier.Value, rigs, bots.WeightMultiplier, ours, tally))
                {
                    tally.Tiers.Add(Convert.ToInt32(tier.Key));
                }
            }

            logger.Info($"[ModularVests] APBS {ApbsVersion}: {rigs.Count} rig(s) over " +
                        $"{tally.Tiers.Count} tier(s) ({string.Join(", ", tally.Tiers.Order())}); " +
                        $"{tally.Weighed} entries weighed, {tally.Dropped} dropped where the prototype " +
                        $"is not worn, {tally.Bots} bot pool(s) seen");
        }
        catch (Exception ex)
        {
            logger.Warning($"[ModularVests] could not read the APBS tier tables ({ex.Message}); " +
                           "its own weights for our rigs are left as they are");
        }
    }

    /// <summary>What the sync did, for one line in the log.</summary>
    private sealed class Tally
    {
        public readonly HashSet<int> Tiers = [];
        public int Weighed;
        public int Dropped;
        public int Bots;
    }

    /// <summary>The prototype a rig's weight follows - its own, or the one it recolours.</summary>
    private static string PrototypeOf(ItemsConfig items, VestConfig vest) =>
        vest.TierFrom.Length > 0
            ? items.AllVests().FirstOrDefault(v => v.Key == vest.TierFrom)?.CloneTpl ?? vest.CloneTpl
            : vest.CloneTpl;

    /// <summary>
    /// One tier: every bot in it gets our rigs weighed against their prototypes, and the pouch
    /// cells go out of its mod tables.
    /// </summary>
    private static bool SyncTier(object? tier, List<(MongoId Tpl, MongoId Prototype, int Sharing)> rigs,
        double multiplier, HashSet<MongoId> ours, Tally tally)
    {
        if (tier == null)
        {
            return false;
        }

        var changed = false;
        var equipment = tier.GetType().GetProperty("EquipmentData")?.GetValue(tier);
        foreach (var property in equipment?.GetType().GetProperties() ?? [])
        {
            var slots = property.GetValue(equipment)?.GetType().GetProperty("Equipment")
                ?.GetValue(property.GetValue(equipment)) as IDictionary;
            if (slots == null)
            {
                continue;
            }

            tally.Bots++;
            changed |= SyncBot(slots, rigs, multiplier, ours, tally);
        }

        if (tier.GetType().GetProperty("ModsData")?.GetValue(tier)
            is Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>> mods)
        {
            foreach (var rig in rigs.Where(rig => mods.ContainsKey(rig.Tpl)))
            {
                foreach (var cell in mods[rig.Tpl].Keys
                             .Where(slot => slot.StartsWith(ClusterGrid.SlotPrefix, StringComparison.OrdinalIgnoreCase))
                             .ToList())
                {
                    mods[rig.Tpl].Remove(cell);
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>One bot of one tier: an armoured rig only where its prototype is worn.</summary>
    private static bool SyncBot(IDictionary slots, List<(MongoId Tpl, MongoId Prototype, int Sharing)> rigs,
        double multiplier, HashSet<MongoId> ours, Tally tally)
    {
        var armor = PoolOf(slots, "ArmorVest");
        var armouredRigs = PoolOf(slots, "ArmouredRig");
        var rigsPool = PoolOf(slots, "TacticalVest");
        if (armouredRigs == null)
        {
            return false;
        }

        // APBS runs its import again whenever its web app reloads the config, so the totals
        // must not count what we put there last time: the weights would climb on every reload.
        double Total(Dictionary<MongoId, double>? pool) =>
            pool?.Where(entry => !ours.Contains(entry.Key)).Sum(entry => entry.Value) ?? 0;

        var armorTotal = Total(armor);
        var rigTotal = Total(armouredRigs) + Total(rigsPool);
        var changed = false;

        foreach (var (tpl, prototype, sharing) in rigs)
        {
            var prototypeWeight = armor != null && armor.TryGetValue(prototype, out var w) ? w : 0;
            if (prototypeWeight <= 0 || armorTotal <= 0 || rigTotal <= 0)
            {
                changed |= armouredRigs.Remove(tpl);
                changed |= rigsPool?.Remove(tpl) ?? false;
                tally.Dropped++;
                continue;
            }

            var weight = Services.BotPoolRegistrar.Weight(prototypeWeight, armorTotal, rigTotal, multiplier, sharing);
            if (!armouredRigs.TryGetValue(tpl, out var current) || Math.Abs(current - weight) > 0.5)
            {
                armouredRigs[tpl] = weight;
                changed = true;
            }

            tally.Weighed++;
        }

        return changed;
    }

    private static Dictionary<MongoId, double>? PoolOf(IDictionary slots, string slotName)
    {
        foreach (DictionaryEntry entry in slots)
        {
            if (entry.Key.ToString() == slotName)
            {
                return entry.Value as Dictionary<MongoId, double>;
            }
        }

        return null;
    }

    /// <summary>
    /// The tier tables, reached from the import service through whichever of its helpers holds
    /// the data loader. APBS injects them; nothing here knows their types by name but one.
    /// </summary>
    private static IDictionary? TiersOf(object importService)
    {
        var loader = FindDataLoader(importService, depth: 2);
        var all = loader?.GetType().GetProperty("AllTierDataDirty")?.GetValue(loader);
        return all?.GetType().GetProperty("Tiers")?.GetValue(all) as IDictionary;
    }

    private static object? FindDataLoader(object? from, int depth)
    {
        if (from == null || depth < 0)
        {
            return null;
        }

        var fields = from.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var field in fields)
        {
            var value = field.GetValue(from);
            if (value == null)
            {
                continue;
            }

            if (value.GetType().Name == DataLoaderName)
            {
                return value;
            }

            if (value.GetType().Assembly == from.GetType().Assembly)
            {
                var found = FindDataLoader(value, depth - 1);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static Type? SafeGetType(Assembly assembly, string name)
    {
        try
        {
            return assembly.GetType(name);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

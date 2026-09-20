using ModularVests.Server.Config;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ModularVests.Server.Services;

/// <summary>
/// Puts the rigs into the equipment pools of the vanilla bot types. A rig turns up among a bot
/// type's rigs exactly as often as the armor vest it was cloned from turns up among that type's
/// armor vests - which is what "the prototype's tier" means without a tier system: the bot
/// types that wear the prototype, in the proportion they wear it.
///
/// The plate and panel slots are copied from the prototype's own <c>mods</c> entry (the clone
/// keeps the slot names), and the pouch cells are deliberately left out: with no chance in
/// <c>equipmentMods</c> the game's mod generator skips them, which is what we want. It fills
/// slots one at a time without looking at their neighbours and would happily cover a cell
/// twice - <see cref="BotPouchService"/> hangs the pouches instead.
/// </summary>
[Injectable]
public class BotPoolRegistrar(
    BotTable botTable,
    TemplateTable templateTable,
    BotConfig botConfig,
    ISptLogger<BotPoolRegistrar> logger)
{
    /// <summary>Adds every registered rig to the pools; returns a one-line summary.</summary>
    public string Register(ItemsConfig items, BotsConfig bots)
    {
        var added = 0;
        var unworn = new List<string>();

        // Our own rigs never count towards the total a share is taken of: otherwise each rig
        // would be weighed against a pool the ones before it had already grown.
        var all = items.AllVests();
        var ours = all.Select(v => ModConfigs.VestTpl(v.Key)).ToHashSet();

        // The colours of one carrier all follow the same prototype and split its share between
        // them: five colours at the full share would put five times as many modular rigs on bots.
        var sharing = all.GroupBy(v => PrototypeOf(items, v))
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var vest in all)
        {
            var tpl = ModConfigs.VestTpl(vest.Key);
            if (!templateTable.Items.ContainsKey(tpl))
            {
                continue; // an optional colour whose donor is not installed
            }

            var prototype = new MongoId(PrototypeOf(items, vest));
            var types = AddToTypes(tpl, prototype, bots.WeightMultiplier, ours,
                sharing[PrototypeOf(items, vest)]);
            if (types == 0)
            {
                unworn.Add(vest.Key);
                continue;
            }

            AddToLevelWhitelists(tpl, prototype);
            added++;
        }

        if (unworn.Count > 0)
        {
            logger.Info($"[ModularVests] no vanilla bot wears the prototype of {string.Join(", ", unworn)}, " +
                        "so those rigs are not given to bots");
        }

        return $"{added} rig(s) in the equipment pools of vanilla bots";
    }

    /// <summary>
    /// The armor vest whose share of a bot type's vests the rig inherits: its own donor, or the
    /// donor of the rig it recolours when nobody wears its own (a Couturier colour).
    /// </summary>
    private static string PrototypeOf(ItemsConfig items, VestConfig vest) =>
        vest.TierFrom.Length > 0
            ? items.AllVests().FirstOrDefault(v => v.Key == vest.TierFrom)?.CloneTpl ?? vest.CloneTpl
            : vest.CloneTpl;

    /// <summary>
    /// Adds the rig to every bot type whose armor vest pool holds the prototype, at the share
    /// of that type's rigs the prototype has of its armor vests, split over the rigs that follow
    /// that prototype. Returns how many types took it.
    /// </summary>
    private int AddToTypes(MongoId tpl, MongoId prototype, double multiplier, HashSet<MongoId> ours,
        int sharing)
    {
        var types = 0;
        foreach (var (_, type) in botTable.Types)
        {
            var equipment = type?.BotInventory?.Equipment;
            if (equipment == null ||
                !equipment.TryGetValue(EquipmentSlots.ArmorVest, out var armor) ||
                !armor.TryGetValue(prototype, out var prototypeWeight) ||
                !equipment.TryGetValue(EquipmentSlots.TacticalVest, out var rigs))
            {
                continue;
            }

            var armorTotal = armor.Where(entry => !ours.Contains(entry.Key)).Sum(entry => entry.Value);
            var rigTotal = rigs.Where(entry => !ours.Contains(entry.Key)).Sum(entry => entry.Value);
            if (armorTotal <= 0 || rigTotal <= 0 || prototypeWeight <= 0)
            {
                continue;
            }

            var weight = Weight(prototypeWeight, armorTotal, rigTotal, multiplier, sharing);
            if (!rigs.TryAdd(tpl, weight))
            {
                types++; // already there from an earlier start: still a type that wears it
                continue;
            }

            CopyMods(type!.BotInventory!.Mods, tpl, prototype);
            types++;
        }

        return types;
    }

    /// <summary>
    /// The weight one rig gets in a bot type's rig pool: the share its prototype has of that
    /// type's armor vests, applied to that type's rigs, divided between the colours that follow
    /// the same prototype. Never below 1, so a rare prototype still puts its colours on a bot.
    /// </summary>
    internal static double Weight(double prototypeWeight, double armorTotal, double rigTotal,
        double multiplier, int sharing) =>
        Math.Max(1, Math.Round(prototypeWeight / armorTotal * rigTotal * multiplier / Math.Max(1, sharing)));

    /// <summary>
    /// The clone's plate and panel slots keep the prototype's names, so its whole <c>mods</c>
    /// entry carries over. The pouch cells are not added: there is no chance for them in
    /// <c>equipmentMods</c>, so the generator leaves them alone.
    /// </summary>
    private static void CopyMods(Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>? mods,
        MongoId tpl, MongoId prototype)
    {
        if (mods == null || mods.ContainsKey(tpl) || !mods.TryGetValue(prototype, out var source))
        {
            return;
        }

        mods[tpl] = source
            .Where(slot => !slot.Key.StartsWith(ClusterGrid.SlotPrefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(slot => slot.Key, slot => new HashSet<MongoId>(slot.Value));
    }

    /// <summary>
    /// The level whitelists decide what a bot of a given level may wear at all. Wherever the
    /// prototype is allowed as an armor vest, the rig is allowed as a rig.
    /// </summary>
    private void AddToLevelWhitelists(MongoId tpl, MongoId prototype)
    {
        foreach (var (_, filters) in botConfig.Equipment ?? [])
        {
            foreach (var range in filters?.Whitelist ?? [])
            {
                var equipment = range.Equipment;
                if (equipment == null ||
                    !equipment.TryGetValue(EquipmentSlots.ArmorVest.ToString(), out var armor) ||
                    !armor.Contains(prototype))
                {
                    continue;
                }

                if (equipment.TryGetValue(EquipmentSlots.TacticalVest.ToString(), out var rigs))
                {
                    rigs.Add(tpl);
                }
            }
        }
    }
}

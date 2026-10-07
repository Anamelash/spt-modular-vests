using System.Reflection;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Services.InRaid;

namespace ModularVests.Server.Patches;

/// <summary>
/// Every server method the mod patches, in one place, with a self-test at start-up - the same
/// arrangement the client uses. A method the server no longer has is a mod that needs an
/// update, not a crash: the bot half (or the insurance of pouches) switches off, the items, the
/// trader and the rig window keep working, and the log says which target went missing.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PatchTargets(ISptLogger<PatchTargets> logger)
{
    /// <summary>Capacity: where a bot's spare magazines, ammo, meds and loot are put.</summary>
    public static MethodBase? AddItemToEquipmentSlot =>
        AccessTools.Method(typeof(BotGeneratorHelper), "AddItemWithChildrenToEquipmentSlot");

    /// <summary>The end of a bot's inventory, where a rig without pouches is still outfitted.</summary>
    public static MethodBase? GenerateInventory =>
        AccessTools.Method(typeof(BotInventoryGenerator), "GenerateInventory");

    /// <summary>The pool a PMC's backpack loot is drawn from.</summary>
    public static MethodBase? GeneratePmcBackpackLootPool =>
        AccessTools.Method(typeof(PMCLootGenerator), "GeneratePMCBackpackLootPool");

    /// <summary>The end of a PMC raid, entered while the profile still holds the pre-raid inventory.</summary>
    public static MethodBase? HandlePostRaidPmc =>
        AccessTools.Method(typeof(LocationLifecycleService), "HandlePostRaidPmc");

    /// <summary>The insured items lost in a raid, sorted into one package per trader.</summary>
    public static MethodBase? MapInsuredItemsToTrader =>
        AccessTools.Method(typeof(InsuranceService), "MapInsuredItemsToTrader");

    /// <summary>The attachments a trader may keep back when an insurance package comes home.</summary>
    public static MethodBase? RemoveNonModdableAttachments =>
        AccessTools.Method(typeof(InsuranceController), "RemoveNonModdableAttachments");

    private bool? _available;
    private bool? _insuranceAvailable;

    /// <summary>
    /// Whether every target is where it is expected to be. Checked once; the answer is logged
    /// the first time it is asked for.
    /// </summary>
    public bool Available => _available ??= CheckTargets(
        "Bots keep their rigs empty",
        (nameof(AddItemToEquipmentSlot), AddItemToEquipmentSlot),
        (nameof(GenerateInventory), GenerateInventory),
        (nameof(GeneratePmcBackpackLootPool), GeneratePmcBackpackLootPool));

    /// <summary>Whether the insurance targets are there; checked and logged the same way.</summary>
    public bool InsuranceAvailable => _insuranceAvailable ??= CheckTargets(
        "Insured rigs come back without their pouches",
        (nameof(HandlePostRaidPmc), HandlePostRaidPmc),
        (nameof(MapInsuredItemsToTrader), MapInsuredItemsToTrader),
        (nameof(RemoveNonModdableAttachments), RemoveNonModdableAttachments));

    private bool CheckTargets(string consequence, params (string Name, MethodBase? Target)[] targets)
    {
        var missing = targets.Where(target => target.Target == null).Select(target => target.Name).ToList();
        if (missing.Count == 0)
        {
            return true;
        }

        logger.Warning($"[ModularVests] this server version has no {string.Join(", ", missing)}. " +
                       $"{consequence}; the mod needs an update for this server version");
        return false;
    }
}

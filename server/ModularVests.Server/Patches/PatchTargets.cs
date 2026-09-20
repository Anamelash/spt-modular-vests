using System.Reflection;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Helpers.Bot;

namespace ModularVests.Server.Patches;

/// <summary>
/// Every server method the mod patches, in one place, with a self-test at start-up - the same
/// arrangement the client uses. A method the server no longer has is a mod that needs an
/// update, not a crash: the bot half switches off, the items, the trader and the rig window
/// keep working, and the log says which target went missing.
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

    private bool? _available;

    /// <summary>
    /// Whether every target is where it is expected to be. Checked once; the answer is logged
    /// the first time it is asked for.
    /// </summary>
    public bool Available => _available ??= CheckTargets();

    private bool CheckTargets()
    {
        var missing = new List<string>();
        Check(nameof(AddItemToEquipmentSlot), AddItemToEquipmentSlot);
        Check(nameof(GenerateInventory), GenerateInventory);
        Check(nameof(GeneratePmcBackpackLootPool), GeneratePmcBackpackLootPool);

        void Check(string name, MethodBase? target)
        {
            if (target == null)
            {
                missing.Add(name);
            }
        }

        if (missing.Count == 0)
        {
            return true;
        }

        logger.Warning($"[ModularVests] this server version has no {string.Join(", ", missing)}. " +
                       "Bots keep their rigs empty; the mod needs an update for this server version");
        return false;
    }
}

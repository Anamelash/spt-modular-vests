using System.Reflection;
using ModularVests.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ModularVests.Server.Patches;

/// <summary>
/// Single pouches in the backpacks of PMCs. Their loot pools are not read from a bot file but
/// built here from the whitelisted base classes in pmc.json, and <c>SimpleContainer</c> cannot
/// be whitelisted without bringing SICC cases, key tools and document cases along with it -
/// so the pouches are added to the finished pool instead, weighed against an item already in it.
///
/// The pool is cached by role, so this runs about twice in a session; the items it adds are
/// only added once either way.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PmcBackpackLootPatch(ModConfigs configs, TemplateTable templateTable) : AbstractPatch
{
    private static ModConfigs _configs = null!;
    private static TemplateTable _templates = null!;

    protected override MethodBase? GetTargetMethod()
    {
        _configs = configs;
        _templates = templateTable;
        return PatchTargets.GeneratePmcBackpackLootPool;
    }

    [PatchPostfix]
    public static void Postfix(Dictionary<MongoId, double> __result)
    {
        var items = _configs.Items;
        var bots = _configs.Bots;
        if (items == null || bots == null || __result == null)
        {
            return;
        }

        var entry = bots.Loot.PmcBackpack;
        if (string.IsNullOrWhiteSpace(entry.Like))
        {
            return;
        }

        var pouches = items.AllPouches()
            .Select(pouch => ModConfigs.PouchTpl(pouch.Key))
            .Where(_templates.Items.ContainsKey)
            .ToList();

        LootRegistrar.AddToPool(__result, new MongoId(entry.Like), pouches, entry.Weight,
            Math.Max(1, items.Colours().Count));
    }
}

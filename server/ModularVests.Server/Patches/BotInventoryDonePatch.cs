using System.Reflection;
using ModularVests.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ModularVests.Server.Patches;

/// <summary>
/// The end of a bot's inventory: a modular rig that never had anything put in it still gets
/// its pouches, and the bot is then forgotten.
///
/// A postfix runs even when another mod's prefix skipped the original (APBS generates its own
/// inventory), so a bot that carried nothing - or one whose generator never reached the vest -
/// still comes out of here wearing a kit rather than an empty frame.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class BotInventoryDonePatch(BotPouchService pouches) : AbstractPatch
{
    private static BotPouchService _pouches = null!;

    protected override MethodBase? GetTargetMethod()
    {
        _pouches = pouches;
        return PatchTargets.GenerateInventory;
    }

    [PatchPostfix]
    public static void Postfix(BotBaseInventory __result, MongoId botId)
    {
        var rig = __result?.Items?.FirstOrDefault(
            item => item.SlotId == EquipmentSlots.TacticalVest.ToString());
        if (rig != null && _pouches.VestKeyOf(rig.Template) != null)
        {
            _pouches.PouchesOf(botId, rig, __result!, null);
        }

        _pouches.Forget(botId);
    }
}

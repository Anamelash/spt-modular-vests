using System.Reflection;
using ModularVests.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace ModularVests.Server.Patches;

/// <summary>
/// Gives a bot in a modular rig somewhere to put things. The generator reads a container's
/// grids from its template and ours has none, so without this every spare magazine goes to the
/// pockets and then nowhere: <c>NO_SPACE</c>.
///
/// The prefix takes over only when the item is headed for the TacticalVest slot and the rig in
/// that slot is one of ours; anything else falls through to the original, and so does an item
/// the pouches have no room for - it lands in the pockets or the backpack, as it always did.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class BotPouchCapacityPatch(BotPouchService pouches) : AbstractPatch
{
    private static BotPouchService _pouches = null!;

    protected override MethodBase? GetTargetMethod()
    {
        _pouches = pouches;
        return PatchTargets.AddItemToEquipmentSlot;
    }

    [PatchPrefix]
    public static bool Prefix(
        ref ItemAddedResult __result,
        MongoId botId,
        HashSet<EquipmentSlots> equipmentSlots,
        MongoId rootItemId,
        MongoId rootItemTplId,
        IEnumerable<Item> itemWithChildren,
        BotBaseInventory inventory)
    {
        if (!equipmentSlots.Contains(EquipmentSlots.TacticalVest) || inventory.Items == null)
        {
            return true;
        }

        var rig = inventory.Items.FirstOrDefault(
            item => item.SlotId == EquipmentSlots.TacticalVest.ToString());
        if (rig == null || _pouches.VestKeyOf(rig.Template) == null)
        {
            return true;
        }

        var items = itemWithChildren.ToList();
        if (!_pouches.TryStore(botId, rig, inventory, null, rootItemId, rootItemTplId, items))
        {
            return true;
        }

        __result = ItemAddedResult.SUCCESS;
        return false;
    }
}

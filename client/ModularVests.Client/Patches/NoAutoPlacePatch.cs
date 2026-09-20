using System;
using EFT.InventoryLogic;
using HarmonyLib;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// Pouch cells are never a place automatic placement picks. The game looks for room with
    /// ItemManipulator.QuickFindAppropriatePlace, which walks every container of every equipped
    /// item before the stash - slots included. A pouch taken off on the Modding screen with the
    /// backpack and pockets full was put straight onto another free cell of the same rig instead of
    /// into the stash (or refused with "no free space"); a quick move could hang a pouch on a cell.
    ///
    /// Only the search is refused (Slot.TryFindLocationForItem). Attaching on the Modding screen and
    /// dragging onto a cell address the slot directly and are left alone.
    /// </summary>
    internal static class NoAutoPlacePatch
    {
        public static void Apply(Harmony harmony)
        {
            harmony.Patch(PatchTargets.Slot_TryFindLocationForItem
                          ?? throw new InvalidOperationException("Slot.TryFindLocationForItem not found"),
                postfix: new HarmonyMethod(typeof(NoAutoPlacePatch), nameof(Postfix)));
        }

        private static void Postfix(Slot __instance, ref ItemAddress location, ref bool __result)
        {
            if (__result && PouchSlots.IsPouchSlot(__instance))
            {
                location = null;
                __result = false;
            }
        }
    }
}

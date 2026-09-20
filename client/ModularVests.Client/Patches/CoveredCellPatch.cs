using System;
using EFT.InventoryLogic;
using EFT.UI.WeaponModding;
using HarmonyLib;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// The Modding screen hides the icons of cells covered by a larger pouch: they cannot take
    /// anything while it is there, and their icons only crowd the one of the pouch.
    ///
    /// Covered is the game's own bookkeeping (Slot.BlockerSlots, see SlotBlockingPatch). The icon
    /// is switched off in CheckVisibility, the same way the mod class toggles hide theirs, so the
    /// screen leaves it out when it spaces the icons around the item. Attaching or removing a
    /// pouch rebuilds the icons, which brings the uncovered cells back.
    /// </summary>
    internal static class CoveredCellPatch
    {
        private static readonly AccessTools.FieldRef<ModdingScreenSlotView, Slot> SlotRef =
            AccessTools.FieldRefAccess<ModdingScreenSlotView, Slot>(PatchTargets.ModdingScreenSlotView_Slot);

        public static void Apply(Harmony harmony)
        {
            var target = PatchTargets.ModdingScreenSlotView_CheckVisibility
                         ?? throw new InvalidOperationException("ModdingScreenSlotView.CheckVisibility not found");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(CoveredCellPatch), nameof(Postfix)));
        }

        private static void Postfix(ModdingScreenSlotView __instance)
        {
            try
            {
                var slot = SlotRef(__instance);
                if (slot != null && slot.BlockerSlots.Count > 0 && PouchSlots.IsPouchSlot(slot))
                {
                    __instance.gameObject.SetActive(false);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] covered cell icon: {ex}");
            }
        }
    }
}

using System;
using System.Linq;
using EFT.InventoryLogic;
using HarmonyLib;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// The character screen lists every non-armor slot of an equipped container under the
    /// "special slots" header (that is how the pockets show theirs). A modular rig's eight
    /// pouch slots landed there as well, next to the pouch grids, which is noise: pouches are
    /// fitted on the Modding screen.
    ///
    /// The panel is skipped when a rig has nothing but pouch slots to show. An item that has
    /// real special slots as well keeps the vanilla panel.
    /// </summary>
    internal static class PouchSlotPanelPatch
    {
        public static void Apply(Harmony harmony)
        {
            var target = PatchTargets.SearchableSlotView_CreateSlots
                         ?? throw new InvalidOperationException("SearchableSlotView.CreateSlots not found");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(PouchSlotPanelPatch), nameof(Prefix)));
        }

        private static bool Prefix(Item item)
        {
            try
            {
                if (!(item is CompoundItem compound) || compound.Slots == null)
                {
                    return true;
                }

                var shown = compound.Slots.Where(slot => !(slot is ArmorSlot)).ToList();
                return shown.Count == 0 || !shown.All(PouchSlots.IsPouchSlot);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] pouch slot panel: {ex}");
                return true;
            }
        }
    }
}

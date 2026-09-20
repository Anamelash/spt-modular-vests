using System;
using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;
using HarmonyLib;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// Automatic placement fills the equipment in a fixed order (InventoryEquipmentExtension:
    /// the rig's containers, pockets, backpack...). For a rig that is its own slots and grids; a
    /// modular rig has no grids, and its pouches' grids sit one level down, in the items on its
    /// cells - nothing listed them, so a quick move (Ctrl+click), a pick-up or unloaded rounds went
    /// past them into the backpack. They are put where a rig's grids stand: right after the rig's
    /// own containers, in cell order.
    /// </summary>
    internal static class PouchGridsForLootPatch
    {
        public static void Apply(Harmony harmony)
        {
            harmony.Patch(PatchTargets.InventoryEquipmentExtension_GetPrioritizedContainersForLoot
                          ?? throw new InvalidOperationException("GetPrioritizedContainersForLoot not found"),
                postfix: new HarmonyMethod(typeof(PouchGridsForLootPatch), nameof(ContainersPostfix)));
            harmony.Patch(PatchTargets.InventoryEquipmentExtension_GetPrioritizedGridsForUnloadedObject
                          ?? throw new InvalidOperationException("GetPrioritizedGridsForUnloadedObject not found"),
                postfix: new HarmonyMethod(typeof(PouchGridsForLootPatch), nameof(GridsPostfix)));
            harmony.Patch(PatchTargets.ItemManipulator_QuickFindAppropriatePlace
                          ?? throw new InvalidOperationException("ItemManipulator.QuickFindAppropriatePlace not found"),
                prefix: new HarmonyMethod(typeof(PouchGridsForLootPatch), nameof(TargetsPrefix)));
        }

        /// <summary>
        /// Room looked for in given items (a drop onto an item, the rest of a group dragged by UI
        /// Fixes, which aims each next item at the owner of the grid it was dropped on): a modular
        /// rig and its pouches are one storage, as a rig and its grids are. A modular rig brings its
        /// pouches after it; a pouch on a modular rig brings the other pouches of that rig after it,
        /// so what does not fit spills over into them.
        /// </summary>
        private static void TargetsPrefix(Item item, ref IEnumerable<CompoundItem> targets)
        {
            try
            {
                if (targets == null)
                {
                    return;
                }

                List<CompoundItem> expanded = null;
                var given = targets as IList<CompoundItem> ?? targets.ToList();
                foreach (var target in given)
                {
                    var isRig = PouchSlots.HasAny(target);
                    var rig = isRig ? target : PouchSlots.SlotOf(target)?.ParentItem as CompoundItem;
                    if (rig == null)
                    {
                        continue;
                    }

                    // in the order the rig window shows them, from the pouch dropped on
                    expanded = expanded ?? new List<CompoundItem>(given);
                    var at = expanded.IndexOf(target) + 1;
                    foreach (var pouch in PouchOrder.After(rig, isRig ? null : target))
                    {
                        if (pouch != item && !expanded.Contains(pouch))
                        {
                            expanded.Insert(at++, pouch);
                        }
                    }
                }

                targets = expanded ?? given;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] pouches as one storage: {ex}");
            }
        }

        private static void ContainersPostfix(InventoryEquipment equipment, ref IEnumerable<IContainer> __result)
        {
            try
            {
                var rig = ModularRig(equipment);
                if (rig == null || __result == null)
                {
                    return;
                }

                var list = __result.ToList();
                var after = list.FindLastIndex(container => container.ParentItem == rig);
                list.InsertRange(after + 1, PouchGrids(rig).Cast<IContainer>());
                __result = list;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] pouch grids for loot: {ex}");
            }
        }

        /// <summary>The rig's own grids come first there; a modular rig has none, so its pouches' go first.</summary>
        private static void GridsPostfix(InventoryEquipment equipment, ref IEnumerable<Grid> __result)
        {
            try
            {
                var rig = ModularRig(equipment);
                if (rig != null && __result != null)
                {
                    __result = PouchGrids(rig).Concat(__result).ToList();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] pouch grids for unloading: {ex}");
            }
        }

        private static CompoundItem ModularRig(InventoryEquipment equipment)
        {
            var rig = equipment?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem as CompoundItem;
            return rig != null && PouchSlots.HasAny(rig) ? rig : null;
        }

        private static List<Grid> PouchGrids(CompoundItem rig) =>
            PouchSlots.AttachedPouches(rig).SelectMany(pouch => pouch.Grids ?? Array.Empty<Grid>()).ToList();
    }
}

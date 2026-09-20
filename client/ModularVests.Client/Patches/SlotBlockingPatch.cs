using System;
using System.Collections.Generic;
using System.Linq;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// Ф20: a pouch that covers more than one cell blocks the cells it covers, through the
    /// game's own slot blocking - the one a helmet uses to block the headset slot.
    ///
    /// The game keeps the bookkeeping (Slot.BlockerSlots, set in AddInternal and cleared in
    /// RemoveItemInternal, rebuilt by every profile load), the errors, the highlighting of the
    /// pouch in the way and swaps by drag and drop. The mod only answers which cells a pouch
    /// covers from a given cell:
    /// <list type="number">
    /// <item>the rig's pouch slots get their cluster as neighbours (Slot.ConflictingSlots), as
    /// equipment slots get theirs in the InventoryEquipment constructor - without them
    /// CheckConditions skips the conflict check altogether;</item>
    /// <item>GetConflictingSlot of a pouch slot returns the covered cells (ClusterGrid);</item>
    /// <item>one rule the game does not know: a cell may not be covered twice. Two items may
    /// block the same slot as far as the game is concerned; a 1x2 in cell 2 and a 2x1 in cell
    /// 3 would both cover the empty cell 4.</item>
    /// </list>
    /// </summary>
    internal static class SlotBlockingPatch
    {
        private static readonly HashSet<string> LoggedRigs = new HashSet<string>();

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(PatchTargets.Vest_Ctor ?? throw new InvalidOperationException("Vest constructor not found"),
                postfix: new HarmonyMethod(typeof(SlotBlockingPatch), nameof(VestCtorPostfix)));
            harmony.Patch(PatchTargets.Slot_GetConflictingSlot
                          ?? throw new InvalidOperationException("Slot.GetConflictingSlot not found"),
                postfix: new HarmonyMethod(typeof(SlotBlockingPatch), nameof(GetConflictingSlotPostfix)));
            harmony.Patch(PatchTargets.Slot_CheckConditions
                          ?? throw new InvalidOperationException("Slot.CheckConditions not found"),
                postfix: new HarmonyMethod(typeof(SlotBlockingPatch), nameof(CheckConditionsPostfix)));
        }

        private static void VestCtorPostfix(Vest __instance)
        {
            try
            {
                if (!PouchSlots.HasAny(__instance))
                {
                    return;
                }

                foreach (var cluster in PouchSlots.Of(__instance).GroupBy(PouchSlots.ClusterOf))
                {
                    if (cluster.Key == 0)
                    {
                        continue;
                    }

                    var neighbours = cluster.ToDictionary(slot => slot.ID);
                    foreach (var slot in cluster)
                    {
                        slot.ConflictingSlots = neighbours;
                    }
                }

                LogFootprints(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] pouch cells of {__instance?.StringTemplateId}: {ex}");
            }
        }

        private static void GetConflictingSlotPostfix(Slot __instance, Item item, ref IEnumerable<Slot> __result)
        {
            if (item == null || __instance.ConflictingSlots == null || !PouchSlots.IsPouchSlot(__instance))
            {
                return;
            }

            try
            {
                __result = PouchSlots.CoveredSlots(__instance, item);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] cells covered from {__instance.ID}: {ex}");
            }
        }

        private static void CheckConditionsPostfix(Slot __instance, Item item, bool ignoreRestrictions,
            ref Option<bool> __result)
        {
            if (ignoreRestrictions || __result.Failed || item == null || __instance.ConflictingSlots == null ||
                !PouchSlots.IsPouchSlot(__instance))
            {
                return;
            }

            try
            {
                foreach (var covered in PouchSlots.CoveredSlots(__instance, item))
                {
                    if (covered.BlockerSlots.Count > 0)
                    {
                        // the pouch that already covers the cell is the one in the way
                        __result = new Slot.ConflictingSlotBlockedError(item, __instance, covered.BlockerSlots[0]);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] cell check of {__instance.ID}: {ex}");
            }
        }

        /// <summary>
        /// Once per rig template: the footprint of every pouch the cells accept, read back from
        /// the filters the way the blocking reads it. Must match the server's line-up.
        /// </summary>
        private static void LogFootprints(Vest rig)
        {
            if (!LoggedRigs.Add(rig.StringTemplateId))
            {
                return;
            }

            var cells = PouchSlots.ClusterSlots(rig, 1);
            if (cells.Skip(1).Any(c => c == null))
            {
                Plugin.Log.LogWarning($"[ModularVests] rig {rig.StringTemplateId}: cluster 1 is incomplete, " +
                                      "pouch footprints unknown");
                return;
            }

            bool Accepts(int position, string tpl) =>
                cells[position].Filters.Any(f => f?.Filter != null && f.Filter.Any(id => id.ToString() == tpl));

            var tpls = cells[1].Filters.Where(f => f?.Filter != null).SelectMany(f => f.Filter)
                .Select(id => id.ToString()).Distinct();
            var lines = new List<string>();
            foreach (var tpl in tpls)
            {
                var footprint = ClusterGrid.TryInferFootprint(p => Accepts(p, tpl), out var f) ? f.ToString() : "?";
                lines.Add($"{(tpl + " ShortName").Localized()} ({tpl}) {footprint}");
            }

            Plugin.Log.LogInfo($"[ModularVests] rig {rig.StringTemplateId}: {PouchSlots.Of(rig).Count} pouch cells, " +
                               "pouch footprints from the cell filters: " + string.Join(", ", lines.ToArray()));
        }
    }
}

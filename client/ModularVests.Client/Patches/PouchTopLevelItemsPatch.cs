using System;
using System.Collections.Generic;
using EFT.InventoryLogic;
using HarmonyLib;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// InventoryExtension.GetTopLevelItems walks the given containers exactly one level deep:
    /// what sits in their slots and what lies in their grids. On a modular rig that yields the
    /// pouches themselves and nothing else - the rig has no grids of its own and a magazine in a
    /// pouch is one level further down. Out of raid that walk is the whole search behind the
    /// context menu's "Reload" and "Load ammo" (ItemUiContext.FindSuitableMagazine takes this
    /// branch when not in a raid, the reachable-item search - which <see cref="RaidReachPatch"/>
    /// extends - only in one), so a rig carrying all its magazines in pouches answered with
    /// "Can't find any non-empty magazine", in the stash and for a rig lying there alike.
    ///
    /// Each pouch it yields is followed by the contents of that pouch's grids: one level further,
    /// and only for our pouches.
    /// </summary>
    internal static class PouchTopLevelItemsPatch
    {
        public static void Apply(Harmony harmony)
        {
            harmony.Patch(PatchTargets.InventoryExtension_GetTopLevelItems
                          ?? throw new InvalidOperationException("GetTopLevelItems not found"),
                postfix: new HarmonyMethod(typeof(PouchTopLevelItemsPatch), nameof(Postfix)));
        }

        private static void Postfix(ref IEnumerable<Item> __result)
        {
            if (__result != null)
            {
                __result = WithPouchContents(__result);
            }
        }

        private static IEnumerable<Item> WithPouchContents(IEnumerable<Item> items)
        {
            foreach (var item in items)
            {
                yield return item;

                if (PouchSlots.SlotOf(item) == null || !(item is CompoundItem pouch) || pouch.Grids == null)
                {
                    continue;
                }

                foreach (var grid in pouch.Grids)
                {
                    foreach (var inside in grid.Items)
                    {
                        yield return inside;
                    }
                }
            }
        }
    }
}

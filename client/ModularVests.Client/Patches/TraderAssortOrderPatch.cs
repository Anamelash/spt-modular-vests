using System;
using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// The trader screen lays the assortment out in the game's own order (ItemSorter.Sort): by the
    /// item's class, then by template id as a string. The grid is filled in TradingGridView.ApplyFilter,
    /// which sorts the items once more right before placing them (Grid.AddAnywhere, in that order) -
    /// reordering any list handed to it earlier (TraderDealScreen.PrepareTraderGrid) came to nothing.
    /// So the order is set in ItemSorter.Sort itself, only while ApplyFilter runs. The mod's ids are hashes of their keys, so its
    /// pouches came out shuffled - a double mag pouch next to a medical one, the colours of one
    /// pouch scattered. The ids are for good (they are in profiles); the order is set here instead.
    ///
    /// Only the mod's items move, and only among the places they already hold within their class:
    /// every other item, and the game's sorting anywhere else (the stash), is untouched. Pouches by
    /// size (1x1, 1x2, 2x1, 2x2 - fewer gaps as the grid fills), then model, then colour in the
    /// palette's order; rigs by name, which keeps the kits and colours of a carrier together.
    /// </summary>
    internal static class TraderAssortOrderPatch
    {
        /// <summary>The palette (the colour keys of items.jsonc), in the order the colours are shown.</summary>
        private static readonly string[] Palette = { "coyote", "olive", "multicam", "black", "emr_summer" };

        private static bool _errorLogged;

        /// <summary>Set while the trader grid is being filled: the only sorting this patch touches.</summary>
        [ThreadStatic]
        private static bool _fillingTraderGrid;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(PatchTargets.TradingGridView_ApplyFilter
                          ?? throw new InvalidOperationException("TradingGridView.ApplyFilter not found"),
                prefix: new HarmonyMethod(typeof(TraderAssortOrderPatch), nameof(FillPrefix)),
                finalizer: new HarmonyMethod(typeof(TraderAssortOrderPatch), nameof(FillFinalizer)));
            harmony.Patch(PatchTargets.ItemSorter_Sort
                          ?? throw new InvalidOperationException("ItemSorter.Sort not found"),
                postfix: new HarmonyMethod(typeof(TraderAssortOrderPatch), nameof(SortPostfix)));
        }

        private static void FillPrefix() => _fillingTraderGrid = true;

        private static Exception FillFinalizer(Exception __exception)
        {
            _fillingTraderGrid = false;
            return __exception;
        }

        private static void SortPostfix(List<Item> __result)
        {
            if (!_fillingTraderGrid || __result == null || __result.Count == 0)
            {
                return;
            }

            try
            {
                var list = __result;
                foreach (var group in Enumerable.Range(0, list.Count)
                             .Where(i => IsModItem(list[i]))
                             .GroupBy(i => list[i].GetType()))
                {
                    var places = group.ToList();
                    var sorted = places.Select(i => list[i])
                        .OrderBy(item => IsPouch(item) ? Area(item) : 0)
                        .ThenBy(item => IsPouch(item) ? item.CalculateCellSize().X : 0)
                        .ThenBy(item => Model(item), StringComparer.Ordinal)
                        .ThenBy(item => ColourRank(item))
                        .ThenBy(item => item.Id, StringComparer.Ordinal)
                        .ToList();
                    for (var k = 0; k < places.Count; k++)
                    {
                        list[places[k]] = sorted[k];
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Plugin.Log.LogError($"[ModularVests] trader assortment order: {ex}");
                }
            }
        }

        private static bool IsModItem(Item item) =>
            item?.Template?._name != null &&
            item.Template._name.StartsWith(PouchModelPatch.TemplateNamePrefix, StringComparison.Ordinal);

        private static bool IsPouch(Item item) =>
            item.Template._name.StartsWith(PouchModelPatch.TemplateNamePrefix + "pouch_", StringComparison.Ordinal);

        private static int Area(Item item)
        {
            var size = item.CalculateCellSize();
            return size.X * size.Y;
        }

        /// <summary>The template's name without its colour: "modularvests_pouch_magpouch07_olive" -> "..._magpouch07".</summary>
        private static string Model(Item item)
        {
            var name = item.Template._name;
            foreach (var colour in Palette)
            {
                if (name.EndsWith("_" + colour, StringComparison.Ordinal))
                {
                    return name.Substring(0, name.Length - colour.Length - 1);
                }
            }

            return name;
        }

        /// <summary>The colour's place in the palette; other names (a colour of another mod) after it.</summary>
        private static int ColourRank(Item item)
        {
            var name = item.Template._name;
            for (var i = 0; i < Palette.Length; i++)
            {
                if (name.EndsWith("_" + Palette[i], StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return Palette.Length;
        }
    }
}

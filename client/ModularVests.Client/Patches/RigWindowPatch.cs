using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// Ф11: the window of a modular rig shows the grids of every attached pouch, after the
    /// rig's own grids, in slot order. A rig with an empty RigLayoutName is drawn by
    /// GeneratedGridsView; its Show is replaced for rigs that have pouch slots.
    ///
    /// The vanilla 8-argument Show cannot be reused: it pairs views with
    /// compoundItem.Containers by index, which never contains a pouch's grids.
    /// </summary>
    internal static class RigWindowPatch
    {
        private static readonly AccessTools.FieldRef<UIElement, UIParent> UiRef =
            AccessTools.FieldRefAccess<UIElement, UIParent>(PatchTargets.UIElement_UI);

        /// <summary>What the last Show of a view was called with, to rebuild it when a pouch changes.</summary>
        private sealed class ShowArgs
        {
            public CompoundItem Rig;
            public ItemContext Context;
            public ItemController Controller;
            public FilterPanel FilterPanel;
            public ItemUiContext UiContext;
            public bool Magnify;
            public int Generation;
        }

        private static readonly ConditionalWeakTable<GeneratedGridsView, ShowArgs> LastShow =
            new ConditionalWeakTable<GeneratedGridsView, ShowArgs>();

        public static void Apply(Harmony harmony)
        {
            var target = PatchTargets.GeneratedGridsView_Show
                         ?? throw new InvalidOperationException("GeneratedGridsView.Show not found");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(RigWindowPatch), nameof(Prefix)));
        }

        private static bool Prefix(GeneratedGridsView __instance, CompoundItem compoundItem, ItemContext itemContext,
            ItemController itemController, FilterPanel filterPanel, ItemUiContext itemUiContext, bool magnify)
        {
            if (!PouchSlots.HasAny(compoundItem))
            {
                return true;
            }

            var args = LastShow.GetOrCreateValue(__instance);
            args.Rig = compoundItem;
            args.Context = itemContext;
            args.Controller = itemController;
            args.FilterPanel = filterPanel;
            args.UiContext = itemUiContext;
            args.Magnify = magnify;

            try
            {
                Show(__instance, args);
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] rig window: {ex}");
                return true;
            }
        }

        private static void Show(GeneratedGridsView view, ShowArgs args)
        {
            var ui = UiRef(view);
            ui.Dispose();
            args.Generation++;

            // views of a previous Show are only hidden by the dispose above
            if (view.GridViews != null)
            {
                foreach (var old in view.GridViews)
                {
                    if (old != null)
                    {
                        Object.Destroy(old.gameObject);
                    }
                }
            }

            // every grid to show: the rig's own, then each pouch's, in slot order
            var grids = new List<Grid>();
            var contexts = new List<ItemContext>();
            var groups = new List<int>();
            var kinds = new List<string>();
            var stacked = new List<bool>();
            foreach (var grid in args.Rig.Grids ?? Array.Empty<Grid>())
            {
                grids.Add(grid);
                contexts.Add(args.Context);
                groups.Add(-1 - grids.Count);
                kinds.Add("rig " + Shape(grid));
                stacked.Add(false);
            }

            var pouchIndex = 0;
            foreach (var slot in PouchSlots.Of(args.Rig))
            {
                Action<Item> changed = _ => ScheduleRebuild(view, args, args.Generation);
                slot.OnAddOrRemoveItem += changed;
                var subscribed = slot;
                ui.AddDisposable(() => subscribed.OnAddOrRemoveItem -= changed);

                if (!(slot.ContainedItem is CompoundItem pouch) || pouch.Grids == null || pouch.Grids.Length == 0)
                {
                    continue;
                }

                // same parenting the rig window gives the rig itself
                var pouchContext = args.Context.CreateChild(pouch);
                ui.AddDisposable(pouchContext);
                pouchIndex++;
                var kind = string.Join(" ", Array.ConvertAll(pouch.Grids, Shape));
                var inColumn = InColumn(pouch.Grids);
                foreach (var grid in pouch.Grids)
                {
                    grids.Add(grid);
                    contexts.Add(pouchContext);
                    groups.Add(pouchIndex);
                    kinds.Add(kind);
                    stacked.Add(inColumn);
                }
            }

            // In rows no wider than the window, each row centred, pouches of a kind together and
            // mixed rows symmetric (GridRows): all in one row they ran off the screen.
            var rows = GridRows.Layout(grids.ConvertAll(g => g.GridWidth), groups, kinds, stacked,
                ClientConfig.RigWindowWidth.Value);
            var column = CreateRows(view, rows.Count, out var rowTransforms, out var spacing);
            ui.AddDisposable(() =>
            {
                if (column != null)
                {
                    Object.Destroy(column);
                }
            });

            var views = new GridView[grids.Count];
            for (var r = 0; r < rows.Count; r++)
            {
                // Every pouch's grids go into a group of their own - side by side, or one above the
                // other for a stacked pouch - so they have a parent to themselves, as the grids of any
                // item in the game do (UI Fixes maps a multi-grid item from its grids' parent).
                // The rig's own grids stand in the row directly.
                Transform pouchGroup = null;
                var pouchOfGroup = int.MinValue;
                foreach (var index in rows[r])
                {
                    var parent = rowTransforms[r];
                    if (groups[index] > 0)
                    {
                        if (pouchGroup == null || pouchOfGroup != groups[index])
                        {
                            pouchGroup = CreatePouchGroup(rowTransforms[r], spacing, stacked[index]);
                            pouchOfGroup = groups[index];
                        }

                        parent = pouchGroup;
                    }
                    else
                    {
                        pouchGroup = null;
                    }

                    views[index] = ShowGrid(view, ui, grids[index], contexts[index], args, parent);
                }
            }

            // the pouches in reading order: what spills over from one goes into the next on screen
            var shown = new List<CompoundItem>();
            foreach (var row in rows)
            {
                foreach (var index in row)
                {
                    if (groups[index] > 0 && grids[index].ParentItem is CompoundItem pouch && !shown.Contains(pouch))
                    {
                        shown.Add(pouch);
                    }
                }
            }

            PouchOrder.Record(args.Rig, shown);

            view.GridViews = views;
            view.SlotViews = Array.Empty<SlotView>();
            view.ShowGameObject();
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)view.transform);
        }

        /// <summary>
        /// A column of centred rows under the window's own layout. The rows lay out their grids
        /// at the grids' own sizes; the column sizes itself to them, so whatever lays the window
        /// out sees one block of the right size.
        /// </summary>
        private static GameObject CreateRows(GeneratedGridsView view, int count, out List<Transform> rows,
            out float spacing)
        {
            var parentLayout = view.GetComponent<HorizontalOrVerticalLayoutGroup>();
            spacing = parentLayout != null ? parentLayout.spacing : DefaultSpacing;
            if (!_layoutLogged)
            {
                _layoutLogged = true;
                Plugin.Log.LogInfo("[ModularVests] rig window: grids in rows under " +
                                   (view.GetComponent<LayoutGroup>()?.GetType().Name ?? "no layout group") +
                                   $", spacing {spacing}, at most {ClientConfig.RigWindowWidth.Value} cells a row");
            }

            var column = Group<VerticalLayoutGroup>("ModularVests.GridRows", view.transform, spacing);

            // the block sits in the window's own layout group, which is not ours to set up: it sizes
            // itself as a whole (inside, the layout groups size everything)
            var fitter = column.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            rows = new List<Transform>();
            for (var i = 0; i < count; i++)
            {
                rows.Add(Group<HorizontalLayoutGroup>("Row " + (i + 1), column.transform, spacing).transform);
            }

            return column;
        }

        /// <summary>
        /// The grids of one pouch inside a row: a column for a stacked pouch, else side by side.
        /// Pivot top-left, like a container's grids panel in the game: UI Fixes reads the grids'
        /// offsets from their parent's top-left corner.
        /// </summary>
        private static Transform CreatePouchGroup(Transform row, float spacing, bool stacked)
        {
            var group = stacked
                ? Group<VerticalLayoutGroup>("Pouch sections", row, spacing)
                : Group<HorizontalLayoutGroup>("Pouch sections", row, spacing);
            ((RectTransform)group.transform).pivot = new Vector2(0f, 1f);
            return group.transform;
        }

        /// <summary>
        /// A layout group that sizes its children: each grid by its LayoutElement, which the game's
        /// GridView fills with the grid's size (OnGridResized), each nested group by what it holds.
        /// Sizes then flow up in one layout pass. A group that only placed its children at their
        /// current sizes needed a ContentSizeFitter to get a size of its own, and a fitter inside a
        /// layout group lags a pass behind it: a partial rebuild (a right click in the inspect
        /// window) laid the pouch columns out at a stale 100x100.
        /// </summary>
        private static GameObject Group<T>(string name, Transform parent, float spacing)
            where T : HorizontalOrVerticalLayoutGroup
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            var group = go.AddComponent<T>();
            group.childAlignment = TextAnchor.UpperCenter;
            group.spacing = spacing;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            return go;
        }

        /// <summary>
        /// A pouch shows its sections in a column when none of them is taller than wide (the admin
        /// pouch's 2x1 + 2x1, the utility pouch's 2x2 + 2x1, the gadget pouch's 1x1 + 1x1): side by
        /// side they read as one wide strip. Tall sections (a magazine pouch's 1x2) stay side by side.
        /// </summary>
        private static bool InColumn(Grid[] sections)
        {
            if (sections.Length < 2)
            {
                return false;
            }

            foreach (var section in sections)
            {
                if (section.GridHeight > section.GridWidth)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Pouches with the same section shapes are one kind, whatever the model or colour.</summary>
        private static string Shape(Grid grid) => grid.GridWidth + "x" + grid.GridHeight;

        private static bool _gridLayoutLogged;

        /// <summary>
        /// Once: what the layout groups will size the first grid to, next to its own size. They
        /// should match; a larger preferred size (another layout element on the grid, e.g. an Image
        /// with a big sprite) would stretch the grids.
        /// </summary>
        private static void LogGridLayout(GridView gridView)
        {
            if (_gridLayoutLogged)
            {
                return;
            }

            _gridLayoutLogged = true;
            var rect = (RectTransform)gridView.transform;
            Plugin.Log.LogInfo($"[ModularVests] rig window: grid size {rect.sizeDelta}, layout size " +
                               $"({LayoutUtility.GetPreferredWidth(rect)}, {LayoutUtility.GetPreferredHeight(rect)})");
        }

        private const float DefaultSpacing = 4f;

        private static bool _layoutLogged;

        private static GridView ShowGrid(GeneratedGridsView view, UIParent ui, Grid grid, ItemContext context,
            ShowArgs args, Transform parent)
        {
            var gridView = Object.Instantiate(view._gridViewTemplate, parent, worldPositionStays: false);

            // where the layout groups read the grid's size; GridView.Show fills it in
            gridView.gameObject.GetOrAddComponent<LayoutElement>();
            gridView.Show(grid, context, args.Controller, args.UiContext, args.FilterPanel, args.Magnify);
            LogGridLayout(gridView);
            ui.AddDisposable(gridView);
            return gridView;
        }

        /// <summary>
        /// A pouch was attached or detached while the window is open. The event fires in the
        /// middle of the inventory operation, so the rebuild waits for the next frame.
        /// </summary>
        private static void ScheduleRebuild(GeneratedGridsView view, ShowArgs args, int generation)
        {
            if (view != null && view.isActiveAndEnabled)
            {
                view.StartCoroutine(RebuildNextFrame(view, args, generation));
            }
        }

        private static IEnumerator RebuildNextFrame(GeneratedGridsView view, ShowArgs args, int generation)
        {
            yield return null;

            // closed, or already rebuilt by an earlier event of the same operation
            if (view == null || !view.isActiveAndEnabled || args.Generation != generation)
            {
                yield break;
            }

            try
            {
                Show(view, args);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] rig window rebuild: {ex}");
            }
        }
    }
}

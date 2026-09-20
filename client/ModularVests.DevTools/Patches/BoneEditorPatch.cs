using System;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.WeaponModding;
using HarmonyLib;
using ModularVests.Client;
using ModularVests.Client.Bones;
using UnityEngine;

namespace ModularVests.DevTools.Patches
{
    /// <summary>
    /// Ф14: hooks the bone editor to the Modding screen. Always applied: the F12 switch is
    /// read at runtime, so the editor can be turned on in a running game.
    /// </summary>
    internal static class BoneEditorPatch
    {
        public static void Apply(Harmony harmony, GameObject host)
        {
            harmony.Patch(DevPatchTargets.ItemObserveScreen_CreateModSlotViews
                          ?? throw new InvalidOperationException("ItemObserveScreen.CreateModSlotViews not found"),
                postfix: new HarmonyMethod(typeof(BoneEditorPatch), nameof(CreateModSlotViewsPostfix)));
            harmony.Patch(DevPatchTargets.ItemObserveScreen_Close
                          ?? throw new InvalidOperationException("ItemObserveScreen.Close not found"),
                prefix: new HarmonyMethod(typeof(BoneEditorPatch), nameof(ClosePrefix)));
            harmony.Patch(DevPatchTargets.WeaponPreview_Rotate
                          ?? throw new InvalidOperationException("WeaponPreview.Rotate not found"),
                prefix: new HarmonyMethod(typeof(BoneEditorPatch), nameof(RotatePrefix)));

            harmony.Patch(DevPatchTargets.ClientApplication_CursorVisibilityChanged
                          ?? throw new InvalidOperationException("CursorVisibilityChangedHandler not found"),
                prefix: new HarmonyMethod(typeof(BoneEditorPatch), nameof(CursorVisibilityPrefix)));

            harmony.Patch(DevPatchTargets.WeaponPreview_Hide
                          ?? throw new InvalidOperationException("WeaponPreview.Hide not found"),
                prefix: new HarmonyMethod(typeof(BoneEditorPatch), nameof(PreviewHidePrefix)));

            BoneEditor.Create(host);
        }

        // The base is a generic shared with the build editor: the screen type is checked here.
        private static void CreateModSlotViewsPostfix(object __instance, CompoundItem weapon)
        {
            try
            {
                if (!(__instance is WeaponModdingScreen screen) || !PouchSlots.HasAny(weapon))
                {
                    return;
                }

                var rotator = screen._weaponPreview != null ? screen._weaponPreview.Rotator : null;
                var pool = rotator != null ? rotator.GetComponentInChildren<AssetPoolObject>() : null;
                var view = pool != null ? pool.ContainerCollectionView : null;
                if (view == null)
                {
                    DevPlugin.Log.LogWarning("[ModularVests] bone editor: the preview model has no bone view, editor not attached");
                    return;
                }

                BoneEditor.Instance?.Begin(screen, weapon, view);
            }
            catch (Exception ex)
            {
                DevPlugin.Log.LogError($"[ModularVests] bone editor begin: {ex}");
            }
        }

        private static void ClosePrefix(object __instance)
        {
            try
            {
                if (__instance is WeaponModdingScreen screen)
                {
                    BoneEditor.Instance?.OnScreenClose(screen);
                }
            }
            catch (Exception ex)
            {
                DevPlugin.Log.LogError($"[ModularVests] bone editor end: {ex}");
            }
        }

        /// <summary>
        /// While the editor is up, the cursor stays visible and unlocked. The Modding screen
        /// locks it on every drag inside the preview, and a locked cursor sits in the middle
        /// of the screen instead of on the bone being dragged.
        /// </summary>
        private static bool CursorVisibilityPrefix(bool isCursorVisible) =>
            isCursorVisible || !(BoneEditor.Instance != null && BoneEditor.Instance.Active);

        private static void PreviewHidePrefix(WeaponPreview __instance)
        {
            try
            {
                BoneEditor.Instance?.OnPreviewHidden(__instance);
            }
            catch (Exception ex)
            {
                DevPlugin.Log.LogError($"[ModularVests] bone editor preview hide: {ex}");
            }
        }

        /// <summary>A bone drag must not also spin the preview under the cursor.</summary>
        private static bool RotatePrefix() => !(BoneEditor.Instance != null && BoneEditor.Instance.Dragging);
    }
}

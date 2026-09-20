using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.WeaponModding;
using HarmonyLib;
using ModularVests.Client;

namespace ModularVests.DevTools
{
    /// <summary>Patch targets of the dev tools, with their own startup self-test.</summary>
    internal static class DevPatchTargets
    {
        /// <summary>
        /// The Modding screen is a closed generic; its reference-type arguments share one body
        /// with the build editor, so the editor filters by screen type at runtime.
        /// </summary>
        public static Type ModdingScreenBase =>
            typeof(ItemObserveScreen<WeaponModdingScreen.WeaponModdingScreenController, WeaponModdingScreen>);

        public static MethodBase ItemObserveScreen_CreateModSlotViews =>
            AccessTools.Method(ModdingScreenBase, "CreateModSlotViews", new[] { typeof(CompoundItem) });

        public static MethodBase ItemObserveScreen_Close => AccessTools.Method(ModdingScreenBase, "Close");

        public static MethodBase ItemObserveScreen_UpdatePositions =>
            AccessTools.Method(ModdingScreenBase, "UpdatePositions");

        public static MethodBase ItemObserveScreen_HighlightMod =>
            AccessTools.Method(ModdingScreenBase, "HighlightMod");

        /// <summary>
        /// Where the game hides and locks the cursor. The Modding screen asks for that as soon
        /// as a drag starts inside the preview, which warps the cursor to the centre of the
        /// screen and leaves the mouse position useless for grabbing anything.
        /// </summary>
        public static MethodBase ClientApplication_CursorVisibilityChanged => AccessTools.Method(
            typeof(ClientApplicationInitOperation),
            nameof(ClientApplicationInitOperation.CursorVisibilityChangedHandler));

        /// <summary>Tears the preview model down: the real end of a modding session.</summary>
        public static MethodBase WeaponPreview_Hide =>
            AccessTools.Method(typeof(WeaponPreview), nameof(WeaponPreview.Hide));

        /// <summary>Spins the Modding screen preview (mouse drag and arrow keys).</summary>
        public static MethodBase WeaponPreview_Rotate =>
            AccessTools.Method(typeof(WeaponPreview), nameof(WeaponPreview.Rotate));

        public static FieldInfo ItemObserveScreen_WeaponPreview =>
            AccessTools.Field(ModdingScreenBase, "_weaponPreview");

        private static readonly Dictionary<string, Func<object>> All = new Dictionary<string, Func<object>>
        {
            { nameof(ItemObserveScreen_CreateModSlotViews), () => ItemObserveScreen_CreateModSlotViews },
            { nameof(ItemObserveScreen_Close), () => ItemObserveScreen_Close },
            { nameof(ItemObserveScreen_UpdatePositions), () => ItemObserveScreen_UpdatePositions },
            { nameof(ItemObserveScreen_HighlightMod), () => ItemObserveScreen_HighlightMod },
            { nameof(ItemObserveScreen_WeaponPreview), () => ItemObserveScreen_WeaponPreview },
            { nameof(WeaponPreview_Rotate), () => WeaponPreview_Rotate },
            { nameof(WeaponPreview_Hide), () => WeaponPreview_Hide },
            { nameof(ClientApplication_CursorVisibilityChanged), () => ClientApplication_CursorVisibilityChanged },
            { "ItemObserveScreen._viewporter", () => AccessTools.Field(ModdingScreenBase, "_viewporter") },
        };

        public static List<string> SelfTest() => PatchTargets.SelfTest(All);
    }
}

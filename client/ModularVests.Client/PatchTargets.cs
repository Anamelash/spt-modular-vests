using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using EFT.UI.WeaponModding;
using HarmonyLib;

namespace ModularVests.Client
{
    /// <summary>
    /// Single registry of every patch target. The names are whatever SPT's prepatcher chose
    /// for this release, so they live here and the startup self-test reports drift at game
    /// load rather than on the first rig window.
    /// </summary>
    internal static class PatchTargets
    {
        // --- rig window (Ф11) ---

        /// <summary>Dynamic grid layout of a container window (rigs with an empty RigLayoutName).</summary>
        public static MethodBase GeneratedGridsView_Show => AccessTools.Method(typeof(GeneratedGridsView),
            nameof(GeneratedGridsView.Show), new[]
            {
                typeof(CompoundItem), typeof(ItemContext), typeof(ItemController), typeof(FilterPanel),
                typeof(ItemUiContext), typeof(bool),
            });

        /// <summary>Grid view of one grid; the rig window instantiates one per grid.</summary>
        public static MethodBase GridView_Show => AccessTools.Method(typeof(GridView), nameof(GridView.Show), new[]
        {
            typeof(Grid), typeof(ItemContext), typeof(ItemController), typeof(ItemUiContext), typeof(FilterPanel),
            typeof(bool),
        });

        public static FieldInfo UIElement_UI => AccessTools.Field(typeof(UIElement), "UI");

        /// <summary>Lists an equipped container's non-armor slots under the "special slots" header.</summary>
        public static MethodBase SearchableSlotView_CreateSlots =>
            AccessTools.Method(typeof(SearchableSlotView), nameof(SearchableSlotView.CreateSlots));

        /// <summary>Shows or hides a slot icon of the Modding screen by the mod class toggles.</summary>
        public static MethodBase ModdingScreenSlotView_CheckVisibility => AccessTools.Method(
            typeof(ModdingScreenSlotView), nameof(ModdingScreenSlotView.CheckVisibility));

        public const string ModdingScreenSlotView_Slot = "_slot";

        /// <summary>Fills the trader's grid: sorts the assortment (ItemSorter.Sort), then places it in that order.</summary>
        public static MethodBase TradingGridView_ApplyFilter => AccessTools.Method(
            typeof(TradingGridView), nameof(TradingGridView.ApplyFilter));

        /// <summary>The game's item order: by class, then by template id.</summary>
        public static MethodBase ItemSorter_Sort => AccessTools.Method(typeof(ItemSorter), nameof(ItemSorter.Sort),
            new[] { typeof(IEnumerable<Item>) });

        /// <summary>A slot of the inspect window: picks the empty slot's background by the slot's name.</summary>
        public static MethodBase ModSlotView_Show => AccessTools.Method(typeof(ModSlotView), nameof(ModSlotView.Show),
            new[] { typeof(Slot), typeof(ItemContext), typeof(ItemController), typeof(ItemUiContext) });

        // --- bones (Ф10) ---

        /// <summary>Where a model's slots are matched with its bones — preview and body alike.</summary>
        public static MethodBase ObjectsFactory_AttachMods => AccessTools.Method(typeof(ObjectsFactory),
            nameof(ObjectsFactory.AttachMods));

        /// <summary>Puts the model of an equipped item on the character (and skins it to the skeleton).</summary>
        public static MethodBase PlayerBody_SlotView_CreateAndParent => AccessTools.Method(
            typeof(PlayerBody.SlotView), nameof(PlayerBody.SlotView.CreateAndParent));

        // --- pouch seat (Ф21) ---

        /// <summary>Hangs a mod's model on its bone, with the model's ModPlacer or the default offset.</summary>
        public static MethodBase SlotView_InsertItem => AccessTools.Method(typeof(ContainerCollectionView.SlotView),
            nameof(ContainerCollectionView.SlotView.InsertItem), new[] { typeof(Item), typeof(UnityEngine.GameObject) });

        // --- pouch models ---

        /// <summary>Every asset of a bundle arrives here once it is loaded.</summary>
        public static MethodBase EasyBundle_SetAssets =>
            AccessTools.PropertySetter(typeof(Diz.Resources.EasyBundle), nameof(Diz.Resources.EasyBundle.Assets));

        /// <summary>The icon cache key of one item (the icon's key XORs those of the item and its parts).</summary>
        public static MethodBase IconsHash_HashForItem =>
            AccessTools.Method(typeof(IconsHash), nameof(IconsHash.HashForItem), new[] { typeof(Item) });

        // --- cell blocking (Ф20) ---

        /// <summary>
        /// Rig constructor: the slots exist by the end of it (CompoundItem builds them), and the
        /// first AddInternal - a profile load - comes after it.
        /// </summary>
        public static MethodBase Vest_Ctor => AccessTools.FirstConstructor(typeof(Vest),
            c => !c.IsStatic && c.GetParameters().Length == 2 && c.GetParameters()[0].ParameterType == typeof(string));

        /// <summary>Which slots an item blocks from a slot; an iterator, patched from the outside.</summary>
        public static MethodBase Slot_GetConflictingSlot =>
            AccessTools.Method(typeof(Slot), nameof(Slot.GetConflictingSlot), new[] { typeof(Item) });

        public static MethodBase Slot_CheckConditions => AccessTools.Method(typeof(Slot), nameof(Slot.CheckConditions),
            new[] { typeof(Item), typeof(bool), typeof(bool) });

        // --- raid rules (Ф12) ---

        public static MethodBase Slot_CanAcceptRaid => AccessTools.Method(typeof(Slot), nameof(Slot.CanAcceptRaid));

        /// <summary>Where automatic placement (QuickFindAppropriatePlace) asks a slot for room.</summary>
        public static MethodBase Slot_TryFindLocationForItem => AccessTools.Method(typeof(Slot),
            nameof(Slot.TryFindLocationForItem), new[] { typeof(Item), typeof(ItemAddress).MakeByRefType() });

        public static MethodBase Slot_RemoveItemInternal => AccessTools.Method(typeof(Slot),
            nameof(Slot.RemoveItemInternal), new[] { typeof(bool), typeof(bool) });

        // --- reach in raid (Ф13) ---

        public static MethodInfo GetReachableItemsOfTypeNonAllocDefinition { get; } = AccessTools.Method(
            typeof(InventoryController), nameof(InventoryController.GetReachableItemsOfTypeNonAlloc));

        public static MethodInfo GetReachableItemsOfTypeDefinition { get; } = AccessTools.Method(
            typeof(InventoryController), nameof(InventoryController.GetReachableItemsOfType));

        /// <summary>
        /// The search the other two end up in, and the one the bots call directly: it names the
        /// equipment slots to walk instead of taking the fast-access ones.
        /// </summary>
        public static MethodInfo GetAcceptableItemsNonAllocDefinition { get; } = AccessTools.Method(
            typeof(InventoryController), nameof(InventoryController.GetAcceptableItemsNonAlloc));

        /// <summary>
        /// Types whose methods (lambdas and state machines included) search reachable items:
        /// for the player, reload and its quick variants, the ammo/magazine selectors, the
        /// grenade hotkey and the UI magazine lookup; for bots, reloading, topping magazines up
        /// and first aid - a bot wearing a modular rig has all of that in its pouches.
        /// </summary>
        public static readonly Type[] ReachCallerTypes =
        {
            typeof(FirearmHandsInputTranslator), typeof(PlayerInputTranslator), typeof(ItemUiContext),
            typeof(AmmoSelector),
            typeof(BotReload), typeof(BotReloadMagazine), typeof(BotFirstAid),
        };

        /// <summary>Every non-generic method of <see cref="ReachCallerTypes"/> that calls a reachable-item search.</summary>
        public static List<MethodBase> ReachCallSites() => _reachCallSites ??= ScanReachCallSites();

        private static List<MethodBase> _reachCallSites;

        private static List<MethodBase> ScanReachCallSites()
        {
            var result = new List<MethodBase>();
            if (GetReachableItemsOfTypeNonAllocDefinition == null || GetReachableItemsOfTypeDefinition == null ||
                GetAcceptableItemsNonAllocDefinition == null)
            {
                return result;
            }

            foreach (var type in ReachCallerTypes.SelectMany(WithNested))
            {
                if (type.ContainsGenericParameters)
                {
                    continue;
                }

                foreach (var method in type.GetMethods(AccessTools.allDeclared))
                {
                    if (!method.IsAbstract && !method.ContainsGenericParameters && CallsReach(method))
                    {
                        result.Add(method);
                    }
                }
            }

            return result;
        }

        private static IEnumerable<Type> WithNested(Type type)
        {
            yield return type;
            foreach (var nested in type.GetNestedTypes(AccessTools.all))
            {
                foreach (var t in WithNested(nested))
                {
                    yield return t;
                }
            }
        }

        private static bool CallsReach(MethodBase method)
        {
            try
            {
                foreach (var instruction in PatchProcessor.GetOriginalInstructions(method))
                {
                    if (instruction.operand is MethodInfo called && called.IsGenericMethod &&
                        called.DeclaringType == typeof(InventoryController))
                    {
                        var definition = called.GetGenericMethodDefinition();
                        if (definition == GetReachableItemsOfTypeNonAllocDefinition ||
                            definition == GetReachableItemsOfTypeDefinition ||
                            definition == GetAcceptableItemsNonAllocDefinition)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // a body the reader cannot parse is not a call site we can rewrite anyway
            }

            return false;
        }

        /// <summary>The equipment's containers in the order automatic placement fills them (quick move, loot).</summary>
        public static MethodBase InventoryEquipmentExtension_GetPrioritizedContainersForLoot => AccessTools.Method(
            typeof(InventoryEquipmentExtension), nameof(InventoryEquipmentExtension.GetPrioritizedContainersForLoot));

        /// <summary>Finds room for an item in the given items' containers (drops onto an item, quick moves).</summary>
        public static MethodBase ItemManipulator_QuickFindAppropriatePlace => AccessTools.Method(
            typeof(ItemManipulator), nameof(ItemManipulator.QuickFindAppropriatePlace), new[]
            {
                typeof(Item), typeof(ItemController), typeof(IEnumerable<CompoundItem>),
                typeof(ItemManipulator.EMoveItemOrder), typeof(bool),
            });

        /// <summary>Where unloaded rounds go.</summary>
        public static MethodBase InventoryEquipmentExtension_GetPrioritizedGridsForUnloadedObject => AccessTools.Method(
            typeof(InventoryEquipmentExtension), nameof(InventoryEquipmentExtension.GetPrioritizedGridsForUnloadedObject));

        /// <summary>One level into the given containers: their slots and their grids (the search
        /// behind the context menu out of raid).</summary>
        public static MethodBase InventoryExtension_GetTopLevelItems =>
            AccessTools.Method(typeof(InventoryExtension), nameof(InventoryExtension.GetTopLevelItems));

        public static MethodBase InventoryExtension_GetThrowablePriorityGrenadesList =>
            AccessTools.Method(typeof(InventoryExtension), nameof(InventoryExtension.GetThrowablePriorityGrenadesList));

        public static MethodBase InventoryExtension_GetThrowableGrenadesNonAlloc =>
            AccessTools.Method(typeof(InventoryExtension), nameof(InventoryExtension.GetThrowableGrenadesNonAlloc));

        public static MethodBase InventoryController_IsAtBindablePlace => AccessTools.Method(
            typeof(InventoryController), nameof(InventoryController.IsAtBindablePlace), new[] { typeof(Item) });

        // --- self-test ---

        private static readonly Dictionary<string, Func<object>> All = new Dictionary<string, Func<object>>
        {
            { nameof(GeneratedGridsView_Show), () => GeneratedGridsView_Show },
            { nameof(GridView_Show), () => GridView_Show },
            { nameof(UIElement_UI), () => UIElement_UI },
            { nameof(SearchableSlotView_CreateSlots), () => SearchableSlotView_CreateSlots },
            { nameof(ModdingScreenSlotView_CheckVisibility), () => ModdingScreenSlotView_CheckVisibility },
            { nameof(ModSlotView_Show), () => ModSlotView_Show },
            { nameof(TradingGridView_ApplyFilter), () => TradingGridView_ApplyFilter },
            { nameof(ItemSorter_Sort), () => ItemSorter_Sort },
            { "ModdingScreenSlotView._slot",() => AccessTools.Field(typeof(ModdingScreenSlotView), ModdingScreenSlotView_Slot) },
            { "GeneratedGridsView._gridViewTemplate", () => AccessTools.Field(typeof(GeneratedGridsView), "_gridViewTemplate") },
            { nameof(ObjectsFactory_AttachMods), () => ObjectsFactory_AttachMods },
            { nameof(PlayerBody_SlotView_CreateAndParent), () => PlayerBody_SlotView_CreateAndParent },
            { nameof(EasyBundle_SetAssets), () => EasyBundle_SetAssets },
            { nameof(IconsHash_HashForItem), () => IconsHash_HashForItem },
            { nameof(SlotView_InsertItem), () => SlotView_InsertItem },
            { nameof(Vest_Ctor), () => Vest_Ctor },
            { nameof(Slot_GetConflictingSlot), () => Slot_GetConflictingSlot },
            { nameof(Slot_CheckConditions), () => Slot_CheckConditions },
            { "Slot.ConflictingSlots", () => AccessTools.Field(typeof(Slot), nameof(Slot.ConflictingSlots)) },
            { "Slot.BlockerSlots", () => AccessTools.Field(typeof(Slot), nameof(Slot.BlockerSlots)) },
            { nameof(Slot_CanAcceptRaid), () => Slot_CanAcceptRaid },
            { nameof(Slot_TryFindLocationForItem), () => Slot_TryFindLocationForItem },
            { nameof(InventoryEquipmentExtension_GetPrioritizedContainersForLoot), () => InventoryEquipmentExtension_GetPrioritizedContainersForLoot },
            { nameof(ItemManipulator_QuickFindAppropriatePlace), () => ItemManipulator_QuickFindAppropriatePlace },
            { nameof(InventoryEquipmentExtension_GetPrioritizedGridsForUnloadedObject), () => InventoryEquipmentExtension_GetPrioritizedGridsForUnloadedObject },
            { nameof(Slot_RemoveItemInternal), () => Slot_RemoveItemInternal },
            { nameof(GetReachableItemsOfTypeNonAllocDefinition), () => GetReachableItemsOfTypeNonAllocDefinition },
            { nameof(GetReachableItemsOfTypeDefinition), () => GetReachableItemsOfTypeDefinition },
            { nameof(GetAcceptableItemsNonAllocDefinition), () => GetAcceptableItemsNonAllocDefinition },
            { "FirearmHandsInputTranslator.ReloadExternalMagazine (reach call site)", () => ReachCallSites().Find(m => m.Name == nameof(FirearmHandsInputTranslator.ReloadExternalMagazine)) },
            { "BotReload (reach call sites)", () => ReachCallSiteIn(typeof(BotReload)) },
            { "BotReloadMagazine (reach call sites)", () => ReachCallSiteIn(typeof(BotReloadMagazine)) },
            { "BotFirstAid (reach call sites)", () => ReachCallSiteIn(typeof(BotFirstAid)) },
            { nameof(InventoryExtension_GetTopLevelItems), () => InventoryExtension_GetTopLevelItems },
            { nameof(InventoryExtension_GetThrowablePriorityGrenadesList), () => InventoryExtension_GetThrowablePriorityGrenadesList },
            { nameof(InventoryExtension_GetThrowableGrenadesNonAlloc), () => InventoryExtension_GetThrowableGrenadesNonAlloc },
            { nameof(InventoryController_IsAtBindablePlace), () => InventoryController_IsAtBindablePlace },
        };

        /// <summary>A rewritten call site in this type or one nested in it; null when there is none.</summary>
        private static MethodBase ReachCallSiteIn(Type type)
        {
            var nested = new HashSet<Type>(WithNested(type));
            return ReachCallSites().Find(m => m.DeclaringType != null && nested.Contains(m.DeclaringType));
        }

        /// <summary>Resolves every target; returns the unresolved ones (empty = all good).</summary>
        public static List<string> SelfTest() => SelfTest(All);

        /// <summary>Resolves the given targets; returns the unresolved ones.</summary>
        public static List<string> SelfTest(Dictionary<string, Func<object>> targets)
        {
            var failed = new List<string>();
            foreach (var kv in targets)
            {
                try
                {
                    if (kv.Value() == null)
                    {
                        failed.Add(kv.Key);
                    }
                }
                catch (Exception)
                {
                    failed.Add(kv.Key);
                }
            }

            return failed;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT.InventoryLogic;
using HarmonyLib;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// Ф13: what lies in a pouch on the rig is within reach in raid — quick reload, grenade
    /// selection, quick-slot binds — for the player and for bots alike (Ф45: a bot in a modular
    /// rig keeps its spare magazines, its loose rounds and its medicine in the pouches, and
    /// without this it would reload once and then stand there).
    ///
    /// The game looks for reachable items through the generic
    /// InventoryController.GetAcceptableItemsNonAlloc&lt;TItem&gt;, which only walks the
    /// top-level containers of the rig and the pockets. That method is NOT patched: every
    /// TItem is a reference type, so on Mono all instantiations run one shared body, and a
    /// Harmony replacement compiled for one TItem would type-check every other caller's
    /// items against the wrong type. Instead, the non-generic call sites of the player's
    /// reload/grenade code get a <see cref="Reach"/> extension appended after the search,
    /// closed per call site, which adds the pouch contents to what the search found.
    /// </summary>
    internal static class RaidReachPatch
    {
        public static void Apply(Harmony harmony)
        {
            var sites = PatchTargets.ReachCallSites();
            if (sites.Count == 0)
            {
                throw new InvalidOperationException("no reachable-item call sites found");
            }

            var transpiler = new HarmonyMethod(typeof(RaidReachPatch), nameof(Transpiler));
            foreach (var site in sites)
            {
                harmony.Patch(site, transpiler: transpiler);
            }

            Plugin.Log.LogInfo($"[ModularVests] reachable-item search extended at {sites.Count} call site(s)");

            harmony.Patch(PatchTargets.InventoryExtension_GetThrowablePriorityGrenadesList
                          ?? throw new InvalidOperationException("GetThrowablePriorityGrenadesList not found"),
                postfix: new HarmonyMethod(typeof(RaidReachPatch), nameof(PriorityGrenadesPostfix)));
            harmony.Patch(PatchTargets.InventoryExtension_GetThrowableGrenadesNonAlloc
                          ?? throw new InvalidOperationException("GetThrowableGrenadesNonAlloc not found"),
                postfix: new HarmonyMethod(typeof(RaidReachPatch), nameof(GrenadesNonAllocPostfix)));
            harmony.Patch(PatchTargets.InventoryController_IsAtBindablePlace
                          ?? throw new InvalidOperationException("IsAtBindablePlace not found"),
                postfix: new HarmonyMethod(typeof(RaidReachPatch), nameof(IsAtBindablePlacePostfix)));
        }

        // --- call-site extension ---

        /// <summary>
        /// Appends the pouch search to a reachable-item search without touching the call
        /// itself: the arguments are spilled into locals, the original call stays exactly as
        /// it stands - operand included - and the extension runs on what it found. Replacing
        /// the operand would hide the call from the other mods that extend the same searches
        /// the same way (Use Items Anywhere rewrites these very call sites, and a mod that no
        /// longer finds its call gives up on the method or throws).
        /// </summary>
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            ILGenerator generator, MethodBase __originalMethod)
        {
            var extended = 0;
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    instruction.operand is MethodInfo called &&
                    Reach.TryGetExtension(called, out var extension, out var arguments))
                {
                    foreach (var emitted in Extend(instruction, called, extension, arguments, generator))
                    {
                        yield return emitted;
                    }

                    extended++;
                    continue;
                }

                yield return instruction;
            }

            if (extended == 0)
            {
                Plugin.Log.LogWarning($"[ModularVests] {Describe(__originalMethod)}: no vanilla reachable-item " +
                                      "search left to extend (another mod rewrote the call) - pouch contents stay " +
                                      "out of that search");
            }
        }

        /// <summary>
        /// The original call, preceded by the spill of its arguments into fresh locals and
        /// followed by the extension fed from those same locals. Whatever the call leaves on
        /// the stack is the extension's first argument.
        /// </summary>
        private static IEnumerable<CodeInstruction> Extend(CodeInstruction call, MethodInfo called,
            MethodInfo extension, int[] arguments, ILGenerator generator)
        {
            var parameters = called.GetParameters();
            var locals = new LocalBuilder[parameters.Length + 1];
            locals[0] = generator.DeclareLocal(called.DeclaringType);
            for (var i = 0; i < parameters.Length; i++)
            {
                locals[i + 1] = generator.DeclareLocal(parameters[i].ParameterType);
            }

            var spill = new List<CodeInstruction>();
            for (var i = locals.Length - 1; i >= 0; i--)
            {
                spill.Add(new CodeInstruction(OpCodes.Stloc, locals[i]));
            }

            // a jump aimed at the call arrives with the arguments on the stack, so it has to
            // land on the spill instead
            spill[0].labels.AddRange(call.labels);
            call.labels.Clear();

            foreach (var instruction in spill)
            {
                yield return instruction;
            }

            foreach (var local in locals)
            {
                yield return new CodeInstruction(OpCodes.Ldloc, local);
            }

            yield return call;

            foreach (var index in arguments)
            {
                yield return new CodeInstruction(OpCodes.Ldloc, locals[index]);
            }

            yield return new CodeInstruction(OpCodes.Call, extension);
        }

        private static string Describe(MethodBase method) =>
            method == null ? "<unknown>" : $"{method.DeclaringType?.Name}.{method.Name}";

        // --- grenades ---

        private static void PriorityGrenadesPostfix(InventoryController inventoryController, List<ThrowWeap> __result)
        {
            if (__result == null)
            {
                return;
            }

            var added = false;
            foreach (var grenade in Reach.PouchItems<ThrowWeap>(inventoryController, Reach.GrenadeSlots))
            {
                if (inventoryController.Examined(grenade) && !__result.Contains(grenade))
                {
                    __result.Add(grenade);
                    added = true;
                }
            }

            if (added)
            {
                __result.Sort((x, y) => x.ThrowType.CompareTo(y.ThrowType));
            }
        }

        private static void GrenadesNonAllocPostfix(InventoryController inventoryController,
            SortedDictionary<string, List<ThrowWeap>> grenadesDictionary)
        {
            if (grenadesDictionary == null)
            {
                return;
            }

            foreach (var grenade in Reach.PouchItems<ThrowWeap>(inventoryController, Reach.GrenadeSlots))
            {
                if (!inventoryController.Examined(grenade))
                {
                    continue;
                }

                if (!grenadesDictionary.TryGetValue(grenade.TemplateId, out var list))
                {
                    grenadesDictionary.Add(grenade.TemplateId, list = new List<ThrowWeap>());
                }

                if (!list.Contains(grenade))
                {
                    list.Add(grenade);
                }
            }
        }

        // --- binds ---

        /// <summary>
        /// Vanilla: bindable when the item's container sits directly in a fast-access slot.
        /// Here: also when that container is a pouch on a rig that sits there. The type and
        /// state conditions are vanilla's own.
        /// </summary>
        private static void IsAtBindablePlacePostfix(InventoryController __instance, Item item, ref bool __result)
        {
            if (__result || item?.CurrentAddress == null || item.Parent is OwnerItself)
            {
                return;
            }

            var pouch = item.Parent.Container?.ParentItem;
            var pouchSlot = PouchSlots.SlotOf(pouch);
            if (pouchSlot == null)
            {
                return;
            }

            var rigSlot = pouchSlot.ParentItem?.CurrentAddress?.Container as Slot;
            if (rigSlot == null || !Reach.IsFastAccessSlot(__instance, rigSlot))
            {
                return;
            }

            if (item is CompoundItem compound && compound.MissingVitalParts.Any())
            {
                return;
            }

            if (!__instance.Examined(item))
            {
                return;
            }

            __result = item is Weapon || item is ThrowWeap || item.GetItemComponent<KnifeComponent>() != null ||
                       item is Meds || item is FoodDrink || item is PortableRangeFinder || item is Compass ||
                       item is RadioTransmitter;
        }
    }

    /// <summary>
    /// What follows a reachable-item search at a rewritten call site, one closed instantiation
    /// per site: the vanilla search runs untouched, then these add what lies in the pouches.
    /// </summary>
    public static class Reach
    {
        internal static readonly EquipmentSlot[] GrenadeSlots = { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets };

        private static readonly MethodInfo ReachableExtension =
            AccessTools.Method(typeof(Reach), nameof(AddPouchItemsToReachable));

        private static readonly MethodInfo AllocatingExtension =
            AccessTools.Method(typeof(Reach), nameof(PlusPouchItems));

        private static readonly MethodInfo AcceptableExtension =
            AccessTools.Method(typeof(Reach), nameof(AddPouchItemsToAcceptable));

        /// <summary>
        /// The extension that follows the given search and which of the spilled values it
        /// takes: 0 is the controller, 1 and up are the call's own arguments in order. What the
        /// search itself returns is already on the stack and is not listed.
        /// </summary>
        internal static bool TryGetExtension(MethodInfo called, out MethodInfo extension, out int[] arguments)
        {
            extension = null;
            arguments = null;
            if (!called.IsGenericMethod || called.DeclaringType != typeof(InventoryController))
            {
                return false;
            }

            var definition = called.GetGenericMethodDefinition();
            var typeArguments = called.GetGenericArguments();
            if (definition == PatchTargets.GetReachableItemsOfTypeNonAllocDefinition)
            {
                extension = ReachableExtension.MakeGenericMethod(typeArguments);
                arguments = new[] { 0, 1, 2 }; // controller, list, predicate
            }
            else if (definition == PatchTargets.GetReachableItemsOfTypeDefinition)
            {
                extension = AllocatingExtension.MakeGenericMethod(typeArguments);
                arguments = new[] { 0, 1 }; // (items found), controller, predicate
            }
            else if (definition == PatchTargets.GetAcceptableItemsNonAllocDefinition)
            {
                extension = AcceptableExtension.MakeGenericMethod(typeArguments);
                arguments = new[] { 0, 1, 2, 3 }; // controller, slots, list, predicate
            }

            return extension != null;
        }

        /// <summary>Adds what lies in the pouches of the rigs in the fast-access slots.</summary>
        public static void AddPouchItemsToReachable<TItem>(InventoryController controller,
            IList<TItem> preAllocatedList, Predicate<TItem> predicate) where TItem : Item =>
            AddPouchItems(controller, Inventory.FastAccessSlots, preAllocatedList, predicate);

        /// <summary>The same, for the search that allocates and returns its own list.</summary>
        public static IEnumerable<TItem> PlusPouchItems<TItem>(IEnumerable<TItem> found,
            InventoryController controller, Predicate<TItem> predicate) where TItem : Item
        {
            var list = found == null ? new List<TItem>() : new List<TItem>(found);
            AddPouchItems(controller, Inventory.FastAccessSlots, list, predicate);
            return list;
        }

        /// <summary>
        /// The search that names its own equipment slots — what the bots' reload, magazine
        /// top-up and first aid call. The pouches searched are the ones on the rigs in those
        /// same slots, so a bot looking in its rig finds what hangs on it.
        /// </summary>
        public static void AddPouchItemsToAcceptable<TItem>(InventoryController controller,
            EquipmentSlot[] equipmentSlots, IList<TItem> preAllocatedList, Predicate<TItem> predicate)
            where TItem : Item =>
            AddPouchItems(controller, equipmentSlots, preAllocatedList, predicate);

        private static void AddPouchItems<TItem>(InventoryController controller, EquipmentSlot[] equipmentSlots,
            IList<TItem> list, Predicate<TItem> predicate) where TItem : Item
        {
            if (list == null || list.IsReadOnly)
            {
                return;
            }

            try
            {
                foreach (var item in PouchItems<TItem>(controller, equipmentSlots))
                {
                    if ((predicate == null || predicate(item)) && !list.Contains(item))
                    {
                        list.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] pouch search: {ex}");
            }
        }

        /// <summary>Items of the type lying in the pouches of the rigs in the given equipment slots.</summary>
        internal static IEnumerable<TItem> PouchItems<TItem>(InventoryController controller, EquipmentSlot[] slots)
            where TItem : Item
        {
            var equipment = controller?.Inventory?.Equipment;
            if (equipment == null || slots == null)
            {
                yield break;
            }

            foreach (var slotName in slots)
            {
                if (!(equipment.GetSlot(slotName)?.ContainedItem is CompoundItem rig))
                {
                    continue;
                }

                foreach (var pouch in PouchSlots.AttachedPouches(rig))
                {
                    if (pouch.Grids == null)
                    {
                        continue;
                    }

                    foreach (var grid in pouch.Grids)
                    {
                        foreach (var item in grid.Items)
                        {
                            if (item is TItem typed && !(item is UnknownItem))
                            {
                                yield return typed;
                            }
                        }
                    }
                }
            }
        }

        internal static bool IsFastAccessSlot(InventoryController controller, Slot slot)
        {
            var equipment = controller?.Inventory?.Equipment;
            if (equipment == null)
            {
                return false;
            }

            foreach (var slotName in Inventory.FastAccessSlots)
            {
                if (equipment.GetSlot(slotName) == slot)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

using System;
using EFT;
using EFT.InventoryLogic;
using Diz.LanguageExtensions;
using HarmonyLib;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// Ф12: in raid a pouch can be neither attached nor detached. Only the pouch slot is
    /// locked: what lies inside the pouch stays free to take and to put.
    /// </summary>
    internal static class RaidLockPatch
    {
        public static void Apply(Harmony harmony)
        {
            harmony.Patch(PatchTargets.Slot_CanAcceptRaid
                          ?? throw new InvalidOperationException("Slot.CanAcceptRaid not found"),
                prefix: new HarmonyMethod(typeof(RaidLockPatch), nameof(CanAcceptRaidPrefix)));
            harmony.Patch(PatchTargets.Slot_RemoveItemInternal
                          ?? throw new InvalidOperationException("Slot.RemoveItemInternal not found"),
                prefix: new HarmonyMethod(typeof(RaidLockPatch), nameof(RemoveItemInternalPrefix)));
        }

        private static bool IsLocked(Slot slot) =>
            ClientConfig.LockPouchesInRaid.Value && InGameStatus.InRaid && PouchSlots.IsPouchSlot(slot);

        private static bool CanAcceptRaidPrefix(Slot __instance, ref InventoryError error, ref bool __result)
        {
            if (!IsLocked(__instance))
            {
                return true;
            }

            error = new Slot.SlotLockedError(__instance);
            __result = false;
            return false;
        }

        private static bool RemoveItemInternalPrefix(Slot __instance, bool ignoreRestrictions,
            ref OperationResult<ContainerRemoveResult> __result)
        {
            if (ignoreRestrictions || __instance.ContainedItem == null || !IsLocked(__instance))
            {
                return true;
            }

            __result = new Slot.SlotLockedError(__instance);
            return false;
        }
    }
}

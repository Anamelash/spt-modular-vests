using System;
using EFT.InventoryLogic;
using HarmonyLib;
using ModularVests.Client.Bones;
using UnityEngine;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// Ф21: right after the game hangs a pouch model on its cell bone, the model is moved onto
    /// its seat (PouchSeat): the middle of the cells it covers, with its mount.
    /// </summary>
    internal static class PouchSeatPatch
    {
        private static bool _errorLogged;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(PatchTargets.SlotView_InsertItem
                          ?? throw new InvalidOperationException("ContainerCollectionView.SlotView.InsertItem not found"),
                postfix: new HarmonyMethod(typeof(PouchSeatPatch), nameof(Postfix)));
        }

        private static void Postfix(ContainerCollectionView.SlotView __instance, Item item, GameObject itemView)
        {
            try
            {
                var slot = PouchSlots.SlotOf(item);
                if (slot == null || itemView == null)
                {
                    return;
                }

                PouchSeat.Reseat(slot, item, __instance.Bone, itemView.transform);

                // a pouch hung on a rig that is already worn: no solid colliders inside the wearer
                if (PouchPhysics.OnBody(__instance.Bone))
                {
                    PouchPhysics.DisableColliders(itemView.transform);
                }
            }
            catch (Exception ex)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Plugin.Log.LogError($"[ModularVests] pouch seat: {ex}");
                }
            }
        }
    }
}

using System;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using ModularVests.Client.Bones;
using UnityEngine;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// A rig worn on a character is not hung on a bone: PlayerBody.SlotView.CreateAndParent
    /// parents the model to the body's mesh transform and skins its meshes onto the player's
    /// skeleton (Dress.Skin). The prefab's transforms - and the pouch bones inside them - are
    /// left where they were, so pouches hung in the air and ignored every animation.
    ///
    /// Right after that call the pouch bones are tied to the skeleton the meshes now use.
    /// </summary>
    internal static class PouchBodyPatch
    {
        public static void Apply(Harmony harmony)
        {
            var target = PatchTargets.PlayerBody_SlotView_CreateAndParent
                         ?? throw new InvalidOperationException("PlayerBody.SlotView.CreateAndParent not found");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(PouchBodyPatch), nameof(Postfix)));
        }

        private static void Postfix(PlayerBody.SlotView __instance, PlayerBody playerBody, GameObject model)
        {
            try
            {
                PouchBones.BindToSkeleton(playerBody, __instance._item as CompoundItem, model);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] pouch bones on a character: {ex}");
            }
        }
    }
}

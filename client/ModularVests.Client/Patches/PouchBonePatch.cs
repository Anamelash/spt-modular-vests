using System;
using EFT.InventoryLogic;
using HarmonyLib;
using ModularVests.Client.Bones;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// Ф10: before the game matches a model's slots with its bones, give the pouch slots
    /// bones to match. Runs synchronously at the call, ahead of the async body.
    /// </summary>
    internal static class PouchBonePatch
    {
        private static bool _errorLogged;

        public static void Apply(Harmony harmony)
        {
            var target = PatchTargets.ObjectsFactory_AttachMods
                         ?? throw new InvalidOperationException("ObjectsFactory.AttachMods not found");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(PouchBonePatch), nameof(Prefix)));
        }

        private static void Prefix(ContainerCollection containerCollection, ContainerCollectionView collectionView)
        {
            try
            {
                if (!(containerCollection is CompoundItem rig) || !PouchSlots.HasAny(rig) ||
                    collectionView == null || collectionView.GameObject == null)
                {
                    return;
                }

                PouchBones.Ensure(rig, collectionView.GameObject.transform);
            }
            catch (Exception ex)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Plugin.Log.LogError($"[ModularVests] pouch bones: {ex}");
                }
            }
        }
    }
}

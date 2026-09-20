using EFT;
using EFT.AssetsManager;
using UnityEngine;

namespace ModularVests.Client.Bones
{
    /// <summary>
    /// A rig worn on a body has its loot half switched off (DressItem.EnableLoot), and with it
    /// everything hung on it - the colliders of plate models included. The mod moves the pouch
    /// bones into the dress half so the pouches show, and their models came along with their solid
    /// colliders still on, hugging the torso inside the character's own capsule: physics pushed the
    /// player out of them every frame (drifting sideways, barely answering the controls).
    ///
    /// On a body the pouch models' solid colliders are switched off through the pool's own record
    /// (AssetPoolObject.StoreCollider), which switches them back on when the model goes back to the
    /// pool - a pooled model is used in the preview and on the ground again.
    /// </summary>
    internal static class PouchPhysics
    {
        /// <summary>Whether a transform hangs on a player's body.</summary>
        public static bool OnBody(Transform transform) =>
            transform != null && transform.GetComponentInParent<PlayerBody>() != null;

        /// <summary>Switches off the solid colliders of a pouch model; returns how many it switched off.</summary>
        public static int DisableColliders(Transform pouchModel)
        {
            if (pouchModel == null)
            {
                return 0;
            }

            var pool = pouchModel.GetComponentInParent<AssetPoolObject>();
            var count = 0;
            foreach (var collider in pouchModel.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (collider.isTrigger || !collider.enabled)
                {
                    continue;
                }

                pool?.StoreCollider(collider);
                collider.enabled = false;
                count++;
            }

            return count;
        }
    }
}

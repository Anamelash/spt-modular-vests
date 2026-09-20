using UnityEngine;

namespace ModularVests.Client.Bones
{
    /// <summary>
    /// Makes a pouch bone follow a bone of the character's skeleton.
    ///
    /// On a body the rig's model is not hung on a bone: its meshes are re-bound to the
    /// player's skeleton (Dress.Skin) while the prefab's own transforms stay where they were,
    /// so a pouch parented inside that prefab hangs in the air and never moves. Re-parenting
    /// the bone under the skeleton would fight the model pool, which hands the same prefab out
    /// again for a preview; copying the pose every LateUpdate leaves the hierarchy alone.
    ///
    /// The component is never destroyed, only unbound. Destroy is deferred to the end of the
    /// frame, and a pooled model is re-posed (Clear) and re-bound (Bind) within one frame when
    /// a character is rebuilt - after a trip to the main menu, say. The bind then went to the
    /// component already marked for destruction, and the pouches were left hanging in the air.
    /// </summary>
    internal sealed class PouchBoneFollower : MonoBehaviour
    {
        private Transform _target;
        private Vector3 _localPosition;
        private Quaternion _localRotation;
        private bool _bound;

        public Transform Target => _target;

        public void Bind(Transform target, Vector3 localPosition, Quaternion localRotation)
        {
            _target = target;
            _localPosition = localPosition;
            _localRotation = localRotation;
            _bound = target != null;
            enabled = true;

            // a bone hidden when its last skeleton went away comes back with the new one
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            if (_bound)
            {
                Apply();
            }
        }

        /// <summary>Unbinds a bone that is being posed in its model's own space again.</summary>
        public static void Clear(Transform bone)
        {
            var follower = bone.GetComponent<PouchBoneFollower>();
            if (follower != null)
            {
                follower._target = null;
                follower._bound = false;
                follower.enabled = false;
            }

            if (!bone.gameObject.activeSelf)
            {
                bone.gameObject.SetActive(true);
            }
        }

        private void LateUpdate()
        {
            if (!_bound)
            {
                return;
            }

            if (_target == null)
            {
                // The skeleton this bone followed is gone while the rig model is still out. A
                // pouch left where the bone happens to be would float in the air: hide it until
                // the model is bound or posed again (both switch the bone back on). A death does
                // not get here - the corpse keeps the player's own skeleton, and the ragdoll
                // moves the very bones the pouches follow.
                _bound = false;
                gameObject.SetActive(false);
                return;
            }

            Apply();
        }

        private void Apply()
        {
            var bone = transform;
            bone.position = _target.TransformPoint(_localPosition);
            bone.rotation = _target.rotation * _localRotation;
        }
    }
}

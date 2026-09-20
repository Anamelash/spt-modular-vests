using System.Collections.Generic;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using UnityEngine;

namespace ModularVests.Client.Bones
{
    /// <summary>
    /// Where a pouch model sits on the rig. The game hangs it on the bone of its anchor cell
    /// (ContainerCollectionView.SlotView.InsertItem); a pouch that covers several cells belongs
    /// in the middle of them, so the model is moved under that bone:
    ///
    /// the seat stands in the middle of the covered cells' bones, and it is turned the way the
    /// cells lie, not the way the bones happen to be turned: its up runs from the lower row of
    /// cells to the upper one, its right from the left column to the right one (as seen from
    /// the front), exactly as the segments run, tilt into the surface included; the bones (their
    /// mean outward direction) only decide which side faces out. A 1x2 pouch
    /// follows the segment between its two cells; moving one of them turns it. The model's mount
    /// (mounts.json, else a ModPlacer on the model, else the game's own default) puts the model
    /// straight on the seat. A 1x1 pouch's seat is its bone: it sits exactly as the game puts it.
    /// </summary>
    internal static class PouchSeat
    {
        /// <summary>Re-seats every pouch attached to a rig, on the given model of it.</summary>
        public static void ReseatAll(CompoundItem rig, Transform modelRoot)
        {
            if (rig == null || modelRoot == null)
            {
                return;
            }

            var views = modelRoot.GetComponent<AssetPoolObject>()?.ContainerCollectionView;
            foreach (var slot in PouchSlots.Of(rig))
            {
                if (slot.ContainedItem == null)
                {
                    continue;
                }

                Transform bone = null;
                Transform view = null;
                if (views != null && views.ContainerBones.TryGetValue(slot, out var slotView))
                {
                    bone = slotView.Bone;
                    view = slotView.ItemView;
                }

                if (bone == null)
                {
                    bone = TransformTools.FindTransformRecursive(modelRoot, slot.ID, ignoreCase: true);
                }

                if (view == null && bone != null)
                {
                    view = ModelOn(bone);
                }

                Reseat(slot, slot.ContainedItem, bone, view, modelRoot);
            }
        }

        /// <summary>
        /// Seats one pouch model hung on its anchor bone. <paramref name="modelRoot"/> is the
        /// rig model the cell bones are looked up in; null finds it from the bone.
        /// </summary>
        public static void Reseat(Slot slot, Item pouch, Transform bone, Transform view, Transform modelRoot = null)
        {
            if (slot == null || pouch == null || bone == null || view == null || view.parent != bone)
            {
                return;
            }

            if (!Seat(slot, pouch, bone, modelRoot, out var seatPosition, out var seatRotation))
            {
                return;
            }

            GetMount(pouch, view, out var mountPosition, out var mountRotation);
            view.localPosition = seatPosition + seatRotation * mountPosition;
            view.localRotation = seatRotation * mountRotation;
        }

        /// <summary>
        /// The seat of a pouch in its anchor bone's space. False when the slot is not a cell or
        /// the pouch's footprint is unknown.
        /// </summary>
        public static bool Seat(Slot slot, Item pouch, Transform bone, Transform modelRoot,
            out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (!ClusterGrid.TryParse(slot.ID, out var cluster, out var cell))
            {
                return false;
            }

            var footprint = PouchSlots.FootprintOf(pouch, slot.ParentItem as CompoundItem);
            if (footprint == null)
            {
                return false;
            }

            var covered = ClusterGrid.Covered(cell, footprint.Value);
            if (covered.Length == 0)
            {
                return true;
            }

            // the bones of every cell the pouch takes, by cell
            var root = modelRoot != null ? modelRoot : RootOf(bone);
            var cells = new Dictionary<int, Transform> { [cell] = bone };
            foreach (var other in covered)
            {
                var found = root != null
                    ? TransformTools.FindTransformRecursive(root, ClusterGrid.SlotName(cluster, other), ignoreCase: true)
                    : null;
                if (found != null)
                {
                    cells[other] = found;
                }
            }

            if (cells.Count == 1)
            {
                return true;
            }

            var sum = Vector3.zero;
            var outward = Vector3.zero;
            var rotations = new List<Quaternion>();
            foreach (var t in cells.Values)
            {
                sum += t.position;
                outward += t.forward;
                rotations.Add(t.rotation);
            }

            position = bone.InverseTransformPoint(sum / cells.Count);
            var turned = FrameFromCells(cells, cell, footprint.Value, outward) ?? Average(rotations);
            rotation = Quaternion.Inverse(bone.rotation) * turned;
            return true;
        }

        /// <summary>
        /// The seat's orientation from where the cells lie: up from the lower row to the upper,
        /// right (as seen from the front) from the left column to the right - exactly, tilt into the
        /// surface included; the bones' mean outward direction only picks the facing side (and the
        /// turn around a single segment). Null when the cells give no direction
        /// (missing bones, cells on top of each other, a segment along the outward direction).
        /// </summary>
        private static Quaternion? FrameFromCells(Dictionary<int, Transform> cells, int anchor, Footprint footprint,
            Vector3 outward)
        {
            if (outward.sqrMagnitude < 1e-8f)
            {
                return null;
            }

            var normal = outward.normalized;
            var column = ClusterGrid.ColumnOf(anchor);
            var row = ClusterGrid.RowOf(anchor);
            var up = Vector3.zero;
            var right = Vector3.zero;

            // every vertical segment (top minus bottom) and every horizontal one (right minus left)
            for (var c = column; c < column + footprint.Width; c++)
            {
                for (var r = row; r + 1 < row + footprint.Height; r++)
                {
                    if (cells.TryGetValue(ClusterGrid.PositionAt(c, r), out var top) &&
                        cells.TryGetValue(ClusterGrid.PositionAt(c, r + 1), out var bottom))
                    {
                        up += top.position - bottom.position;
                    }
                }
            }

            for (var r = row; r < row + footprint.Height; r++)
            {
                for (var c = column; c + 1 < column + footprint.Width; c++)
                {
                    if (cells.TryGetValue(ClusterGrid.PositionAt(c, r), out var left) &&
                        cells.TryGetValue(ClusterGrid.PositionAt(c + 1, r), out var next))
                    {
                        right += next.position - left.position;
                    }
                }
            }

            // The segments set the axes exactly - tilted into the surface as much as the cells
            // are - and the bones only say which side faces out. Right as seen from the front is
            // the seat's -X (its +X is the wearer's right); Unity's axes: Z = X x Y, Y = Z x X.
            var hasUp = up.sqrMagnitude > 1e-8f;
            var hasRight = right.sqrMagnitude > 1e-8f;
            if (hasUp)
            {
                var y = up.normalized;
                var facing = normal;
                if (hasRight)
                {
                    // a 2x2: the plane of the cells, unless the cells are laid out the wrong way
                    // round and it faces into the rig
                    var x = Vector3.ProjectOnPlane(-right, y);
                    var plane = Vector3.Cross(x, y);
                    if (x.sqrMagnitude > 1e-8f && Vector3.Dot(plane, normal) > 0f)
                    {
                        facing = plane;
                    }
                }

                var z = Vector3.ProjectOnPlane(facing, y);
                return z.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(z.normalized, y) : (Quaternion?)null;
            }

            if (hasRight)
            {
                var x = -right.normalized;
                var z = Vector3.ProjectOnPlane(normal, x);
                return z.sqrMagnitude > 1e-8f
                    ? Quaternion.LookRotation(z.normalized, Vector3.Cross(z.normalized, x))
                    : (Quaternion?)null;
            }

            return null;
        }

        /// <summary>
        /// The pouch's entry in mounts.json if it has one (what the editor sets), else the model's
        /// own ModPlacer (what its bundle ships with), else the game's default.
        /// </summary>
        public static void GetMount(Item pouch, Transform view, out Vector3 position, out Quaternion rotation)
        {
            if (Plugin.Mounts != null && Plugin.Mounts.TryGet(pouch.StringTemplateId, out var stored))
            {
                position = new Vector3(stored.Position[0], stored.Position[1], stored.Position[2]);
                rotation = Quaternion.Euler(stored.Rotation[0], stored.Rotation[1], stored.Rotation[2]);
                return;
            }

            var placer = view != null ? view.GetComponent<ModPlacer>() : null;
            if (placer != null)
            {
                position = placer.ModPosition;
                rotation = Quaternion.Euler(placer.ModRotation);
                return;
            }

            var mount = MountPose.Default();
            position = new Vector3(mount.Position[0], mount.Position[1], mount.Position[2]);
            rotation = Quaternion.Euler(mount.Rotation[0], mount.Rotation[1], mount.Rotation[2]);
        }

        /// <summary>The pouch model hung on a cell bone: its child that came from the asset pool.</summary>
        public static Transform ModelOn(Transform bone)
        {
            for (var i = 0; i < bone.childCount; i++)
            {
                var child = bone.GetChild(i);
                if (child.GetComponent<AssetPoolObject>() != null)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>The rig model a cell bone belongs to.</summary>
        public static Transform RootOf(Transform bone)
        {
            var marker = bone.GetComponent<PouchBoneMarker>();
            if (marker != null && marker.Root != null)
            {
                return marker.Root;
            }

            var pooled = bone.parent != null ? bone.parent.GetComponentInParent<AssetPoolObject>() : null;
            return pooled != null ? pooled.transform : bone.parent;
        }

        /// <summary>Mean of a few nearby rotations: sign-aligned sum, normalised.</summary>
        public static Quaternion Average(IList<Quaternion> rotations)
        {
            if (rotations.Count == 0)
            {
                return Quaternion.identity;
            }

            var first = rotations[0];
            var sum = Vector4.zero;
            foreach (var q in rotations)
            {
                var sign = Quaternion.Dot(first, q) < 0f ? -1f : 1f;
                sum += new Vector4(q.x, q.y, q.z, q.w) * sign;
            }

            var length = sum.magnitude;
            return length < 1e-6f
                ? first
                : new Quaternion(sum.x / length, sum.y / length, sum.z / length, sum.w / length);
        }
    }
}

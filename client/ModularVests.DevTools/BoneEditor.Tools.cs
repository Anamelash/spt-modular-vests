using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using ModularVests.Client;
using ModularVests.Client.Bones;
using ModularVests.DevTools.Geometry;
using UnityEngine;

namespace ModularVests.DevTools
{
    // The layout tools: surface snap, mirror, cluster arrangement, projection onto the surface,
    // pouch mounts and the layout diagnostics.
    internal sealed partial class BoneEditor
    {
        /// <summary>Reach of the "Project" buttons: bones may stand well off the surface.</summary>
        private const float ProjectLift = 0.3f;

        /// <summary>A cell and its mirror may differ this much before it is reported.</summary>
        private const float MirrorToleranceMetres = 0.005f;

        private const float MirrorToleranceDegrees = 2f;

        private static float CellWidth => DevConfig.CellWidth.Value;

        private static float CellHeight => DevConfig.CellHeight.Value;

        private static float Lift => DevConfig.SurfaceLift.Value;

        // --- surface snap ---

        private MeshSurface _surface;
        private string _snapState = "";

        /// <summary>The surface of the rig model, built on first use.</summary>
        private MeshSurface Surface()
        {
            if (_surface == null && ModelRoot != null)
            {
                _surface = MeshSurface.ForLoot(ModelRoot);
                Trace("surface", _surface.Usable
                    ? $"surface of the rig: {_surface.Triangles} triangles"
                    : $"no surface for the rig: {_surface.Error}");
            }

            return _surface;
        }

        private void InvalidateSurface()
        {
            _surface = null;
            _snapState = "";
        }

        /// <summary>The snap is on and there is a surface to snap to (never for pouch mounts).</summary>
        private bool SnapActive => DevConfig.SurfaceSnap.Value && !_mountMode && Surface()?.Usable == true;

        private void ToggleSnap()
        {
            DevConfig.SurfaceSnap.Value = !DevConfig.SurfaceSnap.Value;
            _snapState = "";

            // switching it on or off moves nothing
            _status = DevConfig.SurfaceSnap.Value ? "surface snap on" : "surface snap off";
        }

        private string SnapLabel()
        {
            if (!DevConfig.SurfaceSnap.Value)
            {
                return "SNAP OFF";
            }

            if (_mountMode)
            {
                return "SNAP ON (not for pouch mounts)";
            }

            var surface = Surface();
            if (surface == null || !surface.Usable)
            {
                return "SNAP UNAVAILABLE: " + (surface?.Error ?? "no model");
            }

            return string.IsNullOrEmpty(_snapState) ? "SNAP ON" : "SNAP ON · " + _snapState;
        }

        /// <summary>
        /// Stands a bone on a surface hit: at the point (plus the surface offset), facing along
        /// the normal of the whole cell, its roll against the rig's up kept.
        /// </summary>
        private void PlaceOnSurface(Transform bone, SurfaceHit hit)
        {
            var surface = Surface();
            var normal = SurfaceMath.CellNormal(surface, hit, MeshSurface.P(bone.right), MeshSurface.P(bone.up),
                CellWidth / 2f, CellHeight / 2f, Lift);
            ViewFrame(out _, out _, out var up);
            var frame = SurfaceMath.FrameOnSurface(normal, MeshSurface.P(up),
                new CellFrame { Forward = MeshSurface.P(bone.forward), Up = MeshSurface.P(bone.up) });

            bone.position = MeshSurface.V(hit.Point) + MeshSurface.V(normal) * DevConfig.SurfaceOffset.Value;
            bone.rotation = Quaternion.LookRotation(MeshSurface.V(frame.Forward), MeshSurface.V(frame.Up));
        }

        /// <summary>Puts a bone back onto the surface along its own normal; false (and no move) on a miss.</summary>
        private bool ReprojectBone(Transform bone, float lift)
        {
            var surface = Surface();
            if (surface == null || !surface.Usable ||
                !SurfaceMath.Reproject(surface, MeshSurface.P(bone.position), MeshSurface.P(bone.forward), lift,
                    out var hit))
            {
                return false;
            }

            PlaceOnSurface(bone, hit);
            return true;
        }

        /// <summary>"Project": puts cells onto the surface, whatever the snap switch says.</summary>
        private void Project(IEnumerable<Slot> slots)
        {
            var surface = Surface();
            if (surface == null || !surface.Usable)
            {
                _status = "no surface to project onto: " + (surface?.Error ?? "no model");
                return;
            }

            var done = 0;
            var missed = new List<string>();
            foreach (var slot in slots)
            {
                var bone = BoneOf(slot);
                if (bone == null)
                {
                    continue;
                }

                if (ReprojectBone(bone, ProjectLift))
                {
                    StorePose(slot, bone);
                    done++;
                }
                else
                {
                    missed.Add(ShortLabel(slot));
                }
            }

            Reseat();
            Refresh();
            _status = $"projected {done}" + (missed.Count > 0 ? ", missed " + string.Join(", ", missed.ToArray()) : "");
        }

        // --- the rig's frame ---

        /// <summary>
        /// The sagittal plane of the rig (a point on it and the wearer's right as its normal) and
        /// the rig's up, all in world space: through the anchor, in the directions of the model.
        /// </summary>
        private void ViewFrame(out Vector3 planePoint, out Vector3 right, out Vector3 up)
        {
            var root = ModelRoot;
            var frame = PouchBones.GetFrame(_rig, root);
            right = root.TransformDirection(frame.Right).normalized;
            up = root.TransformDirection(frame.Up).normalized;
            planePoint = root.TransformPoint(frame.Origin);
        }

        // --- mirror ---

        private Slot MirrorOf(Slot slot)
        {
            return slot != null && ClusterGrid.TryParse(slot.ID, out var cluster, out var position)
                ? CellSlot(ClusterGrid.MirrorCluster(cluster), ClusterGrid.MirrorPosition(position))
                : null;
        }

        /// <summary>
        /// Puts a cell's mirror image onto its cell in the paired cluster (and onto the surface
        /// there, with the snap on). False when there is no pair; <paramref name="offSurface"/>
        /// tells a mirror that found no surface on its side.
        /// </summary>
        private bool MirrorCell(Slot slot, out bool offSurface)
        {
            offSurface = false;
            var pair = MirrorOf(slot);
            var source = BoneOf(slot);
            var target = BoneOf(pair);
            if (source == null || target == null)
            {
                return false;
            }

            ViewFrame(out var planePoint, out var right, out _);
            var normal = MeshSurface.P(right);
            var position = MirrorMath.ReflectPoint(MeshSurface.P(source.position), MeshSurface.P(planePoint), normal);
            var r = source.rotation;
            var rotation = MirrorMath.ReflectRotation(new Quat(r.x, r.y, r.z, r.w), normal);
            target.position = MeshSurface.V(position);
            target.rotation = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);

            // the mesh is not perfectly symmetric: the mirror image is put back onto its own side
            if (SnapActive && !ReprojectBone(target, Lift))
            {
                offSurface = true;
            }

            StorePose(pair, target);
            return true;
        }

        private void MirrorCell(Slot slot)
        {
            if (MirrorCell(slot, out var offSurface) && offSurface)
            {
                _status = $"mirror {ShortLabel(MirrorOf(slot))} found no surface on its side, left as mirrored";
            }
        }

        /// <summary>"Mirror cluster": every cell of the selected cluster onto the paired one.</summary>
        private void MirrorCluster()
        {
            var done = 0;
            var offSurface = new List<string>();
            for (var position = 1; position <= ClusterGrid.CellsPerCluster; position++)
            {
                var slot = CellSlot(_cluster, position);
                if (MirrorCell(slot, out var off))
                {
                    done++;
                    if (off)
                    {
                        offSurface.Add(ShortLabel(MirrorOf(slot)));
                    }
                }
            }

            Reseat();
            Refresh();
            var pair = ClusterGrid.MirrorCluster(_cluster);
            _status = done == 0
                ? $"C{_cluster} has no paired cluster C{pair} on this rig"
                : $"C{_cluster} mirrored onto C{pair}" + (offSurface.Count > 0
                    ? "; no surface for " + string.Join(", ", offSurface.ToArray()) + " (left as mirrored)"
                    : "");
        }

        // --- arrange ---

        /// <summary>
        /// "Arrange cluster": cells 2-4 laid out from cell 1, a cell's size apart in cell 1's own
        /// axes (right as seen from the front, and down). With the snap on the steps are taken on
        /// the surface: each cell is put back on it along cell 1's normal and faces its own.
        /// </summary>
        private void ArrangeCluster()
        {
            var first = CellBone(_cluster, 1);
            if (first == null)
            {
                _status = $"C{_cluster} has no cell 1";
                return;
            }

            var right = first.rotation * MeshSurface.V(CellInfo.GrowRight);
            var down = first.rotation * MeshSurface.V(CellInfo.GrowDown);
            var missed = new List<string>();
            for (var position = 2; position <= ClusterGrid.CellsPerCluster; position++)
            {
                var slot = CellSlot(_cluster, position);
                var bone = BoneOf(slot);
                if (bone == null)
                {
                    continue;
                }

                bone.position = first.position + right * (ClusterGrid.ColumnOf(position) * CellWidth) +
                                down * (ClusterGrid.RowOf(position) * CellHeight);
                bone.rotation = first.rotation;
                if (SnapActive)
                {
                    if (SurfaceMath.Reproject(Surface(), MeshSurface.P(bone.position), MeshSurface.P(first.forward),
                            Lift, out var hit))
                    {
                        PlaceOnSurface(bone, hit);
                    }
                    else
                    {
                        missed.Add(ShortLabel(slot));
                    }
                }

                StorePose(slot, bone);
                if (DevConfig.LiveMirror.Value)
                {
                    MirrorCell(slot, out _);
                }
            }

            Reseat();
            Refresh();
            _status = $"C{_cluster} arranged from cell 1" +
                      (missed.Count > 0 ? "; no surface for " + string.Join(", ", missed.ToArray()) : "");
        }

        // --- symmetry of the mirror pairs ---

        /// <summary>A pair may move this little before "Arrange pairs" counts the layout as symmetric already.</summary>
        private const float SymmetricMetres = 0.0001f;

        private const float SymmetricDegrees = 0.05f;

        /// <summary>The layout before the last "Arrange pairs", next to bones.json (never shipped).</summary>
        private static string BackupPath =>
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Plugin.Bones.Path) ?? ".", "bones.backup.json");

        /// <summary>
        /// "Arrange pairs": every cell and its mirror (clusters 1-2, 3-4) become exact mirror images
        /// about the middle of the layout - on each axis the mean of the two, measured from the middle
        /// (<see cref="MirrorMath.AveragePair"/>). The layout of this rig is backed up first, unless it
        /// is symmetric already (a second press must not overwrite the hand-made layout with the
        /// arranged one).
        /// </summary>
        private void ArrangePairs()
        {
            var left = new List<Slot>();
            var right = new List<Slot>();
            foreach (var slot in _slots)
            {
                var pair = MirrorOf(slot);
                if (ClusterGrid.TryParse(slot.ID, out var cluster, out _) && cluster % 2 == 1 &&
                    BoneOf(slot) != null && BoneOf(pair) != null)
                {
                    left.Add(slot);
                    right.Add(pair);
                }
            }

            if (left.Count == 0)
            {
                _status = "no mirror pairs on this rig";
                return;
            }

            ViewFrame(out var origin, out var rightAxis, out _);
            var normal = MeshSurface.P(rightAxis);
            var aPositions = left.ConvertAll(s => MeshSurface.P(BoneOf(s).position));
            var bPositions = right.ConvertAll(s => MeshSurface.P(BoneOf(s).position));
            var plane = MirrorMath.SymmetryPlane(aPositions, bPositions, MeshSurface.P(origin), normal);

            var arranged = new List<KeyValuePair<Transform, Pose>>();
            var moved = 0f;
            var turned = 0f;
            for (var i = 0; i < left.Count; i++)
            {
                var a = BoneOf(left[i]);
                var b = BoneOf(right[i]);
                var aPosition = aPositions[i];
                var bPosition = bPositions[i];
                var aRotation = Q(a.rotation);
                var bRotation = Q(b.rotation);
                MirrorMath.AveragePair(ref aPosition, ref aRotation, ref bPosition, ref bRotation, plane, normal);

                foreach (var (bone, position, rotation) in new[] { (a, aPosition, aRotation), (b, bPosition, bRotation) })
                {
                    var pose = new Pose(MeshSurface.V(position), new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W));
                    moved = Mathf.Max(moved, Vector3.Distance(bone.position, pose.position));
                    turned = Mathf.Max(turned, Quaternion.Angle(bone.rotation, pose.rotation));
                    arranged.Add(new KeyValuePair<Transform, Pose>(bone, pose));
                }
            }

            if (moved < SymmetricMetres && turned < SymmetricDegrees)
            {
                _status = $"{left.Count} mirror pairs are symmetric already; the backup is left as it was";
                return;
            }

            var tpl = _rig.StringTemplateId;
            try
            {
                var backup = new BoneStore(BackupPath, _ => { });
                backup.Load();
                backup.SetRig(tpl, Plugin.Bones.GetRig(tpl));
                backup.Save();
            }
            catch (System.Exception ex)
            {
                _status = "BACKUP FAILED, nothing arranged: " + ex.Message;
                DevPlugin.Log.LogError($"[ModularVests.DevTools] layout backup failed: {ex}");
                return;
            }

            foreach (var entry in arranged)
            {
                entry.Key.SetPositionAndRotation(entry.Value.position, entry.Value.rotation);
            }

            foreach (var slot in left.Concat(right))
            {
                StorePose(slot, BoneOf(slot));
            }

            Reseat();
            Refresh();
            var middle = Vector3.Dot(MeshSurface.V(plane) - origin, rightAxis);
            _status = $"{left.Count} mirror pairs arranged about the middle ({middle * 1000f:0} mm from the anchor " +
                      $"to the wearer's right); moved up to {moved * 1000f:0.#} mm, {turned:0.#}°; " +
                      "the layout before is in bones.backup.json (Restore)";
            DevPlugin.Log.LogInfo($"[ModularVests.DevTools] {tpl}: mirror pairs arranged, previous layout backed up to {BackupPath}");
        }

        /// <summary>"Restore": this rig's layout from before the last "Arrange pairs".</summary>
        private void RestoreBackup()
        {
            var tpl = _rig.StringTemplateId;
            Dictionary<string, BonePose> poses;
            try
            {
                if (!System.IO.File.Exists(BackupPath))
                {
                    _status = "no backup yet: Arrange pairs makes one";
                    return;
                }

                var backup = new BoneStore(BackupPath, _ => { });
                backup.Load();
                poses = backup.GetRig(tpl);
            }
            catch (System.Exception ex)
            {
                _status = "RESTORE FAILED: " + ex.Message;
                DevPlugin.Log.LogError($"[ModularVests.DevTools] layout restore failed: {ex}");
                return;
            }

            if (poses.Count == 0)
            {
                _status = "the backup has nothing for this rig";
                return;
            }

            Plugin.Bones.SetRig(tpl, poses);
            ReapplyPoses();

            Reseat();
            Refresh();
            _status = "layout restored from bones.backup.json (not saved yet)";
        }

        private static Quat Q(Quaternion q) => new Quat(q.x, q.y, q.z, q.w);

        // --- pouch mounts ---

        private void ToggleMountMode()
        {
            if (_dragging)
            {
                return;
            }

            _mountMode = !_mountMode;
            _status = _mountMode
                ? "pouch mount: the gizmo moves the pouch of the selected cell on its seat"
                : "cells";
        }

        /// <summary>
        /// Stores how the pouch model stands on its seat, for its template: the same mount is used
        /// on every cell (the seat moves with the cells, the mount does not). It overrides the
        /// model's own ModPlacer; a reset goes back to it.
        /// </summary>
        private void StoreMount(Slot slot, Transform view)
        {
            var pouch = slot?.ContainedItem;
            var bone = BoneOf(slot);
            if (pouch == null || bone == null || view == null || Plugin.Mounts == null)
            {
                return;
            }

            if (!PouchSeat.Seat(slot, pouch, bone, ModelRoot, out var seatPosition, out var seatRotation))
            {
                return;
            }

            var inverse = Quaternion.Inverse(seatRotation);
            var position = inverse * (view.localPosition - seatPosition);
            var rotation = (inverse * view.localRotation).eulerAngles;
            Plugin.Mounts.Set(pouch.StringTemplateId, new MountPose
            {
                Position = new[] { Round(position.x), Round(position.y), Round(position.z) },
                Rotation = new[] { Round(Angle(rotation.x)), Round(Angle(rotation.y)), Round(Angle(rotation.z)) },
            });
        }

        private void ResetMount(Slot slot)
        {
            var pouch = slot?.ContainedItem;
            if (pouch == null || Plugin.Mounts == null)
            {
                _status = $"{Label(slot)} has no pouch";
                return;
            }

            Plugin.Mounts.Remove(pouch.StringTemplateId);
            Reseat();
            Refresh();
            _status = $"{pouch.ShortName.Localized()}: mount reset to the default";
        }

        private static float Round(float value) => Mathf.Round(value * 10000f) / 10000f;

        private static float Angle(float degrees) => degrees > 180f ? degrees - 360f : degrees;

        // --- diagnostics ---

        private const float DiagnosticsInterval = 0.3f;

        private readonly List<string> _warnings = new List<string>();
        private float _nextDiagnostics;

        /// <summary>
        /// What looks wrong with the layout, a few times a second: cells in the wrong order,
        /// cells nobody has placed yet, a cluster on the wrong side of the rig, and (without the
        /// live mirror) cells that differ from their mirror.
        /// </summary>
        private void UpdateDiagnostics()
        {
            if (Time.unscaledTime < _nextDiagnostics || ModelRoot == null)
            {
                return;
            }

            _nextDiagnostics = Time.unscaledTime + DiagnosticsInterval;
            _warnings.Clear();
            ViewFrame(out var planePoint, out var right, out _);
            var tpl = _rig.StringTemplateId;

            foreach (var cluster in _clusters)
            {
                var bones = new Transform[ClusterGrid.CellsPerCluster + 1];
                var unplaced = new List<int>();
                var centre = Vector3.zero;
                var count = 0;
                for (var position = 1; position <= ClusterGrid.CellsPerCluster; position++)
                {
                    var slot = CellSlot(cluster, position);
                    bones[position] = BoneOf(slot);
                    if (slot == null || bones[position] == null)
                    {
                        continue;
                    }

                    centre += bones[position].position;
                    count++;
                    if (!Plugin.Bones.TryGet(tpl, slot.ID, out _))
                    {
                        unplaced.Add(position);
                    }
                }

                if (bones[1] != null)
                {
                    _warnings.AddRange(CellInfo.OrderWarnings(cluster, MeshSurface.P(bones[1].position),
                        MeshSurface.P(bones[1].right), MeshSurface.P(bones[1].up),
                        bones[2] != null ? MeshSurface.P(bones[2].position) : (Vec3?)null,
                        bones[3] != null ? MeshSurface.P(bones[3].position) : (Vec3?)null));
                }

                if (unplaced.Count > 0)
                {
                    _warnings.Add($"C{cluster}: cell(s) {string.Join(", ", unplaced.ConvertAll(p => p.ToString()).ToArray())} " +
                                  "still in the default pose");
                }

                if (count > 0)
                {
                    var side = Vector3.Dot(centre / count - planePoint, right);
                    var left = cluster % 2 == 1;
                    if (left ? side > 0f : side < 0f)
                    {
                        _warnings.Add($"C{cluster} ({CellInfo.ClusterName(cluster)}) is on the wearer's " +
                                      $"{(left ? "right" : "left")} side");
                    }
                }

                if (!DevConfig.LiveMirror.Value && cluster % 2 == 1)
                {
                    MirrorWarnings(cluster, planePoint, right);
                }
            }
        }

        private void MirrorWarnings(int cluster, Vector3 planePoint, Vector3 right)
        {
            var normal = MeshSurface.P(right);
            for (var position = 1; position <= ClusterGrid.CellsPerCluster; position++)
            {
                var slot = CellSlot(cluster, position);
                var a = BoneOf(slot);
                var b = BoneOf(MirrorOf(slot));
                if (a == null || b == null)
                {
                    continue;
                }

                var mirrored = MeshSurface.V(MirrorMath.ReflectPoint(MeshSurface.P(a.position), MeshSurface.P(planePoint),
                    normal));
                var r = a.rotation;
                var q = MirrorMath.ReflectRotation(new Quat(r.x, r.y, r.z, r.w), normal);
                var distance = Vector3.Distance(mirrored, b.position);
                var angle = Quaternion.Angle(new Quaternion(q.X, q.Y, q.Z, q.W), b.rotation);
                if (distance > MirrorToleranceMetres || angle > MirrorToleranceDegrees)
                {
                    _warnings.Add($"{ShortLabel(slot)} and its mirror {ShortLabel(MirrorOf(slot))} differ by " +
                                  $"{distance * 1000f:0} mm, {angle:0.#}°");
                }
            }
        }

        private static string ShortLabel(Slot slot) =>
            slot != null && ClusterGrid.TryParse(slot.ID, out var c, out var p) ? CellInfo.Label(c, p) : slot?.ID ?? "?";
    }
}

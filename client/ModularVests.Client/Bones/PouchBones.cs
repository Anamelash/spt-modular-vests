using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace ModularVests.Client.Bones
{
    /// <summary>
    /// Marks a bone the mod created, and remembers what it hangs from: the chest plate of the
    /// model (an anchored bone) or the model root.
    /// </summary>
    internal sealed class PouchBoneMarker : MonoBehaviour
    {
        public Transform Root;
        public bool Anchored;
    }

    /// <summary>
    /// Where the default layout sits on a rig model, in the model root's local space: the
    /// front surface of the chest plate, with the rig's right, up and outward directions.
    /// </summary>
    internal sealed class AnchorFrame
    {
        public Vector3 Origin;
        public Vector3 Right;
        public Vector3 Up;
        public Vector3 Forward;
        public string Source;

        public Vector3 ToLocal(float x, float y, float z) => Origin + Right * x + Up * y + Forward * z;

        public Quaternion Rotation => Quaternion.LookRotation(Forward, Up);
    }

    /// <summary>
    /// The rig models have no bones for pouch slots, so the mod adds empty transforms named
    /// after the slots. The game then finds them like any other bone: slot icons and lines on
    /// the Modding screen, and the pouch model hung on the bone in the preview and on the body.
    ///
    /// The bones hang from the model's chest plate transform when it has one, not from the
    /// model root, and their poses are stored in that transform's space. The root of the same
    /// item is not the same thing in every context (preview vs. the model worn on a body), and
    /// a bone measured from it lands somewhere else on the character; the plate is the same
    /// piece of the rig everywhere, and on a body it moves with the torso.
    /// </summary>
    internal static class PouchBones
    {
        /// <summary>
        /// Directions of an armor/rig model in its own space. The Modding screen turns such a
        /// model by (-90, 180, 0) to face the camera, so its front is local -Y and its top is
        /// local +Z.
        /// </summary>
        internal static readonly Vector3 ModelForward = new Vector3(0f, -1f, 0f);
        internal static readonly Vector3 ModelUp = new Vector3(0f, 0f, 1f);

        /// <summary>
        /// The WEARER's right, derived the way Unity derives it from up and forward (local +X),
        /// never written down by hand. It used to be the viewer's right (-X): the frame was
        /// then mirrored against the character's, and pouches on a body came out swapped left
        /// for right and turned inward.
        /// </summary>
        private static readonly Vector3 ModelRight = Vector3.Cross(ModelUp, ModelForward);

        /// <summary>How far the layout stands off the front surface, metres.</summary>
        private const float SurfaceOffset = 0.02f;

        /// <summary>Chest height as a fraction of the model's height above its centre, when no plate is found.</summary>
        private const float ChestHeightFraction = 0.15f;

        private static readonly Dictionary<int, AnchorFrame> Frames = new Dictionary<int, AnchorFrame>();
        private static readonly Dictionary<int, Transform> Anchors = new Dictionary<int, Transform>();
        private static readonly HashSet<string> Logged = new HashSet<string>();


        public static void Ensure(CompoundItem rig, Transform root)
        {
            if (rig == null || root == null || Plugin.Bones == null)
            {
                return;
            }

            List<Slot> slots = PouchSlots.Of(rig);
            if (slots.Count == 0)
            {
                return;
            }


            var tpl = rig.StringTemplateId;
            var anchor = GetAnchor(rig, root);
            var parent = anchor != null ? anchor : root;

            for (var i = 0; i < slots.Count; i++)
            {
                var id = slots[i].ID;
                var bone = TransformTools.FindTransformRecursive(root, id, ignoreCase: true);
                PouchBoneMarker marker;
                if (bone == null)
                {
                    var go = new GameObject(id);
                    marker = go.AddComponent<PouchBoneMarker>();
                    bone = go.transform;
                }
                else
                {
                    marker = bone.GetComponent<PouchBoneMarker>();
                    if (marker == null)
                    {
                        // a model that brings its own bone for this slot wins
                        continue;
                    }
                }

                if (bone.parent != parent)
                {
                    bone.SetParent(parent, worldPositionStays: false);
                }

                marker.Root = root;
                marker.Anchored = anchor != null;

                // this model is being posed in its own space again (it may have come back from
                // the pool after a turn on a character)
                PouchBoneFollower.Clear(bone);

                // Models are pooled and come back with the bones of a previous life: the pose
                // is re-applied every time so an edited layout is what the next build shows.
                if (Plugin.Bones.TryGet(tpl, id, out var pose))
                {
                    ApplyPose(bone, pose, root);
                }
                else
                {
                    ApplyDefault(bone, rig);
                }
            }

            LogContext(rig, root, anchor, slots);
        }

        /// <summary>Puts a bone where the default layout has it: on the chest plate.</summary>
        public static void ApplyDefault(Transform bone, CompoundItem rig)
        {
            var marker = bone.GetComponent<PouchBoneMarker>();
            var root = marker != null && marker.Root != null ? marker.Root : bone.parent;
            var frame = GetFrame(rig, root);
            // the bone is named after its slot; anything else stands on the anchor
            var grid = BoneStore.DefaultFor(bone.name)?.Position ?? new[] { 0f, 0f, 0f };

            // the default layout is described in the model's own space; put it where the bone
            // hangs from, which is the chest plate unless the model has none
            var inRoot = frame.ToLocal(grid[0], grid[1], grid[2]);
            bone.localPosition = ToParent(bone, root, inRoot);
            bone.localRotation = ToParent(bone, root, frame.Rotation);
            bone.localScale = Vector3.one;
        }

        /// <summary>
        /// A bone's pose in the rig's frame (<see cref="GetFrame"/>): metres along the rig's right,
        /// up and forward from the frame's origin on its chest, and its turn against the frame. The
        /// same numbers mean the same spot on any rig, with a chest plate or without one - which is
        /// how a layout moves from one model to another.
        /// </summary>
        public static void ReadInFrame(Transform bone, CompoundItem rig, Transform root, out Vector3 offset,
            out Quaternion turn)
        {
            var frame = GetFrame(rig, root);
            var fromOrigin = root.InverseTransformPoint(bone.position) - frame.Origin;
            offset = new Vector3(Vector3.Dot(fromOrigin, frame.Right), Vector3.Dot(fromOrigin, frame.Up),
                Vector3.Dot(fromOrigin, frame.Forward));
            turn = Quaternion.Inverse(frame.Rotation) * (Quaternion.Inverse(root.rotation) * bone.rotation);
        }

        /// <summary>Puts a bone at a pose read by <see cref="ReadInFrame"/>, in this rig's frame.</summary>
        public static void PlaceInFrame(Transform bone, CompoundItem rig, Transform root, Vector3 offset, Quaternion turn)
        {
            var frame = GetFrame(rig, root);
            bone.localPosition = ToParent(bone, root, frame.ToLocal(offset.x, offset.y, offset.z));
            bone.localRotation = ToParent(bone, root, frame.Rotation * turn);
            bone.localScale = Vector3.one;
        }

        /// <summary>
        /// Applies a stored pose. Poses written before the bones moved onto the chest plate are
        /// in the model root's space and are converted here, so an existing layout keeps its place.
        /// </summary>
        public static void ApplyPose(Transform bone, BonePose pose, Transform root)
        {
            var position = new Vector3(pose.Position[0], pose.Position[1], pose.Position[2]);
            var rotation = Quaternion.Euler(pose.Rotation[0], pose.Rotation[1], pose.Rotation[2]);
            var marker = bone.GetComponent<PouchBoneMarker>();
            var anchored = marker != null && marker.Anchored;

            if (pose.IsAnchored == anchored)
            {
                bone.localPosition = position;
                bone.localRotation = rotation;
            }
            else if (anchored)
            {
                // stored against the root, now hanging from the plate
                bone.localPosition = ToParent(bone, root, position);
                bone.localRotation = ToParent(bone, root, rotation);
            }
            else
            {
                // stored against the plate, but this model has no plate to hang from
                bone.localPosition = position;
                bone.localRotation = rotation;
            }

            bone.localScale = Vector3.one;
        }

        public static BonePose ReadPose(Transform bone)
        {
            var p = bone.localPosition;
            var r = bone.localEulerAngles;
            var marker = bone.GetComponent<PouchBoneMarker>();
            return new BonePose
            {
                Position = new[] { Round(p.x), Round(p.y), Round(p.z) },
                Rotation = new[] { Round(Normalize(r.x)), Round(Normalize(r.y)), Round(Normalize(r.z)) },
                Space = marker != null && marker.Anchored ? BonePose.AnchorSpace : BonePose.RootSpace,
            };
        }

        private static Vector3 ToParent(Transform bone, Transform root, Vector3 inRoot) =>
            bone.parent == root || bone.parent == null
                ? inRoot
                : bone.parent.InverseTransformPoint(root.TransformPoint(inRoot));

        private static Quaternion ToParent(Transform bone, Transform root, Quaternion inRoot) =>
            bone.parent == root || bone.parent == null
                ? inRoot
                : Quaternion.Inverse(bone.parent.rotation) * root.rotation * inRoot;

        // --- the model worn on a character ---
        //
        // The item's own (loot) model and the model worn on a character (the dress) are the same
        // geometry in two spaces: the loot model stands along its root's +Z facing -Y, a worn
        // (skinned) mesh is built along +Y facing +Z and may carry an import scale. A cell laid out
        // on the loot model is carried over exactly, through where a worn mesh stands against the
        // loot model (WornMeshInRoot): the layout is made once, on the loot model.
        //
        // A rig may be worn as several skinned meshes (the IOTV: torso, collar, shoulder pads,
        // groin flap), each skinned to its own bones of the skeleton. A cluster follows the
        // skeleton bone nearest to its middle among the bones of all of them. One mesh alone,
        // picked by its size, gave the shoulder pads' neck bone to the chest clusters: small
        // differences between the bind pose and the pose of the moment then swung them up to the
        // head.

        /// <summary>
        /// Where a worn mesh stands against the loot model, in the root's space: at the skinned
        /// mesh object's place, either as the object stands or stood up the way the loot model
        /// stands (built along the root's +Y facing +Z, where the loot model is along +Z facing -Y).
        /// Which of the two is right depends on the model: the IOTV Full Protection and Assault
        /// objects are turned 90° against the root and want the stand-up, the High Mobility's groin
        /// flap stands as the loot model already - stood up, its pouches lay flat on the floor.
        /// <see cref="WornSkins"/> takes the one that lies on the loot model.
        /// </summary>
        public static Matrix4x4 WornMeshInRoot(Transform root, SkinnedMeshRenderer skin, bool standUp,
            out Quaternion rotation)
        {
            var turn = standUp ? Quaternion.LookRotation(ModelForward, ModelUp) : Quaternion.identity;
            var mesh = skin.transform;
            rotation = turn * (Quaternion.Inverse(root.rotation) * mesh.rotation);
            return Matrix4x4.TRS(turn * root.InverseTransformPoint(mesh.position), rotation,
                Divide(mesh.lossyScale, root.lossyScale));
        }

        private static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(
            b.x != 0f ? a.x / b.x : 1f, b.y != 0f ? a.y / b.y : 1f, b.z != 0f ? a.z / b.z : 1f);

        /// <summary>A skinned mesh of the rig, bound to the character's skeleton.</summary>
        private sealed class WornSkin
        {
            public SkinnedMeshRenderer Skin;
            public Transform[] Bones;
            public Matrix4x4[] Bindposes;

            /// <summary>Mesh space to the root's space, and back.</summary>
            public Matrix4x4 ToRoot;
            public Matrix4x4 FromRoot;
            public Quaternion Rotation;

            /// <summary>Its box in the root's space, as WornMeshInRoot stands it.</summary>
            public Bounds Box;

            /// <summary>The loot mesh it lies on (null: none), how much they share and how far their corners are apart.</summary>
            public string Match;
            public float Overlap;
            public float Off;

            /// <summary>Placed stood up (true) or as its object stands.</summary>
            public bool StoodUp;

            public bool Shown => Skin.gameObject.activeInHierarchy;

            public string Name => Skin.sharedMesh.name;
        }

        /// <summary>A bone of the skeleton, through the skinned mesh that knows its bind pose.</summary>
        private struct SkeletonBone
        {
            public WornSkin Skin;
            public int Index;
            public float Distance;

            public Transform Transform => Skin.Bones[Index];
        }

        /// <summary>Least overlap (intersection over union) of a worn mesh's box with its loot counterpart.</summary>
        private const float MatchOverlap = 0.9f;

        /// <summary>How far the corners of the two boxes may be apart, as a fraction of the size, plus a slack in metres.</summary>
        private const float MatchTolerance = 0.03f;

        private const float MatchSlack = 0.005f;

        /// <summary>
        /// The worn meshes a cell may be carried over through. The rig's own skinned meshes that the
        /// game has skinned onto the skeleton (not the plates or pouches hung on it: their own pool
        /// objects) - but only those that, stood up by <see cref="WornMeshInRoot"/>, lie on a mesh
        /// of the loot model. A worn mesh whose object is turned otherwise (a part the kit does not
        /// use) would lay the whole layout flat: the pouches of the IOTV High Mobility lay on the
        /// floor, carried over through its groin flap. The shown ones first; with none lying on the
        /// loot model, the largest one, with a warning. Everything is written to the log once per rig.
        /// </summary>
        private static List<WornSkin> WornSkins(Transform root, string tpl)
        {
            var own = root.GetComponent<EFT.AssetsManager.AssetPoolObject>();
            var report = new List<string>();

            // the loot model's meshes (switched off on a body, still there)
            var loot = new List<KeyValuePair<string, Bounds>>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                if (filter.sharedMesh == null ||
                    (own != null && filter.GetComponentInParent<EFT.AssetsManager.AssetPoolObject>(true) != own))
                {
                    continue;
                }

                var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                if (Encapsulate(root, filter.transform, filter.sharedMesh, ref min, ref max))
                {
                    var box = new Bounds((min + max) / 2f, max - min);
                    loot.Add(new KeyValuePair<string, Bounds>(filter.sharedMesh.name, box));
                    report.Add($"loot mesh '{filter.sharedMesh.name}' (shown {filter.gameObject.activeInHierarchy}): {Box(box)}");
                }
            }

            var all = new List<WornSkin>();
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
            {
                var bones = skin.bones;
                var head = $"worn mesh '{(skin.sharedMesh != null ? skin.sharedMesh.name : "-")}' on '{skin.name}' " +
                           $"(shown {skin.gameObject.activeInHierarchy}, drawn {skin.enabled}, {bones?.Length ?? 0} bones)";
                string skipped = null;
                if (skin.sharedMesh == null)
                {
                    skipped = "no mesh";
                }
                else if (bones == null || bones.Length == 0 || bones[0] == null)
                {
                    skipped = "no first bone";
                }
                else if (bones[0].IsChildOf(root))
                {
                    skipped = "not skinned onto the skeleton";
                }
                else if (own != null && skin.GetComponentInParent<EFT.AssetsManager.AssetPoolObject>(true) != own)
                {
                    skipped = "another pool object";
                }
                else if (skin.sharedMesh.bindposes.Length != bones.Length)
                {
                    skipped = "bind poses do not match the bones";
                }

                if (skipped != null)
                {
                    report.Add(head + ": left out, " + skipped);
                    continue;
                }

                // stood up first (most models), then as the object stands; the better one is kept
                var local = root.InverseTransformPoint(skin.transform.position);
                var turned = Quaternion.Angle(root.rotation, skin.transform.rotation);
                WornSkin entry = null;
                foreach (var standUp in new[] { true, false })
                {
                    var placed = Place(root, skin, bones, standUp, loot, out var tolerance);
                    report.Add(head + $", {(standUp ? "stood up" : "as it stands")}: {Box(placed.Box)}; object at " +
                               $"{V(local)}, turned {turned:0}° against the root, scale {skin.transform.lossyScale.x:0.###}; " +
                               $"best loot overlap {placed.Overlap:P0}, corners {placed.Off * 1000f:0} mm apart " +
                               $"(allowed {tolerance * 1000f:0}) -> " +
                               (placed.Match != null ? $"lies on '{placed.Match}'" : "does NOT lie on the loot model"));
                    if (entry == null || (entry.Match == null && (placed.Match != null || placed.Overlap > entry.Overlap)))
                    {
                        entry = placed;
                    }

                    if (entry.Match != null)
                    {
                        break;
                    }
                }

                all.Add(entry);
            }

            var chosen = all.FindAll(s => s.Match != null && s.Shown);
            if (chosen.Count == 0)
            {
                chosen = all.FindAll(s => s.Match != null);
            }

            var warning = false;
            if (chosen.Count == 0 && all.Count > 0)
            {
                // nothing lies on the loot model: the largest (shown) one, as before
                var pool = all.FindAll(s => s.Shown);
                if (pool.Count == 0)
                {
                    pool = all;
                }

                pool.Sort((a, b) => Volume(b.Box).CompareTo(Volume(a.Box)));
                chosen.Add(pool[0]);
                warning = true;
            }

            var names = chosen.ConvertAll(s => s.Name + (s.StoodUp ? " (stood up)" : " (as it stands)")).ToArray();
            report.Add(chosen.Count == 0
                ? "no worn mesh to carry the cells over through"
                : "carried over through " + string.Join(", ", names) +
                  (warning ? " (NONE lies on the loot model: the largest is used, pouches may stand wrong)" : ""));

            var text = $"[ModularVests] rig {tpl}: the model on a character (root space, metres):" +
                       Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", report.ToArray());
            if (Logged.Add("skins:" + text))
            {
                if (warning)
                {
                    Plugin.Log.LogWarning(text);
                }
                else
                {
                    Plugin.Log.LogInfo(text);
                }
            }

            return chosen;
        }

        /// <summary>A worn mesh placed one way against the loot model, and how well it lies on it.</summary>
        private static WornSkin Place(Transform root, SkinnedMeshRenderer skin, Transform[] bones, bool standUp,
            List<KeyValuePair<string, Bounds>> loot, out float tolerance)
        {
            var toRoot = WornMeshInRoot(root, skin, standUp, out var rotation);
            var entry = new WornSkin
            {
                Skin = skin,
                Bones = bones,
                Bindposes = skin.sharedMesh.bindposes,
                ToRoot = toRoot,
                FromRoot = toRoot.inverse,
                Rotation = rotation,
                Box = BoxInRoot(toRoot, skin.sharedMesh.bounds),
                StoodUp = standUp,
            };

            tolerance = MatchTolerance * entry.Box.size.magnitude + MatchSlack;
            foreach (var candidate in loot)
            {
                var overlap = Overlap(entry.Box, candidate.Value);
                if (overlap > entry.Overlap)
                {
                    entry.Overlap = overlap;
                    entry.Off = Mathf.Max((entry.Box.min - candidate.Value.min).magnitude,
                        (entry.Box.max - candidate.Value.max).magnitude);
                    entry.Match = entry.Overlap >= MatchOverlap && entry.Off <= tolerance ? candidate.Key : null;
                }
            }

            return entry;
        }

        private static Bounds BoxInRoot(Matrix4x4 toRoot, Bounds b)
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (var i = 0; i < 8; i++)
            {
                var corner = toRoot.MultiplyPoint3x4(new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z));
                min = Vector3.Min(min, corner);
                max = Vector3.Max(max, corner);
            }

            return new Bounds((min + max) / 2f, max - min);
        }

        /// <summary>Intersection over union of two boxes' volumes.</summary>
        private static float Overlap(Bounds a, Bounds b)
        {
            var min = Vector3.Max(a.min, b.min);
            var max = Vector3.Min(a.max, b.max);
            var size = max - min;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            {
                return 0f;
            }

            var shared = size.x * size.y * size.z;
            var union = Volume(a) + Volume(b) - shared;
            return union > 0f ? shared / union : 0f;
        }

        private static float Volume(Bounds b) => b.size.x * b.size.y * b.size.z;

        private static string V(Vector3 v) => $"{v.x:0.000}/{v.y:0.000}/{v.z:0.000}";

        private static string Box(Bounds b) => $"centre {V(b.center)} size {V(b.size)}";

        /// <summary>
        /// Bones of the torso a cluster may follow: the spine, the ribcage, the gear bones on it and
        /// the pelvis. Never the neck, the head or a limb - small differences between the bind pose
        /// and the pose of the moment swing a pouch far on such a bone.
        /// </summary>
        private static bool IsTorsoBone(string name) =>
            name.IndexOf("Spine", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Ribcage", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Gear", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Pelvis", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// The torso bone nearest to a point of the root's space, in its bind pose; any bone when the
        /// meshes know no torso bone.
        /// </summary>
        private static SkeletonBone NearestBone(List<WornSkin> skins, Vector3 point)
        {
            var best = Nearest(skins, point, torsoOnly: true);
            return best.Skin != null ? best : Nearest(skins, point, torsoOnly: false);
        }

        private static SkeletonBone Nearest(List<WornSkin> skins, Vector3 point, bool torsoOnly)
        {
            var best = new SkeletonBone();
            var bestDistance = float.MaxValue;
            foreach (var skin in skins)
            {
                for (var i = 0; i < skin.Bindposes.Length; i++)
                {
                    if (skin.Bones[i] == null || (torsoOnly && !IsTorsoBone(skin.Bones[i].name)))
                    {
                        continue;
                    }

                    var inMesh = skin.Bindposes[i].inverse.MultiplyPoint3x4(Vector3.zero);
                    var distance = (skin.ToRoot.MultiplyPoint3x4(inMesh) - point).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        best = new SkeletonBone { Skin = skin, Index = i, Distance = Mathf.Sqrt(distance) };
                        bestDistance = distance;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Ties the pouch bones of a rig worn on a body to the skeleton it is skinned to.
        ///
        /// On a body the model is not hung on a bone: its meshes are re-bound to the player's
        /// skeleton (Dress.Skin) while the prefab's transforms stay put. A skinned mesh carries
        /// exactly the matrix that maps its own space into a bone's space at bind time
        /// (Mesh.bindposes), so a cell's loot pose, carried into that mesh's space, is expressed
        /// in the bone's terms and followed.
        /// </summary>
        public static void BindToSkeleton(PlayerBody playerBody, CompoundItem rig, GameObject model)
        {
            if (model == null || rig == null)
            {
                return;
            }

            var markers = model.GetComponentsInChildren<PouchBoneMarker>(includeInactive: true);
            if (markers.Length == 0)
            {
                return;
            }

            var root = model.transform;
            var skins = WornSkins(root, rig.StringTemplateId);
            if (skins.Count == 0)
            {
                Plugin.Log.LogWarning($"[ModularVests] rig {rig.StringTemplateId}: the model on a character has no " +
                                      "skinned mesh bound to the skeleton, pouches stay in the model's own space");
                return;
            }

            if (playerBody != null && playerBody.MeshTransform != null &&
                Quaternion.Angle(root.rotation, playerBody.MeshTransform.rotation) > 1f)
            {
                Log(model, $"note: the rig root is turned {Quaternion.Angle(root.rotation, playerBody.MeshTransform.rotation):0}° " +
                           "against the body; the pouches follow the rig");
            }

            // On a body the item's loot half is switched off and its dress half switched on
            // (DressItem.EnableLoot); a bone left in the loot half would vanish with it.
            var dress = model.GetComponent<DressItem>();
            var host = dress != null && dress.DressPrefab != null ? dress.DressPrefab.transform : skins[0].Skin.transform;

            // the loot poses first, in the root's space: the bones still stand in them
            var cells = new List<WornCell>();
            foreach (var marker in markers)
            {
                var bone = marker.transform;
                cells.Add(new WornCell
                {
                    Bone = bone,
                    Position = root.InverseTransformPoint(bone.position),
                    Rotation = Quaternion.Inverse(root.rotation) * bone.rotation,
                });
            }

            // The cells of a cluster all follow ONE skeleton bone, the one nearest the middle of
            // the cluster: a pouch spread over several cells hangs from its anchor and is seated
            // between the others, which must not drift apart as the body moves.
            var targets = new Dictionary<int, SkeletonBone>();
            foreach (var group in GroupByCluster(cells))
            {
                var centre = Vector3.zero;
                foreach (var cell in group.Value)
                {
                    centre += cell.Position;
                }

                targets[group.Key] = NearestBone(skins, centre / group.Value.Count);
            }

            foreach (var cell in cells)
            {
                var target = ClusterGrid.TryParse(cell.Bone.name, out var cluster, out _) && targets.ContainsKey(cluster)
                    ? targets[cluster]
                    : NearestBone(skins, cell.Position);
                if (target.Skin == null)
                {
                    continue;
                }

                if (cell.Bone.parent != host)
                {
                    cell.Bone.SetParent(host, worldPositionStays: true);
                }

                // root space -> the mesh's space -> the bone's space (its bind pose)
                var skin = target.Skin;
                var bind = skin.Bindposes[target.Index];
                var inMesh = skin.FromRoot.MultiplyPoint3x4(cell.Position);
                var turnInMesh = Quaternion.Inverse(skin.Rotation) * cell.Rotation;
                cell.Bone.gameObject.GetOrAddComponent<PouchBoneFollower>()
                    .Bind(target.Transform, bind.MultiplyPoint3x4(inMesh), bind.rotation * turnInMesh);
            }

            // the bones now stand where the body has them (Bind poses at once): seat the pouches
            // between them again
            PouchSeat.ReseatAll(rig, root);

            // the pouches came over from the switched-off loot half: no solid colliders inside the wearer
            var disabled = 0;
            foreach (var cell in cells)
            {
                disabled += PouchPhysics.DisableColliders(PouchSeat.ModelOn(cell.Bone));
            }

            if (disabled > 0)
            {
                Log(model, $"{disabled} solid collider(s) of its pouch models switched off");
            }

            // which bone each cluster follows: always written, once per rig and outcome - the
            // first thing to look at when pouches stand wrong on a character
            // and where each cluster ended up on the body: its height above the body's origin (the
            // feet) - a chest is some 1.2-1.4 m up, a cummerbund 1.0-1.2 m
            var body = playerBody != null ? playerBody.MeshTransform : null;
            var bound = new List<string>();
            var key = "bind:" + rig.StringTemplateId;
            foreach (var target in targets)
            {
                key += $"|{target.Key}:{target.Value.Transform.name}:{target.Value.Skin.Name}";
                var where = "";
                if (body != null)
                {
                    var centre = Vector3.zero;
                    var count = 0;
                    foreach (var cell in cells)
                    {
                        if (ClusterGrid.TryParse(cell.Bone.name, out var cluster, out _) && cluster == target.Key)
                        {
                            centre += cell.Bone.position;
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        centre /= count;
                        where = $", now {Vector3.Dot(centre - body.position, Vector3.up):0.00} m above the feet, " +
                                $"{V(body.InverseTransformPoint(centre))} in the body mesh's space";
                    }
                }

                bound.Add($"C{target.Key} -> '{target.Value.Transform.name}' of '{target.Value.Skin.Name}' " +
                          $"({target.Value.Distance * 100f:0} cm from the bone{where})");
            }

            var summary = $"[ModularVests] rig {rig.StringTemplateId} on a character: clusters follow " +
                          string.Join("; ", bound.ToArray());
            if (Logged.Add(key))
            {
                Plugin.Log.LogInfo(summary);
            }
        }

        private sealed class WornCell
        {
            public Transform Bone;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        private static Dictionary<int, List<WornCell>> GroupByCluster(List<WornCell> cells)
        {
            var result = new Dictionary<int, List<WornCell>>();
            foreach (var cell in cells)
            {
                if (!ClusterGrid.TryParse(cell.Bone.name, out var cluster, out _))
                {
                    continue;
                }

                if (!result.TryGetValue(cluster, out var list))
                {
                    result[cluster] = list = new List<WornCell>();
                }

                list.Add(cell);
            }

            return result;
        }

        private static void Log(GameObject model, string message)
        {
            if (ClientConfig.DebugLog.Value && Logged.Add("body:" + model.name + ":" + message))
            {
                Plugin.Log.LogInfo($"[ModularVests] '{model.name}' on a character: {message}");
            }
        }

        // --- anchor ---

        /// <summary>The transform the bones hang from: the model's chest plate, or null.</summary>
        public static Transform GetAnchor(CompoundItem rig, Transform root)
        {
            var key = root.GetInstanceID();
            if (Anchors.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var anchor = FindChestAnchor(rig, root);
            if (anchor != null)
            {
                Anchors[key] = anchor;
            }

            return anchor;
        }

        public static AnchorFrame GetFrame(CompoundItem rig, Transform root)
        {
            var key = root.GetInstanceID();
            if (Frames.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var frame = FindFrame(rig, root, out var reliable);

            // a model still being set up has no bounds yet: try again next time
            if (reliable)
            {
                Frames[key] = frame;
            }

            return frame;
        }

        private static AnchorFrame FindFrame(CompoundItem rig, Transform root, out bool reliable)
        {
            var frame = new AnchorFrame
            {
                Origin = Vector3.zero,
                Right = ModelRight,
                Up = ModelUp,
                Forward = ModelForward,
                Source = "model origin",
            };

            var hasBounds = TryGetModelBounds(root, out var min, out var max);
            reliable = hasBounds;

            var anchor = GetAnchor(rig, root);
            if (anchor != null)
            {
                frame.Origin = root.InverseTransformPoint(CentreOf(anchor));
                frame.Source = "'" + anchor.name + "'";
            }
            else if (hasBounds)
            {
                var centre = (min + max) * 0.5f;
                var height = Vector3.Dot(max - min, Abs(ModelUp));
                frame.Origin = centre + ModelUp * (height * ChestHeightFraction);
                frame.Source = "model bounds";
            }

            // stand the layout on the front surface of the model, whatever the anchor's depth
            if (hasBounds)
            {
                var along = Vector3.Dot(frame.Origin, Abs(ModelForward));
                var front = Vector3.Dot(ModelForward.x + ModelForward.y + ModelForward.z < 0 ? min : max,
                    Abs(ModelForward));
                frame.Origin += ModelForward * (Mathf.Abs(front - along) + SurfaceOffset);
            }

            return frame;
        }

        /// <summary>
        /// The chest plate of the model, if it has something recognisable as one: a transform
        /// named after the front plate slot, after its plate collider, or containing "chest".
        /// </summary>
        private static Transform FindChestAnchor(CompoundItem rig, Transform root)
        {
            var names = new List<string>();
            foreach (var slot in rig.Slots ?? Array.Empty<Slot>())
            {
                if (!(slot is ArmorSlot) || PouchSlots.IsPouchSlot(slot))
                {
                    continue;
                }

                var mask = slot.ArmorPlateColliderMask;
                if (string.Equals(slot.ID, "Front_plate", StringComparison.OrdinalIgnoreCase) ||
                    (mask & (EArmorPlateCollider.Plate_Granit_SAPI_chest | EArmorPlateCollider.Plate_Korund_chest)) != 0)
                {
                    names.Add(slot.ID);
                    foreach (EArmorPlateCollider flag in Enum.GetValues(typeof(EArmorPlateCollider)))
                    {
                        if ((mask & flag) != 0)
                        {
                            names.Add(flag.ToString());
                        }
                    }
                }
            }

            foreach (var name in names)
            {
                var found = TransformTools.FindTransformRecursive(root, name, ignoreCase: true);
                if (found != null && found.GetComponent<PouchBoneMarker>() == null)
                {
                    return found;
                }
            }

            foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t != root && t.name.IndexOf("chest", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    t.GetComponentInParent<PouchBoneMarker>(true) == null)
                {
                    return t;
                }
            }

            return null;
        }

        private static Vector3 CentreOf(Transform t)
        {
            var collider = t.GetComponent<Collider>();
            if (collider is BoxCollider box)
            {
                return t.TransformPoint(box.center);
            }

            var renderer = t.GetComponent<Renderer>();
            if (renderer != null && renderer.bounds.size != Vector3.zero)
            {
                return renderer.bounds.center;
            }

            return t.position;
        }

        /// <summary>
        /// Box of the model's own meshes in root space (attached pouches excluded). Measured
        /// from the meshes rather than the renderers: on a character half of the model is
        /// switched off, and a disabled renderer reports nothing.
        /// </summary>
        private static bool TryGetModelBounds(Transform root, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var any = false;

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                any |= Encapsulate(root, filter.transform, filter.sharedMesh, ref min, ref max);
            }

            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
            {
                any |= Encapsulate(root, skinned.transform, skinned.sharedMesh, ref min, ref max);
            }

            return any;
        }

        private static bool Encapsulate(Transform root, Transform owner, Mesh mesh, ref Vector3 min, ref Vector3 max)
        {
            if (mesh == null || owner.GetComponentInParent<PouchBoneMarker>(true) != null)
            {
                return false;
            }

            var b = mesh.bounds;
            if (b.size == Vector3.zero)
            {
                return false;
            }

            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                var local = root.InverseTransformPoint(owner.TransformPoint(corner));
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }

            return true;
        }

        // --- diagnostics ---
        //
        // Where a bone ends up cannot be seen from a log line about the template alone: the
        // same rig is built as a preview model and again on a character, and the two do not
        // share a root. One line per model says which context it was and where the bones went.

        private static void LogContext(CompoundItem rig, Transform root, Transform anchor, List<Slot> slots)
        {
            if (!ClientConfig.DebugLog.Value)
            {
                return;
            }

            var onBody = root.GetComponentInParent<EFT.PlayerBody>() != null ||
                         root.GetComponentInParent<EFT.Player>() != null;
            var key = $"{rig.StringTemplateId}:{(onBody ? "body" : "preview")}:{root.name}";
            if (!Logged.Add(key))
            {
                return;
            }

            var bone = TransformTools.FindTransformRecursive(root, slots[0].ID, ignoreCase: true);
            var frame = GetFrame(rig, root);
            Plugin.Log.LogInfo(
                $"[ModularVests] pouch bones on '{root.name}' ({(onBody ? "character" : "preview")}): " +
                $"anchor {(anchor != null ? "'" + anchor.name + "'" : "none, " + frame.Source)}, " +
                $"{slots.Count} slot(s), first bone local {(bone != null ? bone.localPosition.ToString("F3") : "n/a")}, " +
                $"world {(bone != null ? bone.position.ToString("F3") : "n/a")}, " +
                $"root world {root.position.ToString("F3")}");
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private static float Normalize(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
            {
                angle -= 360f;
            }
            else if (angle <= -180f)
            {
                angle += 360f;
            }

            return angle;
        }

        // keeps bones.json readable: sub-0.1 mm and sub-0.01 degree noise is not layout
        private static float Round(float value) => Mathf.Round(value * 10000f) / 10000f;
    }
}

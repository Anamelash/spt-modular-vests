#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModularVests.Client.Bones
{
    /// <summary>
    /// Pose of one pouch bone on the item's own (loot) model.
    /// </summary>
    public sealed class BonePose
    {
        // No initialisers: Newtonsoft leaves a missing field as it is, and a default here would
        // turn a half-written entry into a pose at the origin.
        [JsonProperty("pos", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Position { get; set; }

        /// <summary>Euler angles, degrees.</summary>
        [JsonProperty("rot", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Rotation { get; set; }

        /// <summary>What the pose is measured from (see <see cref="AnchorSpace"/>).</summary>
        [JsonProperty("space", NullValueHandling = NullValueHandling.Ignore)]
        public string Space { get; set; }


        /// <summary>Measured from the chest plate of the model, which is where bones hang from.</summary>
        public const string AnchorSpace = "anchor";

        /// <summary>
        /// Measured from the model root. What the mod wrote before the bones moved onto the
        /// chest plate, and what a model without a recognisable plate still uses; entries
        /// without a "space" are read as this.
        /// </summary>
        public const string RootSpace = "root";

        [JsonIgnore]
        public bool IsAnchored => Space == AnchorSpace;

        public BonePose Clone() => new BonePose
        {
            Position = (float[])Position?.Clone(),
            Rotation = (float[])Rotation?.Clone(),
            Space = Space,
        };

        /// <summary>True when the pose is present and complete.</summary>
        internal bool HasPose =>
            Position != null && Position.Length == 3 && Rotation != null && Rotation.Length == 3;
    }

    /// <summary>
    /// Where the pouch slots of each rig sit on its item model: rig tpl -> slot id -> pose. The
    /// model worn on a character is the same geometry: the client carries every pose over to it.
    ///
    /// The file next to the plugin is the shipped default AND what the in-game editor writes
    /// back to, so a layout made in the editor is the layout that ships. A slot without an
    /// entry falls back to the default layout (<see cref="DefaultFor"/>, anchored on the chest
    /// plate by the client), which keeps the modding screen usable for a rig nobody has laid
    /// out yet. (A "dress" pose of old files is ignored and dropped on the next save.)
    ///
    /// Plain data and Newtonsoft only: no Unity types, so the tests can load it.
    /// </summary>
    public sealed class BoneStore
    {
        /// <summary>Horizontal spacing of the cells of the default layout, metres.</summary>
        public const float DefaultSpacing = 0.065f;

        /// <summary>Vertical spacing of the default layout, metres.</summary>
        public const float DefaultRowSpacing = 0.09f;

        private readonly string _path;
        private readonly Action<string> _warn;
        private Dictionary<string, Dictionary<string, BonePose>> _poses =
            new Dictionary<string, Dictionary<string, BonePose>>();

        /// <summary>
        /// Rigs that share another rig's layout: tpl -> the tpl that owns it. In the file an alias is
        /// a string where a layout would be ("tpl": "owner tpl"). Every read and write of an alias
        /// goes to its owner, so kits of one model (same torso, other shoulders or groin) are laid
        /// out once, and a rig that is not in the group keeps a layout of its own.
        /// </summary>
        private Dictionary<string, string> _aliases = new Dictionary<string, string>();

        public BoneStore(string path, Action<string> warn = null)
        {
            _path = path;
            _warn = warn ?? (_ => { });
        }

        public string Path => _path;

        /// <summary>True when there are changes not yet written by <see cref="Save"/>.</summary>
        public bool Dirty { get; private set; }

        /// <summary>Reads the file; a missing or broken file leaves the store empty (defaults everywhere).</summary>
        public void Load()
        {
            _poses = new Dictionary<string, Dictionary<string, BonePose>>();
            _aliases = new Dictionary<string, string>();
            Dirty = false;

            if (!File.Exists(_path))
            {
                _warn($"bone layout {_path} not found, every pouch slot uses the default layout");
                return;
            }

            try
            {
                var read = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(File.ReadAllText(_path));
                if (read == null)
                {
                    return;
                }

                foreach (var rig in read)
                {
                    if (rig.Value == null || rig.Value.Type == JTokenType.Null)
                    {
                        continue;
                    }

                    if (rig.Value.Type == JTokenType.String)
                    {
                        _aliases[rig.Key] = rig.Value.Value<string>();
                        continue;
                    }

                    var layout = rig.Value.ToObject<Dictionary<string, BonePose>>();
                    var slots = new Dictionary<string, BonePose>();
                    foreach (var slot in layout ?? new Dictionary<string, BonePose>())
                    {
                        var entry = Sanitise(slot.Value);
                        if (entry != null)
                        {
                            slots[slot.Key] = entry;
                        }
                        else
                        {
                            _warn($"bone layout: {rig.Key}/{slot.Key} is malformed, default used");
                        }
                    }

                    _poses[rig.Key] = slots;
                }

                DropBadAliases();
            }
            catch (Exception ex)
            {
                _poses = new Dictionary<string, Dictionary<string, BonePose>>();
                _aliases = new Dictionary<string, string>();
                _warn($"bone layout {_path} is unreadable ({ex.Message}), every pouch slot uses the default layout");
            }
        }

        /// <summary>Keeps the well-formed parts of an entry; null when nothing usable is left.</summary>
        private static BonePose Sanitise(BonePose entry)
        {
            if (entry == null)
            {
                return null;
            }

            return entry.HasPose ? entry : null;
        }

        // --- a slot ---

        public bool TryGet(string rigTpl, string slotId, out BonePose pose)
        {
            pose = null;
            if (!TryGetEntry(rigTpl, slotId, out var entry) || !entry.HasPose)
            {
                return false;
            }

            pose = entry;
            return true;
        }

        /// <summary>Sets the pose of a slot.</summary>
        public void Set(string rigTpl, string slotId, BonePose pose)
        {
            if (pose == null || !pose.HasPose)
            {
                throw new ArgumentException("pose must have three position and three rotation values");
            }

            var entry = Entry(rigTpl, slotId);
            entry.Position = (float[])pose.Position.Clone();
            entry.Rotation = (float[])pose.Rotation.Clone();
            entry.Space = pose.Space;
            Dirty = true;
        }

        /// <summary>Drops the pose: the slot goes back to the default layout.</summary>
        public void Remove(string rigTpl, string slotId)
        {
            if (!TryGetEntry(rigTpl, slotId, out var entry) || !entry.HasPose)
            {
                return;
            }

            entry.Position = null;
            entry.Rotation = null;
            entry.Space = null;
            Prune(rigTpl, slotId, entry);
            Dirty = true;
        }

        // --- a rig as a whole ---

        /// <summary>A copy of every entry of a rig; empty for an unknown rig.</summary>
        public Dictionary<string, BonePose> GetRig(string rigTpl)
        {
            var copy = new Dictionary<string, BonePose>();
            if (rigTpl != null && _poses.TryGetValue(LayoutOf(rigTpl), out var slots))
            {
                foreach (var slot in slots)
                {
                    copy[slot.Key] = slot.Value.Clone();
                }
            }

            return copy;
        }

        /// <summary>Replaces every entry of a rig with copies of the given ones (none = the rig is dropped).</summary>
        public void SetRig(string rigTpl, IDictionary<string, BonePose> entries)
        {
            if (rigTpl == null)
            {
                throw new ArgumentNullException(nameof(rigTpl));
            }

            var slots = new Dictionary<string, BonePose>();
            foreach (var slot in entries ?? new Dictionary<string, BonePose>())
            {
                if (slot.Key != null && slot.Value != null && slot.Value.HasPose)
                {
                    slots[slot.Key] = slot.Value.Clone();
                }
            }

            _poses[LayoutOf(rigTpl)] = slots;
            Dirty = true;
        }

        // --- shared layouts ---

        /// <summary>The rig whose layout this rig uses: its alias owner, or itself.</summary>
        public string LayoutOf(string rigTpl) =>
            rigTpl != null && _aliases.TryGetValue(rigTpl, out var owner) ? owner : rigTpl;

        /// <summary>Makes a rig use another rig's layout (its own entries, if any, are dropped).</summary>
        public void Share(string rigTpl, string ownerTpl)
        {
            if (rigTpl == null || ownerTpl == null || rigTpl == ownerTpl || _aliases.ContainsKey(ownerTpl))
            {
                throw new ArgumentException("a rig shares the layout of a rig that has a layout of its own");
            }

            _poses.Remove(rigTpl);
            _aliases[rigTpl] = ownerTpl;
            Dirty = true;
        }

        /// <summary>An alias must name a rig that is not an alias itself (no chains, no loops).</summary>
        private void DropBadAliases()
        {
            foreach (var alias in new List<KeyValuePair<string, string>>(_aliases))
            {
                if (string.IsNullOrEmpty(alias.Value) || alias.Value == alias.Key || _aliases.ContainsKey(alias.Value))
                {
                    _aliases.Remove(alias.Key);
                    _warn($"bone layout: {alias.Key} shares the layout of '{alias.Value}', which is not a layout " +
                          "of its own; it gets a layout of its own");
                }
            }
        }

        private bool TryGetEntry(string rigTpl, string slotId, out BonePose entry)
        {
            entry = null;
            rigTpl = LayoutOf(rigTpl);
            return rigTpl != null && slotId != null &&
                   _poses.TryGetValue(rigTpl, out var slots) && slots.TryGetValue(slotId, out entry);
        }

        private BonePose Entry(string rigTpl, string slotId)
        {
            rigTpl = LayoutOf(rigTpl);
            if (!_poses.TryGetValue(rigTpl, out var slots))
            {
                _poses[rigTpl] = slots = new Dictionary<string, BonePose>();
            }

            if (!slots.TryGetValue(slotId, out var entry))
            {
                slots[slotId] = entry = new BonePose();
            }

            return entry;
        }

        private void Prune(string rigTpl, string slotId, BonePose entry)
        {
            rigTpl = LayoutOf(rigTpl);
            if (!entry.HasPose)
            {
                _poses[rigTpl].Remove(slotId);
            }
        }

        /// <summary>Writes atomically (temp file + replace), so a crash never leaves half a layout.</summary>
        public void Save()
        {
            AtomicFile.WriteAllText(_path, JsonConvert.SerializeObject(Sorted(), Formatting.Indented));
            Dirty = false;
        }

        /// <summary>
        /// Default layout in the coordinates of the rig's anchor frame (metres; x towards the
        /// WEARER's right, y up), so an unedited rig shows every cell on its front in a
        /// recognisable grid: clusters in mirrored pairs either side of the anchor (the chest
        /// plate), the first pair on the chest, each next pair lower and wider (the
        /// cummerbund). Inside a cluster the cells are 2x2 at <see cref="DefaultSpacing"/> by
        /// <see cref="DefaultRowSpacing"/>, cell 1 top left AS SEEN FROM THE FRONT - the
        /// viewer's left is the wearer's right, so columns count towards -x. Null for a name
        /// that is not a pouch cell. The client maps these onto the model.
        /// </summary>
        public static BonePose DefaultFor(string slotName)
        {
            if (!ClusterGrid.TryParse(slotName, out var cluster, out var position))
            {
                return null;
            }

            // odd clusters on the wearer's left, even ones on the right
            var pair = (cluster - 1) / 2;
            var side = cluster % 2 == 1 ? -1f : 1f;
            var centreX = side * (DefaultPairOffset + pair * DefaultPairWidening);
            var centreY = -pair * DefaultPairDrop;

            var column = ClusterGrid.ColumnOf(position);
            var row = ClusterGrid.RowOf(position);
            return new BonePose
            {
                Position = new[]
                {
                    centreX - (column - (ClusterGrid.Columns - 1) / 2f) * DefaultSpacing,
                    centreY + ((ClusterGrid.Rows - 1) / 2f - row) * DefaultRowSpacing,
                    0f,
                },
                Rotation = new[] { 0f, 0f, 0f },
            };
        }

        /// <summary>Distance of the first pair of clusters from the anchor, metres.</summary>
        public const float DefaultPairOffset = 0.08f;

        /// <summary>How much further out each next pair of clusters sits, metres.</summary>
        public const float DefaultPairWidening = 0.12f;

        /// <summary>How much lower each next pair of clusters sits, metres.</summary>
        public const float DefaultPairDrop = 0.2f;

        // stable key order keeps the shipped file diff-friendly
        private SortedDictionary<string, object> Sorted()
        {
            var sorted = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var rig in _poses)
            {
                if (rig.Value.Count > 0)
                {
                    sorted[rig.Key] = new SortedDictionary<string, BonePose>(rig.Value, StringComparer.Ordinal);
                }
            }

            foreach (var alias in _aliases)
            {
                sorted[alias.Key] = alias.Value;
            }

            return sorted;
        }
    }
}

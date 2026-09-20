#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ModularVests.Client.Bones
{
    /// <summary>
    /// How a pouch model sits on its seat: local position and rotation (Euler, degrees) of the
    /// model relative to the seat point of the cells it covers.
    /// </summary>
    public sealed class MountPose
    {
        [JsonProperty("pos")]
        public float[] Position { get; set; }

        [JsonProperty("rot")]
        public float[] Rotation { get; set; }

        internal bool IsValid =>
            Position != null && Position.Length == 3 && Rotation != null && Rotation.Length == 3;

        public MountPose Clone() => new MountPose
        {
            Position = (float[])Position?.Clone(),
            Rotation = (float[])Rotation?.Clone(),
        };

        /// <summary>
        /// What the game itself does with a model that has no ModPlacer
        /// (ContainerCollectionView.SlotView.InsertItem): no offset, turned (90, 0, 0).
        /// </summary>
        public static MountPose Default() => new MountPose
        {
            Position = new[] { 0f, 0f, 0f },
            Rotation = new[] { 90f, 0f, 0f },
        };
    }

    /// <summary>
    /// Mounts of the pouch models, per pouch template: pouches are vanilla models with their own
    /// pivots and axes, and a mount puts each one straight on its seat. Ships with the mod
    /// (mounts.json next to the plugin) until the pouches have bundles of their own, where a
    /// ModPlacer does the same job. A pouch without an entry gets <see cref="MountPose.Default"/>.
    ///
    /// Plain data and Newtonsoft only: no Unity types, so the tests can load it.
    /// </summary>
    public sealed class MountStore
    {
        private readonly string _path;
        private readonly Action<string> _warn;
        private Dictionary<string, MountPose> _mounts = new Dictionary<string, MountPose>();

        public MountStore(string path, Action<string> warn = null)
        {
            _path = path;
            _warn = warn ?? (_ => { });
        }

        public string Path => _path;

        public bool Dirty { get; private set; }

        /// <summary>Reads the file; a missing file is fine (every pouch mounts by default).</summary>
        public void Load()
        {
            _mounts = new Dictionary<string, MountPose>();
            Dirty = false;
            if (!File.Exists(_path))
            {
                return;
            }

            try
            {
                var read = JsonConvert.DeserializeObject<Dictionary<string, MountPose>>(File.ReadAllText(_path));
                foreach (var entry in read ?? new Dictionary<string, MountPose>())
                {
                    if (entry.Value != null && entry.Value.IsValid)
                    {
                        _mounts[entry.Key] = entry.Value;
                    }
                    else
                    {
                        _warn($"pouch mounts: {entry.Key} is malformed, default used");
                    }
                }
            }
            catch (Exception ex)
            {
                _mounts = new Dictionary<string, MountPose>();
                _warn($"pouch mounts {_path} are unreadable ({ex.Message}), every pouch mounts by default");
            }
        }

        public bool TryGet(string pouchTpl, out MountPose mount)
        {
            mount = null;
            return pouchTpl != null && _mounts.TryGetValue(pouchTpl, out mount);
        }

        /// <summary>The pouch's mount, or the default one.</summary>
        public MountPose Get(string pouchTpl) => TryGet(pouchTpl, out var mount) ? mount : MountPose.Default();

        public void Set(string pouchTpl, MountPose mount)
        {
            if (pouchTpl == null || mount == null || !mount.IsValid)
            {
                throw new ArgumentException("a mount needs a pouch template and three position and rotation values");
            }

            _mounts[pouchTpl] = mount.Clone();
            Dirty = true;
        }

        public void Remove(string pouchTpl)
        {
            if (pouchTpl != null && _mounts.Remove(pouchTpl))
            {
                Dirty = true;
            }
        }

        public void Save()
        {
            var sorted = new SortedDictionary<string, MountPose>(_mounts, StringComparer.Ordinal);
            AtomicFile.WriteAllText(_path, JsonConvert.SerializeObject(sorted, Formatting.Indented));
            Dirty = false;
        }
    }
}

using System.Collections.Generic;
using ModularVests.Client;
using ModularVests.Client.Bones;
using Newtonsoft.Json;
using UnityEngine;

namespace ModularVests.DevTools
{
    // Undo: every edit starts with a snapshot of the rig's layout and of the
    // mounts of the pouches on it; Undo puts the last snapshot back into the stores and the bones
    // back where it has them. Nothing is written to disk by it - saving is as before.
    internal sealed partial class BoneEditor
    {
        private const int UndoDepth = 50;

        private sealed class UndoStep
        {
            public string Rig;
            public string What;
            public Dictionary<string, BonePose> Poses;

            /// <summary>Pouch tpl -> its mount, null for "none stored".</summary>
            public Dictionary<string, MountPose> Mounts;
        }

        private readonly List<UndoStep> _undo = new List<UndoStep>();

        /// <summary>Called at the start of every edit, before anything moves.</summary>
        private void BeginEdit(string what)
        {
            if (_rig == null || Plugin.Bones == null)
            {
                return;
            }

            _undo.Add(Snapshot(what));
            if (_undo.Count > UndoDepth)
            {
                _undo.RemoveAt(0);
            }
        }

        private UndoStep Snapshot(string what)
        {
            var mounts = new Dictionary<string, MountPose>();
            foreach (var slot in _slots)
            {
                var pouch = slot.ContainedItem?.StringTemplateId;
                if (pouch == null || mounts.ContainsKey(pouch))
                {
                    continue;
                }

                mounts[pouch] = Plugin.Mounts != null && Plugin.Mounts.TryGet(pouch, out var mount) ? mount.Clone() : null;
            }

            return new UndoStep
            {
                Rig = _rig.StringTemplateId,
                What = what,
                Poses = Plugin.Bones.GetRig(_rig.StringTemplateId),
                Mounts = mounts,
            };
        }

        /// <summary>Back one edit. Steps that changed nothing (a tool that found nothing to do) are skipped.</summary>
        private void Undo()
        {
            if (_dragging || _rig == null)
            {
                return;
            }

            var now = Json(Snapshot(""));
            while (_undo.Count > 0)
            {
                var step = _undo[_undo.Count - 1];
                _undo.RemoveAt(_undo.Count - 1);
                if (step.Rig != _rig.StringTemplateId)
                {
                    _undo.Clear();
                    break;
                }

                if (Json(step) == now)
                {
                    continue;
                }

                Plugin.Bones.SetRig(step.Rig, step.Poses);
                if (Plugin.Mounts != null)
                {
                    foreach (var mount in step.Mounts)
                    {
                        if (mount.Value == null)
                        {
                            Plugin.Mounts.Remove(mount.Key);
                        }
                        else
                        {
                            Plugin.Mounts.Set(mount.Key, mount.Value);
                        }
                    }
                }

                ReapplyPoses();
                Reseat();
                Refresh();
                _status = $"undone: {step.What}" + (_undo.Count > 0 ? $" ({_undo.Count} more)" : "");
                return;
            }

            _status = "nothing to undo";
        }

        private static string Json(UndoStep step) => JsonConvert.SerializeObject(new object[] { step.Poses, step.Mounts });

        /// <summary>Every bone to the pose the store has for it.</summary>
        private void ReapplyPoses()
        {
            var tpl = _rig.StringTemplateId;
            foreach (var slot in _slots)
            {
                var bone = BoneOf(slot);
                if (bone == null)
                {
                    continue;
                }

                if (Plugin.Bones.TryGet(tpl, slot.ID, out var pose))
                {
                    PouchBones.ApplyPose(bone, pose, ModelRoot);
                }
                else
                {
                    PouchBones.ApplyDefault(bone, _rig);
                }
            }
        }
    }
}

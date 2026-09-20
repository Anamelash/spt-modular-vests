using System.Collections.Generic;
using EFT;
using ModularVests.Client;
using ModularVests.Client.Bones;
using UnityEngine;

namespace ModularVests.DevTools
{
    // "Copy layout" / "Paste layout": a whole rig's layout from one model onto another. Each cell is
    // taken in the rig's frame (PouchBones.ReadInFrame: metres from the point on its chest, along its
    // right, up and forward) and put down in the frame of the other model, so it lands on the same
    // spot of the chest whether the model hangs its cells from a chest plate or has none (MF-UNTAR).
    // The copy outlives the Modding screen: copy on one rig, open another, paste.
    internal sealed partial class BoneEditor
    {
        private sealed class LayoutClipboard
        {
            public string Source;
            public readonly Dictionary<string, KeyValuePair<Vector3, Quaternion>> Cells =
                new Dictionary<string, KeyValuePair<Vector3, Quaternion>>();
        }

        private LayoutClipboard _layoutClipboard;

        private void CopyLayout()
        {
            var root = ModelRoot;
            if (root == null)
            {
                return;
            }

            var copy = new LayoutClipboard { Source = _rig.ShortName.Localized() };
            foreach (var slot in _slots)
            {
                var bone = BoneOf(slot);
                if (bone == null)
                {
                    continue;
                }

                PouchBones.ReadInFrame(bone, _rig, root, out var offset, out var turn);
                copy.Cells[slot.ID] = new KeyValuePair<Vector3, Quaternion>(offset, turn);
            }

            _layoutClipboard = copy;
            _status = $"layout of {copy.Source} copied: {copy.Cells.Count} cell(s), in the rig's frame " +
                      $"({PouchBones.GetFrame(_rig, root).Source})";
            DevPlugin.Log.LogInfo($"[ModularVests.DevTools] {_rig.StringTemplateId}: layout copied, {copy.Cells.Count} " +
                                  $"cell(s), frame from {PouchBones.GetFrame(_rig, root).Source}");
        }

        /// <summary>Every cell of this rig that the copy has onto the same spot of this model; cells it lacks keep theirs.</summary>
        private void PasteLayout()
        {
            var root = ModelRoot;
            if (root == null || _layoutClipboard == null)
            {
                return;
            }

            var placed = 0;
            var missed = new List<string>();
            foreach (var slot in _slots)
            {
                var bone = BoneOf(slot);
                if (bone == null || !_layoutClipboard.Cells.TryGetValue(slot.ID, out var cell))
                {
                    missed.Add(slot.ID);
                    continue;
                }

                PouchBones.PlaceInFrame(bone, _rig, root, cell.Key, cell.Value);
                StorePose(slot, bone);
                placed++;
            }

            Reseat();
            Refresh();
            var frame = PouchBones.GetFrame(_rig, root).Source;
            _status = $"layout of {_layoutClipboard.Source} pasted: {placed} cell(s) in this rig's frame ({frame})" +
                      (missed.Count > 0 ? $", not in the copy: {string.Join(", ", missed.ToArray())}" : "") +
                      "; Project puts them onto the surface";
            DevPlugin.Log.LogInfo($"[ModularVests.DevTools] {_rig.StringTemplateId}: layout of {_layoutClipboard.Source} " +
                                  $"pasted, {placed} cell(s), frame from {frame}");
        }
    }
}

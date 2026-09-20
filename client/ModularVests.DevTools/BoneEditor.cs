using System;
using System.Collections.Generic;
using EFT.InventoryLogic;
using EFT.UI.WeaponModding;
using ModularVests.Client;
using ModularVests.Client.Bones;
using UnityEngine;

namespace ModularVests.DevTools
{
    /// <summary>
    /// In-game layout tool for the pouch cells of a modular rig, on its Modding screen. A
    /// development tool: it writes bones.json and mounts.json, which the plugin reads.
    ///
    /// Every cell is a bone named after its slot (mod_pouch_N); cells come in 2x2 clusters
    /// (ClusterGrid). The selected cell carries a gizmo (three axis arms and a centre block,
    /// real colliders on the preview layer): dragging an arm moves along that axis, the centre
    /// moves in the view plane, an axis modifier key constrains a centre drag, the rotate
    /// modifier turns a drag into a rotation. Surface snap keeps every edit on the rig's mesh.
    /// Clusters can be mirrored onto their pair and laid out from their cell 1; the pouch mount
    /// mode moves a pouch model on its seat instead of a bone.
    ///
    /// Poses are local to whatever the bone hangs from (the model's chest plate, see
    /// PouchBones), per rig template and slot, and are written out when the preview closes or on
    /// the save shortcut. The F12 switch is read live: the hooks are always in place and do
    /// nothing while the editor is off.
    /// </summary>
    internal sealed partial class BoneEditor : MonoBehaviour
    {
        private enum Axis
        {
            None,
            X,
            Y,
            Z,
        }

        /// <summary>How long the screen must stay inactive before the editor lets go, seconds.</summary>
        private const float InactiveGrace = 0.5f;

        private static readonly Color HighlightColor = new Color(1f, 0.75f, 0.1f);

        public static BoneEditor Instance { get; private set; }

        public static bool Enabled => DevConfig.BoneEditorEnabled.Value;

        private WeaponModdingScreen _screen;
        private CompoundItem _rig;
        private ContainerCollectionView _view;
        private List<Slot> _slots = new List<Slot>();
        private readonly Dictionary<string, Slot> _cells = new Dictionary<string, Slot>();
        private readonly List<int> _clusters = new List<int>();
        private int _cluster = 1;
        private int _position = 1;
        private string _selectedRig;
        private BonePose _clipboard;
        private bool _mountMode;
        private string _status = "";

        /// <summary>
        /// Lives on the plugin's own object, the way every other BepInEx component in this
        /// game does. A GameObject of our own with DontDestroyOnLoad did get created, and the
        /// editor did attach to a screen, but Unity never ticked it: not a single Update ran.
        /// </summary>
        public static void Create(GameObject host)
        {
            if (Instance != null)
            {
                return;
            }

            Instance = host.AddComponent<BoneEditor>();
            DevPlugin.Log.LogInfo($"[ModularVests.DevTools] bone editor component created on '{host.name}'");
        }

        public bool Active => Enabled && _screen != null && _rig != null;

        /// <summary>The Modding screen (re)built the slot icons for this rig.</summary>
        public void Begin(WeaponModdingScreen screen, CompoundItem rig, ContainerCollectionView view)
        {
            _screen = screen;
            _rig = rig;
            _view = view;
            _slots = PouchSlots.Of(rig);
            _dragging = false;
            IndexCells();

            // a rebuild (a pouch attached or removed) keeps the selection on the same rig
            if (_selectedRig != rig.StringTemplateId)
            {
                _selectedRig = rig.StringTemplateId;
                _cluster = _clusters.Count > 0 ? _clusters[0] : 1;
                _position = 1;
            }

            _logged.RemoveWhere(k => k.StartsWith("trace:", StringComparison.Ordinal));
            _traced = 0;
            LogOnce("begin:" + rig.StringTemplateId,
                $"[ModularVests.DevTools] bone editor attached to {rig.StringTemplateId}: {_slots.Count} pouch cell(s) " +
                $"in {_clusters.Count} cluster(s), {_slots.FindAll(s => BoneOf(s) != null).Count} with bones, " +
                $"editor {(Enabled ? "on" : "off")}");

            if (Enabled && DevConfig.MeshDiagnostics.Value && ModelRoot != null)
            {
                MeshGeometry.LogDiagnostics(ModelRoot, rig.StringTemplateId);
            }

            InvalidateSurface();
            if (Enabled)
            {
                Highlight();
            }
        }

        private void IndexCells()
        {
            _cells.Clear();
            _clusters.Clear();
            foreach (var slot in _slots)
            {
                if (ClusterGrid.TryParse(slot.ID, out var cluster, out _))
                {
                    _cells[slot.ID] = slot;
                    if (!_clusters.Contains(cluster))
                    {
                        _clusters.Add(cluster);
                    }
                }
            }

            _clusters.Sort();
        }

        // --- cells and selection ---

        private Slot CellSlot(int cluster, int position) =>
            _cells.TryGetValue(ClusterGrid.SlotName(cluster, position), out var slot) ? slot : null;

        private Transform CellBone(int cluster, int position) => BoneOf(CellSlot(cluster, position));

        private Slot SelectedSlot() => CellSlot(_cluster, _position) ?? (_slots.Count > 0 ? _slots[0] : null);

        private Transform SelectedBone() => BoneOf(SelectedSlot());

        private void Select(int cluster, int position)
        {
            if (CellSlot(cluster, position) == null)
            {
                return;
            }

            _cluster = cluster;
            _position = position;
            Highlight();
        }

        private void Select(Slot slot)
        {
            if (slot != null && ClusterGrid.TryParse(slot.ID, out var cluster, out var position))
            {
                Select(cluster, position);
            }
        }

        private void StepCluster(int direction)
        {
            if (_clusters.Count == 0)
            {
                return;
            }

            var index = _clusters.IndexOf(_cluster);
            index = ((index < 0 ? 0 : index) + direction + _clusters.Count) % _clusters.Count;
            Select(_clusters[index], _position);
        }

        private Transform BoneOf(Slot slot)
        {
            if (slot == null || _view == null)
            {
                return null;
            }

            var bone = _view.ContainerBones.TryGetValue(slot, out var slotView) ? slotView.Bone : null;
            if (bone == null && _view.GameObject != null)
            {
                bone = TransformTools.FindTransformRecursive(_view.GameObject.transform, slot.ID, ignoreCase: true);
            }

            return bone;
        }

        /// <summary>The pouch model hung on a cell, or null.</summary>
        private Transform PouchViewOf(Slot slot)
        {
            if (slot?.ContainedItem == null || _view == null)
            {
                return null;
            }

            if (_view.ContainerBones.TryGetValue(slot, out var slotView) && slotView.ItemView != null)
            {
                return slotView.ItemView;
            }

            var bone = BoneOf(slot);
            return bone != null ? PouchSeat.ModelOn(bone) : null;
        }

        /// <summary>What the gizmo moves: the selected cell's bone, or its pouch in mount mode.</summary>
        private Transform EditTarget() => _mountMode ? PouchViewOf(SelectedSlot()) : SelectedBone();

        // The layout is made on the item's own model only: the worn model is the same geometry,
        // and the plugin carries every cell over to it exactly (PouchBones.BindToSkeleton).
        private Transform ModelRoot => _view != null && _view.GameObject != null ? _view.GameObject.transform : null;

        /// <summary>
        /// A Modding screen reports a close. That is NOT taken as "the editor is done": the
        /// screen fires it while it is opening as well (and screens are pooled and reused),
        /// which used to detach the editor the moment it attached. All it means here is that
        /// this is a good moment to write the layout out; whether the editor keeps working is
        /// decided by <see cref="Update"/>, which watches the screen itself.
        /// </summary>
        public void OnScreenClose(WeaponModdingScreen screen)
        {
            if (_screen != null && screen == _screen)
            {
                Trace("close", "screen reported a close, layout saved");
                SaveIfDirty();
            }
        }

        /// <summary>
        /// The preview was taken down (WeaponPreview.Hide). That IS the end of a session: the
        /// model, its bones and the gizmo are gone with it.
        /// </summary>
        public void OnPreviewHidden(WeaponPreview preview)
        {
            if (_screen != null && preview != null && _screen._weaponPreview == preview)
            {
                Trace("hidden", "the preview closed, editor detached");
                End();
            }
        }

        /// <summary>The editor is done with this screen: write what changed and let go.</summary>
        public void End()
        {
            if (_screen == null)
            {
                return;
            }

            SaveIfDirty();
            ClearHighlight();

            _mountMode = false;
            InvalidateSurface();

            _screen = null;
            _rig = null;
            _view = null;
            _slots = new List<Slot>();
            _cells.Clear();
            _clusters.Clear();
            _undo.Clear();
            _heldKey = KeyCode.None;
            _dragging = false;
            _grabAxis = Axis.None;
            _gizmo.Destroy();
        }

        // --- the frame loop ---

        private float _inactiveSince;

        private void Update()
        {
            LogOnce("tick", "[ModularVests.DevTools] bone editor is ticking");

            if (_screen == null)
            {
                Trace("idle", "no screen attached");
                return;
            }

            // The screen hides and shows itself while it loads a preview, so a single inactive
            // frame means nothing; only a screen that stays gone ends the session.
            if (!_screen.isActiveAndEnabled)
            {
                if (_inactiveSince <= 0f)
                {
                    _inactiveSince = Time.unscaledTime;
                }
                else if (Time.unscaledTime - _inactiveSince > InactiveGrace)
                {
                    Trace("detach", "the screen is gone, editor detached");
                    End();
                }

                return;
            }

            _inactiveSince = 0f;

            Trace("update", $"running: enabled={Enabled}, cells={_slots.Count}, " +
                            $"camera={(Camera() != null ? "ok" : "MISSING")}, " +
                            $"bone={(SelectedBone() != null ? "ok" : "MISSING")}");

            if (!Enabled)
            {
                _dragging = false;
                _gizmo.Hide();
                if (_highlighted)
                {
                    ClearHighlight();
                }

                return;
            }

            if (_slots.Count == 0)
            {
                return;
            }

            HandleMouse();
            UpdateDiagnostics();
            HandleKeys();
        }

        private void HandleKeys()
        {
            if (DevConfig.UndoShortcut.Value.IsDown())
            {
                Undo();
                return;
            }

            if (DevConfig.SaveShortcut.Value.IsDown())
            {
                Save();
                return;
            }

            if (DevConfig.CopyShortcut.Value.IsDown())
            {
                Copy();
                return;
            }

            if (DevConfig.PasteShortcut.Value.IsDown())
            {
                Paste();
                return;
            }

            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                return;
            }

            for (var position = 1; position <= ClusterGrid.CellsPerCluster; position++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + position - 1))
                {
                    Select(_cluster, position);
                    return;
                }
            }

            if (Input.GetKeyDown(DevConfig.NextCluster.Value))
            {
                StepCluster(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1);
                return;
            }

            if (Input.GetKeyDown(DevConfig.ResetKey.Value))
            {
                ResetSelected();
                return;
            }

            if (Input.GetKeyDown(DevConfig.SurfaceSnapToggle.Value))
            {
                ToggleSnap();
                return;
            }

            if (!_dragging)
            {
                HandleNudge();
            }
        }

        // --- editing ---

        private static BonePose ReadPose(Transform bone) => bone == null ? null : PouchBones.ReadPose(bone);

        /// <summary>Writes a cell's pose, as it stands, to the store.</summary>
        private void StorePose(Slot slot, Transform bone)
        {
            if (slot != null && bone != null)
            {
                Plugin.Bones.Set(_rig.StringTemplateId, slot.ID, PouchBones.ReadPose(bone));
            }
        }

        private void Copy()
        {
            if (_mountMode)
            {
                _status = "copy/paste works on cells, not on pouch mounts";
                return;
            }

            _clipboard = ReadPose(SelectedBone());
            _status = _clipboard != null ? $"copied {Label(SelectedSlot())}" : "nothing to copy";
        }

        private void Paste()
        {
            var bone = SelectedBone();
            if (_mountMode || bone == null || _clipboard == null)
            {
                _status = _mountMode ? "copy/paste works on cells, not on pouch mounts" : "nothing to paste";
                return;
            }

            BeginEdit("paste onto " + Label(SelectedSlot()));

            // pasted as it is: the source was already where it should be
            PouchBones.ApplyPose(bone, _clipboard, bone.parent);
            Commit(bone);
            _status = $"pasted onto {Label(SelectedSlot())}";
        }

        private void ResetSelected()
        {
            var slot = SelectedSlot();
            BeginEdit("reset of " + Label(slot));
            if (_mountMode)
            {
                ResetMount(slot);
                return;
            }

            var bone = SelectedBone();
            if (bone == null || bone.parent == null)
            {
                return;
            }

            Plugin.Bones.Remove(_rig.StringTemplateId, slot.ID);
            PouchBones.ApplyDefault(bone, _rig);
            _status = $"{Label(slot)} reset to default";

            // with the surface snap on, the default pose is put onto the surface as well
            if (SnapActive)
            {
                if (ReprojectBone(bone, Lift))
                {
                    Commit(bone);
                    _status += ", put on the surface";
                    return;
                }

                _status += ", NO SURFACE there - left in the air";
            }

            Reseat();
            Refresh();
        }

        /// <summary>A cell was edited: store it (and its mirror), re-seat the pouches, redraw.</summary>
        private void Commit(Transform target)
        {
            if (_mountMode)
            {
                StoreMount(SelectedSlot(), target);
            }
            else
            {
                StorePose(SelectedSlot(), target);
                if (DevConfig.LiveMirror.Value)
                {
                    MirrorCell(SelectedSlot());
                }
            }

            Reseat();
            Refresh();
        }

        private void Reseat()
        {
            try
            {
                PouchSeat.ReseatAll(_rig, ModelRoot);
            }
            catch (Exception ex)
            {
                DevPlugin.Log.LogWarning($"[ModularVests.DevTools] re-seat failed: {ex.Message}");
            }
        }

        private void Refresh()
        {
            try
            {
                _screen.UpdatePositions();
            }
            catch (Exception ex)
            {
                DevPlugin.Log.LogWarning($"[ModularVests.DevTools] bone editor: UpdatePositions failed: {ex.Message}");
            }

            Highlight();
        }

        private bool _highlighted;

        private void Highlight()
        {
            var bone = SelectedBone();
            if (bone == null || _screen == null)
            {
                return;
            }

            try
            {
                _screen.HighlightMod(bone, HighlightColor, overriding: true);
                _highlighted = true;
            }
            catch (Exception)
            {
                // no highlighter until the preview has finished loading; the next edit retries
            }
        }

        private void ClearHighlight()
        {
            _highlighted = false;
            try
            {
                _screen?.HideModHighlight(overriding: true);
            }
            catch (Exception)
            {
                // the screen is tearing down; the highlight goes with it
            }
        }

        private void SaveIfDirty()
        {
            if (Plugin.Bones.Dirty || (Plugin.Mounts != null && Plugin.Mounts.Dirty))
            {
                Save();
            }
        }

        private void Save()
        {
            try
            {
                Plugin.Bones.Save();
                Plugin.Mounts?.Save();
                _status = "saved " + System.IO.Path.GetFileName(Plugin.Bones.Path) + " and " +
                          System.IO.Path.GetFileName(Plugin.Mounts?.Path ?? "mounts.json");
                DevPlugin.Log.LogInfo($"[ModularVests.DevTools] layout saved to {Plugin.Bones.Path}");
            }
            catch (Exception ex)
            {
                _status = "SAVE FAILED: " + ex.Message;
                DevPlugin.Log.LogError($"[ModularVests.DevTools] layout save failed: {ex}");
            }
        }

        private static string Label(Slot slot) =>
            slot != null && ClusterGrid.TryParse(slot.ID, out var c, out var p)
                ? $"{Geometry.CellInfo.Label(c, p)} ({slot.ID})"
                : slot?.ID ?? "?";

        // --- logging (F12: Debug log) ---
        //
        // The editor lives on a screen the mod does not own, behind the game's own input and
        // cursor handling; when a grab does not happen there is nothing to see. These lines
        // say which step was reached.

        private const int TraceBudget = 40;

        private readonly HashSet<string> _logged = new HashSet<string>();
        private int _traced;

        private void LogOnce(string key, string message)
        {
            if (_logged.Add(key))
            {
                DevPlugin.Log.LogInfo(message);
            }
        }

        /// <summary>One line per key, per screen session.</summary>
        private void Trace(string key, string message)
        {
            if (DevConfig.EditorDebugLog.Value)
            {
                LogOnce("trace:" + key, "[ModularVests.DevTools] bone editor " + message);
            }
        }

        /// <summary>One line per event, until the budget for this screen session runs out.</summary>
        private void TraceCounted(string message)
        {
            if (DevConfig.EditorDebugLog.Value && _traced < TraceBudget)
            {
                _traced++;
                DevPlugin.Log.LogInfo("[ModularVests.DevTools] bone editor " + message);
            }
        }

        private Camera Camera()
        {
            try
            {
                return _screen != null ? _screen._viewporter.TargetCamera : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void OnDestroy()
        {
            _gizmo.Destroy();
        }
    }
}

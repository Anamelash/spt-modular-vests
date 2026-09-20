using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using ModularVests.Client;
using ModularVests.Client.Bones;
using ModularVests.DevTools.Geometry;
using UnityEngine;

namespace ModularVests.DevTools
{
    // The panel and what is drawn over the preview.
    //
    // Everything says which cell is which, so cells and clusters do not get mixed up: a label
    // "cluster.cell" over every bone in its cluster's colour, the outline 1-2-4-3 of each
    // cluster with cell 1 marked, the passport of the selected cell next to it, its cell frame
    // and the directions a pouch grows from it, and the bounding boxes of the attached pouches
    // (which ones: the "pouch boxes" switch on the panel).
    internal sealed partial class BoneEditor
    {
        private const float GizmoLength = 0.08f;

        private static readonly Color[] ClusterColors =
        {
            new Color(1f, 0.55f, 0.1f), new Color(0.2f, 0.85f, 1f), new Color(1f, 0.35f, 0.85f),
            new Color(0.55f, 1f, 0.3f), new Color(1f, 0.95f, 0.3f), new Color(0.6f, 0.55f, 1f),
        };

        private static readonly Color GrowRightColor = new Color(1f, 0.3f, 0.3f);
        private static readonly Color GrowDownColor = new Color(0.3f, 1f, 0.4f);
        private static readonly Color WarningColor = new Color(1f, 0.8f, 0.2f);

        private static readonly string[] PouchBoxesLabels = { "pouch", "+ mirror", "cluster", "all" };

        private Rect _window = new Rect(20f, 120f, 440f, 10f);
        private Texture2D _pixel;
        private readonly Dictionary<string, GUIStyle> _styles = new Dictionary<string, GUIStyle>();

        private static Color ClusterColor(int cluster) => ClusterColors[(cluster - 1 + ClusterColors.Length * 8) %
                                                                       ClusterColors.Length];

        private void OnGUI()
        {
            if (!Active)
            {
                return;
            }

            if (Event.current.type == EventType.Repaint)
            {
                DrawOverlay();
            }

            Trace("gui", "panel drawn");
            _window = GUILayout.Window(0x4D564245, _window, DrawWindow, "Pouch cell editor");
        }

        // --- overlay ---

        private void DrawOverlay()
        {
            var camera = Camera();
            if (camera == null)
            {
                return;
            }

            foreach (var cluster in _clusters)
            {
                DrawClusterOutline(camera, cluster);
            }

            foreach (var slot in BoxedPouches())
            {
                DrawPouchBox(camera, slot);
            }

            var selected = SelectedBone();
            if (selected != null && !_mountMode)
            {
                DrawCellFrame(camera, selected);
                DrawGrowth(camera, selected);
            }

            foreach (var slot in _slots)
            {
                DrawLabel(camera, slot, slot == SelectedSlot());
            }

            DrawGizmoAxes(camera);
        }

        private bool ToGui(Camera camera, Vector3 world, out Vector2 gui)
        {
            var screen = camera.WorldToScreenPoint(world);
            gui = new Vector2(screen.x, Screen.height - screen.y);
            return screen.z > 0f;
        }

        private void Line(Camera camera, Vector3 a, Vector3 b, Color color, float width)
        {
            if (ToGui(camera, a, out var ga) && ToGui(camera, b, out var gb))
            {
                DrawLine(ga, gb, color, width);
            }
        }

        /// <summary>Lines 1-2-4-3-1 between the bones of a cluster, cell 1 marked by a square.</summary>
        private void DrawClusterOutline(Camera camera, int cluster)
        {
            var color = ClusterColor(cluster);
            var faded = new Color(color.r, color.g, color.b, 0.75f);
            int[] order = { 1, 2, 4, 3, 1 };
            for (var i = 0; i + 1 < order.Length; i++)
            {
                var a = CellBone(cluster, order[i]);
                var b = CellBone(cluster, order[i + 1]);
                if (a != null && b != null)
                {
                    Line(camera, a.position, b.position, faded, 1.5f);
                }
            }

            var first = CellBone(cluster, 1);
            if (first != null && ToGui(camera, first.position, out var at))
            {
                const float half = 7f;
                DrawRect(new Rect(at.x - half, at.y - half, half * 2f, half * 2f), color, 2f);
            }
        }

        /// <summary>The cell's own rectangle: one cell size, in the bone's X/Y plane.</summary>
        private void DrawCellFrame(Camera camera, Transform bone)
        {
            DrawQuad(camera, bone.position, bone.rotation, CellWidth, CellHeight, new Color(1f, 1f, 1f, 0.85f), 1.5f);
        }

        /// <summary>Arrows the way a pouch grows from this cell: right as seen from the front, and down.</summary>
        private void DrawGrowth(Camera camera, Transform bone)
        {
            DrawArrow(camera, bone.position, bone.rotation * MeshSurface.V(CellInfo.GrowRight) * CellWidth,
                GrowRightColor);
            DrawArrow(camera, bone.position, bone.rotation * MeshSurface.V(CellInfo.GrowDown) * CellHeight,
                GrowDownColor);
        }

        /// <summary>The cells holding the pouches whose boxes are drawn (the panel's "pouch boxes" switch).</summary>
        private IEnumerable<Slot> BoxedPouches()
        {
            var shown = new HashSet<Slot>();
            switch (DevConfig.PouchBoxes.Value)
            {
                case PouchBoxesMode.Selected:
                    shown.Add(PouchAt(SelectedSlot()));
                    break;
                case PouchBoxesMode.SelectedAndMirror:
                    shown.Add(PouchAt(SelectedSlot()));
                    shown.Add(PouchAt(MirrorOf(SelectedSlot())));
                    break;
                case PouchBoxesMode.Cluster:
                    foreach (var slot in _slots)
                    {
                        if (PouchSlots.ClusterOf(slot) == _cluster)
                        {
                            shown.Add(PouchAt(slot));
                        }
                    }

                    break;
                default:
                    foreach (var slot in _slots)
                    {
                        shown.Add(PouchAt(slot));
                    }

                    break;
            }

            shown.Remove(null);
            return shown;
        }

        /// <summary>The cell whose pouch covers this cell: the cell itself, or the one blocking it.</summary>
        private static Slot PouchAt(Slot slot)
        {
            if (slot == null)
            {
                return null;
            }

            if (slot.ContainedItem != null)
            {
                return slot;
            }

            foreach (var blocker in slot.BlockerSlots)
            {
                if (blocker.ContainedItem != null && PouchSlots.IsPouchSlot(blocker))
                {
                    return blocker;
                }
            }

            return null;
        }

        /// <summary>
        /// The pouch model's bounding box - the meshes it is drawn with, in the model's own axes - so
        /// the real size and turn of the pouch show, half transparent in its cluster's colour.
        /// </summary>
        private void DrawPouchBox(Camera camera, Slot slot)
        {
            var view = PouchViewOf(slot);
            if (view == null || !LocalBounds(view, out var min, out var max))
            {
                return;
            }

            var color = ClusterColor(PouchSlots.ClusterOf(slot));
            color.a = 0.5f;
            var corners = new Vector3[8];
            for (var i = 0; i < 8; i++)
            {
                corners[i] = view.TransformPoint(new Vector3(
                    (i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
            }

            // the 12 edges: every pair of corners one bit apart
            for (var i = 0; i < 8; i++)
            {
                for (var bit = 1; bit < 8; bit <<= 1)
                {
                    if ((i & bit) == 0)
                    {
                        Line(camera, corners[i], corners[i | bit], color, 2f);
                    }
                }
            }
        }

        /// <summary>Bounds of every mesh under the model, in the model root's space.</summary>
        private static bool LocalBounds(Transform root, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var any = false;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                var b = mesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = root.InverseTransformPoint(filter.transform.TransformPoint(new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z)));
                    min = Vector3.Min(min, corner);
                    max = Vector3.Max(max, corner);
                    any = true;
                }
            }

            return any;
        }

        private void DrawQuad(Camera camera, Vector3 centre, Quaternion rotation, float width, float height, Color color,
            float lineWidth)
        {
            var x = rotation * Vector3.right * (width / 2f);
            var y = rotation * Vector3.up * (height / 2f);
            var corners = new[] { centre + x + y, centre - x + y, centre - x - y, centre + x - y };
            for (var i = 0; i < 4; i++)
            {
                Line(camera, corners[i], corners[(i + 1) % 4], color, lineWidth);
            }
        }

        private void DrawArrow(Camera camera, Vector3 from, Vector3 vector, Color color)
        {
            if (!ToGui(camera, from, out var a) || !ToGui(camera, from + vector, out var b))
            {
                return;
            }

            DrawLine(a, b, color, 2.5f);
            var direction = (b - a).normalized;
            if (direction.sqrMagnitude < 0.5f)
            {
                return;
            }

            var side = new Vector2(-direction.y, direction.x);
            DrawLine(b, b - direction * 10f + side * 5f, color, 2.5f);
            DrawLine(b, b - direction * 10f - side * 5f, color, 2.5f);
        }

        private void DrawLabel(Camera camera, Slot slot, bool selected)
        {
            var bone = BoneOf(slot);
            if (bone == null || !ClusterGrid.TryParse(slot.ID, out var cluster, out var position) ||
                !ToGui(camera, bone.position, out var at))
            {
                return;
            }

            var color = ClusterColor(cluster);
            var text = CellInfo.Label(cluster, position);
            var style = Style(color, selected ? 17 : 12);
            var size = style.CalcSize(new GUIContent(text));
            var rect = new Rect(at.x + 6f, at.y - size.y - 4f, size.x, size.y);
            if (selected)
            {
                var box = new Rect(rect.x - 3f, rect.y - 1f, rect.width + 6f, rect.height + 2f);
                Fill(box, new Color(0f, 0f, 0f, 0.6f));
                DrawRect(box, color, 1.5f);
            }

            Shadowed(rect, text, style);

            if (selected)
            {
                var passport = CellInfo.Passport(slot.ID);
                var small = Style(Color.white, 11);
                var passportSize = small.CalcSize(new GUIContent(passport));
                var passportRect = new Rect(at.x + 6f, at.y + 6f, passportSize.x, passportSize.y);
                Fill(new Rect(passportRect.x - 2f, passportRect.y, passportRect.width + 4f, passportRect.height),
                    new Color(0f, 0f, 0f, 0.55f));
                Shadowed(passportRect, passport, small);
            }
        }

        /// <summary>The gizmo's axes over the model; the constrained one drawn long and thick.</summary>
        private void DrawGizmoAxes(Camera camera)
        {
            var target = EditTarget();
            if (target == null || target.parent == null)
            {
                return;
            }

            var active = _dragging ? _dragAxis : HeldAxis();
            DrawAxis(camera, target, Axis.X, Color.red, active);
            DrawAxis(camera, target, Axis.Y, Color.green, active);
            if (!SnapActive)
            {
                DrawAxis(camera, target, Axis.Z, new Color(0.3f, 0.5f, 1f), active);
            }
        }

        private void DrawAxis(Camera camera, Transform target, Axis axis, Color color, Axis active)
        {
            var direction = AxisWorld(target, axis).normalized;
            var length = axis == active ? GizmoLength * 4f : GizmoLength;
            var from = target.position - (axis == active ? direction * length : Vector3.zero);
            Line(camera, from, target.position + direction * length,
                axis == active ? color : new Color(color.r, color.g, color.b, 0.6f), axis == active ? 3f : 2f);
        }

        // --- drawing primitives ---

        private Texture2D Pixel()
        {
            if (_pixel == null)
            {
                _pixel = new Texture2D(1, 1);
                _pixel.SetPixel(0, 0, Color.white);
                _pixel.Apply();
            }

            return _pixel;
        }

        private void DrawLine(Vector2 a, Vector2 b, Color color, float width)
        {
            var delta = b - a;
            var length = delta.magnitude;
            if (length < 1f)
            {
                return;
            }

            var matrix = GUI.matrix;
            var previous = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, length, width), Pixel());
            GUI.matrix = matrix;
            GUI.color = previous;
        }

        private void DrawRect(Rect rect, Color color, float width)
        {
            DrawLine(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), color, width);
            DrawLine(new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), color, width);
            DrawLine(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), color, width);
            DrawLine(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin), color, width);
        }

        private void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Pixel());
            GUI.color = previous;
        }

        private GUIStyle Style(Color color, int size)
        {
            var key = $"{color}:{size}";
            if (!_styles.TryGetValue(key, out var style))
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, wordWrap = false };
                style.normal.textColor = color;
                _styles[key] = style;
            }

            return style;
        }

        private void Shadowed(Rect rect, string text, GUIStyle style)
        {
            var shadow = Style(Color.black, style.fontSize);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, shadow);
            GUI.Label(rect, text, style);
        }

        // --- the panel ---

        private void DrawWindow(int id)
        {
            GUILayout.Label(_rig.ShortName.Localized() +
                            (Plugin.Bones.Dirty || (Plugin.Mounts?.Dirty ?? false) ? "  (unsaved)" : ""));


            GUILayout.BeginHorizontal();
            var snap = GUILayout.Toggle(DevConfig.SurfaceSnap.Value, $" surface snap ({DevConfig.SurfaceSnapToggle.Value})");
            if (snap != DevConfig.SurfaceSnap.Value)
            {
                ToggleSnap();
            }

            GUILayout.Label(SnapLabel());
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("pouch boxes:", GUILayout.Width(80f));
            var boxes = (PouchBoxesMode)GUILayout.Toolbar((int)DevConfig.PouchBoxes.Value, PouchBoxesLabels);
            if (boxes != DevConfig.PouchBoxes.Value)
            {
                DevConfig.PouchBoxes.Value = boxes;
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DevConfig.LiveMirror.Value = GUILayout.Toggle(DevConfig.LiveMirror.Value, " live mirror");
            var mount = GUILayout.Toggle(_mountMode, " pouch mount");
            if (mount != _mountMode)
            {
                ToggleMountMode();
            }

            GUILayout.EndHorizontal();

            // the selected cell
            var slot = SelectedSlot();
            GUILayout.Space(4f);
            GUILayout.Label(slot != null ? CellInfo.Passport(slot.ID) : "no cell selected", Wrapped());
            var target = EditTarget();
            if (_mountMode)
            {
                GUILayout.Label(target != null
                    ? $"mount of {slot.ContainedItem.ShortName.Localized()} ({slot.ContainedItem.StringTemplateId})"
                    : "no pouch in this cell to mount");
            }

            if (target != null)
            {
                var p = target.localPosition;
                var r = target.localEulerAngles;
                GUILayout.Label($"pos  {p.x:0.0000}  {p.y:0.0000}  {p.z:0.0000}   " +
                                $"rot  {Angle(r.x):0.0}  {Angle(r.y):0.0}  {Angle(r.z):0.0}");
            }

            // tools
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Mirror C{_cluster} → C{ClusterGrid.MirrorCluster(_cluster)}"))
            {
                BeginEdit($"mirror C{_cluster}");
                MirrorCluster();
            }

            if (GUILayout.Button($"Arrange C{_cluster} from cell 1"))
            {
                BeginEdit($"arrange C{_cluster}");
                ArrangeCluster();
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Arrange pairs (symmetric)"))
            {
                BeginEdit("arrange pairs");
                ArrangePairs();
            }

            if (GUILayout.Button("Restore", GUILayout.Width(80f)))
            {
                BeginEdit("restore");
                RestoreBackup();
            }

            if (GUILayout.Button($"Undo ({_undo.Count})", GUILayout.Width(80f)))
            {
                Undo();
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy layout"))
            {
                CopyLayout();
            }

            GUI.enabled = _layoutClipboard != null;
            if (GUILayout.Button(_layoutClipboard != null ? $"Paste layout of {_layoutClipboard.Source}" : "Paste layout"))
            {
                BeginEdit("paste layout");
                PasteLayout();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Project:", GUILayout.Width(55f));
            if (GUILayout.Button("cell"))
            {
                BeginEdit("project cell");
                Project(new[] { slot });
            }

            if (GUILayout.Button($"C{_cluster}"))
            {
                var cells = new List<Slot>();
                for (var position = 1; position <= ClusterGrid.CellsPerCluster; position++)
                {
                    cells.Add(CellSlot(_cluster, position));
                }

                BeginEdit($"project C{_cluster}");
                Project(cells.FindAll(s => s != null));
            }

            if (GUILayout.Button("all"))
            {
                BeginEdit("project all");
                Project(_slots);
            }

            GUILayout.EndHorizontal();

            // what looks wrong
            if (_warnings.Count > 0)
            {
                var previous = GUI.contentColor;
                GUI.contentColor = WarningColor;
                foreach (var warning in _warnings)
                {
                    GUILayout.Label("! " + warning, Wrapped());
                }

                GUI.contentColor = previous;
            }

            // the cells, cluster by cluster, laid out the way they read
            GUILayout.Space(4f);
            for (var i = 0; i < _clusters.Count; i += 2)
            {
                GUILayout.BeginHorizontal();
                DrawClusterCells(_clusters[i]);
                if (i + 1 < _clusters.Count)
                {
                    DrawClusterCells(_clusters[i + 1]);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4f);
            GUILayout.Label(
                "drag the gizmo or a dot   right-click: reset   1-4: cell   " +
                $"{DevConfig.NextCluster.Value} / Shift+{DevConfig.NextCluster.Value}: cluster\n" +
                $"hold {DevConfig.AxisXModifier.Value}/{DevConfig.AxisYModifier.Value}/" +
                $"{DevConfig.AxisZModifier.Value}: lock axis   hold {DevConfig.RotateModifier.Value}: rotate   " +
                $"hold {DevConfig.FineModifier.Value}: fine\n" +
                $"axis + {DevConfig.NudgeMinus.Value}/{DevConfig.NudgePlus.Value}: nudge   " +
                $"{DevConfig.ResetKey.Value}: reset   {DevConfig.SaveShortcut.Value}: save   " +
                $"{DevConfig.CopyShortcut.Value} / {DevConfig.PasteShortcut.Value}: copy / paste", Wrapped());

            if (!string.IsNullOrEmpty(_status))
            {
                GUILayout.Label(_status, Wrapped());
            }

            GUI.DragWindow();
        }

        /// <summary>A cluster as a 2x2 block of buttons, in its colour, with what each cell holds.</summary>
        private void DrawClusterCells(int cluster)
        {
            var tpl = _rig.StringTemplateId;
            var previous = GUI.contentColor;
            GUILayout.BeginVertical(GUILayout.Width(205f));
            GUI.contentColor = ClusterColor(cluster);
            GUILayout.Label($"C{cluster} {CellInfo.ClusterName(cluster)}");
            for (var row = 0; row < ClusterGrid.Rows; row++)
            {
                GUILayout.BeginHorizontal();
                for (var column = 0; column < ClusterGrid.Columns; column++)
                {
                    var position = ClusterGrid.PositionAt(column, row);
                    var slot = CellSlot(cluster, position);
                    if (slot == null)
                    {
                        GUILayout.Label("-", GUILayout.Width(98f));
                        continue;
                    }

                    var stored = Plugin.Bones.TryGet(tpl, slot.ID, out _);
                    var content = slot.ContainedItem != null ? slot.ContainedItem.ShortName.Localized() : "empty";
                    var selected = cluster == _cluster && position == _position;
                    var label = $"{(selected ? "> " : "")}{CellInfo.Label(cluster, position)} {content}{(stored ? "" : " *")}";
                    if (GUILayout.Button(label, GUILayout.Width(98f)))
                    {
                        Select(cluster, position);
                    }
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUI.contentColor = previous;
        }

        private GUIStyle _wrapped;

        private GUIStyle Wrapped()
        {
            if (_wrapped == null)
            {
                _wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true };
            }

            return _wrapped;
        }
    }
}

using ModularVests.DevTools.Geometry;
using UnityEngine;

namespace ModularVests.DevTools
{
    // Mouse and keyboard editing.
    //
    // The mouse is polled here, and the press/release edges are derived from
    // Input.GetMouseButton by hand: on this screen GetMouseButtonDown/Up do not fire
    // (SPT.WeaponCamoAndStickers hit the same wall and does the same). The edited transform
    // carries a gizmo of real colliders, so a grab is a physics raycast; clicking another cell's
    // bone selects it. The preview's own drag-rotation is held off while dragging
    // (BoneEditorPatch blocks WeaponPreview.Rotate).
    internal sealed partial class BoneEditor
    {
        private const float PickRadius = 18f;
        private const float DragThreshold = 3f;
        private const float RepeatDelay = 0.35f;
        private const float RepeatInterval = 0.05f;

        /// <summary>Longest step of a slide along the surface, metres: short enough to follow a curve.</summary>
        private const float SlideStep = 0.004f;

        private KeyCode _heldKey = KeyCode.None;
        private float _nextRepeat;

        // drag state
        private bool _dragging;
        private bool _dragMoved;
        private Vector2 _dragPressScreen;
        private Axis _dragAxis;
        private bool _dragRotating;
        private bool _dragFine;
        private Vector2 _dragStartScreen;
        private Vector3 _dragStartLocal;
        private Quaternion _dragStartRotation;
        private Vector3 _dragStartWorld;
        private Vector3 _dragAxisWorld;
        private Plane _dragPlane;
        private Vector3 _dragGrabOffset;
        private Vector2 _dragScreenOffset;
        private float _dragStartT;
        private float _slid;
        private Axis _grabAxis;
        private bool _leftWasDown;
        private bool _rightWasDown;
        private readonly BoneGizmo _gizmo = new BoneGizmo();

        /// <summary>True while something is being dragged.</summary>
        public bool Dragging => _dragging;

        /// <summary>
        /// The frame the gizmo's arms follow: with the surface snap, the bone's own axes (the
        /// arms slide along the surface); otherwise the axes of what the target hangs from.
        /// </summary>
        private Quaternion EditFrame(Transform target) =>
            SnapActive ? target.rotation : target.parent != null ? target.parent.rotation : target.rotation;

        private Vector3 AxisWorld(Transform target, Axis axis) => EditFrame(target) * AxisVector(axis);

        private void HandleMouse()
        {
            var camera = Camera();
            if (camera == null)
            {
                _dragging = false;
                return;
            }

            Vector2 mouse = Input.mousePosition;
            var leftDown = Input.GetMouseButton(0);
            var rightDown = Input.GetMouseButton(1);
            var leftPressed = leftDown && !_leftWasDown;
            var rightPressed = rightDown && !_rightWasDown;
            _leftWasDown = leftDown;
            _rightWasDown = rightDown;

            var target = EditTarget();
            if (target != null)
            {
                _gizmo.Show(target, camera, EditFrame(target), showZ: !SnapActive);
            }
            else
            {
                _gizmo.Hide();
            }

            if (_dragging)
            {
                if (leftDown)
                {
                    Drag(mouse);
                }
                else
                {
                    _dragging = false;
                    _grabAxis = Axis.None;
                    if (!_dragMoved)
                    {
                        _status = $"selected {Label(SelectedSlot())}";
                    }
                }

                return;
            }

            if (!leftPressed && !rightPressed)
            {
                return;
            }

            // the editor panel takes its own clicks
            var guiMouse = new Vector2(mouse.x, Screen.height - mouse.y);
            if (_window.Contains(guiMouse))
            {
                return;
            }

            var ray = camera.ScreenPointToRay(mouse);
            var part = target != null ? _gizmo.Pick(ray) : GizmoPart.None;
            TraceCounted($"press: button={(leftPressed ? "left" : "right")}, mouse={mouse}, " +
                         $"cursor(visible={Cursor.visible}, lock={Cursor.lockState}), gizmo={part}, " +
                         $"gizmoBuilt={_gizmo.Exists}, nearestDot={NearestDotDistance(camera, mouse):0.#}px");
            if (part == GizmoPart.None)
            {
                var picked = PickBone(camera, mouse);
                if (picked == null)
                {
                    return;
                }

                if (picked != SelectedSlot())
                {
                    // another cell: switch to it, and edit it on the next press
                    Select(picked);
                    if (rightPressed)
                    {
                        ResetSelected();
                    }

                    return;
                }

                if (target == null)
                {
                    _status = _mountMode ? $"{Label(picked)} has no pouch to mount" : "";
                    return;
                }

                // the selected cell's own dot: grab it even when the gizmo was missed
                part = GizmoPart.Centre;
            }

            if (rightPressed)
            {
                ResetSelected();
                return;
            }

            _grabAxis = AxisOf(part);
            BeginEdit((_mountMode ? "mount of " : "drag of ") + Label(SelectedSlot()));
            _dragging = true;
            _dragMoved = false;
            _dragPressScreen = mouse;

            // grabbed off-centre, the target keeps its distance to the cursor instead of jumping under it
            var at = camera.WorldToScreenPoint(target.position);
            _dragScreenOffset = new Vector2(at.x, at.y) - mouse;
            Anchor(mouse);
            LogOnce("drag", $"[ModularVests.DevTools] bone editor: dragging {Label(SelectedSlot())} by {part}");
        }

        private static Axis AxisOf(GizmoPart part)
        {
            switch (part)
            {
                case GizmoPart.AxisX: return Axis.X;
                case GizmoPart.AxisY: return Axis.Y;
                case GizmoPart.AxisZ: return Axis.Z;
                default: return Axis.None;
            }
        }

        /// <summary>The axis of this stroke: the grabbed arm, or the modifier key.</summary>
        private Axis EffectiveAxis() => _grabAxis != Axis.None ? _grabAxis : HeldAxis();

        /// <summary>The cell whose bone is closest to the cursor on screen, within the pick radius.</summary>
        private EFT.InventoryLogic.Slot PickBone(Camera camera, Vector2 mouse)
        {
            EFT.InventoryLogic.Slot best = null;
            var bestDistance = PickRadius;
            foreach (var slot in _slots)
            {
                var bone = BoneOf(slot);
                if (bone == null)
                {
                    continue;
                }

                var screen = camera.WorldToScreenPoint(bone.position);
                if (screen.z <= 0f)
                {
                    continue;
                }

                var distance = Vector2.Distance(mouse, screen);
                if (distance < bestDistance)
                {
                    best = slot;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private void Drag(Vector2 screenPosition)
        {
            var target = EditTarget();
            var camera = Camera();
            if (target == null || camera == null)
            {
                _dragging = false;
                return;
            }

            // a click is not an edit: nothing moves until the cursor does
            if (!_dragMoved)
            {
                if (Vector2.Distance(screenPosition, _dragPressScreen) < DragThreshold)
                {
                    return;
                }

                _dragMoved = true;
            }

            // a modifier pressed or released mid-drag starts a new stroke from here
            if (EffectiveAxis() != _dragAxis || RotateHeld() != _dragRotating || FineHeld() != _dragFine)
            {
                Anchor(screenPosition);
                return;
            }

            var factor = _dragFine ? DevConfig.FineFactor.Value : 1f;
            var parent = target.parent;

            if (_dragRotating)
            {
                var angle = (screenPosition.x - _dragStartScreen.x) / Screen.width * 360f * factor;
                angle = Snap(angle, DevConfig.RotateSnap.Value);
                Vector3 axis;
                if (SnapActive)
                {
                    // on the surface only the roll is free: the tilt belongs to the surface
                    axis = _dragStartRotation * Vector3.forward;
                }
                else if (_dragAxis == Axis.None)
                {
                    axis = parent.InverseTransformDirection(camera.transform.forward).normalized;
                }
                else
                {
                    axis = parent.InverseTransformDirection(_dragAxisWorld).normalized;
                }

                target.localRotation = Quaternion.AngleAxis(angle, axis) * _dragStartRotation;
            }
            else if (SnapActive)
            {
                if (!DragOnSurface(target, camera, screenPosition, factor))
                {
                    return;
                }
            }
            else if (_dragAxis == Axis.None)
            {
                var ray = camera.ScreenPointToRay(screenPosition);
                if (!_dragPlane.Raycast(ray, out var distance))
                {
                    return;
                }

                var point = parent.InverseTransformPoint(ray.GetPoint(distance) + _dragGrabOffset);
                var delta = (point - _dragStartLocal) * factor;
                var snap = DevConfig.MoveSnap.Value;
                target.localPosition = _dragStartLocal +
                                       new Vector3(Snap(delta.x, snap), Snap(delta.y, snap), Snap(delta.z, snap));
            }
            else
            {
                var t = ClosestAlongAxis(_dragStartWorld, _dragAxisWorld, camera.ScreenPointToRay(screenPosition));
                var distance = Snap((t - _dragStartT) * factor, DevConfig.MoveSnap.Value);
                target.localPosition = _dragStartLocal + parent.InverseTransformVector(_dragAxisWorld * distance);
            }

            Commit(target);
        }

        /// <summary>
        /// A drag with the surface snap on. The centre follows the surface under the cursor (a
        /// miss leaves the bone on its last hit); the X/Y arms slide along the surface; Z does
        /// nothing. False when nothing moved.
        /// </summary>
        private bool DragOnSurface(Transform bone, Camera camera, Vector2 screenPosition, float factor)
        {
            switch (_dragAxis)
            {
                case Axis.None:
                {
                    var aim = _dragPressScreen + (screenPosition - _dragPressScreen) * factor + _dragScreenOffset;
                    if (!Surface().Raycast(camera.ScreenPointToRay(aim), 100f, out var hit))
                    {
                        _snapState = "no surface";
                        return false;
                    }

                    _snapState = "hit";
                    PlaceOnSurface(bone, hit);
                    return true;
                }

                case Axis.Z:
                    _status = "Z is locked while the surface snap is on (the gap is the surface offset setting)";
                    return false;

                default:
                {
                    var t = ClosestAlongAxis(_dragStartWorld, _dragAxisWorld, camera.ScreenPointToRay(screenPosition));
                    var distance = Snap((t - _dragStartT) * factor, DevConfig.MoveSnap.Value);
                    var step = distance - _slid;
                    if (Mathf.Abs(step) < 1e-6f)
                    {
                        return false;
                    }

                    if (!SlideBone(bone, _dragAxis, step))
                    {
                        _snapState = "no surface";
                        return false;
                    }

                    _snapState = "hit";
                    _slid = distance;
                    return true;
                }
            }
        }

        /// <summary>Distance to the closest bone dot on screen, whatever the pick radius.</summary>
        private float NearestDotDistance(Camera camera, Vector2 mouse)
        {
            var best = float.NaN;
            foreach (var slot in _slots)
            {
                var bone = BoneOf(slot);
                if (bone == null)
                {
                    continue;
                }

                var distance = Vector2.Distance(mouse, camera.WorldToScreenPoint(bone.position));
                if (float.IsNaN(best) || distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        private void Anchor(Vector2 screenPosition)
        {
            var target = EditTarget();
            var camera = Camera();
            if (target == null || camera == null)
            {
                _dragging = false;
                return;
            }

            _dragAxis = EffectiveAxis();
            _dragRotating = RotateHeld();
            _dragFine = FineHeld();
            _dragStartScreen = screenPosition;
            _dragStartLocal = target.localPosition;
            _dragStartRotation = target.localRotation;
            _dragStartWorld = target.position;
            _slid = 0f;

            var ray = camera.ScreenPointToRay(screenPosition);
            _dragPlane = new Plane(-camera.transform.forward, target.position);
            _dragGrabOffset = _dragPlane.Raycast(ray, out var distance)
                ? target.position - ray.GetPoint(distance)
                : Vector3.zero;

            if (_dragAxis != Axis.None)
            {
                _dragAxisWorld = AxisWorld(target, _dragAxis).normalized;
                _dragStartT = ClosestAlongAxis(target.position, _dragAxisWorld, ray);
            }
        }

        /// <summary>Parameter of the point on the axis line (origin, direction) closest to the ray.</summary>
        private static float ClosestAlongAxis(Vector3 origin, Vector3 direction, Ray ray)
        {
            var w = origin - ray.origin;
            var b = Vector3.Dot(direction, ray.direction);
            var d = Vector3.Dot(direction, w);
            var e = Vector3.Dot(ray.direction, w);
            var denominator = 1f - b * b;
            if (Mathf.Abs(denominator) < 1e-5f)
            {
                return 0f; // looking straight down the axis
            }

            return (b * e - d) / denominator;
        }

        private static float Snap(float value, float step) =>
            step > 0f ? Mathf.Round(value / step) * step : value;

        private static Vector3 AxisVector(Axis axis)
        {
            switch (axis)
            {
                case Axis.X: return Vector3.right;
                case Axis.Y: return Vector3.up;
                case Axis.Z: return Vector3.forward;
                default: return Vector3.zero;
            }
        }

        private static Axis HeldAxis()
        {
            if (Input.GetKey(DevConfig.AxisXModifier.Value))
            {
                return Axis.X;
            }

            if (Input.GetKey(DevConfig.AxisYModifier.Value))
            {
                return Axis.Y;
            }

            return Input.GetKey(DevConfig.AxisZModifier.Value) ? Axis.Z : Axis.None;
        }

        private static bool RotateHeld() => Input.GetKey(DevConfig.RotateModifier.Value);

        private static bool FineHeld() => Input.GetKey(DevConfig.FineModifier.Value);

        // --- nudge keys ---

        private void HandleNudge()
        {
            float sign;
            if (Pressed(DevConfig.NudgePlus.Value))
            {
                sign = 1f;
            }
            else if (Pressed(DevConfig.NudgeMinus.Value))
            {
                sign = -1f;
            }
            else
            {
                if (!Input.GetKey(DevConfig.NudgePlus.Value) && !Input.GetKey(DevConfig.NudgeMinus.Value))
                {
                    _heldKey = KeyCode.None;
                }

                return;
            }

            var axis = HeldAxis();
            var target = EditTarget();
            if (target == null)
            {
                return;
            }

            if (Input.GetKeyDown(DevConfig.NudgePlus.Value) || Input.GetKeyDown(DevConfig.NudgeMinus.Value))
            {
                BeginEdit("nudge of " + Label(SelectedSlot()));
            }

            if (axis == Axis.None)
            {
                _status = $"hold {DevConfig.AxisXModifier.Value}/{DevConfig.AxisYModifier.Value}/" +
                          $"{DevConfig.AxisZModifier.Value} to pick the nudge axis";
                return;
            }

            var factor = sign * (FineHeld() ? DevConfig.FineFactor.Value : 1f);
            if (RotateHeld())
            {
                var around = SnapActive
                    ? Vector3.forward
                    : Quaternion.Inverse(target.localRotation) *
                      target.parent.InverseTransformDirection(AxisWorld(target, axis));
                target.localRotation *= Quaternion.AngleAxis(DevConfig.RotateStep.Value * factor, around);
            }
            else if (SnapActive)
            {
                if (axis == Axis.Z)
                {
                    _status = "Z is locked while the surface snap is on";
                    return;
                }

                var step = Snap(DevConfig.MoveStep.Value * factor, DevConfig.MoveSnap.Value);
                if (!SlideBone(target, axis, step))
                {
                    _snapState = "no surface";
                    _status = "no surface that way - the step was not made";
                    return;
                }

                _snapState = "hit";
            }
            else
            {
                target.localPosition += target.parent.InverseTransformVector(
                    AxisWorld(target, axis).normalized * (DevConfig.MoveStep.Value * factor));
            }

            Commit(target);
        }

        /// <summary>Key down, or held past the repeat delay.</summary>
        private bool Pressed(KeyCode key)
        {
            if (Input.GetKeyDown(key))
            {
                _heldKey = key;
                _nextRepeat = Time.unscaledTime + RepeatDelay;
                return true;
            }

            if (_heldKey == key && Input.GetKey(key) && Time.unscaledTime >= _nextRepeat)
            {
                _nextRepeat = Time.unscaledTime + RepeatInterval;
                return true;
            }

            return false;
        }

        /// <summary>Slides a bone along its own X or Y axis over the surface; false (and no move) on a miss.</summary>
        private bool SlideBone(Transform bone, Axis axis, float distance)
        {
            var offset = DevConfig.SurfaceOffset.Value;
            var point = MeshSurface.P(bone.position - bone.forward * offset);
            var normal = MeshSurface.P(bone.forward);
            var tangent = MeshSurface.P(axis == Axis.X ? bone.right : bone.up);
            if (!SurfaceMath.Slide(Surface(), ref point, ref normal, tangent, distance, SlideStep, Lift))
            {
                return false;
            }

            PlaceOnSurface(bone, new SurfaceHit { Point = point, Normal = normal, GeometricNormal = normal });
            return true;
        }
    }
}

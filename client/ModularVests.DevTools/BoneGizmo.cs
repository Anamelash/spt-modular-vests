using UnityEngine;

namespace ModularVests.DevTools
{
    /// <summary>Which part of the gizmo a ray hit.</summary>
    internal enum GizmoPart
    {
        None,
        Centre,
        AxisX,
        AxisY,
        AxisZ,
    }

    /// <summary>Marks a gizmo part so a raycast hit can be mapped back to it.</summary>
    internal sealed class GizmoHandle : MonoBehaviour
    {
        public GizmoPart Part;
    }

    /// <summary>
    /// The grab handles of the edited transform: three axis arms and a centre block, as solid
    /// colliders in the preview scene.
    ///
    /// Picking is a physics raycast against those colliders on the game's Weapon Preview
    /// layer, the way SPT.WeaponCamoAndStickers does it, rather than a hit test against the
    /// UI dots: the dots are not reliably raycast targets, and a collider also gives the
    /// cursor something with depth to grab.
    /// </summary>
    internal sealed class BoneGizmo
    {
        /// <summary>Arm length and thickness, in gizmo units (scaled by camera distance).</summary>
        private const float ArmLength = 2f;
        private const float ArmThickness = 0.16f;
        private const float CentreSize = 0.35f;

        /// <summary>Gizmo size as a fraction of its distance to the camera.</summary>
        private const float ScaleFactor = 1f / 30f;

        private GameObject _root;
        private GameObject _zArm;
        private Transform _target;

        public bool Exists => _root != null;

        /// <summary>
        /// Puts the gizmo on the edited transform (creating it on first use), its arms along
        /// <paramref name="frame"/>, and scales it to the view. <paramref name="showZ"/> false
        /// hides the Z arm (surface snap: nothing may lift a bone off the surface).
        /// </summary>
        public void Show(Transform target, Camera camera, Quaternion frame, bool showZ = true)
        {
            if (target == null || camera == null)
            {
                Hide();
                return;
            }

            if (_root == null)
            {
                Build();
            }

            if (_target != target)
            {
                _target = target;
                _root.transform.SetParent(target.parent, worldPositionStays: false);
            }

            var transform = _root.transform;
            transform.position = target.position;
            transform.rotation = frame;
            transform.localScale = Vector3.one;
            var lossy = transform.lossyScale.x;
            var size = Vector3.Distance(camera.transform.position, target.position) * ScaleFactor;
            transform.localScale = Vector3.one * (lossy > 1e-6f ? size / lossy : size);

            if (_zArm != null && _zArm.activeSelf != showZ)
            {
                _zArm.SetActive(showZ);
            }

            if (!_root.activeSelf)
            {
                _root.SetActive(true);
            }
        }

        public void Hide()
        {
            if (_root != null && _root.activeSelf)
            {
                _root.SetActive(false);
            }
        }

        public void Destroy()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
                _zArm = null;
                _target = null;
            }
        }

        /// <summary>The part of the gizmo the ray hits, if any.</summary>
        public GizmoPart Pick(Ray ray)
        {
            if (_root == null || !_root.activeSelf)
            {
                return GizmoPart.None;
            }

            // BSG turns off automatic transform syncing, so colliders sit where they were
            // last synced unless we ask for it (the gizmo moves every frame)
            if (!Physics.autoSyncTransforms)
            {
                Physics.SyncTransforms();
            }

            var hits = Physics.RaycastNonAlloc(ray, Hits, maxDistance: 100f, layerMask: 1 << PreviewLayer);
            var best = GizmoPart.None;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < hits; i++)
            {
                var handle = Hits[i].collider.GetComponentInParent<GizmoHandle>();
                if (handle != null && Hits[i].distance < bestDistance)
                {
                    best = handle.Part;
                    bestDistance = Hits[i].distance;
                }
            }

            return best;
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        private static int PreviewLayer => LayersMaskController.WeaponPreview;

        private void Build()
        {
            _root = new GameObject("ModularVests.BoneGizmo");
            _root.SetActive(false);

            AddPart(GizmoPart.Centre, Vector3.zero, Quaternion.identity,
                Vector3.one * CentreSize, new Color(1f, 0.85f, 0.2f));
            AddPart(GizmoPart.AxisX, Vector3.right * (ArmLength / 2f), Quaternion.Euler(0f, 0f, -90f),
                new Vector3(ArmThickness, ArmLength, ArmThickness), Color.red);
            AddPart(GizmoPart.AxisY, Vector3.up * (ArmLength / 2f), Quaternion.identity,
                new Vector3(ArmThickness, ArmLength, ArmThickness), Color.green);
            _zArm = AddPart(GizmoPart.AxisZ, Vector3.forward * (ArmLength / 2f), Quaternion.Euler(90f, 0f, 0f),
                new Vector3(ArmThickness, ArmLength, ArmThickness), new Color(0.3f, 0.5f, 1f));

            TransformTools.SetLayersRecursively(_root, PreviewLayer);
        }

        private GameObject AddPart(GizmoPart part, Vector3 localPosition, Quaternion localRotation, Vector3 localScale,
            Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = part.ToString();
            go.AddComponent<GizmoHandle>().Part = part;

            var transform = go.transform;
            transform.SetParent(_root.transform, worldPositionStays: false);
            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
            transform.localScale = localScale;

            // The game ships no material the mod may borrow, so the arms are usually
            // invisible: the editor draws the axes itself on top of the screen. Colour them
            // anyway for the builds where a colour shader is around.
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default") ??
                    Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    renderer.material = new Material(shader) { color = color };
                }
                else
                {
                    renderer.enabled = false;
                }
            }

            return go;
        }
    }
}

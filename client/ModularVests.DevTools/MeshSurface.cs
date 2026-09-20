using System.Collections.Generic;
using ModularVests.Client.Bones;
using ModularVests.DevTools.Geometry;
using UnityEngine;

namespace ModularVests.DevTools
{
    /// <summary>
    /// The surface of the rig in the preview, in world space: the front faces of its meshes
    /// (<see cref="MeshGeometry"/>), each taken through its transform as it stands at the moment
    /// of the ray, so a preview turned or a model rebuilt between rays is still hit correctly.
    ///
    /// The model's mesh filters with an enabled renderer (hidden LODs are left out), except
    /// anything hanging from a pouch bone (the pouches themselves - a bone must not stick to its
    /// own pouch) and the gizmo.
    /// </summary>
    internal sealed class MeshSurface : ISurface
    {
        private struct Part
        {
            public Transform Transform;
            public TriangleSurface Geometry;
        }

        private readonly List<Part> _parts = new List<Part>();

        /// <summary>Why the surface is unusable, or null.</summary>
        public string Error { get; private set; }

        public int Triangles { get; private set; }

        public bool Usable => _parts.Count > 0;

        public static MeshSurface ForLoot(Transform root)
        {
            var surface = new MeshSurface();
            var errors = new List<string>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(includeInactive: false))
            {
                var renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled || filter.GetComponentInParent<PouchBoneMarker>() != null ||
                    filter.GetComponentInParent<GizmoHandle>() != null)
                {
                    continue;
                }

                surface.Add(filter.transform, filter.sharedMesh, errors);
            }

            surface.Finish(errors, "the model has no visible meshes");
            return surface;
        }


        private void Add(Transform transform, Mesh mesh, List<string> errors)
        {
            var geometry = MeshGeometry.Get(mesh, out var error);
            if (geometry == null)
            {
                errors.Add($"'{(mesh != null ? mesh.name : transform.name)}': {error}");
                return;
            }

            _parts.Add(new Part { Transform = transform, Geometry = geometry });
            Triangles += geometry.TriangleCount;
        }

        private void Finish(List<string> errors, string nothing)
        {
            if (_parts.Count == 0)
            {
                Error = errors.Count > 0 ? string.Join("; ", errors.ToArray()) : nothing;
            }
            else if (errors.Count > 0)
            {
                DevPlugin.Log.LogWarning("[ModularVests.DevTools] surface: some meshes are left out: " +
                                         string.Join("; ", errors.ToArray()));
            }
        }


        public bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out SurfaceHit hit)
        {
            hit = default;
            var found = false;
            foreach (var part in _parts)
            {
                if (part.Transform == null)
                {
                    continue;
                }

                var toLocal = part.Transform.worldToLocalMatrix;
                var localOrigin = toLocal.MultiplyPoint3x4(V(origin));
                var localDirection = toLocal.MultiplyVector(V(direction));
                var scale = localDirection.magnitude;
                if (scale < 1e-9f)
                {
                    continue;
                }

                if (!part.Geometry.Raycast(P(localOrigin), P(localDirection / scale), maxDistance * scale, out var local))
                {
                    continue;
                }

                var distance = local.Distance / scale;
                if (found && distance >= hit.Distance)
                {
                    continue;
                }

                // normals go through the inverse transpose: right under any scale, mirrored or not
                var normals = toLocal.transpose;
                hit = new SurfaceHit
                {
                    Point = P(part.Transform.TransformPoint(V(local.Point))),
                    Normal = P(normals.MultiplyVector(V(local.Normal)).normalized),
                    GeometricNormal = P(normals.MultiplyVector(V(local.GeometricNormal)).normalized),
                    Distance = distance,
                    Triangle = local.Triangle,
                };
                found = true;
            }

            return found;
        }

        public bool Raycast(Ray ray, float maxDistance, out SurfaceHit hit) =>
            Raycast(P(ray.origin), P(ray.direction.normalized), maxDistance, out hit);

        public static Vector3 V(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        public static Vec3 P(Vector3 v) => new Vec3(v.x, v.y, v.z);
    }
}

#nullable disable
using System;

namespace ModularVests.DevTools.Geometry
{
    /// <summary>Where a ray met a surface.</summary>
    internal struct SurfaceHit
    {
        public Vec3 Point;

        /// <summary>Smoothed normal: the vertex normals interpolated at the point (unit).</summary>
        public Vec3 Normal;

        /// <summary>Normal of the triangle itself (unit).</summary>
        public Vec3 GeometricNormal;

        public float Distance;
        public int Triangle;
    }

    /// <summary>
    /// A triangle mesh in its own space, raycast by brute force (Möller–Trumbore). A few tens of
    /// thousands of triangles take well under a millisecond per ray, which is enough for every
    /// frame of a drag. Only front faces are hit: a ray from outside never lands on the inside of
    /// the rig or goes through a gap onto its back.
    ///
    /// Front is where the geometric normal Cross(b - a, c - a) points, Unity's convention for a
    /// front-facing (clockwise) triangle.
    /// </summary>
    internal sealed class TriangleSurface : ISurface
    {
        private readonly float[] _positions;
        private readonly float[] _normals;
        private readonly int[] _indices;

        /// <param name="positions">x, y, z per vertex.</param>
        /// <param name="normals">x, y, z per vertex, or null (the triangles' own normals are used).</param>
        /// <param name="indices">Three vertex indices per triangle.</param>
        public TriangleSurface(float[] positions, float[] normals, int[] indices)
        {
            _positions = positions ?? throw new ArgumentNullException(nameof(positions));
            _normals = normals != null && normals.Length == positions.Length ? normals : null;
            _indices = indices ?? throw new ArgumentNullException(nameof(indices));
        }

        public int TriangleCount => _indices.Length / 3;

        public int VertexCount => _positions.Length / 3;

        public bool HasNormals => _normals != null;

        /// <summary>
        /// The nearest front-facing hit along a ray. <paramref name="direction"/> must be unit
        /// length. Works in the mesh's own space: a mirrored transform needs no special case there (the
        /// game flips the winding when drawing it, so the same faces are the visible ones).
        /// </summary>
        public bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out SurfaceHit hit)
        {
            hit = default;
            var best = maxDistance;
            var found = false;
            float bestU = 0f, bestV = 0f;
            var bestTriangle = -1;

            for (var t = 0; t + 2 < _indices.Length; t += 3)
            {
                var ia = _indices[t] * 3;
                var ib = _indices[t + 1] * 3;
                var ic = _indices[t + 2] * 3;
                if (ia + 2 >= _positions.Length || ib + 2 >= _positions.Length || ic + 2 >= _positions.Length)
                {
                    continue;
                }

                var ax = _positions[ia];
                var ay = _positions[ia + 1];
                var az = _positions[ia + 2];
                var e1x = _positions[ib] - ax;
                var e1y = _positions[ib + 1] - ay;
                var e1z = _positions[ib + 2] - az;
                var e2x = _positions[ic] - ax;
                var e2y = _positions[ic + 1] - ay;
                var e2z = _positions[ic + 2] - az;

                // geometric normal (unnormalised) and the side the ray comes from
                var nx = e1y * e2z - e1z * e2y;
                var ny = e1z * e2x - e1x * e2z;
                var nz = e1x * e2y - e1y * e2x;
                var facing = nx * direction.X + ny * direction.Y + nz * direction.Z;
                if (facing >= 0f)
                {
                    continue;
                }

                // Möller–Trumbore
                var px = direction.Y * e2z - direction.Z * e2y;
                var py = direction.Z * e2x - direction.X * e2z;
                var pz = direction.X * e2y - direction.Y * e2x;
                var det = e1x * px + e1y * py + e1z * pz;
                if (Math.Abs(det) < 1e-12f)
                {
                    continue;
                }

                var inv = 1f / det;
                var sx = origin.X - ax;
                var sy = origin.Y - ay;
                var sz = origin.Z - az;
                var u = (sx * px + sy * py + sz * pz) * inv;
                if (u < 0f || u > 1f)
                {
                    continue;
                }

                var qx = sy * e1z - sz * e1y;
                var qy = sz * e1x - sx * e1z;
                var qz = sx * e1y - sy * e1x;
                var v = (direction.X * qx + direction.Y * qy + direction.Z * qz) * inv;
                if (v < 0f || u + v > 1f)
                {
                    continue;
                }

                var distance = (e2x * qx + e2y * qy + e2z * qz) * inv;
                if (distance < 0f || distance >= best)
                {
                    continue;
                }

                best = distance;
                bestU = u;
                bestV = v;
                bestTriangle = t;
                found = true;
            }

            if (!found)
            {
                return false;
            }

            hit.Distance = best;
            hit.Point = origin + direction * best;
            hit.Triangle = bestTriangle / 3;
            hit.GeometricNormal = GeometricNormal(bestTriangle);
            hit.Normal = _normals != null ? SmoothNormal(bestTriangle, bestU, bestV, hit.GeometricNormal) : hit.GeometricNormal;
            return true;
        }

        private Vec3 Vertex(int index) =>
            new Vec3(_positions[index * 3], _positions[index * 3 + 1], _positions[index * 3 + 2]);

        private Vec3 VertexNormal(int index) =>
            new Vec3(_normals[index * 3], _normals[index * 3 + 1], _normals[index * 3 + 2]);

        private Vec3 GeometricNormal(int t)
        {
            var a = Vertex(_indices[t]);
            return Vec3.Cross(Vertex(_indices[t + 1]) - a, Vertex(_indices[t + 2]) - a).Normalized;
        }

        /// <summary>The vertex normals at barycentric (u, v); the triangle's own when they are unusable.</summary>
        private Vec3 SmoothNormal(int t, float u, float v, Vec3 fallback)
        {
            var n = (VertexNormal(_indices[t]) * (1f - u - v) + VertexNormal(_indices[t + 1]) * u +
                     VertexNormal(_indices[t + 2]) * v).Normalized;

            // vertex normals that face away from the triangle are broken data, not a surface
            return n.SqrLength < 0.5f || Vec3.Dot(n, fallback) <= 0f ? fallback : n;
        }
    }
}

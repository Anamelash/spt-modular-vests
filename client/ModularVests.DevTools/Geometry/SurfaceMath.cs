#nullable disable
using System;

namespace ModularVests.DevTools.Geometry
{
    /// <summary>Anything a ray can hit: the front faces of one mesh, or of several.</summary>
    internal interface ISurface
    {
        /// <summary>Nearest front-facing hit along a ray with a unit direction.</summary>
        bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out SurfaceHit hit);
    }

    /// <summary>A cell's orientation: +Z out of the surface, +Y up along it.</summary>
    internal struct CellFrame
    {
        public Vec3 Forward;
        public Vec3 Up;

        public Vec3 Right => Vec3.Cross(Up, Forward);
    }

    /// <summary>
    /// Putting bones onto a surface: the math of the editor's surface snap, free of Unity so it
    /// can be tested. Every result either lies on the surface or is reported as a miss.
    /// </summary>
    internal static class SurfaceMath
    {
        /// <summary>
        /// Puts a point back onto the surface: a ray from <c>point + normal * lift</c> down the
        /// normal, <c>2 * lift</c> long. Front faces only (those facing along the normal).
        /// </summary>
        public static bool Reproject(ISurface surface, Vec3 point, Vec3 normal, float lift, out SurfaceHit hit)
        {
            var n = normal.Normalized;
            if (n.SqrLength < 0.5f)
            {
                hit = default;
                return false;
            }

            return surface.Raycast(point + n * lift, -n, 2f * lift, out hit);
        }

        /// <summary>
        /// The normal of a whole cell rather than of one triangle (straps and folds would make a
        /// bone nod): the normal at the centre averaged with the ones at the four corners of the
        /// cell, found by reprojecting the corners along the centre normal. Corners that miss the
        /// surface are left out.
        /// </summary>
        public static Vec3 CellNormal(ISurface surface, SurfaceHit centre, Vec3 tangentX, Vec3 tangentY,
            float halfWidth, float halfHeight, float lift)
        {
            var n0 = centre.Normal;
            var x = Vec3.ProjectOnPlane(tangentX, n0).Normalized;
            var y = Vec3.ProjectOnPlane(tangentY, n0).Normalized;
            if (x.SqrLength < 0.5f || y.SqrLength < 0.5f)
            {
                return n0;
            }

            var sum = n0;
            for (var i = 0; i < 4; i++)
            {
                var corner = centre.Point + x * ((i & 1) == 0 ? -halfWidth : halfWidth) +
                             y * ((i & 2) == 0 ? -halfHeight : halfHeight);
                if (Reproject(surface, corner, n0, lift, out var hit))
                {
                    sum = sum + hit.Normal;
                }
            }

            var result = sum.Normalized;
            return result.SqrLength < 0.5f ? n0 : result;
        }

        /// <summary>
        /// The orientation of a cell standing on a surface with this normal: +Z along the normal,
        /// +Y as close to the rig's up as the surface allows, then turned around the normal by the
        /// roll the cell had against the rig's up before (its roll is kept, only the tilt follows
        /// the surface). When the normal is parallel to the rig's up, the old up is used instead.
        /// </summary>
        public static CellFrame FrameOnSurface(Vec3 normal, Vec3 rigUp, CellFrame old)
        {
            var n = normal.Normalized;

            var oldReference = Vec3.ProjectOnPlane(rigUp, old.Forward.Normalized);
            var oldUp = Vec3.ProjectOnPlane(old.Up, old.Forward.Normalized);
            var roll = oldReference.SqrLength > 1e-8f && oldUp.SqrLength > 1e-8f
                ? Vec3.SignedAngle(oldReference.Normalized, oldUp.Normalized, old.Forward.Normalized)
                : 0f;

            var reference = Vec3.ProjectOnPlane(rigUp, n);
            if (reference.SqrLength < 1e-6f)
            {
                // Facing straight up or down the rig: nothing to measure the roll from. The old
                // up is carried over, turned by the least rotation from the old facing to this one.
                var oldForward = old.Forward.Normalized;
                var axis = Vec3.Cross(oldForward, n);
                var up = axis.SqrLength > 1e-10f
                    ? Vec3.Rotate(old.Up, axis.Normalized,
                        (float)Math.Acos(Math.Max(-1f, Math.Min(1f, Vec3.Dot(oldForward, n)))))
                    : old.Up;
                reference = Vec3.ProjectOnPlane(up, n);
                roll = 0f;
            }

            if (reference.SqrLength < 1e-8f)
            {
                reference = Vec3.ProjectOnPlane(Math.Abs(n.X) < 0.9f ? Vec3.Right : Vec3.Up, n);
            }

            return new CellFrame { Forward = n, Up = Vec3.Rotate(reference.Normalized, n, roll).Normalized };
        }

        /// <summary>
        /// Slides a point along the surface: steps of at most <paramref name="maxStep"/> along
        /// the tangent (kept in the plane of the current normal), each put back on the surface.
        /// On a curve the point follows the arc rather than the chord. Stops at the last point
        /// on the surface and returns false when a step misses.
        /// </summary>
        public static bool Slide(ISurface surface, ref Vec3 point, ref Vec3 normal, Vec3 tangent, float distance,
            float maxStep, float lift)
        {
            if (Math.Abs(distance) < 1e-9f)
            {
                return true;
            }

            var sign = distance < 0f ? -1f : 1f;
            var left = Math.Abs(distance);
            var direction = tangent * sign;
            while (left > 1e-9f)
            {
                var step = Math.Min(left, maxStep);
                var along = Vec3.ProjectOnPlane(direction, normal).Normalized;
                if (along.SqrLength < 0.5f)
                {
                    return false;
                }

                if (!Reproject(surface, point + along * step, normal, Math.Max(lift, step * 2f), out var hit))
                {
                    return false;
                }

                point = hit.Point;
                normal = hit.Normal;
                direction = along;
                left -= step;
            }

            return true;
        }
    }
}

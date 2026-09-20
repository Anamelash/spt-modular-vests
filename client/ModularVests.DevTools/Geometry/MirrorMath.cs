#nullable disable
using System;
using System.Collections.Generic;

namespace ModularVests.DevTools.Geometry
{
    /// <summary>
    /// Reflection of a pose across a plane (the rig's sagittal plane). A position is reflected;
    /// a rotation R becomes M·R·M, M being the reflection - a proper rotation again, so a cell
    /// stays a cell: +Z still out of the surface, +Y still up, and the columns swap sides the way
    /// the cell numbering does (cell 1 of one cluster lands on cell 2 of its pair).
    /// </summary>
    internal static class MirrorMath
    {
        public static Vec3 ReflectPoint(Vec3 point, Vec3 planePoint, Vec3 normal) =>
            point - normal * (2f * Vec3.Dot(point - planePoint, normal));

        public static Vec3 ReflectDirection(Vec3 direction, Vec3 normal) =>
            direction - normal * (2f * Vec3.Dot(direction, normal));

        /// <summary>
        /// The mirror image of a cell's frame: its forward and up reflected across the plane, its own
        /// X (the wearer's right) reflected and negated - still a cell, still right-handed. That is
        /// M(n)·R·M(x): the plane's reflection in world space on the left, the reflection across the
        /// cell's own X on the right. A reflection is minus a half turn about its normal, and the two
        /// minus signs cancel, so in quaternions it is n·q·x (pure quaternions of n and of the X axis).
        ///
        /// Using M(n) on both sides is right only while the plane's normal is the world X; with the
        /// preview turned it tilted every mirrored cell by twice the turn.
        /// </summary>
        public static Quat ReflectRotation(Quat rotation, Vec3 normal)
        {
            var n = normal.Normalized;
            return new Quat(n.X, n.Y, n.Z, 0f) * rotation * new Quat(1f, 0f, 0f, 0f);
        }

        /// <summary>
        /// The plane a layout of mirror pairs is symmetric about: normal <paramref name="normal"/>,
        /// through the mean of the pairs' midpoints along it (so the cells themselves say where the
        /// middle is, not a part of the model). <paramref name="origin"/> only fixes the point on
        /// the plane; with no pairs the plane goes through it.
        /// </summary>
        public static Vec3 SymmetryPlane(IList<Vec3> left, IList<Vec3> right, Vec3 origin, Vec3 normal)
        {
            var count = Math.Min(left.Count, right.Count);
            if (count == 0)
            {
                return origin;
            }

            var offset = 0f;
            for (var i = 0; i < count; i++)
            {
                offset += Vec3.Dot((left[i] + right[i]) * 0.5f - origin, normal);
            }

            return origin + normal * (offset / count);
        }

        /// <summary>
        /// Makes a pair exact mirror images across the plane: each takes the mean of its own pose
        /// and its partner's mirror image - on every axis the mean distance from the middle, the
        /// mean height, the mean depth, and the halfway turn.
        /// </summary>
        public static void AveragePair(ref Vec3 aPosition, ref Quat aRotation, ref Vec3 bPosition, ref Quat bRotation,
            Vec3 planePoint, Vec3 normal)
        {
            var position = (aPosition + ReflectPoint(bPosition, planePoint, normal)) * 0.5f;
            var rotation = Halfway(aRotation, ReflectRotation(bRotation, normal));
            aPosition = position;
            aRotation = rotation;
            bPosition = ReflectPoint(position, planePoint, normal);
            bRotation = ReflectRotation(rotation, normal);
        }

        /// <summary>The rotation halfway between two (normalised sum of the quaternions on the same side).</summary>
        public static Quat Halfway(Quat a, Quat b)
        {
            var sign = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W < 0f ? -1f : 1f;
            var sum = new Quat(a.X + sign * b.X, a.Y + sign * b.Y, a.Z + sign * b.Z, a.W + sign * b.W);
            var norm = sum.Norm;
            return norm > 1e-6f ? new Quat(sum.X / norm, sum.Y / norm, sum.Z / norm, sum.W / norm) : a;
        }
    }
}

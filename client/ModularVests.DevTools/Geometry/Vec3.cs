#nullable disable
using System;

namespace ModularVests.DevTools.Geometry
{
    /// <summary>
    /// A 3D vector for the editor's pure math (surface raycasts, mirroring). Same conventions as
    /// Unity's Vector3 - including the plain cross product - but no Unity dependency, so the
    /// tests can run it; the editor converts at the boundary.
    /// </summary>
    internal readonly struct Vec3 : IEquatable<Vec3>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
        public static readonly Vec3 Right = new Vec3(1f, 0f, 0f);
        public static readonly Vec3 Up = new Vec3(0f, 1f, 0f);
        public static readonly Vec3 Forward = new Vec3(0f, 0f, 1f);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);

        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        public static Vec3 operator *(float s, Vec3 a) => a * s;

        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);

        public float SqrLength => X * X + Y * Y + Z * Z;

        /// <summary>Unit length; zero stays zero.</summary>
        public Vec3 Normalized
        {
            get
            {
                var length = Length;
                return length > 1e-12f ? this / length : Zero;
            }
        }

        /// <summary>The part of v in the plane with the given (unit) normal.</summary>
        public static Vec3 ProjectOnPlane(Vec3 v, Vec3 normal) => v - normal * Dot(v, normal);

        /// <summary>Signed angle (radians) from a to b around axis, both taken in the axis' plane.</summary>
        public static float SignedAngle(Vec3 from, Vec3 to, Vec3 axis) =>
            (float)Math.Atan2(Dot(Cross(from, to), axis), Dot(from, to));

        /// <summary>v rotated around a unit axis by an angle in radians (Rodrigues).</summary>
        public static Vec3 Rotate(Vec3 v, Vec3 axis, float angle)
        {
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            return v * cos + Cross(axis, v) * sin + axis * (Dot(axis, v) * (1f - cos));
        }

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 397) ^ (Z.GetHashCode() * 7919);

        public override string ToString() => $"({X:0.####}, {Y:0.####}, {Z:0.####})";
    }

    /// <summary>A rotation quaternion (x, y, z, w), Unity's layout and product order.</summary>
    internal readonly struct Quat
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float W;

        public Quat(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static readonly Quat Identity = new Quat(0f, 0f, 0f, 1f);

        public static Quat AngleAxis(float radians, Vec3 axis)
        {
            var a = axis.Normalized;
            var s = (float)Math.Sin(radians / 2f);
            return new Quat(a.X * s, a.Y * s, a.Z * s, (float)Math.Cos(radians / 2f));
        }

        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
            a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        public Vec3 Rotate(Vec3 v)
        {
            var u = new Vec3(X, Y, Z);
            var t = Vec3.Cross(u, v) * 2f;
            return v + t * W + Vec3.Cross(u, t);
        }

        public float Norm => (float)Math.Sqrt(X * X + Y * Y + Z * Z + W * W);

        public override string ToString() => $"({X:0.####}, {Y:0.####}, {Z:0.####}, {W:0.####})";
    }
}

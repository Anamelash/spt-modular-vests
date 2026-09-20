using ModularVests.DevTools.Geometry;
using Xunit;

namespace ModularVests.Server.Tests;

public class GeometryTests
{
    private const float Eps = 1e-4f;

    private static void Near(Vec3 expected, Vec3 actual, float eps = Eps)
    {
        Assert.True((expected - actual).Length < eps, $"expected {expected}, got {actual}");
    }

    /// <summary>A flat square in z = 0, facing +Z, of the given half size; four triangles around its centre.</summary>
    private static TriangleSurface Square(float half = 1f, float z = 0f, bool withNormals = true)
    {
        float[] positions = [0f, 0f, z, -half, -half, z, half, -half, z, half, half, z, -half, half, z];
        // Cross(b - a, c - a) must point to +Z: counter-clockwise seen from +Z in these coordinates
        int[] indices = [0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1];
        var normals = withNormals ? Enumerable.Repeat(new[] { 0f, 0f, 1f }, 5).SelectMany(n => n).ToArray() : null;
        return new TriangleSurface(positions, normals, indices);
    }

    // --- raycast ---

    [Fact]
    public void A_ray_into_a_known_triangle_hits_it()
    {
        var surface = Square();
        Assert.True(surface.Raycast(new Vec3(0.3f, 0.2f, 2f), -Vec3.Forward, 10f, out var hit));
        Near(new Vec3(0.3f, 0.2f, 0f), hit.Point);
        Near(Vec3.Forward, hit.Normal);
        Near(Vec3.Forward, hit.GeometricNormal);
        Assert.Equal(2f, hit.Distance, 4);
    }

    [Fact]
    public void A_ray_past_the_mesh_or_too_short_misses()
    {
        var surface = Square();
        Assert.False(surface.Raycast(new Vec3(1.5f, 0f, 2f), -Vec3.Forward, 10f, out _));
        Assert.False(surface.Raycast(new Vec3(0f, 0f, 2f), -Vec3.Forward, 1.5f, out _));
        Assert.False(surface.Raycast(new Vec3(0f, 0f, 2f), Vec3.Forward, 10f, out _));
    }

    [Fact]
    public void The_back_of_a_surface_is_not_hit()
    {
        var surface = Square();
        Assert.False(surface.Raycast(new Vec3(0f, 0f, -2f), Vec3.Forward, 10f, out _));
    }

    [Fact]
    public void The_nearest_front_face_wins()
    {
        // two parallel squares: the ray from above meets the upper one first
        var surface = Combine(Square(z: 0f), Square(z: 0.5f));
        Assert.True(surface.Raycast(new Vec3(0.1f, 0.1f, 2f), -Vec3.Forward, 10f, out var hit));
        Assert.Equal(0.5f, hit.Point.Z, 4);
    }

    [Fact]
    public void Vertex_normals_are_interpolated()
    {
        // a single triangle with two different vertex normals
        float[] positions = [0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f];
        var tilted = new Vec3(1f, 0f, 1f).Normalized;
        float[] normals = [0f, 0f, 1f, tilted.X, tilted.Y, tilted.Z, 0f, 0f, 1f];
        var surface = new TriangleSurface(positions, normals, [0, 1, 2]);

        Assert.True(surface.Raycast(new Vec3(0.5f, 0.1f, 1f), -Vec3.Forward, 5f, out var hit));
        Near(Vec3.Forward, hit.GeometricNormal);
        Assert.True(hit.Normal.X > 0.1f && hit.Normal.X < tilted.X, hit.Normal.ToString());
        Assert.Equal(1f, hit.Normal.Length, 4);
    }

    [Fact]
    public void Without_vertex_normals_the_triangle_normal_is_used()
    {
        var surface = Square(withNormals: false);
        Assert.True(surface.Raycast(new Vec3(0.2f, 0.2f, 1f), -Vec3.Forward, 5f, out var hit));
        Near(Vec3.Forward, hit.Normal);
    }

    // --- reprojection ---

    [Fact]
    public void Reproject_puts_a_point_back_on_the_surface()
    {
        var surface = Square();
        Assert.True(SurfaceMath.Reproject(surface, new Vec3(0.2f, 0.4f, 0.03f), Vec3.Forward, 0.05f, out var hit));
        Near(new Vec3(0.2f, 0.4f, 0f), hit.Point);
    }

    [Fact]
    public void Reproject_misses_past_the_edge_and_beyond_the_lift()
    {
        var surface = Square(half: 0.5f);
        Assert.False(SurfaceMath.Reproject(surface, new Vec3(0.6f, 0f, 0f), Vec3.Forward, 0.05f, out _));
        Assert.False(SurfaceMath.Reproject(surface, new Vec3(0f, 0f, 0.2f), Vec3.Forward, 0.05f, out _));
    }

    [Fact]
    public void Reproject_on_a_step_takes_the_level_within_reach()
    {
        // a step: the lower level at z = 0 (x < 0), the upper one at z = 0.1 (x > 0)
        var surface = Combine(Offset(Square(half: 0.5f), new Vec3(-0.5f, 0f, 0f)),
            Offset(Square(half: 0.5f, z: 0.1f), new Vec3(0.5f, 0f, 0f)));

        Assert.True(SurfaceMath.Reproject(surface, new Vec3(0.2f, 0f, 0.09f), Vec3.Forward, 0.05f, out var upper));
        Assert.Equal(0.1f, upper.Point.Z, 4);
        Assert.True(SurfaceMath.Reproject(surface, new Vec3(-0.2f, 0f, 0.01f), Vec3.Forward, 0.05f, out var lower));
        Assert.Equal(0f, lower.Point.Z, 4);

        // off the upper level by a small step, from its height: the lower level is out of reach
        Assert.False(SurfaceMath.Reproject(surface, new Vec3(-0.01f, 0f, 0.1f), Vec3.Forward, 0.05f, out _));
    }

    // --- normals of a cell ---

    [Fact]
    public void The_cell_normal_averages_the_corners()
    {
        // a roof: two planes tilted around the y axis, the ridge along y at x = 0
        var left = Tilted(-1f);
        var right = Tilted(1f);
        var surface = Combine(left, right);
        Assert.True(SurfaceMath.Reproject(surface, new Vec3(0.01f, 0f, 0.5f), Vec3.Forward, 1f, out var centre));

        var one = centre.Normal;
        var cell = SurfaceMath.CellNormal(surface, centre, Vec3.Right, Vec3.Up, 0.1f, 0.1f, 0.2f);
        Assert.True(Math.Abs(cell.X) < Math.Abs(one.X), $"{cell} should lean less than {one}");
        Assert.Equal(1f, cell.Length, 4);
    }

    // --- orientation on a surface ---

    [Fact]
    public void A_cell_on_a_surface_faces_along_the_normal_and_keeps_its_roll()
    {
        var up = Vec3.Up;
        var rolled = Vec3.Rotate(up, Vec3.Forward, 0.3f);
        var old = new CellFrame { Forward = Vec3.Forward, Up = rolled };

        var normal = new Vec3(1f, 0f, 1f).Normalized;
        var frame = SurfaceMath.FrameOnSurface(normal, up, old);
        Near(normal, frame.Forward);
        Assert.Equal(0f, Vec3.Dot(frame.Forward, frame.Up), 4);
        Assert.Equal(1f, frame.Up.Length, 4);

        // the roll against the rig's up, measured around the new normal, is the old one
        var reference = Vec3.ProjectOnPlane(up, normal).Normalized;
        Assert.Equal(0.3f, Vec3.SignedAngle(reference, frame.Up, normal), 3);
    }

    [Fact]
    public void Without_roll_the_cell_stands_upright()
    {
        var old = new CellFrame { Forward = Vec3.Forward, Up = Vec3.Up };
        var frame = SurfaceMath.FrameOnSurface(new Vec3(0.2f, 0f, 1f), Vec3.Up, old);
        Near(Vec3.Up, frame.Up);
    }

    [Fact]
    public void A_normal_along_the_rig_up_falls_back_to_the_old_up()
    {
        var old = new CellFrame { Forward = Vec3.Forward, Up = Vec3.Up };
        var frame = SurfaceMath.FrameOnSurface(Vec3.Up, Vec3.Up, old);
        Near(Vec3.Up, frame.Forward);
        Assert.Equal(0f, Vec3.Dot(frame.Forward, frame.Up), 4);
        Assert.Equal(1f, frame.Up.Length, 4);
        Assert.True(Vec3.Dot(frame.Up, Vec3.Forward) < -0.99f, frame.Up.ToString());
    }

    // --- sliding ---

    [Fact]
    public void Sliding_along_a_cylinder_stays_on_it_and_follows_the_arc()
    {
        const float radius = 0.15f;
        var surface = Cylinder(radius, height: 0.4f, segments: 128);
        var point = new Vec3(0f, 0f, radius);
        var normal = Vec3.Forward;

        Assert.True(SurfaceMath.Slide(surface, ref point, ref normal, Vec3.Right, 0.2f, maxStep: 0.005f, lift: 0.05f));

        // on the surface, with the surface's normal
        var r = (float)Math.Sqrt(point.X * point.X + point.Z * point.Z);
        Assert.Equal(radius, r, 3);
        Assert.Equal(0f, point.Y, 4);
        Near(new Vec3(point.X, 0f, point.Z).Normalized, normal, 0.03f);

        // the distance went along the arc: 0.2 m of arc is 1.33 rad of the circle, well past
        // where a straight 0.2 m chord would have ended (0.2 m sideways would leave the cylinder)
        var angle = Math.Atan2(point.X, point.Z);
        Assert.Equal(0.2 / radius, angle, 1);
    }

    [Fact]
    public void Sliding_off_the_edge_stops_on_the_last_point_of_the_surface()
    {
        var surface = Square(half: 0.1f);
        var point = Vec3.Zero;
        var normal = Vec3.Forward;
        Assert.False(SurfaceMath.Slide(surface, ref point, ref normal, Vec3.Right, 0.5f, maxStep: 0.01f, lift: 0.05f));
        Assert.True(point.X <= 0.1f && point.X > 0.08f, point.ToString());
        Assert.Equal(0f, point.Z, 4);
    }

    // --- mirror ---

    [Fact]
    public void Mirrored_rotation_is_M_R_M_and_a_proper_rotation()
    {
        var random = new Random(7);
        for (var i = 0; i < 20; i++)
        {
            var axis = new Vec3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f,
                (float)random.NextDouble() - 0.5f);
            var normal = new Vec3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f,
                (float)random.NextDouble() - 0.5f).Normalized;
            CheckMirror(Quat.AngleAxis((float)(random.NextDouble() * 6.0), axis), normal);
        }
    }

    /// <summary>
    /// A mirrored cell: forward and up are the mirror images of the original's, its own X (the
    /// wearer's right) is the mirror image negated.
    /// </summary>
    private static void CheckMirror(Quat rotation, Vec3 normal)
    {
        var mirrored = MirrorMath.ReflectRotation(rotation, normal);
        Assert.Equal(1f, mirrored.Norm, 4);

        Near(MirrorMath.ReflectDirection(rotation.Rotate(Vec3.Forward), normal), mirrored.Rotate(Vec3.Forward));
        Near(MirrorMath.ReflectDirection(rotation.Rotate(Vec3.Up), normal), mirrored.Rotate(Vec3.Up));
        Near(-MirrorMath.ReflectDirection(rotation.Rotate(Vec3.Right), normal), mirrored.Rotate(Vec3.Right));

        // proper: the mirrored frame is right-handed like the original (x cross y = z)
        var x = mirrored.Rotate(Vec3.Right);
        var y = mirrored.Rotate(Vec3.Up);
        Near(mirrored.Rotate(Vec3.Forward), Vec3.Cross(x, y));
    }

    /// <summary>
    /// The rig turned in the preview: its mirror plane is not the world's X. A left cell facing out
    /// must mirror onto a right cell facing out, upright - not tilted by the turn.
    /// </summary>
    [Fact]
    public void Mirroring_holds_with_the_rig_turned()
    {
        var turn = Quat.AngleAxis(0.7f, Vec3.Up); // the preview turned ~40 degrees
        var normal = turn.Rotate(Vec3.Right);
        var facing = turn.Rotate(new Vec3(-0.4f, 0f, 1f).Normalized);
        var cell = LookRotation(facing, Vec3.Up);

        var mirrored = MirrorMath.ReflectRotation(cell, normal);
        Near(MirrorMath.ReflectDirection(facing, normal), mirrored.Rotate(Vec3.Forward));
        Near(Vec3.Up, mirrored.Rotate(Vec3.Up));

        // and mirroring twice is the original
        var back = MirrorMath.ReflectRotation(mirrored, normal);
        Near(cell.Rotate(Vec3.Forward), back.Rotate(Vec3.Forward));
        Near(cell.Rotate(Vec3.Right), back.Rotate(Vec3.Right));
    }

    [Fact]
    public void Mirroring_across_the_x_plane_keeps_a_cell_facing_out_and_upright()
    {
        // a cell on the left side facing out and to the left (-x), upright
        var facing = new Vec3(-0.5f, 0f, 1f).Normalized;
        var rotation = LookRotation(facing, Vec3.Up);
        var mirrored = MirrorMath.ReflectRotation(rotation, Vec3.Right);

        Near(new Vec3(0.5f, 0f, 1f).Normalized, mirrored.Rotate(Vec3.Forward));
        Near(Vec3.Up, mirrored.Rotate(Vec3.Up));

        var p = MirrorMath.ReflectPoint(new Vec3(-0.1f, 0.2f, 0.3f), new Vec3(0.01f, 0f, 0f), Vec3.Right);
        Near(new Vec3(0.12f, 0.2f, 0.3f), p);
    }

    [Fact]
    public void Arranged_pairs_are_mirror_images_about_the_middle_of_the_layout()
    {
        // two pairs placed by hand a little off: the middle is at x = 0.01, not at the origin
        Vec3[] left = [new(-0.10f, 0.20f, 0.30f), new(-0.21f, -0.10f, 0.25f)];
        Vec3[] right = [new(0.13f, 0.22f, 0.29f), new(0.22f, -0.10f, 0.27f)];
        var plane = MirrorMath.SymmetryPlane(left, right, Vec3.Zero, Vec3.Right);
        Near(new Vec3(0.01f, 0f, 0f), plane);

        var aRotation = LookRotation(new Vec3(-0.2f, 0f, 1f).Normalized, Vec3.Up);
        var bRotation = LookRotation(new Vec3(0.3f, 0.05f, 1f).Normalized, Vec3.Up);
        var a = left[0];
        var b = right[0];
        MirrorMath.AveragePair(ref a, ref aRotation, ref b, ref bRotation, plane, Vec3.Right);

        // on each axis the mean: 0.11 and 0.12 from the middle -> 0.115; heights and depths averaged
        Near(new Vec3(0.01f - 0.115f, 0.21f, 0.295f), a);
        Near(new Vec3(0.01f + 0.115f, 0.21f, 0.295f), b);

        // and turned as each other's mirror image, halfway between the two
        Near(MirrorMath.ReflectDirection(aRotation.Rotate(Vec3.Forward), Vec3.Right), bRotation.Rotate(Vec3.Forward));
        Near(MirrorMath.ReflectDirection(aRotation.Rotate(Vec3.Up), Vec3.Right), bRotation.Rotate(Vec3.Up));
        Assert.True(Math.Abs(aRotation.Norm - 1f) < Eps);
        Assert.True(aRotation.Rotate(Vec3.Forward).X < 0f, "the left cell keeps facing left");
    }

    [Fact]
    public void An_arranged_layout_stays_as_it_is()
    {
        var rotation = LookRotation(new Vec3(-0.2f, 0f, 1f).Normalized, Vec3.Up);
        var a = new Vec3(-0.1f, 0.2f, 0.3f);
        var b = MirrorMath.ReflectPoint(a, Vec3.Zero, Vec3.Right);
        var aRotation = rotation;
        var bRotation = MirrorMath.ReflectRotation(rotation, Vec3.Right);
        MirrorMath.AveragePair(ref a, ref aRotation, ref b, ref bRotation, Vec3.Zero, Vec3.Right);
        Near(new Vec3(-0.1f, 0.2f, 0.3f), a);
        Near(rotation.Rotate(Vec3.Forward), aRotation.Rotate(Vec3.Forward));
    }

    // --- cell names ---

    [Theory]
    [InlineData("mod_pouch_10",
        "C3 · cummerbund L · cell 2 (top-right) · mod_pouch_10 · accepts 1x2, 1x1 · mirror → C4 cell 1 (mod_pouch_13)")]
    [InlineData("mod_pouch_1",
        "C1 · chest L · cell 1 (top-left) · mod_pouch_1 · accepts 2x2, 2x1, 1x2, 1x1 · mirror → C2 cell 2 (mod_pouch_6)")]
    [InlineData("mod_pouch_8",
        "C2 · chest R · cell 4 (bottom-right) · mod_pouch_8 · accepts 1x1 · mirror → C1 cell 3 (mod_pouch_3)")]
    public void Passport_names_the_cell(string slot, string passport)
    {
        Assert.Equal(passport, CellInfo.Passport(slot));
    }

    [Fact]
    public void Swapped_cells_are_reported()
    {
        // cell 1 axes: +X is the wearer's right, so "right as seen from the front" is -X
        var p1 = Vec3.Zero;
        var good = CellInfo.OrderWarnings(1, p1, Vec3.Right, Vec3.Up, new Vec3(-0.065f, 0f, 0f), new Vec3(0f, -0.09f, 0f));
        Assert.Empty(good);

        var swapped = CellInfo.OrderWarnings(1, p1, Vec3.Right, Vec3.Up, new Vec3(0f, -0.09f, 0f), new Vec3(-0.065f, 0f, 0f));
        Assert.Equal(2, swapped.Count);
    }

    // --- helpers ---

    private static Quat LookRotation(Vec3 forward, Vec3 up)
    {
        // rotation taking +Z to forward and +Y to up (orthonormalised), via two steps
        var f = forward.Normalized;
        var first = FromTo(Vec3.Forward, f);
        var currentUp = first.Rotate(Vec3.Up);
        var wantedUp = Vec3.ProjectOnPlane(up, f).Normalized;
        var roll = Quat.AngleAxis(Vec3.SignedAngle(currentUp, wantedUp, f), f);
        return roll * first;
    }

    private static Quat FromTo(Vec3 from, Vec3 to)
    {
        var axis = Vec3.Cross(from, to);
        if (axis.SqrLength < 1e-10f)
        {
            return Vec3.Dot(from, to) > 0f ? Quat.Identity : Quat.AngleAxis((float)Math.PI, Vec3.Up);
        }

        return Quat.AngleAxis((float)Math.Acos(Math.Clamp(Vec3.Dot(from, to), -1f, 1f)), axis);
    }

    private sealed class Several(params ISurface[] parts) : ISurface
    {
        public bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out SurfaceHit hit)
        {
            hit = default;
            var found = false;
            foreach (var part in parts)
            {
                if (part.Raycast(origin, direction, maxDistance, out var h) && (!found || h.Distance < hit.Distance))
                {
                    hit = h;
                    found = true;
                }
            }

            return found;
        }
    }

    private static ISurface Combine(params ISurface[] parts) => new Several(parts);

    private sealed class Shifted(ISurface inner, Vec3 offset) : ISurface
    {
        public bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out SurfaceHit hit)
        {
            if (!inner.Raycast(origin - offset, direction, maxDistance, out hit))
            {
                return false;
            }

            hit.Point = hit.Point + offset;
            return true;
        }
    }

    private static ISurface Offset(ISurface surface, Vec3 offset) => new Shifted(surface, offset);

    /// <summary>Half of a roof: a plane through the y axis leaning to one side, facing up-and-out.</summary>
    private static TriangleSurface Tilted(float side)
    {
        // from the ridge (x = 0, z = 0.5) down to x = side, z = 0
        float[] positions = [0f, -1f, 0.5f, 0f, 1f, 0.5f, side, 1f, 0f, side, -1f, 0f];
        int[] indices = side > 0 ? [0, 3, 2, 0, 2, 1] : [0, 1, 2, 0, 2, 3];
        return new TriangleSurface(positions, null, indices);
    }

    /// <summary>A cylinder around the y axis with outward faces and normals.</summary>
    private static TriangleSurface Cylinder(float radius, float height, int segments)
    {
        var positions = new List<float>();
        var normals = new List<float>();
        for (var i = 0; i < segments; i++)
        {
            var angle = 2.0 * Math.PI * i / segments;
            var x = (float)Math.Sin(angle);
            var z = (float)Math.Cos(angle);
            foreach (var y in new[] { -height / 2f, height / 2f })
            {
                positions.AddRange([x * radius, y, z * radius]);
                normals.AddRange([x, 0f, z]);
            }
        }

        var indices = new List<int>();
        for (var i = 0; i < segments; i++)
        {
            var a = i * 2;
            var b = (i + 1) % segments * 2;
            // quad a(bottom) a+1(top) b+1(top) b(bottom), wound to face outward
            indices.AddRange([a, b + 1, a + 1, a, b, b + 1]);
        }

        var surface = new TriangleSurface(positions.ToArray(), normals.ToArray(), indices.ToArray());

        // sanity: the winding really faces outward
        Assert.True(surface.Raycast(new Vec3(0f, 0f, 1f), -Vec3.Forward, 5f, out _));
        return surface;
    }
}

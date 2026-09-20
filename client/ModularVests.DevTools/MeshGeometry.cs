using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ModularVests.DevTools.Geometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace ModularVests.DevTools
{
    /// <summary>
    /// A copy of a mesh's triangles on the CPU, for the editor's surface raycasts.
    ///
    /// Meshes from the game's bundles are usually not readable (their CPU copy is dropped after
    /// upload), so a MeshCollider cannot be cooked from them. The data is still on the GPU: the
    /// vertex and index buffers are read back once per mesh (Mesh.GetVertexBuffer /
    /// GetIndexBuffer, GraphicsBuffer.GetData) and decoded here. A readable mesh takes the short
    /// way (GetVertices / GetNormals / GetIndices).
    /// </summary>
    internal static class MeshGeometry
    {
        private sealed class Entry
        {
            public TriangleSurface Surface;
            public string Error;
            public string Path;
            public double Milliseconds;
        }

        private static readonly Dictionary<int, Entry> Cache = new Dictionary<int, Entry>();

        /// <summary>The mesh's triangles, or null with the reason.</summary>
        public static TriangleSurface Get(Mesh mesh, out string error)
        {
            error = null;
            if (mesh == null)
            {
                error = "no mesh";
                return null;
            }

            var key = mesh.GetInstanceID();
            if (!Cache.TryGetValue(key, out var entry))
            {
                Cache[key] = entry = Read(mesh);
            }

            error = entry.Error;
            return entry.Surface;
        }

        private static Entry Read(Mesh mesh)
        {
            var watch = Stopwatch.StartNew();
            var entry = new Entry();
            try
            {
                float[] positions;
                float[] normals;
                int[] indices;
                if (mesh.isReadable)
                {
                    entry.Path = "readable";
                    ReadCpu(mesh, out positions, out normals, out indices);
                }
                else
                {
                    entry.Path = "gpu";
                    ReadGpu(mesh, out positions, out normals, out indices);
                }

                var check = CheckBounds(mesh, positions);
                if (check != null)
                {
                    entry.Error = check;
                }
                else if (indices.Length < 3)
                {
                    entry.Error = "no triangles";
                }
                else
                {
                    entry.Surface = new TriangleSurface(positions, normals, indices);
                }
            }
            catch (Exception ex)
            {
                entry.Error = $"{entry.Path} read failed: {ex.GetType().Name}: {ex.Message}";
            }

            entry.Milliseconds = watch.Elapsed.TotalMilliseconds;
            return entry;
        }

        private static void ReadCpu(Mesh mesh, out float[] positions, out float[] normals, out int[] indices)
        {
            var vertices = mesh.vertices;
            positions = new float[vertices.Length * 3];
            for (var i = 0; i < vertices.Length; i++)
            {
                positions[i * 3] = vertices[i].x;
                positions[i * 3 + 1] = vertices[i].y;
                positions[i * 3 + 2] = vertices[i].z;
            }

            var n = mesh.normals;
            normals = null;
            if (n != null && n.Length == vertices.Length)
            {
                normals = new float[n.Length * 3];
                for (var i = 0; i < n.Length; i++)
                {
                    normals[i * 3] = n[i].x;
                    normals[i * 3 + 1] = n[i].y;
                    normals[i * 3 + 2] = n[i].z;
                }
            }

            var list = new List<int>();
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) == MeshTopology.Triangles)
                {
                    list.AddRange(mesh.GetIndices(s, applyBaseVertex: true));
                }
            }

            indices = list.ToArray();
        }

        private static void ReadGpu(Mesh mesh, out float[] positions, out float[] normals, out int[] indices)
        {
            var vertexCount = mesh.vertexCount;
            if (!mesh.HasVertexAttribute(VertexAttribute.Position))
            {
                throw new InvalidOperationException("mesh has no positions");
            }

            var streams = new Dictionary<int, byte[]>();
            positions = ReadAttribute(mesh, VertexAttribute.Position, vertexCount, streams, out _);
            normals = mesh.HasVertexAttribute(VertexAttribute.Normal)
                ? ReadAttribute(mesh, VertexAttribute.Normal, vertexCount, streams, out _)
                : null;

            byte[] indexBytes;
            using (var buffer = mesh.GetIndexBuffer())
            {
                indexBytes = new byte[buffer.count * buffer.stride];
                buffer.GetData(indexBytes);
            }

            var wide = mesh.indexFormat == IndexFormat.UInt32;
            var list = new List<int>();
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                var sub = mesh.GetSubMesh(s);
                if (sub.topology != MeshTopology.Triangles)
                {
                    continue;
                }

                for (var i = sub.indexStart; i < sub.indexStart + sub.indexCount; i++)
                {
                    var index = wide ? (int)BitConverter.ToUInt32(indexBytes, i * 4) : BitConverter.ToUInt16(indexBytes, i * 2);
                    list.Add(index + sub.baseVertex);
                }
            }

            indices = list.ToArray();
        }

        /// <summary>One vertex attribute as three floats per vertex, whatever its storage format.</summary>
        private static float[] ReadAttribute(Mesh mesh, VertexAttribute attribute, int vertexCount,
            Dictionary<int, byte[]> streams, out string format)
        {
            var stream = mesh.GetVertexAttributeStream(attribute);
            var offset = mesh.GetVertexAttributeOffset(attribute);
            var kind = mesh.GetVertexAttributeFormat(attribute);
            var dimension = mesh.GetVertexAttributeDimension(attribute);
            var stride = mesh.GetVertexBufferStride(stream);
            format = $"{kind}x{dimension}";

            if (!streams.TryGetValue(stream, out var bytes))
            {
                using (var buffer = mesh.GetVertexBuffer(stream))
                {
                    bytes = new byte[buffer.count * buffer.stride];
                    buffer.GetData(bytes);
                }

                streams[stream] = bytes;
            }

            var result = new float[vertexCount * 3];
            var components = Math.Min(dimension, 3);
            for (var v = 0; v < vertexCount; v++)
            {
                var at = v * stride + offset;
                for (var c = 0; c < components; c++)
                {
                    result[v * 3 + c] = Decode(bytes, at, c, kind);
                }
            }

            return result;
        }

        private static float Decode(byte[] bytes, int at, int component, VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                    return BitConverter.ToSingle(bytes, at + component * 4);
                case VertexAttributeFormat.Float16:
                    return HalfToFloat(BitConverter.ToUInt16(bytes, at + component * 2));
                case VertexAttributeFormat.SNorm16:
                    return Math.Max(BitConverter.ToInt16(bytes, at + component * 2) / 32767f, -1f);
                case VertexAttributeFormat.SNorm8:
                    return Math.Max((sbyte)bytes[at + component] / 127f, -1f);
                case VertexAttributeFormat.UNorm16:
                    return BitConverter.ToUInt16(bytes, at + component * 2) / 65535f;
                case VertexAttributeFormat.UNorm8:
                    return bytes[at + component] / 255f;
                default:
                    throw new NotSupportedException("vertex format " + format);
            }
        }

        private static float HalfToFloat(ushort half)
        {
            var sign = (half >> 15) & 1;
            var exponent = (half >> 10) & 0x1f;
            var mantissa = half & 0x3ff;
            float value;
            if (exponent == 0)
            {
                value = mantissa / 1024f * (float)Math.Pow(2, -14);
            }
            else if (exponent == 31)
            {
                value = mantissa == 0 ? float.PositiveInfinity : float.NaN;
            }
            else
            {
                value = (1f + mantissa / 1024f) * (float)Math.Pow(2, exponent - 15);
            }

            return sign == 1 ? -value : value;
        }

        /// <summary>The read vertices must fill the mesh's own bounds: a wrong decode does not.</summary>
        private static string CheckBounds(Mesh mesh, float[] positions)
        {
            if (positions.Length < 3)
            {
                return "no vertices";
            }

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (var i = 0; i + 2 < positions.Length; i += 3)
            {
                var p = new Vector3(positions[i], positions[i + 1], positions[i + 2]);
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z))
                {
                    return "vertices decoded as NaN";
                }

                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            var bounds = mesh.bounds;
            var tolerance = 0.001f + bounds.size.magnitude * 0.01f;
            if ((min - bounds.min).magnitude > tolerance || (max - bounds.max).magnitude > tolerance)
            {
                return $"vertices do not match the mesh bounds (read {min:F3}..{max:F3}, " +
                       $"bounds {bounds.min:F3}..{bounds.max:F3})";
            }

            return null;
        }

        // --- diagnostics (the spike: does reading the GPU copy work in this game) ---

        private static readonly HashSet<int> Diagnosed = new HashSet<int>();

        /// <summary>One report per model: colliders, and for every mesh how it reads.</summary>
        public static void LogDiagnostics(Transform root, string what)
        {
            if (root == null || !Diagnosed.Add(root.GetInstanceID()))
            {
                return;
            }

            var report = new StringBuilder();
            report.Append($"[ModularVests.DevTools] mesh diagnostics of '{root.name}' ({what}), " +
                          $"graphics {SystemInfo.graphicsDeviceType}:");

            foreach (var collider in root.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                report.Append($"\n  collider '{collider.name}' {collider.GetType().Name}, " +
                              $"enabled={collider.enabled}, layer={collider.gameObject.layer}");
            }

            var meshes = new List<KeyValuePair<string, Mesh>>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                meshes.Add(new KeyValuePair<string, Mesh>("filter '" + filter.name + "'", filter.sharedMesh));
            }

            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
            {
                meshes.Add(new KeyValuePair<string, Mesh>("skin '" + skin.name + "'", skin.sharedMesh));
            }

            foreach (var item in meshes)
            {
                var mesh = item.Value;
                if (mesh == null)
                {
                    report.Append($"\n  {item.Key}: no mesh");
                    continue;
                }

                var attributes = new List<string>();
                foreach (var a in mesh.GetVertexAttributes())
                {
                    attributes.Add($"{a.attribute}:{a.format}x{a.dimension}@s{a.stream}");
                }

                var triangles = 0L;
                for (var s = 0; s < mesh.subMeshCount; s++)
                {
                    var sub = mesh.GetSubMesh(s);
                    if (sub.topology == MeshTopology.Triangles)
                    {
                        triangles += sub.indexCount / 3;
                    }
                }

                Get(mesh, out var error);
                var entry = Cache[mesh.GetInstanceID()];
                report.Append($"\n  {item.Key} mesh '{mesh.name}': readable={mesh.isReadable}, " +
                              $"vertices={mesh.vertexCount}, triangles={triangles}, index={mesh.indexFormat}, " +
                              $"streams={mesh.vertexBufferCount} [{string.Join(", ", attributes.ToArray())}] -> " +
                              $"{entry.Path} {(error == null ? "OK" : "FAILED: " + error)} in {entry.Milliseconds:0.0} ms");
            }

            DevPlugin.Log.LogInfo(report.ToString());
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    // Build-scoped geometry only. Keep instances in the caller's local asset cache,
    // then release them after placement; no permanent CPU copy or physics mesh.
    internal sealed class ReefSupportGeometry
    {
        const int SurfaceCells = 32;
        const int BinaryMagic = 0x52534647; // RSFG
        const int BinaryVersion = 1;
        public Bounds bounds;
        public Vector3[] rockContacts;
        public Vector3[] coralContacts;
        Vector3[] vertices;
        int[] triangles;
        List<int>[] surfaceBuckets;

        public static ReefSupportGeometry Read(GameObject prefab, bool surface)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            bool readable = true;
            foreach (MeshFilter filter in filters)
                if (filter.sharedMesh != null && !filter.sharedMesh.isReadable) readable = false;
            Vector3[] points;
            int[] indices;
            if (readable) Collect(prefab, filters, surface, false, out points, out indices);
            else ReadBaked(prefab, filters, surface, out points, out indices);
            if (points.Length == 0)
                throw new InvalidOperationException("[ReefSupport] No mesh vertices in " + prefab.name);
            var result = new ReefSupportGeometry { vertices = points, triangles = indices };
            result.bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 point in points)
            {
                if (!Finite(point.x) || !Finite(point.y) || !Finite(point.z))
                    throw new InvalidOperationException("[ReefSupport] Non-finite vertex in " + prefab.name);
                result.bounds.Encapsulate(point);
            }
            result.rockContacts = result.LowContacts(12, 0.40f);
            result.coralContacts = result.LowContacts(8, 0.12f);
            if (surface) result.BuildSurfaceBuckets();
            else result.vertices = null;
            return result;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        Vector3[] LowContacts(int cells, float heightFraction)
        {
            var lowest = new Vector3[cells * cells];
            var present = new bool[lowest.Length];
            float cutoff = bounds.min.y + bounds.size.y * heightFraction;
            Vector3 absoluteLowest = vertices[0];
            foreach (Vector3 point in vertices)
            {
                if (point.y < absoluteLowest.y) absoluteLowest = point;
                if (point.y > cutoff) continue;
                int slot = Cell(point.x, bounds.min.x, bounds.size.x, cells) +
                    cells * Cell(point.z, bounds.min.z, bounds.size.z, cells);
                if (!present[slot] || point.y < lowest[slot].y)
                {
                    present[slot] = true;
                    lowest[slot] = point;
                }
            }
            var contacts = new List<Vector3>(lowest.Length);
            for (int i = 0; i < lowest.Length; i++) if (present[i]) contacts.Add(lowest[i]);
            if (contacts.Count == 0) contacts.Add(absoluteLowest);
            return contacts.ToArray();
        }

        static int Cell(float value, float minimum, float size, int cells)
        {
            if (size <= 0f) return 0;
            return Mathf.Clamp(Mathf.FloorToInt((value - minimum) / size * cells), 0, cells - 1);
        }

        void BuildSurfaceBuckets()
        {
            surfaceBuckets = new List<int>[SurfaceCells * SurfaceCells];
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                double area = ((double)b.z - c.z) * ((double)a.x - c.x) +
                    ((double)c.x - b.x) * ((double)a.z - c.z);
                if (Math.Abs(area) <= 1e-20) continue;
                int x0 = Cell(Mathf.Min(a.x, b.x, c.x), bounds.min.x, bounds.size.x, SurfaceCells);
                int x1 = Cell(Mathf.Max(a.x, b.x, c.x), bounds.min.x, bounds.size.x, SurfaceCells);
                int z0 = Cell(Mathf.Min(a.z, b.z, c.z), bounds.min.z, bounds.size.z, SurfaceCells);
                int z1 = Cell(Mathf.Max(a.z, b.z, c.z), bounds.min.z, bounds.size.z, SurfaceCells);
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int slot = z * SurfaceCells + x;
                    if (surfaceBuckets[slot] == null) surfaceBuckets[slot] = new List<int>();
                    surfaceBuckets[slot].Add(t);
                }
            }
        }

        public bool TryTop(float localX, float localZ, out float localY)
        {
            localY = 0f;
            if (surfaceBuckets == null || !Finite(localX) || !Finite(localZ) ||
                localX < bounds.min.x || localX > bounds.max.x ||
                localZ < bounds.min.z || localZ > bounds.max.z) return false;
            int slot = Cell(localX, bounds.min.x, bounds.size.x, SurfaceCells) +
                SurfaceCells * Cell(localZ, bounds.min.z, bounds.size.z, SurfaceCells);
            List<int> candidates = surfaceBuckets[slot];
            if (candidates == null) return false;
            bool found = false;
            double highest = double.NegativeInfinity;
            foreach (int t in candidates)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                double denominator = ((double)b.z - c.z) * ((double)a.x - c.x) +
                    ((double)c.x - b.x) * ((double)a.z - c.z);
                double u = (((double)b.z - c.z) * ((double)localX - c.x) +
                    ((double)c.x - b.x) * ((double)localZ - c.z)) / denominator;
                double v = (((double)c.z - a.z) * ((double)localX - c.x) +
                    ((double)a.x - c.x) * ((double)localZ - c.z)) / denominator;
                // Only roundoff tolerance at a triangle edge, never a bounding-box fill.
                if (u < -1e-7 || v < -1e-7 || u + v > 1.0000001) continue;
                double y = u * a.y + v * b.y + (1.0 - u - v) * c.y;
                if (y > highest) { highest = y; found = true; }
            }
            if (found) localY = (float)highest;
            return found;
        }

        // Placement-only query: retain the real gap beneath a coral overhang instead of filling its box.
        public bool TryVerticalSpan(float x, float z, out float bottom, out float top)
        {
            bottom = float.PositiveInfinity; top = float.NegativeInfinity;
            if (surfaceBuckets == null || !Finite(x) || !Finite(z) ||
                x < bounds.min.x || x > bounds.max.x || z < bounds.min.z || z > bounds.max.z) return false;
            var candidates = surfaceBuckets[Cell(x, bounds.min.x, bounds.size.x, SurfaceCells) +
                SurfaceCells * Cell(z, bounds.min.z, bounds.size.z, SurfaceCells)];
            if (candidates == null) return false;
            foreach (int t in candidates)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                double den = ((double)b.z - c.z) * ((double)a.x - c.x) + ((double)c.x - b.x) * ((double)a.z - c.z);
                if (Math.Abs(den) < 1e-20) continue;
                double u = (((double)b.z - c.z) * ((double)x - c.x) + ((double)c.x - b.x) * ((double)z - c.z)) / den;
                double v = (((double)c.z - a.z) * ((double)x - c.x) + ((double)a.x - c.x) * ((double)z - c.z)) / den;
                if (u < -1e-7 || v < -1e-7 || u + v > 1.0000001) continue;
                float y = (float)(u * a.y + v * b.y + (1.0 - u - v) * c.y);
                bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
            }
            return !float.IsPositiveInfinity(bottom);
        }

        // Both endpoints are in the prefab-root space used by Read(..., true).
        // Surface contact, including an endpoint or coplanar edge, blocks the segment.
        // This tests a line against surfaces, not mesh containment or body clearance.
        public bool IntersectsSegment(Vector3 from, Vector3 to)
        {
            if (vertices == null || triangles == null)
                throw new InvalidOperationException("[ReefSupport] Segment query requires Read(..., true).");
            if (!Finite(from.x) || !Finite(from.y) || !Finite(from.z) ||
                !Finite(to.x) || !Finite(to.y) || !Finite(to.z)) return false;
            double dx = (double)to.x - from.x, dy = (double)to.y - from.y, dz = (double)to.z - from.z;
            double lengthSquared = dx * dx + dy * dy + dz * dz;
            if (lengthSquared == 0.0) return false;

            // Segment AABB broad phase also handles zero-thickness vertical meshes.
            // Expand only for float-coordinate roundoff; never fill the box as a hit.
            double padding = Math.Max(1.0, Math.Max(bounds.size.x, Math.Max(bounds.size.y, bounds.size.z))) * 1e-7;
            Vector3 min = bounds.min, max = bounds.max;
            if (Math.Max(from.x, to.x) < min.x - padding || Math.Min(from.x, to.x) > max.x + padding ||
                Math.Max(from.y, to.y) < min.y - padding || Math.Min(from.y, to.y) > max.y + padding ||
                Math.Max(from.z, to.z) < min.z - padding || Math.Min(from.z, to.z) > max.z + padding) return false;

            // Do not use surfaceBuckets: a vertical wall has no XZ projected area.
            for (int i = 0; i < triangles.Length; i += 3)
                if (SegmentTriangle(from, to, dx, dy, dz, lengthSquared,
                    vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]])) return true;
            return false;
        }

        static bool SegmentTriangle(Vector3 from, Vector3 to, double dx, double dy, double dz,
            double lengthSquared, Vector3 a, Vector3 b, Vector3 c)
        {
            const double parameterTolerance = 1e-9;
            double e1x = (double)b.x - a.x, e1y = (double)b.y - a.y, e1z = (double)b.z - a.z;
            double e2x = (double)c.x - a.x, e2y = (double)c.y - a.y, e2z = (double)c.z - a.z;
            double nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
            double normalSquared = nx * nx + ny * ny + nz * nz;
            if (normalSquared == 0.0) return false; // Degenerate triangles have no blocking surface.
            double px = dy * e2z - dz * e2y, py = dz * e2x - dx * e2z, pz = dx * e2y - dy * e2x;
            double determinant = e1x * px + e1y * py + e1z * pz;
            double tx = (double)from.x - a.x, ty = (double)from.y - a.y, tz = (double)from.z - a.z;
            if (Math.Abs(determinant) <= 1e-12 * Math.Sqrt(normalSquared * lengthSquared))
            {
                // Parallel segments can still overlap a thin wall in its own plane.
                double edgeScale = Math.Max(Math.Sqrt(e1x * e1x + e1y * e1y + e1z * e1z),
                    Math.Sqrt(e2x * e2x + e2y * e2y + e2z * e2z));
                double planeTolerance = parameterTolerance * Math.Sqrt(normalSquared) * edgeScale;
                double firstDistance = nx * tx + ny * ty + nz * tz;
                double lastDistance = nx * (tx + dx) + ny * (ty + dy) + nz * (tz + dz);
                if (Math.Abs(firstDistance) > planeTolerance || Math.Abs(lastDistance) > planeTolerance) return false;
                int omit = Math.Abs(nx) >= Math.Abs(ny) && Math.Abs(nx) >= Math.Abs(nz) ? 0 :
                    Math.Abs(ny) >= Math.Abs(nz) ? 1 : 2;
                return CoplanarSegmentTriangle(from, to, a, b, c, omit);
            }

            // Moller-Trumbore with signed determinant: accept either winding.
            double inverse = 1.0 / determinant;
            double u = (tx * px + ty * py + tz * pz) * inverse;
            if (u < -parameterTolerance || u > 1.0 + parameterTolerance) return false;
            double qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
            double v = (dx * qx + dy * qy + dz * qz) * inverse;
            if (v < -parameterTolerance || u + v > 1.0 + parameterTolerance) return false;
            double t = (e2x * qx + e2y * qy + e2z * qz) * inverse;
            return t >= -parameterTolerance && t <= 1.0 + parameterTolerance;
        }

        static bool CoplanarSegmentTriangle(Vector3 from, Vector3 to, Vector3 a, Vector3 b, Vector3 c, int omit)
        {
            int x = (omit + 1) % 3, y = (omit + 2) % 3;
            return PointInTriangle2D(from, a, b, c, x, y) || PointInTriangle2D(to, a, b, c, x, y) ||
                SegmentsIntersect2D(from, to, a, b, x, y) || SegmentsIntersect2D(from, to, b, c, x, y) ||
                SegmentsIntersect2D(from, to, c, a, x, y);
        }

        static double Orient2D(Vector3 a, Vector3 b, Vector3 c, int x, int y) =>
            ((double)b[x] - a[x]) * ((double)c[y] - a[y]) - ((double)b[y] - a[y]) * ((double)c[x] - a[x]);

        static bool PointInTriangle2D(Vector3 p, Vector3 a, Vector3 b, Vector3 c, int x, int y)
        {
            double epsilon = Math.Abs(Orient2D(a, b, c, x, y)) * 1e-9;
            double ab = Orient2D(a, b, p, x, y), bc = Orient2D(b, c, p, x, y), ca = Orient2D(c, a, p, x, y);
            return (ab >= -epsilon && bc >= -epsilon && ca >= -epsilon) ||
                (ab <= epsilon && bc <= epsilon && ca <= epsilon);
        }

        static bool SegmentsIntersect2D(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int x, int y)
        {
            double rx = (double)b[x] - a[x], ry = (double)b[y] - a[y];
            double sx = (double)d[x] - c[x], sy = (double)d[y] - c[y];
            if (rx == 0.0 && ry == 0.0) return false; // Point case was handled by PointInTriangle2D.
            double tx = (double)c[x] - a[x], ty = (double)c[y] - a[y];
            double denominator = rx * sy - ry * sx;
            double scale = Math.Sqrt((rx * rx + ry * ry) * (sx * sx + sy * sy));
            if (Math.Abs(denominator) > 1e-12 * scale)
            {
                double t = (tx * sy - ty * sx) / denominator, u = (tx * ry - ty * rx) / denominator;
                return t >= -1e-9 && t <= 1.000000001 && u >= -1e-9 && u <= 1.000000001;
            }
            if (Math.Abs(tx * ry - ty * rx) > 1e-9 * Math.Sqrt((tx * tx + ty * ty) * (rx * rx + ry * ry)))
                return false;
            // Collinear edges: overlap on the segment's dominant axis, endpoints included.
            int axis = Math.Abs(rx) >= Math.Abs(ry) ? x : y;
            return Math.Max(Math.Min(a[axis], b[axis]), Math.Min(c[axis], d[axis])) <=
                Math.Min(Math.Max(a[axis], b[axis]), Math.Max(c[axis], d[axis]));
        }

        static void Collect(GameObject prefab, MeshFilter[] filters, bool includeTriangles, bool editorRead,
            out Vector3[] points, out int[] indices)
        {
            var mergedPoints = new List<Vector3>();
            var mergedIndices = new List<int>();
            Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;
            foreach (MeshFilter filter in filters)
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                Matrix4x4 toRoot = rootInverse * filter.transform.localToWorldMatrix;
                int offset = mergedPoints.Count;
#if UNITY_EDITOR
                if (editorRead)
                {
                    // Editor API deliberately permits reading meshes whose player CPU
                    // copy is disabled; it does not change importer settings or meshes.
                    using (var dataArray = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
                    {
                        Mesh.MeshData data = dataArray[0];
                        using (var buffer = new Unity.Collections.NativeArray<Vector3>(data.vertexCount,
                            Unity.Collections.Allocator.Temp, Unity.Collections.NativeArrayOptions.UninitializedMemory))
                        {
                            data.GetVertices(buffer);
                            for (int i = 0; i < buffer.Length; i++) mergedPoints.Add(toRoot.MultiplyPoint3x4(buffer[i]));
                        }
                        if (includeTriangles)
                        for (int sub = 0; sub < data.subMeshCount; sub++)
                        {
                            SubMeshDescriptor descriptor = data.GetSubMesh(sub);
                            if (descriptor.topology != MeshTopology.Triangles) continue;
                            using (var buffer = new Unity.Collections.NativeArray<int>(descriptor.indexCount,
                                Unity.Collections.Allocator.Temp, Unity.Collections.NativeArrayOptions.UninitializedMemory))
                            {
                                data.GetIndices(buffer, sub, true);
                                for (int i = 0; i < buffer.Length; i++) mergedIndices.Add(offset + buffer[i]);
                            }
                        }
                    }
                    continue;
                }
#endif
                foreach (Vector3 point in mesh.vertices) mergedPoints.Add(toRoot.MultiplyPoint3x4(point));
                if (includeTriangles)
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    foreach (int index in mesh.GetIndices(sub, true)) mergedIndices.Add(offset + index);
                }
            }
            points = mergedPoints.ToArray();
            indices = mergedIndices.ToArray();
            ValidateIndices(points.Length, indices);
        }

        static void ValidateIndices(int vertexCount, int[] indices)
        {
            if (indices.Length % 3 != 0) throw new InvalidDataException("[ReefSupport] Incomplete triangle.");
            foreach (int index in indices)
                if (index < 0 || index >= vertexCount) throw new InvalidDataException("[ReefSupport] Invalid mesh index.");
        }

        static string Signature(GameObject prefab, MeshFilter[] filters)
        {
            var text = new StringBuilder(prefab.name);
            Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;
            foreach (MeshFilter filter in filters)
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                text.Append('|').Append(mesh.name).Append(':').Append(mesh.vertexCount);
                Matrix4x4 toRoot = rootInverse * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 16; i++) text.Append(':').Append(toRoot[i].ToString("R", CultureInfo.InvariantCulture));
                for (int i = 0; i < 3; i++)
                    text.Append(':').Append(mesh.bounds.min[i].ToString("R", CultureInfo.InvariantCulture))
                        .Append(':').Append(mesh.bounds.max[i].ToString("R", CultureInfo.InvariantCulture));
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    text.Append(':').Append((int)mesh.GetTopology(sub)).Append(':').Append(mesh.GetIndexCount(sub));
            }
            return text.ToString();
        }

        static void ReadBaked(GameObject prefab, MeshFilter[] filters, bool surface,
            out Vector3[] points, out int[] indices)
        {
            string resource = "ReefSupport/" + prefab.name;
            TextAsset baked = Resources.Load<TextAsset>(resource);
            if (baked == null)
                throw new InvalidOperationException("[ReefSupport] Mesh is not readable and " + resource +
                    ".bytes is missing. Run Swan/Codex/Bake unreadable reef support geometry, or -executeMethod " +
                    "SwanUnderwater.Editor.ReefSupportGeometryBake.Run before building. No bounds fallback is used.");
            try
            {
                using (var stream = new MemoryStream(baked.bytes, false))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != BinaryMagic || reader.ReadInt32() != BinaryVersion ||
                        reader.ReadString() != prefab.name || reader.ReadString() != Signature(prefab, filters))
                        throw new InvalidDataException("[ReefSupport] Incompatible baked geometry: " + prefab.name);
                    reader.ReadString(); // Editor source dependency hash; refreshed by the build preprocessor.
                    int count = reader.ReadInt32();
                    if (count < 1 || count > (stream.Length - stream.Position) / 12)
                        throw new InvalidDataException("[ReefSupport] Invalid baked vertex count.");
                    points = new Vector3[count];
                    for (int i = 0; i < count; i++)
                        points[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    int indexCount = reader.ReadInt32();
                    if (indexCount < 0 || indexCount % 3 != 0 || (long)indexCount * 4 != stream.Length - stream.Position)
                        throw new InvalidDataException("[ReefSupport] Invalid baked triangle count.");
                    indices = surface ? new int[indexCount] : new int[0];
                    if (surface)
                    {
                        for (int i = 0; i < indexCount; i++) indices[i] = reader.ReadInt32();
                        ValidateIndices(count, indices);
                    }
                }
            }
            finally { Resources.UnloadAsset(baked); }
        }

#if UNITY_EDITOR
        internal static bool BakeIfUnreadable(GameObject prefab, string assetPath)
        {
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            bool needsBake = false;
            foreach (MeshFilter filter in filters)
                if (filter.sharedMesh != null && !filter.sharedMesh.isReadable) needsBake = true;
            if (!needsBake) return false;
            string signature = Signature(prefab, filters);
            string sourceHash = UnityEditor.AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
            string directory = "Assets/Resources/ReefSupport";
            string path = Path.Combine(directory, prefab.name + ".bytes");
            if (File.Exists(path))
            {
                try
                {
                    using (var reader = new BinaryReader(File.OpenRead(path)))
                    {
                        if (reader.ReadInt32() == BinaryMagic && reader.ReadInt32() == BinaryVersion &&
                            reader.ReadString() == prefab.name && reader.ReadString() == signature &&
                            reader.ReadString() == sourceHash)
                        {
                            int count = reader.ReadInt32();
                            if (count > 0 && (long)count * 12 <= reader.BaseStream.Length - reader.BaseStream.Position - 4)
                            {
                                reader.BaseStream.Seek((long)count * 12, SeekOrigin.Current);
                                int indexCount = reader.ReadInt32();
                                if (indexCount >= 0 && indexCount % 3 == 0 &&
                                    (long)indexCount * 4 == reader.BaseStream.Length - reader.BaseStream.Position) return false;
                            }
                        }
                    }
                }
                catch (IOException) { /* Replace incomplete bake. */ }
            }
            Vector3[] points;
            int[] indices;
            Collect(prefab, filters, true, true, out points, out indices);
            if (points.Length == 0) throw new InvalidDataException("[ReefSupport] Empty source " + assetPath);
            Directory.CreateDirectory(directory);
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(BinaryMagic); writer.Write(BinaryVersion);
                writer.Write(prefab.name); writer.Write(signature); writer.Write(sourceHash);
                writer.Write(points.Length);
                foreach (Vector3 point in points) { writer.Write(point.x); writer.Write(point.y); writer.Write(point.z); }
                writer.Write(indices.Length);
                foreach (int index in indices) writer.Write(index);
            }
            Debug.Log("[ReefSupport] Baked unreadable asset " + assetPath + " -> " + path);
            return true;
        }
#endif
    }
}

#if UNITY_EDITOR
namespace SwanUnderwater.Editor
{
    // Kept in this assembly so the runtime geometry type can remain internal.
    public sealed class ReefSupportGeometryBake : UnityEditor.Build.IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report) => Run();

        [UnityEditor.MenuItem("Swan/Codex/Bake unreadable reef support geometry")]
        public static void Run()
        {
            const string directory = "Assets/Resources/RealisticReef";
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            int written = 0;
            foreach (string path in Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                string extension = Path.GetExtension(path);
                if (!string.Equals(extension, ".glb", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new InvalidOperationException("[ReefSupport] Cannot import " + path);
                if (ReefSupportGeometry.BakeIfUnreadable(prefab, path)) written++;
            }
            if (written > 0) UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[ReefSupport] Unreadable geometry preprocessing complete; written=" + written);
        }
    }
}
#endif

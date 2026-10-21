#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater.Editor
{
    /// <summary>Read-only measurement of the exact generated LOD0 used by the close-up preview.</summary>
    public static class OceanReefGroundingDiagnostic
    {
        [Serializable] sealed class GapStats
        {
            public int count;
            public float min, p05, p35, median, p90, max;
            public float within2cmFraction, supported15mmFraction, above2cmFraction, above5cmFraction,
                above10cmFraction, penetratingOver3cmFraction;
        }
        [Serializable] sealed class SetReport
        {
            public string name, sampling;
            public int points, rockSupportedPoints;
            public GapStats seabedOnly, seabedAndRockLod0;
        }
        [Serializable] sealed class RockReport
        {
            public string hierarchy;
            public Vector3 min, max;
            public int vertices, triangles;
        }
        [Serializable] sealed class Report
        {
            public string screenshot, group, csv;
            public Vector3 cameraPosition, cameraEuler, boundsMin, boundsMax;
            public int schemaVersion = 1, lodIndex = 0, groupIndex, meshVertexCount, triangleReferencedVertexCount,
                uniqueReferencedPositions;
            public float modelLength;
            public bool productionSampleMatched;
            public ReefHabitat.GroundingSample productionSample;
            public ReefHabitat.GroundingSummary productionSummary;
            public List<RockReport> rocks = new List<RockReport>();
            public List<SetReport> samples = new List<SetReport>();
            public float nativeStyleUnappliedDeltaY;
            public bool nativeStyleUnappliedCapReached;
            public string scope = "Read-only generated Unity LOD0 mesh positions/triangles at the screenshot camera. " +
                "All gap values are metres: positive is above a surface, negative is penetration. " +
                "Rock support is the highest actual triangle at the same XZ across nearby LOD0 rocks, regardless of " +
                "whether the point is visible from this camera; bounds only reject non-overlapping candidates. " +
                "No collider, contact shadow or renderer bounds are substituted for a support surface. " +
                "Lower-band vertices and one-point-per-XZ-cell samples have different denominators. " +
                "Finite point samples do not measure continuous contact area or establish aesthetic acceptance. " +
                "The native-style delta is diagnostic only and is never applied. Not headset evidence.";
        }
        sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<int> triangles = new List<int>();
            public Bounds bounds;
        }
        struct Face
        {
            public Vector3 a, b, c;
            public double denominator;
            public int rock;
        }
        sealed class Surfaces
        {
            const float CellSize = .5f;
            readonly List<Face> faces = new List<Face>();
            readonly Dictionary<Vector2Int, List<int>> bins = new Dictionary<Vector2Int, List<int>>();
            public void Add(Geometry mesh, int rock)
            {
                for (int i = 0; i < mesh.triangles.Count; i += 3)
                {
                    Vector3 a = mesh.vertices[mesh.triangles[i]], b = mesh.vertices[mesh.triangles[i + 1]], c = mesh.vertices[mesh.triangles[i + 2]];
                    double den = ((double)b.z - c.z) * ((double)a.x - c.x) + ((double)c.x - b.x) * ((double)a.z - c.z);
                    if (Math.Abs(den) <= 1e-20) continue;
                    int index = faces.Count;
                    faces.Add(new Face { a = a, b = b, c = c, denominator = den, rock = rock });
                    int x0 = Cell(Mathf.Min(a.x, b.x, c.x)), x1 = Cell(Mathf.Max(a.x, b.x, c.x));
                    int z0 = Cell(Mathf.Min(a.z, b.z, c.z)), z1 = Cell(Mathf.Max(a.z, b.z, c.z));
                    for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!bins.TryGetValue(key, out var list)) bins.Add(key, list = new List<int>());
                        list.Add(index);
                    }
                }
            }
            static int Cell(float value) => Mathf.FloorToInt(value / CellSize);
            public float Height(Vector3 p, float floor, out int rock)
            {
                rock = -1; double top = floor;
                if (!bins.TryGetValue(new Vector2Int(Cell(p.x), Cell(p.z)), out var list)) return floor;
                foreach (int index in list)
                {
                    var f = faces[index];
                    double u = (((double)f.b.z - f.c.z) * ((double)p.x - f.c.x) + ((double)f.c.x - f.b.x) * ((double)p.z - f.c.z)) / f.denominator;
                    double v = (((double)f.c.z - f.a.z) * ((double)p.x - f.c.x) + ((double)f.a.x - f.c.x) * ((double)p.z - f.c.z)) / f.denominator;
                    if (u < -1e-7 || v < -1e-7 || u + v > 1.0000001) continue;
                    double y = u * f.a.y + v * f.b.y + (1 - u - v) * f.c.y;
                    if (y > top) { top = y; rock = f.rock; }
                }
                return (float)top;
            }
        }

        public static string Capture(ReefProximityLod reef, int groupIndex, Camera camera,
            string screenshot, string outputDirectory, int width, int height)
        {
            var sample = reef.SampleForValidation(groupIndex);
            var group = sample.group;
            Geometry actual = ReadLod0(group);
            var referenced = new HashSet<int>(actual.triangles);
            var indexed = new List<Vector3>();
            foreach (int index in referenced) indexed.Add(actual.vertices[index]);
            var unique = new List<Vector3>(new HashSet<Vector3>(indexed));
            unique.Sort(ComparePosition);
            Bounds localBounds = new Bounds(group.transform.InverseTransformPoint(actual.vertices[0]), Vector3.zero);
            foreach (var p in actual.vertices) localBounds.Encapsulate(group.transform.InverseTransformPoint(p));
            // Placement adds yaw only at the group: this length is independent of that yaw, matching
            // the native exporter's original-model longest side multiplied by its uniform fit scale.
            float length = Mathf.Max(localBounds.size.x, localBounds.size.y, localBounds.size.z);
            string stem = "grounding-" + Path.GetFileNameWithoutExtension(screenshot);
            var report = new Report { screenshot = screenshot, group = Hierarchy(group.transform), groupIndex = groupIndex,
                csv = stem + ".csv", cameraPosition = camera.transform.position, cameraEuler = camera.transform.eulerAngles,
                boundsMin = actual.bounds.min, boundsMax = actual.bounds.max, modelLength = length,
                meshVertexCount = actual.vertices.Count, triangleReferencedVertexCount = referenced.Count,
                uniqueReferencedPositions = unique.Count, productionSummary = ReefHabitat.LastGrounding };
            foreach (var saved in ReefHabitat.GroundingSamples)
                if (saved.coral && (saved.position - group.transform.position).sqrMagnitude < 1e-10f)
                { report.productionSample = saved; report.productionSampleMatched = true; break; }
            var surfaces = new Surfaces();
            for (int i = 0; i < reef.RegisteredCount; i++)
            {
                var rock = reef.SampleForValidation(i);
                if (!rock.group.name.StartsWith("Reef ", StringComparison.Ordinal)) continue;
                if (!OverlapsXZ(actual.bounds, rock.bounds)) continue;
                Geometry geometry = ReadLod0(rock.group);
                if (!OverlapsXZ(actual.bounds, geometry.bounds)) continue;
                surfaces.Add(geometry, report.rocks.Count);
                report.rocks.Add(new RockReport { hierarchy = Hierarchy(rock.group.transform), min = geometry.bounds.min,
                    max = geometry.bounds.max, vertices = geometry.vertices.Count, triangles = geometry.triangles.Count / 3 });
            }
            var csv = new StringBuilder("set,index,x,y,z,seabedY,supportY,gapSeabed,gapSupport,rockIndex,pixelX,pixelY,viewDepth,inFrame\n");
            AddSet(report, csv, "referenced_vertices_low12pct", "Every triangle-referenced vertex index in lowest 12% of world Y extent; UV/normal seam duplicates retained, vertex-density weighted.",
                Band(indexed, actual.bounds, .12f), surfaces, camera, width, height);
            AddSet(report, csv, "unique_positions_low12pct", "Exact-position welded triangle-referenced vertices in the same band; still vertex-density weighted, not contact area.",
                Band(unique, actual.bounds, .12f), surfaces, camera, width, height);
            AddSet(report, csv, "production_lod0_8x8_low12pct", "Reconstruction of production LOD0 LowContacts: all mesh vertices, lowest 12% Y, one lowest actual vertex per occupied 8x8 cell in yaw-aligned group XZ. This is not the saved multi-LOD/assigned-rock metric.",
                ProductionEnvelope(actual.vertices, group.transform, localBounds), surfaces, camera, width, height);
            List<Vector3> native = NativeEnvelope(unique, actual.bounds, length);
            var nativeGaps = AddSet(report, csv, "native_style_low10pct_xz_envelope", "Native export_coral_grounding.py comparison: exact welded referenced positions; lowest 10% height, world-XZ cells of 2.5% yaw-independent model length, one lowest vertex per cell. Same algorithm, Unity scene surfaces/layout, not a native runtime result.",
                native, surfaces, camera, width, height);
            int supported = nativeGaps.FindAll(g => g <= .015f).Count;
            if (supported / (float)nativeGaps.Count < .35f)
            {
                nativeGaps.Sort();
                float needed = Mathf.Max(0, nativeGaps[Mathf.Max(0, Mathf.CeilToInt(nativeGaps.Count * .35f) - 1)] + .015f);
                report.nativeStyleUnappliedDeltaY = -Mathf.Min(length * .15f, needed);
                report.nativeStyleUnappliedCapReached = needed > length * .15f;
            }
            string path = Path.Combine(outputDirectory, stem + ".json");
            File.WriteAllText(Path.Combine(outputDirectory, report.csv), csv.ToString(), new UTF8Encoding(false));
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            Debug.Log("REEF_GROUNDING_DIAGNOSTIC " + path + "; read-only sampled geometry, no visual acceptance");
            return Path.GetFileName(path);
        }
        static Geometry ReadLod0(LODGroup group)
        {
            var geometry = new Geometry(); var seen = new HashSet<MeshFilter>();
            var lods = group.GetLODs();
            if (lods.Length == 0) throw new InvalidOperationException("Missing LOD0: " + group.name);
            foreach (var renderer in lods[0].renderers)
            {
                if (renderer == null) throw new InvalidOperationException("Null LOD0 renderer: " + group.name);
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("Expected real static reef mesh: " + renderer.name);
                if (!seen.Add(filter)) continue;
                using (var array = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
                {
                    Mesh.MeshData data = array[0]; int offset = geometry.vertices.Count;
                    using (var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory))
                    {
                        data.GetVertices(vertices);
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            Vector3 p = filter.transform.TransformPoint(vertices[i]);
                            if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) throw new InvalidOperationException("Non-finite reef vertex");
                            geometry.vertices.Add(p);
                        }
                    }
                    for (int sub = 0; sub < data.subMeshCount; sub++)
                    {
                        var descriptor = data.GetSubMesh(sub);
                        if (descriptor.topology != MeshTopology.Triangles) continue;
                        using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory))
                        {
                            data.GetIndices(indices, sub, true);
                            for (int i = 0; i < indices.Length; i++) geometry.triangles.Add(offset + indices[i]);
                        }
                    }
                }
            }
            if (geometry.vertices.Count == 0 || geometry.triangles.Count == 0 || geometry.triangles.Count % 3 != 0)
                throw new InvalidOperationException("Empty/incomplete real reef triangles: " + group.name);
            geometry.bounds = new Bounds(geometry.vertices[0], Vector3.zero);
            foreach (var p in geometry.vertices) geometry.bounds.Encapsulate(p);
            return geometry;
        }
        static List<Vector3> Band(List<Vector3> points, Bounds bounds, float fraction) =>
            points.FindAll(p => p.y <= bounds.min.y + bounds.size.y * fraction);
        static List<Vector3> ProductionEnvelope(List<Vector3> points, Transform group, Bounds local)
        {
            var cells = new Dictionary<Vector2Int, Vector3>();
            foreach (var p in points)
            {
                var q = group.InverseTransformPoint(p);
                if (q.y > local.min.y + local.size.y * .12f) continue;
                var key = new Vector2Int(Mathf.Clamp(Mathf.FloorToInt((q.x - local.min.x) / local.size.x * 8), 0, 7),
                    Mathf.Clamp(Mathf.FloorToInt((q.z - local.min.z) / local.size.z * 8), 0, 7));
                if (!cells.TryGetValue(key, out var old) || p.y < old.y) cells[key] = p;
            }
            var result = new List<Vector3>(cells.Values); result.Sort(ComparePosition); return result;
        }
        static List<Vector3> NativeEnvelope(List<Vector3> points, Bounds bounds, float length)
        {
            var cells = new Dictionary<Vector2Int, Vector3>();
            foreach (var p in Band(points, bounds, .10f))
            {
                var key = new Vector2Int(Mathf.FloorToInt((p.x - bounds.min.x) / (length * .025f)),
                    Mathf.FloorToInt((p.z - bounds.min.z) / (length * .025f)));
                if (!cells.TryGetValue(key, out var old) || p.y < old.y) cells[key] = p;
            }
            var result = new List<Vector3>(cells.Values); result.Sort(ComparePosition); return result;
        }
        static int ComparePosition(Vector3 a, Vector3 b)
        { int x = a.x.CompareTo(b.x); if (x != 0) return x; int z = a.z.CompareTo(b.z); return z != 0 ? z : a.y.CompareTo(b.y); }
        static bool OverlapsXZ(Bounds a, Bounds b) => a.min.x <= b.max.x && a.max.x >= b.min.x && a.min.z <= b.max.z && a.max.z >= b.min.z;
        static string Hierarchy(Transform value) => value.parent == null ? value.name : Hierarchy(value.parent) + "/" + value.name + "[" + value.GetSiblingIndex() + "]";
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        static List<float> AddSet(Report report, StringBuilder csv, string name, string sampling, List<Vector3> points,
            Surfaces support, Camera camera, int width, int height)
        {
            if (points.Count == 0) throw new InvalidOperationException("Empty root sample set " + name);
            var floorGaps = new List<float>(); var gaps = new List<float>(); int rocks = 0;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i]; float floor = UnderwaterWorld.SampleRenderedSeabedHeight(p.x, p.z);
                float top = support.Height(p, floor, out int rock); if (rock >= 0) rocks++;
                float fg = p.y - floor, gap = p.y - top; floorGaps.Add(fg); gaps.Add(gap);
                Vector3 viewport = camera.WorldToViewportPoint(p);
                csv.Append(name).Append(',').Append(i);
                foreach (float value in new[] { p.x, p.y, p.z, floor, top, fg, gap }) csv.Append(',').Append(F(value));
                csv.Append(',').Append(rock).Append(',').Append(F(viewport.x * width)).Append(',').Append(F((1 - viewport.y) * height))
                    .Append(',').Append(F(viewport.z)).Append(',').Append(viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1 ? 1 : 0).Append('\n');
            }
            report.samples.Add(new SetReport { name = name, sampling = sampling, points = points.Count, rockSupportedPoints = rocks,
                seabedOnly = Stats(floorGaps), seabedAndRockLod0 = Stats(gaps) });
            return gaps;
        }
        static GapStats Stats(List<float> values)
        {
            var v = new List<float>(values); v.Sort(); int n = v.Count;
            return new GapStats { count = n, min = v[0], p05 = Percentile(v, .05f), p35 = Percentile(v, .35f), median = Percentile(v, .5f),
                p90 = Percentile(v, .9f), max = v[n - 1], within2cmFraction = v.FindAll(g => Mathf.Abs(g) <= .02f).Count / (float)n,
                supported15mmFraction = v.FindAll(g => g <= .015f).Count / (float)n, above2cmFraction = v.FindAll(g => g > .02f).Count / (float)n,
                above5cmFraction = v.FindAll(g => g > .05f).Count / (float)n, above10cmFraction = v.FindAll(g => g > .1f).Count / (float)n,
                penetratingOver3cmFraction = v.FindAll(g => g < -.03f).Count / (float)n };
        }
        static float Percentile(List<float> v, float p)
        { float at = (v.Count - 1) * p; int low = Mathf.FloorToInt(at); return Mathf.Lerp(v[low], v[Mathf.Min(low + 1, v.Count - 1)], at - low); }
    }
}
#endif

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SwanUnderwater.Editor
{
    /// <summary>Read-only numerical evidence; never creates or changes terrain or a scene.</summary>
    public static class SeabedOverlapAudit
    {
        const int NearCells = 160;
        const float MinX = -40f, MinZ = -44f, NearStep = 0.5f;

        [Serializable]
        public sealed class Report
        {
            public string utc, unity, scope;
            public bool currentMeshesFound;
            public int samples, positiveVertices, boundarySamples, boundaryMisses;
            public float positiveArea, totalArea, maximumPenetration, maximumBoundaryGap;
            public Vector3 worstPoint;
        }

        // Can run alone using -executeMethod SwanUnderwater.Editor.SeabedOverlapAudit.Run.
        // Current-mesh evidence is available only after the runtime world has been created.
        [MenuItem("Swan/Codex/Audit seabed overlap (read only)")]
        public static void Run()
        {
            string output = Path.GetFullPath(Path.Combine("Validation", "iteration-3", "seabed-audit",
                DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")));
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-seabedAuditOutput") output = Path.GetFullPath(args[i + 1]);
            AuditOldLayout(output);
            AuditCurrentMeshes(output);
        }

        public static void AuditOldLayout(string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            var report = NewReport("Old geometry: 12.5m grid H-0.12 versus 0.5m grid H+0.035, " +
                "using the current Unity terrain sampler. The aligned, same-diagonal grids are nested, " +
                "so their piecewise-linear height difference reaches its exact maximum at a fine-grid vertex. " +
                "Positive area is integrated exactly over the fine triangles (up to float roundoff).");
            var difference = new float[(NearCells + 1) * (NearCells + 1)];
            report.maximumPenetration = float.NegativeInfinity;
            using (var writer = new StreamWriter(Path.Combine(outputDirectory, "old-layout-samples.csv")))
            {
                writer.WriteLine("x,z,coarse_y,fine_y,coarse_minus_fine");
                for (int z = 0; z <= NearCells; z++)
                for (int x = 0; x <= NearCells; x++)
                {
                    float wx = MinX + x * NearStep, wz = MinZ + z * NearStep;
                    float coarse = OldCoarseHeight(wx, wz);
                    float fine = UnderwaterWorld.SampleTerrainHeight(wx, wz) + 0.035f;
                    float delta = coarse - fine;
                    difference[z * (NearCells + 1) + x] = delta;
                    report.samples++;
                    if (delta > 0f) report.positiveVertices++;
                    if (delta > report.maximumPenetration)
                    {
                        report.maximumPenetration = delta;
                        report.worstPoint = new Vector3(wx, coarse, wz);
                    }
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R},{3:R},{4:R}",
                        wx, wz, coarse, fine, delta));
                }
            }
            for (int z = 0; z < NearCells; z++)
            for (int x = 0; x < NearCells; x++)
            {
                int a = z * (NearCells + 1) + x, b = a + 1, c = a + NearCells + 1, d = c + 1;
                report.positiveArea += 0.125f * (PositiveFraction(difference[a], difference[c], difference[b]) +
                    PositiveFraction(difference[b], difference[c], difference[d]));
            }
            report.totalArea = 80f * 80f;
            Save(outputDirectory, "old-layout.json", report);
            Debug.Log($"[SeabedAudit] old overlapArea={report.positiveArea:F3}/{report.totalArea:F1}m2, " +
                $"maximumPenetration={report.maximumPenetration:F4}m at {report.worstPoint}; " +
                $"positiveVertices={report.positiveVertices}/{report.samples}; {outputDirectory}");
        }

        // Call from the preview runner after UnderwaterWorld has generated both meshes.
        public static void AuditCurrentMeshes(string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            var report = NewReport("Actual generated world meshes: base XZ overlap with the near rectangular footprint " +
                "and absolute height difference on every near boundary vertex. No mesh, material or scene writes.");
            MeshFilter coarse = null, fine = null;
            foreach (MeshFilter filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (filter.sharedMesh == null) continue;
                if (filter.sharedMesh.name == "SeabedTerrain") coarse = filter;
                if (filter.sharedMesh.name == "NearSeabedTerrain") fine = filter;
            }
            report.currentMeshesFound = coarse != null && fine != null;
            if (!report.currentMeshesFound)
            {
                Save(outputDirectory, "current-layout.json", report);
                Debug.LogWarning("[SeabedAudit] Current meshes unavailable; call AuditCurrentMeshes after world creation. " + outputDirectory);
                return;
            }
            Vector3[] cv = WorldVertices(coarse), fv = WorldVertices(fine);
            int[] indices = coarse.sharedMesh.triangles;
            Bounds footprint = new Bounds(fv[0], Vector3.zero);
            foreach (Vector3 p in fv) footprint.Encapsulate(p);
            var polygon = new List<Vector2>(8);
            var scratch = new List<Vector2>(8);
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = cv[indices[i]], b = cv[indices[i + 1]], c = cv[indices[i + 2]];
                if (Mathf.Max(a.x, b.x, c.x) <= footprint.min.x || Mathf.Min(a.x, b.x, c.x) >= footprint.max.x ||
                    Mathf.Max(a.z, b.z, c.z) <= footprint.min.z || Mathf.Min(a.z, b.z, c.z) >= footprint.max.z) continue;
                polygon.Clear();
                polygon.Add(new Vector2(a.x, a.z)); polygon.Add(new Vector2(b.x, b.z)); polygon.Add(new Vector2(c.x, c.z));
                Clip(ref polygon, ref scratch, 0, footprint.min.x, true);
                Clip(ref polygon, ref scratch, 0, footprint.max.x, false);
                Clip(ref polygon, ref scratch, 1, footprint.min.z, true);
                Clip(ref polygon, ref scratch, 1, footprint.max.z, false);
                float area = 0f;
                for (int j = 0; j < polygon.Count; j++)
                {
                    Vector2 p = polygon[j], q = polygon[(j + 1) % polygon.Count];
                    area += p.x * q.y - p.y * q.x;
                }
                report.positiveArea += Mathf.Abs(area) * 0.5f;
            }
            foreach (Vector3 p in fv)
            {
                if (Mathf.Abs(p.x - footprint.min.x) > 0.001f && Mathf.Abs(p.x - footprint.max.x) > 0.001f &&
                    Mathf.Abs(p.z - footprint.min.z) > 0.001f && Mathf.Abs(p.z - footprint.max.z) > 0.001f) continue;
                report.boundarySamples++;
                float height;
                if (!TryHeight(p, cv, indices, out height)) { report.boundaryMisses++; continue; }
                float gap = Mathf.Abs(height - p.y);
                if (gap > report.maximumBoundaryGap) { report.maximumBoundaryGap = gap; report.worstPoint = p; }
            }
            report.totalArea = footprint.size.x * footprint.size.z;
            report.samples = fv.Length;
            Save(outputDirectory, "current-layout.json", report);
            Debug.Log($"[SeabedAudit] current baseOverlapArea={report.positiveArea:F5}m2, " +
                $"boundaryMaxGap={report.maximumBoundaryGap:F6}m, missingBoundary={report.boundaryMisses}/{report.boundarySamples}; {outputDirectory}");
        }

        static Report NewReport(string scope) => new Report { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion, scope = scope };
        static void Save(string directory, string name, Report report) => File.WriteAllText(Path.Combine(directory, name), JsonUtility.ToJson(report, true));
        static Vector3[] WorldVertices(MeshFilter filter)
        {
            Vector3[] vertices = filter.sharedMesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = filter.transform.TransformPoint(vertices[i]);
            return vertices;
        }
        static float OldCoarseHeight(float x, float z)
        {
            float x0 = Mathf.Floor(x / 12.5f) * 12.5f, z0 = Mathf.Floor(z / 12.5f) * 12.5f;
            float u = (x - x0) / 12.5f, v = (z - z0) / 12.5f;
            float a = UnderwaterWorld.SampleTerrainHeight(x0, z0) - 0.12f;
            float b = UnderwaterWorld.SampleTerrainHeight(x0 + 12.5f, z0) - 0.12f;
            float c = UnderwaterWorld.SampleTerrainHeight(x0, z0 + 12.5f) - 0.12f;
            if (u + v <= 1f) return a * (1f - u - v) + b * u + c * v;
            float d = UnderwaterWorld.SampleTerrainHeight(x0 + 12.5f, z0 + 12.5f) - 0.12f;
            return b * (1f - v) + c * (1f - u) + d * (u + v - 1f);
        }
        static float PositiveFraction(float a, float b, float c)
        {
            if (a <= 0f && b <= 0f && c <= 0f) return 0f;
            if (a >= 0f && b >= 0f && c >= 0f) return 1f;
            if (a * b >= 0f) return c > 0f ? c / (c - a) * c / (c - b) : 1f - c / (c - a) * c / (c - b);
            if (a * c >= 0f) return b > 0f ? b / (b - a) * b / (b - c) : 1f - b / (b - a) * b / (b - c);
            return a > 0f ? a / (a - b) * a / (a - c) : 1f - a / (a - b) * a / (a - c);
        }
        static void Clip(ref List<Vector2> polygon, ref List<Vector2> scratch, int axis, float boundary, bool minimum)
        {
            scratch.Clear();
            if (polygon.Count == 0) return;
            Vector2 previous = polygon[polygon.Count - 1];
            bool previousInside = minimum ? previous[axis] >= boundary : previous[axis] <= boundary;
            foreach (Vector2 current in polygon)
            {
                bool inside = minimum ? current[axis] >= boundary : current[axis] <= boundary;
                if (inside != previousInside) scratch.Add(Vector2.LerpUnclamped(previous, current,
                    (boundary - previous[axis]) / (current[axis] - previous[axis])));
                if (inside) scratch.Add(current);
                previous = current; previousInside = inside;
            }
            List<Vector2> swap = polygon; polygon = scratch; scratch = swap;
        }
        static bool TryHeight(Vector3 p, Vector3[] vertices, int[] indices, out float height)
        {
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                if (p.x < Mathf.Min(a.x, b.x, c.x) - 0.0001f || p.x > Mathf.Max(a.x, b.x, c.x) + 0.0001f ||
                    p.z < Mathf.Min(a.z, b.z, c.z) - 0.0001f || p.z > Mathf.Max(a.z, b.z, c.z) + 0.0001f) continue;
                float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(denominator) < 0.000001f) continue;
                float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / denominator;
                float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / denominator;
                if (u < -0.0001f || v < -0.0001f || u + v > 1.0001f) continue;
                height = u * a.y + v * b.y + (1f - u - v) * c.y;
                return true;
            }
            height = 0f;
            return false;
        }
    }
}

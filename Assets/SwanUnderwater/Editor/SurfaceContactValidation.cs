using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SwanUnderwater.Editor
{
    // Queries the generated mesh triangles, independently of the procedural height
    // sampler used for placement. Editor-only; no colliders or player overhead.
    public static class SurfaceContactValidation
    {
        [Serializable] public sealed class Report
        {
            public string utc;
            public int samplerPoints, samplerMisses, grassRootVertices, floatingGrassRoots, grassMisses;
            public int shells, unsupportedShells, shellVertexMisses;
            public float minimumShellContact = float.PositiveInfinity, maximumShellContact = float.NegativeInfinity;
            public float maximumSamplerError, minimumGrassGap = float.PositiveInfinity, maximumGrassGap = float.NegativeInfinity;
            public bool passed;
        }
        struct Triangle { public Vector3 a, b, c; }
        sealed class Ground
        {
            readonly Dictionary<Vector2Int, List<Triangle>> cells = new Dictionary<Vector2Int, List<Triangle>>();
            public void Add(MeshFilter filter)
            {
                Mesh mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                int[] indices = mesh.triangles;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = filter.transform.TransformPoint(vertices[i]);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var t = new Triangle { a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]] };
                    float minX = Mathf.Min(t.a.x, t.b.x, t.c.x), maxX = Mathf.Max(t.a.x, t.b.x, t.c.x);
                    float minZ = Mathf.Min(t.a.z, t.b.z, t.c.z), maxZ = Mathf.Max(t.a.z, t.b.z, t.c.z);
                    if (maxX < -100f || minX > 100f || maxZ < -104f || minZ > 100f) continue;
                    for (int z = Mathf.FloorToInt(minZ / 2f); z <= Mathf.FloorToInt(maxZ / 2f); z++)
                    for (int x = Mathf.FloorToInt(minX / 2f); x <= Mathf.FloorToInt(maxX / 2f); x++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!cells.TryGetValue(key, out var bucket)) cells.Add(key, bucket = new List<Triangle>());
                        bucket.Add(t);
                    }
                }
            }
            public bool Height(float x, float z, out float height)
            {
                height = float.NegativeInfinity;
                if (!cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(x / 2f), Mathf.FloorToInt(z / 2f)), out var bucket)) return false;
                foreach (Triangle t in bucket)
                {
                    float den = (t.b.z - t.c.z) * (t.a.x - t.c.x) + (t.c.x - t.b.x) * (t.a.z - t.c.z);
                    if (Mathf.Abs(den) < 0.000001f) continue;
                    float u = ((t.b.z - t.c.z) * (x - t.c.x) + (t.c.x - t.b.x) * (z - t.c.z)) / den;
                    float v = ((t.c.z - t.a.z) * (x - t.c.x) + (t.a.x - t.c.x) * (z - t.c.z)) / den;
                    if (u < -0.00001f || v < -0.00001f || u + v > 1.00001f) continue;
                    height = Mathf.Max(height, u * t.a.y + v * t.b.y + (1f - u - v) * t.c.y);
                }
                return !float.IsNegativeInfinity(height);
            }
        }

        public static void Capture(string directory)
        {
            var ground = new Ground();
            var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            MeshFilter[] all = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (MeshFilter filter in all)
                if (filter.sharedMesh != null && (filter.sharedMesh.name == "SeabedTerrain" || filter.sharedMesh.name == "NearSeabedTerrain")) ground.Add(filter);
            var random = new System.Random(41002026);
            for (int i = 0; i < 2000; i++)
            {
                float radius = i < 1000 ? 40f : 96f;
                float x = ((float)random.NextDouble() * 2f - 1f) * radius;
                float z = ((float)random.NextDouble() * 2f - 1f) * radius - 4f;
                report.samplerPoints++;
                if (!ground.Height(x, z, out float y)) { report.samplerMisses++; continue; }
                report.maximumSamplerError = Mathf.Max(report.maximumSamplerError, Mathf.Abs(y - UnderwaterWorld.SampleRenderedSeabedHeight(x, z)));
            }
            foreach (MeshFilter filter in all)
            {
                if (filter.sharedMesh == null || !filter.sharedMesh.name.StartsWith("Seagrass quadrant ")) continue;
                Vector3[] vertices = filter.sharedMesh.vertices;
                Vector2[] uv = filter.sharedMesh.uv;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (uv[i].y > 0.00001f) continue;
                    Vector3 p = filter.transform.TransformPoint(vertices[i]);
                    report.grassRootVertices++;
                    if (!ground.Height(p.x, p.z, out float y)) { report.grassMisses++; continue; }
                    float gap = p.y - y;
                    report.minimumGrassGap = Mathf.Min(report.minimumGrassGap, gap);
                    report.maximumGrassGap = Mathf.Max(report.maximumGrassGap, gap);
                    if (gap > 0.003f) report.floatingGrassRoots++;
                }
            }
            report.passed = report.samplerPoints == 2000 && report.samplerMisses == 0 && report.maximumSamplerError < 0.0001f &&
                report.grassRootVertices > 0 && report.grassMisses == 0 && report.floatingGrassRoots == 0;
            var shellField = UnityEngine.Object.FindFirstObjectByType<SeabedShells>();
            if (shellField != null)
            {
                var filters = new Dictionary<int, MeshFilter>();
                var verticesByChunk = new Dictionary<int, Vector3[]>();
                foreach (MeshFilter filter in shellField.GetComponentsInChildren<MeshFilter>())
                {
                    int chunk = int.Parse(filter.sharedMesh.name.Substring("Seabed shell quadrant ".Length));
                    filters.Add(chunk, filter);
                    verticesByChunk.Add(chunk, filter.sharedMesh.vertices);
                }
                foreach (SeabedShells.Placement shell in shellField.Placements)
                {
                    report.shells++;
                    float nearest = float.PositiveInfinity;
                    var filter = filters[shell.chunk];
                    var vertices = verticesByChunk[shell.chunk];
                    for (int i = shell.firstVertex + shell.surfaceVertexCount; i < shell.firstVertex + shell.vertexCount; i++)
                    {
                        Vector3 p = filter.transform.TransformPoint(vertices[i]);
                        if (!ground.Height(p.x, p.z, out float y)) { report.shellVertexMisses++; continue; }
                        nearest = Mathf.Min(nearest, p.y - y);
                    }
                    report.minimumShellContact = Mathf.Min(report.minimumShellContact, nearest);
                    report.maximumShellContact = Mathf.Max(report.maximumShellContact, nearest);
                    if (nearest > 0.003f || nearest < -0.005f) report.unsupportedShells++;
                }
            }
            report.passed &= report.shells >= 50 && report.unsupportedShells == 0 && report.shellVertexMisses == 0;
            File.WriteAllText(Path.Combine(directory, "reef-grounding.json"), JsonUtility.ToJson(ReefHabitat.LastGrounding, true));
            File.WriteAllText(Path.Combine(directory, "surface-contact.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[SurfaceContact] " + JsonUtility.ToJson(report));
            if (!report.passed) throw new InvalidOperationException("Generated surface/grass contact audit failed; see surface-contact.json.");
        }
    }
}

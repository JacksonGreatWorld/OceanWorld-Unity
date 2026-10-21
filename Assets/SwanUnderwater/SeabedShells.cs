using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>Small, closed shell valves made locally; no downloaded models or per-shell objects.</summary>
    public sealed class SeabedShells : MonoBehaviour
    {
        public enum ShellKind { Scallop, Clam, Cockle }
        public struct Placement
        {
            public Vector3 center;
            public Bounds bounds;
            public ShellKind kind;
            public bool interiorUp;
            public float width, height;
            // Mesh-local vertex range also makes independent Editor ground-contact checks possible.
            public int chunk, firstVertex, vertexCount, surfaceVertexCount;
        }

        public const int MaximumShells = 84;
        public const int MaximumTriangles = MaximumShells * 252;
        const int AngularSegments = 20;
        const int RadialRings = 3;
        const float FanHalfAngle = 1.35f;
        readonly List<Object> owned = new List<Object>();
        readonly List<Placement> placements = new List<Placement>();
        public IReadOnlyList<Placement> Placements => placements;
        public int TriangleCount { get; private set; }
        public int ChunkCount { get; private set; }

        sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Vector2> shape = new List<Vector2>();
            public readonly List<Color> colors = new List<Color>();
            public readonly List<int> triangles = new List<int>();
        }

        /// <summary>Call after the reef and seagrass. Destroy the returned GameObject to release generated resources.</summary>
        public static SeabedShells Build(Transform parent, Vector3 spawn, bool sanctuary = false)
        {
            var go = new GameObject("Scattered Seabed Shells");
            go.transform.position = new Vector3(spawn.x, 0f, spawn.z);
            go.transform.SetParent(parent, true);
            var shells = go.AddComponent<SeabedShells>();
            shells.Create(parent, spawn, sanctuary);
            return shells;
        }

        void Create(Transform parent, Vector3 spawn, bool sanctuary)
        {
            Shader shader = Resources.Load<Shader>("Shaders/SeabedShell");
            if (shader == null) { Debug.LogError("[SeabedShells] Missing shell shader; skipped."); return; }
            var material = new Material(shader) { name = "Weathered chalk and rose shell", enableInstancing = true };
            owned.Add(material);
            var rng = new System.Random(20092026);
            Vector2 origin = new Vector2(spawn.x, spawn.z);
            var grass = parent != null ? parent.GetComponentInChildren<SeagrassMeadow>() : null;
            List<Bounds> reefs = ReefBounds(parent);
            var chunks = new[] { new Geometry(), new Geometry(), new Geometry(), new Geometry() };

            // A few larger shells can be discovered 1–3 m ahead, on either edge of the clear passage.
            for (int attempt = 0; attempt < 240 && placements.Count < 6; attempt++)
            {
                float angle = Range(rng, 0.42f, 1.08f) * (attempt % 2 == 0 ? 1f : -1f);
                float radius = Range(rng, 1.4f, 2.9f);
                Vector2 point = origin + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius;
                TryPlace(chunks, point, origin, Range(rng, 0.145f, 0.195f), rng, reefs, grass);
            }
            int nearCount = placements.Count;

            if (sanctuary)
            {
                // Reserve a few flat-sand discoveries in every outer habitat before
                // the global budget fills. Steeper basin rims otherwise reject one
                // region's early patches while easier regions consume all 84 slots.
                for (int zone = 1; zone < OceanCoralSanctuary.ExplorationZones.Count; zone++)
                {
                    Vector2 home = origin + OceanCoralSanctuary.ExplorationZones[zone].offset;
                    int target = Mathf.Min(MaximumShells, placements.Count + 5);
                    for (int attempt = 0; attempt < 180 && placements.Count < target; attempt++)
                    {
                        float angle = Range(rng, 0f, Mathf.PI * 2f);
                        float radius = Range(rng, 2f, 18f);
                        Vector2 point = home + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius;
                        TryPlace(chunks, point, origin, Range(rng, .08f, .18f), rng, reefs, grass);
                    }
                }
            }

            // Sparse little deposits separated by open sand, rather than a regular scatter/grid.
            for (int patch = 0; patch < 240 && placements.Count < MaximumShells; patch++)
            {
                float angle = Range(rng, 0f, Mathf.PI * 2f);
                float radius = Range(rng, 3.6f, patch % 3 == 0 ? 12f : 25f);
                Vector2 center = origin + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius;
                if(sanctuary && patch%4!=0)
                {
                    int zone=1+((patch/4)*3+patch%4-1)%6;
                    Vector2 home=OceanCoralSanctuary.ExplorationZones[zone].offset;
                    center=origin+home+new Vector2(Mathf.Sin(angle),Mathf.Cos(angle))*Range(rng,3.6f,13f);
                }
                int count = rng.Next(2, 6);
                for (int i = 0; i < count && placements.Count < MaximumShells; i++)
                {
                    float offsetAngle = Range(rng, 0f, Mathf.PI * 2f);
                    float offsetRadius = Range(rng, 0.08f, 0.60f);
                    Vector2 point = center + new Vector2(Mathf.Cos(offsetAngle), Mathf.Sin(offsetAngle)) * offsetRadius;
                    TryPlace(chunks, point, origin, Range(rng, 0.08f, 0.18f), rng, reefs, grass);
                }
            }
            for (int i = 0; i < chunks.Length; i++) MakeChunk(chunks[i], i, material);
            Debug.Log($"[SeabedShells] shells={placements.Count}, near1to3m={nearCount}, triangles={TriangleCount}/{MaximumTriangles}, chunks={ChunkCount}; one opaque material; actual reef bounds and grass-root exclusion");
            for (int i = 0; i < Mathf.Min(nearCount, 6); i++)
            {
                Placement p = placements[i];
                Debug.Log($"[SeabedShells] near[{i}] {p.kind} center=({p.center.x:F3},{p.center.y:F3},{p.center.z:F3}) width={p.width:F3}m interiorUp={p.interiorUp}");
            }
            if (placements.Count < 50 || nearCount < 3)
                Debug.LogWarning("[SeabedShells] Too few clear flat placements; inspect the shell count and nearby discovery points.");
        }

        static List<Bounds> ReefBounds(Transform parent)
        {
            var result = new List<Bounds>();
            var biome = parent != null ? parent.GetComponentInChildren<OceanCampaignBiomeGeometry>() : null;
            if (biome != null) { result.AddRange(biome.Solids); return result; }
            Transform reef = parent != null ? parent.Find("Scanned Reef Habitat") : null;
            bool sanctuary=reef==null;
            if (reef == null && parent != null) reef = parent.Find("Coral Sanctuary");
            if (reef == null) return result;
            if(sanctuary)
            {
                // An outer bank contains a terrace plus sibling coral/rock LODGroups.
                // Looking only at its first child misses those grounded outcrops; using
                // one combined bank AABB would incorrectly exclude the open water too.
                var lodRenderers=new HashSet<Renderer>();
                foreach(LODGroup group in reef.GetComponentsInChildren<LODGroup>(true))
                {
                    LOD[] levels=group.GetLODs();
                    if(levels.Length>0)
                        foreach(Renderer renderer in levels[0].renderers)
                            if(renderer!=null&&!lodRenderers.Contains(renderer))result.Add(renderer.bounds);
                    foreach(LOD level in levels)
                        foreach(Renderer renderer in level.renderers)
                            if(renderer!=null)lodRenderers.Add(renderer);
                }
                foreach(Renderer renderer in reef.GetComponentsInChildren<Renderer>(true))
                    if(!lodRenderers.Contains(renderer))result.Add(renderer.bounds);
                return result;
            }
            foreach (Transform group in reef)
            {
                if (group.childCount == 0) continue;
                // Only the actual primary mesh, not the small approximate sphere collider or repeated LODs.
                Renderer[] renderers = group.GetChild(0).GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                result.Add(bounds);
            }
            return result;
        }

        void TryPlace(Geometry[] chunks, Vector2 point, Vector2 origin, float width, System.Random rng,
            List<Bounds> reefs, SeagrassMeadow grass)
        {
            Vector2 offset = point - origin;
            float clearance = width * 0.65f + 0.055f;
            if (offset.sqrMagnitude < 1.15f * 1.15f ||
                (offset.y > -3f && offset.y < 10f && Mathf.Abs(offset.x) < 0.8f + clearance)) return;
            foreach (Bounds reef in reefs)
            {
                float x = Mathf.Clamp(point.x, reef.min.x, reef.max.x);
                float z = Mathf.Clamp(point.y, reef.min.z, reef.max.z);
                if ((point - new Vector2(x, z)).sqrMagnitude < clearance * clearance) return;
            }
            if (grass != null && grass.IsNearGrass(point, clearance + 0.20f)) return;
            foreach (Placement previous in placements)
            {
                float spacing = (previous.width + width) * 0.65f + 0.04f;
                if ((point - new Vector2(previous.center.x, previous.center.z)).sqrMagnitude < spacing * spacing) return;
            }

            const float step = 0.16f;
            float ground = Ground(point.x, point.y);
            float dx = (Ground(point.x + step, point.y) - Ground(point.x - step, point.y)) / (2f * step);
            float dz = (Ground(point.x, point.y + step) - Ground(point.x, point.y - step)) / (2f * step);
            if (dx * dx + dz * dz > 0.10f) return; // Only fairly level sand, below about 18 degrees.
            float residual = 0f;
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -width * 0.55f : width * 0.55f;
                float z = i < 2 ? -width * 0.55f : width * 0.55f;
                residual = Mathf.Max(residual, Mathf.Abs(Ground(point.x + x, point.y + z) - (ground + dx * x + dz * z)));
            }
            if (residual > 0.007f) return;

            ShellKind kind = (ShellKind)(placements.Count % 3);
            bool interiorUp = placements.Count % 9 == 4;
            Geometry shell = MakeValve(kind, width, interiorUp, rng);
            int surfaceVertexCount = shell.vertices.Count / 2;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, new Vector3(-dx, 1f, -dz).normalized) *
                Quaternion.AngleAxis(Range(rng, 0f, 360f), Vector3.up);
            var anchor = new Vector3(point.x, ground, point.y);
            // Fit the rigid underside to the actual rendered triangles, then bury contact points by 1.2 mm.
            float lift = float.NegativeInfinity;
            for (int i = surfaceVertexCount; i < shell.vertices.Count; i++)
            {
                Vector3 vertex = anchor + rotation * shell.vertices[i];
                lift = Mathf.Max(lift, Ground(vertex.x, vertex.z) - vertex.y);
            }
            anchor.y += lift - 0.0012f;
            int chunkIndex = (point.x >= origin.x ? 1 : 0) + (point.y >= origin.y ? 2 : 0);
            Geometry chunk = chunks[chunkIndex];
            int first = chunk.vertices.Count;
            Bounds worldBounds = new Bounds(anchor + rotation * shell.vertices[0], Vector3.zero);
            for (int i = 0; i < shell.vertices.Count; i++)
            {
                Vector3 world = anchor + rotation * shell.vertices[i];
                worldBounds.Encapsulate(world);
                chunk.vertices.Add(transform.InverseTransformPoint(world));
                chunk.uv.Add(shell.uv[i]); chunk.shape.Add(shell.shape[i]); chunk.colors.Add(shell.colors[i]);
            }
            foreach (int index in shell.triangles) chunk.triangles.Add(first + index);
            placements.Add(new Placement { center = worldBounds.center, bounds = worldBounds, kind = kind,
                interiorUp = interiorUp, width = width, height = worldBounds.size.y, chunk = chunkIndex,
                firstVertex = first, vertexCount = shell.vertices.Count, surfaceVertexCount = surfaceVertexCount });
        }

        static Geometry MakeValve(ShellKind kind, float width, bool interiorUp, System.Random rng)
        {
            var g = new Geometry();
            bool fan = kind == ShellKind.Scallop;
            int rowSize = fan ? AngularSegments + 1 : AngularSegments;
            Color tint = Color.Lerp(new Color(0.65f, 0.61f, 0.52f), new Color(0.65f, 0.52f, 0.51f), Range(rng, 0f, 1f));
            tint *= Range(rng, 0.87f, 1.07f); tint.a = 1f;
            if (interiorUp) tint = Color.Lerp(tint, new Color(0.77f, 0.72f, 0.66f), 0.42f);
            AddSurfaceVertex(g, kind, width, 0f, 0f, interiorUp, tint);
            for (int ring = 1; ring <= RadialRings; ring++)
                for (int j = 0; j < rowSize; j++)
                    AddSurfaceVertex(g, kind, width, ring / (float)RadialRings, j / (float)AngularSegments, interiorUp, tint);
            for (int j = 0; j < AngularSegments; j++)
            {
                int next = fan ? j + 1 : (j + 1) % rowSize;
                AddTriangle(g, 0, 1 + j, 1 + next);
                for (int ring = 1; ring < RadialRings; ring++)
                {
                    int a = 1 + (ring - 1) * rowSize + j, b = 1 + ring * rowSize + j;
                    int c = 1 + (ring - 1) * rowSize + next, d = 1 + ring * rowSize + next;
                    AddTriangle(g, a, b, c); AddTriangle(g, c, b, d);
                }
            }
            int surface = g.vertices.Count, topIndices = g.triangles.Count;
            float thickness = 0.0007f + width * 0.008f;
            for (int i = 0; i < surface; i++)
            {
                g.vertices.Add(g.vertices[i] - Vector3.up * thickness);
                g.uv.Add(g.uv[i]); g.shape.Add(g.shape[i]);
                Color underside = Color.Lerp(g.colors[i], new Color(0.77f, 0.72f, 0.65f), 0.3f);
                underside.a = 1f; g.colors.Add(underside);
            }
            for (int i = 0; i < topIndices; i += 3)
                AddTriangle(g, g.triangles[i] + surface, g.triangles[i + 2] + surface, g.triangles[i + 1] + surface);
            var rim = new List<int>();
            if (fan)
            {
                rim.Add(0);
                for (int ring = 1; ring <= RadialRings; ring++) rim.Add(1 + (ring - 1) * rowSize);
                for (int j = 1; j < rowSize; j++) rim.Add(1 + (RadialRings - 1) * rowSize + j);
                for (int ring = RadialRings - 1; ring >= 1; ring--) rim.Add(1 + (ring - 1) * rowSize + rowSize - 1);
            }
            else for (int j = 0; j < rowSize; j++) rim.Add(1 + (RadialRings - 1) * rowSize + j);
            for (int i = 0; i < rim.Count; i++)
            {
                int a = rim[i], b = rim[(i + 1) % rim.Count];
                AddTriangle(g, a, a + surface, b); AddTriangle(g, b, a + surface, b + surface);
            }
            return g;
        }

        static void AddSurfaceVertex(Geometry g, ShellKind kind, float width, float radius, float angular,
            bool interiorUp, Color tint)
        {
            bool fan = kind == ShellKind.Scallop;
            float angle = fan ? Mathf.Lerp(-FanHalfAngle, FanHalfAngle, angular) : angular * Mathf.PI * 2f;
            float rib = 0.5f + 0.5f * Mathf.Cos(angular * Mathf.PI * 20f);
            float outline = 1f + (kind == ShellKind.Clam ? 0.006f : 0.018f) * rib * radius;
            float x = Mathf.Sin(angle) * width * 0.5f * radius * outline / (fan ? Mathf.Sin(FanHalfAngle) : 1f);
            float z = fan ? (Mathf.Cos(angle) * radius - 0.42f) * width * 0.83f :
                Mathf.Cos(angle) * width * (kind == ShellKind.Clam ? 0.36f : 0.45f) * radius * outline;
            float dome = fan ? Mathf.Sin(radius * Mathf.PI) : Mathf.Sqrt(Mathf.Max(0f, 1f - radius * radius));
            float height = width * (kind == ShellKind.Cockle ? 0.20f : 0.14f) * dome;
            height += width * (kind == ShellKind.Clam ? 0.002f : 0.009f) * rib * Mathf.Sin(radius * Mathf.PI);
            if (interiorUp) height = width * 0.16f - height;
            g.vertices.Add(new Vector3(x, height + 0.0015f, z));
            // Continuous planar UVs avoid a seam in the circular clam mesh's procedural rib detail.
            g.uv.Add(new Vector2(x / width, z / width));
            g.shape.Add(new Vector2(radius, (float)kind));
            g.colors.Add(tint);
        }

        static void AddTriangle(Geometry g, int a, int b, int c)
        { g.triangles.Add(a); g.triangles.Add(b); g.triangles.Add(c); }

        void MakeChunk(Geometry geometry, int index, Material material)
        {
            if (geometry.vertices.Count == 0) return;
            var mesh = new Mesh { name = "Seabed shell quadrant " + index };
            mesh.SetVertices(geometry.vertices); mesh.SetUVs(0, geometry.uv); mesh.SetUVs(1, geometry.shape);
            mesh.SetColors(geometry.colors); mesh.SetTriangles(geometry.triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh.UploadMeshData(!Application.isEditor);
            owned.Add(mesh);
            var go = new GameObject(mesh.name); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            TriangleCount += geometry.triangles.Count / 3; ChunkCount++;
        }

        static float Ground(float x, float z) => UnderwaterWorld.SampleRenderedSeabedHeight(x, z);
        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
        void OnDestroy() { foreach (Object item in owned) if (item != null) Destroy(item); owned.Clear(); }
    }
}

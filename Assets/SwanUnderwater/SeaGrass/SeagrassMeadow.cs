using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>
    /// Small, irregular meadows on the sandy edges of the scanned reef. Call Build after
    /// ReefHabitat.Build. Meshes and their one shared material belong to this component.
    /// </summary>
    public sealed class SeagrassMeadow : MonoBehaviour
    {
        public const int MaximumTufts = 1536;
        public const int MaximumTriangles = MaximumTufts * 4 * 7;
        const float MeadowRadius = 34f;
        const float RootLift = -0.025f; // Bury roots below the actual rendered triangles, not the noise sampler.
        const int BladeSegments = 4;

        readonly List<Object> owned = new List<Object>();
        readonly List<Vector2> roots = new List<Vector2>(MaximumTufts);
        public IReadOnlyList<Vector2> RootPositions => roots;
#if UNITY_EDITOR
        readonly List<Vector2> routeEdgeRoots = new List<Vector2>();
        public IReadOnlyList<Vector2> RouteEdgeRootPositions => routeEdgeRoots;
#endif

        public bool IsNearGrass(Vector2 worldXZ, float clearance)
        {
            // Taller sanctuary leaves can sweep beyond their root. Keep shell discoveries
            // in clear sand rather than inside the visible swaying canopy.
            if (UsesSanctuaryCurrent) clearance += .38f;
            float squared = clearance * clearance;
            foreach (Vector2 root in roots) if ((worldXZ - root).sqrMagnitude < squared) return true;
            return false;
        }
        public int TuftCount { get; private set; }
        public int TriangleCount { get; private set; }
        public int ChunkCount { get; private set; }
        public int DistantTriangleCount { get; private set; }
        public int MeadowPatchCount { get; private set; }
        public bool UsesSanctuaryCurrent { get; private set; }
        public const float MaximumCurrentDeflection = 1.10f;
        public const float MaximumCurrentVerticalDrop = .40f;
        const float CurrentCullingSafetyMargin = .015f;
        Material currentMaterial;

        sealed class Chunk
        {
            public readonly List<Vector3> positions = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Vector2> motion = new List<Vector2>();
            public readonly List<Vector2> anchors = new List<Vector2>();
            public readonly List<Color> colors = new List<Color>();
            public readonly List<int> indices = new List<int>();
        }

        struct Patch
        {
            public Vector2 center;
            public float radius, aspect, rotation;
            public bool routeEdge;
        }

        /// <summary>Build once in world metres; destroy the returned object to release generated resources.</summary>
        public static SeagrassMeadow Build(Transform parent, Vector3 spawn, bool sanctuary = false)
        {
            var go = new GameObject("Seagrass Meadows");
            // Preserve a world-aligned, metre-scaled root even if the owner has a transform.
            go.transform.position = new Vector3(spawn.x, 0f, spawn.z);
            go.transform.SetParent(parent, true);
            var meadow = go.AddComponent<SeagrassMeadow>();
            meadow.Create(spawn, sanctuary);
            return meadow;
        }

        void Create(Vector3 spawn, bool sanctuary)
        {
            if (sanctuary)
            {
                CreateSanctuary(spawn);
                return;
            }
            Shader shader = Resources.Load<Shader>("Shaders/Seagrass");
            if (shader == null)
            {
                Debug.LogError("[Seagrass] Missing Resources/Shaders/Seagrass; meadow skipped.");
                return;
            }
            var material = new Material(shader) { name = "Seagrass olive and teal", enableInstancing = true };
            if (sanctuary)
            {
                material.SetColor("_RootColor", new Color(.14f, .17f, .10f));
                material.SetColor("_TipColor", new Color(.34f, .37f, .22f));
                material.SetFloat("_SwayStrength", .22f);
            }
            owned.Add(material);
            var rng = new System.Random(19092026);
            var origin = new Vector2(spawn.x, spawn.z);
            var patches = new List<Patch>(38);
            var pockets = ReefHabitat.Pockets;
            IReadOnlyList<Bounds> solidReefs=sanctuary?OceanCoralSanctuary.SolidBounds:null;
            if (!sanctuary && transform.parent != null)
            {
                var biome = transform.parent.GetComponentInChildren<OceanCampaignBiomeGeometry>();
                if (biome != null) solidReefs = biome.Solids;
            }

            // Both near and middle reefs get vegetation; the central sandy passage stays open.
            AddReefPatches(patches, pockets, origin, rng, 14, 0f, 14f);
            AddReefPatches(patches, pockets, origin, rng, 14, 14f, 32f);
            for (int band = 0; band < 2; band++)
            {
                int added = 0;
                for (int attempt = 0; attempt < 160 && added < 5; attempt++)
                {
                    float angle = Range(rng, 0f, Mathf.PI * 2f);
                    float radius = band == 0 ? Range(rng, 5f, 13f) : Range(rng, 15f, 29f);
                    Vector2 center = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    if (!CanAddPatch(center, patches, origin)) continue;
                    patches.Add(NewPatch(center, rng));
                    added++;
                }
            }

            if (sanctuary)
            {
                // Spend the same tuft budget across the whole expedition, not only the
                // accepted entrance composition. Sparse deposits leave real sandy routes.
                if (patches.Count > 14) patches.RemoveRange(14, patches.Count - 14);
                var zones = OceanCoralSanctuary.ExplorationZones;
                for (int ring = 0; ring < 6; ring++)
                    for (int zone = 1; zone < zones.Count; zone++)
                    {
                        for(int attempt=0;attempt<24;attempt++)
                        {
                            float angle=Range(rng,0f,Mathf.PI*2f),radius=Range(rng,4f,15f);
                            Vector2 center=origin+zones[zone].offset+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                            if(!CanAddPatch(center,patches,origin,104f))continue;
                            patches.Add(NewPatch(center,rng));break;
                        }
                    }
            }
            // Small fixed cells keep distant grass out of close-view draw submissions.
            int grid = sanctuary ? 8 : 2;
            var chunks = new Chunk[grid*grid];
            for(int i=0;i<chunks.Length;i++)chunks[i]=new Chunk();
            foreach (Patch patch in patches)
            {
                int target = sanctuary ? rng.Next(27,34) : rng.Next(32, 59);
                int accepted = 0;
                float sin = Mathf.Sin(patch.rotation), cos = Mathf.Cos(patch.rotation);
                var tuftPositions = new List<Vector2>(target);
                for (int attempt = 0; attempt < target * 10 && accepted < target && TuftCount < MaximumTufts; attempt++)
                {
                    float angle = Range(rng, 0f, Mathf.PI * 2f);
                    // Slightly denser centres and scalloped boundaries, with bare sand between patches.
                    float distance = Mathf.Pow(Range(rng, 0f, 1f), 0.7f) * patch.radius;
                    float x = Mathf.Cos(angle) * distance;
                    float z = Mathf.Sin(angle) * distance * patch.aspect;
                    var point = patch.center + new Vector2(x * cos - z * sin, x * sin + z * cos);
                    if (!ClearRoot(point, origin, pockets, sanctuary ? 106f : MeadowRadius)) continue;
                    if(solidReefs!=null&&!ClearOfSolidReefs(point,solidReefs))continue;
                    if (Mathf.PerlinNoise(point.x * 0.8f + 61f, point.y * 0.8f + 93f) < 0.30f) continue;
                    bool crowded = false;
                    foreach (Vector2 previous in tuftPositions)
                        if ((point - previous).sqrMagnitude < 0.011f) { crowded = true; break; }
                    if (crowded) continue;

                    float floor = UnderwaterWorld.SampleRenderedSeabedHeight(point.x, point.y);
                    float dx = UnderwaterWorld.SampleRenderedSeabedHeight(point.x + 0.2f, point.y) - floor;
                    float dz = UnderwaterWorld.SampleRenderedSeabedHeight(point.x, point.y + 0.2f) - floor;
                    if (dx * dx + dz * dz > 0.028f) continue; // Avoid sharp banks and exposed rock slopes.
                    int chunk = sanctuary
                        ? Mathf.Clamp(Mathf.FloorToInt((point.x-origin.x+112f)/28f),0,7)
                            + 8*Mathf.Clamp(Mathf.FloorToInt((point.y-origin.y+112f)/28f),0,7)
                        : (point.x >= origin.x ? 1 : 0) + (point.y >= origin.y ? 2 : 0);
                    AddTuft(chunks[chunk], point, origin, rng);
                    tuftPositions.Add(point);
                    roots.Add(point);
                    TuftCount++;
                    accepted++;
                }
            }

            for (int i = 0; i < chunks.Length; i++) CreateChunk(chunks[i], i, material);
            Debug.Log($"[Seagrass] patches={patches.Count}, tufts={TuftCount}, triangles={TriangleCount}/{MaximumTriangles}, chunks={ChunkCount}; one opaque material, GPU current, no per-frame CPU update");
        }

        static void AddReefPatches(List<Patch> patches, IReadOnlyList<ReefHabitat.CoralPocket> pockets,
            Vector2 origin, System.Random rng, int target, float inner, float outer)
        {
            if (pockets.Count == 0) return;
            int added = 0;
            for (int attempt = 0; attempt < 420 && added < target; attempt++)
            {
                ReefHabitat.CoralPocket pocket = pockets[rng.Next(pockets.Count)];
                var reef = new Vector2(pocket.center.x, pocket.center.z);
                float range = Vector2.Distance(reef, origin);
                if (range < inner || range > outer) continue;
                float angle = Range(rng, 0f, Mathf.PI * 2f);
                float distance = pocket.radius + Range(rng, 1.15f, 2.4f);
                Vector2 center = reef + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                if (!CanAddPatch(center, patches, origin)) continue;
                patches.Add(NewPatch(center, rng));
                added++;
            }
        }

        static Patch NewPatch(Vector2 center, System.Random rng)
        {
            return new Patch { center = center, radius = Range(rng, 1f, 2.1f),
                aspect = Range(rng, 0.55f, 0.9f), rotation = Range(rng, 0f, Mathf.PI * 2f) };
        }

        static bool CanAddPatch(Vector2 point, List<Patch> patches, Vector2 origin, float radius = 31f)
        {
            if ((point - origin).sqrMagnitude > radius * radius || InSwimPassage(point - origin)) return false;
            foreach (Patch patch in patches)
                if ((patch.center - point).sqrMagnitude < 2.7f * 2.7f) return false;
            return true;
        }

        static bool InSwimPassage(Vector2 offset)
        {
            return offset.sqrMagnitude < 2.5f * 2.5f ||
                (offset.y > -10f && offset.y < 17f && Mathf.Abs(offset.x) < 1.7f);
        }

        static bool ClearRoot(Vector2 point, Vector2 origin, IReadOnlyList<ReefHabitat.CoralPocket> pockets, float radius = MeadowRadius)
        {
            if ((point - origin).sqrMagnitude > radius * radius || InSwimPassage(point - origin)) return false;
            foreach (ReefHabitat.CoralPocket pocket in pockets)
            {
                float dx = point.x - pocket.center.x, dz = point.y - pocket.center.z;
                float clearance = pocket.radius + 0.28f;
                if (dx * dx + dz * dz < clearance * clearance) return false;
            }
            return true;
        }

        static bool ClearOfSolidReefs(Vector2 point,IReadOnlyList<Bounds> solids)
        {
            foreach(Bounds reef in solids)
                if(point.x>=reef.min.x-.18f&&point.x<=reef.max.x+.18f&&
                    point.y>=reef.min.z-.18f&&point.y<=reef.max.z+.18f)return false;
            return true;
        }

        static void AddTuft(Chunk chunk, Vector2 point, Vector2 origin, System.Random rng)
        {
            int blades = rng.Next(3, 5);
            float tuftHeight = Range(rng, 0.22f, 0.61f);
            float rotation = Range(rng, 0f, Mathf.PI * 2f);
            Color tint = Color.Lerp(new Color(0.85f, 0.83f, 0.77f), new Color(0.77f, 0.85f, 0.83f), Range(rng, 0f, 1f));
            tint *= Range(rng, 0.86f, 1.08f);
            tint.a = 1f;
            for (int blade = 0; blade < blades; blade++)
            {
                float angle = rotation + blade * 2.39996f + Range(rng, -0.25f, 0.25f);
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var across = new Vector3(-direction.z, 0f, direction.x);
                Vector2 rootXZ = point + new Vector2(direction.x, direction.z) * Range(rng, 0.01f, 0.045f);
                var root = new Vector3(rootXZ.x - origin.x,
                    UnderwaterWorld.SampleRenderedSeabedHeight(rootXZ.x, rootXZ.y) + RootLift, rootXZ.y - origin.y);
                float height = Mathf.Clamp(tuftHeight * Range(rng, 0.72f, 1.16f), 0.15f, 0.7f);
                float width = Range(rng, 0.013f, 0.026f);
                float lean = Range(rng, 0.15f, 0.44f) * height;
                float phase = Range(rng, 0f, Mathf.PI * 2f);
                int start = chunk.positions.Count;
                // Four curved segments terminate in one point, so there are no degenerate tip faces.
                for (int segment = 0; segment < BladeSegments; segment++)
                {
                    float t = segment / (float)BladeSegments;
                    Vector3 center = root + Vector3.up * (height * t) + direction * (lean * t * t);
                    float halfWidth = width * 0.5f * (0.72f + 0.5f * Mathf.Sin(t * Mathf.PI)) * (1f - t * 0.62f);
                    AddVertex(chunk, center - across * halfWidth, new Vector2(0f, t), height, phase, tint);
                    AddVertex(chunk, center + across * halfWidth, new Vector2(1f, t), height, phase, tint);
                    if (segment == 0) continue;
                    int previous = start + (segment - 1) * 2, current = start + segment * 2;
                    chunk.indices.Add(previous); chunk.indices.Add(current); chunk.indices.Add(previous + 1);
                    chunk.indices.Add(previous + 1); chunk.indices.Add(current); chunk.indices.Add(current + 1);
                }
                int tip = chunk.positions.Count, last = tip - 2;
                AddVertex(chunk, root + Vector3.up * height + direction * lean, new Vector2(0.5f, 1f), height, phase, tint);
                chunk.indices.Add(last); chunk.indices.Add(tip); chunk.indices.Add(last + 1);
            }
        }

        static void AddVertex(Chunk chunk, Vector3 point, Vector2 uv, float height, float phase, Color color)
        {
            chunk.positions.Add(point); chunk.uv.Add(uv);
            chunk.motion.Add(new Vector2(height, phase)); chunk.colors.Add(color);
        }

        void CreateChunk(Chunk chunk, int index, Material material)
        {
            if (chunk.indices.Count == 0) return;
            var mesh = new Mesh { name = "Seagrass quadrant " + index };
            // The entire meadow fits below 65k vertices, including the worst-case four blades/tuft.
            mesh.SetVertices(chunk.positions); mesh.SetUVs(0, chunk.uv); mesh.SetUVs(1, chunk.motion);
            mesh.SetColors(chunk.colors); mesh.SetTriangles(chunk.indices, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            Bounds bounds = mesh.bounds;
            bounds.Expand(new Vector3(0.6f, 0.15f, 0.6f)); // Include maximum GPU tip displacement.
            mesh.bounds = bounds;
            // Keep Editor-only CPU data for independent contact inspection of generated meshes.
            mesh.UploadMeshData(!Application.isEditor);
            owned.Add(mesh);
            var go = new GameObject(mesh.name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            TriangleCount += chunk.indices.Count / 3;
            ChunkCount++;
        }

        // Sanctuary keeps the original tuft budget, but spends it on visibly different
        // low mats, medium ribbons and tall sheltered stands, distributed by habitat.
        void CreateSanctuary(Vector3 spawn)
        {
            Shader shader = Resources.Load<Shader>("Shaders/SanctuarySeagrass");
            if (shader == null)
            {
                Debug.LogError("[Seagrass] Missing SanctuarySeagrass; meadow skipped.");
                return;
            }
            UsesSanctuaryCurrent = true;
            currentMaterial = new Material(shader) { name = "Sanctuary olive kelp current", enableInstancing = true };
            owned.Add(currentMaterial);
            var rng = new System.Random(16102026);
            var origin = new Vector2(spawn.x, spawn.z);
            var near = new Chunk[64];
            var distant = new Chunk[64];
            for (int i = 0; i < near.Length; i++) { near[i] = new Chunk(); distant[i] = new Chunk(); }
            var patches = new List<Patch>(56);
            var zones = OceanCoralSanctuary.ExplorationZones;
            var solids = OceanCoralSanctuary.SolidBounds;
            // Reserve each region before filling leftovers, so a more jagged reef cannot
            // consume every accepted root in the first few easily planted sandy areas.
            for (int zone = 0; zone < zones.Count; zone++)
            {
                Vector2 home = origin + zones[zone].offset;
                var localPatches = SanctuaryPatches(home, origin, solids, patches, rng, zone);
                int quota = zone == 0 ? 240 : 216;
                int target = Mathf.Min(MaximumTufts, TuftCount + quota);
                int previous = TuftCount;
                for (int pass = 0; pass < 3 && TuftCount < target; pass++)
                    foreach (Patch patch in localPatches)
                    {
                        if (TuftCount >= target) break;
                        PlantSanctuaryPatch(patch, origin, near, distant, rng,
                            Mathf.Min(target, TuftCount + (pass == 0 ? 30 : 18)));
                    }
                Debug.Log($"[SeagrassHabitat] zone={zone}, tufts={TuftCount-previous}, patches={localPatches.Count}");
            }
            for (int pass = 0; pass < 3 && TuftCount < MaximumTufts; pass++)
                foreach (Patch patch in patches)
                {
                    if (TuftCount >= MaximumTufts) break;
                    PlantSanctuaryPatch(patch, origin, near, distant, rng,
                        Mathf.Min(MaximumTufts, TuftCount + 12));
                }
            MeadowPatchCount = patches.Count;
            for (int i = 0; i < near.Length; i++) CreateSanctuaryChunk(near[i], distant[i], i);
            Debug.Log($"[Seagrass] sanctuary patches={MeadowPatchCount}, tufts={TuftCount}/{MaximumTufts}, " +
                $"triangles={TriangleCount}/{MaximumTriangles}, distantTriangles={DistantTriangleCount}, chunks={ChunkCount}; " +
                "one opaque material; one selected draw/chunk; native two-level LOD; anchored segmented GPU current");
        }

        static List<Patch> SanctuaryPatches(Vector2 home, Vector2 origin, IReadOnlyList<Bounds> solids,
            List<Patch> all, System.Random rng, int zone)
        {
            var result = new List<Patch>(9);
            if (zone == 0)
            {
                // Retain near-spawn meadow discoveries used by the campaign instead of
                // moving all suitable grass roots into the newly expanded outer regions.
                var entrance = new List<Patch>(5);
                AddReefPatches(entrance, ReefHabitat.Pockets, origin, rng, 5, 0f, 14f);
                foreach (Patch patch in entrance) { result.Add(patch); all.Add(patch); }
            }
            else
            {
                // Spend part of each EXISTING outer-region quota along the sandy
                // journey, before the first dense rock-footing patches consume it.
                // The centre lane stays bare and the stands alternate, taper and
                // break up with the same root noise; this is not a roadside fence.
                AddSanctuaryRoutePatches(result,all,origin,solids,rng,zone);
            }
            // Long irregular strips skirt actual rock footings; AABB exclusion is
            // conservative so leaves do not start inside collision-supported reefs.
            for (int attempt = 0; attempt < 200 && result.Count < 5; attempt++)
            {
                Bounds reef = solids[rng.Next(solids.Count)];
                var centre = new Vector2(reef.center.x, reef.center.z);
                if (Vector2.Distance(centre, home) > (zone == 0 ? 29f : 25f)) continue;
                int side = rng.Next(4);
                float along = Range(rng, -.68f, .68f);
                Vector2 p = side < 2
                    ? new Vector2(side == 0 ? reef.min.x - 1.0f : reef.max.x + 1.0f,
                        reef.center.z + along * reef.extents.z)
                    : new Vector2(reef.center.x + along * reef.extents.x,
                        side == 2 ? reef.min.z - 1.0f : reef.max.z + 1.0f);
                if (!CanAddPatch(p, all, origin, 106f)) continue;
                var patch = new Patch { center = p, radius = Range(rng, 2.2f, 3.6f),
                    aspect = Range(rng, .27f, .48f), rotation = side < 2 ? Mathf.PI * .5f : 0f };
                result.Add(patch); all.Add(patch);
            }
            // Scalloped, separated meadow pockets on the basin rims, never a full
            // grass carpet or a ring aligned to the player's camera.
            for (int attempt = 0; attempt < 240 && result.Count < 9; attempt++)
            {
                float angle = Range(rng, 0f, Mathf.PI * 2f);
                Vector2 p = home + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(rng, 5f, 20f);
                if (!CanAddPatch(p, all, origin, 106f) || !ClearOfSolidReefs(p, solids)) continue;
                var patch = new Patch { center = p, radius = Range(rng, 1.8f, 3.3f),
                    aspect = Range(rng, .48f, .8f), rotation = Range(rng, 0f, Mathf.PI * 2f) };
                result.Add(patch); all.Add(patch);
            }
            return result;
        }

        static void AddSanctuaryRoutePatches(List<Patch> result,List<Patch> all,Vector2 origin,
            IReadOnlyList<Bounds> solids,System.Random rng,int zone)
        {
            // Two neighbouring route segments per district, in the same order as
            // the authored expedition. No new terrain, collider or moving anchor.
            int[] starts={0,10,12,14,16,18,21};
            var route=OceanCoralSanctuary.ExplorationRouteOffsets;
            for(int part=0;part<2;part++)
            {
                int segment=starts[zone]+part;
                Vector2 delta=route[segment+1]-route[segment];
                Vector2 direction=delta.normalized;
                Vector2 side=new Vector2(-direction.y,direction.x);
                Vector2 along=Vector2.Lerp(route[segment],route[segment+1],
                    Range(rng,.27f,.63f));
                int firstSide=(zone+part)%2==0?1:-1;
                for(int attempt=0;attempt<6;attempt++)
                {
                    int sign=attempt%2==0?firstSide:-firstSide;
                    float lateral=3.6f+(attempt/2)*1.1f+Range(rng,-.30f,.45f);
                    Vector2 p=origin+along+side*(sign*lateral);
                    if(!CanAddPatch(p,all,origin,106f)||!ClearOfSolidReefs(p,solids))continue;
                    var patch=new Patch { center=p,radius=Range(rng,4.8f,7.1f),
                        aspect=Range(rng,.15f,.28f),rotation=Mathf.Atan2(direction.y,direction.x),routeEdge=true };
                    result.Add(patch);all.Add(patch);break;
                }
            }
        }

        void PlantSanctuaryPatch(Patch patch, Vector2 origin, Chunk[] near, Chunk[] distant,
            System.Random rng, int target)
        {
            float sin = Mathf.Sin(patch.rotation), cos = Mathf.Cos(patch.rotation);
            var pockets = ReefHabitat.Pockets;
            for (int attempt = 0; attempt < 720 && TuftCount < target; attempt++)
            {
                float angle = Range(rng, 0f, Mathf.PI * 2f);
                float distance = Mathf.Pow(Range(rng, 0f, 1f), .68f) * patch.radius;
                float x = Mathf.Cos(angle) * distance, z = Mathf.Sin(angle) * distance * patch.aspect;
                Vector2 point = patch.center + new Vector2(x * cos - z * sin, x * sin + z * cos);
                if (!ClearRoot(point, origin, pockets, 107f) || !ClearOfSolidReefs(point, OceanCoralSanctuary.SolidBounds)) continue;
                if (OnSanctuaryRoute(point - origin)) continue;
                // Leave room for the resting arc AND GPU current at route shoulders,
                // so readable near-water vegetation cannot brush through the diver.
                if(patch.routeEdge&&(NearRoute(point-origin,OceanCoralSanctuary.ExplorationRouteOffsets,2.2f)||
                    NearRoute(point-origin,OceanCoralSanctuary.ExplorationShortcutOffsets,2.2f)))continue;
                float noise = Mathf.PerlinNoise(point.x * .72f + 61f, point.y * .72f + 93f);
                if (noise < .32f) continue;
                bool crowded = false;
                foreach (Vector2 root in roots)
                    if ((root - point).sqrMagnitude < .065f) { crowded = true; break; }
                if (crowded) continue;
                float floor = UnderwaterWorld.SampleRenderedSeabedHeight(point.x, point.y);
                float dx = UnderwaterWorld.SampleRenderedSeabedHeight(point.x + .2f, point.y) - floor;
                float dz = UnderwaterWorld.SampleRenderedSeabedHeight(point.x, point.y + .2f) - floor;
                if (dx * dx + dz * dz > .028f) continue;
                int cell = Mathf.Clamp(Mathf.FloorToInt((point.x - origin.x + 112f) / 28f), 0, 7) +
                    8 * Mathf.Clamp(Mathf.FloorToInt((point.y - origin.y + 112f) / 28f), 0, 7);
                // Height is spatially correlated: tall sheltered pockets alternate
                // with low sea-grass. Nearby leaves share a current but not one phase.
                float stand = Mathf.PerlinNoise(patch.center.x * .11f + 11f, patch.center.y * .11f + 37f);
                float height = Mathf.Lerp(.38f, 1.22f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.18f, .78f, stand))) * Range(rng, .82f, 1.10f);
                float rotation = Range(rng, 0f, Mathf.PI * 2f);
                Color tint = Color.Lerp(new Color(.74f, .78f, .61f), new Color(.57f, .73f, .69f), noise);
                tint *= Range(rng, .88f, 1.12f); tint.a = 1f;
                for (int blade = 0; blade < 3; blade++)
                {
                    float leafAngle = rotation + blade * 2.39996f;
                    float leafHeight = Mathf.Clamp(height * Range(rng, .78f, 1.10f), .27f, 1.35f);
                    // Low broad leaves and taller narrow ribbons read as different
                    // growth forms while retaining three blades and the same LOD mesh.
                    float width = Range(rng, .036f, .068f) * (leafHeight > 1f ? .86f : leafHeight < .55f ? 1.18f : 1f);
                    float phase = Range(rng, -.7f, .7f);
                    Vector2 root = point + new Vector2(Mathf.Cos(leafAngle), Mathf.Sin(leafAngle)) * .025f;
                    AddSanctuaryBlade(near[cell], root, origin, leafHeight, width, leafAngle, phase, tint, 5);
                    if (blade < 2) AddSanctuaryBlade(distant[cell], root, origin, leafHeight, width, leafAngle, phase, tint, 2);
                }
                roots.Add(point); TuftCount++;
#if UNITY_EDITOR
                if(patch.routeEdge)routeEdgeRoots.Add(point);
#endif
            }
        }

        static bool OnSanctuaryRoute(Vector2 point)
        {
            return NearRoute(point, OceanCoralSanctuary.ExplorationRouteOffsets, 1.05f) ||
                NearRoute(point, OceanCoralSanctuary.ExplorationShortcutOffsets, 1.05f);
        }

        static bool NearRoute(Vector2 point, IReadOnlyList<Vector2> route, float clearance)
        {
            for (int i = 0; i + 1 < route.Count; i++)
            {
                Vector2 a = route[i], segment = route[i + 1] - a;
                float t = Mathf.Clamp01(Vector2.Dot(point - a, segment) / Mathf.Max(.0001f, segment.sqrMagnitude));
                if ((point - a - segment * t).sqrMagnitude < clearance * clearance) return true;
            }
            return false;
        }

        static void AddSanctuaryBlade(Chunk chunk, Vector2 point, Vector2 origin, float height,
            float width, float angle, float phase, Color tint, int segments)
        {
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 across = new Vector3(-direction.z, 0f, direction.x);
            Vector3 root = new Vector3(point.x - origin.x,
                UnderwaterWorld.SampleRenderedSeabedHeight(point.x, point.y) + RootLift, point.y - origin.y);
            // Author a soft, asymmetric resting arc. GPU current adds movement to it;
            // it no longer has to turn a straight upright card into a flexible leaf.
            // Both LODs sample this same curve and keep identical root/tip positions.
            float individuality=Mathf.Sin(point.x*1.71f+point.y*.83f+phase*2.4f);
            float lean = height * (.29f+.13f*individuality);
            float curl = height * (.08f+.035f*Mathf.Cos(point.x*.67f-point.y*1.13f+phase));
            int first = chunk.positions.Count;
            for (int section = 0; section < segments; section++)
            {
                float t = section / (float)segments;
                Vector3 centre = root + Vector3.up * (height * t) + direction * (lean * t * t) +
                    across*(curl*Mathf.Sin(t*Mathf.PI)*t);
                float halfWidth = width * .5f * (1f + .20f * Mathf.Sin(t * Mathf.PI)) * (1f - t * .70f);
                // A shallow twist avoids every blade being a rigid, flat ribbon. The
                // base stays horizontal, so both root edges still meet the true floor.
                Vector3 edge=Quaternion.AngleAxis(individuality*24f*t,direction)*across;
                Vector3 left = centre - edge * halfWidth, right = centre + edge * halfWidth;
                if (section == 0)
                {
                    // Both edge roots touch the actual rendered floor even on a basin slope.
                    left.y = UnderwaterWorld.SampleRenderedSeabedHeight(left.x + origin.x, left.z + origin.y) + RootLift;
                    right.y = UnderwaterWorld.SampleRenderedSeabedHeight(right.x + origin.x, right.z + origin.y) + RootLift;
                }
                AddVertex(chunk, left, new Vector2(0, t), height, phase, tint); chunk.anchors.Add(point);
                AddVertex(chunk, right, new Vector2(1, t), height, phase, tint); chunk.anchors.Add(point);
                if (section == 0) continue;
                int previous = first + (section - 1) * 2, current = first + section * 2;
                chunk.indices.Add(previous); chunk.indices.Add(current); chunk.indices.Add(previous + 1);
                chunk.indices.Add(previous + 1); chunk.indices.Add(current); chunk.indices.Add(current + 1);
            }
            int tip = chunk.positions.Count, last = tip - 2;
            AddVertex(chunk, root + Vector3.up * height + direction * lean, new Vector2(.5f, 1f), height, phase, tint);
            chunk.anchors.Add(point);
            chunk.indices.Add(last); chunk.indices.Add(tip); chunk.indices.Add(last + 1);
        }

        void CreateSanctuaryChunk(Chunk near, Chunk distant, int index)
        {
            if (near.indices.Count == 0) return;
            var groupObject = new GameObject("Seagrass current cell " + index);
            groupObject.transform.SetParent(transform, false);
            var detailed = SanctuaryRenderer(near, index, groupObject.transform, false);
            var economical = SanctuaryRenderer(distant, index, groupObject.transform, true);
            var group = groupObject.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(new[] { new LOD(.25f, new[] { detailed }), new LOD(.05f, new[] { economical }) });
            group.RecalculateBounds();
            // Sparse cells should not switch geometry close to a viewer merely
            // because their occupied footprint is small. Never reduce real bounds.
            group.size = Mathf.Max(20f, group.size);
            ChunkCount++;
        }

        MeshRenderer SanctuaryRenderer(Chunk geometry, int index, Transform parent, bool distant)
        {
            var mesh = new Mesh { name = distant ? "Seagrass distant " + index : "Seagrass quadrant " + index };
            mesh.SetVertices(geometry.positions); mesh.SetUVs(0, geometry.uv); mesh.SetUVs(1, geometry.motion);
            mesh.SetUVs(2, geometry.anchors); mesh.SetColors(geometry.colors); mesh.SetTriangles(geometry.indices, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            Bounds bounds = mesh.bounds;
            // Bounds stores centre/extents, whose float reconstruction can round an
            // exactly padded edge inward by an ULP. Keep a real 15 mm safety apron
            // beyond the full conservative motion envelope, rather than relaxing
            // containment checks or relying on a vertex lying exactly on the edge.
            bounds.Expand(new Vector3((MaximumCurrentDeflection + CurrentCullingSafetyMargin) * 2f,
                (MaximumCurrentVerticalDrop + CurrentCullingSafetyMargin) * 2f,
                (MaximumCurrentDeflection + CurrentCullingSafetyMargin) * 2f));
            mesh.bounds = bounds;
            mesh.UploadMeshData(!Application.isEditor); owned.Add(mesh);
            var go = new GameObject(mesh.name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = currentMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (distant) DistantTriangleCount += geometry.indices.Count / 3;
            else TriangleCount += geometry.indices.Count / 3;
            return renderer;
        }

#if UNITY_EDITOR
        /// <summary>Local Editor capture hook. Null restores real shader time; no Player entry point.</summary>
        public void SetValidationTime(float? seconds)
        {
            if (currentMaterial == null) return;
            currentMaterial.SetFloat("_ValidationTime", seconds.GetValueOrDefault());
            currentMaterial.SetFloat("_ValidationTimeEnabled", seconds.HasValue ? 1f : 0f);
        }
#endif

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        void OnDestroy()
        {
            foreach (Object resource in owned) if (resource != null) Destroy(resource);
            owned.Clear();
        }
    }
}

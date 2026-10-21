using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>Scanned reef forms, composed at human eye height with clear swim channels.</summary>
    public static class ReefHabitat
    {
        private struct AssetPair
        {
            public GameObject near;
            public GameObject far;
            public GameObject distant;
            public ReefSupportGeometry nearGeometry, farGeometry, distantGeometry;
        }

        [Serializable]
        public struct GroundingSummary
        {
            public int rocks, corals, rockSupportedCorals, groundSupportedCorals, lodsChecked, contactSamples, unanchoredLods;
            public float minimumContactGap, maximumContactGap, maximumRockBasalGap, maximumCoralAnchorGap, maximumLodAnchorSpread;
        }
        [Serializable]
        public struct GroundingSample
        {
            public string asset;
            public bool coral, rockSupported;
            public Vector3 position;
            public float verticalAdjustment, contactGap, minimumContactGap, maximumContactGap, lodAnchorSpread;
            public int contactSamples, unanchoredLods;
        }
        private static GroundingSummary grounding;
        private static readonly List<GroundingSample> groundingSamples = new List<GroundingSample>();
        public static GroundingSummary LastGrounding => grounding;
        public static IReadOnlyList<GroundingSample> GroundingSamples => groundingSamples;

        // These heavy geometry references live only for Build; diagnostics retain scalar results only.
        private sealed class ModelSurface
        {
            public Transform transform;
            public ReefSupportGeometry geometry;
        }
        private sealed class PlacedAsset
        {
            public Bounds bounds;
            public readonly List<ModelSurface> models = new List<ModelSurface>();

            public bool TrySurface(float worldX, float worldZ, out float height)
            {
                height = float.PositiveInfinity;
                foreach (ModelSurface model in models)
                {
                    // These scanned assets share an upright root; placement adds yaw and uniform scale only.
                    Vector3 local = model.transform.InverseTransformPoint(new Vector3(worldX, 0f, worldZ));
                    float localHeight;
                    if (!model.geometry.TryTop(local.x, local.z, out localHeight)) return false;
                    float worldHeight = model.transform.TransformPoint(new Vector3(local.x, localHeight, local.z)).y;
                    height = Mathf.Min(height, worldHeight);
                }
                // A support must exist in every rock LOD. Use the lower of their actual surfaces,
                // never a box or an interpolated surface spanning a hole in the scan.
                return models.Count > 0;
            }
        }

        public struct CoralPocket
        {
            public Vector3 center, shelter;
            public float radius;
        }
        private static readonly List<CoralPocket> pockets = new List<CoralPocket>();
        private static readonly List<Vector3> vents = new List<Vector3>();
        public static IReadOnlyList<CoralPocket> Pockets => pockets;
        public static IReadOnlyList<Vector3> BubbleSources => vents;

        /// <summary>Publish a prepared environment's habitat once, never during swimming.</summary>
        public static void SetActiveData(IReadOnlyList<CoralPocket> activePockets, IReadOnlyList<Vector3> activeVents)
        {
            if (ReferenceEquals(activePockets, pockets) || ReferenceEquals(activeVents, vents))
                throw new ArgumentException("Environment habitat must be an owned snapshot.");
            pockets.Clear(); vents.Clear();
            if (activePockets != null) for (int i = 0; i < activePockets.Count; i++) pockets.Add(activePockets[i]);
            if (activeVents != null) for (int i = 0; i < activeVents.Count; i++) vents.Add(activeVents[i]);
        }

        private static readonly Color[] CoralTints = {
            new Color(0.84f, 0.62f, 0.46f), new Color(0.65f, 0.71f, 0.51f),
            new Color(0.66f, 0.51f, 0.62f), new Color(0.69f, 0.79f, 0.74f),
            new Color(0.81f, 0.72f, 0.57f)
        };

        /// <summary>Immediate build for existing Editor checks and other synchronous callers.</summary>
        public static void Build(Transform parent, Func<float, float, float> floor, Vector3 spawn)
        {
            IEnumerator build = BuildRoutine(parent, floor, spawn, false, null);
            try { while (build.MoveNext()) { } }
            finally { (build as IDisposable)?.Dispose(); }
        }

        /// <summary>
        /// Await this entire iterator before creating dependents that consume Pockets/BubbleSources.
        /// Progress describes completed loading, geometry preparation and model placement work.
        /// </summary>
        public static IEnumerator BuildStaged(Transform parent, Func<float, float, float> floor,
            Vector3 spawn, Action<float> progress = null)
        {
            return BuildRoutine(parent, floor, spawn, true, progress);
        }

        private static IEnumerator BuildRoutine(Transform parent, Func<float, float, float> floor,
            Vector3 spawn, bool staged, Action<float> progress)
        {
            pockets.Clear();
            vents.Clear();
            groundingSamples.Clear();
            grounding = new GroundingSummary { maximumRockBasalGap = float.NegativeInfinity,
                maximumCoralAnchorGap = float.NegativeInfinity, minimumContactGap = float.PositiveInfinity,
                maximumContactGap = float.NegativeInfinity };
            string[] names = { "coral_cuneata", "coral_spicifera", "coral_brain", "coral_palmata",
                "rock_coast_05", "rock_coast_02" };
            // Hold every loaded prefab until the iterator finishes. In particular, no asset may
            // be reclaimed between its async request completing and the later geometry pass.
            var preloaded = new Dictionary<string, GameObject>(16);
            progress?.Invoke(0f);
            if (staged)
            {
                int completed = 0;
                foreach (string stem in names)
                {
                    int lodCount = stem.StartsWith("rock_", StringComparison.Ordinal) ? 2 : 3;
                    for (int lod = 0; lod < lodCount; lod++)
                    {
                        string path = "RealisticReef/" + stem + "_lod" + lod;
                        ResourceRequest request = Resources.LoadAsync<GameObject>(path);
                        while (!request.isDone)
                        {
                            progress?.Invoke(0.25f * (completed + request.progress) / 16f);
                            yield return null;
                        }
                        preloaded.Add(path, request.asset as GameObject);
                        completed++;
                        progress?.Invoke(0.25f * completed / 16f);
                        yield return null;
                    }
                }
            }

            if (staged)
            {
                var textures = new HashSet<Texture2D>();
                foreach (GameObject model in preloaded.Values)
                    if (model != null) foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                        foreach (Material material in renderer.sharedMaterials)
                            if (material != null) foreach (string property in material.GetTexturePropertyNames())
                                if (material.GetTexture(property) is Texture2D texture) textures.Add(texture);
                int astc = 0, source4k = 0, mipmapped = 0, trilinear16 = 0; long bytes = 0;
                foreach (Texture2D texture in textures)
                {
                    if (texture.format == TextureFormat.ASTC_4x4) astc++;
                    if (texture.width == 4096 && texture.height == 4096) source4k++;
                    if (texture.mipmapCount > 1) mipmapped++;
                    if (texture.filterMode == FilterMode.Trilinear && texture.anisoLevel == 16) trilinear16++;
                    bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
                }
                Debug.Log($"[ReefTextures] count={textures.Count} astc4x4={astc} source4k={source4k} mipmapped={mipmapped} trilinear16={trilinear16} deviceSupportsASTC={SystemInfo.SupportsTextureFormat(TextureFormat.ASTC_4x4)} runtime_bytes={bytes}; full source mip dimensions retained");
            }

            var corals = new List<AssetPair>(4);
            var rocks = new List<AssetPair>(2);
            int geometryCount = 0;
            foreach (string stem in names)
            {
                // Preserve the former PairAssets order exactly; one scan's contact/grid work
                // remains atomic, but no frame must prepare all sixteen scan geometries.
                bool surface = stem.StartsWith("rock_", StringComparison.Ordinal);
                var pair = new AssetPair {
                    near = LoadedAsset(stem, 0, preloaded), far = LoadedAsset(stem, 1, preloaded),
                    distant = LoadedAsset(stem, 2, preloaded)
                };
                if (pair.near == null || pair.far == null)
                    throw new InvalidOperationException("Missing scanned reef LOD pair: " + stem);
                pair.nearGeometry = ReefSupportGeometry.Read(pair.near, surface);
                geometryCount++;
                if (staged) { progress?.Invoke(0.25f + 0.20f * geometryCount / 16f); yield return null; }
                pair.farGeometry = ReefSupportGeometry.Read(pair.far, surface);
                geometryCount++;
                if (staged) { progress?.Invoke(0.25f + 0.20f * geometryCount / 16f); yield return null; }
                if (pair.distant != null)
                {
                    pair.distantGeometry = ReefSupportGeometry.Read(pair.distant, surface);
                    geometryCount++;
                    if (staged) { progress?.Invoke(0.25f + 0.20f * geometryCount / 16f); yield return null; }
                }
                (surface ? rocks : corals).Add(pair);
            }
            if (corals.Count == 0 || rocks.Count == 0)
            {
                Debug.LogError($"[ReefHabitat] Missing scanned assets: coral={corals.Count}, rock={rocks.Count}. Build is not visually accepted.");
                yield break;
            }

            var root = new GameObject("Scanned Reef Habitat").transform;
            root.SetParent(parent, false);
            var proximity = root.gameObject.AddComponent<ReefProximityLod>();
            var rng = new System.Random(20260919);
            var materialCache = new Dictionary<string, Material>();
            int coralCount = 0, rockCount = 0;

            // Irregular reef islands flank a sandy passage; no enclosing ring or wall.
            Vector3[] nearPatches = {
                new Vector3(-3.2f,0,1.3f), new Vector3(3.6f,0,3.1f),
                new Vector3(-5.5f,0,7.4f), new Vector3(6.6f,0,10.5f),
                new Vector3(-4.4f,0,-5.4f), new Vector3(4.5f,0,-6.5f),
                new Vector3(-10f,0,-0.7f), new Vector3(10f,0,2f)
            };
            foreach (Vector3 offset in nearPatches)
            {
                Vector3 center = spawn + offset;
                float rockHeight = Range(rng, 1.35f, 1.85f);
                PlacedAsset rock = Place(rocks[0], root, center, floor, rockHeight, Range(rng, 4.6f, 5.8f),
                    Range(rng, 0f, 360f), -0.12f, false, 0, materialCache, true);
                rockCount++;
                if (staged) { progress?.Invoke(PlacementProgress(coralCount + rockCount)); yield return null; }
                for (int c = 0; c < 12; c++)
                {
                    float angle = Range(rng, 0f, Mathf.PI * 2f);
                    float radius = c == 0 ? 0f : Range(rng, 0.55f, 2.1f);
                    Vector3 p = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    PlacedAsset coral = Place(corals[c % corals.Count], root, p, floor, Range(rng, 0.85f, 1.45f),
                        Range(rng, 1.2f, 2.15f), Range(rng, 0, 360), -0.04f,
                        true, rng.Next(CoralTints.Length), materialCache, false, false, rock);
                    AddPocket(coral.bounds.center, coral.bounds.min.y, coral.bounds.size.y,
                        c % corals.Count != 2, c == 1 || c == 5);
                    coralCount++;
                    if (staged && (c + 1) % 3 == 0)
                    { progress?.Invoke(PlacementProgress(coralCount + rockCount)); yield return null; }
                }
            }

            // Mid-distance reefs provide parallax and destinations while the player swims.
            for (int i = 0; i < 22; i++)
            {
                float a = i * 2.39996f + 0.45f;
                float radius = Range(rng, 15f, 39f);
                Vector3 p = spawn + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                float h = Range(rng, 1.0f, 2.5f);
                PlacedAsset rock = Place(rocks[0], root, p, floor, h, Range(rng, 5f, 8f),
                    Range(rng, 0, 360), -0.2f, false, 0, materialCache, true);
                rockCount++;
                if (staged) { progress?.Invoke(PlacementProgress(coralCount + rockCount)); yield return null; }
                for (int c = 0; c < 7; c++)
                {
                    Vector3 cp = p + new Vector3(Range(rng, -1.5f, 1.5f), 0, Range(rng, -1.2f, 1.2f));
                    PlacedAsset coral = Place(corals[(i+c) % corals.Count], root, cp, floor, Range(rng, 0.65f, 1.3f),
                        Range(rng, 1.2f, 2.2f), Range(rng, 0, 360), -0.04f,
                        true, (i+c) % CoralTints.Length, materialCache, false, false, rock);
                    AddPocket(coral.bounds.center, coral.bounds.min.y, coral.bounds.size.y,
                        (i+c) % corals.Count != 2, i % 5 == 0 && c == 1);
                    coralCount++;
                    if (staged && ((c + 1) % 3 == 0 || c == 6))
                    { progress?.Invoke(PlacementProgress(coralCount + rockCount)); yield return null; }
                }
            }

            // Broad remote reef ridges, below the main sightline, disappear gradually in water fog.
            for (int i = 0; i < 22; i++)
            {
                float a = i * 2.39996f;
                float r = Range(rng, 44f, 91f);
                Vector3 p = spawn + new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)) * r;
                Place(rocks[i % rocks.Count], root, p, floor, Range(rng, 2.1f, 4.5f),
                    Range(rng, 6f, 12f), Range(rng, 0, 360), -0.3f, false, 0, materialCache, false, true);
                rockCount++;
                if (staged) { progress?.Invoke(PlacementProgress(coralCount + rockCount)); yield return null; }
            }
            Debug.Log($"[ReefHabitat] scanned coral={coralCount}, reef rocks={rockCount}; shared materials={materialCache.Count}; near/mid/far ranges=3-12/15-39/44-91m");
            Debug.Log($"[ReefGrounding] rocks={grounding.rocks}, corals={grounding.corals}, onRock={grounding.rockSupportedCorals}, " +
                $"onGround={grounding.groundSupportedCorals}, lods={grounding.lodsChecked}, " +
                $"maxRockBasalGap={grounding.maximumRockBasalGap:F4}m, maxCoralAnchorGap={grounding.maximumCoralAnchorGap:F4}m, " +
                $"sampleGap=[{grounding.minimumContactGap:F4},{grounding.maximumContactGap:F4}]m, " +
                $"samples={grounding.contactSamples}, unanchoredLods={grounding.unanchoredLods}, " +
                $"maxLodAnchorSpread={grounding.maximumLodAnchorSpread:F4}m; build-time triangle support, no added mesh colliders");
            Debug.Log($"[ReefLOD] groups={proximity.RegisteredCount} remoteUpgraded={proximity.UpgradedRemoteCount} enter=8m exit=9m distance=nearest_bounds_point; remote fallback=LOD1, other fallback=native; meshes shared");
            GC.KeepAlive(preloaded);
            progress?.Invoke(1f);
        }

        private static float PlacementProgress(int placed) => 0.45f + 0.54f * placed / 302f;

        private static GameObject LoadedAsset(string stem, int lod, Dictionary<string, GameObject> preloaded)
        {
            string path = "RealisticReef/" + stem + "_lod" + lod;
            return preloaded.TryGetValue(path, out GameObject loaded) ? loaded : Resources.Load<GameObject>(path);
        }

        private static void AddPocket(Vector3 p, float bottom, float height, bool branching, bool vent)
        {
            // Only branching corals provide crevices. Brain coral is solid: fish stay above it.
            var center = new Vector3(p.x, bottom + height * 0.8f, p.z);
            pockets.Add(new CoralPocket { center = center, radius = Mathf.Clamp(height * 0.65f, 0.45f, 1.1f),
                shelter = new Vector3(p.x + height * 0.18f, bottom + height * (branching ? 0.62f : 1.12f), p.z) });
            if (vent && branching) vents.Add(new Vector3(p.x - height * 0.2f, bottom + height * 0.58f, p.z + height * 0.12f));
        }

        private static PlacedAsset Place(AssetPair asset, Transform parent, Vector3 p, Func<float,float,float> floor,
            float height, float width, float yaw, float lift, bool coral, int palette,
            Dictionary<string,Material> cache, bool collision, bool distant = false, PlacedAsset supportRock = null)
        {
            var group = new GameObject((coral ? "Coral " : "Reef ") + asset.near.name);
            group.transform.SetParent(parent, false);
            group.transform.position = new Vector3(p.x, floor(p.x,p.z), p.z);
            group.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var primary = UnityEngine.Object.Instantiate(asset.near, group.transform, false);
            // Shared source coordinates and one LOD0 fit prevent independent LOD normalization jumps.
            Fit(primary, asset.nearGeometry.bounds, height, width);
            var placed = new PlacedAsset();
            placed.models.Add(new ModelSurface { transform = primary.transform,
                geometry = asset.nearGeometry });
            Renderer[] nearRenderers = primary.GetComponentsInChildren<Renderer>();
            PrepareMaterials(nearRenderers, coral, palette, cache);
            LODGroup lod = null;
            if (asset.far != null)
            {
                var low = UnityEngine.Object.Instantiate(asset.far, group.transform, false);
                Fit(low, asset.nearGeometry.bounds, height, width);
                placed.models.Add(new ModelSurface { transform = low.transform, geometry = asset.farGeometry });
                Renderer[] farRenderers = low.GetComponentsInChildren<Renderer>();
                PrepareMaterials(farRenderers, coral, palette, cache);
                lod = group.AddComponent<LODGroup>();
                // Keep economical LOD1 contact shadows when the viewer leaves the 8–9m detail zone.
                foreach (var r in farRenderers) r.shadowCastingMode = ShadowCastingMode.On;
                if (asset.distant != null)
                {
                    var remote = UnityEngine.Object.Instantiate(asset.distant, group.transform, false);
                    Fit(remote, asset.nearGeometry.bounds, height, width);
                    placed.models.Add(new ModelSurface { transform = remote.transform, geometry = asset.distantGeometry });
                    Renderer[] remoteRenderers = remote.GetComponentsInChildren<Renderer>();
                    PrepareMaterials(remoteRenderers, coral, palette, cache);
                    foreach (var r in remoteRenderers) r.shadowCastingMode = ShadowCastingMode.Off;
                    lod.SetLODs(new[] { new LOD(0.13f, nearRenderers), new LOD(0.035f, farRenderers), new LOD(0.004f, remoteRenderers) });
                }
                else lod.SetLODs(new[] { new LOD(0.09f, nearRenderers), new LOD(0.004f, farRenderers) });
                lod.fadeMode = LODFadeMode.None;
                lod.RecalculateBounds();
            }
            // Former distant ridges also cast when approached; URP limits shadow rendering to 24m.
            Ground(group.transform, placed, asset.near.name, floor, supportRock, coral, lift);
            foreach (var col in group.GetComponentsInChildren<Collider>()) UnityEngine.Object.Destroy(col);
            Bounds placedBounds = WorldBounds(primary);
            placed.bounds = placedBounds;
            // Every island, including the formerly LOD1-only remote ridges, has genuine
            // high geometry fitted/grounded with its cheaper versions. Instances share asset meshes.
            if (lod != null) parent.GetComponent<ReefProximityLod>().Register(lod, placedBounds, distant);
            if (collision && nearRenderers.Length > 0)
            {
                // Conservative rounded obstacle, with no expensive concave mesh physics.
                Bounds bounds = placedBounds;
                var obstacle = group.AddComponent<SphereCollider>();
                obstacle.center = group.transform.InverseTransformPoint(bounds.center);
                obstacle.radius = Mathf.Min(bounds.extents.x, bounds.extents.z, bounds.extents.y) * 0.85f;
            }
            return placed;
        }

        private static void Fit(GameObject model, Bounds reference, float height, float width)
        {
            // Preserve the original yaw-dependent size envelope, but derive it from LOD0 for every LOD.
            Bounds worldReference = new Bounds(model.transform.parent.TransformPoint(reference.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = reference.center + Vector3.Scale(reference.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                worldReference.Encapsulate(model.transform.parent.TransformPoint(local));
            }
            float scale = Mathf.Min(height / Mathf.Max(worldReference.size.y, 0.001f),
                width / Mathf.Max(worldReference.size.x, worldReference.size.z, 0.001f));
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = new Vector3(-reference.center.x * scale, -reference.min.y * scale, -reference.center.z * scale);
        }

        private static float SupportHeight(float x, float z, Func<float,float,float> floor, PlacedAsset rock, out bool onRock)
        {
            float ground = floor(x,z), surface = ground;
            onRock = rock != null && rock.TrySurface(x,z,out surface) && surface > ground;
            return onRock ? surface : ground;
        }

        private static void Ground(Transform group, PlacedAsset placed, string asset, Func<float,float,float> floor,
            PlacedAsset supportRock, bool coral, float lift)
        {
            float shift = float.PositiveInfinity;
            float smallestAnchor = float.PositiveInfinity, largestAnchor = float.NegativeInfinity;
            bool usesRock = false;
            foreach (ModelSurface model in placed.models)
            {
                Vector3[] contacts = coral ? model.geometry.coralContacts : model.geometry.rockContacts;
                float required = coral ? float.NegativeInfinity : float.PositiveInfinity;
                bool modelOnRock = false;
                foreach (Vector3 contact in contacts)
                {
                    Vector3 world = model.transform.TransformPoint(contact);
                    bool onRock;
                    float delta = SupportHeight(world.x,world.z,floor,coral ? supportRock : null,out onRock) - world.y;
                    if (coral ? delta > required : delta < required) { required = delta; modelOnRock = onRock; }
                }
                shift = Mathf.Min(shift,required);
                smallestAnchor = Mathf.Min(smallestAnchor,required);
                largestAnchor = Mathf.Max(largestAnchor,required);
                usesRock |= modelOnRock;
            }
            // Rocks bury their sampled scan skirts; every coral LOD retains at least one real root
            // contact. The small root embed hides simplification differences without invented pedestals.
            shift += coral ? -0.04f : Mathf.Clamp(lift, -0.15f, -0.06f);
            group.position += Vector3.up * shift;
            float worstGap = float.NegativeInfinity;
            float minimumGap = float.PositiveInfinity, maximumGap = float.NegativeInfinity;
            int samples = 0, unanchoredLods = 0;
            foreach (ModelSurface model in placed.models)
            {
                Vector3[] contacts = coral ? model.geometry.coralContacts : model.geometry.rockContacts;
                float gap = coral ? float.PositiveInfinity : float.NegativeInfinity;
                foreach (Vector3 contact in contacts)
                {
                    Vector3 world = model.transform.TransformPoint(contact);
                    bool onRock;
                    float current = world.y - SupportHeight(world.x,world.z,floor,coral ? supportRock : null,out onRock);
                    gap = coral ? Mathf.Min(gap,current) : Mathf.Max(gap,current);
                    minimumGap = Mathf.Min(minimumGap,current);
                    maximumGap = Mathf.Max(maximumGap,current);
                    samples++;
                }
                worstGap = Mathf.Max(worstGap,gap);
                if (gap > 0.005f) unanchoredLods++;
                grounding.lodsChecked++;
            }
            grounding.contactSamples += samples;
            grounding.unanchoredLods += unanchoredLods;
            grounding.minimumContactGap = Mathf.Min(grounding.minimumContactGap,minimumGap);
            grounding.maximumContactGap = Mathf.Max(grounding.maximumContactGap,maximumGap);
            float spread = largestAnchor-smallestAnchor;
            grounding.maximumLodAnchorSpread = Mathf.Max(grounding.maximumLodAnchorSpread,spread);
            if (coral)
            {
                grounding.corals++;
                if (usesRock) grounding.rockSupportedCorals++; else grounding.groundSupportedCorals++;
                grounding.maximumCoralAnchorGap = Mathf.Max(grounding.maximumCoralAnchorGap,worstGap);
            }
            else
            {
                grounding.rocks++;
                grounding.maximumRockBasalGap = Mathf.Max(grounding.maximumRockBasalGap,worstGap);
            }
            groundingSamples.Add(new GroundingSample { asset = asset, coral = coral, rockSupported = usesRock,
                position = group.position, verticalAdjustment = shift, contactGap = worstGap,
                minimumContactGap = minimumGap, maximumContactGap = maximumGap, contactSamples = samples,
                unanchoredLods = unanchoredLods, lodAnchorSpread = spread });
        }

        private static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position,Vector3.one);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void PrepareMaterials(Renderer[] renderers, bool coral, int palette, Dictionary<string,Material> cache)
        {
            foreach (Renderer r in renderers)
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material source = mats[i];
                    if (source == null) continue;
                    string key = source.GetInstanceID() + ":" + (coral ? palette.ToString() : "rock");
                    if (!cache.TryGetValue(key, out Material mat))
                    {
                        mat = new Material(source) { name = "Reef " + source.name, enableInstancing = true };
                        Color tint = coral ? CoralTints[palette] : new Color(0.73f, 0.78f, 0.70f);
                        if (mat.HasProperty("baseColorFactor")) mat.SetColor("baseColorFactor", tint);
                        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                        if (mat.HasProperty("roughnessFactor")) mat.SetFloat("roughnessFactor", 0.88f);
                        if (mat.HasProperty("metallicFactor")) mat.SetFloat("metallicFactor", 0f);
                        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
                        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.14f);
                        cache.Add(key, mat);
                    }
                    mats[i] = mat;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }
        }

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max-min);
    }
}

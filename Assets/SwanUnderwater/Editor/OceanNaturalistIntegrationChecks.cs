#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>
    /// Read-only checks against the real imported assets and, separately, a real
    /// running level. These checks never synthesize hand input or claim to execute
    /// transition rollback, coroutine cancellation or physical observation.
    /// </summary>
    public static class OceanNaturalistIntegrationChecks
    {
        static readonly string[] FishResources = {
            "Fish/01_clownfish", "Fish/02_blue_tang", "Fish/23_mandarinfish", "Fish/03_lionfish",
            "Fish/05_pufferfish_inflated", "Fish/20_seahorse", "Fish/22_flounder", "Fish/04_anglerfish",
            "Fish/16_manta_ray", "Fish/12_bluefin_tuna", "Fish/15_great_white_shark",
            "Fish/14_hammerhead_shark", "Fish/13_swordfish", "Fish/19_moray_eel", "Fish/21_ocean_sunfish"
        };
        public static int Assertions { get; private set; }

        // Loading the last model still yields one frame before ConfigureSubjects
        // registers its observations. Await the actual run-bound completion state.
        public static bool ObservationRegistryReady(OceanCampaignController campaign)
        {
            var naturalist = campaign?.Naturalist;
            if (naturalist == null) return false;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            return (bool)typeof(OceanNaturalist).GetField("configured", flags).GetValue(naturalist) &&
                ReferenceEquals(typeof(OceanNaturalist).GetField("boundRun", flags).GetValue(naturalist), campaign.State.Current);
        }

        static void Check(bool condition, string message)
        { Assertions++; if (!condition) throw new InvalidOperationException("Naturalist integration: " + message); }

        [MenuItem("OceanWorld/Validation/Naturalist imported assets")]
        public static void RunAll()
        {
            Assertions = 0;
            Check(!EditorApplication.isCompiling && !EditorApplication.isUpdating, "wait for actual Editor imports and compilation to finish");
            Check(OceanSpeciesCatalog.CatalogVersion == 3 && OceanSpeciesCatalog.Count == 31 && OceanSpeciesCatalog.All.Count == 31,
                "append-only observation catalog version 3 retains 28 old identities and adds butterflyfish, turtle and dolphin");
            Check(OceanSpeciesCatalog.AvailableCount == 30 && !OceanSpeciesCatalog.IsAvailable(13) &&
                !OceanSpeciesCatalog.IsAvailable(-1) && !OceanSpeciesCatalog.IsAvailable(OceanSpeciesCatalog.Count),
                "retired moray keeps its stable ID while the available observation catalog excludes it and invalid IDs");
            Check(TreasureHunt.SpeciesNames.Length == 10 && OceanLevelCatalog.FishSpeciesCount == 10,
                "legacy shell and target-fish identities remain unchanged");
            var paths = new HashSet<string>(StringComparer.Ordinal);
            for (int id = 0; id < OceanSpeciesCatalog.Count; id++)
            {
                var entry = OceanSpeciesCatalog.Get(id);
                Check(entry.Id == id && ReferenceEquals(entry, OceanSpeciesCatalog.All[id]), "catalog index and durable ID agree: " + id);
                Check(OceanSpeciesCatalog.IsAvailable(id) == (id != 13), "only the unverified moray is unavailable: " + id);
                Check(entry.FirstLevel >= 1 && entry.FirstLevel <= OceanLevelCatalog.Count, "unlock level belongs to the existing campaign: " + id);
                Check(!string.IsNullOrWhiteSpace(entry.Name) && !string.IsNullOrWhiteSpace(entry.Description) &&
                    !string.IsNullOrWhiteSpace(entry.Habitat) && !string.IsNullOrWhiteSpace(entry.SearchHint), "description and search clue exist: " + id);
                if (id < 15)
                {
                    Check(entry.ResourcePath == FishResources[id], "published fish model identity is stable: " + id);
                    if (id < OceanLevelCatalog.FishSpeciesCount)
                        Check(entry.Name == OceanLevelCatalog.FishNames[id] && entry.ResourcePath == OceanLevelCatalog.FishPaths[id],
                            "observation IDs 0-9 agree with old campaign fish IDs: " + id);
                }
                else if (id <= 24)
                {
                    Check(entry.Name == TreasureHunt.SpeciesNames[id - 15] && entry.ResourcePath == "", "observation IDs 15-24 agree with old procedural shells: " + id);
                    continue;
                }
                else if (id >= 27)
                    Check(entry.ResourcePath == new[] { "MarineExpansion/mackerel-volume", "MarineExpansion/butterfly-fish", "MarineExpansion/sea-turtle", "MarineExpansion/dolphin" }[id - 27], "appended marine animal points to admitted source or volume derivative bake");
                else
                    Check(entry.ResourcePath == (id == 25 ? "RealisticReef/market_coral_platygyra_lod0" : "RealisticReef/market_fan_coral_lod0"),
                        "new coral identities point to admitted market bake products: " + id);

                Check(paths.Add(entry.ResourcePath), "different catalog model categories do not silently share one resource path: " + id);
                if (!OceanSpeciesCatalog.IsAvailable(id))
                {
                    Check(Resources.Load<GameObject>(entry.ResourcePath) == null,
                        "retired identity keeps metadata without shipping its unavailable model: " + id);
                    continue;
                }
                GameObject asset = Resources.Load<GameObject>(entry.ResourcePath);
                Check(asset != null, "real imported Resources model missing for ID " + id + ": " + entry.ResourcePath +
                    (id >= 25 ? "; finish the market bake before marking this check passed" : ""));
                string assetPath = AssetDatabase.GetAssetPath(asset);
                Check(ResourceKey(assetPath) == entry.ResourcePath, "Resources resolves the intended source asset: " + id + " -> " + assetPath);
                CheckRenderable(asset, "imported category " + id);
            }
            Debug.Log("OceanNaturalistIntegrationChecks imported assets PASS " + Assertions +
                " assertions. This does not establish runtime registration, encounter reachability, input ownership or headset performance.");
        }

        /// <summary>
        /// Invoke only after the actual game has prepared a fresh level 20 and all
        /// optional residents. Existing gameplay state, files and transforms are untouched.
        /// </summary>
        [MenuItem("OceanWorld/Validation/Naturalist live fresh level 20")]
        public static void CheckFreshLevelTwentyScene()
        {
            Assertions = 0;
            Check(EditorApplication.isPlaying && !EditorApplication.isPaused, "run the real game in Play Mode first");
            var world = UnityEngine.Object.FindFirstObjectByType<UnderwaterWorld>();
            var campaign = UnityEngine.Object.FindFirstObjectByType<OceanCampaignController>();
            var hunt = UnityEngine.Object.FindFirstObjectByType<TreasureHunt>();
            Check(world != null && campaign != null && hunt != null && campaign.Naturalist != null, "actual world, campaign, pickups and observation component exist");
            Check(world.IsReady && !campaign.EnvironmentTransitioning && world.ActiveEnvironment == OceanEnvironmentKind.CoralSanctuary,
                "actual sanctuary is fully prepared and no environment operation is in progress");
            var run = campaign.State.Current;
            Check(run != null && run.level == 20 && run.shellMask == 0 && run.fishMask == 0 && run.coinMask == 0 &&
                !run.toolOwned && !run.sharkCaught && !run.completed && !run.failed && run.consumedPickupIds.Count == 0,
                "use a genuinely fresh level 20 in an isolated test save; this check never resets the player's run");
            CheckRetiredMorayAbsentInScene(world, campaign);

            var represented = new HashSet<int>();
            var animals = new List<GameObject>(); world.GatherNaturalistAnimals(animals);
            foreach (GameObject model in animals)
            {
                if (!VisibleModel(model)) continue;
                for (int id = 0; id < FishResources.Length; id++)
                {
                    string path = FishResources[id], name = path.Substring(path.LastIndexOf('/') + 1);
                    if (model.name.IndexOf(name, StringComparison.Ordinal) >= 0) { represented.Add(id); break; }
                }
            }
            foreach (var target in campaign.Creatures.Targets)
                if (VisibleModel(target.Visual)) represented.Add(target.ObservationSpecies);
            foreach (var shell in hunt.Pickups)
                if (shell.kind >= 0 && !shell.consumed && VisibleModel(shell.visual)) represented.Add(15 + shell.kind);
            foreach (var tag in world.ActiveEnvironmentRoot.GetComponentsInChildren<OceanNaturalistTag>(true))
                if (VisibleModel(tag.gameObject)) represented.Add(tag.Species);

            // Check the actual runtime registry independently of the visible-model
            // inventory. Merely seeing a model does not prove it is observable.
            var registered = new HashSet<int>();
            var subjects = typeof(OceanNaturalist).GetField("subjects", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(campaign.Naturalist) as IEnumerable;
            Check(subjects != null, "actual observation registry is available");
            foreach (object subject in subjects)
            {
                Type type = subject.GetType();
                var model = type.GetField("model").GetValue(subject) as Transform;
                if (model != null && VisibleModel(model.gameObject))
                    registered.Add((int)type.GetField("species").GetValue(subject));
            }
            for (int id = 0; id < OceanSpeciesCatalog.Count; id++)
            {
                if (!OceanSpeciesCatalog.IsAvailable(id))
                {
                    Check(!registered.Contains(id) && !represented.Contains(id), "retired category is neither registered nor represented: " + id);
                    continue;
                }
                Check(registered.Contains(id), "actual observation registry is missing available category " + id);
                Check(represented.Contains(id), "actual active level has no visible model for " + id + " " + OceanSpeciesCatalog.Get(id).Name +
                    "; wait for asynchronous resident loading, then inspect the real placement if still missing");
            }
            Check(registered.Count == OceanSpeciesCatalog.AvailableCount && represented.Count == OceanSpeciesCatalog.AvailableCount,
                "actual fresh level contains exactly the thirty available model categories");
            Debug.Log("OceanNaturalistIntegrationChecks live model inventory PASS " + Assertions +
                " assertions; all 30 available categories exist, retained ID13 has no scene model or registry subject. Player reachability, occlusion and hand input still require the documented live regression.");
        }

        /// <summary>Actual hierarchy and registry check; never edits actors or discovery data.</summary>
        public static void CheckRetiredMorayAbsentInScene(UnderwaterWorld world, OceanCampaignController campaign)
        {
            Check(world != null && world.ActiveEnvironmentRoot != null && campaign != null && campaign.Naturalist != null,
                "real active environment and observation registry are required for retired-resource exclusion");
            foreach (Transform model in world.ActiveEnvironmentRoot.GetComponentsInChildren<Transform>(true))
                Check(model.name.IndexOf("19_moray_eel", StringComparison.OrdinalIgnoreCase) < 0,
                    "actual active hierarchy contains no retired moray source, including inactive children");
            foreach (var tag in world.ActiveEnvironmentRoot.GetComponentsInChildren<OceanNaturalistTag>(true))
                Check(tag.Species != 13, "actual active environment contains no retired ID13 observation tag");
            var subjects = typeof(OceanNaturalist).GetField("subjects", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(campaign.Naturalist) as IEnumerable;
            Check(subjects != null, "real observation subject list exists for retired-resource exclusion");
            foreach (object subject in subjects)
                Check((int)subject.GetType().GetField("species").GetValue(subject) != 13,
                    "retired ID13 is absent from the production observation registry");
        }

        static string ResourceKey(string assetPath)
        {
            const string marker = "/Resources/";
            int start = assetPath.LastIndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return "";
            string key = assetPath.Substring(start + marker.Length);
            int dot = key.LastIndexOf('.'); return dot < 0 ? key : key.Substring(0, dot);
        }

        static bool VisibleModel(GameObject model)
        {
            if (model == null || !model.activeInHierarchy) return false;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy) return true;
            return false;
        }

        static void CheckRenderable(GameObject model, string label)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Check(renderers.Length > 0, label + " has real renderers");
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                Check(mesh != null && mesh.vertexCount > 0 && mesh.subMeshCount > 0, label + " has imported mesh data: " + renderer.name);
                Material[] materials = renderer.sharedMaterials;
                Check(materials.Length > 0, label + " has material slots: " + renderer.name);
                foreach (Material material in materials)
                    Check(material != null && material.shader != null, label + " has material and shader references: " + renderer.name);
                Bounds b = renderer.localBounds;
                Check(Finite(b.center) && Finite(b.size) && b.size.sqrMagnitude > .000000001f, label + " has finite nonzero renderer bounds: " + renderer.name);
            }
        }
        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
            !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    }
}
#endif

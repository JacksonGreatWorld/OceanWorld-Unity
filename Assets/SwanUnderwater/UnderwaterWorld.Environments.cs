using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater
{
    public sealed partial class UnderwaterWorld
    {
        sealed class PreparedEnvironment
        {
            public OceanEnvironmentKind kind;
            public OceanCampaignBiome biome;
            public OceanEnvironmentIdentity Identity => new OceanEnvironmentIdentity(kind, biome);
            public Transform root, reef, fish, schooling, large;
            public readonly List<FishAgent> heroes = new List<FishAgent>();
            public readonly List<Boid> schoolingFish = new List<Boid>();
            public readonly List<BoidSchool> schools = new List<BoidSchool>();
            public readonly List<LargeCreature> creatures = new List<LargeCreature>();
            public readonly List<Bounds> solids = new List<Bounds>();
            public readonly List<ReefHabitat.CoralPocket> pockets = new List<ReefHabitat.CoralPocket>();
            public readonly List<Vector3> vents = new List<Vector3>();
            public MaterialPropertyBlock[] blocks;
        }

        sealed class EnvironmentConstructionVisibility : IDisposable
        {
            readonly Transform root;
            readonly Dictionary<Renderer, bool> originalFlags = new Dictionary<Renderer, bool>();
            readonly List<Renderer> renderers = new List<Renderer>();

            public EnvironmentConstructionVisibility(Transform root) { this.root = root; }

            public void SuspendNewRenderers()
            {
                if (root == null) return;
                root.GetComponentsInChildren(true, renderers);
                foreach (Renderer renderer in renderers)
                {
                    if (!originalFlags.ContainsKey(renderer)) originalFlags.Add(renderer, renderer.forceRenderingOff);
                    renderer.forceRenderingOff = true;
                }
            }

            public void Dispose()
            {
                foreach (KeyValuePair<Renderer, bool> original in originalFlags)
                    if (original.Key != null) original.Key.forceRenderingOff = original.Value;
                originalFlags.Clear(); renderers.Clear();
            }
        }

        readonly Dictionary<OceanEnvironmentIdentity, PreparedEnvironment> preparedEnvironments =
            new Dictionary<OceanEnvironmentIdentity, PreparedEnvironment>();
        PreparedEnvironment activeEnvironment, buildingEnvironment;
        bool preparingEnvironment;
        bool restoreReadyAfterPreparation;
        OceanEnvironmentVeil transitionVeil;
        static readonly Bounds[] NoSolids = Array.Empty<Bounds>();

        Transform EnvironmentParent => buildingEnvironment != null ? buildingEnvironment.root :
            activeEnvironment != null ? activeEnvironment.root : transform;
        public Transform ActiveEnvironmentRoot => EnvironmentParent;
        public Transform ActiveReefRoot => buildingEnvironment != null ? buildingEnvironment.reef : activeEnvironment?.reef;
        public IReadOnlyList<Bounds> ActiveSolidBounds => buildingEnvironment != null ? buildingEnvironment.solids :
            activeEnvironment != null ? activeEnvironment.solids : NoSolids;
        public OceanEnvironmentKind ActiveEnvironment => activeEnvironment != null ? activeEnvironment.kind : OceanEnvironmentKind.Classic;
        public OceanCampaignBiome ActiveBiome => activeEnvironment != null ? activeEnvironment.biome : OceanCampaignBiome.Reef;
        internal OceanCampaignBiome ConstructingBiome => (buildingEnvironment ?? activeEnvironment)?.biome ?? OceanCampaignBiome.Reef;
        // Both environments deliberately use the same world-anchored sand sampler and physical scale.
        public Vector3 ActiveSpawn => new Vector3(SpawnX, 0, SpawnZ);

        void BeginInitialEnvironment()
        {
            string directory = Application.persistentDataPath;
#if UNITY_EDITOR
            string[] arguments = Environment.GetCommandLineArgs();
            for(int i=0;i+1<arguments.Length;i++)
                if(arguments[i]=="-codexCampaignSaveDir") directory=System.IO.Path.GetFullPath(arguments[i+1]);
#endif
            int level = 1;
            if (OceanSaveStore.TryLoad(directory, out OceanSaveData saved, out string ignored) && saved?.currentRun != null)
                level = Mathf.Clamp(saved.currentRun.level,1,OceanLevelCatalog.Count);
            int presentationLevel = OceanCampaignAvailability.EntryLevel(level);
            var identity = OceanEnvironmentIdentity.ForLevel(presentationLevel);
            var kind = identity.Terrain;
            var root = new GameObject("Environment · " + identity).transform;
            root.SetParent(transform, false);
            activeEnvironment = new PreparedEnvironment { kind = kind, biome = identity.Biome, root = root };
            ApplyEnvironmentAtmosphere(kind);
            Debug.Log($"[OceanEnvironment] startup savedLevel={level} presentationLevel={presentationLevel} environment={kind}; only the published presentation is constructed; saved_run_unchanged=true");
        }

        void CaptureInitialEnvironment()
        {
            if (activeEnvironment.reef == null) throw new InvalidOperationException("Initial reef root is missing.");
            CaptureEnvironment(activeEnvironment);
            preparedEnvironments.Add(activeEnvironment.Identity, activeEnvironment);
            ApplyEnvironmentAtmosphere(activeEnvironment.kind);
        }

        void CaptureEnvironment(PreparedEnvironment environment)
        {
            environment.fish = fishRoot; environment.schooling = boidRoot; environment.large = largeCreatureRoot;
            environment.heroes.Clear(); environment.heroes.AddRange(fishAgents);
            environment.schoolingFish.Clear(); environment.schoolingFish.AddRange(boids);
            environment.schools.Clear(); environment.schools.AddRange(boidSchools);
            environment.creatures.Clear(); environment.creatures.AddRange(largeCreatures);
            environment.blocks = boidMPBs;
            environment.pockets.Clear(); environment.pockets.AddRange(ReefHabitat.Pockets);
            environment.vents.Clear(); environment.vents.AddRange(ReefHabitat.BubbleSources);
        }

        void RestoreEnvironment(PreparedEnvironment environment)
        {
            fishRoot = environment.fish; boidRoot = environment.schooling; largeCreatureRoot = environment.large;
            fishAgents.Clear(); fishAgents.AddRange(environment.heroes);
            boids.Clear(); boids.AddRange(environment.schoolingFish);
            boidSchools.Clear(); boidSchools.AddRange(environment.schools);
            boidSchoolCount = environment.schools.Count;
            largeCreatures.Clear(); largeCreatures.AddRange(environment.creatures);
            boidMPBs = environment.blocks;
            ReefHabitat.SetActiveData(environment.pockets, environment.vents);
            activeEnvironment = environment;
            buildingEnvironment = null;
            ApplyEnvironmentAtmosphere(environment.kind);
            CacheLargeReefBounds();
            foreach (var creature in largeCreatures)
                if (creature.isShark && creature.transform != null && creature.transform.gameObject.activeInHierarchy &&
                    !campaignSharks.Contains(creature.transform))
                    EnsureAmbientSharkWaterStart(creature);
        }

        public bool ExternalTransitionCoverActive { get; set; }

        /// <summary>Prepare once, then atomically switch the active environment. A failed build retains the old world.</summary>
        public IEnumerator PrepareEnvironmentForLevel(int level, Action<bool> complete)
        {
            var requested = OceanEnvironmentIdentity.ForLevel(level);
            if (activeEnvironment == null || preparingEnvironment) { complete?.Invoke(false); yield break; }
            if (activeEnvironment.Identity.Equals(requested)) { complete?.Invoke(true); yield break; }
            preparingEnvironment = true;
            bool wasReady = IsReady;
            restoreReadyAfterPreparation = wasReady;
            PreparedEnvironment previous = activeEnvironment;
            double started = Time.realtimeSinceStartupAsDouble;
            PreparedEnvironment target = null;
            bool targetPrepared = false, succeeded = false;
            IsReady = false;
            try
            {
                CaptureEnvironment(previous);
                if (wasReady && transitionVeil == null && !ExternalTransitionCoverActive) transitionVeil = OceanEnvironmentVeil.Show(mainCamera);
                if (transitionVeil != null) yield return transitionVeil.Cover();
                Exception failure = null;
                if (preparedEnvironments.TryGetValue(requested, out target)) targetPrepared = true;
                else
                {
                    var go = new GameObject("Environment · " + requested);
                    target = new PreparedEnvironment { kind = requested.Terrain, biome = requested.Biome, root = go.transform };
                    buildingEnvironment = target;
                    go.transform.SetParent(transform, false);
                    fishAgents.Clear(); boids.Clear(); boidSchools.Clear(); largeCreatures.Clear();
                    yield return BuildPreparedEnvironmentHidden(target, error => failure = error);
                    if (failure == null)
                    {
                        CaptureEnvironment(target);
                        preparedEnvironments.Add(requested, target);
                        targetPrepared = true;
                    }
                }
                if (failure != null)
                    Debug.LogError("[OceanEnvironment] prepare_failed; retained previous environment: " + failure);
                else
                {
                    previous.root.gameObject.SetActive(false);
                    target.root.gameObject.SetActive(true);
                    RestoreEnvironment(target);
                    Physics.SyncTransforms();
                    // A cancelled operation at this yield must still restore the old environment.
                    // Rendering may submit the prepared world while its simulation remains paused.
                    yield return null;
                    succeeded = true;
                }
            }
            finally
            {
                // This boundary includes cover, snapshot, activation and physics failures, not
                // only exceptions from the nested builder. Iterator disposal also runs it.
                try
                {
                    if (!succeeded)
                    {
                        if (target != null && target != previous && target.root != null)
                        {
                            target.root.gameObject.SetActive(false);
                            if (!targetPrepared)
                            {
                                if (preparedEnvironments.TryGetValue(requested, out PreparedEnvironment cached) &&
                                    ReferenceEquals(cached, target)) preparedEnvironments.Remove(requested);
                                Destroy(target.root.gameObject);
                            }
                        }
                        previous.root.gameObject.SetActive(true);
                        RestoreEnvironment(previous);
                        Physics.SyncTransforms();
                    }
                }
                finally
                {
                    buildingEnvironment = null;
                    IsReady = wasReady;
                    preparingEnvironment = false;
                    if (!succeeded)
                    {
                        if (transitionVeil != null) Destroy(transitionVeil.gameObject);
                        transitionVeil = null;
                    }
                }
            }
            Debug.Log($"[OceanEnvironment] active={ActiveEnvironment} level={level} success={succeeded} elapsed_ms={(Time.realtimeSinceStartupAsDouble-started)*1000:F1} cached={preparedEnvironments.Count}");
            complete?.Invoke(succeeded);
        }

        public IEnumerator FinishEnvironmentTransition()
        {
            OceanEnvironmentVeil veil = transitionVeil;
            if (veil != null) yield return veil.Reveal();
            transitionVeil = null;
        }

        public void AbortEnvironmentTransition()
        {
            if (transitionVeil != null) Destroy(transitionVeil.gameObject);
            transitionVeil = null;
            if (!preparingEnvironment) return;
            if (buildingEnvironment != null && buildingEnvironment != activeEnvironment)
            {
                buildingEnvironment.root.gameObject.SetActive(false);
                // A completed cached map is reusable even if the caller aborts its presentation.
                if (!preparedEnvironments.TryGetValue(buildingEnvironment.Identity, out PreparedEnvironment cached) ||
                    !ReferenceEquals(cached, buildingEnvironment)) Destroy(buildingEnvironment.root.gameObject);
            }
            if (activeEnvironment != null)
            {
                activeEnvironment.root.gameObject.SetActive(true);
                RestoreEnvironment(activeEnvironment);
            }
            preparingEnvironment = false;
            IsReady = restoreReadyAfterPreparation;
        }

        public void ResetEnvironmentReveal()
        {
            if (activeEnvironment == null || mainCamera == null) return;
            var guardian = activeEnvironment.root.GetComponentInChildren<OceanSkyGuardian>(true);
            if (guardian != null) guardian.ResetEncounter(mainCamera.transform.position, Vector3.forward);
        }

        void CreateEnvironmentGuardian()
        {
            var guardian = OceanSkyGuardian.Build(this, mainCamera, ActiveSpawn, EnvironmentParent);
            var environment = buildingEnvironment ?? activeEnvironment;
            if (environment != null && environment.kind == OceanEnvironmentKind.CoralSanctuary && environment.biome == OceanCampaignBiome.Reef)
                guardian.UseSanctuaryComposition();
        }

        IEnumerator BuildEnvironmentReef(PreparedEnvironment environment, Action<float> progress = null)
        {
            if (environment.biome != OceanCampaignBiome.Reef)
            {
                var map = OceanCampaignBiomeGeometry.Build(environment.root, environment.biome, SampleRenderedSeabedHeight, ActiveSpawn);
                environment.reef = map.transform;
                environment.solids.AddRange(map.Solids);
                ReefHabitat.SetActiveData(map.Pockets, map.Vents);
                progress?.Invoke(1f);
                yield return null;
            }
            else if(environment.kind == OceanEnvironmentKind.Classic)
            {
                yield return ReefHabitat.BuildStaged(environment.root,SampleRenderedSeabedHeight,ActiveSpawn,progress);
                environment.reef=environment.root.Find("Scanned Reef Habitat");
            }
            else
            {
                yield return OceanCoralSanctuary.BuildStaged(environment.root,SampleRenderedSeabedHeight,ActiveSpawn,progress);
                environment.reef=environment.root.Find("Coral Sanctuary");
                environment.solids.AddRange(OceanCoralSanctuary.SolidBounds);
                ReefHabitat.SetActiveData(OceanCoralSanctuary.Pockets,OceanCoralSanctuary.BubbleSources);
            }
            if(environment.reef==null)throw new InvalidOperationException("Prepared reef root is missing.");
        }

        IEnumerator BuildPreparedEnvironment(PreparedEnvironment environment)
        {
            CreateSeabedTerrain(); yield return null;
            CreateNearSeabedPatch(); yield return null;
            yield return BuildEnvironmentReef(environment);
            SeagrassMeadow.Build(environment.root, ActiveSpawn, BuildingSanctuary); yield return null;
            SeabedShells.Build(environment.root, ActiveSpawn, BuildingSanctuary); yield return null;
            CreateBubbleVents();
            UnderwaterAmbience.Build(environment.root, ActiveSpawn, BuildingSanctuary); yield return null;
            yield return CreateFishSchool();
            yield return CreateBoids();
            CreateLargeCreatures(); yield return null;
            CreateEnvironmentGuardian();
            CreateSanctuaryContent();
            Debug.Log($"[OceanEnvironment] prepared environment={environment.kind} pockets={ReefHabitat.Pockets.Count} vents={ReefHabitat.BubbleSources.Count} solids={environment.solids.Count} hero={fishAgents.Count} schools={boidSchools.Count}");
        }

        IEnumerator BuildPreparedEnvironmentHidden(PreparedEnvironment environment, Action<Exception> failed)
        {
            // Keep the hierarchy active: fitting, LOD registration, ground exclusion and
            // initializers use real renderer/collider bounds. Suppress submission only,
            // before each flattened frame yield, so the two maps never render together.
            var visibility = new EnvironmentConstructionVisibility(environment.root);
            IEnumerator operation = OceanCampaignController.RunTransitionOperation(BuildPreparedEnvironment(environment), failed);
            try
            {
                while (operation.MoveNext())
                {
                    visibility.SuspendNewRenderers();
                    yield return operation.Current;
                }
            }
            finally
            {
                try { (operation as IDisposable)?.Dispose(); }
                finally { visibility.Dispose(); }
            }
        }
    }
}

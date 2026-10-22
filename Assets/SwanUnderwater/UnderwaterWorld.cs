using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace SwanUnderwater
{
    public sealed partial class UnderwaterWorld : MonoBehaviour
    {
        [Header("World")]
        [SerializeField] private int fishCount = 120;
        [SerializeField] private Vector3 swimArea = new Vector3(32f, 10f, 32f);
        [SerializeField] private int bubbleVentCount = 5;
        [SerializeField] private int reefClusterCount = 90;

        [Header("Schooling fish (CPU simulation, shared instanced materials)")]
        [SerializeField] private int boidCount = 1200;
        [SerializeField] private int boidSchoolCount = 12;
        [SerializeField] private float boidSchoolRadius = 5f;
        [SerializeField] private float boidSpeed = 2.5f;
        [SerializeField] private float boidSpawnRadius = 40f;
        [SerializeField] private float boidDespawnRadius = 70f;
        [SerializeField] private int largeCreatureCount = 4;
        [SerializeField] private float largeCreatureSpeed = 1.2f;
        [SerializeField] private float largeCreatureOrbitRadius = 18f;

        [Header("Resource paths")]
        [SerializeField] private string fishResourceFolder = "Fish";
        [SerializeField] private string envResourceFolder = "Environment";
        [SerializeField] private string rockResourceFolder = "Environment/Rocks";
        [SerializeField] private string sandColorPath = "Textures/Sand/Ground101_Color";
        [SerializeField] private string sandNormalPath = "Textures/Sand/Ground101_NormalGL";
        [SerializeField] private string rockColorPath = "Textures/Rock/Rock019_Color";
        [SerializeField] private string rockNormalPath = "Textures/Rock/Rock019_NormalGL";

        [Header("Diagnostics")]
        [SerializeField] private bool autoRotateTest = false;

        private readonly List<FishAgent> fishAgents = new List<FishAgent>();
        private Transform fishRoot;
        private Material sandMaterial;
        private Material underwaterSky;
        private Light underwaterSun;

        // Boids system
        private readonly List<Boid> boids = new List<Boid>();
        private readonly List<BoidSchool> boidSchools = new List<BoidSchool>();
        private readonly List<LargeCreature> largeCreatures = new List<LargeCreature>();
        private Transform boidRoot;
        private Transform largeCreatureRoot;
        private GameObject[] boidPrefabPool; // shared fish prefabs for boids
        private MaterialPropertyBlock[] boidMPBs;
        private int boidUpdateFrameOffset;
        private readonly Dictionary<string, Material[]> boidMaterialCache = new Dictionary<string, Material[]>();
        private readonly Dictionary<string, Material> heroMaterialCache = new Dictionary<string, Material>();
        private Material rockMaterial;
        private Material bubbleMaterial;
        private Material domeMaterial;
        private Material fishSwimTemplate;
        private GameObject[] fishPrefabGO;
        private Camera mainCamera;
        private Transform xrOrigin;
        private HandSwimController handController;

        private const float BaseTerrainY = -2.8f;
        private const float SpawnX = OceanEnvironmentProfile.SpawnX, SpawnZ = OceanEnvironmentProfile.SpawnZ;
        // The existing public terrain-query API is shared by props, projectiles and
        // locomotion. Resolve its map from the owner, including an in-progress build,
        // instead of leaving a mutable profile behind after transition cancellation.
        private static readonly List<UnderwaterWorld> seabedOwners = new List<UnderwaterWorld>();
        internal OceanEnvironmentKind ConstructingEnvironmentKind =>
            (buildingEnvironment ?? activeEnvironment)?.kind ?? OceanEnvironmentKind.Classic;
        public static OceanEnvironmentKind RenderedSeabedEnvironment
        {
            get
            {
                // Usually one owner. Preserve the older live context if an isolated
                // preview/test world is created and destroyed in the same process.
                for (int i = seabedOwners.Count - 1; i >= 0; i--)
                    if (seabedOwners[i] != null) return seabedOwners[i].ConstructingEnvironmentKind;
                return OceanEnvironmentKind.Classic;
            }
        }

        private ReefExperienceSettings experience;
        private float closestSharkDistanceEver = float.PositiveInfinity;
        public bool IsReady { get; private set; }
        private OceanEntry loading;
        private readonly List<UnityEngine.Object> startupResources = new List<UnityEngine.Object>();

        private void Awake()
        {
            seabedOwners.Add(this);
            experience = ReefExperienceSettings.Load();
            Application.targetFrameRate = 90;
            QualitySettings.vSyncCount = 0;
            OceanClarityPolicy.ApplyGlobal();

            // Runtime override: the scene/Inspector may serialize older design-time
            // values (140 hero, 6 large). Enforce the current ecological-layering
            // numbers here so a build never silently uses stale serialized counts.
            fishCount = experience.heroFish;
            boidCount = experience.schoolingFish;
            boidSchoolCount = experience.schools;
            largeCreatureCount = experience.largeFish;
            largeCreatureOrbitRadius = experience.largeFishDistance;
            Random.InitState(20260917);
            autoRotateTest = false;

            ConfigurePlayer();
            // Keep tracking initialization alive so custom hand models can bind before
            // the reveal; suppress every locomotion input while the world is incomplete.
            handController.SetInteractionSuppressed(true);
            loading = OceanEntry.Show(mainCamera);
        }

        private void OnDestroy()
        {
            seabedOwners.Remove(this);
        }

        private System.Collections.IEnumerator Start()
        {
            // Flatten nested jobs so a failed stage keeps an explicit error card instead of
            // revealing an incomplete world or leaving a fictitious completed progress bar.
            var jobs = new Stack<System.Collections.IEnumerator>();
            jobs.Push(InitializeWorld());
            while (jobs.Count > 0)
            {
                object current = null;
                bool more = false;
                System.Exception failure = null;
                try { more = jobs.Peek().MoveNext(); if (more) current = jobs.Peek().Current; }
                catch (System.Exception e) { failure = e; }
                if (failure != null)
                {
                    Debug.LogException(failure);
                    if (loading != null) loading.Fail();
                    yield break;
                }
                if (!more) { (jobs.Pop() as System.IDisposable)?.Dispose(); continue; }
                if (current is System.Collections.IEnumerator nested) jobs.Push(nested);
                else yield return current;
            }
        }

        private System.Collections.IEnumerator InitializeWorld()
        {
            // A real camera submission is required before any substantial resource work.
            double phaseStart = BeginStartupStage("await_opaque");
            while (loading != null && !loading.WorldBuildAllowed) yield return null;
            EndStartupStage("await_opaque", phaseStart);
            ConfigureUnderwaterAtmosphere();
            yield return TimedStartupStage("preload", PreloadWorldResources());
            phaseStart = BeginStartupStage("materials");
            CreateMaterials();
            LoadFishPrefabs();
            EndStartupStage("materials", phaseStart);
            loading.SetProgress(0.30f, "准备海底地形");
            yield return null;
            BeginInitialEnvironment();
            phaseStart = BeginStartupStage("terrain");
            CreateSeabedTerrain();
            yield return null;
            CreateNearSeabedPatch();
            yield return null;
            EndStartupStage("terrain", phaseStart);
            yield return TimedStartupStage("reef", BuildEnvironmentReef(activeEnvironment,
                p => loading.SetProgress(0.32f + p * 0.40f, "准备珊瑚")));
            phaseStart = BeginStartupStage("seagrass");
            SeagrassMeadow.Build(EnvironmentParent, new Vector3(SpawnX, 0f, SpawnZ),
                BuildingSanctuary);
            SeabedShells.Build(EnvironmentParent, new Vector3(SpawnX, 0f, SpawnZ),
                BuildingSanctuary);
            EndStartupStage("seagrass", phaseStart);
            yield return null;
            phaseStart = BeginStartupStage("ambient");
            CreateBubbleVents();
            CreateUnderwaterDome();
            UnderwaterAmbience.Build(EnvironmentParent, new Vector3(SpawnX, 0, SpawnZ),
                BuildingSanctuary);
            EndStartupStage("ambient", phaseStart);
            loading.SetProgress(0.76f, "准备鱼群");
            yield return TimedStartupStage("fish", CreateFishSchool());
            yield return TimedStartupStage("boids", CreateBoids());
            phaseStart = BeginStartupStage("large_fish");
            CreateLargeCreatures();
            EndStartupStage("large_fish", phaseStart);
            phaseStart = BeginStartupStage("guardian");
            CreateEnvironmentGuardian();
            CreateSanctuaryContent();
            EndStartupStage("guardian", phaseStart);
            CaptureInitialEnvironment();
            yield return TimedStartupStage("treasure", TreasureHunt.BuildStaged(this, mainCamera, handController, ActiveSpawn));
            var campaignHunt = GetComponentInChildren<TreasureHunt>(true);
            if (campaignHunt == null || campaignHunt.Campaign == null)
                throw new System.InvalidOperationException("Startup campaign unavailable.");
            campaignHunt.Campaign.PrepareActorsForEntry();
            loading.SetProgress(0.96f, "即将抵达");
            phaseStart = BeginStartupStage("hands_audio");
            gameObject.AddComponent<DiverHandVisuals>();
            gameObject.AddComponent<HandWaterWake>();
            SwimFlowVfx.Install(handController, mainCamera);
            SetupBackgroundAudio();
            // Render the completed scene under the opaque card before fading it away.
            yield return null;
            yield return null;
            EndStartupStage("hands_audio", phaseStart);
            loading.Complete();
            phaseStart = BeginStartupStage("reveal_to_controls");
            while (loading != null && !loading.WorldPlayable) yield return null;
            // The complete scene is exposed before controls are released. The original
            // readable captions can continue over gameplay, and never add a second wait.
            IsReady = true;
            OceanClarityPolicy.ReportReady();
            handController.SetInteractionSuppressed(false);
            startupResources.Clear();
            EndStartupStage("reveal_to_controls", phaseStart);
            Debug.Log($"[OceanEntry] control_ready frame={Time.frameCount} t={Time.realtimeSinceStartupAsDouble:F3} captionsActive={OceanEntry.IsLoading}");
            Debug.Log($"[OceanEntry] world_ready frame={Time.frameCount} t={Time.realtimeSinceStartupAsDouble:F3}");
        }

        static double BeginStartupStage(string stage)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            Debug.Log($"[OceanStartup] stage={stage} event=begin t={now:F3} frame={Time.frameCount}");
            return now;
        }
        static void EndStartupStage(string stage, double start)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            Debug.Log($"[OceanStartup] stage={stage} event=end elapsed_ms={(now-start)*1000:F3} t={now:F3} frame={Time.frameCount}; wall duration including yielded frames, not GPU timing");
        }
        static System.Collections.IEnumerator TimedStartupStage(string stage, System.Collections.IEnumerator job)
        {
            double start = BeginStartupStage(stage);
            yield return job;
            EndStartupStage(stage, start);
        }

        private static readonly string[] HeroSpecies = { "01_clownfish", "02_blue_tang", "03_lionfish",
            "05_pufferfish_inflated", "20_seahorse", "22_flounder", "23_mandarinfish", "21_ocean_sunfish", "04_anglerfish" };
        private System.Collections.IEnumerator PreloadWorldResources()
        {
            var paths = new List<string>();
            foreach (string name in HeroSpecies) paths.Add(fishResourceFolder + "/" + name);
            foreach (string name in new[] { "15_great_white_shark", "16_manta_ray", "14_hammerhead_shark", "12_bluefin_tuna", "04_anglerfish" })
                paths.Add(fishResourceFolder + "/" + name);
            foreach (string name in new[] { "02_blue_tang", "23_mandarinfish", "01_clownfish" }) paths.Add("SchoolFish/" + name);
            paths.AddRange(new[] { sandColorPath, sandNormalPath, rockColorPath, rockNormalPath,
                "Bubbles/bubble_ring_glowCircle_512", "DiverHands/LeftHand", "DiverHands/RightHand", "Audio/underwater_ambient" });
            // Hold completed requests until synchronous consumers take ownership; no LoadAll
            // of unused fish species and no unload while the loading card is active.
            for (int i = 0; i < paths.Count; i++)
            {
                ResourceRequest request = Resources.LoadAsync(paths[i]);
                while (!request.isDone)
                {
                    loading.SetProgress(0.03f + 0.25f * (i + request.progress) / paths.Count, "正在潜入深蓝");
                    yield return null;
                }
                if (request.asset == null) throw new System.InvalidOperationException("Missing startup resource: " + paths[i]);
                startupResources.Add(request.asset);
                loading.SetProgress(0.03f + 0.25f * (i + 1) / paths.Count, "正在潜入深蓝");
                yield return null;
            }
        }

        private float perfTimer = 0f;
        private int perfFrames = 0;

        private void Update()
        {
            if (!IsReady) return;
            for (int i = 0; i < fishAgents.Count; i++)
                {
                fishAgents[i].Tick(Time.deltaTime, transform.position, swimArea, handController);
                fishAgents[i].TickVisual(Time.deltaTime);
            }
            long boidStepStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            UpdateBoids(Time.deltaTime);
            RuntimeDiagnostics.RecordBoidStepTicks(System.Diagnostics.Stopwatch.GetTimestamp() - boidStepStarted);
            UpdateLargeCreatures(Time.deltaTime);

            // Performance monitor: log FPS + fish count every 5s
            perfTimer += Time.unscaledDeltaTime;
            perfFrames++;
            if (perfTimer >= 5f)
            {
                float fps = perfFrames / perfTimer;
                int totalFish = fishAgents.Count + boids.Count + largeCreatures.Count;
                Debug.Log($"[Perf] FPS={fps:F1}, hero={fishAgents.Count}, boids={boids.Count}, large={largeCreatures.Count}, total={totalFish}");
                LogReefLife();
                perfTimer = 0f;
                perfFrames = 0;
            }
        }

        // ==================================================================
        // 1. ATMOSPHERE
        // ==================================================================
        private void ConfigureUnderwaterAtmosphere()
        {
            // Keep nearby detail, then absorb the warm sand into deep water.
            // At low eye heights a distant transition compresses into a narrow horizon band.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.038f;
            RenderSettings.fogColor = new Color(0.032f, 0.19f, 0.235f, 1f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.24f, 0.41f, 0.46f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.25f, 0.29f);
            RenderSettings.ambientGroundColor = new Color(0.055f, 0.095f, 0.11f);

            // A pure flat teal background is the main reason screenshots read as an
            // aquarium. Use the included gradient skybox shader; it contains no
            // transparent geometry or depth sorting, so it is safer than the failed
            // water-surface mesh on OpenGLES3.
            Shader skyShader = Shader.Find("SwanUnderwater/UnderwaterSkybox");
            Camera cam = Camera.main;
            if (skyShader != null)
            {
                Material sky = new Material(skyShader) { name = "Runtime Underwater Skybox" };
                sky.SetColor("_TopColor", new Color(0.30f, 0.66f, 0.68f));
                sky.SetColor("_HorizonColor", RenderSettings.fogColor);
                sky.SetColor("_BottomColor", new Color(0.008f, 0.07f, 0.09f));
                sky.SetFloat("_TopIntensity", 1.05f);
                sky.SetFloat("_HorizonIntensity", 1f);
                RenderSettings.skybox = sky;
                underwaterSky = sky;
#if UNITY_VISIONOS && !UNITY_EDITOR
                OceanVisionOSDepthBackground.Attach(cam, sky);
#endif
                if (cam != null && !OceanEntry.IsLoading) cam.clearFlags = CameraClearFlags.Skybox;
                Debug.Log("[UnderwaterWorld] Gradient underwater skybox enabled");
            }
            else if (cam != null && !OceanEntry.IsLoading)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.035f, 0.27f, 0.34f, 1f);
                Debug.LogWarning("[UnderwaterWorld] UnderwaterSkybox shader missing; using solid fallback");
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;


            Light existing = FindFirstObjectByType<Light>();
            Light sun = existing != null ? existing : new GameObject("Filtered Sunlight").AddComponent<Light>();
            sun.name = "Filtered Sunlight";
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.96f, 0.86f);
            sun.intensity = 1.26f;
            sun.shadows = LightShadows.Soft;
            OceanReefShadowPolicy.ConfigureSun(sun);
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(.32f, -.92f, -.18f)); // same sun direction as the surface and water shafts
            underwaterSun = sun;
            RenderSettings.sun = sun;
        }

        // One lighting state follows the active map, including rollback. The first map
        // keeps its existing palette; the sanctuary separates warm foreground sunlight
        // from cool blue water instead of tinting every surface with the same yellow.
        void ApplyEnvironmentAtmosphere(OceanEnvironmentKind kind)
        {
            bool sanctuary = kind == OceanEnvironmentKind.CoralSanctuary;
            if (mainCamera != null) mainCamera.farClipPlane = sanctuary ? OceanSanctuaryVista.RecommendedFarClipPlane : 200f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = sanctuary ? .032f : .038f;
            RenderSettings.fogColor = sanctuary ? new Color(.025f, .205f, .32f) : new Color(.032f, .19f, .235f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sanctuary ? new Color(.27f, .43f, .54f) : new Color(.24f, .41f, .46f);
            RenderSettings.ambientEquatorColor = sanctuary ? new Color(.105f, .235f, .325f) : new Color(.12f, .25f, .29f);
            RenderSettings.ambientGroundColor = sanctuary ? new Color(.045f, .085f, .125f) : new Color(.055f, .095f, .11f);
            Vector3 sunRay = sanctuary ? new Vector3(.44f, -.84f, .24f) : new Vector3(.32f, -.92f, -.18f);
            if (underwaterSun != null)
            {
                underwaterSun.color = sanctuary ? new Color(1f, .945f, .85f) : new Color(1f, .96f, .86f);
                underwaterSun.intensity = sanctuary ? 1.38f : 1.26f;
                underwaterSun.transform.rotation = Quaternion.LookRotation(sunRay);
            }
            if (underwaterSky != null)
            {
                underwaterSky.SetColor("_TopColor", sanctuary ? new Color(.30f, .65f, .79f) : new Color(.30f, .66f, .68f));
                underwaterSky.SetColor("_HorizonColor", RenderSettings.fogColor);
                underwaterSky.SetColor("_BottomColor", OceanWaterOptics.Bottom(kind));
                underwaterSky.SetVector("_SunDirection", -sunRay.normalized);
                underwaterSky.SetFloat("_SurfaceDetail", sanctuary ? 1.6f : 1f);
            }
            if (sandMaterial != null)
            {
                sandMaterial.SetColor("_BaseColor", sanctuary ? new Color(.91f, 1f, 1.05f) : new Color(.80f, .82f, .74f));
                sandMaterial.SetFloat("_SanctuaryBlend", sanctuary ? 1f : 0f);
                sandMaterial.SetColor("_WaterBottomColor", OceanWaterOptics.Bottom(kind));
                sandMaterial.SetFloat("_CausticScale", sanctuary ? 3.4f : 1f);
                sandMaterial.SetFloat("_CausticStrength", sanctuary ? .14f : .22f);
                var contact = sanctuary && ActiveReefRoot != null
                    ? ActiveReefRoot.GetComponent<OceanSanctuaryResources>() : null;
                sandMaterial.SetTexture("_GroundOcclusion", contact != null && contact.GroundOcclusion != null
                    ? contact.GroundOcclusion : Texture2D.whiteTexture);
                sandMaterial.SetVector("_GroundOcclusionRect", contact != null
                    ? contact.GroundOcclusionRect : new Vector4(-50f, -50f, .01f, .01f));
            }
        }

        // ==================================================================
        // 2. MATERIALS
        // ==================================================================
        private void CreateMaterials()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            // --- Sand PBR ---
            sandMaterial = new Material(Shader.Find("SwanUnderwater/SeabedSand") ?? lit) { name = "SandPBR" };
            Texture2D sandColor = Resources.Load<Texture2D>(sandColorPath);
            Texture2D sandNormal = Resources.Load<Texture2D>(sandNormalPath);
            if (sandColor != null)
            {
                sandMaterial.SetTexture("_BaseMap", sandColor);
                sandMaterial.SetColor("_BaseColor", new Color(0.80f, 0.82f, 0.74f));
            }
            else sandMaterial.SetColor("_BaseColor", new Color(0.40f, 0.46f, 0.47f));
            if (sandNormal != null && sandMaterial.HasProperty("_BumpMap"))
            {
                sandMaterial.SetTexture("_BumpMap", sandNormal);
                sandMaterial.EnableKeyword("_NORMALMAP");
                sandMaterial.SetFloat("_BumpScale", 0.28f);
            }
            sandMaterial.SetFloat("_Metallic", 0f);
            sandMaterial.SetFloat("_Smoothness", 0.12f);

            // --- Rock PBR ---
            rockMaterial = new Material(lit) { name = "RockPBR" };
            Texture2D rockColor = Resources.Load<Texture2D>(rockColorPath);
            Texture2D rockNormal = Resources.Load<Texture2D>(rockNormalPath);
            if (rockColor != null) { rockMaterial.SetTexture("_BaseMap", rockColor); rockMaterial.SetColor("_BaseColor", new Color(0.45f, 0.42f, 0.40f)); }
            if (rockNormal != null && rockMaterial.HasProperty("_BumpMap"))
            {
                rockMaterial.SetTexture("_BumpMap", rockNormal);
                rockMaterial.EnableKeyword("_NORMALMAP");
                rockMaterial.SetFloat("_BumpScale", 1.0f);
            }
            rockMaterial.SetFloat("_Metallic", 0f);
            rockMaterial.SetFloat("_Smoothness", 0.3f);

            // --- Procedural bubble texture ---
            Texture2D bubbleTex = Resources.Load<Texture2D>("Bubbles/bubble_ring_glowCircle_512");
            if (bubbleTex == null) bubbleTex = GenerateBubbleTexture(256);
            Shader bubbleShader = Shader.Find("SwanUnderwater/BubbleParticle");
            if (bubbleShader != null)
            {
                bubbleMaterial = new Material(bubbleShader) { name = "BubbleMat" };
                bubbleMaterial.SetTexture("_BaseMap", bubbleTex);
                bubbleMaterial.SetColor("_BaseColor", new Color(0.8f, 0.95f, 1f, 0.7f));
                Debug.Log("[UnderwaterWorld] BubbleParticle shader found");
            }
            else
            {
                Shader unlit = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                bubbleMaterial = new Material(unlit) { name = "BubbleMat" };
                bubbleMaterial.SetTexture("_BaseMap", bubbleTex);
                bubbleMaterial.SetColor("_BaseColor", new Color(0.72f, 0.96f, 1f, 0.55f));
                Debug.LogWarning("[UnderwaterWorld] BubbleParticle shader not found, using fallback");
            }

            // --- Fish swim shader template ---
            Shader fishShader = Shader.Find("SwanUnderwater/FishSwim");
            if (fishShader != null)
            {
                fishSwimTemplate = new Material(fishShader) { name = "FishSwimTemplate" };
                Debug.Log("[UnderwaterWorld] FishSwim shader found");
            }
            else Debug.LogWarning("[UnderwaterWorld] FishSwim shader not found");

            // --- Dome material: prefer custom shader with Cull Front baked in (reliable) ---
            Texture2D gradTex = GenerateDomeGradient(4, 256);
            Shader domeShader = Shader.Find("SwanUnderwater/UnderwaterDome");
            if (domeShader != null)
            {
                domeMaterial = new Material(domeShader) { name = "DomeMat" };
                domeMaterial.SetTexture("_BaseMap", gradTex);
                Debug.Log("[UnderwaterWorld] Dome material: custom UnderwaterDome shader");
            }
            else
            {
                Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlitShader != null)
                {
                    domeMaterial = new Material(unlitShader) { name = "DomeMat" };
                    domeMaterial.SetTexture("_BaseMap", gradTex);
                    domeMaterial.SetFloat("_Surface", 0f);
                    domeMaterial.SetFloat("_Cull", 1f);
                    domeMaterial.renderQueue = 1000;
                    Debug.Log("[UnderwaterWorld] Dome material: URP Unlit fallback");
                }
            }
        }

        private static Texture2D GenerateBubbleTexture(int size)
        {
            // Ring-shaped bubble: transparent center, opaque edge, highlight
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float outerR = size * 0.46f;
            float innerR = size * 0.32f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    float normalized = dist / outerR;

                    // Ring alpha: transparent center, opaque edge
                    float ringAlpha = 0f;
                    if (normalized > 0.5f && normalized < 1f)
                    {
                        ringAlpha = Mathf.SmoothStep(0.5f, 0.75f, normalized) * Mathf.SmoothStep(1f, 0.85f, normalized);
                    }

                    // Highlight spot at upper-left
                    Vector2 hlCenter = center + new Vector2(-size * 0.14f, size * 0.16f);
                    float hlDist = Vector2.Distance(new Vector2(x, y), hlCenter) / (size * 0.12f);
                    float highlight = Mathf.SmoothStep(1f, 0f, hlDist) * 0.9f;

                    // Subtle rim light
                    float rim = Mathf.SmoothStep(0.9f, 0.98f, normalized) * (1f - Mathf.SmoothStep(0.98f, 1f, normalized));

                    float alpha = Mathf.Max(ringAlpha * 0.6f, highlight * 0.8f, rim * 0.5f);
                    Color c = new Color(0.75f + highlight, 0.95f + highlight * 0.3f, 1f, alpha);
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        private static Texture2D GenerateDomeGradient(int width, int height)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            // y=0 is bottom of sphere (dark), y=height is top (bright)
            Color bottom = new Color(0.005f, 0.04f, 0.08f);
            Color mid = new Color(0.02f, 0.16f, 0.22f);
            Color horizon = new Color(0.04f, 0.24f, 0.30f);
            Color top = new Color(0.06f, 0.18f, 0.22f);
            for (int y = 0; y < height; y++)
            {
                float t = y / (float)height;
                Color c;
                if (t < 0.25f) c = Color.Lerp(bottom, mid, t * 4f);
                else if (t < 0.6f) c = Color.Lerp(mid, horizon, (t - 0.25f) / 0.35f);
                else c = Color.Lerp(horizon, top, (t - 0.6f) / 0.4f);
                for (int x = 0; x < width; x++) tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        // ==================================================================
        // 3. TERRAIN HEIGHT SAMPLER
        // ==================================================================
        public static float SampleTerrainHeight(float wx, float wz) =>
            SampleTerrainHeight(wx, wz, RenderedSeabedEnvironment);

        public static float SampleTerrainHeight(float wx, float wz, OceanEnvironmentKind kind)
        {
            float classic = ClassicTerrainHeight(wx, wz);
            return kind == OceanEnvironmentKind.CoralSanctuary
                ? classic + OceanSanctuaryTerrain.Relief(wx, wz) : classic;
        }

        // Keep the original map's exact noise and arithmetic path. Sanctuary relief
        // is an additive fixed profile; it never moves/rebuilds with the swimmer.
        private static float ClassicTerrainHeight(float wx, float wz)
        {
            // Broad, soft natural dunes and shallow gullies. Earlier 1.5m/0.65m
            // high-frequency terms made 0.5-1m sharp triangular facets; keep them
            // tiny so they read as sand ripples instead of angular pyramids.
            float h1 = Mathf.PerlinNoise(wx * 0.014f, wz * 0.014f) * 4.2f;
            float h2 = Mathf.PerlinNoise(wx * 0.035f + 100f, wz * 0.035f + 100f) * 1.8f;
            float h3 = Mathf.PerlinNoise(wx * 0.11f + 200f, wz * 0.11f + 200f) * 0.45f;
            float h4 = Mathf.PerlinNoise(wx * 0.26f + 300f, wz * 0.26f + 300f) * 0.18f;
            float h5 = Mathf.PerlinNoise(wx * 0.72f + 500f, wz * 0.72f + 500f) * 0.12f;
            float h6 = Mathf.PerlinNoise(wx * 1.35f + 600f, wz * 1.35f + 600f) * 0.06f;
            float height = h1 + h2 + h3 + h4 + h5 + h6 - 3.05f;

            float distFromSpawn = new Vector2(wx, wz - SpawnZ).magnitude;
            // Flatten only the immediate spawn point (2m), let dunes form beyond
            if (distFromSpawn < 8f)
            {
                float flatten = Mathf.SmoothStep(0f, 1f, distFromSpawn / 8f);
                height = Mathf.Lerp(0f, height, flatten);
            }

            // Radial downward slope: terrain gently descends with distance,
            // so the horizon dips below the fog line instead of forming a wall.
            // Extended to 600m for 1600m terrain, deeper drop for abyssal feel.
            float slope = Mathf.Pow(Mathf.Clamp01(distFromSpawn / 600f), 1.5f) * 22f;
            height -= slope;

            return BaseTerrainY + height;
        }

        // ==================================================================
        // 4. FISH PREFAB LOADING
        // ==================================================================
        private void LoadFishPrefabs()
        {
            var selected = new List<GameObject>();
            foreach (string species in HeroSpecies)
            {
                GameObject prefab = Resources.Load<GameObject>(fishResourceFolder + "/" + species);
                if (prefab == null) throw new System.InvalidOperationException("Missing reef fish: " + species);
                OceanClarityPolicy.PrepareModelTextures(prefab);
                selected.Add(prefab);
            }
            fishPrefabGO = selected.ToArray();
            // HeroSpecies is the explicit marine allowlist shared by preloading and
            // population selection; directory-wide or duplicate name filters can silently
            // omit a valid species and turn the expanded roster into a startup failure.
            Debug.Log($"[UnderwaterWorld] Loaded {fishPrefabGO.Length} marine reef fish prefabs");
        }

        // ==================================================================
        // 5. SEABED TERRAIN
        // ==================================================================
        private void CreateSeabedTerrain()
        {
            // The far mesh is a ring, not an overlapping plane beneath the near
            // mesh: coarse triangles used to bridge valleys and cut through it.
            OceanEnvironmentKind kind = ConstructingEnvironmentKind;
            bool sanctuary = kind == OceanEnvironmentKind.CoralSanctuary;
            List<float> xs = sanctuary ? SanctuaryFarTerrainX : FarTerrainX;
            List<float> zs = sanctuary ? SanctuaryFarTerrainZ : FarTerrainZ;
            float hole = sanctuary ? OceanEnvironmentProfile.SanctuaryTerrainHalfExtent : 40f;
            int stride = xs.Count;
            var vertices = new Vector3[stride * zs.Count];
            var uv = new Vector2[vertices.Length];
            var triangles = new List<int>(128 * 128 * 6);
            for (int z = 0; z < zs.Count; z++)
                for (int x = 0; x < xs.Count; x++)
                {
                    int i = z * stride + x;
                    vertices[i] = new Vector3(xs[x], SampleTerrainHeight(xs[x], zs[z], kind) - 0.12f, zs[z]);
                    uv[i] = new Vector2(xs[x], zs[z]) * 0.15f;
                }
            for (int z = 0; z < zs.Count - 1; z++)
                for (int x = 0; x < xs.Count - 1; x++)
                {
                    if (xs[x] >= SpawnX - hole && xs[x + 1] <= SpawnX + hole &&
                        zs[z] >= SpawnZ - hole && zs[z + 1] <= SpawnZ + hole) continue;
                    int a = z * stride + x, b = a + 1, c = a + stride, d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            Mesh mesh = new Mesh { name = "SeabedTerrain", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.SetTriangles(triangles, 0);
            ApplySoftTerrainNormals(mesh);
            ApplySedimentColors(mesh);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            var terrain = new GameObject("SeabedTerrain");
            terrain.transform.SetParent(EnvironmentParent, false);
            terrain.AddComponent<MeshFilter>().mesh = mesh;
            terrain.AddComponent<MeshRenderer>().sharedMaterial = sandMaterial;
            if (sanctuary) CreateMediumSeabedRing();
        }

        private static List<float> TerrainAxis(float center, float halfExtent = 40f)
        {
            var values = new SortedSet<float>();
            for (int i = 0; i <= 128; i++) values.Add(-800f + i * 12.5f);
            values.Add(center - halfExtent);
            values.Add(center + halfExtent);
            return new List<float>(values);
        }

        private static readonly List<float> FarTerrainX = TerrainAxis(SpawnX);
        private static readonly List<float> FarTerrainZ = TerrainAxis(SpawnZ);
        private static readonly List<float> SanctuaryFarTerrainX = TerrainAxis(SpawnX, OceanEnvironmentProfile.SanctuaryTerrainHalfExtent);
        private static readonly List<float> SanctuaryFarTerrainZ = TerrainAxis(SpawnZ, OceanEnvironmentProfile.SanctuaryTerrainHalfExtent);
        // Include every far-edge vertex so even nonuniform 12.5m breakpoints are
        // shared at the medium perimeter; regular 2m samples alone leave tiny cracks.
        private static List<float> MediumTerrainAxis(float center, List<float> farAxis)
        {
            float half = OceanEnvironmentProfile.SanctuaryTerrainHalfExtent;
            float spacing = OceanEnvironmentProfile.SanctuaryMediumGridSpacing;
            int cells = Mathf.RoundToInt(half * 2f / spacing);
            var values = new SortedSet<float>();
            for (int i = 0; i <= cells; i++) values.Add(center - half + i * spacing);
            foreach (float value in farAxis)
                if (value >= center - half && value <= center + half) values.Add(value);
            values.Add(center - 40f); values.Add(center + 40f);
            return new List<float>(values);
        }
        private static readonly List<float> MediumTerrainX = MediumTerrainAxis(SpawnX, SanctuaryFarTerrainX);
        private static readonly List<float> MediumTerrainZ = MediumTerrainAxis(SpawnZ, SanctuaryFarTerrainZ);
        private enum SeabedBand { Near, Medium, Far }

        /// <summary>The visible triangle surface, including the fine/medium/far edge blends.
        /// Grounded props must use this instead of the unmeshed noise function.</summary>
        public static float SampleRenderedSeabedHeight(float x, float z) =>
            SampleRenderedSeabedHeight(x, z, RenderedSeabedEnvironment);

        public static float SampleRenderedSeabedHeight(float x, float z, OceanEnvironmentKind kind)
        {
            SeabedBand band = SeabedSegmentCell(x, z, kind, out float x0, out float x1, out float z0, out float z1);
            x = Mathf.Clamp(x, -800f, 800f); z = Mathf.Clamp(z, -800f, 800f);
            float a = SeabedVertexHeight(x0, z0, band, kind);
            float b = SeabedVertexHeight(x1, z0, band, kind);
            float c = SeabedVertexHeight(x0, z1, band, kind);
            float u = Mathf.InverseLerp(x0, x1, x), v = Mathf.InverseLerp(z0, z1, z);
            if (u + v <= 1f) return a * (1f - u - v) + b * u + c * v;
            float d = SeabedVertexHeight(x1, z1, band, kind);
            return b * (1f - v) + c * (1f - u) + d * (u + v - 1f);
        }

        private static float SeabedVertexHeight(float x, float z, SeabedBand band, OceanEnvironmentKind kind) =>
            band == SeabedBand.Near ? NearTerrainHeight(x, z, kind) :
            band == SeabedBand.Medium ? MediumTerrainHeight(x, z) : SampleTerrainHeight(x, z, kind) - .12f;

        private static void TerrainInterval(List<float> axis, float value, out float lower, out float upper)
        {
            int index = axis.BinarySearch(value);
            if (index < 0) index = ~index - 1;
            index = Mathf.Clamp(index, 0, axis.Count - 2);
            lower = axis[index]; upper = axis[index + 1];
        }

        private static float NearTerrainHeight(float x, float z, OceanEnvironmentKind kind)
        {
            float dx = 40f - Mathf.Abs(x - SpawnX), dz = 40f - Mathf.Abs(z - SpawnZ);
            float interior = SampleTerrainHeight(x, z, kind) + 0.035f;
            float distance = Mathf.Min(dx, dz);
            if (distance >= 4f) return interior;
            bool xEdge = dx <= dz;
            float edge = xEdge ? SpawnX + Mathf.Sign(x - SpawnX) * 40f : SpawnZ + Mathf.Sign(z - SpawnZ) * 40f;
            float along = xEdge ? z : x;
            float center = xEdge ? SpawnZ : SpawnX;
            if (kind == OceanEnvironmentKind.CoralSanctuary)
            {
                // The medium ring uses a fixed 2m grid aligned to the fine patch.
                // Every fine boundary vertex lies on the same adjacent medium edge.
                TerrainInterval(xEdge ? MediumTerrainZ : MediumTerrainX, along,
                    out float mediumLower, out float mediumUpper);
                float ma = xEdge ? MediumTerrainHeight(edge, mediumLower) : MediumTerrainHeight(mediumLower, edge);
                float mb = xEdge ? MediumTerrainHeight(edge, mediumUpper) : MediumTerrainHeight(mediumUpper, edge);
                float mediumBorder = Mathf.Lerp(ma, mb, Mathf.InverseLerp(mediumLower, mediumUpper, along));
                return Mathf.Lerp(mediumBorder, interior, Mathf.SmoothStep(0f, 1f, distance / 4f));
            }
            // Interpolate the exact adjacent coarse edge, including inserted corners.
            float lower = Mathf.Max(Mathf.Floor(along / 12.5f) * 12.5f, center - 40f);
            float upper = Mathf.Min(lower + 12.5f, center + 40f);
            if (lower == center - 40f) upper = Mathf.Min(Mathf.Ceil(lower / 12.5f) * 12.5f, center + 40f);
            float a = xEdge ? SampleTerrainHeight(edge, lower, kind) : SampleTerrainHeight(lower, edge, kind);
            float b = xEdge ? SampleTerrainHeight(edge, upper, kind) : SampleTerrainHeight(upper, edge, kind);
            float border = Mathf.Lerp(a, b, Mathf.InverseLerp(lower, upper, along)) - 0.12f;
            return Mathf.Lerp(border, interior, Mathf.SmoothStep(0f, 1f, distance / 4f));
        }

        private static float MediumTerrainHeight(float x, float z)
        {
            float half = OceanEnvironmentProfile.SanctuaryTerrainHalfExtent;
            float dx = half - Mathf.Abs(x - SpawnX), dz = half - Mathf.Abs(z - SpawnZ);
            float interior = SampleTerrainHeight(x, z, OceanEnvironmentKind.CoralSanctuary) + .035f;
            float distance = Mathf.Min(dx, dz);
            if (distance >= 8f) return interior;
            bool xEdge = dx <= dz;
            float edge = xEdge ? SpawnX + Mathf.Sign(x - SpawnX) * half : SpawnZ + Mathf.Sign(z - SpawnZ) * half;
            float along = xEdge ? z : x;
            TerrainInterval(xEdge ? SanctuaryFarTerrainZ : SanctuaryFarTerrainX, along, out float lower, out float upper);
            float a = xEdge ? SampleTerrainHeight(edge, lower, OceanEnvironmentKind.CoralSanctuary) :
                SampleTerrainHeight(lower, edge, OceanEnvironmentKind.CoralSanctuary);
            float b = xEdge ? SampleTerrainHeight(edge, upper, OceanEnvironmentKind.CoralSanctuary) :
                SampleTerrainHeight(upper, edge, OceanEnvironmentKind.CoralSanctuary);
            float border = Mathf.Lerp(a, b, Mathf.InverseLerp(lower, upper, along)) - .12f;
            return Mathf.Lerp(border, interior, Mathf.SmoothStep(0f, 1f, distance / 8f));
        }

        private void CreateMediumSeabedRing()
        {
            List<float> xs = MediumTerrainX, zs = MediumTerrainZ;
            int stride = xs.Count;
            var vertices = new Vector3[stride * zs.Count];
            var uv = new Vector2[vertices.Length];
            var triangles = new List<int>(xs.Count * zs.Count * 6);
            for (int z = 0; z < zs.Count; z++)
                for (int x = 0; x < xs.Count; x++)
                {
                    float wx = xs[x], wz = zs[z];
                    int index = z * stride + x;
                    vertices[index] = new Vector3(wx, MediumTerrainHeight(wx, wz), wz);
                    uv[index] = new Vector2(wx, wz) * .15f;
                }
            for (int z = 0; z < zs.Count - 1; z++)
                for (int x = 0; x < xs.Count - 1; x++)
                {
                    if (xs[x] >= SpawnX - 40f && xs[x + 1] <= SpawnX + 40f &&
                        zs[z] >= SpawnZ - 40f && zs[z + 1] <= SpawnZ + 40f) continue;
                    int a = z * stride + x, b = a + 1, c = a + stride, d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            var mesh = new Mesh { name = "Sanctuary Medium Seabed Ring", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices; mesh.uv = uv; mesh.SetTriangles(triangles, 0);
            ApplySoftTerrainNormals(mesh); ApplySedimentColors(mesh);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var terrain = new GameObject("Sanctuary Medium Seabed Ring");
            terrain.transform.SetParent(EnvironmentParent, false);
            terrain.AddComponent<OceanSanctuaryResources>().Own(mesh);
            terrain.AddComponent<MeshFilter>().mesh = mesh;
            var renderer = terrain.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = sandMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        /// <summary>
        /// Earliest segment contact with the rendered triangle height plus vertical clearance.
        /// Splits at every near/medium/far grid boundary and triangle diagonal, so a narrow peak
        /// cannot fall between fixed-distance samples. Outside the mesh, follows the same
        /// edge clamp as SampleRenderedSeabedHeight. Caller owns reusable scratch storage.
        /// </summary>
        public static bool TryIntersectRenderedSeabedSegment(Vector3 from, Vector3 to, float clearance,
            List<float> scratch, out float distance)
            => TryIntersectRenderedSeabedSegment(from, to, clearance, scratch, out distance, RenderedSeabedEnvironment);

        public static bool TryIntersectRenderedSeabedSegment(Vector3 from, Vector3 to, float clearance,
            List<float> scratch, out float distance, OceanEnvironmentKind kind)
        {
            distance = float.PositiveInfinity;
            if (scratch == null) return false;
            scratch.Clear();
            if (!SeabedSegmentFinite(from) || !SeabedSegmentFinite(to) ||
                float.IsNaN(clearance) || float.IsInfinity(clearance) || clearance < 0f) return false;
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (!SeabedSegmentFinite(delta) || float.IsNaN(length) || float.IsInfinity(length)) return false;
            const double contactTolerance = .00001; // 10 micrometres for float vertex arithmetic.
            double initialGap = (double)from.y - SampleRenderedSeabedHeight(from.x, from.z, kind) - clearance;
            if (initialGap <= contactTolerance) { distance = 0f; return true; }
            if (length == 0f) return false;

            scratch.Add(0f); scratch.Add(1f);
            bool sanctuary = kind == OceanEnvironmentKind.CoralSanctuary;
            foreach (float x in sanctuary ? SanctuaryFarTerrainX : FarTerrainX) AddSeabedGridCut(from.x, delta.x, x, scratch);
            foreach (float z in sanctuary ? SanctuaryFarTerrainZ : FarTerrainZ) AddSeabedGridCut(from.z, delta.z, z, scratch);
            for (int i = 0; i <= 160; i++)
            {
                AddSeabedGridCut(from.x, delta.x, SpawnX - 40f + i * .5f, scratch);
                AddSeabedGridCut(from.z, delta.z, SpawnZ - 40f + i * .5f, scratch);
            }
            if (sanctuary)
            {
                foreach (float x in MediumTerrainX) AddSeabedGridCut(from.x, delta.x, x, scratch);
                foreach (float z in MediumTerrainZ) AddSeabedGridCut(from.z, delta.z, z, scratch);
            }
            scratch.Sort();
            int gridCutCount = scratch.Count;
            for (int i = 1; i < gridCutCount; i++)
            {
                float ta = scratch[i - 1], tb = scratch[i];
                if (tb <= ta) continue;
                Vector3 mid = SeabedSegmentPoint(from, delta, (ta + tb) * .5f);
                SeabedSegmentCell(mid.x, mid.z, kind, out float x0, out float x1, out float z0, out float z1);
                Vector3 a = SeabedSegmentPoint(from, delta, ta), b = SeabedSegmentPoint(from, delta, tb);
                double da = Mathf.InverseLerp(x0, x1, a.x) + Mathf.InverseLerp(z0, z1, a.z) - 1.0;
                double db = Mathf.InverseLerp(x0, x1, b.x) + Mathf.InverseLerp(z0, z1, b.z) - 1.0;
                if ((da < 0.0 && db > 0.0) || (da > 0.0 && db < 0.0))
                {
                    float cut = (float)(ta + (tb - ta) * (-da / (db - da)));
                    if (cut > ta && cut < tb) scratch.Add(cut);
                }
            }
            scratch.Sort();
            float previousT = 0f;
            double previousGap = initialGap;
            for (int i = 1; i < scratch.Count; i++)
            {
                float t = scratch[i];
                if (t <= previousT) continue;
                Vector3 p = SeabedSegmentPoint(from, delta, t);
                double gap = (double)from.y + (double)delta.y * t - SampleRenderedSeabedHeight(p.x, p.z, kind) - clearance;
                if (gap <= contactTolerance)
                {
                    // Each open interval is in one planar triangle. Solve its linear
                    // signed-height gap, including a tangent contact at the next vertex.
                    double fraction = gap <= 0.0 ? previousGap / (previousGap - gap) : 1.0;
                    double hitT = previousT + (t - previousT) * fraction;
                    distance = Mathf.Clamp((float)(length * hitT), 0f, length);
                    return true;
                }
                previousT = t; previousGap = gap;
            }
            return false;
        }

        private static void AddSeabedGridCut(float from, float delta, float coordinate, List<float> scratch)
        {
            if (delta == 0f) return;
            double t = ((double)coordinate - from) / delta;
            if (t > 0.0 && t < 1.0) scratch.Add((float)t);
        }

        private static Vector3 SeabedSegmentPoint(Vector3 from, Vector3 delta, float t) => new Vector3(
            (float)((double)from.x + (double)delta.x * t),
            (float)((double)from.y + (double)delta.y * t),
            (float)((double)from.z + (double)delta.z * t));

        private static SeabedBand SeabedSegmentCell(float x, float z, OceanEnvironmentKind kind,
            out float x0, out float x1, out float z0, out float z1)
        {
            bool near = x >= SpawnX - 40f && x <= SpawnX + 40f && z >= SpawnZ - 40f && z <= SpawnZ + 40f;
            if (near)
            {
                x0 = SpawnX - 40f + Mathf.Clamp(Mathf.FloorToInt((x - SpawnX + 40f) * 2f), 0, 159) * .5f;
                z0 = SpawnZ - 40f + Mathf.Clamp(Mathf.FloorToInt((z - SpawnZ + 40f) * 2f), 0, 159) * .5f;
                x1 = x0 + .5f; z1 = z0 + .5f;
                return SeabedBand.Near;
            }
            bool sanctuary = kind == OceanEnvironmentKind.CoralSanctuary;
            float half = OceanEnvironmentProfile.SanctuaryTerrainHalfExtent;
            if (sanctuary && Mathf.Abs(x - SpawnX) <= half && Mathf.Abs(z - SpawnZ) <= half)
            {
                TerrainInterval(MediumTerrainX, x, out x0, out x1);
                TerrainInterval(MediumTerrainZ, z, out z0, out z1);
                return SeabedBand.Medium;
            }
            TerrainInterval(sanctuary ? SanctuaryFarTerrainX : FarTerrainX, Mathf.Clamp(x, -800f, 800f), out x0, out x1);
            TerrainInterval(sanctuary ? SanctuaryFarTerrainZ : FarTerrainZ, Mathf.Clamp(z, -800f, 800f), out z0, out z1);
            return SeabedBand.Far;
        }

        private static bool SeabedSegmentFinite(Vector3 p) =>
            !float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y) &&
            !float.IsNaN(p.z) && !float.IsInfinity(p.z);


        /// <summary>
        /// Area-weighted smooth normals for the seabed. RecalculateNormals assigns one
        /// normal per triangle on seams and amplifies angular dune silhouettes. This
        /// averages connected triangle normals by area for continuous, sandy slopes.
        /// </summary>
        private static void ApplySedimentColors(Mesh mesh)
        {
            // Compute broad sediment patches once, with the same phase on both grids.
            Vector3[] vertices = mesh.vertices;
            var colors = new Color[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                float broad = Mathf.PerlinNoise(p.x * 0.075f + 37.2f, p.z * 0.075f + 91.4f);
                float fine = Mathf.PerlinNoise(p.x * 0.23f + 13.5f, p.z * 0.23f + 54.8f);
                float sediment = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 0.68f, broad * 0.8f + fine * 0.2f));
                colors[i] = Color.Lerp(new Color(1f, 0.98f, 0.93f), new Color(0.57f, 0.65f, 0.62f), sediment);
            }
            mesh.colors = colors;
        }

        private static void ApplySoftTerrainNormals(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] tris = mesh.triangles;
            Vector3[] normals = new Vector3[vertices.Length];
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                float area = cross.magnitude;
                if (area < 0.000001f) continue;
                Vector3 n = cross / area;
                normals[a] += n * area;
                normals[b] += n * area;
                normals[c] += n * area;
            }
            for (int i = 0; i < normals.Length; i++)
                normals[i] = normals[i].sqrMagnitude > 0.000001f ? normals[i].normalized : Vector3.up;
            mesh.normals = normals;
        }

        /// <summary>
        /// High-resolution near-field seabed overlay. The 1600m base terrain uses
        /// 12.5m cells for mobile-VR cost, which looks faceted when the player is near
        /// the floor. This 80m patch uses 0.5m cells and the exact same height
        /// sampler, joined to the outer ring over its last four metres. It provides smooth dunes,
        /// gullies and convincing contact under the reef garden while the coarse
        /// mesh remains only as the fogged distant floor.
        /// </summary>
        private void CreateNearSeabedPatch()
        {
            const int gridSize = 160;
            const float worldSize = 80f;

            Vector3[] vertices = new Vector3[(gridSize + 1) * (gridSize + 1)];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[gridSize * gridSize * 6];

            for (int z = 0; z <= gridSize; z++)
            {
                for (int x = 0; x <= gridSize; x++)
                {
                    int idx = z * (gridSize + 1) + x;
                    float wx = SpawnX + (x / (float)gridSize - 0.5f) * worldSize;
                    float wz = SpawnZ + (z / (float)gridSize - 0.5f) * worldSize;
                    float y = NearTerrainHeight(wx, wz, ConstructingEnvironmentKind);
                    vertices[idx] = new Vector3(wx, y, wz);
                    // Same world-space texture phase and 6.67m period as the far floor.
                    uv[idx] = new Vector2(wx, wz) * 0.15f;
                }
            }

            int t = 0;
            for (int z = 0; z < gridSize; z++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    int v0 = z * (gridSize + 1) + x;
                    int v1 = v0 + 1;
                    int v2 = v0 + gridSize + 1;
                    int v3 = v2 + 1;
                    triangles[t++] = v0; triangles[t++] = v2; triangles[t++] = v1;
                    triangles[t++] = v1; triangles[t++] = v2; triangles[t++] = v3;
                }
            }

            Mesh mesh = new Mesh { name = "NearSeabedTerrain" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            ApplySoftTerrainNormals(mesh);
            ApplySedimentColors(mesh);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            GameObject terrain = new GameObject("Near Seabed Detail Patch");
            terrain.transform.SetParent(EnvironmentParent, false);
            terrain.AddComponent<MeshFilter>().mesh = mesh;
            MeshRenderer mr = terrain.AddComponent<MeshRenderer>();
            mr.sharedMaterial = sandMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            Debug.Log($"[Terrain] Near patch: {gridSize}x{gridSize}, {worldSize}m, {vertices.Length} verts, 0.5m cells");
        }

        // ==================================================================
        // 6. REEF CLUSTERS with proper normalization
        // ==================================================================
        private void CreateReefClusters()
        {
            Transform root = new GameObject("Reef Garden").transform;
            root.SetParent(EnvironmentParent, false);

            GameObject coralFormation = Resources.Load<GameObject>(envResourceFolder + "/coral_formation");
            GameObject seaAnemone = Resources.Load<GameObject>(envResourceFolder + "/sea_anemone");
            GameObject kelp = Resources.Load<GameObject>(envResourceFolder + "/kelp_seaweed");
            GameObject quaterniusRock = Resources.Load<GameObject>(envResourceFolder + "/quaternius_rock");
            GameObject[] rockObjs = Resources.LoadAll<GameObject>(rockResourceFolder);

            // New detailed colored coral GLBs
            GameObject[] newCorals = Resources.LoadAll<GameObject>("CoralColored");
            Debug.Log($"[Reef] Loaded {newCorals.Length} new coral GLB models from CoralColored/");

            // Pick cluster centers - closer to spawn for mid-ground visibility
            List<Vector3> centers = new List<Vector3>();
            // Deterministic 360-degree near/mid garden ring around the spawn. The
            // headset's initial yaw is not under our control, so a single +Z
            // corridor could be entirely behind the player. These anchors guarantee
            // dense colorful reef in whichever direction the first shot faces.
            for (int ring = 0; ring < 3; ring++)
            {
                float radius = ring == 0 ? 3.4f : (ring == 1 ? 6.6f : 10.2f);
                int count = ring == 0 ? 8 : (ring == 1 ? 10 : 12);
                float ringOffset = ring * 0.33f;
                for (int k = 0; k < count; k++)
                {
                    float a = (k / (float)count) * Mathf.PI * 2f + ringOffset;
                    float rr = radius * 0.92f + ((k * 37 + ring * 13) % 7) * 0.08f;
                    float ax = Mathf.Cos(a) * rr;
                    float az = Mathf.Sin(a) * rr;
                    centers.Add(new Vector3(SpawnX + ax, 0f, SpawnZ + az));
                }
            }

            int attempts = 0;
            while (centers.Count < reefClusterCount && attempts < 600)
            {
                attempts++;
                // Dense coral garden biased to the FORWARD view (+Z), so the first thing
                // the player sees is a reef garden instead of empty sand. Angle within
                // +/- ~75 deg of forward; a smaller share fills the sides.
                float angle;
                float dist;
                float r = Random.value;
                if (r < 0.75f)
                {
                    angle = Random.Range(0f, Mathf.PI * 2f); // all directions (garden is a 360 ring)
                    dist = Random.Range(1.8f, 11.5f);
                }
                else if (r < 0.95f)
                {
                    angle = Mathf.PI / 2f + (Random.value < 0.5f ? -1f : 1f) * Random.Range(1.3f, 1.85f); // sides
                    dist = Random.Range(2.2f, 10.5f);
                }
                else
                {
                    angle = Random.Range(0f, Mathf.PI * 2f); // a few behind
                    dist = Random.Range(6f, 18f);
                }
                float cx = SpawnX + Mathf.Cos(angle) * dist;
                float cz = SpawnZ + Mathf.Sin(angle) * dist;
                // Soft spacing - allow natural overlap
                bool tooClose = false;
                foreach (var existing in centers)
                {
                    if (Vector2.Distance(new Vector2(cx, cz), new Vector2(existing.x, existing.z)) < 1.45f)
                    { tooClose = true; break; }
                }
                if (!tooClose) centers.Add(new Vector3(cx, 0, cz));
            }

            int total = 0;
            foreach (Vector3 center in centers)
            {
                // Dense clusters: 9-20 instances per cluster, tighter spread.
                int clusterSize = Random.Range(12, 24);
                // Closer clusters are bigger/more detailed
                float distFromCenter = new Vector2(center.x, center.z - SpawnZ).magnitude;
                float distScale = Mathf.Clamp01(1f - (distFromCenter - 15f) / 25f); // 1.0 near, 0.0 far
                for (int i = 0; i < clusterSize && total < 850; i++)
                {
                    // Tight spread so each cluster is a dense garden patch, not a line.
                    float spread = Random.Range(0.55f, 1.8f);
                    float px = center.x + Random.Range(-spread, spread);
                    float pz = center.z + Random.Range(-spread, spread);
                    float terrainY = SampleTerrainHeight(px, pz);

                    GameObject prefab = null;
                    float targetH = 1f;
                    int pick = Random.Range(0, 100);

                    // 84% new colored GLBs (branching / brain / fan / sea-grass reef kits),
                    // 8% low dark reef base, 5% tall kelp, 3% anemone. The lower weights
                    // stop the old single kelp model from forming two green walls.
                    if (pick < 84 && newCorals.Length > 0)
                    {
                        prefab = newCorals[Random.Range(0, newCorals.Length)];
                        // Larger foreground gardens; varied clump height, still human-scale.
                        targetH = Random.Range(0.75f, 1.85f) * (0.9f + distScale * 0.55f);
                    }
                    else if (pick < 92 && (quaterniusRock != null || rockObjs.Length > 0))
                    {
                        prefab = quaterniusRock != null ? quaterniusRock : rockObjs[Random.Range(0, rockObjs.Length)];
                        targetH = Random.Range(0.35f, 1.15f) * (0.85f + distScale * 0.4f);
                    }
                    else if (pick < 97 && kelp != null)
                    {
                        prefab = kelp;
                        targetH = Random.Range(0.75f, 1.55f);
                    }
                    else if (seaAnemone != null) { prefab = seaAnemone; targetH = Random.Range(0.35f, 0.75f); }
                    else if (coralFormation != null) { prefab = coralFormation; targetH = Random.Range(0.55f, 1.25f); }
                    else if (rockObjs.Length > 0) { prefab = rockObjs[Random.Range(0, rockObjs.Length)]; targetH = Random.Range(0.4f, 1.0f); }

                    if (prefab != null)
                    {
                        GameObject inst = Instantiate(prefab, new Vector3(px, terrainY, pz), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), root);
                        if (NormalizeAndPlace(inst, targetH, terrainY))
                        {
                            // Debug: log renderer/material info for new GLB corals
                            if (newCorals.Length > 0 && prefab == newCorals[0] || (newCorals.Length > 0 && System.Array.IndexOf(newCorals, prefab) >= 0))
                            {
                                var dbgRends = inst.GetComponentsInChildren<Renderer>();
                                foreach (var dr in dbgRends)
                                {
                                    var mats = dr.sharedMaterials;
                                    foreach (var dm in mats)
                                    {
                                        string texName = dm != null && dm.HasProperty("_BaseMap") && dm.GetTexture("_BaseMap") != null
                                            ? dm.GetTexture("_BaseMap").name : "NONE";
                                        Debug.Log($"[CoralTex] {prefab.name} renderer={dr.name} mat={dm?.shader?.name} tex={texName}");
                                    }
                                }
                            }
                            bool isNewColoredCoral = newCorals.Length > 0 && System.Array.IndexOf(newCorals, prefab) >= 0;
                            // Only apply rock material fallback to old models, never touch new GLB materials
                            if (!isNewColoredCoral && prefab != null && (prefab == coralFormation || prefab == seaAnemone || prefab == kelp || prefab == quaterniusRock || rockObjs.Length == 0 || System.Array.IndexOf(rockObjs, prefab) >= 0))
                                ApplyRockMaterialIfNeeded(inst);
                            // IMPORTANT: do NOT tint the new colored GLB corals. They ship with
                            // their own painted branching/brain-coral albedo; multiplying it by a
                            // flat saturated color erased the texture and made them look like balls.
                            // Only the old plain models get a subtle color variant.
                            if (!isNewColoredCoral)
                                ApplyCoralColorVariant(inst);
                            DestroyChildColliders(inst);
                            total++;
                        }
                        else Destroy(inst);
                    }
                }
            }
            Debug.Log($"[UnderwaterWorld] Created {total} reef instances across {centers.Count} clusters");
        }

        /// <summary>
        /// Guaranteed tall, colorful branching coral ring at 1.6-9m so the opening
        /// view reads as an upright reef garden (corals rise into the lower-middle of
        /// the frame) instead of a low colorful ribbon at the feet. Uses only the
        /// MiniPoly colored GLBs; the random reef clusters still fill the rest.
        /// </summary>
        private void CreateTallForegroundReef()
        {
            GameObject[] corals = Resources.LoadAll<GameObject>("CoralColored");
            if (corals.Length == 0)
            {
                Debug.LogWarning("[Reef] No colored coral GLBs for foreground garden");
                return;
            }
            Transform root = GameObject.Find("Reef Garden")?.transform;
            if (root == null) root = transform;

            System.Random rng = new System.Random(7331);
            int placed = 0;
            // inner ring (tall, close), mid ring (medium)
            for (int ring = 0; ring < 2; ring++)
            {
                int count = ring == 0 ? 26 : 22;
                for (int k = 0; k < count; k++)
                {
                    float a = (k / (float)count) * Mathf.PI * 2f + (ring * 0.21f);
                    float radius = ring == 0
                        ? 1.9f + (float)(rng.NextDouble() * 1.9)   // 1.9-3.8m
                        : 4.4f + (float)(rng.NextDouble() * 4.2);   // 4.4-8.6m
                    float px = SpawnX + Mathf.Cos(a) * radius;
                    float pz = SpawnZ + Mathf.Sin(a) * radius;
                    float ty = ring == 0
                        ? 1.4f + (float)(rng.NextDouble() * 1.1)   // 1.4-2.5m tall close
                        : 1.0f + (float)(rng.NextDouble() * 0.9);  // 1.0-1.9m mid
                    GameObject prefab = corals[rng.Next(corals.Length)];
                    float terrainY = SampleTerrainHeight(px, pz);
                    GameObject inst = Instantiate(prefab, new Vector3(px, terrainY, pz),
                        Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f), root);
                    if (NormalizeAndPlace(inst, ty, terrainY))
                    {
                        DestroyChildColliders(inst);
                        placed++;
                    }
                    else Destroy(inst);
                }
            }
            Debug.Log($"[Reef] Tall foreground garden placed={placed} colored corals");
        }

        /// <summary>
        /// Normalize instance to target height and snap bottom to terrainY.
        /// Returns false if bounds are invalid (instance should be destroyed).
        /// </summary>
        private static bool NormalizeAndPlace(GameObject inst, float targetHeight, float terrainY)
        {
            // MiniPoly GLBs encode the 0.01m->1m unit conversion as a legitimate
            // 100x scale on every mesh node; resetting that collapsed the multi-piece
            // coral kit to tiny fragments. Keep the genuine 100x. Only neutralize a
            // genuinely pathological extra scale (the earlier ~9800x grey quad came
            // from that compounding). Compute effective scale on each renderer root.
            Renderer[] rends = inst.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return false;

            // Use world bounds after resetting child scales
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            if (b.size.y < 0.005f || b.size.magnitude > 50f) return false;

            // Normalize by MAX dimension to target height
            float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (maxDim < 0.01f) return false;
            float scaleFactor = Mathf.Min(targetHeight / b.size.y, 4f / maxDim);
            inst.transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);

            // Recalculate world bounds after scaling
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            // Align bottom to terrain
            float yOffset = terrainY - b.min.y;
            inst.transform.position += Vector3.up * yOffset;

            return true;
        }

        private void ApplyRockMaterialIfNeeded(GameObject go)
        {
            Renderer[] rends = go.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in rends)
            {
                Material m = r.sharedMaterial;
                // Only override if material is missing, error shader, or has no texture
                if (m == null) { r.sharedMaterial = rockMaterial; continue; }
                string shaderName = m.shader != null ? m.shader.name : "";
                if (shaderName.Contains("InternalError") || shaderName == "Standard")
                {
                    // Check if it has a valid base texture
                    if (!m.HasProperty("_BaseMap") || m.GetTexture("_BaseMap") == null)
                        r.sharedMaterial = rockMaterial;
                }
            }
        }

        private static void DestroyChildColliders(GameObject go)
        {
            Collider[] cols = go.GetComponentsInChildren<Collider>();
            foreach (Collider c in cols) Destroy(c);
        }

        /// <summary>
        /// Apply a random warm color tint to coral/anemone instances via MaterialPropertyBlock.
        /// Adds visual variety without increasing draw calls.
        /// </summary>
        private static void ApplyCoralColorVariant(GameObject go)
        {
            Renderer[] rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;

            // Palette: pink, orange, purple, green, red
            Color[] palette = new Color[]
            {
                new Color(1.0f, 0.3f, 0.5f),    // hot pink
                new Color(1.0f, 0.5f, 0.2f),    // bright orange
                new Color(0.6f, 0.2f, 0.8f),    // vivid purple
                new Color(0.3f, 0.8f, 0.4f),    // neon green
                new Color(1.0f, 0.2f, 0.2f),    // bright red
                new Color(1.0f, 0.85f, 0.2f),   // golden yellow
                new Color(0.9f, 0.4f, 0.7f),    // magenta
                new Color(0.5f, 0.9f, 0.8f),    // aqua
            };
            Color tint = palette[Random.Range(0, palette.Length)];

            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            foreach (Renderer r in rends)
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", tint);
                r.SetPropertyBlock(mpb);
            }
        }

        // ==================================================================
        // 7. UNDERWATER DOME
        // ==================================================================
        private void CreateUnderwaterDome()
        {
            // Use Unity's procedural skybox as the guaranteed OpenGLES3 background.
            // The previous custom inverted-dome shader rendered black on Swan.
            // The extended seabed skirt and distance fog hide the terrain boundary,
            // while a skybox can never expose a mesh seam or camera escape edge.
            Debug.Log("[UnderwaterWorld] Using procedural skybox + fog for seamless distance");
        }

        // ==================================================================
        // 8. BUBBLE VENTS (fixed parent + terrain Y)
        // ==================================================================
        private void CreateBubbleVents()
        {
            Transform root = new GameObject("Bubble Vents").transform;
            root.SetParent(EnvironmentParent, false);  // FIX: was root.SetParent(root, false)

            var sources = ReefHabitat.BubbleSources;
            for (int i = 0; i < sources.Count; i++)
            {
                GameObject vent = new GameObject("Bubble Vent");
                vent.transform.SetParent(root, false);
                vent.transform.position = sources[i];

                ParticleSystem ps = vent.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.014f, 0.052f);
                main.startColor = new Color(0.80f, 0.95f, 1f, 0.86f);
                main.maxParticles = 32;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = -0.016f;

                var emission = ps.emission;
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(1.0f, 3.2f);
                emission.SetBursts(new[] {new ParticleSystem.Burst(i % 4, (short)3, (short)7, 0, 4.5f)});
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.025f;
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.055f;
                noise.frequency = 1.2f;
                noise.scrollSpeed = 0.2f;
                var sizeLife = ps.sizeOverLifetime;
                sizeLife.enabled = true;
                sizeLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f));
                var colorLife = ps.colorOverLifetime;
                colorLife.enabled = true;
                Gradient alphaGrad = new Gradient();
                alphaGrad.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.85f, 0.65f), new GradientAlphaKey(0f, 1f) }
                );
                colorLife.color = alphaGrad;
                var velLife = ps.velocityOverLifetime;
                velLife.enabled = true;
                velLife.x = 0f;
                velLife.y = 0.22f;
                velLife.x = 0.025f;
                velLife.space = ParticleSystemSimulationSpace.World;
                velLife.z = 0f;
                ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = bubbleMaterial;
            }
        }

        // ==================================================================
        // 8b. WATER SURFACE GLOW PLANE
        // ==================================================================
        private Transform waterSurfacePlane;
        private void CreateWaterSurfacePlane()
        {
            // Large glowing quad 8m above player, simulating water surface from below
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plane.name = "WaterSurfaceGlow";
            plane.transform.SetParent(transform, false);
            plane.transform.position = new Vector3(SpawnX, BaseTerrainY + 8f, SpawnZ);
            plane.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // face downward
            plane.transform.localScale = new Vector3(200f, 200f, 1f);

            // Use opaque bright unlit material (reliable on OpenGLES3)
            Shader unlitSh = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitSh == null) unlitSh = Shader.Find("Unlit/Color");
            Material glowMat = new Material(unlitSh);
            glowMat.color = new Color(0.4f, 0.85f, 0.9f, 1.0f);
            glowMat.renderQueue = 2000;

            Renderer r = plane.GetComponent<Renderer>();
            r.material = glowMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            // Remove collider
            Collider col = plane.GetComponent<Collider>();
            if (col != null) Destroy(col);

            waterSurfacePlane = plane.transform;
            Debug.Log("[UnderwaterWorld] Water surface glow plane created at Y=" + (BaseTerrainY + 8f));
        }

        // ==================================================================
        // 9. FISH SCHOOL
        // ==================================================================
        private System.Collections.IEnumerator CreateFishSchool()
        {
            fishRoot = new GameObject("Free Swimming Fish").transform;
            fishRoot.SetParent(EnvironmentParent, false);
            PrepareSanctuaryLife();

            bool useRealModels = fishPrefabGO != null && fishPrefabGO.Length > 0;

            // The imported directory also contains freshwater fish. Pick an explicit
            // marine roster, with habitat variety guaranteed in every exploration zone.
            var kind = BuildingSanctuary ? OceanEnvironmentKind.CoralSanctuary : OceanEnvironmentKind.Classic;
            int population = OceanResidentDensityPolicy.HeroCount(kind, fishCount);
            for (int i = 0; i < population; i++)
            {
                if (useRealModels)
                {
                    string name = OceanResidentDensityPolicy.HeroResourceName(i, kind);
                    GameObject prefab = System.Array.Find(fishPrefabGO, candidate => candidate.name == name);
                    if (prefab == null) throw new System.InvalidOperationException("Marine population resource missing: " + name);
                    SpawnHeroFish(prefab, i, ref useRealModels);
                }
                else
                {
                    var fishObject = CreateProceduralFish(Random.ColorHSV(), Random.Range(0.32f, 0.82f));
                    fishObject.transform.SetParent(fishRoot, false);
                    fishObject.transform.localPosition = new Vector3(Random.Range(-15f, 15f), Random.Range(-2f, 6f), Random.Range(-15f, 15f));
                    FishAgent agent = new FishAgent(fishObject.transform, Random.Range(0.65f, 1.6f), Random.insideUnitSphere.normalized, false);
                    fishAgents.Add(agent);
                }
                if (i % 12 == 11) yield return null;
            }
            Debug.Log($"[FishSchool] {fishAgents.Count} reef/mid hero fish; {OceanResidentDensityPolicy.MarineHeroSpecies} marine categories distributed by habitat");
        }

        /// <summary>Extra campaign animals share the real reef-fish art/swim materials, but are not added to the ambient simulation.</summary>
        public GameObject CreateCampaignCreature(string resourcePath, Transform parent, float size)
        {
            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null) throw new System.InvalidOperationException("Missing campaign creature: " + resourcePath);
            OceanClarityPolicy.PrepareModelTextures(prefab);
            Random.State randomState = Random.state;
            GameObject model = null;
            try
            {
                model = Instantiate(prefab, parent, false);
                AlignSpeciesModel(model, prefab.name);
                NormalizeFishScale(model, size);
                ApplyFishSwimShader(model, prefab.name);
                return model;
            }
            catch { if (model != null) Destroy(model); throw; }
            finally { Random.state = randomState; }
        }

        /// <summary>Exact hero-prefab matches only; callers cache this once per campaign level. Schooling boids are unaffected.</summary>
        public void GetCampaignHeroFish(string resourcePath, List<GameObject> result)
        {
            result.Clear();
            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null || fishRoot == null) return;
            string prefix = "Fish_" + prefab.name + "_";
            foreach (Transform model in fishRoot)
                if (model.name.StartsWith(prefix, System.StringComparison.Ordinal) &&
                    int.TryParse(model.name.Substring(prefix.Length), out _)) result.Add(model.gameObject);
        }

        // Campaign sharks keep these exact visuals; only one system may steer them at a time.
        readonly HashSet<Transform> campaignSharks = new HashSet<Transform>();
        public void GetCampaignSharks(List<GameObject> result)
        {
            result.Clear();
            foreach (var animal in largeCreatures)
                if (animal.isShark && animal.transform != null && animal.transform.gameObject.activeSelf && !campaignSharks.Contains(animal.transform))
                    result.Add(animal.transform.gameObject);
        }
        public void SetCampaignSharkControlled(GameObject model, bool controlled)
        {
            if (model == null) return;
            if (controlled) { campaignSharks.Add(model.transform); return; }
            campaignSharks.Remove(model.transform);
            foreach (var animal in largeCreatures)
                if (animal.transform == model.transform)
                {
                    EnsureAmbientSharkWaterStart(animal);
                    animal.direction = model.transform.forward;
                    animal.currentSpeed = 0f; animal.passStage = 0; animal.passTimer = 8f;
                    animal.sharkRouteTimer = 0f; animal.sharkRouteValid = false;
                    Vector3 offset = LargeBodyCenter(animal) - animal.patrolCenter;
                    animal.orbitAngle = Mathf.Atan2(offset.z, offset.x);
                    break;
                }
        }

        private void SpawnHeroFish(GameObject prefab, int index, ref bool useRealModels)
        {
            GameObject fishObject = Instantiate(prefab, fishRoot, false);
            string fishName = prefab.name;
            fishObject.name = "Fish_" + prefab.name + "_" + index;

            // Realistic per-species target length (metres). Large species are
            // handled by the dedicated orbit system; keep hero copies modest.
            float targetLength = OceanResidentDensityPolicy.HeroLength(fishName, index,
                SpeciesTargetLength(fishName, Random.Range(0.35f, 0.6f)));
            NormalizeFishScale(fishObject, targetLength);
            fishObject.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            ApplyFishSwimShader(fishObject, prefab.name);

            // Place by water layer relative to the seabed under the spawn point,
            // so fish cruise above the floor instead of floating up near the dome.
            float px = SpawnX + Random.Range(-12f, 12f);
            float pz = SpawnZ + Random.Range(-12f, 12f);
            float floorY = SampleTerrainHeight(px, pz);
            float clearance = Mathf.Max(0.6f, targetLength * 1.4f);
            float y;
            if (fishName.Contains("flounder") || fishName.Contains("moray") || fishName.Contains("angler"))
                y = floorY + clearance * 0.55f;                 // benthic
            else if (fishName.Contains("seahorse"))
                y = floorY + clearance;                         // just above bottom
            else if (fishName.Contains("tuna") || fishName.Contains("swordfish") || fishName.Contains("salmon") || fishName.Contains("trout") || fishName.Contains("bass"))
                y = Mathf.Clamp(floorY + Random.Range(3.0f, 5.5f), -0.5f, 3.2f); // mid/open water
            else
                y = Mathf.Clamp(floorY + Random.Range(1.2f, 3.2f), -1.6f, 3.0f); // reef / mid-water

            int pocketIndex = -1;
            bool openWater = fishName.Contains("salmon") || fishName.Contains("trout") || fishName.Contains("bass");
            if (!openWater && targetLength <= 0.65f && index % 4 != 3 && ReefHabitat.Pockets.Count > 0)
            {
                // Distributed residents use different corals; open-water species retain their layer.
                pocketIndex = (index * 37 + 5) % ReefHabitat.Pockets.Count;
                Vector3 reefPosition = CoralTarget(pocketIndex, index * 2.39996f, false, targetLength * 0.35f);
                px = reefPosition.x; y = reefPosition.y; pz = reefPosition.z;
            }
            PlaceSanctuaryHero(index, targetLength*.35f, ref px, ref y, ref pz, ref pocketIndex, fishName);
            fishObject.transform.position = new Vector3(px, y, pz);
            bool slow = fishName.Contains("seahorse") || fishName.Contains("puffer") || fishName.Contains("moray") || fishName.Contains("flounder") || fishName.Contains("angler");
            FishAgent agent = new FishAgent(fishObject.transform, Random.Range(0.2f, 0.65f), Random.insideUnitSphere.normalized,
                slow, pocketIndex, targetLength * 0.35f);
            fishAgents.Add(agent);
        }

        private static Vector3 CoralTarget(int pocketIndex, float phase, bool shelter, float clearance)
        {
            var pocket = ReefHabitat.Pockets[pocketIndex];
            Vector3 point = shelter ? pocket.shelter : pocket.center;
            float radius = shelter ? Mathf.Min(pocket.radius * 0.28f, 0.18f) : pocket.radius * 1.35f;
            point += new Vector3(Mathf.Cos(phase) * radius,
                shelter ? 0.04f : 0.16f + Mathf.Sin(phase * 1.7f) * 0.12f, Mathf.Sin(phase) * radius);
            // A brain coral's refuge is above its solid crown. Branching refuges lie between branches.
            if (pocket.shelter.y > pocket.center.y)
                point.y = Mathf.Max(point.y, pocket.shelter.y + clearance);
            point.y = Mathf.Max(point.y, SampleTerrainHeight(point.x, point.z) + Mathf.Max(0.15f, clearance));
            return point;
        }

        private void LogReefLife()
        {
            int residents = 0, tinyResidents = 0, sheltered = 0, visits = 0, responses = 0, passes = 0;
            foreach (var fish in fishAgents)
            {
                if (fish.IsReefResident) residents++;
                if (fish.IsSheltered) sheltered++;
                visits += fish.ShelterEntries;
                responses += fish.HandShelterResponses;
            }
            foreach (var fish in boids)
            {
                if (fish.pocketIndex >= 0) tinyResidents++;
                if (fish.inShelter) sheltered++;
                visits += fish.shelterEntries;
                responses += fish.handShelterResponses;
            }
            float nearestShark = float.PositiveInfinity;
            Vector3 body = PlayerBodyCenter();
            foreach (var fish in largeCreatures)
            {
                passes += fish.completedPasses;
                if (fish.isShark) nearestShark = Mathf.Min(nearestShark, Vector3.Distance(LargeBodyCenter(fish), body));
            }
            Debug.Log($"[ReefLife] heroResidents={residents}, schoolingResidents={tinyResidents}, pockets={ReefHabitat.Pockets.Count}, " +
                $"sheltered={sheltered}, shelterEntries={visits}, handRefuges={responses}, sharkPasses={passes}, " +
                $"nearestSharkCenter={nearestShark:F2}m, closestSharkCenterEver={closestSharkDistanceEver:F2}m");
        }

        private void ApplyFishSwimShader(GameObject fishObject, string prefabName)
        {
            if (fishSwimTemplate == null) return;
            AlignSpeciesModel(fishObject, prefabName);
            Renderer[] rends = fishObject.GetComponentsInChildren<Renderer>();
            Debug.Log($"[FishMat] {prefabName}: {rends.Length} renderers");
            foreach (Renderer r in rends)
            {
                Material[] mats = r.sharedMaterials;
                float phase = 0f;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material src = mats[m];
                    string key = prefabName + ":" + (src != null ? src.GetInstanceID().ToString() : "none");
                    if (heroMaterialCache.TryGetValue(key, out Material shared))
                    {
                        // Preserve the seeded layout's random sequence from SetSpeciesParams.
                        Random.Range(0.8f, 1.2f);
                        Random.Range(0.85f, 1.15f);
                        phase = Random.Range(0f, Mathf.PI * 2f);
                        mats[m] = shared;
                        continue;
                    }
                    Material newMat = new Material(fishSwimTemplate) { enableInstancing = true };
                    if (src != null)
                    {
                        // glTFast uses non-standard property names (baseColorTexture, normalTexture, etc.)
                        // Map them to standard URP names used by FishSwim shader
                        CopyTextureProperty(src, newMat, "baseColorTexture", "_BaseMap");
                        CopyTextureProperty(src, newMat, "_BaseMap", "_BaseMap");
                        CopyTextureProperty(src, newMat, "_MainTex", "_BaseMap");
                        CopyTextureProperty(src, newMat, "normalTexture", "_BumpMap");
                        CopyTextureProperty(src, newMat, "_BumpMap", "_BumpMap");
                        CopyTextureProperty(src, newMat, "_NormalMap", "_BumpMap");
                        CopyTextureProperty(src, newMat, "metallicRoughnessTexture", "_MetallicGlossMap");
                        CopyTextureProperty(src, newMat, "_MetallicGlossMap", "_MetallicGlossMap");
                        CopyTextureProperty(src, newMat, "occlusionTexture", "_OcclusionMap");
                        CopyTextureProperty(src, newMat, "_OcclusionMap", "_OcclusionMap");
                        CopyTextureProperty(src, newMat, "emissiveTexture", "_EmissionMap");
                        CopyTextureProperty(src, newMat, "_EmissionMap", "_EmissionMap");
                        // Colors and factors
                        CopyColorProperty(src, newMat, "baseColorFactor", "_BaseColor");
                        CopyColorProperty(src, newMat, "_BaseColor", "_BaseColor");
                        CopyColorProperty(src, newMat, "_Color", "_BaseColor");
                        CopyColorProperty(src, newMat, "emissiveFactor", "_EmissionColor");
                        CopyColorProperty(src, newMat, "_EmissionColor", "_EmissionColor");
                        CopyFloatProperty(src, newMat, "metallicFactor", "_Metallic");
                        CopyFloatProperty(src, newMat, "_Metallic", "_Metallic");
                        CopyFloatProperty(src, newMat, "_Smoothness", "_Smoothness");
                        CopyFloatProperty(src, newMat, "normalScale", "_BumpScale");
                        CopyFloatProperty(src, newMat, "_BumpScale", "_BumpScale");
                        CopyFloatProperty(src, newMat, "occlusionStrength", "_OcclusionStrength");
                        CopyFloatProperty(src, newMat, "_OcclusionStrength", "_OcclusionStrength");
                    }
                    SetSpeciesParams(newMat, prefabName);
                    phase = newMat.GetFloat("_SwingPhase");
                    heroMaterialCache.Add(key, newMat);
                    mats[m] = newMat;
                }
                r.sharedMaterials = mats;
                OceanFishVisualProfile.ExpandRendererBounds(r);
                var properties = new MaterialPropertyBlock();
                properties.SetFloat("_SwingPhase", phase);
                r.SetPropertyBlock(properties);
            }
        }

        private static void AlignSpeciesModel(GameObject fish, string species)
        {
            string name = species.ToLowerInvariant();
            float yaw = name.Contains("great_white") ? OceanFishVisualProfile.GreatWhiteMeshYawCorrection
                : name.Contains("manta") || name.Contains("flounder") ? 90f : 0f;
            if (yaw == 0f || fish.transform.Find("Species mesh alignment") != null) return;
            var children = new List<Transform>();
            foreach (Transform child in fish.transform) children.Add(child);
            var pivot = new GameObject("Species mesh alignment").transform;
            pivot.SetParent(fish.transform, false);
            foreach (Transform child in children) child.SetParent(pivot, false);
            // Imported great-white vertices have a diagonal (+X,+Z) nose after glTFast's
            // handedness conversion. Movement owns +Z; only the visual needs this correction.
            pivot.localRotation = Quaternion.Euler(0, yaw, 0);
        }

        private static void CopyTextureProperty(Material src, Material dst, string srcProp, string dstProp)
        {
            if (src.HasProperty(srcProp) && dst.HasProperty(dstProp))
            {
                Texture tex = src.GetTexture(srcProp);
                if (tex != null) { OceanClarityPolicy.PrepareTexture(tex); dst.SetTexture(dstProp, tex); }
            }
        }

        private static void CopyTextureProperty(Material src, Material dst, string prop)
        {
            CopyTextureProperty(src, dst, prop, prop);
        }

        private static void CopyColorProperty(Material src, Material dst, string srcProp, string dstProp)
        {
            if (src.HasProperty(srcProp) && dst.HasProperty(dstProp))
                dst.SetColor(dstProp, src.GetColor(srcProp));
        }

        private static void CopyColorProperty(Material src, Material dst, string prop)
        {
            CopyColorProperty(src, dst, prop, prop);
        }

        private static void CopyFloatProperty(Material src, Material dst, string srcProp, string dstProp)
        {
            if (src.HasProperty(srcProp) && dst.HasProperty(dstProp))
                dst.SetFloat(dstProp, src.GetFloat(srcProp));
        }

        private static void CopyFloatProperty(Material src, Material dst, string prop)
        {
            CopyFloatProperty(src, dst, prop, prop);
        }

        private void SetSpeciesParams(Material mat, string name)
        {
            float amp = 0.18f, freq = 4.0f, halfLen = 0.35f, bob = 0.025f;
            string n = name.ToLower();
            if (n.Contains("moray")) { amp = 0.35f; freq = 2.5f; halfLen = 0.5f; }
            else if (n.Contains("seahorse")) { amp = 0.06f; freq = 7f; halfLen = 0.2f; bob = 0.05f; }
            else if (n.Contains("puffer")) { amp = 0.08f; freq = 2f; halfLen = 0.3f; }
            else if (n.Contains("lionfish")) { amp = 0.22f; freq = 3.5f; halfLen = 0.4f; }
            else if (n.Contains("clownfish")) { amp = 0.18f; freq = 4.5f; halfLen = 0.25f; }
            else if (n.Contains("blue_tang")) { amp = 0.2f; freq = 4f; halfLen = 0.3f; }
            else if (n.Contains("betta")) { amp = 0.25f; freq = 5f; halfLen = 0.3f; }
            else if (n.Contains("mandarin")) { amp = 0.15f; freq = 5.5f; halfLen = 0.25f; }
            else if (n.Contains("shark")) { amp = 0.3f; freq = 1.8f; halfLen = 0.8f; bob = 0.04f; }
            else if (n.Contains("manta")) { amp = 0.15f; freq = 1.2f; halfLen = 0.7f; bob = 0.06f; }
            else if (n.Contains("tuna") || n.Contains("swordfish")) { amp = 0.22f; freq = 3.5f; halfLen = 0.6f; }
            else if (n.Contains("piranha")) { amp = 0.16f; freq = 6f; halfLen = 0.2f; }
            else if (n.Contains("sunfish")) { amp = 0.06f; freq = 1.5f; halfLen = 0.5f; bob = 0.03f; }
            else if (n.Contains("angler")) { amp = 0.12f; freq = 2.5f; halfLen = 0.35f; }
            else if (n.Contains("koi") || n.Contains("goldfish")) { amp = 0.2f; freq = 3.5f; halfLen = 0.35f; }
            else if (n.Contains("bass") || n.Contains("trout") || n.Contains("salmon")) { amp = 0.2f; freq = 4f; halfLen = 0.4f; }
            else if (n.Contains("catfish")) { amp = 0.22f; freq = 3f; halfLen = 0.45f; }
            else if (n.Contains("flounder")) { amp = 0.1f; freq = 3f; halfLen = 0.35f; }
            else if (n.Contains("angelfish")) { amp = 0.18f; freq = 4.5f; halfLen = 0.3f; }
            else if (n.Contains("arowana")) { amp = 0.24f; freq = 3.5f; halfLen = 0.5f; }

            mat.SetFloat("_SwingAmp", amp * Random.Range(0.8f, 1.2f));
            mat.SetFloat("_SwingFreq", freq * Random.Range(0.85f, 1.15f));
            mat.SetFloat("_SwingPhase", Random.Range(0f, Mathf.PI * 2f));
            mat.SetFloat("_FishHalfLength", halfLen);
            mat.SetFloat("_VerticalBob", bob);
            OceanFishVisualProfile.Apply(mat, name);
        }

        private static float SpeciesTargetLength(string n, float fallback)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("sunfish")) return 0.9f;
            if (n.Contains("eel") || n.Contains("moray")) return 2.5f;
            if (n.Contains("catfish")) return 0.6f;
            if (n.Contains("swordfish") || n.Contains("tuna") || n.Contains("salmon")) return 0.8f;
            if (n.Contains("bass") || n.Contains("trout") || n.Contains("arowana")) return 0.55f;
            if (n.Contains("koi") || n.Contains("goldfish")) return 0.4f;
            if (n.Contains("piranha")) return 0.18f;
            if (n.Contains("flounder")) return 0.4f;
            if (n.Contains("angler")) return 0.45f;
            if (n.Contains("seahorse")) return 0.3f;
            if (n.Contains("puffer")) return 0.4f;
            return fallback;
        }

        private static void NormalizeFishScale(GameObject go, float targetLength)
        {
            Renderer[] rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            float size = b.size.magnitude;
            if (size > 0.001f) go.transform.localScale *= targetLength / size;
        }

        private static GameObject CreateProceduralFish(Color color, float scale)
        {
            GameObject root = new GameObject("Reef Fish (procedural)");
            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material bodyMaterial = new Material(lit) { color = color };
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(1.35f, 0.62f, 0.48f) * scale;
            body.GetComponent<Renderer>().sharedMaterial = bodyMaterial;
            Destroy(body.GetComponent<Collider>());
            return root;
        }

        // ==================================================================
        // 10. PLAYER + camera
        // ==================================================================

        private void CreateForegroundMountain()
        {
            // Foreground mountain: extends from seabed up above water surface.
            // Visible as dark silhouette through water, with underwater rocky base.
            Transform root = new GameObject("ForegroundMountain").transform;
            root.SetParent(EnvironmentParent, false);

            GameObject[] rockObjs = Resources.LoadAll<GameObject>("Environment/Rocks");
            GameObject quaterniusRock = Resources.Load<GameObject>("Environment/quaternius_rock");

            // Position: far ahead (+Z), off to one side as a distant silhouette so it
            // never fills the forward view like a wall. 70m is inside fog but still readable.
            float baseX = -35f;
            float baseZ = SpawnZ + 135f;
            float seabedY = SampleTerrainHeight(baseX, baseZ);

            // A modest distant ridge: a few NORMALIZED rocks stacked loosely, not a
            // giant slab. Real rock heights are 2-5m so it reads as a far reef/island
            // foot rather than a foreground monolith.
            int rockCount = 5;
            for (int i = 0; i < rockCount; i++)
            {
                GameObject prefab = null;
                if (i < 2 && quaterniusRock != null) prefab = quaterniusRock;
                else if (rockObjs.Length > 0) prefab = rockObjs[Random.Range(0, rockObjs.Length)];
                if (prefab == null) continue;

                var go = Instantiate(prefab, root);

                float t = (float)i / rockCount;
                float layerRadius = Mathf.Lerp(3.5f, 1f, t);
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float px = baseX + Mathf.Cos(angle) * layerRadius;
                float pz = baseZ + Mathf.Sin(angle) * layerRadius;
                float py = seabedY + Mathf.Lerp(0f, 6f, t);

                go.transform.position = new Vector3(px, py, pz);
                // Normalize to realistic heights (2-5m) via renderer bounds instead of
                // multiplying raw scale, which produced the huge vertical slab.
                float targetHeight = Mathf.Lerp(3.5f, 5.5f, t);
                NormalizeAndPlace(go, targetHeight, py);
                go.transform.rotation = Quaternion.Euler(0f, Random.Range(0, 360), 0f);

                // Use rock material (no FishSwim shader)
                var rends = go.GetComponentsInChildren<Renderer>();
                var rockMat = Resources.Load<Material>("Textures/Rock/RockMat");
                // Apply PBR rock material if available
                var litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (litShader != null && rockMat == null)
                {
                    rockMat = new Material(litShader);
                    var rc = Resources.Load<Texture2D>("Textures/Rock/Rock019_Color");
                    var rn = Resources.Load<Texture2D>("Textures/Rock/Rock019_NormalGL");
                    if (rc != null) rockMat.SetTexture("_BaseMap", rc);
                    if (rn != null && rockMat.HasProperty("_BumpMap")) rockMat.SetTexture("_BumpMap", rn);
                    rockMat.SetColor("_BaseColor", new Color(0.4f, 0.42f, 0.45f));
                }
                if (rockMat != null)
                    foreach (var r in rends) r.sharedMaterial = rockMat;
            }
            Debug.Log($"[Mountain] Created foreground mountain at {baseX},{baseZ}, seabedY={seabedY:F1}");
        }


        private void SetupBackgroundAudio()
        {
            // Existing underwater background loop; the Environment switch preserves its original gain.
            var clip = Resources.Load<AudioClip>("Audio/underwater_ambient");
            if (clip == null)
            {
                Debug.LogWarning("[Audio] underwater_ambient not found in Resources/Audio");
                return;
            }
            var listener = mainCamera != null ? mainCamera.GetComponent<AudioListener>() : null;
            if (listener == null && mainCamera != null)
                mainCamera.gameObject.AddComponent<AudioListener>();

            // Remove any existing BGM source to avoid duplicates
            var existing = GameObject.Find("BGM");
            if (existing != null) Destroy(existing);

            var bgmGO = new GameObject("BGM");
            bgmGO.transform.SetParent(transform, false);
            var src = bgmGO.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = true;
            src.volume = 0.22f;
            src.spatialBlend = 0f; // 2D
            src.dopplerLevel = 0f;
            src.ignoreListenerVolume = false;
            OceanAudioPreferences.Register(src, OceanAudioCategory.Environment);
            src.Play();
            Debug.Log($"[Audio] BGM playing: {clip.name}, length={clip.length:F1}s");
        }

        private void ConfigurePlayer()
        {
            xrOrigin = GameObject.Find("XR Origin (VR)")?.transform;
            if (xrOrigin == null) xrOrigin = new GameObject("XR Origin (VR)").transform;
            // Align the tracked floor to the seabed. Real head height stays authoritative;
            // reef forms rise to eye level without forcing head pitch or roll.
            xrOrigin.position = new Vector3(SpawnX, SampleTerrainHeight(SpawnX, SpawnZ) + 0.12f, SpawnZ);
            xrOrigin.rotation = Quaternion.identity;
            // No CameraPitchLock: headset XR tracking fully drives camera rotation.

            // Hand tracking + swim locomotion (includes controller fallback)
            handController = xrOrigin.GetComponent<HandSwimController>();
            if (handController == null)
                handController = xrOrigin.gameObject.AddComponent<HandSwimController>();

            mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.clearFlags = CameraClearFlags.Skybox;
                mainCamera.backgroundColor = new Color(0.06f, 0.30f, 0.38f, 1f);
                mainCamera.farClipPlane = 200f;
#if UNITY_VISIONOS && !UNITY_EDITOR
                // CompositorServices requires at least .1 m on the verified visionOS runtime.
                mainCamera.nearClipPlane = 0.1f;
#else
                mainCamera.nearClipPlane = 0.05f;
#endif
            }

            ConfigureRenderScale();

            // Auto-rotation test mode for screenshots
            if (autoRotateTest)
            {
                StartCoroutine(AutoRotateTest());
            }
        }

        private void ConfigureRenderScale()
        {
            float defaultW = XRSettings.eyeTextureWidth;
            float defaultH = XRSettings.eyeTextureHeight;
            Debug.Log($"[RenderScale] Default eye: {defaultW}x{defaultH}, resScale={XRSettings.eyeTextureResolutionScale}");
            // Wait for the active XR provider before applying the configured scale.
            StartCoroutine(ApplyRenderScaleAfterInit());
        }

        private System.Collections.IEnumerator ApplyRenderScaleAfterInit()
        {
            // Allow either PICO or OpenXR to finish initializing its display.
            yield return new WaitForSeconds(1.5f);

            float targetScale = experience.requestedEyeScale;
#if ENABLE_PICO_XR_SDK
            var pxrManager = OceanXRPlatform.IsPico ? FindObjectOfType<PXR_Manager>() : null;
            if (pxrManager != null)
            {
                // Keep adaptive scaling off while validating a fixed rendering budget.
                pxrManager.maxEyeTextureScale = targetScale;
                pxrManager.minEyeTextureScale = Mathf.Min(1f, targetScale);
                pxrManager.adaptiveResolution = false;
            }
#endif

            // URP owns XR target scaling. Applying both URP and XR multipliers would double-scale.
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.renderScale = targetScale;
            else
                XRSettings.eyeTextureResolutionScale = targetScale;
            XRSettings.renderViewportScale = 1.0f;
            Debug.Log($"[RenderScale] Requested scale={targetScale}; runtime acceptance must be measured");

            yield return new WaitForSeconds(1f);
            float actualW = XRSettings.eyeTextureWidth;
            float actualH = XRSettings.eyeTextureHeight;
            float actualRes = XRSettings.eyeTextureResolutionScale;
            var runningDisplays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(runningDisplays);
            foreach (var display in runningDisplays)
                if (display.running && display.TryGetDisplayRefreshRate(out float refresh) && refresh > 0f)
                    Application.targetFrameRate = Mathf.RoundToInt(refresh);
            Debug.Log($"[RenderScale] Actual eye texture={actualW}x{actualH}, reportedScale={actualRes}, viewportScale={XRSettings.renderViewportScale}; eye dimensions already include allocation scale");
        }

        private System.Collections.IEnumerator AutoRotateTest()
        {
            if (xrOrigin == null) yield break;
            float[] angles = { 0, 45, 90, 135, 180, 225, 270, 315 };
            yield return new WaitForSeconds(2f);
            foreach (float angle in angles)
            {
                xrOrigin.rotation = Quaternion.Euler(0f, angle, 0f);
                Debug.Log($"[AutoRotateTest] Now facing {angle} degrees");
                yield return new WaitForSeconds(3f);
            }
        }

        // ==================================================================
        // BOIDS: 200+ schooling fish with GPU instancing
        // ==================================================================
        private Material[] GetOrCreateBoidMaterials(GameObject prefab)
        {
            // A same-named full-detail fallback and SchoolFish asset must not share variants.
            string cacheKey = prefab.name + ":" + prefab.GetInstanceID();
            if (boidMaterialCache.TryGetValue(cacheKey, out var cached))
                return cached;

            bool schoolAsset = Resources.Load<GameObject>("SchoolFish/" + prefab.name) == prefab;
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            var result = new List<Material>();
            foreach (var r in renderers)
            {
                foreach (var srcMat in r.sharedMaterials)
                {
                    if (srcMat == null) { result.Add(null); continue; }
                    Material mat;
                    if (srcMat.shader.name == "SwanUnderwater/FishSwim")
                    {
                        mat = new Material(srcMat);
                    }
                    else
                    {
                        mat = new Material(Shader.Find("SwanUnderwater/FishSwim"));
                        CopyTextureProperty(srcMat, mat, "baseColorTexture", "_BaseMap");
                        CopyTextureProperty(srcMat, mat, "_BaseMap", "_BaseMap");
                        CopyTextureProperty(srcMat, mat, "normalTexture", "_BumpMap");
                        CopyTextureProperty(srcMat, mat, "metallicRoughnessTexture", "_MetallicGlossMap");
                        CopyTextureProperty(srcMat, mat, "occlusionTexture", "_OcclusionMap");
                        CopyTextureProperty(srcMat, mat, "emissiveTexture", "_EmissionMap");
                        CopyColorProperty(srcMat, mat, "baseColorFactor", "_BaseColor");
                        CopyFloatProperty(srcMat, mat, "metallicFactor", "_Metallic");
                        CopyFloatProperty(srcMat, mat, "normalScale", "_BumpScale");
                        CopyFloatProperty(srcMat, mat, "occlusionStrength", "_OcclusionStrength");
                        SetSpeciesParams(mat, prefab.name);
                    }
                    // Only the verified small-school resources use flat normals, AO=1 and no emission.
                    // If a future school asset gains one of these maps, retain the complete shader.
                    bool detailedSource =
                        (srcMat.HasProperty("normalTexture") && srcMat.GetTexture("normalTexture") != null) ||
                        (srcMat.HasProperty("occlusionTexture") && srcMat.GetTexture("occlusionTexture") != null) ||
                        (srcMat.HasProperty("emissiveTexture") && srcMat.GetTexture("emissiveTexture") != null) ||
                        (srcMat.HasProperty("_BumpMap") && srcMat.GetTexture("_BumpMap") != null) ||
                        (srcMat.HasProperty("_OcclusionMap") && srcMat.GetTexture("_OcclusionMap") != null) ||
                        (srcMat.HasProperty("_EmissionMap") && srcMat.GetTexture("_EmissionMap") != null);
                    bool simpleSchool = schoolAsset && !detailedSource;
                    if (simpleSchool) mat.EnableKeyword("_SCHOOL_SIMPLE");
                    else mat.DisableKeyword("_SCHOOL_SIMPLE");
                    // Packed metallic/roughness texture and factors stay exactly as copied above.
                    mat.enableInstancing = true;
                    if (schoolAsset)
                        Debug.Log($"[SchoolMaterial] fish={prefab.name}, simple={simpleSchool}, " +
                            $"packedMetallicMap={mat.GetTexture("_MetallicGlossMap") != null}, instancing={mat.enableInstancing}");
                    result.Add(mat);
                }
            }
            var arr = result.ToArray();
            boidMaterialCache[cacheKey] = arr;
            return arr;
        }

        private System.Collections.IEnumerator CreateBoids()
        {
            boidRoot = new GameObject("Boids").transform;
            boidRoot.SetParent(EnvironmentParent, false);

            // Small schooling fish species only
            var allPrefabs = fishPrefabGO;
            var schoolPrefabs = new List<GameObject>();
            int lowPolySpecies = 0;
            string[] boidSpecies = { "02_blue_tang", "23_mandarinfish", "01_clownfish" };
            foreach (var name in boidSpecies)
            {
                var p = Resources.Load<GameObject>("SchoolFish/" + name);
                if (p != null) lowPolySpecies++;
                else p = System.Array.Find(allPrefabs, x => x.name == name);
                if (p != null) schoolPrefabs.Add(p);
            }
            if (schoolPrefabs.Count == 0) schoolPrefabs.AddRange(allPrefabs);
            boidPrefabPool = schoolPrefabs.ToArray();
            Debug.Log($"[Boids] lowpolySpecies={lowPolySpecies}/{boidSpecies.Length}; hero resources unchanged");

            boidSchoolCount = OceanResidentDensityPolicy.SchoolCount(boidSchoolCount, boidCount);
            // Preserve the fish budget while distributing more groups along actual routes.
            int fishPerSchool = boidCount / boidSchoolCount;
            for (int s = 0; s < boidSchoolCount; s++)
            {
                float ang = (float)s / boidSchoolCount * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                float dist = Random.Range(5f, 19f);
                float cx = SpawnX + Mathf.Cos(ang) * dist;
                float cz = SpawnZ + Mathf.Sin(ang) * dist;
                float floorY = SampleTerrainHeight(cx, cz);
                // Schools cruise 2.5-6m above the seabed (mid-water), never up in the dome.
                float cy = Mathf.Clamp(floorY + Random.Range(2.5f, 6f), -0.5f, 2.6f);
                // Collective heading is a near-horizontal shared vector; tangent-ish bias.
                float headAng = ang + Mathf.PI * 0.5f + Random.Range(-0.6f, 0.6f);
                var school = new BoidSchool
                {
                    center = new Vector3(cx, cy, cz),
                    direction = new Vector3(Mathf.Cos(headAng), 0f, Mathf.Sin(headAng)).normalized,
                    speed = Random.Range(0.7f, 1.3f),
                    turnRate = Random.Range(0.05f, 0.12f),
                    scatterTimer = 0f,
                    fishCount = 0
                };
                if (OceanResidentDensityPolicy.IsSchoolReefResident(s) && ReefHabitat.Pockets.Count > 0)
                {
                    school.pocketIndex = (s * 23 + 11) % ReefHabitat.Pockets.Count;
                    school.center = ReefHabitat.Pockets[school.pocketIndex].center + Vector3.up * 0.25f;
                    school.speed = Random.Range(0.25f, 0.5f);
                    var nearby = new List<int>();
                    for (int p = 0; p < ReefHabitat.Pockets.Count; p++) nearby.Add(p);
                    Vector3 home = school.center;
                    nearby.Sort((a, b) => (ReefHabitat.Pockets[a].center - home).sqrMagnitude.CompareTo(
                        (ReefHabitat.Pockets[b].center - home).sqrMagnitude));
                    // Each group spreads among eight adjacent corals, with a distinct phase per fish.
                    school.pocketIndices = nearby.GetRange(0, Mathf.Min(8, nearby.Count)).ToArray();
                }
                PlaceSanctuarySchool(school,s);
                boidSchools.Add(school);
            }

            // Conservative model diagonals 9–15 cm; the old 4–8 cm fish vanished at distance.
            boidMPBs = new MaterialPropertyBlock[boidCount];
            for (int i = 0; i < boidCount; i++)
            {
                int schoolIdx = i % boidSchoolCount;
                var school = boidSchools[schoolIdx];
                var prefab = boidPrefabPool[schoolIdx % boidPrefabPool.Length];
                var go = Instantiate(prefab, boidRoot);

                float targetLen = OceanResidentDensityPolicy.SchoolLength(schoolIdx, i / boidSchoolCount);
                NormalizeFishScale(go, targetLen);

                // Position within large, loose school formation (3x volume, natural layering)
                Vector3 offset = new Vector3(
                    Random.Range(-2.2f, 2.2f),
                    Random.Range(-0.65f, 0.65f),
                    Random.Range(-1.5f, 1.5f)
                );
                go.transform.position = school.center + offset;
                // All three admitted SchoolFish GLBs have a measured +Z nose.
                // Spawn with the collective heading instead of spending the first turn sideways.
                go.transform.rotation = Quaternion.LookRotation(school.direction, Vector3.up);

                // Apply shared instanced materials
                var renderers = go.GetComponentsInChildren<Renderer>();
                var sharedMats = GetOrCreateBoidMaterials(prefab);
                foreach (var r in renderers)
                    r.sharedMaterials = sharedMats;

                // Per-instance: swim phase + slight silver tint for schooling look
                var mpb = new MaterialPropertyBlock();
                float phase = Random.Range(0f, Mathf.PI * 2f);
                Color tint = Color.Lerp(new Color(0.85f, 0.9f, 0.95f), Color.white, Random.Range(0f, 0.4f));
                mpb.SetFloat("_SwingPhase", phase);
                mpb.SetColor("_BaseColor", tint);
                foreach (var r in renderers) r.SetPropertyBlock(mpb);
                boidMPBs[i] = mpb;

                var boid = new Boid
                {
                    transform = go.transform,
                    velocity = school.direction * school.speed * Random.Range(0.8f, 1.2f),
                    desiredVelocity = school.direction * school.speed,
                    renderers = renderers,
                    motion = OceanSchoolFishMotion.Create(school.direction, phase,
                        sharedMats.Length > 0 && sharedMats[0] != null ? sharedMats[0].GetFloat("_SwingFreq") : 4.5f),
                    phase = phase,
                    schoolIndex = schoolIdx,
                    escapeTimer = 0f,
                    formationOffset = offset,
                    terrainFloor = SampleTerrainHeight(go.transform.position.x, go.transform.position.z) + 0.25f,
                    baseScale = targetLen
                };
                if (school.pocketIndices != null)
                {
                    boid.pocketIndex = school.pocketIndices[(i / boidSchoolCount) % school.pocketIndices.Length];
                    boid.reefTarget = CoralTarget(boid.pocketIndex, phase, false, 0.05f);
                    boid.transform.position = boid.reefTarget;
                    boid.retargetTimer = Random.Range(0.2f, 3.5f);
                    boid.terrainFloor = SampleTerrainHeight(boid.reefTarget.x, boid.reefTarget.z) + 0.15f;
                }
                else
                {
                    Vector3 spawn = go.transform.position;
                    spawn.y = Mathf.Max(spawn.y, boid.terrainFloor);
                    go.transform.position = spawn;
                }
                Vector3 placed = boid.transform.position;
                placed.y = Mathf.Max(placed.y, boid.terrainFloor);
                boid.transform.position = placed;
                Vector4 initialClock = OceanSchoolFishMotion.CaptureShaderClock(ref boid.motion, boid.baseScale, Time.time, out Vector4 initialMotion);
                mpb.SetVector("_SwimMotion", initialMotion);
                mpb.SetVector("_SchoolClock", initialClock);
                foreach (Renderer renderer in renderers) renderer.SetPropertyBlock(mpb);
                boids.Add(boid);
                school.fishCount++;
                if (i % 40 == 39)
                {
                    loading.SetProgress(0.78f + 0.15f * (i + 1) / boidCount, "准备鱼群");
                    yield return null;
                }
            }
            Debug.Log($"[Boids] Created {boids.Count} tiny schooling fish in {boidSchoolCount} schools (each ~{fishPerSchool})");
        }

        private void UpdateBoids(float dt)
        {
            // Patrol centres and their members must use the same bounded active step.
            // A resumed/long frame otherwise leaves a smooth centre but jumping fish.
            dt = OceanSchoolFishMotion.BoundedDelta(dt);
            if (dt <= 0f) return;
            Vector3 playerPos = mainCamera != null ? mainCamera.transform.position : new Vector3(SpawnX, 0f, SpawnZ);

            // Refresh from Unity every active frame: retained maps and external parent/fish
            // repositioning must never inherit a stale simulation position. Entries are
            // overwritten immediately below after each move, preserving the original
            // i-order: a later fish observes an earlier neighbour's current-frame position.
            for (int i = 0; i < boids.Count; i++)
                boids[i].worldPosition = boids[i].transform.position;

            // === School-level update: collective direction + gentle turning ===
            for (int s = 0; s < boidSchools.Count; s++)
            {
                var school = boidSchools[s];
                if (UpdateSanctuarySchool(school, s, playerPos, dt)) continue;
                if (school.pocketIndex >= 0)
                {
                    float angle = Time.time * 0.15f + s * 1.7f;
                    school.direction = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    school.center = ReefHabitat.Pockets[school.pocketIndex].center +
                        new Vector3(Mathf.Cos(angle) * 0.6f, 0.25f, Mathf.Sin(angle) * 0.6f);
                    continue; // Residents respond individually at their own coral, not one shared flee point.
                }

                // Use the closest individually tracked hand, including one-hand tracking.
                Vector3 handPos = Vector3.zero;
                float handDistance;
                bool handsActive = handController != null && handController.TryGetClosestActiveHand(school.center, out handPos, out handDistance);
                if (handsActive && school.scatterTimer <= 0f)
                {
                    Vector3 toHand = school.center - handPos;
                    if (toHand.sqrMagnitude < 9f) // 3m detection for group
                    {
                        school.direction = toHand.normalized;
                        school.scatterTimer = 1.5f;
                        school.speed = 2.8f; // burst speed
                    }
                }

                // Recover from scatter
                if (school.scatterTimer > 0f)
                {
                    school.scatterTimer -= dt;
                    if (school.scatterTimer <= 0f)
                        school.speed = Random.Range(0.7f, 1.3f);
                }
                else
                {
                    // Gentle turning: slowly rotate direction for curved path
                    float turn = Mathf.Sin(Time.time * 0.3f + s * 1.7f) * school.turnRate;
                    school.direction = Quaternion.Euler(0f, turn * dt * 30f, 0f) * school.direction;
                    school.direction = school.direction.normalized;
                }

                // Move school center in collective direction
                school.center += school.direction * school.speed * dt;
                // Gentle vertical drift
                float floorAtSchool = SampleTerrainHeight(school.center.x, school.center.z);
                school.center.y = Mathf.MoveTowards(school.center.y,
                    Mathf.Clamp(floorAtSchool + 3.5f, -0.5f, 2.6f), 0.4f * dt);

                Vector3 fromPlayer = SchoolBoundaryOffset(school,playerPos);
                if (fromPlayer.sqrMagnitude > 32f * 32f)
                    school.direction = Vector3.RotateTowards(school.direction,
                        -fromPlayer.normalized, dt * 0.9f, 0f).normalized;

            }

            // === Fish-level update: staggered steering, with movement every frame ===
            int fishPerSchool = boids.Count / boidSchoolCount;
            int updateParity = Time.frameCount % 2; // alternate schools each frame

            for (int i = 0; i < boids.Count; i++)
            {
                var b = boids[i];
                var school = boidSchools[b.schoolIndex];
                Vector3 position = b.worldPosition;

                // Staggered update: only half the schools do full boids per frame
                // Non-updated schools keep moving with current velocity (direction is stable)
                if (b.schoolIndex % 2 == updateParity)
                {
                    // 1. Alignment: strong shared heading so the whole school travels one way
                    Vector3 alignment = school.direction * school.speed * 4f;

                    // 2. Cohesion: stay near school center
                    // Preserve a loose volume instead of collapsing every fish onto one line.
                    Vector3 formationTarget = school.center + SanctuaryFormationOffset(school, b.formationOffset) +
                        Vector3.up * (Mathf.Sin(Time.time * 0.45f + b.phase) * 0.15f);
                    Vector3 toCenter = formationTarget - position;
                    float centerDist = toCenter.magnitude;
                    Vector3 cohesion = centerDist > 0.01f ? toCenter.normalized * Mathf.Min(centerDist * 1.5f, 4f) : Vector3.zero;

                    // 3. Separation: avoid nearby fish (weak, sampled)
                    Vector3 separation = Vector3.zero;
                    int localIndex = i / boidSchoolCount;
                    for (int j = 0; j < 4; j++)
                    {
                        int memberCount = (boids.Count - 1 - b.schoolIndex) / boidSchoolCount + 1;
                        int idx = b.schoolIndex + ((localIndex + j * 11 + 1) % memberCount) * boidSchoolCount;
                        if (idx >= 0 && idx < boids.Count && idx != i)
                        {
                            Vector3 diff = position - boids[idx].worldPosition;
                            float d = diff.magnitude;
                            if (d < 0.25f && d > 0.001f)
                                separation += diff.normalized * (0.25f - d) * 2f;
                        }
                    }

                    Vector3 desired;
                    if (b.pocketIndex >= 0)
                        desired = ReefBoidVelocity(b, school, position, separation, dt * 2f);
                    else
                    {
                        desired = (alignment + cohesion + separation).normalized * school.speed;
                        if (school.scatterTimer > 0f) desired = desired.normalized * 2.8f;
                    }

                    desired = SanctuaryBoidVelocity(school, position, formationTarget, separation, desired);
                    b.desiredVelocity = desired;
                }

                // Keep above terrain (tiny fish, 0.2m clearance)
                // Re-sample immediately near the bottom; high fish retain staggered terrain queries.
                if ((i + Time.frameCount) % 8 == 0 || position.y < b.terrainFloor + 0.4f)
                {
                    Vector3 probe = position + Vector3.ProjectOnPlane(b.transform.forward, Vector3.up) * Mathf.Max(.08f, b.baseScale);
                    b.terrainFloor = SampleTerrainHeight(probe.x, probe.z) + .15f;
                }
                Vector3 desiredMotion = b.desiredVelocity;
                if (position.y < b.terrainFloor + .12f)
                    desiredMotion.y = Mathf.Max(desiredMotion.y, Mathf.Max(.18f, (b.terrainFloor + .15f - position.y) * 2f));
                // Turn the body, then swim along its nose. A cohesion/escape goal behind
                // a fish can no longer pull it backwards while its visible body catches up.
                b.velocity = OceanSchoolFishMotion.Step(ref b.motion, desiredMotion, b.baseScale,
                    b.escapeTimer > 0f || school.scatterTimer > 0f, dt, out Quaternion heading);
                Vector3 proposed = position + b.velocity * dt;
                if (proposed.y < b.terrainFloor && (position.y >= b.terrainFloor || proposed.y <= position.y))
                {
                    // Hold translation while the nose turns upward; never hard-set Y
                    // after moving, which made a nose-first fish visibly slide vertically.
                    b.velocity = Vector3.zero;
                    b.motion.speed = 0f;
                    b.transform.rotation = heading;
                }
                else
                {
                    b.transform.SetPositionAndRotation(proposed, heading);
                    // Unity stores local transforms. Read back the actual world value so
                    // non-identity/scaled parents retain the old world->local->world float
                    // roundtrip for later neighbours, rather than accumulating a cache drift.
                    b.worldPosition = b.transform.position;
                }
                // Shared material and GPU instancing remain intact. Each fish uploads a
                // speed/turn-driven clock only once per eight frames; GPU advances between.
                if ((i + Time.frameCount) % 8 == 0)
                {
                    MaterialPropertyBlock block = boidMPBs[i];
                    Vector4 clock = OceanSchoolFishMotion.CaptureShaderClock(ref b.motion, b.baseScale, Time.time, out Vector4 swimMotion);
                    block.SetVector("_SwimMotion", swimMotion);
                    block.SetVector("_SchoolClock", clock);
                    foreach (Renderer renderer in b.renderers)
                        if (renderer != null) renderer.SetPropertyBlock(block);
                }
            }
        }

        private Vector3 ReefBoidVelocity(Boid fish, BoidSchool school, Vector3 position, Vector3 separation, float dt)
        {
            fish.escapeTimer = Mathf.Max(0f, fish.escapeTimer - dt);
            fish.retargetTimer -= dt;
            Vector3 hand = Vector3.zero;
            float handDistance = float.PositiveInfinity;
            bool threatened = handController != null &&
                handController.TryGetClosestActiveHand(position, out hand, out handDistance) && handDistance < 0.9f * 0.9f;
            if (threatened)
            {
                if (fish.escapeTimer <= 0f)
                {
                    fish.handShelterResponses++;
                    fish.pocketIndex = SafeRefugePocket(position, hand, school.pocketIndices, fish.pocketIndex);
                    fish.reefTarget = CoralTarget(fish.pocketIndex, fish.phase, true, 0.05f);
                    fish.inShelter = false;
                }
                fish.escapeTimer = 2.2f;
                fish.returningToShelter = true;
                fish.retargetTimer = 2.8f;
            }
            if (fish.retargetTimer <= 0f && fish.escapeTimer <= 0f)
            {
                fish.returningToShelter = Random.value < 0.32f;
                fish.reefTarget = CoralTarget(fish.pocketIndex, fish.phase + Time.time * 0.35f,
                    fish.returningToShelter, 0.05f);
                fish.retargetTimer = Random.Range(2f, 4.5f);
                fish.inShelter = false;
            }
            Vector3 toTarget = fish.reefTarget - position;
            float distance = toTarget.magnitude;
            if (fish.returningToShelter && distance < 0.16f)
            {
                if (!fish.inShelter) fish.shelterEntries++;
                fish.inShelter = true;
                return toTarget * 0.5f; // Hover inside the individual crevice instead of overshooting it.
            }
            float speed = fish.escapeTimer > 0f ? 1.4f : school.speed;
            speed *= Mathf.Clamp01(distance / (fish.escapeTimer > 0f ? 0.3f : 0.5f));
            Vector3 desired = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized * speed : Vector3.zero;
            if (!fish.returningToShelter) desired += separation * 0.15f;
            if (threatened && handDistance < 0.28f * 0.28f && distance > 0.3f)
                desired += (position - hand).normalized * 0.35f;
            return Vector3.ClampMagnitude(desired, fish.escapeTimer > 0f ? 1.5f : 0.65f);
        }

        private static int SafeRefugePocket(Vector3 fishPosition, Vector3 hand, int[] candidates, int fallback)
        {
            int chosen = fallback;
            float best = float.PositiveInfinity;
            foreach (int candidate in candidates)
            {
                Vector3 refuge = ReefHabitat.Pockets[candidate].shelter;
                Vector3 path = refuge - fishPosition;
                float t = path.sqrMagnitude > 0.001f ? Mathf.Clamp01(Vector3.Dot(hand - fishPosition, path) / path.sqrMagnitude) : 0f;
                float handPathDistance = Vector3.Distance(fishPosition + path * t, hand);
                float score = path.magnitude + Mathf.Max(0f, 0.65f - handPathDistance) * 5f;
                if (score < best) { best = score; chosen = candidate; }
            }
            return chosen;
        }

        // ==================================================================
        // LARGE CREATURES: 3-4 big animals cruising at distance
        // ==================================================================
        private readonly List<Bounds> largeReefBounds = new List<Bounds>();

        private void CreateLargeCreatures()
        {
            largeCreatureRoot = new GameObject("LargeCreatures").transform;
            largeCreatureRoot.SetParent(EnvironmentParent, false);

            // Large creatures: shark, hammerhead, manta, tuna, swordfish
            string[] largePrefabNames = { "15_great_white_shark", "14_hammerhead_shark", "16_manta_ray", "12_bluefin_tuna", "13_swordfish" };
            // Conservative body envelope diameters in metres (shark ~5m, hammerhead ~4m, manta ~4m, tuna ~2.2m, swordfish ~3m)
            float[] largeScales = { 5f, 4f, 4f, 2.2f, 3f };
            float[] largeSpeeds = { 1.5f, 1.8f, 1.0f, 2.2f, 2.5f };

            CacheLargeReefBounds();
            // The largest measured shark patrols the reef; smaller sharks retain nearby passes.
            // the rest are manta/hammerhead so the scene has at most a few big fish.
            string[] largeRoster = { "15_great_white_shark", "16_manta_ray", "14_hammerhead_shark", "12_bluefin_tuna" };
            float[] rosterScales = { 5f, 4f, 4f, 2.2f };
            float[] rosterSpeeds = { 1.6f, 1.0f, 1.8f, 2.2f };

            for (int i = 0; i < largeCreatureCount; i++)
            {
                string name = largeRoster[i % largeRoster.Length];
                var prefab = Resources.Load<GameObject>($"{fishResourceFolder}/{name}");
                if (prefab == null)
                {
                    var all = Resources.LoadAll<GameObject>(fishResourceFolder);
                    // Filter out small fish (moray, clownfish, etc.) from large creature fallback
                    prefab = all[Random.Range(0, all.Length)];
                    int guard = 0;
                    while ((prefab.name.ToLowerInvariant().Contains("moray") || prefab.name.ToLowerInvariant().Contains("clownfish") || prefab.name.ToLowerInvariant().Contains("piranha") || prefab.name.ToLowerInvariant().Contains("seahorse") || prefab.name.ToLowerInvariant().Contains("koi") || prefab.name.ToLowerInvariant().Contains("goldfish")) && guard++ < 20)
                        prefab = all[Random.Range(0, all.Length)];
                }

                var go = Instantiate(prefab, largeCreatureRoot);
                AlignSpeciesModel(go, prefab.name);
                // Normalize to the configured full bounds diagonal (m),
                // instead of multiplying scale blindly (which caused giant black fish).
                float realLength = rosterScales[i % rosterScales.Length] * Random.Range(0.95f, 1.08f);
                NormalizeFishScale(go, realLength);
                float scale = realLength;

                // Shark starts in the forward (+Z) arc so it visibly crosses the
                // opening view; the others spread around the ring.
                bool isShark = name.Contains("shark");
                bool openWaterPatrol = isShark && (buildingEnvironment ?? activeEnvironment)?.kind == OceanEnvironmentKind.Classic;
                float ang = i == 0 ? Mathf.PI / 2f : openWaterPatrol ? Mathf.PI * .76f : (float)i / largeCreatureCount * Mathf.PI * 2f;
                float radius = isShark ? 13f + i * 2f : largeCreatureOrbitRadius + Random.Range(5f, 15f);
                float lx = SpawnX + Mathf.Cos(ang) * radius;
                float lz = SpawnZ + Mathf.Sin(ang) * radius;
                // Open water 3-6m above the seabed; never up near the dome top.
                float ly = Mathf.Clamp(SampleTerrainHeight(lx, lz) + Random.Range(3f, 6f), -0.5f, 2.2f);
                go.transform.position = new Vector3(lx, ly, lz);

                // Apply shader with shared materials (GPU instancing)
                var renderers = go.GetComponentsInChildren<Renderer>();
                var lcSharedMats = GetOrCreateBoidMaterials(prefab);
                foreach (var r in renderers)
                    r.sharedMaterials = lcSharedMats;

                var mpb = new MaterialPropertyBlock();
                float phase = Random.Range(0f, Mathf.PI * 2f);
                mpb.SetFloat("_SwingPhase", phase);
                mpb.SetColor("_BaseColor", Color.white);
                foreach (var r in renderers) r.SetPropertyBlock(mpb);

                Bounds bodyBounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.one);
                for (int r = 1; r < renderers.Length; r++) bodyBounds.Encapsulate(renderers[r].bounds);

                var lc = new LargeCreature
                {
                    transform = go.transform,
                    direction = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang)),
                    speed = rosterSpeeds[i % rosterSpeeds.Length] * Random.Range(0.85f, 1.15f),
                    orbitAngle = ang,
                    phase = phase,
                    baseScale = scale,
                    // Match NormalizeFishScale's full AABB diagonal before patrol rotation.
                    // This is a conservative enclosing diameter, not anatomical nose-to-tail length.
                    bodyEnvelopeDiameter = OceanSharkPatrolPolicy.EnvelopeDiameter(bodyBounds.size.x, bodyBounds.size.y, bodyBounds.size.z),
                    bodyRadius = bodyBounds.extents.magnitude + .15f,
                    localBodyCenter = go.transform.InverseTransformPoint(bodyBounds.center),
                    patrolCenter = new Vector3(SpawnX, 0f, SpawnZ),
                    isShark = isShark,
                    openWaterPatrol = openWaterPatrol,
                    passTimer = 6f + i * 8f
                };
                largeCreatures.Add(lc);
            }
            float[] measuredEnvelopes = new float[largeCreatures.Count];
            for (int i = 0; i < largeCreatures.Count; i++)
                measuredEnvelopes[i] = largeCreatures[i].isShark ? largeCreatures[i].bodyEnvelopeDiameter : 0f;
            int largest = OceanSharkPatrolPolicy.Largest(measuredEnvelopes);
            if (largest >= 0) ConfigureReefShark(largeCreatures[largest]);
            foreach (var creature in largeCreatures)
                if (creature.isShark)
                {
                    if (!creature.reefPatrol && creature.openWaterPatrol)
                        creature.patrolHeight = AmbientSharkCruiseHeight(creature.patrolCenter, 17f, creature.bodyRadius);
                    PlaceLargeSharkInClearWater(creature);
                }
            Debug.Log($"[LargeCreatures] Created {largeCreatures.Count} large creatures; reefBounds={largeReefBounds.Count}");
        }

        private void UpdateLargeCreatures(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            dt = Mathf.Min(dt, 0.05f);
            Vector3 playerPos = PlayerBodyCenter();
            for (int i = 0; i < largeCreatures.Count; i++)
            {
                var lc = largeCreatures[i];
                if (lc.transform == null || !lc.transform.gameObject.activeInHierarchy || campaignSharks.Contains(lc.transform)) continue;
                Vector3 body = LargeBodyCenter(lc);
                if (lc.isShark)
                    closestSharkDistanceEver = Mathf.Min(closestSharkDistanceEver, Vector3.Distance(body, playerPos));
                if (!lc.reefPatrol) lc.patrolCenter = Vector3.MoveTowards(lc.patrolCenter, playerPos, 0.45f * dt);
                lc.passTimer -= dt;
                if (lc.isShark && !lc.reefPatrol && lc.passStage == 0 && lc.passTimer <= 0f) BeginSharkPass(lc, playerPos);
                Vector3 target;
                if (lc.reefPatrol)
                {
                    // Advance from actual body position: an obstacle cannot leave the route racing ahead.
                    lc.orbitAngle = Mathf.Atan2((body.z - lc.patrolCenter.z) / lc.patrolRadii.y,
                        (body.x - lc.patrolCenter.x) / lc.patrolRadii.x);
                    OceanSharkPatrolPolicy.Point(lc.patrolCenter.x, lc.patrolCenter.z,
                        lc.patrolRadii.x, lc.patrolRadii.y, lc.orbitAngle + .38f, out float tx, out float tz);
                    OceanSharkPatrolPolicy.KeepTargetOutsidePlayer(ref tx, ref tz, playerPos.x, playerPos.z,
                        OceanSharkPatrolPolicy.PlayerClearance(lc.bodyEnvelopeDiameter, true) + 2f, lc.orbitAngle);
                    target = new Vector3(tx, lc.patrolHeight + Mathf.Sin(lc.phase + Time.time * .13f) * .2f, tz);
                }
                else if (lc.passStage > 0)
                {
                    lc.passElapsed += dt;
                    lc.passMinDistance = Mathf.Min(lc.passMinDistance, Vector3.Distance(body, playerPos));
                    target = lc.passStage == 1 ? lc.passEntry : lc.passExit;
                    if ((body - target).sqrMagnitude < 3.0f * 3.0f)
                    {
                        if (lc.passStage == 1) { lc.passStage = 2; target = lc.passExit; }
                        else
                        {
                            lc.passStage = 0;
                            lc.completedPasses++;
                            lc.passTimer = Random.Range(18f, 28f);
                            lc.orbitAngle = Mathf.Atan2(body.z - playerPos.z, body.x - playerPos.x);
                            Debug.Log($"[SharkPass] fish={lc.transform.name}, completed={lc.completedPasses}, timeout=false, " +
                                $"minBodyCenterDistance={lc.passMinDistance:F2}m, elapsed={lc.passElapsed:F2}s, nextIn={lc.passTimer:F1}s");
                        }
                    }
                    if (lc.passTimer < -40f)
                    {
                        Debug.Log($"[SharkPass] fish={lc.transform.name}, completed={lc.completedPasses}, timeout=true, " +
                            $"minBodyCenterDistance={lc.passMinDistance:F2}m, elapsed={lc.passElapsed:F2}s, nextIn=8.0s");
                        lc.passStage = 0;
                        lc.passTimer = 8f;
                    } // Moving player can invalidate an old approach.
                }
                else
                {
                    float radius = lc.isShark ? 12f + Mathf.Sin(lc.phase + Time.time * 0.13f) * 2f : largeCreatureOrbitRadius;
                    lc.orbitAngle += lc.speed / radius * dt;
                    float lookAhead = lc.orbitAngle + 0.45f;
                    target = lc.patrolCenter + new Vector3(Mathf.Cos(lookAhead) * radius, 0f, Mathf.Sin(lookAhead) * radius);
                    target.y = playerPos.y + 0.4f + Mathf.Sin(lc.phase + Time.time * 0.2f) * 0.25f;
                }
                float floorClearance = lc.isShark ? lc.bodyRadius + .2f : lc.baseScale * 0.22f + 0.4f;
                target.y = Mathf.Max(target.y, SampleTerrainHeight(target.x, target.z) + floorClearance);
                if (lc.isShark) target.y = Mathf.Max(target.y, LargeSharkWaterHeight(target, lc.bodyRadius));
                if (lc.isShark && !lc.reefPatrol && lc.openWaterPatrol) target.y = Mathf.Max(target.y, lc.patrolHeight);
                Vector3 desired = (target - body).normalized;
                if (lc.isShark)
                {
                    UpdateAmbientSharkHeading(lc, body, desired, playerPos, dt);
                    float actualDistance = Vector3.Distance(LargeBodyCenter(lc), playerPos);
                    closestSharkDistanceEver = Mathf.Min(closestSharkDistanceEver, actualDistance);
                    if (lc.passStage > 0) lc.passMinDistance = Mathf.Min(lc.passMinDistance, actualDistance);
                    continue;
                }
                Vector3 away = Vector3.ProjectOnPlane(body - playerPos, Vector3.up);
                float distance = away.magnitude;
                // Body-aware avoidance begins before the long nose reaches the diver.
                float safetyRadius = OceanSharkPatrolPolicy.PlayerClearance(lc.isShark ? lc.bodyEnvelopeDiameter : lc.baseScale, lc.reefPatrol);
                if (distance < safetyRadius + 1.2f && distance > 0.01f)
                {
                    Vector3 outward = away / distance;
                    Vector3 tangent = Vector3.Cross(Vector3.up, outward);
                    if (Vector3.Dot(tangent, lc.direction) < 0f) tangent = -tangent;
                    float urgency = Mathf.Clamp01((safetyRadius + 1.2f - distance) / 1.2f);
                    desired = Vector3.Lerp(desired, (tangent + outward * urgency * 1.8f).normalized, urgency).normalized;
                }
                lc.direction = Vector3.RotateTowards(lc.direction, desired, dt * 0.75f, 0f).normalized;
                float wantedSpeed = lc.speed * (lc.passStage > 0 ? 1.1f : 1f);
                lc.currentSpeed = Mathf.MoveTowards(lc.currentSpeed, wantedSpeed, dt * 0.55f);
                Vector3 movedBody = OceanFishSteering.Advance(body, lc.transform.rotation, lc.direction * lc.currentSpeed,
                    dt, 85f, false, out Quaternion heading);
                Vector3 displacement = movedBody - body;
                if (distance < safetyRadius && Vector3.Dot(displacement, away) < 0f)
                    displacement = Vector3.zero; // brake rather than skating sideways past the diver
                // A bounding sphere includes nose/tail and shader sway. Sweep its centre against cached
                // real reef bounds; no collider creation, per-frame allocations or rotating-pivot drift.
                if (lc.isShark && !LargeSharkSegmentClear(body, body + displacement, lc.bodyRadius))
                {
                    displacement = Vector3.up * lc.currentSpeed * dt;
                    if (!LargeSharkSegmentClear(body, body + displacement, lc.bodyRadius)) displacement = Vector3.zero;
                }
                movedBody = body + displacement;
                lc.transform.rotation = heading;
                lc.transform.position += movedBody - LargeBodyCenter(lc);
                body = LargeBodyCenter(lc);
                Vector3 nose = body + lc.direction * (lc.baseScale * 0.45f);
                Vector3 tail = body - lc.direction * (lc.baseScale * 0.45f);
                float floor = Mathf.Max(SampleTerrainHeight(body.x, body.z),
                    Mathf.Max(SampleTerrainHeight(nose.x, nose.z), SampleTerrainHeight(tail.x, tail.z))) + floorClearance;
                if (!lc.isShark && body.y < floor) lc.transform.position += Vector3.up * (floor - body.y);
                if (lc.isShark)
                {
                    float actualDistance = Vector3.Distance(LargeBodyCenter(lc), playerPos);
                    closestSharkDistanceEver = Mathf.Min(closestSharkDistanceEver, actualDistance);
                    if (lc.passStage > 0) lc.passMinDistance = Mathf.Min(lc.passMinDistance, actualDistance);
                }
            }
        }

        // At most four route searches per second per shark. Actual short movement is
        // checked every frame, so a cached route never permits swimming through a diver/reef.
        static readonly float[] AmbientSharkRouteAngles = { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 180f };
        private void UpdateAmbientSharkHeading(LargeCreature creature, Vector3 body, Vector3 desired, Vector3 player, float dt)
        {
            float clearance = OceanSharkPatrolPolicy.PlayerClearance(creature.bodyEnvelopeDiameter, creature.reefPatrol);
            Vector3 away = Vector3.ProjectOnPlane(body - player, Vector3.up);
            if (away.sqrMagnitude < (clearance + 1.2f) * (clearance + 1.2f) && away.sqrMagnitude > .0001f)
            {
                Vector3 outward = away.normalized;
                Vector3 tangent = Vector3.Cross(Vector3.up, outward);
                if (Vector3.Dot(tangent, creature.transform.forward) < 0f) tangent = -tangent;
                float urgency = Mathf.Clamp01((clearance + 1.2f - away.magnitude) / 1.2f);
                desired = Vector3.Lerp(desired, (tangent + outward * urgency * 1.8f).normalized, urgency).normalized;
            }

            creature.sharkRouteTimer -= dt;
            if (creature.sharkRouteTimer <= 0f)
            {
                creature.sharkRouteTimer = .25f;
                creature.sharkRouteValid = false;
                Vector3 flat = Vector3.ProjectOnPlane(desired, Vector3.up);
                if (flat.sqrMagnitude < .0001f) flat = Vector3.ProjectOnPlane(creature.transform.forward, Vector3.up);
                if (flat.sqrMagnitude < .0001f) flat = Vector3.forward;
                flat.Normalize();
                float pitch = Mathf.Clamp(Mathf.Atan2(desired.y, Vector3.ProjectOnPlane(desired, Vector3.up).magnitude),
                    -12f * Mathf.Deg2Rad, 12f * Mathf.Deg2Rad);
                // Braking must not shrink the obstacle horizon: otherwise a detour is
                // discarded as soon as speed falls, then selected again on acceleration.
                float lookAhead = Mathf.Max(2.2f, creature.speed * 2f);
                // A descending target can lie beyond a raised reef shelf. If all
                // pitched routes fail, stay level until there is room to descend;
                // never reject a legal start merely because every yaw shares its pitch.
                int routePasses = pitch < -.001f ? 2 : 1;
                for (int pass = 0; pass < routePasses && !creature.sharkRouteValid; pass++)
                {
                    float candidatePitch = pass == 0 ? pitch : 0f;
                    foreach (float angle in AmbientSharkRouteAngles)
                    {
                        Vector3 horizontal = Quaternion.AngleAxis(angle, Vector3.up) * flat;
                        Vector3 candidate = horizontal * Mathf.Cos(candidatePitch) + Vector3.up * Mathf.Sin(candidatePitch);
                        Vector3 endpoint = body + candidate * lookAhead;
                        if (!AmbientSharkPlayerSegmentClear(body, endpoint, player, clearance) ||
                            !LargeSharkSegmentClear(body, endpoint, creature.bodyRadius)) continue;
                        creature.sharkRouteDirection = candidate;
                        creature.sharkRouteValid = true;
                        break;
                    }
                }
            }

            if (!creature.sharkRouteValid) { creature.currentSpeed = 0f; return; }
            // Only the visible wrapper owns heading. Rotation is bounded, with an upright
            // +/-12 degree pitch target; imported mesh alignment stays below this transform.
            creature.transform.rotation = Quaternion.RotateTowards(creature.transform.rotation,
                Quaternion.LookRotation(creature.sharkRouteDirection, Vector3.up), 43f * dt);
            // Rotate about the measured body centre, not an off-centre imported mesh pivot.
            creature.transform.position += body - LargeBodyCenter(creature);
            creature.direction = creature.transform.forward;
            float alignment = Mathf.Max(0f, Vector3.Dot(creature.direction, creature.sharkRouteDirection));
            alignment *= alignment;
            if (alignment < .25f) alignment = 0f;
            float wantedSpeed = creature.speed * (creature.passStage > 0 ? 1.1f : 1f) * alignment;
            creature.currentSpeed = Mathf.MoveTowards(creature.currentSpeed, wantedSpeed, dt * (wantedSpeed < creature.currentSpeed ? 2.5f : .55f));
            creature.currentSpeed = Mathf.Min(creature.currentSpeed, creature.speed * 1.1f * alignment);
            if (creature.currentSpeed <= .00001f) return;
            Vector3 next = body + creature.direction * (creature.currentSpeed * dt);
            if (!AmbientSharkPlayerSegmentClear(body, next, player, clearance) ||
                !LargeSharkSegmentClear(body, next, creature.bodyRadius))
            {
                // Stop, then turn toward the next bounded route search. Never project a
                // step sideways or substitute vertical translation for nose-first motion.
                creature.currentSpeed = 0f;
                return;
            }
            creature.transform.position += next - body;
        }

        private static bool AmbientSharkPlayerSegmentClear(Vector3 from, Vector3 to, Vector3 player, float clearance)
        {
            Vector3 offset = Vector3.ProjectOnPlane(from - player, Vector3.up);
            Vector3 step = Vector3.ProjectOnPlane(to - from, Vector3.up);
            float startSquared = offset.sqrMagnitude;
            if (startSquared < clearance * clearance)
            {
                if (startSquared < .00000001f) return step.sqrMagnitude > .00000001f;
                return Vector3.Dot(offset, step) > 0f; // A diver may approach a stopped shark: allow only escape outward.
            }
            float t = step.sqrMagnitude > .00000001f ? Mathf.Clamp01(-Vector3.Dot(offset, step) / step.sqrMagnitude) : 0f;
            return (offset + step * t).sqrMagnitude >= clearance * clearance;
        }

        private void CacheLargeReefBounds()
        {
            largeReefBounds.Clear();
            Transform reef = ActiveReefRoot;
            if (ActiveSolidBounds.Count > 0) { largeReefBounds.AddRange(ActiveSolidBounds); return; }
            if (reef == null) return;
            foreach (Transform group in reef)
            {
                // All LODs participate, including disabled renderers, to avoid a later LOD growing into the route.
                Renderer[] renderers = group.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                largeReefBounds.Add(bounds);
            }
        }

        private void ConfigureReefShark(LargeCreature creature)
        {
            // Pick an actual front reef island, then its neighbouring coral pockets; this anchor never follows the head.
            Vector3 desired = new Vector3(SpawnX, 0f, SpawnZ + 12f), anchor = desired;
            float nearest = float.PositiveInfinity;
            foreach (var pocket in ReefHabitat.Pockets)
            {
                float distance = new Vector2(pocket.center.x - desired.x, pocket.center.z - desired.z).sqrMagnitude;
                if (distance < nearest) { nearest = distance; anchor = pocket.center; }
            }
            Bounds island = new Bounds(anchor, Vector3.zero);
            foreach (var pocket in ReefHabitat.Pockets)
                if (new Vector2(pocket.center.x - anchor.x, pocket.center.z - anchor.z).sqrMagnitude <= 6f * 6f)
                { island.Encapsulate(pocket.center + Vector3.one * pocket.radius); island.Encapsulate(pocket.center - Vector3.one * pocket.radius); }
            creature.reefPatrol = true;
            creature.patrolCenter = island.center;
            creature.patrolRadii = new Vector2(OceanSharkPatrolPolicy.PatrolRadius(island.extents.x, creature.bodyEnvelopeDiameter),
                OceanSharkPatrolPolicy.PatrolRadius(island.extents.z, creature.bodyEnvelopeDiameter));
            creature.patrolHeight = island.max.y + creature.bodyRadius + .55f;
            creature.passStage = 0;
            Debug.Log($"[SharkPatrol] fish={creature.transform.name}, mode=reef-perimeter, envelopeDiameter={creature.bodyEnvelopeDiameter:F2}m, sizeMetric=initial-bounds-diagonal, " +
                $"center={creature.patrolCenter:F2}, radii={creature.patrolRadii:F2}, playerCenterBuffer={OceanSharkPatrolPolicy.PlayerClearance(creature.bodyEnvelopeDiameter, true):F2}m, pockets={ReefHabitat.Pockets.Count}");
        }

        private void PlaceLargeSharkInClearWater(LargeCreature creature)
        {
            Vector3 body = LargeBodyCenter(creature);
            if (creature.reefPatrol)
            {
                OceanSharkPatrolPolicy.Point(creature.patrolCenter.x, creature.patrolCenter.z,
                    creature.patrolRadii.x, creature.patrolRadii.y, 0f, out float x, out float z);
                body = new Vector3(x, creature.patrolHeight, z);
                creature.direction = Vector3.forward;
            }
            else if (creature.openWaterPatrol) body.y = Mathf.Max(body.y, creature.patrolHeight);
            body.y = Mathf.Max(body.y, LargeSharkWaterHeight(body, creature.bodyRadius) + .15f);
            creature.transform.rotation = Quaternion.LookRotation(creature.direction);
            creature.transform.position += body - LargeBodyCenter(creature);
        }

        // Called only when ownership is released or a prepared environment is restored.
        // Cached maps can return after their actors were released while another map owned
        // largeCreatures/reef bounds. Validate against the now-active map once, before its
        // ordinary route loop resumes; a legal earned position and pose are left untouched.
        private void EnsureAmbientSharkWaterStart(LargeCreature creature)
        {
            Vector3 returnedBody = LargeBodyCenter(creature);
            if (LargeSharkSegmentClear(returnedBody, returnedBody, creature.bodyRadius)) return;
            PlaceLargeSharkInClearWater(creature);
            Vector3 restoredBody = LargeBodyCenter(creature);
            if (!LargeSharkSegmentClear(restoredBody, restoredBody, creature.bodyRadius))
                throw new System.InvalidOperationException("No legal ambient water at configuration start: " + creature.transform.name);
            creature.currentSpeed = 0f;
            creature.sharkRouteTimer = 0f; creature.sharkRouteValid = false;
            Debug.Log($"[AmbientShark] start-water fish={creature.transform.name}, from={returnedBody:F3}, to={restoredBody:F3}, clearance={creature.bodyRadius:F3}");
        }

        private float LargeSharkWaterHeight(Vector3 center, float radius)
        {
            float height = SampleRenderedSeabedHeight(center.x, center.z);
            // Nine samples cover the spherical body's footprint, with extra clearance for terrain slope.
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * .25f;
                height = Mathf.Max(height, SampleRenderedSeabedHeight(center.x + Mathf.Cos(angle) * radius, center.z + Mathf.Sin(angle) * radius));
            }
            foreach (Bounds reef in largeReefBounds)
                if (center.x + radius >= reef.min.x && center.x - radius <= reef.max.x &&
                    center.z + radius >= reef.min.z && center.z - radius <= reef.max.z)
                    height = Mathf.Max(height, reef.max.y);
            return height + radius + .35f;
        }

        private float AmbientSharkCruiseHeight(Vector3 center, float routeRadius, float bodyRadius)
        {
            // Place the nearby swimmer in a connected open-water layer above the
            // surrounding reef rim, not in isolated gaps between individual corals.
            float height = LargeSharkWaterHeight(center, bodyRadius);
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI * .125f;
                Vector3 point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * routeRadius;
                height = Mathf.Max(height, LargeSharkWaterHeight(point, bodyRadius));
            }
            foreach (Bounds reef in largeReefBounds)
            {
                float x = Mathf.Clamp(center.x, reef.min.x - bodyRadius, reef.max.x + bodyRadius);
                float z = Mathf.Clamp(center.z, reef.min.z - bodyRadius, reef.max.z + bodyRadius);
                if ((x - center.x) * (x - center.x) + (z - center.z) * (z - center.z) <= routeRadius * routeRadius)
                    height = Mathf.Max(height, reef.max.y + bodyRadius + .35f);
            }
            return height + .2f;
        }

        private bool LargeSharkSegmentClear(Vector3 from, Vector3 to, float radius)
        {
            // Tiny bounded steps use the exact rendered height sampler; terrain clearance includes the footprint.
            if (to.y < LargeSharkWaterHeight(to, radius) - .1f) return false;
            Vector3 delta = to - from; float length = delta.magnitude;
            Ray ray = new Ray(from, length > .00001f ? delta / length : Vector3.forward);
            foreach (Bounds reef in largeReefBounds)
            {
                // Preview and actual movement share the entire safety-water shelf, not
                // just its endpoint. A long ray can skim a reef corner above its physical
                // box, then leave the footprint; accepting it would strand the next tiny
                // step in LargeSharkWaterHeight's additional .25m safety band.
                float enter = 0f, exit = 1f;
                if (ClipSharkFootprintAxis(from.x, delta.x, reef.min.x - radius, reef.max.x + radius, ref enter, ref exit) &&
                    ClipSharkFootprintAxis(from.z, delta.z, reef.min.z - radius, reef.max.z + radius, ref enter, ref exit))
                {
                    float lowestY = from.y + delta.y * (delta.y >= 0f ? enter : exit);
                    if (lowestY < reef.max.y + radius + .35f - .1f) return false;
                }
                Bounds expanded = reef; expanded.Expand(radius * 2f);
                if (expanded.Contains(to) || expanded.Contains(from) ||
                    (length > .00001f && expanded.IntersectRay(ray, out float entry) && entry <= length)) return false;
            }
            return true;
        }

        private static bool ClipSharkFootprintAxis(float origin, float delta, float min, float max, ref float enter, ref float exit)
        {
            if (delta == 0f) return origin >= min && origin <= max;
            float a = (min - origin) / delta, b = (max - origin) / delta;
            enter = Mathf.Max(enter, Mathf.Min(a, b));
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit;
        }

        private Vector3 PlayerBodyCenter() => mainCamera != null ? mainCamera.transform.position - Vector3.up * 0.45f :
            new Vector3(SpawnX, SampleTerrainHeight(SpawnX, SpawnZ) + 1.2f, SpawnZ);

        private static Vector3 LargeBodyCenter(LargeCreature creature) => creature.transform.TransformPoint(creature.localBodyCenter);

        private void BeginSharkPass(LargeCreature creature, Vector3 playerBody)
        {
            Vector3 forward = mainCamera != null ? Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up) : Vector3.forward;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 lateral = Vector3.Cross(Vector3.up, forward);
            float side = Vector3.Dot(LargeBodyCenter(creature) - playerBody, lateral) >= 0f ? 1f : -1f;
            // Capture the route once. A later head turn cannot swing this 5m animal through the face.
            Vector3 crossing = playerBody + forward * 6.5f + Vector3.up * 0.15f;
            creature.passEntry = crossing + lateral * (side * 9f);
            creature.passExit = crossing - lateral * (side * 11f);
            // Arrival and steering must share these same safe points. Keeping a buried arrival
            // point while only raising the steering copy could make the 3m arrival sphere unreachable.
            creature.passEntry.y = Mathf.Max(creature.passEntry.y, Mathf.Max(
                SampleTerrainHeight(creature.passEntry.x, creature.passEntry.z) + creature.bodyRadius + .2f,
                LargeSharkWaterHeight(creature.passEntry, creature.bodyRadius)));
            creature.passExit.y = Mathf.Max(creature.passExit.y, Mathf.Max(
                SampleTerrainHeight(creature.passExit.x, creature.passExit.z) + creature.bodyRadius + .2f,
                LargeSharkWaterHeight(creature.passExit, creature.bodyRadius)));
            // Both endpoints and the intervening reef share one open-water layer.
            // Endpoint-only heights can strand a low, slow-turning shark on either side.
            if (creature.openWaterPatrol)
            {
                creature.patrolHeight = AmbientSharkCruiseHeight(playerBody, 17f, creature.bodyRadius);
                creature.passEntry.y = creature.passExit.y = Mathf.Max(creature.patrolHeight,
                    Mathf.Max(creature.passEntry.y, creature.passExit.y));
            }
            creature.passStage = 1;
            creature.passTimer = 0f;
            creature.passElapsed = 0f;
            creature.passMinDistance = Vector3.Distance(LargeBodyCenter(creature), playerBody);
            Debug.Log($"[SharkPass] fish={creature.transform.name}, approach: plannedCenterClearance=6.5m, envelopeDiameter={creature.bodyEnvelopeDiameter:F2}m, routeCaptured=true");
        }

        // ==================================================================
        // BOID (schooling fish - lightweight, batched update)
        // ==================================================================
        private sealed class Boid
        {
            public Transform transform;
            // Per-active-frame cache only, never serialized or retained across a refresh.
            public Vector3 worldPosition;
            public Vector3 velocity;
            public Vector3 desiredVelocity;
            public Renderer[] renderers;
            public OceanSchoolFishMotion.State motion;
            public Vector3 formationOffset;
            public float phase;
            public int schoolIndex;
            public float escapeTimer;
            public float baseScale;
            public float terrainFloor = -10f;
            public int pocketIndex = -1;
            public Vector3 reefTarget;
            public float retargetTimer;
            public bool returningToShelter, inShelter;
            public int shelterEntries, handShelterResponses;
        }

        private sealed class BoidSchool
        {
            public Vector3 center;
            public Vector3 direction; // collective heading, normalized
            public float speed;
            public float turnRate; // radians/sec for gentle turning
            public float scatterTimer; // >0 means fleeing from hand
            public int fishCount;
            public int pocketIndex = -1;
            public int[] pocketIndices;
        }

        private sealed class LargeCreature
        {
            public Transform transform;
            public Vector3 direction;
            public float speed;
            public float orbitAngle;
            public float phase;
            public float baseScale;
            public Vector3 localBodyCenter, patrolCenter, passEntry, passExit;
            public Vector2 patrolRadii;
            public float bodyEnvelopeDiameter, bodyRadius, patrolHeight;
            public bool isShark, reefPatrol, openWaterPatrol;
            public int passStage, completedPasses;
            public float passTimer, currentSpeed;
            public float passMinDistance, passElapsed;
            public float sharkRouteTimer;
            public bool sharkRouteValid;
            public Vector3 sharkRouteDirection;
        }

        // ==================================================================
        // FISH AGENT (with hand-escape behavior)
        // ==================================================================
        private sealed class FishAgent
        {
            private readonly Transform transform;
            private readonly float baseSpeed;
            private readonly OceanFishMotion motion;
            private readonly bool upright;
            private readonly Vector3 habitatCenter;
            private readonly float floorClearance;
            private Vector3 direction;
            private Vector3 target;
            private float retargetTimer;
            private readonly float phase;
            private float pauseTimer;
            private float currentSpeed;
            private readonly int pocketIndex;
            private readonly float bodyClearance;
            private bool targetIsShelter;
            public bool IsReefResident => pocketIndex >= 0;
            public bool IsSheltered { get; private set; }
            public int ShelterEntries { get; private set; }
            public int HandShelterResponses { get; private set; }

            // Escape state
            private bool isEscaping;
            private float escapeTimer;
            private float escapeCooldown;
            private Vector3 escapeDir;
            private readonly float escapeSpeedMul;
            private const float EscapeRadius = 0.55f;
            private const float EscapeDuration = 1.2f;
            private const float EscapeCooldown = 2.5f;

            public FishAgent(Transform transform, float speed, Vector3 direction, bool slowEscape = false,
                int pocketIndex = -1, float bodyClearance = 0.2f)
            {
                this.transform = transform;
                upright = transform.name.ToLowerInvariant().Contains("seahorse");
                motion = new OceanFishMotion(transform.gameObject, bodyClearance / .35f);
                this.baseSpeed = speed;
                this.pocketIndex = pocketIndex;
                this.bodyClearance = Mathf.Max(0.15f, bodyClearance);
                habitatCenter = transform.position;
                floorClearance = Mathf.Clamp(transform.position.y - SampleTerrainHeight(transform.position.x, transform.position.z), 0.25f, 3.5f);
                this.currentSpeed = speed;
                this.direction = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.forward;
                this.phase = Random.Range(0f, Mathf.PI * 2f);
                this.escapeSpeedMul = slowEscape ? 1.5f : 2.5f;
                PickTarget(Vector3.zero, new Vector3(30f, 8f, 30f));
            }

            public void TickVisual(float dt) => motion.Tick(dt);

            public void Tick(float dt, Vector3 center, Vector3 bounds, HandSwimController hands)
            {
                if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
                dt = Mathf.Min(dt, OceanFishSteering.MaximumStep);
                Vector3 handPos = Vector3.zero;
                float handDistance;
                bool handsActive = hands != null && hands.TryGetClosestActiveHand(transform.position, out handPos, out handDistance);
                // --- Escape detection ---
                if (escapeCooldown > 0f) escapeCooldown -= dt;

                if (handsActive && !isEscaping && escapeCooldown <= 0f)
                {
                    float distSq = (transform.position - handPos).sqrMagnitude;
                    if (distSq < EscapeRadius * EscapeRadius)
                    {
                        isEscaping = true;
                        escapeTimer = EscapeDuration;
                        escapeCooldown = EscapeCooldown;
                        escapeDir = (transform.position - handPos).normalized;
                        escapeDir.y += Random.Range(-0.2f, 0.3f);
                        escapeDir.Normalize();
                        currentSpeed = baseSpeed * escapeSpeedMul;
                        if (IsReefResident)
                        {
                            HandShelterResponses++;
                            target = CoralTarget(pocketIndex, phase, true, bodyClearance);
                            targetIsShelter = true;
                            IsSheltered = false;
                        }
                    }
                }

                if (isEscaping)
                {
                    if (IsReefResident && handsActive && (transform.position - handPos).sqrMagnitude < 1f)
                        escapeTimer = Mathf.Max(escapeTimer, 0.5f);
                    escapeTimer -= dt;
                    if (escapeTimer <= 0f)
                    {
                        isEscaping = false;
                        currentSpeed = baseSpeed;
                    }
                    else
                    {
                        if (IsReefResident)
                        {
                            Vector3 refuge = target - transform.position;
                            if (refuge.magnitude < 0.18f)
                            {
                                if (!IsSheltered) ShelterEntries++;
                                IsSheltered = true;
                                currentSpeed = 0f;
                                return;
                            }
                            escapeDir = refuge.normalized;
                            currentSpeed = baseSpeed * escapeSpeedMul * Mathf.Clamp01(refuge.magnitude / 0.4f);
                        }
                        // Flee: sharp turn, high speed, tail flick
                        direction = Vector3.Slerp(direction, escapeDir, dt * 4f).normalized;
                        float flicker = 1f + Mathf.Sin(Time.time * 12f + phase) * 0.3f;
                        AdvanceFacing(direction * currentSpeed * flicker, dt, 140f);
                        Vector3 fleeingPosition = transform.position;
                        fleeingPosition.y = Mathf.Max(fleeingPosition.y,
                            SampleTerrainHeight(fleeingPosition.x, fleeingPosition.z) + bodyClearance);
                        transform.position = fleeingPosition;
                        return;
                    }
                }

                // --- Normal cruising ---
                pauseTimer -= dt;
                if (pauseTimer <= 0f)
                {
                    if (Random.value < 0.3f) { currentSpeed = 0f; pauseTimer = Random.Range(0.5f, 1.5f); }
                    else { currentSpeed = baseSpeed * Random.Range(0.7f, 1.3f); pauseTimer = Random.Range(3f, 7f); }
                }

                retargetTimer -= dt;
                float targetDistance = Vector3.Distance(transform.position, target);
                if (IsReefResident && targetIsShelter && targetDistance < 0.18f)
                {
                    if (!IsSheltered) ShelterEntries++;
                    IsSheltered = true;
                    currentSpeed = 0f;
                }
                if (retargetTimer <= 0f || (!IsReefResident && targetDistance < 0.5f))
                    PickTarget(center, bounds);

                Vector3 desired = (target - transform.position).normalized;
                direction = Vector3.Slerp(direction, desired, dt * 0.75f).normalized;
                float bob = Mathf.Sin(Time.time * 1.4f + phase) * 0.035f;
                float approach = IsReefResident ? Mathf.Clamp01(targetDistance / 0.45f) : 1f;
                AdvanceFacing(direction * currentSpeed * approach + Vector3.up * (IsSheltered ? 0f : bob), dt, 85f);
                Vector3 p = transform.position;
                p.y = Mathf.Max(p.y, SampleTerrainHeight(p.x, p.z) + bodyClearance);
                transform.position = p;
            }

            private void AdvanceFacing(Vector3 desiredVelocity, float dt, float turnRate)
            {
                Vector3 next = OceanFishSteering.Advance(transform.position, transform.rotation, desiredVelocity,
                    dt, turnRate, upright, out Quaternion heading);
                transform.SetPositionAndRotation(next, heading);
            }

            private void PickTarget(Vector3 center, Vector3 bounds)
            {
                IsSheltered = false;
                if (IsReefResident)
                {
                    targetIsShelter = Random.value < 0.3f;
                    target = CoralTarget(pocketIndex, phase + Random.Range(0f, Mathf.PI * 2f), targetIsShelter, bodyClearance);
                    retargetTimer = Random.Range(3f, 6f);
                    currentSpeed = baseSpeed;
                    return;
                }
                float x = habitatCenter.x + Random.Range(-3.5f, 3.5f);
                float z = habitatCenter.z + Random.Range(-3.5f, 3.5f);
                target = new Vector3(x, SampleTerrainHeight(x, z) + floorClearance + Random.Range(-0.15f, 0.25f), z);
                retargetTimer = Random.Range(4f, 9f);
            }
        }
    }

    public sealed class DomeFollower : MonoBehaviour
    {
        public Transform domeTransform;
        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam != null && domeTransform != null)
            {
                Vector3 p = domeTransform.position;
                p.x = cam.transform.position.x;
                p.z = cam.transform.position.z;
                domeTransform.position = p;
            }
        }
    }

    /// <summary>
    /// Locks camera pitch for testing/screenshots. Overrides headset pitch
    /// while preserving yaw and roll. Remove in production.
    /// </summary>
    public sealed class UnderwaterLocomotion : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 3.2f;
        [SerializeField] private float verticalSpeed = 2.2f;
        [SerializeField] private float turnSpeed = 65f;

        private UnityEngine.XR.InputDevice left;
        private UnityEngine.XR.InputDevice right;

        private void OnEnable()
        {
            left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        }

        private void Update()
        {
            if (!left.isValid) left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!right.isValid) right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            Vector2 move = Vector2.zero;
            Vector2 look = Vector2.zero;
            left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out move);
            right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out look);

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                move += new Vector2((keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f), (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
                look += new Vector2((keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f), (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f));
            }

            Camera camera = Camera.main;
            Transform basis = camera != null ? camera.transform : transform;
            Vector3 forward = Vector3.ProjectOnPlane(basis.forward, Vector3.up).normalized;
            Vector3 rightDirection = Vector3.ProjectOnPlane(basis.right, Vector3.up).normalized;
            Vector3 velocity = forward * move.y + rightDirection * move.x + Vector3.up * look.y * (verticalSpeed / moveSpeed);
            if (velocity.sqrMagnitude > 1f) velocity.Normalize();
            transform.position += velocity * moveSpeed * Time.deltaTime;
            transform.Rotate(Vector3.up, look.x * turnSpeed * Time.deltaTime, Space.World);
            Vector3 p = transform.position;
            p.y = Mathf.Clamp(p.y, -2.4f, 8f);
            p.x = Mathf.Clamp(p.x, -26f, 26f);
            p.z = Mathf.Clamp(p.z, -26f, 26f);
            transform.position = p;
        }
    }
}

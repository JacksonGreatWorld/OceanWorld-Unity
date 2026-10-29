using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    [InitializeOnLoad]
    public static class CodexBuild
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterWorld.unity";
        private const string PreviewKey = "SwanCodexPreview.";

        static CodexBuild()
        {
            if (SessionState.GetBool(PreviewKey + "active", false)) AttachPreviewCallbacks();
        }

        // Launch a separate process without -quit or -nographics. SessionState survives Play Mode domain reload.
        public static void CapturePreview()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("CapturePreview is only for a separate batch-mode Editor process.");
            try
            {
                if (HasArgument("-quit") || HasArgument("-nographics"))
                    throw new ArgumentException("CapturePreview requires graphics and owns process exit; omit -quit and -nographics.");
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Start CapturePreview from Edit Mode.");
                if (!File.Exists(ScenePath)) throw new FileNotFoundException("Missing saved preview scene.", ScenePath);
                SessionState.SetBool(PreviewKey + "active", true);
                SessionState.SetInt(PreviewKey + "phase", 0);
                SessionState.SetString(PreviewKey + "startedUtcTicks", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
                SessionState.SetString(PreviewKey + "exception", "");
                SessionState.SetString(PreviewKey + "output", "");
                AttachPreviewCallbacks();
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
                Debug.Log("CODEX_PREVIEW_STARTED: existing scene; eight-second Play Mode warm-up; warm-up plus 120-second timeout; editor-only captures.");
                EditorApplication.isPlaying = true;
            }
            catch (Exception e) { FinishPreview(false, e.ToString()); }
        }

        private static void AttachPreviewCallbacks()
        {
            EditorApplication.update -= UpdatePreview;
            EditorApplication.update += UpdatePreview;
            Application.logMessageReceived -= RecordPreviewException;
            Application.logMessageReceived += RecordPreviewException;
        }

        private static void RecordPreviewException(string message, string trace, LogType type)
        {
            if ((type == LogType.Exception || type == LogType.Assert) && SessionState.GetBool(PreviewKey + "active", false))
                SessionState.SetString(PreviewKey + "exception", message + "\n" + trace);
        }

        private static void UpdatePreview()
        {
            if (!SessionState.GetBool(PreviewKey + "active", false)) return;
            try
            {
                long started = long.Parse(SessionState.GetString(PreviewKey + "startedUtcTicks", "0"), CultureInfo.InvariantCulture);
                float warmup = float.Parse(Argument("-codexPreviewWarmup", "8"), CultureInfo.InvariantCulture);
                if (DateTime.UtcNow.Ticks - started > TimeSpan.FromSeconds(warmup + 120).Ticks)
                    throw new TimeoutException("Preview exceeded warm-up plus 120 seconds after entry-point start.");
                string exception = SessionState.GetString(PreviewKey + "exception", "");
                if (!string.IsNullOrEmpty(exception)) throw new InvalidOperationException("Scene raised an exception/assert: " + exception);
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (EditorApplication.isPaused) throw new InvalidOperationException("Editor Play Mode paused before preview could finish.");
                var world = Object.FindFirstObjectByType<UnderwaterWorld>();
                if (OceanEntry.Instance != null || (world != null && !world.IsReady))
                {
                    CaptureEntryProgress();
                    SessionState.SetString(PreviewKey + "playingUtcTicks", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
                    return;
                }
                if (SessionState.GetInt(PreviewKey + "phase", 0) == 0)
                {
                    SessionState.SetInt(PreviewKey + "phase", 1);
                    SessionState.SetString(PreviewKey + "playingUtcTicks", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
                    return;
                }
                long playing = long.Parse(SessionState.GetString(PreviewKey + "playingUtcTicks", "0"), CultureInfo.InvariantCulture);
                if (DateTime.UtcNow.Ticks - playing < TimeSpan.FromSeconds(warmup).Ticks) return;
                CaptureFixedViews();
                string output = SessionState.GetString(PreviewKey + "output", "");
                if (string.IsNullOrEmpty(output) || Directory.GetFiles(output, "*.png").Length != 8)
                    throw new IOException("Preview did not create all eight fixed-view PNG files.");
                // A synchronous render may have raised an exception through Unity's logging callback.
                exception = SessionState.GetString(PreviewKey + "exception", "");
                if (!string.IsNullOrEmpty(exception)) throw new InvalidOperationException(exception);
                FinishPreview(true, output);
            }
            catch (Exception e) { FinishPreview(false, e.ToString()); }
        }

        private static void FinishPreview(bool success, string detail)
        {
            SessionState.SetBool(PreviewKey + "active", false);
            EditorApplication.update -= UpdatePreview;
            Application.logMessageReceived -= RecordPreviewException;
            if (success) Debug.Log("CODEX_PREVIEW_SUCCEEDED: " + detail);
            else Debug.LogError("CODEX_PREVIEW_FAILED: " + detail);
            // This API is limited to its own batch process. Nothing is saved back to the scene.
            if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        }

        private static void CaptureEntryProgress()
        {
            var entry = OceanEntry.Instance;
            if (entry == null) return;
            // Batch Editor has no Game View render loop. Submit the actual main camera
            // explicitly for this preview only; device startup still waits on its XR render.
            if (entry.RenderedFrames < 2)
            {
                Camera main = Camera.main;
                var data = main.GetUniversalAdditionalCameraData();
                bool xr = data.allowXRRendering;
                var initial = new RenderTexture(320, 180, 24);
                try
                {
                    data.allowXRRendering = false;
                    initial.Create();
                    Canvas.ForceUpdateCanvases();
                    RenderPipeline.SubmitRenderRequest(main, new UniversalRenderPipeline.SingleCameraRequest { destination = initial });
                }
                finally { data.allowXRRendering = xr; initial.Release(); Object.DestroyImmediate(initial); }
            }
            bool isTitle = entry.CinematicPhase == OceanEntrySequence.Beat.Welcome || entry.CinematicPhase == OceanEntrySequence.Beat.Chapter;
            if (entry.BeatProgress < (isTitle ? .12f : .40f)) return;
            int milestone = (int)entry.CinematicPhase;
            string suffix = !isTitle || entry.BeatProgress >= .40f && entry.BeatProgress < .85f ? ""
                : entry.BeatProgress < .40f ? "-early" : "-late";
            string key = PreviewKey + "entry" + milestone + suffix;
            if (SessionState.GetBool(key, false)) return;
            SessionState.SetBool(key, true);
            string dir = Path.GetFullPath(Path.Combine(Argument("-codexValidationRoot", "Validation/iteration-6"), "entry-preview"));
            Directory.CreateDirectory(dir);
            var go = new GameObject("Entry preview camera");
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(Camera.main);
            camera.transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation);
            camera.enabled = false;
            camera.fieldOfView = 80f;
            camera.aspect = 16f / 9f;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            var rt = new RenderTexture(1920, 1080, 24);
            var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            try
            {
                entry.AttachPreviewCamera(camera);
                Canvas.ForceUpdateCanvases();
                rt.Create();
                camera.targetTexture = rt;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(dir, "stage-" + milestone + suffix + ".png"), pixels.EncodeToPNG());
                File.WriteAllText(Path.Combine(dir, "stage-" + milestone + suffix + ".txt"),
                    $"Editor-only cinematic view; neutral background is NOT VST. beat={entry.Stage}, beatProgress={entry.BeatProgress}, resources={entry.TargetProgress}, actualMainCameraRenderCallbacks={entry.RenderedFrames}\n");
            }
            finally
            {
                entry.AttachPreviewCamera(Camera.main);
                RenderTexture.active = old;
                camera.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(go);
            }
        }

        [MenuItem("Swan/Codex/Build optimized Android release")]
        // The same Edit Mode checks run independently at each local milestone and
        // again before packaging. No headset or production service is used.
        public static void RunRegressionChecks()
        {
            using (OceanLanguage.OverrideForValidation(OceanLanguageChoice.SimplifiedChinese))
            {
                RunChineseRegressionChecks();
                SwanUnderwater.EditorTests.OceanLanguageChecks.RunAll();
            }
            Debug.Log("OCEAN_REGRESSION_CHECKS_PASS");
        }
        static void RunChineseRegressionChecks()
        {
            SwanUnderwater.EditorTests.OceanCampaignAvailabilityTests.RunAll();
            SwanUnderwater.EditorTests.OceanPlayerContentBuildChecks.RunAll();
            SwanUnderwater.EditorTests.WaterStrokeTrajectoryTests.RunAll();
            SwanUnderwater.EditorTests.SingleHandTurnTests.RunAll();
            SwanUnderwater.EditorTests.ControllerSnapTurnTests.RunAll();
            SwanUnderwater.EditorTests.OceanEntrySequenceTests.RunAll();
            SwanUnderwater.EditorTests.OceanEntryLifecycleTests.RunAll();
            SwanUnderwater.EditorTests.OceanEntryPlaybackTests.RunAll();
            SwanUnderwater.EditorTests.TreasureCollectionTests.RunAll();
            SwanUnderwater.EditorTests.ShellGrabGestureTests.RunAll();
            SwanUnderwater.EditorTests.SwimInteractionRecoveryTests.RunAll();
            SwanUnderwater.EditorTests.HandSwimMovementTests.RunAll();
            SwanUnderwater.EditorTests.ReefSegmentGeometryTests.RunAll();
            SwanUnderwater.EditorTests.ReefLodChecks.RunPolicyTests();
            SwanUnderwater.EditorTests.OceanReefShadowChecks.RunAll();
            SwanUnderwater.EditorTests.OceanButterflyNearChecks.RunAll();
            SwanUnderwater.EditorTests.OceanSharkPatrolTests.RunAll();
            SwanUnderwater.EditorTests.OceanGuardianCompositionTests.RunAll();
            SwanUnderwater.EditorTests.HeadRelocationTests.RunAll();
            SwanUnderwater.EditorTests.OceanMenuPinchTests.RunAll();
            SwanUnderwater.EditorTests.OceanHandTrackingTests.RunAll();
            SwanUnderwater.EditorTests.OceanMenuPressGateTests.RunAll();
            SwanUnderwater.EditorTests.OceanSpearTriggerTests.RunAll();
            SwanUnderwater.EditorTests.RenderedSeabedSegmentTests.RunAll();
            if (HasArgument("-codexSeabedCacheTiming"))
                SwanUnderwater.EditorTests.OceanRenderedSeabedCacheChecks.RunAll();
            else SwanUnderwater.EditorTests.OceanRenderedSeabedCacheChecks.Validate();
            SwanUnderwater.EditorTests.OceanCampaignTests.RunAll();
            SwanUnderwater.EditorTests.OceanEnvironmentTransitionTests.RunAll();
            SwanUnderwater.EditorTests.OceanMapPassageTests.RunAll();
            SwanUnderwater.EditorTests.OceanMeshCollisionTests.RunAll();
            SwanUnderwater.Editor.OceanFishVisualChecks.Run();
            SwanUnderwater.EditorTests.OceanSchoolFishMotionChecks.RunAll();
            SwanUnderwater.EditorTests.OceanNearFishMotionChecks.RunAll();
            SwanUnderwater.EditorTests.OceanCreatureResponseChecks.RunAll();
            SwanUnderwater.EditorTests.OceanTutorialExitChecks.RunAll();
            SwanUnderwater.EditorTests.OceanMarineResidentChecks.CheckNativeSteering();
            SwanUnderwater.EditorTests.OceanBoidTransformCacheTests.RunAll();
            SwanUnderwater.EditorTests.OceanDiagnosticLogTests.RunAll();
            SwanUnderwater.EditorTests.OceanNaturalistIntegrationChecks.RunAll();
            SwanUnderwater.EditorTests.OceanMarketMaterialChecks.RunAll();
            OceanMarineExpansionBake.ValidatePublishedAssets();
            SwanUnderwater.EditorTests.OceanMackerelVolumeBake.ValidatePublishedAssets();
            OceanButterflyFishBake.ValidatePublishedAssets();
            OceanMarineCompanionBake.ValidatePublishedAssets();
            SwanUnderwater.EditorTests.OceanResidentDensityTests.RunAll();
            SwanUnderwater.EditorTests.OceanSharkMotionTests.RunAll();
            SwanUnderwater.EditorTests.OceanCampaignGuideTests.RunAll();
            SwanUnderwater.EditorTests.OceanCampaignProgressRefreshTests.RunAll();
            if (OceanReleaseFeatures.LeaderboardsEnabled) SwanUnderwater.EditorTests.OceanLeaderboardTests.RunAll();
            else SwanUnderwater.EditorTests.OceanLeaderboardDisabledChecks.RunAll();
            SwanUnderwater.EditorTests.OceanPublicExportChecks.RunAll();
            SwanUnderwater.EditorTests.OceanSlimFontChecks.RunAll();
            SwanUnderwater.EditorTests.OceanCampaignCreatureTests.RunAll();
            SwanUnderwater.EditorTests.OceanWaterOpticsTests.RunAll();
            SwanUnderwater.EditorTests.OceanCampaignSyncTests.RunAll();
            SwanUnderwater.EditorTests.OceanCampaign100UiTests.RunAll();
            SwanUnderwater.EditorTests.OceanAchievementTests.RunAll();
            SwanUnderwater.EditorTests.OceanAchievementProgressTests.RunAll();
            SwanUnderwater.EditorTests.OceanAchievementControllerTests.RunAll();
            SwanUnderwater.EditorTests.OceanAchievementUiTests.RunAll();
            SwanUnderwater.EditorTests.OceanSaveRecoveryTests.RunAll();
            SwanUnderwater.EditorTests.OceanComfortTutorialTests.RunAll();
            SwanUnderwater.EditorTests.OceanComfortVisualTests.RunAll();
            SwanUnderwater.EditorTests.OceanAudioPreferencesTests.RunAll();
            SwanUnderwater.EditorTests.OceanCampaignBiomeTests.RunAll();
            SwanUnderwater.EditorTests.OceanMechanicRulesTests.RunAll();
            SwanUnderwater.EditorTests.OceanFirstTwentyActivityTests.RunAll();
            SwanUnderwater.EditorTests.OceanTransientGateChecks.RunAll();
            SwanUnderwater.EditorTests.OceanActivityReadabilityChecks.RunAll();
            SwanUnderwater.EditorTests.OceanMenuClickFeedbackTests.RunAll();
            SwanUnderwater.EditorTests.OceanMechanicSceneTests.RunAll();
            SwanUnderwater.EditorTests.OceanInstalledSaveChecks.RunIfRequested();
        }

        public static void BuildAndroid()
        {
            bool quest = QuestBuild.Active;
            string targetPlatform = Argument("-codexTargetPlatform", quest ? "quest3" : "pico");
            if (targetPlatform != (quest ? "quest3" : "pico"))
                throw new BuildFailedException("Quest builds must enter through QuestBuild.BuildAndroid; PICO uses CodexBuild.BuildAndroid.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("Stop Play Mode before building.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new BuildFailedException("Select Android first, or launch Unity with -buildTarget Android. No in-method target switch is performed.");
            if (!File.Exists(ScenePath)) throw new BuildFailedException("Missing existing scene: " + ScenePath);
            RunRegressionChecks();
            if (PlayerSettings.SplashScreen.show || PlayerSettings.SplashScreen.showUnityLogo ||
                PlayerSettings.virtualRealitySplashScreen != null ||
                (!quest && !ByteDance.PICO.XR.PXR_ProjectSetting.GetProjectConfig().videoSeeThrough) ||
                Resources.Load<OceanEntryAssets>("Loading/OceanEntryAssets") == null ||
                Resources.Load<Font>("Loading/OceanTitleFont") == null)
                throw new BuildFailedException("Apply StartupSetup.Apply before building; expected VST, calligraphic title font and no Unity splash/poster.");
            if (quest) QuestBuild.ValidatePrepared();
            foreach (string name in new[] {"02_blue_tang", "23_mandarinfish", "01_clownfish"})
                if (Resources.Load<GameObject>("SchoolFish/" + name) == null)
                    throw new BuildFailedException("Missing dedicated schooling mesh: " + name);
            foreach (string name in new[] {"coral_cuneata", "coral_spicifera", "coral_brain", "coral_palmata"})
                for (int lod = 0; lod < 3; lod++)
                    if (Resources.Load<GameObject>("RealisticReef/" + name + "_lod" + lod) == null)
                        throw new BuildFailedException("Missing coral LOD: " + name + "_lod" + lod);
            if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty)
                throw new BuildFailedException("Save scene changes first; the build consumes the existing saved scene.");
            if (EditorUserBuildSettings.buildAppBundle)
                throw new BuildFailedException("APK build requested but Build App Bundle is enabled. Select APK explicitly before building.");

            int versionCode = int.Parse(Argument("-codexVersionCode", "34"), CultureInfo.InvariantCulture);
            if (versionCode < 1) throw new BuildFailedException("Version code must be positive.");
            string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string release = Argument("-codexReleaseVersion", "1.0." + versionCode);
            if (!System.Text.RegularExpressions.Regex.IsMatch(release, @"^\d+\.\d+\.\d+$"))
                throw new BuildFailedException("Release version must have major.minor.patch format.");
            string version = release + "-codex." + stamp;
            string outputDir = Path.GetFullPath(Path.Combine("Builds", "codex-v34", stamp));
            Directory.CreateDirectory(outputDir);
            string apk = Path.Combine(outputDir, (quest ? "OceanWorld-Quest3-" : "SwanUnderwaterWorld-") + version + ".apk");
            var info = new BuildData { utc = DateTime.UtcNow.ToString("O"), version = version, versionCode = versionCode,
                unity = Application.unityVersion, scene = ScenePath, sceneSha256 = HashFile(ScenePath), apk = apk,
                applicationIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
                development = HasArgument("-codexDevelopment"),
                graphicsApis = string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)),
                scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android).ToString(),
                architecture = PlayerSettings.Android.targetArchitectures.ToString(), targetPlatform = targetPlatform, result = "Started" };
            string manifest = Path.Combine(outputDir, "build-manifest.json");
            File.WriteAllText(manifest, JsonUtility.ToJson(info, true));
            string previousVersion = PlayerSettings.bundleVersion;
            int previousCode = PlayerSettings.Android.bundleVersionCode;
            bool previousLog = PlayerSettings.usePlayerLog;
            bool previousTiming = PlayerSettings.enableFrameTimingStats;
            bool previousExportProject = EditorUserBuildSettings.exportAsGoogleAndroidProject;
            var previousPreloadedAssets = PlayerSettings.GetPreloadedAssets();
            try
            {
                // This entry point promises an APK. A cached VibeLab/Gradle export
                // preference must not turn the .apk output path into a directory.
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                PlayerSettings.bundleVersion = version;
                PlayerSettings.Android.bundleVersionCode = versionCode;
                PlayerSettings.usePlayerLog = true;
                PlayerSettings.enableFrameTimingStats = true;
                Debug.Log("CODEX_BUILD_STARTED: " + JsonUtility.ToJson(info));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, locationPathName = apk,
                    target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.DetailedBuildReport | (info.development ? BuildOptions.Development : BuildOptions.None)
                });
                info.result = report.summary.result.ToString();
                info.errors = report.summary.totalErrors;
                info.warnings = report.summary.totalWarnings;
                info.bytes = report.summary.totalSize.ToString(CultureInfo.InvariantCulture);
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"Optimized Android build {report.summary.result}; errors={report.summary.totalErrors}");
                if (!File.Exists(apk) || Directory.Exists(apk))
                    throw new BuildFailedException("APK build did not produce a regular installation file: " + apk);
                SwanUnderwater.EditorTests.OceanPlayerContentBuildChecks.ValidatePackedAssets(report, outputDir);
                var reefAssets = new System.Text.StringBuilder("packed_type\tbytes\tsource\n");
                long reefTextureBytes = 0; int bakedTextures = 0, legacyBakedTextures = 0, originalTextures = 0;
                var packedMarketTextures = new HashSet<string>(StringComparer.Ordinal);
                var packedMarineTextures = new HashSet<string>(StringComparer.Ordinal);
                long marineTextureBytes = 0;
                var packedMarineTextureBytes = new Dictionary<string,long>(StringComparer.Ordinal);
                var marineAssets = new System.Text.StringBuilder("packed_type\tbytes\tsource\n");
                foreach (var packed in report.packedAssets)
                    foreach (var asset in packed.contents)
                    {
                        string path = asset.sourceAssetPath;
                        if (path != null && (path.StartsWith("Assets/ArtBaked/MarineExpansion/", StringComparison.Ordinal) ||
                            path.StartsWith("Assets/ArtSources/MarineExpansion/", StringComparison.Ordinal) ||
                            path.StartsWith("Assets/ArtBaked/MarineCompanions/", StringComparison.Ordinal) ||
                            path.StartsWith("Assets/ArtSources/MarineCompanions/", StringComparison.Ordinal)))
                        {
                            marineAssets.Append(asset.type).Append('\t').Append(asset.packedSize).Append('\t').Append(path).Append('\n');
                            if (asset.type == typeof(Texture2D))
                            {
                                if (!(path.StartsWith("Assets/ArtBaked/MarineExpansion/", StringComparison.Ordinal) ||
                                    path.StartsWith("Assets/ArtBaked/MarineCompanions/", StringComparison.Ordinal)))
                                    throw new BuildFailedException("Original marine source image leaked into player: " + path);
                                packedMarineTextures.Add(path); marineTextureBytes += (long)asset.packedSize;
                                packedMarineTextureBytes.TryGetValue(path,out long prior);
                                packedMarineTextureBytes[path]=prior+(long)asset.packedSize;
                            }
                        }
                        if (path == null || !(path.StartsWith("Assets/ArtBaked/ReefAstc/", StringComparison.Ordinal) ||
                            path.StartsWith("Assets/ArtSources/RealisticReef/", StringComparison.Ordinal) ||
                            path.StartsWith("Assets/ArtBaked/MarketReef/", StringComparison.Ordinal) ||
                            path.StartsWith("Assets/ArtSources/MarketReef/", StringComparison.Ordinal) ||
                            path.StartsWith("Assets/Resources/RealisticReef/", StringComparison.Ordinal))) continue;
                        reefAssets.Append(asset.type).Append('\t').Append(asset.packedSize).Append('\t').Append(path).Append('\n');
                        if (asset.type == typeof(Texture2D))
                        {
                            reefTextureBytes += (long)asset.packedSize;
                            if (path.StartsWith("Assets/ArtBaked/ReefAstc/", StringComparison.Ordinal))
                            { bakedTextures++; legacyBakedTextures++; }
                            else if (path.StartsWith("Assets/ArtBaked/MarketReef/", StringComparison.Ordinal))
                            { bakedTextures++; packedMarketTextures.Add(path); }
                            else originalTextures++;
                        }
                    }
                File.WriteAllText(Path.Combine(outputDir, "reef-packed-assets.tsv"), reefAssets.ToString());
                Debug.Log($"[ReefBuild] textures_baked={bakedTextures} textures_original={originalTextures} packed_texture_bytes={reefTextureBytes}");
                if (Directory.Exists("Assets/ArtBaked/ReefAstc") && (legacyBakedTextures != 48 || originalTextures != 0))
                    throw new BuildFailedException("Reef build must retain all 48 legacy baked textures and no imported original textures.");
                var expectedMarketTextures = new HashSet<string>(StringComparer.Ordinal);
                if (Directory.Exists("Assets/ArtBaked/MarketReef"))
                    foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/ArtBaked/MarketReef" }))
                        expectedMarketTextures.Add(AssetDatabase.GUIDToAssetPath(guid));
                if (!packedMarketTextures.SetEquals(expectedMarketTextures))
                    throw new BuildFailedException("Packed market textures must exactly match the admitted native bake assets.");
                Debug.Log("[MarketReefBuild] exact admitted textures=" + expectedMarketTextures.Count +
                    "; source ASTC/explicit lossless fan MASK quality verified by the bake and Resources gates.");
                File.WriteAllText(Path.Combine(outputDir, "marine-packed-assets.tsv"), marineAssets.ToString());
                // Independent per-species inventory/budgets preserve the old mackerel
                // gate while admitting exactly the three newly validated packages.
                CheckPackedMarineTextures(packedMarineTextureBytes,"Assets/ArtBaked/MarineExpansion/mackerel",2,3500000);
                CheckPackedMarineTextures(packedMarineTextureBytes,"Assets/ArtBaked/MarineExpansion/butterfly-fish",3,9000000);
                CheckPackedMarineTextures(packedMarineTextureBytes,"Assets/ArtBaked/MarineCompanions/sea-turtle",3,68000000);
                CheckPackedMarineTextures(packedMarineTextureBytes,"Assets/ArtBaked/MarineCompanions/dolphin",2,12000000);
                if(packedMarineTextures.Count!=10||marineTextureBytes>92500000)
                    throw new BuildFailedException("Marine packages must contain exactly ten admitted textures within their combined budget.");
                Debug.Log($"[MarineBuild] exact admitted textures={packedMarineTextures.Count}; packedBytes={marineTextureBytes}; no source images.");
                info.apkSha256 = HashFile(apk);
                info.apkFileBytes = new FileInfo(apk).Length.ToString(CultureInfo.InvariantCulture);
                Debug.Log("CODEX_APK_BUILT: " + apk + " sha256=" + info.apkSha256);
            }
            catch (Exception e)
            {
                info.result = "Failed";
                info.failure = e.Message;
                throw;
            }
            finally
            {
                PlayerSettings.bundleVersion = previousVersion;
                PlayerSettings.Android.bundleVersionCode = previousCode;
                PlayerSettings.usePlayerLog = previousLog;
                PlayerSettings.enableFrameTimingStats = previousTiming;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = previousExportProject;
                PlayerSettings.SetPreloadedAssets(previousPreloadedAssets);
                AssetDatabase.SaveAssets();
                File.WriteAllText(manifest, JsonUtility.ToJson(info, true));
                Debug.Log("CODEX_BUILD_MANIFEST: " + manifest);
            }
        }

        static void CheckPackedMarineTextures(Dictionary<string,long> packed,string folder,int count,long budget)
        {
            var expected=new HashSet<string>(StringComparer.Ordinal);
            foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{folder}))expected.Add(AssetDatabase.GUIDToAssetPath(guid));
            var actual=new HashSet<string>(StringComparer.Ordinal);long bytes=0;
            foreach(var entry in packed)if(entry.Key.StartsWith(folder+"/",StringComparison.Ordinal)){actual.Add(entry.Key);bytes+=entry.Value;}
            if(expected.Count!=count||!actual.SetEquals(expected)||bytes<=0||bytes>budget)
                throw new BuildFailedException("Marine texture inventory/budget mismatch: "+folder+" expected="+count+" actual="+actual.Count+" bytes="+bytes+" budget="+budget);
            Debug.Log("[MarineBuild] "+folder+" textures="+actual.Count+" packedBytes="+bytes);
        }

        // Editor-only comparison: creates its own untracked camera and never changes Camera.main/XR Origin.
        [MenuItem("Swan/Codex/Capture fixed viewpoints (Editor Play Mode)")]
        public static void CaptureFixedViews()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Editor Play Mode and allow the scene to initialize first.");
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            if (displays.Exists(display => display.running))
                throw new InvalidOperationException("Editor comparison capture refuses an active XR display. Use a separate non-XR Editor preview.");
            Camera source = Camera.main;
            if (source == null) throw new InvalidOperationException("Scene has no main camera.");
            Debug.Log($"[FogCapture] active={RenderSettings.fog} mode={RenderSettings.fogMode} density={RenderSettings.fogDensity} exp2Keyword={Shader.IsKeywordEnabled("FOG_EXP2")} params={Shader.GetGlobalVector("unity_FogParams")} color={Shader.GetGlobalColor("unity_FogColor")}");
            string[] position = Argument("-codexEyePosition", "0,-0.6,-4").Split(',');
            if (position.Length != 3) throw new ArgumentException("-codexEyePosition needs x,y,z in world metres.");
            Vector3 eye = new Vector3(float.Parse(position[0], CultureInfo.InvariantCulture),
                float.Parse(position[1], CultureInfo.InvariantCulture), float.Parse(position[2], CultureInfo.InvariantCulture));
            string dir = Path.GetFullPath(Path.Combine(Argument("-codexValidationRoot", "Validation/iteration-2"), "editor", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")));
            Directory.CreateDirectory(dir);
            SeabedOverlapAudit.AuditOldLayout(dir);
            SeabedOverlapAudit.AuditCurrentMeshes(dir);
            SurfaceContactValidation.Capture(dir);
            var host = new GameObject("Codex untracked comparison camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = host.AddComponent<Camera>();
            camera.CopyFrom(source);
            camera.enabled = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.fieldOfView = 80f;
            camera.aspect = 16f / 9f;
            camera.transform.position = eye;
            camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            string[] names = { "front", "right", "back", "left", "up15", "down15", "up55", "up85" };
            Vector3[] angles = { Vector3.zero, new Vector3(0, 90, 0), new Vector3(0, 180, 0),
                new Vector3(0, 270, 0), new Vector3(-15, 0, 0), new Vector3(15, 0, 0), new Vector3(-55,0,0), new Vector3(-85,0,0) };
            try
            {
                if (PerformanceHud.Instance != null) PerformanceHud.Instance.SetEditorPreviewCamera(camera);
                target.Create();
                camera.targetTexture = target;
                for (int i = 0; i < names.Length; i++)
                {
                    camera.transform.rotation = Quaternion.Euler(angles[i]);
                    if (GraphicsSettings.currentRenderPipeline != null)
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    else camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(Path.Combine(dir, names[i] + ".png"), pixels.EncodeToPNG());
                }
                string detailDir = Path.Combine(dir, "details");
                Directory.CreateDirectory(detailDir);
                Action<string, Vector3, Vector3> detail = (name, position, focus) =>
                {
                    camera.fieldOfView = 50f;
                    camera.transform.position = position;
                    camera.transform.rotation = Quaternion.LookRotation(focus - position, Vector3.up);
                    if (GraphicsSettings.currentRenderPipeline != null)
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    else camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(Path.Combine(detailDir, name + ".png"), pixels.EncodeToPNG());
                };
                var shells = Object.FindFirstObjectByType<SeabedShells>();
                if (shells != null && shells.Placements.Count > 0)
                {
                    Vector3 shell = shells.Placements[0].center;
                    detail("shell-closeup", shell + new Vector3(-0.42f, 0.48f, -0.75f), shell);
                }
                detail("left-reef-contact", new Vector3(-0.5f, -1.95f, -4.1f), new Vector3(-3.2f, -2.6f, -2.7f));
                detail("right-reef-contact", new Vector3(0.1f, -1.8f, -3.6f), new Vector3(3.6f, -2.55f, -0.9f));
                File.WriteAllText(Path.Combine(dir, "capture.txt"),
                    $"Editor-only monocular comparison, NOT headset validation.\nScene: {source.gameObject.scene.path}\n" +
                    $"Fixed eye metres: {eye}\nVertical FOV: 80\nResolution: 1920x1080\nPlay time: {Time.time:F3}\n" +
                    "Views: front/right/back/left/up15/down15/up55/up85. Main camera and XR Origin are unchanged.\n");
                Debug.Log("CODEX_EDITOR_CAPTURES: " + dir);
                if (SessionState.GetBool(PreviewKey + "active", false)) SessionState.SetString(PreviewKey + "output", dir);
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(target);
                if (PerformanceHud.Instance != null) PerformanceHud.Instance.SetEditorPreviewCamera(null);
                Object.DestroyImmediate(host);
            }
        }

        private static bool HasArgument(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
        private static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0) return fallback;
            if (index + 1 >= args.Length) throw new ArgumentException("Missing value for " + name);
            return args[index + 1];
        }
        private static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        [Serializable] private sealed class BuildData
        {
            public string utc, unity, version, scene, sceneSha256, applicationIdentifier, graphicsApis,
                scriptingBackend, architecture, targetPlatform, apk, apkSha256, bytes, apkFileBytes, result, failure;
            public int versionCode, errors, warnings;
            public bool development;
        }
    }
}

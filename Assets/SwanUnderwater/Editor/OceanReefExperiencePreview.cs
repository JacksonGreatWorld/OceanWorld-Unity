#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Separate graphical batch Editor: production entry/world, explicit Mobile URP, isolated campaign files.</summary>
    [InitializeOnLoad]
    public static class OceanReefExperiencePreview
    {
        const string Key = "OceanReefExperience.", ScenePath = "Assets/Scenes/UnderwaterWorld.unity";
        const string MobilePath = "Assets/Settings/Mobile_RPAsset.asset";
        const int Width = 1280, Height = 720;
        static UniversalRenderPipelineAsset mobile;
        static RenderTexture target;
        static Texture2D pixels;
        static GameObject cameraHost;
        static string pendingFailure;
        static bool finishing, exercised;
        static double readyAt = -1, nextSubmitAt, previousSubmitAt = -1;
        static int lastSubmittedFrame = -1;
        static Report report = new Report();
        static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        [Serializable] sealed class ViewRecord
        {
            public string file, reefGroup;
            public Vector3 position, focus, euler;
            public float verticalFov, groundHeight;
        }
        [Serializable] sealed class Report
        {
            public bool success, worldReady, entryDisposed, mainPoseUnchanged, qualityRestored;
            public bool editorUploadGuardVerified, noPageRequested, networkAttemptObserved;
            public bool patrolPassed, questEscapePassed, shadowsPassed;
            public int mainCameraSubmissions, patrolAssertions, questEscapeAssertions, shadowAssertions;
            public int skippedSameFrameTicks, skippedRateLimitedTicks, firstSubmittedFrame = -1, lastSubmittedFrame = -1;
            public int originalQualityIndex = -1, restoredQualityIndex = -1;
            public int width = Width, height = Height, qualityIndex, msaa, shadowCascades, shadowAtlas;
            public float renderScale, shadowDistance, minimumQuestEscapeDistance;
            public float largestSharkMinDistance, smallerSharkMinDistance, largestSharkTravel, patrolMaxStep, longestPatrolStopSeconds;
            public float largestSharkEnvelopeDiameter;
            public string largestSharkName;
            public double elapsedSeconds;
            public double minimumSubmitIntervalSeconds;
            public string scene, pipelineSource = MobilePath, pipelineSourceGuid, runtimePipeline, qualityName;
            public string detail, saveDirectory;
            public string originalQualityName, restoredQualityName, originalPipelinePath, restoredPipelinePath;
            public string restoredMobilePipelinePath, qualityRestorationPhase;
            public string scope = "Actual production scene and entry in a non-XR Editor Play session, Mobile URP runtime clone, real reef meshes/materials/lights/fog. Camera-only monocular 1280x720 images; close-up positions and shark test time/hits are synthetic. Not headset tracking, stereo quality, device performance or player comfort acceptance. No scene/assets are saved. Public offline client has no transport; no leaderboard UI/page request is invoked.";
            public List<ViewRecord> screenshots = new List<ViewRecord>();
            public List<string> groundingDiagnostics = new List<string>();
            public List<string> errors = new List<string>();
        }
        static OceanReefExperiencePreview()
        {
            if (SessionState.GetBool(Key + "active", false))
            {
                finishing = SessionState.GetBool(Key + "finishing", false);
                if (finishing) report = JsonUtility.FromJson<Report>(SessionState.GetString(Key + "finishReport", "{}"));
                Attach();
            }
        }

        // -executeMethod SwanUnderwater.Editor.OceanReefExperiencePreview.Capture
        // Required: -codexCampaignSaveDir <fresh private folder>. Omit -quit and -nographics.
        public static void Capture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate graphical batch Editor.");
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start in Edit Mode.");
                string output = Path.GetFullPath("Validation/iteration-27/reef-experience-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
                string save = null;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Omit -quit/-nographics: preview renders and owns process exit.");
                    if (args[i] == "-codexReefExperienceOutput") output = Path.GetFullPath(Value(args, ref i));
                    else if (args[i] == "-codexCampaignSaveDir") save = Path.GetFullPath(Value(args, ref i));
                }
                if (save == null || SameDirectory(save, Application.persistentDataPath))
                    throw new ArgumentException("A separate -codexCampaignSaveDir is required.");
                if (Directory.Exists(save) && Directory.GetFileSystemEntries(save).Length != 0)
                    throw new IOException("Campaign preview directory must be empty; existing player/fixture files are refused.");
                if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length != 0)
                    throw new IOException("Use an empty preview output directory.");
                if (SameDirectory(output, save)) throw new ArgumentException("Keep output and campaign directories separate.");
                Directory.CreateDirectory(output); Directory.CreateDirectory(save);
                SessionState.SetString(Key + "output", output); SessionState.SetString(Key + "save", save);
                SessionState.SetString(Key + "started", DateTime.UtcNow.Ticks.ToString());
                SessionState.SetString(Key + "clone", "Reef Experience Mobile Preview " + Guid.NewGuid().ToString("N"));
                SessionState.SetInt(Key + "originalQuality", QualitySettings.GetQualityLevel());
                SessionState.SetString(Key + "originalQualityName", QualitySettings.names[QualitySettings.GetQualityLevel()]);
                SessionState.SetString(Key + "originalPipeline", AssetDatabase.GetAssetPath(QualitySettings.renderPipeline));
                SessionState.SetInt(Key + "mobileQuality", FindMobileQuality());
                SessionState.SetBool(Key + "active", true); Attach();
                RejectXR(); EnsureMobile();
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }
        static string Value(string[] args, ref int index)
        {
            if (++index >= args.Length || args[index].StartsWith("-")) throw new ArgumentException("Missing preview argument value.");
            return args[index];
        }
        static bool SameDirectory(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        static int FindMobileQuality()
        {
            for (int i = 0; i < QualitySettings.names.Length; i++) if (QualitySettings.names[i] == "Mobile") return i;
            throw new InvalidOperationException("Expected named Mobile quality tier.");
        }
        static void Attach()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged -= PlayState; EditorApplication.playModeStateChanged += PlayState;
            Application.logMessageReceived -= Log;
            if (!finishing) Application.logMessageReceived += Log;
        }
        static void PlayState(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            if (finishing)
            {
                if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key + "enteredEditMode", true);
                return;
            }
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                try { EnsureMobile(); } catch (Exception error) { pendingFailure = error.ToString(); }
            }
        }
        static void EnsureMobile()
        {
            int mobileIndex = SessionState.GetInt(Key + "mobileQuality", -1);
            if (mobileIndex < 0) throw new InvalidOperationException("Mobile quality state was lost.");
            if (mobile == null)
            {
                string cloneName = SessionState.GetString(Key + "clone", "");
                foreach (var existing in Resources.FindObjectsOfTypeAll<UniversalRenderPipelineAsset>())
                    if (existing.name == cloneName) { mobile = existing; break; }
                if (mobile == null)
                {
                    var source = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobilePath);
                    if (source == null) throw new InvalidOperationException("Missing Mobile pipeline asset.");
                    mobile = Object.Instantiate(source); mobile.name = cloneName; mobile.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            QualitySettings.SetQualityLevel(mobileIndex, true);
            if (!SessionState.GetBool(Key + "mobileOriginalCaptured", false))
            {
                SessionState.SetString(Key + "mobileOriginalPipeline", AssetDatabase.GetAssetPath(QualitySettings.renderPipeline));
                SessionState.SetBool(Key + "mobileOriginalCaptured", true);
            }
            QualitySettings.renderPipeline = mobile;
        }
        static void VerifyMobile()
        {
            if (QualitySettings.GetQualityLevel() != SessionState.GetInt(Key + "mobileQuality", -1) || QualitySettings.renderPipeline != mobile)
                throw new InvalidOperationException("Runtime changed away from the explicit Mobile preview pipeline.");
            if (GraphicsSettings.currentRenderPipeline != mobile)
                throw new InvalidOperationException("Effective runtime render pipeline is not the Mobile clone.");
        }
        static void RejectXR()
        {
            displays.Clear(); SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays) if (display.running) throw new InvalidOperationException("Preview refuses an active XR display.");
            if (XRSettings.isDeviceActive) throw new InvalidOperationException("Preview refuses an active XR device.");
        }
        static void Log(string message, string stack, LogType kind)
        {
            if (kind == LogType.Exception || kind == LogType.Assert) pendingFailure = message + "\n" + stack;
            if (kind == LogType.Error && report.errors.Count < 20) report.errors.Add(message);
            if (message.StartsWith("[OceanLeaderboard] upload_attempted", StringComparison.Ordinal) ||
                message.StartsWith("[OceanLeaderboard] page_success", StringComparison.Ordinal) ||
                message.StartsWith("[OceanLeaderboard] page_failure", StringComparison.Ordinal))
            { report.networkAttemptObserved = true; pendingFailure = "Unexpected leaderboard network operation during isolated Editor preview."; }
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            if (finishing)
            {
                // Exiting Play Mode reloads this class. SessionState retains both the result and restoration request.
                bool leftPlay = !SessionState.GetBool(Key + "needsPlayExit", false) || SessionState.GetBool(Key + "enteredEditMode", false);
                if (leftPlay && !EditorApplication.isPlayingOrWillChangePlaymode) CompleteFinish();
                else if (long.TryParse(SessionState.GetString(Key + "finishStarted", "0"), out long ticks) &&
                    DateTime.UtcNow.Ticks - ticks > TimeSpan.FromSeconds(30).Ticks)
                {
                    report.success = false; report.detail += "\nTimed out leaving Play Mode; quality restoration was not verified.";
                    ExitWithReport();
                }
                return;
            }
            try
            {
                if (pendingFailure != null) throw new InvalidOperationException(pendingFailure);
                if (Elapsed() > 300) throw new TimeoutException("Reef experience preview exceeded 300 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (Time.frameCount == lastSubmittedFrame) { report.skippedSameFrameTicks++; return; }
                double now = EditorApplication.timeSinceStartup;
                if (now < nextSubmitAt) { report.skippedRateLimitedTicks++; return; }
                if (mobile == null) EnsureMobile(); VerifyMobile(); RejectXR();
                var view = Camera.main; if (view == null) return;
                if (target == null)
                {
                    target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Reef experience temporary RT", hideFlags = HideFlags.HideAndDontSave };
                    target.Create(); pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                }
                // At most once per Unity frame and 20 Hz. Editor update can run many times without a new game frame.
                // Continue through opaque Warp: the entry acknowledgement deliberately rejects auxiliary cameras.
                lastSubmittedFrame = Time.frameCount; nextSubmitAt = now + .05;
                if (previousSubmitAt >= 0)
                {
                    double interval = now - previousSubmitAt;
                    report.minimumSubmitIntervalSeconds = report.minimumSubmitIntervalSeconds == 0 ? interval : Math.Min(report.minimumSubmitIntervalSeconds, interval);
                }
                previousSubmitAt = now;
                if (report.firstSubmittedFrame < 0) report.firstSubmittedFrame = lastSubmittedFrame;
                report.lastSubmittedFrame = lastSubmittedFrame;
                Submit(view); report.mainCameraSubmissions++;
                if (OceanEntry.Instance != null)
                {
                    if (OceanEntry.Instance.HasFailed) throw new InvalidOperationException("Production ocean entry failed.");
                    return;
                }
                var world = Object.FindFirstObjectByType<UnderwaterWorld>();
                if (world == null || !world.IsReady) return;
                if (readyAt < 0) { readyAt = EditorApplication.timeSinceStartup; return; }
                if (EditorApplication.timeSinceStartup - readyAt < .25 || exercised) return;
                exercised = true; Exercise(world, view);
                Finish(true, "Actual entry completed; Mobile scene screenshots and ambient patrol, quest escape and built reef shadow checks passed.");
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }
        static void Exercise(UnderwaterWorld world, Camera view)
        {
            VerifyMobile(); RejectXR();
            if (view.gameObject.scene.path != ScenePath) throw new InvalidOperationException("Unexpected scene.");
            report.scene = view.gameObject.scene.path; report.worldReady = world.IsReady; report.entryDisposed = OceanEntry.Instance == null;
            report.pipelineSourceGuid = AssetDatabase.AssetPathToGUID(MobilePath); report.runtimePipeline = GraphicsSettings.currentRenderPipeline.name;
            report.qualityIndex = QualitySettings.GetQualityLevel(); report.qualityName = QualitySettings.names[report.qualityIndex];
            report.msaa = mobile.msaaSampleCount; report.renderScale = mobile.renderScale; report.shadowDistance = mobile.shadowDistance;
            report.shadowCascades = mobile.shadowCascadeCount; report.shadowAtlas = mobile.mainLightShadowmapResolution;
            string clientSource = File.ReadAllText("Assets/SwanUnderwater/OceanLeaderboardClient.cs");
            report.editorUploadGuardVerified = clientSource.Contains("public bool OfflineOnly => true;") &&
                !clientSource.Contains("UnityWebRequest") && !clientSource.Contains("HttpClient") &&
                typeof(OceanLeaderboardClient).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) == null;
            if (!report.editorUploadGuardVerified) throw new InvalidOperationException("Public offline transport exclusion changed; review before preview.");
            report.noPageRequested = true;
            foreach (var client in Object.FindObjectsByType<OceanLeaderboardClient>(FindObjectsSortMode.None))
                if (client.PageRequestVersion != 0 || client.IsBusy) report.noPageRequested = false;
            if (!report.noPageRequested || report.networkAttemptObserved) throw new InvalidOperationException("Preview must not request online leaderboard data.");

            var reef = world.GetComponentInChildren<ReefProximityLod>();
            if (reef == null || reef.RegisteredCount == 0) throw new InvalidOperationException("Actual generated reef is missing.");
            Vector3 originalPosition = view.transform.position; Quaternion originalRotation = view.transform.rotation;
            cameraHost = new GameObject("Temporary reef experience untracked camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraHost.AddComponent<Camera>(); camera.CopyFrom(view); camera.scene = view.scene;
            camera.enabled = false; camera.tag = "Untagged"; camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.aspect = (float)Width / Height; camera.nearClipPlane = .03f; camera.useOcclusionCulling = false;
            var sourceData = view.GetUniversalAdditionalCameraData(); var data = camera.GetUniversalAdditionalCameraData();
            data.allowXRRendering = false; data.renderPostProcessing = sourceData.renderPostProcessing;
            data.volumeLayerMask = sourceData.volumeLayerMask; data.volumeTrigger = camera.transform;
            try
            {
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                Save(camera, "01-spawn-front.png", "", originalPosition + view.transform.forward, reef);
                CloseReef(camera, view, reef, -1, "02-left-reef-ground.png");
                CloseReef(camera, view, reef, 1, "03-right-reef-ground.png");
            }
            finally { reef.EvaluateForValidation(originalPosition); Object.DestroyImmediate(cameraHost); cameraHost = null; }

            EditorTests.OceanSharkPatrolTests.RunAll();
            EditorTests.OceanSharkPatrolTests.RunInScene(world, view);
            report.patrolAssertions = EditorTests.OceanSharkPatrolTests.LastAssertionCount; report.patrolPassed = true;
            report.largestSharkMinDistance = EditorTests.OceanSharkPatrolTests.SceneLargestMinDistance;
            report.smallerSharkMinDistance = EditorTests.OceanSharkPatrolTests.SceneSmallestMinDistance;
            report.largestSharkTravel = EditorTests.OceanSharkPatrolTests.SceneLargestTravel;
            report.patrolMaxStep = EditorTests.OceanSharkPatrolTests.SceneMaxStep;
            report.longestPatrolStopSeconds = EditorTests.OceanSharkPatrolTests.SceneLongestStopSeconds;
            report.largestSharkName = EditorTests.OceanSharkPatrolTests.SceneLargestSharkName;
            report.largestSharkEnvelopeDiameter = EditorTests.OceanSharkPatrolTests.SceneLargestEnvelopeDiameter;
            EditorTests.OceanSharkEscapeTests.RunInScene(world, view);
            report.questEscapeAssertions = EditorTests.OceanSharkEscapeTests.LastAssertionCount;
            report.minimumQuestEscapeDistance = EditorTests.OceanSharkEscapeTests.MinimumEscapeDistance; report.questEscapePassed = true;
            EditorTests.OceanReefShadowChecks.RunAll(); EditorTests.OceanReefShadowChecks.ValidateBuilt(reef);
            report.shadowAssertions = EditorTests.OceanReefShadowChecks.Assertions; report.shadowsPassed = true;
            report.mainPoseUnchanged = view.transform.position == originalPosition && Quaternion.Angle(view.transform.rotation, originalRotation) < .001f;
            if (!report.mainPoseUnchanged) throw new InvalidOperationException("Preview changed the main camera pose.");
            VerifyMobile();
        }
        static void CloseReef(Camera camera, Camera view, ReefProximityLod reef, int side, string file)
        {
            int selected = -1; float nearest = float.PositiveInfinity;
            Vector3 eye = view.transform.position, right = Vector3.ProjectOnPlane(view.transform.right, Vector3.up).normalized;
            for (int i = 0; i < reef.RegisteredCount; i++)
            {
                var candidate = reef.SampleForValidation(i);
                if (candidate.remoteRidge || !candidate.group.name.StartsWith("Coral ", StringComparison.Ordinal)) continue;
                Vector3 delta = candidate.bounds.center - eye;
                if (Vector3.Dot(delta, right) * side < .4f) continue;
                float distance = candidate.bounds.SqrDistance(eye);
                if (distance < nearest) { nearest = distance; selected = i; }
            }
            if (selected < 0) throw new InvalidOperationException("No actual coral on requested side " + side);
            var sample = reef.SampleForValidation(selected); Bounds bounds = sample.bounds;
            Vector3 towardPlayer = Vector3.ProjectOnPlane(eye - bounds.center, Vector3.up).normalized;
            if (towardPlayer.sqrMagnitude < .5f) towardPlayer = -Vector3.forward;
            float floor = UnderwaterWorld.SampleRenderedSeabedHeight(bounds.center.x, bounds.center.z);
            Vector3 focus = new Vector3(bounds.center.x, Mathf.Max(floor + .20f, bounds.min.y + bounds.size.y * .32f), bounds.center.z);
            float distanceOut = Mathf.Max(1.25f, Mathf.Max(bounds.extents.x, bounds.extents.z) + 1.10f);
            Vector3 position = focus + towardPlayer * distanceOut + Vector3.up * .65f;
            position.y = Mathf.Max(position.y, UnderwaterWorld.SampleRenderedSeabedHeight(position.x, position.z) + .75f);
            camera.fieldOfView = 65f; camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
            Save(camera, file, sample.group.name, focus, reef);
            report.groundingDiagnostics.Add(OceanReefGroundingDiagnostic.Capture(reef, selected, camera, file,
                SessionState.GetString(Key + "output", ""), Width, Height));
        }
        static void Save(Camera camera, string file, string group, Vector3 focus, ReefProximityLod reef)
        {
            VerifyMobile(); reef.EvaluateForValidation(camera.transform.position); Submit(camera);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(SessionState.GetString(Key + "output", ""), file), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; }
            report.screenshots.Add(new ViewRecord { file = file, reefGroup = group, position = camera.transform.position,
                focus = focus, euler = camera.transform.eulerAngles, verticalFov = camera.fieldOfView,
                groundHeight = UnderwaterWorld.SampleRenderedSeabedHeight(focus.x, focus.z) });
        }
        static void Submit(Camera camera)
        {
            var data = camera.GetUniversalAdditionalCameraData(); bool xr = data.allowXRRendering;
            try { data.allowXRRendering = false; RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target }); }
            finally { data.allowXRRendering = xr; }
        }
        static double Elapsed()
        {
            if (!long.TryParse(SessionState.GetString(Key + "started", ""), out long ticks)) return 0;
            return (DateTime.UtcNow.Ticks - ticks) / (double)TimeSpan.TicksPerSecond;
        }
        static void Finish(bool success, string detail)
        {
            if (finishing) return; finishing = true;
            report.success = success; report.detail = detail; report.elapsedSeconds = Elapsed();
            report.saveDirectory = SessionState.GetString(Key + "save", "");
            Application.logMessageReceived -= Log;
            try
            {
                if (cameraHost != null) Object.DestroyImmediate(cameraHost);
                if (target != null) { if (RenderTexture.active == target) RenderTexture.active = null; target.Release(); Object.DestroyImmediate(target); }
                if (pixels != null) Object.DestroyImmediate(pixels);
            }
            catch (Exception cleanup) { report.success = false; report.detail += "\nCleanup: " + cleanup; }
            if (!SessionState.GetBool(Key + "active", false)) { ExitWithReport(); return; }
            SessionState.SetBool(Key + "finishing", true);
            SessionState.SetString(Key + "finishStarted", DateTime.UtcNow.Ticks.ToString());
            SessionState.SetString(Key + "finishReport", JsonUtility.ToJson(report));
            // PC is excluded from Android in this project. Do not try to restore its Edit Mode selection in a
            // platform-filtered Play session or write the PC asset into a still-selected Mobile slot.
            bool needsPlayExit = EditorApplication.isPlayingOrWillChangePlaymode;
            SessionState.SetBool(Key + "needsPlayExit", needsPlayExit);
            SessionState.SetBool(Key + "enteredEditMode", !needsPlayExit);
            if (needsPlayExit) EditorApplication.isPlaying = false;
            else CompleteFinish();
        }
        static RenderPipelineAsset LoadPipeline(string key)
        {
            string path = SessionState.GetString(Key + key, "");
            return path.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
        }
        static void CompleteFinish()
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Quality restoration requires Edit Mode.");
                report.qualityRestorationPhase = "EditMode";
                int mobileIndex = SessionState.GetInt(Key + "mobileQuality", -1);
                int originalIndex = SessionState.GetInt(Key + "originalQuality", -1);
                report.originalQualityIndex = originalIndex;
                report.originalQualityName = SessionState.GetString(Key + "originalQualityName", "");
                report.originalPipelinePath = SessionState.GetString(Key + "originalPipeline", "");
                QualitySettings.SetQualityLevel(mobileIndex, true);
                if (QualitySettings.GetQualityLevel() != mobileIndex) throw new InvalidOperationException("Could not select Mobile tier for restoration.");
                QualitySettings.renderPipeline = LoadPipeline("mobileOriginalPipeline");
                QualitySettings.SetQualityLevel(originalIndex, true);
                if (QualitySettings.GetQualityLevel() != originalIndex) throw new InvalidOperationException("Could not restore original Edit Mode quality index " + originalIndex);
                QualitySettings.renderPipeline = LoadPipeline("originalPipeline");
                report.restoredQualityIndex = QualitySettings.GetQualityLevel();
                report.restoredQualityName = QualitySettings.names[report.restoredQualityIndex];
                report.restoredPipelinePath = AssetDatabase.GetAssetPath(QualitySettings.renderPipeline);
                report.restoredMobilePipelinePath = AssetDatabase.GetAssetPath(QualitySettings.GetRenderPipelineAssetAt(mobileIndex));
                report.qualityRestored = report.restoredQualityIndex == originalIndex && report.restoredQualityName == report.originalQualityName &&
                    report.restoredPipelinePath == report.originalPipelinePath &&
                    report.restoredMobilePipelinePath == SessionState.GetString(Key + "mobileOriginalPipeline", "");
                if (!report.qualityRestored) throw new InvalidOperationException("Restored quality/pipeline readback differs from the original Editor state.");
                string cloneName = SessionState.GetString(Key + "clone", "");
                foreach (var asset in Resources.FindObjectsOfTypeAll<UniversalRenderPipelineAsset>())
                    if (asset.name == cloneName) Object.DestroyImmediate(asset);
            }
            catch (Exception cleanup) { report.success = false; report.detail += "\nEdit Mode restoration: " + cleanup; }
            ExitWithReport();
        }
        static void ExitWithReport()
        {
            EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= PlayState; Application.logMessageReceived -= Log;
            SessionState.SetBool(Key + "active", false); SessionState.SetBool(Key + "mobileOriginalCaptured", false);
            SessionState.SetBool(Key + "finishing", false);
            string output = SessionState.GetString(Key + "output", "");
            try { if (output.Length > 0) File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true)); }
            catch (Exception write) { report.success = false; Debug.LogError(write); }
            Debug.Log("OCEAN_REEF_EXPERIENCE_" + (report.success ? "PASS " : "FAIL ") + report.detail);
            EditorApplication.Exit(report.success ? 0 : 1);
        }
    }
}
#endif

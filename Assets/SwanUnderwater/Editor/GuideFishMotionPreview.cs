#if UNITY_EDITOR
using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Owns a separate batch Editor process; records the real intro, never a replacement trajectory.</summary>
    [InitializeOnLoad]
    public static class GuideFishMotionPreview
    {
        const string Key = "SwanGuideMotion.";
        const string Scene = "Assets/Scenes/UnderwaterWorld.unity";
        const int Width = 960, Height = 540, Fps = 20;
        // Name each required beat so inserting another sequence beat cannot silently change coverage.
        const int RequiredStages = (1 << (int)OceanEntrySequence.Beat.Approach) |
            (1 << (int)OceanEntrySequence.Beat.Invite) | (1 << (int)OceanEntrySequence.Beat.Wiggle) |
            (1 << (int)OceanEntrySequence.Beat.Turn) | (1 << (int)OceanEntrySequence.Beat.Dash);
        static Camera camera;
        static IntroGuideFish fish;
        static RenderTexture target;
        static Texture2D pixels;
        static StreamWriter csv;
        static int frames, stageMask, previousFrame = -1;
        static double nextSample;
        static float firstTime = -1f, lastTime;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly List<StartupEvent> startupEvents = new List<StartupEvent>();
        static bool sawBuildGate, sawContentReady, sawReveal, sawWarpWaiting, worldReadyLogged, revealImage;
        static bool controlReadyLogged, sawPlayable;
        static bool sawCoveredAmmoFailure;
        static double failedResultObservedAt = -1;
        static int opaqueSubmissions;
        static string observedStage = "", resourcePath = "unavailable";
        static int triangles;
        static bool authored;
        static Camera syntheticHeadCamera;
        static Quaternion initialHeadRotation;
        static float syntheticHeadYaw, maximumSyntheticHeadYaw;
        static int syntheticHeadTicks;
        static readonly List<UnityEngine.XR.XRDisplaySubsystem> xrDisplays = new List<UnityEngine.XR.XRDisplaySubsystem>();

        [Serializable] sealed class StartupEvent
        {
            public string kind, stage;
            public int frame, opaqueCameraSubmissions;
            public double realtime;
            public float progress, opacity;
            public bool buildAllowed, contentReady, revealed, cameraRestored, playable;
        }
        [Serializable] sealed class StartupResult
        {
            public bool success, buildGateSeen, contentReadySeen, revealSeen, warpWaitingSeen, finalWorldReadyLog, controlReadySeen, playableCaptionSeen;
            public int opaqueCameraSubmissions;
            public string scope, detail;
            public StartupEvent[] events;
        }
        static bool FullFlow => SessionState.GetBool(Key + "fullFlow", false);
        static bool HeadTurn => SessionState.GetBool(Key + "headTurn", false);
        static bool ExpectedAmmoFailure => SessionState.GetBool(Key + "expectedAmmoFailure", false);

        static GuideFishMotionPreview() { if (SessionState.GetBool(Key + "active", false)) Attach(); }

        // -executeMethod SwanUnderwater.Editor.GuideFishMotionPreview.Capture
        // Omit -quit/-nographics; optional -codexMotionOutput <empty directory>.
        // Add -codexGuideDetail for a synchronized side close-up of the actual animated mesh.
        // Add -codexGuideFullFlow to continue past Warp through real world readiness and entry disposal.
        // Add -codexGuideHeadTurn for synthetic main-camera yaw; this is NOT real head tracking.
        public static void Capture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Motion preview requires its own batch Editor process.");
            try
            {
                string[] arguments = Environment.GetCommandLineArgs();
                foreach (string arg in arguments)
                    if (arg == "-quit" || arg == "-nographics") throw new ArgumentException("Omit -quit and -nographics; this preview renders and owns exit.");
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start motion preview in Edit Mode.");
                string output = "Validation/iteration-20/intro/motion-preview";
                bool detail = Array.IndexOf(arguments, "-codexGuideDetail") >= 0;
                bool fullFlow = Array.IndexOf(arguments, "-codexGuideFullFlow") >= 0;
                bool headTurn = Array.IndexOf(arguments, "-codexGuideHeadTurn") >= 0;
                bool expectedAmmoFailure = Array.IndexOf(arguments, "-codexExpectedAmmoFailure") >= 0;
                if (expectedAmmoFailure && !fullFlow) throw new ArgumentException("Ammo restore validation requires -codexGuideFullFlow.");
                for (int i = 0; i + 1 < arguments.Length; i++) if (arguments[i] == "-codexMotionOutput") output = arguments[i + 1];
                output = Path.GetFullPath(output);
                Directory.CreateDirectory(output);
                if (Directory.GetFiles(output, "frame-*.png").Length > 0) throw new IOException("Use an empty motion-preview directory to avoid mixing runs.");
                SessionState.SetString(Key + "output", output);
                SessionState.SetBool(Key + "detail", detail);
                SessionState.SetBool(Key + "fullFlow", fullFlow);
                SessionState.SetBool(Key + "headTurn", headTurn);
                SessionState.SetBool(Key + "expectedAmmoFailure", expectedAmmoFailure);
                if (detail) Directory.CreateDirectory(Path.Combine(output, "detail"));
                SessionState.SetString(Key + "started", DateTime.UtcNow.Ticks.ToString(Inv));
                SessionState.SetString(Key + "exception", "");
                SessionState.SetBool(Key + "active", true);
                Attach();
                ValidateFishImport();
                EditorSceneManager.OpenScene(Scene);
                Debug.Log($"GUIDE_MOTION_STARTED: actual Play Mode intro; neutral background is NOT VST; visible-time 20 fps; fullFlow={fullFlow}; syntheticHeadTurn={headTurn} (NOT real head tracking); wall timeout={(fullFlow ? 180 : 90)}s.");
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }

        static void Attach()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= LogException; Application.logMessageReceived += LogException;
            RenderPipelineManager.endCameraRendering -= AuditCameraSubmission;
            RenderPipelineManager.endCameraRendering += AuditCameraSubmission;
        }
        static void LogException(string message, string trace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Assert) SessionState.SetString(Key + "exception", message + "\n" + trace);
            if (message.StartsWith("[OceanEntry] world_ready ")) worldReadyLogged = true;
            if (message.StartsWith("[OceanEntry] control_ready ")) controlReadyLogged = true;
        }

        static void ValidateFishImport()
        {
            const string path = "Assets/Resources/Fish/01_clownfish.glb";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || Resources.Load<GameObject>("Fish/01_clownfish") != prefab)
                throw new InvalidOperationException("The guide must use the exact ordinary reef Fish/01_clownfish resource.");
            int count=0,materials=0;
            foreach(MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh=filter.sharedMesh;
                if(mesh==null)continue;
                if(!Finite(mesh.bounds.center)||!Finite(mesh.bounds.size)||mesh.bounds.size.sqrMagnitude<=.000001f)
                    throw new InvalidOperationException("Reef clownfish has invalid source mesh bounds.");
                for(int sub=0;sub<mesh.subMeshCount;sub++)
                    if(mesh.GetTopology(sub)==MeshTopology.Triangles)count+=checked((int)(mesh.GetIndexCount(sub)/3));
            }
            foreach(Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach(Material material in renderer.sharedMaterials)if(material!=null)materials++;
            if(count<=0||materials<=0||prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length!=0)
                throw new InvalidOperationException("Expected the ordinary textured static reef mesh, not the replacement skeletal fish.");
            SessionState.SetInt(Key+"expectedTriangles",count);
            Debug.Log($"GUIDE_IMPORT_VALID: resource=Fish/01_clownfish triangles={count} materialSlots={materials} animation=reef_shader");
        }
        static bool Finite(Vector3 p) => !float.IsNaN(p.x)&&!float.IsInfinity(p.x)&&!float.IsNaN(p.y)&&!float.IsInfinity(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.z);

        static void AuditCameraSubmission(ScriptableRenderContext context, Camera submitted)
        {
            if (!FullFlow || submitted != Camera.main) return;
            OceanEntry entry = OceanEntry.Instance;
            if (entry != null && entry.CinematicPhase == OceanEntrySequence.Beat.Warp && entry.PreviewWarpOpacity >= .9999f)
                opaqueSubmissions++;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            try
            {
                long started = long.Parse(SessionState.GetString(Key + "started", "0"), Inv);
                if (DateTime.UtcNow.Ticks - started > TimeSpan.FromSeconds(FullFlow ? 180 : 90).Ticks) throw new TimeoutException("Actual intro/world flow exceeded its wall timeout.");
                string error = SessionState.GetString(Key + "exception", "");
                if (error.Length > 0) throw new InvalidOperationException(error);
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (EditorApplication.isPaused) throw new InvalidOperationException("Play Mode paused during capture.");
                OceanEntry entry = OceanEntry.Instance;
                Camera source = Camera.main;
                // Input is applied on every Editor tick, before frame/sample filtering and rendering.
                // Production entry/fish update remains responsible for responding to the main view.
                if (HeadTurn && entry != null && source != null) ApplySyntheticHeadTurn(source, entry.CinematicVisibleTime);
                if (previousFrame == Time.frameCount) return;
                previousFrame = Time.frameCount;
                if (entry == null)
                {
                    if (FullFlow && sawReveal)
                    {
                        UnderwaterWorld world = Object.FindFirstObjectByType<UnderwaterWorld>();
                        if (world != null && world.IsReady && worldReadyLogged)
                        {
                            TreasureHunt hunt = world.GetComponentInChildren<TreasureHunt>(true);
                            if (hunt == null || hunt.Campaign == null || !hunt.Campaign.ActorsPrepared)
                                throw new InvalidOperationException("Final control-ready world has no prepared campaign actors.");
                            if (ExpectedAmmoFailure)
                            {
                                ValidateAmmoFailure(hunt, true);
                                if (failedResultObservedAt < 0) failedResultObservedAt = Time.unscaledTimeAsDouble;
                                if (Time.unscaledTimeAsDouble - failedResultObservedAt < 1.0) return;
                            }
                            if (!sawBuildGate || !sawContentReady || !controlReadyLogged || opaqueSubmissions == 0)
                                throw new InvalidOperationException("Missing opaque submission/build/content/control readiness evidence.");
                            CaptureStartupFrame(Camera.main, "startup-final-world");
                            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexNearClarity") >= 0)
                            {
                                string detailOutput = Path.Combine(SessionState.GetString(Key + "output", ""), "near-clarity");
                                OceanGuardianRevealChecks.CaptureInScene(Camera.main, Path.Combine(detailOutput, "guardian"));
                                EditorTests.ReefLodChecks.RunPolicyTests();
                                EditorTests.ReefLodChecks.ValidateBuilt(Object.FindFirstObjectByType<ReefProximityLod>(), Camera.main.transform.position);
                                OceanNearClarityChecks.ValidateInScene(Camera.main, detailOutput);
                            }
                            Finish(true, "Real intro completed, camera restored, world ready and world_ready completion log received.");
                        }
                        return;
                    }
                    if (frames > 0) throw new InvalidOperationException("Intro disappeared before the required transition."); return;
                }
                if (entry.HasFailed) throw new InvalidOperationException("Actual intro entered Failed.");
                if (source == null) throw new InvalidOperationException("Actual intro has no main camera.");
                if (target == null) InitializeCapture(source);
                if (FullFlow) AuditStartup(entry, source);
                if (entry.RenderedFrames < 2)
                {
                    // Satisfy the existing real-camera render gate. This submission leaves its pose
                    // and targetTexture unchanged; optional synthetic head input is applied above.
                    var data = source.GetUniversalAdditionalCameraData();
                    bool xr = data.allowXRRendering;
                    try { data.allowXRRendering = false; Submit(source); }
                    finally { data.allowXRRendering = xr; }
                    return;
                }
                if (entry.CinematicPhase > OceanEntrySequence.Beat.Dash)
                {
                    if ((stageMask & RequiredStages) != RequiredStages || frames < Fps * 4)
                        throw new InvalidOperationException("Missing Approach/Invite/Wiggle/Turn/Dash motion frames.");
                    if (!FullFlow) { Finish(true, "Reached Warp after complete real fish encounter."); return; }
                    // Batch Edit Mode has no guaranteed Game view. Submit the actual tracked camera
                    // once per player frame so the production opaque-render callback can open its gate.
                    var data = source.GetUniversalAdditionalCameraData(); bool xr = data.allowXRRendering;
                    try { data.allowXRRendering = false; Submit(source); }
                    finally { data.allowXRRendering = xr; }
                    return;
                }
                float clock = entry.CinematicVisibleTime;
                if (clock + .0001 < nextSample) return;
                CaptureFrame(entry, source, clock);
                nextSample = firstTime + frames / (double)Fps;
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }

        static void ApplySyntheticHeadTurn(Camera source, float visibleSeconds)
        {
            // Never replace an active XR device's pose, even if batch XR unexpectedly starts.
            SubsystemManager.GetSubsystems(xrDisplays);
            foreach (var display in xrDisplays)
                if (display.running) throw new InvalidOperationException("Synthetic head-turn preview refuses an active XR display.");
            if (syntheticHeadCamera == null)
            {
                syntheticHeadCamera = source;
                initialHeadRotation = source.transform.rotation;
            }
            else if (syntheticHeadCamera != source)
                throw new InvalidOperationException("Main camera changed during the synthetic head-turn input.");
            float turn = visibleSeconds < 12f
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((visibleSeconds - 6f) / 4f))
                : 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((visibleSeconds - 12f) / 4f));
            syntheticHeadYaw = 60f * turn;
            source.transform.rotation = Quaternion.AngleAxis(syntheticHeadYaw, Vector3.up) * initialHeadRotation;
            maximumSyntheticHeadYaw = Mathf.Max(maximumSyntheticHeadYaw, syntheticHeadYaw);
            syntheticHeadTicks++;
        }

        static void InitializeCapture(Camera source)
        {
            camera = new GameObject("Guide motion untracked preview", typeof(Camera)).GetComponent<Camera>();
            camera.gameObject.hideFlags = HideFlags.HideAndDontSave;
            camera.enabled = false;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32); target.Create();
            pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            fish = Object.FindFirstObjectByType<IntroGuideFish>();
            csv = new StreamWriter(Path.Combine(SessionState.GetString(Key + "output", ""), "frames.csv")) { AutoFlush = true };
            csv.WriteLine("frame,visible_seconds,unscaled_seconds,stage,beat_progress,fish_world_x,fish_world_y,fish_world_z,fish_camera_x,fish_camera_y,fish_camera_z,viewport_x,viewport_y,viewport_depth,camera_world_x,camera_world_y,camera_world_z,main_render_callbacks,fish_world_qx,fish_world_qy,fish_world_qz,fish_world_qw,swim_speed,swim_blend,idle,tail_phase,fin_phase,turn_amount,camera_world_qx,camera_world_qy,camera_world_qz,camera_world_qw,synthetic_head_yaw_degrees");
        }

        static void AuditStartup(OceanEntry entry, Camera source)
        {
            if (observedStage != entry.Stage) { observedStage = entry.Stage; AddStartupEvent("phase", entry); }
            if (entry.TargetProgress > .0001f && !entry.WorldBuildAllowed)
                throw new InvalidOperationException("World resources advanced before the opaque camera submission gate.");
            if (entry.CinematicPhase < OceanEntrySequence.Beat.Warp && entry.WorldBuildAllowed)
                throw new InvalidOperationException("World build gate opened while the fish is still visible.");
            if (entry.WorldBuildAllowed && !sawBuildGate)
            {
                if (opaqueSubmissions == 0 || entry.CinematicPhase != OceanEntrySequence.Beat.Warp || entry.PreviewWarpOpacity < .9999f)
                    throw new InvalidOperationException("World gate lacks a preceding tracked-camera full-opacity Warp submission.");
                sawBuildGate = true; AddStartupEvent("build_gate_after_opaque_submission", entry);
                CaptureStartupFrame(source, "startup-opaque-warp");
            }
            if (entry.CinematicPhase == OceanEntrySequence.Beat.Warp && entry.BeatProgress >= 1f && !entry.PreviewContentReady)
            {
                if (entry.PreviewWarpOpacity < .9999f || entry.WorldRevealed)
                    throw new InvalidOperationException("Warp time elapsed while assets pending, but full cover was not retained.");
                if (!sawWarpWaiting)
                { sawWarpWaiting = true; AddStartupEvent("warp_holding_for_content", entry); CaptureStartupFrame(source, "startup-waiting-warp"); }
            }
            if (entry.PreviewContentReady && !sawContentReady)
            {
                if (!sawBuildGate) throw new InvalidOperationException("Content became ready without the opaque build gate.");
                sawContentReady = true; AddStartupEvent("content_ready", entry);
            }
            if (entry.WorldRevealed && !sawReveal)
            {
                if (!entry.PreviewContentReady || !sawBuildGate || !entry.PreviewCameraRestored || source.cullingMask == (1 << OceanEntry.IntroLayer))
                    throw new InvalidOperationException("Reveal occurred before content readiness or without camera restoration.");
                sawReveal = true; AddStartupEvent("reveal_ready_and_camera_restored", entry);
            }
            if (entry.CinematicPhase == OceanEntrySequence.Beat.Reveal && entry.BeatProgress >= .5f && !revealImage)
            { revealImage = true; CaptureStartupFrame(source, "startup-reveal-middle"); }
            if (entry.CinematicPhase < OceanEntrySequence.Beat.Welcome && !OceanEntry.BlocksGameplay)
                throw new InvalidOperationException("Gameplay was released before the complete reveal fade.");
            if (entry.WorldPlayable || controlReadyLogged)
            {
                TreasureHunt hunt = Object.FindFirstObjectByType<TreasureHunt>();
                if (hunt == null || hunt.Campaign == null || !hunt.Campaign.ActorsPrepared)
                    throw new InvalidOperationException("World became playable before campaign actors/contact targets were prepared.");
            }
            if (ExpectedAmmoFailure)
            {
                TreasureHunt hunt = Object.FindFirstObjectByType<TreasureHunt>();
                if (hunt != null && hunt.Campaign != null) ValidateAmmoFailure(hunt, false);
            }
            if (controlReadyLogged && !sawPlayable)
            {
                UnderwaterWorld world = Object.FindFirstObjectByType<UnderwaterWorld>();
                if (!entry.WorldPlayable || OceanEntry.BlocksGameplay || !entry.PreviewCameraRestored ||
                    !entry.PreviewContentReady || world == null || !world.IsReady)
                    throw new InvalidOperationException("Control-ready log disagrees with complete, exposed playable world.");
                sawPlayable = true; AddStartupEvent("controls_released_with_caption_alive", entry);
                CaptureStartupFrame(source, "startup-playable-caption");
            }
        }

        static void ValidateAmmoFailure(TreasureHunt hunt, bool final)
        {
            OceanCampaignController campaign = hunt.Campaign;
            if (campaign == null || campaign.Panel == null) throw new InvalidOperationException("Missing restored campaign UI.");
            if (!hunt.CoreActive && campaign.Panel.IsOpen)
                throw new InvalidOperationException("Restored ammo failure exposed its result panel before CoreActive.");
            if (!campaign.ActorsPrepared) return;
            if (campaign.State.Current.level != 5 || !campaign.State.Failed || !campaign.State.AmmoExhausted ||
                campaign.State.Current.spearShotsFired != 20 || campaign.State.Current.sharkHits != 0)
                throw new InvalidOperationException("Expected prepared level-five zero-hit exhausted-ammo restore fixture.");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            bool pending = (bool)typeof(OceanCampaignController).GetField("pendingEntryAmmoFailure", flags).GetValue(campaign);
            double resultAt = (double)typeof(OceanCampaignController).GetField("resultAt", flags).GetValue(campaign);
            if (!double.IsPositiveInfinity(resultAt))
                throw new InvalidOperationException("Initial restored ammo failure scheduled a duplicate delayed result.");
            if (!hunt.CoreActive)
            {
                if (!pending) throw new InvalidOperationException("Covered restored ammo failure lost its deferred presentation flag.");
                if (!sawCoveredAmmoFailure)
                {
                    sawCoveredAmmoFailure = true;
                    Debug.Log("GUIDE_AMMO_FAILURE_COVERED: actors prepared and state failed; panel closed, deferred presentation, no result timer.");
                }
            }
            if (final)
            {
                if (!sawCoveredAmmoFailure || !hunt.CoreActive || !campaign.Panel.IsOpen || pending)
                    throw new InvalidOperationException("Restored ammo failure did not hand off exactly once to its active result panel.");
                if (failedResultObservedAt < 0)
                    Debug.Log("GUIDE_AMMO_FAILURE_RESULT: CoreActive, actors prepared, failed, panel open; observing one second without delayed result scheduling.");
            }
        }

        static void AddStartupEvent(string kind, OceanEntry entry)
        {
            startupEvents.Add(new StartupEvent { kind = kind, stage = entry.Stage, frame = Time.frameCount,
                realtime = Time.realtimeSinceStartupAsDouble, opaqueCameraSubmissions = opaqueSubmissions,
                progress = entry.TargetProgress, opacity = entry.PreviewWarpOpacity, buildAllowed = entry.WorldBuildAllowed,
                contentReady = entry.PreviewContentReady, revealed = entry.WorldRevealed, cameraRestored = entry.PreviewCameraRestored,
                playable = entry.WorldPlayable });
        }

        static void CaptureStartupFrame(Camera source, string name)
        {
            if (source == null) throw new InvalidOperationException("The real main camera disappeared during startup.");
            // Capture the same camera/culling/background used by the real scene, with XR temporarily
            // disabled only for the mono Editor render request. No actor or sequence state is changed.
            var data = source.GetUniversalAdditionalCameraData(); bool xr = data.allowXRRendering;
            RenderTexture previous = RenderTexture.active;
            try
            {
                data.allowXRRendering = false; Submit(source); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(SessionState.GetString(Key + "output", ""), name + ".png"), pixels.EncodeToPNG());
            }
            finally { data.allowXRRendering = xr; RenderTexture.active = previous; }
        }

        static void Submit(Camera view)
        {
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(view, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        }

        static void CaptureFrame(OceanEntry entry, Camera source, float clock)
        {
            if (fish == null) fish = Object.FindFirstObjectByType<IntroGuideFish>();
            if (fish == null) throw new InvalidOperationException("Actual intro has no guide fish.");
            if (fish.ResourcePath != "Fish/01_clownfish" || fish.UsesAuthoredAnimation)
                throw new InvalidOperationException("The captured guide is not the ordinary reef clownfish/shader path.");
            Bounds localBounds=fish.LocalBounds;
            if(!Finite(localBounds.center)||!Finite(localBounds.size)||!Finite(fish.transform.position)||
                float.IsNaN(fish.Length)||float.IsInfinity(fish.Length)||Mathf.Abs(fish.Length-.30f)>.001f||
                localBounds.size.x<=0f||localBounds.size.y<=0f||Mathf.Abs(localBounds.size.z-fish.Length)>.001f||localBounds.size.magnitude>.65f||
                fish.TriangleCount!=SessionState.GetInt(Key+"expectedTriangles",-1))
                throw new InvalidOperationException("Guide geometry must keep its finite measured 0.30m reef model and original topology.");
            resourcePath = fish.ResourcePath; triangles = fish.TriangleCount; authored = fish.UsesAuthoredAnimation;
            camera.CopyFrom(source);
            camera.enabled = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.fieldOfView = 80f; camera.aspect = (float)Width / Height;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .065f, .080f, 1f);
            camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            RenderTexture previous = RenderTexture.active;
            try
            {
                Submit(camera);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(SessionState.GetString(Key + "output", ""), $"frame-{frames:D4}.png"), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; }
            Vector3 world = fish.transform.position, local = camera.transform.InverseTransformPoint(world);
            Vector3 viewport = camera.WorldToViewportPoint(world), eye = source.transform.position;
            Quaternion orientation = fish.transform.rotation;
            Quaternion cameraOrientation = source.transform.rotation;
            csv.WriteLine(string.Format(Inv, "{0},{1:F4},{2:F4},{3},{4:F4},{5:F5},{6:F5},{7:F5},{8:F5},{9:F5},{10:F5},{11:F5},{12:F5},{13:F5},{14:F5},{15:F5},{16:F5},{17},{18:F6},{19:F6},{20:F6},{21:F6}",
                frames, clock, Time.unscaledTimeAsDouble, entry.Stage, entry.BeatProgress, world.x, world.y, world.z,
                local.x, local.y, local.z, viewport.x, viewport.y, viewport.z, eye.x, eye.y, eye.z, entry.RenderedFrames,
                orientation.x, orientation.y, orientation.z, orientation.w)
                + string.Format(Inv, ",{0:F5},{1:F5},{2},{3:F5},{4:F5},{5:F5}",
                    fish.SwimSpeed, fish.SwimBlend, fish.IsIdle ? 1 : 0, fish.TailPhase, fish.FinPhase, fish.TurnAmount)
                + string.Format(Inv, ",{0:F6},{1:F6},{2:F6},{3:F6},{4:F4}",
                    cameraOrientation.x, cameraOrientation.y, cameraOrientation.z, cameraOrientation.w, syntheticHeadYaw));
            if (SessionState.GetBool(Key + "detail", false))
            {
                // Follows the actual fish only to expose deformation; the main view/CSV above retains
                // the true encounter path. This camera is evidence of the shader, not tracked-head framing.
                camera.fieldOfView = 42f;
                camera.transform.position = world + fish.transform.right * .62f + Vector3.up * .09f - fish.transform.forward * .045f;
                camera.transform.LookAt(world, Vector3.up);
                previous = RenderTexture.active;
                try
                {
                    Submit(camera); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply(false, false);
                    File.WriteAllBytes(Path.Combine(SessionState.GetString(Key + "output", ""), "detail", $"frame-{frames:D4}.png"), pixels.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; }
            }
            if (firstTime < 0f) firstTime = clock;
            lastTime = clock; stageMask |= 1 << (int)entry.CinematicPhase; frames++;
        }

        static void Finish(bool success, string detail)
        {
            SessionState.SetBool(Key + "active", false);
            EditorApplication.update -= Tick; Application.logMessageReceived -= LogException;
            RenderPipelineManager.endCameraRendering -= AuditCameraSubmission;
            string output = SessionState.GetString(Key + "output", "");
            try
            {
                if (csv != null) { csv.Dispose(); csv = null; }
                if (!string.IsNullOrEmpty(output))
                    File.WriteAllText(Path.Combine(output, "manifest.txt"), string.Format(Inv,
                        "success={0}\nframes={1}\nfps={2}\nsize={3}x{4}\nfirst_visible_seconds={5:F4}\nlast_visible_seconds={6:F4}\nstage_mask={7}\nscene={8}\nbackground=neutral Editor background, NOT VST\nrender=actual scene, actual fish shader, mono URP\nsampling=actual sequence visible time; NOT device frame pacing\ndetail={9}\nexit_code={10}\nrequired_stage_mask={11}\n",
                        success, frames, Fps, Width, Height, firstTime, lastTime, stageMask, Scene, detail, success ? 0 : 1, RequiredStages)
                        + $"resource={resourcePath}\ntriangles={triangles}\nauthored_animation={authored}\ndetail_enabled={SessionState.GetBool(Key + "detail", false)}\nfull_flow={FullFlow}\n"
                        + "animation=ordinary reef FishSwim deformation; no replacement skeletal clips\ndetail_camera=side-following close-up of same animated fish; not a headset view or replacement path\n"
                        + string.Format(Inv, "synthetic_head_turn={0}\nsynthetic_head_ticks={1}\nsynthetic_head_max_yaw_degrees={2:F4}\n", HeadTurn, syntheticHeadTicks, maximumSyntheticHeadYaw)
                        + $"expected_ammo_failure={ExpectedAmmoFailure}\ncovered_ammo_failure_seen={sawCoveredAmmoFailure}\n"
                        + "head_input=" + (HeadTurn ? "SYNTHETIC Editor main-camera world-Y rotation, NOT real head tracking; visible seconds 6-10 smooth 0 to +60 degrees, 10-12 hold, 12-16 smooth return; position untouched" : "No synthetic head-turn input") + "\n");
                if (FullFlow && !string.IsNullOrEmpty(output))
                    File.WriteAllText(Path.Combine(output, "startup-flow.json"), JsonUtility.ToJson(new StartupResult {
                        success = success, buildGateSeen = sawBuildGate, contentReadySeen = sawContentReady,
                        revealSeen = sawReveal, warpWaitingSeen = sawWarpWaiting, finalWorldReadyLog = worldReadyLogged,
                        controlReadySeen = controlReadyLogged, playableCaptionSeen = sawPlayable,
                        opaqueCameraSubmissions = opaqueSubmissions, events = startupEvents.ToArray(), detail = detail,
                        scope = "Actual Editor Play Mode sequence/world/mono main-camera submissions. Uniform opacity plus submitted frame and screenshots; not GPU completion, PICO compositor, VST visibility, headset comfort or device frame pacing. No synthetic readiness or replacement curve."
                            + (HeadTurn ? " Main-camera yaw is SYNTHETIC Editor input, NOT real head tracking." : "")
                    }, true));
                if (syntheticHeadCamera != null) syntheticHeadCamera.transform.rotation = initialHeadRotation;
                if (camera != null) Object.DestroyImmediate(camera.gameObject);
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
            catch (Exception error) { success = false; detail += "\nFinalization: " + error; }
            finally
            {
                Debug.Log((success ? "GUIDE_MOTION_SUCCEEDED: " : "GUIDE_MOTION_FAILED: ") + output + " " + detail);
                if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
            }
        }
    }
}
#endif

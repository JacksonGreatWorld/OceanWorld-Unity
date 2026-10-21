using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Profiling;

namespace SwanUnderwater
{
    /// <summary>Measures the Unity player-loop interval, not headset presentation FPS.</summary>
    public sealed class RuntimeDiagnostics : MonoBehaviour
    {
        private const float WarmupSeconds = 10f;
        private const float WindowSeconds = 10f;
        private const double LiveUpdateSeconds = 0.5;
        private const double FpsWindowSeconds = 1.0;
        private readonly float[] samples = new float[8192];
        private readonly float[] sorted = new float[8192];
        private readonly float[] recentIntervals = new float[1024];
        private readonly FrameTiming[] timing = new FrameTiming[1];
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private int count, droppedSamples;
        private double resumeAt, windowStart;
        private float longestMs;
        private double sumMs;
        private bool paused;
        private OceanDiagnosticLog writer;
        private bool fileCapacityNotice;
        private HandSwimController hands;
        private OceanCampaignController campaign;
        private OceanLeaderboardBridge leaderboard;
        private ProfilerRecorder drawCalls, setPassCalls, triangles, batches, gcAllocated;
        private int recentStart, recentCount;
        private double recentSeconds, nextLiveUpdate;
        private static long boidSectionTicks, boidSectionLongestTicks, boidSectionSamples;

        /// <summary>Main-thread synchronous UpdateBoids elapsed time; no allocation or
        /// profiler dependency. It includes work/preemption inside that call, not GPU
        /// execution, rendering submission outside it, or headset presentation.</summary>
        internal static void RecordBoidStepTicks(long elapsedTicks)
        {
            if (elapsedTicks < 0) return;
            boidSectionTicks += elapsedTicks;
            boidSectionSamples++;
            if (elapsedTicks > boidSectionLongestTicks) boidSectionLongestTicks = elapsedTicks;
        }

        /// <summary>HUD snapshot refreshed twice per second. FPS is a rolling player-loop rate;
        /// other values are latest reported frames, possibly from different provider frames.</summary>
        public LiveSample Latest { get; private set; }

        public enum GpuSource { Unavailable, FrameTimingManager, XrDisplaySubsystem, XrStats }

        public struct LiveSample
        {
            public double sampledAt, loopFps, fpsWindowSeconds, cpuFrameMs, gpuFrameMs;
            public int fpsWindowSamples;
            public bool fpsAvailable, cpuAvailable, gpuAvailable;
            public GpuSource gpuSource;
            public bool frameTimingEnabled, frameTimingCpuAvailable, frameTimingGpuAvailable;
            public double frameTimingCpuMs, frameTimingGpuMs;
            public bool xrDisplayGpuAvailable, xrStatsGpuAvailable;
            public double xrDisplayGpuMs, xrStatsGpuMs;
            public float displayRefreshHz;
            public bool displayRefreshAvailable;
            public bool drawCallsAvailable, setPassCallsAvailable, trianglesAvailable, batchesAvailable, gcAllocatedAvailable;
            public long drawCalls, setPassCalls, triangles, batches, gcAllocatedBytes;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<RuntimeDiagnostics>() != null) return;
            var host = new GameObject("Runtime Diagnostics");
            DontDestroyOnLoad(host);
            host.AddComponent<RuntimeDiagnostics>();
        }

        private void Awake()
        {
            // Capacity one keeps only the most recently completed frame. No per-frame managed polling.
            drawCalls = TryStartCounter(ProfilerCategory.Render, "Draw Calls Count");
            setPassCalls = TryStartCounter(ProfilerCategory.Render, "SetPass Calls Count");
            triangles = TryStartCounter(ProfilerCategory.Render, "Triangles Count");
            batches = TryStartCounter(ProfilerCategory.Render, "Batches Count");
            gcAllocated = TryStartCounter(ProfilerCategory.Memory, "GC Allocated In Frame");
            if (GetComponent<PerformanceHud>() == null) gameObject.AddComponent<PerformanceHud>();
            resumeAt = Time.realtimeSinceStartupAsDouble;
            ResetWindow();
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "Diagnostics");
                writer = OceanDiagnosticLog.CreateSessionFile(dir, DateTime.UtcNow, out string path);
                Debug.Log("[SwanDiagnostics] file=" + path + " session_utf8_byte_cap=" + writer.MaxSessionBytes +
                    " cap_includes_terminal_record=true old_files_preserved=true");
            }
            catch (Exception e) { Debug.LogWarning("[SwanDiagnostics] file unavailable: " + e.Message); }
            Debug.Log($"[SwanDiagnostics] version={Application.version} unity={Application.unityVersion} " +
                      $"development={Debug.isDebugBuild} graphics={SystemInfo.graphicsDeviceType} device={SystemInfo.deviceModel} " +
                      "metric=Time.unscaledDeltaTime player_loop_interval_not_gpu_or_compositor_fps warmup_seconds=10 window_seconds=10 " +
                      "hud_fps=rolling_approximately_1s_loop_rate hud_update_seconds=0.5 " +
                      "cpu=FrameTimingManager_cpuFrameTime_including_waits gpu=latest_provider_app_frame_not_compositor");
        }

        private void Update()
        {
            if (paused) return;
            if (OceanEntry.BlocksGameplay)
            {
                resumeAt = Time.realtimeSinceStartupAsDouble;
                recentStart = recentCount = 0;
                recentSeconds = nextLiveUpdate = 0;
                Latest = default;
                ResetWindow();
                return;
            }
            if (FrameTimingManager.IsFeatureEnabled()) FrameTimingManager.CaptureFrameTimings();
            double now = Time.realtimeSinceStartupAsDouble;
            AddRecentInterval(Time.unscaledDeltaTime);
            if (now >= nextLiveUpdate)
            {
                Latest = SampleLatest();
                nextLiveUpdate = now + LiveUpdateSeconds;
            }
            if (now - resumeAt < WarmupSeconds) { ResetWindow(); return; }
            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms > 0 && !float.IsNaN(ms) && !float.IsInfinity(ms))
            {
                longestMs = Mathf.Max(longestMs, ms);
                sumMs += ms;
                if (count < samples.Length) samples[count++] = ms;
                else droppedSamples++;
            }
            if (now - windowStart >= WindowSeconds) Report("interval");
        }

        private void OnApplicationPause(bool value)
        {
            if (value && count > 0) Report("partial_before_pause");
            paused = value;
            resumeAt = Time.realtimeSinceStartupAsDouble;
            recentStart = recentCount = 0;
            recentSeconds = nextLiveUpdate = 0;
            Latest = default;
            ResetWindow();
        }

        private void OnDestroy()
        {
            try { if (count > 0) Report("partial_before_destroy"); }
            finally
            {
                drawCalls.Dispose();
                setPassCalls.Dispose();
                triangles.Dispose();
                batches.Dispose();
                gcAllocated.Dispose();
                CloseDiagnosticLog();
            }
        }

        private void ResetWindow()
        {
            count = droppedSamples = 0;
            sumMs = longestMs = 0;
            boidSectionTicks = boidSectionLongestTicks = boidSectionSamples = 0;
            windowStart = Time.realtimeSinceStartupAsDouble;
        }

        private void Report(string reason)
        {
            if (count == 0) return;
            if (hands == null) hands = FindFirstObjectByType<HandSwimController>();
            Array.Copy(samples, sorted, count);
            Array.Sort(sorted, 0, count);
            var report = new ReportData
            {
                utc = DateTime.UtcNow.ToString("O"), reason = reason, version = Application.version,
                metric = "Time.unscaledDeltaTime: Unity player-loop interval; NOT GPU cost or compositor/presented FPS",
                elapsedSeconds = Time.realtimeSinceStartupAsDouble - windowStart,
                samples = count, percentileSamplesDropped = droppedSamples,
                meanMs = sumMs / (count + droppedSamples), p50Ms = Percentile(0.50f),
                p95Ms = Percentile(0.95f), p99Ms = Percentile(0.99f), longestMs = longestMs,
                targetFrameRate = Application.targetFrameRate, developmentBuild = Debug.isDebugBuild,
                xrEnabled = XRSettings.enabled, xrDeviceActive = XRSettings.isDeviceActive,
                xrReportedEyeWidth = XRSettings.eyeTextureWidth, xrReportedEyeHeight = XRSettings.eyeTextureHeight,
                xrReportedRenderScale = XRSettings.eyeTextureResolutionScale,
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled()
            };
            LiveSample live = SampleLatest();
            Latest = live;
            report.boidSectionTimingAvailable = boidSectionSamples > 0;
            report.boidSectionSamples = boidSectionSamples;
            double ticksToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            report.boidSectionMeanMs = boidSectionSamples > 0 ?
                boidSectionTicks * ticksToMs / boidSectionSamples : -1;
            report.boidSectionLongestMs = boidSectionSamples > 0 ? boidSectionLongestTicks * ticksToMs : -1;
            report.boidSectionTimingScope = "Stopwatch synchronous UnderwaterWorld.UpdateBoids call elapsed ms since diagnostics window reset; includes CPU/native work and preemption inside call; excludes other update/render work; NOT GPU/compositor timing. Section sample count can differ from loop-interval sample count. Available only in instrumented builds; no historical baseline section measurement.";
            // These are latest-frame snapshots, never 10-second means, peaks, or GPU timings.
            report.profilerCounterScope = "Latest completed Unity frame sampled at report time; not window averages; unavailable=-1";
            report.drawCallsAvailable = live.drawCallsAvailable; report.latestDrawCalls = live.drawCalls;
            report.setPassCallsAvailable = live.setPassCallsAvailable; report.latestSetPassCalls = live.setPassCalls;
            report.trianglesAvailable = live.trianglesAvailable; report.latestTriangles = live.triangles;
            report.batchesAvailable = live.batchesAvailable; report.latestBatches = live.batches;
            report.gcAllocatedInFrameAvailable = live.gcAllocatedAvailable; report.latestGcAllocatedInFrameBytes = live.gcAllocatedBytes;
            report.hudFpsScope = "Rolling approximately 1 second of Time.unscaledDeltaTime; Unity loop rate, not presented/compositor FPS";
            report.hudLoopFpsAvailable = live.fpsAvailable;
            report.hudLoopFps = live.loopFps;
            report.hudFpsWindowSeconds = live.fpsWindowSeconds;
            report.hudFpsWindowSamples = live.fpsWindowSamples;
            report.frameTimingEnabled = live.frameTimingEnabled;
            report.frameTimingCpuAvailable = live.frameTimingCpuAvailable;
            report.frameTimingGpuAvailable = live.frameTimingGpuAvailable;
            report.latestFrameTimingCpuMs = live.frameTimingCpuMs;
            report.latestFrameTimingGpuMs = live.frameTimingGpuMs;
            report.cpuTimingScope = "FrameTiming.cpuFrameTime in ms, including waiting/overheads; not pure CPU execution cost";
            report.gpuTimingScope = "Latest available application GPU frame: FrameTimingManager, then XR display, then XRStats; provider frames need not align; not compositor GPU cost";
            report.gpuTimingAvailable = live.gpuAvailable;
            report.gpuTimingSource = live.gpuSource.ToString();
            report.latestGpuMs = live.gpuFrameMs;
            report.xrDisplayGpuAvailable = live.xrDisplayGpuAvailable;
            report.latestXrDisplayGpuMs = live.xrDisplayGpuMs;
            report.xrStatsGpuAvailable = live.xrStatsGpuAvailable;
            report.latestXrStatsGpuMs = live.xrStatsGpuMs;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)
            {
                report.urpRenderScale = pipeline.renderScale;
                report.msaaSamples = pipeline.msaaSampleCount;
            }
            if (hands != null)
            {
                report.leftHandTracked = hands.LeftHandActive;
                report.rightHandTracked = hands.RightHandActive;
                report.leftPaddleTracked = hands.LeftPaddleTracked;
                report.rightPaddleTracked = hands.RightPaddleTracked;
                report.menuLocomotionPaused = hands.MenuLocomotionPaused;
                report.interactionSuppressed = hands.InteractionSuppressed;
                report.handVisualsReadable = hands.CanReadTrackedVisuals;
                report.leftPaddleDecision = hands.LeftPaddleDecision.ToString();
                report.rightPaddleDecision = hands.RightPaddleDecision.ToString();
                report.handTrackingBackend = OceanHandTracking.BackendName;
                report.leftGraspValid = hands.LeftGraspValidThisStep;
                report.rightGraspValid = hands.RightGraspValidThisStep;
                report.leftGraspArmed = hands.LeftGraspArmed;
                report.rightGraspArmed = hands.RightGraspArmed;
                report.leftPaddleFistSuppressed = hands.LeftPaddleFistSuppressed;
                report.rightPaddleFistSuppressed = hands.RightPaddleFistSuppressed;
                report.leftGraspFingerMask = hands.LeftGraspFingerMask;
                report.rightGraspFingerMask = hands.RightGraspFingerMask;
                report.nativePinchBegins = hands.NativePinchBegins;
                report.nativePinchInputCurrent = hands.NativePinchInputCurrent;
                report.nativePinchMenuConsumed = hands.NativePinchMenuConsumed;
                report.nativePinchWorldGrabsAccepted = hands.NativePinchWorldGrabsAccepted;
                report.completedStrokes = hands.CompletedStrokeCount;
                report.leftStrokeCount = hands.LeftStrokeCount;
                report.rightStrokeCount = hands.RightStrokeCount;
                report.trackingSampleSeconds = hands.TrackingSampleSeconds;
                report.leftHandTrackedSeconds = hands.LeftHandTrackedSeconds;
                report.rightHandTrackedSeconds = hands.RightHandTrackedSeconds;
                report.movementSpeed = hands.CurrentSpeed;
                report.handTurnSpeedDegreesPerSecond = hands.CurrentHandTurnSpeed;
                report.handTurnDegreesTotal = hands.TotalHandTurnDegrees;
                report.handTurnAbsoluteDegreesTotal = hands.TotalAbsoluteHandTurnDegrees;
                report.handTurnState = hands.HandTurnState.ToString();
            }
            report.strokeCountScope = "Cumulative recognized individual hand pulls since controller initialization; a simultaneous left+right pull counts as 2. Earlier v37 used a different paired-stroke definition; do not compare raw totals directly.";
            report.trackingDurationScope = "Cumulative controller sampled seconds and valid locomotion-anchor seconds: current tracked wrist on AVP, palm on PICO/Quest. These are denominators, not the current 10-second report window. Gesture suppression does not change tracking validity. leftHandTracked/rightHandTracked remain interaction palm status.";
            report.paddleDecisionScope = "Latest controller step at report time; not window counts. Console PaddleDecision records window counts. Menu pause and interaction suppression are latest state.";
            if (campaign == null) campaign = FindFirstObjectByType<OceanCampaignController>();
            if (leaderboard == null) leaderboard = FindFirstObjectByType<OceanLeaderboardBridge>();
            if (campaign != null)
            {
                report.campaignGameplayAllowed = campaign.GameplayAllowed;
                if (campaign.State != null)
                {
                    report.campaignCanCollect = campaign.State.CanCollect;
                    report.campaignLevel = campaign.State.Current == null ? 0 : campaign.State.Current.level;
                }
                if (campaign.Panel != null)
                {
                    report.campaignMenuOpen = campaign.Panel.IsOpen;
                    report.campaignMenuTransitioning = campaign.Panel.IsTransitioning;
                    report.campaignMenuAvailable = campaign.Panel.IsAvailableForDiagnostics;
                    report.campaignMenuPage = campaign.Panel.PageNameForDiagnostics;
                }
            }
            report.leaderboardOpen = leaderboard != null && leaderboard.IsOpen;
            report.entryBlocksGameplay = OceanEntry.BlocksGameplay;
            report.interactionDiagnosticScope = "Latest controller/panel state at report time, not window counts. Native pinch counters are cumulative accepted begins/menu claims/successful world consumption, not raw OS events or strict fist counts. Joint masks only report current adapter state bits; they add no pose qualification.";
            report.leftRawFrameCurrent = CaptureJointFlags(OceanHandSide.HandLeft,
                out report.leftWristFlags, out report.leftMiddleProximalFlags, out report.leftCurrentValidPoseMask, out report.leftCurrentTrackedPoseMask);
            report.rightRawFrameCurrent = CaptureJointFlags(OceanHandSide.HandRight,
                out report.rightWristFlags, out report.rightMiddleProximalFlags, out report.rightCurrentValidPoseMask, out report.rightCurrentTrackedPoseMask);
            Camera head = Camera.main;
            if (head != null)
            {
                report.headWorldPosition = head.transform.position;
                report.eyeHeightAboveSeabed = head.transform.position.y -
                    UnderwaterWorld.SampleTerrainHeight(head.transform.position.x, head.transform.position.z);
            }
            // SampleLatest refreshed this reusable subsystem list at report time.
            var passes = new List<RenderPassData>();
            foreach (var display in displays)
            {
                if (!display.running) continue;
                report.xrRunningDisplayCount++;
                if (display.TryGetDisplayRefreshRate(out float rate)) report.xrReportedRefreshHz = rate;
                for (int index = 0; index < display.GetRenderPassCount(); index++)
                {
                    display.GetRenderPass(index, out var pass);
                    passes.Add(new RenderPassData { index = index, width = pass.renderTargetDesc.width,
                        height = pass.renderTargetDesc.height, slices = pass.renderTargetDesc.volumeDepth,
                        views = pass.GetRenderParameterCount() });
                }
            }
            report.xrRenderTargets = passes.ToArray();
            string json = JsonUtility.ToJson(report);
            Debug.Log("[SwanDiagnostics] " + json);
            try
            {
                writer?.TryWriteLine(json);
                if (writer != null && writer.SizeLimitReached && !fileCapacityNotice)
                {
                    fileCapacityNotice = true;
                    Debug.LogWarning("[SwanDiagnostics] session file reached UTF-8 byte cap; file logging stopped, old files preserved. " +
                        "Subsequent reports still use the existing Unity console path. Only reason=interval and samples>0 are complete performance windows.");
                }
            }
            catch (Exception e) { Debug.LogWarning("[SwanDiagnostics] write failed; file logging stopped: " + e.Message); CloseDiagnosticLog(); }
            ResetWindow();
        }

        private void CloseDiagnosticLog()
        {
            OceanDiagnosticLog closing = writer; writer = null;
            try { closing?.Dispose(); }
            catch (Exception e) { Debug.LogWarning("[SwanDiagnostics] close failed: " + e.Message); }
        }

        private float Percentile(float p) => sorted[Mathf.Clamp(Mathf.CeilToInt(count * p) - 1, 0, count - 1)];
        private static bool CaptureJointFlags(OceanHandSide side, out int wristFlags, out int middleFlags,
            out int validPoseMask, out int trackedPoseMask)
        {
            wristFlags = middleFlags = validPoseMask = trackedPoseMask = 0;
            if (!OceanHandTracking.TryGetFrame(side, out var frame) || frame.jointLocations == null) return false;
            const OceanHandLocationStatus valid = OceanHandLocationStatus.PositionValid | OceanHandLocationStatus.OrientationValid;
            const OceanHandLocationStatus tracked = valid | OceanHandLocationStatus.PositionTracked | OceanHandLocationStatus.OrientationTracked;
            int count = (int)Math.Min(26u, Math.Min(frame.jointCount, (uint)frame.jointLocations.Length));
            for (int i = 0; i < count; i++)
            {
                var flags = frame.jointLocations[i].locationStatus;
                if ((flags & valid) == valid) validPoseMask |= 1 << i;
                if ((flags & tracked) == tracked) trackedPoseMask |= 1 << i;
            }
            if (count > (int)OceanHandJointId.JointWrist) wristFlags = (int)frame.jointLocations[(int)OceanHandJointId.JointWrist].locationStatus;
            if (count > (int)OceanHandJointId.JointMiddleProximal) middleFlags = (int)frame.jointLocations[(int)OceanHandJointId.JointMiddleProximal].locationStatus;
            return true;
        }
        private static bool IsPositiveFinite(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

        private void AddRecentInterval(float seconds)
        {
            if (!IsPositiveFinite(seconds)) return;
            if (recentCount == recentIntervals.Length)
            {
                recentSeconds -= recentIntervals[recentStart];
                recentStart = (recentStart + 1) % recentIntervals.Length;
                recentCount--;
            }
            recentIntervals[(recentStart + recentCount) % recentIntervals.Length] = seconds;
            recentSeconds += seconds;
            recentCount++;
            // Keep enough whole intervals to span at least one second. During startup use the
            // available shorter window; preserve a >1s stall until subsequent frames replace it.
            while (recentCount > 1 && recentSeconds - recentIntervals[recentStart] >= FpsWindowSeconds)
            {
                recentSeconds -= recentIntervals[recentStart];
                recentStart = (recentStart + 1) % recentIntervals.Length;
                recentCount--;
            }
        }

        private LiveSample SampleLatest()
        {
            var live = new LiveSample
            {
                sampledAt = Time.realtimeSinceStartupAsDouble,
                fpsAvailable = recentCount > 0 && IsPositiveFinite(recentSeconds),
                fpsWindowSeconds = recentSeconds, fpsWindowSamples = recentCount,
                cpuFrameMs = -1, gpuFrameMs = -1,
                frameTimingCpuMs = -1, frameTimingGpuMs = -1,
                xrDisplayGpuMs = -1, xrStatsGpuMs = -1,
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled()
            };
            live.loopFps = live.fpsAvailable ? recentCount / recentSeconds : -1;
            live.drawCallsAvailable = TryReadCounter(drawCalls, out live.drawCalls);
            live.setPassCallsAvailable = TryReadCounter(setPassCalls, out live.setPassCalls);
            live.trianglesAvailable = TryReadCounter(triangles, out live.triangles);
            live.batchesAvailable = TryReadCounter(batches, out live.batches);
            live.gcAllocatedAvailable = TryReadCounter(gcAllocated, out live.gcAllocatedBytes);
            if (live.frameTimingEnabled && FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                // cpuFrameTime includes waits. Never substitute Time.unscaledDeltaTime for it.
                live.frameTimingCpuAvailable = IsPositiveFinite(timing[0].cpuFrameTime);
                live.frameTimingGpuAvailable = IsPositiveFinite(timing[0].gpuFrameTime);
                if (live.frameTimingCpuAvailable) live.frameTimingCpuMs = timing[0].cpuFrameTime;
                if (live.frameTimingGpuAvailable) live.frameTimingGpuMs = timing[0].gpuFrameTime;
            }
            live.cpuAvailable = live.frameTimingCpuAvailable;
            live.cpuFrameMs = live.frameTimingCpuMs;
            displays.Clear();
            SubsystemManager.GetSubsystems(displays);
            bool runningXr = false;
            foreach (var display in displays)
            {
                if (!display.running) continue;
                runningXr = true;
                if (!live.displayRefreshAvailable && display.TryGetDisplayRefreshRate(out float rate) && IsPositiveFinite(rate))
                {
                    live.displayRefreshAvailable = true;
                    live.displayRefreshHz = rate;
                }
                // Unity XR APIs report seconds; FrameTimingManager reports milliseconds.
                if (!live.xrDisplayGpuAvailable && display.TryGetAppGPUTimeLastFrame(out float gpuSeconds) && IsPositiveFinite(gpuSeconds))
                {
                    live.xrDisplayGpuAvailable = true;
                    live.xrDisplayGpuMs = gpuSeconds * 1000.0;
                }
            }
            if (runningXr && XRStats.TryGetGPUTimeLastFrame(out float legacyGpuSeconds) && IsPositiveFinite(legacyGpuSeconds))
            {
                live.xrStatsGpuAvailable = true;
                live.xrStatsGpuMs = legacyGpuSeconds * 1000.0;
            }
            if (live.frameTimingGpuAvailable)
            {
                live.gpuSource = GpuSource.FrameTimingManager;
                live.gpuFrameMs = live.frameTimingGpuMs;
            }
            else if (live.xrDisplayGpuAvailable)
            {
                live.gpuSource = GpuSource.XrDisplaySubsystem;
                live.gpuFrameMs = live.xrDisplayGpuMs;
            }
            else if (live.xrStatsGpuAvailable)
            {
                live.gpuSource = GpuSource.XrStats;
                live.gpuFrameMs = live.xrStatsGpuMs;
            }
            live.gpuAvailable = live.gpuSource != GpuSource.Unavailable;
            return live;
        }

        private static ProfilerRecorder TryStartCounter(ProfilerCategory category, string name)
        {
            try { return ProfilerRecorder.StartNew(category, name, 1); }
            catch (Exception e)
            {
                // Counter support varies by platform/build type. Do not enable full profiling to force it.
                Debug.LogWarning("[SwanDiagnostics] counter unavailable: " + name + ": " + e.Message);
                return default;
            }
        }

        private static bool TryReadCounter(ProfilerRecorder recorder, out long value)
        {
            value = -1;
            if (!recorder.Valid || recorder.Count == 0) return false;
            long latest = recorder.LastValue;
            if (latest < 0) return false;
            value = latest; // A genuine recorded zero is valid; a missing counter is explicitly -1/false.
            return true;
        }

        [Serializable] private sealed class RenderPassData { public int index, width, height, slices, views; }
        [Serializable] private sealed class ReportData
        {
            public string utc, reason, version, metric;
            public double elapsedSeconds, meanMs, latestFrameTimingCpuMs, latestFrameTimingGpuMs;
            public int samples, percentileSamplesDropped, targetFrameRate, xrReportedEyeWidth, xrReportedEyeHeight, xrRunningDisplayCount;
            public float p50Ms, p95Ms, p99Ms, longestMs, xrReportedRenderScale, xrReportedRefreshHz;
            public bool developmentBuild, xrEnabled, xrDeviceActive, frameTimingEnabled, frameTimingCpuAvailable, frameTimingGpuAvailable;
            public bool boidSectionTimingAvailable;
            public long boidSectionSamples;
            public double boidSectionMeanMs, boidSectionLongestMs;
            public string boidSectionTimingScope;
            public bool leftHandTracked, rightHandTracked;
            public bool leftPaddleTracked, rightPaddleTracked, menuLocomotionPaused, interactionSuppressed, handVisualsReadable;
            public string leftPaddleDecision, rightPaddleDecision, handTrackingBackend, paddleDecisionScope;
            public bool leftGraspValid, rightGraspValid, leftGraspArmed, rightGraspArmed;
            public bool leftPaddleFistSuppressed, rightPaddleFistSuppressed;
            public int leftGraspFingerMask, rightGraspFingerMask;
            public int nativePinchBegins, nativePinchMenuConsumed, nativePinchWorldGrabsAccepted;
            public bool nativePinchInputCurrent;
            public bool campaignGameplayAllowed, campaignCanCollect, campaignMenuOpen, campaignMenuTransitioning, campaignMenuAvailable, leaderboardOpen, entryBlocksGameplay;
            public int campaignLevel;
            public string campaignMenuPage, interactionDiagnosticScope;
            public bool leftRawFrameCurrent, rightRawFrameCurrent;
            public int leftWristFlags, rightWristFlags, leftMiddleProximalFlags, rightMiddleProximalFlags;
            public int leftCurrentValidPoseMask, rightCurrentValidPoseMask, leftCurrentTrackedPoseMask, rightCurrentTrackedPoseMask;
            public int completedStrokes, msaaSamples;
            public int leftStrokeCount, rightStrokeCount;
            public double trackingSampleSeconds, leftHandTrackedSeconds, rightHandTrackedSeconds;
            public string strokeCountScope, trackingDurationScope;
            public float movementSpeed, eyeHeightAboveSeabed, urpRenderScale;
            public float handTurnSpeedDegreesPerSecond, handTurnDegreesTotal, handTurnAbsoluteDegreesTotal;
            public string handTurnState;
            public Vector3 headWorldPosition;
            public RenderPassData[] xrRenderTargets;
            public string profilerCounterScope;
            public bool drawCallsAvailable, setPassCallsAvailable, trianglesAvailable, batchesAvailable, gcAllocatedInFrameAvailable;
            public long latestDrawCalls, latestSetPassCalls, latestTriangles, latestBatches, latestGcAllocatedInFrameBytes;
            public string hudFpsScope, cpuTimingScope, gpuTimingScope, gpuTimingSource;
            public bool hudLoopFpsAvailable, gpuTimingAvailable, xrDisplayGpuAvailable, xrStatsGpuAvailable;
            public int hudFpsWindowSamples;
            public double hudLoopFps, hudFpsWindowSeconds, latestGpuMs, latestXrDisplayGpuMs, latestXrStatsGpuMs;
        }
    }
}

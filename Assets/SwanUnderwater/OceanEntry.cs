using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SwanUnderwater
{
    /// <summary>A tracked MR fish encounter, deep-blue passage and two animated calligraphic chapter titles.</summary>
    public sealed class OceanEntry : MonoBehaviour
    {
        public static OceanEntry Instance { get; private set; }
        public static bool IsLoading => Instance != null;
        public static bool BlocksGameplay => Instance != null && !Instance.WorldPlayable;
        public int RenderedFrames { get; private set; }
        public float DisplayProgress => progress;
        public float TargetProgress => progress;
        public string Stage => sequence.Current.ToString();
        public bool HasFailed => sequence.Current == OceanEntrySequence.Beat.Failed;
        public bool IsReplay { get; private set; }
        public bool SkipRequested => sequence.SkipRequested;
        public bool CanOperatePlayback => isActiveAndEnabled && !replayTerminated && !appPaused && appFocused && !HasFailed && !sequence.Finished;
        public bool WorldRevealed => sequence.WorldRevealed;
        public bool WorldPlayable => ready && cameraRestored && sequence.ControlsAllowed && (!IsReplay || sequence.Finished);
        // World art can align to the same stable, naturally tracked direction as the invitation.
        public bool TryGetEncounterAnchor(out Vector3 eye, out Vector3 flatForward)
        {
            eye = encounterEye; flatForward = encounterForward;
            return anchored;
        }
        /// <summary>The tracked camera has submitted a completely opaque Warp frame.
        /// World initialization can now run without competing with the visible guide fish.</summary>
        public bool WorldBuildAllowed => fullCoverRendered && !HasFailed;
        public float BeatProgress => sequence.Progress;
        public float CinematicVisibleTime => sequence.TotalVisibleTime;
        public OceanEntrySequence.Beat CinematicPhase => sequence.Current;
        public const int IntroLayer = 30;
        public const string WelcomeText = "《欢迎来到梦幻海底世界》";
        public const string ChapterText = "《第一关，贝壳寻宝》";
        public const string SwimHintText = "双手划动可以前进";

        readonly OceanEntrySequence sequence = new OceanEntrySequence();
        Camera view;
        EntryPassthrough passthrough;
        Action<bool> replayEnded;
        bool replayTerminated;
        OceanVisionOSImmersion visionImmersion;
        IntroGuideFish guide;
        GameObject subtitleRoot, tunnel;
        Canvas canvas;
        OceanCinematicTitles titles;
        Material textMaterial, tunnelMaterial, trailMaterial;
        TrailRenderer trail;
        int originalMask;
        CameraClearFlags originalClear;
        Color originalBackground;
        Vector3 encounterEye, encounterForward, encounterRight, fishPosition, turnStart, dashStart, dashDirection;
        Vector3 inviteOrigin, inviteForward, inviteStart, inviteVelocity, turnControl, turnExitControl, turnEnd;
        Quaternion fishRotation, turnRotation, inviteRotation;
        float inviteOrbitTime;
        double startedAt, nextLog, retryAt;
        float progress;
        bool ready, anchored, firstRendered, cameraRestored, appPaused, appFocused = true;
        bool fullCoverPrepared, fullCoverRendered;
        bool resetCinematicDelta;
#if UNITY_EDITOR
        // A display-provider seam for lifecycle checks; no substitute for a rendered XR frame.
        Func<bool> visionOSDisplayReadyForChecks = null;
#endif
        OceanEntrySequence.Beat timingBeat;
        GapStats loopGaps, renderGaps;
        double lastLoopAt, lastRenderAt;
        int timingInterruptions;
        bool timingActive;
        string resourceStage = "开始";

        // Scalar counters only: no per-frame allocation/logging. These are application callback
        // gaps, not GPU or compositor presentation timings; exclude focus/pause gaps explicitly.
        struct GapStats
        {
            public int count, over22, over33, over50;
            public double sum, maximum;
            public void Add(double seconds)
            {
                if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
                count++; sum += seconds; maximum = Math.Max(maximum, seconds);
                if (seconds > 1.0 / 45.0) over22++;
                if (seconds > 1.0 / 30.0) over33++;
                if (seconds > .05) over50++;
            }
            public string Describe() => $"n={count},mean_ms={(count > 0 ? sum * 1000 / count : 0):F3},max_ms={maximum * 1000:F3},over22={over22},over33={over33},over50={over50}";
        }

        public static OceanEntry Show(Camera camera)
            => Create(camera, false, null);

        /// <summary>Replays presentation only, with an already prepared world. No campaign,
        /// tutorial, score, save or leaderboard initialization belongs to this path.</summary>
        public static OceanEntry ShowReplay(Camera camera, Action<bool> ended)
            => Create(camera, true, ended);

        static OceanEntry Create(Camera camera, bool replay, Action<bool> ended)
        {
            if (camera == null) throw new InvalidOperationException("Ocean entry requires the existing tracked camera.");
            if (Instance != null) throw new InvalidOperationException("Ocean entry is already active.");
            var host = new GameObject("Clownfish passage");
            var entry = host.AddComponent<OceanEntry>();
            Instance = entry;
            entry.IsReplay = replay; entry.replayEnded = ended;
            try { entry.Build(camera); if (replay) entry.Complete(); return entry; }
            catch { entry.replayEnded = null; ReleaseObject(host); Instance = null; throw; }
        }

        public bool RequestSkip()
        {
            if (!CanOperatePlayback || !sequence.RequestSkip()) return false;
            if (guide != null) guide.SetVisible(false);
            if (trail != null) { trail.emitting = false; trail.Clear(); }
            if (titles != null) titles.Hide();
            Debug.Log("[OceanEntry] skip_requested ready=" + ready + " replay=" + IsReplay + "; opaque_render_and_readiness_still_required");
            return true;
        }

        /// <summary>A deliberate menu/pickup action takes visual priority over optional playable captions.</summary>
        public static void DismissPlayableTitles()
        {
            OceanEntry entry = Instance;
            if (entry == null || !entry.WorldPlayable || !entry.sequence.DismissPlayableTitles()) return;
            if (entry.titles != null) entry.titles.Hide();
            Debug.Log($"[OceanEntry] playable_titles_dismissed t={Time.realtimeSinceStartupAsDouble:F3}; deliberate interaction, world already ready");
            entry.BeatChanged();
        }

        void Build(Camera camera)
        {
            view = camera;
            originalMask = camera.cullingMask;
            originalClear = camera.clearFlags;
            originalBackground = camera.backgroundColor;
            startedAt = Time.realtimeSinceStartupAsDouble;
            var assets = Resources.Load<OceanEntryAssets>("Loading/OceanEntryAssets");
            if (assets == null || assets.font == null) throw new InvalidOperationException("Missing intro font; run StartupSetup.Apply.");
            view.cullingMask = 1 << IntroLayer;
            passthrough = EntryPassthrough.Begin(view);
            visionImmersion = OceanVisionOSImmersion.Begin(view);
#if UNITY_EDITOR
            // A neutral preview background, explicitly not a simulated camera feed.
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.055f,.065f,.080f,1f);
#endif
            guide = IntroGuideFish.Create(transform);
            if (guide == null) throw new InvalidOperationException("Missing clownfish guide.");
            SetLayer(guide.gameObject);
            guide.SetVisible(false);
            BuildTunnel();
            BuildSubtitles(assets.font);
            BuildTrail();
            // Keep the fish encounter cinematic-only, without a skip prompt or pointer target.
            RenderPipelineManager.endCameraRendering += Rendered;
            Debug.Log($"[OceanEntry] shown frame={Time.frameCount} t={startedAt:F3}; MR fish encounter; no loading poster/bar");
        }

        void BuildTunnel()
        {
            tunnelMaterial = new Material(Resources.Load<Shader>("Shaders/OceanEntryWarp")) { name = "Deep blue passage" };
            tunnelMaterial.SetFloat("_Opacity", 0f);
            tunnel = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tunnel.name = "Surrounding deep blue passage";
            tunnel.transform.SetParent(transform, false);
            tunnel.transform.localScale = Vector3.one * 28f;
            ReleaseObject(tunnel.GetComponent<Collider>());
            var renderer = tunnel.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = tunnelMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            SetLayer(tunnel);
            tunnel.SetActive(false);
        }

        void BuildSubtitles(Font font)
        {
            Font titleFont = Resources.Load<Font>("Loading/OceanTitleFont");
            if (titleFont == null) throw new InvalidOperationException("Missing calligraphic intro font.");
            textMaterial = new Material(Resources.Load<Shader>("PerformanceHudUI")) { name = "Ocean title UI", renderQueue = 4100 };
            titles = OceanCinematicTitles.Create(view.transform, view, titleFont, font, textMaterial, IntroLayer);
            subtitleRoot = titles.gameObject;
            canvas = subtitleRoot.GetComponent<Canvas>();
        }

        void BuildTrail()
        {
            trailMaterial = new Material(Resources.Load<Shader>("Shaders/OceanEntryTrail")) { name = "Fish light wake" };
            trail = guide.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = trailMaterial;
            trail.widthCurve = AnimationCurve.Linear(0f, .035f, 1f, 0f);
            trail.time = .7f;
            trail.minVertexDistance = .025f;
            trail.numCornerVertices = 2;
            trail.emitting = false;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.startColor = new Color(.2f, .9f, 1f, .55f);
            trail.endColor = new Color(.15f, .15f, .9f, 0f);
        }

        static void SetLayer(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = IntroLayer;
        }

        void Rendered(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view || !isActiveAndEnabled || HasFailed || replayTerminated) return;
            if (UsesVisionOSDisplayGate() && (appPaused || !appFocused || !CanDisplayCinematic()))
                { SuspendCinematic(); return; }
            RenderedFrames++;
            double now = Time.realtimeSinceStartupAsDouble;
            if (timingActive && !appPaused && appFocused)
            {
                if (lastRenderAt > 0) renderGaps.Add(now - lastRenderAt);
                lastRenderAt = now;
            }
            if (!fullCoverRendered && fullCoverPrepared && !appPaused && appFocused)
            {
                fullCoverRendered = true;
                if (visionImmersion != null) visionImmersion.EnterOcean();
                Debug.Log($"[OceanEntry] world_build_allowed opaque_rendered frame={Time.frameCount} t={now:F3}");
            }
            if (firstRendered) return;
            firstRendered = true;
            Debug.Log($"[OceanEntry] first_render frame={Time.frameCount} t={Time.realtimeSinceStartupAsDouble:F3}");
        }

        public void SetProgress(float value, string label)
        {
            if (HasFailed || ready) return;
            progress = Mathf.Max(progress, Mathf.Clamp01(value));
            if (resourceStage != label)
                Debug.Log($"[OceanEntry] resource_stage={label} progress={progress:F3} t={Time.realtimeSinceStartupAsDouble:F3}");
            resourceStage = label;
        }

        public void Complete()
        {
            if (HasFailed) return;
            ready = true;
            progress = 1f;
            Debug.Log($"[OceanEntry] content_ready frame={Time.frameCount} t={Time.realtimeSinceStartupAsDouble:F3}");
        }

        public void Fail()
        {
            FlushTiming("failed");
            fullCoverPrepared = fullCoverRendered = false;
            SuspendCinematic();
            sequence.Fail();
            if (guide != null) guide.SetVisible(false);
            if (trail != null) { trail.emitting = false; trail.Clear(); }
            if (tunnel != null) tunnel.SetActive(false);
            if (titles != null) titles.ShowError("加载暂未完成  请重新进入");
            if (passthrough != null) passthrough.Restore();
            if (view != null) { view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.005f,.02f,.05f,1f); }
            Debug.LogError($"[OceanEntry] initialization failed before world readiness at {resourceStage}");
        }

        void Update()
        {
            TickCinematic(Time.unscaledDeltaTime);
        }

        void TickCinematic(float frameDelta)
        {
            if (!isActiveAndEnabled || view == null || HasFailed || replayTerminated) return;
            if (appPaused || !appFocused)
            {
                if (UsesVisionOSDisplayGate()) SuspendCinematic();
                return;
            }
            if (!cameraRestored && !passthrough.IsActive && Time.realtimeSinceStartupAsDouble >= retryAt)
            {
                retryAt = Time.realtimeSinceStartupAsDouble+.5;
                passthrough.ResumeIfNeeded();
            }
            if (!CanDisplayCinematic()) { SuspendCinematic(); return; }
            if (RenderedFrames < 2) return;
            // The engine delta can span an unavailable display or suspension. The first
            // usable frame rebuilds the clock baseline; only following frames count.
            // Keep the retained pose for this baseline frame as well: the guide's
            // own animation clock reads the engine delta when Pose is called.
            if (resetCinematicDelta) { resetCinematicDelta = false; return; }
            if (!anchored) AnchorEncounter();
            double loopAt = Time.realtimeSinceStartupAsDouble;
            if (lastLoopAt > 0) loopGaps.Add(loopAt - lastLoopAt);
            lastLoopAt = loopAt;
            if (sequence.Step(frameDelta, ready && (!sequence.SkipRequested || fullCoverRendered)))
            { BeatChanged(); if (sequence.Finished) return; }
            float t = sequence.Progress, time = sequence.TimeInBeat;
            var beat = sequence.Current;
            if (beat == OceanEntrySequence.Beat.Approach)
            {
                Vector3 start = encounterEye + encounterForward * 1.9f - encounterRight * .85f - Vector3.up * .18f;
                Vector3 bend = encounterEye + encounterForward * 1.35f - encounterRight * .55f - Vector3.up * .12f;
                Vector3 end = encounterEye + encounterForward * 1.18f - Vector3.up * .12f;
                float s = Smooth(t);
                fishPosition = (1-s)*(1-s)*start + 2*(1-s)*s*bend + s*s*end;
                Vector3 tangent = 2*(1-s)*(bend-start) + 2*s*(end-bend);
                fishRotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                float speed = tangent.magnitude * (6f * t * (1f - t) / sequence.Duration);
                guide.Pose(fishPosition, fishRotation, speed, 1f);
            }
            else if (beat == OceanEntrySequence.Beat.Invite || beat == OceanEntrySequence.Beat.Wiggle)
            {
                float frameDt = frameDelta;
                float dt = float.IsNaN(frameDt) || float.IsInfinity(frameDt) ? 0f : Mathf.Clamp(frameDt, 0f, .05f);
                // Swim in the real-space encounter area established at entry. Rotating the HMD
                // must not drag the fish's path sideways or reverse its travel under a steady tail.
                Vector3 right = Vector3.Cross(Vector3.up, inviteForward);
                float orbitTime = beat == OceanEntrySequence.Beat.Invite ? time : inviteOrbitTime + time;
                if (beat == OceanEntrySequence.Beat.Invite) inviteOrbitTime = time;
                OceanEntrySequence.SwimOrbit(orbitTime, out float phase, out float orbitRate);
                float sin = Mathf.Sin(phase), cos = Mathf.Cos(phase);
                float enter = Smooth(Mathf.Clamp01(orbitTime / 1.2f));
                // A small, steady arc lets the original reef fish's slow tail wave carry the scene.
                Vector3 desired = inviteOrigin + right * (.42f * sin)
                    + inviteForward * (1.18f + .26f * (1f - cos))
                    + Vector3.up * (-.12f + .012f * sin);
                Vector3 previous = fishPosition;
                fishPosition = Vector3.Lerp(inviteStart, desired, enter);
                inviteVelocity = dt > .00001f ? (fishPosition - previous) / dt : Vector3.zero;
                Vector3 tangent = right * (.42f * cos) + inviteForward * (.26f * sin)
                    + Vector3.up * (.012f * cos);
                // Derive heading from the smooth world-space arc, including its arrival blend.
                float blendTime = Mathf.Clamp01(orbitTime / 1.2f);
                float enterRate = 6f * blendTime * (1f - blendTime) / 1.2f;
                Vector3 headingVelocity = tangent * orbitRate * enter
                    + (desired - inviteStart) * enterRate;
                Vector3 heading = headingVelocity.sqrMagnitude > .000025f
                    ? headingVelocity.normalized : fishRotation * Vector3.forward;
                float turn = Vector3.Dot(Vector3.Cross(fishRotation * Vector3.forward, heading), Vector3.up);
                Quaternion swimming = Quaternion.LookRotation(heading, Vector3.up)
                    * Quaternion.AngleAxis(Mathf.Clamp(-turn * 8f, -3f, 3f), Vector3.forward);
                Quaternion desiredRotation = Quaternion.Slerp(inviteRotation, swimming, enter);
                fishRotation = Quaternion.RotateTowards(fishRotation, desiredRotation, 85f * dt);
                guide.Pose(fishPosition, fishRotation, inviteVelocity.magnitude);
            }
            else if (beat == OceanEntrySequence.Beat.Turn)
            {
                // Match the swimming velocity at entry and a gentle 0.4 m/s departure at exit.
                float inv = 1f - t;
                fishPosition = inv * inv * inv * turnStart + 3f * inv * inv * t * turnControl
                    + 3f * inv * t * t * turnExitControl + t * t * t * turnEnd;
                Vector3 velocity = 3f * (inv * inv * (turnControl - turnStart)
                    + 2f * inv * t * (turnExitControl - turnControl)
                    + t * t * (turnEnd - turnExitControl)) / sequence.Duration;
                Quaternion alongCurve = velocity.sqrMagnitude > .0001f
                    ? Quaternion.LookRotation(velocity, Vector3.up) : fishRotation;
                fishRotation = Quaternion.Slerp(turnRotation, alongCurve, Smooth(Mathf.Clamp01(time / .35f)));
                guide.Pose(fishPosition, fishRotation, Mathf.Clamp(velocity.magnitude, .18f, .65f),
                    Mathf.Lerp(1f, 1.2f, Smooth(t)));
            }
            else if (beat == OceanEntrySequence.Beat.Dash)
            {
                float forwardDistance = .4f * time + .125f * time * time;
                fishPosition = dashStart + dashDirection * forwardDistance + Vector3.up * (.04f * t * t * t);
                Vector3 velocity = dashDirection * (.4f + .25f * time)
                    + Vector3.up * (.12f * t * t / sequence.Duration);
                guide.Pose(fishPosition, Quaternion.LookRotation(velocity), Mathf.Min(velocity.magnitude, 1.15f),
                    Mathf.Lerp(1.2f, 1.5f, Smooth(t)));
            }
            float opacity = beat == OceanEntrySequence.Beat.Dash ? .48f*Smooth(t)
                : beat == OceanEntrySequence.Beat.Warp ? Mathf.Lerp(.48f,1f,Smooth(Mathf.Min(1f,t/.62f)))
                : beat == OceanEntrySequence.Beat.Reveal ? 1f-Smooth(t) : 0f;
            tunnel.SetActive(opacity > .001f);
            tunnel.transform.SetPositionAndRotation(view.transform.position, Quaternion.LookRotation(dashDirection.sqrMagnitude>.1f?dashDirection:encounterForward));
            tunnelMaterial.SetFloat("_Opacity",opacity);
            fullCoverPrepared = beat == OceanEntrySequence.Beat.Warp && opacity >= .9999f;
            tunnelMaterial.SetFloat("_FlowTime",sequence.TotalVisibleTime);
            tunnelMaterial.SetFloat("_Rush",beat == OceanEntrySequence.Beat.Reveal ? 1-t : 1f);
            if (beat == OceanEntrySequence.Beat.Welcome || beat == OceanEntrySequence.Beat.Chapter)
                titles.Animate(time, sequence.Duration);
            else titles.Hide();
            if (Time.realtimeSinceStartupAsDouble >= nextLog)
            {
                nextLog = Time.realtimeSinceStartupAsDouble+1;
                Debug.Log($"[OceanEntry] beat={beat} beatTime={time:F2} resources={progress:F3} rendered={RenderedFrames} vstRequest={passthrough.IsActive} t={Time.realtimeSinceStartupAsDouble:F3}");
            }
        }

        void AnchorEncounter()
        {
            anchored = true;
            encounterEye = view.transform.position;
            encounterForward = FlatForward(view.transform);
            encounterRight = Vector3.Cross(Vector3.up,encounterForward);
            dashDirection = encounterForward;
            guide.SetVisible(true);
            BeginTiming();
            Debug.Log($"[OceanEntry] beat_start=Approach t={Time.realtimeSinceStartupAsDouble:F3}; anchor={encounterEye}");
        }
        static Vector3 FlatForward(Transform t)
        {
            Vector3 f = Vector3.ProjectOnPlane(t.forward,Vector3.up);
            return f.sqrMagnitude>.02f?f.normalized:Vector3.forward;
        }
        static float Smooth(float value) => value*value*(3f-2f*value);

        void BeatChanged()
        {
            FlushTiming("phase_end");
            BeginTiming();
            var beat = sequence.Current;
            Debug.Log($"[OceanEntry] beat_start={beat} t={Time.realtimeSinceStartupAsDouble:F3} ready={ready}");
            if (beat == OceanEntrySequence.Beat.Invite)
            {
                inviteOrigin = encounterEye; inviteForward = encounterForward;
                inviteStart = fishPosition; inviteRotation = fishRotation;
                inviteVelocity = Vector3.zero; inviteOrbitTime = 0f;
                Debug.Log($"[OceanEntry] reef_invite lap={OceanEntrySequence.SwimOrbitSeconds:F1}s lateral=.84m depth=1.18..1.70m; world-anchored relaxed cruise, original reef clownfish");
            }
            if (beat == OceanEntrySequence.Beat.Wiggle)
                Debug.Log($"[OceanEntry] gentle_farewell duration={sequence.Duration:F1}s; same reef swim, then turn forward");
            if (beat == OceanEntrySequence.Beat.Turn)
            {
                turnStart = fishPosition; turnRotation = fishRotation;
                dashDirection = FlatForward(view.transform);
                turnControl = turnStart + Vector3.ClampMagnitude(inviteVelocity, 1.25f)
                    * (OceanEntrySequence.DurationOf(OceanEntrySequence.Beat.Turn) / 3f);
                turnEnd = turnStart + dashDirection * .5f + Vector3.ClampMagnitude(inviteVelocity, .65f) * .7f
                    + Vector3.up * .02f;
                turnExitControl = turnEnd - dashDirection * (.4f * OceanEntrySequence.DurationOf(OceanEntrySequence.Beat.Turn) / 3f);
            }
            if (beat == OceanEntrySequence.Beat.Dash) { dashStart = fishPosition; trail.Clear(); trail.emitting = true; }
            if (beat == OceanEntrySequence.Beat.Warp) { guide.SetVisible(false); trail.emitting = false; }
            if (beat == OceanEntrySequence.Beat.Reveal)
            {
                RestoreCamera();
                Debug.Log($"[OceanEntry] scene_revealed t={Time.realtimeSinceStartupAsDouble:F3}; opaque transition before VST release");
            }
            if (beat == OceanEntrySequence.Beat.Welcome)
            {
                titles.Show(false);
                Debug.Log($"[OceanEntry] world_playable t={Time.realtimeSinceStartupAsDouble:F3}; reveal complete, captions continue without blocking controls");
            }
            if (beat == OceanEntrySequence.Beat.Chapter) titles.Show(true);
            if (beat == OceanEntrySequence.Beat.Finished)
            {
                Debug.Log($"[OceanEntry] transition_finished frames={RenderedFrames} elapsed={Time.realtimeSinceStartupAsDouble-startedAt:F3}s");
                // Completion is a state transition, not a side effect of Unity delivering
                // destruction callbacks (ordinary EditMode objects need not receive them).
                RestoreCamera();
                if (IsReplay) NotifyReplayEnded();
                ReleaseObject(gameObject);
            }
        }

        void RestoreCamera()
        {
            if (cameraRestored) return;
            if (passthrough != null) passthrough.Restore();
            if (view != null)
            {
                view.cullingMask = originalMask;
                view.clearFlags = originalClear;
                view.backgroundColor = originalBackground;
            }
            cameraRestored = true;
        }
        void BeginTiming()
        {
            timingBeat = sequence.Current;
            loopGaps = default; renderGaps = default;
            lastLoopAt = lastRenderAt = 0; timingInterruptions = 0;
            timingActive = sequence.Current < OceanEntrySequence.Beat.Finished;
        }
        void FlushTiming(string reason)
        {
            if (!timingActive) return;
            timingActive = false;
            Debug.Log($"[OceanEntryTiming] beat={timingBeat} reason={reason} loop=({loopGaps.Describe()}) render_callback=({renderGaps.Describe()}) focus_pause_breaks={timingInterruptions}; application intervals, not compositor/GPU timing");
        }
        void BreakTimingWindow()
        {
            lastLoopAt = lastRenderAt = 0;
            if (timingActive) timingInterruptions++;
        }

        bool UsesVisionOSDisplayGate()
        {
#if UNITY_EDITOR
            return visionOSDisplayReadyForChecks != null;
#elif UNITY_VISIONOS
            return true;
#else
            return false;
#endif
        }

        bool CanDisplayCinematic()
        {
#if UNITY_EDITOR
            if (visionOSDisplayReadyForChecks != null) return visionOSDisplayReadyForChecks();
#endif
#if UNITY_VISIONOS && !UNITY_EDITOR
            // The SDK's ready flag alone can outlive its original compositor. Require
            // the current running display/layer as well, including after the reveal.
            if (!OceanXRPlatform.IsVisionOS) return false;
            var loader = UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager.activeLoader;
            var display = loader.GetLoadedSubsystem<UnityEngine.XR.XRDisplaySubsystem>();
            return display != null && display.running && OceanXRPlatform.VisionOSImmersiveSpaceReady &&
                UnityEngine.XR.VisionOS.VisionOS.GetLayerRenderer() != IntPtr.Zero &&
                (sequence.WorldRevealed || (passthrough != null && passthrough.IsActive));
#else
            return true;
#endif
        }

        void SuspendCinematic()
        {
            if (!UsesVisionOSDisplayGate() || !resetCinematicDelta) BreakTimingWindow();
            if (UsesVisionOSDisplayGate()) resetCinematicDelta = true;
            fullCoverPrepared = false;
        }

        void OnApplicationPause(bool paused) { appPaused = paused; SuspendCinematic(); }
        void OnApplicationFocus(bool focused)
        {
#if !UNITY_EDITOR
            ApplyApplicationFocus(focused);
#endif
        }
        void ApplyApplicationFocus(bool focused)
        {
            appFocused = focused;
            SuspendCinematic();
        }

#if UNITY_EDITOR
        public void ApplyFocusForValidation(bool focused) => ApplyApplicationFocus(focused);
        public float PreviewWarpOpacity => tunnelMaterial != null ? tunnelMaterial.GetFloat("_Opacity") : 0f;
        public bool PreviewContentReady => ready;
        public bool PreviewCameraRestored => cameraRestored;
        public void AttachPreviewCamera(Camera camera)
        {
            subtitleRoot.transform.SetParent(camera.transform, false);
            subtitleRoot.transform.localPosition = new Vector3(0f,.10f,2.4f);
            subtitleRoot.transform.localScale = Vector3.one*.002f;
            canvas.worldCamera = camera;
        }
#endif
        void OnDisable()
        {
            FlushTiming("disabled");
            fullCoverPrepared = fullCoverRendered = false;
            SuspendCinematic();
            RestoreCamera();
            if (IsReplay) NotifyReplayEnded();
        }
        void NotifyReplayEnded()
        {
            replayTerminated = true;
            RenderPipelineManager.endCameraRendering -= Rendered;
            if (Instance == this) Instance = null;
            var ended = replayEnded; replayEnded = null;
            ended?.Invoke(sequence.Finished && ready && cameraRestored);
        }
        void OnDestroy()
        {
            RestoreCamera();
            RenderPipelineManager.endCameraRendering -= Rendered;
            if (passthrough != null) passthrough.Dispose();
            if (guide != null) guide.Dispose();
            if (titles != null) titles.Dispose();
            ReleaseObject(textMaterial); ReleaseObject(tunnelMaterial); ReleaseObject(trailMaterial);
            if (Instance == this) Instance = null;
            if (IsReplay) NotifyReplayEnded();
        }
        static void ReleaseObject(UnityEngine.Object item)
        { if (item != null) { if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); } }
    }
}

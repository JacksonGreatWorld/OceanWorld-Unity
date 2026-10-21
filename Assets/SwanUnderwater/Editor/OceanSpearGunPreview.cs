#if UNITY_EDITOR
using System;
using System.Collections;
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
    /// <summary>Actual world and production spear logic, driven by explicitly synthetic Editor hand samples.</summary>
    [InitializeOnLoad]
    public static class OceanSpearGunPreview
    {
        const string Key = "SwanSpearPreview.";
        const float StepSeconds = .02f;
        static RenderTexture target;
        static Texture2D pixels;
        static bool running;
        static int sampleFrame;
        static Report report = new Report();
        [Serializable] sealed class InitialPose
        {
            public string scope = "CPU-readable visible mesh vertices transformed to world space before the first spear render; rendered-seabed sampler used at every vertex and nine footprint points.";
            public int meshes, vertices;
            public Vector3 toolPosition, meshMin, meshMax, pickupMin, pickupMax;
            public float footprintSurfaceMaximum, boundsFloorGap, minimumVertexFloorGap, maximumVertexFloorGap;
        }
        [Serializable] sealed class Report
        {
            public bool success;
            public int assertions;
            public string detail;
            public string scope = "Actual Unity world, spear mesh, production trigger gate, swept trigger/obstacle collisions, permanent ownership, twenty-round settlement, collection and file save/load. Hand positions, time steps and the explicit blocker are synthetic Editor test inputs. Not headset tracking, physical reach, comfort, audible sound or device performance acceptance. Extra close-up cameras never write the main camera pose.";
            public List<string> screenshots = new List<string>();
        }
        static OceanSpearGunPreview() { if (SessionState.GetBool(Key + "active", false)) Attach(); }
        public static void Capture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Separate batch Editor required.");
            string output = Path.GetFullPath("Validation/iteration-20/campaign/spear-preview"), saveDirectory = null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Rendered preview owns process exit.");
                if (args[i] == "-codexSpearOutput" && i + 1 < args.Length) output = Path.GetFullPath(args[++i]);
                else if (args[i] == "-codexCampaignSaveDir" && i + 1 < args.Length) saveDirectory = Path.GetFullPath(args[++i]);
            }
            if (saveDirectory == null || saveDirectory == Path.GetFullPath(Application.persistentDataPath))
                throw new ArgumentException("Pass -codexCampaignSaveDir with a fresh private test folder.");
            if (File.Exists(Path.Combine(saveDirectory, OceanSaveStore.FileName)) || File.Exists(Path.Combine(saveDirectory, OceanSaveStore.BackupFileName)))
                throw new IOException("Preview refuses an existing campaign save.");
            Directory.CreateDirectory(output);
            if (Directory.GetFiles(output).Length > 0) throw new IOException("Use a fresh spear output folder.");
            SessionState.SetString(Key + "output", output);
            SessionState.SetString(Key + "started", DateTime.UtcNow.Ticks.ToString());
            SessionState.SetBool(Key + "active", true); Attach();
            EditorTests.OceanSpearTriggerTests.RunAll();
            EditorTests.OceanSharkMotionTests.RunAll();
            EditorTests.RenderedSeabedSegmentTests.RunAll();
            EditorSceneManager.OpenScene("Assets/Scenes/UnderwaterWorld.unity");
            EditorApplication.isPlaying = true;
        }
        static void Attach()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= Error; Application.logMessageReceived += Error;
        }
        static void Error(string message, string stack, LogType type)
        { if (type == LogType.Exception || type == LogType.Assert) Finish(false, message + "\n" + stack); }
        static void Tick()
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            try
            {
                if (DateTime.UtcNow.Ticks - long.Parse(SessionState.GetString(Key + "started", "0")) > TimeSpan.FromSeconds(300).Ticks)
                    throw new TimeoutException("Spear scene preview exceeded 300 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                Camera view = Camera.main; if (view == null) return;
                if (target == null)
                { target = new RenderTexture(1280, 960, 24); target.Create(); pixels = new Texture2D(1280, 960, TextureFormat.RGB24, false); }
                if (OceanEntry.Instance != null)
                {
                    if (OceanEntry.Instance.HasFailed) throw new InvalidOperationException("World initialization failed.");
                    Submit(view); // The opaque warp frame must render before deferred world construction is allowed.
                    return;
                }
                if (running) return;
                var hunt = Object.FindFirstObjectByType<TreasureHunt>();
                if (hunt == null || !hunt.CoreActive || hunt.Campaign == null) return;
                running = true; hunt.StartCoroutine(Exercise(hunt, view));
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }
        static IEnumerator Exercise(TreasureHunt hunt, Camera view)
        {
            yield return PrepareLevelFive(hunt, "initial spear exercise");
            try
            {
                Require(hunt.GameplayActive, "level-five gameplay available after menu transition");
                RunLevelFive(hunt.Campaign, view, Require, Save, SessionState.GetString(Key + "output", ""));
                hunt.Campaign.SaveNow();
                Require(hunt.Campaign.LastSaveSucceeded, "production controller saves final result");
                hunt.StartCoroutine(ExerciseMultiple(hunt, view));
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        static IEnumerator ExerciseMultiple(TreasureHunt hunt, Camera view)
        {
            yield return PrepareLevelFive(hunt, "independent shark exercise");
            try
            {
                RunMultiShark(hunt.Campaign, view, Require, Save);
                hunt.Campaign.SaveNow();
                Require(hunt.Campaign.LastSaveSucceeded, "multi-shark state saved through real controller");
                Finish(true, "Real scene swept collisions for every shark; independent progress, flee, swimming pose, persistence and shared ammo. Synthetic time/aim, no physical-hand or headset performance claim.");
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        static IEnumerator PrepareLevelFive(TreasureHunt hunt, string context)
        {
            var campaign = hunt.Campaign;
            // Fresh private saves may offer automatic practice. This Editor-only entry
            // finishes that prerequisite instead of bypassing production gameplay gates.
            campaign.Panel.SkipTutorialForValidation();
            campaign.HandleAction("start_level", 5);
            double switchDeadline = Time.realtimeSinceStartupAsDouble + 120;
            while (campaign.EnvironmentTransitioning)
            {
                if (Time.realtimeSinceStartupAsDouble >= switchDeadline)
                    throw new TimeoutException("Spear preview level-five switch exceeded 120 seconds: " + context);
                yield return null;
            }
            Require(campaign.State.Current.level == 5, "production level-five switch completed: " + context);
            yield return null; yield return null;
            campaign.Panel.SkipTutorialForValidation();
            double inputDeadline = Time.realtimeSinceStartupAsDouble + 4;
            while (!campaign.GameplayAllowed && Time.realtimeSinceStartupAsDouble < inputDeadline) yield return null;
            Require(campaign.GameplayAllowed && hunt.GameplayActive,
                "real menu/input isolation completed before synthetic spear samples: " + context);
        }

        static void RunMultiShark(OceanCampaignController campaign, Camera view, Action<bool,string> check, Action<Camera,string> capture)
        {
            var actors = campaign.Creatures; var state = campaign.State; var gun = actors.SpearGun;
            var sharks = new List<OceanCampaignCreatures.CreatureTarget>();
            foreach (var target in actors.Targets) if (target.IsShark) sharks.Add(target);
            check(sharks.Count >= 3, "at least three real independently shootable sharks");
            actors.TickGameplay(StepSeconds, true); Physics.SyncTransforms();
            check(actors.TryGrasp(gun.PickupBounds.center + Vector3.up * .12f, true), "multi run acquires tool");
            var reef = SceneReefBounds(); int frame = 0;
            foreach (var shark in sharks)
            {
                Vector3 from = shark.Body.transform.position; Quaternion before = shark.Body.transform.rotation;
                int otherEscapes = 0;
                foreach (var other in sharks) if (other != shark) otherEscapes += other.Body.GetComponent<OceanSharkMotion>().EscapeCount;
                ShotPose(shark, reef, out Vector3 palm, out Vector3 aim);
                PressTrigger(gun, palm, aim, (++frame % 2) == 0, check); ResolveFlights(gun);
                check(state.SharkHitsFor(shark.SharkId) == 1 && state.Current.sharkHits == 1, "real sweep scores only current shark, best summary not pooled: " + shark.SharkId);
                int othersNow = 0;
                foreach (var other in sharks) if (other != shark) othersNow += other.Body.GetComponent<OceanSharkMotion>().EscapeCount;
                check(otherEscapes == othersNow, "hit cannot trigger another shark escape");
                for (int step = 0; step < 260; step++) { actors.TickGameplay(StepSeconds, true); Physics.SyncTransforms(); }
                var motion = shark.Body.GetComponent<OceanSharkMotion>();
                check(motion.EscapeCount == 1 && Vector3.Distance(from, shark.Body.transform.position) > .60f, "actual struck shark swims out of original position: " + shark.SharkId);
                check(shark.Body.transform.up.y > .96f && Mathf.Abs(shark.Body.transform.forward.y) < .22f, "actual shark remains upright with bounded pitch: " + shark.SharkId);
                check(!state.CatchShark(shark.SharkId), "one hit on multiple sharks cannot unlock capture");
            }
            check(state.Current.sharkHits == 1 && state.Current.spearShotsFired == sharks.Count, "all hits remain independent under one ammo budget");
            for (int i = 0; i < sharks.Count; i++)
            {
                int expected = 1; check(state.SharkHitsFor(sharks[i].SharkId) == expected, "untouched individual counter survives another shark shot");
            }
            var ambient = sharks[1];
            ShotPose(ambient, reef, out Vector3 extraPalm, out Vector3 extraAim);
            PressTrigger(gun, extraPalm, extraAim, false, check); ResolveFlights(gun);
            check(state.SharkHitsFor(ambient.SharkId) == 2 && state.SharkHitsFor(0) == 1 && state.Current.sharkHits == 2,
                "ambient second hit remains distinct from primary and other ambient shark");
            string directory = Path.Combine(SessionState.GetString(Key + "output", ""), "multi-shark-save");
            check(OceanSaveStore.TrySave(directory, state.Save, out string error), "multi progress saves: " + error);
            check(OceanSaveStore.TryLoad(directory, out OceanSaveData saved, out _, out error), "multi progress loads: " + error);
            var restored = new OceanCampaignState(saved);
            foreach (var shark in sharks) check(restored.SharkHitsFor(shark.SharkId) == state.SharkHitsFor(shark.SharkId), "individual progress restored: " + shark.SharkId);
            int ambientId = ambient.SharkId, count = sharks.Count;
            actors.ConfigureLevel(view.transform.position); actors.TickGameplay(StepSeconds, true); Physics.SyncTransforms();
            sharks.Clear(); foreach (var target in actors.Targets) if (target.IsShark) sharks.Add(target);
            check(sharks.Count == count, "reconfigure never duplicates or drops shark wrappers");
            ambient = null;
            foreach (var target in sharks)
            {
                check(target.Body.GetComponents<OceanSharkMotion>().Length == 1, "exactly one movement owner per wrapper");
                if (target.SharkId == ambientId) ambient = target;
            }
            check(ambient != null && state.SharkHitsFor(ambientId) == 2 && state.SharkHitsFor(0) == 1, "stable ambient identity after reconfigure");
            gun = actors.SpearGun;
            while (state.SharkHitsFor(ambientId) < state.SharkHitTarget)
            {
                ShotPose(ambient, reef, out extraPalm, out extraAim);
                PressTrigger(gun, extraPalm, extraAim, false, check); ResolveFlights(gun);
                for (int step = 0; step < 260 && ambient.Body.GetComponent<OceanSharkMotion>().Escaping; step++)
                { actors.TickGameplay(StepSeconds, true); Physics.SyncTransforms(); }
            }
            for (int step = 0; step < 80; step++) actors.TickGameplay(StepSeconds, true);
            Physics.SyncTransforms();
            check(state.SharkHitsFor(0) == 1 && !state.GoalComplete, "primary progress unaffected by fully hit ambient shark");
            var closeRoot = new GameObject("Synthetic ambient shark close-up");
            try
            {
                var close = closeRoot.AddComponent<Camera>(); close.CopyFrom(view); close.enabled = false;
                close.stereoTargetEye = StereoTargetEyeMask.None; close.GetUniversalAdditionalCameraData().allowXRRendering = false;
                Near(close, ambient.WorldBounds.center, new Vector3(4f, 1.5f, -5f));
                capture(close, "spear-07-ambient-individual-progress");
            }
            finally { Object.DestroyImmediate(closeRoot); }
            check(actors.TryGrasp(ambient.WorldBounds.center, true) && state.GoalComplete && state.SharkCaughtFor(ambientId) && !state.SharkCaughtFor(0),
                "capture applies to this fully hit ambient shark, not primary");
            actors.ConfigureLevel(view.transform.position); Physics.SyncTransforms();
            foreach (var target in actors.Targets)
                if (target.IsShark) check(target.Body.gameObject.activeInHierarchy == (target.SharkId != ambientId), "only captured identity hidden after reconfigure");
            state.RestartLevel(); actors.ConfigureLevel(view.transform.position); actors.TickGameplay(StepSeconds, true); Physics.SyncTransforms();
            int fresh = 0;
            foreach (var target in actors.Targets)
                if (target.IsShark) { fresh++; check(state.SharkHitsFor(target.SharkId) == 0 && target.Body.gameObject.activeInHierarchy, "restart restores shark with zero individual hits"); }
            check(fresh == count && state.Current.spearShotsFired == 0 && !state.Current.sharkCaught, "restart resets only run progress and all motion wrappers");
            Debug.Log("MULTI_SHARK_SCENE_PASSED sharks="+count+" independentSweepHits=true ambientCapture=true stableReconfigure=true restart=true");
        }

        /// <summary>Shared by this focused preview and the existing full campaign preview. Starts in a fresh level five.</summary>
        public static void RunLevelFive(OceanCampaignController campaign, Camera view,
            Action<bool, string> check, Action<Camera, string> capture, string evidenceDirectory)
        {
            var state = campaign.State; var creatures = campaign.Creatures; var gun = creatures.SpearGun;
            var world = Object.FindFirstObjectByType<UnderwaterWorld>();
            check(state.Current.level == 5 && state.Current.sharkHits == 0 && !state.Current.toolOwned, "fresh fifth-level run");
            check(state.UnlimitedTime && !state.TimedOut, "fifth level remains untimed");
            check(gun != null && gun.CanPickup && gun.AmmoRemaining == 20, "actual unowned grounded weapon with twenty-round run budget");
            OceanCampaignCreatures.CreatureTarget shark = QuestShark(creatures);
            check(shark != null && shark.Body.isTrigger, "actual quest shark trigger exists");
            int sharks = 0; foreach (var target in creatures.Targets) if (target.IsShark) sharks++;
            check(sharks >= 3, "all actual great-white and hammerhead sharks join the challenge");
            creatures.TickGameplay(StepSeconds, true); Physics.SyncTransforms();
            check(creatures.TryGrasp(shark.WorldBounds.center, true) && !state.Current.sharkCaught, "cannot capture without tool and hits");
            var reefs = SceneReefBounds(); check(reefs.Count > 0, "actual reef bounds available");
            var cameraRoot = new GameObject("Synthetic spear preview close-up camera");
            var close = cameraRoot.AddComponent<Camera>(); close.CopyFrom(view); close.enabled = false;
            close.tag = "Untagged"; close.stereoTargetEye = StereoTargetEyeMask.None;
            close.fieldOfView = 50f; close.aspect = 4f / 3f; close.nearClipPlane = .03f;
            close.GetUniversalAdditionalCameraData().allowXRRendering = false;
            close.useOcclusionCulling = false;
            Vector3 headLocal = view.transform.localPosition; Quaternion headRotation = view.transform.localRotation;
            try
            {
                CheckInitialPose(gun, evidenceDirectory, check);
                Vector3 originalGround = gun.PickupBounds.center;
                Near(close, originalGround, new Vector3(.8f, .65f, -.9f));
                capture(close, "spear-01-grounded");
                capture(close, "spear-01b-grounded-second-render");
                check(creatures.TryGrasp(originalGround + Vector3.up * .12f, true), "real reachable pickup accepted");
                check(state.Current.toolOwned && gun.CurrentPhase == OceanSpearGun.Phase.Hidden && !gun.ToolTransform.gameObject.activeSelf && !gun.CanPickup,
                    "first pickup grants permanent ownership and hides tool");
                capture(close, "spear-02-owned-hidden");
                ShotPose(shark, reefs, out Vector3 palm, out Vector3 aim);
                Samples(gun, palm, aim, true, .4f, 8);
                check(!gun.Held && gun.ShotsFired == 0, "curled index cannot reveal or fire hidden gun");
                foreach (int missing in new[] { 0, 1, 2, 3 })
                {
                    ReleaseTrigger(gun, palm, aim, true);
                    check(gun.Held && gun.HeldLeft, "one left hand can equip without other hand");
                    Sample(gun, palm, aim, true, 0f, gripping: missing != 0, thumb: missing != 1, extended: missing != 2, fresh: missing != 3);
                    check(!gun.Held && !gun.ToolTransform.gameObject.activeSelf && gun.AmmoRemaining == 20, "relax/thumb/reach/tracking loss hides without refill or fire " + missing);
                    Samples(gun, palm, aim, true, .4f, 4);
                    check(!gun.Held && gun.ShotsFired == 0, "curled recovery cannot re-equip or fire");
                }
                ReleaseTrigger(gun, palm, aim, false);
                check(gun.Held && !gun.HeldLeft && gun.AmmoRemaining == 20, "one right hand independently equips same budget");
                Near(close, gun.ToolTransform.position, -aim * 1.1f + Vector3.Cross(Vector3.up, aim) * .8f + Vector3.up * .25f);
                capture(close, "spear-03-right-pose-twenty-rounds");
                Sample(gun, palm, aim, true, .4f);
                check(!gun.Held && gun.ShotsFired == 0, "switching to curled other hand cannot fire");
                ReleaseTrigger(gun, palm, aim, true);
                gun.TickForValidation(.25f, true);
                check(gun.Held && !gun.Armed && gun.AmmoRemaining == 20, "long frame clears trigger qualification without losing permanent ownership");
                Samples(gun, palm, aim, true, .4f, 5);
                check(gun.ShotsFired == 0, "long-frame recovery requires fresh extension before curl");

                OpenWaterPose(shark, out palm, out aim);
                PressTrigger(gun, palm, aim, true, check); // Miss 1: paused open-water projectile.
                check(gun.InFlight && gun.AmmoRemaining == 19, "first shot consumes one durable round");
                Vector3 paused = gun.ProjectileTransform.position;
                for (int i = 0; i < 60; i++) gun.TickForValidation(StepSeconds, false);
                check(!gun.Held && gun.InFlight && gun.ProjectileTransform.position == paused && state.Current.sharkHits == 0 && gun.AmmoRemaining == 19,
                    "menu pause hides gun, freezes flight and preserves budget");
                ResolveFlights(gun);
                check(!gun.InFlight && state.Current.sharkHits == 0, "resumed independent projectile resolves once");

                ShotPose(shark, reefs, out palm, out aim);
                var barrier = new GameObject("Synthetic spear obstruction");
                try
                {
                    barrier.transform.SetPositionAndRotation(shark.WorldBounds.center - aim * .50f, Quaternion.LookRotation(aim));
                    barrier.AddComponent<BoxCollider>().size = new Vector3(.7f, .7f, .08f); Physics.SyncTransforms();
                    PressTrigger(gun, palm, aim, false, check); ResolveFlights(gun);
                    check(gun.LastResolutionReason == "obstruction" && state.Current.sharkHits == 0 && gun.AmmoRemaining == 18, "actual box blocks second shot");
                    Samples(gun, palm, aim, false, .4f, 40);
                    check(gun.ShotsFired == 2 && gun.AmmoRemaining == 18, "held curl never autofires");
                }
                finally { barrier.SetActive(false); Object.DestroyImmediate(barrier); Physics.SyncTransforms(); }
                Vector3 floor = originalGround; floor.y = UnderwaterWorld.SampleRenderedSeabedHeight(floor.x, floor.z);
                palm = floor + Vector3.up * 1.65f; aim = new Vector3(0f, -1f, .06f).normalized;
                PressTrigger(gun, palm, aim, false, check); ResolveFlights(gun);
                check(gun.LastResolutionReason == "floor" && state.Current.sharkHits == 0 && gun.AmmoRemaining == 17, "rendered seabed stops third shot");
                check(gun.ProjectileTransform != null && Mathf.Abs(gun.ProjectileTransform.position.y - UnderwaterWorld.SampleRenderedSeabedHeight(gun.ProjectileTransform.position.x, gun.ProjectileTransform.position.z)) < .08f,
                    "actual floor hit lies on rendered surface");
                Bounds reef = reefs[0]; palm = new Vector3(reef.center.x, reef.max.y + 1.65f, reef.center.z); aim = new Vector3(0f, -1f, .001f).normalized;
                PressTrigger(gun, palm, aim, true, check); ResolveFlights(gun);
                check(gun.LastResolutionReason == "reef" && state.Current.sharkHits == 0 && gun.AmmoRemaining == 16, "actual visible reef blocks fourth shot before terrain");
                OpenWaterPose(shark, out palm, out aim);
                PressTrigger(gun, palm, aim, true, check); PressTrigger(gun, palm, aim, false, check);
                check(gun.ActiveProjectilesCount >= 2 && gun.AmmoRemaining == 14, "left and right share budget while independent shots coexist");
                Near(close, gun.ToolTransform.position + aim * .8f, Vector3.Cross(Vector3.up, aim) * 1.8f + Vector3.up * .4f - aim * .35f);
                capture(close, "spear-04-concurrent-projectiles"); ResolveFlights(gun);
                while (gun.ShotsFired < 10) { PressTrigger(gun, palm, aim, gun.ShotsFired % 2 == 0, check); ResolveFlights(gun); }
                check(state.Current.sharkHits == 0 && gun.AmmoRemaining == 10, "ten misses consume ten shared rounds without progressing shark");
                check(!gun.TryPickup(originalGround, false), "owned equipment cannot be picked again to refill");

                for (int hit = 1; hit <= OceanCampaignState.RequiredSharkHits; hit++)
                {
                    ShotPose(shark, reefs, out palm, out aim);
                    PressTrigger(gun, palm, aim, hit % 2 == 0, check); ResolveFlights(gun);
                    check(state.Current.sharkHits == hit && gun.ShotsFired == 10 + hit && gun.AmmoRemaining == 10 - hit,
                        "one real swept shark hit per spent shot " + hit);
                    check(!state.Failed, "twentieth shot resolves hit before any ammunition failure " + hit);
                    if (hit == 4) CheckRestoredRun(campaign, view, evidenceDirectory, check);
                    if (hit < 10)
                    {
                        Vector3 before = shark.Body.transform.position;
                        for (int step = 0; step < 260 && creatures.SharkEscaping; step++) { creatures.TickGameplay(StepSeconds, true); gun.TickForValidation(StepSeconds, true); Physics.SyncTransforms(); }
                        check(Vector3.Distance(before, shark.Body.transform.position) > .60f, "hit shark actually escapes and requires a new approach " + hit);
                        check(creatures.TryGrasp(shark.WorldBounds.center, false) && !state.Current.sharkCaught, "cannot capture before ten impacts");
                    }
                }
                check(gun.AmmoRemaining == 0 && state.CanCollect && !state.Failed, "twentieth successful shot leaves ten-hit shark captureable");
                int total = gun.ShotsFired; Samples(gun, palm, aim, true, 0f, 10); Samples(gun, palm, aim, true, .4f, 10);
                check(gun.ShotsFired == total && total == 20 && !gun.Held, "twenty-first input cannot fire or reveal exhausted equipment");
                Near(close, shark.WorldBounds.center, new Vector3(1.7f, .5f, -1.8f)); capture(close, "spear-05-twentieth-hit-capture-ready");
                Vector3 catchPoint = shark.WorldBounds.center;
                check(creatures.TryGrasp(catchPoint, false) && state.Current.sharkCaught && state.Stars == 2, "production empty-hand capture succeeds after twentieth shot");
                check(!creatures.TryGrasp(catchPoint, false), "captured shark cannot be replayed");
                var upgraded = new OceanCampaignState(state.Save); check(upgraded.CollectCoin(20000) && upgraded.Stars == 3, "post-goal coin still adds third star");
                CheckMissBudget(world, view, close, capture, check, evidenceDirectory);
                EditorTests.OceanSharkEscapeTests.RunInScene(world, view);
                check(EditorTests.OceanSharkEscapeTests.MinimumEscapeDistance > .60f, "independent real-scene escape suite passed");
                check(Vector3.Distance(view.transform.localPosition, headLocal) < .000001f && Quaternion.Angle(view.transform.localRotation, headRotation) < .001f,
                    "synthetic exercise never writes tracked head local pose");
                Debug.Log($"SPEAR_SCENE_EXERCISE_PASSED shots={gun.ShotsFired} hits={state.Current.sharkHits} stars={state.Stars} durableAmmo={state.SpearAmmoRemaining} headLocalUnchanged=true");
            }
            finally { Object.DestroyImmediate(cameraRoot); }
        }
        static OceanCampaignCreatures.CreatureTarget QuestShark(OceanCampaignCreatures creatures)
        { foreach (var target in creatures.Targets) if (target.IsShark) return target; return null; }
        static void ShotPose(OceanCampaignCreatures.CreatureTarget shark, List<Bounds> reefs, out Vector3 palm, out Vector3 aim)
        {
            Vector3 center = shark.WorldBounds.center;
            for (int i = 0; i < 24; i++)
            {
                aim = Vector3.ProjectOnPlane(Quaternion.AngleAxis(i * 15f, Vector3.up) * shark.Body.transform.right, Vector3.up).normalized;
                palm = center - aim * 1.35f - Vector3.up * OceanSpearGunVisual.MuzzleLocalPosition.y;
                if (palm.y < UnderwaterWorld.SampleRenderedSeabedHeight(palm.x, palm.z) + .50f) continue;
                bool blocked = false;
                foreach (Bounds bounds in reefs)
                {
                    Bounds expanded = bounds; expanded.Expand(.10f);
                    if (expanded.Contains(palm) || (expanded.IntersectRay(new Ray(palm, aim), out float distance) && distance <= 1.35f)) { blocked = true; break; }
                }
                if (blocked || Physics.CheckSphere(palm, .04f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                    Physics.SphereCast(palm, .04f, aim, out _, 1.35f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                return;
            }
            throw new InvalidOperationException("No clear synthetic approach to actual quest shark.");
        }
        static List<Bounds> SceneReefBounds()
        {
            var result = new List<Bounds>();
            var world = Object.FindFirstObjectByType<UnderwaterWorld>();
            Transform reef = world != null ? world.ActiveReefRoot : null;
            if (reef == null) return result;
            foreach (Transform group in reef)
            {
                if (group.childCount == 0) continue;
                Renderer[] renderers = group.GetChild(0).GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                result.Add(bounds);
            }
            return result;
        }
        static void OpenWaterPose(OceanCampaignCreatures.CreatureTarget shark, out Vector3 palm, out Vector3 aim)
        { palm = shark.WorldBounds.center + Vector3.up * 8f; aim = Vector3.forward; }
        static void Sample(OceanSpearGun gun, Vector3 palm, Vector3 aim, bool left, float curl,
            bool gripping = true, bool thumb = true, bool extended = true, bool fresh = true)
        {
            gun.DrivePoseForValidation(palm, aim, left, gripping, thumb, extended, curl, StepSeconds, fresh, ++sampleFrame);
            gun.TickForValidation(StepSeconds, true);
        }
        static void Samples(OceanSpearGun gun, Vector3 palm, Vector3 aim, bool left, float curl, int count)
        { for (int i = 0; i < count; i++) Sample(gun, palm, aim, left, curl); }
        static void ReleaseTrigger(OceanSpearGun gun, Vector3 palm, Vector3 aim, bool left)
        { Samples(gun, palm, aim, left, 0f, 10); }
        static void PressTrigger(OceanSpearGun gun, Vector3 palm, Vector3 aim, bool left, Action<bool, string> check)
        {
            int shots = gun.ShotsFired, ammo = gun.AmmoRemaining;
            ReleaseTrigger(gun, palm, aim, left);
            check(gun.ShotsFired == shots && gun.AmmoRemaining == ammo && gun.HeldLeft == left, "released one-hand pose equips without spending a round");
            Samples(gun, palm, aim, left, .4f, 3);
            check(gun.ShotsFired == shots + 1 && gun.AmmoRemaining == ammo - 1, "one extension-to-curl cycle spends one durable cartridge");
        }
        static void ResolveFlights(OceanSpearGun gun)
        { for (int i = 0; i < 160 && gun.InFlight; i++) gun.TickForValidation(StepSeconds, true); }
        static void CheckRestoredRun(OceanCampaignController campaign, Camera view, string evidenceDirectory, Action<bool, string> check)
        {
            string folder = Path.Combine(evidenceDirectory, "spear-midrun-save"); Directory.CreateDirectory(folder);
            check(OceanSaveStore.TrySave(folder, campaign.State.Save, out string saveError), "production store saves partial shared budget: " + saveError);
            check(OceanSaveStore.TryLoad(folder, out OceanSaveData data, out OceanSaveLoadStatus status, out string loadError), "production store reloads partial run: " + loadError);
            var restored = new OceanCampaignState(data);
            check(status == OceanSaveLoadStatus.Loaded && restored.Current.sharkHits == 4 && restored.Current.spearShotsFired == 14 && restored.SpearAmmoRemaining == 6, "four hits, fourteen shots and six remaining survive restart");
            var owner = new GameObject("Synthetic restored level-five instance");
            try
            {
                var actors = owner.AddComponent<OceanCampaignCreatures>();
                actors.Initialize(Object.FindFirstObjectByType<UnderwaterWorld>(), view, null, restored, null);
                actors.ConfigureLevel(view.transform.position); actors.TickGameplay(StepSeconds, true); Physics.SyncTransforms();
                check(actors.SpearGun.CurrentPhase == OceanSpearGun.Phase.Hidden && !actors.SpearGun.CanPickup && actors.SpearGun.AmmoRemaining == 6, "restored permanent tool stays hidden without replenishment");
                OpenWaterPose(QuestShark(actors), out Vector3 palm, out Vector3 aim); ReleaseTrigger(actors.SpearGun, palm, aim, false);
                check(actors.SpearGun.Held && !actors.SpearGun.HeldLeft && actors.SpearGun.AmmoRemaining == 6, "restored owned tool appears for one right-hand pose with saved budget");
            }
            finally { owner.SetActive(false); Object.DestroyImmediate(owner); Physics.SyncTransforms(); }
        }
        static void CheckMissBudget(UnderwaterWorld world, Camera view, Camera close, Action<Camera, string> capture,
            Action<bool, string> check, string evidenceDirectory)
        {
            var owner = new GameObject("Synthetic exhausted run"); var state = new OceanCampaignState(); state.StartLevel(5);
            try
            {
                var actors = owner.AddComponent<OceanCampaignCreatures>(); actors.Initialize(world, view, null, state, null);
                actors.ConfigureLevel(view.transform.position); actors.TickGameplay(StepSeconds, true); Physics.SyncTransforms();
                var gun = actors.SpearGun;
                check(actors.TryGrasp(gun.PickupBounds.center + Vector3.up * .12f, true), "independent miss run acquires actual ground equipment");
                OpenWaterPose(QuestShark(actors), out Vector3 palm, out Vector3 aim);
                for (int shot = 1; shot <= 20; shot++)
                {
                    PressTrigger(gun, palm, aim, shot % 2 == 0, check);
                    if (shot < 20) ResolveFlights(gun);
                }
                check(gun.AmmoRemaining == 0 && gun.InFlight && !state.Failed, "twentieth missed shot still in flight cannot fail before settlement");
                Samples(gun, palm, aim, true, 0f, 10); Samples(gun, palm, aim, true, .4f, 10);
                check(gun.ShotsFired == 20, "twenty-first input cannot exceed shared budget");
                ResolveFlights(gun);
                check(state.Failed && state.AmmoExhausted && state.FailureReason == "ammoExhausted" && state.Current.sharkHits == 0 && !gun.Held && !gun.CanPickup,
                    "settled twentieth miss fails, hides equipment and cannot be refilled");
                Near(close, palm, new Vector3(1f, .5f, -1.5f)); capture(close, "spear-06-exhausted-hidden-no-projectiles");
                string folder = Path.Combine(evidenceDirectory, "spear-exhausted-save");
                check(OceanSaveStore.TrySave(folder, state.Save, out string error) && OceanSaveStore.TryLoad(folder, out _, out error), "ammunition failure persists through production store: " + error);
                var pending = new OceanCampaignState(); pending.StartLevel(5); pending.AcquireTool();
                for (int shot = 0; shot < 20; shot++) check(pending.TryFireSpear(), "synthetic interrupted run consumes durable round");
                var reloadOwner = new GameObject("Synthetic zero-ammo restore");
                try
                {
                    var reload = reloadOwner.AddComponent<OceanCampaignCreatures>(); reload.Initialize(world, view, null, pending, null); reload.ConfigureLevel(view.transform.position);
                    check(pending.Failed && pending.AmmoExhausted && reload.SpearGun.AmmoRemaining == 0, "gun creation explicitly settles restored zero-ammo run without surviving projectiles");
                }
                finally { reloadOwner.SetActive(false); Object.DestroyImmediate(reloadOwner); }
            }
            finally { owner.SetActive(false); Object.DestroyImmediate(owner); Physics.SyncTransforms(); }
        }
        static void Near(Camera camera, Vector3 point, Vector3 offset)
        { camera.transform.SetPositionAndRotation(point + offset, Quaternion.LookRotation(-offset, Vector3.up)); }
        static void CheckInitialPose(OceanSpearGun gun, string evidenceDirectory, Action<bool, string> check)
        {
            var pose = new InitialPose { toolPosition = gun.ToolTransform.position,
                minimumVertexFloorGap = float.PositiveInfinity, maximumVertexFloorGap = float.NegativeInfinity,
                footprintSurfaceMaximum = float.NegativeInfinity };
            Bounds actual = default; bool found = false;
            foreach (MeshFilter filter in gun.ToolTransform.GetComponentsInChildren<MeshFilter>(false))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || filter.sharedMesh == null) continue;
                check(filter.sharedMesh.isReadable, "initial visible spear geometry is CPU-readable");
                pose.meshes++;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 world = filter.transform.TransformPoint(vertex);
                    check(!float.IsNaN(world.x) && !float.IsInfinity(world.x) && !float.IsNaN(world.y) && !float.IsInfinity(world.y) &&
                        !float.IsNaN(world.z) && !float.IsInfinity(world.z), "initial spear world vertex is finite");
                    if (!found) { actual = new Bounds(world, Vector3.zero); found = true; } else actual.Encapsulate(world);
                    float gap = world.y - UnderwaterWorld.SampleRenderedSeabedHeight(world.x, world.z);
                    pose.minimumVertexFloorGap = Mathf.Min(pose.minimumVertexFloorGap, gap);
                    pose.maximumVertexFloorGap = Mathf.Max(pose.maximumVertexFloorGap, gap);
                    pose.vertices++;
                }
            }
            check(found && pose.meshes > 0 && pose.vertices > 0, "initial spear has visible world-space geometry");
            Bounds pickup = gun.PickupBounds;
            pose.meshMin = actual.min; pose.meshMax = actual.max; pose.pickupMin = pickup.min; pose.pickupMax = pickup.max;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                pose.footprintSurfaceMaximum = Mathf.Max(pose.footprintSurfaceMaximum,
                    UnderwaterWorld.SampleRenderedSeabedHeight(pickup.center.x + pickup.extents.x * x, pickup.center.z + pickup.extents.z * z));
            pose.boundsFloorGap = actual.min.y - pose.footprintSurfaceMaximum;
            Directory.CreateDirectory(evidenceDirectory);
            File.WriteAllText(Path.Combine(evidenceDirectory, "initial-speargun-pose.json"), JsonUtility.ToJson(pose, true));
            Debug.Log($"[SpearPreview] InitialPose meshes={pose.meshes} vertices={pose.vertices} tool={pose.toolPosition:F5} meshMin={pose.meshMin:F5} meshMax={pose.meshMax:F5} pickupMin={pose.pickupMin:F5} pickupMax={pose.pickupMax:F5} surfaceMax={pose.footprintSurfaceMaximum:F5} boundsGap={pose.boundsFloorGap:F5} vertexGapMin={pose.minimumVertexFloorGap:F5} vertexGapMax={pose.maximumVertexFloorGap:F5}");
            check(Mathf.Abs(actual.min.y - pickup.min.y) <= .01f, "initial placement bounds agree with actual visible mesh bottom");
            check(pose.boundsFloorGap >= -.002f && pose.boundsFloorGap <= .10f,
                "initial actual mesh bottom is on rendered footprint surface, expected +0.006m: " + pose.boundsFloorGap);
            check(pose.minimumVertexFloorGap >= -.002f && pose.minimumVertexFloorGap <= .10f,
                "initial visible vertex has actual terrain contact within 0.1m: " + pose.minimumVertexFloorGap);
        }
        static void Require(bool value, string reason)
        { report.assertions++; if (!value) throw new InvalidOperationException("Spear preview: " + reason); }
        static void Submit(Camera camera)
        {
            var data = camera.GetUniversalAdditionalCameraData(); bool xr = data.allowXRRendering;
            try
            {
                data.allowXRRendering = false; Canvas.ForceUpdateCanvases();
                Object.FindFirstObjectByType<TreasureFeedback>()?.SubmitCelebrationForPreview(camera);
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            }
            finally { data.allowXRRendering = xr; }
        }
        static void Save(Camera camera, string name)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                Submit(camera); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(SessionState.GetString(Key + "output", ""), name + ".png"), pixels.EncodeToPNG());
                report.screenshots.Add(name + ".png");
            }
            finally { RenderTexture.active = previous; }
        }
        static void Finish(bool success, string detail)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            SessionState.SetBool(Key + "active", false); EditorApplication.update -= Tick; Application.logMessageReceived -= Error;
            report.success = success; report.detail = detail;
            File.WriteAllText(Path.Combine(SessionState.GetString(Key + "output", ""), "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log((success ? "SPEAR_PREVIEW_SUCCEEDED: " : "SPEAR_PREVIEW_FAILED: ") + detail);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif

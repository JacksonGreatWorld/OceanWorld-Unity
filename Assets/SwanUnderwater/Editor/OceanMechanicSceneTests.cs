#if UNITY_EDITOR
using System;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Published mission configuration and preserved future metadata; interaction fixtures run only when their levels are published. Synthetic Editor state only.</summary>
    public static class OceanMechanicSceneTests
    {
        public static int Assertions { get; private set; }
        static int frame = 1000000;
        static void Check(bool condition, string message)
        { Assertions++; if (!condition) throw new InvalidOperationException("Expedition scene: " + message); }
        sealed class Scene : IDisposable
        {
            readonly GameObject root;
            public readonly OceanCampaignState state;
            public readonly OceanMechanicRuntime runtime;
            public readonly Camera camera;
            public readonly string InitialSaveJson;
            public Scene(int level)
            {
                root = new GameObject("Isolated synthetic expedition scene " + level);
                var viewObject = new GameObject("Isolated test view"); viewObject.transform.SetParent(root.transform);
                camera = viewObject.AddComponent<Camera>(); camera.enabled = false; camera.transform.position = new Vector3(0, 1.6f, 0);
                state = new OceanCampaignState(); state.StartLevel(level);
                InitialSaveJson = JsonUtility.ToJson(state.Save);
                runtime = root.AddComponent<OceanMechanicRuntime>(); runtime.Initialize(null, camera, null, state, null);
                runtime.ConfigureLevel(Vector3.zero);
            }
            public void Point(int target, float pinch, int count = 1, float motion = .4f)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 direction = target < 0 ? Vector3.up : (runtime.NodePosition(target) - camera.transform.position).normalized;
                    runtime.StepSceneTest(.05f, frame++, new Ray(camera.transform.position, direction), pinch, true,
                        default, 0, false, camera.transform.position, Vector3.forward, motion);
                }
            }
            public void Click(int target) { Point(target, 0, 5); Point(target, 1); }
            public void Cross(int target)
            {
                Vector3 p = runtime.NodePosition(target);
                camera.transform.position = p - Vector3.forward * .12f; Point(-1, 0);
                camera.transform.position = p + Vector3.forward * .12f; Point(-1, 0);
            }
            public void Twin(int count)
            {
                for (int i = 0; i < count; i++)
                    runtime.StepSceneTest(.05f, frame++, new Ray(camera.transform.position, runtime.NodePosition(0) - camera.transform.position), 0, true,
                        new Ray(camera.transform.position, runtime.NodePosition(1) - camera.transform.position), 0, true,
                        camera.transform.position, Vector3.forward);
            }
            public void Dispose()
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
        }
        public static void RunAll()
        {
            Assertions = 0;
            for (int level = 1; level <= OceanLevelCatalog.Count; level++)
            using (var scene = new Scene(level))
            {
                Check(ReferenceEquals(scene.state.Definition, OceanLevelCatalog.Get(level)) && scene.state.Save.bestStars.Length == OceanLevelCatalog.Count,
                    "all100 catalog identities and durable history slots remain readable " + level);
                if (OceanCampaignAvailability.IsOpen(level) && scene.state.HasMechanic) continue;
                Check(scene.runtime.Rules == null && scene.runtime.VisibleNodeCount == 0 && scene.runtime.StationRoot == null && scene.runtime.CaptionRectForValidation == null,
                    "unpublished or original published level has no expedition rules/nodes/station/caption " + level);
                Check(scene.runtime.GetComponentsInChildren<MeshFilter>(true).Length == 0 && scene.runtime.GetComponentsInChildren<Canvas>(true).Length == 0 && scene.runtime.GetComponentsInChildren<Light>(true).Length == 0,
                    "configuration creates no mission mesh, canvas, or light outside published mechanics " + level);
                scene.runtime.TickGameplay(.05f, true); scene.runtime.TickGameplay(.05f, false);
                scene.runtime.SuspendSceneTest(); scene.runtime.ConfigureLevel(new Vector3(2, 0, 3));
                Check(JsonUtility.ToJson(scene.state.Save) == scene.InitialSaveJson && scene.state.Current.mechanicProgress == 0 && scene.state.Current.mechanicEventIds.Count == 0,
                    "real configure/tick/pause/reconfigure cannot award or alter unopened progress " + level);
                Check(scene.runtime.Rules == null && scene.runtime.VisibleNodeCount == 0 && scene.runtime.StationRoot == null && scene.runtime.CaptionRectForValidation == null,
                    "reconfiguration remains allocation-free for unopened mechanics " + level);
                Check(OceanCampaignState.Validate(scene.state.Save, out string error), "preserved catalog state validates without constructing future art: " + level + " " + error);
            }
            for (int tier = 0; tier < 16; tier++)
            {
                int level = 21 + tier * 5;
                if (!OceanCampaignAvailability.IsOpen(level)) continue;
                using (var scene = new Scene(level))
                {
                var runtime = scene.runtime; var state = scene.state;
                Check(runtime.Rules != null && runtime.VisibleNodeCount >= 1 && runtime.VisibleNodeCount <= 3, "bounded actual station exists for tier " + tier);
                Check(runtime.StationRoot.GetComponentsInChildren<Collider>(true).Length == 0, "station adds no physics collision surface for tier " + tier);
                Check(runtime.StationRoot.GetComponentsInChildren<MeshFilter>().Length >= runtime.VisibleNodeCount, "actual node meshes exist for tier " + tier);
                // Arriving with a closed hand never dispatches a pinch, even for immediately clickable mechanics.
                scene.Point(0, 1, 4);
                Check(state.Current.mechanicProgress == 0 && state.Current.mechanicStage == 0, "initial held pinch cannot earn a stage for tier " + tier);
                runtime.SuspendSceneTest();
                switch (runtime.Rules.Kind)
                {
                    case OceanMechanicKind.CurrentRiding:
                        scene.Point(0, 0, 35, 0); Check(state.Current.mechanicProgress == 0, "static current aim cannot count");
                        scene.Point(0, 0, 18); break;
                    case OceanMechanicKind.OxygenRelay:
                        scene.Click(0); Check(state.Current.mechanicStage == 1, "oxygen supply is separate from delivery"); scene.Click(1); break;
                    case OceanMechanicKind.DeepLight:
                        scene.Point(0, 0, 28); Check(state.Current.mechanicStage == 1, "real hand light dwell reveals the scene node"); scene.Click(0); break;
                    case OceanMechanicKind.Observation:
                        scene.Point(0, 0, 35); Check(state.Current.mechanicStage == 1, "scene gaze and ray dwell focuses observation"); scene.Click(0); break;
                    case OceanMechanicKind.Escort: scene.Point(1, 0, 120); break;
                    case OceanMechanicKind.KelpMaze:
                        scene.Click(runtime.Rules.MazeExit); Check(state.Current.mechanicProgress == 0 && state.Current.mechanicStage == 1, "maze selection requires later actual head crossing");
                        scene.Cross(runtime.Rules.MazeExit); break;
                    case OceanMechanicKind.ShipwreckLock:
                    case OceanMechanicKind.CoralRepair:
                    case OceanMechanicKind.Cleanup:
                        scene.Click(0); Check(state.Current.mechanicStage == 1, "real qualified pinch starts scene carry");
                        scene.Point(1, 1, 7); scene.Point(1, 0); break;
                    case OceanMechanicKind.JellyfishDodge:
                        state.Current.mechanicElapsedSeconds = 3f; scene.Click(0); break;
                    case OceanMechanicKind.OctopusTracking:
                        state.Current.mechanicElapsedSeconds = 4.55f; scene.Click(0); break;
                    case OceanMechanicKind.FishHerding:
                        scene.Point(1, 0, 35, 0); Check(state.Current.mechanicProgress == 0, "stationary pointer does not herd fish");
                        scene.Point(1, 0, 80); break;
                    case OceanMechanicKind.TideGates:
                        state.Current.mechanicElapsedSeconds = 6.1f; scene.Click(0); scene.Click(1); break;
                    case OceanMechanicKind.RingRace:
                        scene.Click(0); Check(state.Current.mechanicProgress == 0, "scene ring cannot be clicked to complete"); scene.Cross(0); break;
                    case OceanMechanicKind.RunePuzzle:
                        scene.Point(-1, 0, 60);
                        for (int i = 0; i < 3; i++) scene.Click(runtime.Rules.ExpectedRune);
                        break;
                    case OceanMechanicKind.ElectricFinale:
                        scene.Twin(35); Check(state.Current.mechanicStage == 1, "two distinct ray targets charge actual station");
                        state.Current.mechanicElapsedSeconds = 4.81f; scene.Click(2); break;
                }
                Check(state.Current.mechanicProgress == 1 && state.Current.mechanicEventIds.Count == 1,
                    "actual ray/gesture/scene contract commits exactly one durable event for " + runtime.Rules.Kind);
                Check(OceanCampaignState.Validate(state.Save, out string error), "scene-created campaign state validates: " + error);
                }
            }
            // Retain interaction regressions for a future published tier without adding a runtime gate bypass.
            if (OceanCampaignAvailability.IsOpen(51)) TrackingAndPause();
            if (OceanCampaignAvailability.IsOpen(76)) CleanupSorting();
            if (OceanCampaignAvailability.IsOpen(21)) CaptionPauseVisibility();
            Check(OceanMechanicRuntime.CrossedRing(new Vector3(0, 1, -.1f), new Vector3(0, 1, .1f), new Vector3(0, 1, 0), .4f), "ring center segment qualifies");
            Check(!OceanMechanicRuntime.CrossedRing(new Vector3(1, 1, -.1f), new Vector3(1, 1, .1f), new Vector3(0, 1, 0), .4f), "swimming around ring does not qualify");
            Check(!OceanMechanicRuntime.CrossedRing(new Vector3(0, 1, -3), new Vector3(0, 1, 3), new Vector3(0, 1, 0), .4f), "relocation cannot synthesize a race crossing");
            Debug.Log("OceanMechanicSceneTests PASS " + Assertions + " assertions; all100 metadata retained, unopened real ConfigureLevel creates no station/caption or progress; published tier interactions only. No headset comfort/performance claim.");
        }
        static void TrackingAndPause()
        {
            using (var scene = new Scene(51))
            {
                scene.Click(0); Check(scene.state.Current.mechanicStage == 1, "carry begins before interruption");
                scene.runtime.SuspendSceneTest(); Check(scene.state.Current.mechanicStage == 0, "pause clears physical carry without awarding progress");
                scene.Point(1, 1, 6); scene.Point(1, 0);
                Check(scene.state.Current.mechanicProgress == 0, "old held pinch/release cannot complete after pause");
                scene.Click(0);
                scene.runtime.StepSceneTest(.05f, frame++, default, 0, false, default, 0, false, scene.camera.transform.position, Vector3.forward);
                Check(scene.state.Current.mechanicStage == 0 && scene.state.Current.mechanicProgress == 0, "complete tracking loss cancels carry without a phantom delivery");
            }
        }
        static void CleanupSorting()
        {
            using (var scene = new Scene(76))
            {
                scene.Click(0); scene.Point(1, 1, 7); scene.Point(1, 0);
                Check(scene.state.Current.mechanicProgress == 1, "plastic bottle enters fixed plastic bin");
                scene.Click(0); scene.Point(1, 1, 7); scene.Point(1, 0);
                Check(scene.state.Current.mechanicProgress == 1 && scene.state.Current.mechanicMistakes == 1,
                    "next metal item cannot be accepted by the same plastic bin");
                scene.Point(-1, 0, 20); scene.Click(0); scene.Point(2, 1, 7); scene.Point(2, 0);
                Check(scene.state.Current.mechanicProgress == 2 && scene.state.Current.mechanicEventIds.Count == 2,
                    "metal item must reach the separate fixed metal bin to earn its event");
            }
        }
        static void CaptionPauseVisibility()
        {
            using (var scene = new Scene(21))
            {
                var rect = scene.runtime.CaptionRectForValidation;
                Check(rect != null && rect.GetComponent<Canvas>().enabled, "active expedition owns its visible instruction canvas");
                Check(rect.GetComponentsInChildren<UnityEngine.UI.Image>().Length == 1,
                    "caption uses exactly one noninteractive contrast plate");
                scene.runtime.TickGameplay(.05f, false);
                Check(!rect.GetComponent<Canvas>().enabled,
                    "pause hides both expedition text and its contrast plate without touching the world nodes");
                scene.runtime.TickGameplay(.05f, false);
                scene.runtime.RefreshCaptionPoseForValidation();
                Check(scene.runtime.CaptionRectForValidation == rect && !rect.GetComponent<Canvas>().enabled,
                    "repeated paused frames retain the disabled canvas without querying Text.canvas");
                scene.runtime.ConfigureLevel(Vector3.zero);
                Check(scene.runtime.CaptionRectForValidation.GetComponent<Canvas>().enabled,
                    "reconfigured current level owns a fresh visible caption canvas");
            }
        }
        /// <summary>Call after the actual campaign, camera and station have prepared a representative tier.</summary>
        public static void ValidateHudSeparation(Camera camera, OceanCampaignController campaign)
        {
            Check(camera != null && campaign != null && campaign.Panel != null && campaign.Mechanics != null,
                "actual camera, campaign HUD and expedition station exist for projected layout verification");
            var runtime = campaign.Mechanics;
            runtime.RefreshCaptionPoseForValidation();
            RectTransform caption = runtime.CaptionRectForValidation;
            if (campaign.State.Definition.Mechanic == OceanMechanicKind.None)
            { Check(caption == null, "legacy levels do not contain an expedition instruction caption"); return; }
            Check(caption != null && caption.gameObject.activeInHierarchy, "current tier owns its actual caption canvas");
            Rect captionRect = Project(camera, caption);
            Rect hudRect = Project(camera, campaign.Panel.GuideRectForValidation);
            Check(!captionRect.Overlaps(hudRect), "expedition instruction and existing gold HUD must not overlap in the actual camera projection");
            Check(captionRect.xMin >= .02f && captionRect.xMax <= .98f && captionRect.yMin >= .02f && captionRect.yMax <= .40f,
                "expedition instruction fits inside the lower view band with safe frame padding");
            for (int i = 0; i < runtime.VisibleNodeCount; i++)
            {
                Rect labelRect = Project(camera, runtime.NodeLabelRectForValidation(i));
                Check(!labelRect.Overlaps(hudRect), "node label does not overlap the existing gold HUD: " + i);
                Check(!labelRect.Overlaps(captionRect), "node label does not overlap the expedition instruction: " + i);
            }
            Debug.Log("Expedition projected layout PASS level=" + campaign.State.Current.level +
                " caption=" + captionRect + " existingHud=" + hudRect + "; actual camera and world RectTransforms.");
        }
        static Rect Project(Camera camera, RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 point = camera.WorldToViewportPoint(corners[i]);
                Check(point.z > 0, "projected UI rectangle remains in front of the actual camera");
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
#endif

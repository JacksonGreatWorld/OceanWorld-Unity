#if UNITY_EDITOR
using System;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual scene adapters with explicit synthetic rays; never player/device progress.</summary>
    public static class OceanFirstTwentyActivityTests
    {
        public static int Assertions { get; private set; }
        static int frame = 3000000;
        static readonly int[] Levels = { 6, 9, 14, 16, 18 };
        static readonly OceanMechanicKind[] Kinds = { OceanMechanicKind.RingRace, OceanMechanicKind.Observation,
            OceanMechanicKind.RunePuzzle, OceanMechanicKind.CurrentRiding, OceanMechanicKind.Escort };
        static void Check(bool value, string message)
        { Assertions++; if (!value) throw new InvalidOperationException("First twenty activities: " + message); }
        sealed class Scene : IDisposable
        {
            readonly GameObject root;
            public readonly OceanCampaignState state = new OceanCampaignState();
            public readonly OceanMechanicRuntime runtime;
            public readonly Camera camera;
            readonly UnderwaterWorld world;
            public Scene(int level, UnderwaterWorld actualWorld = null)
            {
                world = actualWorld;
                root = new GameObject("Synthetic optional expedition " + level);
                try
                {
                    var eye = new GameObject("Synthetic camera"); eye.transform.SetParent(root.transform);
                    Vector3 spawn = world == null ? Vector3.zero : world.ActiveSpawn;
                    float floor = world == null ? 0 : UnderwaterWorld.SampleRenderedSeabedHeight(spawn.x, spawn.z, world.ActiveEnvironment);
                    camera = eye.AddComponent<Camera>(); camera.enabled = false; camera.transform.position = new Vector3(spawn.x, floor + 1.6f, spawn.z);
                    state.StartLevel(level);
                    runtime = root.AddComponent<OceanMechanicRuntime>(); runtime.Initialize(world, camera, null, state, null);
                    runtime.ConfigureLevel(spawn);
                    if (world != null) CheckActualStation(this, world);
                    camera.transform.position = runtime.NodePosition(0) - Vector3.forward * 2f;
                }
                catch { UnityEngine.Object.DestroyImmediate(root); throw; }
            }
            public void Point(int target, float pinch, int count = 1, float motion = .4f, bool tracked = true)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 direction = target < 0 ? Vector3.up : (runtime.NodePosition(target) - camera.transform.position).normalized;
                    if (target >= 0) camera.transform.forward = direction;
                    int before = state.Current.mechanicProgress;
                    runtime.StepSceneTest(.05f, frame++, new Ray(camera.transform.position, direction), pinch, tracked,
                        default, 0, false, camera.transform.position, camera.transform.forward, motion);
                    if (world != null && state.Current.mechanicProgress != before) CheckActualStation(this, world);
                }
            }
            public void Click(int target) { Point(target, 0, 5); Point(target, 1); }
            public void Cross()
            {
                Vector3 center = runtime.NodePosition(0), forward = runtime.StationRoot.GetChild(0).forward;
                // The previous gate dissolves at its old pose before the next cue appears.
                camera.transform.position = center - forward * .15f; Point(-1, 0, 12);
                camera.transform.position = center + forward * .15f; Point(-1, 0);
            }
            public void Dispose() => UnityEngine.Object.DestroyImmediate(root);
        }
        public static void RunAll()
        {
            Assertions = 0;
            for (int i = 0; i < Levels.Length; i++)
            {
                var state = new OceanCampaignState(); state.StartLevel(Levels[i]);
                Check(state.Definition.Mechanic == Kinds[i] && state.Definition.MechanicOptional, "five distinct optional level contracts " + Levels[i]);
                string partial = JsonUtility.ToJson(state.Save);
                var restore = new OceanCampaignState(state.Save);
                Check(JsonUtility.ToJson(restore.Save) == partial, "old active run with no mechanic evidence is preserved " + Levels[i]);
                Check(!OceanCampaignGuide.Describe(state, 0).Action.Contains("可选支线"), "main hint retains first priority " + Levels[i]);
                Check(OceanCampaignGuide.Describe(state, 2).Action.Contains("可选支线"), "optional action is explicitly labeled " + Levels[i]);
                CompleteMain(state);
                Check(state.GoalComplete && state.Stars == 2 && !state.MechanicComplete && state.Current.mechanicProgress == 0,
                    "unchanged main collection earns historical two stars without side activity " + Levels[i]);
                string completed = JsonUtility.ToJson(state.Save);
                Check(OceanCampaignState.Validate(state.Save, out string error), "historical completed run is still valid " + Levels[i] + " " + error);
                restore = new OceanCampaignState(state.Save);
                Check(JsonUtility.ToJson(restore.Save) == completed && restore.GoalComplete, "restoring completed history does not revoke result " + Levels[i]);
                Check(OceanCampaignGuide.Describe(state, 2).Details.Contains("主目标已完成"), "side suggestion acknowledges completion " + Levels[i]);
                for (int node = 0; node < state.Definition.MechanicTarget; node++) Check(state.RecordMechanicProgress(100000 + node), "bounded side progress accepts its own event " + Levels[i]);
                Check(state.GoalComplete && state.Stars == 2 && state.MechanicComplete, "side activity never changes the star rule " + Levels[i]);
                Check(!state.RecordMechanicProgress(100000), "completed side activity cannot duplicate rewards " + Levels[i]);
                Check(OceanCampaignState.Validate(state.Save, out error), "completed side progress validates " + Levels[i] + " " + error);
                for (int coin = 0; coin < state.CoinTargetCount; coin++) Check(state.CollectCoin(OceanLevelCatalog.CoinId(coin)), "existing hidden coin flow remains available");
                Check(state.Stars == 3, "all hidden coins still determine the third star " + Levels[i]);
            }
            var history = new OceanSaveData(); foreach (int level in Levels) history.bestStars[level - 1] = 3;
            Check(OceanAchievementProgress.FromSavedHistory(history, 0).CompletedMechanics == 0,
                "old stars cannot invent optional activity achievements");
            Check(!OceanCampaignAvailability.IsOpen(21) && !OceanCampaignAvailability.IsOpen(100), "future content stays closed");
            RingScene(); ObservationScene(); RuneScene(); CurrentScene(); EscortScene(); DisabledCanvasResume();
            Check(!OceanMechanicRuntime.CrossedRing(Vector3.back * 2, Vector3.forward * 2, Vector3.zero, .7f), "a relocation cannot be a ring crossing");
            Check(!OceanMechanicRuntime.CrossedRing(Vector3.back * .1f, Vector3.back * .1f, Vector3.zero, .7f), "standing still cannot cross a ring");
            Debug.Log("[OceanFirstTwentyActivityTests] PASS " + Assertions + " assertions; five optional hands-first activities, old main goals/save history preserved; synthetic Editor input, no headset claim.");
        }
        static void CompleteMain(OceanCampaignState state)
        {
            for (int species = 0; species < 10; species++)
                if ((state.ShellTargetMask & (1 << species)) != 0) Check(state.CollectShell(1000 + species, species), "real ordered/unordered shell target");
            for (int species = 0; species < 10; species++)
                if ((state.FishTargetMask & (1 << species)) != 0) Check(state.CollectFish(species), "real ordered/unordered fish target");
        }
        /// <summary>Run against a fully prepared real sanctuary; temporary states never touch its campaign or player camera.</summary>
        public static void RunInScene(UnderwaterWorld world)
        {
            Check(world != null && world.ActiveEnvironment == OceanEnvironmentKind.CoralSanctuary,
                "actual sanctuary must finish loading before activity scene validation");
            Physics.SyncTransforms();
            RingScene(world); ObservationScene(world); RuneScene(world); CurrentScene(world); EscortScene(world); DisabledCanvasResume(world);
            Debug.Log("[OceanFirstTwentyActivityTests] ACTUAL WORLD PASS " + Assertions +
                " cumulative assertions; five real stations, terrain/physics corridor, native species and synthetic hand interactions. No player save or tracked camera moved.");
        }
        static void CheckActualStation(Scene scene, UnderwaterWorld world)
        {
            var runtime = scene.runtime;
            Check(runtime.ExplorationAvailableForValidation && runtime.StationRoot != null && runtime.StationRoot.gameObject.activeInHierarchy,
                "real route places an available optional station for level " + scene.state.Current.level);
            Vector3 center = runtime.ExplorationCenterForValidation;
            float floor = UnderwaterWorld.SampleRenderedSeabedHeight(center.x, center.z, world.ActiveEnvironment);
            Check(center.y - floor >= 1.25f && center.y - floor <= 3.35f, "station is above the real rendered floor within seated swim reach");
            Check(runtime.GetComponentsInChildren<Collider>(true).Length == 0, "real activity adds no physical obstacle");
            var instructions = runtime.CaptionRectForValidation.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Check(instructions.Length > 0, "real station retains its owned instruction text");
            foreach (var label in instructions)
                Check(label.text.Contains(OceanLanguage.Text(" · 可选支线")) || scene.state.MechanicComplete,
                    "real instruction explicitly marks optional activity in the current locale: " + label.text);
            for (int i = 0; i < runtime.VisibleNodeCount; i++)
                Check(!Physics.CheckSphere(runtime.NodePosition(i), .32f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                    "real interaction node is outside non-trigger reef collisions " + i);
            // This short corridor stays before the arch. Sample the actual sand relief
            // rather than drawing a straight camera-height line through sloped terrain.
            Vector3 spawn = world.ActiveSpawn;
            Vector3 last = spawn; last.y = UnderwaterWorld.SampleRenderedSeabedHeight(last.x, last.z, world.ActiveEnvironment) + 1.6f;
            float distance = new Vector2(center.x - spawn.x, center.z - spawn.z).magnitude;
            for (float d = .5f; d <= distance + .5f; d += .5f)
            {
                Vector3 point = OceanMechanicRuntime.ExplorationRoutePoint(spawn, Mathf.Min(d, distance), out _);
                point.y = UnderwaterWorld.SampleRenderedSeabedHeight(point.x, point.z, world.ActiveEnvironment) + 1.6f;
                Check(!Physics.CheckCapsule(last, point, .28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                    "real swim corridor capsule remains clear before station " + scene.state.Current.level + " at " + d);
                last = point;
            }
        }
        static void RingScene(UnderwaterWorld world = null)
        {
            using (var scene = new Scene(6, world))
            {
                scene.Point(-1, 0, 20); Check(scene.state.Current.mechanicProgress == 0, "waiting cannot complete ring");
                Vector3 first = scene.runtime.NodePosition(0);
                for (int i = 0; i < 3; i++) scene.Cross();
                Check(scene.state.MechanicComplete && (scene.runtime.NodePosition(0) - first).sqrMagnitude > 60f, "three real ordered crossings move along fixed route");
                Check(!scene.state.GoalComplete && scene.runtime.GetComponentsInChildren<Collider>(true).Length == 0,
                    "ring adds no swim obstruction and cannot replace main collection");
            }
        }
        static void DisabledCanvasResume(UnderwaterWorld world = null)
        {
            using (var scene = new Scene(6, world))
            {
                RectTransform originalLabel = scene.runtime.NodeLabelRectForValidation(0);
                var canvases = scene.runtime.StationRoot.GetComponentsInChildren<Canvas>(true);
                foreach (Canvas canvas in canvases) canvas.enabled = false;
                // The art pass and runtime visibility changes can temporarily disable
                // every Canvas while gameplay resumes and refreshes node presentation.
                scene.Cross();
                Check(scene.state.Current.mechanicProgress == 1 &&
                    scene.runtime.NodeLabelRectForValidation(0) == originalLabel,
                    "disabled canvases retain the same owned label through a real progress refresh");
                scene.runtime.StationRoot.gameObject.SetActive(false);
                Check(scene.runtime.NodeLabelRectForValidation(0) == originalLabel,
                    "inactive station can expose its owned label without asking Graphic.canvas");
                scene.runtime.StationRoot.gameObject.SetActive(true);
                foreach (Canvas canvas in canvases) canvas.enabled = true;
                scene.runtime.SuspendSceneTest(); scene.Cross();
                Check(scene.state.Current.mechanicProgress == 2 &&
                    scene.runtime.NodeLabelRectForValidation(0) == originalLabel,
                    "canvas and station recovery can refresh again without reconstructing labels");
            }
        }
        static void ObservationScene(UnderwaterWorld world = null)
        {
            using (var scene = new Scene(9, world))
            {
                Check(scene.runtime.StationRoot.GetComponentsInChildren<MeshRenderer>(true).Length > 1, "observation uses real blue tang and clownfish model renderers");
                scene.Point(0, 1, 5); Check(scene.state.Current.mechanicProgress == 0, "held entry pinch cannot record observation");
                scene.Point(0, 0, 30); Check(scene.state.Current.mechanicStage == 1, "open hand and head focus need a real dwell");
                scene.runtime.SuspendSceneTest(); scene.Point(0, 1, 5);
                Check(scene.state.Current.mechanicProgress == 0, "pause and held pinch cannot synthesize shutter");
                Check(scene.state.Current.mechanicStage == 0, "pause discards the interrupted focused stage");
                scene.Point(0, 0, 5); scene.Point(0, 1);
                Check(scene.state.Current.mechanicProgress == 0 && scene.state.Current.mechanicStage == 0,
                    "a new pinch after only five open frames cannot reuse focus from before the menu");
                scene.Point(0, 0, 30); Check(scene.state.Current.mechanicStage == 1, "complete resumed dwell refocuses observation");
                scene.Point(0, 0, 1, tracked:false);
                Check(scene.state.Current.mechanicStage == 0, "tracking loss also discards the focused stage");
                scene.Point(0, 0, 5); scene.Point(0, 1);
                Check(scene.state.Current.mechanicProgress == 0 && scene.state.Current.mechanicStage == 0,
                    "short reopening after tracking restoration cannot synthesize a focused shutter");
                scene.Point(0, 0, 30); scene.Click(0);
                Check(scene.state.Current.mechanicProgress == 1, "release and fresh focused shutter record one real node");
                scene.camera.transform.position = scene.runtime.NodePosition(0) - Vector3.forward * 2f;
                scene.Point(0, 0, 30); scene.Click(0);
                Check(scene.state.MechanicComplete, "second species needs its own focus and shutter");
            }
        }
        static void RuneScene(UnderwaterWorld world = null)
        {
            using (var scene = new Scene(14, world))
            {
                scene.Point(-1, 0, 60); scene.Click(1);
                Check(scene.state.Current.mechanicProgress == 0 && scene.state.Current.mechanicMistakes == 1, "wrong remembered rune restarts preview without altering main collection");
                scene.Point(-1, 0, 60); scene.Click(0); scene.Click(2); scene.Click(1);
                Check(scene.state.MechanicComplete, "three separated correct pinches finish remembered pattern");
            }
        }
        static void CurrentScene(UnderwaterWorld world = null)
        {
            using (var scene = new Scene(16, world))
            {
                scene.Point(0, 0, 30, 0); Check(scene.state.Current.mechanicProgress == 0, "stationary hand cannot power current");
                for (int i = 0; i < 3; i++)
                { scene.camera.transform.position = scene.runtime.NodePosition(0) - Vector3.forward * 2f; scene.Point(0, 0, 19); }
                Check(scene.state.MechanicComplete, "three separate vanes need actual open hand motion nearby");
            }
        }
        static void EscortScene(UnderwaterWorld world = null)
        {
            using (var scene = new Scene(18, world))
            {
                var animators = scene.runtime.StationRoot.GetComponentsInChildren<Animator>(true);
                Check(animators.Length == 1 && animators[0].runtimeAnimatorController != null && !animators[0].applyRootMotion,
                    "escort keeps the admitted native swim controller without root translation");
                scene.Point(1, 1, 10); Check(scene.state.Current.mechanicProgress == 0, "closed hand cannot deliver turtle");
                for (int i = 0; i < 2; i++)
                { scene.camera.transform.position = scene.runtime.NodePosition(0) - Vector3.forward * 2f; scene.Point(1, 0, 100); }
                Check(scene.state.MechanicComplete, "calm open hand must physically guide both turtle deliveries");
                Check(scene.runtime.GetComponentsInChildren<Collider>(true).Length == 0, "native turtle cannot block swimming or collecting");
            }
        }
    }
}
#endif

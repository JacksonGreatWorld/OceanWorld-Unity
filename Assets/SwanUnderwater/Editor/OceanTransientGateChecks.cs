#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Transient guidance and production crossing lifecycle. All progress is isolated in memory.</summary>
    public static class OceanTransientGateChecks
    {
        static readonly List<string> issues = new List<string>();
        static int frame = 8100000;
        public static int Assertions { get; private set; }
        public static IReadOnlyList<string> Issues => issues;
        static void Check(bool condition, string detail)
        { Assertions++; if (!condition) issues.Add(detail); }
        static void Require(bool condition, string detail)
        { if (!condition) throw new InvalidOperationException("Transient gate fixture: " + detail); }

        public static void RunAll()
        {
            Assertions = 0; issues.Clear();
            Check(OceanActivityGateEffect.Radius >= .69f && OceanActivityGateEffect.Radius <= .71f,
                "the guidance radius matches the existing optional crossing aperture");
            Check(OceanActivityGateEffect.CueLifetime >= 7.9f && OceanActivityGateEffect.CueLifetime <= 8.1f,
                "idle cue lifetime remains bounded at eight seconds");
            EffectLifecycle();
            ApproachAndIdle();
            RuntimeLifecycle();
            if (issues.Count != 0)
                throw new InvalidOperationException("TRANSIENT_GATE_FAILED assertions=" + Assertions + " failures=" + issues.Count +
                    "; " + string.Join("; ", issues.GetRange(0, Mathf.Min(12, issues.Count))));
            Debug.Log("TRANSIENT_GATE_PASS assertions=" + Assertions +
                "; true effect renderers and actual campaign crossing adapter, idle/reapproach/slow swimming, pause/restore/retry/complete cleanup. In-memory synthetic inputs, no player save or wearer claim.");
        }

        static void Tick(OceanActivityGateEffect effect, Vector3 eye, int frames, bool active = true, bool complete = false)
        { for (int i = 0; i < frames; i++) effect.Tick(.05f, eye, active, complete); }
        static bool HasRenderedGeometry(Transform root)
        {
            if (root == null) return false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && !renderer.forceRenderingOff) return true;
            return false;
        }
        static bool RenderersNear(Transform root, Vector3 at, float maximumDistance)
        {
            bool any = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff) continue;
                any = true;
                if (Vector3.Distance(renderer.bounds.center, at) > maximumDistance) return false;
            }
            return any;
        }
        static HashSet<int> MaterialIds(Transform root)
        {
            var result = new HashSet<int>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials) if (material != null) result.Add(material.GetInstanceID());
            return result;
        }
        static HashSet<int> MeshIds(Transform root)
        {
            var result = new HashSet<int>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) result.Add(filter.sharedMesh.GetInstanceID());
            return result;
        }

        static void EffectLifecycle()
        {
            var owner = new GameObject("Isolated transient gate owner");
            OceanActivityGateEffect effect = null;
            try
            {
                effect = new OceanActivityGateEffect(owner.transform);
                Vector3 first = new Vector3(0, 1.6f, 0), forward = Vector3.forward, eye = first - forward * 4f;
                effect.Configure(first, Quaternion.identity);
                Check(!effect.CueActive && !effect.Visible && !HasRenderedGeometry(effect.Root), "configuration itself never leaves a permanent ring in the scenery");
                Tick(effect, first - forward * 12f, 5);
                Check(!effect.Visible, "a remote waypoint is hidden instead of a distant static landmark");
                Tick(effect, first + forward * 4f, 5);
                Check(!effect.CueActive, "a fresh cue does not open behind a player who is already beyond its plane");
                Tick(effect, first - forward * 12f, 2);
                Tick(effect, eye, 6);
                Check(effect.CueActive && effect.Visible && effect.Opacity > 0 && HasRenderedGeometry(effect.Root), "forward approach opens a real bounded cue");
                Check(effect.Root.GetComponentsInChildren<Collider>(true).Length == 0, "guidance never obstructs swimming with a collider");
                var waterRenderer = effect.Root.GetComponent<MeshRenderer>();
                Check(waterRenderer.sharedMaterial.shader.name == "SwanUnderwater/ActivityRippleGate" && waterRenderer.sharedMaterial.shader.isSupported,
                    "actual cue uses the supported luminous water shader");
                Check(waterRenderer.sharedMaterial.HasProperty("_RipplePhase") && waterRenderer.sharedMaterial.HasProperty("_PassProgress"),
                    "ripple animation and passing dissolve are wired to the admitted material");
                Mesh waterMesh = effect.Root.GetComponent<MeshFilter>().sharedMesh;
                bool openCenter = true, reachesHalo = false;
                foreach (Vector3 point in waterMesh.vertices)
                { openCenter &= point.magnitude > .5f; reachesHalo |= point.magnitude > 1.3f; }
                Check(openCenter && reachesHalo && waterMesh.vertexCount <= 256,
                    "bounded open-center geometry includes the expanding wave halo without a full-screen disk");
                var block = new MaterialPropertyBlock(); waterRenderer.GetPropertyBlock(block);
                Check(Mathf.Approximately(block.GetFloat("_RipplePhase"), effect.RipplePhase), "renderer receives the owner clock at the shown pose");
                bool expired = effect.Expired;
                float frozenPhase = effect.RipplePhase;
                foreach (float invalid in new[] { 0f, -.1f, .1001f, float.NaN, float.PositiveInfinity }) effect.Tick(invalid, eye, true, false);
                Check(!effect.Visible && !effect.CueActive && effect.Expired == expired, "invalid or relocated time steps hide stale guidance without spending its idle lifetime");
                Check(Mathf.Approximately(effect.RipplePhase, frozenPhase), "invalid steps cannot advance the rendered ripple clock");
                Tick(effect, eye, 6);

                effect.Crossed();
                Check(!effect.CueActive, "a qualified crossing immediately disarms the old cue");
                float passingPhase = effect.RipplePhase;
                Vector3 next = first + new Vector3(1, 0, 3);
                effect.Configure(next, Quaternion.identity);
                Check(Mathf.Approximately(effect.RipplePhase, passingPhase), "next logical node preserves the old-position wave phase during dissolve");
                Tick(effect, next - forward * 4f, 2);
                Check(effect.RipplePhase >= 0f && effect.RipplePhase < 1f && !Mathf.Approximately(effect.RipplePhase, passingPhase),
                    "the old-position passing wave continues with a bounded owner-driven phase");
                Check(!effect.CueActive, "a newly configured gate waits while the old gate dissipates");
                Check(effect.Visible && RenderersNear(effect.Root, first, .9f), "the short dissipation remains at the crossed location, not the next waypoint");
                Tick(effect, next - forward * 4f, 12);
                Check(effect.CueActive && effect.Visible && RenderersNear(effect.Root, next, .9f), "the next nearby cue can appear after old dissipation ends");
                effect.Crossed(); Tick(effect, next - forward * 4f, 30);
                Check(!effect.CueActive && !effect.Visible, "a crossed cue stays consumed until a new logical waypoint is configured");

                var materials = MaterialIds(effect.Root); var meshes = MeshIds(effect.Root);
                int renderers = effect.Root.GetComponentsInChildren<Renderer>(true).Length;
                for (int i = 0; i < 60; i++)
                {
                    Vector3 at = new Vector3(i % 3, 1.6f, i % 5);
                    effect.Configure(at, Quaternion.Euler(0, i * 7f, 0));
                    Vector3 approaching = at - Quaternion.Euler(0, i * 7f, 0) * Vector3.forward * 4f;
                    Tick(effect, approaching, 12); effect.Crossed(); Tick(effect, approaching, 12);
                    effect.Suspend();
                    Check(!effect.Visible && !HasRenderedGeometry(effect.Root), "repeated suspend clears both cue and dissipation " + i);
                }
                Check(materials.SetEquals(MaterialIds(effect.Root)) && meshes.SetEquals(MeshIds(effect.Root)) && renderers == effect.Root.GetComponentsInChildren<Renderer>(true).Length,
                    "repeated waypoints reuse their owned renderers, meshes and shared materials");

                effect.Configure(first, Quaternion.identity); Tick(effect, eye, 6); effect.Crossed();
                effect.Tick(.05f, eye, true, true);
                Check(!effect.CueActive && !effect.Visible && !HasRenderedGeometry(effect.Root), "completion cancels even an in-flight dissipation immediately");
                Tick(effect, eye, 200, complete: true);
                Check(!effect.Visible, "completion never recreates a permanent green or blue circle");
                effect.Configure(first, Quaternion.identity); Tick(effect, eye, 6);
                effect.Tick(.05f, eye, false, false);
                Check(!effect.Visible && !HasRenderedGeometry(effect.Root), "inactive gameplay hides the cue on the same qualified frame");

                Transform ownedRoot = effect.Root;
                var ownedMaterials = new List<Material>();
                foreach (Renderer renderer in ownedRoot.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null && !AssetDatabase.Contains(material) && !ownedMaterials.Contains(material)) ownedMaterials.Add(material);
                effect.Dispose(); effect.Dispose();
                Check(ownedRoot == null || !ownedRoot.gameObject.activeInHierarchy, "disposal removes the owned visual root without leaving active geometry");
                if (!Application.isPlaying)
                    foreach (Material material in ownedMaterials) Check(material == null, "EditMode disposal releases owned materials");
            }
            finally { effect?.Dispose(); Object.DestroyImmediate(owner); }
        }

        static void ApproachAndIdle()
        {
            var owner = new GameObject("Isolated slow swimmer gate");
            OceanActivityGateEffect effect = null;
            try
            {
                effect = new OceanActivityGateEffect(owner.transform);
                Vector3 center = new Vector3(0, 1.6f, 0);
                effect.Configure(center, Quaternion.identity);
                for (int i = 0; i < 400; i++)
                {
                    Vector3 eye = center - Vector3.forward * (5.9f - i * .01f);
                    effect.Tick(.05f, eye, true, false);
                    if (i > 8) Check(effect.CueActive && !effect.Expired, "slow continuous approach keeps guidance available beyond twenty seconds " + i);
                }
                Vector3 stationary = center - Vector3.forward * 1.91f;
                Tick(effect, stationary, 190);
                Check(effect.Expired && !effect.CueActive && !effect.Visible && !HasRenderedGeometry(effect.Root), "eight seconds without approach progress fades out the idle cue");
                Tick(effect, stationary, 400);
                Check(effect.Expired && !effect.Visible, "waiting indefinitely does not repeatedly recreate an expired cue");
                for (int i = 1; i <= 14; i++) effect.Tick(.05f, stationary + Vector3.forward * (i * .05f), true, false);
                Check(effect.CueActive && !effect.Expired && effect.Visible, "a clear fresh approach of at least half a metre rearms an expired cue");

                Tick(effect, center - Vector3.forward * 9f, 2);
                Tick(effect, center - Vector3.forward * 5f, 6);
                Check(effect.CueActive && effect.Visible, "leaving beyond eight metres and approaching again also rearms guidance");
                effect.Suspend();
                Check(!effect.Visible, "tracking/menu suspension immediately hides a slow swimmer's cue");
            }
            finally { effect?.Dispose(); Object.DestroyImmediate(owner); }
        }

        sealed class RuntimeScene : IDisposable
        {
            readonly GameObject root;
            public readonly OceanCampaignState State;
            public readonly Camera Camera;
            public readonly OceanMechanicRuntime Runtime;
            public RuntimeScene(OceanCampaignState restored = null)
            {
                root = new GameObject("Isolated transient ring campaign");
                State = restored ?? new OceanCampaignState(); if (restored == null) State.StartLevel(6);
                Camera = new GameObject("Isolated synthetic ring camera").AddComponent<Camera>(); Camera.transform.SetParent(root.transform, false); Camera.enabled = false; Camera.transform.position = new Vector3(0, 1.6f, 0);
                Runtime = root.AddComponent<OceanMechanicRuntime>(); Runtime.Initialize(null, Camera, null, State, null); Runtime.ConfigureLevel(Vector3.zero);
            }
            public Vector3 Normal => Runtime.StationRoot.Find("Mission node 0").forward;
            public void Sample(Vector3 eye, int frames = 1, bool tracked = true)
            {
                Camera.transform.position = eye; Camera.transform.forward = Normal;
                for (int i = 0; i < frames; i++) Runtime.StepSceneTest(.05f, frame++, new Ray(eye, Vector3.up), 0, tracked, default, 0, false, eye, Camera.transform.forward);
            }
            public void Cross()
            {
                Vector3 center = Runtime.NodePosition(0), normal = Normal;
                Sample(center - normal * .15f, 2); Sample(center + normal * .15f);
            }
            public void Dispose()
            {
                // Explicit fixture cleanup in Edit Mode, independent of Unity's PlayMode callbacks.
                if (Runtime != null) typeof(OceanMechanicRuntime).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Runtime, null);
                Object.DestroyImmediate(root);
            }
        }
        static bool TextVisible(RectTransform root)
        {
            if (root == null || !root.gameObject.activeInHierarchy) return false;
            foreach (Canvas canvas in root.GetComponentsInParent<Canvas>(true)) if (!canvas.enabled || !canvas.gameObject.activeInHierarchy) return false;
            foreach (Text text in root.GetComponentsInChildren<Text>(true)) if (text.isActiveAndEnabled) return true;
            return false;
        }
        static void NoSolidRing(OceanMechanicRuntime runtime, string context)
        {
            Transform node = runtime.StationRoot.Find("Mission node 0"); Require(node != null, "original logical ring node exists");
            foreach (MeshRenderer renderer in node.GetComponents<MeshRenderer>())
                Check(!renderer.enabled || !renderer.gameObject.activeInHierarchy, "old opaque ring never renders: " + context);
        }
        static void CompleteMain(OceanCampaignState state)
        {
            for (int species = 0; species < 10; species++)
                if ((state.ShellTargetMask & (1 << species)) != 0) Check(state.CollectShell(21000 + species, species), "existing shell main target remains collectable");
            for (int species = 0; species < 10; species++)
                if ((state.FishTargetMask & (1 << species)) != 0) Check(state.CollectFish(species), "existing fish main target remains collectable");
        }
        static void RuntimeLifecycle()
        {
            using (var idleScene = new RuntimeScene())
            {
                Vector3 at = idleScene.Runtime.NodePosition(0), normal = idleScene.Normal;
                idleScene.Sample(at - normal * .15f, 200);
                Check(idleScene.Runtime.GateEffectForValidation.Expired && !idleScene.Runtime.GateEffectForValidation.Visible,
                    "a nearby resting player can let the visual cue dissipate");
                idleScene.Cross();
                Check(idleScene.State.Current.mechanicProgress == 1,
                    "visual idle expiry cannot invalidate the original tracked crossing or strand a nearby swimmer");
            }
            using (var scene = new RuntimeScene())
            {
                Require(scene.Runtime.GateEffectForValidation != null, "sixth-level runtime owns the transient effect");
                var effect = scene.Runtime.GateEffectForValidation;
                Check(!effect.Visible, "new level's gate remains hidden until the activity is actually played");
                NoSolidRing(scene.Runtime, "configure");
                Vector3 center = scene.Runtime.NodePosition(0), normal = scene.Normal;
                scene.Sample(center - normal * 12f, 4);
                Check(!effect.Visible && !TextVisible(scene.Runtime.NodeLabelRectForValidation(0)), "remote ring and its label do not pollute the scenery");
                scene.Sample(center - normal * 4f, 8);
                Check(effect.CueActive && effect.Visible, "actual production sample path opens the nearby current cue");
                NoSolidRing(scene.Runtime, "approach");
                scene.Sample(center - normal * .15f, 2); scene.Runtime.SuspendSceneTest();
                Check(!effect.Visible, "actual suspension hides existing visual guidance");
                scene.Sample(center + normal * .15f, 2);
                Check(scene.State.Current.mechanicProgress == 0, "crossing during a menu cannot synthesize resumed progress");
                scene.Sample(center - normal * .15f, 2); scene.Sample(center, tracked: false);
                Check(!effect.Visible, "actual tracking loss hides the ring");
                scene.Sample(center + normal * .15f, 2);
                Check(scene.State.Current.mechanicProgress == 0, "tracking restoration cannot reuse the old head segment for a reward");

                for (int completed = 1; completed <= 3; completed++)
                {
                    scene.Cross();
                    Check(scene.State.Current.mechanicProgress == completed, "one physical segment records exactly one real crossing " + completed);
                    NoSolidRing(scene.Runtime, "crossing " + completed);
                    if (completed < 3)
                    {
                        Check(!effect.CueActive, "next waypoint is not shown during the previous ring's dissipation " + completed);
                        scene.Sample(scene.Camera.transform.position, 12);
                    }
                }
                Check(scene.State.MechanicComplete && !scene.State.GoalComplete, "three crossings finish only the optional activity");
                Check(!effect.Visible && !effect.CueActive && !TextVisible(scene.Runtime.NodeLabelRectForValidation(0)), "completion immediately removes ring and world label");
                string completedState = JsonUtility.ToJson(scene.State.Save);
                for (int i = 0; i < 240; i++) scene.Runtime.TickGameplay(.05f, true);
                using (OceanLanguage.OverrideForValidation(OceanLanguageChoice.English))
                    for (int i = 0; i < 8; i++) scene.Runtime.TickGameplay(.05f, true);
                Check(JsonUtility.ToJson(scene.State.Save) == completedState, "completed hover and locale refresh cannot create progress or elapsed-time rewards");
                Check(!effect.Visible && !TextVisible(scene.Runtime.NodeLabelRectForValidation(0)), "completed hover and locale refresh never restore the old marker");
                Check(scene.Runtime.GetComponentsInChildren<Collider>(true).Length == 0, "the real activity still adds no swim obstruction");

                CompleteMain(scene.State);
                Check(scene.State.GoalComplete && scene.State.Stars == 2, "original main collection still awards historical two stars");
                var restored = new OceanCampaignState(scene.State.Save);
                using (var restoreScene = new RuntimeScene(restored))
                {
                    Check(restoreScene.State.MechanicComplete && !restoreScene.Runtime.GateEffectForValidation.Visible,
                        "restoring already completed activity history does not recreate a gate");
                    restoreScene.Runtime.TickGameplay(.05f, true);
                    Check(!restoreScene.Runtime.GateEffectForValidation.Visible, "completed real TickGameplay keeps restored guidance hidden");
                }
                Transform previousRoot = effect.Root;
                scene.State.StartLevel(7); scene.Runtime.ConfigureLevel(Vector3.zero);
                Check(scene.Runtime.GateEffectForValidation == null && (previousRoot == null || !previousRoot.gameObject.activeInHierarchy), "leaving for a no-mechanic level releases the old gate");
                scene.State.StartLevel(6); scene.Runtime.ConfigureLevel(Vector3.zero);
                Check(scene.State.Current.mechanicProgress == 0 && scene.Runtime.GateEffectForValidation != null && !scene.Runtime.GateEffectForValidation.Visible,
                    "retry creates one fresh unstarted gate without retaining completion or effects");
                NoSolidRing(scene.Runtime, "retry");
            }
        }
    }
}
#endif

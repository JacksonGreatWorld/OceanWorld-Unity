#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Production coroutine failure handling plus isolated, real-world environment switching.</summary>
    public static class OceanEnvironmentTransitionTests
    {
        public static int Assertions { get; private set; }

        [UnityEditor.MenuItem("OceanWorld/Checks/Environment transition rules")]
        public static void RunAll()
        {
            Assertions = 0;
            for (int level = 1; level <= OceanLevelCatalog.Count; level++)
                Check(OceanEnvironmentProfile.ForLevel(level) == (level <= 5 ? OceanEnvironmentKind.Classic : OceanEnvironmentKind.CoralSanctuary), "durable level chooses its environment " + level);
            foreach (int level in new[] { 0, -1, OceanLevelCatalog.Count + 1 })
            {
                bool rejected = false;
                try { OceanEnvironmentProfile.ForLevel(level); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, "invalid level cannot select an environment " + level);
            }
            NestedCompletion(); NestedFailure(); CancellationDisposesBoth(); DisposeFailure();
            ConstructionVisibilityRetainsGeometry();
            Debug.Log("OceanEnvironmentTransitionTests PASS: " + Assertions + " assertions; mapping, nested cleanup and construction visibility/bounds. No scene, native tracking or headset acceptance claim.");
        }

        static void Check(bool condition, string detail)
        { Assertions++; if (!condition) throw new InvalidOperationException("Environment transition: " + detail); }

        static IEnumerator Run(IEnumerator operation, Action<Exception> failed) =>
            (IEnumerator)typeof(OceanCampaignController).GetMethod("RunTransitionOperation", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { operation, failed });

        static IEnumerator Leaf(List<string> events, bool fail)
        {
            try
            {
                events.Add("leaf-enter"); yield return null;
                if (fail) throw new InvalidOperationException("injected child failure");
                events.Add("leaf-done");
            }
            finally { events.Add("leaf-dispose"); }
        }

        static IEnumerator Parent(List<string> events, bool fail)
        {
            try { events.Add("parent-enter"); yield return Leaf(events, fail); events.Add("parent-done"); }
            finally { events.Add("parent-dispose"); }
        }

        static void NestedCompletion()
        {
            var events = new List<string>(); Exception error = null;
            IEnumerator runner = Run(Parent(events, false), caught => error = caught);
            int frames = 0; while (runner.MoveNext()) { frames++; Check(runner.Current == null, "nested iterator flattened to a frame yield"); }
            Check(error == null && frames == 1, "successful stage completes without a false failure");
            Check(string.Join(",", events) == "parent-enter,leaf-enter,leaf-done,leaf-dispose,parent-done,parent-dispose", "natural completion disposes child and parent once");
        }

        static void NestedFailure()
        {
            var events = new List<string>(); Exception error = null; int failures = 0;
            IEnumerator runner = Run(Parent(events, true), caught => { error = caught; failures++; });
            while (runner.MoveNext()) { }
            Check(error != null && error.Message == "injected child failure" && failures == 1, "child exception reaches the owning transaction exactly once");
            Check(string.Join(",", events) == "parent-enter,leaf-enter,leaf-dispose,parent-dispose", "failed child cannot continue the commit stage and unwinds both iterators");
        }

        static void CancellationDisposesBoth()
        {
            var events = new List<string>(); Exception error = null;
            IEnumerator runner = Run(Parent(events, false), caught => error = caught);
            Check(runner.MoveNext(), "stage begins before cancellation");
            ((IDisposable)runner).Dispose();
            Check(error == null && string.Join(",", events) == "parent-enter,leaf-enter,leaf-dispose,parent-dispose", "cancellation disposes both suspended stages without running commit");
        }

        sealed class BadDispose : IEnumerator, IDisposable
        {
            public object Current => null;
            public bool MoveNext() => false;
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => throw new InvalidOperationException("injected dispose failure");
        }

        static void DisposeFailure()
        {
            Exception error = null;
            IEnumerator runner = Run(new BadDispose(), caught => error = caught);
            Check(!runner.MoveNext() && error != null && error.Message == "injected dispose failure", "stage disposal failure cannot strand the transaction outside rollback handling");
        }

        static void ConstructionVisibilityRetainsGeometry()
        {
            var root = new GameObject("Isolated construction visibility check");
            IDisposable visibility = null;
            try
            {
                root.transform.position = new Vector3(12, 3, -7);
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform, false); cube.transform.localScale = new Vector3(2, 3, 4);
                Renderer renderer = cube.GetComponent<Renderer>(); Collider collider = cube.GetComponent<Collider>();
                Physics.SyncTransforms();
                Bounds rendered = renderer.bounds, solid = collider.bounds;
                Type visibilityType = typeof(UnderwaterWorld).GetNestedType("EnvironmentConstructionVisibility", BindingFlags.NonPublic);
                visibility = (IDisposable)Activator.CreateInstance(visibilityType, new object[] { root.transform });
                MethodInfo suspend = visibilityType.GetMethod("SuspendNewRenderers");
                suspend.Invoke(visibility, null);
                Check(renderer.forceRenderingOff && renderer.enabled && root.activeInHierarchy,
                    "construction suppresses submission without disabling renderer or hierarchy");
                Check((renderer.bounds.center - rendered.center).sqrMagnitude < .000001f &&
                    (renderer.bounds.size - rendered.size).sqrMagnitude < .000001f && rendered.size.sqrMagnitude > 1,
                    "hidden construction retains actual renderer bounds used by fitting and LODs");
                Check(collider.enabled && (collider.bounds.center - solid.center).sqrMagnitude < .000001f &&
                    (collider.bounds.size - solid.size).sqrMagnitude < .000001f,
                    "hidden construction retains collider geometry and bounds");
                GameObject late = GameObject.CreatePrimitive(PrimitiveType.Cube);
                late.transform.SetParent(root.transform, false);
                Renderer lateRenderer = late.GetComponent<Renderer>();
                lateRenderer.enabled = false; lateRenderer.forceRenderingOff = true;
                suspend.Invoke(visibility, null);
                Check(lateRenderer.forceRenderingOff && !lateRenderer.enabled, "later inactive renderers are covered without enabling them");
                visibility.Dispose();
                Check(!renderer.forceRenderingOff && renderer.enabled && lateRenderer.forceRenderingOff && !lateRenderer.enabled,
                    "construction restores exact original flags, including deliberately hidden renderers");
                visibility.Dispose();
                Check(!renderer.forceRenderingOff && lateRenderer.forceRenderingOff, "visibility cleanup is safe when disposed twice");
            }
            finally
            {
                visibility?.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>Only invoke from a batch Play Mode preview using a private save directory.</summary>
        public static IEnumerator RunInScene(UnderwaterWorld world, TreasureHunt hunt)
        {
            if (!Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "-codexCampaignSaveDir") < 0)
                throw new InvalidOperationException("Environment scene tests require an isolated batch save directory.");
            if (world == null || hunt == null || hunt.Campaign == null) throw new ArgumentException("A prepared world and campaign are required.");
            OceanCampaignController campaign = hunt.Campaign;
            Transform classicRoot = null, sanctuaryRoot = null;
            bool cancelledHiddenConstruction = false;
            Color classicFog = default; float classicDensity = 0;
            foreach (int level in new[] { 1, 6, 1, 6, 6 })
            {
                if (level == 6 && sanctuaryRoot == null && !cancelledHiddenConstruction)
                {
                    yield return CancelUncachedConstruction(world, campaign);
                    cancelledHiddenConstruction = true;
                }
                OceanLevelRun oldRun = campaign.State.Current;
                Transform oldRoot = world.ActiveEnvironmentRoot;
                float oldTime = oldRun.remainingSeconds;
                bool observedHiddenConstruction = false;
                campaign.HandleAction("start_level", level);
                yield return null;
                if (campaign.EnvironmentTransitioning && ReferenceEquals(oldRun, campaign.State.Current))
                    Check(campaign.State.Current.remainingSeconds == oldTime, "old timer pauses during environment preparation");
                double deadline = Time.realtimeSinceStartupAsDouble + 90;
                while (campaign.EnvironmentTransitioning)
                {
                    if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Environment switch did not finish.");
                    Transform building = world.ActiveEnvironmentRoot;
                    if (building != null && building != oldRoot && world.ActiveEnvironment == OceanEnvironmentProfile.ForLevel(oldRun.level))
                    {
                        Renderer[] renderers = building.GetComponentsInChildren<Renderer>(true);
                        if (renderers.Length > 0)
                        {
                            bool hidden = true;
                            foreach (Renderer renderer in renderers) hidden &= renderer.forceRenderingOff;
                            Check(hidden && building.gameObject.activeInHierarchy && oldRoot.gameObject.activeInHierarchy,
                                "active construction cannot submit a second environment while the prior map is retained");
                            observedHiddenConstruction = true;
                        }
                    }
                    yield return null;
                }
                yield return null;
                // The map commit precedes the menu's release-to-arm quarantine.
                // Observe actual readiness instead of assuming one Editor frame is enough.
                double inputDeadline = Time.realtimeSinceStartupAsDouble + 8;
                while (!hunt.GameplayActive)
                {
                    Check(Time.realtimeSinceStartupAsDouble < inputDeadline, "bounded input release after map commit " + level);
                    yield return null;
                }
                Check(campaign.State.Current.level == level && hunt.GameplayActive, "requested level is playable " + level);
                Check(world.ActiveEnvironment == OceanEnvironmentProfile.ForLevel(level), "rendered environment matches durable level " + level);
                Check(world.ActiveEnvironmentRoot != null && world.ActiveEnvironmentRoot.gameObject.activeInHierarchy, "current environment is visible");
                Check(world.ActiveReefRoot != null && world.ActiveReefRoot.gameObject.activeInHierarchy, "current treasure/actor reef is visible");
                Transform current = world.ActiveEnvironmentRoot;
                Renderer terrain = current.Find("SeabedTerrain").GetComponent<Renderer>();
                Check(!terrain.forceRenderingOff && terrain.bounds.size.sqrMagnitude > 1,
                    "committed terrain submits again with valid fitted bounds");
                Check(terrain.sharedMaterial.HasProperty("_SanctuaryBlend") &&
                    Mathf.Approximately(terrain.sharedMaterial.GetFloat("_SanctuaryBlend"), level < 6 ? 0 : 1),
                    "sand rendering profile follows the committed environment " + level);
                if (level == 6 && sanctuaryRoot == null)
                    Check(observedHiddenConstruction, "first sanctuary entry exercises hidden multi-frame construction");
                if (level == 1)
                {
                    if (classicRoot == null)
                    {
                        classicRoot = current; classicFog = RenderSettings.fogColor; classicDensity = RenderSettings.fogDensity;
                    }
                    else
                    {
                        Check(classicRoot == current, "classic scene is reused after returning from level six");
                        AssertAtmosphere(world, classicFog, classicDensity, 0, "returning from sanctuary restores the classic profile");
                    }
                    if (sanctuaryRoot != null) Check(!sanctuaryRoot.gameObject.activeSelf, "inactive sanctuary cannot leave visible meshes or colliders behind");
                }
                else
                {
                    Check(Vector4.Distance(RenderSettings.fogColor, classicFog) > .001f &&
                        !Mathf.Approximately(RenderSettings.fogDensity, classicDensity),
                        "sanctuary has its own fog colour and depth profile");
                    if (sanctuaryRoot == null) sanctuaryRoot = current;
                    else Check(sanctuaryRoot == current, "sanctuary scene is reused on later entries and same-level restart");
                    Check(classicRoot != sanctuaryRoot && !classicRoot.gameObject.activeSelf, "classic meshes/colliders are inactive in the sanctuary");
                }
                int coins = 0, shells = 0; var ids = new HashSet<int>();
                foreach (TreasureHunt.Pickup pickup in hunt.Pickups)
                {
                    Check(ids.Add(pickup.id), "pickup IDs remain unique after repeated map changes");
                    if (pickup.kind < 0)
                    {
                        coins++;
                        if (level >= 6) Check(pickup.hiddenFromEntry && pickup.approachPosition != Vector3.zero, "sanctuary coin remains hidden and has a validated approach");
                    }
                    else shells |= 1 << pickup.kind;
                }
                Check(coins == campaign.State.CoinTargetCount && shells == campaign.State.ShellTargetMask, "exact target set restored for current environment");
            }
            // Cancel the production iterator after a cached map is activated but before commit.
            Transform beforeCancel = world.ActiveEnvironmentRoot;
            OceanLevelRun beforeRun = campaign.State.Current;
            Color beforeFog = RenderSettings.fogColor; float beforeDensity = RenderSettings.fogDensity;
            Exception cancellationFailure = null; bool callbackCalled = false, cancelledAfterActivation = false;
            IEnumerator operation = Run(
                world.PrepareEnvironmentForLevel(1, _ => callbackCalled = true), error => cancellationFailure = error);
            while (operation.MoveNext())
            {
                if(world.ActiveEnvironment == OceanEnvironmentKind.Classic)
                {
                    ((IDisposable)operation).Dispose(); cancelledAfterActivation = true; break;
                }
                yield return operation.Current;
            }
            yield return null;
            Check(cancelledAfterActivation && cancellationFailure == null && !callbackCalled, "actual map cancellation stops before commit callback");
            Check(world.IsReady && world.ActiveEnvironmentRoot == beforeCancel && beforeCancel.gameObject.activeInHierarchy,
                "cancellation restores the prepared prior environment and input readiness");
            Check(ReferenceEquals(beforeRun,campaign.State.Current), "cancelling a map preparation never replaces the durable run");
            AssertAtmosphere(world, beforeFog, beforeDensity, 1, "cached cancellation restores sanctuary fog and sand profile");
            campaign.HandleAction("start_level",1);
            while(campaign.EnvironmentTransitioning) yield return null;
            yield return null;
            Check(world.ActiveEnvironment == OceanEnvironmentKind.Classic && hunt.GameplayActive, "cached map remains usable after cancellation");
            campaign.HandleAction("start_level",6);
            while(campaign.EnvironmentTransitioning) yield return null;
            yield return null;
            Check(world.ActiveEnvironment == OceanEnvironmentKind.CoralSanctuary && hunt.GameplayActive, "return from cancellation recovery remains playable");
            // Query the actual generated arch, not a synthetic bounds box.
            for(int step=0;step<=16;step++)
            {
                float x=OceanCoralSanctuary.ArchCenter.x, z=OceanCoralSanctuary.ArchCenter.z-4f+step*.5f;
                float floor=UnderwaterWorld.SampleRenderedSeabedHeight(x,z);
                Vector3 bottom=new Vector3(x,floor+.30f,z);
                Vector3 top=new Vector3(x,floor+1.35f,z);
                Check(!Physics.CheckCapsule(bottom,top,.22f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore),
                    "actual sanctuary arch has a clear swimming capsule at step "+step);
            }
            Debug.Log("OceanEnvironmentTransitionTests scene PASS: actual 1→6→1→6→restart switch, root reuse, input readiness, paused old timer and pickup placement. Synthetic Editor exercise; no tracked-hand or PICO performance claim.");
        }

        static IEnumerator CancelUncachedConstruction(UnderwaterWorld world, OceanCampaignController campaign)
        {
            Transform before = world.ActiveEnvironmentRoot, abandoned = null;
            OceanLevelRun run = campaign.State.Current;
            Color beforeFog = RenderSettings.fogColor; float beforeDensity = RenderSettings.fogDensity;
            bool callbackCalled = false, cancelled = false; Exception error = null;
            IEnumerator operation = Run(world.PrepareEnvironmentForLevel(6, _ => callbackCalled = true), caught => error = caught);
            double deadline = Time.realtimeSinceStartupAsDouble + 90;
            try
            {
                while (operation.MoveNext())
                {
                    if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Hidden construction cancellation did not reach a renderable stage.");
                    Transform building = world.ActiveEnvironmentRoot;
                    if (building != null && building != before)
                    {
                        Renderer[] renderers = building.GetComponentsInChildren<Renderer>(true);
                        if (renderers.Length > 0)
                        {
                            bool hidden = true;
                            foreach (Renderer renderer in renderers) hidden &= renderer.forceRenderingOff;
                            Check(hidden && building.gameObject.activeInHierarchy && renderers[0].bounds.size.sqrMagnitude > 1,
                                "uncached construction is hidden while geometry bounds remain available");
                            abandoned = building;
                            ((IDisposable)operation).Dispose(); cancelled = true; break;
                        }
                    }
                    yield return operation.Current;
                }
            }
            finally { (operation as IDisposable)?.Dispose(); }
            Check(cancelled && error == null && !callbackCalled, "uncached cancellation disposes construction before commit");
            Check(world.IsReady && world.ActiveEnvironmentRoot == before && before.gameObject.activeInHierarchy &&
                !abandoned.gameObject.activeSelf && ReferenceEquals(run, campaign.State.Current),
                "uncached cancellation restores old visibility, readiness and durable run");
            AssertAtmosphere(world, beforeFog, beforeDensity, 0, "uncached cancellation retains classic fog and sand profile");
            yield return null;
            Check(abandoned == null, "cancelled partial environment is destroyed rather than cached");
        }

        static void AssertAtmosphere(UnderwaterWorld world, Color fog, float density, float sandBlend, string detail)
        {
            Material sand = world.ActiveEnvironmentRoot.Find("SeabedTerrain").GetComponent<Renderer>().sharedMaterial;
            Check(Vector4.Distance(RenderSettings.fogColor, fog) < .0001f && Mathf.Approximately(RenderSettings.fogDensity, density) &&
                sand.HasProperty("_SanctuaryBlend") && Mathf.Approximately(sand.GetFloat("_SanctuaryBlend"), sandBlend), detail);
        }
    }
}
#endif

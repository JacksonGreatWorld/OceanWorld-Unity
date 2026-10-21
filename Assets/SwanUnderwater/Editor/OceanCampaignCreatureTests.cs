#if UNITY_EDITOR
using System;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Deterministic qualification and generated geometry checks; no simulated headset acceptance.</summary>
    public static class OceanCampaignCreatureTests
    {
        static int assertions;
        public static int LastAssertionCount => assertions;
        public static void RunAll()
        {
            assertions = 0;
            TestSharkProgression(); TestFishResources(); TestShellGeometry();
            Debug.Log("CAMPAIGN_CREATURE_TESTS_PASSED assertions=" + assertions);
        }
        static void Require(bool condition, string detail)
        { assertions++; if (!condition) throw new InvalidOperationException("Campaign creature test: " + detail); }
        // Scene swept-hit qualification lives in OceanSpearGunPreview; these are durable rule checks only.
        static void TestSharkProgression()
        {
            foreach (int level in new[] { 5, 10, 15, 20 })
            {
            var state = new OceanCampaignState(); state.StartLevel(level);
            Require(!state.HitShark() && !state.CatchShark(), "no tool cannot progress shark state");
            Require(state.AcquireTool() && !state.AcquireTool(), "tool ownership is durable and acquired once");
            for (int hit = 1; hit <= state.SharkHitTarget; hit++)
            {
                Require(state.TryFireSpear() && state.HitShark() && state.Current.sharkHits == hit, "one accepted spent shot increments once " + hit);
                if (hit < state.SharkHitTarget)
                    Require(!state.CatchShark(), "cannot catch before catalog hit target");
            }
            Require(!state.HitShark() && state.Current.sharkHits == state.SharkHitTarget, "shark hit count caps at this boss target");
            Require(state.CatchShark() && !state.CatchShark(), "one final capture cannot replay");
            Require(state.GoalComplete == (level == 5), "mixed bosses still require fish and shells after shark capture");
            for (int kind = 0; kind < 10; kind++)
            {
                if ((state.FishTargetMask & (1 << kind)) != 0) Require(state.CollectFish(kind), "mixed boss fish target accepted");
                if ((state.ShellTargetMask & (1 << kind)) != 0) Require(state.CollectShell(kind, kind), "mixed boss shell target accepted");
            }
            Require(state.GoalComplete && state.Stars == 2, "all mixed targets complete two stars");
            for (int slot = 0; slot < state.CoinTargetCount; slot++) Require(state.CollectCoin(OceanLevelCatalog.CoinId(slot)), "each hidden coin remains collectible");
            Require(state.Stars == 3, "all boss-level coins upgrade completed goal");
            }
        }
        static void TestFishResources()
        {
            Require(ReferenceEquals(OceanCampaignCreatures.FishNames, OceanLevelCatalog.FishNames) &&
                ReferenceEquals(OceanCampaignCreatures.FishResourcePaths, OceanLevelCatalog.FishPaths), "campaign uses the shared stable fish catalog");
            Require(OceanCampaignCreatures.FishResourcePaths.Length == 10, "ten real fish resources");
            for (int species = 0; species < OceanLevelCatalog.FishSpeciesCount; species++)
            {
                GameObject prefab = Resources.Load<GameObject>(OceanCampaignCreatures.FishResourcePaths[species]);
                Require(prefab != null && prefab.GetComponentsInChildren<Renderer>(true).Length > 0, "actual imported fish art exists for " + species);
                Require(Finite(OceanLevelCatalog.FishLengths[species]) && OceanLevelCatalog.FishLengths[species] > .1f && OceanLevelCatalog.FishLengths[species] < 1f, "finite reachable catalog dimensions " + species);
            }
        }

        /// <summary>Actual generated scene and production actor logic with a separate synthetic observer; no head/hand tracking claim.</summary>
        public static void RunExpandedInScene(UnderwaterWorld world, Camera actualView)
        {
            if (world == null || actualView == null) throw new ArgumentException("A completed world and view are required.");
            assertions = 0;
            var owner = new GameObject("Synthetic expanded campaign actors");
            var observerObject = new GameObject("Synthetic expanded campaign observer", typeof(Camera));
            var observer = observerObject.GetComponent<Camera>(); observer.enabled = false;
            observer.transform.SetPositionAndRotation(actualView.transform.position, actualView.transform.rotation);
            var state = new OceanCampaignState(); var actors = owner.AddComponent<OceanCampaignCreatures>();
            string action = ""; int value = -1;
            actors.Initialize(world, observer, null, state, (a, v) => { action = a; value = v; });
            try
            {
                foreach (int level in new[] { 6, 8, 14, 20 })
                {
                    state.StartLevel(level); observer.transform.SetPositionAndRotation(actualView.transform.position, actualView.transform.rotation);
                    actors.ConfigureLevel(actualView.transform.position); Physics.SyncTransforms();
                    int fishMask = 0, sharks = 0;
                    foreach (var target in actors.Targets)
                    {
                        if (target.IsAmbient) continue;
                        if (target.IsShark) sharks++; else fishMask |= 1 << target.Species;
                        Require(target.Body.isTrigger && target.Visual.activeInHierarchy, "generated quest actor is visible and does not block locomotion");
                        Vector3 p = target.Body.transform.position;
                        Require(Finite(p.x) && Finite(p.y) && Finite(p.z) && Mathf.Abs(p.x) < 46f && Mathf.Abs(p.z) < 46f, "quest actor remains within finite playable bounds");
                    }
                    Require(fishMask == state.FishTargetMask && sharks == (state.RequiresShark ? 1 : 0), "all catalog target bits coexist with optional boss in level " + level);
                    Require((actors.SpearGun != null) == state.RequiresShark, "tool generation follows boss requirement");
                    if (level == 8)
                    {
                        OceanCampaignCreatures.CreatureTarget wrong = null;
                        foreach (var target in actors.Targets) if (!target.IsAmbient && target.Species == 4) wrong = target;
                        actors.TickGameplay(.02f, true);
                        Require(wrong != null && actors.TryGrasp(wrong.WorldBounds.center, true) && action == "fish-order" && value == 0 && state.Current.fishMask == 0 && wrong.Visual.activeInHierarchy, "production wrong-order grasp gives next species and leaves fish untouched");
                    }
                }
                OceanCampaignCreatures.CreatureTarget fish = null;
                foreach (var target in actors.Targets) if (!target.IsAmbient && !target.IsShark && target.Species == 0) fish = target;
                Require(fish != null, "final level includes ordinary clownfish alongside shark");
                observer.transform.position = fish.Body.transform.position + Vector3.right * .3f;
                Vector3 start = fish.Body.transform.position; actors.TickGameplay(.02f, true); Physics.SyncTransforms();
                Require(fish.FleeCount == 1 && fish.IsFleeing, "real proximity logic starts one bounded fish flee");
                Vector3 paused = fish.Body.transform.position; float cooldown = fish.FleeCooldown;
                for (int i = 0; i < 10; i++) actors.TickGameplay(.02f, false);
                Require(Vector3.Distance(paused, fish.Body.transform.position) < .00001f && fish.FleeCooldown == cooldown, "menu pause freezes fish flee and catch-window timer");
                for (int i = 0; i < 50 && fish.IsFleeing; i++)
                {
                    Vector3 previous = fish.Body.transform.position; actors.TickGameplay(.02f, true); Physics.SyncTransforms();
                    Require(Vector3.Distance(previous, fish.Body.transform.position) <= .01601f, "fish flee never exceeds 0.8 metres per second");
                }
                Require(!fish.IsFleeing && fish.FleeCooldown >= 3.99f && Vector3.Distance(start, fish.Body.transform.position) < .66f, "flee ends within one second and 0.65 metres with a catch window");
                observer.transform.position = fish.Body.transform.position + Vector3.right * .25f;
                Vector3 catchPosition = fish.Body.transform.position;
                for (int i = 0; i < 180; i++) actors.TickGameplay(.02f, true);
                Require(fish.FleeCount == 1 && !fish.IsFleeing && Vector3.Distance(catchPosition, fish.Body.transform.position) < .00001f, "continued player proximity cannot extend flee during the catch window");
                Physics.SyncTransforms();
                Require(actors.TryGrasp(fish.WorldBounds.center, true) && (state.Current.fishMask & 1) != 0, "production grasp catches resting fish after flee");
                Debug.Log("EXPANDED_CAMPAIGN_CREATURE_TESTS_PASSED assertions=" + assertions + "; actual production targets/proximity/motion, synthetic observer/time/grasp, not tracked-hand acceptance.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(observerObject); }
        }
        static void TestShellGeometry()
        {
            Require(CollectibleVisuals.ShellNames.Length == 10, "ten stable species names");
            var shells = new GameObject[10]; GameObject coin = null;
            try
            {
                for (int kind = 0; kind < 10; kind++)
                {
                    shells[kind] = CollectibleVisuals.CreateShell(null, kind);
                    Mesh mesh = shells[kind].GetComponent<MeshFilter>().sharedMesh;
                    Require(mesh != null && mesh.isReadable, "readable shell geometry " + kind);
                    Vector3[] vertices = mesh.vertices; int[] faces = mesh.triangles;
                    Require(Mathf.Abs(mesh.bounds.min.y) < .00001f, "bottom origin " + kind);
                    float width = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z);
                    Require(width >= .20f && width <= .261f && faces.Length / 3 < 6000, "shell size and budget " + kind);
                    foreach (Vector3 vertex in vertices)
                        Require(Finite(vertex.x) && Finite(vertex.y) && Finite(vertex.z), "finite shell vertex " + kind);
                    for (int i = 0; i < faces.Length; i += 3)
                    {
                        Require(faces[i] >= 0 && faces[i] < vertices.Length && faces[i + 1] >= 0 && faces[i + 1] < vertices.Length && faces[i + 2] >= 0 && faces[i + 2] < vertices.Length, "valid triangle indices " + kind);
                        Require(Vector3.Cross(vertices[faces[i + 1]] - vertices[faces[i]], vertices[faces[i + 2]] - vertices[faces[i]]).sqrMagnitude > 1e-20f, "non-degenerate triangle " + kind);
                    }
                    Debug.Log($"[CampaignGeometry] shell={kind} {CollectibleVisuals.ShellNames[kind]} vertices={vertices.Length} tri={faces.Length / 3} bounds={mesh.bounds.size:F5}");
                }
                coin = CollectibleVisuals.CreateCoin(null);
                Mesh coinMesh = coin.GetComponent<MeshFilter>().sharedMesh;
                Require(coinMesh.name == "Ocean Coin" && Mathf.Abs(coinMesh.bounds.size.x - CollectibleVisuals.CoinDiameter) < .00001f, "coin slot and diameter unchanged");
                CollectibleVisuals.AcquireCoinResources(out Mesh particle, out Material material);
                try { Require(particle.name == "Ocean Coin Particle" && material.enableInstancing, "reward coin slot remains instanced"); }
                finally { CollectibleVisuals.ReleaseSharedResources(); }
            }
            finally
            {
                foreach (GameObject shell in shells) if (shell != null) UnityEngine.Object.DestroyImmediate(shell);
                if (coin != null) UnityEngine.Object.DestroyImmediate(coin);
            }
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
#endif

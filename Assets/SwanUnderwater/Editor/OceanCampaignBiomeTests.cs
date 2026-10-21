#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    public static class OceanCampaignBiomeTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool okay, string message)
        { Assertions++; if (!okay) throw new InvalidOperationException("Independent biome maps: " + message); }
        public static void RunAll()
        {
            Assertions = 0;
            var cache = new Dictionary<OceanEnvironmentIdentity, int>();
            for (int level = 1; level <= OceanLevelCatalog.Count; level++)
            {
                var key = OceanEnvironmentIdentity.ForLevel(level);
                Check(key.Terrain == OceanEnvironmentProfile.ForLevel(level), "existing terrain sampler stays selected for level " + level);
                Check(key.Biome == (level <= 20 ? OceanCampaignBiome.Reef : OceanLevelCatalog.Get(level).Environment), "catalog exclusively selects biome " + level);
                var definition = OceanLevelCatalog.Get(level);
                float minimum = OceanExpeditionSearchPolicy.MinimumDistance(definition,true);
                Check(minimum <= definition.SearchRadius + 8f, "every level has a nonempty hidden-coin search interval " + level);
                Check(OceanExpeditionSearchPolicy.ContainsDistance(definition,minimum,true) &&
                    OceanExpeditionSearchPolicy.ContainsDistance(definition,definition.SearchRadius+8f,true), "both admitted distance boundaries remain reachable " + level);
                Check(!OceanExpeditionSearchPolicy.ContainsDistance(definition,minimum-.01f,true) &&
                    !OceanExpeditionSearchPolicy.ContainsDistance(definition,definition.SearchRadius+8.01f,true), "outside distance boundaries are rejected " + level);
                if (level <= 20) Check(minimum == 5f + definition.Chapter, "legacy coin placement distance remains unchanged " + level);
                if (!cache.ContainsKey(key)) cache.Add(key, level);
                Check(cache[key] <= level, "same composite key reuses first prepared map " + level);
            }
            Check(cache.Count == 7, "classic/sanctuary plus five independent expansion maps have distinct cache entries");
            Check(OceanEnvironmentIdentity.ForLevel(1).Equals(OceanEnvironmentIdentity.ForLevel(5)), "legacy classic root remains shared");
            Check(OceanEnvironmentIdentity.ForLevel(6).Equals(OceanEnvironmentIdentity.ForLevel(20)), "legacy sanctuary root remains shared");
            Check(!OceanEnvironmentIdentity.ForLevel(20).Equals(OceanEnvironmentIdentity.ForLevel(21)), "level21 cannot accidentally early-return to the old sanctuary");
            var runeLevel = OceanLevelCatalog.Get(91);
            Check(5f+runeLevel.Chapter > runeLevel.SearchRadius+8f, "level91 reproduces the old impossible global-chapter coin interval");
            Check(OceanExpeditionSearchPolicy.ContainsDistance(runeLevel,8f,true), "level91 now permits candidates behind real nearby ruins");
            Check(!OceanExpeditionSearchPolicy.ContainsDistance(runeLevel,float.NaN,true) && !OceanExpeditionSearchPolicy.ContainsDistance(runeLevel,float.PositiveInfinity,true), "invalid distance cannot admit a coin");
            foreach (var biome in new[] { OceanCampaignBiome.Shipwreck, OceanCampaignBiome.KelpForest, OceanCampaignBiome.DeepRuins, OceanCampaignBiome.CurrentChannel, OceanCampaignBiome.Abyss })
            {
                var parent = new GameObject("Isolated map check " + biome);
                try
                {
                    Vector3 spawn = new Vector3(7, 0, -11);
                    Func<float,float,float> floor = (x,z) => -.9f + x * .025f - z * .018f;
                    var map = OceanCampaignBiomeGeometry.Build(parent.transform, biome, floor, spawn);
                    Physics.SyncTransforms();
                    Check(map.transform.parent == parent.transform && map.Biome == biome, "each map is wholly owned by its environment root");
                    Check(map.RenderBatchCount >= 2 && map.RenderBatchCount <= 4, "static map uses at most four opaque batches " + biome);
                    Check(map.TriangleCount > 100 && map.TriangleCount <= 12000, "original map triangles stay within explicit12K budget " + biome);
                    Check(map.Pockets.Count == 16 && map.Vents.Count == 4, "fish/collectible habitats are map-owned snapshots " + biome);
                    Check(map.GroundAnchors.Count > 10 && map.Solids.Count > 0, "grounding and structural blockers exist " + biome);
                    foreach (Vector3 anchor in map.GroundAnchors)
                        Check(anchor.y <= floor(anchor.x,anchor.z) + .0001f && anchor.y >= floor(anchor.x,anchor.z) - .13f,
                            "every anchored stem/footing uses this map's sampled floor " + biome);
                    Bounds centralLane = new Bounds(spawn + new Vector3(0, floor(spawn.x,spawn.z) + 1.6f, 4f), new Vector3(3, 3.2f, 8));
                    foreach (Bounds solid in map.Solids) Check(!solid.Intersects(centralLane), "3m-wide forward mission corridor stays unobstructed " + biome);
                    int actualTriangles = 0;
                    foreach (MeshFilter filter in map.GetComponentsInChildren<MeshFilter>())
                    {
                        actualTriangles += filter.sharedMesh.triangles.Length / 3;
                        Check(filter.sharedMesh.bounds.size.sqrMagnitude > 0, "batch has explicit nonempty render bounds");
                        foreach (Vector3 vertex in filter.sharedMesh.vertices)
                            Check(!float.IsNaN(vertex.x) && !float.IsInfinity(vertex.x) && !float.IsNaN(vertex.y) && !float.IsInfinity(vertex.y) &&
                                !float.IsNaN(vertex.z) && !float.IsInfinity(vertex.z), "map vertices are finite");
                    }
                    Check(actualTriangles == map.TriangleCount, "reported budget counts actual generated triangles");
                }
                finally { UnityEngine.Object.DestroyImmediate(parent); }
            }
            VegetationRejectsStructuralFootprints();
            Debug.Log("[OceanCampaignBiomeTests] PASS " + Assertions + " assertions; real mesh/route/ground checks, no headset performance claim.");
        }

        static void VegetationRejectsStructuralFootprints()
        {
            var previousPockets = new List<ReefHabitat.CoralPocket>(ReefHabitat.Pockets);
            var previousVents = new List<Vector3>(ReefHabitat.BubbleSources);
            var parent = new GameObject("Blocked biome vegetation negative check");
            try
            {
                var map = OceanCampaignBiomeGeometry.Build(parent.transform, OceanCampaignBiome.Shipwreck, (x,z) => -.8f, Vector3.zero);
                // Deliberately make every candidate footprint solid. The old root-name-only path
                // admitted grass and shells here because it could not see an expedition map.
                map.Solids.Clear(); map.Solids.Add(new Bounds(Vector3.zero, new Vector3(100,20,100)));
                ReefHabitat.SetActiveData(map.Pockets,map.Vents);
                var meadow = SeagrassMeadow.Build(parent.transform,Vector3.zero,false);
                Check(meadow.TuftCount == 0, "generic grass rejects biome structural footprints even without legacy reef names");
                var shells = SeabedShells.Build(parent.transform,Vector3.zero,false);
                Check(shells.Placements.Count == 0, "decorative shells reject biome structural footprints even without legacy reef names");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
                ReefHabitat.SetActiveData(previousPockets,previousVents);
            }
        }

        /// <summary>Isolated scene regression for cold and cached transitions, independent of rule-only cache tests.</summary>
        public static IEnumerator RunInScene(UnderwaterWorld world, TreasureHunt hunt)
        {
            if (!Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "-codexCampaignSaveDir") < 0)
                throw new InvalidOperationException("Biome scene checks require an isolated batch save directory.");
            hunt.Campaign.Panel.SkipTutorialForValidation();
            var seen = new Dictionary<OceanEnvironmentIdentity, Transform>();
            var levels = new List<int>();
            for (int level = 1; level <= OceanLevelCatalog.Count; level++) levels.Add(level);
            levels.AddRange(new[] { 21, 51, 6, 1 });
            foreach (int level in levels)
            {
                hunt.Campaign.HandleAction("start_level", level);
                double deadline = Time.realtimeSinceStartupAsDouble + 120;
                while (hunt.Campaign.EnvironmentTransitioning)
                {
                    if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Biome scene transition exceeded120seconds.");
                    yield return null;
                }
                var key = OceanEnvironmentIdentity.ForLevel(level);
                Check(hunt.Campaign.State.Current.level == level, "actual level preparation committed rather than silently rolling back " + level);
                Check(world.ActiveEnvironment == key.Terrain && world.ActiveBiome == key.Biome, "actual scene activates requested terrain and biome " + level);
                int coins = 0;
                foreach (var pickup in hunt.Pickups) if (pickup.kind < 0)
                {
                    coins++;
                    if (level > 20) Check(pickup.hiddenFromEntry, "expansion coin is hidden by actual mesh from entry " + level);
                    Check(pickup.visual != null && pickup.renderer != null, "prepared coin has a real visual and graspable bounds " + level);
                }
                Check(coins == hunt.Campaign.State.CoinTargetCount, "every level prepared all authored coin slots " + level);
                Transform root = world.ActiveEnvironmentRoot;
                if (seen.TryGetValue(key, out Transform previous)) Check(previous == root, "actual revisit retains the prepared root and mesh " + level);
                else seen.Add(key,root);
                foreach (var pair in seen) Check(pair.Value.gameObject.activeSelf == pair.Key.Equals(key), "only active biome root is submitted");
            }
        }
    }
}
#endif

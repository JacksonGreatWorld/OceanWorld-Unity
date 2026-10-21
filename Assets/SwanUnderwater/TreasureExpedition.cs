using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater
{
    public sealed partial class TreasureHunt
    {
        sealed class Niche
        {
            public Vector2 point;
            public float clearance;
        }
        readonly List<SceneSurface> expeditionSurfaces = new List<SceneSurface>();
        readonly List<Bounds> expeditionBounds = new List<Bounds>();
        readonly List<Niche> expeditionNiches = new List<Niche>();
        bool expeditionCached;
        Transform expeditionReef;

        public System.Collections.IEnumerator PrepareExpeditionHabitat(Vector3 spawn)
        {
            Transform reef = world.ActiveReefRoot;
            if (expeditionCached && expeditionReef == reef) yield break;
            if (reef == null) throw new InvalidOperationException("Expedition reef missing.");
            expeditionCached = false;
            expeditionReef = reef;
            expeditionSurfaces.Clear(); expeditionBounds.Clear(); expeditionNiches.Clear();
            foreach (Transform group in reef)
            {
                if (group.childCount == 0) continue;
                Transform model = group.GetChild(0);
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                expeditionSurfaces.Add(new SceneSurface { model = model, bounds = bounds });
            }
            // The arch is open water between solid pieces. Its overall renderer bounds must
            // not turn the whole opening into an invisible treasure/creature blocker.
            if (world.ActiveSolidBounds != null)
                foreach (Bounds bounds in world.ActiveSolidBounds) expeditionBounds.Add(bounds);
            if (expeditionBounds.Count == 0)
                foreach (SceneSurface surface in expeditionSurfaces) expeditionBounds.Add(surface.bounds);
            // Cache mesh geometry, not pickup instances: level changes do not repeatedly decode scan meshes.
            var rng = new System.Random(20872026);
            foreach (SceneSurface surface in expeditionSurfaces)
            {
                // Only the largest campaign search circle can contribute a niche. Keep far reefs
                // in the obstruction list, but do not scan their full triangle mesh for loot.
                Vector3 nearest = surface.bounds.ClosestPoint(new Vector3(spawn.x, surface.bounds.center.y, spawn.z));
                if (FlatDistance(nearest, spawn) > 25f) continue;
                ReadSurfaceShape(surface, reefGeometry);
                ReefSupportGeometry shape = surface.shape;
                yield return null;
                int accepted = 0;
                for (int attempt = 0; attempt < 180 && accepted < 3; attempt++)
                {
                    float x = Range(rng, shape.bounds.min.x, shape.bounds.max.x);
                    float z = Range(rng, shape.bounds.min.z, shape.bounds.max.z);
                    if (!shape.TryVerticalSpan(x, z, out float bottom, out float top)) continue;
                    Vector3 lower = surface.model.TransformPoint(new Vector3(x, bottom, z));
                    Vector3 upper = surface.model.TransformPoint(new Vector3(x, top, z));
                    var point = new Vector2(lower.x, lower.z);
                    float clearance = lower.y - Ground(point);
                    if (Vector2.Distance(point, new Vector2(spawn.x, spawn.z)) > 25f || Mathf.Abs(point.x) > 44f || Mathf.Abs(point.y) > 44f || clearance < .55f || clearance > 1.5f || upper.y - lower.y < .04f) continue;
                    bool distinct = true;
                    foreach (Niche other in expeditionNiches) if (Vector2.Distance(point, other.point) < 1.45f) { distinct = false; break; }
                    if (!distinct || !GroundCandidate(point, null, 0f) || !ExpeditionRimClear(point)) continue;
                    Vector3 target = new Vector3(point.x, Ground(point) + .06f, point.y);
                    if (!TryApproach(target, expeditionSurfaces, reefGeometry, out _, out _, out _)) continue;
                    expeditionNiches.Add(new Niche { point = point, clearance = clearance }); accepted++;
                }
            }
            expeditionCached = true;
            Debug.Log($"[Campaign] expedition_habitat niches={expeditionNiches.Count} reefMeshes={reefGeometry.Count}");
        }

        bool ExpeditionRimClear(Vector2 point)
        {
            float floor = Ground(point);
            for (int sample = 0; sample < 9; sample++)
            {
                float angle = sample * Mathf.PI * .25f, radius = sample == 8 ? 0f : .15f;
                if (!EmptyAt(new Vector3(point.x + Mathf.Cos(angle) * radius, floor + .13f,
                    point.y + Mathf.Sin(angle) * radius), .12f, expeditionSurfaces, reefGeometry)) return false;
            }
            return true;
        }

        void PlaceExpeditionDiscoveries(Vector3 spawn, OceanLevelDefinition level)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            if (!expeditionCached) throw new InvalidOperationException("Expedition habitat must be staged before gameplay.");
            var rng = new System.Random(20802026 + level.Level * 7919);
            var grass = world.ActiveEnvironmentRoot != null ? world.ActiveEnvironmentRoot.GetComponentInChildren<SeagrassMeadow>() : null;
            // Coins get first choice of screened niches. Shell identities stay separate from durable coin slots.
            for (int slot = 0; slot < level.CoinCount; slot++)
            {
                bool coralFirst = (slot + level.Level) % 2 == 0;
                bool placed = coralFirst && PlaceNiche(-1, OceanLevelCatalog.CoinId(slot), spawn, level, rng);
                if (!placed) placed = PlaceGrass(-1, OceanLevelCatalog.CoinId(slot), spawn, level, grass, rng);
                if (!placed && !coralFirst) placed = PlaceNiche(-1, OceanLevelCatalog.CoinId(slot), spawn, level, rng);
                if (!placed) placed = PlaceOpen(-1, OceanLevelCatalog.CoinId(slot), spawn, level, rng, true);
                if (!placed) throw new InvalidOperationException($"No reachable concealed coin: level {level.Level} slot {slot}");
            }
            int ordinal = 0;
            for (int kind = 0; kind < OceanCampaignState.ShellSpeciesCount; kind++)
            {
                if ((level.ShellMask & (1 << kind)) == 0) continue;
                int id = kind; // Stable sparse species ids for the new layouts; coins never occupy this namespace.
                bool placed = ordinal % 3 == 0 && PlaceNiche(kind, id, spawn, level, rng);
                if (!placed && ordinal % 3 != 2) placed = PlaceGrass(kind, id, spawn, level, grass, rng);
                if (!placed) placed = PlaceOpen(kind, id, spawn, level, rng, false);
                if (!placed) throw new InvalidOperationException($"No reachable shell: level {level.Level} species {kind}");
                ordinal++;
            }
            if (pickups.Count != ordinal + level.CoinCount) throw new InvalidOperationException("Incomplete expedition layout.");
            Debug.Log($"[Campaign] expedition_layout_ms level={level.Level} ms={timer.Elapsed.TotalMilliseconds:F1}");
        }

        bool InExpeditionRange(Vector2 point, Vector3 spawn, OceanLevelDefinition level, bool coin)
        {
            if (Mathf.Abs(point.x) > 44f || Mathf.Abs(point.y) > 44f) return false;
            float distance = Vector2.Distance(point, new Vector2(spawn.x, spawn.z));
            return OceanExpeditionSearchPolicy.ContainsDistance(level, distance, coin);
        }

        bool PlaceNiche(int kind, int id, Vector3 spawn, OceanLevelDefinition level, System.Random rng)
        {
            if (expeditionNiches.Count == 0) return false;
            int offset = rng.Next(expeditionNiches.Count);
            for (int i = 0; i < expeditionNiches.Count; i++)
            {
                Niche niche = expeditionNiches[(offset + i) % expeditionNiches.Count];
                if (!InExpeditionRange(niche.point, spawn, level, kind < 0) || !GroundCandidate(niche.point, null, 0f)) continue;
                if (RegisterExpeditionPickup(kind, id, "coral-overhang", niche.point, rng, niche.clearance, spawn)) return true;
            }
            return false;
        }

        bool PlaceGrass(int kind, int id, Vector3 spawn, OceanLevelDefinition level, SeagrassMeadow grass, System.Random rng)
        {
            if (grass == null || grass.RootPositions.Count == 0) return false;
            int offset = rng.Next(grass.RootPositions.Count);
            for (int i = 0; i < grass.RootPositions.Count; i++)
            {
                Vector2 point = grass.RootPositions[(offset + i) % grass.RootPositions.Count];
                if (!InExpeditionRange(point, spawn, level, kind < 0) || !GroundCandidate(point, expeditionBounds, -.03f)) continue;
                if (RegisterExpeditionPickup(kind, id, "seagrass", point, rng, 0f, spawn)) return true;
            }
            return false;
        }

        bool PlaceOpen(int kind, int id, Vector3 spawn, OceanLevelDefinition level, System.Random rng, bool concealed)
        {
            for (int attempt = 0; attempt < 1800; attempt++)
            {
                float angle = Range(rng, -Mathf.PI, Mathf.PI);
                float radius = Range(rng, concealed ? level.SearchRadius * .8f : 3f, level.SearchRadius + 7f);
                var point = new Vector2(spawn.x + Mathf.Sin(angle) * radius, spawn.z + Mathf.Cos(angle) * radius);
                if (!InExpeditionRange(point, spawn, level, concealed) || !GroundCandidate(point, expeditionBounds, .10f)) continue;
                Vector3 target = new Vector3(point.x, Ground(point) + .07f, point.y);
                // A fallback coin must be hidden by actual scan geometry from the starting viewpoint.
                if (concealed && MeshLineClear(EntryEye(spawn), target, expeditionSurfaces, reefGeometry)) continue;
                if (RegisterExpeditionPickup(kind, id, concealed ? "reef-behind" : "sand", point, rng, 0f, spawn)) return true;
            }
            return false;
        }

        static Vector3 EntryEye(Vector3 spawn) => new Vector3(spawn.x, Ground(new Vector2(spawn.x, spawn.z)) + 1.5f, spawn.z);

        bool RegisterExpeditionPickup(int kind, int id, string habitat, Vector2 point, System.Random rng, float clearance, Vector3 spawn)
        {
            if (kind < 0) foreach (Pickup previous in pickups)
                if (previous.kind < 0 && Vector2.Distance(point, new Vector2(previous.GroundAnchor.x, previous.GroundAnchor.z)) < 3f) return false;
            if (!ExpeditionRimClear(point)) return false;
            Pickup item = AddPickup(kind, habitat, point, rng, true, id);
            item.hiddenFromEntry = !MeshLineClear(EntryEye(spawn), item.renderer.bounds.center, expeditionSurfaces, reefGeometry);
            if (kind < 0 && !item.hiddenFromEntry)
            { item.coinSparkles?.Dispose(); item.visual.SetActive(false); Destroy(item.visual); return false; }
            if (!TryApproach(item.renderer.bounds.center, expeditionSurfaces, reefGeometry,
                out Vector3 approach, out Vector3 eye, out Vector3 palm))
            {
                item.coinSparkles?.Dispose(); item.visual.SetActive(false); Destroy(item.visual); return false;
            }
            item.overhangClearance = clearance; item.approach = approach;
            item.approachPosition = eye; item.graspPosition = palm;
            RegisterPickup(item); return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using HandType = SwanUnderwater.OceanHandSide;
using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Level one: real world-space discoveries and one pickup per new hand grasp.</summary>
    [DefaultExecutionOrder(100)]
    public sealed partial class TreasureHunt : MonoBehaviour
    {
        public static readonly string[] SpeciesNames = { "扇贝", "海螺", "宝螺", "蛤蜊", "锥螺", "蚶贝", "牡蛎", "帽贝", "蛏子", "骨螺" };
        public const float GrabReach = .72f;
        public const float RevealRadius = 2.2f;
        public const float WorldCoinDiameter = .11f;
        public sealed class Pickup
        {
            public int id, kind;
            public string habitat;
            public GameObject visual;
            public Renderer renderer;
            public CoinProximitySparkles coinSparkles;
            public bool consumed, discovered, hiddenFromEntry;
            public float overhangClearance;
            public Vector3 approach = Vector3.back;
            public Vector3 approachPosition, graspPosition;
            public Vector3 GroundAnchor { get; internal set; }
        }
        readonly List<Pickup> pickups = new List<Pickup>(16);
        readonly Dictionary<string, ReefSupportGeometry> reefGeometry = new Dictionary<string, ReefSupportGeometry>();
        TreasureCollectionState inventory = new TreasureCollectionState();
        readonly RaycastHit[] obstructionHits = new RaycastHit[16];
        readonly Collider[] palmOverlaps = new Collider[16];
        sealed class SceneSurface
        {
            public Transform model;
            public Bounds bounds;
            public ReefSupportGeometry shape;
        }
        UnderwaterWorld world;
        Camera view;
        HandSwimController hands;
        TreasureFeedback feedback;
        bool ready, paused, focused = true;
        double nextPlaytestCheck;
        public IReadOnlyList<Pickup> Pickups => pickups;
        public TreasureCollectionState Inventory => inventory;
        public OceanCampaignController Campaign { get; private set; }
        public bool CoreActive => ready && !paused && focused && world != null && world.IsReady && !OceanEntry.BlocksGameplay;
        public bool GameplayActive => CoreActive && (Campaign == null || Campaign.GameplayAllowed);

        public static System.Collections.IEnumerator BuildStaged(UnderwaterWorld owner, Camera camera, HandSwimController controller, Vector3 spawn)
        {
            var root = new GameObject("Ocean Campaign - Discoveries");
            root.transform.SetParent(owner.transform, false);
            var hunt = root.AddComponent<TreasureHunt>();
            hunt.world = owner; hunt.view = camera; hunt.hands = controller;
            hunt.feedback = root.AddComponent<TreasureFeedback>();
            hunt.feedback.Initialize(camera);
            hunt.feedback.SetProgress(0);
            hunt.feedback.SetVisible(false);
            hunt.Campaign = root.AddComponent<OceanCampaignController>();
            hunt.Campaign.Initialize(hunt, owner, camera, controller, hunt.feedback, spawn);
            // Read the durable level before choosing its environment. A resumed sanctuary run
            // must never briefly expose classic pickups or retain its obstruction cache.
            yield return hunt.Campaign.PrepareEnvironmentForEntry();
            hunt.ready = true;
            Debug.Log($"[Treasure] campaign_ready level={hunt.Campaign.State.Current.level} pickups={hunt.pickups.Count} reveal={RevealRadius:F2}m grab={GrabReach:F2}m");
        }

        void LateUpdate()
        {
            bool playing = GameplayActive;
            if (feedback != null) feedback.SetVisible(CoreActive);
            if (!playing || view == null) { HideCoinSparkles(); return; }
            if (ProcessPlaytestRequest()) return;
            foreach (Pickup pickup in pickups)
            {
                if (pickup.consumed || pickup.visual == null) continue;
                float distance = Vector3.Distance(view.transform.position, pickup.renderer.bounds.center);
                float near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RevealRadius, .70f, distance));
                float glow = near * (.68f + .10f * Mathf.Sin(Time.time * 2.1f + pickup.id));
                // The world coin has its own gold-only surface highlight and tiny depth-tested glints.
                CollectibleVisuals.SetProximityGlow(pickup.visual, pickup.kind < 0 ? 0f : glow);
                if (pickup.coinSparkles != null) pickup.coinSparkles.Refresh(view, near);
                if (near > .01f && !pickup.discovered)
                { pickup.discovered = true; Debug.Log($"[Treasure] discovered id={pickup.id} kind={pickup.kind} habitat={pickup.habitat}"); }
            }
            if (hands == null) return;
            bool leftIntent = hands.TryGetWorldGrabIntent(HandType.HandLeft, out Vector3 leftPalm);
            bool rightIntent = hands.TryGetWorldGrabIntent(HandType.HandRight, out Vector3 rightPalm);
            if (!leftIntent && !rightIntent) return;
            // Fish move in the campaign's LateUpdate. Auto-sync is disabled, so refresh
            // their physical bounds once for an active grasp, not on every idle frame.
            Physics.SyncTransforms();
            if (leftIntent) TryPendingWorldGrasp(HandType.HandLeft, leftPalm);
            if (rightIntent) TryPendingWorldGrasp(HandType.HandRight, rightPalm);
        }

        void TryPendingWorldGrasp(HandType side, Vector3 palm)
        {
            bool left = side == HandType.HandLeft;
            if (TryWorldGrasp(palm, left))
            {
                hands.ConsumeWorldGrabIntent(side);
                Debug.Log($"[WorldGrab] hand={(left ? "left" : "right")} accepted=True palm={palm:F3}");
            }
            else if (left ? hands.LeftGraspStartedThisFrame : hands.RightGraspStartedThisFrame)
            {
                FindNearestPickup(palm, out float shellDistance);
                float fishDistance = Campaign != null ? Campaign.Creatures.NearestGraspDistance(palm) : float.PositiveInfinity;
                Debug.Log($"[WorldGrab] hand={(left ? "left" : "right")} accepted=False reachableShell={shellDistance:F3} reachableCreature={fishDistance:F3} palm={palm:F3}; waiting for contact inside intent window");
            }
        }

        bool TryWorldGrasp(Vector3 palm, bool left)
        {
            if (!GameplayActive || !Finite(palm)) return false;
            Pickup nearest = FindNearestPickup(palm, out float shellDistance);
            float fishDistance = Campaign != null ? Campaign.Creatures.NearestGraspDistance(palm) : float.PositiveInfinity;
            // Select what the hand actually touches first. A ground shell inside arm's reach
            // must not steal a grasp intended for a much closer fish.
            if (fishDistance + .015f < shellDistance)
                return Campaign != null && Campaign.TryCreatureGrasp(palm, left);
            if (nearest == null) return false;
            CollectNearest(nearest, out bool handled);
            return handled;
        }

        [Serializable] sealed class PlaytestRequest
        {
            public int version;
            public string requestId, action;
            public long expiresUnixSeconds;
            public int[] restoreConsumedShellIds;
        }
        [Serializable] sealed class PlaytestResult
        {
            public string requestId, error;
            public bool success, hiddenComplete;
            public int distinctCount;
            public Vector3 eyePosition, coinPosition;
        }

        // Explicit local test request only: no network listener, arbitrary transform or auto-grab.
        // An ordinary launch has no request and keeps the original new-expedition behavior.
        bool ProcessPlaytestRequest()
        {
            if (Time.unscaledTimeAsDouble < nextPlaytestCheck) return false;
            nextPlaytestCheck = Time.unscaledTimeAsDouble + .5;
            string path = Path.Combine(Application.persistentDataPath, "treasure-playtest-request.json");
            if (!File.Exists(path)) return false;
            var result = new PlaytestResult();
            try
            {
                if (new FileInfo(path).Length > 4096) throw new InvalidOperationException("Playtest request too large.");
                var request = JsonUtility.FromJson<PlaytestRequest>(File.ReadAllText(path));
                result.requestId = request?.requestId;
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (request == null || request.version != 1 || request.action != "move_to_hidden_coin" ||
                    !Guid.TryParse(request.requestId, out _) || request.expiresUnixSeconds < now ||
                    request.expiresUnixSeconds > now + 3600)
                    throw new InvalidOperationException("Invalid or expired local playtest request.");
                if (hands == null) throw new InvalidOperationException("Tracked player rig unavailable.");
                var restore = request.restoreConsumedShellIds ?? Array.Empty<int>();
                var distinctIds = new HashSet<int>();
                if (Campaign != null && restore.Length != 0) throw new InvalidOperationException("Campaign progress is restored from the local save, not a legacy test request.");
                if (restore.Length > 15) throw new InvalidOperationException("Invalid shell restore list.");
                foreach (int id in restore)
                    if (id < 0 || id >= pickups.Count || pickups[id].kind < 0 || !distinctIds.Add(id))
                        throw new InvalidOperationException("Restore accepts unique existing shell ids only.");
                Pickup coin = pickups.Find(p => p.kind == TreasureCollectionState.CoinKind && !p.consumed);
                if (coin == null) throw new InvalidOperationException("Hidden coin is already collected.");
                Vector3 target = coin.renderer.bounds.center;
                Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < .5f) forward = Vector3.forward;
                bool found = false;
                for (int i = 0; i < 12 && !found; i++)
                {
                    Vector3 direction = Quaternion.AngleAxis(i * 30f, Vector3.up) * forward;
                    Vector3 eye = target - direction * .58f + Vector3.up * .76f;
                    if (Physics.CheckSphere(eye, .25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                        !ClearReach(eye, target)) continue;
                    found = hands.TryRelocateHead(eye, direction);
                }
                if (!found) throw new InvalidOperationException("No clear viewing spot beside hidden coin.");
                // Restore only ids verified by the operator from this user's previous run.
                // No new-item, completion or hidden-reward effects are replayed.
                foreach (int id in restore)
                {
                    Pickup pickup = pickups[id];
                    inventory.Collect(pickup.id, pickup.kind);
                    pickup.consumed = true; pickup.visual.SetActive(false);
                }
                feedback.SetProgress(inventory.Mask);
                result.success = true; result.coinPosition = target;
                result.eyePosition = view.transform.position;
                Debug.Log($"[TreasurePlaytest] relocated request={request.requestId} eye={result.eyePosition:F3} coin={target:F3} distance={Vector3.Distance(result.eyePosition, target):F3} restoredShells={restore.Length} species={inventory.DistinctCount}/5 hidden={inventory.HiddenComplete}; coin awaits real grasp");
            }
            catch (Exception error)
            {
                result.error = error.Message;
                Debug.LogWarning("[TreasurePlaytest] rejected: " + error.Message);
            }
            result.distinctCount = inventory.DistinctCount; result.hiddenComplete = inventory.HiddenComplete;
            try
            {
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "treasure-playtest-result.json"),
                    JsonUtility.ToJson(result, true));
                File.Delete(path); // Consume only this fixed, explicitly supplied request file.
            }
            catch (Exception error)
            {
                nextPlaytestCheck = double.PositiveInfinity;
                Debug.LogWarning("[TreasurePlaytest] receipt unavailable; stopped polling: " + error.Message);
            }
            return true;
        }

        bool TryGrasp(Vector3 palm)
        {
            if (!GameplayActive || !Finite(palm)) return false;
            Pickup nearest = FindNearestPickup(palm, out _);
            if (nearest == null) return false;
            return CollectNearest(nearest);
        }

        Pickup FindNearestPickup(Vector3 palm, out float distance)
        {
            Pickup nearest = null;
            float closest = GrabReach * GrabReach;
            foreach (Pickup pickup in pickups)
            {
                if (pickup.consumed || pickup.renderer == null) continue;
                Vector3 surface = pickup.renderer.bounds.ClosestPoint(palm);
                float squared = (surface - palm).sqrMagnitude;
                if (squared > closest || !ClearReach(palm, pickup.renderer.bounds.center)) continue;
                closest = squared; nearest = pickup;
            }
            distance = nearest == null ? float.PositiveInfinity : Mathf.Sqrt(closest);
            return nearest;
        }

        bool CollectNearest(Pickup nearest) => CollectNearest(nearest, out _);

        bool CollectNearest(Pickup nearest, out bool handled)
        {
            handled = false;
            if (Campaign != null)
            {
                if (!Campaign.CollectPickup(nearest, out handled)) return false;
                // Retain the original five-species read-only preview surface for earlier Editor tools.
                if (nearest.kind < 5) inventory.Collect(nearest.id, nearest.kind);
            }
            else
            {
                var result = inventory.Collect(nearest.id, nearest.kind);
                if (result.outcome == TreasureCollectionState.Outcome.Ignored) return false;
                handled = true;
                feedback.SetProgress(inventory.Mask);
                if (nearest.kind < 0) feedback.ShowHiddenReward();
                else feedback.ShowCollection(nearest.kind, SpeciesNames[nearest.kind], result.count,
                    result.outcome == TreasureCollectionState.Outcome.DuplicateSpecies);
            }
            nearest.consumed = true;
            if (nearest.coinSparkles != null) nearest.coinSparkles.Dispose();
            nearest.visual.SetActive(false);
            return true;
        }

        bool ClearReach(Vector3 from, Vector3 target)
        {
            int overlaps = Physics.OverlapSphereNonAlloc(from, .008f, palmOverlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (overlaps == palmOverlaps.Length) return false;
            for (int i = 0; i < overlaps; i++)
            {
                Collider hit = palmOverlaps[i];
                if (hit == null || (hands != null && hit.transform.IsChildOf(hands.transform))) continue;
                return false;
            }
            Vector3 delta = target - from;
            float distance = delta.magnitude;
            if (distance < .001f) return true;
            int count = Physics.RaycastNonAlloc(from, delta / distance, obstructionHits,
                distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // Fail closed if the buffer is full; collection must not tunnel through reef obstacles.
            if (count == obstructionHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Collider hit = obstructionHits[i].collider;
                if (hit == null || (hands != null && hit.transform.IsChildOf(hands.transform))) continue;
                return false;
            }
            return true;
        }

        internal void ClearForUnavailableLevel()
        {
            foreach (Pickup item in pickups)
            {
                if (item.coinSparkles != null) item.coinSparkles.Dispose();
                if (item.visual != null) { item.visual.SetActive(false); Destroy(item.visual); }
            }
            pickups.Clear(); inventory = new TreasureCollectionState();
        }

        public void RebuildDiscoveries(Vector3 spawn, OceanCampaignState state)
        {
            ClearForUnavailableLevel();
            if (!OceanCampaignAvailability.IsOpen(state.Current.level)) return;
            int species = state.Current.level == 1 ? 5 : state.Current.level == 2 ? 10 : state.Current.level == 3 ? 2 : 0;
            if (state.Current.level <= 5) PlaceDiscoveries(spawn, species);
            else PlaceExpeditionDiscoveries(spawn, state.Definition);
            foreach (Pickup item in pickups)
            {
                if (!state.IsConsumed(item.id)) continue;
                item.consumed = true; item.visual.SetActive(false);
                if (item.coinSparkles != null) item.coinSparkles.Dispose();
                if (item.kind < 5) inventory.Collect(item.id, item.kind);
            }
            Debug.Log($"[Campaign] layout level={state.Current.level} shells={pickups.Count - state.CoinTargetCount} shellMask={state.Definition.ShellMask} coins={state.CoinTargetCount} consumed={state.Current.consumedPickupIds.Count}");
        }

        void PlaceDiscoveries(Vector3 spawn, int species)
        {
            var rng = new System.Random(20002026);
            var grass = world.ActiveEnvironmentRoot != null ? world.ActiveEnvironmentRoot.GetComponentInChildren<SeagrassMeadow>() : null;
            Transform reef = world.ActiveReefRoot;
            var reefBounds = new List<Bounds>();
            var coralModels = new List<Transform>();
            var surfaces = new List<SceneSurface>();
            if (reef != null) foreach (Transform group in reef)
            {
                if (group.childCount == 0) continue;
                Transform model = group.GetChild(0);
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                reefBounds.Add(bounds);
                surfaces.Add(new SceneSurface { model = model, bounds = bounds });
                if (group.name.StartsWith("Coral ", StringComparison.Ordinal) && FlatDistance(bounds.center, spawn) < 21f)
                    coralModels.Add(model);
            }
            // Five sand discoveries form a readable first trail, with space between finds.
            for (int kind = 0; kind < species; kind++)
            {
                bool placed = false;
                for (int attempt = 0; attempt < 900 && !placed; attempt++)
                {
                    float radius = kind == 0 ? Range(rng, 1.5f, 3f) : Range(rng, 3f + kind, 7f + kind * 2f);
                    float angle = Range(rng, -.95f, .95f);
                    Vector2 point = new Vector2(spawn.x + Mathf.Sin(angle) * radius, spawn.z + Mathf.Cos(angle) * radius);
                    if (!GroundCandidate(point, reefBounds, .24f) || (grass != null && grass.IsNearGrass(point, .36f))) continue;
                    AddPickup(kind, "sand", point, rng); placed = true;
                }
                if (!placed) throw new InvalidOperationException("No accessible sand placement for shell " + kind);
            }
            // Real tuft positions, not a random point merely near the meadow's bounding rectangle.
            if (species > 0 && (grass == null || grass.RootPositions.Count == 0)) throw new InvalidOperationException("Treasure meadow missing.");
            int offset = species > 0 ? rng.Next(grass.RootPositions.Count) : 0;
            int habitatSpecies = Math.Min(5, species);
            for (int slot = 0; slot < habitatSpecies; slot++)
            {
                int kind = species == 10 ? slot + 5 : slot;
                bool placed = false;
                for (int i = 0; i < grass.RootPositions.Count && !placed; i++)
                {
                    Vector2 point = grass.RootPositions[(offset + i * 37) % grass.RootPositions.Count];
                    float distance = Vector2.Distance(point, new Vector2(spawn.x, spawn.z));
                    if (distance < 3f || distance > 19f || !GroundCandidate(point, reefBounds, -.03f)) continue;
                    AddPickup(kind, "seagrass", point, rng); placed = true; offset += i + 13;
                }
                if (!placed) throw new InvalidOperationException("No accessible grass placement for shell " + kind);
            }
            // Use actual mesh intersections to hide shells beneath coral overhangs with a visible reaching route.
            coralModels.Sort((a, b) => FlatDistance(a.position, spawn).CompareTo(FlatDistance(b.position, spawn)));
            var geometry = reefGeometry;
            int coralKind = 0;
            foreach (Transform model in coralModels)
            {
                if (coralKind == habitatSpecies) break;
                string resourceName = model.name.Replace("(Clone)", "");
                if (!geometry.TryGetValue(resourceName, out ReefSupportGeometry shape))
                {
                    var source = Resources.Load<GameObject>("RealisticReef/" + resourceName);
                    if (source == null) continue;
                    shape = ReefSupportGeometry.Read(source, true); geometry.Add(resourceName, shape);
                }
                for (int i = 0; i < 160 && coralKind < habitatSpecies; i++)
                {
                    float x = Range(rng, shape.bounds.min.x, shape.bounds.max.x);
                    float z = Range(rng, shape.bounds.min.z, shape.bounds.max.z);
                    if (!shape.TryVerticalSpan(x, z, out float bottom, out float top)) continue;
                    Vector3 lower = model.TransformPoint(new Vector3(x, bottom, z));
                    Vector3 upper = model.TransformPoint(new Vector3(x, top, z));
                    Vector2 point = new Vector2(lower.x, lower.z);
                    float ground = Ground(point);
                    if (lower.y - ground < .55f || lower.y - ground > 1.5f || upper.y - lower.y < .04f ||
                        !GroundCandidate(point, null, 0f) || FlatDistance(lower, spawn) < 3f) continue;
                    bool rimClear = EmptyAt(new Vector3(point.x, ground + .13f, point.y), .12f, surfaces, geometry);
                    for (int sample = 0; sample < 8; sample++)
                    {
                        float angle = sample * Mathf.PI * .25f;
                        Vector3 rimWorld = new Vector3(point.x + Mathf.Cos(angle) * .15f, ground + .13f,
                            point.y + Mathf.Sin(angle) * .15f);
                        if (!EmptyAt(rimWorld, .12f, surfaces, geometry))
                        { rimClear = false; break; }
                    }
                    if (!rimClear) continue;
                    Pickup hidden = AddPickup(coralKind, "coral-overhang", point, rng, true);
                    if (!TryApproach(hidden.renderer.bounds.center, surfaces, geometry,
                        out Vector3 approach, out Vector3 eye, out Vector3 palm))
                    { Destroy(hidden.visual); continue; }
                    hidden.overhangClearance = lower.y - ground;
                    hidden.approach = approach; hidden.approachPosition = eye; hidden.graspPosition = palm;
                    RegisterPickup(hidden); coralKind++;
                    break;
                }
            }
            if (coralKind != habitatSpecies) throw new InvalidOperationException("Only " + coralKind + "/" + habitatSpecies + " verified coral niches.");
            for (int attempt = 0; attempt < 1600; attempt++)
            {
                float angle = Range(rng, -2.7f, -1.1f), radius = Range(rng, 10f, 17f);
                Vector2 point = new Vector2(spawn.x + Mathf.Sin(angle) * radius, spawn.z + Mathf.Cos(angle) * radius);
                if (!GroundCandidate(point, reefBounds, .08f)) continue;
                AddPickup(-1, "hidden-cove", point, rng); break;
            }
            if (pickups.Count != species + habitatSpecies * 2 + 1) throw new InvalidOperationException("Missing campaign discoveries or hidden coin.");
        }

        static bool EmptyAt(Vector3 point, float clearance, List<SceneSurface> surfaces,
            Dictionary<string, ReefSupportGeometry> cache)
        {
            foreach (SceneSurface surface in surfaces)
            {
                Bounds b = surface.bounds;
                if (point.x < b.min.x || point.x > b.max.x || point.z < b.min.z || point.z > b.max.z ||
                    point.y - clearance > b.max.y || point.y + clearance < b.min.y) continue;
                ReadSurfaceShape(surface, cache);
                Vector3 local = surface.model.InverseTransformPoint(point);
                if (!surface.shape.TryVerticalSpan(local.x, local.z, out float low, out float high)) continue;
                float bottom = surface.model.TransformPoint(new Vector3(local.x, low, local.z)).y;
                float top = surface.model.TransformPoint(new Vector3(local.x, high, local.z)).y;
                // A conservative occupied column: do not infer an internal cavity between two scan surfaces.
                if (point.y + clearance >= bottom && point.y - clearance <= top) return false;
            }
            return true;
        }

        bool TryApproach(Vector3 target, List<SceneSurface> surfaces, Dictionary<string, ReefSupportGeometry> cache,
            out Vector3 direction, out Vector3 eyePosition, out Vector3 palmPosition)
        {
            for (int a = 0; a < 12; a++)
            {
                float angle = a * Mathf.PI / 6f;
                Vector3 outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                Vector3 eye = target + outward * .9f + Vector3.up * .5f;
                if (Physics.CheckSphere(eye, .20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                bool clear = true;
                for (int s = 0; s <= 10; s++)
                    if (!EmptyAt(Vector3.Lerp(target, eye, s / 10f), .065f, surfaces, cache)) { clear = false; break; }
                Vector3 palm = target + outward * .5f + Vector3.up * .15f;
                if (!clear || !ClearReach(palm, target) || !MeshLineClear(eye, target, surfaces, cache) ||
                    !MeshLineClear(palm, target, surfaces, cache)) continue;
                direction = outward; eyePosition = eye; palmPosition = palm; return true;
            }
            direction = eyePosition = palmPosition = Vector3.zero; return false;
        }

        static bool MeshLineClear(Vector3 from, Vector3 to, List<SceneSurface> surfaces,
            Dictionary<string, ReefSupportGeometry> cache)
        {
            Vector3 delta = to - from; float distance = delta.magnitude;
            if (distance < .001f) return true;
            var ray = new Ray(from, delta / distance);
            foreach (SceneSurface surface in surfaces)
            {
                if (!surface.bounds.Contains(from) &&
                    (!surface.bounds.IntersectRay(ray, out float entry) || entry > distance)) continue;
                ReadSurfaceShape(surface, cache);
                if (surface.shape.IntersectsSegment(surface.model.InverseTransformPoint(from),
                    surface.model.InverseTransformPoint(to))) return false;
            }
            return true;
        }

        static void ReadSurfaceShape(SceneSurface surface, Dictionary<string, ReefSupportGeometry> cache)
        {
            if (surface.shape != null) return;
            string name = surface.model.name.Replace("(Clone)", "");
            if (cache.TryGetValue(name, out surface.shape)) return;
            var source = Resources.Load<GameObject>("RealisticReef/" + name);
            if (source != null)
            {
                surface.shape = ReefSupportGeometry.Read(source, true);
                cache.Add(name, surface.shape);
            }
            else
            {
                // The sanctuary arch and reef platforms are generated meshes. Read their
                // actual triangles in model-local coordinates; a giant bounds box would
                // incorrectly close the arch and invent concealment in empty water.
                surface.shape = ReefSupportGeometry.Read(surface.model.gameObject, true);
            }
        }

        bool GroundCandidate(Vector2 point, List<Bounds> blockers, float margin)
        {
            float floor = Ground(point), dx = Ground(point + new Vector2(.14f, 0f)) - Ground(point - new Vector2(.14f, 0f)),
                dz = Ground(point + new Vector2(0f, .14f)) - Ground(point - new Vector2(0f, .14f));
            if (dx * dx + dz * dz > .007f) return false;
            foreach (Pickup previous in pickups)
                if (Vector2.Distance(point, new Vector2(previous.GroundAnchor.x, previous.GroundAnchor.z)) < 1.35f) return false;
            if (blockers != null) foreach (Bounds b in blockers)
            {
                if (point.x > b.min.x - margin && point.x < b.max.x + margin &&
                    point.y > b.min.z - margin && point.y < b.max.z + margin) return false;
            }
            return !Physics.CheckSphere(new Vector3(point.x, floor + .16f, point.y), .13f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        Pickup AddPickup(int kind, string habitat, Vector2 point, System.Random rng, bool provisional = false, int pickupId = -1)
        {
            GameObject visual = kind < 0 ? CollectibleVisuals.CreateCoin(transform) : CollectibleVisuals.CreateShell(transform, kind);
            if (kind < 0) visual.transform.localScale = Vector3.one * (WorldCoinDiameter / CollectibleVisuals.CoinDiameter);
            float dx = (Ground(point + new Vector2(.1f, 0f)) - Ground(point - new Vector2(.1f, 0f))) / .2f;
            float dz = (Ground(point + new Vector2(0f, .1f)) - Ground(point - new Vector2(0f, .1f))) / .2f;
            visual.transform.rotation = Quaternion.FromToRotation(Vector3.up, new Vector3(-dx, 1f, -dz).normalized) *
                Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f);
            visual.transform.position = new Vector3(point.x, Ground(point), point.y);
            var renderer = visual.GetComponentInChildren<Renderer>();
            if (renderer == null) throw new InvalidOperationException("Collectible missing renderer.");
            // Find contact on the actual generated lower rim, keeping the authored object on the terrain.
            float adjustment = float.NegativeInfinity;
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                float cutoff = mesh.bounds.min.y + mesh.bounds.size.y * .08f;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    if (vertex.y > cutoff) continue;
                    Vector3 worldVertex = filter.transform.TransformPoint(vertex);
                    adjustment = Mathf.Max(adjustment, UnderwaterWorld.SampleRenderedSeabedHeight(worldVertex.x, worldVertex.z) - worldVertex.y);
                }
            }
            if (!float.IsNegativeInfinity(adjustment)) visual.transform.position += Vector3.up * (adjustment - .002f);
            var pickup = new Pickup { id = pickupId >= 0 ? pickupId : kind < 0 ? OceanLevelCatalog.CoinId(0) : pickups.Count, kind = kind, habitat = habitat, visual = visual, renderer = renderer,
                GroundAnchor = visual.transform.position };
            // Add after contact fitting, so sparkle bounds never affect grounding or grab reach.
            if (kind < 0) pickup.coinSparkles = CoinProximitySparkles.Create(visual.transform);
            if (!provisional) RegisterPickup(pickup);
            return pickup;
        }

        void RegisterPickup(Pickup pickup)
        {
            pickups.Add(pickup);
            Debug.Log($"[Treasure] placed id={pickup.id} kind={pickup.kind} habitat={pickup.habitat} pos={pickup.GroundAnchor:F3} size={pickup.renderer.bounds.size:F3}");
        }

        static float Range(System.Random rng, float min, float max) => min + (max - min) * (float)rng.NextDouble();
        static float Ground(Vector2 point) => UnderwaterWorld.SampleRenderedSeabedHeight(point.x, point.y);
        static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) &&
            !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);
        void HideCoinSparkles()
        {
            foreach (Pickup pickup in pickups)
                if (pickup.coinSparkles != null) pickup.coinSparkles.Hide();
        }
        void OnDisable() { HideCoinSparkles(); }
        void OnApplicationPause(bool value) { paused = value; if (value) HideCoinSparkles(); }
        void OnApplicationFocus(bool value)
        {
#if !UNITY_EDITOR
            focused = value;
            if (!value) HideCoinSparkles();
#endif
        }

#if UNITY_EDITOR
        // Drives the same distance/occlusion/pickup transaction without inventing tracked hands.
        public bool TryGraspForValidation(Vector3 palm) => TryGrasp(palm);
        public bool TryWorldGraspForValidation(Vector3 palm, bool left)
        { Physics.SyncTransforms(); return TryWorldGrasp(palm, left); }
#endif
    }
}

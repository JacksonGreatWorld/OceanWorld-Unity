using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SwanUnderwater
{
    /// <summary>Small, reachable quest populations; ambient world fish keep their existing simulation.</summary>
    public sealed class OceanCampaignCreatures : MonoBehaviour
    {
        public static readonly string[] FishNames = OceanLevelCatalog.FishNames;
        public static readonly string[] FishResourcePaths = OceanLevelCatalog.FishPaths;
        public const string SharkName = "大白鲨", SharkResourcePath = "Fish/15_great_white_shark";
        public const float CatchReach = .66f, ToolReach = .72f;

        public sealed class CreatureTarget
        {
            public int Species { get; internal set; }
            public bool IsShark { get; internal set; }
            public int SharkId { get; internal set; } = -1;
            public string SharkLabel { get; internal set; }
            public int ObservationSpecies => !IsShark ? Species : Visual != null &&
                Visual.name.IndexOf("hammerhead", StringComparison.OrdinalIgnoreCase) >= 0 ? 11 : 10;
            internal OceanSharkMotion sharkMotion;
            internal OceanFishMotion sharkVisualMotion;
            internal Transform originalParent;
            internal int originalSibling;
            internal int reactedHits;
            internal Text sharkText;
            internal Canvas sharkCanvas;
            internal int shownHits = -1;
            public bool IsAmbient { get; internal set; }
            public GameObject Visual { get; internal set; }
            public CapsuleCollider Body { get; internal set; }
            public Bounds WorldBounds => Body.bounds;
            public bool IsFleeing => fleeRemaining > 0f;
            public float FleeCooldown => fleeCooldown;
            public int FleeCount { get; internal set; }
            internal Transform root;
            internal Vector3 anchor;
            internal float phase;
            internal bool originalActive;
            internal float clearanceRadius;
            internal float fleeRemaining, fleeCooldown;
            internal Vector3 fleeTarget;
        }

        readonly List<CreatureTarget> targets = new List<CreatureTarget>(11);
        readonly List<Bounds> reefBounds = new List<Bounds>();
        readonly List<GameObject> ambientCandidates = new List<GameObject>();
        readonly RaycastHit[] obstruction = new RaycastHit[24];
        readonly Collider[] palmObstruction = new Collider[16];
        readonly List<float> sharkTerrainStops = new List<float>(48);
        static readonly float[] EscapeAngles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f };
        UnderwaterWorld world;
        Camera view;
        HandSwimController hands;
        OceanCampaignState state;
        Action<string, int> notify;
        CreatureTarget shark;
        Vector3 spawn;
        bool active;
        float hintUntil;
        public IReadOnlyList<CreatureTarget> Targets => targets;
        public OceanSpearGun SpearGun { get; private set; }
        public bool ToolHeld => SpearGun != null && SpearGun.Held;
        public bool HeldInLeftHand => SpearGun != null && SpearGun.HeldLeft;
        public bool SharkEscaping => shark != null && shark.sharkMotion != null && shark.sharkMotion.Escaping;
        public Vector3 SharkEscapeTarget => shark != null && shark.sharkMotion != null ? shark.sharkMotion.EscapeTarget : Vector3.zero;
        public float SharkEscapeDistance => shark != null && shark.sharkMotion != null ? shark.sharkMotion.EscapeDistance : 0f;
        public int SharkEscapeCount => shark != null && shark.sharkMotion != null ? shark.sharkMotion.EscapeCount : 0;
        public float SharkClearanceRadius => shark != null ? shark.clearanceRadius : 0f;
        public int LastInteractedSharkId { get; private set; } = -1;
        public string SharkDisplayName(int identity)
        {
            foreach (var target in targets) if (target.IsShark && target.SharkId == identity) return target.SharkLabel;
            return "鲨鱼 " + (identity + 1);
        }

        public void Initialize(UnderwaterWorld owner, Camera camera, HandSwimController controller,
            OceanCampaignState campaign, Action<string, int> eventCallback)
        {
            world = owner; view = camera; hands = controller; state = campaign; notify = eventCallback;
        }

        public void ConfigureLevel(Vector3 levelSpawn)
        {
            ClearActors(); spawn = levelSpawn;
            if (state == null || state.Current == null || !OceanCampaignAvailability.IsOpen(state.Current.level) || world == null || view == null) return;
            CacheReefBounds();
            for (int kind = 0; kind < OceanLevelCatalog.FishSpeciesCount; kind++)
                if ((state.FishTargetMask & (1 << kind)) != 0 && (state.Current.fishMask & (1 << kind)) == 0)
                    CreateTarget(kind, false);
            for (int kind = 0; kind < OceanLevelCatalog.FishSpeciesCount; kind++)
            {
                if ((state.FishTargetMask & (1 << kind)) == 0) continue;
                world.GetCampaignHeroFish(FishResourcePaths[kind], ambientCandidates);
                foreach (GameObject model in ambientCandidates) AddAmbientTarget(model, kind);
            }
            if (state.RequiresShark)
            {
                if (!state.SharkCaughtFor(0)) shark = CreateTarget(-1, true);
                world.GetCampaignSharks(ambientCandidates);
                for (int index = 0; index < ambientCandidates.Count; index++)
                    AddAmbientShark(ambientCandidates[index], index + 1);
                SpearGun = OceanSpearGun.Create(transform, view, hands, state, null,
                    spawn, reefBounds, ClearReach, ToolEvent);
                foreach (var target in targets)
                    if (target.IsShark && target.Body != null && target.Body.gameObject.activeInHierarchy)
                        SpearGun.RegisterShark(target.Body, target.SharkId);
            }
        }

        public void TickGameplay(float dt, bool playing)
        {
            active = playing && state != null && state.CanCollect;
            if (!active || !Finite(dt) || dt <= 0f || dt > .10f)
            {
                return;
            }
            foreach (CreatureTarget target in targets)
            {
                if (target.root == null || !target.root.gameObject.activeInHierarchy) continue;
                if (target.IsShark) { TickShark(target, dt); continue; }
                if (target.IsAmbient) continue;
                Vector3 threat = view.transform.position;
                float threatDistance = Vector3.Distance(threat, target.WorldBounds.center);
                bool close = threatDistance < 1.5f;
                if (hands != null && hands.TryGetClosestActiveHand(target.WorldBounds.center, out Vector3 palm, out _))
                {
                    float handDistance = Vector3.Distance(palm, target.Body.ClosestPoint(palm));
                    close |= handDistance < 1.05f;
                    if (handDistance < threatDistance) { threat = palm; threatDistance = handDistance; }
                }
                if (state.Current.level > 5 && TickFishFlee(target, threat, threatDistance, dt)) continue;
                if (close) continue; // The cooldown is a real stationary opportunity to reach and grasp.
                float speed = state.Current.level > 5 ? state.Definition.FishSpeed : 1f;
                float phase = target.phase + dt * .32f * speed;
                Vector3 desired = target.anchor + FishOrbit(phase);
                Vector3 next = Vector3.MoveTowards(target.root.position, desired, dt * .18f * speed);
                if (!CreatureSegmentClear(target.root.position, next, target.clearanceRadius, state.Current.level > 5)) continue;
                target.phase = phase; target.root.position = next;
                Vector3 tangent = new Vector3(-Mathf.Sin(phase) * .45f, .035f * 1.7f * Mathf.Cos(phase * 1.7f), Mathf.Cos(phase) * .32f);
                if (tangent.sqrMagnitude > .00001f)
                    target.root.rotation = Quaternion.RotateTowards(target.root.rotation, Quaternion.LookRotation(tangent), dt * 65f);
            }
        }

        static Vector3 FishOrbit(float phase) => new Vector3(Mathf.Cos(phase) * .45f, Mathf.Sin(phase * 1.7f) * .035f, Mathf.Sin(phase) * .32f);
        bool TickFishFlee(CreatureTarget target, Vector3 threat, float distance, float dt)
        {
            target.fleeCooldown = Mathf.Max(0f, target.fleeCooldown - dt);
            if (!target.IsFleeing && target.fleeCooldown <= 0f && distance < state.Definition.FishFleeDistance)
            {
                // One short response, then a 4–6 second catch window even if the player remains close.
                target.fleeCooldown = 4f + target.Species * (2f / 9f);
                Vector3 away = Vector3.ProjectOnPlane(target.root.position - threat, Vector3.up).normalized;
                if (away.sqrMagnitude < .1f) away = target.root.forward;
                foreach (float angle in EscapeAngles)
                {
                    Vector3 candidate = target.root.position + Quaternion.AngleAxis(angle, Vector3.up) * away * .65f;
                    if (!CreatureSegmentClear(target.root.position, candidate, target.clearanceRadius, true)) continue;
                    target.fleeTarget = candidate; target.fleeRemaining = .85f; target.FleeCount++; break;
                }
            }
            if (!target.IsFleeing) return false;
            Vector3 from = target.root.position;
            float speed = Mathf.Min(.80f, .58f * state.Definition.FishSpeed);
            Vector3 next = Vector3.MoveTowards(from, target.fleeTarget, dt * speed);
            bool clear = CreatureSegmentClear(from, next, target.clearanceRadius, true);
            if (clear)
            {
                target.root.position = next;
                if ((next - from).sqrMagnitude > .000001f)
                    target.root.rotation = Quaternion.RotateTowards(target.root.rotation, Quaternion.LookRotation(next - from), dt * 100f);
            }
            target.fleeRemaining = Mathf.Max(0f, target.fleeRemaining - dt);
            if (!clear || target.fleeRemaining <= 0f || Vector3.Distance(next, target.fleeTarget) < .03f)
            {
                target.fleeRemaining = 0f;
                target.fleeCooldown = 4f + target.Species * (2f / 9f);
                target.anchor = target.root.position - FishOrbit(target.phase);
            }
            return true;
        }

        // The caller supplies one fresh open-to-fist edge; this method owns scene reach/occlusion and the state transaction.
        public bool TryGrasp(Vector3 palm, bool left)
        {
            if (!active || !Finite(palm)) return false;
            CreatureTarget nearest = NearestCreature(palm, out float nearestDistance);
            // Prefer contact with a nearby animal over a distant tool in the same reach sphere.
            float gunDistance = GunGraspDistance(palm);
            if (gunDistance <= nearestDistance && SpearGun != null && SpearGun.TryPickup(palm, left)) return true;
            if (nearest == null) return false;
            return GraspCreature(nearest);
        }

        float GunGraspDistance(Vector3 palm)
        {
            if (SpearGun == null || !SpearGun.CanPickup) return float.PositiveInfinity;
            Bounds bounds = SpearGun.PickupBounds;
            float distance = Vector3.Distance(palm, bounds.ClosestPoint(palm));
            return distance <= ToolReach && ClearReach(palm, bounds.center) ? distance : float.PositiveInfinity;
        }

        public float NearestGraspDistance(Vector3 palm)
        {
            if (!active || !Finite(palm)) return float.PositiveInfinity;
            NearestCreature(palm, out float distance);
            return Mathf.Min(distance, GunGraspDistance(palm));
        }

        CreatureTarget NearestCreature(Vector3 palm, out float nearestDistance)
        {
            CreatureTarget nearest = null; nearestDistance = CatchReach;
            foreach (CreatureTarget target in targets)
            {
                if (target.root == null || !target.root.gameObject.activeSelf) continue;
                Vector3 point = target.Body.ClosestPoint(palm);
                float distance = Vector3.Distance(palm, point);
                if (distance > nearestDistance || !ClearReach(palm, target.WorldBounds.center)) continue;
                nearest = target; nearestDistance = distance;
            }
            if (nearest == null) nearestDistance = float.PositiveInfinity;
            return nearest;
        }

        bool GraspCreature(CreatureTarget nearest)
        {
            if (nearest.IsShark)
            {
                if (!state.Current.toolOwned)
                { Hint("need-tool", 0); return true; }
                LastInteractedSharkId = nearest.SharkId;
                if (state.SharkHitsFor(nearest.SharkId) < state.SharkHitTarget)
                { Hint("shark-remaining", state.SharkHitTarget - state.SharkHitsFor(nearest.SharkId)); return true; }
                if (state.CatchShark(nearest.SharkId))
                {
                    nearest.root.gameObject.SetActive(false);
                    if (SpearGun != null) SpearGun.Drop(false);
                    notify?.Invoke("shark", 0);
                }
                return true;
            }
            if (state.CollectFish(nearest.Species))
            {
                nearest.root.gameObject.SetActive(false);
                notify?.Invoke("fish", nearest.Species);
            }
            else if ((state.Current.fishMask & (1 << nearest.Species)) != 0) Hint("fish-owned", nearest.Species);
            else if (state.Definition.OrderedFish && state.NextFishSpecies >= 0) Hint("fish-order", state.NextFishSpecies);
            return true;
        }

        void AddAmbientTarget(GameObject model, int kind)
        {
            if (model == null || !model.activeSelf) return;
            var hitRoot = new GameObject("Campaign catch volume"); hitRoot.transform.SetParent(model.transform, false);
            // A unit-scale child keeps contact dimensions in metres while retaining the ambient fish's own motion.
            Vector3 scale = model.transform.lossyScale;
            if (Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) < .0001f) { Destroy(hitRoot); return; }
            hitRoot.transform.localScale = new Vector3(1f / Mathf.Abs(scale.x), 1f / Mathf.Abs(scale.y), 1f / Mathf.Abs(scale.z));
            Bounds local = default; bool haveBounds = false;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                Bounds bounds = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    point = hitRoot.transform.InverseTransformPoint(renderer.transform.TransformPoint(point));
                    if (!haveBounds) { local = new Bounds(point, Vector3.zero); haveBounds = true; } else local.Encapsulate(point);
                }
            }
            if (!haveBounds) { Destroy(hitRoot); return; }
            var body = hitRoot.AddComponent<CapsuleCollider>(); body.isTrigger = true; body.direction = 2;
            body.center = local.center;
            body.radius = Mathf.Max(.035f, Mathf.Min(local.size.x, local.size.y) * .45f);
            body.height = Mathf.Max(body.radius * 2f, local.size.z * .85f);
            targets.Add(new CreatureTarget { Species = kind, IsAmbient = true, Visual = model, Body = body,
                root = model.transform, originalActive = model.activeSelf });
        }

        CreatureTarget CreateTarget(int kind, bool isShark)
        {
            var root = new GameObject(isShark ? "Campaign - great white shark" : "Campaign - " + FishNames[kind]);
            root.transform.SetParent(transform, false);
            GameObject visual = world.CreateCampaignCreature(isShark ? SharkResourcePath : FishResourcePaths[kind], root.transform,
                isShark ? 1.65f : OceanLevelCatalog.FishLengths[kind]);
            Bounds bounds = ModelBounds(visual);
            // Recentre the imported mesh inside an unscaled gameplay root, preserving the prefab's original axes.
            visual.transform.position -= bounds.center - root.transform.position;
            bounds = ModelBounds(visual);
            var body = root.AddComponent<CapsuleCollider>(); body.isTrigger = true; body.direction = 2;
            body.center = Vector3.zero;
            body.radius = isShark ? Mathf.Max(.10f, Mathf.Min(bounds.size.x * .30f, bounds.size.y * .44f)) : Mathf.Max(.055f, Mathf.Min(bounds.size.x, bounds.size.y) * .45f);
            body.height = Mathf.Max(body.radius * 2f, bounds.size.z * (isShark ? .72f : .85f));
            float clearanceRadius = bounds.extents.magnitude + .04f;
            Vector3 anchor = FindWaterPoint(isShark ? 4 : kind, isShark,
                isShark ? Mathf.Max(1.55f, clearanceRadius + .78f) : Mathf.Max(.85f, clearanceRadius + .55f));
            float phase = isShark ? Mathf.PI : kind * 1.3f;
            root.transform.position = anchor + new Vector3(Mathf.Cos(phase) * (isShark ? .72f : .45f), Mathf.Sin(phase * 1.7f) * .035f, Mathf.Sin(phase) * (isShark ? .42f : .32f));
            root.transform.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(phase), 0, Mathf.Cos(phase)));
            var target = new CreatureTarget { Species = kind, IsShark = isShark, Visual = visual, Body = body,
                root = root.transform, anchor = anchor, phase = phase, clearanceRadius = clearanceRadius };
            targets.Add(target);
            if (isShark) ConfigureShark(target, 0, "大白鲨");
            return target;
        }

        void CacheReefBounds()
        {
            reefBounds.Clear();
            if (world.ActiveSolidBounds != null && world.ActiveSolidBounds.Count > 0)
            {
                foreach (Bounds bounds in world.ActiveSolidBounds) reefBounds.Add(bounds);
                return;
            }
            Transform reef = world.ActiveReefRoot;
            if (reef == null) return;
            foreach (Transform group in reef)
            {
                if (group.childCount == 0) continue;
                Renderer[] renderers = group.GetChild(0).GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                reefBounds.Add(bounds);
            }
        }
        Vector3 FindWaterPoint(int index, bool isShark, float clearance)
        {
            Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .5f) forward = Vector3.forward;
            for (int attempt = 0; attempt < 180; attempt++)
            {
                bool expandedLevel = state.Current.level > 5;
                float angle = -65f + index * (expandedLevel ? 137.5f : 31f) + (attempt % 12) * 29f;
                float radius = expandedLevel
                    ? Mathf.Lerp(3.2f, Mathf.Max(3.2f, state.Definition.SearchRadius - clearance), ((index * 7 + attempt / 12) % 13) / 12f)
                    : 3.2f + index * .60f + (attempt / 12) * .45f;
                Vector3 point = spawn + Quaternion.AngleAxis(angle, Vector3.up) * forward * radius;
                if (!WithinMovementArea(point, clearance)) continue;
                point.y = Mathf.Max(UnderwaterWorld.SampleRenderedSeabedHeight(point.x, point.z) + clearance + .3f, view.transform.position.y - .25f);
                if (Physics.CheckSphere(point, clearance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                bool blocked = false;
                foreach (Bounds reef in reefBounds)
                {
                    Bounds expanded = reef; expanded.Expand(clearance * 2f);
                    if (isShark || expandedLevel ? expanded.Contains(point) : reef.SqrDistance(point) < clearance * clearance)
                    { blocked = true; break; }
                }
                foreach (CreatureTarget other in targets)
                    if (!other.IsAmbient && Vector3.Distance(other.anchor, point) < 2f) { blocked = true; break; }
                if (!blocked) return point;
            }
            throw new InvalidOperationException("No clear, reachable campaign water pocket for target " + index);
        }

        void ToolEvent(string action, int value) => notify?.Invoke(action, value);

        public void NotifySharkHit(Vector3 incomingDirection) => NotifySharkHit(0, incomingDirection);
        public void NotifySharkHit(int identity, Vector3 incomingDirection)
        {
            foreach (var target in targets)
            {
                if (!target.IsShark || target.SharkId != identity || target.root == null || !target.root.gameObject.activeInHierarchy) continue;
                int hits = state.SharkHitsFor(identity);
                if (hits <= target.reactedHits) return;
                target.reactedHits = hits; LastInteractedSharkId = identity;
                // The last required hit stops the individual shark for a reachable capture.
                if (hits < state.SharkHitTarget)
                    target.sharkMotion.ReactToHit(incomingDirection, view.transform.position);
                Debug.Log($"[CampaignShark] hit id={identity} individual={hits}/{state.SharkHitTarget} best={state.Current.sharkHits} escape={target.sharkMotion.Escaping}");
                return;
            }
        }

        void AddAmbientShark(GameObject model, int identity)
        {
            Bounds bounds = ModelBounds(model);
            Transform oldParent = model.transform.parent; int sibling = model.transform.GetSiblingIndex();
            // Rotate a centred, unscaled wrapper; the imported mesh keeps its original axes/scale and shader.
            var wrapper = new GameObject("Campaign shark steering " + identity);
            wrapper.transform.SetParent(oldParent, false);
            wrapper.transform.SetPositionAndRotation(bounds.center, model.transform.rotation);
            model.transform.SetParent(wrapper.transform, true);
            var body = wrapper.AddComponent<CapsuleCollider>(); body.direction = 2; body.isTrigger = true;
            Bounds local = LocalModelBounds(model, wrapper.transform);
            body.center = local.center;
            body.radius = Mathf.Max(.10f, Mathf.Min(local.size.x * .34f, local.size.y * .46f));
            body.height = Mathf.Max(body.radius * 2f, local.size.z * .85f);
            var target = new CreatureTarget { Species = -1, IsShark = true, IsAmbient = true,
                Visual = model, Body = body, root = wrapper.transform, originalActive = model.activeSelf,
                originalParent = oldParent, originalSibling = sibling, clearanceRadius = local.extents.magnitude + local.center.magnitude + .08f };
            targets.Add(target); world.SetCampaignSharkControlled(model, true);
            ConfigureShark(target, identity, model.name.IndexOf("hammerhead", StringComparison.OrdinalIgnoreCase) >= 0 ? "双髻鲨" : "大白鲨");
            if (state.SharkCaughtFor(identity)) wrapper.SetActive(false);
        }

        void ConfigureShark(CreatureTarget target, int identity, string name)
        {
            target.SharkId = identity; target.SharkLabel = name + " " + (identity + 1);
            target.reactedHits = state.SharkHitsFor(identity);
            EstablishSharkWaterStart(target);
            target.sharkMotion = target.root.gameObject.AddComponent<OceanSharkMotion>();
            target.sharkVisualMotion = new OceanFishMotion(target.Visual, ModelBounds(target.Visual).size.z);
            target.sharkMotion.Configure(identity, target.clearanceRadius,
                (from, to) => CreatureSegmentClear(from, to, target.clearanceRadius, false));
            var panel = new GameObject("Individual shark progress", typeof(RectTransform), typeof(Canvas));
            panel.transform.SetParent(target.root, false);
            target.sharkCanvas = panel.GetComponent<Canvas>(); target.sharkCanvas.renderMode = RenderMode.WorldSpace;
            var rect = panel.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(320f, 64f);
            panel.transform.localScale = Vector3.one * .0026f;
            var textObject = new GameObject("Shark identity and hits", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(panel.transform, false);
            var textRect = textObject.GetComponent<RectTransform>(); textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            target.sharkText = textObject.GetComponent<Text>();
            target.sharkText.font = Resources.Load<Font>("Treasure/TreasureFont"); target.sharkText.fontSize = 24;
            target.sharkText.alignment = TextAnchor.MiddleCenter; target.sharkText.color = new Color(1f,.87f,.44f,1f);
            target.sharkText.raycastTarget = false;
            // No GraphicRaycaster: progress text cannot intercept world/menu rays.
        }

        void TickShark(CreatureTarget target, float dt)
        {
            bool ready = state.SharkHitsFor(target.SharkId) >= state.SharkHitTarget;
            bool aiming = SpearGun != null && SpearGun.Held && SpearGun.ToolTransform != null;
            Ray ray = aiming ? new Ray(SpearGun.ToolTransform.position, SpearGun.ToolTransform.forward) : default;
            target.sharkMotion.Tick(dt, view.transform.position, aiming, ray, ready);
            target.sharkVisualMotion.Tick(dt);
            int hits = state.SharkHitsFor(target.SharkId);
            if (target.shownHits != hits)
            {
                target.shownHits = hits;
                target.sharkText.text = target.SharkLabel + "  " + hits + "/" + state.SharkHitTarget + (ready ? " · 握拳抓取" : "");
            }
            target.sharkCanvas.enabled = (aiming || hits > 0) && Vector3.Distance(view.transform.position, target.root.position) < 20f;
            if (target.sharkCanvas.enabled)
            {
                Bounds bounds = target.Body.bounds;
                target.sharkCanvas.transform.position = bounds.center + Vector3.up * (bounds.extents.y + .35f);
                target.sharkCanvas.transform.rotation = view.transform.rotation;
            }
        }

        static Bounds LocalModelBounds(GameObject model, Transform space)
        {
            Bounds result = default; bool found = false;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                for (int corner = 0; corner < 8; corner++)
                {
                    Bounds bounds = renderer.localBounds;
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    point = space.InverseTransformPoint(renderer.transform.TransformPoint(point));
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; } else result.Encapsulate(point);
                }
            if (!found) throw new InvalidOperationException("Shark has no visible geometry.");
            return result;
        }

        // Called only while ConfigureLevel owns placement under the loading/transition cover.
        // An ambient shark can start inside the campaign's more conservative full-body reef envelope.
        // Every route checks its origin, so accepting that origin made the largest animal hold forever.
        void EstablishSharkWaterStart(CreatureTarget target)
        {
            Vector3 original = target.root.position;
            float radius = target.clearanceRadius;
            if (CreatureSegmentClear(original, original, radius, false)) return;
            // An ambient shark can have travelled beyond the quest's reachable rectangle.
            // Search around a valid centre first; a 6m local ring around a far-out point has no legal candidate.
            Vector3 searchCenter = original, areaCenter = MovementAreaCenter;
            float safeHalfExtent = Mathf.Max(0f, MovementAreaHalfExtent - radius - .10f);
            searchCenter.x = areaCenter.x + Mathf.Clamp(original.x - areaCenter.x, -safeHalfExtent, safeHalfExtent);
            searchCenter.z = areaCenter.z + Mathf.Clamp(original.z - areaCenter.z, -safeHalfExtent, safeHalfExtent);
            for (int ring = 0; ring < 4; ring++)
                for (int sample = 0; sample < (ring == 0 ? 1 : 12); sample++)
                {
                    float angle = sample * Mathf.PI / 6f;
                    Vector3 candidate = searchCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (ring * 2f);
                    if (!WithinMovementArea(candidate, radius)) continue;
                    float floor = UnderwaterWorld.SampleRenderedSeabedHeight(candidate.x, candidate.z);
                    for (int edge = 0; edge < 8; edge++)
                    {
                        float a = edge * Mathf.PI * .25f;
                        floor = Mathf.Max(floor, UnderwaterWorld.SampleRenderedSeabedHeight(
                            candidate.x + Mathf.Cos(a) * radius, candidate.z + Mathf.Sin(a) * radius));
                    }
                    foreach (Bounds reef in reefBounds)
                        if (candidate.x + radius >= reef.min.x && candidate.x - radius <= reef.max.x &&
                            candidate.z + radius >= reef.min.z && candidate.z - radius <= reef.max.z)
                            floor = Mathf.Max(floor, reef.max.y);
                    candidate.y = Mathf.Max(original.y, floor + radius + .45f);
                    // The exact runtime water/physics predicate is the final authority, not the height estimate.
                    if (!CreatureSegmentClear(candidate, candidate, radius, false)) continue;
                    target.root.position = candidate;
                    Debug.Log($"[CampaignShark] start-water id={target.SharkId} from={original:F3} to={candidate:F3} clearance={radius:F3}");
                    return;
                }
            throw new InvalidOperationException("No valid full-body campaign water start for shark " + target.SharkId);
        }

        static float MovementAreaHalfExtent => UnderwaterWorld.RenderedSeabedEnvironment == OceanEnvironmentKind.CoralSanctuary
            ? OceanEnvironmentProfile.SanctuaryPlayableHalfExtent - 4f : 46f;
        static Vector3 MovementAreaCenter => UnderwaterWorld.RenderedSeabedEnvironment == OceanEnvironmentKind.CoralSanctuary
            ? new Vector3(OceanEnvironmentProfile.SpawnX, 0f, OceanEnvironmentProfile.SpawnZ) : Vector3.zero;
        static bool WithinMovementArea(Vector3 point, float radius)
        {
            Vector3 center = MovementAreaCenter;
            float extent = MovementAreaHalfExtent;
            return Mathf.Abs(point.x - center.x) + radius <= extent && Mathf.Abs(point.z - center.z) + radius <= extent;
        }

        bool CreatureSegmentClear(Vector3 from, Vector3 to, float radius, bool keepInSearchArea)
        {
            // Match the current map's swimmer boundary, including the expanded Sanctuary's spawn offset.
            if (!Finite(from) || !Finite(to) || !WithinMovementArea(to, radius)) return false;
            if (keepInSearchArea && Vector3.ProjectOnPlane(to - spawn, Vector3.up).magnitude + radius > state.Definition.SearchRadius + 1f) return false;
            if (UnderwaterWorld.TryIntersectRenderedSeabedSegment(from, to, radius, sharkTerrainStops, out _)) return false;
            Vector3 delta = to - from; float length = delta.magnitude;
            var ray = new Ray(from, length > .00001f ? delta / length : Vector3.forward);
            foreach (Bounds bounds in reefBounds)
            {
                Bounds expanded = bounds; expanded.Expand(radius * 2f);
                if (expanded.Contains(from) || (length > .00001f && expanded.IntersectRay(ray, out float entry) && entry <= length)) return false;
            }
            int count = Physics.OverlapSphereNonAlloc(from, radius, palmObstruction, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == palmObstruction.Length) return false;
            for (int i = 0; i < count; i++)
                if (palmObstruction[i] != null && (hands == null || !palmObstruction[i].transform.IsChildOf(hands.transform))) return false;
            if (length <= .00001f) return true;
            count = Physics.SphereCastNonAlloc(from, radius, ray.direction, obstruction, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == obstruction.Length) return false;
            for (int i = 0; i < count; i++)
                if (obstruction[i].collider != null && (hands == null || !obstruction[i].transform.IsChildOf(hands.transform))) return false;
            return true;
        }
        static Bounds ModelBounds(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("Campaign animal has no visible mesh.");
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
        bool ClearReach(Vector3 palm, Vector3 destination)
        {
            int count = Physics.OverlapSphereNonAlloc(palm, .008f, palmObstruction, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == palmObstruction.Length) return false;
            for (int i = 0; i < count; i++)
                if (palmObstruction[i] != null && (hands == null || !palmObstruction[i].transform.IsChildOf(hands.transform))) return false;
            Vector3 delta = destination - palm;
            if (delta.sqrMagnitude < .00001f) return true;
            count = Physics.RaycastNonAlloc(palm, delta.normalized, obstruction, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == obstruction.Length) return false;
            for (int i = 0; i < count; i++)
                if (obstruction[i].collider != null && (hands == null || !obstruction[i].transform.IsChildOf(hands.transform))) return false;
            return true;
        }
        void Hint(string message, int value)
        {
            if (Time.unscaledTime < hintUntil) return;
            hintUntil = Time.unscaledTime + .9f; notify?.Invoke(message, value);
        }
        internal void ClearForUnavailableLevel() => ClearActors();
        void ClearActors()
        {
            active = false;
            LastInteractedSharkId = -1;
            foreach (CreatureTarget target in targets)
            {
                if (target.IsAmbient && target.IsShark)
                {
                    if (target.Visual != null)
                    {
                        target.sharkVisualMotion?.Release();
                        target.Visual.transform.SetParent(target.originalParent, true);
                        target.Visual.transform.SetSiblingIndex(target.originalSibling);
                        target.Visual.SetActive(target.originalActive);
                        if (world != null) world.SetCampaignSharkControlled(target.Visual, false);
                    }
                    if (target.root != null) { target.root.gameObject.SetActive(false); Destroy(target.root.gameObject); }
                }
                else if (target.IsAmbient)
                {
                    if (target.root != null) target.root.gameObject.SetActive(target.originalActive);
                    if (target.Body != null) { target.Body.enabled = false; Destroy(target.Body.gameObject); }
                }
                else if (target.root != null) { target.root.gameObject.SetActive(false); Destroy(target.root.gameObject); }
            }
            targets.Clear(); shark = null;
            if (SpearGun != null) { SpearGun.gameObject.SetActive(false); Destroy(SpearGun.gameObject); }
            SpearGun = null;
        }
        void OnDisable() { if (SpearGun != null) SpearGun.Drop(false); }
        void OnDestroy() { ClearActors(); }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

    }
}

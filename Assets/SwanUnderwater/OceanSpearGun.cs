using System;
using System.Collections.Generic;
using HandType = SwanUnderwater.OceanHandSide;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>Owned equipment appears for a finger-gun pose; twenty shared durable shots per run.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class OceanSpearGun : MonoBehaviour
    {
        public enum Phase { Grounded, Held, Recoil, Dropping, Hidden }
        public Phase CurrentPhase { get; private set; }
        public Transform ToolTransform => art != null ? art.transform : null;
        public Bounds PickupBounds => BoundsOf(art.gameObject);
        public bool Held => CurrentPhase == Phase.Held || CurrentPhase == Phase.Recoil;
        public bool HeldLeft => Held && heldLeft;
        public bool InFlight => ActiveProjectilesCount > 0;
        public int ActiveProjectilesCount
        {
            get { int count = 0; foreach (var shot in projectiles) if (shot.active) count++; return count; }
        }
        public bool RecoveryPending => state != null && state.RequiresSpearRecovery && state.Current.pendingSpearRecovery;
        public bool CanRecover => RecoveryPending && !InFlight && state.CanCollect && CurrentPhase == Phase.Grounded;
        public bool CanPickup => CurrentPhase == Phase.Grounded && state != null && (!state.Current.toolOwned || CanRecover);
        public const int Capacity = OceanCampaignState.SpearShotLimit;
        public int AmmoRemaining => state != null ? state.SpearAmmoRemaining : 0;
        public int ShotsFired { get; private set; }
        public string LastResolutionReason { get; private set; } = "none";
        public OceanSpearGunVisual Visual => art;
        public Transform ProjectileTransform => projectiles.Count > 0 ? projectiles[projectiles.Count - 1].visual.transform : null;
        public bool Armed => Held && triggerGate.Armed;
        public int LastHitSharkId { get; private set; } = -1;

        readonly OceanSpearTriggerGate triggerGate = new OceanSpearTriggerGate();
        readonly OceanMenuPressGate[] recoveryPinch = { new OceanMenuPressGate(), new OceanMenuPressGate() };
        sealed class Flight
        {
            public GameObject visual;
            public Vector3 velocity;
            public float age, fade, wakeDistance;
            public int number;
            public bool active = true;
        }
        readonly List<Flight> projectiles = new List<Flight>(Capacity * 2);
        readonly RaycastHit[] castHits = new RaycastHit[48];
        readonly Collider[] overlaps = new Collider[32];
        readonly List<Bounds> reefs = new List<Bounds>();
        readonly List<float> terrainStops = new List<float>(48);
        OceanSpearGunVisual art;
        OceanCampaignController campaign;
        OceanCampaignState state;
        Camera view;
        HandSwimController hands;
        readonly Dictionary<Collider, int> sharkBodies = new Dictionary<Collider, int>();
        Func<Vector3, Vector3, bool> clearReach;
        Action<string, int> notify;
        ParticleSystem wake;
        Material wakeMaterial;
        Mesh wakeQuad;
        AudioSource sound;
        AudioClip launchSound, impactSound;
        bool heldLeft;
        float phaseTime;
        double nextInputLog;
        double nextRecoveryPlacement;
        
        
        const float FlightRadius = .035f;

        public static OceanSpearGun Create(Transform parent, Camera camera, HandSwimController controller,
            OceanCampaignState campaignState, Collider sharkBody, Vector3 spawn, IReadOnlyList<Bounds> reefBounds,
            Func<Vector3, Vector3, bool> reachCheck, Action<string, int> callback)
        {
            var root = new GameObject("Campaign - owned gesture speargun"); root.transform.SetParent(parent, false);
            try
            {
                var gun = root.AddComponent<OceanSpearGun>();
                gun.view = camera; gun.hands = controller; gun.state = campaignState;
                if (sharkBody != null) gun.RegisterShark(sharkBody, 0);
                gun.campaign = parent.GetComponent<OceanCampaignController>(); gun.clearReach = reachCheck; gun.notify = callback;
                foreach (Bounds b in reefBounds) gun.reefs.Add(b);
                gun.art = OceanSpearGunVisual.Create(root.transform);
                gun.CreateEffects(); gun.PlaceAtSpawn(spawn);
                gun.CurrentPhase = Phase.Grounded;
                gun.ShotsFired = campaignState.Current.spearShotsFired;
                if (campaignState.Current.toolOwned) gun.Hide();
                gun.FinalizeAmmo();
                if (gun.RecoveryPending && gun.state.CanCollect) gun.ShowRecovery();
                return gun;
            }
            catch { root.SetActive(false); Destroy(root); throw; }
        }

        public void RegisterShark(Collider body, int identity)
        {
            if (body == null || identity < 0 || identity >= OceanCampaignState.MaximumSharks) throw new ArgumentException("Invalid shark hit volume.");
            if (sharkBodies.ContainsValue(identity)) throw new InvalidOperationException("Duplicate shark identity.");
            sharkBodies.Add(body, identity);
        }
        bool IsShark(Collider body) => body != null && body.enabled && body.gameObject.activeInHierarchy && sharkBodies.ContainsKey(body);

        void LateUpdate()
        {
#if UNITY_EDITOR
            if (validationDriven) return;
#endif
            bool playing = campaign != null && campaign.GameplayAllowed;
            float dt = Time.unscaledDeltaTime;
            if (!playing)
            {
                if (Held) Drop(false);
                ResetRecoveryPinch();
                if (wake != null) wake.Pause();
                return;
            }
            if (!Finite(dt) || dt <= 0f || dt > .10f)
            {
                // A hitch must not manufacture a trigger pull.
                if (Held) triggerGate.Suspend();
                ResetRecoveryPinch();
                return;
            }
            if (wake != null && wake.isPaused) wake.Play();
            if (RecoveryPending)
            {
                if (Held) Hide();
                if (!InFlight && state.CanCollect)
                {
                    if (CurrentPhase != Phase.Grounded && Time.unscaledTimeAsDouble >= nextRecoveryPlacement)
                    { nextRecoveryPlacement = Time.unscaledTimeAsDouble + .1; ShowRecovery(); }
                    TickRecoveryPinch(dt);
                }
                if (hands != null) hands.SetSpearAimSuppressed(false, false);
            }
            else if (state.Current.toolOwned)
            {
                // Keep the equipped side while its pose is valid; the other hand can equip
                // as soon as this one relaxes. Every change requires a fresh trigger release.
                bool preferredLeft = Held ? heldLeft : true;
                if (!ReadPose(preferredLeft, dt) && !ReadPose(!preferredLeft, dt)) Hide();
                if (hands != null) hands.SetSpearAimSuppressed(Held && heldLeft, Held && !heldLeft);
            }
            TickMotion(dt);
        }

        bool ReadPose(bool left, float dt)
        {
            HandType side = left ? HandType.HandLeft : HandType.HandRight;
            if (hands == null || !hands.TryGetPalmPosition(side, out Vector3 palm) ||
                !hands.TryGetSpearPose(side, out bool grip, out bool thumb, out float curl,
                    out Vector3 aim, out bool extended)) return false;
            bool existing = Held && heldLeft == left;
            if (!grip || !thumb || !extended || (!existing && curl > OceanSpearTriggerGate.ReleaseCurl)) return false;
            ProcessPose(palm, aim, left, grip, thumb, extended, curl, dt, true, Time.frameCount);
            if (Time.unscaledTimeAsDouble >= nextInputLog)
            {
                nextInputLog = Time.unscaledTimeAsDouble + 1.0;
                Debug.Log($"[SpearTrigger] hand={(left ? "left" : "right")} grip={grip} thumb={thumb} extended={extended} curl={curl:F3} armed={Armed} ammo={AmmoRemaining}");
            }
            return Held;
        }

        public bool TryPickup(Vector3 palm, bool left)
        {
            if (!CanPickup || !state.CanCollect || !Finite(palm)) return false;
            Bounds bounds = PickupBounds;
            if (Vector3.Distance(palm, bounds.ClosestPoint(palm)) > OceanCampaignCreatures.ToolReach ||
                !clearReach(palm, bounds.center)) return false;
            if (CanRecover) return RecoverSpear(left);
            if (!state.AcquireTool()) return false;
            Hide();
            notify?.Invoke("tool", 0);
            Debug.Log($"[SpearGun] acquired hand={(left ? "left" : "right")} hits={state.Current.sharkHits} ammo={AmmoRemaining}");
            return true;
        }

        void ProcessPose(Vector3 palm, Vector3 aim, bool left, bool gripping, bool thumbExtended,
            bool handExtended, float indexCurl, float dt, bool fresh, int frame)
        {
            if (RecoveryPending) { if (Held) Hide(); return; }
            if (state == null || !state.CanFireSpear || !fresh || !gripping || !thumbExtended || !handExtended ||
                !Finite(palm) || !Finite(aim) || aim.sqrMagnitude < .5f || !Finite(indexCurl))
            { Hide(); return; }
            if (!Held || heldLeft != left)
            {
                Hide();
                if (indexCurl > OceanSpearTriggerGate.ReleaseCurl) return;
                heldLeft = left; CurrentPhase = Phase.Held; phaseTime = 0f;
                triggerGate.Acquire(left);
                art.gameObject.SetActive(true); art.SetLoaded(true); art.SetRecoil(0f);
                art.transform.SetPositionAndRotation(palm, Quaternion.LookRotation(aim, Vector3.up));
                Debug.Log($"[SpearGun] equipped hand={(left ? "left" : "right")} ammo={AmmoRemaining}");
            }
            art.transform.position = Vector3.Lerp(art.transform.position, palm, 1f - Mathf.Exp(-dt * 40f));
            art.transform.rotation = Quaternion.Slerp(art.transform.rotation, Quaternion.LookRotation(aim, Vector3.up), 1f - Mathf.Exp(-dt * 32f));
            if (triggerGate.Step(frame, dt, left, true, true, indexCurl)) Fire(palm, art.transform.forward);
        }

        void Hide()
        {
            CurrentPhase = Phase.Hidden; phaseTime = 0f; triggerGate.Reset();
            if (art != null) { art.SetRecoil(0f); art.gameObject.SetActive(false); }
            if (hands != null) hands.SetSpearAimSuppressed(false, false);
        }

        void FinalizeAmmo()
        {
            if (state != null && state.FinalizeSpearAmmo(InFlight))
            { Hide(); notify?.Invoke("ammo-exhausted", 0); }
        }

        void Fire(Vector3 palm, Vector3 direction)
        {
            if (!Held || !state.TryFireSpear()) return;
            CurrentPhase = Phase.Recoil; phaseTime = 0f; ShotsFired++;
            LastResolutionReason = "in-flight";
            art.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            art.SetLoaded(false); art.SetRecoil(0f);
            var shot = new Flight { visual = art.CreateProjectile(transform), velocity = direction * 11f, number = ShotsFired };
            shot.visual.transform.SetPositionAndRotation(art.Muzzle.position, Quaternion.LookRotation(direction, Vector3.up));
            projectiles.Add(shot);
            sound.transform.position = art.Muzzle.position;
            sound.PlayOneShot(launchSound, .65f); EmitWake(art.Muzzle.position, -direction * .25f, 8, .10f);
            Debug.Log($"[SpearGun] fire shot={ShotsFired} hand={(heldLeft ? "left" : "right")} ammo={AmmoRemaining} direction={direction:F3}");
            notify?.Invoke("tool-fired", AmmoRemaining);
            // The loaded shaft can extend beyond a nearby collider. Sweep from the real grip too.
            Physics.SyncTransforms();
            TraceSegment(shot, palm, shot.visual.transform.position);
        }

        void TickMotion(float dt)
        {
            if (CurrentPhase == Phase.Recoil)
            {
                phaseTime += dt;
                float t = Mathf.Clamp01(phaseTime / .26f);
                art.SetRecoil(Mathf.Sin(t * Mathf.PI));
                if (t >= 1f)
                {
                    if (AmmoRemaining == 0 || RecoveryPending) Hide();
                    else { CurrentPhase = Phase.Held; art.SetRecoil(0f); art.SetLoaded(true); }
                }
            }
            // Creature transforms move in LateUpdate; the project deliberately disables auto sync.
            if (InFlight) Physics.SyncTransforms();
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                Flight shot = projectiles[i];
                if (shot.active)
                {
                    shot.age += dt;
                    Vector3 from = shot.visual.transform.position;
                    shot.velocity += Vector3.down * (.35f * dt);
                    Vector3 to = from + shot.velocity * dt;
                    shot.visual.transform.rotation = Quaternion.LookRotation(shot.velocity.normalized, Vector3.up);
                    if (!TraceSegment(shot, from, to)) shot.visual.transform.position = to;
                    float length = Vector3.Distance(from, shot.visual.transform.position);
                    shot.wakeDistance += length;
                    int bubbles = Mathf.Min(8, Mathf.FloorToInt(shot.wakeDistance / .16f));
                    if (bubbles > 0)
                    {
                        shot.wakeDistance -= bubbles * .16f;
                        for (int b = 0; b < bubbles; b++) EmitWake(Vector3.Lerp(from, shot.visual.transform.position, (b + 1f) / bubbles), Vector3.up * .08f, 1, .065f);
                    }
                    if (shot.age >= 2.3f && shot.active) Resolve(shot, null, shot.visual.transform.position, "range");
                }
                else
                {
                    shot.fade -= dt;
                    if (shot.fade <= 0f) { shot.visual.SetActive(false); Destroy(shot.visual); projectiles.RemoveAt(i); }
                }
            }
            FinalizeAmmo();
        }

        // Nearest obstruction wins. An overlap is not repeatedly scored; the complete shot is resolved once.
        bool TraceSegment(Flight shot, Vector3 from, Vector3 to)
        {
            if (!shot.active) return false;
            Vector3 delta = to - from; float length = delta.magnitude;
            float nearest = float.PositiveInfinity; Collider hitShark = null; string reason = "obstruction";
            // Reef physics uses inexpensive small spheres. Its visible silhouette is blocked conservatively here.
            var ray = new Ray(from, length > .00001f ? delta / length : Vector3.forward);
            foreach (Bounds reef in reefs)
            {
                Bounds expanded = reef; expanded.Expand(FlightRadius * 2f);
                if (expanded.Contains(from)) nearest = 0f;
                else if (length > .00001f && expanded.IntersectRay(ray, out float entry) && entry <= length)
                    nearest = Mathf.Min(nearest, entry);
            }
            if (nearest < float.PositiveInfinity) reason = "reef";
            if (UnderwaterWorld.TryIntersectRenderedSeabedSegment(from, to, FlightRadius, terrainStops, out float groundHit) && groundHit <= nearest)
            { nearest = groundHit; reason = "floor"; }
            int count = Physics.OverlapSphereNonAlloc(from, FlightRadius, overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            if (count == overlaps.Length) { Resolve(shot, null, from, "overlap-capacity"); return true; }
            Collider startedAtShark = null;
            for (int i = 0; i < count; i++)
            {
                Collider c = overlaps[i];
                if (IsShark(c)) startedAtShark = c;
                else if (IsObstacle(c)) { Resolve(shot, null, from, "obstruction"); return true; }
            }
            if (nearest <= 0f) { Resolve(shot, null, from, reason); return true; }
            if (startedAtShark != null) { Resolve(shot, startedAtShark, from, "shark"); return true; }
            if (length < .00001f) return false;
            count = Physics.SphereCastNonAlloc(from, FlightRadius, delta / length, castHits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            if (count == castHits.Length) { Resolve(shot, null, from, "cast-capacity"); return true; }
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = castHits[i];
                if (!IsShark(hit.collider) && !IsObstacle(hit.collider)) continue;
                if (hit.distance > nearest) continue;
                // At equal distance prefer the blocking environment.
                if (hit.distance == nearest && IsShark(hit.collider)) continue;
                nearest = hit.distance; hitShark = IsShark(hit.collider) ? hit.collider : null;
                reason = hitShark != null ? "shark" : "obstruction";
            }
            if (nearest < float.PositiveInfinity)
            { Resolve(shot, hitShark, from + delta.normalized * nearest, reason); return true; }
            return false;
        }

        bool IsObstacle(Collider c) => c != null && !c.isTrigger && !c.transform.IsChildOf(transform) &&
            (hands == null || !c.transform.IsChildOf(hands.transform));

        void Resolve(Flight shot, Collider hitShark, Vector3 point, string reason)
        {
            if (!shot.active) return;
            shot.active = false; shot.fade = .55f;
            LastResolutionReason = reason;
            shot.visual.transform.position = point;
            int identity = hitShark != null && sharkBodies.TryGetValue(hitShark, out int id) ? id : -1;
            bool scored = identity >= 0 && state.HitShark(identity);
            if (scored) LastHitSharkId = identity;
            if (scored && campaign != null && campaign.Creatures != null) campaign.Creatures.NotifySharkHit(identity, shot.velocity.normalized);
            EmitWake(point, Vector3.up * .16f, scored ? 18 : 7, scored ? .20f : .11f);
            if (scored) { sound.transform.position = point; sound.PlayOneShot(impactSound, .7f); notify?.Invoke("shark-hit", state.SharkHitsFor(identity)); }
            else notify?.Invoke("tool-miss", 0);
            Debug.Log($"[SpearGun] resolved shot={shot.number} reason={reason} scored={scored} shark={identity} hits={(identity >= 0 ? state.SharkHitsFor(identity) : 0)} best={state.Current.sharkHits}");
            FinalizeAmmo();
            if (RecoveryPending && !InFlight && state.CanCollect) ShowRecovery();
        }

        void ResetRecoveryPinch()
        { for (int i = 0; i < recoveryPinch.Length; i++) recoveryPinch[i].Reset(); }

        // The tether returns the same owned visual near the seated player. The resolved shot
        // remains authoritative and is never replayed; retrieval only clears the durable latch.
        void ShowRecovery()
        {
            if (!RecoveryPending || InFlight || !state.CanCollect) return;
            Hide(); art.gameObject.SetActive(true); art.SetLoaded(true);
            Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .5f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            for (int i = 0; i < 12; i++)
            {
                float side = i % 2 == 0 ? -1 : 1;
                Vector3 candidate = view.transform.position + forward * (.65f + (i / 4) * .16f) + right * side * (.22f + (i % 4) * .05f) - Vector3.up * .27f;
                candidate.y = Mathf.Max(candidate.y, UnderwaterWorld.SampleRenderedSeabedHeight(candidate.x, candidate.z) + .25f);
                art.transform.SetPositionAndRotation(candidate, Quaternion.LookRotation(right, Vector3.up));
                if (!Blocked(PickupBounds) && clearReach(view.transform.position, PickupBounds.center))
                {
                    CurrentPhase = Phase.Grounded; ResetRecoveryPinch();
                    notify?.Invoke("tool-recovery", AmmoRemaining); return;
                }
            }
            // No clear near-hand return exists yet. Keep it hidden and retry after the
            // player moves, rather than teleporting it through a wall or minting ammunition.
            art.gameObject.SetActive(false); CurrentPhase = Phase.Hidden;
            ResetRecoveryPinch();
        }
        bool RecoverSpear(bool left)
        {
            if (!CanRecover || !state.TryRecoverSpear()) return false;
            Hide(); ResetRecoveryPinch();
            notify?.Invoke("tool-recovered", AmmoRemaining);
            Debug.Log($"[SpearGun] recovered hand={(left ? "left" : "right")} shots={state.Current.spearShotsFired} ammo={AmmoRemaining}");
            return true;
        }
        void TickRecoveryPinch(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                Ray ray = default; float strength = 0;
                bool fresh = hands != null && hands.TryGetMenuHandRay((HandType)i, out ray, out strength);
                if (RecoveryPointer(i, Time.frameCount, dt, ray, strength, fresh)) return;
            }
        }
        bool RecoveryPointer(int side, int frame, float dt, Ray ray, float strength, bool fresh)
        {
            Bounds bounds = default; float distance = 0;
            bool hit = fresh && CanRecover && Finite(ray.origin) && Finite(ray.direction) && ray.direction.sqrMagnitude > .01f;
            if (hit)
            {
                bounds = PickupBounds; bounds.Expand(.10f);
                hit = bounds.IntersectRay(ray, out distance) && distance >= 0 && distance <= 3f &&
                    clearReach(ray.origin, bounds.center);
            }
            int pressed = recoveryPinch[side].Step(frame, dt, hit, strength, hit ? 1 : -1, true);
            return pressed >= 0 && RecoverSpear(side == 0);
        }

        public void Drop(bool feedback) { if (Held) Hide(); }

        void PlaceAtSpawn(Vector3 spawn)
        {
            Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .5f) forward = Vector3.forward;
            for (int i = 0; i < 100; i++)
            {
                Vector3 candidate = spawn + Quaternion.AngleAxis(i * 31f, Vector3.up) * (forward * (1.55f + i * .05f) + Vector3.Cross(Vector3.up, forward) * .65f);
                candidate.y = UnderwaterWorld.SampleRenderedSeabedHeight(candidate.x, candidate.z) + .12f;
                art.transform.SetPositionAndRotation(candidate, Quaternion.Euler(0, 25f, 90f)); PutOnFloor();
                if (Blocked(PickupBounds)) continue;
                return;
            }
            throw new InvalidOperationException("No clear ground position for the recoverable speargun.");
        }
        bool Blocked(Bounds bounds)
        {
            foreach (Bounds reef in reefs) if (reef.Intersects(bounds)) return true;
            // Check above the terrain itself; the world has no loose gameplay solids beneath the gun.
            return Physics.CheckBox(bounds.center + Vector3.up * .025f, bounds.extents * .85f, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
        void PutOnFloor()
        { Bounds b = PickupBounds; art.transform.position += Vector3.up * (GroundHeight(b) - b.min.y); }
        static float GroundHeight(Bounds b)
        {
            float highest = float.NegativeInfinity;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                highest = Mathf.Max(highest, UnderwaterWorld.SampleRenderedSeabedHeight(b.center.x + b.extents.x * x, b.center.z + b.extents.z * z));
            return highest + .006f;
        }
        static Bounds BoundsOf(GameObject obj)
        {
            // A freshly-created procedural renderer can still have stale world bounds before its first render.
            // Ground placement must be deterministic before that frame, using the actual authored geometry.
            var filters = obj.GetComponentsInChildren<MeshFilter>(false);
            Bounds b = default; bool found = false;
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                Bounds local = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = filter.transform.TransformPoint(corner);
                    if (!found) { b = new Bounds(point, Vector3.zero); found = true; } else b.Encapsulate(point);
                }
            }
            if (!found) throw new InvalidOperationException("Speargun has no geometry for placement.");
            return b;
        }

        void CreateEffects()
        {
            Shader shader = Shader.Find("SwanUnderwater/HandWake");
            if (shader == null) throw new InvalidOperationException("Missing speargun water wake shader.");
            wakeMaterial = new Material(shader) { name = "Spear bubble rings" };
            wakeQuad = new Mesh { name = "Spear wake quad" };
            wakeQuad.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(-.5f,.5f,0), new Vector3(.5f,.5f,0) };
            wakeQuad.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one }; wakeQuad.triangles = new[] { 0,2,1,2,3,1 }; wakeQuad.RecalculateBounds();
            var go = new GameObject("Speargun water trail"); go.transform.SetParent(transform, false);
            wake = go.AddComponent<ParticleSystem>(); wake.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = wake.main; main.loop = true; main.playOnAwake = false; main.maxParticles = 72;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = .8f; main.startSpeed = 0; main.startSize = .08f;
            var emission = wake.emission; emission.enabled = false; var shape = wake.shape; shape.enabled = false;
            var size = wake.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0,.3f,1,1.6f));
            var colors = wake.colorOverLifetime; colors.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white,0), new GradientColorKey(Color.white,1) },
                new[] { new GradientAlphaKey(.85f,0), new GradientAlphaKey(.55f,.4f), new GradientAlphaKey(0,1) }); colors.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = wakeQuad;
            renderer.alignment = ParticleSystemRenderSpace.Facing; renderer.sharedMaterial = wakeMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
            wake.Play();
            var audioRoot = new GameObject("Speargun spatial sound"); audioRoot.transform.SetParent(transform, false);
            sound = audioRoot.AddComponent<AudioSource>(); sound.playOnAwake = false; sound.spatialBlend = .45f; sound.minDistance = 1f; sound.maxDistance = 16f;
            OceanAudioPreferences.Register(sound, OceanAudioCategory.Feedback);
            launchSound = MakeSound(false); impactSound = MakeSound(true);
        }
        void EmitWake(Vector3 point, Vector3 velocity, int count, float size)
        {
            for (int i = 0; i < count; i++)
            {
                float y = 1f - 2f * (i + .5f) / count, a = i * 2.399963f;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                Vector3 spread = count > 1 ? new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r) : Vector3.zero;
                var e = new ParticleSystem.EmitParams { position = point + spread * .025f, velocity = velocity + spread * .22f,
                    startSize = size * (.7f + .3f * ((i * 7 % 11) / 10f)), startLifetime = .65f + (i % 4) * .10f,
                    startColor = new Color(.55f,.91f,1f,.75f), applyShapeToPosition = false };
                wake.Emit(e, 1);
            }
        }
        static AudioClip MakeSound(bool impact)
        {
            const int rate = 22050; int count = (int)(rate * (impact ? .28f : .32f)); var samples = new float[count]; uint rng = 18463;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate; rng = rng * 1664525u + 1013904223u;
                float noise = ((rng >> 8) / 8388607.5f) - 1f;
                float envelope = Mathf.Exp(-t * (impact ? 16f : 11f)) * Mathf.Min(1f,t * 500f);
                float tone = Mathf.Sin(2f * Mathf.PI * (impact ? 310f : 180f) * t * Mathf.Exp(-t * 3f));
                samples[i] = envelope * (tone * .28f + noise * (impact ? .16f : .12f));
            }
            var clip = AudioClip.Create(impact ? "Spear underwater impact" : "Speargun elastic release", count, 1, rate, false); clip.SetData(samples, 0); return clip;
        }
        void OnDisable() { if (Held) Drop(false); triggerGate.Reset(); ResetRecoveryPinch(); if (wake != null) wake.Clear(); }
        void OnDestroy()
        {
            foreach (var shot in projectiles) if (shot.visual != null) Destroy(shot.visual);
            projectiles.Clear();
            if (wakeMaterial != null) Destroy(wakeMaterial); if (wakeQuad != null) Destroy(wakeQuad);
            if (launchSound != null) Destroy(launchSound); if (impactSound != null) Destroy(impactSound);
        }
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
#if UNITY_EDITOR
        bool validationDriven;
        public void DrivePoseForValidation(Vector3 palm, Vector3 aim, bool left, bool gripping,
            bool thumbExtended, bool handExtended, float indexCurl, float dt, bool fresh, int frame)
        {
            validationDriven = true;
            if (!Finite(dt) || dt <= 0f || dt > .10f) { triggerGate.Suspend(); ResetRecoveryPinch(); return; }
            ProcessPose(palm, aim, left, gripping, thumbExtended, handExtended, indexCurl, dt, fresh, frame);
        }
        public void DriveHeldForValidation(Vector3 palm, Vector3 head, Vector3 forward, bool gripping,
            float indexCurl, float dt, bool fresh, int frame, bool indexFresh = true)
        { DrivePoseForValidation(palm, forward, heldLeft, gripping, true, true, indexCurl, dt, fresh && indexFresh, frame); }
        public void TickForValidation(float dt, bool playing)
        {
            validationDriven = true;
            if (!playing) { if (Held) Drop(false); ResetRecoveryPinch(); return; }
            if (!Finite(dt) || dt <= 0f || dt > .10f) { if (Held) triggerGate.Suspend(); ResetRecoveryPinch(); return; }
            TickMotion(dt);
        }
        public bool RecoveryPointerForValidation(int side, int frame, float dt, Ray ray, float pinch, bool fresh)
        { validationDriven = true; return RecoveryPointer(side, frame, dt, ray, pinch, fresh); }
#endif
    }
}

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual level-20 campaign sharks and production scene clearance. Synthetic time/hits, no tracked-hand claim.</summary>
    public static class OceanSharkSceneMotionTests
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        [Serializable] public sealed class SharkSample
        {
            public int id;
            public string name, initialState, afterCruiseState, afterHitState;
            public bool ambient, startClear, captureReadyAfterLastShot, capturedAfterLastShot;
            public float clearance, cruiseTravel, longestHold, escapeTravel, escapeNet, escapeSideways, maxStep, minForwardDot = 1f;
            public Vector3 start, afterCruise, afterEscape;
        }
        sealed class Saved
        {
            public OceanCampaignCreatures.CreatureTarget target;
            public OceanSharkMotion motion;
            public FieldInfo[] fields;
            public object[] values;
            public Vector3 position;
            public Quaternion rotation;
            public bool rootActive, visualActive;
            public int reactedHits, shownHits;
            public Transform canvas;
            public Vector3 canvasLocalPosition;
            public Quaternion canvasLocalRotation;
        }
        public static int LastAssertionCount { get; private set; }
        public static readonly List<SharkSample> Samples = new List<SharkSample>();

        public static void RunInScene(UnderwaterWorld world, Camera view)
        {
            LastAssertionCount = 0; Samples.Clear();
            var hunt = UnityEngine.Object.FindFirstObjectByType<TreasureHunt>();
            if (world == null || view == null || hunt == null || hunt.Campaign == null || hunt.Campaign.State.Current.level != 20)
                throw new InvalidOperationException("Load the actual production level 20 before campaign shark scene checks.");
            OceanCampaignCreatures actors = hunt.Campaign.Creatures;
            OceanCampaignState state = hunt.Campaign.State;
            string savedState = JsonUtility.ToJson(state.Save);
            var saved = new List<Saved>(); var failures = new List<string>();
            var clear = typeof(OceanCampaignCreatures).GetMethod("CreatureSegmentClear", Fields);
            var tick = typeof(OceanCampaignCreatures).GetMethod("TickShark", Fields);
            object callback = Get(actors, "notify"), weapon = Get(actors, "<SpearGun>k__BackingField"), wasActive = Get(actors, "active"),
                lastInteracted = Get(actors, "<LastInteractedSharkId>k__BackingField");
            // Keep this synthetic transaction inside the isolated test: no save callback, reward,
            // weapon drop or menu side effects. Steering/clearance/hit/catch methods remain production code.
            Set(actors, "notify", null); Set(actors, "<SpearGun>k__BackingField", null); Set(actors, "active", true);
            try
            {
                foreach (var target in actors.Targets)
                {
                    if (!target.IsShark || target.Body == null || !target.Body.gameObject.activeInHierarchy) continue;
                    var motion = (OceanSharkMotion)Get(target, "sharkMotion");
                    var fields = typeof(OceanSharkMotion).GetFields(Fields);
                    var row = new Saved { target = target, motion = motion, fields = fields, values = new object[fields.Length],
                        position = target.Body.transform.position, rotation = target.Body.transform.rotation,
                        rootActive = target.Body.gameObject.activeSelf, visualActive = target.Visual.activeSelf,
                        reactedHits = (int)Get(target, "reactedHits"), shownHits = (int)Get(target, "shownHits") };
                    for (int i = 0; i < fields.Length; i++) row.values[i] = fields[i].GetValue(motion);
                    row.canvas = ((Canvas)Get(target, "sharkCanvas")).transform;
                    row.canvasLocalPosition = row.canvas.localPosition; row.canvasLocalRotation = row.canvas.localRotation;
                    saved.Add(row);
                }
                Require(saved.Count >= 3, "actual dedicated, large ambient and small ambient shark are all present", failures);
                foreach (Saved row in saved)
                {
                    foreach (Saved other in saved) Restore(other);
                    state.StartLevel(20); state.AcquireTool();
                    Set(row.target, "reactedHits", 0);
                    object[] tickArgs = { row.target, .02f };
                    var t = row.target.Body.transform;
                    float radius = (float)Get(row.target, "clearanceRadius");
                    var sample = new SharkSample { id = row.target.SharkId, name = row.target.Visual.name, ambient = row.target.IsAmbient,
                        clearance = radius, start = t.position, initialState = row.motion.MotionState,
                        startClear = (bool)clear.Invoke(actors, new object[] { t.position, t.position, radius, false }) };
                    Samples.Add(sample);
                    Require(sample.startClear, "legal campaign full-body start id=" + sample.id, failures);
                    float holding = 0f;
                    for (int i = 0; i < 500; i++)
                    {
                        Vector3 before = t.position;
                        tick.Invoke(actors, tickArgs);
                        float distance = Vector3.Distance(before, t.position);
                        sample.cruiseTravel += distance;
                        holding = distance < .0001f ? holding + .02f : 0f;
                        sample.longestHold = Mathf.Max(sample.longestHold, holding);
                        CheckStep(t, before, sample, failures);
                    }
                    sample.afterCruise = t.position; sample.afterCruiseState = row.motion.MotionState;
                    Require(sample.cruiseTravel > .8f && sample.longestHold < 3f,
                        "each shark continuously swims in real level-20 water id=" + sample.id + " travel=" + sample.cruiseTravel + " hold=" + sample.longestHold, failures);
                    Require(state.TryFireSpear() && state.HitShark(sample.id), "production first spent shot id=" + sample.id, failures);
                    foreach (Saved other in saved)
                        if (other != row) Require(state.SharkHitsFor(other.target.SharkId) == 0, "per-animal hit isolation", failures);
                    Vector3 hit = t.position;
                    Vector3 away = Vector3.ProjectOnPlane(hit - view.transform.position, Vector3.up).normalized;
                    if (away.sqrMagnitude < .001f) away = Vector3.forward;
                    Vector3 side = Vector3.Cross(Vector3.up, away);
                    actors.NotifySharkHit(sample.id, away);
                    Require(Vector3.Distance(hit, t.position) < .000001f, "hit does not teleport", failures);
                    for (int i = 0; i < 300 && row.motion.Escaping; i++)
                    {
                        Vector3 before = t.position;
                        tick.Invoke(actors, tickArgs);
                        sample.escapeTravel += Vector3.Distance(before, t.position);
                        sample.escapeSideways = Mathf.Max(sample.escapeSideways, Mathf.Abs(Vector3.Dot(t.position - hit, side)));
                        CheckStep(t, before, sample, failures);
                    }
                    sample.afterEscape = t.position; sample.escapeNet = Vector3.Distance(hit, t.position); sample.afterHitState = row.motion.MotionState;
                    Require(sample.escapeTravel > 1.2f && sample.escapeNet > .65f, "real hit produces a visible retreat id=" + sample.id, failures);
                    Require(sample.escapeSideways > .30f, "retreat includes an actual lateral break id=" + sample.id, failures);

                    // Six earlier misses plus thirteen accepted hits: the twentieth and final shot
                    // earns the capture. Run the exact public shot/hit path rather than granting ammo.
                    state.Current.sharkProgress.Clear(); state.Current.sharkProgress.Add(new OceanSharkProgress(sample.id, 13));
                    state.Current.sharkHits = 13; state.Current.spearShotsFired = 19;
                    Set(row.target, "reactedHits", 13);
                    Require(state.TryFireSpear() && state.HitShark(sample.id), "last ammo earns fourteenth individual hit", failures);
                    actors.NotifySharkHit(sample.id, away);
                    for (int i = 0; i < 100; i++) tick.Invoke(actors, tickArgs);
                    sample.captureReadyAfterLastShot = !state.Failed && row.motion.MotionState == "capture" && row.motion.Speed == 0f;
                    Require(sample.captureReadyAfterLastShot, "last shot retains capture opportunity id=" + sample.id, failures);
                    Physics.SyncTransforms();
                    sample.capturedAfterLastShot = actors.TryGrasp(row.target.WorldBounds.center, true) && state.SharkCaughtFor(sample.id);
                    Require(sample.capturedAfterLastShot, "production reach/catch accepts earned individual id=" + sample.id, failures);
                    // Restore this shark before the next identity's synthetic sequence.
                    Restore(row); Physics.SyncTransforms();
                    Debug.Log("[SharkSceneMotion] " + JsonUtility.ToJson(sample));
                }
                if (failures.Count != 0) throw new InvalidOperationException("Campaign shark scene failures: " + string.Join("; ", failures));
                Debug.Log("SHARK_SCENE_MOTION_TESTS_PASSED assertions=" + LastAssertionCount + " sharks=" + Samples.Count + "; production level-20 actors, geometry and shot/catch transactions; synthetic time/input, not wearer acceptance");
            }
            finally
            {
                foreach (Saved row in saved) Restore(row);
                JsonUtility.FromJsonOverwrite(savedState, state.Save);
                Set(actors, "notify", callback); Set(actors, "<SpearGun>k__BackingField", weapon); Set(actors, "active", wasActive);
                Set(actors, "<LastInteractedSharkId>k__BackingField", lastInteracted);
                Physics.SyncTransforms();
            }
        }
        public static SharkSample UnsafeClaimSample { get; private set; }
        public static Vector3 UnsafeClaimOriginalCenter { get; private set; }

        /// <summary>
        /// Reproduce a returned ambient body inside an actual reef envelope, without touching
        /// the live campaign actors. The source shark mesh is real, 5m, and the owner remains inactive.
        /// Compatible with the 2.8.9 AddAmbientShark API so the same fixture can prove the negative baseline.
        /// </summary>
        public static void RunUnsafeClaimFixture(UnderwaterWorld world, Camera view) => RunIsolatedClaimFixture(world, view, false);

        /// <summary>A cached ambient shark may be far beyond the quest rectangle before a later shark level takes ownership.</summary>
        public static void RunOutOfBoundsClaimFixture(UnderwaterWorld world, Camera view) => RunIsolatedClaimFixture(world, view, true);

        static void RunIsolatedClaimFixture(UnderwaterWorld world, Camera view, bool outsideMovementArea)
        {
            if (world == null || view == null) throw new ArgumentException("Actual world and view required.");
            var owner = new GameObject("Isolated real-shark unsafe claim fixture"); owner.SetActive(false);
            // The controller is inactive. The isolated model holder stays active so the original
            // 2.8.9 GetComponentsInChildren<Renderer>() contract can measure its imported child mesh.
            var modelHolder = new GameObject("Isolated real-shark fixture model holder");
            var actors = owner.AddComponent<OceanCampaignCreatures>();
            var state = new OceanCampaignState(); state.StartLevel(20);
            actors.Initialize(world, view, null, state, null);
            var failures = new List<string>();
            var clear = typeof(OceanCampaignCreatures).GetMethod("CreatureSegmentClear", Fields);
            var tick = typeof(OceanCampaignCreatures).GetMethod("TickShark", Fields);
            try
            {
                typeof(OceanCampaignCreatures).GetMethod("CacheReefBounds", Fields).Invoke(actors, null);
                var reef = (List<Bounds>)Get(actors, "reefBounds");
                Bounds chosen = default; float best = float.NegativeInfinity;
                foreach (Bounds bounds in reef)
                {
                    // A central production reef avoids the old +/-46m boundary being a second cause.
                    if (Mathf.Abs(bounds.center.x) > 30f || Mathf.Abs(bounds.center.z) > 30f || bounds.size.sqrMagnitude < .5f) continue;
                    float score = bounds.size.x * bounds.size.y * bounds.size.z;
                    if (score > best) { best = score; chosen = bounds; }
                }
                if (float.IsNegativeInfinity(best)) throw new InvalidOperationException("Actual central reef geometry required for unsafe claim fixture.");
                GameObject model = world.CreateCampaignCreature(OceanCampaignCreatures.SharkResourcePath, modelHolder.transform, 5f);
                model.name = "Fixture real 5m 15_great_white_shark" + (outsideMovementArea ? " outside movement area" : " inside actual reef");
                model.transform.rotation = Quaternion.identity;
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException("The source shark mesh is absent.");
                Bounds visual = renderers[0].bounds;
                foreach (Renderer renderer in renderers) visual.Encapsulate(renderer.bounds);
                Vector3 originalCenter = chosen.center;
                if (outsideMovementArea)
                {
                    bool sanctuary = UnderwaterWorld.RenderedSeabedEnvironment == OceanEnvironmentKind.CoralSanctuary;
                    Vector3 mapCenter = sanctuary ? new Vector3(OceanEnvironmentProfile.SpawnX, 0f, OceanEnvironmentProfile.SpawnZ) : Vector3.zero;
                    float halfExtent = sanctuary ? OceanEnvironmentProfile.SanctuaryPlayableHalfExtent - 4f : 46f;
                    originalCenter.x = mapCenter.x + halfExtent + 30f;
                    originalCenter.z = mapCenter.z - halfExtent - 30f;
                }
                model.transform.position += originalCenter - visual.center;
                UnsafeClaimOriginalCenter = originalCenter;
                typeof(OceanCampaignCreatures).GetMethod("AddAmbientShark", Fields).Invoke(actors, new object[] { model, 29 });
                if (actors.Targets.Count != 1) throw new InvalidOperationException("Fixture must own only its cloned source shark.");
                var target = actors.Targets[0]; var motion = (OceanSharkMotion)Get(target, "sharkMotion");
                Transform body = target.Body.transform; float radius = (float)Get(target, "clearanceRadius");
                bool originalClear = (bool)clear.Invoke(actors, new object[] { originalCenter, originalCenter, radius, false });
                var sample = new SharkSample { id = target.SharkId, name = model.name, ambient = true, clearance = radius,
                    start = body.position, initialState = motion.MotionState,
                    startClear = (bool)clear.Invoke(actors, new object[] { body.position, body.position, radius, false }) };
                UnsafeClaimSample = sample;
                Require(!originalClear, "fixture original imported body is outside its production water contract", failures);
                Require(sample.startClear, "unsafe returned claim establishes legal full-body water before steering", failures);
                object[] args = { target, .02f }; float holding = 0f;
                for (int frame = 0; frame < 500; frame++)
                {
                    Vector3 before = body.position; tick.Invoke(actors, args);
                    float distance = Vector3.Distance(before, body.position); sample.cruiseTravel += distance;
                    holding = distance < .0001f ? holding + .02f : 0f; sample.longestHold = Mathf.Max(sample.longestHold, holding);
                    CheckStep(body, before, sample, failures);
                }
                sample.afterCruise = body.position; sample.afterCruiseState = motion.MotionState;
                Require(sample.cruiseTravel > .8f && sample.longestHold < 3f,
                    "returned real 5m shark swims after campaign claim instead of permanent holding", failures);
                Debug.Log("[SharkUnsafeClaim] outsideMovementArea=" + outsideMovementArea + " originalClear=" + originalClear + " originalCenter=" + UnsafeClaimOriginalCenter.ToString("F3") +
                    " reef=" + chosen.ToString() + " sample=" + JsonUtility.ToJson(sample));
                if (failures.Count > 0) throw new InvalidOperationException("Unsafe shark claim failures: " + string.Join("; ", failures));
                Debug.Log("SHARK_UNSAFE_CLAIM_FIXTURE_PASSED outsideMovementArea=" + outsideMovementArea + " actual 5m imported source + actual reef bounds + production claim/steering; synthetic placement/time only");
            }
            finally
            {
                // ClearActors returns/removes only this clone from campaign ownership. It never claimed
                // any live largeCreatures entry, and the owner's inactive state prevents Update/OnEnable work.
                typeof(OceanCampaignCreatures).GetMethod("ClearActors", Fields).Invoke(actors, null);
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(modelHolder);
                Physics.SyncTransforms();
            }
        }

        static void CheckStep(Transform t, Vector3 before, SharkSample sample, List<string> failures)
        {
            Vector3 delta = t.position - before;
            sample.maxStep = Mathf.Max(sample.maxStep, delta.magnitude);
            Require(delta.magnitude <= OceanSharkMotion.MaximumSpeed * .02f + .0001f, "bounded step id=" + sample.id, failures);
            if (delta.sqrMagnitude > .00000001f)
            {
                float dot = Vector3.Dot(delta.normalized, t.forward); sample.minForwardDot = Mathf.Min(sample.minForwardDot, dot);
                Require(dot > .995f, "actual campaign root moves nose-first id=" + sample.id, failures);
            }
            Require(t.up.y > .96f && Mathf.Abs(t.forward.y) <= Mathf.Sin(OceanSharkMotion.MaximumPitch * Mathf.Deg2Rad) + .0001f,
                "upright bounded swimming pose id=" + sample.id, failures);
        }
        static void Restore(Saved row)
        {
            for (int i = 0; i < row.fields.Length; i++) row.fields[i].SetValue(row.motion, row.values[i]);
            row.target.Body.transform.SetPositionAndRotation(row.position, row.rotation);
            row.target.Body.gameObject.SetActive(row.rootActive); row.target.Visual.SetActive(row.visualActive);
            Set(row.target, "reactedHits", row.reactedHits); Set(row.target, "shownHits", -1);
            row.canvas.SetLocalPositionAndRotation(row.canvasLocalPosition, row.canvasLocalRotation);
        }
        static object Get(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
        static void Set(object value, string name, object field) => value.GetType().GetField(name, Fields).SetValue(value, field);
        static void Require(bool pass, string message, List<string> failures)
        {
            LastAssertionCount++;
            if (!pass && !failures.Contains(message)) failures.Add(message);
        }
    }
}
#endif

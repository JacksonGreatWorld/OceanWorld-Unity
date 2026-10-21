#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Production ambient update and actual imported mesh landmarks; synthetic time, not wearer acceptance.</summary>
    public static class OceanAmbientSharkHeadingTests
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly MethodInfo Update = typeof(UnderwaterWorld).GetMethod("UpdateLargeCreatures", Fields);
        static readonly MethodInfo Steer = typeof(UnderwaterWorld).GetMethod("UpdateAmbientSharkHeading", Fields);
        static readonly MethodInfo Place = typeof(UnderwaterWorld).GetMethod("PlaceLargeSharkInClearWater", Fields);
        static readonly MethodInfo WaterHeight = typeof(UnderwaterWorld).GetMethod("LargeSharkWaterHeight", Fields);
        static readonly MethodInfo SegmentClear = typeof(UnderwaterWorld).GetMethod("LargeSharkSegmentClear", Fields);
        public static int LastAssertionCount { get; private set; }
        public static float MinimumNoseMovementDot { get; private set; }
        public static float MaximumStep { get; private set; }
        public static float TotalTravel { get; private set; }
        public static int BlockedSteps { get; private set; }
        public static string LastStepDiagnostic { get; private set; }
        public static string FirstOriginalConditionFailureDiagnostic { get; private set; }
        public static int OriginalConditionFailureCount { get; private set; }
        public static float MaximumResidualMeters { get; private set; }
        public static float MaximumResidualToBoundRatio { get; private set; }
        public static int LastRestoredAssertionCount { get; private set; }

        [Serializable] sealed class StepDiagnostic
        {
            public string fish, stage;
            public int sample;
            public float dt, currentSpeed, distance, forwardDot, noseDot, anatomyForwardDot, rotationDegrees, expectedDistance, errorMeters, lateralErrorMeters;
            public float residualBoundMeters, resolvableDistanceMeters;
            public bool originalConditionsPass, residualWithinBound, strictDirectionRequired;
            public Vector3 bodyBefore, bodyAfter, originBefore, originAfter, localBodyCenter, scale, delta, forward, nose;
            public Vector3 expectedStep, error, lateralError, coordinateUlpBefore, coordinateUlpAfter, centerRoundTripBefore, centerRoundTripAfter;
            public Quaternion rotationBefore, rotationAfter;
        }

        [Serializable] sealed class RouteDiagnostic
        {
            public string fish, cohort, phase, outcome;
            public bool startClear, currentClear, routeValid, reefPatrol;
            public float startWaterHeight, waterHeight, bodyRadius, heightMargin, travel, netDisplacement, currentSpeed, routeTimer, playerDistance;
            public int passStage, observedSamples;
            public Vector3 startBody, body, forward, routeDirection;
        }

        sealed class Saved
        {
            public object animal;
            public FieldInfo[] fields;
            public object[] values;
            public Transform transform;
            public Vector3 position;
            public Quaternion rotation;
            public bool active;
            public MeshFilter mesh;
            public Vector3 head, tail;
            public bool shark;
            public float sceneTravel;
            public bool cohortStartClear;
            public float cohortStartWaterHeight;
            public Vector3 cohortStartBody;
            public string stage;
            public int sample;
            public Vector3 sampleOrigin, sampleCenterRoundTrip;
            public Quaternion sampleRotation;
            public Vector3 Nose => (mesh.transform.TransformPoint(head) - mesh.transform.TransformPoint(tail)).normalized;
            public Vector3 Body => transform.TransformPoint((Vector3)Get(animal, "localBodyCenter"));
            public Vector3 CaptureBody(string context, int index)
            {
                stage = context; sample = index; sampleOrigin = transform.position; sampleRotation = transform.rotation;
                Vector3 body = Body;
                sampleCenterRoundTrip = sampleOrigin + transform.TransformVector((Vector3)Get(animal, "localBodyCenter")) - body;
                return body;
            }
            public void Restore()
            {
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(animal, values[i]);
                transform.SetPositionAndRotation(position, rotation);
                transform.gameObject.SetActive(active);
            }
        }

        public static void RunInScene(UnderwaterWorld world, Camera view)
        {
            LastAssertionCount = BlockedSteps = 0; MinimumNoseMovementDot = 1f; MaximumStep = TotalTravel = 0f;
            LastStepDiagnostic = FirstOriginalConditionFailureDiagnostic = null;
            OriginalConditionFailureCount = 0; MaximumResidualMeters = MaximumResidualToBoundRatio = 0f;
            CheckPrecisionControls();
            Check(world != null && view != null, "actual initialized world and camera required");
            var animals = (IList)Get(world, "largeCreatures");
            var reef = (List<Bounds>)Get(world, "largeReefBounds");
            var controlled = (HashSet<Transform>)Get(world, "campaignSharks");
            var oldReef = reef.ToArray(); var oldControlled = new List<Transform>(controlled);
            var random = UnityEngine.Random.state; object closest = Get(world, "closestSharkDistanceEver");
            var saved = new List<Saved>();
            var inactiveWrappers = new List<GameObject>();
            try
            {
                foreach (object animal in animals)
                {
                    var row = new Saved { animal = animal, fields = animal.GetType().GetFields(Fields), transform = (Transform)Get(animal, "transform"), shark = (bool)Get(animal, "isShark") };
                    row.values = new object[row.fields.Length];
                    for (int i = 0; i < row.fields.Length; i++) row.values[i] = row.fields[i].GetValue(animal);
                    row.position = row.transform.position; row.rotation = row.transform.rotation; row.active = row.transform.gameObject.activeSelf; saved.Add(row);
                    if (!row.shark) continue;
                    // A level-five captured ambient visual may be inactive. This synchronous
                    // check does not run campaign Update/LateUpdate or mutate its saved progress.
                    row.transform.gameObject.SetActive(true);
                    for (Transform parent = row.transform.parent; parent != null && parent.name.StartsWith("Campaign shark steering ", StringComparison.Ordinal); parent = parent.parent)
                        if (!parent.gameObject.activeSelf) { inactiveWrappers.Add(parent.gameObject); parent.gameObject.SetActive(true); }
                    Check(row.transform.gameObject.activeInHierarchy, "ambient visual available before campaign ownership: " + row.transform.name);
                    row.mesh = row.transform.GetComponentInChildren<MeshFilter>();
                    Check(row.mesh != null && row.mesh.sharedMesh != null, "real shark mesh, not an empty steering root");
                    Landmarks(row.mesh.sharedMesh.vertices, out row.head, out row.tail);
                    Check(Vector3.Angle(row.Nose, row.transform.forward) < 6f, "actual imported anatomical nose aligned: " + row.transform.name);
                    controlled.Remove(row.transform);
                    Set(animal, "sharkRouteTimer", 0f);
                }
                int sharkCount = 0; foreach (Saved row in saved) if (row.shark) sharkCount++;
                Check(sharkCount >= 2, "actual great white and smaller hammerhead available");

                // A quest can return a body position that satisfies its own route contract
                // but is below the ambient footprint clearance. Observe that position without
                // teleporting it; an unsafe start may legitimately hold instead of swimming.
                ObserveCohort(world, view, saved, "returnedQuest", false);

                // Separately establish legal production spawn prerequisites in this isolated
                // synchronous Editor test. No campaign/save/device state is changed; finally
                // restores every original field/pose, including the returned quest position.
                foreach (Saved row in saved) if (row.shark)
                {
                    world.SetCampaignSharkControlled(row.transform.gameObject, false);
                    Place.Invoke(world, new object[] { row.animal });
                }
                ObserveCohort(world, view, saved, "legalAmbientSpawn", true);

                foreach (Saved row in saved)
                {
                    if (!row.shark) continue;
                    // Claim/release uses the real ownership API; the ambient loop must not fight a quest actor.
                    world.SetCampaignSharkControlled(row.transform.gameObject, true);
                    Vector3 owned = row.Body; Quaternion ownedRotation = row.transform.rotation;
                    for (int i = 0; i < 10; i++) Update.Invoke(world, new object[] { .02f });
                    Check(Vector3.Distance(owned, row.Body) < .00001f && Quaternion.Angle(ownedRotation, row.transform.rotation) < .001f,
                        "campaign ownership prevents competing ambient movement");
                    world.SetCampaignSharkControlled(row.transform.gameObject, false);
                    Check(Vector3.Distance(owned, row.Body) < .00001f && Quaternion.Angle(ownedRotation, row.transform.rotation) < .001f,
                        "legal campaign release preserves the earned body position and pose");
                    Check((float)Get(row.animal, "sharkRouteTimer") == 0f && !(bool)Get(row.animal, "sharkRouteValid"),
                        "release invalidates old ambient route before resuming");
                    for (int i = 0; i < 100; i++)
                    { Vector3 before = row.CaptureBody("ownership-release", i); Update.Invoke(world, new object[] { .02f }); CheckStep(row, before, .02f); }

                    CheckIllegalReleaseRecovery(world, row, reef, oldReef);

                    // A wall directly ahead reproduces the old pure-up replacement. Keep the real
                    // model/radius and production helper, but isolate a deterministic high-water obstacle.
                    reef.Clear(); float radius = (float)Get(row.animal, "bodyRadius");
                    Vector3 body = new Vector3(0f, 40f, 0f);
                    row.transform.rotation = Quaternion.identity; row.transform.position += body - row.Body;
                    Set(row.animal, "currentSpeed", 1.5f); Set(row.animal, "direction", Vector3.forward);
                    Set(row.animal, "sharkRouteDirection", Vector3.forward); Set(row.animal, "sharkRouteValid", true); Set(row.animal, "sharkRouteTimer", .20f);
                    reef.Add(new Bounds(body + Vector3.forward * (radius + .52f), new Vector3(16f, 8f, 1f)));
                    Vector3 player = body - Vector3.forward * 50f;
                    Steer.Invoke(world, new object[] { row.animal, body, Vector3.forward, player, .05f });
                    Check(Vector3.Distance(row.Body, body) < .00001f && (float)Get(row.animal, "currentSpeed") == 0f,
                        "blocked actual forward step stops instead of sideways/up displacement");
                    BlockedSteps++;
                    float bypassTravel = 0f;
                    for (int i = 0; i < 500; i++)
                    {
                        Vector3 before = row.CaptureBody("wall-bypass", i); Quaternion rotation = row.transform.rotation;
                        Steer.Invoke(world, new object[] { row.animal, before, Vector3.forward, player, .02f });
                        CheckStep(row, before, .02f); bypassTravel += Vector3.Distance(before, row.Body);
                        // acos(dot) loses resolution for a sub-degree float quaternion.
                        // atan2 of the relative quaternion retains the same angular limit.
                        // Parent/world quaternion reconstruction in the real imported hierarchy
                        // measured .8613006 degrees for the requested .86-degree step.
                        // Allow .02 degrees of transform precision, retaining the 43 deg/s cap.
                        Check(StableRotationAngle(rotation, row.transform.rotation) <= 43f * .02f + .02f,
                            "bounded rotation while finding safe water; stable=" + StableRotationAngle(rotation, row.transform.rotation)
                            + " acos=" + Quaternion.Angle(rotation, row.transform.rotation));
                        Check(Mathf.Abs(row.transform.forward.y) <= Mathf.Sin(12f * Mathf.Deg2Rad) + .0001f, "no vertical nose-up escape");
                        Check((bool)typeof(UnderwaterWorld).GetMethod("LargeSharkSegmentClear", Fields).Invoke(world,
                            new object[] { before, row.Body, radius }), "actual moved envelope does not enter obstacle");
                    }
                    Check(bypassTravel > .1f, "bounded candidate search turns toward available clear water");

                    // A broad raised shelf leaves a legal horizontal route while all
                    // eight -12 degree rays end below its conservative safe-water height.
                    reef.Clear(); body = new Vector3(0f, 40.15f, 0f);
                    row.transform.rotation = Quaternion.identity; row.transform.position += body - row.Body;
                    Set(row.animal, "currentSpeed", 0f); Set(row.animal, "sharkRouteTimer", 0f); Set(row.animal, "sharkRouteValid", false);
                    reef.Add(new Bounds(new Vector3(0f, 40f - radius - .45f, 0f), new Vector3(100f, .2f, 100f)));
                    player = body - Vector3.forward * 50f;
                    Check((bool)typeof(UnderwaterWorld).GetMethod("LargeSharkSegmentClear", Fields).Invoke(world,
                        new object[] { body, body, radius }), "shelf fixture starts in legal water");
                    body = row.CaptureBody("shelf-fallback", 0);
                    Steer.Invoke(world, new object[] { row.animal, body, new Vector3(0f, -.5f, 1f).normalized, player, .02f });
                    Check((bool)Get(row.animal, "sharkRouteValid") && Mathf.Abs(((Vector3)Get(row.animal, "sharkRouteDirection")).y) < .00001f,
                        "failed downward candidates fall back to a clear horizontal heading");
                    Check(Vector3.Distance(body, row.Body) > .00001f && Mathf.Abs(row.Body.y - body.y) < .00001f,
                        "legal shelf route swims forward without vertical correction or permanent stop");
                    CheckStep(row, body, .02f);
                    reef.Clear(); reef.AddRange(oldReef); row.Restore();
                }
                CheckPlayerSegments();
                Debug.Log($"AMBIENT_SHARK_HEADING_TESTS_PASSED assertions={LastAssertionCount} noseMovementDotMin={MinimumNoseMovementDot:F5} maxStep={MaximumStep:F5} travel={TotalTravel:F2} blockedStops={BlockedSteps} originalConditionFailures={OriginalConditionFailureCount} residualMax={MaximumResidualMeters:G9}m residualToBoundMax={MaximumResidualToBoundRatio:G9}; actual GLB and ambient production methods, synthetic time/obstacle, no headset acceptance");
            }
            finally
            {
                foreach (Saved row in saved) row.Restore();
                foreach (GameObject wrapper in inactiveWrappers) if (wrapper != null) wrapper.SetActive(false);
                reef.Clear(); reef.AddRange(oldReef); controlled.Clear(); foreach (Transform transform in oldControlled) controlled.Add(transform);
                Set(world, "closestSharkDistanceEver", closest); UnityEngine.Random.state = random;
            }
        }

        // Invoke after an actual 5 -> 6 -> 1 roundtrip, once the campaign switch
        // finishes. This checks the returned cached map before any test placement;
        // it deliberately does not call Place/Ensure to make its own input legal.
        public static void RunRestoredAmbientWaterInScene(UnderwaterWorld world, Camera view)
        {
            int initialAssertions = LastAssertionCount;
            LastRestoredAssertionCount = 0;
            Check(world != null && view != null, "actual restored world and camera required");
            var animals = (IList)Get(world, "largeCreatures");
            var controlled = (HashSet<Transform>)Get(world, "campaignSharks");
            var saved = new List<Saved>();
            var random = UnityEngine.Random.state;
            object closest = Get(world, "closestSharkDistanceEver");
            try
            {
                int sharks = 0;
                foreach (object animal in animals)
                {
                    var row = new Saved { animal = animal, fields = animal.GetType().GetFields(Fields), transform = (Transform)Get(animal, "transform") };
                    row.values = new object[row.fields.Length];
                    for (int field = 0; field < row.fields.Length; field++) row.values[field] = row.fields[field].GetValue(animal);
                    row.position = row.transform.position; row.rotation = row.transform.rotation;
                    row.active = row.transform.gameObject.activeSelf;
                    row.shark = (bool)Get(animal, "isShark") && row.transform.gameObject.activeInHierarchy && !controlled.Contains(row.transform);
                    saved.Add(row);
                    if (!row.shark) continue;
                    sharks++;
                    row.mesh = row.transform.GetComponentInChildren<MeshFilter>();
                    Check(row.mesh != null && row.mesh.sharedMesh != null, "restored ambient actor retains actual imported anatomy");
                    Landmarks(row.mesh.sharedMesh.vertices, out row.head, out row.tail);
                    float radius = (float)Get(animal, "bodyRadius");
                    Check((bool)SegmentClear.Invoke(world, new object[] { row.Body, row.Body, radius }),
                        "actual cached map return starts in legal ambient water without test placement: " + row.transform.name);
                }
                Check(sharks >= 2, "returned non-shark quest releases both ambient sharks");
                var before = new Vector3[saved.Count];
                for (int sample = 0; sample < 500; sample++)
                {
                    foreach (Saved row in saved) if (row.shark) row.CaptureBody("actual-map-return", sample);
                    for (int i = 0; i < saved.Count; i++) before[i] = saved[i].Body;
                    Update.Invoke(world, new object[] { .02f });
                    for (int i = 0; i < saved.Count; i++)
                    {
                        Saved row = saved[i];
                        if (!row.shark) continue;
                        CheckStep(row, before[i], .02f);
                        row.sceneTravel += Vector3.Distance(before[i], row.Body);
                    }
                }
                foreach (Saved row in saved) if (row.shark)
                    Check(row.sceneTravel > .25f, "actual cached map return resumes useful forward ambient travel: " + row.transform.name);
                Debug.Log("RESTORED_AMBIENT_SHARK_WATER_TESTS_PASSED environment=" + world.ActiveEnvironment + " sharks=" + sharks +
                    " assertions=" + (LastAssertionCount - initialAssertions) + "; actual cached map, no prerequisite relocation by test");
            }
            finally
            {
                foreach (Saved row in saved) row.Restore();
                UnityEngine.Random.state = random;
                typeof(UnderwaterWorld).GetField("closestSharkDistanceEver", Fields).SetValue(world, closest);
                LastRestoredAssertionCount = LastAssertionCount - initialAssertions;
            }
        }

        static void ObserveCohort(UnderwaterWorld world, Camera view, List<Saved> saved, string cohort, bool requireLegalMovement)
        {
            foreach (Saved row in saved) if (row.shark)
            {
                row.sceneTravel = 0f; row.cohortStartBody = row.Body;
                float radius = (float)Get(row.animal, "bodyRadius");
                row.cohortStartClear = (bool)SegmentClear.Invoke(world, new object[] { row.Body, row.Body, radius });
                row.cohortStartWaterHeight = (float)WaterHeight.Invoke(world, new object[] { row.Body, radius });
                LogRouteState(world, view, row, cohort, "start", 0);
            }
            // Emit all subjects' initial state before the first prerequisite assertion.
            if (requireLegalMovement)
                foreach (Saved row in saved) if (row.shark)
                    Check(row.cohortStartClear, "production placement establishes legal ambient water: " + row.transform.name);
            var previous = new Vector3[saved.Count];
            int observed = 0;
            try
            {
                for (int frame = 0; frame < 600; frame++)
                {
                    for (int i = 0; i < saved.Count; i++) previous[i] = saved[i].CaptureBody(cohort, frame);
                    Update.Invoke(world, new object[] { .02f }); observed++;
                    // Record every subject's travel before a failing step assertion can stop the test.
                    for (int i = 0; i < saved.Count; i++) if (saved[i].shark)
                        saved[i].sceneTravel += Vector3.Distance(saved[i].Body, previous[i]);
                    for (int i = 0; i < saved.Count; i++) if (saved[i].shark) CheckStep(saved[i], previous[i], .02f);
                }
            }
            finally
            {
                foreach (Saved row in saved) if (row.shark) LogRouteState(world, view, row, cohort, "end", observed);
            }
            if (requireLegalMovement)
                foreach (Saved row in saved) if (row.shark)
                    Check(row.sceneTravel > .25f, "each shark swims from a verified legal ambient spawn: " + row.transform.name);
        }

        static void LogRouteState(UnderwaterWorld world, Camera view, Saved row, string cohort, string phase, int samples)
        {
            Vector3 body = row.Body; float radius = (float)Get(row.animal, "bodyRadius");
            float water = (float)WaterHeight.Invoke(world, new object[] { body, radius });
            bool clear = (bool)SegmentClear.Invoke(world, new object[] { body, body, radius });
            string outcome = phase == "start" ? "prerequisite-observation" :
                row.sceneTravel > .25f ? "swimming" :
                !row.cohortStartClear && row.sceneTravel <= .0001f ? "unsafe-returned-position-held-without-relocation" : "limited-or-no-travel";
            Debug.Log("[AmbientSharkRouteDiagnostic] " + JsonUtility.ToJson(new RouteDiagnostic
            {
                fish = row.transform.name, cohort = cohort, phase = phase, outcome = outcome,
                startClear = row.cohortStartClear, currentClear = clear, startWaterHeight = row.cohortStartWaterHeight,
                waterHeight = water, bodyRadius = radius, heightMargin = body.y - water,
                startBody = row.cohortStartBody, body = body, forward = row.transform.forward,
                travel = row.sceneTravel, netDisplacement = Vector3.Distance(body, row.cohortStartBody),
                routeValid = (bool)Get(row.animal, "sharkRouteValid"), routeTimer = (float)Get(row.animal, "sharkRouteTimer"),
                routeDirection = (Vector3)Get(row.animal, "sharkRouteDirection"), currentSpeed = (float)Get(row.animal, "currentSpeed"),
                passStage = (int)Get(row.animal, "passStage"), reefPatrol = (bool)Get(row.animal, "reefPatrol"), observedSamples = samples,
                playerDistance = Vector3.Distance(body, view.transform.position - Vector3.up * .45f)
            }));
        }

        static void CheckPlayerSegments()
        {
            MethodInfo method = typeof(UnderwaterWorld).GetMethod("AmbientSharkPlayerSegmentClear", BindingFlags.Static | BindingFlags.NonPublic);
            bool Clear(Vector3 a, Vector3 b) => (bool)method.Invoke(null, new object[] { a, b, Vector3.zero, 3f });
            Check(!Clear(new Vector3(0, 0, 4), new Vector3(0, 0, -4)), "segment cannot cross diver exclusion with two outside endpoints");
            Check(!Clear(new Vector3(0, 0, 2), new Vector3(0, 0, 1)), "diver approaching shark cannot cause inward movement");
            Check(Clear(new Vector3(0, 0, 2), new Vector3(0, 0, 2.1f)), "already-near shark can escape outward");
            Check(Clear(Vector3.zero, Vector3.right * .1f), "coincident diver and shark can separate without a permanent zero-vector stall");
        }

        static void CheckIllegalReleaseRecovery(UnderwaterWorld world, Saved row, List<Bounds> reef, Bounds[] originalReef)
        {
            float radius = (float)Get(row.animal, "bodyRadius");
            try
            {
                for (int fixture = 0; fixture < 2; fixture++)
                {
                    reef.Clear();
                    Vector3 illegal = row.Body;
                    illegal.y = fixture == 0 ? 40f :
                        UnderwaterWorld.SampleRenderedSeabedHeight(illegal.x, illegal.z) + radius + .04f;
                    row.transform.rotation = Quaternion.identity;
                    row.transform.position += illegal - row.Body;
                    Set(row.animal, "direction", Vector3.forward);
                    if (fixture == 0) reef.Add(new Bounds(illegal, Vector3.one));
                    Check(!(bool)SegmentClear.Invoke(world, new object[] { row.Body, row.Body, radius }),
                        "release fixture reproduces an illegal ambient origin: " + fixture);

                    world.SetCampaignSharkControlled(row.transform.gameObject, true);
                    world.SetCampaignSharkControlled(row.transform.gameObject, false);
                    Vector3 restored = row.Body;
                    Check((bool)SegmentClear.Invoke(world, new object[] { restored, restored, radius }),
                        "configuration-time ownership release restores legal full-body water: " + fixture);
                    Check(Vector3.Distance(illegal, restored) > .1f,
                        "illegal ownership release actually repairs the origin instead of clearing route state only");

                    float travel = 0f;
                    Vector3 player = restored - Vector3.forward * 50f;
                    for (int sample = 0; sample < 200; sample++)
                    {
                        Vector3 before = row.CaptureBody("recovered-release-" + fixture, sample);
                        Steer.Invoke(world, new object[] { row.animal, before, Vector3.forward, player, .02f });
                        CheckStep(row, before, .02f);
                        Check((bool)SegmentClear.Invoke(world, new object[] { before, row.Body, radius }),
                            "recovered shark retains actual runtime segment clearance");
                        travel += Vector3.Distance(before, row.Body);
                    }
                    Check(travel > .5f, "recovered ownership release resumes nose-first travel rather than permanent holding: " + fixture);
                }
            }
            finally { reef.Clear(); reef.AddRange(originalReef); }
        }

        static void CheckStep(Saved row, Vector3 before, float dt)
        {
            Vector3 after = row.Body, delta = after - before; float distance = delta.magnitude;
            Vector3 forward = row.transform.forward, nose = row.Nose;
            float currentSpeed = (float)Get(row.animal, "currentSpeed");
            Vector3 expected = forward * (currentSpeed * dt), error = delta - expected;
            float bound = TranslationErrorBound(before, after, row.sampleOrigin, row.transform.position);
            float resolvable = bound * 50f;
            bool strictDirection = distance > resolvable && expected.magnitude > resolvable;
            MaximumStep = Mathf.Max(MaximumStep, distance); TotalTravel += distance;
            MaximumResidualMeters = Mathf.Max(MaximumResidualMeters, error.magnitude);
            MaximumResidualToBoundRatio = Mathf.Max(MaximumResidualToBoundRatio, error.magnitude / bound);
            float forwardDot = distance > .00001f ? Vector3.Dot(delta / distance, forward) : 1f;
            float noseDot = distance > .00001f ? Vector3.Dot(delta / distance, nose) : 1f;
            float anatomyForwardDot = Vector3.Dot(nose, forward);
            bool originalPass = distance <= (float)Get(row.animal, "speed") * 1.1f * dt + .00002f && forwardDot > .9999f && noseDot > .99f;
            bool residualPass = TranslationMatches(delta, expected, bound);
            bool newPass = residualPass && anatomyForwardDot > .99f && (!strictDirection || (forwardDot > .9999f && noseDot > .99f));
            if (!originalPass) OriginalConditionFailureCount++;
            if ((!originalPass && FirstOriginalConditionFailureDiagnostic == null) || !newPass)
            {
                Vector3 lateral = delta - forward * Vector3.Dot(delta, forward);
                var diagnostic = new StepDiagnostic
                {
                    fish = row.transform.name, stage = row.stage, sample = row.sample, dt = dt, currentSpeed = currentSpeed,
                    distance = distance, forwardDot = forwardDot, noseDot = noseDot, anatomyForwardDot = anatomyForwardDot,
                    rotationDegrees = Quaternion.Angle(row.sampleRotation, row.transform.rotation),
                    expectedDistance = currentSpeed * dt, errorMeters = error.magnitude, lateralErrorMeters = lateral.magnitude,
                    residualBoundMeters = bound, resolvableDistanceMeters = resolvable,
                    originalConditionsPass = originalPass, residualWithinBound = residualPass, strictDirectionRequired = strictDirection,
                    bodyBefore = before, bodyAfter = after, originBefore = row.sampleOrigin, originAfter = row.transform.position,
                    localBodyCenter = (Vector3)Get(row.animal, "localBodyCenter"), scale = row.transform.lossyScale,
                    delta = delta, forward = forward, nose = nose, expectedStep = expected, error = error, lateralError = lateral,
                    coordinateUlpBefore = CoordinateUlp(before), coordinateUlpAfter = CoordinateUlp(after),
                    centerRoundTripBefore = row.sampleCenterRoundTrip,
                    centerRoundTripAfter = row.transform.position + row.transform.TransformVector((Vector3)Get(row.animal, "localBodyCenter")) - after,
                    rotationBefore = row.sampleRotation, rotationAfter = row.transform.rotation
                };
                LastStepDiagnostic = JsonUtility.ToJson(diagnostic);
                if (!originalPass && FirstOriginalConditionFailureDiagnostic == null) FirstOriginalConditionFailureDiagnostic = LastStepDiagnostic;
                Debug.Log("[AmbientSharkStepDiagnostic] " + LastStepDiagnostic);
            }
            Check(distance <= (float)Get(row.animal, "speed") * 1.1f * dt + bound, "bounded actual body-centre step");
            Check(anatomyForwardDot > .99f, "actual imported anatomical nose agrees with heading every frame: " + row.transform.name);
            Check(residualPass, "actual body displacement equals speed * dt * actual forward within coordinate precision");
            if (distance <= .00001f) return;
            MinimumNoseMovementDot = Mathf.Min(MinimumNoseMovementDot, noseDot);
            if (!strictDirection) return; // Tiny normalized differences amplify float-coordinate rounding.
            Check(noseDot > .99f, "actual imported anatomical nose follows resolvable world displacement: " + row.transform.name);
            Check(forwardDot > .9999f, "no sideways projected/vertical substitute translation at resolvable step length");
        }

        // Budget for the complete TransformPoint -> centre compensation -> endpoint
        // subtraction -> translated TransformPoint chain, not a single float add.
        // The measured 34 mm step in passage-04 accumulated 5.08 micrometres while
        // its forward dot was .99999982. 32 coordinate ULPs with a 10 micrometre
        // floor stay far below the 1 cm lateral/vertical negative controls.
        // This is an absolute vector residual bound, not a
        // permissive angle test. The original angle failure is retained as evidence.
        static float StableRotationAngle(Quaternion from, Quaternion to)
        {
            Quaternion relative = Quaternion.Inverse(from) * to;
            return 2f * Mathf.Atan2(Mathf.Sqrt(relative.x * relative.x + relative.y * relative.y + relative.z * relative.z),
                Mathf.Abs(relative.w)) * Mathf.Rad2Deg;
        }

        static float TranslationErrorBound(Vector3 before, Vector3 after, Vector3 originBefore, Vector3 originAfter)
        {
            Vector3 bodyUlp = Vector3.Max(CoordinateUlp(before), CoordinateUlp(after));
            Vector3 originUlp = Vector3.Max(CoordinateUlp(originBefore), CoordinateUlp(originAfter));
            return Mathf.Max(.00001f, 32f * Vector3.Max(bodyUlp, originUlp).magnitude);
        }
        static bool TranslationMatches(Vector3 actual, Vector3 expected, float bound) => (actual - expected).magnitude <= bound;

        static void CheckPrecisionControls()
        {
            foreach (Vector3 origin in new[] { Vector3.zero, new Vector3(41.17f, 40.15f, -3.42f) })
            {
                Vector3 tiny = new Vector3(.3f, .1f, 1f).normalized * .00001f;
                Vector3 quantized = origin + tiny;
                float bound = TranslationErrorBound(origin, quantized, origin, quantized);
                Check(TranslationMatches(quantized - origin, tiny, bound), "coordinate rounding retained for tiny forward step");
                Vector3 expected = Vector3.forward * .01f;
                Check(!TranslationMatches(expected + Vector3.right * .01f, expected, bound), "negative control rejects one-centimetre sideways drift");
                Check(!TranslationMatches(Vector3.up * .01f, expected, bound), "negative control rejects one-centimetre pure vertical substitute");
                Check(bound * 50f < .01f, "centimetre fixture is above the strict direction resolution threshold");
            }
        }

        static Vector3 CoordinateUlp(Vector3 value) => new Vector3(FloatUlp(value.x), FloatUlp(value.y), FloatUlp(value.z));
        static float FloatUlp(float value)
        {
            value = Mathf.Abs(value);
            int bits = BitConverter.SingleToInt32Bits(value);
            return BitConverter.Int32BitsToSingle(bits + 1) - value;
        }

        // Mesh-space PCA and end-region averages; not transform.forward masquerading as anatomy.
        static void Landmarks(Vector3[] vertices, out Vector3 head, out Vector3 tail)
        {
            Vector3 mean = Vector3.zero; foreach (Vector3 p in vertices) mean += p; mean /= vertices.Length;
            var covariance = new float[3, 3];
            foreach (Vector3 p in vertices) { Vector3 q = p - mean; for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) covariance[i, j] += q[i] * q[j]; }
            Vector3 axis = Vector3.forward;
            for (int n = 0; n < 80; n++)
                axis = new Vector3(covariance[0, 0] * axis.x + covariance[0, 1] * axis.y + covariance[0, 2] * axis.z,
                    covariance[1, 0] * axis.x + covariance[1, 1] * axis.y + covariance[1, 2] * axis.z,
                    covariance[2, 0] * axis.x + covariance[2, 1] * axis.y + covariance[2, 2] * axis.z).normalized;
            if (axis.z < 0f) axis = -axis;
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (Vector3 p in vertices) { float s = Vector3.Dot(p, axis); min = Mathf.Min(min, s); max = Mathf.Max(max, s); }
            head = tail = Vector3.zero; int heads = 0, tails = 0;
            foreach (Vector3 p in vertices)
            { float s = Vector3.Dot(p, axis); if (s > Mathf.Lerp(min, max, .93f)) { head += p; heads++; } if (s < Mathf.Lerp(min, max, .07f)) { tail += p; tails++; } }
            Check(heads > 0 && tails > 0, "actual mesh anatomical end regions exist"); head /= heads; tail /= tails;
        }
        static object Get(object instance, string name) => instance.GetType().GetField(name, Fields).GetValue(instance);
        static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Fields).SetValue(instance, value);
        static void Check(bool pass, string message) { LastAssertionCount++; if (!pass) throw new InvalidOperationException("Ambient shark: " + message); }
    }
}
#endif

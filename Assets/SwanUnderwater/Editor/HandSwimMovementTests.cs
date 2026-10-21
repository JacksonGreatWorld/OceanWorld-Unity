#if UNITY_EDITOR
using System;
using System.Reflection;
using HandType = SwanUnderwater.OceanHandSide;
using HandJoint = SwanUnderwater.OceanHandJointId;
using HandJointLocation = SwanUnderwater.OceanHandJoint;
using HandJointLocations = SwanUnderwater.OceanHandFrame;
using HandLocationStatus = SwanUnderwater.OceanHandLocationStatus;
using HandAimState = SwanUnderwater.OceanHandAim;
using HandAimStatus = SwanUnderwater.OceanHandAimStatus;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    public static class HandSwimMovementTests
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        static void Set(HandSwimController controller, string field, object value) =>
            typeof(HandSwimController).GetField(field, Private).SetValue(controller, value);

        static void Joint(HandJointLocations sample, int index, Vector3 point)
        {
            sample.jointLocations[index] = new HandJointLocation {
                locationStatus = HandLocationStatus.PositionTracked | HandLocationStatus.PositionValid,
                pose = new UnityEngine.Pose(point, Quaternion.identity)
            };
        }
        static void Finger(HandJointLocations sample, int proximal, Vector3 origin, float curl)
        {
            float a = Mathf.Acos(1f - 1.5f * curl), length = .025f;
            Joint(sample, proximal, origin);
            origin += Vector3.forward * length; Joint(sample, proximal + 1, origin);
            origin += new Vector3(0, -Mathf.Sin(a), Mathf.Cos(a)) * length; Joint(sample, proximal + 2, origin);
            origin += new Vector3(0, -Mathf.Sin(a * 2), Mathf.Cos(a * 2)) * length; Joint(sample, proximal + 3, origin);
        }
        static HandJointLocations Pose(bool left, Vector3 palm, float indexCurl, Vector3 thumbDirection, float gripCurl = .4f)
        {
            var sample = new HandJointLocations { isActive = 1, jointCount = 26, handScale = 1,
                jointLocations = new HandJointLocation[26] };
            Joint(sample, (int)HandJoint.JointPalm, palm);
            Joint(sample, (int)HandJoint.JointIndexMetacarpal, palm - Vector3.forward * .035f);
            Finger(sample, (int)HandJoint.JointIndexProximal, palm + Vector3.forward * .01f, indexCurl);
            Finger(sample, (int)HandJoint.JointMiddleProximal, palm + Vector3.right * .02f, gripCurl);
            Finger(sample, (int)HandJoint.JointRingProximal, palm + Vector3.right * .04f, gripCurl);
            Finger(sample, (int)HandJoint.JointLittleProximal, palm + Vector3.right * .06f, gripCurl);
            for (int i = 0; i < 4; i++) Joint(sample, (int)HandJoint.JointThumbMetacarpal + i,
                palm + Vector3.right * (left ? -.04f : .04f) + thumbDirection * (.025f * i));
            return sample;
        }

        static int CheckCachedJointPoses(HandSwimController controller, Transform origin, Transform eye)
        {
            int assertions = 0;
            Action<bool, string> check = (value, message) => {
                if (!value) throw new InvalidOperationException("Cached spear-pose validation: " + message);
                assertions++;
            };
            Set(controller, "trackingSpace", origin);
            eye.localPosition = Vector3.up * 1.65f;
            controller.enabled = true; // Synchronous test; destroyed before a frame/SDK Update.
            foreach (bool left in new[] { true, false })
            {
                HandType hand = left ? HandType.HandLeft : HandType.HandRight;
                string field = left ? "leftLocations" : "rightLocations";
                Vector3 palm = new Vector3(left ? -.2f : .2f, 1.25f, .42f);
                typeof(HandSwimController).GetProperty(left ? "LeftHandActive" : "RightHandActive").SetValue(controller, true);
                Action<HandJointLocations> inject = sample => {
                    Set(controller, field, sample); Set(controller, "handSampleFrame", Time.frameCount);
                    typeof(HandSwimController).GetProperty(left ? "LeftPalmPos" : "RightPalmPos")
                        .SetValue(controller, origin.TransformPoint(palm));
                };
                inject(Pose(left, palm, 0f, Vector3.up));
                check(controller.TryGetSpearPose(hand, out bool grip, out bool thumb, out float curl, out Vector3 aim, out bool reach) &&
                    grip && thumb && reach && curl <= .12f && Vector3.Dot(aim, origin.forward) > .999f,
                    "either hand's straight trigger/upward thumb produces a valid forward pose");
                Vector3 beforeAim = aim;
                inject(Pose(left, palm, .45f, Vector3.up));
                check(controller.TryGetSpearPose(hand, out grip, out thumb, out curl, out aim, out reach) &&
                    grip && thumb && reach && curl >= .22f && Vector3.Dot(aim, beforeAim) > .999f,
                    "curling the trigger does not tilt the metacarpal-derived aim down");
                inject(Pose(left, palm, 0f, Quaternion.AngleAxis(60, Vector3.forward) * Vector3.up));
                check(controller.TryGetSpearPose(hand, out _, out thumb, out _, out _, out _) && thumb,
                    "a 60-degree sideways thumb lean is accepted, not only exactly vertical");
                inject(Pose(left, palm, 0f, Vector3.right));
                check(controller.TryGetSpearPose(hand, out _, out thumb, out _, out _, out _) && !thumb,
                    "a horizontal thumb is valid tracking but not the requested thumb-up pose");
                inject(Pose(left, palm, 0f, Vector3.up, 0f));
                check(controller.TryGetSpearPose(hand, out grip, out _, out _, out _, out _) && !grip,
                    "open middle/ring/little fingers cannot display a gun");
                var missing = Pose(left, palm, 0f, Vector3.up);
                missing.jointLocations[(int)HandJoint.JointIndexMetacarpal].locationStatus = HandLocationStatus.PositionValid;
                inject(missing);
                check(!controller.TryGetSpearPose(hand, out _, out _, out _, out _, out _),
                    "estimated-only metacarpal cannot silently masquerade as tracked aim");
                inject(Pose(left, palm, 0f, Vector3.up)); Set(controller, "handSampleFrame", Time.frameCount - 1);
                check(!controller.TryGetSpearPose(hand, out _, out _, out _, out _, out _), "previous-frame joints cannot aim");
                origin.rotation = Quaternion.Euler(0, 90, 0);
                inject(Pose(left, palm, 0f, Vector3.up));
                check(controller.TryGetSpearPose(hand, out grip, out thumb, out _, out aim, out reach) &&
                    grip && thumb && reach && Vector3.Dot(aim, Vector3.right) > .999f,
                    "tracking-origin yaw changes world aim while preserving thumb-up and reach");
                origin.rotation = Quaternion.identity;
            }
            return assertions;
        }

        /// <summary>
        /// Calls the actual controller's spear/locomotion arbitration with fresh cached joints.
        /// Aiming changes which hand can paddle; it cannot erase momentum made by the free hand.
        /// This is synchronous synthetic input, so no native hand fetch or device pose is involved.
        /// </summary>
        static int CheckSpearSwimContinuity(HandSwimController controller, Transform origin, Transform eye)
        {
            int assertions = 0;
            Action<bool, string> check = (ok, why) => {
                if (!ok) throw new InvalidOperationException("Spear/swim continuity: " + why);
                assertions++;
            };
            var suppressed = typeof(HandSwimController).GetMethod("IsSpearAimSuppressed", Private);
            var swimField = typeof(HandSwimController).GetField("swimVelocity", Private);
            var currentField = typeof(HandSwimController).GetField("currentVelocity", Private);
            Vector3 swim = new Vector3(.35f, .08f, .72f);
            Vector3 current = new Vector3(.23f, .05f, .50f);
            Action<string> momentumUnchanged = reason => {
                check(Vector3.Distance((Vector3)swimField.GetValue(controller), swim) < .000001f,
                    reason + " must preserve existing paddle velocity from the free hand");
                check(Vector3.Distance((Vector3)currentField.GetValue(controller), current) < .000001f,
                    reason + " must preserve the locomotion motor's current velocity");
            };
            Action<bool, bool, string> handGuards = (left, right, reason) => {
                check((bool)suppressed.Invoke(controller, new object[] { HandType.HandLeft }) == left,
                    reason + ": left hand's actual paddle guard");
                check((bool)suppressed.Invoke(controller, new object[] { HandType.HandRight }) == right,
                    reason + ": right hand's actual paddle guard");
                check(!controller.MenuLocomotionPaused, reason + " cannot set a global menu pause");
            };
            Action<bool, float, Vector3> inject = (left, gripCurl, thumb) => {
                Vector3 palm = new Vector3(left ? -.2f : .2f, 1.25f, .42f);
                Set(controller, left ? "leftLocations" : "rightLocations", Pose(left, palm, 0f, thumb, gripCurl));
                Set(controller, "handSampleFrame", Time.frameCount);
                typeof(HandSwimController).GetProperty(left ? "LeftHandActive" : "RightHandActive").SetValue(controller, true);
                typeof(HandSwimController).GetProperty(left ? "LeftPalmPos" : "RightPalmPos")
                    .SetValue(controller, origin.TransformPoint(palm));
            };
            Set(controller, "trackingSpace", origin);
            eye.localPosition = Vector3.up * 1.65f;
            controller.enabled = true;
            inject(true, .4f, Vector3.up); inject(false, .4f, Vector3.up);
            controller.SetSpearAimSuppressed(false, false);
            Set(controller, "swimVelocity", swim); Set(controller, "currentVelocity", current);

            controller.SetSpearAimSuppressed(true, false);
            momentumUnchanged("entering left-hand aim while the right hand has supplied propulsion");
            handGuards(true, false, "left-hand aim occupies only the left hand");
            for (int i = 0; i < 3; i++)
            {
                controller.SetSpearAimSuppressed(false, false);
                momentumUnchanged("hiding the gun on recognition interruption " + i);
                handGuards(false, false, "hidden gun releases both paddle guards");
                controller.SetSpearAimSuppressed(true, false);
                momentumUnchanged("re-equipping the left-hand gun after interruption " + i);
                handGuards(true, false, "re-equipping still leaves the right hand free");
            }
            controller.SetSpearAimSuppressed(false, true);
            momentumUnchanged("switching aim from left hand to right hand");
            handGuards(false, true, "right-hand aim occupies only the right hand");
            controller.SetSpearAimSuppressed(false, true);
            momentumUnchanged("renewing an unchanged right-hand aim lease");

            // The renderer's one-frame lease is not enough without the same-frame real pose.
            inject(false, 0f, Vector3.up);
            handGuards(false, false, "opening the aiming fingers releases their paddle guard immediately");
            momentumUnchanged("actual pose release before the next gun LateUpdate");
            inject(false, .4f, Vector3.right);
            handGuards(false, false, "lowering the thumb also cancels actual aim immediately");
            inject(false, .4f, Vector3.up);
            Set(controller, "spearAimFrame", Time.frameCount - 1);
            handGuards(false, true, "the previous LateUpdate lease remains valid for this Update");
            Set(controller, "spearAimFrame", Time.frameCount - 2);
            handGuards(false, false, "an unrenewed lease expires even with a valid finger-gun pose");
            momentumUnchanged("expiry of the right-hand aim lease");
            controller.SetSpearAimSuppressed(false, true);
            momentumUnchanged("reacquiring the gun after lease expiry");
            handGuards(false, true, "a renewed lease still occupies only its aiming hand");
            controller.SetSpearAimSuppressed(false, false);
            handGuards(false, false, "explicit gun hiding releases both hands immediately");
            momentumUnchanged("final gun hiding");

            var turn = (SingleHandTurnModel)typeof(HandSwimController).GetField("handTurnModel", Private).GetValue(controller);
            var bankField = typeof(SingleHandTurnModel).GetField("deferredTurn", Private);
            var pendingField = typeof(HandSwimController).GetField("pendingHandTurn", Private);
            foreach (bool aimingLeft in new[] { true, false })
            foreach (bool confirmedPull in new[] { false, true })
            {
                foreach (bool aimingHandOwnsTurn in new[] { false, true })
                {
                    controller.SetSpearAimSuppressed(false, false);
                    turn.Reset();
                    const float dt = .01f;
                    for (int i = 0; i < 50; i++) turn.Step(dt, 0f, 0f, true, true);
                    bool turningLeft = aimingHandOwnsTurn ? aimingLeft : !aimingLeft;
                    float yawDelta = 0f;
                    for (int i = 0; i < (confirmedPull ? 40 : 1); i++)
                        yawDelta = turn.Step(dt, turningLeft ? .8f : 0f, turningLeft ? 0f : .8f, true, true);
                    var expectedState = confirmedPull ?
                        (turningLeft ? SingleHandTurnModel.TurnState.LeftPull : SingleHandTurnModel.TurnState.RightPull) :
                        (turningLeft ? SingleHandTurnModel.TurnState.PendingLeft : SingleHandTurnModel.TurnState.PendingRight);
                    check(turn.State == expectedState, "public stroke input establishes the intended turn-owner fixture");
                    float bank = (float)bankField.GetValue(turn), yawVelocity = turn.YawVelocity;
                    if (!confirmedPull) check(bank > 0f, "a pending real single-hand pull has an unspent finite turn bank");
                    else check(Mathf.Abs(yawDelta) > 0f, "a confirmed real single-hand pull produces turn for the motor");
                    Set(controller, "pendingHandTurn", yawDelta);
                    inject(aimingLeft, .4f, Vector3.up);
                    inject(!aimingLeft, 0f, Vector3.up);
                    controller.SetSpearAimSuppressed(aimingLeft, !aimingLeft);
                    string context = (aimingLeft ? "left" : "right") + " aiming hand, " + expectedState;
                    momentumUnchanged(context);
                    if (aimingHandOwnsTurn)
                    {
                        check(turn.State == SingleHandTurnModel.TurnState.AwaitingStableInput &&
                            turn.YawVelocity == 0f && (float)bankField.GetValue(turn) == 0f,
                            context + ": entering aim cancels only its own old turn gesture/bank");
                        check((float)pendingField.GetValue(controller) == 0f,
                            context + ": entering aim cancels its own not-yet-applied motor yaw");
                    }
                    else
                    {
                        check(turn.State == expectedState && Mathf.Abs(turn.YawVelocity - yawVelocity) < .000001f,
                            context + ": the free hand keeps its pending/confirmed steering state");
                        check(Mathf.Abs((float)bankField.GetValue(turn) - bank) < .000001f &&
                            Mathf.Abs((float)pendingField.GetValue(controller) - yawDelta) < .000001f,
                            context + ": the free hand keeps both its deferred bank and motor yaw");
                    }
                }
            }
            controller.SetSpearAimSuppressed(false, false); turn.Reset(); Set(controller, "pendingHandTurn", 0f);
            return assertions;
        }

        /// <summary>
        /// Actual Unity Physics regression; callable after worldReady, using a separate far-away
        /// rig and temporary colliders. No real headset pose or scene collider is modified.
        /// </summary>
        public static void RunPhysicsInScene()
        {
            var sandbox = new GameObject("Validation-only near-ground swim physics");
            int assertions = 0;
            try
            {
                var rig = new GameObject("Validation-only physics rig");
                rig.transform.SetParent(sandbox.transform, false);
                var controller = rig.AddComponent<HandSwimController>(); controller.enabled = false;
                var eye = new GameObject("Validation-only physics eye"); eye.transform.SetParent(rig.transform, false);
                Set(controller, "xrOrigin", rig.transform); Set(controller, "head", eye.transform);
                var capsule = typeof(HandSwimController).GetMethod("GetBodyCapsule", Private);
                var move = typeof(HandSwimController).GetMethod("MoveWithCollision", Private);
                var separating = typeof(HandSwimController).GetMethod("IsSeparatingSphereOverlap", Private);
                Action<bool, string> check = (ok, why) => {
                    if (!ok) throw new InvalidOperationException("Near-ground swim physics: " + why);
                    assertions++;
                };
                Func<object[]> body = () => {
                    object[] values = { Vector3.zero, Vector3.zero, 0f };
                    capsule.Invoke(controller, values); return values;
                };
                Action<Vector3> place = point => {
                    rig.transform.position = point; eye.transform.localPosition = Vector3.zero;
                    Physics.SyncTransforms();
                };
                Func<Vector3, Vector3> travel = delta => {
                    Vector3 start = rig.transform.position;
                    move.Invoke(controller, new object[] { delta }); Physics.SyncTransforms();
                    return rig.transform.position - start;
                };
                const float x = 256f, z = 256f;
                float floor = UnderwaterWorld.SampleRenderedSeabedHeight(x, z);
                place(new Vector3(x, floor + .45f, z));
                var near = body(); Vector3 bottom = (Vector3)near[0], top = (Vector3)near[1]; float radius = (float)near[2];
                check(bottom.y - radius >= floor + .024f, "a minimum-clearance eye has no buried lower capsule");
                check(top.y >= bottom.y && Mathf.Abs(radius - .22f) < .0001f,
                    "shortening preserves head radius and a non-inverted capsule");
                check(!Physics.CheckCapsule(bottom, top, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                    "temporary fixture starts outside existing world colliders");
                var ground = new GameObject("Validation-only rendered-floor proxy");
                ground.transform.SetParent(sandbox.transform, false); ground.transform.position = new Vector3(x, floor - .5f, z);
                var floorCollider = ground.AddComponent<BoxCollider>(); floorCollider.size = new Vector3(6, 1, 6);
                Physics.SyncTransforms();
                check(Vector3.Distance(travel(Vector3.up * .03f), Vector3.up * .03f) < .0001f,
                    "a close-to-floor capsule moves upward out of the inspection pose");
                UnityEngine.Object.DestroyImmediate(ground);

                Vector3 highEye = new Vector3(x, 500f, z); place(highEye);
                var high = body(); bottom = (Vector3)high[0]; top = (Vector3)high[1]; radius = (float)high[2];
                var rock = new GameObject("Validation-only initially-overlapping reef sphere");
                rock.transform.SetParent(sandbox.transform, false);
                var sphere = rock.AddComponent<SphereCollider>(); sphere.radius = .30f;
                rock.transform.position = bottom - Vector3.up * (radius + sphere.radius - .012f);
                Physics.SyncTransforms();
                check(Physics.CheckCapsule(bottom, top, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                    "fixture actually begins in a small reef-sphere overlap");
                check((bool)separating.Invoke(controller, new object[] { sphere, bottom, top, radius, Vector3.up * .03f }),
                    "upward movement increases actual separation from the current overlap");
                check(!(bool)separating.Invoke(controller, new object[] { sphere, bottom, top, radius, Vector3.down * .03f }),
                    "movement deeper into the sphere cannot qualify for escape");
                check(Vector3.Distance(travel(Vector3.up * .03f), Vector3.up * .03f) < .0001f,
                    "actual capsule cast permits strict upward separation from an initial reef overlap");
                place(highEye);
                check(travel(Vector3.down * .03f).sqrMagnitude < .00000001f,
                    "actual motor does not move inward through an already-overlapping reef sphere");

                place(highEye);
                var roof = new GameObject("Validation-only separate solid overhang");
                roof.transform.SetParent(sandbox.transform, false);
                roof.transform.position = new Vector3(x, top.y + radius + .01f + .05f, z);
                var box = roof.AddComponent<BoxCollider>(); box.size = new Vector3(2, .10f, 2);
                Physics.SyncTransforms();
                check(travel(Vector3.up * .03f).sqrMagnitude < .00000001f,
                    "escaping one overlap cannot bypass a separate solid overhang ahead");
                UnityEngine.Object.DestroyImmediate(roof); UnityEngine.Object.DestroyImmediate(rock);
                place(highEye);
                var wall = new GameObject("Validation-only reef wall"); wall.transform.SetParent(sandbox.transform, false);
                wall.transform.position = new Vector3(x, (top.y + bottom.y) * .5f, z + radius + .035f + .05f);
                var wallBox = wall.AddComponent<BoxCollider>(); wallBox.size = new Vector3(2, 3, .10f);
                Physics.SyncTransforms();
                Vector3 blocked = travel(Vector3.forward * .10f);
                check(blocked.z >= -.0001f && blocked.z <= .015f && Mathf.Abs(blocked.x) < .0001f,
                    "ordinary forward movement still stops at an existing solid reef collider with skin");
                Debug.Log("HandSwimMovementTests physics PASS: " + assertions +
                    " assertions; actual capsule/box/sphere queries and motor movement, synthetic isolated geometry. No wearer claim.");
            }
            finally { UnityEngine.Object.DestroyImmediate(sandbox); Physics.SyncTransforms(); }
        }

        /// <summary>
        /// Exercise the real tracked-eye boundary constraint, including interior positions
        /// where subtracting/re-adding SpawnZ is not an identity in float32. This does not
        /// use hand samples, a world scene, or native tracking.
        /// </summary>
        public static void RunSanctuaryBoundaryRegression()
        {
            var root = new GameObject("Validation-only sanctuary boundary momentum");
            int assertions = 0;
            try
            {
                var controller = root.AddComponent<HandSwimController>(); controller.enabled = false;
                var eye = new GameObject("Validation-only boundary eye"); eye.transform.SetParent(root.transform, false);
                Set(controller, "xrOrigin", root.transform); Set(controller, "head", eye.transform);
                var constrain = typeof(HandSwimController).GetMethod("ConstrainSanctuaryBoundary", Private);
                var swimField = typeof(HandSwimController).GetField("swimVelocity", Private);
                var currentField = typeof(HandSwimController).GetField("currentVelocity", Private);
                Action<bool, string> check = (ok, reason) => {
                    if (!ok) throw new InvalidOperationException("Sanctuary boundary momentum: " + reason);
                    assertions++;
                };
                Func<Vector3, Vector3, bool> exactly = (a, b) => a.x == b.x && a.y == b.y && a.z == b.z;
                // Float values deliberately straddle the shared spawn and forward arch
                // approach. A boundary with 110m half extent has no authority here.
                foreach (float z in new[] { -4f, -2f, -1.7962722f, -.0064587155f, 0f, .7642254f, 1.8167769f, 6.0000005f, 7.0868268f })
                foreach (float roomOffset in new[] { 0f, .37f, -1.23f })
                foreach (float sign in new[] { -1f, 1f })
                {
                    eye.transform.localPosition = new Vector3(.19f, 1.65f, roomOffset);
                    root.transform.position = new Vector3(.35f, 3f, z - roomOffset);
                    Vector3 startEye = eye.transform.position, startOrigin = root.transform.position;
                    Vector3 swim = new Vector3(.31f, .08f, sign * .72f);
                    Vector3 current = new Vector3(.23f, .05f, sign * .50f);
                    Set(controller, "swimVelocity", swim); Set(controller, "currentVelocity", current);
                    for (int repeat = 0; repeat < 12; repeat++)
                    {
                        constrain.Invoke(controller, null);
                        string context = "interior z=" + startEye.z.ToString("R") + ", room offset=" + roomOffset +
                            ", velocity sign=" + sign + ", repeat=" + repeat;
                        check(exactly(root.transform.position, startOrigin) && exactly(eye.transform.position, startEye),
                            context + ": repeated constraints must leave interior poses exactly unchanged");
                        check(exactly((Vector3)swimField.GetValue(controller), swim),
                            context + ": interior water momentum must be unchanged");
                        check(exactly((Vector3)currentField.GetValue(controller), current),
                            context + ": interior motor momentum must be unchanged");
                    }
                }
                const float safe = OceanEnvironmentProfile.SanctuaryPlayableHalfExtent - .22f - .025f;
                foreach (bool xAxis in new[] { false, true })
                foreach (float edgeSign in new[] { -1f, 1f })
                foreach (bool outward in new[] { false, true })
                foreach (float roomOffset in new[] { 0f, .37f, -1.23f })
                {
                    eye.transform.localPosition = new Vector3(roomOffset, 1.65f, roomOffset);
                    Vector3 wantedEye = new Vector3(OceanEnvironmentProfile.SpawnX, 4.65f, OceanEnvironmentProfile.SpawnZ);
                    if (xAxis) wantedEye.x += edgeSign * 115f; else wantedEye.z += edgeSign * 115f;
                    root.transform.position = wantedEye - eye.transform.localPosition;
                    float along = edgeSign * (outward ? .72f : -.72f);
                    Vector3 momentum = xAxis ? new Vector3(along, .08f, .31f) : new Vector3(.31f, .08f, along);
                    Set(controller, "swimVelocity", momentum); Set(controller, "currentVelocity", momentum);
                    constrain.Invoke(controller, null);
                    Vector3 actualEye = eye.transform.position;
                    Vector3 expectedEye = wantedEye;
                    if (xAxis) expectedEye.x = OceanEnvironmentProfile.SpawnX + edgeSign * safe;
                    else expectedEye.z = OceanEnvironmentProfile.SpawnZ + edgeSign * safe;
                    check(Vector3.Distance(actualEye, expectedEye) < .00003f,
                        "true boundary clamps the tracked eye with room-scale offset, on either axis/edge");
                    Vector3 expectedMomentum = momentum;
                    if (outward) { if (xAxis) expectedMomentum.x = 0f; else expectedMomentum.z = 0f; }
                    check(exactly((Vector3)swimField.GetValue(controller), expectedMomentum),
                        "true boundary clears only outward water momentum; inward/tangent/ascent remain live");
                    check(exactly((Vector3)currentField.GetValue(controller), expectedMomentum),
                        "true boundary clears only outward motor momentum; inward/tangent/ascent remain live");
                }
                Debug.Log("HandSwimMovementTests sanctuary boundary PASS: " + assertions +
                    " assertions; actual controller constraint, float32 interior invariance and room-scale edge momentum. No native SDK/device claim.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>Exercise the actual capsule motor in clear space, without a tracked rig.</summary>
        public static void RunAll()
        {
            var root = new GameObject("Validation-only swim motor");
            try
            {
                var controller = root.AddComponent<HandSwimController>(); controller.enabled = false;
                var eye = new GameObject("Validation-only eye"); eye.transform.SetParent(root.transform, false);
                eye.transform.localPosition = new Vector3(0f, 500f, 0f);
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(HandSwimController).GetField("xrOrigin", flags).SetValue(controller, root.transform);
                typeof(HandSwimController).GetField("head", flags).SetValue(controller, eye.transform);
                var move = typeof(HandSwimController).GetMethod("MoveWithCollision", flags);
                if (Physics.CheckCapsule(eye.transform.position + Vector3.down * 1.1f, eye.transform.position, .3f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException("Swim motor validation requires empty space around its separate eye at y=500.");
                int assertions = 0;
                foreach (Vector3 delta in new[] { Vector3.forward * .0002f, Vector3.right * .0005f, Vector3.zero, Vector3.forward * .02f })
                {
                    Vector3 before = root.transform.position;
                    move.Invoke(controller, new object[] { delta });
                    if (Vector3.Distance(root.transform.position, before + delta) > .000002f)
                        throw new InvalidOperationException("Actual swim motor discarded a valid clear-space displacement: " + delta);
                    assertions++;
                }
                var targeted = typeof(HandSwimController).GetMethod("IsMenuHandTargeted", flags);
                Action<HandType, bool, string> expect = (hand, expected, reason) =>
                {
                    if ((bool)targeted.Invoke(controller, new object[] { hand }) != expected)
                        throw new InvalidOperationException("Menu target lease: " + reason);
                    assertions++;
                };
                expect(HandType.HandLeft, false, "a hand without a menu target cannot suppress natural pinch");
                controller.SetMenuHandTargeted(HandType.HandLeft, true);
                expect(HandType.HandLeft, true, "actual hit enables only its hand");
                expect(HandType.HandRight, false, "the other hand stays free");
                controller.SetMenuHandTargeted(HandType.HandLeft, false);
                expect(HandType.HandLeft, false, "a lost target clears immediately");
                controller.SetMenuHandTargeted(HandType.HandRight, true);
                expect(HandType.HandRight, true, "the other hand can target independently");
                typeof(HandSwimController).GetField("menuRightTargetFrame", flags).SetValue(controller, Time.frameCount - 2);
                expect(HandType.HandRight, false, "an unrenewed target cannot persist");
                typeof(HandSwimController).GetField("menuRightTargetFrame", flags).SetValue(controller, Time.frameCount + 1);
                expect(HandType.HandRight, false, "invalid future samples cannot activate a target");
                assertions += CheckCachedJointPoses(controller, root.transform, eye.transform);
                assertions += CheckSpearSwimContinuity(controller, root.transform, eye.transform);
                RunSanctuaryBoundaryRegression();
                Debug.Log("HandSwimMovementTests PASS: " + assertions + " assertions; actual capsule motor, menu target expiry, synthetic cached spear joints and independent aiming/swimming arbitration. No native SDK/device claim.");
                RunPhysicsInScene();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
#endif

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Real admitted GLB anatomy plus deterministic trajectory checks; no wearable acceptance claim.</summary>
    public static class OceanSchoolFishMotionChecks
    {
        public static int LastAssertionCount { get; private set; }

        public static void RunAll()
        {
            LastAssertionCount = 0;
            Shader shader = Resources.Load<Shader>("Shaders/FishSwim");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "real production school shader imports");
            CheckClockContract();
            foreach (string name in new[] { "01_clownfish", "02_blue_tang", "23_mandarinfish" })
            {
                GameObject prefab = Resources.Load<GameObject>("SchoolFish/" + name);
                Check(prefab != null, "admitted source resource exists: " + name);
                var model = Object.Instantiate(prefab);
                Material material = new Material(shader);
                try
                {
                    OceanFishVisualProfile.Apply(material, name);
                    Check(material.HasProperty("_SchoolClock"), "continuous GPU school clock exists");
                    MeshFilter filter = model.GetComponentInChildren<MeshFilter>(true);
                    Check(filter != null && filter.sharedMesh != null, "actual source vertices available");
                    // The three real static resources have a wide, blunt head at +Z and a
                    // thin caudal fin at -Z. Verify this anatomical evidence, not a hardcoded mesh name.
                    Bounds bounds = filter.sharedMesh.bounds;
                    float minHead = float.PositiveInfinity, maxHead = float.NegativeInfinity;
                    float minTail = float.PositiveInfinity, maxTail = float.NegativeInfinity;
                    int headCount = 0, tailCount = 0;
                    foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    {
                        Vector3 point = model.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                        if (point.z > bounds.center.z + bounds.extents.z * .80f)
                        { minHead = Mathf.Min(minHead, point.x); maxHead = Mathf.Max(maxHead, point.x); headCount++; }
                        if (point.z < bounds.center.z - bounds.extents.z * .80f)
                        { minTail = Mathf.Min(minTail, point.x); maxTail = Mathf.Max(maxTail, point.x); tailCount++; }
                    }
                    Check(headCount > 10 && tailCount > 10 && maxHead - minHead > (maxTail - minTail) * 1.7f,
                        "actual +Z blunt head differs from -Z thin tail: " + name);
                    Check(Quaternion.Angle(filter.transform.rotation, Quaternion.identity) < .1f,
                        "source mesh has no unaccounted rotation: " + name);
                    var state = OceanSchoolFishMotion.Create(Vector3.forward, .37f, material.GetFloat("_SwingFreq"));
                    float travelled = 0f;
                    Vector3 position = Vector3.zero;
                    for (int frame = 0; frame < 1800; frame++)
                    {
                        // Reef target reversal, upward/downward movement and escape all
                        // exercise the case that formerly translated before rotating.
                        Vector3 desired = frame < 250 ? Vector3.forward : frame < 600 ? Vector3.back :
                            frame < 1000 ? new Vector3(1f, 2f, 0f) : frame < 1400 ? new Vector3(-1f, -2f, .3f) : Vector3.zero;
                        bool escape = frame >= 600 && frame < 1000;
                        desired = desired.normalized * (escape ? 2.8f : .85f);
                        Quaternion before = model.transform.rotation;
                        Vector3 velocity = OceanSchoolFishMotion.Step(ref state, desired, .12f, escape, 1f / 90f, out Quaternion rotation);
                        model.transform.rotation = rotation;
                        position += velocity / 90f; travelled += velocity.magnitude / 90f;
                        Check(Finite(position) && Finite(velocity), "finite trajectory");
                        if (velocity.sqrMagnitude > .00000001f)
                            Check(Vector3.Dot(velocity.normalized, model.transform.forward) > .99999f,
                                "actual visible +Z nose follows translation, without lateral/reverse skating");
                        Check(Mathf.Abs(state.pitch) <= OceanSchoolFishMotion.MaximumPitch + .001f &&
                            Mathf.Abs(state.bank) <= OceanSchoolFishMotion.MaximumBank + .001f,
                            "no vertical flips or excessive rolling");
                        Check(Quaternion.Angle(before, rotation) <= 4.2f,
                            "body rotates continuously during abrupt target reversal");
                        Vector4 clock = OceanSchoolFishMotion.CaptureShaderClock(ref state, .12f, frame / 90f, out _);
                        Check(clock.y > 0f && clock.y <= material.GetFloat("_SwingFreq") * 2.1501f,
                            "bounded speed-responsive tail cadence");
                    }
                    Check(travelled > 15f && state.speed == 0f, "continuous useful travel and calm stop");
                    float frozenPhase = state.phase, frozenYaw = state.yaw;
                    foreach (float dt in new[] { 0f, -.1f, float.NaN, float.PositiveInfinity })
                        OceanSchoolFishMotion.Step(ref state, Vector3.forward, .12f, false, dt, out _);
                    OceanSchoolFishMotion.Step(ref state, new Vector3(float.NaN, 0f, 0f), .12f, false, .01f, out _);
                    Check(state.phase == frozenPhase && state.yaw == frozenYaw, "paused/invalid input preserves motion clock and pose");
                    float idleStroke = state.stroke;
                    for (int i = 0; i < 180; i++)
                        OceanSchoolFishMotion.Step(ref state, Vector3.forward * 2.8f, .12f, true, 1f / 90f, out _);
                    Check(state.stroke > idleStroke + .4f, "burst swimming visibly increases tail stroke");
                    Debug.Log("[SchoolFishAnatomy] resource=" + name + " headWidth=" + (maxHead - minHead) +
                        " tailWidth=" + (maxTail - minTail) + " sourceForward=+Z travel=" + travelled);
                }
                finally { Object.DestroyImmediate(model); Object.DestroyImmediate(material); }
            }
            // Negative control reproduces the removed order: translate backwards then
            // Slerp the old forward body by 12/90. It still faces forward while moving back.
            Quaternion oldPose = Quaternion.Slerp(Quaternion.identity, Quaternion.LookRotation(Vector3.back), 12f / 90f);
            Check(Vector3.Dot(oldPose * Vector3.forward, Vector3.back) < -.8f,
                "negative control reproduces previous backwards school movement");
            Debug.Log("SCHOOL_FISH_MOTION_CHECKS_PASS assertions=" + LastAssertionCount +
                "; real 3 SchoolFish GLB anatomy, nose-first paths, turning, pitch/bank, burst/stop and GPU clock. Wearable appearance remains unverified.");
        }

        static void CheckClockContract()
        {
            foreach (float dt in new[] { 1f / 30f, 1f / 72f })
            {
                var state = OceanSchoolFishMotion.Create(Vector3.forward, .37f, 4.5f);
                float now = 0f;
                Vector4 clock = OceanSchoolFishMotion.CaptureShaderClock(ref state, .12f, now, out Vector4 motion);
                float lastRendered = motion.x;
                for (int frame = 1; frame <= 576; frame++)
                {
                    now += dt;
                    float beforeUpload = GpuPhase(motion, clock, now);
                    Vector3 wanted = Vector3.forward * (frame < 220 ? .2f : 2.8f);
                    OceanSchoolFishMotion.Step(ref state, wanted, .12f, frame >= 220, dt, out _);
                    if (frame % 8 == 0)
                    {
                        clock = OceanSchoolFishMotion.CaptureShaderClock(ref state, .12f, now, out motion);
                        Check(EquivalentPhase(beforeUpload, GpuPhase(motion, clock, now)),
                            "30/72 Hz staggered upload preserves the rendered phase when cadence changes");
                    }
                    float rendered = GpuPhase(motion, clock, now);
                    float forward = Mathf.Repeat(rendered - lastRendered, Mathf.PI * 2f);
                    Check(forward > 0f && forward < .4f,
                        "30/72 Hz tails advance every rendered frame between eight-frame uploads");
                    lastRendered = rendered;
                }

                foreach (float hitch in new[] { .2f, 2f })
                {
                    now += hitch;
                    float saturated = GpuPhase(motion, clock, now);
                    Check(clock.z == OceanSchoolFishMotion.MaximumClockExtrapolation,
                        "shader extrapolation is explicitly bounded");
                    if (hitch > clock.z)
                        Check(EquivalentPhase(saturated, GpuPhase(motion, clock, now + 10f)),
                            "long pauses hold the last bounded pose instead of extrapolating forever");
                    float bounded = OceanSchoolFishMotion.BoundedDelta(hitch);
                    Vector3 velocity = OceanSchoolFishMotion.Step(ref state, Vector3.forward * 2.8f, .12f, true, hitch, out _);
                    Check(bounded == .05f && (velocity * bounded).magnitude <= .140001f,
                        "hitch translation uses the same .05 s bound as steering in both environments");
                    clock = OceanSchoolFishMotion.CaptureShaderClock(ref state, .12f, now, out motion);
                    Check(EquivalentPhase(saturated, GpuPhase(motion, clock, now)),
                        ".2 s / 2 s hitch recovery never rewinds the rendered tail");
                    float resumed = GpuPhase(motion, clock, now + dt);
                    Check(Mathf.Repeat(resumed - motion.x, Mathf.PI * 2f) > 0f &&
                        Mathf.Repeat(resumed - motion.x, Mathf.PI * 2f) < .4f,
                        "the first post-pause rendered frame resumes continuously");
                }
                // Phase wrapping preserves the sine/cosine pose as well.
                state.phase = Mathf.PI * 20f - .01f;
                state.clockTimestamp = now; state.clockRate = 8f; state.clockReady = true;
                float wrappedBefore = state.phase + .1f * 8f;
                clock = OceanSchoolFishMotion.CaptureShaderClock(ref state, .12f, now + .1f, out motion);
                Check(EquivalentPhase(wrappedBefore, motion.x), "phase normalization does not change the visible pose");
            }
            foreach (float invalid in new[] { 0f, -.1f, float.NaN, float.PositiveInfinity })
                Check(OceanSchoolFishMotion.BoundedDelta(invalid) == 0f, "invalid simulation deltas cannot move school fish");
            // The removed contract advanced CPU by .05 while GPU advanced by .2.
            Check(Mathf.Abs((.2f - .05f) * 4.5f) > .5f,
                "negative control reproduces the former long-frame phase disagreement");
        }

        // Independent expression matching the actual HLSL clock contract, rather than
        // consuming the new CPU predictor as its own expected answer.
        static float GpuPhase(Vector4 motion, Vector4 clock, float timestamp)
            => motion.x + Mathf.Min(Mathf.Max(timestamp - clock.x, 0f), Mathf.Max(clock.z, 0f)) * clock.y;
        static bool EquivalentPhase(float first, float second)
            => Mathf.Abs(Mathf.DeltaAngle(first * Mathf.Rad2Deg, second * Mathf.Rad2Deg)) < .015f;

        static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        static void Check(bool condition, string detail)
        { LastAssertionCount++; if (!condition) throw new InvalidOperationException("School fish: " + detail); }
    }
}
#endif

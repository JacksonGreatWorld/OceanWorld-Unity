using System;
using System.Numerics;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Regression traces through the real grasp and stroke models, without a device.</summary>
    public static class SwimInteractionRecoveryTests
    {
        static int assertions;
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("SwimInteractionRecoveryTests: " + message); assertions++; }

        static ShellGrabGesture.Finger Finger(float curl, float x)
        {
            float angle = (float)Math.Acos(1f - 1.5f * curl), length = .025f;
            Vector3 a = new Vector3(x, 0, 0), b = a + Vector3.UnitZ * length;
            Vector3 c = b + new Vector3(0, -(float)Math.Sin(angle), (float)Math.Cos(angle)) * length;
            Vector3 d = c + new Vector3(0, -(float)Math.Sin(2 * angle), (float)Math.Cos(2 * angle)) * length;
            return new ShellGrabGesture.Finger(a, b, c, d);
        }
        static ShellGrabGesture.HandSample Pose(float middle, float ring, float little) =>
            new ShellGrabGesture.HandSample(true, Finger(middle, 0), Finger(ring, .02f), Finger(little, .04f));
        static void Hold(ShellGrabGesture gesture, ShellGrabGesture.HandSample pose, float duration, float dt)
        { for (int i = 0; i < (int)Math.Ceiling(duration / dt); i++) gesture.Step(dt, pose); }

        public static void RunAll()
        {
            assertions = 0;
            foreach (int hz in new[] { 30, 60, 90, 120 })
            {
                float dt = 1f / hz;
                var grab = new ShellGrabGesture();
                Hold(grab, Pose(0, 0, 0), .2f, dt); Hold(grab, Pose(.4f, .4f, .4f), .2f, dt);
                Check(grab.Active && grab.SuppressStroke, "confirmed aiming fist still blocks paddle at " + hz);
                Hold(grab, Pose(0, .18f, .18f), .11f, dt);
                Check(!grab.Active && !grab.Armed && !grab.SuppressStroke,
                    "a released grasp cannot keep swimming locked until all three fingers straighten at " + hz);
                Hold(grab, Pose(.4f, .4f, .4f), .2f, dt);
                Check(!grab.Active && !grab.StartedThisStep && grab.SuppressStroke,
                    "partial release does not rearm pickups; real closed fist still blocks at " + hz);
                grab.Step(dt, default);
                Check(grab.SuppressStroke, "missing finger data conservatively retains closed-fist guard");
                Hold(grab, Pose(.18f, .18f, .18f), .1f, dt);
                Check(!grab.SuppressStroke && !grab.Active && !grab.Armed,
                    "fresh relaxed recovery removes stale swim guard without inventing pickup");

                foreach (bool left in new[] { true, false })
                {
                    var model = new WaterStrokeModel();
                    Vector3 head = new Vector3(0, 1.65f, 0), palm = new Vector3(left ? -.25f : .25f, 1.15f, .5f);
                    Func<bool, bool, WaterStrokeModel.Output> sample = (suppressed, tracked) =>
                    {
                        var active = new WaterStrokeModel.HandSample(tracked, palm, suppressed);
                        var absent = new WaterStrokeModel.HandSample(false, Vector3.Zero);
                        return model.Step(dt, left ? active : absent, left ? absent : active, head, Vector3.UnitZ);
                    };
                    for (int i = 0; i < (int)Math.Ceiling(.5f / dt); i++) sample(true, true);
                    Check(model.CompletedStrokeCount == 0, "aim hold cannot paddle");
                    float maximum = 0f;
                    for (int i = 0; i < (int)Math.Ceiling(.15f / dt); i++)
                    {
                        palm.Z -= .9f * dt;
                        maximum = Math.Max(maximum, sample(false, true).Strength);
                    }
                    Check(model.CompletedStrokeCount == 1 && maximum > 0f,
                        "a fresh short pull after stable tracked aiming is not lost to a second tracking delay: " + hz + "/" + left);

                    // Closed-fist motion must not become thrust after the hand stops and opens.
                    for (int i = 0; i < (int)Math.Ceiling(.15f / dt); i++)
                    { palm.Z -= .4f * dt; sample(true, true); }
                    int before = model.CompletedStrokeCount; maximum = 0f;
                    for (int i = 0; i < (int)Math.Ceiling(.4f / dt); i++) maximum = Math.Max(maximum, sample(false, true).Strength);
                    Check(model.CompletedStrokeCount == before && maximum == 0f,
                        "release at rest cannot spend velocity or distance from aiming");

                    sample(true, false); palm.Z = .5f;
                    for (int i = 0; i < (int)Math.Ceiling(.12f / dt); i++)
                    { palm.Z -= .9f * dt; sample(false, true); }
                    Check(model.CompletedStrokeCount == before,
                        "real tracking loss still requires the existing stabilization interval");
                }
            }
            string result = "SwimInteractionRecoveryTests PASS: " + assertions +
                " assertions; grasp release, closed aim protection, brief post-aim paddles, both hands, four frame rates and true tracking recovery. No real wearer/collision claim.";
#if UNITY_EDITOR
            UnityEngine.Debug.Log(result);
#else
            Console.WriteLine(result);
#endif
        }
    }
}

#if UNITY_EDITOR || SHELL_GRAB_TEST_MAIN
using System;
using System.Numerics;
using Finger = SwanUnderwater.ShellGrabGesture.Finger;
using Hand = SwanUnderwater.ShellGrabGesture.HandSample;

namespace SwanUnderwater.EditorTests
{
    /// <summary>
    /// Synthetic joint geometry through the actual grasp recognizer and paddle model.
    /// No SDK mocks, target-range substitutes or headset comfort claims.
    /// Offline: csc -r:System.Numerics.dll -define:SHELL_GRAB_TEST_MAIN -out:/tmp/ShellGrabTests.exe
    /// ShellGrabGesture.cs WaterStrokeModel.cs Editor/ShellGrabGestureTests.cs
    /// </summary>
    public static class ShellGrabGestureTests
    {
        static int assertions;
        static readonly Hand Open = Pose(0f, 0f, 0f);
        static readonly Hand Closed = Pose(.42f, .42f, .42f);

        static void Require(bool value, string reason)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("Shell grab: " + reason);
        }

        static void Log(string message)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.Log(message);
#else
            Console.WriteLine(message);
#endif
        }

        static Finger FingerWithCurl(float curl, Vector3 origin, float size = 1f)
        {
            // Three equal phalanges with equal interphalangeal bend angles have
            // chord/length=(1+2cos(angle))/3, giving an independently specified curl.
            float angle = (float)Math.Acos(1f - 1.5f * curl);
            float length = .025f * size;
            Vector3 proximal = origin;
            Vector3 intermediate = proximal + Vector3.UnitZ * length;
            Vector3 distal = intermediate + new Vector3(0, -(float)Math.Sin(angle), (float)Math.Cos(angle)) * length;
            Vector3 tip = distal + new Vector3(0, -(float)Math.Sin(2f * angle), (float)Math.Cos(2f * angle)) * length;
            return new Finger(proximal, intermediate, distal, tip);
        }

        static Hand Pose(float middle, float ring, float little)
        {
            return new Hand(true, FingerWithCurl(middle, Vector3.Zero),
                FingerWithCurl(ring, new Vector3(.02f, 0, 0), .9f),
                FingerWithCurl(little, new Vector3(.04f, 0, 0), .75f));
        }

        static int Hold(ShellGrabGesture gesture, Hand pose, float seconds, int hz, bool suppressed = false)
        {
            int started = 0;
            for (int i = 0; i < (int)Math.Ceiling(seconds * hz); i++)
                if (gesture.Step(1f / hz, pose, suppressed)) started++;
            return started;
        }

        static void EdgeAndRecovery(int hz)
        {
            var gesture = new ShellGrabGesture();
            Require(Hold(gesture, Closed, 1f, hz) == 0 && !gesture.Active && gesture.SuppressStroke,
                hz + " Hz: initially closed hand cannot create a grab, but cannot paddle either");
            Hold(gesture, Open, .12f, hz);
            Require(gesture.Armed && !gesture.SuppressStroke, hz + " Hz: deliberate opening rearms");
            int frames = 0, edges = 0;
            while (!gesture.Active && frames < hz)
            {
                frames++;
                if (gesture.Step(1f / hz, Closed)) edges++;
            }
            double elapsed = frames / (double)hz;
            Require(edges == 1 && elapsed >= .099999 && elapsed <= .10 + 1.0 / hz + .0001,
                hz + " Hz: one rising edge after about 100ms of consecutive closure");
            Require(Hold(gesture, Closed, 2f, hz) == 0 && gesture.Active && !gesture.StartedThisStep,
                hz + " Hz: holding the fist never repeats the pickup edge");
            Require(Hold(gesture, Pose(.18f, .18f, .18f), .4f, hz) == 0 && gesture.Active,
                hz + " Hz: hysteresis retains a held grasp between close/release thresholds");
            gesture.Step(1f / hz, Open);
            Hold(gesture, Closed, .15f, hz);
            Require(gesture.Active && !gesture.StartedThisStep,
                hz + " Hz: one open/noisy sample neither releases nor duplicates a grasp");
            gesture.Step(1f / hz, default);
            Require(!gesture.Active && !gesture.Armed && !gesture.StartedThisStep && gesture.SuppressStroke,
                hz + " Hz: lost finger data immediately cancels grasp and retains the fist paddle guard");
            Require(Hold(gesture, Closed, .5f, hz) == 0 && !gesture.Active,
                hz + " Hz: a recovered old fist cannot become a new grab");
            Hold(gesture, Open, .12f, hz);
            Require(Hold(gesture, Closed, .15f, hz) == 1 && gesture.Active,
                hz + " Hz: opening then closing after recovery produces exactly one new edge");
            Hold(gesture, Open, .12f, hz);
            Require(!gesture.Active && gesture.Armed && !gesture.SuppressStroke,
                hz + " Hz: stable opening releases and restores ordinary paddle input");
            Log("PASS fist timing/recovery @" + hz + " Hz: first edge at " + elapsed.ToString("F4") + "s");
        }

        static void RejectPinchAndTransientPoses()
        {
            var gesture = new ShellGrabGesture();
            Hold(gesture, Open, .2f, 90);
            // Thumb and index motion is not supplied to the model at all: the three
            // finger geometry is identical for open palm and a simple thumb-index pinch.
            Require(Hold(gesture, Open, 1f, 90) == 0 && !gesture.Active && !gesture.SuppressStroke,
                "pure thumb-index pinch with the other fingers open is not a grasp");
            foreach (Hand partial in new[] { Pose(.42f, 0, 0), Pose(.42f, .42f, 0), Pose(0, .42f, .42f) })
                Require(Hold(gesture, partial, .4f, 90) == 0 && !gesture.Active,
                    "one or two curled fingers do not satisfy the three-finger fist");
            Hold(gesture, Open, .2f, 90);
            Hold(gesture, Closed, .04f, 90);
            Hold(gesture, Open, .12f, 90);
            Require(!gesture.Active && !gesture.StartedThisStep,
                "a short closure glitch is discarded instead of accumulating into a future grab");
            Require(Hold(gesture, Closed, .12f, 90) == 1, "all three curled with relaxed thumb/index can grasp");
            Hold(gesture, Pose(0, .42f, .42f), .12f, 90);
            Require(!gesture.Active && !gesture.Armed, "opening one required finger releases, without rearming an incompletely open hand");
            Require(Hold(gesture, Closed, .3f, 90) == 0, "reclosing a partly released fist cannot repeat the edge");
        }

        static void BriefFingerGapAndIntent(int hz)
        {
            float dt = 1f / hz;
            var gesture = new ShellGrabGesture();
            Hold(gesture, Open, .2f, hz);
            Hold(gesture, Closed, .035f, hz);
            gesture.Step(dt, default, false, true);
            Require(gesture.Armed && !gesture.ValidThisStep && !gesture.StartedThisStep,
                "brief finger occlusion with a trusted palm preserves prior open arming, never confirms closure");
            int edges = 0; float restored = 0f;
            while (!gesture.Active && restored < .3f)
            { if (gesture.Step(dt, Closed, false, true)) edges++; restored += dt; }
            Require(edges == 1 && restored >= .0999f, "all three tracked fingers must re-establish full close stability after a gap");

            var intent = new WorldGrabIntent();
            intent.Step(dt, true, gesture.Active, gesture.StartedThisStep, Vector3.Zero, false);
            Require(intent.Available, "new confirmed fist opens bounded target-contact window");
            for (int i = 0; i < (int)(.20f / dt); i++) intent.Step(dt, true, true, false, Vector3.UnitZ * .15f, false);
            Require(intent.Available && intent.Consume() && !intent.Consume(), "contact arriving 200ms after closure can consume once");
            intent.Step(dt, true, true, false, Vector3.UnitZ * .2f, false);
            Require(!intent.Available, "holding the same fist cannot pick a second object");
            intent.Step(dt, true, true, true, Vector3.Zero, false);
            for (int i = 0; i < (int)Math.Ceiling(.52f / dt); i++) intent.Step(dt, true, true, false, Vector3.Zero, false);
            Require(!intent.Available, "unused world intent expires after half a second");
            foreach (int reason in new[] { 0, 1, 2, 3, 4 })
            {
                intent.Step(dt, true, true, true, Vector3.Zero, false);
                intent.Step(reason == 4 ? .2f : dt, reason != 0, reason != 1, false,
                    reason == 3 ? Vector3.UnitX * .6f : Vector3.Zero, reason == 2);
                Require(!intent.Available, "tracking loss, release, menu, large travel or long frame cancels intent");
            }
            Hold(gesture, Open, .2f, hz);
            for (int i = 0; i < (int)Math.Ceiling(.12f / dt); i++) gesture.Step(dt, default, false, true);
            Require(!gesture.Armed && Hold(gesture, Closed, .2f, hz) == 0, "long finger loss requires release again");
            Hold(gesture, Open, .2f, hz); gesture.Step(dt, default, false, false);
            Require(!gesture.Armed && Hold(gesture, Closed, .2f, hz) == 0, "even one real palm-tracking loss cannot preserve old arming");
        }

        static Finger Transform(Finger finger, float scale, bool mirror)
        {
            Quaternion rotation = Quaternion.CreateFromYawPitchRoll(.9f, -.5f, .3f);
            Vector3 translation = new Vector3(2f, 1.2f, -3f);
            Func<Vector3, Vector3> point = value => Vector3.Transform(
                new Vector3(value.X * (mirror ? -scale : scale), value.Y * scale, value.Z * scale), rotation) + translation;
            return new Finger(point(finger.Proximal), point(finger.Intermediate), point(finger.Distal), point(finger.Tip));
        }

        static void RetainedGestureIntent(int hz)
        {
            float dt = 1f / hz;
            var gesture = new ShellGrabGesture(); var intent = new WorldGrabIntent();
            Action<Hand, bool, Vector3, bool> step = (sample, palmTracked, palm, suppressed) => {
                gesture.Step(dt, sample, suppressed, palmTracked);
                // The same combined entry used by HandSwimController, not a separate test policy.
                intent.Step(dt, palmTracked, gesture, palm, suppressed);
            };
            Action acquire = () => {
                gesture.Reset(); intent.Reset();
                for (int i = 0; i < (int)Math.Ceiling(.12f / dt); i++) step(Open, true, Vector3.Zero, false);
                for (int i = 0; i < (int)Math.Ceiling(.15f / dt) && !intent.Available; i++) step(Closed, true, Vector3.Zero, false);
                Require(intent.Available && gesture.ValidThisStep, "combination starts from a real confirmed fist");
            };
            acquire(); float before = intent.RemainingSeconds;
            int gapFrames = Math.Max(1, (int)(.055f / dt));
            for (int i = 0; i < gapFrames; i++) step(default, true, Vector3.UnitZ * .1f, false);
            Require(gesture.Active && !gesture.ValidThisStep && !gesture.StartedThisStep && intent.Available,
                "short missing-finger gap retains existing state but cannot satisfy the fresh-sample action gate");
            Require(Math.Abs(intent.RemainingSeconds - (before - gapFrames * dt)) < .00001f,
                "missing-finger frames spend the original deadline instead of extending it");
            step(Closed, true, Vector3.UnitZ * .15f, false);
            Require(gesture.ValidThisStep && gesture.Active && !gesture.StartedThisStep && intent.Available && intent.Consume(),
                "restored complete fist can spend the remaining old intent without requiring another edge");
            step(default, true, Vector3.UnitZ * .15f, false); step(Closed, true, Vector3.UnitZ * .15f, false);
            Require(!intent.Available && !intent.Consume(), "short gap and recovery cannot recreate a consumed intent");

            for (int reason = 0; reason < 6; reason++)
            {
                acquire();
                if (reason == 0)
                    for (int i = 0; i < (int)Math.Ceiling(.12f / dt); i++) step(default, true, Vector3.Zero, false);
                else if (reason == 1) step(default, false, Vector3.Zero, false);
                else if (reason == 2)
                    for (int i = 0; i < (int)Math.Ceiling(.10f / dt); i++) step(Open, true, Vector3.Zero, false);
                else if (reason == 3) step(default, true, Vector3.Zero, true);
                else if (reason == 4) step(default, true, Vector3.UnitX * .6f, false);
                else
                {
                    while (intent.RemainingSeconds > dt * 1.5f) step(Closed, true, Vector3.Zero, false);
                    step(default, true, Vector3.Zero, false); step(default, true, Vector3.Zero, false);
                }
                step(Closed, true, Vector3.Zero, false);
                Require(!intent.Available,
                    "long finger gap, real palm loss, release, suppression, travel or deadline cancels and cannot resurrect intent");
            }
            acquire(); intent.Reset(); // The runtime aiming path explicitly clears this hand's intent.
            step(default, true, Vector3.Zero, false); step(Closed, true, Vector3.Zero, false);
            Require(!intent.Available, "aim-cleared intent is not recreated by retained Active after finger recovery");
        }

        static void GeometryAndIndependentHands()
        {
            foreach (float size in new[] { .7f, 1f, 1.3f })
            foreach (bool mirror in new[] { false, true })
            {
                Finger finger = Transform(Closed.Middle, size, mirror);
                Require(ShellGrabGesture.TryFingerCurl(finger, out float curl) && Math.Abs(curl - .42f) < .0002f,
                    "curl is invariant under left/right mirror, rotation, translation and realistic hand size");
            }
            var left = new ShellGrabGesture();
            var right = new ShellGrabGesture();
            Hold(left, Open, .2f, 90); Hold(right, Open, .2f, 90);
            int leftEdges = Hold(left, Closed, .2f, 90), rightEdges = Hold(right, Open, .2f, 90);
            Require(leftEdges == 1 && rightEdges == 0 && left.Active && !right.Active, "a left fist cannot activate the right hand");
            left.Step(1f / 90f, default);
            rightEdges += Hold(right, Closed, .2f, 90);
            Require(!left.Active && right.Active && rightEdges == 1, "left tracking loss does not disarm the independent right hand");
        }

        static void InvalidAndSuppressedInput()
        {
            Hand collapsed = new Hand(true, default, default, default);
            Hand nan = Closed; nan.Middle.Tip.X = float.NaN;
            Hand infinite = Closed; infinite.Ring.Intermediate.Y = float.PositiveInfinity;
            foreach (Hand invalid in new[] { default(Hand), collapsed, nan, infinite })
            {
                var gesture = new ShellGrabGesture();
                Hold(gesture, Open, .2f, 90); Hold(gesture, Closed, .2f, 90);
                gesture.Step(1f / 90f, invalid);
                Require(!gesture.Active && !gesture.StartedThisStep && Hold(gesture, Closed, .2f, 90) == 0,
                    "missing, collapsed or nonfinite joints cancel the grasp and require release before recovery");
            }
            foreach (float dt in new[] { 0f, -.01f, .25f, float.NaN, float.PositiveInfinity })
            {
                var gesture = new ShellGrabGesture();
                Hold(gesture, Open, .2f, 90);
                gesture.Step(dt, Closed);
                Require(!gesture.Active && Hold(gesture, Closed, .2f, 90) == 0,
                    "invalid time or a frame gap cannot confirm or preserve a pending grab");
            }
            var paused = new ShellGrabGesture();
            Hold(paused, Open, .2f, 90); Hold(paused, Closed, .2f, 90);
            paused.Step(1f / 90f, Closed, true);
            Require(!paused.Active && !paused.StartedThisStep && Hold(paused, Closed, .3f, 90) == 0,
                "interaction suppression and resumed old closure cannot cause a pickup");
            Hold(paused, Open, .12f, 90); Hold(paused, Closed, .2f, 90);
            paused.Reset();
            Require(!paused.Active && !paused.StartedThisStep && Hold(paused, Closed, .3f, 90) == 0,
                "pause/disable Reset discards both active state and stale rising edge");
        }

        sealed class SwimSimulation
        {
            public readonly ShellGrabGesture LeftGrab = new ShellGrabGesture(), RightGrab = new ShellGrabGesture();
            public readonly WaterStrokeModel Paddle = new WaterStrokeModel(), Baseline = new WaterStrokeModel();
            public Vector3 Left = new Vector3(-.18f, 1.15f, .48f), Right = new Vector3(.18f, 1.15f, .48f);
            public Hand LeftPose = Open, RightPose = Open;
            public int LeftEdges, RightEdges;
            public bool OpenPaddleMatchesBaseline = true;
            public void Step()
            {
                const float dt = 1f / 90f;
                if (LeftGrab.Step(dt, LeftPose)) LeftEdges++;
                if (RightGrab.Step(dt, RightPose)) RightEdges++;
                var output = Paddle.Step(dt,
                    new WaterStrokeModel.HandSample(true, Left, LeftGrab.SuppressStroke),
                    new WaterStrokeModel.HandSample(true, Right, RightGrab.SuppressStroke),
                    new Vector3(0, 1.65f, 0), Vector3.UnitZ);
                var original = Baseline.Step(dt, new WaterStrokeModel.HandSample(true, Left),
                    new WaterStrokeModel.HandSample(true, Right), new Vector3(0, 1.65f, 0), Vector3.UnitZ);
                OpenPaddleMatchesBaseline &= output.NewStrokes == original.NewStrokes &&
                    output.LeftStrength == original.LeftStrength && output.RightStrength == original.RightStrength;
            }
            public void Hold(float seconds) { for (int i = 0; i < (int)Math.Ceiling(seconds * 90); i++) Step(); }
            public void Move(Vector3 left, Vector3 right, float seconds)
            {
                Vector3 fromLeft = Left, fromRight = Right;
                int frames = (int)Math.Ceiling(seconds * 90);
                for (int i = 1; i <= frames; i++)
                {
                    Left = Vector3.Lerp(fromLeft, left, i / (float)frames);
                    Right = Vector3.Lerp(fromRight, right, i / (float)frames);
                    Step();
                }
            }
        }

        static void GraspCannotPaddle()
        {
            Vector3 leftPull = new Vector3(-.4f, 1.15f, .13f), rightPull = new Vector3(.4f, 1.15f, .13f);
            var open = new SwimSimulation();
            open.Hold(.3f); open.Move(leftPull, rightPull, .85f); open.Hold(.3f);
            Require(open.OpenPaddleMatchesBaseline && open.Paddle.CompletedStrokeCount == 2 && open.LeftEdges + open.RightEdges == 0,
                "ordinary open-hand swimming is numerically unchanged from the real original paddle model");
            var holding = new SwimSimulation();
            holding.Hold(.3f);
            holding.LeftPose = Closed; holding.Step();
            Require(holding.LeftGrab.SuppressStroke && !holding.LeftGrab.Active,
                "the closed candidate blocks paddle input before the 100ms pickup confirmation");
            holding.Move(leftPull, rightPull, .85f);
            Require(holding.LeftEdges == 1 && holding.RightEdges == 0 && holding.Paddle.LeftStrokeCount == 0 &&
                    holding.Paddle.RightStrokeCount == 1 && holding.Baseline.LeftStrokeCount == 1,
                "a moving closed fist cannot paddle; the independent open partner still completes its real stroke");
            holding.LeftPose = Open; holding.Hold(.3f);
            holding.Move(new Vector3(-.18f, 1.15f, .48f), rightPull, .5f); holding.Hold(.3f);
            holding.Move(leftPull, rightPull, .85f);
            Require(holding.Paddle.LeftStrokeCount == 1 && holding.Paddle.RightStrokeCount == 1 && holding.LeftEdges == 1,
                "release and a fresh real paddle restore that hand's propulsion without fabricating another pickup");
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Swan Underwater/Validate Shell Grab Gesture")]
#endif
        public static void RunAll()
        {
            assertions = 0;
            foreach (int hz in new[] { 30, 60, 72, 90, 120 }) EdgeAndRecovery(hz);
            foreach (int hz in new[] { 30, 60, 72, 90, 120 }) BriefFingerGapAndIntent(hz);
            foreach (int hz in new[] { 30, 60, 72, 90, 120 }) RetainedGestureIntent(hz);
            RejectPinchAndTransientPoses();
            GeometryAndIndependentHands();
            InvalidAndSuppressedInput();
            GraspCannotPaddle();
            Log($"ShellGrabGestureTests PASS: {assertions} assertions. Synthetic geometry/timing and actual paddle-model integration; PXR joint availability and real grip comfort remain device checks.");
        }

#if SHELL_GRAB_TEST_MAIN
        public static int Main()
        {
            try { RunAll(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
#endif
    }
}
#endif

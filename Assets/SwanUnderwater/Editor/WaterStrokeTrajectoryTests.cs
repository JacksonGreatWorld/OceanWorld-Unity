#if UNITY_EDITOR || WATER_STROKE_TEST_MAIN
using System;
using System.Numerics;

namespace SwanUnderwater.EditorTests
{
    /// <summary>
    /// Synthetic trajectory regression checks against the actual runtime recognizer.
    /// No SDK/device mocks: positions are explicit metres in tracking space.
    /// Also runnable with mcs -r:System.Numerics.dll -define:WATER_STROKE_TEST_MAIN.
    /// These checks establish algorithm behavior, not real headset comfort or tracking quality.
    /// </summary>
    public static class WaterStrokeTrajectoryTests
    {
        private sealed class Simulation
        {
            public readonly WaterStrokeModel Model = new WaterStrokeModel();
            public Vector3 Head = new Vector3(0f, 1.65f, 0f);
            public Vector3 Forward = Vector3.UnitZ;
            public Vector3 Left = new Vector3(-0.18f, 1.15f, 0.48f);
            public Vector3 Right = new Vector3(0.18f, 1.15f, 0.48f);
            public bool LeftValid = true, RightValid = true, Suppressed;
            public Vector3 Velocity;
            public float PeakSpeed, PoweredSeconds;
            public readonly float Dt;
            public WaterStrokeModel.Output Last;

            public Simulation(int hz = 90) { Dt = 1f / hz; }

            public void Sample()
            {
                Last = Model.Step(Dt,
                    new WaterStrokeModel.HandSample(LeftValid, Left, Suppressed),
                    new WaterStrokeModel.HandSample(RightValid, Right, Suppressed), Head, Forward);
                Velocity = WaterStrokeModel.AdvanceVelocity(Velocity, Last, Dt, 1.6f, 2.5f, 0.7f);
                PeakSpeed = Math.Max(PeakSpeed, Velocity.Length());
                if (Last.Strength > 0f) PoweredSeconds += Dt;
                Require(!float.IsNaN(Velocity.X) && Velocity.Length() <= 2.5001f, "finite speed capped at 2.5 m/s");
            }

            public void Hold(float seconds)
            {
                int frames = (int)Math.Ceiling(seconds / Dt);
                for (int i = 0; i < frames; i++) Sample();
            }

            public void Move(Vector3 leftTo, Vector3 rightTo, float seconds)
            {
                Vector3 leftFrom = Left, rightFrom = Right;
                int frames = (int)Math.Ceiling(seconds / Dt);
                for (int i = 1; i <= frames; i++)
                {
                    Left = Vector3.Lerp(leftFrom, leftTo, (float)i / frames);
                    Right = Vector3.Lerp(rightFrom, rightTo, (float)i / frames);
                    Sample();
                }
            }
        }

        private static int assertions;
        private static void Require(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("WaterStroke trajectory failed: " + message);
        }

        private static void Report(string name, Simulation simulation)
        {
            string text = $"PASS {name}: pulls={simulation.Model.CompletedStrokeCount}, " +
                $"peak={simulation.PeakSpeed:F3}m/s, powered={simulation.PoweredSeconds:F3}s, " +
                $"Lvalid={simulation.Model.LeftTrackedSeconds:F3}/{simulation.Model.TrackingSampleSeconds:F3}s, " +
                $"Rvalid={simulation.Model.RightTrackedSeconds:F3}/{simulation.Model.TrackingSampleSeconds:F3}s";
#if UNITY_EDITOR
            UnityEngine.Debug.Log(text);
#else
            Console.WriteLine(text);
#endif
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Swan Underwater/Validate Hand Paddle Trajectories")]
#endif
        public static void RunAll()
        {
            assertions = 0;
            UpperBreaststroke();
            PartialTrackingLossContinuity();
            UpwardDownstrokes();
            foreach (int hz in new[] { 60, 72, 90, 120 })
            {
                var slow = new Simulation(hz);
                slow.Hold(0.3f);
                slow.Move(new Vector3(-0.4f, 1.15f, 0.28f), new Vector3(0.4f, 1.15f, 0.28f), 1.2f);
                Require(slow.Model.CompletedStrokeCount == 2, "slow bilateral pulls recognized once per hand");
                Require(slow.Velocity.Z > 1f && Math.Abs(slow.Velocity.X) < 0.001f, "symmetric slow pulls move forward without drift");
                Require(slow.PeakSpeed > 1.4f && slow.PeakSpeed < 1.9f, "slow cruising remains below fast-pull maximum");
                Require(slow.PoweredSeconds > 0.7f, "slow pulls power continuously instead of one impulse");
                Report("slow breaststroke @" + hz, slow);

                var fast = new Simulation(hz);
                fast.Hold(0.3f);
                fast.Move(new Vector3(-0.42f, 1.13f, 0.12f), new Vector3(0.42f, 1.13f, 0.12f), 0.45f);
                Require(fast.Model.CompletedStrokeCount == 2, "fast bilateral pulls recognized");
                Require(fast.Velocity.Z > 1.5f, "fast pull accelerates beyond previous 1.0 m/s setting");
                Require(fast.PeakSpeed > slow.PeakSpeed + 0.4f, "fast and slow strokes have distinct power");
                float afterPull = fast.Velocity.Length();
                fast.Hold(0.2f);
                float afterSettle = fast.Velocity.Length();
                fast.Hold(0.8f);
                float afterGlide = fast.Velocity.Length();
                Require(afterPull > 0.5f && afterGlide > 0.15f && afterGlide < afterSettle, "stopped hands glide and slow down");
                fast.Hold(5f);
                Require(fast.Velocity.Length() < 0.06f && fast.Model.CompletedStrokeCount == 2, "holding still cannot retrigger or sustain movement");
                Report("fast + stop/glide @" + hz, fast);
            }

            var alternating = new Simulation();
            alternating.Hold(0.3f);
            Vector3 leftReach = alternating.Left, rightReach = alternating.Right;
            alternating.Move(new Vector3(-0.38f, 1.15f, 0.2f), alternating.Right, 0.65f);
            Require(alternating.Model.LeftStrokeCount == 1 && alternating.Model.RightStrokeCount == 0, "left hand does not wait for right");
            alternating.Move(leftReach, new Vector3(0.38f, 1.15f, 0.2f), 0.65f);
            Require(alternating.Model.LeftStrokeCount == 1 && alternating.Model.RightStrokeCount == 1, "opposite hand can pull while first recovers");
            alternating.Move(new Vector3(-0.38f, 1.15f, 0.2f), rightReach, 0.65f);
            Require(alternating.Model.LeftStrokeCount == 2 && alternating.Model.RightStrokeCount == 1, "left hand rearms after recovery");
            Report("alternating hands", alternating);

            var one = new Simulation { RightValid = false };
            one.Hold(0.3f);
            one.Move(new Vector3(-0.38f, 1.15f, 0.17f), one.Right, 0.8f);
            Require(one.Model.CompletedStrokeCount == 1 && one.Velocity.Z > 1f, "one tracked hand propels alone");
            Require(one.Model.RightTrackedSeconds == 0 && one.Model.LeftTrackedSeconds > 1f, "SDK validity denominator preserves one-hand loss");
            Report("one hand without other tracking", one);

            var outward = new Simulation();
            outward.Hold(0.3f);
            outward.Move(new Vector3(-0.48f, 1.15f, 0.48f), new Vector3(0.48f, 1.15f, 0.48f), 0.8f);
            Require(outward.Model.CompletedStrokeCount == 2 && outward.Velocity.Z > 0.8f, "outward sweep alone can paddle forward");
            Report("outward-only paddle", outward);

            var sideways = new Simulation();
            sideways.Hold(0.3f);
            sideways.Move(sideways.Left + new Vector3(-0.2f, 0f, -0.3f), sideways.Right + new Vector3(-0.2f, 0f, -0.3f), 0.75f);
            Require(sideways.Velocity.X > 0.08f && sideways.Velocity.Z > Math.Abs(sideways.Velocity.X) * 2f,
                "hand motion steers right with gaze still dominant");
            Report("paddle + gaze direction blend", sideways);

            foreach (float vertical in new[] { -0.5f, 0.5f })
            {
                var climb = new Simulation { Forward = Vector3.Normalize(new Vector3(0f, vertical, 0.866f)) };
                climb.Hold(0.3f);
                climb.Move(climb.Left + new Vector3(-0.2f, -vertical * 0.15f, -0.3f),
                    climb.Right + new Vector3(0.2f, -vertical * 0.15f, -0.3f), 0.8f);
                Require(climb.Velocity.Y * vertical > 0.15f && climb.Velocity.Z > 0.5f, "gaze and paddle allow ascent/descent");
                Report(vertical > 0f ? "ascent" : "descent", climb);
            }

            var yawed = new Simulation();
            var rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI / 2f);
            yawed.Left = Vector3.Transform(yawed.Left, rotate);
            yawed.Right = Vector3.Transform(yawed.Right, rotate);
            yawed.Forward = Vector3.UnitX;
            yawed.Hold(0.3f);
            yawed.Move(Vector3.Transform(new Vector3(-0.4f, 1.15f, 0.2f), rotate),
                Vector3.Transform(new Vector3(0.4f, 1.15f, 0.2f), rotate), 0.8f);
            Require(yawed.Velocity.X > 1f && Math.Abs(yawed.Velocity.Z) < 0.001f, "90-degree tracking coordinates rotate propulsion correctly");
            Report("tracking-space rotation", yawed);

            var lost = new Simulation();
            lost.Hold(0.3f);
            lost.Move(new Vector3(-0.4f, 1.15f, 0.2f), new Vector3(0.4f, 1.15f, 0.2f), 0.6f);
            lost.LeftValid = lost.RightValid = false;
            lost.Sample();
            Require(lost.Velocity.LengthSquared() == 0f && lost.Last.Strength == 0f, "tracking loss clears propulsion immediately");
            int previousCount = lost.Model.CompletedStrokeCount;
            lost.Hold(0.4f);
            lost.Left = leftReach; lost.Right = rightReach;
            lost.LeftValid = lost.RightValid = true;
            lost.Hold(0.1f);
            lost.Move(new Vector3(-0.4f, 1.15f, 0.2f), new Vector3(0.4f, 1.15f, 0.2f), 0.08f);
            Require(lost.Model.CompletedStrokeCount == previousCount && lost.Velocity.LengthSquared() == 0f, "recovery window blocks a false impulse");
            lost.Hold(0.3f);
            lost.Move(leftReach, rightReach, 0.5f);
            lost.Hold(0.2f);
            lost.Move(new Vector3(-0.4f, 1.15f, 0.2f), new Vector3(0.4f, 1.15f, 0.2f), 0.7f);
            Require(lost.Model.CompletedStrokeCount == previousCount + 2 && lost.Velocity.Z > 1f, "normal swimming resumes after reacquisition");
            Report("tracking loss + recovery", lost);

            var raise = new Simulation();
            raise.Hold(0.3f);
            raise.Move(raise.Left + Vector3.UnitY * 0.6f, raise.Right + Vector3.UnitY * 0.6f, 0.6f);
            raise.Hold(0.5f);
            raise.Move(leftReach, rightReach, 0.6f);
            raise.Hold(0.4f);
            Require(raise.Model.CompletedStrokeCount == 0 && raise.PeakSpeed == 0f, "ordinary raise/hold/lower does not paddle");
            // A near-eye UI point followed by retraction stays outside the paddle zone.
            raise.Left = new Vector3(-0.18f, 1.58f, 0.6f);
            raise.Right = new Vector3(0.18f, 1.58f, 0.6f);
            raise.Hold(0.4f);
            raise.Move(raise.Left - Vector3.UnitZ * 0.35f, raise.Right - Vector3.UnitZ * 0.35f, 0.6f);
            Require(raise.Model.CompletedStrokeCount == 0, "near-eye UI retraction does not paddle");
            Report("ordinary UI raise and near-eye reach", raise);

            var ui = new Simulation { Suppressed = true };
            ui.Hold(0.3f);
            ui.Move(new Vector3(-0.4f, 1.15f, 0.2f), new Vector3(0.4f, 1.15f, 0.2f), 0.7f);
            Require(ui.Model.CompletedStrokeCount == 0 && ui.PeakSpeed == 0f, "pinch/UI capture suppresses even paddle-shaped motion");
            Report("pinch / explicit UI suppression", ui);

            var jitter = new Simulation();
            jitter.Hold(0.3f);
            for (int frame = 0; frame < 1800; frame++)
            {
                float offset = (float)Math.Sin(frame * 0.63f) * 0.003f;
                jitter.Left = leftReach + new Vector3(offset, 0f, offset);
                jitter.Right = rightReach - new Vector3(offset, 0f, offset);
                jitter.Forward = new Vector3((float)Math.Sin(frame * 0.006f), 0f, (float)Math.Cos(frame * 0.006f));
                jitter.Sample();
            }
            Require(jitter.Model.CompletedStrokeCount == 0 && jitter.PeakSpeed == 0f, "20 seconds of jitter and head rotation cannot accumulate a pull");
            Report("jitter + head rotation", jitter);

            var bodyMotion = new Simulation();
            bodyMotion.Hold(0.3f);
            for (int frame = 0; frame < 90; frame++)
            {
                Vector3 movement = new Vector3(0f, 0f, -0.4f / 90f);
                bodyMotion.Head += movement;
                bodyMotion.Left += movement;
                bodyMotion.Right += movement;
                bodyMotion.Sample();
            }
            Require(bodyMotion.Model.CompletedStrokeCount == 0 && bodyMotion.PeakSpeed == 0f,
                "physical whole-body translation does not fabricate swimming");
            for (int frame = 0; frame < 90; frame++)
            {
                bodyMotion.Head += new Vector3(0f, 0f, 0.3f / 90f);
                bodyMotion.Sample();
            }
            Require(bodyMotion.Model.CompletedStrokeCount == 0 && bodyMotion.PeakSpeed == 0f,
                "moving only the head with stationary hands cannot paddle");
            Report("physical body translation / head-only lean", bodyMotion);

            var jump = new Simulation();
            jump.Hold(0.3f);
            jump.Left -= Vector3.UnitZ * 0.5f;
            jump.Right -= Vector3.UnitZ * 0.5f;
            jump.Sample();
            Require(jump.Last.TrackingLost && jump.Velocity.LengthSquared() == 0f, "large tracking jump resets velocity");
            jump.Hold(0.4f);
            Require(jump.Model.CompletedStrokeCount == 0, "stationary reacquisition does not count a stroke");
            jump.Last = jump.Model.Step(0.25f, new WaterStrokeModel.HandSample(true, jump.Left),
                new WaterStrokeModel.HandSample(true, jump.Right), jump.Head, jump.Forward);
            Require(jump.Last.TrackingLost, "long frame gap resets derivative state");
            Report("tracking discontinuity / frame gap", jump);

#if UNITY_EDITOR
            UnityEngine.Debug.Log($"PASS WaterStroke trajectory suite: {assertions} assertions. Synthetic only; no headset comfort claim.");
#else
            Console.WriteLine($"PASS WaterStroke trajectory suite: {assertions} assertions. Synthetic only; no headset comfort claim.");
#endif
        }

        static void UpperBreaststroke()
        {
            foreach (int hz in new[] { 60, 72, 90, 120 })
            foreach (float height in new[] { -.08f, .03f })
            {
                var high = new Simulation(hz);
                high.Left.Y = high.Right.Y = high.Head.Y + height;
                high.Hold(.35f);
                high.Move(high.Left + new Vector3(-.22f, 0f, -.25f),
                    high.Right + new Vector3(.22f, 0f, -.25f), .65f);
                Require(high.Model.CompletedStrokeCount == 2 && high.Velocity.Z > .8f,
                    "natural outward breaststroke near head height produces forward propulsion");
                int count = high.Model.CompletedStrokeCount;
                high.Hold(.7f);
                Require(high.Model.CompletedStrokeCount == count,
                    "high hands held still cannot retrigger a paddle");
                Report("high outward breaststroke height=" + height + " @" + hz, high);
            }
            foreach (bool left in new[] { true, false })
            {
                var single = new Simulation { LeftValid = left, RightValid = !left };
                single.Left.Y = single.Right.Y = single.Head.Y - .05f;
                single.Hold(.35f);
                single.Move(single.Left + new Vector3(-.22f, 0f, -.25f),
                    single.Right + new Vector3(.22f, 0f, -.25f), .65f);
                Require(single.Model.CompletedStrokeCount == 1 && single.Velocity.Z > .3f,
                    "either free hand can swim with a high outward pull independently");
            }
            var upperPoint = new Simulation();
            upperPoint.Left.Y = upperPoint.Right.Y = upperPoint.Head.Y - .05f;
            upperPoint.Hold(.35f);
            upperPoint.Move(upperPoint.Left - Vector3.UnitZ * .3f,
                upperPoint.Right - Vector3.UnitZ * .3f, .65f);
            Require(upperPoint.Model.CompletedStrokeCount == 0 && upperPoint.PeakSpeed == 0f,
                "ordinary high UI pointing and straight retraction cannot propel without outward intent");
            foreach (float sweep in new[] { .024f, .026f })
            {
                var boundary = new Simulation();
                boundary.Left.Y = boundary.Right.Y = boundary.Head.Y - .05f;
                boundary.Hold(.35f);
                boundary.Move(boundary.Left + new Vector3(-sweep, 0f, -.3f),
                    boundary.Right + new Vector3(sweep, 0f, -.3f), .65f);
                Require(sweep < .025f ? boundary.Model.CompletedStrokeCount == 0 && boundary.PeakSpeed == 0f :
                    boundary.Model.CompletedStrokeCount == 2 && boundary.Velocity.Z > 0f,
                    "small incidental sideways drift cannot turn high UI retraction into swimming");
            }
            var highSuppressed = new Simulation { Suppressed = true };
            highSuppressed.Left.Y = highSuppressed.Right.Y = highSuppressed.Head.Y - .05f;
            highSuppressed.Hold(.35f);
            highSuppressed.Move(highSuppressed.Left + new Vector3(-.22f, 0f, -.25f),
                highSuppressed.Right + new Vector3(.22f, 0f, -.25f), .65f);
            Require(highSuppressed.Model.CompletedStrokeCount == 0 && highSuppressed.PeakSpeed == 0f,
                "expanded high zone cannot bypass UI or weapon interaction suppression");
            var highBody = new Simulation();
            highBody.Left.Y = highBody.Right.Y = highBody.Head.Y - .05f;
            highBody.Hold(.35f);
            for (int frame = 0; frame < 90; frame++)
            {
                Vector3 movement = new Vector3(-.002f, 0f, -.003f);
                highBody.Head += movement; highBody.Left += movement; highBody.Right += movement;
                highBody.Sample();
            }
            Require(highBody.Model.CompletedStrokeCount == 0 && highBody.PeakSpeed == 0f,
                "physical body translation with high still hands cannot propel");
            foreach (int hz in new[] { 60, 72, 90, 120 })
            {
                var lowerPoint = new Simulation(hz);
                lowerPoint.Left.Y = lowerPoint.Right.Y = lowerPoint.Head.Y - .05f;
                lowerPoint.Hold(.35f);
                lowerPoint.Move(lowerPoint.Left - Vector3.UnitZ * .3f,
                    lowerPoint.Right - Vector3.UnitZ * .3f, .65f);
                int frames = 0;
                while (lowerPoint.Left.Y - lowerPoint.Head.Y > -.14f && frames++ < hz * 3)
                {
                    Vector3 motion = new Vector3(0f, -.15f * lowerPoint.Dt, -.28f * lowerPoint.Dt);
                    lowerPoint.Left += motion; lowerPoint.Right += motion; lowerPoint.Sample();
                }
                Require(lowerPoint.Left.Y - lowerPoint.Head.Y <= -.14f,
                    "UI lowering fixture crosses into chest-height zone");
                Require(lowerPoint.Model.CompletedStrokeCount == 0 && lowerPoint.Last.Strength == 0f,
                    "lowering a rejected high UI retraction cannot spend old distance as a paddle");
                var lowerStroke = new Simulation(hz);
                lowerStroke.Left.Y = lowerStroke.Right.Y = lowerStroke.Head.Y - .05f;
                lowerStroke.Hold(.35f);
                lowerStroke.Move(lowerStroke.Left + new Vector3(-.22f, -.16f, -.25f),
                    lowerStroke.Right + new Vector3(.22f, -.16f, -.25f), .65f);
                Require(lowerStroke.Model.CompletedStrokeCount == 2 && lowerStroke.Velocity.Z > .8f,
                    "valid high-to-low breaststroke remains continuous without duplicate strokes");
            }
            var overhead = new Simulation();
            overhead.Left.Y = overhead.Right.Y = overhead.Head.Y + .13f;
            overhead.Hold(.35f);
            overhead.Move(overhead.Left + new Vector3(-.22f, 0f, -.25f),
                overhead.Right + new Vector3(.22f, 0f, -.25f), .65f);
            Require(overhead.Model.CompletedStrokeCount == 0 && overhead.PeakSpeed == 0f,
                "true overhead gestures remain outside the expanded paddle zone");
            var upperClimb = new Simulation { Forward = Vector3.UnitY };
            upperClimb.Left.Y = upperClimb.Right.Y = upperClimb.Head.Y - .05f;
            upperClimb.Hold(.35f);
            upperClimb.Move(upperClimb.Left - Vector3.UnitY * .25f,
                upperClimb.Right - Vector3.UnitY * .25f, .5f);
            Require(upperClimb.Model.CompletedStrokeCount == 2 && upperClimb.Velocity.Y > .3f,
                "deliberate downward push while looking up works from a natural high start");
        }

        static void PartialTrackingLossContinuity()
        {
            foreach (int hz in new[] { 60, 72, 90, 120 })
            foreach (bool drivingLeft in new[] { true, false })
            {
                var swimming = new Simulation(hz);
                swimming.Hold(.35f);
                Vector3 pull = new Vector3(drivingLeft ? -.14f : .14f, 0f, -.20f);
                swimming.Move(swimming.Left + (drivingLeft ? pull : Vector3.Zero),
                    swimming.Right + (drivingLeft ? Vector3.Zero : pull), .35f);
                Require(swimming.Velocity.Length() > .3f && swimming.Last.Strength > 0f,
                    "continuous tracked-hand pull has actual propulsion before other hand dropout");
                int count = swimming.Model.CompletedStrokeCount;
                for (int frame = 0; frame < 20; frame++)
                {
                    if (drivingLeft) swimming.RightValid = frame % 2 != 0;
                    else swimming.LeftValid = frame % 2 != 0;
                    Vector3 step = pull * (swimming.Dt / .35f);
                    if (drivingLeft) swimming.Left += step; else swimming.Right += step;
                    float before = swimming.Velocity.Length();
                    swimming.Sample();
                    Require(!swimming.Last.NoTrackedHands && swimming.Last.Strength > 0f,
                        "healthy hand remains usable while other hand flickers in and out");
                    Require(swimming.Velocity.Length() >= before * .95f,
                        "other hand dropout must not erase healthy-hand propulsion");
                }
                Require(swimming.Model.CompletedStrokeCount == count,
                    "passive hand reacquisition cannot fabricate extra strokes");
                float beforeJump = swimming.Velocity.Length();
                if (drivingLeft)
                {
                    swimming.Right -= Vector3.UnitZ * .5f;
                    swimming.Left += pull * (swimming.Dt / .35f);
                }
                else
                {
                    swimming.Left -= Vector3.UnitZ * .5f;
                    swimming.Right += pull * (swimming.Dt / .35f);
                }
                swimming.Sample();
                Require(swimming.Last.TrackingLost && !swimming.Last.NoTrackedHands &&
                    swimming.Last.Strength > 0f && swimming.Velocity.Length() >= beforeJump * .95f,
                    "one hand discontinuity cannot erase healthy-hand propulsion or add a false impulse");
                swimming.LeftValid = swimming.RightValid = false;
                swimming.Sample();
                Require(swimming.Last.NoTrackedHands && swimming.Velocity.LengthSquared() == 0f,
                    "both hands unavailable still clear propulsion immediately");
                Report("partial tracking loss continuity left=" + drivingLeft + " @" + hz, swimming);
            }
            foreach (bool singleHand in new[] { true, false })
            {
                var jump = new Simulation { RightValid = !singleHand };
                jump.Hold(.35f);
                jump.Move(jump.Left + new Vector3(-.14f, 0f, -.20f),
                    jump.Right + new Vector3(.14f, 0f, -.20f), .35f);
                Require(jump.Velocity.Length() > .3f, "jump protection fixture starts with nonzero propulsion");
                jump.Left -= Vector3.UnitZ * .5f;
                jump.Right -= Vector3.UnitZ * .5f;
                jump.Sample();
                Require(jump.Last.TrackingLost && jump.Last.NoTrackedHands && jump.Velocity.LengthSquared() == 0f,
                    "discontinuity of every usable hand immediately clears preexisting propulsion");
                int count = jump.Model.CompletedStrokeCount;
                jump.Hold(.5f);
                Require(jump.Model.CompletedStrokeCount == count && jump.Velocity.LengthSquared() == 0f,
                    "stationary reacquisition after a discontinuity cannot create propulsion");
            }
            var delayed = new Simulation();
            delayed.Hold(.35f);
            delayed.Move(delayed.Left + new Vector3(-.14f, 0f, -.20f),
                delayed.Right + new Vector3(.14f, 0f, -.20f), .35f);
            Require(delayed.Velocity.Length() > .3f, "long-frame fixture starts with propulsion");
            var invalid = delayed.Model.Step(.25f, new WaterStrokeModel.HandSample(true, delayed.Left),
                new WaterStrokeModel.HandSample(true, delayed.Right), delayed.Head, delayed.Forward);
            Require(invalid.NoTrackedHands && WaterStrokeModel.AdvanceVelocity(delayed.Velocity, invalid,
                .25f, 1.6f, 2.5f, .7f).LengthSquared() == 0f,
                "long frame still clears preexisting propulsion and derivative state");
        }

        static void UpwardDownstrokes()
        {
            foreach (int hz in new[] { 30, 60, 90, 120 })
            foreach (float pitch in new[] { .65f, 1f })
            {
                var climb = new Simulation(hz) { Forward = new Vector3(0f, pitch, (float)Math.Sqrt(1f - pitch * pitch)) };
                climb.Hold(.35f);
                climb.Move(climb.Left - Vector3.UnitY * .32f, climb.Right - Vector3.UnitY * .32f, .5f);
                Require(climb.Model.CompletedStrokeCount == 2 && climb.Velocity.Y > .3f && climb.Velocity.Y > Math.Abs(climb.Velocity.Z),
                    "looking up and pushing water down produces mainly upward thrust, including straight-up gaze");
                float speed = climb.Velocity.Length(); int count = climb.Model.CompletedStrokeCount;
                climb.Hold(.6f);
                Require(climb.Model.CompletedStrokeCount == count && climb.Velocity.Length() < speed,
                    "upward gaze and a stopped hand cannot sustain or retrigger power");
                Report("upward downstroke pitch=" + pitch + " @" + hz, climb);
            }
            foreach (bool left in new[] { true, false })
            {
                var one = new Simulation { Forward = Vector3.UnitY, LeftValid = left, RightValid = !left };
                one.Hold(.35f);
                one.Move(one.Left - Vector3.UnitY * .30f, one.Right - Vector3.UnitY * .30f, .5f);
                Require(one.Model.CompletedStrokeCount == 1 && one.Velocity.Y > .1f, "either single hand can push upward without the other tracking");
            }
            foreach (int reason in new[] { 0, 1, 2, 3 })
            {
                var reject = new Simulation { Forward = reason == 0 ? Vector3.UnitZ : Vector3.UnitY, Suppressed = reason == 1 };
                if (reason == 2) { reject.Left.Y = reject.Right.Y = reject.Head.Y + .5f; }
                reject.Hold(.35f);
                reject.Move(reject.Left + Vector3.UnitY * (reason == 3 ? .25f : -.25f),
                    reject.Right + Vector3.UnitY * (reason == 3 ? .25f : -.25f), .5f);
                Require(reject.Model.CompletedStrokeCount == 0 && reject.PeakSpeed == 0f,
                    "ordinary face-forward vertical gestures, UI/aim suppression, overhead gestures and upward recovery do not propel");
            }
            var body = new Simulation { Forward = Vector3.UnitY }; body.Hold(.35f);
            for (int i = 0; i < 60; i++)
            { body.Head -= Vector3.UnitY * .004f; body.Left -= Vector3.UnitY * .004f; body.Right -= Vector3.UnitY * .004f; body.Sample(); }
            Require(body.Model.CompletedStrokeCount == 0 && body.PeakSpeed == 0f, "crouching with hands moving alongside the head is not an upstroke");
        }

#if WATER_STROKE_TEST_MAIN
        public static int Main()
        {
            try { RunAll(); return 0; }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }
#endif
    }
}
#endif

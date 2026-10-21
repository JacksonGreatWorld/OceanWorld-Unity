using System;
using System.Numerics;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual pure production gate and joint metric; no live tracking/physics claims.</summary>
    public static class OceanSpearTriggerTests
    {
        static int assertions;
        sealed class Trial
        {
            public readonly OceanSpearTriggerGate Gate = new OceanSpearTriggerGate();
            public readonly bool Left;
            public int Frame, Fires;
            public Trial(bool left = true) { Left = left; Gate.Acquire(left); }
            public bool Sample(float curl, float dt = .02f, bool fresh = true, bool grip = true,
                bool suppressed = false, bool? left = null, int? frame = null)
            {
                Frame = frame ?? Frame + 1;
                bool fired = Gate.Step(Frame, dt, left ?? Left, fresh, grip, curl, suppressed);
                if (fired) Fires++;
                return fired;
            }
            public void Hold(float curl, float duration, float dt = .02f)
            {
                for (int i = 0; i < (int)Math.Ceiling(duration / dt); i++) Sample(curl, dt);
            }
            public void Cycle(float dt = .02f)
            { Hold(0f, .16f, dt); Hold(.4f, .12f, dt); }
        }

        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("OceanSpearTriggerTests: " + message); assertions++; }

        static ShellGrabGesture.Finger Finger(bool bent, bool left, float scale, Quaternion rotation, Vector3 offset)
        {
            var a = Vector3.Zero;
            var b = new Vector3(0f, 0f, .04f);
            var c = bent ? new Vector3(0f, -.025f, .04f) : new Vector3(0f, 0f, .065f);
            var d = bent ? new Vector3(0f, -.025f, .02f) : new Vector3(0f, 0f, .085f);
            Func<Vector3, Vector3> transform = v =>
                Vector3.Transform(new Vector3(left ? -v.X : v.X, v.Y, v.Z) * scale, rotation) + offset;
            return new ShellGrabGesture.Finger(transform(a), transform(b), transform(c), transform(d));
        }

        public static void RunAll()
        {
            assertions = 0;
            // Feed actual index-joint geometry through the shared physical metric, on both
            // hands and at two hand sizes/orientations, while three-finger grip stays true.
            foreach (bool left in new[] { true, false }) foreach (float scale in new[] { .8f, 1.2f })
            {
                Quaternion rotation = Quaternion.CreateFromYawPitchRoll(.7f, -.4f, .2f);
                Vector3 offset = new Vector3(.2f, 1.3f, -.1f);
                Check(ShellGrabGesture.TryFingerCurl(Finger(false, left, scale, rotation, offset), out float open) &&
                    open <= OceanSpearTriggerGate.ReleaseCurl, "transformed straight index is released");
                Check(ShellGrabGesture.TryFingerCurl(Finger(true, left, scale, rotation, offset), out float closed) &&
                    closed >= OceanSpearTriggerGate.PressCurl, "transformed curled index can pull trigger");
                var t = new Trial(left);
                t.Hold(closed, 1f); Check(t.Fires == 0, "initial curled index on gun reveal cannot fire");
                t.Hold(open, .16f); t.Hold(closed, .12f);
                Check(t.Fires == 1 && t.Gate.ShotsFired == 1, "actual joint metric recognizes one trigger pull");
            }

            foreach (int fps in new[] { 30, 60, 90, 120 }) foreach (bool left in new[] { true, false })
            {
                float dt = 1f / fps;
                var t = new Trial(left);
                t.Hold(.4f, .4f, dt); Check(t.Fires == 0, "initial curled index on reveal at " + fps);
                for (int shot = 1; shot <= 12; shot++)
                {
                    t.Cycle(dt);
                    Check(t.Fires == shot && t.Gate.ShotsFired == shot,
                        "one deliberate trigger input " + shot + " at " + fps + " left=" + left);
                    t.Hold(.4f, .25f, dt);
                    Check(t.Fires == shot, "sustained trigger never auto-fires at " + fps);
                }
                t.Cycle(dt); Check(t.Fires == 13 && t.Gate.ShotsFired == 13, "input recognizer imposes no ammunition capacity");
                t.Gate.Suspend(); Check(t.Gate.ShotsFired == 13 && t.Gate.Held, "Suspend preserves visible session input history");
                t.Gate.Acquire(left); t.Hold(.4f, .2f, dt);
                Check(t.Fires == 13 && t.Gate.ShotsFired == 0,
                    "hidden then newly shown gun starts fresh but a curled index cannot fire");
                t.Cycle(dt); Check(t.Fires == 14 && t.Gate.ShotsFired == 1, "new visible session supports next deliberate input");
            }

            // Interruption mid-pull cannot preserve the earlier release qualification.
            foreach (string kind in new[] { "tracking", "menu", "index-nan", "index-infinity", "index-negative",
                "index-large", "grip", "long", "zero-dt", "nan-dt", "backwards", "frame-gap", "explicit" })
            {
                var t = new Trial(); t.Cycle(); t.Hold(0f, .16f); t.Sample(.4f, .02f);
                Check(t.Gate.Armed && t.Fires == 1, "candidate established before " + kind);
                switch (kind)
                {
                    case "tracking": t.Sample(.4f, fresh: false); break;
                    case "menu": t.Sample(.4f, suppressed: true); break;
                    case "index-nan": t.Sample(float.NaN); break;
                    case "index-infinity": t.Sample(float.PositiveInfinity); break;
                    case "index-negative": t.Sample(-.01f); break;
                    case "index-large": t.Sample(1.01f); break;
                    case "grip": t.Sample(.4f, grip: false); break;
                    case "long": t.Sample(.4f, .1001f); break;
                    case "zero-dt": t.Sample(.4f, 0f); break;
                    case "nan-dt": t.Sample(.4f, float.NaN); break;
                    case "backwards": t.Sample(.4f, frame: 0); break;
                    case "frame-gap": t.Sample(.4f, frame: t.Frame + 2); break;
                    case "explicit": t.Gate.Suspend(); break;
                }
                Check(!t.Gate.Armed && t.Gate.Held && t.Gate.ShotsFired == 1,
                    "interruption preserves session count, clears qualification: " + kind);
                t.Hold(.4f, .3f);
                Check(t.Fires == 1, "recovered old curled index cannot fire: " + kind);
                t.Cycle(); Check(t.Fires == 2 && t.Gate.ShotsFired == 2, "fresh release/pull works: " + kind);
            }

            var timing = new Trial(); timing.Sample(0f, .10f);
            Check(!timing.Gate.Armed, "first visible-session observation spends no earlier dt");
            timing.Sample(0f, .10f); Check(!timing.Gate.Armed, "one long release sample cannot arm");
            timing.Sample(0f, .10f); Check(timing.Gate.Armed, "two capped release samples can arm");
            Check(!timing.Sample(.4f, .10f) && timing.Gate.ShotsFired == 0, "one long curled sample cannot fire");
            Check(timing.Sample(.4f, .01f) && timing.Gate.ShotsFired == 1, "second curled sample completes stable pull");

            var thresholds = new Trial(); thresholds.Sample(0f);
            thresholds.Sample(.12f, .04f); thresholds.Sample(.13f, .04f); thresholds.Sample(.12f, .04f);
            Check(!thresholds.Gate.Armed, "release stability must be continuous, midzone cancels candidate");
            thresholds.Sample(.12f, .04f); Check(thresholds.Gate.Armed, "release threshold inclusive");
            thresholds.Sample(.18f, .02f); Check(thresholds.Gate.Armed, "armed state persists across hysteresis zone");
            thresholds.Sample(.22f, .02f); thresholds.Sample(.21f, .02f); thresholds.Sample(.22f, .02f);
            Check(thresholds.Fires == 0, "press stability must be continuous");
            Check(thresholds.Sample(.22f, .02f), "press threshold inclusive after stable pull");

            var duplicate = new Trial(); duplicate.Sample(0f); duplicate.Sample(0f, .04f);
            int repeated = duplicate.Frame;
            for (int i = 0; i < 20; i++) duplicate.Sample(0f, .04f, frame: repeated);
            Check(!duplicate.Gate.Armed, "duplicate release frames cannot accumulate stability");
            duplicate.Sample(0f, .04f); duplicate.Sample(.4f, .02f); repeated = duplicate.Frame;
            for (int i = 0; i < 20; i++) duplicate.Sample(.4f, .02f, frame: repeated);
            Check(duplicate.Fires == 0, "duplicate press frames cannot fire");
            Check(duplicate.Sample(.4f, .02f), "next physical frame fires once"); repeated = duplicate.Frame;
            Check(!duplicate.Sample(.4f, frame: repeated) && duplicate.Fires == 1, "same fire frame never fires twice");

            var owner = new Trial(false); owner.Hold(0f, .16f);
            Check(!owner.Gate.Step(owner.Frame + 1, .02f, true, true, true, .4f) && owner.Gate.Armed,
                "unheld hand does not mutate held hand qualification");
            owner.Hold(.4f, .1f); Check(owner.Fires == 1, "correct hand retains input after other hand call");
            owner.Gate.Reset(); owner.Cycle();
            Check(!owner.Gate.Held && owner.Gate.ShotsFired == 0 && owner.Fires == 1,
                "a hidden gun cannot recognize input until a new visible session");

            var recovery = new Trial(); recovery.Cycle(); recovery.Gate.Suspend();
            recovery.Sample(0f, .10f); recovery.Sample(0f, .04f);
            Check(!recovery.Gate.Armed && recovery.Gate.ShotsFired == 1, "recovery does not credit time before first fresh observation");
            recovery.Sample(0f, .04f); recovery.Sample(.4f, .02f);
            recovery.Sample(.4f, suppressed: true, frame: recovery.Frame);
            Check(!recovery.Gate.Armed && recovery.Fires == 1, "same-frame suppression may cancel candidate safely");

            string result = "OceanSpearTriggerTests PASS: " + assertions +
                " assertions; actual index curl, both hands, four frame rates, unbounded input cycles with externally owned ammunition, release/pull hysteresis, duplicate frames and interruption recovery. No live hand/physics claim.";
#if UNITY_EDITOR
            UnityEngine.Debug.Log(result);
#else
            Console.WriteLine(result);
#endif
        }
    }
}

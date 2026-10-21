#if UNITY_EDITOR || OCEAN_MENU_PRESS_STANDALONE
using System;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Synthetic press semantics; does not claim physical aiming, tracking or headset comfort.</summary>
    public static class OceanMenuPressGateTests
    {
        static int assertions;
        public static int LastAssertionCount => assertions;

        public static void RunAll()
        {
            assertions = 0;
            HoverThenPress(); HeldEntryAndTargetChange(); TrackingAndAnimationReset();
            HysteresisAndRelease(); DuplicateFrames(); InvalidSamples(); IndependentHands(); FrameRates();
            string result = "OCEAN_MENU_PRESS_TESTS_PASSED assertions=" + assertions +
                "; synthetic hover/pinch, held-entry, target switching, tracking recovery, animation lock, hysteresis and frame de-duplication. Not headset acceptance.";
#if UNITY_EDITOR
            UnityEngine.Debug.Log(result);
#else
            Console.WriteLine(result);
#endif
        }

        sealed class Pointer
        {
            public readonly OceanMenuPressGate gate = new OceanMenuPressGate();
            public int frame;
            public int Step(float strength = 0f, int target = 7, bool fresh = true, bool enabled = true, float dt = .02f)
                => gate.Step(frame++, dt, fresh, strength, target, enabled);
            public void Arm(int target = 7)
            {
                for (int i = 0; i < 7; i++) Require(Step(0f, target) == -1, "released hover does not click");
                Require(gate.Armed, "stable released hover qualifies");
            }
        }
        static void Require(bool condition, string detail)
        { assertions++; if (!condition) throw new InvalidOperationException("Ocean menu press: " + detail); }

        static void HoverThenPress()
        {
            var p = new Pointer();
            Require(p.Step(.9f) == -1, "pinch at arrival does not click");
            p.Arm();
            Require(p.Step(.8f) == 7, "new pinch on qualified hover clicks correct target");
            Require(!p.gate.Armed, "accepted press consumes qualification");
            for (int i = 0; i < 300; i++) Require(p.Step(.9f) == -1, "holding pinch never repeats");
            p.Arm(); Require(p.Step(.75f) == 7, "release then new pinch may click again");
        }
        static void HeldEntryAndTargetChange()
        {
            var p = new Pointer();
            for (int i = 0; i < 8; i++) Require(p.Step(.9f, -1) == -1, "pinch without target does not click");
            for (int i = 0; i < 40; i++) Require(p.Step(.9f, 4) == -1, "held pinch entering a target is not a new press");
            p.Arm(4);
            Require(p.Step(.9f, 5) == -1 && !p.gate.Armed, "new target cannot inherit hover qualification");
            for (int i = 0; i < 40; i++) Require(p.Step(.9f, 4) == -1, "held pinch returning to old target stays blocked");
            p.Arm(4); Require(p.Step(.9f, 4) == 4, "new released hover recovers after switching");
            p.Arm(4); Require(p.Step(0f, -1) == -1 && !p.gate.Armed, "ray leaving all targets clears qualification");
            Require(p.Step(.9f, 4) == -1, "re-entry press cannot reuse old hover");
        }
        static void TrackingAndAnimationReset()
        {
            var p = new Pointer(); p.Arm();
            Require(p.Step(0f, fresh: false) == -1 && !p.gate.Armed, "tracking loss resets even with stale open signal");
            for (int i = 0; i < 40; i++) Require(p.Step(.9f) == -1, "held pinch at tracking recovery does not click");
            p.Arm(); Require(p.Step(.9f, enabled: false) == -1 && !p.gate.Armed, "animation lock cancels armed press");
            for (int i = 0; i < 20; i++) Require(p.Step(.9f) == -1, "animation unlocking while held cannot click");
            p.Arm(); p.gate.Reset();
            Require(p.Step(.9f) == -1, "page/open/close reset requires new released hover");
            p.Arm(); Require(p.Step(.9f) == 7, "fresh deliberate press works after reset");
        }
        static void HysteresisAndRelease()
        {
            var p = new Pointer();
            for (int i = 0; i < 50; i++) Require(p.Step(.50f) == -1 && !p.gate.Armed, "middle strength never arms");
            Require(p.Step(.35f) == -1, "release threshold is inclusive");
            Require(p.Step(.35f) == -1, "short release remains unarmed");
            Require(p.Step(.50f) == -1 && !p.gate.Armed, "middle signal interrupts incomplete release hover");
            Require(p.Step(.35f) == -1, "second short release");
            Require(p.Step(.35f) == -1 && !p.gate.Armed, "separate release fragments do not add together");
            Require(p.Step(.9f) == -1, "too-short released hover cannot trigger");
            p.Arm();
            Require(p.Step(.50f) == -1 && p.gate.Armed, "qualified gradual pinch remains eligible");
            Require(p.Step(.749f) == -1 && p.gate.Armed, "below press threshold does not trigger");
            Require(p.Step(.75f) == 7, "press threshold is inclusive");
            for (int i = 0; i < 80; i++) Require(p.Step(i % 2 == 0 ? .74f : .76f) == -1, "press-threshold jitter cannot repeat without release");
        }
        static void DuplicateFrames()
        {
            var p = new Pointer(); p.Arm(); int frame = p.frame;
            Require(p.gate.Step(frame, .02f, true, .9f, 7, true) == 7, "first frame call can press");
            for (int i = 0; i < 20; i++) Require(p.gate.Step(frame, .10f, true, 0f, 7, true) == -1 && !p.gate.Armed, "same frame cannot add hover time");
            p.gate.Reset(); Require(p.gate.Step(frame, .10f, true, .9f, 7, true) == -1, "reset cannot bypass same-frame de-duplication");
            Require(p.gate.Step(0, .02f, true, 0f, 7, true) == -1 && !p.gate.Armed, "rewound frame counter resets qualification");
            for (int i = 1; i <= 7; i++) Require(p.gate.Step(i, .02f, true, 0f, 7, true) == -1, "counter restart can recover deliberately");
            Require(p.gate.Step(8, .02f, true, .9f, 7, true) == 7, "new timeline can click after released hover");
        }
        static void InvalidSamples()
        {
            foreach (float strength in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -.01f, 1.01f })
            {
                var p = new Pointer(); p.Arm();
                Require(p.Step(strength) == -1 && !p.gate.Armed, "invalid strength clears stale qualification");
                Require(p.Step(.9f) == -1, "invalid sample cannot bridge into a press");
            }
            foreach (float dt in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -.01f, 0f, .101f, 3f })
            {
                var p = new Pointer(); p.Arm();
                Require(p.Step(.9f, dt: dt) == -1 && !p.gate.Armed, "invalid/stalled time cannot synthesize a press");
            }
            var q = new OceanMenuPressGate();
            Require(q.Step(-2, .02f, true, 0f, 7, true) == -1 && !q.Armed, "negative frame is invalid");
            Require(q.Step(0, .10f, true, 0f, 7, true) == -1 && !q.Armed, "arrival does not retroactively hover for a full delta");
        }
        static void IndependentHands()
        {
            var left = new Pointer(); var right = new Pointer(); left.Arm(2); right.Arm(3);
            Require(left.Step(.9f, 2) == 2 && right.Step(.9f, 3) == 3, "independent hands provide independent candidates; caller owns global arbitration");
            for (int i = 0; i < 20; i++) Require(right.Step(.9f, 3) == -1, "losing hand cannot replay candidate while held");
            left.gate.Reset(); right.gate.Reset();
            Require(left.Step(.9f, 2) == -1 && right.Step(.9f, 3) == -1, "page dispatch resets both candidates");
        }
        static void FrameRates()
        {
            foreach (int fps in new[] { 30, 60, 72, 90, 120 })
            {
                var p = new Pointer(); float dt = 1f / fps;
                Require(p.Step(0f, dt: dt) == -1 && !p.gate.Armed, "initial sample cannot arm at " + fps);
                int samples = (int)Math.Ceiling(OceanMenuPressGate.HoverSeconds / dt);
                for (int i = 0; i < samples + 1; i++) Require(p.Step(0f, dt: dt) == -1, "stable hover at " + fps);
                Require(p.gate.Armed && p.Step(.9f, dt: dt) == 7, "same semantic click at " + fps);
            }
        }
#if OCEAN_MENU_PRESS_STANDALONE
        public static void Main() => RunAll();
#endif
    }
}
#endif

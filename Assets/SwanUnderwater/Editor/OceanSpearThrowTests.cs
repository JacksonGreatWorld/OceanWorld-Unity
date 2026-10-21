using System;
using System.Numerics;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Deterministic tracked-pose traces; no Unity scene, device or physics claims.</summary>
    public static class OceanSpearThrowTests
    {
        static int assertions;
        sealed class Trial
        {
            public readonly OceanSpearThrowGate Gate = new OceanSpearThrowGate();
            public Vector3 Palm, Head = new Vector3(0, 1.6f, 0), Forward = Vector3.UnitZ, Direction;
            public readonly bool Left;
            public int Frame, Fires;
            public float Speed;
            public Trial(bool left = true)
            { Left = left; Palm = new Vector3(left ? -.22f : .22f, 1.25f, .22f); Gate.Acquire(left); }
            public bool Sample(float dt, bool fresh = true, bool gripping = true, bool suppressed = false,
                bool? left = null, int? frame = null)
            {
                Frame = frame ?? Frame + 1;
                bool fired = Gate.Step(Frame, dt, left ?? Left, fresh, gripping, Palm, Head, Forward, suppressed,
                    out Vector3 direction, out float speed);
                if (fired) { Fires++; Direction = direction; Speed = speed; }
                return fired;
            }
            public void Stable(float dt)
            { for (int i = 0; i < (int)Math.Ceiling(OceanSpearThrowGate.SettleSeconds / dt) + 2; i++) Sample(dt); }
            public void Move(Vector3 palmVelocity, Vector3 headVelocity, float seconds, float dt)
            {
                for (int i = 0; i < (int)Math.Ceiling(seconds / dt); i++)
                { Palm += palmVelocity * dt; Head += headVelocity * dt; Sample(dt); }
            }
        }

        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("OceanSpearThrowTests: " + message); assertions++; }

        public static void RunAll()
        {
            assertions = 0;
            foreach (int fps in new[] { 30, 60, 90, 120 }) foreach (bool left in new[] { true, false })
            {
                float dt = 1f / fps; var t = new Trial(left);
                t.Stable(dt); Check(t.Gate.Armed, "settled holding hand arms at " + fps);
                t.Move(Vector3.UnitZ * 1.1f, Vector3.Zero, .22f, dt);
                Check(t.Fires == 1 && t.Gate.Fired, "one forward throw at " + fps + " left=" + left);
                Check(t.Speed >= OceanSpearThrowGate.MinimumForwardSpeed && Vector3.Dot(t.Direction, Vector3.UnitZ) > .99f,
                    "finite forward output at " + fps);
                t.Stable(dt); t.Move(Vector3.UnitZ, Vector3.Zero, .22f, dt);
                Check(t.Fires == 1, "fired latch survives settling and another motion");
            }

            const float step = 1f / 60f;
            var initial = new Trial();
            initial.Move(Vector3.UnitZ * 1.2f, Vector3.Zero, .25f, step);
            Check(initial.Fires == 0 && !initial.Gate.Armed, "pickup during fast movement cannot fire");
            initial.Stable(step); initial.Move(Vector3.UnitZ, Vector3.Zero, .16f, step);
            Check(initial.Fires == 1, "pickup becomes usable after low-speed stability");

            var extended = new Trial(); extended.Palm.Z = .70f;
            extended.Stable(step); extended.Move(Vector3.Zero, Vector3.Zero, 2f, step);
            Check(extended.Gate.Armed && extended.Fires == 0, "already extended held hand never throws by its position alone");
            var body = new Trial(); body.Stable(step);
            body.Move(Vector3.UnitZ * 1.2f, Vector3.UnitZ * 1.2f, .3f, step);
            Check(body.Fires == 0, "whole-body forward translation cannot throw");
            var head = new Trial(); head.Stable(step);
            head.Move(Vector3.Zero, -Vector3.UnitZ * 1.2f, .3f, step);
            Check(head.Fires == 0, "moving only the head backward cannot throw");
            foreach (Vector3 velocity in new[] { -Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ * .25f,
                new Vector3(2f, 0, .8f) })
            {
                var t = new Trial(); t.Stable(step); t.Move(velocity, Vector3.Zero, .35f, step);
                Check(t.Fires == 0, "backward/side/up/slow/mostly-sideways motion rejected " + velocity);
            }

            var shortPull = new Trial(); shortPull.Stable(step);
            shortPull.Move(Vector3.UnitZ, Vector3.Zero, .05f, step); shortPull.Stable(step);
            Check(shortPull.Fires == 0, "fast but less than 10cm does not throw");
            var singleJump = new Trial(); singleJump.Stable(step);
            singleJump.Palm.Z += .12f; singleJump.Sample(.08f); singleJump.Sample(.02f);
            Check(singleJump.Fires == 0, "one isolated large sample cannot satisfy a sustained throw");

            // Each interruption occurs mid-candidate. No old motion may bridge recovery.
            foreach (string interruption in new[] { "tracking", "menu", "open", "long", "jump", "nan", "reverse-frame" })
            {
                var t = new Trial(); t.Stable(step); t.Move(Vector3.UnitZ, Vector3.Zero, .05f, step);
                Vector3 restore = t.Palm;
                switch (interruption)
                {
                    case "tracking": t.Sample(step, fresh: false); break;
                    case "menu": t.Sample(step, suppressed: true); break;
                    case "open": t.Sample(step, gripping: false); break;
                    case "long": t.Sample(.2f); break;
                    case "jump": t.Palm.Z += .5f; t.Sample(step); t.Palm = restore; break;
                    case "nan": t.Palm.X = float.NaN; t.Sample(step); t.Palm = restore; break;
                    case "reverse-frame": t.Sample(step, frame: 0); break;
                }
                Check(!t.Gate.Armed && t.Fires == 0, "interruption clears candidate: " + interruption);
                t.Move(Vector3.UnitZ, Vector3.Zero, .2f, step);
                Check(t.Fires == 0, "recovery still moving cannot throw: " + interruption);
                t.Stable(step); t.Move(Vector3.UnitZ, Vector3.Zero, .16f, step);
                Check(t.Fires == 1, "fresh stable recovery works: " + interruption);
            }

            var duplicate = new Trial(); duplicate.Stable(step); int sameFrame = duplicate.Frame;
            Vector3 original = duplicate.Palm; duplicate.Palm.Z += .5f;
            Check(!duplicate.Sample(step, frame: sameFrame) && duplicate.Gate.Armed, "duplicate frame cannot consume or invalidate a second pose");
            duplicate.Palm = original; duplicate.Move(Vector3.UnitZ, Vector3.Zero, .16f, step);
            Check(duplicate.Fires == 1, "duplicate frame did not corrupt the real baseline");
            var wrongHand = new Trial(); wrongHand.Stable(step);
            Check(!wrongHand.Sample(step, left: false) && wrongHand.Gate.Armed, "other hand cannot consume held-hand input");
            wrongHand.Move(Vector3.UnitZ, Vector3.Zero, .16f, step);
            Check(wrongHand.Fires == 1, "held hand still throws after unrelated hand sample");
            wrongHand.Gate.Reset(); wrongHand.Stable(step); wrongHand.Move(Vector3.UnitZ, Vector3.Zero, .16f, step);
            Check(wrongHand.Fires == 1 && !wrongHand.Gate.Held, "reset without pickup cannot rearm");
            wrongHand.Gate.Acquire(true); wrongHand.Stable(step); wrongHand.Move(Vector3.UnitZ, Vector3.Zero, .16f, step);
            Check(wrongHand.Fires == 2, "successful new pickup permits exactly one more throw");

            foreach (float dt in new[] { 0f, -.01f, float.NaN, float.PositiveInfinity })
            {
                var t = new Trial(); t.Stable(step); t.Sample(dt);
                Check(!t.Gate.Armed && t.Fires == 0, "invalid dt never fires");
            }
            var badForward = new Trial(); badForward.Stable(step); badForward.Forward = Vector3.Zero; badForward.Sample(step);
            Check(!badForward.Gate.Armed, "undefined forward cancels qualification");
            string result = "OceanSpearThrowTests PASS: " + assertions + " assertions; four frame rates, both hands, stable pickup, one-shot latch, body/head motion and interruption recovery. No live hand/physics claim.";
#if UNITY_EDITOR
            UnityEngine.Debug.Log(result);
#else
            Console.WriteLine(result);
#endif
        }
    }
}

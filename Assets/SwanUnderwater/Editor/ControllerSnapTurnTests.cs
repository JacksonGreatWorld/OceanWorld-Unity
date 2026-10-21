using System;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Real controller gate, without XR/Unity stubs. Does not establish headset comfort.</summary>
    public static class ControllerSnapTurnTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool value, string message)
        {
            Assertions++;
            if (!value) throw new InvalidOperationException("ControllerSnapTurnTests: " + message);
        }

        public static void RunAll()
        {
            Assertions = 0;
            foreach (float sign in new[] { -1f, 1f })
            foreach (float degrees in new[] { 15f, 30f, 60f })
            {
                var gate = new ControllerSnapTurnGate();
                Check(gate.Step(sign, degrees) == sign * degrees, "initial deliberate excursion keeps configured snap");
                for (int frame = 0; frame < 120; frame++)
                    Check(gate.Step(sign, degrees) == 0f, "held stick never repeats");

                // The same production Reset is called on focus loss/return, disable and relocation.
                gate.Reset();
                gate.Reset();
                for (int frame = 0; frame < 120; frame++)
                    Check(gate.Step(sign, degrees) == 0f, "resume while held cannot rotate");
                Check(gate.Step(-sign, degrees) == 0f, "switching sign without a sampled neutral is not a new edge");
                Check(gate.Step(sign * .25f, degrees) == 0f, "neutral boundary itself does not rearm");
                Check(gate.Step(sign * .65f, degrees) == 0f, "still disarmed at exact press boundary");
                Check(gate.Step(sign * .249f, degrees) == 0f, "fresh neutral rearms without moving");
                Check(gate.Step(sign * .649f, degrees) == 0f, "subthreshold motion does not consume armed edge");
                Check(gate.Step(sign * .65f, degrees) == sign * degrees, "new post-recovery excursion snaps once");
                Check(gate.Step(sign, degrees) == 0f, "new excursion remains single-shot");
                gate.Reset();
                Check(gate.Step(sign, degrees) == 0f, "relocation while held also needs neutral");
                gate.Step(0f, degrees);
                Check(gate.Step(-sign, degrees) == -sign * degrees, "opposite direction works after neutral");

                for (int frame = 0; frame < 10; frame++)
                    Check(gate.Step(0f, degrees, false) == 0f, "disconnected default zero is not real neutral");
                Check(gate.Step(sign, degrees, true) == 0f, "reconnected held stick cannot rotate");
                Check(gate.Step(0f, degrees, true) == 0f, "reconnected real neutral rearms without rotating");
                Check(gate.Step(sign, degrees, true) == sign * degrees, "new excursion after real neutral rotates once");
                Check(gate.Step(sign, degrees, true) == 0f, "reconnected excursion still does not repeat");

                // Both production state setters Reset on actual entry and exit edges, never
                // on repeated same-state calls. Exercise the gate's corresponding boundaries.
                foreach (string state in new[] { "menu locomotion pause", "interaction suppression" })
                {
                    gate.Step(0f, degrees);
                    gate.Reset(); // Enter while an unused snap is armed.
                    Check(gate.Step(sign, degrees) == 0f, state + " entry discards the armed snap");
                    gate.Step(0f, degrees);
                    gate.Reset(); // Exit also discards any earlier neutral observation.
                    Check(gate.Step(sign, degrees) == 0f, state + " exit with held stick requires fresh neutral");
                    Check(gate.Step(-sign, degrees) == 0f, state + " exit cannot turn by reversing held stick");
                    Check(gate.Step(0f, degrees, false) == 0f, state + " exit unavailable sample is not neutral");
                    Check(gate.Step(sign, degrees) == 0f, state + " remains disarmed until real neutral");
                    Check(gate.Step(0f, degrees) == 0f, state + " fresh neutral does not rotate");
                    Check(gate.Step(sign, degrees) == sign * degrees, state + " new excursion rotates once");
                    Check(gate.Step(sign, degrees) == 0f, state + " held excursion does not repeat");
                }
            }
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var gate = new ControllerSnapTurnGate();
                Check(gate.Step(invalid, 30f) == 0f, "invalid axis cannot rotate");
                Check(gate.Step(1f, 30f) == 0f, "invalid-axis recovery needs neutral");
                gate.Step(0f, 30f);
                Check(gate.Step(1f, invalid) == 0f, "invalid angle cannot rotate");
                Check(gate.Step(1f, 30f) == 0f, "invalid-angle recovery needs neutral");
            }
#if UNITY_EDITOR
            UnityEngine.Debug.Log("ControllerSnapTurnTests PASS " + Assertions + " assertions; pure input transitions, not device validation.");
#else
            Console.WriteLine("ControllerSnapTurnTests PASS " + Assertions + " assertions; pure input transitions, not device validation.");
#endif
        }

#if OCEAN_CONTROLLER_SNAP_TEST_MAIN
        public static int Main()
        {
            try { RunAll(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
#endif
    }
}

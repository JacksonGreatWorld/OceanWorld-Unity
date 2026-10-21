using System;

namespace SwanUnderwater.EditorTests
{
    public static class OceanGuardianCompositionTests
    {
        static int checks;
        static void Check(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }
        public static void RunAll()
        {
            checks = 0;
            var initial = OceanGuardianComposition.Evaluate(0, 1.6f, 1.6f, 17.1f);
            Check(initial.right == 0 && initial.forward > 70 && initial.headingForward < 0 && initial.headingRight == 0,
                "Reveal lies ahead and the real head faces the observer.");
            foreach (double t in new[] { -1.0, 0.0, 1.2, 2.999, 3.0 })
                Check(OceanGuardianComposition.OrbitTime(t) == 0, "Short reveal hold must not advance the orbit.");
            double previousSpeed = -1;
            foreach (double t in new[] { 3.0001, 4.0, 5.5, 7.0, 7.9999, 8.0001 })
            {
                double speed = (OceanGuardianComposition.OrbitTime(t + .00001) - OceanGuardianComposition.OrbitTime(t)) / .00001;
                Check(speed >= previousSpeed - .00001 && speed >= 0 && speed <= 1.00001,
                    "Orbit enters monotonically without an acceleration jump or overspeed.");
                previousSpeed = speed;
            }
            var a = OceanGuardianComposition.Evaluate(12.3, 1.6f, 1.6f, 17.1f);
            var b = OceanGuardianComposition.Evaluate(162.3, 1.6f, 1.6f, 17.1f);
            Check(Math.Abs(a.right-b.right) < .0001 && Math.Abs(a.forward-b.forward) < .0001,
                "Completed ramp keeps the original slow 150-second closed orbit.");
            foreach (float eye in new[] { -3f, 1.6f, 40f, 1000f })
            {
                for (int i = 0; i <= 30; i++)
                {
                    var p = OceanGuardianComposition.Evaluate(i * 5, 1.6f, eye, 17.1f);
                    Check(p.height - 17.1f - eye >= 19.999f && p.forward >= 80 && p.forward <= 112 &&
                        Math.Abs(p.bank) <= 3 && p.headingRight*p.headingRight+p.headingForward*p.headingForward > 200,
                        "Orbit preserves safety, finite tangent and bounded far composition at every observer height.");
                }
            }
            bool rejected = false;
            try { OceanGuardianComposition.OrbitTime(double.NaN); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Invalid clocks cannot introduce NaN transforms.");
            Console.WriteLine("OceanGuardianCompositionTests PASS assertions=" + checks);
        }
#if OCEAN_GUARDIAN_TEST_MAIN
        public static void Main() => RunAll();
#endif
    }
}

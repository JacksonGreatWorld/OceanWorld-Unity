#if UNITY_EDITOR || OCEAN_MECHANIC_STANDALONE
using System;

namespace SwanUnderwater.EditorTests
{
    public static class OceanMechanicRulesTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool condition, string message)
        { Assertions++; if (!condition) throw new InvalidOperationException("Expedition mechanics: " + message); }
        static OceanMechanicInput Input(float time = 10f, int target = 0) => new OceanMechanicInput {
            Seconds = .05f, ActiveTime = time, Target = target, Tracked = true, Open = true, GazeAligned = true,
            HandMotion = .4f, InCurrent = true
        };
        static OceanMechanicOutcome Hold(OceanMechanicRules rules, OceanMechanicInput input, int steps = 50)
        {
            OceanMechanicOutcome outcome = OceanMechanicOutcome.None;
            for (int i = 0; i < steps; i++)
            {
                input.ActiveTime += input.Seconds;
                outcome = rules.Step(input);
                if (outcome != OceanMechanicOutcome.None) break;
            }
            return outcome;
        }
        public static void RunAll()
        {
            Assertions = 0;
            var current = new OceanMechanicRules(OceanMechanicKind.CurrentRiding, 1);
            var idle = Input(); idle.HandMotion = 0;
            Check(Hold(current, idle) == OceanMechanicOutcome.None, "stationary pointing cannot power current");
            Check(Hold(current, Input()) == OceanMechanicOutcome.Progress, "real open palm motion in current powers vane");
            var oxygen = new OceanMechanicRules(OceanMechanicKind.OxygenRelay, 1);
            var input = Input(1); input.PinchBegan = true;
            Check(oxygen.Step(input) == OceanMechanicOutcome.StageChanged && oxygen.Stage == 1, "available supply bubble loads oxygen");
            input.Target = 1;
            Check(oxygen.Step(input) == OceanMechanicOutcome.Progress, "oxygen requires a separate destination delivery");
            var unavailable = new OceanMechanicRules(OceanMechanicKind.OxygenRelay, 1);
            input = Input(6); input.PinchBegan = true;
            Check(unavailable.Step(input) == OceanMechanicOutcome.None, "submerged supply cannot be popped");
            var light = new OceanMechanicRules(OceanMechanicKind.DeepLight, 1);
            Check(Hold(light, Input()) == OceanMechanicOutcome.StageChanged && light.Stage == 1, "sustained hand light reveals organism");
            input = Input(); input.PinchBegan = true;
            Check(light.Step(input) == OceanMechanicOutcome.Progress, "revealed organism still needs a record gesture");
            var photo = new OceanMechanicRules(OceanMechanicKind.Observation, 1);
            input = Input(); input.GazeAligned = false;
            Check(Hold(photo, input) == OceanMechanicOutcome.None, "observation needs head and hand agreement");
            Check(Hold(photo, Input()) == OceanMechanicOutcome.StageChanged, "steady framed observation completes focusing");
            input = Input(); input.PinchBegan = true;
            Check(photo.Step(input) == OceanMechanicOutcome.Progress, "separate shutter pinch records observation");
            var escort = new OceanMechanicRules(OceanMechanicKind.Escort, 1);
            Check(Hold(escort, Input()) == OceanMechanicOutcome.None, "pointing does not complete undelivered escort");
            input = Input(); input.Arrived = true;
            Check(Hold(escort, input) == OceanMechanicOutcome.Progress, "calm escort inside safety ring completes delivery");
            var maze = new OceanMechanicRules(OceanMechanicKind.KelpMaze, 1);
            input = Input(10, 1); input.PinchBegan = true;
            Check(maze.Step(input) == OceanMechanicOutcome.Mistake && maze.Completed == 0, "wrong maze junction cannot advance");
            input = Input(12); input.PinchBegan = true;
            Check(maze.Step(input) == OceanMechanicOutcome.StageChanged && maze.Completed == 0, "safe junction selection still requires physical navigation");
            input.PinchBegan = false; input.Arrived = true;
            Check(maze.Step(input) == OceanMechanicOutcome.Progress && maze.MazeExit == 1, "safe junction crossing advances and changes route clue");
            foreach (var kind in new[] { OceanMechanicKind.ShipwreckLock, OceanMechanicKind.CoralRepair, OceanMechanicKind.Cleanup })
                CarryContract(kind);
            var jelly = new OceanMechanicRules(OceanMechanicKind.JellyfishDodge, 1);
            input = Input(.1f); input.InCurrent = false; input.PinchBegan = true;
            Check(jelly.Step(input) == OceanMechanicOutcome.Mistake, "discharging jellyfish denies pearl");
            input.ActiveTime = 3;
            Check(jelly.Step(input) == OceanMechanicOutcome.Progress, "jellyfish safe interval allows pearl");
            var octopus = new OceanMechanicRules(OceanMechanicKind.OctopusTracking, 1);
            input = Input(.1f, 2); input.PinchBegan = true;
            Check(octopus.Step(input) == OceanMechanicOutcome.Mistake, "camouflage decoy cannot count as octopus");
            input = Input(4.7f); input.PinchBegan = true;
            Check(octopus.Step(input) == OceanMechanicOutcome.Progress, "visible true octopus accepts tracked selection");
            var herd = new OceanMechanicRules(OceanMechanicKind.FishHerding, 1);
            Check(Hold(herd, Input()) == OceanMechanicOutcome.None, "herd progress requires actual fish arrival");
            input = Input(); input.Arrived = true;
            Check(Hold(herd, input) == OceanMechanicOutcome.Progress, "herd settles in target ring");
            var tide = new OceanMechanicRules(OceanMechanicKind.TideGates, 1);
            input = Input(1, 1); input.PinchBegan = true;
            Check(tide.Step(input) == OceanMechanicOutcome.None, "tide gate needs activation first");
            input.Target = 0; Check(tide.Step(input) == OceanMechanicOutcome.StageChanged, "tide lever arms gate");
            input.Target = 1; input.ActiveTime = 4;
            Check(tide.Step(input) == OceanMechanicOutcome.Mistake && tide.Stage == 0, "closed tide denies traversal and resets lever");
            input.Target = 0; input.ActiveTime = 6.2f; tide.Step(input); input.Target = 1;
            Check(tide.Step(input) == OceanMechanicOutcome.Progress, "active lever and correct tide complete gate");
            var ring = new OceanMechanicRules(OceanMechanicKind.RingRace, 1);
            input = Input(); input.PinchBegan = true;
            Check(ring.Step(input) == OceanMechanicOutcome.None, "a click cannot replace swimming through a ring");
            input.Arrived = true; Check(ring.Step(input) == OceanMechanicOutcome.Progress, "validated head crossing advances ordered ring");
            var rune = new OceanMechanicRules(OceanMechanicKind.RunePuzzle, 1);
            input = Input(); input.PinchBegan = true;
            Check(rune.Step(input) == OceanMechanicOutcome.StageChanged && rune.Stage == 1, "first remembered rune is partial only");
            input.Target = 1; Check(rune.Step(input) == OceanMechanicOutcome.Mistake && rune.Stage == 0, "wrong second rune resets sequence");
            for (int i = 0; i < 3; i++)
            {
                input.ActiveTime = 12; input.Target = rune.ExpectedRune;
                var outcome = rune.Step(input);
                Check(outcome == (i == 2 ? OceanMechanicOutcome.Progress : OceanMechanicOutcome.StageChanged), "rune sequence requires all three ordered presses");
            }
            var electric = new OceanMechanicRules(OceanMechanicKind.ElectricFinale, 1);
            Check(Hold(electric, Input()) == OceanMechanicOutcome.None, "one hand cannot charge twin electrodes");
            input = Input(); input.Bimanual = true;
            Check(Hold(electric, input) == OceanMechanicOutcome.StageChanged && electric.Stage == 1, "two distinct hand targets charge reactor");
            input = Input(12.2f, 2); input.PinchBegan = true;
            Check(electric.Step(input) == OceanMechanicOutcome.Progress, "charged reactor fires only on rhythm interval");
            PauseAndInvalidInputs();
#if UNITY_EDITOR
            UnityEngine.Debug.Log("OceanMechanicRulesTests PASS " + Assertions + " assertions; 16 distinct deterministic interaction contracts, no device input claim.");
#else
            Console.WriteLine("OceanMechanicRulesTests PASS " + Assertions + " assertions; 16 distinct deterministic interaction contracts, no device input claim.");
#endif
        }
        static void CarryContract(OceanMechanicKind kind)
        {
            var rules = new OceanMechanicRules(kind, 1);
            var input = Input(); input.PinchBegan = true; input.Pinching = true;
            Check(rules.Step(input) == OceanMechanicOutcome.StageChanged && rules.Stage == 1, kind + " picks up a physical item");
            input.PinchBegan = false;
            for (int i = 0; i < 6; i++) { input.ActiveTime += .05f; rules.Step(input); }
            if (kind == OceanMechanicKind.ShipwreckLock)
            {
                input.TwistDegrees = 30;
                Check(rules.Step(input) == OceanMechanicOutcome.StageChanged && rules.Stage == 2, "shipwreck key additionally needs angular turning");
            }
            input.Target = 1; input.Pinching = false; input.Released = true;
            Check(rules.Step(input) == OceanMechanicOutcome.Progress, kind + " correct socket delivery consumes item once");
            Check(rules.Step(input) == OceanMechanicOutcome.None && rules.Completed == 1, kind + " held release cannot repeat delivery");
            var cancelled = new OceanMechanicRules(kind, 1, 0, 1);
            Check(cancelled.Stage == 0, kind + " application restart never invents a held item");
            input = Input(); input.PinchBegan = true; input.Pinching = true; cancelled.Step(input); cancelled.Suspend();
            Check(cancelled.Stage == 0, kind + " pause cancels transient physical carry");
        }
        static void PauseAndInvalidInputs()
        {
            var rules = new OceanMechanicRules(OceanMechanicKind.DeepLight, 1);
            var input = Input();
            for (int i = 0; i < 10; i++) rules.Step(input);
            Check(rules.Dwell > 0, "active hand light accumulates dwell");
            input.Tracked = false; rules.Step(input);
            Check(rules.Dwell == 0 && rules.Stage == 0, "tracking loss cancels partial dwell");
            foreach (float invalid in new[] { 0, -.01f, .11f, float.NaN, float.PositiveInfinity })
            { input = Input(); input.Seconds = invalid; Check(rules.Step(input) == OceanMechanicOutcome.None && rules.Dwell == 0, "invalid/long step cannot gain progress"); }
            var photo = new OceanMechanicRules(OceanMechanicKind.Observation, 1);
            var harderPhoto = new OceanMechanicRules(OceanMechanicKind.Observation, 1.3f);
            for (int i = 0; i < 38; i++) { input = Input(); photo.Step(input); harderPhoto.Step(input); }
            Check(photo.Stage == 1 && harderPhoto.Stage == 0, "within-tier intensity actually tightens focus requirement");
        }
    }
}
#endif

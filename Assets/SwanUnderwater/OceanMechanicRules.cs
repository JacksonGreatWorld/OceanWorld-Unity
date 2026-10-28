using System;

namespace SwanUnderwater
{
    /// <summary>Validated hand/world observations for one active gameplay step, never raw SDK state.</summary>
    public struct OceanMechanicInput
    {
        public float Seconds, ActiveTime, HandMotion, TwistDegrees;
        public int Target;
        public bool Tracked, Open, PinchBegan, Pinching, Released, GazeAligned, Bimanual, Arrived, InCurrent, WrongRoute;
    }

    public enum OceanMechanicOutcome { None, StageChanged, Progress, Mistake }

    /// <summary>
    /// Small deterministic interaction contracts. Durable count/stage remain in the campaign run;
    /// dwell, held gestures and reaction windows are deliberately discarded across pause/tracking loss.
    /// </summary>
    public sealed class OceanMechanicRules
    {
        public OceanMechanicKind Kind { get; }
        public int Stage { get; private set; }
        public float Dwell { get; private set; }
        public float Oxygen { get; private set; } = 36f;
        public float CarrySeconds { get; private set; }
        public int Completed { get; private set; }
        readonly float intensity;
        bool holding;
        float blockedUntil;
        public OceanMechanicRules(OceanMechanicKind kind, float difficulty, int completed = 0, int stage = 0)
        {
            Kind = kind; intensity = Math.Max(.5f, Math.Min(1.5f, difficulty));
            Completed = Math.Max(0, completed); Stage = stage >= 0 && stage <= MaximumStage(kind) ? stage : 0;
            // A physical item cannot survive an application restart still held in a vanished hand.
            if (CarriesItem(kind)) Stage = 0;
        }
        public static bool CarriesItem(OceanMechanicKind kind) => kind == OceanMechanicKind.ShipwreckLock ||
            kind == OceanMechanicKind.CoralRepair || kind == OceanMechanicKind.Cleanup;
        static int MaximumStage(OceanMechanicKind kind)
        {
            switch (kind)
            {
                case OceanMechanicKind.ShipwreckLock: case OceanMechanicKind.RunePuzzle: return 2;
                case OceanMechanicKind.OxygenRelay: case OceanMechanicKind.DeepLight: case OceanMechanicKind.Observation:
                case OceanMechanicKind.KelpMaze: case OceanMechanicKind.CoralRepair: case OceanMechanicKind.Cleanup:
                case OceanMechanicKind.TideGates: case OceanMechanicKind.ElectricFinale: return 1;
                default: return 0;
            }
        }
        public static float Cycle(float time, float period) => time - (float)Math.Floor(time / period) * period;
        public bool JellySafe(float time) => Cycle(time + Completed * .71f, 5f) >= 2.1f * intensity;
        public bool TideOpen(float time) => Cycle(time, 6f) < 2.8f / intensity;
        public bool ElectricBeat(float time) => Cycle(time, 2.4f) < .85f / intensity;
        public bool OctopusVisible(float time) => Cycle(time, 4.5f) < 1.5f;
        public bool BubbleAvailable(float time) => Cycle(time, 7f) < 4.8f;
        public int ExpectedRune => (Completed + Stage * 2) % 3;
        public int MazeExit => Completed % 2;
        public void Suspend()
        {
            Dwell = 0; CarrySeconds = 0; holding = false;
            // Focus is a continuous head/hand observation, not an earned node.
            // Resuming after a menu or lost tracking must focus again before a shutter.
            if (CarriesItem(Kind) || Kind == OceanMechanicKind.Observation) Stage = 0;
        }
        OceanMechanicOutcome Progress()
        { Completed++; Stage = 0; Dwell = 0; CarrySeconds = 0; holding = false; return OceanMechanicOutcome.Progress; }
        OceanMechanicOutcome Mistake(float time)
        { Dwell = 0; blockedUntil = time + .7f; return OceanMechanicOutcome.Mistake; }
        bool Hold(bool eligible, float seconds, float dt)
        { Dwell = eligible ? Dwell + dt : 0f; return Dwell >= seconds; }
        public OceanMechanicOutcome Step(OceanMechanicInput input)
        {
            if (!input.Tracked || !Finite(input.Seconds) || input.Seconds <= 0 || input.Seconds > .1f ||
                !Finite(input.ActiveTime) || !Finite(input.HandMotion) || !Finite(input.TwistDegrees))
            { Suspend(); return OceanMechanicOutcome.None; }
            float dt = input.Seconds, now = input.ActiveTime;
            if (now < blockedUntil) return OceanMechanicOutcome.None;
            bool primary = input.Target == 0;
            switch (Kind)
            {
                case OceanMechanicKind.CurrentRiding:
                    // A real open-palm stroke powers the vane; merely pointing or waiting does not.
                    if (Hold(primary && input.Open && input.HandMotion > .11f && input.InCurrent,
                        1.1f * intensity, dt)) return Progress();
                    break;
                case OceanMechanicKind.OxygenRelay:
                    Oxygen = Math.Max(0, Oxygen - dt);
                    if (Oxygen == 0) { Oxygen = 36f; Stage = 0; return Mistake(now); }
                    if (Stage == 0 && primary && input.PinchBegan && BubbleAvailable(now))
                    { Stage = 1; Oxygen = 36f; return OceanMechanicOutcome.StageChanged; }
                    if (Stage == 1 && input.Target == 1 && input.PinchBegan) return Progress();
                    break;
                case OceanMechanicKind.DeepLight:
                    if (Stage == 0 && Hold(primary && input.Open, 1.3f * intensity, dt))
                    { Stage = 1; Dwell = 0; return OceanMechanicOutcome.StageChanged; }
                    if (Stage == 1 && primary && input.PinchBegan) return Progress();
                    break;
                case OceanMechanicKind.Observation:
                    if (Stage == 0 && Hold(primary && input.Open && input.GazeAligned, 1.8f * intensity, dt))
                    { Stage = 1; Dwell = 0; return OceanMechanicOutcome.StageChanged; }
                    if (Stage == 1 && primary && input.GazeAligned && input.PinchBegan) return Progress();
                    if (Stage == 1 && (!primary || !input.GazeAligned)) { Stage = 0; return OceanMechanicOutcome.StageChanged; }
                    break;
                case OceanMechanicKind.Escort:
                    if (input.Open && input.Arrived && Hold(true, .7f, dt)) return Progress();
                    if (!input.Arrived) Dwell = 0;
                    break;
                case OceanMechanicKind.KelpMaze:
                    if (input.WrongRoute) { Stage = 0; return Mistake(now); }
                    if (Stage == 1 && input.Arrived) return Progress();
                    if (input.PinchBegan && input.Target >= 0)
                    { if (input.Target == MazeExit) { Stage = 1; return OceanMechanicOutcome.StageChanged; } return Mistake(now); }
                    break;
                case OceanMechanicKind.ShipwreckLock:
                case OceanMechanicKind.CoralRepair:
                case OceanMechanicKind.Cleanup:
                    if (Stage == 0 && primary && input.PinchBegan)
                    { Stage = 1; holding = true; CarrySeconds = 0; return OceanMechanicOutcome.StageChanged; }
                    if (Stage == 1)
                    {
                        CarrySeconds += dt;
                        if (Kind == OceanMechanicKind.ShipwreckLock && input.Pinching && Math.Abs(input.TwistDegrees) >= 22f)
                        { Stage = 2; return OceanMechanicOutcome.StageChanged; }
                        if (input.Released || (!input.Pinching && holding))
                        {
                            bool socket = input.Target == 1 && CarrySeconds >= .25f;
                            Stage = 0; holding = false;
                            if (socket && Kind != OceanMechanicKind.ShipwreckLock) return Progress();
                            return Mistake(now);
                        }
                    }
                    else if (Stage == 2 && input.Released)
                    {
                        bool socket = input.Target == 1;
                        Stage = 0; holding = false;
                        return socket ? Progress() : Mistake(now);
                    }
                    break;
                case OceanMechanicKind.JellyfishDodge:
                    if (primary && input.PinchBegan) return JellySafe(now) ? Progress() : Mistake(now);
                    if (input.InCurrent && !JellySafe(now)) return Mistake(now);
                    break;
                case OceanMechanicKind.OctopusTracking:
                    if (input.PinchBegan && input.Target >= 0)
                        return primary && OctopusVisible(now) ? Progress() : Mistake(now);
                    break;
                case OceanMechanicKind.FishHerding:
                    if (input.Arrived && Hold(input.Open, .4f, dt)) return Progress();
                    if (!input.Arrived) Dwell = 0;
                    break;
                case OceanMechanicKind.TideGates:
                    if (Stage == 0 && primary && input.PinchBegan)
                    { Stage = 1; return OceanMechanicOutcome.StageChanged; }
                    if (Stage == 1 && input.Target == 1 && input.PinchBegan)
                    { if (TideOpen(now)) return Progress(); Stage = 0; return Mistake(now); }
                    break;
                case OceanMechanicKind.RingRace:
                    if (input.Arrived) return Progress();
                    break;
                case OceanMechanicKind.RunePuzzle:
                    if (input.PinchBegan && input.Target >= 0)
                    {
                        if (input.Target != ExpectedRune) { Stage = 0; return Mistake(now); }
                        Stage++;
                        if (Stage >= 3) return Progress();
                        return OceanMechanicOutcome.StageChanged;
                    }
                    break;
                case OceanMechanicKind.ElectricFinale:
                    if (Stage == 0 && Hold(input.Bimanual && input.Open, 1.5f * intensity, dt))
                    { Stage = 1; Dwell = 0; return OceanMechanicOutcome.StageChanged; }
                    if (Stage == 1 && input.Target == 2 && input.PinchBegan)
                    { if (ElectricBeat(now)) return Progress(); Stage = 0; return Mistake(now); }
                    break;
            }
            return OceanMechanicOutcome.None;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

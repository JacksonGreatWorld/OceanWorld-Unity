#if UNITY_EDITOR || SINGLE_HAND_TURN_TEST_MAIN
using System;
using System.Numerics;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual stroke recognizer + actual steering model, with explicit tracking-space trajectories.</summary>
    public static class SingleHandTurnTests
    {
        sealed class Simulation
        {
            public readonly WaterStrokeModel Stroke = new WaterStrokeModel();
            public readonly SingleHandTurnModel Turn = new SingleHandTurnModel();
            public readonly float Dt;
            public Vector3 Left = new Vector3(-0.18f, 1.15f, 0.48f), Right = new Vector3(0.18f, 1.15f, 0.48f);
            public Vector3 Head = new Vector3(0.04f, 1.65f, 0.03f), Forward = Vector3.UnitZ;
            public bool LeftTracked = true, RightTracked = true, Suppressed;
            public Vector3 Velocity;
            public float Yaw, AbsoluteYaw, PeakYawSpeed, LastYaw;
            public WaterStrokeModel.Output Last;
            Vector3 origin = new Vector3(2f, 0.4f, -3f);
            Quaternion orientation = Quaternion.Identity;

            public Simulation(int hz = 90) { Dt = 1f / hz; }
            public void Sample()
            {
                Last = Stroke.Step(Dt, new WaterStrokeModel.HandSample(LeftTracked, Left, Suppressed),
                    new WaterStrokeModel.HandSample(RightTracked, Right, Suppressed), Head, Forward);
                var worldOutput = Last;
                worldOutput.Direction = Vector3.Transform(Last.Direction, orientation);
                Velocity = WaterStrokeModel.AdvanceVelocity(Velocity, worldOutput, Dt, 1.6f, 2.5f, 0.7f);
                origin += Velocity * Dt;
                LastYaw = Turn.Step(Dt, Last.LeftStrength, Last.RightStrength, LeftTracked, RightTracked,
                    Last.TrackingLost, Suppressed);
                Require(!float.IsNaN(LastYaw) && Math.Abs(LastYaw) <= 25f * Dt + 0.0001f, "bounded finite yaw step");
                if (LastYaw != 0f)
                {
                    Vector3 pivot = origin + Vector3.Transform(Head, orientation);
                    Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, LastYaw * (float)Math.PI / 180f);
                    float speedBefore = Velocity.Length(), verticalBefore = Velocity.Y;
                    origin = pivot + Vector3.Transform(origin - pivot, rotation);
                    orientation = Quaternion.Normalize(rotation * orientation);
                    Velocity = Vector3.Transform(Velocity, rotation);
                    Require(Vector3.Distance(pivot, origin + Vector3.Transform(Head, orientation)) < 0.00002f,
                        "off-centre head stays at rotation pivot");
                    Require(Math.Abs(Velocity.Length() - speedBefore) < 0.00002f && Math.Abs(Velocity.Y - verticalBefore) < 0.00002f,
                        "yaw preserves speed and ascent/descent component");
                    Vector3 recoveredLocalHead = Vector3.Transform(pivot - origin, Quaternion.Conjugate(orientation));
                    Require(Vector3.Distance(recoveredLocalHead, Head) < 0.00002f, "artificial yaw cannot change tracked local head pose");
                }
                Yaw += LastYaw;
                AbsoluteYaw += Math.Abs(LastYaw);
                PeakYawSpeed = Math.Max(PeakYawSpeed, Math.Abs(Turn.YawVelocity));
            }
            public void Hold(float seconds)
            {
                for (int i = 0, count = (int)Math.Ceiling(seconds / Dt); i < count; i++) Sample();
            }
            public void Move(Vector3 left, Vector3 right, float seconds)
            {
                Vector3 fromLeft = Left, fromRight = Right;
                int count = (int)Math.Ceiling(seconds / Dt);
                for (int i = 1; i <= count; i++)
                {
                    Left = Vector3.Lerp(fromLeft, left, i / (float)count);
                    Right = Vector3.Lerp(fromRight, right, i / (float)count);
                    Sample();
                }
            }
        }

        static int assertions;
        static readonly Vector3 LeftPull = new Vector3(-0.40f, 1.15f, 0.13f);
        static readonly Vector3 RightPull = new Vector3(0.40f, 1.15f, 0.13f);
        static void Require(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Single-hand steering failed: " + message);
        }
        static void Report(string name, Simulation s)
        {
            Log($"PASS {name}: yaw={s.Yaw:F3}deg, absoluteYaw={s.AbsoluteYaw:F3}deg, peakYaw={s.PeakYawSpeed:F3}deg/s, " +
                $"pulls={s.Stroke.LeftStrokeCount}/{s.Stroke.RightStrokeCount}");
        }
        static void Log(string message)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.Log(message);
#else
            Console.WriteLine(message);
#endif
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Swan Underwater/Validate Single-Hand Paddle Steering")]
#endif
        public static void RunAll()
        {
            assertions = 0;
            foreach (int hz in new[] { 60, 72, 90, 120 })
            {
                var left = new Simulation(hz);
                left.Hold(0.4f);
                left.Move(LeftPull, left.Right, 0.85f);
                Require(left.Yaw > 3f && left.Yaw < 20f && left.PeakYawSpeed <= 25.0001f, "left pull turns right comfortably");
                Require(left.Stroke.LeftStrokeCount == 1 && left.Stroke.RightStrokeCount == 0, "steering requires one real left stroke");
                float during = left.Yaw;
                left.Hold(2f);
                Require(left.Turn.YawVelocity == 0f && left.Yaw - during < 5f && left.Stroke.CompletedStrokeCount == 1,
                    "stopping hands ends yaw and rig yaw never fabricates extra strokes");
                Report("left/rightward + stop @" + hz, left);

                var right = new Simulation(hz);
                right.Hold(0.4f);
                right.Move(right.Left, RightPull, 0.85f);
                right.Hold(2f);
                Require(right.Yaw < -3f && Math.Abs(right.Yaw + left.Yaw) < 0.1f, "right pull turns left symmetrically");
                Report("right/leftward @" + hz, right);

                foreach (float offset in new[] { -0.25f, -0.12f, 0f, 0.12f, 0.25f })
                {
                    var both = new Simulation(hz);
                    both.Hold(0.4f);
                    Vector3 l0 = both.Left, r0 = both.Right;
                    float leftDelay = Math.Max(0f, offset), rightDelay = Math.Max(0f, -offset);
                    int count = (int)Math.Ceiling((0.85f + Math.Abs(offset)) / both.Dt);
                    for (int i = 1; i <= count; i++)
                    {
                        float time = i * both.Dt;
                        both.Left = Vector3.Lerp(l0, LeftPull, Clamp01((time - leftDelay) / 0.85f));
                        both.Right = Vector3.Lerp(r0, RightPull, Clamp01((time - rightDelay) / 0.85f));
                        both.Sample();
                    }
                    both.Hold(0.7f);
                    Require(both.AbsoluteYaw < 0.25f && both.Stroke.CompletedStrokeCount == 2,
                        "bilateral pulls offset up to 250ms do not snake or lose propulsion");
                    Require(both.Velocity.Length() > 0.5f, "bilateral stroke still propels");
                    Report("both offset=" + offset + " @" + hz, both);
                }

                foreach (float duration in new[] { 0.25f, 0.35f, 0.5f })
                {
                    foreach (bool leftVisible in new[] { true, false })
                    {
                        var quick = new Simulation(hz) { LeftTracked = leftVisible, RightTracked = !leftVisible };
                        quick.Hold(0.4f);
                        quick.Move(leftVisible ? LeftPull : quick.Left, leftVisible ? quick.Right : RightPull, duration);
                        float lastTurningAfterStop = 0f;
                        for (int i = 1; i <= (int)Math.Ceiling(0.8f / quick.Dt); i++)
                        {
                            quick.Sample();
                            if (Math.Abs(quick.LastYaw) > 0.00001f) lastTurningAfterStop = i * quick.Dt;
                        }
                        Require(quick.Yaw * (leftVisible ? 1f : -1f) >= 2.5f && quick.AbsoluteYaw <= 4.5f,
                            "a confirmed short pull gives a perceptible finite turn even if it ended during pairing");
                        Require(lastTurningAfterStop <= 0.65f && quick.Turn.YawVelocity == 0f,
                            "deferred short-pull turn has a short bounded tail");
                        float stoppedAngle = quick.Yaw;
                        quick.Hold(1f);
                        Require(quick.Yaw == stoppedAngle && quick.Stroke.CompletedStrokeCount == 1,
                            "holding a stopped hand cannot refill the angular bank or create another stroke");
                        if (hz == 90) Log($"PASS short pull {(leftVisible ? "left" : "right")} {duration:F2}s: " +
                            $"yaw={quick.Yaw:F3}deg, tail={lastTurningAfterStop:F3}s, pulls={quick.Stroke.CompletedStrokeCount}");
                    }

                    foreach (float offset in new[] { -0.25f, 0f, 0.25f })
                    {
                        var quickPair = new Simulation(hz);
                        quickPair.Hold(0.4f);
                        Vector3 l0 = quickPair.Left, r0 = quickPair.Right;
                        float leftDelay = Math.Max(0f, offset), rightDelay = Math.Max(0f, -offset);
                        int frames = (int)Math.Ceiling((duration + Math.Abs(offset)) / quickPair.Dt);
                        for (int i = 1; i <= frames; i++)
                        {
                            float time = i * quickPair.Dt;
                            quickPair.Left = Vector3.Lerp(l0, LeftPull, Clamp01((time - leftDelay) / duration));
                            quickPair.Right = Vector3.Lerp(r0, RightPull, Clamp01((time - rightDelay) / duration));
                            quickPair.Sample();
                        }
                        quickPair.Hold(1f);
                        Require(quickPair.AbsoluteYaw == 0f && quickPair.Stroke.CompletedStrokeCount == 2,
                            "a paired short pull cancels the deferred angular bank without snaking");
                    }
                }
            }

            foreach (bool leftVisible in new[] { true, false })
            {
                var one = new Simulation { LeftTracked = leftVisible, RightTracked = !leftVisible };
                one.Hold(0.4f);
                one.Move(leftVisible ? LeftPull : one.Left, leftVisible ? one.Right : RightPull, 0.85f);
                Require(one.Yaw * (leftVisible ? 1f : -1f) > 3f && one.Stroke.CompletedStrokeCount == 1,
                    "stable one-hand visibility accepts a new real pull without waiting for the other hand");
                Report(leftVisible ? "only left ever visible" : "only right ever visible", one);
            }

            var loss = new Simulation();
            loss.Hold(0.4f);
            Vector3 leftReach = loss.Left, rightReach = loss.Right;
            loss.Move(Vector3.Lerp(leftReach, LeftPull, 0.45f), Vector3.Lerp(rightReach, RightPull, 0.45f), 0.4f);
            loss.RightTracked = false;
            loss.Move(LeftPull, RightPull, 0.45f);
            Require(loss.AbsoluteYaw == 0f, "losing a partner mid bilateral pull cannot convert remaining thrust into a turn");
            loss.Hold(0.4f);
            loss.Move(leftReach, loss.Right, 0.5f);
            loss.Hold(0.25f);
            loss.Move(LeftPull, loss.Right, 0.85f);
            Require(loss.Yaw > 3f, "new stable single-hand pull after loss and recovery can turn");
            loss.RightTracked = true;
            loss.Sample();
            Require(loss.LastYaw == 0f && loss.Turn.YawVelocity == 0f, "reappearing partner stops old yaw immediately");
            float beforeRecovery = loss.Yaw;
            loss.Hold(0.5f);
            Require(loss.Yaw == beforeRecovery, "stationary recovery never resumes stale yaw");
            Report("bilateral loss, new solo stroke, reacquisition", loss);

            var turningLoss = new Simulation();
            turningLoss.Hold(0.4f);
            turningLoss.Move(Vector3.Lerp(turningLoss.Left, LeftPull, 0.75f), turningLoss.Right, 0.65f);
            Require(turningLoss.Yaw > 1f, "test enters actual turning before loss");
            turningLoss.RightTracked = false;
            turningLoss.Sample();
            float stoppedYaw = turningLoss.Yaw;
            turningLoss.Move(LeftPull, turningLoss.Right, 0.4f);
            Require(turningLoss.Yaw == stoppedYaw && turningLoss.Turn.YawVelocity == 0f,
                "visibility change instantly stops yaw and continuing old pull cannot rearm it");
            turningLoss.LeftTracked = false;
            turningLoss.Hold(0.5f);
            Require(turningLoss.Yaw == stoppedYaw, "complete loss cannot turn");
            Report("loss while turning", turningLoss);

            var ui = new Simulation();
            ui.Hold(0.4f);
            ui.Move(ui.Left + Vector3.UnitY * 0.55f, ui.Right, 0.7f);
            ui.Hold(0.3f);
            Require(ui.AbsoluteYaw == 0f && ui.Stroke.CompletedStrokeCount == 0, "ordinary hand raise does not steer");
            ui.Suppressed = true;
            ui.Left = leftReach; ui.Right = rightReach;
            ui.Hold(0.4f);
            ui.Move(LeftPull, ui.Right, 0.85f);
            Require(ui.AbsoluteYaw == 0f, "pinch/UI capture blocks paddle-shaped steering");
            Report("UI raise and suppression", ui);

            var jitter = new Simulation();
            jitter.Hold(0.4f);
            for (int i = 0; i < 900; i++)
            {
                jitter.Left = leftReach + new Vector3((float)Math.Sin(i * 0.7f) * 0.003f, 0f, 0f);
                jitter.Forward = new Vector3((float)Math.Sin(i * 0.002f), 0f, (float)Math.Cos(i * 0.002f));
                jitter.Sample();
            }
            Require(jitter.AbsoluteYaw == 0f && jitter.Stroke.CompletedStrokeCount == 0, "jitter/head-only looking cannot steer");
            Report("stationary jitter and looking", jitter);

            var model = new SingleHandTurnModel();
            for (int i = 0; i < 40; i++) model.Step(1f / 90f, 0f, 0f, true, false);
            float previous = 0f, angle = 0f;
            for (int i = 0; i < 90; i++)
            {
                angle += model.Step(1f / 90f, 1f, 0f, true, false);
                Require(model.YawVelocity - previous <= 100f / 90f + 0.0001f, "angular acceleration is bounded");
                previous = model.YawVelocity;
            }
            Require(angle > 10f, "confirmed continuous push produces useful yaw");
            Require(model.Step(0.2f, 1f, 0f, true, false) == 0f && model.YawVelocity == 0f, "long frame gap clears turn");
            Require(model.Step(float.NaN, 1f, 0f, true, false) == 0f, "invalid timing cannot rotate");
            Require(model.Step(1f / 90f, float.NaN, 0f, true, false) == 0f, "invalid strength cannot rotate");
            Log($"PASS SingleHandTurn suite: {assertions} assertions. Synthetic trajectories do not replace headset comfort validation.");
        }
        static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

#if SINGLE_HAND_TURN_TEST_MAIN
        public static int Main()
        {
            try { RunAll(); return 0; }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }
#endif
    }
}
#endif

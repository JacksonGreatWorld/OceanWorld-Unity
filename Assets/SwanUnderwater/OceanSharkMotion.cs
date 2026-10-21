using System;
using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>
    /// Per-animal steering for campaign sharks. The owner supplies a +Z-facing motion wrapper,
    /// validated water segments and gameplay time; imported visual transforms and swim shaders stay owned by the model.
    /// </summary>
    public sealed class OceanSharkMotion : MonoBehaviour
    {
        public const float HomeRadius = 16f;
        public const float MaximumSpeed = 2.60f;
        public const float MaximumTurnRate = 118f;
        public const float MaximumPitch = 12f;
        public const float MaximumBank = 8f;

        static readonly float[] CandidateAngles = { 0f, 28f, -28f, 55f, -55f, 85f, -85f, 125f, -125f, 180f };
        Func<Vector3, Vector3, bool> segmentClear;
        Vector3 home, goal, escapeStart, escapeAway, escapeSide, blockedHeading;
        float clearance, clock, goalTimer, escapeTimer, dodgeTimer, aimHeld, dodgeCooldown, escapeBreakTimer, blockedTimer;
        float speed, yaw, pitch, bank, patrolPhase;
        float cruiseSpeed, aimDelay;
        uint randomState;
        bool configured, hasGoal, escaping, dodging, captureReady, escapeFollowThrough;

        public bool Escaping => escaping;
        public Vector3 EscapeTarget { get; private set; }
        public float EscapeDistance { get; private set; }
        public int EscapeCount { get; private set; }
        public int DodgeCount { get; private set; }
        public float Speed => speed;
        public Vector3 HomePosition => home;
        public string MotionState => captureReady ? "capture" : escaping ? "escape" : dodging ? "dodge" : hasGoal ? "cruise" : "holding";

        public void Configure(int identity, float bodyClearance, Func<Vector3, Vector3, bool> waterSegmentClear)
        {
            if (!Finite(transform.position) || !Finite(transform.forward))
                throw new InvalidOperationException("Shark motion requires a finite world transform.");
            if (!Finite(bodyClearance) || bodyClearance <= 0f)
                throw new ArgumentOutOfRangeException(nameof(bodyClearance));
            segmentClear = waterSegmentClear ?? throw new ArgumentNullException(nameof(waterSegmentClear));
            clearance = bodyClearance;
            home = transform.position;
            Vector3 forward = transform.forward;
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude < .0001f) flat = Vector3.forward;
            flat.Normalize();
            yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg, -MaximumPitch, MaximumPitch);
            bank = 0f;
            randomState = unchecked((uint)identity * 747796405u + 2891336453u);
            if (randomState == 0u) randomState = 1u;
            cruiseSpeed = .60f + NextUnit() * .24f;
            aimDelay = .24f + NextUnit() * .10f;
            patrolPhase = NextUnit() * Mathf.PI * 2f;
            clock = goalTimer = escapeTimer = dodgeTimer = aimHeld = dodgeCooldown = speed = blockedTimer = escapeBreakTimer = 0f;
            EscapeCount = DodgeCount = 0; EscapeDistance = 0f;
            EscapeTarget = escapeStart = home;
            escaping = dodging = captureReady = escapeFollowThrough = false;
            hasGoal = false; configured = true;
            // Configuration and hit notifications never rotate or relocate an existing animal.
        }

        public void Tick(float dt, Vector3 playerPosition, bool aimThreat, Ray aimRay, bool readyToCapture)
        {
            if (!configured || !Finite(dt) || dt <= 0f || dt > .10f || !Finite(playerPosition)) return;
            clock += dt;
            goalTimer = Mathf.Max(0f, goalTimer - dt);
            dodgeCooldown = Mathf.Max(0f, dodgeCooldown - dt);
            blockedTimer = Mathf.Max(0f, blockedTimer - dt);
            captureReady = readyToCapture;
            if (captureReady)
            {
                // A completed target becomes reachable. It stays at its earned location rather than snapping to the diver.
                escaping = dodging = hasGoal = false; aimHeld = 0f;
                speed = Mathf.MoveTowards(speed, 0f, dt * 5.5f);
                bank = Mathf.MoveTowards(bank, 0f, dt * 24f);
                pitch = Mathf.MoveTowards(pitch, 0f, dt * 24f);
                ApplyPoseAndMove(dt);
                return;
            }

            if (escaping)
            {
                escapeTimer -= dt;
                escapeBreakTimer -= dt;
                // A short lateral break precedes the longer retreat. Do not simply follow one
                // straight hit-to-goal ray for the whole response; that made repeated hits look rigid.
                if (!escapeFollowThrough && escapeBreakTimer <= 0f)
                {
                    escapeFollowThrough = true;
                    Vector3 retreat = escapeAway + escapeSide * .24f;
                    if (TryPlanOffset(retreat, 3.5f + NextUnit() * .5f, true)) EscapeTarget = goal;
                    else goalTimer = .08f;
                }
                if (escapeTimer <= 0f || (escapeFollowThrough && Vector3.Distance(transform.position, EscapeTarget) < .25f))
                { escaping = false; hasGoal = false; goalTimer = .10f; }
            }
            if (dodging)
            {
                dodgeTimer -= dt;
                if (dodgeTimer <= 0f || Vector3.Distance(transform.position, goal) < .25f)
                { dodging = false; hasGoal = false; goalTimer = .15f; }
            }
            UpdateAimThreat(dt, aimThreat, aimRay);

            if (!hasGoal && goalTimer <= 0f)
            {
                if (escaping)
                {
                    // A clear straight route can still have an obstructed turning arc. Replan at most
                    // nine times per second, preserving the hit reaction instead of falling back to a patrol.
                    Vector3 direction = escapeFollowThrough ? escapeAway + escapeSide * .24f : escapeAway * .70f + escapeSide * .85f;
                    if (TryPlanOffset(direction, escapeFollowThrough ? 3.5f : 2.2f, true)) EscapeTarget = goal;
                    goalTimer = .12f;
                }
                else PlanCruise(playerPosition);
            }
            if (hasGoal && !escaping && !dodging && (Vector3.Distance(transform.position, goal) < .5f || goalTimer <= 0f))
                PlanCruise(playerPosition);

            Vector3 desired = hasGoal ? goal - transform.position : transform.forward;
            float desiredYaw = yaw, desiredPitch = 0f;
            if (desired.sqrMagnitude > .0001f)
            {
                desiredYaw = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
                desiredPitch = Mathf.Clamp(-Mathf.Atan2(desired.y,
                    Mathf.Sqrt(desired.x * desired.x + desired.z * desired.z)) * Mathf.Rad2Deg, -MaximumPitch, MaximumPitch);
            }
            float oldYaw = yaw;
            float turnRate = escaping || dodging ? MaximumTurnRate : 68f;
            yaw = Mathf.MoveTowardsAngle(yaw, desiredYaw, dt * turnRate);
            pitch = Mathf.MoveTowards(pitch, desiredPitch, dt * 23f);
            float yawRate = Mathf.DeltaAngle(oldYaw, yaw) / dt;
            bank = Mathf.MoveTowards(bank, Mathf.Clamp(-yawRate * .075f, -MaximumBank, MaximumBank), dt * 35f);

            // Translation follows the animal's nose. A tight turn slows first instead of sliding sideways/backwards.
            float angle = Mathf.Abs(Mathf.DeltaAngle(yaw, desiredYaw));
            float alignment = Mathf.Clamp01(Mathf.Cos(Mathf.Min(90f, angle) * Mathf.Deg2Rad));
            alignment *= alignment;
            if (angle > 72f || !hasGoal) alignment = 0f;
            float targetSpeed = (escaping ? 2.40f : dodging ? 1.65f : cruiseSpeed * (1f + .12f * Mathf.Sin(clock * .67f))) * alignment;
            speed = Mathf.MoveTowards(speed, targetSpeed, dt * (targetSpeed < speed ? 4.5f : escaping || dodging ? 3.6f : 1.8f));
            // Residual speed cannot carry an animal through a turn that its body has not made yet.
            speed = Mathf.Min(speed, MaximumSpeed * alignment);
            ApplyPoseAndMove(dt);
            if (escaping) EscapeDistance = Vector3.Distance(escapeStart, transform.position);
        }

        public void ReactToHit(Vector3 incomingDirection, Vector3 threatPosition)
        {
            if (!configured || captureReady || !Finite(incomingDirection) || !Finite(threatPosition)) return;
            EscapeCount++;
            escapeStart = transform.position; EscapeDistance = 0f;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - threatPosition, Vector3.up);
            Vector3 incoming = Vector3.ProjectOnPlane(incomingDirection, Vector3.up);
            if (away.sqrMagnitude < .001f) away = incoming;
            if (away.sqrMagnitude < .001f) away = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            away.Normalize();
            if (incoming.sqrMagnitude > .001f) away = (away + incoming.normalized * .35f).normalized;
            escapeAway = away;
            Vector3 lateral = Vector3.Cross(Vector3.up, away);
            float turn = Vector3.Dot(lateral, transform.forward);
            float side = Mathf.Abs(turn) > .15f ? Mathf.Sign(turn) : NextUnit() < .5f ? -1f : 1f;
            escapeSide = lateral * side;
            escapeFollowThrough = false; escapeBreakTimer = .82f;
            dodging = false; aimHeld = 0f; dodgeCooldown = 1.35f;
            escaping = TryPlanOffset(away * .70f + escapeSide * .85f, 2.35f + NextUnit() * .25f, true);
            EscapeTarget = escaping ? goal : transform.position;
            escapeTimer = escaping ? 5.0f : 0f;
            goalTimer = escaping ? escapeTimer : .12f;
        }

        void UpdateAimThreat(float dt, bool threat, Ray ray)
        {
            if (escaping || dodging || dodgeCooldown > 0f || !threat || !Finite(ray.origin) || !Finite(ray.direction) || ray.direction.sqrMagnitude < .5f)
            { aimHeld = 0f; return; }
            Vector3 rayDirection = ray.direction.normalized;
            Vector3 relative = transform.position - ray.origin;
            float forward = Vector3.Dot(relative, rayDirection);
            Vector3 offset = relative - rayDirection * forward;
            if (forward < .15f || forward > 13f || offset.sqrMagnitude > (clearance + .65f) * (clearance + .65f))
            { aimHeld = 0f; return; }
            aimHeld += dt;
            if (aimHeld < aimDelay) return;
            aimHeld = 0f; dodgeCooldown = 1.65f + NextUnit() * .65f;
            Vector3 side = Vector3.Cross(Vector3.up, rayDirection);
            if (side.sqrMagnitude < .01f) side = transform.right;
            if (Vector3.Dot(offset, side) < -.03f || (offset.sqrMagnitude < .01f && NextUnit() < .5f)) side = -side;
            Vector3 direction = side.normalized + Vector3.ProjectOnPlane(transform.forward, Vector3.up) * .38f;
            if (TryPlanOffset(direction.normalized, 2.2f, false))
            { dodging = true; dodgeTimer = 2.2f; goalTimer = dodgeTimer; DodgeCount++; }
        }

        void PlanCruise(Vector3 player)
        {
            hasGoal = false; goalTimer = .55f;
            patrolPhase += .40f + NextUnit() * .36f;
            // Long, non-circular arcs have stable headings, with bounded vertical changes.
            Vector3 orbit = home + new Vector3(Mathf.Sin(patrolPhase) * 6.4f,
                Mathf.Sin(patrolPhase * .73f) * .65f, Mathf.Cos(patrolPhase) * 5.1f);
            Vector3 direction = orbit - transform.position;
            Vector3 nearPlayer = transform.position - player;
            if (nearPlayer.sqrMagnitude < 2.8f * 2.8f && nearPlayer.sqrMagnitude > .001f)
                direction += nearPlayer.normalized * 2.1f;
            if (direction.sqrMagnitude < .01f) direction = transform.forward;
            if (TryPlanOffset(direction.normalized, 3.8f + NextUnit() * 1.7f, false))
            {
                // Blend vertical drift into the accepted horizontal route only after validating its complete segment.
                Vector3 elevated = goal; elevated.y = Mathf.Clamp(orbit.y, home.y - 1.5f, home.y + 1.5f);
                if (ValidSegment(transform.position, elevated)) goal = elevated;
                goalTimer = 3.8f + NextUnit() * 1.8f;
            }
        }

        bool TryPlanOffset(Vector3 direction, float distance, bool flee)
        {
            Vector3 origin = transform.position;
            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (direction.sqrMagnitude < .001f) direction = Vector3.forward;
            direction.Normalize();
            // Fixed candidate budget; no allocations and no unbounded path search on the render thread.
            // Score the animal's current heading as well as its goal. The old first-clear choice could
            // repeatedly demand an about-turn through a reef even though a clear forward arc existed.
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            for (int pass = 0; pass < 2; pass++)
            {
                float length = distance * (pass == 0 ? 1f : .68f);
                int tested = 0;
                for (int search = 0; search < CandidateAngles.Length; search++)
                {
                    int selected = -1; float best = float.NegativeInfinity; Vector3 heading = Vector3.zero;
                    // Rank with cheap heading math first. In clear water only the best candidate
                    // needs the more expensive terrain/reef/physics segment query.
                    for (int i = 0; i < CandidateAngles.Length; i++)
                    {
                        if ((tested & (1 << i)) != 0) continue;
                        Vector3 option = Quaternion.AngleAxis(CandidateAngles[i], Vector3.up) * direction;
                        float score = Vector3.Dot(option, direction) * 1.4f + Vector3.Dot(option, forward) * .95f;
                        if (blockedTimer > 0f) score -= Mathf.Max(0f, Vector3.Dot(option, blockedHeading)) * 2.0f;
                        if (score <= best) continue;
                        best = score; selected = i; heading = option;
                    }
                    if (selected < 0) break;
                    tested |= 1 << selected;
                    Vector3 candidate = origin + heading * length;
                    if (flee) candidate.y = Mathf.Min(candidate.y + .10f, home.y + 1f);
                    if (!ValidSegment(origin, candidate)) continue;
                    goal = candidate; hasGoal = true; return true;
                }
            }
            hasGoal = false; return false;
        }

        void ApplyPoseAndMove(float dt)
        {
            Quaternion orientation = Quaternion.Euler(pitch, yaw, bank);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, orientation, MaximumTurnRate * dt);
            if (speed <= .00001f) return;
            Vector3 from = transform.position;
            Vector3 next = from + transform.forward * (speed * dt);
            if (!ValidSegment(from, next))
            {
                blockedHeading = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                blockedTimer = .75f;
                speed = 0f; hasGoal = false; goalTimer = Mathf.Min(goalTimer, .08f);
                dodging = false;
                return;
            }
            transform.position = next;
        }

        bool ValidSegment(Vector3 from, Vector3 to)
        {
            if (!Finite(to) || Mathf.Abs(to.y - home.y) > 2f) return false;
            Vector3 offset = to - home; offset.y = 0f;
            return offset.sqrMagnitude <= HomeRadius * HomeRadius && segmentClear(from, to);
        }

        float NextUnit()
        {
            randomState ^= randomState << 13; randomState ^= randomState >> 17; randomState ^= randomState << 5;
            return (randomState & 0x00ffffffu) / 16777216f;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}

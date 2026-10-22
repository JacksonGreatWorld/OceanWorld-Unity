using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Near reef/quest fish turn their visible +Z nose before translating.
    /// Seahorses retain their upright body and may hover vertically. No animation, RNG,
    /// collision or collection state is owned here.</summary>
    public static class OceanFishSteering
    {
        public const float MaximumStep = .05f, MaximumPitch = 35f, MaximumBank = 10f;

        public static Vector3 Advance(Vector3 position, Quaternion rotation, Vector3 desiredVelocity,
            float elapsed, float turnRate, bool upright, out Quaternion heading, float maximumTravel = float.PositiveInfinity)
        {
            heading = rotation;
            if (!Finite(elapsed) || elapsed <= 0f || !Finite(desiredVelocity) ||
                !Finite(turnRate) || turnRate <= 0f || float.IsNaN(maximumTravel) || maximumTravel < 0f)
                return position;
            float dt = Mathf.Min(elapsed, MaximumStep), speed = desiredVelocity.magnitude;
            if (speed <= .00001f) return position;
            Vector3 direction = desiredVelocity / speed;
            Vector3 planar = Vector3.ProjectOnPlane(direction, Vector3.up);
            Vector3 currentPlanar = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            if (planar.sqrMagnitude < .000001f)
                planar = currentPlanar.sqrMagnitude > .000001f ? currentPlanar.normalized : Vector3.forward;
            else planar.Normalize();
            float pitch = upright ? 0f : Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg,
                -MaximumPitch, MaximumPitch);
            float bank = upright ? 0f : -Mathf.Clamp(Vector3.SignedAngle(currentPlanar, planar, Vector3.up) * .12f,
                -MaximumBank, MaximumBank);
            Quaternion target = Quaternion.LookRotation(planar, Vector3.up) * Quaternion.Euler(pitch, 0f, bank);
            heading = Quaternion.RotateTowards(rotation, target, turnRate * dt);
            // A seahorse's head stays upright even while its dorsal fin moves it up/down.
            if (upright) heading = Quaternion.LookRotation(Vector3.ProjectOnPlane(heading * Vector3.forward, Vector3.up).normalized, Vector3.up);
            Vector3 forward = heading * Vector3.forward;
            float alignment = upright ? Vector3.Dot(forward, planar) : Vector3.Dot(forward, direction);
            float throttle = alignment > .35f ? alignment * alignment : 0f;
            Vector3 velocity = upright
                ? forward * (new Vector2(desiredVelocity.x, desiredVelocity.z).magnitude * throttle) + Vector3.up * desiredVelocity.y
                : forward * (speed * throttle);
            Vector3 step = velocity * dt;
            if (step.magnitude > maximumTravel) step = step.normalized * maximumTravel;
            return position + step;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}

using System;

namespace SwanUnderwater
{
    /// <summary>One snap per stick excursion; focus/relocation recovery requires a new neutral sample.</summary>
    public sealed class ControllerSnapTurnGate
    {
        // Preserve the initial controller behavior. Every explicit recovery disarms it.
        bool armed = true;

        public void Reset() => armed = false;

        public float Step(float horizontalAxis, float degrees, bool sampleAvailable = true)
        {
            // A failed device read returns zero too; only a real zero is a neutral observation.
            if (!sampleAvailable || float.IsNaN(horizontalAxis) || float.IsInfinity(horizontalAxis) ||
                float.IsNaN(degrees) || float.IsInfinity(degrees))
            {
                Reset();
                return 0f;
            }
            float amount = Math.Abs(horizontalAxis);
            if (amount < .25f) armed = true;
            if (!armed || amount < .65f) return 0f;
            armed = false;
            return Math.Sign(horizontalAxis) * degrees;
        }
    }
}

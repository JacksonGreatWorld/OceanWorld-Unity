using System;

namespace SwanUnderwater
{
    /// <summary>Presentation-only four-second demonstration. It has no connection to stroke, score or lesson authority.</summary>
    public sealed class OceanTutorialStrokeDemo
    {
        public float Seconds { get; private set; }
        public bool Holding => Seconds >= 1.5f && Seconds < 2.25f;
        public bool Recovering => Seconds >= 2.25f;
        public float PullFraction => Seconds < 1.5f ? Smooth(Seconds / 1.5f) : Seconds < 2.25f ? 1f : 1f - Smooth((Seconds - 2.25f) / 1.75f);
        public void Reset() => Seconds = 0f;
        public void Advance(float dt, bool visible)
        {
            if (!visible || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f || dt > .1f) return;
            Seconds = (Seconds + dt) % 4f;
        }
        static float Smooth(float value)
        { value = Math.Max(0f,Math.Min(1f,value)); return value * value * (3f - 2f * value); }
    }
}

using System;

namespace SwanUnderwater
{
    /// <summary>User comfort choices are separate from campaign progress and never modify a saved run.</summary>
    public readonly struct OceanComfortProfile
    {
        public readonly int Speed, Volume, Turn, TextScale;
        public readonly bool Seated;
        public OceanComfortProfile(int speed, int volume, int turn, bool seated, int textScale = 0)
        {
            Speed = Math.Max(0, Math.Min(2, speed));
            Volume = Math.Max(0, Math.Min(4, volume));
            Turn = Math.Max(0, Math.Min(2, turn));
            TextScale = Math.Max(0, Math.Min(2, textScale));
            Seated = seated;
        }
        // Existing installations retain their current full speed and steering until the player chooses otherwise.
        public static OceanComfortProfile Default => new OceanComfortProfile(2, 4, 2, false);
        public float SpeedMultiplier => Speed == 0 ? .6f : Speed == 1 ? .8f : 1f;
        public float TurnMultiplier => Turn == 0 ? 0f : Turn == 1 ? .5f : 1f;
        public float MasterVolume => Volume * .25f;
        // Scale the complete canvas with its hit areas and text boxes, preserving every line-break budget.
        public float MenuScaleMultiplier => 1f + TextScale * .1f;
        public string TextScaleName => TextScale == 0 ? "标准" : TextScale == 1 ? "较大" : "大字";
        public string SpeedName => Speed == 0 ? "轻缓" : Speed == 1 ? "适中" : "标准";
        public string TurnName => Turn == 0 ? "关闭人工转向" : Turn == 1 ? "轻缓（15°）" : "标准（30°）";
    }
}

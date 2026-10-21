using System;

namespace SwanUnderwater
{
    /// <summary>Only fresh tracking, recognized strokes, a consumed practice grab and menu actions advance the lesson.</summary>
    public sealed class OceanInteractiveTutorialRules
    {
        public enum Step { Inactive, Tracking, SwimAndStop, PracticeGrab, Menu, Complete, Skipped }
        public Step Current { get; private set; }
        public bool Active => Current >= Step.Tracking && Current <= Step.Menu;
        public bool AllowsMenuReturn(bool campaignCanCollect) => campaignCanCollect || Active;
        public int PracticeStrokes { get; private set; }
        public bool MenuOpened { get; private set; }
        public bool TargetsViewed { get; private set; }
        int previousStrokes;
        float stableSeconds, stoppedSeconds;
        public void Start(int strokes)
        {
            Current = Step.Tracking; previousStrokes = Math.Max(0, strokes);
            PracticeStrokes = 0; stableSeconds = stoppedSeconds = 0f;
            MenuOpened = TargetsViewed = false;
        }
        public void Observe(float dt, bool openHandTracked, int strokes, float speed, bool playable)
        {
            int delta = Math.Max(0, strokes - previousStrokes);
            previousStrokes = Math.Max(0, strokes);
            bool valid = playable && dt > 0f && dt <= .1f && !float.IsNaN(dt) &&
                !float.IsNaN(speed) && !float.IsInfinity(speed) && speed >= 0f;
            if (!valid) { stableSeconds = stoppedSeconds = 0f; return; }
            if (Current == Step.Tracking)
            {
                stableSeconds = openHandTracked ? stableSeconds + dt : 0f;
                if (stableSeconds >= .6f) { Current = Step.SwimAndStop; stoppedSeconds = 0f; }
            }
            else if (Current == Step.SwimAndStop)
            {
                PracticeStrokes += delta;
                stoppedSeconds = openHandTracked && speed <= .08f && PracticeStrokes >= 2 ? stoppedSeconds + dt : 0f;
                if (stoppedSeconds >= .6f) Current = Step.PracticeGrab;
            }
        }
        public bool PracticeGrabAccepted(bool consumedFreshIntent)
        {
            if (Current != Step.PracticeGrab || !consumedFreshIntent) return false;
            Current = Step.Menu; return true;
        }
        public void OpenMenu() { if (Current == Step.Menu) MenuOpened = true; }
        public void ViewTargets() { if (Current == Step.Menu && MenuOpened) TargetsViewed = true; }
        public bool Resume()
        {
            if (Current != Step.Menu || !MenuOpened || !TargetsViewed) return false;
            Current = Step.Complete; return true;
        }
        public void Skip() { if (Active) Current = Step.Skipped; }
    }
}

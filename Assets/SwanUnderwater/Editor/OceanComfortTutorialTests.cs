using System;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Real lesson transitions and comfort bounds. These checks do not certify headset comfort or usability.</summary>
    public static class OceanComfortTutorialTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool value, string message)
        { Assertions++; if (!value) throw new InvalidOperationException("Comfort/tutorial: " + message); }
        static void Observe(OceanInteractiveTutorialRules rules, int frames, bool open, int strokes, float speed, bool playing = true)
        { for (int i = 0; i < frames; i++) rules.Observe(.1f, open, strokes, speed, playing); }
        public static void RunAll()
        {
            Assertions = 0;
            var defaults = OceanComfortProfile.Default;
            Check(defaults.SpeedMultiplier == 1f && defaults.TurnMultiplier == 1f && defaults.MasterVolume == 1f && !defaults.Seated, "old default movement and sound retained");
            var low = new OceanComfortProfile(-3, -10, -1, true);
            var high = new OceanComfortProfile(200, 200, 200, false);
            Check(low.Speed == 0 && low.MasterVolume == 0f && low.TurnMultiplier == 0f && low.Seated, "invalid stored indices clamp safely");
            Check(high.Speed == 2 && high.Volume == 4 && high.Turn == 2, "out of range high settings clamp");
            float previous = 0f;
            for (int speed = 0; speed < 3; speed++)
            {
                var profile = new OceanComfortProfile(speed, 4, 2, false);
                Check(profile.SpeedMultiplier > previous && profile.SpeedMultiplier <= 1f, "speed choices are increasing and never boost beyond legacy limit"); previous = profile.SpeedMultiplier;
            }
            Check(new OceanComfortProfile(2, 2, 1, false).MasterVolume == .5f, "half volume is actually half amplitude");
            Check(new OceanComfortProfile(2, 4, 1, false).TurnMultiplier == .5f, "reduced hand and controller steering share half scale");
            Check(defaults.TextScale == 0 && defaults.MenuScaleMultiplier == 1f, "existing menu angular size is the default");
            Check(new OceanComfortProfile(2,4,2,false,-1).TextScale == 0 && new OceanComfortProfile(2,4,2,false,999).TextScale == 2, "stored text preset is bounded");
            Check(Math.Abs(new OceanComfortProfile(2,4,2,false,1).MenuScaleMultiplier-1.1f)<.0001f &&
                Math.Abs(new OceanComfortProfile(2,4,2,false,2).MenuScaleMultiplier-1.2f)<.0001f, "large text uses bounded complete-canvas presets");
            var demonstration = new OceanTutorialStrokeDemo();
            for(int i=0;i<16;i++)demonstration.Advance(.1f,true);
            Check(demonstration.Holding && demonstration.PullFraction == 1f, "slow demonstration includes a real stop phase");
            float beforePause = demonstration.Seconds;
            for(int i=0;i<30;i++)demonstration.Advance(.1f,false);
            demonstration.Advance(float.NaN,true);demonstration.Advance(.5f,true);
            Check(demonstration.Seconds == beforePause, "menu/hidden/invalid intervals cannot advance the demonstration");
            for(int i=0;i<1000;i++)
            {
                demonstration.Advance(.05f,true);
                Check(demonstration.Seconds>=0f && demonstration.Seconds<4f && demonstration.PullFraction>=0f && demonstration.PullFraction<=1f, "demonstration remains bounded through repeated cycles");
            }
            demonstration.Reset();Check(demonstration.Seconds==0f && demonstration.PullFraction==0f,"repeat lesson resets only its diagram clock");
            var rules = new OceanInteractiveTutorialRules();
            Check(!rules.Active, "lesson inactive until started");
            Check(!rules.AllowsMenuReturn(false) && rules.AllowsMenuReturn(true), "a finished campaign cannot resume without an active lesson");
            rules.Start(10); Check(rules.Active && rules.Current == OceanInteractiveTutorialRules.Step.Tracking, "begin tracking lesson");
            Check(rules.AllowsMenuReturn(false), "repeat lesson can return to practice beside a failed saved run");
            Observe(rules, 10, false, 10, 0f); Check(rules.Current == OceanInteractiveTutorialRules.Step.Tracking, "untracked/closed hand cannot pass");
            Observe(rules, 4, true, 10, 0f); rules.Observe(.1f, false, 10, 0f, true);
            Observe(rules, 3, true, 10, 0f); Check(rules.Current == OceanInteractiveTutorialRules.Step.Tracking, "tracking dwell must be continuous");
            rules.Observe(float.NaN, true, 20, 0f, true); Observe(rules, 4, true, 20, 0f);
            Check(rules.Current == OceanInteractiveTutorialRules.Step.Tracking, "invalid dt resets tracking dwell");
            Observe(rules, 3, true, 20, 0f); Check(rules.Current == OceanInteractiveTutorialRules.Step.SwimAndStop && rules.PracticeStrokes == 0, "tracking phase strokes do not pay for next lesson");
            Observe(rules, 20, true, 20, 0f); Check(rules.Current == OceanInteractiveTutorialRules.Step.SwimAndStop, "standing still cannot replace swimming");
            rules.Observe(.1f, true, 21, .8f, true); Check(rules.PracticeStrokes == 1, "one actual recognized stroke counted");
            rules.Observe(.1f, true, 22, .8f, true); Observe(rules, 10, true, 22, .8f);
            Check(rules.Current == OceanInteractiveTutorialRules.Step.SwimAndStop, "two strokes require deceleration too");
            Observe(rules, 4, true, 22, 0f); Observe(rules, 5, true, 30, 0f, false);
            Observe(rules, 3, true, 30, 0f); Check(rules.Current == OceanInteractiveTutorialRules.Step.SwimAndStop && rules.PracticeStrokes == 2, "menu/focus interruption resets stop dwell and ignores interrupted strokes");
            Observe(rules, 4, true, 30, 0f); Check(rules.Current == OceanInteractiveTutorialRules.Step.PracticeGrab, "two actual strokes and stable stop unlock reachable shell");
            Check(!rules.PracticeGrabAccepted(false) && rules.Current == OceanInteractiveTutorialRules.Step.PracticeGrab, "hover/proximity alone cannot grab practice shell");
            Check(rules.PracticeGrabAccepted(true) && rules.Current == OceanInteractiveTutorialRules.Step.Menu, "consumed fresh intent advances once");
            Check(!rules.PracticeGrabAccepted(true), "repeat shell events ignored");
            rules.ViewTargets(); Check(!rules.TargetsViewed && !rules.Resume(), "targets view cannot bypass real menu open");
            rules.OpenMenu(); Check(rules.MenuOpened && !rules.Resume(), "opening menu alone cannot finish lesson");
            rules.ViewTargets(); Check(rules.TargetsViewed && rules.Resume() && !rules.Active, "open targets and resume completes lesson");
            Check(!rules.Resume(), "completion is one-shot");
            Check(!rules.AllowsMenuReturn(false), "completion yields failed-run result gating back to the campaign");
            rules.Start(200); Check(rules.Active && rules.PracticeStrokes == 0 && !rules.MenuOpened && !rules.TargetsViewed, "repeat lesson fully resets only lesson state");
            rules.Skip(); Check(!rules.Active && rules.Current == OceanInteractiveTutorialRules.Step.Skipped, "skip exits immediately without forged completion");
            Check(!rules.AllowsMenuReturn(false), "skipping cannot enable ordinary collection in a finished run");
            rules.Start(300); Observe(rules, 7, true, 300, 0f);
            rules.Observe(.2f, true, 302, 0f, true); Observe(rules, 10, true, 302, 0f);
            Check(rules.PracticeStrokes == 0 && rules.Current == OceanInteractiveTutorialRules.Step.SwimAndStop, "hitch cannot import stale stroke increments");
            rules.Observe(.1f, true, 0, 0f, true); Check(rules.PracticeStrokes == 0, "counter recovery cannot fabricate a negative or wrapped stroke");
#if UNITY_EDITOR
            UnityEngine.Debug.Log("[OceanComfortTutorialTests] PASS " + Assertions + " assertions; no headset claim.");
#else
            Console.WriteLine("[OceanComfortTutorialTests] PASS " + Assertions + " assertions; no headset claim.");
#endif
        }
#if OCEAN_COMFORT_TUTORIAL_TEST_MAIN
        public static int Main()
        { try { RunAll(); return 0; } catch (Exception error) { Console.Error.WriteLine(error); return 1; } }
#endif
    }
}

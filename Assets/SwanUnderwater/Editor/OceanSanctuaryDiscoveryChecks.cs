#if UNITY_EDITOR
using System;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    public static class OceanSanctuaryDiscoveryChecks
    {
        public static int Assertions { get; private set; }
        static void Check(bool condition, string message)
        { Assertions++; if (!condition) throw new InvalidOperationException("Sanctuary discovery: " + message); }

        public static void RunAll()
        {
            Assertions = 0;
            var state = new OceanSiteVisitProgress(7);
            for (int i = 0; i < 5; i++) Check(state.Tick(0, .2, true) == -1, "a brief pass does not finish a visit");
            Check(state.Tick(0, .2, true) == 0 && state.VisitedCount == 1, "continuous presence discovers the area once");
            for (int i = 0; i < 80; i++) Check(state.Tick(0, .2, true) == -1, "returning to a visited site never repeats its toast");
            Check(state.VisitedCount == 1, "no repeat progress");
            state.Tick(1, .2, true); state.Tick(1, .2, true);
            state.Tick(1, 120, false);
            for (int i = 0; i < 5; i++) Check(state.Tick(1, .2, true) == -1, "menu/focus absence discards partial dwell");
            Check(state.Tick(1, .2, true) == 1, "return requires the full active dwell");
            for (int i = 0; i < 50; i++) state.Tick(-1, .2, true);
            state.Tick(2, .2, true); state.Tick(2, .2, true); state.Tick(-1, .2, true);
            for (int i = 0; i < 5; i++) Check(state.Tick(2, .2, true) == -1, "leaving an area resets its pending visit");
            Check(state.Tick(2, .2, true) == 2, "re-enter and dwell can discover the site");
            var longFrame = new OceanSiteVisitProgress(1);
            Check(longFrame.Tick(0, 120, true) == -1 && longFrame.VisitedCount == 0,
                "a stalled frame cannot fast-forward discovery");
            Check(longFrame.Tick(0, double.NaN, true) == -1 && longFrame.Tick(0, double.PositiveInfinity, true) == -1,
                "non-finite timing is rejected");
            Check(longFrame.Tick(7, .2, true) == -1 && longFrame.Tick(-1, .2, true) == -1,
                "out of range sites are ignored");

            Check(OceanSanctuaryDiscovery.Sites.Count == 7, "all seven authored areas have a discovery");
            for (int i = 0; i < OceanSanctuaryDiscovery.Sites.Count; i++)
            {
                var site = OceanSanctuaryDiscovery.Sites[i];
                Check(!string.IsNullOrWhiteSpace(site.name) && !string.IsNullOrWhiteSpace(site.hint), "readable name and exploration clue");
                Check(Mathf.Abs(site.offset.x) + site.radius < OceanEnvironmentProfile.SanctuaryPlayableHalfExtent &&
                    Mathf.Abs(site.offset.y) + site.radius < OceanEnvironmentProfile.SanctuaryPlayableHalfExtent,
                    "discovery stays inside the playable environment");
                float closest = float.PositiveInfinity;
                foreach (Vector2 point in OceanCoralSanctuary.ExplorationRouteOffsets)
                    closest = Mathf.Min(closest, Vector2.Distance(point, site.offset));
                Check(closest < .01f, "discovery landmark is on an authored swim-route point");
                for (int j = 0; j < i; j++)
                    Check(Vector2.Distance(site.offset, OceanSanctuaryDiscovery.Sites[j].offset) >
                        site.radius + OceanSanctuaryDiscovery.Sites[j].radius, "separate regions cannot double-trigger");
            }
            Debug.Log("OceanSanctuaryDiscoveryChecks PASS " + Assertions + " assertions; tracking/occlusion/feedback require runtime review.");
        }
    }
}
#endif

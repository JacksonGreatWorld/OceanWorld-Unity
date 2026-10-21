using System;

namespace SwanUnderwater
{
    /// <summary>Session-only exploration; collecting, stars, saves and leaderboard scores have separate owners.</summary>
    public sealed class OceanSiteVisitProgress
    {
        public const double RequiredDwellSeconds = 1.2;
        public const double NoticeCooldownSeconds = 9;
        public int VisitedCount { get; private set; }
        public int SiteCount { get; }
        readonly bool[] visited;
        int candidate = -1;
        double dwell, cooldown;

        public OceanSiteVisitProgress(int siteCount)
        {
            if (siteCount < 1 || siteCount > 32) throw new ArgumentOutOfRangeException(nameof(siteCount));
            SiteCount = siteCount;
            visited = new bool[siteCount];
        }

        public bool HasVisited(int site) => site >= 0 && site < visited.Length && visited[site];

        /// <summary>Pause/menu/focus loss cannot finish a visit; returning to the site requires a fresh dwell.</summary>
        public void Interrupt() { candidate = -1; dwell = 0; }

        /// <returns>A newly discovered site, or -1. Delta is active gameplay time, not wall-clock absence.</returns>
        public int Tick(int nearestVisibleSite, double deltaSeconds, bool available)
        {
            if (!available || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds <= 0)
            { Interrupt(); return -1; }
            // A long frame or a resumed app must not instantly finish a discovery.
            double dt = Math.Min(.25, deltaSeconds);
            cooldown = Math.Max(0, cooldown - dt);
            if (cooldown > 0 || nearestVisibleSite < 0 || nearestVisibleSite >= SiteCount || HasVisited(nearestVisibleSite))
            { Interrupt(); return -1; }
            if (candidate != nearestVisibleSite) { candidate = nearestVisibleSite; dwell = 0; }
            dwell += dt;
            if (dwell + .0000001 < RequiredDwellSeconds) return -1;
            int discovered = candidate;
            visited[discovered] = true;
            VisitedCount++;
            cooldown = NoticeCooldownSeconds;
            Interrupt();
            return discovered;
        }
    }
}

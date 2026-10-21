using System;

namespace SwanUnderwater
{
    public interface IOceanCampaignScoreSink
    {
        bool EnsureSession();
        bool BeginRun(string runId, int level);
        bool RecordShell(string runId, int species, int pickupId);
        bool RecordFish(string runId, int species, int pickupId);
        bool RecordCoin(string runId, int pickupId);
        bool RecordShark(string runId, int pickupId);
    }

    /// <summary>Recover committed discoveries after a crash between the campaign save and outbox write.</summary>
    public sealed class OceanCampaignScoreSync
    {
        string savedRun;
        int savedFish = -1;
        int savedCoins = -1, savedShellCount = -1;
        bool savedShark;

        public bool TryPersist(OceanLevelRun run, IOceanCampaignScoreSink sink)
        {
            if (run == null || sink == null || string.IsNullOrEmpty(run.leaderboardRunId)) return false;
            int fish = run.fishMask & ~run.leaderboardFishBaseline;
            int coins = run.coinMask & ~run.leaderboardCoinMaskBaseline;
            bool shark = run.sharkCaught && !run.leaderboardSharkBaseline;
            if (savedRun == run.leaderboardRunId && savedFish == fish && savedCoins == coins && savedShark == shark && savedShellCount == run.leaderboardShells.Count) return true;
            if (!sink.EnsureSession() || !sink.BeginRun(run.leaderboardRunId, run.level)) return false;
            foreach (OceanShellScore shell in run.leaderboardShells)
                if (!sink.RecordShell(run.leaderboardRunId, shell.species, shell.pickupId)) return false;
            for (int species = 0; species < OceanCampaignState.FishSpeciesCount; species++)
                if ((fish & (1 << species)) != 0 && !sink.RecordFish(run.leaderboardRunId, species, 10000 + species)) return false;
            for (int slot = 0; slot < OceanLevelCatalog.Get(run.level).CoinCount; slot++)
                if ((coins & (1 << slot)) != 0 && !sink.RecordCoin(run.leaderboardRunId, OceanLevelCatalog.CoinId(slot))) return false;
            if (shark && !sink.RecordShark(run.leaderboardRunId, 10004)) return false;
            savedRun = run.leaderboardRunId; savedFish = fish; savedCoins = coins; savedShark = shark; savedShellCount = run.leaderboardShells.Count;
            return true;
        }
    }
}

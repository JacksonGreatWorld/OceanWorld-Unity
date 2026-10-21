using System;
using System.IO;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    public static class OceanAchievementProgressTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool value, string message)
        { Assertions++; if (!value) throw new InvalidOperationException("Achievement history: " + message); }

        public static void RunAll()
        {
            Assertions = 0;
            var empty = OceanAchievementProgress.FromSavedHistory(null, 0);
            Check(empty.CompletedLevels == 0 && empty.CoinTotal == 0 && !empty.CampaignFinished, "missing history never invents achievements");
            var history = new OceanSaveData { shellMask = 1023, fishMask = 3, shark = true };
            history.bestStars[0] = 3; history.bestStars[5] = 3;
            history.currentRun = new OceanLevelRun { level = 6, coinMask = 3 };
            string before = JsonUtility.ToJson(history);
            var facts = OceanAchievementProgress.FromSavedHistory(history, 7, 2, 1, 2);
            Check(facts.ShellSpecies == 10 && facts.FishSpecies == 2 && facts.SharkCaught, "legacy masks prove species, not arbitrary lifetime totals");
            Check(facts.ShellTotal == 10 && facts.FishTotal == 3, "durable species and shark establish conservative lifetime lower bounds");
            Check(facts.CoinTotal == 3, "two historical three-star levels prove their actual one-plus-two coins without adding the active run again");
            Check(facts.CompletedLevels == 2 && facts.ThreeStarLevels == 2 && facts.TotalStars == 6, "history counts distinct best results");
            Check(facts.CompletedBiomes == 2 && facts.CompletedMechanics == 0, "classic and sanctuary are distinct, original levels are not new mechanics");
            Check(facts.ObservedSpecies == 7 && facts.ObservedTarget == OceanSpeciesCatalog.AvailableCount, "observations use their independent current available catalog");
            facts = OceanAchievementProgress.FromSavedHistory(history, 7, 53, 106, 58);
            Check(facts.ShellTotal == 53 && facts.FishTotal == 106 && facts.CoinTotal == 58, "ledger counts and legacy lower bounds use maximum, never addition");
            Check(JsonUtility.ToJson(history) == before, "achievement backfill cannot mutate campaign, collection, score baselines or current run");
            foreach (int level in new[] { 21, 26, 46, 51, 81 }) history.bestStars[level - 1] = 2;
            facts = OceanAchievementProgress.FromSavedHistory(history, 0);
            Check(facts.CompletedBiomes == 7 && facts.CompletedMechanics == 5, "seven environment identities and five unique mechanisms are represented");
            history.bestStars[21] = 2;
            facts = OceanAchievementProgress.FromSavedHistory(history, 0);
            Check(facts.CompletedMechanics == 5, "repeated tier does not count as another mechanism");
            var finaleOnly = new OceanSaveData(); finaleOnly.bestStars[99] = 2;
            facts = OceanAchievementProgress.FromSavedHistory(finaleOnly, 0);
            Check(facts.CampaignFinished && facts.CompletedLevels == 1, "finishing the final selectable level never implies all100 completed");
            long coins = 0;
            for (int i = 0; i < history.bestStars.Length; i++) { history.bestStars[i] = 3; coins += OceanLevelCatalog.Get(i + 1).CoinCount; }
            facts = OceanAchievementProgress.FromSavedHistory(history, OceanSpeciesCatalog.Count);
            Check(facts.CompletedLevels == 100 && facts.ThreeStarLevels == 100 && facts.TotalStars == 300, "all100 best results yield exact campaign/star facts");
            Check(facts.CompletedMechanics == 16 && facts.CompletedBiomes == 7 && facts.CoinTotal == coins, "complete historical route supplies bounded, nonduplicated proof");
            Check(facts.ObservedSpecies == 30 && facts.ObservedTarget == 30, "historical catalog capacity cannot inflate the current available observation facts");
            CheckAvailableObservations();
            Debug.Log("[OceanAchievementProgressTests] PASS " + Assertions + " assertions; durable snapshot derivation, no player files changed.");
        }

        static void CheckAvailableObservations()
        {
            var naturalist = new OceanNaturalistState();
            const string utc = "2026-10-20T10:00:00Z";
            for (int id = 0; id < OceanSpeciesCatalog.Count - 1; id++)
                if (OceanSpeciesCatalog.IsAvailable(id)) Check(naturalist.TryRecord(id, 20, utc), "real available observation fixture records ID " + id);
            Check(naturalist.TryRecord(13, 20, utc), "legacy observation ID13 remains readable evidence");
            Check(naturalist.Count == 30 && naturalist.AvailableCount == 29 && naturalist.Contains(13),
                "twenty-nine actual available observations plus legacy ID13 do not become thirty available observations");
            string directory = Path.Combine(Path.GetTempPath(), "ocean-achievement-active-observations-" + Guid.NewGuid().ToString("N"));
            bool passed = false;
            try
            {
                var journal = new OceanAchievementJournal(directory);
                var facts = OceanAchievementProgress.FromSavedHistory(null, naturalist.AvailableCount);
                journal.Evaluate(facts, false);
                Check(facts.ObservedSpecies == 29 && !journal.IsUnlocked("observe-current-all") && !journal.IsUnlocked("observe-all"),
                    "legacy ID13 neither completes the current full catalog nor creates an unearned historical honour");
                Check(naturalist.TryRecord(30, 20, utc) && naturalist.AvailableCount == 30 && naturalist.Count == 31,
                    "the thirtieth available discovery is a new real observation, not a fabricated retired record");
                facts = OceanAchievementProgress.FromSavedHistory(null, naturalist.AvailableCount);
                journal.Evaluate(facts, true);
                Check(journal.IsUnlocked("observe-current-all") && journal.IsUnread("observe-current-all") && journal.Progress("observe-current-all") == 30 &&
                    !journal.IsUnlocked("observe-all") && journal.HistoricalUnlockedCount == 0,
                    "all thirty actual available observations earn only the current-edition full-catalog achievement");
                Check(naturalist.Records.Count == 31 && naturalist.Contains(13) && !naturalist.TryRecord(30, 20, utc),
                    "achievement evaluation preserves original observation identities and duplicate rules");
                passed = true;
            }
            finally
            {
                if (passed && Directory.Exists(directory)) Directory.Delete(directory, true);
                else if (!passed) Debug.Log("Achievement available-observation fixture preserved at " + directory);
            }
        }
    }
}

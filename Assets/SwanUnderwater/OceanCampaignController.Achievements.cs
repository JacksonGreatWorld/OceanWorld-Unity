using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater
{
    public sealed partial class OceanCampaignController
    {
        public OceanAchievementJournal Achievements { get; private set; }
        readonly Queue<string> achievementNotices = new Queue<string>();
        OceanSaveData achievementHistory;
        bool achievementLifetimeInitialized, achievementsClosing;
        double nextAchievementNotice;

        void InitializeAchievements(OceanSaveData verifiedHistory)
        {
            achievementHistory = verifiedHistory == null ? null : OceanCampaignState.Copy(verifiedHistory);
            Achievements = new OceanAchievementJournal(SaveDirectory);
            Panel.BindAchievements(Achievements);
            leaderboard.LocalProgressChanged += OnAchievementSourceChanged;
            if (!Achievements.Writable) Debug.LogWarning("[Achievements] existing achievement files preserved; " + Achievements.LastError);
        }

        void OnAchievementSourceChanged() => RefreshAchievements(true);

        void CommitAchievementHistory(OceanSaveData history, bool notify = true)
        {
            achievementHistory = history;
            RefreshAchievements(notify);
        }

        void RefreshAchievements(bool notify)
        {
            if (!initialized || achievementsClosing || Achievements == null) return;
            bool ready = leaderboard != null && leaderboard.LocalProgressAvailable;
            var facts = OceanAchievementProgress.FromSavedHistory(achievementHistory, Naturalist?.State.AvailableCount ?? 0,
                leaderboard?.LocalShellTotal ?? 0, leaderboard?.LocalFishTotal ?? 0, leaderboard?.LocalCoinTotal ?? 0);
            long before = Achievements.Revision;
            // Loading a historical leaderboard for the first time is backfill, not a new earned event.
            var unlocked = Achievements.Evaluate(facts, notify && achievementLifetimeInitialized);
            if (ready && Achievements.Writable && string.IsNullOrEmpty(Achievements.LastError)) achievementLifetimeInitialized = true;
            for (int i = 0; i < unlocked.Count; i++)
                if (Achievements.IsUnread(unlocked[i]) && achievementNotices.Count < OceanAchievementCatalog.Count)
                    achievementNotices.Enqueue(unlocked[i]);
            if (Achievements.Revision != before && Panel != null && Panel.isActiveAndEnabled) Panel.Refresh();
        }

        void TickAchievementNotices()
        {
            if (Achievements == null || achievementsClosing || achievementNotices.Count == 0 ||
                !GameplayAllowed || OceanEntry.IsLoading || !feedback.CanShowExplorationMessage ||
                Time.unscaledTimeAsDouble < nextAchievementNotice) return;
            while (achievementNotices.Count > 0)
            {
                string id = achievementNotices.Peek();
                if (!Achievements.IsUnread(id)) { achievementNotices.Dequeue(); continue; }
                string name = null;
                foreach (var definition in OceanAchievementCatalog.All)
                    if (definition.Id == id) { name = definition.Name; break; }
                if (name == null) { achievementNotices.Dequeue(); continue; }
                string noticeName = name;
                if (feedback.TryShowLocalizedAchievementMessage("成就达成 · " + noticeName,
                    () => OceanLanguage.Format("成就达成 · {0}", OceanLanguage.Text(noticeName))))
                {
                    achievementNotices.Dequeue(); nextAchievementNotice = Time.unscaledTimeAsDouble + 5;
                    Debug.Log("[Achievements] unlocked=" + id);
                }
                break;
            }
        }

        void DisposeAchievements()
        {
            achievementsClosing = true;
            if (leaderboard != null) leaderboard.LocalProgressChanged -= OnAchievementSourceChanged;
            achievementNotices.Clear();
        }
    }
}

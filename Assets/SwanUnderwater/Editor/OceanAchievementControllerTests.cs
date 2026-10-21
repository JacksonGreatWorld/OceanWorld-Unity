using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Failure/retry on the actual controller reconciliation method, using an isolated data source.</summary>
    public static class OceanAchievementControllerTests
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        public static int Assertions { get; private set; }
        static void Check(bool value, string message)
        { Assertions++; if (!value) throw new InvalidOperationException("Achievement controller: " + message); }
        public static void RunAll()
        {
            Assertions = 0;
            string directory = Path.Combine(Path.GetTempPath(), "ocean-achievement-controller-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string storeDirectory = Path.Combine(directory, "temporarily-unwritable");
            var host = new GameObject("Isolated achievement reconciliation controller");
            OceanCampaignController campaign = null;
            try
            {
                campaign = host.AddComponent<OceanCampaignController>(); campaign.enabled = false;
                var bridge = host.AddComponent<OceanLeaderboardBridge>(); bridge.enabled = false;
                var client = host.AddComponent<OceanLeaderboardClient>(); client.enabled = false;
                // No client initialization: there are no identity tasks, requests, scoring or production writes.
                typeof(OceanLeaderboardBridge).GetField("client", Flags).SetValue(bridge, client);
                Set(campaign, "leaderboard", bridge); Set(campaign, "initialized", true);
                var journal = new OceanAchievementJournal(storeDirectory);
                Set(campaign, "<Achievements>k__BackingField", journal);
                var verified = new OceanSaveData { shellMask = 1 };
                verified.bestStars[0] = 3;
                Set(campaign, "achievementHistory", verified);
                File.WriteAllText(storeDirectory, "This fixture-owned file makes directory creation fail.");
                Refresh(campaign);
                Check(journal.Revision == 0 && journal.UnlockedCount == 0 && journal.LastError.Length > 0,
                    "first backfill write failure cannot expose unsaved unlocks");
                Check(!(bool)Get(campaign, "achievementLifetimeInitialized"), "failed baseline stays in quiet reconciliation");
                File.Delete(storeDirectory);
                Refresh(campaign);
                Check(journal.IsUnlocked("levels-1") && journal.IsUnlocked("shell-species-1") && journal.IsUnlocked("coins-1"),
                    "retry backfills the authenticated read history even without a new campaign write");
                Check(journal.UnreadCount == 0 && (bool)Get(campaign, "achievementLifetimeInitialized"),
                    "successful historical retry is quiet and only then enables new-event notices");
                verified.shellMask = 31; Refresh(campaign);
                Check(journal.IsUnlocked("shell-species-5") && journal.IsUnread("shell-species-5"),
                    "a subsequent new durable fact unlocks with unread feedback");
                long revision = journal.Revision; Refresh(campaign);
                Check(journal.Revision == revision && journal.UnreadCount == 1, "repeat source notifications are idempotent");

                var naturalist = host.AddComponent<OceanNaturalist>(); naturalist.enabled = false;
                string observationDirectory = Path.Combine(directory, "legacy-observation-source");
                var historical = new OceanNaturalistStore(observationDirectory);
                const string utc = "2026-10-20T10:00:00Z";
                // Fixture-owned prior history, including retired ID13; persist and reload
                // before controller reconciliation so the source is an authenticated file.
                for (int id = 0; id < 30; id++) Check(historical.State.TryRecord(id, 20, utc), "old observation fixture accepts ID " + id);
                Check(historical.SetObservationEnabled(false), "legacy observation fixture commits its original identities");
                var observed = new OceanNaturalistStore(observationDirectory);
                Check(observed.Writable && observed.State.Count == 30 && observed.State.AvailableCount == 29,
                    "verified observation source retains historical ID13 without current completion credit");
                Set(naturalist, "<Store>k__BackingField", observed); Set(campaign, "<Naturalist>k__BackingField", naturalist);
                Refresh(campaign);
                Check(!journal.IsUnlocked("observe-current-all") && journal.Progress("observe-current-all") == 29 &&
                    !journal.IsUnlocked("observe-all"), "real controller uses available observations rather than thirty total historical records");
                Check(observed.TryRecord(30, 20, utc), "actual thirtieth available observation commits normally");
                Refresh(campaign);
                Check(journal.IsUnlocked("observe-current-all") && journal.IsUnread("observe-current-all") &&
                    !journal.IsUnlocked("observe-all") && observed.State.Count == 31 && observed.State.Contains(13),
                    "current full completion is earned without deleting history or fabricating a legacy honour");
            }
            finally
            {
                if (campaign != null) Set(campaign, "initialized", false);
                UnityEngine.Object.DestroyImmediate(host);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            CheckFeedbackPriority();
            Debug.Log("[OceanAchievementControllerTests] PASS " + Assertions + " assertions; actual reconciliation retry, no campaign/leaderboard files modified.");
        }
        static void CheckFeedbackPriority()
        {
            var host = new GameObject("Isolated achievement feedback priority");
            var cameraHost = new GameObject("Achievement feedback camera");
            try
            {
                var view = cameraHost.AddComponent<Camera>(); view.enabled = false;
                var feedback = host.AddComponent<TreasureFeedback>(); feedback.Initialize(view); feedback.SetVisible(true);
                Check(feedback.TryShowAchievementMessage("成就达成 · 第一枚贝壳"), "idle feedback accepts an achievement");
                feedback.ShowCampaignMessage("收集到：扇贝", pickup: true);
                Check(feedback.CurrentNotification == "收集到：扇贝", "a subsequent pickup immediately preempts an achievement");
                Check(!feedback.TryShowAchievementMessage("成就达成 · 五彩贝藏"), "a new achievement cannot preempt pickup feedback");
                feedback.SetVisible(false); feedback.SetVisible(true);
                Check(feedback.TryShowAchievementMessage("成就达成 · 第一枚贝壳"), "reset presentation accepts a new low-priority achievement");
                feedback.ShowHiddenReward();
                Check(feedback.CurrentNotification == TreasureFeedback.HiddenRewardText && feedback.IsCelebrating,
                    "coin celebration retains presentation ownership");
                Check((int)typeof(TreasureFeedback).GetField("queueCount", Flags).GetValue(feedback) == 0,
                    "a preempted achievement cannot reenter the normal pickup reward queue");
                Check(!feedback.TryShowAchievementMessage("成就达成 · 金光初现"), "coin celebration does not accept achievements");
                feedback.SetVisible(false);
                Check(!feedback.TryShowAchievementMessage("成就达成 · 第一枚贝壳"), "hidden feedback does not display achievements");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(cameraHost); }
        }
        static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
        static object Get(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
        static void Refresh(OceanCampaignController controller) => typeof(OceanCampaignController)
            .GetMethod("RefreshAchievements", Flags).Invoke(controller, new object[] { true });
    }
}

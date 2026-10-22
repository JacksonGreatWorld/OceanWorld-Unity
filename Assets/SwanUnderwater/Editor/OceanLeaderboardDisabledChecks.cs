#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater.EditorTests
{
    /// <summary>
    /// Disabled-release checks against production UI, ledger, bridge and client methods.
    /// All generated history belongs to a unique temporary fixture. No real identity,
    /// HTTP service, tracked hand input or headset acceptance is exercised here.
    /// </summary>
    public static class OceanLeaderboardDisabledChecks
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const string Session = "dddddddddddddddddddddddddddddddd";
        public static int Assertions { get; private set; }
        static void Check(bool value, string detail)
        { Assertions++; if (!value) throw new InvalidOperationException("Disabled leaderboard: " + detail); }

        [MenuItem("OceanWorld/Validation/Disabled leaderboard release")]
        public static void RunAll()
        {
            Assertions = 0;
            Check(!OceanReleaseFeatures.LeaderboardsEnabled, "release policy disables both boards and nickname entry");
            string directory = Path.Combine(Path.GetTempPath(), "ocean-disabled-ranking-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var host = new GameObject("Isolated disabled leaderboard fixture");
            OceanCampaignController campaign = null;
            bool passed = false;
            try
            {
                string storage = Path.Combine(directory, "Leaderboard", "editor-test");
                string legacyEvent = SeedLegacyFullQueue(storage);
                CheckLocalOnlyLedger(storage, legacyEvent);
                var original = Snapshot(storage);
                var cameraHost = new GameObject("Disabled leaderboard fixture camera");
                cameraHost.transform.SetParent(host.transform, false);
                var view = cameraHost.AddComponent<Camera>(); view.enabled = false; view.transform.position = Vector3.up * 1.6f;
                var hands = host.AddComponent<HandSwimController>(); hands.enabled = false;
                int progressEvents = 0, closedEvents = 0;
                var bridge = host.AddComponent<OceanLeaderboardBridge>(); bridge.enabled = false;
                bridge.LocalProgressChanged += () => progressEvents++;
                bridge.Initialize(view, hands, directory, () => closedEvents++);
                var client = (OceanLeaderboardClient)Get(bridge, "client");
                Check(client != null && bridge.LocalProgressAvailable, "disabled bridge still loads local lifetime history for achievements");
                client.enabled = false;
                Check(host.GetComponents<OceanLeaderboardPanel>().Length == 0 && Get(bridge, "panel") == null,
                    "bridge initialization creates neither ranking canvas nor nickname keyboard");
                CheckUnchanged(original, "initialization is read-only for authenticated legacy history and backups");
                Check(client.LocalShellTotal == 1 && client.LocalFishTotal == 1 && client.LocalCoinTotal == 1,
                    "production client reads prior local totals without signing identity initialization");
                NoTransport(client, "initialization");

                bridge.Open();
                Invoke(bridge, "HandleAction", "rename", "不应保存的昵称");
                Invoke(bridge, "HandleAction", "load", "");
                Check(!bridge.IsOpen && !hands.MenuLocomotionPaused && closedEvents == 0,
                    "legacy bridge actions neither open a panel, pause swimming nor hand off to another menu");
                Check(!client.SetNickname("不应保存的昵称"), "direct nickname call is refused while disabled");
                client.RefreshPage(); client.RefreshPage(discovery: true);
                Check(client.PageRequestVersion == 0 && !(bool)Get(client, "pageQueued"),
                    "neither score nor discovery page is queued");
                for (int i = 0; i < 8; i++) Invoke(client, "Update");
                NoTransport(client, "legacy open, rename, page and update paths");
                CheckUnchanged(original, "disabled network and profile operations do not mutate any historical file");

                string discoveryFile = Path.Combine(storage, "ocean-discoveries.json");
                byte[] discoveryOriginal = File.ReadAllBytes(discoveryFile);
                Check(bridge.RecordDiscovery(1, 1), "optional discovery synchronization cannot block local observation");
                Check(BytesEqual(discoveryOriginal, File.ReadAllBytes(discoveryFile)),
                    "disabled discovery sync leaves old upload records byte-for-byte intact");
                var run = new OceanCampaignState(); run.StartLevel(1);
                Check(run.CollectShell(7, 0) && run.CollectCoin(20000), "fixture makes one real campaign shell and hidden-coin transaction");
                int eventsBeforePickups = progressEvents;
                Check(bridge.PersistRun(run.Current) && progressEvents > eventsBeforePickups,
                    "actual score synchronizer commits local pickups and emits achievement source changes");
                int eventsAfterPickups = progressEvents;
                Check(bridge.PersistRun(run.Current) && progressEvents == eventsAfterPickups,
                    "repeated committed run is idempotent and does not emit a false new-progress notice");
                Check(client.LocalShellTotal == 2 && client.LocalFishTotal == 1 && client.LocalCoinTotal == 2 && client.PendingCount == 1,
                    "new local pickups advance lifetime facts without appending to old pending uploads");
                Check(progressEvents > 0, "local durability still emits the achievement source notification");
                AssertLegacyMetadata((OceanLeaderboardLedger)Get(client, "ledger"), legacyEvent);
                NoTransport(client, "local shell, coin and observation transactions");

                campaign = host.AddComponent<OceanCampaignController>(); campaign.enabled = false;
                Set(campaign, "<State>k__BackingField", run);
                Set(campaign, "<SaveDirectory>k__BackingField", Path.Combine(directory, "campaign"));
                Set(campaign, "hands", hands); Set(campaign, "view", view); Set(campaign, "leaderboard", bridge);
                var panel = host.AddComponent<OceanCampaignPanel>(); panel.enabled = false;
                panel.Initialize(view, hands, run, campaign.HandleAction);
                Set(campaign, "<Panel>k__BackingField", panel);
                var journal = new OceanAchievementJournal(Path.Combine(directory, "achievements"));
                Set(campaign, "<Achievements>k__BackingField", journal); panel.BindAchievements(journal);
                var observation = host.AddComponent<OceanNaturalist>(); observation.enabled = false;
                var observed = new OceanNaturalistStore(Path.Combine(directory, "observations"));
                Check(observed.TryRecord(0, 1, "2026-10-22T00:00:00Z"), "independent local naturalist store records an observation");
                Set(observation, "<Store>k__BackingField", observed);
                Set(observation, "leaderboard", bridge);
                Invoke(observation, "SyncDiscoveries");
                NoTransport(client, "production naturalist historical-sync path");
                Set(campaign, "<Naturalist>k__BackingField", observation); panel.BindNaturalist(observation);
                run.Save.bestStars[0] = 3;
                Set(campaign, "achievementHistory", JsonUtility.FromJson<OceanSaveData>(JsonUtility.ToJson(run.Save)));
                Set(campaign, "initialized", true);
                Invoke(campaign, "RefreshAchievements", false);
                Check(journal.IsUnlocked("shell-species-1") && journal.IsUnlocked("coins-1") &&
                    journal.IsUnlocked("levels-1") && journal.IsUnlocked("observe-1"),
                    "actual controller reconciliation keeps shell, coin, completion and naturalist achievements working offline");
                Check(journal.Progress("shell-total-50") == 2 && journal.Progress("fish-total-100") == 1,
                    "actual controller uses preserved and newly committed lifetime totals");

                panel.SetAvailable(true); panel.OpenMenu();
                NoRankingTargets(panel, "main menu");
                Check(!panel.ClickForValidation("leaderboard"), "production main menu has no actionable ranking target");
                CheckLegacyControllerAction(campaign, panel, hands, true);
                panel.CloseMenu(); hands.SetMenuLocomotionPaused(false);
                CheckLegacyControllerAction(campaign, panel, hands, false);
                panel.OpenMenu();
                Check(panel.ClickForValidation("show_comfort") && panel.ClickForValidation("comfort_more"), "production settings pages remain reachable");
                NoRankingTargets(panel, "settings");
                panel.OpenMenu();
                Check(panel.ClickForValidation("show_collection") && panel.ClickForValidation("naturalist_collection"), "production observation catalog remains reachable");
                NoRankingTargets(panel, "observation catalog");
                panel.OpenMenu();
                Check(panel.ClickForValidation("show_collection") && panel.ClickForValidation("show_achievements"), "production achievement page remains reachable");
                NoRankingTargets(panel, "achievements");
                for (int i = 0; i < 4; i++) { panel.ShowLevelPageForValidation(i); NoRankingTargets(panel, "chapter " + (i + 1)); }
                for (int i = 0; i < 5; i++) { panel.ShowCollectionForValidation(i); NoRankingTargets(panel, "collection page " + i); }
                NoTransport(client, "all production menu and collection pages");

                var reloadHost = new GameObject("Disabled local client reload");
                reloadHost.transform.SetParent(host.transform, false);
                var reloaded = reloadHost.AddComponent<OceanLeaderboardClient>(); reloaded.enabled = false;
                // A disabled release must not need a deployable URL even for direct initialization.
                reloaded.Initialize("not-an-online-endpoint", Path.Combine(directory, "Leaderboard"));
                Check(reloaded.LocalShellTotal == 2 && reloaded.LocalCoinTotal == 2 && reloaded.PendingCount == 1,
                    "restart reads new local totals and the exact old pending count without URL validation");
                Check(reloaded.RecordShell(run.Current.leaderboardRunId, 0, 7) && reloaded.RecordCoin(run.Current.leaderboardRunId, 20000) &&
                    reloaded.LocalShellTotal == 2 && reloaded.LocalCoinTotal == 2,
                    "restart retains pickup identities and never double credits the same shell or coin");
                AssertLegacyMetadata((OceanLeaderboardLedger)Get(reloaded, "ledger"), legacyEvent);
                NoTransport(reloaded, "restart and duplicate pickup recovery");
                Check(BytesEqual(discoveryOriginal, File.ReadAllBytes(discoveryFile)), "catalog and pickup paths never alter historical discovery upload records");
                passed = true;
            }
            finally
            {
                if (campaign != null) Set(campaign, "initialized", false);
                UnityEngine.Object.DestroyImmediate(host);
                if (passed && Directory.Exists(directory)) Directory.Delete(directory, true);
                else if (!passed) Debug.Log("Disabled leaderboard fixture preserved at " + directory);
            }
            Debug.Log("[OceanLeaderboardDisabledChecks] PASS " + Assertions +
                " assertions; production UI/controller/local journals with synthetic isolated history, no real network or headset claim.");
        }

        /// <summary>Call at actual entry, after pickups and after catalog interaction; never changes the running scene.</summary>
        public static void CheckInScene(OceanCampaignController campaign, string stage)
        {
            Check(!OceanReleaseFeatures.LeaderboardsEnabled && campaign != null, stage + ": real disabled-release controller required");
            var bridge = (OceanLeaderboardBridge)Get(campaign, "leaderboard");
            Check(bridge != null && !bridge.IsOpen && Get(bridge, "panel") == null, stage + ": no ranking UI is constructed or open");
            var clients = campaign.GetComponents<OceanLeaderboardClient>();
            Check(clients.Length == 1 && ReferenceEquals(clients[0], Get(bridge, "client")), stage + ": exactly one local-history client exists");
            foreach (OceanLeaderboardClient client in clients) NoTransport(client, stage);
            if (campaign.Panel != null) NoRankingTargets(campaign.Panel, stage);
        }

        static string SeedLegacyFullQueue(string directory)
        {
            var ledger = new OceanLeaderboardLedger(directory, 1);
            Check(ledger.BindIdentity(new string('a', 64)) && ledger.SetNickname("旧昵称草稿") && ledger.PendingCount == 1,
                "synthetic legacy fixture owns an authenticated identity and a full single-entry profile queue");
            // A fixture-owned confirmed historical name is distinct from the pending edit.
            string path = Path.Combine(directory, "ocean-leaderboard.json");
            var envelope = OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(File.ReadAllText(path)));
            var body = OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(OceanLeaderboardJson.Text(envelope, "body")));
            body["nickname"] = "海底观察员";
            string payload = OceanLeaderboardJson.Write(body);
            File.WriteAllText(path, OceanLeaderboardJson.Write(new Dictionary<string, object> {
                { "body", payload }, { "sha256", OceanLeaderboardLedger.Hash(payload) } }), new UTF8Encoding(false));
            var discovery = new OceanDiscoveryLedger(directory);
            Check(discovery.BindIdentity(new string('a', 64)) && discovery.Record(Session, 0, 1), "fixture retains an older pending discovery journal");
            return OceanLeaderboardJson.Write(ledger.Batch()[0].ToJson());
        }

        static void CheckLocalOnlyLedger(string directory, string legacyEvent)
        {
            var ledger = new OceanLeaderboardLedger(directory, 1, queueForUpload: false);
            AssertLegacyMetadata(ledger, legacyEvent);
            const string run = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            Check(ledger.BeginRun(Session, run, 1) && ledger.RecordShell(Session, run, 0, 5) && ledger.RecordCoin(Session, run, 20000),
                "a full old upload queue cannot block local run, shell or coin durability");
            int fishLevel = 0, species = 0;
            for (int level = 1; level <= OceanCampaignAvailability.AvailableLevelCount && fishLevel == 0; level++)
                for (int fish = 0; fish < OceanLevelCatalog.FishSpeciesCount; fish++)
                    if ((OceanLevelCatalog.Get(level).FishMask & (1 << fish)) != 0) { fishLevel = level; species = fish; break; }
            const string fishRun = "cccccccccccccccccccccccccccccccc";
            Check(fishLevel > 0 && ledger.BeginRun(Session, fishRun, fishLevel) && ledger.RecordFish(Session, fishRun, species, 10000 + species),
                "local-only mode preserves actual target-fish score validation");
            Check(ledger.ShellTotal == 1 && ledger.FishTotal == 1 && ledger.CoinTotal == 1 && ledger.PendingCount == 1,
                "local-only events change counters without changing pending uploads");
            AssertLegacyMetadata(ledger, legacyEvent);
            var reload = new OceanLeaderboardLedger(directory, 1, queueForUpload: false);
            Check(reload.RecordShell(Session, run, 0, 5) && reload.RecordCoin(Session, run, 20000) &&
                reload.ShellTotal == 1 && reload.FishTotal == 1 && reload.CoinTotal == 1 && reload.PendingCount == 1,
                "local-only restart preserves de-duplication despite sequence gaps in the untouched upload queue");
            AssertLegacyMetadata(reload, legacyEvent);
        }

        static void AssertLegacyMetadata(OceanLeaderboardLedger ledger, string legacyEvent)
        {
            var pending = ledger.Batch();
            Check(ledger.Writable && ledger.DeviceId == new string('a', 64) && ledger.Nickname == "海底观察员" &&
                ledger.NicknameDraft == "旧昵称草稿" && ledger.NicknameStatus == "pending" && pending.Count == 1 &&
                pending[0].sequence == 1 && pending[0].type == "profile" && pending[0].nickname == "旧昵称草稿" &&
                pending[0].eventId == ledger.PendingNicknameId && OceanLeaderboardJson.Write(pending[0].ToJson()) == legacyEvent,
                "old identity, confirmed nickname, pending nickname and original profile event remain unchanged");
        }

        static void CheckLegacyControllerAction(OceanCampaignController campaign, OceanCampaignPanel panel,
            HandSwimController hands, bool open)
        {
            string before = JsonUtility.ToJson(campaign.State.Save), page = panel.PageNameForDiagnostics;
            bool paused = hands.MenuLocomotionPaused;
            Set(campaign, "resultAt", 12345d);
            campaign.HandleAction("leaderboard", 0);
            Check(panel.IsOpen == open && hands.MenuLocomotionPaused == paused && panel.PageNameForDiagnostics == page &&
                (double)Get(campaign, "resultAt") == 12345d && JsonUtility.ToJson(campaign.State.Save) == before,
                "legacy controller ranking action is inert while menu " + (open ? "open" : "closed"));
        }

        static void NoRankingTargets(OceanCampaignPanel panel, string stage)
        {
            foreach (object target in (IEnumerable)Get(panel, "targets"))
            {
                string id = (string)Get(target, "id");
                Check(id.IndexOf("leaderboard", StringComparison.OrdinalIgnoreCase) < 0 &&
                    id.IndexOf("nickname", StringComparison.OrdinalIgnoreCase) < 0, stage + ": no built ranking or nickname target " + id);
            }
            foreach (Text text in panel.GetComponentsInChildren<Text>(true))
                if (text.gameObject.activeInHierarchy)
                    Check(!text.text.Contains("排行") && !text.text.Contains("昵称"), stage + ": no visible disabled-feature promise " + text.name);
        }

        static void NoTransport(OceanLeaderboardClient client, string stage)
        {
            foreach (string field in new[] { "identity", "identityTask", "signingTask", "request", "worker" })
                Check(Get(client, field) == null, stage + ": no " + field);
            Check(!client.Ready && !client.IsBusy && !(bool)Get(client, "pageQueued") && client.Profile == null && client.CachedPage == null &&
                Get(client, "profile") == null && Get(client, "cachedPage") == null && Get(client, "baseUrl") == null &&
                client.DeviceId == "" && client.PublicId == "" && client.Nickname == "" && client.NicknameDraft == "",
                stage + ": historical profile and cached rankings are not exposed");
        }

        static Dictionary<string, byte[]> Snapshot(string directory)
        { var result = new Dictionary<string, byte[]>(); foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)) result.Add(path, File.ReadAllBytes(path)); return result; }
        static void CheckUnchanged(Dictionary<string, byte[]> original, string detail)
        { foreach (var file in original) Check(File.Exists(file.Key) && BytesEqual(file.Value, File.ReadAllBytes(file.Key)), detail + ": " + Path.GetFileName(file.Key)); }
        static bool BytesEqual(byte[] a, byte[] b)
        { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        static object Get(object target, string field) => target.GetType().GetField(field, Fields).GetValue(target);
        static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
        static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Fields).Invoke(target, args);
    }
}
#endif

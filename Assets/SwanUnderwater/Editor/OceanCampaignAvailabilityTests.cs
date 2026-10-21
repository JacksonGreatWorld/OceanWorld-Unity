#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Public-release access gates, authenticated historical saves and actual menu/controller callbacks.
    /// Scene checks require a separate batch Editor and an explicitly isolated save directory.</summary>
    public static class OceanCampaignAvailabilityTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const string SeedFile = "availability-startup-fixture.json";
        public static int Assertions { get; private set; }
        static void Check(bool value, string message)
        { Assertions++; if (!value) throw new InvalidOperationException("Campaign availability: " + message); }
        static object Get(object owner, string field) => owner.GetType().GetField(field, Private).GetValue(owner);
        static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
        static string Hash(byte[] value)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "").ToLowerInvariant(); }
        static bool HasText(OceanCampaignPanel panel, string text)
        { foreach (Text label in panel.GetComponentsInChildren<Text>()) if (label.gameObject.activeInHierarchy && label.text.Contains(text)) return true; return false; }

        public static void RunAll()
        {
            Assertions = 0;
            Check(OceanCampaignAvailability.AvailableLevelCount == 20, "the public release opens exactly twenty levels");
            Check(OceanCampaignAvailability.IsOpen(1) && OceanCampaignAvailability.IsOpen(20), "both published boundary levels are open");
            Check(!OceanCampaignAvailability.IsOpen(0) && !OceanCampaignAvailability.IsOpen(21) &&
                !OceanCampaignAvailability.IsOpen(100) && !OceanCampaignAvailability.IsOpen(int.MaxValue), "invalid and future levels are closed");
            Check(OceanCampaignAvailability.IsFinalLevel(20) && !OceanCampaignAvailability.IsFinalLevel(19) &&
                !OceanCampaignAvailability.IsFinalLevel(100) && !OceanCampaignAvailability.IsFinalLevel(0), "twenty is this release's final level, independent of historical catalog size");
            Check(OceanCampaignAvailability.EntryLevel(1) == 1 && OceanCampaignAvailability.EntryLevel(20) == 20 &&
                OceanCampaignAvailability.EntryLevel(21) == 1 && OceanCampaignAvailability.EntryLevel(100) == 1,
                "entry presentation chooses a safe open environment for future history");
            Check(OceanCampaignAvailability.UnavailableMessage == "未开放，敬请期待", "locked-level explanation matches the requested player copy");
            var font = Resources.Load<Font>("Treasure/TreasureFont");
            Check(font != null, "the actual menu body font is present");
            foreach (char character in OceanCampaignAvailability.UnavailableMessage)
                Check(font.HasCharacter(character), "the locked-level notice has a real glyph: " + character);

            string directory = Path.Combine(Path.GetTempPath(), "ocean-availability-save-" + Guid.NewGuid().ToString("N"));
            try
            {
                var historical = HistoricalFutureState();
                string before = JsonUtility.ToJson(historical.Save);
                Check(OceanCampaignState.Validate(historical.Save, out string error), "future historical run remains a valid durable save: " + error);
                Check(OceanSaveStore.TrySave(directory, historical.Save, out error), "private future-history fixture can be authenticated: " + error);
                var bytes = SnapshotFiles(directory);
                Check(OceanSaveStore.TryLoad(directory, out OceanSaveData loaded, out OceanSaveLoadStatus status, out error) &&
                    status == OceanSaveLoadStatus.Loaded, "reader accepts the authenticated level100 history: " + error);
                Check(loaded.bestStars.Length == 100 && loaded.currentRun.level == 100 && JsonUtility.ToJson(loaded) == before,
                    "opening only twenty levels does not truncate, renumber or replace the saved hundred-level history");
                var owner = new OceanCampaignState(loaded);
                Check(owner.Current.level == 100 && owner.Save.bestStars[99] == 3 && owner.Save.shellMask == historical.Save.shellMask &&
                    owner.Save.fishMask == historical.Save.fishMask, "state construction preserves future best stars and collections");
                Check(OceanCampaignAvailability.EntryLevel(owner.Current.level) == 1 && JsonUtility.ToJson(owner.Save) == before,
                    "safe entry selection never edits the historical run or its identity");
                Check(OceanSaveStore.TryReloadExistingReadOnly(directory, out loaded, out status, out error) &&
                    loaded.currentRun.level == 100 && JsonUtility.ToJson(loaded) == before, "explicit read-only retry retains the exact future snapshot");
                Check(SameFiles(bytes, SnapshotFiles(directory)), "policy and read-only lookup do not rewrite either original file");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            Debug.Log("[OceanCampaignAvailabilityTests] PASS " + Assertions + " assertions; access policy and isolated authenticated historical files, no headset acceptance.");
        }

        /// <summary>Invoke from the prepared isolated world preview after tutorial and entry complete.
        /// All attempted level changes are rejected or left at confirmation; no scene is rebuilt.</summary>
        public static IEnumerator RunInScene(TreasureHunt hunt)
        {
            RequireIsolatedScene(hunt);
            Assertions = 0;
            var campaign = hunt.Campaign;
            using (var fixture = new SceneFixture(hunt))
            {
                fixture.Enter();
                var panel = campaign.Panel;
                foreach (int level in new[] { 1, 20 })
                {
                    fixture.ShowLevel(level);
                    Check(panel.ClickForValidation("start_level", level), "published level " + level + " is selectable through its real button");
                    Check(panel.PageNameForDiagnostics == "Confirm", "published level selection still requires explicit confirmation");
                    fixture.AssertUnchanged("open-level confirmation " + level);
                }
                foreach (int level in new[] { 21, 100 })
                {
                    fixture.ShowLevel(level);
                    Check(panel.ClickForValidation("start_level", level), "future level " + level + " remains clickable for an explanation");
                    Check(panel.PageNameForDiagnostics != "Confirm" && HasText(panel, OceanCampaignAvailability.UnavailableMessage),
                        "future selection explains its status without entering the start confirmation");
                    fixture.AssertUnchanged("future UI selection " + level);
                }
                panel.ShowUnavailableLevel(21);
                Check(HasText(panel, OceanCampaignAvailability.UnavailableMessage), "the controller's public unavailable UI presents the same player message");
                foreach (int level in new[] { 21, 100 }) fixture.Refuse("start_level", level);

                // Synthetic historical runs stay inside this paused Editor fixture; no save or score callback accepts them.
                fixture.UseRun(HistoricalFutureState().Current);
                fixture.Refuse("restart_level", 0);
                fixture.Refuse("resume", 0);
                Check(!campaign.GameplayAllowed, "future historical progress cannot be resumed into unpublished gameplay");

                var final = new OceanCampaignState(); final.StartLevel(20); CompleteTargets(final);
                Check(final.GoalComplete && OceanCampaignState.Validate(final.Save, out _), "final-open-level fixture has legitimately completed its rule targets");
                fixture.UseRun(final.Current);
                fixture.Refuse("next_level", 0);
                Check(campaign.State.Current.level == 20, "next from twenty cannot launch or allocate level21");
            }
            Debug.Log("[OceanCampaignAvailabilityTests] SCENE PASS " + Assertions + " assertions; actual controller/menu callbacks, unchanged actors/resources and private save bytes; synthetic Editor state only.");
            yield break;
        }

        /// <summary>Call before opening the world scene, in a NEW -codexCampaignSaveDir.
        /// Use a separate batch invocation; never point this fixture at an existing player directory.</summary>
        public static void SeedUnavailableStartupFixture()
        {
            if (!Application.isEditor || !Application.isBatchMode || Application.isPlaying)
                throw new InvalidOperationException("Future-startup seed requires a separate batch Editor before Play Mode.");
            string directory = IsolatedDirectory();
            if (Directory.Exists(directory) || File.Exists(directory))
                throw new IOException("Future-startup fixture requires a brand-new directory; existing files are preserved.");
            // Two independently valid generations expose an otherwise invisible backup rotation:
            // writing the latest run again would replace the older recovery generation.
            var older = HistoricalFutureState(); older.Save.bestStars[97] = 0;
            if (!OceanSaveStore.TrySave(directory, older.Save, out string error)) throw new IOException("Future-startup older seed failed: " + error);
            var historical = HistoricalFutureState();
            if (!OceanSaveStore.TrySave(directory, historical.Save, out error)) throw new IOException("Future-startup current seed failed: " + error);
            string primary = Path.Combine(directory, OceanSaveStore.FileName), backup = Path.Combine(directory, OceanSaveStore.BackupFileName);
            string ledgerDirectory = Path.Combine(directory, "Leaderboard", "editor-test");
            var ledger = new OceanLeaderboardLedger(ledgerDirectory);
            const string session = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", run = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            if (!ledger.BeginRun(session, run, 1) || !ledger.RecordShell(session, run, 0, 42) || !ledger.RecordCoin(session, run, 20000))
                throw new IOException("Future-startup private score seed failed.");
            var pending = new List<string>(); foreach (var item in ledger.Batch(64, 100)) pending.Add(item.eventId);
            var seed = new StartupSeed {
                primaryHash = Hash(File.ReadAllBytes(primary)), backupHash = Hash(File.ReadAllBytes(backup)),
                primaryWriteTicks = File.GetLastWriteTimeUtc(primary).Ticks, backupWriteTicks = File.GetLastWriteTimeUtc(backup).Ticks,
                expectedSaveJson = JsonUtility.ToJson(historical.Save), expectedBackupSaveJson = JsonUtility.ToJson(older.Save),
                expectedShells = ledger.ShellTotal, expectedFish = ledger.FishTotal, expectedCoins = ledger.CoinTotal,
                expectedPendingEventIds = pending.ToArray()
            };
            if (seed.primaryHash == seed.backupHash) throw new InvalidOperationException("Startup fixture must retain distinct valid primary and recovery generations.");
            File.WriteAllText(Path.Combine(directory, SeedFile), JsonUtility.ToJson(seed));
        }

        /// <summary>Checks the production startup path after SeedUnavailableStartupFixture and normal world loading.
        /// The saved run remains100; only its presentation environment may safely fall back to level1.</summary>
        public static IEnumerator RunUnavailableStartupInScene(TreasureHunt hunt)
        {
            RequireIsolatedScene(hunt);
            Assertions = 0;
            var campaign = hunt.Campaign;
            var seed = JsonUtility.FromJson<StartupSeed>(File.ReadAllText(Path.Combine(campaign.SaveDirectory, SeedFile)));
            Check(seed != null && seed.schema == 1, "this is an explicitly seeded startup fixture");
            Check(seed.primaryHash != seed.backupHash && seed.expectedSaveJson != seed.expectedBackupSaveJson,
                "fixture starts with independently valid, observably different current and recovery generations");
            AssertStartupOriginals(campaign.SaveDirectory, seed, "normal startup");
            Check(campaign.State.Current.level == 100 && campaign.State.Save.bestStars.Length == 100 &&
                JsonUtility.ToJson(campaign.State.Save) == seed.expectedSaveJson, "production controller keeps the exact future history and active run");
            var world = (UnderwaterWorld)Get(campaign, "world");
            var safe = OceanEnvironmentIdentity.ForLevel(1);
            Check(world.ActiveEnvironment == safe.Terrain && world.ActiveBiome == safe.Biome && world.ActiveEnvironmentRoot != null,
                "production startup constructs the safe first environment instead of the future biome");
            Check(((IDictionary)Get(world, "preparedEnvironments")).Count == 1 && campaign.Mechanics.StationRoot == null,
                "startup does not allocate an unpublished cached environment or future mechanic station");
            using (var fixture = new SceneFixture(hunt))
            {
                fixture.Enter();
                campaign.Panel.CloseMenu();
                Check(!campaign.GameplayAllowed, "closing the menu cannot bypass the future-run gameplay gate");
                fixture.Refuse("resume", 0);
                fixture.Refuse("restart_level", 0);
                fixture.Refuse("start_level", 100);
            }
            AssertStartupOriginals(campaign.SaveDirectory, seed, "future-run resume/restart refusals before explicit new-level consent");

            // This second phase deliberately accepts a new open run through the production
            // writable path. Do not wrap it in SceneFixture, whose refusal-only checks suppress writes.
            Check((bool)Get(campaign, "writable"), "authenticated future history retains the ordinary writable controller path");
            Check(!campaign.LastSaveSucceeded, "archive protection did not pretend to save or rotate the future run");
            var bridge = (OceanLeaderboardBridge)Get(campaign, "leaderboard");
            if (!bridge.LocalProgressAvailable)
                Check(bridge.PersistRun((OceanLevelRun)Get(campaign, "committedScoreRun")), "verified archived run can initialize its local score bridge");
            var client = (OceanLeaderboardClient)Get(bridge, "client");
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (!client.Ready)
            { Check(Time.realtimeSinceStartupAsDouble < deadline, "private Editor score identity becomes ready within thirty seconds"); yield return null; }
            var identity = Get(client, "identity") as OceanLeaderboardIdentity;
            Check(identity != null && identity.IsTestIdentity && !client.IsBusy, "new-run acceptance uses only an idle private Editor test identity");
            string identityBefore = client.DeviceId;
            AssertSeededScore(campaign.SaveDirectory, seed);
            var historical = JsonUtility.FromJson<OceanSaveData>(seed.expectedSaveJson);
            OceanLevelRun archived = campaign.State.Current;
            campaign.HandleAction("start_level", 1);
            Check(campaign.EnvironmentTransitioning || campaign.State.Current.level == 1,
                "production HandleAction accepts open level1 from archived100 even though the protected archive was not rewritten");
            deadline = Time.realtimeSinceStartupAsDouble + 90;
            try
            {
                while (campaign.EnvironmentTransitioning)
                { Check(Time.realtimeSinceStartupAsDouble < deadline, "real archived-to-open transition completes within ninety seconds"); yield return null; }
            }
            finally
            {
                if (campaign.EnvironmentTransitioning) typeof(OceanCampaignController).GetMethod("CancelLevelSwitch", Private).Invoke(campaign, null);
            }
            Check(campaign.State.Current.level == 1 && !ReferenceEquals(campaign.State.Current, archived) && campaign.LastSaveSucceeded,
                "real writable transition creates and commits a fresh open run rather than remaining stranded in the archive");
            Check(campaign.ActorsPrepared && world.ActiveEnvironment == safe.Terrain && world.ActiveBiome == safe.Biome,
                "accepted open run has prepared real actors in its playable environment");
            Check(SameHistory(historical, campaign.State.Save), "starting open level1 preserves all hundred best-star slots and global collections");
            Check(OceanSaveStore.TryLoad(campaign.SaveDirectory, out OceanSaveData saved, out OceanSaveLoadStatus loadStatus, out string error) &&
                loadStatus == OceanSaveLoadStatus.Loaded && saved.currentRun.level == 1 && SameHistory(historical, saved),
                "ordinary writable save commits open level1 without truncating authenticated hundred-level history: " + error);
            Check(client.DeviceId == identityBefore, "open-run transition preserves the private fixture's score identity");
            AssertSeededScore(campaign.SaveDirectory, seed);
            Debug.Log("[OceanCampaignAvailabilityTests] STARTUP PASS " + Assertions + " assertions; distinct authenticated future primary/recovery preserved through startup/refusal, then actual writable level100-to-level1 transition with history and score retained; private Editor fixture only.");
        }

        [Serializable] sealed class StartupSeed
        {
            public int schema = 1;
            public string primaryHash, backupHash, expectedSaveJson, expectedBackupSaveJson;
            public long primaryWriteTicks, backupWriteTicks, expectedShells, expectedFish, expectedCoins;
            public string[] expectedPendingEventIds;
        }

        static void AssertStartupOriginals(string directory, StartupSeed seed, string context)
        {
            string primary = Path.Combine(directory, OceanSaveStore.FileName), backup = Path.Combine(directory, OceanSaveStore.BackupFileName);
            Check(Hash(File.ReadAllBytes(primary)) == seed.primaryHash && Hash(File.ReadAllBytes(backup)) == seed.backupHash,
                context + " preserves both distinct authenticated originals byte for byte");
            Check(File.GetLastWriteTimeUtc(primary).Ticks == seed.primaryWriteTicks && File.GetLastWriteTimeUtc(backup).Ticks == seed.backupWriteTicks,
                context + " preserves both write times rather than silently rewriting an identical generation");
        }
        static bool SameHistory(OceanSaveData before, OceanSaveData after)
        {
            if (before.bestStars.Length != 100 || after.bestStars.Length != 100 || before.shellMask != after.shellMask || before.fishMask != after.fishMask || before.shark != after.shark) return false;
            for (int i = 0; i < 100; i++) if (before.bestStars[i] != after.bestStars[i]) return false;
            return true;
        }
        static void AssertSeededScore(string directory, StartupSeed seed)
        {
            var ledger = new OceanLeaderboardLedger(Path.Combine(directory, "Leaderboard", "editor-test"));
            Check(ledger.Writable && seed.expectedShells > 0 && seed.expectedCoins > 0 && ledger.ShellTotal == seed.expectedShells &&
                ledger.FishTotal == seed.expectedFish && ledger.CoinTotal == seed.expectedCoins,
                "known nonzero private score survives startup and the accepted new open run");
            var pending = new HashSet<string>(); foreach (var item in ledger.Batch(64, 100)) pending.Add(item.eventId);
            Check(seed.expectedPendingEventIds != null && seed.expectedPendingEventIds.Length == 4,
                "private fixture includes a genuine durable demo/run/shell/coin outbox rather than zero-score defaults");
            foreach (string id in seed.expectedPendingEventIds) Check(pending.Contains(id), "old private pending evidence remains in the durable outbox");
        }

        static OceanCampaignState HistoricalFutureState()
        {
            var result = new OceanCampaignState(); result.StartLevel(100);
            for (int i = 0; i < result.Save.bestStars.Length; i++) result.Save.bestStars[i] = i % 3 == 0 ? 3 : i % 3 == 1 ? 2 : 0;
            result.Save.bestStars[99] = 3; result.Save.shellMask = 1023;
            result.Save.fishMask = (1 << OceanCampaignState.FishSpeciesCount) - 1;
            result.Save.shark = true;
            return result;
        }

        static string IsolatedDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-codexCampaignSaveDir")
            {
                string directory = Path.GetFullPath(args[i + 1]);
                if (directory == Path.GetFullPath(Application.persistentDataPath)) throw new InvalidOperationException("The player's persistent directory is forbidden.");
                return directory;
            }
            throw new InvalidOperationException("An explicit isolated -codexCampaignSaveDir is required.");
        }
        static void RequireIsolatedScene(TreasureHunt hunt)
        {
            if (!Application.isEditor || !Application.isBatchMode || !Application.isPlaying || hunt == null || hunt.Campaign == null || !hunt.CoreActive)
                throw new InvalidOperationException("Availability scene checks need a ready actual world in isolated batch Editor Play Mode.");
            if (Path.GetFullPath(hunt.Campaign.SaveDirectory) != IsolatedDirectory()) throw new InvalidOperationException("Campaign is not using the explicit private fixture directory.");
            if (OceanEntry.BlocksGameplay || hunt.Campaign.EnvironmentTransitioning || hunt.Campaign.Panel.TutorialActive)
                throw new InvalidOperationException("Complete entry, tutorial and any environment transition first.");
        }
        static Dictionary<string, string> SnapshotFiles(string directory)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Directory.Exists(directory)) foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)) result.Add(path, Hash(File.ReadAllBytes(path)));
            return result;
        }
        static bool SameFiles(Dictionary<string, string> before, Dictionary<string, string> after)
        { if (before.Count != after.Count) return false; foreach (var item in before) if (!after.TryGetValue(item.Key, out string hash) || hash != item.Value) return false; return true; }
        static void CompleteTargets(OceanCampaignState state)
        {
            for (int i = 0; i < OceanCampaignState.ShellSpeciesCount; i++) if ((state.ShellTargetMask & (1 << i)) != 0) Check(state.CollectShell(i, i), "fixture's required shell is accepted");
            for (int i = 0; i < OceanCampaignState.FishSpeciesCount; i++) if ((state.FishTargetMask & (1 << i)) != 0) Check(state.CollectFish(i), "fixture's required fish is accepted");
            if (state.HasMechanic) for (int i = 0; i < state.Definition.MechanicTarget; i++) Check(state.RecordMechanicProgress(100000 + i), "fixture's required mechanic node is accepted");
            if (state.RequiresShark)
            {
                Check(state.AcquireTool(), "fixture owns its required tool");
                while (state.Current.sharkHits < state.SharkHitTarget)
                {
                    if (state.Current.pendingSpearRecovery) Check(state.TryRecoverSpear(), "fixture recovers without refilling");
                    Check(state.TryFireSpear() && state.HitShark(), "fixture spends one round per shark hit");
                }
                Check(state.CatchShark(), "fixture's target shark can be captured");
            }
        }

        sealed class SceneFixture : IDisposable
        {
            readonly TreasureHunt hunt; readonly OceanCampaignController campaign; readonly OceanCampaignState state;
            readonly OceanCampaignPanel panel; readonly UnderwaterWorld world; readonly HandSwimController hands;
            readonly OceanLevelRun originalRun; readonly string originalJson;
            readonly Dictionary<string, string> files;
            readonly Dictionary<Behaviour, bool> enabled = new Dictionary<Behaviour, bool>();
            readonly Dictionary<string, object> fields = new Dictionary<string, object>();
            readonly Transform originalEnvironment; readonly int originalCacheCount; readonly string originalActors;
            readonly object page; readonly bool available, open, menuPaused, suppressed, lastSaveSucceeded;
            readonly int levelPage; bool entered;
            public SceneFixture(TreasureHunt hunt)
            {
                this.hunt = hunt; campaign = hunt.Campaign; state = campaign.State; panel = campaign.Panel;
                world = (UnderwaterWorld)Get(campaign, "world"); hands = (HandSwimController)Get(campaign, "hands");
                originalRun = state.Current; originalJson = JsonUtility.ToJson(state.Save); files = SnapshotFiles(campaign.SaveDirectory);
                originalEnvironment = world.ActiveEnvironmentRoot; originalCacheCount = ((IDictionary)Get(world, "preparedEnvironments")).Count; originalActors = ActorIds(hunt);
                page = Get(panel, "page"); levelPage = panel.LevelPageForValidation; available = panel.IsAvailableForDiagnostics; open = panel.IsOpen;
                menuPaused = hands.MenuLocomotionPaused; suppressed = hands.InteractionSuppressed; lastSaveSucceeded = campaign.LastSaveSucceeded;
                foreach (string name in new[] { "writable", "warnedSave", "nextSave", "nextScoreRetry", "resultAt", "resumedFrame", "committedScoreRun", "environmentFaulted" }) fields.Add(name, Get(campaign, name));
            }
            public void Enter()
            {
                foreach (var client in campaign.GetComponents<OceanLeaderboardClient>())
                { Check(!client.IsBusy, "existing test transport must be idle before availability checks"); enabled.Add(client, client.enabled); }
                enabled.Add(campaign, campaign.enabled); enabled.Add(campaign.Naturalist, campaign.Naturalist.enabled);
                entered = true;
                foreach (var item in enabled) item.Key.enabled = false;
                Set(campaign, "writable", false); Set(campaign, "warnedSave", true); Set(campaign, "nextSave", double.PositiveInfinity); Set(campaign, "nextScoreRetry", double.PositiveInfinity);
                panel.SetAvailable(true); panel.OpenMenu(); hands.SetMenuLocomotionPaused(true);
            }
            public void ShowLevel(int level) { panel.ShowLevelPageForValidation((level - 1) / 5); }
            public void UseRun(OceanLevelRun run) { state.Save.currentRun = run; panel.Refresh(); }
            public void AssertUnchanged(string context)
            {
                Check(ReferenceEquals(state.Current, originalRun) && JsonUtility.ToJson(state.Save) == originalJson && !campaign.EnvironmentTransitioning,
                    context + " preserves exact original run and campaign data");
                Check(world.ActiveEnvironmentRoot == originalEnvironment && ((IDictionary)Get(world, "preparedEnvironments")).Count == originalCacheCount && ActorIds(hunt) == originalActors,
                    context + " preserves the actual environment cache and actor instances");
                Check(SameFiles(files, SnapshotFiles(campaign.SaveDirectory)), context + " never writes original private files");
            }
            public void Refuse(string action, int level)
            {
                OceanLevelRun run = state.Current; string json = JsonUtility.ToJson(state.Save), actors = ActorIds(hunt);
                Transform environment = world.ActiveEnvironmentRoot;
                int cacheCount = ((IDictionary)Get(world, "preparedEnvironments")).Count;
                campaign.HandleAction(action, level);
                Check(ReferenceEquals(state.Current, run) && JsonUtility.ToJson(state.Save) == json && !campaign.EnvironmentTransitioning,
                    action + " cannot replace or mutate the closed run");
                Check(world.ActiveEnvironmentRoot == environment && cacheCount == ((IDictionary)Get(world, "preparedEnvironments")).Count && actors == ActorIds(hunt),
                    action + " cannot replace environment roots, allocate a future cache or rebuild actors");
                Check(panel.IsOpen && HasText(panel, OceanCampaignAvailability.UnavailableMessage), action + " leaves a reachable menu and clear unavailable explanation");
                Check(SameFiles(files, SnapshotFiles(campaign.SaveDirectory)), action + " preserves every original private file");
            }
            public void Dispose()
            {
                if (!entered) return;
                if (campaign.EnvironmentTransitioning) typeof(OceanCampaignController).GetMethod("CancelLevelSwitch", Private).Invoke(campaign, null);
                state.Save.currentRun = originalRun;
                foreach (var item in fields) Set(campaign, item.Key, item.Value);
                typeof(OceanCampaignController).GetProperty("LastSaveSucceeded").SetValue(campaign, lastSaveSucceeded);
                panel.SetAvailable(available);
                if (open) panel.OpenMenu(); else panel.CloseMenu();
                Set(panel, "levelPage", levelPage);
                typeof(OceanCampaignPanel).GetMethod("ShowPage", Private).Invoke(panel, new[] { page });
                hands.SetMenuLocomotionPaused(menuPaused); hands.SetInteractionSuppressed(suppressed);
                foreach (var item in enabled) if (item.Key != null) item.Key.enabled = item.Value;
                Check(ReferenceEquals(state.Current, originalRun) && JsonUtility.ToJson(state.Save) == originalJson && SameFiles(files, SnapshotFiles(campaign.SaveDirectory)),
                    "availability fixture restores state owner/input and leaves all private source files untouched");
            }
        }
        static string ActorIds(TreasureHunt hunt)
        {
            var ids = new List<int>();
            foreach (var item in hunt.Pickups) if (item.visual != null) ids.Add(item.visual.GetInstanceID());
            foreach (var actor in hunt.Campaign.Creatures.Targets) if (actor.Visual != null) ids.Add(actor.Visual.GetInstanceID());
            var gun = hunt.Campaign.Creatures.SpearGun; if (gun != null) ids.Add(gun.GetInstanceID());
            var station = hunt.Campaign.Mechanics.StationRoot; if (station != null) ids.Add(station.GetInstanceID());
            ids.Sort(); return string.Join(",", ids);
        }
    }
}
#endif

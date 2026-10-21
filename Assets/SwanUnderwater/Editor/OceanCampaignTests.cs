#if UNITY_EDITOR || OCEAN_CAMPAIGN_STANDALONE
using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SwanUnderwater.EditorTests
{
    public static class OceanCampaignTests
    {
        public static int LastAssertionCount { get; private set; }
        static void Check(bool value, string detail)
        { LastAssertionCount++; if (!value) throw new InvalidOperationException("Ocean campaign: " + detail); }
        public static void RunAll()
        {
            LastAssertionCount = 0;
            RulesAndClock(); ShellOrders(new[] { 0, 1, 2, 3, 4 }, 0); MixedOrders(new[] { 0, 1, 2, 3, 4 }, 0);
            AllLevelsAndHistory(); ToolGate(); AmmunitionBudget(); AmmunitionFinalization(); IndependentSharks(); IndependentSharkPersistence(); Persistence(); LegacySpearMigration(); LeaderboardMigrationAndRecovery(); CommittedScoreBoundary(); TwentyLevelRules(); VersionTwoMigration(); ShellScoreMigration(); HundredLevelRules(); TwentySlotHistoryMigration(); RecoveryChallenge(); VersionFourRecoveryMigration();
            string summary = "OceanCampaignTests PASS: " + LastAssertionCount + " assertions; 100-level catalog, ordered targets, multi-coin recovery, 120 legacy shell orders, 120 legacy mixed orders, timers, two/three stars, independent shark identities/hits/capture, durable shared 20-round budget/final resolution, legacy migration, history, actual JSON/filesystem roundtrip and backup recovery. No Unity scene, tracked input or headset claim.";
#if UNITY_EDITOR
            UnityEngine.Debug.Log(summary);
#else
            Console.WriteLine(summary);
#endif
        }
        static void Valid(OceanCampaignState state)
        { Check(OceanCampaignState.Validate(state.Save, out string error), "state validates: " + error); }
        static void RulesAndClock()
        {
            var state = new OceanCampaignState();
            Check(state.Current == null && !state.CanCollect && state.Stars == 0, "new save is menu-ready without an implicit run");
            for (int level = 1; level <= 5; level++)
            {
                state.StartLevel(level); Check(state.Current.level == level, "all levels selectable");
                Check(state.TimeLimitSeconds == (level == 5 ? 0 : level == 1 ? 1800 : 1200), "confirmed level time limit");
                Check(!state.AcquireTool() || level == 5, "speargun limited to level five"); Valid(state);
            }
            bool threw = false; try { state.StartLevel(0); } catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw && state.Current.level == 5, "invalid selection cannot destroy active run");
            state.StartLevel(1); state.Tick(1799.5f, false); Check(state.Current.remainingSeconds == 1800, "paused time unchanged");
            foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f }) state.Tick(invalid, true);
            Check(state.Current.remainingSeconds == 1800, "invalid time input ignored");
            state.Tick(1799.5f, true); Check(state.Current.remainingSeconds == .5f && !state.Failed, "fractional last second remains playable");
            Check(state.CollectCoin(20000) && state.Stars == 0 && !state.GoalComplete, "coin alone earns no stars or completion");
            state.Tick(.5f, true); Check(state.TimedOut && state.Failed && state.Stars == 0, "exact time boundary fails incomplete target");
            Check(!state.CollectShell(1, 0) && !state.CollectCoin(20000), "no pickups after timeout"); Valid(state);
            state.RestartLevel(); CompleteTargets(state); state.Tick(1800, true);
            Check(state.TimedOut && !state.Failed && state.GoalComplete && state.Stars == 2, "timeout retains earned two stars");
            Check(!state.CollectCoin(20000), "cannot add third star after timeout"); Valid(state);
            state.RestartLevel(); CompleteTargets(state); Check(state.CollectCoin(20000), "coin accepted after targets");
            state.Tick(float.MaxValue, true); Check(state.Stars == 3 && state.TimedOut && !state.Failed, "large tick saturates without losing three stars"); Valid(state);
            state.StartLevel(4); Check(!state.CollectShell(1, 0) && !state.IsConsumed(1), "wrong category does not consume instance");
            Check(!state.CollectFish(-1) && !state.CollectFish(4), "invalid fish rejected");
            state.StartLevel(1); Check(!state.CollectShell(-1, 0) && !state.CollectShell(1, 5) && !state.CollectShell(1, -1), "invalid shell rejected");
            Check(!state.CollectCoin(-1), "invalid coin ID rejected");
            Check(state.CollectShell(1, 0) && !state.CollectShell(1, 1) && !state.CollectCoin(1), "physical ID shared across shell and coin events");
            Check(state.CollectShell(2, 0) && state.Current.shellMask == 1 && state.IsConsumed(2), "different physical duplicate disappears without extra species");
            Check(!state.AcquireTool() && !state.HitShark() && !state.CatchShark(), "level one cannot mutate shark state"); Valid(state);
        }
        static void ShellOrders(int[] order, int depth)
        {
            if (depth == order.Length)
            {
                var state = new OceanCampaignState(); state.StartLevel(1);
                for (int i = 0; i < order.Length; i++)
                {
                    Check(state.CollectShell(i, order[i]), "new shell accepted");
                    Check(!state.CollectShell(i, order[i]), "same shell not replayed");
                    Check(state.CollectShell(i + 100, order[i]), "physical duplicate accepted");
                    Check(state.GoalComplete == (i == 4) && state.Stars == (i == 4 ? 2 : 0), "target completion independent of collection order");
                }
                Check(state.CollectCoin(20000) && state.Stars == 3 && state.Save.bestStars[0] == 3, "after-target coin upgrades best result");
                Check(!state.CollectCoin(20000), "hidden reward accepted once"); Valid(state); return;
            }
            for (int i = depth; i < order.Length; i++)
            { Swap(order, depth, i); ShellOrders(order, depth + 1); Swap(order, depth, i); }
        }
        static void MixedOrders(int[] order, int depth)
        {
            if (depth == order.Length)
            {
                var state = new OceanCampaignState(); state.StartLevel(3); Check(state.CollectCoin(20000), "coin before mixed goals");
                for (int i = 0; i < order.Length; i++)
                {
                    int item = order[i]; Check(item < 2 ? state.CollectShell(item, item) : state.CollectFish(item - 2), "mixed target accepted");
                    Check(state.GoalComplete == (i == 4) && state.Stars == (i == 4 ? 3 : 0), "mixed goal needs every category");
                }
                Check(!state.CollectFish(0), "same fish species cannot replay"); Valid(state); return;
            }
            for (int i = depth; i < order.Length; i++)
            { Swap(order, depth, i); MixedOrders(order, depth + 1); Swap(order, depth, i); }
        }
        static void Swap(int[] values, int a, int b) { int old = values[a]; values[a] = values[b]; values[b] = old; }
        static void CompleteTargets(OceanCampaignState state)
        {
            for (int i = 0; i < 10; i++) if ((state.ShellTargetMask & (1 << i)) != 0) state.CollectShell(i, i);
            for (int i = 0; i < OceanCampaignState.FishSpeciesCount; i++) if ((state.FishTargetMask & (1 << i)) != 0) state.CollectFish(i);
            if (state.HasMechanic) for (int node = 0; node < state.Definition.MechanicTarget; node++) state.RecordMechanicProgress(100000 + node);
            if (state.RequiresShark)
            {
                state.AcquireTool();
                while (state.Current.sharkHits < state.SharkHitTarget) { if (state.Current.pendingSpearRecovery) Check(state.TryRecoverSpear(), "helper retrieves without refilling"); Check(state.TryFireSpear(), "target helper consumes round"); Check(state.HitShark(), "target helper accepts hit"); }
                state.CatchShark();
            }
        }
        static void AllLevelsAndHistory()
        {
            var state = new OceanCampaignState();
            for (int level = 1; level <= OceanCampaignState.LevelCount; level++)
            {
                state.StartLevel(level); CompleteTargets(state); Check(state.GoalComplete && state.Stars == 2, "every target set earns two stars");
                for (int slot = state.CoinTargetCount - 1; slot >= 0; slot--) Check(state.CollectCoin(20000 + slot), "completed run permits each remaining coin");
                Check(state.Stars == 3, "all coins upgrade a completed target to three stars");
                Check(state.Save.bestStars[level - 1] == 3, "best result recorded per level");
                state.RestartLevel(); Check(state.Stars == 0 && state.Save.bestStars[level - 1] == 3, "restart clears run without deleting best");
                CompleteTargets(state); Check(state.Save.bestStars[level - 1] == 3, "later two-star result does not downgrade history"); Valid(state);
            }
            Check(state.Save.shellMask == 1023 && state.Save.fishMask == 1023 && state.Save.shark, "global collection contains every earned species");
            state.ResetLevel(2); Check(state.Current.level == 2 && state.Current.remainingSeconds == 1200 && state.Current.shellMask == 0 && state.Current.consumedPickupIds.Count == 0, "reset starts selected level clean");
            state.RestartGame(); Check(state.Current.level == 1 && state.Current.remainingSeconds == 1800 && state.Current.fishMask == 0 && !state.Current.coinCollected, "restart game begins clean level one");
            foreach (int stars in state.Save.bestStars) Check(stars == 3, "restart game retains all best stars");
            Check(state.Save.shellMask == 1023 && state.Save.fishMask == 1023 && state.Save.shark, "restart game retains global collection");
            var restored = new OceanCampaignState(state.Save); state.CollectShell(1, 0);
            Check(restored.Current.shellMask == 0 && !restored.IsConsumed(1), "constructor owns a deep copy"); Valid(restored);
        }
        static void ToolGate()
        {
            var state = new OceanCampaignState(); state.StartLevel(5);
            Check(state.UnlimitedTime && state.Current.remainingSeconds == 0 && !state.TimedOut, "level five zero means unlimited");
            state.Tick(float.MaxValue, true); Check(!state.Failed && !state.TimedOut && state.CanCollect, "unlimited level never times out");
            Check(!state.HitShark() && !state.CatchShark(), "no speargun means no hits/catch");
            Check(state.AcquireTool() && !state.AcquireTool(), "speargun acquired once");
            for (int hit = 1; hit <= 9; hit++)
            { Check(state.TryFireSpear() && state.HitShark() && state.Current.sharkHits == hit, "validated spent shot counted once"); Check(!state.CatchShark() && !state.GoalComplete, "cannot catch before tenth hit"); }
            Check(state.TryFireSpear() && state.HitShark() && state.Current.sharkHits == 10 && !state.HitShark(), "hit count caps at ten");
            Check(!state.GoalComplete && state.Stars == 0, "ten hits alone do not complete capture objective");
            Check(state.CatchShark() && !state.CatchShark() && state.GoalComplete && state.Stars == 2 && state.Save.shark, "catch after ten hits completes two-star ending");
            Check(state.CollectCoin(20000) && state.Stars == 3, "continue exploring after ending can add coin"); Valid(state);
            state.RestartLevel(); state.CollectCoin(20000); Check(state.Stars == 0 && !state.GoalComplete, "level five coin alone not an ending");
            CompleteTargets(state); Check(state.Stars == 3, "coin before catch yields three-star ending"); Valid(state);
        }
        static void AmmunitionBudget()
        {
            var state = new OceanCampaignState();
            Check(!state.TryFireSpear() && state.SpearAmmoRemaining == 0, "no run has no ammunition");
            state.StartLevel(1); Check(!state.TryFireSpear() && state.SpearAmmoRemaining == 0, "other levels cannot spend spear rounds");
            state.StartLevel(5); Check(state.SpearAmmoRemaining == 20 && !state.CanFireSpear && !state.TryFireSpear(), "unowned gun cannot fire");
            Check(state.AcquireTool() && !state.HitShark(), "unspent hit rejected");
            for (int shot = 1; shot <= 20; shot++)
            {
                Check(state.TryFireSpear() && state.Current.spearShotsFired == shot && state.SpearAmmoRemaining == 20 - shot, "one transaction consumes one durable round");
                Check(!state.AcquireTool() && state.Current.spearShotsFired == shot, "acquire/show never replenishes ammunition");
                Valid(state);
            }
            Check(!state.CanFireSpear && !state.TryFireSpear() && state.Current.spearShotsFired == 20, "twenty is a strict shared cap");
            Check(state.CanCollect && !state.Failed, "last projectile may resolve after ammunition reaches zero");
            var restored = new OceanCampaignState(state.Save);
            Check(restored.SpearAmmoRemaining == 0 && restored.Current.toolOwned, "deep copy retains ownership and empty ammunition");
            state.RestartLevel(); Check(state.SpearAmmoRemaining == 20 && !state.Current.toolOwned, "explicit restart begins a new budget and acquisition");
            state.Current.spearShotsFired = 1; Check(!OceanCampaignState.Validate(state.Save, out _), "spent ammunition without ownership rejected");
            state.Current.toolOwned = true; state.Current.spearShotsFired = 21;
            Check(!OceanCampaignState.Validate(state.Save, out _), "overspent save rejected");
            state.Current.spearShotsFired = 0; state.Current.sharkHits = 1;
            Check(!OceanCampaignState.Validate(state.Save, out _), "new schema cannot have more hits than spent rounds");
        }
        static void LegacySpearMigration()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ocean-legacy-spear-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string main = Path.Combine(dir, OceanSaveStore.FileName), backup = Path.Combine(dir, OceanSaveStore.BackupFileName);
                foreach (int hits in new[] { 0, 4, 10 })
                {
                    foreach (bool caught in new[] { false, true })
                    {
                        if (caught && hits < 10) continue;
                        string old = LegacyJson(hits, caught); File.WriteAllText(main, old);
                        if (File.Exists(backup)) File.Delete(backup);
                        Check(OceanSaveStore.TryLoad(dir, out OceanSaveData data, out OceanSaveLoadStatus status, out string error) && status == OceanSaveLoadStatus.Loaded, "authentic old schema one loads: " + error);
                        Check(data.currentRun.sharkHits == hits && data.currentRun.spearShotsFired == hits && data.currentRun.sharkCaught == caught, "legacy minimum known spend preserves all hits/capture");
                        Check(data.bestStars[4] == (caught ? 3 : 0) && data.currentRun.coinCollected, "legacy stars/coin preserved");
                        Check(File.ReadAllText(main) == old && !File.Exists(backup), "migration load does not rewrite evidence");
                        var state = new OceanCampaignState(data); Valid(state);
                        Check(state.SpearAmmoRemaining == 20 - hits && (caught || state.CanFireSpear), "migrated remaining budget is deterministic");
                        Check(OceanSaveStore.TrySave(dir, data, out error), "explicit save commits new field: " + error);
                        Check(File.ReadAllText(main).Contains("\"spearShotsFired\":" + hits), "new field present on new generation");
                        Check(File.ReadAllText(backup) == old, "legacy recovery generation retained");
                        Check(OceanSaveStore.TryLoad(dir, out OceanSaveData again, out error), "new generation reloads"); Equal(data, again);
                        File.WriteAllText(main, "corrupt");
                        Check(OceanSaveStore.TryLoad(dir, out again, out status, out error) && status == OceanSaveLoadStatus.RecoveredBackup && again.currentRun.spearShotsFired == hits, "legacy backup also migrates after corrupt new primary");
                        File.WriteAllText(main, old.Replace("\"sharkHits\":" + hits, "\"sharkHits\":" + (hits == 0 ? 1 : 0)));
                        File.Delete(backup);
                        Check(!OceanSaveStore.TryLoad(dir, out again, out status, out error), "modified legacy data cannot borrow original checksum");
                    }
                }
                foreach (int shots in new[] { 4, 13, 20 })
                {
                    string old = LegacyJson(4, false, shots); File.WriteAllText(main, old);
                    if (File.Exists(backup)) File.Delete(backup);
                    Check(OceanSaveStore.TryLoad(dir, out OceanSaveData data, out OceanSaveLoadStatus status, out string error), "intermediate ammunition schema loads: " + error);
                    Check(data.currentRun.spearShotsFired == shots && !data.currentRun.ammoExhausted && !data.currentRun.failed, "intermediate spend preserved without invented failure");
                    Check(File.ReadAllText(main) == old, "intermediate migration is read-only");
                    var restored = new OceanCampaignState(data);
                    Check(restored.FinalizeSpearAmmo(false) == (shots == 20), "explicit restore settlement respects saved zero ammunition");
                    Check(OceanSaveStore.TrySave(dir, restored.Save, out error), "intermediate schema upgrades explicitly: " + error);
                    Check(OceanSaveStore.TryLoad(dir, out OceanSaveData again, out error), "upgraded failure reason reloads"); Equal(restored.Save, again);
                }
            }
            finally { Directory.Delete(dir, true); }
        }
        static string LegacyJson(int hits, bool caught, int? shots = null)
        {
            // Independent fixture in the exact pre-ammunition canonical format, signed without Store internals.
            string yes = caught ? "true" : "false";
            string payload = "{\"version\":1,\"bestStars\":[0,0,0,0," + (caught ? 3 : 0) + "],\"shellMask\":0,\"fishMask\":0,\"shark\":" + yes +
                ",\"currentRun\":{\"level\":5,\"remainingSeconds\":0,\"shellMask\":0,\"fishMask\":0,\"coinCollected\":true,\"toolOwned\":true,\"sharkHits\":" + hits +
                ",\"sharkCaught\":" + yes + (shots.HasValue ? ",\"spearShotsFired\":" + shots.Value : "") +
                ",\"consumedPickupIds\":[900],\"completed\":" + yes + ",\"failed\":false,\"timedOut\":false}}";
            string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", "").ToLowerInvariant();
            return payload.Substring(0, payload.Length - 1) + ",\"sha256\":\"" + hash + "\"}\n";
        }
        sealed class ScoreSink : IOceanCampaignScoreSink
        {
            public readonly System.Collections.Generic.HashSet<string> events = new System.Collections.Generic.HashSet<string>();
            public int calls, refuseCall = -1, score;
            bool Store(string key, int points = 0)
            { if (++calls == refuseCall) return false; if (events.Add(key)) score += points; return true; }
            public bool EnsureSession() => Store("demo");
            public bool BeginRun(string id, int level) => Store("run:" + id);
            public bool RecordShell(string id, int species, int pickup) => Store(id + ":shell:" + pickup, 1);
            public bool RecordFish(string id, int species, int pickup) => Store(id + ":fish:" + species, 2);
            public bool RecordCoin(string id, int pickup) => Store(id + ":coin:" + pickup, 10);
            public bool RecordShark(string id, int pickup) => Store(id + ":shark", 2);
        }
        static void LeaderboardMigrationAndRecovery()
        {
            var state = new OceanCampaignState(); state.StartLevel(3); state.CollectFish(0); state.CollectCoin(20000);
            // Represents an authentic pre-leaderboard save: existing collectibles are preserved without inventing lifetime totals.
            state.Current.leaderboardRunId = "";
            state = new OceanCampaignState(state.Save);
            string id = state.Current.leaderboardRunId;
            Check(id.Length == 32 && state.Current.leaderboardFishBaseline == 1 && state.Current.leaderboardCoinBaseline, "migration excludes historical discoveries");
            var sink = new ScoreSink(); var sync = new OceanCampaignScoreSync();
            Check(sync.TryPersist(state.Current, sink) && sink.score == 0, "migration creates session/run without retrospective points");
            int calls = sink.calls;
            Check(sync.TryPersist(state.Current, sink) && sink.calls == calls, "unchanged frame does no outbox work");
            state.CollectFish(1); Check(sync.TryPersist(state.Current, sink) && sink.score == 2, "new post-migration fish counted");
            var reload = new OceanCampaignState(state.Save);
            Check(reload.Current.leaderboardRunId == id && reload.Current.leaderboardFishBaseline == 1, "reload retains run and migration baseline");
            sync = new OceanCampaignScoreSync();
            Check(sync.TryPersist(reload.Current, sink) && sink.score == 2, "crash after enqueue replays stable IDs without double counting");
            reload.CollectFish(2); sync = new OceanCampaignScoreSync(); sink.refuseCall = sink.calls + 4;
            Check(!sync.TryPersist(reload.Current, sink) && sink.score == 2, "local write failure does not mark run fully queued");
            sink.refuseCall = -1;
            Check(sync.TryPersist(reload.Current, sink) && sink.score == 4, "retry recovers missed event after campaign-save/outbox crash");
            reload.RestartLevel(); string nextId = reload.Current.leaderboardRunId;
            Check(nextId != id && reload.Current.leaderboardFishBaseline == 0 && !reload.Current.leaderboardCoinBaseline, "restart creates independent run and no old baseline");
            reload.CollectFish(0); reload.CollectCoin(20000);
            Check(sync.TryPersist(reload.Current, sink) && sink.score == 16, "new run scores same species once and new coin ten");
            reload.StartLevel(5); CompleteTargets(reload);
            Check(sync.TryPersist(reload.Current, sink) && sink.score == 18, "actual shark catch counts one fish");
            reload.Current.leaderboardRunId = "../bad";
            Check(!OceanCampaignState.Validate(reload.Save, out _), "unsafe run ID rejected");
            reload.Current.leaderboardRunId = Guid.NewGuid().ToString("N"); reload.Current.leaderboardFishBaseline = 1;
            Check(!OceanCampaignState.Validate(reload.Save, out _), "baseline cannot invent uncollected fish");
        }
        static void CommittedScoreBoundary()
        {
            string root = Path.Combine(Path.GetTempPath(), "ocean-score-commit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var state = new OceanCampaignState(); state.StartLevel(3);
                string campaign = Path.Combine(root, "campaign"), blocked = Path.Combine(root, "blocked");
                Check(OceanSaveStore.TrySave(campaign, state.Save, out string error), "commit initial durable run");
                var committed = new OceanCampaignState(state.Save).Current;
                File.WriteAllText(blocked, "not a directory"); state.CollectFish(0);
                Check(!OceanSaveStore.TrySave(blocked, state.Save, out error), "real campaign write failure");
                var sink = new ScoreSink(); var sync = new OceanCampaignScoreSync();
                Check(sync.TryPersist(committed, sink) && sink.score == 0, "uncommitted live fish cannot enter score ledger");
                Check(OceanSaveStore.TrySave(campaign, state.Save, out error), "retry commits fish with original stable run");
                committed = new OceanCampaignState(state.Save).Current;
                Check(sync.TryPersist(committed, sink) && sink.score == 2, "score starts only after campaign commit");
                Check(OceanSaveStore.TryLoad(campaign, out OceanSaveData loaded, out error), "committed fish reloads");
                Check(new OceanCampaignScoreSync().TryPersist(loaded.currentRun, sink) && sink.score == 2, "reload replays same durable score once");
            }
            finally { Directory.Delete(root, true); }
        }
        static void TwentyLevelRules()
        {
            var state = new OceanCampaignState();
            Check(state.Save.version == 5 && state.Save.bestStars.Length == 100 && OceanCampaignState.FishSpeciesCount == 10, "new format has one hundred history slots and ten durable fish identities");
            state.StartLevel(20); string runId = state.Current.leaderboardRunId;
            bool threw = false; try { state.StartLevel(101); } catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw && state.Current.leaderboardRunId == runId, "out-of-range level leaves active run untouched");
            Check(state.CoinTargetCount == 5 && state.CoinTargetMask == 31 && state.SharkHitTarget == 14 && state.ShotLimit == 20, "final challenge has five coins, fourteen hits and twenty rounds");
            Check(!state.CollectCoin(0) && !state.CollectCoin(19999) && !state.CollectCoin(20005), "only bounded canonical coin IDs accepted");
            Check(!state.CollectShell(20000, 0) && !state.IsConsumed(20000), "shell cannot occupy a reserved coin identity");
            Check(state.CollectCoin(20004) && !state.CollectCoin(20004) && state.CoinCount == 1 && state.Stars == 0 && !state.Current.coinCollected, "coins are independent unordered slots and never complete main targets");
            var sink = new ScoreSink(); var sync = new OceanCampaignScoreSync();
            Check(sync.TryPersist(state.Current, sink) && sink.score == 10, "partial coin mask is durably scoreable before collecting every coin");
            state.CollectCoin(20002);
            Check(new OceanCampaignScoreSync().TryPersist(state.Current, sink) && sink.score == 20, "restart replays prior slot once and credits another slot");
            CompleteTargets(state);
            Check(state.Stars == 2 && !state.Current.coinCollected, "full mixed boss target with partial coins earns two stars");
            foreach (int slot in new[] { 3, 0, 1 }) Check(state.CollectCoin(20000 + slot), "remaining coins can be collected after boss success");
            Check(state.Stars == 3 && state.CoinCount == 5 && state.Current.coinCollected && state.Save.bestStars[19] == 3, "only every final-level coin grants third star");
            Check(sync.TryPersist(state.Current, sink) && sink.score == 82, "ten shells, ten fish plus one shark and five distinct coins survive outbox replay"); Valid(state);
            state.StartLevel(8);
            Check(state.Definition.OrderedFish && state.NextFishSpecies == 0, "ordered fish exposes first outstanding target");
            Check(!state.CollectFish(4) && state.Current.fishMask == 0 && state.Save.fishMask == 1023, "wrong-order fish does not consume current progress or prior global collection");
            foreach (int species in new[] { 0, 1, 2, 4 })
            { Check(state.NextFishSpecies == species && state.CollectFish(species), "ordered fish accepts exactly the next target"); Check(!state.CollectFish(species), "ordered fish cannot replay"); }
            Check(state.NextFishSpecies == -1 && !state.GoalComplete, "finishing ordered fish still requires level shells"); Valid(state);
            state.StartLevel(14);
            Check(state.Definition.OrderedShell && state.NextShellSpecies == 0 && !state.CollectShell(85, 2) && !state.IsConsumed(85), "wrong-order shell stays collectible");
            Check(state.CollectShell(85, 0) && state.CollectShell(86, 0) && state.IsConsumed(86) && !state.CollectShell(86, 0) && state.Current.leaderboardShells.Count == 2 && state.NextShellSpecies == 2, "ordered species advances once; a different same-species physical shell scores once");
            Valid(state);
            state.Current.shellMask |= 1 << 4;
            Check(!OceanCampaignState.Validate(state.Save, out string orderError) && orderError.Contains("Ordered"), "save cannot skip an ordered target");
            foreach (int level in new[] { 5, 10, 15, 20 })
            {
                state.StartLevel(level); state.AcquireTool();
                for (int shot = 0; shot < state.ShotLimit; shot++) { state.TryFireSpear(); if (shot < state.SharkHitTarget - 1) state.HitShark(); }
                Check(!state.FinalizeSpearAmmo(true) && !state.Failed, "last in-flight round delays every boss failure");
                Check(state.HitShark() && !state.FinalizeSpearAmmo(false) && state.CatchShark(), "last-round target hit preserves capture at every boss threshold");
                CompleteTargets(state); Check(state.GoalComplete && !state.Failed, "mixed boss targets remain completable with zero ammo"); Valid(state);
                state.RestartLevel(); state.AcquireTool();
                for (int shot = 0; shot < state.ShotLimit; shot++) state.TryFireSpear();
                Check(state.FinalizeSpearAmmo(false) && state.AmmoExhausted, "missed twenty-round budget fails every boss"); Valid(state);
            }
        }
        static string SignedFixture(string payload)
        {
            string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", "").ToLowerInvariant();
            return payload.Substring(0, payload.Length - 1) + ",\"sha256\":\"" + hash + "\"}\n";
        }
        static void VersionTwoMigration()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ocean-v2-migration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            string main = Path.Combine(dir, OceanSaveStore.FileName), backup = Path.Combine(dir, OceanSaveStore.BackupFileName);
            const string identity = "1234567890abcdef1234567890abcdef";
            try
            {
                for (int schema = 0; schema < 3; schema++)
                {
                    string payload = "{\"version\":1,\"bestStars\":[2,3,3,2,0],\"shellMask\":1023,\"fishMask\":15,\"shark\":false,\"currentRun\":{" +
                        "\"level\":3,\"remainingSeconds\":100,\"shellMask\":3,\"fishMask\":7,\"coinCollected\":true,\"toolOwned\":false,\"sharkHits\":0,\"sharkCaught\":false,\"spearShotsFired\":0," +
                        "\"consumedPickupIds\":[0,1,6],\"completed\":true,\"failed\":false,\"timedOut\":false,\"ammoExhausted\":false" +
                        (schema == 0 ? "" : ",\"leaderboardRunId\":\"" + identity + "\",\"leaderboardFishBaseline\":1,\"leaderboardCoinBaseline\":" + (schema == 2 ? "true" : "false") + ",\"leaderboardSharkBaseline\":false") + "}}";
                    string old = SignedFixture(payload); File.WriteAllText(main, old); if (File.Exists(backup)) File.Delete(backup);
                    Check(OceanSaveStore.TryLoad(dir, out OceanSaveData data, out string error), "authenticated latest v1 schema loads: " + error);
                    Check(data.version == 5 && data.bestStars.Length == 100 && data.bestStars[0] == 2 && data.bestStars[2] == 3 && data.bestStars[19] == 0, "migration extends history without inventing new-level stars");
                    Check(data.currentRun.coinMask == 1 && data.currentRun.coinCollected && data.currentRun.consumedPickupIds.Count == 4 && data.currentRun.consumedPickupIds.Contains(6) && data.currentRun.consumedPickupIds.Contains(20000), "real old entity coin ID and canonical alias coexist");
                    Check(data.currentRun.leaderboardCoinMaskBaseline == (schema == 2 ? 1 : 0), "old leaderboard exclusion maps exactly to slot zero");
                    Check(File.ReadAllText(main) == old && !File.Exists(backup), "upgrade load is read-only");
                    var state = new OceanCampaignState(data); var sink = new ScoreSink();
                    Check(new OceanCampaignScoreSync().TryPersist(state.Current, sink) && sink.score == (schema == 0 ? 0 : schema == 1 ? 14 : 4), "pre-leaderboard progress stays baseline, existing score-enabled progress remains recoverable");
                    Check(schema == 0 || state.Current.leaderboardRunId == identity, "migration preserves existing scoring identity");
                    Check(!state.CollectCoin(20000) && state.Stars == 3, "migrated old coin cannot respawn as new slot zero");
                    Check(OceanSaveStore.TrySave(dir, state.Save, out error) && File.ReadAllText(backup) == old, "explicit v2 commit preserves authenticated legacy backup");
                    Check(OceanSaveStore.TryLoad(dir, out data, out error), "new v2 generation reloads"); Equal(state.Save, data);
                    File.WriteAllText(main, old.Replace("\"coinCollected\":true", "\"coinCollected\":false")); File.Delete(backup);
                    Check(!OceanSaveStore.TryLoad(dir, out data, out error), "legacy mutation cannot pass original checksum then receive migration");
                }
                string idle = SignedFixture("{\"version\":1,\"bestStars\":[3,2,0,0,0],\"shellMask\":31,\"fishMask\":0,\"shark\":false,\"currentRun\":null}");
                File.WriteAllText(main, idle);
                Check(OceanSaveStore.TryLoad(dir, out OceanSaveData menu, out _) && menu.version == 5 && menu.currentRun == null && menu.bestStars[0] == 3 && menu.bestStars.Length == 100, "menu-only v1 history migrates without starting a run");
                var partial = new OceanCampaignState(menu); partial.StartLevel(20); partial.CollectCoin(20004); partial.CollectCoin(20002);
                Check(OceanSaveStore.TrySave(dir, partial.Save, out string saveError) && OceanSaveStore.TryLoad(dir, out menu, out saveError), "sparse v2 final-level coin mask persists: " + saveError);
                Check(menu.currentRun.coinMask == 20 && !menu.currentRun.coinCollected && menu.currentRun.consumedPickupIds.Contains(20004), "sparse coin slots roundtrip without collapsing to a boolean");
                Equal(partial.Save, menu);
            }
            finally { Directory.Delete(dir, true); }
        }
        static OceanCampaignState ExhaustedRun(int hits)
        {
            var state = new OceanCampaignState(); state.StartLevel(5); state.AcquireTool();
            for (int shot = 1; shot <= 20; shot++)
            { Check(state.TryFireSpear(), "exhaustion fixture spends real round"); if (shot <= hits) Check(state.HitShark(), "exhaustion fixture accepts real hit"); }
            return state;
        }
        static void IndependentSharks()
        {
            var state = new OceanCampaignState(); state.StartLevel(5);
            Check(state.Current.sharkProgress.Count == 1 && state.Current.sharkProgress[0].id == 0, "new boss run has the stable historical shark identity");
            Check(!state.HitShark(1) && !state.CatchShark(1), "every shark requires the acquired gun");
            Check(state.AcquireTool(), "independent shark fixture acquires gun");
            foreach (int invalidId in new[] { -1, 32, int.MinValue, int.MaxValue })
                Check(!state.HitShark(invalidId) && !state.CatchShark(invalidId) && state.SharkHitsFor(invalidId) == 0 && !state.SharkCaughtFor(invalidId), "out-of-range identity cannot mutate progress");
            Check(!state.HitShark(1) && state.Current.sharkProgress.Count == 1, "no projectile budget cannot allocate a target");
            for (int i = 0; i < 5; i++)
            {
                Check(state.TryFireSpear() && state.HitShark(1), "first shark accepts its own projectile");
                Check(!state.HitShark(2), "same spent round cannot be counted again on another shark");
                Check(state.TryFireSpear() && state.HitShark(2), "second shark accepts a separate projectile");
            }
            Check(state.SharkHitsFor(0) == 0 && state.SharkHitsFor(1) == 5 && state.SharkHitsFor(2) == 5 && state.Current.sharkHits == 5, "aggregate is best individual count, never ten combined hits");
            Check(!state.CatchShark(0) && !state.CatchShark(1) && !state.CatchShark(2) && !state.GoalComplete, "different sharks cannot borrow each other's damage to become catchable"); Valid(state);
            var copy = new OceanCampaignState(state.Save);
            copy.Current.sharkProgress[1].hits--;
            Check(state.SharkHitsFor(1) == 5 && copy.SharkHitsFor(1) == 4, "per-shark records are deep copied");
            for (int i = 0; i < 4; i++) Check(state.TryFireSpear() && state.HitShark(2), "second shark builds its independent progress");
            for (int i = 0; i < 5; i++) Check(state.TryFireSpear(), "missed rounds still consume the common gun budget");
            Check(state.SpearAmmoRemaining == 1 && state.Current.sharkHits == 9, "only one cartridge remains before final independent target hit");
            Check(state.TryFireSpear() && !state.FinalizeSpearAmmo(true) && state.HitShark(2), "twentieth projectile can finish a nonzero shark identity");
            Check(state.SpearAmmoRemaining == 0 && !state.FinalizeSpearAmmo(false) && !state.Failed, "one individually ready shark preserves the zero-ammo capture opportunity");
            Check(!state.CatchShark(1) && state.CatchShark(2) && state.SharkCaughtFor(2) && !state.SharkCaughtFor(1) && !state.SharkCaughtFor(0), "only the independently completed shark can be captured");
            Check(state.GoalComplete && state.Stars == 2 && state.Save.shark && !state.TryFireSpear(), "any qualifying captured shark satisfies the existing goal and score summary"); Valid(state);
            Check(state.CollectCoin(20000) && state.Stars == 3, "independent shark success still supports the hidden coin star");
            state.RestartLevel();
            Check(state.SharkHitsFor(2) == 0 && !state.SharkCaughtFor(2) && state.Current.sharkProgress.Count == 1 && state.SpearAmmoRemaining == 20 && state.Save.bestStars[4] == 3, "restart clears all individual progress while preserving history");
            state.AcquireTool();
            for (int i = 0; i < 20; i++) { Check(state.TryFireSpear(), "split failure fixture spends a real round"); if (i < 18) Check(state.HitShark(i % 2 + 1), "split failure fixture hits independent targets"); }
            Check(state.Current.sharkHits == 9 && state.FinalizeSpearAmmo(false) && state.AmmoExhausted, "eighteen total hits split nine each still fail without an individually ready shark"); Valid(state);
            state.RestartLevel(); state.AcquireTool();
            for (int i = 0; i < 10; i++) Check(state.TryFireSpear() && state.HitShark(31), "highest stable shark identity accepts its own hits");
            Check(state.CatchShark(31) && state.GoalComplete, "highest stable shark identity remains independently catchable"); Valid(state);
            var pristine = new OceanCampaignState(); pristine.StartLevel(5); pristine.AcquireTool(); pristine.TryFireSpear(); pristine.HitShark(1);
            foreach (int mutation in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 })
            {
                var bad = new OceanCampaignState(pristine.Save);
                switch (mutation)
                {
                    case 0: bad.Current.sharkProgress.Add(new OceanSharkProgress(1)); break;
                    case 1: bad.Current.sharkProgress.Add(null); break;
                    case 2: bad.Current.sharkProgress.Add(new OceanSharkProgress(32)); break;
                    case 3: bad.Current.sharkProgress[1].hits = -1; break;
                    case 4: bad.Current.sharkProgress[1].hits = 11; break;
                    case 5: bad.Current.sharkProgress.Add(new OceanSharkProgress(2, 1)); break;
                    case 6: bad.Current.sharkHits = 0; break;
                    case 7: bad.Current.sharkCaught = true; bad.Save.shark = true; break;
                    case 8: bad.Current.sharkProgress[1].caught = true; break;
                }
                Check(!OceanCampaignState.Validate(bad.Save, out _), "invalid individual list/summary/ammunition state is rejected: " + mutation);
            }
            pristine.StartLevel(1); pristine.Current.sharkProgress.Add(new OceanSharkProgress(0));
            Check(!OceanCampaignState.Validate(pristine.Save, out _), "non-boss levels cannot claim even empty individual shark identities");
        }
        static string LegacyThreeSharkPayload(int hits, bool caught, int shots, string individualJson = null)
        {
            string yes = caught ? "true" : "false";
            return "{\"version\":3,\"bestStars\":[0,0,0,0," + (caught ? 2 : 0) + ",0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],\"shellMask\":0,\"fishMask\":0,\"shark\":" + yes +
                ",\"currentRun\":{\"level\":5,\"remainingSeconds\":0,\"shellMask\":0,\"fishMask\":0,\"coinCollected\":false,\"toolOwned\":true,\"sharkHits\":" + hits +
                ",\"sharkCaught\":" + yes + ",\"spearShotsFired\":" + shots + ",\"consumedPickupIds\":[],\"completed\":" + yes + ",\"failed\":false,\"timedOut\":false,\"ammoExhausted\":false" +
                ",\"leaderboardRunId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"leaderboardFishBaseline\":0,\"leaderboardCoinBaseline\":false,\"leaderboardSharkBaseline\":false,\"coinMask\":0,\"leaderboardCoinMaskBaseline\":0,\"leaderboardShells\":[]" +
                (individualJson == null ? "" : ",\"sharkProgress\":" + individualJson) + "}}";
        }
        static void IndependentSharkPersistence()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ocean-independent-sharks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            string main = Path.Combine(directory, OceanSaveStore.FileName), backup = Path.Combine(directory, OceanSaveStore.BackupFileName);
            try
            {
                foreach (int hits in new[] { 0, 4, 10 }) foreach (bool caught in new[] { false, true })
                {
                    if (caught && hits != 10) continue;
                    foreach (string legacyList in new[] { null, "[]" })
                    {
                        string old = SignedFixture(LegacyThreeSharkPayload(hits, caught, hits + 2, legacyList)); File.WriteAllText(main, old); if (File.Exists(backup)) File.Delete(backup);
                        Check(OceanSaveStore.TryLoad(directory, out OceanSaveData data, out string error), "authenticated schema-three legacy shark loads: " + error);
                        Check(data.currentRun.sharkProgress.Count == 1 && data.currentRun.sharkProgress[0].id == 0 && data.currentRun.sharkProgress[0].hits == hits && data.currentRun.sharkProgress[0].caught == caught, "only historical identity zero inherits old progress");
                        Check(data.currentRun.spearShotsFired == hits + 2 && data.currentRun.leaderboardRunId == "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" && data.bestStars[4] == (caught ? 2 : 0), "migration preserves ammunition, scoring identity and star history");
                        Check(File.ReadAllText(main) == old && !File.Exists(backup), "legacy load authenticates and migrates without rewriting the source");
                        Check(OceanSaveStore.TrySave(directory, data, out error), "explicit commit persists individual legacy migration: " + error);
                        Check(File.ReadAllText(main).Contains("\"sharkProgress\":[{\"id\":0,\"hits\":" + hits) && File.ReadAllText(backup) == old, "new canonical generation includes individuals and preserves legacy backup");
                        Check(OceanSaveStore.TryLoad(directory, out OceanSaveData again, out error), "new canonical independent generation reloads"); Equal(data, again);
                    }
                }
                var state = new OceanCampaignState(); state.StartLevel(5); state.AcquireTool();
                for (int i = 0; i < 4; i++) { state.TryFireSpear(); state.HitShark(2); state.TryFireSpear(); state.HitShark(17); }
                Check(OceanSaveStore.TrySave(directory, state.Save, out string writeError), "actual two-shark save commits: " + writeError);
                Check(OceanSaveStore.TryLoad(directory, out OceanSaveData restored, out writeError), "actual two-shark save reloads"); Equal(state.Save, restored);
                var resumed = new OceanCampaignState(restored);
                Check(resumed.SharkHitsFor(2) == 4 && resumed.SharkHitsFor(17) == 4 && resumed.Current.sharkHits == 4 && !resumed.CatchShark(2), "restart cannot collapse spread damage into a capture-ready shark");
                for (int i = 0; i < 6; i++) { resumed.TryFireSpear(); resumed.HitShark(17); }
                Check(resumed.CatchShark(17) && OceanSaveStore.TrySave(directory, resumed.Save, out writeError) && OceanSaveStore.TryLoad(directory, out restored, out writeError), "individually earned capture survives a full filesystem roundtrip"); Equal(resumed.Save, restored);
                foreach (string malformed in new[] {
                    "[{\"id\":1,\"hits\":3,\"caught\":false}]",
                    "[{\"id\":1,\"hits\":4,\"caught\":false},{\"id\":2,\"hits\":4,\"caught\":false}]",
                    "[{\"id\":1,\"hits\":4,\"caught\":false},{\"id\":1,\"hits\":0,\"caught\":false}]",
                    "[{\"id\":32,\"hits\":4,\"caught\":false}]", "[null]", "null",
                    "[{\"id\":1,\"hits\":4,\"caught\":true}]", "[{\"hits\":4,\"caught\":false}]" })
                {
                    string invalid = SignedFixture(LegacyThreeSharkPayload(4, false, 4, malformed)); File.WriteAllText(main, invalid); File.Delete(backup);
                    Check(!OceanSaveStore.TryLoad(directory, out _, out _), "correctly signed but inconsistent individual JSON is rejected");
                    Check(!OceanSaveStore.TrySave(directory, resumed.Save, out _) && File.ReadAllText(main) == invalid, "invalid source evidence cannot be overwritten");
                }
            }
            finally { Directory.Delete(directory, true); }
        }
        static void AmmunitionFinalization()
        {
            var empty = new OceanCampaignState();
            Check(!empty.FinalizeSpearAmmo(false) && empty.FailureReason == "", "no active run cannot finalize");
            empty.StartLevel(1); Check(!empty.FinalizeSpearAmmo(false), "other levels ignore ammunition settlement");
            empty.Tick(1800, true); Check(empty.FailureReason == "timeExpired" && !empty.AmmoExhausted, "timer failure has distinct UI reason");
            empty.StartLevel(5); empty.AcquireTool(); empty.TryFireSpear();
            Check(!empty.FinalizeSpearAmmo(false) && !empty.Failed, "remaining rounds cannot cause ammo failure");
            foreach (int hits in new[] { 0, 9 })
            {
                var state = ExhaustedRun(hits);
                Check(!state.FinalizeSpearAmmo(true) && !state.Failed && state.CanCollect, "in-flight last shots never finalize early");
                Check(state.FinalizeSpearAmmo(false) && state.Failed && state.AmmoExhausted && !state.TimedOut, "settled twenty-round run below ten hits fails with ammunition reason");
                Check(state.FailureReason == "ammoExhausted" && state.Current.sharkHits == hits && state.Stars == 0, "failure reason and accepted hits retained");
                Check(!state.HitShark() && !state.CatchShark() && !state.CollectCoin(20000) && !state.TryFireSpear(), "resolved failure blocks later transactions");
                Check(!state.FinalizeSpearAmmo(false), "failure notification is one-shot");
                Valid(state);
                var restored = new OceanCampaignState(state.Save);
                Check(restored.AmmoExhausted && restored.FailureReason == "ammoExhausted" && restored.Current.sharkHits == hits, "copy retains exact failure cause");
                state.RestartLevel(); Check(!state.Failed && !state.AmmoExhausted && state.FailureReason == "" && state.SpearAmmoRemaining == 20, "explicit restart clears cause and budget");
            }
            var forgiving = ExhaustedRun(9);
            Check(!forgiving.FinalizeSpearAmmo(true) && forgiving.HitShark() && forgiving.Current.sharkHits == 10, "twentieth in-flight hit commits before finalization");
            Check(!forgiving.FinalizeSpearAmmo(false) && forgiving.CanCollect && !forgiving.Failed, "ten-hit zero-round shark remains catchable");
            Check(forgiving.CatchShark() && forgiving.Stars == 2 && !forgiving.FinalizeSpearAmmo(false), "earned two-star success cannot be overwritten by settlement");
            Check(forgiving.CollectCoin(20000) && forgiving.Stars == 3 && !forgiving.FinalizeSpearAmmo(false), "exhausted successful run can still find coin"); Valid(forgiving);
            var miss = ExhaustedRun(9);
            Check(miss.FinalizeSpearAmmo(false) && miss.Current.sharkHits == 9, "last-round miss below ten hits fails"); Valid(miss);
            var catchBeforeSettle = ExhaustedRun(10);
            Check(!catchBeforeSettle.FinalizeSpearAmmo(false) && !catchBeforeSettle.FinalizeSpearAmmo(false), "repeated settlement preserves ten-hit capture opportunity");
            Check(catchBeforeSettle.CatchShark() && !catchBeforeSettle.FinalizeSpearAmmo(false) && catchBeforeSettle.Stars == 2, "actual capture committed before settlement stays successful");
            var invalid = new OceanCampaignState(catchBeforeSettle.Save); invalid.Current.ammoExhausted = true; invalid.Current.failed = true;
            Check(!OceanCampaignState.Validate(invalid.Save, out _), "save cannot claim both success and ammo failure");
            var ready = ExhaustedRun(10); ready.Current.ammoExhausted = true; ready.Current.failed = true;
            Check(!OceanCampaignState.Validate(ready.Save, out _), "ten-hit uncaptured save cannot claim ammunition failure");
            var partial = ExhaustedRun(0); partial.Current.spearShotsFired = 19; partial.Current.ammoExhausted = true; partial.Current.failed = true;
            Check(!OceanCampaignState.Validate(partial.Save, out _), "save cannot claim ammo failure with unspent rounds");
            string dir = Path.Combine(Path.GetTempPath(), "ocean-ammo-results-" + Guid.NewGuid().ToString("N"));
            try
            {
                var pending = ExhaustedRun(9);
                Check(OceanSaveStore.TrySave(dir, pending.Save, out string error), "zero-round pending save: " + error);
                Check(OceanSaveStore.TryLoad(dir, out OceanSaveData data, out error), "zero-round pending load"); Equal(pending.Save, data);
                var restored = new OceanCampaignState(data);
                Check(!restored.Failed && restored.FinalizeSpearAmmo(false), "caller explicitly settles restored zero-round state without recoverable projectiles");
                Check(OceanSaveStore.TrySave(dir, restored.Save, out error), "ammo failure save: " + error);
                Check(OceanSaveStore.TryLoad(dir, out data, out error), "ammo failure load"); Equal(restored.Save, data);
                Check(data.currentRun.ammoExhausted && data.currentRun.sharkHits == 9, "persisted failure retains accepted progress");
                Check(OceanSaveStore.TrySave(dir, forgiving.Save, out error), "successful exhausted run save: " + error);
                Check(OceanSaveStore.TryLoad(dir, out data, out error), "successful exhausted run load"); Equal(forgiving.Save, data);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
        static void Persistence()
        {
            string parent = Path.Combine(Path.GetTempPath(), "ocean-campaign-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            try
            {
                string dir = Path.Combine(parent, "main");
                Check(OceanSaveStore.TryLoad(dir, out OceanSaveData empty, out OceanSaveLoadStatus status, out string error) && status == OceanSaveLoadStatus.NewSave && empty.currentRun == null, "missing store produces explicit new save");
                Check(!Directory.Exists(dir), "load does not create or overwrite files");
                var state = new OceanCampaignState(); state.StartLevel(3); state.CollectShell(18, 0); state.CollectFish(2); state.Tick(17.125f, true);
                Check(OceanSaveStore.TrySave(dir, state.Save, out error), "initial save: " + error);
                string main = Path.Combine(dir, OceanSaveStore.FileName), backup = Path.Combine(dir, OceanSaveStore.BackupFileName);
                Check(File.Exists(main) && File.Exists(backup), "first commit creates verified recovery copy");
                Check(OceanSaveStore.TryLoad(dir, out OceanSaveData data, out status, out error) && status == OceanSaveLoadStatus.Loaded, "load valid primary");
                Equal(state.Save, data);
                var culture = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                    state.Tick(.33333334f, true); Check(OceanSaveStore.TrySave(dir, state.Save, out error), "non-English culture saves numeric JSON");
                    Check(OceanSaveStore.TryLoad(dir, out data, out error), "non-English culture roundtrip"); Equal(state.Save, data);
                }
                finally { CultureInfo.CurrentCulture = culture; }
                string lastGood = File.ReadAllText(main), priorGood = File.ReadAllText(backup);
                Check(lastGood != priorGood, "backup is previous generation");
                File.WriteAllText(main, "{truncated");
                Check(OceanSaveStore.TryLoad(dir, out data, out status, out error) && status == OceanSaveLoadStatus.RecoveredBackup && !string.IsNullOrEmpty(error), "corrupt primary explicitly falls back");
                Check(data.currentRun.remainingSeconds == 1182.875f && data.currentRun.fishMask == 4, "fallback data is verified prior generation");
                Check(File.ReadAllText(main) == "{truncated", "load never mutates corrupted evidence");
                state = new OceanCampaignState(data); state.CollectCoin(20000);
                Check(OceanSaveStore.TrySave(dir, state.Save, out error), "save after backup recovery: " + error);
                Check(File.ReadAllText(backup) == priorGood, "corrupt primary never poisons valid backup");
                Check(OceanSaveStore.TryLoad(dir, out data, out error), "recovered save becomes readable primary"); Equal(state.Save, data);
                File.WriteAllText(main, File.ReadAllText(main).Replace("\"coinCollected\":true", "\"coinCollected\":false"));
                Check(OceanSaveStore.TryLoad(dir, out data, out status, out error) && status == OceanSaveLoadStatus.RecoveredBackup, "checksum catches syntactically valid data corruption");
                File.WriteAllText(main, "{\"version\":6,\"future\":true}"); string foreign = File.ReadAllText(main);
                Check(!OceanSaveStore.TryLoad(dir, out data, out status, out error) && status == OceanSaveLoadStatus.UnsupportedVersion, "unknown primary schema is not silently rolled back");
                Check(!OceanSaveStore.TrySave(dir, state.Save, out error) && File.ReadAllText(main) == foreign && File.ReadAllText(backup) == priorGood, "unknown schema cannot be overwritten");
                File.WriteAllText(main, lastGood.Replace("\"version\":5", "\"version\":5,\"futureField\":true"));
                Check(!OceanSaveStore.TryLoad(dir, out data, out status, out error) && status == OceanSaveLoadStatus.UnsupportedVersion && !OceanSaveStore.TrySave(dir, state.Save, out error), "unknown fields require explicit migration rather than overwrite");
                File.WriteAllText(main, "{\"version\":0}");
                Check(!OceanSaveStore.TryLoad(dir, out data, out status, out error) && status == OceanSaveLoadStatus.UnsupportedVersion, "older schema requires explicit migration");
                File.WriteAllText(main, lastGood); File.WriteAllText(backup, "{\"version\":9}");
                Check(!OceanSaveStore.TrySave(dir, state.Save, out error), "unknown backup also preserved");
                File.WriteAllText(main, "broken"); File.WriteAllText(backup, "also broken");
                Check(!OceanSaveStore.TryLoad(dir, out data, out status, out error) && status == OceanSaveLoadStatus.Corrupt && data == null, "both corrupt reports failure without empty progress");
                Check(!OceanSaveStore.TrySave(dir, new OceanSaveData(), out error) && File.ReadAllText(main) == "broken", "fresh defaults cannot erase unrecoverable history");
                File.WriteAllText(main, lastGood); File.WriteAllText(backup, priorGood);
                string stray = Path.Combine(dir, OceanSaveStore.FileName + ".tmp-interrupted"); File.WriteAllText(stray, "partial write");
                Check(OceanSaveStore.TryLoad(dir, out data, out status, out error) && status == OceanSaveLoadStatus.Loaded, "interrupted temp cannot replace committed primary");
                Check(File.ReadAllText(stray) == "partial write", "foreign temp file remains untouched");
                var bad = new OceanSaveData { version = 12 };
                Check(!OceanSaveStore.TrySave(dir, bad, out error) && File.ReadAllText(main) == lastGood, "invalid outgoing version cannot overwrite save");
                string blocked = Path.Combine(parent, "not-a-directory"); File.WriteAllText(blocked, "keep");
                Check(!OceanSaveStore.TrySave(blocked, state.Save, out error) && !string.IsNullOrEmpty(error), "I/O failure is reported");
                Check(!OceanSaveStore.TryLoad(blocked, out data, out status, out error) && status == OceanSaveLoadStatus.IoError, "blocked directory is not mistaken for an empty save");
                // Every level persists active and resolved state, including exact timeout and unlimited speargun progression.
                for (int level = 1; level <= OceanCampaignState.LevelCount; level++)
                {
                    string levelDir = Path.Combine(parent, "level-" + level); state.StartLevel(level); state.Tick(12.34567f, true);
                    if (state.RequiresShark) { state.AcquireTool(); for (int hit = 0; hit < 4; hit++) { if(state.Current.pendingSpearRecovery)state.TryRecoverSpear(); state.TryFireSpear(); state.HitShark(); } if(state.Current.pendingSpearRecovery)state.TryRecoverSpear(); state.TryFireSpear(); }
                    Check(OceanSaveStore.TrySave(levelDir, state.Save, out error), "level-specific active save");
                    Check(OceanSaveStore.TryLoad(levelDir, out data, out error), "level-specific active load"); Equal(state.Save, data);
                    CompleteTargets(state); state.CollectCoin(20000); state.Tick(4000f, true);
                    Check(OceanSaveStore.TrySave(levelDir, state.Save, out error), "level-specific result save");
                    Check(OceanSaveStore.TryLoad(levelDir, out data, out error), "level-specific result load"); Equal(state.Save, data);
                }
                string malformedDir = Path.Combine(parent, "malformed"); Check(OceanSaveStore.TrySave(malformedDir, state.Save, out error), "malformed fixture baseline");
                string malformedMain = Path.Combine(malformedDir, OceanSaveStore.FileName); string validJson = File.ReadAllText(malformedMain);
                foreach (string malformed in new[] { validJson + "junk", validJson.Replace("\"version\":5", "\"version\":5,\"version\":5"), "{\"version\":01}", "{\"version\":1e999}", "[]" })
                {
                    File.WriteAllText(malformedMain, malformed);
                    Check(OceanSaveStore.TryLoad(malformedDir, out data, out status, out error) && status == OceanSaveLoadStatus.RecoveredBackup, "malformed JSON rejected and valid backup used");
                }
            }
            finally { Directory.Delete(parent, true); }
        }
        static void ShellScoreMigration()
        {
            string dir=Path.Combine(Path.GetTempPath(),"ocean-shell-migration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try
            {
                // Exact v2 canonical fixture: these two already consumed entities have no historical score record.
                string payload="{\"version\":2,\"bestStars\":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],\"shellMask\":1,\"fishMask\":0,\"shark\":false,\"currentRun\":{\"level\":1,\"remainingSeconds\":1800,\"shellMask\":1,\"fishMask\":0,\"coinCollected\":false,\"toolOwned\":false,\"sharkHits\":0,\"sharkCaught\":false,\"spearShotsFired\":0,\"consumedPickupIds\":[0,5],\"completed\":false,\"failed\":false,\"timedOut\":false,\"ammoExhausted\":false,\"leaderboardRunId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"leaderboardFishBaseline\":0,\"leaderboardCoinBaseline\":false,\"leaderboardSharkBaseline\":false,\"coinMask\":0,\"leaderboardCoinMaskBaseline\":0}}";
                string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-","").ToLowerInvariant();
                string path=Path.Combine(dir,OceanSaveStore.FileName);File.WriteAllText(path,payload.Substring(0,payload.Length-1)+",\"sha256\":\""+hash+"\"}");
                Check(OceanSaveStore.TryLoad(dir,out OceanSaveData loaded,out string error),"authentic v2 shell baseline migrates: "+error);
                var state=new OceanCampaignState(loaded);var sink=new ScoreSink();var sync=new OceanCampaignScoreSync();
                Check(state.Save.version==5&&state.Current.leaderboardShells.Count==0&&!state.CollectShell(0,0)&&!state.CollectShell(5,0),"old entities remain consumed without invented score records");
                Check(sync.TryPersist(state.Current,sink)&&sink.score==0,"old shells have zero retroactive points");
                Check(state.CollectShell(10,0)&&state.Current.leaderboardShells.Count==1,"new physical entity of already known species records a point");
                Check(OceanSaveStore.TrySave(dir,state.Save,out error)&&OceanSaveStore.TryLoad(dir,out loaded,out error),"schema3 shell record is durable");
                Check(sync.TryPersist(loaded.currentRun,sink)&&sink.score==1&&new OceanCampaignScoreSync().TryPersist(loaded.currentRun,sink)&&sink.score==1,"crash replay of committed shell remains once");
                var bad=new OceanCampaignState(loaded);bad.Current.leaderboardShells.Add(new OceanShellScore(10,0));Check(!OceanCampaignState.Validate(bad.Save,out _),"duplicate score entity rejected");
                bad=new OceanCampaignState(loaded);bad.Current.leaderboardShells[0].pickupId=20000;Check(!OceanCampaignState.Validate(bad.Save,out _),"coin ID cannot become a shell score");
                bad=new OceanCampaignState(loaded);bad.Current.leaderboardShells[0].species=9;Check(!OceanCampaignState.Validate(bad.Save,out _),"non-target shell score rejected");
                bad=new OceanCampaignState(loaded);bad.Current.leaderboardShells[0].pickupId=11;Check(!OceanCampaignState.Validate(bad.Save,out _),"score must have consumed-entity evidence");
            }
            finally{Directory.Delete(dir,true);}
        }
        static void HundredLevelRules()
        {
            Check(OceanLevelCatalog.Count == 100 && OceanLevelCatalog.ChapterNames.Length == 20, "one hundred selectable levels in twenty named chapters");
            var state = new OceanCampaignState();
            for (int level = 21; level <= 100; level++)
            {
                state.StartLevel(level); var d = state.Definition;
                Check((int)d.Mechanic == (level - 21) / 5 + 1 && d.MechanicTarget == 3 + (level - 21) % 5, "each five-level tier introduces a new mechanic with five progressive targets");
                Check(d.TimeLimitSeconds > 0 && d.TimeLimitSeconds <= 600 && !string.IsNullOrEmpty(d.MechanicHint), "authored extended levels have bounded sessions and hand instructions");
                Check(!state.GoalComplete && !state.MechanicComplete, "empty collection goals cannot bypass required mechanic");
                Check(state.SetMechanicStage(Math.Min(1, OceanCampaignState.MaximumMechanicStage(d.Mechanic))) && state.RecordMechanicMistake(), "bounded intermediate stage and mistakes accepted");
                state.UpdateMechanicElapsed(1.25f); state.UpdateMechanicElapsed(float.NaN); state.UpdateMechanicElapsed(-1);
                Check(state.Current.mechanicElapsedSeconds == 1.25f, "only valid active time advances mechanic clock");
                Check(state.RecordMechanicProgress(100000, 0) && !state.RecordMechanicProgress(100000) && state.Current.mechanicStage == 0, "node replay rejected and successful node resets partial stage");
                int invalidStage = OceanCampaignState.MaximumMechanicStage(d.Mechanic) + 1;
                Check(!state.SetMechanicStage(invalidStage) && !state.RecordMechanicProgress(999998, invalidStage) && state.Current.mechanicProgress == 1,
                    "impossible mechanic phase rejected before mutating progress");
                var badPhase = new OceanCampaignState(state.Save); badPhase.Current.mechanicStage = invalidStage;
                Check(!OceanCampaignState.Validate(badPhase.Save, out _), "a checksum-valid but impossible per-mechanic phase is rejected");
                badPhase.Current.mechanicStage = 64;
                Check(!OceanCampaignState.Validate(badPhase.Save, out _), "legacy broad phase bound cannot skip a rune sequence");
                var restored = new OceanCampaignState(state.Save);
                Check(!restored.RecordMechanicProgress(100000) && restored.Current.mechanicProgress == 1, "durable identities survive an immutable restart snapshot");
                restored.Current.mechanicEventIds.Add(100000);
                Check(!OceanCampaignState.Validate(restored.Save, out _), "duplicate mechanic identity rejected");
                restored = new OceanCampaignState(state.Save); restored.Current.mechanicProgress++;
                Check(!OceanCampaignState.Validate(restored.Save, out _), "progress without committed event rejected");
                CompleteTargets(state); Check(state.GoalComplete && state.MechanicComplete && state.Stars == 2, "new mechanic and mixed collection produce normal two stars");
                Check(!state.RecordMechanicProgress(999999) && !state.SetMechanicStage(1), "completed mechanic cannot invent extra progress");
                if (OceanCampaignState.MaximumMechanicStage(d.Mechanic) > 0)
                {
                    var stalePhase = new OceanCampaignState(state.Save); stalePhase.Current.mechanicStage = 1;
                    Check(!OceanCampaignState.Validate(stalePhase.Save, out _), "completed mechanics cannot retain an unfinished phase");
                }
                Valid(state);
            }
            state.StartLevel(20);
            Check(!state.RecordMechanicProgress(100000) && !state.SetMechanicStage(1) && !state.RecordMechanicMistake(), "published twenty levels cannot mutate new mechanic fields");
            state.Current.mechanicStage = 1;
            Check(!OceanCampaignState.Validate(state.Save, out _), "new mechanic state in a legacy level is invalid");
        }
        static void TwentySlotHistoryMigration()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ocean-twenty-history-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                int[] stars = new int[20]; for(int i=0;i<20;i++) stars[i]=i%3==0?0:i%3==1?2:3;
                string payload = "{\"version\":3,\"bestStars\":["+string.Join(",",stars)+"],\"shellMask\":1023,\"fishMask\":1023,\"shark\":true,\"currentRun\":null}";
                string main = Path.Combine(dir, OceanSaveStore.FileName); string old = SignedFixture(payload); File.WriteAllText(main, old);
                Check(OceanSaveStore.TryLoad(dir,out OceanSaveData data,out string error),"authenticated twenty-slot history migration: "+error);
                Check(data.version==5&&data.bestStars.Length==100&&data.shellMask==1023&&data.fishMask==1023&&data.shark,"twenty-slot migration retains collections and expands history");
                for(int i=0;i<100;i++)Check(data.bestStars[i]==(i<20?stars[i]:0),"all historical stars preserved, future slots earn no retroactive credit");
                Check(File.ReadAllText(main)==old,"loading migrated history never rewrites original evidence");
                var state=new OceanCampaignState(data); state.StartLevel(100);CompleteTargets(state);
                OceanSaveData reloaded = null;
                Check(OceanSaveStore.TrySave(dir,state.Save,out error)&&OceanSaveStore.TryLoad(dir,out reloaded,out error),"new level save commits with verified old-generation backup: "+error);
                for(int i=0;i<20;i++)Check(reloaded.bestStars[i]==stars[i],"new-level save cannot downgrade historical stars");
                Check(File.ReadAllText(Path.Combine(dir,OceanSaveStore.BackupFileName))==old,"migration commit retains original v3 recovery generation");
                // A checksum authenticates bytes, not a valid migration: unrecognized history sizes remain rejected.
                File.WriteAllText(main,SignedFixture(payload.Replace(string.Join(",",stars),"0,0,0")));
                File.Delete(Path.Combine(dir,OceanSaveStore.BackupFileName));
                Check(!OceanSaveStore.TryLoad(dir,out data,out OceanSaveLoadStatus status,out error)&&status==OceanSaveLoadStatus.Corrupt,"authenticated malformed legacy history cannot be padded silently");
            }
            finally { Directory.Delete(dir,true); }
        }
        static void RecoveryChallenge()
        {
            var state = new OceanCampaignState(); state.StartLevel(20); state.AcquireTool();
            Check(!state.RequiresSpearRecovery && state.TryFireSpear() && !state.Current.pendingSpearRecovery && state.CanFireSpear && !state.TryRecoverSpear(), "published boss gun retains consecutive trigger shots without a recovery lock");
            state.Current.pendingSpearRecovery = true;
            Check(!OceanCampaignState.Validate(state.Save, out _), "old levels cannot contain a final-tier recovery lock");
            for(int level=96;level<=100;level++)
            {
                state.StartLevel(level);
                Check(state.RequiresSpearRecovery && state.SharkHitTarget==10+level-96 && state.ShotLimit==20 && !state.TryRecoverSpear(), "final-tier data adds bounded 10–14 hit recovery challenges");
                Check(state.AcquireTool()&&state.TryFireSpear()&&state.Current.pendingSpearRecovery&&state.SpearAmmoRemaining==19, "new shot permanently spends one round and locks until recovered");
                Check(!state.TryFireSpear()&&!state.AcquireTool()&&state.Current.spearShotsFired==1, "held trigger or repeated pickup cannot bypass pending recovery or refill");
                Check(state.HitShark()&&!state.HitShark(), "pending recovery does not suppress the real projectile's one valid hit");
                var restored=new OceanCampaignState(state.Save);
                Check(restored.Current.pendingSpearRecovery&&!restored.CanFireSpear&&restored.SpearAmmoRemaining==19, "restored challenge retains its pending recovery and exact ammunition");
                Check(restored.TryRecoverSpear()&&!restored.TryRecoverSpear()&&restored.SpearAmmoRemaining==19&&restored.CanFireSpear, "one validated retrieval unlocks without inventing ammunition");
                CompleteTargets(restored); Check(restored.GoalComplete&&restored.Stars==2,"recovery challenge and electric goals complete together"); Valid(restored);
            }
            state.StartLevel(96);state.AcquireTool();
            for(int shot=0;shot<20;shot++){if(state.Current.pendingSpearRecovery)Check(state.TryRecoverSpear(),"miss loop retrieves previous shot");Check(state.TryFireSpear(),"miss loop spends another round");}
            Check(state.Current.pendingSpearRecovery&&!state.FinalizeSpearAmmo(true),"last pending projectile gets its normal final resolution chance");
            Check(state.FinalizeSpearAmmo(false)&&state.Failed&&state.AmmoExhausted&&!state.TryRecoverSpear()&&!state.TryFireSpear(),"resolved last miss fails without a free recovery round"); Valid(state);
            string dir=Path.Combine(Path.GetTempPath(),"ocean-pending-recovery-"+Guid.NewGuid().ToString("N"));
            try
            {
                OceanSaveData loaded=null;
                Check(OceanSaveStore.TrySave(dir,state.Save,out string error)&&OceanSaveStore.TryLoad(dir,out loaded,out error),"exhausted pending recovery is a supported durable result: "+error);
                Check(loaded.currentRun.pendingSpearRecovery&&loaded.currentRun.failed&&loaded.currentRun.spearShotsFired==20,"pending failure roundtrip retains budget and result");
            }
            finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
            state.StartLevel(96);state.AcquireTool();
            for(int shot=0;shot<20;shot++)
            {if(state.Current.pendingSpearRecovery)state.TryRecoverSpear();Check(state.TryFireSpear(),"last-hit loop spends round");if(shot>=10)Check(state.HitShark(),"last ten shots hit");}
            Check(state.Current.pendingSpearRecovery&&state.Current.sharkHits==10&&!state.FinalizeSpearAmmo(false)&&state.CatchShark(),"last cartridge can stun and capture before optional retrieval");
            CompleteTargets(state);Check(state.GoalComplete&&state.Stars==2&&!state.Failed,"last-shot capture and electric mechanism still earn stars");
            Check(state.TryRecoverSpear()&&state.SpearAmmoRemaining==0&&!state.CanFireSpear,"post-success retrieval never refills an exhausted gun");Valid(state);
        }
        static void VersionFourRecoveryMigration()
        {
            string dir=Path.Combine(Path.GetTempPath(),"ocean-v4-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try
            {
                var state=new OceanCampaignState();state.StartLevel(96);
                for(int n=0;n<state.Definition.MechanicTarget;n++)state.RecordMechanicProgress(100000+n);
                state.CollectCoin(20000);state.Save.bestStars[0]=2;state.Save.bestStars[19]=3;state.Save.bestStars[95]=3;
                Check(OceanSaveStore.TrySave(dir,state.Save,out string error),"v4 fixture begins as valid v5 output: "+error);
                string main=Path.Combine(dir,OceanSaveStore.FileName),backup=Path.Combine(dir,OceanSaveStore.BackupFileName);
                string encoded=File.ReadAllText(main);string payload=encoded.Substring(0,encoded.LastIndexOf(",\"sha256\":",StringComparison.Ordinal))+"}";
                payload=payload.Replace("\"version\":5","\"version\":4").Replace(",\"pendingSpearRecovery\":false","").Replace("\"sharkProgress\":[{\"id\":0,\"hits\":0,\"caught\":false}]","\"sharkProgress\":[]").Replace("\"completed\":false","\"completed\":true");
                foreach(bool expired in new[]{false,true})
                {
                    string original=SignedFixture(expired?payload.Replace("\"remainingSeconds\":600","\"remainingSeconds\":0").Replace("\"timedOut\":false","\"timedOut\":true"):payload);
                    File.WriteAllText(main,original);if(File.Exists(backup))File.Delete(backup);
                    Check(OceanSaveStore.TryLoad(dir,out OceanSaveData migrated,out error),"authenticated interim-v4 final-tier migration: "+error);
                    Check(migrated.version==5&&migrated.bestStars[0]==2&&migrated.bestStars[19]==3&&migrated.bestStars[95]==3,"all historical stars survive new recovery challenge requirement");
                    Check(!migrated.currentRun.completed&&migrated.currentRun.failed==expired&&migrated.currentRun.timedOut==expired&&migrated.currentRun.mechanicProgress==3&&migrated.currentRun.coinCollected&&!migrated.currentRun.pendingSpearRecovery,"only active final-tier completion is recalculated; electric and coin evidence remain");
                    Check(migrated.currentRun.leaderboardRunId==state.Current.leaderboardRunId&&File.ReadAllText(main)==original,"migration preserves score identity and original bytes until explicit commit");
                    Check(OceanSaveStore.TrySave(dir,migrated,out error)&&File.ReadAllText(backup)==original,"first v5 commit retains exact authenticated v4 backup: "+error);
                }
                string malformed=SignedFixture(payload.Replace("\"toolOwned\":false","\"toolOwned\":true"));File.WriteAllText(main,malformed);File.Delete(backup);
                Check(!OceanSaveStore.TryLoad(dir,out _,out OceanSaveLoadStatus status,out error)&&status==OceanSaveLoadStatus.Corrupt,"v4 cannot invent weapon progress that its original final tier never had");
            }
            finally{Directory.Delete(dir,true);}
        }
        static void Equal(OceanSaveData a, OceanSaveData b)
        {
            Check(a.version == b.version && a.shellMask == b.shellMask && a.fishMask == b.fishMask && a.shark == b.shark, "global fields roundtrip");
            for (int i = 0; i < OceanCampaignState.LevelCount; i++) Check(a.bestStars[i] == b.bestStars[i], "best stars roundtrip");
            Check((a.currentRun == null) == (b.currentRun == null), "active run presence roundtrip");
            if (a.currentRun == null) return;
            var x = a.currentRun; var y = b.currentRun;
            Check(x.level == y.level && x.remainingSeconds == y.remainingSeconds && x.shellMask == y.shellMask && x.fishMask == y.fishMask &&
                x.coinCollected == y.coinCollected && x.coinMask == y.coinMask && x.toolOwned == y.toolOwned && x.pendingSpearRecovery == y.pendingSpearRecovery && x.sharkHits == y.sharkHits && x.spearShotsFired == y.spearShotsFired && x.sharkCaught == y.sharkCaught &&
                x.completed == y.completed && x.failed == y.failed && x.timedOut == y.timedOut && x.ammoExhausted == y.ammoExhausted &&
                x.leaderboardRunId == y.leaderboardRunId && x.leaderboardFishBaseline == y.leaderboardFishBaseline &&
                x.leaderboardCoinMaskBaseline == y.leaderboardCoinMaskBaseline && x.leaderboardCoinBaseline == y.leaderboardCoinBaseline && x.leaderboardSharkBaseline == y.leaderboardSharkBaseline, "all active run scalars roundtrip exactly");
            Check(x.mechanicProgress==y.mechanicProgress&&x.mechanicStage==y.mechanicStage&&x.mechanicMistakes==y.mechanicMistakes&&x.mechanicElapsedSeconds==y.mechanicElapsedSeconds,"mechanic scalars roundtrip");
            Check(x.mechanicEventIds.Count==y.mechanicEventIds.Count,"mechanic event count roundtrip");
            for(int i=0;i<x.mechanicEventIds.Count;i++)Check(x.mechanicEventIds[i]==y.mechanicEventIds[i],"durable mechanic event identity roundtrip");
            Check(x.leaderboardShells.Count==y.leaderboardShells.Count,"shell score count roundtrip");
            for(int i=0;i<x.leaderboardShells.Count;i++)Check(x.leaderboardShells[i].pickupId==y.leaderboardShells[i].pickupId&&x.leaderboardShells[i].species==y.leaderboardShells[i].species,"shell score identity/species roundtrip");
            Check(x.sharkProgress.Count == y.sharkProgress.Count, "individual shark count roundtrip");
            for (int i = 0; i < x.sharkProgress.Count; i++) Check(x.sharkProgress[i].id == y.sharkProgress[i].id && x.sharkProgress[i].hits == y.sharkProgress[i].hits && x.sharkProgress[i].caught == y.sharkProgress[i].caught, "individual shark identity/hits/capture roundtrip");
            Check(x.consumedPickupIds.Count == y.consumedPickupIds.Count, "consumed count roundtrip");
            for (int i = 0; i < x.consumedPickupIds.Count; i++) Check(x.consumedPickupIds[i] == y.consumedPickupIds[i], "consumed identity/order roundtrip");
            Check(OceanCampaignState.Validate(b, out string error), "loaded data validates: " + error);
        }
#if OCEAN_CAMPAIGN_STANDALONE
        public static int Main()
        { try { RunAll(); return 0; } catch (Exception error) { Console.Error.WriteLine(error); return 1; } }
#endif
    }
}
#endif

using System;
using System.Collections.Generic;

namespace SwanUnderwater
{
    [Serializable]
    public sealed class OceanShellScore
    {
        public int pickupId, species;
        public OceanShellScore(int id, int kind) { pickupId = id; species = kind; }
    }

    [Serializable]
    public sealed class OceanSharkProgress
    {
        public int id, hits;
        public bool caught;
        public OceanSharkProgress(int identity, int hitCount = 0, bool isCaught = false)
        { id = identity; hits = hitCount; caught = isCaught; }
    }

    [Serializable]
    public sealed class OceanLevelRun
    {
        public int level;
        public int mechanicProgress, mechanicStage, mechanicMistakes;
        public float mechanicElapsedSeconds;
        public List<int> mechanicEventIds = new List<int>();
        public float remainingSeconds;
        public int shellMask, fishMask, coinMask;
        public bool coinCollected, toolOwned;
        public bool pendingSpearRecovery;
        public int sharkHits;
        public int spearShotsFired;
        public bool sharkCaught;
        // sharkHits/sharkCaught retain the best individual / any-captured summary used by old saves and scores.
        public List<OceanSharkProgress> sharkProgress = new List<OceanSharkProgress>();
        public List<int> consumedPickupIds = new List<int>();
        // Only successful shell pickups performed after schema 3 are eligible for lifetime scoring.
        public List<OceanShellScore> leaderboardShells = new List<OceanShellScore>();
        public bool completed, failed, timedOut, ammoExhausted;
        public string leaderboardRunId = "";
        public int leaderboardFishBaseline, leaderboardCoinMaskBaseline;
        public bool leaderboardCoinBaseline, leaderboardSharkBaseline;
    }

    [Serializable]
    public sealed class OceanSaveData
    {
        public int version = 5;
        public int[] bestStars = new int[OceanLevelCatalog.Count];
        public int shellMask, fishMask;
        public bool shark;
        public OceanLevelRun currentRun;
    }

    /// <summary>Deterministic campaign rules. Scene code validates reach, tracking, hits and pause state.</summary>
    public sealed class OceanCampaignState
    {
        public const int LevelCount = OceanLevelCatalog.Count, ShellSpeciesCount = 10, FishSpeciesCount = OceanLevelCatalog.FishSpeciesCount, RequiredSharkHits = 10;
        public const int SpearShotLimit = 20;
        public const int MaximumSharks = 32;
        public OceanSaveData Save { get; }
        public OceanLevelRun Current => Save.currentRun;
        public OceanLevelDefinition Definition => OceanLevelCatalog.Get(Current == null ? 1 : Current.level);
        public int CoinCount => Current == null ? 0 : CountBits(Current.coinMask);
        public int CoinTargetCount => Current == null ? 0 : Definition.CoinCount;
        public int CoinTargetMask => Current == null ? 0 : CoinMaskFor(CoinTargetCount);
        public int SharkHitTarget => Current == null ? 0 : Definition.SharkHits;
        public int ShotLimit => Current == null ? 0 : Definition.AmmoLimit;
        public bool RequiresShark => Current != null && SharkHitTarget > 0;
        public bool HasMechanic => Current != null && Definition.Mechanic != OceanMechanicKind.None;
        public bool MechanicComplete => Current != null && (!HasMechanic || Current.mechanicProgress >= Definition.MechanicTarget);
        public int NextFishSpecies => Current == null ? -1 : FirstMissing(FishTargetMask, Current.fishMask);
        public int NextShellSpecies => Current == null ? -1 : FirstMissing(ShellTargetMask, Current.shellMask);
        public bool GoalComplete => Current != null && Current.completed;
        public bool Failed => Current != null && Current.failed;
        public bool TimedOut => Current != null && Current.timedOut;
        public bool AmmoExhausted => Current != null && Current.ammoExhausted;
        public string FailureReason => !Failed ? string.Empty : AmmoExhausted ? "ammoExhausted" : "timeExpired";
        public int Stars => GoalComplete ? (Current.coinCollected ? 3 : 2) : 0;
        public float TimeLimitSeconds => Current == null ? 0f : GetTimeLimitSeconds(Current.level);
        public bool UnlimitedTime => Current != null && TimeLimitSeconds == 0f;
        public int ShellTargetMask => Current == null ? 0 : GetShellTargetMask(Current.level);
        public int FishTargetMask => Current == null ? 0 : GetFishTargetMask(Current.level);
        public bool CanCollect => Current != null && !Current.timedOut && !Current.failed;
        public int SpearAmmoRemaining => RequiresShark ? Math.Max(0, ShotLimit - Current.spearShotsFired) : 0;
        public bool RequiresSpearRecovery => RequiresShark && Definition.Mechanic == OceanMechanicKind.ElectricFinale;
        public bool CanFireSpear => CanCollect && RequiresShark && Current.toolOwned && !Current.sharkCaught &&
            !Current.pendingSpearRecovery && SpearAmmoRemaining > 0;

        public OceanCampaignState(OceanSaveData data = null)
        {
            data = data ?? new OceanSaveData();
            if (!Validate(data, out string error)) throw new ArgumentException(error, nameof(data));
            Save = Copy(data);
            MigrateLegacySharkProgress(Save);
            // Existing collection masks cannot reconstruct lifetime counts. Attach a new
            // durable run identity, excluding progress earned before this feature existed.
            if (Current != null && string.IsNullOrEmpty(Current.leaderboardRunId))
            {
                Current.leaderboardRunId = Guid.NewGuid().ToString("N");
                Current.leaderboardShells.Clear();
                Current.leaderboardFishBaseline = Current.fishMask;
                Current.leaderboardCoinBaseline = Current.coinCollected;
                Current.leaderboardCoinMaskBaseline = Current.coinMask;
                Current.leaderboardSharkBaseline = Current.sharkCaught;
            }
        }

        /// <summary>Keep the shared state owner; adopt only a completely authenticated and validated load.</summary>
        internal void ApplyVerifiedSave(OceanSaveData data)
        {
            var restored = new OceanCampaignState(data);
            Save.version = restored.Save.version; Save.bestStars = restored.Save.bestStars;
            Save.shellMask = restored.Save.shellMask; Save.fishMask = restored.Save.fishMask;
            Save.shark = restored.Save.shark; Save.currentRun = restored.Save.currentRun;
        }

        public static float GetTimeLimitSeconds(int level)
        { CheckLevel(level); return OceanLevelCatalog.Get(level).TimeLimitSeconds; }
        public static int GetShellTargetMask(int level)
        { CheckLevel(level); return OceanLevelCatalog.Get(level).ShellMask; }
        public static int GetFishTargetMask(int level)
        { CheckLevel(level); return OceanLevelCatalog.Get(level).FishMask; }

        /// <summary>All catalog levels are selectable. Starting replaces only the active run, not history.</summary>
        public void StartLevel(int level)
        {
            CheckLevel(level);
            Save.currentRun = new OceanLevelRun { level = level, remainingSeconds = GetTimeLimitSeconds(level), leaderboardRunId = Guid.NewGuid().ToString("N") };
            MigrateLegacySharkProgress(Save);
        }
        public void RestartLevel() => StartLevel(Current == null ? 1 : Current.level);
        /// <summary>Begin a clean run of the requested level, preserving best stars and global collection.</summary>
        public void ResetLevel(int level) => StartLevel(level);
        public void RestartGame() => StartLevel(1);
        public bool IsConsumed(int instanceId) => Current != null && Current.consumedPickupIds.Contains(instanceId);

        /// <summary>Caller sets running=false for menus/result panels. Completed goals still use remaining time.</summary>
        public void Tick(float seconds, bool running)
        {
            if (!running || !CanCollect || UnlimitedTime || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            Current.remainingSeconds = Math.Max(0f, Current.remainingSeconds - seconds);
            if (Current.remainingSeconds == 0f)
            {
                Current.timedOut = true;
                Current.failed = !GoalComplete;
            }
        }

        /// <summary>Scene code validates hands, reach and the mechanic-specific event before committing a durable node.</summary>
        public bool RecordMechanicProgress(int uniqueEventId, int stage = 0)
        {
            if (!CanCollect || !HasMechanic || MechanicComplete || uniqueEventId < 0 || uniqueEventId > 1000000000 ||
                stage < 0 || stage > MaximumMechanicStage(Definition.Mechanic) ||
                (Current.mechanicProgress + 1 == Definition.MechanicTarget && stage != 0) || Current.mechanicEventIds.Contains(uniqueEventId)) return false;
            Current.mechanicEventIds.Add(uniqueEventId); Current.mechanicProgress++;
            Current.mechanicStage = stage; UpdateGoal(); return true;
        }
        public bool SetMechanicStage(int stage)
        {
            if (!CanCollect || !HasMechanic || MechanicComplete || stage < 0 || stage > MaximumMechanicStage(Definition.Mechanic)) return false;
            Current.mechanicStage = stage; return true;
        }
        public bool RecordMechanicMistake()
        {
            if (!CanCollect || !HasMechanic || MechanicComplete || Current.mechanicMistakes >= 100000) return false;
            Current.mechanicMistakes++; return true;
        }
        /// <summary>Caller supplies active gameplay time only. No time is integrated across menus or tracking loss.</summary>
        public void UpdateMechanicElapsed(float seconds)
        {
            if (!CanCollect || !HasMechanic || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            Current.mechanicElapsedSeconds = Math.Min(Definition.TimeLimitSeconds, Current.mechanicElapsedSeconds + seconds);
        }

        /// <summary>Physical duplicates remain collectible; order applies only to undiscovered species.</summary>
        public bool CollectShell(int instanceId, int species)
        {
            if (!CanCollect || instanceId > 1000000000 || species < 0 || species >= ShellSpeciesCount || (ShellTargetMask & (1 << species)) == 0 ||
                (instanceId >= OceanLevelCatalog.CoinIdBase && instanceId < OceanLevelCatalog.CoinIdBase + 5) ||
                (Definition.OrderedShell && (Current.shellMask & (1 << species)) == 0 && species != NextShellSpecies) || !AcceptPickupId(instanceId)) return false;
            Current.consumedPickupIds.Add(instanceId);
            Current.leaderboardShells.Add(new OceanShellScore(instanceId, species));
            Current.shellMask |= 1 << species; Save.shellMask |= 1 << species;
            UpdateGoal(); return true;
        }
        public bool CollectFish(int species)
        {
            if (!CanCollect || species < 0 || species >= FishSpeciesCount || (FishTargetMask & (1 << species)) == 0 || (Current.fishMask & (1 << species)) != 0 ||
                (Definition.OrderedFish && species != NextFishSpecies)) return false;
            Current.fishMask |= 1 << species; Save.fishMask |= 1 << species;
            UpdateGoal(); return true;
        }
        public bool CollectCoin(int instanceId)
        {
            int slot = instanceId - OceanLevelCatalog.CoinIdBase;
            if (!CanCollect || slot < 0 || slot >= CoinTargetCount || (Current.coinMask & (1 << slot)) != 0 || !AcceptPickupId(instanceId)) return false;
            Current.consumedPickupIds.Add(instanceId); Current.coinMask |= 1 << slot;
            Current.coinCollected = Current.coinMask == CoinMaskFor(CoinTargetCount);
            UpdateGoal(); return true;
        }
        public bool AcquireTool()
        {
            if (!CanCollect || !RequiresShark || Current.toolOwned) return false;
            Current.toolOwned = true; return true;
        }
        /// <summary>Consume one permanent run cartridge before creating its projectile. Showing the tool never refills it.</summary>
        public bool TryFireSpear()
        {
            if (!CanFireSpear) return false;
            Current.spearShotsFired++;
            if (RequiresSpearRecovery) Current.pendingSpearRecovery = true;
            return true;
        }
        /// <summary>A validated reach/grab of the recovered spear unlocks the next shot; ammunition is never replenished.</summary>
        public bool TryRecoverSpear()
        {
            if (!CanCollect || !RequiresSpearRecovery || !Current.pendingSpearRecovery) return false;
            Current.pendingSpearRecovery = false; return true;
        }
        /// <summary>
        /// Call after all current projectile resolutions (including the last hit), or explicitly after restore.
        /// The catalog hit target preserves the opportunity to capture even with no ammunition remaining.
        /// Returns true only for a newly committed ammunition failure. Success is never replaced with failure.
        /// </summary>
        public bool FinalizeSpearAmmo(bool projectilesInFlight)
        {
            if (Current == null || !RequiresShark || Current.failed || Current.completed || Current.sharkCaught ||
                projectilesInFlight || Current.spearShotsFired < ShotLimit) return false;
            if (Current.sharkHits >= SharkHitTarget) return false;
            Current.ammoExhausted = true; Current.failed = true; return true;
        }
        public int SharkHitsFor(int id)
        {
            var progress = FindShark(id);
            return progress == null ? 0 : progress.hits;
        }
        public bool SharkCaughtFor(int id) => FindShark(id)?.caught ?? false;
        public bool HitShark() => HitShark(0);
        /// <summary>Scene code must call once per validated speargun shot, not once per overlap frame.</summary>
        public bool HitShark(int id)
        {
            if (id < 0 || id >= MaximumSharks || !CanCollect || !RequiresShark || !Current.toolOwned || Current.sharkCaught) return false;
            var progress = FindShark(id);
            if ((progress != null && (progress.caught || progress.hits >= SharkHitTarget)) || TotalSharkHits() >= Current.spearShotsFired) return false;
            if (progress == null) Current.sharkProgress.Add(progress = new OceanSharkProgress(id));
            progress.hits++;
            Current.sharkHits = Math.Max(Current.sharkHits, progress.hits);
            return true;
        }
        public bool CatchShark() => CatchShark(0);
        public bool CatchShark(int id)
        {
            if (id < 0 || id >= MaximumSharks || !CanCollect || !RequiresShark || !Current.toolOwned || Current.sharkCaught) return false;
            var progress = FindShark(id);
            if (progress == null || progress.caught || progress.hits < SharkHitTarget) return false;
            progress.caught = true; Current.sharkCaught = true; Save.shark = true;
            UpdateGoal(); return true;
        }
        OceanSharkProgress FindShark(int id)
        {
            if (id < 0 || id >= MaximumSharks || Current?.sharkProgress == null) return null;
            foreach (var progress in Current.sharkProgress) if (progress.id == id) return progress;
            return null;
        }
        int TotalSharkHits()
        {
            int total = 0;
            foreach (var progress in Current.sharkProgress) total += progress.hits;
            return total;
        }
        bool AcceptPickupId(int id) => id >= 0 && Current.consumedPickupIds.Count < 4096 && !IsConsumed(id);
        void UpdateGoal()
        {
            Current.completed = MeetsGoal(Current);
            Save.bestStars[Current.level - 1] = Math.Max(Save.bestStars[Current.level - 1], Stars);
        }
        static bool MeetsGoal(OceanLevelRun run)
        {
            var definition = OceanLevelCatalog.Get(run.level);
            return (definition.Mechanic == OceanMechanicKind.None || run.mechanicProgress >= definition.MechanicTarget) &&
                (definition.SharkHits == 0 || (run.toolOwned && run.sharkHits >= definition.SharkHits && run.sharkCaught)) &&
                (run.shellMask & GetShellTargetMask(run.level)) == GetShellTargetMask(run.level) &&
                (run.fishMask & GetFishTargetMask(run.level)) == GetFishTargetMask(run.level);
        }
        static void CheckLevel(int level)
        { if (level < 1 || level > LevelCount) throw new ArgumentOutOfRangeException(nameof(level), "Level must be within the campaign catalog."); }

        internal static OceanSaveData Copy(OceanSaveData source)
        {
            var result = new OceanSaveData { version = source.version, bestStars = (int[])source.bestStars.Clone(), shellMask = source.shellMask, fishMask = source.fishMask, shark = source.shark };
            var run = source.currentRun;
            if (run != null) result.currentRun = new OceanLevelRun { level = run.level, remainingSeconds = run.remainingSeconds,
                mechanicProgress = run.mechanicProgress, mechanicStage = run.mechanicStage, mechanicMistakes = run.mechanicMistakes,
                mechanicElapsedSeconds = run.mechanicElapsedSeconds, mechanicEventIds = new List<int>(run.mechanicEventIds),
                shellMask = run.shellMask, fishMask = run.fishMask, coinMask = run.coinMask, coinCollected = run.coinCollected, toolOwned = run.toolOwned,
                sharkHits = run.sharkHits, spearShotsFired = run.spearShotsFired, sharkCaught = run.sharkCaught, pendingSpearRecovery = run.pendingSpearRecovery, consumedPickupIds = new List<int>(run.consumedPickupIds),
                sharkProgress = run.sharkProgress == null ? new List<OceanSharkProgress>() : run.sharkProgress.ConvertAll(item => new OceanSharkProgress(item.id, item.hits, item.caught)),
                leaderboardShells = run.leaderboardShells.ConvertAll(item => new OceanShellScore(item.pickupId, item.species)),
                completed = run.completed, failed = run.failed, timedOut = run.timedOut, ammoExhausted = run.ammoExhausted,
                leaderboardRunId = run.leaderboardRunId, leaderboardFishBaseline = run.leaderboardFishBaseline,
                leaderboardCoinBaseline = run.leaderboardCoinBaseline, leaderboardCoinMaskBaseline = run.leaderboardCoinMaskBaseline, leaderboardSharkBaseline = run.leaderboardSharkBaseline };
            return result;
        }

        /// <summary>Call only after validating/authenticating an old save; its one historical shark was identity zero.</summary>
        internal static void MigrateLegacySharkProgress(OceanSaveData data)
        {
            var run = data.currentRun;
            if (run == null || OceanLevelCatalog.Get(run.level).SharkHits == 0) return;
            if (run.sharkProgress == null) run.sharkProgress = new List<OceanSharkProgress>();
            if (run.sharkProgress.Count == 0) run.sharkProgress.Add(new OceanSharkProgress(0, run.sharkHits, run.sharkCaught));
        }

        /// <summary>Reject inconsistent/unsupported data; never silently erase unknown progress.</summary>
        public static bool Validate(OceanSaveData data, out string error)
        {
            error = null;
            if (data == null || data.version != 5) return Invalid("Unsupported or missing save version; explicit migration required.", out error);
            if (data.bestStars == null || data.bestStars.Length != LevelCount) return Invalid("bestStars must contain every catalog level.", out error);
            foreach (int stars in data.bestStars) if (stars != 0 && stars != 2 && stars != 3) return Invalid("Only zero, two or three stars are valid.", out error);
            if (data.shellMask < 0 || (data.shellMask & ~1023) != 0 || data.fishMask < 0 || (data.fishMask & ~((1 << FishSpeciesCount) - 1)) != 0) return Invalid("Global collection contains unknown species.", out error);
            var run = data.currentRun;
            if (run == null) return true;
            if (!string.IsNullOrEmpty(run.leaderboardRunId))
            {
                if (run.leaderboardRunId.Length != 32) return Invalid("Invalid leaderboard run identity.", out error);
                foreach (char c in run.leaderboardRunId)
                    if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return Invalid("Invalid leaderboard run identity.", out error);
            }
            if (run.leaderboardFishBaseline < 0 || (run.leaderboardFishBaseline & ~run.fishMask) != 0 ||
                (run.leaderboardCoinBaseline && !run.coinCollected) || run.leaderboardCoinMaskBaseline < 0 ||
                (run.leaderboardCoinMaskBaseline & ~run.coinMask) != 0 || (run.leaderboardSharkBaseline && !run.sharkCaught))
                return Invalid("Leaderboard migration baseline exceeds collected progress.", out error);
            if (run.level < 1 || run.level > LevelCount) return Invalid("Invalid active level.", out error);
            var definition = OceanLevelCatalog.Get(run.level);
            float limit = definition.TimeLimitSeconds;
            if (run.mechanicEventIds == null || run.mechanicEventIds.Count > 64 || run.mechanicProgress < 0 ||
                run.mechanicProgress > definition.MechanicTarget || run.mechanicProgress != run.mechanicEventIds.Count ||
                run.mechanicStage < 0 || run.mechanicStage > MaximumMechanicStage(definition.Mechanic) ||
                (run.mechanicProgress == definition.MechanicTarget && run.mechanicStage != 0) || run.mechanicMistakes < 0 || run.mechanicMistakes > 100000 ||
                float.IsNaN(run.mechanicElapsedSeconds) || float.IsInfinity(run.mechanicElapsedSeconds) || run.mechanicElapsedSeconds < 0 || run.mechanicElapsedSeconds > limit)
                return Invalid("Invalid durable mechanic progress or active time.", out error);
            var mechanicIds = new HashSet<int>();
            foreach (int id in run.mechanicEventIds)
                if (id < 0 || id > 1000000000 || !mechanicIds.Add(id)) return Invalid("Mechanic event identities must be bounded and unique.", out error);
            if (definition.Mechanic == OceanMechanicKind.None &&
                (run.mechanicProgress != 0 || run.mechanicStage != 0 || run.mechanicMistakes != 0 || run.mechanicElapsedSeconds != 0))
                return Invalid("Legacy levels cannot contain new mechanic progress.", out error);
            int coinTarget = CoinMaskFor(definition.CoinCount);
            if (run.coinMask < 0 || (run.coinMask & ~coinTarget) != 0 || run.coinCollected != (run.coinMask == coinTarget))
                return Invalid("Coin mask disagrees with the level or all-coins flag.", out error);
            if (run.leaderboardCoinBaseline != (run.leaderboardCoinMaskBaseline == coinTarget))
                return Invalid("Legacy coin baseline flag disagrees with the migration mask.", out error);
            if ((definition.OrderedShell && !IsOrderedPrefix(definition.ShellMask, run.shellMask)) ||
                (definition.OrderedFish && !IsOrderedPrefix(definition.FishMask, run.fishMask)))
                return Invalid("Ordered target progress skipped an earlier species.", out error);
            if (float.IsNaN(run.remainingSeconds) || float.IsInfinity(run.remainingSeconds) || run.remainingSeconds < 0f || run.remainingSeconds > limit) return Invalid("Invalid remaining time.", out error);
            if (run.shellMask < 0 || (run.shellMask & ~GetShellTargetMask(run.level)) != 0 || run.fishMask < 0 || (run.fishMask & ~GetFishTargetMask(run.level)) != 0) return Invalid("Active inventory has species outside the level targets.", out error);
            if ((data.shellMask & run.shellMask) != run.shellMask || (data.fishMask & run.fishMask) != run.fishMask || (run.sharkCaught && !data.shark)) return Invalid("Global collection omits active-run discoveries.", out error);
            if (run.sharkHits < 0 || run.sharkHits > definition.SharkHits || (run.sharkHits > 0 && !run.toolOwned) || (run.sharkCaught && (!run.toolOwned || run.sharkHits < definition.SharkHits))) return Invalid("Invalid tool/shark progression.", out error);
            if (run.spearShotsFired < 0 || run.spearShotsFired > definition.AmmoLimit || run.spearShotsFired < run.sharkHits ||
                (run.spearShotsFired > 0 && !run.toolOwned)) return Invalid("Invalid durable spear ammunition.", out error);
            if (run.pendingSpearRecovery && (definition.Mechanic != OceanMechanicKind.ElectricFinale || definition.SharkHits == 0 ||
                !run.toolOwned || run.spearShotsFired == 0)) return Invalid("Pending spear recovery requires a spent round in a recovery challenge.", out error);
            // An absent/empty list is the explicitly supported old single-shark representation.
            // Once individual records exist their totals and summaries must be fully consistent.
            if (run.sharkProgress != null && run.sharkProgress.Count > 0)
            {
                if (definition.SharkHits == 0 || run.sharkProgress.Count > MaximumSharks)
                    return Invalid("Individual shark progress requires a bounded boss-level list.", out error);
                var sharkIds = new HashSet<int>(); int totalHits = 0, bestHits = 0; bool anyCaught = false;
                foreach (var shark in run.sharkProgress)
                {
                    if (shark == null || shark.id < 0 || shark.id >= MaximumSharks || !sharkIds.Add(shark.id) ||
                        shark.hits < 0 || shark.hits > definition.SharkHits || (shark.hits > 0 && !run.toolOwned) ||
                        (shark.caught && (!run.toolOwned || shark.hits < definition.SharkHits)))
                        return Invalid("Invalid individual shark progression or duplicate identity.", out error);
                    totalHits += shark.hits; bestHits = Math.Max(bestHits, shark.hits); anyCaught |= shark.caught;
                }
                if (totalHits > run.spearShotsFired || bestHits != run.sharkHits || anyCaught != run.sharkCaught)
                    return Invalid("Individual sharks disagree with spent ammunition or the best/any summary.", out error);
            }
            if (definition.SharkHits == 0 && (run.toolOwned || run.sharkCaught || run.sharkHits != 0 || run.spearShotsFired != 0)) return Invalid("Tool/shark progress requires a boss level.", out error);
            if (run.completed != MeetsGoal(run)) return Invalid("Completion flag disagrees with the targets.", out error);
            bool expectedTimeout = limit > 0f && run.remainingSeconds == 0f;
            if (run.ammoExhausted && (definition.SharkHits == 0 || run.spearShotsFired != definition.AmmoLimit || run.sharkHits >= definition.SharkHits || run.completed || run.sharkCaught))
                return Invalid("Ammunition failure requires an exhausted boss run below its hit target.", out error);
            if (run.timedOut != expectedTimeout || run.failed != ((expectedTimeout && !run.completed) || run.ammoExhausted))
                return Invalid("Failure reason disagrees with the timer, ammunition or targets.", out error);
            int earned = run.completed ? (run.coinCollected ? 3 : 2) : 0;
            if (data.bestStars[run.level - 1] < earned) return Invalid("Best stars omit the current result.", out error);
            if (run.consumedPickupIds == null || run.consumedPickupIds.Count > 4096) return Invalid("Invalid consumed-pickup list.", out error);
            var unique = new HashSet<int>();
            foreach (int id in run.consumedPickupIds) if (id < 0 || !unique.Add(id)) return Invalid("Consumed IDs must be non-negative and unique.", out error);
            for (int slot = 0; slot < 5; slot++)
                if (((run.coinMask & (1 << slot)) != 0) != unique.Contains(OceanLevelCatalog.CoinId(slot)))
                    return Invalid("Coin mask disagrees with consumed coin IDs.", out error);
            if (run.leaderboardShells == null || run.leaderboardShells.Count > 4096) return Invalid("Invalid new shell score list.", out error);
            var scoredShells = new HashSet<int>();
            foreach (OceanShellScore shell in run.leaderboardShells)
                if (shell == null || shell.pickupId < 0 || shell.pickupId > 1000000000 ||
                    (shell.pickupId >= OceanLevelCatalog.CoinIdBase && shell.pickupId < OceanLevelCatalog.CoinIdBase + 5) ||
                    !unique.Contains(shell.pickupId) || !scoredShells.Add(shell.pickupId) || shell.species < 0 || shell.species >= ShellSpeciesCount ||
                    (definition.ShellMask & (1 << shell.species)) == 0 || (run.shellMask & (1 << shell.species)) == 0)
                    return Invalid("Shell score must match a unique consumed entity and this level's collected species.", out error);
            return true;
        }
        /// <summary>Only phases emitted by this mechanic may appear in a durable run.</summary>
        public static int MaximumMechanicStage(OceanMechanicKind kind)
        {
            switch (kind)
            {
                case OceanMechanicKind.ShipwreckLock:
                case OceanMechanicKind.RunePuzzle: return 2;
                case OceanMechanicKind.OxygenRelay:
                case OceanMechanicKind.DeepLight:
                case OceanMechanicKind.Observation:
                case OceanMechanicKind.KelpMaze:
                case OceanMechanicKind.CoralRepair:
                case OceanMechanicKind.Cleanup:
                case OceanMechanicKind.TideGates:
                case OceanMechanicKind.ElectricFinale: return 1;
                default: return 0;
            }
        }
        internal static int CoinMaskFor(int count) => (1 << count) - 1;
        static int CountBits(int mask) { int count = 0; while (mask != 0) { count += mask & 1; mask >>= 1; } return count; }
        static int FirstMissing(int target, int collected)
        { for (int species = 0; species < 10; species++) if ((target & (1 << species)) != 0 && (collected & (1 << species)) == 0) return species; return -1; }
        static bool IsOrderedPrefix(int target, int collected)
        { int missing = FirstMissing(target, collected); return missing < 0 || (collected & ~((1 << missing) - 1)) == 0; }
        static bool Invalid(string detail, out string error) { error = detail; return false; }
    }
}

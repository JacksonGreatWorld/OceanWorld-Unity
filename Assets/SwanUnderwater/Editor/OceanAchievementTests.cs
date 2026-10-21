#if UNITY_EDITOR || OCEAN_CAMPAIGN_STANDALONE
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Deterministic facts and isolated real-filesystem transactions; no Unity scene/device claim.</summary>
    public static class OceanAchievementTests
    {
        public static int LastAssertionCount { get; private set; }
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static void RunAll()
        {
            LastAssertionCount = 0;
            string root = Path.Combine(Path.GetTempPath(), "ocean-achievement-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); bool passed = false;
            try
            {
                CatalogBoundaries(root); LegacyEditionCompatibility(root); ColdAndHistoricalBackfill(root); LiveProgressAndSeen(root);
                SaveFailureRollback(root); CorruptionAndFuturePreservation(root); GenerationConflicts(root); OtherFilesUntouched(root);
                passed = true;
                Print("OceanAchievementTests PASS: " + LastAssertionCount +
                    " assertions; 30 current thresholds plus one frozen historical identity, old journal byte preservation, trusted-snapshot backfill, sticky progress/unread, idempotence, actual JSON/checksum/backup/filesystem roundtrip, corruption/future protection, failed-write rollback and external generation conflicts. No Unity scene, headset or server claim.");
            }
            finally
            {
                if (passed) Directory.Delete(root, true);
                else Print("OceanAchievementTests failing fixtures preserved at " + root);
            }
        }

        static void CatalogBoundaries(string root)
        {
            Check(OceanAchievementCatalog.Count == 31 && OceanAchievementCatalog.All.Count == 31 && OceanAchievementCatalog.CurrentCount == 30,
                "thirty current entries plus one preserved historical identity");
            Check(OceanAchievementCatalog.All[11].Id == "observe-all" && OceanAchievementCatalog.All[29].Id == "campaign-finished" &&
                OceanAchievementCatalog.All[30].Id == "observe-current-all", "new current edition appends without reordering existing durable identities");
            Check(Find("observe-all").Historical && Find("observe-all").Target == 31 &&
                Find("observe-all").Description == "观察本版图鉴的全部31种生物" && !Find("observe-current-all").Historical &&
                Find("observe-current-all").Target == 30, "historical 31-category honour stays immutable while current edition has its own identity");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var categories = new HashSet<string>(StringComparer.Ordinal);
            foreach (OceanAchievementDefinition definition in OceanAchievementCatalog.All)
            {
                Check(!string.IsNullOrWhiteSpace(definition.Id) && identities.Add(definition.Id), "stable unique achievement identity");
                Check(definition.Target > 0 && !string.IsNullOrWhiteSpace(definition.Name) && !string.IsNullOrWhiteSpace(definition.Description), "visible Chinese metadata and positive threshold");
                categories.Add(definition.Category);
                Check(definition.Progress(FactsFor(definition.Id, -1)) == 0, "negative facts cannot unlock: " + definition.Id);
                Check(definition.Progress(FactsFor(definition.Id, definition.Target - 1)) == definition.Target - 1, "exact lower boundary: " + definition.Id);
                Check(definition.Progress(FactsFor(definition.Id, definition.Target)) == definition.Target, "exact achievement boundary: " + definition.Id);
                Check(definition.Progress(FactsFor(definition.Id, long.MaxValue)) == definition.Target, "large facts saturate without overflow: " + definition.Id);
                string directory = Path.Combine(root, "boundary-" + definition.Id);
                var journal = new OceanAchievementJournal(directory);
                journal.Evaluate(FactsFor(definition.Id, definition.Target - 1), true);
                Check(!journal.IsUnlocked(definition.Id), "lower boundary stays locked: " + definition.Id);
                IReadOnlyList<string> unlocked = journal.Evaluate(FactsFor(definition.Id, definition.Target), true);
                Check(journal.IsUnlocked(definition.Id) && journal.IsUnread(definition.Id) && Contains(unlocked, definition.Id), "threshold commits exactly one identity transition");
                Check(new OceanAchievementJournal(directory).IsUnlocked(definition.Id), "threshold survives actual disk reload");
            }
            Check(categories.SetEquals(new[] { "收集", "探索", "关卡", "珍藏", "挑战" }), "five UI categories match the catalog");
            var noCatalog = new OceanAchievementFacts { ObservedSpecies = 0, ObservedTarget = 0 };
            Check(Find("observe-all").Progress(noCatalog) == 0, "empty/missing observation catalog is not completion");
            noCatalog.ObservedSpecies = 31; noCatalog.ObservedTarget = 32;
            Check(Find("observe-all").Progress(noCatalog) == 31 && Find("observe-all").Target == 31,
                "a later observation catalog preserves the original 31-species achievement identity");
            noCatalog.ObservedSpecies = 30;
            Check(Find("observe-all").Progress(noCatalog) == 30, "edition achievement exposes actual species progress rather than a boolean");
        }

        static void LegacyEditionCompatibility(string root)
        {
            string directory = Path.Combine(root, "legacy-thirty-definitions"), file = FileIn(directory);
            var original = new OceanAchievementJournal(directory);
            original.Evaluate(MaximumFacts(), false);
            // Create the exact prior schema/catalog shape: 30 entries, no appended current honour.
            Mutate(file, body =>
            {
                var rows = OceanLeaderboardJson.Array(body, "entries");
                Check(OceanLeaderboardJson.Text(OceanLeaderboardJson.Object(rows[30]), "id") == "observe-current-all",
                    "legacy fixture removes only the appended identity");
                rows.RemoveAt(30);
            });
            File.Copy(file, file + ".backup");
            string primary = File.ReadAllText(file, Utf8), backup = File.ReadAllText(file + ".backup", Utf8);
            DateTime marker = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(file, marker); File.SetLastWriteTimeUtc(file + ".backup", marker);
            var loaded = new OceanAchievementJournal(directory);
            Check(loaded.Writable && loaded.UnlockedCount == 30 && loaded.CurrentUnlockedCount == 29 && loaded.HistoricalUnlockedCount == 1,
                "old 30-entry journal loads as 29 current unlocks plus its earned historical honour");
            Check(loaded.IsUnlocked("observe-all") && loaded.Progress("observe-all") == 31 &&
                !loaded.IsUnlocked("observe-current-all") && loaded.Progress("observe-current-all") == 0,
                "old 31-category progress is retained but never impersonates the new current-edition achievement");
            Check(loaded.MarkAllSeen() && File.ReadAllText(file, Utf8) == primary && File.ReadAllText(file + ".backup", Utf8) == backup &&
                File.GetLastWriteTimeUtc(file) == marker && File.GetLastWriteTimeUtc(file + ".backup") == marker,
                "opening and no-op reading an old journal preserve both original byte/checksum envelopes and timestamps");
            var current = MaximumFacts(); current.ObservedSpecies = 30;
            var earned = loaded.Evaluate(current, false);
            Check(Contains(earned, "observe-current-all") && loaded.CurrentUnlockedCount == 30 && loaded.HistoricalUnlockedCount == 1 &&
                loaded.UnlockedCount == 31 && loaded.Progress("observe-all") == 31,
                "current full completion remains 30 of 30 regardless of the additional frozen historical honour");
            Check(File.ReadAllText(file + ".backup", Utf8) == primary, "first new earned commit keeps the exact old journal as backup");
            var reloaded = new OceanAchievementJournal(directory);
            Check(reloaded.Writable && reloaded.CurrentUnlockedCount == 30 && reloaded.HistoricalUnlockedCount == 1 && reloaded.UnreadCount == 0,
                "historical and current full completion both survive a normal journal reload without notifications");
        }

        static void ColdAndHistoricalBackfill(string root)
        {
            string directory = Path.Combine(root, "old-save-backfill"), file = FileIn(directory);
            var journal = new OceanAchievementJournal(directory);
            Check(journal.Writable && journal.LastError == "" && journal.Revision == 0 && journal.UnlockedCount == 0 && journal.UnreadCount == 0, "new journal starts empty and writable");
            Check(!Directory.Exists(directory), "reading an absent journal does not create storage");
            Check(journal.Evaluate(new OceanAchievementFacts(), false).Count == 0 && journal.MarkAllSeen() && !File.Exists(file), "empty evaluation and empty mark-seen do not write");
            Check(!journal.IsUnlocked("unknown") && !journal.IsUnread(null) && journal.Progress("unknown") == 0, "unknown UI identities are safe queries");
            bool threw = false; try { journal.Evaluate(null, true); } catch (ArgumentNullException) { threw = true; }
            Check(threw && journal.Revision == 0 && !File.Exists(file), "null facts fail without mutation");
            // These masks, best stars and observations can be proven from old saves.
            // Lifetime pickup counts cannot be reconstructed from those masks.
            var historical = new OceanAchievementFacts { ShellSpecies = 10, FishSpecies = 5, ObservedSpecies = 12,
                CompletedLevels = 20, ThreeStarLevels = 7, TotalStars = 54, CompletedBiomes = 1, SharkCaught = true };
            IReadOnlyList<string> retroactive = journal.Evaluate(historical, false);
            Check(retroactive.Count > 0 && journal.IsUnlocked("shell-species-10") && journal.IsUnlocked("levels-20") &&
                journal.IsUnlocked("shark-caught"), "reliable old collection and level facts backfill achievements");
            Check(journal.UnreadCount == 0 && !journal.IsUnread("shell-species-10"), "boot backfill produces no unread notifications");
            Check(journal.Progress("shell-total-50") == 0 && journal.Progress("fish-total-100") == 0 && journal.Progress("coins-1") == 0,
                "historical species/stars do not invent lifetime collection or coin totals");
            var restored = new OceanAchievementJournal(directory);
            Check(restored.Writable && restored.UnlockedCount == journal.UnlockedCount && restored.UnreadCount == 0 && restored.Revision == journal.Revision,
                "backfill persists without changing notification state on reload");
            AssertNoRewrite(restored, historical, false, file);
            AssertNoRewrite(restored, historical, true, file);
            Check(restored.UnreadCount == 0, "later notify=true cannot replay old retroactive unlocks");
        }

        static void LiveProgressAndSeen(string root)
        {
            string directory = Path.Combine(root, "live-progress"), file = FileIn(directory);
            var journal = new OceanAchievementJournal(directory);
            var facts = new OceanAchievementFacts { ShellSpecies = 1 };
            IReadOnlyList<string> first = journal.Evaluate(facts, true);
            Check(first.Count == 1 && first[0] == "shell-species-1" && journal.UnlockedCount == 1 && journal.UnreadCount == 1,
                "a live first pickup unlocks once and is unread only after save");
            long firstRevision = journal.Revision;
            facts.ShellSpecies = 3;
            Check(journal.Evaluate(facts, true).Count == 0 && journal.Progress("shell-species-5") == 3 && journal.UnreadCount == 1 && journal.Revision == firstRevision + 1,
                "partial progress is durable without a false unlock/toast");
            Check(File.Exists(file + ".backup"), "second atomic commit keeps the previous verified primary as backup");
            var restored = new OceanAchievementJournal(directory);
            Check(restored.Writable && restored.Progress("shell-species-5") == 3 && restored.IsUnread("shell-species-1"), "partial progress and unread survive reload");
            facts.FishSpecies = 1;
            IReadOnlyList<string> backfill = restored.Evaluate(facts, false);
            Check(backfill.Count == 1 && restored.IsUnlocked("fish-species-1") && !restored.IsUnread("fish-species-1") && restored.IsUnread("shell-species-1"),
                "silent backfill preserves previously unseen live achievements");
            AssertNoRewrite(restored, new OceanAchievementFacts(), false, file);
            Check(restored.Progress("shell-species-5") == 3 && restored.IsUnlocked("shell-species-1"), "reset/regressed facts cannot revoke progress or unlocks");
            long beforeSeen = restored.Revision;
            Check(restored.MarkAllSeen() && restored.UnreadCount == 0 && restored.UnlockedCount == 2 && restored.Revision == beforeSeen + 1,
                "explicit mark-seen commits unread changes without relocking");
            string bytes = File.ReadAllText(file, Utf8); DateTime marker = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(file, marker);
            Check(restored.MarkAllSeen() && File.ReadAllText(file, Utf8) == bytes && File.GetLastWriteTimeUtc(file) == marker,
                "mark-seen with no unread does not rewrite the file");
            var seen = new OceanAchievementJournal(directory);
            Check(seen.Writable && seen.UnreadCount == 0 && seen.UnlockedCount == 2 && seen.Progress("shell-species-5") == 3, "seen state persists independently of current facts");
        }

        static void SaveFailureRollback(string root)
        {
            string blocked = Path.Combine(root, "blocked-directory");
            var journal = new OceanAchievementJournal(blocked);
            File.WriteAllText(blocked, "not a directory", Utf8);
            Check(journal.Evaluate(new OceanAchievementFacts { ShellSpecies = 1 }, true).Count == 0 && journal.UnlockedCount == 0 &&
                journal.UnreadCount == 0 && journal.Progress("shell-species-5") == 0 && journal.Revision == 0 && journal.LastError.Length > 0,
                "failed real filesystem write rolls back unlock, partial progress, unread and revision");
            Check(File.ReadAllText(blocked, Utf8) == "not a directory", "failed save preserves the blocking file");
            File.Delete(blocked);
            Check(journal.Evaluate(new OceanAchievementFacts { ShellSpecies = 1 }, true).Count == 1 && journal.IsUnread("shell-species-1"),
                "transient write failure may be retried without a phantom prior unlock");

            string seenDirectory = Path.Combine(root, "mark-seen-failure"), file = FileIn(seenDirectory);
            var unseen = new OceanAchievementJournal(seenDirectory);
            unseen.Evaluate(new OceanAchievementFacts { FishSpecies = 1 }, true);
            string before = File.ReadAllText(file, Utf8); long revision = unseen.Revision;
            Directory.CreateDirectory(file + ".backup");
            Check(!unseen.MarkAllSeen() && unseen.UnreadCount == 1 && unseen.IsUnread("fish-species-1") && unseen.Revision == revision,
                "blocked backup generation cannot create phantom mark-seen state");
            Check(File.ReadAllText(file, Utf8) == before && Directory.GetFiles(seenDirectory, "*.tmp-*").Length == 0,
                "failed mark-seen preserves the primary and leaves no partial temp files");
            Directory.Delete(file + ".backup");
            Check(new OceanAchievementJournal(seenDirectory).IsUnread("fish-species-1"), "unseen unlock remains after failed write and reload");
        }

        static void CorruptionAndFuturePreservation(string root)
        {
            string[] mutations = { "truncated", "checksum", "schema", "catalog", "unknown-id", "unknown-field", "duplicate-id", "invalid-unread", "oversized" };
            foreach (string mutation in mutations)
            {
                string directory = Path.Combine(root, "preserve-" + mutation), file = FileIn(directory);
                var journal = new OceanAchievementJournal(directory);
                journal.Evaluate(new OceanAchievementFacts { ShellSpecies = 1 }, true);
                journal.Evaluate(new OceanAchievementFacts { ShellSpecies = 1, FishSpecies = 1 }, true);
                if (mutation == "truncated") File.WriteAllText(file, "{broken", Utf8);
                else if (mutation == "oversized") File.WriteAllText(file, new string('x', 131073), Utf8);
                else if (mutation == "checksum") File.WriteAllText(file, File.ReadAllText(file, Utf8).Replace("sha256", "sha255"), Utf8);
                else Mutate(file, body => {
                    if (mutation == "schema") body["schema"] = 99;
                    else if (mutation == "catalog") body["catalogVersion"] = 99;
                    else if (mutation == "unknown-field") body["futureData"] = true;
                    else
                    {
                        var rows = OceanLeaderboardJson.Array(body, "entries");
                        var first = OceanLeaderboardJson.Object(rows[0]);
                        if (mutation == "unknown-id") first["id"] = "future-achievement-identity";
                        if (mutation == "duplicate-id") OceanLeaderboardJson.Object(rows[1])["id"] = first["id"];
                        if (mutation == "invalid-unread") { first["progress"] = 0; first["unlocked"] = false; first["unread"] = true; }
                    }
                });
                string primary = File.ReadAllText(file, Utf8), backup = File.ReadAllText(file + ".backup", Utf8);
                var protectedJournal = new OceanAchievementJournal(directory);
                Check(!protectedJournal.Writable && protectedJournal.LastError.Length > 0, "corrupt/future data becomes read-only: " + mutation);
                Check(protectedJournal.IsUnlocked("shell-species-1") && protectedJournal.Evaluate(MaximumFacts(), true).Count == 0 && !protectedJournal.MarkAllSeen(),
                    "verified backup may be displayed but is never silently rewritten: " + mutation);
                Check(File.ReadAllText(file, Utf8) == primary && File.ReadAllText(file + ".backup", Utf8) == backup,
                    "both generations preserved byte-for-byte: " + mutation);
            }
            string backupOnly = Path.Combine(root, "backup-only"), backupFile = FileIn(backupOnly);
            var origin = new OceanAchievementJournal(backupOnly);
            origin.Evaluate(new OceanAchievementFacts { ShellSpecies = 1 }, true);
            File.Move(backupFile, backupFile + ".backup");
            var readOnlyBackup = new OceanAchievementJournal(backupOnly);
            Check(!readOnlyBackup.Writable && readOnlyBackup.IsUnlocked("shell-species-1") && !File.Exists(backupFile), "missing primary does not promote backup without recovery approval");

            string futureBackup = Path.Combine(root, "future-backup"), futureFile = FileIn(futureBackup);
            var future = new OceanAchievementJournal(futureBackup);
            future.Evaluate(new OceanAchievementFacts { ShellSpecies = 1 }, true);
            File.Copy(futureFile, futureFile + ".backup");
            Mutate(futureFile + ".backup", body => body["schema"] = 999);
            string preserved = File.ReadAllText(futureFile + ".backup", Utf8);
            var guarded = new OceanAchievementJournal(futureBackup);
            Check(!guarded.Writable && guarded.IsUnlocked("shell-species-1") && guarded.Evaluate(MaximumFacts(), true).Count == 0 &&
                File.ReadAllText(futureFile + ".backup", Utf8) == preserved, "even a valid primary cannot overwrite a future-format backup");
        }

        static void GenerationConflicts(string root)
        {
            string directory = Path.Combine(root, "concurrent-generations"), file = FileIn(directory);
            var first = new OceanAchievementJournal(directory);
            first.Evaluate(new OceanAchievementFacts { ShellSpecies = 1 }, true);
            var stale = new OceanAchievementJournal(directory);
            first.Evaluate(new OceanAchievementFacts { ShellSpecies = 1, FishSpecies = 1 }, true);
            string newer = File.ReadAllText(file, Utf8); long previous = stale.Revision;
            Check(stale.Evaluate(new OceanAchievementFacts { ObservedSpecies = 1 }, true).Count == 0 && !stale.Writable && stale.Revision == previous &&
                !stale.IsUnlocked("observe-1") && File.ReadAllText(file, Utf8) == newer,
                "stale journal cannot overwrite a newer committed generation or publish unlocks");
            Check(new OceanAchievementJournal(directory).IsUnlocked("fish-species-1"), "newer achievement generation remains loadable");
        }

        static void OtherFilesUntouched(string root)
        {
            string directory = Path.Combine(root, "independent-storage"); Directory.CreateDirectory(directory);
            string[] names = { "ocean-campaign.json", "ocean-leaderboard.json", "ocean-naturalist.json", "ocean-discoveries.json" };
            foreach (string name in names) File.WriteAllText(Path.Combine(directory, name), "unchanged-" + name, Utf8);
            var journal = new OceanAchievementJournal(directory);
            Check(journal.Evaluate(MaximumFacts(), true).Count == 31 && journal.UnlockedCount == 31 && journal.UnreadCount == 31 &&
                journal.CurrentUnlockedCount == 30 && journal.HistoricalUnlockedCount == 1,
                "maximum edition fixtures include thirty current achievements and one historical honour exactly once");
            Check(journal.MarkAllSeen() && new OceanAchievementJournal(directory).UnlockedCount == 31, "all achievements and seen state persist");
            foreach (string name in names) Check(File.ReadAllText(Path.Combine(directory, name), Utf8) == "unchanged-" + name,
                "achievement storage never changes scoring, campaign or naturalist files: " + name);
        }

        static void AssertNoRewrite(OceanAchievementJournal journal, OceanAchievementFacts facts, bool notify, string file)
        {
            string before = File.ReadAllText(file, Utf8); long revision = journal.Revision;
            DateTime marker = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc); File.SetLastWriteTimeUtc(file, marker);
            Check(journal.Evaluate(facts, notify).Count == 0 && journal.Revision == revision && File.ReadAllText(file, Utf8) == before &&
                File.GetLastWriteTimeUtc(file) == marker, "equivalent/regressed snapshot does not rewrite or replay unlocks");
        }
        static OceanAchievementFacts FactsFor(string id, long value)
        {
            int count = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, value));
            var facts = new OceanAchievementFacts();
            if (id.StartsWith("shell-species-", StringComparison.Ordinal)) facts.ShellSpecies = count;
            else if (id.StartsWith("fish-species-", StringComparison.Ordinal)) facts.FishSpecies = count;
            else if (id == "shell-total-50") facts.ShellTotal = value;
            else if (id == "fish-total-100") facts.FishTotal = value;
            else if (id == "observe-all") facts.ObservedSpecies = count;
            else if (id.StartsWith("observe-", StringComparison.Ordinal)) facts.ObservedSpecies = count;
            else if (id.StartsWith("biomes-", StringComparison.Ordinal)) facts.CompletedBiomes = count;
            else if (id.StartsWith("levels-", StringComparison.Ordinal)) facts.CompletedLevels = count;
            else if (id.StartsWith("coins-", StringComparison.Ordinal)) facts.CoinTotal = value;
            else if (id.StartsWith("three-stars-", StringComparison.Ordinal)) facts.ThreeStarLevels = count;
            else if (id == "stars-100") facts.TotalStars = count;
            else if (id == "shark-caught") facts.SharkCaught = value > 0;
            else if (id.StartsWith("mechanics-", StringComparison.Ordinal)) facts.CompletedMechanics = count;
            else if (id == "campaign-finished") facts.CampaignFinished = value > 0;
            else throw new InvalidOperationException("Uncovered achievement identity: " + id);
            return facts;
        }
        static OceanAchievementFacts MaximumFacts() => new OceanAchievementFacts { ShellSpecies = 10, FishSpecies = 10,
            ShellTotal = long.MaxValue, FishTotal = long.MaxValue, CoinTotal = long.MaxValue, ObservedSpecies = 31,
            CompletedLevels = 100, ThreeStarLevels = 100, TotalStars = 300, CompletedBiomes = 6, CompletedMechanics = 16,
            SharkCaught = true, CampaignFinished = true };
        static OceanAchievementDefinition Find(string id)
        { foreach (OceanAchievementDefinition definition in OceanAchievementCatalog.All) if (definition.Id == id) return definition; throw new InvalidOperationException(id); }
        static bool Contains(IReadOnlyList<string> rows, string id)
        { foreach (string row in rows) if (row == id) return true; return false; }
        static string FileIn(string directory) => Path.Combine(directory, OceanAchievementJournal.FileName);
        static void Mutate(string file, Action<Dictionary<string, object>> change)
        {
            var envelope = OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(File.ReadAllText(file, Utf8)));
            var body = OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(OceanLeaderboardJson.Text(envelope, "body")));
            change(body); string text = OceanLeaderboardJson.Write(body); envelope["body"] = text;
            using (var sha = SHA256.Create()) envelope["sha256"] = BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(file, OceanLeaderboardJson.Write(envelope), Utf8);
        }
        static void Check(bool condition, string detail)
        { LastAssertionCount++; if (!condition) throw new InvalidOperationException("Ocean achievements: " + detail); }
        static void Print(string message)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.Log(message);
#else
            Console.WriteLine(message);
#endif
        }
    }
}
#endif

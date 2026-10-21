using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SwanUnderwater
{
    public enum OceanSaveLoadStatus { NewSave, Loaded, RecoveredBackup, UnsupportedVersion, Corrupt, IoError }

    /// <summary>Version-five checksummed JSON; authenticated v1/v2/v3/v4 histories migrate without retrospective rewards.</summary>
    public static class OceanSaveStore
    {
        public const string FileName = "ocean-campaign.json";
        public const string BackupFileName = "ocean-campaign.backup.json";
        const int MaximumBytes = 262144;
        static readonly object Gate = new object();
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        enum ReadStatus { Missing, Valid, Unsupported, Invalid, IoError }

        public static bool TryLoad(string directory, out OceanSaveData data, out string error)
        { return TryLoad(directory, out data, out OceanSaveLoadStatus ignored, out error); }

        public static bool TryLoad(string directory, out OceanSaveData data, out OceanSaveLoadStatus status, out string error)
        {
            lock (Gate)
            {
                data = null; status = OceanSaveLoadStatus.IoError; error = null;
                try
                {
                    string root = Resolve(directory);
                    ReadStatus primary = Read(Path.Combine(root, FileName), out data, out string mainError);
                    if (primary == ReadStatus.Valid) { status = OceanSaveLoadStatus.Loaded; return true; }
                    // A readable newer/older schema is not corruption: never roll it back to an older backup.
                    if (primary == ReadStatus.Unsupported)
                    { status = OceanSaveLoadStatus.UnsupportedVersion; error = mainError; data = null; return false; }
                    ReadStatus backup = Read(Path.Combine(root, BackupFileName), out data, out string backupError);
                    if (backup == ReadStatus.Valid)
                    { status = OceanSaveLoadStatus.RecoveredBackup; error = "Recovered verified backup; primary: " + mainError; return true; }
                    data = null;
                    if (primary == ReadStatus.Missing && backup == ReadStatus.Missing)
                    { data = new OceanSaveData(); status = OceanSaveLoadStatus.NewSave; return true; }
                    status = backup == ReadStatus.Unsupported ? OceanSaveLoadStatus.UnsupportedVersion :
                        primary == ReadStatus.IoError || backup == ReadStatus.IoError ? OceanSaveLoadStatus.IoError : OceanSaveLoadStatus.Corrupt;
                    error = "No loadable save. Primary: " + mainError + "; backup: " + backupError; return false;
                }
                catch (Exception exception) { error = exception.Message; return false; }
            }
        }

        /// <summary>Player retry reads existing authenticated files only; missing files never become a replacement new game.</summary>
        public static bool TryReloadExistingReadOnly(string directory, out OceanSaveData data, out OceanSaveLoadStatus status, out string error)
        {
            if (!TryLoad(directory, out data, out status, out error)) return false;
            if (status == OceanSaveLoadStatus.Loaded || status == OceanSaveLoadStatus.RecoveredBackup) return true;
            data = null;
            error = "Original save files are missing; retry cannot create a new save.";
            return false;
        }

        public static bool TrySave(string directory, OceanSaveData data, out string error)
        {
            lock (Gate)
            {
                error = null; string temporary = null, backupTemporary = null;
                try
                {
                    if (!OceanCampaignState.Validate(data, out error)) return false;
                    // Snapshot before serializing: callers continue using their live campaign after this call.
                    var snapshot = OceanCampaignState.Copy(data);
                    OceanCampaignState.MigrateLegacySharkProgress(snapshot);
                    string json = Encode(snapshot);
                    string root = Resolve(directory); Directory.CreateDirectory(root);
                    string primaryPath = Path.Combine(root, FileName), backupPath = Path.Combine(root, BackupFileName);
                    ReadStatus primary = Read(primaryPath, out OceanSaveData oldPrimary, out string mainError);
                    ReadStatus backup = Read(backupPath, out OceanSaveData oldBackup, out string backupError);
                    if (primary == ReadStatus.Unsupported || backup == ReadStatus.Unsupported)
                    { error = "Refusing to overwrite an unsupported version; explicit migration required. " + mainError + " " + backupError; return false; }
                    if (primary == ReadStatus.IoError || backup == ReadStatus.IoError)
                    { error = "Cannot verify existing files. " + mainError + " " + backupError; return false; }
                    if (primary != ReadStatus.Valid && backup != ReadStatus.Valid &&
                        (primary != ReadStatus.Missing || backup != ReadStatus.Missing))
                    { error = "Refusing to overwrite unrecoverable save files; preserve them for explicit recovery."; return false; }
                    temporary = WriteTemporary(root, FileName, json);
                    if (Read(temporary, out OceanSaveData verified, out string verificationError) != ReadStatus.Valid)
                        throw new IOException("Temporary save failed verification: " + verificationError);
                    if (primary == ReadStatus.Missing)
                    {
                        // The first save has a verified recovery copy even if interrupted before its primary rename.
                        if (backup != ReadStatus.Valid)
                        {
                            backupTemporary = WriteTemporary(root, BackupFileName, json);
                            if (Read(backupTemporary, out verified, out verificationError) != ReadStatus.Valid)
                                throw new IOException("Temporary backup failed verification: " + verificationError);
                            if (File.Exists(backupPath)) File.Replace(backupTemporary, backupPath, null);
                            else File.Move(backupTemporary, backupPath);
                            backupTemporary = null;
                        }
                        File.Move(temporary, primaryPath);
                    }
                    else
                    {
                        // Same-directory replacement is atomic. A corrupt primary must never replace a valid backup.
                        File.Replace(temporary, primaryPath, primary == ReadStatus.Valid ? backupPath : null);
                    }
                    temporary = null;
                    if (Read(primaryPath, out verified, out verificationError) != ReadStatus.Valid)
                        throw new IOException("Committed primary failed verification: " + verificationError);
                    if (Read(backupPath, out verified, out verificationError) != ReadStatus.Valid)
                        throw new IOException("Committed backup failed verification: " + verificationError);
                    return true;
                }
                catch (Exception exception) { error = exception.GetType().Name + ": " + exception.Message; return false; }
                finally { DeleteOwnTemporary(temporary); DeleteOwnTemporary(backupTemporary); }
            }
        }
        static string Resolve(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A fixed save directory is required.", nameof(directory));
            string root = Path.GetFullPath(directory);
            if (File.Exists(root)) throw new IOException("Save directory is an existing file.");
            return root;
        }
        static string WriteTemporary(string root, string name, string json)
        {
            string path = Path.Combine(root, name + ".tmp-" + Guid.NewGuid().ToString("N"));
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(json);
            if (bytes.Length > MaximumBytes) throw new IOException("Save exceeds the bounded format size.");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                return path;
            }
            catch { DeleteOwnTemporary(path); throw; }
        }
        static void DeleteOwnTemporary(string path)
        { if (path != null) try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        static ReadStatus Read(string path, out OceanSaveData data, out string error)
        {
            data = null; error = "missing";
            try
            {
                if (new FileInfo(path).Length > MaximumBytes) throw new FormatException("Save exceeds size limit.");
                string json = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));
                var root = ObjectValue(new JsonReader(json).Read());
                int version = Integer(root, "version");
                if (version != 1 && version != 2 && version != 3 && version != 4 && version != 5) { error = "Unsupported save version " + version + "; explicit migration required."; return ReadStatus.Unsupported; }
                Keys(root, "version", "bestStars", "shellMask", "fishMask", "shark", "currentRun", "sha256");
                data = new OceanSaveData { version = version, bestStars = IntArray(root, "bestStars"), shellMask = Integer(root, "shellMask"),
                    fishMask = Integer(root, "fishMask"), shark = Boolean(root, "shark") };
                bool legacySpearBudget = false, legacyFailureReason = false, legacyLeaderboard = false, includeSharkProgress = false;
                if (root["currentRun"] != null)
                {
                    var run = ObjectValue(root["currentRun"]);
                    legacySpearBudget = !run.ContainsKey("spearShotsFired");
                    legacyFailureReason = !run.ContainsKey("ammoExhausted");
                    legacyLeaderboard = !run.ContainsKey("leaderboardRunId");
                    includeSharkProgress = version >= 3 && run.ContainsKey("sharkProgress");
                    if ((legacySpearBudget && !legacyFailureReason) || (!legacyLeaderboard && (legacySpearBudget || legacyFailureReason)))
                        throw new FormatException("Unrecognized partial save schema.");
                    if (version >= 2 && (legacySpearBudget || legacyFailureReason || legacyLeaderboard))
                        throw new FormatException("Version two requires the complete campaign schema.");
                    var expected = new List<string> { "level", "remainingSeconds", "shellMask", "fishMask", "coinCollected", "toolOwned", "sharkHits", "sharkCaught", "consumedPickupIds", "completed", "failed", "timedOut" };
                    if (!legacySpearBudget) expected.Add("spearShotsFired");
                    if (!legacyFailureReason) expected.Add("ammoExhausted");
                    if (!legacyLeaderboard) expected.AddRange(new[] { "leaderboardRunId", "leaderboardFishBaseline", "leaderboardCoinBaseline", "leaderboardSharkBaseline" });
                    if (version >= 2) expected.AddRange(new[] { "coinMask", "leaderboardCoinMaskBaseline" });
                    if (version >= 3) expected.Add("leaderboardShells");
                    if (version >= 4 && !includeSharkProgress) throw new FormatException("Version four requires individual shark progress.");
                    if (includeSharkProgress) expected.Add("sharkProgress");
                    if (version >= 4) expected.AddRange(new[] { "mechanicProgress", "mechanicStage", "mechanicMistakes", "mechanicElapsedSeconds", "mechanicEventIds" });
                    if (version >= 5) expected.Add("pendingSpearRecovery");
                    Keys(run, expected.ToArray());
                    data.currentRun = new OceanLevelRun { level = Integer(run, "level"), remainingSeconds = Number(run, "remainingSeconds"),
                        shellMask = Integer(run, "shellMask"), fishMask = Integer(run, "fishMask"), coinCollected = Boolean(run, "coinCollected"),
                        toolOwned = Boolean(run, "toolOwned"), sharkHits = Integer(run, "sharkHits"), sharkCaught = Boolean(run, "sharkCaught"),
                        spearShotsFired = legacySpearBudget ? Integer(run, "sharkHits") : Integer(run, "spearShotsFired"),
                        consumedPickupIds = new List<int>(IntArray(run, "consumedPickupIds")), completed = Boolean(run, "completed"),
                        failed = Boolean(run, "failed"), timedOut = Boolean(run, "timedOut"),
                        ammoExhausted = !legacyFailureReason && Boolean(run, "ammoExhausted"),
                        leaderboardRunId = legacyLeaderboard ? "" : StringValue(run, "leaderboardRunId"),
                        leaderboardFishBaseline = legacyLeaderboard ? 0 : Integer(run, "leaderboardFishBaseline"),
                        leaderboardCoinBaseline = !legacyLeaderboard && Boolean(run, "leaderboardCoinBaseline"),
                        leaderboardSharkBaseline = !legacyLeaderboard && Boolean(run, "leaderboardSharkBaseline"),
                        coinMask = version >= 2 ? Integer(run, "coinMask") : 0,
                        leaderboardCoinMaskBaseline = version >= 2 ? Integer(run, "leaderboardCoinMaskBaseline") : 0 };
                    if (version >= 4)
                    {
                        data.currentRun.pendingSpearRecovery = version >= 5 && Boolean(run, "pendingSpearRecovery");
                        data.currentRun.mechanicProgress = Integer(run, "mechanicProgress");
                        data.currentRun.mechanicStage = Integer(run, "mechanicStage");
                        data.currentRun.mechanicMistakes = Integer(run, "mechanicMistakes");
                        data.currentRun.mechanicElapsedSeconds = Number(run, "mechanicElapsedSeconds");
                        data.currentRun.mechanicEventIds = new List<int>(IntArray(run, "mechanicEventIds"));
                    }
                    if (version >= 3)
                    {
                        if (!(run["leaderboardShells"] is List<object> shells) || shells.Count > 4096) throw new FormatException("Invalid shell score list.");
                        foreach (object item in shells)
                        {
                            var shell = ObjectValue(item); Keys(shell, "pickupId", "species");
                            data.currentRun.leaderboardShells.Add(new OceanShellScore(Integer(shell, "pickupId"), Integer(shell, "species")));
                        }
                    }
                    if (includeSharkProgress)
                    {
                        if (!(run["sharkProgress"] is List<object> sharks) || sharks.Count > OceanCampaignState.MaximumSharks)
                            throw new FormatException("Invalid individual shark list.");
                        foreach (object item in sharks)
                        {
                            var shark = ObjectValue(item); Keys(shark, "id", "hits", "caught");
                            data.currentRun.sharkProgress.Add(new OceanSharkProgress(Integer(shark, "id"), Integer(shark, "hits"), Boolean(shark, "caught")));
                        }
                    }
                }
                // Authenticate the exact recognized schema before adding fields, coins or history slots.
                // Loading never rewrites either recovery generation.
                if (!(root["sha256"] is string checksum) || !string.Equals(checksum, Hash(Payload(data, !legacySpearBudget, !legacyFailureReason, !legacyLeaderboard, includeSharkProgress)), StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("Save checksum mismatch.");
                if (version == 1) MigrateVersionOne(data);
                // Existing consumed entities remain consumed; only future CollectShell calls add score records.
                if (data.version == 2) data.version = 3;
                if (data.version == 3) MigrateVersionThree(data);
                if (data.version == 4) MigrateVersionFour(data);
                if (!OceanCampaignState.Validate(data, out error)) { data = null; return ReadStatus.Invalid; }
                OceanCampaignState.MigrateLegacySharkProgress(data);
                error = null; return ReadStatus.Valid;
            }
            catch (FileNotFoundException) { data = null; error = "missing"; return ReadStatus.Missing; }
            catch (DirectoryNotFoundException) { data = null; error = "missing"; return ReadStatus.Missing; }
            catch (UnsupportedSchemaException e) { data = null; error = e.Message; return ReadStatus.Unsupported; }
            catch (IOException e) { data = null; error = e.Message; return ReadStatus.IoError; }
            catch (UnauthorizedAccessException e) { data = null; error = e.Message; return ReadStatus.IoError; }
            catch (Exception e) { data = null; error = e.Message; return ReadStatus.Invalid; }
        }
        static void MigrateVersionOne(OceanSaveData data)
        {
            if (data.bestStars.Length != 5 || data.fishMask < 0 || (data.fishMask & ~15) != 0)
                throw new FormatException("Version one must retain its original five levels and four fish species.");
            var run = data.currentRun;
            if (run != null)
            {
                if (run.level < 1 || run.level > 5 || (run.coinCollected && run.consumedPickupIds.Count == 0))
                    throw new FormatException("Invalid version-one run or coin evidence.");
                // Keep old shell/coin entity IDs verbatim, and add the new slot-zero alias.
                // The leaderboard run ID and old coin event hash remain unchanged.
                run.coinMask = run.coinCollected ? 1 : 0;
                run.leaderboardCoinMaskBaseline = run.leaderboardCoinBaseline ? 1 : 0;
                if (run.coinCollected && !run.consumedPickupIds.Contains(OceanLevelCatalog.CoinIdBase)) run.consumedPickupIds.Add(OceanLevelCatalog.CoinIdBase);
            }
            int[] stars = new int[OceanLevelCatalog.LegacyLevelCount];
            Array.Copy(data.bestStars, stars, 5); data.bestStars = stars; data.version = 2;
        }
        static void MigrateVersionThree(OceanSaveData data)
        {
            if (data.bestStars.Length != OceanLevelCatalog.LegacyLevelCount ||
                (data.currentRun != null && (data.currentRun.level < 1 || data.currentRun.level > OceanLevelCatalog.LegacyLevelCount)))
                throw new FormatException("Version three must retain its original twenty-level history.");
            var stars = new int[OceanLevelCatalog.Count];
            Array.Copy(data.bestStars, stars, OceanLevelCatalog.LegacyLevelCount);
            data.bestStars = stars; data.version = 4;
        }
        static void MigrateVersionFour(OceanSaveData data)
        {
            // Version four existed only in this local acceleration milestone; it had no final-tier spear objective.
            // Preserve all history, discoveries, score evidence and time. Only the newly extended active goal changes.
            var run = data.currentRun;
            if (run != null && run.level >= 96 && run.level <= 100)
            {
                if (run.toolOwned || run.sharkHits != 0 || run.sharkCaught || run.spearShotsFired != 0 ||
                    (run.sharkProgress != null && run.sharkProgress.Count != 0))
                    throw new FormatException("Version-four final-tier saves cannot contain unrecognized weapon progress.");
                run.completed = false;
                run.failed = run.timedOut;
            }
            data.version = 5;
        }
        static string Encode(OceanSaveData data)
        {
            string payload = Payload(data);
            return payload.Substring(0, payload.Length - 1) + ",\"sha256\":\"" + Hash(payload) + "\"}\n";
        }
        static string Hash(string value)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        static string Payload(OceanSaveData data, bool includeSpearBudget = true, bool includeFailureReason = true, bool includeLeaderboard = true, bool includeSharkProgress = true)
        {
            var text = new StringBuilder(512);
            text.Append("{\"version\":").Append(data.version).Append(",\"bestStars\":"); AppendArray(text, data.bestStars);
            text.Append(",\"shellMask\":").Append(data.shellMask).Append(",\"fishMask\":").Append(data.fishMask)
                .Append(",\"shark\":").Append(Bool(data.shark)).Append(",\"currentRun\":");
            var r = data.currentRun;
            if (r == null) text.Append("null");
            else
            {
                text.Append("{\"level\":").Append(r.level).Append(",\"remainingSeconds\":").Append(r.remainingSeconds.ToString("G9", Invariant))
                    .Append(",\"shellMask\":").Append(r.shellMask).Append(",\"fishMask\":").Append(r.fishMask)
                    .Append(",\"coinCollected\":").Append(Bool(r.coinCollected)).Append(",\"toolOwned\":").Append(Bool(r.toolOwned))
                    .Append(",\"sharkHits\":").Append(r.sharkHits).Append(",\"sharkCaught\":").Append(Bool(r.sharkCaught));
                if (includeSpearBudget) text.Append(",\"spearShotsFired\":").Append(r.spearShotsFired);
                text.Append(",\"consumedPickupIds\":"); AppendArray(text, r.consumedPickupIds);
                text.Append(",\"completed\":").Append(Bool(r.completed)).Append(",\"failed\":").Append(Bool(r.failed))
                    .Append(",\"timedOut\":").Append(Bool(r.timedOut));
                if (includeFailureReason) text.Append(",\"ammoExhausted\":").Append(Bool(r.ammoExhausted));
                if (includeLeaderboard)
                    text.Append(",\"leaderboardRunId\":\"").Append(r.leaderboardRunId ?? "").Append("\"")
                        .Append(",\"leaderboardFishBaseline\":").Append(r.leaderboardFishBaseline)
                        .Append(",\"leaderboardCoinBaseline\":").Append(Bool(r.leaderboardCoinBaseline))
                        .Append(",\"leaderboardSharkBaseline\":").Append(Bool(r.leaderboardSharkBaseline));
                if (data.version >= 2)
                    text.Append(",\"coinMask\":").Append(r.coinMask)
                        .Append(",\"leaderboardCoinMaskBaseline\":").Append(r.leaderboardCoinMaskBaseline);
                if (data.version >= 3)
                {
                    text.Append(",\"leaderboardShells\":[");
                    for (int i = 0; i < r.leaderboardShells.Count; i++)
                    {
                        if (i > 0) text.Append(',');
                        text.Append("{\"pickupId\":").Append(r.leaderboardShells[i].pickupId)
                            .Append(",\"species\":").Append(r.leaderboardShells[i].species).Append('}');
                    }
                    text.Append(']');
                    if (includeSharkProgress)
                    {
                        text.Append(",\"sharkProgress\":[");
                        for (int i = 0; i < r.sharkProgress.Count; i++)
                        {
                            if (i > 0) text.Append(',');
                            var shark = r.sharkProgress[i];
                            text.Append("{\"id\":").Append(shark.id).Append(",\"hits\":").Append(shark.hits)
                                .Append(",\"caught\":").Append(Bool(shark.caught)).Append('}');
                        }
                        text.Append(']');
                    }
                }
                if (data.version >= 4)
                {
                    text.Append(",\"mechanicProgress\":").Append(r.mechanicProgress)
                        .Append(",\"mechanicStage\":").Append(r.mechanicStage)
                        .Append(",\"mechanicMistakes\":").Append(r.mechanicMistakes)
                        .Append(",\"mechanicElapsedSeconds\":").Append(r.mechanicElapsedSeconds.ToString("G9", Invariant))
                        .Append(",\"mechanicEventIds\":"); AppendArray(text, r.mechanicEventIds);
                }
                if (data.version >= 5) text.Append(",\"pendingSpearRecovery\":").Append(Bool(r.pendingSpearRecovery));
                text.Append('}');
            }
            return text.Append('}').ToString();
        }
        static string Bool(bool value) => value ? "true" : "false";
        static void AppendArray(StringBuilder text, IEnumerable<int> values)
        {
            text.Append('['); bool first = true;
            foreach (int value in values) { if (!first) text.Append(','); text.Append(value.ToString(Invariant)); first = false; }
            text.Append(']');
        }
        static Dictionary<string, object> ObjectValue(object value)
        { return value as Dictionary<string, object> ?? throw new FormatException("Expected JSON object."); }
        sealed class UnsupportedSchemaException : Exception
        { public UnsupportedSchemaException(string message) : base(message) { } }
        static void Keys(Dictionary<string, object> obj, params string[] expected)
        {
            foreach (string key in obj.Keys) if (Array.IndexOf(expected, key) < 0)
                throw new UnsupportedSchemaException("Unknown save field " + key + "; explicit schema migration required.");
            if (obj.Count != expected.Length) throw new FormatException("Unexpected or missing save fields; explicit schema migration required.");
            foreach (string key in expected) if (!obj.ContainsKey(key)) throw new FormatException("Missing field: " + key);
        }
        static double Numeric(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out object value) || !(value is double number) || double.IsNaN(number) || double.IsInfinity(number)) throw new FormatException("Invalid number: " + key);
            return number;
        }
        static int Integer(Dictionary<string, object> obj, string key)
        {
            double n = Numeric(obj, key); if (n < int.MinValue || n > int.MaxValue || n != Math.Truncate(n)) throw new FormatException("Invalid integer: " + key);
            return (int)n;
        }
        static float Number(Dictionary<string, object> obj, string key) => (float)Numeric(obj, key);
        static string StringValue(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out object value) || !(value is string result)) throw new FormatException("Invalid string: " + key);
            return result;
        }
        static bool Boolean(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out object value) || !(value is bool result)) throw new FormatException("Invalid boolean: " + key);
            return result;
        }
        static int[] IntArray(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out object value) || !(value is List<object> values) || values.Count > 4096) throw new FormatException("Invalid array: " + key);
            var result = new int[values.Count];
            for (int i = 0; i < result.Length; i++)
            {
                if (!(values[i] is double n) || n < int.MinValue || n > int.MaxValue || n != Math.Truncate(n)) throw new FormatException("Invalid integer array element.");
                result[i] = (int)n;
            }
            return result;
        }

        // Bounded JSON reader for a numeric/boolean campaign schema. Rejects duplicate keys and trailing data.
        // No reflection, runtime code generation, package dependency or Unity native serializer is needed on IL2CPP.
        sealed class JsonReader
        {
            readonly string text; int at, values;
            public JsonReader(string text) { this.text = text; }
            public object Read() { object result = Value(0); Space(); if (at != text.Length) throw new FormatException("Trailing JSON data."); return result; }
            void Space() { while (at < text.Length && (text[at] == ' ' || text[at] == '\t' || text[at] == '\r' || text[at] == '\n')) at++; }
            bool Take(char c) { Space(); if (at < text.Length && text[at] == c) { at++; return true; } return false; }
            void Need(char c) { if (!Take(c)) throw new FormatException("Expected JSON punctuation: " + c); }
            object Value(int depth)
            {
                if (depth > 16 || ++values > 10000) throw new FormatException("JSON nesting/item limit exceeded.");
                Space(); if (at >= text.Length) throw new FormatException("Incomplete JSON.");
                if (text[at] == '{')
                {
                    at++; var obj = new Dictionary<string, object>(StringComparer.Ordinal); if (Take('}')) return obj;
                    do { string key = String(); Need(':'); if (obj.ContainsKey(key)) throw new FormatException("Duplicate JSON key."); obj.Add(key, Value(depth + 1)); } while (Take(','));
                    Need('}'); return obj;
                }
                if (text[at] == '[')
                {
                    at++; var array = new List<object>(); if (Take(']')) return array;
                    do { if (array.Count >= 4096) throw new FormatException("JSON array limit exceeded."); array.Add(Value(depth + 1)); } while (Take(','));
                    Need(']'); return array;
                }
                if (text[at] == '"') return String();
                foreach (string literal in new[] { "true", "false", "null" })
                    if (at + literal.Length <= text.Length && string.CompareOrdinal(text, at, literal, 0, literal.Length) == 0)
                    { at += literal.Length; if (literal == "null") return null; return literal == "true"; }
                int start = at; if (text[at] == '-') at++;
                if (at >= text.Length || text[at] < '0' || text[at] > '9') throw new FormatException("Invalid JSON number.");
                if (text[at] == '0') at++; else while (at < text.Length && char.IsDigit(text[at])) at++;
                if (at < text.Length && text[at] == '.') { at++; Digits(); }
                if (at < text.Length && (text[at] == 'e' || text[at] == 'E')) { at++; if (at < text.Length && (text[at] == '+' || text[at] == '-')) at++; Digits(); }
                if (!double.TryParse(text.Substring(start, at - start), NumberStyles.Float, Invariant, out double number) || double.IsNaN(number) || double.IsInfinity(number)) throw new FormatException("Invalid JSON number.");
                return number;
            }
            void Digits() { int start = at; while (at < text.Length && text[at] >= '0' && text[at] <= '9') at++; if (at == start) throw new FormatException("Expected number digits."); }
            string String()
            {
                Need('"'); var result = new StringBuilder();
                while (at < text.Length)
                {
                    char c = text[at++]; if (c == '"') return result.ToString(); if (c < 32) throw new FormatException("Control character in JSON string.");
                    if (c != '\\') { result.Append(c); continue; }
                    if (at == text.Length) throw new FormatException("Incomplete JSON escape.");
                    char escape = text[at++];
                    switch (escape)
                    {
                        case '"': case '\\': case '/': result.Append(escape); break;
                        case 'b': result.Append('\b'); break; case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break; case 'r': result.Append('\r'); break; case 't': result.Append('\t'); break;
                        case 'u':
                            if (at + 4 > text.Length || !ushort.TryParse(text.Substring(at, 4), NumberStyles.HexNumber, Invariant, out ushort code)) throw new FormatException("Invalid Unicode escape.");
                            result.Append((char)code); at += 4; break;
                        default: throw new FormatException("Invalid JSON escape.");
                    }
                }
                throw new FormatException("Unterminated JSON string.");
            }
        }
    }
}

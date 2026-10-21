using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SwanUnderwater
{
    /// <summary>Only authenticated collection/history/ledger snapshots belong here; missing historical counts stay zero.</summary>
    public sealed class OceanAchievementFacts
    {
        public int ShellSpecies, FishSpecies, ObservedSpecies, ObservedTarget = OceanSpeciesCatalog.AvailableCount;
        public int CompletedLevels, ThreeStarLevels, TotalStars, CompletedBiomes, CompletedMechanics;
        public long ShellTotal, FishTotal, CoinTotal;
        public bool SharkCaught, CampaignFinished;
    }

    public sealed class OceanAchievementDefinition
    {
        public readonly string Id, Name, Description, Category;
        public readonly long Target;
        public bool Historical => Id == "observe-all";
        readonly Func<OceanAchievementFacts, long> measure;

        internal OceanAchievementDefinition(string id, string name, string description, string category,
            long target, Func<OceanAchievementFacts, long> measure)
        { Id = id; Name = name; Description = description; Category = category; Target = target; this.measure = measure; }

        public long Progress(OceanAchievementFacts facts)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            return Math.Min(Target, Math.Max(0L, measure(facts)));
        }
    }

    /// <summary>Append-only achievement identities. No achievement changes leaderboard scoring or campaign state.</summary>
    public static class OceanAchievementCatalog
    {
        static readonly OceanAchievementDefinition[] definitions = {
            D("shell-species-1", "第一枚贝壳", "收集第一种贝壳", "收集", 1, f => f.ShellSpecies),
            D("shell-species-5", "五彩贝藏", "收集五种不同的贝壳", "收集", 5, f => f.ShellSpecies),
            D("shell-species-10", "贝壳鉴赏家", "收齐十种不同的贝壳", "收集", 10, f => f.ShellSpecies),
            D("fish-species-1", "初识鱼群", "捕获第一种鱼", "收集", 1, f => f.FishSpecies),
            D("fish-species-5", "珊瑚伙伴", "捕获五种不同的鱼", "收集", 5, f => f.FishSpecies),
            D("fish-species-10", "鱼类收藏家", "捕获十种不同的鱼", "收集", 10, f => f.FishSpecies),
            D("shell-total-50", "拾贝达人", "累计收集五十枚贝壳", "收集", 50, f => f.ShellTotal),
            D("fish-total-100", "百鱼之约", "累计捕获一百条鱼", "收集", 100, f => f.FishTotal),
            D("observe-1", "海洋初见", "完成第一种海洋生物的观察", "探索", 1, f => f.ObservedSpecies),
            D("observe-10", "海底博物志", "观察十种不同的海洋生物", "探索", 10, f => f.ObservedSpecies),
            D("observe-20", "深蓝观察家", "观察二十种不同的海洋生物", "探索", 20, f => f.ObservedSpecies),
            // V1 is permanently the 31-species edition; later catalogs must not revoke or redefine this identity.
            D("observe-all", "万物入册", "观察本版图鉴的全部31种生物", "探索", 31, f => f.ObservedSpecies),
            D("biomes-3", "三域足迹", "在三类海域中完成关卡", "探索", 3, f => f.CompletedBiomes),
            D("biomes-5", "五海巡礼", "在五类海域中完成关卡", "探索", 5, f => f.CompletedBiomes),
            D("levels-1", "海底启航", "首次完成一个关卡", "关卡", 1, f => f.CompletedLevels),
            D("levels-5", "初阶探险家", "累计完成五个不同关卡", "关卡", 5, f => f.CompletedLevels),
            D("levels-20", "深海征服者", "累计完成二十个不同关卡", "关卡", 20, f => f.CompletedLevels),
            D("levels-50", "远洋行者", "累计完成五十个不同关卡", "关卡", 50, f => f.CompletedLevels),
            D("levels-100", "百关探险家", "完成全部一百个关卡", "关卡", 100, f => f.CompletedLevels),
            D("coins-1", "金光初现", "找到第一枚隐藏金币", "珍藏", 1, f => f.CoinTotal),
            D("coins-10", "海底寻金者", "累计找到十枚金币", "珍藏", 10, f => f.CoinTotal),
            D("coins-50", "黄金收藏家", "累计找到五十枚金币", "珍藏", 50, f => f.CoinTotal),
            D("three-stars-1", "完美首航", "首次获得一个关卡的三星评价", "珍藏", 1, f => f.ThreeStarLevels),
            D("three-stars-10", "十次闪耀", "在十个不同关卡获得三星", "珍藏", 10, f => f.ThreeStarLevels),
            D("stars-100", "星海百光", "历史最佳评价累计达到一百颗星", "珍藏", 100, f => f.TotalStars),
            D("shark-caught", "鲨鱼猎手", "完成一次鲨鱼捕获", "挑战", 1, f => f.SharkCaught ? 1 : 0),
            D("mechanics-1", "新玩法初探", "完成一种探险机制所在的关卡", "挑战", 1, f => f.CompletedMechanics),
            D("mechanics-8", "巧手探险家", "完成八种不同探险机制所在的关卡", "挑战", 8, f => f.CompletedMechanics),
            D("mechanics-16", "全能海洋行者", "完成全部十六种探险机制所在的关卡", "挑战", 16, f => f.CompletedMechanics),
            D("campaign-finished", "终章回响", "完成第一百关的最终挑战", "挑战", 1, f => f.CampaignFinished ? 1 : 0),
            // Append a new identity rather than changing the earned V1 31-category honour.
            D("observe-current-all", "万物入册", "观察当前图鉴的全部30种生物", "探索", 30, f => f.ObservedSpecies)
        };
        public static readonly IReadOnlyList<OceanAchievementDefinition> All = Array.AsReadOnly(definitions);
        public static int Count => definitions.Length;
        public static int CurrentCount => Count - 1;

        static OceanAchievementDefinition D(string id, string name, string description, string category, long target,
            Func<OceanAchievementFacts, long> measure) => new OceanAchievementDefinition(id, name, description, category, target, measure);
        internal static int Index(string id)
        {
            if (id == null) return -1;
            for (int i = 0; i < definitions.Length; i++) if (string.Equals(definitions[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }
    }

    /// <summary>
    /// A separate, checksummed local journal. A checksum detects damaged files, not hostile score tampering.
    /// Candidate state becomes visible only after the atomic primary-file commit. Corrupt/future/conflicting
    /// files stay read-only, including valid backups; no recovery silently overwrites player evidence.
    /// </summary>
    public sealed class OceanAchievementJournal
    {
        public const string FileName = "ocean-achievements.json";
        const int MaximumBytes = 131072, CatalogVersion = 1;
        const long MaximumRevision = 9007199254740990L;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static readonly IReadOnlyList<string> Empty = Array.AsReadOnly(new string[0]);
        readonly string directory, primaryPath, backupPath;
        State state = new State();
        string primaryHash, backupHash;
        bool primaryExists, backupExists;
        public bool Writable { get; private set; } = true;
        public string LastError { get; private set; } = "";
        public int UnlockedCount { get { int count = 0; foreach (bool flag in state.unlocked) if (flag) count++; return count; } }
        public int CurrentUnlockedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < state.unlocked.Length; i++)
                    if (state.unlocked[i] && !OceanAchievementCatalog.All[i].Historical) count++;
                return count;
            }
        }
        public int HistoricalUnlockedCount => UnlockedCount - CurrentUnlockedCount;
        public int UnreadCount { get { int count = 0; foreach (bool flag in state.unread) if (flag) count++; return count; } }
        public long Revision => state.revision;

        sealed class State
        {
            public long revision;
            public long[] progress = new long[OceanAchievementCatalog.Count];
            public bool[] unlocked = new bool[OceanAchievementCatalog.Count], unread = new bool[OceanAchievementCatalog.Count];
            public State Copy() => new State { revision = revision, progress = (long[])progress.Clone(),
                unlocked = (bool[])unlocked.Clone(), unread = (bool[])unread.Clone() };
        }
        sealed class Unsupported : Exception { public Unsupported(string detail) : base(detail) { } }

        public OceanAchievementJournal(string directory)
        {
            this.directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
            primaryPath = Path.Combine(this.directory, FileName); backupPath = primaryPath + ".backup";
            primaryExists = Exists(primaryPath); backupExists = Exists(backupPath);
            if (!primaryExists && !backupExists) return;
            State primary = null, backup = null;
            try { if (primaryExists) primary = Read(primaryPath, out primaryHash); }
            catch (Exception error) { Protect("Achievement primary preserved: " + error.GetType().Name + ": " + error.Message); }
            try { if (backupExists) backup = Read(backupPath, out backupHash); }
            catch (Exception error) { Protect("Achievement backup preserved: " + error.GetType().Name + ": " + error.Message); }
            if (primary != null) state = primary;
            else if (backup != null) state = backup;
            if (primary == null && backup != null) Protect("Verified achievement backup is available read-only; primary requires recovery.");
            if (primary != null && backup != null && (backup.revision > primary.revision ||
                (backup.revision == primary.revision && Body(primary) != Body(backup))))
                Protect("Conflicting achievement generations preserved; recovery required.");
        }

        public bool IsUnlocked(string id) { int index = OceanAchievementCatalog.Index(id); return index >= 0 && state.unlocked[index]; }
        public bool IsUnread(string id) { int index = OceanAchievementCatalog.Index(id); return index >= 0 && state.unread[index]; }
        public long Progress(string id) { int index = OceanAchievementCatalog.Index(id); return index >= 0 ? state.progress[index] : 0L; }

        public IReadOnlyList<string> Evaluate(OceanAchievementFacts facts, bool notify)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (!Writable) return Empty;
            State candidate = null; List<string> unlocked = null;
            for (int i = 0; i < OceanAchievementCatalog.Count; i++)
            {
                OceanAchievementDefinition definition = OceanAchievementCatalog.All[i];
                long progress = Math.Max(state.progress[i], definition.Progress(facts));
                bool complete = state.unlocked[i] || progress >= definition.Target;
                if (progress == state.progress[i] && complete == state.unlocked[i]) continue;
                if (candidate == null) candidate = state.Copy();
                candidate.progress[i] = progress; candidate.unlocked[i] = complete;
                if (complete && !state.unlocked[i])
                {
                    candidate.unread[i] = notify;
                    if (unlocked == null) unlocked = new List<string>();
                    unlocked.Add(definition.Id);
                }
            }
            if (candidate == null) return Empty;
            if (!Commit(candidate)) return Empty;
            return unlocked == null ? Empty : unlocked.AsReadOnly();
        }

        public bool MarkAllSeen()
        {
            if (!Writable) return false;
            if (UnreadCount == 0) return true;
            State candidate = state.Copy(); Array.Clear(candidate.unread, 0, candidate.unread.Length);
            return Commit(candidate);
        }

        bool Commit(State candidate)
        {
            string temporary = null;
            try
            {
                if (state.revision >= MaximumRevision) throw new IOException("Achievement revision limit reached; preserve for migration.");
                // A live journal must not overwrite a newer process, externally restored file or future schema.
                if (!Matches(primaryPath, primaryExists, primaryHash) || !Matches(backupPath, backupExists, backupHash))
                { Protect("Achievement files changed outside this journal; reload before writing."); return false; }
                candidate.revision = state.revision + 1;
                string body = Body(candidate);
                byte[] bytes = Utf8.GetBytes(OceanLeaderboardJson.Write(new Dictionary<string, object> {
                    { "body", body }, { "sha256", Hash(Utf8.GetBytes(body)) } }));
                if (bytes.Length > MaximumBytes) throw new IOException("Achievement size limit exceeded.");
                string nextHash = Hash(bytes), previousHash = primaryHash;
                Directory.CreateDirectory(directory);
                temporary = primaryPath + ".tmp-" + Guid.NewGuid().ToString("N");
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (primaryExists) File.Replace(temporary, primaryPath, backupPath);
                else File.Move(temporary, primaryPath);
                temporary = null;
                if (primaryExists) { backupHash = previousHash; backupExists = true; }
                primaryHash = nextHash; primaryExists = true;
                state = candidate; LastError = "";
                return true;
            }
            catch (Exception error) { LastError = "Achievement save failed: " + error.GetType().Name + ": " + error.Message; return false; }
            finally { if (temporary != null) { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } } }
        }

        static State Read(string path, out string fileHash)
        {
            fileHash = null;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumBytes) throw new IOException("Missing, empty or oversized achievement file.");
            byte[] bytes = File.ReadAllBytes(path);
            var envelope = OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(Utf8.GetString(bytes)));
            Keys(envelope, "body", "sha256");
            string body = OceanLeaderboardJson.Text(envelope, "body");
            if (body == null || !string.Equals(Hash(Utf8.GetBytes(body)), OceanLeaderboardJson.Text(envelope, "sha256"), StringComparison.Ordinal))
                throw new FormatException("Achievement checksum mismatch.");
            var data = OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(body));
            if (OceanLeaderboardJson.Number(data, "schema") != 1 || OceanLeaderboardJson.Number(data, "catalogVersion") != CatalogVersion)
                throw new Unsupported("Unsupported achievement schema/catalog; preserved without migration.");
            Keys(data, "schema", "catalogVersion", "revision", "entries");
            long revision = OceanLeaderboardJson.Number(data, "revision");
            if (revision < 1 || revision > MaximumRevision) throw new FormatException("Invalid achievement revision.");
            var rows = OceanLeaderboardJson.Array(data, "entries");
            if (rows.Count > OceanAchievementCatalog.Count) throw new Unsupported("Unknown achievement catalog entries.");
            var loaded = new State { revision = revision };
            var seen = new HashSet<int>();
            foreach (object row in rows)
            {
                var item = OceanLeaderboardJson.Object(row); Keys(item, "id", "progress", "unlocked", "unread");
                int index = OceanAchievementCatalog.Index(OceanLeaderboardJson.Text(item, "id"));
                if (index < 0) throw new Unsupported("Unknown achievement identity; future data preserved.");
                if (!seen.Add(index)) throw new FormatException("Duplicate achievement identity.");
                long progress = OceanLeaderboardJson.Number(item, "progress");
                bool unlocked = Flag(item, "unlocked"), unread = Flag(item, "unread");
                if (progress < 0 || progress > OceanAchievementCatalog.All[index].Target ||
                    unlocked != (progress == OceanAchievementCatalog.All[index].Target) || (unread && !unlocked))
                    throw new FormatException("Inconsistent achievement progress/unread state.");
                loaded.progress[index] = progress; loaded.unlocked[index] = unlocked; loaded.unread[index] = unread;
            }
            fileHash = Hash(bytes); return loaded;
        }

        static string Body(State value)
        {
            var entries = new List<object>(OceanAchievementCatalog.Count);
            for (int i = 0; i < OceanAchievementCatalog.Count; i++)
                entries.Add(new Dictionary<string, object> { { "id", OceanAchievementCatalog.All[i].Id },
                    { "progress", value.progress[i] }, { "unlocked", value.unlocked[i] }, { "unread", value.unread[i] } });
            return OceanLeaderboardJson.Write(new Dictionary<string, object> { { "schema", 1 }, { "catalogVersion", CatalogVersion },
                { "revision", value.revision }, { "entries", entries } });
        }
        static bool Flag(Dictionary<string, object> row, string key)
        { if (!row.TryGetValue(key, out object value) || !(value is bool result)) throw new FormatException("Invalid achievement boolean."); return result; }
        static void Keys(Dictionary<string, object> data, params string[] expected)
        {
            if (data.Count != expected.Length) throw new Unsupported("Unknown or missing achievement fields; preserved for migration.");
            foreach (string key in expected) if (!data.ContainsKey(key)) throw new Unsupported("Unknown achievement fields; preserved for migration.");
        }
        static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
        static bool Matches(string path, bool exists, string hash)
        {
            if (Exists(path) != exists) return false;
            if (!exists) return true;
            var info = new FileInfo(path);
            return info.Exists && info.Length > 0 && info.Length <= MaximumBytes && Hash(File.ReadAllBytes(path)) == hash;
        }
        void Protect(string message) { Writable = false; LastError = LastError.Length == 0 ? message : LastError + " " + message; }
        static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}

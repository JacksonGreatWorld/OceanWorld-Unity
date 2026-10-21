using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SwanUnderwater
{
    /// <summary>Independent checksummed sidecar; never rewrites the existing campaign or leaderboard files.</summary>
    public sealed class OceanNaturalistStore
    {
        public const string FileName = "ocean-naturalist.json";
        const int MaximumBytes = 65536;
        readonly string directory;
        bool primaryVerified;
        public OceanNaturalistState State { get; private set; } = new OceanNaturalistState();
        public bool Writable { get; private set; } = true;
        public bool ObservationEnabled { get; private set; } = true;
        public string LastError { get; private set; } = "";
        sealed class Unsupported : Exception { public Unsupported() : base("Newer naturalist data preserved") { } }

        public OceanNaturalistStore(string saveDirectory)
        {
            directory = Path.GetFullPath(saveDirectory);
            string file = Path.Combine(directory, FileName), backup = file + ".backup";
            if (!File.Exists(file) && !File.Exists(backup)) return;
            try { Read(file); primaryVerified = true; }
            catch (Unsupported e) { Writable = false; LastError = e.Message; }
            catch (Exception first)
            {
                try { Read(backup); LastError = "Recovered naturalist backup: " + first.GetType().Name; }
                catch (Exception second) { Writable = false; LastError = "Naturalist files preserved: " + first.GetType().Name + " / " + second.GetType().Name; }
            }
        }

        public bool TryRecord(int species, int level, string utc)
        {
            // The reader still accepts old ID13 records verbatim. New observations
            // cannot award a retired category or silently substitute another species.
            if (!Writable || !OceanSpeciesCatalog.IsAvailable(species) || !State.TryRecord(species, level, utc)) return false;
            if (Save()) return true;
            State.UndoFailedWrite(species);
            return false;
        }
        public bool SetObservationEnabled(bool value)
        {
            if (!Writable) return false;
            if (value == ObservationEnabled) return true;
            bool before = ObservationEnabled; ObservationEnabled = value;
            if (Save()) return true;
            ObservationEnabled = before; return false;
        }
        string Body()
        {
            var rows = new List<object>();
            foreach (OceanNaturalistRecord record in State.Records)
                rows.Add(new Dictionary<string, object> { {"species", record.Species}, {"firstLevel", record.FirstLevel}, {"observedUtc", record.ObservedUtc} });
            return OceanLeaderboardJson.Write(new Dictionary<string, object> {
                {"schema", 1}, {"catalogVersion", OceanSpeciesCatalog.CatalogVersion}, {"observationEnabled", ObservationEnabled}, {"records", rows} });
        }
        bool Save()
        {
            string temporary = null, backupTemporary = null;
            try
            {
                Directory.CreateDirectory(directory);
                string body = Body();
                byte[] bytes = new UTF8Encoding(false, true).GetBytes(OceanLeaderboardJson.Write(
                    new Dictionary<string, object> { {"body", body}, {"sha256", Hash(body)} }));
                if (bytes.Length > MaximumBytes) throw new IOException("Naturalist size limit");
                string file = Path.Combine(directory, FileName), backup = file + ".backup";
                temporary = file + ".tmp-" + Guid.NewGuid().ToString("N"); Write(temporary, bytes);
                if (File.Exists(file)) File.Replace(temporary, file, primaryVerified ? backup : null);
                else
                {
                    backupTemporary = backup + ".tmp-" + Guid.NewGuid().ToString("N"); Write(backupTemporary, bytes);
                    if (File.Exists(backup)) File.Replace(backupTemporary, backup, null); else File.Move(backupTemporary, backup);
                    backupTemporary = null; File.Move(temporary, file);
                }
                temporary = null; primaryVerified = true; LastError = ""; return true;
            }
            catch (Exception e) { LastError = "Naturalist save failed: " + e.GetType().Name; return false; }
            finally { Delete(temporary); Delete(backupTemporary); }
        }
        void Read(string path)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > MaximumBytes) throw new IOException("Missing or oversized naturalist file");
            var envelope = OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(File.ReadAllText(path, new UTF8Encoding(false, true))));
            string body = OceanLeaderboardJson.Text(envelope, "body");
            if (Hash(body) != OceanLeaderboardJson.Text(envelope, "sha256")) throw new FormatException("Naturalist checksum mismatch");
            var data = OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(body));
            long catalogVersion = OceanLeaderboardJson.Number(data, "catalogVersion");
            if (OceanLeaderboardJson.Number(data, "schema") != 1 || catalogVersion < 1 || catalogVersion > OceanSpeciesCatalog.CatalogVersion) throw new Unsupported();
            int supportedCount = OceanSpeciesCatalog.CountForVersion((int)catalogVersion);
            if (!data.TryGetValue("observationEnabled", out object enabled) || !(enabled is bool flag)) throw new FormatException("Invalid observation preference");
            var rows = OceanLeaderboardJson.Array(data, "records");
            if (rows.Count > supportedCount) throw new FormatException("Too many naturalist records");
            var state = new OceanNaturalistState();
            foreach (object row in rows)
            {
                var record = OceanLeaderboardJson.Object(row);
                int id = checked((int)OceanLeaderboardJson.Number(record, "species"));
                int level = checked((int)OceanLeaderboardJson.Number(record, "firstLevel"));
                // Authenticate and validate against the historical catalog before accepting its append-only migration.
                if (id < 0 || id >= supportedCount || !state.TryRecord(id, level, OceanLeaderboardJson.Text(record, "observedUtc"))) throw new FormatException("Invalid or duplicate naturalist record");
            }
            State = state; ObservationEnabled = flag;
        }
        static void Write(string path, byte[] bytes)
        { using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); } }
        static void Delete(string path)
        { if (path == null) return; try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        static string Hash(string value)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(new UTF8Encoding(false, true).GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
    }
}

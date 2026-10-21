using System;
using System.Collections.Generic;
using System.Globalization;

namespace SwanUnderwater
{
    public sealed class OceanNaturalistRecord
    {
        public readonly int Species, FirstLevel;
        public readonly string ObservedUtc;
        public OceanNaturalistRecord(int species, int firstLevel, string observedUtc)
        { Species = species; FirstLevel = firstLevel; ObservedUtc = observedUtc; }
    }

    /// <summary>A lifetime set of actually identified model categories, independent of campaign score and retries.</summary>
    public sealed class OceanNaturalistState
    {
        static readonly string[] UtcFormats = CreateUtcFormats();
        readonly List<OceanNaturalistRecord> records = new List<OceanNaturalistRecord>();
        readonly HashSet<int> discovered = new HashSet<int>();
        readonly IReadOnlyList<OceanNaturalistRecord> readOnlyRecords;
        public OceanNaturalistState() { readOnlyRecords = records.AsReadOnly(); }
        public IReadOnlyList<OceanNaturalistRecord> Records => readOnlyRecords;
        public int Count => records.Count;
        public int AvailableCount
        {
            get { int count = 0; foreach (var record in records) if (OceanSpeciesCatalog.IsAvailable(record.Species)) count++; return count; }
        }
        public int AvailableRareCount
        {
            get { int count = 0; foreach (var record in records) if (OceanSpeciesCatalog.IsAvailable(record.Species) && OceanSpeciesCatalog.Get(record.Species).Rare) count++; return count; }
        }
        public int RareCount { get; private set; }
        public bool Contains(int id) => discovered.Contains(id);
        public bool TryRecord(int id, int level, string utc)
        {
            if (id < 0 || id >= OceanSpeciesCatalog.Count || level < OceanSpeciesCatalog.Get(id).FirstLevel || level > 20 ||
                !ValidUtc(utc) ||
                discovered.Contains(id)) return false;
            discovered.Add(id); records.Add(new OceanNaturalistRecord(id, level, utc));
            if (OceanSpeciesCatalog.Get(id).Rare) RareCount++;
            return true;
        }
        static bool ValidUtc(string value)
        {
            // Unzoned/partial dates must not acquire the host's local offset on reload.
            if (value == null || value.Length < 20 || value[10] != 'T' ||
                !(value.EndsWith("Z", StringComparison.Ordinal) || value.EndsWith("+00:00", StringComparison.Ordinal))) return false;
            return DateTimeOffset.TryParseExact(value, UtcFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out DateTimeOffset time) && time.Offset == TimeSpan.Zero;
        }
        static string[] CreateUtcFormats()
        {
            var formats = new string[16];
            for (int digits = 0; digits <= 7; digits++)
            {
                string prefix = "yyyy-MM-dd'T'HH:mm:ss" + (digits == 0 ? "" : "." + new string('f', digits));
                formats[digits * 2] = prefix + "'Z'";
                formats[digits * 2 + 1] = prefix + "zzz";
            }
            return formats;
        }
        internal void UndoFailedWrite(int id)
        {
            if (!discovered.Remove(id)) return;
            for (int i = records.Count - 1; i >= 0; i--)
                if (records[i].Species == id) { records.RemoveAt(i); break; }
            if (OceanSpeciesCatalog.Get(id).Rare) RareCount--;
        }
    }

    /// <summary>Explicit hand-ray observation; no head-gaze selection and no consumption of grab/shoot gestures.</summary>
    public sealed class OceanNaturalistObservation
    {
        public const double RequiredSeconds = 2;
        int subject = -1;
        double seconds;
        public float Progress => (float)Math.Min(1, seconds / RequiredSeconds);
        public void Reset() { subject = -1; seconds = 0; }
        public bool Tick(int stableSubject, double delta, bool available)
        {
            if (!available || stableSubject < 0 || double.IsNaN(delta) || double.IsInfinity(delta) || delta <= 0)
            { Reset(); return false; }
            if (subject != stableSubject) { subject = stableSubject; seconds = 0; }
            seconds += Math.Min(.1, delta);
            if (seconds + .000001 < RequiredSeconds) return false;
            Reset(); return true;
        }
    }
}

#if UNITY_EDITOR || OCEAN_DIAGNOSTIC_LOG_STANDALONE
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
#if !OCEAN_DIAGNOSTIC_LOG_STANDALONE
using UnityEngine;
#endif

namespace SwanUnderwater.EditorTests
{
    /// <summary>Observable byte/JSONL/file-system contracts. Uses only memory streams
    /// and a new private temp folder; never opens the game's logs or saves.</summary>
    public static class OceanDiagnosticLogTests
    {
        public static int LastAssertionCount { get; private set; }
        public static string LastFixtureDirectory { get; private set; }
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        const string Report = "{\"reason\":\"interval\",\"samples\":7,\"meanMs\":14.25,\"label\":\"海龟🐢\\n下一行\"}";

        public static void RunAll()
        {
            LastAssertionCount = 0;
            CheckNormalRecord();
            CheckExactCapacity();
            CheckRejectedWholeRecord();
            CheckChunkBoundary();
            CheckInvalidInputAndDispose();
            CheckIoFailure();
            CheckIndependentSessions();
            string message = "DIAGNOSTIC_LOG_TESTS_PASSED assertions=" + LastAssertionCount +
                "; UTF-8 bytes, whole JSONL records, capacity marker, stopped IO, independent CreateNew sessions; fixtures=" + LastFixtureDirectory;
#if OCEAN_DIAGNOSTIC_LOG_STANDALONE
            Console.WriteLine(message);
#else
            Debug.Log(message);
#endif
        }

        static void CheckNormalRecord()
        {
            using (var memory = new MemoryStream())
            {
                using (var log = new OceanDiagnosticLog(memory, 4096, true))
                {
                    Check(log.TryWriteLine(Report), "normal report is accepted");
                    BytesEqual(memory.ToArray(), StrictUtf8.GetBytes(Report + "\n"), "normal report JSON is byte-for-byte unchanged, UTF-8 without BOM");
                    Check(log.WrittenBytes == memory.Length && log.RecordsWritten == 1, "observed bytes/record count match the stream");
                    Check(!log.IsStopped && !log.SizeLimitReached, "normal reporting stays open");
                    List<Dictionary<string, object>> lines = ReadRecords(memory.ToArray());
                    Check(lines.Count == 1 && OceanLeaderboardJson.Number(lines[0], "samples") == 7 &&
                        OceanLeaderboardJson.Text(lines[0], "label") == "海龟🐢\n下一行", "all normal fields and escaped newlines survive parsing");
                }
                BytesEqual(memory.ToArray(), StrictUtf8.GetBytes(Report + "\n"), "ordinary dispose never adds a misleading capacity marker");
            }
        }

        static void CheckExactCapacity()
        {
            long limit = StrictUtf8.GetByteCount(Report + "\n" + OceanDiagnosticLog.CapacityEndRecord + "\n");
            using (var memory = new MemoryStream())
            using (var log = new OceanDiagnosticLog(memory, limit, true))
            {
                Check(log.TryWriteLine(Report), "report exactly filling data capacity is accepted");
                Check(log.SizeLimitReached && log.IsStopped, "exact data boundary terminates immediately");
                Check(memory.Length == limit && log.WrittenBytes == limit, "terminal record is included in exact total byte cap");
                List<Dictionary<string, object>> records = ReadRecords(memory.ToArray());
                Check(records.Count == 2, "report plus one terminal record only");
                CheckTerminal(records[1]);
                Check(CompleteWindows(records) == 1, "capacity marker is not a performance interval sample");
                byte[] before = memory.ToArray();
                for (int i = 0; i < 100; i++) Check(!log.TryWriteLine(Report), "stopped session rejects subsequent reports");
                BytesEqual(memory.ToArray(), before, "repeated stopped calls cannot grow a file or duplicate the terminal record");
                Check(log.RecordsWritten == 1, "terminal marker does not count as a normal report");
            }
        }

        static void CheckRejectedWholeRecord()
        {
            string next = "{\"reason\":\"interval\",\"samples\":8,\"label\":\"章鱼🐙\"}";
            long limit = StrictUtf8.GetByteCount(Report + "\n" + next + "\n" + OceanDiagnosticLog.CapacityEndRecord + "\n") - 1;
            using (var memory = new MemoryStream())
            using (var log = new OceanDiagnosticLog(memory, limit, true))
            {
                Check(log.TryWriteLine(Report), "first report leaves a reserved terminal slot");
                Check(!log.TryWriteLine(next) && log.SizeLimitReached, "one-byte UTF-8 shortfall rejects the whole second report");
                BytesEqual(memory.ToArray(), StrictUtf8.GetBytes(Report + "\n" + OceanDiagnosticLog.CapacityEndRecord + "\n"),
                    "neither a JSON prefix nor half a multibyte character is appended");
                Check(memory.Length <= limit && log.WrittenBytes == memory.Length, "rejected record cannot exceed the total cap");
                Check(ReadRecords(memory.ToArray()).Count == 2, "capacity stop preserves complete parseable JSONL");
            }
            using (var memory = new MemoryStream())
            using (var log = new OceanDiagnosticLog(memory, OceanDiagnosticLog.CapacityEndRecordByteCount + 3, true))
            {
                Check(!log.TryWriteLine(next), "oversized first report is not partly written");
                List<Dictionary<string, object>> records = ReadRecords(memory.ToArray());
                Check(records.Count == 1 && log.RecordsWritten == 0, "oversized first report leaves only a termination record");
                CheckTerminal(records[0]);
            }
        }

        static void CheckChunkBoundary()
        {
            string prefix = "{\"text\":\"";
            string json = prefix + new string('x', 1023 - prefix.Length) + "🐢" + new string('海', 2200) + "🪼\"}";
            byte[] expected = StrictUtf8.GetBytes(json + "\n");
            using (var memory = new MemoryStream())
            using (var log = new OceanDiagnosticLog(memory, expected.Length + OceanDiagnosticLog.CapacityEndRecordByteCount + 1, true))
            {
                Check(log.TryWriteLine(json), "large record spans reused encoding chunks");
                BytesEqual(memory.ToArray(), expected, "surrogate pair at a chunk edge and later Chinese text retain exact UTF-8 bytes");
                Check(ReadRecords(memory.ToArray()).Count == 1, "chunked output parses as one original JSON record");
            }
        }

        static void CheckInvalidInputAndDispose()
        {
            using (var memory = new MemoryStream())
            {
                var log = new OceanDiagnosticLog(memory, 4096, true);
                Throws<ArgumentNullException>(() => log.TryWriteLine(null), "null input rejected before writing");
                foreach (string invalid in new[] { "", "{}\n{}", "{}\r{}" })
                    Throws<ArgumentException>(() => log.TryWriteLine(invalid), "raw line breaks or empty records rejected");
                foreach (string invalid in new[] { "{\"x\":\"\ud83d\"}", "{\"x\":\"\udc22\"}" })
                    Throws<EncoderFallbackException>(() => log.TryWriteLine(invalid), "unpaired surrogate rejected before any prefix write");
                Check(memory.Length == 0 && log.WrittenBytes == 0 && log.RecordsWritten == 0 && !log.IsStopped,
                    "bad input neither consumes budget nor prevents a later valid report");
                Check(log.TryWriteLine(Report), "valid report remains writable after rejected input");
                byte[] before = memory.ToArray(); log.Dispose(); log.Dispose();
                Check(memory.CanWrite && log.IsStopped, "leaveOpen and idempotent disposal are honored");
                Throws<ObjectDisposedException>(() => log.TryWriteLine(Report), "disposed writer cannot resume");
                BytesEqual(memory.ToArray(), before, "disposal appends no data");
            }
            using (var memory = new MemoryStream())
                Throws<ArgumentOutOfRangeException>(() => new OceanDiagnosticLog(memory, OceanDiagnosticLog.CapacityEndRecordByteCount + 2, true), "cap must reserve a minimum complete JSONL object and marker");
            byte[] oldBytes = StrictUtf8.GetBytes("old evidence");
            using (var existing = new MemoryStream((byte[])oldBytes.Clone()))
            {
                Throws<ArgumentException>(() => new OceanDiagnosticLog(existing, 4096, true), "existing stream is never overwritten or appended as a new session");
                BytesEqual(existing.ToArray(), oldBytes, "rejected existing stream remains untouched");
            }
            var owned = new MemoryStream(); var ownedLog = new OceanDiagnosticLog(owned);
            ownedLog.Dispose(); Check(!owned.CanWrite, "owned stream is closed on disposal");
        }

        static void CheckIoFailure()
        {
            using (var stream = new FailingStream(2, false))
            using (var log = new OceanDiagnosticLog(stream, 4096, true))
            {
                Throws<IOException>(() => log.TryWriteLine(Report), "partial IO failure is surfaced");
                Check(log.IsStopped && !log.SizeLimitReached && log.RecordsWritten == 0, "IO error stops reporting without claiming a capacity stop or complete report");
                Check(log.WrittenBytes == 2 && stream.Length == 2, "accepted IO prefix is not hidden by the byte counter");
                int writes = stream.Writes;
                Check(!log.TryWriteLine(Report) && stream.Writes == writes && stream.Length == 2, "no retry or terminal append occurs after a failed partial write");
            }
            using (var stream = new FailingStream(int.MaxValue, true))
            using (var log = new OceanDiagnosticLog(stream, 4096, true))
            {
                Throws<IOException>(() => log.TryWriteLine(Report), "flush failure is surfaced");
                Check(log.IsStopped && !log.SizeLimitReached, "flush failure is not falsely certified as a capacity termination");
                BytesEqual(stream.Bytes, StrictUtf8.GetBytes(Report + "\n"), "a flush failure leaves the accepted source record untouched");
                int flushes = stream.Flushes; log.Dispose();
                Check(stream.Flushes == flushes, "faulted disposal does not retry a known failing flush");
            }
        }

        static void CheckIndependentSessions()
        {
            LastFixtureDirectory = Path.Combine(Path.GetTempPath(), "OceanDiagnosticLog-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(LastFixtureDirectory);
            string evidence = Path.Combine(LastFixtureDirectory, "runtime-existing-evidence.jsonl");
            byte[] original = StrictUtf8.GetBytes("{\"oldEvidence\":\"保留\"}\n"); File.WriteAllBytes(evidence, original);
            DateTime sameUtc = new DateTime(2026, 10, 20, 12, 34, 56, 123, DateTimeKind.Utc);
            string first, second;
            using (var one = OceanDiagnosticLog.CreateSessionFile(LastFixtureDirectory, sameUtc, out first, 4096))
                Check(one.TryWriteLine(Report), "first private file session writes successfully");
            byte[] beforeRestart = File.ReadAllBytes(first);
            using (var two = OceanDiagnosticLog.CreateSessionFile(LastFixtureDirectory, sameUtc, out second, 4096))
                Check(two.TryWriteLine("{\"reason\":\"partial_before_pause\",\"samples\":3}"), "restart creates an independent new session at the same timestamp");
            Check(first != second && Path.GetFileName(first).StartsWith("runtime-", StringComparison.Ordinal) &&
                Path.GetExtension(first) == ".jsonl" && Path.GetFileName(second).StartsWith("runtime-", StringComparison.Ordinal), "existing runtime-*.jsonl discovery remains compatible");
            Check(Directory.GetFiles(LastFixtureDirectory).Length == 3, "no existing file is deleted or reused");
            BytesEqual(File.ReadAllBytes(evidence), original, "unrelated prior evidence bytes remain unchanged");
            BytesEqual(File.ReadAllBytes(first), beforeRestart, "opening a later session does not mutate the previous session");
            Check(CompleteWindows(ReadRecords(File.ReadAllBytes(first))) == 1 && CompleteWindows(ReadRecords(File.ReadAllBytes(second))) == 0,
                "analysis accepts only reason=interval with positive samples; a pause report is not a full window");
            // Keep this new private fixture folder for failure/audit evidence. Never
            // enumerate, remove or rotate the game's persistent Diagnostics directory.
        }

        static List<Dictionary<string, object>> ReadRecords(byte[] bytes)
        {
            string text = StrictUtf8.GetString(bytes);
            Check(text.EndsWith("\n", StringComparison.Ordinal) && text.IndexOf('\r') < 0, "JSONL ends in LF, without platform CR or incomplete line");
            string[] lines = text.Split('\n'); var records = new List<Dictionary<string, object>>();
            for (int i = 0; i < lines.Length - 1; i++) records.Add(OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(lines[i])));
            return records;
        }

        static int CompleteWindows(List<Dictionary<string, object>> records)
        {
            int count = 0;
            foreach (var record in records)
                if (OceanLeaderboardJson.Text(record, "reason") == "interval" && OceanLeaderboardJson.Number(record, "samples", 0) > 0) count++;
            return count;
        }
        static void CheckTerminal(Dictionary<string, object> record) => Check(
            OceanLeaderboardJson.Text(record, "recordType") == "diagnostic_log_end" &&
            OceanLeaderboardJson.Text(record, "reason") == "session_byte_limit" && OceanLeaderboardJson.Number(record, "samples") == 0,
            "terminal JSON has an explicit type and cannot impersonate a sampled interval");
        static void BytesEqual(byte[] actual, byte[] expected, string message)
        {
            bool same = actual.Length == expected.Length;
            for (int i = 0; same && i < actual.Length; i++) same = actual[i] == expected[i];
            Check(same, message);
        }
        static void Throws<T>(Action action, string message) where T : Exception
        {
            try { action(); } catch (T) { Check(true, message); return; }
            throw new InvalidOperationException("Diagnostic log missing expected " + typeof(T).Name + ": " + message);
        }
        static void Check(bool condition, string message)
        { LastAssertionCount++; if (!condition) throw new InvalidOperationException("Diagnostic log: " + message); }

        sealed class FailingStream : Stream
        {
            readonly MemoryStream memory = new MemoryStream(); readonly int failAfter; readonly bool failFlush;
            public int Writes, Flushes;
            public byte[] Bytes => memory.ToArray();
            public FailingStream(int failAfter, bool failFlush) { this.failAfter = failAfter; this.failFlush = failFlush; }
            public override bool CanRead => false;
            public override bool CanSeek => true;
            public override bool CanWrite => true;
            public override long Length => memory.Length;
            public override long Position { get => memory.Position; set => memory.Position = value; }
            public override void Flush() { Flushes++; if (failFlush) throw new IOException("Independent flush fault"); }
            public override void Write(byte[] bytes, int offset, int count)
            {
                Writes++; int allowed = (int)Math.Max(0, Math.Min(count, (long)failAfter - memory.Position));
                memory.Write(bytes, offset, allowed);
                if (allowed < count) throw new IOException("Independent prefix-write fault");
            }
            public override int Read(byte[] bytes, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => memory.Seek(offset, origin);
            public override void SetLength(long value) => memory.SetLength(value);
            protected override void Dispose(bool disposing) { if (disposing) memory.Dispose(); base.Dispose(disposing); }
        }
#if OCEAN_DIAGNOSTIC_LOG_STANDALONE
        public static void Main() => RunAll();
#endif
    }
}
#endif

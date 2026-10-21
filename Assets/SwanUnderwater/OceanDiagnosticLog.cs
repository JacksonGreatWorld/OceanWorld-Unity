using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SwanUnderwater
{
    /// <summary>
    /// A single new UTF-8 JSONL session, bounded including its capacity marker.
    /// Never opens an existing file, rotates, removes old evidence or changes report JSON.
    /// Call only from the existing report path, not the per-frame sample path.
    /// </summary>
    public sealed class OceanDiagnosticLog : IDisposable
    {
        public const long DefaultSessionByteLimit = 8L * 1024 * 1024;
        public const string CapacityEndRecord = "{\"recordType\":\"diagnostic_log_end\",\"reason\":\"session_byte_limit\",\"samples\":0}";
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static readonly byte[] EndBytes = Encoding.ASCII.GetBytes(CapacityEndRecord + "\n");
        readonly Stream stream;
        readonly bool leaveOpen;
        readonly byte[] buffer = new byte[4096];
        bool disposed, faulted;

        public long MaxSessionBytes { get; }
        public long WrittenBytes { get; private set; }
        public long RecordsWritten { get; private set; }
        public bool IsStopped { get; private set; }
        public bool SizeLimitReached { get; private set; }
        public static int CapacityEndRecordByteCount => EndBytes.Length;

        /// <summary>The supplied stream must represent a new empty session. File factory
        /// below uses CreateNew, including when timestamps coincide after restart.</summary>
        public OceanDiagnosticLog(Stream stream, long maxSessionBytes = DefaultSessionByteLimit, bool leaveOpen = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            ValidateLimit(maxSessionBytes);
            if (!stream.CanWrite || !stream.CanSeek || stream.Length != 0 || stream.Position != 0)
                throw new ArgumentException("Diagnostic sessions require a writable, seekable, empty stream.", nameof(stream));
            this.stream = stream; this.leaveOpen = leaveOpen; MaxSessionBytes = maxSessionBytes;
        }

        public static OceanDiagnosticLog CreateSessionFile(string directory, DateTime utc, out string path,
            long maxSessionBytes = DefaultSessionByteLimit)
        {
            ValidateLimit(maxSessionBytes);
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "runtime-" + utc.ToUniversalTime().ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N") + ".jsonl");
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            try { return new OceanDiagnosticLog(file, maxSessionBytes); }
            catch { file.Dispose(); throw; }
        }

        /// <summary>
        /// Accepts a complete compact JSON record, including escaped line breaks.
        /// Returns false after capacity/IO stop; no partial record is written for a size
        /// rejection. IO failures stop further writes and propagate to the caller.
        /// WrittenBytes counts bytes accepted by the stream, not a durability guarantee.
        /// </summary>
        public bool TryWriteLine(string compactJson)
        {
            if (disposed) throw new ObjectDisposedException(nameof(OceanDiagnosticLog));
            if (IsStopped) return false;
            if (compactJson == null) throw new ArgumentNullException(nameof(compactJson));
            if (compactJson.Length == 0 || compactJson.IndexOf('\n') >= 0 || compactJson.IndexOf('\r') >= 0)
                throw new ArgumentException("Expected one nonempty compact JSON record.", nameof(compactJson));
            // Count before touching the stream. Strict UTF-8 also rejects a dangling
            // surrogate before any prefix of that malformed record can be written.
            long lineBytes = (long)Utf8.GetByteCount(compactJson) + 1;
            long dataCapacity = MaxSessionBytes - EndBytes.Length;
            try
            {
                if (lineBytes > dataCapacity - WrittenBytes)
                { StopAtCapacity(); return false; }
                int offset = 0;
                while (offset < compactJson.Length)
                {
                    int chars = Math.Min(1024, compactJson.Length - offset);
                    if (offset + chars < compactJson.Length && char.IsHighSurrogate(compactJson[offset + chars - 1]))
                        chars--; // Encode both halves together at a chunk boundary.
                    int bytes = Utf8.GetBytes(compactJson, offset, chars, buffer, 0);
                    WriteBytes(buffer, bytes); offset += chars;
                }
                buffer[0] = (byte)'\n'; WriteBytes(buffer, 1); RecordsWritten++;
                if (WrittenBytes == dataCapacity) StopAtCapacity();
                else stream.Flush();
                return true;
            }
            catch
            {
                faulted = true; IsStopped = true;
                // An IO implementation may accept a prefix before throwing. Observe
                // that position rather than pretending that incomplete bytes vanished.
                try { WrittenBytes = Math.Max(WrittenBytes, stream.Position); } catch { }
                throw;
            }
        }

        void StopAtCapacity()
        {
            WriteBytes(EndBytes, EndBytes.Length);
            stream.Flush();
            SizeLimitReached = true; IsStopped = true;
        }

        void WriteBytes(byte[] bytes, int count)
        {
            stream.Write(bytes, 0, count);
            WrittenBytes += count;
        }

        static void ValidateLimit(long value)
        {
            if (value < EndBytes.Length + 3)
                throw new ArgumentOutOfRangeException(nameof(value), "Reserve a complete JSON object line and the capacity marker.");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; IsStopped = true;
            try { if (!faulted) stream.Flush(); }
            finally { if (!leaveOpen) stream.Dispose(); }
        }
    }
}

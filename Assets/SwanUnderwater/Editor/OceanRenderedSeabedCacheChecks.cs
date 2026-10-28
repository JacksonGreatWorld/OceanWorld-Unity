#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual Unity noise and production sampler A/B, never a mocked height field.
    /// No world startup, XR, resources, player files, scenes or device commands are used.</summary>
    public static class OceanRenderedSeabedCacheChecks
    {
        [Serializable]
        public sealed class Benchmark
        {
            public string scenario, environment;
            public int queriesPerRound, warmupPairs, measuredPairs;
            public double uncachedMicrosecondsPerQuery, cachedMicrosecondsPerQuery, coldFillMilliseconds, checksum;
            public UnderwaterWorld.RenderedSeabedVertexCacheStats warmPass;
        }

        [Serializable]
        public sealed class Report
        {
            public bool success;
            public string utc, unity;
            public string scope = "Unity Editor CPU only: frozen original height/interpolation versus actual cached production entry; fixed query inputs and alternating timing order. No rendering, GPU, compositor, device FPS or allocation-byte claim.";
            public string originalSamplerSha256 = "79632fac00829b9a1820675d8e95bc9b5bd9d2b15c6f51c0daf84fdf92704189";
            public string allocationBytes = "Not measured. Slot payload bound excludes managed array headers and is not a GC allocation sample.";
            public int assertions, pointComparisons, segmentComparisons;
            public bool originalOwnerContextRestored;
            public UnderwaterWorld.RenderedSeabedVertexCacheStats stressCapacity;
            public Benchmark[] benchmarks;
        }

        public static Report LastReport { get; private set; }
        static int assertions, points, segments;
        static double benchmarkSink;

        static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Rendered seabed cache: " + message);
        }

        static void Same(float a, float b, string message) =>
            Check(BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b), message + " bit-exact float");

        public static void Validate()
        {
            assertions = points = segments = 0;
            LastReport = NewReport();
            OceanEnvironmentKind previous = UnderwaterWorld.RenderedSeabedEnvironment;
            using (var cache = new UnderwaterWorld.RenderedSeabedCacheValidationScope())
            {
                CheckPoints(); CheckSeams(); CheckKindBandAndEviction(); CheckSegments(); CheckOwnerContexts(previous);
            }
            LastReport.originalOwnerContextRestored = UnderwaterWorld.RenderedSeabedEnvironment == previous;
            Check(LastReport.originalOwnerContextRestored, "previous live/no-owner context restored");
            FinishReport();
            LastReport.success = true;
            UnityEngine.Debug.Log("RENDERED_SEABED_CACHE_CHECKS_PASS assertions=" + assertions +
                " points=" + points + " segments=" + segments + "; original Unity noise, exact float heights/contact, bounded tables and map lifecycle; no device claim.");
        }

        public static void RunAll()
        {
            Validate();
            var rows = new List<Benchmark>();
            using (var cache = new UnderwaterWorld.RenderedSeabedCacheValidationScope())
            {
                foreach (OceanEnvironmentKind kind in new[] { OceanEnvironmentKind.Classic, OceanEnvironmentKind.CoralSanctuary })
                {
                    rows.Add(Measure(kind, "near swimmer", 0));
                    if (kind == OceanEnvironmentKind.CoralSanctuary) rows.Add(Measure(kind, "medium exploration", 1));
                    rows.Add(Measure(kind, "far/clamped floor", 2));
                }
                rows.Add(Measure(OceanEnvironmentKind.CoralSanctuary, "whole-domain varied queries", 3));
            }
            LastReport.benchmarks = rows.ToArray();
            FinishReport();
            UnityEngine.Debug.Log("RENDERED_SEABED_CACHE_EDITOR_TIMING " + JsonUtility.ToJson(LastReport));
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-oceanSeabedCacheReport")
                {
                    string output = Path.GetFullPath(args[i + 1]);
                    if (File.Exists(output)) throw new IOException("Refusing to overwrite previous seabed-cache report.");
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    File.WriteAllText(output, JsonUtility.ToJson(LastReport, true) + "\n");
                }
        }

        static Report NewReport() => new Report { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion };
        static void FinishReport()
        { LastReport.assertions = assertions; LastReport.pointComparisons = points; LastReport.segmentComparisons = segments; }

        static void Point(float x, float z, OceanEnvironmentKind kind)
        {
            float original = UnderwaterWorld.SampleRenderedSeabedReferenceForValidation(x, z, kind);
            Same(original, UnderwaterWorld.SampleRenderedSeabedHeight(x, z, kind), "cold/replaced point " + kind);
            Same(original, UnderwaterWorld.SampleRenderedSeabedHeight(x, z, kind), "repeated point " + kind);
            points++;
        }

        static void CheckPoints()
        {
            var random = new System.Random(20261023);
            foreach (OceanEnvironmentKind kind in new[] { OceanEnvironmentKind.Classic, OceanEnvironmentKind.CoralSanctuary })
            {
                // Exact fine nodes plus off-node points on both triangle halves.
                for (int x = 0; x <= 160; x += 4)
                for (int z = 0; z <= 160; z += 4)
                {
                    float wx = OceanEnvironmentProfile.SpawnX - 40f + x * .5f;
                    float wz = OceanEnvironmentProfile.SpawnZ - 40f + z * .5f;
                    Point(wx, wz, kind); Point(wx + .173f, wz + .217f, kind); Point(wx + .317f, wz + .293f, kind);
                }
                for (int x = 0; x <= 128; x += 2)
                for (int z = 0; z <= 128; z += 2)
                    Point(-800f + x * 12.5f, -800f + z * 12.5f, kind);
                for (int i = 0; i < 2048; i++)
                    Point((float)(random.NextDouble() * 2000 - 1000), (float)(random.NextDouble() * 2000 - 1000), kind);
            }
            // Existing unknown-kind fallback must not collide with a supported map.
            foreach (int kind in new[] { -1, 2, int.MaxValue }) Point(51.23f, -73.19f, (OceanEnvironmentKind)kind);
        }

        static float Adjacent(float value, bool up)
        {
            if (value == 0f) return up ? float.Epsilon : -float.Epsilon;
            int bits = BitConverter.SingleToInt32Bits(value);
            return BitConverter.Int32BitsToSingle(bits + ((value > 0f) == up ? 1 : -1));
        }

        static void CheckSeams()
        {
            float[] xs = { -850f, -800f, -128f, -40f, -0f, 0f, 40f, 128f, 800f, 850f };
            float[] zs = { -850f, -800f, -132f, -44f, -4f, 0f, 36f, 124f, 800f, 850f };
            foreach (OceanEnvironmentKind kind in new[] { OceanEnvironmentKind.Classic, OceanEnvironmentKind.CoralSanctuary })
            foreach (float x in xs)
            foreach (float z in zs)
            {
                Point(x, z, kind); Point(Adjacent(x, false), Adjacent(z, false), kind);
                Point(Adjacent(x, true), Adjacent(z, true), kind);
            }
        }

        static void CheckKindBandAndEviction()
        {
            UnderwaterWorld.ResetRenderedVertexCacheForValidation();
            UnderwaterWorld.CountRenderedVertexQueriesForValidation(true);
            for (int kind = 0; kind < 2; kind++)
            for (int band = 0; band < 3; band++)
            {
                float reference = UnderwaterWorld.RenderedVertexForValidation(52.5f, -69f, band, (OceanEnvironmentKind)kind, true);
                Same(reference, UnderwaterWorld.RenderedVertexForValidation(52.5f, -69f, band, (OceanEnvironmentKind)kind, false), "kind/band cold key");
                Same(reference, UnderwaterWorld.RenderedVertexForValidation(52.5f, -69f, band, (OceanEnvironmentKind)kind, false), "kind/band warm key");
            }
            var isolation = UnderwaterWorld.RenderedVertexCacheStatsForValidation();
            Check(isolation.allocatedTables == 6 && isolation.misses == 6 && isolation.hits == 6, "six independent kind/band tables");
            float negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
            Same(UnderwaterWorld.RenderedVertexForValidation(negativeZero, 0f, 0, OceanEnvironmentKind.Classic, true),
                UnderwaterWorld.RenderedVertexForValidation(negativeZero, 0f, 0, OceanEnvironmentKind.Classic, false), "negative-zero raw coordinate");
            for (int i = 0; i < 20000; i++)
            {
                float x = -5000f + i * .5f;
                Same(UnderwaterWorld.RenderedVertexForValidation(x, -6.25f, 2, OceanEnvironmentKind.Classic, true),
                    UnderwaterWorld.RenderedVertexForValidation(x, -6.25f, 2, OceanEnvironmentKind.Classic, false), "evicted/colliding exact key");
            }
            var full = UnderwaterWorld.RenderedVertexCacheStatsForValidation();
            Check(full.maximumSlots == 49152 && full.allocatedSlots <= full.maximumSlots && full.occupiedSlots <= full.allocatedSlots,
                "visited points cannot grow the fixed storage cap");
            Check(full.maximumSlotPayloadBytes == 786432, "slot payload bound is explicit, excluding array headers");
            LastReport.stressCapacity = full;
            UnderwaterWorld.ResetRenderedVertexCacheForValidation();
            Check(UnderwaterWorld.RenderedVertexCacheStatsForValidation().allocatedSlots == 0, "subsystem/last-owner reset releases all tables");
        }

        static void CheckSegments()
        {
            var oldScratch = new List<float>(1024); var newScratch = new List<float>(1024);
            var routes = new[] {
                new Vector4(3.12f,-6.27f,3.12f,-6.27f), new Vector4(-40.91f,7.14f,-39.62f,7.68f),
                new Vector4(39.73f,-5.16f,40.78f,-4.83f), new Vector4(127.71f,2.17f,128.43f,2.83f),
                new Vector4(-720.3f,-3.73f,719.7f,-3.67f), new Vector4(850f,20f,950f,20f) };
            foreach (OceanEnvironmentKind kind in new[] { OceanEnvironmentKind.Classic, OceanEnvironmentKind.CoralSanctuary })
            foreach (Vector4 route in routes)
            {
                float start = UnderwaterWorld.SampleRenderedSeabedReferenceForValidation(route.x, route.y, kind);
                float end = UnderwaterWorld.SampleRenderedSeabedReferenceForValidation(route.z, route.w, kind);
                Vector3 from = new Vector3(route.x, start + .31f, route.y), to = new Vector3(route.z, end - .27f, route.w);
                bool oldHit; float oldDistance;
                UnderwaterWorld.BypassRenderedVertexCacheForValidation(true);
                try { oldHit = UnderwaterWorld.TryIntersectRenderedSeabedSegment(from, to, .025f, oldScratch, out oldDistance, kind); }
                finally { UnderwaterWorld.BypassRenderedVertexCacheForValidation(false); }
                bool newHit = UnderwaterWorld.TryIntersectRenderedSeabedSegment(from, to, .025f, newScratch, out float newDistance, kind);
                Check(oldHit == newHit, "unchanged actual segment collision result");
                Same(oldDistance, newDistance, "unchanged actual segment collision distance"); segments++;
            }
        }

        static void CheckOwnerContexts(OceanEnvironmentKind previous)
        {
            const float x = 73.17f, z = -65.29f;
            using (var owner = new UnderwaterWorld.RenderedSeabedOwnerValidationScope(OceanEnvironmentKind.Classic))
            {
                CheckDefault(OceanEnvironmentKind.Classic, x, z);
                owner.SetBuilding(OceanEnvironmentKind.CoralSanctuary);
                CheckDefault(OceanEnvironmentKind.CoralSanctuary, x, z);
                owner.CancelBuilding(); CheckDefault(OceanEnvironmentKind.Classic, x, z);
                owner.SetActive(OceanEnvironmentKind.CoralSanctuary); CheckDefault(OceanEnvironmentKind.CoralSanctuary, x, z);
                owner.SetBuilding(OceanEnvironmentKind.Classic); CheckDefault(OceanEnvironmentKind.Classic, x, z);
                owner.CancelBuilding(); CheckDefault(OceanEnvironmentKind.CoralSanctuary, x, z);
            }
            Check(UnderwaterWorld.RenderedSeabedEnvironment == previous, "destroyed nested owner restores the actual previous context");
        }

        static void CheckDefault(OceanEnvironmentKind expected, float x, float z)
        {
            Check(UnderwaterWorld.RenderedSeabedEnvironment == expected, "real owner/building environment wins");
            Same(UnderwaterWorld.SampleRenderedSeabedReferenceForValidation(x, z, expected),
                UnderwaterWorld.SampleRenderedSeabedHeight(x, z), "public default uses current owner, not last cache kind");
        }

        static Benchmark Measure(OceanEnvironmentKind kind, string name, int profile)
        {
            const int count = 1024, warmup = 4, rounds = 24;
            var queries = new Vector2[count]; var random = new System.Random(20261023 + profile);
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                queries[i] = profile == 0 ? new Vector2(-35f + 70f * t, -4f + Mathf.Sin(t * 12f) * 14f) :
                    profile == 1 ? new Vector2(46f + 75f * t, -4f + Mathf.Sin(t * 9f) * 24f) :
                    profile == 2 ? new Vector2(145f + 720f * t, 210f + Mathf.Sin(t * 9f) * 30f) :
                    new Vector2((float)(random.NextDouble() * 2000 - 1000), (float)(random.NextDouble() * 2000 - 1000));
            }
            UnderwaterWorld.ResetRenderedVertexCacheForValidation();
            long coldStart = Stopwatch.GetTimestamp(); double coldChecksum = Batch(queries, kind, false);
            double coldMs = (Stopwatch.GetTimestamp() - coldStart) * 1000.0 / Stopwatch.Frequency;
            long oldTicks = 0, cachedTicks = 0;
            for (int pair = -warmup; pair < rounds; pair++)
            {
                long a, b; double oldSum, cachedSum;
                if ((pair & 1) == 0)
                { a = Timed(queries, kind, true, out oldSum); b = Timed(queries, kind, false, out cachedSum); }
                else
                { b = Timed(queries, kind, false, out cachedSum); a = Timed(queries, kind, true, out oldSum); }
                Check(oldSum == cachedSum && oldSum == coldChecksum, "identical timed fixed-input checksum");
                if (pair >= 0) { oldTicks += a; cachedTicks += b; }
            }
            UnderwaterWorld.CountRenderedVertexQueriesForValidation(true);
            benchmarkSink = Batch(queries, kind, false);
            var stats = UnderwaterWorld.RenderedVertexCacheStatsForValidation();
            UnderwaterWorld.CountRenderedVertexQueriesForValidation(false);
            return new Benchmark { scenario = name, environment = kind.ToString(), queriesPerRound = count,
                warmupPairs = warmup, measuredPairs = rounds, coldFillMilliseconds = coldMs, checksum = benchmarkSink,
                uncachedMicrosecondsPerQuery = oldTicks * 1000000.0 / Stopwatch.Frequency / rounds / count,
                cachedMicrosecondsPerQuery = cachedTicks * 1000000.0 / Stopwatch.Frequency / rounds / count, warmPass = stats };
        }

        static long Timed(Vector2[] queries, OceanEnvironmentKind kind, bool reference, out double sum)
        { long start = Stopwatch.GetTimestamp(); sum = Batch(queries, kind, reference); return Stopwatch.GetTimestamp() - start; }
        static double Batch(Vector2[] queries, OceanEnvironmentKind kind, bool reference)
        {
            double sum = 0;
            foreach (Vector2 q in queries) sum += reference
                ? UnderwaterWorld.SampleRenderedSeabedReferenceForValidation(q.x, q.y, kind)
                : UnderwaterWorld.SampleRenderedSeabedHeight(q.x, q.y, kind);
            return sum;
        }
    }
}
#endif

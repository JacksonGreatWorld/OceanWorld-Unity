using System;
using UnityEngine;

namespace SwanUnderwater
{
    public sealed partial class UnderwaterWorld
    {
        // The sampler selects EXACT fixed-grid coordinates before reaching this
        // cache. Do not quantize inputs, cache a nearby interpolated surface, or
        // seed heights from rounded mesh indices: even tiny coordinate changes
        // can alter the original noise/interpolation at collision boundaries.
        const int RenderedVertexSlotsPerTable = 8192;
        const int RenderedVertexBandCount = 3;
        const int RenderedVertexKindCount = 2;
        const int RenderedVertexTableCount = RenderedVertexBandCount * RenderedVertexKindCount;

        struct RenderedVertexSlot
        {
            public ulong key;
            public float height;
            public int occupied;
        }

        // Main-thread queries already own the terrain sampler. Direct mapping
        // has no eviction allocations, growing dictionary or history of visited
        // points. At most 6 * 8192 * 16 = 786432 slot bytes, plus array headers.
        // Each table is lazy: Classic never needs the Sanctuary Medium table.
        static RenderedVertexSlot[][] renderedVertexCaches;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRenderedSeabedVertexCaches()
        {
            renderedVertexCaches = null;
#if UNITY_EDITOR
            renderedVertexCountQueries = false;
            renderedVertexBypass = false;
            renderedVertexHits = renderedVertexMisses = 0;
#endif
        }

        static float UncachedRenderedSeabedVertexHeight(float x, float z, SeabedBand band, OceanEnvironmentKind kind) =>
            band == SeabedBand.Near ? NearTerrainHeight(x, z, kind) :
            band == SeabedBand.Medium ? MediumTerrainHeight(x, z) : SampleTerrainHeight(x, z, kind) - .12f;

        static float CachedRenderedSeabedVertexHeight(float x, float z, SeabedBand band, OceanEnvironmentKind kind)
        {
#if UNITY_EDITOR
            if (renderedVertexBypass) return UncachedRenderedSeabedVertexHeight(x, z, band, kind);
#endif
            int kindIndex = (int)kind, bandIndex = (int)band;
            // Preserve the old public sampler's fallback for unknown enum values.
            if ((uint)kindIndex >= RenderedVertexKindCount || (uint)bandIndex >= RenderedVertexBandCount)
                return UncachedRenderedSeabedVertexHeight(x, z, band, kind);
            if (renderedVertexCaches == null) renderedVertexCaches = new RenderedVertexSlot[RenderedVertexTableCount][];
            int tableIndex = kindIndex * RenderedVertexBandCount + bandIndex;
            RenderedVertexSlot[] table = renderedVertexCaches[tableIndex];
            if (table == null) renderedVertexCaches[tableIndex] = table = new RenderedVertexSlot[RenderedVertexSlotsPerTable];
            ulong key = RenderedVertexKey(x, z);
            ref RenderedVertexSlot slot = ref table[RenderedVertexHash(key)];
            if (slot.occupied != 0 && slot.key == key)
            {
#if UNITY_EDITOR
                if (renderedVertexCountQueries) renderedVertexHits++;
#endif
                return slot.height;
            }
            // Use the original function with explicit kind. No global current-map
            // overload is called, including during another map's construction.
            float height = UncachedRenderedSeabedVertexHeight(x, z, band, kind);
            slot.key = key; slot.height = height; slot.occupied = 1;
#if UNITY_EDITOR
            if (renderedVertexCountQueries) renderedVertexMisses++;
#endif
            return height;
        }

        static ulong RenderedVertexKey(float x, float z) =>
            ((ulong)(uint)BitConverter.SingleToInt32Bits(x) << 32) | (uint)BitConverter.SingleToInt32Bits(z);

        static int RenderedVertexHash(ulong key)
        {
            // Mix both raw float bit patterns; regular half-metre grid bits must
            // not all alias. A collision replaces a slot but never returns it for
            // a different key. No query order can change the returned height.
            unchecked
            {
                key ^= key >> 33; key *= 0xff51afd7ed558ccdUL;
                key ^= key >> 33; key *= 0xc4ceb9fe1a85ec53UL;
                key ^= key >> 33;
                return (int)(key & (RenderedVertexSlotsPerTable - 1));
            }
        }

#if UNITY_EDITOR
        static bool renderedVertexCountQueries, renderedVertexBypass;
        static long renderedVertexHits, renderedVertexMisses;

        [Serializable]
        public struct RenderedSeabedVertexCacheStats
        {
            public long hits, misses;
            public int allocatedTables, allocatedSlots, occupiedSlots, maximumSlots, maximumSlotPayloadBytes;
        }

        public static RenderedSeabedVertexCacheStats RenderedVertexCacheStatsForValidation()
        {
            var result = new RenderedSeabedVertexCacheStats {
                hits = renderedVertexHits, misses = renderedVertexMisses,
                maximumSlots = RenderedVertexSlotsPerTable * RenderedVertexTableCount,
                maximumSlotPayloadBytes = RenderedVertexSlotsPerTable * RenderedVertexTableCount * 16 };
            if (renderedVertexCaches == null) return result;
            foreach (var table in renderedVertexCaches)
            {
                if (table == null) continue;
                result.allocatedTables++; result.allocatedSlots += table.Length;
                foreach (var slot in table) if (slot.occupied != 0) result.occupiedSlots++;
            }
            return result;
        }

        public static void CountRenderedVertexQueriesForValidation(bool enabled)
        { renderedVertexHits = renderedVertexMisses = 0; renderedVertexCountQueries = enabled; }
        public static void BypassRenderedVertexCacheForValidation(bool enabled) => renderedVertexBypass = enabled;
        public static void ResetRenderedVertexCacheForValidation() => ResetRenderedSeabedVertexCaches();

        /// <summary>Frozen old public sampling arithmetic, before any cache lookup.
        /// Original sampler+vertex SHA256: 79632fac00829b9a1820675d8e95bc9b5bd9d2b15c6f51c0daf84fdf92704189.</summary>
        public static float SampleRenderedSeabedReferenceForValidation(float x, float z, OceanEnvironmentKind kind)
        {
            SeabedBand band = SeabedSegmentCell(x, z, kind, out float x0, out float x1, out float z0, out float z1);
            x = Mathf.Clamp(x, -800f, 800f); z = Mathf.Clamp(z, -800f, 800f);
            float a = UncachedRenderedSeabedVertexHeight(x0, z0, band, kind);
            float b = UncachedRenderedSeabedVertexHeight(x1, z0, band, kind);
            float c = UncachedRenderedSeabedVertexHeight(x0, z1, band, kind);
            float u = Mathf.InverseLerp(x0, x1, x), v = Mathf.InverseLerp(z0, z1, z);
            if (u + v <= 1f) return a * (1f - u - v) + b * u + c * v;
            float d = UncachedRenderedSeabedVertexHeight(x1, z1, band, kind);
            return b * (1f - v) + c * (1f - u) + d * (u + v - 1f);
        }

        public static float RenderedVertexForValidation(float x, float z, int band, OceanEnvironmentKind kind, bool reference) =>
            reference ? UncachedRenderedSeabedVertexHeight(x, z, (SeabedBand)band, kind) :
                CachedRenderedSeabedVertexHeight(x, z, (SeabedBand)band, kind);

        /// <summary>Preserve any already live scene's bounded caches when a fixture ends.</summary>
        public sealed class RenderedSeabedCacheValidationScope : IDisposable
        {
            readonly RenderedVertexSlot[][] savedTables = renderedVertexCaches;
            readonly bool savedCounting = renderedVertexCountQueries, savedBypass = renderedVertexBypass;
            readonly long savedHits = renderedVertexHits, savedMisses = renderedVertexMisses;
            bool disposed;
            public RenderedSeabedCacheValidationScope() => ResetRenderedSeabedVertexCaches();
            public void Dispose()
            {
                if (disposed) return;
                disposed = true; renderedVertexCaches = savedTables;
                renderedVertexCountQueries = savedCounting; renderedVertexBypass = savedBypass;
                renderedVertexHits = savedHits; renderedVertexMisses = savedMisses;
            }
        }

        /// <summary>Inactive owner fixture exercises actual build/active map selection;
        /// never runs Awake, startup, assets, XR, saves, scene construction or gameplay.</summary>
        public sealed class RenderedSeabedOwnerValidationScope : IDisposable
        {
            readonly GameObject root;
            readonly UnderwaterWorld owner;
            bool disposed;
            public RenderedSeabedOwnerValidationScope(OceanEnvironmentKind activeKind)
            {
                root = new GameObject("Inactive rendered seabed owner fixture");
                root.SetActive(false); owner = root.AddComponent<UnderwaterWorld>();
                owner.activeEnvironment = new PreparedEnvironment { kind = activeKind };
                seabedOwners.Add(owner);
            }
            public void SetActive(OceanEnvironmentKind kind)
            { owner.activeEnvironment.kind = kind; owner.buildingEnvironment = null; }
            public void SetBuilding(OceanEnvironmentKind kind) =>
                owner.buildingEnvironment = new PreparedEnvironment { kind = kind };
            public void CancelBuilding() => owner.buildingEnvironment = null;
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                owner.OnDestroy();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
#endif
    }
}

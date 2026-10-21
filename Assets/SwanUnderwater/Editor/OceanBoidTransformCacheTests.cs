#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Old/new production school trajectory comparison on actual Unity Transforms, with detached inactive owners.</summary>
    public static class OceanBoidTransformCacheTests
    {
        public static int LastAssertionCount { get; private set; }
        public static string LastBenchmarkJson { get; private set; }

        public static void RunAll()
        {
            LastAssertionCount = UnderwaterWorld.RunBoidTransformCacheValidation();
            LastBenchmarkJson = JsonUtility.ToJson(UnderwaterWorld.RunBoidTransformCacheMicrobenchmark());
            Debug.Log("BOID_TRANSFORM_CACHE_TESTS_PASSED assertions=" + LastAssertionCount +
                "; actual Unity Transform trajectories, staggered steering, immediate neighbour ordering, parent roundtrips, relocation, terrain holds, bounded time and shader clock; synthetic input/time, no device or wearer acceptance");
            Debug.Log("BOID_TRANSFORM_CACHE_EDITOR_TIMING " + LastBenchmarkJson);
        }

        /// <summary>
        /// Optional Play Mode check of both real frame parities. Every synchronous
        /// suite disposes its fixtures and restores habitat/terrain/RNG before yielding.
        /// No XR input or headset performance acceptance is implied.
        /// </summary>
        public static IEnumerator RunInScene()
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("RunInScene requires actual Play Mode frames.");
            int parityMask = 0, assertions = 0;
            for (int frame = 0; frame < 2; frame++)
            {
                parityMask |= 1 << (Time.frameCount % 2);
                assertions += UnderwaterWorld.RunBoidTransformCacheValidation();
                yield return null;
            }
            if (parityMask != 3)
                throw new InvalidOperationException("Boid validation did not observe both actual frame parities.");
            LastAssertionCount = assertions;
            Debug.Log("BOID_TRANSFORM_CACHE_CROSS_FRAME_TESTS_PASSED assertions=" + assertions +
                "; both actual Unity frame parities, freshly isolated/restored fixtures per frame; no device or wearer acceptance");
        }
    }
}
#endif

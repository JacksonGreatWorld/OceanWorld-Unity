#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SwanUnderwater
{
    public sealed partial class UnderwaterWorld
    {
        // This oracle is frozen before the transform-cache optimization. Keep its
        // trajectory arithmetic and immediate Transform neighbour reads unchanged.
        // Original UpdateBoids + ReefBoidVelocity SHA-256: d18d91e5260a89c1c79a19fa0098b23c1ba8246a99de2d2e76cc3b7d20c39d5b
        const float BoidValidationEpsilon = .00008f;
        static int boidValidationAssertions;

        [Serializable]
        public sealed class BoidTransformCacheBenchmark
        {
            public string scope = "Unity Editor, actual Transform CPU simulation only; no rendering, device, compositor or FPS claim";
            public int fish, schools, warmupPairs, measuredPairs, frameParity;
            public double referenceMicrosecondsPerStep, optimizedMicrosecondsPerStep;
        }

        public static BoidTransformCacheBenchmark LastBoidTransformCacheBenchmark { get; private set; }

        /// <summary>
        /// Real Unity Transform differential checks. Owners never become active, so
        /// production Awake/Start, scene setup, saves and XR are not invoked.
        /// </summary>
        public static int RunBoidTransformCacheValidation()
        {
            boidValidationAssertions = 0;
            Random.State savedRandom = Random.state;
            var savedPockets = new List<ReefHabitat.CoralPocket>(ReefHabitat.Pockets);
            var savedVents = new List<Vector3>(ReefHabitat.BubbleSources);
            try
            {
                ReefHabitat.SetActiveData(new[] { new ReefHabitat.CoralPocket {
                    center = new Vector3(5f, 1f, -1f), shelter = new Vector3(5.1f, 1.1f, -1f), radius = .25f } }, null);
                CheckBoidNeighbourOrder();
                CheckBoidTrajectory(false, false, false);
                CheckBoidTrajectory(true, false, false);
                CheckBoidTrajectory(true, false, true);
                CheckBoidTrajectory(true, true, false);
                CheckBoidTerrainHold();
                CheckBoidInvalidAndBoundedDelta();
                return boidValidationAssertions;
            }
            finally
            {
                ReefHabitat.SetActiveData(savedPockets, savedVents);
                Random.state = savedRandom;
            }
        }

        /// <summary>Descriptive paired timing only; it deliberately has no speed assertion.</summary>
        public static BoidTransformCacheBenchmark RunBoidTransformCacheMicrobenchmark()
        {
            Random.State savedRandom = Random.state;
            try
            {
                using (var old = new BoidValidationFixture("old benchmark", 1200, 12, false, false, false))
                using (var cached = new BoidValidationFixture("cached benchmark", 1200, 12, false, false, false))
                {
                    const int warmup = 16, pairs = 128;
                    long referenceTicks = 0, optimizedTicks = 0;
                    Random.InitState(2940120);
                    for (int i = -warmup; i < pairs; i++)
                    {
                        Random.State seed = Random.state;
                        long start, oldTicks, cachedTicks;
                        if ((i & 1) == 0)
                        {
                            start = Stopwatch.GetTimestamp(); old.world.UpdateBoidsUncachedReference(.02f);
                            oldTicks = Stopwatch.GetTimestamp() - start;
                            Random.state = seed;
                            start = Stopwatch.GetTimestamp(); cached.world.UpdateBoids(.02f);
                            cachedTicks = Stopwatch.GetTimestamp() - start;
                        }
                        else
                        {
                            start = Stopwatch.GetTimestamp(); cached.world.UpdateBoids(.02f);
                            cachedTicks = Stopwatch.GetTimestamp() - start;
                            Random.state = seed;
                            start = Stopwatch.GetTimestamp(); old.world.UpdateBoidsUncachedReference(.02f);
                            oldTicks = Stopwatch.GetTimestamp() - start;
                        }
                        if (i < 0) continue;
                        referenceTicks += oldTicks; optimizedTicks += cachedTicks;
                    }
                    CheckBoidFixturesEqual(old, cached, "benchmark final trajectories");
                    LastBoidTransformCacheBenchmark = new BoidTransformCacheBenchmark {
                        fish = 1200, schools = 12, warmupPairs = warmup, measuredPairs = pairs,
                        frameParity = Time.frameCount % 2,
                        referenceMicrosecondsPerStep = referenceTicks * 1000000d / Stopwatch.Frequency / pairs,
                        optimizedMicrosecondsPerStep = optimizedTicks * 1000000d / Stopwatch.Frequency / pairs };
                    return LastBoidTransformCacheBenchmark;
                }
            }
            finally { Random.state = savedRandom; }
        }

        sealed class BoidValidationFixture : IDisposable
        {
            public readonly UnderwaterWorld world;
            readonly GameObject owner, fishParent;
            public Transform Parent => fishParent.transform;

            public BoidValidationFixture(string name, int count, int schools, bool transformed, bool sanctuary, bool residents)
            {
                owner = new GameObject("Boid validation owner: " + name); owner.SetActive(false);
                world = owner.AddComponent<UnderwaterWorld>(); world.enabled = false;
                fishParent = new GameObject("Boid validation transforms: " + name);
                fishParent.SetActive(false);
                if (transformed)
                {
                    Parent.SetPositionAndRotation(new Vector3(3.3f, -1.7f, 2.1f), Quaternion.Euler(13f, 37f, -8f));
                    Parent.localScale = new Vector3(1.7f, .65f, 1.2f);
                }
                world.activeEnvironment = new PreparedEnvironment {
                    kind = sanctuary ? OceanEnvironmentKind.CoralSanctuary : OceanEnvironmentKind.Classic,
                    biome = OceanCampaignBiome.Reef, root = Parent };
                // Terrain is a shared world query. Publish this isolated owner's kind
                // before initializing either fixture, even inside a live sanctuary scene.
                seabedOwners.Add(world);
                world.boidSchoolCount = schools;
                world.boidMPBs = new MaterialPropertyBlock[count];
                Vector3 basePosition = new Vector3(5f, sanctuary ? 20f : 2f, -1f);
                for (int s = 0; s < schools; s++)
                {
                    Vector3 center = basePosition + new Vector3(s * .5f, 0f, s * .04f);
                    var school = new BoidSchool { center = center, direction = new Vector3(.3f, 0f, 1f).normalized,
                        speed = .85f, turnRate = .63f, fishCount = (count - 1 - s) / schools + 1,
                        scatterTimer = s == 0 && residents ? .015f : 0f };
                    if (residents) { school.pocketIndex = 0; school.pocketIndices = new[] { 0 }; }
                    world.boidSchools.Add(school);
                    if (!sanctuary) continue;
                    Vector3 a = center - Vector3.forward * 2f, c = center + Vector3.forward * 2f;
                    world.sanctuaryPatrols.Add(school, new SanctuaryPatrol {
                        points = new[] { a, center, c }, distances = new[] { 0f, 2f, 4f }, length = 4f,
                        travel = 1.4f, cruiseSpeed = .9f, spread = .2f, home = center });
                    world.sanctuarySchoolHomes.Add(school, center);
                }
                for (int i = 0; i < count; i++)
                {
                    int schoolIndex = i % schools, ordinal = i / schools;
                    var go = new GameObject("boid " + i); go.transform.SetParent(Parent, false);
                    Vector3 heading = new Vector3(.3f, 0f, 1f).normalized;
                    Vector3 position = basePosition + new Vector3(schoolIndex * .5f + ordinal * .013f - .08f, 0f, (ordinal % 3) * .01f);
                    go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(heading));
                    float phase = .23f * i;
                    var motion = OceanSchoolFishMotion.Create(heading, phase, 4.5f); motion.speed = .8f;
                    var fish = new Boid { transform = go.transform, renderers = Array.Empty<Renderer>(),
                        velocity = heading * .8f, desiredVelocity = new Vector3(-.25f, .03f, .6f), motion = motion,
                        formationOffset = new Vector3((ordinal % 4) * .08f, (ordinal % 3) * .06f, -.12f),
                        phase = phase, schoolIndex = schoolIndex, baseScale = .13f,
                        terrainFloor = SampleTerrainHeight(position.x, position.z) + .15f };
                    if (residents)
                    {
                        fish.pocketIndex = 0; fish.reefTarget = basePosition;
                        fish.retargetTimer = .01f; fish.returningToShelter = (i & 1) == 0;
                    }
                    world.boids.Add(fish); world.boidMPBs[i] = new MaterialPropertyBlock();
                }
                BoidCheck(world.mainCamera == null && world.handController == null && !world.IsReady,
                    "isolated fixture never initializes XR, camera or production world");
            }

            public void SetActive(bool active) => fishParent.SetActive(active);

            public void Dispose()
            {
                seabedOwners.Remove(world);
                if (fishParent != null) Object.DestroyImmediate(fishParent);
                if (owner != null) Object.DestroyImmediate(owner);
            }
        }

        static void CheckBoidTrajectory(bool transformed, bool sanctuary, bool residents)
        {
            using (var old = new BoidValidationFixture("old trajectory", 26, 2, transformed, sanctuary, residents))
            using (var cached = new BoidValidationFixture("cached trajectory", 26, 2, transformed, sanctuary, residents))
            {
                Random.InitState(2940210);
                for (int i = 0; i < 64; i++)
                {
                    if (i == 8 || i == 32)
                    {
                        old.SetActive(false); cached.SetActive(false);
                        RelocateBoidParent(old, i); RelocateBoidParent(cached, i);
                        Vector3 moved = old.world.boids[3].transform.position + new Vector3(.9f, .17f, -.3f);
                        old.world.boids[3].transform.position = moved; cached.world.boids[3].transform.position = moved;
                    }
                    if (i == 9 || i == 33) { old.SetActive(true); cached.SetActive(true); }
                    PairedBoidStep(old, cached, i % 9 == 0 ? .5f : .02f, "trajectory " + transformed + "/" + sanctuary + " tick=" + i);
                }
            }
        }

        static void RelocateBoidParent(BoidValidationFixture fixture, int tick)
        {
            Transform t = fixture.Parent;
            t.position += new Vector3(.23f, .05f, -.19f);
            t.rotation = Quaternion.Euler(2f, tick * .7f, -1f) * t.rotation;
            t.localScale = Vector3.Scale(t.localScale, new Vector3(1.03f, .98f, 1.01f));
        }

        static void CheckBoidNeighbourOrder()
        {
            using (var old = new BoidValidationFixture("old neighbour order", 26, 2, false, false, false))
            using (var cached = new BoidValidationFixture("cached neighbour order", 26, 2, false, false, false))
            {
                Vector3[] before = BoidPositions(old.world);
                Vector3[] desiredBefore = new Vector3[old.world.boids.Count];
                for (int i = 0; i < before.Length; i++) desiredBefore[i] = old.world.boids[i].desiredVelocity;
                PairedBoidStep(old, cached, .05f, "in-place neighbour order");
                int parity = Time.frameCount % 2, target = parity + 2;
                var liveAtTurn = (Vector3[])before.Clone();
                for (int i = 0; i < target; i++) liveAtTurn[i] = old.world.boids[i].transform.position;
                Vector3 actualExpected = ClassicBoidDesiredForValidation(old.world, target, before[target], liveAtTurn);
                Vector3 snapshotExpected = ClassicBoidDesiredForValidation(old.world, target, before[target], before);
                BoidNear(old.world.boids[target].desiredVelocity, actualExpected, "later fish sees earlier member after its actual movement");
                BoidCheck(Vector3.Distance(actualExpected, snapshotExpected) > .00001f,
                    "fixture distinguishes immediate neighbour updates from a stale frame snapshot");
                int passive = 1 - parity;
                BoidNear(old.world.boids[passive].desiredVelocity, desiredBefore[passive], "other school retains staggered desired velocity");
                BoidCheck(Vector3.Distance(before[passive], old.world.boids[passive].transform.position) > .001f,
                    "non-steering parity still moves every active tick");
            }
        }

        static Vector3 ClassicBoidDesiredForValidation(UnderwaterWorld world, int i, Vector3 position, Vector3[] neighbors)
        {
            Boid b = world.boids[i]; BoidSchool school = world.boidSchools[b.schoolIndex];
            Vector3 target = school.center + b.formationOffset + Vector3.up * (Mathf.Sin(Time.time * .45f + b.phase) * .15f);
            Vector3 toCenter = target - position;
            Vector3 cohesion = toCenter.magnitude > .01f ? toCenter.normalized * Mathf.Min(toCenter.magnitude * 1.5f, 4f) : Vector3.zero;
            Vector3 separation = Vector3.zero;
            int localIndex = i / world.boidSchoolCount, members = (world.boids.Count - 1 - b.schoolIndex) / world.boidSchoolCount + 1;
            for (int j = 0; j < 4; j++)
            {
                int index = b.schoolIndex + ((localIndex + j * 11 + 1) % members) * world.boidSchoolCount;
                if (index < 0 || index >= neighbors.Length || index == i) continue;
                Vector3 diff = position - neighbors[index]; float distance = diff.magnitude;
                if (distance < .25f && distance > .001f) separation += diff.normalized * (.25f - distance) * 2f;
            }
            return (school.direction * school.speed * 4f + cohesion + separation).normalized * school.speed;
        }

        static void CheckBoidTerrainHold()
        {
            using (var old = new BoidValidationFixture("old terrain hold", 26, 2, true, false, false))
            using (var cached = new BoidValidationFixture("cached terrain hold", 26, 2, true, false, false))
            {
                int index = 1 - Time.frameCount % 2;
                foreach (BoidValidationFixture fixture in new[] { old, cached })
                {
                    Boid b = fixture.world.boids[index];
                    Vector3 p = new Vector3(5f, 0f, -1f); p.y = SampleTerrainHeight(p.x, p.z) + .10f;
                    b.transform.SetPositionAndRotation(p, Quaternion.Euler(20f, 0f, 0f));
                    b.motion = OceanSchoolFishMotion.Create(b.transform.forward, .7f, 4.5f); b.motion.speed = .4f;
                    b.desiredVelocity = Vector3.forward * .6f; b.terrainFloor = p.y + .05f;
                }
                Vector3 before = old.world.boids[index].transform.position;
                Quaternion rotation = old.world.boids[index].transform.rotation;
                PairedBoidStep(old, cached, .02f, "terrain-blocked body");
                Boid held = cached.world.boids[index];
                BoidNear(held.transform.position, before, "blocked translation stays in place");
                BoidCheck(held.velocity == Vector3.zero && held.motion.speed == 0f, "blocked body stops forward translation");
                BoidCheck(Quaternion.Angle(rotation, held.transform.rotation) > .5f, "blocked translation still turns the visible nose upward");
            }
        }

        static void CheckBoidInvalidAndBoundedDelta()
        {
            using (var old = new BoidValidationFixture("old invalid dt", 26, 2, true, false, false))
            using (var cached = new BoidValidationFixture("cached invalid dt", 26, 2, true, false, false))
            {
                foreach (float dt in new[] { 0f, -.02f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    Vector3[] positions = BoidPositions(cached.world);
                    float yaw = cached.world.boids[0].motion.yaw;
                    Vector3 center = cached.world.boidSchools[0].center;
                    PairedBoidStep(old, cached, dt, "invalid dt " + dt);
                    for (int i = 0; i < positions.Length; i++) BoidNear(cached.world.boids[i].transform.position, positions[i], "invalid dt position remains unchanged");
                    BoidNear(cached.world.boidSchools[0].center, center, "invalid dt school remains unchanged");
                    BoidNear(cached.world.boids[0].motion.yaw, yaw, "invalid dt motion remains unchanged");
                }
            }
            using (var bounded = new BoidValidationFixture("bounded .05 dt", 26, 2, true, false, false))
            using (var spike = new BoidValidationFixture("long dt spike", 26, 2, true, false, false))
            {
                Random.State seed = Random.state;
                bounded.world.UpdateBoids(.05f); Random.state = seed; spike.world.UpdateBoids(2f);
                CheckBoidFixturesEqual(bounded, spike, "long frame is bounded to .05 seconds");
            }
        }

        static void PairedBoidStep(BoidValidationFixture old, BoidValidationFixture cached, float dt, string context)
        {
            Random.State seed = Random.state;
            old.world.UpdateBoidsUncachedReference(dt);
            Random.State afterOld = Random.state;
            Random.state = seed;
            cached.world.UpdateBoids(dt);
            BoidCheck(JsonUtility.ToJson(Random.state) == JsonUtility.ToJson(afterOld), context + " preserves random consumption");
            CheckBoidFixturesEqual(old, cached, context);
        }

        static Vector3[] BoidPositions(UnderwaterWorld world)
        {
            var positions = new Vector3[world.boids.Count];
            for (int i = 0; i < positions.Length; i++) positions[i] = world.boids[i].transform.position;
            return positions;
        }

        static void CheckBoidFixturesEqual(BoidValidationFixture old, BoidValidationFixture cached, string context)
        {
            for (int s = 0; s < old.world.boidSchools.Count; s++)
            {
                BoidSchool a = old.world.boidSchools[s], b = cached.world.boidSchools[s];
                BoidNear(a.center, b.center, context + " school center"); BoidNear(a.direction, b.direction, context + " school heading");
                BoidNear(a.speed, b.speed, context + " school speed"); BoidNear(a.scatterTimer, b.scatterTimer, context + " school scatter");
                if (!old.world.sanctuaryPatrols.TryGetValue(a, out SanctuaryPatrol patrolA)) continue;
                SanctuaryPatrol patrolB = cached.world.sanctuaryPatrols[b];
                BoidNear(patrolA.travel, patrolB.travel, context + " sanctuary progress");
                BoidNear(patrolA.velocity, patrolB.velocity, context + " sanctuary velocity");
                BoidNear(patrolA.spread, patrolB.spread, context + " sanctuary spread");
            }
            for (int i = 0; i < old.world.boids.Count; i++)
            {
                Boid a = old.world.boids[i], b = cached.world.boids[i];
                string item = context + " fish=" + i;
                BoidNear(a.transform.position, b.transform.position, item + " world position");
                BoidNear(a.transform.localPosition, b.transform.localPosition, item + " parent-local roundtrip");
                Quaternion qa = a.transform.rotation, qb = b.transform.rotation;
                float same = new Vector4(qa.x - qb.x, qa.y - qb.y, qa.z - qb.z, qa.w - qb.w).magnitude;
                float opposite = new Vector4(qa.x + qb.x, qa.y + qb.y, qa.z + qb.z, qa.w + qb.w).magnitude;
                BoidCheck(Mathf.Min(same, opposite) <= BoidValidationEpsilon, item + " body orientation");
                BoidNear(a.velocity, b.velocity, item + " velocity"); BoidNear(a.desiredVelocity, b.desiredVelocity, item + " desired velocity");
                BoidNear(a.terrainFloor, b.terrainFloor, item + " terrain floor"); BoidNear(a.reefTarget, b.reefTarget, item + " reef target");
                BoidNear(a.escapeTimer, b.escapeTimer, item + " escape timer"); BoidNear(a.retargetTimer, b.retargetTimer, item + " retarget timer");
                BoidCheck(a.returningToShelter == b.returningToShelter && a.inShelter == b.inShelter && a.shelterEntries == b.shelterEntries &&
                    a.handShelterResponses == b.handShelterResponses, item + " refuge state");
                CheckBoidMotionEqual(a.motion, b.motion, item);
                BoidNear(old.world.boidMPBs[i].GetVector("_SwimMotion"), cached.world.boidMPBs[i].GetVector("_SwimMotion"), item + " uploaded swim motion");
                BoidNear(old.world.boidMPBs[i].GetVector("_SchoolClock"), cached.world.boidMPBs[i].GetVector("_SchoolClock"), item + " uploaded continuous clock");
            }
        }

        static void CheckBoidMotionEqual(OceanSchoolFishMotion.State a, OceanSchoolFishMotion.State b, string context)
        {
            BoidNear(a.yaw, b.yaw, context + " yaw"); BoidNear(a.pitch, b.pitch, context + " pitch");
            BoidNear(a.bank, b.bank, context + " bank"); BoidNear(a.speed, b.speed, context + " speed");
            BoidNear(a.phase, b.phase, context + " phase"); BoidNear(a.cadence, b.cadence, context + " cadence");
            BoidNear(a.stroke, b.stroke, context + " stroke"); BoidNear(a.turn, b.turn, context + " turn");
            BoidNear(a.clockTimestamp, b.clockTimestamp, context + " clock timestamp"); BoidNear(a.clockRate, b.clockRate, context + " clock rate");
            BoidCheck(a.clockReady == b.clockReady, context + " clock ready");
        }

        static void BoidNear(float a, float b, string context) => BoidCheck(!float.IsNaN(a) && !float.IsNaN(b) &&
            Mathf.Abs(a - b) <= BoidValidationEpsilon, context + " expected=" + a + " actual=" + b);
        static void BoidNear(Vector3 a, Vector3 b, string context) => BoidCheck((a - b).sqrMagnitude <= BoidValidationEpsilon * BoidValidationEpsilon,
            context + " expected=" + a.ToString("F7") + " actual=" + b.ToString("F7"));
        static void BoidNear(Vector4 a, Vector4 b, string context) => BoidCheck((a - b).sqrMagnitude <= BoidValidationEpsilon * BoidValidationEpsilon,
            context + " expected=" + a + " actual=" + b);
        static void BoidCheck(bool condition, string context)
        {
            boidValidationAssertions++;
            if (!condition) throw new InvalidOperationException("Boid Transform cache differential: " + context);
        }

        private void UpdateBoidsUncachedReference(float dt)
        {
            // Patrol centres and their members must use the same bounded active step.
            // A resumed/long frame otherwise leaves a smooth centre but jumping fish.
            dt = OceanSchoolFishMotion.BoundedDelta(dt);
            if (dt <= 0f) return;
            Vector3 playerPos = mainCamera != null ? mainCamera.transform.position : new Vector3(SpawnX, 0f, SpawnZ);

            // === School-level update: collective direction + gentle turning ===
            for (int s = 0; s < boidSchools.Count; s++)
            {
                var school = boidSchools[s];
                if (UpdateSanctuarySchool(school, s, playerPos, dt)) continue;
                if (school.pocketIndex >= 0)
                {
                    float angle = Time.time * 0.15f + s * 1.7f;
                    school.direction = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    school.center = ReefHabitat.Pockets[school.pocketIndex].center +
                        new Vector3(Mathf.Cos(angle) * 0.6f, 0.25f, Mathf.Sin(angle) * 0.6f);
                    continue; // Residents respond individually at their own coral, not one shared flee point.
                }

                // Use the closest individually tracked hand, including one-hand tracking.
                Vector3 handPos = Vector3.zero;
                float handDistance;
                bool handsActive = handController != null && handController.TryGetClosestActiveHand(school.center, out handPos, out handDistance);
                if (handsActive && school.scatterTimer <= 0f)
                {
                    Vector3 toHand = school.center - handPos;
                    if (toHand.sqrMagnitude < 9f) // 3m detection for group
                    {
                        school.direction = toHand.normalized;
                        school.scatterTimer = 1.5f;
                        school.speed = 2.8f; // burst speed
                    }
                }

                // Recover from scatter
                if (school.scatterTimer > 0f)
                {
                    school.scatterTimer -= dt;
                    if (school.scatterTimer <= 0f)
                        school.speed = Random.Range(0.7f, 1.3f);
                }
                else
                {
                    // Gentle turning: slowly rotate direction for curved path
                    float turn = Mathf.Sin(Time.time * 0.3f + s * 1.7f) * school.turnRate;
                    school.direction = Quaternion.Euler(0f, turn * dt * 30f, 0f) * school.direction;
                    school.direction = school.direction.normalized;
                }

                // Move school center in collective direction
                school.center += school.direction * school.speed * dt;
                // Gentle vertical drift
                float floorAtSchool = SampleTerrainHeight(school.center.x, school.center.z);
                school.center.y = Mathf.MoveTowards(school.center.y,
                    Mathf.Clamp(floorAtSchool + 3.5f, -0.5f, 2.6f), 0.4f * dt);

                Vector3 fromPlayer = SchoolBoundaryOffset(school,playerPos);
                if (fromPlayer.sqrMagnitude > 32f * 32f)
                    school.direction = Vector3.RotateTowards(school.direction,
                        -fromPlayer.normalized, dt * 0.9f, 0f).normalized;

            }

            // === Fish-level update: staggered steering, with movement every frame ===
            int fishPerSchool = boids.Count / boidSchoolCount;
            int updateParity = Time.frameCount % 2; // alternate schools each frame

            for (int i = 0; i < boids.Count; i++)
            {
                var b = boids[i];
                var school = boidSchools[b.schoolIndex];

                // Staggered update: only half the schools do full boids per frame
                // Non-updated schools keep moving with current velocity (direction is stable)
                if (b.schoolIndex % 2 == updateParity)
                {
                    // 1. Alignment: strong shared heading so the whole school travels one way
                    Vector3 alignment = school.direction * school.speed * 4f;

                    // 2. Cohesion: stay near school center
                    // Preserve a loose volume instead of collapsing every fish onto one line.
                    Vector3 formationTarget = school.center + SanctuaryFormationOffset(school, b.formationOffset) +
                        Vector3.up * (Mathf.Sin(Time.time * 0.45f + b.phase) * 0.15f);
                    Vector3 toCenter = formationTarget - b.transform.position;
                    float centerDist = toCenter.magnitude;
                    Vector3 cohesion = centerDist > 0.01f ? toCenter.normalized * Mathf.Min(centerDist * 1.5f, 4f) : Vector3.zero;

                    // 3. Separation: avoid nearby fish (weak, sampled)
                    Vector3 separation = Vector3.zero;
                    int localIndex = i / boidSchoolCount;
                    for (int j = 0; j < 4; j++)
                    {
                        int memberCount = (boids.Count - 1 - b.schoolIndex) / boidSchoolCount + 1;
                        int idx = b.schoolIndex + ((localIndex + j * 11 + 1) % memberCount) * boidSchoolCount;
                        if (idx >= 0 && idx < boids.Count && idx != i)
                        {
                            Vector3 diff = b.transform.position - boids[idx].transform.position;
                            float d = diff.magnitude;
                            if (d < 0.25f && d > 0.001f)
                                separation += diff.normalized * (0.25f - d) * 2f;
                        }
                    }

                    Vector3 desired;
                    if (b.pocketIndex >= 0)
                        desired = ReefBoidVelocityUncachedReference(b, school, separation, dt * 2f);
                    else
                    {
                        desired = (alignment + cohesion + separation).normalized * school.speed;
                        if (school.scatterTimer > 0f) desired = desired.normalized * 2.8f;
                    }

                    desired = SanctuaryBoidVelocity(school, b.transform.position, formationTarget, separation, desired);
                    b.desiredVelocity = desired;
                }

                // Keep above terrain (tiny fish, 0.2m clearance)
                // Re-sample immediately near the bottom; high fish retain staggered terrain queries.
                if ((i + Time.frameCount) % 8 == 0 || b.transform.position.y < b.terrainFloor + 0.4f)
                {
                    Vector3 probe = b.transform.position + Vector3.ProjectOnPlane(b.transform.forward, Vector3.up) * Mathf.Max(.08f, b.baseScale);
                    b.terrainFloor = SampleTerrainHeight(probe.x, probe.z) + .15f;
                }
                Vector3 desiredMotion = b.desiredVelocity;
                if (b.transform.position.y < b.terrainFloor + .12f)
                    desiredMotion.y = Mathf.Max(desiredMotion.y, Mathf.Max(.18f, (b.terrainFloor + .15f - b.transform.position.y) * 2f));
                // Turn the body, then swim along its nose. A cohesion/escape goal behind
                // a fish can no longer pull it backwards while its visible body catches up.
                b.velocity = OceanSchoolFishMotion.Step(ref b.motion, desiredMotion, b.baseScale,
                    b.escapeTimer > 0f || school.scatterTimer > 0f, dt, out Quaternion heading);
                b.transform.rotation = heading;
                Vector3 proposed = b.transform.position + b.velocity * dt;
                if (proposed.y < b.terrainFloor && (b.transform.position.y >= b.terrainFloor || proposed.y <= b.transform.position.y))
                {
                    // Hold translation while the nose turns upward; never hard-set Y
                    // after moving, which made a nose-first fish visibly slide vertically.
                    b.velocity = Vector3.zero;
                    b.motion.speed = 0f;
                }
                else b.transform.position = proposed;
                // Shared material and GPU instancing remain intact. Each fish uploads a
                // speed/turn-driven clock only once per eight frames; GPU advances between.
                if ((i + Time.frameCount) % 8 == 0)
                {
                    MaterialPropertyBlock block = boidMPBs[i];
                    Vector4 clock = OceanSchoolFishMotion.CaptureShaderClock(ref b.motion, b.baseScale, Time.time, out Vector4 swimMotion);
                    block.SetVector("_SwimMotion", swimMotion);
                    block.SetVector("_SchoolClock", clock);
                    foreach (Renderer renderer in b.renderers)
                        if (renderer != null) renderer.SetPropertyBlock(block);
                }
            }
        }

        private Vector3 ReefBoidVelocityUncachedReference(Boid fish, BoidSchool school, Vector3 separation, float dt)
        {
            fish.escapeTimer = Mathf.Max(0f, fish.escapeTimer - dt);
            fish.retargetTimer -= dt;
            Vector3 position = fish.transform.position;
            Vector3 hand = Vector3.zero;
            float handDistance = float.PositiveInfinity;
            bool threatened = handController != null &&
                handController.TryGetClosestActiveHand(position, out hand, out handDistance) && handDistance < 0.9f * 0.9f;
            if (threatened)
            {
                if (fish.escapeTimer <= 0f)
                {
                    fish.handShelterResponses++;
                    fish.pocketIndex = SafeRefugePocket(position, hand, school.pocketIndices, fish.pocketIndex);
                    fish.reefTarget = CoralTarget(fish.pocketIndex, fish.phase, true, 0.05f);
                    fish.inShelter = false;
                }
                fish.escapeTimer = 2.2f;
                fish.returningToShelter = true;
                fish.retargetTimer = 2.8f;
            }
            if (fish.retargetTimer <= 0f && fish.escapeTimer <= 0f)
            {
                fish.returningToShelter = Random.value < 0.32f;
                fish.reefTarget = CoralTarget(fish.pocketIndex, fish.phase + Time.time * 0.35f,
                    fish.returningToShelter, 0.05f);
                fish.retargetTimer = Random.Range(2f, 4.5f);
                fish.inShelter = false;
            }
            Vector3 toTarget = fish.reefTarget - position;
            float distance = toTarget.magnitude;
            if (fish.returningToShelter && distance < 0.16f)
            {
                if (!fish.inShelter) fish.shelterEntries++;
                fish.inShelter = true;
                return toTarget * 0.5f; // Hover inside the individual crevice instead of overshooting it.
            }
            float speed = fish.escapeTimer > 0f ? 1.4f : school.speed;
            speed *= Mathf.Clamp01(distance / (fish.escapeTimer > 0f ? 0.3f : 0.5f));
            Vector3 desired = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized * speed : Vector3.zero;
            if (!fish.returningToShelter) desired += separation * 0.15f;
            if (threatened && handDistance < 0.28f * 0.28f && distance > 0.3f)
                desired += (position - hand).normalized * 0.35f;
            return Vector3.ClampMagnitude(desired, fish.escapeTimer > 0f ? 1.5f : 0.65f);
        }

    }
}
#endif

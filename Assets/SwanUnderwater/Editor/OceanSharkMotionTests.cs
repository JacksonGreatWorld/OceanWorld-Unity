#if UNITY_EDITOR
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual Unity transforms, synthetic time and aim. Does not claim wearer or rendered-animation acceptance.</summary>
    public static class OceanSharkMotionTests
    {
        public static int LastAssertionCount { get; private set; }
        public static void RunAll()
        {
            LastAssertionCount = 0;
            var first = new GameObject("Shark motion test A");
            var second = new GameObject("Shark motion test B");
            var child = new GameObject("Imported visual axis and scale");
            try
            {
                first.transform.position = new Vector3(3f, 4f, -2f);
                first.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
                child.transform.SetParent(first.transform, false);
                child.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                child.transform.localScale = new Vector3(.23f, .23f, .23f);
                Quaternion visualRotation = child.transform.localRotation; Vector3 visualScale = child.transform.localScale;
                var a = first.AddComponent<OceanSharkMotion>(); var b = second.AddComponent<OceanSharkMotion>();
                Vector3 initial = first.transform.position; Quaternion initialRotation = first.transform.rotation;
                int segments = 0; Vector3 lastCheckedFrom = default, lastCheckedTo = default;
                a.Configure(7, .9f, (from, to) => { segments++; lastCheckedFrom = from; lastCheckedTo = to; return true; });
                b.Configure(8, 1.3f, (from, to) => true);
                Check(first.transform.position == initial && Quaternion.Angle(first.transform.rotation, initialRotation) < .001f, "configure does not snap existing body");
                Check(a.EscapeCount == 0 && b.EscapeCount == 0, "per actor counters initially empty");
                Vector3 player = initial - Vector3.forward * 5f;
                float travelled = 0f;
                for (int i = 0; i < 1800; i++)
                {
                    Vector3 before = first.transform.position; Quaternion rotation = first.transform.rotation;
                    a.Tick(.02f, player, false, default, false);
                    CheckStep(first.transform, before, rotation, .02f);
                    CheckHome(a, first.transform.position);
                    if (Vector3.Distance(before, first.transform.position) > .00001f)
                        Check(Vector3.Distance(lastCheckedFrom, before) < .00001f && Vector3.Distance(lastCheckedTo, first.transform.position) < .00001f,
                            "actual movement uses last scene clearance segment");
                    travelled += Vector3.Distance(before, first.transform.position);
                }
                Check(travelled > 10f, "cruise swims useful distance instead of holding");
                Check(segments > 100, "scene clearance is queried through sustained movement");
                Check(child.transform.localScale == visualScale && Quaternion.Angle(child.transform.localRotation, visualRotation) < .001f, "visual scale and imported axis untouched");

                a.Configure(7, .9f, (from, to) => true);
                initial = first.transform.position; initialRotation = first.transform.rotation;
                a.ReactToHit(Vector3.forward, initial - Vector3.forward * 4f);
                Check(a.Escaping && a.EscapeCount == 1 && b.EscapeCount == 0, "hit flees only struck animal");
                Check(first.transform.position == initial && Quaternion.Angle(first.transform.rotation, initialRotation) < .001f, "hit plans without relocating or rotating");
                Check(Vector3.Distance(initial, a.EscapeTarget) >= 2.2f, "escape goal has meaningful travel");
                for (int i = 0; i < 300 && a.Escaping; i++)
                {
                    Vector3 before = first.transform.position; Quaternion rotation = first.transform.rotation;
                    a.Tick(.02f, player, false, default, false);
                    CheckStep(first.transform, before, rotation, .02f); CheckHome(a, first.transform.position);
                }
                Check(!a.Escaping && a.EscapeDistance > 1.5f, "escape ends with actual visible travel");

                // Regression: a hit from behind used to produce a single rigid straight retreat.
                first.transform.SetPositionAndRotation(new Vector3(0f, 4f, 0f), Quaternion.identity);
                a.Configure(23, .9f, (from, to) => true);
                initial = first.transform.position;
                a.ReactToHit(Vector3.forward, initial - Vector3.forward * 4f);
                float lateralBreak = 0f, burstSpeed = 0f;
                for (int i = 0; i < 300 && a.Escaping; i++)
                {
                    Vector3 before = first.transform.position; Quaternion rotation = first.transform.rotation;
                    a.Tick(.02f, initial - Vector3.forward * 4f, false, default, false);
                    CheckStep(first.transform, before, rotation, .02f);
                    lateralBreak = Mathf.Max(lateralBreak, Mathf.Abs(first.transform.position.x - initial.x));
                    burstSpeed = Mathf.Max(burstSpeed, a.Speed);
                }
                Check(lateralBreak > .50f && burstSpeed > 2f && a.EscapeDistance > 2f,
                    "hit breaks laterally then accelerates into a meaningful retreat");

                a.Configure(17, .9f, (from, to) => true);
                Ray ray = new Ray(first.transform.position - first.transform.forward * 5f, first.transform.forward);
                a.Tick(.1f, player, true, ray, false); a.Tick(.1f, player, true, ray, false);
                Check(a.DodgeCount == 0, "aim not dodged instantly");
                for (int i = 0; i < 40 && a.DodgeCount == 0; i++)
                { ray = new Ray(first.transform.position - first.transform.forward * 5f, first.transform.forward); a.Tick(.02f, player, true, ray, false); }
                Check(a.DodgeCount == 1, "sustained gun aim produces lateral dodge");
                for (int i = 0; i < 30; i++) a.Tick(.02f, player, true, ray, false);
                Check(a.DodgeCount == 1, "dodge cooldown prevents every-frame evasion");
                a.ReactToHit(Vector3.forward, first.transform.position - Vector3.forward * 2f);
                for (int i = 0; i < 100; i++) a.Tick(.02f, player, false, default, true);
                Vector3 resting = first.transform.position;
                for (int i = 0; i < 30; i++) a.Tick(.02f, player, true, ray, true);
                Check(a.MotionState == "capture" && a.Speed == 0f && !a.Escaping && first.transform.position == resting, "capture holds reachable earned location despite aiming");

                a.Configure(21, .9f, (from, to) => false);
                initial = first.transform.position;
                a.ReactToHit(Vector3.forward, initial - Vector3.forward * 3f);
                for (int i = 0; i < 200; i++) a.Tick(.02f, player, true, ray, false);
                Check(first.transform.position == initial && !a.Escaping, "unreachable routes cannot enter blocked water");

                a.Configure(22, .9f, (from, to) => to.x <= from.x || to.x < initial.x + .25f);
                for (int i = 0; i < 500; i++)
                {
                    a.Tick(.02f, player, false, default, false);
                    Check(first.transform.position.x <= initial.x + .25001f, "actual segment never crosses obstacle boundary");
                }
                // A valid start and a clear side exit must not become a permanent hold when
                // a broad obstacle blocks the initial fleeing direction and its turning arc.
                first.transform.SetPositionAndRotation(new Vector3(0f, 4f, 0f), Quaternion.identity);
                Vector3 detourStart = first.transform.position;
                Func<Vector3, Vector3, bool> wallClear = (from, to) =>
                {
                    float dz = to.z - from.z;
                    if (Mathf.Abs(dz) < .000001f) return true;
                    float entry = (detourStart.z + 1f - from.z) / dz;
                    if (entry < 0f || entry > 1f) return true;
                    return Mathf.Abs(Mathf.Lerp(from.x, to.x, entry) - detourStart.x) > 1.6f;
                };
                Check(wallClear(detourStart, detourStart) && wallClear(detourStart, detourStart + Vector3.right * 4f),
                    "detour fixture has a legal start and an available clear-water exit");
                a.Configure(31, .9f, wallClear);
                a.ReactToHit(Vector3.forward, detourStart - Vector3.forward * 4f);
                float detourTravel = 0f, hold = 0f, longestHold = 0f;
                for (int frame = 0; frame < 400; frame++)
                {
                    Vector3 before = first.transform.position; Quaternion rotation = first.transform.rotation;
                    a.Tick(.02f, detourStart - Vector3.forward * 4f, false, default, false);
                    CheckStep(first.transform, before, rotation, .02f);
                    Check(wallClear(before, first.transform.position), "actual curved detour does not cross the blocked wall");
                    float step = Vector3.Distance(before, first.transform.position); detourTravel += step;
                    hold = step < .0001f ? hold + .02f : 0f; longestHold = Mathf.Max(longestHold, hold);
                }
                Check(detourTravel > 2f && longestHold < 2.5f, "legal start with a clear detour does not permanently hold");

                CheckGentleYieldAndTurn();

                Vector3 paused = first.transform.position; Quaternion pausedRotation = first.transform.rotation; float pausedSpeed = a.Speed;
                foreach (float invalid in new[] {0f, -.01f, .11f, 1f, float.NaN, float.PositiveInfinity}) a.Tick(invalid, player, true, ray, false);
                a.Tick(.02f, new Vector3(float.NaN, 0f, 0f), true, ray, false);
                Check(first.transform.position == paused && Quaternion.Angle(first.transform.rotation, pausedRotation) < .001f && a.Speed == pausedSpeed, "pause hitches and invalid input do not move or rotate");
                int priorHits = a.EscapeCount;
                a.ReactToHit(new Vector3(float.PositiveInfinity, 0f, 0f), player);
                Check(a.EscapeCount == priorHits, "invalid hit does not mutate state");
                Debug.Log("SHARK_MOTION_TESTS_PASSED assertions=" + LastAssertionCount + "; actual Unity transforms, synthetic time and aiming; rendered swim and physical-hand acceptance remain separate");
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        static void CheckGentleYieldAndTurn()
        {
            var animal = new GameObject("Shark near-diver turn fixture");
            try
            {
                animal.transform.SetPositionAndRotation(Vector3.up * 4f, Quaternion.identity);
                var motion = animal.AddComponent<OceanSharkMotion>();
                motion.Configure(43, .9f, (from, to) => true);
                Vector3 origin = animal.transform.position;
                Quaternion rotation = animal.transform.rotation;
                Vector3 threat = origin + Vector3.right * 4f;
                motion.ReactToHit(Vector3.left, threat);
                Check(motion.Escaping && motion.EscapeCount == 1, "turn fixture uses a real accepted hit");
                motion.Tick(.02f, threat, false, default, false);
                CheckStep(animal.transform, origin, rotation, .02f);
                Check(Quaternion.Angle(rotation, animal.transform.rotation) < OceanSharkMotion.MaximumTurnRate * .02f * .7f,
                    "new lateral escape builds its turn instead of instant full-rate yaw");

                animal.transform.SetPositionAndRotation(origin, Quaternion.identity);
                motion.Configure(43, .9f, (from, to) => true);
                Vector3 player = origin - Vector3.forward * 1.2f;
                float initialDistance = Vector3.Distance(origin, player);
                for (int frame = 0; frame < 30; frame++)
                {
                    Vector3 before = animal.transform.position; Quaternion pose = animal.transform.rotation;
                    motion.Tick(.02f, player, false, default, false);
                    CheckStep(animal.transform, before, pose, .02f); CheckHome(motion, animal.transform.position);
                }
                Check(motion.YieldCount == 1 && Vector3.Distance(animal.transform.position, player) > initialDistance + .1f,
                    "a nearby diver triggers one gentle departure before the old cruise goal expires");
                Check(motion.DodgeCount == 0 && motion.EscapeCount == 0,
                    "player proximity cannot invent a hit or gun-aim event");
                for (int frame = 0; frame < 30; frame++)
                    motion.Tick(.02f, animal.transform.position - Vector3.forward * 1.2f, false, default, false);
                Check(motion.YieldCount == 1, "following diver cannot trigger a new proximity plan every frame");
                motion.ReactToHit(Vector3.forward, player);
                Check(motion.EscapeCount == 1 && motion.Escaping, "a hit still overrides gentle yielding");
                int yields = motion.YieldCount;
                for (int frame = 0; frame < 30; frame++)
                    motion.Tick(.02f, animal.transform.position - Vector3.forward * 1.2f, false, default, false);
                Check(motion.YieldCount == yields, "proximity never replaces an active escape route");

                motion.Configure(43, .9f, (from, to) => true);
                for (int frame = 0; frame < 80; frame++)
                    motion.Tick(.02f, animal.transform.position - Vector3.forward, true,
                        new Ray(animal.transform.position - Vector3.forward, Vector3.forward), true);
                Vector3 readyPosition = animal.transform.position;
                Check(motion.YieldCount == 0 && motion.DodgeCount == 0 && motion.Speed == 0f && motion.MotionState == "capture",
                    "earned capture remains reachable despite nearby diver and aim");
                motion.Tick(.02f, readyPosition, false, default, true);
                Check(animal.transform.position == readyPosition, "capture cannot drift away through proximity avoidance");

                motion.Configure(43, .9f, (from, to) => true);
                for (int frame = 0; frame < 50; frame++)
                    motion.Tick(.02f, origin + Vector3.up * 5f, false, default, false);
                Check(motion.YieldCount == 0, "a vertically distant diver does not start a planar false alarm");
                motion.Configure(43, .9f, (from, to) => false);
                Vector3 blocked = animal.transform.position;
                for (int frame = 0; frame < 180; frame++)
                    motion.Tick(.02f, blocked - Vector3.forward, false, default, false);
                Check(motion.YieldCount == 0 && animal.transform.position == blocked,
                    "proximity cannot manufacture movement through an unavailable water route");
            }
            finally { Object.DestroyImmediate(animal); }
        }

        static void CheckStep(Transform body, Vector3 before, Quaternion rotation, float dt)
        {
            Vector3 movement = body.position - before;
            Check(movement.magnitude <= OceanSharkMotion.MaximumSpeed * dt + .00001f, "bounded world speed");
            Check(Quaternion.Angle(rotation, body.rotation) <= OceanSharkMotion.MaximumTurnRate * dt + .001f, "continuous bounded orientation");
            if (movement.sqrMagnitude > .00000001f) Check(Vector3.Dot(movement.normalized, body.forward) > .999f, "swims nose first without side or reverse slide");
            Check(Mathf.Abs(Mathf.Asin(Mathf.Clamp(body.forward.y, -1f, 1f)) * Mathf.Rad2Deg) <= OceanSharkMotion.MaximumPitch + .01f, "pitch remains anatomically bounded");
            Check(body.up.y > .96f, "no rolls or flipped swimming pose");
        }
        static void CheckHome(OceanSharkMotion motion, Vector3 body)
        {
            Vector3 offset = body - motion.HomePosition;
            Check(Mathf.Abs(offset.y) <= 2.0001f && Vector3.ProjectOnPlane(offset, Vector3.up).magnitude <= OceanSharkMotion.HomeRadius + .0001f, "home volume retained");
        }
        static void Check(bool condition, string message)
        { LastAssertionCount++; if (!condition) throw new InvalidOperationException("Shark motion: " + message); }
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Call after a real world has finished building. Synthetic time/hits; no physical-hand claim.</summary>
    public static class OceanSharkEscapeTests
    {
        public static int LastAssertionCount { get; private set; }
        public static float MinimumEscapeDistance { get; private set; }
        public static void RunInScene(UnderwaterWorld world, Camera view)
        {
            if (world == null || view == null) throw new ArgumentException("A generated world and actual view are required.");
            LastAssertionCount = 0; MinimumEscapeDistance = float.PositiveInfinity;
            var owner = new GameObject("Synthetic shark escape test");
            var state = new OceanCampaignState(); state.StartLevel(5);
            var actors = owner.AddComponent<OceanCampaignCreatures>();
            try
            {
                var reef = ReefBounds(world);
                actors.Initialize(world, view, null, state, null);
                actors.ConfigureLevel(view.transform.position); Physics.SyncTransforms();
                int sharkCount = 0; var identities = new HashSet<int>();
                foreach (var animal in actors.Targets)
                    if (animal.IsShark) { sharkCount++; Check(identities.Add(animal.SharkId), "every visible shark has unique identity"); }
                Check(sharkCount >= 1 && actors.Targets[0].SharkId == 0, "independent quest shark exists; already-owned ambient motion is not stolen from another campaign");
                var target = actors.Targets[0]; Transform body = target.Body.transform;
                Check(state.AcquireTool(), "durable ownership acquired");
                Vector3 firstPosition = body.position;
                for (int hit = 1; hit < 10; hit++)
                {
                    Vector3 before = body.position; Quaternion rotation = body.rotation;
                    Check(state.TryFireSpear() && state.HitShark(), "spent shot accepted " + hit);
                    actors.NotifySharkHit(body.position - view.transform.position);
                    Check(Vector3.Distance(body.position, before) < .000001f && Quaternion.Angle(rotation, body.rotation) < .001f, "hit planning cannot teleport/rotate shark " + hit);
                    int events = actors.SharkEscapeCount; actors.NotifySharkHit(Vector3.forward);
                    Check(actors.SharkEscapeCount == events, "duplicate notification cannot retrigger escape");
                    for (int pause = 0; pause < 10; pause++) actors.TickGameplay(.02f, false);
                    Check(Vector3.Distance(before, body.position) < .000001f, "menu pause freezes escape");
                    for (int frame = 0; frame < 260 && actors.SharkEscaping; frame++)
                    {
                        Vector3 old = body.position; Quaternion oldRotation = body.rotation;
                        actors.TickGameplay(.02f, true); Physics.SyncTransforms();
                        Check(Vector3.Distance(old, body.position) <= OceanSharkMotion.MaximumSpeed * .02f + .00001f, "escape world speed bounded");
                        Check(Quaternion.Angle(oldRotation, body.rotation) <= OceanSharkMotion.MaximumTurnRate * .02f + .001f, "escape rotation remains continuous");
                        if (frame % 20 == 0) CheckSurfaceAndReef(target.Visual, reef);
                    }
                    float moved = Vector3.Distance(before, body.position);
                    MinimumEscapeDistance = Mathf.Min(MinimumEscapeDistance, moved);
                    Check(moved > .60f && !actors.SharkEscaping, "hit " + hit + " produces completed, chaseable escape rather than a stationary wait; distance=" + moved);
                    Check(Mathf.Abs(body.position.x) <= 46f && Mathf.Abs(body.position.z) <= 46f, "quest remains inside player movement area");
                    Debug.Log($"[SharkEscapeTest] hit={hit} distance={moved:F3} from={before:F3} to={body.position:F3}");
                }
                Vector3 finish = body.position;
                Check(Vector3.Distance(firstPosition, finish) > .60f, "escape anchor does not return shark to initial pocket");
                Check(state.TryFireSpear() && state.HitShark(), "tenth hit accepted");
                actors.NotifySharkHit(Vector3.forward);
                for (int frame = 0; frame < 100; frame++) actors.TickGameplay(.02f, true);
                Check(!actors.SharkEscaping && Vector3.Distance(finish, body.position) < .7f, "tenth hit smoothly brakes for capture");
                Vector3 resting = body.position;
                for (int frame = 0; frame < 100; frame++) actors.TickGameplay(.02f, true);
                Check(Vector3.Distance(resting, body.position) < .000001f, "capture-ready shark remains still after braking");
                Physics.SyncTransforms();
                Check(actors.TryGrasp(body.position, true) && state.GoalComplete && state.Stars == 2, "production reach/catch transaction completes after ten hits");
                Check(!target.Visual.activeInHierarchy, "captured shark visual hidden");
                Debug.Log($"SHARK_ESCAPE_TESTS_PASSED assertions={LastAssertionCount} escapes={actors.SharkEscapeCount} minimumTravel={MinimumEscapeDistance:F3}; actual generated reef/terrain and shark, synthetic hits/time, not tracked-hand acceptance");
            }
            finally { Object.DestroyImmediate(owner); }
        }
        static void CheckSurfaceAndReef(GameObject visual, List<Bounds> reef)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            {
                float terrain = UnderwaterWorld.SampleRenderedSeabedHeight(bounds.center.x + bounds.extents.x * x, bounds.center.z + bounds.extents.z * z);
                Check(bounds.min.y > terrain, "actual visible bounds remain above nine-point rendered seabed footprint");
            }
            foreach (Bounds obstacle in reef) Check(!bounds.Intersects(obstacle), "actual visible bounds do not enter reef bounds");
        }
        static List<Bounds> ReefBounds(UnderwaterWorld world)
        {
            var result = new List<Bounds>(); Transform root = world.ActiveReefRoot;
            if (root == null) throw new InvalidOperationException("Real reef habitat is required.");
            foreach (Transform group in root)
            {
                if (group.childCount == 0) continue;
                Renderer[] renderers = group.GetChild(0).GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds; foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                result.Add(bounds);
            }
            Check(result.Count > 0, "real reef geometry present"); return result;
        }
        static void Check(bool value, string detail)
        { LastAssertionCount++; if (!value) throw new InvalidOperationException("Shark escape: " + detail); }
    }
}
#endif

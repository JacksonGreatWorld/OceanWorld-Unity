#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Real imported anatomy and production near-fish owner checks. Synthetic time/goals,
    /// never a claim of tracked hands, headset appearance or performance.</summary>
    public static class OceanNearFishMotionChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static int LastAssertionCount { get; private set; }

        public static void RunAll()
        {
            LastAssertionCount = 0;
            Random.State saved = Random.state;
            try
            {
                Random.InitState(301);
                CheckFlounderAnatomy();
                foreach (float dt in new[] { 1f / 30f, 1f / 72f, 1f / 90f }) CheckSteering(dt);
                CheckActualHeroOwner();
                CheckCampaignBlockedTurn();
                CheckCampaignRearThreatEscape();
                CheckActualLargeNonSharkOwner();
                // The former order translated back before a visually slow Slerp caught up.
                Quaternion old = Quaternion.Slerp(Quaternion.identity, Quaternion.LookRotation(Vector3.back), 2.5f / 90f);
                Check(Vector3.Dot(old * Vector3.forward, Vector3.back) < -.95f,
                    "negative control reproduces previous backwards near-fish skating");
                Debug.Log("OCEAN_NEAR_FISH_MOTION_CHECKS_PASS assertions=" + LastAssertionCount +
                    "; actual flounder GLB head/tail, production hero owner, nose-first turns, upright hover and bounded time. Synthetic validation; headset review remains required.");
            }
            finally { Random.state = saved; }
        }

        static void CheckFlounderAnatomy()
        {
            GameObject prefab = Resources.Load<GameObject>("Fish/22_flounder");
            Check(prefab != null, "actual shipped flounder exists");
            var fish = Object.Instantiate(prefab);
            try
            {
                MeshFilter filter = fish.GetComponentInChildren<MeshFilter>(true);
                Check(filter != null && filter.sharedMesh != null, "actual flounder vertices are available");
                Bounds bounds = filter.sharedMesh.bounds;
                Vector3 head = Vector3.zero, tail = Vector3.zero;
                float headMinY = float.PositiveInfinity, headMaxY = float.NegativeInfinity;
                float tailMinY = float.PositiveInfinity, tailMaxY = float.NegativeInfinity;
                int heads = 0, tails = 0;
                foreach (Vector3 v in filter.sharedMesh.vertices)
                {
                    // glTFast reverses source X. The thick, blunt head is at imported -X;
                    // the other end is a broad but almost planar caudal fin.
                    if (v.x < bounds.center.x - bounds.extents.x * .8f)
                    { head += v; heads++; headMinY = Mathf.Min(headMinY, v.y); headMaxY = Mathf.Max(headMaxY, v.y); }
                    if (v.x > bounds.center.x + bounds.extents.x * .8f)
                    { tail += v; tails++; tailMinY = Mathf.Min(tailMinY, v.y); tailMaxY = Mathf.Max(tailMaxY, v.y); }
                }
                Check(heads > 100 && tails > 100 && headMaxY - headMinY > (tailMaxY - tailMinY) * 3f,
                    "real -X head is substantially thicker than the +X caudal fin");
                head /= heads; tail /= tails;
                Vector3 before = (filter.transform.TransformPoint(head) - filter.transform.TransformPoint(tail)).normalized;
                Check(Mathf.Abs(Vector3.Dot(before, fish.transform.forward)) < .2f,
                    "uncorrected actual flounder is sideways to steering +Z");
                var align = typeof(UnderwaterWorld).GetMethod("AlignSpeciesModel", BindingFlags.Static | BindingFlags.NonPublic);
                Check(align != null, "production visual alignment method exists");
                align.Invoke(null, new object[] { fish, prefab.name });
                Vector3 nose = (filter.transform.TransformPoint(head) - filter.transform.TransformPoint(tail)).normalized;
                Check(Vector3.Dot(nose, fish.transform.forward) > .98f, "production alignment makes actual flounder nose face steering +Z");
                Check(Vector3.Dot(fish.transform.up, Vector3.up) > .9999f, "flatfish body remains flat after visual-only yaw correction");
                Quaternion corrected = filter.transform.rotation;
                align.Invoke(null, new object[] { fish, prefab.name });
                Check(Quaternion.Angle(corrected, filter.transform.rotation) < .0001f, "repeated preparation cannot rotate flounder twice");
                Debug.Log("[NearFishAnatomy] flounder imported head=-X; headY=" + (headMaxY - headMinY).ToString("G7") +
                    " tailY=" + (tailMaxY - tailMinY).ToString("G7") + " correctedNoseDot=" + Vector3.Dot(nose, fish.transform.forward).ToString("G7"));
            }
            finally { Object.DestroyImmediate(fish); }
        }

        static void CheckSteering(float dt)
        {
            Vector3 position = new Vector3(0f, 20f, 0f);
            Quaternion rotation = Quaternion.identity;
            float travelled = 0f;
            for (int i = 0; i < 900; i++)
            {
                Vector3 wanted = i < 150 ? Vector3.back : i < 450 ? new Vector3(1f, .4f, -.5f) : new Vector3(-1f, -.3f, .8f);
                Quaternion previous = rotation;
                Vector3 next = OceanFishSteering.Advance(position, rotation, wanted.normalized * .65f,
                    dt, 140f, false, out rotation);
                Vector3 step = next - position; travelled += step.magnitude;
                if (step.sqrMagnitude > .00000001f)
                    Check(Vector3.Dot(step.normalized, rotation * Vector3.forward) > .9998f, "translation follows visible nose at dt=" + dt);
                Check(Quaternion.Angle(previous, rotation) <= 140f * Mathf.Min(dt, .05f) + .01f,
                    "near-fish turns continuously before travel");
                Check(Mathf.Abs((rotation * Vector3.forward).y) <= Mathf.Sin(OceanFishSteering.MaximumPitch * Mathf.Deg2Rad) + .0001f,
                    "near-fish pitch stays anatomically bounded");
                position = next;
            }
            Check(travelled > 3f, "nose-first turning retains useful travel at dt=" + dt);
            Vector3 anchor = position;
            foreach (float invalid in new[] { 0f, -.1f, float.NaN, float.PositiveInfinity })
                Check(OceanFishSteering.Advance(anchor, rotation, Vector3.forward, invalid, 65f, false, out _) == anchor,
                    "invalid or paused time cannot move a near fish");
            Vector3 bounded = OceanFishSteering.Advance(anchor, Quaternion.identity, Vector3.forward * .65f, 2f, 65f, false, out _);
            Check(Vector3.Distance(bounded, anchor) <= .03251f, "long frame cannot jump a near fish");
            Quaternion hover = Quaternion.identity;
            for (int i = 0; i < 180; i++)
            {
                position = OceanFishSteering.Advance(position, hover, new Vector3(.2f, .1f, -.2f), dt, 65f, true, out hover);
                Check(Vector3.Dot(hover * Vector3.up, Vector3.up) > .99999f, "seahorse remains upright while hovering");
            }
        }

        static void CheckActualHeroOwner()
        {
            Type agentType = typeof(UnderwaterWorld).GetNestedType("FishAgent", BindingFlags.NonPublic);
            Check(agentType != null, "production hero movement owner exists");
            var host = new GameObject("Isolated production near fish owner");
            try
            {
                host.transform.SetPositionAndRotation(new Vector3(0f, 20f, 0f), Quaternion.identity);
                object agent = Activator.CreateInstance(agentType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new object[] { host.transform, .4f, Vector3.back, false, -1, .15f }, null);
                Set(agentType, agent, "pauseTimer", 100f); Set(agentType, agent, "retargetTimer", 100f);
                Set(agentType, agent, "target", host.transform.position + Vector3.back * 5f);
                MethodInfo tick = agentType.GetMethod("Tick");
                Check(tick != null, "production near-fish Tick exists");
                float travel = 0f;
                for (int i = 0; i < 450; i++)
                {
                    Vector3 from = host.transform.position;
                    tick.Invoke(agent, new object[] { 1f / 90f, Vector3.zero, Vector3.one * 30f, null });
                    Vector3 step = host.transform.position - from; travel += step.magnitude;
                    if (step.sqrMagnitude > .00000001f)
                        Check(Vector3.Dot(step.normalized, host.transform.forward) > .9995f,
                            "actual production hero owner never moves sideways/backwards during target reversal");
                }
                Check(travel > .8f, "actual near-fish owner recovers from a target behind its head");
            }
            finally { Object.DestroyImmediate(host); }
        }

        static void CheckCampaignBlockedTurn()
        {
            var owner = new GameObject("Isolated production campaign blocked turn");
            var observer = new GameObject("Isolated campaign observer", typeof(Camera));
            var body = new GameObject("Synthetic body for real private campaign Tick");
            try
            {
                observer.GetComponent<Camera>().enabled = false;
                observer.transform.position = new Vector3(0f, 20f, -10f);
                body.transform.SetPositionAndRotation(new Vector3(0f, 20f, 0f), Quaternion.identity);
                var collider = body.AddComponent<CapsuleCollider>(); collider.isTrigger = true;
                collider.radius = .02f; collider.height = .04f;
                var target = new OceanCampaignCreatures.CreatureTarget();
                Type targetType = typeof(OceanCampaignCreatures.CreatureTarget);
                targetType.GetProperty("Species").SetValue(target, 0);
                targetType.GetProperty("Body").SetValue(target, collider);
                Set(targetType, target, "root", body.transform);
                Set(targetType, target, "anchor", new Vector3(-4f, 20f, -1.5f));
                Set(targetType, target, "clearanceRadius", .03f);
                var state = new OceanCampaignState(); state.StartLevel(3);
                var actors = owner.AddComponent<OceanCampaignCreatures>();
                actors.Initialize(null, observer.GetComponent<Camera>(), null, state, null);
                var targets = (IList)typeof(OceanCampaignCreatures).GetField("targets", Private).GetValue(actors);
                targets.Add(target);
                var solids = (List<Bounds>)typeof(OceanCampaignCreatures).GetField("reefBounds", Private).GetValue(actors);
                // A half-metre fixed obstacle ahead of the conservative .03 m body
                // clearance. Turning past its old forward heading permits a left/back
                // exit. No obstacle is moved/removed to create the recovery.
                solids.Add(new Bounds(new Vector3(0f, 20f, .2312f), new Vector3(.5f, 2f, .4f)));
                MethodInfo clear = typeof(OceanCampaignCreatures).GetMethod("CreatureSegmentClear", Private);
                MethodInfo orbit = typeof(OceanCampaignCreatures).GetMethod("FishOrbit", BindingFlags.Static | BindingFlags.NonPublic);
                FieldInfo phaseField = targetType.GetField("phase", Private);
                FieldInfo anchorField = targetType.GetField("anchor", Private);
                Check(clear != null && orbit != null && phaseField != null && anchorField != null,
                    "real production segment gate, orbit and current target state exist");
                Physics.SyncTransforms();
                int blockedTurningFrames = 0; float travel = 0f; bool movedAfterRealBlock = false;
                for (int i = 0; i < 160; i++)
                {
                    Vector3 previous = body.transform.position;
                    Quaternion previousHeading = body.transform.rotation;
                    // Predict the same immediate candidate from the actual current owner
                    // state, then ask its real private clearance method. A helper-imposed
                    // zero step while poorly aligned is not evidence of an obstacle block.
                    float phase = (float)phaseField.GetValue(target) + .02f * .32f;
                    Vector3 goal = (Vector3)anchorField.GetValue(target) + (Vector3)orbit.Invoke(null, new object[] { phase });
                    Vector3 toward = goal - previous;
                    Vector3 proposed = OceanFishSteering.Advance(previous, previousHeading, toward.normalized * .18f,
                        .02f, 65f, false, out Quaternion expectedHeading, toward.magnitude);
                    bool translationRejected = (proposed - previous).sqrMagnitude > .0000000001f &&
                        !(bool)clear.Invoke(actors, new object[] { previous, proposed, .03f, false });
                    actors.TickGameplay(.02f, true);
                    Vector3 step = body.transform.position - previous;
                    travel += step.magnitude;
                    if (translationRejected)
                    {
                        Check(step.sqrMagnitude < .0000000001f, "the actual segment gate rejects translation into the fixed obstacle");
                        Check(Quaternion.Angle(previousHeading, body.transform.rotation) > .1f &&
                            Quaternion.Angle(expectedHeading, body.transform.rotation) < .001f,
                            "a proven obstacle-rejected step still commits its production turn");
                        blockedTurningFrames++;
                    }
                    if (step.sqrMagnitude > .00000001f)
                    {
                        movedAfterRealBlock |= blockedTurningFrames > 0;
                        Check(Vector3.Dot(step.normalized, body.transform.forward) > .9995f,
                            "production quest fish follows its nose after blocked turning");
                    }
                }
                Check(blockedTurningFrames > 10 && movedAfterRealBlock && travel > .05f,
                    "production private Tick turns through proven obstacle-rejected steps, then swims clear of that same fixed obstacle");
                // Menu pause remains exact; this test does not collect or alter a save.
                Vector3 paused = body.transform.position; Quaternion pausedHeading = body.transform.rotation;
                actors.TickGameplay(.02f, false);
                Check(body.transform.position == paused && Quaternion.Angle(pausedHeading, body.transform.rotation) < .0001f,
                    "menu pause still freezes the quest fish position and heading");
                Debug.Log("[NearFishBlockedTurn] original production Tick+CreatureSegmentClear; provenRejectedTurningFrames=" + blockedTurningFrames +
                    " movedAfterRealBlock=" + movedAfterRealBlock + " unchangedObstacleWidth=.5m travel=" + travel.ToString("G7"));
            }
            finally
            {
                // The fixture owns its body, not the runtime's deferred Destroy path.
                if (owner != null && owner.GetComponent<OceanCampaignCreatures>() != null)
                    ((IList)typeof(OceanCampaignCreatures).GetField("targets", Private).GetValue(owner.GetComponent<OceanCampaignCreatures>())).Clear();
                Object.DestroyImmediate(owner); Object.DestroyImmediate(observer); Object.DestroyImmediate(body);
            }
        }

        static void CheckCampaignRearThreatEscape()
        {
            var owner = new GameObject("Isolated production campaign rear-threat escape");
            var observer = new GameObject("Isolated campaign rear-threat observer", typeof(Camera));
            var body = new GameObject("Synthetic rear-threat body");
            try
            {
                observer.GetComponent<Camera>().enabled = false;
                body.transform.SetPositionAndRotation(new Vector3(0f, 20f, 0f), Quaternion.identity);
                var collider = body.AddComponent<CapsuleCollider>(); collider.isTrigger = true; collider.radius = .02f; collider.height = .04f;
                var target = new OceanCampaignCreatures.CreatureTarget(); Type type = target.GetType();
                type.GetProperty("Species").SetValue(target, 0); type.GetProperty("Body").SetValue(target, collider);
                Set(type, target, "root", body.transform); Set(type, target, "clearanceRadius", .03f);
                var state = new OceanCampaignState(); state.StartLevel(6);
                var actors = owner.AddComponent<OceanCampaignCreatures>(); actors.Initialize(null, observer.GetComponent<Camera>(), null, state, null);
                MethodInfo flee = typeof(OceanCampaignCreatures).GetMethod("TickFishFlee", Private);
                Check(flee != null, "production private fish flee method exists");
                Vector3 origin = body.transform.position, threat = origin + Vector3.forward * .3f;
                Physics.SyncTransforms(); int ticks = 0; float travel = 0f;
                do
                {
                    Vector3 before = body.transform.position;
                    bool fleeing = (bool)flee.Invoke(actors, new object[] { target, threat, .3f, .02f });
                    Check(fleeing, "rear threat starts/continues one real bounded escape");
                    Vector3 step = body.transform.position - before; travel += step.magnitude; ticks++;
                    Check(step.magnitude <= .01601f, "rear-threat escape keeps original .8 m/s cap");
                    if (step.sqrMagnitude > .00000001f)
                        Check(Vector3.Dot(step.normalized, body.transform.forward) > .9995f, "rear-threat escape cannot skate sideways");
                } while (target.IsFleeing && ticks < 60);
                Check(!target.IsFleeing && ticks <= 43 && travel > .12f && travel < .66f,
                    "180-degree away direction uses a reachable lateral break within the original .85 s window");
                Check(target.FleeCount == 1 && target.FleeCooldown >= 3.99f,
                    "rear-threat response preserves the original single escape and catch cooldown");
                Debug.Log("[NearFishRearThreat] actual private TickFishFlee; ticks=" + ticks + " travel=" + travel.ToString("G7") + " cooldown=" + target.FleeCooldown.ToString("G7"));
            }
            finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(observer); Object.DestroyImmediate(body); }
        }

        static void CheckActualLargeNonSharkOwner()
        {
            var host = new GameObject("Inactive actual large non-shark factory"); host.SetActive(false);
            var observer = new GameObject("Large non-shark observer", typeof(Camera));
            Material template = null;
            var ownedMaterials = new HashSet<Material>();
            try
            {
                observer.GetComponent<Camera>().enabled = false; observer.transform.position = new Vector3(0f, 20f, -30f);
                var world = host.AddComponent<UnderwaterWorld>();
                template = new Material(Resources.Load<Shader>("Shaders/FishSwim"));
                Set(typeof(UnderwaterWorld), world, "fishSwimTemplate", template);
                Set(typeof(UnderwaterWorld), world, "mainCamera", observer.GetComponent<Camera>());
                var list = (IList)typeof(UnderwaterWorld).GetField("largeCreatures", Private).GetValue(world);
                Type creatureType = typeof(UnderwaterWorld).GetNestedType("LargeCreature", BindingFlags.NonPublic);
                MethodInfo tick = typeof(UnderwaterWorld).GetMethod("UpdateLargeCreatures", Private);
                Check(creatureType != null && tick != null, "actual large-creature owner exists");
                foreach (string resource in new[] { "Fish/16_manta_ray", "Fish/12_bluefin_tuna" })
                {
                    GameObject model = world.CreateCampaignCreature(resource, host.transform, 1.8f);
                    // The production owner skips inactive animals; give this isolated model an active parent.
                    model.transform.SetParent(null, true); model.transform.SetPositionAndRotation(new Vector3(10f, 20f, 0f), Quaternion.identity);
                    try
                    {
                        MeshFilter filter = model.GetComponentInChildren<MeshFilter>(true); Check(filter != null, "actual large mesh exists: " + resource);
                        Vector3 head = Vector3.zero, tail = Vector3.zero; int heads = 0, tails = 0; Bounds source = filter.sharedMesh.bounds;
                        bool ray = resource.Contains("manta");
                        foreach (Vector3 v in filter.sharedMesh.vertices)
                        {
                            float axis = ray ? -v.x : v.z, center = ray ? -source.center.x : source.center.z, half = ray ? source.extents.x : source.extents.z;
                            if (axis > center + half * .8f) { head += v; heads++; }
                            if (axis < center - half * .8f) { tail += v; tails++; }
                        }
                        Check(heads > 10 && tails > 10, "real non-shark anatomical endpoints exist: " + resource); head /= heads; tail /= tails;
                        Vector3 nose = (filter.transform.TransformPoint(head) - filter.transform.TransformPoint(tail)).normalized;
                        Check(Vector3.Dot(nose, model.transform.forward) > .97f, "production visual correction aligns large non-shark nose: " + resource);
                        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true); Bounds bounds = renderers[0].bounds;
                        foreach (Renderer renderer in renderers)
                            foreach (Material material in renderer.sharedMaterials) if (material != null) ownedMaterials.Add(material);
                        for (int r = 1; r < renderers.Length; r++) bounds.Encapsulate(renderers[r].bounds);
                        Vector3 centerLocal = model.transform.InverseTransformPoint(bounds.center);
                        object animal = Activator.CreateInstance(creatureType, true);
                        Set(creatureType, animal, "transform", model.transform); Set(creatureType, animal, "direction", Vector3.back);
                        Set(creatureType, animal, "speed", .65f); Set(creatureType, animal, "baseScale", 1.8f);
                        Set(creatureType, animal, "localBodyCenter", centerLocal); Set(creatureType, animal, "patrolCenter", new Vector3(0f, 20f, 0f));
                        list.Add(animal); float travel = 0f;
                        for (int i = 0; i < 600; i++)
                        {
                            Vector3 before = model.transform.TransformPoint(centerLocal);
                            tick.Invoke(world, new object[] { .02f });
                            Vector3 step = model.transform.TransformPoint(centerLocal) - before; travel += step.magnitude;
                            if (step.sqrMagnitude <= .00000001f) continue;
                            nose = (filter.transform.TransformPoint(head) - filter.transform.TransformPoint(tail)).normalized;
                            Check(Vector3.Dot(step.normalized, nose) > .97f, "actual UpdateLargeCreatures keeps body centre travel nose-first: " + resource);
                        }
                        Check(travel > 1f, "actual large non-shark owner keeps swimming: " + resource);
                        Debug.Log("[NearFishLargeOwner] " + resource + " real mesh+production UpdateLargeCreatures travel=" + travel.ToString("G7"));
                    }
                    finally { list.Clear(); Object.DestroyImmediate(model); }
                }
            }
            finally
            {
                Object.DestroyImmediate(host); Object.DestroyImmediate(observer);
                foreach (Material material in ownedMaterials) Object.DestroyImmediate(material);
                if (template != null) Object.DestroyImmediate(template);
            }
        }

        static void Set(Type type, object value, string field, object data)
        { FieldInfo info = type.GetField(field, Private | BindingFlags.Public); Check(info != null, "production field exists: " + field); info.SetValue(value, data); }
        static void Check(bool condition, string detail)
        { LastAssertionCount++; if (!condition) throw new InvalidOperationException("Near fish motion: " + detail); }
    }
}
#endif

#if UNITY_EDITOR || OCEAN_SHARK_PATROL_TEST_MAIN
using System;
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#endif

namespace SwanUnderwater.EditorTests
{
    public static class OceanSharkPatrolTests
    {
        public static int LastAssertionCount { get; private set; }
        public static string SceneLargestSharkName { get; private set; }
        public static float SceneLargestEnvelopeDiameter { get; private set; }
        public static int SceneCompletedSmallPasses { get; private set; }
        public static float SceneSmallRouteMaxHorizontalRadius { get; private set; }
        public static float SceneLargestMinDistance { get; private set; }
        public static float SceneSmallestMinDistance { get; private set; }
        public static float SceneMaxStep { get; private set; }
        public static float SceneLargestTravel { get; private set; }
        public static float SceneLongestStopSeconds { get; private set; }
        public static void RunAll()
        {
            LastAssertionCount = 0;
            Check(OceanSharkPatrolPolicy.Largest(new float[] {4.8f, 0, 3.7f, 0}) == 0, "measured full-body envelope selects largest");
            Check(OceanSharkPatrolPolicy.Largest(new float[] {3.1f, 5.2f, 4f}) == 1, "selection independent of roster order/species name");
            Check(OceanSharkPatrolPolicy.Largest(new float[] {0, 0}) == -1, "missing sharks does not select another animal");
            Check(OceanSharkPatrolPolicy.PlayerClearance(4.8f, true) > OceanSharkPatrolPolicy.PlayerClearance(3.7f, false) + 3f,
                "largest shark leaves substantially more body clearance");
            // Regression: a rotated/broad body's largest axis can be smaller while its full envelope is larger.
            float broad = OceanSharkPatrolPolicy.EnvelopeDiameter(3.4f, 1.4f, 3.4f);
            float narrow = OceanSharkPatrolPolicy.EnvelopeDiameter(1f, 1f, 4f);
            Check(3.4f < 4f && broad > narrow && OceanSharkPatrolPolicy.Largest(new float[] {broad,narrow}) == 0,
                "full diagonal prevents largest-axis ordering inversion");
            Check(Math.Abs(broad-OceanSharkPatrolPolicy.EnvelopeDiameter(1.4f,3.4f,3.4f))<.00001f, "axis permutation keeps size metric");
            Check(5f*.95f > 4f*1.08f, "entire current size-jitter range keeps great-white envelope larger than hammerhead");
            float rx = OceanSharkPatrolPolicy.PatrolRadius(5f, 4.8f), rz = OceanSharkPatrolPolicy.PatrolRadius(3f, 4.8f);
            float lastX = 0, lastZ = 0;
            for (int i = 0; i <= 7200; i++)
            {
                float phase = i * (float)Math.PI / 3600;
                OceanSharkPatrolPolicy.Point(6f, 9f, rx, rz, phase, out float x, out float z);
                Check(Math.Abs((x-6f)*(x-6f)/(rx*rx)+(z-9f)*(z-9f)/(rz*rz)-1f) < .00001f, "reef anchored ellipse");
                if (i > 0) Check(Math.Sqrt((x-lastX)*(x-lastX)+(z-lastZ)*(z-lastZ)) < .012f, "route continuous at full lap seam");
                lastX = x; lastZ = z;
                // A diver can approach or cross the patrol; the next target stays beyond body-aware spacing.
                float px = (float)Math.Sin(phase*.7f)*12, pz = (float)Math.Cos(phase)*10;
                float clearance = OceanSharkPatrolPolicy.PlayerClearance(4.8f, true)+2f;
                OceanSharkPatrolPolicy.KeepTargetOutsidePlayer(ref x, ref z, px, pz, clearance, phase);
                Check((x-px)*(x-px)+(z-pz)*(z-pz) >= clearance*clearance-.0001f, "moving diver exclusion");
            }
            float cx = 3, cz = 4;
            OceanSharkPatrolPolicy.KeepTargetOutsidePlayer(ref cx, ref cz, 3, 4, 10, 0);
            Check(Math.Abs(cx-13)<.00001 && Math.Abs(cz-4)<.00001, "coincident target has finite fallback");
#if UNITY_EDITOR
            CheckIntermediateWaterClearance();
            Debug.Log("[SharkPatrolTests] pure PASS assertions=" + LastAssertionCount);
#else
            Console.WriteLine("[SharkPatrolTests] pure PASS assertions=" + LastAssertionCount);
#endif
        }
#if OCEAN_SHARK_PATROL_TEST_MAIN
        public static void Main() => RunAll();
#endif
#if UNITY_EDITOR
        // A forward preview must not accept a route whose next tiny forward step
        // violates the same safety-water contract. Endpoints alone miss this shelf.
        static void CheckIntermediateWaterClearance()
        {
            var host = new GameObject("Isolated ambient shark intermediate-water fixture") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false); // No Awake/Start, production entry, player rig or resource loading.
            UnityEngine.Random.State random = UnityEngine.Random.state;
            try
            {
                var world = host.AddComponent<UnderwaterWorld>();
                const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var reef = (List<Bounds>)typeof(UnderwaterWorld).GetField("largeReefBounds", fields).GetValue(world);
                reef.Add(new Bounds(new Vector3(3.0001f, 37.9f, -2.9f), new Vector3(2f, .2f, 2f)));
                var segment = typeof(UnderwaterWorld).GetMethod("LargeSharkSegmentClear", fields);
                Vector3 body = new Vector3(0f, 40.15f, 0f), heading = new Vector3(1f, 0f, 1f).normalized;
                bool Clear(Vector3 from, Vector3 to) => (bool)segment.Invoke(world, new object[] { from, to, 2f });
                Check(Clear(body, body), "intermediate-water fixture starts in legal water");
                Check(!Clear(body, body + heading * .001f), "tiny forward step sees the intermediate safety-water shelf");
                Vector3 higher = body + Vector3.up * .5f;
                Check(Clear(higher, higher) && Clear(higher, higher + heading * 2.2f),
                    "same route half a metre higher remains legal, not blanket rejection");
                bool previewClear = Clear(body, body + heading * 2.2f);
                Debug.Log("[SharkPatrolIntermediateClearance] longPreviewClear=" + previewClear +
                    "; isolated fixed geometry, original legal origin, .001m short step and 2.2m look-ahead; no scene placement or headset claim");
                Check(!previewClear, "long route preview rejects its intermediate safety-water obstacle instead of accepting a permanently blocked short step");
                Check(!Clear(body + heading * 2.2f, body), "reversing the same route still rejects the intermediate shelf");
                Check(Clear(higher + heading * 2.2f, higher), "reverse high-water route remains traversable");

                Vector3 overReef = new Vector3(3f, higher.y, -2.9f);
                Check(Clear(overReef, overReef + Vector3.up), "pure vertical ascent above the reef retains a valid water column");
                Check(!Clear(overReef, new Vector3(overReef.x, body.y, overReef.z)),
                    "pure vertical descent cannot enter the safety-water shelf");

                // Exercise exact X/Z-parallel tangencies through the production segment
                // API. A neighbouring open lane remains valid at the identical height.
                float footprintMaxZ = reef[0].max.z + 2f, footprintMinX = reef[0].min.x - 2f;
                Vector3 zBoundaryFrom = new Vector3(-1f, body.y, footprintMaxZ);
                Vector3 zBoundaryTo = new Vector3(7f, body.y, footprintMaxZ);
                Check(!Clear(zBoundaryFrom, zBoundaryTo), "constant-Z route on the footprint boundary rejects the intermediate shelf");
                Check(Clear(zBoundaryFrom + Vector3.forward * .01f, zBoundaryTo + Vector3.forward * .01f),
                    "constant-Z open lane outside the footprint is not falsely blocked");
                Vector3 xBoundaryFrom = new Vector3(footprintMinX, body.y, -7f);
                Vector3 xBoundaryTo = new Vector3(footprintMinX, body.y, 1f);
                Check(!Clear(xBoundaryFrom, xBoundaryTo), "constant-X route on the footprint boundary rejects the intermediate shelf");
                Check(Clear(xBoundaryFrom - Vector3.right * .01f, xBoundaryTo - Vector3.right * .01f),
                    "constant-X open lane outside the footprint is not falsely blocked");
                Vector3 highChannelFrom = new Vector3(-1f, higher.y, -2.9f);
                Vector3 highChannelTo = new Vector3(7f, higher.y, -2.9f);
                Check(Clear(highChannelFrom, highChannelTo) && Clear(highChannelTo, highChannelFrom),
                    "high-water channel directly over the full reef is legal in both directions");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Random.state = random;
            }
        }

        sealed class SharkDiagnosticTracker
        {
            public object creature;
            public Vector3 startBody;
            public float travel, stoppedSeconds, longestStoppedSeconds, maximumMeasuredSpeed, maximumPlayerHorizontalDistance;
            public int observedSteps, invalidRouteSteps, zeroSpeedSteps;
        }

        [Serializable] sealed class SharkDiagnosticSnapshot
        {
            public string fish, phase, mode;
            public string scope = "Actual production world and ambient update; synthetic fixed .05s steps, not elapsed PlayMode time, headset FPS or wearer acceptance. No diagnostic placement or ownership changes.";
            public float syntheticSeconds, travel, netDisplacement, longestStoppedSeconds;
            public float nominalSpeed, currentSpeed, meanMeasuredSpeed, maximumMeasuredSpeed;
            public float bodyRadius, playerClearance, playerDistance, maximumPlayerHorizontalDistance, waterHeight, heightMargin;
            public float passTimer, routeTimer, unityTime, timeScale;
            public int observedSteps, invalidRouteSteps, zeroSpeedSteps, passStage, completedPasses;
            public bool routeValid, currentBodyClear, activeInHierarchy, campaignOwned, worldReady;
            public bool entryLoading, entryBlocksGameplay, gameplayAllowed, menuOpen;
            public Vector3 startBody, body, routeDirection, forward, playerBody;
        }

        /// <summary>Actual generated animals, synthetic 90s update. Restores all fish/runtime fields and RNG, no saved scene edits.</summary>
        public static void RunInScene(UnderwaterWorld world, Camera view)
        {
            RunAll();
            Check(world != null && view != null, "generated scene required");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var list = (IList)typeof(UnderwaterWorld).GetField("largeCreatures", flags).GetValue(world);
            var step = typeof(UnderwaterWorld).GetMethod("UpdateLargeCreatures", flags);
            var state = new List<Tuple<object, FieldInfo[], object[], Vector3, Quaternion>>();
            var diagnostics = new List<SharkDiagnosticTracker>();
            UnityEngine.Random.State random = UnityEngine.Random.state;
            FieldInfo nearest = typeof(UnderwaterWorld).GetField("closestSharkDistanceEver", flags);
            object oldNearest = nearest.GetValue(world);
            float maxEnvelope = 0, patrolEnvelope = 0; int patrolCount = 0, smallCount = 0;
            bool greatWhiteSeen = false; int smallPassesBefore = 0;
            SceneCompletedSmallPasses = 0; SceneSmallRouteMaxHorizontalRadius = 0;
            float minPatrolDistance = float.PositiveInfinity, pathLength = 0, stoppedSeconds = 0;
            SceneSmallestMinDistance = float.PositiveInfinity; SceneMaxStep = 0; SceneLongestStopSeconds = 0;
            try
            {
                foreach (object creature in list)
                {
                    FieldInfo[] fields = creature.GetType().GetFields(flags); object[] values = new object[fields.Length];
                    for (int i = 0; i < fields.Length; i++) values[i] = fields[i].GetValue(creature);
                    Transform t = (Transform)Get(creature, "transform");
                    state.Add(Tuple.Create(creature, fields, values, t.position, t.rotation));
                    if ((bool)Get(creature, "isShark")) diagnostics.Add(new SharkDiagnosticTracker
                    {
                        creature = creature, startBody = Body(creature),
                        maximumPlayerHorizontalDistance = Vector3.ProjectOnPlane(Body(creature) - view.transform.position, Vector3.up).magnitude
                    });
                }
                // Capture every animal before the first original assertion can fail. These
                // observations neither relocate an unsafe start nor release campaign ownership.
                foreach (var diagnostic in diagnostics) LogSharkDiagnostic(world, view, diagnostic, "initial");
                foreach (var saved in state)
                {
                    object creature = saved.Item1;
                    Transform t = (Transform)Get(creature, "transform");
                    if (!(bool)Get(creature, "isShark")) continue;
                    float diameter = (float)Get(creature, "bodyEnvelopeDiameter"); maxEnvelope = Mathf.Max(maxEnvelope, diameter);
                    Check(diameter > 0 && (float)Get(creature, "bodyRadius") >= diameter*.5f, "sphere contains measured full body envelope");
                    Check(Mathf.Abs(diameter-(float)Get(creature,"baseScale")) < .01f,
                        "actual generated envelope matches NormalizeFishScale diagonal contract: " + t.name);
                    if (t.name.Contains("15_great_white_shark"))
                    {
                        greatWhiteSeen = true;
                        Check((bool)Get(creature,"reefPatrol"), "current largest visual great white uses reef patrol");
                    }
                    if ((bool)Get(creature, "reefPatrol"))
                    { patrolCount++; patrolEnvelope = diameter; SceneLargestSharkName=t.name; SceneLargestEnvelopeDiameter=diameter; }
                    else { smallCount++; smallPassesBefore += (int)Get(creature,"completedPasses"); }
                }
                Check(greatWhiteSeen, "actual expected great-white resource present, not a fallback model");
                Check(patrolCount == 1 && smallCount >= 1 && Mathf.Abs(maxEnvelope-patrolEnvelope)<.00001f,
                    "exactly actual largest ambient shark is reef-bound; smaller ambient shark remains nearby");
                var clear = typeof(UnderwaterWorld).GetMethod("LargeSharkSegmentClear", flags);
                var args = new object[] {.05f};
                for (int frame = 0; frame < 1800; frame++)
                {
                    var prior = new List<Vector3>();
                    foreach (var saved in state) prior.Add(Body(saved.Item1));
                    step.Invoke(world, args);
                    // Accumulate all subjects before any original movement/clearance assertion
                    // can abort this frame, so finally still reports the other shark honestly.
                    for (int i = 0; i < state.Count; i++)
                    {
                        object creature = state[i].Item1;
                        if (!(bool)Get(creature, "isShark")) continue;
                        SharkDiagnosticTracker diagnostic = diagnostics.Find(row => ReferenceEquals(row.creature, creature));
                        float moved = Vector3.Distance(prior[i], Body(creature));
                        diagnostic.travel += moved;
                        diagnostic.observedSteps++;
                        diagnostic.stoppedSeconds = moved < .0001f ? diagnostic.stoppedSeconds + .05f : 0f;
                        diagnostic.longestStoppedSeconds = Mathf.Max(diagnostic.longestStoppedSeconds, diagnostic.stoppedSeconds);
                        diagnostic.maximumMeasuredSpeed = Mathf.Max(diagnostic.maximumMeasuredSpeed, moved / .05f);
                        diagnostic.maximumPlayerHorizontalDistance = Mathf.Max(diagnostic.maximumPlayerHorizontalDistance,
                            Vector3.ProjectOnPlane(Body(creature) - view.transform.position, Vector3.up).magnitude);
                        if (!(bool)Get(creature, "sharkRouteValid")) diagnostic.invalidRouteSteps++;
                        if ((float)Get(creature, "currentSpeed") <= .00001f) diagnostic.zeroSpeedSteps++;
                    }
                    if ((frame + 1) % 200 == 0)
                        foreach (var diagnostic in diagnostics) LogSharkDiagnostic(world, view, diagnostic, "sample-10s");
                    for (int i = 0; i < state.Count; i++)
                    {
                        object creature = state[i].Item1;
                        if (!(bool)Get(creature, "isShark")) continue;
                        Vector3 body = Body(creature); float moved = Vector3.Distance(prior[i], body);
                        SceneMaxStep = Mathf.Max(SceneMaxStep, moved);
                        Check(moved <= (float)Get(creature, "speed") * 1.1f * .05f + .0001f, "body movement bounded including pivot rotation");
                        if ((bool)Get(creature, "reefPatrol"))
                        {
                            Check((int)Get(creature, "passStage") == 0, "largest never starts close face pass");
                            Check((Vector3)Get(creature, "patrolCenter") == (Vector3)state[i].Item3[Array.FindIndex(state[i].Item2, f=>f.Name=="patrolCenter")], "reef anchor never tracks player");
                            minPatrolDistance = Mathf.Min(minPatrolDistance, Vector3.Distance(body, view.transform.position - Vector3.up*.45f));
                            pathLength += moved;
                            stoppedSeconds = moved < .0001f ? stoppedSeconds + .05f : 0;
                            SceneLongestStopSeconds = Mathf.Max(SceneLongestStopSeconds, stoppedSeconds);
                        }
                        else
                        {
                            SceneSmallestMinDistance = Mathf.Min(SceneSmallestMinDistance,
                                Vector3.Distance(body, view.transform.position - Vector3.up*.45f));
                            if ((int)Get(creature,"passStage") > 0 && frame % 10 == 0)
                            {
                                Vector3 entry=(Vector3)Get(creature,"passEntry"), exit=(Vector3)Get(creature,"passExit");
                                float radius=(float)Get(creature,"bodyRadius");
                                Check((bool)clear.Invoke(world,new object[] {entry,entry,radius}) &&
                                    (bool)clear.Invoke(world,new object[] {exit,exit,radius}), "stored arrival points are in clear water");
                                Check((bool)clear.Invoke(world, new object[] { entry, exit, radius }),
                                    "entire planned side pass clears intermediate reef, not only its endpoints");
                                float horizontal=Mathf.Max(Vector3.ProjectOnPlane(entry-view.transform.position,Vector3.up).magnitude,
                                    Vector3.ProjectOnPlane(exit-view.transform.position,Vector3.up).magnitude);
                                SceneSmallRouteMaxHorizontalRadius=Mathf.Max(SceneSmallRouteMaxHorizontalRadius,horizontal);
                                Check(horizontal<=13f, "raising pass height preserves nearby horizontal route");
                            }
                        }
                        if (frame % 10 == 0)
                            Check((bool)clear.Invoke(world, new object[] {body, body, Get(creature,"bodyRadius")}), "actual body envelope clear of reef and rendered seabed");
                    }
                }
                foreach (var saved in state)
                    if ((bool)Get(saved.Item1,"isShark") && !(bool)Get(saved.Item1,"reefPatrol"))
                        SceneCompletedSmallPasses += (int)Get(saved.Item1,"completedPasses");
                SceneCompletedSmallPasses -= smallPassesBefore;
                Check(SceneCompletedSmallPasses > 0, "smaller shark completes an actual pass within synthetic 90 seconds");
                SceneLargestMinDistance = minPatrolDistance; SceneLargestTravel = pathLength;
                Check(SceneLongestStopSeconds < 5f, "no prolonged stall behind reef");
                Check(pathLength > 35f, "largest swims rather than stalling at obstruction");
                Check(minPatrolDistance >= OceanSharkPatrolPolicy.PlayerClearance(patrolEnvelope,true)-.12f,
                    "largest stays at conservative body envelope radius plus diver buffer");
                foreach (var diagnostic in diagnostics)
                {
                    if ((bool)Get(diagnostic.creature, "reefPatrol")) continue;
                    string name = ((Transform)Get(diagnostic.creature, "transform")).name;
                    Check(diagnostic.longestStoppedSeconds < 5f, "each smaller shark avoids prolonged stalls: " + name +
                        " longestStop=" + diagnostic.longestStoppedSeconds);
                    Check(diagnostic.travel > 35f, "each smaller shark swims usefully throughout synthetic 90 seconds: " + name +
                        " travel=" + diagnostic.travel);
                    Check(diagnostic.maximumPlayerHorizontalDistance <= 25f, "each smaller shark remains visible around the player: " + name +
                        " maximumHorizontalDistance=" + diagnostic.maximumPlayerHorizontalDistance);
                }
                Debug.Log($"[SharkPatrolTests] scene PASS assertions={LastAssertionCount}, syntheticSeconds=90, largestFish={SceneLargestSharkName}, largestEnvelopeDiameter={patrolEnvelope:F2}, minCenterDistance={minPatrolDistance:F2}, pathLength={pathLength:F2}, smallMin={SceneSmallestMinDistance:F2}, maxStep={SceneMaxStep:F4}, longestStop={SceneLongestStopSeconds:F2}, smallCompletedPasses={SceneCompletedSmallPasses}, smallRouteMaxHorizontalRadius={SceneSmallRouteMaxHorizontalRadius:F2}");
            }
            finally
            {
                // Always emit all subjects before restoring their starting state, including
                // a partial run stopped by one of the unchanged assertions above.
                foreach (var diagnostic in diagnostics)
                {
                    try { LogSharkDiagnostic(world, view, diagnostic, "finally-summary"); }
                    catch (Exception error) { Debug.LogWarning("[SharkPatrolDiagnostic] summary unavailable: " + error.Message); }
                }
                foreach (var saved in state)
                {
                    for (int i=0;i<saved.Item2.Length;i++) saved.Item2[i].SetValue(saved.Item1,saved.Item3[i]);
                    Transform t=(Transform)Get(saved.Item1,"transform");t.SetPositionAndRotation(saved.Item4,saved.Item5);
                }
                nearest.SetValue(world,oldNearest);UnityEngine.Random.state=random;
            }
        }
        static void LogSharkDiagnostic(UnderwaterWorld world, Camera view, SharkDiagnosticTracker diagnostic, string phase)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            object creature = diagnostic.creature;
            Transform bodyTransform = (Transform)Get(creature, "transform");
            Vector3 body = Body(creature), player = view.transform.position - Vector3.up * .45f;
            float radius = (float)Get(creature, "bodyRadius");
            float water = (float)typeof(UnderwaterWorld).GetMethod("LargeSharkWaterHeight", fields).Invoke(world, new object[] { body, radius });
            bool clear = (bool)typeof(UnderwaterWorld).GetMethod("LargeSharkSegmentClear", fields).Invoke(world, new object[] { body, body, radius });
            var controlled = (HashSet<Transform>)typeof(UnderwaterWorld).GetField("campaignSharks", fields).GetValue(world);
            var campaign = UnityEngine.Object.FindFirstObjectByType<OceanCampaignController>();
            bool reefPatrol = (bool)Get(creature, "reefPatrol");
            float seconds = diagnostic.observedSteps * .05f;
            Debug.Log("[SharkPatrolDiagnostic] " + JsonUtility.ToJson(new SharkDiagnosticSnapshot
            {
                fish = bodyTransform.name, phase = phase, mode = reefPatrol ? "reef-perimeter" : "nearby-pass",
                syntheticSeconds = seconds, observedSteps = diagnostic.observedSteps,
                startBody = diagnostic.startBody, body = body, travel = diagnostic.travel,
                netDisplacement = Vector3.Distance(diagnostic.startBody, body), longestStoppedSeconds = diagnostic.longestStoppedSeconds,
                nominalSpeed = (float)Get(creature, "speed"), currentSpeed = (float)Get(creature, "currentSpeed"),
                meanMeasuredSpeed = seconds > 0f ? diagnostic.travel / seconds : 0f, maximumMeasuredSpeed = diagnostic.maximumMeasuredSpeed,
                invalidRouteSteps = diagnostic.invalidRouteSteps, zeroSpeedSteps = diagnostic.zeroSpeedSteps,
                routeValid = (bool)Get(creature, "sharkRouteValid"), routeDirection = (Vector3)Get(creature, "sharkRouteDirection"),
                routeTimer = (float)Get(creature, "sharkRouteTimer"), passStage = (int)Get(creature, "passStage"),
                passTimer = (float)Get(creature, "passTimer"), completedPasses = (int)Get(creature, "completedPasses"),
                bodyRadius = radius, playerClearance = OceanSharkPatrolPolicy.PlayerClearance((float)Get(creature, "bodyEnvelopeDiameter"), reefPatrol),
                playerDistance = Vector3.Distance(player, body), maximumPlayerHorizontalDistance = diagnostic.maximumPlayerHorizontalDistance,
                playerBody = player, forward = bodyTransform.forward,
                waterHeight = water, heightMargin = body.y - water, currentBodyClear = clear,
                activeInHierarchy = bodyTransform.gameObject.activeInHierarchy, campaignOwned = controlled.Contains(bodyTransform), worldReady = world.IsReady,
                entryLoading = OceanEntry.IsLoading, entryBlocksGameplay = OceanEntry.BlocksGameplay,
                gameplayAllowed = campaign != null && campaign.GameplayAllowed, menuOpen = campaign != null && campaign.Panel != null && campaign.Panel.IsOpen,
                unityTime = Time.time, timeScale = Time.timeScale
            }));
        }
        static object Get(object value,string name)=>value.GetType().GetField(name, BindingFlags.Instance|BindingFlags.Public).GetValue(value);
        static Vector3 Body(object creature)=>((Transform)Get(creature,"transform")).TransformPoint((Vector3)Get(creature,"localBodyCenter"));
#endif
        static void Check(bool pass,string message)
        {
            LastAssertionCount++;
            if(!pass)throw new InvalidOperationException("[SharkPatrolTests] "+message);
        }
    }
}
#endif

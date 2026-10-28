using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SwanUnderwater
{
    /// <summary>
    /// A bounded expedition station uses the existing fresh hand rays and release-to-arm pinch gate.
    /// It never modifies the tracked camera; all durable events go through OceanCampaignState.
    /// </summary>
    public sealed class OceanMechanicRuntime : MonoBehaviour
    {
        public const float MaximumReach = 6f;
        static readonly string[] RuneSymbols = { "△", "○", "□" };
        public OceanMechanicRules Rules { get; private set; }
        public int VisibleNodeCount => nodes.Count;
        public Transform StationRoot => station != null ? station.transform : null;
        public Vector3 NodePosition(int index) => nodes[index].transform.position;
        public Bounds NodeBounds(int index) => new Bounds(NodePosition(index), Vector3.one * .72f);
        OceanCampaignState state;
        UnderwaterWorld world;
        Camera view;
        HandSwimController hands;
        Action<string> changed;
        public Func<string> MessagePresentation { get; private set; }
        readonly struct HintCopy
        {
            readonly string template;
            readonly object[] values;
            public HintCopy(string template, params object[] values) { this.template = template; this.values = values; }
            public string Source => string.Format(System.Globalization.CultureInfo.InvariantCulture, template, values);
            public string Render() => OceanLanguage.Format(template, values);
        }
        void NotifyHint(HintCopy copy)
        {
            // The receiver retains a renderer over this immutable event snapshot,
            // never a closure that rereads later progress or mechanic stages.
            MessagePresentation = copy.Render;
            try { changed?.Invoke(copy.Source); }
            finally { MessagePresentation = null; }
        }
        OceanLevelRun boundRun;
        GameObject station, carriedVisual, escortVisual;
        readonly GameObject[] observationVisuals = new GameObject[2];
        Text caption;
        Canvas captionCanvas;
        RectTransform captionRoot;
        readonly List<GameObject> nodes = new List<GameObject>(3);
        readonly List<Text> labels = new List<Text>(3);
        readonly List<RectTransform> labelRoots = new List<RectTransform>(3);
        readonly List<Material> ownedMaterials = new List<Material>(8);
        readonly List<Mesh> ownedMeshes = new List<Mesh>(3);
        readonly OceanMenuPressGate[] pinchGates = { new OceanMenuPressGate(), new OceanMenuPressGate() };
        readonly bool[] wasPinching = new bool[2];
        readonly RaycastHit[] occluders = new RaycastHit[24];
        readonly Material[] nodeMaterials = new Material[3];
        Material inactiveMaterial, successMaterial, warningMaterial;
        Light handLight;
        Vector3 spawn, previousHead, mobilePosition, stationCenter, stationForward = Vector3.forward, stationRight = Vector3.right;
        Vector3 initialCarryDirection;
        float planeZ, stationHeight, sectionStarted, nextCaption, eyeHeightAboveFloor;
        float appliedTextScale = -1f;
        int carryingHand = -1;
        bool wasPlaying, headBaseline, explorationAvailable = true;

        public void Initialize(UnderwaterWorld owner, Camera camera, HandSwimController tracking,
            OceanCampaignState campaign, Action<string> onChanged)
        {
            world = owner; view = camera; hands = tracking; state = campaign; changed = onChanged;
            OceanLanguage.Changed -= RefreshLocalizedCopy;
            OceanLanguage.Changed += RefreshLocalizedCopy;
        }

        public void ConfigureLevel(Vector3 levelSpawn)
        {
            Clear(); spawn = levelSpawn; boundRun = state?.Current;
            if (boundRun == null || !OceanCampaignAvailability.IsOpen(boundRun.level) || state.Definition.Mechanic == OceanMechanicKind.None || view == null) return;
            Rules = new OceanMechanicRules(state.Definition.Mechanic, state.Definition.MechanicIntensity,
                boundRun.mechanicProgress, boundRun.mechanicStage);
            state.SetMechanicStage(Rules.Stage);
            station = new GameObject("Hands-first expedition " + state.Definition.Mechanic);
            station.transform.SetParent(transform, false);
            stationHeight = view.transform.position.y - .12f;
            eyeHeightAboveFloor = Mathf.Clamp(view.transform.position.y - FloorAt(spawn), 1.3f, 2.1f);
            planeZ = spawn.z + 2.65f;
            nodeMaterials[0] = MakeMaterial("Mission turquoise", new Color(.18f, .88f, .88f));
            nodeMaterials[1] = MakeMaterial("Mission gold", new Color(1f, .71f, .19f));
            nodeMaterials[2] = MakeMaterial("Mission violet", new Color(.66f, .39f, .95f));
            inactiveMaterial = MakeMaterial("Mission dormant", new Color(.13f, .22f, .27f));
            successMaterial = MakeMaterial("Mission safe", new Color(.18f, .85f, .39f));
            warningMaterial = MakeMaterial("Mission hazard", new Color(.94f, .28f, .31f));
            Mesh primary = OceanMechanicEnvironment.MarkerMesh(Rules.Kind); ownedMeshes.Add(primary);
            Mesh ring = OceanMechanicEnvironment.RingMesh(); ownedMeshes.Add(ring);
            Mesh bin = null;
            if (Rules.Kind == OceanMechanicKind.Cleanup) { bin = OceanMechanicEnvironment.BinMesh(); ownedMeshes.Add(bin); }
            int count = Rules.Kind == OceanMechanicKind.RunePuzzle || Rules.Kind == OceanMechanicKind.Cleanup ||
                Rules.Kind == OceanMechanicKind.OctopusTracking || Rules.Kind == OceanMechanicKind.ElectricFinale ? 3 :
                Rules.Kind == OceanMechanicKind.DeepLight || Rules.Kind == OceanMechanicKind.Observation ||
                Rules.Kind == OceanMechanicKind.CurrentRiding || Rules.Kind == OceanMechanicKind.JellyfishDodge ||
                Rules.Kind == OceanMechanicKind.RingRace ? 1 : 2;
            for (int i = 0; i < count; i++)
            {
                var node = new GameObject("Mission node " + i); node.transform.SetParent(station.transform, false);
                node.AddComponent<MeshFilter>().sharedMesh = i == 1 && (Rules.Kind == OceanMechanicKind.Escort ||
                    Rules.Kind == OceanMechanicKind.FishHerding || Rules.Kind == OceanMechanicKind.TideGates) ? ring : primary;
                if (Rules.Kind == OceanMechanicKind.Cleanup && i > 0) node.GetComponent<MeshFilter>().sharedMesh = bin;
                var renderer = node.AddComponent<MeshRenderer>(); renderer.sharedMaterial = nodeMaterials[i];
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                nodes.Add(node); labels.Add(MakeText(node.transform, "Mission label", new Vector2(340, 70), .0015f, 25));
                // Graphic.canvas is unavailable while a canvas/ancestor is disabled.
                // Keep the node label's owned transform through hide/resume instead.
                labelRoots.Add((RectTransform)labels[i].transform.parent);
                labelRoots[i].localPosition = new Vector3(0, -.46f, 0);
            }
            // Keep the instruction in the lower view band, below the world-anchored node
            // labels. Its old above-node position projected onto the existing gold HUD.
            // Scale and distance preserve the original angular text size without placing
            // this near-field instruction behind the seabed when the player looks down.
            caption = MakeText(station.transform, "Expedition instruction", new Vector2(840, 160), .00070f, 27);
            captionRoot = (RectTransform)caption.transform.parent;
            captionCanvas = captionRoot.GetComponent<Canvas>();
            var plate = new GameObject("Instruction contrast plate", typeof(RectTransform), typeof(Image));
            plate.transform.SetParent(captionRoot, false); plate.transform.SetAsFirstSibling();
            var plateRect = plate.GetComponent<RectTransform>();
            plateRect.anchorMin = Vector2.zero; plateRect.anchorMax = Vector2.one;
            plateRect.offsetMin = plateRect.offsetMax = Vector2.zero;
            var plateImage = plate.GetComponent<Image>(); plateImage.color = new Color(.018f, .075f, .095f, .93f);
            plateImage.raycastTarget = false;
            var captionTextRect = caption.GetComponent<RectTransform>();
            captionTextRect.offsetMin = new Vector2(18, 8); captionTextRect.offsetMax = new Vector2(-18, -8);
            UpdateCaptionPose();
            if (Rules.Kind == OceanMechanicKind.Escort)
            {
                GameObject turtle = Resources.Load<GameObject>("MarineExpansion/sea-turtle");
                if (turtle != null)
                {
                    escortVisual = Instantiate(turtle, nodes[0].transform, false);
                    if (state.Definition.MechanicOptional) PrepareNativeTurtle(escortVisual);
                    else FitVisual(escortVisual, .48f);
                    nodes[0].GetComponent<MeshRenderer>().enabled = false;
                }
            }
            if (Rules.Kind == OceanMechanicKind.Observation && state.Definition.MechanicOptional)
            {
                for (int i = 0; i < observationVisuals.Length; i++)
                {
                    string path = i == 0 ? OceanLevelCatalog.FishPaths[1] : OceanLevelCatalog.FishPaths[0];
                    if (world != null) observationVisuals[i] = world.CreateCampaignCreature(path, nodes[0].transform, .52f);
                    else
                    {
                        GameObject prefab = Resources.Load<GameObject>(path);
                        if (prefab == null) throw new InvalidOperationException("Missing observation species " + path);
                        observationVisuals[i] = Instantiate(prefab, nodes[0].transform, false);
                        FitVisual(observationVisuals[i], .52f);
                    }
                    foreach (Collider collider in observationVisuals[i].GetComponentsInChildren<Collider>(true))
                    { collider.enabled = false; Release(collider); }
                    observationVisuals[i].SetActive(false);
                }
                nodes[0].GetComponent<MeshRenderer>().enabled = false;
            }
            if (Rules.Kind == OceanMechanicKind.DeepLight)
            {
                var lightObject = new GameObject("Tracked hand expedition flashlight");
                lightObject.transform.SetParent(station.transform, false);
                handLight = lightObject.AddComponent<Light>(); handLight.type = LightType.Spot;
                handLight.range = MaximumReach; handLight.spotAngle = 32f; handLight.innerSpotAngle = 18f;
                handLight.color = new Color(.48f, .86f, 1f); handLight.intensity = 1.2f;
                handLight.shadows = LightShadows.None; handLight.enabled = false;
            }
            sectionStarted = boundRun.mechanicElapsedSeconds;
            ResetNodes(); RefreshPresentation(true);
            Debug.Log("[Expedition] configured level=" + boundRun.level + " mechanic=" + Rules.Kind +
                " target=" + state.Definition.MechanicTarget + " nodes=" + nodes.Count + "; no added physics colliders");
        }

        public void TickGameplay(float dt, bool playing)
        {
            if (Rules == null || boundRun == null || boundRun != state.Current || station == null) return;
            UpdateCaptionPose();
            bool active = playing && explorationAvailable && state.CanCollect && !state.MechanicComplete && hands != null &&
                hands.CanReadTrackedVisuals && Finite(dt) && dt > 0 && dt <= .1f;
            if (!active)
            {
                if (wasPlaying) Suspend();
                wasPlaying = false;
                SetCaptionVisible(playing && (state.MechanicComplete || !explorationAvailable));
                return;
            }
            if (!wasPlaying)
            {
                Suspend(); wasPlaying = true; previousHead = view.transform.position; headBaseline = true;
                RefreshPresentation(true); return; // A resumed frame cannot spend pre-menu dt or a held pinch.
            }
            HandSample left = ReadHand(0), right = ReadHand(1);
            TickSamples(dt, left, right, view.transform.position, view.transform.forward);
        }

        struct HandSample
        {
            public bool valid, open;
            public Ray ray;
            public float pinch, motion;
            public int target;
        }
        HandSample ReadHand(int side)
        {
            var result = new HandSample { target = -1 };
            if (!hands.TryGetMenuHandRay((OceanHandSide)side, out result.ray, out result.pinch)) return result;
            result.valid = true;
            result.open = result.pinch <= .2f && !(side == 0 ? hands.LeftGraspActive : hands.RightGraspActive);
            result.motion = (side == 0 ? hands.LeftPalmVelocity : hands.RightPalmVelocity).magnitude;
            result.target = SelectNode(result.ray); return result;
        }
        int SelectNode(Ray ray)
        {
            int selected = -1; float nearest = MaximumReach;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i].activeSelf) continue;
                Bounds bounds = NodeBounds(i);
                if (!bounds.IntersectRay(ray, out float distance) || distance < 0 || distance > nearest || !ClearRay(ray, distance)) continue;
                nearest = distance; selected = i;
            }
            return selected;
        }
        bool ClearRay(Ray ray, float distance)
        {
            if (world == null || world.ActiveEnvironmentRoot == null) return true;
            int count = Physics.RaycastNonAlloc(ray, occluders, Mathf.Max(0, distance - .05f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count >= occluders.Length) return false;
            for (int i = 0; i < count; i++)
                if (occluders[i].transform != null && occluders[i].transform.IsChildOf(world.ActiveEnvironmentRoot)) return false;
            return true;
        }
        void TickSamples(float dt, HandSample left, HandSample right, Vector3 eye, Vector3 forward)
        {
            if (!left.valid) { pinchGates[0].Reset(); wasPinching[0] = false; }
            if (!right.valid) { pinchGates[1].Reset(); wasPinching[1] = false; }
            if (!left.valid && !right.valid) { Suspend(); return; }
            state.UpdateMechanicElapsed(dt);
            float now = boundRun.mechanicElapsedSeconds;
            int frame = Time.frameCount;
#if UNITY_EDITOR
            if (sceneTestFrame >= 0) frame = sceneTestFrame;
#endif
            int lp = pinchGates[0].Step(frame, dt, left.valid, left.pinch, left.target, true);
            int rp = pinchGates[1].Step(frame, dt, right.valid, right.pinch, right.target, true);
            int side = carryingHand >= 0 ? carryingHand : lp >= 0 ? 0 : rp >= 0 ? 1 :
                left.valid && left.target >= 0 ? 0 : right.valid ? 1 : 0;
            HandSample hand = side == 0 ? left : right;
            if (handLight != null)
            {
                handLight.enabled = hand.valid && hand.open;
                if (handLight.enabled)
                { handLight.transform.position = hand.ray.origin; handLight.transform.rotation = Quaternion.LookRotation(hand.ray.direction); }
            }
            bool released = hand.valid && wasPinching[side] && hand.pinch <= OceanMenuPressGate.ReleaseThreshold;
            bool began = (side == 0 ? lp : rp) >= 0;
            bool pinching = hand.valid && hand.pinch > OceanMenuPressGate.ReleaseThreshold;
            var input = new OceanMechanicInput {
                Seconds = dt, ActiveTime = now, Tracked = hand.valid, Target = hand.target,
                PinchBegan = began, Pinching = pinching, Released = released, Open = hand.open,
                HandMotion = hand.motion, GazeAligned = hand.target >= 0 &&
                    Vector3.Dot(forward.normalized, (nodes[hand.target].transform.position - eye).normalized) > .94f,
                Bimanual = left.valid && right.valid && left.open && right.open &&
                    ((left.target == 0 && right.target == 1) || (left.target == 1 && right.target == 0)),
                InCurrent = state.Definition.MechanicOptional ?
                    Vector3.Distance(eye, stationCenter) < 4.5f :
                    Vector3.Distance(eye, new Vector3(spawn.x, eye.y, spawn.z + 1)) < 4.5f
            };
            if (carryingHand >= 0 && hand.valid)
                input.TwistDegrees = Vector3.SignedAngle(initialCarryDirection, hand.ray.direction, Vector3.up);
            if (Rules.Kind == OceanMechanicKind.Cleanup && input.Target > 0)
                input.Target = input.Target == 1 + boundRun.mechanicProgress % 2 ? 1 : 2;
            if (Rules.Kind == OceanMechanicKind.Escort || Rules.Kind == OceanMechanicKind.FishHerding)
            {
                bool steering = hand.valid && hand.open && (Rules.Kind == OceanMechanicKind.Escort || hand.motion > .08f);
                if (steering && TryMissionPlane(hand.ray, out Vector3 target))
                {
                    if (state.Definition.MechanicOptional)
                        target = stationCenter + stationRight * Mathf.Clamp(Vector3.Dot(target - stationCenter, stationRight), -1.7f, 1.7f);
                    else { target.x = Mathf.Clamp(target.x, spawn.x - 1.7f, spawn.x + 1.7f); target.y = stationHeight; }
                    // Escort follows a calm lure; schooling fish require actual hand sweeps.
                    float speed = Rules.Kind == OceanMechanicKind.Escort ? .45f : .70f;
                    Vector3 before = mobilePosition;
                    mobilePosition = Vector3.MoveTowards(mobilePosition, target, dt * speed);
                    nodes[0].transform.position = mobilePosition;
                    if (state.Definition.MechanicOptional && (mobilePosition - before).sqrMagnitude > .000001f)
                        nodes[0].transform.rotation = Quaternion.RotateTowards(nodes[0].transform.rotation,
                            Quaternion.LookRotation(mobilePosition - before), dt * 75f);
                }
                input.Arrived = Vector3.Distance(mobilePosition, nodes[1].transform.position) < .32f;
            }
            if (Rules.Kind == OceanMechanicKind.RingRace)
            {
                input.Arrived = headBaseline && CrossedRing(previousHead, eye, nodes[0].transform.position,
                    state.Definition.MechanicOptional ? stationForward : Vector3.forward,
                    state.Definition.MechanicOptional ? .70f : .45f);
                previousHead = eye; headBaseline = true;
            }
            if (Rules.Kind == OceanMechanicKind.KelpMaze)
            {
                input.Arrived = headBaseline && CrossedRing(previousHead, eye, nodes[Rules.MazeExit].transform.position, .55f);
                input.WrongRoute = headBaseline && CrossedRing(previousHead, eye, nodes[1 - Rules.MazeExit].transform.position, .55f);
                previousHead = eye; headBaseline = true;
            }
            if (Rules.Kind == OceanMechanicKind.CurrentRiding && hand.open && input.InCurrent && hand.target == 0 &&
                (!state.Definition.MechanicOptional || hand.motion > .11f))
                hands?.ApplyEnvironmentalCurrent(state.Definition.MechanicOptional ? stationForward * .12f :
                    new Vector3(Mathf.Sin(now * .35f) * .10f, 0, .12f), dt);
            if (Rules.Kind == OceanMechanicKind.JellyfishDodge)
                input.InCurrent = Vector3.Distance(eye, nodes[0].transform.position) < .72f;
            if (Rules.Kind == OceanMechanicKind.RunePuzzle && now - sectionStarted < 2.7f) input.PinchBegan = false;
            int oldStage = Rules.Stage;
            OceanMechanicOutcome outcome = Rules.Step(input);
            wasPinching[0] = left.valid && left.pinch > OceanMenuPressGate.ReleaseThreshold;
            wasPinching[1] = right.valid && right.pinch > OceanMenuPressGate.ReleaseThreshold;
            if (OceanMechanicRules.CarriesItem(Rules.Kind) && oldStage == 0 && Rules.Stage == 1 && began)
            { carryingHand = side; initialCarryDirection = hand.ray.direction; }
            if (OceanMechanicRules.CarriesItem(Rules.Kind) && Rules.Stage > 0 && hand.valid)
                ShowCarry(hand.ray);
            else ClearCarry();
            if (oldStage != Rules.Stage) state.SetMechanicStage(Rules.Stage);
            if (outcome == OceanMechanicOutcome.Progress)
            {
                int eventId = 100000 + boundRun.mechanicProgress;
                if (state.RecordMechanicProgress(eventId))
                {
                    sectionStarted = now; ResetNodes();
                    NotifyHint(new HintCopy(state.Definition.MechanicOptional ?
                        state.MechanicComplete ? "可选支线完成 · 主目标与星级不受影响" : "支线节点完成 · 沿沙谷寻找下一个点" :
                        state.MechanicComplete ? "探险机制完成！" : "完成一个任务节点 {0}/{1}",
                        boundRun.mechanicProgress, state.Definition.MechanicTarget));
                }
                else Rules = new OceanMechanicRules(state.Definition.Mechanic, state.Definition.MechanicIntensity,
                    boundRun.mechanicProgress, boundRun.mechanicStage);
            }
            else if (outcome == OceanMechanicOutcome.Mistake)
            {
                state.RecordMechanicMistake();
                changed?.Invoke(MistakeHint());
            }
            else if (outcome == OceanMechanicOutcome.StageChanged) NotifyHint(StageCopy());
            RefreshPresentation(outcome != OceanMechanicOutcome.None);
        }

        public static bool CrossedRing(Vector3 from, Vector3 to, Vector3 center, float radius)
            => CrossedRing(from, to, center, Vector3.forward, radius);
        public static bool CrossedRing(Vector3 from, Vector3 to, Vector3 center, Vector3 normal, float radius)
        {
            if (normal.sqrMagnitude < .5f || radius <= 0) return false;
            normal.Normalize();
            float a = Vector3.Dot(from - center, normal), b = Vector3.Dot(to - center, normal);
            if (a >= 0 || b < 0 || (to - from).sqrMagnitude > 1f || Mathf.Abs(b - a) < .00001f) return false;
            Vector3 crossing = Vector3.Lerp(from, to, -a / (b - a));
            return Vector3.ProjectOnPlane(crossing - center, normal).sqrMagnitude <= radius * radius;
        }
        bool TryMissionPlane(Ray ray, out Vector3 point)
        {
            point = default;
            if (state.Definition.MechanicOptional)
            {
                float approach = Vector3.Dot(ray.direction, stationForward);
                if (Mathf.Abs(approach) < .001f) return false;
                float depth = Vector3.Dot(stationCenter - ray.origin, stationForward) / approach;
                if (depth < .1f || depth > MaximumReach) return false;
                point = ray.GetPoint(depth); return true;
            }
            if (Mathf.Abs(ray.direction.z) < .001f) return false;
            float distance = (planeZ - ray.origin.z) / ray.direction.z;
            if (distance < .1f || distance > MaximumReach) return false;
            point = ray.GetPoint(distance); return true;
        }
        void ShowCarry(Ray ray)
        {
            if (carriedVisual == null)
            {
                carriedVisual = new GameObject("Carried mission item"); carriedVisual.transform.SetParent(station.transform, false);
                carriedVisual.AddComponent<MeshFilter>().sharedMesh = nodes[0].GetComponent<MeshFilter>().sharedMesh;
                var renderer = carriedVisual.AddComponent<MeshRenderer>(); renderer.sharedMaterial =
                    Rules.Kind == OceanMechanicKind.Cleanup && boundRun.mechanicProgress % 2 == 1 ? nodeMaterials[1] : nodeMaterials[0];
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            }
            if (TryMissionPlane(ray, out Vector3 point)) carriedVisual.transform.position = point;
            carriedVisual.transform.rotation = Quaternion.Euler(0, 0,
                Vector3.SignedAngle(initialCarryDirection, ray.direction, Vector3.up));
        }
        void ClearCarry()
        { if (carriedVisual != null) Release(carriedVisual); carriedVisual = null; carryingHand = -1; }
        void ResetNodes()
        {
            if (nodes.Count == 0) return;
            if (state.Definition.MechanicOptional) { ResetExplorationNodes(); return; }
            float offset = Mathf.Sin(boundRun.mechanicProgress * 1.4f) * .30f;
            for (int i = 0; i < nodes.Count; i++)
            {
                float x = nodes.Count == 1 ? offset : nodes.Count == 2 ? (i == 0 ? -.85f : .85f) : (i - 1) * .95f;
                nodes[i].transform.position = new Vector3(spawn.x + x, stationHeight, planeZ);
                nodes[i].transform.rotation = Quaternion.identity;
            }
            if (Rules.Kind == OceanMechanicKind.RingRace)
                nodes[0].transform.position = new Vector3(spawn.x + offset, stationHeight + .12f, spawn.z + 2 + boundRun.mechanicProgress * .8f);
            if (Rules.Kind == OceanMechanicKind.KelpMaze)
                for (int i = 0; i < nodes.Count; i++) nodes[i].transform.position =
                    new Vector3(spawn.x + (i == 0 ? -.8f : .8f), stationHeight + .12f, spawn.z + 2 + boundRun.mechanicProgress * .8f);
            if (Rules.Kind == OceanMechanicKind.OctopusTracking)
            {
                int visibleSlot = boundRun.mechanicProgress % 3;
                Vector3 old = nodes[0].transform.position;
                nodes[0].transform.position = nodes[visibleSlot].transform.position;
                nodes[visibleSlot].transform.position = old;
            }
            mobilePosition = nodes[0].transform.position;
            previousHead = view.transform.position; headBaseline = true;
        }
        // These five activities reuse a short, authored sand corridor. Nothing is
        // attached to the head and no collider/current can trap the player's swim.
        void ResetExplorationNodes()
        {
            int step = Mathf.Min(boundRun.mechanicProgress, state.Definition.MechanicTarget - 1);
            float distance = Rules.Kind == OceanMechanicKind.RingRace ? 4.5f + step * 5f :
                Rules.Kind == OceanMechanicKind.Observation ? 6f + step * 9f :
                Rules.Kind == OceanMechanicKind.CurrentRiding ? 6f + step * 8f :
                Rules.Kind == OceanMechanicKind.Escort ? 7f + step * 8f : 7f;
            stationCenter = ExplorationRoutePoint(spawn, distance, out stationForward);
            stationRight = Vector3.Cross(Vector3.up, stationForward).normalized;
            explorationAvailable = TryClearExplorationStation(stationCenter, out Vector3 clearCenter);
            if (!explorationAvailable)
            {
                foreach (GameObject node in nodes) node.SetActive(false);
                Debug.LogWarning("[Expedition] optional route obstructed; main collection remains available level=" + boundRun.level);
                return;
            }
            stationCenter = clearCenter;
            stationHeight = stationCenter.y;
            for (int i = 0; i < nodes.Count; i++)
            {
                float lateral = nodes.Count == 1 ? 0 : nodes.Count == 2 ? (i == 0 ? -.85f : .85f) : (i - 1) * .95f;
                nodes[i].transform.SetPositionAndRotation(stationCenter + stationRight * lateral,
                    Quaternion.LookRotation(stationForward));
                nodes[i].transform.localScale = Vector3.one;
            }
            if (Rules.Kind == OceanMechanicKind.RingRace) nodes[0].transform.localScale = Vector3.one * 1.6f;
            if (Rules.Kind == OceanMechanicKind.Escort) nodes[0].transform.rotation = Quaternion.LookRotation(stationRight);
            if (Rules.Kind == OceanMechanicKind.Observation)
                for (int i = 0; i < observationVisuals.Length; i++)
                    if (observationVisuals[i] != null) observationVisuals[i].SetActive(i == step % observationVisuals.Length);
            mobilePosition = nodes[0].transform.position;
            previousHead = view.transform.position; headBaseline = true;
        }
        public static Vector3 ExplorationRoutePoint(Vector3 origin, float distance, out Vector3 forward)
        {
            var route = OceanCoralSanctuary.ExplorationRouteOffsets;
            distance = Mathf.Max(0, distance); forward = Vector3.forward;
            for (int i = 1; i < route.Count; i++)
            {
                Vector2 delta = route[i] - route[i - 1]; float length = delta.magnitude;
                if (length < .001f) continue;
                forward = new Vector3(delta.x, 0, delta.y) / length;
                if (distance <= length)
                    return origin + new Vector3(route[i - 1].x, 0, route[i - 1].y) + forward * distance;
                distance -= length;
            }
            Vector2 last = route[route.Count - 1]; return origin + new Vector3(last.x, 0, last.y);
        }
        float FloorAt(Vector3 point) => world == null ? spawn.y :
            UnderwaterWorld.SampleRenderedSeabedHeight(point.x, point.z, world.ActiveEnvironment);
        bool TryClearExplorationStation(Vector3 preferred, out Vector3 result)
        {
            // Bounded setup-only search preserves a reachable corridor and samples
            // the same rendered terrain as locomotion, never a flat assumed floor.
            float width = nodes.Count == 3 ? 1.5f : nodes.Count == 2 ? 1.4f : .95f;
            for (int lift = 0; lift < 4; lift++)
            for (int offset = 0; offset < 5; offset++)
            {
                Vector3 p = preferred + stationForward * (offset == 0 ? 0 : (offset % 2 == 1 ? 1 : -1) * ((offset + 1) / 2) * .6f);
                p.y = FloorAt(p) + eyeHeightAboveFloor + lift * .4f;
                bool clear = true;
                if (world != null) foreach (Bounds solid in world.ActiveSolidBounds)
                {
                    Bounds room = new Bounds(p, new Vector3(width * 2f, 1.8f, 1.5f));
                    if (room.Intersects(solid)) { clear = false; break; }
                }
                if (clear) { result = p; return true; }
            }
            // The mission is optional. A blocked authored corridor must not turn
            // into a required unreachable task or move the player to make it fit.
            result = preferred; return false;
        }
        string WaypointHint()
        {
            Vector3 delta = stationCenter - view.transform.position;
            Vector3 flat = Vector3.ProjectOnPlane(delta, Vector3.up);
            float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(view.transform.forward, Vector3.up), flat, Vector3.up);
            string turn = Mathf.Abs(angle) > 110f ? "回头" : angle < -25f ? "向左" : angle > 25f ? "向右" : "向前";
            return OceanLanguage.Format("可选支线 · {0}沿沙谷寻找下个点 · {1}米", OceanLanguage.Text(turn), Mathf.CeilToInt(delta.magnitude));
        }
        static void PrepareNativeTurtle(GameObject model)
        {
            var data = Resources.Load<TextAsset>("MarineExpansion/sea-turtle-geometry");
            var geometry = data == null ? null : JsonUtility.FromJson<OceanAuthoredFishGeometry>(data.text);
            var animator = model.GetComponentInChildren<Animator>(true);
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (geometry == null || !geometry.IsValid || animator == null || animator.runtimeAnimatorController == null || skin == null)
                throw new InvalidOperationException("Escort requires the admitted sea-turtle swim rig and measured geometry.");
            model.transform.localScale *= .70f / geometry.firstPoseDiagonal;
            animator.applyRootMotion = false; animator.enabled = true; skin.updateWhenOffscreen = false;
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) { collider.enabled = false; Release(collider); }
        }
        void Suspend()
        {
            Rules?.Suspend();
            if (state?.Current == boundRun && Rules != null) state.SetMechanicStage(Rules.Stage);
            for (int i = 0; i < 2; i++) { pinchGates[i].Reset(); wasPinching[i] = false; }
            ClearCarry(); headBaseline = false;
            if (handLight != null) handLight.enabled = false;
        }
        void RefreshPresentation(bool force)
        {
            if (caption == null || (!force && Time.unscaledTime < nextCaption)) return;
            nextCaption = Time.unscaledTime + .15f;
            float now = boundRun.mechanicElapsedSeconds;
            SetCaptionVisible(true);
            UpdateCaptionPose();
            RefreshLocalizedCopy();
            if (!explorationAvailable)
                return;
            for (int i = 0; i < nodes.Count; i++)
            {
                var renderer = nodes[i].GetComponent<MeshRenderer>();
                Material material = state.MechanicComplete ? successMaterial : nodeMaterials[i];
                if (!state.MechanicComplete && Rules.Kind == OceanMechanicKind.Cleanup)
                    material = nodeMaterials[i == 0 ? boundRun.mechanicProgress % 2 : i - 1];
                if (Rules.Kind == OceanMechanicKind.JellyfishDodge) material = Rules.JellySafe(now) ? successMaterial : warningMaterial;
                if (Rules.Kind == OceanMechanicKind.OctopusTracking)
                    material = Rules.OctopusVisible(now) && i == 0 ? nodeMaterials[0] : inactiveMaterial;
                if (Rules.Kind == OceanMechanicKind.DeepLight && Rules.Stage == 0) material = inactiveMaterial;
                if (Rules.Kind == OceanMechanicKind.TideGates && i == 1)
                    material = Rules.Stage == 1 && Rules.TideOpen(now) ? successMaterial : warningMaterial;
                if (Rules.Kind == OceanMechanicKind.ElectricFinale && i == 2)
                    material = Rules.Stage == 1 && Rules.ElectricBeat(now) ? successMaterial : inactiveMaterial;
                if (Rules.Kind == OceanMechanicKind.RunePuzzle && now - sectionStarted < 2.7f)
                {
                    int previewStage = Mathf.Min(2, (int)((now - sectionStarted) / .9f));
                    int preview = (boundRun.mechanicProgress + previewStage * 2) % 3;
                    material = i == preview ? successMaterial : inactiveMaterial;
                }
                renderer.sharedMaterial = material;
                labelRoots[i].rotation = view.transform.rotation;
            }
        }
        void RefreshLocalizedCopy()
        {
            // A completed or paused station can retain visible UI without running
            // gameplay ticks. Locale changes affect only copy, never state or pose.
            if (caption == null || Rules == null || boundRun == null || state == null || state.Current != boundRun) return;
            caption.text = !explorationAvailable ? OceanLanguage.Text("可选支线暂不可用 · 继续完成主目标") :
                OceanLanguage.Format("{0}  {1}/{2}{3}\n{4}", OceanLanguage.Text(state.Definition.Name), boundRun.mechanicProgress, state.Definition.MechanicTarget,
                    state.Definition.MechanicOptional ? OceanLanguage.Text(" · 可选支线") : "",
                    state.MechanicComplete ? OceanLanguage.Text(state.Definition.MechanicOptional ?
                        "支线完成 · 主目标与星级不受影响" : "机制完成 · 继续完成本关收藏与金币目标") :
                        state.Definition.MechanicOptional && Vector3.Distance(view.transform.position, stationCenter) > MaximumReach ?
                        WaypointHint() : StageHint());
            for (int i = 0; i < labels.Count; i++)
                if (labels[i] != null) labels[i].text = NodeLabel(i);
        }
        void UpdateCaptionPose()
        {
            if (captionRoot == null || view == null) return;
            // Reuse the player's existing text preference without changing the
            // station mesh or ray-hit bounds. Logical line-wrap boxes stay fixed.
            float scale = hands == null ? 1f : hands.Comfort.MenuScaleMultiplier;
            if (!Mathf.Approximately(appliedTextScale, scale))
            {
                captionRoot.localScale = Vector3.one * (.00070f * scale);
                foreach (RectTransform label in labelRoots)
                    if (label != null) label.localScale = Vector3.one * (.0015f * scale);
                appliedTextScale = scale;
            }
            captionRoot.SetPositionAndRotation(
                view.ViewportToWorldPoint(new Vector3(.5f, .15f, .85f)), view.transform.rotation);
        }
        void SetCaptionVisible(bool visible)
        {
            if (caption == null || captionCanvas == null) return;
            caption.enabled = visible;
            captionCanvas.enabled = visible;
        }
        string NodeLabel(int index) => OceanLanguage.Text(NodeLabelSource(index));
        string NodeLabelSource(int index)
        {
            switch (Rules.Kind)
            {
                case OceanMechanicKind.CurrentRiding: return "洋流导流叶 · 张手划动";
                case OceanMechanicKind.OxygenRelay: return index == 0 ? Rules.BubbleAvailable(boundRun.mechanicElapsedSeconds) ? "补给泡 · 捏合" : "补给尚未浮起" : "缺氧珊瑚 · 捏合送氧";
                case OceanMechanicKind.DeepLight: return Rules.Stage == 0 ? "微弱生物光 · 张手指向" : "已照亮 · 捏合记录";
                case OceanMechanicKind.Observation: return state.Definition.MechanicOptional ?
                    OceanLanguage.Format("{0} · 观察对象", OceanLanguage.Text(boundRun.mechanicProgress % 2 == 0 ? "蓝倒吊" : "小丑鱼")) : "观察对象 · 稳定手射线";
                case OceanMechanicKind.Escort: return index == 0 ? "护送海龟" : "海龟安全圈";
                case OceanMechanicKind.KelpMaze: return index == Rules.MazeExit ? "鱼鳞路标 · 捏合后游过" : "纠缠海藻 · 避开此路";
                case OceanMechanicKind.ShipwreckLock: return index == 0 ? "钥匙 · 按住捏合拾起" : "锁孔 · 偏转钥匙再松开";
                case OceanMechanicKind.JellyfishDodge: return Rules.JellySafe(boundRun.mechanicElapsedSeconds) ? "安全窗 · 捏合珍珠" : "水母放电 · 等待绿色";
                case OceanMechanicKind.OctopusTracking: return "辨认章鱼 · 仅抓亮起真身";
                case OceanMechanicKind.FishHerding: return index == 0 ? "张手挥动引导鱼群" : "鱼群归巢圈";
                case OceanMechanicKind.CoralRepair: return index == 0 ? "活珊瑚枝 · 按住捏合" : "修复底座 · 松开种植";
                case OceanMechanicKind.Cleanup: return index == 0 ? boundRun.mechanicProgress % 2 == 0 ? "塑料瓶 · 按住捏合" : "金属罐 · 按住捏合" :
                    index == 1 ? "塑料回收箱" : "金属回收箱";
                case OceanMechanicKind.TideGates: return index == 0 ? "潮汐开关 · 捏合" : "闸门 · 绿色时捏合通过";
                case OceanMechanicKind.RingRace: return "气泡环 · 划水穿过圆心";
                case OceanMechanicKind.RunePuzzle: return RuneSymbols[index];
                case OceanMechanicKind.ElectricFinale: return index == 2 ? "守卫核心 · 绿灯时捏合" : "电鳗电极 · 双手各指一端";
                default: return "任务节点";
            }
        }
        string StageHint() => StageCopy().Render();
        HintCopy StageCopy()
        {
            switch (Rules.Kind)
            {
                case OceanMechanicKind.CurrentRiding: return new HintCopy("张开手指向导流叶并缓慢划动，推动洋流；中央水流会轻推你");
                case OceanMechanicKind.OxygenRelay: return new HintCopy(Rules.Stage == 0 ? "捏合补给泡，再松手指向右侧珊瑚捏合送氧 · 氧气 {0}秒" : "已携带氧气 · 松手后捏合右侧珊瑚 · 氧气 {0}秒", Mathf.CeilToInt(Rules.Oxygen));
                case OceanMechanicKind.DeepLight: return Rules.Stage == 0 ? new HintCopy("张手指向暗处，保持照明 {0:0.0}秒", Rules.Dwell) : new HintCopy("发光生物已照亮 · 捏合完成记录");
                case OceanMechanicKind.Observation: return new HintCopy(Rules.Stage == 0 ? "头与手同时指向目标，张手稳定观察，再捏合快门" : "对焦完成 · 保持头与手朝向并捏合快门");
                case OceanMechanicKind.Escort: return new HintCopy("张手用射线引导海龟游向右侧安全圈，保持平缓");
                case OceanMechanicKind.KelpMaze: return new HintCopy(Rules.Stage == 0 ? "观察鱼鳞路标，先捏合选择出口，再坐姿划水穿过" : "路标已确认 · 坐姿划水穿过正确岔路圆心");
                case OceanMechanicKind.ShipwreckLock: return new HintCopy(Rules.Stage == 0 ? "捏住钥匙别松手，偏转手射线22°，移到锁孔再松开" : Rules.Stage == 1 ? "保持捏合并偏转钥匙22°" : "钥匙已转动 · 指向右侧锁孔并松开");
                case OceanMechanicKind.JellyfishDodge: return new HintCopy("水母红色放电时避让，绿色安全窗捏合收取珍珠");
                case OceanMechanicKind.OctopusTracking: return new HintCopy("跟踪短暂显形的青色章鱼真身，松开后捏合，避开伪装");
                case OceanMechanicKind.FishHerding: return new HintCopy("张手指向右侧鱼圈并实际挥动，推动小鱼归巢");
                case OceanMechanicKind.CoralRepair: return new HintCopy(Rules.Stage == 0 ? "捏住活珊瑚枝，拖到右侧底座，再松开种植" : "保持捏合拖到右侧底座，松开完成修复");
                case OceanMechanicKind.Cleanup: return new HintCopy("辨认塑料或金属，捏住垃圾拖到同类回收箱，再松开");
                case OceanMechanicKind.TideGates: return new HintCopy(Rules.Stage == 0 ? "先捏合左侧潮汐开关" : "闸门绿色时，松手后捏合右侧闸门通过");
                case OceanMechanicKind.RingRace: return new HintCopy("坐姿划水，头部穿过下一个气泡环；无需站起");
                case OceanMechanicKind.RunePuzzle: return boundRun.mechanicElapsedSeconds - sectionStarted < 2.7f ? new HintCopy("记住三次发光顺序，随后依次捏合符文") : new HintCopy("按刚才顺序捏合三个符文 · 已输入 {0}/3", Rules.Stage);
                case OceanMechanicKind.ElectricFinale: return new HintCopy(Rules.Stage == 0 ? "双手张开各指向一个电极，持续充能；随后对准核心" : "已充能 · 核心绿灯节拍时捏合发射");
                default: return new HintCopy(state.Definition.MechanicHint);
            }
        }
        string MistakeHint()
        {
            if (Rules.Kind == OceanMechanicKind.OxygenRelay) return "氧气补给中断 · 返回补给泡重新接力";
            if (OceanMechanicRules.CarriesItem(Rules.Kind)) return "物品未正确送达 · 松开双手后重新拾取";
            if (Rules.Kind == OceanMechanicKind.RunePuzzle) { sectionStarted = boundRun.mechanicElapsedSeconds; return "符文顺序不符 · 再看一次发光顺序"; }
            return "先观察安全窗口与目标提示，再松手重试";
        }
        Material MakeMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, enableInstancing = true, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .25f);
            ownedMaterials.Add(material); return material;
        }
        static Text MakeText(Transform parent, string name, Vector2 size, float scale, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas)); go.transform.SetParent(parent, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.GetComponent<RectTransform>().sizeDelta = size; go.transform.localScale = Vector3.one * scale;
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text)); textObject.transform.SetParent(go.transform, false);
            var rect = textObject.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            Text text = textObject.GetComponent<Text>(); text.font = Resources.Load<Font>("Treasure/TreasureFont");
            text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter; text.color = new Color(.96f, .985f, .98f); text.raycastTarget = false;
            // A single offset copy adds separation on bright sand and moving fish
            // without another canvas, material, input target or full-screen pass.
            var shadow = textObject.AddComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = new Color(.004f, .018f, .025f, .94f);
            shadow.effectDistance = new Vector2(1.25f, -1.25f);
            shadow.useGraphicAlpha = true;
            return text;
        }
        static void FitVisual(GameObject visual, float length)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds; for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (size > .001f) visual.transform.localScale *= length / size;
            foreach (var collider in visual.GetComponentsInChildren<Collider>()) Release(collider);
            foreach (var renderer in renderers) { renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; }
        }
        internal void ClearForUnavailableLevel() => Clear();
        void Clear()
        {
            if (station != null) { station.SetActive(false); Release(station); }
            station = carriedVisual = escortVisual = null; observationVisuals[0] = observationVisuals[1] = null;
            caption = null; captionCanvas = null; captionRoot = null; nodes.Clear(); labels.Clear(); labelRoots.Clear();
            appliedTextScale = -1f;
            foreach (var mesh in ownedMeshes) if (mesh != null) Release(mesh); ownedMeshes.Clear();
            foreach (var material in ownedMaterials) if (material != null) Release(material); ownedMaterials.Clear();
            handLight = null;
            Rules = null; boundRun = null; carryingHand = -1; wasPlaying = false; headBaseline = false; explorationAvailable = true;
            for (int i = 0; i < 2; i++) { pinchGates[i].Reset(); wasPinching[i] = false; }
        }
        void OnDisable() { if (Rules != null) Suspend(); }
        void OnDestroy() { OceanLanguage.Changed -= RefreshLocalizedCopy; Clear(); }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void Release(UnityEngine.Object value)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { DestroyImmediate(value); return; }
#endif
            Destroy(value);
        }

#if UNITY_EDITOR
        public RectTransform CaptionRectForValidation => captionRoot;
        public bool ExplorationAvailableForValidation => explorationAvailable;
        public Vector3 ExplorationCenterForValidation => stationCenter;
        public GameObject ObservationVisualForValidation(int index) => observationVisuals[index];
        public GameObject EscortVisualForValidation => escortVisual;
        public RectTransform NodeLabelRectForValidation(int index) => labelRoots[index];
        public void RefreshCaptionPoseForValidation() => UpdateCaptionPose();
        /// <summary>Scene harness supplies explicit synthetic rays; never called by shipped input or device validation.</summary>
        public void StepSceneTest(float dt, int sampleFrame, Ray leftRay, float leftPinch, bool leftValid,
            Ray rightRay, float rightPinch, bool rightValid, Vector3 eye, Vector3 forward, float motion = .4f)
        {
            // Test frames may advance without actual rendered frames; keep the same ray/occlusion
            // selection and qualified pinch implementation as the real TickSamples path.
            sceneTestFrame = sampleFrame;
            var left = new HandSample { valid = leftValid, ray = leftRay, pinch = leftPinch, open = leftPinch <= .2f,
                motion = motion, target = leftValid ? SelectNode(leftRay) : -1 };
            var right = new HandSample { valid = rightValid, ray = rightRay, pinch = rightPinch, open = rightPinch <= .2f,
                motion = motion, target = rightValid ? SelectNode(rightRay) : -1 };
            TickSamples(dt, left, right, eye, forward); sceneTestFrame = -1;
        }
        int sceneTestFrame = -1;
        public void SuspendSceneTest() => Suspend();
#endif
    }
}

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
        OceanLevelRun boundRun;
        GameObject station, carriedVisual, escortVisual;
        Text caption;
        Canvas captionCanvas;
        RectTransform captionRoot;
        readonly List<GameObject> nodes = new List<GameObject>(3);
        readonly List<Text> labels = new List<Text>(3);
        readonly List<Material> ownedMaterials = new List<Material>(8);
        readonly List<Mesh> ownedMeshes = new List<Mesh>(3);
        readonly OceanMenuPressGate[] pinchGates = { new OceanMenuPressGate(), new OceanMenuPressGate() };
        readonly bool[] wasPinching = new bool[2];
        readonly RaycastHit[] occluders = new RaycastHit[24];
        readonly Material[] nodeMaterials = new Material[3];
        Material inactiveMaterial, successMaterial, warningMaterial;
        Light handLight;
        Vector3 spawn, previousHead, mobilePosition;
        Vector3 initialCarryDirection;
        float planeZ, stationHeight, sectionStarted, nextCaption;
        int carryingHand = -1;
        bool wasPlaying, headBaseline;

        public void Initialize(UnderwaterWorld owner, Camera camera, HandSwimController tracking,
            OceanCampaignState campaign, Action<string> onChanged)
        { world = owner; view = camera; hands = tracking; state = campaign; changed = onChanged; }

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
                labels[i].canvas.transform.localPosition = new Vector3(0, -.46f, 0);
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
                    FitVisual(escortVisual, .48f);
                    nodes[0].GetComponent<MeshRenderer>().enabled = false;
                }
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
            bool active = playing && state.CanCollect && !state.MechanicComplete && hands != null &&
                hands.CanReadTrackedVisuals && Finite(dt) && dt > 0 && dt <= .1f;
            if (!active)
            {
                if (wasPlaying) Suspend();
                wasPlaying = false;
                SetCaptionVisible(playing && state.MechanicComplete);
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
                InCurrent = Vector3.Distance(eye, new Vector3(spawn.x, eye.y, spawn.z + 1)) < 4.5f
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
                    target.x = Mathf.Clamp(target.x, spawn.x - 1.7f, spawn.x + 1.7f); target.y = stationHeight;
                    // Escort follows a calm lure; schooling fish require actual hand sweeps.
                    float speed = Rules.Kind == OceanMechanicKind.Escort ? .45f : .70f;
                    mobilePosition = Vector3.MoveTowards(mobilePosition, target, dt * speed);
                    nodes[0].transform.position = mobilePosition;
                }
                input.Arrived = Vector3.Distance(mobilePosition, nodes[1].transform.position) < .32f;
            }
            if (Rules.Kind == OceanMechanicKind.RingRace)
            {
                input.Arrived = headBaseline && CrossedRing(previousHead, eye, nodes[0].transform.position, .45f);
                previousHead = eye; headBaseline = true;
            }
            if (Rules.Kind == OceanMechanicKind.KelpMaze)
            {
                input.Arrived = headBaseline && CrossedRing(previousHead, eye, nodes[Rules.MazeExit].transform.position, .55f);
                input.WrongRoute = headBaseline && CrossedRing(previousHead, eye, nodes[1 - Rules.MazeExit].transform.position, .55f);
                previousHead = eye; headBaseline = true;
            }
            if (Rules.Kind == OceanMechanicKind.CurrentRiding && hand.open && input.InCurrent && hand.target == 0)
                hands?.ApplyEnvironmentalCurrent(new Vector3(Mathf.Sin(now * .35f) * .10f, 0, .12f), dt);
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
                    changed?.Invoke(state.MechanicComplete ? "探险机制完成！" : "完成一个任务节点 " + boundRun.mechanicProgress + "/" + state.Definition.MechanicTarget);
                }
                else Rules = new OceanMechanicRules(state.Definition.Mechanic, state.Definition.MechanicIntensity,
                    boundRun.mechanicProgress, boundRun.mechanicStage);
            }
            else if (outcome == OceanMechanicOutcome.Mistake)
            {
                state.RecordMechanicMistake();
                changed?.Invoke(MistakeHint());
            }
            else if (outcome == OceanMechanicOutcome.StageChanged) changed?.Invoke(StageHint());
            RefreshPresentation(outcome != OceanMechanicOutcome.None);
        }

        public static bool CrossedRing(Vector3 from, Vector3 to, Vector3 center, float radius)
        {
            float a = from.z - center.z, b = to.z - center.z;
            if (a >= 0 || b < 0 || (to - from).sqrMagnitude > 1f || Mathf.Abs(b - a) < .00001f) return false;
            Vector3 crossing = Vector3.Lerp(from, to, -a / (b - a));
            return new Vector2(crossing.x - center.x, crossing.y - center.y).sqrMagnitude <= radius * radius;
        }
        bool TryMissionPlane(Ray ray, out Vector3 point)
        {
            point = default;
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
            caption.text = state.Definition.Name + "  " + boundRun.mechanicProgress + "/" + state.Definition.MechanicTarget +
                "\n" + (state.MechanicComplete ? "机制完成 · 继续完成本关收藏与金币目标" : StageHint());
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
                labels[i].text = NodeLabel(i);
                labels[i].canvas.transform.rotation = view.transform.rotation;
            }
        }
        void UpdateCaptionPose()
        {
            if (captionRoot == null || view == null) return;
            captionRoot.SetPositionAndRotation(
                view.ViewportToWorldPoint(new Vector3(.5f, .15f, .85f)), view.transform.rotation);
        }
        void SetCaptionVisible(bool visible)
        {
            if (caption == null || captionCanvas == null) return;
            caption.enabled = visible;
            captionCanvas.enabled = visible;
        }
        string NodeLabel(int index)
        {
            switch (Rules.Kind)
            {
                case OceanMechanicKind.CurrentRiding: return "洋流导流叶 · 张手划动";
                case OceanMechanicKind.OxygenRelay: return index == 0 ? Rules.BubbleAvailable(boundRun.mechanicElapsedSeconds) ? "补给泡 · 捏合" : "补给尚未浮起" : "缺氧珊瑚 · 捏合送氧";
                case OceanMechanicKind.DeepLight: return Rules.Stage == 0 ? "微弱生物光 · 张手指向" : "已照亮 · 捏合记录";
                case OceanMechanicKind.Observation: return "观察对象 · 稳定手射线";
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
        string StageHint()
        {
            switch (Rules.Kind)
            {
                case OceanMechanicKind.CurrentRiding: return "张开手指向导流叶并缓慢划动，推动洋流；中央水流会轻推你";
                case OceanMechanicKind.OxygenRelay: return (Rules.Stage == 0 ? "捏合补给泡，再松手指向右侧珊瑚捏合送氧" : "已携带氧气 · 松手后捏合右侧珊瑚") + " · 氧气 " + Mathf.CeilToInt(Rules.Oxygen) + "秒";
                case OceanMechanicKind.DeepLight: return Rules.Stage == 0 ? "张手指向暗处，保持照明 " + Rules.Dwell.ToString("0.0") + "秒" : "发光生物已照亮 · 捏合完成记录";
                case OceanMechanicKind.Observation: return Rules.Stage == 0 ? "头与手同时指向目标，张手稳定观察，再捏合快门" : "对焦完成 · 保持头与手朝向并捏合快门";
                case OceanMechanicKind.Escort: return "张手用射线引导海龟游向右侧安全圈，保持平缓";
                case OceanMechanicKind.KelpMaze: return Rules.Stage == 0 ? "观察鱼鳞路标，先捏合选择出口，再坐姿划水穿过" : "路标已确认 · 坐姿划水穿过正确岔路圆心";
                case OceanMechanicKind.ShipwreckLock: return Rules.Stage == 0 ? "捏住钥匙别松手，偏转手射线22°，移到锁孔再松开" : Rules.Stage == 1 ? "保持捏合并偏转钥匙22°" : "钥匙已转动 · 指向右侧锁孔并松开";
                case OceanMechanicKind.JellyfishDodge: return "水母红色放电时避让，绿色安全窗捏合收取珍珠";
                case OceanMechanicKind.OctopusTracking: return "跟踪短暂显形的青色章鱼真身，松开后捏合，避开伪装";
                case OceanMechanicKind.FishHerding: return "张手指向右侧鱼圈并实际挥动，推动小鱼归巢";
                case OceanMechanicKind.CoralRepair: return Rules.Stage == 0 ? "捏住活珊瑚枝，拖到右侧底座，再松开种植" : "保持捏合拖到右侧底座，松开完成修复";
                case OceanMechanicKind.Cleanup: return "辨认塑料或金属，捏住垃圾拖到同类回收箱，再松开";
                case OceanMechanicKind.TideGates: return Rules.Stage == 0 ? "先捏合左侧潮汐开关" : "闸门绿色时，松手后捏合右侧闸门通过";
                case OceanMechanicKind.RingRace: return "坐姿划水，头部穿过下一个气泡环；无需站起";
                case OceanMechanicKind.RunePuzzle: return boundRun.mechanicElapsedSeconds - sectionStarted < 2.7f ? "记住三次发光顺序，随后依次捏合符文" : "按刚才顺序捏合三个符文 · 已输入 " + Rules.Stage + "/3";
                case OceanMechanicKind.ElectricFinale: return Rules.Stage == 0 ? "双手张开各指向一个电极，持续充能；随后对准核心" : "已充能 · 核心绿灯节拍时捏合发射";
                default: return state.Definition.MechanicHint;
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
            text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter; text.color = new Color(.87f, 1f, 1f); text.raycastTarget = false;
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
            station = carriedVisual = escortVisual = null; caption = null; captionCanvas = null; captionRoot = null; nodes.Clear(); labels.Clear();
            foreach (var mesh in ownedMeshes) if (mesh != null) Release(mesh); ownedMeshes.Clear();
            foreach (var material in ownedMaterials) if (material != null) Release(material); ownedMaterials.Clear();
            handLight = null;
            Rules = null; boundRun = null; carryingHand = -1; wasPlaying = false; headBaseline = false;
            for (int i = 0; i < 2; i++) { pinchGates[i].Reset(); wasPinching[i] = false; }
        }
        void OnDisable() { if (Rules != null) Suspend(); }
        void OnDestroy() => Clear();
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
        public RectTransform NodeLabelRectForValidation(int index) => labels[index].canvas.transform as RectTransform;
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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using HandType = SwanUnderwater.OceanHandSide;
using HandJoint = SwanUnderwater.OceanHandJointId;
using HandJointLocation = SwanUnderwater.OceanHandJoint;
using HandJointLocations = SwanUnderwater.OceanHandFrame;
using HandLocationStatus = SwanUnderwater.OceanHandLocationStatus;
using HandAimState = SwanUnderwater.OceanHandAim;
using HandAimStatus = SwanUnderwater.OceanHandAimStatus;
using StrokeVector = System.Numerics.Vector3;

namespace SwanUnderwater
{
    /// <summary>
    /// Hand tracking + independent single/dual-hand paddle locomotion for XR headsets.
    /// Uses validated tracking-local joint samples; controller joystick remains available independently.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class HandSwimController : MonoBehaviour
    {
        [Header("Locomotion")]
        [SerializeField] private float swimSpeed = 1.6f;
        [SerializeField] private float joystickSpeed = 1.6f;
        [SerializeField] private float turnSpeed = 45f;
        [SerializeField] private float verticalSpeed = 0.9f;
        [SerializeField] private bool enableDebugLog = false;
        [SerializeField, Min(0.1f)] private float maxMoveSpeed = 2.5f;
        [SerializeField, Min(0.1f)] private float acceleration = 3.5f;
        [SerializeField, Min(0.1f)] private float deceleration = 2.5f;
        [SerializeField, Min(0.1f)] private float glideDamping = 0.7f;
        [SerializeField, Range(0f, 0.5f)] private float joystickDeadZone = 0.15f;

        [Header("Comfort")]
        [SerializeField] private bool snapTurnEnabled = true;
        [SerializeField, Range(15f, 60f)] private float snapTurnDegrees = 30f;
        [SerializeField] private bool seatedMode = false;

        [Header("Single-Hand Paddle Steering")]
        [SerializeField] private bool singleHandTurnEnabled = true;
        [SerializeField, Range(5f, 40f)] private float singleHandTurnSpeed = 25f;
        [SerializeField, Range(0.18f, 0.45f)] private float singleHandPairingDelay = 0.28f;

        [Header("Environment Collision")]
        [SerializeField, Min(0.1f)] private float bodyRadius = 0.22f;
        [SerializeField, Min(0.5f)] private float bodyHeight = 1.25f;
        [SerializeField, Min(0.01f)] private float collisionSkin = 0.025f;
        [SerializeField, Min(0.1f)] private float overlapCorrectionSpeed = 1.0f;
        private float remainingOverlapCorrection;
        private readonly RaycastHit[] movementHits = new RaycastHit[64];
        private readonly Collider[] overlappingObstacles = new Collider[64];
        private CapsuleCollider meshOverlapProbe;

        [Header("Stroke Detection")]
        [SerializeField, Min(0.02f)] private float minimumPullDistance = 0.04f;
        [SerializeField, Min(0.06f)] private float minimumPullSpeed = 0.10f;
        [SerializeField, Min(0.1f)] private float stableTrackingSeconds = 0.2f;
        [SerializeField] private bool suppressPinchingHand = true;

        [Header("Hand Visuals")]
        [SerializeField] private bool showHands = true;
        [SerializeField] private Material handMaterial;

        public enum HandMode { None, HandTracking, Controller }
        public HandMode CurrentMode { get; private set; } = HandMode.None;

        // Public hand positions for fish escape
        public Vector3 LeftPalmPos { get; private set; }
        public Vector3 RightPalmPos { get; private set; }
        public bool LeftHandActive { get; private set; }
        public bool RightHandActive { get; private set; }
        /// <summary>Current reliable locomotion position: visible wrist on AVP, tracked palm elsewhere.</summary>
        public bool LeftPaddleTracked => PaddleSampleFresh && leftPaddleTracked;
        public bool RightPaddleTracked => PaddleSampleFresh && rightPaddleTracked;
        private bool PaddleSampleFresh => CanReadTrackedVisuals && handSampleFrame == Time.frameCount &&
            relocationFrame != Time.frameCount;
        /// <summary>Fresh, debounced fists. Initial/recovered closed hands must release before grabbing.</summary>
        public bool LeftGraspActive => GraspSampleFresh && LeftHandActive && leftGrasp.Active;
        public bool RightGraspActive => GraspSampleFresh && RightHandActive && rightGrasp.Active;
        /// <summary>One frame per deliberate open-to-closed gesture; holding near a new target is not a new grab.</summary>
        public bool LeftGraspStartedThisFrame => GraspSampleFresh && LeftHandActive && leftGrasp.StartedThisStep;
        public bool RightGraspStartedThisFrame => GraspSampleFresh && RightHandActive && rightGrasp.StartedThisStep;
        // Latest strict joint diagnostics; native pinches never populate these fist fields.
        public bool LeftGraspValidThisStep => GraspSampleFresh && LeftHandActive && leftGrasp.ValidThisStep;
        public bool RightGraspValidThisStep => GraspSampleFresh && RightHandActive && rightGrasp.ValidThisStep;
        public bool LeftGraspArmed => GraspSampleFresh && LeftHandActive && leftGrasp.Armed;
        public bool RightGraspArmed => GraspSampleFresh && RightHandActive && rightGrasp.Armed;
        public bool LeftPaddleFistSuppressed => PaddleSampleFresh && LeftPaddleFistBlocked;
        public bool RightPaddleFistSuppressed => PaddleSampleFresh && RightPaddleFistBlocked;
        public int LeftGraspFingerMask => GraspSampleFresh && LeftHandActive ? leftFingerMask : 0;
        public int RightGraspFingerMask => GraspSampleFresh && RightHandActive ? rightFingerMask : 0;
        public int NativePinchBegins => nativeVisionOSPinch.AcceptedBeginCount;
        public int NativePinchMenuConsumed => nativeVisionOSPinch.MenuConsumedCount;
        public int NativePinchWorldGrabsAccepted { get; private set; }
        public bool NativePinchInputCurrent => NativePinchReadable && nativeVisionOSPinch.HasCurrentInput(Time.frameCount);
        private bool GraspSampleFresh => isActiveAndEnabled && !appPaused && appFocused && !interactionSuppressed &&
            handSampleFrame == Time.frameCount && relocationFrame != Time.frameCount;
        /// <summary>Recognized individual hand pulls; a simultaneous two-hand pull counts as two.</summary>
        public int CompletedStrokeCount => strokeModel.CompletedStrokeCount;
        public int LeftStrokeCount => strokeModel.LeftStrokeCount;
        public int RightStrokeCount => strokeModel.RightStrokeCount;
        /// <summary>Current locomotion speed in m/s, excluding physical head motion and collision correction.</summary>
        public float CurrentSpeed => currentVelocity.magnitude;
        /// <summary>Filtered paddle velocities in world axes (AVP wrist, other palm), excluding artificial rig motion.</summary>
        public Vector3 LeftPalmVelocity { get; private set; }
        public Vector3 RightPalmVelocity { get; private set; }
        public float LeftStrokeStrength { get; private set; }
        public float RightStrokeStrength { get; private set; }
        public float StrokeStrength => Mathf.Clamp01((LeftStrokeStrength + RightStrokeStrength) / 1.5f);
        public Vector3 StrokeDirection { get; private set; }
        public Transform TrackingSpace => trackingSpace;
        public bool CanReadTrackedVisuals => isActiveAndEnabled && !appPaused && appFocused;
        public bool MenuLocomotionPaused => menuLocomotionPaused;
        public bool InteractionSuppressed => interactionSuppressed;
        public OceanComfortProfile Comfort { get; private set; } = OceanComfortProfile.Default;
        public WaterStrokeModel.PaddleDecision LeftPaddleDecision => strokeModel.LeftDecision;
        public WaterStrokeModel.PaddleDecision RightPaddleDecision => strokeModel.RightDecision;
        /// <summary>Applied paddle yaw speed in degrees/s; positive means right, from a left-hand pull.</summary>
        public float CurrentHandTurnSpeed { get; private set; }
        public float HandTurnDegreesThisFrame { get; private set; }
        public float TotalHandTurnDegrees { get; private set; }
        public float TotalAbsoluteHandTurnDegrees { get; private set; }
        public SingleHandTurnModel.TurnState HandTurnState => handTurnModel.State;
        // Valid paddle tracking time / sampled time: AVP wrist, other backends palm.
        // Suppression and gesture recognition do not alter this tracking denominator.
        public double TrackingSampleSeconds => strokeModel.TrackingSampleSeconds;
        public double LeftHandTrackedSeconds => strokeModel.LeftTrackedSeconds;
        public double RightHandTrackedSeconds => strokeModel.RightTrackedSeconds;

        private Transform xrOrigin;
        private Transform head;
        private Transform trackingSpace;
        private HandJointLocations leftLocations;
        private HandJointLocations rightLocations;
        private struct MenuAimSample
        {
            public int frame;
            public bool valid, rayValid;
            public Ray localRay;
            public float pinchStrength;
            public MenuHandRayDiagnostics diagnostics;
        }
        public readonly struct MenuHandRayDiagnostics
        {
            public readonly int Frame;
            public readonly bool NativeFetchSucceeded, NativeAimValid, TipsValid;
            public readonly HandAimStatus NativeAimStatus;
            public readonly OceanHandAimSource Source;
            public readonly float NativeTouchStrength, TipDistanceMeters, PinchStrength;
            public bool NativeIndexPinching => (NativeAimStatus & HandAimStatus.AimIndexPinching) != 0;
            public MenuHandRayDiagnostics(int frame, bool fetched, bool aimValid, HandAimStatus flags,
                float legacyTouch, bool tipsValid, float tipDistance, float strength, OceanHandAimSource source = OceanHandAimSource.None)
            {
                Source = source; Frame = frame; NativeFetchSucceeded = fetched; NativeAimValid = aimValid; NativeAimStatus = flags;
                NativeTouchStrength = legacyTouch; TipsValid = tipsValid; TipDistanceMeters = tipDistance; PinchStrength = strength;
            }
        }
        private MenuAimSample leftMenuAim = new MenuAimSample { frame = -1 };
        private MenuAimSample rightMenuAim = new MenuAimSample { frame = -1 };
        private Vector3 leftPalmLocal;
        private Vector3 rightPalmLocal;
        private Vector3 leftPaddleLocal, rightPaddleLocal;
        private bool leftPaddleTracked, rightPaddleTracked, paddleUsesWrist;
        private int handSampleFrame = -1;
        private int relocationFrame = -1;
        private readonly ControllerSnapTurnGate snapTurnGate = new ControllerSnapTurnGate();
        private bool appPaused;
        private bool appFocused = true;
        private Material ownedHandMaterial;
        private InputDevice leftController;
        private InputDevice rightController;

        // Hand tracking objects
        private GameObject leftHandGO;
        private GameObject rightHandGO;

        private readonly WaterStrokeModel strokeModel = new WaterStrokeModel();
        private readonly SingleHandTurnModel handTurnModel = new SingleHandTurnModel();
        private readonly ShellGrabGesture leftGrasp = new ShellGrabGesture();
        private readonly ShellGrabGesture rightGrasp = new ShellGrabGesture();
        // Locomotion-only guards never arm pickup or tool input when the interaction palm is unavailable.
        private readonly ShellGrabGesture leftPaddleFistGuard = new ShellGrabGesture();
        private readonly ShellGrabGesture rightPaddleFistGuard = new ShellGrabGesture();
        private float leftPaddleOpenEvidence, rightPaddleOpenEvidence;
        private bool LeftPaddleFistBlocked => (paddleUsesWrist ? leftPaddleFistGuard : leftGrasp).SuppressStroke;
        private bool RightPaddleFistBlocked => (paddleUsesWrist ? rightPaddleFistGuard : rightGrasp).SuppressStroke;
        private readonly WorldGrabIntent leftGrabIntent = new WorldGrabIntent();
        private readonly WorldGrabIntent rightGrabIntent = new WorldGrabIntent();
        private readonly OceanVisionOSPinchInput nativeVisionOSPinch = new OceanVisionOSPinchInput();
        private readonly WorldGrabIntent leftNativePinchIntent = new WorldGrabIntent();
        private readonly WorldGrabIntent rightNativePinchIntent = new WorldGrabIntent();
        private readonly bool[] nativeWorldIntentSelected = new bool[2];
        private readonly int[] nativeWorldIntentReadFrame = { -1, -1 };
        private bool NativePinchReadable => paddleUsesWrist && GraspSampleFresh && trackingSpace != null;
        private int leftFingerMask, rightFingerMask, diagnosticLeftFingerValid, diagnosticRightFingerValid;
        private int diagnosticLeftGrabEdges, diagnosticRightGrabEdges, diagnosticGrabAttempts, diagnosticGrabConsumed;
        private Vector3 leftFingerCurls, rightFingerCurls;
        private float pendingHandTurn;
        private bool interactionSuppressed;
        private bool menuLocomotionPaused;
        private bool spearAimLeft, spearAimRight;
        private int spearAimFrame = -1;
        private bool menuLeftTargeted, menuRightTargeted;
        private int menuLeftTargetFrame = -1, menuRightTargetFrame = -1;
        private int diagnosticFrames, diagnosticLeftTracked, diagnosticRightTracked;
        private int diagnosticLeftPaddleTracked, diagnosticRightPaddleTracked, diagnosticLeftPaddleFist, diagnosticRightPaddleFist;
        private int diagnosticLeftGrasp, diagnosticRightGrasp, diagnosticLeftPinch, diagnosticRightPinch;
        private int diagnosticLeftAim, diagnosticRightAim, diagnosticMenu, diagnosticCollisionFrames;
        private int diagnosticCollisionFrame = -1;
        private float diagnosticSeconds, diagnosticRequestedMetres, diagnosticMovedMetres;
        private string diagnosticLastCollision = "none";
        private readonly int[] diagnosticLeftDecisions = new int[(int)WaterStrokeModel.PaddleDecision.UpperIntent + 1],
            diagnosticRightDecisions = new int[(int)WaterStrokeModel.PaddleDecision.UpperIntent + 1];
        private int diagnosticPartialLoss, diagnosticNoUsableHands;
        private Vector3 diagnosticLeftMin = Vector3.one * float.PositiveInfinity, diagnosticLeftMax = Vector3.one * float.NegativeInfinity;
        private Vector3 diagnosticRightMin = Vector3.one * float.PositiveInfinity, diagnosticRightMax = Vector3.one * float.NegativeInfinity;
        private bool customLeftVisual, customRightVisual;
        private Vector3 swimVelocity;
        private Vector3 currentVelocity;
        private static readonly int[] VisualJointIndices =
        {
            (int)HandJoint.JointWrist, (int)HandJoint.JointPalm,
            (int)HandJoint.JointThumbTip, (int)HandJoint.JointIndexTip,
            (int)HandJoint.JointMiddleTip, (int)HandJoint.JointRingTip,
            (int)HandJoint.JointLittleTip
        };

        // Joint visualization
        private readonly List<Transform> leftJoints = new List<Transform>();
        private readonly List<Transform> rightJoints = new List<Transform>();

        private void Start()
        {
            ApplyComfort(new OceanComfortProfile(PlayerPrefs.GetInt("ocean_comfort_speed_v1", 2),
                PlayerPrefs.GetInt("ocean_comfort_volume_v1", 4), PlayerPrefs.GetInt("ocean_comfort_turn_v1", 2),
                PlayerPrefs.GetInt("ocean_comfort_seated_v1", 0) != 0, PlayerPrefs.GetInt("ocean_comfort_text_scale_v1", 0)), false);
            xrOrigin = transform;
            Camera cam = Camera.main;
            head = cam != null ? cam.transform : transform;
            // Match the installed SDK hand building block, including Device-origin floor offset.
            trackingSpace = xrOrigin.Find("Camera Offset");
            if (trackingSpace == null)
                trackingSpace = head.parent != null && head.IsChildOf(xrOrigin) ? head.parent : xrOrigin;

            leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            // Try to initialize hand tracking visuals
            InitializeHandTracking();
            SetHandsVisible(false, false);
        }

        public void ApplyComfort(OceanComfortProfile profile, bool persist = true)
        {
            Comfort = profile; seatedMode = profile.Seated;
            AudioListener.volume = profile.MasterVolume;
            snapTurnGate.Reset(); ResetStroke(); swimVelocity = currentVelocity = Vector3.zero;
            if (!persist) return;
            PlayerPrefs.SetInt("ocean_comfort_speed_v1", profile.Speed);
            PlayerPrefs.SetInt("ocean_comfort_volume_v1", profile.Volume);
            PlayerPrefs.SetInt("ocean_comfort_turn_v1", profile.Turn);
            PlayerPrefs.SetInt("ocean_comfort_seated_v1", profile.Seated ? 1 : 0);
            PlayerPrefs.SetInt("ocean_comfort_text_scale_v1", profile.TextScale);
            PlayerPrefs.Save();
        }

        /// <summary>A bounded external water force moves only the rig through the existing collision path.</summary>
        public void ApplyEnvironmentalCurrent(Vector3 velocity, float dt)
        {
            if (!CanReadTrackedVisuals || interactionSuppressed || menuLocomotionPaused || xrOrigin == null || head == null ||
                float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f || dt > .1f ||
                !IsFiniteVector(velocity)) return;
            Vector3 movement = Vector3.ClampMagnitude(velocity, .18f) * Mathf.Min(dt, .05f);
            if (UnderwaterWorld.RenderedSeabedEnvironment == OceanEnvironmentKind.CoralSanctuary)
            {
                float clearance = Mathf.Max(.1f, bodyRadius) + Mathf.Max(.005f, collisionSkin);
                movement.x = OceanEnvironmentProfile.LimitSanctuaryOutwardStep(head.position.x - OceanEnvironmentProfile.SpawnX, movement.x, clearance);
                movement.z = OceanEnvironmentProfile.LimitSanctuaryOutwardStep(head.position.z - OceanEnvironmentProfile.SpawnZ, movement.z, clearance);
            }
            MoveWithCollision(movement);
            if (UnderwaterWorld.RenderedSeabedEnvironment == OceanEnvironmentKind.CoralSanctuary) ConstrainSanctuaryBoundary();
            ConstrainEyeAboveSeabed();
        }

        private void InitializeHandTracking()
        {
            if (!showHands) return;

            // Try loading PICO hand prefabs
            GameObject leftPrefab = OceanHandTracking.IsPicoBackend ? Resources.Load<GameObject>("Prefabs/HandLeft") : null;
            GameObject rightPrefab = OceanHandTracking.IsPicoBackend ? Resources.Load<GameObject>("Prefabs/HandRight") : null;

            if (leftPrefab != null && rightPrefab != null)
            {
                leftHandGO = Instantiate(leftPrefab, trackingSpace, false);
                rightHandGO = Instantiate(rightPrefab, trackingSpace, false);
                leftHandGO.transform.localPosition = rightHandGO.transform.localPosition = Vector3.zero;
                leftHandGO.transform.localRotation = rightHandGO.transform.localRotation = Quaternion.identity;
                if (enableDebugLog) Debug.Log("[HandSwim] PICO hand prefabs loaded");
            }
            else
            {
                // Fallback: create joint-based hands
                CreateJointHands();
                if (enableDebugLog) Debug.Log("[HandSwim] Using joint-based hand visualization");
            }
        }

        private void CreateJointHands()
        {
            leftHandGO = new GameObject("LeftHand");
            rightHandGO = new GameObject("RightHand");
            leftHandGO.transform.SetParent(trackingSpace, false);
            rightHandGO.transform.SetParent(trackingSpace, false);

            Material mat = handMaterial;
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    ownedHandMaterial = new Material(shader);
                    ownedHandMaterial.color = new Color(0.8f, 0.75f, 0.7f);
                    mat = ownedHandMaterial;
                }
            }

            // Create joint spheres for both hands (wrist + palm + 5 fingertips = 7 per hand)
            for (int h = 0; h < 2; h++)
            {
                var list = h == 0 ? leftJoints : rightJoints;
                var parent = h == 0 ? leftHandGO : rightHandGO;
                for (int i = 0; i < 7; i++)
                {
                    GameObject joint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    joint.transform.SetParent(parent.transform, false);
                    joint.transform.localScale = Vector3.one * 0.025f;
                    Destroy(joint.GetComponent<Collider>());
                    Renderer r = joint.GetComponent<Renderer>();
                    if (r != null && mat != null) r.sharedMaterial = mat;
                    list.Add(joint.transform);
                }
            }
        }

        private void Update()
        {
            if (appPaused || !appFocused) return;
            UpdateDevices();
            UpdateHandTracking();
            PollNativeVisionOSPinch();
            // A relocation may be requested before this Update. Refresh real tracking,
            // but do not spend a grasp, paddle or controller input in the relocation frame.
            if (relocationFrame == Time.frameCount) return;
            UpdateGraspGestures();
            UpdateNativePinchIntents(Time.deltaTime);
            UpdateStrokeDetection();
            UpdateLocomotion();
            RefreshPalmWorldPositions();
            TraceSwimDiagnostics();
        }

        private void UpdateDevices()
        {
            if (!leftController.isValid) leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!rightController.isValid) rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        }

        private void PollNativeVisionOSPinch()
        {
            if (!paddleUsesWrist) { ResetNativePinchInput(); return; }
            // One native snapshot after the current Dynamic hand sample. Menu pause still allows Resume.
            nativeVisionOSPinch.Poll(Time.frameCount, NativePinchReadable,
                LeftPaddleTracked, leftPaddleLocal, RightPaddleTracked, rightPaddleLocal);
        }

        private void UpdateNativePinchIntents(float dt) => StepNativePinchIntents(Time.frameCount, dt);

        private void StepNativePinchIntents(int frame, float dt)
        {
            StepNativePinchIntent(HandType.HandLeft, leftNativePinchIntent, frame, dt);
            StepNativePinchIntent(HandType.HandRight, rightNativePinchIntent, frame, dt);
        }

        private void StepNativePinchIntent(HandType side, WorldGrabIntent intent, int frame, float dt)
        {
            bool tracked = side == HandType.HandLeft ? LeftPaddleTracked : RightPaddleTracked;
            bool available = nativeVisionOSPinch.TryGetWorldPinch(side, frame,
                out bool started, out bool active, out Vector3 position);
            intent.Step(dt, NativePinchReadable && tracked && available, active, started, ToStrokeVector(position),
                menuLocomotionPaused || IsMenuHandTargeted(side) || IsSpearAimSuppressed(side));
        }

        private void ResetNativePinchWindows()
        {
            leftNativePinchIntent.Reset(); rightNativePinchIntent.Reset();
            nativeWorldIntentSelected[0] = nativeWorldIntentSelected[1] = false;
            nativeWorldIntentReadFrame[0] = nativeWorldIntentReadFrame[1] = -1;
        }

        private void ResetNativePinchInput()
        {
            nativeVisionOSPinch.Reset();
            ResetNativePinchWindows();
        }

        private void UpdateHandTracking()
        {
            // PXR_Hand.Computed is aim state and can remain stale after a failed fetch.
            OceanHandTracking.TryGetFrame(HandType.HandLeft, out var left);
            OceanHandTracking.TryGetFrame(HandType.HandRight, out var right);
            ApplyHandTrackingFrames(left, right, OceanXRPlatform.IsVisionOS, Time.frameCount, OceanHandTracking.LastInvalidationFrame);
        }

        private void ApplyHandTrackingFrames(HandJointLocations left, HandJointLocations right,
            bool visionOS, int currentFrame, int lastInvalidationFrame)
        {
            leftLocations = left; rightLocations = right; paddleUsesWrist = visionOS;
            leftPalmLocal = rightPalmLocal = Vector3.zero;
            // Palm eligibility remains strict for visuals, menu, grasp and trigger input.
            LeftHandActive = OceanHandTracking.IsCurrent(left, currentFrame, lastInvalidationFrame) &&
                TryReadJoint(left, (int)HandJoint.JointPalm, out leftPalmLocal);
            RightHandActive = OceanHandTracking.IsCurrent(right, currentFrame, lastInvalidationFrame) &&
                TryReadJoint(right, (int)HandJoint.JointPalm, out rightPalmLocal);
            leftPaddleTracked = OceanHandTracking.TryReadPaddlePosition(left, visionOS, currentFrame, lastInvalidationFrame, out leftPaddleLocal);
            rightPaddleTracked = OceanHandTracking.TryReadPaddlePosition(right, visionOS, currentFrame, lastInvalidationFrame, out rightPaddleLocal);
            handSampleFrame = currentFrame;
            RefreshPalmWorldPositions();
            SetHandsVisible(LeftHandActive, RightHandActive);
            if (LeftHandActive) UpdateJointVisuals(leftJoints, leftLocations);
            if (RightHandActive) UpdateJointVisuals(rightJoints, rightLocations);
            CurrentMode = LeftHandActive || RightHandActive || LeftPaddleTracked || RightPaddleTracked ? HandMode.HandTracking
                : leftController.isValid || rightController.isValid ? HandMode.Controller : HandMode.None;
        }

        private static bool TryReadJoint(HandJointLocations locations, int index, out Vector3 position)
        {
            position = Vector3.zero;
            if (locations.isActive == 0 || locations.jointLocations == null ||
                index < 0 || index >= locations.jointCount || index >= locations.jointLocations.Length) return false;
            var joint = locations.jointLocations[index];
            const HandLocationStatus required = HandLocationStatus.PositionValid | HandLocationStatus.PositionTracked;
            if ((joint.locationStatus & required) != required) return false;
            position = joint.pose.position; // Adapters convert native coordinates exactly once.
            return IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void RefreshPalmWorldPositions()
        {
            LeftPalmPos = LeftHandActive ? trackingSpace.TransformPoint(leftPalmLocal) : Vector3.zero;
            RightPalmPos = RightHandActive ? trackingSpace.TransformPoint(rightPalmLocal) : Vector3.zero;
        }

        private void SetHandsVisible(bool leftVisible, bool rightVisible)
        {
            if (leftHandGO != null) leftHandGO.SetActive(showHands && !customLeftVisual && leftVisible);
            if (rightHandGO != null) rightHandGO.SetActive(showHands && !customRightVisual && rightVisible);
        }

        /// <summary>Use custom hand art while keeping SDK palm/joint tracking and fallback recovery intact.</summary>
        public void SetCustomHandVisualsActive(bool active)
        {
            SetCustomHandVisualsActive(active, active);
        }

        public void SetCustomHandVisualsActive(bool left, bool right)
        {
            customLeftVisual = left;
            customRightVisual = right;
            SetHandsVisible(LeftHandActive, RightHandActive);
        }

        private static void UpdateJointVisuals(List<Transform> joints, HandJointLocations locations)
        {
            for (int i = 0; i < joints.Count; i++)
            {
                bool valid = TryReadJoint(locations, VisualJointIndices[i], out Vector3 position);
                joints[i].gameObject.SetActive(valid);
                if (valid) joints[i].localPosition = position;
            }
        }

        private Vector3 HorizontalForward()
        {
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            return forward.sqrMagnitude > 0.001f ? forward.normalized : xrOrigin.forward;
        }

        private static StrokeVector ToStrokeVector(Vector3 value) => new StrokeVector(value.x, value.y, value.z);
        private static Vector3 ToUnityVector(StrokeVector value) => new Vector3(value.X, value.Y, value.Z);

        private static bool TryReadGraspFinger(HandJointLocations locations, HandJoint proximal,
            HandJoint intermediate, HandJoint distal, HandJoint tip, out ShellGrabGesture.Finger finger)
        {
            finger = default;
            if (!TryReadJoint(locations, (int)proximal, out Vector3 a) ||
                !TryReadJoint(locations, (int)intermediate, out Vector3 b) ||
                !TryReadJoint(locations, (int)distal, out Vector3 c) ||
                !TryReadJoint(locations, (int)tip, out Vector3 d)) return false;
            finger = new ShellGrabGesture.Finger(ToStrokeVector(a), ToStrokeVector(b), ToStrokeVector(c), ToStrokeVector(d));
            return true;
        }

        private static ShellGrabGesture.HandSample ReadGraspSample(bool palmTracked, HandJointLocations locations)
            => ReadGraspSampleWithDiagnostics(palmTracked, locations, out _, out _);

        private static ShellGrabGesture.HandSample ReadGraspSampleWithDiagnostics(bool palmTracked,
            HandJointLocations locations, out int fingerMask, out Vector3 curls)
        {
            // Reuse this frame's validated SDK sample; no second query or per-frame allocations.
            // Thumb/index are deliberately excluded so an ordinary pinch cannot become a fist.
            fingerMask = 0; curls = new Vector3(float.NaN, float.NaN, float.NaN);
            if (!palmTracked) return default;
            bool validMiddle = TryReadGraspFinger(locations, HandJoint.JointMiddleProximal, HandJoint.JointMiddleIntermediate,
                HandJoint.JointMiddleDistal, HandJoint.JointMiddleTip, out var middle) && ShellGrabGesture.TryFingerCurl(middle, out curls.x);
            bool validRing = TryReadGraspFinger(locations, HandJoint.JointRingProximal, HandJoint.JointRingIntermediate,
                HandJoint.JointRingDistal, HandJoint.JointRingTip, out var ring) && ShellGrabGesture.TryFingerCurl(ring, out curls.y);
            bool validLittle = TryReadGraspFinger(locations, HandJoint.JointLittleProximal, HandJoint.JointLittleIntermediate,
                HandJoint.JointLittleDistal, HandJoint.JointLittleTip, out var little) && ShellGrabGesture.TryFingerCurl(little, out curls.z);
            if (validMiddle) fingerMask |= 1; else curls.x = float.NaN;
            if (validRing) fingerMask |= 2; else curls.y = float.NaN;
            if (validLittle) fingerMask |= 4; else curls.z = float.NaN;
            if (fingerMask != 7) return default;
            return new ShellGrabGesture.HandSample(true, middle, ring, little);
        }

        private void UpdateGraspGestures() => UpdateGraspGestures(Time.deltaTime);

        private void UpdateGraspGestures(float dt)
        {
            bool leftWasBlocked = LeftPaddleFistBlocked, rightWasBlocked = RightPaddleFistBlocked;
            leftGrasp.Step(dt, ReadGraspSampleWithDiagnostics(LeftHandActive, leftLocations, out leftFingerMask, out leftFingerCurls), interactionSuppressed, LeftHandActive);
            rightGrasp.Step(dt, ReadGraspSampleWithDiagnostics(RightHandActive, rightLocations, out rightFingerMask, out rightFingerCurls), interactionSuppressed, RightHandActive);
            if (paddleUsesWrist)
            {
                UpdateVisionOSPaddleFistGuard(leftPaddleFistGuard, LeftPaddleTracked, leftLocations, dt, ref leftPaddleOpenEvidence);
                UpdateVisionOSPaddleFistGuard(rightPaddleFistGuard, RightPaddleTracked, rightLocations, dt, ref rightPaddleOpenEvidence);
            }
            leftGrabIntent.Step(dt, LeftHandActive, leftGrasp,
                ToStrokeVector(leftPalmLocal), interactionSuppressed || menuLocomotionPaused);
            rightGrabIntent.Step(dt, RightHandActive, rightGrasp,
                ToStrokeVector(rightPalmLocal), interactionSuppressed || menuLocomotionPaused);
            if (leftGrasp.ValidThisStep) diagnosticLeftFingerValid++;
            if (rightGrasp.ValidThisStep) diagnosticRightFingerValid++;
            if (leftGrasp.StartedThisStep) diagnosticLeftGrabEdges++;
            if (rightGrasp.StartedThisStep) diagnosticRightGrabEdges++;
            // A newly clenched steering hand must not spend an old deferred yaw bank.
            // The other hand's paddle recognition and ordinary translational glide remain intact.
            var turnState = handTurnModel.State;
            if ((!leftWasBlocked && LeftPaddleFistBlocked &&
                    (turnState == SingleHandTurnModel.TurnState.PendingLeft || turnState == SingleHandTurnModel.TurnState.LeftPull)) ||
                (!rightWasBlocked && RightPaddleFistBlocked &&
                    (turnState == SingleHandTurnModel.TurnState.PendingRight || turnState == SingleHandTurnModel.TurnState.RightPull)))
            {
                handTurnModel.Reset();
                pendingHandTurn = 0f;
            }
        }

        private void UpdateVisionOSPaddleFistGuard(ShellGrabGesture guard, bool wristTracked,
            HandJointLocations locations, float dt, ref float openEvidence)
        {
            guard.Step(dt, ReadGraspSample(wristTracked, locations), interactionSuppressed, wristTracked);
            // One reliably open required finger disproves the three-finger fist. It cannot arm pickup or a tool.
            // Keep the original release debounce; unknown data is never evidence of release.
            bool open = TryReadTrackedOpenFinger(locations, HandJoint.JointMiddleProximal, HandJoint.JointMiddleIntermediate,
                    HandJoint.JointMiddleDistal, HandJoint.JointMiddleTip) ||
                TryReadTrackedOpenFinger(locations, HandJoint.JointRingProximal, HandJoint.JointRingIntermediate,
                    HandJoint.JointRingDistal, HandJoint.JointRingTip) ||
                TryReadTrackedOpenFinger(locations, HandJoint.JointLittleProximal, HandJoint.JointLittleIntermediate,
                    HandJoint.JointLittleDistal, HandJoint.JointLittleTip);
            if (!guard.SuppressStroke || !wristTracked || interactionSuppressed || menuLocomotionPaused ||
                !IsFinite(dt) || dt <= 0f || dt > .1f || !open)
            { openEvidence = 0f; return; }
            openEvidence = Mathf.Min(ShellGrabGesture.OpenStableSeconds, openEvidence + dt);
            if (openEvidence >= ShellGrabGesture.OpenStableSeconds) { guard.Reset(); openEvidence = 0f; }
        }

        private static bool TryReadTrackedOpenFinger(HandJointLocations locations, HandJoint proximal,
            HandJoint intermediate, HandJoint distal, HandJoint tip) =>
            TryReadGraspFinger(locations, proximal, intermediate, distal, tip, out var finger) &&
            ShellGrabGesture.TryFingerCurl(finger, out float curl) && curl <= ShellGrabGesture.ReleaseCurl;

        private bool IsPinching(HandJointLocations locations)
        {
            return suppressPinchingHand &&
                TryReadJoint(locations, (int)HandJoint.JointThumbTip, out Vector3 thumb) &&
                TryReadJoint(locations, (int)HandJoint.JointIndexTip, out Vector3 index) &&
                (thumb - index).sqrMagnitude < 0.028f * 0.028f;
        }

        /// <summary>Fresh fist or AVP native near-pinch intent for at most 0.5s/0.5m. Caller still checks reach and obstacles.</summary>
        public bool TryGetWorldGrabIntent(HandType hand, out Vector3 palm)
        {
            palm = Vector3.zero;
            if (!GraspSampleFresh || menuLocomotionPaused || IsSpearAimSuppressed(hand)) return false;
            bool left = hand == HandType.HandLeft;
            if (!left && hand != HandType.HandRight) return false;
            int index = (int)hand;
            nativeWorldIntentReadFrame[index] = Time.frameCount;
            nativeWorldIntentSelected[index] = false;
            var grasp = left ? leftGrasp : rightGrasp;
            bool fist = grasp.ValidThisStep && grasp.Active && (left ? leftGrabIntent : rightGrabIntent).Available &&
                TryGetPalmPosition(hand, out palm);
            if (!fist)
            {
                if (!TryGetNativeWorldGrabPoint(hand, out palm)) return false;
                nativeWorldIntentSelected[index] = true;
            }
            diagnosticGrabAttempts++; return true;
        }

        private bool TryGetNativeWorldGrabPoint(HandType side, out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            bool left = side == HandType.HandLeft;
            if ((!left && side != HandType.HandRight) || !NativePinchReadable || menuLocomotionPaused ||
                !(left ? LeftPaddleTracked : RightPaddleTracked) || IsMenuHandTargeted(side) || IsSpearAimSuppressed(side)) return false;
            var intent = left ? leftNativePinchIntent : rightNativePinchIntent;
            // Re-read after menu LateUpdate: its same-gesture claim must beat Treasure's later query.
            if (!nativeVisionOSPinch.TryGetWorldPinch(side, Time.frameCount, out _, out bool active, out Vector3 position) ||
                !active || !intent.Available) { intent.Reset(); return false; }
            worldPosition = trackingSpace.TransformPoint(position);
            return IsFinite(worldPosition.x) && IsFinite(worldPosition.y) && IsFinite(worldPosition.z);
        }

        /// <summary>One acted-on target (including an intentional target hint) spends this fist's intent.</summary>
        public bool ConsumeWorldGrabIntent(HandType hand)
        {
            if (hand != HandType.HandLeft && hand != HandType.HandRight) return false;
            int index = (int)hand;
            bool consumed = (hand == HandType.HandLeft ? leftGrabIntent : rightGrabIntent).Consume();
            bool nativeSelected = nativeWorldIntentReadFrame[index] == Time.frameCount && nativeWorldIntentSelected[index];
            if ((nativeSelected || consumed) && TryGetNativeWorldGrabPoint(hand, out _))
            {
                bool nativeConsumed = (hand == HandType.HandLeft ? leftNativePinchIntent : rightNativePinchIntent).Consume();
                if (nativeConsumed && nativeVisionOSPinch.ConsumeWorld(hand, Time.frameCount))
                {
                    if (nativeSelected) NativePinchWorldGrabsAccepted++;
                    consumed = true;
                }
            }
            nativeWorldIntentSelected[index] = false;
            nativeWorldIntentReadFrame[index] = -1;
            if (consumed) diagnosticGrabConsumed++;
            return consumed;
        }
        private void ResetGrabIntents() { leftGrabIntent.Reset(); rightGrabIntent.Reset(); }
        private void ResetPaddleFistGuards()
        {
            leftPaddleFistGuard.Reset(); rightPaddleFistGuard.Reset();
            leftPaddleOpenEvidence = rightPaddleOpenEvidence = 0f;
        }

        private void UpdateStrokeDetection() => UpdateStrokeDetection(Time.deltaTime);

        private void UpdateStrokeDetection(float dt)
        {
            strokeModel.MinimumPullDistance = minimumPullDistance;
            strokeModel.MinimumPullSpeed = minimumPullSpeed;
            strokeModel.StableTrackingSeconds = stableTrackingSeconds;
            // A free-water thumb/index pinch is not automatically a UI interaction.
            // Panel LateUpdate renews each real target hit for the next locomotion Update.
            bool leftPinching = LeftHandActive && IsMenuHandTargeted(HandType.HandLeft) && IsPinching(leftLocations);
            bool rightPinching = RightHandActive && IsMenuHandTargeted(HandType.HandRight) && IsPinching(rightLocations);
            bool leftAiming = IsSpearAimSuppressed(HandType.HandLeft);
            bool rightAiming = IsSpearAimSuppressed(HandType.HandRight);
            diagnosticFrames++;
            if (LeftPaddleTracked) { diagnosticLeftPaddleTracked++; if (LeftPaddleFistBlocked) diagnosticLeftPaddleFist++; }
            if (RightPaddleTracked) { diagnosticRightPaddleTracked++; if (RightPaddleFistBlocked) diagnosticRightPaddleFist++; }
            if (LeftHandActive)
            {
                diagnosticLeftTracked++;
                if (leftGrasp.SuppressStroke) diagnosticLeftGrasp++;
                if (leftPinching) diagnosticLeftPinch++;
                if (leftAiming) diagnosticLeftAim++;
            }
            if (RightHandActive)
            {
                diagnosticRightTracked++;
                if (rightGrasp.SuppressStroke) diagnosticRightGrasp++;
                if (rightPinching) diagnosticRightPinch++;
                if (rightAiming) diagnosticRightAim++;
            }
            if (interactionSuppressed || menuLocomotionPaused) diagnosticMenu++;
            // Read in the fixed SDK tracking space. Moving/turning the rig cannot fabricate hand speed.
            var output = strokeModel.Step(dt,
                new WaterStrokeModel.HandSample(LeftPaddleTracked, ToStrokeVector(leftPaddleLocal),
                    interactionSuppressed || menuLocomotionPaused || leftPinching || LeftPaddleFistBlocked || leftAiming),
                new WaterStrokeModel.HandSample(RightPaddleTracked, ToStrokeVector(rightPaddleLocal),
                    interactionSuppressed || menuLocomotionPaused || rightPinching || RightPaddleFistBlocked || rightAiming),
                ToStrokeVector(trackingSpace.InverseTransformPoint(head.position)),
                ToStrokeVector(trackingSpace.InverseTransformDirection(head.forward)), seatedMode);
            diagnosticLeftDecisions[(int)strokeModel.LeftDecision]++;
            diagnosticRightDecisions[(int)strokeModel.RightDecision]++;
            if (output.TrackingLost && !output.NoTrackedHands) diagnosticPartialLoss++;
            if (output.NoTrackedHands) diagnosticNoUsableHands++;
            Vector3 headLocal = trackingSpace.InverseTransformPoint(head.position);
            if (LeftPaddleTracked)
            {
                diagnosticLeftMin = Vector3.Min(diagnosticLeftMin, leftPaddleLocal - headLocal);
                diagnosticLeftMax = Vector3.Max(diagnosticLeftMax, leftPaddleLocal - headLocal);
            }
            if (RightPaddleTracked)
            {
                diagnosticRightMin = Vector3.Min(diagnosticRightMin, rightPaddleLocal - headLocal);
                diagnosticRightMax = Vector3.Max(diagnosticRightMax, rightPaddleLocal - headLocal);
            }
            LeftPalmVelocity = trackingSpace.TransformDirection(ToUnityVector(output.LeftVelocity));
            RightPalmVelocity = trackingSpace.TransformDirection(ToUnityVector(output.RightVelocity));
            LeftStrokeStrength = output.LeftStrength;
            RightStrokeStrength = output.RightStrength;
            StrokeDirection = trackingSpace.TransformDirection(ToUnityVector(output.Direction));
            // The recognizer uses tracking coordinates; persistent motion/collisions use world coordinates.
            output.Direction = ToStrokeVector(StrokeDirection);
            swimVelocity = ToUnityVector(WaterStrokeModel.AdvanceVelocity(ToStrokeVector(swimVelocity),
                output, dt, swimSpeed * Comfort.SpeedMultiplier, maxMoveSpeed * Comfort.SpeedMultiplier, glideDamping));
            if (output.NoTrackedHands || interactionSuppressed || menuLocomotionPaused) swimVelocity = Vector3.zero;
            handTurnModel.MaximumYawSpeed = singleHandTurnSpeed * Comfort.TurnMultiplier;
            handTurnModel.PairingDelay = singleHandPairingDelay;
            pendingHandTurn = handTurnModel.Step(dt, output.LeftStrength, output.RightStrength,
                LeftPaddleTracked, RightPaddleTracked, output.TrackingLost,
                !singleHandTurnEnabled || Comfort.Turn == 0 || interactionSuppressed || menuLocomotionPaused || leftPinching || rightPinching);
            // The gesture model clears stale thrust on loss. Keep the motion integrator's
            // deceleration so one occluded hand cannot abruptly stop the other hand's stroke.
            if (enableDebugLog && output.NewStrokes > 0)
                Debug.Log($"[HandSwim] Paddle: new={output.NewStrokes}, total={CompletedStrokeCount}, strength={StrokeStrength:F2}");
        }

        private void ResetStroke()
        {
            strokeModel.Reset();
            handTurnModel.Reset();
            pendingHandTurn = CurrentHandTurnSpeed = HandTurnDegreesThisFrame = 0f;
            LeftPalmVelocity = RightPalmVelocity = StrokeDirection = Vector3.zero;
            LeftStrokeStrength = RightStrokeStrength = 0f;
        }

        /// <summary>UI can suspend all locomotion while retaining valid hand tracking and visuals.</summary>
        public void SetInteractionSuppressed(bool suppressed)
        {
            if (interactionSuppressed == suppressed) return;
            interactionSuppressed = suppressed;
            snapTurnGate.Reset();
            leftGrasp.Reset();
            rightGrasp.Reset();
            ResetPaddleFistGuards();
            ResetGrabIntents();
            ResetNativePinchInput();
            ResetStroke();
            swimVelocity = currentVelocity = Vector3.zero;
        }

        /// <summary>Menus pause artificial movement while fresh real hand/grasp samples remain available to UI.</summary>
        public void SetMenuLocomotionPaused(bool paused)
        {
            if (menuLocomotionPaused == paused) return;
            menuLocomotionPaused = paused;
            leftPaddleOpenEvidence = rightPaddleOpenEvidence = 0f;
            snapTurnGate.Reset();
            ResetGrabIntents();
            // Preserve whole-gesture UI ownership through Close/Resume; its old world windows are discarded.
            nativeVisionOSPinch.ResetForMenu();
            ResetNativePinchWindows();
            swimVelocity = currentVelocity = Vector3.zero;
            ResetStroke();
            if (!paused)
            {
                // The fist that pressed Resume must not also collect a world object.
                leftGrasp.Reset(); rightGrasp.Reset();
                ResetPaddleFistGuards();
                relocationFrame = Time.frameCount;
            }
        }

        /// <summary>
        /// Repositions the tracked eye by moving only XR Origin, with horizontal
        /// heading alignment. The caller must validate destination clearance. Head/hand
        /// local tracking poses are never assigned; the next physical sample stays live.
        /// </summary>
        public bool TryRelocateHead(Vector3 desiredEye, Vector3 desiredFlatForward)
        {
            if (!isActiveAndEnabled || appPaused || !appFocused || xrOrigin == null || head == null ||
                trackingSpace == null || head == xrOrigin || !head.IsChildOf(xrOrigin) ||
                !IsFinite(desiredEye.x) || !IsFinite(desiredEye.y) || !IsFinite(desiredEye.z) ||
                !IsFinite(desiredFlatForward.x) || !IsFinite(desiredFlatForward.y) || !IsFinite(desiredFlatForward.z))
                return false;
            Vector3 currentFlat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            Vector3 targetFlat = new Vector3(desiredFlatForward.x, 0f, desiredFlatForward.z);
            // Looking exactly up/down has no stable horizontal heading; let the caller
            // retry after a fresh pose instead of inventing a head orientation.
            if (!IsFinite(currentFlat.sqrMagnitude) || !IsFinite(targetFlat.sqrMagnitude) ||
                currentFlat.sqrMagnitude < .000001f || targetFlat.sqrMagnitude < .000001f) return false;
            Quaternion yaw = Quaternion.AngleAxis(Vector3.SignedAngle(currentFlat, targetFlat, Vector3.up), Vector3.up);
            Vector3 targetOrigin = desiredEye + yaw * (xrOrigin.position - head.position);
            if (!IsFinite(targetOrigin.x) || !IsFinite(targetOrigin.y) || !IsFinite(targetOrigin.z)) return false;

            xrOrigin.SetPositionAndRotation(targetOrigin, yaw * xrOrigin.rotation);
            swimVelocity = currentVelocity = Vector3.zero;
            ResetStroke();
            leftGrasp.Reset();
            rightGrasp.Reset();
            ResetPaddleFistGuards();
            ResetGrabIntents();
            ResetNativePinchInput();
            snapTurnGate.Reset();
            relocationFrame = Time.frameCount;
            handSampleFrame = -1;
            RefreshPalmWorldPositions();
            return true;
        }

        private Vector2 ApplyDeadZone(Vector2 input)
        {
            float magnitude = Mathf.Min(input.magnitude, 1f);
            float threshold = Mathf.Clamp(joystickDeadZone, 0f, 0.5f);
            if (magnitude <= threshold) return Vector2.zero;
            return input.normalized * ((magnitude - threshold) / (1f - threshold));
        }

        private void UpdateLocomotion()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            HandTurnDegreesThisFrame = CurrentHandTurnSpeed = 0f;
            if (interactionSuppressed || menuLocomotionPaused) return;
            // Shared by both overlap checks: physical leaning never causes an unbounded rig jump.
            remainingOverlapCorrection = Mathf.Min(0.04f, Mathf.Max(0.1f, overlapCorrectionSpeed) * dt);
            leftController.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 rawMove);
            bool lookAvailable = rightController.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 rawLook);
            Vector2 move = ApplyDeadZone(rawMove);
            Vector2 look = ApplyDeadZone(rawLook);
            bool controllerSteering = Comfort.Turn != 0 && Mathf.Abs(rawLook.x) > 0.25f;
            if (controllerSteering)
            {
                handTurnModel.Reset();
                pendingHandTurn = 0f;
            }
            Vector3 forward = HorizontalForward();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 controllerVelocity = ((forward * move.y + right * move.x) * joystickSpeed + Vector3.up * look.y * verticalSpeed) * Comfort.SpeedMultiplier;
            // Cap combined input too: a stroke and joystick cannot stack above the speed limit.
            Vector3 targetVelocity = Vector3.ClampMagnitude(swimVelocity + controllerVelocity, maxMoveSpeed * Comfort.SpeedMultiplier);
            float rate = targetVelocity.sqrMagnitude > currentVelocity.sqrMagnitude ? acceleration : deceleration;
            currentVelocity = Vector3.MoveTowards(currentVelocity, targetVelocity, rate * dt);
            currentVelocity = Vector3.ClampMagnitude(currentVelocity, maxMoveSpeed * Comfort.SpeedMultiplier);
            ConstrainEyeAboveSeabed();
            ResolveRockOverlaps();
            Vector3 beforeMove = xrOrigin.position;
            Vector3 requestedMovement = currentVelocity * dt;
            if (UnderwaterWorld.RenderedSeabedEnvironment == OceanEnvironmentKind.CoralSanctuary)
            {
                float clearance = Mathf.Max(.1f, bodyRadius) + Mathf.Max(.005f, collisionSkin);
                Vector3 eye = head.position;
                requestedMovement.x = OceanEnvironmentProfile.LimitSanctuaryOutwardStep(
                    eye.x - OceanEnvironmentProfile.SpawnX, requestedMovement.x, clearance);
                requestedMovement.z = OceanEnvironmentProfile.LimitSanctuaryOutwardStep(
                    eye.z - OceanEnvironmentProfile.SpawnZ, requestedMovement.z, clearance);
            }
            MoveWithCollision(requestedMovement);
            diagnosticRequestedMetres += requestedMovement.magnitude;
            diagnosticMovedMetres += Vector3.Distance(beforeMove, xrOrigin.position);

            float turn = 0f;
            if (snapTurnEnabled)
            {
                turn = snapTurnGate.Step(rawLook.x, snapTurnDegrees * Comfort.TurnMultiplier, lookAvailable);
            }
            else turn = look.x * turnSpeed * Comfort.TurnMultiplier * dt;
            if (Mathf.Abs(turn) > 0.001f)
            {
                // Turn around the head so seated or physically off-centre users do not orbit the rig.
                xrOrigin.RotateAround(head.position, Vector3.up, turn);
                ResetStroke();
                swimVelocity = Vector3.zero;
                if (snapTurnEnabled) currentVelocity = Vector3.zero;
            }
            else if (!controllerSteering && Mathf.Abs(pendingHandTurn) > 0.00001f)
            {
                // Tracking-space samples stay unchanged. Only world pose/momentum rotate;
                // never reset the paddle recognizer each frame or write the camera's local pose.
                Quaternion yaw = Quaternion.AngleAxis(pendingHandTurn, Vector3.up);
                xrOrigin.RotateAround(head.position, Vector3.up, pendingHandTurn);
                swimVelocity = yaw * swimVelocity;
                currentVelocity = yaw * currentVelocity;
                LeftPalmVelocity = yaw * LeftPalmVelocity;
                RightPalmVelocity = yaw * RightPalmVelocity;
                StrokeDirection = yaw * StrokeDirection;
                HandTurnDegreesThisFrame = pendingHandTurn;
                CurrentHandTurnSpeed = dt > 0f ? pendingHandTurn / dt : 0f;
                TotalHandTurnDegrees += pendingHandTurn;
                TotalAbsoluteHandTurnDegrees += Mathf.Abs(pendingHandTurn);
            }
            if (UnderwaterWorld.RenderedSeabedEnvironment == OceanEnvironmentKind.CoralSanctuary)
                ConstrainSanctuaryBoundary();
            else
            {
                Vector3 p = xrOrigin.position;
                p.x = Mathf.Clamp(p.x, -50f, 50f);
                p.z = Mathf.Clamp(p.z, -50f, 50f);
                xrOrigin.position = p;
            }
            ConstrainEyeAboveSeabed();
            ResolveRockOverlaps();
            if (UnderwaterWorld.RenderedSeabedEnvironment == OceanEnvironmentKind.CoralSanctuary)
                ConstrainSanctuaryBoundary();
            ConstrainEyeAboveSeabed();
        }

        private void ConstrainSanctuaryBoundary()
        {
            float clearance = Mathf.Max(.1f, bodyRadius) + Mathf.Max(.005f, collisionSkin);
            Vector3 eye = head.position;
            float localX = eye.x - OceanEnvironmentProfile.SpawnX;
            float localZ = eye.z - OceanEnvironmentProfile.SpawnZ;
            // Subtract in the same coordinate space as the clamp. Reconstructing a
            // world coordinate by adding SpawnZ can round even an interior eye pose,
            // and that tiny false correction would clear forward momentum below.
            Vector3 correction = new Vector3(
                OceanEnvironmentProfile.ClampSanctuaryEyeCoordinate(localX, clearance) - localX, 0f,
                OceanEnvironmentProfile.ClampSanctuaryEyeCoordinate(localZ, clearance) - localZ);
            xrOrigin.position += correction;
            // Clear only momentum pressing into the boundary. No input/hand recognizer
            // reset: an inward stroke, vertical ascent or sideways turn remains usable.
            if (correction.x != 0f)
            {
                if (currentVelocity.x * correction.x < 0f) currentVelocity.x = 0f;
                if (swimVelocity.x * correction.x < 0f) swimVelocity.x = 0f;
            }
            if (correction.z != 0f)
            {
                if (currentVelocity.z * correction.z < 0f) currentVelocity.z = 0f;
                if (swimVelocity.z * correction.z < 0f) swimVelocity.z = 0f;
            }
        }

        private void GetBodyCapsule(out Vector3 bottom, out Vector3 top, out float radius)
            => GetBodyCapsuleAtEye(head.position, out bottom, out top, out radius);

        private void GetBodyCapsuleAtEye(Vector3 eye, out Vector3 bottom, out Vector3 top, out float radius)
        {
            radius = Mathf.Max(0.1f, bodyRadius);
            float height = Mathf.Max(radius * 2f, seatedMode ? bodyHeight * 0.75f : bodyHeight);
            // World-space vertical capsule follows the real head position but never its pitch/roll.
            top = eye + Vector3.up * (0.08f - radius);
            bottom = top - Vector3.up * (height - radius * 2f);
            // Close inspection near sand must not leave a full standing capsule buried
            // below the rendered floor and intersecting rock volumes beside the player.
            float floor = UnderwaterWorld.SampleRenderedSeabedHeight(eye.x, eye.z);
            bottom.y = Mathf.Min(top.y, Mathf.Max(bottom.y, floor + radius + Mathf.Max(.005f, collisionSkin)));
        }

        private void MoveWithCollision(Vector3 displacement)
        {
            float skin = Mathf.Max(0.005f, collisionSkin);
            // One impact plus one slide preserves forward motion along a rock face.
            // A 1mm/frame cutoff discarded every gentle swim below .09m/s at 90Hz.
            // Keep a numerical 10-micrometre epsilon, without weakening real collision skin.
            for (int iteration = 0; iteration < 2 && displacement.sqrMagnitude > 0.0000000001f; iteration++)
            {
                GetBodyCapsule(out Vector3 bottom, out Vector3 top, out float radius);
                float distance = displacement.magnitude;
                Vector3 direction = displacement / distance;
                int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction,
                    movementHits, distance + skin, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float nearest = distance + skin;
                Vector3 normal = Vector3.zero;
                for (int i = 0; i < count; i++)
                {
                    var hit = movementHits[i];
                    if (hit.collider == null || hit.collider.transform.IsChildOf(xrOrigin)) continue;
                    if (hit.distance <= .0001f && IsSeparatingSphereOverlap(hit.collider as SphereCollider,
                        bottom, top, radius, displacement)) continue;
                    if (hit.distance <= .0001f && IsSeparatingMeshOverlap(hit.collider as MeshCollider,
                        bottom, top, radius, displacement)) continue;
                    if (hit.distance <= nearest)
                    {
                        nearest = hit.distance;
                        normal = hit.normal;
                        diagnosticLastCollision = hit.collider.name;
                    }
                }
                if (normal.sqrMagnitude < 0.001f)
                {
                    xrOrigin.position += displacement;
                    return;
                }
                float allowed = Mathf.Clamp(nearest - skin, 0f, distance);
                if (diagnosticCollisionFrame != Time.frameCount)
                { diagnosticCollisionFrames++; diagnosticCollisionFrame = Time.frameCount; }
                xrOrigin.position += direction * allowed;
                displacement = Vector3.ProjectOnPlane(direction * (distance - allowed), normal);
                currentVelocity = Vector3.ProjectOnPlane(currentVelocity, normal);
                swimVelocity = Vector3.ProjectOnPlane(swimVelocity, normal);
            }
        }

        private bool IsSeparatingSphereOverlap(SphereCollider sphere, Vector3 bottom, Vector3 top, float radius, Vector3 movement)
        {
            if (sphere == null || movement.sqrMagnitude > .1251f * .1251f) return false;
            Vector3 center = sphere.transform.TransformPoint(sphere.center), scale = sphere.transform.lossyScale;
            float combined = radius + sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 closest = new Vector3(bottom.x, Mathf.Clamp(center.y, bottom.y, top.y), bottom.z);
            Vector3 away = closest - center;
            float before = away.sqrMagnitude;
            if (before >= combined * combined || before < .00000001f || Vector3.Dot(movement, away) <= .00000001f) return false;
            GetBodyCapsuleAtEye(head.position + movement, out Vector3 endBottom, out Vector3 endTop, out _);
            Vector3 endClosest = new Vector3(endBottom.x, Mathf.Clamp(center.y, endBottom.y, endTop.y), endBottom.z);
            // Only this already-overlapped sphere is filtered. Every other cast hit is
            // retained, including an overhang ahead; crossing through a sphere cannot qualify.
            return (endClosest - center).sqrMagnitude > before + .00000001f;
        }

        private bool TryMeshPenetration(MeshCollider mesh, Vector3 bottom, Vector3 top, float radius,
            out Vector3 direction, out float depth)
        {
            direction = Vector3.zero; depth = 0;
            if (mesh == null || mesh.sharedMesh == null || !mesh.enabled || !mesh.gameObject.activeInHierarchy) return false;
            if (meshOverlapProbe == null)
            {
                // Query-only primitive, disabled between direct penetration queries.
                // A capsule can be paired with a non-convex mesh in ComputePenetration.
                var probe = new GameObject("Swimmer mesh-overlap query") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                meshOverlapProbe = probe.AddComponent<CapsuleCollider>();
                meshOverlapProbe.enabled = false; meshOverlapProbe.direction = 1;
                meshOverlapProbe.center = Vector3.zero;
            }
            meshOverlapProbe.radius = radius;
            meshOverlapProbe.height = Vector3.Distance(bottom, top) + radius * 2f;
            // Unity 6000.0.84f1 returns false with a disabled primitive even for a proven
            // 7cm overlap. Enable its native shape only for this synchronous direct query;
            // no simulation/query can run between it and the finally block on this thread.
            meshOverlapProbe.enabled = true;
            try
            {
                return Physics.ComputePenetration(meshOverlapProbe, (bottom + top) * .5f, Quaternion.identity,
                    mesh, mesh.transform.position, mesh.transform.rotation, out direction, out depth) &&
                    IsFinite(depth) && depth > 0f && IsFinite(direction.x) && IsFinite(direction.y) && IsFinite(direction.z);
            }
            finally { meshOverlapProbe.enabled = false; }
        }

        private bool IsSeparatingMeshOverlap(MeshCollider mesh, Vector3 bottom, Vector3 top, float radius, Vector3 movement)
        {
            if (movement.sqrMagnitude > .1251f * .1251f ||
                !TryMeshPenetration(mesh, bottom, top, radius, out Vector3 away, out float before) ||
                Vector3.Dot(movement, away) <= .00000001f) return false;
            GetBodyCapsuleAtEye(head.position + movement, out Vector3 endBottom, out Vector3 endTop, out float endRadius);
            // Ignore only the already-overlapped mesh, and only when the requested short
            // stroke reduces penetration along the same outward side. Other hits stay live.
            if (!TryMeshPenetration(mesh, endBottom, endTop, endRadius, out Vector3 endAway, out float after)) return true;
            return Vector3.Dot(away, endAway) > 0f && after < before - .000001f;
        }

        private bool ResolveMeshOverlap(MeshCollider mesh, Vector3 bottom, Vector3 top, float radius)
        {
            if (!TryMeshPenetration(mesh, bottom, top, radius, out Vector3 away, out float depth)) return false;
            float distance = Mathf.Min(depth + Mathf.Max(.005f, collisionSkin), remainingOverlapCorrection);
            Vector3 correction = away * distance;
            // An arch ceiling may push down, but never under the sand where the floor
            // constraint would push back up and create alternating whole-world motion.
            float minimumEye = UnderwaterWorld.SampleRenderedSeabedHeight(head.position.x + correction.x, head.position.z + correction.z) + .45f;
            correction.y = Mathf.Max(correction.y, Mathf.Min(0f, minimumEye - head.position.y));
            float actual = correction.magnitude;
            if (actual <= .00001f) return false;
            xrOrigin.position += correction;
            remainingOverlapCorrection = Mathf.Max(0f, remainingOverlapCorrection - actual);
            Vector3 normal = correction / actual;
            if (Vector3.Dot(currentVelocity, normal) < 0f) currentVelocity = Vector3.ProjectOnPlane(currentVelocity, normal);
            if (Vector3.Dot(swimVelocity, normal) < 0f) swimVelocity = Vector3.ProjectOnPlane(swimVelocity, normal);
            return true;
        }

        private void ResolveRockOverlaps()
        {
            // Casts alone cannot resolve an initial overlap caused by physical head movement.
            // Keep classic sphere correction intact; sanctuary meshes use their real surfaces.
            for (int pass = 0; pass < 2; pass++)
            {
                if (remainingOverlapCorrection <= 0.00001f) return;
                GetBodyCapsule(out Vector3 bottom, out Vector3 top, out float radius);
                int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlappingObstacles,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                bool moved = false;
                for (int i = 0; i < count; i++)
                {
                    Collider obstacle = overlappingObstacles[i];
                    if (obstacle == null || obstacle.transform.IsChildOf(xrOrigin)) continue;
                    if (obstacle is MeshCollider mesh)
                    {
                        GetBodyCapsule(out bottom, out top, out radius);
                        moved |= ResolveMeshOverlap(mesh, bottom, top, radius);
                        if (remainingOverlapCorrection <= .00001f) return;
                        continue;
                    }
                    var sphere = overlappingObstacles[i] as SphereCollider;
                    if (sphere == null || sphere.transform.IsChildOf(xrOrigin)) continue;
                    GetBodyCapsule(out bottom, out top, out radius);
                    Vector3 centre = sphere.transform.TransformPoint(sphere.center);
                    Vector3 scale = sphere.transform.lossyScale;
                    float obstacleRadius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                    float verticalGap = Mathf.Clamp(centre.y, bottom.y, top.y) - centre.y;
                    float clearanceRadius = radius + obstacleRadius + Mathf.Max(0.005f, collisionSkin);
                    float horizontalClearance = Mathf.Sqrt(Mathf.Max(0f,
                        clearanceRadius * clearanceRadius - verticalGap * verticalGap));
                    Vector3 away = new Vector3(top.x - centre.x, 0f, top.z - centre.z);
                    float distance = away.magnitude;
                    float penetration = horizontalClearance - distance;
                    if (penetration <= 0f) continue;
                    // Resolve sideways, never down into the seabed and back up in the next constraint.
                    Vector3 normal = distance > 0.0001f ? away / distance : -HorizontalForward();
                    float correction = Mathf.Min(penetration, remainingOverlapCorrection);
                    xrOrigin.position += normal * correction;
                    remainingOverlapCorrection -= correction;
                    if (Vector3.Dot(currentVelocity, normal) < 0f) currentVelocity = Vector3.ProjectOnPlane(currentVelocity, normal);
                    if (Vector3.Dot(swimVelocity, normal) < 0f) swimVelocity = Vector3.ProjectOnPlane(swimVelocity, normal);
                    moved = true;
                    if (remainingOverlapCorrection <= 0.00001f) return;
                }
                if (!moved) break;
            }
        }

        private void ConstrainEyeAboveSeabed()
        {
            Vector3 eye = head.position;
            float minimumEyeY = UnderwaterWorld.SampleRenderedSeabedHeight(eye.x, eye.z) + 0.45f;
            if (eye.y >= minimumEyeY) return;
            xrOrigin.position += Vector3.up * (minimumEyeY - eye.y);
            if (currentVelocity.y < 0f) currentVelocity.y = 0f;
            if (swimVelocity.y < 0f) swimVelocity.y = 0f;
        }

        /// <summary>Returns only a fresh tracked palm position, in Unity world space.</summary>
        public bool TryGetPalmPosition(HandType hand, out Vector3 worldPosition)
        {
            bool valid = isActiveAndEnabled && !appPaused && appFocused && handSampleFrame == Time.frameCount &&
                (hand == HandType.HandLeft ? LeftHandActive : hand == HandType.HandRight && RightHandActive);
            worldPosition = valid ? (hand == HandType.HandLeft ? LeftPalmPos : RightPalmPos) : Vector3.zero;
            return valid;
        }

        /// <summary>Fresh index-tip position from the same validated joint sample used by grasp recognition.</summary>
        public bool TryGetIndexTipPosition(HandType hand, out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            if (!isActiveAndEnabled || appPaused || !appFocused || trackingSpace == null || handSampleFrame != Time.frameCount)
                return false;
            bool valid = hand == HandType.HandLeft ? LeftHandActive : hand == HandType.HandRight && RightHandActive;
            if (!valid) return false;
            if (!TryReadJoint(hand == HandType.HandLeft ? leftLocations : rightLocations, (int)HandJoint.JointIndexTip, out Vector3 local)) return false;
            worldPosition = trackingSpace.TransformPoint(local);
            return true;
        }

        /// <summary>Index curl from the same tracked joint sample as pickup; no native strength aliases.</summary>
        public bool TryGetSpearIndexCurl(HandType hand, out float curl)
        {
            curl = 0f;
            if (!GraspSampleFresh || !TryGetPalmPosition(hand, out _)) return false;
            var locations = hand == HandType.HandLeft ? leftLocations : rightLocations;
            return TryReadGraspFinger(locations, HandJoint.JointIndexProximal, HandJoint.JointIndexIntermediate,
                HandJoint.JointIndexDistal, HandJoint.JointIndexTip, out var finger) &&
                ShellGrabGesture.TryFingerCurl(finger, out curl);
        }

        /// <summary>
        /// Sustained tool grip excludes the trigger finger. Pickup still requires the normal fresh fist edge,
        /// while this raw, validated pose can recover after a tracking hitch without inventing another pickup.
        /// </summary>
        public bool TryGetSpearGrip(HandType hand, out bool gripping)
        {
            gripping = false;
            if (!GraspSampleFresh || !TryGetPalmPosition(hand, out _)) return false;
            var sample = ReadGraspSample(true, hand == HandType.HandLeft ? leftLocations : rightLocations);
            if (!sample.Tracked || !ShellGrabGesture.TryFingerCurl(sample.Middle, out float middle) ||
                !ShellGrabGesture.TryFingerCurl(sample.Ring, out float ring) ||
                !ShellGrabGesture.TryFingerCurl(sample.Little, out float little)) return false;
            gripping = Mathf.Min(middle, Mathf.Min(ring, little)) > ShellGrabGesture.ReleaseCurl;
            return true;
        }

        /// <summary>
        /// Same-frame validated joints only. Grip excludes index/thumb; thumb must be
        /// straight and raised, and the palm must reach in front of the viewer. aimDir is
        /// a world-space hand-back direction, independent of the index tip's trigger curl.
        /// A valid sample can return false pose flags; missing required joints return false.
        /// </summary>
        public bool TryGetSpearPose(HandType hand, out bool gripping, out bool thumbExtended,
            out float indexCurl, out Vector3 aimDir, out bool handExtended)
        {
            gripping = thumbExtended = handExtended = false;
            indexCurl = 0f; aimDir = Vector3.zero;
            if (head == null || trackingSpace == null || !TryGetSpearGrip(hand, out gripping) ||
                !TryGetSpearIndexCurl(hand, out indexCurl)) return false;
            var locations = hand == HandType.HandLeft ? leftLocations : rightLocations;
            if (!TryReadGraspFinger(locations, HandJoint.JointThumbMetacarpal, HandJoint.JointThumbProximal,
                HandJoint.JointThumbDistal, HandJoint.JointThumbTip, out var thumb) ||
                !ShellGrabGesture.TryFingerCurl(thumb, out float thumbCurl) ||
                !TryReadJoint(locations, (int)HandJoint.JointIndexMetacarpal, out Vector3 indexBase) ||
                !TryReadJoint(locations, (int)HandJoint.JointIndexProximal, out Vector3 indexKnuckle)) return false;
            Vector3 shaft = trackingSpace.TransformDirection(indexKnuckle - indexBase);
            if (!IsFinite(shaft.sqrMagnitude) || shaft.sqrMagnitude < .000025f || shaft.sqrMagnitude > .0225f)
                return false;
            aimDir = shaft.normalized;
            Vector3 thumbShaft = trackingSpace.TransformDirection(ToUnityVector(thumb.Tip - thumb.Intermediate));
            float thumbLength = thumbShaft.magnitude;
            if (!IsFinite(thumbLength) || thumbLength < .008f || thumbLength > .15f) return false;
            thumbExtended = thumbCurl <= .16f && thumbShaft.y >= .012f && thumbShaft.y / thumbLength >= .35f;
            Vector3 palm = hand == HandType.HandLeft ? LeftPalmPos : RightPalmPos;
            Vector3 relative = palm - head.position;
            float distance = relative.magnitude;
            handExtended = IsFinite(distance) && distance >= .25f && distance <= .85f &&
                Vector3.Dot(relative, head.forward) >= .16f && relative.y >= -.70f && relative.y <= .20f;
            return true;
        }

        /// <summary>
        /// The tool renews its visible aiming hands each frame. A stale request expires
        /// after the next Update, and releasing the actual pose cancels it immediately.
        /// No SDK query, tracking override, grab reset or global movement pause occurs.
        /// </summary>
        public void SetSpearAimSuppressed(bool left, bool right)
        {
            bool expired = Time.frameCount - spearAimFrame > 1;
            bool leftEntered = left && (!spearAimLeft || expired);
            bool rightEntered = right && (!spearAimRight || expired);
            spearAimLeft = left; spearAimRight = right; spearAimFrame = Time.frameCount;
            if (left) leftGrabIntent.Reset();
            if (right) rightGrabIntent.Reset();
            // Shared velocity includes the other hand's propulsion and normal water glide.
            // Equipping/re-equipping a gun must not zero it. The actual pose guard in
            // UpdateStrokeDetection already ends only the aiming hand's paddle segment.
            var turnState = handTurnModel.State;
            if ((leftEntered && (turnState == SingleHandTurnModel.TurnState.PendingLeft ||
                    turnState == SingleHandTurnModel.TurnState.LeftPull)) ||
                (rightEntered && (turnState == SingleHandTurnModel.TurnState.PendingRight ||
                    turnState == SingleHandTurnModel.TurnState.RightPull)))
            {
                // Cancel a deferred turn only when its own steering hand begins aiming.
                handTurnModel.Reset(); pendingHandTurn = 0f;
            }
        }

        private bool IsSpearAimSuppressed(HandType hand)
        {
            if (spearAimFrame < 0 || Time.frameCount - spearAimFrame > 1 ||
                !(hand == HandType.HandLeft ? spearAimLeft : spearAimRight)) return false;
            return TryGetSpearPose(hand, out bool grip, out bool thumb, out _, out _, out bool reach) &&
                grip && thumb && reach;
        }

        /// <summary>
        /// Panel renews a fresh, interactable ray target after its hit test each frame.
        /// A false result clears immediately; a missed update expires automatically.
        /// This gates only that hand's pinch suppression. An open menu still globally
        /// pauses artificial locomotion through SetMenuLocomotionPaused.
        /// </summary>
        public void SetMenuHandTargeted(HandType hand, bool targeted)
        {
            if (hand == HandType.HandLeft)
            { menuLeftTargeted = targeted; menuLeftTargetFrame = Time.frameCount; }
            else if (hand == HandType.HandRight)
            { menuRightTargeted = targeted; menuRightTargetFrame = Time.frameCount; }
        }

        private bool IsMenuHandTargeted(HandType hand)
        {
            bool left = hand == HandType.HandLeft;
            if (!left && hand != HandType.HandRight) return false;
            int sampleFrame = left ? menuLeftTargetFrame : menuRightTargetFrame;
            long age = (long)Time.frameCount - sampleFrame;
            return sampleFrame >= 0 && age >= 0 && age <= 1 && (left ? menuLeftTargeted : menuRightTargeted);
        }

        private void TraceSwimDiagnostics()
        {
            float dt = Time.unscaledDeltaTime;
            if (!IsFinite(dt) || dt <= 0f) return;
            diagnosticSeconds += dt;
            if (diagnosticSeconds < 10f) return;
            // Kept below Android's per-line truncation threshold. Counts have explicit
            // sample denominators and can overlap; movement excludes physical head motion
            // and separate overlap corrections. This is diagnosis, not a collision verdict.
            Debug.Log($"[SwimInput] seconds={diagnosticSeconds:F2} frames={diagnosticFrames} trackedL={diagnosticLeftTracked} trackedR={diagnosticRightTracked} paddleTrackedL={diagnosticLeftPaddleTracked} paddleTrackedR={diagnosticRightPaddleTracked} paddleFistL={diagnosticLeftPaddleFist} paddleFistR={diagnosticRightPaddleFist} graspL={diagnosticLeftGrasp} graspR={diagnosticRightGrasp} pinchL={diagnosticLeftPinch} pinchR={diagnosticRightPinch} aimL={diagnosticLeftAim} aimR={diagnosticRightAim} globalPause={diagnosticMenu} requestedM={diagnosticRequestedMetres:F4} movedM={diagnosticMovedMetres:F4} collisionFrames={diagnosticCollisionFrames} lastCollision={diagnosticLastCollision} strokes={CompletedStrokeCount} speed={CurrentSpeed:F3}; tracked=interactionPalm paddle={(paddleUsesWrist ? "wrist" : "palm")}");
            Debug.Log($"[GrabInput] frames={diagnosticFrames} palmL={diagnosticLeftTracked} palmR={diagnosticRightTracked} fingersValidL={diagnosticLeftFingerValid} fingersValidR={diagnosticRightFingerValid} fingerMaskL={leftFingerMask} fingerMaskR={rightFingerMask} curlsL={leftFingerCurls:F3} curlsR={rightFingerCurls:F3} armedL={leftGrasp.Armed} armedR={rightGrasp.Armed} edgeL={diagnosticLeftGrabEdges} edgeR={diagnosticRightGrabEdges} intentL={leftGrabIntent.RemainingSeconds:F3} intentR={rightGrabIntent.RemainingSeconds:F3} attempts={diagnosticGrabAttempts} consumed={diagnosticGrabConsumed}; fingerMask bits=middle/ring/little, curl NaN=unavailable");
            Debug.Log($"[PaddleDecision] frames={diagnosticFrames} L={DecisionCounts(diagnosticLeftDecisions)} R={DecisionCounts(diagnosticRightDecisions)} partialLoss={diagnosticPartialLoss} noUsableHands={diagnosticNoUsableHands} relativePaddleBoundsL={diagnosticLeftMin:F2}..{diagnosticLeftMax:F2} relativePaddleBoundsR={diagnosticRightMin:F2}..{diagnosticRightMax:F2}; paddle={(paddleUsesWrist ? "wrist" : "palm")} bounds metres in tracking axes relative to head, not world travel");
            System.Array.Clear(diagnosticLeftDecisions, 0, diagnosticLeftDecisions.Length);
            System.Array.Clear(diagnosticRightDecisions, 0, diagnosticRightDecisions.Length);
            diagnosticPartialLoss = diagnosticNoUsableHands = 0;
            diagnosticLeftMin = diagnosticRightMin = Vector3.one * float.PositiveInfinity;
            diagnosticLeftMax = diagnosticRightMax = Vector3.one * float.NegativeInfinity;
            diagnosticLeftFingerValid = diagnosticRightFingerValid = diagnosticLeftGrabEdges = diagnosticRightGrabEdges = diagnosticGrabAttempts = diagnosticGrabConsumed = 0;
            diagnosticFrames = diagnosticLeftTracked = diagnosticRightTracked = 0;
            diagnosticLeftPaddleTracked = diagnosticRightPaddleTracked = diagnosticLeftPaddleFist = diagnosticRightPaddleFist = 0;
            diagnosticLeftGrasp = diagnosticRightGrasp = diagnosticLeftPinch = diagnosticRightPinch = 0;
            diagnosticLeftAim = diagnosticRightAim = diagnosticMenu = diagnosticCollisionFrames = 0;
            diagnosticSeconds = diagnosticRequestedMetres = diagnosticMovedMetres = 0f;
            diagnosticLastCollision = "none";
        }

        private static string DecisionCounts(int[] counts)
        {
            var summary = new System.Text.StringBuilder();
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] == 0) continue;
                if (summary.Length > 0) summary.Append(',');
                summary.Append((WaterStrokeModel.PaddleDecision)i).Append(':').Append(counts[i]);
            }
            return summary.ToString();
        }

        /// <summary>
        /// Fresh PICO aim ray in Unity world space and 0..1 thumb/index-tip pinch strength.
        /// The ray retains the SDK aim pose; pinch comes from this frame's validated
        /// joint positions because legacy touchStrengthRay can differ from finger pinch.
        /// Private per-finger strength fields are never read. The UI owns release-to-arm
        /// and pinch hysteresis, and must disarm whenever this method returns false.
        /// </summary>
        public bool TryGetMenuHandRay(HandType hand, out Ray worldRay, out float pinchStrength)
        {
            return TryGetMenuHandPointer(hand, out worldRay, out pinchStrength, out bool pinchReady) && pinchReady;
        }

        /// <summary>A current pointer may be drawn while hidden fingertips keep pinch interaction disarmed.</summary>
        public bool TryGetMenuHandPointer(HandType hand, out Ray worldRay, out float pinchStrength, out bool pinchReady)
        {
            worldRay = default; pinchStrength = float.NaN; pinchReady = false;
            if (!GraspSampleFresh || trackingSpace == null) return false;
            if (hand == HandType.HandLeft && LeftHandActive)
                return ReadMenuHandRay(hand, ref leftMenuAim, out worldRay, out pinchStrength, out pinchReady);
            if (hand == HandType.HandRight && RightHandActive)
                return ReadMenuHandRay(hand, ref rightMenuAim, out worldRay, out pinchStrength, out pinchReady);
            return false;
        }

        /// <summary>Read-only cinematic UI input while world interaction is suppressed. This
        /// never exposes a grab/stroke and remains subject to current tracking and focus.</summary>
        public bool TryGetEntryHandPointer(HandType hand, out Ray worldRay, out float pinchStrength, out bool pinchReady)
        {
            worldRay = default; pinchStrength = float.NaN; pinchReady = false;
            if (!OceanEntry.BlocksGameplay || !CanReadTrackedVisuals || trackingSpace == null ||
                handSampleFrame != Time.frameCount || relocationFrame == Time.frameCount) return false;
            if (hand == HandType.HandLeft && LeftHandActive)
                return ReadMenuHandRay(hand, ref leftMenuAim, out worldRay, out pinchStrength, out pinchReady);
            if (hand == HandType.HandRight && RightHandActive)
                return ReadMenuHandRay(hand, ref rightMenuAim, out worldRay, out pinchStrength, out pinchReady);
            return false;
        }

        /// <summary>One current system pinch press; no inferred Palm/finger pose can synthesize this event.</summary>
        public bool TryGetNativeVisionOSMenuPinch(int slot, out Ray worldRay)
        {
            worldRay = default;
            if (!NativePinchReadable || !nativeVisionOSPinch.TryGetMenuPress(slot, Time.frameCount, out Ray localRay) ||
                !IsFinite(localRay.origin.x) || !IsFinite(localRay.origin.y) || !IsFinite(localRay.origin.z) ||
                !IsFinite(localRay.direction.sqrMagnitude) || localRay.direction.sqrMagnitude < .000001f) return false;
            Vector3 direction = trackingSpace.TransformDirection(localRay.direction.normalized);
            Vector3 origin = trackingSpace.TransformPoint(localRay.origin);
            if (!IsFinite(origin.x) || !IsFinite(origin.y) || !IsFinite(origin.z) ||
                !IsFinite(direction.sqrMagnitude) || direction.sqrMagnitude < .000001f) return false;
            worldRay = new Ray(origin, direction.normalized); return true;
        }

        public void ConsumeNativeVisionOSMenuPinch(int slot)
        {
            if (!TryGetNativeVisionOSMenuPinch(slot, out _)) return;
            bool leftBefore = nativeVisionOSPinch.TryGetWorldPinch(HandType.HandLeft, Time.frameCount, out _, out _, out _);
            bool rightBefore = nativeVisionOSPinch.TryGetWorldPinch(HandType.HandRight, Time.frameCount, out _, out _, out _);
            if (!nativeVisionOSPinch.ConsumeByMenu(slot, Time.frameCount)) return;
            bool leftClaimed = leftBefore && !nativeVisionOSPinch.TryGetWorldPinch(HandType.HandLeft, Time.frameCount, out _, out _, out _);
            bool rightClaimed = rightBefore && !nativeVisionOSPinch.TryGetWorldPinch(HandType.HandRight, Time.frameCount, out _, out _, out _);
            // A known owner cannot spend either its native or simultaneous fist window in Treasure LateUpdate.
            // With no unique owner, discard fist windows rather than guessing chirality.
            if (leftClaimed) { leftNativePinchIntent.Reset(); leftGrabIntent.Reset(); }
            if (rightClaimed) { rightNativePinchIntent.Reset(); rightGrabIntent.Reset(); }
            if (!leftClaimed && !rightClaimed) ResetGrabIntents();
            nativeWorldIntentSelected[0] = nativeWorldIntentSelected[1] = false;
            nativeWorldIntentReadFrame[0] = nativeWorldIntentReadFrame[1] = -1;
        }

        private bool ReadMenuHandRay(HandType hand, ref MenuAimSample sample, out Ray worldRay, out float pinchStrength, out bool pinchReady)
        {
            worldRay = default; pinchStrength = float.NaN; pinchReady = false;
            if (sample.frame != Time.frameCount)
            {
                sample = new MenuAimSample { frame = Time.frameCount };
                HandAimState aim = default;
                // Query at most once per requested hand/frame, including failed fetches.
                // PXR_Hand's cached properties can otherwise survive a failed SDK fetch.
                bool fetched = OceanHandTracking.TryGetAim(hand, out aim);
                bool aimValid = fetched && TryValidateMenuAim(aim, out sample.localRay);
                HandJointLocations locations = hand == HandType.HandLeft ? leftLocations : rightLocations;
                float distance = float.NaN, strength = float.NaN;
                Vector3 index = default;
                bool tipsValid = TryReadJoint(locations, (int)HandJoint.JointThumbTip, out Vector3 thumb) &&
                    TryReadJoint(locations, (int)HandJoint.JointIndexTip, out index);
                if (tipsValid)
                {
                    distance = Vector3.Distance(thumb, index);
                    tipsValid = TryGetMenuPinchStrength(distance, out strength);
                }
                sample.rayValid = aimValid;
                sample.valid = aimValid && tipsValid;
                sample.pinchStrength = strength;
                sample.diagnostics = new MenuHandRayDiagnostics(Time.frameCount, fetched, aimValid,
                    fetched ? aim.aimStatus : 0, fetched ? aim.touchStrengthRay : float.NaN,
                    tipsValid, distance, tipsValid ? strength : float.NaN, aim.Source);
            }
            if (!sample.rayValid) return false;
            // Convert at access time so a legitimate Origin yaw/translation this frame
            // is applied once, just like the palm and joint coordinate conversion.
            Vector3 origin = trackingSpace.TransformPoint(sample.localRay.origin);
            Vector3 direction = trackingSpace.rotation * sample.localRay.direction;
            float lengthSquared = direction.sqrMagnitude;
            if (!IsFiniteVector(origin) || !IsFiniteVector(direction) ||
                !IsFinite(lengthSquared) || lengthSquared < .000001f) return false;
            worldRay = new Ray(origin, direction / Mathf.Sqrt(lengthSquared));
            pinchReady = sample.valid;
            pinchStrength = pinchReady ? sample.pinchStrength : float.NaN;
            return true;
        }

        /// <summary>Read the input method's exact current-frame cache without another SDK query.</summary>
        public bool TryGetMenuHandRayDiagnostics(HandType hand, out MenuHandRayDiagnostics diagnostics)
        {
            diagnostics = default;
            if (hand != HandType.HandLeft && hand != HandType.HandRight) return false;
            MenuAimSample sample = hand == HandType.HandLeft ? leftMenuAim : rightMenuAim;
            if (sample.frame != Time.frameCount) return false;
            diagnostics = sample.diagnostics;
            return true;
        }

        /// <summary>
        /// Deterministic distance mapping in metres: 24 mm maps to the menu press
        /// threshold and 40 mm to release. The gate retains all temporal hysteresis.
        /// </summary>
        public static bool TryGetMenuPinchStrength(float tipDistanceMeters, out float strength)
        {
            strength = float.NaN;
            if (!IsFinite(tipDistanceMeters) || tipDistanceMeters < 0f || tipDistanceMeters > .30f) return false;
            float amountPerMeter = (OceanMenuPressGate.PressThreshold - OceanMenuPressGate.ReleaseThreshold) / (.040f - .024f);
            strength = Mathf.Clamp01(OceanMenuPressGate.PressThreshold + (.024f - tipDistanceMeters) * amountPerMeter);
            return true;
        }

        private static bool TryValidateMenuAim(HandAimState aim, out Ray localRay)
        {
            localRay = default;
            const HandAimStatus required = HandAimStatus.AimComputed | HandAimStatus.AimRayValid;
            if ((aim.aimStatus & required) != required || (aim.aimStatus & HandAimStatus.SystemGesture) != 0) return false;
            Vector3 position = aim.aimRayPose.position;
            Quaternion rotation = aim.aimRayPose.rotation;
            float normSquared = rotation.x * rotation.x + rotation.y * rotation.y +
                rotation.z * rotation.z + rotation.w * rotation.w;
            if (!IsFiniteVector(position) || !IsFinite(rotation.x) || !IsFinite(rotation.y) ||
                !IsFinite(rotation.z) || !IsFinite(rotation.w) ||
                !IsFinite(normSquared) || normSquared < .000001f) return false;
            float reciprocalNorm = 1f / Mathf.Sqrt(normSquared);
            rotation = new Quaternion(rotation.x * reciprocalNorm, rotation.y * reciprocalNorm,
                rotation.z * reciprocalNorm, rotation.w * reciprocalNorm);
            localRay = new Ray(position, rotation * Vector3.forward);
            return true;
        }

        private static bool IsFiniteVector(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        /// <summary>Find the nearest valid hand for this target, never the midpoint between hands.</summary>
        public bool TryGetClosestActiveHand(Vector3 targetWorldPosition, out Vector3 handWorldPosition, out float sqrDistance)
        {
            handWorldPosition = Vector3.zero;
            sqrDistance = float.PositiveInfinity;
            bool found = TryGetPalmPosition(HandType.HandLeft, out Vector3 leftPosition);
            if (found)
            {
                handWorldPosition = leftPosition;
                sqrDistance = (targetWorldPosition - leftPosition).sqrMagnitude;
            }
            if (TryGetPalmPosition(HandType.HandRight, out Vector3 rightPosition))
            {
                float rightDistance = (targetWorldPosition - rightPosition).sqrMagnitude;
                if (rightDistance < sqrDistance)
                {
                    handWorldPosition = rightPosition;
                    sqrDistance = rightDistance;
                }
                found = true;
            }
            return found;
        }

        // Compatibility only. Per-fish escape should use TryGetClosestActiveHand instead.
        public Vector3 GetAverageHandPos()
        {
            bool left = TryGetPalmPosition(HandType.HandLeft, out Vector3 lp);
            bool right = TryGetPalmPosition(HandType.HandRight, out Vector3 rp);
            return left && right ? (lp + rp) * 0.5f : left ? lp : right ? rp : head != null ? head.position : transform.position;
        }

        public bool IsAnyHandActive() => TryGetPalmPosition(HandType.HandLeft, out _) || TryGetPalmPosition(HandType.HandRight, out _);

        public void SetSeatedMode(bool enabled)
        {
            seatedMode = enabled;
            ResetStroke();
            swimVelocity = Vector3.zero;
        }

        private void ClearTrackingAndMotion()
        {
            OceanHandTracking.Invalidate();
            LeftHandActive = RightHandActive = false;
            leftPaddleTracked = rightPaddleTracked = false;
            leftPaddleLocal = rightPaddleLocal = Vector3.zero;
            LeftPalmPos = RightPalmPos = Vector3.zero;
            CurrentMode = HandMode.None;
            handSampleFrame = -1;
            leftMenuAim = rightMenuAim = new MenuAimSample { frame = -1 };
            menuLeftTargeted = menuRightTargeted = spearAimLeft = spearAimRight = false;
            menuLeftTargetFrame = menuRightTargetFrame = spearAimFrame = -1;
            swimVelocity = currentVelocity = Vector3.zero;
            snapTurnGate.Reset();
            leftGrasp.Reset();
            rightGrasp.Reset();
            ResetPaddleFistGuards();
            ResetGrabIntents();
            ResetNativePinchInput();
            ResetStroke();
            SetHandsVisible(false, false);
        }

        private void OnApplicationPause(bool paused)
        {
            appPaused = paused;
            ClearTrackingAndMotion();
        }

        private void OnApplicationFocus(bool focused)
        {
#if !UNITY_EDITOR
            // PICO system overlays can take focus without issuing a pause callback.
            // Both loss and return discard the old grasp; a fresh open pose must rearm it.
            appFocused = focused;
            ClearTrackingAndMotion();
#endif
        }

        private void OnDisable() => ClearTrackingAndMotion();

        private void OnDestroy()
        {
            if (meshOverlapProbe != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(meshOverlapProbe.gameObject); else
#endif
                Destroy(meshOverlapProbe.gameObject);
            }
            nativeVisionOSPinch.Dispose();
            if (ownedHandMaterial != null) Destroy(ownedHandMaterial);
            if (leftHandGO != null) Destroy(leftHandGO);
            if (rightHandGO != null) Destroy(rightHandGO);
        }
    }
}

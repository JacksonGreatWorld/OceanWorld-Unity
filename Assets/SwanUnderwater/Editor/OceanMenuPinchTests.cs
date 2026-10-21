using System;
using System.Reflection;
using HandType = SwanUnderwater.OceanHandSide;
using HandJoint = SwanUnderwater.OceanHandJointId;
using HandJointLocation = SwanUnderwater.OceanHandJoint;
using HandJointLocations = SwanUnderwater.OceanHandFrame;
using HandLocationStatus = SwanUnderwater.OceanHandLocationStatus;
using HandAimState = SwanUnderwater.OceanHandAim;
using HandAimStatus = SwanUnderwater.OceanHandAimStatus;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Regression for a valid hover ray whose legacy touch channel remains zero.</summary>
    public static class OceanMenuPinchTests
    {
        public static void RunAll()
        {
            int assertions = 0;
            Action<bool, string> check = (condition, description) =>
            {
                if (!condition) throw new InvalidOperationException("OceanMenuPinchTests: " + description);
                assertions++;
            };
            var aim = new HandAimState
            {
                aimStatus = HandAimStatus.AimComputed | HandAimStatus.AimRayValid,
                aimRayPose = new Pose(new Vector3(.2f, 1.2f, .4f), Quaternion.identity),
                touchStrengthRay = 0f
            };
            #if ENABLE_PICO_XR_SDK && !OCEAN_XR_HANDS_ONLY
            // Preserve the original PICO native-Z regression through the real adapter.
            aim = OceanPicoHandTracking.ConvertAim(new ByteDance.PICO.XR.HandAimState {
                aimStatus=ByteDance.PICO.XR.HandAimStatus.AimComputed|ByteDance.PICO.XR.HandAimStatus.AimRayValid,
                aimRayPose=new ByteDance.PICO.XR.Posef(new Vector3(.2f,1.2f,-.4f),Quaternion.identity),touchStrengthRay=0f });
#endif
            // Reflect only our production pose validator; never SDK internals.
            MethodInfo validate = typeof(HandSwimController).GetMethod("TryValidateMenuAim", BindingFlags.NonPublic | BindingFlags.Static);
            check(validate != null, "production aim validator is present");
            object[] arguments = { aim, null };
            check((bool)validate.Invoke(null, arguments), "legacy touch zero preserves the valid native hover pose");
            check(Math.Abs(((Ray)arguments[1]).origin.z - .4f) < .00001f, "tracking-local Unity Z remains intact (PICO builds also exercise the raw SDK adapter)");
            check(HandSwimController.TryGetMenuPinchStrength(.060f, out float open), "60mm tips are valid");
            check(HandSwimController.TryGetMenuPinchStrength(.020f, out float closed), "20mm tips are valid");
            check(open <= OceanMenuPressGate.ReleaseThreshold && closed >= OceanMenuPressGate.PressThreshold,
                "actual 60mm/20mm geometry crosses both gate thresholds despite legacy touch zero");
            check(HandSwimController.TryGetMenuPinchStrength(.040f, out float boundaryOpen) &&
                boundaryOpen <= OceanMenuPressGate.ReleaseThreshold, "40mm releases at the boundary");
            check(HandSwimController.TryGetMenuPinchStrength(.024f, out float boundaryClosed) &&
                boundaryClosed >= OceanMenuPressGate.PressThreshold, "24mm presses at the boundary");
            var gate = new OceanMenuPressGate();
            check(gate.Step(1, .05f, true, closed, 7, true) == -1, "initial closed hand cannot click");
            check(gate.Step(2, .05f, true, closed, 7, true) == -1 && !gate.Armed, "holding an initial pinch does not arm");
            check(gate.Step(3, .05f, true, open, 7, true) == -1, "released hover first half cannot click");
            check(gate.Step(4, .05f, true, open, 7, true) == -1 && gate.Armed, "released hover arms after 100ms");
            check(gate.Step(5, .02f, true, closed, 7, true) == 7, "60mm to 20mm selects the hovered target");
            check(gate.Step(6, .02f, true, closed, 7, true) == -1, "held pinch cannot repeat");
            check(gate.Step(7, .02f, false, closed, 7, true) == -1 && !gate.Armed, "tracking loss discards qualification");
            check(gate.Step(8, .02f, true, closed, 7, true) == -1, "closed hand after recovery cannot click");
            check(gate.Step(9, .05f, true, open, 7, true) == -1, "recovery release first half");
            check(gate.Step(10, .05f, true, open, 7, true) == -1 && gate.Armed, "recovery requires a fresh released hover");
            check(gate.Step(11, .02f, true, closed, 7, true) == 7, "fresh post-recovery pinch works");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -.001f, .31f })
                check(!HandSwimController.TryGetMenuPinchStrength(invalid, out float value) && float.IsNaN(value),
                    "invalid distance is unavailable, not a fabricated release");
            string result = "OceanMenuPinchTests PASS: " + assertions + " assertions; legacy-zero native ray, actual tip distances and production press gate. No live tracking claim.";
#if UNITY_EDITOR
            Debug.Log(result);
#else
            Console.WriteLine(result);
#endif
        }
    }
}

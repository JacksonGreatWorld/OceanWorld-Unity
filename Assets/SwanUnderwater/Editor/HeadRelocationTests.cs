using System;
using System.Reflection;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    // Editor transform test: uses the production relocation method and a synthetic
    // tracked hierarchy, without invoking SDK tracking or pretending to pick up items.
    public static class HeadRelocationTests
    {
        static int assertions;
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Set(HandSwimController controller, string name, object value) =>
            typeof(HandSwimController).GetField(name, Private).SetValue(controller, value);
        static T Get<T>(HandSwimController controller, string name) =>
            (T)typeof(HandSwimController).GetField(name, Private).GetValue(controller);
        static void Require(bool value, string reason)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("Head relocation: " + reason);
        }
        static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < .000001f;

        public static void RunAll()
        {
            assertions = 0;
            var rig = new GameObject("Relocation test rig");
            try
            {
                rig.transform.SetPositionAndRotation(new Vector3(2f, -.5f, -3f), Quaternion.Euler(0f, 37f, 0f));
                var offset = new GameObject("Camera Offset").transform;
                offset.SetParent(rig.transform, false);
                offset.localPosition = new Vector3(.18f, .07f, -.05f);
                var head = new GameObject("Synthetic tracked head").transform;
                head.SetParent(offset, false);
                head.localPosition = new Vector3(.21f, 1.67f, -.12f);
                head.localRotation = Quaternion.Euler(24f, 17f, -7f);
                var controller = rig.AddComponent<HandSwimController>();
                Set(controller, "xrOrigin", rig.transform);
                Set(controller, "head", head);
                Set(controller, "trackingSpace", offset);
                Set(controller, "swimVelocity", Vector3.forward * 2f);
                Set(controller, "currentVelocity", Vector3.right);
                Set(controller, "pendingHandTurn", 10f);
                Set(controller, "leftPalmLocal", new Vector3(-.25f, 1.2f, .45f));
                Set(controller, "rightPalmLocal", new Vector3(.25f, 1.2f, .45f));
                Set(controller, "<LeftHandActive>k__BackingField", true);
                Set(controller, "<RightHandActive>k__BackingField", true);
                Vector3 localPosition = head.localPosition, oldOffset = offset.localPosition;
                Quaternion localRotation = head.localRotation;
                float oldForwardY = head.forward.y, oldUpY = head.up.y;
                Vector3 destination = new Vector3(11f, -1f, 4f), heading = new Vector3(-1f, 0f, 2f);

                Require(controller.TryRelocateHead(destination, heading), "initialized rig relocates");
                Require(Near(head.position, destination), "actual tracked eye reaches requested world point");
                Require(Vector3.Angle(Vector3.ProjectOnPlane(head.forward, Vector3.up), heading) < .01f,
                    "horizontal view heading aligns using only rig yaw");
                Require(Near(head.localPosition, localPosition) && Quaternion.Angle(head.localRotation, localRotation) < .01f,
                    "physical head local tracking pose remains unchanged");
                Require(Near(offset.localPosition, oldOffset), "floor/camera offset remains unchanged");
                Require(Mathf.Abs(head.forward.y - oldForwardY) < .00001f && Mathf.Abs(head.up.y - oldUpY) < .00001f,
                    "pitch and roll relative to world up are retained");
                Require(controller.CurrentSpeed == 0f && Get<Vector3>(controller, "swimVelocity") == Vector3.zero,
                    "both locomotion velocity banks clear");
                Require(Get<float>(controller, "pendingHandTurn") == 0f, "old delayed steering clears");
                Require(Near(controller.LeftPalmPos, offset.TransformPoint(Get<Vector3>(controller, "leftPalmLocal"))) &&
                    Near(controller.RightPalmPos, offset.TransformPoint(Get<Vector3>(controller, "rightPalmLocal"))),
                    "both palms refresh through the moved tracking space");
                Set(controller, "handSampleFrame", Time.frameCount);
                Require(!(bool)typeof(HandSwimController).GetProperty("GraspSampleFresh", Private).GetValue(controller),
                    "even a later tracking refresh cannot authorize a grasp in this frame");
                Vector3 acceptedPosition = rig.transform.position;
                Quaternion acceptedRotation = rig.transform.rotation;
                Require(!controller.TryRelocateHead(destination, Vector3.zero), "undefined target heading rejected");
                Require(!controller.TryRelocateHead(new Vector3(float.NaN, 0f, 0f), heading), "non-finite target rejected");
                Require(Near(rig.transform.position, acceptedPosition) && Quaternion.Angle(rig.transform.rotation, acceptedRotation) < .01f,
                    "invalid requests leave accepted rig pose unchanged");
                Set(controller, "head", null);
                Require(!controller.TryRelocateHead(destination, heading), "missing initialized tracked head rejected");
                Debug.Log("[HeadRelocationTests] PASS " + assertions + " assertions; synthetic hierarchy only.");
            }
            finally { UnityEngine.Object.DestroyImmediate(rig); }
        }
    }
}

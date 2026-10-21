#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Real model/bone updates on temporary controllers with synthetic poses; no wearer or propulsion claim.</summary>
    public static class DiverHandVisualsTests
    {
        public static int Assertions { get; private set; }
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const int Frame = 123;
        const OceanHandLocationStatus PoseValid = OceanHandLocationStatus.PositionValid | OceanHandLocationStatus.OrientationValid;

        static void Check(bool value, string why)
        {
            Assertions++;
            if (!value) throw new InvalidOperationException("DiverHandVisualsTests: " + why);
        }
        static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
        static object Call(DiverHandVisuals visuals, string method, params object[] args) =>
            typeof(DiverHandVisuals).GetMethod(method, Private, null, Array.ConvertAll(args, a => a?.GetType() ?? typeof(HandSwimController)), null)
                .Invoke(visuals, args);

        public static void RunAll()
        {
            Assertions = 0;
            foreach (bool visionOS in new[] { true, false })
            {
                using (var f = new Fixture())
                {
#if UNITY_VISIONOS
                    Check(Get<bool>(f.Visuals, "visionOSVisuals"), "visionOS visual policy exists before Start");
#else
                    Check(!Get<bool>(f.Visuals, "visionOSVisuals"), "other targets retain their original visual policy");
#endif
                    f.Initialize(visionOS);
                    Check(Get<bool>(f.Visuals, "initialized") && f.Space.childCount == 2, "both platforms instantiate Unity hand models: " + visionOS);
                    Check(Get<Transform[]>(f.Visuals, "leftBones").Length == 26 && Get<Transform[]>(f.Visuals, "rightBones").Length == 26,
                        "both 26-joint model mappings remain available");
                    Check(Get<Material>(f.Visuals, "material") != null && !f.LeftFallback.activeSelf && !f.RightFallback.activeSelf,
                        "Unity meshes own hand display without joint spheres");
                    Get<GameObject>(f.Visuals, "left").SetActive(true);
                    Get<GameObject>(f.Visuals, "right").SetActive(true);
                    Call(f.Visuals, "OnDisable");
                    Check(f.LeftFallback.activeSelf == !visionOS && f.RightFallback.activeSelf == !visionOS,
                        "disable preserves AVP sphere suppression and other platforms' fallback recovery");
                    Check(!Get<GameObject>(f.Visuals, "left").activeSelf && !Get<GameObject>(f.Visuals, "right").activeSelf,
                        "disable hides the custom models");
                }
            }
            foreach (bool left in new[] { true, false }) CheckVisualPoses(left);
            MissingDependencies();
            Debug.Log("DiverHandVisualsTests PASS: " + Assertions +
                " assertions; restored Unity meshes, estimated visual poses, partial joints, exact transformed bones/skin, stale/invalid wrist, unchanged input flags and lifecycle. AVP hand shape and propulsion unverified.");
        }

        static void CheckVisualPoses(bool left)
        {
            using (var f = new Fixture())
            {
                f.Initialize(true);
                var model = Get<GameObject>(f.Visuals, left ? "left" : "right");
                var bones = Get<Transform[]>(f.Visuals, left ? "leftBones" : "rightBones");
                var frame = OceanHandFrame.Allocate();frame.isActive = 1;frame.SampleFrame = Frame;
                var bindPalm = new Pose(bones[0].localPosition, bones[0].localRotation);
                frame.jointCount = 2;
                frame.jointLocations[1] = new OceanHandJoint { locationStatus = PoseValid,
                    pose = new Pose(f.Space.InverseTransformPoint(bones[1].position), Quaternion.Inverse(f.Space.rotation) * bones[1].rotation) };
                Check(f.Apply(model, bones, frame) && model.activeSelf, "first wrist-only estimated frame can display the import bind hand");
                Check(Vector3.Distance(bones[0].localPosition, bindPalm.position) < .00001f && Quaternion.Angle(bones[0].localRotation, bindPalm.rotation) < .05f,
                    "first missing Palm retains the import bind pose rather than an identity pose");
                frame.jointCount = 26;
                for (int i = 0; i < 26; i++)
                    frame.jointLocations[i] = new OceanHandJoint { locationStatus = PoseValid,
                        pose = new Pose(f.Space.InverseTransformPoint(bones[i].position), Quaternion.Inverse(f.Space.rotation) * bones[i].rotation) };
                var bindVertices = BakeInSpace(model, f.Space);
                var bindUncompensatedVertices = BakeInSpace(model, f.Space, false);
                var renderer = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Debug.Log("DiverHandVisualsTests skin baseline " + (left ? "left" : "right") +
                    " renderer localScale=" + renderer.transform.localScale.ToString("G9") +
                    " lossyScale=" + renderer.transform.lossyScale.ToString("G9") +
                    " rendererToWorld=" + renderer.transform.localToWorldMatrix.ToString("G9") +
                    " worldToSpace=" + f.Space.worldToLocalMatrix.ToString("G9"));

                // Round-trip actual FBX bind poses after disturbing bones and moving the tracking origin.
                for (int i = 0; i < 26; i++)
                { bones[i].localPosition += new Vector3(.003f, -.002f, .001f);bones[i].localRotation *= Quaternion.Euler(3, -2, 1); }
                f.Space.SetPositionAndRotation(new Vector3(1.7f, .8f, -.9f), Quaternion.Euler(17, 39, -8));
                f.Space.localScale = Vector3.one * 1.2f;
                Check(f.Apply(model, bones, frame), "valid estimated joints render without tracked flags: " + left);
                float positionError = 0, rotationError = 0;
                for (int i = 0; i < 26; i++)
                {
                    positionError = Mathf.Max(positionError, Vector3.Distance(bones[i].position, f.Space.TransformPoint(frame.jointLocations[i].pose.position)));
                    rotationError = Mathf.Max(rotationError, Quaternion.Angle(bones[i].rotation, f.Space.rotation * frame.jointLocations[i].pose.rotation));
                }
                Check(positionError < .00005f && rotationError < .05f, "all valid bones match tracking-space poses under translation/rotation/scale");
                var restoredVertices = BakeInSpace(model, f.Space);
                var restoredUncompensatedVertices = BakeInSpace(model, f.Space, false);
                Debug.Log("DiverHandVisualsTests skin restored " + (left ? "left" : "right") +
                    " bonePositionError=" + positionError.ToString("G9") + " boneRotationError=" + rotationError.ToString("G9") +
                    " renderer localScale=" + renderer.transform.localScale.ToString("G9") +
                    " lossyScale=" + renderer.transform.lossyScale.ToString("G9") +
                    " rendererToWorld=" + renderer.transform.localToWorldMatrix.ToString("G9") +
                    " worldToSpace=" + f.Space.worldToLocalMatrix.ToString("G9"));
                LogSkinDifference(left, "scale=1.2 useScale=false", bindUncompensatedVertices, restoredUncompensatedVertices);
                LogSkinDifference(left, "scale=1.2 useScale=true", bindVertices, restoredVertices);
                // Isolate BakeMesh scaling from bone restoration, then restore the original fixture state.
                f.Space.localScale = Vector3.one;
                var unitVertices = BakeInSpace(model, f.Space);
                LogSkinDifference(left, "scale=1 useScale=false", bindUncompensatedVertices, BakeInSpace(model, f.Space, false));
                LogSkinDifference(left, "scale=1 useScale=true", bindVertices, unitVertices);
                f.Space.localScale = Vector3.one * 1.2f;
                float unitVertexError = 0;
                if (unitVertices.Length == bindVertices.Length)
                    for (int i = 0; i < unitVertices.Length; i++) unitVertexError = Mathf.Max(unitVertexError, Vector3.Distance(unitVertices[i], bindVertices[i]));
                Check(unitVertices.Length > 0 && unitVertices.Length == bindVertices.Length && unitVertexError < .00005f,
                    "actual skinned geometry also round-trips at unit tracking scale");
                float vertexError = 0;
                if (restoredVertices.Length == bindVertices.Length)
                    for (int i = 0; i < restoredVertices.Length; i++) vertexError = Mathf.Max(vertexError, Vector3.Distance(restoredVertices[i], bindVertices[i]));
                Check(restoredVertices.Length > 0 && restoredVertices.Length == bindVertices.Length && vertexError < .00005f,
                    "actual skinned geometry round-trips without a mirror, axis offset or distorted bind shape");
                Check(Array.TrueForAll(frame.jointLocations, j => j.locationStatus == PoseValid), "rendering does not fabricate tracked input flags");

                int tip = (int)OceanHandJointId.JointIndexTip;
                var priorTip = new Pose(bones[tip].localPosition, bones[tip].localRotation);
                frame.jointLocations[0] = default;frame.jointLocations[tip] = default;
                frame.jointLocations[1].pose.position += new Vector3(.02f, .01f, -.01f);
                Check(f.Apply(model, bones, frame) && model.activeSelf, "missing Palm and fingertip do not hide the whole AVP hand");
                Check(Vector3.Distance(bones[tip].localPosition, priorTip.position) < .00001f && Quaternion.Angle(bones[tip].localRotation, priorTip.rotation) < .05f,
                    "missing fingertip retains a local pose and follows its parent");

                int intermediate = (int)OceanHandJointId.JointIndexIntermediate;
                int distal = (int)OceanHandJointId.JointIndexDistal;
                var priorIntermediate = new Pose(bones[intermediate].localPosition, bones[intermediate].localRotation);
                var parent = bones[distal].parent;
                frame.jointLocations[intermediate] = default;
                frame.jointLocations[distal].pose.position += new Vector3(.004f, -.005f, .003f);
                Check(f.Apply(model, bones, frame), "one missing intermediate joint does not reject the hand");
                Check(Vector3.Distance(bones[intermediate].localPosition, priorIntermediate.position) < .00001f &&
                    Quaternion.Angle(bones[intermediate].localRotation, priorIntermediate.rotation) < .05f, "missing intermediate retains its own local pose");
                Check(bones[distal].parent == parent && Vector3.Distance(bones[distal].position, f.Space.TransformPoint(frame.jointLocations[distal].pose.position)) < .00005f,
                    "valid descendant is exact without being rebased against a different ancestor");

                frame.jointLocations[distal].pose.position = new Vector3(float.NaN, 0, 0);
                Check(f.Apply(model, bones, frame) && Finite(bones[distal].position), "nonfinite finger pose is skipped without corrupting or hiding the hand");
                frame.jointLocations[distal].pose = new Pose(Vector3.zero, new Quaternion(0, 0, 0, 0));
                Check(f.Apply(model, bones, frame) && Finite(bones[distal].position), "zero finger quaternion cannot corrupt the displayed skeleton");
                var priorDistal = new Pose(bones[distal].position, bones[distal].rotation);
                frame.jointLocations[distal].pose.rotation = new Quaternion(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue);
                Check(f.Apply(model, bones, frame) && Vector3.Distance(bones[distal].position, priorDistal.position) < .00001f &&
                    Quaternion.Angle(bones[distal].rotation, priorDistal.rotation) < .05f, "finite components with an overflowed quaternion norm are skipped");
                frame.SampleFrame = Frame - 1;
                Check(!f.Apply(model, bones, frame) && !model.activeSelf, "stale hand data hides the model");
                frame.SampleFrame = Frame;frame.jointLocations[1] = default;
                Check(!f.Apply(model, bones, frame) && !model.activeSelf, "missing wrist hides the model");
                frame.jointLocations[1] = new OceanHandJoint { locationStatus = PoseValid, pose = new Pose(new Vector3(float.NaN, 0, 0), Quaternion.identity) };
                Check(!f.Apply(model, bones, frame), "nonfinite wrist cannot anchor a virtual hand");
                frame.jointLocations[1].pose = Pose.identity;frame.isActive = 0;
                Check(!f.Apply(model, bones, frame), "inactive hand cannot retain an old visible mesh");
                frame.isActive = 1;Set(f.Visuals, "visionOSVisuals", false);
                Check(!f.Apply(model, bones, frame), "other platforms retain the original complete/tracked-root gate");
            }
        }

        static void LogSkinDifference(bool left, string measurement, Vector3[] expected, Vector3[] actual)
        {
            float maxError = 0;
            int worst = 0;
            for (int i = 0; i < Mathf.Min(expected.Length, actual.Length); i++)
            {
                float error = Vector3.Distance(expected[i], actual[i]);
                if (error > maxError) { maxError = error;worst = i; }
            }
            string vertices = "";
            if (expected.Length > 0 && actual.Length > 0)
                foreach (int i in new[] { 0, Mathf.Min(expected.Length, actual.Length) / 2, worst })
                    vertices += " vertex[" + i + "] expected=" + expected[i].ToString("G9") +
                        " actual=" + actual[i].ToString("G9") +
                        " lengthRatio=" + (actual[i].magnitude / Mathf.Max(expected[i].magnitude, 1e-10f)).ToString("G9");
            Debug.Log("DiverHandVisualsTests skin " + (left ? "left" : "right") + " " + measurement +
                " vertexCount=" + expected.Length + "/" + actual.Length + " maxError=" + maxError.ToString("G9") +
                " worstIndex=" + worst + vertices);
        }

        static Vector3[] BakeInSpace(GameObject model, Transform space, bool useScale = true)
        {
            var renderer = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var mesh = new Mesh();
            try
            {
                // Compensate inherited renderer scale; the matrix below applies it exactly once.
                renderer.BakeMesh(mesh, useScale);var vertices = mesh.vertices;
                var toSpace = space.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = toSpace.MultiplyPoint3x4(vertices[i]);
                return vertices;
            }
            finally { Object.DestroyImmediate(mesh); }
        }
        static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) &&
            !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);
        static void MissingDependencies()
        {
            foreach (bool visionOS in new[] { true, false })
                using (var f = new Fixture())
                {
                    Call(f.Visuals, "InitializeHandVisuals", visionOS, null);Call(f.Visuals, "OnDisable");
                    Check(f.Space.childCount == 0, "missing controller is safe on init/disable: " + visionOS);
                }
            using (var f = new Fixture())
            {
                f.Hands.SetCustomHandVisualsActive(true);Set(f.Hands, "trackingSpace", null);
                f.Initialize(true);Call(f.Visuals, "OnDisable");
                Check(f.Space.childCount == 0 && !f.LeftFallback.activeSelf && !f.RightFallback.activeSelf,
                    "missing tracking space cannot create art or reopen AVP fallback");
            }
        }

        sealed class Fixture : IDisposable
        {
            readonly GameObject root;
            public readonly HandSwimController Hands;
            public readonly DiverHandVisuals Visuals;
            public readonly Transform Space;
            public readonly GameObject LeftFallback, RightFallback;

            public Fixture()
            {
                root = new GameObject("DiverHandVisuals regression");root.SetActive(false);
                Hands = root.AddComponent<HandSwimController>();Visuals = root.AddComponent<DiverHandVisuals>();
                Space = new GameObject("Tracking space").transform;Space.SetParent(root.transform, false);
                LeftFallback = new GameObject("Left joint fallback");RightFallback = new GameObject("Right joint fallback");
                LeftFallback.transform.SetParent(root.transform, false);RightFallback.transform.SetParent(root.transform, false);
                Set(Hands, "trackingSpace", Space);Set(Hands, "leftHandGO", LeftFallback);Set(Hands, "rightHandGO", RightFallback);
                typeof(HandSwimController).GetProperty("LeftHandActive").SetValue(Hands, true);
                typeof(HandSwimController).GetProperty("RightHandActive").SetValue(Hands, true);
            }
            public void Initialize(bool visionOS) => Call(Visuals, "InitializeHandVisuals", visionOS, Hands);
            public bool Apply(GameObject model, Transform[] bones, OceanHandFrame frame) =>
                (bool)Call(Visuals, "ApplyVisualFrame", model, bones, frame, Frame);
            public void Dispose()
            {
                foreach (string field in new[] { "left", "right", "material" })
                {
                    var owned = Get<Object>(Visuals, field);Set(Visuals, field, null);
                    if (owned != null) Object.DestroyImmediate(owned);
                }
                Set(Hands, "leftHandGO", null);Set(Hands, "rightHandGO", null);Object.DestroyImmediate(root);
            }
        }
    }
}
#endif

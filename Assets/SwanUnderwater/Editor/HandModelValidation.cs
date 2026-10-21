#if ENABLE_PICO_XR_SDK && !OCEAN_XR_HANDS_ONLY
using System;
using System.Collections.Generic;
using System.IO;
using ByteDance.PICO.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Imported-asset and coordinate-conversion validation, not live tracking evidence.</summary>
    public static class HandModelValidation
    {
        private const float PositionTolerance = 0.0002f;
        private const float RotationToleranceDegrees = 0.1f;

        // Separate Unity process, graphics enabled. No Play Mode or device is used. This entry owns
        // batch exit: -executeMethod SwanUnderwater.Editor.HandModelValidation.Run (omit -nographics).
        [MenuItem("Swan/Codex/Validate and capture imported hand models")]
        public static void Run()
        {
            string output = Path.GetFullPath(Argument("-handValidationOutput",
                Path.Combine("Validation", "iteration-2", "hand-model", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"))));
            Directory.CreateDirectory(output);
            var report = new Summary { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                scope = "Editor imported FBX bind-pose and PXR conversion round-trip. No device, tracking, comfort or dynamic pose validation.",
                output = output };
            Scene preview = default;
            Material material = null;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Hand asset validation requires Edit Mode.");
                var displays = new List<XRDisplaySubsystem>();
                SubsystemManager.GetSubsystems(displays);
                if (XRSettings.isDeviceActive || displays.Exists(display => display.running))
                    throw new InvalidOperationException("Hand asset validation refuses active XR.");
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Graphics are required for hand screenshots; omit -nographics.");

                preview = EditorSceneManager.NewPreviewScene();
                var tracking = new GameObject("Validation tracking space");
                SceneManager.MoveGameObjectToScene(tracking, preview);
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("The existing URP Lit shader is missing.");
                material = new Material(shader) { name = "Hand validation slate glove" };
                material.SetColor("_BaseColor", new Color(0.10f, 0.24f, 0.27f));
                material.SetFloat("_Smoothness", 0.32f);
                material.SetFloat("_Metallic", 0.05f);

                HandState left = InspectHand(true, preview, tracking.transform, material, report);
                HandState right = InspectHand(false, preview, tracking.transform, material, report);
                report.hands = new[] { left.report, right.report };
                CheckMirror(left, right, report);
                NormalizeForPreview(left, -0.16f);
                NormalizeForPreview(right, 0.16f);
                Capture(preview, left.model, right.model, output, report);
                report.passed = report.errors.Count == 0 && report.captures.Count == 3;
            }
            catch (Exception e)
            {
                report.errors.Add(e.ToString());
                report.passed = false;
            }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (material != null) Object.DestroyImmediate(material);
                File.WriteAllText(Path.Combine(output, "summary.json"), JsonUtility.ToJson(report, true));
                File.WriteAllText(Path.Combine(output, "scope.txt"),
                    "Static imported Unity XR Hands sample meshes. No captured or simulated user hand tracking.\n" +
                    "Images preserve the imported bind pose; both wrists are aligned to a common orientation for comparison.\n" +
                    "view-positive-y and view-negative-y show opposite sides; the latter reverses screen left/right.\n" +
                    "The summary records the source L_/R_ names, geometry, bindings, X-mirror check and nonzero TrackingSpace round-trip.\n" +
                    "A pass proves asset/coordinate consistency only; a real PXR fist/open-hand and tracking recovery check remains required.\n");
            }
            Debug.Log("CODEX_HAND_MODEL_VALIDATION: " + (report.passed ? "PASS " : "FAIL ") + output);
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
            else if (!report.passed) throw new InvalidOperationException(string.Join("\n", report.errors));
        }

        private static HandState InspectHand(bool leftSide, Scene preview, Transform tracking, Material material, Summary summary)
        {
            string side = leftSide ? "Left" : "Right";
            string assetPath = "Assets/Resources/DiverHands/" + side + "Hand.fbx";
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null) throw new FileNotFoundException("Imported hand FBX missing", assetPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
            model.transform.SetParent(tracking, false);
            var bones = DiverHandVisuals.MapBones(model, leftSide);
            var data = new HandReport { side = side, asset = assetPath, mappedJoints = bones.Length,
                importerOptimizesHierarchy = (AssetImporter.GetAtPath(assetPath) as ModelImporter)?.optimizeGameObjects ?? false };
            var state = new HandState { model = model, bones = bones, report = data, wristLocal = new Vector3[26] };
            var allTransforms = model.GetComponentsInChildren<Transform>(true);
            var unique = new HashSet<Transform>();
            string prefix = leftSide ? "L_" : "R_";
            var jointData = new List<JointReport>();
            Quaternion inverseWrist = Quaternion.Inverse(bones[1].rotation);
            for (int i = 0; i < 26; i++)
            {
                string expected = prefix + DiverHandVisuals.JointNames[i];
                int occurrences = 0;
                foreach (Transform bone in allTransforms) if (bone.name == expected) occurrences++;
                bool nameOk = bones[i].name == expected && occurrences == 1 && unique.Add(bones[i]);
                bool enumOk = Enum.GetName(typeof(HandJoint), i) == "Joint" + DiverHandVisuals.JointNames[i];
                int parent = ParentIndex(i);
                bool hierarchyOk = parent < 0 || bones[i].parent == bones[parent];
                if (!nameOk || !enumOk || !hierarchyOk)
                    summary.errors.Add(side + " joint " + i + " mapping/hierarchy mismatch: " + expected);
                state.wristLocal[i] = inverseWrist * (bones[i].position - bones[1].position);
                jointData.Add(new JointReport { index = i, name = bones[i].name,
                    parent = bones[i].parent != null ? bones[i].parent.name : "",
                    occurrences = occurrences, mappingMatchesPico = enumOk && nameOk,
                    hierarchyMatches = hierarchyOk, wristLocalPosition = state.wristLocal[i] });
            }
            data.joints = jointData.ToArray();
            data.uniqueJointCount = unique.Count;

            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException(side + " has no skinned mesh renderer.");
            var meshes = new List<MeshReport>();
            var bakedBefore = new List<Vector3[]>();
            foreach (var renderer in renderers)
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null) throw new InvalidOperationException(side + " renderer has no mesh.");
                var materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.updateWhenOffscreen = true;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                long triangles = 0;
                bool allTriangles = true;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    if (mesh.GetTopology(i) == MeshTopology.Triangles) triangles += mesh.GetIndexCount(i) / 3;
                    else allTriangles = false;
                }
                var bindings = mesh.bindposes;
                float bindError = 0;
                bool bindBonesValid = bindings.Length == renderer.bones.Length;
                for (int i = 0; i < bindings.Length && i < renderer.bones.Length; i++)
                {
                    Transform bone = renderer.bones[i];
                    if (bone == null || !unique.Contains(bone)) { bindBonesValid = false; continue; }
                    Matrix4x4 skin = renderer.transform.worldToLocalMatrix * bone.localToWorldMatrix * bindings[i];
                    bindError = Mathf.Max(bindError, IdentityError(skin));
                }
                if (!bindBonesValid || bindError > 0.002f || !allTriangles)
                    summary.errors.Add(side + " mesh binding/topology check failed: " + mesh.name + " error=" + bindError);
                data.vertices += mesh.vertexCount;
                data.triangles += triangles;
                meshes.Add(new MeshReport { name = mesh.name, vertices = mesh.vertexCount, triangles = triangles,
                    submeshes = mesh.subMeshCount, rendererBones = renderer.bones.Length, bindposes = bindings.Length,
                    bindBonesMapped = bindBonesValid, bindIdentityMaxError = bindError, allTriangles = allTriangles });
                bakedBefore.Add(BakeInTrackingSpace(renderer, tracking));
            }
            data.meshes = meshes.ToArray();

            // Independent source is the imported bind pose. Encode it into raw PXR coordinates,
            // perturb the hierarchy, and replay through the same public SDK conversion functions.
            var localPoses = new Posef[26];
            for (int i = 0; i < 26; i++)
            {
                Vector3 p = tracking.InverseTransformPoint(bones[i].position);
                Quaternion q = Quaternion.Inverse(tracking.rotation) * bones[i].rotation;
                localPoses[i] = new Posef { Position = new Vector3f { x = p.x, y = p.y, z = -p.z },
                    Orientation = new Quatf { x = q.x, y = q.y, z = -q.z, w = -q.w } };
            }
            // Capture every desired pose before changing any parent of another joint.
            for (int i = 0; i < 26; i++)
            {
                bones[i].localPosition += new Vector3(0.003f, -0.002f, 0.001f);
                bones[i].localRotation *= Quaternion.Euler(3, -2, 1);
            }
            tracking.SetPositionAndRotation(new Vector3(1.7f, 0.8f, -0.9f), Quaternion.Euler(17, 39, -8));
            ApplyPose(bones[1], localPoses[1], tracking);
            for (int i = 0; i < 26; i++) if (i != 1) ApplyPose(bones[i], localPoses[i], tracking);
            for (int i = 0; i < 26; i++)
            {
                Vector3 expectedPosition = tracking.TransformPoint(localPoses[i].Position.ToVector3());
                Quaternion expectedRotation = tracking.rotation * localPoses[i].Orientation.ToQuat();
                data.roundTripJointMaxMeters = Mathf.Max(data.roundTripJointMaxMeters, Vector3.Distance(expectedPosition, bones[i].position));
                data.roundTripRotationMaxDegrees = Mathf.Max(data.roundTripRotationMaxDegrees, Quaternion.Angle(expectedRotation, bones[i].rotation));
            }
            for (int i = 0; i < renderers.Length; i++)
            {
                Vector3[] after = BakeInTrackingSpace(renderers[i], tracking);
                if (after.Length != bakedBefore[i].Length) throw new InvalidOperationException("Baked topology changed during round-trip.");
                for (int j = 0; j < after.Length; j++)
                    data.roundTripVertexMaxMeters = Mathf.Max(data.roundTripVertexMaxMeters, Vector3.Distance(bakedBefore[i][j], after[j]));
            }
            data.roundTripPassed = data.roundTripJointMaxMeters < PositionTolerance &&
                data.roundTripVertexMaxMeters < PositionTolerance && data.roundTripRotationMaxDegrees < RotationToleranceDegrees;
            if (!data.roundTripPassed) summary.errors.Add(side + " PXR bind-pose round-trip exceeded tolerance.");
            tracking.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return state;
        }

        private static int ParentIndex(int joint)
        {
            if (joint == 1) return -1;
            if (joint == 0 || joint == 2 || joint == 6 || joint == 11 || joint == 16 || joint == 21) return 1;
            return joint - 1;
        }

        private static void ApplyPose(Transform bone, Posef raw, Transform tracking) =>
            bone.SetPositionAndRotation(tracking.TransformPoint(raw.Position.ToVector3()), tracking.rotation * raw.Orientation.ToQuat());

        private static float IdentityError(Matrix4x4 matrix)
        {
            float error = 0;
            for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
                error = Mathf.Max(error, Mathf.Abs(matrix[row, column] - (row == column ? 1 : 0)));
            return error;
        }

        private static Vector3[] BakeInTrackingSpace(SkinnedMeshRenderer renderer, Transform tracking)
        {
            var mesh = new Mesh();
            try
            {
                renderer.BakeMesh(mesh);
                Vector3[] vertices = mesh.vertices;
                Matrix4x4 toTracking = tracking.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = toTracking.MultiplyPoint3x4(vertices[i]);
                return vertices;
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        private static void CheckMirror(HandState left, HandState right, Summary report)
        {
            double directSquared = 0, reflectedSquared = 0;
            for (int i = 0; i < 26; i++)
            {
                Vector3 p = left.wristLocal[i];
                directSquared += (p - right.wristLocal[i]).sqrMagnitude;
                reflectedSquared += (new Vector3(-p.x, p.y, p.z) - right.wristLocal[i]).sqrMagnitude;
            }
            report.directWristJointRmsMeters = Math.Sqrt(directSquared / 26);
            report.reflectedXWristJointRmsMeters = Math.Sqrt(reflectedSquared / 26);
            report.leftThumbWristX = left.wristLocal[5].x;
            report.rightThumbWristX = right.wristLocal[5].x;
            report.oppositeThumbSides = report.leftThumbWristX * report.rightThumbWristX < 0;
            report.leftRightMirrorPassed = report.oppositeThumbSides && report.reflectedXWristJointRmsMeters < 0.005 &&
                report.reflectedXWristJointRmsMeters < report.directWristJointRmsMeters * 0.25;
            if (!report.leftRightMirrorPassed) report.errors.Add("Left/right source assets do not form the expected wrist-space X mirror pair.");
        }

        private static void NormalizeForPreview(HandState hand, float wristX)
        {
            hand.model.transform.rotation = Quaternion.Inverse(hand.bones[1].rotation) * hand.model.transform.rotation;
            hand.model.transform.position += new Vector3(wristX, 0, 0) - hand.bones[1].position;
        }

        private static void Capture(Scene scene, GameObject left, GameObject right, string output, Summary summary)
        {
            Bounds bounds = default;
            bool initialized = false;
            foreach (var model in new[] { left, right }) foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                // Editor skin bounds can lag manually changed bones. Fit the camera to baked
                // geometry itself rather than an unrefreshed renderer bounding box.
                var baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked);
                    foreach (Vector3 local in baked.vertices)
                    {
                        Vector3 world = renderer.transform.TransformPoint(local);
                        if (!initialized) { bounds = new Bounds(world, Vector3.zero); initialized = true; }
                        else bounds.Encapsulate(world);
                    }
                }
                finally { Object.DestroyImmediate(baked); }
            }
            var cameraHost = new GameObject("Hand validation camera");
            SceneManager.MoveGameObjectToScene(cameraHost, scene);
            Camera camera = cameraHost.AddComponent<Camera>();
            camera.scene = scene;
            camera.enabled = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.05f, 0.065f);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(0.15f, bounds.extents.magnitude * 1.15f);
            camera.aspect = 16f / 9f;
            camera.nearClipPlane = 0.001f;
            camera.farClipPlane = 10;
            var additional = camera.GetUniversalAdditionalCameraData();
            additional.allowXRRendering = false;
            additional.renderPostProcessing = false;
            var lightHost = new GameObject("Hand validation key light");
            SceneManager.MoveGameObjectToScene(lightHost, scene);
            Light light = lightHost.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.9f, 0.97f, 1);
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
            var texture = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            string[] names = { "view-positive-y", "view-negative-y", "view-oblique" };
            Vector3[] directions = { Vector3.up, Vector3.down, new Vector3(0.5f, 0.8f, 0.5f).normalized };
            try
            {
                texture.Create();
                camera.targetTexture = texture;
                for (int i = 0; i < names.Length; i++)
                {
                    camera.transform.position = bounds.center + directions[i] * 1.2f;
                    camera.transform.rotation = Quaternion.LookRotation(-directions[i], i < 2 ? Vector3.back : Vector3.up);
                    light.transform.rotation = camera.transform.rotation * Quaternion.Euler(20, -25, 0);
                    if (GraphicsSettings.currentRenderPipeline != null)
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
                    else camera.Render();
                    RenderTexture.active = texture;
                    pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                    pixels.Apply();
                    string path = Path.Combine(output, names[i] + ".png");
                    File.WriteAllBytes(path, pixels.EncodeToPNG());
                    Color32[] colors = pixels.GetPixels32();
                    Color32 background = colors[0];
                    int different = 0;
                    for (int p = 0; p < colors.Length; p += 16)
                        if (Math.Abs(colors[p].r - background.r) + Math.Abs(colors[p].g - background.g) + Math.Abs(colors[p].b - background.b) > 25) different++;
                    float contentFraction = (float)different / ((colors.Length + 15) / 16);
                    summary.captures.Add(new CaptureReport { path = path, nonBackgroundFraction = contentFraction });
                    if (contentFraction < 0.003f) summary.errors.Add("Hand image appears empty: " + names[i]);
                }
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                texture.Release();
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(pixels);
            }
        }

        private static string Argument(string name, string fallback)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : fallback;
        }

        private sealed class HandState { public GameObject model; public Transform[] bones; public Vector3[] wristLocal; public HandReport report; }
        [Serializable] private sealed class Summary
        {
            public string utc, unity, scope, output;
            public bool passed, leftRightMirrorPassed, oppositeThumbSides;
            public double directWristJointRmsMeters, reflectedXWristJointRmsMeters;
            public float leftThumbWristX, rightThumbWristX;
            public HandReport[] hands;
            public List<string> errors = new List<string>();
            public List<CaptureReport> captures = new List<CaptureReport>();
        }
        [Serializable] private sealed class HandReport
        {
            public string side, asset;
            public int mappedJoints, uniqueJointCount, vertices;
            public long triangles;
            public bool importerOptimizesHierarchy, roundTripPassed;
            public float roundTripJointMaxMeters, roundTripRotationMaxDegrees, roundTripVertexMaxMeters;
            public JointReport[] joints;
            public MeshReport[] meshes;
        }
        [Serializable] private sealed class JointReport
        {
            public int index, occurrences;
            public string name, parent;
            public bool mappingMatchesPico, hierarchyMatches;
            public Vector3 wristLocalPosition;
        }
        [Serializable] private sealed class MeshReport
        {
            public string name;
            public int vertices, submeshes, rendererBones, bindposes;
            public long triangles;
            public bool bindBonesMapped, allTriangles;
            public float bindIdentityMaxError;
        }
        [Serializable] private sealed class CaptureReport { public string path; public float nonBackgroundFraction; }
    }
}

#endif

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
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
    public static class OceanGuardianRevealChecks
    {
        [Serializable] sealed class Report
        {
            public bool passed, entryAnchor, completeInitialOutline;
            public string scope;
            public float wingspan, verticalEnvelope, verticalFov, aspect, centreElevation, horizontalAngle;
            public Vector3 anchorEye, anchorForward;
            public int assertions, projectedVertices;
            public List<string> images = new List<string>();
            public List<string> checks = new List<string>();
        }

        // Fast isolated real-prefab/shader render; does NOT initialize the game or touch its save.
        // -executeMethod SwanUnderwater.Editor.OceanGuardianRevealChecks.Run
        // Optional -codexGuardianOutput <directory>. Graphics required; owns process exit.
        public static void Run()
        {
            string output = Argument("-codexGuardianOutput", "Validation/iteration-26/guardian-isolated");
            Scene preview = default;
            bool success = false;
            try
            {
                Guard();
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
                preview = EditorSceneManager.NewPreviewScene();
                var host = new GameObject("Isolated guardian validation owner"); SceneManager.MoveGameObjectToScene(host, preview);
                var owner = host.AddComponent<UnderwaterWorld>(); // Edit Mode: no Awake/Start execution.
                var cameraObject = new GameObject("Isolated level-eye camera"); SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>(); camera.scene = preview;
                camera.enabled = false; camera.fieldOfView = 60; camera.aspect = 16f / 9; camera.farClipPlane = 200;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f,.30f,.38f);
                camera.transform.SetPositionAndRotation(new Vector3(0,1.6f,-4), Quaternion.Euler(0,73,0));
                var lightObject = new GameObject("Isolated guardian key light"); SceneManager.MoveGameObjectToScene(lightObject, preview);
                var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1;
                light.transform.rotation = Quaternion.Euler(48,-30,0);
                var guardian = OceanSkyGuardian.Build(owner, camera, Vector3.zero);
                Capture(guardian, camera, output, false);
                success = true;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally { if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview); }
            if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        }

        // Call after world.IsReady from the actual full-flow preview. The real HMD remains untouched.
        public static void CaptureInScene(Camera head, string output)
        {
            Guard();
            var guardian = Object.FindFirstObjectByType<OceanSkyGuardian>();
            if (guardian == null || head == null || OceanEntry.BlocksGameplay)
                throw new InvalidOperationException("Full-world guardian capture requires the exposed playable scene.");
            Capture(guardian, head, output, true);
        }

        static void Capture(OceanSkyGuardian guardian, Camera head, string output, bool world)
        {
            Directory.CreateDirectory(output);
            var report = new Report { scope = world ?
                "Actual scene, prefab, material and shader in mono rendering. Temporary camera uses saved encounter eye and flat heading; no HMD pose changes. Not headset/VST or user comfort evidence." :
                "Isolated actual prefab/material/shader at a synthetic 73-degree encounter yaw; 60-degree vertical camera matches scene serialization. No game world, save, network, HMD or VST.",
                entryAnchor = guardian.UsesEntryAnchor, wingspan = guardian.ActualWingSpan, verticalEnvelope = guardian.VerticalEnvelope,
                anchorEye = guardian.RevealEye, anchorForward = guardian.RevealForward,
                verticalFov = head.fieldOfView, aspect = head.aspect };
            void Check(bool value, string description)
            { report.assertions++; report.checks.Add(description + ": " + value); if (!value) throw new InvalidOperationException(description); }
            var go = new GameObject("Fixed initial-eye guardian validation camera");
            SceneManager.MoveGameObjectToScene(go, head.gameObject.scene);
            Camera camera = go.AddComponent<Camera>(); camera.CopyFrom(head); camera.scene = head.scene;
            camera.enabled = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            camera.transform.SetPositionAndRotation(guardian.RevealEye, Quaternion.LookRotation(guardian.RevealForward, Vector3.up));
            var target = new RenderTexture(1280,720,24); target.Create();
            var image = new Texture2D(1280,720,TextureFormat.RGB24,false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                EditorTests.OceanGuardianCompositionTests.RunAll();
                Check(!world || guardian.UsesEntryAnchor, "Production reveal uses the saved entry heading");
                Check(Mathf.Abs(guardian.ActualWingSpan - OceanSkyGuardian.WingSpan) < .05f && guardian.TriangleCount == 4450,
                    "Real ray enlarged without changing topology");
                Check(guardian.GetComponentsInChildren<Collider>().Length == 0, "Guardian adds no swimming obstacle");
                foreach (double time in new[] { 0.0, 3.0, 8.0, 25.0 })
                {
                    guardian.PoseForValidation(time);
                    Check(guardian.ConservativeBottomY >= head.transform.position.y + OceanSkyGuardian.MinimumBodyGap - .001f,
                        "Conservative body stays at least 20 metres above observer at t=" + time);
                    foreach (Renderer renderer in guardian.GetComponentsInChildren<Renderer>())
                        Check(renderer.bounds.min.y >= guardian.ConservativeBottomY - .01f, "Expanded culling bounds fit the altitude envelope");
                    if (time == 0)
                    {
                        Vector3 offset = guardian.transform.position - camera.transform.position;
                        report.centreElevation = Mathf.Atan2(offset.y, Vector3.ProjectOnPlane(offset, Vector3.up).magnitude) * Mathf.Rad2Deg;
                        Check(report.centreElevation >= 15 && report.centreElevation <= 25, "Initial centre is naturally above the level gaze");
                        ProjectActualOutline(guardian, camera, report, Check);
                    }
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target; image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply(false,false);
                    string name = "guardian-level-eye-" + ((int)time).ToString("D2") + "s.png";
                    File.WriteAllBytes(Path.Combine(output,name), image.EncodeToPNG()); report.images.Add(name);
                }
                report.passed = true;
            }
            finally
            {
                guardian.RestorePoseForValidation(); RenderTexture.active = previous;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image); Object.DestroyImmediate(go);
                File.WriteAllText(Path.Combine(output,"guardian-reveal.json"), JsonUtility.ToJson(report,true));
            }
            Debug.Log("GUARDIAN_REVEAL_CHECKS_PASSED assertions=" + report.assertions + " width=" + report.horizontalAngle + " elevation=" + report.centreElevation);
        }

        static void ProjectActualOutline(OceanSkyGuardian guardian, Camera camera, Report report, Action<bool,string> check)
        {
            float minAngle = float.PositiveInfinity, maxAngle = float.NegativeInfinity;
            bool complete = true;
            foreach (MeshFilter filter in guardian.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<Renderer>(); Material material = renderer.sharedMaterial;
                Vector3 centre = material.GetVector("_Centre"), extent = material.GetVector("_HalfExtent");
                // Offline projection mirrors the unchanged shader's t=0 deformation; actual PNGs are
                // separately rendered by URP and must be visually reviewed, not inferred from this math.
                foreach (Vector3 source in filter.sharedMesh.vertices)
                {
                    Vector3 p = source;
                    float x = (p.z-centre.z)/Mathf.Max(extent.z,.01f), y = -(p.x-centre.x)/Mathf.Max(extent.x,.01f);
                    float wing = Mathf.Clamp01((Mathf.Abs(x)-.16f)/.84f), tail = Mathf.Clamp01((-y-.35f)/.65f);
                    p.y += Mathf.Sin(y*1.15f)*wing*wing*.10f;
                    p.x += tail*tail*.17f; p.z += Mathf.Sin(y*3.1f)*tail*tail*.018f;
                    Vector3 world = filter.transform.TransformPoint(p), local = camera.transform.InverseTransformPoint(world);
                    Vector3 viewport = camera.WorldToViewportPoint(world);
                    complete &= viewport.z > camera.nearClipPlane && viewport.z < camera.farClipPlane &&
                        viewport.x > 0 && viewport.x < 1 && viewport.y > 0 && viewport.y < 1;
                    float angle = Mathf.Atan2(local.x, local.z)*Mathf.Rad2Deg;
                    minAngle = Mathf.Min(minAngle,angle); maxAngle = Mathf.Max(maxAngle,angle); report.projectedVertices++;
                }
            }
            report.horizontalAngle = maxAngle-minAngle; report.completeInitialOutline = complete;
            check(report.projectedVertices > 1000 && complete, "All actual deformed initial vertices fit the level main-camera frustum");
            check(report.horizontalAngle >= 34 && report.horizontalAngle <= 50, "Large initial silhouette has about 35 to 50 degrees width");
        }
        static void Guard()
        {
            var displays = new List<XRDisplaySubsystem>(); SubsystemManager.GetSubsystems(displays);
            if (XRSettings.isDeviceActive || displays.Exists(x => x.running)) throw new InvalidOperationException("Refuses active XR.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Rendering required; omit -nographics.");
        }
        static string Argument(string key,string fallback)
        { string[] args=Environment.GetCommandLineArgs(); int i=Array.IndexOf(args,key); return i>=0&&i+1<args.Length?args[i+1]:fallback; }
    }
}
#endif

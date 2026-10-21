#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Imported anatomical landmarks and actual quest/ambient creation paths, not empty steering wrappers.</summary>
    public static class OceanSharkHeadingValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        [Serializable] public sealed class Case
        {
            public string resource, path;
            public Vector3 meshHead, meshTail, meshForward, correctedHalfExtents, meshScale;
            public float rawYaw, correctedAngle, minimumMovementDot = 1f, negativeControlDot, travel;
            public int movementSamples; public List<string> images = new List<string>();
        }
        [Serializable] public sealed class Report
        {
            public string unity, scope = "Real glTFast imported meshes; production CreateTarget/CreateCampaignCreature/AddAmbientShark paths in an isolated Editor preview scene. Synthetic time/aim, no tracked-hand or device claim.";
            public bool success; public int assertions; public string error;
            public List<Case> cases = new List<Case>();
        }
        sealed class Subject { public OceanCampaignCreatures.CreatureTarget target; public MeshFilter filter; public Case record; }
        static int assertions;

        // -executeMethod SwanUnderwater.Editor.OceanSharkHeadingValidation.Run
        // Graphics required; owns exit. Optional -oceanSharkHeadingOutput <empty directory>.
        public static void Run()
        {
            string output = Path.GetFullPath(Argument("-oceanSharkHeadingOutput", "Validation/shark-heading-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")));
            var report = new Report { unity = Application.unityVersion }; assertions = 0;
            Scene scene = default; OceanCampaignCreatures actors = null; UnderwaterWorld world = null;
            Material template = null, arrowMaterial = null; RenderTexture target = null; Texture2D pixels = null;
            try
            {
                Check(!EditorApplication.isPlayingOrWillChangePlaymode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Edit Mode and graphics required");
                Check(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "fresh output directory required"); Directory.CreateDirectory(output);
                scene = EditorSceneManager.NewPreviewScene();
                var worldHost = new GameObject("Inactive world factory for exact production methods"); worldHost.SetActive(false); SceneManager.MoveGameObjectToScene(worldHost, scene);
                world = worldHost.AddComponent<UnderwaterWorld>();
                template = new Material(Resources.Load<Shader>("Shaders/FishSwim")); typeof(UnderwaterWorld).GetField("fishSwimTemplate", Private).SetValue(world, template);
                var owner = new GameObject("Production campaign actor owner"); SceneManager.MoveGameObjectToScene(owner, scene); actors = owner.AddComponent<OceanCampaignCreatures>();
                var cameraHost = new GameObject("Heading comparison camera"); SceneManager.MoveGameObjectToScene(cameraHost, scene);
                Camera camera = cameraHost.AddComponent<Camera>(); camera.scene = scene; camera.enabled = false; camera.transform.position = new Vector3(0, 12, -5);
                camera.stereoTargetEye = StereoTargetEyeMask.None; camera.orthographic = true; camera.aspect = 16f / 9f; camera.nearClipPlane = .01f; camera.farClipPlane = 200f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .09f, .115f);
                var data = camera.GetUniversalAdditionalCameraData(); data.allowXRRendering = false; data.renderPostProcessing = false;
                var state = new OceanCampaignState(); state.StartLevel(5); actors.Initialize(world, camera, null, state, null);
                typeof(OceanCampaignCreatures).GetMethod("CreateTarget", Private).Invoke(actors, new object[] { -1, true });
                var subjects = new List<Subject>();
                AddSubject(actors.Targets[0], "Fish/15_great_white_shark", "quest CreateTarget", subjects, report);
                foreach (string resource in new[] { "Fish/15_great_white_shark", "Fish/14_hammerhead_shark" })
                {
                    var modelParent = new GameObject("Ambient source owner"); SceneManager.MoveGameObjectToScene(modelParent, scene);
                    GameObject model = world.CreateCampaignCreature(resource, modelParent.transform, 1.8f);
                    model.name = resource.Substring(5); model.transform.SetPositionAndRotation(new Vector3(subjects.Count * 10f, 12f, 0), Quaternion.Euler(0, 37, 0));
                    typeof(OceanCampaignCreatures).GetMethod("AddAmbientShark", Private).Invoke(actors, new object[] { model, subjects.Count });
                    AddSubject(actors.Targets[actors.Targets.Count - 1], resource, "ambient AddAmbientShark", subjects, report);
                }
                foreach (Subject subject in subjects)
                {
                    Transform body = subject.target.Body.transform;
                    Vector3 nose = (subject.filter.transform.TransformPoint(subject.record.meshHead) - subject.filter.transform.TransformPoint(subject.record.meshTail)).normalized;
                    subject.record.correctedAngle = Vector3.Angle(nose, body.forward);
                    Check(subject.record.correctedAngle < 6f, subject.record.path + " actual anatomical nose aligns with steering +Z");
                    if (subject.record.resource.Contains("great_white"))
                    {
                        Transform pivot = subject.target.Visual.transform.Find("Species mesh alignment"); Check(pivot != null, "white shark visual alignment exists");
                        Quaternion saved = pivot.localRotation; pivot.localRotation = Quaternion.identity;
                        nose = (subject.filter.transform.TransformPoint(subject.record.meshHead) - subject.filter.transform.TransformPoint(subject.record.meshTail)).normalized;
                        subject.record.negativeControlDot = Vector3.Dot(nose, body.forward);
                        Check(subject.record.negativeControlDot < .80f, "removing actual white-shark correction reproduces sideways movement"); pivot.localRotation = saved;
                    }
                }
                for (int frame = 0; frame < 360; frame++)
                {
                    Vector3[] before = subjects.Select(x => x.target.Body.transform.position).ToArray();
                    actors.TickGameplay(.02f, true);
                    for (int i = 0; i < subjects.Count; i++)
                    {
                        Subject subject = subjects[i]; Vector3 displacement = subject.target.Body.transform.position - before[i];
                        if (displacement.magnitude < .00001f) continue;
                        Vector3 nose = (subject.filter.transform.TransformPoint(subject.record.meshHead) - subject.filter.transform.TransformPoint(subject.record.meshTail)).normalized;
                        float dot = Vector3.Dot(displacement.normalized, nose); subject.record.minimumMovementDot = Mathf.Min(subject.record.minimumMovementDot, dot);
                        subject.record.movementSamples++; subject.record.travel += displacement.magnitude;
                        Check(dot > .98f, "actual model anatomy follows movement during production cruise/turn: " + subject.record.path);
                    }
                }
                foreach (Subject subject in subjects) Check(subject.record.movementSamples > 30 && subject.record.travel > .5f, "real actor swims enough to validate its heading");
                for (int i = 0; i < 2; i++)
                {
                    var lightHost = new GameObject("Heading light " + i); SceneManager.MoveGameObjectToScene(lightHost, scene);
                    var light = lightHost.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = i == 0 ? 1.7f : .65f; light.shadows = LightShadows.None;
                    light.transform.rotation = Quaternion.Euler(i == 0 ? new Vector3(35, -40, 0) : new Vector3(-25, 140, 0));
                }
                arrowMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")); arrowMaterial.SetColor("_BaseColor", new Color(.18f, 1f, .32f)); arrowMaterial.SetFloat("_Cull", 0f);
                var arrow = new GameObject("Green steering direction arrow"); SceneManager.MoveGameObjectToScene(arrow, scene);
                var line = arrow.AddComponent<LineRenderer>(); line.sharedMaterial = arrowMaterial; line.positionCount = 2; line.useWorldSpace = true;
                var arrowHead = new GameObject("Arrow head"); SceneManager.MoveGameObjectToScene(arrowHead, scene);
                var arrowMesh = new Mesh(); arrowHead.AddComponent<MeshFilter>().sharedMesh = arrowMesh; arrowHead.AddComponent<MeshRenderer>().sharedMaterial = arrowMaterial;
                target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target; pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                foreach (var subject in subjects) { subject.target.Visual.SetActive(false); foreach (Canvas canvas in subject.target.Body.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false; }
                for (int i = 0; i < subjects.Count; i++)
                {
                    Subject subject = subjects[i]; subject.target.Visual.SetActive(true);
                    Transform pivot = subject.target.Visual.transform.Find("Species mesh alignment"); Quaternion saved = pivot != null ? pivot.localRotation : Quaternion.identity;
                    foreach (bool before in subject.record.resource.Contains("great_white") ? new[] { true, false } : new[] { false })
                    {
                        if (pivot != null) pivot.localRotation = before ? Quaternion.identity : saved;
                        for (int phase = 0; phase < 2; phase++)
                        {
                            foreach (Renderer renderer in subject.target.Visual.GetComponentsInChildren<Renderer>(true))
                            { var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); block.SetVector("_SwimMotion", new Vector4(phase * Mathf.PI * .5f, 1f, 0f, 1f)); renderer.SetPropertyBlock(block); }
                            Vector3[] vertices = subject.filter.sharedMesh.vertices.Select(x => subject.filter.transform.TransformPoint(x)).ToArray(); Bounds bounds = new Bounds(vertices[0], Vector3.zero); foreach (var p in vertices) bounds.Encapsulate(p);
                            Transform body = subject.target.Body.transform; float length = bounds.size.magnitude;
                            Vector3 start = bounds.center + body.up * (bounds.extents.magnitude * .45f), end = start + body.forward * (length * .7f), side = body.right * (length * .06f);
                            line.startWidth = line.endWidth = length * .015f; line.SetPosition(0, start); line.SetPosition(1, end - body.forward * length * .1f);
                            arrowMesh.vertices = new[] { end, end - body.forward * length * .16f + side, end - body.forward * length * .16f - side }; arrowMesh.triangles = new[] { 0, 1, 2 }; arrowMesh.RecalculateBounds();
                            camera.transform.position = bounds.center + (body.right * 1.25f + body.up * .55f + body.forward).normalized * length * 2f;
                            camera.transform.LookAt(bounds.center + body.up * length * .10f, body.up); camera.orthographicSize = length * .62f;
                            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                            RenderTexture previous = RenderTexture.active; RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply(false, false); RenderTexture.active = previous;
                            string path = Path.Combine(output, "actor-" + i + (before ? "-before" : "-corrected") + "-phase-" + phase + ".png"); File.WriteAllBytes(path, pixels.EncodeToPNG()); subject.record.images.Add(path);
                        }
                    }
                    if (pivot != null) pivot.localRotation = saved; subject.target.Visual.SetActive(false);
                }
                Object.DestroyImmediate(arrowMesh); report.success = true;
            }
            catch (Exception exception) { report.error = exception.ToString(); Debug.LogException(exception); }
            finally
            {
                report.assertions = assertions;
                if (actors != null) ((IList)typeof(OceanCampaignCreatures).GetField("targets", Private).GetValue(actors)).Clear();
                if (world != null) foreach (Material material in ((IDictionary)typeof(UnderwaterWorld).GetField("heroMaterialCache", Private).GetValue(world)).Values) if (material != null && !AssetDatabase.Contains(material)) Object.DestroyImmediate(material);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                if (template != null) Object.DestroyImmediate(template); if (arrowMaterial != null) Object.DestroyImmediate(arrowMaterial);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); } if (pixels != null) Object.DestroyImmediate(pixels);
                Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            }
            Debug.Log("SHARK_HEADING_VALIDATION_" + (report.success ? "PASS " : "FAIL ") + output);
            if (Application.isBatchMode) EditorApplication.Exit(report.success ? 0 : 1);
        }

        static void AddSubject(OceanCampaignCreatures.CreatureTarget target, string resource, string path, List<Subject> subjects, Report report)
        {
            MeshFilter filter = target.Visual.GetComponentInChildren<MeshFilter>(true); Check(filter != null && filter.sharedMesh != null, "actual imported shark mesh exists");
            Vector3[] points = filter.sharedMesh.vertices; Vector3 mean = Vector3.zero; foreach (var p in points) mean += p; mean /= points.Length;
            var covariance = new float[3, 3]; foreach (var p in points) { Vector3 q = p - mean; for (int i = 0; i < 3; i++) for (int k = 0; k < 3; k++) covariance[i, k] += q[i] * q[k]; }
            Vector3 axis = Vector3.forward; for (int n = 0; n < 80; n++) axis = new Vector3(Vector3.Dot(new Vector3(covariance[0, 0], covariance[0, 1], covariance[0, 2]), axis), Vector3.Dot(new Vector3(covariance[1, 0], covariance[1, 1], covariance[1, 2]), axis), Vector3.Dot(new Vector3(covariance[2, 0], covariance[2, 1], covariance[2, 2]), axis)).normalized;
            if (axis.z < 0f) axis = -axis; float min = points.Min(x => Vector3.Dot(x, axis)), max = points.Max(x => Vector3.Dot(x, axis));
            Vector3 head = Vector3.zero, tail = Vector3.zero; int heads = 0, tails = 0;
            foreach (var p in points) { float along = Vector3.Dot(p, axis); if (along > Mathf.Lerp(min, max, .93f)) { head += p; heads++; } if (along < Mathf.Lerp(min, max, .07f)) { tail += p; tails++; } }
            Check(heads > 0 && tails > 0, "anatomical end regions measured"); head /= heads; tail /= tails; Vector3 nose = (head - tail).normalized;
            Quaternion correction = Quaternion.Euler(0, resource.Contains("great_white") ? -45 : 0, 0); Bounds canonical = new Bounds(correction * points[0], Vector3.zero); foreach (var p in points) canonical.Encapsulate(correction * p);
            var record = new Case { resource = resource, path = path, meshHead = head, meshTail = tail, meshForward = nose,
                rawYaw = Mathf.Atan2(nose.x, nose.z) * Mathf.Rad2Deg, correctedHalfExtents = canonical.extents, meshScale = filter.transform.lossyScale };
            report.cases.Add(record); subjects.Add(new Subject { target = target, filter = filter, record = record });
            Debug.Log("[SharkHeading] " + resource + " path=" + path + " head=" + head + " tail=" + tail + " rawYaw=" + record.rawYaw + " canonicalHalf=" + canonical.extents);
            foreach (Renderer renderer in target.Visual.GetComponentsInChildren<Renderer>()) foreach (Material material in renderer.sharedMaterials)
                Check(material != null && material.shader.name == "SwanUnderwater/FishSwim" && material.GetVector("_FishBody").x == (resource.Contains("great_white") ? 6f : 5f), "production species shader profile matches anatomical basis");
        }
        static void Check(bool condition, string detail) { assertions++; if (!condition) throw new InvalidOperationException("Shark heading: " + detail); }
        static string Argument(string key, string fallback) { string[] args = Environment.GetCommandLineArgs(); for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1]; return fallback; }
    }
}
#endif

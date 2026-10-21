#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Production transition, real GPU draws and corresponding physical plane points.
    /// Synthetic independent eye projections, NOT XR Multiview, compositor or wearer acceptance.</summary>
    public static class OceanMapPassageStereoPreview
    {
        const int Size = 1024;
        const float MeanLimit = .004f, MaximumLimit = .065f;
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] sealed class Eye
        {
            public string name, calibrationPng;
            public Vector3 position;
            public Matrix4x4 view, projection;
            public bool readbackYFlipped, coversAllCorners;
            public float calibrationErrorPixels;
            public Vector2 markerExpected, markerObserved;
        }
        [Serializable] sealed class Sample
        {
            public Vector3 headPoint;
            public Vector2 leftUv, rightUv;
            public Color left, right;
            public float rgbError;
        }
        [Serializable] sealed class Pair
        {
            public string key, leftPng, rightPng, leftSha256, rightSha256;
            public float ipd, flowTime, rush, planeDepth, meanRgbError, maximumRgbError, shiftedControlMeanError;
            public float leftLuminanceRange, rightLuminanceRange;
            public bool headTransformUnchanged;
            public Eye leftEye, rightEye;
            public List<Sample> samples = new List<Sample>();
        }
        [Serializable] sealed class Comparison
        {
            public string key;
            public float baselineMean, candidateMean, baselineMaximum, candidateMaximum;
        }
        [Serializable] sealed class Report
        {
            public bool success, qualityRestored, headTransformUnchanged;
            public int width = Size, height = Size;
            public float meanLimit = MeanLimit, maximumLimit = MaximumLimit;
            public string label, shaderSha256, runtimeSha256, graphicsApi, colorSpace, baselineReport;
            public string scope = "Actual OceanEnvironmentTransition geometry/material and Mobile URP GPU rendering. Synthetic eye view/projection overrides on one unchanged head Camera; NOT XR Multiview hardware, compositor output or wearer comfort acceptance. Same physical cover-plane points are projected independently into both rendered eye images; no shader-equation mirror or same-pixel comparison.";
            public string readback = "ARGBFloat linear RenderTexture -> RGBAFloat linear CPU texture; metrics use float RGB. PNGs explicitly encode linear RGB to sRGB for previews. Y orientation is independently calibrated with an actual rendered white marker.";
            public List<Pair> pairs = new List<Pair>();
            public List<Comparison> comparisons = new List<Comparison>();
            public List<string> failures = new List<string>();
        }
        sealed class Pixels
        {
            public Color[] colors;
            public float range;
        }

        // Separate graphical batch Editor; omit -quit and -nographics. Owns exit 0/1.
        // -executeMethod SwanUnderwater.Editor.OceanMapPassageStereoPreview.Capture
        // -codexStereoOutput <fresh directory> [-codexStereoLabel baseline|candidate]
        // [-codexStereoBaselineReport <earlier report.json>]
        public static void Capture()
        {
            string output = Path.GetFullPath("Validation/map-passage-stereo-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
            var report = new Report { label = "unlabelled" };
            bool directoryReady = false;
            var originalPipeline = QualitySettings.renderPipeline;
            var originalDefault = GraphicsSettings.defaultRenderPipeline;
            bool originalAsync = ShaderUtil.allowAsyncCompilation, originalSrgb = GL.sRGBWrite;
            UniversalRenderPipelineAsset pipeline = null;
            Scene scene = default;
            Camera camera = null;
            OceanEnvironmentTransition effect = null;
            RenderTexture target = null;
            Material markerMaterial = null;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Omit -quit/-nographics: stereo preview renders and owns exit.");
                    if (args[i] == "-codexStereoOutput") output = Path.GetFullPath(Value(args, ref i));
                    else if (args[i] == "-codexStereoLabel") report.label = Value(args, ref i);
                    else if (args[i] == "-codexStereoBaselineReport") report.baselineReport = Path.GetFullPath(Value(args, ref i));
                }
                if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Use an independent graphical batch Editor in Edit Mode.");
                if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length != 0)
                    throw new IOException("Output must be a fresh directory; existing evidence is never overwritten.");
                Directory.CreateDirectory(output); directoryReady = true;
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
                    throw new InvalidOperationException("A real GPU and ARGBFloat render target are required.");
                report.graphicsApi = SystemInfo.graphicsDeviceType.ToString(); report.colorSpace = QualitySettings.activeColorSpace.ToString();
                report.shaderSha256 = Hash(File.ReadAllBytes("Assets/Resources/Shaders/OceanEnvironmentTransition.shader"));
                report.runtimeSha256 = Hash(File.ReadAllBytes("Assets/SwanUnderwater/OceanEnvironmentTransition.cs"));
                ShaderUtil.allowAsyncCompilation = false; GL.sRGBWrite = false;
                var source = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
                if (source == null) throw new InvalidOperationException("Mobile URP source asset missing.");
                pipeline = Object.Instantiate(source); pipeline.name = "Temporary stereo regression Mobile URP";
                pipeline.hideFlags = HideFlags.HideAndDontSave; pipeline.msaaSampleCount = 1; pipeline.renderScale = 1f; pipeline.supportsHDR = false;
                QualitySettings.renderPipeline = pipeline; GraphicsSettings.defaultRenderPipeline = pipeline;
                scene = EditorSceneManager.NewPreviewScene();
                var head = new GameObject("Unchanged synthetic head Camera"); SceneManager.MoveGameObjectToScene(head, scene);
                camera = head.AddComponent<Camera>(); camera.scene = scene; camera.enabled = false;
                camera.transform.SetPositionAndRotation(new Vector3(1.2f, 1.6f, -2.1f), Quaternion.Euler(11f, 23f, 4f));
                camera.nearClipPlane = .05f; camera.farClipPlane = 30f; camera.aspect = 1f;
                camera.stereoTargetEye = StereoTargetEyeMask.None; camera.allowMSAA = false; camera.allowHDR = false;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.useOcclusionCulling = false; camera.cullingMask = 1 << 29;
                var data = camera.GetUniversalAdditionalCameraData(); data.allowXRRendering = false; data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None; data.dithering = false; data.stopNaN = false;
                Vector3 headPosition = camera.transform.position; Quaternion headRotation = camera.transform.rotation;
                Transform headParent = camera.transform.parent;
                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                    { name = "Linear float synthetic eye capture", antiAliasing = 1, useMipMap = false, autoGenerateMips = false };
                target.Create(); camera.targetTexture = target;
                effect = OceanEnvironmentTransition.Create(camera);
                typeof(OceanEnvironmentTransition).GetMethod("SetOpacity", Fields).Invoke(effect, new object[] { 1f });
                var surface = effect.GetComponent<MeshRenderer>(); Material material = surface.sharedMaterial;
                float depth = effect.transform.localPosition.z;
                var marker = GameObject.CreatePrimitive(PrimitiveType.Quad); SceneManager.MoveGameObjectToScene(marker, scene);
                Object.DestroyImmediate(marker.GetComponent<Collider>()); marker.name = "Independent readback orientation marker";
                marker.layer = 29; marker.transform.SetParent(camera.transform, false);
                marker.transform.localPosition = new Vector3(depth * .37f, depth * .23f, depth);
                marker.transform.localScale = Vector3.one * (depth * .055f);
                markerMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                markerMaterial.SetColor("_BaseColor", Color.white); markerMaterial.SetFloat("_Cull", 0f);
                marker.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;
                float[] ipds = { .058f, .064f, .070f };
                for (int configuration = 0; configuration < ipds.Length; configuration++)
                {
                    float ipd = ipds[configuration];
                    Eye left = MakeEye(camera, "left", -ipd * .5f, configuration);
                    Eye right = MakeEye(camera, "right", ipd * .5f, configuration);
                    surface.enabled = false; marker.SetActive(true);
                    Calibrate(camera, target, marker.transform.position, left, output, configuration);
                    Calibrate(camera, target, marker.transform.position, right, output, configuration);
                    marker.SetActive(false); surface.enabled = true;
                    left.coversAllCorners = Covers(effect, left); right.coversAllCorners = Covers(effect, right);
                    for (int phase = 0; phase < 3; phase++)
                    {
                        float flow = new[] { .15f, .8f, 1.65f }[phase], rush = new[] { .2f, .6f, 1f }[phase];
                        material.SetFloat("_FlowTime", flow); material.SetFloat("_Rush", rush);
                        string key = "case-" + configuration + "-phase-" + phase;
                        Pixels l = Draw(camera, target, left), r = Draw(camera, target, right);
                        var pair = new Pair { key = key, ipd = ipd, flowTime = flow, rush = rush, planeDepth = depth,
                            leftEye = left, rightEye = right, leftPng = key + "-left.png", rightPng = key + "-right.png",
                            leftLuminanceRange = l.range, rightLuminanceRange = r.range };
                        SavePng(output, pair.leftPng, l.colors); SavePng(output, pair.rightPng, r.colors);
                        pair.leftSha256 = Hash(File.ReadAllBytes(Path.Combine(output, pair.leftPng)));
                        pair.rightSha256 = Hash(File.ReadAllBytes(Path.Combine(output, pair.rightPng)));
                        for (int y = -8; y <= 8; y++) for (int x = -11; x <= 11; x++)
                        {
                            Vector3 local = new Vector3(x / 11f * depth * .55f, y / 8f * depth * .42f, depth);
                            Vector3 worldPoint = camera.transform.TransformPoint(local);
                            Vector2 lu = Project(left, worldPoint), ru = Project(right, worldPoint);
                            if (!Inside(lu) || !Inside(ru)) continue;
                            Color lc = Bilinear(l.colors, lu), rc = Bilinear(r.colors, ru);
                            float error = RgbError(lc, rc);
                            pair.samples.Add(new Sample { headPoint = local, leftUv = lu, rightUv = ru, left = lc, right = rc, rgbError = error });
                            pair.meanRgbError += error; pair.maximumRgbError = Mathf.Max(pair.maximumRgbError, error);
                            Vector2 shifted = ru + Vector2.right * .025f;
                            pair.shiftedControlMeanError += RgbError(lc, Bilinear(r.colors, shifted));
                        }
                        if (pair.samples.Count > 0)
                        { pair.meanRgbError /= pair.samples.Count; pair.shiftedControlMeanError /= pair.samples.Count; }
                        pair.headTransformUnchanged = camera.transform.parent == headParent && camera.transform.position == headPosition &&
                            Quaternion.Angle(camera.transform.rotation, headRotation) < .00001f;
                        report.pairs.Add(pair);
                        Debug.Log("[MapPassageStereo] " + key + " depth=" + depth + " samples=" + pair.samples.Count +
                            " mean=" + pair.meanRgbError + " max=" + pair.maximumRgbError + " shiftedControl=" + pair.shiftedControlMeanError);
                    }
                }
                report.headTransformUnchanged = camera.transform.parent == headParent && camera.transform.position == headPosition &&
                    Quaternion.Angle(camera.transform.rotation, headRotation) < .00001f;
                // Measure and save every actual pair before final assertions, including the old failing version.
                foreach (Pair pair in report.pairs)
                {
                    Check(report, pair.samples.Count >= 300, pair.key + ": sufficient common physical plane samples");
                    Check(report, pair.meanRgbError <= MeanLimit && pair.maximumRgbError <= MaximumLimit,
                        pair.key + ": corresponding plane colours disagree (mean=" + pair.meanRgbError + ", max=" + pair.maximumRgbError + ")");
                    Check(report, pair.shiftedControlMeanError > MeanLimit * 1.5f, pair.key + ": deliberate 2.5% right-eye offset must fail colour matching");
                    Check(report, pair.leftLuminanceRange > .02f && pair.rightLuminanceRange > .02f, pair.key + ": both images contain real nonconstant shader content");
                    Check(report, pair.leftEye.calibrationErrorPixels < 2.5f && pair.rightEye.calibrationErrorPixels < 2.5f,
                        pair.key + ": rendered marker must validate independent projection/readback coordinates");
                    Check(report, pair.leftEye.coversAllCorners && pair.rightEye.coversAllCorners, pair.key + ": actual mesh covers both complete eye frusta");
                    Check(report, pair.planeDepth >= 2f, pair.key + ": head-attached cover must not sit at uncomfortable near-card depth");
                    Check(report, pair.headTransformUnchanged, pair.key + ": rendering cannot rewrite the head transform");
                }
                Check(report, report.headTransformUnchanged && report.pairs.Count == 9, "all nine phase/frustum pairs completed without head writes");
                if (!string.IsNullOrEmpty(report.baselineReport))
                {
                    var baseline = JsonUtility.FromJson<Report>(File.ReadAllText(report.baselineReport));
                    foreach (Pair pair in report.pairs) foreach (Pair earlier in baseline.pairs) if (earlier.key == pair.key)
                        report.comparisons.Add(new Comparison { key = pair.key, baselineMean = earlier.meanRgbError,
                            candidateMean = pair.meanRgbError, baselineMaximum = earlier.maximumRgbError, candidateMaximum = pair.maximumRgbError });
                }
            }
            catch (Exception error) { report.failures.Add(error.ToString()); }
            finally
            {
                if (effect != null)
                {
                    // Production OnDestroy schedules PlayMode Destroy. These preview-owned
                    // resources are instead released immediately in this EditMode process.
                    Material material = effect.GetComponent<MeshRenderer>().sharedMaterial;
                    Mesh mesh = effect.GetComponent<MeshFilter>().sharedMesh;
                    typeof(OceanEnvironmentTransition).GetField("material", Fields).SetValue(effect, null);
                    typeof(OceanEnvironmentTransition).GetField("mesh", Fields).SetValue(effect, null);
                    Object.DestroyImmediate(effect.gameObject); Object.DestroyImmediate(material); Object.DestroyImmediate(mesh);
                }
                if (camera != null) camera.targetTexture = null;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (markerMaterial != null) Object.DestroyImmediate(markerMaterial);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                QualitySettings.renderPipeline = originalPipeline; GraphicsSettings.defaultRenderPipeline = originalDefault;
                ShaderUtil.allowAsyncCompilation = originalAsync; GL.sRGBWrite = originalSrgb;
                report.qualityRestored = QualitySettings.renderPipeline == originalPipeline && GraphicsSettings.defaultRenderPipeline == originalDefault;
                if (pipeline != null) Object.DestroyImmediate(pipeline);
                report.success = report.failures.Count == 0 && report.qualityRestored;
                if (directoryReady) File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
                Debug.Log("OCEAN_MAP_PASSAGE_STEREO_" + (report.success ? "PASS" : "FAIL") + " output=" + output + " failures=" + report.failures.Count);
                EditorApplication.Exit(report.success ? 0 : 1);
            }
        }

        static Eye MakeEye(Camera camera, string name, float offset, int configuration)
        {
            Vector3 position = camera.transform.TransformPoint(Vector3.right * offset);
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(position, camera.transform.rotation, Vector3.one).inverse;
            float n = camera.nearClipPlane, outer = 1.15f + configuration * .08f, inner = .88f - configuration * .035f;
            float left = name == "left" ? outer : inner, right = name == "left" ? inner : outer;
            return new Eye { name = name, position = position, view = view,
                projection = Matrix4x4.Frustum(-left * n, right * n, -.96f * n, 1.08f * n, n, camera.farClipPlane) };
        }
        static void Calibrate(Camera camera, RenderTexture target, Vector3 marker, Eye eye, string output, int configuration)
        {
            eye.readbackYFlipped = false; Pixels image = Draw(camera, target, eye);
            eye.calibrationPng = "case-" + configuration + "-" + eye.name + "-coordinate-marker.png";
            SavePng(output, eye.calibrationPng, image.colors);
            double x = 0, y = 0; int count = 0;
            for (int py = 0; py < Size; py++) for (int px = 0; px < Size; px++)
                if (image.colors[py * Size + px].r > .7f && image.colors[py * Size + px].g > .7f && image.colors[py * Size + px].b > .7f)
                { x += px; y += py; count++; }
            Vector2 expected = Project(eye, marker) * Size - Vector2.one * .5f;
            Vector2 observed = count > 0 ? new Vector2((float)(x / count), (float)(y / count)) : new Vector2(-10000, -10000);
            Vector2 flipped = new Vector2(expected.x, Size - 1 - expected.y);
            eye.readbackYFlipped = Vector2.Distance(flipped, observed) < Vector2.Distance(expected, observed);
            eye.markerExpected = eye.readbackYFlipped ? flipped : expected; eye.markerObserved = observed;
            eye.calibrationErrorPixels = Vector2.Distance(eye.markerExpected, observed);
        }
        static Pixels Draw(Camera camera, RenderTexture target, Eye eye)
        {
            camera.worldToCameraMatrix = eye.view; camera.projectionMatrix = eye.projection;
            camera.cullingMatrix = eye.projection * eye.view;
            for (int pass = 0; pass < 2; pass++)
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var previous = RenderTexture.active;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); texture.Apply(false, false);
                Color[] colors = texture.GetPixels(); float min = float.PositiveInfinity, max = float.NegativeInfinity;
                foreach (Color color in colors)
                {
                    float luma = color.r * .2126f + color.g * .7152f + color.b * .0722f;
                    min = Mathf.Min(min, luma); max = Mathf.Max(max, luma);
                }
                return new Pixels { colors = colors, range = max - min };
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
        }
        static Vector2 Project(Eye eye, Vector3 world)
        {
            Vector4 clip = eye.projection * eye.view * new Vector4(world.x, world.y, world.z, 1f);
            Vector2 uv = new Vector2(clip.x / clip.w, clip.y / clip.w) * .5f + Vector2.one * .5f;
            if (eye.readbackYFlipped) uv.y = 1f - uv.y;
            return uv;
        }
        static bool Covers(OceanEnvironmentTransition effect, Eye eye)
        {
            Bounds bounds = effect.GetComponent<MeshFilter>().sharedMesh.bounds;
            Matrix4x4 inverse = (eye.projection * eye.view).inverse;
            Vector3 normal = effect.transform.forward, origin = effect.transform.position;
            foreach (float x in new[] { -1f, 1f }) foreach (float y in new[] { -1f, 1f })
            {
                Vector4 p = inverse * new Vector4(x, y, 0f, 1f);
                Vector3 ray = new Vector3(p.x, p.y, p.z) / p.w - eye.position;
                float denominator = Vector3.Dot(normal, ray);
                if (Mathf.Abs(denominator) < .000001f) return false;
                float distance = Vector3.Dot(origin - eye.position, normal) / denominator;
                Vector3 local = effect.transform.InverseTransformPoint(eye.position + ray * distance);
                if (distance <= 0f || local.x < bounds.min.x || local.x > bounds.max.x || local.y < bounds.min.y || local.y > bounds.max.y) return false;
            }
            return true;
        }
        static bool Inside(Vector2 uv) => uv.x > .03f && uv.x < .97f && uv.y > .03f && uv.y < .97f;
        static Color Bilinear(Color[] pixels, Vector2 uv)
        {
            float x = Mathf.Clamp(uv.x * Size - .5f, 0f, Size - 1f), y = Mathf.Clamp(uv.y * Size - .5f, 0f, Size - 1f);
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), x1 = Mathf.Min(x0 + 1, Size - 1), y1 = Mathf.Min(y0 + 1, Size - 1);
            return Color.LerpUnclamped(Color.LerpUnclamped(pixels[y0 * Size + x0], pixels[y0 * Size + x1], x - x0),
                Color.LerpUnclamped(pixels[y1 * Size + x0], pixels[y1 * Size + x1], x - x0), y - y0);
        }
        static float RgbError(Color a, Color b) => (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b)) / 3f;
        static void SavePng(string output, string name, Color[] linear)
        {
            var preview = new Texture2D(Size, Size, TextureFormat.RGB24, false, true);
            try
            {
                var encoded = new Color32[linear.Length];
                for (int i = 0; i < linear.Length; i++) { Color c = linear[i].gamma; c.a = 1f; encoded[i] = c; }
                preview.SetPixels32(encoded); preview.Apply(false, false); File.WriteAllBytes(Path.Combine(output, name), preview.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(preview); }
        }
        static void Check(Report report, bool pass, string detail) { if (!pass) report.failures.Add(detail); }
        static string Value(string[] args, ref int index)
        { if (++index >= args.Length || args[index].StartsWith("-")) throw new ArgumentException("Missing stereo preview argument."); return args[index]; }
        static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>
    /// Explicit admission of the original licensed, animated mackerel. No import/build/domain-reload hooks.
    /// Dolphin and turtle originals stay quarantined in Sources until their own animation gates pass.
    /// Uses the user-authorized existing Unity pipeline; never alters any older fish or scene.
    /// </summary>
    public static class OceanMarineExpansionBake
    {
        const string Package = "Sources/MarineExpansion/mackerel-eca6071c";
        const string PortableReceipt = "Sources/MarineExpansion/mackerel-native-admission.json";
        const string SourceRoot = "Assets/ArtSources/MarineExpansion/mackerel";
        const string BakedRoot = "Assets/ArtBaked/MarineExpansion/mackerel";
        const string PrefabPath = "Assets/Resources/MarineExpansion/mackerel.prefab";
        const string GeometryPath = "Assets/Resources/MarineExpansion/mackerel-geometry.json";
        const string ModelName = "Mackerel_6_try4.fbx";
        const string ArchiveHash = "1dc61bf813ed2da8c0e1e4f55936d6f83daa3bb1088b8a0fa94cf940727c8653";
        const string Uid = "eca6071c26fc4013b8c94a3f37a434c7";
        const string SourceUrl = "https://sketchfab.com/3d-models/atlantic-mackerel-animated-and-game-ready-eca6071c26fc4013b8c94a3f37a434c7";
        const string ReadbackShaderPath = "Assets/SwanUnderwater/Editor/ReefAstcReadback.shader";
        const long SourceDeclaredTriangles = 6002;
        const long ReviewedImportedTriangles = 5986;
        const int ReviewedImportedVertices = 3354, ReviewedSkinBones = 16;
        static readonly InputFile[] Inputs = {
            new InputFile("source/" + ModelName, 570188, 782874125u, "8b1c7668a55bb73c5d40aa3450d8b1374165a33addb315db820748610ec26126"),
            new InputFile("textures/mackerel_sideview.png", 231948, 1297103326u, "e14db4c944a1236130bddf88703227b398858e3d95c9e4b4741ca1adc4c25f91"),
            new InputFile("textures/mackerel_sideview_normal.png", 215538, 3088977131u, "c29fcd1027fa095cfd890e26655cc54a7368105ea654ff87de56b475712e1d70")
        };
        static readonly string[] ProtectedRoots = { "Assets/Resources/Fish", "Assets/Resources/RealisticReef", "Assets/ArtBaked/MarketReef", "Assets/ArtBaked/ReefAstc", "Assets/ArtSources/MarketReef" };
        static Pending pending;
        static Material decoder;
        static bool calibrated, flipRows;
        static string EvidenceRoot => Path.GetFullPath(Argument("-oceanMarineBakeOutput", Environment.GetEnvironmentVariable("OCEAN_MARINE_BAKE_REPORT_DIR") ?? "Validation/marine-expansion-bake"));
        static string Latest => Path.Combine(EvidenceRoot, "latest.json");
        static string ModelPath => SourceRoot + "/" + ModelName;

        sealed class InputFile
        {
            public readonly string path, hash; public readonly long bytes; public readonly uint crc;
            public InputFile(string path, long bytes, uint crc, string hash) { this.path = path; this.bytes = bytes; this.crc = crc; this.hash = hash; }
        }
        [Serializable] sealed class Receipt
        { public string uid = "", source_url = "", author = "", license = "", license_url = "", sha256 = ""; public long bytes = 0; public Roster[] roster = Array.Empty<Roster>(); }
        [Serializable] sealed class Roster { public string path = ""; public long bytes = 0, crc = 0; }
        [Serializable] public sealed class FileRecord { public string path, sha256; public long bytes; }
        [Serializable] public sealed class ClipRecord
        { public string name; public float length, frameRate; public bool looping, selected; public int curves; }
        [Serializable] public sealed class TranslationRecord
        { public string path, property; public float range, endpointDelta, worldScale; public bool navigationRoot; }
        [Serializable] public sealed class RotationRecord { public string path; public float loopAngle; }
        [Serializable] public sealed class PoseBoundsRecord { public Vector3 min, max; }
        [Serializable] public sealed class DecodeProbeRecord
        {
            public string source, sha256, format, graphicsFormat, rgbaSha256;
            public bool normal, loaded, srgb, readable; public int width, height, mips, rawBytes, color32Count, alphaMin = 255, alphaMax;
        }
        [Serializable] public sealed class DecodeProbeReport
        { public string unity, utc; public List<DecodeProbeRecord> textures = new List<DecodeProbeRecord>(); }
        [Serializable] public sealed class DiagnosticBone
        { public string path; public Vector3 position; public Quaternion rotation; public float angleFromFirst; }
        [Serializable] public sealed class DiagnosticPose
        { public string clip, image; public float time; public Vector3 boundsMin, boundsMax, noseDirection; public List<DiagnosticBone> bones = new List<DiagnosticBone>(); }
        [Serializable] public sealed class DiagnosticReport
        {
            public string unity, scope = "Original source FBX/Swim and Idle; CPU BakeMesh(useScale:true) world-vertex snapshots rendered as static meshes to avoid GPU skin-cache reuse within one Editor tick. No curve/asset mutation or admission.";
            public Vector3 skinLocalScale, skinLossyScale, originalRendererMin, originalRendererMax;
            public Vector3 bakeFalseLocalMin, bakeFalseLocalMax, bakeTrueLocalMin, bakeTrueLocalMax;
            public Vector3 falseNoScaleWorldMin, falseNoScaleWorldMax, trueTransformWorldMin, trueTransformWorldMax;
            public float scaleContractMaximumDifference;
            public List<DiagnosticPose> poses = new List<DiagnosticPose>();
        }
        [Serializable] public sealed class TextureRecord
        {
            public string source, path, sourceSha256, type, format, graphicsFormat, normalPacking;
            public bool srgb, androidOverride, mipmaps, opaqueAlpha;
            public int width, height, sourceWidth, sourceHeight, mips, copyMaximumError, maximumRgbError, maximumAlphaError;
            public long runtimeBytes, expectedPayloadBytes; public double meanByteError, rmsByteError;
        }
        [Serializable] public sealed class Report
        {
            public int schemaVersion = 1;
            public string tool = "OceanMarineExpansionBake", utc, unity, directory, state, error;
            public string species = "Atlantic mackerel", uid = Uid, author, license, sourceUrl = SourceUrl, packageSha256 = ArchiveHash;
            public string selectedClip, prefabPath = PrefabPath, animationPolicy = "Original Generic Swim skin animation; no procedural fish deformation or curve removal. Native ASTC4x4 opaque base and lossless RGBA32 AG normal, all mips; verified Unity DXT5nm policy.";
            public bool success, applyRootMotion, animationValidated;
            public int skinnedRenderers, bones, vertices, poseSamples, finalizationUpdates;
            public long triangles, sourceDeclaredTriangles = SourceDeclaredTriangles;
            public float maxVertexTravel, maxCenterTravel, maxHeadTravel, maxPoseDiagonal, loopVertexGap, rootPositionTravel, loopBoneAngle, sampleEndTime;
            public string reviewedThresholdPolicy = "Only this exact licensed mackerel source/Swim: diagnostic-02 CPU close-ups reviewed intact/natural tail flex; BakeMesh(true)+TransformPoint scale contract differs from false+TRS-no-scale by 1.74e-6. Tail travel<=.60 initial body diagonal and AABB-center travel<=.30 allow authored tail bends; head drift<=.08 and posed diagonal<=1.30 prevent whole-body drift/explosion. Original root and near-end vertex/bone loop gates unchanged. Evidence: Validation/sharks-20261017/mackerel-source-clips-02.";
            public Vector2 textureUvScale = Vector2.one;
            public Vector3 boundsMin, boundsMax, animatedBoundsMin, animatedBoundsMax, skinLocalMin, skinLocalMax;
            public Vector3 sourceNoseDirection, sourceUpDirection, headingCorrectionEuler;
            public string sourcePrincipalAxis, headingVerification;
            public string geometryPath = GeometryPath;
            public List<PoseBoundsRecord> posedBounds = new List<PoseBoundsRecord>();
            public List<FileRecord> sourceFiles = new List<FileRecord>();
            public List<FileRecord> protectedFiles = new List<FileRecord>();
            public List<FileRecord> after = new List<FileRecord>();
            public List<string> plannedPaths = new List<string>();
            public List<ClipRecord> clips = new List<ClipRecord>();
            public List<TranslationRecord> translations = new List<TranslationRecord>();
            public List<RotationRecord> rotations = new List<RotationRecord>();
            public List<TextureRecord> textures = new List<TextureRecord>();
        }
        sealed class Pending { public Report report; public Exception error; public List<FileRecord> snapshot; public int ticks, stable; }

        [MenuItem("Swan/Codex/Marine Expansion/Admit original animated mackerel")]
        public static void Run()
        {
            Report report = null;
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode && pending == null, "Edit Mode with no pending admission is required.");
                Require(EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android, "Android must already be the active target; this tool does not switch project settings.");
                Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "A graphics-capable Editor is required; do not use -nographics.");
                Require(SystemInfo.supportsAsyncGPUReadback, "Calibrated ASTC decode requires GPU readback.");
                Require(PlayerSettings.GetNormalMapEncoding(UnityEditor.Build.NamedBuildTarget.Android) == NormalMapEncoding.DXT5nm,
                    "Native URP AG normal encoding requires the existing Android DXT5nm policy. No project setting will be changed.");
                Require(!HasArgument("-quit"), "Do not use -quit; deferred Unity material writes must stabilize before this tool exits.");
                Directory.CreateDirectory(EvidenceRoot);
                Dictionary<string, byte[]> verified = VerifySource(out Receipt receipt);
                if (OwnedFiles().Any())
                {
                    Report previous = ReadLatest();
                    Require(previous.success && previous.state == "admitted", "Existing outputs are not a completed transaction. Inspect or explicitly Rollback before retrying.");
                    VerifyFiles(previous.after);
                    Require(SetEqual(previous.after, Snapshot(OwnedFiles())), "Unknown files in the owned namespace.");
                    ValidateOutputs(previous);
                    Debug.Log("MARINE_EXPANSION_BAKE_UNCHANGED verified original mackerel and all outputs; " + Latest);
                    Exit(0); return;
                }
                report = new Report { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                    directory = Path.Combine(EvidenceRoot, "mackerel-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")),
                    state = "preparing", author = receipt.author, license = receipt.license };
                Directory.CreateDirectory(report.directory);
                foreach (InputFile input in Inputs) report.sourceFiles.Add(new FileRecord { path = Package + "/source-original.zip!/" + input.path, sha256 = input.hash, bytes = input.bytes });
                report.protectedFiles = Snapshot(ProtectedRoots.SelectMany(FilesUnder));
                Save(report);
                MakeFolder(SourceRoot); MakeFolder(BakedRoot); MakeFolder("Assets/Resources/MarineExpansion");
                foreach (InputFile input in Inputs)
                {
                    string path = SourceRoot + "/" + Path.GetFileName(input.path);
                    Plan(path, report); File.WriteAllBytes(path, verified[input.path]);
                    Require(Hash(path) == input.hash, "Staged original changed: " + path);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ConfigureModel(report);
                CalibrateReadback();
                Texture2D color = ImportRuntimeTexture("mackerel_sideview.png", false, report);
                Texture2D normal = ImportRuntimeTexture("mackerel_sideview_normal.png", true, report);
                Material material = CreateMaterial(color, normal, report);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                Require(model != null, "Imported FBX missing.");
                AnimationClip clip = SelectedClip(report);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model); GameObject wrapper = null;
                try
                {
                    instance.name = "Atlantic Mackerel (original animated skin)";
                    Animator animator = instance.GetComponent<Animator>();
                    Require(animator != null && animator.avatar != null && animator.avatar.isValid && !animator.avatar.isHuman, "A valid Generic Animator/avatar is required.");
                    animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.enabled = false;
                    InspectSkinAndAnimation(instance, clip, report);
                    foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                    {
                        Require(renderer is SkinnedMeshRenderer, "Unexpected rigid renderer in the original animated animal.");
                        var materials = renderer.sharedMaterials;
                        Require(materials.Length > 0, "Renderer has no material slots.");
                        for (int i = 0; i < materials.Length; i++) materials[i] = material;
                        renderer.sharedMaterials = materials; renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
                    }
                    string controllerPath = BakedRoot + "/mackerel-swim.controller";
                    Plan(controllerPath, report);
                    AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                    AnimatorState swim = controller.layers[0].stateMachine.AddState("Original Swim"); swim.motion = clip;
                    controller.layers[0].stateMachine.defaultState = swim;
                    animator.runtimeAnimatorController = controller; animator.enabled = true;
                    // Reinstantiation avoids serializing the last sampled bone pose as the prefab rest pose.
                    Object.DestroyImmediate(instance);
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    instance.name = "Atlantic Mackerel";
                    foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                    {
                        var materials = renderer.sharedMaterials; for (int i = 0; i < materials.Length; i++) materials[i] = material;
                        renderer.sharedMaterials = materials; renderer.receiveShadows = true;
                    }
                    animator = instance.GetComponent<Animator>(); animator.applyRootMotion = false;
                    animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    skin.localBounds = new Bounds((report.skinLocalMin + report.skinLocalMax) * .5f, report.skinLocalMax - report.skinLocalMin);
                    // Keep every authored FBX transform/animation under a static, measured correction.
                    // The new unit root is the navigation contract, not an animated model/bone.
                    wrapper = new GameObject("Atlantic Mackerel");
                    var correction = new GameObject("Original FBX static heading correction"); correction.transform.SetParent(wrapper.transform, false);
                    Quaternion heading = Quaternion.Inverse(Quaternion.LookRotation(report.sourceNoseDirection, report.sourceUpDirection));
                    correction.transform.localRotation = heading;
                    correction.transform.localPosition = -(heading * ((report.boundsMin + report.boundsMax) * .5f));
                    report.headingCorrectionEuler = heading.eulerAngles;
                    instance.transform.SetParent(correction.transform, false);
                    // Renderer custom bounds are not serialized in a prefab. Seal the sampled envelope
                    // as explicit runtime data instead of treating template.localBounds as persistent metadata.
                    OceanAuthoredFishGeometry geometry = ExpectedGeometry(report);
                    Plan(GeometryPath, report); File.WriteAllText(GeometryPath, JsonUtility.ToJson(geometry, true));
                    AssetDatabase.ImportAsset(GeometryPath, ImportAssetOptions.ForceSynchronousImport);
                    Plan(PrefabPath, report);
                    Require(PrefabUtility.SaveAsPrefabAsset(wrapper, PrefabPath) != null, "Prefab save failed.");
                }
                finally { if (wrapper != null) Object.DestroyImmediate(wrapper); else if (instance != null) Object.DestroyImmediate(instance); }
                AssetDatabase.SaveAssets(); ValidateOutputs(report);
                BeginFinalization(report, null);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (report != null) BeginFinalization(report, exception); else Exit(1);
                if (!Application.isBatchMode && report == null) throw;
            }
        }

        static Dictionary<string, byte[]> VerifySource(out Receipt receipt)
        {
            string receiptPath = Package + "/receipt.json", archivePath = Package + "/source-original.zip";
            Require(File.Exists(receiptPath) && File.Exists(archivePath), "Canonical receipt/archive missing.");
            receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(receiptPath));
            Require(receipt != null && receipt.uid == Uid && receipt.source_url == SourceUrl && receipt.license == "CC-BY-4.0" &&
                receipt.license_url == "https://creativecommons.org/licenses/by/4.0/" && receipt.author == "Ishxxn (@1shxxn)", "License/provenance differs from the reviewed original.");
            Require(receipt.sha256 == ArchiveHash && receipt.bytes == 1018110 && new FileInfo(archivePath).Length == receipt.bytes && Hash(archivePath) == ArchiveHash,
                "Original package SHA256/size mismatch.");
            Require(receipt.roster != null && receipt.roster.Length == Inputs.Length && receipt.roster.Select(x => x.path).Distinct(StringComparer.Ordinal).Count() == Inputs.Length,
                "Receipt is not a closed unique three-file roster.");
            foreach (InputFile input in Inputs)
                Require(receipt.roster.Any(x => x.path == input.path && x.bytes == input.bytes && x.crc == input.crc), "Receipt member differs: " + input.path);
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                Require(archive.Entries.Count == Inputs.Length, "Archive contains unreviewed members, directories, or duplicates.");
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    InputFile input = Inputs.SingleOrDefault(x => x.path == entry.FullName);
                    Require(input != null && entry.Length == input.bytes && !result.ContainsKey(entry.FullName), "Unexpected original archive entry: " + entry.FullName);
                    Require(!entry.FullName.Contains("..") && !entry.FullName.Contains("\\") && !Path.IsPathRooted(entry.FullName), "Unsafe archive path.");
                    byte[] bytes;
                    using (Stream stream = entry.Open()) using (var memory = new MemoryStream()) { stream.CopyTo(memory); bytes = memory.ToArray(); }
                    Require(bytes.LongLength == input.bytes && Hash(bytes) == input.hash && Crc32(bytes) == input.crc, "Member CRC/SHA256 mismatch: " + input.path);
                    result.Add(input.path, bytes);
                }
            }
            return result;
        }

        /// <summary>Read-only canonical-byte diagnostic. No import, AssetDatabase mutation, or admission.</summary>
        public static void ProbeTextureDecode()
        {
            try
            {
                Dictionary<string, byte[]> verified = VerifySource(out _);
                var report = new DecodeProbeReport { unity = Application.unityVersion, utc = DateTime.UtcNow.ToString("O") };
                foreach (InputFile input in Inputs.Where(x => x.path.EndsWith(".png", StringComparison.Ordinal)))
                {
                    bool normal = input.path.Contains("_normal");
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, normal);
                    try
                    {
                        bool loaded = ImageConversion.LoadImage(texture, verified[input.path], false);
                        var record = new DecodeProbeRecord { source = Package + "/source-original.zip!/" + input.path,
                            sha256 = Hash(verified[input.path]), normal = normal, loaded = loaded,
                            format = texture.format.ToString(), graphicsFormat = texture.graphicsFormat.ToString(),
                            srgb = GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat), readable = texture.isReadable,
                            width = texture.width, height = texture.height, mips = texture.mipmapCount };
                        if (loaded && texture.isReadable)
                        {
                            record.rawBytes = texture.GetRawTextureData<byte>().Length;
                            Color32[] colors = texture.GetPixels32(); record.color32Count = colors.Length;
                            var rgba = new byte[colors.Length * 4];
                            for (int i = 0; i < colors.Length; i++)
                            {
                                rgba[i * 4] = colors[i].r; rgba[i * 4 + 1] = colors[i].g; rgba[i * 4 + 2] = colors[i].b; rgba[i * 4 + 3] = colors[i].a;
                                record.alphaMin = Math.Min(record.alphaMin, colors[i].a); record.alphaMax = Math.Max(record.alphaMax, colors[i].a);
                            }
                            record.rgbaSha256 = Hash(rgba);
                        }
                        report.textures.Add(record);
                        Debug.Log("MARINE_TEXTURE_DECODE_PROBE " + JsonUtility.ToJson(record));
                    }
                    finally { Object.DestroyImmediate(texture); }
                }
                Directory.CreateDirectory(EvidenceRoot);
                File.WriteAllText(Path.Combine(EvidenceRoot, "decode-probe-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + ".json"), JsonUtility.ToJson(report, true));
                Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); Exit(1); if (!Application.isBatchMode) throw; }
        }

        /// <summary>Fixed-camera source-clip diagnosis only. Never alters source, clip, material or admission records.</summary>
        public static void DiagnosticSourceClip()
        {
            Scene scene = default; RenderTexture target = null; Texture2D pixels = null; Mesh mesh = null;
            var snapshotMeshes = new List<Mesh>(); var snapshotObjects = new List<GameObject>();
            RenderTexture previous = RenderTexture.active;
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Edit Mode with graphics is required.");
                VerifySource(out _); Report sourceReport = ReadLatest(); VerifyFiles(sourceReport.after);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                var material = AssetDatabase.LoadAssetAtPath<Material>(BakedRoot + "/mackerel-urp.mat");
                Require(model != null && material != null, "Retained failed-transaction source/material required; do not roll back before diagnosis.");
                string output = Path.GetFullPath(Argument("-oceanMarineDiagnosticOutput", "Validation/sharks-20261017/mackerel-source-clip-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")));
                Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "Use an empty diagnostic output directory."); Directory.CreateDirectory(output);
                scene = EditorSceneManager.NewPreviewScene();
                var wrapper = new GameObject("Source clip diagnostic normalization"); SceneManager.MoveGameObjectToScene(wrapper, scene);
                Require((sourceReport.boundsMax - sourceReport.boundsMin).sqrMagnitude > .000001f, "Source measured bounds missing.");
                var correction = new GameObject("Measured anatomical orientation"); correction.transform.SetParent(wrapper.transform, false);
                Quaternion heading = Quaternion.Inverse(Quaternion.LookRotation(sourceReport.sourceNoseDirection, sourceReport.sourceUpDirection));
                correction.transform.localRotation = heading; correction.transform.localPosition = -(heading * ((sourceReport.boundsMin + sourceReport.boundsMax) * .5f));
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene); instance.transform.SetParent(correction.transform, false);
                foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true)) { animator.enabled = false; animator.applyRootMotion = false; }
                var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true); Require(skin != null, "Original skin missing.");
                var report = new DiagnosticReport { unity = Application.unityVersion, skinLocalScale = skin.transform.localScale,
                    skinLossyScale = skin.transform.lossyScale, originalRendererMin = skin.bounds.min, originalRendererMax = skin.bounds.max };
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true)) { var mats = renderer.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = material; renderer.sharedMaterials = mats; }
                skin.updateWhenOffscreen = true; skin.localBounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
                var cameraHost = new GameObject("Source clip diagnostic camera"); SceneManager.MoveGameObjectToScene(cameraHost, scene);
                Camera camera = cameraHost.AddComponent<Camera>(); camera.scene = scene; camera.enabled = false; camera.orthographic = true;
                camera.orthographicSize = .70f; camera.aspect = 16f / 9f; camera.nearClipPlane = .001f; camera.farClipPlane = 20f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.09f, .12f, .15f); camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.transform.position = new Vector3(1.2f, .35f, .8f); camera.transform.LookAt(Vector3.zero);
                var data = camera.GetUniversalAdditionalCameraData(); data.allowXRRendering = false; data.renderPostProcessing = false;
                for (int i = 0; i < 2; i++)
                {
                    var host = new GameObject(i == 0 ? "Source key" : "Source fill"); SceneManager.MoveGameObjectToScene(host, scene);
                    Light light = host.AddComponent<Light>(); light.type = LightType.Directional; light.color = Color.white; light.intensity = i == 0 ? 1.6f : .75f; light.shadows = LightShadows.None;
                    light.transform.rotation = Quaternion.Euler(i == 0 ? new Vector3(35, -40, 0) : new Vector3(-25, 140, 0));
                }
                target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false); mesh = new Mesh();
                Bounds union = default; bool unionSet = false;
                AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(x => !x.name.StartsWith("__preview__", StringComparison.Ordinal) &&
                    (x.name.IndexOf("Swim", StringComparison.OrdinalIgnoreCase) >= 0 || x.name.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
                Require(clips.Length == 2, "Expected original Swim and Idle clips.");
                foreach (AnimationClip clip in clips)
                {
                    clip.SampleAnimation(instance, 0f); Quaternion[] first = skin.bones.Select(x => x.localRotation).ToArray();
                    string folder = Path.Combine(output, clip.name.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0 ? "idle" : "swim"); Directory.CreateDirectory(folder);
                    float[] times = { 0f, clip.length * .25f, clip.length * .50f, clip.length * .75f, clip.length - .0001f };
                    for (int frame = 0; frame < times.Length; frame++)
                    {
                        clip.SampleAnimation(instance, times[frame]);
                        if (report.poses.Count == 0)
                        {
                            skin.BakeMesh(mesh, false); report.bakeFalseLocalMin = mesh.bounds.min; report.bakeFalseLocalMax = mesh.bounds.max;
                            Vector3[] noScale = mesh.vertices; Matrix4x4 noScaleMatrix = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                            for (int i = 0; i < noScale.Length; i++) noScale[i] = noScaleMatrix.MultiplyPoint3x4(noScale[i]);
                            Bounds noScaleBounds = BoundsOf(noScale); report.falseNoScaleWorldMin = noScaleBounds.min; report.falseNoScaleWorldMax = noScaleBounds.max;
                            skin.BakeMesh(mesh, true); report.bakeTrueLocalMin = mesh.bounds.min; report.bakeTrueLocalMax = mesh.bounds.max;
                            Vector3[] compensated = mesh.vertices;
                            for (int i = 0; i < compensated.Length; i++) { compensated[i] = skin.transform.TransformPoint(compensated[i]); report.scaleContractMaximumDifference = Mathf.Max(report.scaleContractMaximumDifference, Vector3.Distance(compensated[i], noScale[i])); }
                            Bounds compensatedBounds = BoundsOf(compensated); report.trueTransformWorldMin = compensatedBounds.min; report.trueTransformWorldMax = compensatedBounds.max;
                        }
                        skin.BakeMesh(mesh, true);
                        Vector3[] vertices = mesh.vertices; for (int i = 0; i < vertices.Length; i++) vertices[i] = skin.transform.TransformPoint(vertices[i]);
                        Bounds bounds = BoundsOf(vertices); Vector2[] uv = skin.sharedMesh.uv;
                        if (!unionSet) { union = bounds; unionSet = true; } else union.Encapsulate(bounds);
                        Vector3 head = UvCenter(vertices, uv, p => p.x < .12f && p.y > .25f && p.y < .75f), tail = UvCenter(vertices, uv, p => p.x > .87f);
                        var pose = new DiagnosticPose { clip = clip.name, time = times[frame], boundsMin = bounds.min, boundsMax = bounds.max,
                            noseDirection = (head - tail).normalized, image = Path.Combine(folder, "phase-" + frame + ".png") };
                        for (int i = 0; i < skin.bones.Length; i++) pose.bones.Add(new DiagnosticBone { path = AnimationUtility.CalculateTransformPath(skin.bones[i], instance.transform),
                            position = skin.bones[i].position, rotation = skin.bones[i].rotation, angleFromFirst = Quaternion.Angle(first[i], skin.bones[i].localRotation) });
                        Mesh snapshot = Object.Instantiate(mesh); snapshot.name = "Measured " + clip.name + " " + frame; snapshot.vertices = vertices;
                        Vector3[] normals = snapshot.normals; Matrix4x4 normalMatrix = skin.transform.localToWorldMatrix.inverse.transpose;
                        for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized; snapshot.normals = normals;
                        Vector4[] tangents = snapshot.tangents;
                        for (int i = 0; i < tangents.Length; i++) { Vector3 t = skin.transform.TransformVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized; tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w * (skin.transform.localToWorldMatrix.determinant < 0 ? -1f : 1f)); }
                        snapshot.tangents = tangents; snapshot.RecalculateBounds(); snapshotMeshes.Add(snapshot);
                        var host = new GameObject("Measured pose " + report.poses.Count); SceneManager.MoveGameObjectToScene(host, scene);
                        host.AddComponent<MeshFilter>().sharedMesh = snapshot; host.AddComponent<MeshRenderer>().sharedMaterial = material; host.SetActive(false); snapshotObjects.Add(host);
                        report.poses.Add(pose);
                        Debug.Log("[MarineSourceClip] clip=" + clip.name + " t=" + times[frame] + " bounds=" + bounds + " nose=" + pose.noseDirection + " root=" + instance.transform.position);
                    }
                }
                skin.enabled = false;
                Vector3 direction = new Vector3(1.2f, .35f, .8f).normalized;
                camera.transform.position = union.center + direction * Mathf.Max(1f, union.extents.magnitude * 2.5f);
                camera.transform.LookAt(union.center); camera.farClipPlane = Mathf.Max(20f, union.size.magnitude * 5f);
                float fit = 0f;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 point = union.center + Vector3.Scale(union.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 delta = point - union.center;
                    fit = Mathf.Max(fit, Mathf.Abs(Vector3.Dot(delta, camera.transform.up)), Mathf.Abs(Vector3.Dot(delta, camera.transform.right)) / camera.aspect);
                }
                camera.orthographicSize = Mathf.Max(.001f, fit * 1.12f);
                for (int i = 0; i < report.poses.Count; i++)
                {
                    snapshotObjects[i].SetActive(true);
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply(false, false);
                    File.WriteAllBytes(report.poses[i].image, pixels.EncodeToPNG()); snapshotObjects[i].SetActive(false);
                }
                Debug.Log("[MarineSourceClip] sourceScale local=" + report.skinLocalScale + " lossy=" + report.skinLossyScale + " false/noScale versus true/TransformPoint max=" + report.scaleContractMaximumDifference);
                File.WriteAllText(Path.Combine(output, "metrics.json"), JsonUtility.ToJson(report, true));
                Debug.Log("MARINE_SOURCE_CLIP_DIAGNOSTIC_COMPLETE " + output);
            }
            catch (Exception exception) { Debug.LogException(exception); Exit(1); if (!Application.isBatchMode) throw; return; }
            finally
            {
                RenderTexture.active = previous; if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                foreach (Mesh snapshot in snapshotMeshes) if (snapshot != null) Object.DestroyImmediate(snapshot);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); } if (pixels != null) Object.DestroyImmediate(pixels); if (mesh != null) Object.DestroyImmediate(mesh);
            }
            Exit(0);
        }

        static void ConfigureModel(Report report)
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Require(importer != null, "FBX ModelImporter missing.");
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true; importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.resampleCurves = true; importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.importBlendShapes = true; importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.useFileScale = true; importer.globalScale = 1f;
            importer.skinWeights = ModelImporterSkinWeights.Custom; importer.maxBonesPerVertex = 32; importer.minBoneWeight = 0f;
            importer.SaveAndReimport();
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            Require(clips != null && clips.Length == 3 && clips.Count(x => x.name.IndexOf("Swim", StringComparison.OrdinalIgnoreCase) >= 0) == 1,
                "Original expected Death/Idle/Swim clip roster changed.");
            foreach (var clip in clips)
            {
                bool swim = clip.name.IndexOf("Swim", StringComparison.OrdinalIgnoreCase) >= 0;
                clip.loopTime = swim; clip.loopPose = false;
                // Keep source root/position tracks unchanged; the sampled motion gates decide admission.
            }
            importer.clipAnimations = clips; importer.SaveAndReimport();
            Require(importer.animationType == ModelImporterAnimationType.Generic && !importer.optimizeGameObjects && importer.importAnimation, "Animation import contract changed.");
        }

        static Texture2D ImportRuntimeTexture(string name, bool normal, Report report)
        {
            string source = SourceRoot + "/" + name;
            string path = BakedRoot + (normal ? "/mackerel_normal_lossless.asset" : "/mackerel_basecolor_astc.asset");
            Plan(path, report);
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, normal);
            Texture2D compressed = null;
            try
            {
                // Decode canonical PNG bytes directly. The project-wide TextureImportPostprocessor
                // changes imported PNG normal/type/max-size settings, so imported artifacts are not conversion inputs.
                Require(ImageConversion.LoadImage(decoded, File.ReadAllBytes(source), false), "Original PNG decode failed.");
                // Unity 6 LoadImage documents PNG output as ARGB32, regardless of the constructor format.
                // GetPixels32 below normalizes the channel order before the separately calibrated RGBA32 ASTC copy.
                // https://docs.unity3d.com/cn/6000.0/ScriptReference/ImageConversion.LoadImage.html
                Debug.Log("[MarineExpansion] canonical PNG decode " + name + " " + decoded.width + "x" + decoded.height +
                    " format=" + decoded.format + " graphics=" + decoded.graphicsFormat + " mips=" + decoded.mipmapCount + " readable=" + decoded.isReadable);
                Require(decoded.width == 800 && decoded.height == 534 && (decoded.format == TextureFormat.ARGB32 || decoded.format == TextureFormat.RGBA32),
                    "Reviewed original image dimensions/encoding changed: " + name + " actual=" + decoded.width + "x" + decoded.height +
                    " format=" + decoded.format + " graphics=" + decoded.graphicsFormat + " mips=" + decoded.mipmapCount + " readable=" + decoded.isReadable);
                int width = (decoded.width + 3) / 4 * 4, height = (decoded.height + 3) / 4 * 4;
                var pixels = new Color32[width * height]; Color32[] original = decoded.GetPixels32();
                Require(original.Length == 800 * 534, "Canonical decoded Color32 pixel count changed.");
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    Color32 value = original[Math.Min(y, decoded.height - 1) * decoded.width + Math.Min(x, decoded.width - 1)];
                    // URP Packing.hlsl: Android ASTC DXT5nm uses AG; desktop DXT5nm uses
                    // alpha*R/G. R=1, G=Y, A=X gives the same normal in both branches.
                    // Opaque URP Lit exports alpha=1; neither alpha clip nor albedo-alpha smoothness is enabled.
                    // Preserve original RGB and derive a constant opaque alpha, leaving the licensed PNG untouched.
                    pixels[y * width + x] = normal ? new Color32(255, value.g, 0, value.r) : new Color32(value.r, value.g, value.b, 255);
                }
                compressed = new Texture2D(width, height, TextureFormat.RGBA32, true, normal)
                { name = normal ? "Mackerel original normal AG lossless RGBA32" : "Mackerel original sRGB ASTC4x4",
                    filterMode = FilterMode.Trilinear, anisoLevel = 16, wrapMode = TextureWrapMode.Clamp };
                compressed.SetPixels32(pixels); compressed.Apply(true, false);
                int mips = 1 + (int)Math.Floor(Math.Log(Math.Max(width, height), 2));
                Require(compressed.mipmapCount == mips && mips == 10, "Full native mip chain required.");
                var before = new byte[mips][];
                var record = new TextureRecord { source = source, path = path, sourceSha256 = Hash(source), type = "NativeTexture2D",
                    srgb = !normal, androidOverride = false, mipmaps = true, opaqueAlpha = !normal, width = width, height = height,
                    sourceWidth = decoded.width, sourceHeight = decoded.height, mips = mips,
                    normalPacking = normal ? "Lossless RGBA32=(1,Y,0,X), all mips/nonreadable; original normal XY retained exactly (copy/decode error<=1); URP AG and RGorAG decode identical; Android NormalMapEncoding.DXT5nm required" :
                        "Original sRGB RGB retained; derived alpha255 for verified opaque URP Lit (no clip or albedo-alpha smoothness); source PNG unchanged. 2 edge rows padded, UV y scaled534/536; no resample" };
                for (int mip = 0; mip < mips; mip++)
                {
                    before[mip] = compressed.GetPixelData<byte>(mip).ToArray();
                    byte[] sampled = ReadPixels(compressed, mip, !normal);
                    record.copyMaximumError = Math.Max(record.copyMaximumError, MaxError(before[mip], sampled));
                    int w = Math.Max(1, width >> mip), h = Math.Max(1, height >> mip);
                    record.expectedPayloadBytes += normal ? (long)w * h * 4 : (long)((w + 3) / 4) * ((h + 3) / 4) * 16;
                }
                Require(record.copyMaximumError <= 1, "RGBA copy/color-space/mip calibration changed decoded source bytes.");
                if (!normal) EditorUtility.CompressTexture(compressed, TextureFormat.ASTC_4x4, TextureCompressionQuality.Best);
                compressed.Apply(false, false);
                record.runtimeBytes = compressed.GetRawTextureData<byte>().Length;
                TextureFormat expectedFormat = normal ? TextureFormat.RGBA32 : TextureFormat.ASTC_4x4;
                Require(compressed.format == expectedFormat && compressed.mipmapCount == mips && record.runtimeBytes == record.expectedPayloadBytes &&
                    GraphicsFormatUtility.IsSRGBFormat(compressed.graphicsFormat) == !normal, "Native texture codec/payload/mip/color encoding failed.");
                double absolute = 0, squared = 0; long count = 0;
                for (int mip = 0; mip < mips; mip++)
                {
                    byte[] after = ReadPixels(compressed, mip, !normal);
                    Require(after.Length == before[mip].Length, "ASTC readback dimensions changed.");
                    for (int i = 0; i < after.Length; i++)
                    {
                        int error = Math.Abs(after[i] - before[mip][i]); absolute += error; squared += error * error; count++;
                        if ((i & 3) == 3) record.maximumAlphaError = Math.Max(record.maximumAlphaError, error);
                        else record.maximumRgbError = Math.Max(record.maximumRgbError, error);
                    }
                }
                record.meanByteError = absolute / count; record.rmsByteError = Math.Sqrt(squared / count);
                Require(normal ? record.maximumRgbError <= 1 && record.maximumAlphaError <= 1 :
                    record.meanByteError <= 8 && record.rmsByteError <= 12 && record.maximumAlphaError <= 16,
                    "Native texture quality gate failed: " + name + " codec=" + expectedFormat + " mean=" + record.meanByteError + " rms=" + record.rmsByteError + " alpha=" + record.maximumAlphaError);
                record.format = compressed.format.ToString(); record.graphicsFormat = compressed.graphicsFormat.ToString();
                report.textureUvScale = new Vector2(decoded.width / (float)width, decoded.height / (float)height);
                compressed.Apply(false, true); AssetDatabase.CreateAsset(compressed, path); report.textures.Add(record); Save(report);
                return compressed;
            }
            finally { Object.DestroyImmediate(decoded); if (compressed != null && !AssetDatabase.Contains(compressed)) Object.DestroyImmediate(compressed); }
        }

        static Material CreateMaterial(Texture2D color, Texture2D normal, Report report)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Require(shader != null && shader.isSupported, "URP Lit is required.");
            var material = new Material(shader) { name = "Atlantic Mackerel original PBR" };
            material.SetTexture("_BaseMap", color); material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", normal); material.SetFloat("_BumpScale", 1f); material.EnableKeyword("_NORMALMAP");
            material.SetTextureScale("_BaseMap", report.textureUvScale); material.SetTextureScale("_BumpMap", report.textureUvScale);
            material.SetFloat("_Metallic", 0f); material.SetFloat("_Smoothness", .45f);
            material.SetFloat("_Surface", 0f); material.SetFloat("_AlphaClip", 0f); material.SetFloat("_Cull", (float)CullMode.Back);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            material.SetFloat("_ZWrite", 1f); material.SetFloat("_QueueControl", 1f); material.renderQueue = (int)RenderQueue.Geometry;
            string path = BakedRoot + "/mackerel-urp.mat"; Plan(path, report); AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static AnimationClip SelectedClip(Report report)
        {
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(x => !x.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            Require(clips.Length == 3, "Imported clip roster must retain the original three source clips.");
            var selected = clips.Where(x => x.name.IndexOf("Swim", StringComparison.OrdinalIgnoreCase) >= 0 && x.name.IndexOf("Death", StringComparison.OrdinalIgnoreCase) < 0).ToArray();
            Require(selected.Length == 1, "Exactly one original Swim clip required.");
            AnimationClip swim = selected[0];
            Require(swim.length > .8f && swim.length < 1.2f && swim.isLooping && !swim.legacy, "Original ~1-second Generic swim loop required.");
            foreach (AnimationClip clip in clips)
                report.clips.Add(new ClipRecord { name = clip.name, length = clip.length, frameRate = clip.frameRate,
                    looping = clip.isLooping, selected = clip == swim, curves = AnimationUtility.GetCurveBindings(clip).Length });
            report.selectedClip = swim.name; return swim;
        }

        static void InspectSkinAndAnimation(GameObject instance, AnimationClip clip, Report report)
        {
            var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Require(skins.Length == 1, "Expected one actual animated skin; no rigid/static substitute.");
            SkinnedMeshRenderer skin = skins[0]; Mesh source = skin.sharedMesh;
            Require(source != null && source.isReadable && source.vertexCount == ReviewedImportedVertices && skin.bones.Length == ReviewedSkinBones && source.bindposes.Length == skin.bones.Length && skin.rootBone != null,
                "Original skin, bones, bind poses or root bone missing.");
            Require(source.uv.Length == source.vertexCount && source.normals.Length == source.vertexCount && source.tangents.Length == source.vertexCount,
                "Original UVs/normals or calculated normal-map tangents missing.");
            long triangles = 0; for (int submesh = 0; submesh < source.subMeshCount; submesh++) { Require(source.GetTopology(submesh) == MeshTopology.Triangles, "Non-triangle source topology."); triangles += (long)source.GetIndexCount(submesh) / 3; }
            Require(triangles == ReviewedImportedTriangles,
                "Imported geometry differs from the actual reviewed Unity mesh; declared FBX=" + SourceDeclaredTriangles + " reviewed Unity=" + ReviewedImportedTriangles + " current=" + triangles);
            if (triangles != SourceDeclaredTriangles) Debug.LogWarning("[MarineExpansion] FBX declares " + SourceDeclaredTriangles +
                " triangles by polygon fan arithmetic; Unity imported " + triangles + ". Self-intersection discards remain a rendered close-up inspection requirement, not an identical-geometry claim.");
            report.skinnedRenderers = skins.Length; report.bones = skin.bones.Length; report.vertices = source.vertexCount; report.triangles = triangles;
            var baked = new Mesh { name = "Mackerel admission pose sample" };
            try
            {
                // Unity 6 useScale=true compensates for the renderer transform scale before TransformPoint.
                // The source-specific diagnostic proved false+TRS(no scale) and true+TransformPoint agree.
                clip.SampleAnimation(instance, 0f); skin.BakeMesh(baked, true);
                Vector3[] first = WorldVertices(instance.transform, skin.transform, baked.vertices);
                Bounds firstBounds = BoundsOf(first); float diagonal = firstBounds.size.magnitude;
                Require(diagonal > .005f && diagonal < 100f, "Implausible imported world geometry scale.");
                report.boundsMin = firstBounds.min; report.boundsMax = firstBounds.max;
                Vector3 size = firstBounds.size;
                report.sourcePrincipalAxis = size.x >= size.y && size.x >= size.z ? "X" : size.y >= size.z ? "Y" : "Z";
                // The reviewed original PNG is a side profile: head at image left, tail right,
                // dorsal fins above and belly below. This is a measured basis, still subject to rendered preview.
                Vector2[] uv = source.uv;
                Vector3 head = UvCenter(first, uv, p => p.x < .12f && p.y > .25f && p.y < .75f);
                Vector3 tail = UvCenter(first, uv, p => p.x > .87f);
                Vector3 dorsal = UvCenter(first, uv, p => p.y > .70f);
                Vector3 belly = UvCenter(first, uv, p => p.y < .30f);
                Vector3 nose = (head - tail).normalized;
                Vector3 up = Vector3.ProjectOnPlane(dorsal - belly, nose).normalized;
                Require(nose.sqrMagnitude > .9f && up.sqrMagnitude > .9f && Mathf.Abs(Vector3.Dot(nose, up)) < .01f,
                    "Cannot establish anatomical heading from reviewed original UV profile.");
                report.sourceNoseDirection = nose; report.sourceUpDirection = up;
                report.headingVerification = "Original profile PNG visually reviewed: head left, tail right, dorsal top; measured skin UV-region centroids define +Z nose/+Y dorsal on an unscaled wrapper. Rendered nose/swim acceptance still required.";
                Bounds all = firstBounds, localEnvelope = BoundsOf(baked.vertices); Vector3 root = instance.transform.position;
                Quaternion[] firstBoneRotations = skin.bones.Select(x => x.localRotation).ToArray();
                report.sampleEndTime = clip.length - Mathf.Min(.0001f, clip.length * .0001f);
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(Transform) || !binding.propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal)) continue;
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    Require(curve != null && curve.length > 0, "Empty position curve.");
                    Transform bone = string.IsNullOrEmpty(binding.path) ? instance.transform : instance.transform.Find(binding.path);
                    Require(bone != null, "Animation transform path missing: " + binding.path);
                    float min = float.PositiveInfinity, max = float.NegativeInfinity;
                    for (int i = 0; i <= 120; i++) { float value = curve.Evaluate(report.sampleEndTime * i / 120f); Require(Finite(value), "Nonfinite position key."); min = Mathf.Min(min, value); max = Mathf.Max(max, value); }
                    float scale = bone.parent == null ? 1f : Mathf.Max(Mathf.Abs(bone.parent.lossyScale.x), Mathf.Abs(bone.parent.lossyScale.y), Mathf.Abs(bone.parent.lossyScale.z));
                    float range = (max - min) * scale, end = Mathf.Abs(curve.Evaluate(report.sampleEndTime) - curve.Evaluate(0f)) * scale;
                    bool navigationRoot = bone == instance.transform || skin.rootBone.IsChildOf(bone);
                    report.translations.Add(new TranslationRecord { path = binding.path, property = binding.propertyName, range = range, endpointDelta = end,
                        worldScale = scale, navigationRoot = navigationRoot });
                    Require(range <= diagonal * .15f && end <= diagonal * .015f,
                        "Unsafe animated bone locomotion/loop; no curves removed: " + binding.path + " " + binding.propertyName + " range=" + range + " endpoint=" + end + " body=" + diagonal);
                }
                report.poseSamples = 61;
                // Sampling clip.length can wrap a looping clip to zero and conceal a discontinuity.
                for (int sample = 0; sample <= 60; sample++)
                {
                    clip.SampleAnimation(instance, report.sampleEndTime * sample / 60f); skin.BakeMesh(baked, true);
                    Vector3[] vertices = WorldVertices(instance.transform, skin.transform, baked.vertices);
                    Require(vertices.Length == first.Length, "Animation changed skin topology.");
                    Bounds bounds = BoundsOf(vertices); all.Encapsulate(bounds); localEnvelope.Encapsulate(BoundsOf(baked.vertices));
                    report.posedBounds.Add(new PoseBoundsRecord { min = bounds.min, max = bounds.max });
                    Vector3 currentHead = UvCenter(vertices, uv, p => p.x < .12f && p.y > .25f && p.y < .75f);
                    report.maxHeadTravel = Mathf.Max(report.maxHeadTravel, Vector3.Distance(currentHead, head));
                    report.maxPoseDiagonal = Mathf.Max(report.maxPoseDiagonal, bounds.size.magnitude);
                    report.maxCenterTravel = Mathf.Max(report.maxCenterTravel, Vector3.Distance(bounds.center, firstBounds.center));
                    report.rootPositionTravel = Mathf.Max(report.rootPositionTravel, Vector3.Distance(root, instance.transform.position));
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        float moved = Vector3.Distance(first[i], vertices[i]); report.maxVertexTravel = Mathf.Max(report.maxVertexTravel, moved);
                        if (sample == 60) report.loopVertexGap = Mathf.Max(report.loopVertexGap, moved);
                    }
                    foreach (Transform bone in skin.bones) Require(bone != null && Finite(bone.localPosition) && Finite(bone.localRotation), "Invalid or nonfinite posed bone.");
                }
                for (int i = 0; i < skin.bones.Length; i++)
                {
                    float angle = Quaternion.Angle(firstBoneRotations[i], skin.bones[i].localRotation);
                    report.loopBoneAngle = Mathf.Max(report.loopBoneAngle, angle);
                    report.rotations.Add(new RotationRecord { path = AnimationUtility.CalculateTransformPath(skin.bones[i], instance.transform), loopAngle = angle });
                }
                Require(report.maxVertexTravel > diagonal * .002f && report.maxVertexTravel <= diagonal * .60f && report.maxCenterTravel <= diagonal * .30f,
                    "Swim exceeds the reviewed original mackerel tail-bend envelope.");
                Require(report.maxHeadTravel <= diagonal * .08f && report.maxPoseDiagonal <= diagonal * 1.30f,
                    "Original mackerel head drifts or body envelope expands beyond the reviewed intact swim.");
                Require(report.loopVertexGap <= diagonal * .02f && report.rootPositionTravel <= diagonal * .005f,
                    "Swim is not an in-place seamless loop; reject rather than hide translation.");
                Require(report.loopBoneAngle <= 6f, "Bone rotation endpoint discontinuity: " + report.loopBoneAngle + " degrees.");
                report.animatedBoundsMin = all.min; report.animatedBoundsMax = all.max;
                report.skinLocalMin = localEnvelope.min; report.skinLocalMax = localEnvelope.max;
                report.animationValidated = true; report.applyRootMotion = false;
                Debug.Log("[MarineExpansion] original Swim pose PASS samples=" + report.poseSamples + " triangles=" + triangles + " bones=" + report.bones +
                    " bounds=" + firstBounds + " maxDeform=" + report.maxVertexTravel + " headTravel=" + report.maxHeadTravel + " maxPoseDiagonal=" + report.maxPoseDiagonal +
                    " loopGap=" + report.loopVertexGap + " rootTravel=" + report.rootPositionTravel);
            }
            finally { Object.DestroyImmediate(baked); }
        }

        static void ValidateOutputs(Report report)
        {
            Require(report.animationValidated && report.textures.Count == 2 && report.sourceDeclaredTriangles == SourceDeclaredTriangles &&
                report.triangles == ReviewedImportedTriangles && report.vertices == ReviewedImportedVertices && report.bones == ReviewedSkinBones && report.poseSamples == 61 &&
                report.sampleEndTime > 0f && report.loopBoneAngle <= 6f && report.rotations.Count == report.bones, "Animation/texture admission gates incomplete.");
            float diagonal = (report.boundsMax - report.boundsMin).magnitude;
            Require(report.maxVertexTravel > diagonal * .002f && report.maxVertexTravel <= diagonal * .60f && report.maxCenterTravel <= diagonal * .30f &&
                report.maxHeadTravel <= diagonal * .08f && report.maxPoseDiagonal <= diagonal * 1.30f && report.loopVertexGap <= diagonal * .02f && report.rootPositionTravel <= diagonal * .005f,
                "Reviewed source-specific tail/head/loop admission envelope changed.");
            Require(report.geometryPath == GeometryPath && report.posedBounds.Count == 61, "Sealed per-pose geometry metadata is incomplete.");
            var metadata = AssetDatabase.LoadAssetAtPath<TextAsset>(GeometryPath);
            Require(metadata != null, "Runtime geometry metadata is missing.");
            var geometry = JsonUtility.FromJson<OceanAuthoredFishGeometry>(metadata.text);
            OceanAuthoredFishGeometry expectedGeometry = ExpectedGeometry(report);
            Require(geometry != null && geometry.IsValid && geometry.schemaVersion == 1 && geometry.sourceMeshSha256 == Hash(ModelPath) &&
                geometry.sourceMeshSha256 == expectedGeometry.sourceMeshSha256 && Mathf.Abs(geometry.firstPoseDiagonal - expectedGeometry.firstPoseDiagonal) <= .00001f &&
                Vector3.Distance(geometry.navigationCenter, expectedGeometry.navigationCenter) <= .00001f && Vector3.Distance(geometry.navigationSize, expectedGeometry.navigationSize) <= .00001f,
                "Runtime geometry metadata differs from exact source SHA/61 pose bounds/heading/2-percent-margin contract.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(prefab != null && prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 1, "Runtime animated prefab missing.");
            var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Require(skin.sharedMesh != null && skin.sharedMesh.vertexCount == ReviewedImportedVertices && skin.bones.Length == ReviewedSkinBones &&
                Enumerable.Range(0, skin.sharedMesh.subMeshCount).Sum(i => (long)skin.sharedMesh.GetIndexCount(i) / 3) == ReviewedImportedTriangles,
                "Runtime mesh differs from the reviewed exact 5986-triangle/3354-vertex/16-bone source import.");
            Require(prefab.transform.localScale == Vector3.one && Quaternion.Angle(prefab.transform.localRotation, Quaternion.identity) < .001f,
                "Navigation wrapper must retain unit scale and identity (+Z forward) rotation.");
            var animator = prefab.GetComponentInChildren<Animator>(true);
            Require(animator != null && animator.avatar != null && animator.avatar.isValid && animator.runtimeAnimatorController != null && !animator.applyRootMotion,
                "Runtime Animator/controller/avatar contract failed.");
            Require(animator.runtimeAnimatorController.animationClips.Length == 1 && animator.runtimeAnimatorController.animationClips[0].name == report.selectedClip,
                "Runtime controller must reference Swim only, never Death/Idle.");
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true)) foreach (Material material in renderer.sharedMaterials)
                Require(material != null && material.shader.name == "Universal Render Pipeline/Lit" && AssetDatabase.GetAssetPath(material).StartsWith(BakedRoot + "/", StringComparison.Ordinal) &&
                    material.GetTexture("_BaseMap") != null && material.GetTexture("_BumpMap") != null && material.GetColor("_BaseColor") == Color.white && material.IsKeywordEnabled("_NORMALMAP"),
                    "Runtime must use the admitted original-color URP material and normal map.");
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true)) foreach (Material material in renderer.sharedMaterials)
                Require(material.GetTextureScale("_BaseMap") == report.textureUvScale && material.GetTextureScale("_BumpMap") == report.textureUvScale,
                    "Padded source UVs must retain identical calibrated color/normal scale.");
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true)) foreach (Material material in renderer.sharedMaterials)
                Require(material.GetFloat("_Surface") == 0f && material.GetFloat("_AlphaClip") == 0f && material.GetFloat("_SmoothnessTextureChannel") == 0f &&
                    !material.IsKeywordEnabled("_ALPHATEST_ON") && !material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") && !material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"),
                    "Derived base alpha255 is permitted only for opaque, unclipped URP Lit with constant smoothness.");
            foreach (var record in report.textures)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(record.path);
                TextureFormat expectedFormat = record.srgb ? TextureFormat.ASTC_4x4 : TextureFormat.RGBA32;
                Require(texture != null && record.path.EndsWith(".asset", StringComparison.Ordinal) && AssetDatabase.GetMainAssetTypeAtPath(record.path) == typeof(Texture2D) &&
                    !(AssetImporter.GetAtPath(record.path) is TextureImporter) && texture.format == expectedFormat && record.format == expectedFormat.ToString() && texture.width == record.width && texture.height == record.height && texture.mipmapCount == record.mips && !texture.isReadable,
                    "Runtime texture format/resolution/mips/readability changed.");
                Require(GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat) == record.srgb && Hash(record.source) == record.sourceSha256 &&
                    texture.filterMode == FilterMode.Trilinear && texture.anisoLevel == 16 && record.runtimeBytes == record.expectedPayloadBytes &&
                    record.sourceWidth == 800 && record.sourceHeight == 534 && record.width == 800 && record.height == 536 && record.mips == 10 &&
                    record.opaqueAlpha == record.srgb &&
                    record.copyMaximumError <= 1 && (record.srgb ? record.meanByteError <= 8 && record.rmsByteError <= 12 && record.maximumAlphaError <= 16 :
                        record.maximumRgbError <= 1 && record.maximumAlphaError <= 1),
                    "Native color space, source identity, filtering or ASTC-base/lossless-normal quality contract changed.");
            }
            Require(!prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(), "No procedural FishSwim or unrelated runtime components are permitted on the rig asset.");
            string[] dependencies = AssetDatabase.GetDependencies(PrefabPath, true);
            Require(dependencies.Contains(ModelPath) && !dependencies.Any(x => x.StartsWith(SourceRoot + "/", StringComparison.Ordinal) && x.EndsWith(".png", StringComparison.Ordinal)),
                "Prefab must retain its original skinned mesh/clip but must not pack uncompressed original images.");
        }

        static OceanAuthoredFishGeometry ExpectedGeometry(Report report)
        {
            Require(report.posedBounds.Count == 61 && report.geometryPath == GeometryPath, "All original sampled pose bounds are required for geometry metadata.");
            Quaternion heading = Quaternion.Inverse(Quaternion.LookRotation(report.sourceNoseDirection, report.sourceUpDirection));
            Vector3 firstCenter = (report.boundsMin + report.boundsMax) * .5f;
            Matrix4x4 navigation = Matrix4x4.TRS(-(heading * firstCenter), heading, Vector3.one);
            Bounds envelope = default; bool haveEnvelope = false;
            foreach (PoseBoundsRecord pose in report.posedBounds)
            {
                Require(Finite(pose.min) && Finite(pose.max), "Nonfinite sampled geometry bounds.");
                Bounds transformed = OceanAuthoredFishGeometry.TransformBounds(new Bounds((pose.min + pose.max) * .5f, pose.max - pose.min), navigation);
                if (!haveEnvelope) { envelope = transformed; haveEnvelope = true; } else envelope.Encapsulate(transformed);
            }
            Require(haveEnvelope && Finite(envelope.center) && Finite(envelope.size) && envelope.size.x > 0f && envelope.size.y > 0f && envelope.size.z > 0f,
                "Invalid sampled navigation geometry envelope.");
            return new OceanAuthoredFishGeometry { schemaVersion = 1, sourceMeshSha256 = Hash(ModelPath),
                firstPoseDiagonal = (report.boundsMax - report.boundsMin).magnitude,
                navigationCenter = envelope.center, navigationSize = envelope.size * 1.02f };
        }

        /// <summary>Read-only build preflight, including the explicit base-RGB/opaque-alpha255 material contract.
        /// Normal AG alpha remains source X in lossless RGBA32 with error<=1; base remains ASTC4x4.
        /// No import/rewrite/bake/target/exit.</summary>
        public static void ValidateForBuild()
        {
            Require(pending == null, "Admission is still finalizing.");
            Require(PlayerSettings.GetNormalMapEncoding(UnityEditor.Build.NamedBuildTarget.Android) == NormalMapEncoding.DXT5nm,
                "Native URP normal requires the verified Android DXT5nm normal encoding policy.");
            Report report = ReadLatest();
            Require(report.success && report.state == "admitted", "No completed animated marine admission is available.");
            VerifySource(out _); VerifyFiles(report.after);
            Require(SetEqual(report.after, Snapshot(OwnedFiles())), "Owned asset roster changed after admission.");
            ValidateOutputs(report);
            Debug.Log("[MarineExpansion] read-only build admission PASS: original animated mackerel, native ASTC4x4 base/lossless RGBA32 AG normal; verified for PICO/Quest Android DXT5nm, AVP rendering not claimed.");
        }

        static void CalibrateReadback()
        {
            if (calibrated) return;
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ReadbackShaderPath);
            Require(shader != null && shader.isSupported, "Existing exact-mip decoder shader is unavailable.");
            decoder = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            bool orientationChosen = false;
            foreach (bool srgb in new[] { false, true })
            {
                var known = new Texture2D(8, 8, TextureFormat.RGBA32, 4, !srgb);
                try
                {
                    var expected = new byte[4][];
                    for (int mip = 0; mip < 4; mip++)
                    {
                        int size = Math.Max(1, 8 >> mip); var bytes = new byte[size * size * 4];
                        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                        {
                            int p = (y * size + x) * 4;
                            bytes[p] = (byte)((x * 31 + y * 17 + mip * 61) & 255); bytes[p + 1] = (byte)((x * 13 + y * 37 + mip * 29) & 255);
                            bytes[p + 2] = (byte)((x * 71 + y * 7 + mip * 43) & 255); bytes[p + 3] = (byte)((x * 19 + y * 29 + mip * 47) & 255);
                        }
                        expected[mip] = bytes; known.SetPixelData(bytes, mip);
                    }
                    known.Apply(false, false);
                    for (int mip = 0; mip < 4; mip++)
                    {
                        int size = Math.Max(1, 8 >> mip); byte[] actual = RenderDecoded(known, mip, srgb);
                        if (!orientationChosen) { flipRows = MaxError(expected[mip], actual) > 1; orientationChosen = true; }
                        if (flipRows) FlipRows(actual, size, size);
                        Require(MaxError(expected[mip], actual) <= 1, "Independent linear/sRGB/mip/alpha readback calibration failed.");
                    }
                }
                finally { Object.DestroyImmediate(known); }
            }
            calibrated = true;
        }
        static byte[] ReadPixels(Texture texture, int mip, bool srgb)
        {
            Require(calibrated, "Readback has not been calibrated.");
            byte[] bytes = RenderDecoded(texture, mip, srgb);
            if (flipRows) FlipRows(bytes, Math.Max(1, texture.width >> mip), Math.Max(1, texture.height >> mip));
            return bytes;
        }
        static byte[] RenderDecoded(Texture texture, int mip, bool srgb)
        {
            var descriptor = new RenderTextureDescriptor(Math.Max(1, texture.width >> mip), Math.Max(1, texture.height >> mip))
            { graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = 0, msaaSamples = 1, useMipMap = false, autoGenerateMips = false };
            var target = RenderTexture.GetTemporary(descriptor); RenderTexture previous = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
            try
            {
                decoder.SetTexture("_Source", texture); decoder.SetInt("_Mip", mip); decoder.SetFloat("_EncodeSrgb", srgb ? 1 : 0);
                GL.sRGBWrite = false; Graphics.Blit(texture, target, decoder, 0);
                var request = AsyncGPUReadback.Request(target, 0, GraphicsFormat.R8G8B8A8_UNorm); request.WaitForCompletion();
                Require(!request.hasError, "GPU readback failed: " + texture.name + " mip=" + mip);
                return request.GetData<byte>().ToArray();
            }
            finally { decoder.SetTexture("_Source", null); GL.sRGBWrite = previousSrgb; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }
        static int MaxError(byte[] expected, byte[] actual)
        { Require(expected.Length == actual.Length, "Readback byte count mismatch."); int error = 0; for (int i = 0; i < expected.Length; i++) error = Math.Max(error, Math.Abs(expected[i] - actual[i])); return error; }
        static void FlipRows(byte[] bytes, int width, int height)
        { int stride = width * 4; for (int y = 0; y < height / 2; y++) for (int x = 0; x < stride; x++) { int a = y * stride + x, b = (height - 1 - y) * stride + x; byte value = bytes[a]; bytes[a] = bytes[b]; bytes[b] = value; } }
        static void ReleaseReadback()
        { if (decoder != null) Object.DestroyImmediate(decoder); decoder = null; calibrated = false; flipRows = false; }

        static void BeginFinalization(Report report, Exception error)
        {
            if (pending != null) { pending.error = error ?? pending.error; return; }
            report.success = false; report.state = error == null ? "stabilizing" : "stabilizing_failure"; report.error = error?.ToString();
            report.after = Snapshot(OwnedFiles()); Save(report);
            pending = new Pending { report = report, error = error, snapshot = report.after };
            EditorApplication.update += FinalizeUpdate;
        }
        static void FinalizeUpdate()
        {
            if (pending == null) { EditorApplication.update -= FinalizeUpdate; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            Pending work = pending;
            try
            {
                AssetDatabase.SaveAssets(); List<FileRecord> now = Snapshot(OwnedFiles()); work.ticks++;
                work.stable = SetEqual(work.snapshot, now) ? work.stable + 1 : 0; work.snapshot = now;
                work.report.after = now; work.report.finalizationUpdates = work.ticks; Save(work.report);
                Require(work.ticks <= 30, "Deferred imported/material writes did not stabilize.");
                if (work.ticks < 3 || work.stable < 2) return;
                VerifyFiles(work.report.protectedFiles); VerifySource(out _);
                if (work.error == null) ValidateOutputs(work.report);
                work.report.success = work.error == null; work.report.state = work.error == null ? "admitted" : "failed";
                work.report.error = work.error?.ToString(); Save(work.report);
                if (work.report.success)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(PortableReceipt));
                    File.WriteAllText(PortableReceipt, JsonUtility.ToJson(work.report, true));
                }
                Debug.Log(work.error == null ? "MARINE_EXPANSION_BAKE_PASSED " + Latest : "MARINE_EXPANSION_BAKE_FAILED " + Latest);
                pending = null; EditorApplication.update -= FinalizeUpdate; ReleaseReadback(); Exit(work.error == null ? 0 : 1);
            }
            catch (Exception exception)
            {
                work.report.success = false; work.report.state = "failed"; work.report.error = exception.ToString(); work.report.after = Snapshot(OwnedFiles()); Save(work.report);
                Debug.LogException(exception); pending = null; EditorApplication.update -= FinalizeUpdate; ReleaseReadback(); Exit(1);
            }
        }

        [MenuItem("Swan/Codex/Marine Expansion/Roll back latest owned admission")]
        public static void Rollback()
        {
            try
            {
                Require(pending == null && !EditorApplication.isPlayingOrWillChangePlaymode, "Edit Mode required.");
                Report report = ReadLatest(); Require(report.tool == "OceanMarineExpansionBake" && report.state != "rolled_back", "Invalid rollback journal.");
                VerifyFiles(report.after); Require(SetEqual(report.after, Snapshot(OwnedFiles())), "Unknown/edited owned files; refusing rollback.");
                VerifyFiles(report.protectedFiles);
                foreach (string path in new[] { GeometryPath, PrefabPath, SourceRoot, BakedRoot }) if (File.Exists(path) || Directory.Exists(path)) Require(AssetDatabase.DeleteAsset(path), "Delete owned asset failed: " + path);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); Require(!OwnedFiles().Any(), "Owned output remains after rollback.");
                report.state = "rolled_back"; report.success = false; Save(report); Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); Exit(1); if (!Application.isBatchMode) throw; }
        }

        static Vector3[] WorldVertices(Transform root, Transform skin, Vector3[] vertices)
        { for (int i = 0; i < vertices.Length; i++) vertices[i] = skin.TransformPoint(vertices[i]) - root.position; return vertices; }
        static Vector3 UvCenter(Vector3[] vertices, Vector2[] uv, Func<Vector2, bool> includes)
        { Vector3 sum = Vector3.zero; int count = 0; for (int i = 0; i < vertices.Length; i++) if (includes(uv[i])) { sum += vertices[i]; count++; } Require(count > 2, "Original UV anatomical region is missing."); return sum / count; }
        static Bounds BoundsOf(Vector3[] vertices)
        { Require(vertices.Length > 0 && Finite(vertices[0]), "Empty/nonfinite posed mesh."); var result = new Bounds(vertices[0], Vector3.zero); foreach (Vector3 vertex in vertices) { Require(Finite(vertex), "Nonfinite deformed mesh."); result.Encapsulate(vertex); } return result; }
        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        static bool Finite(Vector3 x) => Finite(x.x) && Finite(x.y) && Finite(x.z);
        static bool Finite(Quaternion x) => Finite(x.x) && Finite(x.y) && Finite(x.z) && Finite(x.w) && x.x * x.x + x.y * x.y + x.z * x.z + x.w * x.w > .8f;
        static IEnumerable<string> OwnedFiles()
        {
            foreach (string root in new[] { SourceRoot, BakedRoot }) { foreach (string path in FilesUnder(root)) yield return path; if (File.Exists(root + ".meta")) yield return root + ".meta"; }
            if (File.Exists(PrefabPath)) yield return PrefabPath; if (File.Exists(PrefabPath + ".meta")) yield return PrefabPath + ".meta";
            if (File.Exists(GeometryPath)) yield return GeometryPath; if (File.Exists(GeometryPath + ".meta")) yield return GeometryPath + ".meta";
        }
        static IEnumerable<string> FilesUnder(string root) => Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(x => x.Replace('\\', '/')).Where(x => !Path.GetFileName(x).StartsWith(".", StringComparison.Ordinal)) : Enumerable.Empty<string>();
        static List<FileRecord> Snapshot(IEnumerable<string> paths) => paths.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).Select(x => new FileRecord { path = x, sha256 = Hash(x), bytes = new FileInfo(x).Length }).ToList();
        static bool SetEqual(List<FileRecord> a, List<FileRecord> b) => a.Count == b.Count && a.Zip(b, (x, y) => x.path == y.path && x.sha256 == y.sha256 && x.bytes == y.bytes).All(x => x);
        static void VerifyFiles(List<FileRecord> records) { foreach (var file in records) Require(File.Exists(file.path) && new FileInfo(file.path).Length == file.bytes && Hash(file.path) == file.sha256, "Byte roster changed: " + file.path); }
        static void Plan(string path, Report report) { Require(!File.Exists(path) && !Directory.Exists(path), "Never overwrite a different asset: " + path); report.plannedPaths.Add(path); Save(report); }
        static void MakeFolder(string path) { if (AssetDatabase.IsValidFolder(path)) return; string parent = Path.GetDirectoryName(path).Replace('\\', '/'); MakeFolder(parent); Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))), "Folder creation failed: " + path); }
        static Report ReadLatest()
        {
            // A fresh clone has the tracked complete receipt, not ignored local Validation history.
            // An existing failed/rolled-back local transaction must never be hidden by this fallback.
            bool local = File.Exists(Latest); string path = local ? Latest : PortableReceipt;
            Require(File.Exists(path), "No local ownership journal or portable admission receipt exists.");
            Report report = JsonUtility.FromJson<Report>(File.ReadAllText(path));
            Require(report != null && report.tool == "OceanMarineExpansionBake", "Invalid report.");
            if (!local) Require(report.success && report.state == "admitted" && report.uid == Uid && report.packageSha256 == ArchiveHash,
                "Portable receipt is not a sealed admission of the exact reviewed source.");
            return report;
        }
        static void Save(Report report) { Directory.CreateDirectory(report.directory); string text = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(report.directory, "report.json"), text); File.WriteAllText(Latest, text); }
        static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        static uint Crc32(byte[] bytes) { uint crc = 0xffffffffu; foreach (byte value in bytes) { crc ^= value; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u); } return ~crc; }
        static bool HasArgument(string key) => Environment.GetCommandLineArgs().Contains(key);
        static string Argument(string key, string fallback) { string[] args = Environment.GetCommandLineArgs(); for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1]; return fallback; }
        static void Exit(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException("[MarineExpansionBake] " + message); }
    }
}
#endif

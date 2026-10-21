#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>
    /// Explicit, reversible editor conversion; never runs automatically during import/build.
    /// Sample touches only its new sample folder. ConvertAll requires that successful sample.
    /// Run with graphics (not -nographics): every mip is read back in its original color encoding.
    /// </summary>
    public static class ReefAstcBake
    {
        const string ResourcesRoot = "Assets/Resources/RealisticReef";
        const string SourcesRoot = "Assets/ArtSources/RealisticReef";
        const string BakedRoot = "Assets/ArtBaked/ReefAstc";
        const string SampleRoot = "Assets/ArtBaked/ReefAstcSamples";
        const string SampleName = "coral_cuneata_lod0";
        static readonly string[] Stems = { "coral_cuneata", "coral_spicifera", "coral_brain", "coral_palmata", "rock_coast_05", "rock_coast_02" };
        const string ReadbackShaderPath = "Assets/SwanUnderwater/Editor/ReefAstcReadback.shader";
        static Material readbackMaterial;
        static bool flipReadbackRows, readbackCalibrated;
        static int calibrationMaxByteError;
        static string EvidenceRoot => Path.GetFullPath(Environment.GetEnvironmentVariable("OCEAN_REEF_ASTC_REPORT_DIR") ?? "Validation/iteration-25/reef-astc");
        static string Latest => Path.Combine(EvidenceRoot, "latest-conversion.json");
        static string SampleProof => Path.Combine(EvidenceRoot, "latest-sample.json");

        [Serializable] public sealed class TextureRecord
        {
            public string source, output, role, sourceFormat, targetFormat, sourceGraphicsFormat, targetGraphicsFormat;
            public int width, height, mipCount, alphaMin = 255, alphaMax;
            public bool srgb, readableBefore, readableAfter;
            public long sourceRuntimeBytes, sourceReadbackRgbaBytes, astcBytes, expectedAstcBytes;
            public double meanByteError, rmsByteError, maxByteError, maxAlphaError;
            public int copyMaxByteError;
        }
        [Serializable] public sealed class ModelRecord
        {
            public string name, originalPath, movedPath, prefabPath, originalGuid, originalSha256, originalMetaSha256;
            public string prefabGuid, prefabSha256;
            public int rendererCount, meshCount, materialCount, textureCount, oldMaterialReferences, oldTextureReferences;
            public long triangles;
            public bool moved, validated;
            public string[] dependencies;
        }
        [Serializable] public sealed class Manifest
        {
            public int version = 1;
            public string mode, utc, unity, outputFolder, backupFolder, manifestPath, state, error;
            public bool success;
            public int decoderValidationVersion = 2, readbackCalibrationMaxByteError;
            public bool readbackRowFlip;
            public string decodePath = "Explicit mip texel Load to RGBA8 UNorm RT; sRGB re-encoding in shader; raw readback; independent known-byte calibration";
            public long sourceRuntimeTextureBytes, astcTextureBytes;
            public List<ModelRecord> models = new List<ModelRecord>();
            public List<TextureRecord> textures = new List<TextureRecord>();
            public List<string> createdAssets = new List<string>();
            public List<string> materialImportCorrections = new List<string>();
        }

        [MenuItem("Swan/Codex/Reef ASTC/1 - Validate three-texture sample")]
        public static void Sample() => Execute(SampleCore);
        [MenuItem("Swan/Codex/Reef ASTC/2 - Convert all sixteen reef assets")]
        public static void ConvertAll() => Execute(ConvertCore);
        [MenuItem("Swan/Codex/Reef ASTC/3 - Validate current conversion")]
        public static void ValidateCurrent() => Execute(() =>
        {
            Manifest manifest = ReadManifest(Latest);
            Require(manifest.success && manifest.state == "converted", "No successful active conversion.");
            foreach (ModelRecord model in manifest.models) ValidateModel(model, manifest.outputFolder);
            foreach (TextureRecord record in manifest.textures) ValidateSavedTexture(record);
            Write(Path.Combine(EvidenceRoot, "current-validation-" + Stamp() + ".json"), manifest);
            Debug.Log("[ReefASTC] current validation passed; 16 models, " + manifest.textures.Count + " textures.");
        });
        [MenuItem("Swan/Codex/Reef ASTC/4 - Roll back latest conversion")]
        public static void Rollback() => Execute(() => RollbackCore(ReadManifest(Latest)));

        static void Execute(Action work)
        {
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run only in Edit Mode.");
                Directory.CreateDirectory(EvidenceRoot);
                work();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (readbackMaterial != null) Object.DestroyImmediate(readbackMaterial);
                readbackMaterial = null; readbackCalibrated = false;
            }
        }

        static Manifest NewManifest(string mode, string folder)
        {
            string directory = Path.Combine(EvidenceRoot, mode + "-" + Stamp());
            Directory.CreateDirectory(directory);
            return new Manifest { mode = mode, utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                outputFolder = folder, backupFolder = Path.Combine(directory, "originals"),
                manifestPath = Path.Combine(directory, "report.json"), state = "preparing",
                readbackCalibrationMaxByteError = calibrationMaxByteError, readbackRowFlip = flipReadbackRows };
        }

        static void SampleCore()
        {
            Require(SystemInfo.supportsAsyncGPUReadback, "GPU readback unavailable; do not use -nographics.");
            CalibrateReadback();
            string sourcePath = ResourcesRoot + "/" + SampleName + ".glb";
            Require(File.Exists(sourcePath), "Sample requires original Resources GLB before conversion.");
            var report = NewManifest("sample", SampleRoot + "/" + Stamp());
            report.models.Add(Describe(SampleName));
            MakeFolder(report.outputFolder);
            try
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                BakePrefab(source, report.outputFolder + "/" + SampleName + ".prefab", report, false);
                Require(report.textures.Count == 3, "Expected albedo/normal/packed-data sample textures.");
                AssetDatabase.SaveAssets();
                foreach (TextureRecord record in report.textures) ValidateSavedTexture(record);
                report.state = "sample_passed"; report.success = true;
                Save(report); Write(SampleProof, report);
                Debug.Log("[ReefASTC] sample passed: " + report.manifestPath + "; before/after PNGs are in the same directory. Original resources unchanged.");
            }
            catch (Exception e) { report.error = e.ToString(); report.state = "failed"; Save(report); throw; }
        }

        static void ConvertCore()
        {
            Require(SystemInfo.supportsAsyncGPUReadback, "GPU readback unavailable; do not use -nographics.");
            CalibrateReadback();
            Manifest sample = ReadManifest(SampleProof);
            Require(sample.success && sample.state == "sample_passed" && sample.unity == Application.unityVersion && sample.decoderValidationVersion == 2,
                "Run Sample successfully in this Unity version first.");
            Require(sample.models.Count == 1 && Hash(sample.models[0].originalPath) == sample.models[0].originalSha256,
                "Sample source changed; rerun Sample.");
            Require(!AssetDatabase.IsValidFolder(BakedRoot) && !Directory.Exists(BakedRoot), "Baked destination exists; validate or roll back it first.");
            var report = NewManifest("conversion", BakedRoot);
            foreach (string stem in Stems)
                for (int lod = 0; lod < (stem.StartsWith("rock_", StringComparison.Ordinal) ? 2 : 3); lod++)
                {
                    ModelRecord model = Describe(stem + "_lod" + lod);
                    Require(!File.Exists(model.movedPath) && !File.Exists(model.prefabPath), "Destination already exists: " + model.name);
                    report.models.Add(model);
                }
            Directory.CreateDirectory(report.backupFolder);
            foreach (ModelRecord model in report.models)
            {
                File.Copy(model.originalPath, Path.Combine(report.backupFolder, model.name + ".glb"));
                File.Copy(model.originalPath + ".meta", Path.Combine(report.backupFolder, model.name + ".glb.meta"));
                Require(Hash(Path.Combine(report.backupFolder, model.name + ".glb")) == model.originalSha256, "Backup hash mismatch.");
            }
            Save(report); Write(Latest, report); // Journal exists before the first AssetDatabase mutation.
            MakeFolder(BakedRoot); MakeFolder(SourcesRoot);
            try
            {
                // Bake all replacements before moving any live resource name.
                foreach (ModelRecord model in report.models)
                {
                    EditorUtility.DisplayProgressBar("Reef ASTC 4x4", model.name, report.textures.Count / 48f);
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(model.originalPath);
                    BakePrefab(source, BakedRoot + "/" + model.name + ".prefab", report, true);
                    Save(report); Write(Latest, report);
                }
                Require(report.textures.Count == 48, "Expected exactly 48 unique reef textures.");
                AssetDatabase.SaveAssets();
                foreach (ModelRecord model in report.models)
                {
                    CheckMove(AssetDatabase.MoveAsset(model.originalPath, model.movedPath));
                    model.moved = true; Save(report); Write(Latest, report);
                    Require(AssetDatabase.AssetPathToGUID(model.movedPath) == model.originalGuid, "Source GUID changed.");
                    Require(Hash(model.movedPath) == model.originalSha256, "Source GLB bytes changed.");
                    string stagedPrefab = BakedRoot + "/" + model.name + ".prefab";
                    model.prefabGuid = AssetDatabase.AssetPathToGUID(stagedPrefab);
                    model.prefabSha256 = Hash(stagedPrefab); Save(report); Write(Latest, report);
                    CheckMove(AssetDatabase.MoveAsset(stagedPrefab, model.prefabPath));
                    report.createdAssets.Remove(stagedPrefab); report.createdAssets.Add(model.prefabPath);
                    model.prefabGuid = AssetDatabase.AssetPathToGUID(model.prefabPath);
                    model.prefabSha256 = Hash(model.prefabPath); Save(report); Write(Latest, report);
                }
                AssetDatabase.SaveAssets();
                foreach (ModelRecord model in report.models) ValidateModel(model, BakedRoot);
                foreach (TextureRecord record in report.textures) ValidateSavedTexture(record);
                report.state = "converted"; report.success = true; Save(report); Write(Latest, report);
                Debug.Log("[ReefASTC] converted 16 models/48 textures. ASTC payload=" + report.astcTextureBytes +
                    " bytes. Backup/rollback journal=" + report.manifestPath + "; Android build and visual validation still required.");
            }
            catch (Exception e)
            {
                report.error = e.ToString(); report.state = "failed"; Save(report); Write(Latest, report);
                // Do not erase partial evidence or silently mask the original failure. Rollback uses this journal.
                Debug.LogError("[ReefASTC] Partial conversion retained; run Rollback before using/building resources. " + report.manifestPath);
                throw;
            }
        }

        static ModelRecord Describe(string name)
        {
            string original = ResourcesRoot + "/" + name + ".glb";
            Require(File.Exists(original) && File.Exists(original + ".meta"), "Missing original " + original);
            return new ModelRecord { name = name, originalPath = original, movedPath = SourcesRoot + "/" + name + ".glb",
                prefabPath = ResourcesRoot + "/" + name + ".prefab", originalGuid = AssetDatabase.AssetPathToGUID(original),
                originalSha256 = Hash(original), originalMetaSha256 = Hash(original + ".meta") };
        }

        static void BakePrefab(GameObject source, string destination, Manifest report, bool full)
        {
            Require(source != null, "Missing source prefab.");
            GameObject instance = Object.Instantiate(source);
            try
            {
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = source.name;
                var textureCache = new Dictionary<Texture2D, Texture2D>();
                var materialCache = new Dictionary<Material, Material>();
                string directory = report.outputFolder + "/" + source.name;
                MakeFolder(directory);
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int m = 0; m < materials.Length; m++)
                    {
                        Material original = materials[m];
                        if (original == null) continue;
                        if (!materialCache.TryGetValue(original, out Material replacement))
                        {
                            replacement = new Material(original) { name = original.name };
                            foreach (string property in original.GetTexturePropertyNames())
                            {
                                Texture texture = original.GetTexture(property);
                                if (texture == null) continue;
                                Require(texture is Texture2D, "Unexpected non-2D reef texture: " + property);
                                var texture2D = (Texture2D)texture;
                                if (!textureCache.TryGetValue(texture2D, out Texture2D compressed))
                                {
                                    string path = directory + "/texture_" + textureCache.Count + ".asset";
                                    compressed = Compress(texture2D, path, property, report, !full);
                                    textureCache.Add(texture2D, compressed);
                                }
                                replacement.SetTexture(property, compressed);
                            }
                            string materialPath = directory + "/material_" + materialCache.Count + ".mat";
                            AssetDatabase.CreateAsset(replacement, materialPath); report.createdAssets.Add(materialPath);
                            materialCache.Add(original, replacement);
                        }
                        materials[m] = replacement;
                    }
                    renderer.sharedMaterials = materials;
                }
                PrefabUtility.SaveAsPrefabAsset(instance, destination, out bool saved);
                Require(saved, "Failed saving " + destination); report.createdAssets.Add(destination);
                // Creating a standalone .mat runs URP/ShaderGraph material import migrations,
                // unlike the original GLB subasset. Restore the original state AFTER that pass;
                // preserve only our deliberate texture substitution, then save and compare again.
                foreach (var pair in materialCache) RestoreImportedMaterial(pair.Key, pair.Value, report);
                ValidatePair(source, AssetDatabase.LoadAssetAtPath<GameObject>(destination), null);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        static Texture2D Compress(Texture2D source, string path, string role, Manifest report, bool writeImages)
        {
            bool srgb = GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
            // Preserve mip contents and sRGB encoding explicitly. No Blit, gamma conversion,
            // normal-map swizzle, alpha stripping, resize, or generated replacement mip chain.
            GraphicsFormat rgba = srgb ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            Require(source.width > 0 && source.height > 0 && source.mipmapCount > 0, "Invalid source texture.");
            var record = new TextureRecord { source = AssetDatabase.GetAssetPath(source) + ":" + source.name,
                output = path, role = role, width = source.width, height = source.height, mipCount = source.mipmapCount,
                srgb = srgb, sourceFormat = source.format.ToString(), sourceGraphicsFormat = source.graphicsFormat.ToString(),
                readableBefore = source.isReadable, sourceRuntimeBytes = Profiler.GetRuntimeMemorySizeLong(source) };
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, source.mipmapCount, !srgb)
            { name = source.name, filterMode = source.filterMode, wrapModeU = source.wrapModeU,
                wrapModeV = source.wrapModeV, wrapModeW = source.wrapModeW, anisoLevel = source.anisoLevel,
                mipMapBias = source.mipMapBias };
            try
            {
                var originals = new byte[source.mipmapCount][];
                for (int mip = 0; mip < source.mipmapCount; mip++)
                {
                    originals[mip] = ReadPixels(source, mip, rgba);
                    int w = Math.Max(1, source.width >> mip), h = Math.Max(1, source.height >> mip);
                    Require(originals[mip].Length == w * h * 4, "Readback has unexpected dimensions/format.");
                    copy.SetPixelData(originals[mip], mip);
                    record.sourceReadbackRgbaBytes += originals[mip].Length;
                    record.expectedAstcBytes += (long)((w + 3) / 4) * ((h + 3) / 4) * 16;
                }
                copy.Apply(false, false);
                for (int mip = 0; mip < source.mipmapCount; mip++)
                    record.copyMaxByteError = Math.Max(record.copyMaxByteError, MaxError(originals[mip], ReadPixels(copy, mip, rgba)));
                Require(record.copyMaxByteError <= 1, "RGBA copy changed color encoding or mip pixels beyond one-byte rounding.");
                EditorUtility.CompressTexture(copy, TextureFormat.ASTC_4x4, TextureCompressionQuality.Best);
                Require(copy.format == TextureFormat.ASTC_4x4, "Editor did not produce ASTC 4x4.");
                Require(copy.mipmapCount == source.mipmapCount && GraphicsFormatUtility.IsSRGBFormat(copy.graphicsFormat) == srgb,
                    "Compression changed mip count or color space.");
                record.astcBytes = copy.GetRawTextureData<byte>().Length;
                Require(record.astcBytes == record.expectedAstcBytes, "Unexpected compressed mip payload length.");
                copy.Apply(false, false);
                double squared = 0, absolute = 0; long count = 0;
                for (int mip = 0; mip < source.mipmapCount; mip++)
                {
                    byte[] decoded = ReadPixels(copy, mip, rgba), original = originals[mip];
                    Require(decoded.Length == original.Length, "ASTC decode dimensions changed.");
                    for (int i = 0; i < original.Length; i++)
                    {
                        int delta = Math.Abs(original[i] - decoded[i]);
                        absolute += delta; squared += delta * delta; count++;
                        record.maxByteError = Math.Max(record.maxByteError, delta);
                        if ((i & 3) == 3)
                        {
                            record.alphaMin = Math.Min(record.alphaMin, original[i]); record.alphaMax = Math.Max(record.alphaMax, original[i]);
                            record.maxAlphaError = Math.Max(record.maxAlphaError, delta);
                        }
                    }
                    if (writeImages && mip == 0)
                    {
                        string prefix = Path.Combine(Path.GetDirectoryName(report.manifestPath), "texture_" + report.textures.Count);
                        SavePng(prefix + "_before.png", source.width, source.height, original, srgb);
                        SavePng(prefix + "_after.png", source.width, source.height, decoded, srgb);
                    }
                }
                record.meanByteError = absolute / count; record.rmsByteError = Math.Sqrt(squared / count);
                // A coarse safety gate, not a substitute for inspecting albedo and normal details.
                Require(record.meanByteError <= 8 && record.rmsByteError <= 12 && record.maxAlphaError <= 16,
                    "ASTC sample error exceeds conservative byte-space threshold: " + role);
                copy.Apply(false, true); // No persistent duplicate readable CPU copy in the player.
                record.readableAfter = copy.isReadable; record.targetFormat = copy.format.ToString();
                record.targetGraphicsFormat = copy.graphicsFormat.ToString();
                AssetDatabase.CreateAsset(copy, path); report.createdAssets.Add(path);
                report.textures.Add(record); report.sourceRuntimeTextureBytes += record.sourceRuntimeBytes; report.astcTextureBytes += record.astcBytes;
                Save(report);
                return copy;
            }
            catch { if (!AssetDatabase.Contains(copy)) Object.DestroyImmediate(copy); throw; }
        }

        static void CalibrateReadback()
        {
            if (readbackCalibrated) return;
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ReadbackShaderPath);
            Require(shader != null && shader.isSupported, "Missing/unsupported editor readback shader.");
            readbackMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            flipReadbackRows = false; calibrationMaxByteError = 0;
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
                            int i = (y * size + x) * 4;
                            bytes[i] = (byte)((x * 31 + y * 17 + mip * 61) & 255);
                            bytes[i + 1] = (byte)((x * 13 + y * 37 + mip * 29) & 255);
                            bytes[i + 2] = (byte)((x * 71 + y * 7 + mip * 43) & 255);
                            bytes[i + 3] = (byte)((x * 19 + y * 29 + mip * 47) & 255);
                        }
                        expected[mip] = bytes; known.SetPixelData(bytes, mip);
                    }
                    known.Apply(false, false);
                    for (int mip = 0; mip < 4; mip++)
                    {
                        int size = Math.Max(1, 8 >> mip);
                        byte[] actual = RenderDecoded(known, mip, srgb);
                        if (!orientationChosen)
                        {
                            flipReadbackRows = MaxError(expected[mip], actual) > 1;
                            orientationChosen = true;
                        }
                        if (flipReadbackRows) FlipRows(actual, size, size);
                        int error = MaxError(expected[mip], actual);
                        calibrationMaxByteError = Math.Max(calibrationMaxByteError, error);
                        Require(error <= 1, "Known-byte GPU decoder calibration failed: sRGB=" + srgb + " mip=" + mip + " maxError=" + error);
                    }
                }
                finally { Object.DestroyImmediate(known); }
            }
            readbackCalibrated = true;
            Debug.Log("[ReefASTC] GPU decoder calibration passed: linear+sRGB, 4 independent mip levels, alpha; maxByteError=" +
                calibrationMaxByteError + " rowFlip=" + flipReadbackRows);
        }
        static byte[] ReadPixels(Texture texture, int mip, GraphicsFormat encodedFormat)
        {
            Require(readbackCalibrated, "Readback decoder must pass known-byte calibration first.");
            byte[] data = RenderDecoded(texture, mip, GraphicsFormatUtility.IsSRGBFormat(encodedFormat));
            if (flipReadbackRows) FlipRows(data, Math.Max(1, texture.width >> mip), Math.Max(1, texture.height >> mip));
            return data;
        }
        static byte[] RenderDecoded(Texture texture, int mip, bool encodeSrgb)
        {
            int width = Math.Max(1, texture.width >> mip), height = Math.Max(1, texture.height >> mip);
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = 0,
                msaaSamples = 1, useMipMap = false, autoGenerateMips = false
            };
            RenderTexture target = RenderTexture.GetTemporary(descriptor);
            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            try
            {
                readbackMaterial.SetTexture("_Source", texture);
                readbackMaterial.SetInt("_Mip", mip);
                readbackMaterial.SetFloat("_EncodeSrgb", encodeSrgb ? 1f : 0f);
                // Hardware sampling decodes ASTC (and sRGB) before the shader explicitly restores
                // source byte encoding into a LINEAR uncompressed RT. Only the RT is read back.
                GL.sRGBWrite = false;
                Graphics.Blit(texture, target, readbackMaterial, 0);
                var request = AsyncGPUReadback.Request(target, 0, GraphicsFormat.R8G8B8A8_UNorm);
                request.WaitForCompletion();
                Require(!request.hasError, "Decoded RT readback failed: " + texture.name + " mip=" + mip);
                return request.GetData<byte>().ToArray();
            }
            finally
            {
                readbackMaterial.SetTexture("_Source", null);
                GL.sRGBWrite = previousSrgbWrite; RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
        static int MaxError(byte[] expected, byte[] actual)
        {
            Require(expected.Length == actual.Length, "GPU decoder byte count mismatch.");
            int maximum = 0;
            for (int i = 0; i < expected.Length; i++) maximum = Math.Max(maximum, Math.Abs(expected[i] - actual[i]));
            return maximum;
        }
        static void FlipRows(byte[] bytes, int width, int height)
        {
            int rowBytes = width * 4;
            for (int y = 0; y < height / 2; y++) for (int x = 0; x < rowBytes; x++)
            {
                int a = y * rowBytes + x, b = (height - 1 - y) * rowBytes + x;
                byte swap = bytes[a]; bytes[a] = bytes[b]; bytes[b] = swap;
            }
        }
        static void SavePng(string path, int w, int h, byte[] rgba, bool srgb)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false, !srgb);
            try { texture.LoadRawTextureData(rgba); texture.Apply(false, false); File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
        }
        static void ValidateSavedTexture(TextureRecord record)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(record.output);
            Require(texture != null && texture.width == record.width && texture.height == record.height &&
                texture.mipmapCount == record.mipCount && texture.format == TextureFormat.ASTC_4x4 && !texture.isReadable &&
                GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat) == record.srgb, "Saved texture mismatch: " + record.output);
        }

        static void ValidateModel(ModelRecord record, string outputFolder)
        {
            Require(AssetDatabase.AssetPathToGUID(record.movedPath) == record.originalGuid && Hash(record.movedPath) == record.originalSha256,
                "Original source or GUID changed: " + record.name);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(record.movedPath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(record.prefabPath);
            ValidatePair(source, prefab, record);
            record.oldMaterialReferences = record.oldTextureReferences = 0;
            Object[] dependencies = EditorUtility.CollectDependencies(new Object[] { prefab });
            foreach (Object dependency in dependencies)
            {
                string path = AssetDatabase.GetAssetPath(dependency);
                if (dependency is Material && !path.StartsWith(outputFolder + "/", StringComparison.Ordinal)) record.oldMaterialReferences++;
                if (dependency is Texture && !path.StartsWith(outputFolder + "/", StringComparison.Ordinal)) record.oldTextureReferences++;
            }
            Require(record.oldMaterialReferences == 0 && record.oldTextureReferences == 0,
                "Prefab still references original material/texture objects: " + record.name);
            record.dependencies = AssetDatabase.GetDependencies(record.prefabPath, true);
            record.validated = true;
        }
        static void RestoreImportedMaterial(Material original, Material converted, Manifest report)
        {
            var textures = new Dictionary<string, Texture>();
            foreach (string property in converted.GetTexturePropertyNames()) textures[property] = converted.GetTexture(property);
            for (int p = 0; p < original.shader.GetPropertyCount(); p++)
            {
                string property = original.shader.GetPropertyName(p);
                ShaderPropertyType type = original.shader.GetPropertyType(p);
                if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range)
                {
                    float expected = original.GetFloat(property), imported = converted.GetFloat(property);
                    if (!SameFloat(expected, imported) || float.IsNaN(expected) || float.IsInfinity(expected))
                    {
                        string detail = AssetDatabase.GetAssetPath(original) + ":" + original.name + " property=" + property +
                            " source=" + Number(expected) + " imported=" + Number(imported) +
                            (SameFloat(expected, imported) ? " identical non-finite source value retained" : " restored original after material import");
                        report.materialImportCorrections.Add(detail); Debug.Log("[ReefASTC] " + detail);
                    }
                }
            }
            converted.CopyPropertiesFromMaterial(original);
            foreach (var pair in textures) converted.SetTexture(pair.Key, pair.Value);
            converted.renderQueue = original.renderQueue;
            converted.enableInstancing = original.enableInstancing;
            converted.doubleSidedGI = original.doubleSidedGI;
            converted.globalIlluminationFlags = original.globalIlluminationFlags;
            EditorUtility.SetDirty(converted);
            AssetDatabase.SaveAssetIfDirty(converted);
            Save(report);
        }
        // NaN is not equal under ==, but an unchanged NaN is still the same copied property.
        // Opposite infinities, finite/non-finite substitutions and any finite numeric change fail.
        static bool SameFloat(float a, float b) => a.Equals(b);
        static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        static void RequireScalar(Material source, Material output, string property)
        {
            float a = source.GetFloat(property), b = output.GetFloat(property);
            Require(SameFloat(a, b), "Material scalar changed: material=" + source.name + " shader=" + source.shader.name +
                " property=" + property + " source=" + Number(a) + " converted=" + Number(b));
        }

        static void ValidatePair(GameObject source, GameObject prefab, ModelRecord record)
        {
            Require(source != null && prefab != null, "Missing comparison prefab.");
            Transform[] from = source.GetComponentsInChildren<Transform>(true), to = prefab.GetComponentsInChildren<Transform>(true);
            Require(from.Length == to.Length, "Hierarchy count changed.");
            for (int i = 0; i < from.Length; i++)
                Require(from[i].name == to[i].name && from[i].localPosition == to[i].localPosition &&
                    from[i].localRotation == to[i].localRotation && from[i].localScale == to[i].localScale &&
                    from[i].gameObject.activeSelf == to[i].gameObject.activeSelf, "Mesh transform/hierarchy changed.");
            MeshFilter[] originalMeshes = source.GetComponentsInChildren<MeshFilter>(true), outputMeshes = prefab.GetComponentsInChildren<MeshFilter>(true);
            Require(originalMeshes.Length == outputMeshes.Length, "Mesh count changed.");
            long triangles = 0;
            for (int i = 0; i < originalMeshes.Length; i++)
            {
                Require(originalMeshes[i].sharedMesh == outputMeshes[i].sharedMesh, "Mesh is not the exact original object.");
                Mesh mesh = originalMeshes[i].sharedMesh;
                for (int s = 0; s < mesh.subMeshCount; s++) if (mesh.GetTopology(s) == MeshTopology.Triangles) triangles += (long)mesh.GetIndexCount(s) / 3;
            }
            Renderer[] originalRenderers = source.GetComponentsInChildren<Renderer>(true), outputRenderers = prefab.GetComponentsInChildren<Renderer>(true);
            Require(originalRenderers.Length == outputRenderers.Length, "Renderer count changed.");
            var materials = new HashSet<Material>(); var textures = new HashSet<Texture>();
            for (int i = 0; i < originalRenderers.Length; i++)
            {
                Material[] a = originalRenderers[i].sharedMaterials, b = outputRenderers[i].sharedMaterials;
                Require(a.Length == b.Length && originalRenderers[i].GetType() == outputRenderers[i].GetType(), "Renderer/material slots changed.");
                for (int m = 0; m < a.Length; m++)
                {
                    if (a[m] == null) { Require(b[m] == null, "Null material replaced."); continue; }
                    Require(a[m] != b[m] && a[m].shader == b[m].shader && a[m].renderQueue == b[m].renderQueue && a[m].enableInstancing == b[m].enableInstancing &&
                        a[m].doubleSidedGI == b[m].doubleSidedGI && a[m].globalIlluminationFlags == b[m].globalIlluminationFlags &&
                        a[m].shaderKeywords.OrderBy(x => x).SequenceEqual(b[m].shaderKeywords.OrderBy(x => x)), "Shader/render state changed.");
                    materials.Add(b[m]);
                    for (int p = 0; p < a[m].shader.GetPropertyCount(); p++)
                    {
                        string property = a[m].shader.GetPropertyName(p);
                        switch (a[m].shader.GetPropertyType(p))
                        {
                            case ShaderPropertyType.Color: Require(a[m].GetColor(property) == b[m].GetColor(property), "Material color changed: " + property + " source=" + a[m].GetColor(property).ToString("R") + " converted=" + b[m].GetColor(property).ToString("R")); break;
                            case ShaderPropertyType.Vector: Require(a[m].GetVector(property) == b[m].GetVector(property), "Material vector changed: " + property + " source=" + a[m].GetVector(property).ToString("R") + " converted=" + b[m].GetVector(property).ToString("R")); break;
                            case ShaderPropertyType.Float:
                            case ShaderPropertyType.Range: RequireScalar(a[m], b[m], property); break;
                            case ShaderPropertyType.Int: Require(a[m].GetInteger(property) == b[m].GetInteger(property), "Material integer changed: " + property + " source=" + a[m].GetInteger(property) + " converted=" + b[m].GetInteger(property)); break;
                            case ShaderPropertyType.Texture:
                                Texture oldTexture = a[m].GetTexture(property), newTexture = b[m].GetTexture(property);
                                Require(a[m].GetTextureOffset(property) == b[m].GetTextureOffset(property) &&
                                    a[m].GetTextureScale(property) == b[m].GetTextureScale(property), "Texture UV mapping changed.");
                                if (oldTexture == null) Require(newTexture == null, "Empty texture slot changed.");
                                else { Require(oldTexture != newTexture && newTexture is Texture2D &&
                                    oldTexture.width == newTexture.width && oldTexture.height == newTexture.height, "Texture replacement invalid."); textures.Add(newTexture); }
                                break;
                        }
                    }
                }
            }
            if (record != null) { record.rendererCount = originalRenderers.Length; record.meshCount = originalMeshes.Length;
                record.materialCount = materials.Count; record.textureCount = textures.Count; record.triangles = triangles; }
        }

        static void RollbackCore(Manifest report)
        {
            Require(report.mode == "conversion" && report.outputFolder == BakedRoot && report.models.Count == 16, "Not a supported conversion journal.");
            // Validate ALL originals before deleting anything, including after an interrupted conversion.
            foreach (ModelRecord model in report.models)
            {
                string actual = AssetDatabase.GUIDToAssetPath(model.originalGuid);
                Require(actual == model.originalPath || actual == model.movedPath, "Original GUID moved elsewhere; refusing rollback.");
                Require(Hash(actual) == model.originalSha256, "Original modified; refusing rollback: " + actual);
                if (File.Exists(model.prefabPath))
                {
                    string guid = AssetDatabase.AssetPathToGUID(model.prefabPath);
                    Require(!string.IsNullOrEmpty(model.prefabGuid) && guid == model.prefabGuid && Hash(model.prefabPath) == model.prefabSha256, "Generated prefab modified/foreign; refusing rollback.");
                }
            }
            foreach (ModelRecord model in report.models)
            {
                if (File.Exists(model.prefabPath)) Require(AssetDatabase.DeleteAsset(model.prefabPath), "Cannot remove generated prefab.");
                if (AssetDatabase.GUIDToAssetPath(model.originalGuid) == model.movedPath)
                    CheckMove(AssetDatabase.MoveAsset(model.movedPath, model.originalPath));
                Require(AssetDatabase.AssetPathToGUID(model.originalPath) == model.originalGuid && Hash(model.originalPath) == model.originalSha256,
                    "Restored original mismatch.");
            }
            if (AssetDatabase.IsValidFolder(BakedRoot)) Require(AssetDatabase.DeleteAsset(BakedRoot), "Cannot remove generated bake folder.");
            AssetDatabase.SaveAssets(); report.state = "rolled_back"; report.success = false; Save(report); Write(Latest, report);
            Debug.Log("[ReefASTC] rollback completed: original 16 GLBs and GUIDs restored. Backups/evidence and sample assets retained.");
        }
        static void MakeFolder(string path)
        {
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) MakeFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))), "Cannot create " + path);
        }
        static void Save(Manifest report) => Write(report.manifestPath, report);
        static void Write(string path, Manifest report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path + ".tmp", JsonUtility.ToJson(report, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
        }
        static Manifest ReadManifest(string path)
        {
            Require(File.Exists(path), "Missing journal/proof: " + path);
            return JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
        }
        static string Hash(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static string Stamp() => DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("[ReefASTC] " + message); }
        static void CheckMove(string error) => Require(string.IsNullOrEmpty(error), error);
    }
}
#endif

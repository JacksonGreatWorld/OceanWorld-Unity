#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Explicit 4K source recovery. No automatic import/build hooks and no mesh/material changes.</summary>
    public static class ReefNearDetailUpgrade
    {
        const string BakedRoot = "Assets/ArtBaked/ReefAstc";
        const string PrefabRoot = "Assets/Resources/RealisticReef";
        const string SourcePath = "Sources/RealisticReef/coral_cuneata.glb";
        const string SourceSha = "5d9587671d03ebd3c66ce79966640dd7515655c6f3b684c58f027bde450c8d73";
        const string CurrentSourcePath = "Assets/ArtSources/RealisticReef/coral_cuneata_lod0.glb";
        const string CurrentSourceSha = "c0f73a97e8f2347fc1162144aadd401965389034e45f07d8f25211c632d7d41c";
        const string ReadbackShaderPath = "Assets/SwanUnderwater/Editor/ReefAstcReadback.shader";
        static readonly string[] Roles = { "baseColorTexture", "normalTexture", "occlusionTexture" };
        static Material readbackMaterial;
        static bool flipReadbackRows, readbackCalibrated;
        static int calibrationMaxByteError;
        static string Root => Path.GetFullPath(Environment.GetEnvironmentVariable("OCEAN_REEF_DETAIL_REPORT_DIR") ?? "Validation/iteration-26/reef-detail-upgrade");
        static string Latest => Path.Combine(Root, "latest-upgrade.json");

        [Serializable] public sealed class TextureState
        {
            public string path, guid, beforeHash, afterHash, beforeMetaHash, backup;
            public long fileId, oldAstcBytes, newAstcBytes;
            public int beforeWidth, beforeHeight, beforeMips, beforeFilter, beforeAniso;
            public bool srgb;
        }
        [Serializable] public sealed class ProtectedAsset { public string path, hash; }
        [Serializable] public sealed class Quality
        {
            public string role, target, imageSha256;
            public int width, height, mipCount, copyMaxByteError;
            public bool srgb;
            public long astcBytes;
            public double meanByteError, rmsByteError, maxByteError, maxAlphaError;
        }
        [Serializable] public sealed class Report
        {
            public string utc, unity, directory, state, error, source = SourcePath, sourceSha256 = SourceSha;
            public string mappingProof = "Validation/iteration-26/near-detail/cuneata-source-proof.json";
            public bool success;
            public int calibrationMaxByteError, textureCount, originalTextureReferences, originalMaterialReferences;
            public long beforeAstcBytes, afterAstcBytes;
            public List<TextureState> textures = new List<TextureState>();
            public List<ProtectedAsset> protectedAssets = new List<ProtectedAsset>();
            public List<Quality> quality = new List<Quality>();
        }
        [Serializable] sealed class Glb { public ImageRef[] images; public TextureRef[] textures; public BufferView[] bufferViews; public MaterialRef[] materials; }
        [Serializable] sealed class ImageRef { public int bufferView; }
        [Serializable] sealed class TextureRef { public int source; }
        [Serializable] sealed class BufferView { public int byteOffset, byteLength; }
        [Serializable] sealed class TexInfo { public int index; }
        [Serializable] sealed class PbrRef { public TexInfo baseColorTexture; }
        [Serializable] sealed class MaterialRef { public PbrRef pbrMetallicRoughness; public TexInfo normalTexture, occlusionTexture; }

        [MenuItem("Swan/Codex/Reef Detail/Recover source 4K and improve sampling")]
        public static void Upgrade() => Execute(UpgradeCore);
        [MenuItem("Swan/Codex/Reef Detail/Roll back latest detail upgrade")]
        public static void Rollback() => Execute(RollbackCore);

        static void Execute(Action action)
        {
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Edit Mode required.");
                Directory.CreateDirectory(Root); action();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
            finally
            {
                if (readbackMaterial != null) Object.DestroyImmediate(readbackMaterial);
                readbackMaterial = null; readbackCalibrated = false; EditorUtility.ClearProgressBar();
            }
        }

        static void UpgradeCore()
        {
            Require(SystemInfo.supportsAsyncGPUReadback, "Graphics readback required; do not use -nographics.");
            Require(Hash(SourcePath) == SourceSha && Hash(CurrentSourcePath) == CurrentSourceSha, "Source/mapping baseline changed.");
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                directory = Path.Combine(Root, "upgrade-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")), state = "preparing" };
            Directory.CreateDirectory(Path.Combine(report.directory, "originals"));
            var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { BakedRoot }).Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(x => x, StringComparer.Ordinal).Select(AssetDatabase.LoadAssetAtPath<Texture2D>).ToArray();
            Require(textures.Length == 48, "Expected exactly 48 existing baked textures.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoot + "/coral_cuneata_lod0.prefab");
            Require(prefab != null, "Missing current cuneata prefab.");
            Material material = prefab.GetComponentInChildren<Renderer>(true).sharedMaterial;
            var targets = Roles.Select(role => material.GetTexture(role) as Texture2D).ToArray();
            Require(targets.All(x => x != null && x.width == 2048 && x.height == 2048 && x.mipmapCount == 12), "Expected original 2K/12-mip targets; do not rerun a completed upgrade.");
            Require(targets.Distinct().Count() == 3 && targets.All(textures.Contains), "Unexpected texture bindings.");
            for (int i = 0; i < targets.Length; i++) Require(GraphicsFormatUtility.IsSRGBFormat(targets[i].graphicsFormat) == (i == 0), "Unexpected source color space.");
            // Back up every object changed, plus retain hashes of all assets that must stay byte-identical.
            foreach (Texture2D texture in textures)
            {
                Require(texture.format == TextureFormat.ASTC_4x4 && !texture.isReadable, "Unexpected texture format/readability.");
                string path = AssetDatabase.GetAssetPath(texture), guid; long id;
                Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out guid, out id), "Texture identity unavailable.");
                string backup = Path.Combine(report.directory, "originals", guid + ".asset");
                File.Copy(path, backup); File.Copy(path + ".meta", backup + ".meta");
                var row = new TextureState { path = path, guid = guid, fileId = id, beforeHash = Hash(path), beforeMetaHash = Hash(path + ".meta"),
                    backup = backup, beforeWidth = texture.width, beforeHeight = texture.height, beforeMips = texture.mipmapCount,
                    beforeFilter = (int)texture.filterMode, beforeAniso = texture.anisoLevel, srgb = GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat),
                    oldAstcBytes = AstcBytes(texture.width, texture.height, texture.mipmapCount) };
                Require(Hash(backup) == row.beforeHash && Hash(backup + ".meta") == row.beforeMetaHash, "Backup mismatch.");
                report.beforeAstcBytes += row.oldAstcBytes; report.textures.Add(row);
            }
            foreach (string path in Directory.GetFiles(BakedRoot, "*.mat", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(PrefabRoot, "*.prefab", SearchOption.TopDirectoryOnly))
                .Concat(Directory.GetFiles("Assets/ArtSources/RealisticReef", "*.glb", SearchOption.TopDirectoryOnly)))
            {
                report.protectedAssets.Add(new ProtectedAsset { path = path, hash = Hash(path) });
                report.protectedAssets.Add(new ProtectedAsset { path = path + ".meta", hash = Hash(path + ".meta") });
            }
            Save(report);
            var replacements = new List<Texture2D>();
            try
            {
                CalibrateReadback(); report.calibrationMaxByteError = calibrationMaxByteError;
                byte[][] images = ExtractImages(File.ReadAllBytes(SourcePath));
                for (int i = 0; i < 3; i++) replacements.Add(BuildTexture(images[i], targets[i], Roles[i], report));
                // All three source/decode quality checks passed before the first live texture mutation.
                report.state = "applying"; Save(report);
                for (int i = 0; i < 3; i++) EditorUtility.CopySerialized(replacements[i], targets[i]);
                foreach (Texture2D texture in textures)
                {
                    texture.filterMode = FilterMode.Trilinear; texture.anisoLevel = 16;
                    EditorUtility.SetDirty(texture); AssetDatabase.SaveAssetIfDirty(texture);
                    TextureState row = report.textures.First(x => x.path == AssetDatabase.GetAssetPath(texture));
                    row.afterHash = Hash(row.path); Save(report);
                }
                foreach (TextureState row in report.textures)
                {
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(row.path);
                    string guid; long id; Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out guid, out id) && guid == row.guid && id == row.fileId, "Texture GUID/fileID changed.");
                    bool enlarged = targets.Contains(texture);
                    Require(texture.width == (enlarged ? 4096 : row.beforeWidth) && texture.height == (enlarged ? 4096 : row.beforeHeight) &&
                        texture.mipmapCount == (enlarged ? 13 : row.beforeMips) && texture.format == TextureFormat.ASTC_4x4 && !texture.isReadable &&
                        GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat) == row.srgb && texture.filterMode == FilterMode.Trilinear && texture.anisoLevel == 16,
                        "Saved format/size/sampling mismatch: " + row.path);
                    Require(Hash(row.path + ".meta") == row.beforeMetaHash, "Texture meta changed.");
                    row.newAstcBytes = AstcBytes(texture.width, texture.height, texture.mipmapCount); report.afterAstcBytes += row.newAstcBytes;
                }
                foreach (var item in report.protectedAssets) Require(Hash(item.path) == item.hash, "Protected mesh/material/prefab changed: " + item.path);
                var dependencies = new HashSet<Texture>();
                foreach (string path in Directory.GetFiles(PrefabRoot, "*.prefab"))
                    foreach (Object dependency in EditorUtility.CollectDependencies(new Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(path) }))
                    {
                        string depPath = AssetDatabase.GetAssetPath(dependency);
                        if (dependency is Texture) { dependencies.Add((Texture)dependency); if (!depPath.StartsWith(BakedRoot + "/", StringComparison.Ordinal)) report.originalTextureReferences++; }
                        if (dependency is Material && !depPath.StartsWith(BakedRoot + "/", StringComparison.Ordinal)) report.originalMaterialReferences++;
                    }
                report.textureCount = dependencies.Count;
                Require(report.textureCount == 48 && report.originalTextureReferences == 0 && report.originalMaterialReferences == 0, "Original texture/material dependency reintroduced.");
                Require(report.afterAstcBytes - report.beforeAstcBytes == 48L * 1024 * 1024, "Unexpected ASTC budget delta.");
                report.success = true; report.state = "upgraded"; Save(report);
                Debug.Log("[ReefDetail] PASS: 3 genuine 4K source textures, 48 trilinear/aniso16, GUID/fileID/mesh/material preserved; +48MiB ASTC. " + Path.Combine(report.directory, "report.json"));
            }
            catch (Exception e)
            {
                report.error = e.ToString(); report.state = "failed";
                foreach (var row in report.textures) if (File.Exists(row.path)) row.afterHash = Hash(row.path);
                Save(report); Debug.LogError("[ReefDetail] Explicit Rollback restores all 48 original texture assets from " + report.directory); throw;
            }
            finally { foreach (Texture2D texture in replacements) Object.DestroyImmediate(texture); }
        }

        static Texture2D BuildTexture(byte[] encoded, Texture2D target, string role, Report report)
        {
            bool srgb = GraphicsFormatUtility.IsSRGBFormat(target.graphicsFormat);
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, true, !srgb);
            Texture2D copy = null;
            try
            {
                Require(ImageConversion.LoadImage(source, encoded, false), "Original JPEG decode failed.");
                source.Apply(true, false); // Generate the complete 13-level chain from real 4K mip0; data maps remain linear.
                Require(source.width == 4096 && source.height == 4096 && source.mipmapCount == 13 && GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat) == srgb, "Not a native 4K source with full mip chain.");
                copy = new Texture2D(4096, 4096, TextureFormat.RGBA32, 13, !srgb) { name = target.name,
                    filterMode = FilterMode.Trilinear, anisoLevel = 16, wrapModeU = target.wrapModeU, wrapModeV = target.wrapModeV,
                    wrapModeW = target.wrapModeW, mipMapBias = target.mipMapBias };
                GraphicsFormat format = srgb ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
                var raw = new byte[13][];
                for (int mip = 0; mip < 13; mip++) { raw[mip] = ReadPixels(source, mip, format); copy.SetPixelData(raw[mip], mip); }
                copy.Apply(false, false);
                var quality = new Quality { role = role, target = AssetDatabase.GetAssetPath(target), imageSha256 = HashBytes(encoded), width = 4096, height = 4096, mipCount = 13, srgb = srgb };
                for (int mip = 0; mip < 13; mip++) quality.copyMaxByteError = Math.Max(quality.copyMaxByteError, MaxError(raw[mip], ReadPixels(copy, mip, format)));
                Require(quality.copyMaxByteError <= 1, "Readback changed source encoding.");
                EditorUtility.CompressTexture(copy, TextureFormat.ASTC_4x4, TextureCompressionQuality.Best);
                copy.Apply(false, false);
                quality.astcBytes = copy.GetRawTextureData<byte>().Length;
                Require(copy.format == TextureFormat.ASTC_4x4 && quality.astcBytes == AstcBytes(4096, 4096, 13), "ASTC compression failed.");
                double sum = 0, squared = 0; long count = 0;
                for (int mip = 0; mip < 13; mip++)
                {
                    byte[] decoded = ReadPixels(copy, mip, format); Require(decoded.Length == raw[mip].Length, "Decoded length mismatch.");
                    for (int p = 0; p < decoded.Length; p++)
                    {
                        int delta = Math.Abs(decoded[p] - raw[mip][p]); sum += delta; squared += delta * delta; count++;
                        quality.maxByteError = Math.Max(quality.maxByteError, delta);
                        if ((p & 3) == 3) quality.maxAlphaError = Math.Max(quality.maxAlphaError, delta);
                    }
                    if (mip == 0) { SavePng(Path.Combine(report.directory, role + "_source4k.png"), 4096, 4096, raw[0], srgb); SavePng(Path.Combine(report.directory, role + "_astc4k.png"), 4096, 4096, decoded, srgb); }
                }
                quality.meanByteError = sum / count; quality.rmsByteError = Math.Sqrt(squared / count);
                Require(quality.meanByteError <= 8 && quality.rmsByteError <= 12 && quality.maxAlphaError <= 16, "ASTC quality gate failed: " + role);
                report.quality.Add(quality); Save(report); copy.Apply(false, true);
                Texture2D result = copy; copy = null; return result;
            }
            finally { Object.DestroyImmediate(source); if (copy != null) Object.DestroyImmediate(copy); }
        }

        static byte[][] ExtractImages(byte[] file)
        {
            Require(BitConverter.ToUInt32(file, 0) == 0x46546c67 && BitConverter.ToInt32(file, 4) == 2 && BitConverter.ToInt32(file, 16) == 0x4e4f534a, "Unsupported source GLB.");
            int jsonLength = BitConverter.ToInt32(file, 12), binaryHeader = 20 + jsonLength;
            Require(BitConverter.ToInt32(file, binaryHeader + 4) == 0x004e4942, "Missing binary buffer.");
            Glb data = JsonUtility.FromJson<Glb>(Encoding.UTF8.GetString(file, 20, jsonLength));
            var material = data.materials[0]; int[] indices = { material.pbrMetallicRoughness.baseColorTexture.index, material.normalTexture.index, material.occlusionTexture.index };
            var result = new byte[3][];
            for (int i = 0; i < 3; i++)
            {
                BufferView view = data.bufferViews[data.images[data.textures[indices[i]].source].bufferView];
                Require(view.byteLength > 0 && binaryHeader + 8L + view.byteOffset + view.byteLength <= file.Length, "Invalid embedded image span.");
                result[i] = new byte[view.byteLength]; Buffer.BlockCopy(file, binaryHeader + 8 + view.byteOffset, result[i], 0, view.byteLength);
            }
            return result;
        }

        static void RollbackCore()
        {
            Require(File.Exists(Latest), "No detail-upgrade journal.");
            Report report = JsonUtility.FromJson<Report>(File.ReadAllText(Latest));
            Require(report.textures.Count == 48 && report.state != "rolled_back", "Invalid/already restored journal.");
            foreach (TextureState row in report.textures)
            {
                Require(AssetDatabase.AssetPathToGUID(row.path) == row.guid && Hash(row.path + ".meta") == row.beforeMetaHash, "Foreign asset identity; refusing rollback.");
                Require(Hash(row.backup) == row.beforeHash && Hash(row.backup + ".meta") == row.beforeMetaHash, "Backup damaged.");
                string current = Hash(row.path); Require(current == row.beforeHash || current == row.afterHash, "Asset was edited after upgrade; refusing overwrite.");
            }
            foreach (TextureState row in report.textures)
            {
                File.Copy(row.backup, row.path, true); File.Copy(row.backup + ".meta", row.path + ".meta", true);
                AssetDatabase.ImportAsset(row.path, ImportAssetOptions.ForceUpdate);
                Require(Hash(row.path) == row.beforeHash && AssetDatabase.AssetPathToGUID(row.path) == row.guid, "Restore mismatch.");
            }
            report.state = "rolled_back"; report.success = false; Save(report); Debug.Log("[ReefDetail] Restored all 48 texture assets; mesh/material/prefab unchanged.");
        }
        static long AstcBytes(int w, int h, int mips) { long size = 0; for (int mip = 0; mip < mips; mip++) size += (long)((Math.Max(1, w >> mip) + 3) / 4) * ((Math.Max(1, h >> mip) + 3) / 4) * 16; return size; }
        static string Hash(string path) => HashBytes(File.ReadAllBytes(path));
        static string HashBytes(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        static void Save(Report report)
        {
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(report.directory, "report.json"), json); File.WriteAllText(Latest, json);
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("[ReefDetail] " + message); }
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
    }
}
#endif

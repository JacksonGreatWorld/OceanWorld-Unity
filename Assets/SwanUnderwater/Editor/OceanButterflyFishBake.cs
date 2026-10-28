#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Explicit, closed-source Collada conversion. Static source + procedural swim, never an authored rig.</summary>
    public static class OceanButterflyFishBake
    {
        const string Package = "Sources/MarineExpansion/butterfly-1a5d97ef";
        const string Source = Package + "/extracted/nested/model/";
        const string Dae = Source + "model.dae";
        const string Baked = "Assets/ArtBaked/MarineExpansion/butterfly-fish";
        const string Prefab = "Assets/Resources/MarineExpansion/butterfly-fish.prefab";
        const string Geometry = "Assets/Resources/MarineExpansion/butterfly-fish-geometry.json";
        const string ShaderPath = "Assets/Resources/Shaders/ButterflyFishSwim.shader";
        const string Portable = "Sources/MarineExpansion/butterfly-native-admission.json";
        const string ArchiveSha = "e34bf03aa1866b10df8b1efbfadaea0aa62952152272c094bcb3ebcbab01ef80";
        const string DaeSha = "e859a429418dcaee2323d5b5ad5a9ab818ec336e8e65801e8a514601cff62927";
        const string Uid = "1a5d97ef77004074a6b4277cd7e43200";
        static readonly XNamespace Collada = "http://www.collada.org/2005/11/COLLADASchema";
        static readonly string[] Maps = { "Fish_albedo.jpg", "Fish_opacity.jpg", "Fish_normal.png", "Fish_roughness.jpg", "Fish_metallic.jpg" };
        static readonly string[] MapHashes = {
            "b8a66eb8b206cfb93b0abb16d834f6adb807f24cc936994cce5a58d46ccc2a39",
            "eafcdc4f88f197f43fee3e77fcd5c46170f95bbb74d0edffccf4b950c6c1c514",
            "900fdb06c0436fd4a45cd2096c303e99438679e277038edd219be8041500a800",
            "c1e0cadf9e1aa448079dfbb6b28a57379d76900ac4ebb9edbdbcac10433dab56",
            "13742ebdff2fae860a96e1ff6f4c703f74366b47c3a5f563a18de28951aca5a2" };
        static readonly string[] Protected = { "Assets/Resources/Fish", "Assets/Resources/RealisticReef", "Assets/ArtBaked/MarketReef", "Assets/ArtBaked/MarineExpansion/mackerel", "Assets/Resources/MarineExpansion/mackerel.prefab", "Assets/Resources/MarineExpansion/mackerel-geometry.json" };
        static Material decoder;
        static bool calibrated, flipRows;
        static Pending pending;
        static string Output => Path.GetFullPath(Arg("-oceanButterflyBakeOutput", "Validation/butterfly-fish-bake"));
        static string Latest => Path.Combine(Output, "latest.json");
        [Serializable] public sealed class FileRecord { public string path, sha256; public long bytes; }
        [Serializable] sealed class SourceReceipt { public string uid = "", author = "", license = "", source_url = "", sha256 = "", license_metadata_file = "", license_metadata_sha256 = ""; public long bytes = 0; public Roster[] roster = Array.Empty<Roster>(); public Nested nested_archive = new Nested(); public FileRecord[] extracted_file_roster = Array.Empty<FileRecord>(); }
        [Serializable] sealed class Roster { public string path = ""; public long bytes = 0, crc = 0; }
        [Serializable] sealed class Nested { public string path = ""; public Roster[] roster = Array.Empty<Roster>(); }
        [Serializable] public sealed class TextureRecord
        {
            public string role, path, format, graphicsFormat;
            public bool srgb;
            public int width, height, mips, copyMaximumError, rgbMaximumError, alphaMaximumError;
            public long payloadBytes;
            public double meanError, rmsError, alphaMeanError, normalRmsDegrees, normalP99Degrees;
        }
        [Serializable] public sealed class Report
        {
            public bool publicationProjection;
            public string sourceReportSha256, publicationScope;
            public int schemaVersion = 1;
            public string tool = "OceanButterflyFishBake", uid = Uid, utc, unity, directory, state, error;
            public string author, license, sourceUrl, sourceSha256 = DaeSha, packageSha256 = ArchiveSha, shaderSha256;
            public string prefabPath = Prefab, geometryPath = Geometry;
            public string animationPolicy = "No source skin or animation. Static original geometry, canonical +Z nose, GPU procedural tail/pectoral motion through _SwimMotion. No authored-animation claim.";
            public string coordinatePolicy = "Original Collada Y_UP and identity linear scene matrix; canonical X=-sourceZ,Y=sourceY,Z=-sourceX, handedness-reflected with reversed triangle winding. Uniform normalization to one metre nose-to-tail X-range, AABB centred; original UVs/normals/tangent frame retained.";
            public string texturePolicy = "Original 1024px images, no upscaling. Base=original albedo RGB + original opacity R. Normal=lossless RGBA32 original tangent RGB, dedicated RG positive-Z decode independent of URP AG policy; ASTC was rejected after measured normal-angle loss. PBR=(0,roughness,metallic,1). Base/PBR native ASTC4x4, complete mips, calibrated exact-mip GPU readback. Alpha-to-coverage + .20 clip for translucent authored fin coverage, no transparent sorting.";
            public bool success, sourceAnimated;
            public int vertices, triangles, finalizationUpdates;
            public Vector3 sourceMin, sourceMax, canonicalMin, canonicalMax;
            public Vector3 shaderBodyHalfExtents, navigationSize;
            public float firstPoseDiagonal, templateNoseTailLength = 1f, recommendedNoseTailLength = .24f, recommendedDiagonal;
            public double normalFaceAlignment;
            public List<FileRecord> sourceFiles = new List<FileRecord>(), protectedFiles = new List<FileRecord>(), after = new List<FileRecord>();
            public List<TextureRecord> textures = new List<TextureRecord>();
            public List<string> images = new List<string>(), plannedPaths = new List<string>();
        }
        sealed class Pending { public Report report; public Exception error; public List<FileRecord> snapshot; public int ticks, stable; }

        [MenuItem("Swan/Codex/Marine Expansion/Admit static butterfly fish")]
        public static void Run()
        {
            Report report = null;
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode && pending == null, "Edit Mode with no pending transaction required.");
                Require(!Environment.GetCommandLineArgs().Contains("-quit"), "Omit -quit; deferred material serialization must stabilize before exit.");
                Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && SystemInfo.supportsAsyncGPUReadback, "Graphics and GPU readback required; omit -nographics.");
                SourceReceipt receipt = VerifySources();
                if (Owned().Any())
                {
                    ValidateForBuild(); Debug.Log("BUTTERFLY_BAKE_UNCHANGED " + Latest); Exit(0); return;
                }
                report = new Report { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                    directory = Path.Combine(Output, "butterfly-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")), state = "preparing",
                    author = receipt.author, license = receipt.license, sourceUrl = receipt.source_url, shaderSha256 = Hash(ShaderPath) };
                report.sourceFiles = receipt.extracted_file_roster.Select(x => new FileRecord { path = Package + "/" + x.path, sha256 = x.sha256, bytes = x.bytes }).ToList();
                report.protectedFiles = Snapshot(Protected.SelectMany(Files)); Save(report);
                Folder(Baked); Folder("Assets/Resources/MarineExpansion");
                Mesh mesh = ReadSourceMesh(report); string meshPath = Baked + "/butterfly-canonical.asset"; Plan(meshPath, report); AssetDatabase.CreateAsset(mesh, meshPath);
                Calibrate();
                var images = new Dictionary<string, Color32[]>();
                foreach (string map in Maps) images.Add(map, Decode(Source + "textures/" + map));
                Texture2D color = BakeTexture("base-opacity", images, report);
                Texture2D normal = BakeTexture("normal-rg", images, report);
                Texture2D pbr = BakeTexture("metallic-roughness", images, report);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath); Require(shader != null && shader.isSupported, "Dedicated butterfly shader missing/unsupported.");
                var material = new Material(shader) { name = "Butterfly fish original PBR + authored fin opacity", enableInstancing = true };
                material.SetTexture("_BaseMap", color); material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_BumpMap", normal); material.SetFloat("_BumpScale", 1f);
                material.SetTexture("_MetallicGlossMap", pbr); material.SetFloat("_Metallic", 1f); material.SetFloat("_Smoothness", 1f);
                material.SetVector("_FishBody", new Vector4(0, report.shaderBodyHalfExtents.x, report.shaderBodyHalfExtents.y, report.shaderBodyHalfExtents.z));
                material.SetVector("_FishFins", new Vector4(.22f, .009f, 2.4f, .04f));
                material.SetFloat("_SwingAmp", .022f); material.SetFloat("_SwingFreq", 4.1f); material.SetFloat("_VerticalBob", .003f); material.SetFloat("_FishHalfLength", .5f);
                material.SetFloat("_Cutoff", .20f); material.renderQueue = (int)RenderQueue.AlphaTest;
                Plan(Baked + "/butterfly-pbr.mat", report); AssetDatabase.CreateAsset(material, Baked + "/butterfly-pbr.mat");
                // Bounds enclose analytic worst-case current OceanFishMotion stroke<=1.16 and turn<=1.
                // .022*1.16*(1+.04+.32) + pectoral lateral .009*.2 < .037m; .003+.009 < .012m.
                Bounds envelope = mesh.bounds; envelope.Expand(new Vector3(.082f, .032f, .004f)); envelope.size *= 1.02f;
                var geometry = new OceanAuthoredFishGeometry { sourceMeshSha256 = DaeSha, firstPoseDiagonal = mesh.bounds.size.magnitude,
                    navigationCenter = envelope.center, navigationSize = envelope.size };
                Require(geometry.IsValid, "Invalid canonical procedural envelope.");
                report.navigationSize = envelope.size; report.firstPoseDiagonal = geometry.firstPoseDiagonal;
                report.recommendedDiagonal = report.recommendedNoseTailLength * report.firstPoseDiagonal;
                Plan(Geometry, report); File.WriteAllText(Geometry, JsonUtility.ToJson(geometry, true)); AssetDatabase.ImportAsset(Geometry, ImportAssetOptions.ForceSynchronousImport);
                GameObject fish = new GameObject("Butterfly fish (static source, procedural swim)");
                try
                {
                    fish.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = fish.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                    Plan(Prefab, report); Require(PrefabUtility.SaveAsPrefabAsset(fish, Prefab) != null, "Prefab save failed.");
                }
                finally { Object.DestroyImmediate(fish); }
                AssetDatabase.SaveAssets(); ValidateOutputs(report); RenderPreview(report); BeginFinalization(report, null);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (report != null) BeginFinalization(report, exception); else { Release(); Exit(1); if (!Application.isBatchMode) throw; }
            }
        }

        static SourceReceipt VerifySources()
        {
            var receipt = JsonUtility.FromJson<SourceReceipt>(File.ReadAllText(Package + "/receipt.json"));
            Require(receipt != null && receipt.uid == Uid && receipt.license == "CC-BY-4.0" && receipt.author == "slleggott (@slleggott)" && receipt.sha256 == ArchiveSha && receipt.bytes == 4627295,
                "Exact source identity/commercial CC-BY receipt changed.");
            Require(Hash(Package + "/source-original.zip") == ArchiveSha && Hash(Dae) == DaeSha &&
                receipt.source_url == "https://sketchfab.com/3d-models/butterfly-fish-" + Uid &&
                receipt.nested_archive.path == "extracted/source/model.zip" &&
                Hash(Package + "/extracted/source/model.zip") == "d912f945654ab4ac36d51935ecd37cb8bc8b86ca464bd3028414a761df721ed0", "Original package/DAE/nested SHA or source URL mismatch.");
            for (int i = 0; i < Maps.Length; i++) Require(Hash(Source + "textures/" + Maps[i]) == MapHashes[i], "Exact original texture SHA mismatch: " + Maps[i]);
            Require(Hash(receipt.license_metadata_file) == receipt.license_metadata_sha256 && receipt.license_metadata_sha256 == "2d131886344f048094852b1ecc3d924b7360be41223e327004f79f875e8cb233",
                "Canonical license metadata changed.");
            Require(receipt.roster.Length == 6 && receipt.nested_archive.roster.Length == 6 && receipt.extracted_file_roster.Length == 12, "Canonical closed source roster changed.");
            VerifyZip(Package + "/source-original.zip", receipt.roster); VerifyZip(Package + "/" + receipt.nested_archive.path, receipt.nested_archive.roster);
            foreach (FileRecord file in receipt.extracted_file_roster)
            {
                Require(!file.path.Contains("..") && file.path.StartsWith("extracted/", StringComparison.Ordinal), "Unsafe source path.");
                string path = Package + "/" + file.path;
                Require(new FileInfo(path).Length == file.bytes && Hash(path) == file.sha256, "Extracted original changed: " + path);
            }
            return receipt;
        }
        static void VerifyZip(string path, Roster[] roster)
        {
            using (var archive = ZipFile.OpenRead(path))
            {
                Require(archive.Entries.Count == roster.Length, "ZIP contains unknown members.");
                foreach (Roster row in roster)
                {
                    var entry = archive.GetEntry(row.path); Require(entry != null && entry.Length == row.bytes, "ZIP member changed: " + row.path);
                    using (var input = entry.Open()) using (var output = new MemoryStream())
                    { input.CopyTo(output); Require(Crc(output.ToArray()) == (uint)row.crc, "ZIP CRC failed: " + row.path); }
                }
            }
        }

        static Mesh ReadSourceMesh(Report report)
        {
            XDocument document;
            using (var reader = XmlReader.Create(Dae, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document = XDocument.Load(reader);
            Require(document.Root.Attribute("version").Value == "1.4.1" && document.Descendants(Collada + "up_axis").Single().Value == "Y_UP" &&
                document.Descendants(Collada + "unit").Single().Attribute("meter").Value == "1.000000", "Reviewed coordinate metadata changed.");
            Require(!document.Descendants(Collada + "skin").Any() && !document.Descendants(Collada + "animation").Any(), "Source became rigged/animated; static policy invalid.");
            Require(document.Descendants(Collada + "geometry").Count() == 1 && document.Descendants(Collada + "node").Count() == 1, "Unexpected model hierarchy.");
            float[] matrix = Numbers(document.Descendants(Collada + "node").Single().Element(Collada + "matrix").Value);
            int[] identity = { 0, 5, 10, 15 }; for (int i = 0; i < 16; i++) if (i != 3 && i != 7 && i != 11) Require(Mathf.Abs(matrix[i] - (identity.Contains(i) ? 1f : 0f)) < .00001f, "Unexpected Collada scene rotation/scale.");
            XElement sourceMesh = document.Descendants(Collada + "mesh").Single();
            var sources = sourceMesh.Elements(Collada + "source").ToDictionary(x => x.Attribute("id").Value, x => Numbers(x.Element(Collada + "float_array").Value));
            float[] position = sources["meshId0-positions"], normals = sources["meshId0-normals"], tangents = sources["meshId0-tangents"], bitangents = sources["meshId0-bitangents"], uv = sources["meshId0-tex0"];
            Require(position.Length == 2095 * 3 && normals.Length == position.Length && tangents.Length == position.Length && bitangents.Length == position.Length && uv.Length == 2095 * 2, "Exact source vertex channels changed.");
            XElement polygon = sourceMesh.Element(Collada + "polylist");
            Require(polygon != null && polygon.Attribute("count").Value == "3688" && polygon.Elements(Collada + "input").Count() == 5 &&
                polygon.Elements(Collada + "input").All(x => x.Attribute("offset").Value == "0"), "Only the reviewed shared-index source is supported.");
            Require(Numbers(polygon.Element(Collada + "vcount").Value).Length == 3688 && Numbers(polygon.Element(Collada + "vcount").Value).All(x => x == 3), "Source polygon topology changed.");
            int[] indices = polygon.Element(Collada + "p").Value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            Require(indices.Length == 3688 * 3 && indices.All(x => x >= 0 && x < 2095), "Triangle index contract changed.");
            var vertices = new Vector3[2095]; var ns = new Vector3[2095]; var ts = new Vector4[2095]; var uvs = new Vector2[2095];
            Bounds original = new Bounds(Read3(position, 0), Vector3.zero); for (int i = 0; i < vertices.Length; i++) original.Encapsulate(Read3(position, i));
            Require(Vector3.Distance(original.min, new Vector3(-939.609741f, -718.5979f, -702.781799f)) < .01f &&
                Vector3.Distance(original.max, new Vector3(1275.359619f, 976.713867f, 702.781921f)) < .01f, "Reviewed original geometry bounds changed.");
            float length = original.size.x; Vector3 centre = Convert(original.center);
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = (Convert(Read3(position, i)) - centre) / length; ns[i] = Convert(Read3(normals, i)).normalized;
                Vector3 tangent = Convert(Read3(tangents, i)).normalized, binormal = Convert(Read3(bitangents, i)).normalized;
                Require(Finite(vertices[i]) && Finite(ns[i]) && tangent.sqrMagnitude > .9f && binormal.sqrMagnitude > .9f, "Nonfinite or empty source tangent frame.");
                ts[i] = new Vector4(tangent.x, tangent.y, tangent.z, Vector3.Dot(Vector3.Cross(ns[i], tangent), binormal) >= 0 ? 1 : -1);
                uvs[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]); Require(Finite(uvs[i].x) && Finite(uvs[i].y), "Nonfinite source UV.");
            }
            // Reflection reverses chirality; preserve source outward normals by reversing each triangle.
            int aligned = 0;
            for (int i = 0; i < indices.Length; i += 3)
            {
                int swap = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = swap;
                Vector3 face = Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]], vertices[indices[i + 2]] - vertices[indices[i]]);
                Require(face.sqrMagnitude > .00000000000001f, "Degenerate source triangle.");
                if (Vector3.Dot(face, ns[indices[i]] + ns[indices[i + 1]] + ns[indices[i + 2]]) > 0f) aligned++;
            }
            report.normalFaceAlignment = aligned / 3688.0; Require(report.normalFaceAlignment > .995, "Source surface normals/winding disagree.");
            var mesh = new Mesh { name = "Original butterfly fish canonical +Z, 3688 triangles" };
            mesh.vertices = vertices; mesh.normals = ns; mesh.tangents = ts; mesh.uv = uvs; mesh.triangles = indices; mesh.RecalculateBounds();
            report.vertices = mesh.vertexCount; report.triangles = indices.Length / 3; report.sourceMin = original.min; report.sourceMax = original.max;
            report.canonicalMin = mesh.bounds.min; report.canonicalMax = mesh.bounds.max; report.shaderBodyHalfExtents = mesh.bounds.extents;
            Require(Mathf.Abs(mesh.bounds.size.z - 1f) < .00001f && mesh.bounds.center.magnitude < .00001f, "Canonical body length/centre failed.");
            return mesh;
        }
        static Vector3 Convert(Vector3 p) => new Vector3(-p.z, p.y, -p.x);
        static Vector3 Read3(float[] a, int vertex) => new Vector3(a[vertex * 3], a[vertex * 3 + 1], a[vertex * 3 + 2]);
        static float[] Numbers(string text) => text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        static Color32[] Decode(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try { Require(ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false) && texture.width == 1024 && texture.height == 1024, "Original 1K image decode changed: " + path); return texture.GetPixels32(); }
            finally { Object.DestroyImmediate(texture); }
        }

        static Texture2D BakeTexture(string role, Dictionary<string, Color32[]> images, Report report)
        {
            bool srgb = role == "base-opacity", lossless = role == "normal-rg"; var pixels = new Color32[1024 * 1024];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 color = images["Fish_albedo.jpg"][i], normal = images["Fish_normal.png"][i];
                pixels[i] = srgb ? new Color32(color.r, color.g, color.b, images["Fish_opacity.jpg"][i].r) : role == "normal-rg" ?
                    new Color32(normal.r, normal.g, normal.b, 255) : new Color32(0, images["Fish_roughness.jpg"][i].r, images["Fish_metallic.jpg"][i].r, 255);
            }
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, true, !srgb)
            { name = "Butterfly original " + role + (lossless ? " lossless RGBA32" : " ASTC4x4"), wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 16 };
            try
            {
                texture.SetPixels32(pixels); texture.Apply(true, false);
                var record = new TextureRecord { role = role, path = Baked + "/" + role + (lossless ? "-lossless.asset" : "-astc.asset"), srgb = srgb, width = 1024, height = 1024, mips = texture.mipmapCount };
                Require(record.mips == 11, "Complete 1K mip chain required."); var before = new byte[record.mips][];
                for (int mip = 0; mip < record.mips; mip++)
                {
                    before[mip] = texture.GetPixelData<byte>(mip).ToArray(); record.copyMaximumError = Math.Max(record.copyMaximumError, MaximumError(before[mip], Read(texture, mip, srgb)));
                    int size = Math.Max(1, 1024 >> mip); record.payloadBytes += lossless ? (long)size * size * 4 : (long)((size + 3) / 4) * ((size + 3) / 4) * 16;
                }
                Require(record.copyMaximumError <= 1, "Independent source-copy/color/mip calibration failed.");
                if (!lossless) EditorUtility.CompressTexture(texture, TextureFormat.ASTC_4x4, TextureCompressionQuality.Best); texture.Apply(false, false);
                Require(texture.format == (lossless ? TextureFormat.RGBA32 : TextureFormat.ASTC_4x4) && texture.GetRawTextureData<byte>().Length == record.payloadBytes && texture.mipmapCount == 11 &&
                    GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat) == srgb, "Native ASTC payload/encoding changed.");
                double absolute = 0, squared = 0, alpha = 0, normalSquared = 0; long count = 0, alphaCount = 0; var normalErrors = new List<float>();
                for (int mip = 0; mip < record.mips; mip++)
                {
                    byte[] after = Read(texture, mip, srgb); Require(after.Length == before[mip].Length, "Readback byte roster changed.");
                    for (int i = 0; i < after.Length; i++)
                    {
                        int delta = Math.Abs(after[i] - before[mip][i]); absolute += delta; squared += delta * delta; count++;
                        if ((i & 3) == 3) { record.alphaMaximumError = Math.Max(record.alphaMaximumError, delta); alpha += delta; alphaCount++; }
                        else record.rgbMaximumError = Math.Max(record.rgbMaximumError, delta);
                    }
                    if (role == "normal-rg") for (int i = 0; i < after.Length; i += 4)
                    {
                        float error = Vector3.Angle(Normal(before[mip][i], before[mip][i + 1]), Normal(after[i], after[i + 1]));
                        normalErrors.Add(error); normalSquared += error * error;
                    }
                }
                record.meanError = absolute / count; record.rmsError = Math.Sqrt(squared / count); record.alphaMeanError = alpha / alphaCount;
                if (normalErrors.Count > 0) { normalErrors.Sort(); record.normalRmsDegrees = Math.Sqrt(normalSquared / normalErrors.Count); record.normalP99Degrees = normalErrors[(int)((normalErrors.Count - 1) * .99)]; }
                Debug.Log("[ButterflyBake] " + role + " mean=" + record.meanError + " rms=" + record.rmsError + " alphaMean=" + record.alphaMeanError + " normalRMS=" + record.normalRmsDegrees + " normalP99=" + record.normalP99Degrees);
                Require(record.meanError <= 6 && record.rmsError <= 10 && (!lossless || record.rgbMaximumError <= 1) && (srgb ? record.alphaMeanError <= 4 : record.alphaMaximumError <= 1) &&
                    (role != "normal-rg" || record.normalRmsDegrees <= 3 && record.normalP99Degrees <= 8), "Source-fidelity ASTC texture gate failed: " + role);
                record.format = texture.format.ToString(); record.graphicsFormat = texture.graphicsFormat.ToString(); texture.Apply(false, true);
                Plan(record.path, report); AssetDatabase.CreateAsset(texture, record.path); report.textures.Add(record); Save(report); return texture;
            }
            finally { if (!AssetDatabase.Contains(texture)) Object.DestroyImmediate(texture); }
        }
        static Vector3 Normal(byte r, byte g) { float x = r / 127.5f - 1f, y = g / 127.5f - 1f; return new Vector3(x, y, Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y))).normalized; }

        /// <summary>Verify the published projection and every owned output, without re-admitting omitted download archives.
        /// Retains the original measured model, texture, pose and animation gates; performs no new sampling or writes.</summary>
        public static void ValidatePublishedAssets()
        {
            Require(pending == null, "Admission is still finalizing.");
            Require(File.Exists(Portable) && Hash(Portable) == "1616ff7de82d699a5ae6525a010237012b468b58ec1f4bd98bafdb21f16627a9",
                "Missing or changed published admission projection.");
            Report report = JsonUtility.FromJson<Report>(File.ReadAllText(Portable));
            Require(report != null && report.publicationProjection &&
                report.sourceReportSha256 == "275a7b2c1c04bbfd5bbd19a3221874cbd28b681d05d28fbb799ccca6d10ddb5e",
                "Receipt must identify the exact original admission as a published projection.");
            Require(report.tool == "OceanButterflyFishBake", "Published receipt tool differs.");
            Require(report.uid == Uid && report.packageSha256 == ArchiveSha && report.sourceSha256 == DaeSha, "Published butterfly source identity differs.");
            Require(report.success && report.state == "admitted" && report.after != null && report.after.Count > 0,
                "Published projection is not a completed admission.");
            VerifyFiles(report.after);
            Require(Equal(report.after, Snapshot(Owned())), "Published owned output roster changed.");
            ValidateOutputs(report);
            Debug.Log("OceanButterflyFishBake PUBLISHED_ASSETS_PASS: sealed original output bytes and model/texture/animation gates; source download archives and headset acceptance are not revalidated.");
        }

        public static void ValidateForBuild()
        {
            Require(pending == null, "Admission still finalizing."); VerifySources(); Report report = ReadLatest();
            Require(report.success && report.state == "admitted", "No completed butterfly admission."); VerifyFiles(report.after);
            Require(Equal(report.after, Snapshot(Owned())), "Owned output roster changed."); ValidateOutputs(report);
            Debug.Log("[ButterflyBake] Read-only admission PASS: licensed static butterfly, procedural GPU swim, original 1K PBR/opacity; no native rig claim.");
        }
        static void ValidateOutputs(Report report)
        {
            Require(report.uid == Uid && report.sourceSha256 == DaeSha && !report.sourceAnimated && report.vertices == 2095 && report.triangles == 3688 &&
                report.shaderSha256 == Hash(ShaderPath) && report.textures.Count == 3, "Sealed source/topology/shader/texture admission changed.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Require(prefab != null, "Prefab missing.");
            Require(prefab.transform.localScale == Vector3.one && prefab.transform.localPosition == Vector3.zero && Quaternion.Angle(prefab.transform.localRotation, Quaternion.identity) < .001f &&
                prefab.GetComponentsInChildren<Animator>(true).Length == 0 && prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0 && prefab.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "Static canonical prefab must not contain a rig or hidden behavior.");
            Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh; Material material = prefab.GetComponent<MeshRenderer>().sharedMaterial;
            Require(mesh != null && mesh.vertexCount == 2095 && mesh.triangles.Length == 3688 * 3 && mesh.uv.Length == 2095 && mesh.tangents.Length == 2095 &&
                Vector3.Distance(mesh.bounds.min, report.canonicalMin) < .00001f && Vector3.Distance(mesh.bounds.max, report.canonicalMax) < .00001f, "Canonical source mesh changed.");
            Require(material != null && material.shader.name == "SwanUnderwater/ButterflyFishSwim" && material.GetColor("_BaseColor") == Color.white && material.GetFloat("_Cutoff") == .20f &&
                material.GetVector("_FishBody") == new Vector4(0, report.shaderBodyHalfExtents.x, report.shaderBodyHalfExtents.y, .5f) && material.renderQueue == (int)RenderQueue.AlphaTest, "Dedicated PBR/opacity/procedural-axis material changed.");
            foreach (TextureRecord record in report.textures)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(record.path);
                bool lossless = record.role == "normal-rg";
                Require(texture != null && texture.format == (lossless ? TextureFormat.RGBA32 : TextureFormat.ASTC_4x4) && texture.width == 1024 && texture.height == 1024 && texture.mipmapCount == 11 && !texture.isReadable &&
                    GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat) == record.srgb && texture.filterMode == FilterMode.Trilinear && texture.anisoLevel == 16 &&
                    record.payloadBytes == (lossless ? 5592404 : 1398128) && record.copyMaximumError <= 1 && record.meanError <= 6 && record.rmsError <= 10 && (!lossless || record.rgbMaximumError <= 1) &&
                    (record.srgb ? record.alphaMeanError <= 4 : record.alphaMaximumError <= 1) &&
                    (record.role != "normal-rg" || record.normalRmsDegrees <= 3 && record.normalP99Degrees <= 8), "Native ASTC quality/payload/color/normal contract changed.");
                string property = record.role == "base-opacity" ? "_BaseMap" : record.role == "normal-rg" ? "_BumpMap" : "_MetallicGlossMap";
                Require(material.GetTexture(property) == texture && material.GetTextureScale(property) == Vector2.one, "Exact original-UV texture binding changed.");
            }
            var geometry = JsonUtility.FromJson<OceanAuthoredFishGeometry>(AssetDatabase.LoadAssetAtPath<TextAsset>(Geometry).text);
            Require(geometry.IsValid && geometry.sourceMeshSha256 == DaeSha && Mathf.Abs(geometry.firstPoseDiagonal - report.firstPoseDiagonal) < .00001f &&
                geometry.navigationCenter.magnitude < .00001f && Vector3.Distance(geometry.navigationSize, report.navigationSize) < .00001f, "Runtime procedural envelope differs from admission.");
            Require(AssetDatabase.GetDependencies(Prefab, true).All(x => !x.StartsWith("Assets/ArtSources/", StringComparison.Ordinal)), "Original source images may not enter runtime.");
        }

        static void RenderPreview(Report report)
        {
            Scene scene = EditorSceneManager.NewPreviewScene(); GameObject fish = null; RenderTexture target = null; Texture2D pixels = null;
            try
            {
                fish = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), scene);
                var cameraObject = new GameObject("Butterfly preview camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene; camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .11f, .13f); camera.orthographic = true; camera.orthographicSize = .65f;
                camera.aspect = 16f / 9f; camera.nearClipPlane = .01f; camera.farClipPlane = 20f; var extra = camera.GetUniversalAdditionalCameraData(); extra.allowXRRendering = false; extra.renderPostProcessing = false;
                for (int i = 0; i < 2; i++)
                {
                    var lightObject = new GameObject("Butterfly preview light"); SceneManager.MoveGameObjectToScene(lightObject, scene);
                    var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = i == 0 ? 1.5f : .65f; light.shadows = LightShadows.None;
                    light.transform.rotation = Quaternion.Euler(i == 0 ? new Vector3(30, -35, 0) : new Vector3(-15, 145, 0));
                }
                target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; target.Create(); camera.targetTexture = target;
                pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false); var renderer = fish.GetComponent<Renderer>(); var block = new MaterialPropertyBlock();
                for (int side = 0; side < 2; side++) for (int phase = 0; phase < 3; phase++)
                {
                    block.SetVector("_SwimMotion", new Vector4(phase * Mathf.PI * .5f, 1f, 0, 1f)); renderer.SetPropertyBlock(block);
                    camera.transform.position = side == 0 ? new Vector3(2f, .15f, .4f) : new Vector3(-1.8f, .35f, 1.3f); camera.transform.LookAt(Vector3.zero, Vector3.up);
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
                    try { pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply(false, false); }
                    finally { RenderTexture.active = previous; }
                    string path = Path.Combine(report.directory, "butterfly-view-" + side + "-phase-" + phase + ".png"); File.WriteAllBytes(path, pixels.EncodeToPNG()); report.images.Add(path);
                }
            }
            finally { if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene); if (target != null) { target.Release(); Object.DestroyImmediate(target); } if (pixels != null) Object.DestroyImmediate(pixels); }
        }

        static void Calibrate()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/SwanUnderwater/Editor/ReefAstcReadback.shader"); Require(shader != null && shader.isSupported, "Existing exact-mip decoder unavailable.");
            decoder = new Material(shader) { hideFlags = HideFlags.HideAndDontSave }; bool chosen = false;
            foreach (bool srgb in new[] { false, true })
            {
                var test = new Texture2D(8, 8, TextureFormat.RGBA32, 4, !srgb);
                try
                {
                    var expected = new byte[4][];
                    for (int mip = 0; mip < 4; mip++)
                    {
                        int size = Math.Max(1, 8 >> mip); var data = new byte[size * size * 4];
                        for (int i = 0; i < data.Length; i++) data[i] = (byte)((i * 37 + mip * 61 + i / 4 * 13) & 255);
                        expected[mip] = data; test.SetPixelData(data, mip);
                    }
                    test.Apply(false, false);
                    for (int mip = 0; mip < 4; mip++)
                    {
                        byte[] actual = RenderDecoded(test, mip, srgb); int size = Math.Max(1, 8 >> mip);
                        if (!chosen) { flipRows = MaximumError(expected[mip], actual) > 1; chosen = true; }
                        if (flipRows) Flip(actual, size, size); Require(MaximumError(expected[mip], actual) <= 1, "Independent mip/sRGB/alpha GPU calibration failed.");
                    }
                }
                finally { Object.DestroyImmediate(test); }
            }
            calibrated = true;
        }
        static byte[] Read(Texture texture, int mip, bool srgb)
        { Require(calibrated, "Uncalibrated readback."); var result = RenderDecoded(texture, mip, srgb); if (flipRows) Flip(result, Math.Max(1, texture.width >> mip), Math.Max(1, texture.height >> mip)); return result; }
        static byte[] RenderDecoded(Texture texture, int mip, bool srgb)
        {
            var target = RenderTexture.GetTemporary(new RenderTextureDescriptor(Math.Max(1, texture.width >> mip), Math.Max(1, texture.height >> mip))
            { graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm, msaaSamples = 1, depthBufferBits = 0, useMipMap = false, autoGenerateMips = false });
            RenderTexture previous = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
            try
            {
                decoder.SetTexture("_Source", texture); decoder.SetInt("_Mip", mip); decoder.SetFloat("_EncodeSrgb", srgb ? 1 : 0); GL.sRGBWrite = false;
                Graphics.Blit(texture, target, decoder, 0); var request = AsyncGPUReadback.Request(target, 0, GraphicsFormat.R8G8B8A8_UNorm); request.WaitForCompletion();
                Require(!request.hasError, "Texture decode readback failed."); return request.GetData<byte>().ToArray();
            }
            finally { decoder.SetTexture("_Source", null); GL.sRGBWrite = previousSrgb; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }
        static int MaximumError(byte[] a, byte[] b) { Require(a.Length == b.Length, "Readback byte count changed."); int max = 0; for (int i = 0; i < a.Length; i++) max = Math.Max(max, Math.Abs(a[i] - b[i])); return max; }
        static void Flip(byte[] a, int width, int height) { int stride = width * 4; for (int y = 0; y < height / 2; y++) for (int x = 0; x < stride; x++) { int i = y * stride + x, j = (height - 1 - y) * stride + x; byte t = a[i]; a[i] = a[j]; a[j] = t; } }
        static void Release() { if (decoder != null) Object.DestroyImmediate(decoder); decoder = null; calibrated = flipRows = false; }

        static void BeginFinalization(Report report, Exception error)
        {
            if (pending != null) { pending.error = error ?? pending.error; return; }
            report.success = false; report.state = error == null ? "stabilizing" : "stabilizing_failure"; report.error = error?.ToString(); report.after = Snapshot(Owned()); Save(report);
            pending = new Pending { report = report, error = error, snapshot = report.after }; EditorApplication.update += FinalizeUpdate;
        }
        static void FinalizeUpdate()
        {
            if (pending == null) { EditorApplication.update -= FinalizeUpdate; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return; Pending work = pending;
            try
            {
                AssetDatabase.SaveAssets(); var now = Snapshot(Owned()); work.ticks++; work.stable = Equal(work.snapshot, now) ? work.stable + 1 : 0; work.snapshot = now; work.report.after = now; work.report.finalizationUpdates = work.ticks; Save(work.report);
                Require(work.ticks <= 30, "Deferred writes did not stabilize."); if (work.ticks < 3 || work.stable < 2) return;
                VerifyFiles(work.report.protectedFiles); VerifySources(); if (work.error == null) ValidateOutputs(work.report);
                work.report.success = work.error == null; work.report.state = work.error == null ? "admitted" : "failed"; work.report.error = work.error?.ToString(); Save(work.report);
                if (work.report.success) File.WriteAllText(Portable, JsonUtility.ToJson(work.report, true));
                Debug.Log(work.error == null ? "BUTTERFLY_BAKE_PASSED " + Latest : "BUTTERFLY_BAKE_FAILED " + Latest);
                pending = null; EditorApplication.update -= FinalizeUpdate; Release(); Exit(work.error == null ? 0 : 1);
            }
            catch (Exception exception) { work.report.success = false; work.report.state = "failed"; work.report.error = exception.ToString(); work.report.after = Snapshot(Owned()); Save(work.report); Debug.LogException(exception); pending = null; EditorApplication.update -= FinalizeUpdate; Release(); Exit(1); }
        }
        public static void Rollback()
        {
            try
            {
                Require(pending == null && !EditorApplication.isPlayingOrWillChangePlaymode, "Edit Mode required."); Report report = ReadLatest(); VerifyFiles(report.after);
                Require(Equal(report.after, Snapshot(Owned())), "Unknown/edited files; refusing rollback."); VerifyFiles(report.protectedFiles);
                foreach (string path in new[] { Prefab, Geometry, Baked }) if (File.Exists(path) || Directory.Exists(path)) Require(AssetDatabase.DeleteAsset(path), "Owned deletion failed: " + path);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); Require(!Owned().Any(), "Owned output remains."); report.state = "rolled_back"; report.success = false; Save(report); Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); Exit(1); if (!Application.isBatchMode) throw; }
        }
        static Report ReadLatest() { string path = File.Exists(Latest) ? Latest : Portable; Require(File.Exists(path), "No local/portable ownership receipt."); var report = JsonUtility.FromJson<Report>(File.ReadAllText(path)); Require(report != null && report.tool == "OceanButterflyFishBake" && report.uid == Uid, "Invalid ownership receipt."); return report; }
        static IEnumerable<string> Owned() { foreach (string path in Files(Baked)) yield return path; if (File.Exists(Baked + ".meta")) yield return Baked + ".meta"; foreach (string path in new[] { Prefab, Prefab + ".meta", Geometry, Geometry + ".meta" }) if (File.Exists(path)) yield return path; }
        static IEnumerable<string> Files(string path) => Directory.Exists(path) ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).Where(x => !Path.GetFileName(x).StartsWith(".", StringComparison.Ordinal)).Select(x => x.Replace('\\', '/')) : File.Exists(path) ? new[] { path, path + ".meta" }.Where(File.Exists) : Enumerable.Empty<string>();
        static List<FileRecord> Snapshot(IEnumerable<string> paths) => paths.Distinct().OrderBy(x => x, StringComparer.Ordinal).Select(x => new FileRecord { path = x, sha256 = Hash(x), bytes = new FileInfo(x).Length }).ToList();
        static void VerifyFiles(List<FileRecord> files) { foreach (FileRecord file in files) Require(File.Exists(file.path) && new FileInfo(file.path).Length == file.bytes && Hash(file.path) == file.sha256, "Hash changed: " + file.path); }
        static bool Equal(List<FileRecord> a, List<FileRecord> b) => a.Count == b.Count && a.Zip(b, (x, y) => x.path == y.path && x.sha256 == y.sha256 && x.bytes == y.bytes).All(x => x);
        static void Plan(string path, Report report) { Require(!File.Exists(path) && !Directory.Exists(path), "Refusing overwrite: " + path); report.plannedPaths.Add(path); Save(report); }
        static void Folder(string path) { if (AssetDatabase.IsValidFolder(path)) return; string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))), "Folder creation failed."); }
        static void Save(Report report) { Directory.CreateDirectory(report.directory); Directory.CreateDirectory(Output); string json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(report.directory, "report.json"), json); File.WriteAllText(Latest, json); }
        static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static uint Crc(byte[] a) { uint crc = 0xffffffff; foreach (byte b in a) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320); } return ~crc; }
        static bool Finite(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        static bool Finite(float p) => !float.IsNaN(p) && !float.IsInfinity(p);
        static string Arg(string name, string fallback) { string[] args = Environment.GetCommandLineArgs(); for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1]; return fallback; }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("[ButterflyBake] " + message); }
        static void Exit(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }
    }
}
#endif

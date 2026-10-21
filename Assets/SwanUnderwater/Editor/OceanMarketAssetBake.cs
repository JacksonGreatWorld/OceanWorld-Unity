#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>
    /// Explicit market-asset admission to the existing prefab/ASTC layout. No import,
    /// build or domain-reload hooks. Existing reef assets are never conversion inputs.
    /// Run/Validate/Rollback require a graphics-capable Editor, not -nographics.
    /// </summary>
    public static class OceanMarketAssetBake
    {
        const string SourceRoot = "Assets/ArtSources/MarketReef";
        const string BakedRoot = "Assets/ArtBaked/MarketReef";
        const string ResourceRoot = "Assets/Resources/RealisticReef";
        const string DecoderShader = "Assets/SwanUnderwater/Editor/ReefAstcReadback.shader";
        const string FanShader = "Assets/Resources/Shaders/OceanReefFan.shader";
        static readonly string[] Ids = { "mountainside", "coastal_cliff_02", "coral_platygyra", "fan_coral" };
        static readonly string[] CollisionIds = { "mountainside", "coastal_cliff_02" };
        static Material decoder;
        static bool calibrated, flipRows;
        static int calibrationError;
        static PendingFinalization pendingFinalization;
        const string UrpVersionGuid = "d0353a89b1f911e48b9e16bdc9f2e058";
        const string ShaderGraphVersionGuid = "639247ca83abc874e893eb93af2b5e44";
        static string EvidenceRoot => Path.GetFullPath(Argument("-oceanMarketBakeOutput",
            Environment.GetEnvironmentVariable("OCEAN_MARKET_BAKE_REPORT_DIR") ?? "Validation/market-reef-bake"));
        static string Latest => Path.Combine(EvidenceRoot, "latest.json");

        [Serializable] public sealed class FileState
        {
            public string path, hash, backup;
        }
        [Serializable] public sealed class TextureRecord
        {
            public string source, path, role, format, graphicsFormat, imageSha256, codec, codecPolicy;
            public int width, height, mips, copyMaximumError, maximumError, maximumAlphaError;
            public bool srgb, losslessMask;
            public long astcBytes, payloadBytes;
            public double meanError, rmsError;
            public List<MipCheck> mipChecks = new List<MipCheck>();
        }
        [Serializable] public sealed class MipCheck
        {
            public int mip, width, height, copyMaximumError, maximumRgbError, maximumAlphaError;
        }
        [Serializable] public sealed class MaterialRecord
        {
            public string sourceName, path, sourceShader, targetShader, alphaMode;
            public bool doubleSided, convertedFan;
            public float alphaCutoff;
        }
        [Serializable] public sealed class ModelRecord
        {
            public string id, source, sourceHash, prefab, prefabGuid;
            public bool collision;
            public int lod, sourceLod, renderers, materials, vertices;
            public long triangles;
            public Vector3 boundsMin, boundsMax;
        }
        [Serializable] public sealed class Journal
        {
            public int schemaVersion = 1;
            public string tool = "OceanMarketAssetBake", utc, unity, directory, state, error;
            public bool success, fanLod0Reuse;
            public int calibrationMaximumError;
            public int finalizationUpdates;
            public List<FileState> before = new List<FileState>();
            public List<FileState> after = new List<FileState>();
            public List<FileState> protectedFiles = new List<FileState>();
            public List<string> plannedPaths = new List<string>();
            public List<ModelRecord> models = new List<ModelRecord>();
            public List<TextureRecord> textures = new List<TextureRecord>();
            public List<MaterialRecord> materials = new List<MaterialRecord>();
            public List<FileState> finalizationStart = new List<FileState>();
            public List<string> finalizationMetadataChanges = new List<string>();
        }
        sealed class Input
        {
            public string id, path, hash;
            public int lod, sourceLod, maxPixels;
            public float fanRootHeight, fanHeight;
            public bool collision;
            public GameObject prefab;
            public GLTFast.Schema.Root schema;
            public string[] imageHashes;
        }
        sealed class SharedAssets
        {
            public readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            public readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
        }
        sealed class PendingFinalization
        {
            public Journal journal;
            public Exception operationError;
            public int updates, stableUpdates;
            public List<FileState> lastValidated;
        }
        sealed class MaterialYaml
        {
            public string preamble, material;
            public readonly Dictionary<string, int> versions = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        [MenuItem("Swan/Codex/Market Reef/Bake explicit new assets")]
        public static void Run() => Execute(Bake);
        [MenuItem("Swan/Codex/Market Reef/Validate current assets")]
        public static void Validate() => Execute(() =>
        {
            var journal = ReadLatest();
            Require(journal.success && journal.state == "baked", "Latest transaction is not a completed bake.");
            VerifyHashes(journal.after, "Owned output changed");
            VerifyHashes(journal.protectedFiles, "Protected old reef changed");
            ValidateOutputs(journal);
            Write(Path.Combine(EvidenceRoot, "validation-" + Stamp() + ".json"), journal);
            Debug.Log("[MarketReefBake] current outputs PASS: " + journal.models.Count + " prefabs.");
        });
        [MenuItem("Swan/Codex/Market Reef/Roll back latest owned transaction")]
        public static void Rollback() => Execute(() => RollbackCore(ReadLatest()));

        static void Execute(Action action)
        {
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Edit Mode is required.");
                Require(pendingFinalization == null, "A market transaction is still finalizing.");
                Directory.CreateDirectory(EvidenceRoot);
                action();
                if (Application.isBatchMode && pendingFinalization == null) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
            finally
            {
                if (decoder != null) Object.DestroyImmediate(decoder);
                decoder = null; calibrated = false; EditorUtility.ClearProgressBar();
            }
        }

        static void Bake()
        {
            Require(SystemInfo.supportsAsyncGPUReadback, "Graphics/GPU readback required; do not use -nographics.");
            Require(!HasArgument("-quit"), "Do not pass -quit: the bake exits only after deferred material writes stabilize.");
            var inputs = ReadInputs(); // All twelve paths and material contracts checked before writes.
            bool rebuild = HasArgument("-oceanMarketBakeRebuild");
            Journal previous = File.Exists(Latest) ? ReadLatest() : null;
            var current = OwnedFiles().ToArray();
            if (current.Length > 0)
            {
                Require(previous != null && previous.success && previous.state == "baked",
                    "Market outputs exist without a successful ownership journal. No files will be overwritten.");
                VerifyHashes(previous.after, "Owned output changed outside this transaction");
                Require(new HashSet<string>(current, StringComparer.Ordinal).SetEquals(previous.after.Select(x => x.path)),
                    "Unjournaled files exist in the market output namespace.");
                if (!rebuild)
                {
                    Require(previous.models.Count == inputs.Count && previous.models.All(m => inputs.Any(i =>
                        i.id == m.id && i.lod == m.lod && i.path == m.source && i.hash == m.sourceHash)),
                        "Inputs changed. Inspect changes and explicitly pass -oceanMarketBakeRebuild.");
                    ValidateOutputs(previous);
                    Debug.Log("[MarketReefBake] unchanged inputs/outputs verified; no rewrite.");
                    return;
                }
            }
            else if (previous != null)
                Require(previous.state == "rolled_back" || previous.success,
                    "Previous transaction is incomplete. Run Rollback before starting another bake.");

            var journal = new Journal { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                directory = Path.Combine(EvidenceRoot, "bake-" + Stamp()), state = "preparing",
                fanLod0Reuse = HasArgument("-oceanMarketFanReuseLod0") };
            Directory.CreateDirectory(Path.Combine(journal.directory, "originals"));
            foreach (string path in current)
            {
                string backup = Path.Combine(journal.directory, "originals", path);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(path, backup);
                journal.before.Add(new FileState { path = path, hash = Hash(path), backup = backup });
                Require(Hash(backup) == Hash(path), "Backup mismatch: " + path);
            }
            foreach (string path in OldReefFiles())
                journal.protectedFiles.Add(new FileState { path = path, hash = Hash(path) });
            // Persist both the old byte roster and planned transaction before first mutation.
            foreach (var input in inputs)
                journal.plannedPaths.Add(input.collision ? CollisionPrefabPath(input.id) : PrefabPath(input.id, input.lod));
            Save(journal);
            Exception operationError = null;
            try
            {
                Calibrate(); journal.calibrationMaximumError = calibrationError;
                journal.state = "applying"; Save(journal);
                if (current.Length > 0) DeleteOwned(previous);
                MakeFolder(BakedRoot); MakeFolder(ResourceRoot);
                var shared = new SharedAssets();
                for (int i = 0; i < inputs.Count; i++)
                {
                    var input = inputs[i];
                    EditorUtility.DisplayProgressBar("Market reef ASTC", input.id + " LOD" + input.lod, i / (float)inputs.Count);
                    if (input.collision) BakeCollision(input, journal);
                    else BakeModel(input, journal, shared);
                }
            }
            catch (Exception e)
            {
                // Failed bakes also own newly created native .mat subassets. Let their
                // deferred URP version writes settle before sealing a rollback roster.
                operationError = e;
            }
            BeginFinalization(journal, operationError);
        }

        static void BeginFinalization(Journal journal, Exception operationError)
        {
            Require(pendingFinalization == null, "Duplicate finalization request.");
            Require(!HasArgument("-quit"), "Do not pass -quit: the bake exits only after deferred material writes stabilize.");
            journal.success = false;
            journal.state = operationError == null ? "stabilizing_success" : "stabilizing_failure";
            journal.error = operationError?.ToString();
            journal.after = Snapshot(OwnedFiles()); // Provisional crash-recovery roster, never success.
            journal.finalizationStart = new List<FileState>();
            foreach (var file in journal.after)
            {
                var baseline = new FileState { path = file.path, hash = file.hash };
                if (file.path.EndsWith(".mat", StringComparison.Ordinal) && journal.materials.Any(m => m.path == file.path))
                {
                    baseline.backup = Path.Combine(journal.directory, "finalization-material-baseline", file.path);
                    Directory.CreateDirectory(Path.GetDirectoryName(baseline.backup)); File.Copy(file.path, baseline.backup);
                    Require(Hash(baseline.backup) == baseline.hash, "Material finalization baseline changed while copying.");
                }
                journal.finalizationStart.Add(baseline);
            }
            Save(journal);
            pendingFinalization = new PendingFinalization { journal = journal, operationError = operationError,
                lastValidated = journal.after };
            EditorApplication.update += FinalizationUpdate;
            Debug.Log("[MarketReefBake] Waiting for at least three Editor updates and two matching flushed snapshots; pending success=" +
                (operationError == null) + ". Only known URP/ShaderGraph version subassets may change during this interval.");
        }

        static void FinalizationUpdate()
        {
            var pending = pendingFinalization;
            if (pending == null) { EditorApplication.update -= FinalizationUpdate; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                pending.updates++;
                GuardFinalizationChanges(pending.journal);
                // SaveAssets also flushes dirty hidden AssetVersion subassets; saving
                // only the main Material object does not provide that guarantee.
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.SaveAssets();
                GuardFinalizationChanges(pending.journal);
                VerifyHashes(pending.journal.protectedFiles, "Protected old reef changed during deferred finalization");
                var current = Snapshot(OwnedFiles());
                pending.stableUpdates = SameRoster(current, pending.lastValidated) ? pending.stableUpdates + 1 : 1;
                pending.lastValidated = current;
                pending.journal.after = current; // Only the explicitly guarded roster is accepted.
                pending.journal.finalizationUpdates = pending.updates;
                Save(pending.journal);
                if (pending.updates < 3 || pending.stableUpdates < 2)
                {
                    Require(pending.updates < 8, "Owned output did not stabilize within eight Editor updates.");
                    return;
                }
                bool versionsPersisted = true;
                foreach (var material in pending.journal.materials.Where(IsNativeVersionedMaterial))
                {
                    var yaml = ReadMaterialYaml(material.path);
                    versionsPersisted &= yaml.versions.TryGetValue(UrpVersionGuid, out int urp) && urp == 10 &&
                        yaml.versions.TryGetValue(ShaderGraphVersionGuid, out int graph) && graph == 0;
                }
                if (!versionsPersisted)
                {
                    Require(pending.updates < 8, "Native material version subassets have not both persisted after eight Editor updates.");
                    return;
                }
                if (pending.operationError == null) ValidateOutputs(pending.journal);
                VerifyHashes(pending.journal.after, "Output changed after final validation");
                pending.journal.success = pending.operationError == null;
                pending.journal.state = pending.operationError == null ? "baked" : "failed";
                Save(pending.journal);
                EditorApplication.update -= FinalizationUpdate; pendingFinalization = null;
                if (pending.operationError == null)
                    Debug.Log("[MarketReefBake] PASS " + pending.journal.models.Count + " prefabs / " + pending.journal.textures.Count +
                        " unique textures after " + pending.updates + " Editor updates; ASTC bytes=" + pending.journal.textures.Sum(t => t.astcBytes) +
                        "; lossless MASK RGBA32 bytes=" + pending.journal.textures.Where(t => t.losslessMask).Sum(t => t.payloadBytes) +
                        "; journal=" + Path.Combine(pending.journal.directory, "report.json") + ". Device/rendering validation remains separate.");
                else
                {
                    Debug.LogException(pending.operationError);
                    Debug.LogError("[MarketReefBake] Failed transaction retained with stabilized output hashes. Explicit Rollback is required.");
                }
                if (Application.isBatchMode) EditorApplication.Exit(pending.operationError == null ? 0 : 1);
            }
            catch (Exception error)
            {
                EditorApplication.update -= FinalizationUpdate; pendingFinalization = null;
                pending.journal.success = false; pending.journal.state = "finalization_failed";
                pending.journal.error = (pending.operationError == null ? "" : pending.operationError + "\n") + "Finalization: " + error;
                // Never replace after hashes with unreviewed current bytes after a guard
                // failure. Rollback must refuse externally edited output until reviewed.
                pending.journal.after = pending.lastValidated; Save(pending.journal);
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static bool SameRoster(List<FileState> a, List<FileState> b) => a.Count == b.Count &&
            a.Zip(b, (x, y) => x.path == y.path && x.hash == y.hash).All(x => x);
        static bool IsNativeVersionedMaterial(MaterialRecord record) => !record.convertedFan &&
            record.sourceShader != null && record.sourceShader.Contains("glTF-pbrMetallicRoughness");
        static void GuardFinalizationChanges(Journal journal)
        {
            var current = OwnedFiles().ToArray();
            Require(new HashSet<string>(current, StringComparer.Ordinal).SetEquals(journal.finalizationStart.Select(x => x.path)),
                "Owned files were added/removed while finalizing; refusing to accept external changes.");
            foreach (var file in journal.finalizationStart)
            {
                if (Hash(file.path) == file.hash) continue;
                var material = journal.materials.FirstOrDefault(m => m.path == file.path);
                Require(material != null && IsNativeVersionedMaterial(material) && !string.IsNullOrEmpty(file.backup),
                    "Non-metadata asset changed while finalizing: " + file.path);
                Require(Hash(file.backup) == file.hash, "Material baseline backup changed: " + file.path);
                var before = ReadMaterialYaml(file.backup); var after = ReadMaterialYaml(file.path);
                Require(before.preamble == after.preamble && before.material == after.material,
                    "Material contents changed beyond known version metadata: " + file.path);
                foreach (var version in before.versions)
                    Require(after.versions.TryGetValue(version.Key, out int currentVersion) && currentVersion >= version.Value,
                        "Material version metadata was removed or regressed: " + file.path);
                if (!journal.finalizationMetadataChanges.Contains(file.path)) journal.finalizationMetadataChanges.Add(file.path);
            }
        }

        static MaterialYaml ReadMaterialYaml(string path)
        {
            string text = File.ReadAllText(path);
            var headers = Regex.Matches(text, @"(?m)^--- !u!(\d+) &-?\d+[^\r\n]*\r?$");
            Require(text.StartsWith("%YAML", StringComparison.Ordinal) && headers.Count > 0,
                "Text serialization required to review native material version metadata: " + path);
            var result = new MaterialYaml { preamble = text.Substring(0, headers[0].Index) };
            var allowedKeys = new HashSet<string>(new[] { "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance",
                "m_PrefabAsset", "m_GameObject", "m_Enabled", "m_EditorHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier", "version" });
            for (int i = 0; i < headers.Count; i++)
            {
                int end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
                string block = text.Substring(headers[i].Index, end - headers[i].Index);
                if (headers[i].Groups[1].Value == "21")
                {
                    Require(result.material == null, "More than one main Material document: " + path);
                    result.material = block; continue;
                }
                Require(headers[i].Groups[1].Value == "114", "Unexpected material subasset class: " + path);
                var script = Regex.Match(block, @"(?m)^\s+m_Script: \{fileID: 11500000, guid: ([a-f0-9]{32}), type: 3\}\r?$");
                var version = Regex.Match(block, @"(?m)^\s+version: (\d+)\r?$");
                Require(script.Success && version.Success && (script.Groups[1].Value == UrpVersionGuid ||
                    script.Groups[1].Value == ShaderGraphVersionGuid), "Unknown MonoBehaviour material metadata: " + path);
                string guid = script.Groups[1].Value; int number = int.Parse(version.Groups[1].Value);
                Require(number >= 0 && number <= (guid == UrpVersionGuid ? 10 : 0) && !result.versions.ContainsKey(guid),
                    "Unexpected or duplicate material version metadata: " + path);
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string line in block.Split('\n').Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.Trim() == "MonoBehaviour:") continue;
                    int colon = line.IndexOf(':');
                    string key = colon > 0 ? line.Substring(0, colon).Trim() : "";
                    Require(colon > 0 && allowedKeys.Contains(key) && !fields.ContainsKey(key),
                        "Unexpected field inside version-only metadata: " + path);
                    fields.Add(key, line.Substring(colon + 1).Trim());
                }
                foreach (string key in new[] { "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject" })
                    Require(fields.TryGetValue(key, out string reference) && reference == "{fileID: 0}",
                        "Unexpected object reference inside version metadata: " + path);
                Require(fields.TryGetValue("m_ObjectHideFlags", out string hideFlags) && hideFlags == "11" &&
                    fields.TryGetValue("m_Enabled", out string enabled) && enabled == "1" &&
                    fields.TryGetValue("m_EditorHideFlags", out string editorFlags) && editorFlags == "0" &&
                    fields.TryGetValue("m_Name", out string name) && name.Length == 0 &&
                    fields.TryGetValue("m_EditorClassIdentifier", out string identifier) && identifier.Length == 0,
                    "Unexpected state inside version-only metadata: " + path);
                result.versions.Add(guid, number);
            }
            Require(result.material != null, "No main Material document: " + path); return result;
        }

        static List<Input> ReadInputs()
        {
            var result = new List<Input>();
            bool reuseFan = HasArgument("-oceanMarketFanReuseLod0");
            foreach (string id in Ids)
            for (int lod = 0; lod < 3; lod++)
            {
                int sourceLod = lod;
                string path = SourceRoot + "/" + id + "_lod" + sourceLod + ".glb";
                if (!File.Exists(path) && id == "fan_coral" && lod > 0 && reuseFan)
                { sourceLod = 0; path = SourceRoot + "/" + id + "_lod0.glb"; }
                Require(File.Exists(path), "Missing required input " + path +
                    ". Only fan_coral may explicitly reuse LOD0 with -oceanMarketFanReuseLod0.");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Require(prefab != null, "Unity could not import " + path);
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                Require(renderers.Length > 0 && renderers.All(r => r is MeshRenderer), "Static MeshRenderer assets required: " + path);
                Require(prefab.GetComponentsInChildren<Collider>(true).Length == 0, "Author physics separately from visual imports: " + path);
                foreach (Transform transform in prefab.GetComponentsInChildren<Transform>(true))
                {
                    Matrix4x4 matrix = prefab.transform.worldToLocalMatrix * transform.localToWorldMatrix;
                    Require(matrix.determinant > 0f, "Unbaked reflected/degenerate node transform: " + path + ":" + transform.name);
                }
                foreach (Renderer renderer in renderers)
                {
                    Require(renderer.sharedMaterials.Length > 0 && renderer.sharedMaterials.All(m => m != null && m.shader != null),
                        "Missing imported material/shader: " + path);
                    Require(renderer.GetComponent<MeshFilter>()?.sharedMesh != null, "Renderer has no mesh: " + path);
                    Require(renderer.GetComponent<MeshFilter>().sharedMesh.isReadable,
                        "OceanScannedReefMass.ReadSurface requires these bounded new meshes to remain CPU-readable: " + path);
                    if (id == "fan_coral")
                    {
                        Matrix4x4 matrix = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                        for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
                            Require(Mathf.Abs(matrix[row, column] - (row == column ? 1f : 0f)) < .00001f,
                                "Bake fan child transforms into the normalized mesh before height-based water motion: " + path);
                    }
                }
                var input = new Input { id = id, path = path, hash = Hash(path), lod = lod, sourceLod = sourceLod,
                    maxPixels = id == "fan_coral" ? 2048 : 4096, prefab = prefab, schema = ReadSchema(path) };
                input.imageHashes = ReadImageHashes(path, input.schema);
                Require(input.schema.Materials != null && input.schema.Materials.Count > 0, "No glTF material metadata: " + path);
                if (id == "fan_coral")
                    Require(AssetDatabase.LoadAssetAtPath<Shader>(FanShader) != null, "Missing fan shader: " + FanShader);
                result.Add(input);
            }
            var fanReference = new ModelRecord();
            DescribeGeometry(result.First(x => x.id == "fan_coral" && x.lod == 0).prefab, fanReference);
            float fanHeight = fanReference.boundsMax.y - fanReference.boundsMin.y;
            Require(fanHeight > .0001f, "Fan has no measured root-to-tip height.");
            foreach (var input in result.Where(x => x.id == "fan_coral"))
            { input.fanRootHeight = fanReference.boundsMin.y; input.fanHeight = fanHeight; }
            foreach (string id in CollisionIds)
            {
                string path = SourceRoot + "/" + id + "_collision.glb";
                Require(File.Exists(path), "Missing explicit independent collision source: " + path);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Require(prefab != null, "Cannot import independent collision source " + path);
                ValidateHull(prefab, path);
                result.Add(new Input { id = id, path = path, hash = Hash(path), lod = -1, sourceLod = -1,
                    prefab = prefab, collision = true });
            }
            return result;
        }

        static void BakeCollision(Input input, Journal journal)
        {
            GameObject instance = Object.Instantiate(input.prefab);
            try
            {
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "market_" + input.id + "_collision";
                // This is a separately authored approximation used only for physics.
                // The visual scan remains open; no closure is claimed for its render mesh.
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(renderer);
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                var record = new ModelRecord { id = input.id, source = input.path, sourceHash = input.hash,
                    lod = -1, sourceLod = -1, collision = true, prefab = CollisionPrefabPath(input.id) };
                DescribeGeometry(instance, record); ValidateHull(instance, record.prefab);
                Plan(record.prefab, journal);
                PrefabUtility.SaveAsPrefabAsset(instance, record.prefab, out bool saved);
                Require(saved, "Cannot save independent collision prefab: " + record.prefab);
                record.prefabGuid = AssetDatabase.AssetPathToGUID(record.prefab);
                journal.models.Add(record); Save(journal); ValidateModel(record);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        static void ValidateHull(GameObject root, string label)
        {
            var points = new List<Vector3>(); var faces = new List<int>();
            var weld = new Dictionary<Vector3, int>();
            Matrix4x4 rootInverse = root.transform.worldToLocalMatrix;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                Require(mesh != null && mesh.isReadable, "Independent collision mesh must be CPU-readable: " + label);
                Matrix4x4 matrix = rootInverse * filter.transform.localToWorldMatrix;
                Require(matrix.determinant > 0, "Collision transform must be baked without reflection: " + label);
                Vector3[] vertices = mesh.vertices; var indices = new int[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(vertices[i]);
                    Require(Finite(point.x) && Finite(point.y) && Finite(point.z), "Non-finite collision point: " + label);
                    if (!weld.TryGetValue(point, out int id)) { id = points.Count; points.Add(point); weld.Add(point, id); }
                    indices[i] = id;
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Require(mesh.GetTopology(sub) == MeshTopology.Triangles, "Collision uses triangle indices: " + label);
                    foreach (int index in mesh.GetIndices(sub, true)) faces.Add(indices[index]);
                }
            }
            Require(points.Count >= 4 && faces.Count >= 12 && faces.Count % 3 == 0 && faces.Count / 3 <= 240,
                "Independent hull must contain 4..240 triangles: " + label);
            Bounds bounds = new Bounds(points[0], Vector3.zero); foreach (var point in points) bounds.Encapsulate(point);
            float tolerance = Mathf.Max(.00001f, bounds.size.magnitude * .000001f);
            var edgeCounts = new Dictionary<ulong, int>(); var edgeWinding = new Dictionary<ulong, int>();
            double volume = 0;
            for (int t = 0; t < faces.Count; t += 3)
            {
                int a = faces[t], b = faces[t + 1], c = faces[t + 2];
                Require(a != b && b != c && c != a, "Degenerate collision triangle: " + label);
                Vector3 normal = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                Require(normal.sqrMagnitude > 1e-16f, "Zero-area collision face: " + label);
                normal.Normalize();
                foreach (var point in points)
                    Require(Vector3.Dot(normal, point - points[a]) <= tolerance,
                        "Collision source is not an outward convex hull: " + label);
                Vector3 cross = Vector3.Cross(points[b], points[c]);
                volume += ((double)points[a].x * cross.x + (double)points[a].y * cross.y + (double)points[a].z * cross.z) / 6.0;
                HullEdge(a, b, edgeCounts, edgeWinding); HullEdge(b, c, edgeCounts, edgeWinding); HullEdge(c, a, edgeCounts, edgeWinding);
            }
            Require(edgeCounts.All(e => e.Value == 2 && edgeWinding[e.Key] == 0) && volume > 1e-8,
                "Independent collision hull must be watertight, consistently wound and positive-volume: " + label);
        }
        static void HullEdge(int a, int b, Dictionary<ulong, int> counts, Dictionary<ulong, int> winding)
        {
            ulong key = ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
            counts.TryGetValue(key, out int count); counts[key] = count + 1;
            winding.TryGetValue(key, out int sign); winding[key] = sign + (a < b ? 1 : -1);
        }

        static void BakeModel(Input input, Journal journal, SharedAssets shared)
        {
            string folder = BakedRoot + "/" + input.id;
            MakeFolder(folder);
            GameObject instance = Object.Instantiate(input.prefab);
            try
            {
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "market_" + input.id + "_lod" + input.lod;
                var materialCache = new Dictionary<Material, Material>();
                var replacements = new List<KeyValuePair<Material, Material>>();
                var record = new ModelRecord { id = input.id, source = input.path, sourceHash = input.hash,
                    lod = input.lod, sourceLod = input.sourceLod, prefab = PrefabPath(input.id, input.lod) };
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int m = 0; m < materials.Length; m++)
                    {
                        Material source = materials[m];
                        if (!materialCache.TryGetValue(source, out Material target))
                        {
                            var metadata = FindMaterial(input, source);
                            // Normalise the cached alpha enum before serialising its public definition.
                            // glTFast GetAlphaMode clears its source string on the first call.
                            string alphaMode = metadata.GetAlphaMode().ToString();
                            string materialKey = input.id + ":" + alphaMode + ":" + JsonUtility.ToJson(metadata) +
                                ":" + source.shader.name + ":" + string.Join(":", input.imageHashes);
                            if (shared.materials.TryGetValue(materialKey, out target))
                            {
                                materialCache.Add(source, target); materials[m] = target; continue;
                            }
                            string materialPath = folder + "/material_" + shared.materials.Count + ".mat";
                            bool fan = input.id == "fan_coral";
                            target = fan ? MakeFan(source, metadata, input) : new Material(source);
                            target.name = source.name;
                            foreach (string property in target.GetTexturePropertyNames())
                            {
                                Texture original = ImportedTexture(target, property);
                                if (original == null)
                                {
                                    // A shader's built-in white/bump is a fallback, not a
                                    // downloaded texture. Null retains that shader fallback
                                    // without manufacturing an ASTC image or fake provenance.
                                    target.SetTexture(property, null); continue;
                                }
                                Require(original is Texture2D, "Non-2D imported texture: " + source.name + ":" + property);
                                var texture = (Texture2D)original;
                                string imageHash = ImageHash(input, metadata, property);
                                bool losslessMask = input.id == "fan_coral" && property == "_BaseMap" &&
                                    metadata.GetAlphaMode() == GLTFast.Schema.MaterialBase.AlphaMode.Mask;
                                string key = input.id + ":" + imageHash + ":" + TextureRole(property) + ":" +
                                    GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat) + ":" + texture.width + ":" + texture.height +
                                    ":" + texture.mipmapCount + ":" + texture.wrapModeU + ":" + texture.wrapModeV + ":" + texture.wrapModeW +
                                    ":" + (losslessMask ? "RGBA32-MASK" : "ASTC4x4");
                                if (!shared.textures.TryGetValue(key, out Texture2D compressed))
                                {
                                    string texturePath = folder + "/texture_" + shared.textures.Count + ".asset";
                                    compressed = Compress(texture, texturePath, property, imageHash, input.maxPixels, losslessMask, journal);
                                    shared.textures.Add(key, compressed);
                                }
                                target.SetTexture(property, compressed);
                            }
                            Plan(materialPath, journal); AssetDatabase.CreateAsset(target, materialPath);
                            shared.materials.Add(materialKey, target);
                            materialCache.Add(source, target); replacements.Add(new KeyValuePair<Material, Material>(source, target));
                            journal.materials.Add(new MaterialRecord { sourceName = source.name, path = materialPath,
                                sourceShader = source.shader.name, targetShader = target.shader.name,
                                alphaMode = alphaMode, alphaCutoff = metadata.alphaCutoff,
                                doubleSided = metadata.doubleSided, convertedFan = fan });
                            Save(journal);
                        }
                        materials[m] = target;
                    }
                    renderer.sharedMaterials = materials; record.renderers++;
                }
                // Creating .mat can trigger URP migrations. Restore intended scalar/keyword state
                // afterwards while retaining only the deliberate compressed texture substitution.
                foreach (var pair in replacements)
                {
                    Material expected = input.id == "fan_coral" ? MakeFan(pair.Key, FindMaterial(input, pair.Key), input) : new Material(pair.Key);
                    try { RestoreState(expected, pair.Value); }
                    finally { Object.DestroyImmediate(expected); }
                }
                record.materials = materialCache.Count;
                DescribeGeometry(instance, record);
                Plan(record.prefab, journal);
                PrefabUtility.SaveAsPrefabAsset(instance, record.prefab, out bool saved);
                Require(saved, "Failed creating prefab " + record.prefab);
                record.prefabGuid = AssetDatabase.AssetPathToGUID(record.prefab);
                journal.models.Add(record); Save(journal);
                ValidateModel(record);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        static GLTFast.Schema.MaterialBase FindMaterial(Input input, Material material)
        {
            var materials = input.schema.Materials;
            if (materials.Count == 1) return materials[0];
            string name = material.name;
            var matches = materials.Where(m => m.name == name).ToArray();
            Require(matches.Length == 1, "Cannot uniquely map imported material to glTF metadata: " + input.path + ":" + name);
            return matches[0];
        }

        static Material MakeFan(Material source, GLTFast.Schema.MaterialBase metadata, Input input)
        {
            Require(metadata.GetAlphaMode() != GLTFast.Schema.MaterialBase.AlphaMode.Blend,
                "Fan BLEND requires an explicit transparency decision; refusing silent conversion to MASK.");
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(FanShader);
            Require(shader != null && shader.isSupported, "Unsupported fan shader.");
            var material = new Material(shader) { name = source.name, enableInstancing = source.enableInstancing,
                doubleSidedGI = metadata.doubleSided };
            foreach (string property in new[] { "_BaseMap", "_BumpMap", "_MaskMap", "_EmissionMap", "_OcclusionMap" })
                if (material.HasProperty(property)) material.SetTexture(property, null);
            CopyTexture(source, material, "baseColorTexture", "_BaseMap", TextureInfo(metadata, "_BaseMap"));
            CopyTexture(source, material, "normalTexture", "_BumpMap", TextureInfo(metadata, "_BumpMap"));
            CopyTexture(source, material, "metallicRoughnessTexture", "_MaskMap", TextureInfo(metadata, "_MaskMap"));
            CopyTexture(source, material, "emissiveTexture", "_EmissionMap", TextureInfo(metadata, "_EmissionMap"));
            Texture occlusion = ImportedTexture(source, "occlusionTexture");
            Texture packed = ImportedTexture(source, "metallicRoughnessTexture");
            if (occlusion != null && occlusion != packed)
            {
                CopyTexture(source, material, "occlusionTexture", "_OcclusionMap", TextureInfo(metadata, "_OcclusionMap"));
                SetFloat(material, "_UseOcclusionMap", 1f);
            }
            else if (material.HasProperty("_UseOcclusionMap")) material.SetFloat("_UseOcclusionMap", 0f);
            // Imported ColorShaderProperty values already have Unity's sRGB authoring encoding.
            // Copy them exactly rather than decoding or tinting them for a second time.
            SetColor(material, "_BaseColor", source.HasProperty("baseColorFactor") ? source.GetColor("baseColorFactor") : Color.white);
            SetColor(material, "_EmissionColor", source.HasProperty("emissiveFactor") ? source.GetColor("emissiveFactor") : Color.black);
            SetFloat(material, "_BumpScale", source.HasProperty("normalTexture_scale") ? source.GetFloat("normalTexture_scale") : 1f);
            SetFloat(material, "_NormalEncoding", 0f); // The GLB payload was copied as raw RGB, never Unity normal-map swizzled.
            SetFloat(material, "_SwayRootHeight", input.fanRootHeight);
            SetFloat(material, "_SwayHeight", input.fanHeight);
            SetFloat(material, "_SwayUseUV2", 0f); // Arbitrary downloaded UV1 is not an authored motion contract.
            SetFloat(material, "_Metallic", source.HasProperty("metallicFactor") ? source.GetFloat("metallicFactor") : 1f);
            SetFloat(material, "_Roughness", source.HasProperty("roughnessFactor") ? source.GetFloat("roughnessFactor") : 1f);
            SetFloat(material, "_OcclusionStrength", source.HasProperty("occlusionTexture_strength") ? source.GetFloat("occlusionTexture_strength") : 1f);
            bool mask = metadata.GetAlphaMode() == GLTFast.Schema.MaterialBase.AlphaMode.Mask;
            SetFloat(material, "_Cutoff", metadata.alphaCutoff); SetFloat(material, "_AlphaClip", mask ? 1f : 0f);
            SetFloat(material, "_Cull", metadata.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            if (mask) material.EnableKeyword("_ALPHATEST_ON"); else material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)(mask ? RenderQueue.AlphaTest : RenderQueue.Geometry);
            material.SetOverrideTag("RenderType", mask ? "TransparentCutout" : "Opaque");
            return material;
        }
        static void CopyTexture(Material source, Material target, string from, string to, GLTFast.Schema.TextureInfoBase info)
        {
            Texture texture = ImportedTexture(source, from);
            if (texture == null) return;
            Require(target.HasProperty(to), "Fan shader is missing texture property " + to);
            target.SetTexture(to, texture);
            target.SetTextureOffset(to, source.GetTextureOffset(from));
            target.SetTextureScale(to, source.GetTextureScale(from));
            Require(info != null && info.index >= 0, "Bound fan texture has no source metadata: " + from);
            // The installed glTFast 6.13.1 stores only scaled off-diagonal sine
            // terms in its Vector _Rotation property (identity is zero), not a
            // float angle nor a generic 2x2 matrix. Use the source radians/UV
            // channel instead and retain the importer's already-correct ST.
            var transform = info.Extensions?.KHR_texture_transform;
            float radians = transform != null ? transform.rotation : 0f;
            Require(Finite(radians) && Mathf.Abs(Mathf.DeltaAngle(0f, radians * Mathf.Rad2Deg)) < .0001f,
                "Fan KHR_texture_transform rotation must be baked before shader conversion: " + from);
            int channel = transform != null && transform.texCoord >= 0 ? transform.texCoord : info.texCoord;
            Require(channel == 0, "Fan secondary UV set must be baked before shader conversion: " + from);
        }
        static Texture ImportedTexture(Material material, string property)
        {
            if (!material.HasProperty(property)) return null;
            Texture texture = material.GetTexture(property);
            if (texture == null) return null;
            string path = AssetDatabase.GetAssetPath(texture);
            if (path == "Resources/unity_builtin_extra" || path == "Library/unity default resources" ||
                texture == Texture2D.whiteTexture || texture == Texture2D.blackTexture || texture == Texture2D.grayTexture ||
                texture == Texture2D.normalTexture)
                return null;
            Require(!string.IsNullOrEmpty(path), "Texture has no persistent imported image provenance: " + material.name + ":" + property);
            return texture;
        }
        static void SetFloat(Material material, string property, float value)
        { Require(material.HasProperty(property), "Fan shader missing " + property); material.SetFloat(property, value); }
        static void SetColor(Material material, string property, Color value)
        { Require(material.HasProperty(property), "Fan shader missing " + property); material.SetColor(property, value); }

        static float ExpectedScalar(Material material, string property)
        {
            float value = material.GetFloat(property);
            if (property == "_QueueControl" && value < 0 &&
                material.shader.name.Contains("glTF-pbrMetallicRoughness"))
            {
                Require(material.rawRenderQueue >= 0, "Unreviewed inherited glTF queue initialization.");
                return 1f; // URP BaseShaderGUI.QueueControl.UserOverride, explicit raw queue.
            }
            return value;
        }

        static void RestoreState(Material expected, Material target)
        {
            var textures = target.GetTexturePropertyNames().ToDictionary(p => p, p => target.GetTexture(p));
            target.CopyPropertiesFromMaterial(expected);
            // URP 17 initializes ShaderGraph _QueueControl on its deferred importer.
            // Canonicalize the observed glTF explicit-queue case before sealing the
            // baseline, rather than allowing material-content changes in the guard.
            if (target.HasProperty("_QueueControl"))
                target.SetFloat("_QueueControl", ExpectedScalar(expected, "_QueueControl"));
            foreach (var pair in textures) target.SetTexture(pair.Key, pair.Value);
            target.shaderKeywords = expected.shaderKeywords;
            target.renderQueue = expected.rawRenderQueue; target.enableInstancing = expected.enableInstancing;
            target.doubleSidedGI = expected.doubleSidedGI; target.globalIlluminationFlags = expected.globalIlluminationFlags;
            target.SetOverrideTag("RenderType", expected.GetTag("RenderType", false, ""));
            EditorUtility.SetDirty(target); AssetDatabase.SaveAssetIfDirty(target);
            Require(target.shader == expected.shader && target.renderQueue == expected.renderQueue &&
                target.doubleSidedGI == expected.doubleSidedGI, "Material state changed after asset import.");
            for (int i = 0; i < expected.shader.GetPropertyCount(); i++)
            {
                string property = expected.shader.GetPropertyName(i);
                var type = expected.shader.GetPropertyType(i);
                if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range)
                    Require(ExpectedScalar(expected, property).Equals(target.GetFloat(property)), "Scalar migration changed " + property);
                else if (type == ShaderPropertyType.Color)
                    Require(expected.GetColor(property).Equals(target.GetColor(property)), "Color migration changed " + property);
            }
        }

        static Texture2D Compress(Texture2D source, string path, string role, string imageHash, int maxPixels, bool losslessMask, Journal journal)
        {
            Require(source.width > 0 && source.height > 0 && Math.Max(source.width, source.height) <= maxPixels,
                "Texture exceeds explicit runtime cap " + maxPixels + ": " + source.name + "; resize in the derived GLB pipeline first.");
            int fullMips = 1 + (int)Math.Floor(Math.Log(Math.Max(source.width, source.height), 2));
            Require(source.mipmapCount == fullMips, "Complete source mip chain required: " + source.name);
            bool srgb = GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
            Require(!losslessMask || (role == "_BaseMap" && maxPixels <= 2048),
                "RGBA32 exception is restricted to the explicitly admitted fan MASK base colour at <=2K.");
            var record = new TextureRecord { source = AssetDatabase.GetAssetPath(source) + ":" + source.name,
                path = path, role = role, imageSha256 = imageHash, width = source.width, height = source.height, mips = source.mipmapCount, srgb = srgb,
                losslessMask = losslessMask, codec = losslessMask ? "RGBA32" : "ASTC_4x4",
                codecPolicy = losslessMask ? "Explicit fan MASK base-colour exception: preserve RGB and thin alpha exactly; <=2048px, all mips, <=22369620 bytes per 2K texture"
                    : "ASTC 4x4 original strict RGB/alpha quality thresholds; no relaxation" };
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, source.mipmapCount, !srgb)
            { name = source.name, filterMode = FilterMode.Trilinear, anisoLevel = 16,
                wrapModeU = source.wrapModeU, wrapModeV = source.wrapModeV, wrapModeW = source.wrapModeW, mipMapBias = source.mipMapBias };
            try
            {
                long expectedBytes = 0, expectedRgbaBytes = 0;
                var originals = new byte[source.mipmapCount][];
                for (int mip = 0; mip < source.mipmapCount; mip++)
                {
                    originals[mip] = ReadPixels(source, mip, srgb);
                    int w = Math.Max(1, source.width >> mip), h = Math.Max(1, source.height >> mip);
                    Require(originals[mip].Length == w * h * 4, "Decoded dimensions mismatch.");
                    copy.SetPixelData(originals[mip], mip);
                    expectedBytes += (long)((w + 3) / 4) * ((h + 3) / 4) * 16;
                    expectedRgbaBytes += (long)w * h * 4;
                }
                copy.Apply(false, false);
                for (int mip = 0; mip < source.mipmapCount; mip++)
                {
                    int error = MaxError(originals[mip], ReadPixels(copy, mip, srgb));
                    record.copyMaximumError = Math.Max(record.copyMaximumError, error);
                    record.mipChecks.Add(new MipCheck { mip = mip, width = Math.Max(1, source.width >> mip),
                        height = Math.Max(1, source.height >> mip), copyMaximumError = error });
                }
                Require(record.copyMaximumError <= 1, "RGBA copy changed source color encoding.");
                if (!losslessMask) EditorUtility.CompressTexture(copy, TextureFormat.ASTC_4x4, TextureCompressionQuality.Best);
                copy.Apply(false, false);
                record.payloadBytes = copy.GetRawTextureData<byte>().Length;
                record.astcBytes = losslessMask ? 0 : record.payloadBytes;
                TextureFormat expectedFormat = losslessMask ? TextureFormat.RGBA32 : TextureFormat.ASTC_4x4;
                Require(copy.format == expectedFormat && copy.mipmapCount == record.mips &&
                    GraphicsFormatUtility.IsSRGBFormat(copy.graphicsFormat) == srgb &&
                    record.payloadBytes == (losslessMask ? expectedRgbaBytes : expectedBytes),
                    "Texture codec, payload size, mips or color space changed.");
                double absolute = 0, squared = 0; long count = 0;
                for (int mip = 0; mip < source.mipmapCount; mip++)
                {
                    byte[] decoded = ReadPixels(copy, mip, srgb), original = originals[mip];
                    Require(decoded.Length == original.Length, "ASTC readback length mismatch.");
                    for (int i = 0; i < decoded.Length; i++)
                    {
                        int error = Math.Abs(decoded[i] - original[i]); absolute += error; squared += error * error; count++;
                        record.maximumError = Math.Max(record.maximumError, error);
                        if ((i & 3) == 3)
                        {
                            record.maximumAlphaError = Math.Max(record.maximumAlphaError, error);
                            record.mipChecks[mip].maximumAlphaError = Math.Max(record.mipChecks[mip].maximumAlphaError, error);
                        }
                        else record.mipChecks[mip].maximumRgbError = Math.Max(record.mipChecks[mip].maximumRgbError, error);
                    }
                }
                record.meanError = absolute / count; record.rmsError = Math.Sqrt(squared / count);
                if (losslessMask)
                    Require(record.maximumError <= 1 && record.maximumAlphaError <= 1 &&
                        record.mipChecks.All(m => m.copyMaximumError <= 1 && m.maximumRgbError <= 1 && m.maximumAlphaError <= 1),
                        "Lossless fan MASK per-mip RGB/alpha copy changed by more than one byte: " + source.name);
                else
                    Require(record.meanError <= 8 && record.rmsError <= 12 && record.maximumAlphaError <= 16,
                        "ASTC quality gate failed: " + source.name + " / " + role);
                copy.Apply(false, true); record.format = copy.format.ToString(); record.graphicsFormat = copy.graphicsFormat.ToString();
                Plan(path, journal); AssetDatabase.CreateAsset(copy, path); journal.textures.Add(record); Save(journal);
                ValidateTexture(record); return copy;
            }
            catch { if (!AssetDatabase.Contains(copy)) Object.DestroyImmediate(copy); throw; }
        }

        static void ValidateOutputs(Journal journal)
        {
            Require(journal.models.Count == Ids.Length * 3 + CollisionIds.Length,
                "The explicit twelve-visual plus two-collision roster is incomplete.");
            foreach (string id in Ids) for (int lod = 0; lod < 3; lod++)
                Require(journal.models.Count(m => !m.collision && m.id == id && m.lod == lod) == 1, "Missing/duplicate visual LOD: " + id + "/" + lod);
            foreach (string id in CollisionIds)
                Require(journal.models.Count(m => m.collision && m.id == id) == 1, "Missing/duplicate independent hull: " + id);
            foreach (var model in journal.models)
            {
                Require(Hash(model.source) == model.sourceHash, "Derived source changed: " + model.source);
                ValidateModel(model);
            }
            foreach (var texture in journal.textures) ValidateTexture(texture);
            foreach (var material in journal.materials)
            {
                var actual = AssetDatabase.LoadAssetAtPath<Material>(material.path);
                Require(actual != null && actual.shader != null && actual.shader.name == material.targetShader,
                    "Missing or changed saved material " + material.path);
                if (material.convertedFan)
                {
                    bool mask = material.alphaMode == "Mask";
                    Require(actual.GetFloat("_Cull") == (material.doubleSided ? (float)CullMode.Off : (float)CullMode.Back) &&
                        actual.GetFloat("_AlphaClip") == (mask ? 1f : 0f) &&
                        Mathf.Abs(actual.GetFloat("_Cutoff") - material.alphaCutoff) < .000001f,
                        "Saved fan alpha/culling contract changed.");
                }
            }
        }
        static void ValidateModel(ModelRecord record)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(record.prefab);
            Require(prefab != null, "Missing output prefab " + record.prefab);
            var measured = new ModelRecord(); DescribeGeometry(prefab, measured);
            Require(measured.triangles == record.triangles && measured.vertices == record.vertices &&
                Vector3.Distance(measured.boundsMin, record.boundsMin) < .00001f &&
                Vector3.Distance(measured.boundsMax, record.boundsMax) < .00001f, "Saved prefab geometry changed.");
            foreach (Object dependency in EditorUtility.CollectDependencies(new Object[] { prefab }))
            {
                string path = AssetDatabase.GetAssetPath(dependency);
                if (dependency is Material || dependency is Texture)
                {
                    Require(!record.collision, "Non-visible collision prefab must have no material/texture dependencies: " + path);
                    Require(path.StartsWith(BakedRoot + "/", StringComparison.Ordinal), "Uncompressed/imported material dependency retained: " + path);
                }
                if (dependency is Mesh)
                    Require(path == record.source, "Unexpected mesh dependency: " + path);
            }
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                Require(renderer.sharedMaterials.Length > 0 && renderer.sharedMaterials.All(m => m != null), "Missing material reference.");
            if (record.collision)
            {
                Require(prefab.GetComponentsInChildren<Renderer>(true).Length == 0 &&
                    prefab.GetComponentsInChildren<Collider>(true).Length == 0 && record.materials == 0,
                    "Collision resource is a non-rendering mesh reference; runtime owns the Collider component.");
                ValidateHull(prefab, record.prefab);
            }
        }
        static void DescribeGeometry(GameObject root, ModelRecord record)
        {
            bool first = true;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh; Require(mesh != null, "MeshFilter has no mesh.");
                record.vertices += mesh.vertexCount;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Require(mesh.GetTopology(sub) == MeshTopology.Triangles, "Only triangle visual meshes are admitted.");
                    record.triangles += (long)mesh.GetIndexCount(sub) / 3;
                }
                Bounds bounds = mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Vector3 point = root.transform.InverseTransformPoint(filter.transform.TransformPoint(local));
                    Require(Finite(point.x) && Finite(point.y) && Finite(point.z), "Non-finite mesh bound.");
                    if (first) { record.boundsMin = point; record.boundsMax = point; first = false; }
                    else { record.boundsMin = Vector3.Min(record.boundsMin, point); record.boundsMax = Vector3.Max(record.boundsMax, point); }
                }
            }
            Require(!first && record.triangles > 0, "No real triangle geometry.");
        }
        static void ValidateTexture(TextureRecord record)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(record.path);
            TextureFormat expected = record.losslessMask ? TextureFormat.RGBA32 : TextureFormat.ASTC_4x4;
            Require(texture != null && texture.width == record.width && texture.height == record.height &&
                texture.mipmapCount == record.mips && texture.format == expected && !texture.isReadable &&
                texture.filterMode == FilterMode.Trilinear && texture.anisoLevel == 16 &&
                GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat) == record.srgb,
                "Saved texture codec/state mismatch: " + record.path);
            if (record.losslessMask)
                Require(record.width <= 2048 && record.height <= 2048 && record.payloadBytes <= 22369620 &&
                    record.mipChecks.Count == record.mips && record.mipChecks.All(m => m.copyMaximumError <= 1 &&
                        m.maximumRgbError <= 1 && m.maximumAlphaError <= 1), "Invalid explicit fan MASK lossless exception report.");
        }

        static void Calibrate()
        {
            if (calibrated) return;
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(DecoderShader);
            Require(shader != null && shader.isSupported, "Missing supported decoder shader.");
            decoder = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            bool orientationChosen = false; flipRows = false; calibrationError = 0;
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
                            bytes[p] = (byte)((x * 31 + y * 17 + mip * 61) & 255);
                            bytes[p + 1] = (byte)((x * 13 + y * 37 + mip * 29) & 255);
                            bytes[p + 2] = (byte)((x * 71 + y * 7 + mip * 43) & 255);
                            bytes[p + 3] = (byte)((x * 19 + y * 29 + mip * 47) & 255);
                        }
                        expected[mip] = bytes; known.SetPixelData(bytes, mip);
                    }
                    known.Apply(false, false);
                    for (int mip = 0; mip < 4; mip++)
                    {
                        int size = Math.Max(1, 8 >> mip); byte[] actual = RenderDecoded(known, mip, srgb);
                        if (!orientationChosen) { flipRows = MaxError(expected[mip], actual) > 1; orientationChosen = true; }
                        if (flipRows) FlipRows(actual, size, size);
                        calibrationError = Math.Max(calibrationError, MaxError(expected[mip], actual));
                        Require(calibrationError <= 1, "Known-byte linear/sRGB/mip/alpha GPU calibration failed.");
                    }
                }
                finally { Object.DestroyImmediate(known); }
            }
            calibrated = true;
        }
        static byte[] ReadPixels(Texture texture, int mip, bool srgb)
        {
            Require(calibrated, "Readback must be calibrated first.");
            byte[] bytes = RenderDecoded(texture, mip, srgb);
            if (flipRows) FlipRows(bytes, Math.Max(1, texture.width >> mip), Math.Max(1, texture.height >> mip));
            return bytes;
        }
        static byte[] RenderDecoded(Texture texture, int mip, bool srgb)
        {
            var descriptor = new RenderTextureDescriptor(Math.Max(1, texture.width >> mip), Math.Max(1, texture.height >> mip))
            { graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = 0, msaaSamples = 1, useMipMap = false, autoGenerateMips = false };
            var target = RenderTexture.GetTemporary(descriptor);
            RenderTexture previous = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
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
        {
            Require(expected.Length == actual.Length, "Readback byte count mismatch.");
            int maximum = 0; for (int i = 0; i < expected.Length; i++) maximum = Math.Max(maximum, Math.Abs(expected[i] - actual[i]));
            return maximum;
        }
        static void FlipRows(byte[] bytes, int width, int height)
        {
            int stride = width * 4;
            for (int y = 0; y < height / 2; y++) for (int x = 0; x < stride; x++)
            { int a = y * stride + x, b = (height - 1 - y) * stride + x; byte temp = bytes[a]; bytes[a] = bytes[b]; bytes[b] = temp; }
        }

        static void RollbackCore(Journal journal)
        {
            Require(journal.tool == "OceanMarketAssetBake" && journal.state != "rolled_back", "Invalid/already rolled-back journal.");
            VerifyHashes(journal.after, "Owned output edited since this transaction; manual review required");
            var allowed = new HashSet<string>(journal.after.Select(x => x.path), StringComparer.Ordinal);
            Require(OwnedFiles().All(allowed.Contains), "Unjournaled output exists; refusing rollback.");
            DeleteOwned(journal);
            foreach (var file in journal.before)
            {
                Require(IsOwned(file.path) && Hash(file.backup) == file.hash, "Invalid original backup: " + file.path);
                Directory.CreateDirectory(Path.GetDirectoryName(file.path)); File.Copy(file.backup, file.path, true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            VerifyHashes(journal.before, "Rollback bytes mismatch");
            VerifyHashes(journal.protectedFiles, "Protected old reef changed");
            journal.state = "rolled_back"; journal.success = false; Save(journal);
            Debug.Log("[MarketReefBake] rollback complete; restored " + journal.before.Count + " prior owned files.");
        }
        static void DeleteOwned(Journal journal)
        {
            var files = OwnedFiles().ToArray();
            var allowed = new HashSet<string>(journal.after.Select(x => x.path), StringComparer.Ordinal);
            Require(files.All(allowed.Contains), "Unknown file in owned namespace; refusing deletion.");
            foreach (string prefab in OwnedPrefabPaths())
            {
                if (File.Exists(prefab)) Require(AssetDatabase.DeleteAsset(prefab), "Cannot delete owned prefab " + prefab);
                else if (File.Exists(prefab + ".meta")) File.Delete(prefab + ".meta");
            }
            if (Directory.Exists(BakedRoot)) Require(AssetDatabase.DeleteAsset(BakedRoot), "Cannot delete owned baked folder.");
            else if (File.Exists(BakedRoot + ".meta")) File.Delete(BakedRoot + ".meta");
        }
        static IEnumerable<string> OwnedFiles()
        {
            if (Directory.Exists(BakedRoot))
                foreach (string path in Directory.GetFiles(BakedRoot, "*", SearchOption.AllDirectories)) yield return Normalize(path);
            if (File.Exists(BakedRoot + ".meta")) yield return BakedRoot + ".meta";
            foreach (string path in OwnedPrefabPaths())
            {
                if (File.Exists(path)) yield return path;
                if (File.Exists(path + ".meta")) yield return path + ".meta";
            }
        }
        static IEnumerable<string> OldReefFiles()
        {
            foreach (string folder in new[] { "Assets/ArtSources/RealisticReef", "Assets/ArtBaked/ReefAstc" })
                foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) yield return Normalize(path);
            foreach (string path in Directory.GetFiles(ResourceRoot, "*", SearchOption.TopDirectoryOnly))
                if (!Path.GetFileName(path).StartsWith("market_", StringComparison.Ordinal)) yield return Normalize(path);
        }
        static bool IsOwned(string path) => path == BakedRoot + ".meta" || path.StartsWith(BakedRoot + "/", StringComparison.Ordinal) ||
            OwnedPrefabPaths().Any(prefab => path == prefab || path == prefab + ".meta");
        static IEnumerable<string> OwnedPrefabPaths()
        {
            foreach (string id in Ids) for (int lod = 0; lod < 3; lod++) yield return PrefabPath(id, lod);
            foreach (string id in CollisionIds) yield return CollisionPrefabPath(id);
        }
        static void Plan(string path, Journal journal)
        {
            Require(IsOwned(path) && !File.Exists(path), "Refuse overwriting asset: " + path);
            if (!journal.plannedPaths.Contains(path)) journal.plannedPaths.Add(path);
            Save(journal);
        }
        static void MakeFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Normalize(Path.GetDirectoryName(path)); MakeFolder(parent);
            Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))), "Cannot create folder " + path);
        }
        static GLTFast.Schema.Root ReadSchema(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Require(bytes.Length >= 20 && BitConverter.ToUInt32(bytes, 0) == 0x46546c67 &&
                BitConverter.ToUInt32(bytes, 4) == 2 && BitConverter.ToUInt32(bytes, 16) == 0x4e4f534a, "Invalid GLB: " + path);
            int count = BitConverter.ToInt32(bytes, 12); Require(count > 0 && count <= bytes.Length - 20, "Invalid GLB JSON span.");
            var schema = JsonUtility.FromJson<GLTFast.Schema.Root>(Encoding.UTF8.GetString(bytes, 20, count));
            Require(schema != null, "Cannot parse material schema " + path); return schema;
        }
        static string[] ReadImageHashes(string path, GLTFast.Schema.Root schema)
        {
            byte[] bytes = File.ReadAllBytes(path);
            int jsonLength = BitConverter.ToInt32(bytes, 12), binaryHeader = 20 + jsonLength;
            Require(binaryHeader + 8 <= bytes.Length && BitConverter.ToUInt32(bytes, binaryHeader + 4) == 0x004e4942,
                "Embedded GLB binary buffer required: " + path);
            if (schema.Images == null) return Array.Empty<string>();
            var hashes = new string[schema.Images.Count];
            for (int i = 0; i < hashes.Length; i++)
            {
                var image = schema.Images[i];
                Require(string.IsNullOrEmpty(image.uri) && image.bufferView >= 0 && image.bufferView < schema.BufferViews.Count,
                    "Only embedded images are admitted: " + path);
                var view = schema.BufferViews[image.bufferView];
                Require(view.buffer == 0 && view.byteOffset >= 0 && view.byteLength > 0 &&
                    binaryHeader + 8L + view.byteOffset + view.byteLength <= bytes.Length, "Invalid image buffer span: " + path);
                using (var sha = SHA256.Create())
                    hashes[i] = BitConverter.ToString(sha.ComputeHash(bytes, binaryHeader + 8 + view.byteOffset, view.byteLength))
                        .Replace("-", "").ToLowerInvariant();
            }
            return hashes;
        }
        static string TextureRole(string property)
        {
            if (property == "baseColorTexture" || property == "_BaseMap" || property == "emissiveTexture" || property == "_EmissionMap") return "colour";
            if (property == "normalTexture" || property == "_BumpMap") return "normal";
            return "linear-data";
        }
        static string ImageHash(Input input, GLTFast.Schema.MaterialBase material, string property)
        {
            var info = TextureInfo(material, property);
            Require(info != null && info.index >= 0 && info.index < input.schema.Textures.Count,
                "Bound texture has no glTF source image metadata: " + input.path + ":" + property);
            int index = input.schema.Textures[info.index].GetImageIndex();
            Require(index >= 0 && index < input.imageHashes.Length, "Invalid image index: " + input.path);
            return input.imageHashes[index];
        }
        static GLTFast.Schema.TextureInfoBase TextureInfo(GLTFast.Schema.MaterialBase material, string property)
        {
            switch (property)
            {
                case "baseColorTexture": case "_BaseMap": return material.PbrMetallicRoughness?.BaseColorTexture;
                case "normalTexture": case "_BumpMap": return material.NormalTexture;
                case "metallicRoughnessTexture": case "_MaskMap": return material.PbrMetallicRoughness?.MetallicRoughnessTexture;
                case "occlusionTexture": case "_OcclusionMap": return material.OcclusionTexture;
                case "emissiveTexture": case "_EmissionMap": return material.EmissiveTexture;
                default: throw new InvalidOperationException("[MarketReefBake] Unsupported bound texture semantic: " + property);
            }
        }
        static List<FileState> Snapshot(IEnumerable<string> paths) => paths.Distinct().OrderBy(x => x, StringComparer.Ordinal)
            .Select(path => new FileState { path = path, hash = Hash(path) }).ToList();
        static void VerifyHashes(IEnumerable<FileState> files, string message)
        { foreach (var file in files) Require(File.Exists(file.path) && Hash(file.path) == file.hash, message + ": " + file.path); }
        static string Hash(string path)
        { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static string PrefabPath(string id, int lod) => ResourceRoot + "/market_" + id + "_lod" + lod + ".prefab";
        static string CollisionPrefabPath(string id) => ResourceRoot + "/market_" + id + "_collision.prefab";
        static string Normalize(string path) => path.Replace('\\', '/');
        static string Stamp() => DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static string Argument(string name, string fallback)
        { string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        static bool HasArgument(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
        static Journal ReadLatest()
        {
            Require(File.Exists(Latest), "No ownership journal at " + Latest);
            var journal = JsonUtility.FromJson<Journal>(File.ReadAllText(Latest));
            Require(journal != null && journal.tool == "OceanMarketAssetBake" && journal.schemaVersion == 1, "Invalid market ownership journal.");
            return journal;
        }
        static void Save(Journal journal)
        { Write(Path.Combine(journal.directory, "report.json"), journal); Write(Latest, journal); }
        static void Write(string path, Journal journal)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(journal, true), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path); File.Move(temporary, path);
        }
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("[MarketReefBake] " + message); }
    }
}
#endif

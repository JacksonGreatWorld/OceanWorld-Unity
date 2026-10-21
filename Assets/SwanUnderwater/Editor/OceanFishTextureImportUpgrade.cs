#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Explicit, guarded importer-settings repair. Never substitutes an importer or changes GLB source data.</summary>
    public static class OceanFishTextureImportUpgrade
    {
        const string One = "Assets/Resources/Fish/01_clownfish.glb";
        const string ImporterType = "GLTFast.Editor.GltfImporter";
        const string MipsPath = "importSettings.generateMipMaps";
        const string AnisoPath = "importSettings.anisotropicFilterLevel";
        static string Root => Path.GetFullPath(Environment.GetEnvironmentVariable("OCEAN_FISH_IMPORT_REPORT_DIR") ?? "Validation/iteration-26/fish-import-upgrade");
        static string Proof => Path.Combine(Root, "successful-one.json");

        [Serializable] public sealed class TextureInfo
        {
            public string identity, name, format, graphicsFormat, filter;
            public int width, height, mipCount, aniso; public bool readable;
        }
        [Serializable] public sealed class Settings { public bool generateMipMaps; public int anisotropy; public string protectedSettings; }
        [Serializable] public sealed class Snapshot
        {
            public string path, glbSha256, metaSha256, guid, importerType, importerAssembly, hierarchyHash;
            public Settings settings;
            public List<string> identities = new List<string>(), meshHashes = new List<string>(), clipHashes = new List<string>(), materialHashes = new List<string>();
            public List<TextureInfo> textures = new List<TextureInfo>();
            public int meshCount, clipCount, rendererCount; public long triangles;
        }
        [Serializable] public sealed class Item { public string backup, error; public bool changed, passed; public Snapshot before, after; }
        [Serializable] public sealed class Report
        {
            public string utc, unity, mode, directory, state, error; public bool success;
            public string scope = "Actual Unity Editor importer and imported objects; preserves source bytes, GUID/fileIDs, raw mesh data, hierarchy/skin/animation bindings and material values. Does not claim headset quality/performance.";
            public List<Item> items = new List<Item>();
        }

        [MenuItem("Swan/Codex/Fish Mipmaps/1 - Probe clownfish without changes")]
        public static void Probe() => Run("probe", new[] { One }, false);
        [MenuItem("Swan/Codex/Fish Mipmaps/2 - Upgrade only clownfish")]
        public static void UpgradeOne() => Run("one", new[] { One }, true);
        [MenuItem("Swan/Codex/Fish Mipmaps/3 - Upgrade all Fish and SchoolFish")]
        public static void UpgradeAll()
        {
            try
            {
                Require(File.Exists(Proof), "UpgradeOne must pass first.");
                Report proof = JsonUtility.FromJson<Report>(File.ReadAllText(Proof));
                Require(proof.success && proof.items.Count == 1 && proof.items[0].passed && proof.items[0].after != null &&
                    Hash(One) == proof.items[0].after.glbSha256, "Clownfish proof is absent/stale.");
                Snapshot current = Inspect(One); Compare(proof.items[0].after, current, false);
                string[] fish = Directory.GetFiles("Assets/Resources/Fish", "*.glb").OrderBy(x => x, StringComparer.Ordinal).ToArray();
                string[] school = Directory.GetFiles("Assets/Resources/SchoolFish", "*.glb").OrderBy(x => x, StringComparer.Ordinal).ToArray();
                Require(fish.Length == 25 && school.Length == 3, "Expected 25 full fish and 3 school resources.");
                Run("all", fish.Concat(school).ToArray(), true);
            }
            catch (Exception e) { FailOutsideRun(e); }
        }

        static void Run(string mode, string[] paths, bool change)
        {
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion, mode = mode,
                directory = Path.Combine(Root, mode + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")), state = "inspecting" };
            Directory.CreateDirectory(report.directory);
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Use Edit Mode only.");
                // Inspect every importer before modifying any source settings. Unknown actual types stop here.
                foreach (string path in paths)
                {
                    var item = new Item(); report.items.Add(item);
                    try { item.before = Inspect(path); Save(report); CheckImporter(item.before); }
                    catch (Exception e) { item.error = e.ToString(); throw; }
                }
                if (change)
                {
                    report.state = "reimporting"; Save(report);
                    foreach (Item item in report.items)
                    {
                        Snapshot before = item.before;
                        if (before.settings.generateMipMaps && before.settings.anisotropy == 16 && before.textures.All(HasFullMips))
                        { item.after = before; item.passed = true; Save(report); continue; }
                        item.backup = Path.Combine(report.directory, "originals", before.guid);
                        Directory.CreateDirectory(item.backup);
                        File.Copy(before.path, Path.Combine(item.backup, Path.GetFileName(before.path)));
                        File.Copy(before.path + ".meta", Path.Combine(item.backup, Path.GetFileName(before.path) + ".meta"));
                        Require(Hash(Path.Combine(item.backup, Path.GetFileName(before.path))) == before.glbSha256, "GLB backup mismatch.");
                        Require(Hash(Path.Combine(item.backup, Path.GetFileName(before.path) + ".meta")) == before.metaSha256, "Metadata backup mismatch.");
                        Save(report);
                        try
                        {
                            AssetImporter importer = AssetImporter.GetAtPath(before.path);
                            Require(importer != null && importer.GetType().FullName == ImporterType, "Actual importer changed; refusing replacement.");
                            var serialized = new SerializedObject(importer); serialized.Update();
                            SerializedProperty mips = serialized.FindProperty(MipsPath), aniso = serialized.FindProperty(AnisoPath);
                            Require(mips != null && mips.propertyType == SerializedPropertyType.Boolean && aniso != null && aniso.propertyType == SerializedPropertyType.Integer,
                                "Expected existing serialized settings unavailable; refusing default initialization.");
                            mips.boolValue = true; aniso.intValue = 16;
                            serialized.ApplyModifiedPropertiesWithoutUndo();
                            item.changed = true; Save(report);
                            importer.SaveAndReimport();
                            item.after = Inspect(before.path); Save(report);
                            Compare(before, item.after, true); item.passed = true; Save(report);
                        }
                        catch (Exception e) { item.error = e.ToString(); throw; }
                    }
                }
                else foreach (Item item in report.items) item.passed = true;
                report.success = true; report.state = change ? "upgraded" : "probed"; Save(report);
                if (mode == "one" && change) File.WriteAllText(Proof, JsonUtility.ToJson(report, true));
                Debug.Log("[FishImport] " + mode + " PASS assets=" + report.items.Count + " report=" + Path.Combine(report.directory, "report.json"));
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                report.error = e.ToString(); report.state = "failed"; Save(report);
                Debug.LogError("[FishImport] Stopped. No importer replacement is attempted. Review original GLB/meta backups and before/after records before building. " + Path.Combine(report.directory, "report.json"));
                FailOutsideRun(e);
            }
        }
        static void FailOutsideRun(Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); else throw new InvalidOperationException("Fish importer upgrade stopped.", e); }

        static Snapshot Inspect(string path)
        {
            Require(File.Exists(path) && File.Exists(path + ".meta"), "Missing asset/meta: " + path);
            AssetImporter importer = AssetImporter.GetAtPath(path);
            var snapshot = new Snapshot { path = path, glbSha256 = Hash(path), metaSha256 = Hash(path + ".meta"), guid = AssetDatabase.AssetPathToGUID(path),
                importerType = importer == null ? "<null>" : importer.GetType().FullName, importerAssembly = importer == null ? "<null>" : importer.GetType().Assembly.FullName };
            if (importer == null || snapshot.importerType != ImporterType) return snapshot;
            var serialized = new SerializedObject(importer); serialized.Update();
            SerializedProperty mips = serialized.FindProperty(MipsPath), aniso = serialized.FindProperty(AnisoPath);
            if (mips == null || aniso == null) return snapshot;
            snapshot.settings = new Settings { generateMipMaps = mips.boolValue, anisotropy = aniso.intValue,
                protectedSettings = SettingsHash(serialized) };
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object item in all.Where(x => x != null).OrderBy(Identity, StringComparer.Ordinal))
            {
                string id = Identity(item); snapshot.identities.Add(id);
                if (item is Texture2D texture) snapshot.textures.Add(new TextureInfo { identity = id, name = texture.name, width = texture.width, height = texture.height,
                    mipCount = texture.mipmapCount, aniso = texture.anisoLevel, format = texture.format.ToString(), graphicsFormat = texture.graphicsFormat.ToString(),
                    filter = texture.filterMode.ToString(), readable = texture.isReadable });
                if (item is Mesh mesh)
                {
                    snapshot.meshCount++; snapshot.meshHashes.Add(id + " " + MeshHash(mesh));
                    for (int s = 0; s < mesh.subMeshCount; s++) if (mesh.GetTopology(s) == MeshTopology.Triangles) snapshot.triangles += mesh.GetIndexCount(s) / 3;
                }
                if (item is AnimationClip clip) { snapshot.clipCount++; snapshot.clipHashes.Add(id + " " + ClipHash(clip)); }
                if (item is Material material) snapshot.materialHashes.Add(id + " " + MaterialHash(material));
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Require(model != null && snapshot.meshCount > 0 && snapshot.textures.Count > 0, "Missing real imported model/mesh/textures.");
            snapshot.rendererCount = model.GetComponentsInChildren<Renderer>(true).Length;
            snapshot.hierarchyHash = HierarchyHash(model);
            return snapshot;
        }

        static void CheckImporter(Snapshot snapshot)
        {
            Require(snapshot.importerType == ImporterType, "Actual importer is " + snapshot.importerType + "; only existing " + ImporterType + " is supported: " + snapshot.path);
            Require(snapshot.settings != null, "Importer settings are absent; do not initialize defaults or change node/animation methods.");
        }
        static void Compare(Snapshot before, Snapshot after, bool upgraded)
        {
            CheckImporter(after);
            Require(before.glbSha256 == after.glbSha256 && before.guid == after.guid && before.importerType == after.importerType, "Source bytes/GUID/importer changed.");
            Require(before.settings.protectedSettings == after.settings.protectedSettings, "Unrelated importer setting changed (including node naming, animation and instantiation).");
            Require(before.identities.SequenceEqual(after.identities), "Imported subasset GUID/fileID/name/type changed.");
            Require(before.meshHashes.SequenceEqual(after.meshHashes) && before.triangles == after.triangles && before.meshCount == after.meshCount, "Mesh geometry, vertex data, skin data or mesh binding changed.");
            Require(before.clipHashes.SequenceEqual(after.clipHashes) && before.clipCount == after.clipCount, "Animation curves/settings/events changed.");
            Require(before.materialHashes.SequenceEqual(after.materialHashes), "Material properties/texture references/UV transforms changed.");
            Require(before.hierarchyHash == after.hierarchyHash && before.rendererCount == after.rendererCount, "Hierarchy, transform, renderer or animation/skin binding changed.");
            Require(before.textures.Count == after.textures.Count, "Texture count changed.");
            for (int i = 0; i < before.textures.Count; i++)
            {
                TextureInfo a = before.textures[i], b = after.textures[i];
                Require(a.identity == b.identity && a.width == b.width && a.height == b.height && a.format == b.format && a.graphicsFormat == b.graphicsFormat && a.readable == b.readable && a.filter == b.filter,
                    "Texture identity/mip0 dimensions/format/colorspace/readability/filter changed.");
                if (upgraded) Require(HasFullMips(b) && b.aniso == 16, "Generated asset still lacks full mips/aniso16: " + b.identity);
                else Require(a.mipCount == b.mipCount && a.aniso == b.aniso, "Pilot texture state changed after proof.");
            }
            if (upgraded) Require(after.settings.generateMipMaps && after.settings.anisotropy == 16, "Requested importer settings not persisted.");
        }
        static bool HasFullMips(TextureInfo info) { int size = Math.Max(info.width, info.height), count = 1; while (size > 1) { size >>= 1; count++; } return info.mipCount == count; }

        static string SettingsHash(SerializedObject serialized)
        {
            var rows = new List<string>();
            foreach (string root in new[] { "importSettings", "editorImportSettings", "instantiationSettings" })
            {
                SerializedProperty property = serialized.FindProperty(root); Require(property != null, "Missing settings group " + root);
                SerializedProperty iterator = property.Copy(), end = property.GetEndProperty();
                while (iterator.Next(true) && !SerializedProperty.EqualContents(iterator, end))
                {
                    if (iterator.propertyPath == MipsPath || iterator.propertyPath == AnisoPath) continue;
                    if (iterator.propertyType == SerializedPropertyType.Generic) continue;
                    string value;
                    switch (iterator.propertyType)
                    {
                        case SerializedPropertyType.Boolean: value = iterator.boolValue.ToString(); break;
                        case SerializedPropertyType.Integer: case SerializedPropertyType.Enum: value = iterator.longValue.ToString(); break;
                        case SerializedPropertyType.Float: value = iterator.doubleValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture); break;
                        case SerializedPropertyType.String: value = iterator.stringValue; break;
                        case SerializedPropertyType.ObjectReference: value = Identity(iterator.objectReferenceValue); break;
                        default: throw new InvalidOperationException("Unsupported settings leaf; refusing incomplete comparison: " + iterator.propertyPath + " " + iterator.propertyType);
                    }
                    rows.Add(iterator.propertyPath + "=" + value);
                }
            }
            return string.Join("\n", rows);
        }

        static string MeshHash(Mesh mesh) => Digest(writer =>
        {
            writer.Write(mesh.vertexCount); writer.Write(mesh.subMeshCount); writer.Write((int)mesh.indexFormat); Write(writer, mesh.bounds);
            foreach (var descriptor in mesh.GetVertexAttributes()) { writer.Write((int)descriptor.attribute); writer.Write((int)descriptor.format); writer.Write(descriptor.dimension); writer.Write(descriptor.stream); }
            using (var read = MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var data = read[0]; writer.Write(data.vertexBufferCount);
                for (int stream = 0; stream < data.vertexBufferCount; stream++) { var bytes = data.GetVertexData<byte>(stream); writer.Write(bytes.Length); writer.Write(bytes.ToArray()); }
                var indices = data.GetIndexData<byte>(); writer.Write(indices.Length); writer.Write(indices.ToArray());
                for (int s = 0; s < data.subMeshCount; s++) { var part = data.GetSubMesh(s); writer.Write(part.indexStart); writer.Write(part.indexCount); writer.Write((int)part.topology); writer.Write(part.baseVertex); writer.Write(part.firstVertex); writer.Write(part.vertexCount); Write(writer, part.bounds); }
            }
            var poses = mesh.bindposes; writer.Write(poses.Length); foreach (var matrix in poses) for (int k = 0; k < 16; k++) writer.Write(matrix[k]);
            writer.Write(mesh.blendShapeCount);
            if (mesh.blendShapeCount > 0)
            {
                Require(mesh.isReadable, "Cannot fully inspect unreadable blend-shape data; stop before mutation.");
                var points = new Vector3[mesh.vertexCount]; var normals = new Vector3[mesh.vertexCount]; var tangents = new Vector3[mesh.vertexCount];
                for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                {
                    writer.Write(mesh.GetBlendShapeName(shape)); writer.Write(mesh.GetBlendShapeFrameCount(shape));
                    for (int f = 0; f < mesh.GetBlendShapeFrameCount(shape); f++) { writer.Write(mesh.GetBlendShapeFrameWeight(shape, f)); mesh.GetBlendShapeFrameVertices(shape, f, points, normals, tangents); for (int k = 0; k < points.Length; k++) { Write(writer, points[k]); Write(writer, normals[k]); Write(writer, tangents[k]); } }
                }
            }
        });
        static string ClipHash(AnimationClip clip) => Digest(writer =>
        {
            writer.Write(clip.name); writer.Write(clip.legacy); writer.Write(clip.frameRate); writer.Write(clip.length); writer.Write((int)clip.wrapMode);
            writer.Write(JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(clip)));
            foreach (var binding in AnimationUtility.GetCurveBindings(clip).OrderBy(BindingKey, StringComparer.Ordinal))
            {
                writer.Write(BindingKey(binding)); AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding); writer.Write((int)curve.preWrapMode); writer.Write((int)curve.postWrapMode);
                var keys = curve.keys; writer.Write(keys.Length);
                foreach (var key in keys) { writer.Write(key.time); writer.Write(key.value); writer.Write(key.inTangent); writer.Write(key.outTangent); writer.Write(key.inWeight); writer.Write(key.outWeight); writer.Write((int)key.weightedMode); }
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip).OrderBy(BindingKey, StringComparer.Ordinal))
            { writer.Write(BindingKey(binding)); foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding)) { writer.Write(key.time); writer.Write(Identity(key.value)); } }
            foreach (var item in AnimationUtility.GetAnimationEvents(clip)) { writer.Write(item.time); writer.Write(item.functionName); writer.Write(item.stringParameter); writer.Write(item.floatParameter); writer.Write(item.intParameter); writer.Write((int)item.messageOptions); writer.Write(Identity(item.objectReferenceParameter)); }
        });
        static string BindingKey(EditorCurveBinding value) => value.path + "/" + value.type.FullName + "/" + value.propertyName;
        static string MaterialHash(Material material) => Digest(writer =>
        {
            writer.Write(Identity(material.shader)); writer.Write(material.renderQueue); writer.Write(material.enableInstancing); writer.Write(material.doubleSidedGI); writer.Write((int)material.globalIlluminationFlags);
            writer.Write(string.Join("|", material.shaderKeywords.OrderBy(x => x, StringComparer.Ordinal)));
            for (int p = 0; p < material.shader.GetPropertyCount(); p++)
            {
                string property = material.shader.GetPropertyName(p); writer.Write(property); var type = material.shader.GetPropertyType(p); writer.Write((int)type);
                switch (type)
                {
                    case ShaderPropertyType.Float: case ShaderPropertyType.Range: writer.Write(material.GetFloat(property)); break;
                    case ShaderPropertyType.Int: writer.Write(material.GetInteger(property)); break;
                    case ShaderPropertyType.Vector: Write(writer, material.GetVector(property)); break;
                    case ShaderPropertyType.Color: Write(writer, (Vector4)material.GetColor(property)); break;
                    case ShaderPropertyType.Texture: writer.Write(Identity(material.GetTexture(property))); Write(writer, material.GetTextureScale(property)); Write(writer, material.GetTextureOffset(property)); break;
                }
            }
        });
        static string HierarchyHash(GameObject root) => Digest(writer =>
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                writer.Write(Identity(transform)); writer.Write(transform.name); writer.Write(transform.GetSiblingIndex()); writer.Write(Identity(transform.parent)); writer.Write(transform.gameObject.activeSelf);
                Write(writer, transform.localPosition); Write(writer, transform.localScale); Write(writer, (Vector4)new Vector4(transform.localRotation.x, transform.localRotation.y, transform.localRotation.z, transform.localRotation.w));
                foreach (Component component in transform.GetComponents<Component>())
                {
                    writer.Write(Identity(component));
                    if (component is MeshFilter filter) writer.Write(Identity(filter.sharedMesh));
                    if (component is Renderer renderer) { writer.Write(renderer.enabled); writer.Write((int)renderer.shadowCastingMode); writer.Write(renderer.receiveShadows); foreach (Material mat in renderer.sharedMaterials) writer.Write(Identity(mat)); }
                    if (component is SkinnedMeshRenderer skin) { writer.Write(Identity(skin.sharedMesh)); writer.Write(Identity(skin.rootBone)); writer.Write((int)skin.quality); writer.Write(skin.updateWhenOffscreen); foreach (var bone in skin.bones) writer.Write(Identity(bone)); if (skin.sharedMesh != null) for (int shape = 0; shape < skin.sharedMesh.blendShapeCount; shape++) writer.Write(skin.GetBlendShapeWeight(shape)); }
                    if (component is Animator animator) { writer.Write(Identity(animator.avatar)); writer.Write(Identity(animator.runtimeAnimatorController)); writer.Write(animator.applyRootMotion); writer.Write((int)animator.updateMode); writer.Write((int)animator.cullingMode); }
                    if (component is Animation animation) { writer.Write(Identity(animation.clip)); writer.Write(animation.playAutomatically); writer.Write((int)animation.wrapMode); foreach (AnimationState state in animation) { writer.Write(state.name); writer.Write(Identity(state.clip)); } }
                }
            }
        });
        static string Identity(Object item)
        {
            if (item == null) return "<null>";
            string guid; long id;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(item, out guid, out id)) return item.GetType().FullName + "|" + item.name + "|" + guid + "|" + id;
            Require(!string.IsNullOrEmpty(item.name), "Unidentified unnamed imported object.");
            return "builtin|" + item.GetType().FullName + "|" + item.name;
        }
        static string Digest(Action<BinaryWriter> write)
        { using (var stream = new MemoryStream()) { using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) write(writer); using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant(); } }
        static string Hash(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static void Write(BinaryWriter w, Vector2 x) { w.Write(x.x); w.Write(x.y); }
        static void Write(BinaryWriter w, Vector3 x) { w.Write(x.x); w.Write(x.y); w.Write(x.z); }
        static void Write(BinaryWriter w, Vector4 x) { w.Write(x.x); w.Write(x.y); w.Write(x.z); w.Write(x.w); }
        static void Write(BinaryWriter w, Bounds x) { Write(w, x.center); Write(w, x.extents); }
        static void Save(Report report) { File.WriteAllText(Path.Combine(report.directory, "report.json"), JsonUtility.ToJson(report, true)); }
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException("[FishImport] " + message); }
    }
}
#endif

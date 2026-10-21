#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Excluded source identities remain auditable. Public exports may omit raw archives; neither form is a player dependency.</summary>
    public static class OceanPlayerContentBuildChecks
    {
        public const string ManifestPath = "BuildProfiles/first-20-player-content.json";
        const string ResourcesPrefix = "Assets/Resources/";
        const string ArchivePrefix = "Assets/SourceOnly/UnusedResources/";
        const string SourceOnlyPrefix = "Assets/SourceOnly/";
        public static int Assertions { get; private set; }

        [Serializable] sealed class ContentProfile
        {
            public int version, playableLevelCount, legacyCatalogCount, availableObservationCount;
            public string sourceArchivePolicy;
            public long excludedSourceBytes, excludedMetaBytes;
            public ArchivedAsset[] rows;
            public RequiredResource[] requiredResources;
            public RetainedFile[] retainedAttributionFiles;
        }
        [Serializable] sealed class ArchivedAsset
        {
            public string original, archive, sha256, metaSha256, guid;
            public long sourceBytes, metaBytes;
        }
        [Serializable] sealed class RequiredResource { public string key, source; }
        [Serializable] sealed class RetainedFile { public string path, sha256; }
        [Serializable] sealed class PackedSummary
        {
            public bool success;
            public string profile, profileSha256, packedBytes, scope;
            public int packedContainers, entries, uniqueSources, excludedAssets;
            public string[] violations;
        }

        static void Check(bool condition, string message)
        { Assertions++; if (!condition) throw new BuildFailedException("Player content: " + message); }

        static string ProjectPath(string relative)
        {
            Check(!string.IsNullOrEmpty(relative) && !Path.IsPathRooted(relative), "expected a project-relative path");
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string resolved = Path.GetFullPath(Path.Combine(root, relative));
            Check(resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                "path must remain inside this project: " + relative);
            return resolved;
        }
        static string Sha256(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static ContentProfile ReadProfile()
        {
            string path = ProjectPath(ManifestPath);
            Check(File.Exists(path), "missing shared first-20 content profile");
            var profile = JsonUtility.FromJson<ContentProfile>(File.ReadAllText(path));
            Check(profile != null && profile.version == 1 && profile.rows != null && profile.rows.Length == 12,
                "expected version 1 with twelve historical source-only mappings");
            Check(profile.sourceArchivePolicy == "required" || profile.sourceArchivePolicy == "provenance-only",
                "sourceArchivePolicy must explicitly require archives or declare omitted public provenance");
            Check(profile.playableLevelCount == 20 && profile.legacyCatalogCount == 100 && profile.availableObservationCount == 30,
                "playability policy must not truncate durable campaign or observation identities");
            Check(profile.requiredResources != null && profile.requiredResources.Length > 0 &&
                profile.retainedAttributionFiles != null && profile.retainedAttributionFiles.Length > 0,
                "required runtime dependencies and attribution must be explicit");
            return profile;
        }

        static string ResourceKey(string source)
        {
            const string segment = "/Resources/";
            int start = source.LastIndexOf(segment, StringComparison.Ordinal);
            Check(start >= 0, "resource source must be inside a Resources folder: " + source);
            string key = source.Substring(start + segment.Length);
            return key.Substring(0, key.Length - Path.GetExtension(key).Length);
        }
        static bool Forbidden(string source, HashSet<string> originals) =>
            !string.IsNullOrEmpty(source) && (source.StartsWith(SourceOnlyPrefix, StringComparison.Ordinal) || originals.Contains(source));

        [MenuItem("OceanWorld/Validation/First twenty player content")]
        public static void RunAll()
        {
            Assertions = 0;
            Check(!EditorApplication.isCompiling && !EditorApplication.isUpdating, "wait for Unity imports before checking actual resources");
            var profile = ReadProfile();
            var originals = new HashSet<string>(StringComparer.Ordinal);
            var archives = new HashSet<string>(StringComparer.Ordinal);
            var guids = new HashSet<string>(StringComparer.Ordinal);
            long sourceBytes = 0, metaBytes = 0;
            int verifiedArchives = 0, omittedArchives = 0;
            foreach (var row in profile.rows)
            {
                Check(row != null && row.original != null && row.original.StartsWith(ResourcesPrefix, StringComparison.Ordinal),
                    "every exclusion must describe its original Resources location");
                Check(row.archive == ArchivePrefix + row.original.Substring(ResourcesPrefix.Length),
                    "archive retains its original resource-relative path: " + row.original);
                Check(originals.Add(row.original) && archives.Add(row.archive) && guids.Add(row.guid), "archive mappings and GUIDs must be unique");
                string original = ProjectPath(row.original), archived = ProjectPath(row.archive);
                Check(!File.Exists(original) && !File.Exists(original + ".meta"), "excluded original/meta remains in Resources: " + row.original);
                Check(Regex.IsMatch(row.sha256 ?? "", "^[a-f0-9]{64}$") && Regex.IsMatch(row.metaSha256 ?? "", "^[a-f0-9]{64}$") &&
                    Regex.IsMatch(row.guid ?? "", "^[a-f0-9]{32}$") && row.sourceBytes > 0 && row.metaBytes > 0,
                    "historical archive identity must remain complete: " + row.archive);
                bool hasSource = File.Exists(archived), hasMeta = File.Exists(archived + ".meta");
                if (hasSource || hasMeta)
                {
                    Check(hasSource && hasMeta, "archive and its original meta must appear together: " + row.archive);
                    Check(new FileInfo(archived).Length == row.sourceBytes && Sha256(archived) == row.sha256, "source bytes changed: " + row.archive);
                    Check(new FileInfo(archived + ".meta").Length == row.metaBytes && Sha256(archived + ".meta") == row.metaSha256, "original meta bytes changed: " + row.archive);
                    Match match = Regex.Match(File.ReadAllText(archived + ".meta"), @"(?m)^guid: ([a-f0-9]{32})\r?$");
                    Check(match.Success && match.Groups[1].Value == row.guid, "preserved GUID differs from manifest: " + row.archive);
                    Check(AssetDatabase.AssetPathToGUID(row.archive) == row.guid && AssetDatabase.GUIDToAssetPath(row.guid) == row.archive,
                        "Unity must resolve the one preserved GUID to the archive: " + row.archive);
                    verifiedArchives++;
                }
                else
                {
                    Check(profile.sourceArchivePolicy == "provenance-only", "required source archive is missing: " + row.archive);
                    Check(string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(row.guid)),
                        "omitted archive GUID resolves to another imported asset: " + row.original);
                    omittedArchives++;
                }
                Check(Resources.Load<UnityEngine.Object>(ResourceKey(row.original)) == null,
                    "excluded model is still loadable through Resources: " + row.original);
                sourceBytes += row.sourceBytes; metaBytes += row.metaBytes;
            }
            Check(sourceBytes == profile.excludedSourceBytes && metaBytes == profile.excludedMetaBytes,
                "historical source-byte accounting agrees with the manifest; omitted archives are not verified or shipped; this is not an APK saving claim");
            RunPackedPolicyRegression(originals);

            var requiredKeys = new HashSet<string>(StringComparer.Ordinal);
            var dependencyRoots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in profile.requiredResources)
            {
                Check(row != null && !string.IsNullOrEmpty(row.source) && row.key == ResourceKey(row.source) && requiredKeys.Add(row.key),
                    "required Resources key/source must agree and be unique");
                Check(File.Exists(ProjectPath(row.source)), "required runtime source missing: " + row.source);
                UnityEngine.Object loaded = Resources.Load<UnityEngine.Object>(row.key);
                Check(loaded != null && AssetDatabase.GetAssetPath(loaded) == row.source,
                    "required dependency resolves to its real source: " + row.key);
                dependencyRoots.Add(row.source);
            }
            int available = 0;
            foreach (var species in OceanSpeciesCatalog.All)
            {
                if (!OceanSpeciesCatalog.IsAvailable(species.Id)) continue;
                available++;
                Check(species.FirstLevel <= profile.playableLevelCount, "available observation must be reachable in the open levels: " + species.Id);
                if (!string.IsNullOrEmpty(species.ResourcePath))
                    Check(requiredKeys.Contains(species.ResourcePath) && Resources.Load<GameObject>(species.ResourcePath) != null,
                        "all current observation models remain: " + species.Id);
            }
            Check(available == profile.availableObservationCount && OceanSpeciesCatalog.AvailableCount == available,
                "all thirty available observation categories remain without reactivating retired ID13");
            Check(OceanLevelCatalog.Count == profile.legacyCatalogCount && requiredKeys.Contains("OceanCampaign/extended-levels"),
                "the complete authored catalog remains available for historical save validation");
            foreach (var row in profile.retainedAttributionFiles)
            {
                Check(row != null && !string.IsNullOrEmpty(row.path), "attribution entry must name its original file");
                string path = ProjectPath(row.path);
                Check(File.Exists(path) && Sha256(path) == row.sha256, "third-party attribution changed or disappeared: " + row.path);
            }
            foreach (var scene in EditorBuildSettings.scenes) if (scene.enabled) dependencyRoots.Add(scene.path);
            foreach (UnityEngine.Object preload in PlayerSettings.GetPreloadedAssets())
                if (preload != null)
                {
                    string path = AssetDatabase.GetAssetPath(preload);
                    if (!string.IsNullOrEmpty(path)) dependencyRoots.Add(path);
                }
            string[] roots = new string[dependencyRoots.Count]; dependencyRoots.CopyTo(roots);
            foreach (string dependency in AssetDatabase.GetDependencies(roots, true))
                Check(!Forbidden(dependency, originals), "scene, preload or real runtime dependency pulls source-only content into player: " + dependency);
            Debug.Log("OceanPlayerContentBuildChecks PASS " + Assertions + " assertions; declared historical source bytes=" + sourceBytes +
                "; archive policy=" + profile.sourceArchivePolicy + "; verified archives=" + verifiedArchives + "; omitted archives=" + omittedArchives +
                "; excluded identities=" + profile.rows.Length + "; observations=" + available + "; actual APK savings require a completed build.");
        }

        static string[] PackedViolations(IEnumerable<string> paths, HashSet<string> originals)
        {
            var violations = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in paths) if (Forbidden(path, originals)) violations.Add(path);
            string[] result = new string[violations.Count]; violations.CopyTo(result); Array.Sort(result, StringComparer.Ordinal);
            return result;
        }
        static void RunPackedPolicyRegression(HashSet<string> originals)
        {
            Check(PackedViolations(new[] { "Assets/Resources/Shaders/FishSwim.shader", "" }, originals).Length == 0,
                "ordinary player entries remain allowed");
            foreach (string original in originals)
            {
                Check(PackedViolations(new[] { original }, originals).Length == 1, "packed old Resources path must be rejected: " + original);
                string archive = ArchivePrefix + original.Substring(ResourcesPrefix.Length);
                Check(PackedViolations(new[] { archive }, originals).Length == 1, "packed source archive must be rejected even in provenance-only exports: " + archive);
            }
            Check(PackedViolations(new[] { SourceOnlyPrefix + "Unexpected/FutureAsset.prefab" }, originals).Length == 1,
                "any unexpected SourceOnly dependency must also be rejected");
        }

        /// <summary>Inspect every actual packed entry; write complete evidence before rejecting a leak.</summary>
        public static void ValidatePackedAssets(BuildReport report, string outputDir)
        {
            Check(report != null, "actual BuildReport is required");
            var profile = ReadProfile();
            var originals = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in profile.rows) originals.Add(row.original);
            var uniqueSources = new HashSet<string>(StringComparer.Ordinal);
            var packedSourcePaths = new List<string>();
            var tsv = new StringBuilder("packed_container\tpacked_type\tbytes\tsource\n");
            ulong bytes = 0;
            int entries = 0, container = 0;
            foreach (var packed in report.packedAssets)
            {
                foreach (var asset in packed.contents)
                {
                    string path = asset.sourceAssetPath ?? "";
                    tsv.Append(container).Append('\t').Append(asset.type == null ? "" : asset.type.FullName).Append('\t')
                        .Append(asset.packedSize.ToString(CultureInfo.InvariantCulture)).Append('\t')
                        .Append(path.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')).Append('\n');
                    entries++; checked { bytes += asset.packedSize; }
                    if (!string.IsNullOrEmpty(path)) uniqueSources.Add(path);
                    packedSourcePaths.Add(path);
                }
                container++;
            }
            string[] leaked = PackedViolations(packedSourcePaths, originals);
            var summary = new PackedSummary { success = entries > 0 && leaked.Length == 0, profile = ManifestPath, profileSha256 = Sha256(ProjectPath(ManifestPath)),
                packedContainers = container, entries = entries, uniqueSources = uniqueSources.Count, packedBytes = bytes.ToString(CultureInfo.InvariantCulture),
                excludedAssets = profile.rows.Length, violations = leaked,
                scope = "Every BuildReport.packedAssets content entry. Packed payload bytes exclude archive compression, native libraries and StreamingAssets copied outside packed files; not an APK byte total or runtime performance claim." };
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "all-packed-assets.tsv"), tsv.ToString());
            File.WriteAllText(Path.Combine(outputDir, "player-content-packed-report.json"), JsonUtility.ToJson(summary, true) + "\n");
            Check(entries > 0, "BuildReport must contain actual packed entries");
            Check(leaked.Length == 0, "source-only assets leaked into player: " + string.Join(", ", leaked));
            Debug.Log("[PlayerContentBuild] entries=" + entries + " sources=" + uniqueSources.Count + " excluded_leaks=0; archive policy=" + profile.sourceArchivePolicy + "; raw archive preservation is not claimed for public exports.");
        }
    }
}
#endif

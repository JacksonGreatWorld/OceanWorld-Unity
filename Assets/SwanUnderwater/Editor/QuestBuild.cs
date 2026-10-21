using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using XRHandsTracking = UnityEngine.XR.Hands.OpenXR.HandTracking;

namespace SwanUnderwater.Editor
{
    /// <summary>Temporary Quest Android profile. Never changes scripting defines or the source scene.</summary>
    public static class QuestBuild
    {
        public const string PackageId = "com.example.oceanworld.quest";
        public static bool Active { get; private set; }
        internal static readonly string[] RequiredFeatures =
        {
            "com.unity.openxr.feature.metaquest",
            "com.unity.openxr.feature.input.oculustouch",
            "com.unity.openxr.feature.input.metaquestplus",
            "com.unity.openxr.feature.input.metahandtrackingaim",
            "com.unity.openxr.feature.input.handinteraction",
            "com.unity.openxr.feature.compositionlayers",
            "com.unity.openxr.feature.arfoundation-meta-session",
            "com.unity.openxr.feature.arfoundation-meta-camera",
            "MetaOpenXR-OpenXRLifeCycle"
        };

        [MenuItem("Swan/Codex/Build Quest 3 Android release")]
        public static void BuildAndroid()
        {
            using (PrepareQuest3()) CodexBuild.BuildAndroid();
        }

        public static IDisposable PrepareQuest3()
        {
            if (Active || BuildPipeline.isBuildingPlayer || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new BuildFailedException("Quest profile requires an idle Android Editor and cannot be nested.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new BuildFailedException("Launch with -buildTarget Android before preparing Quest; no domain reload inside the transaction.");
            var scope = new ProfileScope();
            try { Active = true; scope.Apply(); return scope; }
            catch { scope.Dispose(); throw; }
        }

        internal static bool IsPicoBinary(PluginImporter plugin)
        {
            string path = plugin.assetPath.Replace('\\', '/');
            bool pico = path.StartsWith("Packages/com.bytedance.pico.xr/", StringComparison.Ordinal) ||
                path.StartsWith("Packages/Local/PICO-Unity-SDK-main/XR/", StringComparison.Ordinal);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            // Keep managed DLLs available for PICO C# compilation. They are never a Quest XR provider.
            return pico && (plugin.isNativePlugin || ext == ".aar" || ext == ".jar" || ext == ".so" || ext == ".androidlib");
        }

        internal static string[] PicoAndroidPluginPaths() => PluginImporter.GetImporters(BuildTarget.Android)
            .Where(IsPicoBinary).Select(plugin => plugin.assetPath).OrderBy(path => path, StringComparer.Ordinal).ToArray();

        internal static string PluginMetaPath(PluginImporter plugin)
        {
            string path = plugin.assetPath.Replace('\\', '/');
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            if (package != null && path.StartsWith(package.assetPath + "/", StringComparison.Ordinal))
                return Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1)) + ".meta";
            return Path.GetFullPath(path + ".meta");
        }

        internal static void ValidatePrepared()
        {
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            var loaders = general?.AssignedSettings?.activeLoaders;
            Require(Active && loaders != null && loaders.Count == 1 && loaders[0] is OpenXRLoader,
                "Quest must have exactly one Android loader: Unity OpenXR.");
            Require(general.InitManagerOnStart, "Quest XR must initialize at startup.");
            Require(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) == PackageId, "Quest package ID mismatch.");
            Require(PlayerSettings.Android.minSdkVersion >= AndroidSdkVersions.AndroidApiLevel32, "Meta passthrough requires minSDK32.");
            foreach (string id in RequiredFeatures)
                Require(FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, id)?.enabled == true,
                    "Missing Quest OpenXR feature: " + id);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            Require(settings.GetFeature<XRHandsTracking>()?.enabled == true,
                "Quest requires the XR Hands HandTracking subsystem feature, not Microsoft Hand Interaction.");
            foreach (var feature in settings.GetFeatures<OpenXRFeature>())
            {
                Require(!feature.enabled || !feature.GetType().FullName.StartsWith("ByteDance.", StringComparison.Ordinal),
                    "A PICO OpenXR feature was enabled in the Quest profile.");
                Require(!feature.enabled || feature.GetType().FullName != "UnityEngine.XR.OpenXR.Features.Interactions.MicrosoftHandInteraction",
                    "The deprecated Microsoft Hand Interaction profile must remain disabled on Quest.");
            }
            foreach (var plugin in PluginImporter.GetImporters(BuildTarget.Android))
                Require(!IsPicoBinary(plugin), "PICO native plugin still eligible for Quest: " + plugin.assetPath);
        }

        internal static void Require(bool condition, string message)
        {
            if (!condition) throw new BuildFailedException(message);
        }

        sealed class AssetReference
        {
            public readonly string guid, path, typeName;
            public readonly long localId;
            public AssetReference(UnityEngine.Object value)
            {
                Require(value != null, "Quest restoration cannot snapshot a missing asset.");
                Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string assetGuid, out long assetLocalId),
                    "Quest restoration requires a persistent asset reference.");
                guid = assetGuid;
                localId = assetLocalId;
                path = AssetDatabase.GetAssetPath(value);
                typeName = value.GetType().FullName;
            }
            public T Resolve<T>() where T : UnityEngine.Object
            {
                string currentPath = AssetDatabase.GUIDToAssetPath(guid);
                Require(!string.IsNullOrEmpty(currentPath), "Restoration asset is missing: " + path + " (" + guid + ")");
                // Load the main asset first, then inspect subassets such as per-platform XR settings/features.
                AssetDatabase.LoadMainAssetAtPath(currentPath);
                foreach (var value in AssetDatabase.LoadAllAssetsAtPath(currentPath))
                    if (value is T typed && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string id, out long fileId) &&
                        id == guid && fileId == localId) return typed;
                throw new BuildFailedException("Restoration subasset is missing: " + currentPath + "#" + localId + " (" + typeName + ")");
            }
        }

        sealed class ObjectState
        {
            readonly AssetReference asset;
            readonly string json;
            readonly Dictionary<string, AssetReference> references = new Dictionary<string, AssetReference>();
            public ObjectState(UnityEngine.Object obj)
            {
                asset = new AssetReference(obj);
                json = EditorJsonUtility.ToJson(obj);
                var serialized = new SerializedObject(obj);
                var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null)
                        references[property.propertyPath] = new AssetReference(property.objectReferenceValue);
            }
            public void Restore()
            {
                var value = asset.Resolve<UnityEngine.Object>();
                EditorJsonUtility.FromJsonOverwrite(json, value);
                // JSON object-reference instance IDs may also have expired during BuildPlayer cleanup.
                var serialized = new SerializedObject(value);
                foreach (var entry in references)
                {
                    var property = serialized.FindProperty(entry.Key);
                    Require(property != null, "XR restoration reference field is missing: " + entry.Key);
                    property.objectReferenceValue = entry.Value.Resolve<UnityEngine.Object>();
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(value);
            }
        }

        sealed class PluginState
        {
            public PluginImporter importer;
            public bool any, android, excludeAndroid;
            readonly string assetPath, metaPath, importerJson;
            readonly byte[] metaBytes;
            public PluginState(PluginImporter value)
            {
                importer = value;
                assetPath = value.assetPath;
                metaPath = PluginMetaPath(value);
                Require(File.Exists(metaPath), "Cannot snapshot plugin metadata: " + metaPath);
                metaBytes = File.ReadAllBytes(metaPath);
                importerJson = EditorJsonUtility.ToJson(value);
                any = value.GetCompatibleWithAnyPlatform();
                android = value.GetCompatibleWithPlatform(BuildTarget.Android);
                // Querying an unused exclusion can materialize a missing Any-platform settings block.
                excludeAndroid = any && value.GetExcludeFromAnyPlatform(BuildTarget.Android);
                LogState("snapshot");
            }
            public void Exclude()
            {
                // Explicit platform compatibility and Any Platform exclusions are separate inputs.
                // Clear the explicit flag first: that setter can override the Any Platform exclusion.
                importer.SetCompatibleWithPlatform(BuildTarget.Android, false);
                if (any) importer.SetExcludeFromAnyPlatform(BuildTarget.Android, true);
                importer.SaveAndReimport();
                Require(importer.GetCompatibleWithAnyPlatform() == any &&
                    !importer.GetCompatibleWithPlatform(BuildTarget.Android) &&
                    (!any || importer.GetExcludeFromAnyPlatform(BuildTarget.Android)),
                    "Could not exclude PICO Android plugin: " + importer.assetPath);
            }
            public void RestoreMemory()
            {
                importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                Require(importer != null, "Plugin importer disappeared before restoration: " + assetPath);
                LogState("before-memory-restore");
                // Synchronize the live importer first. A disk-only restore leaves the native importer
                // holding Quest's excluded state, which can be written back during a later refresh.
                EditorJsonUtility.FromJsonOverwrite(importerJson, importer);
                importer.SetCompatibleWithPlatform(BuildTarget.Android, android);
                if (any) importer.SetExcludeFromAnyPlatform(BuildTarget.Android, excludeAndroid);
                importer.SaveAndReimport();
                LogState("after-memory-restore");
            }
            public void RestoreMetadata()
            {
                // Preserve absent/default fields as well as explicit flags. Rewriting getters with setters
                // can serialize an implicit Android-compatible import as an explicit disabled platform.
                File.WriteAllBytes(metaPath, metaBytes);
                LogState("after-original-meta-write");
            }
            public void ValidateRestored()
            {
                // Called only after a full metadata scan. ImportAsset on the asset itself can retain
                // the old in-memory importer and write its excluded flags over the restored metadata.
                importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                LogState("after-meta-refresh");
                Require(importer != null && importer.GetCompatibleWithAnyPlatform() == any &&
                    importer.GetCompatibleWithPlatform(BuildTarget.Android) == android &&
                    (!any || importer.GetExcludeFromAnyPlatform(BuildTarget.Android) == excludeAndroid),
                    "Plugin metadata did not restore its original compatibility flags: " + assetPath);
                Require(metaBytes.SequenceEqual(File.ReadAllBytes(metaPath)),
                    "Plugin metadata bytes changed during restoration: " + assetPath);
            }
            void LogState(string stage)
            {
                string name = Path.GetFileName(assetPath);
                if (name != "PxrPlatform.aar" && name != "LoaderForUnitySDK.aar" && name != "StagePlatform.aar") return;
                string text = File.Exists(metaPath) ? File.ReadAllText(metaPath) : "";
                var match = System.Text.RegularExpressions.Regex.Match(text, @"(?m)^    Android:\s*\r?\n      enabled:\s*([01])");
                string metaAndroid = match.Success ? match.Groups[1].Value : "implicit-or-other-format";
                Debug.Log("[QuestBuild] plugin-restore stage=" + stage + "; path=" + assetPath +
                    "; metaAndroid=" + metaAndroid + "; getterAndroid=" + (importer != null ? importer.GetCompatibleWithPlatform(BuildTarget.Android).ToString() : "missing") +
                    "; any=" + (importer != null && importer.GetCompatibleWithAnyPlatform()) +
                    "; excludeAndroid=" + (importer != null && importer.GetCompatibleWithAnyPlatform() ? importer.GetExcludeFromAnyPlatform(BuildTarget.Android).ToString() : "not-applicable") +
                    "; actuallyEligible=" + PicoAndroidPluginPaths().Contains(assetPath) +
                    "; snapshotAndroid=" + android + "; snapshotAny=" + any + "; snapshotExcludeAndroid=" + excludeAndroid +
                    "; snapshotJsonLength=" + importerJson.Length);
            }
        }

        sealed class ProfileScope : IDisposable
        {
            readonly List<ObjectState> objects = new List<ObjectState>();
            readonly List<PluginState> plugins = new List<PluginState>();
            readonly AssetReference generalAsset, managerAsset;
            readonly List<AssetReference> loaderAssets;
            readonly AssetReference[] preloadedAssets;
            readonly bool initOnStart;
            readonly string appId, bootSettings;
            readonly AndroidSdkVersions minSdk, targetSdk;
            readonly AndroidArchitecture architecture;
            readonly ScriptingImplementation backend;
            readonly GraphicsDeviceType[] graphics;
            readonly bool automaticGraphics, appBundle;
            readonly ColorSpace colorSpace;
            readonly AndroidApplicationEntry entry;
            readonly UIOrientation orientation;
            readonly string[] picoAndroidPlugins;
            bool disposed;

            public ProfileScope()
            {
                var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
                var manager = general != null ? general.AssignedSettings : null;
                Require(manager != null, "Existing Android XR Manager settings are required.");
                generalAsset = new AssetReference(general);
                managerAsset = new AssetReference(manager);
                loaderAssets = manager.activeLoaders.Select(loader => new AssetReference(loader)).ToList();
                preloadedAssets = PlayerSettings.GetPreloadedAssets().Select(asset => asset != null ? new AssetReference(asset) : null).ToArray();
                initOnStart = general.InitManagerOnStart;
                appId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
                minSdk = PlayerSettings.Android.minSdkVersion;
                targetSdk = PlayerSettings.Android.targetSdkVersion;
                architecture = PlayerSettings.Android.targetArchitectures;
                backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
                graphics = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
                automaticGraphics = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android);
                colorSpace = PlayerSettings.colorSpace;
                appBundle = EditorUserBuildSettings.buildAppBundle;
                entry = PlayerSettings.Android.applicationEntry;
                orientation = PlayerSettings.defaultInterfaceOrientation;
                bootSettings = EditorUserBuildSettings.GetPlatformSettings("Android", "xr-boot-settings");
                picoAndroidPlugins = PicoAndroidPluginPaths();
                // Capture every file before any PlayerSettings setter can trigger a refresh/import.
                foreach (var plugin in PluginImporter.GetAllImporters().Where(IsPicoBinary))
                    plugins.Add(new PluginState(plugin));
            }

            public void Apply()
            {
                var general = generalAsset.Resolve<XRGeneralSettings>();
                var manager = managerAsset.Resolve<XRManagerSettings>();
                // Public package APIs create missing settings/features without toggling packages or defines.
                FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
                Require(settings != null, "OpenXR Android settings could not be created.");
                objects.Add(new ObjectState(settings));
                var features = settings.GetFeatures<OpenXRFeature>();
                foreach (var feature in features) if (feature != null) objects.Add(new ObjectState(feature));
                foreach (var feature in features) if (feature != null) feature.enabled = false;
                foreach (string id in RequiredFeatures)
                {
                    var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, id);
                    Require(feature != null, "Installed packages do not expose required Quest feature: " + id);
                    feature.enabled = true;
                    EditorUtility.SetDirty(feature);
                }
                // OpenXR's deprecated Microsoft profile shares this feature ID with XR Hands.
                // Resolve the subsystem by type: ID lookup returns whichever feature is first.
                var handTracking = settings.GetFeature<XRHandsTracking>();
                Require(handTracking != null, "The installed XR Hands package does not expose its HandTracking feature.");
                handTracking.enabled = true;
                EditorUtility.SetDirty(handTracking);
                var quest = settings.GetFeature<MetaQuestFeature>();
                // Public device registration preserves future devices; configure this build for Quest 3 only.
                quest.AddTargetDevice("eureka", "Quest 3", true);
                var questSerialized = new SerializedObject(quest);
                var devices = questSerialized.FindProperty("targetDevices");
                Require(devices != null && devices.isArray, "Meta Quest 1.16 target-device schema changed.");
                for (int i = 0; i < devices.arraySize; i++)
                {
                    var item = devices.GetArrayElementAtIndex(i);
                    item.FindPropertyRelative("enabled").boolValue = item.FindPropertyRelative("manifestName").stringValue == "eureka";
                }
                questSerialized.ApplyModifiedPropertiesWithoutUndo();
                quest.ForceRemoveInternetPermission = false; // Private leaderboard still needs HTTPS.
                settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                settings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;
                settings.autoColorSubmissionMode = true;
                EditorUtility.SetDirty(settings);

                Require(XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, BuildTargetGroup.Android),
                    "Could not register Unity OpenXR loader.");
                var openxr = manager.activeLoaders.FirstOrDefault(loader => loader is OpenXRLoader);
                Require(openxr != null && manager.TrySetLoaders(new List<XRLoader> { openxr }), "Could not make OpenXR the sole Android loader.");
                general.InitManagerOnStart = true;
                EditorUtility.SetDirty(manager);
                EditorUtility.SetDirty(general);
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
                PlayerSettings.colorSpace = ColorSpace.Linear;
                EditorUserBuildSettings.buildAppBundle = false;
                PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
                // Include Any Platform plugins even when their explicit Android flag is false.
                foreach (var snapshot in plugins) snapshot.Exclude();
                AssetDatabase.SaveAssets();
                ValidatePrepared();
                Debug.Log("[QuestBuild] prepared OpenXR1.16.1 / Meta2.4.1; package=" + PackageId +
                    "; PICO Android binaries excluded=" + plugins.Count + "; minSDK32 targetSDK35 GLES3 ARM64; source scene unchanged.");
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                var errors = new List<Exception>();
                void Restore(Action action) { try { action(); } catch (Exception e) { errors.Add(e); } }
                Restore(() =>
                {
                    var general = generalAsset.Resolve<XRGeneralSettings>();
                    var manager = managerAsset.Resolve<XRManagerSettings>();
                    general.AssignedSettings = manager;
                    var restoredLoaders = loaderAssets.Select(asset => asset.Resolve<XRLoader>()).ToList();
                    if (!manager.TrySetLoaders(restoredLoaders))
                    {
                        foreach (var asset in loaderAssets)
                            Require(XRPackageMetadataStore.AssignLoader(manager, asset.typeName, BuildTargetGroup.Android),
                                "Failed to register original Android loader: " + asset.typeName);
                        restoredLoaders = loaderAssets.Select(asset => asset.Resolve<XRLoader>()).ToList();
                        Require(manager.TrySetLoaders(restoredLoaders), "Failed to restore reloaded Android loader assets.");
                    }
                    general.InitManagerOnStart = initOnStart;
                    EditorUtility.SetDirty(manager);
                    EditorUtility.SetDirty(general);
                });
                // Restore all serialized feature properties without firing feature-enable callbacks mid-restore.
                foreach (var obj in objects) Restore(obj.Restore);
                Restore(() => PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, appId));
                Restore(() => { PlayerSettings.Android.minSdkVersion = minSdk; PlayerSettings.Android.targetSdkVersion = targetSdk; PlayerSettings.Android.targetArchitectures = architecture; });
                Restore(() => PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, backend));
                Restore(() => { PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, graphics); PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, automaticGraphics); PlayerSettings.colorSpace = colorSpace; EditorUserBuildSettings.buildAppBundle = appBundle; });
                Restore(() => { PlayerSettings.Android.applicationEntry = entry; PlayerSettings.defaultInterfaceOrientation = orientation; });
                Restore(() => EditorUserBuildSettings.SetPlatformSettings("Android", "xr-boot-settings", bootSettings));
                Restore(() => PlayerSettings.SetPreloadedAssets(preloadedAssets.Select(asset => asset?.Resolve<UnityEngine.Object>()).ToArray()));
                foreach (var plugin in plugins) Restore(plugin.RestoreMemory);
                Restore(AssetDatabase.SaveAssets);
                // Finish setters/saves before restoring metadata. Refresh scans all changed .meta files
                // before reloading their importers; per-asset ImportAsset is not equivalent to that scan.
                foreach (var plugin in plugins) Restore(plugin.RestoreMetadata);
                Restore(() => AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport));
                foreach (var plugin in plugins) Restore(plugin.ValidateRestored);
                Restore(() => Require(picoAndroidPlugins.SequenceEqual(PicoAndroidPluginPaths()),
                    "PICO Android plugin eligibility changed during restoration. Before: " + string.Join(",", picoAndroidPlugins) +
                    "; after: " + string.Join(",", PicoAndroidPluginPaths())));
                Active = false;
                if (errors.Count != 0) throw new AggregateException("Quest build finished, but profile restoration failed. Resolve before any PICO build.", errors);
                Debug.Log("[QuestBuild] restored original Android loaders, features, player settings and PICO plugin import compatibility.");
            }
        }
    }

    // BuildPipeline invokes this on the temporary scene copy only. Never save the source scene.
    public sealed class QuestSceneProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 10000;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (!QuestBuild.Active || report == null || report.summary.platform != BuildTarget.Android) return;
            PrepareSceneCopy(scene);
        }

        internal static void PrepareSceneCopy(Scene scene)
        {
            var origins = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<XROrigin>(true)).ToArray();
            QuestBuild.Require(origins.Length == 1 && origins[0].enabled && origins[0].Camera != null,
                "Quest scene must preserve exactly one enabled XR Origin with its camera.");
            QuestBuild.Require(origins[0].Camera.GetComponent<TrackedPoseDriver>() != null,
                "Quest camera requires the existing Input System Tracked Pose Driver.");
            var sessions = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ARSession>(true)).ToArray();
            QuestBuild.Require(sessions.Length <= 1, "Quest scene cannot contain multiple AR Sessions.");
            if (sessions.Length == 0)
            {
                var host = new GameObject("Quest AR Session");
                SceneManager.MoveGameObjectToScene(host, scene);
                var session = host.AddComponent<ARSession>();
                session.matchFrameRateRequested = false;
                session.attemptUpdate = false;
            }
            int removed = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component != null && component.GetType().FullName == "ByteDance.PICO.XR.PXR_Manager")
                    { UnityEngine.Object.DestroyImmediate(component); removed++; }
            Debug.Log("[QuestBuild] removed PXR_Manager from build scene copy: " + removed);
        }
    }

    public sealed class QuestBuildGuard : IPreprocessBuildWithReport, IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (QuestBuild.Active) QuestBuild.ValidatePrepared();
        }
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (!QuestBuild.Active) return;
            string manifestPath = Path.Combine(path, "src/main/AndroidManifest.xml");
            string manifest = File.ReadAllText(manifestPath);
            QuestBuild.Require(!manifest.Contains("pvr.app.type") && !manifest.Contains("pxr.sdk.generation"),
                "PICO manifest metadata leaked into Quest Gradle project.");
            // XR Management 4.6 emits application metadata in a separate merged Android library.
            string xrManifestPath = Path.Combine(path, "xrmanifest.androidlib/AndroidManifest.xml");
            string xrManifest = File.Exists(xrManifestPath) ? File.ReadAllText(xrManifestPath) : "";
            QuestBuild.Require(xrManifest.Contains("com.oculus.supportedDevices") && xrManifest.Contains("eureka"),
                "Meta Quest manifest requirements were not applied to xrmanifest.androidlib.");
            QuestBuild.Require(!xrManifest.Contains("pvr.app.type") && !xrManifest.Contains("pxr.sdk.generation"),
                "PICO metadata leaked into the merged XR manifest library.");
            // Final merged APK manifest is checked separately after BuildPlayer, not inferred from this intermediate file.
            Debug.Log("[QuestBuild] generated Gradle manifest has Quest device metadata and no PICO runtime metadata: " + manifestPath);
        }
    }
}

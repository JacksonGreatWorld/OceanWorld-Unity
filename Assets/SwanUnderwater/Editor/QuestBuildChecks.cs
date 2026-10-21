using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;
using XRHandsTracking = UnityEngine.XR.Hands.OpenXR.HandTracking;

namespace SwanUnderwater.Editor
{
    /// <summary>Edit-mode transaction / actual source-scene-copy checks. Not a device or performance test.</summary>
    public static class QuestBuildChecks
    {
        [Serializable] sealed class Report
        {
            public bool success, settingsRestored, sourceSceneUnchanged;
            public int assertions, picoPluginsExcluded, sourceOrigins, questSessions;
            public int picoAndroidEligibleBefore, picoEligibleWithoutExplicitAndroid;
            public string graphicsBefore, graphicsAfter, restorationCheckError;
            public bool graphicsRestored;
            public string[] picoAndroidBefore, picoAndroidAfter, picoAndroidMissing, picoAndroidAdded;
            public bool picoAndroidEligibilityRestored, pluginMetadataRestored, temporarySceneDeleted;
            public bool originalLoaderReferencesUnloaded;
            public string[] originalLoaderAssetPaths;
            public string handTrackingFeatureType;
            public bool microsoftHandInteractionEnabled;
            public string evidence = "Unity Editor configuration and disposable scene-copy validation only; no Quest runtime or passthrough visibility claim.";
        }
        static int assertions;
        static void Check(bool value, string description) { assertions++; QuestBuild.Require(value, description); }

        public static void RunAll()
        {
            assertions = 0;
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            string before = State();
            byte[] source = File.ReadAllBytes("Assets/Scenes/UnderwaterWorld.unity");
            var report = new Report();
            report.graphicsBefore = GraphicsState();
            report.originalLoaderAssetPaths = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android)
                .AssignedSettings.activeLoaders.Select(loader => AssetDatabase.GetAssetPath(loader)).ToArray();
            var androidPico = PluginImporter.GetImporters(BuildTarget.Android).Where(QuestBuild.IsPicoBinary).ToArray();
            report.picoAndroidEligibleBefore = androidPico.Length;
            report.picoEligibleWithoutExplicitAndroid = androidPico.Count(plugin => !plugin.GetCompatibleWithPlatform(BuildTarget.Android));
            report.picoAndroidBefore = QuestBuild.PicoAndroidPluginPaths();
            var pluginMetadata = PluginImporter.GetAllImporters().Where(QuestBuild.IsPicoBinary)
                .ToDictionary(plugin => QuestBuild.PluginMetaPath(plugin), plugin => File.ReadAllBytes(QuestBuild.PluginMetaPath(plugin)));
            Scene previous = SceneManager.GetActiveScene();
            Scene test = default;
            string testScenePath = "Assets/__QuestBuildValidation_" + Guid.NewGuid().ToString("N") + ".unity";
            try
            {
                using (QuestBuild.PrepareQuest3())
                {
                    QuestBuild.ValidatePrepared();
                    Check(QuestBuild.Active, "Quest transaction is active.");
                    var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
                    var handTracking = settings.GetFeature<XRHandsTracking>();
                    report.handTrackingFeatureType = handTracking != null ? handTracking.GetType().FullName : "missing";
                    report.microsoftHandInteractionEnabled = settings.GetFeatures<OpenXRFeature>().Any(feature =>
                        feature != null && feature.enabled && feature.GetType().FullName == "UnityEngine.XR.OpenXR.Features.Interactions.MicrosoftHandInteraction");
                    Check(handTracking != null && handTracking.enabled, "The actual XR Hands subsystem feature is enabled by type.");
                    Check(!report.microsoftHandInteractionEnabled, "The duplicate-ID Microsoft Hand Interaction profile remains disabled.");
                    report.picoPluginsExcluded = PluginImporter.GetAllImporters().Count(QuestBuild.IsPicoBinary);
                    Check(report.picoPluginsExcluded > 0, "PICO plugins were discovered, not silently missed by path.");
                    Check(!PluginImporter.GetImporters(BuildTarget.Android).Any(QuestBuild.IsPicoBinary), "No PICO native importer targets Android during Quest transaction.");
                    Check(PluginImporter.GetAllImporters().Where(QuestBuild.IsPicoBinary).All(plugin =>
                        !plugin.GetCompatibleWithPlatform(BuildTarget.Android) &&
                        (!plugin.GetCompatibleWithAnyPlatform() || plugin.GetExcludeFromAnyPlatform(BuildTarget.Android))),
                        "All PICO binaries exclude Android, including Any Platform defaults.");
                    Check(AssetDatabase.CopyAsset("Assets/Scenes/UnderwaterWorld.unity", testScenePath), "Created a disposable serialized scene copy.");
                    test = EditorSceneManager.OpenScene(testScenePath, OpenSceneMode.Additive);
                    SceneManager.SetActiveScene(test);
                    // Copy the whole serialized scene to preserve cross-root references. Never replace,
                    // save or close the user's original scene, including an untitled unsaved scene.
                    var origin = test.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<XROrigin>(true)).Single();
                    Transform parent = origin.transform.parent;
                    Camera camera = origin.Camera;
                    Transform cameraParent = camera.transform.parent;
                    Vector3 cameraPosition = camera.transform.localPosition;
                    Quaternion cameraRotation = camera.transform.localRotation;
                    QuestSceneProcessor.PrepareSceneCopy(test);
                    report.sourceOrigins = test.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<XROrigin>(true)).Count();
                    report.questSessions = test.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<ARSession>(true)).Count();
                    Check(report.sourceOrigins == 1 && report.questSessions == 1, "One XR origin / one AR session in Quest copy.");
                    Check(origin.Camera == camera && origin.transform.parent == parent && camera.transform.parent == cameraParent,
                        "Quest does not reparent or replace the XR camera.");
                    Check(camera.transform.localPosition == cameraPosition && camera.transform.localRotation == cameraRotation,
                        "Quest does not alter head pose.");
                    Check(!test.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<MonoBehaviour>(true))
                        .Any(component => component != null && component.GetType().FullName == "ByteDance.PICO.XR.PXR_Manager"),
                        "Quest scene copy has no PXR_Manager.");
                    EditorSceneManager.CloseScene(test, true);
                    test = default;
                }
                report.settingsRestored = State() == before;
                Check(report.settingsRestored, "All captured settings and native compatibility restored after normal disposal.");
                try
                {
                    using (QuestBuild.PrepareQuest3())
                    {
                        // BuildPlayer unloads unused assets after removing PICO from activeLoaders.
                        // Invalidate these old native references before testing failure restoration.
                        foreach (string path in report.originalLoaderAssetPaths)
                        {
                            var previousLoader = AssetDatabase.LoadAssetAtPath<XRLoader>(path);
                            Check(previousLoader != null, "Original loader asset exists before simulated build cleanup.");
                            Resources.UnloadAsset(previousLoader);
                            Check(previousLoader == null, "Old native loader reference was invalidated by asset unload.");
                        }
                        report.originalLoaderReferencesUnloaded = true;
                        throw new TestFailure();
                    }
                }
                catch (TestFailure) { }
                Check(State() == before && !QuestBuild.Active, "All settings restored after loader unload and injected build failure.");
                Check(report.picoAndroidBefore.SequenceEqual(QuestBuild.PicoAndroidPluginPaths()), "Actual Android PICO plugin list restored, including implicit imports.");
                Check(pluginMetadata.All(pair => File.Exists(pair.Key) && pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))),
                    "All PICO native metadata bytes restored after normal and failure transactions.");
                Check(GraphicsState() == report.graphicsBefore, "Graphics API list, automatic mode and color space restored.");
                report.sourceSceneUnchanged = source.SequenceEqual(File.ReadAllBytes("Assets/Scenes/UnderwaterWorld.unity"));
                Check(report.sourceSceneUnchanged, "Source scene bytes remain unchanged.");
                report.success = true;
            }
            finally
            {
                if (test.IsValid()) EditorSceneManager.CloseScene(test, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (File.Exists(testScenePath)) AssetDatabase.DeleteAsset(testScenePath);
                report.temporarySceneDeleted = !File.Exists(testScenePath) && !File.Exists(testScenePath + ".meta");
                assertions++;
                if (!report.temporarySceneDeleted) report.success = false;
                // PrepareQuest3 can fail before the first test assertion. Still report the actual
                // finally-restored state rather than leaving false defaults that imply a restore failure.
                try
                {
                    report.settingsRestored = State() == before && !QuestBuild.Active;
                    report.graphicsAfter = GraphicsState();
                    report.graphicsRestored = report.graphicsAfter == report.graphicsBefore;
                    report.picoAndroidAfter = QuestBuild.PicoAndroidPluginPaths();
                    report.picoAndroidMissing = report.picoAndroidBefore.Except(report.picoAndroidAfter).ToArray();
                    report.picoAndroidAdded = report.picoAndroidAfter.Except(report.picoAndroidBefore).ToArray();
                    report.picoAndroidEligibilityRestored = report.picoAndroidBefore.SequenceEqual(report.picoAndroidAfter);
                    report.pluginMetadataRestored = pluginMetadata.All(pair => File.Exists(pair.Key) && pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)));
                    report.sourceSceneUnchanged = source.SequenceEqual(File.ReadAllBytes("Assets/Scenes/UnderwaterWorld.unity"));
                }
                catch (Exception error) { report.restorationCheckError = error.ToString(); }
                report.assertions = assertions;
                string dir = Argument("-codexValidationRoot", "Validation/iteration-28/quest-build-checks");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "quest-profile-checks.json"), JsonUtility.ToJson(report, true));
            }
            QuestBuild.Require(report.temporarySceneDeleted, "Temporary validation scene and metadata were not deleted.");
            Debug.Log("QUEST_BUILD_CHECKS_PASSED: " + assertions + " assertions; original PICO profile restored.");
        }

        sealed class TestFailure : Exception { }
        static string GraphicsState() => string.Join("|", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)) +
            ";automatic=" + PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android) + ";colorSpace=" + PlayerSettings.colorSpace;
        static string State()
        {
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            var items = new List<string>
            {
                string.Join("|", general.AssignedSettings.activeLoaders.Select(loader => loader.GetType().FullName)),
                general.InitManagerOnStart.ToString(),
                PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
                PlayerSettings.Android.minSdkVersion.ToString(), PlayerSettings.Android.targetSdkVersion.ToString(),
                PlayerSettings.Android.targetArchitectures.ToString(), PlayerSettings.Android.applicationEntry.ToString(),
                PlayerSettings.defaultInterfaceOrientation.ToString(),
                PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android).ToString(),
                PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android).ToString(),
                string.Join("|", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)), PlayerSettings.colorSpace.ToString(),
                EditorUserBuildSettings.buildAppBundle.ToString(),
                EditorUserBuildSettings.GetPlatformSettings("Android", "xr-boot-settings")
            };
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            items.Add(EditorJsonUtility.ToJson(settings));
            foreach (var feature in settings.GetFeatures<OpenXRFeature>().OrderBy(feature => feature.GetType().FullName))
                items.Add(EditorJsonUtility.ToJson(feature));
            foreach (var plugin in PluginImporter.GetAllImporters().Where(QuestBuild.IsPicoBinary).OrderBy(plugin => plugin.assetPath))
            {
                bool any = plugin.GetCompatibleWithAnyPlatform();
                items.Add(plugin.assetPath + "|" + any + "|" + plugin.GetCompatibleWithPlatform(BuildTarget.Android) + "|" +
                    (any ? plugin.GetExcludeFromAnyPlatform(BuildTarget.Android).ToString() : "not-applicable"));
            }
            items.Add("Android eligible PICO plugins: " + string.Join("|", QuestBuild.PicoAndroidPluginPaths()));
            items.Add("Preloaded assets: " + string.Join("|", PlayerSettings.GetPreloadedAssets().Select(asset =>
            {
                if (asset == null) return "null";
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId);
                return guid + ":" + localId;
            })));
            return string.Join("\n", items);
        }
        static string Argument(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return fallback;
        }
    }
}

using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SwanUnderwater.Editor
{
    public static class UnderwaterBuild
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterWorld.unity";
        private const string ApkPath = "Builds/SwanUnderwaterWorld.apk";

        [MenuItem("Swan/Build Underwater World")]
        public static void BuildFromMenu()
        {
            BuildAndroid();
        }

        public static void CreateScene()
        {
            Scene template = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            GameObject oldWorld = GameObject.Find("Underwater World");
            if (oldWorld != null) Object.DestroyImmediate(oldWorld);

            GameObject world = new GameObject("Underwater World");
            world.AddComponent<UnderwaterWorld>();

            Light light = Object.FindFirstObjectByType<Light>();
            if (light != null)
            {
                light.name = "Filtered Sunlight";
                light.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 80f;
            }

            EditorSceneManager.SaveScene(template, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("UNDERWATER_SCENE_CREATED: " + ScenePath);
        }

        public static void BuildAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                throw new BuildFailedException("Unable to switch the active build target to Android.");
            }

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            CreateScene();
            PlayerSettings.companyName = "OceanWorld Community";
            PlayerSettings.productName = BrandingSetup.ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.example.oceanworld");
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.MTRendering = true;

            Directory.CreateDirectory("Builds");
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"Android build failed: {report.summary.result}, errors={report.summary.totalErrors}");

            Debug.Log($"UNDERWATER_APK_BUILT: {Path.GetFullPath(ApkPath)} bytes={report.summary.totalSize}");
        }
    }
}

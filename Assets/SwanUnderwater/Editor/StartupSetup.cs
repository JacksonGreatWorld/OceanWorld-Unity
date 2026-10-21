using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using ByteDance.PICO.XR;

namespace SwanUnderwater.Editor
{
    public static class StartupSetup
    {
        [MenuItem("Swan/Codex/Apply underwater entry presentation")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new BuildFailedException("Apply startup settings in Edit Mode.");
            var art = AssetDatabase.LoadAssetAtPath<Texture2D>(BrandingSetup.IconPath);
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/Loading/OceanLoadingFont.otf");
            if (art == null || font == null) throw new BuildFailedException("Missing original artwork or loading font.");
            const string path = "Assets/Resources/Loading/OceanEntryAssets.asset";
            var settings = AssetDatabase.LoadAssetAtPath<OceanEntryAssets>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<OceanEntryAssets>();
                AssetDatabase.CreateAsset(settings, path);
            }
            // Artwork remains the app icon; the runtime encounter uses a real 3D fish.
            settings.artwork = null;
            settings.font = font;
            EditorUtility.SetDirty(settings);
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.virtualRealitySplashScreen = null;
            var xr = PXR_ProjectSetting.GetProjectConfig();
            xr.videoSeeThrough = true;
            EditorUtility.SetDirty(xr);
            AssetDatabase.SaveAssets();
            if (PlayerSettings.SplashScreen.show || PlayerSettings.SplashScreen.showUnityLogo)
                throw new BuildFailedException("Unity splash/logo setting did not persist.");
            Directory.CreateDirectory("Validation/iteration-6");
            File.WriteAllText("Validation/iteration-6/startup-settings.txt",
                "Unity splash=false\nUnity logo=false\nVR splash=" + AssetDatabase.GetAssetPath(PlayerSettings.virtualRealitySplashScreen) +
                "\nRuntime poster=none\nVST=" + xr.videoSeeThrough + "\nFont=" + AssetDatabase.GetAssetPath(font) + "\n");
            Debug.Log("CODEX_STARTUP_SETTINGS: Unity splash/logo and poster disabled; VST enabled; blue-fish encounter and Chinese subtitles.");
        }
    }
}

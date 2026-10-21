using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif

namespace SwanUnderwater.Editor
{
    /// <summary>Assigns the user's unchanged source art to Unity's icon slots. No image generation,
    /// SDK export, package ID, signing, graphics API or runtime scene changes are performed.</summary>
    public static class BrandingSetup
    {
        public const string ProductName = "海底世界";
        public const string IconPath = "Assets/Branding/OceanWorldIcon.png";
        private const string SourceSha256 = "af012628286e6fa36a12a5d089fa1669f3f27239137a4fb028e3b41e8032daa9";

        // Run in a separate Android-target Editor with -batchmode -quit
        // -executeMethod SwanUnderwater.Editor.BrandingSetup.Apply. This does not build an APK.
        [MenuItem("Swan/Codex/Apply Ocean World name and original icon")]
        public static void Apply()
        {
#if !UNITY_ANDROID
            throw new BuildFailedException("BrandingSetup requires an Android Editor target; launch with -buildTarget Android.");
#else
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("Apply branding in Edit Mode.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new BuildFailedException("Launch the Editor with -buildTarget Android; no target switch is performed here.");
            if (!File.Exists(IconPath) || HashFile(IconPath) != SourceSha256)
                throw new BuildFailedException("The original user icon is missing or its bytes changed: " + IconPath);
            List<string> manifests = AuditCustomManifests();
            string applicationId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            string backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android).ToString();
            string graphics = string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android));
            var architectures = PlayerSettings.Android.targetArchitectures;
            bool customKeystore = PlayerSettings.Android.useCustomKeystore;
            string keystore = PlayerSettings.Android.keystoreName;
            string alias = PlayerSettings.Android.keyaliasName;

            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer == null) throw new BuildFailedException("Icon TextureImporter was not available.");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            // The project environment-texture postprocessor must skip Assets/Branding/.
            // This explicit Android import prevents an old per-platform compression override.
            var androidImport = importer.GetPlatformTextureSettings("Android");
            androidImport.name = "Android";
            androidImport.overridden = true;
            androidImport.maxTextureSize = 2048;
            androidImport.format = TextureImporterFormat.RGBA32;
            androidImport.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(androidImport);
            importer.SaveAndReimport();

            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null || icon.width != 1254 || icon.height != 1254 || importer.mipmapEnabled)
                throw new BuildFailedException("Icon import did not preserve 1254 x 1254 with mipmaps disabled; check texture postprocessors.");
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            var report = new BrandingReport
            {
                utc = DateTime.UtcNow.ToString("O"), productName = PlayerSettings.productName,
                iconAsset = IconPath, sourceSha256 = HashFile(IconPath), sourceWidth = icon.width, sourceHeight = icon.height,
                applicationIdentifier = applicationId, graphicsApis = graphics, scriptingBackend = backend,
                adaptivePolicy = "Every required adaptive layer explicitly references the same original opaque artwork. No split, padding, recolor or generated image. Android launcher masking and Unity icon-size export still apply.",
                customManifestsAudited = manifests.ToArray()
            };
            AssignAndVerify(AndroidPlatformIconKind.Legacy, "Legacy", icon, report);
            AssignAndVerify(AndroidPlatformIconKind.Round, "Round", icon, report);
            AssignAndVerify(AndroidPlatformIconKind.Adaptive, "Adaptive", icon, report);

            Texture2D[] defaults = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            if (defaults == null || defaults.Length == 0)
                throw new BuildFailedException("Default icon readback is empty.");
            foreach (Texture2D value in defaults)
                if (value != icon) throw new BuildFailedException("Default icon readback differs from the original source asset.");
            if (PlayerSettings.productName != ProductName || HashFile(IconPath) != SourceSha256)
                throw new BuildFailedException("Product name or original image changed during branding setup.");
            report.protectedSettingsUnchanged =
                PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) == applicationId &&
                PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android).ToString() == backend &&
                string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)) == graphics &&
                PlayerSettings.Android.targetArchitectures == architectures &&
                PlayerSettings.Android.useCustomKeystore == customKeystore &&
                PlayerSettings.Android.keystoreName == keystore && PlayerSettings.Android.keyaliasName == alias;
            if (!report.protectedSettingsUnchanged)
                throw new BuildFailedException("An unrelated Android identity/signing/render configuration changed while branding was applied.");
            AssetDatabase.SaveAssets();
            string output = Path.GetFullPath(Path.Combine("Validation", "branding", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "branding-settings.json"), JsonUtility.ToJson(report, true));
            Debug.Log("CODEX_BRANDING_APPLIED: " + ProductName + " | original sha256=" + SourceSha256 + " | settings readback=" + output);
#endif
        }

#if UNITY_ANDROID
        private static void AssignAndVerify(PlatformIconKind kind, string name, Texture2D original, BrandingReport report)
        {
            PlatformIcon[] slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            if (slots == null || slots.Length == 0) throw new BuildFailedException("No Android " + name + " icon slots are available.");
            foreach (PlatformIcon slot in slots)
            {
                // Legacy/round need one layer; adaptive needs foreground and background. Optional
                // layers stay empty instead of inventing monochrome or separated artwork.
                var textures = new Texture2D[slot.maxLayerCount];
                for (int layer = 0; layer < slot.minLayerCount; layer++) textures[layer] = original;
                slot.SetTextures(textures);
            }
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
            foreach (PlatformIcon slot in PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind))
            {
                if (slot.minLayerCount < 1 || (name == "Adaptive" && slot.minLayerCount < 2))
                    throw new BuildFailedException("Unexpected " + name + " icon layer contract.");
                for (int layer = 0; layer < slot.minLayerCount; layer++)
                    if (slot.GetTexture(layer) != original)
                        throw new BuildFailedException(name + " icon layer did not retain the original artwork.");
                report.androidSlots.Add(new SlotReport { kind = name, width = slot.width, height = slot.height,
                    requiredLayers = slot.minLayerCount, assignedAsset = AssetDatabase.GetAssetPath(slot.GetTexture(0)) });
            }
        }
#endif

        private static List<string> AuditCustomManifests()
        {
            var audited = new List<string>();
            XNamespace android = "http://schemas.android.com/apk/res/android";
            foreach (string path in Directory.GetFiles("Assets", "*Manifest.xml", SearchOption.AllDirectories))
            {
                string file = Path.GetFileName(path);
                if (file != "AndroidManifest.xml" && file != "LauncherManifest.xml") continue;
                audited.Add(path.Replace('\\', '/'));
                XDocument document = XDocument.Load(path);
                foreach (XElement element in document.Descendants())
                {
                    string tag = element.Name.LocalName;
                    if (tag != "application" && tag != "activity" && tag != "activity-alias") continue;
                    foreach (string attribute in new[] { "label", "icon", "roundIcon" })
                    {
                        string value = (string)element.Attribute(android + attribute);
                        if (value == null) continue;
                        bool expected = attribute == "label" ? value == "@string/app_name" || value == ProductName
                            : value == "@mipmap/app_icon" || value == "@drawable/app_icon";
                        if (!expected)
                            throw new BuildFailedException("Custom Android branding needs explicit review before Unity PlayerSettings can be authoritative: " + path + " " + tag + " android:" + attribute + "=" + value);
                    }
                }
            }
            return audited;
        }

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        [Serializable] private sealed class BrandingReport
        {
            public string utc, productName, iconAsset, sourceSha256, applicationIdentifier, graphicsApis, scriptingBackend, adaptivePolicy;
            public int sourceWidth, sourceHeight;
            public bool protectedSettingsUnchanged;
            public string[] customManifestsAudited;
            public List<SlotReport> androidSlots = new List<SlotReport>();
        }
        [Serializable] private sealed class SlotReport
        {
            public string kind, assignedAsset;
            public int width, height, requiredLayers;
        }
    }
}

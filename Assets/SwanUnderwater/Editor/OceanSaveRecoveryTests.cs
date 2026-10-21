#if UNITY_EDITOR || OCEAN_CAMPAIGN_STANDALONE
using System;
using System.IO;
using System.Reflection;
#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
#endif

namespace SwanUnderwater.EditorTests
{
    public static class OceanSaveRecoveryTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool condition, string message)
        { Assertions++; if (!condition) throw new InvalidOperationException("Read-only save retry: " + message); }
        public static void RunAll()
        {
            Assertions = 0;
            ExistingFilesOnly(); AuthenticatedRestoreKeepsSharedState();
#if UNITY_EDITOR
            ProductionMenuCallback();
            OceanSaveRecoverySceneTests.ProductionInFlightCallback();
            Assertions += OceanSaveRecoverySceneTests.Assertions;
            Debug.Log("OceanSaveRecoveryTests PASS " + Assertions + " assertions; existing-path authenticated reads, corruption/future protection and actual production menu callback; no device or overwrite recovery claim.");
#else
            Console.WriteLine("OceanSaveRecoveryTests PASS " + Assertions + " assertions; authenticated read-only filesystem recovery, no Unity/device claim.");
#endif
        }
        static void ExistingFilesOnly()
        {
            string root = Path.Combine(Path.GetTempPath(), "ocean-read-only-retry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var state = new OceanCampaignState(); state.StartLevel(3); state.CollectFish(0); state.Save.bestStars[19] = 3;
                Check(OceanSaveStore.TrySave(root, state.Save, out string error), "trusted baseline fixture: " + error);
                string primary = Path.Combine(root, OceanSaveStore.FileName), backup = Path.Combine(root, OceanSaveStore.BackupFileName);
                string trusted = File.ReadAllText(primary);
                File.WriteAllText(primary, "{broken-primary"); File.WriteAllText(backup, "{broken-backup");
                Check(!OceanSaveStore.TryReloadExistingReadOnly(root, out OceanSaveData loaded, out OceanSaveLoadStatus status, out error) && loaded == null && status == OceanSaveLoadStatus.Corrupt, "both corrupt reads remain an explicit failure");
                Check(File.ReadAllText(primary) == "{broken-primary" && File.ReadAllText(backup) == "{broken-backup", "failed retry leaves both corrupt originals byte-for-byte unchanged");
                // An external repair of the trusted backup permits reading, never rewriting the damaged primary.
                File.WriteAllText(backup, trusted);
                Check(OceanSaveStore.TryReloadExistingReadOnly(root, out loaded, out status, out error) && status == OceanSaveLoadStatus.RecoveredBackup && loaded.currentRun.fishMask == 1 && loaded.bestStars[19] == 3, "retry can read the authenticated existing backup without losing history");
                Check(File.ReadAllText(primary) == "{broken-primary" && File.ReadAllText(backup) == trusted, "manual backup read never repairs or overwrites either original");
                File.WriteAllText(primary, "{\"version\":99,\"future\":true}"); string future = File.ReadAllText(primary);
                Check(!OceanSaveStore.TryReloadExistingReadOnly(root, out loaded, out status, out error) && status == OceanSaveLoadStatus.UnsupportedVersion && loaded == null, "future primary never falls back to an older authenticated backup");
                Check(File.ReadAllText(primary) == future && File.ReadAllText(backup) == trusted, "future version and its backup remain untouched");
                string missing = Path.Combine(root, "missing-originals");
                Check(!OceanSaveStore.TryReloadExistingReadOnly(missing, out loaded, out status, out error) && loaded == null && status == OceanSaveLoadStatus.NewSave && !Directory.Exists(missing), "removed originals never silently become a new game or a new directory");
                string blocked = Path.Combine(root, "file-instead-of-directory"); File.WriteAllText(blocked, "keep");
                Check(!OceanSaveStore.TryReloadExistingReadOnly(blocked, out loaded, out status, out error) && status == OceanSaveLoadStatus.IoError && File.ReadAllText(blocked) == "keep", "I/O failure does not replace an inaccessible original");
                File.WriteAllText(primary, trusted);
                Check(OceanSaveStore.TryReloadExistingReadOnly(root, out loaded, out status, out error) && status == OceanSaveLoadStatus.Loaded && File.ReadAllText(primary) == trusted && File.ReadAllText(backup) == trusted, "externally restored primary is reread without a disk commit");
            }
            finally { Directory.Delete(root, true); }
        }
        static void Apply(OceanCampaignState state, OceanSaveData data) => typeof(OceanCampaignState)
            .GetMethod("ApplyVerifiedSave", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(state, new object[] { data });
        static void AuthenticatedRestoreKeepsSharedState()
        {
            var state = new OceanCampaignState(); state.StartLevel(1); state.CollectShell(1, 0);
            var restored = new OceanCampaignState(); restored.StartLevel(20); restored.Save.bestStars[0] = 3; restored.CollectFish(0); restored.CollectCoin(20004);
            OceanSaveData sameContainer = state.Save;
            Apply(state, restored.Save);
            Check(ReferenceEquals(state.Save, sameContainer) && state.Current.level == 20 && state.Save.bestStars[0] == 3 && state.Current.fishMask == 1 && state.Current.coinMask == 16, "authenticated adoption preserves the shared campaign owner and restores exact progress");
            restored.Current.fishMask = 0;
            Check(state.Current.fishMask == 1, "restored snapshot is not aliased to the reader's candidate");
            var before = state.Current;
            bool rejected = false;
            try { Apply(state, new OceanSaveData { version = 99 }); } catch (TargetInvocationException error) { rejected = error.InnerException is ArgumentException; }
            Check(rejected && ReferenceEquals(before, state.Current) && state.Save.bestStars[0] == 3, "an invalid candidate cannot partially replace current state");
            Check(OceanSaveRecoveryCopy.Describe(OceanSaveLoadStatus.RecoveredBackup, true).Contains("保持只读") && OceanSaveRecoveryCopy.Describe(OceanSaveLoadStatus.UnsupportedVersion, false).Contains("支持该存档的版本"), "success and future-schema instructions state the conservative recovery boundary");
        }
#if UNITY_EDITOR
        static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);
        static bool HasText(OceanCampaignPanel panel, string value)
        { foreach (var text in panel.GetComponentsInChildren<Text>()) if (text.gameObject.activeInHierarchy && text.text.Contains(value)) return true; return false; }
        static bool Visible(OceanCampaignPanel panel, string id)
        { foreach (var button in panel.GetButtonsForValidation()) if (button.id == id) return true; return false; }
        static void ProductionMenuCallback()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ocean-retry-menu-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var root = new GameObject("Isolated production save recovery UI");
            OceanCampaignController controller = null;
            try
            {
                var cameraObject = new GameObject("Read-only retry view"); cameraObject.transform.SetParent(root.transform);
                var view = cameraObject.AddComponent<Camera>(); view.enabled = false; view.transform.position = new Vector3(0, 1.6f, 0);
                var hands = root.AddComponent<HandSwimController>(); hands.enabled = false;
                var state = new OceanCampaignState(); state.StartLevel(3); state.CollectFish(0); state.Save.bestStars[0] = 3;
                Check(OceanSaveStore.TrySave(directory, state.Save, out string error), "menu fixture trustworthy backup: " + error);
                string primary = Path.Combine(directory, OceanSaveStore.FileName), backup = Path.Combine(directory, OceanSaveStore.BackupFileName), trusted = File.ReadAllText(backup);
                controller = root.AddComponent<OceanCampaignController>(); controller.enabled = false;
                var panel = root.AddComponent<OceanCampaignPanel>(); panel.Initialize(view, hands, state, controller.HandleAction, false);
                SetProperty(controller, "State", state); SetProperty(controller, "Panel", panel); SetProperty(controller, "SaveDirectory", directory);
                SetField(controller, "initialized", true); SetField(controller, "writable", false); SetField(controller, "warnedSave", true);
                panel.SetSaveRecoveryStatus(true, OceanSaveLoadStatus.IoError, false); panel.SetAvailable(true); panel.OpenMenu();
                Check(Visible(panel, "save_recovery") && panel.ClickForValidation("save_recovery") && Visible(panel, "retry_save_load"), "read-only top row opens actual recovery page and retry control");
                Check(panel.ClickForValidation("save_recovery_targets") && panel.PageNameForDiagnostics == "Targets", "recovery page preserves access to real current goals");
                panel.OpenMenu(); Check(panel.ClickForValidation("save_recovery"), "recovery remains reachable after viewing goals");
                File.WriteAllText(primary, "bad-primary"); File.WriteAllText(backup, "bad-backup");
                var originalRun = state.Current;
                Check(panel.ClickForValidation("retry_save_load") && HasText(panel, "无法通过检查"), "actual menu dispatch reaches controller and reports authenticated corrupt failure");
                Check(ReferenceEquals(originalRun, controller.State.Current) && controller.State.Current.fishMask == 1 && controller.State.Save.bestStars[0] == 3, "failed production callback does not reset or replace volatile progress");
                Check(File.ReadAllText(primary) == "bad-primary" && File.ReadAllText(backup) == "bad-backup", "failed production callback creates no replacement save");
                File.WriteAllText(primary, "{\"version\":99,\"future\":true}"); File.WriteAllText(backup, trusted); string future = File.ReadAllText(primary);
                Check(panel.ClickForValidation("retry_save_load") && HasText(panel, "存档版本暂不支持"), "production retry reports future schema instead of rolling back to trusted older backup");
                controller.SaveNow();
                Check(ReferenceEquals(originalRun, controller.State.Current) && !controller.LastSaveSucceeded && File.ReadAllText(primary) == future && File.ReadAllText(backup) == trusted, "manual retry and later save callback remain read-only");
                foreach (var label in panel.GetComponentsInChildren<Text>())
                    if (label.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(label.text))
                    {
                        Check(label.preferredHeight <= label.rectTransform.rect.height + .5f, "recovery copy fits actual production UGUI box " + label.name);
                        foreach (char c in label.text) if (!char.IsWhiteSpace(c)) Check(label.font.HasCharacter(c), "recovery uses an admitted font glyph " + c);
                    }
                SetField(controller, "writable", true); panel.SetSaveRecoveryStatus(false, OceanSaveLoadStatus.Loaded, true); panel.OpenMenu();
                Check(Visible(panel, "show_targets") && !Visible(panel, "save_recovery") && !Visible(panel, "retry_save_load"), "normal loaded game retains its original menu with no recovery operation");
                controller.HandleAction("retry_save_load", 0);
                Check(File.ReadAllText(primary) == future && File.ReadAllText(backup) == trusted, "normal menu cannot invoke a hidden downgrade or repair action");
            }
            finally
            {
                if (controller != null) SetField(controller, "initialized", false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root);
                Directory.Delete(directory, true);
            }
        }
#endif
    }
}
#endif

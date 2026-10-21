using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater
{
    public sealed partial class OceanCampaignPanel
    {
        bool saveRecoveryReadOnly, saveRecoverySucceeded;
        OceanSaveLoadStatus saveRecoveryStatus;
        string saveRecoveryNotice;
        Text saveRecoveryBody;

        void BuildSaveRecovery()
        {
            Transform p = PageRoot(Page.SaveRecovery);
            saveRecoveryBody = Label(p, "Verified save recovery explanation", "", 20, new Vector2(0, 62), new Vector2(650, 166));
            Button(p, "retry_save_load", "重新读取", new Vector2(0, -67), new Vector2(620, 54), () => Dispatch("retry_save_load", 0), true);
            Button(p, "save_recovery_targets", "本关目标", new Vector2(-163, -143), new Vector2(298, 54), () => ShowPage(Page.Targets));
            Button(p, "save_recovery_menu", "返回菜单", new Vector2(163, -143), new Vector2(298, 54), () => ShowPage(Page.Menu));
            Label(p, "Read-only retry scope", OceanSaveRecoveryCopy.ReplacesUnsaved, 17, new Vector2(0, -222), new Vector2(680, 56)).color = Muted;
        }
        public void SetSaveRecoveryStatus(bool readOnly, OceanSaveLoadStatus status, bool readSucceeded, string notice = null)
        {
            saveRecoveryReadOnly = readOnly; saveRecoveryStatus = status; saveRecoverySucceeded = readSucceeded;
            saveRecoveryNotice = notice;
            if (initialized) Refresh();
        }
        void RefreshSaveRecovery()
        {
            if (saveRecoveryBody == null) return;
            saveRecoveryBody.text = saveRecoveryNotice ?? OceanSaveRecoveryCopy.Describe(saveRecoveryStatus, saveRecoverySucceeded);
            targetsButton.id = saveRecoveryReadOnly ? "save_recovery" : "show_targets";
            if (!saveRecoveryReadOnly) return;
            SetEnabled(targetsButton, true);
            menuProgress.text = saveRecoverySucceeded ? "存档已重新读取 · 本次保持只读\n查看读取结果与本关目标" : "存档暂时无法读取 · 重新读取\n本次只读，原文件保留";
        }
        void OpenTargetsOrSaveRecovery() => ShowPage(saveRecoveryReadOnly ? Page.SaveRecovery : Page.Targets);
        internal void SuspendPracticeForSaveReload() => tutorial?.Skip(false);
        public void ShowSaveRecovery()
        {
            if (!initialized || !saveRecoveryReadOnly) return;
            if (!IsOpen) OpenMenu();
            ShowPage(Page.SaveRecovery);
        }
    }
}

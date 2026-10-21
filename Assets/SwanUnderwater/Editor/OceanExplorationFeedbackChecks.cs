#if UNITY_EDITOR
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    public static class OceanExplorationFeedbackChecks
    {
        public static void RunAll()
        {
            var host = new GameObject("Isolated exploration feedback checks");
            var cameraHost = new GameObject("Isolated feedback camera");
            try
            {
                var camera = cameraHost.AddComponent<Camera>(); camera.enabled = false;
                var feedback = host.AddComponent<TreasureFeedback>();
                feedback.Initialize(camera); feedback.SetVisible(true);
                Check(feedback.TryShowExplorationMessage("发现 · 晨光礁拱"), "idle presentation accepts an exploration caption");
                Check(!feedback.TryShowExplorationMessage("发现 · 摇曳草海"), "a second area cannot queue behind the first");
                feedback.ShowCampaignMessage("收集到：扇贝", pickup: true);
                Check(feedback.CurrentNotification == "收集到：扇贝", "a real collection immediately replaces exploration text");
                Check(!feedback.TryShowExplorationMessage("发现 · 摇曳草海"), "exploration cannot replace a collection");
                feedback.SetVisible(false); feedback.SetVisible(true);
                Check(feedback.TryShowExplorationMessage("发现 · 海扇峡谷"), "cleared presentation can show another area");
                feedback.ShowHiddenReward();
                Check(feedback.CurrentNotification == TreasureFeedback.HiddenRewardText && feedback.IsCelebrating,
                    "hidden coin immediately replaces exploration and retains its celebration");
                Check(!feedback.TryShowExplorationMessage("发现 · 层礁回廊"), "celebration never accepts exploration");
                feedback.SetVisible(false);
                Check(feedback.CurrentNotification == "" && !feedback.CanShowExplorationMessage,
                    "hidden/disabled presentation clears transient text");
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(cameraHost); }
            Debug.Log("OceanExplorationFeedbackChecks PASS 8 assertions; real UI ownership/priority only, visual/device review pending.");
        }
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Exploration feedback: " + message); }
    }
}
#endif

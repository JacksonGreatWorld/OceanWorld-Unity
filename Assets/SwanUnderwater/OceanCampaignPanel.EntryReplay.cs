using UnityEngine;

namespace SwanUnderwater
{
    public sealed partial class OceanCampaignPanel
    {
        OceanEntry replay;
        Page replayReturnPage;
        bool replayReturnAvailable;
        Target replayEntry;

        void BuildEntryReplay(Transform parent)
        {
            replayEntry = Button(parent, "entry_replay", "重看完整入场 · 进度保留", new Vector2(0, -176), new Vector2(650, 43), BeginEntryReplay);
        }

        void BeginEntryReplay()
        {
            if (!initialized || !available || disposed || !IsOpen || TutorialActive || replay != null || OceanEntry.IsLoading) return;
            replayReturnPage = page; replayReturnAvailable = available;
            // Preserve IsOpen, campaign run and tutorial qualification. No Dispatch/controller
            // callback is involved, so this cannot emit DemoOpened, scores or level events.
            SetAvailable(false); hands.SetMenuLocomotionPaused(true);
            try { replay = OceanEntry.ShowReplay(view, EntryReplayEnded); }
            catch (System.Exception exception)
            { EntryReplayEnded(false); Debug.LogWarning("[OceanEntry] replay_unavailable: " + exception.Message); }
        }

        void EntryReplayEnded(bool completed)
        {
            replay = null;
            if (!initialized || disposed || hands == null) return;
            SetAvailable(replayReturnAvailable);
            IsOpen = true; hands.SetMenuLocomotionPaused(true);
            AnchorPanel(); ShowPage(replayReturnPage); Refresh(); ResetInput();
            Debug.Log("[OceanEntry] replay_returned completed=" + completed + "; campaign_and_tutorial_untouched=true");
        }

        void RefreshEntryReplay()
        { SetEnabled(replayEntry, !TutorialActive && replay == null && !OceanEntry.IsLoading); }

        void CancelEntryReplay()
        {
            if (replay == null) return;
            OceanEntry pending = replay; replay = null;
            DestroyOwned(pending.gameObject);
        }
    }
}

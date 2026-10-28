using System;
using UnityEngine;

namespace SwanUnderwater
{
    public sealed partial class OceanCampaignPanel
    {
        GameObject tutorialExitRoot;
        Target tutorialExit;
        bool? teachingHeaderShown;
        float tutorialExitTextScale = -1f;

        void BuildTutorialExit()
        {
            // The teaching graphic remains noninteractive. This one reused target
            // belongs to the same ray/pinch/controller owner as the normal menu.
            tutorialExitRoot = MakeCanvas("Direct tutorial close control", new Vector2(230, 54));
            tutorialExitRoot.transform.SetParent(view.transform, false);
            tutorialExitRoot.transform.localPosition = new Vector3(0f, -.195f, .80f);
            tutorialExit = Button(tutorialExitRoot.transform, "tutorial_exit", "关闭教学", Vector2.zero,
                new Vector2(230, 54), CloseTutorialAndResume, true);
            SetTutorialExitVisible(false);
        }

        void RefreshTutorialExitPresentation()
        {
            if (tutorialExitRoot == null) return;
            float textScale = hands != null ? hands.Comfort.MenuScaleMultiplier : 1f;
            if (tutorialExitTextScale != textScale)
            {
                tutorialExitTextScale = textScale;
                tutorialExitRoot.transform.localScale = Vector3.one * .00085f * textScale;
            }
            bool teaching = TutorialActive;
            SetTutorialExitVisible(initialized && available && isActiveAndEnabled && !disposed &&
                teaching && !IsOpen && !IsTransitioning);
            if (close == null || teachingHeaderShown == teaching) return;
            teachingHeaderShown = teaching;
            Vector2 size = new Vector2(teaching ? 148f : 92f, 50f);
            close.rect.anchoredPosition = new Vector2(teaching ? 284f : 312f, 240f);
            close.rect.sizeDelta = size;
            close.border.rectTransform.sizeDelta = size - Vector2.one * 2f;
            close.label.rectTransform.sizeDelta = size - new Vector2(20f, 6f);
            close.label.text = L(teaching ? "关闭教学" : "收起");
            // A lesson can also end through its rule owner (including a repeat
            // beside an expired or archived run). Refresh the header's ordinary
            // visibility/enabled rules once, without refreshing model collections.
            if (initialized)
            {
                RefreshMenuProgress(state.Current != null && OceanCampaignAvailability.IsOpen(state.Current.level));
                RefreshHeader();
            }
            if (teaching) close.label.color = Gold;
        }

        void SetTutorialExitVisible(bool show)
        {
            if (tutorialExitRoot != null && tutorialExitRoot.activeSelf != show)
                tutorialExitRoot.SetActive(show);
        }

        void CloseHeader()
        {
            if (TutorialActive) CloseTutorialAndResume();
            else Dispatch("resume", 0);
        }

        void CloseTutorialAndResume()
        {
            if (!TutorialActive) return;
            tutorial.Skip();
            // Keep this press owned by the UI after its graphic disappears, so
            // it cannot also become a grasp in the newly resumed game world.
            worldInputUntil = Math.Max(worldInputUntil, Time.unscaledTimeAsDouble + .45);
            ResetInput();
            Dispatch("resume", 0);
        }

        void DisposeTutorialExit()
        {
            SetTutorialExitVisible(false);
            DestroyOwned(tutorialExitRoot);
            tutorialExitRoot = null;
            tutorialExit = null;
        }

#if UNITY_EDITOR
        public RectTransform TutorialExitRectForValidation => tutorialExit != null ? tutorialExit.rect : null;
        public bool TutorialExitVisibleForValidation => tutorialExitRoot != null && tutorialExitRoot.activeInHierarchy;
#endif
    }
}

using System;
using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Isolated, repeatable hands-first practice. It never gives campaign stars, discoveries, ammunition or score.</summary>
    [DefaultExecutionOrder(96)]
    public sealed class OceanInteractiveTutorial : MonoBehaviour
    {
        const string SeenKey = "ocean_interactive_tutorial_seen_v1";
        public readonly OceanInteractiveTutorialRules Rules = new OceanInteractiveTutorialRules();
        public bool Active => Rules.Active;
        string practiceHint = "";
        public string PracticeHint { get => OceanLanguage.Text(practiceHint); private set => practiceHint = value; }
        Camera view;
        HandSwimController hands;
        OceanCampaignPanel panel;
        Action changed;
        GameObject shell;
        Renderer shellRenderer;
        OceanTutorialVisual teachingVisual;
        bool available, automaticOfferChecked;
        double nextRefresh;
        OceanInteractiveTutorialRules.Step presentedStep;

        public static bool ShouldOfferAutomatically(bool loadSucceeded, OceanSaveLoadStatus status) =>
            loadSucceeded && status == OceanSaveLoadStatus.NewSave;

        public void Initialize(Camera camera, HandSwimController controller, OceanCampaignPanel owner, Action onChanged)
        { view = camera; hands = controller; panel = owner; changed = onChanged; }

        public void SetAvailable(bool value, bool newCampaign)
        {
            available = value;
            if (!value) teachingVisual?.SetVisible(false);
            if (value && !automaticOfferChecked)
            {
                automaticOfferChecked = true;
                if (newCampaign && PlayerPrefs.GetInt(SeenKey, 0) == 0) Begin();
            }
            RefreshPracticeVisibility();
        }

        public void Begin()
        {
            Rules.Start(hands.CompletedStrokeCount); PracticeHint = "";
            if (teachingVisual == null) teachingVisual = gameObject.AddComponent<OceanTutorialVisual>();
            teachingVisual.Begin(view,Resources.Load<Font>("Treasure/TreasureFont"));
            DestroyPractice(); panel.CloseMenu();
            OceanEntry.DismissPlayableTitles();
            Debug.Log("[OceanTutorial] begin campaign_untouched=true clock_paused=true");
            PresentStep();
        }

        public void Skip(bool persist = true)
        {
            if (!Active) return;
            Rules.Skip(); Finish(false, persist);
        }

        public void NotifyMenuOpened() { Rules.OpenMenu(); teachingVisual?.SetVisible(false); changed?.Invoke(); }
        public void NotifyTargetsViewed() { Rules.ViewTargets(); changed?.Invoke(); }
        public void NotifyResumed() { if (Rules.Resume()) Finish(true); }

        void LateUpdate()
        {
            if (hands == null || !Active) return;
            bool playable = available && !panel.IsOpen && !panel.WorldInputBlocked && hands.CanReadTrackedVisuals && !hands.InteractionSuppressed;
            bool open = hands.LeftGraspArmed || hands.RightGraspArmed;
            // Tracking invalidates dwell and stop evidence. Recognizer counters contain real hand strokes only.
            Rules.Observe(Time.unscaledDeltaTime, open, hands.CompletedStrokeCount, hands.CurrentSpeed, playable);
            teachingVisual?.Render(Rules.Current,hands.LeftGraspValidThisStep,hands.RightGraspValidThisStep,
                hands.LeftGraspArmed,hands.RightGraspArmed,Time.unscaledDeltaTime,playable);
            if (presentedStep != Rules.Current) PresentStep();
            RefreshPracticeVisibility();
            if (playable && Rules.Current == OceanInteractiveTutorialRules.Step.PracticeGrab && shell != null)
            {
                bool left = TryPracticeGrab(OceanHandSide.HandLeft);
                if (!left) TryPracticeGrab(OceanHandSide.HandRight);
                if (Rules.Current == OceanInteractiveTutorialRules.Step.PracticeGrab) UpdatePracticeHint();
            }
            if (Time.unscaledTimeAsDouble >= nextRefresh)
            { nextRefresh = Time.unscaledTimeAsDouble + .25; changed?.Invoke(); }
        }

        bool TryPracticeGrab(OceanHandSide side)
        {
            if (!hands.TryGetPalmPosition(side, out Vector3 palm)) return false;
            float distance = Vector3.Distance(palm, shellRenderer.bounds.ClosestPoint(palm));
            if (distance > .18f || !hands.TryGetWorldGrabIntent(side, out _) || !hands.ConsumeWorldGrabIntent(side)) return false;
            if (!Rules.PracticeGrabAccepted(true)) return false;
            PracticeHint = "练习扇贝已拾取 · 请打开金色菜单";
            DestroyPractice(); PresentStep(); return true;
        }

        void UpdatePracticeHint()
        {
            float nearest = float.PositiveInfinity; bool armed = false;
            for (int index = 0; index < 2; index++)
            {
                var side = index == 0 ? OceanHandSide.HandLeft : OceanHandSide.HandRight;
                if (!hands.TryGetPalmPosition(side, out Vector3 palm)) continue;
                float distance = Vector3.Distance(palm, shellRenderer.bounds.ClosestPoint(palm));
                if (distance >= nearest) continue;
                nearest = distance; armed = side == OceanHandSide.HandLeft ? hands.LeftGraspArmed : hands.RightGraspArmed;
            }
            PracticeHint = float.IsPositiveInfinity(nearest) ? "把手放到头显可见范围 · 可点关闭教学退出" : nearest > .18f ?
                "再靠近一点 · 练习扇贝不计入收藏" : !armed ? "先张开手，再握拳 · 练习扇贝不计入收藏" : "现在握拳，拾取练习扇贝";
        }

        void PresentStep()
        {
            presentedStep = Rules.Current;
            if (Rules.Current == OceanInteractiveTutorialRules.Step.PracticeGrab)
            {
                DestroyPractice(); shell = CollectibleVisuals.CreateShell(transform, 0);
                shell.name = "Tutorial practice scallop (no campaign reward)";
                Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < .5f) forward = Vector3.forward;
                // A fixed reachable chest-height target supports seated players without bending to the seabed.
                shell.transform.position = view.transform.position + forward * .43f - Vector3.up * .22f;
                shell.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                shellRenderer = shell.GetComponent<Renderer>();
                CollectibleVisuals.SetProximityGlow(shell, .8f);
            }
            Debug.Log("[OceanTutorial] step=" + Rules.Current + " campaign_untouched=true");
            changed?.Invoke();
        }

        void Finish(bool complete, bool persist = true)
        {
            DestroyPractice(); if (persist) { PlayerPrefs.SetInt(SeenKey, 1); PlayerPrefs.Save(); }
            teachingVisual?.Close();
            Debug.Log("[OceanTutorial] " + (complete ? "completed_real_actions" : "skipped") + " campaign_untouched=true");
            changed?.Invoke();
        }
        void RefreshPracticeVisibility()
        { if (shell != null) shell.SetActive(available && !panel.IsOpen && hands.CanReadTrackedVisuals); }
        void DestroyPractice()
        { if (shell != null) { shell.SetActive(false); Destroy(shell); } shell = null; shellRenderer = null; }
        void OnDisable() { if (shell != null) shell.SetActive(false); teachingVisual?.SetVisible(false); }
        void OnDestroy() { DestroyPractice(); teachingVisual?.Close(); }
    }
}

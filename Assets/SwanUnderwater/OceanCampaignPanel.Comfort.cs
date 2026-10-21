using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater
{
    public sealed partial class OceanCampaignPanel
    {
        OceanInteractiveTutorial tutorial;
        bool automaticTutorialAllowed;
        Target comfortSpeed, comfortVolume, comfortTurn, comfortSeated, tutorialSkip, comfortText;
        readonly Target[] audioCategoryTargets = new Target[3];
        static readonly string[] AudioCategoryNames = { "环境声音", "音乐", "反馈音效" };
        Text tutorialStatus;

        void BuildComfort()
        {
            Transform p = PageRoot(Page.Comfort);
            comfortSpeed = Button(p, "comfort_speed", "", new Vector2(0, 130), new Vector2(650, 43), () => ChangeComfort(0));
            comfortVolume = Button(p, "comfort_volume", "", new Vector2(0, 80), new Vector2(650, 43), () => ChangeComfort(1));
            comfortTurn = Button(p, "comfort_turn", "", new Vector2(0, 30), new Vector2(650, 43), () => ChangeComfort(2));
            comfortSeated = Button(p, "comfort_seated", "", new Vector2(0, -20), new Vector2(650, 43), () => ChangeComfort(3));
            Button(p, "comfort_more", "字级 / 分项声音 / 性能", new Vector2(0, -70), new Vector2(650, 43), () => ShowPage(Page.ComfortAudio));
            Button(p, "tutorial_repeat", "开始 / 重看四步教学", new Vector2(-165, -123), new Vector2(310, 43), () => tutorial.Begin(), true);
            tutorialSkip = Button(p, "tutorial_skip", "跳过教学，继续探索", new Vector2(165, -123), new Vector2(310, 43), () => { tutorial.Skip(); Dispatch("resume", 0); });
            tutorialStatus = Label(p, "Tutorial status and access", "", 14, new Vector2(0, -224), new Vector2(690, 44));
            tutorialStatus.color = Muted;
            BuildEntryReplay(p);
            Transform audio = PageRoot(Page.ComfortAudio);
            comfortText = Button(audio, "comfort_text_scale", "", new Vector2(0, 126), new Vector2(650, 49), () => ChangeComfort(4));
            for (int i = 0; i < 3; i++)
            {
                int category = i;
                audioCategoryTargets[i] = Button(audio, "comfort_audio_" + i, "", new Vector2(0, 68 - i * 58), new Vector2(650, 49),
                    () => { OceanAudioPreferences.ToggleMute((OceanAudioCategory)category); RefreshComfort(); ResetInput(); });
            }
            performanceToggle = Button(audio, "toggle_performance", "", new Vector2(0, -106), new Vector2(650, 49), () => { Dispatch("toggle_performance", 0); Refresh(); ResetInput(); });
            Label(audio, "Independent sound routing", "环境：海底背景 · 音乐：结局配乐 · 反馈：收集与渔枪\n字级同步放大菜单、文字和点击区域，保留原排版", 17,
                new Vector2(0, -201), new Vector2(690, 72)).color = Muted;
        }

        void ChangeComfort(int field)
        {
            OceanComfortProfile old = hands.Comfort;
            hands.ApplyComfort(new OceanComfortProfile(field == 0 ? (old.Speed + 1) % 3 : old.Speed,
                field == 1 ? (old.Volume + 1) % 5 : old.Volume, field == 2 ? (old.Turn + 1) % 3 : old.Turn,
                field == 3 ? !old.Seated : old.Seated, field == 4 ? (old.TextScale + 1) % 3 : old.TextScale));
            RefreshComfort(); ResetInput();
        }

        void RefreshComfort()
        {
            if (comfortSpeed == null) return;
            OceanComfortProfile profile = hands.Comfort;
            comfortSpeed.label.text = "移动速度：" + profile.SpeedName + "（点击切换）";
            comfortVolume.label.text = "全部声音：" + (profile.Volume == 0 ? "静音" : (profile.Volume * 25) + "%") + "（点击切换）";
            comfortTurn.label.text = "人工转向：" + profile.TurnName + "（点击切换）";
            comfortSeated.label.text = "划水姿势：" + (profile.Seated ? "坐姿 · 小幅动作" : "站姿 / 常规") + "（点击切换）";
            comfortText.label.text = "菜单字级：" + profile.TextScaleName + "（点击切换）";
            for (int i = 0; i < audioCategoryTargets.Length; i++)
                audioCategoryTargets[i].label.text = AudioCategoryNames[i] + "：" + (OceanAudioPreferences.IsMuted((OceanAudioCategory)i) ? "静音" : "开启") + "（点击切换）";
            ApplyExpansionPose();
            if (hudRoot != null) hudRoot.transform.localScale = Vector3.one * .00085f * profile.MenuScaleMultiplier;
            SetEnabled(tutorialSkip, TutorialActive);
            RefreshEntryReplay();
            tutorialStatus.text = TutorialActive ? "教学暂停本关时钟，不改变星级、收藏或弹药。\n" + OceanCampaignGuide.DescribeTutorial(tutorial.Rules).Action :
                "坐姿无需弯腰拾取练习物；保持周围有安全活动空间。\n手追不可用时，可用控制器射线打开菜单和退出；任务抓取需要手追。";
        }

        void RefreshTutorialPresentation()
        {
            if (!initialized) return;
            RefreshHud(); if (page == Page.Comfort || page == Page.ComfortAudio) RefreshComfort();
        }

        bool IsNewCampaignForTutorial()
        {
            if (!automaticTutorialAllowed) return false;
            if (state.Current == null || state.Current.level != 1 || state.Save.shellMask != 0 || state.Save.fishMask != 0 || state.Save.shark) return false;
            foreach (int stars in state.Save.bestStars) if (stars > 0) return false;
            return true;
        }
#if UNITY_EDITOR
        public RectTransform ExpandedRectForValidation => (RectTransform)panelRoot.transform;
        public void SetTextScaleForValidation(int index)
        {
            var old = hands.Comfort;
            hands.ApplyComfort(new OceanComfortProfile(old.Speed,old.Volume,old.Turn,old.Seated,index),false);
            RefreshComfort();
        }
#endif
    }
}

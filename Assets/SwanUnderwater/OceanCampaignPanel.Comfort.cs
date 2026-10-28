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
        readonly Target[] languageChoices = new Target[3];
        Text languageDetails;

        void BuildComfort()
        {
            Transform p = PageRoot(Page.Comfort);
            comfortSpeed = Button(p, "comfort_speed", "", new Vector2(0, 130), new Vector2(650, 43), () => ChangeComfort(0));
            comfortVolume = Button(p, "comfort_volume", "", new Vector2(0, 80), new Vector2(650, 43), () => ChangeComfort(1));
            comfortTurn = Button(p, "comfort_turn", "", new Vector2(0, 30), new Vector2(650, 43), () => ChangeComfort(2));
            comfortSeated = Button(p, "comfort_seated", "", new Vector2(0, -20), new Vector2(650, 43), () => ChangeComfort(3));
            Button(p, "language_settings", "语言 / Language", new Vector2(-165, -70), new Vector2(310, 43), () => ShowPage(Page.Language));
            Button(p, "comfort_more", "字级 / 声音 / 性能", new Vector2(165, -70), new Vector2(310, 43), () => ShowPage(Page.ComfortAudio));
            Button(p, "tutorial_repeat", "开始 / 重看四步教学", new Vector2(-165, -123), new Vector2(310, 43), () => tutorial.Begin(), true);
            tutorialSkip = Button(p, "tutorial_skip", "关闭教学，继续探索", new Vector2(165, -123), new Vector2(310, 43), CloseTutorialAndResume);
            tutorialStatus = Label(p, "Tutorial status and access", "", 14, new Vector2(0, -224), new Vector2(690, 44));
            tutorialStatus.color = Muted;
            BuildEntryReplay(p);
            BuildLanguage();
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
            comfortSpeed.label.text = OceanLanguage.Format("移动速度：{0}（点击切换）", OceanLanguage.Text(profile.SpeedName));
            comfortVolume.label.text = OceanLanguage.Format("全部声音：{0}（点击切换）", profile.Volume == 0 ? OceanLanguage.Text("静音") : (profile.Volume * 25) + "%");
            comfortTurn.label.text = OceanLanguage.Format("人工转向：{0}（点击切换）", OceanLanguage.Text(profile.TurnName));
            comfortSeated.label.text = OceanLanguage.Format("划水姿势：{0}（点击切换）", OceanLanguage.Text(profile.Seated ? "坐姿 · 小幅动作" : "站姿 / 常规"));
            comfortText.label.text = OceanLanguage.Format("菜单字级：{0}（点击切换）", OceanLanguage.Text(profile.TextScaleName));
            for (int i = 0; i < audioCategoryTargets.Length; i++)
                audioCategoryTargets[i].label.text = OceanLanguage.Format("{0}：{1}（点击切换）", OceanLanguage.Text(AudioCategoryNames[i]), OceanLanguage.Text(OceanAudioPreferences.IsMuted((OceanAudioCategory)i) ? "静音" : "开启"));
            ApplyExpansionPose();
            if (hudRoot != null) hudRoot.transform.localScale = Vector3.one * .00085f * profile.MenuScaleMultiplier;
            RefreshTutorialExitPresentation();
            SetEnabled(tutorialSkip, TutorialActive);
            RefreshEntryReplay();
            tutorialStatus.text = TutorialActive ? OceanLanguage.Text("教学暂停本关时钟，不改变星级、收藏或弹药。\n") + OceanCampaignGuide.DescribeTutorial(tutorial.Rules).Action :
                OceanLanguage.Text("坐姿无需弯腰拾取练习物；保持周围有安全活动空间。\n手追不可用时，可用控制器射线打开菜单和退出；任务抓取需要手追。");
            RefreshLanguageSettings();
        }

        void BuildLanguage()
        {
            Transform p = PageRoot(Page.Language);
            for (int i=0;i<languageChoices.Length;i++)
            {
                int choice=i;
                languageChoices[i]=Button(p,"language_"+i,"",new Vector2(0,105-i*73),new Vector2(650,56),
                    ()=>{OceanLanguage.SetPreference((OceanLanguageChoice)choice);RefreshLanguageSettings();ResetInput();});
            }
            languageDetails=Label(p,"Language preference scope","",20,new Vector2(0,-153),new Vector2(665,125));
            languageDetails.color=Muted;
            RefreshLanguageSettings();
        }

        void RefreshLanguageSettings()
        {
            if(languageDetails==null)return;
            var selected=OceanLanguage.Preference;
            for(int i=0;i<languageChoices.Length;i++)
            {
                string name=i==0?OceanLanguage.Format("跟随系统（{0}）",OceanLanguage.Text(OceanLanguage.SystemDefault==OceanLanguageChoice.English?"英文":"中文")):
                    i==1?"简体中文":"English";
                languageChoices[i].label.text=name+(selected==(OceanLanguageChoice)i?OceanLanguage.Text(" · 已选"):"");
                languageChoices[i].label.color=selected==(OceanLanguageChoice)i?Gold:Ink;
            }
            languageDetails.text=OceanLanguage.Text("默认跟随系统语言；无法识别时，中国地区用中文，其他地区用英文。\n手动选择立即生效并保存。可随时恢复跟随系统，关卡与收藏不会改变。");
        }

        void OnApplicationFocus(bool focused) { if(focused)OceanLanguage.RefreshSystemDefault(); }

        void RefreshTutorialPresentation()
        {
            if (!initialized) return;
            RefreshHud(); RefreshTutorialExitPresentation();
            if (page == Page.Comfort || page == Page.ComfortAudio) RefreshComfort();
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

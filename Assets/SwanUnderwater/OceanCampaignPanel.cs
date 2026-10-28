using System;
using System.Collections.Generic;
using HandType = SwanUnderwater.OceanHandSide;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR;

namespace SwanUnderwater
{
    /// <summary>World-fixed, explicitly operated campaign menu. State mutations belong to the controller callback.</summary>
    [DefaultExecutionOrder(95)]
    public sealed partial class OceanCampaignPanel : MonoBehaviour
    {
        enum Page { Menu, Levels, Targets, ShellsOne, ShellsTwo, Marine, MarineTwo, Sharks, Confirm, Result, CollectionHub, Naturalist, Comfort, ComfortAudio, SaveRecovery, Achievements, Language }
        sealed class Target
        {
            public string id;
            public RectTransform rect;
            public Image image;
            public Text label;
            public Action action;
            public bool enabled = true;
            public Color normal;
            public OceanMenuOrnament border;
            public float hover;
            public bool hovered;
            public Vector3 pointerPoint;
            public int pointerFrame = -1;
        }
        sealed class HandInput
        {
            public readonly OceanMenuPressGate gate = new OceanMenuPressGate();
            public LineRenderer ray, cursor;
            public Ray smoothedRay;
            public bool hasRay;
            public int hitIndex = -1, debugFrames, debugFresh, debugHit, debugRelease, debugArmed, debugPress, debugLong;
            public float debugMaximumDt;
            public double nextTrace;
#if UNITY_EDITOR
            public bool validationActive, validationFresh, validationPinchFresh;
            public Ray validationRay;
            public float validationStrength;
#endif
        }
        sealed class ControllerInput
        {
            public InputDevice device;
            public bool triggerArmed;
            public LineRenderer ray;
        }
        const int PageSize = 5, ShellCount = 10, FishOffset = 10, SharkIndex = 20, SpeciesCount = 21;
        static readonly string[] ShellNames = { "扇贝", "海螺", "宝螺", "蛤蜊", "锥螺", "蚶贝", "牡蛎", "帽贝", "蛏子", "骨螺" };
        static readonly Page[] CollectionPages = { Page.ShellsOne, Page.ShellsTwo, Page.Marine, Page.MarineTwo, Page.Sharks };
        static readonly string[] CollectionGroups = { "贝壳珍藏 一", "贝壳珍藏 二", "海洋鱼类 一", "海洋鱼类 二", "海洋巨兽" };
        static readonly Color Ink = new Color(.89f, .97f, .97f), Muted = new Color(.55f, .75f, .77f);
        static readonly Color ButtonColor = new Color(.035f, .17f, .20f, .98f), Gold = new Color(1f, .79f, .35f);
        readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
        readonly List<Target> targets = new List<Target>(42);
        readonly List<Material> ownedMaterials = new List<Material>();
        readonly Dictionary<Material, Material> previewMaterials = new Dictionary<Material, Material>();
        readonly Text[] levelLabels = new Text[PageSize], levelDetails = new Text[PageSize], collectionStatuses = new Text[SpeciesCount];
        readonly Target[] levelTargets = new Target[PageSize];
        readonly Image[] collectionCards = new Image[SpeciesCount];
        readonly Transform[] collectionAnchors = new Transform[SpeciesCount], goalAnchors = new Transform[5];
        readonly GameObject[] speciesModels = new GameObject[SpeciesCount], goalCards = new GameObject[5];
        readonly Text[] goalNames = new Text[5], goalStatuses = new Text[5];
        const int MechanicGoalIndex = SpeciesCount;
        readonly int[] goalSpecies = new int[SpeciesCount + 1];
        readonly HandInput leftHand = new HandInput(), rightHand = new HandInput();
        readonly ControllerInput leftController = new ControllerInput(), rightController = new ControllerInput();
        Camera view;
        HandSwimController hands;
        OceanCampaignState state;
        OceanCampaignCreatures creatures;
        Action<string, int> callback;
        Font bodyFont, titleFont;
        Material uiMaterial, imageMaterial, rayMaterial;
        Texture2D roundedTexture;
        Sprite roundedSprite;
        GameObject panelRoot, hudRoot;
        Text heading, subtitle, hudProgress, hudTime, hudHint, menuProgress, confirmText, resultText, levelPageLabel;
        Target back, close, resume, restart, targetsButton, goalPrevious, goalNext, resultExplore, resultNext, resultRetry, resultReplay, confirmAccept;
        Target levelPrevious, levelNext, performanceToggle, menuPerformanceToggle;
        int guideHint, guideLevel = -1;
        double nextGuideHint;
        int levelPage;
        Text goalPageLabel;
        int goalCount, goalPage, boundGoalLevel = -1, boundGoalPage = -1;
        Page page, confirmReturn;
        string confirmAction, unavailableLevelNotice, confirmTitleSource, confirmDetailsSource;
        int confirmLevel;
        bool initialized, available, disposed;
        int activatedFrame = -1;
        double blockedUntil;
        float unreachableSeconds, openAmount;
        bool buttonHoveredThisFrame, wasTransitioning;
        Vector3 expandedPosition;
        Quaternion expandedRotation;
        CanvasGroup panelGroup;
        OceanMenuOrnament panelBorder;
        OceanMenuClickFeedback clickFeedback;
        Target touchBar;
        double worldInputUntil;
        public bool IsOpen { get; private set; }
        public bool IsTransitioning => IsOpen ? openAmount < .9999f : openAmount > .0001f;
        public bool WorldInputBlocked => IsTransitioning || Time.unscaledTimeAsDouble < worldInputUntil;
        public bool IsAvailableForDiagnostics => available;
        public string PageNameForDiagnostics => page.ToString();
        public bool TutorialActive => tutorial != null && tutorial.Active;

        public void Initialize(Camera camera, HandSwimController controller, OceanCampaignState campaign, Action<string, int> onAction, bool offerAutomaticTutorial = false)
        {
            if (camera == null || controller == null || campaign == null || onAction == null) throw new ArgumentNullException("Campaign panel initialization");
            view = camera; hands = controller; state = campaign; callback = onAction;
            automaticTutorialAllowed = offerAutomaticTutorial;
            creatures = GetComponent<OceanCampaignCreatures>();
            if (initialized) { Refresh(); return; }
            bodyFont = Resources.Load<Font>("Treasure/TreasureFont");
            titleFont = Resources.Load<Font>("Loading/OceanTitleFont");
            Shader shader = Resources.Load<Shader>("PerformanceHudUI");
            if (bodyFont == null || titleFont == null || shader == null) throw new InvalidOperationException("Campaign UI font or shader missing.");
            uiMaterial = Own(new Material(shader) { name = "Campaign text UI", renderQueue = 4120 });
            imageMaterial = Own(new Material(shader) { name = "Campaign panel UI", renderQueue = 4110 });
            rayMaterial = Own(new Material(shader) { name = "Campaign controller ray", renderQueue = 4160 });
            BuildRoundedSprite();
            panelRoot = MakeCanvas("Ocean campaign menu", new Vector2(760, 620));
            panelRoot.transform.SetParent(transform, false);
            panelRoot.transform.localScale = Vector3.one * .00115f;
            panelGroup = panelRoot.AddComponent<CanvasGroup>();
            AddImage(panelRoot.transform, "Menu background", Vector2.zero, new Vector2(760, 620), new Color(.009f, .026f, .052f, .96f));
            panelBorder = Ornament(panelRoot.transform, "Luminous gold frame", Vector2.zero, new Vector2(758, 618), 2.2f, 21f, true);
            AddImage(panelRoot.transform, "Header accent", new Vector2(0, 285), new Vector2(210, 2), Gold);
            heading = Label(panelRoot.transform, "Menu title", "海底世界", 47, new Vector2(0, 236), new Vector2(550, 70), true);
            heading.color = new Color(1f, .89f, .63f);
            subtitle = Label(panelRoot.transform, "Menu subtitle", "", 21, new Vector2(0, 182), new Vector2(675, 45));
            subtitle.color = Muted;
            Label(panelRoot.transform, "Explicit controls", OceanXRPlatform.IsVisionOS ? "看向选项捏合选择 · 靠近物体后捏合抓取（也可握拳）" : "射线指向后捏合选择", 18,
                new Vector2(0, -279), new Vector2(700, 34)).color = Muted;
            back = Button(panelRoot.transform, "back", "返回", new Vector2(-312, 240), new Vector2(92, 50), Back);
            close = Button(panelRoot.transform, "close", "收起", new Vector2(312, 240), new Vector2(92, 50), CloseHeader);
            BuildMenu(); BuildLevels(); BuildCollections(); BuildCollectionHub(); BuildNaturalist(); BuildAchievements(); BuildTargets(); BuildConfirm(); BuildResult(); BuildHud(); BuildComfort(); BuildSaveRecovery(); BuildTutorialExit();
            leftHand.ray = BuildRay("Left hand menu ray");
            rightHand.ray = BuildRay("Right hand menu ray");
            leftHand.cursor = BuildCursor("Left hand menu cursor");
            rightHand.cursor = BuildCursor("Right hand menu cursor");
            leftController.ray = BuildRay("Left menu controller ray");
            rightController.ray = BuildRay("Right menu controller ray");
            BuildClickFeedback();
            initialized = true;
            tutorial = gameObject.AddComponent<OceanInteractiveTutorial>();
            tutorial.Initialize(view, hands, this, RefreshTutorialPresentation);
            ShowPage(Page.Menu); Refresh();
            panelRoot.SetActive(false); hudRoot.SetActive(false);
            OceanLanguage.Changed += RefreshLanguage;
        }

        public void SetAvailable(bool value)
        {
            if (!value) { if(clickFeedback!=null)clickFeedback.Cancel(); SetTutorialExitVisible(false); }
            if (available == value) return;
            available = value;
            if (!initialized) return;
            tutorial.SetAvailable(value, IsNewCampaignForTutorial());
            if (available && IsOpen) AnchorPanel();
            panelRoot.SetActive(available && (IsOpen || IsTransitioning));
            hudRoot.SetActive(available && !IsOpen && !IsTransitioning);
            RefreshTutorialExitPresentation();
            ResetInput();
        }

        public void OpenMenu()
        {
            if (!initialized) return;
            bool wasOpen = IsOpen; Page previousPage = page;
            IsOpen = true;
            tutorial?.NotifyMenuOpened();
            hands.SetMenuLocomotionPaused(true);
            AnchorPanel(); ShowPage(Page.Menu); Refresh();
            panelRoot.SetActive(available); hudRoot.SetActive(false); ResetInput();
            RefreshTutorialExitPresentation();
            ApplyExpansionPose();
            if (!wasOpen || previousPage != page) LogMenuState("open");
        }

        public void CloseMenu()
        {
            if (!initialized) return;
            bool wasOpen = IsOpen;
            IsOpen = false;
            tutorial?.NotifyResumed();
            panelRoot.SetActive(available && IsTransitioning); hudRoot.SetActive(available && !IsTransitioning);
            hands.SetMenuLocomotionPaused(false); ResetInput();
            RefreshTutorialExitPresentation();
            if (wasOpen) LogMenuState("close");
        }

        public void ShowResult(bool ending)
        {
            if (!initialized) return;
            bool wasOpen = IsOpen; Page previousPage = page;
            IsOpen = true;
            hands.SetMenuLocomotionPaused(true);
            AnchorPanel(); ShowPage(Page.Result); Refresh();
            panelRoot.SetActive(available); hudRoot.SetActive(false); ResetInput();
            ApplyExpansionPose();
            if (!wasOpen || previousPage != page) LogMenuState("result");
        }

        void LogMenuState(string action) => Debug.Log($"[CampaignMenu] action={action} open={IsOpen} page={page} available={available} transition={IsTransitioning} locomotionPaused={hands.MenuLocomotionPaused} frame={Time.frameCount}");

        public void Refresh()
        {
            if (!initialized) return;
#if UNITY_EDITOR
            FullRefreshCountForValidation++;
#endif
            var run = state.Current;
            bool openRun = run != null && OceanCampaignAvailability.IsOpen(run.level);
            if (page == Page.Targets && run != null && !openRun) { ShowUnavailableLevel(run.level); return; }
            RefreshHud();
            string performanceLabel = F("性能面板：{0}", L(PerformanceHud.PreferenceVisible ? "开" : "关"));
            performanceToggle.label.text = performanceLabel;
            menuPerformanceToggle.label.text = performanceLabel;
            RefreshComfort();
            RefreshMenuProgress(openRun);
            RefreshLevels();
            for (int i = 0; i < SpeciesCount; i++)
            {
                bool collected = i < ShellCount ? (state.Save.shellMask & (1 << i)) != 0 : i < SharkIndex ? (state.Save.fishMask & (1 << (i - FishOffset))) != 0 : state.Save.shark;
                collectionStatuses[i].text = L(collected ? "已收藏" : "未收集");
                collectionStatuses[i].color = collected ? Gold : Muted;
                collectionCards[i].color = collected ? new Color(.055f, .19f, .21f, 1) : new Color(.027f, .10f, .13f, 1);
            }
            if (page == Page.Targets && openRun) RefreshTargets();
            else RefreshCollectionModels();
            RefreshNaturalist();
            RefreshAchievements();
            RefreshSaveRecovery();
            if (run != null && !openRun && saveRecoveryReadOnly)
                menuProgress.text = L("存档读取与恢复\n历史进度已保留，请选择开放关卡");
            RefreshResult(); RefreshHeader();
        }

        /// <summary>
        /// Clock, ammunition and held-tool changes affect the compact task strip. They
        /// must not rebuild strings and dirty graphics on every hidden menu page.
        /// State-changing events and opening a page still call Refresh() immediately.
        /// </summary>
        public void RefreshProgress()
        {
            if (!initialized) return;
#if UNITY_EDITOR
            ProgressRefreshCountForValidation++;
#endif
            RefreshHud();
            if (!IsOpen) return;
            var run = state.Current;
            bool openRun = run != null && OceanCampaignAvailability.IsOpen(run.level);
            if (page == Page.Targets && run != null && !openRun) { ShowUnavailableLevel(run.level); return; }
            switch (page)
            {
                case Page.Menu:
                    // Read-only recovery copy is owned by SetSaveRecoveryStatus/Refresh.
                    // A clock tick must neither overwrite it nor refresh its hidden page.
                    if (!saveRecoveryReadOnly) RefreshMenuProgress(openRun);
                    break;
                case Page.Targets: if (openRun) RefreshTargets(); break;
                case Page.Result: RefreshResult(); break;
            }
            // Header text belongs to the visible page; collections, achievement rows,
            // comfort options and model previews retain their event-driven refresh.
            RefreshHeader();
        }

        void RefreshMenuProgress(bool openRun)
        {
            var run = state.Current;
            menuProgress.text = run == null ? F("当前开放前{0}关，请选择关卡", OceanCampaignAvailability.AvailableLevelCount) : !openRun ? L("历史进度已保留\n请选择已开放关卡开始探索") : F("查看本关目标 · 已完成 {0}/{1}\n{2} · {3}", CompletedTargets(), TotalTargets(), CoinProgress(), state.UnlimitedTime ? L("不限时") : F("剩余 {0}", Clock(run.remainingSeconds)));
            SetEnabled(targetsButton, openRun);
            bool canReturn = openRun && (tutorial != null ? tutorial.Rules.AllowsMenuReturn(state.CanCollect) : state.CanCollect);
            SetEnabled(resume, canReturn);
            resume.label.text = L(run != null && !openRun ? "请先选择开放关卡" : TutorialActive ? (tutorial.Rules.TargetsViewed ? "完成教学" : "继续教学") : run != null && state.GoalComplete ? "继续探索" : "继续本关");
            SetEnabled(restart, openRun); SetEnabled(close, TutorialActive || canReturn);
        }

        void BuildMenu()
        {
            Transform p = PageRoot(Page.Menu);
            targetsButton = Button(p, "show_targets", "查看本关目标", new Vector2(0, 124), new Vector2(624, 70), OpenTargetsOrSaveRecovery);
            menuProgress = targetsButton.label; menuProgress.fontSize = 20;
            resume = Button(p, "resume", "继续本关", new Vector2(-163, 43), new Vector2(298, 64), () => Dispatch("resume", 0), true);
            Button(p, "show_levels", "选择关卡", new Vector2(163, 43), new Vector2(298, 64), OpenLevelPicker);
            Button(p, "show_collection", "收藏 / 图鉴 / 成就", new Vector2(-163, -43), new Vector2(298, 64), () => ShowPage(Page.CollectionHub));
            restart = Button(p, "restart_level", "重置本关", new Vector2(163, -43), new Vector2(298, 64),
                () => Confirm("restart_level", 0, "重置本关", "本关从头开始。\n历史最佳星级与收藏会保留。"));
            Button(p, "restart_game", "重新开始游戏", new Vector2(-163, -129), new Vector2(298, 64),
                () => Confirm("restart_game", 0, "重新开始游戏", "回到第一关，重新开启冒险。\n历史最佳星级与收藏会保留。"));
            Button(p, "quit", "退出游戏", new Vector2(163, -129), new Vector2(298, 64),
                () => Confirm("quit", 0, "退出游戏", "保存当前进度并退出海底世界？"));
            bool rankings = OceanReleaseFeatures.LeaderboardsEnabled;
            if (rankings)
                Button(p, "leaderboard", "排行榜", new Vector2(-214, -202), new Vector2(198, 50), () => Dispatch("leaderboard", 0));
            Button(p, "show_comfort", "设置 / 教学", new Vector2(rankings ? 0 : -163, -202),
                new Vector2(rankings ? 198 : 298, 50), () => ShowPage(Page.Comfort));
            menuPerformanceToggle = Button(p, "toggle_performance", "", new Vector2(rankings ? 214 : 163, -202), new Vector2(rankings ? 198 : 298, 50),
                () => { Dispatch("toggle_performance", 0); Refresh(); ResetInput(); });
            menuPerformanceToggle.label.fontSize = 22;
            Label(p, "Star rule", "完成目标得2星，收齐本关金币得3星；金币不代替目标", 17,
                new Vector2(0, -249), new Vector2(700, 24)).color = Muted;
        }

        void BuildLevels()
        {
            Transform p = PageRoot(Page.Levels);
            for (int i = 0; i < PageSize; i++)
            {
                int slot = i;
                var target = Button(p, "start_level_" + (i + 1), "", new Vector2(0, 118 - i * 74), new Vector2(670, 68),
                    () => SelectLevelSlot(slot));
                target.label.fontSize = 21; target.label.rectTransform.anchoredPosition = new Vector2(0, 17);
                target.label.rectTransform.sizeDelta = new Vector2(642, 35);
                levelTargets[i] = target; levelLabels[i] = target.label;
                levelDetails[i] = Label(target.rect, "Level objectives and allowance", "", 17, new Vector2(0, -18), new Vector2(640, 30));
                levelDetails[i].color = Muted;
            }
            levelPrevious = Button(p, "levels_previous", "上一章", new Vector2(-208, -237), new Vector2(150, 42), () => ChangeLevelPage(-1));
            levelPageLabel = Label(p, "Chapter page", "", 19, new Vector2(0, -237), new Vector2(225, 42));
            levelNext = Button(p, "levels_next", "下一章", new Vector2(208, -237), new Vector2(150, 42), () => ChangeLevelPage(1));
        }
        void OpenLevelPicker()
        { unavailableLevelNotice = null; levelPage = state.Current == null || !OceanCampaignAvailability.IsOpen(state.Current.level) ? 0 : (state.Current.level - 1) / PageSize; ShowPage(Page.Levels); }
        void ChangeLevelPage(int delta)
        { unavailableLevelNotice = null; levelPage = Mathf.Clamp(levelPage + delta, 0, (OceanLevelCatalog.Count - 1) / PageSize); RefreshLevels(); RefreshHeader(); ResetInput(); }
        public void ShowUnavailableLevel(int level)
        {
            if (!initialized || OceanCampaignAvailability.IsOpen(level)) return;
            if (!IsOpen) OpenMenu();
            levelPage = (Mathf.Clamp(level, 1, OceanLevelCatalog.Count) - 1) / PageSize;
            unavailableLevelNotice = OceanCampaignAvailability.UnavailableMessage;
            ShowPage(Page.Levels);
        }
        void SelectLevelSlot(int slot)
        {
            int level = levelPage * PageSize + slot + 1;
            if (!OceanCampaignAvailability.IsOpen(level)) { ShowUnavailableLevel(level); return; }
            var definition = OceanLevelCatalog.Get(level);
            Confirm("start_level", level, "进入第" + level + "关", definition.Name + "\n" + definition.Brief + "\n" +
                LevelAllowance(level) + "\n开始新一轮，保留历史最佳与收藏。");
        }
        static string LevelAllowance(int level)
        { var d = OceanLevelCatalog.Get(level); return F("{0} · 金币{1}枚", d.TimeLimitSeconds <= 0 ? L("不限时") : F("{0}分钟", Mathf.CeilToInt(d.TimeLimitSeconds / 60f)), d.CoinCount); }
        void RefreshLevels()
        {
            int pagesCount = (OceanLevelCatalog.Count + PageSize - 1) / PageSize;
            levelPage = Mathf.Clamp(levelPage, 0, pagesCount - 1);
            for (int slot = 0; slot < PageSize; slot++)
            {
                int level = levelPage * PageSize + slot + 1;
                bool exists = level <= OceanLevelCatalog.Count; levelTargets[slot].rect.gameObject.SetActive(exists);
                if (!exists) continue;
                var definition = OceanLevelCatalog.Get(level);
                levelTargets[slot].id = "start_level_" + level;
                levelLabels[slot].text = F("第{0}关  {1}    {2}", level, L(definition.Name), Stars(state.Save.bestStars[level - 1]));
                bool open = OceanCampaignAvailability.IsOpen(level);
                levelTargets[slot].normal = open ? ButtonColor : new Color(.025f,.073f,.10f,.98f);
                levelLabels[slot].color = open ? Ink : Muted;
                levelDetails[slot].text = open ? LevelPreview(definition) : F("{0} · 历史记录保留", L(OceanCampaignAvailability.UnavailableMessage));
                levelDetails[slot].color = open ? Muted : new Color(.64f,.73f,.76f);
            }
            SetEnabled(levelPrevious, levelPage > 0); SetEnabled(levelNext, levelPage + 1 < pagesCount);
            levelPageLabel.text = F("第{0}章 / 共{1}章", levelPage + 1, pagesCount);
        }

        static string LevelPreview(OceanLevelDefinition definition)
        {
            if(OceanLanguage.IsEnglish&&!definition.MechanicOptional)
            {
                var parts=new List<string>(4);
                if(definition.ShellMask!=0)parts.Add(F("贝壳{0}种",Count(definition.ShellMask)));
                if(definition.FishMask!=0)parts.Add(F("鱼{0}种",Count(definition.FishMask)));
                if(definition.SharkHits>0)parts.Add(F("单鲨{0}次命中后抓取",definition.SharkHits));
                if(definition.OrderedShell||definition.OrderedFish)parts.Add(L("依图鉴顺序"));
                return string.Join(" · ",parts)+" · "+LevelAllowance(definition.Level);
            }
            if (!definition.MechanicOptional) return L(definition.Brief)+" · "+LevelAllowance(definition.Level);
            string kind;
            switch (definition.Mechanic)
            {
                case OceanMechanicKind.RingRace:kind="穿环";break;
                case OceanMechanicKind.Observation:kind="观察";break;
                case OceanMechanicKind.RunePuzzle:kind="符文";break;
                case OceanMechanicKind.CurrentRiding:kind="导流";break;
                case OceanMechanicKind.Escort:kind="护航";break;
                default:kind="探索";break;
            }
            // A concise, state-derived preview fits the existing large row. The
            // confirmation keeps the complete authored brief and collection order.
            return F("可选{0} · 贝壳{1}种 / 鱼{2}种 · {3}", L(kind), Count(definition.ShellMask), Count(definition.FishMask), LevelAllowance(definition.Level));
        }

        void BuildCollections()
        {
            for (int group = 0; group < CollectionPages.Length; group++)
            {
                Page currentPage = CollectionPages[group];
                Transform p = PageRoot(currentPage);
                for (int cell = 0; cell < 5; cell++)
                {
                    int index = group * PageSize + cell;
                    if (index >= SpeciesCount) break;
                    Vector2 location = new Vector2((cell % 3 - 1) * 224, cell < 3 ? 70 : -112);
                    if (cell >= 3) location.x += 112;
                    if (group == CollectionPages.Length - 1) location = new Vector2(0, 20);
                    Image card = AddImage(p, "Collection card " + index, location, new Vector2(210, 164), ButtonColor);
                    collectionCards[index] = card;
                    var anchor = new GameObject("Actual species model " + index).transform;
                    anchor.SetParent(card.transform, false); anchor.localPosition = new Vector3(0, 28, -8);
                    collectionAnchors[index] = anchor;
                    string name = SpeciesNameSource(index);
                    Label(card.transform, "Species name", name, 23, new Vector2(0, -38), new Vector2(195, 40));
                    collectionStatuses[index] = Label(card.transform, "Collection status", "未收集", 17, new Vector2(0, -68), new Vector2(190, 30));
                }
                int previous = (group + CollectionPages.Length - 1) % CollectionPages.Length, next = (group + 1) % CollectionPages.Length;
                Button(p, "collection_previous_" + group, "上一页", new Vector2(-208, -229), new Vector2(150, 48), () => ShowCollectionPage(previous));
                Label(p, "Collection page", (group + 1) + " / " + CollectionPages.Length, 20, new Vector2(0, -229), new Vector2(120, 48)).color = Muted;
                Button(p, "collection_next_" + group, "下一页", new Vector2(208, -229), new Vector2(150, 48), () => ShowCollectionPage(next));
            }
        }

        void BuildTargets()
        {
            Transform p = PageRoot(Page.Targets);
            for (int cell = 0; cell < 5; cell++)
            {
                Vector2 location = new Vector2((cell % 3 - 1) * 224, cell < 3 ? 70 : -112);
                if (cell >= 3) location.x += 112;
                Image card = AddImage(p, "Current target card " + cell, location, new Vector2(210, 164), ButtonColor);
                goalCards[cell] = card.gameObject;
                var anchor = new GameObject("Current target model slot " + cell).transform;
                anchor.SetParent(card.transform, false); anchor.localPosition = new Vector3(0, 28, -8); goalAnchors[cell] = anchor;
                goalNames[cell] = Label(card.transform, "Target species", "", 23, new Vector2(0, -38), new Vector2(195, 40));
                goalStatuses[cell] = Label(card.transform, "This run target progress", "", 17, new Vector2(0, -68), new Vector2(195, 30));
            }
            goalPrevious = Button(p, "targets_previous", "上一页", new Vector2(-208, -229), new Vector2(150, 48),
                () => { goalPage = Math.Max(0, goalPage - 1); Refresh(); ResetInput(); });
            goalPageLabel = Label(p, "Target page", "", 20, new Vector2(0, -229), new Vector2(140, 45));
            goalNext = Button(p, "targets_next", "下一页", new Vector2(208, -229), new Vector2(150, 48),
                () => { goalPage++; Refresh(); ResetInput(); });
        }

        void RefreshTargets()
        {
            var run = state.Current; goalCount = 0;
            if (run != null)
            {
                if (state.HasMechanic && !state.Definition.MechanicOptional) goalSpecies[goalCount++] = MechanicGoalIndex;
                AppendTargets(state.ShellTargetMask, 0);
                AppendTargets(state.FishTargetMask, FishOffset);
                if (state.RequiresShark) goalSpecies[goalCount++] = SharkIndex;
                if (state.Definition.MechanicOptional) goalSpecies[goalCount++] = MechanicGoalIndex;
            }
            int pageCount = Math.Max(1, (goalCount + PageSize - 1) / PageSize);
            goalPage = Mathf.Clamp(goalPage, 0, pageCount - 1);
            if (boundGoalLevel != (run == null ? 0 : run.level) || boundGoalPage != goalPage)
            {
                RestoreCollectionModels();
                for (int cell = 0; cell < PageSize && goalPage * PageSize + cell < goalCount; cell++)
                {
                    int index = goalSpecies[goalPage * PageSize + cell]; if (index == MechanicGoalIndex) continue; EnsureSpeciesModel(index);
                    speciesModels[index].transform.SetParent(goalAnchors[cell], false); speciesModels[index].SetActive(true);
                }
                boundGoalLevel = run == null ? 0 : run.level; boundGoalPage = goalPage;
            }
            for (int cell = 0; cell < PageSize; cell++)
            {
                int offset = goalPage * PageSize + cell; goalCards[cell].SetActive(offset < goalCount);
                if (offset >= goalCount) continue;
                int index = goalSpecies[offset];
                if (index == MechanicGoalIndex)
                {
                    goalNames[cell].rectTransform.anchoredPosition = new Vector2(0, 52);
                    goalStatuses[cell].rectTransform.anchoredPosition = new Vector2(0, -12);
                    goalStatuses[cell].rectTransform.sizeDelta = new Vector2(195, 110);
                    goalNames[cell].text = L(state.Definition.MechanicOptional ? "可选支线" : state.Definition.Name.Split('·')[0]);
                    goalStatuses[cell].text = F("{0} {1}/{2}\n{3}", L(state.MechanicComplete ? "已完成" : "进度"), run.mechanicProgress, state.Definition.MechanicTarget, L(state.Definition.MechanicHint));
                    goalStatuses[cell].fontSize = 17;
                    goalStatuses[cell].color = state.MechanicComplete ? Gold : Muted;
                    continue;
                }
                goalNames[cell].rectTransform.anchoredPosition = new Vector2(0, -38);
                goalStatuses[cell].rectTransform.anchoredPosition = new Vector2(0, -68);
                goalStatuses[cell].rectTransform.sizeDelta = new Vector2(195, 30);
                goalStatuses[cell].fontSize = 17;
                bool caught = index < ShellCount ? (run.shellMask & (1 << index)) != 0 : index < SharkIndex ? (run.fishMask & (1 << (index - FishOffset))) != 0 : run.sharkCaught;
                bool next = index < ShellCount ? state.Definition.OrderedShell && state.NextShellSpecies == index : index < SharkIndex && state.Definition.OrderedFish && state.NextFishSpecies == index - FishOffset;
                int order = index < ShellCount ? OrderIndex(state.Definition.OrderedShell, state.ShellTargetMask, index) : index < SharkIndex ? OrderIndex(state.Definition.OrderedFish, state.FishTargetMask, index - FishOffset) : -1;
                goalNames[cell].text = (order < 0 ? "" : (order + 1) + ". ") + SpeciesName(index);
                goalStatuses[cell].text = caught ? L(index < ShellCount ? "本关已收集" : "本关已捕获") : next ? L("下一目标") : order >= 0 ? L("按顺序寻找") : index < ShellCount ? L("本关待寻找") : index < SharkIndex ? L("本关待捕获") :
                    !run.toolOwned ? L("先找到渔枪") : run.sharkHits < state.SharkHitTarget ? F("最高命中 {0}/{1}", run.sharkHits, state.SharkHitTarget) : L("已眩晕，可以抓取");
                goalStatuses[cell].color = caught || next ? Gold : Muted;
            }
            goalPrevious.rect.gameObject.SetActive(pageCount > 1); goalNext.rect.gameObject.SetActive(pageCount > 1);
            SetEnabled(goalPrevious, goalPage > 0); SetEnabled(goalNext, goalPage + 1 < pageCount);
            goalPageLabel.text = F("本关目标 {0}/{1}", goalPage + 1, pageCount);
        }
        void AppendTargets(int mask, int offset)
        {
            // Catalog order is the persistent species index, matching State.FirstMissing.
            for (int species = 0; species < ShellCount; species++)
                if ((mask & (1 << species)) != 0) goalSpecies[goalCount++] = offset + species;
        }
        static int OrderIndex(bool ordered, int mask, int species) => ordered ? Count(mask & ((1 << species) - 1)) : -1;
        static string SpeciesNameSource(int index) => index < ShellCount ? ShellNames[index] : index < SharkIndex ? OceanLevelCatalog.FishNames[index - FishOffset] : "鲨鱼";
        static string SpeciesName(int index) => L(SpeciesNameSource(index));
        void EnsureSpeciesModel(int index)
        { if (speciesModels[index] == null) BuildSpeciesModel(collectionAnchors[index], index); }
        void RefreshCollectionModels()
        {
            int group = Array.IndexOf(CollectionPages, page);
            for (int i = 0; i < SpeciesCount; i++)
            {
                bool visible = group >= 0 && i / PageSize == group;
                if (visible) EnsureSpeciesModel(i);
                if (speciesModels[i] != null) speciesModels[i].SetActive(visible);
            }
        }
        void RestoreCollectionModels()
        {
            for (int i = 0; i < speciesModels.Length; i++)
                if (speciesModels[i] != null)
                { speciesModels[i].transform.SetParent(collectionAnchors[i], false); speciesModels[i].SetActive(false); }
            boundGoalLevel = boundGoalPage = -1;
        }

        void BuildConfirm()
        {
            Transform p = PageRoot(Page.Confirm);
            confirmText = Label(p, "Confirm details", "", 23, new Vector2(0, 22), new Vector2(660, 174));
            Button(p, "cancel", "取消", new Vector2(-160, -143), new Vector2(280, 70), () => ShowPage(confirmReturn));
            confirmAccept = Button(p, "confirm", "确认", new Vector2(160, -143), new Vector2(280, 70), () => Dispatch(confirmAction, confirmLevel), true);
        }

        void BuildResult()
        {
            Transform p = PageRoot(Page.Result);
            resultText = Label(p, "Result details", "", 25, new Vector2(0, 70), new Vector2(660, 152));
            resultExplore = Button(p, "result_explore", "继续探索，寻找金币", new Vector2(0, -42), new Vector2(580, 64), () => Dispatch("resume", 0), true);
            resultNext = Button(p, "next_level", "进入下一关", new Vector2(0, -123), new Vector2(580, 64), () => Dispatch("next_level", 0));
            resultRetry = Button(p, "result_retry", "重试本关", new Vector2(0, -42), new Vector2(580, 64), () => Dispatch("restart_level", 0), true);
            resultReplay = Button(p, "ending_replay", "重新开始冒险", new Vector2(0, -123), new Vector2(580, 64),
                () => Confirm("restart_game", 0, "重新开始游戏", "回到第一关，重新开启冒险。\n历史最佳星级与收藏会保留。"));
            Button(p, "result_menu", "主菜单", new Vector2(-165, -220), new Vector2(290, 52), () => ShowPage(Page.Menu));
            Button(p, "result_quit", "退出游戏", new Vector2(165, -220), new Vector2(290, 52),
                () => Confirm("quit", 0, "退出游戏", "保存当前进度并退出海底世界？"));
        }

        void BuildHud()
        {
            // 20% thinner; main physical text size is unchanged and the details grow from 13 to 16.
            hudRoot = MakeCanvas("Campaign goals and menu entry", new Vector2(650, 56));
            hudRoot.transform.SetParent(view.transform, false);
            hudRoot.transform.localPosition = new Vector3(0, .210f, .70f);
            hudRoot.transform.localScale = Vector3.one * .00085f;
            touchBar = Button(hudRoot.transform, "menu", "", Vector2.zero, new Vector2(650, 56), () => Dispatch("menu", 0), true);
            touchBar.normal = new Color(.011f, .035f, .065f, .86f);
            touchBar.border.SetMotion(true, .095f);
            touchBar.label.gameObject.SetActive(false);
            hudProgress = Label(touchBar.rect, "Current action", "", 18, new Vector2(-51, 11.5f), new Vector2(520, 30));
            hudProgress.alignment = TextAnchor.MiddleLeft; hudProgress.color = new Color(1f, .88f, .61f);
            hudTime = Label(touchBar.rect, "Labeled counts and time", "", 16, new Vector2(-51, -11.5f), new Vector2(520, 27));
            hudTime.alignment = TextAnchor.MiddleLeft;
            hudHint = Label(touchBar.rect, "Expand hint", "捏合菜单", 15, new Vector2(267, 0), new Vector2(84, 29)); hudHint.color = Muted;
        }
        void RefreshHud()
        {
            int level = state.Current == null ? 0 : state.Current.level;
            if(level != guideLevel){guideLevel=level;guideHint=0;nextGuideHint=Time.unscaledTimeAsDouble+8;}
            if (state.Current != null && !OceanCampaignAvailability.IsOpen(level))
            {
                hudProgress.text = L("历史进度已保留，请选择开放关卡");
                hudTime.text = F("当前开放前{0}关 · 捏合展开菜单", OceanCampaignAvailability.AvailableLevelCount);
                hudTime.color = Muted;
                return;
            }
            var copy=TutorialActive ? OceanCampaignGuide.DescribeTutorial(tutorial.Rules,tutorial.PracticeHint) : OceanCampaignGuide.Describe(state,guideHint);
            hudProgress.text=copy.Action;hudTime.text=copy.Details;hudTime.color=copy.Urgent?Gold:Muted;
        }

        void RefreshResult()
        {
            var run = state.Current;
            bool openRun = run != null && OceanCampaignAvailability.IsOpen(run.level);
            bool failed = openRun && state.Failed;
            bool completed = openRun && state.GoalComplete;
            bool final = completed && OceanCampaignAvailability.IsFinalLevel(run.level);
            resultRetry.rect.gameObject.SetActive(failed);
            resultExplore.rect.gameObject.SetActive(completed && state.CanCollect);
            resultNext.rect.gameObject.SetActive(completed && OceanCampaignAvailability.IsOpen(run.level + 1));
            resultReplay.rect.gameObject.SetActive(final);
            resultExplore.label.text = L(run != null && !run.coinCollected && !run.timedOut ? "继续探索，收齐金币" : "继续探索海底");
            if (run != null && !openRun) resultText.text = F("历史进度与收藏已保留。\n{0}\n请从主菜单选择已开放关卡。", L(OceanCampaignAvailability.UnavailableMessage));
            else if (failed) resultText.text = F("{0}\n目标 {1}/{2} · {3}\n本次发现已进入收藏库。",
                state.AmmoExhausted ? F("{0}发弹药已用完，挑战失败。", state.ShotLimit) : L("时间已到，目标尚未完成。"), CompletedTargets(), TotalTargets(), CoinProgress());
            else if (completed) resultText.text = F("{0}  本关获得 {1} 星\n{2} · {3}\n{4}", Stars(state.Stars), state.Stars, CoinProgress(),
                L(run.coinCollected ? "本关金币已收齐" : run.timedOut ? "寻找金币的时间已结束" : "收齐金币可得3星"),
                final ? F("第{0}关已完成，当前篇章圆满！\n后续关卡{1}。", OceanCampaignAvailability.AvailableLevelCount, L(OceanCampaignAvailability.UnavailableMessage)) : L("目标已完成，可以进入下一关。"));
            else resultText.text = L("海底冒险正在继续");
        }
        int CompletedTargets() => state.Current == null ? 0 : Count(state.Current.shellMask & state.ShellTargetMask) + Count(state.Current.fishMask & state.FishTargetMask) + (state.RequiresShark && state.Current.sharkCaught ? 1 : 0) + (state.HasMechanic && !state.Definition.MechanicOptional && state.MechanicComplete ? 1 : 0);
        int TotalTargets() => Count(state.ShellTargetMask) + Count(state.FishTargetMask) + (state.RequiresShark ? 1 : 0) + (state.HasMechanic && !state.Definition.MechanicOptional ? 1 : 0);
        string CoinProgress() => F("金币 {0}/{1}", state.CoinCount, state.CoinTargetCount);
        string ObjectiveCounts()
        {
            var run = state.Current;
            if (run == null) return F("前{0}关开放探索", OceanCampaignAvailability.AvailableLevelCount);
            if (!OceanCampaignAvailability.IsOpen(run.level)) return L("历史进度已保留 · 请选择开放关卡");
            var parts = new List<string>(3);
            if (state.HasMechanic && !state.Definition.MechanicOptional) parts.Add(F("机制 {0}/{1}", run.mechanicProgress, state.Definition.MechanicTarget));
            if (state.ShellTargetMask != 0) parts.Add(F("贝壳 {0}/{1}", Count(run.shellMask & state.ShellTargetMask), Count(state.ShellTargetMask)));
            if (state.FishTargetMask != 0) parts.Add(F("鱼类 {0}/{1}", Count(run.fishMask & state.FishTargetMask), Count(state.FishTargetMask)));
            if (state.RequiresShark) parts.Add(run.sharkCaught ? L("鲨鱼已收集") : F("最高命中 {0}/{1}", run.sharkHits, state.SharkHitTarget));
            return string.Join(" · ", parts);
        }

        void RefreshHeader()
        {
            back.rect.gameObject.SetActive(page != Page.Menu);
            bool unavailableRun = state.Current != null && !OceanCampaignAvailability.IsOpen(state.Current.level);
            close.rect.gameObject.SetActive(TutorialActive || (page != Page.Confirm && page != Page.Result && !unavailableRun));
            switch (page)
            {
                case Page.SaveRecovery: heading.text = L("海底世界"); subtitle.text = L("存档读取与恢复"); break;
                case Page.Menu: heading.text = L("海底世界"); subtitle.text = L(unavailableRun ? "历史进度已保留 · 请选择开放关卡" : "冒险暂停 · 随时继续"); break;
                case Page.Comfort: heading.text = L("舒适设置与教学"); subtitle.text = L(TutorialActive ? "教学中 · 关卡计时与目标暂停" : "设置立即生效并记忆 · 随时退出休息"); break;
                case Page.ComfortAudio: heading.text = L("字级与分项声音"); subtitle.text = L("保留总音量 · 各项静音独立记忆"); break;
                case Page.Language: heading.text = L("语言 / Language"); subtitle.text = L("跟随系统或选择偏好的界面语言"); break;
                case Page.Levels: heading.text = L("选择关卡"); subtitle.text = unavailableLevelNotice != null ? L(unavailableLevelNotice) : F("{0} · 第{1}—{2}关", L(OceanLevelCatalog.ChapterNames[levelPage]), levelPage * PageSize + 1, Math.Min(OceanLevelCatalog.Count, (levelPage + 1) * PageSize)); break;
                case Page.Targets: heading.text = L("本关目标"); subtitle.text = state.Current == null ? L("请先选择关卡") : ObjectiveCounts() + " · " + CoinProgress(); break;
                case Page.CollectionHub: heading.text = L("海洋收藏与观察"); subtitle.text = L("关卡收集与跨关探索，都留在这里"); break;
                case Page.Naturalist: heading.text = L("海洋观察图鉴"); subtitle.text = NaturalistSummary(); break;
                case Page.Achievements: heading.text = L("海洋成就"); subtitle.text = AchievementSummary(); break;
                case Page.ShellsOne: case Page.ShellsTwo: case Page.Marine: case Page.MarineTwo: case Page.Sharks:
                    heading.text = L("海洋收藏库");
                    subtitle.text = F("已收藏 {0} / {1}    ·    {2}", Count(state.Save.shellMask) + Count(state.Save.fishMask) + (state.Save.shark ? 1 : 0), SpeciesCount, L(CollectionGroups[Array.IndexOf(CollectionPages, page)])); break;
                case Page.Confirm: subtitle.text = L("请明确确认本次操作"); RefreshConfirmationCopy(); break;
                case Page.Result:
                    heading.text = L(unavailableRun ? "请选择开放关卡" : state.Failed ? "再试一次" : state.Current != null && state.GoalComplete && OceanCampaignAvailability.IsFinalLevel(state.Current.level) ? "当前篇章圆满" : "关卡完成");
                    subtitle.text = L(unavailableRun ? "历史进度与收藏已保留" : "本关结果与海洋收藏"); break;
            }
        }

        void ShowCollectionPage(int index) => ShowPage(CollectionPages[Mathf.Clamp(index, 0, CollectionPages.Length - 1)]);
        void ShowPage(Page value)
        {
            if (value == Page.Targets && state.Current != null && !OceanCampaignAvailability.IsOpen(state.Current.level))
            { ShowUnavailableLevel(state.Current.level); return; }
            if (value != Page.Levels) unavailableLevelNotice = null;
            if (page == Page.Targets && value != Page.Targets) RestoreCollectionModels();
            if (value == Page.Targets && page != Page.Targets) goalPage = 0;
            page = value;
            if (value == Page.Targets) tutorial?.NotifyTargetsViewed();
            foreach (var pair in pages) pair.Value.SetActive(pair.Key == page);
            if (initialized) { Refresh(); ResetInput(); }
        }
        void Back() => ShowPage(page == Page.Confirm ? confirmReturn : (page == Page.ComfortAudio || page == Page.Language) ? Page.Comfort : page == Page.Achievements ? Page.CollectionHub : Page.Menu);
        void Confirm(string action, int level, string title, string details)
        {
            if (action == "start_level" && !OceanCampaignAvailability.IsOpen(level)) { ShowUnavailableLevel(level); return; }
            if (action == "restart_level" && state.Current != null && !OceanCampaignAvailability.IsOpen(state.Current.level))
            { ShowUnavailableLevel(state.Current.level); return; }
            confirmReturn = page; confirmAction = action; confirmLevel = level;
            confirmTitleSource = title; confirmDetailsSource = details;
            ShowPage(Page.Confirm); RefreshConfirmationCopy();
        }
        static string L(string source) => OceanLanguage.Text(source);
        static string F(string source, params object[] values) => OceanLanguage.Format(source, values);

        void RefreshLanguage()
        {
            if (this == null || !initialized || disposed || hudProgress == null) return;
            // Keep current page, confirmations, selection and campaign state intact.
            // The language service already refreshed static label bindings.
            teachingHeaderShown = null;
            Refresh(); RefreshTutorialExitPresentation();
        }

        void RefreshConfirmationCopy()
        {
            if (page != Page.Confirm || string.IsNullOrEmpty(confirmAction)) return;
            if (confirmAction == "start_level")
            {
                var definition = OceanLevelCatalog.Get(confirmLevel);
                heading.text = F("进入第{0}关", confirmLevel);
                confirmText.text = F("{0}\n{1}\n{2}\n开始新一轮，保留历史最佳与收藏。", L(definition.Name), L(definition.Brief), LevelAllowance(confirmLevel));
            }
            else { heading.text = L(confirmTitleSource); confirmText.text = L(confirmDetailsSource); }
            confirmAccept.label.text = L(confirmAction == "quit" ? "保存并退出" : "确认开始");
        }

        void Dispatch(string action, int level)
        {
            if (string.IsNullOrEmpty(action)) return;
            if (action == "start_level" && !OceanCampaignAvailability.IsOpen(level)) { ShowUnavailableLevel(level); return; }
            if ((action == "resume" || action == "restart_level" || action == "next_level") && state.Current != null)
            {
                int requestedLevel = state.Current.level + (action == "next_level" ? 1 : 0);
                if (!OceanCampaignAvailability.IsOpen(requestedLevel)) { ShowUnavailableLevel(requestedLevel); return; }
            }
            if (action == "resume" && TutorialActive)
            {
                tutorial.NotifyResumed();
                // A repeat lesson can run beside a failed/finished saved run. While it is still
                // active, close only its menu; after completion the controller returns that run's result.
                if (TutorialActive) { CloseMenu(); return; }
            }
            callback?.Invoke(action, level);
        }

        void AnchorPanel()
        {
            unreachableSeconds = 0f;
            Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < .01f) forward = Vector3.ProjectOnPlane(hands.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            forward.Normalize();
            expandedPosition = view.transform.position + forward * 1.05f - Vector3.up * .015f;
            expandedRotation = Quaternion.LookRotation(forward, Vector3.up);
            ApplyExpansionPose();
        }

        // Head-local metres. Generous angular limits keep ordinary head movement
        // from moving a panel that the player is reading or reaching toward.
        public static bool ShouldRecoverMenu(Vector3 panelCenterInHeadSpace)
        {
            if (!Finite(panelCenterInHeadSpace)) return false;
            float distanceSquared = panelCenterInHeadSpace.sqrMagnitude;
            return distanceSquared < .20f * .20f || distanceSquared > 1.85f * 1.85f ||
                panelCenterInHeadSpace.z <= 0f ||
                Math.Abs(panelCenterInHeadSpace.x) > panelCenterInHeadSpace.z * 2.144507f || // tan(65 degrees)
                Math.Abs(panelCenterInHeadSpace.y) > panelCenterInHeadSpace.z * 1.428148f;   // tan(55 degrees)
        }

        void RecoverUnreachableMenu()
        {
            if (!IsOpen || IsTransitioning || buttonHoveredThisFrame || activatedFrame == Time.frameCount ||
                Time.unscaledTimeAsDouble < blockedUntil ||
                !ShouldRecoverMenu(view.transform.InverseTransformPoint(panelRoot.transform.position)))
            { unreachableSeconds = 0f; return; }
            // A stalled/paused frame cannot itself satisfy the sustained-away gate.
            unreachableSeconds += Mathf.Min(Time.unscaledDeltaTime, .10f);
            if (unreachableSeconds < .80f) return;
            AnchorPanel(); ResetInput();
        }

        void ApplyExpansionPose()
        {
            if (panelRoot == null || hudRoot == null) return;
            float t = Mathf.SmoothStep(0, 1, openAmount);
            panelRoot.transform.SetPositionAndRotation(Vector3.Lerp(hudRoot.transform.position, expandedPosition, t),
                Quaternion.Slerp(hudRoot.transform.rotation, expandedRotation, t));
            panelRoot.transform.localScale = Vector3.one * .00115f * hands.Comfort.MenuScaleMultiplier * Mathf.Lerp(.24f, 1f, t);
            panelGroup.alpha = Mathf.SmoothStep(0, 1f, openAmount);
            panelBorder.SetState(1f, .15f + .35f * Mathf.Sin(t * Mathf.PI));
        }

        void AnimatePresentation()
        {
            bool moving = IsTransitioning;
            if (moving)
            {
                openAmount = Mathf.MoveTowards(openAmount, IsOpen ? 1f : 0f, Time.unscaledDeltaTime / (IsOpen ? .36f : .26f));
                ApplyExpansionPose();
                panelRoot.SetActive(available && (IsOpen || IsTransitioning));
                hudRoot.SetActive(available && !IsOpen && !IsTransitioning);
            }
            if (wasTransitioning && !IsTransitioning) ResetInput();
            wasTransitioning = IsTransitioning;
            foreach (Target target in targets)
            {
                if (!target.rect.gameObject.activeInHierarchy) continue;
                target.hover = Mathf.MoveTowards(target.hover, target.hovered ? 1f : 0f, Time.unscaledDeltaTime * 9f);
                target.image.color = target.enabled ? Color.Lerp(target.normal, new Color(.18f, .135f, .055f, .98f), target.hover)
                    : new Color(.024f, .041f, .058f, .85f);
                target.border.SetState(1f, target.enabled ? target.hover : 0f);
                target.border.color = target.enabled ? Color.white : new Color(.4f, .4f, .4f, .6f);
            }
        }

        void LateUpdate()
        {
            if (!initialized || !available || disposed) return;
            RefreshTutorialExitPresentation();
            if(hudRoot.activeSelf && Time.unscaledTimeAsDouble >= nextGuideHint)
            { guideHint=(guideHint+1)%20;nextGuideHint=Time.unscaledTimeAsDouble+8;RefreshHud(); }
            buttonHoveredThisFrame = false;
            foreach (Target target in targets) target.hovered = false;
            ProcessNativeMenuPinches();
            ProcessHandRay(HandType.HandLeft, leftHand);
            ProcessHandRay(HandType.HandRight, rightHand);
            ProcessController(XRNode.LeftHand, leftController);
            ProcessController(XRNode.RightHand, rightController);
            RecoverUnreachableMenu();
            AnimatePresentation();
            RefreshTutorialExitPresentation();
        }

        void ProcessNativeMenuPinches()
        {
            if (!OceanXRPlatform.IsVisionOS) return;
            for (int slot = 0; slot < 2; slot++)
                ProcessNativeMenuPinch(slot);
        }

        void ProcessNativeMenuPinch(int slot)
        {
            if (!initialized || !available || disposed || !hands.TryGetNativeVisionOSMenuPinch(slot, out Ray ray) ||
                !Finite(ray.origin) || !Finite(ray.direction) ||
                ray.direction.sqrMagnitude < .5f || float.IsInfinity(ray.direction.sqrMagnitude)) return;
            Target target = FindByRay(ray, out float distance, true);
            if (target == null) return;
            RememberPointer(target,ray.GetPoint(distance));
            // Claim before an action can close the menu. A blocked/disabled UI hit still
            // owns this entire native gesture, so it cannot become a world pickup later.
            hands.ConsumeNativeVisionOSMenuPinch(slot);
            if (target.enabled) Activate(target);
        }

        void ProcessHandRay(HandType hand, HandInput input)
        {
#if UNITY_EDITOR
            if (input.validationActive)
            { ProcessRaySample(input, input.validationRay, input.validationStrength, input.validationFresh, input.validationPinchFresh);
                hands.SetMenuHandTargeted(hand, input.validationFresh && input.hitIndex >= 0 && !IsTransitioning && Time.unscaledTimeAsDouble >= blockedUntil); return; }
#endif
            Ray ray; float strength; bool fresh, pinchReady;
            if (OceanXRPlatform.IsVisionOS)
                fresh = hands.TryGetMenuHandPointer(hand, out ray, out strength, out pinchReady);
            else
            {
                fresh = hands.TryGetMenuHandRay(hand, out ray, out strength);
                pinchReady = fresh;
            }
            ProcessRaySample(input, ray, strength, fresh, pinchReady);
            hands.SetMenuHandTargeted(hand, fresh && input.hitIndex >= 0 && !IsTransitioning && Time.unscaledTimeAsDouble >= blockedUntil);
            TraceHandInput(hand, input, strength, fresh);
        }

        void TraceHandInput(HandType hand, HandInput input, float strength, bool fresh)
        {
            input.debugFrames++;
            if (fresh) input.debugFresh++;
            if (input.hitIndex >= 0)
            {
                input.debugHit++;
                if (strength <= OceanMenuPressGate.ReleaseThreshold) input.debugRelease++;
                if (strength >= OceanMenuPressGate.PressThreshold) input.debugPress++;
            }
            if (input.gate.Armed) input.debugArmed++;
            if (Time.unscaledDeltaTime > OceanMenuPressGate.MaximumSampleSeconds) input.debugLong++;
            input.debugMaximumDt = Mathf.Max(input.debugMaximumDt, Time.unscaledDeltaTime);
            if (Time.unscaledTimeAsDouble < input.nextTrace) return;
            input.nextTrace = Time.unscaledTimeAsDouble + 2;
            bool sampled = hands.TryGetMenuHandRayDiagnostics(hand, out var diagnostic);
            string hit = input.hitIndex >= 0 ? targets[input.hitIndex].id : "none";
            Debug.Log($"[MenuInput] hand={hand} frames={input.debugFrames} fresh={input.debugFresh} hit={input.debugHit} releasedHit={input.debugRelease} armed={input.debugArmed} pressedHit={input.debugPress} longFrames={input.debugLong} maxDt={input.debugMaximumDt:F4} target={hit} strength={strength:F3} sampled={sampled} tips={diagnostic.TipsValid} tipMm={diagnostic.TipDistanceMeters * 1000:F1} nativeAim={diagnostic.NativeAimValid} legacyTouch={diagnostic.NativeTouchStrength:F3} nativeIndex={diagnostic.NativeIndexPinching} open={IsOpen} transition={IsTransitioning} blocked={Time.unscaledTimeAsDouble < blockedUntil}");
            input.debugFrames = input.debugFresh = input.debugHit = input.debugRelease = input.debugArmed = input.debugPress = input.debugLong = 0;
            input.debugMaximumDt = 0;
        }

        void ProcessRaySample(HandInput input, Ray ray, float strength, bool fresh, bool pinchReady = true)
        {
            pinchReady = pinchReady && !float.IsNaN(strength) && !float.IsInfinity(strength) && strength >= 0f && strength <= 1f;
            input.hitIndex = -1;
            if (!fresh || !Finite(ray.origin) || !Finite(ray.direction) || ray.direction.sqrMagnitude < .5f)
            {
                input.hasRay = false; input.gate.Reset(); input.ray.enabled = input.cursor.enabled = false; return;
            }
            if (!input.hasRay || Vector3.Distance(input.smoothedRay.origin, ray.origin) > .4f ||
                Vector3.Angle(input.smoothedRay.direction, ray.direction) > 45)
                input.smoothedRay = ray;
            else
            {
                float factor = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 30f);
                input.smoothedRay = new Ray(Vector3.Lerp(input.smoothedRay.origin, ray.origin, factor),
                    Vector3.Slerp(input.smoothedRay.direction, ray.direction, factor).normalized);
            }
            input.hasRay = true; ray = input.smoothedRay;
            Target selected = FindByRay(ray, out float distance);
            bool interactable = !IsTransitioning && Time.unscaledTimeAsDouble >= blockedUntil;
            int index = selected == null ? -1 : targets.IndexOf(selected);
            input.hitIndex = index;
            input.ray.enabled = !IsTransitioning && (IsOpen || selected != null);
            input.ray.SetPosition(0, ray.origin); input.ray.SetPosition(1, ray.GetPoint(selected == null ? 1.6f : distance));
            input.ray.startColor = new Color(.87f, .71f, .4f, .32f);
            input.ray.endColor = selected == null ? new Color(.76f, .8f, .87f, .45f) : new Color(1f, .86f, .48f, .95f);
            input.cursor.enabled = selected != null && !IsTransitioning;
            if (selected != null && !IsTransitioning)
            {
                Highlight(selected);
                RememberPointer(selected,ray.GetPoint(distance));
                Vector3 center = ray.GetPoint(distance) - selected.rect.forward * .003f;
                float radius = pinchReady ? Mathf.Lerp(.010f, .005f, Mathf.Clamp01(strength)) : .010f;
                for (int i = 0; i < input.cursor.positionCount; i++)
                {
                    float angle = i * Mathf.PI * 2 / input.cursor.positionCount;
                    input.cursor.SetPosition(i, center + (selected.rect.right * Mathf.Cos(angle) + selected.rect.up * Mathf.Sin(angle)) * radius);
                }
            }
            int pressed = StepPointerGate(input.gate, Time.frameCount, Time.unscaledDeltaTime, fresh, pinchReady, strength, index, interactable);
            if (pressed >= 0) Activate(targets[pressed]);
        }

        static int StepPointerGate(OceanMenuPressGate gate, int frame, float seconds, bool rayFresh,
            bool pinchReady, float strength, int target, bool interactable)
            => gate.Step(frame, seconds, rayFresh && pinchReady, strength, target, interactable);

        Target FindByRay(Ray ray, out float nearest, bool includeDisabled = false)
        {
            Target selected = null; nearest = 4f;
            foreach (Target target in targets)
            {
                if ((!includeDisabled && !target.enabled) || !target.rect.gameObject.activeInHierarchy) continue;
                float denominator = Vector3.Dot(ray.direction, target.rect.forward);
                if (denominator <= .0001f) continue;
                float distance = Vector3.Dot(target.rect.position - ray.origin, target.rect.forward) / denominator;
                if (distance < .02f || distance > nearest) continue;
                Vector3 local = target.rect.InverseTransformPoint(ray.GetPoint(distance));
                if (!target.rect.rect.Contains(new Vector2(local.x, local.y))) continue;
                selected = target; nearest = distance;
            }
            return selected;
        }

        void ProcessController(XRNode node, ControllerInput input)
        {
            if (!input.device.isValid) input.device = InputDevices.GetDeviceAtXRNode(node);
            if (hands.TrackingSpace == null || !input.device.isValid ||
                !input.device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked ||
                !input.device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 localPosition) ||
                !input.device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion localRotation))
            { input.triggerArmed = false; input.ray.enabled = false; return; }
            Vector3 origin = hands.TrackingSpace.TransformPoint(localPosition);
            Vector3 direction = hands.TrackingSpace.rotation * localRotation * Vector3.forward;
            if (!Finite(origin) || !Finite(direction) || direction.sqrMagnitude < .5f)
            { input.triggerArmed = false; input.ray.enabled = false; return; }
            Target selected = IsTransitioning ? null : FindByRay(new Ray(origin, direction), out _);
            float nearest = selected == null ? 1.6f : Vector3.Distance(origin,
                origin + direction * (Vector3.Dot(selected.rect.position - origin, selected.rect.forward) / Vector3.Dot(direction, selected.rect.forward)));
            input.ray.enabled = !IsTransitioning && (IsOpen || selected != null);
            input.ray.SetPosition(0, origin); input.ray.SetPosition(1, origin + direction * (selected == null ? 1.1f : nearest));
            input.ray.startColor = input.ray.endColor = selected == null ? new Color(.45f, .85f, .90f, .65f) : Gold;
            Highlight(selected);
            if (selected != null) RememberPointer(selected,origin+direction*nearest);
            input.device.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerButton);
            input.device.TryGetFeatureValue(CommonUsages.trigger, out float trigger);
            bool pressed = triggerButton || trigger > .65f;
            if (!pressed && trigger < .25f) input.triggerArmed = true;
            if (pressed && input.triggerArmed)
            { input.triggerArmed = false; if (selected != null) Activate(selected); }
        }

        void Highlight(Target target)
        {
            if (target == null) return;
            buttonHoveredThisFrame = true;
            target.hovered = true;
        }
        static void RememberPointer(Target target,Vector3 point)
        {
            if (target==null||!Finite(point)) return;
            target.pointerPoint=point;target.pointerFrame=Time.frameCount;
        }
        void Activate(Target target)
        {
            if (!available || IsTransitioning || target == null || !target.enabled || !target.rect.gameObject.activeInHierarchy ||
                Time.unscaledTimeAsDouble < blockedUntil || activatedFrame == Time.frameCount) return;
            activatedFrame = Time.frameCount; blockedUntil = Time.unscaledTimeAsDouble + .30;
            worldInputUntil = Time.unscaledTimeAsDouble + .45;
#if UNITY_EDITOR
            ActivationCountForValidation++;
#endif
            Debug.Log("[CampaignUI] ray_click target=" + target.id);
            Vector3 point=target.pointerFrame==Time.frameCount?target.pointerPoint:target.rect.position;
            Quaternion rotation=target.rect.rotation;Vector3 scale=target.rect.lossyScale;
            Vector3 forward=target.rect.forward;
            float extent=Mathf.Min(target.rect.rect.width,target.rect.rect.height);
            bool hasAction=target.action!=null;
            target.action?.Invoke();
            // Page changes may hide the originating button. A single separate
            // canvas acknowledges the accepted press at its original world point.
            // No acknowledgement is emitted by rejected gates or an absent action.
            if (hasAction&&available&&isActiveAndEnabled&&clickFeedback!=null)
                clickFeedback.Play(point-forward*.004f,rotation,scale,extent,new Color(1f,.87f,.58f,.92f));
        }
        void ResetInput()
        {
            if(clickFeedback!=null)clickFeedback.Cancel();
            if (hands != null) { hands.SetMenuHandTargeted(HandType.HandLeft, false); hands.SetMenuHandTargeted(HandType.HandRight, false); }
            unreachableSeconds = 0f;
            blockedUntil = Time.unscaledTimeAsDouble + .35;
            leftHand.gate.Reset(); rightHand.gate.Reset();
            leftHand.hasRay = rightHand.hasRay = false;
            if (leftHand.ray != null) leftHand.ray.enabled = false;
            if (rightHand.ray != null) rightHand.ray.enabled = false;
            if (leftHand.cursor != null) leftHand.cursor.enabled = false;
            if (rightHand.cursor != null) rightHand.cursor.enabled = false;
            leftController.triggerArmed = rightController.triggerArmed = false;
            if (leftController.ray != null) leftController.ray.enabled = false;
            if (rightController.ray != null) rightController.ray.enabled = false;
        }

        Transform PageRoot(Page value)
        {
            var root = new GameObject("Page " + value, typeof(RectTransform));
            root.transform.SetParent(panelRoot.transform, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(720, 530);
            pages.Add(value, root); return root.transform;
        }
        GameObject MakeCanvas(string name, Vector2 size)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            ((RectTransform)root.transform).sizeDelta = size;
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = view; canvas.overrideSorting = true; canvas.sortingOrder = 32758;
            return root;
        }
        Text Label(Transform parent, string name, string value, int size, Vector2 position, Vector2 dimensions, bool title = false)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Text)); root.transform.SetParent(parent, false);
            Text text = root.GetComponent<Text>(); text.font = title ? titleFont : bodyFont;
            text.material = uiMaterial; text.fontSize = size; OceanLanguage.Bind(text, value); text.color = Ink;
            text.alignment = TextAnchor.MiddleCenter; text.supportRichText = false; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            // Noto's ascent+descent is about 1.448 em. Truncate drops a whole line
            // when its box is even slightly shorter; keep explicit single-line slack.
            if (!title) dimensions.y = Mathf.Max(dimensions.y, Mathf.Ceil(size * 1.55f + 2f));
            text.rectTransform.anchoredPosition = position; text.rectTransform.sizeDelta = dimensions; return text;
        }
        Image AddImage(Transform parent, string name, Vector2 position, Vector2 dimensions, Color color)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image)); root.transform.SetParent(parent, false);
            var image = root.GetComponent<Image>(); image.sprite = roundedSprite; image.type = Image.Type.Sliced;
            image.material = imageMaterial; image.color = color; image.raycastTarget = false;
            image.rectTransform.anchoredPosition = position; image.rectTransform.sizeDelta = dimensions; return image;
        }
        Target Button(Transform parent, string id, string text, Vector2 position, Vector2 dimensions, Action action, bool primary = false)
        {
            Color normal = primary ? new Color(.085f, .29f, .29f, 1) : ButtonColor;
            Image image = AddImage(parent, "Button " + id, position, dimensions, normal);
            var target = new Target { id = id, rect = image.rectTransform, image = image, action = action, normal = normal };
            target.border = Ornament(image.transform, "Gold button border", Vector2.zero, dimensions - Vector2.one * 2, 1.3f, 12f, false);
            target.label = Label(image.transform, "Button label", text, 25, Vector2.zero, dimensions - new Vector2(20, 6));
            if (primary) target.label.color = Gold;
            targets.Add(target); return target;
        }
        static void SetEnabled(Target target, bool enabled)
        { target.enabled = enabled; target.label.color = enabled ? Ink : new Color(.35f, .49f, .51f); }

        OceanMenuOrnament Ornament(Transform parent, string name, Vector2 position, Vector2 dimensions, float thickness, float radius, bool motion)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(OceanMenuOrnament)); root.transform.SetParent(parent, false);
            var ornament = root.GetComponent<OceanMenuOrnament>(); ornament.material = imageMaterial;
            ornament.rectTransform.anchoredPosition = position; ornament.rectTransform.sizeDelta = dimensions;
            ornament.SetAppearance(new Color(1f, .79f, .37f, .84f), thickness, radius);
            ornament.SetState(1f, 0f); ornament.SetMotion(motion, .075f); return ornament;
        }

        LineRenderer BuildCursor(string name)
        {
            var line = BuildRay(name); line.loop = true; line.positionCount = 24;
            line.startWidth = line.endWidth = .0017f;
            line.startColor = line.endColor = new Color(1f, .91f, .61f, .96f); return line;
        }

        void BuildClickFeedback()
        {
            var root=new GameObject("Accepted menu press acknowledgement",typeof(RectTransform),typeof(Canvas),typeof(OceanMenuClickFeedback));
            // Keep the point fixed if the panel collapses toward the head-following
            // HUD. Lifetime is explicitly owned by this panel, not the page hierarchy.
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
            canvas.worldCamera=view;canvas.overrideSorting=true;canvas.sortingOrder=32760;
            clickFeedback=root.GetComponent<OceanMenuClickFeedback>();clickFeedback.material=rayMaterial;
            clickFeedback.raycastTarget=false;clickFeedback.Cancel();
        }

        void BuildRoundedSprite()
        {
            const int size = 64;
            roundedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Campaign rounded card", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x - 31.5f) - 18), dy = Mathf.Max(0, Mathf.Abs(y - 31.5f) - 18);
                pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(13 - Mathf.Sqrt(dx * dx + dy * dy)));
            }
            roundedTexture.SetPixels32(pixels); roundedTexture.Apply(false, true);
            roundedSprite = Sprite.Create(roundedTexture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0,
                SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16));
        }

        void BuildSpeciesModel(Transform anchor, int index)
        {
            GameObject model;
            if (index < 10) model = CollectibleVisuals.CreateShell(anchor, index);
            else
            {
                string path = index == SharkIndex ? "Fish/15_great_white_shark" : OceanLevelCatalog.FishPaths[index - FishOffset];
                GameObject prefab = Resources.Load<GameObject>(path);
                if (prefab == null) throw new InvalidOperationException("Collection preview missing " + path);
                model = Instantiate(prefab, anchor, false);
            }
            model.name = "Collection preview " + index;
            speciesModels[index] = model;
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) { collider.enabled = false; DestroyOwned(collider); }
            foreach (Animator animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            model.transform.localRotation = index < 10 ? Quaternion.Euler(-67, 18, 8) : Quaternion.Euler(0, 68, -7);
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false; var localBounds = new Bounds();
            foreach (Renderer renderer in renderers)
            {
                Bounds b = renderer.localBounds;
                Matrix4x4 matrix = anchor.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (!hasBounds) { localBounds = new Bounds(point, Vector3.zero); hasBounds = true; } else localBounds.Encapsulate(point);
                }
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material original = materials[slot]; if (original == null) continue;
                    if (!previewMaterials.TryGetValue(original, out Material clone))
                    {
                        clone = Own(new Material(original) { name = original.name + " collection preview", renderQueue = 4140 });
                        if (clone.HasProperty("_ZTest")) clone.SetInt("_ZTest", (int)CompareFunction.Always);
                        if (clone.HasProperty("_ZWrite")) clone.SetInt("_ZWrite", 0);
                        previewMaterials.Add(original, clone);
                    }
                    materials[slot] = clone;
                }
                renderer.sharedMaterials = materials; renderer.sortingOrder = 32759;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            }
            if (hasBounds)
            {
                float scale = Mathf.Min(170 / Mathf.Max(.0001f, localBounds.size.x), 88 / Mathf.Max(.0001f, localBounds.size.y));
                model.transform.localScale *= scale; model.transform.localPosition -= localBounds.center * scale;
            }
        }

        LineRenderer BuildRay(string name)
        {
            var root = new GameObject(name, typeof(LineRenderer)); root.transform.SetParent(transform, false);
            LineRenderer line = root.GetComponent<LineRenderer>(); line.sharedMaterial = rayMaterial; line.useWorldSpace = true;
            line.positionCount = 2; line.startWidth = line.endWidth = .0015f;
            line.sortingOrder = 32760;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false; line.enabled = false; return line;
        }
        Material Own(Material material) { ownedMaterials.Add(material); return material; }
        static int Count(int mask) { int count = 0; while (mask != 0) { count += mask & 1; mask >>= 1; } return count; }
        static string Stars(int value) => value == 3 ? "★★★" : value == 2 ? "★★☆" : "☆☆☆";
        static string Clock(float seconds) { int value = Mathf.CeilToInt(Mathf.Max(0, seconds)); return (value / 60).ToString("00") + ":" + (value % 60).ToString("00"); }
        static bool Finite(Vector3 point) => !float.IsNaN(point.x) && !float.IsInfinity(point.x) && !float.IsNaN(point.y) &&
            !float.IsInfinity(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.z);
        static void DestroyOwned(UnityEngine.Object item)
        { if (item != null) { if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); } }
        void OnDisable()
        {
            CancelEntryReplay();
            ResetInput();
            SetTutorialExitVisible(false);
            if (panelRoot != null) panelRoot.SetActive(false);
            if (hudRoot != null) hudRoot.SetActive(false);
        }
        void OnEnable()
        {
            if (!initialized) return;
            panelRoot.SetActive(available && (IsOpen || IsTransitioning)); hudRoot.SetActive(available && !IsOpen && !IsTransitioning);
            RefreshTutorialExitPresentation();
        }

#if UNITY_EDITOR
        public int FullRefreshCountForValidation { get; private set; }
        public int ProgressRefreshCountForValidation { get; private set; }
        public void SkipTutorialForValidation() { tutorial?.Skip(false); }
        public void DriveMenuRayForValidation(bool left, Ray ray, float strength, bool fresh = true, bool pinchReady = true)
        {
            HandInput input = left ? leftHand : rightHand;
            input.validationActive = true; input.validationFresh = fresh; input.validationPinchFresh = pinchReady;
            input.validationRay = ray; input.validationStrength = strength;
        }
        public void SetGuideHintForValidation(int index){guideHint=Math.Max(0,index);guideLevel=state.Current==null?0:state.Current.level;nextGuideHint=Time.unscaledTimeAsDouble+8;RefreshHud();}
        public int GuideHintForValidation=>guideHint;
        public string GuideActionForValidation=>hudProgress.text;
        public string GuideDetailsForValidation=>hudTime.text;
        public RectTransform GuideRectForValidation=>(RectTransform)hudRoot.transform;
        public void ClearValidationRays()
        { leftHand.validationActive = rightHand.validationActive = false; ResetInput(); }
        public int ActivationCountForValidation { get; private set; }
        public OceanMenuClickFeedback ClickFeedbackForValidation=>clickFeedback;
        public bool ActivateForValidation(string id)
        {
            foreach(Target target in targets)
                if(target.id==id&&target.rect.gameObject.activeInHierarchy)
                {int before=ActivationCountForValidation;Activate(target);return ActivationCountForValidation!=before;}
            return false;
        }
        public bool IsTargetHoveredForValidation(string id)
        { foreach (Target target in targets) if (target.id == id) return target.hovered; return false; }
        public float ExpansionForValidation => openAmount;
        public readonly struct ButtonInfoForValidation
        {
            public readonly string id, title;
            public readonly RectTransform rect;
            public readonly bool enabled;
            public ButtonInfoForValidation(string id, string title, RectTransform rect, bool enabled)
            { this.id = id; this.title = title; this.rect = rect; this.enabled = enabled; }
        }
        public List<ButtonInfoForValidation> GetButtonsForValidation()
        {
            var result = new List<ButtonInfoForValidation>();
            foreach (Target target in targets)
                if (target.rect.gameObject.activeInHierarchy)
                    result.Add(new ButtonInfoForValidation(target.id, target.label.text, target.rect, target.enabled));
            return result;
        }
        public void ShowCollectionForValidation(int pageIndex = 0)
        { if (!IsOpen) OpenMenu(); ShowCollectionPage(Mathf.Clamp(pageIndex, 0, CollectionPages.Length - 1)); }
        public void ShowLevelPageForValidation(int index)
        { if (!IsOpen) OpenMenu(); unavailableLevelNotice = null; levelPage = Mathf.Clamp(index, 0, (OceanLevelCatalog.Count - 1) / PageSize); ShowPage(Page.Levels); }
        public int ActiveSpeciesModelsForValidation
        { get { int count = 0; foreach (var model in speciesModels) if (model != null && model.activeInHierarchy) count++; return count; } }
        public int InstantiatedSpeciesModelsForValidation
        { get { int count = 0; foreach (var model in speciesModels) if (model != null) count++; return count; } }
        public int LevelPageForValidation => levelPage;
        // Invoke the visible button's real callback, including its confirmation page.
        // This bypasses only physical input/cooldown; it does not mutate campaign state.
        public bool ClickForValidation(string action, int level = 0)
        {
            string id = action == "start_level" ? action + "_" + level : action;
            foreach (Target target in targets)
                if (target.id == id && target.enabled && target.rect.gameObject.activeInHierarchy)
                { target.action?.Invoke(); return true; }
            return false;
        }
#endif
        void OnDestroy()
        {
            if (disposed) return; disposed = true;
            OceanLanguage.Changed -= RefreshLanguage;
            CancelEntryReplay();
            if (hands != null) hands.SetMenuLocomotionPaused(false);
            DestroyOwned(panelRoot); DestroyOwned(hudRoot);
            DisposeTutorialExit();
            if (clickFeedback != null) DestroyOwned(clickFeedback.gameObject);
            if (leftHand.ray != null) DestroyOwned(leftHand.ray.gameObject);
            if (rightHand.ray != null) DestroyOwned(rightHand.ray.gameObject);
            if (leftHand.cursor != null) DestroyOwned(leftHand.cursor.gameObject);
            if (rightHand.cursor != null) DestroyOwned(rightHand.cursor.gameObject);
            if (leftController.ray != null) DestroyOwned(leftController.ray.gameObject);
            if (rightController.ray != null) DestroyOwned(rightController.ray.gameObject);
            foreach (Material material in ownedMaterials) DestroyOwned(material);
            DestroyOwned(roundedSprite); DestroyOwned(roundedTexture);
        }
    }
}

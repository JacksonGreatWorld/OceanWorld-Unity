using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Connects durable rules to the existing world, tracked interactions, menu and rewards.</summary>
    [DefaultExecutionOrder(90)]
    public sealed partial class OceanCampaignController : MonoBehaviour
    {
        public OceanCampaignState State { get; private set; }
        public OceanCampaignPanel Panel { get; private set; }
        public OceanCampaignCreatures Creatures { get; private set; }
        public OceanNaturalist Naturalist { get; private set; }
        public OceanMechanicRuntime Mechanics { get; private set; }
        public bool GameplayAllowed => initialized && OceanCampaignAvailability.IsOpen(State.Current.level) && !environmentTransitioning && !environmentFaulted && hunt.CoreActive && !Panel.IsOpen && !Panel.TutorialActive && !Panel.WorldInputBlocked && !leaderboard.IsOpen && State.CanCollect && Time.frameCount > resumedFrame;
        public bool EnvironmentTransitioning => environmentTransitioning;
        public bool LastSaveSucceeded { get; private set; }
        public string SaveDirectory { get; private set; }
        TreasureHunt hunt;
        HandSwimController hands;
        TreasureFeedback feedback;
        OceanLeaderboardBridge leaderboard;
        UnderwaterWorld world;
        Camera view;
        OceanLevelRun committedScoreRun;
        Vector3 spawn;
        bool initialized, configured, warnedSave, writable = true;
        bool preparingEntryActors, entryPresentationShown, pendingEntryAmmoFailure;
        bool entryEnvironmentPrepared, environmentTransitioning, environmentFaulted;
        Coroutine levelSwitchCoroutine;
        IEnumerator levelSwitchOperation;
        IEnumerator activeSwitchStage;
        OceanEnvironmentTransition mapPassage;
        public bool ActorsPrepared => configured;
        int lastStars, resumedFrame = -1, lastSecond = -1, lastAmmo = -1;
        bool lastTimedOut, lastFailed, lastToolHeld;
        double nextSave, nextScoreRetry, resultAt = double.PositiveInfinity;
        string pendingSaveNotice;

        public void Initialize(TreasureHunt owner, UnderwaterWorld world, Camera view,
            HandSwimController controller, TreasureFeedback presentation, Vector3 spawnPoint)
        {
            hunt = owner; this.world = world; this.view = view; hands = controller; feedback = presentation; spawn = spawnPoint;
            SaveDirectory = Application.persistentDataPath;
#if UNITY_EDITOR
            // Rendered integration tests use a private folder, never the developer's play save.
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-codexCampaignSaveDir") SaveDirectory = Path.GetFullPath(args[i + 1]);
#endif
            bool loaded = OceanSaveStore.TryLoad(SaveDirectory, out OceanSaveData data, out OceanSaveLoadStatus status, out string error);
            if (!loaded)
            {
                writable = false;
                pendingSaveNotice = "存档暂时无法读取。菜单顶部可重新读取，原存档保留。";
                Debug.LogWarning("[Campaign] save_load_failed status=" + status + " detail=" + error);
            }
            State = new OceanCampaignState(loaded ? data : null);
            if (State.Current == null) State.StartLevel(1);
            if (loaded && !OceanCampaignAvailability.IsOpen(State.Current.level))
                committedScoreRun = OceanCampaignState.Copy(State.Save).currentRun;
            lastStars = State.Stars; lastTimedOut = State.TimedOut; lastFailed = State.Failed;
            Creatures = gameObject.AddComponent<OceanCampaignCreatures>();
            Creatures.Initialize(world, view, hands, State, CreatureEvent);
            Mechanics = gameObject.AddComponent<OceanMechanicRuntime>();
            Mechanics.Initialize(world, view, hands, State, MechanicEvent);
            Panel = gameObject.AddComponent<OceanCampaignPanel>();
            Panel.Initialize(view, hands, State, HandleAction, OceanInteractiveTutorial.ShouldOfferAutomatically(loaded,status));
            Panel.SetSaveRecoveryStatus(!loaded, status, false);
            leaderboard = gameObject.AddComponent<OceanLeaderboardBridge>();
            leaderboard.Initialize(view, hands, SaveDirectory, () => { Panel.SetAvailable(hunt.CoreActive); Panel.OpenMenu(); });
            Naturalist = gameObject.AddComponent<OceanNaturalist>();
            Naturalist.Initialize(world, this, hunt, view, hands, feedback, leaderboard, SaveDirectory,
                () => { Panel.Refresh(); RefreshAchievements(true); });
            Panel.BindNaturalist(Naturalist);
            InitializeAchievements(loaded ? data : null);
            Panel.CloseMenu(); Panel.SetAvailable(false);
            hands.SetMenuLocomotionPaused(false);
            feedback.SetJournalVisible(false);
            initialized = true;
            RefreshAchievements(false);
            Debug.Log($"[Campaign] loaded status={status} level={State.Current.level} seconds={State.Current.remainingSeconds:F1} shells={State.Current.shellMask} fish={State.Current.fishMask} coin={State.Current.coinCollected} stars={State.Stars}");
            SaveNow();
        }

        void LateUpdate()
        {
            if (!initialized) return;
            bool available = hunt.CoreActive;
            Panel.SetAvailable(available && !leaderboard.IsOpen && !environmentTransitioning);
            ReconcileMenuPause(available, OceanXRPlatform.IsVisionOS);
            if (available && committedScoreRun != null && Time.unscaledTimeAsDouble >= nextScoreRetry)
            { nextScoreRetry = Time.unscaledTimeAsDouble + 1; leaderboard.PersistRun(committedScoreRun); }
            if (available && !configured && !environmentTransitioning && !environmentFaulted) PrepareActorsForEntry();
            if (available && !entryPresentationShown)
            {
                entryPresentationShown = true;
                if (pendingEntryAmmoFailure) { pendingEntryAmmoFailure = false; lastFailed = State.Failed; SaveNow(); }
                if (!OceanCampaignAvailability.IsOpen(State.Current.level))
                { OceanEntry.DismissPlayableTitles(); Panel.ShowUnavailableLevel(State.Current.level); }
                else if (State.Failed || State.TimedOut || (OceanCampaignAvailability.IsFinalLevel(State.Current.level) && State.GoalComplete))
                { OceanEntry.DismissPlayableTitles(); Panel.ShowResult(OceanCampaignAvailability.IsFinalLevel(State.Current.level) && State.GoalComplete); }
                if (!string.IsNullOrEmpty(pendingSaveNotice))
                { OceanEntry.DismissPlayableTitles(); feedback.SetVisible(true); feedback.ShowCampaignMessage(pendingSaveNotice); pendingSaveNotice = null; }
            }
            bool playing = available && GameplayAllowed;
            State.Tick(Time.unscaledDeltaTime, playing);
            Creatures.TickGameplay(Time.unscaledDeltaTime, playing);
            Mechanics.TickGameplay(Time.unscaledDeltaTime, playing);
            if (State.TimedOut != lastTimedOut)
            {
                lastTimedOut = State.TimedOut; SaveNow();
                resultAt = Time.unscaledTimeAsDouble;
                Debug.Log($"[Campaign] timeout level={State.Current.level} failed={State.Failed} stars={State.Stars}");
            }
            if (available && Time.unscaledTimeAsDouble >= resultAt)
            {
                resultAt = double.PositiveInfinity;
                OpenResult();
            }
            if (available && State.Failed && !lastFailed)
            {
                lastFailed = true; SaveNow();
                if (double.IsPositiveInfinity(resultAt)) resultAt = Time.unscaledTimeAsDouble + .8;
                Debug.Log($"[Campaign] failed reason={State.FailureReason} shots={State.Current.spearShotsFired} hits={State.Current.sharkHits}");
            }
            int second = Mathf.CeilToInt(State.Current.remainingSeconds);
            var gun = Creatures.SpearGun;
            int ammo = gun != null ? gun.AmmoRemaining : -1;
            bool held = gun != null && gun.Held;
            if (second != lastSecond || ammo != lastAmmo || held != lastToolHeld)
            { lastSecond = second; lastAmmo = ammo; lastToolHeld = held; Panel.Refresh(); }
            if (playing && Time.unscaledTimeAsDouble >= nextSave) SaveNow();
            TickAchievementNotices();
        }

        // On AVP, native selection can switch between the two menu owners. Derive
        // the shared pause from their current state rather than retaining an orphan flag.
        // Entry/focus/world readiness continue to gate this independently.
        void ReconcileMenuPause(bool available, bool visionOS)
        {
            if (!available || !visionOS || hands == null || Panel == null) return;
            bool expected = environmentTransitioning || Panel.IsOpen || (leaderboard != null && leaderboard.IsOpen);
            if (hands.MenuLocomotionPaused == expected) return;
            Debug.Log($"[CampaignUI] menu_pause_reconcile old={hands.MenuLocomotionPaused} new={expected} campaignOpen={Panel.IsOpen} leaderboardOpen={leaderboard != null && leaderboard.IsOpen}");
            hands.SetMenuLocomotionPaused(expected);
        }

        // Runs under the opaque startup cover, after loading the saved level and before
        // the world becomes playable. Calling it again never rebuilds a prepared scene.
        public IEnumerator PrepareEnvironmentForEntry()
        {
            if (entryEnvironmentPrepared) yield break;
            if (!initialized) throw new InvalidOperationException("Campaign must initialize before environment preparation.");
            bool prepared = false;
            yield return world.PrepareEnvironmentForLevel(OceanCampaignAvailability.EntryLevel(State.Current.level), value => prepared = value);
            if (!prepared) throw new InvalidOperationException("Saved level environment could not be prepared.");
            spawn = world.ActiveSpawn;
            Physics.SyncTransforms();
            if (OceanCampaignAvailability.IsOpen(State.Current.level))
            {
                yield return hunt.PrepareExpeditionHabitat(spawn);
                hunt.RebuildDiscoveries(spawn, State);
            }
            entryEnvironmentPrepared = true;
        }

        // The revealed world must already contain the saved level's actors and contact bounds.
        public void PrepareActorsForEntry()
        {
            if (configured) return;
            if (!initialized || !entryEnvironmentPrepared || State == null || State.Current == null || Creatures == null)
                throw new InvalidOperationException("Campaign must initialize before actor preparation.");
            if (!OceanCampaignAvailability.IsOpen(State.Current.level))
            { configured = true; return; }
            double start = Time.realtimeSinceStartupAsDouble;
            Debug.Log($"[OceanStartup] stage=campaign_actors event=begin t={start:F3} frame={Time.frameCount}");
            preparingEntryActors = true;
            try
            {
                Physics.SyncTransforms();
                Creatures.ConfigureLevel(spawn);
                Mechanics.ConfigureLevel(spawn);
                Physics.SyncTransforms();
                configured = true;
            }
            finally { preparingEntryActors = false; }
            double end = Time.realtimeSinceStartupAsDouble;
            Debug.Log($"[OceanStartup] stage=campaign_actors event=end elapsed_ms={(end-start)*1000:F3} t={end:F3} frame={Time.frameCount}");
        }

        public bool CollectPickup(TreasureHunt.Pickup pickup) => CollectPickup(pickup, out _);

        // A hint handles this grasp without collecting or hiding the physical object.
        public bool CollectPickup(TreasureHunt.Pickup pickup, out bool handled)
        {
            handled = false;
            if (!GameplayAllowed) return false;
            if (pickup.kind >= 0 && State.Definition.OrderedShell &&
                (State.Current.shellMask & (1 << pickup.kind)) == 0 && pickup.kind != State.NextShellSpecies)
            {
                OceanEntry.DismissPlayableTitles();
                if (State.NextShellSpecies >= 0) feedback.ShowCampaignMessage("请先寻找：" + TreasureHunt.SpeciesNames[State.NextShellSpecies]);
                handled = true;
                return false;
            }
            bool duplicate = pickup.kind >= 0 && (State.Current.shellMask & (1 << pickup.kind)) != 0;
            bool accepted = pickup.kind < 0 ? State.CollectCoin(pickup.id) : State.CollectShell(pickup.id, pickup.kind);
            if (!accepted) return false;
            handled = true;
            OceanEntry.DismissPlayableTitles();
            if (pickup.kind < 0)
            {
                feedback.ShowHiddenReward(State.CoinCount, State.CoinTargetCount);
                Debug.Log($"[Campaign] hidden_coin collected={State.CoinCount}/{State.CoinTargetCount}");
            }
            else
            {
                string name = TreasureHunt.SpeciesNames[pickup.kind];
                int count = CountBits(State.Current.shellMask), goal = CountBits(State.ShellTargetMask);
                feedback.ShowCampaignMessage(duplicate ? $"该贝壳你已经拥有：{name}（{count}/{goal}）" :
                    $"你收集到1种贝壳：{name}（{count}/{goal}）", pickup: true, duplicate: duplicate);
            }
            Debug.Log($"[Campaign] pickup level={State.Current.level} id={pickup.id} kind={pickup.kind} duplicate={duplicate} shellMask={State.Current.shellMask} coin={State.Current.coinCollected} stars={State.Stars}");
            Changed(); return true;
        }
        public bool TryCreatureGrasp(Vector3 palm, bool left) => GameplayAllowed && Creatures.TryGrasp(palm, left);

        void MechanicEvent(string message)
        {
            if (!GameplayAllowed) return;
            OceanEntry.DismissPlayableTitles();
            feedback.ShowCampaignMessage(message, pickup: State.MechanicComplete);
            Debug.Log($"[Expedition] level={State.Current.level} progress={State.Current.mechanicProgress}/{State.Definition.MechanicTarget} stage={State.Current.mechanicStage} mistakes={State.Current.mechanicMistakes}");
            Changed();
        }
        void CreatureEvent(string action, int value)
        {
            // Recover an exhausted saved weapon under the entry cover; defer its presentation.
            if (preparingEntryActors && action == "ammo-exhausted")
            { pendingEntryAmmoFailure = true; return; }
            OceanEntry.DismissPlayableTitles();
            switch (action)
            {
                case "fish": feedback.ShowCampaignMessage("收集到：" + OceanCampaignCreatures.FishNames[value], pickup: true); break;
                case "tool": feedback.ShowCampaignMessage($"已拥有渔枪！单手握拳，翘拇指、伸食指显枪；弯曲食指射击。本关共{State.ShotLimit}发。", pickup: true); break;
                case "shark-hit":
                    string hitName = Creatures.SharkDisplayName(Creatures.LastInteractedSharkId);
                    feedback.ShowCampaignMessage(value >= State.SharkHitTarget ?
                        hitName + " 已眩晕，靠近握拳抓取！" : $"{hitName} 命中 {value}/{State.SharkHitTarget}", pickup: true, priority: true); break;
                case "shark": feedback.ShowCampaignMessage("你成功抓到了" + Creatures.SharkDisplayName(Creatures.LastInteractedSharkId) + "！", pickup: true); break;
                case "fish-order":
                    if (value >= 0) feedback.ShowCampaignMessage("请先抓到：" + OceanCampaignCreatures.FishNames[value]);
                    return;
                case "fish-owned": feedback.ShowCampaignMessage("该鱼你已经拥有：" + OceanCampaignCreatures.FishNames[value]); return;
                case "need-tool": feedback.ShowCampaignMessage("缺少工具，先找到海底的渔枪。"); return;
                case "shark-remaining": feedback.ShowCampaignMessage(Creatures.SharkDisplayName(Creatures.LastInteractedSharkId) + $" 还需命中 {value} 次，才能抓取。"); return;
                case "tool-fired":
                    Panel.Refresh(); SaveNow();
                    if (value == 0) feedback.ShowCampaignMessage("最后一发已射出，等待命中结果。");
                    return;
                case "tool-recovery":
                    if (!preparingEntryActors)
                        feedback.ShowCampaignMessage("鱼叉已系回身前 · 靠近握抓，或指向后松手再捏合回收。回收不会补弹。");
                    Panel.Refresh(); SaveNow(); return;
                case "tool-recovered":
                    feedback.ShowCampaignMessage($"鱼叉已回收 · 余弹{value}/{State.ShotLimit}。重新伸直食指准备下一发。", pickup: true);
                    Panel.Refresh(); SaveNow(); return;
                case "tool-miss":
                    if (State.SpearAmmoRemaining > 0) feedback.ShowCampaignMessage(State.RequiresSpearRecovery ? "未命中 · 等待鱼叉回到身前并回收后再试。" : "未命中，瞄准后再次扣动扳机。");
                    return;
                case "ammo-exhausted":
                    feedback.ShowCampaignMessage($"{State.ShotLimit}发已用完，挑战失败。可以重新开始本关。", priority: true);
                    Panel.Refresh(); SaveNow(); resultAt = Time.unscaledTimeAsDouble + .8; return;
                default: return;
            }
            Debug.Log($"[Campaign] {action} level={State.Current.level} value={value} fishMask={State.Current.fishMask} hits={State.Current.sharkHits} stars={State.Stars}");
            Changed();
        }
        void Changed()
        {
            if (State.Stars > lastStars)
            {
                if (lastStars == 0)
                {
                    feedback.ShowCampaignMessage(OceanCampaignAvailability.IsFinalLevel(State.Current.level) ? "前20关冒险完成，更多海域敬请期待！" : "本关目标达成！", true,
                        priority: true, fanfare: !OceanCampaignAvailability.IsFinalLevel(State.Current.level));
                    if (OceanCampaignAvailability.IsFinalLevel(State.Current.level)) feedback.PlayEnding();
                }
                lastStars = State.Stars;
                resultAt = Time.unscaledTimeAsDouble + (feedback.IsCelebrating ? TreasureFeedback.CelebrationDuration + .35 : 3.2);
            }
            Panel.Refresh(); SaveNow();
        }

        void OpenResult()
        {
            OceanEntry.DismissPlayableTitles();
            hands.SetMenuLocomotionPaused(true);
            Panel.ShowResult(OceanCampaignAvailability.IsFinalLevel(State.Current.level) && State.GoalComplete);
            SaveNow();
        }

        public void HandleAction(string action, int level)
        {
            if (!initialized || environmentTransitioning) return;
            OceanEntry.DismissPlayableTitles();
            Debug.Log($"[CampaignUI] action={action} level={level} current={State.Current.level}");
            switch (action)
            {
                case "retry_save_load": RetrySaveLoadReadOnly(); break;
                case "toggle_performance":
                    PerformanceHud.SetPreferenceVisible(!PerformanceHud.PreferenceVisible);
                    Panel.Refresh(); break;
                case "menu":
                    resultAt = double.PositiveInfinity;
                    hands.SetMenuLocomotionPaused(true); Panel.OpenMenu(); SaveNow(); break;
                case "leaderboard":
                    resultAt = double.PositiveInfinity; SaveNow();
                    Panel.CloseMenu(); Panel.SetAvailable(false); leaderboard.Open(); break;
                case "resume":
                    if (!OceanCampaignAvailability.IsOpen(State.Current.level)) { Panel.ShowUnavailableLevel(State.Current.level); break; }
                    if (environmentFaulted) { feedback.ShowCampaignMessage("请重新选择关卡，完成场景恢复后再继续。"); break; }
                    if (!State.CanCollect) { OpenResult(); break; }
                    resultAt = double.PositiveInfinity;
                    Panel.CloseMenu(); hands.SetMenuLocomotionPaused(false); resumedFrame = Time.frameCount;
                    break;
                case "start_level":
                    if (level >= 1 && level <= OceanCampaignState.LevelCount) BeginLevel(level);
                    break;
                case "restart_level": BeginLevel(State.Current.level); break;
                case "restart_game": BeginLevel(1); break;
                case "next_level":
                    if (!OceanCampaignAvailability.IsOpen(State.Current.level)) { Panel.ShowUnavailableLevel(State.Current.level); break; }
                    if (State.GoalComplete && State.Current.level < OceanCampaignState.LevelCount) BeginLevel(State.Current.level + 1);
                    break;
                case "quit": SaveNow(); Application.Quit(); break;
            }
        }
        void BeginLevel(int level)
        {
            if (environmentTransitioning || level < 1 || level > OceanCampaignState.LevelCount) return;
            if (!OceanCampaignAvailability.IsOpen(level))
            { Panel.ShowUnavailableLevel(level); return; }
            // Only local durability is required. Network availability never gates a restart.
            if (writable)
            {
                bool archivedRun = !OceanCampaignAvailability.IsOpen(State.Current.level);
                if (!archivedRun) SaveNow();
                if ((!archivedRun && !LastSaveSucceeded) || !leaderboard.PersistRun(committedScoreRun))
                { feedback.ShowCampaignMessage("排行榜成绩暂未存入本机，请稍后重试。"); return; }
            }
            environmentTransitioning = true;
            levelSwitchOperation = SwitchLevel(level);
            levelSwitchCoroutine = StartCoroutine(levelSwitchOperation);
        }

        IEnumerator SwitchLevel(int level)
        {
            OceanLevelRun previousRun = State.Current;
            Vector3 previousSpawn = spawn, previousEye = view.transform.position, previousForward = view.transform.forward;
            bool wasSuppressed = hands.InteractionSuppressed;
            bool finalized = false;
            double started = Time.realtimeSinceStartupAsDouble;
            try
            {
                leaderboard.Close(); Panel.SetAvailable(false);
                hands.SetMenuLocomotionPaused(true); hands.SetInteractionSuppressed(true);
                resultAt = double.PositiveInfinity;
                feedback.SetVisible(false); feedback.SetVisible(hunt.CoreActive);
                feedback.ShowCampaignMessage(level > 20 ? "正在前往" + OceanLevelCatalog.Get(level).Name + "…" :
                    level >= OceanEnvironmentProfile.SanctuaryFirstLevel ? "正在前往珊瑚秘境…" : "正在前往阳光海湾…");
                Exception failure = null;
                yield return RunOwnedSwitchStage(PreparePassageAndLevel(previousRun.level, level), error => failure = error);
                if (failure != null)
                {
                    Debug.LogWarning($"[Campaign] environment_switch_failed level={level} detail={failure}");
                    // Roll back the same state object retained by the panel and creature controller.
                    // The original run ID and consumed IDs survive; failed attempts earn no score.
                    State.Save.currentRun = previousRun; spawn = previousSpawn;
                    Exception rollbackFailure = null;
                    yield return RunOwnedSwitchStage(RestoreLevel(previousRun.level), error => rollbackFailure = error);
                    environmentFaulted = rollbackFailure != null;
                    if (rollbackFailure != null) Debug.LogWarning("[Campaign] environment_rollback_failed detail=" + rollbackFailure);
                    else hands.TryRelocateHead(previousEye, previousForward);
                }
                else environmentFaulted = false;
                Exception revealFailure = null;
                yield return RunOwnedSwitchStage(FinishPassage(), error => revealFailure = error);
                if (revealFailure != null)
                {
                    Debug.LogWarning("[Campaign] transition_reveal_failed detail=" + revealFailure);
                    world.AbortEnvironmentTransition();
                }
                lastStars = State.Stars; lastTimedOut = State.TimedOut; lastFailed = State.Failed; lastSecond = -1;
                finalized = true;
                environmentTransitioning = false;
                hands.SetInteractionSuppressed(wasSuppressed);
                Panel.SetAvailable(hunt.CoreActive); Panel.Refresh();
                if (failure == null)
                {
                    Panel.CloseMenu(); hands.SetMenuLocomotionPaused(false);
                    resumedFrame = Time.frameCount; SaveNow();
                    Debug.Log($"[Campaign] start level={level} environment={world.ActiveEnvironment} seconds={State.Current.remainingSeconds:F0} unlimited={State.UnlimitedTime} switch_ms={(Time.realtimeSinceStartupAsDouble-started)*1000:F1}");
                }
                else
                {
                    Panel.OpenMenu();
                    feedback.ShowCampaignMessage(environmentFaulted ? "场景暂时无法恢复，请重新选择关卡重试。原进度已保留。" : "场景暂未准备好，已返回原关卡。可以重新选择重试。");
                }
            }
            finally
            {
                CleanupMapPassage();
                levelSwitchOperation = null; levelSwitchCoroutine = null;
                environmentTransitioning = false;
                if (!finalized)
                {
                    // A cancelled owner or unexpected UI exception must not commit the new
                    // run, strand an opaque veil or permanently suppress menu hand input.
                    State.Save.currentRun = previousRun; spawn = previousSpawn;
                    environmentFaulted = true;
                    if (world != null) world.AbortEnvironmentTransition();
                }
                if (hands != null) hands.SetInteractionSuppressed(wasSuppressed);
                if (!finalized && Panel != null)
                {
                    Panel.SetAvailable(hunt != null && hunt.CoreActive); Panel.OpenMenu();
                }
            }
        }

        IEnumerator PreparePassageAndLevel(int previousLevel, int nextLevel)
        {
            if (OceanEnvironmentTransition.Required(OceanCampaignAvailability.EntryLevel(previousLevel), nextLevel))
            {
                mapPassage = OceanEnvironmentTransition.Create(view);
                world.ExternalTransitionCoverActive = true;
                Debug.Log($"[Campaign] map_passage from={previousLevel} to={nextLevel} input_paused=true");
                yield return mapPassage.Cover();
            }
            yield return PrepareLevel(nextLevel);
        }

        IEnumerator FinishPassage()
        {
            yield return world.FinishEnvironmentTransition();
            if (mapPassage != null && !mapPassage.Cancelled) yield return mapPassage.Reveal();
        }

        void CleanupMapPassage()
        {
            if (world != null) world.ExternalTransitionCoverActive = false;
            if (mapPassage == null) return;
            mapPassage.Cancel(); Destroy(mapPassage.gameObject); mapPassage = null;
        }

        void CancelLevelSwitch()
        {
            IEnumerator operation = levelSwitchOperation;
            IEnumerator stage = activeSwitchStage;
            Coroutine coroutine = levelSwitchCoroutine;
            levelSwitchOperation = null; levelSwitchCoroutine = null; activeSwitchStage = null;
            if (coroutine != null) StopCoroutine(coroutine);
            // Explicit disposal guarantees the transaction's finally runs on owner disable.
            try { (stage as IDisposable)?.Dispose(); }
            finally
            {
                try { (operation as IDisposable)?.Dispose(); }
                finally { CleanupMapPassage(); }
            }
        }

        IEnumerator RunOwnedSwitchStage(IEnumerator operation, Action<Exception> failed)
        {
            IEnumerator stage = RunTransitionOperation(operation, failed); activeSwitchStage = stage;
            try { while (stage.MoveNext()) yield return stage.Current; }
            finally
            {
                (stage as IDisposable)?.Dispose();
                if (ReferenceEquals(activeSwitchStage, stage)) activeSwitchStage = null;
            }
        }

        IEnumerator PrepareLevel(int level)
        {
            if (!OceanCampaignAvailability.IsOpen(level)) throw new InvalidOperationException("Requested level is not published.");
            bool prepared = false;
            yield return world.PrepareEnvironmentForLevel(level, value => prepared = value);
            if (!prepared) throw new InvalidOperationException("Requested environment was not prepared.");
            spawn = world.ActiveSpawn;
            Physics.SyncTransforms();
            yield return hunt.PrepareExpeditionHabitat(spawn);
            Vector3 eye = new Vector3(spawn.x, UnderwaterWorld.SampleRenderedSeabedHeight(spawn.x, spawn.z) + 1.5f, spawn.z);
            // Never silently leave the player inside the other environment's rock volumes.
            // Brief tracking interruption gets a chance to recover without touching the camera.
            double relocationDeadline = Time.realtimeSinceStartupAsDouble + 2;
            while (!hands.TryRelocateHead(eye, Vector3.forward))
            {
                if (Time.realtimeSinceStartupAsDouble >= relocationDeadline)
                    throw new InvalidOperationException("Fresh tracked head pose unavailable for safe spawn relocation.");
                yield return null;
            }
            // Target height/heading and recovered weapon placement use the real view pose.
            // Relocate first so an ascending player does not spawn the next level's fish
            // far overhead or behind the new entry composition.
            world.ResetEnvironmentReveal();
            State.StartLevel(level);
            hunt.RebuildDiscoveries(spawn, State);
            Creatures.ConfigureLevel(spawn); Mechanics.ConfigureLevel(spawn); configured = true;
            Physics.SyncTransforms();
        }

        IEnumerator RestoreLevel(int level)
        {
            bool prepared = false;
            yield return world.PrepareEnvironmentForLevel(OceanCampaignAvailability.EntryLevel(level), value => prepared = value);
            if (!prepared) throw new InvalidOperationException("Previous environment could not be restored.");
            spawn = world.ActiveSpawn;
            Physics.SyncTransforms();
            if (!OceanCampaignAvailability.IsOpen(level))
            {
                Creatures.ClearForUnavailableLevel(); Mechanics.ClearForUnavailableLevel();
                hunt.ClearForUnavailableLevel(); configured = true; yield break;
            }
            yield return hunt.PrepareExpeditionHabitat(spawn);
            hunt.RebuildDiscoveries(spawn, State);
            Creatures.ConfigureLevel(spawn); Mechanics.ConfigureLevel(spawn); configured = true;
            Physics.SyncTransforms();
        }

        // Unity otherwise reports exceptions from a yielded nested iterator outside its
        // caller. Flatten only this transaction so cleanup/rollback always sees the failure.
        internal static IEnumerator RunTransitionOperation(IEnumerator operation, Action<Exception> failed)
        {
            var jobs = new Stack<IEnumerator>(); jobs.Push(operation);
            try
            {
                while (jobs.Count > 0)
                {
                    bool more = false; object current = null; Exception error = null;
                    try { more = jobs.Peek().MoveNext(); if (more) current = jobs.Peek().Current; }
                    catch (Exception caught) { error = caught; }
                    if (error != null) { failed(error); yield break; }
                    if (!more)
                    {
                        try { (jobs.Pop() as IDisposable)?.Dispose(); }
                        catch (Exception caught) { error = caught; }
                        if (error != null) { failed(error); yield break; }
                        continue;
                    }
                    if (current is IEnumerator nested) jobs.Push(nested);
                    else yield return current;
                }
            }
            finally
            {
                while (jobs.Count > 0)
                    try { (jobs.Pop() as IDisposable)?.Dispose(); }
                    catch (Exception caught) { Debug.LogWarning("[Campaign] transition_cleanup_failed detail=" + caught); }
            }
        }
        public void SaveNow()
        {
            if (!initialized || environmentTransitioning) return;
            nextSave = Time.unscaledTimeAsDouble + 15;
            // A verified later run is an archive in this release. Even an identical
            // save would rotate its distinct recovery copy, so leave both originals alone.
            if (!OceanCampaignAvailability.IsOpen(State.Current.level))
            { LastSaveSucceeded = false; return; }
            string saveError = writable ? null : "Existing save could not be loaded; writes disabled.";
            LastSaveSucceeded = writable && OceanSaveStore.TrySave(SaveDirectory, State.Save, out saveError);
            // Never credit volatile progress whose stable run identity was not committed.
            if (LastSaveSucceeded)
            {
                var committed = OceanCampaignState.Copy(State.Save);
                committedScoreRun = committed.currentRun;
                CommitAchievementHistory(committed);
            }
            if (!LastSaveSucceeded && !warnedSave)
            {
                warnedSave = true;
                Debug.LogWarning("[Campaign] progress_not_saved; preserved existing files: " + saveError);
                if (hunt.CoreActive) feedback.ShowCampaignMessage("进度暂未保存，请保留游戏再试。");
            }
        }
        public static int CountBits(int value) { int count = 0; while (value != 0) { count += value & 1; value >>= 1; } return count; }
        void OnApplicationPause(bool paused) { if (paused) SaveNow(); }
        void OnApplicationFocus(bool focused) { if (!focused) SaveNow(); }
        void OnApplicationQuit() => SaveNow();
        void OnDisable() { CancelLevelSwitch(); }
        void OnDestroy() { DisposeAchievements(); CancelLevelSwitch(); SaveNow(); if (hands != null) hands.SetMenuLocomotionPaused(false); }
    }
}

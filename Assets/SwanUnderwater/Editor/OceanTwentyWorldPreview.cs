#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
namespace SwanUnderwater.Editor
{
    [InitializeOnLoad]
    public static class OceanTwentyWorldPreview
    {
        const string Key = "OceanTwentyWorld.";
        static RenderTexture target;
        static bool running;
        [Serializable] sealed class Report { public bool success; public int assertions; public string detail; public string scope = "Actual Unity world and pickup transactions with synthetic palm positions; not tracked-hand or headset performance acceptance."; public List<Row> levels = new List<Row>(); }
        [Serializable] sealed class Row { public int level, shells, fish, coins, stars, hits; public string mechanic, biome; public double editorChangeMilliseconds; public List<Spot> spots = new List<Spot>(); }
        [Serializable] sealed class Spot { public int id, kind; public bool hiddenFromEntry; public string habitat; public Vector3 position, eye, palm; }
        [Serializable] sealed class FishGraspContext
        {
            public int level, requestedSpecies, frame, fishMaskBefore, fishMaskAfter, targetSpecies, targetSharkId;
            public bool accepted, gameplayBefore, gameplayAfter, creaturesActiveBefore, creaturesActiveAfter,
                canCollect, menuOpen, tutorialActive, worldInputBlocked, environmentTransitioning, nearestBeforeFinite, nearestAfterFinite;
            public float nearestBefore, nearestAfter;
            public Vector3 palm, targetBoundsCenter, targetBoundsSize;
            public string diagnosticError;
            public List<FishGraspTargetContext> targets = new List<FishGraspTargetContext>();
        }
        [Serializable] sealed class FishGraspTargetContext
        {
            public int species, sharkId, bodyId;
            public bool selected, isShark, isAmbient, rootActiveSelf, rootActiveInHierarchy, colliderEnabled, distanceFinite, clearReach;
            public float distance;
            public Vector3 boundsCenter, boundsSize;
        }
        static Report report = new Report();
        static OceanTwentyWorldPreview() { if (SessionState.GetBool(Key + "active", false)) Attach(); }
        public static void Capture()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-codexRegressionFirst")>=0) CodexBuild.RunRegressionChecks();
            ShaderUtil.allowAsyncCompilation = false;
            string[] args = Environment.GetCommandLineArgs();
            string output = Path.GetFullPath("Validation/iteration-23/world-preview");
            bool isolatedSave = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Rendered preview owns exit.");
                if (args[i] == "-codexTwentyOutput") output = Path.GetFullPath(args[++i]);
                if (args[i] == "-codexCampaignSaveDir") isolatedSave = true;
            }
            if (!Application.isBatchMode || !isolatedSave) throw new InvalidOperationException("Use separate batch Editor and isolated save directory.");
            if (Array.IndexOf(args, "-codexUnavailableStartup") >= 0)
                EditorTests.OceanCampaignAvailabilityTests.SeedUnavailableStartupFixture();
            Directory.CreateDirectory(output);
            SessionState.SetString(Key + "output", output); SessionState.SetString(Key + "start", DateTime.UtcNow.Ticks.ToString());
            SessionState.SetBool(Key + "active", true); Attach();
            EditorSceneManager.OpenScene("Assets/Scenes/UnderwaterWorld.unity"); EditorApplication.isPlaying = true;
        }
        static void Attach() { EditorApplication.update -= Tick; EditorApplication.update += Tick; Application.logMessageReceived -= Error; Application.logMessageReceived += Error; }
        static void Error(string message, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Assert) Finish(false, message + "\n" + stack); }
        static void Tick()
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            try
            {
                if (DateTime.UtcNow.Ticks - long.Parse(SessionState.GetString(Key + "start", "0")) > TimeSpan.FromSeconds(600).Ticks) throw new TimeoutException("20-level world preview timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                Camera camera = Camera.main; if (camera == null) return;
                if (target == null) { target = new RenderTexture(1280, 720, 24); target.Create(); }
                if (OceanEntry.Instance != null)
                {
                    if (OceanEntry.Instance.HasFailed) throw new InvalidOperationException("Entry failed.");
                    Submit(camera); return;
                }
                if (running)
                {
                    // A batch Editor need not have a visible Game view. Submit the actual
                    // main camera while validating the presentation-frame transition gate.
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexMapPassage") >= 0 ||
                        Array.IndexOf(Environment.GetCommandLineArgs(), "-codexMarineDiversity") >= 0 ||
                        Array.IndexOf(Environment.GetCommandLineArgs(), "-codexMarineMotion") >= 0 ||
                        Array.IndexOf(Environment.GetCommandLineArgs(), "-codexAchievements") >= 0 ||
                        Array.IndexOf(Environment.GetCommandLineArgs(), "-codexHundredLevels") >= 0 ||
                        Array.IndexOf(Environment.GetCommandLineArgs(), "-codexCampaignAvailability") >= 0 ||
                        Array.IndexOf(Environment.GetCommandLineArgs(), "-codexUnavailableStartup") >= 0 ||
                        Array.IndexOf(Environment.GetCommandLineArgs(), "-codexPlayableRelease") >= 0) Submit(camera);
                    return;
                }
                var hunt = Object.FindFirstObjectByType<TreasureHunt>();
                if (hunt == null || !hunt.CoreActive) return;
                running = true; hunt.StartCoroutine(Exercise(hunt, camera));
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }
        static IEnumerator Exercise(TreasureHunt hunt, Camera camera)
        {
            OceanCampaignController campaign = hunt.Campaign; OceanCampaignState state = campaign.State;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexUnavailableStartup") >= 0)
            {
                yield return EditorTests.OceanCampaignAvailabilityTests.RunUnavailableStartupInScene(hunt);
                Save(camera, "archived-future-run-menu");
                Finish(true, "Archived later run kept intact at startup; only published backdrop built and future gameplay blocked. Isolated Editor save, no headset acceptance.");
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexCampaignAvailability") >= 0)
            {
                // This isolated menu/scene check is independent of the optional first-run lesson.
                campaign.Panel.SkipTutorialForValidation();
                yield return EditorTests.OceanCampaignAvailabilityTests.RunInScene(hunt);
                Save(camera, "first-twenty-availability-menu");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexMarineDiversity") < 0)
                {
                    Finish(true, "Real menu/controller gates and future-run rejection retain historical slots, files and scene actors. Isolated Editor save, no headset acceptance.");
                    yield break;
                }
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexAchievements") >= 0)
            {
                campaign.Panel.SkipTutorialForValidation();
                Require(campaign.Achievements != null && campaign.Achievements.Writable, "achievement journal initialized in isolated real world");
                campaign.HandleAction("resume", 0);
                double deadline = Time.realtimeSinceStartupAsDouble + 4;
                while (!campaign.GameplayAllowed)
                { Require(Time.realtimeSinceStartupAsDouble < deadline, "normal gameplay resumes after menu quarantine"); yield return null; }
                Require(state.Current.level == 1, "achievement scene fixture starts with a new level1 save");
                TreasureHunt.Pickup first = null;
                foreach (var pickup in hunt.Pickups) if (pickup.kind == 0) { first = pickup; break; }
                Require(first != null && hunt.TryGraspForValidation(first.renderer.bounds.center + Vector3.up * .2f), "real pickup transaction acquires first shell");
                Require(campaign.Achievements.IsUnlocked("shell-species-1") && campaign.Achievements.IsUnread("shell-species-1"), "durable pickup unlocks a new unread achievement through controller");
                long revision = campaign.Achievements.Revision;
                Require(!campaign.CollectPickup(first), "duplicate pickup is rejected by existing campaign");
                Require(campaign.Achievements.Revision == revision, "duplicate input cannot rewrite or re-unlock achievements");
                foreach (var pickup in hunt.Pickups)
                    if (pickup.kind >= 0 && !pickup.consumed)
                        Require(hunt.TryGraspForValidation(pickup.renderer.bounds.center + Vector3.up * .2f), "production grasp collects the remaining level1 shells");
                Require(state.GoalComplete && campaign.Achievements.IsUnlocked("shell-species-5") && campaign.Achievements.IsUnlocked("levels-1"), "goal result commits species and completion achievements");
                foreach (var pickup in hunt.Pickups)
                    if (pickup.kind < 0)
                        Require(hunt.TryGraspForValidation(pickup.renderer.bounds.center + Vector3.up * .2f), "production hidden coin transaction accepted");
                Require(state.Stars == 3 && campaign.Achievements.IsUnlocked("coins-1") && campaign.Achievements.IsUnlocked("three-stars-1"), "coin and three-star achievements share original earned result, not new score events");
                Require(campaign.Naturalist.Store.TryRecord(0, 1, DateTimeOffset.UtcNow.ToString("O")), "real isolated observation store accepts a discovery");
                campaign.SaveNow();
                Require(campaign.Achievements.IsUnlocked("observe-1"), "durable observation participates in reconciliation");
                campaign.HandleAction("menu", 0);
                Require(campaign.Panel.ClickForValidation("show_collection") && campaign.Panel.ClickForValidation("show_achievements"), "production menu callbacks reach the achievement page");
                deadline = Time.realtimeSinceStartupAsDouble + 3;
                while (campaign.Panel.IsTransitioning) { Require(Time.realtimeSinceStartupAsDouble < deadline, "achievement menu expansion completes"); yield return null; }
                Canvas.ForceUpdateCanvases(); Save(camera, "achievement-01-earned-in-world");
                var reloaded = new OceanAchievementJournal(campaign.SaveDirectory);
                Require(reloaded.Writable && reloaded.UnlockedCount == campaign.Achievements.UnlockedCount && reloaded.UnreadCount == campaign.Achievements.UnreadCount, "local achievement file reload preserves earned and unread states");
                Require(campaign.Panel.ClickForValidation("achievements_read_all"), "explicit read control uses production menu input");
                Require(campaign.Achievements.UnreadCount == 0, "reading achievements does not replay game rewards");
                Save(camera, "achievement-02-read-in-world");
                Finish(true, "Actual isolated world pickup/duplicate/goal/hidden coin/observation -> saved achievement -> menu -> reload/read chain. Synthetic Editor grasp positions, no real headset acceptance.");
                yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-codexAccelUsabilityReview")>=0)
            {
                campaign.Panel.SkipTutorialForValidation();
                yield return EditorTests.OceanSaveRecoverySceneTests.RunInScene(hunt);
                Require(true,"production read-only retry scene chain completed and restored its isolated fixture");
                campaign.HandleAction("menu",0);
                Require(campaign.Panel.ClickForValidation("show_comfort"),"open actual Settings for presentation replay");
                string beforeReplay=JsonUtility.ToJson(state.Save);
                Require(campaign.Panel.ClickForValidation("entry_replay"),"actual Settings callback starts the optional presentation");
                yield return null;
                OceanEntry entry=OceanEntry.Instance;
                Require(entry!=null && entry.IsReplay && !entry.WorldPlayable,"replay keeps world input suppressed");
                Submit(camera);Save(camera,"entry-01-cinematic-no-skip");
                var playback=entry.GetComponent<OceanEntryControls>();
                Require(playback==null && !entry.SkipRequested,"real scene has no skip prompt or hidden entry controls");
                double started=Time.realtimeSinceStartupAsDouble;
                while(OceanEntry.Instance!=null)
                {
                    Require(Time.realtimeSinceStartupAsDouble-started<60,"bounded full cinematic replay and cleanup");
                    Submit(camera);yield return null;
                }
                yield return null;
                Require(campaign.Panel.IsOpen && campaign.Panel.IsAvailableForDiagnostics &&
                    campaign.Panel.PageNameForDiagnostics=="Comfort","rendered replay returns to the same paused Settings page");
                Require(JsonUtility.ToJson(state.Save)==beforeReplay,"real rendered replay does not change campaign state");
                double expandedDeadline=Time.realtimeSinceStartupAsDouble+3;
                while(campaign.Panel.IsTransitioning)
                {
                    Require(Time.realtimeSinceStartupAsDouble<expandedDeadline,"returned Settings reaches its actual expanded presentation");
                    yield return null;
                }
                Canvas.ForceUpdateCanvases();Submit(camera);Save(camera,"entry-02-returned-settings");
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-codexContinueHundred")<0)
                {
                    Finish(true,"Read-only retry production callbacks and real scene actor rebuild restored; full entry replay without skip UI submitted actual Unity camera frames and returned to Settings with unchanged campaign. Synthetic Editor actions, no headset/VST/real-hand acceptance.");
                    yield break;
                }
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-codexBoidCacheReview")>=0)
            {
                campaign.Panel.SkipTutorialForValidation();
                yield return EditorTests.OceanBoidTransformCacheTests.RunInScene();
                Require(true,"boid cached transform update matches Editor-only legacy path on actual PlayMode frames");
                Finish(true,"Actual Unity Transform old/new differential and two PlayMode frames passed; fixed synthetic state, no real headset FPS claim.");
                yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-codexTutorialVisualReview")>=0)
            {
                campaign.Panel.SkipTutorialForValidation();campaign.HandleAction("menu",0);
                campaign.Panel.SetAvailable(false);
                var host=new GameObject("Editor-only tutorial presentation review");
                var graphic=host.AddComponent<OceanTutorialVisual>();
                try
                {
                    graphic.Begin(camera,Resources.Load<Font>("Treasure/TreasureFont"));
                    graphic.Render(OceanInteractiveTutorialRules.Step.Tracking,true,false,true,false,.02f,true);
                    EditorTests.OceanComfortVisualTests.CheckProjection(graphic,camera);
                    EditorTests.OceanComfortVisualTests.CheckTextFits(graphic);
                    Submit(camera);yield return null;Save(camera,"tutorial-01-left-tracked-right-missing");
                    for(int frame=0;frame<12;frame++)graphic.Render(OceanInteractiveTutorialRules.Step.SwimAndStop,true,true,true,true,.1f,true);
                    EditorTests.OceanComfortVisualTests.CheckProjection(graphic,camera);
                    EditorTests.OceanComfortVisualTests.CheckTextFits(graphic);
                    Submit(camera);yield return null;Save(camera,"tutorial-02-slow-stroke-demonstration");
                    graphic.SetVisible(false);Require(!graphic.VisibleForValidation,"tutorial visuals hide on menu/pause");
                    graphic.Close();Require(!graphic.HasGraphicForValidation,"tutorial visuals clean up after finishing");
                }
                finally{graphic.Close();Object.Destroy(host);}
                Finish(true,"Tutorial tracking and slow-stroke graphics rendered with synthetic presentation states; lifecycle/projection checked. No real input, reward or tutorial gate was advanced.");
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexHundredLevels") >= 0)
            {
                var world = Object.FindFirstObjectByType<UnderwaterWorld>();
                bool visualOnly=Array.IndexOf(Environment.GetCommandLineArgs(),"-codexMechanicVisualReview")>=0;
                campaign.Panel.SkipTutorialForValidation();
                if(!visualOnly)
                {
                    EditorTests.OceanSpearRecoverySceneTests.RunInScene();
                    yield return EditorTests.OceanCampaignBiomeTests.RunInScene(world, hunt);
                }
                for(int level=visualOnly?21:1;level<=OceanLevelCatalog.Count;level+=visualOnly?5:1)
                {
                    double started=Time.realtimeSinceStartupAsDouble;
                    campaign.HandleAction("start_level",level);
                    while(campaign.EnvironmentTransitioning)
                    {
                        Require(Time.realtimeSinceStartupAsDouble-started<120,"bounded map switch "+level);
                        yield return null;
                    }
                    yield return null; yield return null;
                    double inputDeadline=Time.realtimeSinceStartupAsDouble+4;
                    while(!campaign.GameplayAllowed&&Time.realtimeSinceStartupAsDouble<inputDeadline) yield return null;
                    Require(state.Current.level==level&&campaign.GameplayAllowed,"actual level playing "+level);
                    Require(world.ActiveBiome==OceanEnvironmentIdentity.ForLevel(level).Biome,"actual biome identity "+level);
                    Require(state.Definition==OceanLevelCatalog.Get(level),"single catalog definition "+level);
                    if(level>20)
                    {
                        Require(campaign.Mechanics.Rules!=null&&campaign.Mechanics.VisibleNodeCount>=1,"live mechanic station "+level);
                        Require(state.Definition.TimeLimitSeconds<=600,"new time limit bounded "+level);
                        if((level-21)%5==0)
                        {
                            EditorTests.OceanMechanicSceneTests.ValidateHudSeparation(camera,campaign);
                            Save(camera,"tier-"+((level-1)/5+1).ToString("00")+"-level-"+level);
                        }
                    }
                    float before=state.Current.remainingSeconds;
                    campaign.Panel.OpenMenu();
                    yield return null; yield return null;
                    Require(state.Current.remainingSeconds==before&&!campaign.GameplayAllowed,"real menu freezes clock "+level);
                    campaign.Panel.CloseMenu();
                    inputDeadline=Time.realtimeSinceStartupAsDouble+4;
                    while(!campaign.GameplayAllowed&&Time.realtimeSinceStartupAsDouble<inputDeadline) yield return null;
                    Require(campaign.GameplayAllowed,"menu animation finishes before next level input "+level);
                    report.levels.Add(new Row{level=level,mechanic=state.Definition.Mechanic.ToString(),biome=world.ActiveBiome.ToString(),
                        editorChangeMilliseconds=(Time.realtimeSinceStartupAsDouble-started)*1000});
                }
                Finish(true,visualOnly?"16 representative mechanic tiers rendered and menu pause checked after caption layout correction; synthetic Editor, no headset/performance claim.":"100 real level starts, 16 live mechanic tiers, independent cold/cached biome transitions, menu pause and representative renders; synthetic Editor operation, no wearer/performance claim.");
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexMarineMotion") >= 0)
            {
                campaign.Panel.SkipTutorialForValidation();
                campaign.HandleAction("start_level", 20);
                double deadline = Time.realtimeSinceStartupAsDouble + 120;
                while (campaign.EnvironmentTransitioning)
                {
                    Require(Time.realtimeSinceStartupAsDouble < deadline, "motion scene reaches level twenty");
                    yield return null;
                }
                yield return null; yield return null;
                campaign.Panel.SkipTutorialForValidation();
                double inputDeadline = Time.realtimeSinceStartupAsDouble + 4;
                while (!campaign.GameplayAllowed && Time.realtimeSinceStartupAsDouble < inputDeadline) yield return null;
                var world = Object.FindFirstObjectByType<UnderwaterWorld>();
                Require(state.Current.level == 20 && campaign.GameplayAllowed, "fresh production level twenty is playing");
                Save(camera, "level20-before-motion-checks");
                EditorTests.OceanSharkSceneMotionTests.RunUnsafeClaimFixture(world, camera);
                EditorTests.OceanSharkSceneMotionTests.RunInScene(world, camera);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexSharkBaseline") < 0)
                {
                    EditorTests.OceanSharkSceneMotionTests.RunOutOfBoundsClaimFixture(world, camera);
                    EditorTests.OceanSharkMotionTests.RunAll();
                    // The same harness can run against the preserved 2.8.9 player source
                    // without requiring the new school implementation in that baseline.
                    Type schoolChecks = typeof(OceanTwentyWorldPreview).Assembly.GetType("SwanUnderwater.EditorTests.OceanSchoolFishMotionChecks", true);
                    schoolChecks.GetMethod("RunAll").Invoke(null, null);
                    foreach (int next in new[] { 5, 6, 1 })
                    {
                        campaign.HandleAction("start_level", next);
                        double switchDeadline = Time.realtimeSinceStartupAsDouble + 120;
                        while (campaign.EnvironmentTransitioning)
                        {
                            Require(Time.realtimeSinceStartupAsDouble < switchDeadline, "bounded shark ownership map roundtrip");
                            yield return null;
                        }
                        yield return null; yield return null;
                        inputDeadline = Time.realtimeSinceStartupAsDouble + 4;
                        while (!campaign.GameplayAllowed && Time.realtimeSinceStartupAsDouble < inputDeadline) yield return null;
                        Require(state.Current.level == next && campaign.GameplayAllowed,
                            "map roundtrip restores gameplay at level " + next);
                        if (next == 5) PrepareInvalidCachedShark(world);
                    }
                    EditorTests.OceanAmbientSharkHeadingTests.RunRestoredAmbientWaterInScene(world, camera);
                    EditorTests.OceanAmbientSharkHeadingTests.RunInScene(world, camera);
                    campaign.HandleAction("start_level", 20);
                    deadline = Time.realtimeSinceStartupAsDouble + 120;
                    while (campaign.EnvironmentTransitioning)
                    {
                        Require(Time.realtimeSinceStartupAsDouble < deadline, "bounded final return to level twenty");
                        yield return null;
                    }
                    yield return null; yield return null;
                    inputDeadline = Time.realtimeSinceStartupAsDouble + 4;
                    while (!campaign.GameplayAllowed && Time.realtimeSinceStartupAsDouble < inputDeadline) yield return null;
                    Require(state.Current.level == 20 && campaign.GameplayAllowed, "final level twenty return restores gameplay");
                }
                foreach (var creature in campaign.Creatures.Targets)
                {
                    if (!creature.IsShark || !creature.Visual.activeInHierarchy) continue;
                    Bounds bounds = creature.WorldBounds;
                    Vector3 eye = bounds.center - creature.Body.transform.forward * (bounds.size.magnitude * .65f + 2f) +
                        creature.Body.transform.right * (bounds.size.magnitude * .75f + 1f) + Vector3.up * .8f;
                    yield return ArtView(camera, eye, bounds.center, "level20-shark-" + creature.SharkId + "-a", world);
                    yield return new WaitForSecondsRealtime(.6f);
                    yield return ArtView(camera, eye, creature.WorldBounds.center, "level20-shark-" + creature.SharkId + "-b", world);
                }
                Finish(true, "Actual level twenty shark clear starts, per-animal cruise/evasion, final-shot capture and school anatomy/trajectory clocks checked; rendered Editor evidence, not wearer acceptance.");
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexMapPassage") >= 0)
            {
                // A fresh private save offers hands-on teaching; it deliberately blocks gameplay.
                // This presentation suite exercises map switching, without fabricating tutorial gestures.
                campaign.Panel.SkipTutorialForValidation();
                Require(!campaign.Panel.TutorialActive, "map passage fixture exits optional practice without writing tutorial completion");
                EditorTests.OceanMapPassageTests.RunAll();
                yield return EditorTests.OceanMapPassageTests.RunInScene(camera);
                // RunInScene destroys its final cancelled cover in a finally block.
                // Unity releases Destroy objects at frame end, before the transaction below.
                yield return null; yield return null;
                Require(Object.FindFirstObjectByType<OceanEnvironmentTransition>() == null,
                    "isolated presentation test released its cancelled cover");
                foreach (int next in new[] { 5, 6, 7, 5 })
                {
                    bool expected = OceanEnvironmentTransition.Required(state.Current.level, next);
                    bool seen = false, capturedCover = false, capturedOpaque = false, capturedReveal = false;
                    campaign.HandleAction("start_level", next);
                    double deadline = Time.realtimeSinceStartupAsDouble + 120;
                    while (campaign.EnvironmentTransitioning)
                    {
                        Require(Time.realtimeSinceStartupAsDouble < deadline, "bounded production map switch");
                        Require(!campaign.GameplayAllowed, "gameplay blocked during actual map switch");
                        var effect = Object.FindFirstObjectByType<OceanEnvironmentTransition>();
                        if (effect != null)
                        {
                            seen = true;
                            if (!capturedCover && effect.Opacity > .35f && effect.Opacity < .8f)
                            { Save(camera, "passage-to-" + next + "-cover"); capturedCover = true; }
                            if (!capturedOpaque && effect.Opacity == 1f && effect.OpaqueFrameSubmitted)
                            { Save(camera, "passage-to-" + next + "-opaque"); capturedOpaque = true; }
                            if (capturedOpaque && !capturedReveal && effect.Opacity > .1f && effect.Opacity < .65f)
                            { Save(camera, "passage-to-" + next + "-reveal"); capturedReveal = true; }
                        }
                        yield return null;
                    }
                    yield return null; yield return null;
                    Require(seen == expected, "only a changed environment plays the actual effect");
                    Require(state.Current.level == next && hunt.GameplayActive && campaign.GameplayAllowed,
                        "actual map switch restores playing/input at level " + next);
                    Require(Object.FindFirstObjectByType<OceanEnvironmentTransition>() == null,
                        "transition releases its geometry and render callbacks");
                    Require(!Object.FindFirstObjectByType<UnderwaterWorld>().ExternalTransitionCoverActive,
                        "external cover ownership cleared after switch");
                    Save(camera, "passage-to-" + next + "-ready");
                }
                EditorTests.OceanAmbientSharkHeadingTests.RunInScene(Object.FindFirstObjectByType<UnderwaterWorld>(), camera);
                Finish(true, "Actual main-camera transition submission, pause/focus/cancel, production 5/6/7/5 map roundtrip, input recovery and ambient anatomical shark heading checked. Editor evidence only.");
                yield break;
            }
            bool naturalistChecks = Array.IndexOf(Environment.GetCommandLineArgs(), "-codexNaturalist") >= 0;
            if (naturalistChecks) EditorTests.OceanNaturalistIntegrationChecks.RunAll();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexInteractionSkyOnly") >= 0)
            {
                yield return OceanInteractionSkyChecks.Run(hunt, camera, SessionState.GetString(Key + "output", ""));
                Finish(true, "Actual mixed-target selection, obstructed grasp, near-seabed physics and giant guardian rendering verified with synthetic inputs.");
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexSanctuaryArtPreview") >= 0)
            {
                ShaderUtil.allowAsyncCompilation = false;
                EditorTests.OceanEnvironmentTransitionTests.RunAll();
                EditorTests.OceanMeshCollisionTests.RunAll();
                OceanFishVisualChecks.Run();
                Shader organic = Resources.Load<Shader>("Shaders/OceanSanctuaryOrganic");
                foreach(var message in ShaderUtil.GetShaderMessages(organic)) Debug.LogWarning("[OrganicShader] "+message.severity+" "+message.message);
                Debug.Log("[OrganicShader] supported="+organic.isSupported+" errors="+ShaderUtil.ShaderHasError(organic));
                yield return EditorTests.OceanEnvironmentTransitionTests.RunInScene(Object.FindFirstObjectByType<UnderwaterWorld>(), hunt);
                yield return CaptureEnvironmentArt(hunt, camera);
                Finish(true, "Real dual-environment roundtrip, hidden pickups and cached reset passed; actual Mobile URP fixed-view art captures. Editor monocular evidence, not headset performance acceptance.");
                yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-codexMarineDiversity")>=0)
            {
                var world=Object.FindFirstObjectByType<UnderwaterWorld>();
                campaign.Panel.SkipTutorialForValidation();
                int firstClassicRoot=0;
                foreach(int level in new[]{1,6,20,1})
                {
                    bool cachedClassicReturn=level==1&&firstClassicRoot!=0;
                    campaign.HandleAction("start_level",level);
                    double transitionUntil=Time.realtimeSinceStartupAsDouble+120;
                    while(campaign.EnvironmentTransitioning)
                    { Require(Time.realtimeSinceStartupAsDouble<transitionUntil,"bounded actual marine-diversity map preparation "+level);yield return null; }
                    campaign.Panel.SkipTutorialForValidation();
                    campaign.HandleAction("resume",0);
                    double gameplayUntil=Time.realtimeSinceStartupAsDouble+5;
                    while(!campaign.GameplayAllowed&&Time.realtimeSinceStartupAsDouble<gameplayUntil)yield return null;
                    Require(state.Current.level==level&&campaign.GameplayAllowed&&world.IsReady,
                        "real gameplay resumes before marine-diversity checks "+level);
                    double until=Time.realtimeSinceStartupAsDouble+90;
                    int expected=level==1?0:level==6?27:30;
                    while((world.ActiveNaturalistResidentCount<expected || !EditorTests.OceanNaturalistIntegrationChecks.ObservationRegistryReady(campaign)) &&
                        Time.realtimeSinceStartupAsDouble<until)yield return null;
                    // Retain actual population evidence even if one of the following
                    // exclusion, budget or registration assertions fails.
                    File.WriteAllText(Path.Combine(SessionState.GetString(Key+"output",""),"population-level-"+level+
                        (cachedClassicReturn?"-cached-return":"")+".json"),
                        JsonUtility.ToJson(world.GetResidentDensitySnapshot(),true));
                    Require(world.ActiveNaturalistResidentCount==expected && EditorTests.OceanNaturalistIntegrationChecks.ObservationRegistryReady(campaign),
                        "all admitted resident slots and run-bound observation registry ready at level "+level);
                    EditorTests.OceanResidentDensityTests.RunInScene(world);
                    EditorTests.OceanNaturalistIntegrationChecks.CheckRetiredMorayAbsentInScene(world,campaign);
                    if(level==6)EditorTests.OceanNativeResidentClearanceChecks.RunInScene(world);
                    if(level==20)EditorTests.OceanNaturalistIntegrationChecks.CheckFreshLevelTwentyScene();
                    if(level==1)
                    {
                        int actualRoot=world.ActiveEnvironmentRoot.GetInstanceID();
                        if(cachedClassicReturn)Require(actualRoot==firstClassicRoot,"return to level1 reuses the actual cached Classic environment without retired morays");
                        else firstClassicRoot=actualRoot;
                        continue;
                    }
                    int butterfly=0,turtle=0,dolphin=0,mackerel=0;
                    foreach(var tag in world.ActiveEnvironmentRoot.GetComponentsInChildren<OceanNaturalistTag>())
                    {
                        if(tag.Species==28)butterfly++;if(tag.Species==29)turtle++;if(tag.Species==30)dolphin++;if(tag.Species==27)mackerel++;
                        if(tag.Species<27)continue;
                        Require(tag.GetComponent<OceanWildlifeResident>()!=null,"resident has a movement owner");
                        foreach(Renderer renderer in tag.GetComponentsInChildren<Renderer>())
                            foreach(Material material in renderer.sharedMaterials)
                                Require(material!=null&&material.shader!=null&&material.shader.isSupported,"native resident material is supported");
                        if(tag.Species==28)
                        {
                            Require(tag.GetComponentInChildren<SkinnedMeshRenderer>()==null,"static butterfly not mislabeled as authored rig");
                            Require(tag.GetComponent<Renderer>().sharedMaterial.shader.name=="SwanUnderwater/ButterflyFishSwim","butterfly original-fin shader preserved");
                        }
                        else
                        {
                            var skin=tag.GetComponentInChildren<SkinnedMeshRenderer>();var animator=tag.GetComponentInChildren<Animator>();
                            Require(skin!=null&&animator!=null&&animator.isActiveAndEnabled&&animator.runtimeAnimatorController!=null,
                                "actual native skin and Animator active");
                            Require(Vector3.Distance(skin.bounds.center,tag.transform.position)<3,"native renderer bounds remain near navigation body");
                        }
                    }
                    Require(butterfly==14&&mackerel==10&&turtle==1&&dolphin==2,"actual resource counts 14 butterfly,10 mackerel,1 turtle,2 dolphin");
                    if(level==6)
                    {
                        for(int z=0;z<OceanCoralSanctuary.ExplorationZones.Count;z++)
                        {
                            Vector2 at=OceanCoralSanctuary.ExplorationZones[z].offset;
                            Vector3 eye=world.ActiveSpawn+new Vector3(at.x,0,at.y-6);
                            eye.y=UnderwaterWorld.SampleRenderedSeabedHeight(eye.x,eye.z)+1.6f;
                            yield return ArtView(camera,eye,eye+new Vector3(1,.7f,13),"diversity-zone-"+z,world);
                        }
                        foreach(int id in new[]{28,29,30})
                        {
                            OceanNaturalistTag selected=null;
                            foreach(var tag in world.ActiveEnvironmentRoot.GetComponentsInChildren<OceanNaturalistTag>())if(tag.Species==id){selected=tag;break;}
                            Require(selected!=null,"new resident rendered "+id);
                            Bounds body=selected.GetComponentInChildren<Renderer>().bounds;
                            Vector3 eye=body.center+selected.transform.right*(id==28?.7f:2f)+Vector3.up*.15f;
                            yield return ArtView(camera,eye,body.center,"diversity-native-"+id,world);
                            yield return new WaitForSecondsRealtime(.35f);
                            body=selected.GetComponentInChildren<Renderer>().bounds;
                            yield return ArtView(camera,eye,body.center,"diversity-native-"+id+"-motion",world);
                        }
                    }
                    if(level==20)
                    {
                        // Retry reuses the same assets and transforms; no duplicate population.
                        var retained=new HashSet<int>();
                        foreach(var resident in world.ActiveEnvironmentRoot.GetComponentsInChildren<OceanWildlifeResident>())retained.Add(resident.GetInstanceID());
                        campaign.HandleAction("restart_level",20);
                        transitionUntil=Time.realtimeSinceStartupAsDouble+120;
                        while(campaign.EnvironmentTransitioning)
                        { Require(Time.realtimeSinceStartupAsDouble<transitionUntil,"bounded actual level20 retry");yield return null; }
                        campaign.Panel.SkipTutorialForValidation();
                        campaign.HandleAction("resume",0);
                        gameplayUntil=Time.realtimeSinceStartupAsDouble+5;
                        while(!campaign.GameplayAllowed&&Time.realtimeSinceStartupAsDouble<gameplayUntil)yield return null;
                        Require(state.Current.level==20&&campaign.GameplayAllowed,"retry restores real gameplay before resident reuse checks");
                        var after=world.ActiveEnvironmentRoot.GetComponentsInChildren<OceanWildlifeResident>();
                        Require(after.Length==retained.Count,"retry does not duplicate resident population");
                        foreach(var resident in after)Require(retained.Contains(resident.GetInstanceID()),"retry retains existing resident instances");
                        EditorTests.OceanNaturalistIntegrationChecks.CheckRetiredMorayAbsentInScene(world,campaign);
                    }
                }
                Finish(true,"Actual level1/6/20 to cached level1 population, retired-moray exclusion, thirty available observations at fresh level20, seven-zone species distribution, native skinned swimmers, original butterfly fins and retry reuse verified. Isolated rendered Editor evidence; headset performance/wearer acceptance pending.");
                yield break;
            }
            bool playableRelease = Array.IndexOf(Environment.GetCommandLineArgs(), "-codexPlayableRelease") >= 0;
            if (playableRelease)
            {
                // This suite checks the full published campaign, not automatic lesson completion.
                // Skip only this private Editor lesson; do not write PlayerPrefs or fabricate hand gestures.
                campaign.Panel.SkipTutorialForValidation();
                campaign.HandleAction("resume", 0);
                double inputDeadline = Time.realtimeSinceStartupAsDouble + 8;
                while (!campaign.GameplayAllowed || !CreatureActiveForDiagnostic(campaign.Creatures))
                { Require(Time.realtimeSinceStartupAsDouble < inputDeadline, "published campaign reaches controller and LateUpdate creature readiness after isolated tutorial release"); yield return null; }
                Require(!campaign.Panel.TutorialActive, "published campaign fixture does not leave a blocking lesson active");
            }
            for (int level = 1; level <= 20; level++)
            {
                var change = System.Diagnostics.Stopwatch.StartNew();
                campaign.HandleAction("start_level", level); change.Stop();
                double changeDeadline = Time.realtimeSinceStartupAsDouble + 120;
                while (campaign.EnvironmentTransitioning)
                { Require(Time.realtimeSinceStartupAsDouble < changeDeadline, "bounded published level switch " + level); yield return null; }
                yield return new WaitForSecondsRealtime(.8f);
                if (playableRelease)
                {
                    campaign.Panel.SkipTutorialForValidation();
                    campaign.HandleAction("resume", 0);
                    double inputDeadline = Time.realtimeSinceStartupAsDouble + 8;
                    // Controller readiness changes before LateUpdate publishes the animal interaction state.
                    // Wait for both real states; do not force active, retry a grasp, or delay a fixed duration.
                    while (!campaign.GameplayAllowed || !CreatureActiveForDiagnostic(campaign.Creatures))
                    { Require(Time.realtimeSinceStartupAsDouble < inputDeadline, "published level releases menu, input and LateUpdate creature interactions before pickups " + level); yield return null; }
                    Require(!campaign.Panel.TutorialActive, "published level has no isolated teaching input gate " + level);
                }
                Require(state.Current.level == level && hunt.GameplayActive, "level starts " + level);
                if (naturalistChecks && level == 20)
                {
                    // Observe real imported residents/registry before any synthetic pickups.
                    yield return new WaitForSecondsRealtime(1);
                    EditorTests.OceanNaturalistIntegrationChecks.CheckFreshLevelTwentyScene();
                    EditorTests.OceanMarineResidentChecks.RunInScene(Object.FindFirstObjectByType<UnderwaterWorld>(), camera);
                    var resident = EditorTests.OceanMarineResidentChecks.FindResident(Object.FindFirstObjectByType<UnderwaterWorld>());
                    var closeRoot = new GameObject("Isolated native swimmer camera");
                    try
                    {
                        Camera close = closeRoot.AddComponent<Camera>(); close.CopyFrom(camera); close.enabled = false;
                        close.stereoTargetEye = StereoTargetEyeMask.None; close.GetUniversalAdditionalCameraData().allowXRRendering = false;
                        close.fieldOfView = 42f; close.aspect = 1280f/720f;
                        Bounds body = default; bool firstRenderer = true;
                        foreach (Renderer renderer in resident.GetComponentsInChildren<Renderer>())
                        { if (firstRenderer) { body = renderer.bounds; firstRenderer = false; } else body.Encapsulate(renderer.bounds); }
                        close.transform.position = body.center + resident.transform.right * 1.1f + Vector3.up * .12f - resident.transform.forward * .16f;
                        close.transform.LookAt(body.center);
                        Save(close, "native-mackerel-close");
                    }
                    finally { Object.DestroyImmediate(closeRoot); }
                }
                var row = new Row { level = level, editorChangeMilliseconds = change.Elapsed.TotalMilliseconds }; report.levels.Add(row);
                var ids = new HashSet<int>(); int shells = 0, fish = 0;
                foreach (var item in hunt.Pickups)
                {
                    Require(ids.Add(item.id), "unique physical id " + level);
                    if (item.kind < 0) { row.coins++; Require(item.id >= 20000 && item.id < 20000 + state.CoinTargetCount, "stable coin slot"); }
                    else { row.shells++; shells |= 1 << item.kind; }
                    Require(Mathf.Abs(item.GroundAnchor.x) <= 46 && Mathf.Abs(item.GroundAnchor.z) <= 46, "reachable world bounds");
                    if (level > 5)
                    {
                        Require(item.approachPosition != Vector3.zero && item.graspPosition != Vector3.zero, "validated approach supplied");
                        Require(Vector3.Distance(item.graspPosition, item.renderer.bounds.ClosestPoint(item.graspPosition)) <= TreasureHunt.GrabReach, "palm within grab reach");
                        if (item.kind < 0) Require(item.habitat != "sand" && item.hiddenFromEntry, "late coin concealed by actual mesh from entry eye");
                    }
                    row.spots.Add(new Spot { id = item.id, kind = item.kind, hiddenFromEntry = item.hiddenFromEntry, habitat = item.habitat, position = item.GroundAnchor, eye = item.approachPosition, palm = item.graspPosition });
                }
                Require(shells == state.ShellTargetMask && row.coins == state.CoinTargetCount, "exact shell mask and coin count " + level);
                OceanCampaignCreatures.CreatureTarget shark = null;
                foreach (var creature in campaign.Creatures.Targets)
                {
                    if (creature.IsAmbient) continue;
                    if (creature.IsShark) { shark = creature; continue; }
                    fish |= 1 << creature.Species; row.fish++;
                }
                Require(fish == state.FishTargetMask && (shark != null) == state.RequiresShark, "exact fish and boss targets " + level);
                if (level == 6 || level == 10 || level == 15 || level == 20) Save(camera, "level-" + level + "-world");
                for (int kind = 0; kind < 10; kind++)
                {
                    if ((state.ShellTargetMask & (1 << kind)) == 0) continue;
                    TreasureHunt.Pickup item = null;
                    foreach (var p in hunt.Pickups) if (p.kind == kind) { item = p; break; }
                    Vector3 palm = level > 5 ? item.graspPosition : item.renderer.bounds.center + Vector3.up * .2f;
                    Require(hunt.TryGraspForValidation(palm) && state.IsConsumed(item.id) && !item.visual.activeSelf, "actual shell grasp " + level + "/" + kind);
                }
                for (int kind = 0; kind < 10; kind++)
                {
                    if ((state.FishTargetMask & (1 << kind)) == 0) continue;
                    OceanCampaignCreatures.CreatureTarget item = null;
                    foreach (var c in campaign.Creatures.Targets) if (!c.IsAmbient && !c.IsShark && c.Species == kind) { item = c; break; }
                    Vector3 palm = item.WorldBounds.center + Vector3.up * .05f;
                    FishGraspContext context = FishGraspBefore(campaign, item, palm, level, kind);
                    bool grasped = campaign.Creatures.TryGrasp(palm, true); // Exactly one production attempt; diagnostics never retry or sync transforms.
                    if (!grasped || (state.Current.fishMask & (1 << kind)) == 0) WriteFishGraspFailure(campaign, context, grasped);
                    Require(grasped && (state.Current.fishMask & (1 << kind)) != 0, "actual fish grasp " + level + "/" + kind);
                }
                if (shark != null)
                {
                    Require(state.AcquireTool(), "boss can own gun");
                    // These are durable rule thresholds, not swept projectile or tracked trigger claims.
                    for (int hit = 0; hit < state.SharkHitTarget; hit++) Require(state.TryFireSpear() && state.HitShark(shark.SharkId), "boss rules accept configured hit for the selected shark " + level);
                    Require(campaign.Creatures.TryGrasp(shark.WorldBounds.center + Vector3.up * .05f, true) && state.SharkCaughtFor(shark.SharkId), "actual selected shark grasp " + level);
                    row.hits = state.SharkHitsFor(shark.SharkId);
                }
                Require(state.GoalComplete && state.Stars == 2, "main goals two stars " + level);
                foreach (var item in hunt.Pickups)
                {
                    if (item.kind >= 0) continue;
                    Require(hunt.TryGraspForValidation(level > 5 ? item.graspPosition : item.renderer.bounds.center + Vector3.up * .2f), "actual coin grasp " + level + "/" + item.id);
                }
                Require(state.Stars == 3 && state.Save.bestStars[level - 1] == 3, "all coins third star " + level);
                row.stars = state.Stars; campaign.SaveNow();
                Require(campaign.LastSaveSucceeded && OceanSaveStore.TryLoad(campaign.SaveDirectory, out var loaded, out _) && loaded.currentRun.level == level && loaded.bestStars[level - 1] == 3, "real storage roundtrip " + level);
                Debug.Log("[TwentyWorld] passed level=" + level);
                yield return null;
            }
            EditorTests.OceanCampaignCreatureTests.RunExpandedInScene(Object.FindFirstObjectByType<UnderwaterWorld>(), camera);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexAppendEnvironmentArt") >= 0)
            {
                ShaderUtil.allowAsyncCompilation = false;
                yield return CaptureEnvironmentArt(hunt,camera);
            }
            Finish(true, "All 20 actual layouts, configured creature populations, synthetic reachable shell/fish/coin pickup transactions, boss state thresholds, stars and local save roundtrips passed. Gun gesture/projectile testing remains separate.");
        }
        static bool CreatureActiveForDiagnostic(OceanCampaignCreatures creatures)
            => (bool)typeof(OceanCampaignCreatures).GetField("active", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(creatures);
        static float DiagnosticDistance(float value, out bool finite)
        { finite = !float.IsNaN(value) && !float.IsInfinity(value); return finite ? value : -1f; }
        static FishGraspContext FishGraspBefore(OceanCampaignController campaign, OceanCampaignCreatures.CreatureTarget selected, Vector3 palm, int level, int species)
        {
            var context = new FishGraspContext { level = level, requestedSpecies = species, frame = Time.frameCount,
                fishMaskBefore = campaign.State.Current.fishMask, palm = palm, targetSpecies = selected.Species, targetSharkId = selected.SharkId,
                targetBoundsCenter = selected.WorldBounds.center, targetBoundsSize = selected.WorldBounds.size,
                gameplayBefore = campaign.GameplayAllowed, canCollect = campaign.State.CanCollect,
                menuOpen = campaign.Panel.IsOpen, tutorialActive = campaign.Panel.TutorialActive,
                worldInputBlocked = campaign.Panel.WorldInputBlocked, environmentTransitioning = campaign.EnvironmentTransitioning };
            try
            {
                context.creaturesActiveBefore = CreatureActiveForDiagnostic(campaign.Creatures);
                context.nearestBefore = DiagnosticDistance(campaign.Creatures.NearestGraspDistance(palm), out context.nearestBeforeFinite);
                const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var clear = typeof(OceanCampaignCreatures).GetMethod("ClearReach", fields);
                var rootField = typeof(OceanCampaignCreatures.CreatureTarget).GetField("root", fields);
                foreach (var target in campaign.Creatures.Targets)
                {
                    Transform root = rootField.GetValue(target) as Transform;
                    var row = new FishGraspTargetContext { species = target.Species, sharkId = target.SharkId,
                        selected = ReferenceEquals(target, selected), isShark = target.IsShark, isAmbient = target.IsAmbient,
                        rootActiveSelf = root != null && root.gameObject.activeSelf,
                        rootActiveInHierarchy = root != null && root.gameObject.activeInHierarchy,
                        colliderEnabled = target.Body != null && target.Body.enabled };
                    if (target.Body != null)
                    {
                        Bounds bounds = target.WorldBounds;
                        row.bodyId = target.Body.GetInstanceID(); row.boundsCenter = bounds.center; row.boundsSize = bounds.size;
                        row.distance = DiagnosticDistance(Vector3.Distance(palm, target.Body.ClosestPoint(palm)), out row.distanceFinite);
                        row.clearReach = (bool)clear.Invoke(campaign.Creatures, new object[] { palm, bounds.center });
                    }
                    context.targets.Add(row);
                }
            }
            catch (Exception error) { context.diagnosticError = error.GetType().Name + ": " + error.Message; }
            return context;
        }
        static void WriteFishGraspFailure(OceanCampaignController campaign, FishGraspContext context, bool grasped)
        {
            context.accepted = grasped; context.fishMaskAfter = campaign.State.Current.fishMask;
            context.gameplayAfter = campaign.GameplayAllowed;
            try
            {
                context.creaturesActiveAfter = CreatureActiveForDiagnostic(campaign.Creatures);
                context.nearestAfter = DiagnosticDistance(campaign.Creatures.NearestGraspDistance(context.palm), out context.nearestAfterFinite);
                string directory = SessionState.GetString(Key + "output", "");
                string path = Path.Combine(directory, "fish-grasp-failure-level-" + context.level + "-species-" + context.requestedSpecies + "-frame-" + context.frame + ".json");
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(context, true));
                Debug.LogWarning("[TwentyWorldFishGrasp] one_attempt=" + grasped + " level=" + context.level + " species=" + context.requestedSpecies +
                    " fishMask=" + context.fishMaskBefore + "->" + context.fishMaskAfter + " gameplay=" + context.gameplayBefore +
                    " creaturesActive=" + context.creaturesActiveBefore + " nearestFinite=" + context.nearestBeforeFinite + " context=" + path);
            }
            catch (Exception error)
            { Debug.LogWarning("[TwentyWorldFishGrasp] context_write_failed=" + error.GetType().Name + "; original grasp assertion remains authoritative."); }
        }
        static void Require(bool value, string message) { report.assertions++; if (!value) throw new InvalidOperationException(message); }
        static IEnumerator CaptureEnvironmentArt(TreasureHunt hunt, Camera source)
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            Require(pipeline != null, "Mobile URP pipeline is present");
            for (int i = 0; i < QualitySettings.names.Length; i++)
                if (QualitySettings.names[i] == "Mobile") { QualitySettings.SetQualityLevel(i, true); break; }
            QualitySettings.renderPipeline = pipeline;
            OceanEntry.DismissPlayableTitles();
            var host = new GameObject("Isolated environment art camera");
            Camera camera = host.AddComponent<Camera>(); camera.CopyFrom(source);
            camera.enabled = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            camera.aspect = 1280f / 720; camera.fieldOfView = 78;
            try
            {
                foreach (int level in new[] { 1, 6 })
                {
                    hunt.Campaign.HandleAction("start_level", level);
                    while (hunt.Campaign.EnvironmentTransitioning) yield return null;
                    yield return null;
                    Require(hunt.Campaign.State.Current.level == level && hunt.GameplayActive, "art level playable " + level);
                    hunt.Campaign.Panel.SetAvailable(false);
                    if(PerformanceHud.Instance!=null)PerformanceHud.Instance.gameObject.SetActive(false);
                    var world = Object.FindFirstObjectByType<UnderwaterWorld>();
                    camera.farClipPlane=level==6?OceanSanctuaryVista.RecommendedFarClipPlane:200f;
                    Vector3 spawn = world.ActiveSpawn;
                    Vector3 eye = new Vector3(spawn.x, UnderwaterWorld.SampleRenderedSeabedHeight(spawn.x,spawn.z)+1.5f,spawn.z);
                    yield return ArtView(camera, eye, eye+new Vector3(0,.8f,14), level == 1 ? "01-classic-entry" : "02-sanctuary-entry", world);
                    if (level == 6)
                    {
                        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexSanctuaryContent") >= 0)
                        {
                            EditorTests.OceanSanctuaryDiscoveryChecks.RunAll();
                            EditorTests.OceanExplorationFeedbackChecks.RunAll();
                            EditorTests.OceanSanctuaryMicroLifeChecks.RunAll();
                            EditorTests.OceanSanctuaryLifeChecks.RunAll(world);
                        }
                        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexSanctuaryDetail") >= 0)
                        {
                            EditorTests.OceanSanctuaryTerrainChecks.RunAll();
                            EditorTests.OceanSanctuarySurfaceChecks.RunAll(world);
                            EditorTests.HandSwimMovementTests.RunAll();
                            EditorTests.SwimFlowVfxChecks.RunAll();
                        }
                        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexSanctuaryComposition") >= 0)
                        {
                            EditorTests.OceanSanctuaryCompositionChecks.RunAll(world);
                            EditorTests.OceanSeagrassCurrentChecks.RunAll(world);
                        }
                        Vector3 near = spawn + new Vector3(0,0,4);
                        near.y=UnderwaterWorld.SampleRenderedSeabedHeight(near.x,near.z)+1.5f;
                        yield return ArtView(camera,near,OceanCoralSanctuary.ArchCenter+Vector3.up*3.2f,"03-sanctuary-arch",world);
                        Vector3 close=spawn+new Vector3(0,0,1.1f);
                        close.y=UnderwaterWorld.SampleRenderedSeabedHeight(close.x,close.z)+2.5f;
                        Vector3 colony=spawn+new Vector3(-5.1f,0,1.8f);
                        colony.y=UnderwaterWorld.SampleRenderedSeabedHeight(colony.x,colony.z)+2.6f;
                        Require(!Physics.CheckSphere(close,.12f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore),
                            "near coral art camera stays in real clear water, outside reef collision");
                        yield return ArtView(camera,close,colony,"04-sanctuary-reef-close",world);
                        yield return ArtView(camera,eye,eye+new Vector3(-.4f,1f,.3f)*20,"05-sanctuary-surface",world);
                        Vector3 sandEye=spawn+new Vector3(0,0,7f);
                        sandEye.y=UnderwaterWorld.SampleRenderedSeabedHeight(sandEye.x,sandEye.z)+1.65f;
                        yield return ArtView(camera,sandEye,sandEye+new Vector3(0,-1.5f,4.2f),"06-sanctuary-sand-lighting",world);
                        Vector3 depthEye=new Vector3(OceanCoralSanctuary.ArchCenter.x,0,OceanCoralSanctuary.ArchCenter.z-3.5f);
                        depthEye.y=UnderwaterWorld.SampleRenderedSeabedHeight(depthEye.x,depthEye.z)+1.6f;
                        Require(!Physics.CheckSphere(depthEye,.12f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore),
                            "depth art camera stays in the real arch passage");
                        yield return ArtView(camera,depthEye,depthEye+new Vector3(-3f,1.2f,24f),"07-sanctuary-depth-through-arch",world);
                        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-codexSanctuaryExploration")>=0)
                        {
                            EditorTests.OceanSanctuaryExplorationChecks.RunAll(world);
                            var route=OceanCoralSanctuary.ExplorationRouteOffsets;
                            int[] stops={10,13,15,16,18,22};
                            for(int i=0;i<stops.Length;i++)
                            {
                                Vector2 point=route[stops[i]],next=route[(stops[i]+1)%route.Count];
                                Vector3 at=spawn+new Vector3(point.x,0,point.y);
                                at.y=UnderwaterWorld.SampleRenderedSeabedHeight(at.x,at.z)+1.6f;
                                yield return ArtView(camera,at,at+new Vector3(next.x-point.x,1f,next.y-point.y),
                                    "exploration-"+(i+1).ToString("00"),world);
                            }
                            Vector3 edge=spawn+new Vector3(101,1.6f,72);
                            edge.y=UnderwaterWorld.SampleRenderedSeabedHeight(edge.x,edge.z)+1.6f;
                            yield return ArtView(camera,edge,edge+new Vector3(30,3,22),"exploration-07-outward-vista",world);
                        }
                        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexSanctuaryDetail") >= 0)
                        {
                            yield return EditorTests.SwimFlowVfxChecks.RunInScene(world, camera,
                                label => { HideArtCanvases(); Save(camera, label); });
                        }
                        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexSanctuaryComposition") >= 0)
                            yield return CaptureSanctuaryComposition(world, camera, spawn, eye);
                    }
                }
            }
            finally { Object.Destroy(host); }
        }
        static IEnumerator ArtView(Camera camera,Vector3 eye,Vector3 focus,string name,UnderwaterWorld world)
        {
            camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(focus-eye));
            var lod=world.GetComponentInChildren<ReefProximityLod>();
            if(lod!=null)lod.EvaluateForValidation(eye);
            // Submit the real material variants before reading pixels. The first draw
            // after swapping environments can otherwise capture Editor's pink placeholder.
            HideArtCanvases(); Submit(camera);
            yield return null;
            HideArtCanvases(); Submit(camera);
            yield return null;
            HideArtCanvases();
            Save(camera,name);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-codexFogDiagnostic")>=0&&name=="06-sanctuary-sand-lighting")
            {
                bool linear=Shader.IsKeywordEnabled("FOG_LINEAR"),exp=Shader.IsKeywordEnabled("FOG_EXP"),exp2=Shader.IsKeywordEnabled("FOG_EXP2");
                Debug.Log($"[ArtFog] before force enabled={RenderSettings.fog} mode={RenderSettings.fogMode} density={RenderSettings.fogDensity:R} keywords={linear}/{exp}/{exp2} params={Shader.GetGlobalVector("unity_FogParams")} color={Shader.GetGlobalColor("unity_FogColor")}");
                try
                {
                    Shader.DisableKeyword("FOG_LINEAR");Shader.DisableKeyword("FOG_EXP");Shader.EnableKeyword("FOG_EXP2");
                    Submit(camera);yield return null;
                    Debug.Log($"[ArtFog] after force keywords={Shader.IsKeywordEnabled("FOG_LINEAR")}/{Shader.IsKeywordEnabled("FOG_EXP")}/{Shader.IsKeywordEnabled("FOG_EXP2")} params={Shader.GetGlobalVector("unity_FogParams")} color={Shader.GetGlobalColor("unity_FogColor")}");
                    Save(camera,name+"-forced-exp2");
                }
                finally
                {
                    if(linear)Shader.EnableKeyword("FOG_LINEAR");else Shader.DisableKeyword("FOG_LINEAR");
                    if(exp)Shader.EnableKeyword("FOG_EXP");else Shader.DisableKeyword("FOG_EXP");
                    if(exp2)Shader.EnableKeyword("FOG_EXP2");else Shader.DisableKeyword("FOG_EXP2");
                }
            }
        }

        static IEnumerator CaptureSanctuaryComposition(UnderwaterWorld world, Camera camera, Vector3 spawn, Vector3 eye)
        {
            yield return ArtView(camera, eye, eye + new Vector3(-16, 6, 20), "scale-01-left-masses", world);
            yield return ArtView(camera, eye, eye + new Vector3(17, 7, 19), "scale-02-right-masses", world);
            Vector2 route = OceanCoralSanctuary.ExplorationRouteOffsets[10];
            Vector3 west = spawn + new Vector3(route.x, 0, route.y);
            west.y = UnderwaterWorld.SampleRenderedSeabedHeight(west.x, west.z) + 1.6f;
            yield return ArtView(camera, west, west + new Vector3(19, 7, 0), "scale-03-western-tower", world);
            var meadow = world.ActiveEnvironmentRoot.GetComponentInChildren<SeagrassMeadow>();
            Require(meadow != null && meadow.RootPositions.Count > 0, "current art has an actual meadow");
            Vector2 origin = new Vector2(spawn.x, spawn.z), selected = meadow.RootPositions[0];
            float closest = float.PositiveInfinity;
            foreach (Vector2 root in meadow.RootPositions)
            {
                float distance = Vector2.SqrMagnitude(root - origin);
                if (distance < closest) { closest = distance; selected = root; }
            }
            Vector3 grass = new Vector3(selected.x, UnderwaterWorld.SampleRenderedSeabedHeight(selected.x, selected.y) + .65f, selected.y);
            Vector3 grassEye = grass + new Vector3(0, 1.6f, -2.8f);
            bool clearGrassEye = false;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f;
                Vector3 attempt = grass + new Vector3(Mathf.Sin(angle) * 2.8f, 0, -Mathf.Cos(angle) * 2.8f);
                attempt.y = UnderwaterWorld.SampleRenderedSeabedHeight(attempt.x, attempt.z) + 1.8f;
                if (Physics.CheckSphere(attempt, .15f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                grassEye = attempt; clearGrassEye = true; break;
            }
            Require(clearGrassEye, "current art camera remains outside actual reef geometry");
            // Real rendered material time only: no global time, camera tracking, mesh or root movement.
            try
            {
                meadow.SetValidationTime(0f);
                yield return ArtView(camera, grassEye, grass, "current-01-time-000", world);
                meadow.SetValidationTime(2f);
                yield return ArtView(camera, grassEye, grass, "current-02-time-200", world);
            }
            finally { meadow.SetValidationTime(null); }
        }

        // Editor-only fault injection before 5 -> 6 -> 1. The level-five visual is
        // still campaign-owned when it becomes invalid for its ambient envelope;
        // release occurs after the other map's creature list has become active.
        static void PrepareInvalidCachedShark(UnderwaterWorld world)
        {
            const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            const System.Reflection.BindingFlags statics = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var animals = (IList)typeof(UnderwaterWorld).GetField("largeCreatures", fields).GetValue(world);
            var owned = (HashSet<Transform>)typeof(UnderwaterWorld).GetField("campaignSharks", fields).GetValue(world);
            foreach (object animal in animals)
            {
                Type type = animal.GetType();
                if (!(bool)type.GetField("isShark", fields).GetValue(animal)) continue;
                var model = (Transform)type.GetField("transform", fields).GetValue(animal);
                if (!owned.Contains(model)) continue;
                float radius = (float)type.GetField("bodyRadius", fields).GetValue(animal);
                var bodyMethod = typeof(UnderwaterWorld).GetMethod("LargeBodyCenter", statics);
                Vector3 body = (Vector3)bodyMethod.Invoke(null, new[] { animal });
                float floor = (float)typeof(UnderwaterWorld).GetMethod("LargeSharkWaterHeight", fields).Invoke(world, new object[] { body, radius });
                model.position += Vector3.up * (floor - .25f - body.y);
                Vector3 lowered = (Vector3)bodyMethod.Invoke(null, new[] { animal });
                bool clear = (bool)typeof(UnderwaterWorld).GetMethod("LargeSharkSegmentClear", fields).Invoke(world, new object[] { lowered, lowered, radius });
                Require(!clear, "cached-map fixture really violates the ambient full-body margin");
                Debug.Log("[CachedSharkFixture] invalid level-five campaign-owned source=" + model.name + " body=" + lowered);
                return;
            }
            throw new InvalidOperationException("The actual level-five map must contain a campaign-owned ambient shark for cache recovery validation.");
        }

        static void HideArtCanvases()
        {
            // Editor-only isolated save: capture the world without the head-following
            // task bar drifting back into view during shader warm-up frames.
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                canvas.enabled = false;
        }

        static void Submit(Camera camera)
        {
            var data = camera.GetUniversalAdditionalCameraData(); bool previous = data.allowXRRendering;
            try { data.allowXRRendering = false; RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target }); }
            finally { data.allowXRRendering = previous; }
        }
        static void Save(Camera camera, string name)
        {
            Submit(camera); RenderTexture old = RenderTexture.active; var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply(); File.WriteAllBytes(Path.Combine(SessionState.GetString(Key + "output", ""), name + ".png"), pixels.EncodeToPNG()); }
            finally { RenderTexture.active = old; Object.Destroy(pixels); }
        }
        static void Finish(bool success, string detail)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            SessionState.SetBool(Key + "active", false); EditorApplication.update -= Tick; Application.logMessageReceived -= Error;
            report.success = success; report.detail = detail;
            File.WriteAllText(Path.Combine(SessionState.GetString(Key + "output", ""), "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("TWENTY_WORLD_" + (success ? "PASS " : "FAIL ") + detail); EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif

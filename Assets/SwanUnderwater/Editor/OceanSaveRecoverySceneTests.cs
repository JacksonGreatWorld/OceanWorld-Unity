#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Real controller/actor read-only retry regression; batch Editor and private files only.</summary>
    public static class OceanSaveRecoverySceneTests
    {
        public static int Assertions { get; private set; }
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
        static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        static void Property(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
        static void Check(bool value, string message)
        { Assertions++; if (!value) throw new InvalidOperationException("Save recovery scene: " + message); }
        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        static byte[] ExistingBytes(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
        static bool HasText(OceanCampaignPanel panel, string text)
        { foreach (Text label in panel.GetComponentsInChildren<Text>()) if (label.gameObject.activeInHierarchy && label.text.Contains(text)) return true; return false; }

        public static IEnumerator RunInScene(TreasureHunt hunt)
        {
            if (hunt == null || hunt.Campaign == null) throw new ArgumentException("A prepared campaign is required.");
            return RunInScene((UnderwaterWorld)Get(hunt.Campaign, "world"), hunt);
        }

        /// <summary>Invoke with yield return from the isolated preview after entry/tutorial has finished.</summary>
        public static IEnumerator RunInScene(UnderwaterWorld world, TreasureHunt hunt)
        {
            if (!Application.isEditor || !Application.isPlaying || !Application.isBatchMode ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-codexCampaignSaveDir") < 0)
                throw new InvalidOperationException("Save recovery scene review requires batch Editor Play Mode with an isolated -codexCampaignSaveDir.");
            if (world == null || hunt == null || hunt.Campaign == null || !hunt.CoreActive)
                throw new ArgumentException("A ready actual world and campaign are required.");
            Assertions = 0;
            var fixture = new SceneFixture(world, hunt);
            Exception exerciseFailure = null, restoreFailure = null;
            try
            {
                fixture.Enter();
                // Flatten nested test iterators so even assertion/timeout failures reach the
                // asynchronous production restoration before they are reported to the preview.
                yield return RunGuarded(fixture.Exercise(), error => exerciseFailure = error);
                yield return RunGuarded(fixture.RestoreScene(), error => restoreFailure = error);
                if (restoreFailure != null) throw new InvalidOperationException("Scene restoration failed; stop this preview before any subsequent campaign tests.", restoreFailure);
                if (exerciseFailure != null) throw new InvalidOperationException("Save retry exercise failed after restoring the original scene.", exerciseFailure);
            }
            finally { fixture.Dispose(); }
            Debug.Log("OceanSaveRecoverySceneTests PASS " + Assertions + " assertions; actual menu callback, unchanged corrupt/future actors, in-flight refusal, authenticated same-level backup rebuild and complete scene restoration; Editor synthetic input only.");
        }

        static IEnumerator RunGuarded(IEnumerator operation, Action<Exception> failed)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(operation);
            try
            {
                while (stack.Count > 0)
                {
                    bool more = false; object current = null; Exception error = null;
                    try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; }
                    catch (Exception caught) { error = caught; }
                    if (error != null) { failed(error); yield break; }
                    if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (current is IEnumerator nested) stack.Push(nested); else yield return current;
                }
            }
            finally { while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); }
        }

        sealed class SceneFixture : IDisposable
        {
            readonly UnderwaterWorld world; readonly TreasureHunt hunt; readonly OceanCampaignController campaign;
            readonly OceanCampaignPanel panel; readonly HandSwimController hands; readonly Camera view;
            readonly OceanCampaignState owner; readonly OceanSaveData original; readonly string originalJson, directory, originalDirectory;
            readonly byte[] originalPrimary, originalBackup;
            readonly Dictionary<string, object> controllerFields = new Dictionary<string, object>();
            readonly Dictionary<string, object> panelFields = new Dictionary<string, object>();
            readonly Dictionary<Behaviour, bool> enabled = new Dictionary<Behaviour, bool>();
            readonly bool open, locomotionPaused, suppressed, lastSaveSucceeded;
            readonly Vector3 eye, forward;
            byte[] originalTrusted; bool entered, restored;
            string Primary => Path.Combine(directory, OceanSaveStore.FileName);
            string Backup => Path.Combine(directory, OceanSaveStore.BackupFileName);
            public SceneFixture(UnderwaterWorld world, TreasureHunt hunt)
            {
                this.world = world; this.hunt = hunt; campaign = hunt.Campaign; panel = campaign.Panel;
                Check(!campaign.EnvironmentTransitioning && campaign.ActorsPrepared && !panel.TutorialActive && !(bool)Get(campaign, "environmentFaulted"), "entry and existing transitions must finish before recovery review");
                Check(!((OceanLeaderboardBridge)Get(campaign, "leaderboard")).IsOpen, "leaderboard must be closed before isolated review");
                hands = (HandSwimController)Get(campaign, "hands"); view = (Camera)Get(campaign, "view");
                owner = campaign.State; original = new OceanCampaignState(owner.Save).Save; originalJson = JsonUtility.ToJson(original);
                originalDirectory = campaign.SaveDirectory; directory = Path.Combine(Path.GetTempPath(), "ocean-recovery-scene-" + Guid.NewGuid().ToString("N"));
                originalPrimary = ExistingBytes(Path.Combine(originalDirectory, OceanSaveStore.FileName)); originalBackup = ExistingBytes(Path.Combine(originalDirectory, OceanSaveStore.BackupFileName));
                eye = view.transform.position; forward = view.transform.forward;
                open = panel.IsOpen; locomotionPaused = hands.MenuLocomotionPaused; suppressed = hands.InteractionSuppressed; lastSaveSucceeded = campaign.LastSaveSucceeded;
                foreach (string name in new[] { "writable", "warnedSave", "committedScoreRun", "nextSave", "nextScoreRetry", "resultAt", "pendingSaveNotice", "lastStars", "lastTimedOut", "lastFailed", "lastSecond", "lastAmmo", "lastToolHeld", "resumedFrame", "spawn", "configured", "environmentFaulted" }) controllerFields.Add(name, Get(campaign, name));
                foreach (string name in new[] { "saveRecoveryReadOnly", "saveRecoveryStatus", "saveRecoverySucceeded", "saveRecoveryNotice", "page", "available" }) panelFields.Add(name, Get(panel, name));
            }
            public void Enter()
            {
                // Editor signing keys are created only under this fixture. Existing clients
                // are also suspended; no request, score/discovery persistence or production identity is used.
                Directory.CreateDirectory(directory);
                using (var identity = OceanLeaderboardIdentity.Open(Path.Combine(directory, "editor-test-identity"))) Check(identity.IsTestIdentity, "fixture uses an explicit Editor test identity");
                foreach (var client in campaign.GetComponents<OceanLeaderboardClient>())
                {
                    Check(!client.IsBusy, "finish any existing local test transport before scene review");
                    var identity = Get(client, "identity") as OceanLeaderboardIdentity;
                    Check(identity == null || identity.IsTestIdentity, "existing Editor client cannot use a production signing identity");
                    enabled.Add(client, client.enabled);
                }
                enabled.Add(campaign.Naturalist, campaign.Naturalist.enabled); enabled.Add(campaign, campaign.enabled);
                Check(OceanSaveStore.TrySave(Path.Combine(directory, "original"), original, out string error), "isolated original restoration seed: " + error);
                originalTrusted = File.ReadAllBytes(Path.Combine(directory, "original", OceanSaveStore.FileName));
                entered = true;
                foreach (var entry in enabled) entry.Key.enabled = false;
                Property(campaign, "SaveDirectory", directory); Set(campaign, "writable", false); Set(campaign, "warnedSave", true); Set(campaign, "committedScoreRun", null);
                Set(campaign, "nextSave", double.PositiveInfinity); Set(campaign, "nextScoreRetry", double.PositiveInfinity);
                panel.SetSaveRecoveryStatus(true, OceanSaveLoadStatus.Corrupt, false); panel.SetAvailable(true); panel.ShowSaveRecovery();
                hands.SetInteractionSuppressed(true);
            }
            public IEnumerator Exercise()
            {
                var run = owner.Current; string before = JsonUtility.ToJson(owner.Save), actors = ActorIds(hunt);
                File.WriteAllText(Primary, "bad-primary"); File.WriteAllText(Backup, "bad-backup");
                byte[] primary = File.ReadAllBytes(Primary), backup = File.ReadAllBytes(Backup);
                Check(panel.ClickForValidation("retry_save_load"), "corrupt retry uses the visible production button");
                Check(!campaign.EnvironmentTransitioning && ReferenceEquals(campaign.State, owner) && ReferenceEquals(owner.Current, run) && JsonUtility.ToJson(owner.Save) == before && ActorIds(hunt) == actors, "corrupt callback preserves exact state/run and actual pickup/creature/weapon/mechanic instance IDs");
                Check(HasText(panel, "无法通过检查") && SameBytes(primary, File.ReadAllBytes(Primary)) && SameBytes(backup, File.ReadAllBytes(Backup)), "corrupt callback explains failure without changing original bytes");

                var candidate = new OceanCampaignState(original); candidate.StartLevel(original.currentRun.level);
                Check(candidate.CollectCoin(OceanLevelCatalog.CoinId(0)), "same-level trusted candidate has an observable collected coin");
                for (int species = 0; species < OceanLevelCatalog.FishSpeciesCount; species++)
                    if ((candidate.FishTargetMask & (1 << species)) != 0) { Check(candidate.CollectFish(species), "candidate removes one required physical fish"); break; }
                string seed = Path.Combine(directory, "candidate");
                Check(OceanSaveStore.TrySave(seed, candidate.Save, out string error), "authenticated candidate seed: " + error);
                byte[] trusted = File.ReadAllBytes(Path.Combine(seed, OceanSaveStore.FileName));
                File.WriteAllText(Primary, "{\"version\":99,\"future\":true}"); File.WriteAllBytes(Backup, trusted);
                primary = File.ReadAllBytes(Primary); backup = File.ReadAllBytes(Backup);
                Check(panel.ClickForValidation("retry_save_load"), "future retry uses the production button");
                Check(!campaign.EnvironmentTransitioning && ReferenceEquals(owner.Current, run) && ReferenceEquals(campaign.State, owner) && JsonUtility.ToJson(owner.Save) == before && ActorIds(hunt) == actors, "future primary cannot adopt its older valid backup or replace real actors");
                Check(HasText(panel, "存档版本暂不支持") && SameBytes(primary, File.ReadAllBytes(Primary)) && SameBytes(backup, File.ReadAllBytes(Backup)), "future refusal preserves both originals");

                InFlightRefusal(campaign, panel, hands, view, () => ActorIds(hunt));
                File.WriteAllText(Primary, "bad-primary"); primary = File.ReadAllBytes(Primary);
                Check(panel.ClickForValidation("retry_save_load"), "authenticated backup retry starts through the real callback");
                yield return WaitForReload(campaign);
                Check(ReferenceEquals(campaign.State, owner) && owner.Current.level == original.currentRun.level && owner.Current.leaderboardRunId == candidate.Current.leaderboardRunId && JsonUtility.ToJson(owner.Save) == JsonUtility.ToJson(candidate.Save), "real coroutine adopts the exact authenticated same-level snapshot");
                Check(ActorIds(hunt) != actors && HasText(panel, "已从通过检查的备用存档重新读取"), "successful retry actually replaces physical actors and shows verified backup result");
                AssertActorSnapshot(hunt);
                Check(!(bool)Get(campaign, "writable") && !campaign.LastSaveSucceeded && !campaign.GameplayAllowed && panel.IsOpen && !campaign.EnvironmentTransitioning, "backup success remains read-only, paused and transaction complete");
                campaign.SaveNow();
                Check(SameBytes(primary, File.ReadAllBytes(Primary)) && SameBytes(backup, File.ReadAllBytes(Backup)) && !campaign.LastSaveSucceeded, "completed reload and later SaveNow never repair damaged primary or overwrite trusted backup");
            }
            public IEnumerator RestoreScene()
            {
                if (!entered) yield break;
                if (campaign.EnvironmentTransitioning) typeof(OceanCampaignController).GetMethod("CancelLevelSwitch", Private).Invoke(campaign, null);
                // Test fixture files may be replaced; the original application files are never written.
                File.WriteAllBytes(Primary, originalTrusted); File.WriteAllBytes(Backup, originalTrusted);
                panel.SetAvailable(true); panel.ShowSaveRecovery();
                Check(panel.ClickForValidation("retry_save_load"), "restore uses the same real read-only production transaction");
                yield return WaitForReload(campaign);
                Check(ReferenceEquals(campaign.State, owner) && JsonUtility.ToJson(owner.Save) == originalJson && !(bool)Get(campaign, "environmentFaulted"), "original snapshot and usable actor scene are fully restored before continuing");
                AssertActorSnapshot(hunt);
                Check(hands.TryRelocateHead(eye, forward), "restore original scene head position and heading");
                Check(SameBytes(originalPrimary, ExistingBytes(Path.Combine(originalDirectory, OceanSaveStore.FileName))) && SameBytes(originalBackup, ExistingBytes(Path.Combine(originalDirectory, OceanSaveStore.BackupFileName))), "original application primary/backup bytes remain untouched by the whole review");
                restored = true;
            }
            public void Dispose()
            {
                if (entered)
                {
                    if (campaign.EnvironmentTransitioning) typeof(OceanCampaignController).GetMethod("CancelLevelSwitch", Private).Invoke(campaign, null);
                    // A disposed/cancelled review must fail closed, never silently let later
                    // campaign tests use a state snapshot whose actors were not restored.
                    if (!restored) typeof(OceanCampaignState).GetMethod("ApplyVerifiedSave", Private).Invoke(owner, new object[] { original });
                    Property(campaign, "SaveDirectory", originalDirectory);
                    foreach (var entry in controllerFields) Set(campaign, entry.Key, entry.Value);
                    if (!restored) Set(campaign, "environmentFaulted", true);
                    Property(campaign, "LastSaveSucceeded", lastSaveSucceeded);
                    foreach (var entry in panelFields) Set(panel, entry.Key, entry.Value);
                    panel.SetSaveRecoveryStatus((bool)panelFields["saveRecoveryReadOnly"], (OceanSaveLoadStatus)panelFields["saveRecoveryStatus"], (bool)panelFields["saveRecoverySucceeded"], (string)panelFields["saveRecoveryNotice"]);
                    panel.SetAvailable((bool)panelFields["available"]);
                    if (open) { panel.OpenMenu(); typeof(OceanCampaignPanel).GetMethod("ShowPage", Private).Invoke(panel, new[] { panelFields["page"] }); }
                    else { panel.CloseMenu(); typeof(OceanCampaignPanel).GetMethod("ShowPage", Private).Invoke(panel, new[] { panelFields["page"] }); }
                    hands.SetMenuLocomotionPaused(locomotionPaused); hands.SetInteractionSuppressed(suppressed);
                    foreach (var entry in enabled) if (entry.Key != null) entry.Key.enabled = entry.Value;
                }
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                if (entered && !restored) Debug.LogError("Save recovery scene was interrupted before full actor restoration. Original path/write policy/snapshot/input flags restored, gameplay blocked; stop this preview rather than run subsequent scene tests.");
            }
        }

        static IEnumerator WaitForReload(OceanCampaignController campaign)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 60;
            while (campaign.EnvironmentTransitioning)
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Read-only reload exceeded60seconds.");
                yield return null;
            }
            yield return null; // settle delayed destruction before inspecting the real actor set
        }
        static string ActorIds(TreasureHunt hunt)
        {
            var ids = new List<int>();
            foreach (var pickup in hunt.Pickups) if (pickup.visual != null) ids.Add(pickup.visual.GetInstanceID());
            foreach (var actor in hunt.Campaign.Creatures.Targets)
            { if (actor.Visual != null) ids.Add(actor.Visual.GetInstanceID()); if (actor.Body != null) ids.Add(actor.Body.GetInstanceID()); }
            var gun = hunt.Campaign.Creatures.SpearGun; if (gun != null) { ids.Add(gun.GetInstanceID()); if (gun.ProjectileTransform != null) ids.Add(gun.ProjectileTransform.GetInstanceID()); }
            var station = hunt.Campaign.Mechanics.StationRoot; if (station != null) ids.Add(station.GetInstanceID());
            ids.Sort(); return string.Join(",", ids);
        }
        static void AssertActorSnapshot(TreasureHunt hunt)
        {
            var campaign = hunt.Campaign; var state = campaign.State; int fish = 0, coreSharks = 0;
            foreach (var pickup in hunt.Pickups)
                Check(pickup.visual != null && pickup.consumed == state.IsConsumed(pickup.id) && (!pickup.consumed || !pickup.visual.activeSelf), "pickup consumption/visibility reflects authenticated snapshot id=" + pickup.id);
            foreach (var actor in campaign.Creatures.Targets) if (!actor.IsAmbient)
            { if (actor.IsShark) coreSharks++; else fish |= 1 << actor.Species; }
            Check(fish == (state.FishTargetMask & ~state.Current.fishMask), "actual outstanding fish actors match saved species mask");
            Check(coreSharks == (state.RequiresShark && !state.SharkCaughtFor(0) ? 1 : 0) && (campaign.Creatures.SpearGun != null) == state.RequiresShark, "actual boss and tool presence match saved requirements");
            if (state.RequiresShark) Check(campaign.Creatures.SpearGun.ShotsFired == state.Current.spearShotsFired && campaign.Creatures.SpearGun.RecoveryPending == state.Current.pendingSpearRecovery, "rebuilt tool restores durable ammo and recovery state");
            Check(ReferenceEquals(Get(campaign.Mechanics, "boundRun"), state.Current), "mechanic runtime is rebound to restored current run");
        }
        static OceanSpearGun LastShotGun(Transform parent, Camera view, HandSwimController hands, out OceanCampaignState state)
        {
            state = new OceanCampaignState(); state.StartLevel(96); Check(state.AcquireTool(), "isolated recovery tool acquired");
            for (int i = 0; i < OceanCampaignState.SpearShotLimit - 1; i++) { Check(state.TryFireSpear(), "prepare isolated final durable round"); Check(state.TryRecoverSpear(), "recover each isolated previous round"); }
            var gun = OceanSpearGun.Create(parent, view, hands, state, null, view.transform.position, Array.Empty<Bounds>(), (a, b) => true, null);
            Vector3 palm = view.transform.position + view.transform.forward * .6f;
            int frame = 10000;
            for (int i = 0; i < 16; i++) gun.DrivePoseForValidation(palm, view.transform.forward, true, true, true, true, .05f, .02f, true, frame++);
            for (int i = 0; i < 8; i++) gun.DrivePoseForValidation(palm, view.transform.forward, true, true, true, true, .95f, .02f, true, frame++);
            Check(gun.InFlight && state.Current.spearShotsFired == OceanCampaignState.SpearShotLimit && state.Current.pendingSpearRecovery && !state.Failed, "actual final projectile remains unresolved with durable recovery pending");
            return gun;
        }
        static void InFlightRefusal(OceanCampaignController campaign, OceanCampaignPanel panel, HandSwimController hands, Camera view, Func<string> actors)
        {
            var previousGun = campaign.Creatures.SpearGun; OceanSpearGun gun = null;
            try
            {
                gun = LastShotGun(campaign.Creatures.transform, view, hands, out OceanCampaignState gunState);
                Property(campaign.Creatures, "SpearGun", gun);
                var owner = campaign.State; var run = owner.Current; string before = JsonUtility.ToJson(owner.Save), gunBefore = JsonUtility.ToJson(gunState.Save), ids = actors();
                byte[] primary = File.ReadAllBytes(Path.Combine(campaign.SaveDirectory, OceanSaveStore.FileName)), backup = File.ReadAllBytes(Path.Combine(campaign.SaveDirectory, OceanSaveStore.BackupFileName));
                Check(panel.ClickForValidation("retry_save_load"), "unresolved final-shot refusal uses actual production retry callback");
                Check(!campaign.EnvironmentTransitioning && ReferenceEquals(campaign.State, owner) && ReferenceEquals(owner.Current, run) && JsonUtility.ToJson(owner.Save) == before && actors() == ids && JsonUtility.ToJson(gunState.Save) == gunBefore && gun.InFlight, "in-flight refusal preserves actual campaign, actor IDs, projectile and pending final-shot state");
                Check(HasText(panel, "等待鱼叉结束后再重新读取") && SameBytes(primary, File.ReadAllBytes(Path.Combine(campaign.SaveDirectory, OceanSaveStore.FileName))) && SameBytes(backup, File.ReadAllBytes(Path.Combine(campaign.SaveDirectory, OceanSaveStore.BackupFileName))), "in-flight refusal explains how to retry and changes no file bytes");
            }
            finally { Property(campaign.Creatures, "SpearGun", previousGun); if (gun != null) { gun.gameObject.SetActive(false); UnityEngine.Object.DestroyImmediate(gun.gameObject); } }
        }

        /// <summary>EditMode production callback negative, independent of the batch scene hook.</summary>
        public static void ProductionInFlightCallback()
        {
            Assertions = 0;
            string directory = Path.Combine(Path.GetTempPath(), "ocean-retry-inflight-" + Guid.NewGuid().ToString("N"));
            var root = new GameObject("Isolated production in-flight retry callback"); OceanCampaignController controller = null;
            try
            {
                var view = root.AddComponent<Camera>(); view.enabled = false; view.transform.position = new Vector3(0, 1.6f, 0);
                var hands = root.AddComponent<HandSwimController>(); hands.enabled = false;
                var state = new OceanCampaignState(); state.StartLevel(96);
                Check(OceanSaveStore.TrySave(directory, state.Save, out string error), "in-flight callback authenticated fixture: " + error);
                controller = root.AddComponent<OceanCampaignController>(); controller.enabled = false;
                var panel = root.AddComponent<OceanCampaignPanel>(); panel.Initialize(view, hands, state, controller.HandleAction, false);
                var creatures = root.AddComponent<OceanCampaignCreatures>();
                Property(controller, "State", state); Property(controller, "Panel", panel); Property(controller, "Creatures", creatures); Property(controller, "SaveDirectory", directory);
                Set(controller, "initialized", true); Set(controller, "writable", false); Set(controller, "warnedSave", true);
                panel.SetSaveRecoveryStatus(true, OceanSaveLoadStatus.Corrupt, false); panel.SetAvailable(true); panel.ShowSaveRecovery();
                InFlightRefusal(controller, panel, hands, view, () => creatures.SpearGun == null ? "" : creatures.SpearGun.GetInstanceID().ToString());
            }
            finally
            {
                if (controller != null) Set(controller, "initialized", false);
                UnityEngine.Object.DestroyImmediate(root); if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
#endif

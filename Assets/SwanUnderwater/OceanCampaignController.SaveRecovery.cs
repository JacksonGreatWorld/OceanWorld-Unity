using System;
using System.Collections;
using UnityEngine;

namespace SwanUnderwater
{
    public sealed partial class OceanCampaignController
    {
        /// <summary>Retry never authorizes a write. The current launch remains read-only, even after a verified backup read.</summary>
        void RetrySaveLoadReadOnly()
        {
            if (writable || environmentTransitioning) return;
            // A flying last shot has not resolved its durable ammo outcome yet. Rebuilding
            // its gun could incorrectly turn a pending hit into an exhausted-ammo failure.
            if (Creatures != null && Creatures.SpearGun != null && Creatures.SpearGun.InFlight)
            {
                Panel.SetSaveRecoveryStatus(true, OceanSaveLoadStatus.Loaded, false, OceanSaveRecoveryCopy.SpearInFlight);
                Panel.ShowSaveRecovery();
                return;
            }
            if (!OceanSaveStore.TryReloadExistingReadOnly(SaveDirectory, out OceanSaveData candidate,
                out OceanSaveLoadStatus status, out string error))
            {
                Panel.SetSaveRecoveryStatus(true, status, false);
                Panel.ShowSaveRecovery();
                Debug.LogWarning("[Campaign] save_retry_read_only failed status=" + status + " original_files_unchanged=true detail=" + error);
                return;
            }
            // Authentication and validation finish before touching world actors or the shared state.
            var restored = new OceanCampaignState(candidate);
            if (restored.Current == null) restored.StartLevel(1);
            environmentTransitioning = true;
            levelSwitchOperation = ReloadVerifiedSave(restored.Save, status);
            levelSwitchCoroutine = StartCoroutine(levelSwitchOperation);
        }

        IEnumerator ReloadVerifiedSave(OceanSaveData candidate, OceanSaveLoadStatus status)
        {
            OceanSaveData previous = OceanCampaignState.Copy(State.Save);
            Vector3 previousSpawn = spawn, previousEye = view.transform.position, previousForward = view.transform.forward;
            bool wasSuppressed = hands.InteractionSuppressed, finalized = false;
            try
            {
                leaderboard.Close(); Panel.SetAvailable(false); Panel.SuspendPracticeForSaveReload();
                hands.SetMenuLocomotionPaused(true); hands.SetInteractionSuppressed(true);
                resultAt = double.PositiveInfinity;
                Exception failure = null;
                yield return RunOwnedSwitchStage(PrepareReadOnlyReload(previous.currentRun.level, candidate), error => failure = error);
                if (failure != null)
                {
                    State.ApplyVerifiedSave(previous); spawn = previousSpawn;
                    Exception rollbackFailure = null;
                    yield return RunOwnedSwitchStage(RestoreLevel(previous.currentRun.level), error => rollbackFailure = error);
                    environmentFaulted = rollbackFailure != null;
                    if (rollbackFailure == null) hands.TryRelocateHead(previousEye, previousForward);
                    else Debug.LogWarning("[Campaign] save_retry_scene_rollback_failed detail=" + rollbackFailure);
                    Debug.LogWarning("[Campaign] save_retry_scene_failed original_files_unchanged=true detail=" + failure);
                }
                else
                {
                    environmentFaulted = false;
                    committedScoreRun = null; LastSaveSucceeded = false;
                    CommitAchievementHistory(OceanCampaignState.Copy(State.Save), false);
                    pendingSaveNotice = null; warnedSave = true;
                }
                Exception revealFailure = null;
                yield return RunOwnedSwitchStage(FinishPassage(), error => revealFailure = error);
                if (revealFailure != null) world.AbortEnvironmentTransition();
                lastStars = State.Stars; lastTimedOut = State.TimedOut; lastFailed = State.Failed; lastSecond = -1;
                Panel.SetSaveRecoveryStatus(true, status, failure == null, failure == null ? null : OceanSaveRecoveryCopy.SceneFailed);
                finalized = true; environmentTransitioning = false;
                hands.SetInteractionSuppressed(wasSuppressed); Panel.SetAvailable(hunt.CoreActive);
                Panel.ShowSaveRecovery(); resumedFrame = Time.frameCount;
                Debug.Log("[Campaign] save_retry_read_only applied=" + (failure == null) + " status=" + status + " original_files_unchanged=true writes_disabled=true");
            }
            finally
            {
                CleanupMapPassage(); levelSwitchOperation = null; levelSwitchCoroutine = null; environmentTransitioning = false;
                if (!finalized)
                {
                    State.ApplyVerifiedSave(previous); spawn = previousSpawn; environmentFaulted = true;
                    if (world != null) world.AbortEnvironmentTransition();
                    if (Panel != null) Panel.SetSaveRecoveryStatus(true, status, false, OceanSaveRecoveryCopy.SceneFailed);
                }
                if (hands != null) hands.SetInteractionSuppressed(wasSuppressed);
                if (!finalized && Panel != null) { Panel.SetAvailable(hunt != null && hunt.CoreActive); Panel.ShowSaveRecovery(); }
            }
        }

        IEnumerator PrepareReadOnlyReload(int previousLevel, OceanSaveData candidate)
        {
            int level = candidate.currentRun.level;
            int presentationLevel = OceanCampaignAvailability.EntryLevel(level);
            if (OceanEnvironmentTransition.Required(OceanCampaignAvailability.EntryLevel(previousLevel), presentationLevel))
            {
                mapPassage = OceanEnvironmentTransition.Create(view); world.ExternalTransitionCoverActive = true;
                yield return mapPassage.Cover();
            }
            bool prepared = false;
            yield return world.PrepareEnvironmentForLevel(presentationLevel, value => prepared = value);
            if (!prepared) throw new InvalidOperationException("Reloaded save environment was not prepared.");
            spawn = world.ActiveSpawn; Physics.SyncTransforms();
            if (!OceanCampaignAvailability.IsOpen(level))
            {
                State.ApplyVerifiedSave(candidate);
                Creatures.ClearForUnavailableLevel(); Mechanics.ClearForUnavailableLevel();
                hunt.ClearForUnavailableLevel(); configured = true;
                yield break;
            }
            yield return hunt.PrepareExpeditionHabitat(spawn);
            Vector3 eye = new Vector3(spawn.x, UnderwaterWorld.SampleRenderedSeabedHeight(spawn.x, spawn.z) + 1.5f, spawn.z);
            double deadline = Time.realtimeSinceStartupAsDouble + 2;
            while (!hands.TryRelocateHead(eye, Vector3.forward))
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline) throw new InvalidOperationException("Fresh head pose unavailable for safe save reload.");
                yield return null;
            }
            State.ApplyVerifiedSave(candidate);
            world.ResetEnvironmentReveal(); hunt.RebuildDiscoveries(spawn, State);
            Creatures.ConfigureLevel(spawn); Mechanics.ConfigureLevel(spawn); configured = true;
            Physics.SyncTransforms();
        }
    }
}

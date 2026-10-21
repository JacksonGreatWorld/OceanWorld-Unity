#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Real Unity objects, menu replay and playback/tick/render/cleanup callbacks.
    /// Camera submissions are injected callbacks, not proof of GPU or native VST visibility.</summary>
    public static class OceanEntryPlaybackTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const string SeenKey = "ocean_interactive_tutorial_seen_v1";
        public static int Assertions { get; private set; }
        static void Check(bool value, string message)
        { Assertions++; if (!value) throw new InvalidOperationException("Entry playback: " + message); }
        static void Set(object item, string name, object value) => item.GetType().GetField(name, Private).SetValue(item, value);
        static T Get<T>(object item, string name) => (T)item.GetType().GetField(name, Private).GetValue(item);
        static void Call(object item, string name, params object[] args) => item.GetType().GetMethod(name, Private).Invoke(item, args);
        static void Tick(OceanEntry entry) => Call(entry, "TickCinematic", .05f);
        static void Render(OceanEntry entry, Camera camera) => Call(entry, "Rendered", default(ScriptableRenderContext), camera);

        public static void RunAll()
        {
            Assertions = 0;
            if (OceanEntry.Instance != null) throw new InvalidOperationException("Playback checks require no active entry.");
            float volume = AudioListener.volume;
            try
            {
                SkipRetainsLoadingAndRenderedCover();
                EntryPointerNegativeGuards();
                SharedJointValidation();
                FullReplayUsesMenuCallbackWithoutCampaignEvents();
            }
            finally { AudioListener.volume = volume; }
            Debug.Log("[OceanEntryPlaybackTests] PASS " + Assertions + " assertions; production callbacks, no headset presentation claim.");
        }

        static void SkipRetainsLoadingAndRenderedCover()
        {
            using (var f = new Fixture())
            {
                OceanEntry entry = f.Show();
                var controls = entry.GetComponent<OceanEntryControls>();
                Check(controls == null && f.View.GetComponentsInChildren<Button>(true).Length == 0,
                    "ordinary entry has no skip prompt, button or hidden camera UI hit target");
                entry.ApplyFocusForValidation(false); entry.RequestSkip();
                Check(!entry.SkipRequested, "an unfocused playback request cannot skip");
                entry.ApplyFocusForValidation(true); Call(entry, "OnApplicationPause", true); entry.RequestSkip();
                Check(!entry.SkipRequested, "a paused playback request cannot skip");
                Call(entry, "OnApplicationPause", false);
                entry.RequestSkip();
                Check(entry.SkipRequested && entry.CinematicPhase == OceanEntrySequence.Beat.Warp && !entry.WorldBuildAllowed && !entry.WorldPlayable,
                    "programmatic compatibility request queues opaque cover without granting readiness, loading or controls");
                Check(!entry.RequestSkip(), "a duplicate request cannot advance phases twice");
                Render(entry, f.View); Render(entry, f.View); Tick(entry);
                Check(!entry.WorldBuildAllowed, "preparing the Warp is not a camera submission");
                Render(entry, f.OtherView);
                Check(!entry.WorldBuildAllowed, "an unrelated camera cannot approve the cover");
                entry.ApplyFocusForValidation(false); Render(entry, f.View);
                Check(!entry.WorldBuildAllowed, "focus loss cannot approve a pending opaque callback");
                entry.ApplyFocusForValidation(true); Tick(entry);
                Render(entry, f.View);
                Check(entry.WorldBuildAllowed, "the tracked camera's prepared opaque callback allows the existing loader");
                for (int i = 0; i < 100; i++) Tick(entry);
                Check(entry.CinematicPhase == OceanEntrySequence.Beat.Warp && !entry.WorldPlayable,
                    "a skip remains loading-protected indefinitely until Complete");
                entry.Complete(); Tick(entry);
                Check(entry.CinematicPhase == OceanEntrySequence.Beat.Reveal && !entry.WorldPlayable &&
                    f.View.cullingMask == f.Mask && f.View.backgroundColor == f.Background,
                    "ready enters the ordinary fade and restores the prior camera, without early controls");
                var lease = Get<EntryPassthrough>(entry, "passthrough");
                Check(Get<bool>(lease, "closed") && !Get<bool>(lease, "wantsStart"), "skip closes the temporary VST lease and automatic resume");
                for (int i = 0; i < 20; i++) Tick(entry);
                Check(!entry.WorldPlayable, "one second is still inside the normal 1.3-second reveal");
                int safety = 100;
                while (entry != null && safety-- > 0) Tick(entry);
                Check(safety > 0 && OceanEntry.Instance == null, "playback request completes without any hand-tracking requirement");
            }
            var failed = new OceanEntrySequence(); failed.Fail();
            Check(!failed.RequestSkip() && !failed.ControlsAllowed, "skip cannot recover a failed loader into gameplay");
        }

        static void EntryPointerNegativeGuards()
        {
            using (var f = new Fixture())
            {
                Set(f.Hands, "trackingSpace", f.Root.transform);
                Set(f.Hands, "handSampleFrame", Time.frameCount);
                Check(!Pointer(f.Hands), "no entry cannot bypass suppressed world input");
                OceanEntry entry = f.Show();
                Set(f.Hands, "interactionSuppressed", true);
                Check(!Pointer(f.Hands), "an entry cannot invent a pointer for untracked hands");
                typeof(HandSwimController).GetProperty("LeftHandActive").SetValue(f.Hands, true);
                Set(f.Hands, "trackingSpace", null);
                Check(!Pointer(f.Hands), "entry pointers need a real tracking-space transform");
                Set(f.Hands, "trackingSpace", f.Root.transform);
                foreach (string flag in new[] { "appPaused", "appFocused" })
                {
                    Set(f.Hands, flag, flag == "appPaused");
                    Check(!Pointer(f.Hands), "entry pointer rejects " + flag + " unavailability");
                    Set(f.Hands, flag, flag != "appPaused");
                }
                Set(f.Hands, "handSampleFrame", Time.frameCount - 1);
                Check(!Pointer(f.Hands), "stale hand frames cannot operate entry controls");
                Set(f.Hands, "handSampleFrame", Time.frameCount); Set(f.Hands, "relocationFrame", Time.frameCount);
                Check(!Pointer(f.Hands), "a relocation frame cannot become an entry pinch");
                Set(f.Hands, "relocationFrame", -10); f.Hands.enabled = false;
                Check(!Pointer(f.Hands), "a disabled tracking controller cannot read entry input");
                f.Hands.enabled = true;
                var sequence = Get<OceanEntrySequence>(entry, "sequence");
                while (!sequence.ControlsAllowed) sequence.Step(.05f, true);
                Set(entry, "ready", true); Set(entry, "cameraRestored", true);
                Check(entry.WorldPlayable && !Pointer(f.Hands), "world-playable entry captions cannot use the suppression bypass");
            }
        }
        static bool Pointer(HandSwimController hands) => hands.TryGetEntryHandPointer(OceanHandSide.HandLeft, out _, out _, out _);

        static void SharedJointValidation()
        {
            var frame = OceanHandFrame.Allocate(); frame.isActive = 1;
            int index = (int)OceanHandJointId.JointThumbTip;
            frame.jointLocations[index] = new OceanHandJoint { locationStatus = OceanHandLocationStatus.PositionValid | OceanHandLocationStatus.PositionTracked,
                pose = new Pose(new Vector3(.1f, .2f, .3f), Quaternion.identity) };
            bool Read(OceanHandFrame sample, int i) => (bool)typeof(HandSwimController).GetMethod("TryReadJoint", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { sample, i, Vector3.zero });
            Check(Read(frame, index), "the shared entry/menu path accepts a finite tracked joint");
            Check(!Read(frame, -1) && !Read(frame, 99), "the shared path rejects negative and out-of-range joint indices");
            var invalid = frame; invalid.jointCount = 2;
            Check(!Read(invalid, index), "reported joint count bounds remain enforced");
            invalid = frame; invalid.jointLocations = new OceanHandJoint[2];
            Check(!Read(invalid, index), "truncated provider arrays cannot bypass joint bounds");
            invalid = frame; invalid.isActive = 0;
            Check(!Read(invalid, index), "inactive frames cannot supply entry pinch joints");
            frame.jointLocations[index].locationStatus = OceanHandLocationStatus.PositionValid;
            Check(!Read(frame, index), "valid but untracked positions do not qualify");
            frame.jointLocations[index].locationStatus |= OceanHandLocationStatus.PositionTracked;
            frame.jointLocations[index].pose = new Pose(new Vector3(float.NaN, 0, 0), Quaternion.identity);
            Check(!Read(frame, index), "nonfinite provider joints remain unusable for entry pinches");
        }

        static void FullReplayUsesMenuCallbackWithoutCampaignEvents()
        {
            using (var f = new Fixture())
            {
                var state = new OceanCampaignState(); state.Save.bestStars[0] = 3; state.StartLevel(20);
                state.Tick(600f, true); // A historical failed run is allowed to watch the presentation.
                object run = state.Current;
                string before = JsonUtility.ToJson(state.Save);
                int seen = PlayerPrefs.GetInt(SeenKey, 0), callbacks = 0;
                var panel = f.Root.AddComponent<OceanCampaignPanel>();
                panel.Initialize(f.View, f.Hands, state, (action, level) => callbacks++, false);
                panel.SetAvailable(true); panel.OpenMenu();
                Check(panel.ClickForValidation("show_comfort"), "open Settings via the visible production callback");
                for (int preset = 0; preset < 3; preset++)
                {
                    panel.SetTextScaleForValidation(preset); Canvas.ForceUpdateCanvases();
                    foreach (Text text in panel.GetComponentsInChildren<Text>())
                        if (text.gameObject.activeInHierarchy)
                            Check(text.preferredHeight <= text.rectTransform.rect.height + .5f, "Settings text fits preset " + preset + ": " + text.name);
                    var buttons = panel.GetButtonsForValidation();
                    foreach (var a in buttons) foreach (var b in buttons)
                    {
                        if (string.CompareOrdinal(a.id, b.id) >= 0) continue;
                        Rect ar = a.rect.rect, br = b.rect.rect;
                        var bc = a.rect.InverseTransformPoint(b.rect.TransformPoint(br.center));
                        if (a.rect.parent != b.rect.parent) continue;
                        Rect converted = new Rect(new Vector2(bc.x - br.width / 2, bc.y - br.height / 2), br.size);
                        Check(!ar.Overlaps(converted), "Settings hit regions do not overlap: " + a.id + "/" + b.id);
                    }
                }
                Check(panel.ClickForValidation("entry_replay"), "actual replay button creates a presentation");
                OceanEntry entry = OceanEntry.Instance;
                Check(entry != null && entry.IsReplay && panel.IsOpen && !panel.IsAvailableForDiagnostics && f.Hands.MenuLocomotionPaused,
                    "replay hides the same menu while leaving it logically open and locomotion paused");
                Check(ReferenceEquals(run, state.Current) && JsonUtility.ToJson(state.Save) == before && callbacks == 0,
                    "replay neither reconstructs the campaign nor dispatches start/DemoOpened/score actions");
                Check(!panel.ClickForValidation("entry_replay"), "hidden replay callback cannot start a second presentation");
                Set(entry, "appFocused", true);
                var visited = new HashSet<OceanEntrySequence.Beat>(); bool captionsBlocked = false;
                Render(entry, f.View); Render(entry, f.View);
                int safety = 2000;
                while (entry != null && safety-- > 0)
                {
                    visited.Add(entry.CinematicPhase);
                    if (entry.CinematicPhase == OceanEntrySequence.Beat.Welcome)
                        captionsBlocked |= !entry.WorldPlayable && OceanEntry.BlocksGameplay;
                    Tick(entry); if (entry != null) Render(entry, f.View);
                }
                Check(safety > 0 && captionsBlocked && visited.Contains(OceanEntrySequence.Beat.Approach) &&
                    visited.Contains(OceanEntrySequence.Beat.Invite) && visited.Contains(OceanEntrySequence.Beat.Warp) &&
                    visited.Contains(OceanEntrySequence.Beat.Reveal) && visited.Contains(OceanEntrySequence.Beat.Chapter),
                    "full replay includes the fish, cover, reveal and both captions while the menu is isolated");
                Check(panel.IsOpen && panel.IsAvailableForDiagnostics && panel.PageNameForDiagnostics == "Comfort" && f.Hands.MenuLocomotionPaused,
                    "production end callback restores the original Settings page/input isolation");
                Check(callbacks == 0 && ReferenceEquals(run, state.Current) && JsonUtility.ToJson(state.Save) == before &&
                    !panel.TutorialActive && !Get<bool>(panel, "automaticTutorialAllowed") && PlayerPrefs.GetInt(SeenKey, 0) == seen,
                    "full replay never awards, restarts an old run, completes a lesson or changes automatic tutorial eligibility");
                Check(panel.ClickForValidation("entry_replay"), "completed replay can be deliberately repeated");
                entry = OceanEntry.Instance; Set(entry, "appFocused", true);
                Check(entry.GetComponent<OceanEntryControls>() == null, "menu replay also has no skip prompt or controls");
                entry.RequestSkip(); Render(entry, f.View); Render(entry, f.View); Tick(entry);
                Check(!entry.WorldPlayable && !entry.WorldBuildAllowed, "ready-world replay skip still awaits its own opaque submission");
                Render(entry, f.View); safety = 100;
                while (entry != null && safety-- > 0) Tick(entry);
                Check(safety > 0 && panel.IsAvailableForDiagnostics && panel.PageNameForDiagnostics == "Comfort" &&
                    callbacks == 0 && JsonUtility.ToJson(state.Save) == before && !panel.TutorialActive && PlayerPrefs.GetInt(SeenKey, 0) == seen,
                    "the real replay skip/end callback preserves campaign, score dispatches and independent tutorial qualification");
                Check(panel.ClickForValidation("entry_replay"), "skipped replay also permits another deliberate viewing");
                entry = OceanEntry.Instance; entry.enabled = false; Call(entry, "OnDisable");
                Check(OceanEntry.Instance == null && panel.IsAvailableForDiagnostics && panel.PageNameForDiagnostics == "Comfort" &&
                    f.View.cullingMask == f.Mask && f.View.backgroundColor == f.Background,
                    "disable cancellation closes the VST/camera lease and restores the old menu");
                float frozen = entry.CinematicVisibleTime; entry.enabled = true; Tick(entry);
                Check(entry.CinematicVisibleTime == frozen, "a cancelled presentation cannot revive on enable");
                Object.DestroyImmediate(entry.gameObject);
                Check(callbacks == 0 && JsonUtility.ToJson(state.Save) == before, "cancel/duplicate cleanup cannot emit campaign callbacks");
                Check(panel.ClickForValidation("tutorial_repeat"), "ordinary four-step lesson remains independently available");
                panel.OpenMenu(); panel.ClickForValidation("show_comfort");
                Check(panel.TutorialActive && !panel.ClickForValidation("entry_replay"), "entry replay cannot interrupt or complete an active four-step lesson");
            }
        }

        sealed class Fixture : IDisposable
        {
            public readonly GameObject Root = new GameObject("Isolated entry playback fixture");
            public readonly Camera View, OtherView;
            public readonly HandSwimController Hands;
            public readonly Color Background = new Color(.08f, .13f, .21f, 1f);
            public readonly int Mask = ~0;
            public Fixture()
            {
                var camera = new GameObject("Playback camera"); camera.transform.SetParent(Root.transform, false);
                View = camera.AddComponent<Camera>(); View.enabled = false; View.aspect = 16f / 9f; View.fieldOfView = 60f;
                View.transform.position = Vector3.up * 1.6f; View.cullingMask = Mask; View.clearFlags = CameraClearFlags.SolidColor; View.backgroundColor = Background;
                var other = new GameObject("Unrelated camera"); other.transform.SetParent(Root.transform, false); OtherView = other.AddComponent<Camera>(); OtherView.enabled = false;
                Hands = Root.AddComponent<HandSwimController>();
            }
            public OceanEntry Show()
            { var entry = OceanEntry.Show(View); Set(entry, "appFocused", true); return entry; }
            public void Dispose()
            {
                if (OceanEntry.Instance != null) Object.DestroyImmediate(OceanEntry.Instance.gameObject);
                Object.DestroyImmediate(Root);
            }
        }
    }
}
#endif

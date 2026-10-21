#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>
    /// Drives production entry ticks, render callbacks and cleanup on temporary engine objects.
    /// Display availability is injected; no native SDK, GPU submission, headset or save access.
    /// </summary>
    public static class OceanEntryLifecycleTests
    {
        public static int Assertions { get; private set; }
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void RunAll()
        {
            Assertions = 0;
            UnavailableDisplayAndResume();
            CaptionsWaitForResumedDisplay();
            PauseDiscardsElapsedDelta();
            DisabledCallbackCannotApproveCover();
            AcceptedCoverCannotSurviveDisable();
            FailureCancelsCover(false);
            FailureCancelsCover(true);
            NormalPreviewStillAdvances();
            Debug.Log("OceanEntryLifecycleTests PASS: " + Assertions +
                " assertions; production ticks/callbacks with injected display availability. Native presentation and AVP reentry unverified.");
        }

        static void Check(bool value, string reason)
        {
            Assertions++;
            if (!value) throw new InvalidOperationException("OceanEntryLifecycleTests: " + reason);
        }

        static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, Private).SetValue(target, value);
        static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
        static void Call(OceanEntry entry, string method, params object[] args) =>
            typeof(OceanEntry).GetMethod(method, Private).Invoke(entry, args);
        static bool Near(float a, float b) => Mathf.Abs(a - b) < .00001f;

        static void UnavailableDisplayAndResume()
        {
            using (var f = new Fixture(OceanEntrySequence.Beat.Warp))
            {
                f.PrepareCover();
                float before = f.Entry.CinematicVisibleTime, time = f.Sequence.TimeInBeat;
                int frames = f.Entry.RenderedFrames;
                f.DisplayReady = false;
                f.Tick(5f);
                Check(Near(f.Entry.CinematicVisibleTime, before) && Near(f.Sequence.TimeInBeat, time),
                    "an unavailable compositor freezes the actual sequence despite historical rendered frames");
                Check(!Get<bool>(f.Entry, "fullCoverPrepared"), "unavailable display cancels the pending opaque frame");
                Check(Get<double>(f.Entry, "lastLoopAt") == 0 && Get<double>(f.Entry, "lastRenderAt") == 0,
                    "display loss discards timing-window baselines");
                f.Render();
                Check(f.Entry.RenderedFrames == frames && !f.Entry.WorldBuildAllowed,
                    "a callback while the display is unavailable cannot approve cover");
                f.DisplayReady = true;
                f.Tick(5f);
                Check(Near(f.Entry.CinematicVisibleTime, before) && Near(f.Sequence.TimeInBeat, time),
                    "the first resumed tick excludes the engine delta spanning display loss");
                f.Tick(.025f);
                Check(Near(f.Entry.CinematicVisibleTime, before + .025f), "the following visible tick resumes ordinary sequence time");
                f.Render();
                Check(f.Entry.WorldBuildAllowed, "a fresh prepared frame and available callback authorize world building");
            }
        }

        static void CaptionsWaitForResumedDisplay()
        {
            using (var f = new Fixture(OceanEntrySequence.Beat.Welcome))
            {
                float before = f.Entry.CinematicVisibleTime;
                f.DisplayReady = false;
                f.Tick(4f);
                Check(Near(f.Entry.CinematicVisibleTime, before), "post-reveal captions also wait for the current compositor");
                f.DisplayReady = true;
                f.Tick(4f);
                Check(Near(f.Entry.CinematicVisibleTime, before), "the first resumed caption tick rebuilds its delta baseline");
                f.Tick(.02f);
                Check(Near(f.Entry.CinematicVisibleTime, before + .02f), "visible caption timing resumes without consuming the hidden interval");
            }
        }

        static void PauseDiscardsElapsedDelta()
        {
            using (var f = new Fixture(OceanEntrySequence.Beat.Warp))
            {
                f.PrepareCover();
                float before = f.Entry.CinematicVisibleTime;
                Call(f.Entry, "OnApplicationPause", true);
                f.Tick(6f);
                Check(Near(f.Entry.CinematicVisibleTime, before) && !Get<bool>(f.Entry, "fullCoverPrepared"),
                    "the production pause hook freezes time and withdraws pending cover");
                Call(f.Entry, "OnApplicationPause", false);
                f.Tick(6f);
                Check(Near(f.Entry.CinematicVisibleTime, before), "the resumed engine delta cannot become cinematic time");
                f.Tick(.02f);
                Check(Near(f.Entry.CinematicVisibleTime, before + .02f), "the next unpaused tick advances normally");
            }
        }

        static void DisabledCallbackCannotApproveCover()
        {
            using (var f = new Fixture(OceanEntrySequence.Beat.Warp))
            {
                f.PrepareCover();
                int frames = f.Entry.RenderedFrames;
                f.Disable();
                f.Render();
                Check(f.Entry.RenderedFrames == frames && !f.Entry.WorldBuildAllowed,
                    "the real render callback ignores a disabled component with a prepared Warp");
                Check(!Get<bool>(f.Entry, "fullCoverPrepared") && !Get<bool>(f.Entry, "fullCoverRendered"),
                    "disable clears both pending and accepted cover state");
                Check(f.Camera.cullingMask == f.OriginalMask && f.Camera.clearFlags == CameraClearFlags.SolidColor &&
                    f.Camera.backgroundColor == f.OriginalBackground, "the production disable hook restores its camera snapshot");
                f.Entry.enabled = true;
                f.Render();
                Check(!f.Entry.WorldBuildAllowed, "reenabling and receiving a callback cannot reuse canceled pending cover");
            }
        }

        static void AcceptedCoverCannotSurviveDisable()
        {
            using (var f = new Fixture(OceanEntrySequence.Beat.Warp))
            {
                f.PrepareCover();
                f.Render();
                Check(f.Entry.WorldBuildAllowed, "the fixture first establishes cover through the production callback");
                f.Disable();
                Check(!f.Entry.WorldBuildAllowed && !Get<bool>(f.Entry, "fullCoverRendered"),
                    "a previously accepted frame does not survive entry cancellation");
                f.Entry.enabled = true;
                f.Render();
                Check(!f.Entry.WorldBuildAllowed, "an immediate restarted callback cannot reuse the old accepted frame");
            }
        }

        static void FailureCancelsCover(bool alreadyRendered)
        {
            using (var f = new Fixture(OceanEntrySequence.Beat.Warp))
            {
                f.PrepareCover();
                if (alreadyRendered) f.Render();
                int frames = f.Entry.RenderedFrames;
                float before = f.Entry.CinematicVisibleTime;
                using (var expected = new ExpectedFailureLog())
                {
                    f.Entry.Fail();
                    Check(expected.Count == 1, "the explicit failure reports exactly one expected initialization error");
                }
                Check(f.Entry.HasFailed && !f.Entry.WorldBuildAllowed &&
                    !Get<bool>(f.Entry, "fullCoverPrepared") && !Get<bool>(f.Entry, "fullCoverRendered"),
                    "the production failure path cancels pending and accepted cover; accepted=" + alreadyRendered);
                f.Render();
                f.Tick(5f);
                Check(f.Entry.RenderedFrames == frames && Near(f.Entry.CinematicVisibleTime, before),
                    "late callbacks and ticks cannot revive a failed entry; accepted=" + alreadyRendered);
            }
        }

        static void NormalPreviewStillAdvances()
        {
            using (var f = new Fixture(OceanEntrySequence.Beat.Warp))
            {
                // Editor preview and non-visionOS players retain the normal timing branch.
                Set(f.Entry, "visionOSDisplayReadyForChecks", null);
                float before = f.Entry.CinematicVisibleTime;
                f.Tick(.025f);
                Check(Near(f.Entry.CinematicVisibleTime, before + .025f), "the ordinary preview timing path retains its frame delta");
                float running = f.Entry.CinematicVisibleTime;
                Call(f.Entry, "OnApplicationPause", true);
                f.Tick(5f);
                Check(Near(f.Entry.CinematicVisibleTime, running), "the ordinary timing branch still ignores paused ticks");
                Call(f.Entry, "OnApplicationPause", false);
                f.Tick(5f);
                Check(Near(f.Entry.CinematicVisibleTime, running + .05f),
                    "non-visionOS timing preserves its existing clamped resume delta");
                Check(OceanEntry.WelcomeText == "《欢迎来到梦幻海底世界》", "the requested welcome title is preserved");
            }
        }

        sealed class Fixture : IDisposable
        {
            readonly GameObject root;
            readonly Material warp;
            public readonly OceanEntry Entry;
            public readonly Camera Camera;
            public readonly OceanEntrySequence Sequence;
            public readonly int OriginalMask = ~(1 << OceanEntry.IntroLayer);
            public readonly Color OriginalBackground = new Color(.08f, .13f, .21f, 1f);
            public bool DisplayReady = true;

            public Fixture(OceanEntrySequence.Beat beat)
            {
                root = new GameObject("Temporary entry lifecycle checks") { hideFlags = HideFlags.HideAndDontSave };
                Entry = root.AddComponent<OceanEntry>();
                var eye = new GameObject("Synthetic tracked camera"); eye.transform.SetParent(root.transform, false);
                Camera = eye.AddComponent<Camera>(); Camera.enabled = false;
                Camera.cullingMask = 1 << OceanEntry.IntroLayer;
                Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = Color.clear;
                Set(Entry, "view", Camera); Set(Entry, "originalMask", OriginalMask);
                Set(Entry, "originalClear", CameraClearFlags.SolidColor); Set(Entry, "originalBackground", OriginalBackground);
                Set(Entry, "anchored", true); Set(Entry, "encounterForward", Vector3.forward);
                var lease = new GameObject("Inactive synthetic passthrough lease"); lease.transform.SetParent(root.transform, false);
                Set(Entry, "passthrough", lease.AddComponent<EntryPassthrough>());
                var tunnel = new GameObject("Real Warp material holder"); tunnel.transform.SetParent(root.transform, false);
                Shader shader = Resources.Load<Shader>("Shaders/OceanEntryWarp");
                if (shader == null) throw new InvalidOperationException("Lifecycle checks require the existing Warp shader.");
                warp = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                Set(Entry, "tunnel", tunnel); Set(Entry, "tunnelMaterial", warp);
                var captions = new GameObject("Unused visual captions"); captions.transform.SetParent(root.transform, false);
                var titles = captions.AddComponent<OceanCinematicTitles>();
                Set(titles, "disposed", true); Set(Entry, "titles", titles); // No glyph rendering is asserted by these checks.
                Set(Entry, "nextLog", double.PositiveInfinity);
                Set(Entry, "visionOSDisplayReadyForChecks", (Func<bool>)(() => DisplayReady));
                Sequence = Get<OceanEntrySequence>(Entry, "sequence");
                int safety = 2000;
                while (Sequence.Current != beat && safety-- > 0) Sequence.Step(.05f, true);
                if (Sequence.Current != beat) throw new InvalidOperationException("Could not establish entry test phase " + beat);
                Set(Entry, "cameraRestored", Sequence.WorldRevealed);
                Call(Entry, "BeginTiming");
                Render(); Render();
            }

            public void Tick(float delta) => Call(Entry, "TickCinematic", delta);
            public void Render() => Call(Entry, "Rendered", default(ScriptableRenderContext), Camera);
            public void PrepareCover()
            {
                int safety = 100;
                while (!Get<bool>(Entry, "fullCoverPrepared") && safety-- > 0) Tick(.05f);
                if (!Get<bool>(Entry, "fullCoverPrepared") || warp.GetFloat("_Opacity") < .9999f)
                    throw new InvalidOperationException("The production tick did not prepare a fully opaque Warp.");
            }
            public void Disable()
            {
                Entry.enabled = false;
                // Outside Play mode Unity may not deliver ordinary MonoBehaviour hooks.
                // Call the production cleanup as well; it must be safe if already delivered.
                Call(Entry, "OnDisable");
            }
            public void Dispose()
            {
                // Fixtures own these temporary objects; production OnDestroy must not queue
                // Play-mode Destroy calls from the Editor build-check entry point.
                Set(Entry, "passthrough", null); Set(Entry, "titles", null); Set(Entry, "tunnelMaterial", null);
                Object.DestroyImmediate(root); Object.DestroyImmediate(warp);
            }
        }

        // Capture only the deliberate Fail() diagnostic. Every other log/exception is
        // forwarded unchanged, and an absent or extra expected error fails the check.
        sealed class ExpectedFailureLog : ILogHandler, IDisposable
        {
            readonly ILogHandler previous = Debug.unityLogger.logHandler;
            public int Count { get; private set; }
            public ExpectedFailureLog() { Debug.unityLogger.logHandler = this; }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                string text = string.Format(format, args);
                if (type == LogType.Error && text == "[OceanEntry] initialization failed before world readiness at 开始") { Count++; return; }
                previous.LogFormat(type, context, format, args);
            }
            public void LogException(Exception exception, Object context) => previous.LogException(exception, context);
            public void Dispose() { Debug.unityLogger.logHandler = previous; }
        }
    }
}
#endif

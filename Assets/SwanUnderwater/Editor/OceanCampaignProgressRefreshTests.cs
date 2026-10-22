#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater.EditorTests
{
    /// <summary>
    /// Real production UGUI on an isolated in-memory campaign. Can be copied unchanged
    /// to the baseline: absence of RefreshProgress selects its original Refresh path.
    /// Allocation/timing measurements are Editor current-thread costs, not device FPS.
    /// </summary>
    public static class OceanCampaignProgressRefreshTests
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const int Samples = 200;
        const int AllocationCapacity = 131072;
        public static int Assertions { get; private set; }
        public static Report LastReport { get; private set; }
        [Serializable] public sealed class Measurement
        {
            public string path;
            public bool sampled, allocationAvailable;
            public int calls, fullRefreshCalls, progressRefreshCalls;
            public long allocatedBytes = -1, elapsedTicks;
            public int allocationSamples = -1;
            public double milliseconds;
        }
        [Serializable] public sealed class Report
        {
            public bool success, candidatePresent;
            public string utc, unity, metric, selectedPath;
            public int assertions, levelsChecked, fixtureTextCount, allocationProbeSamples;
            public bool fixtureInitialized, allocationRequested, allocationAvailable, allocationImprovementVerified,
                allocationProbeValid, legacyThreadCounterAvailable;
            public long allocationProbeBytes = -1, legacyThreadProbeBytes = -1, allocationProbeValue = -1;
            public string allocationSource, allocationUnavailableReason, allocationProbeUnit, allocationProbeDataType;
            public Measurement originalFullRefresh, selectedRefresh;
        }
        struct TextState
        {
            public Text label;
            public string content;
            public Color color;
        }
        static void Check(bool value, string description)
        { Assertions++; if (!value) throw new InvalidOperationException("Campaign progress refresh: " + description); }

        sealed class Fixture : IDisposable
        {
            public readonly GameObject root;
            public readonly OceanCampaignPanel panel;
            public readonly OceanCampaignState state;
            public readonly Action selected;
            public readonly bool candidate;
            public readonly Transform hud;
            readonly float masterVolume = AudioListener.volume;
            public Fixture()
            {
                root = new GameObject("Isolated progress UI validation (no save directory)");
                var cameraObject = new GameObject("Progress UI fixture camera");
                cameraObject.transform.SetParent(root.transform, false);
                var view = cameraObject.AddComponent<Camera>(); view.enabled = false;
                view.transform.position = Vector3.up * 1.6f;
                var hands = root.AddComponent<HandSwimController>(); hands.enabled = false;
                state = new OceanCampaignState(); state.StartLevel(1);
                panel = root.AddComponent<OceanCampaignPanel>(); panel.enabled = false;
                panel.Initialize(view, hands, state, (action, level) => { if (action == "resume") panel.CloseMenu(); });
                panel.SkipTutorialForValidation(); panel.SetAvailable(true);
                LastReport.fixtureInitialized = (bool)typeof(OceanCampaignPanel).GetField("initialized", Fields).GetValue(panel);
                LastReport.fixtureTextCount = root.GetComponentsInChildren<Text>(true).Length;
                Check(LastReport.fixtureInitialized, "actual production Initialize completed synchronously");
                Check(LastReport.fixtureTextCount >= 100, "fixture contains actual built production UGUI pages, not an empty component");
                hud = ((GameObject)typeof(OceanCampaignPanel).GetField("hudRoot", Fields).GetValue(panel)).transform;
                MethodInfo method = typeof(OceanCampaignPanel).GetMethod("RefreshProgress", Fields);
                candidate = method != null;
                selected = candidate ? (Action)Delegate.CreateDelegate(typeof(Action), panel, method) : panel.Refresh;
                Close();
            }
            public void Close()
            {
                panel.CloseMenu();
                typeof(OceanCampaignPanel).GetField("openAmount", Fields).SetValue(panel, 0f);
                panel.SetAvailable(false); panel.SetAvailable(true);
            }
            public void Open(string page)
            {
                panel.OpenMenu();
                typeof(OceanCampaignPanel).GetField("openAmount", Fields).SetValue(panel, 1f);
                MethodInfo show = typeof(OceanCampaignPanel).GetMethod("ShowPage", Fields);
                show.Invoke(panel, new[] { Enum.Parse(show.GetParameters()[0].ParameterType, page) });
                typeof(OceanCampaignPanel).GetMethod("ApplyExpansionPose", Fields).Invoke(panel, null);
            }
            public void Dispose()
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                AudioListener.volume = masterVolume;
            }
        }

        public static void RunAll()
        {
            Assertions = 0;
            LastReport = new Report {
                utc = DateTimeOffset.UtcNow.ToString("O"), unity = Application.unityVersion,
                metric = "200 changing-countdown refresh calls; actual production UGUI; production refresh-call counts and Stopwatch elapsed time; optional allocation bytes require an independent positive control; not headset/compositor FPS",
                allocationSource = "not requested",
                allocationUnavailableReason = "Allocation benchmark requires explicit -codexProgressRefreshAllocationBenchmark; unknown bytes use -1, never zero."
            };
            try
            {
                using (var fixture = new Fixture())
                {
                    LastReport.candidatePresent = fixture.candidate;
                    LastReport.selectedPath = fixture.candidate ? "RefreshProgress" : "Refresh (baseline fallback)";
                    for (int level = 1; level <= 20; level++) CheckLevel(fixture, level);
                    CheckSharkProgress(fixture);
                    CheckRecoveryAndLockedHistory(fixture);
                    CheckPageOpenAfterCollection(fixture);
                    Measure(fixture);
                }
                LastReport.success = true;
            }
            finally
            {
                LastReport.assertions = Assertions;
                string json = JsonUtility.ToJson(LastReport, true);
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++)
                    if (args[i] == "-codexProgressRefreshReport")
                    {
                        string path = Path.GetFullPath(args[i + 1]);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, json);
                    }
                UnityEngine.Debug.Log("[OceanCampaignProgressRefreshTests] " + json);
            }
        }

        static void CheckLevel(Fixture fixture, int level)
        {
            fixture.Close(); fixture.state.StartLevel(level); fixture.panel.Refresh();
            Check(fixture.panel.GuideActionForValidation.Contains("第" + level + "关"), "actual task strip contains current level " + level);
            fixture.state.Tick(1.05f, true);
            AssertEquivalent(fixture, "closed HUD level " + level);
            // AssertEquivalent calls the reference after checking the selected path, so
            // the hidden-page sentinel is tested in a separate, selected-only call.
            var hidden = Snapshot(fixture, false);
            fixture.state.Tick(1.05f, true);
            fixture.selected();
            if (fixture.candidate) Check(Same(hidden, Snapshot(fixture, false)), "closed countdown does not write hidden text at level " + level);
            fixture.Open("Menu"); AssertEquivalent(fixture, "open menu level " + level);
            fixture.Open("Targets"); AssertEquivalent(fixture, "target list level " + level);
            fixture.panel.ShowResult(false); AssertEquivalent(fixture, "result view level " + level);
            LastReport.levelsChecked++;
        }

        static void CheckSharkProgress(Fixture fixture)
        {
            fixture.Close(); fixture.state.StartLevel(5); fixture.panel.Refresh();
            Check(fixture.state.AcquireTool(), "isolated shark fixture acquires its tool");
            fixture.panel.Refresh(); // Production acquisition still sends an explicit event refresh.
            AssertEquivalent(fixture, "acquired tool HUD");
            for (int shot = 0; shot < 10; shot++)
            {
                Check(fixture.state.TryFireSpear(), "isolated shot " + shot + " accepted");
                Check(fixture.state.HitShark(0), "isolated validated hit " + shot + " accepted");
                AssertEquivalent(fixture, "remaining ammunition and hits " + shot);
            }
            Check(fixture.state.CatchShark(0), "isolated completed shark can be caught");
            fixture.panel.ShowResult(false); AssertEquivalent(fixture, "shark completion result");
            fixture.Close(); fixture.state.StartLevel(5); Check(fixture.state.AcquireTool(), "isolated failed-ammo fixture owns tool");
            for (int shot = 0; shot < fixture.state.ShotLimit; shot++) Check(fixture.state.TryFireSpear(), "emptying isolated round " + shot);
            Check(fixture.state.FinalizeSpearAmmo(false), "isolated ammunition failure completes after projectiles resolve");
            fixture.panel.ShowResult(false); AssertEquivalent(fixture, "ammunition failure result");
            fixture.Close(); fixture.state.StartLevel(1); fixture.state.Tick(1801f, true);
            Check(fixture.state.Failed, "isolated timer fixture reaches failure");
            fixture.panel.ShowResult(false); AssertEquivalent(fixture, "timeout result");
        }

        static void CheckRecoveryAndLockedHistory(Fixture fixture)
        {
            fixture.state.StartLevel(1);
            fixture.panel.SetSaveRecoveryStatus(true, OceanSaveLoadStatus.Corrupt, false);
            fixture.Open("Menu"); AssertEquivalent(fixture, "read-only save notice remains available");
            fixture.state.StartLevel(21); fixture.panel.Refresh();
            fixture.Open("Menu"); AssertEquivalent(fixture, "unopened historical level preserves recovery notice");
            fixture.Open("Targets");
            Check(fixture.panel.PageNameForDiagnostics != "Confirm", "a progress refresh never opens an unavailable level confirmation");
            fixture.selected();
            Check(fixture.state.Current.level == 21, "presentation never rewrites unavailable historical run");
            fixture.panel.SetSaveRecoveryStatus(false, OceanSaveLoadStatus.NewSave, false);
        }

        static void CheckPageOpenAfterCollection(Fixture fixture)
        {
            fixture.Close(); fixture.state.StartLevel(1); fixture.panel.Refresh();
            Check(fixture.state.CollectShell(0, 0), "isolated shell mutation accepted"); fixture.panel.Refresh();
            fixture.panel.ShowCollectionForValidation(0);
            var visible = Snapshot(fixture, true);
            bool collected = false;
            foreach (var label in visible) if (label.content == "已收藏") collected = true;
            Check(collected, "opening a collection shows the immediate event refresh");
            AssertEquivalent(fixture, "collection status after a closed-menu pickup");
            foreach (string page in new[] { "Levels", "CollectionHub", "Naturalist", "Comfort", "ComfortAudio", "SaveRecovery", "Achievements" })
            { fixture.Open(page); AssertEquivalent(fixture, "page selection " + page); }
        }

        static void AssertEquivalent(Fixture fixture, string context)
        {
            string stateBefore = JsonUtility.ToJson(fixture.state.Save);
            fixture.selected();
            var selected = Snapshot(fixture, true);
            Check(selected.Count >= 2, context + " has actual visible task strip or page text");
            fixture.panel.Refresh();
            Check(Same(selected, Snapshot(fixture, true)), context + " matches the original full-refresh visible text/color");
            Check(stateBefore == JsonUtility.ToJson(fixture.state.Save), context + " does not mutate campaign or history");
        }

        static List<TextState> Snapshot(Fixture fixture, bool visible)
        {
            var result = new List<TextState>();
            foreach (Text text in fixture.root.GetComponentsInChildren<Text>(true))
            {
                bool shown = text.gameObject.activeInHierarchy;
                if (shown != visible || (!visible && text.transform.IsChildOf(fixture.hud))) continue;
                result.Add(new TextState { label = text, content = text.text, color = text.color });
            }
            return result;
        }
        static bool Same(List<TextState> left, List<TextState> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
                if (left[i].label != right[i].label || left[i].content != right[i].content || left[i].color != right[i].color) return false;
            return true;
        }

        static void Measure(Fixture fixture)
        {
            fixture.Close(); fixture.state.StartLevel(6); fixture.panel.Refresh();
            Func<long> readAllocated = null; bool useAllocationRecorder = false;
            LastReport.allocationRequested = Array.IndexOf(Environment.GetCommandLineArgs(), "-codexProgressRefreshAllocationBenchmark") >= 0;
            if (LastReport.allocationRequested) TryAllocationProbe(out readAllocated, out useAllocationRecorder);
            LastReport.originalFullRefresh = MeasurePath(fixture, "Refresh", fixture.panel.Refresh, readAllocated, useAllocationRecorder);
            LastReport.selectedRefresh = MeasurePath(fixture, LastReport.selectedPath, fixture.selected, readAllocated, useAllocationRecorder);
            if (LastReport.allocationAvailable && (LastReport.originalFullRefresh.allocatedBytes <= 0 || LastReport.selectedRefresh.allocatedBytes < 0))
            {
                LastReport.allocationAvailable = false;
                LastReport.allocationUnavailableReason = "Calibrated provider did not produce valid benchmark byte deltas; no allocation-improvement conclusion.";
                LastReport.originalFullRefresh.allocationAvailable = LastReport.selectedRefresh.allocationAvailable = false;
                LastReport.originalFullRefresh.allocatedBytes = LastReport.selectedRefresh.allocatedBytes = -1;
            }
            if (fixture.candidate)
            {
                Check(LastReport.selectedRefresh.fullRefreshCalls == 0, "200 periodic updates execute no hidden full refresh");
                Check(LastReport.selectedRefresh.progressRefreshCalls == Samples, "all 200 selected updates execute the new production path");
                if (LastReport.allocationAvailable && LastReport.originalFullRefresh.allocatedBytes > 0)
                {
                    LastReport.allocationImprovementVerified = LastReport.selectedRefresh.allocatedBytes < LastReport.originalFullRefresh.allocatedBytes * .8;
                    Check(LastReport.allocationImprovementVerified,
                        "opt-in calibrated allocation benchmark reduces periodic UI allocation by at least 20 percent");
                }
            }
        }
        // Unity's Mono runtime may expose GetAllocatedBytesForCurrentThread while
        // returning zero. GC.Alloc is a marker: its sample count can be useful even
        // when Value is zero duration, not bytes. Only a Bytes descriptor with a held
        // 1 MiB positive control, or a calibrated monotonic thread-byte API, is usable.
        // Unavailable optional measurement never fails normal functional/build checks.
        // Official synchronous marker-count example (does not claim byte measurement):
        // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread.html
        static void TryAllocationProbe(out Func<long> readAllocated, out bool useAllocationRecorder)
        {
            readAllocated = null; useAllocationRecorder = false;
            const int probeBytes = 1024 * 1024;
            try
            {
                MethodInfo legacyMethod = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
                var legacy = legacyMethod == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), legacyMethod);
                LastReport.legacyThreadCounterAvailable = legacy != null;
                long before = legacy == null ? 0 : legacy();
                using (var recorder = AllocationRecorder())
                {
                    byte[] positiveControl = new byte[probeBytes]; positiveControl[0] = 1;
                    recorder.Stop();
                    LastReport.legacyThreadProbeBytes = legacy == null ? -1 : legacy() - before;
                    LastReport.allocationProbeSamples = recorder.Count;
                    LastReport.allocationProbeUnit = recorder.Valid ? recorder.UnitType.ToString() : "unavailable";
                    LastReport.allocationProbeDataType = recorder.Valid ? recorder.DataType.ToString() : "unavailable";
                    LastReport.allocationProbeValue = RecordedValues(recorder);
                    bool integer = LastReport.allocationProbeDataType == "Int64" || LastReport.allocationProbeDataType == "UInt64" ||
                        LastReport.allocationProbeDataType == "Int32" || LastReport.allocationProbeDataType == "UInt32";
                    bool bytes = recorder.Valid && integer && LastReport.allocationProbeUnit == "Bytes";
                    LastReport.allocationProbeBytes = bytes ? LastReport.allocationProbeValue : -1;
                    useAllocationRecorder = bytes && recorder.Count > 0 && LastReport.allocationProbeValue >= probeBytes;
                    if (legacy != null && LastReport.legacyThreadProbeBytes >= probeBytes) readAllocated = legacy;
                    GC.KeepAlive(positiveControl);
                }
            }
            catch (Exception error) { LastReport.allocationUnavailableReason = "Optional allocation probe unavailable: " + error.GetType().Name; }
            LastReport.allocationAvailable = LastReport.allocationProbeValid = readAllocated != null || useAllocationRecorder;
            LastReport.allocationSource = readAllocated != null ? "calibrated GC.GetAllocatedBytesForCurrentThread" :
                useAllocationRecorder ? "calibrated Unity GC.Alloc Bytes descriptor/current thread" : "unavailable";
            if (!LastReport.allocationAvailable)
                LastReport.allocationUnavailableReason = "No calibrated current-thread byte provider: GC.Alloc unit=" +
                    LastReport.allocationProbeUnit + ", markerValue=" + LastReport.allocationProbeValue +
                    ", samples=" + LastReport.allocationProbeSamples + ", held-1MiB thread-byte delta=" + LastReport.legacyThreadProbeBytes +
                    ". Marker counts/duration are not allocation bytes; do not claim a memory improvement.";
            else LastReport.allocationUnavailableReason = "";
        }
        static ProfilerRecorder AllocationRecorder() => ProfilerRecorder.StartNew(ProfilerCategory.Internal,
            "GC.Alloc", AllocationCapacity, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        static long RecordedValues(ProfilerRecorder recorder)
        {
            long bytes = 0;
            for (int i = 0; i < recorder.Count; i++) bytes += recorder.GetSample(i).Value;
            return bytes;
        }
        static Measurement MeasurePath(Fixture fixture, string name, Action action, Func<long> readAllocated, bool useAllocationRecorder)
        {
            for (int i = 0; i < 20; i++) { fixture.state.Current.remainingSeconds = 900f - i; action(); }
            int full = Count(fixture.panel, "FullRefreshCountForValidation"), progress = Count(fixture.panel, "ProgressRefreshCountForValidation");
            long ticks, bytes = -1; int allocationSamples = -1;
            if (useAllocationRecorder && readAllocated == null)
            {
                using (var recorder = AllocationRecorder())
                {
                    ticks = TimeCalls(fixture, action); recorder.Stop();
                    if (recorder.Valid && !recorder.WrappedAround && recorder.Count < AllocationCapacity)
                    { bytes = RecordedValues(recorder); allocationSamples = recorder.Count; }
                    else
                    {
                        LastReport.allocationAvailable = false;
                        LastReport.allocationUnavailableReason = name + ": optional recorder unavailable or bounded sample capacity exceeded";
                    }
                }
            }
            else
            {
                long before = readAllocated == null ? -1 : readAllocated();
                ticks = TimeCalls(fixture, action);
                if (readAllocated != null) bytes = readAllocated() - before;
            }
            return new Measurement { sampled = true, allocationAvailable = bytes >= 0, path = name, calls = Samples,
                allocatedBytes = bytes, allocationSamples = allocationSamples, elapsedTicks = ticks,
                milliseconds = ticks * 1000.0 / Stopwatch.Frequency,
                fullRefreshCalls = full < 0 ? -1 : Count(fixture.panel, "FullRefreshCountForValidation") - full,
                progressRefreshCalls = progress < 0 ? -1 : Count(fixture.panel, "ProgressRefreshCountForValidation") - progress };
        }
        static long TimeCalls(Fixture fixture, Action action)
        {
            long before = Stopwatch.GetTimestamp();
            for (int i = 0; i < Samples; i++) { fixture.state.Current.remainingSeconds = 800f - i; action(); }
            return Stopwatch.GetTimestamp() - before;
        }
        static int Count(OceanCampaignPanel panel, string property)
        {
            PropertyInfo counter = typeof(OceanCampaignPanel).GetProperty(property, Fields);
            return counter == null ? -1 : (int)counter.GetValue(panel);
        }
    }
}
#endif

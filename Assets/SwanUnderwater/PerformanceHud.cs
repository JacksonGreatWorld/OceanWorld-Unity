using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine.XR;
#endif

namespace SwanUnderwater
{
    /// <summary>
    /// A small, non-interactive world-space panel parented to the viewing camera. Parenting
    /// affects this panel only: no camera, XR Origin, tracking pose or projection is modified.
    /// FPS is a rolling ~1s Unity player-loop rate. CPU/GPU and counters are latest reported
    /// frames sampled every 0.5s, not rolling averages or necessarily the same provider frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceHud : MonoBehaviour
    {
        private const float RefreshSeconds = 0.5f;
        private const string Missing = "\u2014";
        private static readonly Color TextColor = new Color(0.89f, 0.95f, 0.98f, 1f);
        private static readonly Color LabelColor = new Color(0.57f, 0.73f, 0.79f, 1f);

        public static PerformanceHud Instance { get; private set; }
        private const string VisibilityPreferenceKey = "Ocean.PerformanceHUD.Visible";
        // Keep the view clear by default; explicit menu choices survive app restarts.
        public static bool PreferenceVisible => PlayerPrefs.GetInt(VisibilityPreferenceKey, 0) != 0;
        public static void SetPreferenceVisible(bool visible)
        {
            PlayerPrefs.SetInt(VisibilityPreferenceKey, visible ? 1 : 0);
            PlayerPrefs.Save();
            if (Instance != null)
            {
                Instance.nextRefresh = 0;
                if (!visible && Instance.panel != null) Instance.panel.SetActive(false);
            }
            Debug.Log("[SwanHUD] preference_visible=" + visible);
        }
        private RuntimeDiagnostics diagnostics;
        private Camera attachedCamera;
        private GameObject panel;
        private Canvas canvas;
        private Material overlayMaterial;
        private Font font;
        private Text fps, cpu, gpu, draw, tri, setPass, refresh, note;
        private double nextRefresh;
#if UNITY_EDITOR
        private Camera editorPreviewCamera;
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            diagnostics = GetComponent<RuntimeDiagnostics>();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // Resources keeps the small overlay shader in Release builds. ZTest Always makes
            // the diagnostic panel readable when nearby scenery crosses its fixed depth.
            Shader shader = Resources.Load<Shader>("PerformanceHudUI");
            if (shader != null) overlayMaterial = new Material(shader) { name = "Performance HUD UI (runtime)" };
            else Debug.LogWarning("[SwanHUD] overlay shader missing; using the standard world-space UI material");
        }

        private void LateUpdate()
        {
            if (!PreferenceVisible || OceanEntry.BlocksGameplay) { if (panel != null) panel.SetActive(false); return; }
            if (Instance != this || Time.realtimeSinceStartupAsDouble < nextRefresh) return;
            nextRefresh = Time.realtimeSinceStartupAsDouble + RefreshSeconds;
            Camera target = Camera.main;
#if UNITY_EDITOR
            if (editorPreviewCamera != null) target = editorPreviewCamera;
#endif
            if (target == null || !target.isActiveAndEnabled)
            {
                if (panel != null) panel.SetActive(false);
                return;
            }
            AttachToCamera(target);
            RefreshLabels();
        }

        private void AttachToCamera(Camera target)
        {
            if (!PreferenceVisible) { if (panel != null) panel.SetActive(false); return; }
            if (panel == null) BuildPanel();
            if (attachedCamera != target || panel.transform.parent != target.transform)
            {
                attachedCamera = target;
                panel.transform.SetParent(target.transform, false);
                var rect = (RectTransform)panel.transform;
                rect.localPosition = new Vector3(0.72f, -0.52f, 1.2f);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one * 0.00085f;
                canvas.worldCamera = target;
            }
            panel.SetActive(true);
        }

        private void BuildPanel()
        {
            panel = new GameObject("Performance HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(Image));
            panel.hideFlags = HideFlags.DontSave;
            var rect = (RectTransform)panel.transform;
            rect.sizeDelta = new Vector2(480, 238);
            canvas = panel.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32760;
            panel.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 1f;
            Image background = panel.GetComponent<Image>();
            background.color = new Color(0.018f, 0.038f, 0.05f, 0.84f);
            background.raycastTarget = false;
            if (overlayMaterial != null) background.material = overlayMaterial;

            MakeText("Title", "PERFORMANCE", 18, 12, 290, 26, 20, LabelColor, FontStyle.Bold);
            refresh = MakeText("Refresh", Missing + " Hz", 330, 12, 132, 26, 20, LabelColor);
            refresh.alignment = TextAnchor.MiddleRight;
            fps = MakeCell("FPS", 18, 49);
            cpu = MakeCell("CPU", 250, 49);
            gpu = MakeCell("GPU", 18, 88);
            draw = MakeCell("Draw", 250, 88);
            tri = MakeCell("Tri", 18, 127);
            setPass = MakeCell("Pass", 250, 127);
            note = MakeText("Scope", "FPS loop ~1s | other values: latest\nCPU includes waits | GPU: " + Missing,
                18, 177, 444, 48, 17, LabelColor);
        }

        private Text MakeCell(string label, float x, float y)
        {
            MakeText(label + " Label", label, x, y, 64, 34, 23, LabelColor);
            Text value = MakeText(label + " Value", Missing, x + 64, y, 148, 34, 26, TextColor, FontStyle.Bold);
            value.alignment = TextAnchor.MiddleRight;
            return value;
        }

        private Text MakeText(string name, string initial, float x, float y, float width, float height,
            int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Text));
            child.transform.SetParent(panel.transform, false);
            var rect = (RectTransform)child.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            Text text = child.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.text = initial;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = false;
            text.raycastTarget = false;
            if (overlayMaterial != null) text.material = overlayMaterial;
            return text;
        }

        private void RefreshLabels()
        {
            if (diagnostics == null) diagnostics = GetComponent<RuntimeDiagnostics>();
            RuntimeDiagnostics.LiveSample live = diagnostics != null ? diagnostics.Latest : default;
            // A stale snapshot after pause is not live evidence. Unavailable/zero timings and
            // absent/non-positive render counters remain a dash, never a fabricated zero.
            bool fresh = live.sampledAt > 0 && Time.realtimeSinceStartupAsDouble - live.sampledAt < 1.5;
            fps.text = Number(fresh && live.fpsAvailable, live.loopFps);
            cpu.text = Number(fresh && live.cpuAvailable, live.cpuFrameMs, " ms");
            gpu.text = Number(fresh && live.gpuAvailable, live.gpuFrameMs, " ms");
            draw.text = Count(fresh && live.drawCallsAvailable, live.drawCalls);
            tri.text = Count(fresh && live.trianglesAvailable, live.triangles);
            setPass.text = Count(fresh && live.setPassCallsAvailable, live.setPassCalls);
            refresh.text = Number(fresh && live.displayRefreshAvailable, live.displayRefreshHz, " Hz", "0");
            note.text = "FPS loop ~1s | other values: latest\nCPU includes waits | GPU: " +
                (fresh && live.gpuAvailable ? SourceLabel(live.gpuSource) : Missing);
        }

        private static string Number(bool available, double value, string unit = "", string format = "0.0") =>
            available && value > 0 && !double.IsNaN(value) && !double.IsInfinity(value)
                ? value.ToString(format, CultureInfo.InvariantCulture) + unit : Missing;

        private static string Count(bool available, long value)
        {
            if (!available || value <= 0) return Missing;
            if (value >= 1000000) return (value / 1000000.0).ToString("0.00", CultureInfo.InvariantCulture) + "M";
            if (value >= 10000) return (value / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "K";
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string SourceLabel(RuntimeDiagnostics.GpuSource source)
        {
            switch (source)
            {
                case RuntimeDiagnostics.GpuSource.FrameTimingManager: return "FrameTiming";
                case RuntimeDiagnostics.GpuSource.XrDisplaySubsystem: return "XR display";
                case RuntimeDiagnostics.GpuSource.XrStats: return "XRStats";
                default: return Missing;
            }
        }

#if UNITY_EDITOR
        public bool PanelVisibleForValidation => panel != null && panel.activeInHierarchy;
        public static bool HasVisibilityPreferenceForValidation => PlayerPrefs.HasKey(VisibilityPreferenceKey);
        public static void RestoreVisibilityPreferenceForValidation(bool existed, bool value)
        {
            if (existed) SetPreferenceVisible(value);
            else { PlayerPrefs.DeleteKey(VisibilityPreferenceKey); PlayerPrefs.Save(); if (Instance != null) Instance.nextRefresh = 0; }
        }
        /// <summary>Use only for a fixed-view Editor capture, then pass null in finally before
        /// destroying the temporary camera. This reparents only the HUD, never either camera.</summary>
        public void SetEditorPreviewCamera(Camera camera)
        {
            if (camera != null)
            {
                var displays = new List<XRDisplaySubsystem>();
                SubsystemManager.GetSubsystems(displays);
                if (XRSettings.isDeviceActive || displays.Exists(display => display.running))
                    throw new InvalidOperationException("A HUD preview cannot replace a running XR view.");
            }
            editorPreviewCamera = camera;
            Camera target = camera != null ? camera : Camera.main;
            if (target != null && PreferenceVisible)
            {
                AttachToCamera(target);
                RefreshLabels();
                Canvas.ForceUpdateCanvases();
            }
        }
#endif

        private void OnDisable()
        {
            if (panel != null) panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (panel != null) Destroy(panel);
            if (overlayMaterial != null) Destroy(overlayMaterial);
            // The shared built-in font belongs to Unity; do not destroy it.
        }
    }
}

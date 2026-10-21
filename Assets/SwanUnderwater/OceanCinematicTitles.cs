using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater
{
    /// <summary>Camera-relative typography only. The entry sequence owns its clock, camera and interaction.</summary>
    public sealed class OceanCinematicTitles : MonoBehaviour
    {
        public const string SwimHintText = "双手划动可以前进";
        const float PixelsToMetres = .002f;
        const float MaximumTitleWidth = 810f; // 1.62 m = 37.30 degrees at 2.4 m.
        Canvas canvas;
        CanvasGroup opacity, hintOpacity;
        RectTransform content;
        Text title, hint, error;
        OceanTitleInk titleInk, hintInk;
        GameObject normalContent;
        bool shown, chapter, failed, disposed;
        bool titleFontDirty, bodyFontDirty;

        public Canvas Canvas => canvas;
        public RectTransform TitleRect => title != null ? title.rectTransform : null;
        public RectTransform HintRect => hint != null ? hint.rectTransform : null;
        public bool IsVisible => shown && opacity != null && opacity.alpha > .001f;
        public float TitleOpacity => shown && !failed ? opacity.alpha : 0f;
        public float HintOpacity => shown && chapter && !failed ? opacity.alpha * hintOpacity.alpha : 0f;

        public static OceanCinematicTitles Create(Transform camera, Camera view, Font titleFont,
            Font bodyFont, Material uiMaterial, int layer)
        {
            if (camera == null || view == null || bodyFont == null || uiMaterial == null)
                throw new ArgumentException("Ocean titles require a camera, readable body font and UI material.");
            var root = new GameObject("Ocean cinematic titles", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            root.layer = layer;
            var rect = (RectTransform)root.transform;
            rect.SetParent(camera, false);
            rect.localPosition = new Vector3(0f, .10f, 2.4f);
            rect.localScale = Vector3.one * PixelsToMetres;
            rect.sizeDelta = new Vector2(1000f, 260f);
            var result = root.AddComponent<OceanCinematicTitles>();
            result.Build(view, titleFont != null ? titleFont : bodyFont, bodyFont, uiMaterial, layer);
            return result;
        }

        void Build(Camera view, Font titleFont, Font bodyFont, Material uiMaterial, int layer)
        {
            canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = view;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32767;
            opacity = GetComponent<CanvasGroup>();
            opacity.alpha = 0f;
            opacity.blocksRaycasts = opacity.interactable = false;
            normalContent = new GameObject("Animated title content", typeof(RectTransform));
            normalContent.layer = layer;
            content = (RectTransform)normalContent.transform;
            content.SetParent(transform, false);
            content.sizeDelta = new Vector2(1000f, 220f);

            title = MakeText("Calligraphic ocean title", content, titleFont, 64, uiMaterial, layer);
            title.rectTransform.sizeDelta = new Vector2(1000f, 130f);
            title.rectTransform.anchoredPosition = new Vector2(0f, 24f);
            titleInk = title.gameObject.AddComponent<OceanTitleInk>();
            titleInk.GradientEnabled = true;

            var flourishObject = new GameObject("Slim ocean flourish", typeof(RectTransform), typeof(OceanTitleFlourish));
            flourishObject.layer = layer;
            var flourishRect = (RectTransform)flourishObject.transform;
            flourishRect.SetParent(content, false);
            flourishRect.sizeDelta = new Vector2(390f, 12f);
            flourishRect.anchoredPosition = new Vector2(0f, -30f);
            var flourish = flourishObject.GetComponent<OceanTitleFlourish>();
            flourish.material = uiMaterial;
            flourish.raycastTarget = false;

            hint = MakeText("Swimming hint", content, bodyFont, 34, uiMaterial, layer);
            hint.rectTransform.sizeDelta = new Vector2(900f, 70f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, -79f);
            hint.text = SwimHintText;
            hint.color = new Color(.84f, .94f, .97f, 1f);
            hintOpacity = hint.gameObject.AddComponent<CanvasGroup>();
            hintOpacity.blocksRaycasts = hintOpacity.interactable = false;
            hintInk = hint.gameObject.AddComponent<OceanTitleInk>();
            hintInk.GradientEnabled = false;

            error = MakeText("Readable entry error", transform, bodyFont, 34, uiMaterial, layer);
            error.rectTransform.sizeDelta = new Vector2(860f, 220f);
            error.horizontalOverflow = HorizontalWrapMode.Wrap;
            error.color = new Color(.98f, .94f, .85f, 1f);
            error.gameObject.SetActive(false);
            // Warm both exact title strings before measuring; dynamic fonts may later repack.
            titleFont.RequestCharactersInTexture(OceanEntry.WelcomeText + OceanEntry.ChapterText, 64, FontStyle.Normal);
            bodyFont.RequestCharactersInTexture(SwimHintText, 34, FontStyle.Normal);
            Font.textureRebuilt += FontRebuilt;
        }

        static Text MakeText(string name, Transform parent, Font font, int size, Material material, int layer)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
            child.layer = layer;
            child.transform.SetParent(parent, false);
            Text label = child.GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = FontStyle.Normal;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.color = Color.white;
            label.material = material; // Borrowed: never mutate or destroy the caller's material.
            Shadow shadow = child.GetComponent<Shadow>();
            shadow.effectColor = new Color(.003f, .017f, .036f, .84f);
            shadow.effectDistance = new Vector2(1.3f, -1.5f);
            return label;
        }

        public void Show(bool chapter)
        {
            if (disposed) return;
            this.chapter = chapter;
            shown = true;
            failed = false;
            normalContent.SetActive(true);
            error.gameObject.SetActive(false);
            title.text = chapter ? OceanEntry.ChapterText : OceanEntry.WelcomeText;
            // Fit once per title; no best-fit/font-size changes or preferredWidth queries during Animate.
            float preferred = Mathf.Max(1f, title.preferredWidth);
            float fit = Mathf.Min(1f, MaximumTitleWidth / preferred);
            title.rectTransform.localScale = Vector3.one * fit;
            hint.gameObject.SetActive(chapter);
            hintOpacity.alpha = 0f;
            opacity.alpha = 0f;
            titleInk.SetGlint(-1f, 0f);
            Animate(0f, chapter ? 5.2f : 5.6f);
            Debug.Log($"[OceanTitles] Show chapter={chapter} title={title.text} font={title.font.name} " +
                $"family={string.Join(",", title.font.fontNames)} hint={(chapter ? SwimHintText : "none")} " +
                $"fitWidth={preferred * fit:F1}px/{preferred * fit * PixelsToMetres:F3}m fontSize=64 hintSize=34");
        }

        public void Animate(float time, float duration)
        {
            if (disposed || !shown || failed) return;
            if (!Finite(time) || !Finite(duration) || duration <= 0f) { Hide(); return; }
            float t = Mathf.Max(0f, time);
            // Same fade contract and total beat duration as the existing sequence.
            opacity.alpha = OceanEntrySequence.SubtitleOpacity(t, duration);
            float settle = Mathf.Clamp01(t / 1.10f);
            float ease = 1f - Mathf.Pow(1f - settle, 3f);
            float scale = 1f - .026f * (1f - ease) + .008f * Mathf.Sin(settle * Mathf.PI) * (1f - settle);
            content.localScale = Vector3.one * scale;
            content.anchoredPosition = new Vector2(0f, -14f * (1f - Smooth(t / .95f)) + Mathf.Sin(t * .95f) * 1.5f);
            // One restrained gold sweep; a completed sweep never repeats during the hold.
            float scan = (t - .90f) / 1.90f;
            titleInk.SetGlint(scan, scan > 0f && scan < 1f ? .48f : 0f);
            hintOpacity.alpha = chapter ? Smooth((t - 1.05f) / .65f) : 0f;
        }

        public void Hide()
        {
            if (disposed) return;
            shown = false;
            opacity.alpha = 0f;
            hintOpacity.alpha = 0f;
        }

        public void ShowError(string text)
        {
            if (disposed) return;
            failed = shown = true;
            chapter = false;
            normalContent.SetActive(false);
            error.text = text ?? string.Empty;
            error.gameObject.SetActive(true);
            opacity.alpha = 1f;
        }

        /// <summary>Actual generated glyph+shadow geometry, available after the first Canvas rebuild.
        /// It follows title fitting and content animation; no camera pose assumptions.</summary>
        public bool TryGetTitleWorldBounds(out Bounds bounds) => GetWorldBounds(titleInk, title.transform, TitleOpacity, out bounds);
        public bool TryGetHintWorldBounds(out Bounds bounds) => GetWorldBounds(hintInk, hint.transform, HintOpacity, out bounds);

        static bool GetWorldBounds(OceanTitleInk ink, Transform label, float alpha, out Bounds bounds)
        {
            bounds = default;
            if (alpha <= .001f || ink == null || !ink.HasGeometry) return false;
            Bounds local = ink.InkBounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = local.center + Vector3.Scale(local.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                point = label.TransformPoint(point);
                if (corner == 0) bounds = new Bounds(point, Vector3.zero);
                else bounds.Encapsulate(point);
            }
            return true;
        }

        void FontRebuilt(Font rebuilt)
        {
            if (disposed) return;
            // Atlas notifications can arrive while Canvas is rebuilding geometry.
            // Defer our dirty registration so UGUI never rejects it as reentrant.
            if (title != null && title.font == rebuilt)
            {
                titleInk.InvalidateGeometry();
                titleFontDirty = true;
            }
            if (hint != null && hint.font == rebuilt)
            {
                hintInk.InvalidateGeometry();
                bodyFontDirty = true;
            }
        }

        void LateUpdate()
        {
            if (disposed || CanvasUpdateRegistry.IsRebuildingGraphics()) return;
            if (titleFontDirty)
            {
                titleFontDirty = false;
                title.SetVerticesDirty();
            }
            if (bodyFontDirty)
            {
                bodyFontDirty = false;
                hint.SetVerticesDirty();
                error.SetVerticesDirty();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            Hide();
            disposed = true;
            Font.textureRebuilt -= FontRebuilt;
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        void OnDestroy() { Font.textureRebuilt -= FontRebuilt; }
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Cache a real legacy Text mesh, changing only its vertex colours during the sweep.
    /// Text geometry/UVs rebuild normally when text, rect or the dynamic font atlas changes.</summary>
    [AddComponentMenu("")]
    public sealed class OceanTitleInk : BaseMeshEffect
    {
        public bool GradientEnabled { get; set; }
        public bool HasGeometry { get; private set; }
        public Bounds InkBounds { get; private set; }
        readonly List<Vector3> positions = new List<Vector3>(192);
        readonly List<Color32> originals = new List<Color32>(192);
        readonly List<Color32> colours = new List<Color32>(192);
        Mesh animatedMesh;
        float glintProgress = -1f, glintStrength;
        static readonly Color Pearl = new Color(.98f, 1f, .95f, 1f);
        static readonly Color SeaGlass = new Color(.43f, .83f, .92f, 1f);
        static readonly Color Gold = new Color(1f, .84f, .51f, 1f);

        public override void ModifyMesh(VertexHelper helper)
        {
            if (!IsActive()) return;
            if (animatedMesh == null)
            {
                animatedMesh = new Mesh { name = "Ocean title cached glyphs", hideFlags = HideFlags.DontSave };
                animatedMesh.MarkDynamic();
            }
            positions.Clear(); originals.Clear(); colours.Clear();
            UIVertex vertex = default;
            Bounds bounds = default;
            for (int i = 0; i < helper.currentVertCount; i++)
            {
                helper.PopulateUIVertex(ref vertex, i);
                positions.Add(vertex.position);
                originals.Add(vertex.color);
                colours.Add(vertex.color);
                if (i == 0) bounds = new Bounds(vertex.position, Vector3.zero);
                else bounds.Encapsulate(vertex.position);
            }
            InkBounds = bounds;
            HasGeometry = positions.Count > 0;
            Recolour();
            for (int i = 0; i < colours.Count; i++)
            {
                helper.PopulateUIVertex(ref vertex, i);
                vertex.color = colours[i];
                helper.SetUIVertex(vertex, i);
            }
            helper.FillMesh(animatedMesh);
        }

        public void InvalidateGeometry() { HasGeometry = false; }

        public void SetGlint(float progress, float strength)
        {
            if (strength <= 0f && glintStrength <= 0f) { glintProgress = progress; return; }
            if (Mathf.Abs(progress - glintProgress) < .00001f && Mathf.Abs(strength - glintStrength) < .00001f) return;
            glintProgress = progress;
            glintStrength = strength;
            if (!HasGeometry || !GradientEnabled || animatedMesh == null) return;
            Recolour();
            animatedMesh.SetColors(colours);
            graphic.canvasRenderer.SetMesh(animatedMesh);
        }

        void Recolour()
        {
            if (!GradientEnabled) return;
            float height = Mathf.Max(1f, InkBounds.size.y);
            const float band = 53f;
            float centre = Mathf.LerpUnclamped(InkBounds.min.x - band, InkBounds.max.x + band, glintProgress);
            for (int i = 0; i < positions.Count; i++)
            {
                Color32 original = originals[i];
                // The Shadow effect runs first. Preserve its dark colour and original alpha.
                if (original.r < 70 && original.g < 70 && original.b < 70) { colours[i] = original; continue; }
                Vector3 p = positions[i];
                Color colour = Color.Lerp(SeaGlass, Pearl, Mathf.Clamp01((p.y - InkBounds.min.y) / height));
                float sweep = Mathf.Clamp01(1f - Mathf.Abs(p.x - .20f * p.y - centre) / band);
                sweep = sweep * sweep * (3f - 2f * sweep) * glintStrength;
                colour = Color.Lerp(colour, Gold, sweep);
                colour.a = original.a / 255f;
                colours[i] = colour;
            }
        }

        protected override void OnDestroy()
        {
            if (animatedMesh != null)
            {
                if (Application.isPlaying) Destroy(animatedMesh);
                else DestroyImmediate(animatedMesh);
            }
            base.OnDestroy();
        }
    }

    /// <summary>A single thin wave with a small warm centre, generated once in UI geometry.</summary>
    [AddComponentMenu("")]
    public sealed class OceanTitleFlourish : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            const int segments = 32;
            const float halfWidth = 185f, thickness = 1.15f;
            for (int segment = 0; segment < segments; segment++)
            {
                float a = segment / (float)segments, b = (segment + 1f) / segments;
                AddPoint(helper, a, -thickness * .5f, halfWidth);
                AddPoint(helper, a, thickness * .5f, halfWidth);
                AddPoint(helper, b, thickness * .5f, halfWidth);
                AddPoint(helper, b, -thickness * .5f, halfWidth);
                int index = segment * 4;
                helper.AddTriangle(index, index + 1, index + 2);
                helper.AddTriangle(index, index + 2, index + 3);
            }
            int diamond = helper.currentVertCount;
            Color tint = new Color(.94f, .83f, .57f, .66f);
            helper.AddVert(new Vector3(-4f, 0f, 0f), tint, Vector2.zero);
            helper.AddVert(new Vector3(0f, 2.1f, 0f), tint, Vector2.zero);
            helper.AddVert(new Vector3(4f, 0f, 0f), tint, Vector2.zero);
            helper.AddVert(new Vector3(0f, -2.1f, 0f), tint, Vector2.zero);
            helper.AddTriangle(diamond, diamond + 1, diamond + 2);
            helper.AddTriangle(diamond, diamond + 2, diamond + 3);
        }

        static void AddPoint(VertexHelper helper, float t, float offsetY, float halfWidth)
        {
            float envelope = Mathf.Sin(t * Mathf.PI);
            Color tint = Color.Lerp(new Color(.35f, .75f, .84f, 0f), new Color(.76f, .92f, .93f, .59f), envelope);
            helper.AddVert(new Vector3(Mathf.Lerp(-halfWidth, halfWidth, t), Mathf.Sin(t * Mathf.PI * 2f) * 2.2f + offsetY, 0f),
                tint, Vector2.zero);
        }
    }
}

using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>
    /// One reused, world-anchored acknowledgement of an accepted UI press. This is
    /// not a level-completion effect and never participates in hit testing.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class OceanMenuClickFeedback : UnityEngine.UI.MaskableGraphic
    {
        public const float Duration = .24f;
        const int Segments = 28;
        double started;
        float phase, radius = 22f;
        Vector3 worldPoint;
        Quaternion worldRotation;
        bool playing;
        public bool IsPlaying => playing && gameObject.activeSelf;

        public void Play(Vector3 point, Quaternion rotation, Vector3 scale, float extent, Color tint)
        {
            if (!Finite(point) || !Finite(rotation) || !Finite(scale) || scale.x <= 0f || scale.y <= 0f ||
                scale.z <= 0f || !Finite(extent) || extent < 1f) return;
            worldPoint = point; worldRotation = rotation;
            transform.SetPositionAndRotation(worldPoint, worldRotation);
            transform.localScale = scale;
            radius = Mathf.Clamp(extent * .43f, 10f, 25f);
            rectTransform.sizeDelta = Vector2.one * radius * 2.2f;
            color = tint; phase = 0; started = Time.unscaledTimeAsDouble; playing = true;
            raycastTarget = false; gameObject.SetActive(true); SetVerticesDirty();
        }

        public void Cancel()
        {
            playing = false; phase = 1f;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        protected override void Awake() { base.Awake(); raycastTarget = false; maskable = false; }
        protected override void OnDisable() { playing = false; base.OnDisable(); }
        void LateUpdate()
        {
            if (!playing) return;
            // Clock age expires immediately after an application stall; it cannot
            // resume an old press animation several seconds later.
            phase = Mathf.Clamp01((float)((Time.unscaledTimeAsDouble - started) / Duration));
            if (phase >= 1f)
            {
#if UNITY_EDITOR
                NaturalExpiryCountForValidation++;
#endif
                Cancel(); return;
            }
            transform.SetPositionAndRotation(worldPoint, worldRotation);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper helper)
            => Populate(helper, radius, phase, color);

        internal static void Populate(UnityEngine.UI.VertexHelper helper, float radius, float phase, Color tint)
        {
            helper.Clear();
            if (!Finite(radius) || !Finite(phase) || radius <= 0f || phase < 0f || phase >= 1f) return;
            float easing = 1f - Mathf.Pow(1f - phase, 3f);
            float outer = Mathf.Lerp(radius * .27f, radius, easing);
            float inner = Mathf.Max(0f, outer - Mathf.Lerp(2.8f, .8f, phase));
            tint.a *= (1f - phase) * (1f - phase);
            // A neutral champagne ring means only that the menu received a press.
            // It has no checkmark, green success state, particles or extra material.
            for (int segment = 0; segment < Segments; segment++)
            {
                float a = segment * Mathf.PI * 2f / Segments, b = (segment + 1) * Mathf.PI * 2f / Segments;
                Vector2 x = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 y = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                int first = helper.currentVertCount;
                helper.AddVert(x * outer, tint, Vector2.zero);
                helper.AddVert(y * outer, tint, Vector2.zero);
                helper.AddVert(y * inner, tint, Vector2.zero);
                helper.AddVert(x * inner, tint, Vector2.zero);
                helper.AddTriangle(first, first + 1, first + 2);
                helper.AddTriangle(first, first + 2, first + 3);
            }
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
#if UNITY_EDITOR
        public int NaturalExpiryCountForValidation { get; private set; }
        public void SetPhaseForValidation(float value)
        {
            phase = Mathf.Clamp01(value); started = Time.unscaledTimeAsDouble - phase * Duration;
            if (phase >= 1f) Cancel(); else SetVerticesDirty();
        }
#endif
    }
}

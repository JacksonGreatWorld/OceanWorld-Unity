using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater
{
    /// <summary>Texture-free champagne border, corner engraving and one travelling glint in a single UI mesh.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class OceanMenuOrnament : MaskableGraphic
    {
        [SerializeField] Color gold = new Color(.94f, .78f, .48f, .85f);
        [SerializeField, Min(.25f)] float thickness = 1.5f;
        [SerializeField, Min(0f)] float radius = 14f;
        [SerializeField, Range(0f, 1f)] float progress = 1f;
        [SerializeField, Range(0f, 1f)] float hover;
        [SerializeField] bool motion = true;
        [SerializeField, Range(0f, 1f)] float cyclesPerSecond = .12f;
        float phase, visibleHover, nextRefresh;
        bool pending = true;

        /// <summary>Canvas-local units; the existing caller-owned UI material is used unchanged.</summary>
        public void SetAppearance(Color champagneGold, float thickness = 1.5f, float radius = 14f)
        {
            if (!Finite(champagneGold.r) || !Finite(champagneGold.g) || !Finite(champagneGold.b) || !Finite(champagneGold.a) ||
                !Finite(thickness) || !Finite(radius)) return;
            gold = new Color(Mathf.Clamp01(champagneGold.r), Mathf.Clamp01(champagneGold.g),
                Mathf.Clamp01(champagneGold.b), Mathf.Clamp01(champagneGold.a));
            this.thickness = Mathf.Max(.25f, thickness); this.radius = Mathf.Max(0f, radius);
            pending = true;
        }

        /// <summary>Progress accents a portion of the rim; the full boundary always remains visible.</summary>
        public void SetState(float progress, float hover)
        {
            if (!Finite(progress) || !Finite(hover)) return;
            progress = Mathf.Clamp01(progress); hover = Mathf.Clamp01(hover);
            if (Mathf.Abs(this.progress - progress) < .001f && Mathf.Abs(this.hover - hover) < .001f) return;
            this.progress = progress; this.hover = hover; pending = true;
        }

        /// <summary>Speed is laps per unscaled second. Disabling motion freezes the current highlight.</summary>
        public void SetMotion(bool enabled, float speed = .12f)
        {
            motion = enabled;
            if (Finite(speed)) cyclesPerSecond = Mathf.Clamp(speed, 0f, 1f);
            pending = true;
        }

        protected override void Awake() { base.Awake(); raycastTarget = false; }
        protected override void OnEnable()
        {
            base.OnEnable(); raycastTarget = false; pending = true; nextRefresh = 0f;
        }
        protected override void OnDisable() { pending = false; base.OnDisable(); }

        void LateUpdate()
        {
            if (!IsActive() || CanvasUpdateRegistry.IsRebuildingGraphics()) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .10f);
            if (motion && cyclesPerSecond > 0f) phase = Mathf.Repeat(phase + dt * cyclesPerSecond, 1f);
            float before = visibleHover;
            visibleHover = Mathf.MoveTowards(visibleHover, hover, dt * 5f);
            bool animating = (motion && cyclesPerSecond > 0f) || Mathf.Abs(before - visibleHover) > .0001f;
            if ((!pending && !animating) || Time.unscaledTime < nextRefresh) return;
            if (canvasRenderer.cull && !pending) return;
            // Small decorative motion does not need to rebuild UI geometry at headset frame rate.
            nextRefresh = Time.unscaledTime + 1f / 24f; pending = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            Populate(helper, GetPixelAdjustedRect(), gold * color, thickness, radius, progress, visibleHover, phase);
        }

        // Pure mesh construction is also used by offline numeric validation; no runtime meshes/materials are allocated here.
        internal static void Populate(VertexHelper helper, Rect rect, Color tint, float lineWidth,
            float cornerRadius, float completion, float hoverAmount, float scan)
        {
            helper.Clear();
            float shortSide = Mathf.Min(rect.width, rect.height);
            if (!Finite(shortSide) || shortSide < 2f || !Finite(rect.xMin) || !Finite(rect.yMin) ||
                !Finite(lineWidth) || !Finite(cornerRadius) || !Finite(completion) || !Finite(hoverAmount) || !Finite(scan)) return;
            lineWidth = Mathf.Clamp(lineWidth, .25f, shortSide * .24f);
            cornerRadius = Mathf.Clamp(cornerRadius, 0f, shortSide * .5f);
            completion = Mathf.Clamp01(completion); hoverAmount = Mathf.Clamp01(hoverAmount); scan = Mathf.Repeat(scan, 1f);
            Rect inside = Inset(rect, lineWidth);
            float innerRadius = Mathf.Max(0f, cornerRadius - lineWidth);
            float perimeter = Perimeter(rect, cornerRadius), travelled = 0f;
            for (int piece = 0; piece < 8; piece++)
            {
                float length = PieceLength(rect, cornerRadius, piece);
                int segments = (piece & 1) == 1 ? 8 : Mathf.Clamp(Mathf.CeilToInt(length / 24f), 1, 40);
                if (length < .0001f) continue;
                for (int step = 0; step < segments; step++)
                {
                    float u = step / (float)segments, v = (step + 1f) / segments;
                    Color a = RimTint(tint, (travelled + length * u) / perimeter, completion, hoverAmount, scan);
                    Color b = RimTint(tint, (travelled + length * v) / perimeter, completion, hoverAmount, scan);
                    Quad(helper, Point(rect, cornerRadius, piece, u), Point(rect, cornerRadius, piece, v),
                        Point(inside, innerRadius, piece, v), Point(inside, innerRadius, piece, u), a, b);
                }
                travelled += length;
            }
            if (shortSide < 28f) return;
            Color engraving = tint; engraving.a *= .42f + .22f * hoverAmount;
            float inset = Mathf.Max(lineWidth + 2f, cornerRadius * .35f + lineWidth);
            float leg = Mathf.Clamp(shortSide * .18f, 6f, 21f);
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 origin = new Vector2((corner & 1) == 0 ? rect.xMin : rect.xMax, corner < 2 ? rect.yMin : rect.yMax);
                Vector2 sign = new Vector2((corner & 1) == 0 ? 1f : -1f, corner < 2 ? 1f : -1f);
                Vector2 a = origin + Vector2.Scale(sign, new Vector2(inset, inset + leg));
                Vector2 b = origin + Vector2.Scale(sign, new Vector2(inset, inset + 3f));
                Vector2 c = origin + Vector2.Scale(sign, new Vector2(inset + 3f, inset));
                Vector2 d = origin + Vector2.Scale(sign, new Vector2(inset + leg, inset));
                Line(helper, a, b, lineWidth * .6f, engraving);
                Line(helper, b, c, lineWidth * .6f, engraving);
                Line(helper, c, d, lineWidth * .6f, engraving);
            }
            float starRadius = 2.6f + hoverAmount * 1.0f;
            float starInset = starRadius + lineWidth;
            Rect starPath = Inset(rect, starInset);
            Vector2 star = Along(starPath, Mathf.Max(0f, cornerRadius - starInset), scan);
            Star(helper, star, starRadius, tint, hoverAmount);
        }

        static Color RimTint(Color tint, float along, float completion, float hoverAmount, float scan)
        {
            float distance = Mathf.Abs(along - scan); distance = Mathf.Min(distance, 1f - distance);
            float shine = Mathf.Pow(Mathf.Clamp01(1f - distance / .075f), 3f);
            float active = completion >= .999f ? 1f : completion <= .001f ? 0f :
                1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(completion - .012f, completion + .012f, along));
            Color c = Color.Lerp(tint, new Color(1f, .95f, .80f, tint.a), shine * (.6f + .2f * hoverAmount));
            c.a *= .34f + .26f * active + .12f * hoverAmount + .24f * shine;
            return c;
        }
        static float Perimeter(Rect r, float radius) => 2f * (r.width + r.height - 4f * radius) + 2f * Mathf.PI * radius;
        static float PieceLength(Rect r, float radius, int piece)
        {
            if ((piece & 1) == 1) return radius * Mathf.PI * .5f;
            return (piece == 0 || piece == 4 ? r.width : r.height) - radius * 2f;
        }
        static Rect Inset(Rect r, float amount) => new Rect(r.xMin + amount, r.yMin + amount, r.width - amount * 2f, r.height - amount * 2f);
        static Vector2 Point(Rect r, float radius, int piece, float u)
        {
            switch (piece)
            {
                case 0: return new Vector2(Mathf.Lerp(r.xMin + radius, r.xMax - radius, u), r.yMin);
                case 2: return new Vector2(r.xMax, Mathf.Lerp(r.yMin + radius, r.yMax - radius, u));
                case 4: return new Vector2(Mathf.Lerp(r.xMax - radius, r.xMin + radius, u), r.yMax);
                case 6: return new Vector2(r.xMin, Mathf.Lerp(r.yMax - radius, r.yMin + radius, u));
            }
            int corner = (piece - 1) / 2;
            Vector2 centre = new Vector2(corner < 2 ? r.xMax - radius : r.xMin + radius,
                corner == 0 || corner == 3 ? r.yMin + radius : r.yMax - radius);
            float angle = (-90f + corner * 90f + u * 90f) * Mathf.Deg2Rad;
            return centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        static Vector2 Along(Rect rect, float radius, float t)
        {
            float distance = Perimeter(rect, radius) * t;
            for (int piece = 0; piece < 8; piece++)
            {
                float length = PieceLength(rect, radius, piece);
                if (length > .0001f && distance <= length) return Point(rect, radius, piece, distance / length);
                distance -= length;
            }
            return Point(rect, radius, 0, 0);
        }
        static void Quad(VertexHelper helper, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color start, Color end)
        {
            int first = helper.currentVertCount;
            helper.AddVert(a, start, Vector2.zero); helper.AddVert(b, end, Vector2.zero);
            helper.AddVert(c, end, Vector2.zero); helper.AddVert(d, start, Vector2.zero);
            if (Mathf.Abs(Cross(b - a, c - a)) > .000001f) helper.AddTriangle(first, first + 1, first + 2);
            if (Mathf.Abs(Cross(c - a, d - a)) > .000001f) helper.AddTriangle(first, first + 2, first + 3);
        }
        static void Line(VertexHelper helper, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 delta = b - a;
            if (delta.sqrMagnitude < .0001f) return;
            Vector2 offset = new Vector2(-delta.y, delta.x).normalized * (width * .5f);
            Quad(helper, a - offset, b - offset, b + offset, a + offset, tint, tint);
        }
        static void Star(VertexHelper helper, Vector2 centre, float radius, Color tint, float hoverAmount)
        {
            int first = helper.currentVertCount;
            Color bright = Color.Lerp(tint, new Color(1f, .96f, .82f, tint.a), .7f);
            bright.a *= .72f + hoverAmount * .2f;
            helper.AddVert(centre, bright, Vector2.zero);
            Color tip = tint; tip.a *= .08f;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * .25f, r = (i & 1) == 0 ? radius : radius * .22f;
                helper.AddVert(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r, tip, Vector2.zero);
            }
            for (int i = 0; i < 8; i++) helper.AddTriangle(first, first + i + 1, first + (i + 1) % 8 + 1);
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>Owner-ticked, world-space cue. Never a collider, saved goal or head-attached overlay.</summary>
    public sealed class OceanActivityGateEffect : IDisposable
    {
        public const float Radius = .70f;
        public const float CueLifetime = 8f; // Idle time; a slow swimmer must not lose the target while approaching.
        const float ApproachRange = 6f, RearmRange = 8f, DissolveSeconds = .4f;
        static readonly int ColorId = Shader.PropertyToID("_BaseColor"), PhaseId = Shader.PropertyToID("_RipplePhase"), PassId = Shader.PropertyToID("_PassProgress");
        GameObject root;
        Mesh mesh;
        Material material;
        MeshRenderer renderer;
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        Vector3 center;
        Quaternion rotation;
        float idleAge, revealAge, ripplePhase, closestDistance = float.PositiveInfinity, expiredDistance, dissolve = -1f;
        bool expired, configured, cueActive, consumed;
        public Transform Root => root == null ? null : root.transform;
        public bool CueActive => cueActive;
        public bool Visible => root != null && root.activeSelf && renderer.enabled;
        public bool Expired => expired;
        public float Opacity { get; private set; }
        public float RipplePhase => ripplePhase;

        public OceanActivityGateEffect(Transform parent)
        {
            Shader shader = Resources.Load<Shader>("Shaders/ActivityRippleGate");
            if (shader == null) throw new InvalidOperationException("The water-ripple shader is required for the gate cue.");
            material = new Material(shader) { name = "Transient luminous water ripple" };
            mesh = BuildMesh();
            root = new GameObject("Transient activity bubble gate"); root.transform.SetParent(parent, false);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            root.SetActive(false);
        }
        public void Configure(Vector3 at, Quaternion facing)
        {
            center = at; rotation = facing; configured = true;
            idleAge = revealAge = 0f; expired = cueActive = consumed = false; closestDistance = float.PositiveInfinity;
            // Retain a passing cue at its old pose while the next logical waypoint moves.
            if (dissolve < 0f) Hide();
        }
        public void Tick(float dt, Vector3 eye, bool active, bool complete)
        {
            if (root == null) return;
            if (!configured || !active || complete || !Finite(dt) || dt <= 0f || dt > .1f || !Finite(eye))
            { Suspend(); return; }
            // A bounded explicit clock is deterministic and freezes when suspended.
            // Configure(next) must not reset the old-position passing wave.
            ripplePhase = Mathf.Repeat(ripplePhase + dt * .5f, 1f);
            if (dissolve >= 0f)
            {
                dissolve += dt; cueActive = false;
                if (dissolve >= DissolveSeconds) { dissolve = -1f; Hide(); return; }
                float t = dissolve / DissolveSeconds;
                root.transform.localScale = Vector3.one * Radius * (1f + .22f * t);
                Draw(.5f * (1f - t) * (1f - t)); return;
            }
            if (consumed) { cueActive = false; Hide(); return; }
            float distance = Vector3.Distance(eye, center);
            if (distance > RearmRange)
            { expired = false; idleAge = revealAge = 0f; closestDistance = float.PositiveInfinity; }
            bool approachingSide = Vector3.Dot(eye - center, rotation * Vector3.forward) < .35f;
            if (distance > ApproachRange || !approachingSide) { cueActive = false; Hide(); return; }
            if (expired)
            {
                if (distance <= expiredDistance - .5f)
                { expired = false; idleAge = revealAge = 0f; closestDistance = distance; }
                else { cueActive = false; Hide(); return; }
            }
            if (distance < closestDistance - .04f) { closestDistance = distance; idleAge = 0f; }
            else idleAge += dt;
            revealAge += dt;
            if (idleAge >= CueLifetime)
            { expired = true; expiredDistance = distance; cueActive = false; Hide(); return; }
            cueActive = true;
            root.transform.SetPositionAndRotation(center, rotation * Quaternion.AngleAxis(revealAge * 5f, Vector3.forward));
            float enter = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(revealAge / .4f));
            root.transform.localScale = Vector3.one * Radius * (.97f + .03f * enter + .006f * Mathf.Sin(revealAge * 1.5f));
            Draw(.64f * enter * Mathf.Clamp01(CueLifetime - idleAge));
        }
        public void Crossed()
        {
            cueActive = false; consumed = true;
            if (Visible) dissolve = 0f;
            else { dissolve = -1f; Hide(); }
        }
        public void Suspend()
        {
            cueActive = false; dissolve = -1f; revealAge = 0f; Hide();
            // Keep idle expiry: opening a menu cannot leave or repeatedly respawn a permanent ornament.
        }
        void Draw(float alpha)
        {
            Opacity = alpha; root.SetActive(alpha > .001f); renderer.enabled = alpha > .001f;
            properties.SetColor(ColorId, new Color(.24f, .76f, 1f, alpha));
            properties.SetFloat(PhaseId, ripplePhase);
            properties.SetFloat(PassId, dissolve < 0f ? 0f : Mathf.Clamp01(dissolve / DissolveSeconds));
            renderer.SetPropertyBlock(properties);
        }
        void Hide()
        { Opacity = 0f; if (root != null) { root.SetActive(false); renderer.enabled = false; } }
        static Mesh BuildMesh()
        {
            const int segments = 64;
            // Widen only the visual annulus; the .70m gameplay aperture is unchanged.
            float[] radii = { .62f, 1.38f };
            var vertices = new List<Vector3>(segments * 2); var uv = new List<Vector2>(segments * 2);
            var indices = new List<int>(segments * 6);
            for (int band = 0; band < radii.Length; band++)
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * Mathf.PI * 2f / segments;
                    Vector3 point = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radii[band];
                    vertices.Add(point); uv.Add(new Vector2(point.x, point.y));
                    if (band == radii.Length - 1) continue;
                    int a = band * segments + i, b = band * segments + (i + 1) % segments;
                    indices.Add(a); indices.Add(b); indices.Add(a + segments);
                    indices.Add(b); indices.Add(b + segments); indices.Add(a + segments);
                }
            var result = new Mesh { name = "Open-center water ripple annulus" };
            result.SetVertices(vertices); result.SetUVs(0, uv); result.SetTriangles(indices, 0); result.RecalculateBounds();
            return result;
        }
        public void Dispose()
        {
            Suspend(); configured = false;
            if (root != null) Release(root); if (mesh != null) Release(mesh); if (material != null) Release(material);
            root = null; mesh = null; material = null; renderer = null;
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static void Release(UnityEngine.Object value)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(value); return; }
#endif
            UnityEngine.Object.Destroy(value);
        }
    }
}

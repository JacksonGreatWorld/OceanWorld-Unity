using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>
    /// Sparse, fixed patches of suspended life make local water movement readable.
    /// The existing Bubble Vents still own the main bubble columns. This owner only
    /// adds a few intermittent fine bubbles at already-authored vent positions.
    /// </summary>
    public sealed class OceanSanctuaryMicroLife : MonoBehaviour
    {
        public const int MaximumRegions = 7;
        public const int MaximumVents = 3;
        public const int MaximumActiveRegions = 3;
        public const int MaximumActiveVents = 2;
        public const int ParticlesPerRegion = 80;
        public const int ParticlesPerVent = 12;
        public const int MaximumActiveParticles = MaximumActiveRegions * ParticlesPerRegion + MaximumActiveVents * ParticlesPerVent;
        public const int MaximumActiveRenderers = MaximumActiveRegions + MaximumActiveVents;
        public const string RootName = "Local sanctuary water life";

        // The seven authored discovery passages. These are world anchors, never a
        // camera-centred shell or repeated distance wall. Coordinates are metres.
        static readonly Vector2[] offsets = {
            new Vector2(0,31), new Vector2(-65,20), new Vector2(-45,-48),
            new Vector2(-12,-64), new Vector2(52,-55), new Vector2(79,3), new Vector2(-15,89)
        };
        static readonly Color nearTint = new Color(.83f,.89f,.73f,.30f);
        static readonly Color farTint = new Color(.41f,.72f,.83f,.075f);

        sealed class Patch
        {
            public ParticleSystem particles;
            public ParticleSystemRenderer renderer;
            public Vector3 anchor;
            public bool active, selected;
        }

        readonly List<Patch> regions = new List<Patch>(MaximumRegions);
        readonly List<Patch> vents = new List<Patch>(MaximumVents);
        Transform viewer;
        Material microMaterial;
        float nextRefresh;
        bool initialized, applicationPaused;

        public int RegionCount => regions.Count;
        public int VentCount => vents.Count;
        public int ActiveRendererCount { get; private set; }
        public int ActiveParticleBudget { get; private set; }

        /// <summary>
        /// Call once after sanctuary geometry/vent authoring, with the persistent
        /// environment parent, the tracked eye, and its existing bubble material.
        /// Repeated calls return the same owner; the caller must destroy the parent
        /// to dispose it. Borrowed materials/textures are never destroyed here.
        /// </summary>
        public static OceanSanctuaryMicroLife Build(Transform parent, Transform viewer,
            Func<float,float,float> floor, Vector3 spawn, IReadOnlyList<Vector3> bubbleSources,
            Material sharedBubbleMaterial)
        {
            if (parent == null || viewer == null || floor == null)
                throw new ArgumentNullException(parent == null ? nameof(parent) : viewer == null ? nameof(viewer) : nameof(floor));
            Transform previous = parent.Find(RootName);
            if (previous != null && previous.TryGetComponent(out OceanSanctuaryMicroLife existing))
            {
                existing.viewer = viewer;
                return existing;
            }
            Shader shader = Resources.Load<Shader>("Shaders/BubbleParticle");
            Texture2D texture = Resources.Load<Texture2D>("Textures/Bubbles/bubble_ring_glowCircle_512");
            if (shader == null || texture == null || sharedBubbleMaterial == null)
            {
                Debug.LogWarning("[SanctuaryMicroLife] Existing particle resources missing; optional local life omitted.");
                return null;
            }
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            var owner = root.AddComponent<OceanSanctuaryMicroLife>();
            owner.viewer = viewer;
            owner.microMaterial = new Material(shader) { name = "Shared sanctuary suspended particles" };
            owner.microMaterial.SetTexture("_BaseMap", texture);
            owner.microMaterial.SetColor("_BaseColor", Color.white);
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 p = spawn + new Vector3(offsets[i].x, 0, offsets[i].y);
                p.y = floor(p.x,p.z) + 1.28f;
                owner.regions.Add(owner.CreatePatch("Suspended life passage " + (i+1), p, false, owner.microMaterial, i));
            }
            owner.SelectExistingVents(bubbleSources, sharedBubbleMaterial);
            owner.initialized = true;
            owner.RefreshVisibility();
            Debug.Log("[SanctuaryMicroLife] fixedRegions=" + owner.RegionCount + " fineBubbleSources=" + owner.VentCount +
                " maxActiveRenderers=" + MaximumActiveRenderers + " maxActiveParticles=" + MaximumActiveParticles +
                "; 1 owned material + borrowed bubble material, no colliders or camera-follow geometry");
            return owner;
        }

        void SelectExistingVents(IReadOnlyList<Vector3> sources, Material material)
        {
            if (sources == null) return;
            // Entrance, southern basin and eastern ravine get a small breath-like cue.
            int[] homes = { 0, 3, 5 };
            for (int home = 0; home < homes.Length; home++)
            {
                Vector3 target = regions[homes[home]].anchor;
                float closest = 22f * 22f;
                int selected = -1;
                for (int i = 0; i < sources.Count; i++)
                {
                    Vector3 delta = sources[i] - target;
                    float distance = delta.x*delta.x + delta.z*delta.z;
                    bool used = false;
                    foreach (Patch prior in vents) if ((prior.anchor-sources[i]).sqrMagnitude < .01f) { used = true; break; }
                    if (!used && distance < closest) { selected = i; closest = distance; }
                }
                if (selected >= 0)
                    vents.Add(CreatePatch("Intermittent fine bubbles " + (home+1), sources[selected], true, material, home+71));
            }
        }

        Patch CreatePatch(string label, Vector3 anchor, bool vent, Material material, int seed)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            go.transform.position = anchor;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)(61517+seed*101);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = vent ? 12f : 18f;
            main.prewarm = false;
            main.startSpeed = 0f;
            main.startLifetime = vent ? new ParticleSystem.MinMaxCurve(2.8f,4.2f) : new ParticleSystem.MinMaxCurve(12f,16f);
            main.startSize = vent ? new ParticleSystem.MinMaxCurve(.008f,.026f) : new ParticleSystem.MinMaxCurve(.012f,.024f);
            main.startColor = vent ? new Color(.83f,.94f,.95f,.66f) : nearTint;
            main.maxParticles = vent ? ParticlesPerVent : ParticlesPerRegion;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.Pause;
            var emission = ps.emission;
            emission.rateOverTime = vent ? .16f : 4.4f;
            if (vent)
                emission.SetBursts(new[] { new ParticleSystem.Burst(1.2f, (short)2, (short)4, 0, 7.2f) });
            var shape = ps.shape;
            shape.shapeType = vent ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Box;
            if (vent) shape.radius = .018f;
            else shape.scale = new Vector3(7f,1.8f,6f);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = vent ? .025f : .068f;
            velocity.y = vent ? .24f : .012f;
            velocity.z = vent ? .014f : .035f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = vent ? .018f : .022f;
            noise.frequency = .26f;
            noise.scrollSpeed = .11f;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white,0), new GradientColorKey(Color.white,1) },
                new[] { new GradientAlphaKey(0,0), new GradientAlphaKey(1,.18f), new GradientAlphaKey(.75f,.76f), new GradientAlphaKey(0,1) });
            color.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;
            return new Patch { particles = ps, renderer = renderer, anchor = anchor };
        }

        void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;
            RefreshVisibility();
        }

        /// <summary>Recompute only visibility/emission. Patch transforms remain fixed.</summary>
        public void RefreshVisibility()
        {
            if (!initialized) return;
            ActiveRendererCount = 0; ActiveParticleBudget = 0;
            SelectNearby(regions,MaximumActiveRegions,28f,false);
            SelectNearby(vents,MaximumActiveVents,18f,true);
        }

        void SelectNearby(List<Patch> patches, int maximum, float radius, bool vent)
        {
            foreach (Patch patch in patches) patch.selected = false;
            if (viewer != null && isActiveAndEnabled && !applicationPaused)
                for (int slot = 0; slot < maximum; slot++)
                {
                    Patch best = null;
                    float closest = radius*radius;
                    foreach (Patch patch in patches)
                    {
                        float distance = (patch.anchor-viewer.position).sqrMagnitude;
                        if (!patch.selected && distance < closest) { best = patch; closest = distance; }
                    }
                    if (best == null) break;
                    best.selected = true;
                }
            foreach (Patch patch in patches)
            {
                if (patch.selected != patch.active)
                {
                    patch.active = patch.selected;
                    patch.renderer.enabled = patch.active;
                    if (patch.active) patch.particles.Play(false);
                    else patch.particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                if (!patch.active) continue;
                ActiveRendererCount++;
                ActiveParticleBudget += vent ? ParticlesPerVent : ParticlesPerRegion;
                if (!vent)
                {
                    float distance = Vector3.Distance(viewer.position,patch.anchor);
                    var main = patch.particles.main;
                    main.startColor = Color.Lerp(nearTint,farTint,Mathf.InverseLerp(4f,radius,distance));
                }
            }
        }

        void OnDisable()
        {
            if (!initialized) return;
            RefreshVisibility();
        }
        void OnEnable() { nextRefresh = 0; }
        void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (!paused) { nextRefresh = 0; return; }
            foreach (Patch patch in regions) Clear(patch);
            foreach (Patch patch in vents) Clear(patch);
            ActiveRendererCount = 0; ActiveParticleBudget = 0;
        }
        static void Clear(Patch patch)
        {
            patch.active = patch.selected = false;
            if (patch.particles != null) patch.particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            if (patch.renderer != null) patch.renderer.enabled = false;
        }
        void OnDestroy()
        {
            // Children/ParticleSystems belong to this root; only this material is ours.
            if (microMaterial == null) return;
            if (Application.isPlaying) Destroy(microMaterial);
            else DestroyImmediate(microMaterial);
        }
    }
}

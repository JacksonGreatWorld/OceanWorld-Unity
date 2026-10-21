using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>
    /// The same textured orange-and-white reef clownfish used in the game. Local +Z is its nose,
    /// +Y is up; Pose takes world coordinates. The caller owns the path and visibility.
    /// Use a parent with unit scale to retain the measured 0.30 metre length.
    /// </summary>
    public sealed class IntroGuideFish : MonoBehaviour
    {
        const float TargetLength = 0.30f;
        const string FullResource = "Fish/01_clownfish";
        // Shared with ordinary reef clownfish; these are original GLB mesh-space units.
        public const float SwingAmplitude = OceanFishVisualProfile.ClownfishAmplitude;
        public const float SwingFrequency = OceanFishVisualProfile.ClownfishCadence;
        public const float FishHalfLength = OceanFishVisualProfile.ClownfishHalfLength;
        public const float VerticalBob = OceanFishVisualProfile.ClownfishBob;
        static readonly int PhaseId = Shader.PropertyToID("_SwimPhase");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int MotionId = Shader.PropertyToID("_SwimMotion");

        public float Length { get; private set; }
        public bool IsVisible { get; private set; }
        public int TriangleCount { get; private set; }
        public int MaterialCount { get; private set; }
        public string ResourcePath { get; private set; }
        public float SwimSpeed { get; private set; }
        public float SwimBlend { get; private set; }
        public float TurnAmount { get; private set; }
        public float TailPhase => phase;
        public float FinPhase => phase * 2.4f;
        public bool IsIdle => SwimBlend < .15f;
        public bool UsesAuthoredAnimation => false;
        /// <summary>Rest-pose bounds in the component's centred, +Z-facing local space.</summary>
        public Bounds LocalBounds { get; private set; }

        Renderer[] renderers;
        MaterialPropertyBlock[] properties;
        readonly List<Material> ownedMaterials = new List<Material>();
        float phase;
        Vector3 lastForward;
        bool haveHeading;
        int lastPoseFrame = -1;
        bool disposed;
        /// <summary>Returns null, with one warning, if the actual model/shader is unavailable.</summary>
        public static IntroGuideFish Create(Transform parent)
        {
            // Deliberately use the full reef resource; a different rig/fallback changes the fish
            // the player recognizes from the underwater world.
            GameObject prefab = Resources.Load<GameObject>(FullResource);
            const string resource = FullResource;
            Shader shader = Resources.Load<Shader>("Shaders/IntroGuideFish");
            if (prefab == null || shader == null)
            {
                Debug.LogWarning("[IntroGuideFish] Clownfish model or guide shader is unavailable.");
                return null;
            }
            var root = new GameObject("IntroGuideFish");
            root.transform.SetParent(parent, false);
            var guide = root.AddComponent<IntroGuideFish>();
            guide.ResourcePath = resource;
            if (!guide.Initialize(prefab, shader))
            {
                Debug.LogWarning("[IntroGuideFish] Model has no usable finite mesh bounds.");
                guide.Dispose();
                return null;
            }
            guide.Pose(root.transform.position, root.transform.rotation, 0f);
            guide.haveHeading = false;
            guide.SetVisible(true);
            Debug.Log($"[IntroGuideFish] resource={resource}, length={guide.Length:F3}m, " +
                $"triangles={guide.TriangleCount}, materials={guide.MaterialCount}, authoredAnimation=False, " +
                $"reefSwingAmp={SwingAmplitude:F3}, reefSwingFreq={SwingFrequency:F3}, reefHalfLength={FishHalfLength:F3}, reefBob={VerticalBob:F3}, nose=+Z, up=+Y");
            return guide;
        }

        bool Initialize(GameObject prefab, Shader shader)
        {
            Transform model = new GameObject("Centred Clownfish").transform;
            model.SetParent(transform, false);
            Instantiate(prefab, model, false);
            renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;

            // The reef GLB's nose is +Z. Preserve its imported axes and texture coordinates.
            // Transform mesh-space bounds into this wrapper instead of normalizing
            // by a world AABB diagonal, which varies with a parent's rotation.
            Bounds bounds = default;
            bool haveBounds = false;
            foreach (Renderer renderer in renderers)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : (renderer as SkinnedMeshRenderer)?.sharedMesh;
                // Normalize from actual mesh bounds, independent of the parent's world rotation.
                Bounds local = mesh != null ? mesh.bounds : renderer.localBounds;
                Matrix4x4 matrix = model.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = local.center + Vector3.Scale(local.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    point = matrix.MultiplyPoint3x4(point);
                    if (!Finite(point)) return false;
                    if (!haveBounds) { bounds = new Bounds(point, Vector3.zero); haveBounds = true; }
                    else bounds.Encapsulate(point);
                }
                if (mesh != null)
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                        if (mesh.GetTopology(submesh) == MeshTopology.Triangles)
                            TriangleCount += (int)(mesh.GetIndexCount(submesh) / 3);
            }
            if (!haveBounds || bounds.size.z < 0.001f) return false;
            float scale = TargetLength / bounds.size.z;
            model.localScale = Vector3.one * scale;
            model.localPosition = -bounds.center * scale;
            Length = bounds.size.z * scale;
            LocalBounds = new Bounds(Vector3.zero, bounds.size * scale);

            properties = new MaterialPropertyBlock[renderers.Length];
            var materialCache = new Dictionary<Material, Material>();
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                Material[] sources = renderer.sharedMaterials;
                for (int material = 0; material < sources.Length; material++)
                {
                    Material source = sources[material];
                    if (source == null || !materialCache.TryGetValue(source, out Material owned))
                    {
                        owned = CreateMaterial(source, shader);
                        ownedMaterials.Add(owned);
                        if (source != null) materialCache.Add(source, owned);
                    }
                    sources[material] = owned;
                    MaterialCount++;
                }
                renderer.sharedMaterials = sources;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                // Include accelerated tail, turning and pectoral flutter in CPU culling bounds.
                // Never mutate the shared source mesh bounds.
                Bounds shape = renderer.localBounds;
                properties[index] = new MaterialPropertyBlock();
                shape.Expand(new Vector3(2f * (SwingAmplitude * 1.6f + .025f), 2f * (VerticalBob + .025f), 0f));
                renderer.localBounds = shape;
            }
            return true;
        }

        static Material CreateMaterial(Material source, Shader shader)
        {
            var material = new Material(shader) { name = "Intro Clownfish (owned)", enableInstancing = true };
            material.SetFloat("_SwingAmp", SwingAmplitude);
            material.SetFloat("_SwingFreq", SwingFrequency);
            material.SetFloat("_FishHalfLength", FishHalfLength);
            material.SetFloat("_VerticalBob", VerticalBob);
            OceanFishVisualProfile.Apply(material, FullResource);
            if (source == null) return material;
            CopyTexture(source, material, "baseColorTexture", "_BaseMap");
            CopyTexture(source, material, "_BaseMap", "_BaseMap");
            CopyTexture(source, material, "_MainTex", "_BaseMap");
            CopyTexture(source, material, "metallicRoughnessTexture", "_MetallicRoughnessMap");
            CopyTexture(source, material, "_MetallicGlossMap", "_MetallicRoughnessMap");
            if (source.HasProperty("baseColorFactor")) material.SetColor("_BaseColor", source.GetColor("baseColorFactor"));
            else if (source.HasProperty("_BaseColor")) material.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            else if (source.HasProperty("_Color")) material.SetColor("_BaseColor", source.GetColor("_Color"));
            float metallic = source.HasProperty("metallicFactor") ? source.GetFloat("metallicFactor")
                : source.HasProperty("_Metallic") ? source.GetFloat("_Metallic") : 0f;
            float roughness = source.HasProperty("roughnessFactor") ? source.GetFloat("roughnessFactor")
                : source.HasProperty("_Smoothness") ? 1f - source.GetFloat("_Smoothness") : 1f;
            material.SetFloat("_Metallic", Mathf.Clamp01(metallic));
            material.SetFloat("_Roughness", Mathf.Clamp01(roughness));
            return material;
        }

        static void CopyTexture(Material source, Material target, string from, string to)
        {
            if (!source.HasProperty(from)) return;
            Texture texture = source.GetTexture(from);
            if (texture == null) return;
            target.SetTexture(to, texture);
            target.SetTextureScale(to, source.GetTextureScale(from));
            target.SetTextureOffset(to, source.GetTextureOffset(from));
        }

        /// <summary>Position is the rest-pose body centre; rotation's forward vector is the nose.
        /// The reef's same anatomical deformation smoothly follows this sequence's movement speed.
        /// Resting keeps a small tail/fin stroke; acceleration never resets its continuous phase.
        /// tailWag is retained for call compatibility but intentionally adds no farewell/body shake.
        /// Only visible-sequence Pose calls advance the bounded, continuous unscaled clock.</summary>
        public void Pose(Vector3 position, Quaternion rotation, float swimSpeed, float glow = 1f, float tailWag = 0f)
        {
            if (disposed || properties == null || !Finite(position) || !Finite(swimSpeed) || !Finite(glow) || !Finite(tailWag) ||
                !Finite(rotation.x) || !Finite(rotation.y) || !Finite(rotation.z) || !Finite(rotation.w)) return;
            if (lastPoseFrame != Time.frameCount)
            {
                float elapsed = Time.unscaledDeltaTime;
                float dt = Finite(elapsed) ? Mathf.Clamp(elapsed, 0f, .05f) : 0f;
                float response = 1f - Mathf.Exp(-dt * 5f);
                SwimSpeed = Mathf.Lerp(SwimSpeed, Mathf.Min(Mathf.Abs(swimSpeed), 2f), response);
                SwimBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.035f, .38f, SwimSpeed));
                Vector3 forward = rotation * Vector3.forward;
                float turn = haveHeading && dt > .0001f
                    ? Mathf.Clamp(Vector3.SignedAngle(lastForward, forward, Vector3.up) / (dt * 100f), -1f, 1f) : 0f;
                TurnAmount = Mathf.Lerp(TurnAmount, turn, response);
                lastForward = forward; haveHeading = true;
                // A 20*pi wrap preserves all profile fin and half-rate body phases.
                phase = Mathf.Repeat(phase + dt * SwingFrequency * Mathf.Lerp(.75f, 1.55f, SwimBlend), Mathf.PI * 20f);
                lastPoseFrame = Time.frameCount;
            }
            transform.SetPositionAndRotation(position, rotation);
            for (int index = 0; index < renderers.Length; index++)
            {
                MaterialPropertyBlock block = properties[index];
                block.SetFloat(PhaseId, phase);
                block.SetFloat(GlowId, Mathf.Clamp(glow, 0f, 2f));
                block.SetVector(MotionId, new Vector4(phase, Mathf.Lerp(.52f, 1.16f, SwimBlend), TurnAmount, 1f));
                renderers[index].SetPropertyBlock(block);
            }
        }

        public void SetVisible(bool visible)
        {
            if (disposed) return;
            IsVisible = visible;
            if (renderers != null)
                for (int index = 0; index < renderers.Length; index++) renderers[index].enabled = visible;
        }

        public void Dispose()
        {
            if (disposed) return;
            SetVisible(false);
            disposed = true;
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        void OnDestroy()
        {
            foreach (Material material in ownedMaterials)
                if (material != null)
                {
                    if (Application.isPlaying) Destroy(material);
                    else DestroyImmediate(material);
                }
            ownedMaterials.Clear();
        }

        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

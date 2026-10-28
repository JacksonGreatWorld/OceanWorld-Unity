using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>
    /// Mesh-space swim parameters for the shipped static GLBs, not an authored skeletal rig.
    /// One profile belongs to each shared species material; phase remains an instanced property.
    /// Source axes differ: the great-white spine is diagonal XZ, and ray/flatfish use X.
    /// </summary>
    public static class OceanFishVisualProfile
    {
        static readonly int BodyId = Shader.PropertyToID("_FishBody");
        static readonly int FinId = Shader.PropertyToID("_FishFins");
        static readonly int AmpId = Shader.PropertyToID("_SwingAmp");
        static readonly int FreqId = Shader.PropertyToID("_SwingFreq");
        static readonly int HalfLengthId = Shader.PropertyToID("_FishHalfLength");
        static readonly int BobId = Shader.PropertyToID("_VerticalBob");

        public const float ClownfishAmplitude = .065f;
        public const float ClownfishCadence = 4.5f;
        public const float ClownfishHalfLength = .502f;
        public const float ClownfishBob = .008f;
        public const float GreatWhiteMeshYawCorrection = -45f;

        /// <summary>
        /// Body = (style, mesh half width, mesh half height, mesh half length).
        /// Fins = (tail start, fin displacement, fin cadence ratio, spine flexibility).
        /// Styles: 0 reef, 1 rigid body, 2 upright hover, 3 ray, 4 flatfish, 5 shark, 6 diagonal-source white shark.
        /// Caller may retain its original random draws before applying this deterministic profile.
        /// </summary>
        public static void Apply(Material material, string resourceName)
        {
            if (material == null || !material.HasProperty(BodyId)) return;
            string name = (resourceName ?? "").ToLowerInvariant();
            var body = new Vector4(0f, .24f, .28f, .502f);
            var fins = new Vector4(.05f, .018f, 2.4f, .10f);
            float amplitude = .065f, cadence = 4.5f, bob = .008f;
            if (name.Contains("clownfish"))
            {
                amplitude = ClownfishAmplitude; cadence = ClownfishCadence; bob = ClownfishBob;
            }
            else if (name.Contains("blue_tang"))
            {
                body = new Vector4(0f, .185f, .311f, .502f);
                fins = new Vector4(.15f, .016f, 2.6f, .07f);
                amplitude = .058f; cadence = 4f; bob = .006f;
            }
            else if (name.Contains("puffer"))
            {
                body = new Vector4(1f, .382f, .388f, .502f);
                fins = new Vector4(.55f, .022f, 4.1f, 0f);
                amplitude = .026f; cadence = 3f; bob = .003f;
            }
            else if (name.Contains("seahorse"))
            {
                body = new Vector4(2f, .127f, .503f, .315f);
                fins = new Vector4(.6f, .009f, 4.4f, 0f);
                amplitude = .008f; cadence = 4.5f; bob = .003f;
            }
            else if (name.Contains("manta"))
            {
                body = new Vector4(3f, .494f, .114f, .502f);
                fins = new Vector4(.35f, .085f, 1f, 0f);
                amplitude = .014f; cadence = 1.2f; bob = 0f;
            }
            else if (name.Contains("flounder"))
            {
                body = new Vector4(4f, .502f, .108f, .361f);
                fins = new Vector4(.12f, .013f, 2.5f, .04f);
                amplitude = .025f; cadence = 3.2f; bob = .002f;
            }
            else if (name.Contains("lionfish"))
            {
                body = new Vector4(0f, .353f, .366f, .502f);
                fins = new Vector4(.18f, .027f, 1.5f, .04f);
                amplitude = .043f; cadence = 3.5f; bob = .006f;
            }
            else if (name.Contains("mandarin"))
            {
                body = new Vector4(0f, .376f, .335f, .502f);
                fins = new Vector4(.22f, .023f, 2.4f, .055f);
                amplitude = .040f; cadence = 5f; bob = .005f;
            }
            else if (name.Contains("shark"))
            {
                // Measured after glTFast X reversal and -45 degree anatomical rotation.
                // This also gives the deformation shader the actual head-to-tail axis.
                body = name.Contains("great_white") ? new Vector4(6f, .5031f, .3383f, .7058f)
                    : new Vector4(5f, .280f, .220f, .5021f);
                fins = new Vector4(-.05f, .006f, 1f, .15f);
                amplitude = .076f; cadence = 1.8f; bob = .003f;
            }
            else if (name.Contains("tuna") || name.Contains("swordfish"))
            {
                fins = new Vector4(.28f, .006f, 1.8f, .04f);
                amplitude = .056f; cadence = 4f; bob = .003f;
            }
            else if (name.Contains("sunfish"))
            {
                body = new Vector4(1f, .185f, .502f, .483f);
                fins = new Vector4(.5f, .017f, 1.6f, 0f);
                amplitude = .018f; cadence = 2f; bob = .003f;
            }
            else if (name.Contains("moray"))
            {
                // This source mesh has an X-axis spine, like the flatfish. Keep the
                // axis correction local to deformation; movement owns world orientation.
                body = new Vector4(4f, .502f, .162f, .418f);
                fins = new Vector4(-.35f, .004f, 1.2f, .28f);
                amplitude = .085f; cadence = 2.5f; bob = .002f;
            }
            material.SetVector(BodyId, body);
            material.SetVector(FinId, fins);
            material.SetFloat(AmpId, amplitude);
            material.SetFloat(FreqId, cadence);
            material.SetFloat(HalfLengthId, body.w);
            material.SetFloat(BobId, bob);
        }

        /// <summary>Include GPU tail/wing strokes without changing the shared mesh asset.</summary>
        public static void ExpandRendererBounds(Renderer renderer)
        {
            if (renderer == null) return;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : (renderer as SkinnedMeshRenderer)?.sharedMesh;
            if (mesh == null) return;
            Bounds bounds = mesh.bounds;
            // Covers the largest profile at accelerated stroke and maximum turning.
            bounds.Expand(new Vector3(.32f, .24f, .24f));
            Bounds current = renderer.localBounds;
            current.Encapsulate(bounds);
            renderer.localBounds = current;
        }
    }

    /// <summary>
    /// Optional near-fish clock, ticked by the existing fish owner. No MonoBehaviour,
    /// per-frame allocations, new materials, or CPU vertex deformation are introduced.
    /// Far schools can keep the shader's cheaper shared clock.
    /// </summary>
    public sealed class OceanFishMotion
    {
        static readonly int MotionId = Shader.PropertyToID("_SwimMotion");
        static readonly int PhaseId = Shader.PropertyToID("_SwingPhase");
        static readonly int FrequencyId = Shader.PropertyToID("_SwingFreq");
        static readonly int SchoolClockId = Shader.PropertyToID("_SchoolClock");
        static readonly int BodyId = Shader.PropertyToID("_FishBody");
        readonly Transform transform;
        readonly Renderer[] renderers;
        readonly MaterialPropertyBlock[] blocks;
        readonly bool[] deforming;
        readonly float[] phaseOffsets;
        readonly float length, cadence;
        readonly int firstRenderer = -1;
        Vector3 previousPosition, previousForward;
        float phase, speed, turn, stroke = 1f;
        bool owned;

        public OceanFishMotion(GameObject fish, float bodyLength)
        {
            transform = fish.transform;
            length = Finite(bodyLength) ? Mathf.Max(.08f, bodyLength) : .08f;
            renderers = fish.GetComponentsInChildren<Renderer>(true);
            blocks = new MaterialPropertyBlock[renderers.Length];
            deforming = new bool[renderers.Length];
            phaseOffsets = new float[renderers.Length];
            cadence = OceanFishVisualProfile.ClownfishCadence;
            for (int i = 0; i < renderers.Length; i++)
            {
                Material material = renderers[i].sharedMaterial;
                // Authored skins keep their original Animator, bounds and property block.
                // This clock only owns the static mesh deformation shader's contract.
                if (renderers[i] is SkinnedMeshRenderer || !SupportsClock(material)) continue;
                deforming[i] = true;
                blocks[i] = new MaterialPropertyBlock();
                OceanFishVisualProfile.ExpandRendererBounds(renderers[i]);
                renderers[i].GetPropertyBlock(blocks[i]);
                if (firstRenderer >= 0) continue;
                firstRenderer = i;
                cadence = ReadFrequency(material, blocks[i]);
            }
            previousPosition = transform.position;
            previousForward = transform.forward;
        }

        /// <summary>Return an ambient fish to its shared shader clock when its temporary movement owner ends.</summary>
        public void Release()
        {
            if (!owned) return;
            float timestamp = Time.time;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!deforming[i] || renderers[i] == null) continue;
                Material material = renderers[i].sharedMaterial;
                if (!SupportsClock(material)) continue;
                renderers[i].GetPropertyBlock(blocks[i]);
                float visiblePhase = ReadRenderedPhase(material, blocks[i], timestamp);
                // Mode 0 uses time*frequency+offset. Rebase its offset to the
                // current owned phase, rather than jumping to the old shared clock.
                blocks[i].SetFloat(PhaseId, visiblePhase - timestamp * ReadFrequency(material, blocks[i]));
                blocks[i].SetVector(MotionId, Vector4.zero);
                renderers[i].SetPropertyBlock(blocks[i]);
            }
            owned = false;
        }

        public void Tick(float deltaTime)
        {
            if (transform == null || firstRenderer < 0 || deltaTime <= 0f || !Finite(deltaTime)) return;
            float dt = Mathf.Min(deltaTime, .05f);
            Vector3 position = transform.position, forward = transform.forward;
            if (!owned)
            {
                Renderer first = renderers[firstRenderer];
                if (first == null || !SupportsClock(first.sharedMaterial)) return;
                first.GetPropertyBlock(blocks[firstRenderer]);
                Vector4 previousMotion = ReadVector(first.sharedMaterial, blocks[firstRenderer], MotionId);
                phase = ReadRenderedPhase(first.sharedMaterial, blocks[firstRenderer], Time.time);
                stroke = previousMotion.w >= .5f && Finite(previousMotion.y) ? Mathf.Clamp(previousMotion.y, .3f, 1.5f) : 1f;
                turn = previousMotion.w >= .5f && Finite(previousMotion.z) ? Mathf.Clamp(previousMotion.z, -1f, 1f) : 0f;
                previousPosition = position; previousForward = forward; speed = 0f; owned = true;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (!deforming[i] || renderers[i] == null) continue;
                    renderers[i].GetPropertyBlock(blocks[i]);
                    phaseOffsets[i] = ReadRenderedPhase(renderers[i].sharedMaterial, blocks[i], Time.time) - phase;
                }
                // The ownership frame keeps the already-rendered shader phase;
                // adaptation begins on the next tick without advancing that frame twice.
                WriteMotion(new Vector4(phase, stroke, turn, 1f));
                return;
            }
            float distance = Vector3.Distance(position, previousPosition);
            // Teleports and level placement do not become a one-frame tail burst.
            float observedSpeed = distance < length * 3f ? distance / deltaTime : 0f;
            float response = 1f - Mathf.Exp(-dt * 7f);
            speed = Mathf.Lerp(speed, Mathf.Clamp(observedSpeed / length, 0f, 4f), response);
            float angular = Mathf.Clamp(Vector3.SignedAngle(previousForward, forward, Vector3.up) / (deltaTime * 100f), -1f, 1f);
            turn = Mathf.Lerp(turn, angular, response);
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.05f, 1.8f, speed));
            phase = Mathf.Repeat(phase + dt * cadence * Mathf.Lerp(.75f, 1.55f, blend), Mathf.PI * 20f);
            stroke = Mathf.Lerp(stroke, Mathf.Lerp(.52f, 1.16f, blend), response);
            WriteMotion(new Vector4(phase, stroke, turn, 1f));
            previousPosition = position;
            previousForward = forward;
        }

        void WriteMotion(Vector4 motion)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!deforming[i] || renderers[i] == null) continue;
                // Other owners may update tint/highlight. Preserve their properties.
                renderers[i].GetPropertyBlock(blocks[i]);
                Vector4 rendererMotion = motion; rendererMotion.x += phaseOffsets[i];
                blocks[i].SetVector(MotionId, rendererMotion);
                renderers[i].SetPropertyBlock(blocks[i]);
            }
        }

        static bool SupportsClock(Material material) => material != null && material.HasProperty(BodyId) &&
            material.HasProperty(MotionId) && material.HasProperty(FrequencyId);
        static float ReadFrequency(Material material, MaterialPropertyBlock block)
        {
            float value = block.HasFloat(FrequencyId) ? block.GetFloat(FrequencyId) : material.GetFloat(FrequencyId);
            return Finite(value) ? Mathf.Max(.3f, value) : OceanFishVisualProfile.ClownfishCadence;
        }
        static Vector4 ReadVector(Material material, MaterialPropertyBlock block, int id)
            => block.HasVector(id) ? block.GetVector(id) : material.GetVector(id);
        static float ReadRenderedPhase(Material material, MaterialPropertyBlock block, float timestamp)
        {
            Vector4 motion = ReadVector(material, block, MotionId);
            float result;
            if (motion.w >= 1.5f)
            {
                Vector4 clock = ReadVector(material, block, SchoolClockId);
                result = motion.x + Mathf.Clamp(timestamp - clock.x, 0f, Mathf.Max(0f, clock.z)) * clock.y;
            }
            else if (motion.w >= .5f) result = motion.x;
            else
            {
                float offset = block.HasFloat(PhaseId) ? block.GetFloat(PhaseId) : material.GetFloat(PhaseId);
                result = timestamp * ReadFrequency(material, block) + offset;
            }
            return Finite(result) ? Mathf.Repeat(result, Mathf.PI * 20f) : 0f;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
#if UNITY_EDITOR
        public static float RenderedPhaseForValidation(Material material, MaterialPropertyBlock block, float timestamp)
            => ReadRenderedPhase(material, block, timestamp);
#endif
    }
}

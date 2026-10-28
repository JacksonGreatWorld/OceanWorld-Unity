#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace SwanUnderwater.Editor
{
    /// <summary>Asset/clock regression checks; this does not replace worn-device visual review.</summary>
    public static class OceanFishVisualChecks
    {
        public static void Run()
        {
            Shader reef = Resources.Load<Shader>("Shaders/FishSwim");
            Shader guide = Resources.Load<Shader>("Shaders/IntroGuideFish");
            Require(reef != null && guide != null, "Both actual fish shaders must be present.");
            Require(!ShaderUtil.ShaderHasError(reef) && !ShaderUtil.ShaderHasError(guide), "Fish shader import has errors.");
            var material = new Material(reef);
            GameObject fish = null;
            try
            {
                OceanFishVisualProfile.Apply(material, "01_clownfish");
                Require(material.GetVector("_FishBody").w > .50f, "Clownfish mask must cover its real GLB half length.");
                Require(material.GetFloat("_SwingAmp") < .09f, "A clownfish should not bend its entire head/body.");
                var prefab = Resources.Load<GameObject>("Fish/01_clownfish");
                Require(prefab != null, "The ordinary reef clownfish resource is required.");
                fish = UnityEngine.Object.Instantiate(prefab);
                Renderer[] renderers = fish.GetComponentsInChildren<Renderer>();
                Require(renderers.Length > 0, "Clownfish must contain a visible mesh.");
                foreach (Renderer renderer in renderers) renderer.sharedMaterial = material;
                var originalProperties = new MaterialPropertyBlock();
                originalProperties.SetFloat("_SwingPhase", .4f);
                originalProperties.SetColor("_BaseColor", Color.cyan);
                foreach (Renderer renderer in renderers) renderer.SetPropertyBlock(originalProperties);
                var clock = new OceanFishMotion(fish, .30f);
                clock.Tick(1f / 90f);
                var sample = new MaterialPropertyBlock();
                renderers[0].GetPropertyBlock(sample);
                Require(EquivalentPhase(sample.GetVector("_SwimMotion").x,
                    Time.time * material.GetFloat("_SwingFreq") + .4f),
                    "The ownership frame must inherit the currently rendered shared phase, including shader time.");
                Require(Mathf.Abs(sample.GetVector("_SwimMotion").y - 1f) < .00001f,
                    "A newly owned fish inherits its shared-clock stroke before adapting calmly.");
                for (int i = 0; i < 180; i++) clock.Tick(1f / 90f);
                renderers[0].GetPropertyBlock(sample);
                float idleStroke = sample.GetVector("_SwimMotion").y;
                Require(idleStroke < .53f, "A fish at rest gradually settles to a calm stroke instead of snapping its amplitude.");
                // Another owner updates highlight tint after construction. Animation must retain it.
                sample.SetColor("_BaseColor", Color.magenta);
                renderers[0].SetPropertyBlock(sample);
                for (int i = 0; i < 180; i++)
                {
                    fish.transform.position += Vector3.forward * (.60f / 90f);
                    clock.Tick(1f / 90f);
                }
                renderers[0].GetPropertyBlock(sample);
                Vector4 swimming = sample.GetVector("_SwimMotion");
                Require(swimming.y > idleStroke + .4f, "Sustained swimming must increase tail stroke smoothly.");
                Require(sample.GetColor("_BaseColor") == Color.magenta, "Animation must preserve an external highlight property.");
                float previousPhase = swimming.x;
                clock.Tick(float.NaN);
                clock.Tick(0f);
                renderers[0].GetPropertyBlock(sample);
                Require(sample.GetVector("_SwimMotion").x == previousPhase, "Invalid/zero elapsed time must not corrupt phase.");
                fish.transform.position += Vector3.forward * 100f;
                clock.Tick(1f / 90f);
                renderers[0].GetPropertyBlock(sample);
                Require(sample.GetVector("_SwimMotion").x - previousPhase < .1f, "Level relocation must not cause a burst or reset the tail clock.");
                // Repeated bound preparation must not inflate culling bounds every time.
                Bounds prepared = renderers[0].localBounds;
                OceanFishVisualProfile.ExpandRendererBounds(renderers[0]);
                Require((renderers[0].localBounds.size - prepared.size).sqrMagnitude < .000001f, "GPU culling bound expansion must be idempotent.");
                CheckOwnershipTransfer(fish, renderers, material, clock);
                CheckNativeAnimationIsolation();
                Debug.Log("OCEAN_FISH_VISUAL_CHECKS_PASS: real clownfish asset, shader imports, speed-driven phase, shader-clock handoff, native Animator isolation, tint preservation, invalid dt, teleport and culling bounds. Device appearance remains unverified.");
            }
            finally
            {
                if (fish != null) UnityEngine.Object.DestroyImmediate(fish);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        static void CheckOwnershipTransfer(GameObject fish, Renderer[] renderers, Material material, OceanFishMotion clock)
        {
            var properties = new MaterialPropertyBlock();
            renderers[0].GetPropertyBlock(properties);
            float ownedPhase = properties.GetVector("_SwimMotion").x;
            clock.Release();renderers[0].GetPropertyBlock(properties);
            Require(properties.GetVector("_SwimMotion").w == 0f &&
                EquivalentPhase(Time.time * material.GetFloat("_SwingFreq") + properties.GetFloat("_SwingPhase"), ownedPhase),
                "Returning to the shared shader clock must retain the owned phase at the handoff frame.");
            Require(properties.GetColor("_BaseColor") == Color.magenta, "Clock release retains another owner's highlight tint.");
            float returnedOffset = properties.GetFloat("_SwingPhase");clock.Release();renderers[0].GetPropertyBlock(properties);
            Require(properties.GetFloat("_SwingPhase") == returnedOffset, "A duplicate release cannot rebase the shared clock a second time.");
            clock.Tick(1f / 90f);renderers[0].GetPropertyBlock(properties);
            Require(EquivalentPhase(properties.GetVector("_SwimMotion").x, ownedPhase),
                "The same clock can reacquire the current shared pose without replaying its old phase.");

            // Independently reproduce the actual shader's bounded mode-2 expression,
            // rather than using the production reader to construct its own expectation.
            float now = Time.time;
            Vector4 previousMotion = new Vector4(2.3f, .86f, -.3f, 2f);
            Vector4 previousClock = new Vector4(now - .2f, 3f, .3f, 0f);
            foreach (Renderer renderer in renderers)
            {
                renderer.GetPropertyBlock(properties);
                properties.SetVector("_SwimMotion", previousMotion);properties.SetVector("_SchoolClock", previousClock);
                renderer.SetPropertyBlock(properties);
            }
            float predicted = previousMotion.x + Mathf.Min(Mathf.Max(now - previousClock.x, 0f), previousClock.z) * previousClock.y;
            var takeover = new OceanFishMotion(fish, .3f);takeover.Tick(1f / 90f);renderers[0].GetPropertyBlock(properties);
            Vector4 inherited = properties.GetVector("_SwimMotion");
            Require(EquivalentPhase(inherited.x, predicted) && inherited.y == previousMotion.y && inherited.z == previousMotion.z && inherited.w == 1f,
                "Taking over an extrapolated school clock inherits its real phase, stroke and turn on the ownership frame.");
            Require(EquivalentPhase(OceanFishMotion.RenderedPhaseForValidation(material, properties, now + 10f), inherited.x),
                "Mode 1 is CPU-owned and does not accidentally extrapolate an inherited school clock.");
            properties.SetVector("_SwimMotion", previousMotion);properties.SetVector("_SchoolClock", previousClock);
            Require(EquivalentPhase(OceanFishMotion.RenderedPhaseForValidation(material, properties, now + 10f), 3.2f),
                "A paused mode-2 owner saturates the shader's original maximum extrapolation.");
            Require(Mathf.Abs(Mathf.DeltaAngle(.4f * Mathf.Rad2Deg,
                (.4f + 1.1f * material.GetFloat("_SwingFreq")) * Mathf.Rad2Deg)) > 20f,
                "Negative control exposes the removed offset-only handoff at a nonzero shader timestamp.");

            // Material defaults are real shader inputs when this renderer has no MPB
            // override. GetFloat on an absent property used to silently substitute zero.
            material.SetFloat("_SwingPhase", .73f);
            foreach (Renderer renderer in renderers) renderer.SetPropertyBlock(null);
            var defaultClock = new OceanFishMotion(fish, .3f);defaultClock.Tick(1f / 90f);renderers[0].GetPropertyBlock(properties);
            Require(EquivalentPhase(properties.GetVector("_SwimMotion").x,
                Time.time * material.GetFloat("_SwingFreq") + .73f),
                "Unspecified MPB phase inherits the actual shared material phase instead of zero.");
            defaultClock.Release();
        }

        static void CheckNativeAnimationIsolation()
        {
            foreach (string species in new[] { "mackerel", "sea-turtle", "dolphin" })
            {
                GameObject prefab = Resources.Load<GameObject>("MarineExpansion/" + species);
                Require(prefab != null, "Native admitted animated species exists: " + species);
                GameObject native = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    Animator animator = native.GetComponentInChildren<Animator>(true);
                    Require(animator != null && animator.runtimeAnimatorController != null && !animator.applyRootMotion,
                        "Original in-place Animator is present: " + species);
                    RuntimeAnimatorController controller = animator.runtimeAnimatorController;
                    float animatorSpeed = animator.speed;bool animatorEnabled = animator.enabled;
                    Renderer[] renderers = native.GetComponentsInChildren<Renderer>(true);
                    var bounds = new Bounds[renderers.Length];var materials = new Material[renderers.Length];
                    var properties = new MaterialPropertyBlock();
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        bounds[i] = renderers[i].localBounds;materials[i] = renderers[i].sharedMaterial;
                        renderers[i].GetPropertyBlock(properties);properties.SetFloat("_NativeClockIsolationMarker", 7f);renderers[i].SetPropertyBlock(properties);
                    }
                    var clock = new OceanFishMotion(native, .4f);clock.Tick(1f / 90f);
                    native.transform.position += Vector3.forward * .01f;clock.Tick(1f / 90f);clock.Release();
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        renderers[i].GetPropertyBlock(properties);
                        Require(!properties.HasVector("_SwimMotion") && properties.GetFloat("_NativeClockIsolationMarker") == 7f,
                            "Authored skin property blocks do not acquire a second deformation clock: " + species);
                        Require(renderers[i].localBounds == bounds[i] && renderers[i].sharedMaterial == materials[i],
                            "Native animated bounds and material remain owned by their authored rig: " + species);
                    }
                    Require(animator.runtimeAnimatorController == controller && animator.speed == animatorSpeed && animator.enabled == animatorEnabled && !animator.applyRootMotion,
                        "The optional mesh clock cannot alter Animator/root-motion state: " + species);
                }
                finally { UnityEngine.Object.DestroyImmediate(native); }
            }
        }

        static bool EquivalentPhase(float first, float second)
            => Mathf.Abs(Mathf.DeltaAngle(first * Mathf.Rad2Deg, second * Mathf.Rad2Deg)) < .015f;

        static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("Ocean fish visual check: " + detail);
        }
    }
}
#endif

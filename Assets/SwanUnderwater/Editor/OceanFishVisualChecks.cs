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
                float idleStroke = sample.GetVector("_SwimMotion").y;
                Require(sample.GetVector("_SwimMotion").x > .4f, "The first clock step must preserve the initial individual phase.");
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
                Debug.Log("OCEAN_FISH_VISUAL_CHECKS_PASS: real clownfish asset, shader imports, continuous speed-driven phase, tint preservation, invalid dt, teleport, culling bounds. Device appearance remains unverified.");
            }
            finally
            {
                if (fish != null) UnityEngine.Object.DestroyImmediate(fish);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("Ocean fish visual check: " + detail);
        }
    }
}
#endif

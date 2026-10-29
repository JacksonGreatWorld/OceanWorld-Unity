using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>
    /// World-owned shared variants of admitted source materials. No source asset is
    /// rewritten, and no material, lookup or array is created in a per-frame update.
    /// Disabling the owner retains its live renderers; destruction releases variants.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OceanMarineMaterialLibrary : MonoBehaviour, IDisposable
    {
        public const string ButterflySourceShader = "SwanUnderwater/ButterflyFishSwim";
        public const string ButterflyNearShader = "SwanUnderwater/ButterflyFishNear";
        const string ButterflyShaderResource = "Shaders/ButterflyFishNear";
        readonly Dictionary<Material, Material> butterflyMaterials = new Dictionary<Material, Material>();
        Shader butterflyShader;
        bool warnedUnavailable;
        public int OwnedMaterialCount => butterflyMaterials.Count;

        public static OceanMarineMaterialLibrary For(GameObject owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            var library = owner.GetComponent<OceanMarineMaterialLibrary>();
            return library != null ? library : owner.AddComponent<OceanMarineMaterialLibrary>();
        }

        /// <summary>One shared instance per admitted source and world; borrowed sources are never changed or destroyed.</summary>
        public Material GetButterflyNear(Material source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.shader == null || source.shader.name != ButterflySourceShader)
                throw new InvalidOperationException("Butterfly near variant requires its admitted transparent-fin source shader.");
            if (butterflyMaterials.TryGetValue(source, out Material cached) && cached != null) return cached;
            if (butterflyShader == null) butterflyShader = Resources.Load<Shader>(ButterflyShaderResource);
            if (butterflyShader == null || !butterflyShader.isSupported)
            {
                // A missing optional visual variant must not remove an otherwise valid
                // creature. Build checks require it; unsupported runtime hardware keeps
                // the admitted original instead of a missing/pink material.
                if (!warnedUnavailable)
                {
                    Debug.LogWarning("[MarineMaterials] Near butterfly lighting unavailable; admitted source material retained.");
                    warnedUnavailable = true;
                }
                return source;
            }
            var material = new Material(source) { shader = butterflyShader };
            material.CopyPropertiesFromMaterial(source);
            material.name = source.name + " (world-owned near lighting)";
            material.hideFlags = HideFlags.DontSave;
            // Preserve source maps, UV scale/offset, opacity, packed MR, body masks,
            // custom queue and instancing policy. Only the shader's lighting differs.
            material.enableInstancing = source.enableInstancing;
            material.renderQueue = source.renderQueue;
            butterflyMaterials[source] = material;
            return material;
        }

        /// <summary>Called only when a butterfly is created, never from Update or every menu refresh.</summary>
        public int ApplyButterflyNear(GameObject model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            int changed = 0;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool assigned = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null || source.shader == null || source.shader.name != ButterflySourceShader) continue;
                    Material detailed = GetButterflyNear(source);
                    if (detailed == source) continue;
                    materials[i] = detailed; assigned = true; changed++;
                }
                if (!assigned) continue;
                renderer.sharedMaterials = materials;
                renderer.receiveShadows = true;
            }
            return changed;
        }

        /// <summary>Release after dependent renderers are removed; reusable by isolated Editor owners and lifecycle tests.</summary>
        public void Dispose()
        {
            foreach (Material material in butterflyMaterials.Values)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            butterflyMaterials.Clear();
        }
        void OnDestroy() => Dispose();
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>A distant, non-interactive celestial ray above the playable ocean.</summary>
    public sealed class OceanSkyGuardian : MonoBehaviour
    {
        public const float WingSpan = OceanGuardianComposition.WingSpan, MinimumBodyGap = OceanGuardianComposition.MinimumBodyGap,
            OrbitSeconds = OceanGuardianComposition.OrbitSeconds;
        readonly List<Material> materials = new List<Material>();
        UnderwaterWorld owner;
        Camera view;
        Vector3 encounterEye, encounterForward, encounterRight;
        double elapsed;
        float compositionDistanceScale = 1f;
        public int TriangleCount { get; private set; }
        public float ActualWingSpan { get; private set; }
        public float VerticalEnvelope { get; private set; }
        public float ConservativeBottomY => transform.position.y - VerticalEnvelope;
        public Vector3 RevealEye => encounterEye;
        public Vector3 RevealForward => encounterForward;
        public bool UsesEntryAnchor { get; private set; }

        public static OceanSkyGuardian Build(UnderwaterWorld owner, Camera view, Vector3 spawn, Transform environmentParent = null)
        {
            var prefab = Resources.Load<GameObject>("Fish/16_manta_ray");
            Shader shader = Shader.Find("SwanUnderwater/SkyGuardian");
            if (prefab == null || shader == null) throw new InvalidOperationException("Missing sky guardian mesh or shader.");
            var root = new GameObject("深海巨鲲 · 天空巡游"); root.transform.SetParent(environmentParent != null ? environmentParent : owner.transform, false);
            var guardian = root.AddComponent<OceanSkyGuardian>(); guardian.owner = owner; guardian.view = view;
            if (view == null) throw new ArgumentNullException(nameof(view));
            var entry = OceanEntry.Instance;
            guardian.UsesEntryAnchor = entry != null && entry.TryGetEncounterAnchor(out guardian.encounterEye, out guardian.encounterForward);
            if (!guardian.UsesEntryAnchor)
            {
                guardian.encounterEye = view.transform.position;
                guardian.encounterForward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up);
                if (guardian.encounterForward.sqrMagnitude < .02f) guardian.encounterForward = Vector3.forward;
            }
            guardian.encounterForward.Normalize();
            guardian.encounterRight = Vector3.Cross(Vector3.up, guardian.encounterForward);
            var model = Instantiate(prefab, root.transform, false);
            // glTFast reflects source X: this ray faces local -X and its wings span Z.
            // Align the actual head to root +Z before measuring the full wing span.
            model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            // Keep the source art and texture; the guardian owns its material so ordinary rays never change.
            foreach (Collider collider in model.GetComponentsInChildren<Collider>()) { collider.enabled = false; Destroy(collider); }
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("Guardian has no visible surface.");
            Bounds combined = renderers[0].bounds;
            foreach (Renderer renderer in renderers) combined.Encapsulate(renderer.bounds);
            if (combined.size.x < .01f) throw new InvalidOperationException("Guardian wings have invalid dimensions.");
            model.transform.localScale *= WingSpan / combined.size.x;
            combined = renderers[0].bounds;
            foreach (Renderer renderer in renderers) combined.Encapsulate(renderer.bounds);
            model.transform.position += root.transform.position - combined.center;
            guardian.ActualWingSpan = combined.size.x;
            foreach (Renderer renderer in renderers)
            {
                var replacements = new Material[renderer.sharedMaterials.Length];
                Bounds local = renderer.localBounds;
                for (int i = 0; i < replacements.Length; i++)
                {
                    Material source = renderer.sharedMaterials[i];
                    var material = new Material(shader) { name = "Celestial ray · luminous water markings" };
                    if (source != null) foreach (string property in new[] { "baseColorTexture", "_BaseMap", "_MainTex" })
                    {
                        if (!source.HasProperty(property) || source.GetTexture(property) == null) continue;
                        material.SetTexture("_BaseMap", source.GetTexture(property)); break;
                    }
                    material.SetVector("_Centre", local.center);
                    material.SetVector("_HalfExtent", local.extents);
                    replacements[i] = material; guardian.materials.Add(material);
                }
                renderer.sharedMaterials = replacements;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                // GPU wing movement and tail extension remain inside CPU culling bounds.
                local.Expand(new Vector3(.48f, .25f, .06f)); renderer.localBounds = local;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) guardian.TriangleCount += (int)filter.sharedMesh.GetIndexCount(0) / 3;
            }
            // Derive the safety envelope from the shader-expanded real bounds after scaling.
            // Horizontal yaw does not affect altitude; include the maximum orbit bank at any yaw.
            float vertical = 0, horizontal = 0;
            foreach (Renderer renderer in renderers)
            {
                Bounds bounds = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = root.transform.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    vertical = Mathf.Max(vertical, Mathf.Abs(p.y));
                    horizontal = Mathf.Max(horizontal, new Vector2(p.x, p.z).magnitude);
                }
            }
            guardian.VerticalEnvelope = vertical + horizontal * Mathf.Sin(OceanGuardianComposition.MaximumBank * Mathf.Deg2Rad) + .1f;
            guardian.Place(0);
            Debug.Log($"[SkyGuardian] ready wingspan={guardian.ActualWingSpan:F1}m triangles={guardian.TriangleCount} interactive=False minBodyGap={MinimumBodyGap:F1}m envelope={guardian.VerticalEnvelope:F2}m entryAnchor={guardian.UsesEntryAnchor} forward={guardian.encounterForward} revealDistance={OceanGuardianComposition.ForwardDistance:F1}m");
            return guardian;
        }

        void Update()
        {
            if (owner == null || !owner.IsReady || view == null) return;
            elapsed += Mathf.Min(Time.deltaTime, .10f); Place(elapsed);
        }

        public void ResetEncounter(Vector3 eye, Vector3 forward)
        {
            Vector3 heading = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (heading.sqrMagnitude < .0001f) return;
            encounterEye = eye; encounterForward = heading.normalized;
            encounterRight = Vector3.Cross(Vector3.up, encounterForward);
            elapsed = 0; Place(0);
        }

        public void UseSanctuaryComposition()
        {
            compositionDistanceScale = .70f;
            foreach (Material material in materials) material.SetFloat("_SanctuaryTone", 1f);
            Place(elapsed);
        }

        void Place(double seconds)
        {
            // This is a distant sky layer. Only its minimum altitude follows an ascending observer;
            // no swimming ceiling, camera rotation coupling or reachable catch volume is introduced.
            var pose = OceanGuardianComposition.Evaluate(seconds, encounterEye.y, view.transform.position.y, VerticalEnvelope);
            if (compositionDistanceScale < 1f)
            {
                // A three-quarter crossing silhouette reveals the head, wing and tail
                // above the reef bridge instead of presenting a flat bright belly.
                double phase = OceanGuardianComposition.OrbitTime(seconds) * Math.PI * 2 / OrbitSeconds;
                float s = (float)Math.Sin(phase), c = (float)Math.Cos(phase);
                pose.right = -24f*s + 12f*(c-1f);
                pose.forward = OceanGuardianComposition.ForwardDistance + (12f*(c-1f)-8f*s)/compositionDistanceScale;
                pose.headingRight = -24f*c - 12f*s;
                pose.headingForward = (-12f*s-8f*c)/compositionDistanceScale;
            }
            Vector3 p = encounterEye + encounterRight * pose.right + encounterForward * (pose.forward * compositionDistanceScale);
            p.y = pose.height; transform.position = p;
            Vector3 tangent = encounterRight * pose.headingRight + encounterForward * (pose.headingForward * compositionDistanceScale);
            transform.rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, pose.bank);
            foreach (Material material in materials) material.SetFloat("_SwimTime", (float)(seconds % 10000));
        }
        void OnDestroy()
        {
            foreach (Material material in materials) if (material != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(material); else
#endif
                Destroy(material);
            }
            materials.Clear();
        }
#if UNITY_EDITOR
        public void PoseForValidation(double seconds) => Place(seconds);
        public void RestorePoseForValidation() => Place(elapsed);
#endif
    }
}

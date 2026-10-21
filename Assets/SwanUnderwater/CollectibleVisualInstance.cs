using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Owns one lease on collectible shared resources and a reusable glow property block.</summary>
    [DisallowMultipleComponent]
    public sealed class CollectibleVisualInstance : MonoBehaviour
    {
        MeshRenderer target;
        MaterialPropertyBlock properties;
        bool ownsLease;
        MeshFilter meshFilter;
        Mesh standardMesh, detailedMesh;
        Camera view;
        double nextDetailCheck;
        bool usingDetail;
        float lastGlow = -1f;

        internal void Initialize(MeshRenderer renderer, MeshFilter filter, Mesh detail)
        {
            target = renderer; ownsLease = true;
            meshFilter = filter; standardMesh = filter != null ? filter.sharedMesh : null; detailedMesh = detail;
            properties = new MaterialPropertyBlock();
            SetProximityGlow(0f);
        }

        void LateUpdate()
        {
            if (detailedMesh == null || Time.unscaledTimeAsDouble < nextDetailCheck) return;
            nextDetailCheck = Time.unscaledTimeAsDouble + .15;
            if (view == null) view = Camera.main;
            if (view != null) UpdateDetail(view.transform.position);
        }
        void UpdateDetail(Vector3 eye)
        {
            if (detailedMesh == null || target == null || meshFilter == null) return;
            float squared = target.bounds.SqrDistance(eye);
            bool use = usingDetail ? squared <= 3.0f * 3.0f : squared <= 2.5f * 2.5f;
            if (use == usingDetail) return;
            usingDetail = use; meshFilter.sharedMesh = use ? detailedMesh : standardMesh;
        }
#if UNITY_EDITOR
        public Mesh DetailMeshForValidation => detailedMesh;
        public bool UsesDetailForValidation => usingDetail;
        public void UpdateDetailForValidation(Vector3 eye) => UpdateDetail(eye);
#endif

        public void SetProximityGlow(float value)
        {
            if (target == null || float.IsNaN(value) || float.IsInfinity(value)) return;
            value = Mathf.Clamp01(value);
            if (Mathf.Abs(value - lastGlow) < .002f) return;
            lastGlow = value;
            properties.SetFloat(CollectibleVisuals.ProximityGlowId, value);
            target.SetPropertyBlock(properties);
        }

        void OnDestroy()
        {
            if (!ownsLease) return;
            ownsLease = false;
            CollectibleVisuals.ReleaseSharedResources();
        }
    }
}

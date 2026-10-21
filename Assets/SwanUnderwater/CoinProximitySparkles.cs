using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>One optional draw for the world coin only. Owns its mesh/material, never the coin's shared resources.</summary>
    [DisallowMultipleComponent]
    public sealed class CoinProximitySparkles : MonoBehaviour
    {
        const int GlintCount = 5;
        static readonly int NearId = Shader.PropertyToID("_Near");
        static readonly int ClockId = Shader.PropertyToID("_Clock");
        static readonly int RightId = Shader.PropertyToID("_ViewRight");
        static readonly int UpId = Shader.PropertyToID("_ViewUp");
        Mesh ownedMesh;
        Material ownedMaterial;
        MeshRenderer target;
        MaterialPropertyBlock properties;
        bool disposed;

        public static CoinProximitySparkles Create(Transform coin)
        {
            MeshFilter source = coin.GetComponent<MeshFilter>();
            Shader shader = Resources.Load<Shader>("Shaders/CoinProximitySparkles");
            if (shader == null || source == null || source.sharedMesh == null || !source.sharedMesh.isReadable)
            {
                Debug.LogWarning("[Treasure] Coin sparkle resources unavailable; world coin remains collectible.");
                return null;
            }
            var root = new GameObject("Nearby gold glints");
            root.layer = coin.gameObject.layer;
            root.transform.SetParent(coin, false);
            var effect = root.AddComponent<CoinProximitySparkles>();
            effect.ownedMesh = CreateMesh(source.sharedMesh);
            effect.ownedMaterial = new Material(shader) { name = "World coin nearby gold" };
            root.AddComponent<MeshFilter>().sharedMesh = effect.ownedMesh;
            effect.target = root.AddComponent<MeshRenderer>();
            effect.target.sharedMaterial = effect.ownedMaterial;
            effect.target.shadowCastingMode = ShadowCastingMode.Off;
            effect.target.receiveShadows = false;
            effect.target.lightProbeUsage = LightProbeUsage.Off;
            effect.target.reflectionProbeUsage = ReflectionProbeUsage.Off;
            effect.target.enabled = false;
            effect.properties = new MaterialPropertyBlock();
            return effect;
        }

        public void Refresh(Camera view, float proximity)
        {
            if (disposed || target == null) return;
            if (view == null || !isActiveAndEnabled || float.IsNaN(proximity) || float.IsInfinity(proximity) || proximity <= .003f)
            { Hide(); return; }
            properties.SetFloat(NearId, Mathf.Clamp01(proximity));
            properties.SetFloat(ClockId, Time.time);
            // Use one physical billboard orientation for both eyes, derived from the tracked centre camera.
            properties.SetVector(RightId, view.transform.right);
            properties.SetVector(UpId, view.transform.up);
            target.SetPropertyBlock(properties);
            target.enabled = true;
        }

        public void Hide() { if (target != null) target.enabled = false; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Hide();
            if (target != null) target.sharedMaterial = null;
            var filter = GetComponent<MeshFilter>();
            if (filter != null) filter.sharedMesh = null;
            DestroyOwned(ownedMesh); ownedMesh = null;
            DestroyOwned(ownedMaterial); ownedMaterial = null;
            DestroyOwned(gameObject);
        }

        void OnDisable() { Hide(); }
        void OnDestroy()
        {
            DestroyOwned(ownedMesh); ownedMesh = null;
            DestroyOwned(ownedMaterial); ownedMaterial = null;
        }

        static void DestroyOwned(Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }

        static Mesh CreateMesh(Mesh source)
        {
            Vector3[] surface = source.vertices, sourceNormals = source.normals;
            int[] surfaceIndices = source.triangles;
            int count = surface.Length;
            var vertices = new Vector3[count + GlintCount * 4];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var glint = new Vector2[vertices.Length];
            var indices = new int[surfaceIndices.Length + GlintCount * 6];
            System.Array.Copy(surface, vertices, count);
            System.Array.Copy(sourceNormals, normals, count);
            System.Array.Copy(surfaceIndices, indices, surfaceIndices.Length);
            // uv1.x=0 identifies the coin surface; glints carry a positive, stable phase seed.
            for (int i = 0; i < GlintCount; i++)
            {
                float angle = i * 2.399963f;
                float radius = source.bounds.extents.x * (.40f + .10f * (i % 3));
                Vector3 centre = new Vector3(Mathf.Cos(angle) * radius,
                    source.bounds.max.y + .012f + .009f * (i % 3), Mathf.Sin(angle) * radius);
                int first = count + i * 4;
                for (int j = 0; j < 4; j++)
                {
                    vertices[first + j] = centre;
                    normals[first + j] = Vector3.up;
                    uv[first + j] = new Vector2(j == 0 || j == 3 ? -1f : 1f, j < 2 ? -1f : 1f);
                    glint[first + j] = new Vector2(i + 1f, .013f + .003f * (i % 3));
                }
                int offset = surfaceIndices.Length + i * 6;
                indices[offset] = first; indices[offset + 1] = first + 1; indices[offset + 2] = first + 2;
                indices[offset + 3] = first; indices[offset + 4] = first + 2; indices[offset + 5] = first + 3;
            }
            var mesh = new Mesh { name = "Coin surface and five glints" };
            mesh.vertices = vertices; mesh.normals = normals;
            mesh.uv = uv; mesh.uv2 = glint; mesh.triangles = indices;
            mesh.RecalculateBounds();
            // World coin scale is 0.5; covers the 1.9 cm world-space billboard half-size in any view.
            Bounds bounds = mesh.bounds; bounds.Expand(.11f); mesh.bounds = bounds;
            mesh.UploadMeshData(!Application.isEditor);
            return mesh;
        }
    }
}

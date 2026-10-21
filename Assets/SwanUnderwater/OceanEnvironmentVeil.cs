using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>A short stereo camera-relative cover for map changes; it never moves the player's rig.</summary>
    public sealed class OceanEnvironmentVeil : MonoBehaviour
    {
        Material material;
        Mesh mesh;
        float opacity;

        public static OceanEnvironmentVeil Show(Camera camera)
        {
            if (camera == null) return null;
            Shader shader = Shader.Find("SwanUnderwater/EnvironmentVeil");
            if (shader == null) return null;
            var host = new GameObject("Ocean map transition"); host.transform.SetParent(camera.transform, false);
            host.transform.localPosition = new Vector3(0, 0, Mathf.Max(camera.nearClipPlane + .08f, .35f));
            var cover = host.AddComponent<OceanEnvironmentVeil>();
            cover.material = new Material(shader) { name = "Ocean map transition cover", renderQueue = 4990 };
            cover.mesh = new Mesh { name = "Stereo transition cover" };
            cover.mesh.vertices = new[] { new Vector3(-3,-3,0), new Vector3(3,-3,0), new Vector3(-3,3,0), new Vector3(3,3,0) };
            cover.mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            cover.mesh.triangles = new[] { 0,2,1,2,3,1 }; cover.mesh.RecalculateBounds();
            host.AddComponent<MeshFilter>().sharedMesh = cover.mesh;
            var renderer = host.AddComponent<MeshRenderer>(); renderer.sharedMaterial = cover.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            cover.material.SetFloat("_Opacity", 0);
            return cover;
        }

        public IEnumerator Cover()
        {
            while (opacity < 1f)
            {
                opacity = Mathf.MoveTowards(opacity, 1f, Time.unscaledDeltaTime / .22f);
                material.SetFloat("_Opacity", Mathf.SmoothStep(0,1,opacity)); yield return null;
            }
        }
        public IEnumerator Reveal()
        {
            while (opacity > 0f)
            {
                opacity = Mathf.MoveTowards(opacity, 0f, Time.unscaledDeltaTime / .45f);
                material.SetFloat("_Opacity", Mathf.SmoothStep(0,1,opacity)); yield return null;
            }
            Destroy(gameObject);
        }
        void OnDestroy() { if (material != null) Destroy(material); if (mesh != null) Destroy(mesh); }
    }
}

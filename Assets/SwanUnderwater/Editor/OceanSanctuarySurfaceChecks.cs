#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater.EditorTests
{
    public static class OceanSanctuarySurfaceChecks
    {
        sealed class SurfaceMesh
        {
            readonly Vector3[] vertices;
            readonly int[] triangles;
            public SurfaceMesh(Renderer renderer)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                vertices = mesh.vertices; triangles = mesh.triangles;
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = renderer.transform.TransformPoint(vertices[i]);
            }
            public bool TryTop(float x, float z, out float height)
            {
                height = float.NegativeInfinity;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    double determinant = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Math.Abs(determinant) < .00000001) continue;
                    double u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / determinant;
                    double v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / determinant;
                    double w = 1 - u - v;
                    if (u < -.00002 || v < -.00002 || w < -.00002) continue;
                    height = Mathf.Max(height, (float)(u * a.y + v * b.y + w * c.y));
                }
                return !float.IsNegativeInfinity(height);
            }
        }
        sealed class Garden
        {
            public SurfaceMesh support;
            public Vector3[] vertices;
        }
        static int assertions;
        static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Sanctuary surface: " + message);
        }

        public static int RunAll(UnderwaterWorld world)
        {
            assertions = 0;
            Check(world != null && world.IsReady && world.ActiveEnvironment == OceanEnvironmentKind.CoralSanctuary,
                "ready sanctuary required");
            var supports = new List<SurfaceMesh>();
            var bounds = new List<Bounds>();
            var gardens = new Dictionary<int, Garden>();
            int renderers = 0, triangles = 0;
            Material sharedGrowth = null;
            foreach (var renderer in world.ActiveReefRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.name.StartsWith("Attached coral mosaic ", StringComparison.Ordinal)) continue;
                var lod = renderer.GetComponentInParent<LODGroup>();
                Check(lod != null && lod.lodCount == 2, "growth shares its host's two real LODs");
                var levels = lod.GetLODs();
                Check(Array.IndexOf(levels[0].renderers, renderer) >= 0 &&
                    Array.IndexOf(levels[1].renderers, renderer) < 0, "fine colonies belong only to the detailed substrate");
                Check(renderer.shadowCastingMode == ShadowCastingMode.Off && renderer.sharedMaterials.Length == 1 &&
                    renderer.GetComponent<Collider>() == null, "small growth adds neither shadows nor obstacles");
                if (sharedGrowth == null)
                {
                    sharedGrowth = renderer.sharedMaterial;
                    Texture colour = sharedGrowth.GetTexture("_BaseMap"), normal = sharedGrowth.GetTexture("_BumpMap");
                    Check(colour != null && normal != null, "surface growth uses real existing coral colour and normal textures");
                    Debug.Log($"[SurfaceGrowthTextures] colour={colour.name} {colour.width}x{colour.height}, " +
                        $"normal={normal.name} {normal.width}x{normal.height}; imported resource dimensions, not a per-eye resolution claim.");
                }
                else Check(renderer.sharedMaterial == sharedGrowth, "all surface gardens reuse one material instance");
                var substrate = levels[0].renderers[0];
                Check(substrate != renderer, "real rock remains the supporting renderer");
                var support = new SurfaceMesh(substrate);
                supports.Add(support);
                bounds.Add(substrate.bounds);
                Mesh gardenMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Vector3[] gardenVertices = gardenMesh.vertices;
                for (int i = 0; i < gardenVertices.Length; i++)
                    gardenVertices[i] = renderer.transform.TransformPoint(gardenVertices[i]);
                int seed = int.Parse(renderer.name.Substring(renderer.name.LastIndexOf(' ') + 1));
                gardens.Add(seed, new Garden { support = support, vertices = gardenVertices });
                triangles += gardenMesh.triangles.Length / 3;
                renderers++;
            }
            Check(renderers > 40 && renderers <= 68 && renderers == OceanCoralSanctuary.SurfaceGardenRenderers,
                "surface gardens are combined per reef within their bounded budget");
            Check(triangles > 0 && triangles <= 240000 && triangles == OceanCoralSanctuary.SurfaceGardenTriangles,
                "actual generated geometry stays within the world-wide budget");
            var styles = new int[5];
            foreach (var colony in OceanCoralSanctuary.SurfaceColonies)
            {
                Check(colony.style >= 0 && colony.style < styles.Length, "known ecological growth form");
                styles[colony.style]++;
                Check(Vector3.Distance(colony.root, colony.surfacePoint - colony.normal * .025f) < .00001f,
                    "colony root is embedded in its supporting surface");
                float error = float.PositiveInfinity;
                for (int i = 0; i < supports.Count; i++)
                {
                    if (bounds[i].SqrDistance(colony.surfacePoint) > .001f) continue;
                    if (supports[i].TryTop(colony.surfacePoint.x, colony.surfacePoint.z, out float height))
                        error = Mathf.Min(error, Mathf.Abs(height - colony.surfacePoint.y));
                }
                Check(error < .026f, "anchor agrees with an actual rendered detailed rock surface");
                if (colony.style == 0)
                {
                    Check(gardens.TryGetValue(colony.hostSeed, out var garden), "encrusting colony retains its actual host");
                    Check(colony.firstVertex >= 0 && colony.vertexCount > 0 &&
                        colony.firstVertex + colony.vertexCount <= garden.vertices.Length, "nonempty colony geometry range");
                    for (int i = colony.firstVertex; i < colony.firstVertex + colony.vertexCount; i++)
                    {
                        Vector3 vertex = garden.vertices[i];
                        Check(garden.support.TryTop(vertex.x, vertex.z, out float top),
                            "all encrusting edges have real supporting rock, including across ledges");
                        Check(vertex.y - top > -.03f && vertex.y - top < .08f,
                            "encrusting edge stays close to its actual detailed substrate");
                    }
                }
            }
            Check(OceanCoralSanctuary.SurfaceColonies.Count > 600 && OceanCoralSanctuary.SurfaceColonies.Count <= 1008,
                "visible growth is distributed across reefs rather than a few isolated dots");
            foreach (int count in styles) Check(count > 80, "all five surface forms populate the scene");
            for (int i = 1; i <= 4; i++) Check(OceanCoralSanctuary.RockProfileCounts[i] >= 5,
                "retained procedural reef uses all four silhouettes with at least five banks each; natural walls are measured separately");
            Debug.Log($"OceanSanctuarySurfaceChecks PASS {assertions} assertions; " +
                $"{OceanCoralSanctuary.SurfaceColonies.Count} attached colonies, {renderers} combined detailed renderers, {triangles} triangles. " +
                "Actual geometry/LOD checks, not a visual-quality or headset-frame-rate verdict.");
            return assertions;
        }
    }
}
#endif

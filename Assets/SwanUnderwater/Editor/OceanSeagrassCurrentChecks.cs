#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual generated meadow/LOD/root checks; GPU time captures and headset fluidity are reviewed separately.</summary>
    public static class OceanSeagrassCurrentChecks
    {
        public static int Assertions { get; private set; }
        static void Check(bool condition, string message)
        {
            Assertions++;
            if (!condition) throw new InvalidOperationException("Sanctuary seagrass: " + message);
        }

        public static int RunAll(UnderwaterWorld world)
        {
            Assertions = 0;
            Check(world != null && world.IsReady && world.ActiveEnvironment == OceanEnvironmentKind.CoralSanctuary,
                "ready sanctuary required");
            var meadow = world.ActiveEnvironmentRoot.GetComponentInChildren<SeagrassMeadow>();
            Check(meadow != null && meadow.UsesSanctuaryCurrent, "actual sanctuary meadow uses the new current path");
            Check(meadow.TuftCount >= 1400 && meadow.TuftCount <= SeagrassMeadow.MaximumTufts,
                "visible planting fills the bounded 1536-tuft budget rather than reducing the existing 1395 tufts");
            Check(meadow.MeadowPatchCount >= 35 && meadow.ChunkCount <= 64, "multiple separated habitats within existing cell count");
            Check(meadow.GetComponentsInChildren<Collider>(true).Length == 0, "vegetation cannot block swimming or grabbing");
            int nearTriangles = 0, distantTriangles = 0, roots = 0, tallBlades = 0, shortBlades = 0;
            float minimumHeight = float.PositiveInfinity, maximumHeight = 0f;
            Material material = null;
            var groups = meadow.GetComponentsInChildren<LODGroup>(true);
            Check(groups.Length == meadow.ChunkCount && groups.Length > 0, "one native LOD group per populated cell");
            foreach (var group in groups)
            {
                var levels = group.GetLODs();
                Check(levels.Length == 2 && levels[0].renderers.Length == 1 && levels[1].renderers.Length == 1,
                    "each cell selects one renderer for each of its two real LODs");
                Check(group.fadeMode == LODFadeMode.None && group.size >= 20f,
                    "no extra transparent crossfade and no tiny-cell close-view switch");
                for (int level = 0; level < levels.Length; level++)
                {
                    var renderer = (MeshRenderer)levels[level].renderers[0];
                    Check(renderer != null && renderer.sharedMaterials.Length == 1,
                        "one material slot on each selected renderer");
                    if (material == null) material = renderer.sharedMaterial;
                    Check(renderer.sharedMaterial == material && material.passCount == 1 && material.renderQueue < 2500,
                        "the whole meadow shares one single-pass opaque material");
                    Check(renderer.shadowCastingMode == ShadowCastingMode.Off && renderer.receiveShadows,
                        "leaves receive the existing main-light shadow without adding shadow-map draws");
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    var vertices = mesh.vertices;
                    var uv = mesh.uv;
                    var motion = mesh.uv2;
                    var anchors = new List<Vector2>(); mesh.GetUVs(2, anchors);
                    Check(vertices.Length == uv.Length && vertices.Length == motion.Length && anchors.Count == vertices.Length,
                        "every actual vertex has root/height/phase animation data");
                    Check(mesh.vertexCount < 65536, "all batches fit the existing 16-bit mesh index path");
                    int triangleCount = mesh.triangles.Length / 3;
                    if (level == 0) nearTriangles += triangleCount; else distantTriangles += triangleCount;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 vertex = vertices[i];
                        Check(Finite(vertex.x) && Finite(vertex.y) && Finite(vertex.z) &&
                            Finite(anchors[i].x) && Finite(anchors[i].y), "finite anchored geometry");
                        Bounds motionBounds = mesh.bounds;
                        bool containsMotion = motionBounds.Contains(vertex + Vector3.right * SeagrassMeadow.MaximumCurrentDeflection) &&
                            motionBounds.Contains(vertex - Vector3.right * SeagrassMeadow.MaximumCurrentDeflection) &&
                            motionBounds.Contains(vertex + Vector3.forward * SeagrassMeadow.MaximumCurrentDeflection) &&
                            motionBounds.Contains(vertex - Vector3.forward * SeagrassMeadow.MaximumCurrentDeflection) &&
                            motionBounds.Contains(vertex - Vector3.up * SeagrassMeadow.MaximumCurrentVerticalDrop);
                        Check(containsMotion, containsMotion
                            ? "static culling bounds contain every vertex and the full conservative GPU displacement"
                            : $"static culling bounds omit motion: mesh={mesh.name}, level={level}, vertex={i} {vertex:F7}, " +
                                $"min={motionBounds.min:F7}, max={motionBounds.max:F7}, horizontal={SeagrassMeadow.MaximumCurrentDeflection:F7}, " +
                                $"drop={SeagrassMeadow.MaximumCurrentVerticalDrop:F7}");
                        if (uv[i].y < .00001f)
                        {
                            Vector3 p = renderer.transform.TransformPoint(vertex);
                            float ground = UnderwaterWorld.SampleRenderedSeabedHeight(p.x, p.z);
                            Check(p.y >= ground - .026f && p.y <= ground - .024f,
                                "both width edges of every blade root are buried in actual rendered seabed");
                            roots++;
                        }
                        if (level == 0 && uv[i].y > .9999f)
                        {
                            minimumHeight = Mathf.Min(minimumHeight, motion[i].x); maximumHeight = Mathf.Max(maximumHeight, motion[i].x);
                            if (motion[i].x >= .9f) tallBlades++;
                            if (motion[i].x <= .5f) shortBlades++;
                        }
                    }
                }
            }
            Check(material != null && material.shader.name == "SwanUnderwater/SanctuarySeagrass" &&
                material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader), "real target shader compiles and is supported");
            Check(nearTriangles == meadow.TriangleCount && nearTriangles <= SeagrassMeadow.MaximumTriangles,
                "actual near geometry stays under the previous maximum triangle budget");
            Check(distantTriangles == meadow.DistantTriangleCount && distantTriangles <= nearTriangles / 4,
                "distant blades retain roots and motion at under one quarter of the near triangles");
            Check(roots == meadow.TuftCount * 10, "three near and two far blades have two grounded root vertices each");
            Check(minimumHeight >= .269f && maximumHeight <= 1.351f && maximumHeight >= 1.1f &&
                tallBlades >= 200 && shortBlades >= 200, "low mats and tall ribbons are both visibly represented");
            int nearDiscoveries = 0;
            var origin = new Vector2(world.ActiveSpawn.x, world.ActiveSpawn.z);
            foreach (Vector2 root in meadow.RootPositions)
            {
                float distance = Vector2.Distance(root, origin);
                if (distance >= 3f && distance <= 19f) nearDiscoveries++;
                Check(DistanceToRoute(root - origin, OceanCoralSanctuary.ExplorationRouteOffsets) >= 1.049f &&
                    DistanceToRoute(root - origin, OceanCoralSanctuary.ExplorationShortcutOffsets) >= 1.049f,
                    "named sandy swim routes retain their root-free channel");
            }
            Check(nearDiscoveries >= 35, "near-spawn roots remain available to campaign grass discoveries");
            for (int zone = 1; zone < OceanCoralSanctuary.ExplorationZones.Count; zone++)
            {
                Vector2 home = origin + OceanCoralSanctuary.ExplorationZones[zone].offset;
                int count = 0;
                foreach (Vector2 root in meadow.RootPositions) if (Vector2.Distance(root, home) < 26f) count++;
                Check(count >= 80, "outer region " + zone + " retains a meaningful rooted stand");
            }
            Check(material.GetFloat("_ValidationTimeEnabled") == 0f, "Editor time override starts disabled");
            try
            {
                meadow.SetValidationTime(0f);
                Check(material.GetFloat("_ValidationTimeEnabled") == 1f && material.GetFloat("_ValidationTime") == 0f,
                    "local zero-time capture is available without mutating global shader time");
                meadow.SetValidationTime(2f);
                Check(material.GetFloat("_ValidationTime") == 2f, "second current phase capture uses the same real geometry");
            }
            finally { meadow.SetValidationTime(null); }
            Check(material.GetFloat("_ValidationTimeEnabled") == 0f, "validation always restores actual animation time");
            Debug.Log($"OceanSeagrassCurrentChecks PASS {Assertions} assertions; tufts={meadow.TuftCount}, " +
                $"patches={meadow.MeadowPatchCount}, nearTriangles={nearTriangles}, distantTriangles={distantTriangles}, " +
                $"cells={groups.Length}, heights={minimumHeight:F3}..{maximumHeight:F3}m, " +
                $"tallBlades={tallBlades}, shortBlades={shortBlades}, nearDiscoveryRoots={nearDiscoveries}; " +
                "actual scene geometry and animation setup only, not GPU frame time or headset acceptance.");
            return Assertions;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static float DistanceToRoute(Vector2 point, IReadOnlyList<Vector2> route)
        {
            float result = float.PositiveInfinity;
            for (int i = 0; i + 1 < route.Count; i++)
            {
                Vector2 segment = route[i + 1] - route[i];
                float t = Mathf.Clamp01(Vector2.Dot(point - route[i], segment) / Mathf.Max(.0001f, segment.sqrMagnitude));
                result = Mathf.Min(result, Vector2.Distance(point, route[i] + segment * t));
            }
            return result;
        }
    }
}
#endif

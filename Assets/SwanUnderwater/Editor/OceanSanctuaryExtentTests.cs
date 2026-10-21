#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual mesh/query checks for the expanded map. Run after the active world is ready.</summary>
    public static class OceanSanctuaryExtentTests
    {
        sealed class FloorMesh
        {
            public Vector3[] vertices;
            public int[] triangles;
        }

        static int assertions;
        static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("OceanSanctuaryExtentTests: " + message);
        }

        public static int RunAll(UnderwaterWorld world)
        {
            assertions = 0;
            Check(world != null && world.IsReady, "requires a ready world");
            BoundaryRules();
            var meshes = new List<FloorMesh>();
            foreach (var filter in world.ActiveEnvironmentRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || (mesh.name != "SeabedTerrain" && mesh.name != "NearSeabedTerrain" &&
                    mesh.name != "Sanctuary Medium Seabed Ring")) continue;
                Check(filter.transform.position.sqrMagnitude < .000001f &&
                    Quaternion.Angle(filter.transform.rotation, Quaternion.identity) < .001f &&
                    Vector3.Distance(filter.transform.lossyScale, Vector3.one) < .001f,
                    "terrain grid is fixed in world coordinates");
                meshes.Add(new FloorMesh { vertices = mesh.vertices, triangles = mesh.triangles });
            }
            bool sanctuary = world.ActiveEnvironment == OceanEnvironmentKind.CoralSanctuary;
            Check(meshes.Count == (sanctuary ? 3 : 2), "one near patch, optional medium ring and one far ring");
            foreach (float edge in sanctuary ? new[] { -128f, -40f, 40f, 128f } : new[] { -40f, 40f })
                foreach (float along in new[] { -127f, -100f, -40f, -25f, -12.5f, 0f, 12.5f, 40f, 99f, 127f })
                    foreach (float epsilon in new[] { -.001f, 0f, .001f })
                        foreach (bool swap in new[] { false, true })
                        {
                            float x = OceanEnvironmentProfile.SpawnX + (swap ? along : edge + epsilon);
                            float z = OceanEnvironmentProfile.SpawnZ + (swap ? edge + epsilon : along);
                            float height = MeshHeight(meshes, x, z);
                            Check(Mathf.Abs(height - UnderwaterWorld.SampleRenderedSeabedHeight(x, z)) < .0003f,
                                "default sampler matches actual floor triangles at seam " + x + "," + z);
                            Check(Mathf.Abs(height - UnderwaterWorld.SampleRenderedSeabedHeight(x, z, world.ActiveEnvironment)) < .0003f,
                                "explicit map sampler matches actual floor triangles");
                        }
            Debug.Log("OceanSanctuaryExtentTests PASS: " + assertions + " assertions; actual mesh coverage, fixed grids, seam continuity and tracked-eye boundary rules.");
            return assertions;
        }

        static void BoundaryRules()
        {
            foreach (float clearance in new[] { .125f, .245f, .6f })
                foreach (float sign in new[] { -1f, 1f })
                {
                    float safe = OceanEnvironmentProfile.SanctuaryPlayableHalfExtent - clearance;
                    Check(Mathf.Abs(OceanEnvironmentProfile.LimitSanctuaryOutwardStep(sign * 80f, sign * .1f, clearance) - sign * .1f) < .00001f,
                        "interior propulsion unchanged");
                    float near = OceanEnvironmentProfile.LimitSanctuaryOutwardStep(sign * 100f, sign * .1f, clearance);
                    float far = OceanEnvironmentProfile.LimitSanctuaryOutwardStep(sign * 108f, sign * .1f, clearance);
                    Check(sign * near > sign * far && sign * far > 0f, "outward travel slows smoothly towards edge");
                    Check(Mathf.Abs(OceanEnvironmentProfile.LimitSanctuaryOutwardStep(sign * 108f, -sign * .1f, clearance) + sign * .1f) < .00001f,
                        "inward travel unrestricted");
                    Check(OceanEnvironmentProfile.LimitSanctuaryOutwardStep(sign * safe, sign * .1f, clearance) == 0f,
                        "outward travel stops at capsule-safe edge");
                    foreach (float offset in new[] { -3.2f, 0f, 2.7f })
                    {
                        float origin = sign * 115f - offset, eye = origin + offset;
                        origin += OceanEnvironmentProfile.ClampSanctuaryEyeCoordinate(eye, clearance) - eye;
                        Check(Mathf.Abs(origin + offset - sign * safe) < .0001f, "tracked eye boundary independent of room-scale offset");
                    }
                }
        }

        static float MeshHeight(List<FloorMesh> meshes, float x, float z)
        {
            bool found = false;
            float height = float.NaN;
            foreach (var mesh in meshes)
                for (int i = 0; i < mesh.triangles.Length; i += 3)
                {
                    Vector3 a = mesh.vertices[mesh.triangles[i]], b = mesh.vertices[mesh.triangles[i + 1]], c = mesh.vertices[mesh.triangles[i + 2]];
                    if (x < Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - .00001f || x > Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + .00001f ||
                        z < Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - .00001f || z > Mathf.Max(a.z, Mathf.Max(b.z, c.z)) + .00001f) continue;
                    double determinant = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    double u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / determinant;
                    double v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / determinant;
                    double w = 1 - u - v;
                    if (u < -.00002 || v < -.00002 || w < -.00002) continue;
                    float candidate = (float)(u * a.y + v * b.y + w * c.y);
                    if (found) Check(Mathf.Abs(candidate - height) < .0003f, "shared edge is continuous across terrain pieces");
                    found = true; height = candidate;
                }
            Check(found, "rendered floor covers " + x + "," + z);
            return height;
        }
    }
}
#endif

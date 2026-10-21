using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>
    /// Fixed relief silhouettes beyond the sanctuary's traversable reef. These are real
    /// opaque surfaces, not camera-facing quads, so stereo parallax and turning remain
    /// stable. Their buried skirts meet the actual seabed; only the water haze is shaded.
    /// </summary>
    public static class OceanSanctuaryVista
    {
        public const float RecommendedFarClipPlane = 700f;
        public static int FormationCount { get; private set; }
        public static int TriangleCount { get; private set; }
        /// <summary>Measured from the spawn in the horizontal plane, over all mesh vertices.</summary>
        public static float ClosestHorizontalGeometryRadius { get; private set; }
        /// <summary>Minimum horizontal distance from the playable square to any mesh vertex.</summary>
        public static float MinimumPlayableClearance { get; private set; }

        const int Sectors = 72;
        const int Rings = 12;
        const float PlayableHalfExtent = 110f;

        readonly struct Formation
        {
            public readonly float x, z, width, depth, height, yaw;
            public readonly int layer;
            public Formation(float x, float z, float width, float depth, float height, float yaw, int layer)
            {
                this.x = x; this.z = z; this.width = width; this.depth = depth;
                this.height = height; this.yaw = yaw; this.layer = layer;
            }
        }

        // Deliberately staggered banks, gaps and overlapping shoulders. There is no
        // enclosing cylinder, repeated angular panel strip or visible concentric ring.
        // Width/depth are full footprint spans before the small eroded-edge variation.
        static readonly Formation[] Layout =
        {
            new Formation(-118, 214, 92, 47, 48, -11, 0),
            new Formation(  16, 250, 127, 43, 68, 7, 0),
            new Formation( 137, 225, 101, 48, 58, 18, 0),
            new Formation( 228, 116, 96, 44, 61, 74, 0),
            new Formation( 246, -12, 119, 46, 44, 85, 0),
            new Formation( 221,-139, 103, 48, 73, 112, 0),
            new Formation( 113,-230, 112, 45, 63, 172, 0),
            new Formation( -31,-250, 136, 42, 47, 183, 0),
            new Formation(-147,-220, 98, 48, 59, 164, 0),
            new Formation(-220,-120, 102, 45, 67, 82, 0),
            new Formation(-246,   8, 130, 43, 52, 91, 0),
            new Formation(-218, 144, 106, 47, 79, 114, 0),

            new Formation(-191, 334, 156, 68, 93, -17, 1),
            new Formation(  51, 365, 189, 71, 106, 13, 1),
            new Formation( 268, 288, 133, 62, 87, -24, 1),
            new Formation( 362,  94, 177, 68, 116, 69, 1),
            new Formation( 342,-153, 165, 70, 99, 109, 1),
            new Formation( 173,-353, 180, 64, 107, 171, 1),
            new Formation( -85,-361, 194, 72, 83, 184, 1),
            new Formation(-298,-273, 158, 65, 115, 144, 1),
            new Formation(-366, -21, 191, 69, 105, 94, 1),
            new Formation(-320, 231, 143, 65, 88, 59, 1),

            new Formation(-100, 420, 204, 83, 133, -9, 2),
            new Formation( 274, 342, 209, 80, 151, -33, 2),
            new Formation( 422,  -8, 208, 86, 121, 84, 2),
            new Formation( 244,-350, 201, 80, 149, 155, 2),
            new Formation(-144,-416, 211, 84, 139, 192, 2),
            new Formation(-388,-216, 198, 81, 158, 125, 2),
            new Formation(-405, 185, 205, 82, 141, 69, 2)
        };

        /// <summary>
        /// Call once while building a sanctuary root. The generated material and meshes
        /// belong to that root's OceanSanctuaryResources; nothing is allocated per frame.
        /// No colliders, lights, textures, shadow casters or scripts are created here.
        /// </summary>
        public static void Build(Transform parent, Func<float, float, float> floor,
            Vector3 spawn, OceanSanctuaryResources resources)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (floor == null) throw new ArgumentNullException(nameof(floor));
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            Shader shader = Resources.Load<Shader>("Shaders/OceanSanctuaryVista");
            if (shader == null) throw new InvalidOperationException("[Sanctuary] Missing distant relief shader");

            var root = new GameObject("Fixed distant underwater relief").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = Vector3.one;
            var materials = new Material[3];
            float[] visibility = { .46f, .34f, .25f };
            for (int i = 0; i < materials.Length; i++)
            {
                var material = new Material(shader)
                {
                    name = "Sanctuary blue relief tier " + (i + 1),
                    enableInstancing = true
                };
                material.SetFloat("_Visibility", visibility[i]);
                material.SetFloat("_Density", .0065f + i * .0007f);
                materials[i] = material;
                resources.Own(material);
            }

            FormationCount = 0;
            TriangleCount = 0;
            ClosestHorizontalGeometryRadius = float.PositiveInfinity;
            MinimumPlayableClearance = float.PositiveInfinity;
            for (int i = 0; i < Layout.Length; i++)
            {
                Mesh mesh = CreateMesh(Layout[i], i, floor, spawn);
                resources.Own(mesh);
                var go = new GameObject("Distant reef tier " + (Layout[i].layer + 1) + " formation " + i);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[Layout[i].layer];
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                go.isStatic = true;
                FormationCount++;
                TriangleCount += Sectors * (2 * Rings - 1);
            }
        }

        static Mesh CreateMesh(Formation formation, int index, Func<float, float, float> floor, Vector3 spawn)
        {
            var vertices = new List<Vector3>(1 + Sectors * Rings);
            var colours = new List<Color>(1 + Sectors * Rings);
            var heights = new List<Vector2>(1 + Sectors * Rings);
            var triangles = new List<int>(Sectors * (2 * Rings - 1) * 3);
            float seed = 31.17f + index * 7.131f;
            float angle = formation.yaw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);

            Add(0, 0);
            for (int ring = 1; ring <= Rings; ring++)
            {
                float radius = ring / (float)Rings;
                for (int sector = 0; sector < Sectors; sector++)
                    Add(radius, sector * (Mathf.PI * 2f / Sectors));
            }
            for (int sector = 0; sector < Sectors; sector++)
            {
                int next = (sector + 1) % Sectors;
                // Positive Y winding, verified independently by the offline mesh test.
                triangles.Add(0); triangles.Add(1 + next); triangles.Add(1 + sector);
                for (int ring = 0; ring < Rings - 1; ring++)
                {
                    int a = 1 + ring * Sectors + sector;
                    int b = 1 + ring * Sectors + next;
                    int c = a + Sectors;
                    int d = b + Sectors;
                    triangles.Add(a); triangles.Add(b); triangles.Add(d);
                    triangles.Add(a); triangles.Add(d); triangles.Add(c);
                }
            }
            var mesh = new Mesh { name = "Fixed submerged relief " + index };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, heights);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;

            void Add(float r, float azimuth)
            {
                float edge = 1f + .14f * Mathf.Sin(azimuth * 3f + seed)
                    + .06f * Mathf.Sin(azimuth * 7f - seed * .6f);
                float u = Mathf.Cos(azimuth) * r * edge;
                float v = Mathf.Sin(azimuth) * r * edge;
                float localX = u * formation.width * .5f;
                float localZ = v * formation.depth * .5f;
                float x = spawn.x + formation.x + localX * cos - localZ * sin;
                float z = spawn.z + formation.z + localX * sin + localZ * cos;
                // Broad eroded shoulders and an asymmetric crest replace cone-shaped
                // mountain primitives. Each section has its own non-repeating outline.
                float shoulder = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.43f, 1f, r));
                float crest = .75f + .16f * Mathf.Sin(u * 3.8f + seed)
                    + .09f * Mathf.Cos(u * 7.1f - v * 2.7f + seed * .73f);
                float erosion = .94f + .06f * Mathf.Sin(u * 19f + v * 13f + seed);
                float h = formation.height * shoulder * crest * erosion;
                // The skirt is below the rendered ground, not hovering over a flat
                // reference plane. This samples the same terrain function as the world.
                vertices.Add(new Vector3(x, floor(x, z) - 2.2f + h, z));
                float light = .84f + .16f * Mathf.Sin(u * 5.1f - v * 7.3f + seed);
                colours.Add(new Color(light, .5f + .5f * crest, .85f + .15f * shoulder, 1f));
                heights.Add(new Vector2(Mathf.Clamp01(h / formation.height), formation.layer));
                float dx = x - spawn.x, dz = z - spawn.z;
                ClosestHorizontalGeometryRadius = Mathf.Min(ClosestHorizontalGeometryRadius,
                    Mathf.Sqrt(dx * dx + dz * dz));
                float outsideX = Mathf.Max(0, Mathf.Abs(dx) - PlayableHalfExtent);
                float outsideZ = Mathf.Max(0, Mathf.Abs(dz) - PlayableHalfExtent);
                MinimumPlayableClearance = Mathf.Min(MinimumPlayableClearance,
                    Mathf.Sqrt(outsideX * outsideX + outsideZ * outsideZ));
            }
        }
    }
}

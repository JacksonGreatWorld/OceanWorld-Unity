using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>Original metre-scale expedition maps. Static colour batches, shared admitted shaders, no downloaded assets.</summary>
    public sealed class OceanCampaignBiomeGeometry : MonoBehaviour
    {
        public readonly List<Bounds> Solids = new List<Bounds>();
        public readonly List<ReefHabitat.CoralPocket> Pockets = new List<ReefHabitat.CoralPocket>();
        public readonly List<Vector3> Vents = new List<Vector3>();
        public readonly List<Vector3> GroundAnchors = new List<Vector3>();
        public OceanCampaignBiome Biome { get; private set; }
        public int TriangleCount { get; private set; }
        public int RenderBatchCount { get; private set; }
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly Dictionary<int, Geometry> groups = new Dictionary<int, Geometry>();
        Func<float, float, float> floor;
        Vector3 spawn;

        public static OceanCampaignBiomeGeometry Build(Transform parent, OceanCampaignBiome biome,
            Func<float, float, float> sampleFloor, Vector3 spawn)
        {
            if (sampleFloor == null || parent == null || biome == OceanCampaignBiome.Reef) throw new ArgumentException("An expedition biome and terrain sampler are required.");
            var root = new GameObject("Independent map · " + biome); root.transform.SetParent(parent, false);
            var map = root.AddComponent<OceanCampaignBiomeGeometry>(); map.Biome = biome; map.floor = sampleFloor; map.spawn = spawn;
            try
            {
                switch (biome)
                {
                    case OceanCampaignBiome.Shipwreck: map.Shipwreck(); break;
                    case OceanCampaignBiome.KelpForest: map.KelpForest(); break;
                    case OceanCampaignBiome.DeepRuins: map.DeepRuins(); break;
                    case OceanCampaignBiome.CurrentChannel: map.CurrentChannel(); break;
                    case OceanCampaignBiome.Abyss: map.Abyss(); break;
                    default: throw new ArgumentOutOfRangeException(nameof(biome));
                }
                map.CreateHabitats(); map.Seal();
                Debug.Log($"[OceanBiome] built={biome} batches={map.RenderBatchCount} triangles={map.TriangleCount} solids={map.Solids.Count} central_lane_width=3m min_forward_clearance=8m");
                return map;
            }
            catch { if (Application.isPlaying) Destroy(root); else DestroyImmediate(root); throw; }
        }

        Geometry Group(int slot)
        { if (!groups.TryGetValue(slot, out Geometry mesh)) groups.Add(slot, mesh = new Geometry()); return mesh; }
        Vector3 Ground(float x, float z, float bury = .08f)
        {
            Vector3 p = spawn + new Vector3(x, 0, z); p.y = floor(p.x, p.z) - bury;
            GroundAnchors.Add(p); return p;
        }
        void Column(int slot, float x, float z, float width, float depth, float height, float yaw = 0f)
        {
            Vector3 bottom = Ground(x, z, .12f);
            // Embed the complete footprint, including on the existing sanctuary's sloped sand.
            float c = Mathf.Cos(yaw * Mathf.Deg2Rad), s = Mathf.Sin(yaw * Mathf.Deg2Rad);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
            {
                float px = spawn.x + x + sx * width * .5f * c + sz * depth * .5f * s;
                float pz = spawn.z + z - sx * width * .5f * s + sz * depth * .5f * c;
                bottom.y = Mathf.Min(bottom.y, floor(px, pz) - .06f);
            }
            Box(slot, bottom + Vector3.up * height * .5f, new Vector3(width, height, depth), Quaternion.Euler(0, yaw, 0), true);
        }
        void Box(int slot, Vector3 center, Vector3 size, Quaternion rotation, bool solid = false)
        {
            Group(slot).Box(center, size, rotation);
            if (!solid) return;
            var body = new GameObject("Map structural collision"); body.transform.SetParent(transform, false);
            body.transform.SetPositionAndRotation(center, rotation);
            var collider = body.AddComponent<BoxCollider>(); collider.size = size;
            // Compute exact transformed box AABB without depending on the first PhysX/render update.
            Bounds bounds = new Bounds(center, Vector3.zero);
            for (int i = 0; i < 8; i++) bounds.Encapsulate(center + rotation * Vector3.Scale(size * .5f,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
            Solids.Add(bounds);
        }

        void Shipwreck()
        {
            // A split ribbed wooden hull frames the mission lane; open ends and port-side aisles remain traversable.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int rib = 0; rib < 10; rib++)
                {
                    float z = .4f + rib * 1.35f;
                    float x = side * (4.3f - .42f * Mathf.Cos(rib * .58f));
                    Column(0, x, z, .24f, .27f, 2.1f + .4f * Mathf.Sin(rib * .7f), side * 7f);
                    Vector3 basePoint = Ground(x + side * .35f, z, .12f);
                    Box(0, basePoint + Vector3.up * .3f, new Vector3(1.9f, .22f, 1.28f), Quaternion.Euler(0, side * 7f, side * 11f), true);
                    for (int plank = 0; plank < 3; plank++)
                        Box(0, basePoint + new Vector3(side * .42f, .55f + plank * .34f, 0), new Vector3(.14f, .24f, 1.4f), Quaternion.Euler(0, side * 7, side * 11));
                    if (rib % 3 == 0) Column(1, x + side * .15f, z, .38f, .40f, .35f, rib * 12);
                }
                Column(0, side * 4.6f, 14.4f, .35f, .35f, 3.8f, side * 14f);
            }
            Vector3 mast = Ground(-5.7f, 8.8f); Group(0).Tube(mast, mast + new Vector3(.6f, 5.6f, .2f), .13f, .085f, 10);
            Group(0).Tube(mast + new Vector3(-1.2f, 4.2f, 0), mast + new Vector3(2.0f, 4.1f, .15f), .07f, .06f, 8);
            for (int i = 0; i < 4; i++) Column(0, 6.2f + i % 2 * 1.1f, 6.5f + i / 2 * 1.2f, .85f, .85f, .7f, 12 + i * 17);
            for (int i = 0; i < 8; i++) Kelp(-7.5f + i * .4f, 10.5f + i % 3, 1.0f + i * .06f, i + 22);
        }

        void KelpForest()
        {
            // Curved rooted fronds form alternating groves and branching aisles rather than a wall of flat cards.
            for (int side = -1; side <= 1; side += 2)
            for (int row = 0; row < 8; row++)
            for (int band = 0; band < 3; band++)
            {
                float x = side * (3.2f + band * 2.35f + .55f * Mathf.Sin(row * 1.4f + band));
                float z = -2f + row * 2.5f + (band % 2) * .7f;
                Kelp(x, z, 2.3f + .65f * Mathf.Sin(row * .8f + band) + band * .45f, row * 13 + band * 3 + (side + 1) * 29);
                if (band == 2 && row % 2 == 0) Column(4, x, z, 1.2f, 1.0f, .55f, row * 31);
            }
        }

        void Kelp(float x, float z, float height, int seed)
        {
            Vector3 p = Ground(x, z, .06f);
            Vector3 top = p + new Vector3(.26f * Mathf.Sin(seed), height, .23f * Mathf.Cos(seed));
            Group(2).Tube(p, top, .045f, .018f, 6, true);
            for (int leaf = 0; leaf < 7; leaf++)
            {
                float t = .18f + leaf * .105f;
                Vector3 at = Vector3.Lerp(p, top, t);
                float angle = seed * .7f + leaf * 2.4f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), .33f + .12f * Mathf.Sin(seed + leaf), Mathf.Sin(angle));
                Group(leaf % 2 == 0 ? 2 : 3).Leaf(at, direction.normalized, .7f + height * .17f, .12f + .02f * (leaf % 3), seed + leaf);
            }
        }

        void DeepRuins()
        {
            // Two processional colonnades and broken lintels make a navigable temple, with a broad sand aisle.
            for (int side = -1; side <= 1; side += 2)
            for (int row = 0; row < 5; row++)
            {
                float x = side * 4.5f, z = row * 3.3f + .6f;
                float height = row == 2 && side == 1 ? 2.2f : 4.3f + .25f * Mathf.Sin(row);
                Vector3 basePoint = Ground(x, z, .12f);
                Column(4, x, z, 1.45f, 1.45f, .32f, row * 7);
                Group(4).Tube(basePoint + Vector3.up * .3f, basePoint + Vector3.up * height, .48f, .39f, 10);
                Box(4, basePoint + Vector3.up * (height + .1f), new Vector3(1.25f, .3f, 1.25f), Quaternion.Euler(0, row * 7, 0));
                // A matching simple column volume follows the visual geometry and is included in spawn/reach checks.
                var colliderRoot = new GameObject("Ruins column collision"); colliderRoot.transform.SetParent(transform, false);
                colliderRoot.transform.position = basePoint + Vector3.up * (height * .5f);
                var body = colliderRoot.AddComponent<BoxCollider>(); body.size = new Vector3(.94f, height, .94f);
                Solids.Add(new Bounds(colliderRoot.transform.position, body.size));
                for (int rune = 0; rune < 3; rune++)
                    Box(5, basePoint + new Vector3(-side * .487f, .85f + rune * .55f, 0), new Vector3(.025f, .18f, .24f), Quaternion.Euler(0, 0, 45));
            }
            // The far gate sits beyond the practice/race lane and leaves 3m of physical head clearance.
            float leftFloor = floor(spawn.x - 4.5f, spawn.z + 14.2f), rightFloor = floor(spawn.x + 4.5f, spawn.z + 14.2f);
            Box(4, spawn + new Vector3(0, Mathf.Max(leftFloor, rightFloor) + 4.8f - spawn.y, 14.2f), new Vector3(10.4f, .65f, 1.25f), Quaternion.identity, true);
            Column(4, -7.6f, 8, 1.6f, 3.6f, .85f, -21);
            Column(4, 7.6f, 11.5f, 1.9f, 2.7f, 1.15f, 33);
        }

        void CurrentChannel()
        {
            for (int side = -1; side <= 1; side += 2)
            for (int row = 0; row < 9; row++)
            {
                float x = side * (4.5f + .65f * Mathf.Sin(row * .65f)), z = row * 2.2f - 2;
                Column(4, x, z, 2.2f, 2.5f, 1.1f + .35f * Mathf.Sin(row), side * 14);
                Vector3 p = Ground(x - side * .6f, z, 0);
                Group(5).Tube(p + Vector3.up * .4f, p + new Vector3(0, .6f, 1.15f), .035f, .018f, 6);
                if (row % 2 == 0) Kelp(x + side * .7f, z, 1.5f, row + side * 7);
            }
        }

        void Abyss()
        {
            for (int side = -1; side <= 1; side += 2)
            for (int rock = 0; rock < 8; rock++)
            {
                float x = side * (4.6f + (rock % 3) * .75f), z = rock * 2.3f - 2;
                Column(4, x, z, 1.5f, 1.8f, 2.4f + (rock % 3) * .55f, rock * 31);
                Vector3 p = Ground(x - side * .7f, z, 0);
                for (int bud = 0; bud < 3; bud++) Group(5).Tube(p + Vector3.up * .2f, p + new Vector3(bud * .10f - .1f, .7f + bud * .17f, .12f), .045f, .07f, 6);
            }
        }

        void CreateHabitats()
        {
            // No pocket intersects the central mission lane. Fish and collectible placement still use real floor/solids.
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI * 2f / 16f;
                float radius = i % 2 == 0 ? 12f : 18f;
                Vector3 p = spawn + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius + 2);
                p.y = floor(p.x, p.z);
                Pockets.Add(new ReefHabitat.CoralPocket { center = p, shelter = p + new Vector3(.35f, .15f, .35f), radius = 1.25f });
                if (i % 4 == 0) Vents.Add(p + Vector3.up * .05f);
            }
        }

        void Seal()
        {
            Shader shader = Resources.Load<Shader>("Shaders/OceanSanctuaryOrganic");
            if (shader == null) throw new InvalidOperationException("Existing admitted OceanSanctuaryOrganic shader is missing.");
            Color[] palette = { new Color(.47f,.31f,.18f), new Color(.24f,.33f,.34f), new Color(.16f,.34f,.16f), new Color(.39f,.56f,.19f), new Color(.43f,.51f,.53f), new Color(.30f,.85f,.79f) };
            foreach (var group in groups)
            {
                Mesh mesh = group.Value.Finish("Original " + Biome + " batch " + group.Key);
                TriangleCount += mesh.triangles.Length / 3; owned.Add(mesh);
                var material = new Material(shader) { name = "Original " + Biome + " surface " + group.Key, enableInstancing = true };
                material.SetColor("_BaseColor", palette[group.Key]); material.SetFloat("_BumpScale", group.Key == 4 ? .7f : .12f);
                if (group.Key == 4)
                {
                    material.SetTexture("_BaseMap", Resources.Load<Texture2D>("Textures/Rock/Rock019_Color"));
                    material.SetTexture("_BumpMap", Resources.Load<Texture2D>("Textures/Rock/Rock019_NormalGL"));
                    material.SetFloat("_WorldMapping", 1f); material.SetFloat("_WorldScale", .85f);
                }
                if (group.Key == 2 || group.Key == 3) material.SetFloat("_Sway", .035f);
                owned.Add(material);
                // Existing expedition surface admission expects a placement group with its model child.
                // Preserve that contract so hidden coins use the real mesh, never a map-wide bounding box.
                var placement = new GameObject("Biome geometry group " + group.Key); placement.transform.SetParent(transform, false);
                var batch = new GameObject("Static biome surface " + group.Key); batch.transform.SetParent(placement.transform, false);
                batch.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = batch.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                RenderBatchCount++;
            }
            groups.Clear();
        }

        void OnDestroy()
        { foreach (var asset in owned) if (asset != null) { if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset); } owned.Clear(); }

        sealed class Geometry
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> triangles = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float anchor = 0f, float shade = 1f)
            {
                int n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.one); uv.Add(Vector2.up);
                for (int i = 0; i < 4; i++) colors.Add(new Color(shade,shade,shade,anchor));
                triangles.Add(n); triangles.Add(n+1); triangles.Add(n+2); triangles.Add(n); triangles.Add(n+2); triangles.Add(n+3);
            }
            public void Box(Vector3 center, Vector3 size, Quaternion rotation)
            {
                var p = new Vector3[8]; for (int i = 0; i < 8; i++) p[i] = center + rotation * Vector3.Scale(size * .5f,
                    new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                Quad(p[0],p[2],p[3],p[1],0,.86f); Quad(p[4],p[5],p[7],p[6],0,.95f);
                Quad(p[0],p[4],p[6],p[2],0,.80f); Quad(p[1],p[3],p[7],p[5],0,.90f);
                Quad(p[0],p[1],p[5],p[4],0,.74f); Quad(p[2],p[6],p[7],p[3],0,1f);
            }
            public void Tube(Vector3 from, Vector3 to, float radius0, float radius1, int sides, bool sway = false)
            {
                Vector3 axis = (to-from).normalized;
                Vector3 right = Vector3.Cross(axis,Mathf.Abs(axis.y)>.9f?Vector3.forward:Vector3.up).normalized;
                Vector3 up = Vector3.Cross(axis,right);
                for (int i = 0; i < sides; i++)
                {
                    float a = i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
                    Vector3 p = right*Mathf.Cos(a)+up*Mathf.Sin(a),q=right*Mathf.Cos(b)+up*Mathf.Sin(b);
                    Quad(from+p*radius0,from+q*radius0,to+q*radius1,to+p*radius1,sway?1f:0f,.85f+.15f*(i%2));
                }
            }
            public void Leaf(Vector3 root, Vector3 direction, float length, float width, int seed)
            {
                Vector3 side = Vector3.Cross(direction, Vector3.up).normalized;
                if(side.sqrMagnitude<.1f)side=Vector3.right;
                for(int i=0;i<6;i++)
                {
                    float a=i/6f,b=(i+1)/6f;
                    Vector3 p=root+direction*(a*length)+Vector3.up*(Mathf.Sin(a*Mathf.PI)*.22f)+side*(Mathf.Sin(a*5+seed)*.04f);
                    Vector3 q=root+direction*(b*length)+Vector3.up*(Mathf.Sin(b*Mathf.PI)*.22f)+side*(Mathf.Sin(b*5+seed)*.04f);
                    float wa=Mathf.Sin(a*Mathf.PI)*width,wb=Mathf.Sin(b*Mathf.PI)*width;
                    Quad(p-side*wa,q-side*wb,q+side*wb,p+side*wa,b,.88f+.12f*b);
                }
            }
            public Mesh Finish(string name)
            {
                var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetColors(colors); mesh.SetTriangles(triangles,0);
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
            }
        }
    }
}

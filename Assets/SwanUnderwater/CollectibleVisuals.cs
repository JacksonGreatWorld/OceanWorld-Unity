using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>Shared, metre-scale collectible geometry. +Y is up and every pivot is at the bottom.
    /// Shell kinds: 0 scallop, 1 conch, 2 cowrie, 3 clam, 4 turret, 5 cockle, 6 oyster, 7 limpet, 8 razor clam, 9 murex.
    /// No physics or placement ownership; internal coin mesh slots remain 5 and 6.</summary>
    public static class CollectibleVisuals
    {
        public static readonly string[] ShellNames = { "扇贝", "海螺", "宝螺", "蛤蜊", "锥螺", "蚶贝", "牡蛎", "帽贝", "蛏子", "骨螺" };
        public const float CoinDiameter = .22f;
        public static readonly int ProximityGlowId = Shader.PropertyToID("_ProximityGlow");
        static Mesh[] meshes;
        static Mesh[] detailedShells;
        static Material material;
        static int leases;

        public static GameObject CreateShell(Transform parent, int kind)
        {
            if (kind < 0 || kind >= ShellNames.Length) throw new ArgumentOutOfRangeException(nameof(kind));
            // Internal coin slots 5/6 stay stable for both world and reward factories.
            return Create(parent, kind < 5 ? kind : kind + 2, ShellNames[kind]);
        }

        public static GameObject CreateCoin(Transform parent) => Create(parent, 5, "海洋金币");

        public static void SetProximityGlow(GameObject visual, float amount)
        {
            if (visual != null && visual.TryGetComponent(out CollectibleVisualInstance instance))
                instance.SetProximityGlow(amount);
        }

        /// <summary>Acquire once per batch-rendering owner, then ReleaseSharedResources once on disposal.
        /// One submesh, face +Y, bottom y=0, diameter .22m. Do not destroy the borrowed resources.</summary>
        public static void AcquireCoinResources(out Mesh mesh, out Material sharedMaterial)
        {
            EnsureResources(); leases++;
            mesh = meshes[6]; sharedMaterial = material;
        }

        public static void ReleaseSharedResources()
        {
            if (leases <= 0) return;
            if (--leases != 0) return;
            foreach (Mesh mesh in meshes) DestroyOwned(mesh);
            if (detailedShells != null) foreach (Mesh mesh in detailedShells) DestroyOwned(mesh);
            detailedShells = null;
            DestroyOwned(material); meshes = null; material = null;
        }

        static GameObject Create(Transform parent, int kind, string name)
        {
            EnsureResources();
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.AddComponent<MeshFilter>().sharedMesh = meshes[kind];
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            leases++;
            int shellKind = kind < 5 ? kind : kind >= 7 ? kind - 2 : -1;
            root.AddComponent<CollectibleVisualInstance>().Initialize(renderer,
                root.GetComponent<MeshFilter>(), shellKind >= 0 ? detailedShells[shellKind] : null);
            return root;
        }

        static void EnsureResources()
        {
            if (meshes != null && material != null) return;
            Shader shader = Resources.Load<Shader>("Shaders/CollectibleSurface");
            if (shader == null) throw new InvalidOperationException("Missing Shaders/CollectibleSurface.");
            var created = new Mesh[12];
            try
            {
                created[0] = Scallop().Finish("Collectible Scallop", .24f);
                created[1] = Spiral(false).Finish("Collectible Conch", .25f);
                created[2] = Cowrie().Finish("Collectible Cowrie", .22f);
                created[3] = Clam().Finish("Collectible Clam", .22f);
                created[4] = Spiral(true).Finish("Collectible Turret Shell", .26f);
                created[5] = Coin(true).Finish("Ocean Coin", CoinDiameter);
                created[6] = Coin(false).Finish("Ocean Coin Particle", CoinDiameter);
                created[7] = RibbedValve(false).Finish("Collectible Cockle", .22f);
                created[8] = RibbedValve(true).Finish("Collectible Oyster", .25f);
                created[9] = Limpet().Finish("Collectible Limpet", .21f);
                created[10] = RazorClam().Finish("Collectible Razor Clam", .26f);
                created[11] = Murex().Finish("Collectible Murex", .26f);
                material = new Material(shader) { name = "Shared shell nacre and ocean gold", enableInstancing = true };
                detailedShells = new Mesh[10];
                detailedShells[0] = Scallop(true).Finish("Scallop near detail", .24f);
                detailedShells[1] = Spiral(false,true).Finish("Conch near detail", .25f);
                detailedShells[2] = Cowrie(true).Finish("Cowrie near detail", .22f);
                detailedShells[3] = Clam(true).Finish("Clam near detail", .22f);
                detailedShells[4] = Spiral(true,true).Finish("Turret near detail", .26f);
                detailedShells[5] = RibbedValve(false,true).Finish("Cockle near detail", .22f);
                detailedShells[6] = RibbedValve(true,true).Finish("Oyster near detail", .25f);
                detailedShells[7] = Limpet(true).Finish("Limpet near detail", .21f);
                detailedShells[8] = RazorClam(true).Finish("Razor clam near detail", .26f);
                detailedShells[9] = Murex(true).Finish("Murex near detail", .26f);
                meshes = created;
            }
            catch
            {
                foreach (Mesh mesh in created) DestroyOwned(mesh);
                if (detailedShells != null) foreach (Mesh mesh in detailedShells) DestroyOwned(mesh);
                detailedShells = null;
                DestroyOwned(material); material = null;
                throw;
            }
        }

        static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        sealed class Geometry
        {
            public readonly List<Vector3> p = new List<Vector3>();
            readonly List<Color> colors = new List<Color>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<Vector2> kind = new List<Vector2>();
            readonly List<int> triangles = new List<int>();
            public int Vertex(Vector3 point, Color color, Vector2 tex, int type, float detail = 0f)
            {
                int index = p.Count; p.Add(point); colors.Add(color); uv.Add(tex); kind.Add(new Vector2(type, detail)); return index;
            }
            public void Face(int a, int b, int c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(p[b] - p[a], p[c] - p[a]), outward) < 0f) { int swap = b; b = c; c = swap; }
                if (Vector3.Cross(p[b] - p[a], p[c] - p[a]).sqrMagnitude < 1e-18f) return;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }
            public void Quad(int a, int b, int c, int d, Vector3 outward)
            { Face(a, b, c, outward); Face(a, c, d, outward); }
            public Mesh Finish(string name, float length)
            {
                Bounds bounds = new Bounds(p[0], Vector3.zero);
                foreach (Vector3 point in p) bounds.Encapsulate(point);
                float scale = length / Mathf.Max(bounds.size.x, bounds.size.z);
                Vector3 centre = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                for (int i = 0; i < p.Count; i++) p[i] = (p[i] - centre) * scale;
                var mesh = new Mesh { name = name };
                mesh.SetVertices(p); mesh.SetColors(colors); mesh.SetUVs(0, uv); mesh.SetUVs(1, kind);
                mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                // Small shared meshes stay readable for identical contact fitting on device and Editor.
                mesh.UploadMeshData(false); return mesh;
            }
        }

        static Color ShellColor(float r, float g, float b) => new Color(r, g, b, .025f);
        static readonly Color Ivory = ShellColor(.78f, .67f, .52f);
        static readonly Color Pink = ShellColor(.70f, .42f, .34f);
        static readonly Color Gold = new Color(.83f, .54f, .13f, .90f);
        static readonly Color BrightGold = new Color(.98f, .74f, .29f, .92f);
        static readonly Color RecessGold = new Color(.42f, .24f, .06f, .86f);

        static Geometry Scallop(bool detail = false)
        {
            var g = new Geometry(); int rings = detail ? 24 : 12, sides = detail ? 96 : 72;
            int[][] rows = new int[2][];
            for (int side = 0; side < 2; side++)
            {
                rows[side] = new int[(rings + 1) * (sides + 1)];
                for (int i = 0; i <= rings; i++) for (int j = 0; j <= sides; j++)
                {
                    float r = i / (float)rings, u = j / (float)sides, angle = Mathf.Lerp(-1.34f, 1.34f, u);
                    float rib = Mathf.Pow(.5f + .5f * Mathf.Cos(u * 18f * Mathf.PI * 2f), 2f);
                    float outline = 1f + .025f * rib * r;
                    float dome = Mathf.Sin(r * Mathf.PI);
                    float y = side == 0 ? .004f + (.051f + .009f * rib) * dome : -.009f * dome;
                    Vector3 p = new Vector3(Mathf.Sin(angle) * .122f * r * outline,
                        y, Mathf.Cos(angle) * .205f * r * outline - .081f);
                    Color color = Color.Lerp(Pink, Ivory, .2f + .55f * r);
                    if (side == 1) color = ShellColor(.71f, .63f, .56f);
                    rows[side][i * (sides + 1) + j] = g.Vertex(p, color, new Vector2(u, r), 0, side);
                }
                for (int i = 0; i < rings; i++) for (int j = 0; j < sides; j++)
                {
                    int a = i * (sides + 1) + j, b = a + sides + 1;
                    g.Quad(rows[side][a], rows[side][a + 1], rows[side][b + 1], rows[side][b], side == 0 ? Vector3.up : Vector3.down);
                }
            }
            for (int j = 0; j < sides; j++)
            {
                int a = rings * (sides + 1) + j;
                g.Quad(rows[0][a], rows[1][a], rows[1][a + 1], rows[0][a + 1], g.p[rows[0][a]] - new Vector3(0, 0, -.081f));
            }
            for (int i = 0; i < rings; i++) foreach (int j in new[] { 0, sides })
            {
                int a = i * (sides + 1) + j, b = a + sides + 1;
                g.Quad(rows[0][a], rows[0][b], rows[1][b], rows[1][a], j == 0 ? Vector3.left : Vector3.right);
            }
            // The short hinge ears distinguish the fan from a round valve even at a distance.
            Ellipsoid(g, new Vector3(-.020f, .002f, -.074f), new Vector3(.023f, .007f, .015f), Ivory, 0, 16, 6);
            Ellipsoid(g, new Vector3(.020f, .002f, -.074f), new Vector3(.023f, .007f, .015f), Ivory, 0, 16, 6);
            return g;
        }

        static Geometry Clam(bool detail = false)
        {
            var g = new Geometry(); int sides = detail ? 96 : 64, rings = detail ? 24 : 14;
            int[][] rows = new int[2][];
            for (int side = 0; side < 2; side++)
            {
                rows[side] = new int[(rings + 1) * (sides + 1)];
                for (int i = 0; i <= rings; i++) for (int j = 0; j <= sides; j++)
                {
                    float r = i / (float)rings, u = j / (float)sides, angle = u * Mathf.PI * 2f;
                    float uneven = 1f + .015f * Mathf.Cos(angle * 5f);
                    float dome = Mathf.Sqrt(Mathf.Max(0f, 1f - r * r));
                    float y = side == 0 ? .002f + .050f * dome : -.002f - .029f * dome;
                    y += (side == 0 ? 1f : -1f) * .0013f * Mathf.Sin(r * 16f * Mathf.PI) * Mathf.Sin(r * Mathf.PI);
                    Vector3 p = new Vector3(Mathf.Sin(angle) * .10f * r * uneven, y, Mathf.Cos(angle) * .115f * r);
                    Color c = Color.Lerp(ShellColor(.54f, .51f, .43f), ShellColor(.83f, .78f, .62f), dome * .65f + .2f);
                    rows[side][i * (sides + 1) + j] = g.Vertex(p, c, new Vector2(u, r), 3, side);
                }
                for (int i = 0; i < rings; i++) for (int j = 0; j < sides; j++)
                {
                    int a = i * (sides + 1) + j, b = a + sides + 1;
                    g.Quad(rows[side][a], rows[side][a + 1], rows[side][b + 1], rows[side][b], side == 0 ? Vector3.up : Vector3.down);
                }
            }
            for (int j = 0; j < sides; j++)
            {
                int a = rings * (sides + 1) + j;
                g.Quad(rows[0][a], rows[1][a], rows[1][a + 1], rows[0][a + 1], g.p[rows[0][a]]);
            }
            return g;
        }

        static Geometry Cowrie(bool detail = false)
        {
            var g = new Geometry(); int rows = detail ? 48 : 32, sides = detail ? 64 : 48;
            var ids = new int[rows + 1, sides + 1];
            Color amber = ShellColor(.63f, .40f, .19f);
            for (int i = 0; i <= rows; i++) for (int j = 0; j <= sides; j++)
            {
                float v = i / (float)rows, u = j / (float)sides, z = (v - .5f) * .24f;
                float section = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(v * Mathf.PI)), .68f);
                float angle = Mathf.Lerp(-Mathf.PI + .16f, Mathf.PI - .16f, u);
                float x = Mathf.Sin(angle) * .078f * section * (1.06f - .12f * v);
                float y = .049f * (1f + Mathf.Cos(angle)) * section;
                Color c = Color.Lerp(Ivory, amber, Mathf.SmoothStep(0f, 1f, y / .075f));
                ids[i, j] = g.Vertex(new Vector3(x, y, z), c, new Vector2(u, v), 2);
            }
            for (int i = 0; i < rows; i++) for (int j = 0; j < sides; j++)
            {
                Vector3 middle = (g.p[ids[i, j]] + g.p[ids[i + 1, j + 1]]) * .5f;
                float sectionCentre = .049f * Mathf.Pow(Mathf.Sin((i + .5f) / rows * Mathf.PI), .68f);
                g.Quad(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1], ids[i, j + 1], new Vector3(middle.x, middle.y - sectionCentre, 0));
            }
            // Recessed aperture and opposing small teeth on the underside, visible when picked up.
            for (int i = 1; i < rows - 1; i++)
            {
                Vector3 left = g.p[ids[i, 0]], right = g.p[ids[i, sides]], nextLeft = g.p[ids[i + 1, 0]], nextRight = g.p[ids[i + 1, sides]];
                Color dark = ShellColor(.16f, .095f, .055f);
                int a = g.Vertex(left + Vector3.up * .005f, dark, Vector2.zero, 2, 1);
                int b = g.Vertex(right + Vector3.up * .005f, dark, Vector2.zero, 2, 1);
                int c = g.Vertex(nextRight + Vector3.up * .005f, dark, Vector2.zero, 2, 1);
                int d = g.Vertex(nextLeft + Vector3.up * .005f, dark, Vector2.zero, 2, 1);
                g.Quad(a, b, c, d, Vector3.down);
                if (i % 2 == 0)
                {
                    Ellipsoid(g, left * .75f + right * .25f + Vector3.up * .001f, new Vector3(.006f, .003f, .0027f), Ivory, 2, 8, 4);
                    Ellipsoid(g, left * .25f + right * .75f + Vector3.up * .001f, new Vector3(.006f, .003f, .0027f), Ivory, 2, 8, 4);
                }
            }
            return g;
        }

        static Geometry Spiral(bool slender, bool detail = false)
        {
            var g = new Geometry(); int rings = detail ? 144 : 96, sides = detail ? 36 : 24;
            int type = slender ? 4 : 1;
            var ids = new int[rings + 1, sides + 1];
            float turns = slender ? 5.6f : 3.0f;
            Vector3[] centres = new Vector3[rings + 1];
            float[] tube = new float[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float u = i / (float)rings;
                tube[i] = slender ? .0018f + .050f * Mathf.Pow(u, .85f) : .002f + .073f * Mathf.Pow(u, 1.75f);
                centres[i] = Vector3.forward * ((.5f - u) * (slender ? .30f : .205f));
                float flare = slender ? 1f : 1f + .13f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.88f, 1f, u));
                for (int j = 0; j <= sides; j++)
                {
                    float v = j / (float)sides, cross = v * Mathf.PI * 2f;
                    // One continuous exterior with helical whorl ridges, not a disconnected coil of tubes.
                    float whorl = 1f + (slender ? .12f : .075f) * Mathf.Cos(u * turns * Mathf.PI * 2f + cross);
                    float ribs = 1f + .012f * Mathf.Cos(u * Mathf.PI * 2f * 35f + cross * 2f);
                    float shoulder = slender ? 0f : .008f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(cross * 7f + u * 2f)), 10f)
                        * Mathf.Exp(-Mathf.Pow((u - .74f) / .17f, 2f));
                    float radius = tube[i] * whorl * ribs * flare + shoulder;
                    Vector3 offset = new Vector3(Mathf.Cos(cross), Mathf.Sin(cross) * (slender ? .94f : .86f), 0f) * radius;
                    Color c = slender ? Color.Lerp(ShellColor(.52f, .29f, .12f), Ivory, .5f + .3f * Mathf.Cos(u * Mathf.PI * 2f * turns))
                        : Color.Lerp(Ivory, Pink, .20f + .15f * Mathf.Sin(u * 19f));
                    ids[i, j] = g.Vertex(centres[i] + offset, c, new Vector2(u, v), type);
                }
            }
            for (int i = 0; i < rings; i++) for (int j = 0; j < sides; j++)
            {
                Vector3 outwards = (g.p[ids[i, j]] + g.p[ids[i + 1, j + 1]]) * .5f - (centres[i] + centres[i + 1]) * .5f;
                g.Quad(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1], ids[i, j + 1], outwards);
            }
            int tip = g.Vertex(centres[0], Ivory, Vector2.zero, type);
            for (int j = 0; j < sides; j++) g.Face(tip, ids[0, j + 1], ids[0, j], centres[0] - centres[1]);
            // A real open aperture: rounded thick lip, warm inner wall, and a recessed cavity.
            Vector3 axis = Vector3.back;
            int[] inner = new int[sides + 1], deep = new int[sides + 1];
            for (int j = 0; j <= sides; j++)
            {
                Vector3 offset = g.p[ids[rings, j]] - centres[rings];
                inner[j] = g.Vertex(centres[rings] + offset * .86f + axis * .001f, Pink, new Vector2(1, j / (float)sides), type, 1);
                deep[j] = g.Vertex(centres[rings] + offset * .57f - axis * tube[rings] * .70f,
                    ShellColor(.30f, .17f, .11f), new Vector2(.8f, j / (float)sides), type, 1);
            }
            for (int j = 0; j < sides; j++)
            {
                g.Quad(ids[rings, j], ids[rings, j + 1], inner[j + 1], inner[j], axis);
                g.Quad(inner[j], inner[j + 1], deep[j + 1], deep[j], centres[rings] - g.p[inner[j]]);
            }
            int bottom = g.Vertex(centres[rings] - axis * tube[rings] * .71f, ShellColor(.20f, .105f, .065f), Vector2.zero, type, 1);
            for (int j = 0; j < sides; j++) g.Face(bottom, deep[j], deep[j + 1], axis);
            return g;
        }

        static Geometry RibbedValve(bool oyster, bool detail = false)
        {
            var g = new Geometry(); int sides = detail ? 96 : 64, rings = detail ? 24 : 16;
            var ids = new int[2, rings + 1, sides + 1];
            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i <= rings; i++) for (int j = 0; j <= sides; j++)
                {
                    float r = i / (float)rings, u = j / (float)sides, a = u * Mathf.PI * 2f;
                    float ribs = Mathf.Pow(.5f + .5f * Mathf.Cos(a * (oyster ? 11f : 24f)), 2f);
                    float uneven = oyster ? 1f + .10f * Mathf.Sin(a * 3f) + .055f * Mathf.Cos(a * 7f + 1f) : 1f + .025f * ribs;
                    float dome = Mathf.Sqrt(Mathf.Max(0, 1f - r * r));
                    float layers = oyster ? .005f * Mathf.Sin(r * 14f * Mathf.PI + a * 2f) * r : .008f * ribs * Mathf.Sin(r * Mathf.PI);
                    float y = side == 0 ? .004f + (oyster ? .027f : .070f) * dome + layers : -.004f - (oyster ? .012f : .028f) * dome;
                    float x = Mathf.Sin(a) * (oyster ? .076f : .11f) * r * uneven;
                    float z = Mathf.Cos(a) * .12f * r * uneven;
                    if (oyster) x += .018f * r * Mathf.Cos(a);
                    Color c = oyster ? Color.Lerp(ShellColor(.29f, .32f, .25f), ShellColor(.67f, .67f, .55f), .4f + .35f * Mathf.Sin(r * 20f + a))
                        : Color.Lerp(ShellColor(.50f, .28f, .13f), ShellColor(.87f, .72f, .50f), ribs * .38f + dome * .45f);
                    ids[side, i, j] = g.Vertex(new Vector3(x, y, z), c, new Vector2(u, r), 3, side);
                }
                for (int i = 0; i < rings; i++) for (int j = 0; j < sides; j++)
                    g.Quad(ids[side, i, j], ids[side, i + 1, j], ids[side, i + 1, j + 1], ids[side, i, j + 1], side == 0 ? Vector3.up : Vector3.down);
            }
            for (int j = 0; j < sides; j++) g.Quad(ids[0, rings, j], ids[1, rings, j], ids[1, rings, j + 1], ids[0, rings, j + 1], g.p[ids[0, rings, j]]);
            return g;
        }

        static Geometry Limpet(bool detail = false)
        {
            var g = new Geometry(); int sides = detail ? 112 : 56, rings = detail ? 24 : 14;
            var ids = new int[rings + 1, sides + 1];
            for (int i = 0; i <= rings; i++) for (int j = 0; j <= sides; j++)
            {
                float r = i / (float)rings, u = j / (float)sides, a = u * Mathf.PI * 2f;
                float rib = .5f + .5f * Mathf.Cos(a * 28f);
                Vector3 point = new Vector3(Mathf.Sin(a) * .10f * r, .085f * Mathf.Pow(1f - r, .85f) + .003f * rib * Mathf.Sin(r * Mathf.PI),
                    Mathf.Cos(a) * .12f * r + .033f * (1f - r));
                ids[i, j] = g.Vertex(point, Color.Lerp(ShellColor(.40f, .40f, .24f), ShellColor(.80f, .75f, .52f), rib * .55f + r * .25f), new Vector2(u, r), 0);
            }
            for (int i = 0; i < rings; i++) for (int j = 0; j < sides; j++)
                g.Quad(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1], ids[i, j + 1], Vector3.up);
            int centre = g.Vertex(new Vector3(0, .005f, 0), ShellColor(.44f, .36f, .23f), Vector2.zero, 3, 1);
            for (int j = 0; j < sides; j++) g.Face(centre, ids[rings, j], ids[rings, j + 1], Vector3.down);
            return g;
        }

        static Geometry RazorClam(bool detail = false)
        {
            var g = new Geometry(); int rows = detail ? 40 : 28, sides = detail ? 48 : 32;
            var ids = new int[rows + 1, sides + 1];
            for (int i = 0; i <= rows; i++) for (int j = 0; j <= sides; j++)
            {
                float v = i / (float)rows, u = j / (float)sides, a = u * Mathf.PI * 2f;
                float end = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(v * Mathf.PI)), .16f);
                float seam = Mathf.Pow(Mathf.Abs(Mathf.Cos(a)), 18f);
                Vector3 p = new Vector3(Mathf.Cos(a) * .031f * end, Mathf.Sin(a) * .019f * end * (1f - .12f * seam), (v - .5f) * .27f);
                Color c = Color.Lerp(ShellColor(.43f, .34f, .18f), ShellColor(.85f, .76f, .51f), .55f + .22f * Mathf.Sin(v * 95f));
                if (seam > .8f) c = ShellColor(.26f, .22f, .12f);
                ids[i, j] = g.Vertex(p, c, new Vector2(u, v), 3);
            }
            for (int i = 0; i < rows; i++) for (int j = 0; j < sides; j++)
            {
                Vector3 centre = new Vector3(0, 0, ((i + .5f) / rows - .5f) * .27f);
                g.Quad(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1], ids[i, j + 1], (g.p[ids[i, j]] + g.p[ids[i + 1, j + 1]]) * .5f - centre);
            }
            return g;
        }

        static Geometry Murex(bool detail = false)
        {
            Geometry g = Spiral(false, detail);
            for (int row = 0; row < 3; row++) for (int j = 0; j < 7; j++)
            {
                float u = .52f + row * .18f, a = j * Mathf.PI * 2f / 7f + row * .18f;
                Vector3 axis = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * .86f, -.18f).normalized;
                Vector3 centre = Vector3.forward * ((.5f - u) * .205f) + new Vector3(Mathf.Cos(a), Mathf.Sin(a) * .86f, 0) * (.002f + .073f * Mathf.Pow(u, 1.75f));
                AddSpine(g, centre, axis, .013f + row * .004f, .032f + row * .009f);
            }
            return g;
        }

        static void AddSpine(Geometry g, Vector3 centre, Vector3 axis, float radius, float length)
        {
            Vector3 side = Vector3.Cross(axis, Vector3.forward).normalized, other = Vector3.Cross(axis, side).normalized;
            var ring = new int[9];
            for (int j = 0; j <= 8; j++)
            {
                float a = j / 8f * Mathf.PI * 2f;
                ring[j] = g.Vertex(centre + (side * Mathf.Cos(a) + other * Mathf.Sin(a)) * radius, Ivory, new Vector2(j / 8f, 0), 1);
            }
            int tip = g.Vertex(centre + axis * length + Vector3.forward * .009f, ShellColor(.88f, .78f, .62f), Vector2.one, 1);
            for (int j = 0; j < 8; j++) g.Face(ring[j], ring[j + 1], tip, (g.p[ring[j]] + g.p[ring[j + 1]]) * .5f - centre + axis * .2f);
        }

        static Geometry Coin(bool detailed)
        {
            var g = new Geometry(); int sides = detailed ? 96 : 40;
            Vector2[] profile = { new Vector2(0f, .002f), new Vector2(.098f, .002f), new Vector2(.109f, .004f),
                new Vector2(.110f, .007f), new Vector2(.110f, .013f), new Vector2(.106f, .017f),
                new Vector2(.098f, .018f), new Vector2(.095f, .014f), new Vector2(0f, .014f) };
            var ids = new int[profile.Length, sides + 1];
            for (int i = 0; i < profile.Length; i++) for (int j = 0; j <= sides; j++)
            {
                float angle = j / (float)sides * Mathf.PI * 2f;
                float radius = profile[i].x + (i == 3 || i == 4 ? .0008f * Mathf.Cos(angle * sides * .5f) : 0f);
                Color c = i == 6 ? BrightGold : i == 7 || i == 8 ? Gold : RecessGold;
                ids[i, j] = g.Vertex(new Vector3(Mathf.Cos(angle) * radius, profile[i].y, Mathf.Sin(angle) * radius), c,
                    new Vector2(.5f + Mathf.Cos(angle) * radius / .22f, .5f + Mathf.Sin(angle) * radius / .22f), 5);
            }
            for (int i = 0; i < profile.Length - 1; i++) for (int j = 0; j < sides; j++)
            {
                Vector2 edge = profile[i + 1] - profile[i];
                Vector3 mid = g.p[ids[i, j]] + g.p[ids[i, j + 1]];
                Vector3 outward = new Vector3(edge.y * mid.x, -edge.x * .2f, edge.y * mid.z);
                g.Quad(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1], ids[i, j + 1], outward);
            }
            // Raised bead border and an embossed fish above two fine wave bands.
            for (int j = 0; detailed && j < 28; j++)
            {
                float a = j * Mathf.PI * 2f / 28f;
                Ellipsoid(g, new Vector3(Mathf.Cos(a) * .089f, .0157f, Mathf.Sin(a) * .089f), new Vector3(.0024f, .0019f, .0024f), BrightGold, 5, 8, 4);
            }
            Ellipsoid(g, new Vector3(.008f, .016f, .012f), new Vector3(.043f, .003f, .020f), BrightGold, 5, detailed ? 28 : 16, detailed ? 6 : 4);
            AddTrianglePrism(g, new Vector2(-.028f, .012f), new Vector2(-.062f, -.010f), new Vector2(-.062f, .034f), .014f, .0185f, BrightGold);
            AddTrianglePrism(g, new Vector2(-.003f, .026f), new Vector2(-.009f, .046f), new Vector2(.023f, .028f), .014f, .018f, Gold);
            Ellipsoid(g, new Vector3(.032f, .019f, .017f), new Vector3(.0031f, .0005f, .0031f), RecessGold, 5, detailed ? 12 : 8, 4);
            for (int wave = 0; wave < 2; wave++)
            {
                int[] left = new int[25], right = new int[25];
                for (int j = 0; j <= 24; j++)
                {
                    float x = Mathf.Lerp(-.055f, .055f, j / 24f), z = -.031f - wave * .013f + .004f * Mathf.Sin(j / 24f * Mathf.PI * 3f);
                    left[j] = g.Vertex(new Vector3(x, .016f, z - .0012f), BrightGold, Vector2.zero, 5);
                    right[j] = g.Vertex(new Vector3(x, .016f, z + .0012f), BrightGold, Vector2.zero, 5);
                }
                for (int j = 0; j < 24; j++) g.Quad(left[j], left[j + 1], right[j + 1], right[j], Vector3.up);
            }
            return g;
        }

        static void AddTrianglePrism(Geometry g, Vector2 a, Vector2 b, Vector2 c, float low, float high, Color color)
        {
            Vector2[] corners = { a, b, c }; var bottom = new int[3]; var top = new int[3];
            for (int i = 0; i < 3; i++)
            {
                top[i] = g.Vertex(new Vector3(corners[i].x, high, corners[i].y), color, Vector2.zero, 5);
                bottom[i] = g.Vertex(new Vector3(corners[i].x, low, corners[i].y), color, Vector2.zero, 5);
            }
            g.Face(top[0], top[1], top[2], Vector3.up); g.Face(bottom[0], bottom[2], bottom[1], Vector3.down);
            Vector2 centre = (a + b + c) / 3f;
            for (int i = 0; i < 3; i++)
            {
                int next = (i + 1) % 3; Vector2 outward = (corners[i] + corners[next]) * .5f - centre;
                g.Quad(top[i], bottom[i], bottom[next], top[next], new Vector3(outward.x, 0, outward.y));
            }
        }

        static void Ellipsoid(Geometry g, Vector3 centre, Vector3 radius, Color color, int type, int sides, int rings)
        {
            var ids = new int[rings + 1, sides + 1];
            for (int i = 0; i <= rings; i++) for (int j = 0; j <= sides; j++)
            {
                float v = i / (float)rings, u = j / (float)sides;
                float a = v * Mathf.PI, b = u * Mathf.PI * 2f;
                Vector3 normal = new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b));
                ids[i, j] = g.Vertex(centre + Vector3.Scale(normal, radius), color, new Vector2(u, v), type, 1);
            }
            for (int i = 0; i < rings; i++) for (int j = 0; j < sides; j++)
                g.Quad(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1], ids[i, j + 1],
                    (g.p[ids[i, j]] + g.p[ids[i + 1, j + 1]]) * .5f - centre);
        }
    }
}

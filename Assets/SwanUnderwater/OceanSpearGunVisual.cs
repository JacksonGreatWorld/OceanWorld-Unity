using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>Original metre-scale, ambidextrous speargun visual. The root is the grip centre;
    /// +Z fires and +Y is the top rail. Muzzle and projectiles use a TIP pivot, with the shaft
    /// extending backwards. This component owns visuals only: no Update, input or physics.</summary>
    [DisallowMultipleComponent]
    public sealed class OceanSpearGunVisual : MonoBehaviour
    {
        public const float OverallLength = .72f;
        public const float ProjectileLength = .58f;
        public static readonly Vector3 MuzzleLocalPosition = new Vector3(0f, .105f, .60f);
        public Transform Body { get; private set; }
        public Transform Shaft { get; private set; }
        public Transform Muzzle { get; private set; }
        public bool IsLoaded => Shaft != null && Shaft.gameObject.activeSelf;

        static Mesh bodyMesh, trimMesh, shaftMesh;
        static Material surfaceMaterial, trimMaterial;
        static int leases;
        bool ownsLease;

        public static OceanSpearGunVisual Create(Transform parent)
        {
            Acquire();
            GameObject root = null;
            try
            {
                root = new GameObject("潮汐 · 渔枪");
                var visual = root.AddComponent<OceanSpearGunVisual>();
                visual.ownsLease = true;
                // Activate the owner before parenting, even when a preview/container parent is inactive.
                // This guarantees its destruction callback can release the lease.
                root.transform.SetParent(parent, false);
                if (parent != null) root.layer = parent.gameObject.layer;
                visual.Body = Child("Recoil body", root.transform);
                AddMesh("Teal alloy, timber grip and rubber", visual.Body, bodyMesh, surfaceMaterial);
                AddMesh("Luminous ocean inlay", visual.Body, trimMesh, trimMaterial);
                visual.Muzzle = Child("Muzzle - spear tip origin", visual.Body);
                visual.Muzzle.localPosition = MuzzleLocalPosition;
                visual.Shaft = AddMesh("Loaded spear", visual.Body, shaftMesh, surfaceMaterial);
                visual.Shaft.localPosition = MuzzleLocalPosition;
                return visual;
            }
            catch
            {
                // If the component exists it owns the lease, including delayed Play-mode destruction.
                if (root != null && root.TryGetComponent(out OceanSpearGunVisual owner) && owner.ownsLease)
                    DestroyOwned(root);
                else { DestroyOwned(root); Release(); }
                throw;
            }
        }

        /// <summary>Hides only the loaded spear. The caller owns reload timing.</summary>
        public void SetLoaded(bool loaded)
        {
            if (Shaft != null) Shaft.gameObject.SetActive(loaded);
        }

        /// <summary>Absolute recoil, 0 = rest and 1 = 22 mm back / 3 degrees up.
        /// Does not move the hand root; no automatic animation or per-frame allocation.</summary>
        public void SetRecoil(float amount01)
        {
            if (Body == null) return;
            float amount = float.IsNaN(amount01) || float.IsInfinity(amount01) ? 0f : Mathf.Clamp01(amount01);
            Body.localPosition = new Vector3(0f, 0f, -.022f * amount);
            Body.localRotation = Quaternion.Euler(-3f * amount, 0f, 0f);
        }

        /// <summary>Returns an independent spear with local tip (0,0,0), firing +Z.
        /// Place at Muzzle.position/rotation before animating. Destroy the GameObject to release
        /// its resource lease; it remains valid even after the entire gun has been destroyed.</summary>
        public GameObject CreateProjectile(Transform parent)
        {
            Acquire();
            GameObject root = null;
            try
            {
                root = new GameObject("潮汐 · 独立鱼叉");
                var owner = root.AddComponent<OceanSpearGunVisual>();
                owner.ownsLease = true;
                root.transform.SetParent(parent, false);
                if (parent != null) root.layer = parent.gameObject.layer;
                AddMesh("Spear", root.transform, shaftMesh, surfaceMaterial);
                return root;
            }
            catch
            {
                if (root != null && root.TryGetComponent(out OceanSpearGunVisual owner) && owner.ownsLease)
                    DestroyOwned(root);
                else { DestroyOwned(root); Release(); }
                throw;
            }
        }

        void OnDestroy()
        {
            if (!ownsLease) return;
            ownsLease = false;
            Release();
        }

        static Transform Child(string name, Transform parent)
        {
            var child = new GameObject(name);
            child.layer = parent.gameObject.layer;
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        static Transform AddMesh(string name, Transform parent, Mesh mesh, Material material)
        {
            Transform child = Child(name, parent);
            child.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return child;
        }

        static void Acquire()
        {
            if (bodyMesh == null || surfaceMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/CollectibleSurface");
                if (shader == null) throw new InvalidOperationException("Speargun needs Shaders/CollectibleSurface.");
                try
                {
                    bodyMesh = BuildBody().Finish("Original Tide speargun - combined body");
                    trimMesh = BuildTrim().Finish("Tide speargun - thin luminous inlay");
                    shaftMesh = BuildShaft().Finish("Tide spear - tip pivot");
                    surfaceMaterial = new Material(shader) { name = "Tide alloy / wood / rubber", enableInstancing = true };
                    trimMaterial = new Material(shader) { name = "Tide subtle emissive inlay", enableInstancing = true };
                    // Existing opaque URP shader already supports vertex colours, metallic, fog and XR.
                    // Only the very thin inlay uses its gentle emission. No lights or transparent passes.
                    trimMaterial.SetFloat("_ProximityGlow", 1f);
                }
                catch { DisposeResources(); throw; }
            }
            leases++;
        }

        static void Release()
        {
            if (leases <= 0 || --leases != 0) return;
            DisposeResources();
        }

        static void DisposeResources()
        {
            DestroyOwned(bodyMesh); DestroyOwned(trimMesh); DestroyOwned(shaftMesh);
            DestroyOwned(surfaceMaterial); DestroyOwned(trimMaterial);
            bodyMesh = trimMesh = shaftMesh = null;
            surfaceMaterial = trimMaterial = null;
        }

        static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        // Colour alpha is metallic in the existing surface shader; type 5 is polished alloy,
        // type 3 is rough timber/rubber. The second UV component suppresses shell growth detail.
        static readonly Color Teal = new Color(.025f, .30f, .35f, .63f);
        static readonly Color Silver = new Color(.65f, .75f, .77f, .85f);
        static readonly Color Gold = new Color(.80f, .57f, .23f, .82f);
        static readonly Color Rubber = new Color(.028f, .047f, .050f, .025f);
        static readonly Color Wood = new Color(.30f, .14f, .065f, .025f);
        static readonly Color Cyan = new Color(.22f, .88f, .80f, .2f);

        static Geometry BuildBody()
        {
            var g = new Geometry();
            // Streamlined, oval pressure body, broad at the receiver and narrowing at the muzzle.
            g.LoftZ(new[] { -.12f, -.105f, -.045f, .045f, .30f, .45f, .485f },
                new[] { .018f, .026f, .031f, .028f, .023f, .022f, .017f },
                new[] { .014f, .023f, .027f, .022f, .018f, .018f, .013f }, .074f, Teal, 16);
            g.LoftZ(new[] { -.121f, -.113f, -.10f }, new[] { .019f, .029f, .025f },
                new[] { .015f, .025f, .022f }, .074f, Rubber, 16, 3);
            foreach (float z in new[] { -.065f, .065f, .355f, .455f })
                g.LoftZ(new[] { z - .003f, z + .003f }, new[] { z < .1f ? .032f : .025f, z < .1f ? .032f : .025f },
                    new[] { z < .1f ? .027f : .021f, z < .1f ? .027f : .021f }, .074f, Gold, 16);
            g.LoftZ(new[] { .468f, .477f, .496f, .502f }, new[] { .024f, .032f, .032f, .022f },
                new[] { .019f, .026f, .026f, .019f }, .074f, Silver, 16);

            // Handle is centred on the grip origin, with a rear rake and exposed timber between rubber ribs.
            g.Tube(new[] { new Vector3(0, -.078f, -.025f), new Vector3(0, -.065f, -.027f),
                new Vector3(0, .016f, -.006f), new Vector3(0, .058f, .001f) },
                new[] { .016f, .022f, .020f, .019f }, Wood, 12, 3, .0f, .78f);
            for (int i = 0; i < 6; i++)
            {
                float y = -.062f + i * .017f, z = -.027f + (y + .065f) * .26f;
                g.Tube(new[] { new Vector3(0, y, z), new Vector3(0, y + .006f, z + .00156f) },
                    new[] { .0226f, .0226f }, Rubber, 12, 3, 1f, .80f);
            }
            g.Tube(new[] { new Vector3(0, -.082f, -.027f), new Vector3(0, -.076f, -.026f) },
                new[] { .020f, .020f }, Gold, 12, 5, 1f, .85f);

            // Open oval trigger guard and a separate curved trigger: no filled plate in the opening.
            var guard = new Vector3[33];
            for (int i = 0; i < guard.Length; i++)
            {
                float a = i * Mathf.PI * 2f / (guard.Length - 1);
                guard[i] = new Vector3(0, .010f + Mathf.Cos(a) * .037f, .056f + Mathf.Sin(a) * .040f);
            }
            g.Tube(guard, .0045f, Silver, 8, 5, true);
            g.Tube(new[] { new Vector3(0, .050f, .045f), new Vector3(0, .034f, .045f),
                new Vector3(0, .022f, .052f), new Vector3(0, .017f, .061f) }, .0038f, Gold, 8);

            // Twin taut rubber slings, wishbone links and raised guide rails.
            foreach (float side in new[] { -1f, 1f })
            {
                g.Tube(BandPath(side), .0056f, Rubber, 8, 3);
                g.Tube(new[] { new Vector3(side * .010f, .110f, .034f), new Vector3(0, .108f, .025f) }, .0018f, Silver, 6);
                g.Tube(new[] { new Vector3(side * .008f, .099f, -.058f), new Vector3(side * .008f, .099f, .495f) }, .0027f, Silver, 8);
                // Small recessed timber side insert and paired gold wave engraving.
                for (int wave = 0; wave < 2; wave++)
                {
                    var path = new Vector3[19];
                    for (int i = 0; i < path.Length; i++)
                    {
                        float t = i / 18f;
                        path[i] = new Vector3(side * .0307f, .074f + (wave - .5f) * .009f + Mathf.Sin(t * Mathf.PI * 2f) * .003f,
                            -.046f + t * .073f);
                    }
                    g.Tube(path, .0010f, Gold, 5);
                }
                foreach (float z in new[] { -.050f, .047f })
                    g.Tube(new[] { new Vector3(side * .030f, .075f, z), new Vector3(side * .033f, .075f, z) }, .0033f, Silver, 8);
            }

            // Compact line reel: dark wound centre with silver flanges and a gold axle.
            g.Tube(new[] { new Vector3(-.025f, .025f, .170f), new Vector3(.025f, .025f, .170f) }, .026f, Rubber, 20, 3);
            foreach (float x in new[] { -.027f, .027f })
                g.Tube(new[] { new Vector3(x - .002f, .025f, .170f), new Vector3(x + .002f, .025f, .170f) }, .030f, Silver, 20);
            g.Tube(new[] { new Vector3(-.033f, .025f, .170f), new Vector3(.033f, .025f, .170f) }, .006f, Gold, 12);
            for (int i = 0; i < 5; i++)
            {
                float x = -.019f + i * .0095f;
                g.Tube(new[] { new Vector3(x, .025f, .170f), new Vector3(x + .0015f, .025f, .170f) }, .0264f, Gold, 20);
            }
            return g;
        }

        static Vector3[] BandPath(float side)
        {
            var path = new List<Vector3>();
            Vector3 a = new Vector3(.010f, .111f, .034f), b = new Vector3(.016f, .111f, .050f),
                c = new Vector3(.041f, .108f, .080f), d = new Vector3(.041f, .108f, .145f);
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f, u = 1f - t;
                path.Add(a * (u * u * u) + b * (3f * u * u * t) + c * (3f * u * t * t) + d * (t * t * t));
            }
            // A true rounded return prevents a pinched/kinked rubber tube at the muzzle.
            for (int i = 0; i <= 16; i++)
            {
                float angle = i * Mathf.PI / 16f;
                path.Add(new Vector3(.030f + .011f * Mathf.Cos(angle), .108f, .452f + .011f * Mathf.Sin(angle)));
            }
            a = new Vector3(.019f, .108f, .145f); b = new Vector3(.019f, .108f, .090f);
            c = new Vector3(.010f, .111f, .060f); d = new Vector3(.006f, .111f, .052f);
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f, u = 1f - t;
                path.Add(a * (u * u * u) + b * (3f * u * u * t) + c * (3f * u * t * t) + d * (t * t * t));
            }
            for (int i = 0; i < path.Count; i++)
                path[i] = new Vector3(path[i].x * side, path[i].y, path[i].z);
            return path.ToArray();
        }

        static Geometry BuildTrim()
        {
            var g = new Geometry();
            foreach (float side in new[] { -1f, 1f })
            {
                g.Tube(new[] { new Vector3(side * .0265f, .079f, .082f), new Vector3(side * .0247f, .079f, .22f),
                    new Vector3(side * .0227f, .079f, .33f), new Vector3(side * .0227f, .079f, .428f) }, .0014f, Cyan, 6);
                g.Tube(new[] { new Vector3(side * .016f, .094f, .482f), new Vector3(side * .013f, .098f, .499f) }, .0014f, Cyan, 6);
            }
            return g;
        }

        static Geometry BuildShaft()
        {
            var g = new Geometry();
            // Tip pivot avoids visual/collision offsets in the caller's swept projectile test.
            g.Tube(new[] { new Vector3(0, 0, -.580f), new Vector3(0, 0, -.050f),
                new Vector3(0, 0, -.037f), Vector3.zero }, new[] { .0030f, .0030f, .0062f, 0f }, Silver, 12);
            foreach (float side in new[] { -1f, 1f })
                g.Tube(new[] { new Vector3(0, 0, -.039f), new Vector3(side * .013f, 0, -.069f),
                    new Vector3(side * .014f, 0, -.075f) }, new[] { .0026f, .0018f, 0f }, Silver, 6);
            foreach (float z in new[] { -.545f, -.530f, -.085f })
                g.Tube(new[] { new Vector3(0, 0, z), new Vector3(0, 0, z + .003f) }, .0038f, Gold, 10);
            return g;
        }

        sealed class Geometry
        {
            readonly List<Vector3> p = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Color> colors = new List<Color>();
            readonly List<Vector2> uv = new List<Vector2>(), kind = new List<Vector2>();
            readonly List<int> triangles = new List<int>();
            int Vertex(Vector3 position, Vector3 normal, Color color, float u, float v, int type, float noDetail)
            {
                int index = p.Count; p.Add(position); n.Add(normal); colors.Add(color);
                uv.Add(new Vector2(u, v)); kind.Add(new Vector2(type, noDetail)); return index;
            }
            void Face(int a, int b, int c, Vector3 outward)
            {
                Vector3 cross = Vector3.Cross(p[b] - p[a], p[c] - p[a]);
                if (cross.sqrMagnitude < 1e-18f) return;
                if (Vector3.Dot(cross, outward) < 0f) { int swap = b; b = c; c = swap; }
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }
            public void Tube(Vector3[] path, float radius, Color color, int sides = 10, int type = 5, bool closed = false)
            {
                var radii = new float[path.Length];
                for (int i = 0; i < radii.Length; i++) radii[i] = radius;
                Tube(path, radii, color, sides, type, 1f, 1f, closed);
            }
            public void Tube(Vector3[] path, float[] radii, Color color, int sides, int type = 5,
                float noDetail = 1f, float aspect = 1f, bool closed = false)
            {
                int start = p.Count;
                // Pick one frame reference for the entire path. In particular, a closed trigger
                // guard must not switch axes at its top and produce a twisted seam.
                Vector3 reference = Vector3.up;
                float bestScore = float.PositiveInfinity;
                foreach (Vector3 candidate in new[] { Vector3.up, Vector3.forward, Vector3.right })
                {
                    float score = 0f;
                    for (int i = 0; i < path.Length - 1; i++)
                        score = Mathf.Max(score, Mathf.Abs(Vector3.Dot((path[i + 1] - path[i]).normalized, candidate)));
                    if (score < bestScore) { reference = candidate; bestScore = score; }
                }
                for (int i = 0; i < path.Length; i++)
                {
                    Vector3 tangent = i == 0 ? path[1] - path[0] : i == path.Length - 1 ? path[i] - path[i - 1] : path[i + 1] - path[i - 1];
                    if (closed && (i == 0 || i == path.Length - 1)) tangent = path[1] - path[path.Length - 2];
                    tangent.Normalize();
                    Vector3 axisX = Vector3.Cross(reference, tangent).normalized;
                    if (axisX.sqrMagnitude < .5f) axisX = Vector3.Cross(Vector3.right, tangent).normalized;
                    Vector3 axisY = Vector3.Cross(tangent, axisX).normalized;
                    for (int j = 0; j <= sides; j++)
                    {
                        float angle = j * Mathf.PI * 2f / sides;
                        Vector3 radial = axisX * Mathf.Cos(angle) + axisY * (Mathf.Sin(angle) * aspect);
                        Vector3 normal = (axisX * Mathf.Cos(angle) + axisY * (Mathf.Sin(angle) / aspect)).normalized;
                        // Include taper in the normal, especially the fish-shaped spear tip.
                        int before = Math.Max(0, i - 1), after = Math.Min(path.Length - 1, i + 1);
                        float length = (path[after] - path[before]).magnitude;
                        if (length > .00001f) normal = (normal - tangent * ((radii[after] - radii[before]) / length)).normalized;
                        Vertex(path[i] + radial * radii[i], normal, color, j / (float)sides, i / (float)(path.Length - 1), type, noDetail);
                    }
                }
                for (int i = 0; i < path.Length - 1; i++)
                    for (int j = 0; j < sides; j++)
                    {
                        int a = start + i * (sides + 1) + j, b = a + 1, c = b + sides + 1, d = a + sides + 1;
                        Face(a, b, c, n[a] + n[b] + n[c]); Face(a, c, d, n[a] + n[c] + n[d]);
                    }
                if (!closed)
                {
                    Cap(path[0], -(path[1] - path[0]).normalized, start, sides, color, type, noDetail);
                    Cap(path[path.Length - 1], (path[path.Length - 1] - path[path.Length - 2]).normalized,
                        start + (path.Length - 1) * (sides + 1), sides, color, type, noDetail);
                }
            }
            void Cap(Vector3 center, Vector3 normal, int ring, int sides, Color color, int type, float noDetail)
            {
                int start = p.Count;
                Vertex(center, normal, color, .5f, .5f, type, noDetail);
                for (int j = 0; j <= sides; j++) Vertex(p[ring + j], normal, color, j / (float)sides, 0f, type, noDetail);
                for (int j = 0; j < sides; j++) Face(start, start + 1 + j, start + 2 + j, normal);
            }
            public void LoftZ(float[] z, float[] width, float[] height, float y, Color color, int sides, int type = 5)
            {
                // Width/height change separately while the stock remains aligned to the firing rail.
                int start = p.Count;
                for (int i = 0; i < z.Length; i++)
                    for (int j = 0; j <= sides; j++)
                    {
                        float angle = j * Mathf.PI * 2f / sides;
                        int a = Math.Max(0, i - 1), b = Math.Min(z.Length - 1, i + 1);
                        Vector3 normal = new Vector3(Mathf.Cos(angle) / width[i], Mathf.Sin(angle) / height[i],
                            -((width[b] - width[a]) * Mathf.Cos(angle) * Mathf.Cos(angle) / width[i]
                            + (height[b] - height[a]) * Mathf.Sin(angle) * Mathf.Sin(angle) / height[i]) / (z[b] - z[a])).normalized;
                        Vertex(new Vector3(width[i] * Mathf.Cos(angle), y + height[i] * Mathf.Sin(angle), z[i]), normal,
                            color, j / (float)sides, i / (float)(z.Length - 1), type, 1f);
                    }
                for (int i = 0; i < z.Length - 1; i++)
                    for (int j = 0; j < sides; j++)
                    {
                        int a = start + i * (sides + 1) + j, b = a + 1, c = b + sides + 1, d = a + sides + 1;
                        Face(a, b, c, n[a] + n[b] + n[c]); Face(a, c, d, n[a] + n[c] + n[d]);
                    }
                Cap(new Vector3(0, y, z[0]), Vector3.back, start, sides, color, type, 1f);
                Cap(new Vector3(0, y, z[z.Length - 1]), Vector3.forward, start + (z.Length - 1) * (sides + 1), sides, color, type, 1f);
            }
            public Mesh Finish(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(p); mesh.SetNormals(n); mesh.SetColors(colors); mesh.SetUVs(0, uv); mesh.SetUVs(1, kind);
                mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
                mesh.UploadMeshData(false); // Tiny shared meshes stay readable for exact ground placement and Editor checks.
                return mesh;
            }
        }
    }
}

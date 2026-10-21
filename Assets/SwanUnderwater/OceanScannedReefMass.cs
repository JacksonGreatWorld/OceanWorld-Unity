using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>
    /// Build-scoped placement of admitted, metre-scale scans. The imported silhouettes and
    /// their UVs are retained; this class never stretches a small coral into a mountain.
    /// </summary>
    internal static class OceanScannedReefMass
    {
        internal sealed class Model
        {
            public string stem;
            public readonly GameObject[] prefabs = new GameObject[3];
            public readonly ReefSupportGeometry[] support = new ReefSupportGeometry[3];
            public readonly Surface[] surfaces = new Surface[3];
            public GameObject collisionPrefab;
            public ReefSupportGeometry collisionSupport;
            public Surface collisionSurface;
            public Vector3 front;
        }

        internal sealed class Assets
        {
            public readonly Model mountainside = new Model { stem = "market_mountainside" };
            public readonly Model cliff = new Model { stem = "market_coastal_cliff_02" };
            public readonly Model brain = new Model { stem = "market_coral_platygyra" };
            public readonly Model fan = new Model { stem = "market_fan_coral" };
            public readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
            public int fanColonies, fanNearRenderers, fanAllLodRenderers;
            public int brainColonies, brainNearRenderers, brainAllLodRenderers;
        }

        internal sealed class Surface
        {
            public Vector3[] vertices;
            public int[] triangles;
        }

        internal sealed class Placement
        {
            public Transform root;
            public Bounds bounds, geometryBounds;
            public LODGroup lod;
            public Renderer substrate;
            public Matrix4x4 modelToWorld, worldToModel;
            public Model source;
            public Vector3[] groundContacts;
            public Func<float, float, float> top;
            public int triangles;
            public float scale;
        }

        // Each request is awaited independently, matching the environment's cancellable
        // staged build. Imported meshes/materials are never registered as generated assets.
        internal static IEnumerator Load(bool staged, Action<float> progress, Action<Assets> completed)
        {
            var assets = new Assets();
            Model[] models = { assets.mountainside, assets.cliff, assets.brain, assets.fan };
            int loaded = 0;
            for (int modelIndex = 0; modelIndex < models.Length; modelIndex++)
            {
                Model model = models[modelIndex];
                for (int level = 0; level < model.prefabs.Length; level++)
                {
                    string path = "RealisticReef/" + model.stem + "_lod" + level;
                    if (staged)
                    {
                        ResourceRequest request = Resources.LoadAsync<GameObject>(path);
                        yield return request;
                        model.prefabs[level] = request.asset as GameObject;
                    }
                    else model.prefabs[level] = Resources.Load<GameObject>(path);
                    if (model.prefabs[level] == null)
                        throw new InvalidOperationException("[ScannedReef] Missing admitted source " + path);
                    model.support[level] = ReefSupportGeometry.Read(model.prefabs[level], true);
                    model.surfaces[level] = ReadSurface(model.prefabs[level]);
                    progress?.Invoke(++loaded / 14f);
                    if (staged) yield return null;
                }
                Bounds reference = model.support[0].bounds;
                for (int level = 1; level < model.support.Length; level++)
                {
                    Bounds candidate = model.support[level].bounds;
                    if (Vector3.Distance(reference.center, candidate.center) > .03f ||
                        Vector3.Distance(reference.size, candidate.size) > .06f)
                        throw new InvalidOperationException("[ScannedReef] LODs must share the same admitted origin and envelope: " + model.stem);
                }
                if (modelIndex < 2) model.front = ImportedScanFront(model.surfaces[0], model.stem);
            }
            foreach (Model model in new[] { assets.mountainside, assets.cliff })
            {
                string path = "RealisticReef/" + model.stem + "_collision";
                if (staged)
                {
                    ResourceRequest request = Resources.LoadAsync<GameObject>(path);
                    yield return request;
                    model.collisionPrefab = request.asset as GameObject;
                }
                else model.collisionPrefab = Resources.Load<GameObject>(path);
                if (model.collisionPrefab == null)
                    throw new InvalidOperationException("[ScannedReef] Missing admitted collision source " + path);
                if (model.collisionPrefab.GetComponentsInChildren<Renderer>(true).Length != 0)
                    throw new InvalidOperationException("[ScannedReef] Collision hull must have no visible renderer: " + path);
                model.collisionSupport = ReefSupportGeometry.Read(model.collisionPrefab, true);
                model.collisionSurface = ReadSurface(model.collisionPrefab);
                RequireClosed(model.collisionSurface, path);
                if (model.collisionSurface.triangles.Length / 3 > 240)
                    throw new InvalidOperationException("[ScannedReef] Collision hull exceeds the admitted 240-triangle budget: " + path);
                progress?.Invoke(++loaded / 14f);
                if (staged) yield return null;
            }
            completed(assets);
        }

        internal static Placement Place(Assets assets, Model model, Transform parent, string name,
            Vector3 point, float uniformScale, float yaw, Func<float, float, float> floor,
            OceanSanctuaryResources resources, ReefProximityLod proximity)
        {
            if (!(uniformScale >= .8f && uniformScale <= 1.2f))
                throw new ArgumentOutOfRangeException(nameof(uniformScale), "Keep large scans near their natural metre scale.");
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            group.position = new Vector3(point.x, floor(point.x, point.z), point.z);
            group.rotation = Quaternion.Euler(0, yaw, 0);
            group.localScale = Vector3.one;
            var levels = new LOD[3];
            Transform frontInstance = null, backInstance = null;
            for (int level = 0; level < levels.Length; level++)
            {
                var combined = new List<Renderer>();
                for (int face = 0; face < 2; face++)
                {
                    GameObject instance = UnityEngine.Object.Instantiate(model.prefabs[level], group, false);
                    instance.name = (face == 0 ? "Photographed front " : "Opposed scan ") + model.stem + " LOD" + level;
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.Euler(0, face * 180, 0);
                    instance.transform.localScale = Vector3.one * uniformScale;
                    if (level == 0)
                    { if (face == 0) frontInstance = instance.transform; else backInstance = instance.transform; }
                    foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) Destroy(collider);
                    Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                    AssignMaterials(assets, renderers, resources, level < 2);
                    combined.AddRange(renderers);
                }
                levels[level] = new LOD(level == 0 ? .32f : level == 1 ? .10f : .012f, combined.ToArray());
            }
            GameObject hull = UnityEngine.Object.Instantiate(model.collisionPrefab, group, false);
            hull.name = "Independent closed collision hull " + model.stem;
            hull.transform.localPosition = Vector3.zero;
            hull.transform.localRotation = Quaternion.identity;
            hull.transform.localScale = Vector3.one * uniformScale;
            foreach (Collider collider in hull.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            // A cliff's low ledge is not its foot. Use only genuine lowest half-metre
            // vertices, rather than burying the wall to force every overhang into sand.
            Vector3[] baseContacts = BaseContacts(model);
            float grounding = float.PositiveInfinity;
            foreach (Vector3 contact in baseContacts)
            {
                Vector3 world = hull.transform.TransformPoint(contact);
                grounding = Mathf.Min(grounding, floor(world.x, world.z) - world.y);
            }
            if (float.IsInfinity(grounding) || float.IsNaN(grounding))
                throw new InvalidOperationException("[ScannedReef] No finite real grounding contacts: " + model.stem);
            // The most exposed genuine foot is buried; other base contacts may enter sand.
            // No vertex bending or nonuniform scale is used to match the sea floor.
            group.position += Vector3.up * (grounding - .12f);
            foreach (MeshFilter filter in hull.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
            }
            var lod = group.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(levels); lod.fadeMode = LODFadeMode.None; lod.RecalculateBounds();
            Bounds bounds = BoundsOf(levels[0].renderers);
            proximity.Register(lod, bounds, false);
            Matrix4x4 toWorld = frontInstance.localToWorldMatrix, toModel = toWorld.inverse;
            var contacts = new Vector3[baseContacts.Length];
            for (int i = 0; i < contacts.Length; i++) contacts[i] = toWorld.MultiplyPoint3x4(baseContacts[i]);
            var result = new Placement {
                root = group, bounds = bounds,
                geometryBounds = DetailedGeometryBounds(model.surfaces[0], frontInstance, backInstance),
                lod = lod, substrate = levels[0].renderers[0],
                source = model, modelToWorld = toWorld, worldToModel = toModel,
                groundContacts = contacts, scale = uniformScale,
                triangles = model.surfaces[0].triangles.Length / 3 * 2
            };
            result.top = (x, z) => {
                Vector3 local = toModel.MultiplyPoint3x4(new Vector3(x, group.position.y, z));
                float lowest = float.PositiveInfinity;
                for (int level = 0; level < model.support.Length; level++)
                {
                    if (model.support[level].TryTop(local.x, local.z, out float y)) lowest = Mathf.Min(lowest, y);
                    if (model.support[level].TryTop(-local.x, -local.z, out float opposite)) lowest = Mathf.Min(lowest, opposite);
                }
                return float.IsPositiveInfinity(lowest) ? floor(x, z) :
                    toWorld.MultiplyPoint3x4(new Vector3(local.x, lowest, local.z)).y;
            };
            Debug.Log($"[ScannedReef] host={name} source={model.stem} uniformScale={uniformScale:F2} " +
                $"bounds={bounds} nearTriangles={result.triangles} collisionTriangles={model.collisionSurface.triangles.Length / 3} " +
                $"baseContacts={contacts.Length}; two opposed natural scan sheets, independent closed static collision hull");
            return result;
        }

        static Vector3[] BaseContacts(Model model)
        {
            float cutoff = model.collisionSupport.bounds.min.y + .5f;
            var contacts = new List<Vector3>();
            foreach (Vector3 vertex in model.collisionSurface.vertices)
                if (vertex.y <= cutoff) contacts.Add(vertex);
            if (contacts.Count == 0)
                throw new InvalidOperationException("[ScannedReef] No actual low collision foot vertices: " + model.stem);
            return contacts.ToArray();
        }

        internal static List<Bounds> AttachColonies(Assets assets, Placement host, Vector3 water,
            int count, int seed, OceanSanctuaryResources resources, ReefProximityLod proximity)
        {
            var result = new List<Bounds>(count);
            Surface surface = host.source.surfaces[0];
            var selected = new List<int>(); var areaEnds = new List<float>(); float total = 0;
            for (int index = 0; index < surface.triangles.Length; index += 3)
            {
                WorldTriangle(host, surface, index, out Vector3 a, out Vector3 b, out Vector3 c);
                Vector3 cross = Vector3.Cross(b - a, c - a); float length = cross.magnitude;
                if (length < .0001f) continue;
                Vector3 normal = cross / length, center = (a + b + c) / 3f;
                Vector3 towardWater = water - center; towardWater.y = 0;
                // Natural cliff fronts and ledges are eligible; the buried base, underside
                // and sealed rear do not get colonies. Vertical faces need no TryTop query.
                if (center.y < host.bounds.min.y + .35f || normal.y < -.15f ||
                    (normal.y < .5f && Vector3.Dot(normal, towardWater.normalized) < .2f)) continue;
                float lowerFace = Mathf.Lerp(1f, .2f, Mathf.InverseLerp(5f, 9f, center.y - host.bounds.min.y));
                total += length * .5f * lowerFace; selected.Add(index); areaEnds.Add(total);
            }
            if (total <= 0) throw new InvalidOperationException("[ScannedReef] No exposed attachment faces on " + host.root.name);
            var random = new System.Random(seed); var anchors = new List<Vector3>(count);
            for (int attempt = 0; attempt < count * 12 && result.Count < count; attempt++)
            {
                float ticket = (float)random.NextDouble() * total; int first = 0, last = areaEnds.Count - 1;
                while (first < last) { int middle = (first + last) / 2; if (areaEnds[middle] < ticket) first = middle + 1; else last = middle; }
                WorldTriangle(host, surface, selected[first], out Vector3 a, out Vector3 b, out Vector3 c);
                float u = Mathf.Sqrt((float)random.NextDouble()), v = (float)random.NextDouble();
                Vector3 at = a * (1f - u) + b * (u * (1f - v)) + c * (u * v);
                bool crowded = false;
                foreach (Vector3 previous in anchors) if ((previous - at).sqrMagnitude < .38f) { crowded = true; break; }
                if (crowded) continue;
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                Model colony = result.Count % 9 == 4 ? assets.brain : assets.fan;
                float span = colony == assets.brain ? Mathf.Lerp(.35f, .65f, (float)random.NextDouble()) :
                    Mathf.Lerp(.60f, 1.10f, (float)random.NextDouble());
                Bounds bounds = PlaceColony(assets, colony, host, at, normal, span,
                    (float)random.NextDouble() * 360f, resources, proximity);
                anchors.Add(at); result.Add(bounds);
            }
            Debug.Log($"[ScannedReef] attached={result.Count}/{count} host={host.root.name}; real triangle roots, original texture colour");
            return result;
        }

        static Bounds PlaceColony(Assets assets, Model model, Placement host, Vector3 at, Vector3 normal,
            float diameter, float yaw, OceanSanctuaryResources resources, ReefProximityLod proximity)
        {
            var group = new GameObject("Scanned wall colony " + model.stem).transform;
            group.gameObject.AddComponent<OceanNaturalistTag>().Initialize(model == assets.brain ? 25 : 26);
            group.SetParent(host.root, true);
            group.position = at - normal * .035f;
            group.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, yaw, 0);
            Bounds reference = model.support[0].bounds;
            float scale = diameter / Mathf.Max(.001f, Mathf.Max(reference.size.x, reference.size.z));
            var lods = new LOD[3];
            for (int level = 0; level < lods.Length; level++)
            {
                GameObject instance = UnityEngine.Object.Instantiate(model.prefabs[level], group, false);
                instance.transform.localPosition = new Vector3(-reference.center.x, -reference.min.y, -reference.center.z) * scale;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one * scale;
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) Destroy(collider);
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                AssignMaterials(assets, renderers, resources, false);
                if (model == assets.fan)
                {
                    assets.fanAllLodRenderers += renderers.Length;
                    if (level == 0) assets.fanNearRenderers += renderers.Length;
                }
                else
                {
                    assets.brainAllLodRenderers += renderers.Length;
                    if (level == 0) assets.brainNearRenderers += renderers.Length;
                }
                lods[level] = new LOD(level == 0 ? .28f : level == 1 ? .07f : .005f, renderers);
            }
            if (model == assets.fan) assets.fanColonies++;
            else assets.brainColonies++;
            var lod = group.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(lods); lod.fadeMode = LODFadeMode.None; lod.RecalculateBounds();
            // A 40K source coral earns LOD0 only at genuinely close screen size. The
            // eight-metre boost intended for metre-scale reef substrates would force
            // every miniature into its highest mesh across both entrance walls.
            Bounds bounds = BoundsOf(lods[0].renderers);
            return bounds;
        }

        static void AssignMaterials(Assets assets, Renderer[] renderers, OceanSanctuaryResources resources, bool shadows)
        {
            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null || source.shader == null)
                        throw new InvalidOperationException("[ScannedReef] Missing admitted PBR material on " + renderer.name);
                    if (!assets.materials.TryGetValue(source, out Material material))
                    {
                        material = new Material(source) { name = "Admitted reef " + source.name, enableInstancing = true };
                        // The scan's original texture is the colour reference. Do not apply
                        // the procedural rock grey or the coral colour-variation palette.
                        if (material.HasProperty("roughnessFactor")) material.SetFloat("roughnessFactor", .9f);
                        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .12f);
                        assets.materials.Add(source, material); resources.Own(material);
                    }
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials; renderer.receiveShadows = true;
                renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
        }

        static Bounds DetailedGeometryBounds(Surface surface, Transform front, Transform back)
        {
            // Measure visible vertices once at construction. Renderer bounds deliberately
            // remain the conservative rotated-local-AABB used for culling and clearance.
            Matrix4x4 a = front.localToWorldMatrix, b = back.localToWorldMatrix;
            var result = new Bounds(a.MultiplyPoint3x4(surface.vertices[0]), Vector3.zero);
            foreach (Vector3 vertex in surface.vertices)
            { result.Encapsulate(a.MultiplyPoint3x4(vertex)); result.Encapsulate(b.MultiplyPoint3x4(vertex)); }
            return result;
        }

        static Bounds BoundsOf(Renderer[] renderers)
        {
            if (renderers.Length == 0) throw new InvalidOperationException("[ScannedReef] Empty source renderers");
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static void WorldTriangle(Placement placement, Surface surface, int triangle,
            out Vector3 a, out Vector3 b, out Vector3 c)
        {
            a = placement.modelToWorld.MultiplyPoint3x4(surface.vertices[surface.triangles[triangle]]);
            b = placement.modelToWorld.MultiplyPoint3x4(surface.vertices[surface.triangles[triangle + 1]]);
            c = placement.modelToWorld.MultiplyPoint3x4(surface.vertices[surface.triangles[triangle + 2]]);
        }

        static Surface ReadSurface(GameObject prefab)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            Matrix4x4 inverse = prefab.transform.worldToLocalMatrix;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh; if (mesh == null) continue;
                if (!mesh.isReadable)
                    throw new InvalidOperationException("[ScannedReef] Admission bake must keep the bounded new scan meshes CPU-readable: " + prefab.name);
                Matrix4x4 matrix = inverse * filter.transform.localToWorldMatrix; int first = vertices.Count;
                foreach (Vector3 vertex in mesh.vertices) vertices.Add(matrix.MultiplyPoint3x4(vertex));
                foreach (int index in mesh.triangles) triangles.Add(first + index);
            }
            if (vertices.Count == 0 || triangles.Count == 0)
                throw new InvalidOperationException("[ScannedReef] Empty admitted geometry: " + prefab.name);
            return new Surface { vertices = vertices.ToArray(), triangles = triangles.ToArray() };
        }

        static void RequireClosed(Surface surface, string name)
        {
            var weld = new Dictionary<Vector3Int, int>(); var remap = new int[surface.vertices.Length];
            for (int i = 0; i < remap.Length; i++)
            {
                Vector3 p = surface.vertices[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 10000), Mathf.RoundToInt(p.y * 10000), Mathf.RoundToInt(p.z * 10000));
                if (!weld.TryGetValue(key, out int mapped)) { mapped = weld.Count; weld.Add(key, mapped); }
                remap[i] = mapped;
            }
            var edges = new Dictionary<ulong, int>();
            for (int i = 0; i < surface.triangles.Length; i += 3)
            {
                int a = remap[surface.triangles[i]], b = remap[surface.triangles[i + 1]], c = remap[surface.triangles[i + 2]];
                if (a == b || b == c || c == a) continue;
                Edge(edges, a, b); Edge(edges, b, c); Edge(edges, c, a);
            }
            int open = 0;
            foreach (int count in edges.Values) if (count == 1) open++;
            if (open > 0)
                throw new InvalidOperationException("[ScannedReef] " + name + " has " + open +
                    " open welded edges. The independent admitted collision hull must be genuinely closed.");
        }

        static Vector3 ImportedScanFront(Surface surface, string name)
        {
            // Both admitted originals have their dense photographed face along glTF +Z.
            // Read the actual Unity mesh to account for the importer's RH/LH conversion;
            // do not guess which sign the import pipeline chose.
            int positive = 0, negative = 0;
            for (int i = 0; i < surface.triangles.Length; i += 3)
            {
                Vector3 a = surface.vertices[surface.triangles[i]], b = surface.vertices[surface.triangles[i + 1]],
                    c = surface.vertices[surface.triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                if (normal.z > .1f) positive++;
                else if (normal.z < -.1f) negative++;
            }
            if (positive == negative || positive + negative == 0)
                throw new InvalidOperationException("[ScannedReef] Imported photographed face is ambiguous; verify the admitted axis for " + name);
            Vector3 result = positive > negative ? Vector3.forward : Vector3.back;
            Debug.Log($"[ScannedReefFront] source={name} positiveZFaces={positive} negativeZFaces={negative} " +
                $"importedFront={result}; photographed source +Z, verify final appearance in rendered captures");
            return result;
        }

        static void Edge(Dictionary<ulong, int> edges, int a, int b)
        {
            ulong key = ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
            edges.TryGetValue(key, out int count); edges[key] = count + 1;
        }

        static void Destroy(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>
    /// Measures the generated detailed rock vertices against the rendered sea floor.
    /// Authoring metadata identifies hosts only; nominal heights are never the oracle.
    /// Visibility, art quality and headset performance still require rendered review.
    /// </summary>
    public static class OceanSanctuaryCompositionChecks
    {
        sealed class MeasuredMass
        {
            public Bounds bounds;
            public float relief;
            public int category;
            public bool scan;
        }

        static int assertions;

        static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Sanctuary composition: " + message);
        }

        public static int RunAll(UnderwaterWorld world)
        {
            assertions = 0;
            Check(world != null && world.IsReady && world.ActiveEnvironment == OceanEnvironmentKind.CoralSanctuary,
                "ready sanctuary required");
            var groups = new Dictionary<string, LODGroup>(StringComparer.Ordinal);
            foreach (var group in world.ActiveReefRoot.GetComponentsInChildren<LODGroup>(true))
            {
                if (!group.name.StartsWith("Reef terrace ", StringComparison.Ordinal) &&
                    !group.name.StartsWith("Distant offset reef bluff ", StringComparison.Ordinal)) continue;
                Check(!groups.ContainsKey(group.name), "rock host names identify one actual LOD group: " + group.name);
                groups.Add(group.name, group);
            }
            var scans = new Dictionary<string, OceanCoralSanctuary.ScannedMass>(StringComparer.Ordinal);
            foreach (var scan in OceanCoralSanctuary.ScannedMasses)
            {
                Check(!scans.ContainsKey(scan.name), "one measured record per natural scan host");
                scans.Add(scan.name, scan);
            }
            int plannedOuterScans = 0;
            for (int i = 0; i < OceanCoralSanctuary.ExplorationBanks.Count; i++)
                if (OceanCoralSanctuary.ExplorationBanks[i].height >= 15f)
                {
                    plannedOuterScans++;
                    Check(scans.ContainsKey("Reef terrace " + (100 + i)),
                        "every formerly tall exploration bank is replaced by a measured natural scan");
                }
            foreach (int entrance in new[] { 8, 10, 15 })
                Check(scans.ContainsKey("Reef terrace " + entrance), "formerly tall entrance host uses a natural scan");
            foreach (int boundary in new[] { 101, 104, 106, 108 })
                Check(scans.ContainsKey("Distant offset reef bluff " + boundary), "formerly tall boundary host uses a natural scan");
            Check(scans.Count == 3 + plannedOuterScans + 4 && scans.Count >= 10 && scans.Count <= 16,
                "natural scans cover the authored tall-host replacement plan within a bounded instance budget");

            var measured = new List<MeasuredMass>();
            int outerLarge = 0, outerMedium = 0, outerSmall = 0, outerCount = 0;
            float minimumRelief = float.PositiveInfinity, maximumRelief = 0f;
            float maximumProceduralRelief = 0f;
            float minimumFootprint = float.PositiveInfinity, maximumFootprint = 0f;
            float minimumOuterFootprint = float.PositiveInfinity, maximumOuterFootprint = 0f;
            float largestSmall = 0f, smallestLarge = float.PositiveInfinity;
            float minimumFootingHeight = float.PositiveInfinity, maximumFootingHeight = float.NegativeInfinity;
            Vector3 eye = world.ActiveSpawn;
            eye.y = UnderwaterWorld.SampleRenderedSeabedHeight(eye.x, eye.z) + 1.5f;
            bool leftEntranceFrame = false, rightEntranceFrame = false;

            foreach (var host in OceanCoralSanctuary.ReefMasses)
            {
                Check(groups.TryGetValue(host.name, out var group), "metadata resolves to a generated host: " + host.name);
                var lods = group.GetLODs();
                bool scan = scans.TryGetValue(host.name, out var scanInfo);
                Check(lods.Length == (scan ? 3 : 2) && lods[0].renderers.Length > 0,
                    "rock has its real detailed and fallback levels: " + host.name);
                Renderer substrate = null;
                var substrates = new List<Renderer>();
                foreach (var renderer in lods[0].renderers)
                {
                    var candidate = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                    if (candidate == null || candidate.sharedMesh == null) continue;
                    string meshName = candidate.sharedMesh.name;
                    if (scan || meshName.StartsWith("Reef terrace ", StringComparison.Ordinal) ||
                        meshName.StartsWith("Batched reef bluff ", StringComparison.Ordinal))
                    { if (substrate == null) substrate = renderer; substrates.Add(renderer); }
                }
                Check(substrate != null, "measurement excludes attached corals and uses the actual substrate: " + host.name);
                Vector3[] vertices = WorldVertices(substrates.ToArray());
                Bounds bounds = new Bounds(vertices[0], Vector3.zero);
                float relief = float.NegativeInfinity;
                Vector3 peak = bounds.center;
                bool finite = true;
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 point = vertex;
                    finite &= Finite(point);
                    bounds.Encapsulate(point);
                    if (point.y > peak.y) peak = point;
                    // Sampling the rendered floor beneath each vertex avoids mistaking
                    // a bank on a slope for a mountain, or comparing nominal heights.
                    float localRelief = point.y - UnderwaterWorld.SampleRenderedSeabedHeight(point.x, point.z);
                    relief = Mathf.Max(relief, localRelief);
                }
                Check(finite, "every actual rock vertex is finite: " + host.name);
                Bounds renderBounds = substrate.bounds;
                for (int i = 1; i < substrates.Count; i++) renderBounds.Encapsulate(substrates[i].bounds);
                // Unity transforms the local AABB, not every mesh vertex, when it
                // computes Renderer.bounds. Rotated asymmetric scans legitimately have
                // a larger conservative world AABB than the exact vertex envelope.
                Bounds expectedRenderBounds = RendererCornerBounds(substrates);
                Check(Vector3.Distance(expectedRenderBounds.center, renderBounds.center) < .002f &&
                    Vector3.Distance(expectedRenderBounds.size, renderBounds.size) < .002f,
                    "renderer bounds agree with independently transformed local bounds: " + host.name);
                Bounds enclosure = renderBounds; enclosure.Expand(.004f);
                Check(enclosure.Contains(bounds.min) && enclosure.Contains(bounds.max),
                    "conservative renderer bounds contain all transformed scan vertices: " + host.name);
                Check(relief > .15f && relief < 27f, "finite meaningful rock height within the authored vertical envelope");
                float footing = UnderwaterWorld.SampleRenderedSeabedHeight(host.footing.x, host.footing.z);
                float aboveFooting = peak.y - footing;
                float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
                minimumRelief = Mathf.Min(minimumRelief, relief); maximumRelief = Mathf.Max(maximumRelief, relief);
                if (!scan) maximumProceduralRelief = Mathf.Max(maximumProceduralRelief, relief);
                minimumFootprint = Mathf.Min(minimumFootprint, footprint); maximumFootprint = Mathf.Max(maximumFootprint, footprint);
                minimumFootingHeight = Mathf.Min(minimumFootingHeight, aboveFooting);
                maximumFootingHeight = Mathf.Max(maximumFootingHeight, aboveFooting);
                var item = new MeasuredMass { bounds = bounds, relief = relief, category = host.category, scan = scan };
                measured.Add(item);
                if (scan)
                    CheckNaturalScan(world, group, scanInfo, bounds, host.category);

                if (host.category == 1)
                {
                    outerCount++;
                    minimumOuterFootprint = Mathf.Min(minimumOuterFootprint, footprint);
                    maximumOuterFootprint = Mathf.Max(maximumOuterFootprint, footprint);
                    if (scan)
                    {
                        outerLarge++; smallestLarge = Mathf.Min(smallestLarge, bounds.size.y);
                        Check(bounds.size.y >= 9f && footprint >= 7f && relief >= 6.5f,
                            "natural outer walls retain genuine breadth and meaningful relief above the rendered floor");
                    }
                    else if (relief >= 15f)
                    {
                        outerLarge++; smallestLarge = Mathf.Min(smallestLarge, relief);
                        Check(bounds.size.y >= 14f && footprint >= 6f,
                            "large outer formations have real vertical extent and physical breadth");
                    }
                    else if (relief >= 3.75f && relief <= 10f) outerMedium++;
                    else if (relief >= .65f && relief <= 3f)
                    { outerSmall++; largestSmall = Mathf.Max(largestSmall, relief); }
                }

                if (host.category == 0 && scan)
                {
                    Vector3 direction = bounds.center - eye;
                    float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                    float flat = new Vector2(direction.x, direction.z).magnitude;
                    float bottom = Mathf.Atan2(bounds.min.y - eye.y, flat) * Mathf.Rad2Deg;
                    float top = Mathf.Atan2(bounds.max.y - eye.y, flat) * Mathf.Rad2Deg;
                    Debug.Log($"[SanctuaryEntryMass] host={host.name} peak={peak:F2} " +
                        $"relief={relief:F2}m aboveFooting={aboveFooting:F2}m yaw={yaw:F1} angularHeight={top-bottom:F1} degrees; " +
                        "geometric angle, not an occlusion or binocular visibility verdict.");
                    if (direction.z > 0f && Mathf.Abs(yaw) <= 65f && top - bottom >= 12f)
                    {
                        leftEntranceFrame |= yaw < 0f;
                        rightEntranceFrame |= yaw > 0f;
                    }
                }
            }

            Check(measured.Count == groups.Count && measured.Count >= 60,
                "composition measurements cover the actual generated rock hosts");
            Check(outerCount == 36, "existing outer bank count is preserved");
            Check(outerLarge >= plannedOuterScans, "all outer regions retain natural wall-scale formations");
            Check(maximumProceduralRelief < 15f, "no old elongated fifteen-to-twenty-six-metre procedural peak remains");
            Check(outerMedium >= 10, "middle-size formations remain between the peaks and low reef shelves");
            Check(outerSmall >= 16, "small reef outcrops dominate by number instead of repeating tall mounds");
            Check(smallestLarge >= largestSmall * 3.5f,
                "natural ten-metre walls still differ materially from the retained small reef outcrops");
            Check(maximumOuterFootprint >= minimumOuterFootprint * 2f,
                "outer reef footprints vary as well as their vertical scale: min=" + minimumOuterFootprint.ToString("F2") +
                " max=" + maximumOuterFootprint.ToString("F2"));
            Check(leftEntranceFrame && rightEntranceFrame,
                "natural ten-metre scan walls frame both entry sides without artificial vertical stretch");

            for (int zone = 1; zone < OceanCoralSanctuary.ExplorationZones.Count; zone++)
            {
                Vector2 centre = new Vector2(world.ActiveSpawn.x, world.ActiveSpawn.z) +
                    OceanCoralSanctuary.ExplorationZones[zone].offset;
                bool hasMountain = false, hasLowReef = false;
                foreach (var item in measured)
                {
                    if (item.category != 1 || Vector2.Distance(centre,
                        new Vector2(item.bounds.center.x, item.bounds.center.z)) > 30f) continue;
                    hasMountain |= item.scan ? item.bounds.size.y >= 9f && item.relief >= 6.5f : item.relief >= 15f;
                    hasLowReef |= item.relief >= .65f && item.relief <= 3f;
                }
                Check(hasMountain && hasLowReef, "mountain and low outcrop coexist around outer region " + zone);
            }

            Debug.Log($"OceanSanctuaryCompositionChecks PASS {assertions} assertions; " +
                $"hosts={measured.Count}, outer large/mid/small={outerLarge}/{outerMedium}/{outerSmall}, " +
                $"actual local-floor relief={minimumRelief:F2}..{maximumRelief:F2}m, " +
                $"peak above host footing={minimumFootingHeight:F2}..{maximumFootingHeight:F2}m, " +
                $"horizontal span={minimumFootprint:F2}..{maximumFootprint:F2}m, " +
                $"outer horizontal span={minimumOuterFootprint:F2}..{maximumOuterFootprint:F2}m. " +
                "Independent detailed mesh measurements; route clearance, art quality and device frame time are separate checks.");
            return assertions;
        }

        static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

        static Bounds RendererCornerBounds(List<Renderer> renderers)
        {
            bool first = true; var result = new Bounds();
            foreach (Renderer renderer in renderers)
            {
                Bounds local = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = renderer.localToWorldMatrix.MultiplyPoint3x4(corner);
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            }
            return result;
        }

        static Vector3[] WorldVertices(Renderer[] renderers)
        {
            var result = new List<Vector3>();
            foreach (Renderer renderer in renderers)
            {
                Check(renderer != null, "measured substrate renderer exists");
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Check(filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable,
                    "actual substrate vertices are readable for independent measurements");
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    result.Add(filter.transform.TransformPoint(vertex));
            }
            Check(result.Count > 0, "measured substrate contains actual vertices");
            return result.ToArray();
        }

        static void CheckNaturalScan(UnderwaterWorld world, LODGroup group,
            OceanCoralSanctuary.ScannedMass info, Bounds measured, int category)
        {
            Check(info.source == "market_mountainside" || info.source == "market_coastal_cliff_02",
                "new mountain host resolves to an admitted natural scan");
            Check(info.uniformScale >= .8f && info.uniformScale <= 1.2f,
                "large formation remains near its genuine metre-scale source");
            Check(Vector3.Distance(info.bounds.center, measured.center) < .003f &&
                Vector3.Distance(info.bounds.size, measured.size) < .003f,
                "scan metadata matches independently measured visible geometry");
            Vector3 expected = info.source == "market_mountainside" ?
                new Vector3(10.21f, 10.52f, 5.25f) : new Vector3(40.93f, 10.05f, 8.65f);
            LOD[] levels = group.GetLODs();
            Bounds localEnvelope = default;
            int previousTriangles = int.MaxValue;
            for (int level = 0; level < levels.Length; level++)
            {
                Vector3[] vertices = WorldVertices(levels[level].renderers);
                Vector3 first = group.transform.InverseTransformPoint(vertices[0]) / info.uniformScale;
                Bounds canonical = new Bounds(first, Vector3.zero);
                bool finite = true;
                foreach (Vector3 vertex in vertices)
                {
                    finite &= Finite(vertex);
                    canonical.Encapsulate(group.transform.InverseTransformPoint(vertex) / info.uniformScale);
                }
                Check(finite, "every admitted substrate vertex is finite");
                for (int axis = 0; axis < 3; axis++)
                {
                    float tolerance = Mathf.Max(.08f, expected[axis] * .025f);
                    Check(canonical.size[axis] >= expected[axis] - tolerance &&
                        canonical.size[axis] <= expected[axis] + tolerance,
                        "opposed natural sheets retain the source envelope for " + info.name + " LOD" + level);
                }
                Check(Mathf.Abs(canonical.center.x) < .04f && Mathf.Abs(canonical.center.z) < .04f &&
                    Mathf.Abs(canonical.min.y) < .04f, "source is XZ-centred with its real base at zero");
                if (level == 0) localEnvelope = canonical;
                else Check(Vector3.Distance(localEnvelope.center, canonical.center) < .04f &&
                    Vector3.Distance(localEnvelope.size, canonical.size) < .08f,
                    "all real source LODs retain one common origin and envelope");
                int triangleCount = 0;
                foreach (Renderer renderer in levels[level].renderers)
                {
                    Vector3 scale = renderer.transform.lossyScale;
                    Check(Mathf.Abs(Mathf.Abs(scale.x) - info.uniformScale) < .0001f &&
                        Mathf.Abs(Mathf.Abs(scale.y) - info.uniformScale) < .0001f &&
                        Mathf.Abs(Mathf.Abs(scale.z) - info.uniformScale) < .0001f,
                        "actual rendered transform has equal scale on all three axes");
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    triangleCount += mesh.triangles.Length / 3;
                    Check(mesh.uv.Length == mesh.vertexCount, "native PBR UVs remain attached to every detailed surface");
                    foreach (Material material in renderer.sharedMaterials)
                        Check(material != null && material.shader != null && material.shader.isSupported &&
                            material.renderQueue <= 2500, "natural visual scan sheets use supported opaque materials");
                }
                Check(triangleCount > 0 && triangleCount < previousTriangles,
                    "each real fallback reduces the measured geometry budget");
                if (level == 0) Check(triangleCount == info.nearTriangles, "near scan triangle counter is measured honestly");
                previousTriangles = triangleCount;
                CheckOpposedSheets(group.transform, levels[level].renderers, info, expected, level);
            }
            MeshCollider[] colliders = group.GetComponentsInChildren<MeshCollider>(true);
            Check(colliders.Length > 0, "natural host has real static collision");
            GameObject collisionSource = Resources.Load<GameObject>("RealisticReef/" + info.source + "_collision");
            Check(collisionSource != null && collisionSource.GetComponentsInChildren<Renderer>(true).Length == 0,
                "independent admitted collision source has no renderer or visible proxy material");
            var economical = new HashSet<Mesh>();
            foreach (MeshFilter filter in collisionSource.GetComponentsInChildren<MeshFilter>(true)) economical.Add(filter.sharedMesh);
            var collisionFilters = new List<MeshFilter>(); int hullTriangles = 0;
            foreach (MeshCollider collider in colliders)
            {
                Check(!collider.convex && collider.sharedMesh != null && economical.Contains(collider.sharedMesh),
                    "physics uses the independent low-poly closed hull, with no visual open sheet or old procedural collider");
                Check(collider.GetComponent<Renderer>() == null, "collision hull is never rendered as an artificial rock surface");
                MeshFilter filter = collider.GetComponent<MeshFilter>();
                Check(filter != null && filter.sharedMesh == collider.sharedMesh && collider.sharedMesh.isReadable,
                    "actual independent collision vertices remain inspectable");
                collisionFilters.Add(filter); hullTriangles += collider.sharedMesh.triangles.Length / 3;
            }
            Check(hullTriangles > 0 && hullTriangles <= 240, "independent closed collision stays within its actual 240-triangle budget");
            CheckClosedSurface(collisionFilters.ToArray(), info.name + " collision hull");
            int colonies = 0;
            var colonyRoots = new List<Transform>();
            foreach (LODGroup child in group.GetComponentsInChildren<LODGroup>(true))
                if (child != group && child.name.StartsWith("Scanned wall colony ", StringComparison.Ordinal))
                { colonies++; colonyRoots.Add(child.transform); }
            int limit = category == 0 ? 10 : category == 1 ? 4 : 5;
            Check(colonies >= limit * .75f && colonies <= limit,
                "each actual natural wall receives bounded visible small colonies, without multiplying the global budget");
            var frontRenderers = new List<Renderer>();
            foreach (Renderer renderer in levels[0].renderers)
                if (SheetRoot(group.transform, renderer.transform).name.StartsWith("Photographed front ", StringComparison.Ordinal))
                    frontRenderers.Add(renderer);
            CheckScannedColonyRoots(frontRenderers.ToArray(), colonyRoots, info.name);
            Physics.SyncTransforms();
            Check(HasObserver(world, group.transform, localEnvelope, info.uniformScale, -1f, out Vector3 negativeView),
                "an actual capsule can inspect one side of the scan without entering its body: " + info.name);
            Check(HasObserver(world, group.transform, localEnvelope, info.uniformScale, 1f, out Vector3 positiveView),
                "an actual capsule can inspect the opposite scan side/back: " + info.name);
            Debug.Log($"[NaturalScanComposition] host={info.name} source={info.source} scale={info.uniformScale:F2} " +
                $"canonical={localEnvelope.size:F2}m collisionTriangles={hullTriangles} " +
                $"negativeLocalZView={negativeView:F2} positiveLocalZView={positiveView:F2}; " +
                "three LODs of two opposed natural scan sheets, independent closed collision. " +
                "Clear observer capsules permit rendered front/back review; connectivity and visible seams remain separate checks.");
        }

        static Transform SheetRoot(Transform host, Transform child)
        {
            while (child.parent != null && child.parent != host) child = child.parent;
            return child;
        }

        static void CheckOpposedSheets(Transform host, Renderer[] renderers,
            OceanCoralSanctuary.ScannedMass info, Vector3 expected, int level)
        {
            var pieces = new Dictionary<Transform, List<Renderer>>();
            foreach (Renderer renderer in renderers)
            {
                Transform piece = SheetRoot(host, renderer.transform);
                if (!pieces.TryGetValue(piece, out var list)) { list = new List<Renderer>(); pieces.Add(piece, list); }
                list.Add(renderer);
            }
            Check(pieces.Count == 2, "each visual LOD has two actual opposed scan sheets, not a claimed watertight single scan");
            bool front = false, opposite = false;
            foreach (var pair in pieces)
            {
                bool first = pair.Key.name.StartsWith("Photographed front ", StringComparison.Ordinal);
                bool back = pair.Key.name.StartsWith("Opposed scan ", StringComparison.Ordinal);
                Check(first || back, "visual pieces retain their explicit front/opposed role");
                front |= first; opposite |= back;
                Check(pair.Key.localPosition.sqrMagnitude < 1e-8f &&
                    Quaternion.Angle(pair.Key.localRotation, Quaternion.Euler(0, first ? 0 : 180, 0)) < .01f,
                    "opposed natural sheets share an origin and differ only by a half-turn");
                Vector3[] vertices = WorldVertices(pair.Value.ToArray());
                Bounds canonical = new Bounds(pair.Key.InverseTransformPoint(vertices[0]), Vector3.zero);
                foreach (Vector3 vertex in vertices) canonical.Encapsulate(pair.Key.InverseTransformPoint(vertex));
                Check(Vector3.Distance(canonical.size, expected) < .14f,
                    "each individual sheet preserves the real source dimensions at LOD" + level + ": " + info.name);
            }
            Check(front && opposite, "both actual photographed front and opposed scan are present");
        }

        static void CheckScannedColonyRoots(Renderer[] substrate, List<Transform> colonies, string label)
        {
            // Read the actually rendered LOD0, rather than accepting attachment metadata.
            // The short normal segment must cross a real triangle near each visible root.
            var faces = new List<Vector3>();
            foreach (Renderer renderer in substrate)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter.sharedMesh; Vector3[] vertices = mesh.vertices;
                foreach (int index in mesh.triangles) faces.Add(filter.transform.TransformPoint(vertices[index]));
            }
            foreach (Transform colony in colonies)
            {
                Vector3 up = colony.up.normalized, origin = colony.position - up * .06f;
                bool contact = false;
                for (int triangle = 0; triangle < faces.Count; triangle += 3)
                {
                    Vector3 a = faces[triangle], ab = faces[triangle + 1] - a, ac = faces[triangle + 2] - a;
                    Vector3 normal = Vector3.Cross(ab, ac);
                    if (normal.sqrMagnitude < 1e-12f || Vector3.Dot(normal.normalized, up) < .8f) continue;
                    Vector3 cross = Vector3.Cross(up, ac); float determinant = Vector3.Dot(ab, cross);
                    if (Mathf.Abs(determinant) < 1e-9f) continue;
                    float inverse = 1f / determinant; Vector3 fromA = origin - a;
                    float u = Vector3.Dot(fromA, cross) * inverse;
                    if (u < -.0001f || u > 1.0001f) continue;
                    Vector3 q = Vector3.Cross(fromA, ab); float v = Vector3.Dot(up, q) * inverse;
                    if (v < -.0001f || u + v > 1.0001f) continue;
                    float distance = Vector3.Dot(ac, q) * inverse;
                    if (distance >= 0f && distance <= .12f) { contact = true; break; }
                }
                Check(contact, "scanned small coral root touches an actual detailed rock face with its matching normal: " + label);
            }
        }

        static void CheckClosedSurface(MeshFilter[] filters, string label)
        {
            // Position welding deliberately ignores UV seams. Read the independent
            // physics mesh; visible scan sheets intentionally make no watertight claim.
            var points = new Dictionary<Vector3Int, int>();
            var edges = new Dictionary<ulong, int>();
            foreach (MeshFilter filter in filters)
            {
                Mesh mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices; var remap = new int[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 p = filter.transform.TransformPoint(vertices[i]);
                    var key = new Vector3Int(Mathf.RoundToInt(p.x * 10000), Mathf.RoundToInt(p.y * 10000), Mathf.RoundToInt(p.z * 10000));
                    if (!points.TryGetValue(key, out int index)) { index = points.Count; points.Add(key, index); }
                    remap[i] = index;
                }
                int[] indices = mesh.triangles;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int a = remap[indices[i]], b = remap[indices[i + 1]], c = remap[indices[i + 2]];
                    if (a == b || b == c || c == a) continue;
                    AddEdge(edges, a, b); AddEdge(edges, b, c); AddEdge(edges, c, a);
                }
            }
            int open = 0;
            foreach (int count in edges.Values) if (count == 1) open++;
            Check(open == 0, "actual independent collision hull has no open welded boundary: " + label);
        }

        static void AddEdge(Dictionary<ulong, int> edges, int a, int b)
        {
            ulong key = ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
            edges.TryGetValue(key, out int count); edges[key] = count + 1;
        }

        static bool HasObserver(UnderwaterWorld world, Transform host, Bounds local, float scale, float side, out Vector3 view)
        {
            view = default;
            var hits = new Collider[64];
            foreach (float extra in new[] { .85f, 1.8f, 3f })
            foreach (float across in new[] { 0f, -.3f, .3f, -.46f, .46f })
            {
                Vector3 at = host.TransformPoint(new Vector3((local.center.x + across * local.size.x) * scale, 0,
                    (local.center.z + side * local.extents.z) * scale + side * extra));
                if (Mathf.Abs(at.x - world.ActiveSpawn.x) > OceanEnvironmentProfile.SanctuaryPlayableHalfExtent - .5f ||
                    Mathf.Abs(at.z - world.ActiveSpawn.z) > OceanEnvironmentProfile.SanctuaryPlayableHalfExtent - .5f) continue;
                float floor = UnderwaterWorld.SampleRenderedSeabedHeight(at.x, at.z);
                int count = Physics.OverlapCapsuleNonAlloc(new Vector3(at.x, floor + .34f, at.z),
                    new Vector3(at.x, floor + 1.38f, at.z), .23f, hits, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                Check(count < hits.Length, "observer collision buffer is sufficient");
                bool blocked = false;
                for (int i = 0; i < count; i++) if (hits[i].transform.IsChildOf(world.ActiveReefRoot)) { blocked = true; break; }
                if (!blocked) { view = new Vector3(at.x, floor + 1.5f, at.z); return true; }
            }
            return false;
        }
    }
}
#endif

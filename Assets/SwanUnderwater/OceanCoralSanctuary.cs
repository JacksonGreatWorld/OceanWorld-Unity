using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>
    /// Authored level-six composition in world metres. Only environment geometry lives here:
    /// the caller retains ownership of the camera, locomotion, campaign and fish simulation.
    /// Build once, retain its parent between level resets, and never move the built root to swim.
    /// </summary>
    public static class OceanCoralSanctuary
    {
        sealed class Scan
        {
            public readonly GameObject[] models = new GameObject[3];
            public readonly ReefSupportGeometry[] geometry = new ReefSupportGeometry[3];
        }
        sealed class Island
        {
            public Vector3 center;
            public float width, depth, height, angle, seed;
            public bool bluff;
            public int profile;
            public Func<float, float, float> floor;
            public float Surface(float x,float z)
            {
                float relief=Relief(x,z,out float inset);
                float weathering=.075f*Mathf.Sin(x*2.7f+z*1.8f+seed)*Mathf.Clamp01(inset*5f);
                return floor(x,z)-.13f+height*relief+weathering;
            }
            float Relief(float x,float z,out float inset)
            {
                float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                float dx = x - center.x, dz = z - center.z;
                float u = (dx * c + dz * s) / width;
                float v = (-dx * s + dz * c) / depth;
                // Off-centre, eroded shelves read as layered reef geology rather than smooth
                // conical mounds. The same equation authors every LOD and contact surface.
                float r = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 1.72f) + Mathf.Pow(Mathf.Abs(v), 1.72f), 1f / 1.72f);
                float azimuth = Mathf.Atan2(v, u);
                float edge = 1f + .12f * Mathf.Sin(azimuth * 3f + seed) + .055f * Mathf.Sin(azimuth * 7f - seed);
                inset = Mathf.Clamp01(1f - r / edge);
                float erosion = .025f * Mathf.Sin(u * 13f + seed) * Mathf.Cos(v * 11f - seed);
                float shelf = bluff ? .82f * Smooth(.025f,.14f,inset+erosion) +
                    .18f * Smooth(-.15f,.55f,u + .3f*v) * Smooth(.14f,.24f,inset) :
                    .22f * Smooth(.018f, .085f, inset + erosion) +
                    .32f * Smooth(.19f, .29f, inset + erosion) +
                    .28f * Smooth(.44f, .54f, inset + erosion) +
                    .18f * Smooth(.72f, .83f, inset + erosion);
                if(bluff && profile>0)
                {
                    // Outer buttresses rise through eroded shoulders instead of a single
                    // vertical 82%-height wall. The entrance's five retained bluffs stay intact.
                    shelf=.46f*Smooth(.025f,.22f,inset+erosion)+
                        .32f*Smooth(.24f,.45f,inset+erosion*.7f)+
                        .22f*Smooth(.53f,.76f,inset+erosion*.5f);
                }
                // Outer banks vary their mass, not just their noise. The original footprint and
                // upper height remain unchanged; all LODs and the collider use this equation.
                if (profile == 1) // An elongated, broken ledge with a raised back wall.
                    shelf *= .29f + .71f * Smooth(-.39f,.13f,v + .17f*Mathf.Sin(u*3.9f+seed));
                else if (profile == 2) // Two eroded crowns divided by a real low saddle.
                {
                    float a=Mathf.Exp(-Mathf.Pow((u+.37f)*2.7f,2)-Mathf.Pow((v-.14f)*1.6f,2));
                    float b=Mathf.Exp(-Mathf.Pow((u-.40f)*2.7f,2)-Mathf.Pow((v+.19f)*1.8f,2));
                    shelf *= .20f + .80f*Mathf.Clamp01(Mathf.Max(a,b)*1.08f);
                }
                else if (profile == 3) // Asymmetric tall buttress, tapering to one low shoulder.
                {
                    float spine=Mathf.Exp(-Mathf.Pow((u+.13f-v*.29f)*2.3f,2));
                    shelf *= .19f+.81f*spine*(.43f+.57f*Smooth(-.62f,.35f,v));
                }
                else if (profile == 4) // Tilted limestone slab, cut by an off-centre erosion seam.
                {
                    float plane=.26f+.74f*Smooth(-.58f,.18f,u-.32f*v);
                    float cut=.38f*Mathf.Exp(-Mathf.Pow((v-.22f-u*.17f)*8f,2))*Smooth(-.1f,.55f,u);
                    shelf *= Mathf.Max(.16f,plane-cut);
                }
                float strata = .93f + .11f * Mathf.PerlinNoise(x * .47f + seed, z * .47f + seed);
                return shelf*strata;
            }
            public void CalibrateCrown(int rings,int sides)
            {
                // A narrow buttress mask previously reduced an "18m" nominal bank to a
                // much smaller visible hill. Calibrate ONCE against the exact LOD0 sample
                // coordinates; every LOD/collider/placement then shares the same height.
                float peak=0;
                for(int ring=0;ring<=rings;ring++)
                {
                    float r=Mathf.Max(.002f,ring/(float)rings)*1.13f;
                    for(int side=0;side<sides;side++)
                    {
                        float a=side/(float)sides*Mathf.PI*2f;
                        peak=Mathf.Max(peak,Relief(center.x+Mathf.Cos(a)*width*r,
                            center.z+Mathf.Sin(a)*depth*r,out _));
                    }
                }
                peak=Mathf.Max(peak,Relief(center.x,center.z,out _));
                if(peak>.001f)height/=peak;
            }
            static float Smooth(float a,float b,float value)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,value));
        }
        sealed class Geometry
        {
            public readonly List<Vector3> positions = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Color> colors = new List<Color>();
            public readonly List<int> indices = new List<int>();
            public int Add(Vector3 p, Vector2 t, Color c)
            { int i = positions.Count; positions.Add(p); uv.Add(t); colors.Add(c); return i; }
            public void Quad(int a, int b, int c, int d)
            { indices.Add(a); indices.Add(b); indices.Add(c); indices.Add(a); indices.Add(c); indices.Add(d); }
            public Mesh Mesh(string name,bool weldSeamNormals=false)
            {
                var mesh = new Mesh { name = name, indexFormat = positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(positions); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.SetTriangles(indices, 0);
                mesh.RecalculateNormals();
                if(weldSeamNormals)
                {
                    // UV seams remain separate, but their normals must not make tube-shaped
                    // living organisms look like faceted strips or segmented plastic.
                    Vector3[] normals=mesh.normals;
                    var sums=new Dictionary<Vector3Int,Vector3>();
                    for(int i=0;i<positions.Count;i++)
                    {
                        Vector3 p=positions[i];var key=new Vector3Int(Mathf.RoundToInt(p.x*10000),Mathf.RoundToInt(p.y*10000),Mathf.RoundToInt(p.z*10000));
                        sums.TryGetValue(key,out Vector3 previous);sums[key]=previous+normals[i];
                    }
                    for(int i=0;i<positions.Count;i++)
                    {
                        Vector3 p=positions[i];var key=new Vector3Int(Mathf.RoundToInt(p.x*10000),Mathf.RoundToInt(p.y*10000),Mathf.RoundToInt(p.z*10000));
                        normals[i]=sums[key].normalized;
                    }
                    mesh.normals=normals;
                }
                mesh.RecalculateTangents(); mesh.RecalculateBounds();
                return mesh;
            }
        }

        static readonly List<ReefHabitat.CoralPocket> pockets = new List<ReefHabitat.CoralPocket>();
        static readonly List<Vector3> vents = new List<Vector3>();
        static readonly List<Bounds> solids = new List<Bounds>();
        static readonly List<Vector3> hiddenCoinCandidates = new List<Vector3>();
        public static IReadOnlyList<ReefHabitat.CoralPocket> Pockets => pockets;
        public static IReadOnlyList<Vector3> BubbleSources => vents;
        public static IReadOnlyList<Bounds> SolidBounds => solids;
        /// <summary>Grounded back sides of the entrance terraces; gameplay still verifies sightline and reachability.</summary>
        public static IReadOnlyList<Vector3> HiddenCoinCandidates => hiddenCoinCandidates;
        public static Vector3 ArchCenter { get; private set; }
        public static int CoralCount { get; private set; }
        public static int LivingCoralTriangles { get; private set; }
        public static int GeneratedRockTriangles { get; private set; }
        public readonly struct SurfaceColony
        {
            public readonly Vector3 surfacePoint, root, normal;
            public readonly int style,hostSeed,firstVertex,vertexCount;
            public readonly float diameter;
            public SurfaceColony(Vector3 surface,Vector3 root,Vector3 normal,int style,float diameter,
                int hostSeed,int firstVertex,int vertexCount)
            {
                surfacePoint=surface;this.root=root;this.normal=normal;this.style=style;this.diameter=diameter;
                this.hostSeed=hostSeed;this.firstVertex=firstVertex;this.vertexCount=vertexCount;
            }
        }
        static readonly List<SurfaceColony> surfaceColonies=new List<SurfaceColony>(1100);
        static readonly int[] rockProfiles=new int[5];
        public static IReadOnlyList<SurfaceColony> SurfaceColonies=>surfaceColonies;
        public static IReadOnlyList<int> RockProfileCounts=>rockProfiles;
        public static int SurfaceGardenTriangles { get; private set; }
        public static int SurfaceGardenRenderers { get; private set; }
        public readonly struct ReefMass
        {
            public readonly string name;
            public readonly Bounds bounds;
            public readonly Vector3 footing;
            public readonly float targetCrown,peakAboveFooting;
            public readonly int category,profile;
            public ReefMass(string name,Bounds bounds,Vector3 footing,float target,int category,int profile)
            {
                this.name=name;this.bounds=bounds;this.footing=footing;targetCrown=target;
                peakAboveFooting=bounds.max.y-footing.y;this.category=category;this.profile=profile;
            }
        }
        // Categories: 0 entrance bank, 1 exploration bank, 2 outer buttress, 3 entrance bluff.
        // Bounds are measured from the actual detailed mesh, never a configuration estimate.
        static readonly List<ReefMass> reefMasses=new List<ReefMass>(80);
        public static IReadOnlyList<ReefMass> ReefMasses=>reefMasses;
        public readonly struct ScannedMass
        {
            public readonly string name,source;
            public readonly Bounds bounds;
            public readonly float uniformScale;
            public readonly int nearTriangles;
            public ScannedMass(string name,string source,Bounds bounds,float scale,int triangles)
            {this.name=name;this.source=source;this.bounds=bounds;uniformScale=scale;nearTriangles=triangles;}
        }
        static readonly List<ScannedMass> scannedMasses=new List<ScannedMass>(16);
        public static IReadOnlyList<ScannedMass> ScannedMasses=>scannedMasses;
        public static int ScannedCoralCount { get; private set; }
        /// <summary>Immutable authoring coordinates in metres, relative to the shared spawn.</summary>
        public readonly struct ExplorationZone
        {
            public readonly string name;
            public readonly Vector2 offset;
            public readonly float radius;
            public ExplorationZone(string name, float x, float z, float radius)
            { this.name = name; offset = new Vector2(x, z); this.radius = radius; }
        }
        public static IReadOnlyList<ExplorationZone> ExplorationZones { get; } = Array.AsReadOnly(new[] {
            new ExplorationZone("Entrance coral garden", 0, 12, 25),
            new ExplorationZone("Western limestone terraces", -65, 20, 23),
            new ExplorationZone("Southwest coral courtyards", -48, -40, 23),
            new ExplorationZone("Southern seagrass basin", 4, -66, 24),
            new ExplorationZone("Southeast sheltered lagoon", 57, -48, 23),
            new ExplorationZone("Eastern sea-fan ravine", 75, 5, 23),
            new ExplorationZone("Northern split-reef canyon", -7, 80, 24)
        });
        public static IReadOnlyList<Vector2> ExplorationRouteOffsets { get; } = Array.AsReadOnly(new[] {
            new Vector2(0,0), new Vector2(0,10), new Vector2(0,23), new Vector2(0,31),
            new Vector2(0,38.2f), new Vector2(-4.5f,40.3f), new Vector2(-5,46),
            new Vector2(-16,53), new Vector2(-31,50), new Vector2(-47,39),
            new Vector2(-65,20), new Vector2(-74,-5),
            new Vector2(-64,-28), new Vector2(-45,-48), new Vector2(-12,-64), new Vector2(22,-70),
            new Vector2(52,-55), new Vector2(74,-28), new Vector2(79,3), new Vector2(64,28),
            new Vector2(44,52), new Vector2(15,74), new Vector2(-15,89), new Vector2(-44,79),
            new Vector2(-65,58), new Vector2(-47,39)
        });
        // A southern exit creates a second way out of the entrance garden; it keeps clear
        // of the retained dense terrace composition to the north and east of the arch.
        public static IReadOnlyList<Vector2> ExplorationShortcutOffsets { get; } = Array.AsReadOnly(new[] {
            new Vector2(0,0), new Vector2(0,-20), new Vector2(22,-37), new Vector2(52,-55)
        });
        public readonly struct ExplorationBank
        {
            public readonly Vector2 offset;
            public readonly float width, depth, height;
            public readonly int zone;
            public ExplorationBank(float x,float z,float width,float depth,float height,int zone)
            { offset=new Vector2(x,z);this.width=width;this.depth=depth;this.height=height;this.zone=zone; }
        }
        // Broad masses flank the routes rather than form a uniformly populated circular wall.
        // Their full eroded mesh footprints retain at least 3.5 m of water around the outer
        // route centreline. Small living colonies can decorate these faces without sealing it.
        public static IReadOnlyList<ExplorationBank> ExplorationBanks { get; } = Array.AsReadOnly(new[] {
            new ExplorationBank(-46.00f,20.00f,6.00f,4.50f,2.10f,0),
            new ExplorationBank(-81.40f,35.00f,4.60f,3.50f,1.35f,0),
            new ExplorationBank(-85.00f,2.00f,7.80f,5.50f,7.40f,0),
            new ExplorationBank(-54.20f,-3.00f,11.80f,7.80f,20.00f,0),
            new ExplorationBank(-76.00f,46.40f,5.00f,4.20f,2.10f,0),
            new ExplorationBank(-33.50f,-27.80f,7.20f,5.40f,2.20f,1),
            new ExplorationBank(-60.20f,-54.60f,5.20f,4.40f,1.65f,1),
            new ExplorationBank(-41.70f,-67.90f,4.90f,3.90f,1.55f,1),
            new ExplorationBank(-22.70f,-42.10f,10.50f,7.80f,18.50f,1),
            new ExplorationBank(-73.30f,-42.30f,6.30f,5.30f,6.40f,1),
            new ExplorationBank(7.20f,-47.30f,5.20f,4.80f,2.30f,2),
            new ExplorationBank(-13.60f,-79.60f,7.20f,5.00f,7.80f,2),
            new ExplorationBank(30.00f,-80.00f,11.00f,5.50f,22.00f,2),
            new ExplorationBank(26.80f,-83.30f,4.40f,3.90f,1.35f,2),
            new ExplorationBank(-7.00f,-43.10f,6.40f,5.70f,2.10f,2),
            new ExplorationBank(47.40f,-31.60f,13.50f,9.70f,19.50f,3),
            new ExplorationBank(52.30f,-69.70f,5.20f,3.90f,1.80f,3),
            new ExplorationBank(69.30f,-62.50f,6.80f,5.40f,7.80f,3),
            new ExplorationBank(85.60f,-46.60f,5.40f,4.40f,1.25f,3),
            new ExplorationBank(60.40f,-19.60f,7.40f,5.20f,2.40f,3),
            new ExplorationBank(57.10f,11.40f,10.00f,7.50f,24.00f,4),
            new ExplorationBank(81.40f,29.60f,4.70f,4.20f,2.20f,4),
            new ExplorationBank(93.70f,1.90f,6.90f,5.70f,6.60f,4),
            new ExplorationBank(95.90f,24.50f,6.80f,4.10f,1.35f,4),
            new ExplorationBank(89.80f,-15.70f,5.40f,4.40f,2.45f,4),
            new ExplorationBank(-18.00f,102.90f,5.40f,4.10f,2.10f,5),
            new ExplorationBank(-30.00f,96.00f,12.00f,5.00f,21.00f,5),
            new ExplorationBank(7.80f,59.30f,5.00f,4.90f,1.65f,5),
            new ExplorationBank(-21.40f,67.60f,6.20f,4.80f,7.00f,5),
            new ExplorationBank(-0.70f,97.90f,4.70f,4.10f,2.35f,5),
            new ExplorationBank(-84.60f,-23.30f,6.80f,5.20f,6.50f,6),
            new ExplorationBank(40.90f,29.10f,6.60f,4.80f,5.20f,6),
            new ExplorationBank(67.10f,50.90f,6.90f,4.90f,7.30f,6),
            new ExplorationBank(-7.60f,66.30f,5.80f,4.50f,5.10f,6),
            new ExplorationBank(-42.50f,56.50f,7.00f,4.80f,6.80f,6),
            new ExplorationBank(-66.50f,80.50f,7.20f,5.10f,8.00f,6)
        });

        // Multipliers retain the scanned assets' fine albedo detail. Rust/ochre colonies
        // sit against cool limestone instead of lifting every coral towards pastel white.
        static readonly Color[] palette = {
            new Color(1.08f,.83f,.63f), new Color(1.06f,.72f,.64f),
            new Color(.94f,.93f,.69f), new Color(.85f,.75f,.79f),
            new Color(.99f,.87f,.69f), new Color(.70f,.92f,.88f)
        };
        static readonly Color[] livingPalette = {
            new Color(.77f,.39f,.21f), new Color(.83f,.56f,.29f),
            new Color(.59f,.36f,.28f), new Color(.56f,.51f,.34f),
            new Color(.45f,.54f,.47f), new Color(.65f,.40f,.37f)
        };
        static readonly int[] colonySpecies = { 0, 1, 3, 0, 2, 1, 0, 3 };
        static readonly int[] explorationProfiles = { 1, 2, 3, 4, 1, 2, 3, 4, 2, 1, 3, 4, 4, 2, 3, 1, 1, 2, 3, 4, 2, 4, 3, 1, 1, 2, 4, 3, 1, 2, 3, 4, 1, 2, 3, 4 };

        public static void Build(Transform parent, Func<float,float,float> floor, Vector3 spawn)
        {
            IEnumerator work = BuildRoutine(parent, floor, spawn, false, null);
            try { while (work.MoveNext()) { } }
            finally { (work as IDisposable)?.Dispose(); }
        }
        public static IEnumerator BuildStaged(Transform parent, Func<float,float,float> floor, Vector3 spawn,
            Action<float> progress = null) => BuildRoutine(parent, floor, spawn, true, progress);

        static IEnumerator BuildRoutine(Transform parent, Func<float,float,float> floor, Vector3 spawn,
            bool staged, Action<float> progress)
        {
            if (parent == null || floor == null) throw new ArgumentNullException(parent == null ? nameof(parent) : nameof(floor));
            pockets.Clear(); vents.Clear(); solids.Clear(); hiddenCoinCandidates.Clear(); CoralCount = 0; LivingCoralTriangles = 0; GeneratedRockTriangles = 0;
            surfaceColonies.Clear();reefMasses.Clear();scannedMasses.Clear();ScannedCoralCount=0;
            Array.Clear(rockProfiles,0,rockProfiles.Length);SurfaceGardenTriangles=0;SurfaceGardenRenderers=0;
            progress?.Invoke(0);
            var root = new GameObject("Coral Sanctuary").transform;
            root.SetParent(parent, false);
            // Geometry is authored with world coordinates, independent of the owner transform.
            root.position = Vector3.zero; root.rotation = Quaternion.identity; root.localScale = Vector3.one;
            var resources = root.gameObject.AddComponent<OceanSanctuaryResources>();
            var proximity = root.gameObject.AddComponent<ReefProximityLod>();
            var corals = new List<Scan>(); var rocks = new List<Scan>();
            string[] stems = { "coral_cuneata", "coral_spicifera", "coral_brain", "coral_palmata", "rock_coast_05", "rock_coast_02" };
            int assetCount = 0;
            foreach (string stem in stems)
            {
                var scan = new Scan(); bool rock = stem.StartsWith("rock_", StringComparison.Ordinal);
                for (int lod = 0; lod < (rock ? 2 : 3); lod++)
                {
                    string path = "RealisticReef/" + stem + "_lod" + lod;
                    if (staged)
                    {
                        ResourceRequest request = Resources.LoadAsync<GameObject>(path);
                        yield return request; scan.models[lod] = request.asset as GameObject;
                    }
                    else scan.models[lod] = Resources.Load<GameObject>(path);
                    if (scan.models[lod] == null) throw new InvalidOperationException("[Sanctuary] Missing source scan " + path);
                    scan.geometry[lod] = ReefSupportGeometry.Read(scan.models[lod], false);
                    progress?.Invoke(.16f * ++assetCount / 16f);
                    if (staged) yield return null;
                }
                (rock ? rocks : corals).Add(scan);
            }
            OceanScannedReefMass.Assets market=null;
            IEnumerator marketLoad=OceanScannedReefMass.Load(staged,
                value=>progress?.Invoke(.16f+.10f*value),value=>market=value);
            try {while(marketLoad.MoveNext())yield return marketLoad.Current;}
            finally {(marketLoad as IDisposable)?.Dispose();}
            Shader shader = Resources.Load<Shader>("Shaders/OceanSanctuaryOrganic");
            if (shader == null) throw new InvalidOperationException("[Sanctuary] Missing organic reef shader");
            var stone = new Material(shader) { name = "Sanctuary limestone", enableInstancing = true };
            stone.SetTexture("_BaseMap", Resources.Load<Texture2D>("Textures/Rock/Rock019_Color"));
            stone.SetTexture("_BumpMap", Resources.Load<Texture2D>("Textures/Rock/Rock019_NormalGL"));
            stone.SetColor("_BaseColor", new Color(.75f, .76f, .70f)); stone.SetFloat("_BumpScale", .95f);
            stone.SetFloat("_WorldMapping", 1f); stone.SetFloat("_WorldScale", .85f);
            stone.SetFloat("_Smoothness", .22f);
            stone.SetColor("_WaterBottomColor", OceanWaterOptics.Bottom(OceanEnvironmentKind.CoralSanctuary));
            resources.Own(stone);
            var living = new Material(shader) { name = "Sanctuary living coral", enableInstancing = true };
            living.SetFloat("_Sway", .025f); living.SetFloat("_WorldMapping", 0f);
            living.SetFloat("_Smoothness", .30f);
            living.SetColor("_WaterBottomColor", OceanWaterOptics.Bottom(OceanEnvironmentKind.CoralSanctuary));
            living.SetTexture("_BumpMap",Resources.Load<Texture2D>("Textures/Rock/Rock019_NormalGL"));
            living.SetFloat("_BumpScale", .28f); resources.Own(living);
            // Reuse the admitted scanned coral's 4K maps. White diffuse made the broad
            // attached plates read as pastel paper even when their geometry was grounded.
            var surfaceGrowth=new Material(living){name="Sanctuary scanned surface growth",enableInstancing=true};
            Material scanMaterial=corals[0].models[0].GetComponentInChildren<Renderer>().sharedMaterial;
            if(scanMaterial.HasProperty("baseColorTexture"))
                surfaceGrowth.SetTexture("_BaseMap",scanMaterial.GetTexture("baseColorTexture"));
            else if(scanMaterial.HasProperty("_BaseMap"))
                surfaceGrowth.SetTexture("_BaseMap",scanMaterial.GetTexture("_BaseMap"));
            if(scanMaterial.HasProperty("normalTexture"))
                surfaceGrowth.SetTexture("_BumpMap",scanMaterial.GetTexture("normalTexture"));
            else if(scanMaterial.HasProperty("_BumpMap"))
                surfaceGrowth.SetTexture("_BumpMap",scanMaterial.GetTexture("_BumpMap"));
            surfaceGrowth.SetFloat("_BumpScale",.68f);surfaceGrowth.SetFloat("_Sway",.016f);
            surfaceGrowth.SetFloat("_GrowthPigment",1f);
            surfaceGrowth.SetTextureScale("_BaseMap",new Vector2(2.4f,2.4f));resources.Own(surfaceGrowth);
            var cache = new Dictionary<string, Material>();
            var rng = new System.Random(6102026);
            var ambientFootprints = new List<Island>(112);

            ArchCenter = new Vector3(spawn.x, floor(spawn.x, spawn.z + 17f), spawn.z + 17f);
            ReefSupportGeometry archSupport = CreateArch(root, ArchCenter, stone, resources, proximity, surfaceGrowth, floor);
            if (staged) { progress?.Invoke(.27f); yield return null; }

            // A narrow foreground frame opens into alternating coves, then the arch and
            // separate far pillars. The water between them remains a real traversable volume.
            Vector4[] layout = {
                new Vector4(-5.1f,1.8f,1.2f,3.8f),new Vector4(6.2f,3.2f,2.2f,3.8f),
                new Vector4(-7.2f,7.1f,6.8f,4.1f),new Vector4(5.7f,9.0f,1.3f,3.1f),
                new Vector4(-8.4f,13.6f,3.6f,4.2f),new Vector4(9.0f,15.8f,5.4f,4.1f),
                new Vector4(-7.8f,23.0f,2.1f,3.5f),new Vector4(9.4f,25.2f,6.4f,4.4f),
                new Vector4(-14.0f,25.4f,18.0f,4.6f),new Vector4(-15.0f,12.0f,9.0f,3.5f),
                new Vector4(15.9f,13.8f,21.0f,4.4f),new Vector4(16.3f,31.5f,4.0f,4.6f),
                new Vector4(-16.0f,-6.0f,2.1f,4.2f),new Vector4(14.0f,-5.5f,3.0f,3.6f),
                new Vector4(-23.0f,34.0f,6.4f,4.9f),new Vector4(25.0f,35.2f,16.0f,5.0f),
                new Vector4(-4.8f,33.5f,1.35f,3.2f),new Vector4(6.0f,40.0f,7.5f,3.6f)
            };
            int[] entranceProfiles={0,0,1,0,2,1,0,2,3,1,3,4,0,0,2,4,0,3};
            for (int i = 0; i < layout.Length; i++)
            {
                Vector4 p = layout[i];
                var island = new Island { center = spawn + new Vector3(p.x, 0, p.y),
                    width = p.w, depth = p.w * Range(rng, .73f, 1.05f), height = p.z,
                    angle = Range(rng, -.5f, .5f), seed = i * 7.31f + 3f, floor = floor,profile=entranceProfiles[i] };
                Func<float,float,float> supportSurface;
                if(i==8||i==10||i==15)
                {
                    // Replace the old 18/21/16 m procedural peaks and their colliders together.
                    // These are real 10.5 m scans at native proportions, not stretched coral.
                    supportSurface=CreateScannedMass(market,market.mountainside,root,"Reef terrace "+i,
                        island.center,i==8?1f:i==10?1.08f:.96f,i==8?-7f:i==10?8f:-14f,0,830+i,
                        floor,resources,proximity,ambientFootprints);
                }
                else
                {
                    ambientFootprints.Add(island);
                    supportSurface=CreateIsland(root,island,i,stone,resources,proximity,false,surfaceGrowth);
                }
                int count = i < 6 ? 8 : (i < 12 ? 6 : 4);
                var livingGeometry = new Geometry();
                for (int c = 0; c < count; c++)
                {
                    float angle = c * 2.39996f + Range(rng, -.35f, .35f);
                    float radius = c == 0 ? .12f : Mathf.Sqrt(Range(rng, .08f, .72f));
                    Vector3 at = island.center + new Vector3(Mathf.Cos(angle) * island.width * radius, 0,
                        Mathf.Sin(angle) * island.depth * radius);
                    float width = Range(rng, 1.65f, 2.85f), height = Range(rng, .72f, 1.4f);
                    int species = colonySpecies[(i + c) % colonySpecies.Length];
                    if(species==3){height*=.72f;width*=.8f;}
                    Bounds bounds = PlaceScan(corals[species], root, at, supportSurface,
                        height, width, Range(rng, 0, 360), (i + c) % palette.Length, cache, resources, proximity, false);
                    AddPocket(bounds, c % 3 != 2, c == 1 && i % 2 == 0);
                    CoralCount++;
                    if (c < (i < 6 ? 3 : 2))
                    {
                        Vector3 basePoint = at + new Vector3(.42f, 0, -.24f);
                        basePoint.y = supportSurface(basePoint.x, basePoint.z) - .045f;
                        if (i < 2 && c == 0) AddAnemone(livingGeometry, basePoint, .95f, livingPalette[0], rng, supportSurface);
                        else if ((i + c) % 3 == 0) AddSeaFan(livingGeometry, basePoint, Range(rng, .85f, 1.65f), Range(rng, 0, 360), livingPalette[(i+c+1)%livingPalette.Length], rng);
                        else if ((i+c)%3 == 1) AddSponges(livingGeometry, basePoint, Range(rng, .5f, 1.1f), livingPalette[(i+c+1)%livingPalette.Length], rng, supportSurface);
                        else AddSoftCoral(livingGeometry, basePoint, Range(rng, .5f, 1.1f), livingPalette[(i+c+1)%livingPalette.Length], rng);
                    }
                    if (staged && c % 4 == 3) yield return null;
                }
                // The inner ledges face the swimmer. Rich low colonies cover their lips and
                // break the terrace silhouette, instead of concentrating all detail on top.
                if(i<8)
                {
                    float inward=-Mathf.Sign(island.center.x-spawn.x);
                    for(int lip=0;lip<5;lip++)
                    {
                        Vector3 at=island.center+new Vector3(inward*island.width*(.61f+.09f*Mathf.Sin(lip*2.4f)),0,
                            Mathf.Lerp(-.75f,.75f,lip/4f)*island.depth);
                        int species = colonySpecies[(lip + i) % colonySpecies.Length];
                        float h=species==3?.68f:species==2?.82f:.65f;
                        float w=species==3?1.55f:species==2?1.15f:Range(rng,2.1f,3.0f);
                        Bounds colony=PlaceScan(corals[species],root,at,supportSurface,h,w,
                            Range(rng,0,360),(i+lip+1)%palette.Length,cache,resources,proximity,false);
                        AddPocket(colony,species!=2,lip==2&&i%3==0);CoralCount++;
                        if(lip<2)
                        {
                            Vector3 plant=at+new Vector3(inward*.2f,0,.35f);
                            plant.y=supportSurface(plant.x,plant.z)-.045f;
                            if(lip==0)AddSeaFan(livingGeometry,plant,Range(rng,.7f,1.2f),inward*25f,livingPalette[1],rng);
                            else AddSponges(livingGeometry,plant,Range(rng,.35f,.65f),livingPalette[2],rng,supportSurface);
                        }
                        if(staged&&lip%3==2)yield return null;
                    }
                }
                // Twelve small encrusting colonies grow out from the actual front faces, filling
                // bare ledges without inflating the interactive population or narrowing the route.
                if(i<6)
                {
                    float inward=-Mathf.Sign(island.center.x-spawn.x);
                    for(int patch=0;patch<2;patch++)
                    {
                        Vector3 at=island.center+new Vector3(inward*island.width*(patch==0?.77f:.67f),0,
                            (patch==0?-.43f:.39f)*island.depth);
                        at.y=supportSurface(at.x,at.z);
                        const float epsilon=.035f;
                        Vector3 normal=new Vector3(supportSurface(at.x-epsilon,at.z)-supportSurface(at.x+epsilon,at.z),
                            epsilon*2f,supportSurface(at.x,at.z-epsilon)-supportSurface(at.x,at.z+epsilon)).normalized;
                        PlaceSurfaceScan(corals[(i+patch)%3],root,at,normal,Range(rng,.57f,.94f),
                            Range(rng,0,360),(i+patch+2)%palette.Length,cache,resources);
                        CoralCount++;
                    }
                }
                // Larger scanned outcrops are seated on real ledges, breaking the exposed
                // procedural face with the same detailed rock assets seen at touching range.
                for (int b = 0; b < (i < 10 ? 2 : 1); b++)
                {
                    float inward = -Mathf.Sign(island.center.x - spawn.x);
                    Vector3 at = island.center + new Vector3(inward * island.width * (b == 0 ? .55f : .79f),
                        0, (b == 0 ? -.42f : .38f) * island.depth);
                    PlaceScan(rocks[(i+b)%rocks.Count], root, at, supportSurface,
                        Range(rng,1.1f,1.7f), Range(rng,2.5f,3.8f), Range(rng,0,360), 4, cache, resources, proximity, true);
                }
                if (livingGeometry.positions.Count > 0)
                {
                    Mesh mesh = livingGeometry.Mesh("Living coral patch " + i,true); resources.Own(mesh);
                    CreateMesh(root, "Sea fans and sponges " + i, mesh, living, ShadowCastingMode.On);
                    LivingCoralTriangles += mesh.triangles.Length / 3;
                }
                progress?.Invoke(.27f + .37f * (i+1f) / layout.Length);
                if (staged) yield return null;
            }
            // Small colonies grow on the arch crown; none hang unsupported beneath its opening.
            for (int i = 0; i < 22; i++)
            {
                float t = Mathf.Lerp(.13f, .86f, i / 21f);
                Vector3 point = ArchPath(ArchCenter, t) + new Vector3(0, 0, i%2==0?-.64f:Range(rng,-.2f,.35f));
                Bounds bounds = PlaceScan(corals[i % corals.Count], root, point,
                    (x,z) => archSupport.TryTop(x,z,out float y) ? y : floor(x,z),
                    Range(rng,.48f,.86f), Range(rng,1.4f,2.4f), Range(rng,0,360), i%palette.Length,
                    cache, resources, proximity, false);
                AddPocket(bounds, true, i == 2 || i == 10); CoralCount++;
                if (staged && i % 3 == 2) yield return null;
            }
            // Front-facing shoulder and brow attachments are projected onto real LOD0
            // triangles. They grow out of the surface; none form a new obstacle across the hole.
            for(int patch=0;patch<16;patch++)
            {
                int column=patch%8;
                float t=Mathf.Lerp(.12f,.86f,column/7f);
                float azimuth=(patch<8?1.47f:1.23f)*Mathf.PI;
                ArchAttachment(ArchCenter,t,azimuth,out Vector3 point,out Vector3 normal);
                PlaceSurfaceScan(corals[(patch+1)%corals.Count],root,point,normal,Range(rng,.70f,1.12f),
                    Range(rng,0,360),(patch+1)%palette.Length,cache,resources);
                CoralCount++;
                if(staged&&patch%4==3)yield return null;
            }
            // Low, offset spurs make the channel edges alternate without closing the
            // continuous 2.5-metre central swimming corridor. All roots use real mesh faces.
            Vector4[] spurs = {
                new Vector4(-3.65f, 4.8f, .44f, 1.7f), new Vector4(3.5f, 10.8f, .36f, 1.4f),
                new Vector4(-4.2f, 23.0f, .40f, 2.0f)
            };
            for(int i=0;i<spurs.Length;i++)
            {
                Vector4 p=spurs[i];
                var spur=new Island { center=spawn+new Vector3(p.x,0,p.y),width=p.w,depth=p.w*.78f,
                    height=p.z,angle=0f,seed=17.5f+i*3.2f,floor=floor,bluff=true };
                ambientFootprints.Add(spur);
                Func<float,float,float> surface=CreateIsland(root,spur,layout.Length+i,stone,resources,proximity,false,surfaceGrowth);
                Vector3 at=spur.center;
                PlaceScan(rocks[i%rocks.Count],root,at,surface,.55f,1.5f,Range(rng,0,360),4,cache,resources,proximity,true);
                at+=new Vector3(i%2==0?-.45f:.45f,0,.35f);
                Bounds colony=PlaceScan(corals[i%2],root,at,surface,.31f,1.35f,
                    Range(rng,0,360),i%palette.Length,cache,resources,proximity,false);
                AddPocket(colony,true,false);CoralCount++;
                if(staged)yield return null;
            }
            // Offset flat-topped rock slabs replace the naked concentric pyramids. Their
            // opaque geometry is batched per bluff, giving different silhouettes and parallax.
            Vector4[] distant = {
                new Vector4(-12.8f, 40.5f, 5.9f, 4.8f), new Vector4(18.8f, 40.8f, 8.3f, 5.7f),
                new Vector4(-26.5f, 25.5f, 6.8f, 5.7f), new Vector4(30.5f, 20.5f, 8.6f, 5.6f),
                new Vector4(1.0f, 44.0f, 3.8f, 4.4f)
            };
            for (int i = 0; i < distant.Length; i++)
            {
                Vector4 p = distant[i];
                CreateBluff(root,spawn+new Vector3(p.x,0,p.y),p.w,p.z,i,floor,stone,resources,proximity,ambientFootprints,false,surfaceGrowth);
                if (staged) yield return null;
            }
            // Each distant garden is a real part of the same fixed world. Build once and
            // cache it with the environment; swimming changes only the player transform.
            IEnumerator explorationWork=BuildExplorationGardens(root,floor,spawn,stone,living,surfaceGrowth,corals,rocks,market,
                cache,resources,proximity,ambientFootprints,rng,staged,progress);
            try { while(explorationWork.MoveNext())yield return explorationWork.Current; }
            finally { (explorationWork as IDisposable)?.Dispose(); }
            var rubble = new Geometry();
            for (int i = 0; i < 72; i++)
            {
                float z = spawn.z + Range(rng,.8f,34f);
                float pathBend = Mathf.Sin((z-spawn.z)*.22f)*.68f;
                float x = spawn.x + pathBend + (i%2==0?-1f:1f)*Range(rng,1.7f,4.3f);
                AddRubble(rubble, new Vector3(x,floor(x,z)-.045f,z), Range(rng,.11f,.42f), rng);
            }
            Mesh rubbleMesh = rubble.Mesh("Grounded reef rubble"); resources.Own(rubbleMesh);
            CreateMesh(root,"Sand-channel coral rubble",rubbleMesh,stone,ShadowCastingMode.On);
            GeneratedRockTriangles += rubbleMesh.triangles.Length/3;
            foreach (Vector2 offset in new[] { new Vector2(-10.25f,2.35f), new Vector2(9.9f,5.65f),
                new Vector2(-12.2f,9.25f), new Vector2(13.0f,14.6f) })
            {
                float x=spawn.x+offset.x,z=spawn.z+offset.y;
                hiddenCoinCandidates.Add(new Vector3(x,floor(x,z)+.06f,z));
            }
            IEnumerator contactWork=BakeGroundAmbientContact(resources,ambientFootprints,spawn,staged,progress);
            try { while(contactWork.MoveNext())yield return contactWork.Current; }
            finally { (contactWork as IDisposable)?.Dispose(); }
            OceanSanctuaryVista.Build(root,floor,spawn,resources);
            progress?.Invoke(1f);
            Debug.Log($"[Sanctuary] corals={CoralCount}, pockets={pockets.Count}, vents={vents.Count}, solidRegions={solids.Count}, arch={ArchCenter:F2}, rockTriangles={GeneratedRockTriangles}, livingTriangles={LivingCoralTriangles}, explorationZones={ExplorationZones.Count}, outerBanks={ExplorationBanks.Count}, measuredMasses={reefMasses.Count}, naturalScanMasses={scannedMasses.Count}, scannedColonies={ScannedCoralCount}, surfaceColonies={surfaceColonies.Count}, surfaceTriangles={SurfaceGardenTriangles}, surfaceRenderers={SurfaceGardenRenderers}, rockProfiles={string.Join("/",rockProfiles)}; shared scan LODs, opaque GPU-current flora, fixed world root");
            Debug.Log($"[SanctuaryScannedCoralBudget] totalColonies={ScannedCoralCount}, fanColonies={market.fanColonies}, fanNearRenderers={market.fanNearRenderers}, fanAllLodRenderers={market.fanAllLodRenderers}, brainColonies={market.brainColonies}, brainNearRenderers={market.brainNearRenderers}, brainAllLodRenderers={market.brainAllLodRenderers}; authored renderer inventory, not simultaneous visibility or measured draw calls");
        }

        static IEnumerator BuildExplorationGardens(Transform root,Func<float,float,float> floor,Vector3 spawn,
            Material stone,Material living,Material surfaceGrowth,List<Scan> corals,List<Scan> rocks,OceanScannedReefMass.Assets market,Dictionary<string,Material> cache,
            OceanSanctuaryResources resources,ReefProximityLod proximity,List<Island> ambientFootprints,
            System.Random rng,bool staged,Action<float> progress)
        {
            for(int i=0;i<ExplorationBanks.Count;i++)
            {
                ExplorationBank plan=ExplorationBanks[i];
                string zoneName=plan.zone<6?ExplorationZones[plan.zone+1].name:"Connecting reef passages";
                var bankRoot=new GameObject(zoneName+" — bank "+i).transform;bankRoot.SetParent(root,false);
                Vector2 towardWater=NearestExplorationWater(plan.offset)-plan.offset;
                if(towardWater.sqrMagnitude<.01f)towardWater=Vector2.right;
                towardWater.Normalize();
                int profile=explorationProfiles[i];
                float waterBearing=Mathf.Atan2(towardWater.y,towardWater.x);
                // Low shoulders face the navigable water; broad crowns rise behind them.
                // Align the same existing mass, not a camera-following wall or extra instance.
                float angle=profile==1||profile==2?waterBearing+Mathf.PI*.5f:
                    profile==4?waterBearing+Mathf.PI:i*.43f;
                var island=new Island { center=spawn+new Vector3(plan.offset.x,0,plan.offset.y),
                    width=plan.width,depth=plan.depth,height=plan.height,angle=angle,
                    seed=73.9f+i*5.71f,floor=floor,bluff=false,profile=profile };
                Func<float,float,float> surface;
                if(plan.height>=15f)
                {
                    float naturalScale=i==3?1.05f:i==8?.98f:i==12?1.08f:i==15?1.03f:i==20?1.04f:1f;
                    // One real forty-metre coastal shelf contrasts with the compact
                    // ten-metre mountainsides, without stretching either source mesh.
                    surface=CreateScannedMass(market,i==3?market.cliff:market.mountainside,bankRoot,"Reef terrace "+(100+i),
                        island.center,naturalScale,(i%3-1)*9f,1,1130+i,
                        floor,resources,proximity,ambientFootprints);
                }
                else
                {
                    ambientFootprints.Add(island);
                    surface=CreateIsland(bankRoot,island,100+i,stone,resources,proximity,true,surfaceGrowth);
                }
                var growth=new Geometry();
                for(int colony=0;colony<3;colony++)
                {
                    // The foot colony can be touched from the route; the middle and crown
                    // colonies create a larger composite organism rather than a stretched scan.
                    float bearing=Mathf.Atan2(towardWater.y,towardWater.x)+(colony==1?.66f:colony==2?-.9f:0f);
                    float reach=plan.height>=15f?(colony==0?.98f:colony==1?.86f:.48f):
                        colony==0?1.02f:colony==1?.71f:.32f;
                    Vector3 at=island.center+new Vector3(Mathf.Cos(bearing)*island.width*reach,0,
                        Mathf.Sin(bearing)*island.depth*reach);
                    int species=plan.zone==4?(colony==1?3:1):plan.zone==1?(colony==1?2:0):(i+colony)%corals.Count;
                    float width=colony==0?Range(rng,3.4f,4.7f):Range(rng,2.5f,4.0f);
                    float height=species==2?Range(rng,1.35f,2.1f):species==3?Range(rng,1.25f,2.0f):Range(rng,.85f,1.45f);
                    Bounds bounds=PlaceScan(corals[species],bankRoot,at,surface,height,width,Range(rng,0,360),
                        (i+colony+plan.zone)%palette.Length,cache,resources,proximity,false,true);
                    AddPocket(bounds,species!=2,colony==0&&(i%3==0||plan.zone==4));CoralCount++;
                    if(colony==0)
                    {
                        Vector3 plant=at+new Vector3(towardWater.y*.8f,0,-towardWater.x*.8f);
                        plant.y=surface(plant.x,plant.z)-.045f;
                        if(plan.zone==4)AddSeaFan(growth,plant,Range(rng,1.65f,2.5f),bearing*Mathf.Rad2Deg,
                            livingPalette[(i+2)%livingPalette.Length],rng);
                        else if(plan.zone!=2&&i%2==0)AddSponges(growth,plant,Range(rng,.65f,1.05f),
                            livingPalette[(i+1)%livingPalette.Length],rng,surface);
                        else if(i%3==0)AddSoftCoral(growth,plant,.85f,livingPalette[(i+3)%livingPalette.Length],rng);
                    }
                    if(staged)yield return null;
                }
                // A high-quality scanned rock wraps the route-facing foot. Tall silhouettes
                // come from real limestone banks, while the detailed asset supplies close grain.
                Vector3 rockPoint=island.center+new Vector3(towardWater.x*island.width*.94f,0,
                    towardWater.y*island.depth*.94f);
                PlaceScan(rocks[i%rocks.Count],bankRoot,rockPoint,surface,Range(rng,1.5f,2.5f),
                    Range(rng,4.0f,5.7f),Range(rng,0,360),4,cache,resources,proximity,true,true);
                if(growth.positions.Count>0)
                {
                    Mesh mesh=growth.Mesh("Exploration living reef "+i,true);resources.Own(mesh);
                    Renderer renderer=CreateMesh(bankRoot,"Sea fans and sponges",mesh,living,ShadowCastingMode.On);
                    RegisterExplorationDetail(renderer);
                    LivingCoralTriangles+=mesh.triangles.Length/3;
                }
                // Coins sit behind but outside the solid bank, on the true ground. The
                // gameplay placement pass still checks visibility, blocking and hand reach.
                if(i%3==1)
                {
                    float x=island.center.x-towardWater.x*(island.width*1.35f+1.2f);
                    float z=island.center.z-towardWater.y*(island.depth*1.35f+1.2f);
                    if(Mathf.Abs(x-spawn.x)<104f&&Mathf.Abs(z-spawn.z)<104f)
                        hiddenCoinCandidates.Add(new Vector3(x,floor(x,z)+.06f,z));
                }
                progress?.Invoke(.66f+.23f*(i+1f)/ExplorationBanks.Count);
                if(staged)yield return null;
            }
            // Opaque irregular buttresses sit at different depths around the outer route.
            // Beyond these masses the separate fixed vista supplies distant blue geology.
            Vector4[] outerButtresses={
                new Vector4(-103,-69,9.5f,8.2f),new Vector4(-100,4,25f,8.0f),
                new Vector4(-102,64,14f,6.4f),new Vector4(-50,105,8f,10.0f),
                new Vector4(49,103,23f,7.8f),new Vector4(106,65,11f,8.2f),
                new Vector4(104,-69,26f,7.2f),new Vector4(21,-105,7.5f,9.5f),
                new Vector4(-64,-105,15f,8.4f)
            };
            for(int i=0;i<outerButtresses.Length;i++)
            {
                Vector4 p=outerButtresses[i];
                if(i==1||p.z>=15f)
                    CreateScannedMass(market,i==1?market.cliff:market.mountainside,root,"Distant offset reef bluff "+(100+i),
                        spawn+new Vector3(p.x,0,p.y),i==1?1f:i==4?1.05f:i==6?1.1f:.96f,0f,2,931+i,
                        floor,resources,proximity,ambientFootprints);
                else
                    CreateBluff(root,spawn+new Vector3(p.x,0,p.y),p.w,p.z,100+i,floor,stone,
                        resources,proximity,ambientFootprints,true,surfaceGrowth);
                progress?.Invoke(.89f+.025f*(i+1f)/outerButtresses.Length);
                if(staged)yield return null;
            }
            // One small combined patch per route stretch retains spatial culling. Combining
            // the entire 500 m circuit into one renderer would draw every stone from every view.
            for(int segment=3;segment<ExplorationRouteOffsets.Count-1;segment++)
            {
                Vector2 a=ExplorationRouteOffsets[segment],b=ExplorationRouteOffsets[segment+1];
                Vector2 direction=(b-a).normalized,side=new Vector2(-direction.y,direction.x);
                var patch=new Geometry();
                for(int pebble=0;pebble<9;pebble++)
                {
                    Vector2 at=Vector2.Lerp(a,b,(pebble+.5f)/9f)+side*(pebble%2==0?-1:1)*Range(rng,3.0f,5.0f);
                    float x=spawn.x+at.x,z=spawn.z+at.y;
                    AddRubble(patch,new Vector3(x,floor(x,z)-.045f,z),Range(rng,.13f,.52f),rng);
                }
                Mesh mesh=patch.Mesh("Exploration channel rubble "+segment);resources.Own(mesh);
                Renderer renderer=CreateMesh(root,"Grounded route rubble "+segment,mesh,stone,ShadowCastingMode.Off);
                RegisterExplorationDetail(renderer);
                GeneratedRockTriangles+=mesh.triangles.Length/3;
                if(staged&&segment%3==2)yield return null;
            }
        }

        static Vector2 NearestExplorationWater(Vector2 point)
        {
            Vector2 nearest=point;float distance=float.PositiveInfinity;
            foreach(IReadOnlyList<Vector2> route in new[]{ExplorationRouteOffsets,ExplorationShortcutOffsets})
                for(int i=0;i<route.Count-1;i++)
                {
                    Vector2 a=route[i],ab=route[i+1]-a;
                    Vector2 candidate=a+ab*Mathf.Clamp01(Vector2.Dot(point-a,ab)/Mathf.Max(.001f,ab.sqrMagnitude));
                    float squared=(point-candidate).sqrMagnitude;
                    if(squared<distance){distance=squared;nearest=candidate;}
                }
            return nearest;
        }

        static void RegisterExplorationDetail(Renderer renderer)
        {
            // Native LOD culling removes tiny opaque detail after it has faded into water;
            // it does not alter material opacity or move geometry with the head.
            var lod=renderer.gameObject.AddComponent<LODGroup>();
            float size=Mathf.Max(renderer.bounds.size.x,renderer.bounds.size.y,renderer.bounds.size.z);
            lod.SetLODs(new[]{new LOD(Mathf.Clamp(size/125f,.012f,.3f),new[]{renderer})});
            lod.RecalculateBounds();
        }

        static void AddPocket(Bounds bounds, bool branch, bool vent)
        {
            float h = bounds.size.y;
            Vector3 center = bounds.center; center.y = bounds.min.y + h * .92f;
            pockets.Add(new ReefHabitat.CoralPocket { center = center,
                shelter = new Vector3(center.x + h * .12f, bounds.min.y + h * (branch ? .74f : 1.2f), center.z),
                radius = Mathf.Clamp(h * .65f, .4f, 1.05f) });
            if (vent && branch) vents.Add(new Vector3(center.x - .15f, bounds.min.y + h * .6f, center.z));
        }

        static Bounds PlaceScan(Scan scan, Transform parent, Vector3 point, Func<float,float,float> support,
            float height, float width, float yaw, int tint, Dictionary<string,Material> cache,
            OceanSanctuaryResources resources, ReefProximityLod proximity, bool rock,bool exploration=false)
        {
            var group = new GameObject((rock ? "Reef " : "Coral ") + scan.models[0].name).transform;
            group.SetParent(parent, false); group.position = new Vector3(point.x, support(point.x,point.z), point.z);
            group.rotation = Quaternion.Euler(0,yaw,0);
            Bounds reference = scan.geometry[0].bounds;
            float scale = Mathf.Min(height / Mathf.Max(.01f,reference.size.y), width / Mathf.Max(.01f,Mathf.Max(reference.size.x,reference.size.z)));
            var lods = new List<LOD>(); var models = new List<Transform>();
            float shift = float.PositiveInfinity;
            for (int l=0; l<scan.models.Length && scan.models[l]!=null; l++)
            {
                GameObject model = UnityEngine.Object.Instantiate(scan.models[l],group,false);
                model.transform.localScale=Vector3.one*scale;
                model.transform.localPosition=new Vector3(-reference.center.x*scale,-reference.min.y*scale,-reference.center.z*scale);
                models.Add(model.transform);
                foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(collider);
                Renderer[] renderers=model.GetComponentsInChildren<Renderer>();
                AssignScanMaterials(renderers,rock,tint,l,cache,resources);
                float required=rock?float.PositiveInfinity:float.NegativeInfinity;
                foreach(Vector3 contact in (rock?scan.geometry[l].rockContacts:scan.geometry[l].coralContacts))
                {
                    Vector3 world=model.transform.TransformPoint(contact);
                    float delta=support(world.x,world.z)-world.y;
                    required=rock?Mathf.Min(required,delta):Mathf.Max(required,delta);
                }
                shift=Mathf.Min(shift,required);
                lods.Add(new LOD(l==0?(exploration?.22f:.12f):l==1?(exploration?.075f:.032f):.003f,renderers));
            }
            group.position+=Vector3.up*(shift-(rock?.10f:.045f));
            Renderer[] near=models[0].GetComponentsInChildren<Renderer>();
            Bounds bounds=near[0].bounds;for(int i=1;i<near.Length;i++)bounds.Encapsulate(near[i].bounds);
            if(exploration)
            {
                float size=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
                lods[lods.Count-1]=new LOD(Mathf.Clamp(size/145f,.012f,.055f),lods[lods.Count-1].renderers);
            }
            var lodGroup=group.gameObject.AddComponent<LODGroup>();lodGroup.SetLODs(lods.ToArray());lodGroup.fadeMode=LODFadeMode.None;lodGroup.RecalculateBounds();
            proximity.Register(lodGroup,bounds,false);
            return bounds;
        }

        static void AssignScanMaterials(Renderer[] renderers,bool rock,int tint,int lod,
            Dictionary<string,Material> cache,OceanSanctuaryResources resources)
        {
            foreach(Renderer renderer in renderers)
            {
                Material[] mats=renderer.sharedMaterials;
                for(int m=0;m<mats.Length;m++)
                {
                    Material source=mats[m];if(source==null)continue;
                    string key=source.GetInstanceID()+":"+(rock?-1:tint);
                    if(!cache.TryGetValue(key,out Material material))
                    {
                        material=new Material(source){name="Sanctuary "+source.name,enableInstancing=true};
                        Color color=rock?new Color(.72f,.79f,.77f):palette[tint];
                        if(material.HasProperty("baseColorFactor"))material.SetColor("baseColorFactor",color);
                        if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",color);
                        if(material.HasProperty("roughnessFactor"))material.SetFloat("roughnessFactor",.9f);
                        if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",.12f);
                        cache.Add(key,material);resources.Own(material);
                    }
                    mats[m]=material;
                }
                renderer.sharedMaterials=mats;renderer.receiveShadows=true;
                renderer.shadowCastingMode=lod<2?ShadowCastingMode.On:ShadowCastingMode.Off;
            }
        }
        static void PlaceSurfaceScan(Scan scan,Transform parent,Vector3 point,Vector3 normal,float size,float yaw,
            int tint,Dictionary<string,Material> cache,OceanSanctuaryResources resources)
        {
            var group=new GameObject("Encrusting coral "+scan.models[0].name).transform;
            group.SetParent(parent,false);group.position=point;
            group.rotation=Quaternion.FromToRotation(Vector3.up,normal)*Quaternion.Euler(0,yaw,0);
            Bounds reference=scan.geometry[0].bounds;
            float scale=size/Mathf.Max(reference.size.x,reference.size.y,reference.size.z,.001f);
            var lods=new List<LOD>();float mostRaisedAnchor=float.NegativeInfinity;
            for(int l=0;l<scan.models.Length&&scan.models[l]!=null;l++)
            {
                GameObject model=UnityEngine.Object.Instantiate(scan.models[l],group,false);
                model.transform.localScale=Vector3.one*scale;
                model.transform.localPosition=new Vector3(-reference.center.x*scale,-reference.min.y*scale,-reference.center.z*scale);
                foreach(Collider collider in model.GetComponentsInChildren<Collider>(true))UnityEngine.Object.Destroy(collider);
                Renderer[] renderers=model.GetComponentsInChildren<Renderer>();AssignScanMaterials(renderers,false,tint,l,cache,resources);
                float closest=float.PositiveInfinity;
                foreach(Vector3 contact in scan.geometry[l].coralContacts)
                    closest=Mathf.Min(closest,Vector3.Dot(model.transform.TransformPoint(contact)-point,normal));
                mostRaisedAnchor=Mathf.Max(mostRaisedAnchor,closest);
                lods.Add(new LOD(l==0?.22f:l==1?.055f:.002f,renderers));
            }
            // Every LOD retains a real root contact five centimetres into the attachment plane.
            group.position-=normal*(mostRaisedAnchor+.05f);
            var lodGroup=group.gameObject.AddComponent<LODGroup>();lodGroup.SetLODs(lods.ToArray());lodGroup.RecalculateBounds();
            // Native screen-size selection preserves fine detail at touching distance without
            // forcing 32K-triangle miniature colonies across the entire eight-metre reef zone.
        }

        // An eroded bridge has an off-centre crown, unequal shoulders and a broad brow.
        // A circular half-ring reads as a manufactured doorway at headset scale.
        static readonly Vector3[] archSpine = {
            new Vector3(-6.4f,-.65f,-.25f), new Vector3(-5.9f,2.7f,-.50f),
            new Vector3(-4.4f,4.8f,-.15f), new Vector3(-2.2f,5.6f,.20f),
            new Vector3(.6f,5.7f,.70f), new Vector3(3.5f,4.8f,.35f),
            new Vector3(4.7f,2.8f,-.25f), new Vector3(5.9f,-.65f,.10f)
        };
        static Vector3 ArchPath(Vector3 center,float t)
        {
            float segment = Mathf.Clamp01(t) * (archSpine.Length-1);
            int i = Mathf.Min(archSpine.Length-2,Mathf.FloorToInt(segment)); float u = segment-i;
            Vector3 a=archSpine[Mathf.Max(0,i-1)], b=archSpine[i], c=archSpine[i+1], d=archSpine[Mathf.Min(archSpine.Length-1,i+2)];
            return center+.5f*((2f*b)+(-a+c)*u+(2f*a-5f*b+4f*c-d)*u*u+(-a+3f*b-3f*c+d)*u*u*u);
        }
        static float ArchRadius(float t)
        {
            float shoulder=Mathf.Pow(Mathf.Abs(t-.51f)*1.95f,.65f);
            return 1.10f+.64f*shoulder+.16f*Mathf.Sin(t*17f+.6f)+.09f*Mathf.Cos(t*31f);
        }
        static Vector3 ArchSurfacePoint(Vector3 center,float t,float a)
        {
            Vector3 p=ArchPath(center,t);
            Vector3 tangent=(ArchPath(center,Mathf.Min(1,t+.001f))-ArchPath(center,Mathf.Max(0,t-.001f))).normalized;
            Vector3 outward=Vector3.Cross(tangent,Vector3.forward).normalized;
            float cos=Mathf.Cos(a),sin=Mathf.Sin(a);
            float radialShape=1f+.115f*Mathf.Sin(a*3f+t*9f)+.065f*Mathf.Sin(a*7f-t*13f);
            float depth=1.65f+.25f*Mathf.Sin(t*12f+.6f)+.19f*Mathf.Cos(t*21f);
            float bevel=Mathf.Sign(sin)*Mathf.Pow(Mathf.Abs(sin),.77f);
            Vector3 point=p+outward*(cos*ArchRadius(t)*radialShape)+Vector3.forward*(bevel*depth);
            float weathering=.14f*Mathf.Sin(point.x*3.7f+point.y*2.3f+point.z*1.9f)+
                .09f*Mathf.Sin(point.x*6.1f-point.y*4.4f+point.z*3.1f);
            Vector3 normal=(outward*cos+Vector3.forward*sin).normalized;
            point+=normal*weathering;
            // Thin undercut strata break up the broad bridge without making regular rings.
            point.y+=.055f*Mathf.Sin(point.x*5.3f+point.z*2.8f)*Mathf.Clamp01(1f-Mathf.Abs(cos)*.4f);
            return point;
        }
        static void ArchAttachment(Vector3 center,float t,float angle,out Vector3 point,out Vector3 normal)
        {
            float ring=t*64f,side=angle/(2f*Mathf.PI)*18f;
            int i=Mathf.Clamp(Mathf.FloorToInt(ring),0,63),j=Mathf.Clamp(Mathf.FloorToInt(side),0,17);
            float u=ring-i,v=side-j;
            Vector3 a=ArchSurfacePoint(center,i/64f,j/18f*Mathf.PI*2f);
            Vector3 b=ArchSurfacePoint(center,(i+1)/64f,j/18f*Mathf.PI*2f);
            Vector3 c=ArchSurfacePoint(center,(i+1)/64f,(j+1)/18f*Mathf.PI*2f);
            Vector3 d=ArchSurfacePoint(center,i/64f,(j+1)/18f*Mathf.PI*2f);
            if(u>=v){point=a*(1-u)+b*(u-v)+c*v;normal=Vector3.Cross(b-a,c-a).normalized;}
            else{point=a*(1-v)+c*u+d*(v-u);normal=Vector3.Cross(c-a,d-a).normalized;}
        }
        static Mesh ArchMesh(Vector3 center,int rings,int sides)
        {
            var g=new Geometry();
            for(int i=0;i<=rings;i++)
            {
                float t=i/(float)rings;
                for(int j=0;j<=sides;j++)
                {
                    float a=j/(float)sides*Mathf.PI*2f;
                    Vector3 point=ArchSurfacePoint(center,t,a);
                    float variation=.88f+.12f*Mathf.PerlinNoise(point.x*1.3f+41f,point.y*1.3f+23f);
                    // Keep only subtle mineral variation; mesh relief and real sunlight
                    // shadows provide depth instead of a painted horizontal dark band.
                    float crevice=1f-.04f*Mathf.Pow(Mathf.Clamp01(Mathf.Sin(point.y*5.6f+point.x*1.5f)),8f);
                    Color tint=new Color(.86f,.94f,.92f,0f)*(variation*crevice);tint.a=0f;
                    g.Add(point,new Vector2(t*11f,j/(float)sides*2.3f),tint);
                    if(i<rings&&j<sides){int q=i*(sides+1)+j;g.Quad(q,q+sides+1,q+sides+2,q+1);}
                }
            }
            return g.Mesh("Weathered limestone arch "+rings);
        }
        static ReefSupportGeometry CreateArch(Transform root,Vector3 center,Material stone,OceanSanctuaryResources resources,ReefProximityLod proximity,
            Material surfaceGrowth,Func<float,float,float> floor)
        {
            var group=new GameObject("Natural reef arch — open swim passage").transform;group.SetParent(root,false);
            var renderers=new Renderer[3];int[] rings={64,36,20};int[] sides={18,12,8};
            for(int l=0;l<3;l++)
            {
                Mesh mesh=ArchMesh(center,rings[l],sides[l]);resources.Own(mesh);
                renderers[l]=CreateMesh(group,"Arch LOD"+l,mesh,stone,l<2?ShadowCastingMode.On:ShadowCastingMode.Off);
                if(l==1){var collider=group.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=mesh;}
                if(l==0)GeneratedRockTriangles+=mesh.triangles.Length/3;
            }
            var lod=group.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[]{new LOD(.13f,new[]{renderers[0]}),new LOD(.035f,new[]{renderers[1]}),new LOD(.003f,new[]{renderers[2]})});
            lod.RecalculateBounds();proximity.Register(lod,renderers[0].bounds,false);
            // Segment bounds describe only solid arch ribs; there is no bounding box across its opening.
            for(int i=0;i<16;i++)
            {
                float t=(i+.5f)/16f;
                Vector3 a=ArchPath(center,i/16f),b=ArchPath(center,(i+1f)/16f);
                var bound=new Bounds((a+b)*.5f,Vector3.zero);bound.Encapsulate(a);bound.Encapsulate(b);
                bound.Expand(new Vector3(ArchRadius(t)*2.25f,ArchRadius(t)*2.25f,4.2f));solids.Add(bound);
            }
            ReefSupportGeometry support=ReefSupportGeometry.Read(renderers[0].gameObject,true);
            // A single small garden follows the real upward-facing outer arch surface.
            // Reuse top/edge support checks and only its existing LOD0: no new collision,
            // shadow caster, per-frame placement or change to the three rock LODs.
            CreateSurfaceGarden(group,renderers[0],support,lod,resources,surfaceGrowth,9000,24,floor);
            return support;
        }
        static Mesh IslandMesh(Island island,int rings,int sides)
        {
            var g=new Geometry();
            for(int i=0;i<=rings;i++)
            {
                float r=Mathf.Max(.002f,i/(float)rings)*1.13f;
                for(int j=0;j<=sides;j++)
                {
                    float a=j/(float)sides*Mathf.PI*2f;
                    float x=island.center.x+Mathf.Cos(a)*island.width*r;
                    float z=island.center.z+Mathf.Sin(a)*island.depth*r;
                    var p=new Vector3(x,island.Surface(x,z),z);
                    float variation=.86f+.14f*Mathf.PerlinNoise(x*1.7f+island.seed,z*1.7f);
                    // Match the arch's restrained tint without changing its real surface,
                    // near LOD, collision geometry or directional shadow response.
                    float crevice=1f-.04f*Mathf.Pow(Mathf.Clamp01(Mathf.Sin(p.y*6.7f+x*.7f)),8f);
                    Color tint=new Color(.83f,.91f,.88f,0f)*(variation*crevice);tint.a=0f;
                    g.Add(p,new Vector2(x*.72f,z*.72f),tint);
                    if(i<rings&&j<sides){int q=i*(sides+1)+j;g.Quad(q,q+1,q+sides+2,q+sides+1);}
                }
            }
            // The first ring has a small nonzero radius to keep its radial quads
            // well formed. Close that real opening explicitly; otherwise an exact
            // crown-centre support query falls through both LODs to the seabed.
            Vector3 crown=new Vector3(island.center.x,island.Surface(island.center.x,island.center.z),island.center.z);
            float crownVariation=.86f+.14f*Mathf.PerlinNoise(crown.x*1.7f+island.seed,crown.z*1.7f);
            float crownCrevice=1f-.18f*Mathf.Pow(Mathf.Clamp01(Mathf.Sin(crown.y*6.7f+crown.x*.7f)),8f);
            Color crownTint=new Color(.83f,.91f,.88f,0f)*(crownVariation*crownCrevice);crownTint.a=0f;
            int centre=g.Add(crown,new Vector2(crown.x*.72f,crown.z*.72f),crownTint);
            for(int j=0;j<sides;j++){g.indices.Add(centre);g.indices.Add(j+1);g.indices.Add(j);}
            return g.Mesh("Reef terrace "+rings);
        }
        static Func<float,float,float> CreateScannedMass(OceanScannedReefMass.Assets assets,
            OceanScannedReefMass.Model source,Transform parent,string name,Vector3 point,float scale,float yawOffset,
            int category,int seed,Func<float,float,float> floor,OceanSanctuaryResources resources,
            ReefProximityLod proximity,List<Island> ambientFootprints)
        {
            Vector2 offset=new Vector2(point.x-OceanEnvironmentProfile.SpawnX,point.z-OceanEnvironmentProfile.SpawnZ);
            Vector2 water=NearestExplorationWater(offset)+new Vector2(OceanEnvironmentProfile.SpawnX,OceanEnvironmentProfile.SpawnZ);
            Vector3 towardWater=new Vector3(water.x-point.x,0,water.y-point.z).normalized;
            float yaw=Mathf.Atan2(towardWater.x,towardWater.z)*Mathf.Rad2Deg-
                Mathf.Atan2(source.front.x,source.front.z)*Mathf.Rad2Deg+yawOffset;
            OceanScannedReefMass.Placement placed=OceanScannedReefMass.Place(assets,source,parent,name,
                point,scale,yaw,floor,resources,proximity);
            Bounds realBounds=placed.geometryBounds;
            solids.Add(placed.bounds); // Preserve the conservative formation/player clearance envelope.
            reefMasses.Add(new ReefMass(name,realBounds,
                new Vector3(point.x,floor(point.x,point.z),point.z),realBounds.size.y,category,0));
            scannedMasses.Add(new ScannedMass(name,source.stem,realBounds,scale,placed.triangles));
            GeneratedRockTriangles+=placed.triangles;
            // Small soft contact patches are seeded only where the measured scan actually
            // enters sand. A long cliff AABB must not cast a rectangular dark carpet.
            var cells=new HashSet<Vector2Int>();
            foreach(Vector3 contact in placed.groundContacts)
            {
                float y=floor(contact.x,contact.z);
                if(contact.y>y+.10f)continue;
                var key=new Vector2Int(Mathf.RoundToInt(contact.x/2.3f),Mathf.RoundToInt(contact.z/2.3f));
                if(!cells.Add(key))continue;
                ambientFootprints.Add(new Island{center=new Vector3(contact.x,y,contact.z),
                    width=1.1f,depth=1.1f,height=.35f,angle=0,seed=seed+cells.Count*.71f,floor=floor});
                if(cells.Count>=40)break;
            }
            List<Bounds> colonies=OceanScannedReefMass.AttachColonies(assets,placed,
                new Vector3(water.x,point.y,water.y),category==0?10:category==1?4:5,seed,resources,proximity);
            for(int i=0;i<colonies.Count;i++)
            {
                // These are visual wall attachments. Existing reachable ledge colonies
                // remain the authority for fish homes and collectible shelter points;
                // a vertical face must not acquire a top-of-AABB gameplay target.
                CoralCount++;ScannedCoralCount++;
            }
            return placed.top;
        }

        static Func<float,float,float> CreateIsland(Transform root,Island island,int index,Material stone,OceanSanctuaryResources resources,ReefProximityLod proximity,bool exploration=false,Material surfaceGrowth=null)
        {
            float targetCrown=island.height;
            if(exploration)rockProfiles[island.profile]++;
            island.CalibrateCrown(16,48);
            var group=new GameObject("Reef terrace "+index).transform;group.SetParent(root,false);
            var renderers=new Renderer[2];
            for(int l=0;l<2;l++)
            {
                Mesh mesh=IslandMesh(island,l==0?16:8,l==0?48:28);resources.Own(mesh);
                renderers[l]=CreateMesh(group,"Terrace LOD"+l,mesh,stone,ShadowCastingMode.On);
                if(l==1){var collider=group.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=mesh;}
                else GeneratedRockTriangles+=mesh.triangles.Length/3;
            }
            float last=exploration?Mathf.Clamp(Mathf.Max(renderers[0].bounds.size.x,renderers[0].bounds.size.y,renderers[0].bounds.size.z)/160f,.025f,.19f):.003f;
            var lod=group.gameObject.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(exploration?.24f:.1f,new[]{renderers[0]}),new LOD(last,new[]{renderers[1]})});lod.RecalculateBounds();
            proximity.Register(lod,renderers[0].bounds,false);
            // Inscribed obstruction avoids filling the navigable skirts of irregular terraces.
            Bounds realBounds=renderers[0].bounds;
            Vector3 center=island.center;center.y=realBounds.center.y;
            solids.Add(new Bounds(center,new Vector3(island.width*1.25f,realBounds.size.y,island.depth*1.25f)));
            reefMasses.Add(new ReefMass(group.name,realBounds,
                new Vector3(island.center.x,island.floor(island.center.x,island.center.z),island.center.z),
                targetCrown,exploration?1:0,island.profile));
            // The smooth procedural equation is only a mesh authoring input. Placement uses
            // the actual triangles of both visible LODs, so roots cannot hover over a coarse face.
            var support0=ReefSupportGeometry.Read(renderers[0].gameObject,true);
            var support1=ReefSupportGeometry.Read(renderers[1].gameObject,true);
            if(surfaceGrowth!=null && island.height>.8f)
                CreateSurfaceGarden(group,renderers[0],support0,lod,resources,surfaceGrowth,
                    index,exploration?(targetCrown>=15f?36:targetCrown>=3.75f?12:4):18,island.floor);
            return (x,z) =>
            {
                bool near=support0.TryTop(x,z,out float nearY),far=support1.TryTop(x,z,out float farY);
                return near&&far?Mathf.Min(nearY,farY):near?nearY:far?farY:island.floor(x,z);
            };
        }

        static void CreateBluff(Transform root,Vector3 center,float width,float height,int index,
            Func<float,float,float> floor,Material stone,OceanSanctuaryResources resources,ReefProximityLod proximity,
            List<Island> ambientFootprints,bool exploration=false,Material surfaceGrowth=null)
        {
            var group=new GameObject("Distant offset reef bluff "+index).transform;group.SetParent(root,false);
            var renderers=new Renderer[2];
            for(int l=0;l<2;l++)
            {
                var pieces=new CombineInstance[3];var temporary=new Mesh[3];
                for(int part=0;part<3;part++)
                {
                    float side=part==0?-.48f:part==1?.31f:.69f;
                    var slab=new Island { center=center+new Vector3(side*width,0,(part==1?.31f:-.19f)*width),
                        width=width*(part==1?.70f:.53f),depth=width*(part==1?.61f:.43f),
                        height=height*(part==0?.65f:part==1?1f:.48f),angle=(index+part)*.21f,
                        seed=37.4f+index*5.3f+part*2.7f,floor=floor,bluff=true,profile=exploration?(part==1?(index%3==0?2:4):1):0 };
                    slab.CalibrateCrown(12,32);
                    temporary[part]=IslandMesh(slab,l==0?12:6,l==0?32:16);
                    pieces[part]=new CombineInstance { mesh=temporary[part],transform=Matrix4x4.identity };
                    if(l==0)
                    {
                        ambientFootprints.Add(slab);
                        Bounds realBounds=temporary[part].bounds;
                        Vector3 solidCenter=slab.center;solidCenter.y=realBounds.center.y;
                        solids.Add(new Bounds(solidCenter,new Vector3(slab.width*1.25f,realBounds.size.y,slab.depth*1.25f)));
                    }
                }
                var mesh=new Mesh { name="Batched reef bluff "+index+" LOD"+l };
                mesh.CombineMeshes(pieces,true,true);mesh.RecalculateBounds();resources.Own(mesh);
                foreach(Mesh temporaryMesh in temporary)
                {
                    if(Application.isPlaying)UnityEngine.Object.Destroy(temporaryMesh);
                    else UnityEngine.Object.DestroyImmediate(temporaryMesh);
                }
                renderers[l]=CreateMesh(group,"Bluff LOD"+l,mesh,stone,l==0?ShadowCastingMode.On:ShadowCastingMode.Off);
                if(l==1){var collider=group.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=mesh;}
                else GeneratedRockTriangles+=mesh.triangles.Length/3;
            }
            var lod=group.gameObject.AddComponent<LODGroup>();
            float last=exploration?Mathf.Clamp(Mathf.Max(renderers[0].bounds.size.x,renderers[0].bounds.size.y,renderers[0].bounds.size.z)/180f,.025f,.24f):.003f;
            lod.SetLODs(new[]{new LOD(exploration?.28f:.09f,new[]{renderers[0]}),new LOD(last,new[]{renderers[1]})});
            lod.RecalculateBounds();proximity.Register(lod,renderers[0].bounds,false);
            reefMasses.Add(new ReefMass(group.name,renderers[0].bounds,
                new Vector3(center.x,floor(center.x,center.z),center.z),height,exploration?2:3,0));
            if(surfaceGrowth!=null)
            {
                ReefSupportGeometry support=ReefSupportGeometry.Read(renderers[0].gameObject,true);
                CreateSurfaceGarden(group,renderers[0],support,lod,resources,surfaceGrowth,
                    500+index,18,floor);
            }
        }

        static readonly Color[] surfacePalette={
            new Color(.78f,.46f,.31f),new Color(.78f,.68f,.47f),new Color(.65f,.38f,.43f),
            new Color(.69f,.52f,.29f),new Color(.41f,.55f,.48f),new Color(.72f,.48f,.40f)
        };

        // One generated mesh per host reef. Its visibility is owned by the SAME LODGroup
        // as the substrate, so a tiny colony can never hover above a coarser rock surface.
        // It adds no collider, material instance, shadow caster or frame-based behaviour.
        static void CreateSurfaceGarden(Transform parent,Renderer substrate,ReefSupportGeometry support,
            LODGroup substrateLod,OceanSanctuaryResources resources,Material material,int seed,int count,
            Func<float,float,float> floor)
        {
            Mesh source=substrate.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices=source.vertices;int[] triangles=source.triangles;
            var candidates=new List<int>();var areas=new List<float>();float area=0;
            Vector2 offset=new Vector2(substrate.bounds.center.x-OceanEnvironmentProfile.SpawnX,
                substrate.bounds.center.z-OceanEnvironmentProfile.SpawnZ);
            Vector2 water=NearestExplorationWater(offset)+new Vector2(OceanEnvironmentProfile.SpawnX,OceanEnvironmentProfile.SpawnZ);
            for(int triangle=0;triangle<triangles.Length;triangle+=3)
            {
                Vector3 a=vertices[triangles[triangle]],b=vertices[triangles[triangle+1]],c=vertices[triangles[triangle+2]];
                Vector3 cross=Vector3.Cross(b-a,c-a);
                float magnitude=cross.magnitude;if(magnitude<.00001f||cross.y/magnitude<.075f)continue;
                // Sample area, not vertices: a tessellated patch must not receive more life
                // simply because it has more triangles. Steep exposed ledges remain eligible.
                Vector3 centre=(a+b+c)/3f;
                Vector3 towardWater=new Vector3(water.x-centre.x,0,water.y-centre.z).normalized;
                Vector3 normal=cross/magnitude;
                float facingWeight=Mathf.Lerp(.2f,1f,Mathf.Clamp01(.5f+.5f*Vector3.Dot(normal,towardWater)));
                float visibleSurfaceWeight=Mathf.Lerp(facingWeight,.75f,Mathf.Pow(Mathf.Clamp01(normal.y),4));
                // Spend the SAME colony count in irregular stands rather than a uniform
                // confetti of dots. This field is world anchored and evaluated only while
                // constructing the reef; tessellation density still cannot bias placement.
                float stand=Mathf.PerlinNoise(centre.x*.27f+13.7f,centre.z*.27f+47.1f);
                visibleSurfaceWeight*=Mathf.Lerp(.24f,1.35f,
                    Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.28f,.73f,stand)));
                if(count>=36)
                {
                    // Reallocate the unchanged world budget towards the broad hero faces.
                    // Most life sits on the water-facing lower six metres, while the upper
                    // shoulders retain a smaller population instead of becoming sterile crowns.
                    float relativeHeight=centre.y-floor(centre.x,centre.z);
                    visibleSurfaceWeight*=Mathf.Lerp(1f,.18f,Mathf.SmoothStep(0,1,
                        Mathf.InverseLerp(6f,10f,relativeHeight)));
                }
                // Concentrate the same colony budget on exposed water-facing ledges.
                // Back faces still receive life; no camera-following placement or extra draw calls.
                area+=magnitude*.5f*visibleSurfaceWeight;candidates.Add(triangle);areas.Add(area);
            }
            if(area<=0)return;
            var growth=new Geometry();var rng=new System.Random(71329+seed*173);
            int accepted=0;
            for(int attempt=0;attempt<count*8&&accepted<count;attempt++)
            {
                float selected=Range(rng,0,area);int low=0,high=areas.Count-1;
                while(low<high){int mid=(low+high)/2;if(areas[mid]<selected)low=mid+1;else high=mid;}
                int triangle=candidates[low];
                Vector3 a=vertices[triangles[triangle]],b=vertices[triangles[triangle+1]],c=vertices[triangles[triangle+2]];
                float u=Mathf.Sqrt((float)rng.NextDouble()),v=(float)rng.NextDouble();
                Vector3 point=a*(1-u)+b*(u*(1-v))+c*(u*v);
                if(!support.TryTop(point.x,point.z,out float exposed)||Mathf.Abs(exposed-point.y)>.025f||
                    point.y<floor(point.x,point.z)+.15f)continue;
                Vector3 normal=Vector3.Cross(b-a,c-a).normalized;
                Vector3 root=point-normal*.025f;
                int style=(accepted+seed)%5;
                // Broad foliose/encrusting patches cover selected ledges; smaller branches,
                // fans and sponges break the silhouette rather than making evenly spaced dots.
                float diameter=style==0?Range(rng,1.2f,2.1f):style==1?Range(rng,.90f,1.6f):Range(rng,.48f,1.05f);
                // Neighbouring colonies share a restrained local pigment, so swimming
                // from the warm entrance to the sea-fan ravine reveals readable habitats.
                // Fine variation remains vertex colour: no extra materials or draw calls.
                Color pigment=SurfaceHabitatPigment(point,accepted);
                int firstVertex=growth.positions.Count;
                if(style==0)AddEncrustingPatch(growth,root,normal,diameter,pigment,rng,support);
                else if(style==1)AddPlatingColony(growth,root,normal,diameter,pigment,rng);
                else if(style==2)AddBranchingColony(growth,root,normal,diameter,pigment,rng);
                else if(style==3)AddSmallFan(growth,root,normal,diameter,pigment,rng);
                else AddSmallSponges(growth,root,normal,diameter,pigment,rng);
                if(growth.positions.Count==firstVertex)continue;
                for(int vertex=firstVertex;vertex<growth.positions.Count;vertex++)
                {
                    Color tint=growth.colors[vertex];
                    tint.a=style<2?0:Mathf.Clamp01(Vector3.Distance(growth.positions[vertex],root)/diameter)*.28f;
                    growth.colors[vertex]=tint;
                }
                surfaceColonies.Add(new SurfaceColony(point,root,normal,style,diameter,seed,firstVertex,growth.positions.Count-firstVertex));accepted++;
            }
            if(growth.positions.Count==0)return;
            Mesh mesh=growth.Mesh("Surface coral garden "+seed,true);resources.Own(mesh);
            Renderer renderer=CreateMesh(parent,"Attached coral mosaic "+seed,mesh,material,ShadowCastingMode.Off);
            LOD[] lods=substrateLod.GetLODs();Renderer[] original=lods[0].renderers;
            var near=new Renderer[original.Length+1];Array.Copy(original,near,original.Length);near[near.Length-1]=renderer;
            lods[0].renderers=near;substrateLod.SetLODs(lods);substrateLod.RecalculateBounds();
            int triangleCount=mesh.triangles.Length/3;
            LivingCoralTriangles+=triangleCount;SurfaceGardenTriangles+=triangleCount;SurfaceGardenRenderers++;
        }

        static Color SurfaceHabitatPigment(Vector3 point,int ordinal)
        {
            Vector2 local=new Vector2(point.x-OceanEnvironmentProfile.SpawnX,point.z-OceanEnvironmentProfile.SpawnZ);
            int zone=0;float nearest=float.PositiveInfinity;
            for(int i=0;i<ExplorationZones.Count;i++)
            {
                float squared=(local-ExplorationZones[i].offset).sqrMagnitude;
                if(squared<nearest){zone=i;nearest=squared;}
            }
            // Warm rust/ochre, muted rose and olive colonies remain against cool rock.
            // Select a stable area palette, not one new tint for every tiny colony.
            int dominant=zone==0?0:zone==1?3:zone==2?2:zone==3?4:zone==4?5:zone==5?1:2;
            float variation=Mathf.PerlinNoise(point.x*.63f+29f,point.z*.63f+11f);
            Color colour=Color.Lerp(surfacePalette[dominant],surfacePalette[(dominant+1)%surfacePalette.Length],
                .10f+.22f*variation);
            colour*=.89f+.19f*Mathf.PerlinNoise(point.x*.91f+ordinal*.07f,point.z*.91f+71f);
            colour.a=1f;return colour;
        }

        static void SurfaceBasis(Vector3 normal,float yaw,out Vector3 right,out Vector3 along)
        {
            Vector3 basis=Vector3.Cross(normal,Mathf.Abs(normal.y)>.94f?Vector3.forward:Vector3.up).normalized;
            right=Quaternion.AngleAxis(yaw,normal)*basis;along=Vector3.Cross(normal,right).normalized;
        }

        static void AddEncrustingPatch(Geometry g,Vector3 root,Vector3 normal,float diameter,Color color,
            System.Random rng,ReefSupportGeometry support)
        {
            SurfaceBasis(normal,Range(rng,0,360),out Vector3 right,out Vector3 along);
            // Each lobe conforms to the actual mesh with its own contact. A wide patch
            // crosses triangle boundaries without becoming a floating, flat decal.
            for(int lobe=0;lobe<3;lobe++)
            {
                float angle=lobe*2.39996f;
                Vector3 center=root+(right*Mathf.Cos(angle)+along*Mathf.Sin(angle))*diameter*(lobe==0?0:.19f);
                if(!support.TryTop(center.x,center.z,out float top)||Mathf.Abs(top-center.y)>diameter*.65f)center=root;
                else center.y=top-.015f;
                float radius=diameter*Range(rng,.23f,.33f);
                AddCoralLamina(g,center,normal,right,along,radius,.03f,color,lobe+root.x,support);
            }
        }

        static void AddCoralLamina(Geometry g,Vector3 origin,Vector3 axis,Vector3 right,Vector3 along,
            float radius,float cup,Color color,float phase,ReefSupportGeometry conform=null)
        {
            const int sides=20,faceRings=3;
            int rings=conform==null?4:faceRings;
            var points=new Vector3[rings*(sides+1)];
            bool supported=false;
            for(int attempt=0;attempt<(conform==null?1:6);attempt++)
            {
                supported=true;
                for(int ring=1;ring<=rings;ring++)
                {
                    bool underside=ring>faceRings;
                    float t=underside?1:ring/(float)faceRings;
                    for(int side=0;side<=sides;side++)
                    {
                        float angle=side/(float)sides*Mathf.PI*2;
                        float scallop=1+.065f*Mathf.Sin(angle*5+phase)+.035f*Mathf.Sin(angle*9-phase);
                        Vector3 p=origin+(right*Mathf.Cos(angle)+along*Mathf.Sin(angle))*radius*t*scallop;
                        float ribs=.008f*Mathf.Sin(angle*10+phase)*t;
                        float rim=cup*t*t+ribs;
                        if(conform!=null)
                        {
                            // No tangent-plane fallback. A lobe that straddles a ledge edge
                            // shrinks until EVERY vertex has actual, nearby substrate below it.
                            if(!conform.TryTop(p.x,p.z,out float top)||Mathf.Abs(top-p.y)>radius*.55f+.025f)
                                supported=false;
                            else p.y=top+.012f+Mathf.Clamp(rim,-.004f,.045f);
                        }
                        else p+=axis*(rim-(underside?.026f:0));
                        points[(ring-1)*(sides+1)+side]=p;
                    }
                }
                if(supported)break;
                radius*=.64f;
                if(radius<.045f)return;
            }
            if(!supported)return;
            if(conform!=null)
            {
                if(!conform.TryTop(origin.x,origin.z,out float top))return;
                origin.y=top-.012f;
            }
            int first=g.positions.Count;
            Color centerTint=color*.58f;centerTint.a=0;
            int center=g.Add(origin,new Vector2(.5f,.5f),centerTint);
            for(int ring=1;ring<=rings;ring++)
            {
                bool underside=ring>faceRings;
                float t=underside?1:ring/(float)faceRings;
                for(int side=0;side<=sides;side++)
                {
                    float angle=side/(float)sides*Mathf.PI*2;
                    Color pigment=color*(underside?.53f:Mathf.Lerp(.65f,.94f,t));pigment.a=0;
                    g.Add(points[(ring-1)*(sides+1)+side],new Vector2(.5f+Mathf.Cos(angle)*t*.5f,.5f+Mathf.Sin(angle)*t*.5f),pigment);
                    if(ring==1&&side<sides)
                    {g.indices.Add(center);g.indices.Add(first+1+side+1);g.indices.Add(first+1+side);}
                    if(ring<rings&&side<sides)
                    {int q=first+1+(ring-1)*(sides+1)+side;g.Quad(q,q+1,q+sides+2,q+sides+1);}
                }
            }
        }

        static void AddPlatingColony(Geometry g,Vector3 root,Vector3 normal,float diameter,Color color,System.Random rng)
        {
            SurfaceBasis(normal,Range(rng,0,360),out Vector3 right,out Vector3 along);
            Vector3 axis=(normal*.52f+Vector3.up*.48f).normalized;
            Vector3 top=root+axis*diameter*.20f;
            AddTube(g,root,top,diameter*.055f,diameter*.027f,color,5,2);
            for(int plate=0;plate<2;plate++)
            {
                float angle=plate*2.39996f;
                Vector3 start=Vector3.Lerp(root,top,.55f+plate*.40f);
                Vector3 center=start+(right*Mathf.Cos(angle)+along*Mathf.Sin(angle))*diameter*.16f;
                AddTube(g,start,center,diameter*.03f,diameter*.018f,color,4,1);
                AddCoralLamina(g,center,axis,right,along,diameter*(.29f-plate*.035f),diameter*.025f,
                    Color.Lerp(color,surfacePalette[1],plate*.09f),angle+root.z);
            }
        }

        static void AddBranchingColony(Geometry g,Vector3 root,Vector3 normal,float diameter,Color color,System.Random rng)
        {
            SurfaceBasis(normal,Range(rng,0,360),out Vector3 right,out Vector3 along);
            Vector3 axis=(normal*.40f+Vector3.up*.60f).normalized;
            for(int branch=0;branch<3;branch++)
            {
                float angle=branch*2.39996f;
                Vector3 radial=right*Mathf.Cos(angle)+along*Mathf.Sin(angle);
                Vector3 top=root+axis*diameter*Range(rng,.43f,.72f)+radial*diameter*.24f;
                AddTube(g,root,top,diameter*.043f,diameter*.009f,color,5,2);
                for(int twig=0;twig<2;twig++)
                {
                    Vector3 from=Vector3.Lerp(root,top,.42f+twig*.21f);
                    Vector3 tip=from+axis*diameter*.26f+radial*diameter*(twig==0?-.22f:.24f);
                    AddTube(g,from,tip,diameter*.02f,diameter*.004f,color,4,2);
                }
            }
        }

        static void AddSmallFan(Geometry g,Vector3 root,Vector3 normal,float diameter,Color color,System.Random rng)
        {
            SurfaceBasis(normal,Range(rng,0,360),out Vector3 right,out Vector3 along);
            Vector3 axis=(normal*.35f+Vector3.up*.65f).normalized;
            Vector3 junction=root+axis*diameter*.30f;
            AddTube(g,root,junction,diameter*.037f,diameter*.025f,color,5,2);
            for(int branch=0;branch<5;branch++)
            {
                float spread=(branch-2)*.5f;
                Vector3 top=root+axis*diameter*(.84f-.22f*Mathf.Abs(spread))+right*diameter*spread*.49f;
                AddTube(g,junction,top,diameter*.024f,diameter*.004f,color,4,2);
                Vector3 twig=Vector3.Lerp(junction,top,.57f);
                AddTube(g,twig,twig+axis*diameter*.18f+right*diameter*(branch%2==0?-.13f:.13f),
                    diameter*.011f,diameter*.003f,color,4,1);
            }
        }

        static void AddSmallSponges(Geometry g,Vector3 root,Vector3 normal,float diameter,Color color,System.Random rng)
        {
            SurfaceBasis(normal,Range(rng,0,360),out Vector3 right,out Vector3 along);
            Vector3 axis=(normal*.45f+Vector3.up*.55f).normalized;
            const int sides=8;
            for(int sponge=0;sponge<3;sponge++)
            {
                Vector3 from=root+(right*Mathf.Cos(sponge*2.4f)+along*Mathf.Sin(sponge*2.4f))*diameter*.15f;
                // Every trumpet shares the buried colony base; leaning stems join it.
                AddTube(g,root,from,diameter*.035f,diameter*.045f,color,4,1);
                float height=diameter*Range(rng,.38f,.66f),radius=diameter*Range(rng,.10f,.15f);
                int first=g.positions.Count;
                for(int ring=0;ring<5;ring++)
                {
                    float t=ring==0?0:ring==1?.52f:ring==2?1:ring==3?.97f:.65f;
                    float r=radius*(ring==0?.66f:ring==1?.86f:ring==2?1.08f:ring==3?.69f:.40f);
                    Vector3 center=from+axis*(height*t)+right*(height*.13f*t*t);
                    for(int side=0;side<=sides;side++)
                    {
                        float a=side/(float)sides*Mathf.PI*2;
                        Color tint=color*(ring<3?Mathf.Lerp(.56f,1.05f,t):ring==3?.70f:.25f);tint.a=0;
                        Vector3 p=center+(right*Mathf.Cos(a)+along*Mathf.Sin(a))*r*(1+.07f*Mathf.Sin(a*3+sponge));
                        g.Add(p,new Vector2(side/(float)sides,t),tint);
                        if(ring<4&&side<sides){int q=first+ring*(sides+1)+side;g.Quad(q,q+sides+1,q+sides+2,q+1);}
                    }
                }
            }
        }

        readonly struct AmbientContactFootprint
        {
            readonly Vector2 center;
            readonly float cosine,sine,inverseWidth,inverseDepth,minimumRadius,broadRadius,seed,strength;
            public Vector2 Center=>center;
            public float BroadRadius=>broadRadius;
            public AmbientContactFootprint(Island island)
            {
                center=new Vector2(island.center.x,island.center.z);cosine=Mathf.Cos(island.angle);sine=Mathf.Sin(island.angle);
                inverseWidth=1f/island.width;inverseDepth=1f/island.depth;
                minimumRadius=Mathf.Min(island.width,island.depth);
                broadRadius=Mathf.Max(island.width,island.depth)*1.18f+1.3f;seed=island.seed;
                strength=island.height<1f?.15f:.40f;
            }
            public float Sample(float x,float z)
            {
                float dx=x-center.x,dz=z-center.y;
                if(Mathf.Abs(dx)>broadRadius||Mathf.Abs(dz)>broadRadius)return 1f;
                float u=(dx*cosine+dz*sine)*inverseWidth,v=(-dx*sine+dz*cosine)*inverseDepth;
                float r=Mathf.Pow(Mathf.Pow(Mathf.Abs(u),1.72f)+Mathf.Pow(Mathf.Abs(v),1.72f),1f/1.72f);
                float angle=Mathf.Atan2(v,u);
                // Matches the same authored eroded base footprint used by Island.Surface.
                // This is soft ambient contact, not a sun projection or a physical shadow.
                float edge=1f+.12f*Mathf.Sin(angle*3f+seed)+.055f*Mathf.Sin(angle*7f-seed);
                float outside=Mathf.Max(0f,(r-edge)*minimumRadius);
                float feather=Mathf.SmoothStep(0f,1f,Mathf.Clamp01(outside/1.25f));
                return 1f-strength*(1f-feather);
            }
        }

        static IEnumerator BakeGroundAmbientContact(OceanSanctuaryResources resources,List<Island> islands,
            Vector3 spawn,bool staged,Action<float> progress)
        {
            // Preserve the original ~0.78 m contact texel size over the full larger map.
            // Spatial buckets keep the larger texture from testing every reef for each pixel.
            const int resolution=320,bucketCount=24;const float extent=248f;
            float minX=spawn.x-extent*.5f,minZ=spawn.z-extent*.5f;
            var buckets=new List<AmbientContactFootprint>[bucketCount*bucketCount];
            foreach(Island island in islands)
            {
                var footprint=new AmbientContactFootprint(island);
                int x0=Mathf.Clamp(Mathf.FloorToInt((footprint.Center.x-footprint.BroadRadius-minX)/extent*bucketCount),0,bucketCount-1);
                int x1=Mathf.Clamp(Mathf.FloorToInt((footprint.Center.x+footprint.BroadRadius-minX)/extent*bucketCount),0,bucketCount-1);
                int z0=Mathf.Clamp(Mathf.FloorToInt((footprint.Center.y-footprint.BroadRadius-minZ)/extent*bucketCount),0,bucketCount-1);
                int z1=Mathf.Clamp(Mathf.FloorToInt((footprint.Center.y+footprint.BroadRadius-minZ)/extent*bucketCount),0,bucketCount-1);
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                {
                    int slot=z*bucketCount+x;
                    if(buckets[slot]==null)buckets[slot]=new List<AmbientContactFootprint>(8);
                    buckets[slot].Add(footprint);
                }
            }
            var pixels=new Color32[resolution*resolution];
            for(int z=0;z<resolution;z++)
            {
                float worldZ=minZ+(z+.5f)/resolution*extent;
                for(int x=0;x<resolution;x++)
                {
                    float worldX=minX+(x+.5f)/resolution*extent,occlusion=1f;
                    int slot=Mathf.Min(bucketCount-1,(int)((z+.5f)/resolution*bucketCount))*bucketCount+
                        Mathf.Min(bucketCount-1,(int)((x+.5f)/resolution*bucketCount));
                    List<AmbientContactFootprint> candidates=buckets[slot];
                    if(candidates!=null)foreach(AmbientContactFootprint footprint in candidates)
                        occlusion=Mathf.Min(occlusion,footprint.Sample(worldX,worldZ));
                    byte value=(byte)Mathf.RoundToInt(occlusion*255f);
                    pixels[z*resolution+x]=new Color32(value,value,value,255);
                }
                if(staged&&(z&7)==7){progress?.Invoke(.93f+.06f*(z+1f)/resolution);yield return null;}
            }
            var texture=new Texture2D(resolution,resolution,TextureFormat.RGBA32,false,true) {
                name="Sanctuary baked ground ambient contact",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,
                anisoLevel=0
            };
            resources.Own(texture);texture.SetPixels32(pixels);texture.Apply(false,true);
            resources.GroundOcclusion=texture;
            resources.GroundOcclusionRect=new Vector4(minX,minZ,1f/extent,1f/extent);
            // Arch AABBs are deliberately absent: they would incorrectly darken the open
            // swimming passage. Cached map roots retain this map with no per-frame CPU work.
        }

        static void AddRubble(Geometry g,Vector3 origin,float radius,System.Random rng)
        {
            const int sides=7,rings=4;
            int first=g.positions.Count;
            float stretch=Range(rng,.7f,1.5f),yaw=Range(rng,0,Mathf.PI*2f);
            Color color=new Color(.78f,.86f,.81f,0f)*Range(rng,.76f,.98f);color.a=0f;
            for(int ring=0;ring<=rings;ring++)
            {
                float latitude=ring/(float)rings*Mathf.PI*.5f;
                float reach=Mathf.Cos(latitude);
                for(int j=0;j<=sides;j++)
                {
                    float a=j/(float)sides*Mathf.PI*2f+yaw;
                    float broken=1f+.15f*Mathf.Sin(a*3f+origin.x*1.7f)+.08f*Mathf.Sin(a*5f-origin.z);
                    Vector3 point=origin+new Vector3(Mathf.Cos(a)*radius*reach*broken*stretch,
                        Mathf.Sin(latitude)*radius*.55f,Mathf.Sin(a)*radius*reach*broken);
                    g.Add(point,new Vector2(point.x*.7f,point.z*.7f),color);
                    if(ring<rings&&j<sides){int q=first+ring*(sides+1)+j;g.Quad(q,q+1,q+sides+2,q+sides+1);}
                }
            }
        }

        static Renderer CreateMesh(Transform root,string name,Mesh mesh,Material material,ShadowCastingMode shadows)
        {
            var go=new GameObject(name);go.transform.SetParent(root,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=shadows;renderer.receiveShadows=true;
            return renderer;
        }
        static void AddTube(Geometry g,Vector3 start,Vector3 end,float rootRadius,float tipRadius,Color color,int sides=6,int segments=4)
        {
            Vector3 tangent=(end-start).normalized;
            Vector3 right=Vector3.Cross(tangent,Mathf.Abs(tangent.y)>.95f?Vector3.forward:Vector3.up).normalized;
            Vector3 forward=Vector3.Cross(right,tangent).normalized;
            int first=g.positions.Count;
            for(int ring=0;ring<=segments;ring++)
            {
                float t=ring/(float)segments;
                Vector3 center=Vector3.Lerp(start,end,t)+right*(Mathf.Sin(t*Mathf.PI)*Vector3.Distance(start,end)*.065f);
                float radius=Mathf.Lerp(rootRadius,tipRadius,t);
                for(int j=0;j<=sides;j++)
                {
                    float a=j/(float)sides*Mathf.PI*2f;Color tint=color* Mathf.Lerp(.65f,1.12f,t);
                    // Shared junctions use the same world-height weight, avoiding separated moving branches.
                    tint.a=Mathf.Clamp01((center.y+3f)*.25f);
                    g.Add(center+(right*Mathf.Cos(a)+forward*Mathf.Sin(a))*radius,new Vector2(j/(float)sides,t),tint);
                    if(ring<segments&&j<sides){int q=first+ring*(sides+1)+j;g.Quad(q,q+sides+1,q+sides+2,q+1);}
                }
            }
        }
        static void AddSeaFan(Geometry g,Vector3 origin,float height,float yaw,Color color,System.Random rng)
        {
            Quaternion turn=Quaternion.Euler(0,yaw,0);
            Vector3 trunk=origin+Vector3.up*height*.42f;
            AddTube(g,origin,trunk,.048f,.028f,color);
            for(int branch=0;branch<7;branch++)
            {
                float spread=(branch-3)/3f;
                Vector3 end=origin+turn*new Vector3(spread*height*.65f,height*(.98f-Mathf.Abs(spread)*.22f),Range(rng,-.06f,.06f));
                AddTube(g,trunk,end,.027f,.009f,color);
                for(int twig=0;twig<4;twig++)
                {
                    Vector3 basePoint=Vector3.Lerp(trunk,end,.25f+twig*.17f);
                    Vector3 tip=basePoint+turn*new Vector3((twig%2==0?-1:1)*height*.14f,height*.19f,Range(rng,-.04f,.04f));
                    AddTube(g,basePoint,tip,.013f,.003f,color,5,3);
                }
            }
        }
        static void AddSoftCoral(Geometry g,Vector3 origin,float height,Color color,System.Random rng)
        {
            for(int branch=0;branch<8;branch++)
            {
                float a=branch*2.39996f;float h=height*Range(rng,.55f,1f);
                Vector3 end=origin+new Vector3(Mathf.Cos(a)*height*.32f,h,Mathf.Sin(a)*height*.32f);
                AddTube(g,origin,end,.06f,.025f,color,7,5);
                for(int twig=0;twig<3;twig++)
                {
                    Vector3 from=Vector3.Lerp(origin,end,.4f+twig*.19f);
                    AddTube(g,from,from+new Vector3(Mathf.Cos(a+twig)*.14f,h*.23f,Mathf.Sin(a+twig)*.14f),.025f,.009f,color,6,3);
                }
            }
        }
        static void AddAnemone(Geometry g,Vector3 origin,float width,Color color,System.Random rng,Func<float,float,float> support)
        {
            // Curved, tapered tentacles form a low shelter rather than a fluorescent grass patch.
            for(int tentacle=0;tentacle<34;tentacle++)
            {
                float a=tentacle*2.39996f;
                float reach=Mathf.Sqrt((tentacle+.5f)/34f)*width*.65f;
                Vector3 radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                Vector3 basePoint=origin+radial*reach*.8f;
                basePoint.y=support(basePoint.x,basePoint.z)-.045f;
                float height=Range(rng,.32f,.61f);
                const int segments=12,sides=7;
                int first=g.positions.Count;
                for(int ring=0;ring<=segments;ring++)
                {
                    float t=ring/(float)segments;
                    Vector3 center=basePoint+Vector3.up*(height*t)+radial*(Mathf.Sin(t*Mathf.PI*.8f)*.2f);
                    Vector3 tangent=(Vector3.up*height+radial*(Mathf.Cos(t*Mathf.PI*.8f)*.2f*Mathf.PI*.8f)).normalized;
                    Vector3 right=Vector3.Cross(tangent,Vector3.forward).normalized;
                    Vector3 along=Vector3.Cross(right,tangent).normalized;
                    float radius=Mathf.Lerp(.034f,.008f,t);
                    for(int j=0;j<=sides;j++)
                    {
                        float azimuth=j/(float)sides*Mathf.PI*2f;
                        Color tint=color*Mathf.Lerp(.68f,1.05f,t);tint.a=Mathf.Clamp01((center.y+3f)*.25f);
                        g.Add(center+(right*Mathf.Cos(azimuth)+along*Mathf.Sin(azimuth))*radius,new Vector2(j/(float)sides,t),tint);
                        if(ring<segments&&j<sides){int q=first+ring*(sides+1)+j;g.Quad(q,q+sides+1,q+sides+2,q+1);}
                    }
                }
            }
        }
        static void AddSponges(Geometry g,Vector3 origin,float height,Color color,System.Random rng,Func<float,float,float> support)
        {
            const int sides=10,rings=14;
            for(int tube=0;tube<5;tube++)
            {
                float a=tube*2.39996f;float r=height*Range(rng,.09f,.20f);float h=height*Range(rng,.46f,1.05f);
                Vector3 p=origin+new Vector3(Mathf.Cos(a)*height*.3f,0,Mathf.Sin(a)*height*.3f);
                p.y=support(p.x,p.z)-.045f;
                Vector3 lean=new Vector3(Mathf.Cos(a+.4f),0,Mathf.Sin(a+.4f))*h*Range(rng,.13f,.25f);
                Vector3 bow=new Vector3(-Mathf.Sin(a),0,Mathf.Cos(a))*h*.075f;
                Color pigment=Color.Lerp(color,new Color(.67f,.39f,.26f),.28f);
                int first=g.positions.Count;
                for(int ring=0;ring<rings;ring++)
                {
                    // A curved, irregular trumpet wall rolls into a thick wavy lip and a
                    // recessed dark cavity. Radius/centre/rim all vary, avoiding straight PVC silhouettes.
                    bool outside=ring<=8;
                    float t=outside?ring/8f:ring==9?.985f:ring==10?.88f:ring==11?.72f:ring==12?.66f:.65f;
                    float radius=outside?r*(.58f+.48f*t+.1f*Mathf.Sin(t*Mathf.PI)-.08f*Mathf.Sin(t*Mathf.PI*2f)):
                        ring==9?r*.76f:ring==10?r*.70f:ring==11?r*.56f:ring==12?r*.46f:0f;
                    Vector3 center=p+Vector3.up*(h*t)+lean*(t*t)+bow*Mathf.Sin(t*Mathf.PI);
                    for(int j=0;j<=sides;j++)
                    {
                        float phi=j/(float)sides*Mathf.PI*2f;
                        float ripple=1f+.075f*Mathf.Sin(phi*5f+tube+t*2f)+.035f*Mathf.Sin(phi*9f-t*3f);
                        float rim=(ring==8||ring==9)?h*(.027f*Mathf.Sin(phi*3f+tube)+.014f*Mathf.Sin(phi*5f-a)):0f;
                        float mottling=.94f+.06f*Mathf.Sin(phi*4f+t*11f+tube);
                        Color tint=pigment*((outside?Mathf.Lerp(.53f,.88f,t):ring==9?.71f:.21f)*mottling);tint.a=t;
                        g.Add(center+new Vector3(Mathf.Cos(phi)*radius*ripple,rim,Mathf.Sin(phi)*radius*ripple),new Vector2(j/(float)sides,t*2f),tint);
                        if(ring<rings-1&&j<sides){int q=first+ring*(sides+1)+j;g.Quad(q,q+sides+1,q+sides+2,q+1);}
                    }
                }
            }
        }
        static float Range(System.Random rng,float low,float high)=>low+(float)rng.NextDouble()*(high-low);
    }

    /// <summary>Owns only generated sanctuary resources, never imported meshes or source materials.</summary>
    public sealed class OceanSanctuaryResources : MonoBehaviour
    {
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        public Texture2D GroundOcclusion { get; internal set; }
        /// <summary>(world min X, world min Z, inverse world width, inverse world depth).</summary>
        public Vector4 GroundOcclusionRect { get; internal set; }
        public void Own(UnityEngine.Object asset){if(asset!=null)owned.Add(asset);}
        void OnDestroy()
        {
            foreach(UnityEngine.Object asset in owned)
                if(asset!=null){if(Application.isPlaying)Destroy(asset);else DestroyImmediate(asset);}
            owned.Clear();
        }
    }
}

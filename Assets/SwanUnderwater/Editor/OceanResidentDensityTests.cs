#if UNITY_EDITOR || OCEAN_DENSITY_PURE_TESTS
using System;
using System.Collections.Generic;

namespace SwanUnderwater.EditorTests
{
    public static class OceanResidentDensityTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool value,string message)
        { Assertions++;if(!value)throw new InvalidOperationException("Resident density: "+message); }

        public static int RunAll()
        {
            Assertions=0;
            Check(OceanResidentDensityPolicy.HeroCount(OceanEnvironmentKind.Classic,120)==120,"classic keeps its existing hero budget");
            Check(OceanResidentDensityPolicy.HeroCount(OceanEnvironmentKind.CoralSanctuary,120)==144,"sanctuary has a bounded increase of 24 heroes");
            Check(OceanResidentDensityPolicy.HeroCount(OceanEnvironmentKind.CoralSanctuary,500)==144,"legacy settings cannot bypass the bounded plan");
            Check(OceanResidentDensityPolicy.SchoolCount(12,1200)==18,"school count changes without increasing fish total");
            Check(OceanResidentDensityPolicy.SchoolCount(12,10)==10,"tiny configured populations cannot create empty school indexing");
            foreach(OceanEnvironmentKind environment in new[] { OceanEnvironmentKind.Classic, OceanEnvironmentKind.CoralSanctuary })
            {
                var planned=new HashSet<string>();
                for(int i=0;i<144;i++)
                {
                    string resource=OceanResidentDensityPolicy.HeroResourceName(i,environment);
                    Check(resource.IndexOf("moray",StringComparison.OrdinalIgnoreCase)<0&&
                        resource.IndexOf("eel",StringComparison.OrdinalIgnoreCase)<0,
                        "retired unverified moray swim is never planned in either environment: "+environment+" slot="+i);
                    planned.Add(resource);
                }
                Check(planned.Contains("21_ocean_sunfish"),"existing ocean sunfish replaces the retired slot without removing fish: "+environment);
                Check(planned.Count==9&&OceanResidentDensityPolicy.MarineHeroSpecies==9,"replacement retains nine distinct marine hero categories: "+environment);
            }
            string[] marine={"01_clownfish","02_blue_tang","23_mandarinfish","03_lionfish","05_pufferfish_inflated",
                "20_seahorse","22_flounder","21_ocean_sunfish","04_anglerfish"};
            var allowed=new HashSet<string>(marine);
            var counts=new int[7];var species=new HashSet<string>[7];
            for(int z=0;z<7;z++)species[z]=new HashSet<string>();
            for(int i=0;i<144;i++)
            {
                int zone=OceanResidentDensityPolicy.HeroZone(i),local=OceanResidentDensityPolicy.HeroLocalOrdinal(i);
                string name=OceanResidentDensityPolicy.HeroResourceName(i,OceanEnvironmentKind.CoralSanctuary);
                Check(zone>=0&&zone<7&&local>=0,"zone and local ordinal stay in bounds");
                Check(allowed.Contains(name),"ambient hero plan only uses admitted marine resources");
                Check(name==OceanResidentDensityPolicy.HeroResourceName(i,OceanEnvironmentKind.CoralSanctuary),"species selection is repeatable without RNG");
                float length=OceanResidentDensityPolicy.HeroLength(name,i,.5f);
                Check(length>=.27f&&length<=1.0f,"bounded near/mid body envelope instead of giant reef eels");
                float radius=OceanResidentDensityPolicy.LocalRadius(local);
                Check(radius>=3.5f&&radius<=16.4f,"district layout stays within the authored reef area");
                counts[zone]++;species[zone].Add(name);
            }
            for(int z=0;z<7;z++)
            {
                Check(counts[z]==(z==0?30:19),"all seven districts receive their complete bounded allocation");
                Check(species[z].Count==9,"all seven districts contain nine different marine hero resources");
            }
            var classic=new HashSet<string>();
            for(int i=0;i<120;i++)classic.Add(OceanResidentDensityPolicy.HeroResourceName(i,OceanEnvironmentKind.Classic));
            Check(classic.Count==9,"classic includes all nine available marine hero types");
            var reefGroups=new int[7];int open=0;
            for(int s=0;s<18;s++)
            {
                int zone=OceanResidentDensityPolicy.SchoolZone(s);
                if(OceanResidentDensityPolicy.IsSchoolReefResident(s))reefGroups[zone]++;else open++;
                for(int member=0;member<67;member++)
                {
                    float size=OceanResidentDensityPolicy.SchoolLength(s,member);
                    Check(size>=.08999f&&size<=.15001f,"school bodies stay in the 9–15 cm envelope budget");
                }
            }
            Check(open==4,"four existing open-water patrols retained");
            foreach(int count in reefGroups)Check(count>=1,"each district has its own fixed reef school");
            Check(OceanResidentDensityPolicy.HeroZone(143,1)==0&&OceanResidentDensityPolicy.SchoolZone(17,1)==0,"single-zone fallback has no modulo-zero");
            Check(OceanResidentDensityPolicy.IsBottomResident("22_flounder")&&OceanResidentDensityPolicy.IsBottomResident("20_seahorse"),"sand and grass residents stay in the low-water band");
            Check(!OceanResidentDensityPolicy.IsBottomResident("02_blue_tang"),"reef-water fish are not flattened into the benthic band");
            Console.WriteLine("OceanResidentDensityTests policy PASS "+Assertions+" assertions; deterministic plan only, no rendering/performance claim.");
            return Assertions;
        }
#if UNITY_EDITOR
        public static int RunInScene(UnderwaterWorld world)
        {
            RunAll();
            Check(world!=null&&world.IsReady,"actual ready world required");
            var snapshot=world.GetResidentDensitySnapshot();
            // Publish actual per-zone species/counts BEFORE an assertion, including partial builds.
            UnityEngine.Debug.Log("[ResidentDensity] "+UnityEngine.JsonUtility.ToJson(snapshot));
            Check(snapshot.heroCount==OceanResidentDensityPolicy.HeroCount(world.ActiveEnvironment,120),"actual hero root count matches the current environment budget");
            Check(snapshot.schoolCount==18&&snapshot.schoolingFishCount==1200,"actual 18 schools retain the existing 1200-fish budget");
            var resources=new HashSet<string>();
            var zoneSpecies=new HashSet<string>[snapshot.heroesByZone.Length];
            for(int z=0;z<zoneSpecies.Length;z++)zoneSpecies[z]=new HashSet<string>();
            foreach(var item in snapshot.species)
            {
                Check(item.resourceName.IndexOf("moray",StringComparison.OrdinalIgnoreCase)<0&&
                    item.resourceName.IndexOf("eel",StringComparison.OrdinalIgnoreCase)<0,
                    "actual hero root does not retain a retired moray instance");
                resources.Add(item.resourceName);zoneSpecies[item.zone].Add(item.resourceName);
            }
            Check(resources.Count==9,"actual hero population uses nine marine source resources");
            Check(resources.Contains("21_ocean_sunfish"),"actual existing sunfish fills the retired slot at the unchanged fish budget");
            if(world.ActiveEnvironment==OceanEnvironmentKind.CoralSanctuary)
            {
                for(int z=0;z<snapshot.heroesByZone.Length;z++)
                {
                    Check(snapshot.heroesByZone[z]>=12,"every actual district has near/mid hero fish");
                    Check(zoneSpecies[z].Count>=6,"every actual district retains distinct silhouettes");
                    Check(snapshot.schoolsByZone[z]>=1,"every actual district retains a school");
                }
                Check(snapshot.maximumHeroRouteDistance<=25f,"actual homes stay near authored exploration corridors");
            }
            UnityEngine.Debug.Log("OceanResidentDensityTests scene PASS "+Assertions+" assertions; actual population/placement, no headset density or performance acceptance.");
            return Assertions;
        }
#endif
    }
}
#endif

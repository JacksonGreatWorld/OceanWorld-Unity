using System;
using System.Collections.Generic;
using System.Text;

namespace SwanUnderwater
{
    [Serializable]
    public sealed class OceanLeaderboardRow
    {
        public int rank;
        public string publicId, nickname;
        public long shellTotal, fishTotal, coinTotal, score, demoStarts;
        public int discoveryCount;
        internal static OceanLeaderboardRow Parse(object source, bool ranked=true)
        {
            var v=OceanLeaderboardJson.Object(source);
            long r=OceanLeaderboardJson.Number(v,"rank",0);
            string id=OceanLeaderboardJson.Text(v,"publicId"),name=OceanLeaderboardJson.Text(v,"nickname");
            long shells=OceanLeaderboardJson.Number(v,"shellTotal"),fish=OceanLeaderboardJson.Number(v,"fishTotal"),coin=OceanLeaderboardJson.Number(v,"coinTotal"),score=OceanLeaderboardJson.Number(v,"score"),demos=OceanLeaderboardJson.Number(v,"demoStarts");
            if(OceanLeaderboardJson.Number(v,"scoringVersion",0)!=2||shells<0||shells>900719925474099L||fish>900719925474099L||!OceanLeaderboardLedger.Hex(id,12)||!OceanLeaderboardLedger.ValidNickname(name,out _)||r>int.MaxValue||(ranked&&r<1)||fish<0||coin<0||demos<0||coin>900719925474099L||score!=shells+fish*2+coin*10)
                throw new FormatException("Invalid leaderboard row.");
            return new OceanLeaderboardRow { rank=(int)r,publicId=id,nickname=name,shellTotal=shells,fishTotal=fish,coinTotal=coin,score=score,demoStarts=demos };
        }
        internal static OceanLeaderboardRow ParseDiscovery(object source,int catalogVersion,bool ranked=true)
        {
            int speciesCount=OceanSpeciesCatalog.CountForVersion(catalogVersion);
            var v=OceanLeaderboardJson.Object(source);long rank=OceanLeaderboardJson.Number(v,"rank",0),count=OceanLeaderboardJson.Number(v,"discoveryCount");
            string id=OceanLeaderboardJson.Text(v,"publicId"),name=OceanLeaderboardJson.Text(v,"nickname");
            if(OceanLeaderboardJson.Number(v,"catalogVersion")!=catalogVersion||OceanLeaderboardJson.Number(v,"speciesCount")!=speciesCount||count<0||count>speciesCount||!OceanLeaderboardLedger.Hex(id,12)||!OceanLeaderboardLedger.ValidNickname(name,out _)||rank<0||rank>int.MaxValue||(ranked&&rank<1))throw new FormatException("Invalid discovery leaderboard row.");
            return new OceanLeaderboardRow {rank=(int)rank,publicId=id,nickname=name,discoveryCount=(int)count};
        }
    }
    [Serializable]
    public sealed class OceanLeaderboardPage
    {
        public bool IsDiscovery { get; private set; }
        public int CatalogVersion { get; private set; }
        public int SpeciesCount { get; private set; }
        public OceanLeaderboardRow[] entries;
        public string nextCursor, serverTime;
        public long totalDevices, totalDemoStarts;
        public OceanLeaderboardRow myEntry;
        public static OceanLeaderboardPage CreateLocal(OceanLeaderboardRow row, bool discovery)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            return new OceanLeaderboardPage { IsDiscovery = discovery,
                CatalogVersion = discovery ? OceanSpeciesCatalog.CatalogVersion : 0,
                SpeciesCount = discovery ? OceanSpeciesCatalog.Count : 0,
                entries = new[] { row }, myEntry = row, nextCursor = "", serverTime = "本机记录",
                totalDevices = 1, totalDemoStarts = row.demoStarts };
        }
        public static OceanLeaderboardPage Parse(string json,bool allowLegacyDiscovery=false)
        {
            if(json==null||json.Length>262144) throw new FormatException("Oversized leaderboard page.");
            var v=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(json));
            bool discovery=OceanLeaderboardJson.Text(v,"board","")=="discoveries";
            int catalogVersion=0,speciesCount=0;
            if(discovery)
            {
                long suppliedVersion=OceanLeaderboardJson.Number(v,"catalogVersion",0);
                if(suppliedVersion!=OceanSpeciesCatalog.CatalogVersion&&!(allowLegacyDiscovery&&suppliedVersion>=1&&suppliedVersion<OceanSpeciesCatalog.CatalogVersion))throw new FormatException("Discovery catalog mismatch.");
                catalogVersion=(int)suppliedVersion;speciesCount=OceanSpeciesCatalog.CountForVersion(catalogVersion);
                if(OceanLeaderboardJson.Number(v,"speciesCount",0)!=speciesCount)throw new FormatException("Discovery denominator mismatch.");
            }
            else if(OceanLeaderboardJson.Number(v,"scoringVersion",0)!=2)throw new FormatException("Leaderboard scoring version is not 2.");
            var entries=OceanLeaderboardJson.Array(v,"entries"); if(entries.Count>100) throw new FormatException("Too many rows.");
            var rows=new OceanLeaderboardRow[entries.Count];var ids=new HashSet<string>();
            for(int i=0;i<rows.Length;i++) { rows[i]=discovery?OceanLeaderboardRow.ParseDiscovery(entries[i],catalogVersion):OceanLeaderboardRow.Parse(entries[i]);if(!ids.Add(rows[i].publicId))throw new FormatException("Duplicate public row."); }
            if(!v.TryGetValue("totals",out object totals))throw new FormatException("Missing totals.");
            var t=OceanLeaderboardJson.Object(totals);long devices=OceanLeaderboardJson.Number(t,"devices"),demos=OceanLeaderboardJson.Number(t,"demoStarts");
            string cursor=OceanLeaderboardJson.Text(v,"nextCursor",""),time=OceanLeaderboardJson.Text(v,"serverTime","");
            if(devices<0||demos<0||cursor.Length>4096||time.Length>64)throw new FormatException("Invalid page metadata.");
            return new OceanLeaderboardPage { IsDiscovery=discovery,CatalogVersion=catalogVersion,SpeciesCount=speciesCount,entries=rows,nextCursor=cursor,serverTime=time,totalDevices=devices,totalDemoStarts=demos,
                myEntry=v.TryGetValue("myEntry",out object me)&&me!=null?(discovery?OceanLeaderboardRow.ParseDiscovery(me,catalogVersion,false):OceanLeaderboardRow.Parse(me,false)):null };
        }
    }
    public static class OceanLeaderboardProtocol
    {
        public static string ValidateBaseUrl(string value)
        {
            if(!Uri.TryCreate(value,UriKind.Absolute,out Uri uri)||uri.Scheme!=Uri.UriSchemeHttps||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0)
                throw new ArgumentException("Leaderboard requires an HTTPS base URL without credentials, query or fragment.");
            return uri.AbsoluteUri.TrimEnd('/');
        }
        public static byte[] Payload(string deviceId,IList<OceanLeaderboardEvent> batch)
        {
            if(!OceanLeaderboardLedger.Hex(deviceId,64)||batch==null||batch.Count<1||batch.Count>64)throw new ArgumentException("Invalid signed event batch.");
            var events=new List<object>();long previous=0;
            foreach(var e in batch) { if(e.sequence<=previous)throw new ArgumentException("Events must be sequence ordered.");previous=e.sequence;events.Add(e.ToJson()); }
            return new UTF8Encoding(false,true).GetBytes(OceanLeaderboardJson.Write(new Dictionary<string,object>{{"v",1},{"scoringVersion",2},{"op","events"},{"deviceId",deviceId},{"events",events}}));
        }
        public static byte[] PagePayload(string deviceId,string cursor,int limit,bool discoveries=false)
        {
            if(!OceanLeaderboardLedger.Hex(deviceId,64)||cursor==null||cursor.Length>4096||limit<1||limit>100)throw new ArgumentException("Invalid signed page request.");
            var payload=new Dictionary<string,object>{{"v",1},{"op",discoveries?"discovery_board":"leaderboard"},{"deviceId",deviceId},{"cursor",cursor},{"limit",limit}};
            payload[discoveries?"catalogVersion":"scoringVersion"]=discoveries?OceanSpeciesCatalog.CatalogVersion:2;
            return new UTF8Encoding(false,true).GetBytes(OceanLeaderboardJson.Write(payload));
        }
        public static byte[] DiscoveryPayload(string deviceId,List<object> events)
        {
            if(!OceanLeaderboardLedger.Hex(deviceId,64)||events==null||events.Count<1||events.Count>OceanSpeciesCatalog.Count)throw new ArgumentException("Invalid discovery batch.");
            return new UTF8Encoding(false,true).GetBytes(OceanLeaderboardJson.Write(new Dictionary<string,object>{{"v",1},{"op","discovery_events"},{"catalogVersion",OceanSpeciesCatalog.CatalogVersion},{"deviceId",deviceId},{"events",events}}));
        }
        public static byte[] Envelope(string publicKeySpkiB64,byte[] payload,byte[] signature) => new UTF8Encoding(false,true).GetBytes(OceanLeaderboardJson.Write(new Dictionary<string,object> {
            {"publicKeySpkiB64",publicKeySpkiB64},{"payloadB64",Convert.ToBase64String(payload)},{"signatureB64",Convert.ToBase64String(signature)} }));
    }
}

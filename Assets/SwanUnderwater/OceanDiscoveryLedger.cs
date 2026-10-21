using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;

namespace SwanUnderwater
{
    /// <summary>Separate bounded durable outbox. An old server never sees these as score events.</summary>
    public sealed class OceanDiscoveryLedger
    {
        const string FileName="ocean-discoveries.json";
        readonly string directory;
        readonly Dictionary<int,Dictionary<string,object>> records=new Dictionary<int,Dictionary<string,object>>();
        bool primaryVerified;
        public bool Writable { get; private set; }=true;
        public string LastError { get; private set; }
        public string DeviceId { get; private set; }="";
        public string CachedPageJson { get; private set; }="";
        public string CachedPageCursor { get; private set; }="";
        public int Count => records.Count;
        public int RejectedCount { get {int n=0;foreach(var e in records.Values)if((string)e["status"]=="rejected")n++;return n;} }
        public int PendingCount { get { int n=0;foreach(var r in records.Values)if((string)r["status"]=="pending")n++;return n; } }
        sealed class FutureSchema : Exception { public FutureSchema():base("Newer discovery data preserved; upgrade required.") {} }
        public OceanDiscoveryLedger(string path)
        {
            directory=Path.GetFullPath(path);string file=Path.Combine(directory,FileName);
            if(!File.Exists(file)&&!File.Exists(file+".backup"))return;
            try { Read(file);primaryVerified=true; }
            catch(FutureSchema error) { Writable=false;LastError=error.Message; }
            catch(Exception first)
            {
                try { Read(file+".backup");LastError="Recovered discovery backup: "+first.Message; }
                catch(Exception error) { Writable=false;LastError="Discovery files preserved: "+error.Message; }
            }
        }
        public bool BindIdentity(string id)
        {
            if(!OceanLeaderboardLedger.Hex(id,64))return Fail("Invalid discovery identity.");
            if(DeviceId==id)return true;
            if(DeviceId.Length>0){Writable=false;return Fail("Discovery identity changed; original evidence preserved.");}
            string prior=DeviceId;DeviceId=id;if(Save())return true;DeviceId=prior;return false;
        }
        public bool Record(string session,int species,int level)
        {
            if(species<0||species>=OceanSpeciesCatalog.Count||level<OceanSpeciesCatalog.Get(species).FirstLevel||level>OceanLevelCatalog.Count||!OceanLeaderboardLedger.Identifier(session))return Fail("Invalid discovery species, session or level.");
            if(records.ContainsKey(species))return true;
            if(!Writable)return false;
            var e=new Dictionary<string,object>{{"eventId",OceanLeaderboardLedger.Hash("discovery|"+species.ToString(CultureInfo.InvariantCulture))},{"species",species},{"level",level},{"sessionId",session},{"createdUtc",DateTime.UtcNow.ToString("o")},{"status","pending"},{"reason",""}};
            records.Add(species,e);if(Save())return true;records.Remove(species);return false;
        }
        public List<object> Batch(int maximumCampaignLevel=int.MaxValue)
        {
            var batch=new List<object>();
            foreach(var record in records.Values)if((string)record["status"]=="pending" && Convert.ToInt32(record["level"])<=maximumCampaignLevel)
            {var e=new Dictionary<string,object>(record);e.Remove("status");e.Remove("reason");batch.Add(e);}
            return batch;
        }
        public bool ApplyAcknowledgement(string json,HashSet<string> submitted)
        {
            string before=Snapshot();
            try
            {
                var root=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(json));
                if(OceanLeaderboardJson.Number(root,"catalogVersion")!=OceanSpeciesCatalog.CatalogVersion||OceanLeaderboardJson.Number(root,"speciesCount")!=OceanSpeciesCatalog.Count)throw new FormatException("Discovery catalog mismatch; outbox retained.");
                var complete=new HashSet<string>();var rejected=new Dictionary<string,string>();
                foreach(string key in new[]{"acceptedEventIds","duplicateEventIds"})foreach(object value in OceanLeaderboardJson.Array(root,key))
                {if(!(value is string id)||!submitted.Contains(id))continue;complete.Add(id);}
                foreach(object value in OceanLeaderboardJson.Array(root,"rejected"))
                {var e=OceanLeaderboardJson.Object(value);string id=OceanLeaderboardJson.Text(e,"eventId","");if(submitted.Contains(id)&&e.TryGetValue("retryable",out object retry)&&retry is bool b&&!b)rejected[id]=OceanLeaderboardJson.Text(e,"code","rejected");}
                foreach(var e in records.Values)
                {string id=(string)e["eventId"];if((string)e["status"]!="pending")continue;if(rejected.TryGetValue(id,out string reason)){e["status"]="rejected";e["reason"]=reason;}else if(complete.Contains(id))e["status"]="accepted";}
                if(Save())return true;Restore(before);return false;
            }
            catch(Exception error){Restore(before);return Fail("Invalid discovery acknowledgement: "+error.Message);}
        }
        public bool CachePage(string cursor,string json)
        {
            try { var page=OceanLeaderboardPage.Parse(json);if(!page.IsDiscovery)throw new FormatException("Expected discovery board."); }
            catch(Exception error){return Fail(error.Message);}
            string prior=CachedPageJson,priorCursor=CachedPageCursor;CachedPageJson=json;CachedPageCursor=cursor??"";
            if(Save())return true;CachedPageJson=prior;CachedPageCursor=priorCursor;return false;
        }
        string Snapshot()=>OceanLeaderboardJson.Write(new Dictionary<string,object>{{"schema",2},{"catalogVersion",OceanSpeciesCatalog.CatalogVersion},{"deviceId",DeviceId},{"records",new List<object>(records.Values)},{"page",CachedPageJson},{"cursor",CachedPageCursor}});
        bool Save()
        {
            if(!Writable)return false;string temp=null,bt=null;
            try
            {
                Directory.CreateDirectory(directory);string file=Path.Combine(directory,FileName),backup=file+".backup",body=Snapshot();
                byte[] bytes=new UTF8Encoding(false,true).GetBytes(OceanLeaderboardJson.Write(new Dictionary<string,object>{{"body",body},{"sha256",OceanLeaderboardLedger.Hash(body)}}));
                temp=file+".tmp-"+Guid.NewGuid().ToString("N");Write(temp,bytes);
                if(File.Exists(file))File.Replace(temp,file,primaryVerified?backup:null);
                else {bt=backup+".tmp-"+Guid.NewGuid().ToString("N");Write(bt,bytes);if(File.Exists(backup))File.Replace(bt,backup,null);else File.Move(bt,backup);bt=null;File.Move(temp,file);}
                temp=null;primaryVerified=true;LastError=null;return true;
            }
            catch(Exception error){return Fail("Discovery persistence failed: "+error.Message);}
            finally {Delete(temp);Delete(bt);}
        }
        static void Write(string file,byte[] bytes){using(var s=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None)){s.Write(bytes,0,bytes.Length);s.Flush(true);}}
        static void Delete(string file){try{if(file!=null&&File.Exists(file))File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}}
        void Read(string file)
        {
            if(!File.Exists(file)||new FileInfo(file).Length>524288)throw new IOException("Missing or oversized discovery file.");
            var root=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(File.ReadAllText(file,new UTF8Encoding(false,true))));string body=OceanLeaderboardJson.Text(root,"body");
            if(OceanLeaderboardLedger.Hash(body)!=OceanLeaderboardJson.Text(root,"sha256"))throw new FormatException("Discovery checksum mismatch.");Restore(body);
        }
        void Restore(string json)
        {
            var root=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(json));long schema=OceanLeaderboardJson.Number(root,"schema");
            long catalog=schema==1?OceanLeaderboardJson.Number(root,"catalogVersion",1):OceanLeaderboardJson.Number(root,"catalogVersion");
            if((schema!=1&&schema!=2)||catalog<1||catalog>OceanSpeciesCatalog.CatalogVersion)throw new FutureSchema();
            if((schema==1&&catalog!=1)||(schema==2&&catalog<2))throw new FormatException("Partial discovery catalog migration.");
            int supportedCount=OceanSpeciesCatalog.CountForVersion((int)catalog);
            string id=OceanLeaderboardJson.Text(root,"deviceId","");if(id.Length>0&&!OceanLeaderboardLedger.Hex(id,64))throw new FormatException("Invalid discovery identity.");
            var restored=new Dictionary<int,Dictionary<string,object>>();var entries=OceanLeaderboardJson.Array(root,"records");if(entries.Count>supportedCount)throw new FormatException("Too many observations.");
            foreach(object value in entries)
            {
                var e=OceanLeaderboardJson.Object(value);int species=checked((int)OceanLeaderboardJson.Number(e,"species")),level=checked((int)OceanLeaderboardJson.Number(e,"level"));
                string status=OceanLeaderboardJson.Text(e,"status"),eid=OceanLeaderboardJson.Text(e,"eventId"),session=OceanLeaderboardJson.Text(e,"sessionId"),date=OceanLeaderboardJson.Text(e,"createdUtc");
                if(species<0||species>=supportedCount||level<OceanSpeciesCatalog.Get(species).FirstLevel||level>OceanLevelCatalog.Count||!OceanLeaderboardLedger.Identifier(session)||!DateTimeOffset.TryParse(date,out _)||eid!=OceanLeaderboardLedger.Hash("discovery|"+species.ToString(CultureInfo.InvariantCulture))||restored.ContainsKey(species)||(status!="pending"&&status!="accepted"&&status!="rejected"))throw new FormatException("Invalid observation record.");
                restored.Add(species,e);
            }
            string page=OceanLeaderboardJson.Text(root,"page",""),cursor=OceanLeaderboardJson.Text(root,"cursor","");if(cursor.Length>4096)throw new FormatException("Invalid discovery page cursor.");
            if(page.Length>0&&!OceanLeaderboardPage.Parse(page,allowLegacyDiscovery:true).IsDiscovery)throw new FormatException("Wrong discovery cache.");
            records.Clear();foreach(var pair in restored)records.Add(pair.Key,pair.Value);DeviceId=id;CachedPageJson=page;CachedPageCursor=cursor;
        }
        bool Fail(string error){LastError=error;return false;}
    }
}

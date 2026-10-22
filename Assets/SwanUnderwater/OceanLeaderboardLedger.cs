using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SwanUnderwater
{
    public sealed class OceanLeaderboardEvent
    {
        public string eventId, type, sessionId, runId, createdUtc, nickname;
        public long sequence;
        public int level, species, pickupId;
        public Dictionary<string, object> ToJson() => new Dictionary<string, object> {
            {"eventId",eventId},{"sequence",sequence},{"type",type},{"sessionId",sessionId??""},
            {"runId",runId??""},{"level",level},{"species",species},{"pickupId",pickupId},
            {"createdUtc",createdUtc},{"nickname",nickname??""} };
        public static OceanLeaderboardEvent FromJson(object source)
        {
            var v = OceanLeaderboardJson.Object(source);
            return new OceanLeaderboardEvent { eventId=OceanLeaderboardJson.Text(v,"eventId"), sequence=OceanLeaderboardJson.Number(v,"sequence"),
                type=OceanLeaderboardJson.Text(v,"type"), sessionId=OceanLeaderboardJson.Text(v,"sessionId",""), runId=OceanLeaderboardJson.Text(v,"runId",""),
                level=checked((int)OceanLeaderboardJson.Number(v,"level",0)), species=checked((int)OceanLeaderboardJson.Number(v,"species",0)),
                pickupId=checked((int)OceanLeaderboardJson.Number(v,"pickupId",0)), createdUtc=OceanLeaderboardJson.Text(v,"createdUtc"), nickname=OceanLeaderboardJson.Text(v,"nickname","") };
        }
    }

    /// <summary>Durable application-lifetime ledger. Campaign reset never calls a reset here.</summary>
    public sealed class OceanLeaderboardLedger
    {
        const string FileName="ocean-leaderboard.json";
        const int MaxBytes=20000000, MaxSeen=131072;
        readonly string directory;
        readonly int maxPending;
        readonly bool queueForUpload;
        readonly HashSet<string> seen=new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string,int> runs=new Dictionary<string,int>(StringComparer.Ordinal);
        readonly List<OceanLeaderboardEvent> pending=new List<OceanLeaderboardEvent>();
        readonly List<object> rejected=new List<object>();
        public string LastError { get; private set; }
        public string DeviceId { get; private set; }="";
        public string Nickname { get; private set; }=""; // Server-confirmed only.
        public string NicknameDraft { get; private set; }="";
        public string PendingNicknameId { get; private set; }="";
        public string NicknameStatus { get; private set; }="idle";
        public string NicknameError { get; private set; }="";
        public string ServerProfileJson { get; private set; }="";
        public string CachedPageJson { get; private set; }="";
        public string CachedPageCursor { get; private set; }="";
        public bool Writable { get; private set; }=true;
        public long ShellTotal { get; private set; }
        public long RejectedScoreTotal { get; private set; }
        public long FishTotal { get; private set; }
        public long CoinTotal { get; private set; }
        public long DemoTotal { get; private set; }
        public long RejectedTotal { get; private set; }
        public long Score => ShellTotal + FishTotal*2 + CoinTotal*10;
        public int PendingCount => pending.Count;
        long sequence;
        bool primaryVerified;
        sealed class UnsupportedSchemaException : Exception { public UnsupportedSchemaException() : base("Unsupported leaderboard schema; newer data preserved.") {} }
        public OceanLeaderboardLedger(string directory,int maximumPending=4096,bool queueForUpload=true)
        {
            if (maximumPending<1 || maximumPending>4096) throw new ArgumentOutOfRangeException(nameof(maximumPending));
            this.directory=Path.GetFullPath(directory); maxPending=maximumPending; this.queueForUpload=queueForUpload;
            string path=Path.Combine(this.directory,FileName), backup=path+".backup";
            if (!File.Exists(path)&&!File.Exists(backup)) return;
            try { Read(path); primaryVerified=true; }
            catch(UnsupportedSchemaException error) { Writable=false;LastError=error.Message; }
            catch(Exception primary)
            {
                try { Read(backup); LastError="Recovered leaderboard backup: "+primary.Message; }
                catch(Exception secondary) { Writable=false; LastError="Leaderboard files preserved; recovery required: "+primary.Message+" / "+secondary.Message; }
            }
        }
        public bool BindIdentity(string id)
        {
            if (!Hex(id,64)) return Fail("Invalid application identity.");
            if (DeviceId==id) return true;
            if (DeviceId.Length>0) { Writable=false; return Fail("Keystore identity differs from local ledger; no automatic score migration."); }
            string before=DeviceId; DeviceId=id;
            if (Save()) return true;
            DeviceId=before; return false;
        }
        public bool EnsureSession(string sessionId)
        {
            if (!Identifier(sessionId)) return Fail("Invalid session ID.");
            return Add("demo",sessionId,"",0,0,0,"",Hash("demo|"+sessionId));
        }
        public bool BeginRun(string sessionId,string runId,int level)
        {
            if (!Identifier(runId)||level<1||level>OceanLevelCatalog.Count) return Fail("Invalid run identity or level.");
            if (runs.TryGetValue(runId,out int existing)&&existing!=level) return Fail("A run ID cannot change levels.");
            if (!EnsureSession(sessionId)) return false;
            // A resumed run keeps its level, but every cold session needs a server run-session link.
            return Add("run_start",sessionId,runId,level,0,0,"",Hash("run|"+runId+"|"+sessionId));
        }
        public bool RecordShell(string sessionId,string runId,int species,int pickupId) => Record("shell",sessionId,runId,species,pickupId);
        public bool RecordFish(string sessionId,string runId,int species,int pickupId) => Record("fish",sessionId,runId,species,pickupId);
        public bool RecordShark(string sessionId,string runId,int pickupId) => Record("shark",sessionId,runId,0,pickupId);
        public bool RecordCoin(string sessionId,string runId,int pickupId) => Record("coin",sessionId,runId,0,pickupId);
        bool Record(string type,string sessionId,string runId,int species,int pickupId)
        {
            if (!runs.TryGetValue(runId??"",out int level)||pickupId<0||pickupId>1000000000) return Fail("Persist BeginRun before its scoring events.");
            var definition=OceanLevelCatalog.Get(level);
            if (type=="shell" && (species<0||species>=10||(definition.ShellMask&(1<<species))==0||(pickupId>=20000&&pickupId<20005))) return Fail("Shell entity does not belong to this level.");
            if (type=="fish" && (species<0||species>=OceanLevelCatalog.FishSpeciesCount||(definition.FishMask&(1<<species))==0)) return Fail("Fish is not a target in this level.");
            if (type=="shark" && definition.SharkHits<=0) return Fail("Shark capture requires a boss level.");
            if (type=="coin" && (pickupId<20000||pickupId>=20000+definition.CoinCount)) return Fail("Coin slot is not a target in this level.");
            // Slot zero retains the exact v1 semantic hash, including already ACKed or rejected events.
            string key=type=="shell"?pickupId.ToString(CultureInfo.InvariantCulture):type=="fish"?species.ToString(CultureInfo.InvariantCulture):
                type=="coin"&&pickupId!=20000?pickupId.ToString(CultureInfo.InvariantCulture):"once";
            string id=Hash(type+"|"+runId+"|"+key);
            if (seen.Contains(id)) return true;
            if (!BeginRun(sessionId,runId,level)) return false;
            return Add(type,sessionId,runId,level,species,pickupId,"",id);
        }
        public bool SetNickname(string value)
        {
            if(!queueForUpload)return Fail("Nickname changes are unavailable in local-only mode.");
            if (!ValidNickname(value,out string normalized)) return Fail("Nickname must contain 1–20 visible Unicode characters.");
            if (NicknameDraft==normalized && PendingNicknameId.Length>0) return true;
            if (Nickname==normalized && PendingNicknameId.Length==0)
            {
                string before=Snapshot(); NicknameDraft=normalized; NicknameStatus="accepted"; NicknameError="";
                if(Save())return true; Restore(before);return false;
            }
            return Add("profile","","",0,0,0,normalized,Hash("profile|"+(sequence+1).ToString(CultureInfo.InvariantCulture)+"|"+normalized));
        }
        bool Add(string type,string session,string run,int level,int species,int pickup,string nickname,string id)
        {
            if (seen.Contains(id)) return true;
            if (!Writable) return false;
            if (queueForUpload && pending.Count>=maxPending) return Fail("Offline score queue is full; keep this campaign run and retry after sync.");
            if (seen.Count>=MaxSeen||sequence>=9007199254740990L) return Fail("Leaderboard lifetime index limit reached; preserve data for migration.");
            string previousNickname=Nickname, previousDraft=NicknameDraft, previousPending=PendingNicknameId, previousStatus=NicknameStatus, previousError=NicknameError;
            bool addedRun=type=="run_start"&&!runs.ContainsKey(run);
            sequence++;
            seen.Add(id);
            // Disabled releases retain their real lifetime totals and duplicate/run
            // index. Old upload evidence stays untouched; new local play neither grows
            // the outbox nor becomes blocked when a historical outbox is already full.
            if(queueForUpload)pending.Add(new OceanLeaderboardEvent { eventId=id,sequence=sequence,type=type,sessionId=session,runId=run,level=level,species=species,pickupId=pickup,
                nickname=nickname,createdUtc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture) });
            if (addedRun) runs.Add(run,level);
            if (type=="shell") ShellTotal++;
            if (type=="fish"||type=="shark") FishTotal++;
            if (type=="coin") CoinTotal++;
            if (type=="demo") DemoTotal++;
            if (type=="profile") { NicknameDraft=nickname; PendingNicknameId=id; NicknameStatus="pending"; NicknameError=""; }
            if (Save()) return true;
            seen.Remove(id); if(queueForUpload)pending.RemoveAt(pending.Count-1); sequence--; Nickname=previousNickname; NicknameDraft=previousDraft; PendingNicknameId=previousPending; NicknameStatus=previousStatus; NicknameError=previousError;
            if (addedRun) runs.Remove(run);
            if (type=="shell") ShellTotal--;
            if (type=="fish"||type=="shark") FishTotal--;
            if (type=="coin") CoinTotal--;
            if (type=="demo") DemoTotal--;
            return false;
        }
        public bool HasUploadableEvents(int maximumCampaignLevel)
        {
            foreach(var item in pending) if(item.level<=maximumCampaignLevel) return true;
            return false;
        }
        public List<OceanLeaderboardEvent> Batch(int limit=64,int maximumCampaignLevel=int.MaxValue)
        {
            var result=new List<OceanLeaderboardEvent>();
            // Filter before the bounded take: unsupported new runs must not starve
            // older runs or nickname events behind them. Nothing leaves the outbox.
            for(int i=0;i<pending.Count&&result.Count<Math.Min(64,Math.Max(1,limit));i++)
                if(pending[i].level<=maximumCampaignLevel)
                    result.Add(OceanLeaderboardEvent.FromJson(OceanLeaderboardJson.Parse(OceanLeaderboardJson.Write(pending[i].ToJson()))));
            return result;
        }
        public bool ApplyAcknowledgement(string json,ISet<string> submitted)
        {
            if(!queueForUpload)return Fail("Online acknowledgements are unavailable in local-only mode.");
            string before=null;
            try
            {
                if(json==null||json.Length>262144)throw new FormatException("Oversized acknowledgement.");
                var response=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(json));
                if(OceanLeaderboardJson.Number(response,"scoringVersion",0)!=2)throw new FormatException("Server scoring version is not 2.");
                var completed=new HashSet<string>(StringComparer.Ordinal);
                foreach(string key in new[]{"acceptedEventIds","duplicateEventIds"}) foreach(object id in OceanLeaderboardJson.Array(response,key))
                    if(id is string s&&submitted.Contains(s)) completed.Add(s);
                var failures=new Dictionary<string,string>(StringComparer.Ordinal);
                foreach(object item in OceanLeaderboardJson.Array(response,"rejected"))
                {
                    var rejection=OceanLeaderboardJson.Object(item); string id=OceanLeaderboardJson.Text(rejection,"eventId");
                    if(id==null||!submitted.Contains(id)) continue;
                    if(rejection.TryGetValue("retryable",out object value)&&value is bool retryable&&!retryable)
                    { completed.Add(id); failures[id]=OceanLeaderboardJson.Text(rejection,"code","rejected"); }
                }
                OceanLeaderboardRow serverProfile=null;
                if(response.TryGetValue("profile",out object profile)&&profile!=null) serverProfile=OceanLeaderboardRow.Parse(profile,false);
                foreach(var item in pending)
                    if(item.type=="profile"&&completed.Contains(item.eventId)&&!failures.ContainsKey(item.eventId)&&serverProfile==null)
                        throw new FormatException("A confirmed nickname requires the server profile.");
                before=Snapshot();
                for(int i=pending.Count-1;i>=0;i--)
                {
                    var e=pending[i]; if(!completed.Contains(e.eventId)) continue;
                    if(failures.TryGetValue(e.eventId,out string code))
                    {
                        RejectedTotal++; rejected.Add(new Dictionary<string,object>{{"eventId",e.eventId},{"type",e.type},{"code",code}});
                        if(rejected.Count>256) rejected.RemoveAt(0);
                        if(e.type=="shell"||e.type=="fish"||e.type=="shark"||e.type=="coin") RejectedScoreTotal++;
                        if(e.type=="shell") ShellTotal--;
                        if(e.type=="fish"||e.type=="shark") FishTotal--;
                        if(e.type=="coin") CoinTotal--;
                        if(e.type=="demo") DemoTotal--;
                    }
                    if(e.type=="profile" && e.eventId==PendingNicknameId)
                    {
                        PendingNicknameId="";
                        NicknameError=failures.TryGetValue(e.eventId,out string reason)?reason:"";
                        if(NicknameError.Length==0 && serverProfile!=null && serverProfile.nickname!=e.nickname)NicknameError="profile_superseded";
                        NicknameStatus=NicknameError.Length==0?"accepted":"rejected";
                    }
                    pending.RemoveAt(i);
                }
                if(serverProfile!=null) { ServerProfileJson=OceanLeaderboardJson.Write(profile); Nickname=serverProfile.nickname; }
                if(Save()) return true;
                Restore(before); return false;
            }
            catch(Exception error) { if(before!=null) Restore(before); return Fail("Invalid leaderboard acknowledgement: "+error.Message); }
        }
        public bool CachePage(string cursor,string json)
        {
            if(!queueForUpload)return Fail("Leaderboard pages are unavailable in local-only mode.");
            try { if(OceanLeaderboardPage.Parse(json).IsDiscovery)throw new FormatException("Score cache cannot contain a discovery board."); }
            catch(Exception error) { return Fail("Invalid leaderboard page: "+error.Message); }
            string previous=CachedPageJson, previousCursor=CachedPageCursor;
            CachedPageJson=json; CachedPageCursor=cursor??"";
            if(Save()) return true;
            CachedPageJson=previous; CachedPageCursor=previousCursor; return false;
        }
        string Snapshot()
        {
            var entries=new List<object>(); foreach(var e in pending) entries.Add(e.ToJson());
            var knownRuns=new List<object>(); foreach(var pair in runs) knownRuns.Add(new Dictionary<string,object>{{"runId",pair.Key},{"level",pair.Value}});
            return OceanLeaderboardJson.Write(new Dictionary<string,object>{{"schema",2},{"deviceId",DeviceId},{"nickname",Nickname},{"sequence",sequence},
                {"nicknameDraft",NicknameDraft},{"pendingNicknameId",PendingNicknameId},{"nicknameStatus",NicknameStatus},{"nicknameError",NicknameError},{"shellTotal",ShellTotal},{"rejectedScoreTotal",RejectedScoreTotal},
                {"fishTotal",FishTotal},{"coinTotal",CoinTotal},{"demoTotal",DemoTotal},{"rejectedTotal",RejectedTotal},
                {"seen",new List<string>(seen)},{"runs",knownRuns},{"pending",entries},{"rejected",rejected},
                {"serverProfileJson",ServerProfileJson},{"cachedPageJson",CachedPageJson},{"cachedPageCursor",CachedPageCursor}});
        }
        bool Save()
        {
            if(!Writable) return false;
            string temporary=null,backupTemporary=null;
            try
            {
                Directory.CreateDirectory(directory);
                string body=Snapshot(),json=OceanLeaderboardJson.Write(new Dictionary<string,object>{{"body",body},{"sha256",Hash(body)}});
                byte[] bytes=new UTF8Encoding(false,true).GetBytes(json);
                if(bytes.Length>MaxBytes) throw new IOException("Leaderboard store size limit reached.");
                string path=Path.Combine(directory,FileName),backup=path+".backup";
                temporary=path+".tmp-"+Guid.NewGuid().ToString("N"); Write(temporary,bytes);
                if(File.Exists(path)) File.Replace(temporary,path,primaryVerified?backup:null);
                else
                {
                    backupTemporary=backup+".tmp-"+Guid.NewGuid().ToString("N"); Write(backupTemporary,bytes);
                    if(File.Exists(backup)) File.Replace(backupTemporary,backup,null); else File.Move(backupTemporary,backup);
                    backupTemporary=null; File.Move(temporary,path);
                }
                temporary=null; primaryVerified=true; LastError=null; return true;
            }
            catch(Exception error) { return Fail("Leaderboard persistence failed: "+error.GetType().Name+": "+error.Message); }
            finally { DeleteTemporary(temporary); DeleteTemporary(backupTemporary); }
        }
        static void DeleteTemporary(string path) { try { if(path!=null&&File.Exists(path)) File.Delete(path); } catch(IOException) {} catch(UnauthorizedAccessException) {} }
        static void Write(string path,byte[] bytes)
        { using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { file.Write(bytes,0,bytes.Length); file.Flush(true); } }
        void Read(string path)
        {
            var info=new FileInfo(path); if(!info.Exists||info.Length>MaxBytes) throw new IOException("Missing or oversized ledger.");
            var root=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(File.ReadAllText(path,new UTF8Encoding(false,true))));
            string body=OceanLeaderboardJson.Text(root,"body"),hash=OceanLeaderboardJson.Text(root,"sha256");
            if(body==null||Hash(body)!=hash) throw new FormatException("Leaderboard checksum mismatch.");
            Restore(body);
        }
        void Restore(string json)
        {
            var v=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(json));
            long schema=OceanLeaderboardJson.Number(v,"schema");
            if(schema!=1&&schema!=2) throw new UnsupportedSchemaException();
            string id=OceanLeaderboardJson.Text(v,"deviceId",""); if(id.Length>0&&!Hex(id,64)) throw new FormatException("Invalid identity.");
            var newSeen=new HashSet<string>(StringComparer.Ordinal); foreach(object item in OceanLeaderboardJson.Array(v,"seen"))
                if(!(item is string s)||!Hex(s,64)||!newSeen.Add(s)) throw new FormatException("Invalid duplicate index.");
            if(newSeen.Count>MaxSeen) throw new FormatException("Oversized lifetime index.");
            var newRuns=new Dictionary<string,int>(StringComparer.Ordinal); foreach(object item in OceanLeaderboardJson.Array(v,"runs"))
            { var run=OceanLeaderboardJson.Object(item); string key=OceanLeaderboardJson.Text(run,"runId"); int level=(int)OceanLeaderboardJson.Number(run,"level");
                if(!Identifier(key)||level<1||level>OceanLevelCatalog.Count||newRuns.ContainsKey(key)) throw new FormatException("Invalid run."); newRuns.Add(key,level); }
            var newPending=new List<OceanLeaderboardEvent>(); long previous=0;
            foreach(object item in OceanLeaderboardJson.Array(v,"pending"))
            { var e=OceanLeaderboardEvent.FromJson(item); if(!newSeen.Contains(e.eventId)||e.sequence<=previous||!KnownType(e.type)) throw new FormatException("Invalid outbox."); previous=e.sequence;newPending.Add(e); }
            if(newPending.Count>4096) throw new FormatException("Oversized outbox.");
            long shells=OceanLeaderboardJson.Number(v,"shellTotal",0), rejectedScores=OceanLeaderboardJson.Number(v,"rejectedScoreTotal",0);
            long seq=OceanLeaderboardJson.Number(v,"sequence"), fish=OceanLeaderboardJson.Number(v,"fishTotal"),coins=OceanLeaderboardJson.Number(v,"coinTotal"),demos=OceanLeaderboardJson.Number(v,"demoTotal");
            if(shells<0||shells>MaxSeen||rejectedScores<0||seq<previous||seq<0||fish<0||coins<0||demos<0||fish>MaxSeen||coins>MaxSeen||demos>MaxSeen) throw new FormatException("Invalid counters.");
            var newRejected=OceanLeaderboardJson.Array(v,"rejected");long rejectedCount=OceanLeaderboardJson.Number(v,"rejectedTotal",0);
            string name=OceanLeaderboardJson.Text(v,"nickname",""),profile=OceanLeaderboardJson.Text(v,"serverProfileJson",""),page=OceanLeaderboardJson.Text(v,"cachedPageJson",""),cursor=OceanLeaderboardJson.Text(v,"cachedPageCursor","");
            if(newRejected.Count>256||rejectedCount<0||(name.Length>0&&!ValidNickname(name,out _))||cursor.Length>4096)throw new FormatException("Invalid ledger metadata.");
            string draft=OceanLeaderboardJson.Text(v,"nicknameDraft",""),pendingName=OceanLeaderboardJson.Text(v,"pendingNicknameId",""),nameStatus=OceanLeaderboardJson.Text(v,"nicknameStatus","idle"),nameError=OceanLeaderboardJson.Text(v,"nicknameError","");
            if(schema==1)
            {
                // Old ranks used different weights; preserve counts and confirmed name but never relabel those ranks as live v2 data.
                name=profile.Length>0?OceanLeaderboardJson.Text(OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(profile)),"nickname",""):"";
                profile=page=cursor="";shells=0;
                foreach(var e in newPending)if(e.type=="profile"){draft=e.nickname;pendingName=e.eventId;nameStatus="pending";}
                foreach(object item in newRejected) { string type=OceanLeaderboardJson.Text(OceanLeaderboardJson.Object(item),"type",""); if(type=="fish"||type=="shark"||type=="coin")rejectedScores++; }
            }
            if(profile.Length>0)OceanLeaderboardRow.Parse(OceanLeaderboardJson.Parse(profile),false);
            if(page.Length>0&&OceanLeaderboardPage.Parse(page).IsDiscovery)throw new FormatException("Score ledger contains a discovery board.");
            if((draft.Length>0&&!ValidNickname(draft,out _)) || (nameStatus!="idle"&&nameStatus!="pending"&&nameStatus!="accepted"&&nameStatus!="rejected") || nameError.Length>128)
                throw new FormatException("Invalid nickname request metadata.");
            if((nameStatus=="pending")!=(pendingName.Length>0))throw new FormatException("Nickname pending status disagrees with event identity.");
            if(pendingName.Length>0&&!newPending.Exists(e=>e.type=="profile"&&e.eventId==pendingName&&e.nickname==draft))throw new FormatException("Nickname request is missing its durable event.");
            seen.Clear();foreach(string value in newSeen)seen.Add(value);runs.Clear();foreach(var pair in newRuns)runs.Add(pair.Key,pair.Value);
            pending.Clear();pending.AddRange(newPending);rejected.Clear();rejected.AddRange(newRejected);
            DeviceId=id;sequence=seq;ShellTotal=shells;RejectedScoreTotal=rejectedScores;FishTotal=fish;CoinTotal=coins;DemoTotal=demos;RejectedTotal=rejectedCount;
            Nickname=name;NicknameDraft=draft;PendingNicknameId=pendingName;NicknameStatus=nameStatus;NicknameError=nameError;ServerProfileJson=profile;CachedPageJson=page;CachedPageCursor=cursor;
        }
        bool Fail(string message) { LastError=message;return false; }
        static bool KnownType(string type) => type=="demo"||type=="run_start"||type=="shell"||type=="fish"||type=="shark"||type=="coin"||type=="profile";
        public static string Hash(string value) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(new UTF8Encoding(false,true).GetBytes(value))).Replace("-","").ToLowerInvariant(); }
        public static bool Hex(string s,int length) { if(s==null||s.Length!=length)return false;foreach(char c in s)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))return false;return true; }
        public static bool Identifier(string value) => value!=null&&(Guid.TryParse(value,out _)||Hex(value,32)||Hex(value,64));
        public static bool ValidNickname(string value,out string normalized)
        {
            normalized=null;if(value==null)return false;
            try { value=value.Trim().Normalize(NormalizationForm.FormC); } catch(ArgumentException) { return false; }
            int count=0;
            for(int i=0;i<value.Length;i++)
            {
                var category=CharUnicodeInfo.GetUnicodeCategory(value,i);
                if(category==UnicodeCategory.Control||category==UnicodeCategory.Format||category==UnicodeCategory.LineSeparator||category==UnicodeCategory.ParagraphSeparator||category==UnicodeCategory.Surrogate||category==UnicodeCategory.PrivateUse||category==UnicodeCategory.OtherNotAssigned)return false;
                if(char.IsHighSurrogate(value[i])) { if(i+1>=value.Length||!char.IsLowSurrogate(value[i+1]))return false;i++; }
                else if(char.IsLowSurrogate(value[i]))return false;
                count++;
            }
            if(count<1||count>20)return false; normalized=value;return true;
        }
    }
}

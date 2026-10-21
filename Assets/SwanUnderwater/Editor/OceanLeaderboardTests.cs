using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SwanUnderwater.EditorTests
{
    public static class OceanLeaderboardTests
    {
        public static int Assertions { get; private set; }
        const string S="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",R="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",R2="cccccccccccccccccccccccccccccccc",S2="dddddddddddddddddddddddddddddddd";
        const string Profile="{\"publicId\":\"012345abcdef\",\"nickname\":\"设备-012345\",\"scoringVersion\":2,\"shellTotal\":0,\"fishTotal\":2,\"coinTotal\":1,\"score\":14,\"demoStarts\":1}";
        const string Page="{\"scoringVersion\":2,\"entries\":[{\"rank\":1,\"publicId\":\"012345abcdef\",\"nickname\":\"设备-012345\",\"scoringVersion\":2,\"shellTotal\":0,\"fishTotal\":2,\"coinTotal\":1,\"score\":14,\"demoStarts\":1}],\"nextCursor\":\"opaque+cursor/1\",\"totals\":{\"devices\":1,\"demoStarts\":1},\"myEntry\":null,\"serverTime\":\"2026-09-21T00:00:00Z\"}";
        static void Check(bool value,string message) { Assertions++;if(!value)throw new Exception("Ocean leaderboard assertion failed: "+message); }
        static void Throws(Action action,string message) { bool thrown=false;try{action();}catch(Exception){thrown=true;}Check(thrown,message); }
        public static void RunAll()
        {
            Assertions=0;string root=Path.Combine(Path.GetTempPath(),"ocean-leaderboard-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try { Persistence(Path.Combine(root,"ledger"));QueueBounds(Path.Combine(root,"queue"));Acknowledgements(Path.Combine(root,"ack"));Recovery(Path.Combine(root,"recovery"));Schema(Path.Combine(root,"schema"));WriteFailure(root);TwentyLevels(Path.Combine(root,"twenty"));ShellEntities(Path.Combine(root,"shells"));NicknameConfirmation(Path.Combine(root,"names"));OldLedger(Path.Combine(root,"old"));Discoveries(Path.Combine(root,"discoveries"));DiscoveryCatalogMigration(Path.Combine(root,"discovery-catalog-migration"));DiscoveryCatalogTwoMigration(Path.Combine(root,"discovery-catalog-two"));Pinyin();Protocol();Signing(Path.Combine(root,"signer")); }
            finally { Directory.Delete(root,true); }
#if UNITY_EDITOR
            UnityEngine.Debug.Log("[OceanLeaderboardTests] PASS "+Assertions+" assertions; no production requests or Android execution.");
#else
            Console.WriteLine("[OceanLeaderboardTests] PASS "+Assertions+" assertions; no production requests or Android execution.");
#endif
        }
        static void Discoveries(string path)
        {
            var l=new OceanDiscoveryLedger(path);
            Check(l.Count==0&&l.PendingCount==0,"old score history never reconstructs discoveries");
            Check(l.Record(S,0,1)&&l.Record(S2,0,20)&&l.Count==1&&l.PendingCount==1,"same species globally unique across sessions and levels");
            Check(!l.Record(S,-1,1)&&!l.Record(S,31,20),"unknown discovery ID refused");
            int late=26;Check(!l.Record(S,late,1),"catalog FirstLevel checked before recording");
            Check(l.Record(S,late,OceanSpeciesCatalog.Get(late).FirstLevel)&&l.Count==2,"later category appears only at allowed level");
            var reload=new OceanDiscoveryLedger(path);Check(reload.Count==2&&reload.PendingCount==2,"offline observations survive restart");
            var submitted=new HashSet<string>();foreach(object item in reload.Batch())submitted.Add(OceanLeaderboardJson.Text(OceanLeaderboardJson.Object(item),"eventId"));
            string before=File.ReadAllText(Path.Combine(path,"ocean-discoveries.json"));
            Check(!reload.ApplyAcknowledgement("{\"catalogVersion\":99,\"speciesCount\":27}",submitted)&&reload.PendingCount==2,"unsupported server catalog retains discovery outbox");
            Check(File.ReadAllText(Path.Combine(path,"ocean-discoveries.json"))==before,"invalid ACK does not rewrite evidence");
            string id=OceanLeaderboardLedger.Hash("discovery|0"),id2=OceanLeaderboardLedger.Hash("discovery|26");
            string ack="{\"catalogVersion\":3,\"speciesCount\":31,\"acceptedEventIds\":[\""+id+"\"],\"duplicateEventIds\":[\""+id2+"\"],\"rejected\":[]}";
            Check(reload.ApplyAcknowledgement(ack,new HashSet<string>{id})&&reload.PendingCount==1,"ACK cannot remove an unsubmitted observation");
            Check(reload.ApplyAcknowledgement(ack,submitted)&&reload.PendingCount==0,"accepted and globally duplicate observations drain only submitted outbox");
            Check(reload.Record(S2,0,20)&&reload.PendingCount==0&&reload.Count==2,"replayed observation after ACK is not requeued");
            string page="{\"board\":\"discoveries\",\"catalogVersion\":3,\"speciesCount\":31,\"entries\":[{\"rank\":1,\"publicId\":\"012345abcdef\",\"nickname\":\"海洋收藏家\",\"discoveryCount\":2,\"catalogVersion\":3,\"speciesCount\":31}],\"nextCursor\":null,\"totals\":{\"devices\":1,\"demoStarts\":1},\"myEntry\":null,\"serverTime\":\"2026-10-17T00:00:00Z\"}";
            Check(OceanLeaderboardPage.Parse(page).IsDiscovery&&OceanLeaderboardPage.Parse(page).entries[0].discoveryCount==2&&OceanLeaderboardPage.Parse(page).SpeciesCount==31,"current discovery page retains the explicit new denominator independently of old cached pages");
            Check(reload.CachePage("",page)&&new OceanDiscoveryLedger(path).CachedPageJson==page,"discovery cache is durable and separate from score cache");
            Check(!new OceanLeaderboardLedger(Path.Combine(path,"score")).CachePage("",page),"discovery cache never relabelled as score cache");
            Check(!reload.CachePage("",Page),"score cache never relabelled as discovery cache");
            string file=Path.Combine(path,"ocean-discoveries.json");File.WriteAllText(file,"broken");
            var recovered=new OceanDiscoveryLedger(path);Check(recovered.Writable&&recovered.Count==2,"partial discovery write recovers complete backup");
            Check(recovered.BindIdentity(new string('a',64))&&!recovered.BindIdentity(new string('b',64))&&!recovered.Writable,"new key cannot claim old discoveries");
            var wrapper=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(File.ReadAllText(file)));string body=OceanLeaderboardJson.Text(wrapper,"body").Replace("\"schema\":2","\"schema\":99");wrapper["body"]=body;wrapper["sha256"]=OceanLeaderboardLedger.Hash(body);File.WriteAllText(file,OceanLeaderboardJson.Write(wrapper));
            var future=new OceanDiscoveryLedger(path);Check(!future.Writable&&!future.Record(S,1,1),"future discovery schema preserved without old backup rollback");
        }
        static void WriteDiscoveryFixture(string file,Dictionary<string,object> snapshot)
        {
            string body=OceanLeaderboardJson.Write(snapshot);
            File.WriteAllText(file,OceanLeaderboardJson.Write(new Dictionary<string,object>{{"body",body},{"sha256",OceanLeaderboardLedger.Hash(body)}}));
        }
        static void DiscoveryCatalogMigration(string path)
        {
            var ledger=new OceanDiscoveryLedger(path);Check(ledger.Record(S,0,1)&&ledger.Record(S,26,6),"migration fixture records real old observations");
            string id0=OceanLeaderboardLedger.Hash("discovery|0"),id27=OceanLeaderboardLedger.Hash("discovery|27");
            string ack="{\"catalogVersion\":3,\"speciesCount\":31,\"acceptedEventIds\":[\""+id0+"\"],\"duplicateEventIds\":[],\"rejected\":[]}";
            Check(ledger.ApplyAcknowledgement(ack,new HashSet<string>{id0})&&ledger.PendingCount==1,"migration fixture has distinct accepted and pending records");
            string oldPage="{\"board\":\"discoveries\",\"catalogVersion\":1,\"speciesCount\":27,\"entries\":[],\"nextCursor\":null,\"totals\":{\"devices\":1,\"demoStarts\":1},\"myEntry\":null,\"serverTime\":\"2026-10-17T00:00:00Z\"}";
            string file=Path.Combine(path,"ocean-discoveries.json"),backup=file+".backup";
            var envelope=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(File.ReadAllText(file)));
            var body=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(OceanLeaderboardJson.Text(envelope,"body")));
            body["schema"]=1;body.Remove("catalogVersion");body["page"]=oldPage;WriteDiscoveryFixture(file,body);File.Delete(backup);
            string old=File.ReadAllText(file);
            var migrated=new OceanDiscoveryLedger(path);
            Check(migrated.Writable&&migrated.Count==2&&migrated.PendingCount==1&&migrated.CachedPageJson==oldPage,"old checksum-authenticated queue retains statuses and historical cached view");
            Check(File.ReadAllText(file)==old&&!File.Exists(backup),"queue migration reads without rewriting old evidence");
            Throws(()=>OceanLeaderboardPage.Parse(oldPage),"live response cannot silently downgrade to catalog one");
            Check(OceanLeaderboardPage.Parse(oldPage,allowLegacyDiscovery:true).SpeciesCount==27,"historical cache explicitly retains its old denominator");
            Check(!migrated.Record(S2,27,5)&&migrated.Count==2,"new mackerel queue record cannot predate level six");
            Check(migrated.Record(S2,27,6)&&migrated.Count==3&&migrated.PendingCount==2,"new actual observation appends without requeueing accepted old observations");
            Check(File.ReadAllText(backup)==old,"first upgraded queue save preserves the exact legacy generation");
            var reloaded=new OceanDiscoveryLedger(path);
            Check(reloaded.Writable&&reloaded.Count==3&&reloaded.PendingCount==2&&reloaded.CachedPageJson==oldPage,"new schema reloads old and new records plus old cached page");
            envelope=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(File.ReadAllText(file)));body=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(OceanLeaderboardJson.Text(envelope,"body")));
            Check(OceanLeaderboardJson.Number(body,"schema")==2&&OceanLeaderboardJson.Number(body,"catalogVersion")==3,"next normal save writes schema two with explicit catalog three");
            var records=OceanLeaderboardJson.Array(body,"records");
            Check(records.Count==3&&OceanLeaderboardJson.Text(OceanLeaderboardJson.Object(records[0]),"eventId")==id0&&OceanLeaderboardJson.Text(OceanLeaderboardJson.Object(records[0]),"status")=="accepted","old event identity/status is unchanged");
            string pending=File.ReadAllText(file);
            Check(!reloaded.ApplyAcknowledgement(ack.Replace("\"catalogVersion\":3","\"catalogVersion\":1").Replace("\"speciesCount\":31","\"speciesCount\":27"),new HashSet<string>{id0,id27})&&reloaded.PendingCount==2&&File.ReadAllText(file)==pending,"legacy server acknowledgement cannot consume current queued records");
            var payload=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(Encoding.UTF8.GetString(OceanLeaderboardProtocol.DiscoveryPayload(new string('a',64),reloaded.Batch()))));
            Check(OceanLeaderboardJson.Number(payload,"catalogVersion")==3&&OceanLeaderboardJson.Array(payload,"events").Count==2,"new client uploads old pending plus new records in a catalog-three signed payload");
            payload=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(Encoding.UTF8.GetString(OceanLeaderboardProtocol.PagePayload(new string('a',64),"",20,true))));
            Check(OceanLeaderboardJson.Number(payload,"catalogVersion")==3,"new discovery board requests explicitly select catalog three");
            foreach(bool futureCatalog in new[]{false,true})
            {
                string invalidPath=path+(futureCatalog?"-future":"-v1futureid");Directory.CreateDirectory(invalidPath);string invalidFile=Path.Combine(invalidPath,"ocean-discoveries.json");
                var invalid=new Dictionary<string,object>(body);
                if(futureCatalog)invalid["catalogVersion"]=4;else{invalid["schema"]=1;invalid.Remove("catalogVersion");}
                WriteDiscoveryFixture(invalidFile,invalid);string original=File.ReadAllText(invalidFile);
                var refused=new OceanDiscoveryLedger(invalidPath);
                Check(!refused.Writable&&!refused.Record(S,1,1)&&File.ReadAllText(invalidFile)==original,"future catalog or a signed v1 queue containing ID27 is preserved and cannot be rewritten");
            }
        }
        static void DiscoveryCatalogTwoMigration(string path)
        {
            var ledger=new OceanDiscoveryLedger(path);
            Check(ledger.Record(S,0,1)&&ledger.Record(S,26,6)&&ledger.Record(S,27,6),"v2 queue contains old species including mackerel");
            string id0=OceanLeaderboardLedger.Hash("discovery|0"),id27=OceanLeaderboardLedger.Hash("discovery|27");
            string ack="{\"catalogVersion\":3,\"speciesCount\":31,\"acceptedEventIds\":[\""+id0+"\"],\"duplicateEventIds\":[],\"rejected\":[{\"eventId\":\""+id27+"\",\"code\":\"fixture_rejection\",\"retryable\":false}]}";
            Check(ledger.ApplyAcknowledgement(ack,new HashSet<string>{id0,id27})&&ledger.PendingCount==1,"fixture retains accepted, pending and rejected evidence");
            string file=Path.Combine(path,"ocean-discoveries.json"),backup=file+".backup";
            var wrapper=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(File.ReadAllText(file)));
            var body=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(OceanLeaderboardJson.Text(wrapper,"body")));
            string oldRecords=OceanLeaderboardJson.Write(body["records"]);
            string page="{\"board\":\"discoveries\",\"catalogVersion\":2,\"speciesCount\":28,\"entries\":[{\"rank\":1,\"publicId\":\"012345abcdef\",\"nickname\":\"旧观察员\",\"discoveryCount\":28,\"catalogVersion\":2,\"speciesCount\":28}],\"nextCursor\":\"old-v2-cursor\",\"totals\":{\"devices\":1,\"demoStarts\":1},\"myEntry\":null,\"serverTime\":\"2026-10-17T00:00:00Z\"}";
            body["catalogVersion"]=2;body["page"]=page;body["cursor"]="old-v2-input-cursor";WriteDiscoveryFixture(file,body);File.Delete(backup);
            string original=File.ReadAllText(file);
            var upgraded=new OceanDiscoveryLedger(path);
            Check(upgraded.Writable&&upgraded.Count==3&&upgraded.PendingCount==1&&upgraded.CachedPageJson==page,"schema2/catalog2 queue and old page are preserved");
            Check(File.ReadAllText(file)==original&&!File.Exists(backup),"v2 read is non-mutating");
            var cached=OceanLeaderboardPage.Parse(page,allowLegacyDiscovery:true);
            Check(cached.CatalogVersion==2&&cached.SpeciesCount==28&&cached.entries[0].discoveryCount==28,"cached row keeps its historical numerator and denominator");
            Throws(()=>OceanLeaderboardPage.Parse(page),"v2 page is not accepted as a v3 live response");
            Check(!upgraded.ApplyAcknowledgement(ack.Replace("\"catalogVersion\":3","\"catalogVersion\":2").Replace("\"speciesCount\":31","\"speciesCount\":28"),new HashSet<string>{id0})&&upgraded.PendingCount==1&&File.ReadAllText(file)==original,"old server ACK cannot drain current queue");
            for(int id=28;id<31;id++)
            {
                Check(!upgraded.Record(S2,id,5),"new animal remains locked until level six");
                Check(upgraded.Record(S2,id,6),"new actual animal observation appends");
                if(id==28)Check(File.ReadAllText(backup)==original,"first v3 commit backs up exact v2 bytes");
            }
            var again=new OceanDiscoveryLedger(path);
            Check(again.Count==6&&again.PendingCount==4&&again.CachedPageJson==page,"all three additions and old pending state survive restart");
            wrapper=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(File.ReadAllText(file)));body=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(OceanLeaderboardJson.Text(wrapper,"body")));
            Check(OceanLeaderboardJson.Number(body,"schema")==2&&OceanLeaderboardJson.Number(body,"catalogVersion")==3,"schema is stable while directory version advances");
            var rows=OceanLeaderboardJson.Array(body,"records");var prior=new List<object>{rows[0],rows[1],rows[2]};
            Check(OceanLeaderboardJson.Write(prior)==oldRecords,"old IDs, event hashes, statuses, timestamps and rejection reasons are byte-identical semantic data");
            foreach(int id in new[]{0,26,27})Check(again.Record(S2,id,20)&&again.Count==6&&again.PendingCount==4,"old observations are not duplicated or requeued");
            var submitted=new HashSet<string>();foreach(object item in again.Batch())submitted.Add(OceanLeaderboardJson.Text(OceanLeaderboardJson.Object(item),"eventId"));
            var accepted=new List<object>();foreach(string id in submitted)accepted.Add(id);
            Check(again.ApplyAcknowledgement(OceanLeaderboardJson.Write(new Dictionary<string,object>{{"catalogVersion",3},{"speciesCount",31},{"acceptedEventIds",accepted},{"duplicateEventIds",new List<object>()},{"rejected",new List<object>()}}),submitted)&&again.PendingCount==0,"matching v3 ACK drains exactly submitted records");
        }
        static void Persistence(string path)
        {
            var l=new OceanLeaderboardLedger(path);Check(l.Score==0&&l.DemoTotal==0,"new installation starts from zero, no reconstructed old totals");
            Check(l.EnsureSession(S)&&l.EnsureSession(S)&&l.DemoTotal==1,"same cold session counted once");
            Check(l.BeginRun(S,R,3),"begin level three");Check(l.RecordFish(S,R,0,10000),"first real fish");
            Check(l.RecordFish(S,R,0,99999)&&l.FishTotal==1,"same species cannot be farmed via different pickup slot");
            Check(l.RecordCoin(S,R,20000)&&l.RecordCoin(S,R,20000)&&!l.RecordCoin(S,R,20001)&&l.CoinTotal==1,"canonical coin slot capped by level");
            Check(l.Score==12,"weighted lifetime score");
            string file=Path.Combine(path,"ocean-leaderboard.json");DateTime stamp=File.GetLastWriteTimeUtc(file);string bytes=File.ReadAllText(file);
            for(int i=0;i<20;i++)Check(l.BeginRun(S,R,3)&&l.RecordFish(S,R,0,10000),"idempotent per-frame recovery");
            Check(File.GetLastWriteTimeUtc(file)==stamp&&File.ReadAllText(file)==bytes,"duplicates never rewrite disk");
            Check(!l.BeginRun(S,R,4)&&!l.RecordFish(S,R,3,10003)&&!l.RecordShark(S,R,10004),"invalid run/type rejected");
            var reloaded=new OceanLeaderboardLedger(path);Check(reloaded.Score==12&&reloaded.PendingCount==4,"restart restores score and outbox");
            Check(reloaded.BeginRun(S2,R,3)&&reloaded.RecordFish(S2,R,1,10001),"new cold session can continue existing run");
            Check(reloaded.DemoTotal==2&&reloaded.FishTotal==2,"continued run counts new demo and genuinely new fish");
            Check(reloaded.Batch().Count(x=>x.type=="run_start"&&x.runId==R)==2,"resumed run has one durable run-session link for each cold start");
            int resumedCount=reloaded.PendingCount;Check(reloaded.BeginRun(S2,R,3)&&reloaded.PendingCount==resumedCount,"same session resume is idempotent");
            Check(reloaded.BeginRun(S2,R2,5)&&reloaded.RecordShark(S2,R2,10004)&&reloaded.RecordShark(S2,R2,10004),"shark capture counts once");
            Check(reloaded.FishTotal==3&&reloaded.DemoTotal==2,"restart level adds no demo");
            Check(reloaded.SetNickname("  海底旅人  ")&&reloaded.NicknameDraft=="海底旅人"&&reloaded.Nickname==""&&reloaded.NicknameStatus=="pending","nickname trim");
            int pending=reloaded.PendingCount;Check(reloaded.SetNickname("海底旅人")&&reloaded.PendingCount==pending,"same nickname idempotent");
            Check(new OceanLeaderboardLedger(path).Score==16,"new campaign object cannot erase independent lifetime score");
        }
        static void QueueBounds(string path)
        {
            var l=new OceanLeaderboardLedger(path,2);Check(l.BeginRun(S,R,4)&&l.PendingCount==2,"bounded queue contains demo and run");
            Check(!l.RecordFish(S,R,0,10000)&&l.FishTotal==0&&l.PendingCount==2,"full queue fails before increment");
            Check(l.BeginRun(S,R,4),"durable duplicate still succeeds at capacity");
            var batch=l.Batch();Check(l.ApplyAcknowledgement(Ack(batch.Select(x=>x.eventId)),Ids(batch)),"ack frees queue");
            Check(l.RecordFish(S,R,0,10000)&&l.FishTotal==1,"campaign replay succeeds after queue frees");
            Check(l.Batch()[0].sequence==3,"failed append did not consume sequence");
        }
        static void Acknowledgements(string path)
        {
            var l=new OceanLeaderboardLedger(path);Check(l.BeginRun(S,R,3)&&l.RecordFish(S,R,0,10000)&&l.RecordCoin(S,R,20000),"ack setup");
            var all=l.Batch();var submitted=new HashSet<string>{all[0].eventId};
            Check(l.ApplyAcknowledgement(Ack(all.Select(x=>x.eventId)),submitted)&&l.PendingCount==3,"ACK cannot remove unsubmitted events");
            var rest=l.Batch();Check(l.ApplyAcknowledgement("{\"scoringVersion\":2,\"acceptedEventIds\":[],\"duplicateEventIds\":[\""+rest[0].eventId+"\"],\"rejected\":[],\"profile\":"+Profile+"}",Ids(rest)),"duplicate ACK valid");
            Check(l.Score==12&&l.PendingCount==2&&l.ServerProfileJson.Length>0,"duplicate ACK never increments totals");
            var scoring=l.Batch();string fish=scoring.Single(x=>x.type=="fish").eventId,coin=scoring.Single(x=>x.type=="coin").eventId;
            string rejected="{\"scoringVersion\":2,\"acceptedEventIds\":[],\"duplicateEventIds\":[],\"rejected\":[{\"eventId\":\""+fish+"\",\"code\":\"invalid_fish\",\"retryable\":false},{\"eventId\":\""+coin+"\",\"code\":\"missing_run\",\"retryable\":true}]}";
            Check(l.ApplyAcknowledgement(rejected,Ids(scoring))&&l.PendingCount==1&&l.Score==10&&l.RejectedTotal==1,"permanent rejection rolls back provisional score, retryable stays");
            Check(l.RecordFish(S,R,0,10000)&&l.Score==10,"rejected semantic event retained as seen, cannot rescore");
            Check(l.ApplyAcknowledgement(Ack(new[]{coin}),new HashSet<string>{coin})&&l.PendingCount==0&&l.Score==10,"last ACK");
            Check(l.RecordCoin(S,R,20000)&&l.PendingCount==0&&l.Score==10,"campaign restart replay after ACK stays duplicate");
            Check(l.CachePage("",Page),"cache page durable");var loaded=new OceanLeaderboardLedger(path);
            Check(loaded.Score==10&&loaded.PendingCount==0&&loaded.RejectedTotal==1&&loaded.CachedPageJson==Page,"ack/cache/rejection survive reload");
            string before=File.ReadAllText(Path.Combine(path,"ocean-leaderboard.json"));
            Check(!l.ApplyAcknowledgement("{\"scoringVersion\":2,\"acceptedEventIds\":[],\"duplicateEventIds\":[],\"rejected\":[],\"profile\":{}}",new HashSet<string>()),"malformed profile rejected");
            Check(File.ReadAllText(Path.Combine(path,"ocean-leaderboard.json"))==before&&l.Score==10,"invalid ACK leaves disk and memory unchanged");
        }
        static void Recovery(string path)
        {
            var l=new OceanLeaderboardLedger(path);l.BeginRun(S,R,3);l.RecordFish(S,R,0,10000);l.RecordCoin(S,R,20000);
            string file=Path.Combine(path,"ocean-leaderboard.json"),backup=file+".backup",good=File.ReadAllText(backup);File.WriteAllText(file,"truncated");
            var recovered=new OceanLeaderboardLedger(path);Check(recovered.Writable&&recovered.Score==2,"recover last complete backup, no invented lost final event");
            Check(recovered.RecordCoin(S,R,20000)&&recovered.Score==12,"campaign replay restores latest coin");
            Check(File.ReadAllText(backup)==good,"recovery save never replaces good backup with corrupt primary");
            Check(new OceanLeaderboardLedger(path).Score==12,"recovered primary reload");
            Check(recovered.BindIdentity(new string('a',64)),"bind key hash");Check(!recovered.BindIdentity(new string('b',64))&&!recovered.Writable,"different key cannot claim old local score");
            File.WriteAllText(file,"bad main");File.WriteAllText(backup,"bad backup");var broken=new OceanLeaderboardLedger(path);
            Check(!broken.Writable&&!broken.EnsureSession(S2)&&File.ReadAllText(file)=="bad main","double corruption preserves evidence and fails closed");
        }
        static void Schema(string path)
        {
            var l=new OceanLeaderboardLedger(path);l.EnsureSession(S);string file=Path.Combine(path,"ocean-leaderboard.json");
            var wrapper=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(File.ReadAllText(file)));string body=OceanLeaderboardJson.Text(wrapper,"body").Replace("\"schema\":2","\"schema\":99");
            wrapper["body"]=body;wrapper["sha256"]=OceanLeaderboardLedger.Hash(body);File.WriteAllText(file,OceanLeaderboardJson.Write(wrapper));
            var newer=new OceanLeaderboardLedger(path);Check(!newer.Writable&&!newer.EnsureSession(S2),"newer schema never rolls back to old backup");
        }
        static void WriteFailure(string root)
        {
            string blocker=Path.Combine(root,"blocked");File.WriteAllText(blocker,"file not directory");var l=new OceanLeaderboardLedger(Path.Combine(blocker,"ledger"));
            Check(!l.EnsureSession(S)&&l.PendingCount==0&&l.DemoTotal==0,"failed durable write rolls back all in-memory credit");
        }
        static void TwentyLevels(string path)
        {
            var ledger=new OceanLeaderboardLedger(path);
            Check(ledger.BeginRun(S,R,20)&&!ledger.BeginRun(S,R,21),"twenty-level run accepted, out-of-range refused");
            Check(ledger.RecordFish(S,R,4,10004)&&ledger.RecordShark(S,R,10004)&&ledger.RecordFish(S,R,9,10009),"fish species four and shark share a physical slot without semantic collision");
            Check(!ledger.RecordFish(S,R,10,10010),"unknown eleventh species cannot score");
            for(int slot=4;slot>=0;slot--)
                Check(ledger.RecordCoin(S,R,20000+slot)&&ledger.RecordCoin(S,R,20000+slot),"all five coin slots individually idempotent");
            Check(ledger.FishTotal==3&&ledger.CoinTotal==5&&ledger.Score==56,"five coins and three captures have exact weighted score");
            Check(!ledger.RecordCoin(S,R,19999)&&!ledger.RecordCoin(S,R,20005),"noncanonical and out-of-range coins rejected");
            var batch=ledger.Batch();
            Check(batch.Single(e=>e.type=="coin"&&e.pickupId==20000).eventId==OceanLeaderboardLedger.Hash("coin|"+R+"|once"),"slot zero retains exact already-deployed legacy event ID");
            Check(batch.Where(e=>e.type=="coin").Select(e=>e.eventId).Distinct().Count()==5,"additional coin slots have independent durable IDs");
            Check(ledger.ApplyAcknowledgement(Ack(batch.Select(e=>e.eventId)),Ids(batch))&&ledger.PendingCount==0,"all expanded events acknowledge normally");
            ledger=new OceanLeaderboardLedger(path);
            Check(ledger.Score==56&&ledger.BeginRun(S2,R,20),"new process restores twenty-level ledger and links a resumed session");
            int pending=ledger.PendingCount; string file=Path.Combine(path,"ocean-leaderboard.json"),before=File.ReadAllText(file);
            for(int slot=0;slot<5;slot++)Check(ledger.RecordCoin(S2,R,20000+slot),"ACKed coin replay succeeds after resume");
            Check(ledger.RecordFish(S2,R,4,10004)&&ledger.RecordShark(S2,R,10004)&&ledger.PendingCount==pending&&ledger.Score==56&&File.ReadAllText(file)==before,"ACKed captures and coins do not append or rewrite disk");
            Check(ledger.BeginRun(S2,R2,6)&&!ledger.RecordCoin(S2,R2,20002)&&ledger.RecordCoin(S2,R2,20001)&&ledger.RecordCoin(S2,R2,20000),"new run allows its two slots but no invented third coin");
            Check(ledger.RecordFish(S2,R2,4,10004)&&!ledger.RecordFish(S2,R2,9,10009)&&!ledger.RecordShark(S2,R2,10004),"expanded fish still obey per-level catalog targets and boss restriction");
            Check(ledger.Score==78&&new OceanLeaderboardLedger(path).Score==78,"new run increments lifetime totals without clearing earlier achievements");
        }
        static string ProfileWith(string name) => Profile.Replace("设备-012345",name);
        static string ProfileAck(IEnumerable<string> ids,string name) => Ack(ids).TrimEnd('}')+",\"profile\":"+ProfileWith(name)+"}";
        static void ShellEntities(string path)
        {
            var l=new OceanLeaderboardLedger(path);Check(l.BeginRun(S,R,1),"shell run persisted");
            Check(l.RecordShell(S,R,0,0)&&l.RecordShell(S,R,0,5)&&l.ShellTotal==2&&l.Score==2,"different physical shells of same species each add one");
            Check(l.RecordShell(S,R,0,0)&&l.ShellTotal==2,"same shell stays one");
            Check(!l.RecordShell(S,R,5,1)&&!l.RecordShell(S,R,0,20000)&&!l.RecordShell(S,R,0,-1),"wrong target/coin slot/negative shell ID rejected");
            var shell=l.Batch().Single(e=>e.type=="shell"&&e.pickupId==5);
            Check(shell.eventId==OceanLeaderboardLedger.Hash("shell|"+R+"|5"),"server semantic shell ID exact");
            var batch=l.Batch();Check(l.ApplyAcknowledgement(Ack(batch.Select(e=>e.eventId)),Ids(batch)),"shell ACK");
            l=new OceanLeaderboardLedger(path);Check(l.ShellTotal==2&&l.RecordShell(S2,R,0,5)&&l.ShellTotal==2,"reboot/run resume preserves shell dedup");
            Check(l.BeginRun(S2,R2,1)&&l.RecordShell(S2,R2,0,0)&&l.ShellTotal==3,"new run same entity is a new discovery");
        }
        static void NicknameConfirmation(string path)
        {
            var l=new OceanLeaderboardLedger(path);Check(l.SetNickname("旧昵称"),"queue first name");
            var b=l.Batch();Check(l.ApplyAcknowledgement(ProfileAck(b.Select(e=>e.eventId),"旧昵称"),Ids(b))&&l.Nickname=="旧昵称","only server profile confirms name");
            Check(l.SetNickname("海底旅人")&&l.Nickname=="旧昵称"&&l.NicknameStatus=="pending","queued name never replaces confirmed name");
            l=new OceanLeaderboardLedger(path);Check(l.NicknameDraft=="海底旅人"&&l.Nickname=="旧昵称","offline draft survives restart");
            b=l.Batch();string first=l.PendingNicknameId;
            string refusal="{\"scoringVersion\":2,\"acceptedEventIds\":[],\"duplicateEventIds\":[],\"rejected\":[{\"eventId\":\""+first+"\",\"code\":\"nickname_taken\",\"retryable\":false}],\"profile\":"+ProfileWith("旧昵称")+"}";
            Check(l.ApplyAcknowledgement(refusal,Ids(b))&&l.Nickname=="旧昵称"&&l.NicknameStatus=="rejected"&&l.NicknameError=="nickname_taken"&&l.RejectedScoreTotal==0,"name conflict preserves old name without score-rejection warning");
            Check(l.SetNickname("海底旅人2")&&l.PendingNicknameId!=first,"retry gets new event identity");b=l.Batch();
            Check(!l.ApplyAcknowledgement(Ack(b.Select(e=>e.eventId)),Ids(b))&&l.NicknameStatus=="pending","ACK without actual profile cannot claim name saved");
            Check(l.ApplyAcknowledgement(ProfileAck(b.Select(e=>e.eventId),"海底旅人2"),Ids(b))&&l.Nickname=="海底旅人2"&&l.NicknameStatus=="accepted","unique nickname final profile confirmed");
            l.SetNickname("较旧草稿");b=l.Batch();l.SetNickname("最新草稿");
            Check(l.ApplyAcknowledgement(ProfileAck(b.Select(e=>e.eventId),"较旧草稿"),Ids(b))&&l.NicknameDraft=="最新草稿"&&l.NicknameStatus=="pending","earlier ACK does not replace newer pending draft");
            b=l.Batch();Check(l.ApplyAcknowledgement(ProfileAck(b.Select(e=>e.eventId),"服务端较新昵称"),Ids(b))&&l.NicknameStatus=="rejected"&&l.NicknameError=="profile_superseded","stale duplicate ACK is not a confirmed draft");
        }
        static void OldLedger(string path)
        {
            var l=new OceanLeaderboardLedger(path);l.BeginRun(S,R,3);l.RecordFish(S,R,0,10000);l.RecordCoin(S,R,20000);
            string file=Path.Combine(path,"ocean-leaderboard.json");var wrapper=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(File.ReadAllText(file)));
            var old=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(OceanLeaderboardJson.Text(wrapper,"body")));old["schema"]=1;
            foreach(string key in new[]{"shellTotal","rejectedScoreTotal","nicknameDraft","pendingNicknameId","nicknameStatus","nicknameError"})old.Remove(key);
            old["cachedPageJson"]=Page.Replace("\"score\":14","\"score\":12").Replace("\"scoringVersion\":2,","").Replace("\"shellTotal\":0,","");
            string body=OceanLeaderboardJson.Write(old);File.WriteAllText(file,OceanLeaderboardJson.Write(new Dictionary<string,object>{{"body",body},{"sha256",OceanLeaderboardLedger.Hash(body)}}));
            l=new OceanLeaderboardLedger(path);Check(l.Writable&&l.FishTotal==1&&l.CoinTotal==1&&l.ShellTotal==0&&l.Score==12,"v1 lifetime counters reweighted without invented shells");
            Check(l.CachedPageJson==""&&l.CachedPageCursor=="","old rank cache discarded on scoring migration");
        }
        static void Pinyin()
        {
            string data=File.ReadAllText(Path.Combine("Assets","Resources","Leaderboard","PinyinCandidates.txt"));
            var input=new OceanPinyinInput(data);Check(input.SyllableCount==420,"licensed dictionary loaded fully");
            foreach(var pair in new[]{new[]{"cheng","程"},new[]{"xiao","晓"},new[]{"long","龙"},new[]{"nv","女"},new[]{"lv","吕"},new[]{"ji","𫓹"}})
            {
                foreach(char c in pair[0])Check(input.Append(c),"pinyin accepts letters");int found=input.FindCandidate(pair[1]);Check(found>=0,"arbitrary common Han has candidate");
                input.ChangePage(found/8);Check(input.Select(found%8)==pair[1]&&input.Composition==""&&input.Page==0,"candidate page selection resets composition");
            }
            foreach(char c in "shuang")Check(input.Append(c),"six-letter syllable accepted");
            Check(!input.Append('a')&&input.Erase()&&input.Composition=="shuan","bounded phonetic input and deletion");input.Clear();Check(!input.Erase(),"empty phonetic erase yields to nickname erase");
            foreach(char c in "zzzzzz")input.Append(c);Check(input.Candidates==""&&input.Select(0)==""&&input.Composition=="zzzzzz","unknown spelling cannot invent text");
            input.Clear();input.Append('l');input.Append('i');input.ChangePage(99999);Check(input.Page==input.PageCount-1,"last candidate page clamps");input.ChangePage(-99999);Check(input.Page==0,"first candidate page clamps");
        }
        static void Protocol()
        {
            Check(OceanLeaderboardLedger.ValidNickname("潜水员😀",out _),"unicode scalar nickname supported");
            Check(!OceanLeaderboardLedger.ValidNickname("a\ue000b",out _)&&!OceanLeaderboardLedger.ValidNickname("a\nb",out _)&&!OceanLeaderboardLedger.ValidNickname("a\u200bb",out _)&&!OceanLeaderboardLedger.ValidNickname(new string('海',21),out _),"control/invisible/long nickname rejected");
            Check(OceanLeaderboardProtocol.ValidateBaseUrl("https://example.invalid/ocean/")=="https://example.invalid/ocean","single ocean prefix");
            Throws(()=>OceanLeaderboardProtocol.ValidateBaseUrl("http://example.invalid/ocean"),"no plaintext downgrade");
            Throws(()=>OceanLeaderboardProtocol.ValidateBaseUrl("https://secret@example.invalid:9999/ocean"),"no URL credentials");
            var read=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(Encoding.UTF8.GetString(OceanLeaderboardProtocol.PagePayload(new string('a',64),"opaque+cursor",20))));
            Check(read.Count==6&&OceanLeaderboardJson.Number(read,"scoringVersion")==2&&OceanLeaderboardJson.Text(read,"op")=="leaderboard"&&OceanLeaderboardJson.Text(read,"cursor")=="opaque+cursor"&&OceanLeaderboardJson.Number(read,"limit")==20,"signed read has exact versioned six-field contract without credentials or events");
            Throws(()=>OceanLeaderboardProtocol.PagePayload(new string('a',64),"",0),"invalid read limit rejected before signing");
            var page=OceanLeaderboardPage.Parse(Page);Check(page.entries.Length==1&&page.entries[0].score==14&&page.totalDevices==1&&page.nextCursor=="opaque+cursor/1","server DTO contract");
            Check(OceanLeaderboardPage.Parse(Page.Replace("\"myEntry\":null","\"myEntry\":"+Profile)).myEntry.rank==0,"unranked profile before first demo remains a valid own entry");
            Throws(()=>OceanLeaderboardPage.Parse(Page.Replace("\"score\":14","\"score\":99")),"inconsistent score rejected");
            Throws(()=>OceanLeaderboardPage.Parse(Page.Replace("\"scoringVersion\":2","\"scoringVersion\":1")),"old scoring view is never presented as new ranking");
            Throws(()=>OceanLeaderboardJson.Parse("{\"a\":1,\"a\":2}"),"duplicate JSON fields rejected");
            string escaped="中文\"\\\n😀";Check((string)OceanLeaderboardJson.Parse(OceanLeaderboardJson.Write(escaped))==escaped,"UTF8 JSON exact roundtrip");
        }
        static void Signing(string path)
        {
#if UNITY_EDITOR || OCEAN_LEADERBOARD_TESTS
            using(var identity=OceanLeaderboardIdentity.Open(path))
            {
                Check(identity.IsTestIdentity&&identity.DeviceId.Length==64,"explicit test identity");
                byte[] spki=Convert.FromBase64String(identity.PublicKeySpkiB64);using(var sha=SHA256.Create())Check(BitConverter.ToString(sha.ComputeHash(spki)).Replace("-","").ToLowerInvariant()==identity.DeviceId,"identity derived from exact SPKI bytes");
                var batch=new List<OceanLeaderboardEvent>{new OceanLeaderboardEvent{eventId=OceanLeaderboardLedger.Hash("test"),sequence=1,type="demo",sessionId=S,createdUtc="2026-09-21T00:00:00Z"}};
                byte[] payload=OceanLeaderboardProtocol.Payload(identity.DeviceId,batch),signature=identity.Sign(payload),envelope=OceanLeaderboardProtocol.Envelope(identity.PublicKeySpkiB64,payload,signature);
                var decoded=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(Encoding.UTF8.GetString(envelope)));
                Check(Convert.FromBase64String(OceanLeaderboardJson.Text(decoded,"payloadB64")).SequenceEqual(payload),"envelope preserves signed original payload bytes");
                using(var rsa=RSA.Create())
                {
                    rsa.FromXmlString(File.ReadAllText(Path.Combine(path,"editor-test-rsa.xml")));
                    Check(rsa.KeySize==2048&&rsa.VerifyData(payload,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1),"RSA2048 SHA256 PKCS1v1.5 signature");
                    Check(OceanLeaderboardIdentity.SubjectPublicKeyInfo(rsa.ExportParameters(false)).SequenceEqual(spki),"SPKI stable encoding");
                    payload[0]^=1;Check(!rsa.VerifyData(payload,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1),"changed payload cannot verify");
                }
                using(var reload=OceanLeaderboardIdentity.Open(path))Check(reload.DeviceId==identity.DeviceId,"test key survives reopen");
            }
#endif
        }
        static HashSet<string> Ids(IEnumerable<OceanLeaderboardEvent> events) => new HashSet<string>(events.Select(x=>x.eventId));
        static string Ack(IEnumerable<string> ids) => OceanLeaderboardJson.Write(new Dictionary<string,object>{{"scoringVersion",2},{"acceptedEventIds",ids.ToArray()},{"duplicateEventIds",new string[0]},{"rejected",new object[0]}});
    }
}

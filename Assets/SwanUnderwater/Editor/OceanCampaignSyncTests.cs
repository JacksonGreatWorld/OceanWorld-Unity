#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    public static class OceanCampaignSyncTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool value,string message)
        { Assertions++; if(!value) throw new InvalidOperationException("Campaign sync: "+message); }
        public static void RunAll()
        {
            Assertions=0;
            Check(OceanCampaignSyncPolicy.MaximumLevel(null)==20,"old server is bounded to20");
            foreach(object value in new object[]{"100",19L,101L,99.5,double.NaN,double.PositiveInfinity})
                Check(OceanCampaignSyncPolicy.MaximumLevel(new Dictionary<string,object>{{"maxCampaignLevel",value}})==20,"malformed capability cannot release outbox");
            Check(OceanCampaignSyncPolicy.MaximumLevel(new Dictionary<string,object>{{"maxCampaignLevel",100L}})==100,"explicit100 capability accepted");
            string directory=Path.Combine(Path.GetTempPath(),"ocean-sync-gate-"+Guid.NewGuid().ToString("N"));
            try
            {
                const string session="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
                var ledger=new OceanLeaderboardLedger(directory);
                for(int i=0;i<70;i++) Check(ledger.BeginRun(session,i.ToString("x32"),21),"durable extended run");
                Check(ledger.BeginRun(session,"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",1),"legacy run behind extended queue");
                Check(ledger.RecordShell(session,"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",0,42),"legacy score queued");
                string file=Path.Combine(directory,"ocean-leaderboard.json");
                string before=File.ReadAllText(file); int pending=ledger.PendingCount;
                var oldBatch=ledger.Batch(64,20);
                Check(oldBatch.All(e=>e.level<=20)&&oldBatch.Any(e=>e.type=="shell"),"filter before take avoids legacy starvation");
                Check(ledger.PendingCount==pending&&File.ReadAllText(file)==before,"filter never removes/rejects evidence");
                var reload=new OceanLeaderboardLedger(directory);
                Check(reload.PendingCount==pending&&reload.Batch(64,100).Any(e=>e.level==21),"unsupported data survives cold restart and can later release");
                var observation=new OceanDiscoveryLedger(Path.Combine(directory,"discoveries"));
                Check(observation.Record(session,0,21)&&observation.Record(session,1,1),"new and old observations queued");
                Check(observation.Batch(20).Count==1&&observation.Batch(100).Count==2&&observation.PendingCount==2,"observation gate preserves all records");
            }
            finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
            Debug.Log("OceanCampaignSyncTests PASS: "+Assertions+" assertions; local synthetic identities, no network requests.");
        }
    }
}
#endif

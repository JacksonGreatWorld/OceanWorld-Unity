#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Real temporary ledger/key/config checks. Test identities never contact a production server.</summary>
    public static class OceanPublicExportChecks
    {
        public static int Assertions { get; private set; }
        public static bool MissingConfigurationChecks { get; private set; }
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool condition,string reason)
        { Assertions++;if(!condition)throw new InvalidOperationException("Public leaderboard release: "+reason); }
        static void Throws(Action action,string reason)
        { bool rejected=false;try{action();}catch(Exception){rejected=true;}Check(rejected,reason); }
        static void Tick(OceanLeaderboardClient client) => typeof(OceanLeaderboardClient).GetMethod("Update",Private).Invoke(client,null);
        static void Until(OceanLeaderboardClient client,Func<bool> done)
        {
            DateTime deadline=DateTime.UtcNow.AddSeconds(30);
            while(!done()) { if(DateTime.UtcNow>deadline)throw new TimeoutException("Private Editor identity did not settle.");Tick(client);Thread.Sleep(2); }
        }
        static string Storage(string root)=>Path.Combine(root,"editor-test");
        static Dictionary<string,byte[]> Originals(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
        static void Unchanged(Dictionary<string,byte[]> originals)
        { foreach(var item in originals)Check(File.Exists(item.Key)&&File.ReadAllBytes(item.Key).SequenceEqual(item.Value),"pre-existing ledger/evidence bytes remain unchanged"); }

        [MenuItem("OceanWorld/Validation/Public leaderboard release")]
        public static void RunAll()
        {
            Assertions=0;MissingConfigurationChecks=false;
            string root=Path.Combine(Path.GetTempPath(),"ocean-public-online-contract-"+Guid.NewGuid().ToString("N"));
            var hosts=new List<GameObject>();
            try
            {
                Check(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)==ScriptingImplementation.IL2CPP,"Android backend remains IL2CPP after public sanitization");
                Check(PlayerSettings.GetMobileMTRendering(NamedBuildTarget.Android),"Android multithreaded rendering remains a typed boolean after public sanitization");
                var androidDefines=new HashSet<string>(PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android).Split(';'));
                Check(androidDefines.Contains("ENABLE_PICO_XR_SDK")&&androidDefines.Contains("PICO_SPATIALIZER"),"Android SDK symbols remain defines rather than the public package identifier");
                Check(File.Exists(Path.Combine(Application.dataPath,"Resources/PXR_ProjectSetting.asset")),"PICO application capability settings are retained instead of relying on SDK defaults");
                var picoConfig=ByteDance.PICO.XR.PXR_ProjectSetting.GetProjectConfig();
                Check(picoConfig!=null&&picoConfig.handTracking&&picoConfig.videoSeeThrough,"PICO hand tracking and passthrough remain enabled for the existing entry and swimming controls");
                Directory.CreateDirectory(root);
                Check(OceanLeaderboardConfiguration.Parse("{\"baseUrl\":\"https://example.invalid/ocean/\"}")=="https://example.invalid/ocean","private configuration normalizes HTTPS API prefix");
                foreach(string invalid in new[]{"{}","{\"baseUrl\":\"http://example.invalid\"}","{\"baseUrl\":\"https://user:secret@example.invalid\"}","{\"baseUrl\":\"https://example.invalid?token=secret\"}","{\"baseUrl\":\"https://example.invalid#secret\"}","{\"baseUrl\":\"https://example.invalid\",\"adminPassword\":\"fixture\"}","{\"baseUrl\":\"https://example.invalid\",\"baseUrl\":\"https://other.invalid\"}"})
                    Throws(()=>OceanLeaderboardConfiguration.Parse(invalid),"unsafe or ambiguous release configuration rejected");
                foreach(string invalidUrl in new[]{"https://example.invalid?","https://example.invalid#"," https://example.invalid","https://example.invalid/a b","https://example.invalid\\path"})
                    Throws(()=>OceanLeaderboardProtocol.ValidateBaseUrl(invalidUrl),"explicit URL uses same strict no-query/no-fragment/no-whitespace contract");
                string config=Path.Combine(root,"release.json");
                Throws(()=>OceanLeaderboardReleaseGuard.ValidateReleaseConfiguration(config),"Android release requires a configuration file");
                File.WriteAllText(config,"{\"baseUrl\":\"http://example.invalid\"}");
                Throws(()=>OceanLeaderboardReleaseGuard.ValidateReleaseConfiguration(config),"Android release rejects plaintext configuration");
                File.WriteAllText(config,"{\"baseUrl\":\"https://example.invalid/ocean\"}");
                OceanLeaderboardReleaseGuard.ValidateReleaseConfiguration(config);Check(true,"valid private HTTPS configuration passes release gate");
                Check(typeof(OceanLeaderboardProtocol).GetField("ProductionBaseUrl")==null,"no production endpoint constant exported");

                string directory=Path.Combine(root,"new");
                var firstHost=new GameObject("Isolated public network-capable contract");hosts.Add(firstHost);
                var client=firstHost.AddComponent<OceanLeaderboardClient>();client.Initialize("https://example.invalid/ocean",directory);
                Until(client,()=>client.Ready);
                var signer=(OceanLeaderboardIdentity)typeof(OceanLeaderboardClient).GetField("identity",Private).GetValue(client);
                Check(!client.OfflineOnly&&signer.IsTestIdentity&&client.DeviceId==signer.DeviceId&&client.DeviceId.Length==64,"configured public client uses a real Editor signing identity, never a random installation ID");
                Check(client.EnsureSession(),"session is durably saved");
                string run=Guid.NewGuid().ToString("N");Check(client.BeginRun(run,3),"run is durably saved");
                Check(client.RecordShell(run,0,300)&&client.RecordFish(run,0,10000)&&client.RecordCoin(run,20000),"real shell/fish/coin transactions persist");
                Check(client.LocalScore==13&&client.LocalShellTotal==1&&client.LocalFishTotal==1&&client.LocalCoinTotal==1,"real score uses one/two/ten weights");
                Check(client.RecordShell(run,0,300)&&client.LocalScore==13,"repeated pickup cannot inflate score");
                Check(client.RecordDiscovery(0,3)&&client.PendingDiscoveryCount==1,"real observation remains in its own durable outbox");
                Check(client.SetNickname("珊瑚旅人")&&client.NicknameDraft=="珊瑚旅人"&&client.NicknameStatus=="pending"&&client.Nickname!="珊瑚旅人","Chinese rename remains pending until server uniqueness confirmation");
                int pending=client.PendingCount;
                client.RefreshPage("opaque-fixture-cursor");Tick(client);
                Check(!client.IsBusy&&!client.CachedPageIsLive&&client.FinishedPageRequestVersion==client.PageRequestVersion,"Editor test identity refuses nonloopback page request before creating transport");
                Check(client.LastError.Contains("测试模式")&&client.PendingCount==pending&&client.PendingDiscoveryCount==1,"refused Editor request keeps both outboxes without fake ACK");
                var actualLedger=(OceanLeaderboardLedger)typeof(OceanLeaderboardClient).GetField("ledger",Private).GetValue(client);
                string cache="{\"scoringVersion\":2,\"entries\":[],\"nextCursor\":\"opaque-next\",\"totals\":{\"devices\":0,\"demoStarts\":0},\"myEntry\":null,\"serverTime\":\"local-test-fixture\"}";
                Check(actualLedger.CachePage("opaque-current",cache),"real page cache persists its opaque cursor without modifying outbox");
                var coldHost=new GameObject("Cold signed public contract");hosts.Add(coldHost);var cold=coldHost.AddComponent<OceanLeaderboardClient>();cold.Initialize("https://example.invalid/ocean",directory);Until(cold,()=>cold.Ready);
                Check(cold.DeviceId==client.DeviceId&&cold.LocalScore==13&&cold.LocalDiscoveryCount==1&&cold.NicknameDraft=="珊瑚旅人"&&cold.NicknameStatus=="pending","cold reload preserves signing identity, real counters and pending rename");
                Check(cold.CachedPage!=null&&cold.CachedPageCursor=="opaque-current"&&cold.CachedPage.nextCursor=="opaque-next"&&!cold.CachedPageIsLive&&cold.PendingCount==pending,"cold cached page/cursor is readable but never labelled live or acknowledged");
                Check(cold.EnsureSession()&&cold.LocalDemoStarts==client.LocalDemoStarts,"same process session is not counted twice");

                string oldRoot=Path.Combine(root,"random-id-history"),oldPath=Storage(oldRoot);Directory.CreateDirectory(oldPath);
                var old=new OceanLeaderboardLedger(oldPath);var oldDiscoveries=new OceanDiscoveryLedger(oldPath);string randomId=new string('a',64),session=new string('b',32),oldRun=new string('c',32);
                Check(old.BindIdentity(randomId)&&old.BeginRun(session,oldRun,3)&&old.RecordFish(session,oldRun,0,10000)&&old.SetNickname("旧版旅人"),"seed prior public random-ID score originals");
                Check(oldDiscoveries.BindIdentity(randomId)&&oldDiscoveries.Record(session,0,3),"seed prior public random-ID discovery originals");
                var originals=Originals(oldPath);int oldPending=old.PendingCount;
                var conflictHost=new GameObject("Preserved incompatible public identity");hosts.Add(conflictHost);var conflict=conflictHost.AddComponent<OceanLeaderboardClient>();conflict.Initialize("https://example.invalid/ocean",oldRoot);Until(conflict,()=>conflict.IdentityMigrationRequired);
                Check(!conflict.Ready&&!conflict.IsBusy&&conflict.DeviceId==randomId&&conflict.LocalScore==2&&conflict.PendingCount==oldPending&&conflict.PendingDiscoveryCount==1,"identity conflict retains original ID, score and both pending outboxes");
                Check(conflict.LastError.Contains("需要人工迁移")&&!conflict.EnsureSession()&&!conflict.SetNickname("不能覆盖"),"conflict visibly requires migration and refuses writes instead of rebind");
                conflict.RefreshPage();Tick(conflict);Check(!conflict.CachedPageIsLive&&!conflict.IsBusy&&conflict.RejectedEventCount==0,"identity mismatch creates no transport, fake ACK or rejected score");
                Unchanged(originals);
                string discoveryOnlyRoot=Path.Combine(root,"discovery-only-history"),discoveryOnlyPath=Storage(discoveryOnlyRoot);Directory.CreateDirectory(discoveryOnlyPath);
                var historicalDiscoveries=new OceanDiscoveryLedger(discoveryOnlyPath);
                Check(historicalDiscoveries.BindIdentity(randomId)&&historicalDiscoveries.Record(session,0,3),"seed discovery-only original history without a score ledger");
                var discoveryOriginals=Originals(discoveryOnlyPath);
                var discoveryHost=new GameObject("Discovery-only identity conflict");hosts.Add(discoveryHost);var discoveryConflict=discoveryHost.AddComponent<OceanLeaderboardClient>();discoveryConflict.Initialize("https://example.invalid/ocean",discoveryOnlyRoot);
                Check(!discoveryConflict.EnsureSession()&&!discoveryConflict.SetNickname("不得提前写入"),"either pre-existing identity blocks writes while its signing identity is unresolved");
                Check(!File.Exists(Path.Combine(discoveryOnlyPath,"ocean-leaderboard.json")),"discovery-only unresolved identity does not create a new score ledger");
                Until(discoveryConflict,()=>discoveryConflict.IdentityMigrationRequired);
                Check(discoveryConflict.PendingDiscoveryCount==1&&!discoveryConflict.Ready&&!discoveryConflict.IsBusy&&!File.Exists(Path.Combine(discoveryOnlyPath,"ocean-leaderboard.json")),"discovery-only mismatch keeps its pending history without new score identity or transport");
                Unchanged(discoveryOriginals);

                if(OceanLeaderboardConfiguration.Resolve()==null)
                {
                    string offlineRoot=Path.Combine(root,"missing-release-configuration");
                    var offlineHost=new GameObject("Real missing-configuration client");hosts.Add(offlineHost);var offline=offlineHost.AddComponent<OceanLeaderboardClient>();offline.Initialize(storageDirectory:offlineRoot);Until(offline,()=>offline.Ready);
                    Check(offline.OfflineOnly&&offline.DeviceId.Length==64&&!offline.IsBusy,"genuinely missing configuration still opens a platform signing identity without transport");
                    string offlineRun=Guid.NewGuid().ToString("N");
                    Check(offline.BeginRun(offlineRun,3)&&offline.RecordShell(offlineRun,0,300)&&offline.RecordFish(offlineRun,0,10000)&&offline.RecordCoin(offlineRun,20000)&&offline.RecordDiscovery(0,3),"missing configuration preserves genuine local scoring and discovery writes");
                    int scorePending=offline.PendingCount,discoveryPending=offline.PendingDiscoveryCount;
                    offline.RefreshPage();Tick(offline);offline.RefreshPage(discovery:true);Tick(offline);
                    Check(offline.LocalScore==13&&offline.PendingCount==scorePending&&offline.PendingDiscoveryCount==discoveryPending&&!offline.IsBusy&&!offline.CachedPageIsLive&&offline.FinishedPageRequestVersion==offline.PageRequestVersion,"missing configuration finishes page requests and never creates fake ACK or live results");
                    var offlineColdHost=new GameObject("Cold missing-configuration client");hosts.Add(offlineColdHost);var offlineCold=offlineColdHost.AddComponent<OceanLeaderboardClient>();offlineCold.Initialize(storageDirectory:offlineRoot);Until(offlineCold,()=>offlineCold.Ready);
                    Check(offlineCold.OfflineOnly&&offlineCold.DeviceId==offline.DeviceId&&offlineCold.LocalScore==13&&offlineCold.PendingCount==scorePending&&offlineCold.PendingDiscoveryCount==1,"missing-configuration cold restart retains exact signing identity and both durable outboxes");
                    MissingConfigurationChecks=true;
                }
                else Debug.Log("OceanPublicExportChecks missing-configuration subcase NOT EXECUTED: an existing private release configuration is present; no deployment asset was modified by this test.");
                Debug.Log("OceanPublicExportChecks PASS "+Assertions+" assertions; missingConfigurationChecks="+MissingConfigurationChecks+"; real temporary ledgers and Editor signing keys; private config/release gate; no network requests, player data or device execution.");
            }
            finally
            {
                foreach(var host in hosts)if(host!=null)UnityEngine.Object.DestroyImmediate(host);
                if(Directory.Exists(root))Directory.Delete(root,true);
            }
        }
    }
}
#endif

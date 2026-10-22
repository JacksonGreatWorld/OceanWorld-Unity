using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace SwanUnderwater
{
    /// <summary>Main-thread durable events; background Keystore crypto; one HTTPS operation at a time.</summary>
    public sealed class OceanLeaderboardClient : MonoBehaviour
    {
        static string processSessionId;
        OceanLeaderboardLedger ledger;
        OceanDiscoveryLedger discoveries;
        OceanLeaderboardIdentity identity;
        Task<OceanLeaderboardIdentity> identityTask;
        Task<byte[]> signingTask;
        UnityWebRequest request;
        Coroutine worker;
        string baseUrl, directory, lastError, queuedCursor, configurationError;
        int queuedLimit=20, queuedVersion, pageRequestVersion, failedAttempts;
        bool pageQueued, initialized, destroyed, queuedDiscovery, identityConflict;
        public const string OfflineStatus="暂时离线 · 成绩和发现已保存在本机";
        public bool OfflineOnly => !OceanReleaseFeatures.LeaderboardsEnabled || string.IsNullOrEmpty(baseUrl);
        const string UnavailableMessage="本版本暂未开放排行榜。";
        bool OnlineAllowed => OceanReleaseFeatures.LeaderboardsEnabled && initialized && !destroyed && !OfflineOnly && !identityConflict;
        public bool IdentityMigrationRequired => identityConflict;
        int discoverySupport; // 0 unknown, 1 current catalog, -1 unavailable. Never drop unsupported outbox.
        int serverMaximumCampaignLevel=OceanCampaignSyncPolicy.LegacyMaximumLevel;
        float discoveryProbeAfter, discoveryAttemptAfter;
        string discoveryError;
        public int LocalDiscoveryCount => discoveries?.Count??0;
        public int PendingDiscoveryCount => discoveries?.PendingCount??0;
        public string DiscoveryStatus => discoveryError??(OfflineOnly?OfflineStatus:"");
        float nextAttempt;
        readonly System.Random jitter=new System.Random();
        public event Action Changed;
        public event Action PageChanged;
        public string DeviceId => OceanReleaseFeatures.LeaderboardsEnabled ? identity?.DeviceId ?? ledger?.DeviceId ?? "" : "";
        public string PublicId => OceanReleaseFeatures.LeaderboardsEnabled ? Profile?.publicId ?? "" : "";
        public string Nickname => !OceanReleaseFeatures.LeaderboardsEnabled ? "" : !string.IsNullOrEmpty(ledger?.Nickname)?ledger.Nickname:Profile?.nickname??(DeviceId.Length>=6?"设备-"+DeviceId.Substring(0,6):"设备-待同步");
        public int PendingCount => ledger?.PendingCount??0;
        public string NicknameDraft => OceanReleaseFeatures.LeaderboardsEnabled ? ledger?.NicknameDraft??"" : "";
        public string NicknameStatus => OceanReleaseFeatures.LeaderboardsEnabled ? ledger?.NicknameStatus??"idle" : "disabled";
        public string NicknameError => OceanReleaseFeatures.LeaderboardsEnabled ? ledger?.NicknameError??"" : "";
        public long LocalShellTotal => ledger?.ShellTotal??0;
        public long LocalFishTotal => ledger?.FishTotal??0;
        public long LocalCoinTotal => ledger?.CoinTotal??0;
        public long LocalScore => ledger?.Score??0;
        public long LocalDemoStarts => ledger?.DemoTotal??0;
        public long RejectedEventCount => ledger?.RejectedScoreTotal??0;
        public bool IsBusy => OnlineAllowed && worker!=null;
        public bool Ready => OnlineAllowed && ledger.Writable && identity!=null;
        public string LastError => lastError;
        OceanLeaderboardPage cachedPage;
        OceanLeaderboardRow profile;
        string cachedPageCursor="";
        public OceanLeaderboardPage CachedPage { get => OceanReleaseFeatures.LeaderboardsEnabled ? cachedPage : null; private set => cachedPage=value; }
        public OceanLeaderboardRow Profile { get => OceanReleaseFeatures.LeaderboardsEnabled ? profile : null; private set => profile=value; }
        public string CachedPageCursor { get => OceanReleaseFeatures.LeaderboardsEnabled ? cachedPageCursor : ""; private set => cachedPageCursor=value; }
        // Incremented by RefreshPage. A result is published only for the newest request.
        public int PageRequestVersion => pageRequestVersion;
        public int CompletedPageRequestVersion { get; private set; }
        public int FinishedPageRequestVersion { get; private set; }
        public bool CachedPageIsLive { get; private set; }
        public string SessionId => processSessionId??"";

        public void Initialize(string url=null,string storageDirectory=null)
        {
            if(initialized) return;
            if(OceanReleaseFeatures.LeaderboardsEnabled)
            {
                try { baseUrl=OceanLeaderboardConfiguration.Resolve(url); }
                catch(Exception) when(url==null)
                { baseUrl=null;configurationError="联网暂不可用 · 成绩和发现已保存在本机"; }
            }
            directory=storageDirectory??Path.Combine(Application.persistentDataPath,"Leaderboard");
#if UNITY_EDITOR
            directory=Path.Combine(directory,"editor-test");
#endif
            ledger=new OceanLeaderboardLedger(directory,queueForUpload:OceanReleaseFeatures.LeaderboardsEnabled);discoveries=new OceanDiscoveryLedger(directory);initialized=true;lastError=ledger.LastError??configurationError;discoveryError=discoveries.LastError;
            // Local totals remain available; disabled releases never resolve configuration, names or keys.
            if(!OceanReleaseFeatures.LeaderboardsEnabled) { Changed?.Invoke();return; }
            ReadProfile();
            if(ledger.CachedPageJson.Length>0)
            {
                try { CachedPage=OceanLeaderboardPage.Parse(ledger.CachedPageJson);CachedPageCursor=ledger.CachedPageCursor; }
                catch(Exception error) { lastError="Cached leaderboard unavailable: "+error.Message; }
            }
            if(ledger.Writable) StartIdentity();
            Changed?.Invoke();
        }
        public bool EnsureSession()
        {
            if(!initialized)return NotInitialized();
            if(identityConflict) { Changed?.Invoke();return false; }
            // Do not touch a pre-existing ledger until its platform key has been checked.
            if(OceanReleaseFeatures.LeaderboardsEnabled && identity==null&&(ledger.DeviceId.Length>0||discoveries.DeviceId.Length>0)) { lastError="正在核验本机排行榜身份，请稍后重试";return false; }
            if(processSessionId==null)processSessionId=Guid.NewGuid().ToString("N");
            return Persist(()=>ledger.EnsureSession(processSessionId));
        }
        public bool BeginRun(string runId,int level) => EnsureSession()&&Persist(()=>ledger.BeginRun(processSessionId,runId,level));
        public bool RecordShell(string runId,int species,int eventPickupId) => EnsureSession()&&Persist(()=>ledger.RecordShell(processSessionId,runId,species,eventPickupId));
        public bool RecordFish(string runId,int species,int eventPickupId) => EnsureSession()&&Persist(()=>ledger.RecordFish(processSessionId,runId,species,eventPickupId));
        public bool RecordShark(string runId,int eventPickupId) => EnsureSession()&&Persist(()=>ledger.RecordShark(processSessionId,runId,eventPickupId));
        public bool RecordCoin(string runId,int eventPickupId) => EnsureSession()&&Persist(()=>ledger.RecordCoin(processSessionId,runId,eventPickupId));
        public bool RecordDiscovery(int species,int level)
        {
            if(!initialized)return NotInitialized();
            if(!OceanReleaseFeatures.LeaderboardsEnabled)return true;
            if(!EnsureSession())return false;
            int before=discoveries.Count;bool saved=discoveries.Record(processSessionId,species,level);
            if(!saved)discoveryError=discoveries.LastError;
            if(!saved||before!=discoveries.Count)Changed?.Invoke();return saved;
        }
        public bool SetNickname(string value)
        { if(!OceanReleaseFeatures.LeaderboardsEnabled) { lastError=UnavailableMessage;return false; }
          return initialized?Persist(()=>ledger.SetNickname(value)):NotInitialized(); }
        bool NotInitialized() { lastError="Leaderboard has not been initialized.";return false; }
        bool Persist(Func<bool> operation)
        {
            if(identityConflict) { Changed?.Invoke();return false; }
            if(OceanReleaseFeatures.LeaderboardsEnabled && identity==null&&(ledger.DeviceId.Length>0||discoveries.DeviceId.Length>0)) { lastError="正在核验本机排行榜身份，请稍后重试";return false; }
            int before=ledger.PendingCount;long priorScore=ledger.Score,priorDemos=ledger.DemoTotal;string priorStatus=ledger.NicknameStatus;bool success=operation();
            bool changed=before!=ledger.PendingCount||priorScore!=ledger.Score||priorDemos!=ledger.DemoTotal||priorStatus!=ledger.NicknameStatus;
            if(!success)lastError=ledger.LastError;
            else if(changed)lastError=configurationError;
            if(!success||changed)Changed?.Invoke();
            return success;
        }
        public void RefreshPage(string cursor="",int limit=20,bool discovery=false)
        {
            if(!OceanReleaseFeatures.LeaderboardsEnabled) { lastError=UnavailableMessage;return; }
            if(!initialized) { NotInitialized();return; }
            if(cursor==null||cursor.Length>4096) { lastError="Invalid leaderboard cursor.";Changed?.Invoke();return; }
            queuedDiscovery=discovery;
            string cached=discovery?discoveries.CachedPageJson:ledger.CachedPageJson;
            if(cached.Length>0){try{CachedPage=OceanLeaderboardPage.Parse(cached,allowLegacyDiscovery:discovery);CachedPageCursor=discovery?discoveries.CachedPageCursor:ledger.CachedPageCursor;}catch(Exception){CachedPage=null;}}
            else {CachedPage=null;CachedPageCursor="";}
            pageRequestVersion++;queuedVersion=pageRequestVersion;queuedCursor=cursor;queuedLimit=Mathf.Clamp(limit,1,50);pageQueued=true;
            if(OfflineOnly||identityConflict)
            {
                CachedPageIsLive=false;FinishedPageRequestVersion=queuedVersion;pageQueued=false;Changed?.Invoke();return;
            }
            // Cached content is immediately usable, but never labelled as live after a new request.
            CachedPageIsLive=false;nextAttempt=0;Changed?.Invoke();
        }
        void StartIdentity() { if(!OceanReleaseFeatures.LeaderboardsEnabled)return;identityTask=Task.Run(()=>OceanLeaderboardIdentity.Open(directory)); }
        void Update()
        {
            if(!OceanReleaseFeatures.LeaderboardsEnabled||!initialized||destroyed)return;
            if(identityTask!=null&&identityTask.IsCompleted)
            {
                var completed=identityTask;identityTask=null;
                try
                {
                    var candidate=completed.GetAwaiter().GetResult();
                    if((ledger.DeviceId.Length>0&&ledger.DeviceId!=candidate.DeviceId)||(discoveries.DeviceId.Length>0&&discoveries.DeviceId!=candidate.DeviceId))
                    {
                        // The earlier public offline build used a random ID. Never assign its
                        // events to a different signing key or rewrite/ACK its original files.
                        identityConflict=true;candidate.Dispose();
                        lastError="排行榜身份与本机历史不一致：原始成绩和发现已保留，需要人工迁移；不会自动上传或重绑定。";
                        discoveryError=lastError;FailQueuedPage();
                    }
                    else if(!ledger.BindIdentity(candidate.DeviceId)) { candidate.Dispose();Backoff(ledger.LastError);FailQueuedPage(); }
                    else { identity=candidate;if(!discoveries.BindIdentity(candidate.DeviceId))discoveryError=discoveries.LastError;lastError=configurationError;failedAttempts=0;nextAttempt=0;Debug.Log("[OceanLeaderboard] identity_ready testIdentity="+identity.IsTestIdentity); }
                }
                catch(Exception error) { Backoff("Device signing identity unavailable: "+error.Message);FailQueuedPage(); }
                Changed?.Invoke();
            }
            if(!identityConflict&&identity==null&&identityTask==null&&ledger.Writable&&Time.realtimeSinceStartup>=nextAttempt)StartIdentity();
            if(worker!=null)return;
            if(OfflineOnly||identityConflict) { FailQueuedPage();return; }
            // A deliberate page request is independent of background upload backoff.
            // Signing still requires the ready Keystore identity; there is never an anonymous read.
            if(pageQueued&&!ledger.Writable) { lastError=ledger.LastError;FailQueuedPage(); }
            if(pageQueued&&Ready&&identity.IsTestIdentity&&!new Uri(baseUrl).IsLoopback)
            {
                lastError="测试模式仅查看本地排行榜缓存，不连接正式排行榜。";
                FailQueuedPage();return;
            }
            if(pageQueued&&Ready)
            {
                string cursor=queuedCursor;int limit=queuedLimit,version=queuedVersion;pageQueued=false;
                worker=StartCoroutine(FetchPage(cursor,limit,version,queuedDiscovery));
            }
            else if(!pageQueued&&Ready&&(PendingCount>0||PendingDiscoveryCount>0)&&!identity.IsTestIdentity&&Time.realtimeSinceStartup>=discoveryProbeAfter)
                worker=StartCoroutine(ProbeDiscoveries());
            else if(!pageQueued&&Ready&&PendingCount>0&&ledger.HasUploadableEvents(serverMaximumCampaignLevel)&&!identity.IsTestIdentity&&Time.realtimeSinceStartup>=nextAttempt)worker=StartCoroutine(Upload());
            else if(!pageQueued&&Ready&&discoveries.Writable&&PendingDiscoveryCount>0&&!identity.IsTestIdentity&&Time.realtimeSinceStartup>=discoveryAttemptAfter)
            {
                if(discoverySupport!=1&&Time.realtimeSinceStartup>=discoveryProbeAfter)worker=StartCoroutine(ProbeDiscoveries());
                else if(discoverySupport==1&&!ledger.HasUploadableEvents(serverMaximumCampaignLevel))worker=StartCoroutine(UploadDiscoveries());
            }
        }
        void FailQueuedPage()
        {
            if(!pageQueued)return;
            FinishedPageRequestVersion=queuedVersion;pageQueued=false;Changed?.Invoke();
        }
        IEnumerator FetchPage(string cursor,int limit,int version,bool discovery)
        {
            // First yield ensures worker is assigned before any synchronous early return.
            yield return null;
            if(!OnlineAllowed) { worker=null;yield break; }
            byte[] payload=null,envelope=null;
            try { payload=OceanLeaderboardProtocol.PagePayload(identity.DeviceId,cursor,limit,discovery);var signer=identity;signingTask=Task.Run(()=>signer.Sign(payload)); }
            catch(Exception error) { Backoff("Could not prepare signed page request: "+error.Message); }
            if(signingTask!=null)
            {
                while(!signingTask.IsCompleted)yield return null;
                try { envelope=OceanLeaderboardProtocol.Envelope(identity.PublicKeySpkiB64,payload,signingTask.GetAwaiter().GetResult()); }
                catch(Exception error) { Backoff("Page request signing failed: "+error.Message); }
                signingTask=null;
            }
            if(envelope==null)
            { Debug.LogWarning("[OceanLeaderboard] page_failure http=0 signing=true");FinishedPageRequestVersion=version;worker=null;Changed?.Invoke();yield break; }
            if(!OnlineAllowed) { worker=null;yield break; }
            request=new UnityWebRequest(baseUrl+(discovery?"/v1/discovery-board":"/v1/leaderboard"),"POST") { uploadHandler=new UploadHandlerRaw(envelope),downloadHandler=new DownloadHandlerBuffer() };
            request.SetRequestHeader("Content-Type","application/json; charset=utf-8");Configure(request);
            yield return request.SendWebRequest();
            string body=request.downloadHandler.text;
            bool ok=request.result==UnityWebRequest.Result.Success&&request.responseCode==200;
            string failure=request.error;long status=request.responseCode;
            request.Dispose();request=null;
            if(ok)
            {
                try
                {
                    var page=OceanLeaderboardPage.Parse(body);
                    if(page.IsDiscovery!=discovery)throw new FormatException("Leaderboard kind mismatch.");
                    if(version==pageRequestVersion)
                    {
                        if(discovery?!discoveries.CachePage(cursor,body):!ledger.CachePage(cursor,body))throw new IOException(discovery?discoveries.LastError:ledger.LastError);
                        CachedPage=page;CachedPageCursor=cursor;CachedPageIsLive=true;CompletedPageRequestVersion=version;Debug.Log("[OceanLeaderboard] page_success http="+status);
                        if(page.myEntry!=null&&!discovery)Profile=page.myEntry;
                        lastError=null;failedAttempts=0;nextAttempt=0;PageChanged?.Invoke();
                    }
                }
                catch(Exception error) { Debug.LogWarning("[OceanLeaderboard] page_failure http="+status);Backoff("Leaderboard page could not be saved: "+error.Message); }
            }
            else { Debug.LogWarning("[OceanLeaderboard] page_failure http="+status);Backoff("Leaderboard read failed ("+status+"): "+failure); }
            FinishedPageRequestVersion=version;worker=null;Changed?.Invoke();
        }
        IEnumerator Upload()
        {
            yield return null;
            if(!OnlineAllowed) { worker=null;yield break; }
            var batch=ledger.Batch(64,serverMaximumCampaignLevel);if(batch.Count==0) { worker=null;yield break; }
            var submitted=new HashSet<string>();foreach(var e in batch)submitted.Add(e.eventId);
            byte[] payload=null;
            try { payload=OceanLeaderboardProtocol.Payload(identity.DeviceId,batch);var signer=identity;signingTask=Task.Run(()=>signer.Sign(payload)); }
            catch(Exception error) { Backoff("Could not prepare signed events: "+error.Message); }
            if(signingTask==null) { worker=null;Changed?.Invoke();yield break; }
            while(!signingTask.IsCompleted)yield return null;
            byte[] envelope=null;
            try { envelope=OceanLeaderboardProtocol.Envelope(identity.PublicKeySpkiB64,payload,signingTask.GetAwaiter().GetResult()); }
            catch(Exception error) { Backoff("Device signing failed: "+error.Message); }
            signingTask=null;
            if(envelope==null) { worker=null;Changed?.Invoke();yield break; }
            Debug.Log("[OceanLeaderboard] upload_attempted events="+batch.Count);
            if(!OnlineAllowed) { worker=null;yield break; }
            request=new UnityWebRequest(baseUrl+"/v1/events","POST") { uploadHandler=new UploadHandlerRaw(envelope),downloadHandler=new DownloadHandlerBuffer() };
            request.SetRequestHeader("Content-Type","application/json; charset=utf-8");Configure(request);
            yield return request.SendWebRequest();
            string body=request.downloadHandler.text;bool ok=request.result==UnityWebRequest.Result.Success&&request.responseCode==200;
            string failure=request.error;long status=request.responseCode;request.Dispose();request=null;
            if(ok)
            {
                int before=ledger.PendingCount;
                if(ledger.ApplyAcknowledgement(body,submitted))
                {
                    ReadProfile();
                    var receipt=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(body));
                    Debug.Log("[OceanLeaderboard] ack accepted="+OceanLeaderboardJson.Array(receipt,"acceptedEventIds").Count+" duplicate="+OceanLeaderboardJson.Array(receipt,"duplicateEventIds").Count+" rejected="+OceanLeaderboardJson.Array(receipt,"rejected").Count+" pending="+ledger.PendingCount);
                    if(ledger.PendingCount<before) { lastError=null;failedAttempts=0;nextAttempt=Time.realtimeSinceStartup+.15f; }
                    else Backoff("Server retained this event batch for retry.");
                    if(ledger.RejectedScoreTotal>0)lastError="Some score events were rejected; evidence is retained locally ("+ledger.RejectedScoreTotal+").";
                }
                else Backoff(ledger.LastError);
            }
            else { Debug.LogWarning("[OceanLeaderboard] upload_deferred http="+status+" pending="+ledger.PendingCount);Backoff("Leaderboard upload deferred ("+status+"): "+failure); }
            worker=null;Changed?.Invoke();
        }
        IEnumerator ProbeDiscoveries()
        {
            yield return null;
            if(!OnlineAllowed) { worker=null;yield break; }
            if(!OnlineAllowed) { worker=null;yield break; }
            request=UnityWebRequest.Get(baseUrl+"/v1/capabilities?catalogVersion="+OceanSpeciesCatalog.CatalogVersion);Configure(request);
            yield return request.SendWebRequest();
            bool supported=false;
            serverMaximumCampaignLevel=OceanCampaignSyncPolicy.LegacyMaximumLevel;
            if(request.result==UnityWebRequest.Result.Success&&request.responseCode==200)
            {
                try {var v=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(request.downloadHandler.text));serverMaximumCampaignLevel=OceanCampaignSyncPolicy.MaximumLevel(v);supported=OceanLeaderboardJson.Number(v,"discoveries")==1&&OceanLeaderboardJson.Number(v,"catalogVersion")==OceanSpeciesCatalog.CatalogVersion&&OceanLeaderboardJson.Number(v,"speciesCount")==OceanSpeciesCatalog.Count;}catch(Exception){}
            }
            request.Dispose();request=null;discoverySupport=supported?1:-1;discoveryProbeAfter=Time.realtimeSinceStartup+300;
            discoveryError=supported?null:"图鉴已保存在本机 · 等待服务端支持后上传";worker=null;
            if(serverMaximumCampaignLevel<OceanLevelCatalog.Count&&PendingCount>0)
                lastError="新关卡成绩已保存在本机 · 等待服务端支持后上传";
            Changed?.Invoke();
        }
        IEnumerator UploadDiscoveries()
        {
            yield return null;
            if(!OnlineAllowed) { worker=null;yield break; }
            var batch=discoveries.Batch(serverMaximumCampaignLevel);if(batch.Count==0){discoveryAttemptAfter=Time.realtimeSinceStartup+30;worker=null;yield break;}
            var submitted=new HashSet<string>();foreach(object item in batch)submitted.Add(OceanLeaderboardJson.Text(OceanLeaderboardJson.Object(item),"eventId"));
            byte[] envelope=null,payload=null;
            try {payload=OceanLeaderboardProtocol.DiscoveryPayload(identity.DeviceId,batch);var signer=identity;signingTask=Task.Run(()=>signer.Sign(payload));}
            catch(Exception error){discoveryError=error.Message;}
            if(signingTask!=null)
            {
                while(!signingTask.IsCompleted)yield return null;
                try {envelope=OceanLeaderboardProtocol.Envelope(identity.PublicKeySpkiB64,payload,signingTask.GetAwaiter().GetResult());}
                catch(Exception error){discoveryError=error.Message;}
                signingTask=null;
            }
            if(envelope==null){discoveryAttemptAfter=Time.realtimeSinceStartup+30;worker=null;Changed?.Invoke();yield break;}
            if(!OnlineAllowed) { worker=null;yield break; }
            request=new UnityWebRequest(baseUrl+"/v1/discoveries","POST"){uploadHandler=new UploadHandlerRaw(envelope),downloadHandler=new DownloadHandlerBuffer()};
            request.SetRequestHeader("Content-Type","application/json; charset=utf-8");Configure(request);yield return request.SendWebRequest();
            long status=request.responseCode;string body=request.downloadHandler.text;bool ok=request.result==UnityWebRequest.Result.Success&&status==200;request.Dispose();request=null;
            if(ok&&discoveries.ApplyAcknowledgement(body,submitted))discoveryError=discoveries.RejectedCount>0?"部分发现未通过验证 · 证据保留在本机":discoveries.PendingCount==0?null:"部分图鉴记录等待重试";
            else {discoveryError=ok?discoveries.LastError:"图鉴已离线保存 · 同步暂不可用";if(status==404||status==405){discoverySupport=-1;discoveryProbeAfter=Time.realtimeSinceStartup+300;}}
            discoveryAttemptAfter=Time.realtimeSinceStartup+30;worker=null;Changed?.Invoke();
        }
        static void Configure(UnityWebRequest value)
        {
            value.timeout=15;value.redirectLimit=0;
            // Default platform certificate verification. No custom CertificateHandler, HTTP or redirect downgrade.
        }
        void Backoff(string message)
        {
            lastError=message;failedAttempts=Math.Min(9,failedAttempts+1);
            nextAttempt=Time.realtimeSinceStartup+(float)(Math.Min(300,Math.Pow(2,failedAttempts))*(.85+jitter.NextDouble()*.3));
        }
        void ReadProfile()
        {
            if(string.IsNullOrEmpty(ledger.ServerProfileJson))return;
            try { Profile=OceanLeaderboardRow.Parse(OceanLeaderboardJson.Parse(ledger.ServerProfileJson),false); }
            catch(Exception error) { lastError="Stored server profile is invalid: "+error.Message; }
        }
        void OnApplicationPause(bool paused) { if(OceanReleaseFeatures.LeaderboardsEnabled&&!paused&&initialized){nextAttempt=0;discoveryAttemptAfter=0;discoveryProbeAfter=0;} }
        void OnDestroy()
        {
            destroyed=true;if(request!=null) { request.Abort();request.Dispose();request=null; }
            if(worker!=null)StopCoroutine(worker);
            var signer=identity;identity=null;
            if(signingTask!=null&&!signingTask.IsCompleted)signingTask.ContinueWith(_=>signer?.Dispose());else signer?.Dispose();
            if(identityTask!=null)identityTask.ContinueWith(t=> { if(t.Status==TaskStatus.RanToCompletion)t.Result.Dispose();else _=t.Exception; });
        }
#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetEditorSession() { processSessionId=null; }
#endif
    }
}

using System;
using System.IO;
using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Campaign/menu integration. Opening fish remains free of fonts, Keystore and network initialization.</summary>
    public sealed class OceanLeaderboardBridge : MonoBehaviour, IOceanCampaignScoreSink
    {
        OceanLeaderboardClient client;
        OceanLeaderboardPanel panel;
        OceanCampaignScoreSync scores = new OceanCampaignScoreSync();
        Action onClose;
        string directory;
        int requestedUiVersion, requestedClientVersion;
        string requestedCursor;
        bool waitingPage, submittingNickname;
        public bool IsOpen => OceanReleaseFeatures.LeaderboardsEnabled && panel != null && panel.IsOpen;
        public event Action LocalProgressChanged;
        public bool LocalProgressAvailable => client != null;
        public long LocalShellTotal => client?.LocalShellTotal ?? 0;
        public long LocalFishTotal => client?.LocalFishTotal ?? 0;
        public long LocalCoinTotal => client?.LocalCoinTotal ?? 0;

        public void Initialize(Camera view, HandSwimController hands, string saveDirectory, Action close)
        {
            directory = Path.Combine(saveDirectory, "Leaderboard"); onClose = close;
            // Keep lifetime score/observation history available to achievements while
            // omitting the leaderboard panel, keyboard and historical nickname display.
            if(!OceanReleaseFeatures.LeaderboardsEnabled) { EnsureClient();return; }
            panel = gameObject.AddComponent<OceanLeaderboardPanel>();
            panel.Initialize(view, hands, HandleAction);
        }
        void EnsureClient()
        {
            if (client != null) return;
            client = gameObject.AddComponent<OceanLeaderboardClient>();
            client.Changed += RefreshStatus; client.PageChanged += PageReceived;
            client.Initialize(storageDirectory: directory);
        }
        public bool RecordDiscovery(int species,int level)
        {EnsureClient();return client.RecordDiscovery(species,level);}
        public bool PersistRun(OceanLevelRun run)
        { EnsureClient(); return scores.TryPersist(run, this); }
        public void Open()
        {
            if(!OceanReleaseFeatures.LeaderboardsEnabled)return;
            EnsureClient();panel.Show(client.Nickname, ShortId());
        }
        public void Close() { if (panel != null) panel.Close(); waitingPage = false; }
        string ShortId()
        {
            if(!OceanReleaseFeatures.LeaderboardsEnabled)return "";
            string id = string.IsNullOrEmpty(client.PublicId) ? client.DeviceId : client.PublicId;
            return id.Length > 12 ? id.Substring(0, 12) : id;
        }
        void HandleAction(string action, string value)
        {
            if(!OceanReleaseFeatures.LeaderboardsEnabled) { Close();return; }
            if (action == "close") { Close(); onClose?.Invoke(); return; }
            if (action == "rename")
            {
                bool saved;
                submittingNickname = true;
                try { saved = client.SetNickname(value); }
                finally { submittingNickname = false; }
                if (saved)
                { panel.SetBusy(false); RefreshStatus(); }
                else panel.SetError("昵称暂未保存，请检查输入后重试");
                return;
            }
            if (action != "load") return;
            requestedUiVersion = panel.RequestVersion; requestedCursor = value ?? "";
            waitingPage = false;
            client.RefreshPage(requestedCursor,20,panel.IsDiscovery);
            requestedClientVersion = client.PageRequestVersion;
            // RefreshPage selects the correct board cache before this substitution.
            if (client.CachedPage != null && client.CachedPageCursor == requestedCursor && client.CachedPage.IsDiscovery == panel.IsDiscovery)
                ApplyPage(client.CachedPage, !string.IsNullOrEmpty(requestedCursor), false);
            waitingPage = true;
            panel.SetBusy(true); RefreshStatus();
        }
        void PageReceived()
        {
            if (!OceanReleaseFeatures.LeaderboardsEnabled || !IsOpen || !waitingPage || requestedUiVersion != panel.RequestVersion ||
                requestedClientVersion != client.CompletedPageRequestVersion || requestedCursor != client.CachedPageCursor || client.CachedPage == null || client.CachedPage.IsDiscovery != panel.IsDiscovery) return;
            waitingPage = false;
            ApplyPage(client.CachedPage, !string.IsNullOrEmpty(requestedCursor), true);
            panel.SetProfile(client.Nickname, ShortId()); RefreshStatus();
        }
        void ApplyPage(OceanLeaderboardPage page, bool append, bool live)
        {
            if(!OceanReleaseFeatures.LeaderboardsEnabled)return;
            var rows = new OceanLeaderboardPanel.Row[page.entries.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                var r = page.entries[i];
                rows[i] = new OceanLeaderboardPanel.Row { rank = r.rank, publicId = r.publicId, nickname = r.nickname,
                    discoveryCount = r.discoveryCount, speciesCount = page.IsDiscovery ? page.SpeciesCount : OceanSpeciesCatalog.Count,
                    score = r.score, shellTotal = r.shellTotal, fishTotal = r.fishTotal, coinTotal = r.coinTotal, isMe = r.publicId == client.PublicId };
            }
            // A legacy offline page remains readable, but its signed cursor belongs
            // to that catalog. Only a refreshed current page may request more rows.
            string nextCursor = page.IsDiscovery && page.CatalogVersion != OceanSpeciesCatalog.CatalogVersion
                ? null : page.nextCursor;
            panel.SetPage(rows, nextCursor, page.totalDevices, page.totalDemoStarts, append,
                live ? "排行榜已更新" : "本地缓存 · " + page.serverTime);
        }
        void RefreshStatus()
        {
            LocalProgressChanged?.Invoke();
            if (!OceanReleaseFeatures.LeaderboardsEnabled || !IsOpen || !panel.IsReady || client == null || submittingNickname) return;
            panel.SetProfile(client.Nickname, ShortId());
            if (waitingPage && client.FinishedPageRequestVersion == requestedClientVersion && !string.IsNullOrEmpty(client.LastError))
            { waitingPage = false; panel.SetBusy(false); }
            string local = "本机 " + client.LocalScore + "分 · 贝壳 " + client.LocalShellTotal + " · 鱼 " + client.LocalFishTotal + " · 金币 " + client.LocalCoinTotal;
            if(panel.IsDiscovery)
            {
                local="我的历史发现 "+client.LocalDiscoveryCount+"类 · 当前图鉴目标 "+OceanSpeciesCatalog.AvailableCount+"类";
                if(client.PendingDiscoveryCount>0)local+=" · 待上传 "+client.PendingDiscoveryCount;
                if(client.DiscoveryStatus.Length>0)local+="\n"+client.DiscoveryStatus;
            }
            else if (client.RejectedEventCount > 0) local += " · 部分成绩待核查";
            else if (!string.IsNullOrEmpty(client.LastError)) local += " · 同步暂不可用";
            else if (client.PendingCount > 0) local += " · 待上传 " + client.PendingCount;
            else if (!client.Ready) local += " · 正在准备";
            else local += " · 已同步";
            if (!client.CachedPageIsLive && client.CachedPage != null)
                local += "\n缓存时间 " + client.CachedPage.serverTime;
            panel.SetStatus(local);
            panel.SetNicknameResult(client.Nickname, client.NicknameDraft, client.NicknameStatus, client.NicknameError);
        }
        public bool EnsureSession() => client.EnsureSession();
        public bool BeginRun(string id, int level) => client.BeginRun(id, level);
        public bool RecordShell(string id, int species, int pickup) => client.RecordShell(id, species, pickup);
        public bool RecordFish(string id, int species, int pickup) => client.RecordFish(id, species, pickup);
        public bool RecordCoin(string id, int pickup) => client.RecordCoin(id, pickup);
        public bool RecordShark(string id, int pickup) => client.RecordShark(id, pickup);
        void OnDestroy()
        {
            if (client != null) { client.Changed -= RefreshStatus; client.PageChanged -= PageReceived; }
        }
    }
}

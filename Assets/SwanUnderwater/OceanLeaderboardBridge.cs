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
        public bool IsOpen => panel != null && panel.IsOpen;
        public event Action LocalProgressChanged;
        public bool LocalProgressAvailable => client != null;
        public long LocalShellTotal => client?.LocalShellTotal ?? 0;
        public long LocalFishTotal => client?.LocalFishTotal ?? 0;
        public long LocalCoinTotal => client?.LocalCoinTotal ?? 0;

        public void Initialize(Camera view, HandSwimController hands, string saveDirectory, Action close)
        {
            directory = Path.Combine(saveDirectory, "Leaderboard"); onClose = close;
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
        { EnsureClient(); panel.Show(client.Nickname, ShortId()); }
        public void Close() { if (panel != null) panel.Close(); waitingPage = false; }
        string ShortId()
        {
            string id = string.IsNullOrEmpty(client.PublicId) ? client.DeviceId : client.PublicId;
            return id.Length > 12 ? id.Substring(0, 12) : id;
        }
        void HandleAction(string action, string value)
        {
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
            if (client.OfflineOnly)
            { waitingPage = false; panel.SetBusy(false); RefreshStatus(); return; }
            waitingPage = true;
            panel.SetBusy(true); RefreshStatus();
        }
        void PageReceived()
        {
            if (!IsOpen || !waitingPage || requestedUiVersion != panel.RequestVersion ||
                requestedClientVersion != client.CompletedPageRequestVersion || requestedCursor != client.CachedPageCursor || client.CachedPage == null || client.CachedPage.IsDiscovery != panel.IsDiscovery) return;
            waitingPage = false;
            ApplyPage(client.CachedPage, !string.IsNullOrEmpty(requestedCursor), true);
            panel.SetProfile(client.Nickname, ShortId()); RefreshStatus();
        }
        void ApplyPage(OceanLeaderboardPage page, bool append, bool live)
        {
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
            string nextCursor = client.OfflineOnly || page.IsDiscovery && page.CatalogVersion != OceanSpeciesCatalog.CatalogVersion
                ? null : page.nextCursor;
            panel.SetPage(rows, nextCursor, page.totalDevices, page.totalDemoStarts, append,
                client.OfflineOnly ? OceanLeaderboardClient.OfflineStatus : live ? "排行榜已更新" : "本地缓存 · " + page.serverTime);
        }
        void RefreshStatus()
        {
            LocalProgressChanged?.Invoke();
            if (!IsOpen || !panel.IsReady || client == null || submittingNickname) return;
            panel.SetProfile(client.Nickname, ShortId());
            if (waitingPage && client.FinishedPageRequestVersion == requestedClientVersion && !string.IsNullOrEmpty(client.LastError))
            { waitingPage = false; panel.SetBusy(false); }
            string local = "本机 " + client.LocalScore + "分 · 贝壳 " + client.LocalShellTotal + " · 鱼 " + client.LocalFishTotal + " · 金币 " + client.LocalCoinTotal;
            if (panel.IsDiscovery) local = "我的历史发现 " + client.LocalDiscoveryCount + "类 · 当前图鉴目标 " + OceanSpeciesCatalog.AvailableCount + "类";
            local += "\n" + OceanLeaderboardClient.OfflineStatus;
            if (!string.IsNullOrEmpty(client.LastError)) local += " · 本地保存需检查";
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

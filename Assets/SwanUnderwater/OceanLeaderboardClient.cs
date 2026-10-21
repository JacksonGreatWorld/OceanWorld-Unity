using System;
using System.IO;
using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Public offline edition. Durable ledgers remain authoritative; no transport, endpoint, signing task or background request exists.</summary>
    public sealed class OceanLeaderboardClient : MonoBehaviour
    {
        public const string OfflineStatus = "离线版 · 仅保存在本机，不连接任何服务器";
        static string processSessionId;
        OceanLeaderboardLedger ledger;
        OceanDiscoveryLedger discoveries;
        bool initialized;
        string lastError;
        public event Action Changed;
        public event Action PageChanged;
        public bool OfflineOnly => true;
        public string DeviceId => ledger?.DeviceId ?? "";
        public string PublicId => DeviceId.Length >= 12 ? DeviceId.Substring(0, 12) : "";
        public string NicknameDraft => ledger?.NicknameDraft ?? "";
        public string Nickname => !string.IsNullOrEmpty(NicknameDraft) ? NicknameDraft :
            !string.IsNullOrEmpty(ledger?.Nickname) ? ledger.Nickname : DeviceId.Length >= 6 ? "设备-" + DeviceId.Substring(0, 6) : "设备-本机";
        public string NicknameStatus => string.IsNullOrEmpty(NicknameDraft) ? "idle" : "local";
        public string NicknameError => ledger?.NicknameError ?? "";
        public int PendingCount => ledger?.PendingCount ?? 0;
        public int LocalDiscoveryCount => discoveries?.Count ?? 0;
        public int PendingDiscoveryCount => discoveries?.PendingCount ?? 0;
        public string DiscoveryStatus => discoveries?.LastError ?? OfflineStatus;
        public long LocalShellTotal => ledger?.ShellTotal ?? 0;
        public long LocalFishTotal => ledger?.FishTotal ?? 0;
        public long LocalCoinTotal => ledger?.CoinTotal ?? 0;
        public long LocalScore => ledger?.Score ?? 0;
        public long LocalDemoStarts => ledger?.DemoTotal ?? 0;
        public long RejectedEventCount => ledger?.RejectedScoreTotal ?? 0;
        public bool IsBusy => false;
        public bool Ready => initialized && ledger.Writable;
        public string LastError => lastError;
        public OceanLeaderboardPage CachedPage { get; private set; }
        public OceanLeaderboardRow Profile { get; private set; }
        public string CachedPageCursor => "";
        public int PageRequestVersion { get; private set; }
        public int CompletedPageRequestVersion { get; private set; }
        public int FinishedPageRequestVersion { get; private set; }
        public bool CachedPageIsLive => false;
        public string SessionId => processSessionId ?? "";

        public void Initialize(string url = null, string storageDirectory = null)
        {
            // A caller cannot accidentally re-enable the private service by supplying a URL.
            if (!string.IsNullOrEmpty(url)) throw new ArgumentException("This public edition is offline and accepts no server URL.", nameof(url));
            if (initialized) return;
            string directory = storageDirectory ?? Path.Combine(Application.persistentDataPath, "Leaderboard");
#if UNITY_EDITOR
            directory = Path.Combine(directory, "editor-test");
#endif
            ledger = new OceanLeaderboardLedger(directory);
            discoveries = new OceanDiscoveryLedger(directory);
            initialized = true;
            lastError = ledger.LastError;
            if (ledger.Writable)
            {
                // An installation-local random identifier, never a hardware serial or account ID.
                string identity = string.IsNullOrEmpty(ledger.DeviceId) ? OceanLeaderboardLedger.Hash(Guid.NewGuid().ToString("N")) : ledger.DeviceId;
                if (!ledger.BindIdentity(identity)) lastError = ledger.LastError;
                if (!discoveries.BindIdentity(identity) && lastError == null) lastError = discoveries.LastError;
            }
            Changed?.Invoke();
        }
        public bool EnsureSession()
        {
            if (!initialized) return NotInitialized();
            if (processSessionId == null) processSessionId = Guid.NewGuid().ToString("N");
            return Persist(() => ledger.EnsureSession(processSessionId));
        }
        public bool BeginRun(string runId, int level) => EnsureSession() && Persist(() => ledger.BeginRun(processSessionId, runId, level));
        public bool RecordShell(string runId, int species, int pickup) => EnsureSession() && Persist(() => ledger.RecordShell(processSessionId, runId, species, pickup));
        public bool RecordFish(string runId, int species, int pickup) => EnsureSession() && Persist(() => ledger.RecordFish(processSessionId, runId, species, pickup));
        public bool RecordShark(string runId, int pickup) => EnsureSession() && Persist(() => ledger.RecordShark(processSessionId, runId, pickup));
        public bool RecordCoin(string runId, int pickup) => EnsureSession() && Persist(() => ledger.RecordCoin(processSessionId, runId, pickup));
        public bool RecordDiscovery(int species, int level)
        {
            if (!EnsureSession()) return false;
            bool saved = discoveries.Record(processSessionId, species, level);
            if (!saved) lastError = discoveries.LastError;
            Changed?.Invoke(); return saved;
        }
        public bool SetNickname(string value) => initialized ? Persist(() => ledger.SetNickname(value)) : NotInitialized();
        bool NotInitialized() { lastError = "Local leaderboard has not been initialized."; return false; }
        bool Persist(Func<bool> operation)
        {
            bool saved = operation(); lastError = saved ? null : ledger.LastError;
            Changed?.Invoke(); return saved;
        }
        public void RefreshPage(string cursor = "", int limit = 20, bool discovery = false)
        {
            if (!initialized) { NotInitialized(); return; }
            PageRequestVersion++;
            if (cursor == null || cursor.Length > 4096 || limit < 1 || limit > 100)
            { lastError = "Invalid local leaderboard page request."; FinishedPageRequestVersion = PageRequestVersion; Changed?.Invoke(); return; }
            Profile = new OceanLeaderboardRow { rank = 1, publicId = PublicId, nickname = Nickname,
                shellTotal = LocalShellTotal, fishTotal = LocalFishTotal, coinTotal = LocalCoinTotal,
                score = LocalScore, demoStarts = LocalDemoStarts, discoveryCount = LocalDiscoveryCount };
            CachedPage = OceanLeaderboardPage.CreateLocal(Profile, discovery);
            CompletedPageRequestVersion = FinishedPageRequestVersion = PageRequestVersion;
            Changed?.Invoke(); PageChanged?.Invoke();
        }
#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetEditorSession() { processSessionId = null; }
#endif
    }
}

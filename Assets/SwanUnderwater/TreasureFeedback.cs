using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SwanUnderwater
{
    /// <summary>Presentation only: the level owns pickup validity, persistence and rewards.</summary>
    [DisallowMultipleComponent]
    public sealed class TreasureFeedback : MonoBehaviour
    {
        public const string LevelCompleteText = "恭喜你，集齐5种贝壳，第一关通关！";
        public const string HiddenRewardText = "恭喜你，达成隐藏任务！";
        public const string GrabHintText = "靠近贝壳，握拳收集";
        public const int CoinCapacity = 256;
        public const float CelebrationDuration = 6.2f;
        const int BurstCoins = 84, SparkCapacity = 40;
        static readonly string[] Names = { "扇贝", "海螺", "宝螺", "蛤蜊", "锥螺" };
        public int CollectedMask { get; private set; }
        public int DistinctCount { get; private set; }
        public int ActiveCelebrationCoins { get; private set; }
        public bool IsCelebrating => celebrationActive;
        public string CurrentNotification { get; private set; } = "";
        public bool CanShowExplorationMessage => initialized && !released && isActiveAndEnabled && visible && !IsCelebrating && toastDuration <= 0f && queueCount == 0;
        public Canvas Canvas => hudCanvas;

        Camera view;
        GameObject hudRoot, toastRoot, audioRoot;
        Canvas hudCanvas, toastCanvas;
        CanvasGroup toastAlpha;
        RectTransform toastRect;
        Text countLabel, toastLabel, jackpotHeading;
        readonly Text[] nameLabels = new Text[5];
        readonly Image[] markers = new Image[5];
        TreasureTextGradient toastGradient;
        Font font;
        Material uiMaterial, panelMaterial, coinMaterial, sparkMaterial;
        Texture2D panelTexture;
        Sprite panelSprite;
        Mesh coinMesh, sparkMesh;
        MaterialPropertyBlock sparkProperties;
        readonly Matrix4x4[] sparkMatrices = new Matrix4x4[SparkCapacity];
        readonly Vector4[] sparkColors = new Vector4[SparkCapacity];
        readonly float[] sparkKinds = new float[SparkCapacity];
        AudioSource pickupAudio, rewardAudio, musicAudio;
        AudioClip collectClip, duplicateClip, completeClip, coinsClip;
        bool journalVisible = true;
        bool initialized, visible = true, released, coinReference, celebrationActive;
        double toastStarted, celebrationStarted;
        float toastDuration;
        readonly Notification[] notifications = new Notification[12];
        int queueStart, queueCount;
        readonly Coin[] coins = new Coin[CoinCapacity];
        readonly Matrix4x4[] coinMatrices = new Matrix4x4[CoinCapacity];
        readonly float[] coinDepths = new float[CoinCapacity];
        readonly System.Random random = new System.Random(2046);
        bool explorationToast;
        struct Notification { public string text; public Func<string> presentation; public float duration; public bool gold, completeSound, exploration; }
        Notification activeNotification;
        struct Coin { public Vector3 origin, velocity, spin; public Quaternion rotation; public float delay, life, size; public bool rain; }

        public void Initialize(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (released) throw new InvalidOperationException("Treasure feedback was disposed.");
            view = camera;
            if (!initialized)
            {
                font = Resources.Load<Font>("Treasure/TreasureFont");
                Shader shader = Resources.Load<Shader>("PerformanceHudUI");
                if (font == null || shader == null) throw new InvalidOperationException("Missing Treasure/TreasureFont or PerformanceHudUI.");
                uiMaterial = new Material(shader) { name = "Treasure feedback UI", renderQueue = 4090 };
                BuildPanelResources(); BuildHud(); BuildToast(); BuildAudio();
                CollectibleVisuals.AcquireCoinResources(out coinMesh, out Material sharedCoinMaterial);
                coinReference = true;
                if (coinMesh == null || sharedCoinMaterial == null || !sharedCoinMaterial.enableInstancing)
                    throw new InvalidOperationException("Treasure celebration requires the shared instanced coin material.");
                Shader goldShader = Resources.Load<Shader>("Shaders/TreasureJackpotGold");
                Shader sparkShader = Resources.Load<Shader>("Shaders/TreasureJackpotSpark");
                if (goldShader == null || sparkShader == null) throw new InvalidOperationException("Missing jackpot presentation shaders.");
                coinMaterial = new Material(goldShader) { name = "Bright jackpot gold", renderQueue = 4050, enableInstancing = true };
                sparkMaterial = new Material(sparkShader) { name = "Jackpot glints", renderQueue = 4045, enableInstancing = true };
                sparkProperties = new MaterialPropertyBlock();
                sparkMesh = new Mesh { name = "Jackpot glint quad" };
                sparkMesh.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(.5f,.5f,0), new Vector3(-.5f,.5f,0) };
                sparkMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                sparkMesh.triangles = new[] { 0,1,2,0,2,3 }; sparkMesh.RecalculateBounds(); sparkMesh.UploadMeshData(true);
                string warm = "已收集012345/" + string.Join("", Names) + LevelCompleteText + HiddenRewardText +
                    "你收集到1种贝壳：该贝壳你已经拥有（1/5）" + GrabHintText;
                foreach (int size in new[] { 24, 32, 42 }) font.RequestCharactersInTexture(warm, size, FontStyle.Normal);
                initialized = true;
                OceanLanguage.Changed += RefreshLanguage;
                RefreshProgress();
                Debug.Log($"[TreasureFeedback] ready font={font.name}, shells=5, pooledCoins={CoinCapacity}, coinSubmeshes={coinMesh.subMeshCount}");
            }
            AttachCamera();
            hudRoot.SetActive(visible && journalVisible);
        }

        void AttachCamera()
        {
            hudRoot.transform.SetParent(view.transform, false);
            hudRoot.transform.localPosition = new Vector3(-.50f, .39f, 1.7f);
            hudRoot.transform.localScale = Vector3.one * .00165f;
            hudCanvas.worldCamera = view;
            toastRoot.transform.SetParent(view.transform, false);
            toastRoot.transform.localPosition = new Vector3(0f, -.13f, 2.1f);
            toastRoot.transform.localScale = Vector3.one * .002f;
            toastCanvas.worldCamera = view;
            audioRoot.transform.SetParent(view.transform, false);
        }

        GameObject MakeCanvas(string name, Vector2 size, out Canvas canvas)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            root.hideFlags = HideFlags.DontSave;
            ((RectTransform)root.transform).sizeDelta = size;
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32750;
            return root;
        }

        void BuildPanelResources()
        {
            // Image owns explicit sprite UVs/texture; text keeps its independently rebuilt font atlas.
            panelMaterial = new Material(uiMaterial) { name = "Treasure panel UI" };
            const int extent = 64;
            panelTexture = new Texture2D(extent, extent, TextureFormat.RGBA32, false)
            { name = "Treasure rounded panel", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[extent * extent];
            for (int y = 0; y < extent; y++)
                for (int x = 0; x < extent; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Abs(x - 31.5f) - 18f);
                    float dy = Mathf.Max(0f, Mathf.Abs(y - 31.5f) - 18f);
                    float edge = Mathf.Clamp01(13f - Mathf.Sqrt(dx * dx + dy * dy));
                    Color tint = Color.Lerp(new Color(.014f, .042f, .062f, .86f),
                        new Color(.037f, .098f, .116f, .84f), y / 63f);
                    tint.a *= edge; pixels[y * extent + x] = tint;
                }
            panelTexture.SetPixels32(pixels); panelTexture.Apply(false, true);
            panelSprite = Sprite.Create(panelTexture, new Rect(0f, 0f, extent, extent),
                new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16f, 16f, 16f, 16f));
            panelSprite.name = "Treasure rounded panel";
        }

        void AddPanel(GameObject target)
        {
            Image panel = target.AddComponent<Image>();
            panel.sprite = panelSprite; panel.type = Image.Type.Sliced;
            panel.material = panelMaterial; panel.color = Color.white; panel.raycastTarget = false;
        }

        void BuildHud()
        {
            hudRoot = MakeCanvas("Shell collection journal", new Vector2(420f, 208f), out hudCanvas);
            AddPanel(hudRoot);
            // Keep all white-texture markers together, then all atlas text together for UI batching.
            for (int i = 0; i < 5; i++)
            {
                Vector2 point = ChipPosition(i);
                var marker = new GameObject("Shell status " + i, typeof(RectTransform), typeof(Image));
                marker.transform.SetParent(hudRoot.transform, false);
                var rect = (RectTransform)marker.transform;
                rect.anchoredPosition = point + new Vector2(-42f, 0f);
                rect.sizeDelta = new Vector2(8f, 8f);
                rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                markers[i] = marker.GetComponent<Image>();
                markers[i].material = uiMaterial; markers[i].raycastTarget = false;
            }
            countLabel = MakeText(hudRoot.transform, "Collected count", 32, new Vector2(0f, 68f), new Vector2(380f, 42f));
            countLabel.alignment = TextAnchor.MiddleLeft;
            for (int i = 0; i < 5; i++)
            {
                nameLabels[i] = MakeText(hudRoot.transform, "Shell name " + i, 24, ChipPosition(i), new Vector2(88f, 38f));
                OceanLanguage.Bind(nameLabels[i], Names[i]);
            }
            Text grabHint = MakeText(hudRoot.transform, "How to collect", 24, new Vector2(0f, -74f), new Vector2(384f, 30f));
            OceanLanguage.Bind(grabHint, GrabHintText);
            grabHint.color = new Color(.64f, .83f, .86f);
        }

        static Vector2 ChipPosition(int kind) => new Vector2(-118f + (kind % 3) * 128f, kind < 3 ? 20f : -23f);

        void BuildToast()
        {
            toastRoot = MakeCanvas("Treasure moment", new Vector2(1050f, 140f), out toastCanvas);
            toastAlpha = toastRoot.AddComponent<CanvasGroup>();
            toastAlpha.alpha = 0f; toastAlpha.blocksRaycasts = toastAlpha.interactable = false;
            var body = new GameObject("Rising message", typeof(RectTransform));
            body.transform.SetParent(toastRoot.transform, false);
            toastRect = (RectTransform)body.transform;
            toastRect.sizeDelta = new Vector2(1000f, 120f);
            AddPanel(body);
            toastLabel = MakeText(body.transform, "Collection notification", 32, Vector2.zero, new Vector2(950f, 102f));
            toastGradient = toastLabel.gameObject.AddComponent<TreasureTextGradient>();
            jackpotHeading = MakeText(body.transform, "Jackpot title", 78, new Vector2(0f, 112f), new Vector2(930f, 116f));
            Font titleFont = Resources.Load<Font>("Loading/OceanTitleFont");
            if (titleFont != null) jackpotHeading.font = titleFont;
            jackpotHeading.font.RequestCharactersInTexture("宝藏大奖", 78, FontStyle.Normal);
            OceanLanguage.Bind(jackpotHeading, "宝藏大奖");
            jackpotHeading.gameObject.AddComponent<TreasureTextGradient>().Gold = true;
            jackpotHeading.gameObject.SetActive(false);
        }

        Text MakeText(Transform parent, string name, int size, Vector2 position, Vector2 dimensions)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
            root.transform.SetParent(parent, false);
            var text = root.GetComponent<Text>();
            text.rectTransform.anchoredPosition = position; text.rectTransform.sizeDelta = dimensions;
            text.font = font; text.fontSize = size; text.material = uiMaterial;
            text.alignment = TextAnchor.MiddleCenter; text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false; text.raycastTarget = false;
            var shadow = root.GetComponent<Shadow>();
            shadow.effectColor = new Color(.002f, .018f, .025f, .80f); shadow.effectDistance = new Vector2(1f, -1f);
            return text;
        }

        /// <summary>Restore presentation from the level's persisted five-bit mask, without pickup sounds or rewards.</summary>
        public void SetProgress(int collectedMask)
        {
            CollectedMask = collectedMask & 31;
            int count = 0; for (int i = 0; i < 5; i++) if ((CollectedMask & (1 << i)) != 0) count++;
            DistinctCount = count;
            if (initialized) RefreshProgress();
        }

        void RefreshProgress()
        {
            countLabel.text = OceanLanguage.Format("已收集 {0}/5", DistinctCount);
            for (int i = 0; i < 5; i++)
            {
                bool collected = (CollectedMask & (1 << i)) != 0;
                markers[i].color = collected ? new Color(.97f, .79f, .34f, 1f) : new Color(.24f, .42f, .47f, .7f);
                nameLabels[i].color = collected ? new Color(.91f, .97f, .97f) : new Color(.47f, .65f, .69f);
            }
        }

        public void ShowCollection(int kind, string name, int distinctCount, bool duplicate)
        {
            if (!initialized || released) return;
            if (!duplicate && kind >= 0 && kind < 5) CollectedMask |= 1 << kind;
            DistinctCount = Mathf.Clamp(distinctCount, 0, 5);
            RefreshProgress();
            if (!visible) return;
            // Audio starts in this pickup call; it never waits behind a message animation.
            pickupAudio.PlayOneShot(duplicate ? duplicateClip : collectClip, duplicate ? .45f : .8f);
            if (string.IsNullOrEmpty(name)) name = Names[Mathf.Clamp(kind, 0, 4)];
            string message = duplicate ? "该贝壳你已经拥有（" + DistinctCount + "/5）"
                : "你收集到1种贝壳：" + name + "（" + DistinctCount + "/5）";
            int snapshotCount = DistinctCount;
            Enqueue(new Notification { text = message, duration = duplicate ? 1.8f : 2.8f,
                presentation = () => duplicate ? OceanLanguage.Format("该贝壳你已经拥有（{0}/5）", snapshotCount) :
                    OceanLanguage.Format("你收集到1种贝壳：{0}（{1}/5）", OceanLanguage.Text(name), snapshotCount) });
        }

        public void SetJournalVisible(bool value)
        {
            journalVisible = value;
            if (hudRoot != null) hudRoot.SetActive(visible && value);
        }

        public void ShowCampaignMessage(string message, bool gold = false, bool pickup = false, bool duplicate = false, bool priority = false, bool fanfare = true)
            => ShowLocalizedCampaignMessage(message, null, gold, pickup, duplicate, priority, fanfare);

        /// <summary>The raw message retains gameplay identity; the factory only renders the
        /// captured event values in the selected language, including while this toast is queued.</summary>
        public void ShowLocalizedCampaignMessage(string message, Func<string> presentation, bool gold = false, bool pickup = false, bool duplicate = false, bool priority = false, bool fanfare = true)
        {
            if (!initialized || released || !visible) return;
            if (pickup) pickupAudio.PlayOneShot(duplicate ? duplicateClip : collectClip, duplicate ? .45f : .8f);
            var notification = new Notification { text = message, presentation = presentation, duration = gold ? 3.8f : 2.6f, gold = gold, completeSound = gold && fanfare };
            if (priority && !IsCelebrating) { queueStart = queueCount = 0; StartToast(notification); }
            else Enqueue(notification);
        }

        /// <summary>Optional area captions never queue and yield immediately to collection/campaign feedback.</summary>
        public bool TryShowExplorationMessage(string message)
            => TryShowLocalizedExplorationMessage(message, null);

        public bool TryShowLocalizedExplorationMessage(string message, Func<string> presentation)
        {
            if (!CanShowExplorationMessage || string.IsNullOrWhiteSpace(message)) return false;
            StartToast(new Notification { text = message, presentation = presentation, duration = 2.6f, exploration = true });
            return true;
        }

        /// <summary>Achievement notices yield to pickups, the coin celebration and campaign results.</summary>
        public bool TryShowAchievementMessage(string message)
            => TryShowLocalizedAchievementMessage(message, null);

        public bool TryShowLocalizedAchievementMessage(string message, Func<string> presentation)
        {
            if (!CanShowExplorationMessage || string.IsNullOrWhiteSpace(message)) return false;
            StartToast(new Notification { text = message, presentation = presentation, duration = 3.2f, gold = true, completeSound = true, exploration = true });
            return true;
        }

        public void PlayEnding()
        {
            if (!initialized || released) return;
            // A dedicated final arpeggio resolves the campaign instead of reusing the coin shower.
            if (endingClip == null) endingClip = BuildEndingClip();
            musicAudio.PlayOneShot(endingClip, .75f);
        }
        AudioClip endingClip;
        static AudioClip BuildEndingClip()
        {
            const int rate = 24000; float[] samples = new float[rate * 5];
            double[] notes = { 261.63, 329.63, 392.0, 523.25, 659.25, 783.99, 1046.5 };
            for (int n = 0; n < notes.Length; n++)
                for (int i = 0; i < rate * 2; i++)
                {
                    int at = (int)(n * .38 * rate) + i;
                    if (at >= samples.Length) break;
                    double t = i / (double)rate;
                    double envelope = (1 - Math.Exp(-t * 90)) * Math.Exp(-t * 2.4);
                    samples[at] += (float)(.20 * envelope * (Math.Sin(t * notes[n] * Math.PI * 2) + .15 * Math.Sin(t * notes[n] * Math.PI * 4)));
                }
            var clip = AudioClip.Create("Ocean journey finale", samples.Length, 1, rate, false);
            clip.SetData(samples, 0); return clip;
        }

        public void ShowLevelComplete()
        {
            if (!initialized || released) return;
            SetProgress(31);
            if (visible) Enqueue(new Notification { text = LevelCompleteText, duration = 4.2f, gold = true, completeSound = true });
        }

        public void ShowHiddenReward() => ShowHiddenReward(1, 1);

        public void ShowHiddenReward(int collected, int target)
        {
            if (!initialized || released || !visible) return;
            string rewardText = collected >= target ? HiddenRewardText : $"金币 {collected}/{target} · 继续探索，寻找剩余金币";
            rewardAudio.PlayOneShot(coinsClip, .8f);
            BeginCoins();
            // The jackpot starts with its burst; do not leave its title waiting behind a shell toast.
            if (toastDuration > 0f && !explorationToast && CurrentNotification != HiddenRewardText)
            {
                var previous = activeNotification;
                previous.duration = Mathf.Max(1.5f, toastDuration - (float)(Time.unscaledTimeAsDouble - toastStarted));
                previous.gold = CurrentNotification == LevelCompleteText;
                previous.completeSound = false;
                Enqueue(previous);
            }
            StartToast(new Notification { text = rewardText, duration = CelebrationDuration, gold = true,
                presentation = () => collected >= target ? OceanLanguage.Text(HiddenRewardText) :
                    OceanLanguage.Format("金币 {0}/{1} · 继续探索，寻找剩余金币", collected, target) });
        }

        void Enqueue(Notification notification)
        {
            if (explorationToast && !notification.exploration) { StartToast(notification); return; }
            if (toastDuration <= 0f) { StartToast(notification); return; }
            // Bounded even if callers deliver many duplicate-grab notifications in one frame.
            if (queueCount == notifications.Length) { queueStart = (queueStart + 1) % notifications.Length; queueCount--; }
            notifications[(queueStart + queueCount++) % notifications.Length] = notification;
        }

        void StartToast(Notification notification)
        {
            activeNotification = notification;
            explorationToast = notification.exploration;
            CurrentNotification = notification.text;
            toastLabel.fontSize = notification.gold ? 42 : 32;
            RefreshToastLanguage();
            jackpotHeading.gameObject.SetActive(notification.text == HiddenRewardText);
            toastGradient.Gold = notification.gold;
            toastStarted = Time.unscaledTimeAsDouble; toastDuration = notification.duration;
            toastAlpha.alpha = 0f;
            if (notification.completeSound) rewardAudio.PlayOneShot(completeClip, .75f);
        }

        void RefreshToastLanguage()
        {
            toastLabel.text = activeNotification.presentation != null ? activeNotification.presentation() : OceanLanguage.Text(activeNotification.text);
            float width = Mathf.Max(1f, toastLabel.preferredWidth);
            toastLabel.rectTransform.localScale = Vector3.one * Mathf.Min(1f, 930f / width);
        }

        void RefreshLanguage()
        {
            if (!initialized || released) return;
            RefreshProgress();
            // Keep the original clock, queue, sound and reward state. A locale change
            // changes glyphs and fitting only, never starts a second notification.
            if (toastDuration > 0f) RefreshToastLanguage();
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if (!initialized || released) return;
            hudRoot.SetActive(value && journalVisible);
            if (!value) CancelTransient();
        }

        void Update()
        {
            if (!initialized || released || !visible || view == null) return;
            if (toastDuration > 0f)
            {
                float elapsed = (float)(Time.unscaledTimeAsDouble - toastStarted);
                toastAlpha.alpha = Smooth(elapsed / .20f) * Smooth((toastDuration - elapsed) / .45f);
                toastRect.anchoredPosition = new Vector2(0f, -12f * (1f - Smooth(elapsed / .30f)));
                if (jackpotHeading.gameObject.activeSelf)
                    jackpotHeading.rectTransform.localScale = Vector3.one * (1f + .13f * Mathf.Sin(Mathf.PI * Smooth(elapsed / .5f)) * (1f - Smooth(elapsed / .65f)));
                if (elapsed >= toastDuration)
                {
                    toastDuration = 0f; CurrentNotification = ""; explorationToast = false;
                    if (queueCount > 0)
                    {
                        Notification next = notifications[queueStart]; notifications[queueStart] = default;
                        queueStart = (queueStart + 1) % notifications.Length; queueCount--; StartToast(next);
                    }
                }
            }
        }

        void LateUpdate()
        {
            if (initialized && !released && visible && view != null && celebrationActive) DrawCoins();
        }

        void BeginCoins()
        {
            celebrationStarted = Time.unscaledTimeAsDouble; celebrationActive = true;
            // Positions are in normalized view space until draw time, keeping the shower in both eyes
            // as the wearer looks up after grabbing. No XR camera or tracking transform is changed.
            for (int i = 0; i < coins.Length; i++)
            {
                bool rain = i >= BurstCoins;
                float angle = Range(0, Mathf.PI * 2f);
                coins[i] = new Coin
                {
                    origin = rain ? new Vector3(Range(-1.22f, 1.22f), Range(1.18f, 1.45f), Range(.90f, 1.75f))
                        : new Vector3(Range(-.11f,.11f), Range(-.16f,.08f), Range(1.5f, 1.9f)),
                    velocity = rain ? new Vector3(Range(-.15f,.15f), Range(-1.18f,-.90f), 0)
                        : new Vector3(Mathf.Cos(angle)*Range(.8f,1.35f), Mathf.Sin(angle)*Range(.75f,1.25f)+.3f, Range(-.9f,-.55f)),
                    rotation = Quaternion.Euler(-75f + Range(-18,18), Range(-20,20), Range(0,360)),
                    spin = new Vector3(Range(-210,210), Range(-240,240), Range(-140,140)),
                    delay = rain ? Range(.18f,3.25f) : (i % 3) * .25f + Range(0,.08f),
                    life = rain ? Range(2.45f,2.95f) : Range(2.2f,2.6f),
                    size = i % 11 == 0 ? Range(1.5f,1.8f) : Range(.72f,1.18f), rain = rain
                };
            }
            Debug.Log($"[TreasureJackpot] begin capacity={CoinCapacity} duration={CelebrationDuration:F1}s viewRelative=true nearestDepth=0.85m");
        }

        void ViewExtents(out float width, out float height)
        {
            Matrix4x4 projection = view.stereoEnabled ? view.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left) : view.projectionMatrix;
            width = Mathf.Clamp(1f / Mathf.Max(.01f, Mathf.Abs(projection.m00)), .55f, 1.65f);
            height = Mathf.Clamp(1f / Mathf.Max(.01f, Mathf.Abs(projection.m11)), .55f, 1.45f);
        }

        void DrawCoins()
        {
            float elapsed = (float)(Time.unscaledTimeAsDouble - celebrationStarted);
            if (elapsed >= CelebrationDuration) { celebrationActive = false; ActiveCelebrationCoins = 0; return; }
            ViewExtents(out float halfWidth, out float halfHeight);
            Vector3 eye = view.transform.position; Quaternion basis = view.transform.rotation;
            int count = 0;
            for (int i = 0; i < coins.Length; i++)
            {
                Coin coin = coins[i]; float age = elapsed - coin.delay;
                if (age < 0f || age >= coin.life) continue;
                Vector3 point = coin.origin + coin.velocity * age;
                if (coin.rain) point.x += Mathf.Sin(age * 2.1f + i) * .075f;
                else point.y -= .36f * age * age;
                // Do not let any coin cross the near plane or pass through the wearer.
                float depth = Mathf.Clamp(point.z, .85f, 1.9f);
                if (Mathf.Abs(point.x) > 1.48f || Mathf.Abs(point.y) > 1.60f) continue;
                point = eye + basis * new Vector3(point.x * halfWidth * depth, point.y * halfHeight * depth, depth);
                float size = coin.size * Smooth(age / .10f) * Smooth((coin.life - age) / .38f);
                Matrix4x4 matrix = Matrix4x4.TRS(point, basis * coin.rotation * Quaternion.Euler(coin.spin * age), Vector3.one * size);
                int slot = count;
                while (slot > 0 && coinDepths[slot - 1] < depth)
                {
                    coinDepths[slot] = coinDepths[slot - 1]; coinMatrices[slot] = coinMatrices[slot - 1]; slot--;
                }
                coinDepths[slot] = depth; coinMatrices[slot] = matrix; count++;
            }
            ActiveCelebrationCoins = count;
            DrawSparkles(elapsed, eye, basis, halfWidth, halfHeight);
            if (count > 0)
                Graphics.DrawMeshInstanced(coinMesh, 0, coinMaterial, coinMatrices, count, null,
                    ShadowCastingMode.Off, false, 0, view, LightProbeUsage.Off);
        }

        void DrawSparkles(float elapsed, Vector3 eye, Quaternion basis, float halfWidth, float halfHeight)
        {
            float tail = Smooth((CelebrationDuration - elapsed) / .8f);
            int count = 0;
            for (int i = 0; i < SparkCapacity; i++)
            {
                bool halo = i < 3;
                float waveAge = elapsed - i * .25f;
                float alpha = halo ? Mathf.Clamp01(1f-waveAge/.75f)*Smooth(waveAge/.07f)*.34f :
                    Mathf.Pow(Mathf.Max(0,Mathf.Sin(elapsed*3.5f+i*2.37f)),8)*.85f*Smooth(elapsed/.15f)*tail;
                if (alpha < .005f) continue;
                float x = halo ? 0 : Mathf.Sin(i*17.13f)*1.08f;
                float y = halo ? 0 : Mathf.Cos(i*9.27f)*.94f;
                float depth = 1.55f;
                Vector3 point = eye + basis * new Vector3(x*halfWidth*depth,y*halfHeight*depth,depth);
                float size = halo ? (1.1f+Mathf.Max(0,waveAge)*2.1f)*Mathf.Max(halfWidth,halfHeight) : .12f+(i%4)*.035f;
                sparkMatrices[count] = Matrix4x4.TRS(point,basis*Quaternion.Euler(0,0,i*37+elapsed*18),Vector3.one*size);
                sparkColors[count] = new Vector4(1f,halo ? .57f : .83f,halo ? .10f : .38f,alpha);
                sparkKinds[count++] = halo ? 1 : 0;
            }
            if (count == 0) return;
            sparkProperties.SetVectorArray("_SparkColor",sparkColors); sparkProperties.SetFloatArray("_SparkKind",sparkKinds);
            Graphics.DrawMeshInstanced(sparkMesh,0,sparkMaterial,sparkMatrices,count,sparkProperties,
                ShadowCastingMode.Off,false,0,view,LightProbeUsage.Off);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Call immediately before an Editor SubmitRenderRequest: transient Graphics submissions
        /// from the player Update may have expired before that independent render request.
        /// Uses the production matrices/material/path; no pooled Renderers or player-only changes.
        /// </summary>
        public void SubmitCelebrationForPreview(Camera camera)
        {
            if (initialized && !released && visible && celebrationActive && camera != null && camera == view)
                DrawCoins();
        }
#endif

        float Range(float a, float b) => a + (b - a) * (float)random.NextDouble();

        void BuildAudio()
        {
            audioRoot = new GameObject("Treasure short sounds");
            pickupAudio = audioRoot.AddComponent<AudioSource>(); rewardAudio = audioRoot.AddComponent<AudioSource>();
            musicAudio = audioRoot.AddComponent<AudioSource>();
            foreach (AudioSource source in new[] { pickupAudio, rewardAudio, musicAudio })
            {
                source.playOnAwake = false; source.loop = false; source.spatialBlend = 0f;
                source.dopplerLevel = 0f; source.volume = .55f;
            }
            OceanAudioPreferences.Register(pickupAudio, OceanAudioCategory.Feedback);
            OceanAudioPreferences.Register(rewardAudio, OceanAudioCategory.Feedback);
            OceanAudioPreferences.Register(musicAudio, OceanAudioCategory.Music);
            collectClip = MakeClip("Shell water chime", 0);
            duplicateClip = MakeClip("Shell already held", 1);
            completeClip = MakeClip("Five shells resolved", 2);
            coinsClip = MakeClip("Hidden treasure metallic cascade", 3);
        }

        static AudioClip MakeClip(string name, int kind)
        {
            float[] data = TreasureSoundSynthesis.Generate(kind);
            AudioClip clip = AudioClip.Create(name, data.Length, 1, TreasureSoundSynthesis.SampleRate, false);
            clip.SetData(data, 0); return clip;
        }

        void CancelTransient()
        {
            activeNotification = default;
            toastDuration = 0f; CurrentNotification = ""; explorationToast = false; queueStart = queueCount = 0;
            Array.Clear(notifications, 0, notifications.Length);
            celebrationActive = false; ActiveCelebrationCoins = 0;
            if (toastAlpha != null) toastAlpha.alpha = 0f;
            if (pickupAudio != null) pickupAudio.Stop(); if (rewardAudio != null) rewardAudio.Stop();
            if (musicAudio != null) musicAudio.Stop();
        }
        void OnApplicationPause(bool paused) { if (paused) CancelTransient(); }
        void OnDisable() { if (hudRoot != null) hudRoot.SetActive(false); CancelTransient(); }
        void OnEnable() { if (initialized && hudRoot != null) hudRoot.SetActive(visible && journalVisible); }
        public void Dispose() { Release(); if (Application.isPlaying) Destroy(this); else DestroyImmediate(this); }
        void OnDestroy() => Release();
        void Release()
        {
            OceanLanguage.Changed -= RefreshLanguage;
            if (released) return;
            CancelTransient(); released = true;
            DestroyOwned(hudRoot); DestroyOwned(toastRoot); DestroyOwned(audioRoot); DestroyOwned(uiMaterial); DestroyOwned(coinMaterial);
            DestroyOwned(panelMaterial); DestroyOwned(panelSprite); DestroyOwned(panelTexture);
            DestroyOwned(sparkMaterial); DestroyOwned(sparkMesh);
            DestroyOwned(collectClip); DestroyOwned(duplicateClip); DestroyOwned(completeClip); DestroyOwned(coinsClip); DestroyOwned(endingClip);
            if (coinReference) { CollectibleVisuals.ReleaseSharedResources(); coinReference = false; }
        }
        static void DestroyOwned(UnityEngine.Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
        }
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    }

    /// <summary>One-time PCM synthesis. No streaming audio callbacks, allocation or synthesis during pickup.</summary>
    public static class TreasureSoundSynthesis
    {
        public const int SampleRate = 22050;
        public static float[] Generate(int kind)
        {
            if (kind == 3) return JackpotSoundSynthesis.Generate(SampleRate);
            double duration = kind == 2 ? 1.65 : kind == 1 ? .20 : .48;
            var data = new float[(int)(duration * SampleRate)];
            if (kind == 2)
            {
                double[] notes = { 523.25, 659.25, 783.99, 1046.5 };
                for (int i = 0; i < notes.Length; i++) Ring(data, i * .16, notes[i], .95, .24, false);
            }
            else if (kind == 1) Ring(data, 0, 540, .18, .20, false);
            else { Ring(data, 0, 880, .34, .30, false); Ring(data, .09, 1320, .34, .22, false); }
            float peak = .001f; for (int i = 0; i < data.Length; i++) peak = Math.Max(peak, Math.Abs(data[i]));
            float gain = Math.Min(1f, .72f / peak);
            for (int i = 0; i < data.Length; i++)
            {
                double edge = Math.Min(1, Math.Min(i / (SampleRate * .003), (data.Length - 1 - i) / (SampleRate * .025)));
                data[i] *= gain * (float)Math.Max(0, edge);
            }
            return data;
        }
        static void Ring(float[] data, double start, double frequency, double length, double gain, bool metallic)
        {
            int first = (int)(start * SampleRate), samples = (int)(length * SampleRate);
            for (int i = 0; i < samples && first + i < data.Length; i++)
            {
                double t = i / (double)SampleRate, phase = 2 * Math.PI * frequency * t;
                double envelope = (1 - Math.Exp(-t * 1400)) * Math.Exp(-t * (metallic ? 24 : 7) / Math.Max(.4, length));
                double tone = Math.Sin(phase) + (metallic ? .42 : .20) * Math.Sin(phase * (metallic ? 1.731 : 2))
                    + (metallic ? .22 : .07) * Math.Sin(phase * (metallic ? 2.137 : 3));
                data[first + i] += (float)(tone * envelope * gain);
            }
        }
    }

    [AddComponentMenu("")]
    public sealed class TreasureTextGradient : BaseMeshEffect
    {
        public bool Gold { get; set; }
        public override void ModifyMesh(VertexHelper helper)
        {
            if (!IsActive()) return;
            UIVertex vertex = default; float low = float.PositiveInfinity, high = float.NegativeInfinity;
            for (int i = 0; i < helper.currentVertCount; i++)
            {
                helper.PopulateUIVertex(ref vertex, i); low = Mathf.Min(low, vertex.position.y); high = Mathf.Max(high, vertex.position.y);
            }
            for (int i = 0; i < helper.currentVertCount; i++)
            {
                helper.PopulateUIVertex(ref vertex, i);
                if (vertex.color.r < 70 && vertex.color.g < 70 && vertex.color.b < 70) continue;
                Color tint = Color.Lerp(Gold ? new Color(.99f, .73f, .24f) : new Color(.48f, .85f, .91f),
                    new Color(1f, .99f, .92f), Mathf.InverseLerp(low, high, vertex.position.y));
                tint.a = vertex.color.a / 255f; vertex.color = tint; helper.SetUIVertex(vertex, i);
            }
        }
    }

}

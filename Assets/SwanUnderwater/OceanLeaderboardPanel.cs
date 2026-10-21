using System;
using System.Collections.Generic;
using HandType = SwanUnderwater.OceanHandSide;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR;

namespace SwanUnderwater
{
    /// <summary>Virtualized, ray-operated leaderboard. Transport, identity and authentication belong to the caller.</summary>
    [DefaultExecutionOrder(96)]
    public sealed class OceanLeaderboardPanel : MonoBehaviour
    {
        [Serializable] public sealed class Row
        {
            public int rank;
            public string publicId, nickname;
            public long score, shellTotal, fishTotal, coinTotal;
            public int discoveryCount;
            public int speciesCount = OceanSpeciesCatalog.Count;
            public bool isMe;
        }
        sealed class Target { public string id; public RectTransform rect; public Image image; public Action action; public bool hovered; }
        sealed class Pointer
        {
            public OceanMenuPressGate gate = new OceanMenuPressGate();
            public LineRenderer line;
            public bool dragging, hasRay;
            public Ray ray;
            public float previousY, dragUnits;
#if UNITY_EDITOR
            public bool validation, fresh;
            public Ray sample;
            public float strength;
#endif
        }
        const int VisibleRows = 6, CacheLimit = 250, ListTarget = 10000;
        const float RowHeight = 53f;
        static readonly Color Gold = new Color(1,.81f,.45f), Ink = new Color(.9f,.97f,.98f), Muted = new Color(.53f,.73f,.78f);
        readonly List<Row> rows = new List<Row>();
        readonly List<Target> targets = new List<Target>();
        readonly Pointer left = new Pointer(), right = new Pointer(), leftController = new Pointer(), rightController = new Pointer();
        readonly Text[] ranks = new Text[VisibleRows], names = new Text[VisibleRows], scores = new Text[VisibleRows], details = new Text[VisibleRows];
        readonly Image[] rowCards = new Image[VisibleRows];
        Camera view;
        HandSwimController hands;
        Action<string,string> action;
        Font font;
        Material textMaterial, imageMaterial, rayMaterial;
        GameObject root, board, keyboard;
        RectTransform listRect, scrollThumb;
        Text profile, totals, status, range, draftLabel, compositionLabel, modeLabel, scoreColumn;
        public bool IsDiscovery { get; private set; }
        readonly Text[] candidateLabels=new Text[OceanPinyinInput.PageSize];
        OceanPinyinInput pinyin;
        bool chineseMode=true;
        string serverNameDraft="", serverNameStatus="idle", submittedName="";
        string nickname = "", shortId = "", draft = "", nextPage, requestedPage = "";
        int firstRow, lastActivation = -1;
        bool initialized, built, busy, disposed, appPaused, renamePending;
        bool appFocused = true;
        ResourceRequest fontRequest, dictionaryRequest;
        double blockedUntil;
        float unreachableSeconds;
        Texture2D cardTexture;
        Sprite cardSprite;
        public bool IsOpen { get; private set; }
        public int RequestVersion { get; private set; }
        public int LoadedRowCount => rows.Count;
        public int FirstVisibleRow => firstRow;
        public bool IsBusy => busy;
        public bool IsReady => built;
        public string DraftNickname => draft;

        public void Initialize(Camera camera, HandSwimController controller, Action<string,string> onAction)
        {
            if (initialized) return;
            if (camera == null || controller == null || onAction == null) throw new ArgumentNullException("Leaderboard initialization");
            view = camera; hands = controller; action = onAction;
            initialized=true;
        }
        void BuildUI()
        {
            Shader shader = Resources.Load<Shader>("PerformanceHudUI");
            if (font == null || shader == null) throw new InvalidOperationException("Leaderboard font or UI shader is missing.");
            textMaterial = new Material(shader) { name = "Leaderboard text", renderQueue = 4190 };
            imageMaterial = new Material(shader) { name = "Leaderboard panels", renderQueue = 4180 };
            rayMaterial = new Material(shader) { name = "Leaderboard rays", renderQueue = 4210 };
            BuildCardSprite();
            root = new GameObject("Ocean leaderboard", typeof(RectTransform), typeof(Canvas)); root.transform.SetParent(transform,false);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = view; canvas.overrideSorting = true; canvas.sortingOrder = 32760;
            ((RectTransform)root.transform).sizeDelta = new Vector2(790,720); root.transform.localScale = Vector3.one * .0011f;
            Card(root.transform,"Backdrop",Vector2.zero,new Vector2(790,720),new Color(.008f,.025f,.049f,.98f));
            var ornament = new GameObject("Champagne border",typeof(RectTransform),typeof(OceanMenuOrnament)); ornament.transform.SetParent(root.transform,false);
            var border = ornament.GetComponent<OceanMenuOrnament>(); border.material = imageMaterial; border.rectTransform.sizeDelta = new Vector2(786,716); border.SetAppearance(Gold,2,22); border.SetMotion(true,.055f);
            Label(root.transform,"Title","本机成绩 · 离线版",38,new Vector2(0,302),new Vector2(470,64),Gold);
            Button(root.transform,"close","返回",new Vector2(-319,306),new Vector2(100,52),()=>{ Close(); action("close",""); });
            Button(root.transform,"refresh","刷新",new Vector2(319,306),new Vector2(100,52),RefreshPage);
            board = Group(root.transform,"Rankings"); keyboard = Group(root.transform,"Ray keyboard");
            Button(board.transform,"score_board","积分榜",new Vector2(-145,250),new Vector2(250,42),()=>SwitchBoard(false));
            Button(board.transform,"discovery_board","生物发现榜",new Vector2(145,250),new Vector2(250,42),()=>SwitchBoard(true));
            totals = Label(board.transform,"Totals","离线模式 · 仅显示本机记录",16,new Vector2(0,-291),new Vector2(710,34),Muted);
            profile = Label(board.transform,"Profile","",22,new Vector2(-73,201),new Vector2(550,42),Ink); profile.resizeTextForBestFit=true; profile.resizeTextMinSize=16; profile.resizeTextMaxSize=22;
            Button(board.transform,"edit_name","改昵称",new Vector2(285,203),new Vector2(140,46),OpenKeyboard);
            Label(board.transform,"Rank column","名次",18,new Vector2(-314,159),new Vector2(64,34),Muted);
            var nameColumn=Label(board.transform,"Name column","本机玩家",18,new Vector2(-80,159),new Vector2(378,34),Muted); nameColumn.alignment=TextAnchor.MiddleLeft;
            scoreColumn=Label(board.transform,"Score column","综合分",18,new Vector2(277,159),new Vector2(128,34),Muted);
            listRect = (RectTransform)Group(board.transform,"Drag rankings").transform; listRect.anchoredPosition = new Vector2(0,-15); listRect.sizeDelta = new Vector2(714,318);
            for (int i=0;i<VisibleRows;i++)
            {
                float y=132.5f-i*RowHeight;
                rowCards[i]=Card(listRect,"Row "+i,new Vector2(0,y),new Vector2(706,49),new Color(.022f,.083f,.112f,.96f));
                ranks[i]=Label(rowCards[i].transform,"Rank","",24,new Vector2(-314,0),new Vector2(57,46),Gold);
                names[i]=Label(rowCards[i].transform,"Name","",20,new Vector2(-80,7),new Vector2(378,37),Ink); names[i].alignment=TextAnchor.MiddleLeft; names[i].resizeTextForBestFit=true; names[i].resizeTextMinSize=14; names[i].resizeTextMaxSize=20;
                details[i]=Label(rowCards[i].transform,"Catch totals","",15,new Vector2(-80,-15),new Vector2(378,27),Muted); details[i].alignment=TextAnchor.MiddleLeft;
                scores[i]=Label(rowCards[i].transform,"Score","",25,new Vector2(277,0),new Vector2(128,46),Gold);
            }
            Card(board.transform,"Scroll track",new Vector2(366,-15),new Vector2(4,314),new Color(.10f,.19f,.22f));
            scrollThumb=Card(board.transform,"Scroll thumb",new Vector2(366,108),new Vector2(6,60),Gold).rectTransform;
            range=Label(board.transform,"Range","",17,new Vector2(0,-198),new Vector2(700,30),Muted);
            Button(board.transform,"top","榜首",new Vector2(-277,-241),new Vector2(125,48),RefreshPage);
            Button(board.transform,"previous","向上",new Vector2(-93,-241),new Vector2(155,48),()=>ScrollRows(-VisibleRows));
            Button(board.transform,"next","向下 / 继续",new Vector2(106,-241),new Vector2(203,48),()=>ScrollRows(VisibleRows));
            Button(board.transform,"retry","重试",new Vector2(295,-241),new Vector2(114,48),()=>RequestPage(requestedPage));
            BuildKeyboard();
            status=Label(root.transform,"Status","贝壳1分 · 鱼类2分 · 金币10分",15,new Vector2(0,-331),new Vector2(735,48),Muted);
            left.line=BuildRay("Leaderboard left hand"); right.line=BuildRay("Leaderboard right hand"); leftController.line=BuildRay("Leaderboard left controller"); rightController.line=BuildRay("Leaderboard right controller");
            built=true; root.SetActive(false); keyboard.SetActive(false); Redraw();
        }

        public void Show(string displayName, string deviceShortId)
        {
            if (!initialized) throw new InvalidOperationException("Initialize leaderboard first.");
            bool wasOpen=IsOpen;
            RequestVersion++; IsOpen=true; SetProfile(displayName,deviceShortId); hands.SetMenuLocomotionPaused(true); ResetPointers();
            if(!wasOpen)Debug.Log($"[LeaderboardUI] action=open open={IsOpen} ready={built} locomotionPaused={hands.MenuLocomotionPaused} frame={Time.frameCount}");
            StartCoroutine(OpenWhenFontReady(RequestVersion));
        }
        System.Collections.IEnumerator OpenWhenFontReady(int version)
        {
            // Only the explicit menu opening requests the full CJK font; initialization is cheap.
            if(!built)
            {
                if(fontRequest==null)fontRequest=Resources.LoadAsync<Font>("Leaderboard/LeaderboardFont");
                if(dictionaryRequest==null)dictionaryRequest=Resources.LoadAsync<TextAsset>("Leaderboard/PinyinCandidates");
                while(!fontRequest.isDone||!dictionaryRequest.isDone){if(!IsOpen||version!=RequestVersion||disposed)yield break;yield return null;}
                if(!IsOpen||version!=RequestVersion||disposed)yield break;
                font=fontRequest.asset as Font;
                if(font==null){Debug.LogError("[LeaderboardUI] Complete CJK font is unavailable.");Close();action("close","");yield break;}
                try { pinyin=new OceanPinyinInput((dictionaryRequest.asset as TextAsset)?.text); }
                catch(Exception error){Debug.LogError("[LeaderboardUI] Pinyin input unavailable: "+error.Message);Close();action("close","");yield break;}
                BuildUI();
            }
            if(!IsOpen||version!=RequestVersion)yield break;
            root.SetActive(true);board.SetActive(true);keyboard.SetActive(false);SetProfile(nickname,shortId);Anchor();ResetPointers();
            rows.Clear(); firstRow=0; nextPage=null; busy=false; Redraw(); RequestPage("");
        }
        public void Close()
        {
            if (!initialized) return;
            bool wasOpen=IsOpen;
            RequestVersion++; IsOpen=false; busy=false; renamePending=false; if(root!=null)root.SetActive(false); ResetPointers(); hands.SetMenuLocomotionPaused(false);
            if(wasOpen)Debug.Log($"[LeaderboardUI] action=close open={IsOpen} ready={built} locomotionPaused={hands.MenuLocomotionPaused} frame={Time.frameCount}");
        }
        public void SetProfile(string displayName,string deviceShortId)
        {
            shortId=ShortId(deviceShortId); nickname=string.IsNullOrWhiteSpace(displayName)?"设备-"+shortId:displayName;
            if (profile!=null) profile.text="我的昵称  "+nickname;
        }
        // Only a transport-confirmed profile result may close the editor. Queuing never claims success.
        public void SetNicknameResult(string confirmed,string pendingDraft,string result,string error)
        {
            serverNameDraft=pendingDraft??"";serverNameStatus=result??"idle";
            if(!built||!IsOpen)return;
            if (result == "local" && renamePending && confirmed == submittedName)
            {
                renamePending = false; busy = false;
                if (keyboard.activeSelf && draft.Trim() == submittedName && pinyin.Composition.Length == 0)
                { keyboard.SetActive(false); board.SetActive(true); ResetPointers(); }
                SetStatus("离线昵称已保存在本机 · 不校验与他人重名");
            }
            else if(result=="pending" && keyboard.activeSelf && renamePending)
                SetStatus("离线昵称草稿已保存 · 不进行联网重名校验");
            else if(result=="rejected" && keyboard.activeSelf && (renamePending||draft==serverNameDraft))
                SetError(error=="nickname_taken"?"这个昵称已被使用，请换一个 · 原昵称保留":"昵称未通过验证，请修改后重试 · 原昵称保留");
            else if(result=="accepted" && renamePending && confirmed==submittedName)
            {
                renamePending=false;busy=false;
                if(keyboard.activeSelf && draft.Trim()==submittedName && pinyin.Composition.Length==0)
                {keyboard.SetActive(false);board.SetActive(true);ResetPointers();}
                SetStatus(keyboard.activeSelf?"上次昵称已确认 · 当前编辑尚未提交":"昵称已由服务器确认");
            }
        }
        public void SetBusy(bool value) { busy=value; if(built)Redraw(); }
        public void SetStatus(string text) { if(status!=null){status.text=text??"";status.color=Muted;} }
        public void SetError(string message) { busy=false; renamePending=false; if(built)Redraw(); if(status!=null){status.text=message??"连接暂不可用，请重试";status.color=Gold;} }
        public void SetPage(Row[] page,string nextCursor,long totalPlayers,long totalDemoCount,bool append,string freshness="")
        {
            if (!IsOpen||!built) return;
            if (!append){rows.Clear();firstRow=0;}
            if(page!=null) foreach(Row row in page)
            {
                if(row==null)continue;
                int existing=rows.FindIndex(x=>!string.IsNullOrEmpty(row.publicId)&&x.publicId==row.publicId);
                if(existing>=0)rows[existing]=row;else rows.Add(row);
            }
            if(rows.Count>CacheLimit){int remove=rows.Count-CacheLimit;rows.RemoveRange(0,remove);firstRow=Math.Max(0,firstRow-remove);}
            busy=false;
            nextPage=string.IsNullOrEmpty(nextCursor)?null:nextCursor;
            if(append && nextPage==requestedPage){nextPage=null;SetError("分页暂不可用，请刷新排行榜");}
            else SetStatus(string.IsNullOrEmpty(freshness)?(IsDiscovery?"跨关卡寻找生物 · 同类只记一次":"贝壳1分 · 鱼类2分 · 金币10分"):freshness);
            totals.text="仅本机记录    ·    "+Number(totalDemoCount)+" 次本机体验";
            Redraw();
        }
        void SwitchBoard(bool discovery)
        {
            if(keyboard.activeSelf||IsDiscovery==discovery)return;
            IsDiscovery=discovery;busy=false;RefreshPage();
        }
        void RefreshPage(){if(busy||keyboard.activeSelf)return;RequestVersion++;rows.Clear();firstRow=0;nextPage=null;Redraw();RequestPage("");}
        void RequestPage(string cursor)
        {if(!IsOpen||busy)return;busy=true;requestedPage=cursor??"";Redraw();SetStatus("正在读取本机记录…");action("load",requestedPage);}
        void ScrollRows(int amount)
        {
            if(!IsOpen||keyboard.activeSelf)return;
            firstRow=Mathf.Clamp(firstRow+amount,0,Mathf.Max(0,rows.Count-VisibleRows));Redraw();
            if(amount>0&&firstRow>=Mathf.Max(0,rows.Count-VisibleRows-2)&&nextPage!=null&&!busy)RequestPage(nextPage);
        }
        void Redraw()
        {
            firstRow=Mathf.Clamp(firstRow,0,Mathf.Max(0,rows.Count-VisibleRows));
            if(scoreColumn!=null)scoreColumn.text=IsDiscovery?"发现种类":"综合分";
            for(int i=0;i<VisibleRows;i++)
            {
                int index=firstRow+i; bool present=index<rows.Count;rowCards[i].gameObject.SetActive(present);if(!present)continue;
                Row row=rows[index];ranks[i].text=row.rank.ToString();names[i].text=(row.isMe?"我 · ":"")+(string.IsNullOrEmpty(row.nickname)?"设备-"+ShortId(row.publicId):row.nickname);
                details[i].text=IsDiscovery?"跨关历史累计 · 包含历史收藏":"贝壳 "+Number(row.shellTotal)+" · 鱼 "+Number(row.fishTotal)+" · 金币 "+Number(row.coinTotal);scores[i].text=IsDiscovery?row.discoveryCount+" 类":Number(row.score);
                rowCards[i].color=row.isMe?new Color(.075f,.18f,.19f,.98f):new Color(.022f,.083f,.112f,.96f);
            }
            if(range!=null)range.text=rows.Count==0?(busy?"正在读取排名…":"还没有排名，完成一次探险后再来看看"):rows[firstRow].rank+" — "+rows[Math.Min(rows.Count-1,firstRow+VisibleRows-1)].rank+(nextPage==null&&firstRow+VisibleRows>=rows.Count?"  ·  已到底":"  ·  下滑查看更多");
            if(scrollThumb!=null){float ratio=Mathf.Clamp01(VisibleRows/(float)Math.Max(VisibleRows,rows.Count));float h=Mathf.Max(24,314*ratio);scrollThumb.sizeDelta=new Vector2(6,h);float t=firstRow/(float)Math.Max(1,rows.Count-VisibleRows);scrollThumb.anchoredPosition=new Vector2(366,142-h*.5f-(314-h)*t);}
        }

        void BuildKeyboard()
        {
            Label(keyboard.transform,"Rename help","逐字拼音选字 · 空格选首字 · ü 用 V",20,new Vector2(0,250),new Vector2(700,36),Muted);
            draftLabel=Label(keyboard.transform,"Draft nickname","",28,new Vector2(0,204),new Vector2(710,50),Gold);
            compositionLabel=Label(keyboard.transform,"Pinyin composition","",20,new Vector2(0,158),new Vector2(700,36),Muted);
            Button(keyboard.transform,"candidate_previous","‹",new Vector2(-336,112),new Vector2(44,44),()=>CandidatePage(-1));
            Button(keyboard.transform,"candidate_next","›",new Vector2(336,112),new Vector2(44,44),()=>CandidatePage(1));
            for(int i=0;i<OceanPinyinInput.PageSize;i++)
            {
                int slot=i;Button(keyboard.transform,"candidate_"+i,"",new Vector2((i-3.5f)*75,112),new Vector2(67,44),()=>SelectCandidate(slot));
                candidateLabels[i]=targets[targets.Count-1].rect.GetComponentInChildren<Text>();
            }
            string[] lines={"1234567890","QWERTYUIOP","ASDFGHJKL","ZXCVBNM"};
            for(int r=0;r<lines.Length;r++)for(int c=0;c<lines[r].Length;c++)
            {string key=lines[r][c].ToString();Button(keyboard.transform,"key_"+key,key,new Vector2((c-(lines[r].Length-1)*.5f)*66,54-r*58),new Vector2(59,48),()=>TypeKey(key));}
            Button(keyboard.transform,"input_mode","中文拼音",new Vector2(-280,-184),new Vector2(126,48),()=>{if(busy)return;chineseMode=!chineseMode;pinyin.Clear();RefreshDraft();});
            modeLabel=targets[targets.Count-1].rect.GetComponentInChildren<Text>();modeLabel.fontSize=18;
            Button(keyboard.transform,"space","空格",new Vector2(-140,-184),new Vector2(126,48),()=>{if(pinyin.Composition.Length>0)SelectCandidate(0);else AppendName(" ");});
            Button(keyboard.transform,"erase","退格",new Vector2(0,-184),new Vector2(126,48),()=>{if(busy)return;if(!pinyin.Erase())draft=EraseLast(draft);RefreshDraft();});
            Button(keyboard.transform,"clear","清空",new Vector2(140,-184),new Vector2(126,48),()=>{if(busy)return;draft="";pinyin.Clear();RefreshDraft();});
            Button(keyboard.transform,"save_name","保存",new Vector2(280,-184),new Vector2(126,48),SaveNickname);
            Button(keyboard.transform,"cancel_name","取消",new Vector2(0,-258),new Vector2(220,46),()=>{keyboard.SetActive(false);board.SetActive(true);ResetPointers();});
        }
        void SaveNickname()
        {
            if(busy)return;
            if(pinyin.Composition.Length>0){SetError("请先选择拼音对应的汉字");return;}
            string value=draft.Trim();
            if(!OceanLeaderboardLedger.ValidNickname(value,out _)){SetError("请输入1到20个有效字符");return;}
            value=value.Normalize(System.Text.NormalizationForm.FormC);draft=value;submittedName=value;
            busy=true;renamePending=true;SetStatus("正在保存本机昵称…");action("rename",value);
        }
        void OpenKeyboard()
        {
            if(busy)return;
            draft=(serverNameStatus=="pending"||serverNameStatus=="rejected")&&!string.IsNullOrEmpty(serverNameDraft)?serverNameDraft:nickname;
            pinyin.Clear();keyboard.SetActive(true);board.SetActive(false);RefreshDraft();
            if(serverNameStatus=="pending")SetStatus("本机昵称草稿 · 可继续修改");
            ResetPointers();
        }
        void TypeKey(string key)
        {
            if(busy)return;
            if(chineseMode&&char.IsLetter(key[0]))
            {if(!pinyin.Append(key[0])){SetError("一个拼音最多6个字母，请先选字");return;}RefreshDraft();}
            else if(pinyin.Composition.Length>0)
            {int index=key[0]-'1';if(index>=0&&index<8)SelectCandidate(index);else SetError("请先选字，再输入数字");}
            else AppendName(key);
        }
        void CandidatePage(int delta){if(busy)return;pinyin.ChangePage(delta);RefreshDraft();ResetPointers();}
        void SelectCandidate(int index)
        {
            if(busy)return;string value=pinyin.Candidate(index);
            if(value.Length==0){SetError("没有这个候选字，请检查拼音");return;}
            if(!CanAppend(value))return;pinyin.Select(index);draft+=value;RefreshDraft();ResetPointers();
        }
        bool CanAppend(string value)
        {if(ScalarCount(draft+value)>20){SetError("昵称最多20个字符");return false;}return true;}
        void AppendName(string value){if(busy||!CanAppend(value))return;draft+=value;RefreshDraft();}
        void RefreshDraft()
        {
            draftLabel.text=draft.Length==0?"输入昵称":draft;
            modeLabel.text=chineseMode?"中文拼音":"English";
            compositionLabel.text=pinyin.Composition.Length==0?(chineseMode?"输入一个音节，再选汉字":"英文和数字输入"):"拼音 "+pinyin.Composition+"  ·  "+(pinyin.Page+1)+"/"+pinyin.PageCount+(pinyin.Candidates.Length==0?"  ·  暂无候选":"");
            for(int i=0;i<candidateLabels.Length;i++)candidateLabels[i].text=pinyin.Candidate(i);
            SetStatus("最多20个字符 · 离线昵称仅本机使用，不校验重名");
        }
        static int ScalarCount(string value){int n=0;for(int i=0;i<value.Length;i++){if(char.IsHighSurrogate(value[i])&&i+1<value.Length&&char.IsLowSurrogate(value[i+1]))i++;n++;}return n;}
        public static string EraseLast(string value)
        {if(string.IsNullOrEmpty(value))return "";int[] starts=System.Globalization.StringInfo.ParseCombiningCharacters(value);return value.Substring(0,starts[starts.Length-1]);}
        static string ShortId(string value){value=value??"";return value.Length>6?value.Substring(0,6):value;}
        static string Number(long value)=>Math.Max(0,value).ToString("N0",System.Globalization.CultureInfo.InvariantCulture);
        void Anchor()
        {Vector3 f=Vector3.ProjectOnPlane(view.transform.forward,Vector3.up);if(f.sqrMagnitude<.02f)f=Vector3.forward;f.Normalize();root.transform.SetPositionAndRotation(view.transform.position+f*.95f-Vector3.up*.045f,Quaternion.LookRotation(f,Vector3.up));}

        void LateUpdate()
        {
            if(!initialized||!built||!IsOpen||appPaused||!appFocused||disposed)return;
            foreach(Target t in targets)t.hovered=false;
            ProcessNativeMenuPinches();
            ProcessHand(HandType.HandLeft,left);ProcessHand(HandType.HandRight,right);ProcessController(XRNode.LeftHand,leftController);ProcessController(XRNode.RightHand,rightController);
            foreach(Target t in targets)if(t.image!=null)t.image.color=t.hovered?new Color(.19f,.145f,.062f,.98f):new Color(.027f,.105f,.139f,.98f);
            bool targeted=left.dragging||right.dragging||leftController.dragging||rightController.dragging;
            foreach(Target t in targets)targeted|=t.hovered;
            if(targeted||lastActivation==Time.frameCount||Time.unscaledTimeAsDouble<blockedUntil||
                !OceanCampaignPanel.ShouldRecoverMenu(view.transform.InverseTransformPoint(root.transform.position))) unreachableSeconds=0;
            else
            {
                unreachableSeconds+=Mathf.Min(Time.unscaledDeltaTime,.1f);
                if(unreachableSeconds>=.8f){unreachableSeconds=0;Anchor();ResetPointers();}
            }
        }
        void ProcessNativeMenuPinches()
        {
            if(!OceanXRPlatform.IsVisionOS)return;
            for(int slot=0;slot<2;slot++)
                ProcessNativeMenuPinch(slot);
        }
        void ProcessNativeMenuPinch(int slot)
        {
            if(!initialized||!built||!IsOpen||appPaused||!appFocused||disposed||
                !hands.TryGetNativeVisionOSMenuPinch(slot,out Ray ray)||!Finite(ray.origin)||!Finite(ray.direction)||
                ray.direction.sqrMagnitude<.5f||float.IsInfinity(ray.direction.sqrMagnitude))return;
            Target target=null;float nearest=3f;
            foreach(Target candidate in targets)
                if(candidate.rect.gameObject.activeInHierarchy&&Hit(ray,candidate.rect,out float distance,out _)&&distance<=nearest)
                {target=candidate;nearest=distance;}
            bool onList=!keyboard.activeSelf&&Hit(ray,listRect,out _,out _);
            if(target==null&&!onList)return;
            hands.ConsumeNativeVisionOSMenuPinch(slot);
            // A native Began is already a qualified press; never feed strength=1 into
            // the release/hover gate, which has no native pre-pinch samples.
            if(target!=null&&lastActivation!=Time.frameCount&&Time.unscaledTimeAsDouble>=blockedUntil)
            {lastActivation=Time.frameCount;blockedUntil=Time.unscaledTimeAsDouble+.22;target.action();}
        }
        void ProcessHand(HandType hand,Pointer pointer)
        {
#if UNITY_EDITOR
            if(pointer.validation){bool hit=Process(pointer,pointer.sample,pointer.strength,pointer.fresh);hands.SetMenuHandTargeted(hand,hit);return;}
#endif
            bool fresh=hands.TryGetMenuHandRay(hand,out Ray ray,out float strength);hands.SetMenuHandTargeted(hand,Process(pointer,ray,strength,fresh));
        }
        void ProcessController(XRNode node,Pointer pointer)
        {
            var device=InputDevices.GetDeviceAtXRNode(node);
            bool ok=hands.TrackingSpace!=null&&device.isValid&&device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked)&&tracked;
            if(!ok||!device.TryGetFeatureValue(CommonUsages.devicePosition,out Vector3 p)||!device.TryGetFeatureValue(CommonUsages.deviceRotation,out Quaternion q)){Process(pointer,default,0,false);return;}
            device.TryGetFeatureValue(CommonUsages.trigger,out float press);device.TryGetFeatureValue(CommonUsages.triggerButton,out bool button);
            Process(pointer,new Ray(hands.TrackingSpace.TransformPoint(p),hands.TrackingSpace.rotation*q*Vector3.forward),button?1:press,true);
        }
        bool Process(Pointer pointer,Ray ray,float strength,bool fresh)
        {
            if(!IsOpen){pointer.line.enabled=false;return false;}
            float dt=Time.unscaledDeltaTime;
            if(!fresh||!Finite(ray.origin)||!Finite(ray.direction)||ray.direction.sqrMagnitude<.5f||float.IsNaN(strength)||float.IsInfinity(strength)||dt<=0||dt>OceanMenuPressGate.MaximumSampleSeconds)
            {pointer.gate.Reset();pointer.dragging=false;pointer.hasRay=false;pointer.line.enabled=false;return false;}
            if(!pointer.hasRay||Vector3.Distance(pointer.ray.origin,ray.origin)>.4f||Vector3.Angle(pointer.ray.direction,ray.direction)>45)pointer.ray=ray;
            else{float a=1-Mathf.Exp(-dt*30);pointer.ray=new Ray(Vector3.Lerp(pointer.ray.origin,ray.origin,a),Vector3.Slerp(pointer.ray.direction,ray.direction,a).normalized);}
            pointer.hasRay=true;ray=pointer.ray;Target target=null;float distance=3f;int index=-1;
            for(int i=0;i<targets.Count;i++){Target candidate=targets[i];if(candidate.rect.gameObject.activeInHierarchy&&Hit(ray,candidate.rect,out float d,out _)&&d<=distance){target=candidate;distance=d;index=i;}}
            float listDistance=0,localY=0;
            bool onList=!keyboard.activeSelf&&Hit(ray,listRect,out listDistance,out localY);
            // Drag continues while a fresh ray remains on the panel, then ends cleanly on release/loss.
            if(pointer.dragging)
            {
                if(strength<=OceanMenuPressGate.ReleaseThreshold||!onList){pointer.dragging=false;pointer.gate.Reset();}
                else{pointer.dragUnits+=localY-pointer.previousY;pointer.previousY=localY;int delta=(int)(pointer.dragUnits/RowHeight);if(delta!=0){pointer.dragUnits-=delta*RowHeight;ScrollRows(delta);}index=ListTarget;distance=listDistance;}
            }
            if(index<0&&onList){index=ListTarget;distance=listDistance;}
            if(target!=null)target.hovered=true;
            pointer.line.enabled=true;pointer.line.SetPosition(0,ray.origin);pointer.line.SetPosition(1,ray.GetPoint(index<0?1.5f:distance));pointer.line.startColor=new Color(.9f,.75f,.43f,.30f);pointer.line.endColor=index<0?Muted:Gold;
            int pressed=pointer.gate.Step(Time.frameCount,dt,true,strength,index,Time.unscaledTimeAsDouble>=blockedUntil);
            if(pressed==ListTarget){pointer.dragging=true;pointer.previousY=localY;pointer.dragUnits=0;}
            else if(pressed>=0&&lastActivation!=Time.frameCount){lastActivation=Time.frameCount;blockedUntil=Time.unscaledTimeAsDouble+.22;targets[pressed].action();}
            return index>=0;
        }
        static bool Hit(Ray ray,RectTransform rect,out float distance,out float localY)
        {distance=0;localY=0;float denominator=Vector3.Dot(ray.direction,rect.forward);if(denominator<=.0001f)return false;distance=Vector3.Dot(rect.position-ray.origin,rect.forward)/denominator;if(distance<.02f||distance>4)return false;Vector3 p=rect.InverseTransformPoint(ray.GetPoint(distance));localY=p.y;return rect.rect.Contains(new Vector2(p.x,p.y));}
        void ResetPointers()
        {blockedUntil=Time.unscaledTimeAsDouble+.35;foreach(Pointer p in new[]{left,right,leftController,rightController}){p.gate.Reset();p.dragging=false;p.hasRay=false;if(p.line!=null)p.line.enabled=false;}if(hands!=null){hands.SetMenuHandTargeted(HandType.HandLeft,false);hands.SetMenuHandTargeted(HandType.HandRight,false);}}
        static bool Finite(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsInfinity(p.x)&&!float.IsNaN(p.y)&&!float.IsInfinity(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.z);
        GameObject Group(Transform parent,string name){var g=new GameObject(name,typeof(RectTransform));g.transform.SetParent(parent,false);return g;}
        Image Card(Transform parent,string name,Vector2 point,Vector2 size,Color color){var g=new GameObject(name,typeof(RectTransform),typeof(Image));g.transform.SetParent(parent,false);var im=g.GetComponent<Image>();im.material=imageMaterial;im.sprite=cardSprite;im.type=Image.Type.Sliced;im.color=color;im.raycastTarget=false;im.rectTransform.anchoredPosition=point;im.rectTransform.sizeDelta=size;return im;}
        Text Label(Transform parent,string name,string value,int size,Vector2 point,Vector2 dimensions,Color color)
        {var g=new GameObject(name,typeof(RectTransform),typeof(Text));g.transform.SetParent(parent,false);var t=g.GetComponent<Text>();t.font=font;t.material=textMaterial;t.text=value;t.fontSize=size;t.color=color;t.supportRichText=false;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Overflow;t.verticalOverflow=VerticalWrapMode.Truncate;t.rectTransform.anchoredPosition=point;dimensions.y=Mathf.Max(dimensions.y,size*1.6f+2);t.rectTransform.sizeDelta=dimensions;return t;}
        void Button(Transform parent,string id,string label,Vector2 point,Vector2 size,Action callback)
        {Image im=Card(parent,id,point,size,new Color(.027f,.105f,.139f,.98f));Label(im.transform,"Label",label,22,Vector2.zero,size-new Vector2(12,0),Ink);targets.Add(new Target{id=id,rect=im.rectTransform,image=im,action=callback});}
        LineRenderer BuildRay(string name){var g=new GameObject(name,typeof(LineRenderer));g.transform.SetParent(transform,false);var line=g.GetComponent<LineRenderer>();line.sharedMaterial=rayMaterial;line.useWorldSpace=true;line.positionCount=2;line.startWidth=line.endWidth=.0015f;line.sortingOrder=32761;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;return line;}
        void BuildCardSprite(){const int n=32;cardTexture=new Texture2D(n,n,TextureFormat.RGBA32,false){name="Leaderboard rounded card",wrapMode=TextureWrapMode.Clamp};var colors=new Color32[n*n];for(int y=0;y<n;y++)for(int x=0;x<n;x++){float dx=Mathf.Max(0,Mathf.Abs(x-15.5f)-9),dy=Mathf.Max(0,Mathf.Abs(y-15.5f)-9);colors[y*n+x]=new Color(1,1,1,Mathf.Clamp01(6.5f-Mathf.Sqrt(dx*dx+dy*dy)));}cardTexture.SetPixels32(colors);cardTexture.Apply(false,true);cardSprite=Sprite.Create(cardTexture,new Rect(0,0,n,n),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(8,8,8,8));}
        void OnApplicationPause(bool value){appPaused=value;if(initialized)ResetPointers();}
        void OnApplicationFocus(bool value){appFocused=Application.isEditor||value;if(initialized)ResetPointers();}
        void OnDisable(){if(initialized)Close();}
        void OnEnable(){if(built&&root!=null)root.SetActive(IsOpen);}
        static void DestroyOwned(UnityEngine.Object item){if(item==null)return;if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);}
        void OnDestroy(){if(disposed)return;disposed=true;RequestVersion++;ResetPointers();if(IsOpen&&hands!=null)hands.SetMenuLocomotionPaused(false);DestroyOwned(root);foreach(Pointer p in new[]{left,right,leftController,rightController})if(p.line!=null)DestroyOwned(p.line.gameObject);DestroyOwned(textMaterial);DestroyOwned(imageMaterial);DestroyOwned(rayMaterial);DestroyOwned(cardSprite);DestroyOwned(cardTexture);}
#if UNITY_EDITOR
        public void DriveRayForValidation(bool isLeft,Ray ray,float strength,bool fresh=true){Pointer p=isLeft?left:right;p.validation=true;p.sample=ray;p.strength=strength;p.fresh=fresh;}
        public RectTransform GetTargetForValidation(string id){if(id=="list")return listRect;foreach(Target t in targets)if(t.id==id)return t.rect;return null;}
        public string CandidateForValidation(int slot)=>pinyin?.Candidate(slot)??"";
        public string CompositionForValidation=>pinyin?.Composition??"";
        public string RangeForValidation=>range!=null?range.text:"";
        public bool IsKeyboardOpenForValidation=>keyboard!=null&&keyboard.activeSelf;
        public void ClearValidationRays(){left.validation=right.validation=false;ResetPointers();}
#endif
    }
}

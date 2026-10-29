using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SwanUnderwater
{
    public sealed partial class OceanCampaignPanel
    {
        OceanNaturalist naturalist;
        int naturalistIndex, previewNaturalistIndex = -1;
        Text naturalistName, naturalistDetails, naturalistPage, naturalistStatus;
        Target observationToggle;
        Transform naturalistAnchor;
        GameObject naturalistPreview;
        public void BindNaturalist(OceanNaturalist value) { naturalist = value; if (initialized) Refresh(); }

        void BuildCollectionHub()
        {
            Transform root = PageRoot(Page.CollectionHub);
            Button(root, "campaign_collection", "关卡收藏 · 贝壳与目标鱼", new Vector2(0, 112), new Vector2(620, 70), () => ShowPage(Page.ShellsOne));
            Button(root, "naturalist_collection", "生物观察图鉴 · 跨关探索", new Vector2(0, 27), new Vector2(620, 70), () => ShowPage(Page.Naturalist), true);
            Button(root, "show_achievements", "海洋成就 · 查看进度与荣誉", new Vector2(0, -58), new Vector2(620, 70), OpenAchievements);
            Label(root, "Naturalist instructions", "手射线指向生物并保持2秒，查看说明并记录发现。\n观察记录跨关保留，重复发现不增加种类。", 22,
                new Vector2(0, -163), new Vector2(640, 92)).color = Muted;
        }
        void BuildNaturalist()
        {
            Transform root = PageRoot(Page.Naturalist);
            naturalistName = Label(root, "Naturalist species name", "", 31, new Vector2(0, 123), new Vector2(680, 50));
            naturalistName.color = Gold;
            var anchor = new GameObject("Identified model preview", typeof(RectTransform));
            anchor.transform.SetParent(root, false); anchor.transform.localPosition = new Vector3(-206, 5, -24);
            naturalistAnchor = anchor.transform;
            naturalistStatus = Label(root, "Naturalist status", "", 21, new Vector2(-206, -112), new Vector2(250, 80));
            naturalistDetails = Label(root, "Naturalist description", "", 21, new Vector2(115, -12), new Vector2(390, 245));
            naturalistDetails.alignment = TextAnchor.UpperLeft;
            observationToggle = Button(root, "naturalist_observation", "自然观察：开", new Vector2(0, -173), new Vector2(460, 45),
                () => { naturalist?.ToggleObservation(); Refresh(); ResetInput(); });
            Button(root, "naturalist_previous", "上一类", new Vector2(-235, -231), new Vector2(160, 45), () => ChangeNaturalist(-1));
            naturalistPage = Label(root, "Naturalist catalog position", "", 20, new Vector2(0, -231), new Vector2(225, 44));
            Button(root, "naturalist_next", "下一类", new Vector2(235, -231), new Vector2(160, 45), () => ChangeNaturalist(1));
        }
        string NaturalistSummary()
        {
            int count = naturalist?.State.AvailableCount ?? 0;
            string title = count == OceanSpeciesCatalog.AvailableCount ? "深蓝博物学家" : count >= 20 ? "海洋收藏家" : count >= 10 ? "珊瑚探索者" : "见习观察员";
            return F("{0} · 当前已发现 {1}/{2} · 稀有 {3}", L(title), count, OceanSpeciesCatalog.AvailableCount, naturalist?.State.AvailableRareCount ?? 0);
        }
        bool NaturalistEntryVisible(int id) => OceanSpeciesCatalog.IsAvailable(id) || (naturalist != null && naturalist.State.Contains(id));
        void ChangeNaturalist(int delta)
        {
            delta = Math.Sign(delta);
            if (delta == 0) return;
            do { naturalistIndex = (naturalistIndex + delta + OceanSpeciesCatalog.Count) % OceanSpeciesCatalog.Count; }
            while (!NaturalistEntryVisible(naturalistIndex));
            Refresh(); ResetInput();
        }
        void RefreshNaturalist()
        {
            if (naturalistName == null) return;
            bool visible = page == Page.Naturalist;
            if (naturalistPreview != null) naturalistPreview.SetActive(visible);
            if (!visible) return;
            if (!NaturalistEntryVisible(naturalistIndex)) naturalistIndex = (naturalistIndex + 1) % OceanSpeciesCatalog.Count;
            var entry = OceanSpeciesCatalog.Get(naturalistIndex);
            bool known = naturalist != null && naturalist.State.Contains(entry.Id);
            int ordinal = 0, pageCount = 0;
            for (int id = 0; id < OceanSpeciesCatalog.Count; id++)
                if (NaturalistEntryVisible(id)) { pageCount++; if (id <= naturalistIndex) ordinal++; }
            naturalistPage.text = ordinal + " / " + pageCount;
            observationToggle.label.text = F("自然观察：{0}", L(naturalist != null && naturalist.ObservationEnabled ? "开" : "关"));
            SetEnabled(observationToggle, naturalist != null && naturalist.Store.Writable);
            if (!OceanSpeciesCatalog.IsAvailable(entry.Id))
            {
                naturalistName.text = F("{0} · 历史记录", L(entry.Name));
                naturalistStatus.text = L("已收藏 · 原记录保留");
                naturalistStatus.color = Gold;
                naturalistDetails.text = F("{0}\n\n本版暂停投放，不计入当前图鉴收集目标。已有观察记录保留。", L(entry.Description));
                FitNaturalistDetails(naturalistDetails);
                if (naturalistPreview != null) naturalistPreview.SetActive(false);
                return;
            }
            naturalistName.text = entry.Rare ? F("{0} · 稀有观察", L(entry.Name)) : L(entry.Name);
            naturalistStatus.text = known ? L("已发现 · 已保存") : F("尚未发现\n第{0}关起可观察", entry.FirstLevel);
            naturalistStatus.color = known ? Gold : Muted;
            naturalistDetails.text = known ? F("{0}\n\n栖息线索：{1}\n{2}", L(entry.Description), L(entry.Habitat), L(entry.SearchHint))
                : F("探索线索：{0}\n\n在合适关卡找到它，用手射线指向并保持观察。\n{1}", L(entry.SearchHint),
                    L(entry.Id >= 15 && entry.Id <= 24 ? "这是一类贝壳外形记录，不代表新增活体物种。" : "发现后解锁生物说明。"));
            FitNaturalistDetails(naturalistDetails);
            // Reference shapes help recognition; only descriptions/discovery records stay gated.
            if (previewNaturalistIndex != entry.Id || naturalistPreview == null) CreateNaturalistPreview(entry);
        }
        static void FitNaturalistDetails(Text details)
        {
            // Only this long-form card adapts, on presentation refresh. Keep a
            // readable floor and leave menu text and hit regions unchanged.
            details.fontSize=21;
            while(details.fontSize>18&&details.preferredHeight>details.rectTransform.rect.height+.5f)details.fontSize--;
        }
#if UNITY_EDITOR
        public static void FitNaturalistDetailsForValidation(Text details)=>FitNaturalistDetails(details);
#endif
        void CreateNaturalistPreview(OceanSpeciesDefinition entry)
        {
            if (naturalistPreview != null) DestroyOwned(naturalistPreview);
            naturalistPreview = null; previewNaturalistIndex = entry.Id;
            GameObject model;
            if (entry.Id >= 15 && entry.Id <= 24) model = CollectibleVisuals.CreateShell(naturalistAnchor, entry.Id - 15);
            else
            {
                GameObject prefab = Resources.Load<GameObject>(entry.ResourcePath);
                if (prefab == null) { naturalistStatus.text += L("\n模型待资源验证"); return; }
                model = Instantiate(prefab, naturalistAnchor, false);
            }
            naturalistPreview = model; model.name = "Naturalist preview " + entry.Id;
            foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) { c.enabled = false; DestroyOwned(c); }
            foreach (Animator a in model.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            model.transform.localRotation = entry.Id >= 15 && entry.Id <= 24 ? Quaternion.Euler(-67, 18, 8) : Quaternion.Euler(0, 65, -5);
            bool first = true; var bounds = new Bounds();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Bounds b = renderer.localBounds;
                Matrix4x4 matrix = naturalistAnchor.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                }
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i]; if (source == null) continue;
                    if (!previewMaterials.TryGetValue(source, out Material copy))
                    {
                        copy = Own(new Material(source) { name = source.name + " naturalist preview", renderQueue = 4140 });
                        if (copy.HasProperty("_ZTest")) copy.SetInt("_ZTest", (int)CompareFunction.Always);
                        if (copy.HasProperty("_ZWrite")) copy.SetInt("_ZWrite", 0);
                        previewMaterials.Add(source, copy);
                    }
                    materials[i] = copy;
                }
                renderer.sharedMaterials = materials; renderer.sortingOrder = 32759;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            }
            if (!first)
            {
                float scale = Mathf.Min(220 / Mathf.Max(.001f, bounds.size.x), 150 / Mathf.Max(.001f, bounds.size.y));
                model.transform.localScale *= scale; model.transform.localPosition -= bounds.center * scale;
            }
        }
    }
}

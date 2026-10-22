using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater
{
    public sealed partial class OceanCampaignPanel
    {
        const int AchievementRowsPerPage = 4;
        static readonly string[] AchievementCategories = { "全部", "收集", "探索", "关卡", "珍藏", "挑战" };
        readonly List<OceanAchievementDefinition> achievementSelection = new List<OceanAchievementDefinition>(32);
        readonly Target[] achievementFilters = new Target[6];
        readonly UnityEngine.UI.Image[] achievementCards = new UnityEngine.UI.Image[AchievementRowsPerPage];
        readonly UnityEngine.UI.Image[] achievementTracks = new UnityEngine.UI.Image[AchievementRowsPerPage];
        readonly UnityEngine.UI.Image[] achievementFills = new UnityEngine.UI.Image[AchievementRowsPerPage];
        readonly UnityEngine.UI.Text[] achievementNames = new UnityEngine.UI.Text[AchievementRowsPerPage];
        readonly UnityEngine.UI.Text[] achievementDescriptions = new UnityEngine.UI.Text[AchievementRowsPerPage];
        readonly UnityEngine.UI.Text[] achievementStates = new UnityEngine.UI.Text[AchievementRowsPerPage];
        readonly UnityEngine.UI.Text[] achievementNumbers = new UnityEngine.UI.Text[AchievementRowsPerPage];
        readonly OceanMenuOrnament[] achievementFrames = new OceanMenuOrnament[AchievementRowsPerPage];
        readonly string[] achievementVisibleIds = new string[AchievementRowsPerPage];
        OceanAchievementJournal achievementJournal;
        Target achievementPrevious, achievementNext, achievementMarkSeen;
        int achievementPage, achievementFilter;

        public void BindAchievements(OceanAchievementJournal journal)
        {
            achievementJournal = journal;
            if (initialized) Refresh();
        }

        void BuildAchievements()
        {
            Transform root = PageRoot(Page.Achievements);
            for (int index = 0; index < AchievementCategories.Length; index++)
            {
                int selected = index;
                Target target = Button(root, "achievement_filter_" + index, AchievementCategories[index],
                    new Vector2((index - 2.5f) * 111f, 136f), new Vector2(104f, 36f), () => ChangeAchievementCategory(selected));
                target.label.fontSize = 20;
                target.label.rectTransform.sizeDelta = new Vector2(94f, 34f);
                achievementFilters[index] = target;
            }
            for (int slot = 0; slot < AchievementRowsPerPage; slot++)
            {
                var card = AddImage(root, "Achievement card " + slot, new Vector2(0, 75f - slot * 86f),
                    new Vector2(674f, 82f), new Color(.025f, .105f, .135f, .97f));
                achievementCards[slot] = card;
                achievementFrames[slot] = Ornament(card.transform, "Achievement card frame", Vector2.zero,
                    new Vector2(672f, 80f), 1.0f, 9f, false);
                achievementNames[slot] = Label(card.transform, "Achievement name", "", 20, new Vector2(-90f, 20f), new Vector2(452f, 34f));
                achievementNames[slot].alignment = TextAnchor.MiddleLeft;
                achievementStates[slot] = Label(card.transform, "Achievement unlock status", "", 17,
                    new Vector2(242f, 20f), new Vector2(170f, 31f));
                achievementStates[slot].alignment = TextAnchor.MiddleRight;
                achievementDescriptions[slot] = Label(card.transform, "Achievement description", "", 17,
                    new Vector2(-45f, -12f), new Vector2(542f, 29f));
                achievementDescriptions[slot].alignment = TextAnchor.MiddleLeft;
                achievementNumbers[slot] = Label(card.transform, "Achievement numeric progress", "", 17,
                    new Vector2(277f, -12f), new Vector2(100f, 29f));
                achievementNumbers[slot].alignment = TextAnchor.MiddleRight;
                achievementTracks[slot] = AddImage(card.transform, "Achievement progress track", new Vector2(0, -34f),
                    new Vector2(638f, 5f), new Color(.11f, .22f, .25f, 1f));
                var fill = AddImage(achievementTracks[slot].transform, "Achievement progress fill", Vector2.zero,
                    new Vector2(0f, 5f), Gold);
                fill.rectTransform.anchorMin = fill.rectTransform.anchorMax = new Vector2(0f, .5f);
                fill.rectTransform.pivot = new Vector2(0f, .5f);
                fill.rectTransform.anchoredPosition = Vector2.zero;
                achievementFills[slot] = fill;
            }
            achievementPrevious = Button(root, "achievement_previous", "上一页", new Vector2(-249f, -244f),
                new Vector2(160f, 38f), () => ChangeAchievementPage(-1));
            achievementNext = Button(root, "achievement_next", "下一页", new Vector2(249f, -244f),
                new Vector2(160f, 38f), () => ChangeAchievementPage(1));
            achievementMarkSeen = Button(root, "achievements_read_all", "全部已读", new Vector2(0f, -244f),
                new Vector2(286f, 38f), MarkAchievementsSeen, true);
            foreach (Target target in new[] { achievementPrevious, achievementNext, achievementMarkSeen })
            {
                target.label.fontSize = 20;
                target.label.rectTransform.sizeDelta = target.rect.sizeDelta - new Vector2(16f, 2f);
            }
        }

        public void OpenAchievements()
        {
            if (!initialized) return;
            if (!IsOpen) OpenMenu();
            ShowPage(Page.Achievements);
            // Opening, filtering and paging never erase unread records. Only the labelled button below writes them.
        }

        void ChangeAchievementCategory(int value)
        {
            achievementFilter = Mathf.Clamp(value, 0, AchievementCategories.Length - 1);
            achievementPage = 0;
            Refresh(); ResetInput();
        }
        void ChangeAchievementPage(int delta)
        {
            achievementPage = Mathf.Clamp(achievementPage + delta, 0, AchievementPageCount - 1);
            Refresh(); ResetInput();
        }
        void MarkAchievementsSeen()
        {
            if (achievementJournal != null && achievementJournal.Writable && achievementJournal.UnreadCount > 0)
                achievementJournal.MarkAllSeen();
            Refresh(); ResetInput();
        }
        int AchievementPageCount => Math.Max(1, (achievementSelection.Count + AchievementRowsPerPage - 1) / AchievementRowsPerPage);

        string AchievementSummary()
        {
            if (achievementJournal == null) return "成就记录准备中 · 自动解锁并保存在本机";
            int count = achievementJournal.CurrentUnlockedCount;
            string history = achievementJournal.HistoricalUnlockedCount > 0 ? " · 历史荣誉+" + achievementJournal.HistoricalUnlockedCount : "";
            string pagination = "第" + (achievementPage + 1) + "/" + AchievementPageCount + "页";
            if (!achievementJournal.Writable) return (history.Length > 0 ? "记录只读 · 已解锁 " : "记录只读 · 原件保留 · 已解锁 ") + count + "/" + OceanAchievementCatalog.CurrentCount + history + " · " + pagination;
            if (!string.IsNullOrEmpty(achievementJournal.LastError)) return "保存未完成 · 已解锁 " + count + "/" + OceanAchievementCatalog.CurrentCount + history + " · " + pagination;
            if (history.Length > 0) return "已解锁 " + count + "/" + OceanAchievementCatalog.CurrentCount + history + " · " + pagination;
            return "已解锁 " + count + "/" + OceanAchievementCatalog.CurrentCount + history + " · 未读 " + achievementJournal.UnreadCount +
                " · " + AchievementCategories[achievementFilter] + " " + pagination;
        }

        void RefreshAchievements()
        {
            if (achievementPrevious == null) return;
            achievementSelection.Clear();
            string category = AchievementCategories[achievementFilter];
            foreach (OceanAchievementDefinition definition in OceanAchievementCatalog.All)
                if (!definition.Historical &&
                    (achievementFilter == 0 || string.Equals(category, definition.Category, StringComparison.Ordinal))) achievementSelection.Add(definition);
            foreach (OceanAchievementDefinition definition in OceanAchievementCatalog.All)
                if (definition.Historical && achievementJournal != null && achievementJournal.IsUnlocked(definition.Id) &&
                    (achievementFilter == 0 || string.Equals(category, definition.Category, StringComparison.Ordinal))) achievementSelection.Add(definition);
            achievementPage = Mathf.Clamp(achievementPage, 0, AchievementPageCount - 1);
            for (int index = 0; index < achievementFilters.Length; index++)
            {
                bool selected = index == achievementFilter;
                Target target = achievementFilters[index];
                target.normal = selected ? new Color(.16f, .24f, .21f, 1f) : ButtonColor;
                target.label.color = selected ? Gold : Ink;
                target.border.SetState(1f, selected ? .5f : 0f);
            }
            for (int slot = 0; slot < AchievementRowsPerPage; slot++)
            {
                int index = achievementPage * AchievementRowsPerPage + slot;
                bool present = index < achievementSelection.Count;
                achievementCards[slot].gameObject.SetActive(present);
                achievementVisibleIds[slot] = present ? achievementSelection[index].Id : null;
                if (!present) continue;
                OceanAchievementDefinition definition = achievementSelection[index];
                bool unlocked = achievementJournal != null && achievementJournal.IsUnlocked(definition.Id);
                bool unread = achievementJournal != null && achievementJournal.IsUnread(definition.Id);
                long progress = achievementJournal == null ? 0L : achievementJournal.Progress(definition.Id);
                achievementNames[slot].text = (definition.Historical ? "历史荣誉" : definition.Category) + " · " + definition.Name;
                achievementNames[slot].color = unlocked ? Gold : Ink;
                achievementDescriptions[slot].text = definition.Description;
                achievementDescriptions[slot].color = Muted;
                achievementStates[slot].text = unlocked ? unread ? "已解锁 · 新" : "已解锁" :
                    OceanCampaignAvailability.IsDeferredAchievement(definition.Id) ? "未开放" : "未解锁";
                achievementStates[slot].color = unlocked ? Gold : Muted;
                achievementNumbers[slot].text = progress + " / " + definition.Target;
                achievementNumbers[slot].color = unlocked ? Gold : Ink;
                achievementCards[slot].color = unlocked ? new Color(.065f, .165f, .17f, .98f) : new Color(.025f, .105f, .135f, .97f);
                achievementFrames[slot].SetAppearance(unlocked ? new Color(1f, .79f, .37f, .8f) : new Color(.39f, .55f, .56f, .6f), 1f, 9f);
                float fraction = definition.Target <= 0L ? 0f : Mathf.Clamp01((float)progress / definition.Target);
                achievementFills[slot].rectTransform.sizeDelta = new Vector2(638f * fraction, 5f);
                achievementFills[slot].color = unlocked ? Gold : new Color(.22f, .71f, .72f, 1f);
                achievementFills[slot].gameObject.SetActive(fraction > 0f);
            }
            SetEnabled(achievementPrevious, achievementPage > 0);
            SetEnabled(achievementNext, achievementPage + 1 < AchievementPageCount);
            int unseen = achievementJournal?.UnreadCount ?? 0;
            bool writable = achievementJournal != null && achievementJournal.Writable;
            achievementMarkSeen.label.text = !writable ? achievementJournal == null ? "记录准备中" : "记录只读" :
                unseen > 0 ? "全部已读（" + unseen + "）" : "暂无未读";
            SetEnabled(achievementMarkSeen, writable && unseen > 0);
        }

#if UNITY_EDITOR
        public int AchievementPageForValidation => achievementPage;
        public string AchievementCategoryForValidation => AchievementCategories[achievementFilter];
        public string[] AchievementVisibleIdsForValidation => (string[])achievementVisibleIds.Clone();
        public int AchievementPageCountForValidation => AchievementPageCount;
        public float AchievementFillForValidation(int slot) => achievementFills[slot].rectTransform.rect.width / 638f;
#endif
    }
}

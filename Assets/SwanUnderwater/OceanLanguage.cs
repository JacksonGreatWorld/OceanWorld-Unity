using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater
{
    public enum OceanLanguageChoice { FollowSystem = 0, SimplifiedChinese = 1, English = 2 }

    /// <summary>Offline presentation preference. Never reads or writes campaign data.</summary>
    public static partial class OceanLanguage
    {
        public const string PreferenceKey = "ocean_language_preference_v1";
        static readonly Dictionary<string,string> EnglishStrings = BuildStrings();
        static readonly List<OceanLocalizedLabel> Labels = new List<OceanLocalizedLabel>();
        static bool initialized;
        static string systemLanguageTag, systemCountry;
        static OceanLanguageChoice preference, resolved;
        static Action changed;
        public static event Action Changed
        {
            add { PruneDestroyedListeners(); changed += value; }
            remove { changed -= value; }
        }
        static void PruneDestroyedListeners()
        {
            if(changed==null)return;
            foreach(Action callback in changed.GetInvocationList())
                if(callback.Target is UnityEngine.Object owner && owner==null)changed-=callback;
        }

        public static OceanLanguageChoice Preference { get { EnsureInitialized(); return preference; } }
        public static OceanLanguageChoice Resolved { get { EnsureInitialized(); return resolved; } }
        public static OceanLanguageChoice SystemDefault { get { EnsureInitialized(); return ResolveDefault(systemLanguageTag,systemCountry); } }
        public static bool IsEnglish => Resolved == OceanLanguageChoice.English;

        static Dictionary<string,string> BuildStrings()
        {
            var values = new Dictionary<string,string>(StringComparer.Ordinal);
            AddUiStrings(values); AddWorldStrings(values); AddDataStrings(values); AddGuideStrings(values);
            AddSettingsStrings(values);
            return values;
        }
        static partial void AddUiStrings(Dictionary<string,string> entries);
        static partial void AddWorldStrings(Dictionary<string,string> entries);
        static partial void AddDataStrings(Dictionary<string,string> entries);
        static partial void AddGuideStrings(Dictionary<string,string> entries);

        /// <summary>Supported system language wins; region is a fallback, not IP location.</summary>
        public static OceanLanguageChoice ResolveDefault(string languageTag, string countryCode)
        {
            string language = (languageTag ?? "").Replace('_','-').Split('-')[0].ToLowerInvariant();
            if (language == "zh") return OceanLanguageChoice.SimplifiedChinese;
            if (language == "en") return OceanLanguageChoice.English;
            return string.Equals(countryCode, "CN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(countryCode, "CHN", StringComparison.OrdinalIgnoreCase)
                ? OceanLanguageChoice.SimplifiedChinese : OceanLanguageChoice.English;
        }

        static void EnsureInitialized()
        {
            if (initialized) return;
            ReadSystemLocale(out systemLanguageTag, out systemCountry);
            int saved = PlayerPrefs.GetInt(PreferenceKey, 0);
            preference = saved >= 0 && saved <= 2 ? (OceanLanguageChoice)saved : OceanLanguageChoice.FollowSystem;
            resolved = preference == OceanLanguageChoice.FollowSystem ? ResolveDefault(systemLanguageTag, systemCountry) : preference;
            initialized = true;
        }

        static void ReadSystemLocale(out string language, out string country)
        {
            language = ""; country = "";
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var type = new AndroidJavaClass("java.util.Locale"))
                using (var locale = type.CallStatic<AndroidJavaObject>("getDefault"))
                { language = locale.Call<string>("getLanguage") ?? ""; country = locale.Call<string>("getCountry") ?? ""; }
            }
            catch (Exception) { Debug.LogWarning("[OceanLanguage] OS locale unavailable; using Unity language fallback."); }
#else
            // Culture is local OS configuration. No location permission or network lookup.
            try
            {
                language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                var culture = CultureInfo.CurrentCulture;
                if (!culture.IsNeutralCulture && culture.Name.Length != 0)
                    country = new RegionInfo(culture.Name).TwoLetterISORegionName;
            }
            catch (ArgumentException) { }
#endif
            if (string.IsNullOrEmpty(language) || language == "iv")
            {
                var system = Application.systemLanguage;
                language = system == SystemLanguage.Chinese || system == SystemLanguage.ChineseSimplified ||
                    system == SystemLanguage.ChineseTraditional ? "zh" : system == SystemLanguage.English ? "en" : "";
            }
        }

        public static void SetPreference(OceanLanguageChoice choice, bool persist = true)
        {
            if ((int)choice < 0 || (int)choice > 2) throw new ArgumentOutOfRangeException(nameof(choice));
            EnsureInitialized();
            bool changed = preference != choice;
            preference = choice;
            var next = choice == OceanLanguageChoice.FollowSystem ? ResolveDefault(systemLanguageTag, systemCountry) : choice;
            changed |= next != resolved; resolved = next;
            if (persist) { PlayerPrefs.SetInt(PreferenceKey, (int)choice); PlayerPrefs.Save(); }
            if (changed) NotifyChanged();
        }

        public static void RefreshSystemDefault()
        {
            EnsureInitialized();
            var previousDefault=ResolveDefault(systemLanguageTag,systemCountry);
            ReadSystemLocale(out systemLanguageTag, out systemCountry);
            var next = ResolveDefault(systemLanguageTag, systemCountry);
            if (preference != OceanLanguageChoice.FollowSystem)
            { if(previousDefault!=next)NotifyChanged();return; }
            if (next == resolved) return;
            resolved = next; NotifyChanged();
        }

        static void NotifyChanged()
        {
            // Translate authored labels first. Presenters then redraw dynamic values from state.
            for (int i = Labels.Count - 1; i >= 0; i--)
            { if (Labels[i] == null) Labels.RemoveAt(i); else Labels[i].RefreshLanguage(); }
            // Edit-mode tools and never-awakened objects do not always receive
            // OnDestroy. Never dispatch into their surviving managed wrappers.
            PruneDestroyedListeners();
            changed?.Invoke();
        }
        public static string Text(string chinese)
        {
            if (string.IsNullOrEmpty(chinese) || !IsEnglish) return chinese ?? "";
            return EnglishStrings.TryGetValue(chinese, out var translated) ? translated : chinese;
        }
        public static string Format(string chineseTemplate, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, Text(chineseTemplate), args);
        public static string Choose(string chinese, string english) => IsEnglish ? english : chinese;

        public static void Bind(Text label, string chineseSource)
        {
            if (label == null) return;
            var binding = label.GetComponent<OceanLocalizedLabel>();
            if (binding == null) binding = label.gameObject.AddComponent<OceanLocalizedLabel>();
            binding.SetSource(label, chineseSource ?? "");
        }
        static void PruneDestroyedLabels()
        { for(int i=Labels.Count-1;i>=0;i--)if(Labels[i]==null)Labels.RemoveAt(i); }
        internal static void Register(OceanLocalizedLabel label)
        {
            // Never-opened inactive pages may not receive OnDestroy. Bound growth
            // across UI rebuilds without polling the hierarchy each frame.
            PruneDestroyedLabels();
            if (!Labels.Contains(label)) Labels.Add(label);
        }
        internal static void Unregister(OceanLocalizedLabel label) => Labels.Remove(label);

#if UNITY_EDITOR
        public static IReadOnlyDictionary<string,string> EnglishStringsForValidation => EnglishStrings;
        public static int SubscriberCountForValidation { get { PruneDestroyedListeners(); return changed == null ? 0 : changed.GetInvocationList().Length; } }
        public static int BoundLabelCountForValidation { get { PruneDestroyedLabels(); return Labels.Count; } }
        public static void ReloadForValidation(string languageTag,string country)
        {
            systemLanguageTag=languageTag;systemCountry=country;
            int saved=PlayerPrefs.GetInt(PreferenceKey,0);
            preference=saved>=0&&saved<=2?(OceanLanguageChoice)saved:OceanLanguageChoice.FollowSystem;
            resolved=preference==OceanLanguageChoice.FollowSystem?ResolveDefault(languageTag,country):preference;
            initialized=true;NotifyChanged();
        }
        public static IDisposable OverrideForValidation(OceanLanguageChoice choice, string languageTag = "zh", string country = "CN")
        {
            EnsureInitialized();
            var scope = new ValidationScope(preference, resolved, systemLanguageTag, systemCountry);
            systemLanguageTag = languageTag; systemCountry = country;
            SetPreference(choice, false); return scope;
        }
        sealed class ValidationScope : IDisposable
        {
            readonly OceanLanguageChoice oldPreference, oldResolved; readonly string oldLanguage, oldCountry;
            bool disposed;
            public ValidationScope(OceanLanguageChoice p,OceanLanguageChoice r,string l,string c)
            { oldPreference=p;oldResolved=r;oldLanguage=l;oldCountry=c; }
            public void Dispose()
            {
                if (disposed) return; disposed=true;
                preference=oldPreference;resolved=oldResolved;systemLanguageTag=oldLanguage;systemCountry=oldCountry;
                NotifyChanged();
            }
        }
#endif
    }
}

// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Foundation/HSettings.cs (ModuleId → section id, Money → Silver as int, tier anchors dropped).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using Verse;

namespace JBKit
{
    /// <summary>LogFloat: a positive number on a log-scale slider (stored as the plain number). Silver: whole silver, hard max 10 × silver stack limit.</summary>
    internal enum KitSettingKind { Bool, Int, Float, Percent, LogFloat, Enum, Silver }

    /// <summary>
    /// One tunable value, registered once from code (<see cref="KitRegistry"/>). Values are floats in a <see cref="KitStore"/>:
    /// per save (KitProfile of the game) or, for <see cref="Global"/> settings and mods without per-save, in the template.
    /// Translation keys: &lt;Prefix&gt;.Setting.&lt;Key&gt; and &lt;Prefix&gt;.Setting.&lt;Key&gt;.Desc, categories &lt;Prefix&gt;.SettingCat.&lt;Cat&gt;,
    /// enum values &lt;Prefix&gt;.Enum.&lt;EnumType&gt;.&lt;Name&gt;. English text in code is the fallback.
    /// </summary>
    internal sealed class KitSetting
    {
        public string Key;                 // "lo.regrowDays": lowercase mod or section prefix + dot + camelCase
        public string Section;             // section id (KitSections.Core for general settings)
        public string Category;            // UI group inside the section page, e.g. "Regrowth"
        public KitSettingKind Kind;
        public float Default;
        public float SliderMin, SliderMax;
        public float HardMin = float.MinValue, HardMax = float.MaxValue;
        public Func<float> DynamicHardMax;
        public string HardLimitReason;     // shown when the player hits a technical limit
        public bool Nerd;                  // only shown with "Show nerd settings"
        public bool Global;                // per installation, never per save (UI preferences)
        public Type EnumType;
        public string LabelEn, DescEn;     // English fallback
        public float Step;                 // slider rounding; 0 = automatic
        public int Order;                  // registration order (rows are drawn in this order)
        /// <summary>Older store key this setting replaces: if only the old key is stored, its value is converted with <see cref="LegacyToValue"/>.</summary>
        public string LegacyKey;
        public Func<float, float> LegacyToValue;
        /// <summary>Name of the mod's OLD Scribe_Values field in the settings file. Read once (as string) when the store has no value yet. See KitSettingsBase.</summary>
        public string LegacyScribe;
        /// <summary>Called (deferred, once per flush) after the value changed. Gets the new value.</summary>
        public Action<float> OnChanged;
        /// <summary>The change only takes effect after a restart (the row shows a small tag).</summary>
        public bool Restart;
        /// <summary>Section id or mod packageId that must be active, else the row is greyed with <see cref="RequiresText"/>.</summary>
        public string Requires;
        public string RequiresText;

        public bool IsLog => Kind == KitSettingKind.LogFloat;
        float LogLo => Math.Max(1e-6f, SliderMin);
        public float ToSlider(float v)
        {
            v = Math.Max(SliderMin, Math.Min(SliderMax, v));
            return IsLog ? (float)Math.Log(Math.Max(LogLo, v)) : v;
        }
        public float FromSlider(float x)
        {
            if (!IsLog) return x;
            double v = Math.Exp(x); if (v <= 0) return SliderMin;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(v)) - 2);
            return (float)Math.Max(SliderMin, Math.Min(SliderMax, Math.Round(v / mag) * mag));
        }
        public float SliderLo => IsLog ? (float)Math.Log(LogLo) : SliderMin;
        public float SliderHi => IsLog ? (float)Math.Log(Math.Max(SliderMax, LogLo * 1.001f)) : SliderMax;

        /// <summary>Value stored in <paramref name="st"/> (also through the legacy key), clamped; null if none.</summary>
        public float? StoredIn(KitStore st)
        {
            if (st == null) return null;
            if (st.TryGet(Key, out float v)) return Clamp(v);
            if (LegacyKey != null && LegacyToValue != null && st.TryGet(LegacyKey, out float lv))
            {
                try { return Clamp(LegacyToValue(lv)); } catch { return null; }
            }
            return null;
        }
        public bool IsStoredIn(KitStore st) => st != null && (st.Has(Key) || (LegacyKey != null && st.Has(LegacyKey)));

        public string Label => KitText.Key("Setting." + Key).TranslateOr(LabelEn ?? Key);
        public string Desc => KitText.Key("Setting." + Key + ".Desc").TranslateOr(DescEn ?? "");
        public string CategoryLabel => CatLabel(Category);
        public static string CatLabel(string cat) => string.IsNullOrEmpty(cat) ? "" : KitText.Key("SettingCat." + CatKey(cat)).TranslateOr(cat);
        public static string CatKey(string cat)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in cat ?? "") if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        public float EffectiveHardMax
        {
            get
            {
                if (DynamicHardMax != null) { try { return Math.Min(HardMax, DynamicHardMax()); } catch { } }
                return HardMax;
            }
        }

        public float Clamp(float v)
        {
            if (Kind == KitSettingKind.Bool) return v >= 0.5f ? 1f : 0f;
            if (float.IsNaN(v) || float.IsInfinity(v)) v = Default;
            v = Math.Max(HardMin, Math.Min(EffectiveHardMax, v));
            if (Kind == KitSettingKind.Int || Kind == KitSettingKind.Enum || Kind == KitSettingKind.Silver) v = (float)Math.Round(v);
            return v;
        }

        /// <summary>True when <see cref="Requires"/> is met (or not set).</summary>
        public bool RequiresMet
        {
            get
            {
                if (string.IsNullOrEmpty(Requires)) return true;
                if (KitSections.Find(Requires) != null) return KitSections.On(Requires);
                return KitCompat.IsModActive(Requires);
            }
        }

        /// <summary>Why the row is greyed (null when <see cref="RequiresMet"/>).</summary>
        public string RequiresReason
        {
            get
            {
                if (RequiresMet) return null;
                if (!string.IsNullOrEmpty(RequiresText)) return RequiresText;
                var sec = KitSections.Find(Requires);
                if (sec != null) return KitText.K("Row.RequiresSection", "Needs the section \"{0}\" to be on.", sec.Label);
                return KitText.K("Row.RequiresMod", "Needs {0}.", Requires);
            }
        }

        /// <summary>Player-facing value text (invariant culture numbers, translated On/Off and enum names).</summary>
        public string Format(float v)
        {
            switch (Kind)
            {
                case KitSettingKind.Bool: return v >= 0.5f ? KitText.K("On", "On") : KitText.K("Off", "Off");
                case KitSettingKind.Percent: return (v * 100f).ToString("0.#", CultureInfo.InvariantCulture) + " %";
                case KitSettingKind.Int: return ((long)Math.Round(v)).ToString("#,##0", CultureInfo.InvariantCulture);
                case KitSettingKind.Silver: return KitText.K("Silver", "{0} silver", ((long)Math.Round(v)).ToString("#,##0", CultureInfo.InvariantCulture));
                case KitSettingKind.Enum:
                    if (EnumType != null)
                    {
                        string name = System.Enum.GetName(EnumType, (int)Math.Round(v));
                        if (name != null) return KitText.Key("Enum." + EnumType.Name + "." + name).TranslateOr(GenText.SplitCamelCase(name));
                    }
                    return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
                case KitSettingKind.LogFloat: return v.ToString(v >= 100 ? "#,##0" : v >= 10 ? "0.#" : v >= 1 ? "0.##" : "0.###", CultureInfo.InvariantCulture);
                default: return v.ToString(Math.Abs(v) >= 100 ? "#,##0.#" : "0.###", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Parses an old settings-file value (True/False, int, float in invariant culture, enum name). False if it cannot.</summary>
        public bool TryParseLegacy(string raw, out float value)
        {
            value = 0f;
            if (raw == null) return false;
            raw = raw.Trim();
            if (raw.Length == 0) return false;
            if (bool.TryParse(raw, out bool b)) { value = b ? 1f : 0f; return true; }
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && !float.IsNaN(f) && !float.IsInfinity(f)) { value = Clamp(f); return true; }
            if (Kind == KitSettingKind.Enum && EnumType != null)
            {
                try { value = Clamp(Convert.ToInt32(System.Enum.Parse(EnumType, raw, true))); return true; } catch { }
            }
            return false;
        }

        // Fluent modifiers (return this) -----------------------------------------------------------------------------
        public KitSetting AsGlobal() { Global = true; return this; }
        public KitSetting AsNerd() { Nerd = true; return this; }
        public KitSetting NeedsRestart() { Restart = true; return this; }
        public KitSetting WithStep(float step) { Step = step; return this; }
        public KitSetting WithLimit(string reasonEn, Func<float> dynamicHardMax = null) { HardLimitReason = reasonEn; if (dynamicHardMax != null) DynamicHardMax = dynamicHardMax; return this; }
        public KitSetting Legacy(string oldScribeName) { LegacyScribe = oldScribeName; return this; }
        public KitSetting LegacyStoreKey(string oldKey, Func<float, float> convert) { LegacyKey = oldKey; LegacyToValue = convert; return this; }
        public KitSetting OnChange(Action<float> a) { OnChanged = a; return this; }
        public KitSetting RequiresOn(string sectionOrPackageId, string reasonEn = null) { Requires = sectionOrPackageId; RequiresText = reasonEn; return this; }
    }

    /// <summary>Registry and value access of all settings of this mod.</summary>
    internal static class KitSettings
    {
        static readonly Dictionary<string, KitSetting> byKey = new Dictionary<string, KitSetting>();
        /// <summary>All registrations in registration order.</summary>
        public static readonly List<KitSetting> All = new List<KitSetting>();

        public static KitSetting Add(KitSetting s)
        {
            if (s == null || string.IsNullOrEmpty(s.Key)) return null;
            if (byKey.TryGetValue(s.Key, out var existing))
            {
                KitLog.Once("dupSetting:" + s.Key, KitLogLevel.Warn, "Settings", "Setting " + s.Key + " registered twice; the first registration wins");
                return existing;
            }
            if (s.Section == null) s.Section = KitSections.Core;
            if (s.SliderMax <= s.SliderMin && s.Kind != KitSettingKind.Bool) { s.SliderMin = Math.Min(0, s.Default); s.SliderMax = Math.Max(1, s.Default * 4); }
            s.Order = All.Count;
            byKey[s.Key] = s; All.Add(s);
            return s;
        }

        public static KitSetting Find(string key) => key != null && byKey.TryGetValue(key, out var s) ? s : null;
        public static IEnumerable<KitSetting> OfSection(string section) => All.Where(s => s.Section == section);

        // Stores -----------------------------------------------------------------------------------------------------
        /// <summary>The store a setting lives in for <paramref name="target"/>: the template for global settings, else the target profile.</summary>
        public static KitStore StoreFor(KitSetting s, KitProfile target)
        {
            if (s.Global || target == null) return Kit.Template.Store;
            return target.Store;
        }

        /// <summary>Effective value against an explicit profile (the window edits the save or the template).</summary>
        public static float Value(KitSetting s, KitProfile target)
        {
            var sv = s.StoredIn(StoreFor(s, target));
            return sv ?? s.Clamp(s.Default);
        }
        public static bool IsOverridden(KitSetting s, KitProfile target) => s.IsStoredIn(StoreFor(s, target));

        public static void Set(KitSetting s, KitProfile target, float v)
        {
            if (s.Global && Kit.Comparing) return;   // preset comparison on a scratch profile must not touch the template
            var st = StoreFor(s, target);
            float nv = s.Clamp(v);
            float old = Value(s, target);
            st.Set(s.Key, nv);
            if (s.LegacyKey != null) st.Remove(s.LegacyKey);
            if (Math.Abs(old - nv) > 1e-7f) Kit.MarkChanged(s.Key);
        }

        public static void Reset(KitSetting s, KitProfile target)
        {
            if (s.Global && Kit.Comparing) return;
            var st = StoreFor(s, target);
            float old = Value(s, target);
            st.Remove(s.Key);
            if (s.LegacyKey != null) st.Remove(s.LegacyKey);
            st.freeText.Remove(s.Key);
            if (Math.Abs(old - Value(s, target)) > 1e-7f) Kit.MarkChanged(s.Key);
        }

        /// <summary>Resets every non-global setting of a section in <paramref name="target"/>.</summary>
        public static void ResetSection(string section, KitProfile target)
        {
            foreach (var s in OfSection(section).ToList()) if (!s.Global) Reset(s, target);
        }

        // Reading against the current profile (save if per-save and a game runs, else template) ---------------------
        public static float Get(string key)
        {
            var s = Find(key);
            if (s == null) { KitLog.Once("missingSetting:" + key, KitLogLevel.Error, "Settings", "Unknown setting key " + key + " (not registered)"); return 0f; }
            return Value(s, Kit.Current);
        }
        public static float Get(KitSetting s) => Value(s, Kit.Current);
        public static bool GetBool(string key) => Get(key) >= 0.5f;
        public static int GetInt(string key) => (int)Math.Round(Get(key));
        public static T GetEnum<T>(string key) where T : struct => (T)System.Enum.ToObject(typeof(T), GetInt(key));
        public static int GetSilver(string key) => (int)Math.Round(Get(key));

        public static void Set(string key, float v) { var s = Find(key); if (s != null) Set(s, Kit.Current, v); else KitLog.Once("missingSettingSet:" + key, KitLogLevel.Error, "Settings", "Set: unknown setting key " + key); }
        public static void Reset(string key) { var s = Find(key); if (s != null) Reset(s, Kit.Current); }

        /// <summary>Tests only: forgets every registration (the next mod start registers again).</summary>
        internal static void ClearForTests() { byKey.Clear(); All.Clear(); }
    }

    /// <summary>Key → value store, saved as two parallel lists (stable across versions; unknown keys are kept).</summary>
    internal sealed class KitStore : IExposable
    {
        Dictionary<string, float> values = new Dictionary<string, float>();
        /// <summary>Rows switched to free number entry (UI state, saved with the store).</summary>
        public Dictionary<string, bool> freeText = new Dictionary<string, bool>();

        public bool TryGet(string k, out float v) => values.TryGetValue(k, out v);
        public bool Has(string k) => values.ContainsKey(k);
        public void Set(string k, float v) => values[k] = v;
        public void Remove(string k) => values.Remove(k);
        public int Count => values.Count;
        public IEnumerable<string> Keys => values.Keys;
        public KitStore Clone()
        {
            var c = new KitStore();
            foreach (var kv in values) c.values[kv.Key] = kv.Value;
            foreach (var kv in freeText) c.freeText[kv.Key] = kv.Value;
            return c;
        }

        List<string> tmpK; List<float> tmpV; List<string> tmpFK; List<bool> tmpFV;
        public void ExposeData()
        {
            Scribe_Collections.Look(ref values, "values", LookMode.Value, LookMode.Value, ref tmpK, ref tmpV);
            Scribe_Collections.Look(ref freeText, "freeText", LookMode.Value, LookMode.Value, ref tmpFK, ref tmpFV);
            if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (values == null) values = new Dictionary<string, float>();
                if (freeText == null) freeText = new Dictionary<string, bool>();
            }
        }
    }
}

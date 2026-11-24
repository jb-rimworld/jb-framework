// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Foundation/HSettings.cs (ModuleId → section id, Money → Silver as int, tier anchors dropped); one registry per mod.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using Verse;

namespace JBFramework
{
    /// <summary>LogFloat: a positive number on a log-scale slider (stored as the plain number). Silver: whole silver, hard max 10 × silver stack limit.</summary>
    internal enum KitSettingKind { Bool, Int, Float, Percent, LogFloat, Enum, Silver }

    /// <summary>
    /// One tunable value, registered once from code (<see cref="KitRegistry"/>). Values are floats, stored per save (the
    /// <see cref="KitProfile"/> of the game) or, for global settings and mods without per-save, in the template (settings file).
    /// Translation keys: &lt;Prefix&gt;.Setting.&lt;Key&gt; and &lt;Prefix&gt;.Setting.&lt;Key&gt;.Desc, categories &lt;Prefix&gt;.SettingCat.&lt;Cat&gt;,
    /// enum values &lt;Prefix&gt;.Enum.&lt;EnumType&gt;.&lt;Name&gt;. English text in code is the fallback.
    /// </summary>
    public sealed class KitSetting
    {
        internal KitSetting() { }
        internal KitMod Owner;
        internal string Category;            // UI group inside the section page, e.g. "Regrowth"
        internal KitSettingKind Kind;
        internal float Default;
        internal float SliderMin, SliderMax;
        internal float HardMin = float.MinValue, HardMax = float.MaxValue;
        internal Func<float> DynamicHardMax;
        internal string HardLimitReason;     // shown when the player hits a technical limit
        internal bool Nerd;                  // only shown with "Show nerd settings"
        internal bool Global;                // per installation, never per save (UI preferences)
        internal Type EnumType;
        internal string LabelEn, DescEn;     // English fallback
        internal float Step;                 // slider rounding; 0 = automatic
        internal int Order;                  // registration order (rows are drawn in this order)
        /// <summary>Older store key this setting replaces: if only the old key is stored, its value is converted with LegacyToValue.</summary>
        internal string LegacyKey;
        internal Func<float, float> LegacyToValue;
        /// <summary>Name of the mod's OLD Scribe_Values field in the settings file. Read once (as string) when the store has no value yet. See KitSettingsBase.</summary>
        internal string LegacyScribe;
        internal Func<float, float> LegacyScribeConvert;
        /// <summary>Called (deferred, once per flush) after the value changed. Gets the new value.</summary>
        internal Action<float> OnChanged;
        /// <summary>The change only takes effect after a restart (the row shows a small tag).</summary>
        internal bool Restart;
        /// <summary>Section id or mod packageId that must be active, else the row is greyed with RequiresText.</summary>
        internal string Requires;
        internal string RequiresText;

        /// <summary>The setting's key ("lo.regrowDays"): lower-case mod or area prefix, a dot, camelCase.</summary>
        public string Key { get; internal set; }
        /// <summary>The section the setting belongs to (<see cref="KitMod.Core"/> for general settings).</summary>
        public string SectionId { get; internal set; }
        /// <summary>Translated label (key &lt;Prefix&gt;.Setting.&lt;Key&gt;), English fallback.</summary>
        public string Label => Owner.Key("Setting." + Key).TranslateOr(LabelEn ?? Key);
        /// <summary>Translated description (key &lt;Prefix&gt;.Setting.&lt;Key&gt;.Desc), English fallback.</summary>
        public string Desc => Owner.Key("Setting." + Key + ".Desc").TranslateOr(DescEn ?? "");

        internal bool IsLog => Kind == KitSettingKind.LogFloat;
        float LogLo => Math.Max(1e-6f, SliderMin);
        internal float ToSlider(float v)
        {
            v = Math.Max(SliderMin, Math.Min(SliderMax, v));
            return IsLog ? (float)Math.Log(Math.Max(LogLo, v)) : v;
        }
        internal float FromSlider(float x)
        {
            if (!IsLog) return x;
            double v = Math.Exp(x); if (v <= 0) return SliderMin;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(v)) - 2);
            return (float)Math.Max(SliderMin, Math.Min(SliderMax, Math.Round(v / mag) * mag));
        }
        internal float SliderLo => IsLog ? (float)Math.Log(LogLo) : SliderMin;
        internal float SliderHi => IsLog ? (float)Math.Log(Math.Max(SliderMax, LogLo * 1.001f)) : SliderMax;

        /// <summary>Value stored in <paramref name="st"/> (also through the legacy key), clamped; null if none.</summary>
        internal float? StoredIn(KitStore st)
        {
            if (st == null) return null;
            if (st.TryGet(Key, out float v)) return Clamp(v);
            if (LegacyKey != null && LegacyToValue != null && st.TryGet(LegacyKey, out float lv))
            {
                try { return Clamp(LegacyToValue(lv)); } catch { return null; }
            }
            return null;
        }
        internal bool IsStoredIn(KitStore st) => st != null && (st.Has(Key) || (LegacyKey != null && st.Has(LegacyKey)));

        internal string CategoryLabel => CatLabel(Owner, Category);
        internal static string CatLabel(KitMod k, string cat) => string.IsNullOrEmpty(cat) ? "" : k.Key("SettingCat." + CatKey(cat)).TranslateOr(cat);
        internal static string CatKey(string cat)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in cat ?? "") if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        internal float EffectiveHardMax
        {
            get
            {
                if (DynamicHardMax != null) { try { return Math.Min(HardMax, DynamicHardMax()); } catch { } }
                return HardMax;
            }
        }

        internal float Clamp(float v)
        {
            if (Kind == KitSettingKind.Bool) return v >= 0.5f ? 1f : 0f;
            if (float.IsNaN(v) || float.IsInfinity(v)) v = Default;
            v = Math.Max(HardMin, Math.Min(EffectiveHardMax, v));
            if (Kind == KitSettingKind.Int || Kind == KitSettingKind.Enum || Kind == KitSettingKind.Silver) v = (float)Math.Round(v);
            return v;
        }

        /// <summary>True when <see cref="Requires"/> is met (or not set).</summary>
        internal bool RequiresMet
        {
            get
            {
                if (string.IsNullOrEmpty(Requires)) return true;
                if (Owner.Sections.Find(Requires) != null) return Owner.On(Requires);
                return KitCompat.IsModActive(Requires);
            }
        }

        /// <summary>Why the row is greyed (null when <see cref="RequiresMet"/>).</summary>
        internal string RequiresReason
        {
            get
            {
                if (RequiresMet) return null;
                if (!string.IsNullOrEmpty(RequiresText)) return RequiresText;
                var sec = Owner.Sections.Find(Requires);
                if (sec != null) return KitText.K("Row.RequiresSection", "Needs the section \"{0}\" to be on.", sec.Label);
                return KitText.K("Row.RequiresMod", "Needs {0}.", Requires);
            }
        }

        /// <summary>Player-facing value text (invariant culture numbers, translated On/Off and enum names).</summary>
        internal string Format(float v)
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
                        if (name != null) return Owner.Key("Enum." + EnumType.Name + "." + name).TranslateOr(GenText.SplitCamelCase(name));
                    }
                    return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
                case KitSettingKind.LogFloat: return v.ToString(v >= 100 ? "#,##0" : v >= 10 ? "0.#" : v >= 1 ? "0.##" : "0.###", CultureInfo.InvariantCulture);
                default: return v.ToString(Math.Abs(v) >= 100 ? "#,##0.#" : "0.###", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Parses an old settings-file value (True/False, int, float in invariant culture, enum name). False if it cannot.</summary>
        internal bool TryParseLegacy(string raw, out float value, bool clamp = true)
        {
            value = 0f;
            if (raw == null) return false;
            raw = raw.Trim();
            if (raw.Length == 0) return false;
            if (bool.TryParse(raw, out bool b)) { value = b ? 1f : 0f; return true; }
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && !float.IsNaN(f) && !float.IsInfinity(f)) { value = clamp ? Clamp(f) : f; return true; }
            if (Kind == KitSettingKind.Enum && EnumType != null)
            {
                try { value = Clamp(Convert.ToInt32(System.Enum.Parse(EnumType, raw, true))); return true; } catch { }
            }
            return false;
        }

        // Fluent modifiers (return this) -----------------------------------------------------------------------------
        /// <summary>A preference of the installation: the same in every save, stored in the settings file (UI preferences).</summary>
        public KitSetting AsGlobal() { Global = true; return this; }
        /// <summary>Only shown with "Show nerd settings".</summary>
        public KitSetting AsNerd() { Nerd = true; return this; }
        /// <summary>The change takes effect only after a restart (the row shows a small tag).</summary>
        public KitSetting NeedsRestart() { Restart = true; return this; }
        /// <summary>Slider rounding step (0 = automatic).</summary>
        public KitSetting WithStep(float step) { Step = step; return this; }
        /// <summary>Why the hard limit exists (shown when the player hits it).</summary>
        public KitSetting WithLimit(string reasonEn) { HardLimitReason = reasonEn; return this; }
        /// <summary>Why the hard limit exists and a maximum computed at runtime (the lower of it and the hard maximum wins).</summary>
        public KitSetting WithLimit(string reasonEn, Func<float> dynamicHardMax) { HardLimitReason = reasonEn; if (dynamicHardMax != null) DynamicHardMax = dynamicHardMax; return this; }
        /// <summary>The range a typed value may have (the slider range stays). Values outside are clamped on read and write.</summary>
        public KitSetting HardLimits(float min, float max) { HardMin = Math.Min(min, max); HardMax = Math.Max(min, max); return this; }
        /// <summary>Description (tooltip and search text; key &lt;Prefix&gt;.Setting.&lt;Key&gt;.Desc).</summary>
        public KitSetting Describe(string desc) { DescEn = desc; return this; }
        /// <summary>A group header on the section page ("Speed", "Costs"); rows of one group are drawn together under it. Key &lt;Prefix&gt;.SettingCat.&lt;Group&gt;.</summary>
        public KitSetting InGroup(string group) { Category = group; return this; }
        /// <summary>The mod's old Scribe_Values field name in the settings file: read once, when the store has no value yet.</summary>
        public KitSetting Legacy(string oldScribeName) { LegacyScribe = oldScribeName; return this; }
        /// <summary>Like <see cref="Legacy(string)"/>, with a conversion of the old number (old percent 25 → fraction: <c>v =&gt; v / 100f</c>).</summary>
        public KitSetting Legacy(string oldScribeName, Func<float, float> convert) { LegacyScribe = oldScribeName; LegacyScribeConvert = convert; return this; }
        /// <summary>An older store key this setting replaces, with a conversion of its value.</summary>
        public KitSetting LegacyStoreKey(string oldKey, Func<float, float> convert) { LegacyKey = oldKey; LegacyToValue = convert; return this; }
        /// <summary>Called (deferred, once per frame) after the value changed; gets the new value. Fenced.</summary>
        public KitSetting OnChange(Action<float> onChanged) { OnChanged = onChanged; return this; }
        /// <summary>Greys the row unless a section of this mod is on or a mod/DLC is active.</summary>
        public KitSetting RequiresOn(string sectionOrPackageId) { Requires = sectionOrPackageId; RequiresText = null; return this; }
        /// <summary>Greys the row unless a section of this mod is on or a mod/DLC is active; <paramref name="reasonEn"/> says why.</summary>
        public KitSetting RequiresOn(string sectionOrPackageId, string reasonEn) { Requires = sectionOrPackageId; RequiresText = reasonEn; return this; }
    }

    /// <summary>Registry and value access of all settings of one mod.</summary>
    internal sealed class KitSettingRegistry
    {
        readonly KitMod k;
        readonly Dictionary<string, KitSetting> byKey = new Dictionary<string, KitSetting>();
        /// <summary>All registrations in registration order.</summary>
        public readonly List<KitSetting> All = new List<KitSetting>();
        public KitSettingRegistry(KitMod owner) { k = owner; }

        public KitSetting Add(KitSetting s)
        {
            if (s == null || string.IsNullOrEmpty(s.Key)) return null;
            if (byKey.TryGetValue(s.Key, out var existing))
            {
                k.Log.Once("dupSetting:" + s.Key, KitLogLevel.Warn, "Settings", "Setting " + s.Key + " registered twice; the first registration wins");
                return existing;
            }
            s.Owner = k;
            if (s.SectionId == null) s.SectionId = KitMod.Core;
            if (s.SliderMax <= s.SliderMin && s.Kind != KitSettingKind.Bool) { s.SliderMin = Math.Min(0, s.Default); s.SliderMax = Math.Max(1, s.Default * 4); }
            s.Order = All.Count;
            byKey[s.Key] = s; All.Add(s);
            return s;
        }

        public KitSetting Find(string key) => key != null && byKey.TryGetValue(key, out var s) ? s : null;
        public IEnumerable<KitSetting> OfSection(string section) => All.Where(s => s.SectionId == section);

        /// <summary>The store a setting lives in for <paramref name="target"/>: the template for global settings, else the target profile.</summary>
        public KitStore StoreFor(KitSetting s, KitProfile target)
        {
            if (s.Global || target == null) return k.Template.Store;
            return target.Store;
        }

        /// <summary>Effective value against an explicit profile (the window edits the save or the template).</summary>
        public float Value(KitSetting s, KitProfile target)
        {
            var sv = s.StoredIn(StoreFor(s, target));
            return sv ?? s.Clamp(s.Default);
        }
        public bool IsOverridden(KitSetting s, KitProfile target) => s.IsStoredIn(StoreFor(s, target));

        public void Set(KitSetting s, KitProfile target, float v)
        {
            if (s.Global && k.Comparing) return;   // preset comparison on a scratch profile must not touch the template
            var st = StoreFor(s, target);
            float nv = s.Clamp(v);
            float old = Value(s, target);
            st.Set(s.Key, nv);
            if (s.LegacyKey != null) st.Remove(s.LegacyKey);
            if (Math.Abs(old - nv) > 1e-7f) k.MarkChanged(s.Key);
        }

        public void Reset(KitSetting s, KitProfile target)
        {
            if (s.Global && k.Comparing) return;
            var st = StoreFor(s, target);
            float old = Value(s, target);
            st.Remove(s.Key);
            if (s.LegacyKey != null) st.Remove(s.LegacyKey);
            st.freeText.Remove(s.Key);
            if (Math.Abs(old - Value(s, target)) > 1e-7f) k.MarkChanged(s.Key);
        }

        /// <summary>Resets every non-global setting of a section in <paramref name="target"/>.</summary>
        public void ResetSection(string section, KitProfile target)
        {
            foreach (var s in OfSection(section).ToList()) if (!s.Global) Reset(s, target);
        }

        // Reading against the current profile ---------------------------------------------------------------------------
        public float Get(string key)
        {
            var s = Find(key);
            if (s == null) { k.Log.Once("missingSetting:" + key, KitLogLevel.Error, "Settings", "Unknown setting key " + key + " (not registered)"); return 0f; }
            return Value(s, k.Current);
        }

        public void Set(string key, float v) { var s = Find(key); if (s != null) Set(s, k.Current, v); else k.Log.Once("missingSettingSet:" + key, KitLogLevel.Error, "Settings", "Set: unknown setting key " + key); }
        public void Reset(string key) { var s = Find(key); if (s != null) Reset(s, k.Current); }
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

// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;

namespace JBFramework
{
    /// <summary>A custom page of a mod's settings window, drawn by the mod (listed in the sidebar after the section pages).</summary>
    public sealed class KitPage
    {
        internal KitPage() { }
        internal KitMod Owner;
        internal string LabelEn;
        internal string Icon;
        internal int Order;
        internal string Section;
        internal Action<Rect> Draw;

        /// <summary>The page id (page id in the window: "page:&lt;Id&gt;").</summary>
        public string Id { get; internal set; }
        /// <summary>Translated label (key &lt;Prefix&gt;.Page.&lt;Id&gt;), English fallback.</summary>
        public string Label => Owner.Key("Page." + Id).TranslateOr(LabelEn ?? Id);
    }

    internal sealed class KitPageRegistry
    {
        readonly KitMod owner;
        public readonly List<KitPage> All = new List<KitPage>();
        public KitPageRegistry(KitMod owner) { this.owner = owner; }
        public KitPage Find(string id) => All.FirstOrDefault(p => p.Id == id);
        public KitPage Add(KitPage p)
        {
            if (p == null || string.IsNullOrEmpty(p.Id) || p.Draw == null) return null;
            var existing = Find(p.Id);
            if (existing != null) return existing;
            p.Owner = owner;
            if (p.Order == 0) p.Order = (All.Count + 1) * 10;
            All.Add(p);
            All.Sort((a, b) => a.Order.CompareTo(b.Order));
            return p;
        }
    }

    /// <summary>
    /// What a mod writes in <see cref="KitModBase.Register"/>. All texts are English fallbacks; translations go into the mod's Keyed
    /// files under &lt;Prefix&gt;.Section.*, &lt;Prefix&gt;.Setting.*, &lt;Prefix&gt;.SettingCat.*, &lt;Prefix&gt;.Preset.*,
    /// &lt;Prefix&gt;.Compat.*, &lt;Prefix&gt;.Page.*, &lt;Prefix&gt;.Enum.* (see API.md). Setting methods take the section id first
    /// (<see cref="KitMod.Core"/> = "core" for general settings) and a category (a group header on the page).
    /// Registering the same id or key twice returns the first registration.
    /// </summary>
    public sealed class KitRegistry
    {
        readonly KitMod k;
        internal KitRegistry(KitMod owner) { k = owner; }

        /// <summary>The mod's context (for callbacks that need it).</summary>
        public KitMod Kit => k;

        // Sections ---------------------------------------------------------------------------------------------------------
        /// <summary>
        /// A switchable feature area. <paramref name="available"/>: is it possible at all (DLC or mod present; null = always);
        /// <paramref name="requires"/>: player text when it is not ("Needs Biotech"); <paramref name="onSwitched"/>: called when the
        /// section of the current profile is switched; <paramref name="diagnosis"/>: extra report lines; <paramref name="prepareRemoval"/>:
        /// undo what the section put into the save (report lines appended); <paramref name="legacyEnabled"/>: old settings field with the switch.
        /// </summary>
        public KitSection Section(string id, string label, string desc = null, string icon = null, bool defaultOn = true,
            Func<bool> available = null, string requires = null, int order = 0, Action<bool> onSwitched = null,
            Func<IEnumerable<string>> diagnosis = null, Action<List<string>> prepareRemoval = null, string legacyEnabled = null)
            => k.Sections.Add(new KitSection
            {
                Id = id, LabelEn = label, DescEn = desc, Icon = icon, DefaultOn = defaultOn, AvailableFunc = available, RequiresText = requires,
                Order = order, OnSwitched = onSwitched, DiagnosisFunc = diagnosis, PrepareRemoval = prepareRemoval, LegacyEnabled = legacyEnabled,
            });

        /// <summary>A section that exists only with a mod or DLC (available = that packageId is active; requires text "Needs &lt;modName&gt;").</summary>
        public KitSection SectionFor(string packageId, string modName, string id, string label, string desc = null, string icon = null, bool defaultOn = true, int order = 0)
            => Section(id, label, desc, icon, defaultOn, () => KitCompat.IsModActive(packageId), "Needs " + modName, order);

        // Settings ---------------------------------------------------------------------------------------------------------
        /// <summary>On/off value. <paramref name="legacyScribe"/>: the mod's old Scribe_Values field name (migrated once).</summary>
        public KitSetting Bool(string section, string cat, string key, bool def, string label, string desc = null, bool nerd = false, string legacyScribe = null)
            => k.SettingsReg.Add(new KitSetting { SectionId = section, Category = cat, Key = key, Kind = KitSettingKind.Bool, Default = def ? 1 : 0, SliderMin = 0, SliderMax = 1, HardMin = 0, HardMax = 1, LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe });

        /// <summary>Whole number: slider <paramref name="min"/>..<paramref name="max"/>, free entry up to <paramref name="hardMin"/>..<paramref name="hardMax"/>.</summary>
        public KitSetting Int(string section, string cat, string key, int def, int min, int max, string label, string desc = null, bool nerd = false, int hardMin = 0, int hardMax = 1_000_000, string legacyScribe = null)
            => k.SettingsReg.Add(new KitSetting { SectionId = section, Category = cat, Key = key, Kind = KitSettingKind.Int, Default = def, SliderMin = min, SliderMax = max, HardMin = hardMin, HardMax = hardMax, LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe });

        /// <summary>Decimal number: slider <paramref name="min"/>..<paramref name="max"/>, free entry up to the hard limits.</summary>
        public KitSetting Float(string section, string cat, string key, float def, float min, float max, string label, string desc = null, bool nerd = false, float hardMin = 0f, float hardMax = 1e6f, string legacyScribe = null)
            => k.SettingsReg.Add(new KitSetting { SectionId = section, Category = cat, Key = key, Kind = KitSettingKind.Float, Default = def, SliderMin = min, SliderMax = max, HardMin = hardMin, HardMax = hardMax, LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe });

        /// <summary>Fraction stored as 0..1, shown and typed as percent.</summary>
        public KitSetting Percent(string section, string cat, string key, float def, float min, float max, string label, string desc = null, bool nerd = false, float hardMin = 0f, float hardMax = 1f, string legacyScribe = null)
            => k.SettingsReg.Add(new KitSetting { SectionId = section, Category = cat, Key = key, Kind = KitSettingKind.Percent, Default = def, SliderMin = min, SliderMax = max, HardMin = hardMin, HardMax = hardMax, LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe });

        /// <summary>Positive number on a log-scale slider (multipliers 0.1 … 100).</summary>
        public KitSetting LogFloat(string section, string cat, string key, float def, float min, float max, string label, string desc = null, bool nerd = false, float hardMin = 0f, float hardMax = 1e6f, string legacyScribe = null)
            => k.SettingsReg.Add(new KitSetting { SectionId = section, Category = cat, Key = key, Kind = KitSettingKind.LogFloat, Default = def, SliderMin = min, SliderMax = max, HardMin = hardMin, HardMax = hardMax, LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe });

        /// <summary>One of the values of an enum, shown as a dropdown (labels: &lt;Prefix&gt;.Enum.&lt;EnumType&gt;.&lt;Name&gt;).</summary>
        public KitSetting Enum<T>(string section, string cat, string key, T def, string label, string desc = null, bool nerd = false, string legacyScribe = null) where T : struct, IConvertible
        {
            int max = 0; foreach (var v in System.Enum.GetValues(typeof(T))) max = Math.Max(max, Convert.ToInt32(v));
            return k.SettingsReg.Add(new KitSetting { SectionId = section, Category = cat, Key = key, Kind = KitSettingKind.Enum, EnumType = typeof(T), Default = Convert.ToInt32(def), SliderMin = 0, SliderMax = max, HardMin = 0, HardMax = max, LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe });
        }

        /// <summary>Whole silver. The hard maximum follows 10 × the silver stack limit (keeps the game stable).</summary>
        public KitSetting Silver(string section, string cat, string key, int def, int min, int max, string label, string desc = null, bool nerd = false, string legacyScribe = null)
            => k.SettingsReg.Add(new KitSetting
            {
                SectionId = section, Category = cat, Key = key, Kind = KitSettingKind.Silver, Default = def, SliderMin = min, SliderMax = max, HardMin = 0, HardMax = 2_000_000_000f,
                LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe,
                DynamicHardMax = () => Math.Max(max, (ThingDefOf.Silver?.stackLimit ?? 500) * 10f), HardLimitReason = "10 × the silver stack limit of your game (keeps the game stable)",
            });

        // Presets, compatibility, pages, caretaker, per-save data -------------------------------------------------------------
        /// <summary>
        /// A named set of values shown as a chip on the Overview page and the welcome card. <paramref name="apply"/> changes only what it
        /// names (<see cref="KitProfile.Set(string, float)"/>, <see cref="KitProfile.SetSection"/>). <paramref name="matches"/> (optional)
        /// decides when the chip is highlighted; default: applying it would change nothing.
        /// </summary>
        public KitPreset Preset(string id, string label, string desc, Action<KitProfile> apply, Func<KitProfile, bool> matches = null)
            => k.Presets.Add(new KitPreset { Id = id, LabelEn = label, DescEn = desc, Apply = apply, Matches = matches });

        /// <summary>
        /// A soft integration shown on the Compatibility page. <paramref name="packageIdOrDlc"/>: the other mod's packageId (DLCs:
        /// "ludeon.rimworld.biotech"). Tag its patch classes with [KitCompat(id)] so a failed patch shows up here.
        /// </summary>
        public KitCompatEntry Compat(string name, string packageIdOrDlc, string what, Func<bool> healthy = null)
            => k.Compat.Add(new KitCompatEntry { Name = name, PackageId = packageIdOrDlc, WhatEn = what, Healthy = healthy });

        /// <summary>A known incompatibility: the Compatibility page shows it with the reason and what to do.</summary>
        public KitCompatEntry Incompatible(string name, string packageId, string why)
            => k.Compat.Add(new KitCompatEntry { Name = name, PackageId = packageId, WhatEn = why, Incompatible = true });

        /// <summary>
        /// A custom page drawn by the mod (inside the window's error fence). <paramref name="order"/> below 0 lists it before General;
        /// <paramref name="section"/> dims it while that section is off.
        /// </summary>
        public KitPage Page(string id, string label, string icon = null, int order = 0, Action<Rect> draw = null, string section = null)
            => k.Pages.Add(new KitPage { Id = id, LabelEn = label, Icon = icon, Order = order, Draw = draw, Section = section });

        /// <summary>
        /// A self-repair check: runs once after every load and then one check every 250 ticks (round robin), only while its section is on.
        /// Returns how many problems it fixed; report each fix with <see cref="KitMod.CaretakerFixed"/>. Repair only what is clearly broken.
        /// </summary>
        public void Caretaker(string section, string name, Func<int> check) => k.Caretaker.Register(section, name, check);

        /// <summary>
        /// The mod's own per-save data, stored inside the framework's game component under the mod's packageId (so removing the mod
        /// leaves no missing-class error). <paramref name="exposeData"/> runs in every Scribe pass inside the mod's own node: use
        /// Scribe_Values/Collections/Deep as usual and fix nulls on PostLoadInit. <paramref name="reset"/> runs when a new game object is
        /// created (new game or before a load) so values of the previous game never leak. Only the mod's data is skipped when it is absent.
        /// </summary>
        public void SaveData(Action exposeData, Action reset = null)
        {
            k.SaveExpose = exposeData;
            k.SaveReset = reset;
        }
    }
}

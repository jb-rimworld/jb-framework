// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;

namespace JBKit
{
    /// <summary>A custom page of the settings window (drawn by the mod). src/UI lists it in the sidebar after the section pages.</summary>
    internal sealed class KitPage
    {
        public string Id;
        public string LabelEn;
        public string Icon;
        public int Order;
        /// <summary>Optional section: the page is dimmed while that section is off.</summary>
        public string Section;
        /// <summary>Draws the page into the main area (inside the window's exception fence).</summary>
        public Action<Rect> Draw;
        public string Label => KitText.Key("Page." + Id).TranslateOr(LabelEn ?? Id);
    }

    internal static class KitPages
    {
        public static readonly List<KitPage> All = new List<KitPage>();
        public static KitPage Find(string id) => All.FirstOrDefault(p => p.Id == id);
        public static KitPage Add(KitPage p)
        {
            if (p == null || string.IsNullOrEmpty(p.Id) || p.Draw == null || Find(p.Id) != null) return Find(p?.Id);
            if (p.Order == 0) p.Order = (All.Count + 1) * 10;
            All.Add(p);
            All.Sort((a, b) => a.Order.CompareTo(b.Order));
            return p;
        }
    }

    /// <summary>
    /// What a mod writes in <see cref="KitModBase.Register"/>. All texts are English fallbacks; translations go into the mod's Keyed
    /// files under &lt;Prefix&gt;.Section.*, &lt;Prefix&gt;.Setting.*, &lt;Prefix&gt;.Preset.*, &lt;Prefix&gt;.Compat.*, &lt;Prefix&gt;.Page.*.
    /// Setting methods take the section id first (use <see cref="KitSections.Core"/> = "core" for general settings).
    /// </summary>
    internal sealed class KitRegistry
    {
        // Sections -----------------------------------------------------------------------------------------------------
        public KitSection Section(string id, string label, string desc = null, string icon = null, bool defaultOn = true,
            Func<bool> available = null, string requires = null, int order = 0, Action<bool> onSwitched = null,
            Func<IEnumerable<string>> diagnosis = null, Action<List<string>> prepareRemoval = null, string legacyEnabled = null)
            => KitSections.Add(new KitSection
            {
                Id = id, LabelEn = label, DescEn = desc, Icon = icon, DefaultOn = defaultOn, AvailableFunc = available, RequiresText = requires,
                Order = order, OnSwitched = onSwitched, Diagnosis = diagnosis, PrepareRemoval = prepareRemoval, LegacyEnabled = legacyEnabled,
            });

        /// <summary>Section that exists only with a mod or DLC (availability = that packageId is active; requires text "Needs &lt;name&gt;").</summary>
        public KitSection SectionFor(string packageId, string modName, string id, string label, string desc = null, string icon = null, bool defaultOn = true, int order = 0)
            => Section(id, label, desc, icon, defaultOn, () => KitCompat.IsModActive(packageId), "Needs " + modName, order);

        // Settings -----------------------------------------------------------------------------------------------------
        public KitSetting Bool(string section, string cat, string key, bool def, string label, string desc = null, bool nerd = false, string legacyScribe = null)
            => KitSettings.Add(new KitSetting { Section = section, Category = cat, Key = key, Kind = KitSettingKind.Bool, Default = def ? 1 : 0, SliderMin = 0, SliderMax = 1, HardMin = 0, HardMax = 1, LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe });

        public KitSetting Int(string section, string cat, string key, int def, int min, int max, string label, string desc = null, bool nerd = false, int hardMin = 0, int hardMax = 1_000_000, string legacyScribe = null)
            => KitSettings.Add(new KitSetting { Section = section, Category = cat, Key = key, Kind = KitSettingKind.Int, Default = def, SliderMin = min, SliderMax = max, HardMin = hardMin, HardMax = hardMax, LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe });

        public KitSetting Float(string section, string cat, string key, float def, float min, float max, string label, string desc = null, bool nerd = false, float hardMin = 0f, float hardMax = 1e6f, string legacyScribe = null)
            => KitSettings.Add(new KitSetting { Section = section, Category = cat, Key = key, Kind = KitSettingKind.Float, Default = def, SliderMin = min, SliderMax = max, HardMin = hardMin, HardMax = hardMax, LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe });

        /// <summary>Fraction stored as 0..1 (shown as %).</summary>
        public KitSetting Percent(string section, string cat, string key, float def, float min, float max, string label, string desc = null, bool nerd = false, float hardMin = 0f, float hardMax = 1f, string legacyScribe = null)
            => KitSettings.Add(new KitSetting { Section = section, Category = cat, Key = key, Kind = KitSettingKind.Percent, Default = def, SliderMin = min, SliderMax = max, HardMin = hardMin, HardMax = hardMax, LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe });

        /// <summary>Positive number on a log-scale slider (e.g. multipliers 0.1 … 100).</summary>
        public KitSetting LogFloat(string section, string cat, string key, float def, float min, float max, string label, string desc = null, bool nerd = false, float hardMin = 0f, float hardMax = 1e6f, string legacyScribe = null)
            => KitSettings.Add(new KitSetting { Section = section, Category = cat, Key = key, Kind = KitSettingKind.LogFloat, Default = def, SliderMin = min, SliderMax = max, HardMin = hardMin, HardMax = hardMax, LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe });

        public KitSetting Enum<T>(string section, string cat, string key, T def, string label, string desc = null, bool nerd = false, string legacyScribe = null) where T : struct, IConvertible
        {
            int max = 0; foreach (var v in System.Enum.GetValues(typeof(T))) max = Math.Max(max, Convert.ToInt32(v));
            return KitSettings.Add(new KitSetting { Section = section, Category = cat, Key = key, Kind = KitSettingKind.Enum, EnumType = typeof(T), Default = Convert.ToInt32(def), SliderMin = 0, SliderMax = max, HardMin = 0, HardMax = max, LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe });
        }

        /// <summary>Whole silver. Hard max follows 10 × the silver stack limit (keeps the game stable).</summary>
        public KitSetting Silver(string section, string cat, string key, int def, int min, int max, string label, string desc = null, bool nerd = false, string legacyScribe = null)
            => KitSettings.Add(new KitSetting
            {
                Section = section, Category = cat, Key = key, Kind = KitSettingKind.Silver, Default = def, SliderMin = min, SliderMax = max, HardMin = 0, HardMax = 2_000_000_000f,
                LabelEn = label, DescEn = desc, Nerd = nerd, LegacyScribe = legacyScribe,
                DynamicHardMax = () => Math.Max(max, (ThingDefOf.Silver?.stackLimit ?? 500) * 10f), HardLimitReason = "10 × the silver stack limit of your game (keeps the game stable)",
            });

        // Presets, compatibility, pages, caretaker ----------------------------------------------------------------------
        public KitPreset Preset(string id, string label, string desc, Action<KitProfile> apply, Func<KitProfile, bool> matches = null)
            => KitPresets.Add(new KitPreset { Id = id, LabelEn = label, DescEn = desc, Apply = apply, Matches = matches });

        /// <summary>A soft integration. <paramref name="packageIdOrDlc"/>: packageId (DLCs: "ludeon.rimworld.biotech"). Tag its patch classes with [KitCompat(id)].</summary>
        public KitCompatEntry Compat(string name, string packageIdOrDlc, string what, Func<bool> healthy = null)
            => KitCompat.Add(new KitCompatEntry { Name = name, PackageId = packageIdOrDlc, WhatEn = what, Healthy = healthy });

        /// <summary>A known incompatibility: the page shows it with the reason and what to do.</summary>
        public KitCompatEntry Incompatible(string name, string packageId, string why)
            => KitCompat.Add(new KitCompatEntry { Name = name, PackageId = packageId, WhatEn = why, Incompatible = true });

        public KitPage Page(string id, string label, string icon = null, int order = 0, Action<Rect> draw = null, string section = null)
            => KitPages.Add(new KitPage { Id = id, LabelEn = label, Icon = icon, Order = order, Draw = draw, Section = section });

        /// <summary>A self-repair check (see <see cref="KitCaretaker"/>). Runs only while its section is on; needs the mod's KitGameBase subclass.</summary>
        public void Caretaker(string section, string name, Func<int> check) => KitCaretaker.Register(section, name, check);
    }
}

// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

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

        /// <summary>Sidebar icon (texture path such as "UI/MyMod/Notes"). A missing texture draws nothing.</summary>
        public KitPage WithIcon(string texPath) { Icon = texPath; return this; }
        /// <summary>Position in the sidebar: below 0 before General, otherwise after the section pages (default: registration order).</summary>
        public KitPage WithOrder(int order) { Order = order; Owner?.Pages.Resort(); return this; }
        /// <summary>The page belongs to a section: it is hidden while the section is not available and dimmed (with a switch-on banner) while it is off.</summary>
        public KitPage InSection(string sectionId) { Section = sectionId; return this; }
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
            Resort();
            return p;
        }
        public void Resort() { var o = All.ToList(); All.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : o.IndexOf(a).CompareTo(o.IndexOf(b))); }
    }

    /// <summary>
    /// What a mod writes in <see cref="KitModBase.Register"/>: sections, settings, presets, compatibility entries, pages, caretaker
    /// checks and per-save data. Every method takes only the essentials; everything optional is a chained modifier on the returned
    /// object (<c>r.Float("haul", "mm.speed", 1.5f, 1f, 5f, "Hauling speed").InGroup("Speed").AsNerd()</c>), so new options can be
    /// added later without breaking any call. All texts are English fallbacks; translations go into the mod's Keyed files under
    /// &lt;KeyPrefix&gt;.Section.*, .Setting.*, .SettingCat.*, .Preset.*, .Compat.*, .Page.*, .Enum.* (see API.md).
    /// <para>Conventions: section and page ids are short lower-case words ("walls"); setting keys are "&lt;shortid&gt;.&lt;camelCase&gt;"
    /// ("mm.haulSpeed"), unique within the mod and never renamed (they are the storage keys). The section id <see cref="KitMod.Core"/>
    /// ("core") holds the general, always-on settings. Registering the same id or key twice returns the first registration.</para>
    /// </summary>
    public sealed class KitRegistry
    {
        readonly KitMod k;
        internal KitRegistry(KitMod owner) { k = owner; }

        /// <summary>The mod's context. Use this inside Register (the mod's own <see cref="KitModBase.Kit"/> is set too, but this is always safe).</summary>
        public KitMod Kit => k;

        // Sections ---------------------------------------------------------------------------------------------------------
        /// <summary>
        /// A switchable feature area (on by default; per save when the mod is <see cref="KitInfo.PerSave"/>). Patches of the area ask
        /// <see cref="KitMod.On"/>. Modifiers: <see cref="KitSection.Describe"/>, <see cref="KitSection.OffByDefault"/>,
        /// <see cref="KitSection.OnlyWith(string)"/>, <see cref="KitSection.WhenSwitched"/>, <see cref="KitSection.WithRemoval"/> and more.
        /// </summary>
        public KitSection Section(string id, string label) => k.Sections.Add(new KitSection { Id = id, LabelEn = label });

        /// <summary>A switchable feature area with a description (see <see cref="Section(string, string)"/>).</summary>
        public KitSection Section(string id, string label, string desc) => k.Sections.Add(new KitSection { Id = id, LabelEn = label, DescEn = desc });

        // Settings ---------------------------------------------------------------------------------------------------------
        /// <summary>On/off value in <paramref name="section"/> (<see cref="KitMod.Core"/> for general settings). Read with <see cref="KitMod.GetBool"/>.</summary>
        public KitSetting Bool(string section, string key, bool def, string label) => Bool(section, key, def, label, null);
        /// <summary>On/off value with a description (tooltip and search text).</summary>
        public KitSetting Bool(string section, string key, bool def, string label, string desc)
            => k.SettingsReg.Add(new KitSetting { SectionId = section, Key = key, Kind = KitSettingKind.Bool, Default = def ? 1 : 0, SliderMin = 0, SliderMax = 1, HardMin = 0, HardMax = 1, LabelEn = label, DescEn = desc });

        /// <summary>
        /// Whole number: slider <paramref name="min"/>..<paramref name="max"/>; typed values may go from min(0, min) to max(1,000,000, max)
        /// (change with <see cref="KitSetting.HardLimits"/>). Read with <see cref="KitMod.GetInt"/>.
        /// </summary>
        public KitSetting Int(string section, string key, int def, int min, int max, string label) => Int(section, key, def, min, max, label, null);
        /// <summary>Whole number with a description.</summary>
        public KitSetting Int(string section, string key, int def, int min, int max, string label, string desc)
            => k.SettingsReg.Add(Number(section, key, KitSettingKind.Int, def, min, max, label, desc, 1_000_000f));

        /// <summary>Decimal number: slider <paramref name="min"/>..<paramref name="max"/>; typed values from min(0, min) to max(1,000,000, max). Read with <see cref="KitMod.Get"/>.</summary>
        public KitSetting Float(string section, string key, float def, float min, float max, string label) => Float(section, key, def, min, max, label, null);
        /// <summary>Decimal number with a description.</summary>
        public KitSetting Float(string section, string key, float def, float min, float max, string label, string desc)
            => k.SettingsReg.Add(Number(section, key, KitSettingKind.Float, def, min, max, label, desc, 1_000_000f));

        /// <summary>Fraction stored as 0..1 (0.25 = 25 %), shown and typed as percent. Typed values from min(0, min) to max(1, max).</summary>
        public KitSetting Percent(string section, string key, float def, float min, float max, string label) => Percent(section, key, def, min, max, label, null);
        /// <summary>Percent value with a description.</summary>
        public KitSetting Percent(string section, string key, float def, float min, float max, string label, string desc)
            => k.SettingsReg.Add(Number(section, key, KitSettingKind.Percent, def, min, max, label, desc, 1f));

        /// <summary>Positive number on a log-scale slider (multipliers 0.1 … 100).</summary>
        public KitSetting LogFloat(string section, string key, float def, float min, float max, string label) => LogFloat(section, key, def, min, max, label, null);
        /// <summary>Log-scale number with a description.</summary>
        public KitSetting LogFloat(string section, string key, float def, float min, float max, string label, string desc)
            => k.SettingsReg.Add(Number(section, key, KitSettingKind.LogFloat, def, min, max, label, desc, 1_000_000f));

        /// <summary>
        /// One value of an enum, shown as a dropdown. Labels: &lt;KeyPrefix&gt;.Enum.&lt;EnumTypeName&gt;.&lt;ValueName&gt;, fallback the
        /// value name split at capitals. Stored as the number: never renumber the enum. Read with <see cref="KitMod.GetEnum{T}"/>;
        /// in a preset use <c>p.Set(key, (float)(int)MyEnum.X)</c>.
        /// </summary>
        public KitSetting Enum<T>(string section, string key, T def, string label) where T : struct, IConvertible => Enum(section, key, def, label, null);
        /// <summary>Enum dropdown with a description.</summary>
        public KitSetting Enum<T>(string section, string key, T def, string label, string desc) where T : struct, IConvertible
        {
            int max = 0; foreach (var v in System.Enum.GetValues(typeof(T))) max = Math.Max(max, Convert.ToInt32(v));
            return k.SettingsReg.Add(new KitSetting { SectionId = section, Key = key, Kind = KitSettingKind.Enum, EnumType = typeof(T), Default = Convert.ToInt32(def), SliderMin = 0, SliderMax = max, HardMin = 0, HardMax = max, LabelEn = label, DescEn = desc });
        }

        /// <summary>Whole silver. Typed values stop at 10 × the silver stack limit of the game (keeps the game stable).</summary>
        public KitSetting Silver(string section, string key, int def, int min, int max, string label) => Silver(section, key, def, min, max, label, null);
        /// <summary>Silver value with a description.</summary>
        public KitSetting Silver(string section, string key, int def, int min, int max, string label, string desc)
            => k.SettingsReg.Add(new KitSetting
            {
                SectionId = section, Key = key, Kind = KitSettingKind.Silver, Default = def, SliderMin = min, SliderMax = max, HardMin = 0, HardMax = 2_000_000_000f,
                LabelEn = label, DescEn = desc,
                DynamicHardMax = () => Math.Max(max, (ThingDefOf.Silver?.stackLimit ?? 500) * 10f), HardLimitReason = "10 × the silver stack limit of your game (keeps the game stable)",
            });

        static KitSetting Number(string section, string key, KitSettingKind kind, float def, float min, float max, string label, string desc, float hardMaxDefault)
            => new KitSetting
            {
                SectionId = section, Key = key, Kind = kind, Default = def, SliderMin = min, SliderMax = max,
                HardMin = Math.Min(0f, min), HardMax = Math.Max(hardMaxDefault, max), LabelEn = label, DescEn = desc,
            };

        // Welcome card ---------------------------------------------------------------------------------------------------------
        /// <summary>
        /// The mod's parts of its welcome card (drawn areas, actions, a close callback, hiding presets or sections); every call returns the
        /// same object. A per-save mod with a <see cref="KitInfo.FirstStartPitch"/> or with this registration gets the card.
        /// </summary>
        public KitCard WelcomeCard()
        {
            if (k.Card == null) k.Card = new KitCard { Owner = k };
            return k.Card;
        }

        // Presets, compatibility, pages, caretaker, per-save data -------------------------------------------------------------
        /// <summary>
        /// A named set of values shown as a chip on the Overview page and the welcome card. <paramref name="apply"/> changes only what it
        /// names (<see cref="KitProfile.Set(string, float)"/>, <see cref="KitProfile.SetSection"/>; an Int or Enum is set as a float).
        /// The chip is highlighted while applying it would change nothing (<see cref="KitPreset.HighlightWhen"/> overrides that).
        /// </summary>
        public KitPreset Preset(string id, string label, string desc, Action<KitProfile> apply)
            => k.Presets.Add(new KitPreset { Id = id, LabelEn = label, DescEn = desc, Apply = apply });

        /// <summary>
        /// A soft integration shown on the Compatibility page ("active" / "not installed" / "failed: reason").
        /// <paramref name="packageIdOrDlc"/>: the other mod's packageId (DLCs: "ludeon.rimworld.biotech"). Put
        /// [<see cref="KitCompatAttribute"/>(packageId)] on the Harmony patch classes of the integration: they are applied only while that mod
        /// is active (no error otherwise), and a failed one shows up in this entry. <see cref="KitCompatEntry.HealthyWhen"/> adds a self-check.
        /// </summary>
        public KitCompatEntry Compat(string name, string packageIdOrDlc, string what)
            => k.Compat.Add(new KitCompatEntry { Name = name, PackageId = packageIdOrDlc, WhatEn = what });

        /// <summary>A known incompatibility: the Compatibility page shows it with the reason and what to do.</summary>
        public KitCompatEntry Incompatible(string name, string packageId, string why)
            => k.Compat.Add(new KitCompatEntry { Name = name, PackageId = packageId, WhatEn = why, Incompatible = true });

        /// <summary>
        /// A custom page drawn by the mod, listed after the section pages (<see cref="KitPage.WithOrder"/> below 0: before General).
        /// <paramref name="draw"/> gets the page area and does its own scrolling. It runs inside a UI fence: an exception shows an error
        /// box on that page (with a retry button) and never pauses a section or reaches another mod.
        /// </summary>
        public KitPage Page(string id, string label, Action<Rect> draw)
            => k.Pages.Add(new KitPage { Id = id, LabelEn = label, Draw = draw });

        /// <summary>
        /// A self-repair check: runs once after every load and then one check every 250 ticks (round robin), only while its section is on.
        /// Returns how many problems it fixed; report each fix with <see cref="KitMod.CaretakerFixed"/>. Repair only what is clearly broken.
        /// </summary>
        public void Caretaker(string section, string name, Func<int> check) => k.Caretaker.Register(section, name, check);

        /// <summary>
        /// The mod's own per-save data, stored inside the framework's game component under the mod's packageId (so removing the mod
        /// leaves no missing-class error, and its data comes back when the mod returns). <paramref name="exposeData"/> runs in every Scribe
        /// pass inside the mod's own node: use Scribe_Values/Collections/Deep as usual and fix nulls on PostLoadInit.
        /// </summary>
        public void SaveData(Action exposeData) => SaveData(exposeData, null);

        /// <summary>
        /// Per-save data with a reset: <paramref name="reset"/> runs when a new game object is created (new game or before a load) so
        /// values of the previous game never leak into the next one (static fields!).
        /// </summary>
        public void SaveData(Action exposeData, Action reset)
        {
            k.SaveExpose = exposeData;
            k.SaveReset = reset;
        }

        /// <summary>
        /// The mod's own per-map data, stored in the framework's map component (<see cref="FrameworkMap"/>) under the mod's packageId: a
        /// mod needs no MapComponent class of its own, so removing it leaves no missing-class error, and the node comes back with the mod.
        /// <paramref name="exposeData"/> runs in every Scribe pass of each map, inside the mod's own node, with that map: Scribe_Values /
        /// Collections / Deep / References as usual (references to things and pawns of any map resolve).
        /// <code>r.MapData(m =&gt; { var d = Marks.For(m); Scribe_Values.Look(ref d.count, "count"); Scribe_References.Look(ref d.pawn, "pawn"); });</code>
        /// </summary>
        public void MapData(Action<Map> exposeData) => MapData(exposeData, null);

        /// <summary>
        /// Per-map data with a reset: <paramref name="reset"/> runs for a map when its component is created (new map, or before the map is
        /// loaded) and when the map is removed, so state keyed by that map is forgotten (static dictionaries!).
        /// </summary>
        public void MapData(Action<Map> exposeData, Action<Map> reset)
        {
            k.MapExpose = exposeData;
            k.MapReset = reset;
        }

        /// <summary>
        /// An OLD Game-, World- or MapComponent class of this mod that existing saves contain: keep the class (same name; its ExposeData
        /// reads the old fields), move what it read into <see cref="SaveData(Action)"/> / <see cref="MapData(Action{Map})"/> while
        /// loading (in its ExposeData at PostLoadInit, or its FinalizeInit / LoadedGame). The framework never writes it into a save again
        /// and drops its instances once a load or a new game finished, so old saves keep loading and new saves name no class of the mod.
        /// Returns false (logged) for a type that is not such a component of this mod.
        /// </summary>
        public bool LegacyComponent(Type componentType)
        {
            bool isComp = componentType != null && !componentType.IsAbstract && (typeof(GameComponent).IsAssignableFrom(componentType)
                || typeof(RimWorld.Planet.WorldComponent).IsAssignableFrom(componentType) || typeof(MapComponent).IsAssignableFrom(componentType));
            bool own = componentType != null && (k.Mod == null || componentType.Assembly == k.Mod.GetType().Assembly
                || (k.Content?.assemblies?.loadedAssemblies?.Contains(componentType.Assembly) ?? false));
            if (!isComp || !own || componentType.Assembly == typeof(Framework).Assembly)
            {
                k.Log.Warn("Core", "LegacyComponent(" + componentType?.FullName + "): not a GameComponent, WorldComponent or MapComponent of this mod; ignored");
                return false;
            }
            KitComponentStrip.Add(componentType);
            k.Log.Info("Core", "Legacy component " + componentType.FullName + ": read from old saves, never written again");
            return true;
        }
    }
}

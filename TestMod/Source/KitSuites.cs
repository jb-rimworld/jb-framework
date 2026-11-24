// SPDX-License-Identifier: MIT
// Built-in suites every mod on the framework gets (run against the mod under test, TestContext.Mod; spec SharedKit.md 1.5.11).
// Generalised from Hearth & Harbor's smoke, core.defs, saveload.* and core.settingsdump suites. Part of JBFramework.Testing.dll.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBFramework.Testing
{
    /// <summary>Shared helpers of the built-in suites.</summary>
    internal static class KitSuiteUtil
    {
        public static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        public static bool Near(float a, float b) => Math.Abs(a - b) <= 1e-4f * Math.Max(1f, Math.Abs(b));

        /// <summary>A value different from the default inside the slider range (what a player could pick).</summary>
        public static float OtherValue(KitSetting s)
        {
            float def = s.Clamp(s.Default);
            switch (s.Kind)
            {
                case KitSettingKind.Bool: return def >= 0.5f ? 0f : 1f;
                case KitSettingKind.Enum:
                    int max = (int)Math.Round(s.SliderMax);
                    return max <= 0 ? def : ((int)Math.Round(def) + 1) % (max + 1);
                case KitSettingKind.Int:
                case KitSettingKind.Silver:
                {
                    float mid = (float)Math.Round((s.SliderMin + s.SliderMax) / 2f);
                    if (Near(s.Clamp(mid), def)) mid = Near(s.Clamp(s.SliderMax), def) ? s.SliderMin : s.SliderMax;
                    return mid;
                }
                case KitSettingKind.LogFloat:
                {
                    float lo = Math.Max(1e-6f, s.SliderMin), hi = Math.Max(lo, s.SliderMax);
                    float mid = (float)Math.Sqrt(lo * hi);
                    if (Near(s.Clamp(mid), def)) mid = Near(s.Clamp(hi), def) ? lo : hi;
                    return mid;
                }
                default:
                {
                    float mid = (s.SliderMin + s.SliderMax) / 2f;
                    if (Near(s.Clamp(mid), def)) mid = Near(s.Clamp(s.SliderMax), def) ? s.SliderMin : s.SliderMax;
                    return mid;
                }
            }
        }

        /// <summary>Copy of the template and (when per-save) the save profile, to put everything back after a suite.</summary>
        internal sealed class Snapshot
        {
            KitMod k; KitStore tStore, sStore; Dictionary<string, bool> tSec, sSec; KitProfile save;
            public static Snapshot Take(KitMod k)
            {
                var s = new Snapshot { k = k, tStore = k.Template.Store.Clone(), tSec = new Dictionary<string, bool>(k.Template.sections), save = k.SaveProfile };
                if (s.save != null) { s.sStore = s.save.Store.Clone(); s.sSec = new Dictionary<string, bool>(s.save.sections); }
                return s;
            }
            /// <summary>Restores values and switches (switch callbacks run for sections that change back) and tells the mod about every key once.</summary>
            public void Restore()
            {
                foreach (var p in new[] { (k.Template, tSec), (save, sSec) })
                {
                    if (p.Item1 == null || p.Item2 == null) continue;
                    foreach (var sec in k.Sections.All)
                    {
                        bool want = p.Item2.TryGetValue(sec.Id, out bool on) ? on : sec.DefaultOn;
                        if (p.Item1.SectionOn(sec.Id) != want) p.Item1.SetSection(sec.Id, want);
                    }
                    p.Item1.sections = new Dictionary<string, bool>(p.Item2);
                }
                k.Template.Store = tStore.Clone();
                if (save != null && sStore != null) save.Store = sStore.Clone();
                foreach (var s in k.SettingsReg.All) k.MarkChanged(s.Key);
                k.FlushChanges();
            }
        }


        /// <summary>Every translation key the registry uses, with its English text (the kit.settingsdump contract for tools/kit_lang.py).</summary>
        public static List<KeyValuePair<string, string>> RegistryKeys(KitMod k)
        {
            var list = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>();
            void Add(string suffix, string en) { if (string.IsNullOrEmpty(en)) return; var key = k.Key(suffix); if (seen.Add(key)) list.Add(new KeyValuePair<string, string>(key, en)); }
            foreach (var sec in k.Sections.All)
            {
                Add("Section." + sec.Id, sec.LabelEn ?? sec.Id);
                Add("Section." + sec.Id + ".Desc", sec.DescEn);
                Add("Section." + sec.Id + ".Requires", sec.RequiresText);
            }
            foreach (var s in k.SettingsReg.All)
            {
                if (!string.IsNullOrEmpty(s.Category)) Add("SettingCat." + KitSetting.CatKey(s.Category), s.Category);
                Add("Setting." + s.Key, s.LabelEn ?? s.Key);
                Add("Setting." + s.Key + ".Desc", s.DescEn);
                if (s.Kind == KitSettingKind.Enum && s.EnumType != null)
                    foreach (var n in System.Enum.GetNames(s.EnumType)) Add("Enum." + s.EnumType.Name + "." + n, GenText.SplitCamelCase(n));
            }
            foreach (var p in k.Presets.All) { Add("Preset." + p.Id, p.LabelEn ?? p.Id); Add("Preset." + p.Id + ".Desc", p.DescEn); }
            foreach (var p in k.Pages.All) Add("Page." + p.Id, p.LabelEn ?? p.Id);
            foreach (var c in k.Compat.All) Add("Compat." + KitSetting.CatKey(c.Name) + (c.Incompatible ? ".Why" : ".What"), c.WhatEn);
            return list;
        }

        /// <summary>The root-level children of &lt;ModSettings&gt; in a settings file (name → inner text). Null if the file cannot be parsed.</summary>
        public static Dictionary<string, string> SettingsFileFields(string path, out bool hasKitNode)
        {
            hasKitNode = false;
            var d = new Dictionary<string, string>();
            try
            {
                var doc = new XmlDocument(); doc.Load(path);
                var ms = doc.DocumentElement?.SelectSingleNode("ModSettings");
                if (ms == null) return d;
                foreach (XmlNode n in ms.ChildNodes)
                {
                    if (n.NodeType != XmlNodeType.Element) continue;
                    if (n.Name == "kit") { hasKitNode = true; continue; }
                    d[n.Name] = n.InnerText;
                }
            }
            catch { return null; }
            return d;
        }

        public static string SaveName(KitMod k) => "jb_" + (k?.Info?.ShortId ?? "kit").ToLowerInvariant() + "_saveload";

        public static List<string> ValueLines(KitMod k, KitProfile p)
        {
            var l = new List<string>();
            if (p == null || k == null) return l;
            foreach (var s in k.SettingsReg.All.Where(x => !x.Global)) l.Add(s.Key + "=" + F(k.SettingsReg.Value(s, p)));
            foreach (var sec in k.Sections.All) l.Add("section:" + sec.Id + "=" + p.SectionOn(sec.Id));
            return l;
        }

        /// <summary>No red error naming the framework or the mod since the start of the run (notes the first ones).</summary>
        public static void NoRedErrors(TestContext t, int since, string what)
        {
            var ours = KitTestLog.Since(since, true);
            foreach (var e in ours.Take(10)) t.Note("our red error: " + KitTestLog.Format(e));
            t.Check(ours.Count == 0, what + " (" + ours.Count + " of ours; all mods: " + KitTestLog.Since(since, false).Count + ")");
        }

        /// <summary>Does a save text hold this mod's entry in the framework's component?</summary>
        public static bool SaveHasEntry(string text, KitMod k) => text.Contains("<id>" + FrameworkGame.SaveKey(k) + "</id>");
    }

    /// <summary>kit.smoke: loaded from the expected folders, registrations complete, nothing skipped or paused, no red error of ours.</summary>
    internal sealed class KitSmokeSuite : KitTestSuite
    {
        public override string Name => "kit.smoke";
        public override int MaxTicks => 3000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var k = t.Mod;
            t.Check(k != null && k.Mod != null, "context of the mod under test: " + k?.Name + " " + k?.Version + " (" + k?.PackageId + ") on " + Framework.Name + " " + Framework.Version + " (API level " + Framework.ApiLevel + ")");
            if (k == null) yield break;
            t.Check(!k.Blocked, "not blocked by the API-level check" + (k.Blocked ? ": " + k.BlockedReason : ""));
            t.Check(Framework.Self != null, "the framework's own context exists");
            t.Note("tenants: " + string.Join(", ", Framework.Mods.Select(m => m.Name + " " + m.Version)));
            if (!k.IsFramework)
            {
                t.Check(Framework.Mods.Contains(k), "the mod is listed in Framework.Mods");
                var mine = k.Content?.assemblies?.loadedAssemblies ?? new List<Assembly>();
                t.Check(!mine.Any(a => a == typeof(Framework).Assembly || a.GetName().Name == typeof(Framework).Assembly.GetName().Name), "the mod ships no copy of " + typeof(Framework).Assembly.GetName().Name + ".dll");
                int need = Framework.RequiredLevel(k.Mod.GetType().Assembly, out string built);
                t.Check(need >= 1 && need <= Framework.ApiLevel, "API level stamp " + need + " (built with " + built + ") ≤ installed " + Framework.ApiLevel);
            }

            // folders: the 1.7 view of LoadFolders.xml, never 1.6
            var loaded = KitReport.LoadFolders(k);
            t.Note("load folders: " + string.Join(", ", loaded));
            t.Check(!loaded.Any(f => f == "1.6" || f.StartsWith("1.6/")), "no 1.6 folder loaded on 1.7");
            var expected = ExpectedFolders(k);
            if (expected != null)
            {
                var a = new HashSet<string>(loaded.Select(Norm)); var b = new HashSet<string>(expected.Select(Norm));
                t.Check(a.SetEquals(b), "loaded folders = LoadFolders.xml v1.7 (" + string.Join(", ", expected) + ")");
            }
            else t.Note("no LoadFolders.xml with a v1.7 entry");

            t.Check(k.Settings != null, "settings object found (" + k.Settings?.GetType().Name + ")");
            t.Note("registered: " + k.SettingsReg.All.Count + " settings, " + k.Sections.All.Count + " sections, " + k.Presets.All.Count + " presets, " + k.Compat.All.Count + " compat entries, " + k.Pages.All.Count + " pages");
            var noLabel = k.SettingsReg.All.Where(s => string.IsNullOrWhiteSpace(s.Label) || string.IsNullOrWhiteSpace(s.Desc)).Select(s => s.Key).ToList();
            t.Check(noLabel.Count == 0, "every setting has a label and a description" + (noLabel.Count > 0 ? ": missing " + string.Join(", ", noLabel) : ""));
            var badSec = k.Sections.All.Where(s => string.IsNullOrWhiteSpace(s.Label) || string.IsNullOrWhiteSpace(s.Desc)).Select(s => s.Id).ToList();
            t.Check(badSec.Count == 0, "every section (" + k.Sections.All.Count + ") has a label and a description" + (badSec.Count > 0 ? ": missing " + string.Join(", ", badSec) : ""));
            t.Check(k.Presets.All.All(p => !string.IsNullOrWhiteSpace(p.Label) && !string.IsNullOrWhiteSpace(p.Desc)), "every preset (" + k.Presets.All.Count + ") has a label and a description");
            t.Check(k.Compat.All.All(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.What)), "every compat entry (" + k.Compat.All.Count + ") says what it does");
            t.Check(k.Pages.All.All(p => !string.IsNullOrWhiteSpace(p.Label)), "every custom page (" + k.Pages.All.Count + ") has a label");
            t.Check(k.Patches.Failures.Count == 0, "no patch class skipped (" + k.Patches.Applied + " applied, " + k.Patches.SkippedInactive.Count + " soft integrations inactive" + (k.Patches.Failures.Count > 0 ? "; " + string.Join("; ", k.Patches.Failures) : "") + ")");
            t.Check(!k.Guard.Paused.Any(), "no section paused: " + string.Join(", ", k.Guard.Paused.Select(k.Guard.ReasonFor)));
            t.Check(FrameworkGame.Instance != null, "the framework's game component runs in this game");
            if (k.Info.PerSave)
                t.Check(k.SaveProfile != null && ReferenceEquals(k.Current, k.SaveProfile), "per save: reads go to this save's profile");
            else t.Check(ReferenceEquals(k.Current, k.Template), "global: reads go to the template (settings file)");

            string report = null, combined = null;
            try { report = k.DiagnosisReport(); combined = Framework.DiagnosisReport(); } catch (Exception e) { t.Check(false, "diagnosis report threw " + e.Message); }
            if (report != null)
            {
                t.Check(report.Length > 50 && combined.Length > report.Length / 2, "diagnosis report built (" + report.Split('\n').Length + " lines; combined " + combined.Split('\n').Length + ")");
                string home = null; try { home = Environment.GetEnvironmentVariable("HOME"); } catch { }
                bool clean(string r) => r.IndexOf("/Users/", StringComparison.Ordinal) < 0 && (string.IsNullOrEmpty(home) || r.IndexOf(home, StringComparison.Ordinal) < 0);
                t.Check(clean(report) && clean(combined), "diagnosis reports have no personal paths");
            }

            t.Check(k.Log.ErrorCount == 0 && (Framework.Self?.Log.ErrorCount ?? 0) == 0, "no error in the logs of the mod (" + k.Log.ErrorCount + ") and the framework (" + Framework.Self?.Log.ErrorCount + ") since start");
            KitSuiteUtil.NoRedErrors(t, 0, "no red error of the framework or the mod since start");

            int tick0 = Find.TickManager.TicksGame;
            yield return 600;
            t.Check(Find.TickManager.TicksGame > tick0, "the game ticks");
            t.Check(!k.Guard.Paused.Any() && KitTestLog.Since(0, true).Count == 0, "still nothing paused and no red error of ours after 600 ticks");
        }

        static string Norm(string f) { f = (f ?? "").Replace('\\', '/').Trim('/'); return f.Length == 0 ? "/" : f; }

        static List<string> ExpectedFolders(KitMod k)
        {
            try
            {
                var root = k.Content?.RootDir; if (root == null) return null;
                var path = Path.Combine(root, "LoadFolders.xml"); if (!File.Exists(path)) return null;
                var doc = new XmlDocument(); doc.Load(path);
                var v = doc.DocumentElement?.SelectSingleNode("v1.7"); if (v == null) return null;
                var list = new List<string>();
                foreach (XmlNode li in v.SelectNodes("li"))
                {
                    var f = li.InnerText.Trim();
                    if (f.Length == 0) continue;
                    var attrs = li.Attributes;   // IfModActive / IfModNotActive entries depend on the mod list: not expected unconditionally
                    if (attrs != null && attrs.Count > 0) continue;
                    if (Directory.Exists(Path.Combine(root, f.Trim('/')))) list.Add(f);
                }
                return list;
            }
            catch { return null; }
        }
    }

    /// <summary>kit.settings: every registration round-trips (set/get/clamp/reset), slider inside the hard range, legacy keys resolve,
    /// switches toggle, presets are idempotent, template → new game copy, per-save separation. Puts everything back afterwards.</summary>
    internal sealed class KitSettingsSuite : KitTestSuite
    {
        public override string Name => "kit.settings";
        public override int MaxTicks => 3000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var k = t.Mod;
            var snap = KitSuiteUtil.Snapshot.Take(k);
            var before = KitSuiteUtil.ValueLines(k, k.Current).Concat(KitSuiteUtil.ValueLines(k, k.Template)).ToList();
            try { Body(t, k); }
            finally { snap.Restore(); }
            var after = KitSuiteUtil.ValueLines(k, k.Current).Concat(KitSuiteUtil.ValueLines(k, k.Template)).ToList();
            t.Check(before.SequenceEqual(after), "values and switches put back as they were (" + before.Count + " lines)");
            yield return 1;
        }

        static void Body(TestContext t, KitMod k)
        {
            var reg = k.SettingsReg;
            int bad = 0;
            foreach (var s in reg.All)
            {
                var target = s.Global ? k.Template : k.Current;
                var why = new List<string>();
                if (s.Kind != KitSettingKind.Bool)
                {
                    if (!(s.SliderMin < s.SliderMax)) why.Add("slider min " + s.SliderMin + " >= max " + s.SliderMax);
                    if (s.SliderMin < s.HardMin || s.SliderMax > s.EffectiveHardMax) why.Add("slider " + s.SliderMin + ".." + s.SliderMax + " outside hard " + s.HardMin + ".." + s.EffectiveHardMax);
                    if (s.Default < s.HardMin || s.Default > s.EffectiveHardMax) why.Add("default " + s.Default + " outside hard range");
                }
                float other = KitSuiteUtil.OtherValue(s);
                reg.Set(s, target, other);
                if (!KitSuiteUtil.Near(reg.Value(s, target), s.Clamp(other))) why.Add("set " + other + " read " + reg.Value(s, target));
                if (!reg.IsOverridden(s, target)) why.Add("not stored after set");
                if (s.Kind == KitSettingKind.Bool && !KitSuiteUtil.Near(reg.Value(s, target), other)) why.Add("bool did not flip");
                if (s.Kind != KitSettingKind.Bool)
                {
                    if (s.HardMin > -1e30f)
                    {
                        reg.Set(s, target, s.HardMin - 1000f);
                        if (!KitSuiteUtil.Near(reg.Value(s, target), s.Clamp(s.HardMin))) why.Add("below hard min reads " + reg.Value(s, target));
                    }
                    float hmax = s.EffectiveHardMax;
                    if (hmax < 1e30f)
                    {
                        reg.Set(s, target, hmax * 2f + 1000f);
                        if (!KitSuiteUtil.Near(reg.Value(s, target), s.Clamp(hmax))) why.Add("above hard max reads " + reg.Value(s, target));
                    }
                }
                reg.Reset(s, target);
                if (!KitSuiteUtil.Near(reg.Value(s, target), s.Clamp(s.Default))) why.Add("reset reads " + reg.Value(s, target) + " not the default " + s.Default);
                if (reg.IsOverridden(s, target)) why.Add("still stored after reset");
                if (s.LegacyKey != null)
                {
                    if (s.LegacyToValue == null) why.Add("legacy key without converter");
                    else
                    {
                        var st = reg.StoreFor(s, target);
                        st.Set(s.LegacyKey, s.Default);
                        if (s.StoredIn(st) == null) why.Add("legacy key " + s.LegacyKey + " does not resolve");
                        st.Remove(s.LegacyKey);
                    }
                }
                if (why.Count > 0) { bad++; t.Check(false, s.Key + ": " + string.Join("; ", why)); }
            }
            t.Check(bad == 0, "every registration round-trips (" + reg.All.Count + " settings, " + bad + " bad)");

            var legacy = reg.All.Where(s => s.LegacyScribe != null).Select(s => s.LegacyScribe).Concat(k.Sections.All.Where(s => s.LegacyEnabled != null).Select(s => s.LegacyEnabled)).ToList();
            t.Check(legacy.Count == legacy.Distinct().Count() && !legacy.Contains("kit"), "legacy field names unique and not 'kit' (" + legacy.Count + ")");

            // section switches
            foreach (var sec in k.Sections.All)
            {
                var p = k.Current;
                bool was = p.SectionOn(sec.Id);
                p.SetSection(sec.Id, !was);
                bool flipped = p.SectionOn(sec.Id) == !was && (k.On(sec.Id) == (!was && k.Sections.Available(sec.Id)));
                p.SetSection(sec.Id, was);
                t.Check(flipped && p.SectionOn(sec.Id) == was, "section " + sec.Id + " switches " + (was ? "off" : "on") + " and back (available " + k.Sections.Available(sec.Id) + ")");
            }

            // presets: applying twice changes nothing the second time
            foreach (var pr in k.Presets.All)
            {
                var scratch = k.Template.CloneForNewGame();
                k.SuppressNotifications(() => pr.Apply(scratch));
                var again = k.Presets.Changes(pr, scratch);
                t.Check(again.Count == 0 && k.Presets.IsMatch(pr, scratch), "preset " + pr.Id + " is idempotent and matches after applying" + (again.Count > 0 ? " (changes again: " + string.Join(", ", again) + ")" : ""));
            }

            // template → new game copy
            var tmpl = k.Template; var copy = tmpl.CloneForNewGame();
            var diff = reg.All.Where(s => !s.Global && !KitSuiteUtil.Near(reg.Value(s, copy), reg.Value(s, tmpl))).Select(s => s.Key)
                .Concat(k.Sections.All.Where(sec => copy.SectionOn(sec.Id) != tmpl.SectionOn(sec.Id)).Select(sec => "section:" + sec.Id)).ToList();
            t.Check(diff.Count == 0 && !copy.FirstStartCardDone, "a new game starts with the template's values and switches" + (diff.Count > 0 ? ": differs " + string.Join(", ", diff) : ""));

            // per save: save and template are separate stores; global settings always live in the template
            var save = k.SaveProfile;
            if (k.Info.PerSave)
            {
                t.Check(save != null, "per-save profile exists in this game");
                var s = reg.All.FirstOrDefault(x => !x.Global);
                var g = reg.All.FirstOrDefault(x => x.Global);
                if (save != null && s != null)
                {
                    float tBefore = reg.Value(s, tmpl);
                    reg.Set(s, save, KitSuiteUtil.OtherValue(s));
                    t.Check(KitSuiteUtil.Near(reg.Value(s, tmpl), tBefore) && KitSuiteUtil.Near(k.Get(s.Key), reg.Value(s, save)), s.Key + ": a change in this save leaves the template alone, reads follow the save");
                    reg.Reset(s, save);
                }
                if (save != null && g != null)
                {
                    reg.Set(g, save, KitSuiteUtil.OtherValue(g));
                    t.Check(KitSuiteUtil.Near(reg.Value(g, tmpl), reg.Value(g, save)) && !save.Store.Has(g.Key), g.Key + " (global) is stored in the template, never in the save");
                    reg.Reset(g, save);
                }
            }
            else t.Check(save == null, "not per-save: no save profile");

            // other tenants are not touched by anything above
            foreach (var other in Framework.Mods.Where(m => m != k))
                t.Check(other.SettingsReg.All.All(x => reg.Find(x.Key) == null || ReferenceEquals(reg.Find(x.Key).Owner, k)), "registry of " + other.Name + " is separate");
        }
    }

    /// <summary>kit.defs: every def of this mod has a label (and a description where the game shows one), its textures resolve, and every
    /// translation key of the registry exists in English (Common/Languages/English/Keyed/&lt;Prefix&gt;_Settings.xml, from tools/kit_lang.py).</summary>
    internal sealed class KitDefsSuite : KitTestSuite
    {
        public override string Name => "kit.defs";
        public override int MaxTicks => 3000;

        static readonly HashSet<string> NoLabelTypes = new HashSet<string> { "SoundDef", "EffecterDef", "FleckDef", "ShaderTypeDef", "RulePackDef", "SongDef", "DesignatorDropdownGroupDef", "PawnRenderTreeDef", "SubEffecterDef", "KeyBindingCategoryDef", "MentalBreakDef", "ThingSetMakerDef" };
        static readonly HashSet<string> DescTypes = new HashSet<string> { "TerrainDef", "ResearchProjectDef", "GeneDef", "AbilityDef", "HediffDef", "PreceptDef" };

        public override IEnumerable<int> Run(TestContext t)
        {
            var k = t.Mod;
            var ours = new List<Def>();
            foreach (var type in GenDefDatabase.AllDefTypesWithDatabases())
                foreach (var d in GenDefDatabase.GetAllDefsInDatabaseForDef(type))
                    if (SaveFingerprint.OurDef(d, k)) ours.Add(d);
            t.Note(ours.Count + " defs of this mod: " + string.Join(", ", ours.GroupBy(d => d.GetType().Name).Select(g => g.Key + " " + g.Count())));

            var noLabel = new List<string>(); var noDesc = new List<string>(); var badTex = new List<string>();
            foreach (var d in ours)
            {
                string tn = d.GetType().Name;
                var td = d as ThingDef;
                bool mote = td != null && (td.category == ThingCategory.Mote || td.category == ThingCategory.Ethereal);
                if (!NoLabelTypes.Contains(tn) && !mote && d.label.NullOrEmpty()) noLabel.Add(tn + " " + d.defName);
                bool wantsDesc = DescTypes.Contains(tn) || (td != null && (td.category == ThingCategory.Item || td.category == ThingCategory.Building));
                if (wantsDesc && d.description.NullOrEmpty()) noDesc.Add(tn + " " + d.defName);

                if (d is BuildableDef bd && !mote)
                {
                    bool hasGraphic = (td != null && td.graphicData != null) || (d is TerrainDef ter && !ter.texturePath.NullOrEmpty());
                    if (hasGraphic && (bd.graphic == null || bd.graphic == BaseContent.BadGraphic)) badTex.Add(d.defName + " graphic");
                    if (hasGraphic && td != null && (bd.uiIcon == null || bd.uiIcon == BaseContent.BadTex)) badTex.Add(d.defName + " uiIcon");
                }
                foreach (var f in d.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (f.FieldType != typeof(string)) continue;
                    string n = f.Name.ToLowerInvariant();
                    if (!(n.EndsWith("path") && (n.Contains("icon") || n.Contains("tex")))) continue;
                    if (!(f.GetValue(d) is string path) || path.Length == 0) continue;
                    if (!TextureExists(path)) badTex.Add(d.defName + "." + f.Name + " = " + path);
                }
            }
            t.Check(noLabel.Count == 0, "every def has a label" + (noLabel.Count > 0 ? ": missing " + string.Join(", ", noLabel.Take(20)) : ""));
            t.Check(noDesc.Count == 0, "every shown def has a description" + (noDesc.Count > 0 ? ": missing " + string.Join(", ", noDesc.Take(20)) : ""));
            t.Check(badTex.Count == 0, "every texture resolves" + (badTex.Count > 0 ? ": " + string.Join(", ", badTex.Take(20)) : ""));

            var keys = KitSuiteUtil.RegistryKeys(k);
            var en = LanguageDatabase.defaultLanguage;
            var missing = en == null ? keys.Select(x => x.Key).ToList() : keys.Where(x => !en.HaveTextForKey(x.Key)).Select(x => x.Key).ToList();
            t.Check(missing.Count == 0, "every registry key exists in English (" + keys.Count + " keys" + (missing.Count > 0 ? ", missing " + missing.Count + ": " + string.Join(", ", missing.Take(15)) + " → run `jbmod <Mod> lang --refresh`" : "") + ")");
            // the framework's own texts exist once, in the framework (never copied into a mod)
            var fwKeys = new[] { "JBF.Side.ThisSave", "JBF.Side.Template", "JBF.Header.Search" };
            t.Check(en != null && fwKeys.All(x => en.HaveTextForKey(x)), "the framework's own texts (JBF.*) are loaded");
            yield return 1;
        }

        static bool TextureExists(string path)
        {
            try
            {
                if (ContentFinder<Texture2D>.Get(path, false) != null) return true;
                if (ContentFinder<Texture2D>.Get(path + "_north", false) != null) return true;
                return ContentFinder<Texture2D>.GetAllInFolder(path).Any();
            }
            catch { return false; }
        }
    }

    /// <summary>kit.migration: the slot profile started with the mod's OLD settings file (tools/jbmod migrate-test). Every legacy value
    /// arrived in the template, the .pre-kit backup equals the old file, and the file was rewritten without legacy nodes.</summary>
    internal sealed class KitMigrationSuite : KitTestSuite
    {
        public override string Name => "kit.migration";
        public override int MaxTicks => 3000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var k = t.Mod;
            string orig = KitTestEnv.Get(KitTestEnv.Migration);
            if (string.IsNullOrEmpty(orig) || !File.Exists(orig))
            {
                t.Note("no old settings file provided (JB_TEST_MIGRATION); nothing to check. Run `jbmod <Mod> migrate-test`.");
                yield break;
            }
            t.Check(k.Settings != null, "settings object exists");
            if (k.Settings == null) yield break;
            var old = KitSuiteUtil.SettingsFileFields(orig, out bool oldHadKit);
            t.Check(old != null, "the old settings file is valid XML");
            if (old == null) yield break;
            t.Note("old file: " + old.Count + " fields" + (oldHadKit ? ", already has a kit node" : ""));
            int expected = 0;
            foreach (var s in k.SettingsReg.All.Where(x => x.LegacyScribe != null))
            {
                if (!old.TryGetValue(s.LegacyScribe, out string raw)) { t.Note(s.Key + ": old field " + s.LegacyScribe + " not in the file (default stays)"); continue; }
                if (!s.TryParseLegacy(raw, out float want)) { t.Note(s.Key + ": old value '" + raw + "' unreadable (default stays)"); continue; }
                expected++;
                bool ok = k.Template.Store.TryGet(s.Key, out float got) && KitSuiteUtil.Near(got, want);
                t.Check(ok, s.Key + " = " + s.Format(want) + " (from " + s.LegacyScribe + " '" + raw + "'), read " + (k.Template.Store.TryGet(s.Key, out float g2) ? s.Format(g2) : "nothing"));
            }
            foreach (var sec in k.Sections.All.Where(x => x.LegacyEnabled != null))
            {
                if (!old.TryGetValue(sec.LegacyEnabled, out string raw) || !bool.TryParse(raw.Trim(), out bool want)) continue;
                expected++;
                bool ok = k.Template.sections.TryGetValue(sec.Id, out bool got) && got == want;
                t.Check(ok, "section " + sec.Id + " = " + want + " (from " + sec.LegacyEnabled + ")");
            }
            t.Check(expected > 0 || oldHadKit, "the old file had values to migrate (" + expected + ")");
            if (!oldHadKit) t.Check(k.Settings.MigratedThisSession == (expected > 0), "migration ran this session (" + k.Settings.Migrated.Count + " values)");

            string file = KitSettingsBase.SettingsFilePath(k);
            bool exists = file != null && File.Exists(file);
            t.Check(exists, "settings file exists (" + (file == null ? "?" : Path.GetFileName(file)) + ")");
            if (exists && expected > 0 && !oldHadKit)
            {
                string bak = file + ".pre-kit";
                t.Check(File.Exists(bak) && File.ReadAllBytes(bak).SequenceEqual(File.ReadAllBytes(orig)), "backup " + Path.GetFileName(bak) + " equals the old file");
                var now = KitSuiteUtil.SettingsFileFields(file, out bool nowKit) ?? new Dictionary<string, string>();
                var leftover = k.SettingsReg.All.Where(s => s.LegacyScribe != null && now.ContainsKey(s.LegacyScribe)).Select(s => s.LegacyScribe)
                    .Concat(k.Sections.All.Where(s => s.LegacyEnabled != null && now.ContainsKey(s.LegacyEnabled)).Select(s => s.LegacyEnabled)).ToList();
                t.Check(nowKit && leftover.Count == 0, "file rewritten in the framework format without legacy nodes" + (leftover.Count > 0 ? " (left: " + string.Join(", ", leftover) + ")" : ""));
            }
            yield return 1;
        }
    }

    /// <summary>
    /// kit.saveload, two phases (tools/jbmod saveload): in a new game it changes some per-save values, saves as jb_&lt;shortid&gt;_saveload with a
    /// fingerprint and the values, and puts the live game back; in a game loaded from that save (JB_TEST_LOAD) it compares the fingerprint
    /// and the values, then plays 2500 ticks without a red error of ours.
    /// </summary>
    internal sealed class KitSaveLoadSuite : KitTestSuite
    {
        public override string Name => "kit.saveload";
        public override int MaxTicks => 6000;
        public override bool KeepsLoadedGame => true;

        public override IEnumerable<int> Run(TestContext t)
        {
            var k = t.Mod;
            string save = SaveLoadKit.LoadedSave;
            if (save == null) { foreach (var y in SavePhase(t, k)) yield return y; yield break; }
            t.Note("phase 2: loaded " + save);
            t.Check(FrameworkGame.Instance != null, "the framework's game component exists after the load");
            t.Check(!k.Window.JoinedExistingSave, "the mod's entry was loaded from the save (not created fresh)");
            SaveLoadKit.CompareFingerprints(save, t);
            var want = SaveLoadKit.ReadSide(save, "values");
            if (k.Info.PerSave)
            {
                t.Check(want != null, "values of the save found");
                if (want != null)
                {
                    var now = KitSuiteUtil.ValueLines(k, k.SaveProfile);
                    var d = want.Except(now).ToList();
                    foreach (var l in d.Take(20)) t.Note("saved " + l + ", now " + now.FirstOrDefault(x => x.StartsWith(l.Split('=')[0] + "=")));
                    t.Check(d.Count == 0 && now.Count == want.Count, "every per-save value and switch came back (" + want.Count + ")");
                }
            }
            int mark = KitTestLog.Mark();
            yield return 2500;
            KitSuiteUtil.NoRedErrors(t, 0, "load + 2500 ticks: no red error of ours");
            t.Check(!k.Guard.Paused.Any() && KitTestLog.Since(mark, true).Count == 0, "nothing paused, nothing new while playing");
        }

        IEnumerable<int> SavePhase(TestContext t, KitMod k)
        {
            string name = KitSuiteUtil.SaveName(k);
            t.Note("phase 1: new game → save " + name);
            var snap = KitSuiteUtil.Snapshot.Take(k);
            var changed = new List<string>();
            try
            {
                var sp = k.SaveProfile;
                if (sp != null)
                {
                    foreach (var s in k.SettingsReg.All.Where(x => !x.Global && x.SectionId != KitMod.Core).Concat(k.SettingsReg.All.Where(x => !x.Global)).Distinct().Take(3))
                    { k.SettingsReg.Set(s, sp, KitSuiteUtil.OtherValue(s)); changed.Add(s.Key + "=" + s.Format(k.SettingsReg.Value(s, sp))); }
                    var sec = k.Sections.All.FirstOrDefault(x => k.Sections.Available(x.Id));
                    if (sec != null) { sp.SetSection(sec.Id, !sp.SectionOn(sec.Id)); changed.Add("section:" + sec.Id + "=" + sp.SectionOn(sec.Id)); }
                    k.FlushChanges();
                    t.Note("changed in this save before saving: " + string.Join(", ", changed));
                }
                else t.Note("not per-save: setting values are not part of the save");
                if (SaveLoadKit.SaveWithFingerprint(name, t))
                {
                    SaveLoadKit.WriteSide(name, "values", KitSuiteUtil.ValueLines(k, sp));
                    string text = File.ReadAllText(GenFilePaths.FilePathForSavedGame(name));
                    t.Check(text.Contains("\"" + typeof(FrameworkGame).FullName + "\""), "the save holds the framework's game component " + typeof(FrameworkGame).FullName);
                    if (!k.IsFramework) t.Check(KitSuiteUtil.SaveHasEntry(text, k), "the save holds the mod's entry (" + FrameworkGame.SaveKey(k) + ")");
                    if (sp != null) t.Check(text.Contains("<profile>"), "the save holds the per-save profile");
                    if (k.SaveExpose != null) t.Check(text.Contains("<data>"), "the save holds the mod's SaveData node");
                    t.Check(!text.Contains(typeof(KitTestRunner).Namespace + "."), "nothing of the test harness in the save");
                }
            }
            finally { snap.Restore(); }
            yield return 1;
        }
    }

    /// <summary>kit.addition (explicit): a save made WITHOUT this mod (and without the framework) was loaded with it (tools/jbmod addition).
    /// The entry was created fresh, the save's profile is a copy of the template, and 2500 ticks run without a red error of ours.</summary>
    internal sealed class KitAdditionSuite : KitTestSuite
    {
        public override string Name => "kit.addition";
        public override int MaxTicks => 6000;
        public override bool ExplicitOnly => true;
        public override bool KeepsLoadedGame => true;

        public override IEnumerable<int> Run(TestContext t)
        {
            var k = t.Mod;
            t.Check(SaveLoadKit.LoadedSave != null, "the game was loaded from a save (" + SaveLoadKit.LoadedSave + ")");
            t.Check(FrameworkGame.Instance != null, "the framework's game component was added to the save");
            t.Check(k.Window.JoinedExistingSave, "the mod knows it joined an existing save");
            if (k.Info.PerSave)
            {
                var sp = k.SaveProfile;
                t.Check(sp != null, "the save got a profile");
                if (sp != null)
                {
                    var d = KitSuiteUtil.ValueLines(k, sp).Except(KitSuiteUtil.ValueLines(k, k.Template)).ToList();
                    t.Check(d.Count == 0, "the save's profile is a copy of the template" + (d.Count > 0 ? " (differs: " + string.Join(", ", d.Take(10)) + ")" : ""));
                }
            }
            yield return 2500;
            KitSuiteUtil.NoRedErrors(t, 0, "load + 2500 ticks: no red error of ours");
            t.Check(!k.Guard.Paused.Any(), "nothing paused");
        }
    }

    /// <summary>kit.settingsdump: writes every registry key with its English text to JSON (JB_TEST_DUMP, else &lt;config&gt;/&lt;ShortId&gt;_settings_dump.json)
    /// for tools/kit_lang.py: {"prefix": "XX", "keyed": {"XX.Setting.key": "Label", …}, …}.</summary>
    internal sealed class KitSettingsDumpSuite : KitTestSuite
    {
        public override string Name => "kit.settingsdump";
        public override int MaxTicks => 600;

        public override IEnumerable<int> Run(TestContext t)
        {
            var k = t.Mod;
            string path = KitTestEnv.Get(KitTestEnv.Dump);
            if (string.IsNullOrEmpty(path)) path = Path.Combine(GenFilePaths.ConfigFolderPath, (k.Info?.ShortId ?? "JB") + "_settings_dump.json");
            var keys = KitSuiteUtil.RegistryKeys(k);
            var reg = k.SettingsReg.All;
            var sb = new StringBuilder();
            sb.Append("{\n  \"prefix\": ").Append(J(k.Info.KeyPrefix)).Append(",\n  \"mod\": ").Append(J(k.Info?.Name)).Append(",\n  \"modVersion\": ").Append(J(k.Info?.Version))
              .Append(",\n  \"frameworkVersion\": ").Append(J(Framework.Version)).Append(",\n  \"apiLevel\": ").Append(Framework.ApiLevel).Append(",\n  \"keyed\": {\n");
            for (int i = 0; i < keys.Count; i++) sb.Append("    ").Append(J(keys[i].Key)).Append(": ").Append(J(keys[i].Value)).Append(i + 1 < keys.Count ? ",\n" : "\n");
            sb.Append("  },\n  \"settings\": [\n");
            for (int i = 0; i < reg.Count; i++)
            {
                var s = reg[i];
                sb.Append("    {\"key\": ").Append(J(s.Key)).Append(", \"section\": ").Append(J(s.SectionId)).Append(", \"category\": ").Append(J(s.Category))
                  .Append(", \"kind\": ").Append(J(s.Kind.ToString())).Append(", \"default\": ").Append(KitSuiteUtil.F(s.Default))
                  .Append(", \"min\": ").Append(KitSuiteUtil.F(s.SliderMin)).Append(", \"max\": ").Append(KitSuiteUtil.F(s.SliderMax))
                  .Append(", \"global\": ").Append(s.Global ? "true" : "false").Append(", \"legacy\": ").Append(J(s.LegacyScribe)).Append('}')
                  .Append(i + 1 < reg.Count ? ",\n" : "\n");
            }
            sb.Append("  ],\n  \"sections\": [").Append(string.Join(", ", k.Sections.All.Select(s => J(s.Id)))).Append("]\n}\n");
            bool ok;
            try { File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false)); ok = true; }
            catch (Exception e) { t.Note("write failed: " + e.Message); ok = false; }
            t.Check(ok, "dump written: " + keys.Count + " keys, " + reg.Count + " settings (" + Path.GetFileName(path) + ")");
            yield return 1;
        }

        static string J(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}

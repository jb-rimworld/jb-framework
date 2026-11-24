// SPDX-License-Identifier: MIT
// Rewritten small from Hearth & Harbor Foundation/Modules.cs (string ids registered in code instead of an enum); one registry per mod.
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace JBFramework
{
    /// <summary>A switchable feature area of a mod ("Bereich"): label, description, icon, default, availability and callbacks.</summary>
    public sealed class KitSection
    {
        internal KitSection() { }
        internal KitMod Owner;
        internal string LabelEn, DescEn;
        internal string Icon;
        internal bool DefaultOn = true;
        internal Func<bool> AvailableFunc;
        internal string RequiresText;
        internal int Order;
        internal Action<bool> SwitchedFunc;
        internal Func<IEnumerable<string>> DiagnosisFunc;
        internal Action<List<string>> PrepareRemoval;
        internal string LegacyEnabledField;
        internal string RequiresPackageId, RequiresModName;

        /// <summary>The section id ("walls"). Settings, pages and caretaker checks refer to it.</summary>
        public string Id { get; internal set; }
        /// <summary>Translated label (key &lt;Prefix&gt;.Section.&lt;Id&gt;), English fallback.</summary>
        public string Label => Owner.Key("Section." + Id).TranslateOr(LabelEn ?? Id);
        /// <summary>Translated description (key &lt;Prefix&gt;.Section.&lt;Id&gt;.Desc), English fallback.</summary>
        public string Desc => Owner.Key("Section." + Id + ".Desc").TranslateOr(DescEn ?? "");
        internal string RequiresLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(RequiresText)) return Owner.Key("Section." + Id + ".Requires").TranslateOr(RequiresText);
                if (string.IsNullOrEmpty(RequiresPackageId)) return null;
                return KitText.K("Section.NeedsMod", "Needs {0}.", RequiresModName ?? KitDetect.ActiveName(RequiresPackageId) ?? KitKnown.Note(RequiresPackageId)?.label ?? KitDlcName(RequiresPackageId) ?? RequiresPackageId);
            }
        }

        static string KitDlcName(string id) { foreach (var d in KitCompat.Dlcs) if (KitCompat.Same(d.Id, id)) return d.Name; return null; }

        // Modifiers (return this) --------------------------------------------------------------------------------------
        /// <summary>The description (shown on the Overview page and the section page; key &lt;Prefix&gt;.Section.&lt;Id&gt;.Desc).</summary>
        public KitSection Describe(string desc) { DescEn = desc; return this; }
        /// <summary>Icon of the section page (texture path such as "UI/MyMod/Walls"). A missing texture draws nothing.</summary>
        public KitSection WithIcon(string texPath) { Icon = texPath; return this; }
        /// <summary>The section starts switched off (new games and the template).</summary>
        public KitSection OffByDefault() { DefaultOn = false; return this; }
        /// <summary>
        /// The section exists only while the mod or DLC <paramref name="packageIdOrDlc"/> is active; otherwise it is greyed with "Needs &lt;name&gt;."
        /// (name of the active mod, of the known-mods list, of the DLC, else the packageId).
        /// </summary>
        public KitSection OnlyWith(string packageIdOrDlc) => OnlyWith(packageIdOrDlc, null);
        /// <summary>Like <see cref="OnlyWith(string)"/> with the name shown in "Needs &lt;modName&gt;.".</summary>
        public KitSection OnlyWith(string packageIdOrDlc, string modName)
        {
            AvailableFunc = () => KitCompat.IsModActive(packageIdOrDlc);
            RequiresPackageId = packageIdOrDlc; RequiresModName = modName; RequiresText = null;
            return this;
        }
        /// <summary>The section exists only while <paramref name="available"/> is true; <paramref name="whyNot"/> says why not ("Needs a map with water.").</summary>
        public KitSection AvailableWhen(Func<bool> available, string whyNot) { AvailableFunc = available; RequiresText = whyNot; RequiresPackageId = null; return this; }
        /// <summary>Position among the sections (default: registration order).</summary>
        public KitSection WithOrder(int order) { Order = order; Owner?.Sections.Resort(); return this; }
        /// <summary>Called when the section is switched in the current profile (true = on). Runs even while the section is paused, so it can settle what it holds.</summary>
        public KitSection WhenSwitched(Action<bool> onSwitched) { SwitchedFunc = onSwitched; return this; }
        /// <summary>Extra lines for the section on the Diagnosis page and in the report. Fenced: a throwing function shows an error line.</summary>
        public KitSection WithDiagnosis(Func<IEnumerable<string>> lines) { DiagnosisFunc = lines; return this; }
        /// <summary>
        /// "Prepare for removal": undoes what the section put into the running save (things, bills, hediffs; see <see cref="KitRemoval"/>)
        /// and appends one report line per action. Offered on the section page and the Diagnosis page while a game runs.
        /// </summary>
        public KitSection WithRemoval(Action<List<string>> prepareRemoval)
        {
            var before = PrepareRemoval;
            PrepareRemoval = before == null ? prepareRemoval : (report => { before(report); prepareRemoval?.Invoke(report); });
            return this;
        }
        /// <summary>The mod's OLD settings field (a bool Scribe_Values name) that held this switch: migrated once.</summary>
        public KitSection LegacyEnabled(string oldScribeName) { LegacyEnabledField = oldScribeName; return this; }
    }

    /// <summary>All sections of one mod and the one question every patch asks (<see cref="KitMod.On"/>).</summary>
    internal sealed class KitSectionRegistry
    {
        readonly KitMod k;
        readonly Dictionary<string, KitSection> byId = new Dictionary<string, KitSection>();
        /// <summary>Registered sections, sorted by Order then registration.</summary>
        public readonly List<KitSection> All = new List<KitSection>();
        public KitSectionRegistry(KitMod owner) { k = owner; }

        public KitSection Add(KitSection s)
        {
            if (s == null || string.IsNullOrEmpty(s.Id) || s.Id == KitMod.Core) return null;
            if (byId.TryGetValue(s.Id, out var existing)) return existing;
            s.Owner = k;
            if (s.Order == 0) s.Order = (All.Count + 1) * 10;
            byId[s.Id] = s; All.Add(s);
            Resort();
            return s;
        }

        /// <summary>Stable sort by Order (equal orders keep registration order).</summary>
        public void Resort() { var o = All.ToList(); All.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : o.IndexOf(a).CompareTo(o.IndexOf(b))); }

        public KitSection Find(string id) => id != null && byId.TryGetValue(id, out var s) ? s : null;

        /// <summary>Is the section's code possible here (DLC/mod present)? Core: true; unknown ids: false.</summary>
        public bool Available(string id)
        {
            if (id == null || id == KitMod.Core) return true;
            var s = Find(id);
            if (s == null) return false;
            if (s.AvailableFunc == null) return true;
            try { return s.AvailableFunc(); }
            catch (Exception e) { k.Log.Once("avail:" + id, KitLogLevel.Warn, id, "Availability check threw, section treated as unavailable: " + e.Message); return false; }
        }

        /// <summary>Available, not paused, switched on in the current profile. Unknown ids are a programming error: logged once, off.</summary>
        public bool On(string id)
        {
            if (id == null || id == KitMod.Core) return true;
            if (!byId.ContainsKey(id)) { k.Log.Once("unknownSection:" + id, KitLogLevel.Error, "Core", "Unknown section id '" + id + "' (not registered); treated as off"); return false; }
            if (!Available(id)) return false;
            if (k.Guard.IsPaused(id)) return false;
            return k.Current.SectionOn(id);
        }

        /// <summary>Why a section is not on (null when it is): not available, paused, or switched off.</summary>
        public string WhyOff(string id)
        {
            if (On(id)) return null;
            var s = Find(id);
            if (s == null) return "unknown section";
            if (!Available(id)) return s.RequiresLabel ?? KitText.K("Section.NotAvailable", "Not available in this game.");
            if (k.Guard.IsPaused(id)) return k.Guard.ReasonFor(id);
            return KitText.K("Section.SwitchedOff", "Switched off.");
        }

        public string LabelOf(string id) => id == null || id == KitMod.Core ? k.Name : Find(id)?.Label ?? id;

        internal void NotifySwitched(string id, bool on)
        {
            var s = Find(id);
            if (s?.SwitchedFunc == null) return;
            // not Guard.Run: a paused section must still be able to settle what it holds when switched off
            try { s.SwitchedFunc(on); }
            catch (Exception e) { k.Log.Error(id, "OnSwitched(" + on + ") failed", e); }
            k.Log.Info(id, "Section switched " + (on ? "on" : "off"));
        }

        /// <summary>Runs the section's PrepareRemoval (if any) and switches it off in the current profile. Returns the report lines.</summary>
        public List<string> PrepareRemoval(string id)
        {
            var report = new List<string>();
            var s = Find(id);
            if (s == null) return report;
            if (s.PrepareRemoval != null)
            {
                try { s.PrepareRemoval(report); }
                catch (Exception e) { k.Log.Error(id, "PrepareRemoval failed", e); report.Add("Error: " + e.Message); }
            }
            k.Current.SetSection(id, false);
            foreach (var line in report) k.Log.Info(id, "Prepare removal: " + line);
            return report;
        }
    }
}

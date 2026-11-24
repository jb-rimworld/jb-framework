// SPDX-License-Identifier: MIT
// Rewritten small from Hearth & Harbor Foundation/Modules.cs (string ids registered in code instead of an enum).
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace JBKit
{
    /// <summary>A switchable feature area ("Bereich"): label, description, icon, default, availability and callbacks.</summary>
    internal sealed class KitSection
    {
        public string Id;
        public string LabelEn, DescEn;
        /// <summary>Icon texture path (optional, e.g. "UI/LO/Kit/Mood"); src/UI draws nothing when it is missing.</summary>
        public string Icon;
        public bool DefaultOn = true;
        /// <summary>Is the section possible at all (DLC or mod present)? Null = always. Exceptions count as "not available".</summary>
        public Func<bool> AvailableFunc;
        /// <summary>Player-facing text when not available ("needs Biotech").</summary>
        public string RequiresText;
        public int Order;
        /// <summary>Called when the section of the current profile is switched (true = on). Guarded.</summary>
        public Action<bool> OnSwitched;
        /// <summary>Extra lines for the diagnosis page. Guarded.</summary>
        public Func<IEnumerable<string>> Diagnosis;
        /// <summary>Optional "prepare for removal" action: undo what the section put into the save, append report lines.</summary>
        public Action<List<string>> PrepareRemoval;
        /// <summary>Old settings-file field holding the on/off switch (e.g. "Walls_enabled"), migrated once.</summary>
        public string LegacyEnabled;

        public string Label => KitText.Key("Section." + Id).TranslateOr(LabelEn ?? Id);
        public string Desc => KitText.Key("Section." + Id + ".Desc").TranslateOr(DescEn ?? "");
        public string RequiresLabel => string.IsNullOrEmpty(RequiresText) ? null : KitText.Key("Section." + Id + ".Requires").TranslateOr(RequiresText);
    }

    /// <summary>All sections of this mod and the one question every patch asks: <c>KitSections.On("walls")</c> (also <c>Kit.On</c>).</summary>
    internal static class KitSections
    {
        /// <summary>The always-on pseudo section for general settings and kit code. Never paused, never switched.</summary>
        public const string Core = "core";
        static readonly Dictionary<string, KitSection> byId = new Dictionary<string, KitSection>();
        /// <summary>Registered sections, sorted by Order then registration.</summary>
        public static readonly List<KitSection> All = new List<KitSection>();

        public static KitSection Add(KitSection s)
        {
            if (s == null || string.IsNullOrEmpty(s.Id) || s.Id == Core) return null;
            if (byId.TryGetValue(s.Id, out var existing)) return existing;
            if (s.Order == 0) s.Order = (All.Count + 1) * 10;
            byId[s.Id] = s; All.Add(s);
            All.Sort((a, b) => a.Order.CompareTo(b.Order));
            return s;
        }

        public static KitSection Find(string id) => id != null && byId.TryGetValue(id, out var s) ? s : null;

        /// <summary>Is the section's code possible here (DLC/mod present)? Core and unknown ids: true / false.</summary>
        public static bool Available(string id)
        {
            if (id == null || id == Core) return true;
            var s = Find(id);
            if (s == null) return false;
            if (s.AvailableFunc == null) return true;
            try { return s.AvailableFunc(); }
            catch (Exception e) { KitLog.Once("avail:" + id, KitLogLevel.Warn, id, "Availability check threw, section treated as unavailable: " + e.Message); return false; }
        }

        /// <summary>
        /// True if the section is active right now: available, not paused by the error barrier, and switched on in the current profile
        /// (the save when per-save and a game runs, else the template). Every patch of a section returns early when this is false.
        /// Unknown ids are a programming error: logged once (red), treated as off.
        /// </summary>
        public static bool On(string id)
        {
            if (id == null || id == Core) return true;
            if (!byId.ContainsKey(id)) { KitLog.Once("unknownSection:" + id, KitLogLevel.Error, "Core", "Unknown section id '" + id + "' (not registered); treated as off"); return false; }
            if (!Available(id)) return false;
            if (KitGuard.IsPaused(id)) return false;
            return Kit.Current.SectionOn(id);
        }

        /// <summary>Why a section is not on (null when it is): not available, paused, or switched off.</summary>
        public static string WhyOff(string id)
        {
            if (On(id)) return null;
            var s = Find(id);
            if (s == null) return "unknown section";
            if (!Available(id)) return s.RequiresLabel ?? KitText.K("Section.NotAvailable", "Not available in this game.");
            if (KitGuard.IsPaused(id)) return KitGuard.ReasonFor(id);
            return KitText.K("Section.SwitchedOff", "Switched off.");
        }

        public static string LabelOf(string id) => id == null || id == Core ? (Kit.Info?.Name ?? "Core") : Find(id)?.Label ?? id;

        internal static void NotifySwitched(string id, bool on)
        {
            var s = Find(id);
            if (s?.OnSwitched == null) return;
            // not Guard.Run: a paused section must still be able to settle what it holds when switched off
            try { s.OnSwitched(on); }
            catch (Exception e) { KitLog.Error(id, "OnSwitched(" + on + ") failed", e); }
            KitLog.Info(id, "Section switched " + (on ? "on" : "off"));
        }

        /// <summary>Runs the section's PrepareRemoval (if any) and switches it off in the current profile. Returns the report lines.</summary>
        public static List<string> PrepareRemoval(string id)
        {
            var report = new List<string>();
            var s = Find(id);
            if (s == null) return report;
            if (s.PrepareRemoval != null)
            {
                try { s.PrepareRemoval(report); }
                catch (Exception e) { KitLog.Error(id, "PrepareRemoval failed", e); report.Add("Error: " + e.Message); }
            }
            Kit.Current.SetSection(id, false);
            foreach (var line in report) KitLog.Info(id, "Prepare removal: " + line);
            return report;
        }

        internal static void ClearForTests() { byId.Clear(); All.Clear(); }
    }
}

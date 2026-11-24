// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace JBKit
{
    /// <summary>
    /// A named set of values ("Subtle", "Generous", "Absurd"). <see cref="Apply"/> changes only what the preset sets (via
    /// <see cref="KitProfile.Set(string,float)"/> / <see cref="KitProfile.SetSection"/>); everything else stays.
    /// </summary>
    internal sealed class KitPreset
    {
        public string Id;
        public string LabelEn, DescEn;
        public Action<KitProfile> Apply;
        /// <summary>Optional custom matcher. Default: applying the preset to a copy would change nothing.</summary>
        public Func<KitProfile, bool> Matches;
        public string Label => KitText.Key("Preset." + Id).TranslateOr(LabelEn ?? Id);
        public string Desc => KitText.Key("Preset." + Id + ".Desc").TranslateOr(DescEn ?? "");
    }

    internal static class KitPresets
    {
        public static readonly List<KitPreset> All = new List<KitPreset>();

        public static KitPreset Add(KitPreset p)
        {
            if (p == null || string.IsNullOrEmpty(p.Id) || p.Apply == null) return null;
            var existing = Find(p.Id);
            if (existing != null) return existing;
            All.Add(p);
            return p;
        }
        public static KitPreset Find(string id) => All.FirstOrDefault(p => p.Id == id);

        /// <summary>
        /// Keys (settings and "section:&lt;id&gt;") whose effective value the preset would change in <paramref name="target"/>.
        /// Runs the preset against a scratch copy; callbacks do not fire. Global settings are compared against the template.
        /// </summary>
        public static List<string> Changes(KitPreset p, KitProfile target)
        {
            var result = new List<string>();
            if (p == null || target == null) return result;
            var scratch = target.CloneForNewGame();
            Kit.SuppressNotifications(() =>
            {
                try { p.Apply(scratch); }
                catch (Exception e) { KitLog.Error("Presets", "Preset " + p.Id + " threw while comparing", e); }
            });
            foreach (var s in KitSettings.All)
            {
                if (s.Global) continue;
                if (Math.Abs(KitSettings.Value(s, target) - KitSettings.Value(s, scratch)) > 1e-6f) result.Add(s.Key);
            }
            foreach (var sec in KitSections.All)
                if (target.SectionOn(sec.Id) != scratch.SectionOn(sec.Id)) result.Add("section:" + sec.Id);
            return result;
        }

        /// <summary>True when applying the preset would change nothing (or the custom matcher says so).</summary>
        public static bool IsMatch(KitPreset p, KitProfile target)
        {
            if (p == null || target == null) return false;
            if (p.Matches != null)
            {
                try { return p.Matches(target); }
                catch (Exception e) { KitLog.Once("presetMatch:" + p.Id, KitLogLevel.Warn, "Presets", "Matcher of preset " + p.Id + " threw: " + e.Message); return false; }
            }
            return Changes(p, target).Count == 0;
        }

        /// <summary>The first preset that matches, or null ("Custom").</summary>
        public static KitPreset Matching(KitProfile target) => All.FirstOrDefault(p => IsMatch(p, target));

        /// <summary>Keys the preset would overwrite that the player had changed from their defaults (ask before applying when non-empty).</summary>
        public static List<string> WouldOverwrite(KitPreset p, KitProfile target)
        {
            var list = new List<string>();
            foreach (var k in Changes(p, target))
            {
                if (k.StartsWith("section:")) { if (target.SectionStored(k.Substring(8))) list.Add(k); continue; }
                var s = KitSettings.Find(k);
                if (s != null && KitSettings.IsOverridden(s, target)) list.Add(k);
            }
            return list;
        }

        /// <summary>Applies the preset to <paramref name="target"/> (guarded). Callbacks and OnSettingsChanged follow as for manual edits.</summary>
        public static bool Apply(KitPreset p, KitProfile target)
        {
            if (p == null || target == null) return false;
            bool ok = KitGuard.Run(KitSections.Core, "Preset." + p.Id, () => { p.Apply(target); return true; }, false);
            if (ok) KitLog.Info("Presets", "Preset applied: " + p.Id + (ReferenceEquals(target, Kit.Template) ? " (template)" : ""));
            return ok;
        }
    }
}

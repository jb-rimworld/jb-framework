// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// A named set of values ("Subtle", "Generous", "Absurd"). Applying it changes only what the preset sets (via
    /// <see cref="KitProfile.Set(string, float)"/> / <see cref="KitProfile.SetSection"/>); everything else stays.
    /// </summary>
    public sealed class KitPreset
    {
        internal KitPreset() { }
        internal KitMod Owner;
        internal string LabelEn, DescEn;
        internal Action<KitProfile> Apply;
        internal Func<KitProfile, bool> Matches;

        /// <summary>The preset id.</summary>
        public string Id { get; internal set; }
        /// <summary>Translated label (key &lt;Prefix&gt;.Preset.&lt;Id&gt;), English fallback.</summary>
        public string Label => Owner.Key("Preset." + Id).TranslateOr(LabelEn ?? Id);
        /// <summary>Translated description (key &lt;Prefix&gt;.Preset.&lt;Id&gt;.Desc), English fallback.</summary>
        public string Desc => Owner.Key("Preset." + Id + ".Desc").TranslateOr(DescEn ?? "");
    }

    internal sealed class KitPresetRegistry
    {
        readonly KitMod k;
        public readonly List<KitPreset> All = new List<KitPreset>();
        public KitPresetRegistry(KitMod owner) { k = owner; }

        public KitPreset Add(KitPreset p)
        {
            if (p == null || string.IsNullOrEmpty(p.Id) || p.Apply == null) return null;
            var existing = Find(p.Id);
            if (existing != null) return existing;
            p.Owner = k;
            All.Add(p);
            return p;
        }
        public KitPreset Find(string id) => All.FirstOrDefault(p => p.Id == id);

        /// <summary>
        /// Keys (settings and "section:&lt;id&gt;") whose effective value the preset would change in <paramref name="target"/>.
        /// Runs the preset against a scratch copy; callbacks do not fire. Global settings are not compared.
        /// </summary>
        public List<string> Changes(KitPreset p, KitProfile target)
        {
            var result = new List<string>();
            if (p == null || target == null) return result;
            var scratch = target.CloneForNewGame();
            scratch.Owner = k;
            k.SuppressNotifications(() =>
            {
                try { p.Apply(scratch); }
                catch (Exception e) { k.Log.Error("Presets", "Preset " + p.Id + " threw while comparing", e); }
            });
            foreach (var s in k.SettingsReg.All)
            {
                if (s.Global) continue;
                if (Math.Abs(k.SettingsReg.Value(s, target) - k.SettingsReg.Value(s, scratch)) > 1e-6f) result.Add(s.Key);
            }
            foreach (var sec in k.Sections.All)
                if (target.SectionOn(sec.Id) != scratch.SectionOn(sec.Id)) result.Add("section:" + sec.Id);
            return result;
        }

        /// <summary>True when applying the preset would change nothing (or the custom matcher says so).</summary>
        public bool IsMatch(KitPreset p, KitProfile target)
        {
            if (p == null || target == null) return false;
            if (p.Matches != null)
            {
                try { return p.Matches(target); }
                catch (Exception e) { k.Log.Once("presetMatch:" + p.Id, KitLogLevel.Warn, "Presets", "Matcher of preset " + p.Id + " threw: " + e.Message); return false; }
            }
            return Changes(p, target).Count == 0;
        }

        /// <summary>The first preset that matches, or null ("Custom").</summary>
        public KitPreset Matching(KitProfile target) => All.FirstOrDefault(p => IsMatch(p, target));

        /// <summary>Keys the preset would overwrite that the player had changed from their defaults (ask before applying when non-empty).</summary>
        public List<string> WouldOverwrite(KitPreset p, KitProfile target)
        {
            var list = new List<string>();
            foreach (var key in Changes(p, target))
            {
                if (key.StartsWith("section:")) { if (target.SectionStored(key.Substring(8))) list.Add(key); continue; }
                var s = k.SettingsReg.Find(key);
                if (s != null && k.SettingsReg.IsOverridden(s, target)) list.Add(key);
            }
            return list;
        }

        /// <summary>Applies the preset to <paramref name="target"/> (fenced). Callbacks and OnSettingsChanged follow as for manual edits.</summary>
        public bool Apply(KitPreset p, KitProfile target)
        {
            if (p == null || target == null) return false;
            bool ok = k.Guard.Run(KitMod.Core, "Preset." + p.Id, () => { p.Apply(target); return true; }, false);
            if (ok) k.Log.Info("Presets", "Preset applied: " + p.Id + (ReferenceEquals(target, k.Template) ? " (template)" : ""));
            return ok;
        }
    }
}

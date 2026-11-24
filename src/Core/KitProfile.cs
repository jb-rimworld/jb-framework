// SPDX-License-Identifier: MIT
// Reduced from Hearth & Harbor Foundation/HearthProfile.cs (no tier, content intensity, underworld).
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace JBKit
{
    /// <summary>
    /// Everything game-relevant for one save: section switches and setting values, plus the first-start card state.
    /// The template (in the settings file, <see cref="Kit.Template"/>) has the same shape and is copied into every new game
    /// (and into an existing save the mod joins). Global settings always live in the template's store.
    /// </summary>
    internal sealed class KitProfile : IExposable
    {
        public const int CurrentVersion = 1;
        public Dictionary<string, bool> sections = new Dictionary<string, bool>();
        public KitStore Store = new KitStore();
        public int ProfileVersion = CurrentVersion;
        public bool FirstStartCardDone;

        /// <summary>The section switch stored in this profile, or the section's default. Does not look at availability or pauses (see KitSections.On).</summary>
        public bool SectionOn(string id)
        {
            if (id == null || id == KitSections.Core) return true;
            if (sections.TryGetValue(id, out bool on)) return on;
            var s = KitSections.Find(id);
            return s == null || s.DefaultOn;
        }

        /// <summary>True when the switch is stored here (changed from the default at some point).</summary>
        public bool SectionStored(string id) => id != null && sections.ContainsKey(id);

        /// <summary>
        /// Switches a section. Only writes when the effective value changes; returns true if it did. When this is the running game's
        /// profile (or the template while it is current), the section's OnSwitched callback runs and the mod gets OnSettingsChanged("section:&lt;id&gt;").
        /// </summary>
        public bool SetSection(string id, bool on)
        {
            if (id == null || id == KitSections.Core) return false;
            bool was = SectionOn(id);
            sections[id] = on;
            if (was == on) return false;
            if (ReferenceEquals(this, Kit.Current)) KitSections.NotifySwitched(id, on);
            Kit.MarkChanged("section:" + id);
            return true;
        }

        /// <summary>Forgets a stored switch (back to the section default).</summary>
        public void ResetSection(string id)
        {
            if (id == null || !sections.ContainsKey(id)) return;
            bool was = SectionOn(id);
            sections.Remove(id);
            bool now = SectionOn(id);
            if (was != now)
            {
                if (ReferenceEquals(this, Kit.Current)) KitSections.NotifySwitched(id, now);
                Kit.MarkChanged("section:" + id);
            }
        }

        // Convenience for presets and mod code: value access against THIS profile -------------------------------
        public float Get(string key) { var s = KitSettings.Find(key); return s == null ? 0f : KitSettings.Value(s, this); }
        public bool GetBool(string key) => Get(key) >= 0.5f;
        public void Set(string key, float v)
        {
            var s = KitSettings.Find(key);
            if (s == null) { KitLog.Once("profileSet:" + key, KitLogLevel.Error, "Settings", "Profile.Set: unknown setting key " + key); return; }
            KitSettings.Set(s, this, v);
        }
        public void Set(string key, bool v) => Set(key, v ? 1f : 0f);
        public void Reset(string key) { var s = KitSettings.Find(key); if (s != null) KitSettings.Reset(s, this); }

        /// <summary>Copy for a new game (or a save the mod joins): switches and values; the card is shown again.</summary>
        public KitProfile CloneForNewGame()
        {
            var p = new KitProfile { Store = Store.Clone(), FirstStartCardDone = false, ProfileVersion = ProfileVersion };
            foreach (var kv in sections) p.sections[kv.Key] = kv.Value;
            return p;
        }

        /// <summary>
        /// Replaces switches and non-global values with those of <paramref name="src"/> ("save as template", "apply template to this save").
        /// Global values of this profile are kept. Switch callbacks run for sections that change while this profile is current.
        /// </summary>
        public void CopySettingsFrom(KitProfile src)
        {
            if (src == null || ReferenceEquals(src, this)) return;
            var next = src.Store.Clone();
            foreach (var s in KitSettings.All)
            {
                if (!s.Global) continue;
                next.Remove(s.Key);
                if (Store.TryGet(s.Key, out float v)) next.Set(s.Key, v);
                next.freeText.Remove(s.Key);
                if (Store.freeText.TryGetValue(s.Key, out bool f)) next.freeText[s.Key] = f;
            }
            var before = KitSections.All.ToDictionary(x => x.Id, x => SectionOn(x.Id));
            var changedKeys = KitSettings.All.Where(s => !s.Global && Math.Abs(KitSettings.Value(s, this) - (s.StoredIn(next) ?? s.Clamp(s.Default))) > 1e-7f).Select(s => s.Key).ToList();
            Store = next;
            sections = new Dictionary<string, bool>(src.sections);
            foreach (var k in changedKeys) Kit.MarkChanged(k);
            foreach (var kv in before)
            {
                bool now = SectionOn(kv.Key);
                if (now == kv.Value) continue;
                if (ReferenceEquals(this, Kit.Current)) KitSections.NotifySwitched(kv.Key, now);
                Kit.MarkChanged("section:" + kv.Key);
            }
        }

        List<string> tmpK; List<bool> tmpV;
        public void ExposeData()
        {
            Scribe_Values.Look(ref ProfileVersion, "profileVersion", CurrentVersion);
            Scribe_Collections.Look(ref sections, "sections", LookMode.Value, LookMode.Value, ref tmpK, ref tmpV);
            Scribe_Values.Look(ref FirstStartCardDone, "firstStartCardDone");
            Scribe_Deep.Look(ref Store, "settings");
            if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (sections == null) sections = new Dictionary<string, bool>();
                if (Store == null) Store = new KitStore();
            }
        }
    }
}

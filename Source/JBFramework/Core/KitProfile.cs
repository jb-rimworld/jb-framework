// SPDX-License-Identifier: MIT
// Reduced from Hearth & Harbor Foundation/HearthProfile.cs (no tier, content intensity, underworld).
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// One mod's values for one save: section switches and setting values, plus the welcome-card state. The template (in the mod's
    /// settings file, <see cref="KitMod.Template"/>) has the same shape and is copied into every new game (and into an existing save
    /// the mod joins). Global settings always live in the template's store. Presets get a profile to change.
    /// Plain values only: a save keeps a removed mod's profile without needing any of its types.
    /// </summary>
    public sealed class KitProfile : IExposable
    {
        internal const int CurrentVersion = 1;
        internal KitMod Owner;
        internal Dictionary<string, bool> sections = new Dictionary<string, bool>();
        internal KitStore Store = new KitStore();
        internal int ProfileVersion = CurrentVersion;
        internal bool FirstStartCardDone;

        /// <summary>For the save system. Mods get profiles from <see cref="KitMod"/> and presets, never by creating one.</summary>
        public KitProfile() { }

        /// <summary>The section switch stored in this profile, or the section's default. Does not look at availability or pauses (see <see cref="KitMod.On"/>).</summary>
        public bool SectionOn(string id)
        {
            if (id == null || id == KitMod.Core) return true;
            if (sections.TryGetValue(id, out bool on)) return on;
            var s = Owner?.Sections.Find(id);
            return s == null || s.DefaultOn;
        }

        /// <summary>True when the switch is stored here (changed from the default at some point).</summary>
        public bool SectionStored(string id) => id != null && sections.ContainsKey(id);

        /// <summary>
        /// Switches a section. Only writes when the effective value changes; returns true if it did. When this is the running game's
        /// profile (or the template while it is current), the section's onSwitched callback runs and the mod gets OnSettingsChanged("section:&lt;id&gt;").
        /// </summary>
        public bool SetSection(string id, bool on)
        {
            if (id == null || id == KitMod.Core) return false;
            bool was = SectionOn(id);
            sections[id] = on;
            if (was == on) return false;
            if (Owner != null && ReferenceEquals(this, Owner.Current)) Owner.Sections.NotifySwitched(id, on);
            Owner?.MarkChanged("section:" + id);
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
                if (Owner != null && ReferenceEquals(this, Owner.Current)) Owner.Sections.NotifySwitched(id, now);
                Owner?.MarkChanged("section:" + id);
            }
        }

        // Convenience for presets and mod code: value access against THIS profile -------------------------------
        /// <summary>Value of a setting in this profile (global settings: the template's value; 0 for unknown keys).</summary>
        public float Get(string key) { var s = Owner?.SettingsReg.Find(key); return s == null ? 0f : Owner.SettingsReg.Value(s, this); }
        /// <summary>Value of a Bool setting in this profile.</summary>
        public bool GetBool(string key) => Get(key) >= 0.5f;
        /// <summary>Value of an Int, Enum or Silver setting in this profile, rounded.</summary>
        public int GetInt(string key) => (int)Math.Round(Get(key));
        /// <summary>Sets a setting in this profile (clamped).</summary>
        public void Set(string key, float value)
        {
            var s = Owner?.SettingsReg.Find(key);
            if (s == null) { Owner?.Log.Once("profileSet:" + key, KitLogLevel.Error, "Settings", "Profile.Set: unknown setting key " + key); return; }
            Owner.SettingsReg.Set(s, this, value);
        }
        /// <summary>Sets a Bool setting in this profile.</summary>
        public void Set(string key, bool value) => Set(key, value ? 1f : 0f);
        /// <summary>Resets a setting in this profile to its default.</summary>
        public void Reset(string key) { var s = Owner?.SettingsReg.Find(key); if (s != null) Owner.SettingsReg.Reset(s, this); }

        /// <summary>Copy for a new game (or a save the mod joins): switches and values; the card is shown again.</summary>
        internal KitProfile CloneForNewGame()
        {
            var p = new KitProfile { Owner = Owner, Store = Store.Clone(), FirstStartCardDone = false, ProfileVersion = ProfileVersion };
            foreach (var kv in sections) p.sections[kv.Key] = kv.Value;
            return p;
        }

        /// <summary>
        /// Replaces switches and non-global values with those of <paramref name="src"/> ("save as template", "apply template to this save").
        /// Global values of this profile are kept. Switch callbacks run for sections that change while this profile is current.
        /// </summary>
        internal void CopySettingsFrom(KitProfile src)
        {
            if (src == null || ReferenceEquals(src, this) || Owner == null) return;
            var reg = Owner.SettingsReg;
            var next = src.Store.Clone();
            foreach (var s in reg.All)
            {
                if (!s.Global) continue;
                next.Remove(s.Key);
                if (Store.TryGet(s.Key, out float v)) next.Set(s.Key, v);
                next.freeText.Remove(s.Key);
                if (Store.freeText.TryGetValue(s.Key, out bool f)) next.freeText[s.Key] = f;
            }
            var before = Owner.Sections.All.ToDictionary(x => x.Id, x => SectionOn(x.Id));
            var changedKeys = reg.All.Where(s => !s.Global && Math.Abs(reg.Value(s, this) - (s.StoredIn(next) ?? s.Clamp(s.Default))) > 1e-7f).Select(s => s.Key).ToList();
            Store = next;
            sections = new Dictionary<string, bool>(src.sections);
            foreach (var k in changedKeys) Owner.MarkChanged(k);
            foreach (var kv in before)
            {
                bool now = SectionOn(kv.Key);
                if (now == kv.Value) continue;
                if (ReferenceEquals(this, Owner.Current)) Owner.Sections.NotifySwitched(kv.Key, now);
                Owner.MarkChanged("section:" + kv.Key);
            }
        }

        List<string> tmpK; List<bool> tmpV;
        /// <summary>Saves and loads the profile (plain values only).</summary>
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

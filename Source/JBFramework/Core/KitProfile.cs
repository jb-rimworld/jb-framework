// SPDX-License-Identifier: MIT
// Reduced from Hearth & Harbor Foundation/HearthProfile.cs (no tier, content intensity, underworld).
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace JBFramework
{
    /// <summary>One list value of a profile (saved as plain strings).</summary>
    internal sealed class KitStringList : IExposable
    {
        public List<string> items = new List<string>();
        public void ExposeData()
        {
            Scribe_Collections.Look(ref items, "li", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && items == null) items = new List<string>();
        }
    }

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
        internal Dictionary<string, string> texts = new Dictionary<string, string>();
        internal Dictionary<string, KitStringList> lists = new Dictionary<string, KitStringList>();
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

        // Free text and list values (per save like the numbers; no registration, no row: show them on a custom page) ---------
        /// <summary>
        /// A text value of this profile under a free key of the mod ("dndm.creatorNote"; never a registered setting key), or null.
        /// Like the numbers it lives per save (and in the template). Plain strings only: a save keeps them without any of the mod's types.
        /// </summary>
        public string GetText(string key) => key != null && texts.TryGetValue(key, out var v) ? v : null;
        /// <summary>Sets a text value (null removes it). When this profile is current, OnSettingsChanged(key) follows (debounced).</summary>
        public void SetText(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            string old = GetText(key);
            if (value == null) texts.Remove(key); else texts[key] = value;
            if (old != value) NotifyFree(key);
        }
        /// <summary>A list value under a free key (a copy, in the stored order; empty when unset): mod exclusion lists, defNames …</summary>
        public IReadOnlyList<string> GetList(string key) => key != null && lists.TryGetValue(key, out var l) && l?.items != null ? l.items.ToList() : new List<string>();
        /// <summary>Sets a list value (a copy is stored; null removes it). When this profile is current, OnSettingsChanged(key) follows.</summary>
        public void SetList(string key, IEnumerable<string> values)
        {
            if (string.IsNullOrEmpty(key)) return;
            var old = GetList(key);
            if (values == null) lists.Remove(key); else lists[key] = new KitStringList { items = values.Where(x => x != null).ToList() };
            if (!old.SequenceEqual(GetList(key))) NotifyFree(key);
        }
        void NotifyFree(string key) { if (Owner != null && ReferenceEquals(this, Owner.Current)) Owner.MarkChanged(key); }

        /// <summary>Copy for a new game (or a save the mod joins): switches and values; the card is shown again.</summary>
        internal KitProfile CloneForNewGame()
        {
            var p = new KitProfile { Owner = Owner, Store = Store.Clone(), FirstStartCardDone = false, ProfileVersion = ProfileVersion };
            foreach (var kv in sections) p.sections[kv.Key] = kv.Value;
            foreach (var kv in texts) p.texts[kv.Key] = kv.Value;
            foreach (var kv in lists) p.lists[kv.Key] = new KitStringList { items = kv.Value?.items?.ToList() ?? new List<string>() };
            foreach (var key in p.Store.Keys.Where(x => x.StartsWith(KitMod.CompatKeyPrefix)).ToList()) p.Store.Remove(key);   // template only
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
            // compat switches live in the template only: keep this profile's, never take the source's
            foreach (var key in next.Keys.Where(x => x.StartsWith(KitMod.CompatKeyPrefix)).ToList()) next.Remove(key);
            foreach (var key in Store.Keys.Where(x => x.StartsWith(KitMod.CompatKeyPrefix)).ToList()) if (Store.TryGet(key, out float cv)) next.Set(key, cv);
            var before = Owner.Sections.All.ToDictionary(x => x.Id, x => SectionOn(x.Id));
            var changedKeys = reg.All.Where(s => !s.Global && Math.Abs(reg.Value(s, this) - (s.StoredIn(next) ?? s.Clamp(s.Default))) > 1e-7f).Select(s => s.Key).ToList();
            Store = next;
            sections = new Dictionary<string, bool>(src.sections);
            var freeChanged = src.texts.Keys.Union(texts.Keys).Where(x => src.GetText(x) != GetText(x))
                .Concat(src.lists.Keys.Union(lists.Keys).Where(x => !src.GetList(x).SequenceEqual(GetList(x)))).Distinct().ToList();
            texts = new Dictionary<string, string>(src.texts);
            lists = src.lists.ToDictionary(kv => kv.Key, kv => new KitStringList { items = kv.Value?.items?.ToList() ?? new List<string>() });
            foreach (var k in freeChanged) Owner.MarkChanged(k);
            foreach (var k in changedKeys) Owner.MarkChanged(k);
            foreach (var kv in before)
            {
                bool now = SectionOn(kv.Key);
                if (now == kv.Value) continue;
                if (ReferenceEquals(this, Owner.Current)) Owner.Sections.NotifySwitched(kv.Key, now);
                Owner.MarkChanged("section:" + kv.Key);
            }
        }

        List<string> tmpK; List<bool> tmpV; List<string> tmpTK, tmpTV, tmpLK; List<KitStringList> tmpLV;
        /// <summary>Saves and loads the profile (plain values only).</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref ProfileVersion, "profileVersion", CurrentVersion);
            Scribe_Collections.Look(ref sections, "sections", LookMode.Value, LookMode.Value, ref tmpK, ref tmpV);
            Scribe_Values.Look(ref FirstStartCardDone, "firstStartCardDone");
            Scribe_Deep.Look(ref Store, "settings");
            if (Scribe.mode != LoadSaveMode.Saving || texts.Count > 0) Scribe_Collections.Look(ref texts, "texts", LookMode.Value, LookMode.Value, ref tmpTK, ref tmpTV);
            if (Scribe.mode != LoadSaveMode.Saving || lists.Count > 0) Scribe_Collections.Look(ref lists, "lists", LookMode.Value, LookMode.Deep, ref tmpLK, ref tmpLV);
            if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (sections == null) sections = new Dictionary<string, bool>();
                if (Store == null) Store = new KitStore();
                if (texts == null) texts = new Dictionary<string, string>();
                if (lists == null) lists = new Dictionary<string, KitStringList>();
            }
        }
    }
}

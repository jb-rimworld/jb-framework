// SPDX-License-Identifier: MIT
// Safe def mutation with restore, generalised from Hospital Beds Gen 2's DefMutator and Luciferium Overhaul's live def edits: the first
// change of a value records the original, every later change overwrites only the live value, Restore puts the original back exactly
// (including "this stat was not in the list at all"). Lists are copied when recorded, so in-place edits of the live list are undone too.
// One shared ledger per field for all mods, so two mods editing the same field never undo each other's live edit.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// One mod's reversible def edits. Get it with <see cref="For"/>; group edits by section id (or any name) so switching a section off
    /// can restore exactly its edits: <c>edits.Restore("walls")</c>. Never throws: a bad field name or type is logged once on the owner.
    /// <para>Several mods may edit the same field: the framework keeps one shared ledger per field with the original value and every
    /// mod's edit in order. The live value is the latest edit; when a mod restores, its edit leaves the ledger and the field falls back
    /// to the next most recent edit of another mod, or to the original when no edit is left. A stat a mod added is removed again only
    /// when no other mod still sets it.</para>
    /// </summary>
    public sealed class KitDefEdits
    {
        /// <summary>The edit book of mod <paramref name="owner"/> (one per mod).</summary>
        public static KitDefEdits For(KitMod owner)
        {
            if (owner == null) return null;
            lock (Gate)
            {
                if (!books.TryGetValue(owner, out var b)) { b = new KitDefEdits(owner); books[owner] = b; }
                return b;
            }
        }

        /// <summary>
        /// Sets a field of a def (dotted path for nested objects: "building.bed_defaultMedical", "race.baseBodySize"). The original is
        /// recorded on the first edit of that field by any mod. Returns false when the path does not exist or the value does not fit.
        /// </summary>
        public bool Set(Def def, string fieldPath, object value) => Set(def, fieldPath, value, null);

        /// <summary>Like <see cref="Set(Def, string, object)"/>, in a named group (restore it with <see cref="Restore(string)"/>).</summary>
        public bool Set(Def def, string fieldPath, object value, string group)
        {
            if (def == null || string.IsNullOrEmpty(fieldPath)) return false;
            try
            {
                if (!Resolve(def, fieldPath, out object holder, out FieldInfo f)) { Warn("field:" + def.GetType().Name + "." + fieldPath, "Def edit: " + def.GetType().Name + "." + fieldPath + " not found"); return false; }
                if (value != null && !f.FieldType.IsInstanceOfType(value))
                {
                    try { value = Convert.ChangeType(value, Nullable.GetUnderlyingType(f.FieldType) ?? f.FieldType, System.Globalization.CultureInfo.InvariantCulture); }
                    catch { Warn("type:" + fieldPath, "Def edit: value " + value + " does not fit " + f.FieldType.Name + " " + fieldPath); return false; }
                }
                string key = def.GetType().FullName + ":" + def.defName + ":" + fieldPath;
                lock (Gate)
                {
                    if (!fieldLedger.TryGetValue(key, out var slot))
                    {
                        slot = new FieldSlot { Def = def, Path = fieldPath, Original = CopyIfList(f.GetValue(holder)) };
                        fieldLedger[key] = slot;
                    }
                    slot.Put(this, group, value);
                    f.SetValue(holder, value);
                }
                return true;
            }
            catch (Exception e) { owner.Fail(KitMod.Core, "DefEdits.Set", e); return false; }
        }

        /// <summary>Sets a stat base value (adds the stat when the def lacks it; Restore removes it again).</summary>
        public void SetStat(BuildableDef def, StatDef stat, float value) => SetStat(def, stat, value, null);

        /// <summary>Like <see cref="SetStat(BuildableDef, StatDef, float)"/>, in a named group.</summary>
        public void SetStat(BuildableDef def, StatDef stat, float value, string group)
        {
            if (def == null || stat == null) return;
            try
            {
                string key = def.GetType().FullName + ":" + def.defName + ":" + stat.defName;
                lock (Gate)
                {
                    if (!statLedger.TryGetValue(key, out var slot))
                    {
                        var existing = def.statBases?.FirstOrDefault(s => s.stat == stat);
                        slot = new StatSlot { Def = def, Stat = stat, Had = existing != null, Original = existing?.value ?? 0f };
                        statLedger[key] = slot;
                    }
                    slot.Put(this, group, value);
                    def.SetStatBaseValue(stat, value);
                }
            }
            catch (Exception e) { owner.Fail(KitMod.Core, "DefEdits.SetStat", e); }
        }

        /// <summary>
        /// Sets a building's construction skill requirement AND the copies RimWorld made into its blueprint and frame defs at load
        /// (the skill check reads the frame's copy, so changing only the building does nothing; found in Hospital Beds Gen 2).
        /// </summary>
        public void SetConstructionSkill(ThingDef def, int skill) => SetConstructionSkill(def, skill, null);

        /// <summary>Like <see cref="SetConstructionSkill(ThingDef, int)"/>, in a named group.</summary>
        public void SetConstructionSkill(ThingDef def, int skill, string group)
        {
            if (def == null) return;
            Set(def, "constructionSkillPrerequisite", skill, group);
            foreach (var aux in BuildAux(def)) Set(aux, "constructionSkillPrerequisite", skill, group);
        }

        /// <summary>Did this mod edit the field of the def (and not restore it yet)?</summary>
        public bool IsEdited(Def def, string fieldPath)
        {
            if (def == null) return false;
            lock (Gate) return fieldLedger.TryGetValue(def.GetType().FullName + ":" + def.defName + ":" + fieldPath, out var slot) && slot.Has(this);
        }

        /// <summary>Number of this mod's edits that are not restored (fields and stats).</summary>
        public int Count { get { lock (Gate) return fieldLedger.Values.Count(x => x.Has(this)) + statLedger.Values.Count(x => x.Has(this)); } }

        /// <summary>Undoes all of this mod's edits. Returns how many of its edits were removed.</summary>
        public int Restore() => RestoreWhere(e => true);

        /// <summary>Undoes this mod's edits of one group. Returns how many were removed.</summary>
        public int Restore(string group) => RestoreWhere(e => e.Group == group);

        /// <summary>Undoes this mod's edits of one def. Returns how many were removed.</summary>
        public int RestoreDef(Def def) => def == null ? 0 : RestoreWhere(e => e.Slot.DefOf == def);

        // internal -------------------------------------------------------------------------------------------------------
        static readonly object Gate = new object();
        static readonly Dictionary<KitMod, KitDefEdits> books = new Dictionary<KitMod, KitDefEdits>();
        static readonly Dictionary<string, FieldSlot> fieldLedger = new Dictionary<string, FieldSlot>();
        static readonly Dictionary<string, StatSlot> statLedger = new Dictionary<string, StatSlot>();
        readonly KitMod owner;
        readonly HashSet<string> warned = new HashSet<string>();

        KitDefEdits(KitMod owner) { this.owner = owner; }

        sealed class Edit { public KitDefEdits Book; public string Group; public object Value; public Slot Slot; }

        abstract class Slot
        {
            /// <summary>Edits in the order they were made; the last one is live.</summary>
            public readonly List<Edit> Edits = new List<Edit>();
            public abstract Def DefOf { get; }
            public bool Has(KitDefEdits b) => Edits.Any(e => e.Book == b);
            public void Put(KitDefEdits b, string group, object value)
            {
                Edits.RemoveAll(e => e.Book == b);
                Edits.Add(new Edit { Book = b, Group = group, Value = value, Slot = this });
            }
            /// <summary>Puts the live value (latest remaining edit, else the original) back into the def.</summary>
            public abstract void ApplyTop();
        }

        sealed class FieldSlot : Slot
        {
            public Def Def; public string Path; public object Original;
            public override Def DefOf => Def;
            public override void ApplyTop()
            {
                if (!Resolve(Def, Path, out object holder, out FieldInfo f)) return;
                f.SetValue(holder, Edits.Count > 0 ? Edits[Edits.Count - 1].Value : CopyIfList(Original));
            }
        }

        sealed class StatSlot : Slot
        {
            public BuildableDef Def; public StatDef Stat; public bool Had; public float Original;
            public override Def DefOf => Def;
            public override void ApplyTop()
            {
                if (Edits.Count > 0) Def.SetStatBaseValue(Stat, (float)Edits[Edits.Count - 1].Value);
                else if (Had) Def.SetStatBaseValue(Stat, Original);
                else Def.statBases?.RemoveAll(s => s.stat == Stat);
            }
        }

        int RestoreWhere(Func<Edit, bool> which)
        {
            int n = 0;
            lock (Gate)
            {
                n += RestoreIn(fieldLedger, which);
                n += RestoreIn(statLedger, which);
            }
            return n;
        }

        int RestoreIn<TSlot>(Dictionary<string, TSlot> ledger, Func<Edit, bool> which) where TSlot : Slot
        {
            int n = 0;
            foreach (var kv in ledger.ToList())
            {
                var slot = kv.Value;
                var mine = slot.Edits.Where(e => e.Book == this && which(e)).ToList();
                if (mine.Count == 0) continue;
                bool wasTop = slot.Edits[slot.Edits.Count - 1].Book == this;
                slot.Edits.RemoveAll(e => mine.Contains(e));
                n += mine.Count;
                try { if (wasTop) slot.ApplyTop(); }
                catch (Exception e) { owner.Fail(KitMod.Core, "DefEdits.Restore", e); }
                if (slot.Edits.Count == 0) ledger.Remove(kv.Key);
            }
            return n;
        }

        void Warn(string key, string msg) { if (warned.Add(key)) owner.Log.Warn("Core", msg); }

        static bool Resolve(object root, string path, out object holder, out FieldInfo field)
        {
            holder = root; field = null;
            var parts = path.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                if (holder == null) return false;
                var f = holder.GetType().GetField(parts[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f == null) return false;
                if (i == parts.Length - 1) { field = f; return true; }
                holder = f.GetValue(holder);
            }
            return false;
        }

        /// <summary>Lists are copied (shallow) so in-place changes of the live list cannot change the recorded original.</summary>
        static object CopyIfList(object v)
        {
            if (v is IList list && v.GetType().IsGenericType && v.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                var copy = (IList)Activator.CreateInstance(v.GetType());
                foreach (var x in list) copy.Add(CopyElement(x));
                return copy;
            }
            return v;
        }

        // ThingDefCountClass and StatModifier are mutable classes that mods edit in place: copy those two kinds
        static object CopyElement(object x)
        {
            if (x is ThingDefCountClass c) return new ThingDefCountClass(c.thingDef, c.count);
            if (x is StatModifier s) return new StatModifier { stat = s.stat, value = s.value };
            return x;
        }

        static IEnumerable<ThingDef> BuildAux(ThingDef def)
        {
            List<ThingDef> all;
            try { all = DefDatabase<ThingDef>.AllDefsListForReading; } catch { yield break; }
            foreach (var td in all) if (td != def && td.entityDefToBuild == def) yield return td;
        }
    }
}

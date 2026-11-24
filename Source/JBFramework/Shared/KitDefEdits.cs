// SPDX-License-Identifier: MIT
// Safe def mutation with restore, generalised from Hospital Beds Gen 2's DefMutator and Luciferium Overhaul's live def edits: the first
// change of a value records the original, every later change overwrites only the live value, Restore puts the original back exactly
// (including "this stat was not in the list at all"). Lists are copied when recorded, so in-place edits of the live list are undone too.
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
    /// </summary>
    public sealed class KitDefEdits
    {
        /// <summary>The edit book of mod <paramref name="owner"/> (one per mod).</summary>
        public static KitDefEdits For(KitMod owner)
        {
            if (owner == null) return null;
            lock (books)
            {
                if (!books.TryGetValue(owner, out var b)) { b = new KitDefEdits(owner); books[owner] = b; }
                return b;
            }
        }

        /// <summary>
        /// Sets a field of a def (dotted path for nested objects: "building.bed_defaultMedical", "race.baseBodySize"). The original is
        /// recorded on the first edit of that field. Returns false when the path does not exist or the value does not fit.
        /// </summary>
        public bool Set(Def def, string fieldPath, object value, string group = null)
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
                if (!fields.ContainsKey(key))
                    fields[key] = new FieldEdit { Def = def, Path = fieldPath, Group = group, Original = CopyIfList(f.GetValue(holder)) };
                f.SetValue(holder, value);
                return true;
            }
            catch (Exception e) { owner.Fail(KitMod.Core, "DefEdits.Set", e); return false; }
        }

        /// <summary>Sets a stat base value (adds the stat when the def lacks it; Restore removes it again).</summary>
        public void SetStat(BuildableDef def, StatDef stat, float value, string group = null)
        {
            if (def == null || stat == null) return;
            try
            {
                string key = def.defName + ":" + stat.defName;
                if (!stats.ContainsKey(key))
                {
                    var existing = def.statBases?.FirstOrDefault(s => s.stat == stat);
                    stats[key] = new StatEdit { Def = def, Stat = stat, Group = group, Had = existing != null, Original = existing?.value ?? 0f };
                }
                def.SetStatBaseValue(stat, value);
            }
            catch (Exception e) { owner.Fail(KitMod.Core, "DefEdits.SetStat", e); }
        }

        /// <summary>
        /// Sets a building's construction skill requirement AND the copies RimWorld made into its blueprint and frame defs at load
        /// (the skill check reads the frame's copy, so changing only the building does nothing; found in Hospital Beds Gen 2).
        /// </summary>
        public void SetConstructionSkill(ThingDef def, int skill, string group = null)
        {
            if (def == null) return;
            Set(def, "constructionSkillPrerequisite", skill, group);
            foreach (var aux in BuildAux(def)) Set(aux, "constructionSkillPrerequisite", skill, group);
        }

        /// <summary>Was the field of the def edited (and not restored)?</summary>
        public bool IsEdited(Def def, string fieldPath) => def != null && fields.ContainsKey(def.GetType().FullName + ":" + def.defName + ":" + fieldPath);

        /// <summary>Number of recorded edits (fields and stats).</summary>
        public int Count => fields.Count + stats.Count;

        /// <summary>
        /// Puts the originals back: all edits (<paramref name="group"/> null) or one group's. Returns how many values were restored.
        /// </summary>
        public int Restore(string group = null)
        {
            int n = 0;
            foreach (var kv in fields.Where(x => group == null || x.Value.Group == group).ToList())
            {
                try
                {
                    if (Resolve(kv.Value.Def, kv.Value.Path, out object holder, out FieldInfo f)) { f.SetValue(holder, CopyIfList(kv.Value.Original)); n++; }
                }
                catch (Exception e) { owner.Fail(KitMod.Core, "DefEdits.Restore", e); }
                fields.Remove(kv.Key);
            }
            foreach (var kv in stats.Where(x => group == null || x.Value.Group == group).ToList())
            {
                try
                {
                    var e = kv.Value;
                    if (e.Had) e.Def.SetStatBaseValue(e.Stat, e.Original);
                    else e.Def.statBases?.RemoveAll(s => s.stat == e.Stat);
                    n++;
                }
                catch (Exception ex) { owner.Fail(KitMod.Core, "DefEdits.RestoreStat", ex); }
                stats.Remove(kv.Key);
            }
            return n;
        }

        /// <summary>Restores every edit of one def. Returns how many values were restored.</summary>
        public int RestoreDef(Def def)
        {
            if (def == null) return 0;
            string g = "\u0001def:" + def.defName;
            foreach (var e in fields.Values.Where(x => x.Def == def)) e.Group = g;
            foreach (var e in stats.Values.Where(x => x.Def == def)) e.Group = g;
            return Restore(g);
        }

        // internal -------------------------------------------------------------------------------------------------------
        static readonly Dictionary<KitMod, KitDefEdits> books = new Dictionary<KitMod, KitDefEdits>();
        readonly KitMod owner;
        readonly Dictionary<string, FieldEdit> fields = new Dictionary<string, FieldEdit>();
        readonly Dictionary<string, StatEdit> stats = new Dictionary<string, StatEdit>();
        readonly HashSet<string> warned = new HashSet<string>();

        KitDefEdits(KitMod owner) { this.owner = owner; }

        sealed class FieldEdit { public Def Def; public string Path; public string Group; public object Original; }
        sealed class StatEdit { public BuildableDef Def; public StatDef Stat; public string Group; public bool Had; public float Original; }

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

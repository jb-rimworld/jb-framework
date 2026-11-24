// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Hearth.Core/Testing/SaveFingerprint.cs (assembly set = the mod under test + the framework, defs = the
// mod's content pack). Part of JBFramework.Testing.dll.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;

namespace JBFramework.Testing
{
    /// <summary>
    /// Walks every object of this mod that lives in a save (game/world/map components, areas, ThingComps, hediffs of our defs) by
    /// reflection and writes one line per field: "path = value". Taken right after saving and again right after loading, the two must be
    /// equal. Unlike comparing the XML, this also finds fields that were never saved (a forgotten Scribe line shows up as a difference).
    /// "Ours" = a type compiled into this mod's assembly (that includes the kit's KitProfile/KitStore). Fields that are caches by design go
    /// into <see cref="Transient"/> with the reason.
    /// </summary>
    internal static class SaveFingerprint
    {
        static KitMod mod;
        static HashSet<Assembly> ours = new HashSet<Assembly>();
        /// <summary>A type of the mod under test or of the framework (FrameworkGame holds every mod's profile and SaveData).</summary>
        public static bool Ours(Type t) => t != null && ours.Contains(t.Assembly);
        public static bool OurDef(Def d) => d != null && content != null && d.modContentPack == content;
        public static bool OurDef(Def d, KitMod k) => d != null && k?.Content != null && d.modContentPack == k.Content;

        static ModContentPack content;
        static void Use(KitMod k)
        {
            mod = k;
            content = k?.Content ?? KitTestEnv.TargetContent;   // a mod without a framework context: its content pack
            ours = new HashSet<Assembly> { typeof(Framework).Assembly };
            if (k?.Mod != null) ours.Add(k.Mod.GetType().Assembly);
            foreach (var a in content?.assemblies?.loadedAssemblies ?? new List<Assembly>()) if (a != null && a != typeof(SaveFingerprint).Assembly) ours.Add(a);
        }

        /// <summary>A transient field pattern declared by a mod's suite (TestContext.DeclareTransient). Kept for the rest of the run.</summary>
        public static void Declare(string pattern, string why)
        {
            if (string.IsNullOrEmpty(pattern)) return;
            lock (Transient) if (!Transient.Any(x => x.pattern == pattern)) Transient.Add((pattern, why ?? "declared by the mod's suite"));
        }

        /// <summary>[KitTransient] on a field (by name: the attribute lives in the framework, mods use it in release code).</summary>
        static bool IsTransientField(FieldInfo f)
        {
            try { return f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "KitTransientAttribute"); } catch { return false; }
        }

        /// <summary>Field paths (regex on the path, list indices stripped to []) that are rebuilt after a load, with the reason.</summary>
        public static readonly List<(string pattern, string why)> Transient = new List<(string, string)>
        {
            (@"\.tmp[A-Z]\w*(\[\]|\.count|$)", "Scribe working lists (filled while saving, cleared after loading)"),
            (@"^game/FrameworkGame\.exposed$", "FrameworkGame: true once the component was saved or loaded"),
        };

        /// <summary>Types never walked: links back into the live framework (contexts, settings objects), not save state.</summary>
        static bool Skip(Type ft) => typeof(KitMod).IsAssignableFrom(ft) || typeof(KitSettingsBase).IsAssignableFrom(ft) || typeof(Game).IsAssignableFrom(ft);

        public static List<string> Take(KitMod k)
        {
            Use(k);
            var lines = new List<string>();
            var game = Current.Game; if (game == null) return lines;
            foreach (var c in game.components) if (Ours(c.GetType()) && !(c is KitTestRunner)) Walk(lines, "game/" + c.GetType().Name, c);
            if (Find.World != null) foreach (var c in Find.World.components) if (Ours(c.GetType())) Walk(lines, "world/" + c.GetType().Name, c);
            foreach (var map in game.Maps)
            {
                string mp = "map" + map.uniqueID;
                foreach (var c in map.components) if (Ours(c.GetType())) Walk(lines, mp + "/" + c.GetType().Name, c);
                foreach (var a in map.areaManager.AllAreas) if (Ours(a.GetType())) Walk(lines, mp + "/area/" + a.ID, a);
                foreach (var th in map.listerThings.AllThings)
                {
                    if (OurDef(th.def)) lines.Add(mp + "/" + th.ThingID + " = " + th.Position + " x" + th.stackCount);
                    if (th is ThingWithComps tw && tw.AllComps != null)
                        foreach (var comp in tw.AllComps) if (Ours(comp.GetType())) Walk(lines, mp + "/" + th.ThingID + "/" + comp.GetType().Name, comp);
                    if (th is Pawn p && p.health?.hediffSet != null)
                        foreach (var h in p.health.hediffSet.hediffs)
                            if (OurDef(h.def))
                            {
                                lines.Add(mp + "/" + p.ThingID + "/hediff/" + h.def.defName + " = " + h.Severity.ToString("R", CultureInfo.InvariantCulture));
                                if (Ours(h.GetType())) Walk(lines, mp + "/" + p.ThingID + "/hediff/" + h.def.defName, h);
                            }
                }
            }
            return lines;
        }

        static void Walk(List<string> lines, string path, object root) => WalkObj(lines, path, root, 0, new HashSet<object>(RefEq.Instance));

        sealed class RefEq : IEqualityComparer<object>
        {
            public static readonly RefEq Instance = new RefEq();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        const int MaxLines = 300000;
        static void WalkObj(List<string> lines, string path, object o, int depth, HashSet<object> seen)
        {
            if (lines.Count > MaxLines) { if (lines.Count == MaxLines + 1) lines.Add("<fingerprint cut at " + MaxLines + " lines>"); return; }
            if (depth > 9) { lines.Add(path + " = <too deep>"); return; }
            var t = o.GetType();
            if (!t.IsValueType && !seen.Add(o)) { lines.Add(path + " = <seen>"); return; }
            if (o is Area area) { lines.Add(path + ".id = " + area.ID); lines.Add(path + ".cells = " + area.TrueCount); }
            for (var ty = t; ty != null && Ours(ty); ty = ty.BaseType)
                foreach (var f in ty.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (typeof(Delegate).IsAssignableFrom(f.FieldType) || Skip(f.FieldType) || IsTransientField(f)) continue;
                    object v; try { v = f.GetValue(o); } catch (Exception e) { lines.Add(path + "." + f.Name + " = <" + e.GetType().Name + ">"); continue; }
                    Value(lines, path + "." + Clean(f.Name), v, depth + 1, seen);
                }
        }

        static string Clean(string n) => n.StartsWith("<") && n.Contains(">k__BackingField") ? n.Substring(1, n.IndexOf('>') - 1) : n;

        static void Value(List<string> lines, string path, object v, int depth, HashSet<object> seen)
        {
            string s = Scalar(v);
            if (s != null) { lines.Add(path + " = " + s); return; }
            var t = v.GetType();
            if (v is IDictionary dict)
            {
                var entries = new List<(string k, object v)>();
                foreach (DictionaryEntry e in dict) entries.Add((Scalar(e.Key) ?? e.Key.GetType().Name, e.Value));
                lines.Add(path + ".count = " + entries.Count);
                foreach (var (k, ev) in entries.OrderBy(e => e.k, StringComparer.Ordinal)) Value(lines, path + "[" + k + "]", ev, depth + 1, seen);
                return;
            }
            // only real collections: enumerating an arbitrary IEnumerable could run code (an iterator restarts its coroutine)
            if (v is IEnumerable en && (v is ICollection || IsGenericCollection(t)))
            {
                var items = new List<object>(); foreach (var x in en) { items.Add(x); if (items.Count > 400) break; }
                lines.Add(path + ".count = " + items.Count);
                bool unordered = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(HashSet<>);
                if (unordered)
                {
                    var sub = new List<string>();
                    for (int i = 0; i < items.Count; i++) Value(sub, path + "{}", items[i], depth + 1, seen);
                    sub.Sort(StringComparer.Ordinal); lines.AddRange(sub);
                }
                else for (int i = 0; i < items.Count; i++) Value(lines, path + "[" + i + "]", items[i], depth + 1, seen);
                return;
            }
            if (Ours(t) && !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)) { WalkObj(lines, path, v, depth, seen); return; }
            lines.Add(path + " = <" + t.Name + ">");
        }

        static bool IsGenericCollection(Type t)
        {
            foreach (var i in t.GetInterfaces()) if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>)) return true;
            return false;
        }

        /// <summary>One-line form of a value, or null when it has to be walked.</summary>
        static string Scalar(object v)
        {
            switch (v)
            {
                case null: return "null";
                case string s: return "\"" + s.Replace("\n", "\\n") + "\"";
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case Def def: return "def:" + def.defName;
                case Thing th: return "thing:" + th.ThingID;
                case Map m: return "map:" + m.uniqueID;
                case Faction fa: return "faction:" + fa.loadID;
                case IntVec3 c: return c.ToString();
                case CellRect r: return r.ToString();
                case Rot4 rot: return rot.ToString();
            }
            var t = v.GetType();
            if (t.IsPrimitive || t.IsEnum || v is decimal) return Convert.ToString(v, CultureInfo.InvariantCulture);
            if (v is ILoadReferenceable lr) { try { return "ref:" + lr.GetUniqueLoadID(); } catch { return "ref:?"; } }
            if (t.IsValueType && !Ours(t) && !(v is IEnumerable)) return v.ToString();
            return null;
        }

        static string Strip(string line) => Regex.Replace(line.Split(new[] { " = " }, 2, StringSplitOptions.None)[0], @"\[[^\]]*\]", "[]");

        public static bool IsTransient(string line, out string why)
        {
            string p = Strip(line);
            foreach (var (pat, w) in Transient) if (Regex.IsMatch(p, pat)) { why = w; return true; }
            why = null; return false;
        }

        /// <summary>Lines only in a ("-") or only in b ("+"); transient fields are left out (counted in <paramref name="ignored"/>).</summary>
        public static List<string> Diff(List<string> a, List<string> b, out int ignored)
        {
            var ca = Count(a); var cb = Count(b);
            var outl = new List<string>(); ignored = 0;
            foreach (var kv in ca) { int n = cb.TryGetValue(kv.Key, out var m) ? m : 0; for (int i = n; i < kv.Value; i++) { if (IsTransient(kv.Key, out _)) ignored++; else outl.Add("- " + kv.Key); } }
            foreach (var kv in cb) { int n = ca.TryGetValue(kv.Key, out var m) ? m : 0; for (int i = n; i < kv.Value; i++) { if (IsTransient(kv.Key, out _)) ignored++; else outl.Add("+ " + kv.Key); } }
            outl.Sort((x, y) => string.CompareOrdinal(x.Substring(2), y.Substring(2)));
            return outl;
        }

        static Dictionary<string, int> Count(List<string> l) { var d = new Dictionary<string, int>(); foreach (var x in l) d[x] = d.TryGetValue(x, out var n) ? n + 1 : 1; return d; }
    }

    /// <summary>Two-phase test runs: a suite saves the game; tools/jbmod with LOAD= starts the next game from that save (vanilla dev-mode autostart).</summary>
    internal static class SaveLoadKit
    {
        /// <summary>Name of the save this game was loaded from (JB_TEST_LOAD), or null in a -quicktest game.</summary>
        public static string LoadedSave { get { var s = KitTestEnv.Get(KitTestEnv.Load); return string.IsNullOrEmpty(s) ? null : s; } }
        /// <summary>Fingerprint taken right after the load (before the first tick), or null.</summary>
        public static List<string> AfterLoad;
        public static int AfterLoadTick = -1;

        public static string SavesDir => GenFilePaths.SavedGamesFolderPath;
        public static string SidePath(string save, string what) => Path.Combine(SavesDir, save + "." + what + ".txt");

        /// <summary>True for components of the harness assembly (they do not exist outside test runs).</summary>
        static bool IsTestRunner(GameComponent c) => c != null && c.GetType().Assembly == typeof(SaveLoadKit).Assembly;

        /// <summary>
        /// Saves the game as <paramref name="name"/> and writes its fingerprint next to it (name.fp.txt). Test runner components are left
        /// out of the save (they do not exist in release builds, so a removal test must not see them).
        /// </summary>
        public static bool SaveWithFingerprint(string name, TestContext t)
        {
            var comps = Current.Game.components;
            var runners = comps.Where(IsTestRunner).ToList();
            List<string> fp;
            try
            {
                foreach (var r in runners) comps.Remove(r);
                GameDataSaveLoader.SaveGame(name);   // logs (does not throw) on failure
                fp = SaveFingerprint.Take(t.Mod);
            }
            catch (Exception e) { t.Check(false, "save " + name + " threw " + e.Message); return false; }
            finally { foreach (var r in runners) if (!comps.Contains(r)) comps.Add(r); }
            File.WriteAllLines(SidePath(name, "fp"), fp);
            bool ok = File.Exists(GenFilePaths.FilePathForSavedGame(name));
            t.Check(ok, "saved as " + name + " (" + fp.Count + " fingerprint lines, tick " + Find.TickManager.TicksGame + ", " + runners.Count + " test runner(s) left out)");
            return ok;
        }

        public static void WriteSide(string save, string what, IEnumerable<string> lines) => File.WriteAllLines(SidePath(save, what), lines);
        public static List<string> ReadSide(string save, string what) { var p = SidePath(save, what); return File.Exists(p) ? File.ReadAllLines(p).ToList() : null; }

        /// <summary>Red errors the probe mod recorded in this run (all mods). Empty without the probe.</summary>
        public static List<string> ProbeErrors()
        {
            var p = KitTestEnv.Get(KitTestEnv.ProbeErr);
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) return new List<string>();
            try
            {
                using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var r = new StreamReader(fs))
                    return r.ReadToEnd().Split('\n').Where(l => l.Trim().Length > 0 && !l.Contains("[JBTEST]")).ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>Compares the fingerprint taken at save time with the one taken right after loading; notes every difference.</summary>
        public static void CompareFingerprints(string save, TestContext t, int show = 80)
        {
            var before = ReadSide(save, "fp");
            t.Check(before != null, "fingerprint of the save found");
            t.Check(AfterLoad != null, "fingerprint taken right after the load (tick " + AfterLoadTick + ")");
            if (before == null || AfterLoad == null) return;
            var diff = SaveFingerprint.Diff(before, AfterLoad, out int ignored);
            File.WriteAllLines(SidePath(save, "fpdiff"), diff);
            File.WriteAllLines(SidePath(save, "fpafter"), AfterLoad);
            foreach (var l in diff.Take(show)) t.Note("diff " + l);
            if (before.Count == 0) t.Note("the fingerprint is empty: this mod keeps nothing in the save");
            t.Check(diff.Count == 0, $"state after the load equals the state at save time: {before.Count} lines, {diff.Count} differences, {ignored} transient lines ignored");
        }
    }
}

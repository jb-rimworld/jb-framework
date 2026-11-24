// SPDX-License-Identifier: MIT
// "One architect entry per family", generalised from Definitely Not Dev Mode's DNDMArchitect.AfterResolve: the buildables of one family
// (tiers Mk. I/II/III, sizes, variants) become ONE dropdown in their architect tab instead of one button each, sorted by uiOrder. Works on
// the designator lists RimWorld builds while loading defs (DesignationCategoryDef.ResolveDesignators, private; 1.7 decompile and the 1.6
// reference checked): a postfix there regroups each tab, and a pass after startup regroups tabs resolved before a mod registered.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace JBFramework
{
    /// <summary>Architect menu helpers.</summary>
    public static class KitArchitect
    {
        /// <summary>
        /// The buildables for which <paramref name="family"/> returns the same non-empty key become one dropdown in their architect tab
        /// (keys are per mod: two mods never merge each other's families). Buildables already in a vanilla dropdown group of the same
        /// family are merged too. Register in Register (the regrouping then happens while the game loads) or later (it happens at once).
        /// Combine with <see cref="KitMaterialMenu.AfterDropdownPick"/> for stuffed families.
        /// </summary>
        public static void OneEntryPerFamily(KitMod owner, Func<BuildableDef, string> family)
        {
            if (owner == null || family == null) return;
            lock (rules) rules.Add(new Rule { Owner = owner, Family = family });
            if (startupDone) RegroupAll();
        }

        /// <summary>Regroups every architect tab now (after defs changed at runtime). Idempotent.</summary>
        public static void RegroupAll()
        {
            try { foreach (var cat in DefDatabase<DesignationCategoryDef>.AllDefsListForReading) Regroup(cat); }
            catch (Exception e) { Framework.Self?.Guard.Fail(KitMod.Core, "Architect.RegroupAll", e); }
        }

        // internal -------------------------------------------------------------------------------------------------------
        sealed class Rule { public KitMod Owner; public Func<BuildableDef, string> Family; }
        static readonly List<Rule> rules = new List<Rule>();
        static bool startupDone;

        internal static bool AnyRule { get { lock (rules) return rules.Count > 0; } }

        internal static void AfterStartup()
        {
            startupDone = true;
            if (AnyRule) RegroupAll();
        }

        static BuildableDef DefOf(Designator d) => (d as Designator_Place)?.PlacingDef;

        static string FamilyOf(BuildableDef def, Rule[] snap)
        {
            if (def == null) return null;
            foreach (var r in snap)
            {
                if (r.Owner.Blocked) continue;
                string f = r.Owner.Run(KitMod.Core, "Architect.family", () => r.Family(def), null);
                if (!string.IsNullOrEmpty(f)) return r.Owner.PackageId + "|" + f;
            }
            return null;
        }

        /// <summary>Regroups one tab in place. Returns the number of entries removed.</summary>
        internal static int Regroup(DesignationCategoryDef cat)
        {
            if (cat == null || !AnyRule) return 0;
            List<Designator> list = cat.AllResolvedDesignators;
            if (list == null || list.Count < 2) return 0;
            Rule[] snap; lock (rules) snap = rules.ToArray();
            var famCache = new Dictionary<BuildableDef, string>();
            string Fam(BuildableDef d) { if (d == null) return null; if (!famCache.TryGetValue(d, out var f)) famCache[d] = f = FamilyOf(d, snap); return f; }

            // family → positions of entries that hold only members of that family
            var byFamily = new Dictionary<string, List<int>>();
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                IEnumerable<Designator> els = d is Designator_Dropdown dd ? dd.Elements : (IEnumerable<Designator>)new[] { d };
                var fams = els.Select(e => Fam(DefOf(e))).Distinct().ToList();
                if (fams.Count != 1 || fams[0] == null) continue;
                if (!byFamily.TryGetValue(fams[0], out var pos)) byFamily[fams[0]] = pos = new List<int>();
                pos.Add(i);
            }
            var remove = new List<int>();
            foreach (var kv in byFamily)
            {
                var pos = kv.Value;
                if (pos.Count < 2) continue;
                var members = new List<(Designator d, int order)>();
                int n = 0;
                foreach (int i in pos)
                {
                    if (list[i] is Designator_Dropdown dd) foreach (var e in dd.Elements) members.Add((e, n++));
                    else members.Add((list[i], n++));
                }
                var sorted = members.OrderBy(m => DefOf(m.d)?.uiOrder ?? 0f).ThenBy(m => m.order).Select(m => m.d).ToList();
                var drop = new Designator_Dropdown();
                foreach (var e in sorted) drop.Add(e);
                drop.SetActiveDesignator(sorted[0], false);
                drop.Order = sorted.Min(e => DefOf(e)?.uiOrder ?? 0f);
                list[pos[0]] = drop;
                for (int j = 1; j < pos.Count; j++) remove.Add(pos[j]);
            }
            foreach (int i in remove.OrderByDescending(x => x)) list.RemoveAt(i);
            return remove.Count;
        }
    }

    /// <summary>Regroups each architect tab right after RimWorld built its designators. Early-out unless a mod registered.</summary>
    [HarmonyPatch]
    internal static class Patch_KitArchitectFamilies
    {
        static MethodBase target;

        static bool Prepare()
        {
            target = AccessTools.Method(typeof(DesignationCategoryDef), "ResolveDesignators");
            return target != null;
        }

        static MethodBase TargetMethod() => target;

        static void Postfix(DesignationCategoryDef __instance)
        {
            if (!KitArchitect.AnyRule) return;
            try { KitArchitect.Regroup(__instance); }
            catch (Exception e) { Framework.Self?.Guard.Fail(KitMod.Core, "Architect.patch", e); }
        }
    }
}

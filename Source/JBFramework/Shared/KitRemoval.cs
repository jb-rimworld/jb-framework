// SPDX-License-Identifier: MIT
// Removal preparation: the steps a section's "Prepare removal" usually needs (take the mod's things, hediffs, bills and designations
// out of the running save), as reusable building blocks, plus a way to add steps to a section from anywhere.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// Building blocks for a section's removal preparation (the "Prepare removal" button of the Diagnosis page). Each step works on the
    /// running game, writes one report line and never throws (errors become report lines). Use them in the
    /// <c>prepareRemoval</c> callback of <see cref="KitRegistry.Section"/>, or add them to a section with <see cref="AddStep"/>.
    /// </summary>
    public static class KitRemoval
    {
        /// <summary>
        /// Adds a step to section <paramref name="sectionId"/>'s removal preparation (after the steps registered before). Returns false
        /// when the mod has no such section.
        /// </summary>
        public static bool AddStep(KitMod k, string sectionId, Action<List<string>> step)
        {
            var s = k?.Sections.Find(sectionId);
            if (s == null || step == null) return false;
            var before = s.PrepareRemoval;
            s.PrepareRemoval = before == null ? step : (Action<List<string>>)(report => { before(report); step(report); });
            return true;
        }

        /// <summary>
        /// Removes every thing of the matching defs from all maps: spawned things, minified ones, blueprints and frames of them, and
        /// items in pawn inventories and equipment. <paramref name="refund"/>: buildings drop their cost like a deconstruction.
        /// </summary>
        public static void RemoveThings(Func<ThingDef, bool> which, List<string> report, bool refund = true)
        {
            if (which == null) return;
            Step(report, "things", () =>
            {
                int n = 0;
                foreach (var map in Find.Maps.ToList())
                {
                    foreach (var t in map.listerThings.AllThings.ToList())
                    {
                        if (t.Destroyed) continue;
                        bool hit = Matches(t.def, which)
                                   || (t is MinifiedThing mt && mt.InnerThing != null && Matches(mt.InnerThing.def, which))
                                   || (t.def.entityDefToBuild is ThingDef built && Matches(built, which));
                        if (!hit) continue;
                        if (refund && t.def.category == ThingCategory.Building && t.Spawned) t.Destroy(DestroyMode.Deconstruct);
                        else t.Destroy(DestroyMode.Vanish);
                        n++;
                    }
                }
                foreach (var p in AllPawns())
                {
                    foreach (var t in p.inventory?.innerContainer?.ToList() ?? new List<Thing>())
                        if (Matches(t.def, which)) { t.Destroy(DestroyMode.Vanish); n++; }
                    foreach (var t in p.equipment?.AllEquipmentListForReading?.ToList() ?? new List<ThingWithComps>())
                        if (Matches(t.def, which)) { p.equipment.Remove(t); t.Destroy(DestroyMode.Vanish); n++; }
                    foreach (var t in p.apparel?.WornApparel?.ToList() ?? new List<Apparel>())
                        if (Matches(t.def, which)) { p.apparel.Remove(t); t.Destroy(DestroyMode.Vanish); n++; }
                }
                return KitText.K("Removal.Things", "{0} things removed", n);
            });
        }

        /// <summary>Removes the matching hediffs from every pawn (maps, caravans, world pawns).</summary>
        public static void RemoveHediffs(Func<HediffDef, bool> which, List<string> report)
        {
            if (which == null) return;
            Step(report, "hediffs", () =>
            {
                int n = 0;
                foreach (var p in AllPawns())
                {
                    var hs = p.health?.hediffSet?.hediffs;
                    if (hs == null) continue;
                    foreach (var h in hs.Where(h => h?.def != null && which(h.def)).ToList()) { p.health.RemoveHediff(h); n++; }
                }
                return KitText.K("Removal.Hediffs", "{0} health conditions removed", n);
            });
        }

        /// <summary>Deletes the bills of the matching recipes from every workbench on every map.</summary>
        public static void RemoveBills(Func<RecipeDef, bool> which, List<string> report)
        {
            if (which == null) return;
            Step(report, "bills", () =>
            {
                int n = 0;
                foreach (var map in Find.Maps.ToList())
                    foreach (var giver in map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver).OfType<IBillGiver>().ToList())
                    {
                        var stack = giver.BillStack;
                        if (stack == null) continue;
                        foreach (var b in stack.Bills.Where(b => b?.recipe != null && which(b.recipe)).ToList()) { stack.Delete(b); n++; }
                    }
                return KitText.K("Removal.Bills", "{0} bills deleted", n);
            });
        }

        /// <summary>Removes the matching designations from every map.</summary>
        public static void RemoveDesignations(Func<DesignationDef, bool> which, List<string> report)
        {
            if (which == null) return;
            Step(report, "designations", () =>
            {
                int n = 0;
                foreach (var map in Find.Maps.ToList())
                {
                    var dm = map.designationManager;
                    foreach (var d in dm.AllDesignations.Where(d => d?.def != null && which(d.def)).ToList()) { dm.RemoveDesignation(d); n++; }
                }
                return KitText.K("Removal.Designations", "{0} designations removed", n);
            });
        }

        // internal -------------------------------------------------------------------------------------------------------
        static bool Matches(ThingDef d, Func<ThingDef, bool> which)
        {
            try { return d != null && which(d); } catch { return false; }
        }

        static void Step(List<string> report, string what, Func<string> body)
        {
            if (Current.Game == null) { report?.Add(KitText.K("Removal.NoGame", "No game running: nothing to remove ({0}).", what)); return; }
            try { report?.Add(body()); }
            catch (Exception e) { report?.Add("Error (" + what + "): " + e.Message); }
        }

        static IEnumerable<Pawn> AllPawns()
        {
            var seen = new HashSet<Pawn>();
            foreach (var map in Find.Maps) foreach (var p in map.mapPawns.AllPawns) if (p != null && seen.Add(p)) yield return p;
            foreach (var c in Find.WorldObjects.Caravans) foreach (var p in c.PawnsListForReading) if (p != null && seen.Add(p)) yield return p;
            foreach (var p in Find.WorldPawns.AllPawnsAliveOrDead) if (p != null && seen.Add(p)) yield return p;
        }
    }
}

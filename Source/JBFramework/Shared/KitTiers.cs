// SPDX-License-Identifier: MIT
// Generic tier change in place, from Definitely Not Dev Mode's TierSwap/CompDNDMTierSwap/Command_DNDMTierSwap: a building of a tier
// family gets a "Change tier" gizmo that replaces it with a sibling tier at the same cell, keeping what can be kept.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBFramework
{
    /// <summary>A comp that carries state worth keeping when its building changes tier (timers, modes, counters).</summary>
    public interface IKitTierState
    {
        /// <summary>Copy the state from the comp of the same type on the old building.</summary>
        void CopyTierStateFrom(ThingComp other);
    }

    /// <summary>
    /// Tier families of buildings and the in-place tier change. Register families in Register or OnDefsLoaded; at startup (or at once,
    /// when registered later) every member def gets the "Change tier" gizmo comp (<see cref="CompProperties_KitTierSwap"/>, saves nothing).
    /// </summary>
    public static class KitTiers
    {
        /// <summary>
        /// Families by rule: every building def for which <paramref name="family"/> returns a non-empty key and <paramref name="tier"/>
        /// a tier ≥ 1 belongs to that family. A family needs at least two members with distinct tiers and one size; others are ignored.
        /// <paramref name="allowed"/> (optional) hides the gizmo for a thing; <paramref name="afterSwap"/> (optional) runs after a change
        /// (old thing, already destroyed; new thing, spawned).
        /// </summary>
        public static void Families(KitMod owner, Func<ThingDef, string> family, Func<ThingDef, int> tier,
            Func<Thing, bool> allowed = null, Action<Thing, Thing> afterSwap = null)
        {
            if (owner == null || family == null || tier == null) return;
            lock (rules) rules.Add(new Rule { Owner = owner, Family = family, Tier = tier, Allowed = allowed, AfterSwap = afterSwap });
            if (startupDone) Build();
        }

        /// <summary>A family given by defNames in tier order (tier 1 first). Names without a def are ignored.</summary>
        public static void Family(KitMod owner, string familyId, params string[] defNamesInTierOrder)
        {
            if (owner == null || defNamesInTierOrder == null || defNamesInTierOrder.Length < 2) return;
            var order = defNamesInTierOrder.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i + 1);
            Families(owner, d => order.ContainsKey(d.defName) ? familyId : null, d => order.TryGetValue(d.defName, out int t) ? t : 0);
        }

        /// <summary>The def's family members in tier order (including itself), or null when it is in no family.</summary>
        public static IReadOnlyList<ThingDef> SiblingsOf(ThingDef def)
        {
            if (def == null) return null;
            Ensure();
            return members.TryGetValue(def, out var m) ? m.Family : null;
        }

        /// <summary>The def's tier (1 = lowest), or 0 when it is in no family.</summary>
        public static int TierOf(ThingDef def)
        {
            if (def == null) return 0;
            Ensure();
            return members.TryGetValue(def, out var m) ? m.Tier : 0;
        }

        /// <summary>The family member at <paramref name="tier"/>, or null.</summary>
        public static ThingDef SiblingAt(ThingDef def, int tier)
        {
            if (def == null) return null;
            Ensure();
            return members.TryGetValue(def, out var m) ? m.Family.FirstOrDefault(d => members[d].Tier == tier) : null;
        }

        /// <summary>Roman numeral for tiers 1 … 10 ("I", "II", …), the number otherwise.</summary>
        public static string Roman(int tier)
        {
            string[] r = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return tier >= 1 && tier <= r.Length ? r[tier - 1] : tier.ToString();
        }

        /// <summary>
        /// Replaces <paramref name="old"/> with def <paramref name="to"/> at the same cell and rotation, free and instant. Keeps faction,
        /// material, hit point ratio, paint, forbidden state, storage settings (items stay on their cells), bills, stored battery energy
        /// (up to the new capacity), selection and the state of <see cref="IKitTierState"/> comps. Returns the new thing, or null
        /// (nothing changed) when the swap is not possible or failed (logged on the owning mod).
        /// </summary>
        public static Thing Swap(Thing old, ThingDef to)
        {
            if (old == null || to == null || !old.Spawned || old.def == to || old.def.size != to.size) return null;
            Ensure();
            members.TryGetValue(old.def, out var m);
            var owner = m?.Rule?.Owner ?? Framework.Self;
            Thing result = null;
            Action body = () => result = SwapInner(old, to, m?.Rule);
            if (owner != null) owner.Run(KitMod.Core, "Tier.swap", body);
            else body();
            return result;
        }

        // internal -------------------------------------------------------------------------------------------------------
        internal sealed class Rule { public KitMod Owner; public Func<ThingDef, string> Family; public Func<ThingDef, int> Tier; public Func<Thing, bool> Allowed; public Action<Thing, Thing> AfterSwap; }
        internal sealed class Member { public Rule Rule; public List<ThingDef> Family; public int Tier; }
        static readonly List<Rule> rules = new List<Rule>();
        static Dictionary<ThingDef, Member> members = new Dictionary<ThingDef, Member>();
        static bool startupDone, built;

        internal static Member MemberOf(ThingDef def) { Ensure(); return def != null && members.TryGetValue(def, out var m) ? m : null; }

        internal static void AfterStartup() { startupDone = true; if (rules.Count > 0) Build(); }

        static void Ensure() { if (!built && startupDone) Build(); }

        static void Build()
        {
            built = true;
            var map = new Dictionary<ThingDef, Member>();
            Rule[] snap; lock (rules) snap = rules.ToArray();
            List<ThingDef> defs;
            try { defs = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.category == ThingCategory.Building && d.BuildableByPlayer).ToList(); }
            catch { return; }
            foreach (var r in snap)
            {
                if (r.Owner.Blocked) continue;
                var tagged = new List<(ThingDef d, string f, int t)>();
                foreach (var d in defs)
                {
                    if (map.ContainsKey(d)) continue;
                    string f = r.Owner.Run(KitMod.Core, "Tier.family", () => r.Family(d), null);
                    if (string.IsNullOrEmpty(f)) continue;
                    int t = r.Owner.Run(KitMod.Core, "Tier.tier", () => r.Tier(d), 0);
                    if (t >= 1) tagged.Add((d, f, t));
                }
                foreach (var g in tagged.GroupBy(x => (x.f, x.d.size)))
                {
                    var list = g.OrderBy(x => x.t).ToList();
                    if (list.Count < 2 || list.Select(x => x.t).Distinct().Count() != list.Count)
                    {
                        if (list.Count >= 2) r.Owner.Log.Warn("Core", "Tier family " + g.Key.f + " skipped: two members share a tier (" + string.Join(", ", list.Select(x => x.d.defName + "=" + x.t)) + ")");
                        continue;
                    }
                    var fam = list.Select(x => x.d).ToList();
                    foreach (var x in list) map[x.d] = new Member { Rule = r, Family = fam, Tier = x.t };
                }
            }
            members = map;
            foreach (var d in map.Keys)
            {
                if (d.comps == null) d.comps = new List<CompProperties>();
                if (!d.comps.Any(c => c is CompProperties_KitTierSwap)) d.comps.Add(new CompProperties_KitTierSwap());
            }
        }

        static Thing SwapInner(Thing old, ThingDef to, Rule rule)
        {
            Map map = old.Map;
            IntVec3 pos = old.Position;
            Rot4 rot = old.Rotation;
            Faction fac = old.Faction;
            bool selected = Find.Selector.IsSelected(old);
            float hpFrac = old.def.useHitPoints && old.MaxHitPoints > 0 ? (float)old.HitPoints / old.MaxHitPoints : 1f;
            ColorDef paint = (old as Building)?.PaintColorDef;
            bool forbidden = old.IsForbidden(Faction.OfPlayer);

            StorageSettings storage = null;
            if (old is Building_Storage bs && bs.settings != null) { storage = new StorageSettings(); storage.CopyFrom(bs.settings); }

            List<Bill> bills = null;
            if (old is Building_WorkTable wt && wt.billStack != null && wt.billStack.Count > 0)
            {
                bills = wt.billStack.Bills.ToList();
                wt.billStack.Clear();   // moved, not deleted: no Notify_BillDeleted
            }

            float? energy = old.TryGetComp<CompPowerBattery>()?.StoredEnergy;
            var oldComps = (old as ThingWithComps)?.AllComps?.ToList() ?? new List<ThingComp>();

            ThingDef stuff = to.MadeFromStuff ? (old.Stuff ?? GenStuff.DefaultStuffFor(to)) : null;
            Thing neu = ThingMaker.MakeThing(to, stuff);
            if (fac != null) neu.SetFaction(fac);

            old.Destroy(DestroyMode.WillReplace);
            GenSpawn.Spawn(neu, pos, map, rot, WipeMode.VanishOrMoveAside);

            if (neu.def.useHitPoints) neu.HitPoints = Mathf.Clamp(Mathf.RoundToInt(hpFrac * neu.MaxHitPoints), 1, neu.MaxHitPoints);
            if (paint != null && neu is Building nb) nb.ChangePaint(paint);
            if (storage != null && neu is Building_Storage ns && ns.settings != null) ns.settings.CopyFrom(storage);
            if (bills != null && neu is Building_WorkTable nt && nt.billStack != null)
                foreach (var b in bills) nt.billStack.AddBill(b);
            if (energy.HasValue && neu.TryGetComp<CompPowerBattery>() is CompPowerBattery bat && bat.Props.storedEnergyMax > 0f)
                bat.SetStoredEnergyPct(Mathf.Clamp01(energy.Value / bat.Props.storedEnergyMax));
            if (neu is ThingWithComps nc)
                foreach (var c in nc.AllComps)
                    if (c is IKitTierState st)
                    {
                        var src = oldComps.FirstOrDefault(o => o.GetType() == c.GetType());
                        if (src != null) st.CopyTierStateFrom(src);
                    }
            if (forbidden) neu.SetForbidden(true, false);
            if (selected) Find.Selector.Select(neu, false, false);
            if (rule?.AfterSwap != null) rule.Owner.Run(KitMod.Core, "Tier.afterSwap", () => rule.AfterSwap(old, neu));
            return neu;
        }
    }

    /// <summary>Added in code to every tier family member (<see cref="KitTiers"/>). Saves nothing.</summary>
    public sealed class CompProperties_KitTierSwap : CompProperties
    {
        /// <summary>Uses <see cref="CompKitTierSwap"/>.</summary>
        public CompProperties_KitTierSwap() { compClass = typeof(CompKitTierSwap); }
    }

    /// <summary>Gives a tier family member the "Change tier" gizmo (player buildings only).</summary>
    public sealed class CompKitTierSwap : ThingComp
    {
        /// <summary>The gizmo.</summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer || !parent.Spawned) yield break;
            var m = KitTiers.MemberOf(parent.def);
            if (m == null || m.Rule.Owner.Blocked) yield break;
            if (m.Rule.Allowed != null && !m.Rule.Owner.Run(KitMod.Core, "Tier.allowed", () => m.Rule.Allowed(parent), false)) yield break;
            yield return new Command_KitTierSwap(parent);
        }
    }

    /// <summary>One gizmo for every selected tier building: a menu "Make it …" per reachable tier.</summary>
    public sealed class Command_KitTierSwap : Command
    {
        /// <summary>The building this gizmo belongs to.</summary>
        public readonly Thing thing;

        /// <summary>The gizmo of <paramref name="t"/>.</summary>
        public Command_KitTierSwap(Thing t)
        {
            thing = t;
            defaultLabel = KitText.K("Tier.Change", "Change tier");
            defaultDesc = KitText.K("Tier.ChangeDesc", "Replace this building with another tier of the same kind, right here, free and at once. Material, hit points, paint, storage settings and bills are kept.");
            var next = KitTiers.SiblingsOf(t.def)?.FirstOrDefault(s => s != t.def);
            icon = next?.uiIcon ?? t.def.uiIcon;
            alsoClickIfOtherInGroupClicked = false;
        }

        /// <summary>All tier gizmos group into one.</summary>
        public override bool GroupsWith(Gizmo other) => other is Command_KitTierSwap;

        /// <summary>Opens the tier menu for every selected building.</summary>
        public override void ProcessGroupInput(Event ev, List<Gizmo> group)
        {
            var things = new List<Thing>();
            if (group != null) foreach (var g in group) if (g is Command_KitTierSwap c && c.thing != null && c.thing.Spawned) things.Add(c.thing);
            if (!things.Contains(thing) && thing.Spawned) things.Add(thing);
            int maxTier = things.Select(x => KitTiers.SiblingsOf(x.def)?.Count ?? 0).DefaultIfEmpty(0).Max();
            var opts = new List<FloatMenuOption>();
            for (int tier = 1; tier <= Math.Max(maxTier, 10); tier++)
            {
                int tt = tier;
                var movers = things.Where(x => KitTiers.SiblingAt(x.def, tt) is ThingDef to && to != x.def).ToList();
                if (movers.Count == 0) continue;
                ThingDef shown = KitTiers.SiblingAt(movers[0].def, tt);
                string label = movers.Count == 1
                    ? KitText.K("Tier.MakeIt", "Make it {0}", shown.LabelCap)
                    : KitText.K("Tier.MakeItCount", "Make them tier {0} ({1} buildings)", KitTiers.Roman(tt), movers.Count);
                opts.Add(new FloatMenuOption(label, () =>
                {
                    int done = 0;
                    foreach (var x in movers) if (KitTiers.Swap(x, KitTiers.SiblingAt(x.def, tt)) != null) done++;
                    if (done < movers.Count) Messages.Message(KitText.K("Tier.SomeFailed", "{0} buildings could not change tier (see the log).", movers.Count - done), MessageTypeDefOf.RejectInput, false);
                }, shown));
            }
            if (opts.Count > 0) Find.WindowStack.Add(new FloatMenu(opts));
        }
    }
}

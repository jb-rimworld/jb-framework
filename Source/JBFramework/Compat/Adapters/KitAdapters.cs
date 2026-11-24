// SPDX-License-Identifier: MIT
// Adapters: one guarded implementation per other mod that several JB mods talk to. No hard references: reflection only, resolved
// once, every failure logged once (framework log, channel "Compat") and answered with a safe default. Adding a mod = a ModNoteDef
// in Common/Defs/ModNotes plus, at most, one class here.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace JBFramework
{
    /// <summary>Shared failure reporting of the adapters: one warning per key in the framework's log, then silence.</summary>
    internal static class KitAdapterLog
    {
        static readonly HashSet<string> said = new HashSet<string>();

        internal static void Once(string key, string message)
        {
            lock (said) { if (!said.Add(key)) return; }
            try
            {
                var s = Framework.Self;
                if (s != null) s.Log.Once("adapter:" + key, KitLogLevel.Warn, "Compat", message);
                else Verse.Log.Warning("[" + Framework.Name + "] " + message);
            }
            catch { }
        }
    }

    // ================================================================================================================ Combat Extended
    /// <summary>
    /// Combat Extended (CETeam.CombatExtended). Ammo lookup without a hard reference: weapon comp properties → <c>ammoSet</c>
    /// (AmmoSetDef) → <c>ammoTypes</c> (AmmoLink list) → <c>ammo</c>. Checked against CE's CombatExtended.dll for RimWorld 1.6.
    /// </summary>
    public static class KitCE
    {
        /// <summary>packageIds Combat Extended is published under.</summary>
        public static IReadOnlyList<string> PackageIds => ids;
        static readonly string[] ids = { "CETeam.CombatExtended" };

        /// <summary>Combat Extended is active.</summary>
        public static bool Active => KitDetect.AnyActive(ids);

        /// <summary>The weapon's CE ammo set def (an AmmoSetDef), or null (no CE, melee weapon, not a CE gun).</summary>
        public static Def AmmoSetOf(ThingDef weapon)
        {
            if (weapon?.comps == null || !Active) return null;
            if (sets.TryGetValue(weapon, out var hit)) return hit;
            Def found = null;
            try
            {
                foreach (var cp in weapon.comps)
                {
                    if (cp == null) continue;
                    var f = AccessTools.Field(cp.GetType(), "ammoSet");
                    if (f?.GetValue(cp) is Def set) { found = set; break; }
                }
            }
            catch (Exception e) { KitAdapterLog.Once("ce.ammoSet", "Combat Extended: ammo set lookup failed (" + e.GetType().Name + ": " + e.Message + "); JB mods treat guns as ammo-free."); }
            sets[weapon] = found;
            return found;
        }

        /// <summary>Every ammo of the weapon's ammo set in CE's order (empty without CE or for weapons without ammo).</summary>
        public static IReadOnlyList<ThingDef> AmmoTypesFor(ThingDef weapon)
        {
            if (weapon == null) return Array.Empty<ThingDef>();
            if (types.TryGetValue(weapon, out var hit)) return hit;
            var list = new List<ThingDef>();
            var set = AmmoSetOf(weapon);
            if (set != null)
            {
                try
                {
                    if (AccessTools.Field(set.GetType(), "ammoTypes")?.GetValue(set) is IList links)
                        foreach (var link in links)
                            if (link != null && AccessTools.Field(link.GetType(), "ammo")?.GetValue(link) is ThingDef ammo && !list.Contains(ammo)) list.Add(ammo);
                }
                catch (Exception e) { KitAdapterLog.Once("ce.ammoTypes", "Combat Extended: ammo list lookup failed (" + e.GetType().Name + ": " + e.Message + ")."); }
            }
            types[weapon] = list;
            return list;
        }

        /// <summary>The weapon's first (default) ammo, or null.</summary>
        public static ThingDef DefaultAmmoFor(ThingDef weapon)
        {
            var l = AmmoTypesFor(weapon);
            return l.Count > 0 ? l[0] : null;
        }

        /// <summary>Is the def one of CE's ammo items (it is listed in some weapon's ammo set)?</summary>
        public static bool IsAmmo(ThingDef def)
        {
            if (def == null || !Active) return false;
            if (allAmmo == null)
            {
                allAmmo = new HashSet<ThingDef>();
                try { foreach (var w in DefDatabase<ThingDef>.AllDefsListForReading) if (w.IsRangedWeapon) foreach (var a in AmmoTypesFor(w)) allAmmo.Add(a); }
                catch (Exception e) { KitAdapterLog.Once("ce.allAmmo", "Combat Extended: ammo scan failed (" + e.Message + ")."); }
            }
            return allAmmo.Contains(def);
        }

        static readonly Dictionary<ThingDef, Def> sets = new Dictionary<ThingDef, Def>();
        static readonly Dictionary<ThingDef, List<ThingDef>> types = new Dictionary<ThingDef, List<ThingDef>>();
        static HashSet<ThingDef> allAmmo;
    }

    // ================================================================================================================ Humanoid Alien Races
    /// <summary>
    /// Humanoid Alien Races (erdelf.HumanoidAlienRaces) and custom races in general. Race checks that need no HAR reference: a HAR race
    /// is a ThingDef of class <c>AlienRace.ThingDef_AlienRace</c> (checked against HAR's AlienRace.dll for 1.6); "custom-bodied" means a
    /// humanlike race whose body is not the human body (true for most HAR races, false for xenotypes, which are all human-bodied).
    /// </summary>
    public static class KitHAR
    {
        /// <summary>packageIds Humanoid Alien Races is published under.</summary>
        public static IReadOnlyList<string> PackageIds => ids;
        static readonly string[] ids = { "erdelf.HumanoidAlienRaces" };

        /// <summary>Humanoid Alien Races is active.</summary>
        public static bool Active => KitDetect.AnyActive(ids);

        /// <summary>Is the race def a HAR alien race (its class is AlienRace.ThingDef_AlienRace or derives from it)?</summary>
        public static bool IsAlienRace(ThingDef race)
        {
            if (race == null) return false;
            try
            {
                for (var t = race.GetType(); t != null && t != typeof(ThingDef); t = t.BaseType)
                    if (t.FullName == "AlienRace.ThingDef_AlienRace") return true;
            }
            catch (Exception e) { KitAdapterLog.Once("har.type", "Humanoid Alien Races: race type check failed (" + e.Message + ")."); }
            return false;
        }

        /// <summary>Is the pawn of a HAR alien race?</summary>
        public static bool IsAlien(Pawn p) => IsAlienRace(p?.def);

        /// <summary>
        /// A humanlike race with its own body (not BodyDefOf.Human). Code that walks body parts by human part defs (limbs, organs)
        /// should treat such races carefully (Luciferium Overhaul's default: vanilla healing for them).
        /// </summary>
        public static bool IsCustomBodied(ThingDef race)
        {
            var rp = race?.race;
            if (rp == null || !rp.Humanlike) return false;
            try { return rp.body != null && rp.body != BodyDefOf.Human; }
            catch (Exception e) { KitAdapterLog.Once("har.body", "Race body check failed (" + e.Message + ")."); return false; }
        }

        /// <summary>The pawn's race is custom-bodied.</summary>
        public static bool IsCustomBodied(Pawn p) => IsCustomBodied(p?.def);

        /// <summary>Every humanlike race def, humans first, then by label.</summary>
        public static IEnumerable<ThingDef> HumanlikeRaces()
            => DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.race != null && d.race.Humanlike)
                .OrderBy(d => d == ThingDefOf.Human ? 0 : 1).ThenBy(d => d.label ?? d.defName);
    }

    // ================================================================================================================ Nice Bill Tab
    /// <summary>
    /// Nice Bill Tab (Andromeda.NiceBillTab; also bundled inside Nice Inventory Tab when Nice Bill Tab itself is not active). It caches
    /// the filtered bill list of the open Bills tab and rebuilds it only on its own actions, so bills added or removed from code stay
    /// invisible until the tab is reopened. <see cref="RequestRefresh"/> raises its static <c>NiceBillTab.TabBillsDrawer.shouldRefreshFilter</c>
    /// flag (it rebuilds on the next frame and clears the flag itself). From Set my Defaults (BetterWorkbenchDefaults) NiceBillTabCompat.
    /// </summary>
    public static class KitNiceBillTab
    {
        /// <summary>packageIds that bring Nice Bill Tab's code.</summary>
        public static IReadOnlyList<string> PackageIds => ids;
        static readonly string[] ids = { "Andromeda.NiceBillTab", "Andromeda.NiceInventoryTab" };

        /// <summary>Nice Bill Tab's drawer is loaded (by itself or bundled).</summary>
        public static bool Active { get { Resolve(); return flag != null; } }

        /// <summary>Asks Nice Bill Tab to rebuild its bill list on the next frame. No-op without it. Never throws.</summary>
        public static void RequestRefresh()
        {
            Resolve();
            if (flag == null) return;
            try { flag.SetValue(null, true); }
            catch (Exception e) { KitAdapterLog.Once("nbt.refresh", "Nice Bill Tab: refresh failed (" + e.Message + "); bills added from code appear after reopening the tab."); flag = null; }
        }

        static bool resolved;
        static FieldInfo flag;

        static void Resolve()
        {
            if (resolved) return;
            // found by its type, not by packageId: Nice Bill Tab's 1.6 build runs on 1.7 as an "incompatible version" mod, and then
            // ModsConfig.IsActive and ModLister both say false for it (Set my Defaults request 6)
            if (!KitDetectReady) return;
            resolved = true;
            try
            {
                Type drawer = null;
                foreach (var mcp in LoadedModManager.RunningMods)
                {
                    foreach (var a in mcp?.assemblies?.loadedAssemblies ?? new List<Assembly>())
                    {
                        try { drawer = a?.GetType("NiceBillTab.TabBillsDrawer", false); } catch { drawer = null; }
                        if (drawer != null) break;
                    }
                    if (drawer != null) break;
                }
                var f = drawer != null ? AccessTools.Field(drawer, "shouldRefreshFilter") : null;
                if (f != null && f.IsStatic && f.FieldType == typeof(bool)) flag = f;
                else if (drawer != null || KitDetect.AnyActive(ids)) KitAdapterLog.Once("nbt.resolve", "Nice Bill Tab is active, but NiceBillTab.TabBillsDrawer.shouldRefreshFilter was not found (new version?): bills added from code appear after reopening the tab.");
            }
            catch (Exception e) { KitAdapterLog.Once("nbt.resolve", "Nice Bill Tab: lookup failed (" + e.Message + ")."); }
        }

        static bool KitDetectReady { get { try { return LoadedModManager.RunningModsListForReading.Count > 0; } catch { return false; } } }
    }

    // ================================================================================================================ Hearth & Harbor, Silverline
    /// <summary>Hearth &amp; Harbor and Silverline (JB mods with their own core): presence only.</summary>
    public static class KitHearth
    {
        /// <summary>packageId of Hearth &amp; Harbor.</summary>
        public const string HearthHarborId = "juliusberger00.hearthharbor";
        /// <summary>packageId of Silverline (the bank).</summary>
        public const string SilverlineId = "juliusberger00.silverline";

        /// <summary>Hearth &amp; Harbor is active.</summary>
        public static bool HearthHarbor => KitDetect.IsActive(HearthHarborId);
        /// <summary>Silverline is active.</summary>
        public static bool Silverline => KitDetect.IsActive(SilverlineId);
        /// <summary>Either of the two is active (guests and money may come from them).</summary>
        public static bool Any => HearthHarbor || Silverline;
    }

    // ================================================================================================================ VE pipe networks
    /// <summary>
    /// Vanilla Expanded Framework's PipeSystem (OskarPotocki.VanillaFactionsExpanded.Core): which defs and things belong to a pipe network
    /// (gas, chemfuel, nutrient paste, hemogen …). Read-only and by reflection: a thing is on a network when one of its comps derives from
    /// <c>PipeSystem.CompResource</c>; the network def is <c>CompProperties_Resource.pipeNet</c>. Checked against VEF's PipeSystem.dll for 1.7.
    /// </summary>
    public static class KitVEPipes
    {
        /// <summary>packageId of Vanilla Expanded Framework.</summary>
        public const string PackageId = "OskarPotocki.VanillaFactionsExpanded.Core";

        /// <summary>VEF is active and its PipeSystem types were found.</summary>
        public static bool Active { get { Resolve(); return compResource != null; } }

        /// <summary>defNames of the pipe networks the def connects to (empty when none or without VEF).</summary>
        public static IReadOnlyList<string> NetworksOf(ThingDef def)
        {
            if (def?.comps == null) return Array.Empty<string>();
            Resolve();
            if (propsResource == null) return Array.Empty<string>();
            if (nets.TryGetValue(def, out var hit)) return hit;
            var list = new List<string>();
            try
            {
                foreach (var cp in def.comps)
                    if (cp != null && propsResource.IsInstanceOfType(cp) && pipeNetField?.GetValue(cp) is Def net && !list.Contains(net.defName)) list.Add(net.defName);
            }
            catch (Exception e) { KitAdapterLog.Once("ve.nets", "VE pipe networks: lookup failed (" + e.Message + ")."); }
            nets[def] = list;
            return list;
        }

        /// <summary>Does the def connect to any VE pipe network?</summary>
        public static bool IsPipeDef(ThingDef def) => NetworksOf(def).Count > 0;

        /// <summary>Does the thing have a VE pipe network comp?</summary>
        public static bool IsOnPipeNet(Thing t)
        {
            if (!(t is ThingWithComps tc)) return false;
            Resolve();
            if (compResource == null) return false;
            try { foreach (var c in tc.AllComps) if (c != null && compResource.IsInstanceOfType(c)) return true; }
            catch (Exception e) { KitAdapterLog.Once("ve.thing", "VE pipe networks: thing check failed (" + e.Message + ")."); }
            return false;
        }

        static bool resolved;
        static Type compResource, propsResource;
        static FieldInfo pipeNetField;
        static readonly Dictionary<ThingDef, List<string>> nets = new Dictionary<ThingDef, List<string>>();

        static void Resolve()
        {
            if (resolved || !KitDetect.IsActive(PackageId)) return;
            resolved = true;
            try
            {
                compResource = AccessTools.TypeByName("PipeSystem.CompResource");
                propsResource = AccessTools.TypeByName("PipeSystem.CompProperties_Resource");
                pipeNetField = propsResource != null ? AccessTools.Field(propsResource, "pipeNet") : null;
                if (compResource == null || propsResource == null || pipeNetField == null)
                {
                    KitAdapterLog.Once("ve.resolve", "Vanilla Expanded Framework is active, but its PipeSystem types were not found (new version?): JB mods treat VE pipe buildings as ordinary buildings.");
                    compResource = null; propsResource = null;
                }
            }
            catch (Exception e) { KitAdapterLog.Once("ve.resolve", "VE pipe networks: lookup failed (" + e.Message + ")."); compResource = null; propsResource = null; }
        }
    }
}

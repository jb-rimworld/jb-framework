// SPDX-License-Identifier: MIT
// One implementation of what Definitely Not Dev Mode (Designator_DNDMDropdown + DNDMMaterialMenu) and Hospital Beds Gen 2
// (Patch_DropdownStuffMenu) each wrote: vanilla's Designator_Dropdown selects the picked Designator_Build WITHOUT calling its
// ProcessInput, the only place that opens the material menu, so a stuffed building picked from a dropdown silently uses the default
// material. The framework patches Designator_Dropdown.GetDesignatorSelectAction once (1.7 decompile and the 1.6 reference checked),
// however many mods register; the patch does nothing for defs no mod registered.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JBFramework
{
    /// <summary>
    /// The material menu for stuffed buildings, and "open it after a pick from a dropdown" for the defs a mod registers.
    /// </summary>
    public static class KitMaterialMenu
    {
        /// <summary>
        /// After a pick from any architect dropdown, open the material menu for the stuffed buildings <paramref name="applies"/> accepts.
        /// Opens vanilla's own menu (only materials on the map); the overload with offerAllMaterials = true opens <see cref="Open"/>
        /// (materials on the map first, the rest marked "none on the map"). Register in Register or OnDefsLoaded; defs are checked lazily.
        /// </summary>
        public static void AfterDropdownPick(KitMod owner, Func<ThingDef, bool> applies) => AfterDropdownPick(owner, applies, false);

        /// <summary>Like <see cref="AfterDropdownPick(KitMod, Func{ThingDef, bool})"/>; <paramref name="offerAllMaterials"/> true = the framework's menu with every material.</summary>
        public static void AfterDropdownPick(KitMod owner, Func<ThingDef, bool> applies, bool offerAllMaterials)
        {
            if (owner == null || applies == null) return;
            lock (rules) rules.Add(new Rule { Owner = owner, Applies = applies, OfferAll = offerAllMaterials });
            decided.Clear();
        }

        /// <summary>
        /// Opens the material menu for <paramref name="des"/> and selects it. Materials on the map come first (most common first); when
        /// the map has none, or under "Other materials", every other allowed material, marked "none on the map" (the blueprint then waits
        /// for it). Never silently falls back to a default. Not stuffed: just selects the designator.
        /// </summary>
        public static void Open(Designator_Build des)
        {
            if (des == null) return;
            var def = des.PlacingDef as ThingDef;
            Map map = Find.CurrentMap;
            if (def == null || !def.MadeFromStuff || map == null) { Find.DesignatorManager.Select(des); return; }
            var all = GenStuff.AllowedStuffsFor(def).ToList();
            var onMap = all.Where(s => map.listerThings.ThingsOfDef(s).Count > 0)
                .OrderByDescending(Commonality).ThenBy(s => s.BaseMarketValue).ToList();
            var rest = all.Where(s => !onMap.Contains(s)).OrderByDescending(Commonality).ThenBy(s => s.BaseMarketValue).ToList();
            var opts = new List<FloatMenuOption>();
            foreach (var s in onMap) opts.Add(Option(des, def, s, false));
            if (onMap.Count == 0) foreach (var s in rest) opts.Add(Option(des, def, s, true));
            else if (rest.Count > 0)
                opts.Add(new FloatMenuOption(KitText.K("Material.Other", "Other materials ({0}, none on the map)...", rest.Count), () =>
                    Find.WindowStack.Add(new FloatMenu(rest.Select(s => Option(des, def, s, true)).ToList()))));
            if (opts.Count == 0) { Messages.Message("NoStuffsToBuildWith".Translate(), MessageTypeDefOf.RejectInput, false); return; }
            Find.WindowStack.Add(new FloatMenu(opts) { onCloseCallback = () => MarkChosen(des) });
            Find.DesignatorManager.Select(des);
        }

        /// <summary>Is the designator a stuffed building (it needs a material)?</summary>
        public static bool IsStuffed(Designator d) => d is Designator_Build b && b.PlacingDef is ThingDef td && td.MadeFromStuff;

        // internal -------------------------------------------------------------------------------------------------------
        sealed class Rule { public KitMod Owner; public Func<ThingDef, bool> Applies; public bool OfferAll; }
        static readonly List<Rule> rules = new List<Rule>();
        // def → rule (null = no rule) ; cleared when a rule is added
        static readonly Dictionary<ThingDef, Rule> decided = new Dictionary<ThingDef, Rule>();

        internal static bool AnyRule { get { lock (rules) return rules.Count > 0; } }

        static Rule RuleFor(ThingDef def)
        {
            if (def == null) return null;
            if (decided.TryGetValue(def, out var hit)) return hit;
            Rule found = null;
            Rule[] snap; lock (rules) snap = rules.ToArray();
            foreach (var r in snap)
            {
                if (r.Owner.Blocked) continue;
                bool yes = r.Owner.Run(KitMod.Core, "MaterialMenu.applies", () => r.Applies(def), false);
                if (yes) { found = r; break; }
            }
            decided[def] = found;
            return found;
        }

        /// <summary>The wrapped select action for a dropdown pick, or null to keep vanilla's.</summary>
        internal static Action Wrap(Event ev, Designator des, Action original)
        {
            if (original == null || !(des is Designator_Build build) || !(build.PlacingDef is ThingDef def) || !def.MadeFromStuff) return null;
            var rule = RuleFor(def);
            if (rule == null) return null;
            return () =>
            {
                original();
                rule.Owner.Run(KitMod.Core, "MaterialMenu.open", () =>
                {
                    if (rule.OfferAll) Open(build);
                    else build.ProcessInput(ev);
                });
            };
        }

        static float Commonality(ThingDef s) => s.stuffProps?.commonality ?? 0f;

        static AccessTools.FieldRef<Designator_Build, bool> writeStuffRef;
        static bool writeStuffTried;

        /// <summary>Vanilla shows "(material)" in the label only after a choice (private writeStuff); set it like vanilla does.</summary>
        static void MarkChosen(Designator_Build des)
        {
            if (!writeStuffTried)
            {
                writeStuffTried = true;
                try
                {
                    if (AccessTools.Field(typeof(Designator_Build), "writeStuff") != null)
                        writeStuffRef = AccessTools.FieldRefAccess<Designator_Build, bool>("writeStuff");
                }
                catch (Exception e) { KitAdapterLog.Once("material.writeStuff", "Material menu: Designator_Build.writeStuff not accessible (" + e.Message + "); the build label may not show the material."); }
            }
            if (writeStuffRef != null) writeStuffRef(des) = true;
        }

        static FloatMenuOption Option(Designator_Build des, ThingDef def, ThingDef stuff, bool none)
        {
            string label = GenLabel.ThingLabel(def, stuff).CapitalizeFirst();
            if (none) label += " " + KitText.K("Material.NoneOnMap", "(none on the map, the blueprint waits for it)");
            return new FloatMenuOption(label, () =>
            {
                des.SetStuffDef(stuff);
                MarkChosen(des);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                Find.DesignatorManager.Select(des);
            }, stuff);
        }
    }

    /// <summary>The one patch behind <see cref="KitMaterialMenu.AfterDropdownPick(KitMod, Func{ThingDef, bool})"/>. Early-out unless a mod registered.</summary>
    [HarmonyPatch]
    internal static class Patch_KitDropdownMaterialMenu
    {
        static MethodBase target;

        static bool Prepare()
        {
            target = AccessTools.Method(typeof(Designator_Dropdown), "GetDesignatorSelectAction", new[] { typeof(Event), typeof(Designator) });
            return target != null;
        }

        static MethodBase TargetMethod() => target;

        static void Postfix(Event __0, Designator __1, ref Action __result)   // by position: parameter names differ between builds
        {
            if (!KitMaterialMenu.AnyRule || __result == null) return;
            try
            {
                var wrapped = KitMaterialMenu.Wrap(__0, __1, __result);
                if (wrapped != null) __result = wrapped;
            }
            catch (Exception e) { Framework.Self?.Guard.Fail(KitMod.Core, "MaterialMenu.patch", e); }
        }
    }
}

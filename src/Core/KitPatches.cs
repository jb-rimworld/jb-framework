// SPDX-License-Identifier: MIT
// Patch applier from Hearth & Harbor Foundation/HearthMod.cs (ApplyPatches): every [HarmonyPatch] class one by one, failures listed.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace JBKit
{
    /// <summary>
    /// Applies every [HarmonyPatch] class of the mod's assemblies one by one. A class that fails (target missing, conflicting mod …)
    /// is logged and listed in <see cref="Failures"/>; everything else keeps working. Classes with [KitCompat(id)] are skipped silently
    /// when that mod is not active. Harmony is only touched when at least one patch class exists, so a mod without patches needs no Harmony.
    /// </summary>
    internal static class KitPatches
    {
        /// <summary>"Type.FullName: reason" per skipped class.</summary>
        public static readonly List<string> Failures = new List<string>();
        /// <summary>Patch classes skipped because their [KitCompat] mod is not active.</summary>
        public static readonly List<string> SkippedInactive = new List<string>();
        public static int Applied;
        /// <summary>The mod's Harmony instance (as object, so callers without Harmony references compile). Null before patching or without patch classes.</summary>
        public static object HarmonyInstance;
        /// <summary>True once ApplyAll ran.</summary>
        public static bool Done;

        internal static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
            catch { return Enumerable.Empty<Type>(); }
        }

        static bool HasHarmonyPatchAttribute(Type t)
        {
            try { return t.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"); }
            catch { return false; }
        }

        internal static void ApplyAll(IEnumerable<Assembly> assemblies, string harmonyId)
        {
            if (Done) return;
            Done = true;
            var asms = assemblies.Where(a => a != null && !IsLibrary(a)).Distinct().ToList();
            var classes = asms.SelectMany(SafeTypes).Where(HasHarmonyPatchAttribute).ToList();
            if (classes.Count == 0) { KitLog.Info("Core", "Harmony: no patch classes"); return; }
            try { ApplyWithHarmony(classes, harmonyId); }
            catch (Exception e)
            {
                // Harmony itself missing (mod loaded without brrainz.harmony): name it once, keep the mod running
                Failures.Add("Harmony: " + (e.InnerException ?? e).Message);
                KitLog.Error("Core", "Harmony is not available; " + classes.Count + " patch classes not applied. Is the Harmony mod active and loaded before this mod?", e);
            }
        }

        static bool IsLibrary(Assembly a)
        {
            string n;
            try { n = a.GetName().Name; } catch { return true; }
            return n == "0Harmony" || n.StartsWith("HarmonyMod", StringComparison.Ordinal);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ApplyWithHarmony(List<Type> classes, string harmonyId)
        {
            var harmony = new HarmonyLib.Harmony(harmonyId);
            HarmonyInstance = harmony;
            foreach (var t in classes)
            {
                var compat = t.GetCustomAttribute<KitCompatAttribute>(false);
                if (compat != null && !KitCompat.IsModActive(compat.PackageId)) { SkippedInactive.Add(t.FullName); continue; }
                try
                {
                    harmony.CreateClassProcessor(t).Patch();
                    Applied++;
                    if (compat != null) KitCompat.ReportApplied(compat.PackageId);
                }
                catch (Exception e)
                {
                    var inner = e.InnerException ?? e;
                    Failures.Add(t.FullName + ": " + inner.Message);
                    if (compat != null) KitCompat.ReportFailure(compat.PackageId, t.Name + ": " + inner.Message);
                    KitLog.Error("Core", "Patch class " + t.FullName + " skipped", inner);
                }
            }
            KitLog.Info("Core", $"Harmony ({harmonyId}): {Applied} patch classes applied, {Failures.Count} skipped, {SkippedInactive.Count} not needed (integration not installed)");
        }

        /// <summary>
        /// Methods this mod patches that other Harmony owners patch too ("Type.Method: owner, owner"). For the diagnosis page. Never throws.
        /// </summary>
        public static List<string> SharedTargets()
        {
            try { return HarmonyInstance == null ? new List<string>() : SharedTargetsInner(); }
            catch (Exception e) { return new List<string> { "(could not read Harmony patch info: " + e.Message + ")" }; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static List<string> SharedTargetsInner()
        {
            var h = (HarmonyLib.Harmony)HarmonyInstance;
            var list = new List<string>();
            foreach (var m in h.GetPatchedMethods())
            {
                var info = HarmonyLib.Harmony.GetPatchInfo(m);
                if (info == null) continue;
                var others = info.Owners.Where(o => o != h.Id).Distinct().ToList();
                if (others.Count > 0) list.Add((m.DeclaringType?.Name ?? "?") + "." + m.Name + ": " + string.Join(", ", others));
            }
            return list;
        }
    }
}

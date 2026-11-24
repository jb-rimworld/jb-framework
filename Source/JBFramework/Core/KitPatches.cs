// SPDX-License-Identifier: MIT
// Patch applier from Hearth & Harbor Foundation/HearthMod.cs (ApplyPatches): every [HarmonyPatch] class one by one, failures listed; one per mod.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace JBFramework
{
    /// <summary>
    /// Applies every [HarmonyPatch] class of one mod's assemblies one by one, with the mod's own Harmony id. A class that fails (target
    /// missing, conflicting mod …) is logged and listed in <see cref="Failures"/>; everything else keeps working. Classes with
    /// [KitCompat(id)] are skipped silently when that mod is not active.
    /// </summary>
    internal sealed class KitPatches
    {
        readonly KitMod k;
        /// <summary>"Type.FullName: reason" per skipped class.</summary>
        public readonly List<string> Failures = new List<string>();
        /// <summary>Patch classes skipped because their [KitCompat] mod is not active.</summary>
        public readonly List<string> SkippedInactive = new List<string>();
        public int Applied;
        /// <summary>The mod's Harmony instance (as object). Null before patching or without patch classes.</summary>
        public object HarmonyInstance;
        /// <summary>True once ApplyAll ran.</summary>
        public bool Done;

        public KitPatches(KitMod owner) { k = owner; }

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

        internal void ApplyAll(IEnumerable<Assembly> assemblies, string harmonyId)
        {
            if (Done) return;
            Done = true;
            var asms = assemblies.Where(a => a != null && !IsLibrary(a)).Distinct().ToList();
            var classes = asms.SelectMany(SafeTypes).Where(HasHarmonyPatchAttribute).ToList();
            if (classes.Count == 0) { k.Log.Info("Core", "Harmony: no patch classes"); return; }
            try { ApplyWithHarmony(classes, harmonyId); }
            catch (Exception e)
            {
                // Harmony itself missing (mod loaded without brrainz.harmony): name it once, keep the mod running
                Failures.Add("Harmony: " + (e.InnerException ?? e).Message);
                k.Log.Error("Core", "Harmony is not available; " + classes.Count + " patch classes not applied. Is the Harmony mod active and loaded before this mod?", e);
            }
        }

        static bool IsLibrary(Assembly a)
        {
            string n;
            try { n = a.GetName().Name; } catch { return true; }
            return n == "0Harmony" || n.StartsWith("HarmonyMod", StringComparison.Ordinal);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void ApplyWithHarmony(List<Type> classes, string harmonyId)
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
                    if (compat != null) k.Compat.ReportApplied(compat.PackageId);
                }
                catch (Exception e)
                {
                    var inner = e.InnerException ?? e;
                    Failures.Add(t.FullName + ": " + inner.Message);
                    if (compat != null) k.Compat.ReportFailure(compat.PackageId, t.Name + ": " + inner.Message);
                    k.Log.Error("Core", "Patch class " + t.FullName + " skipped", inner);
                }
            }
            k.Log.Info("Core", $"Harmony ({harmonyId}): {Applied} patch classes applied, {Failures.Count} skipped, {SkippedInactive.Count} not needed (integration not installed)");
        }

        /// <summary>
        /// Methods this mod patches that other Harmony owners patch too ("Type.Method: owner, owner"). For the diagnosis page. Never throws.
        /// </summary>
        public List<string> SharedTargets()
        {
            try { return HarmonyInstance == null ? new List<string>() : SharedTargetsInner(); }
            catch (Exception e) { return new List<string> { "(could not read Harmony patch info: " + e.Message + ")" }; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        List<string> SharedTargetsInner()
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

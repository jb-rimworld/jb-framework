// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;

namespace JBFramework
{
    /// <summary>
    /// Internal extension points, so new framework parts (compatibility knowledge base, shared functions, the test harness assembly
    /// JBFramework.Testing via InternalsVisibleTo) plug in from their own files without editing Core or UI. Every hook is called inside
    /// a fence: an exception is booked on the mod concerned (or on the framework) and never stops the other hooks. Add with +=/Add at
    /// static-constructor time or from a [StaticConstructorOnStartup] class; hooks added after an event fired do not see it again.
    /// </summary>
    internal static class KitHooks
    {
        /// <summary>After a mod's boot sequence (registration, settings, patches) finished. Not called for blocked mods.</summary>
        public static readonly List<Action<KitMod>> ModBooted = new List<Action<KitMod>>();
        /// <summary>Once, after every mod's OnDefsLoaded ran (StaticConstructorOnStartup of the framework).</summary>
        public static readonly List<Action> DefsLoaded = new List<Action>();
        /// <summary>Extra lines for a mod's diagnosis report (appended after the mod's own Diagnosis()).</summary>
        public static readonly List<Func<KitMod, IEnumerable<string>>> ReportLines = new List<Func<KitMod, IEnumerable<string>>>();
        /// <summary>
        /// Extra parts of a mod's Compatibility page, drawn after the integrations and known conflicts: (mod, width, y) → new y.
        /// Drawn inside the page's scroll view and error fence.
        /// </summary>
        public static readonly List<Func<KitMod, float, float, float>> CompatPage = new List<Func<KitMod, float, float, float>>();

        internal static void Each<T>(List<T> list, KitMod k, string site, Action<T> call)
        {
            if (list.Count == 0) return;
            foreach (var h in list.ToArray())
            {
                try { call(h); }
                catch (Exception e)
                {
                    var owner = k ?? Framework.Self;
                    try { owner?.Guard.Fail(KitMod.Core, "Hook." + site, e); } catch { }
                }
            }
        }
    }
}

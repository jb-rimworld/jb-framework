// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// Which DLCs and mods are active. Every check accepts a packageId in any case, with or without the Workshop "_steam" postfix (a
    /// jbmod test slot's ".slotN" copy of a local mod counts as that mod), and
    /// never throws (false when the game is not ready). The active-mod list cannot change while the game runs, so results are cached
    /// after the first call; <see cref="Refresh"/> clears the cache (only tests need it).
    /// </summary>
    public static class KitDetect
    {
        /// <summary>packageId of the Royalty DLC.</summary>
        public const string RoyaltyId = "ludeon.rimworld.royalty";
        /// <summary>packageId of the Ideology DLC.</summary>
        public const string IdeologyId = "ludeon.rimworld.ideology";
        /// <summary>packageId of the Biotech DLC.</summary>
        public const string BiotechId = "ludeon.rimworld.biotech";
        /// <summary>packageId of the Anomaly DLC.</summary>
        public const string AnomalyId = "ludeon.rimworld.anomaly";
        /// <summary>packageId of the Odyssey DLC.</summary>
        public const string OdysseyId = "ludeon.rimworld.odyssey";

        /// <summary>Royalty is active.</summary>
        public static bool Royalty => IsActive(RoyaltyId);
        /// <summary>Ideology is active.</summary>
        public static bool Ideology => IsActive(IdeologyId);
        /// <summary>Biotech is active.</summary>
        public static bool Biotech => IsActive(BiotechId);
        /// <summary>Anomaly is active.</summary>
        public static bool Anomaly => IsActive(AnomalyId);
        /// <summary>Odyssey is active.</summary>
        public static bool Odyssey => IsActive(OdysseyId);

        /// <summary>English names of the active DLCs, in release order.</summary>
        public static IReadOnlyList<string> ActiveDlcNames => KitCompat.ActiveDlcNames();

        /// <summary>Is the mod or DLC active? Cached per packageId.</summary>
        public static bool IsActive(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return false;
            string key = KitCompat.Strip(packageId).ToLowerInvariant();
            lock (cache)
            {
                if (cache.TryGetValue(key, out bool hit)) return hit;
            }
            bool v = KitCompat.IsModActive(packageId);
            if (ModsReady) lock (cache) cache[key] = v;
            return v;
        }

        /// <summary>True when at least one of the packageIds is active (for mods that were re-uploaded under new ids).</summary>
        public static bool AnyActive(params string[] packageIds) => packageIds != null && packageIds.Any(IsActive);

        /// <summary>The first active packageId of the list (as written in the list), or null.</summary>
        public static string FirstActive(params string[] packageIds) => packageIds?.FirstOrDefault(IsActive);

        /// <summary>The display name of an active mod (as in its About.xml), or null when it is not active.</summary>
        public static string ActiveName(string packageId)
        {
            if (!IsActive(packageId)) return null;
            try
            {
                var m = ModLister.GetActiveModWithIdentifier(KitCompat.Strip(packageId), ignorePostfix: true);
                if (m != null) return m.Name;
                string want = Normalize(packageId);
                return ModsConfig.ActiveModsInLoadOrder.FirstOrDefault(x => x != null && Normalize(x.PackageId) == want)?.Name;
            }
            catch { return null; }
        }

        /// <summary>
        /// packageIds of all active mods and DLCs, lower case, without the "_steam" postfix, in load order. Empty before the game read
        /// the mod list.
        /// </summary>
        public static IReadOnlyList<string> ActivePackageIds
        {
            get
            {
                var l = activeIds;
                if (l != null) return l;
                var list = new List<string>();
                try { foreach (var m in ModsConfig.ActiveModsInLoadOrder) if (m != null) list.Add(KitCompat.Strip(m.PackageId).ToLowerInvariant()); }
                catch { }
                if (list.Count > 0) activeIds = list;
                return list;
            }
        }

        /// <summary>Clears the cached answers.</summary>
        public static void Refresh() { lock (cache) cache.Clear(); activeIds = null; KitCompat.ClearCache(); }

        static readonly Dictionary<string, bool> cache = new Dictionary<string, bool>();
        static List<string> activeIds;

        // the mod list is final once the mod classes exist; before that (very early) answers are not cached
        static bool ModsReady
        {
            get { try { return LoadedModManager.RunningModsListForReading.Count > 0; } catch { return false; } }
        }

        /// <summary>packageId without the Workshop "_steam" postfix and without a test-slot suffix ".slotN" (tools/jbmod), trimmed, lower case ("" for null).</summary>
        public static string Normalize(string packageId) => KitCompat.Strip(packageId).ToLowerInvariant();

        /// <summary>The two ids name the same mod (case, "_steam" and a test slot's ".slotN" ignored).</summary>
        public static bool Same(string a, string b) => KitCompat.Same(a, b);
    }
}

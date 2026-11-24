// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace JBKit
{
    /// <summary>
    /// Marks a [HarmonyPatch] class as part of a soft integration. When the named mod/DLC is not active the class is not applied
    /// (no error); when it is active and the class fails, the matching compat entry shows "failed: reason".
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    internal sealed class KitCompatAttribute : Attribute
    {
        public readonly string PackageId;
        public KitCompatAttribute(string packageId) { PackageId = packageId; }
    }

    internal enum KitCompatState { NotInstalled, Active, Failed, Unhealthy, IncompatiblePresent, IncompatibleAbsent }

    /// <summary>One line of the Compatibility page: an integration or a known incompatibility.</summary>
    internal sealed class KitCompatEntry
    {
        public string Name;
        public string PackageId;
        /// <summary>What the integration does (integrations) or why it conflicts and what to do (incompatibilities). English fallback.</summary>
        public string WhatEn;
        public bool Incompatible;
        public Func<bool> Healthy;
        /// <summary>Reasons collected from failed [KitCompat] patch classes.</summary>
        public readonly List<string> Failures = new List<string>();
        public int PatchesApplied;

        public string What => KitText.Key("Compat." + KitSetting.CatKey(Name) + (Incompatible ? ".Why" : ".What")).TranslateOr(WhatEn ?? "");

        public KitCompatState State
        {
            get
            {
                bool active = KitCompat.IsModActive(PackageId);
                if (Incompatible) return active ? KitCompatState.IncompatiblePresent : KitCompatState.IncompatibleAbsent;
                if (!active) return KitCompatState.NotInstalled;
                if (Failures.Count > 0) return KitCompatState.Failed;
                if (Healthy != null)
                {
                    try { if (!Healthy()) return KitCompatState.Unhealthy; }
                    catch (Exception e) { KitLog.Once("compatHealthy:" + PackageId, KitLogLevel.Warn, "Compat", Name + " health check threw: " + e.Message); return KitCompatState.Unhealthy; }
                }
                return KitCompatState.Active;
            }
        }

        public string StateLabel
        {
            get
            {
                switch (State)
                {
                    case KitCompatState.Active: return KitText.K("Compat.Active", "active");
                    case KitCompatState.NotInstalled: return KitText.K("Compat.NotInstalled", "not installed");
                    case KitCompatState.Failed: return KitText.K("Compat.Failed", "failed: {0}", string.Join("; ", Failures));
                    case KitCompatState.Unhealthy: return KitText.K("Compat.Unhealthy", "installed, but the integration reports a problem");
                    case KitCompatState.IncompatiblePresent: return KitText.K("Compat.IncompatiblePresent", "installed: known conflict");
                    default: return KitText.K("Compat.IncompatibleAbsent", "not installed (good)");
                }
            }
        }
    }

    /// <summary>Compatibility registry, DLC detection and the mod-active helper used everywhere in the kit.</summary>
    internal static class KitCompat
    {
        public static readonly List<KitCompatEntry> All = new List<KitCompatEntry>();

        /// <summary>The five DLCs (packageId → English name).</summary>
        public static readonly (string Id, string Name)[] Dlcs =
        {
            ("ludeon.rimworld.royalty", "Royalty"), ("ludeon.rimworld.ideology", "Ideology"), ("ludeon.rimworld.biotech", "Biotech"),
            ("ludeon.rimworld.anomaly", "Anomaly"), ("ludeon.rimworld.odyssey", "Odyssey"),
        };

        public static KitCompatEntry Add(KitCompatEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.PackageId)) return null;
            var existing = All.FirstOrDefault(x => x.Incompatible == e.Incompatible && Same(x.PackageId, e.PackageId));
            if (existing != null) return existing;
            All.Add(e);
            return e;
        }

        public static KitCompatEntry Find(string packageId) => All.FirstOrDefault(x => !x.Incompatible && Same(x.PackageId, packageId));

        static bool Same(string a, string b) => string.Equals(Strip(a), Strip(b), StringComparison.OrdinalIgnoreCase);
        static string Strip(string id)
        {
            if (id == null) return "";
            id = id.Trim();
            return id.EndsWith("_steam", StringComparison.OrdinalIgnoreCase) ? id.Substring(0, id.Length - 6) : id;
        }

        /// <summary>Is a mod or DLC active? Accepts packageIds with or without the Workshop "_steam" postfix, any case. Never throws.</summary>
        public static bool IsModActive(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return false;
            try
            {
                if (ModsConfig.IsActive(packageId)) return true;
                return ModLister.GetActiveModWithIdentifier(Strip(packageId), ignorePostfix: true) != null;
            }
            catch { return false; }
        }

        /// <summary>English names of the active DLCs.</summary>
        public static List<string> ActiveDlcNames() => Dlcs.Where(d => IsModActive(d.Id)).Select(d => d.Name).ToList();

        /// <summary>Called by the patch applier for a failed [KitCompat] patch class.</summary>
        internal static void ReportFailure(string packageId, string reason)
        {
            var e = Find(packageId);
            if (e == null) e = Add(new KitCompatEntry { Name = packageId, PackageId = packageId, WhatEn = "" });
            e.Failures.Add(reason);
        }

        internal static void ReportApplied(string packageId)
        {
            var e = Find(packageId);
            if (e != null) e.PatchesApplied++;
        }
    }
}

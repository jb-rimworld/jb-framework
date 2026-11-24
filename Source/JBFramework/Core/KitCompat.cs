// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// Marks a [HarmonyPatch] class as part of a soft integration. When the named mod or DLC is not active the class is not applied
    /// (no error); when it is active and the class fails, the mod's matching compatibility entry shows "failed: reason".
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class KitCompatAttribute : Attribute
    {
        /// <summary>The other mod's packageId (or a DLC's, "ludeon.rimworld.biotech").</summary>
        public KitCompatAttribute(string packageId) { PackageId = packageId; }
        /// <summary>The other mod's packageId.</summary>
        public string PackageId { get; }
    }

    internal enum KitCompatState { NotInstalled, Active, Failed, Unhealthy, IncompatiblePresent, IncompatibleAbsent, SwitchedOff }

    /// <summary>One line of a mod's Compatibility page: an integration or a known incompatibility.</summary>
    public sealed class KitCompatEntry
    {
        internal KitCompatEntry() { }
        internal KitMod Owner;
        internal string WhatEn;
        internal bool Incompatible;
        internal Func<bool> Healthy;
        /// <summary>Reasons collected from failed [KitCompat] patch classes.</summary>
        internal readonly List<string> Failures = new List<string>();
        internal int PatchesApplied;

        /// <summary>The other mod's name as shown.</summary>
        public string Name { get; internal set; }
        /// <summary>The other mod's packageId (or a DLC's).</summary>
        public string PackageId { get; internal set; }

        /// <summary>A self-check of the integration: false shows "installed, but the integration reports a problem". Fenced.</summary>
        public KitCompatEntry HealthyWhen(Func<bool> healthy) { Healthy = healthy; return this; }

        /// <summary>
        /// The player can switch this integration off on the Compatibility page (PLAN G13c; on by default). The switch is a preference
        /// of the installation (settings file, the same in every save). The mod asks <see cref="KitMod.CompatOn"/> in the integration's
        /// code (its [KitCompat] patch classes are still applied: they must check it); a change arrives as OnSettingsChanged("compat:&lt;packageId&gt;").
        /// </summary>
        public KitCompatEntry Switchable() { CanSwitch = true; return this; }

        internal bool CanSwitch;
        /// <summary>Store key of the switch in the template: "compat:" + the normalized packageId (never clashes with a setting key).</summary>
        internal string SwitchKey => KitMod.CompatKeyPrefix + KitDetect.Normalize(PackageId);
        /// <summary>Switched on (true unless the player switched a switchable entry off).</summary>
        internal bool SwitchedOn => !CanSwitch || Owner == null || !Owner.Template.Store.TryGet(SwitchKey, out float v) || v >= 0.5f;

        internal string What => Owner.Key("Compat." + KitSetting.CatKey(Name) + (Incompatible ? ".Why" : ".What")).TranslateOr(WhatEn ?? "");

        internal KitCompatState State
        {
            get
            {
                bool active = KitCompat.IsModActive(PackageId);
                if (Incompatible) return active ? KitCompatState.IncompatiblePresent : KitCompatState.IncompatibleAbsent;
                if (!active) return KitCompatState.NotInstalled;
                if (Failures.Count > 0) return KitCompatState.Failed;
                if (!SwitchedOn) return KitCompatState.SwitchedOff;
                if (Healthy != null)
                {
                    try { if (!Healthy()) return KitCompatState.Unhealthy; }
                    catch (Exception e) { Owner.Log.Once("compatHealthy:" + PackageId, KitLogLevel.Warn, "Compat", Name + " health check threw: " + e.Message); return KitCompatState.Unhealthy; }
                }
                return KitCompatState.Active;
            }
        }

        internal string StateLabel
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
                    case KitCompatState.SwitchedOff: return KitText.K("Compat.SwitchedOff", "installed, integration switched off");
                    default: return KitText.K("Compat.IncompatibleAbsent", "not installed (good)");
                }
            }
        }
    }

    /// <summary>One mod's compatibility entries.</summary>
    internal sealed class KitCompatRegistry
    {
        readonly KitMod k;
        public readonly List<KitCompatEntry> All = new List<KitCompatEntry>();
        public KitCompatRegistry(KitMod owner) { k = owner; }

        public KitCompatEntry Add(KitCompatEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.PackageId)) return null;
            var existing = All.FirstOrDefault(x => x.Incompatible == e.Incompatible && KitCompat.Same(x.PackageId, e.PackageId));
            if (existing != null) return existing;
            e.Owner = k;
            All.Add(e);
            return e;
        }

        public KitCompatEntry Find(string packageId) => All.FirstOrDefault(x => !x.Incompatible && KitCompat.Same(x.PackageId, packageId));

        /// <summary>Called by the patch applier for a failed [KitCompat] patch class.</summary>
        internal void ReportFailure(string packageId, string reason)
        {
            var e = Find(packageId) ?? Add(new KitCompatEntry { Name = packageId, PackageId = packageId, WhatEn = "" });
            e?.Failures.Add(reason);
        }

        internal void ReportApplied(string packageId)
        {
            var e = Find(packageId);
            if (e != null) e.PatchesApplied++;
        }
    }

    /// <summary>DLC list and the mod-active helper used everywhere in the framework (shared, stateless).</summary>
    internal static class KitCompat
    {
        /// <summary>The five DLCs (packageId → English name).</summary>
        public static readonly (string Id, string Name)[] Dlcs =
        {
            ("ludeon.rimworld.royalty", "Royalty"), ("ludeon.rimworld.ideology", "Ideology"), ("ludeon.rimworld.biotech", "Biotech"),
            ("ludeon.rimworld.anomaly", "Anomaly"), ("ludeon.rimworld.odyssey", "Odyssey"),
        };

        internal static bool Same(string a, string b) => string.Equals(Strip(a), Strip(b), StringComparison.OrdinalIgnoreCase);

        static readonly System.Text.RegularExpressions.Regex SlotSuffix = new System.Text.RegularExpressions.Regex(@"\.slot\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// packageId without the Workshop "_steam" postfix and without a test-slot suffix ".slotN" (tools/jbmod renames the slot copies of
        /// local mods; no real packageId ends that way), trimmed.
        /// </summary>
        internal static string Strip(string id)
        {
            if (id == null) return "";
            id = id.Trim();
            if (id.EndsWith("_steam", StringComparison.OrdinalIgnoreCase)) id = id.Substring(0, id.Length - 6);
            return SlotSuffix.Replace(id, "");
        }

        static readonly Dictionary<string, bool> activeCache = new Dictionary<string, bool>();
        internal static void ClearCache() { lock (activeCache) activeCache.Clear(); }

        /// <summary>Is a mod or DLC active? Accepts packageIds with or without the "_steam" postfix, any case; a test slot's ".slotN" copy counts. Never throws.</summary>
        public static bool IsModActive(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return false;
            try
            {
                if (ModsConfig.IsActive(packageId)) return true;
                if (ModLister.GetActiveModWithIdentifier(Strip(packageId), ignorePostfix: true) != null) return true;
                // slow path (a jbmod slot copy "<id>.slotN"): once per id, the mod list is final once the mod classes exist
                string want = Strip(packageId).ToLowerInvariant();
                lock (activeCache) if (activeCache.TryGetValue(want, out bool hit)) return hit;
                bool found = false;
                foreach (var m in ModsConfig.ActiveModsInLoadOrder)
                    if (m != null && Strip(m.PackageId).Equals(want, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                bool ready; try { ready = LoadedModManager.RunningModsListForReading.Count > 0; } catch { ready = false; }
                if (ready) lock (activeCache) activeCache[want] = found;
                return found;
            }
            catch { return false; }
        }

        /// <summary>English names of the active DLCs.</summary>
        public static List<string> ActiveDlcNames() => Dlcs.Where(d => IsModActive(d.Id)).Select(d => d.Name).ToList();
    }
}

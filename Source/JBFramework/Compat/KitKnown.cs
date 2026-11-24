// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// Queries over the compatibility knowledge base (<see cref="ModNoteDef"/>, shipped with the framework, extendable by any mod's
    /// XML). Available once defs are loaded (from <see cref="KitModBase.OnDefsLoaded"/> on; earlier calls return empty results).
    /// A mod states the tags it cares about with <see cref="Declare"/>; its Compatibility page then lists the active mods with those
    /// tags, their known pitfalls and advice, and the other Harmony owners of the methods it patches.
    /// </summary>
    public static class KitKnown
    {
        /// <summary>Every note (empty before defs are loaded).</summary>
        public static IReadOnlyList<ModNoteDef> All
        {
            get
            {
                try { return DefDatabase<ModNoteDef>.AllDefsListForReading; }
                catch { return Array.Empty<ModNoteDef>(); }
            }
        }

        /// <summary>The note for a packageId (any case, with or without "_steam"), or null.</summary>
        public static ModNoteDef Note(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return null;
            var idx = Index();
            return idx.TryGetValue(KitDetect.Normalize(packageId), out var n) ? n : null;
        }

        /// <summary>Notes with the tag (active or not).</summary>
        public static IEnumerable<ModNoteDef> WithTag(string tag) => All.Where(n => n.HasTag(tag));

        /// <summary>Notes of the mods active in this game.</summary>
        public static IEnumerable<ModNoteDef> Active() => All.Where(n => n.Active);

        /// <summary>Notes of active mods with the tag ("Is any bills UI mod active?").</summary>
        public static IEnumerable<ModNoteDef> ActiveWithTag(string tag) => All.Where(n => n.HasTag(tag) && n.Active);

        /// <summary>Is any active mod known to have the tag?</summary>
        public static bool AnyActiveWithTag(string tag) => ActiveWithTag(tag).Any();

        /// <summary>
        /// Mod <paramref name="k"/> cares about these tags (call in Register or OnDefsLoaded; repeated calls add). Its Compatibility
        /// page and diagnosis report then list the active known mods with any of them.
        /// </summary>
        public static void Declare(KitMod k, params string[] tags)
        {
            if (k == null || tags == null) return;
            lock (declared)
            {
                if (!declared.TryGetValue(k, out var set)) { set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); declared[k] = set; }
                foreach (var t in tags) if (!string.IsNullOrEmpty(t)) set.Add(t.Trim().ToLowerInvariant());
            }
        }

        /// <summary>The tags mod <paramref name="k"/> declared.</summary>
        public static IReadOnlyCollection<string> DeclaredTags(KitMod k)
        {
            lock (declared) return k != null && declared.TryGetValue(k, out var s) ? s.ToArray() : Array.Empty<string>();
        }

        /// <summary>Active known mods relevant for <paramref name="k"/>: any declared tag matches (the mod itself excluded).</summary>
        public static IEnumerable<ModNoteDef> RelevantFor(KitMod k)
        {
            var tags = DeclaredTags(k);
            if (tags.Count == 0) return Enumerable.Empty<ModNoteDef>();
            return Active().Where(n => tags.Any(n.HasTag) && (k == null || !n.packageIds.Any(p => KitDetect.Same(p, k.PackageId))));
        }

        /// <summary>
        /// A readable name for a Harmony owner id: the active mod whose packageId or noted Harmony id it is, else the id itself.
        /// </summary>
        public static string OwnerName(string harmonyId)
        {
            if (string.IsNullOrEmpty(harmonyId)) return "?";
            string norm = KitDetect.Normalize(harmonyId);
            foreach (var n in All)
                if (n.harmonyIds.Any(h => string.Equals(h, harmonyId, StringComparison.OrdinalIgnoreCase)) || n.packageIds.Any(p => KitDetect.Normalize(p) == norm))
                    return n.LabelCap.ToString();
            var name = KitDetect.ActiveName(harmonyId);
            if (!string.IsNullOrEmpty(name)) return name;
            foreach (var k in Framework.AllContexts())
                if (string.Equals(k.Info.HarmonyId, harmonyId, StringComparison.OrdinalIgnoreCase)) return k.Name;
            return harmonyId;
        }

        // internal -------------------------------------------------------------------------------------------------------
        static readonly Dictionary<KitMod, HashSet<string>> declared = new Dictionary<KitMod, HashSet<string>>();
        static Dictionary<string, ModNoteDef> index;
        static int indexCount = -1;

        static Dictionary<string, ModNoteDef> Index()
        {
            var all = All;
            if (index != null && indexCount == all.Count) return index;
            var d = new Dictionary<string, ModNoteDef>();
            foreach (var n in all)
                foreach (var p in n.packageIds ?? new List<string>())
                {
                    string key = KitDetect.Normalize(p);
                    if (key.Length > 0 && !d.ContainsKey(key)) d[key] = n;
                }
            index = d; indexCount = all.Count;
            return d;
        }
    }
}

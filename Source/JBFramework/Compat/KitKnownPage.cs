// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// The knowledge base on the Compatibility pages (through <see cref="KitHooks.CompatPage"/>) and in the diagnosis reports (through
    /// <see cref="KitHooks.ReportLines"/>): known mods relevant for the mod, their pitfalls and advice, and the Harmony co-owners of the
    /// methods the mod patches, by name. On the framework's own page: every active known mod.
    /// </summary>
    internal static class KitKnownPage
    {
        static bool installed;

        internal static void Install()
        {
            if (installed) return;
            installed = true;
            KitHooks.CompatPage.Add(Draw);
            KitHooks.ReportLines.Add(Report);
        }

        // ------------------------------------------------------------------------------------------------ Harmony co-owners
        /// <summary>One patched method and the other Harmony owners on it.</summary>
        internal struct CoOwner { public string Method; public List<string> Owners; }

        /// <summary>Methods mod <paramref name="k"/> patches that other Harmony ids patch too, with readable owner names. Never throws.</summary>
        internal static List<CoOwner> CoOwners(KitMod k)
        {
            // the page draws every frame: Harmony's patch info is read at most every 2 seconds per mod
            float now = Time.realtimeSinceStartup;
            if (k != null && coCache.TryGetValue(k, out var hit) && now - hit.At < 2f) return hit.List;
            var list = CoOwnersUncached(k);
            if (k != null) coCache[k] = (now, list);
            return list;
        }

        static readonly Dictionary<KitMod, (float At, List<CoOwner> List)> coCache = new Dictionary<KitMod, (float, List<CoOwner>)>();

        static List<CoOwner> CoOwnersUncached(KitMod k)
        {
            try { return k?.Patches?.HarmonyInstance == null ? new List<CoOwner>() : CoOwnersInner(k); }
            catch (Exception e) { return new List<CoOwner> { new CoOwner { Method = "(could not read Harmony patch info: " + e.Message + ")", Owners = new List<string>() } }; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static List<CoOwner> CoOwnersInner(KitMod k)
        {
            var h = (HarmonyLib.Harmony)k.Patches.HarmonyInstance;
            var list = new List<CoOwner>();
            foreach (var m in h.GetPatchedMethods())
            {
                var info = HarmonyLib.Harmony.GetPatchInfo(m);
                if (info == null) continue;
                var others = info.Owners.Where(o => o != h.Id).Distinct().Select(KitKnown.OwnerName).Distinct().OrderBy(x => x).ToList();
                if (others.Count > 0) list.Add(new CoOwner { Method = (m.DeclaringType?.Name ?? "?") + "." + m.Name, Owners = others });
            }
            return list.OrderBy(c => c.Method).ToList();
        }

        // ------------------------------------------------------------------------------------------------ report
        static IEnumerable<string> Report(KitMod k)
        {
            var lines = new List<string>();
            if (k.IsFramework)
            {
                var active = KitKnown.Active().ToList();
                lines.Add($"Knowledge base: {KitKnown.All.Count} notes, {active.Count} of these mods active");
                foreach (var n in active) lines.Add($"  {n.label} ({n.ActivePackageId}) [{string.Join(", ", n.tags)}]");
                return lines;
            }
            var tags = KitKnown.DeclaredTags(k);
            if (tags.Count > 0)
            {
                var rel = KitKnown.RelevantFor(k).ToList();
                lines.Add($"Known mods for tags {string.Join(", ", tags)}: {(rel.Count == 0 ? "none active" : rel.Count.ToString())}");
                foreach (var n in rel) lines.Add($"  {n.label} ({n.ActivePackageId}) [{string.Join(", ", n.tags)}]");
            }
            var co = CoOwners(k);
            if (co.Count > 0)
            {
                lines.Add("Other mods on the same methods (by name):");
                foreach (var c in co) lines.Add("  " + c.Method + ": " + string.Join(", ", c.Owners));
            }
            return lines;
        }

        // ------------------------------------------------------------------------------------------------ Compatibility page
        static float Draw(KitMod k, float w, float y)
        {
            if (k.IsFramework) return DrawFramework(k, w, y);
            var tags = KitKnown.DeclaredTags(k);
            if (tags.Count > 0)
            {
                y += 6f;
                y += KitStyle.Section(0f, y, w, KitText.K("Known.Title", "Known mods in your game"));
                var rel = KitKnown.RelevantFor(k).ToList();
                if (rel.Count == 0)
                    y += KitStyle.Paragraph(4f, y, w - 4f, KitText.K("Known.NoneActive", "None of the mods {0} knows about for this mod is active.", Framework.Name), KitStyle.DimGrey) + 6f;
                foreach (var n in rel) y += NoteBlock(n, w, y);
            }
            y += DrawCoOwners(k, w, y);
            return y;
        }

        static float DrawFramework(KitMod k, float w, float y)
        {
            y += 6f;
            var active = KitKnown.Active().ToList();
            y += KitStyle.Section(0f, y, w, KitText.K("Known.FrameworkTitle", "Known mods in your game ({0} of {1} in the knowledge base)", active.Count, KitKnown.All.Count));
            y += KitStyle.Paragraph(4f, y, w - 4f, KitText.K("Known.FrameworkIntro", "What {0} knows about other mods. Every JB mod reads this list; updating {0} updates the knowledge for all of them.", Framework.Name), KitStyle.Grey) + 6f;
            if (active.Count == 0) y += KitStyle.Paragraph(4f, y, w - 4f, KitText.K("Known.NoneAtAll", "None of the known mods is active."), KitStyle.DimGrey) + 6f;
            foreach (var n in active) y += NoteBlock(n, w, y);
            y += DrawCoOwners(k, w, y);
            return y;
        }

        static float DrawCoOwners(KitMod k, float w, float y0)
        {
            var co = CoOwners(k);
            if (co.Count == 0) return 0f;
            float y = y0 + 6f;
            y += KitStyle.Section(0f, y, w, KitText.K("Known.CoOwners", "Other mods changing the same game code"));
            y += KitStyle.Paragraph(4f, y, w - 4f, KitText.K("Known.CoOwnersIntro", "Usually harmless. When something misbehaves, these are the first mods to try without."), KitStyle.DimGrey, GameFont.Tiny) + 4f;
            foreach (var c in co)
                y += KitStyle.Paragraph(14f, y, w - 14f, c.Method + ":  " + string.Join(", ", c.Owners), KitStyle.Grey, GameFont.Tiny) + 2f;
            return y - y0;
        }

        static float NoteBlock(ModNoteDef n, float w, float y0)
        {
            float y = y0;
            string title = n.LabelCap + "  ·  " + string.Join(", ", n.tags);
            KitStyle.LabelColored(new Rect(4f, y, w - 4f, 24f), title, Color.white, TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(new Rect(4f, y, w - 4f, 24f), (n.ActivePackageId ?? "") + (n.versions.Count > 0 ? "\nRimWorld " + string.Join(", ", n.versions) : ""));
            y += 24f;
            if (!string.IsNullOrEmpty(n.description)) y += KitStyle.Paragraph(14f, y, w - 14f, n.description, KitStyle.Grey, GameFont.Tiny) + 2f;
            if (!string.IsNullOrEmpty(n.pitfall)) y += KitStyle.Paragraph(14f, y, w - 14f, KitText.K("Known.Pitfall", "Watch out: {0}", n.pitfall), KitStyle.Accent, GameFont.Tiny) + 2f;
            if (!string.IsNullOrEmpty(n.advice)) y += KitStyle.Paragraph(14f, y, w - 14f, KitText.K("Known.Advice", "Advice: {0}", n.advice), KitStyle.Good, GameFont.Tiny) + 2f;
            return y - y0 + 6f;
        }
    }
}

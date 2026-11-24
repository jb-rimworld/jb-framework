// SPDX-License-Identifier: MIT
// Rewritten small from Hearth & Harbor Foundation/HHIcons.cs: paths come from KitInfo.Theme, KitSection.Icon and KitPage.Icon.
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace JBKit
{
    /// <summary>
    /// Icons of the kit window, loaded lazily on the main thread and cached. A missing texture is null and draws nothing (never a pink
    /// square). Lookup for a page id: <c>Theme.Icons[pageId]</c>, then the section's / custom page's own Icon, then a vanilla fallback
    /// (About, Diagnosis only). Paths are the mod's own, under <c>UI/&lt;Prefix&gt;/Kit/…</c> by convention.
    /// </summary>
    internal static class KitIcons
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// <summary>Texture at a path, or null (missing, empty path, or not on the main thread yet).</summary>
        public static Texture2D Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (cache.TryGetValue(path, out var t)) return t;
            try { t = ContentFinder<Texture2D>.Get(path, false); }
            catch (Exception) { t = null; }
            cache[path] = t;
            return t;
        }

        /// <summary>The mod mark (Theme.MarkTex), or null.</summary>
        public static Texture2D Mark => Get(Kit.Info?.Theme?.MarkTex);

        /// <summary>Icon of a settings page ("overview", "general", "section:&lt;id&gt;", "page:&lt;id&gt;", "compat", "diagnosis", "about").</summary>
        public static Texture2D Page(string pageId)
        {
            if (pageId == null) return null;
            var icons = Kit.Info?.Theme?.Icons;
            if (icons != null && icons.TryGetValue(pageId, out var p) && Get(p) is Texture2D themed) return themed;
            if (pageId.StartsWith("section:")) return Get(KitSections.Find(pageId.Substring(8))?.Icon);
            if (pageId.StartsWith("page:")) return Get(KitPages.Find(pageId.Substring(5))?.Icon);
            switch (pageId)
            {
                case "about": return Safe(() => TexButton.Info);
                case "diagnosis": return Safe(() => TexButton.ToggleLog);
                default: return null;
            }
        }

        /// <summary>Icon of a section (theme entry "section:&lt;id&gt;" or the section's Icon), or null.</summary>
        public static Texture2D Section(string id) => Page("section:" + id);

        static Texture2D Safe(Func<Texture2D> f) { try { return f(); } catch (Exception) { return null; } }

        /// <summary>Draws an icon fitted into <paramref name="r"/>, dimmed when <paramref name="dim"/>. Nothing when the texture is missing.</summary>
        public static void Draw(Rect r, Texture2D tex, bool dim = false)
        {
            if (tex == null) return;
            var old = GUI.color;
            if (dim) GUI.color = new Color(old.r, old.g, old.b, old.a * 0.45f);
            Widgets.DrawTextureFitted(r, tex, 1f);
            GUI.color = old;
        }

        /// <summary>Tests: every texture path the theme, sections and pages name (each should resolve).</summary>
        public static IEnumerable<string> AllPaths()
        {
            var th = Kit.Info?.Theme;
            if (!string.IsNullOrEmpty(th?.MarkTex)) yield return th.MarkTex;
            if (th?.Icons != null) foreach (var kv in th.Icons) if (!string.IsNullOrEmpty(kv.Value)) yield return kv.Value;
            foreach (var s in KitSections.All) if (!string.IsNullOrEmpty(s.Icon)) yield return s.Icon;
            foreach (var p in KitPages.All) if (!string.IsNullOrEmpty(p.Icon)) yield return p.Icon;
        }
    }
}

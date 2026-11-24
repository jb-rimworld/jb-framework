// SPDX-License-Identifier: MIT
// Rewritten small from Hearth & Harbor Foundation/HHIcons.cs: paths come from KitInfo.Theme, KitSection.Icon and KitPage.Icon of each mod.
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// Icons of the settings windows, loaded lazily on the main thread and cached (shared cache: paths are global in RimWorld). A missing
    /// texture is null and draws nothing (never a pink square). Lookup for a page id: the mod's <c>Theme.Icons[pageId]</c>, then the
    /// section's / custom page's own Icon, then the framework's icon (Common/Textures/UI/JBFramework), then a vanilla fallback.
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

        /// <summary>The mod's mark (Theme.MarkTex), or null.</summary>
        public static Texture2D Mark(KitMod k) => Get(k?.Info?.Theme?.MarkTex);

        /// <summary>Framework icon of a standard page (Common/Textures/UI/JBFramework/&lt;Name&gt;), or null.</summary>
        static Texture2D Own(string name) => Get("UI/JBFramework/" + name);

        /// <summary>Icon of a settings page ("overview", "general", "section:&lt;id&gt;", "page:&lt;id&gt;", "compat", "diagnosis", "about").</summary>
        public static Texture2D Page(KitMod k, string pageId)
        {
            if (pageId == null || k == null) return null;
            var icons = k.Info?.Theme?.Icons;
            if (icons != null && icons.TryGetValue(pageId, out var p) && Get(p) is Texture2D themed) return themed;
            if (pageId.StartsWith("section:")) return Get(k.Sections.Find(pageId.Substring(8))?.Icon);
            if (pageId.StartsWith("page:")) return Get(k.Pages.Find(pageId.Substring(5))?.Icon);
            switch (pageId)
            {
                case "overview": return Own("Overview");
                case "general": return Own("General");
                case "compat": return Own("Compat");
                case "about": return Own("About") ?? Safe(() => TexButton.Info);
                case "diagnosis": return Own("Diagnosis") ?? Safe(() => TexButton.ToggleLog);
                default: return null;
            }
        }

        /// <summary>Icon of a section (theme entry "section:&lt;id&gt;" or the section's Icon), or null.</summary>
        public static Texture2D Section(KitMod k, string id) => Page(k, "section:" + id);

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
        public static IEnumerable<string> AllPaths(KitMod k)
        {
            var th = k?.Info?.Theme;
            if (!string.IsNullOrEmpty(th?.MarkTex)) yield return th.MarkTex;
            if (th?.Icons != null) foreach (var kv in th.Icons) if (!string.IsNullOrEmpty(kv.Value)) yield return kv.Value;
            if (k == null) yield break;
            foreach (var s in k.Sections.All) if (!string.IsNullOrEmpty(s.Icon)) yield return s.Icon;
            foreach (var p in k.Pages.All) if (!string.IsNullOrEmpty(p.Icon)) yield return p.Icon;
        }
    }
}

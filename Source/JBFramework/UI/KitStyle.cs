// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Hearth/UI/UiCore.cs (UiStyle, UiProbe). Amber → the mod's accent colour (KitInfo.Theme).
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace JBFramework
{
    /// <summary>Counts how often each kit screen was drawn (tests use it to prove that real OnGUI frames reached the code).</summary>
    internal static class KitUiProbe
    {
        static readonly Dictionary<string, int> hits = new Dictionary<string, int>();
        public static void Hit(string name) { hits.TryGetValue(name, out int n); hits[name] = n + 1; }
        public static int Count(string name) => hits.TryGetValue(name, out int n) ? n : 0;
        public static void Clear() => hits.Clear();
    }

    /// <summary>
    /// Colours and small drawing helpers of the JB settings windows, so a mod's own windows can look the same. The accent colour is
    /// only an accent (Hearth &amp; Harbor style rule). Inside a JB settings window <see cref="Accent"/> is the drawing mod's accent.
    /// </summary>
    public static class KitStyle
    {
        static readonly Color DefaultAccent = new Color(0.95f, 0.72f, 0.30f);
        /// <summary>
        /// The accent of the mod drawing right now (its settings window, its welcome card, or a <see cref="Using"/> scope), else Hearth
        /// &amp; Harbor amber.
        /// </summary>
        public static Color Accent => KitMod.Drawing?.Cfg?.Theme?.Accent ?? DefaultAccent;

        /// <summary>A mod's accent colour (<see cref="KitTheme.Accent"/>), Hearth &amp; Harbor amber for null.</summary>
        public static Color AccentOf(KitMod k) => k?.Cfg?.Theme?.Accent ?? DefaultAccent;

        /// <summary>
        /// Draws as mod <paramref name="k"/> until disposed: <see cref="Accent"/>, <see cref="Section"/>, <see cref="Chip(Rect, string, bool)"/> and
        /// <see cref="Banner"/> use its accent in the mod's own windows. <c>using (KitStyle.Using(Kit)) { … }</c> inside DoWindowContents.
        /// </summary>
        public static IDisposable Using(KitMod k) => new DrawingScope(k);

        sealed class DrawingScope : IDisposable
        {
            readonly KitMod prev; bool done;
            public DrawingScope(KitMod k) { prev = KitMod.Drawing; KitMod.Drawing = k; }
            public void Dispose() { if (done) return; done = true; KitMod.Drawing = prev; }
        }
        /// <summary>The accent at low alpha: backgrounds of selected rows, chips and banners.</summary>
        public static Color AccentSoft { get { var a = Accent; return new Color(a.r, a.g, a.b, 0.18f); } }
        /// <summary>Secondary text.</summary>
        public static readonly Color Grey = new Color(0.72f, 0.72f, 0.72f);
        /// <summary>Descriptions and disabled text.</summary>
        public static readonly Color DimGrey = new Color(0.55f, 0.55f, 0.55f);
        /// <summary>"Works" / "on".</summary>
        public static readonly Color Good = new Color(0.55f, 0.78f, 0.45f);
        /// <summary>"Failed" / "paused".</summary>
        public static readonly Color Bad = new Color(0.88f, 0.42f, 0.38f);
        /// <summary>Thin separators and box outlines.</summary>
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.15f);
        internal const float Pad = 10f, RowH = 28f, Gap = 6f;

        static readonly Dictionary<int, Texture2D> solids = new Dictionary<int, Texture2D>();
        internal static Texture2D Solid(Color c)
        {
            int k = ((int)(c.r * 255) << 24) ^ ((int)(c.g * 255) << 16) ^ ((int)(c.b * 255) << 8) ^ (int)(c.a * 255);
            if (!solids.TryGetValue(k, out var t)) solids[k] = t = SolidColorMaterials.NewSolidColorTexture(c);
            return t;
        }

        /// <summary>A heading in the medium font.</summary>
        public static void Heading(Rect r, string text)
        {
            Text.Font = GameFont.Medium; Widgets.Label(r, text); Text.Font = GameFont.Small;
        }

        /// <summary>Section header in the accent colour with a thin rule beneath. Returns the height used.</summary>
        public static float Section(float x, float y, float w, string text)
        {
            Text.Font = GameFont.Small; GUI.color = Accent;
            Widgets.Label(new Rect(x, y, w, 24f), (text ?? "").ToUpperInvariant());
            GUI.color = Line; Widgets.DrawLineHorizontal(x, y + 24f, w); GUI.color = Color.white;
            return 30f;
        }

        /// <summary>A label in a colour, anchor and font; restores the GUI state afterwards.</summary>
        public static void LabelColored(Rect r, string text, Color c) => LabelColored(r, text, c, TextAnchor.UpperLeft, GameFont.Small);

        /// <summary>A label in a colour and anchor (small font); restores the GUI state afterwards.</summary>
        public static void LabelColored(Rect r, string text, Color c, TextAnchor anchor) => LabelColored(r, text, c, anchor, GameFont.Small);

        /// <summary>A label in a colour, anchor and font; restores the GUI state afterwards.</summary>
        public static void LabelColored(Rect r, string text, Color c, TextAnchor anchor, GameFont font)
        {
            var f = Text.Font; var a = Text.Anchor; Text.Font = font; Text.Anchor = anchor; GUI.color = c;
            Widgets.Label(r, text);
            GUI.color = Color.white; Text.Anchor = a; Text.Font = f;
        }

        /// <summary>Wrapped label at (x,y) with width w; returns its height.</summary>
        public static float Paragraph(float x, float y, float w, string text, Color c) => Paragraph(x, y, w, text, c, GameFont.Small);

        /// <summary>Wrapped label in a font at (x,y) with width w; returns its height.</summary>
        public static float Paragraph(float x, float y, float w, string text, Color c, GameFont font)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            float h = CachedHeight(text, w, font);
            LabelColored(new Rect(x, y, w, h), text, c, TextAnchor.UpperLeft, font);
            return h;
        }

        /// <summary>A progress bar filled to <paramref name="fill01"/> in <paramref name="c"/>.</summary>
        public static void Bar(Rect r, float fill01, Color c)
        {
            Widgets.DrawBoxSolid(r, new Color(0.12f, 0.12f, 0.12f, 0.85f));
            var f = new Rect(r.x + 1, r.y + 1, Mathf.Max(0f, (r.width - 2f) * Mathf.Clamp01(fill01)), r.height - 2f);
            Widgets.DrawBoxSolid(f, c);
            GUI.color = Line; Widgets.DrawBox(r); GUI.color = Color.white;
        }

        /// <summary>Red → amber → green for 0..1.</summary>
        internal static Color Heat(float t01)
        {
            t01 = Mathf.Clamp01(t01);
            return t01 < 0.5f ? Color.Lerp(new Color(0.82f, 0.30f, 0.27f), new Color(0.88f, 0.70f, 0.28f), t01 * 2f)
                              : Color.Lerp(new Color(0.88f, 0.70f, 0.28f), new Color(0.45f, 0.75f, 0.40f), (t01 - 0.5f) * 2f);
        }

        /// <summary>A toggle-style button; selected ones get the accent highlight. Returns true when clicked.</summary>
        public static bool Chip(Rect r, string label, bool selected) => Chip(r, label, selected, null, true);

        /// <summary>A chip with a tooltip.</summary>
        public static bool Chip(Rect r, string label, bool selected, string tip) => Chip(r, label, selected, tip, true);

        /// <summary>A chip with a tooltip that can be disabled (greyed, not clickable).</summary>
        public static bool Chip(Rect r, string label, bool selected, string tip, bool enabled)
        {
            if (selected) { Widgets.DrawBoxSolid(r, AccentSoft); GUI.color = Accent; Widgets.DrawBox(r); GUI.color = Color.white; }
            else if (Mouse.IsOver(r) && enabled) Widgets.DrawHighlight(r);
            else { GUI.color = Line; Widgets.DrawBox(r); GUI.color = Color.white; }
            var a = Text.Anchor; var fnt = Text.Font; Text.Anchor = TextAnchor.MiddleCenter;
            if ((label ?? "").Length > 13 && r.width < 150f) Text.Font = GameFont.Tiny;
            if (!enabled) GUI.color = DimGrey;
            Widgets.Label(r, label);
            GUI.color = Color.white; Text.Anchor = a; Text.Font = fnt;
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
            return enabled && Widgets.ButtonInvisible(r);
        }

        /// <summary>A coloured band with a message and an optional button on the right. Returns true when the button was clicked.</summary>
        public static bool Banner(float x, float y, float w, string text, Color color, string button, out float height)
        {
            float bw = string.IsNullOrEmpty(button) ? 0f : Mathf.Clamp(KitUI.TextW(button) + 36f, 120f, 240f);
            float th = CachedHeight(text, w - bw - 30f, GameFont.Small);
            height = Mathf.Max(38f, th + 10f);
            var b = new Rect(x, y, w, height);
            Widgets.DrawBoxSolid(b, new Color(color.r, color.g, color.b, 0.18f));
            LabelColored(new Rect(b.x + 10f, b.y, b.width - bw - 30f, b.height), text, color, TextAnchor.MiddleLeft);
            return bw > 0f && Widgets.ButtonText(new Rect(b.xMax - bw - 10f, b.y + (height - 30f) / 2f, bw, 30f), button);
        }

        static readonly Dictionary<string, float> heightCache = new Dictionary<string, float>();
        /// <summary>Text height with a cache keyed by text + width + font (CalcHeight is slow and rows draw every frame).</summary>
        internal static float CachedHeight(string text, float width, GameFont font)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            string k = ((int)font) + "|" + (int)width + "|" + text;
            if (heightCache.TryGetValue(k, out float h)) return h;
            var f = Text.Font; Text.Font = font; h = Text.CalcHeight(text, width); Text.Font = f;
            if (heightCache.Count > 4000) heightCache.Clear();
            return heightCache[k] = h;
        }
    }
}

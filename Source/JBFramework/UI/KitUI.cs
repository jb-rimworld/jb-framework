// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Foundation/HUI.cs (one global "Window size" preference for every JB mod, design size of the settings window 1000x720).
using System;
using UnityEngine;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// Window sizing for every window of a mod. Windows declare the size they were designed for at a 900 px tall screen;
    /// <see cref="Size(float, float)"/> scales it up for larger screens and by the player's global "Window size" preference (Mod settings,
    /// JB Framework), and never lets it leave the screen. Usage: <c>public override Vector2 InitialSize =&gt; KitUI.Size(900f, 640f);</c>
    /// </summary>
    public static class KitUI
    {
        /// <summary>Design size of the mod settings window (the vanilla one is 900x700).</summary>
        internal const float SettingsW = 1000f, SettingsH = 720f;
        /// <summary>Smallest settings window (also on small screens, as far as the screen allows).</summary>
        internal const float SettingsMinW = 860f, SettingsMinH = 560f;

        /// <summary>
        /// A rect of the current GUI group (window, scroll view) in screen coordinates (UI units), for the click tests: the offset is the
        /// mouse in screen units minus the mouse in group units, exact inside nested groups. Never throws.
        /// </summary>
        internal static Rect ToScreen(Rect r)
        {
            try
            {
                var off = UI.MousePositionOnUIInverted - Event.current.mousePosition;
                return new Rect(r.x + off.x, r.y + off.y, r.width, r.height);
            }
            catch (Exception) { return Rect.zero; }
        }

        /// <summary>Tests only: pretend the screen has this size (Size and the clamps read it). Null = the real screen.</summary>
        internal static Vector2? TestScreen;
        internal static float ScreenW => TestScreen.HasValue ? TestScreen.Value.x : UI.screenWidth;
        internal static float ScreenH => TestScreen.HasValue ? TestScreen.Value.y : UI.screenHeight;

        /// <summary>Automatic factor for the current screen (1.0 at 900 px height, up to 1.35) times the player's "Window size" preference.</summary>
        public static float Scale
        {
            get
            {
                float auto = Mathf.Clamp(ScreenH / 900f, 1f, 1.35f);
                float user = 1f;
                try { user = Framework.WindowScale; }
                catch (Exception) { /* settings not ready (very early UI): default */ }
                if (user <= 0f || float.IsNaN(user)) user = 1f;
                return auto * user;
            }
        }

        /// <summary>
        /// Scaled window size for a window designed at <paramref name="w"/> × <paramref name="h"/>, clamped to 92 % of the screen.
        /// </summary>
        public static Vector2 Size(float w, float h) => Size(w, h, 0f, 0f, 0.92f, 0.92f);

        /// <summary>Like <see cref="Size(float, float)"/> with a minimum size and the largest share of the screen (0.92 by default).</summary>
        public static Vector2 Size(float w, float h, float minW, float minH, float maxFracW, float maxFracH)
        {
            float s = Scale;
            float maxW = ScreenW * maxFracW, maxH = ScreenH * maxFracH - 35f;
            float rw = Mathf.Min(w * s, maxW), rh = Mathf.Min(h * s, maxH);
            if (minW > 0f) rw = Mathf.Max(rw, Mathf.Min(minW, ScreenW - 20f));
            if (minH > 0f) rh = Mathf.Max(rh, Mathf.Min(minH, ScreenH - 55f));
            return new Vector2(rw, rh);
        }

        /// <summary>Size of this mod's settings window (Dialog_ModSettings, patched by KitWindowPatches).</summary>
        internal static Vector2 SettingsSize() => Size(SettingsW, SettingsH, SettingsMinW, SettingsMinH, 0.95f, 0.92f);

        /// <summary>Splits a rect horizontally by fractions (<c>KitUI.Cols(r, 8f, 0.3f, 0.7f)</c>); <paramref name="gap"/> px sit between columns.</summary>
        public static Rect[] Cols(Rect r, float gap, params float[] fractions)
        {
            var res = new Rect[fractions.Length];
            float total = 0f; foreach (var f in fractions) total += f;
            float usable = r.width - gap * (fractions.Length - 1), x = r.x;
            for (int i = 0; i < fractions.Length; i++)
            {
                float w = usable * fractions[i] / total;
                res[i] = new Rect(x, r.y, w, r.height);
                x += w + gap;
            }
            return res;
        }

        /// <summary>Height of a wrapped text in the small font at <paramref name="width"/> (cached; 0 for empty text). Lay rows out with it: no clipping in German.</summary>
        public static float TextH(string text, float width) => TextH(text, width, GameFont.Small);

        /// <summary>Height of a wrapped text at the given font and width (cached; 0 for empty text).</summary>
        public static float TextH(string text, float width, GameFont font)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            return KitStyle.CachedHeight(text, Mathf.Max(20f, width), font);
        }

        /// <summary>Width of one line of text in the small font (size buttons to their translated label).</summary>
        public static float TextW(string text) => TextW(text, GameFont.Small);

        /// <summary>Width of one line of text at the given font.</summary>
        public static float TextW(string text, GameFont font)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            var f = Text.Font; Text.Font = font;
            float w = Text.CalcSize(text).x;
            Text.Font = f;
            return w;
        }

        /// <summary>True when a window of the given size, centered on the (test) screen, lies completely inside it.</summary>
        internal static bool FitsScreen(Vector2 size) => size.x <= ScreenW + 0.5f && size.y <= ScreenH + 0.5f;
    }
}

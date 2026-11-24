// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Foundation/HUI.cs (scale key kit.windowScale, default factor 1.0, design size of the settings window 1000x720).
using System;
using UnityEngine;
using Verse;

namespace JBKit
{
    /// <summary>
    /// Window sizing for every window of the mod. Windows declare the size they were designed for at a 900 px tall screen;
    /// <see cref="Size"/> scales it up for larger screens and by the player's "Window size" setting (kit.windowScale), and never lets
    /// it leave the screen. Usage: <c>public override Vector2 InitialSize =&gt; KitUI.Size(900f, 640f);</c>
    /// </summary>
    internal static class KitUI
    {
        /// <summary>Design size of the mod settings window (the vanilla one is 900x700).</summary>
        public const float SettingsW = 1000f, SettingsH = 720f;
        /// <summary>Smallest settings window (also on small screens, as far as the screen allows).</summary>
        public const float SettingsMinW = 860f, SettingsMinH = 560f;

        /// <summary>Tests only: pretend the screen has this size (Size and the clamps read it). Null = the real screen.</summary>
        public static Vector2? TestScreen;
        public static float ScreenW => TestScreen.HasValue ? TestScreen.Value.x : UI.screenWidth;
        public static float ScreenH => TestScreen.HasValue ? TestScreen.Value.y : UI.screenHeight;

        /// <summary>Automatic factor for the current screen (1.0 at 900 px height, up to 1.35) times the player setting.</summary>
        public static float Scale
        {
            get
            {
                float auto = Mathf.Clamp(ScreenH / 900f, 1f, 1.35f);
                float user = 1f;
                try { if (KitSettings.Find(KitBuiltins.WindowScaleKey) != null) user = KitSettings.Get(KitBuiltins.WindowScaleKey); }
                catch (Exception) { /* settings not ready (very early UI): default */ }
                if (user <= 0f || float.IsNaN(user)) user = 1f;
                return auto * user;
            }
        }

        /// <summary>Scaled window size, clamped to a fraction of the screen and never below the given minimum.</summary>
        public static Vector2 Size(float w, float h, float minW = 0f, float minH = 0f, float maxFracW = 0.92f, float maxFracH = 0.92f)
        {
            float s = Scale;
            float maxW = ScreenW * maxFracW, maxH = ScreenH * maxFracH - 35f;
            float rw = Mathf.Min(w * s, maxW), rh = Mathf.Min(h * s, maxH);
            if (minW > 0f) rw = Mathf.Max(rw, Mathf.Min(minW, ScreenW - 20f));
            if (minH > 0f) rh = Mathf.Max(rh, Mathf.Min(minH, ScreenH - 55f));
            return new Vector2(rw, rh);
        }

        /// <summary>Size of this mod's settings window (Dialog_ModSettings, patched by KitWindowPatches).</summary>
        public static Vector2 SettingsSize() => Size(SettingsW, SettingsH, SettingsMinW, SettingsMinH, 0.95f, 0.92f);

        /// <summary>Splits a rect horizontally by fractions (e.g. 0.3f, 0.7f); a gap sits between columns.</summary>
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

        /// <summary>Height of a wrapped text at the given font and width (cached; 0 for empty text).</summary>
        public static float TextH(string text, float width, GameFont font = GameFont.Small)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            return KitStyle.CachedHeight(text, Mathf.Max(20f, width), font);
        }

        /// <summary>Width of one line of text at the given font.</summary>
        public static float TextW(string text, GameFont font = GameFont.Small)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            var f = Text.Font; Text.Font = font;
            float w = Text.CalcSize(text).x;
            Text.Font = f;
            return w;
        }

        /// <summary>True when a window of the given size, centered on the (test) screen, lies completely inside it.</summary>
        public static bool FitsScreen(Vector2 size) => size.x <= ScreenW + 0.5f && size.y <= ScreenH + 0.5f;
    }
}

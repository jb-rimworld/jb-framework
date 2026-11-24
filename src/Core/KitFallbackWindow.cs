// SPDX-License-Identifier: MIT
using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Verse;

namespace JBKit
{
    /// <summary>
    /// Minimal settings window for tier "core" (src/UI not compiled in): section switches and every non-nerd setting as a plain row,
    /// against <see cref="Kit.Current"/>. Global settings edit the template. Replaced by src/UI's window through <see cref="KitHooks.DrawSettings"/>.
    /// </summary>
    internal static class KitFallbackWindow
    {
        static Vector2 scroll;
        static float viewH = 1000f;

        public static void Draw(Rect inRect)
        {
            var target = Kit.Current;
            var view = new Rect(0f, 0f, inRect.width - 20f, Mathf.Max(viewH, inRect.height));
            Widgets.BeginScrollView(inRect, ref scroll, view);
            var l = new Listing_Standard();
            l.Begin(view);
            if (Kit.HasSaveProfile) l.Label(KitText.K("Fallback.ThisSave", "These values belong to this save. New colonies start from the template in the settings file."));
            if (KitSections.All.Count > 0)
            {
                l.Label("<b>" + KitText.K("Fallback.Sections", "Sections") + "</b>");
                foreach (var s in KitSections.All)
                {
                    bool avail = KitSections.Available(s.Id);
                    bool on = target.SectionOn(s.Id), nv = on;
                    string label = s.Label + (avail ? "" : " (" + (s.RequiresLabel ?? KitText.K("Section.NotAvailable", "Not available in this game.")) + ")");
                    l.CheckboxLabeled(label, ref nv, s.Desc);
                    if (nv != on && avail) target.SetSection(s.Id, nv);
                }
                l.GapLine();
            }
            bool nerd = Kit.GetBool(KitBuiltins.ShowNerdKey);
            string lastSection = null, lastCat = null;
            foreach (var s in KitSettings.All.OrderBy(x => x.Section == KitSections.Core ? 1 : 0).ThenBy(x => x.Order))
            {
                if (s.Nerd && !nerd) continue;
                if (s.Section != lastSection) { l.Label("<b>" + KitSections.LabelOf(s.Section) + "</b>"); lastSection = s.Section; lastCat = null; }
                if (!string.IsNullOrEmpty(s.Category) && s.Category != lastCat) { l.Label(s.CategoryLabel); lastCat = s.Category; }
                DrawRow(l, s, target);
            }
            viewH = l.CurHeight + 20f;
            l.End();
            Widgets.EndScrollView();
        }

        static void DrawRow(Listing_Standard l, KitSetting s, KitProfile target)
        {
            float v = KitSettings.Value(s, target);
            string reason = s.RequiresReason;
            string tip = s.Desc + (reason != null ? "\n" + reason : "");
            if (s.Kind == KitSettingKind.Bool)
            {
                bool b = v >= 0.5f, nb = b;
                l.CheckboxLabeled(s.Label, ref nb, tip);
                if (nb != b && reason == null) KitSettings.Set(s, target, nb ? 1f : 0f);
                return;
            }
            var row = l.GetRect(Text.LineHeight);
            TooltipHandler.TipRegion(row, tip);
            Widgets.Label(row.LeftHalf(), s.Label);
            Widgets.Label(row.RightHalf(), s.Format(v));
            var sr = l.GetRect(22f);
            float before = s.ToSlider(v);
            float pos = Widgets.HorizontalSlider(sr, before, s.SliderLo, s.SliderHi, false, null, null, null, -1f);
            if (reason == null && Math.Abs(pos - before) > 1e-5f)
            {
                float nv = s.FromSlider(pos);
                if (s.Kind == KitSettingKind.Int || s.Kind == KitSettingKind.Enum || s.Kind == KitSettingKind.Silver) nv = Mathf.Round(nv);
                KitSettings.Set(s, target, nv);
            }
            l.Gap(4f);
        }
    }
}

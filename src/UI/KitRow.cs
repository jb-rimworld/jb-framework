// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Hearth/UI/SettingRowUI.cs. Added: restart / nerd / all-games tags, greyed rows whose Requires is not met,
// enum values as a dropdown, percent typed as percent, log-scale sliders.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Verse;

namespace JBKit
{
    /// <summary>
    /// Row drawer for one <see cref="KitSetting"/> against an explicit profile (so the window can edit the save or the template).
    /// Left: label and grey description. Right: checkbox, dropdown or slider with value, "free" entry switch, reset. A changed row has an
    /// accent edge. Heights are cached; rows outside the visible part of a scroll view are not drawn at all.
    /// </summary>
    internal static class KitRow
    {
        /// <summary>Tests set this to draw every row even outside the viewport.</summary>
        public static bool ForceDrawAll;
        static readonly Dictionary<string, string> buffers = new Dictionary<string, string>();

        public static float LabelWidth(float width) => Mathf.Clamp(width * 0.46f, 180f, 430f);

        public static float Height(KitSetting s, float width)
        {
            float lw = LabelWidth(width) - 4f;
            float h = KitStyle.CachedHeight(s.Label, lw, GameFont.Small);
            string d = s.Desc;
            if (!d.NullOrEmpty()) h += KitStyle.CachedHeight(d, lw, GameFont.Tiny);
            return Mathf.Max(40f, h + 12f);
        }

        /// <summary>Draws the row at (x,y) if it intersects <paramref name="visible"/>; always returns its height.</summary>
        public static float Draw(float x, float y, float width, KitSetting s, KitProfile target, Rect visible)
        {
            float h = Height(s, width);
            if (!ForceDrawAll && (y + h < visible.yMin || y > visible.yMax)) return h;
            KitGuard.Run(KitSections.Core, "UI.Row." + s.Key, () => DrawRow(new Rect(x, y, width, h), s, target));
            return h;
        }

        /// <summary>Resets the row's value (and its free-entry text) in <paramref name="target"/>.</summary>
        public static void Reset(KitSetting s, KitProfile target)
        {
            KitSettings.Reset(s, target);
            buffers.Remove(s.Key);
        }

        /// <summary>Forget typed text (after a preset, template copy or section reset changed values from outside).</summary>
        public static void ClearBuffers() => buffers.Clear();

        static string Tags(KitSetting s)
        {
            var t = new List<string>();
            if (s.Nerd) t.Add(KitText.K("Row.Nerd", "nerd"));
            if (s.Restart) t.Add(KitText.K("Row.Restart", "after restart"));
            if (s.Global && Kit.HasSaveProfile) t.Add(KitText.K("Row.AllGames", "all games"));
            return string.Join(" · ", t);
        }

        static void DrawRow(Rect row, KitSetting s, KitProfile target)
        {
            string reason = s.RequiresReason;
            bool enabled = reason == null;
            if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);

            string tip = s.Desc;
            if (s.Restart) tip += "\n\n" + KitText.K("Row.RestartTip", "Takes effect after restarting RimWorld.");
            if (s.Global && Kit.HasSaveProfile) tip += "\n\n" + KitText.K("Row.AllGamesTip", "A preference of this installation: the same in every save.");
            if (s.Kind != KitSettingKind.Bool && s.Kind != KitSettingKind.Enum)
                tip += "\n\n" + KitText.K("Row.Range", "Slider {0} to {1} · default {2}", s.Format(s.SliderMin), s.Format(s.SliderMax), s.Format(s.Clamp(s.Default)));
            if (s.HardLimitReason != null) tip += "\n" + KitText.K("Row.Limit", "Technical limit") + ": " + s.HardLimitReason;
            if (!enabled) tip += "\n\n" + reason;
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(row, tip.Trim());

            bool over = KitSettings.IsOverridden(s, target);
            float lw = LabelWidth(row.width);
            var lab = new Rect(row.x + 4f, row.y + 3f, lw - 4f, row.height - 4f);
            float lh = KitStyle.CachedHeight(s.Label, lab.width, GameFont.Small);
            Color labelColor = !enabled ? KitStyle.DimGrey : over ? Color.white : new Color(0.88f, 0.88f, 0.88f);
            KitStyle.LabelColored(new Rect(lab.x, lab.y, lab.width, lh), s.Label, labelColor);
            if (!s.Desc.NullOrEmpty()) KitStyle.LabelColored(new Rect(lab.x, lab.y + lh, lab.width, lab.height - lh), s.Desc, KitStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            if (over) Widgets.DrawBoxSolid(new Rect(row.x, row.y + 4f, 2f, row.height - 8f), KitStyle.Accent);
            string tags = Tags(s);
            if (tags.Length > 0) KitStyle.LabelColored(new Rect(row.xMax - 200f, row.y, 196f, 14f), tags, KitStyle.DimGrey, TextAnchor.UpperRight, GameFont.Tiny);

            float x0 = row.x + lw + 8f, x1 = row.xMax - 52f;
            float cy = row.y + row.height / 2f;
            float val = KitSettings.Value(s, target);

            if (!enabled)
            {
                KitStyle.LabelColored(new Rect(x0, cy - 11f, 110f, 22f), s.Format(val), KitStyle.DimGrey, TextAnchor.MiddleLeft);
                KitStyle.LabelColored(new Rect(x0 + 116f, row.y + 4f, x1 - x0 - 116f + 48f, row.height - 8f), reason, KitStyle.DimGrey, TextAnchor.MiddleLeft, GameFont.Tiny);
                return;
            }

            if (s.Kind == KitSettingKind.Bool)
            {
                bool b = val >= 0.5f, nb = b;
                Widgets.Checkbox(new Vector2(x0, cy - 12f), ref nb, 24f);
                if (nb != b) KitSettings.Set(s, target, nb ? 1f : 0f);
                KitStyle.LabelColored(new Rect(x0 + 30f, cy - 11f, 90f, 22f), s.Format(nb ? 1f : 0f), KitStyle.Grey, TextAnchor.MiddleLeft);
            }
            else if (s.Kind == KitSettingKind.Enum && s.EnumType != null)
            {
                var br = new Rect(x0, cy - 13f, Mathf.Min(260f, x1 - x0), 26f);
                if (Widgets.ButtonText(br, s.Format(val)))
                {
                    var opts = new List<FloatMenuOption>();
                    foreach (var ev in Enum.GetValues(s.EnumType))
                    {
                        float fv = Convert.ToInt32(ev);
                        var setting = s; var prof = target;
                        opts.Add(new FloatMenuOption(s.Format(fv), () => KitSettings.Set(setting, prof, fv)));
                    }
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
            }
            else DrawNumber(s, target, val, x0, x1, cy);

            if (over && Widgets.ButtonText(new Rect(row.xMax - 48f, cy - 11f, 46f, 22f), KitText.K("Row.Reset", "reset")))
                Reset(s, target);
        }

        static void DrawNumber(KitSetting s, KitProfile target, float val, float x0, float x1, float cy)
        {
            var store = KitSettings.StoreFor(s, target);
            bool free = store.freeText.TryGetValue(s.Key, out bool f) && f;
            bool pct = s.Kind == KitSettingKind.Percent;
            float freeW = 64f, valW = 104f;
            float sliderW = Mathf.Max(70f, x1 - x0 - valW - freeW - 8f);
            bool beyond = val < s.SliderMin - 1e-6f || val > s.SliderMax + 1e-6f;
            if (!free)
            {
                float pos = s.ToSlider(val);
                float step = s.IsLog ? -1f : s.Step > 0 ? s.Step : (s.Kind == KitSettingKind.Int || s.Kind == KitSettingKind.Silver ? 1f : pct ? 0.01f : -1f);
                float np = Widgets.HorizontalSlider(new Rect(x0, cy - 8f, sliderW, 16f), pos, s.SliderLo, s.SliderHi, false, null, null, null, step);
                if (Mathf.Abs(np - pos) > 1e-6f) KitSettings.Set(s, target, s.FromSlider(np));
                var vr = new Rect(x0 + sliderW + 8f, cy - 11f, valW, 22f);
                KitStyle.LabelColored(vr, s.Format(KitSettings.Value(s, target)), beyond ? KitStyle.Accent : Color.white, TextAnchor.MiddleLeft);
                if (beyond) TooltipHandler.TipRegion(vr, KitText.K("Row.Beyond", "Beyond the slider range. Free entry allows this."));
            }
            else
            {
                if (!buffers.TryGetValue(s.Key, out string buf)) buf = (pct ? val * 100f : val).ToString("0.#####", CultureInfo.InvariantCulture);
                string nb = Widgets.TextField(new Rect(x0, cy - 12f, sliderW, 24f), buf);
                buffers[s.Key] = nb;
                if (float.TryParse(nb.Replace(',', '.').Replace("%", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                {
                    if (pct) parsed /= 100f;
                    float c = s.Clamp(parsed);
                    if (Mathf.Abs(c - val) > 1e-7f) KitSettings.Set(s, target, c);
                    var vr = new Rect(x0 + sliderW + 8f, cy - 11f, valW + 40f, 22f);
                    if (Mathf.Abs(c - parsed) > 1e-6f)
                    {
                        KitStyle.LabelColored(vr, KitText.K("Row.Limited", "limit {0}", s.Format(c)), KitStyle.Accent, TextAnchor.MiddleLeft);
                        TooltipHandler.TipRegion(vr, s.HardLimitReason ?? KitText.K("Row.LimitGeneric", "The game cannot use this value, so the nearest possible one is used."));
                    }
                    else KitStyle.LabelColored(vr, s.Format(c), KitStyle.Grey, TextAnchor.MiddleLeft);
                }
            }
            bool nf = free;
            var fr = new Rect(x1 - freeW + 4f, cy - 12f, freeW, 24f);
            Widgets.CheckboxLabeled(fr, KitText.K("Row.Free", "Free"), ref nf, false, null, null, true);
            TooltipHandler.TipRegion(fr, KitText.K("Row.FreeTip", "Free entry: type any number up to the technical limit of the game."));
            if (nf != free) { store.freeText[s.Key] = nf; buffers.Remove(s.Key); }
        }

        /// <summary>True when the setting matches a search text (label, description, key, category, section name).</summary>
        public static bool MatchesSearch(KitSetting s, string q)
        {
            if (string.IsNullOrEmpty(q)) return true;
            q = q.ToLowerInvariant();
            bool Has(string t) => !string.IsNullOrEmpty(t) && t.ToLowerInvariant().Contains(q);
            return Has(s.Label) || Has(s.Desc) || Has(s.Key) || Has(s.Category) || Has(s.CategoryLabel) || Has(KitSections.LabelOf(s.Section))
                || (s.Section != KitSections.Core && Has(KitSections.Find(s.Section)?.LabelEn));
        }
    }
}

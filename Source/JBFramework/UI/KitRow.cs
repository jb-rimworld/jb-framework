// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Hearth/UI/SettingRowUI.cs. Added: restart / nerd / all-games tags, greyed rows whose Requires is not met,
// enum values as a dropdown, percent typed as percent, log-scale sliders.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// Row drawer for one <see cref="KitSetting"/> against an explicit profile (so the window can edit the save or the template).
    /// Left: label and grey description. Right: checkbox, dropdown or slider with value, a pencil for typed entry (on hover), reset. A changed row has an
    /// accent edge. Heights are cached; rows outside the visible part of a scroll view are not drawn at all.
    /// </summary>
    internal static class KitRow
    {
        /// <summary>Tests set this to draw every row even outside the viewport.</summary>
        public static bool ForceDrawAll;
        static readonly Dictionary<string, string> buffers = new Dictionary<string, string>();

        public static float LabelWidth(float width) => Mathf.Clamp(width * 0.46f, 180f, 430f);

        public static float Height(KitSetting s, float width) => Height(s, width, null);

        /// <summary>Row height including the preview line (text of <see cref="KitSetting.PreviewIn"/>, or null).</summary>
        static float Height(KitSetting s, float width, string preview)
        {
            float lw = LabelWidth(width) - 4f;
            float h = KitStyle.CachedHeight(s.Label, lw, GameFont.Small);
            string d = s.Desc;
            if (!d.NullOrEmpty()) h += KitStyle.CachedHeight(d, lw, GameFont.Tiny);
            h = Mathf.Max(40f, h + 12f);
            if (!string.IsNullOrEmpty(preview)) h += PreviewH(preview, width);
            return h;
        }

        static float PreviewH(string preview, float width) => KitStyle.CachedHeight(preview, Mathf.Max(60f, width - LabelWidth(width) - 12f), GameFont.Tiny) + 4f;

        /// <summary>Draws the row at (x,y) if it intersects <paramref name="visible"/>; always returns its height.</summary>
        public static float Draw(float x, float y, float width, KitSetting s, KitProfile target, Rect visible)
        {
            string preview = s.PreviewIn(target);
            float h = Height(s, width, preview);
            if (!ForceDrawAll && (y + h < visible.yMin || y > visible.yMax)) return h;
            s.Owner.Guard.Run(KitMod.Core, "UI.Row." + s.Key, () =>
            {
                float ph = string.IsNullOrEmpty(preview) ? 0f : PreviewH(preview, width);
                DrawRow(new Rect(x, y, width, h - ph), s, target);
                // preview: grey, under the control column (the label column keeps the description)
                if (ph > 0f)
                {
                    float lw = LabelWidth(width);
                    KitStyle.LabelColored(new Rect(x + lw + 8f, y + h - ph, width - lw - 12f, ph), preview, KitStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
                }
            });
            return h;
        }

        /// <summary>Resets the row's value (and its free-entry text) in <paramref name="target"/>.</summary>
        public static void Reset(KitSetting s, KitProfile target)
        {
            s.Owner.SettingsReg.Reset(s, target);
            // a focused text field would keep (and show again) its old text: let go of the focus first (1.6 hazard review, finding 3)
            if (buffers.Remove(BufKey(s))) Unfocus();
        }

        /// <summary>Width of the per-row reset button: its label in the small font plus padding, 46 to 130 px.</summary>
        internal static float ResetWidth(string label) => Mathf.Clamp(KitUI.TextW(label) + 16f, 46f, 130f);

        static string BufKey(KitSetting s) => s.Owner.PackageId + "|" + s.Key;

        /// <summary>Forget typed text (after a preset, template copy or section reset changed values from outside).</summary>
        public static void ClearBuffers() { if (buffers.Count > 0) Unfocus(); buffers.Clear(); }

        /// <summary>Drops the keyboard focus of a text field whose text is cleared from code (Unity would otherwise show it again).</summary>
        internal static void Unfocus() { try { UI.UnfocusCurrentControl(); } catch { } }

        static string Tags(KitSetting s)
        {
            var t = new List<string>();
            if (s.Nerd) t.Add(KitText.K("Row.Nerd", "nerd"));
            if (s.Restart) t.Add(KitText.K("Row.Restart", "after restart"));
            if (s.Global && s.Owner.HasSaveProfile) t.Add(KitText.K("Row.AllGames", "all games"));
            return string.Join(" · ", t);
        }

        static void DrawRow(Rect row, KitSetting s, KitProfile target)
        {
            string reason = s.RequiresReasonIn(target);
            bool enabled = reason == null;
            if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);

            string tip = s.Desc;
            if (s.Restart) tip += "\n\n" + KitText.K("Row.RestartTip", "Takes effect after restarting RimWorld.");
            if (s.Global && s.Owner.HasSaveProfile) tip += "\n\n" + KitText.K("Row.AllGamesTip", "A preference of this installation: the same in every save.");
            if (s.Kind != KitSettingKind.Bool && s.Kind != KitSettingKind.Enum)
                tip += "\n\n" + KitText.K("Row.Range", "Slider {0} to {1} · default {2}", s.Format(s.SliderMin), s.Format(s.SliderMax), s.Format(s.Clamp(s.Default)));
            if (s.HardLimitReason != null) tip += "\n" + KitText.K("Row.Limit", "Technical limit") + ": " + s.HardLimitReason;
            if (!enabled) tip += "\n\n" + reason;
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(row, tip.Trim());

            var reg = s.Owner.SettingsReg;
            bool over = reg.IsOverridden(s, target);
            float lw = LabelWidth(row.width);
            var lab = new Rect(row.x + 4f, row.y + 3f, lw - 4f, row.height - 4f);
            float lh = KitStyle.CachedHeight(s.Label, lab.width, GameFont.Small);
            Color labelColor = !enabled ? KitStyle.DimGrey : over ? Color.white : new Color(0.88f, 0.88f, 0.88f);
            KitStyle.LabelColored(new Rect(lab.x, lab.y, lab.width, lh), s.Label, labelColor);
            if (!s.Desc.NullOrEmpty()) KitStyle.LabelColored(new Rect(lab.x, lab.y + lh, lab.width, lab.height - lh), s.Desc, KitStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            if (over) Widgets.DrawBoxSolid(new Rect(row.x, row.y + 4f, 2f, row.height - 8f), KitStyle.Accent);
            string tags = Tags(s);
            if (tags.Length > 0)
            {
                // small grey tag right after the label's first line (it used to sit clipped in the row's top-right corner)
                float tagW = KitUI.TextW(tags, GameFont.Tiny) + 4f;
                float tx = Mathf.Min(lab.x + Mathf.Min(KitUI.TextW(s.Label), lab.width) + 8f, lab.xMax - tagW);
                KitStyle.LabelColored(new Rect(tx, lab.y + 2f, tagW, 20f), tags, KitStyle.DimGrey, TextAnchor.MiddleLeft, GameFont.Tiny);
            }

            // the reset button is as wide as its (translated) label: German "zurücksetzen" no longer clips
            string resetLabel = KitText.K("Row.Reset", "reset");
            float resetW = ResetWidth(resetLabel);
            float x0 = row.x + lw + 8f, x1 = row.xMax - resetW - 6f;
            float cy = row.y + row.height / 2f;
            float val = reg.Value(s, target);

            if (!enabled)
            {
                KitStyle.LabelColored(new Rect(x0, cy - 11f, 110f, 22f), s.Format(val), KitStyle.DimGrey, TextAnchor.MiddleLeft);
                KitStyle.LabelColored(new Rect(x0 + 116f, row.y + 4f, x1 - x0 - 116f + resetW, row.height - 8f), reason, KitStyle.DimGrey, TextAnchor.MiddleLeft, GameFont.Tiny);
                return;
            }

            if (s.Kind == KitSettingKind.Bool)
            {
                bool b = val >= 0.5f, nb = b;
                Widgets.Checkbox(new Vector2(x0, cy - 12f), ref nb, 24f);
                if (nb != b) reg.Set(s, target, nb ? 1f : 0f);
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
                        opts.Add(new FloatMenuOption(s.Format(fv), () => setting.Owner.SettingsReg.Set(setting, prof, fv)));
                    }
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
            }
            else DrawNumber(s, target, val, row, x0, x1, cy);

            if (over && Widgets.ButtonText(new Rect(row.xMax - resetW - 2f, cy - 11f, resetW, 22f), resetLabel))
                Reset(s, target);
        }

        /// <summary>
        /// A number row. Drawing never writes: the stored value changes only when the player drags the slider or edits the typed text,
        /// so a value the slider cannot show exactly (0.67 %, 1/3, a value beyond the range from an old settings file) is shown rounded
        /// but stored untouched. A small pencil (shown while the row is hovered, or while typing is on) switches the row between the
        /// slider and typed entry up to the technical limit; the choice is kept per row.
        /// </summary>
        static void DrawNumber(KitSetting s, KitProfile target, float val, Rect row, float x0, float x1, float cy)
        {
            var reg = s.Owner.SettingsReg;
            var store = reg.StoreFor(s, target);
            string bk = BufKey(s);
            bool free = store.freeText.TryGetValue(s.Key, out bool f) && f;
            bool pct = s.Kind == KitSettingKind.Percent;
            const float penW = 24f;
            float valW = 104f;
            float sliderW = Mathf.Max(70f, x1 - x0 - valW - penW - 10f);
            bool beyond = val < s.SliderMin - 1e-6f || val > s.SliderMax + 1e-6f;
            if (!free)
            {
                float pos = s.ToSlider(val);
                // no roundTo: vanilla rounds the returned value even when nobody drags, which rewrote values like 0.67 % to 1 %
                float np = Widgets.HorizontalSlider(new Rect(x0, cy - 8f, sliderW, 16f), pos, s.SliderLo, s.SliderHi, false, null, null, null, -1f);
                if (np != pos)
                {
                    // only a drag moves the handle: round to the step now, and store only a real change
                    float nv = RoundToStep(s, s.FromSlider(np));
                    if (Math.Abs(s.Clamp(nv) - val) > 1e-7f) reg.Set(s, target, nv);
                }
                var vr = new Rect(x0 + sliderW + 8f, cy - 11f, valW, 22f);
                KitStyle.LabelColored(vr, s.Format(reg.Value(s, target)), beyond ? KitStyle.Accent : Color.white, TextAnchor.MiddleLeft);
                if (beyond) TooltipHandler.TipRegion(vr, KitText.K("Row.Beyond", "Beyond the slider range. Typed entry (pencil) allows this."));
            }
            else
            {
                if (!buffers.TryGetValue(bk, out string buf)) buf = (pct ? val * 100f : val).ToString("0.#####", CultureInfo.InvariantCulture);
                string nb = Widgets.TextField(new Rect(x0, cy - 12f, sliderW, 24f), buf) ?? "";
                buffers[bk] = nb;
                var vr = new Rect(x0 + sliderW + 8f, cy - 11f, valW, 22f);
                if (TryParseTyped(nb, pct, out float parsed))
                {
                    float c = s.Clamp(parsed);
                    // only a text the player changed is stored (the first buffer is the stored value, maybe rounded for display)
                    if (nb != buf && Math.Abs(c - val) > 1e-7f) reg.Set(s, target, c);
                    if (Math.Abs(c - parsed) > 1e-6f)
                    {
                        KitStyle.LabelColored(vr, KitText.K("Row.Limited", "limit {0}", s.Format(c)), KitStyle.Accent, TextAnchor.MiddleLeft);
                        TooltipHandler.TipRegion(vr, s.HardLimitReason ?? KitText.K("Row.LimitGeneric", "The game cannot use this value, so the nearest possible one is used."));
                    }
                    else KitStyle.LabelColored(vr, s.Format(reg.Value(s, target)), KitStyle.Grey, TextAnchor.MiddleLeft);
                }
                else KitStyle.LabelColored(vr, s.Format(val), KitStyle.DimGrey, TextAnchor.MiddleLeft);
            }
            // the switch between slider and typing: a pencil, shown while the row is hovered (or while typing is on)
            var pr = new Rect(x1 - penW + 2f, cy - 10f, 20f, 20f);
            bool showPen = free || Mouse.IsOver(row);
            if (showPen)
            {
                GUI.color = free || Mouse.IsOver(pr) ? KitStyle.Accent : Color.white;
                GUI.DrawTexture(pr, TexButton.Rename);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(pr, free ? KitText.K("Row.FreeOffTip", "Back to the slider.")
                                                  : KitText.K("Row.FreeTip", "Type a value instead (any number up to the technical limit of the game)."));
            }
            // the button control exists every frame, hovered or not: a control that comes and goes with the mouse shifts Unity's control
            // ids, and the keyboard focus can jump between typed-entry fields (1.6 hazard review, finding 4)
            if (Widgets.ButtonInvisible(pr) && showPen)
            {
                store.freeText[s.Key] = !free;
                if (free) store.freeText.Remove(s.Key);
                if (buffers.Remove(bk)) Unfocus();
            }
        }

        /// <summary>A typed number (comma or point, optional %, percent rows typed as percent); false for empty or not a finite number.</summary>
        internal static bool TryParseTyped(string text, bool pct, out float value)
        {
            value = 0f;
            if (text == null) return false;
            if (!float.TryParse(text.Replace(',', '.').Replace("%", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float p) || float.IsNaN(p) || float.IsInfinity(p)) return false;
            value = pct ? p / 100f : p;
            return true;
        }

        /// <summary>A dragged slider value rounded to the row's step (<see cref="KitSetting.WithStep"/>, else whole numbers for Int/Silver and three significant digits of the slider span; none for log sliders).</summary>
        internal static float RoundToStep(KitSetting s, float v)
        {
            if (s.IsLog) return v;   // FromSlider already rounds to 3 significant digits
            double step = s.Step > 0 ? (double)(decimal)s.Step : (s.Kind == KitSettingKind.Int || s.Kind == KitSettingKind.Silver ? 1.0 : 0.0);
            if (step <= 0.0)
            {
                // three significant digits of the slider span; percent at most whole percents (a 0..100 % slider moves in 1 % steps)
                double span = Math.Abs((double)s.SliderMax - s.SliderMin);
                step = span <= 0.0 ? 0.001 : Math.Pow(10, Math.Floor(Math.Log10(span)) - 2);
                if (s.Kind == KitSettingKind.Percent) step = Math.Min(step, 0.01);
            }
            return (float)(Math.Round(v / step) * step);
        }

        /// <summary>True when the setting matches a search text (label, description, key, category, section name).</summary>
        public static bool MatchesSearch(KitSetting s, string q)
        {
            if (string.IsNullOrEmpty(q)) return true;
            q = q.ToLowerInvariant();
            bool Has(string t) => !string.IsNullOrEmpty(t) && t.ToLowerInvariant().Contains(q);
            var secs = s.Owner.Sections;
            return Has(s.Label) || Has(s.Desc) || Has(s.Key) || Has(s.Category) || Has(s.CategoryLabel) || Has(secs.LabelOf(s.SectionId)) || Has(s.PreviewIn(null))
                || (s.SectionId != KitMod.Core && Has(secs.Find(s.SectionId)?.LabelEn));
        }
    }
}

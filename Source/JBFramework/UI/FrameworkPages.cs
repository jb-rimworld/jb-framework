// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBFramework
{
    /// <summary>The framework's own "Mods" page: every mod that uses it, versions, areas, patch failures, errors, a link to its settings.</summary>
    internal static class FrameworkPages
    {
        static Vector2 scroll;
        static float viewH = 600f;

        internal static void DrawMods(Rect r)
        {
            KitUiProbe.Hit("framework:mods");
            var mods = Framework.Mods;
            float w = r.width - 18f;
            var view = new Rect(0f, 0f, w, Mathf.Max(viewH, r.height));
            Widgets.BeginScrollView(r, ref scroll, view);
            float y = 0f;
            y += KitStyle.Paragraph(0f, y, w, KitText.K("Mods.Intro", "Every mod that uses {0} in this game. Each one keeps its own settings, log and error fences: a problem in one never switches off another.", Framework.Name), KitStyle.Grey) + 8f;
            string show = KitText.K("Diag.Show", "Show report"), copy = KitText.K("Diag.Copy", "Copy report to clipboard");
            float b1 = Mathf.Clamp(KitUI.TextW(show) + 36f, 180f, 280f), b2 = Mathf.Clamp(KitUI.TextW(copy) + 36f, 220f, 320f);
            if (Widgets.ButtonText(new Rect(0f, y, b1, 30f), show))
                Find.WindowStack.Add(new Dialog_KitReport(KitText.K("Mods.ReportTitle", "{0}: combined report", Framework.Name), Framework.DiagnosisReport()));
            if (Widgets.ButtonText(new Rect(b1 + 10f, y, b2, 30f), copy)) KitDiagnosis.Copy(Framework.DiagnosisReport());
            TooltipHandler.TipRegion(new Rect(b1 + 10f, y, b2, 30f), KitText.K("Diag.CopyTip", "For bug reports: versions, detected mods, what is on and off, and the latest log lines. No personal paths."));
            y += 42f;

            y += KitStyle.Section(0f, y, w, KitText.K("Mods.Found", "Mods found ({0})", mods.Count));
            if (mods.Count == 0)
            {
                y += KitStyle.Paragraph(0f, y, w, KitText.K("Mods.None", "No active mod uses {0}. It does nothing on its own.", Framework.Name), KitStyle.DimGrey) + 6f;
            }
            foreach (var k in mods)
            {
                float h = 0f;
                try { h = ModRow(k, w, y); }
                catch (Exception e)
                {
                    Framework.Self?.Guard.Fail(KitMod.Core, "UI.Mods." + k?.PackageId, e);
                    KitStyle.LabelColored(new Rect(4f, y, w, 24f), (k?.Name ?? "?") + ": " + e.Message, KitStyle.Bad);
                    h = 26f;
                }
                y += h + 6f;
            }
            Widgets.EndScrollView();
            viewH = y + 10f;
        }

        static float ModRow(KitMod k, float w, float y0)
        {
            float y = y0;
            string title = k.Name + (string.IsNullOrEmpty(k.Version) ? "" : "  " + k.Version);
            KitStyle.LabelColored(new Rect(4f, y, w - 180f, 26f), title, k.Blocked ? KitStyle.Bad : Color.white, TextAnchor.MiddleLeft);
            if (!k.Blocked && k.Mod != null && Widgets.ButtonText(new Rect(w - 170f, y, 170f, 26f), KitText.K("Mods.Open", "Open settings")))
            {
                Find.WindowStack.TryRemove(typeof(Dialog_ModSettings), true);
                k.OpenSettings();
            }
            y += 28f;
            string meta = k.PackageId + "  ·  " + (k.RequiredApiLevel > 0 ? KitText.K("Mods.Needs", "needs API level {0}", k.RequiredApiLevel) : KitText.K("Mods.NoLevel", "API level not stated"))
                          + (string.IsNullOrEmpty(k.BuiltWithFramework) ? "" : "  ·  " + KitText.K("Mods.BuiltWith", "built with {0} {1}", Framework.Name, k.BuiltWithFramework))
                          + "  ·  " + KitText.K("Mods.Log", "log file {0}", k.Log.FileName);
            y += KitStyle.Paragraph(14f, y, w - 14f, meta, KitStyle.DimGrey, GameFont.Tiny) + 2f;
            if (k.Blocked)
            {
                y += KitStyle.Paragraph(14f, y, w - 14f, k.BlockedReason, KitStyle.Bad) + 2f;
                return y - y0;
            }
            if (k.Sections.All.Count > 0)
            {
                string areas = string.Join("  ·  ", k.Sections.All.Select(s => s.Label + ": " + (k.On(s.Id) ? KitText.K("On", "On") : KitText.K("Off", "Off"))));
                y += KitStyle.Paragraph(14f, y, w - 14f, KitText.K("Mods.Areas", "Areas: {0}", areas), KitStyle.Grey, GameFont.Tiny) + 2f;
            }
            int fails = k.Patches.Failures.Count;
            string patches = KitText.K("Mods.Patches", "Patches: {0} applied, {1} failed", k.Patches.Applied, fails);
            y += KitStyle.Paragraph(14f, y, w - 14f, patches, fails > 0 ? KitStyle.Bad : KitStyle.Grey, GameFont.Tiny) + 2f;
            foreach (var f in k.Patches.Failures) y += KitStyle.Paragraph(28f, y, w - 28f, f, KitStyle.Bad, GameFont.Tiny) + 1f;
            var paused = k.Guard.Paused.ToList();
            string errs = KitText.K("Mods.Errors", "Errors caught: {0} · log: errors {1}, warnings {2}", k.Guard.TotalFailures, k.Log.ErrorCount, k.Log.WarnCount);
            y += KitStyle.Paragraph(14f, y, w - 14f, errs, k.Log.ErrorCount > 0 ? KitStyle.Accent : KitStyle.Grey, GameFont.Tiny) + 2f;
            foreach (var p in paused) y += KitStyle.Paragraph(28f, y, w - 28f, k.Guard.ReasonFor(p) ?? p, KitStyle.Bad, GameFont.Tiny) + 1f;
            GUI.color = KitStyle.Line; Widgets.DrawLineHorizontal(0f, y + 2f, w); GUI.color = Color.white;
            return y - y0 + 4f;
        }
    }
}

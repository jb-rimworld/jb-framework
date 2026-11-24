// SPDX-License-Identifier: MIT
// Lifted with decoupling from Hearth & Harbor Hearth/UI/DiagnosisPage.cs (incl. Dialog_TextReport → Dialog_KitReport).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBFramework
{
    /// <summary>Plain text window with a Copy button (diagnosis report, removal report).</summary>
    internal sealed class Dialog_KitReport : Window
    {
        readonly string title, text; Vector2 scroll;
        readonly KitMod owner;
        readonly int lineCount;
        public override Vector2 InitialSize => KitUI.Size(820f, 640f, 600f, 480f, 0.92f, 0.92f);

        public Dialog_KitReport(string title, string text, KitMod owner = null)
        {
            this.owner = owner;
            this.title = title ?? ""; this.text = text ?? "";
            doCloseX = true; doCloseButton = true; absorbInputAroundWindow = true; resizeable = true; draggable = true;
            int n = 1; foreach (char ch in this.text) if (ch == '\n') n++;
            lineCount = this.text.Length == 0 ? 0 : n;
        }

        public string ReportText => text;

        public override void DoWindowContents(Rect inRect)
        {
            (owner?.Guard ?? Framework.Self?.Guard)?.Run(KitMod.Core, "UI.Report", () =>
            {
                KitUiProbe.Hit("report");
                float th = Mathf.Max(34f, KitUI.TextH(title, inRect.width, GameFont.Medium));
                Text.Font = GameFont.Medium; Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, th), title); Text.Font = GameFont.Small;
                KitStyle.LabelColored(new Rect(inRect.x, inRect.y + th, inRect.width, 20f), KitText.K("Diag.Lines", "{0} lines", lineCount), KitStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
                float top = inRect.y + th + 24f;
                // bottom row: Copy at the left; the vanilla Close button sits at the center of the same row
                float rowH = 40f;
                var outR = new Rect(inRect.x, top, inRect.width, inRect.yMax - top - rowH - 14f);
                Widgets.DrawBoxSolid(outR, new Color(0f, 0f, 0f, 0.18f));
                var inner = outR.ContractedBy(6f);
                float h = KitStyle.CachedHeight(text, inner.width - 20f, GameFont.Small);
                var view = new Rect(0, 0, inner.width - 18f, h + 8f);
                Widgets.BeginScrollView(inner, ref scroll, view);
                Widgets.Label(new Rect(0, 0, view.width, h + 4f), text);
                Widgets.EndScrollView();
                string copy = KitText.K("Diag.CopyShort", "Copy to clipboard");
                float cw = Mathf.Clamp(KitUI.TextW(copy) + 36f, 200f, Mathf.Max(200f, inRect.width / 2f - Window.CloseButSize.x / 2f - 12f));
                if (Widgets.ButtonText(new Rect(inRect.x, inRect.yMax - rowH, cw, 36f), copy)) KitDiagnosis.Copy(text);
            });
        }
    }

    /// <summary>Diagnosis page: versions, load folders, sections with Resume / Prepare removal, caretaker, patches, recent log, report.</summary>
    internal static class KitDiagnosis
    {
        static Vector2 logScroll;
        /// <summary>Text of the last "Prepare for removal" run (tests read it).</summary>
        public static string LastRemovalReport;

        /// <summary>The newest log lines of this mod (first line of each entry), oldest first.</summary>
        public static List<string> RecentLog(KitMod k, int n)
        {
            try
            {
                var all = k.Log.RecentSnapshot();
                return all.Skip(Math.Max(0, all.Length - n)).Select(l => l.Split('\n')[0]).ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>The full report: KitReport (versions, folders, sections, Harmony, compat, guard, caretaker, mod lines) plus the newest log lines.</summary>
        public static string BuildText(KitMod k, int logLines = 60)
        {
            if (k.IsFramework) return Framework.DiagnosisReport();
            var sb = new StringBuilder();
            foreach (var l in KitReport.EnvLines()) sb.AppendLine(l);
            sb.Append(KitReport.Text(k));
            sb.AppendLine("Last log lines:");
            foreach (var l in RecentLog(k, logLines)) sb.AppendLine("  " + l);
            return sb.ToString();
        }

        public static void Copy(string text)
        {
            try
            {
                GUIUtility.systemCopyBuffer = text ?? "";
                Messages.Message(KitText.K("Diag.Copied", "Copied to the clipboard."), MessageTypeDefOf.SilentInput, false);
            }
            catch (Exception e) { Framework.Self?.Log.Warn("UI", "Copy to clipboard failed: " + e.Message); }
        }

        public static void ShowReport(KitMod k) => Find.WindowStack.Add(new Dialog_KitReport(KitText.K("Diag.ReportTitle", "{0}: diagnosis", k.Name), BuildText(k), k));

        /// <summary>Runs a section's PrepareRemoval (switches it off) and shows the report.</summary>
        public static void RunPrepareRemoval(KitMod k, string id)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                Find.WindowStack.Add(new Dialog_KitReport(KitText.K("Diag.RemovalTitle", "Prepare for removal"), KitText.K("Diag.NeedsSave", "Open a game first. Preparing removal works on a running game.")));
                return;
            }
            var lines = k.Sections.PrepareRemoval(id);
            var report = new List<string> { "== " + k.Sections.LabelOf(id) };
            if (lines.Count == 0) report.Add(KitText.K("Diag.NothingToDo", "Nothing needed cleaning up."));
            else report.AddRange(lines.Select(l => "  " + l));
            report.Add("");
            report.Add(KitText.K("Diag.RemovalDone", "Done. The section is switched off. Save the game before you remove the mod."));
            LastRemovalReport = string.Join("\n", report);
            Find.WindowStack.Add(new Dialog_KitReport(KitText.K("Diag.RemovalTitle", "Prepare for removal"), LastRemovalReport, k));
        }

        public static float Draw(KitMod k, float w)
        {
            KitUiProbe.Hit("diagnosis");
            float y = 0f;
            var info = k.Cfg;
            string rw; try { rw = VersionControl.CurrentVersionStringWithRev; } catch { rw = "?"; }
            KitStyle.LabelColored(new Rect(0, y, w, 24f), $"{info?.Name} {info?.Version}  ·  {Framework.Name} {Framework.Version} (API {Framework.ApiLevel})  ·  RimWorld {rw}", KitStyle.Grey);
            y += 26f;
            KitStyle.LabelColored(new Rect(0, y, w, 22f), KitText.K("Diag.Summary", "{0} settings registered · {1} warnings · {2} errors in the log · log file {3}", k.SettingsReg.All.Count, k.Log.WarnCount, k.Log.ErrorCount, k.Log.FileName), KitStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
            y += 24f;

            string show = KitText.K("Diag.Show", "Show report"), copy = KitText.K("Diag.Copy", "Copy report to clipboard");
            float b1 = Mathf.Clamp(KitUI.TextW(show) + 36f, 180f, 280f), b2 = Mathf.Clamp(KitUI.TextW(copy) + 36f, 220f, 320f);
            if (Widgets.ButtonText(new Rect(0, y, b1, 30f), show)) ShowReport(k);
            if (Widgets.ButtonText(new Rect(b1 + 10f, y, b2, 30f), copy)) Copy(BuildText(k));
            TooltipHandler.TipRegion(new Rect(b1 + 10f, y, b2, 30f), KitText.K("Diag.CopyTip", "For bug reports: versions, detected mods, what is on and off, and the latest log lines. No personal paths."));
            y += 40f;

            y += KitStyle.Section(0, y, w, KitText.K("Diag.Folders", "Loaded folders"));
            y += KitStyle.Paragraph(4f, y, w - 8f, string.Join("  ·  ", KitReport.LoadFolders(k)), KitStyle.Grey, GameFont.Tiny) + 8f;

            if (k.Sections.All.Count > 0)
            {
                y += KitStyle.Section(0, y, w, KitText.K("Diag.Sections", "Sections"));
                bool playing = Current.ProgramState == ProgramState.Playing;
                foreach (var s in k.Sections.All)
                {
                    bool avail = k.Sections.Available(s.Id), paused = k.Guard.IsPaused(s.Id), on = k.On(s.Id);
                    KitStyle.LabelColored(new Rect(4f, y, 220f, 26f), s.Label, avail ? Color.white : KitStyle.DimGrey, TextAnchor.MiddleLeft);
                    string state = on ? KitText.K("On", "On") : (k.WhyOff(s.Id) ?? "");
                    KitStyle.LabelColored(new Rect(230f, y, Mathf.Max(100f, w - 230f - 250f), 26f), state, paused ? KitStyle.Bad : on ? KitStyle.Good : KitStyle.Grey, TextAnchor.MiddleLeft, GameFont.Tiny);
                    float bx = w - 120f;
                    if (paused && Widgets.ButtonText(new Rect(bx, y + 1f, 116f, 24f), KitText.K("Diag.Resume", "Resume"))) k.Guard.Resume(s.Id);
                    if (s.PrepareRemoval != null && playing && on)
                    {
                        var pr = new Rect(w - 250f, y + 1f, 124f, 24f);
                        if (Widgets.ButtonText(pr, KitText.K("Diag.PrepareRemovalShort", "Prepare removal")))
                        {
                            var sec = s;
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                                KitText.K("Section.PrepareRemovalConfirm", "Prepare this game for living without {0}? The section is switched off and what it placed in the save is cleaned up. Save a backup first.", sec.Label),
                                () => RunPrepareRemoval(k, sec.Id), true));
                        }
                        TooltipHandler.TipRegion(pr, KitText.K("Section.PrepareRemovalTip", "Switches the section off and removes what it put into this save, so the save also loads without it."));
                    }
                    y += 26f;
                    if (s.DiagnosisFunc != null)
                        foreach (var line in KitReport.SafeLines(k, "Diagnosis." + s.Id, s.DiagnosisFunc))
                            y += KitStyle.Paragraph(14f, y, w - 18f, line, KitStyle.Grey, GameFont.Tiny) + 1f;
                }
                y += 8f;
            }

            if (k.Caretaker.Checks.Count > 0)
            {
                y += KitStyle.Section(0, y, w, KitText.K("Diag.Caretaker", "Caretaker (repairs so far: {0})", k.Caretaker.TotalFixes));
                foreach (var c in k.Caretaker.Checks)
                {
                    KitStyle.LabelColored(new Rect(4f, y, w * 0.5f, 22f), c.Name, Color.white, TextAnchor.MiddleLeft);
                    KitStyle.LabelColored(new Rect(w * 0.5f, y, w * 0.5f, 22f), KitText.K("Diag.Fixes", "runs {0} · last {1} · total {2}", c.Runs, c.LastFixes, c.TotalFixes), c.TotalFixes > 0 ? KitStyle.Accent : KitStyle.Grey, TextAnchor.MiddleLeft);
                    y += 22f;
                }
                y += 8f;
            }

            y += KitStyle.Section(0, y, w, KitText.K("Diag.Patches", "Patches ({0} applied, {1} skipped)", k.Patches.Applied, k.Patches.Failures.Count));
            if (k.Patches.Failures.Count == 0) { KitStyle.LabelColored(new Rect(4f, y, w, 22f), KitText.K("Diag.PatchesOk", "All patch classes applied."), KitStyle.Good); y += 24f; }
            foreach (var f in k.Patches.Failures) y += KitStyle.Paragraph(10f, y, w - 20f, f, KitStyle.Bad, GameFont.Tiny) + 2f;
            if (k.Patches.SkippedInactive.Count > 0)
                y += KitStyle.Paragraph(4f, y, w - 8f, KitText.K("Diag.NotNeeded", "{0} integration patches not needed (their mod is not active).", k.Patches.SkippedInactive.Count), KitStyle.DimGrey, GameFont.Tiny) + 2f;
            y += 8f;

            if (k.Mod != null)
            {
                var lines = KitReport.SafeLines(k, "Mod.Diagnosis", k.Mod.CallDiagnosis);
                if (lines.Count > 0)
                {
                    y += KitStyle.Section(0, y, w, info?.Name ?? "");
                    foreach (var l in lines) y += KitStyle.Paragraph(4f, y, w - 8f, l, KitStyle.Grey, GameFont.Tiny) + 1f;
                    y += 8f;
                }
            }

            y += KitStyle.Section(0, y, w, KitText.K("Diag.Log", "Latest log lines"));
            var lg = RecentLog(k, 60);
            var logR = new Rect(0, y, w, 260f);
            Widgets.DrawBoxSolid(logR, new Color(0f, 0f, 0f, 0.35f));
            const float lineH = 16f;
            var lview = new Rect(0, 0, logR.width - 18f, Mathf.Max(logR.height, lg.Count * lineH + 6f));
            Widgets.BeginScrollView(logR, ref logScroll, lview);
            Text.Font = GameFont.Tiny;
            for (int i = 0; i < lg.Count; i++)
            {
                string line = lg[i];
                bool err = line.Contains("| ERROR |"), warn = line.Contains("| WARN  |");
                GUI.color = err ? KitStyle.Bad : warn ? KitStyle.Accent : KitStyle.Grey;
                Widgets.Label(new Rect(4f, 3f + i * lineH, lview.width, lineH), line);
            }
            GUI.color = Color.white; Text.Font = GameFont.Small;
            Widgets.EndScrollView();
            y += logR.height + 10f;
            return y;
        }
    }
}

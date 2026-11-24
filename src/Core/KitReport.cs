// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace JBKit
{
    /// <summary>
    /// Plain-text facts for the diagnosis page, bug reports and test logs. Never contains personal paths: load folders are printed
    /// relative to the mod folder, the log file by name only. src/UI adds its own lines through <see cref="KitHooks.ExtraReportLines"/>.
    /// </summary>
    internal static class KitReport
    {
        /// <summary>The folders this mod loaded, relative to its root (proves the 1.6 freeze / 1.7 layout), highest priority first.</summary>
        public static List<string> LoadFolders()
        {
            var list = new List<string>();
            try
            {
                var c = Kit.Mod?.Content;
                if (c?.foldersToLoadDescendingOrder == null) return list;
                string root = c.RootDir.TrimEnd('/', '\\');
                foreach (var f in c.foldersToLoadDescendingOrder)
                {
                    string rel = f.StartsWith(root, StringComparison.Ordinal) ? f.Substring(root.Length).TrimStart('/', '\\') : Path.GetFileName(f);
                    list.Add(rel.Length == 0 ? "/" : rel.Replace('\\', '/'));
                }
            }
            catch (Exception e) { list.Add("(could not read: " + e.Message + ")"); }
            return list;
        }

        public static List<string> Lines()
        {
            var L = new List<string>();
            void Add(string s) => L.Add(s);
            string rw; try { rw = VersionControl.CurrentVersionStringWithRev; } catch { rw = "?"; }
            var info = Kit.Info;
            Add($"{info?.Name} {info?.Version} · JB.Kit {KitInfo.KitVersion} ({KitInfo.KitCommit}, tier {KitInfo.KitTier}{(KitInfo.KitTests ? ", TEST BUILD" : "")})");
            Add($"RimWorld {rw} · packageId {Kit.Mod?.Content?.PackageId} · per-save {(info != null && info.PerSave ? "on" : "off")}");
            int modCount = 0; try { modCount = ModsConfig.ActiveModsInLoadOrder.Count(); } catch { }
            var dlcs = KitCompat.ActiveDlcNames();
            Add($"Active mods: {modCount} · DLC: {(dlcs.Count == 0 ? "none" : string.Join(", ", dlcs))}");
            Add("Load folders: " + string.Join(", ", LoadFolders()));
            Add($"Settings: {KitSettings.All.Count} registered · profile: {(Kit.HasSaveProfile ? "this save" : "template")} · settings file: {(Kit.Settings != null ? "yes" : "MISSING")}" +
                (Kit.Settings != null && Kit.Settings.MigratedThisSession ? $" · migrated {Kit.Settings.Migrated.Count} old values" : ""));
            if (KitSections.All.Count > 0)
            {
                Add("Sections:");
                foreach (var s in KitSections.All)
                {
                    string state = KitSections.On(s.Id) ? "on" : "off (" + KitSections.WhyOff(s.Id) + ")";
                    Add($"  {s.Id}: {state}");
                    if (s.Diagnosis != null)
                        foreach (var line in KitGuard.Run(s.Id, "Diagnosis." + s.Id, () => s.Diagnosis().ToList(), new List<string>())) Add("    " + line);
                }
            }
            Add($"Harmony: {KitPatches.Applied} patch classes applied, {KitPatches.Failures.Count} skipped, {KitPatches.SkippedInactive.Count} not needed");
            foreach (var f in KitPatches.Failures) Add("  skipped: " + f);
            var shared = KitPatches.SharedTargets();
            if (shared.Count > 0) { Add("Methods also patched by other mods:"); foreach (var s in shared) Add("  " + s); }
            if (KitCompat.All.Count > 0)
            {
                Add("Compatibility:");
                foreach (var c in KitCompat.All) Add($"  {c.Name} ({c.PackageId}): {c.StateLabel}");
            }
            Add($"Errors caught: {KitGuard.TotalFailures} · log errors {KitLog.ErrorCount}, warnings {KitLog.WarnCount} · log file {KitLog.FileName}");
            foreach (var p in KitGuard.Paused) Add("  paused: " + KitGuard.ReasonFor(p));
            if (KitCaretaker.Checks.Count > 0)
                Add("Caretaker: " + string.Join(", ", KitCaretaker.Checks.Select(c => $"{c.Name} {c.Runs} runs/{c.TotalFixes} fixes")));
            if (Kit.Mod != null) foreach (var line in KitGuard.Run(KitSections.Core, "Mod.Diagnosis", () => Kit.Mod.Diagnosis().ToList(), new List<string>())) Add(line);
            if (KitHooks.ExtraReportLines != null) foreach (var line in KitGuard.Run(KitSections.Core, "ExtraReportLines", () => KitHooks.ExtraReportLines().ToList(), new List<string>())) Add(line);
            return L;
        }

        /// <summary>The report as one text (for the Copy button and test output).</summary>
        public static string Text()
        {
            var sb = new StringBuilder();
            foreach (var l in Lines()) sb.AppendLine(l);
            return sb.ToString();
        }
    }
}

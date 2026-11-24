// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// Plain-text facts for the diagnosis pages, bug reports and test logs. Never contains personal paths: load folders are printed
    /// relative to the mod folder, log files by name only. Every mod's part is fenced: a failing mod shows one error line.
    /// </summary>
    internal static class KitReport
    {
        /// <summary>The folders a mod loaded, relative to its root, highest priority first (proves the 1.6/1.7 layout).</summary>
        public static List<string> LoadFolders(KitMod k)
        {
            var list = new List<string>();
            try
            {
                var c = k?.Content;
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

        /// <summary>
        /// Runs a mod's report function. A throwing one gives an error line instead; it is logged once and never pauses a section
        /// (a broken report must not switch off gameplay).
        /// </summary>
        internal static List<string> SafeLines(KitMod k, string site, Func<IEnumerable<string>> f)
        {
            if (f == null) return new List<string>();
            try { return (f() ?? Enumerable.Empty<string>()).ToList(); }
            catch (Exception e)
            {
                k.Log.Once("report:" + site + ":" + e.GetType().Name, KitLogLevel.Error, "Report", site + " threw (report shows an error line; nothing was paused): " + e);
                return new List<string> { "(" + site + " failed: " + e.GetType().Name + ": " + e.Message + ")" };
            }
        }

        public static List<string> Lines(KitMod k)
        {
            var L = new List<string>();
            void Add(string s) => L.Add(s);
            var info = k.Cfg;
            Add($"{info.Name} {info.Version} · packageId {k.PackageId} · per-save {(info.PerSave ? "on" : "off")} · needs API level {(k.RequiredApiLevel > 0 ? k.RequiredApiLevel.ToString() : "-")}" +
                (string.IsNullOrEmpty(k.BuiltWithFramework) ? "" : " (built with " + k.BuiltWithFramework + ")"));
            if (k.Blocked) { Add("BLOCKED: " + k.BlockedReason); return L; }
            Add("Load folders: " + string.Join(", ", LoadFolders(k)));
            Add($"Settings: {k.SettingsReg.All.Count} registered · profile: {(k.HasSaveProfile ? "this save" : "template")} · settings file: {(k.Settings != null ? "yes" : "none")}" +
                (k.Settings != null && k.Settings.MigratedThisSession ? $" · migrated {k.Settings.Migrated.Count} old values" : ""));
            if (k.Sections.All.Count > 0)
            {
                Add("Sections:");
                foreach (var s in k.Sections.All)
                {
                    string state = k.On(s.Id) ? "on" : "off (" + k.WhyOff(s.Id) + ")";
                    Add($"  {s.Id}: {state}");
                    if (s.DiagnosisFunc != null)
                        foreach (var line in SafeLines(k, "Diagnosis." + s.Id, s.DiagnosisFunc)) Add("    " + line);
                }
            }
            Add($"Harmony ({k.Cfg.HarmonyId}): {k.Patches.Applied} patch classes applied, {k.Patches.Failures.Count} skipped, {k.Patches.SkippedInactive.Count} not needed");
            foreach (var f in k.Patches.Failures) Add("  skipped: " + f);
            var shared = k.Patches.SharedTargets();
            if (shared.Count > 0) { Add("Methods also patched by other mods:"); foreach (var s in shared) Add("  " + s); }
            if (k.Compat.All.Count > 0)
            {
                Add("Compatibility:");
                foreach (var c in k.Compat.All) Add($"  {c.Name} ({c.PackageId}): {c.StateLabel}");
            }
            Add($"Errors caught: {k.Guard.TotalFailures} · log errors {k.Log.ErrorCount}, warnings {k.Log.WarnCount} · log file {k.Log.FileName}");
            foreach (var p in k.Guard.Paused) Add("  paused: " + k.Guard.ReasonFor(p));
            if (k.Caretaker.Checks.Count > 0)
                Add("Caretaker: " + string.Join(", ", k.Caretaker.Checks.Select(c => $"{c.Name} {c.Runs} runs/{c.TotalFixes} fixes")));
            if (k.Mod != null) foreach (var line in SafeLines(k, "Mod.Diagnosis", k.Mod.CallDiagnosis)) Add(line);
            KitHooks.Each(KitHooks.ReportLines, k, "ReportLines", h => { foreach (var line in h(k) ?? Enumerable.Empty<string>()) Add(line); });
            return L;
        }

        /// <summary>One mod's report as text, never throws.</summary>
        public static string Text(KitMod k)
        {
            var sb = new StringBuilder();
            try { foreach (var l in Lines(k)) sb.AppendLine(l); }
            catch (Exception e) { sb.AppendLine((k?.Name ?? "?") + ": report failed: " + e.GetType().Name + ": " + e.Message); }
            return sb.ToString();
        }

        /// <summary>Environment lines shared by every report: framework, RimWorld, mods and DLCs.</summary>
        public static List<string> EnvLines()
        {
            var L = new List<string> { Framework.Header() };
            int modCount = 0; try { modCount = ModsConfig.ActiveModsInLoadOrder.Count(); } catch { }
            var dlcs = KitCompat.ActiveDlcNames();
            L.Add($"Active mods: {modCount} · DLC: {(dlcs.Count == 0 ? "none" : string.Join(", ", dlcs))} · mods using {Framework.Name}: {Framework.Mods.Count}");
            return L;
        }

        /// <summary>One line per dependent mod: name, version, areas on/total, patch failures, errors, blocked.</summary>
        public static IEnumerable<string> TenantSummary()
        {
            var list = new List<string>();
            foreach (var k in Framework.Mods)
            {
                try
                {
                    if (k.Blocked) { list.Add($"{k.Name} {k.Version}: BLOCKED ({k.BlockedReason})"); continue; }
                    int on = k.Sections.All.Count(s => k.On(s.Id));
                    list.Add($"{k.Name} {k.Version} ({k.PackageId}): areas {on}/{k.Sections.All.Count} on, patches {k.Patches.Applied} applied/{k.Patches.Failures.Count} failed, errors {k.Log.ErrorCount}, paused {k.Guard.Paused.Count()}");
                }
                catch (Exception e) { list.Add(k?.Name + ": summary failed: " + e.Message); }
            }
            if (list.Count == 0) list.Add("No mod uses " + Framework.Name + " yet.");
            return list;
        }

        /// <summary>The combined report: environment, the framework's own part, then every dependent mod, each fenced.</summary>
        public static string Combined()
        {
            var sb = new StringBuilder();
            try { foreach (var l in EnvLines()) sb.AppendLine(l); } catch (Exception e) { sb.AppendLine("environment failed: " + e.Message); }
            var self = Framework.Self;
            if (self != null)
            {
                sb.AppendLine();
                sb.AppendLine("== " + Framework.Name);
                sb.Append(Text(self));
            }
            foreach (var k in Framework.Mods)
            {
                sb.AppendLine();
                sb.AppendLine("== " + k.Name);
                sb.Append(Text(k));
            }
            return sb.ToString();
        }
    }
}

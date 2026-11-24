// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Foundation/Guard.cs (ModuleId → string section id).
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace JBKit
{
    /// <summary>
    /// Error barrier. Wrap ticks, UI and patch bodies with real logic in <c>KitGuard.Run(section, "Site", …)</c>.
    /// An exception is logged once per site with full stack, counted and swallowed. Three failures in a row at one site pause the
    /// section for this session: <see cref="KitSections.On"/> returns false, a letter tells the player, the window offers Resume.
    /// The section <see cref="KitSections.Core"/> is never paused. A successful run resets that site's counter.
    /// </summary>
    internal static class KitGuard
    {
        public const int MaxConsecutive = 3;
        static readonly Dictionary<(string, string), int> consecutive = new Dictionary<(string, string), int>();
        static readonly HashSet<string> paused = new HashSet<string>();
        static readonly Dictionary<string, string> pauseReason = new Dictionary<string, string>();
        static readonly HashSet<string> loggedSites = new HashSet<string>();
        /// <summary>Total exceptions caught since start (all sections).</summary>
        public static int TotalFailures;

        public static bool IsPaused(string section) => section != null && section != KitSections.Core && paused.Contains(section);
        public static IEnumerable<string> Paused => paused;
        /// <summary>Why a section is paused (null if it is not).</summary>
        public static string ReasonFor(string section) => section != null && pauseReason.TryGetValue(section, out var r) ? r : null;

        /// <summary>Runs the action unless the section is paused (then it returns immediately).</summary>
        public static void Run(string section, string site, Action a)
        {
            if (IsPaused(section)) return;
            try { a(); Ok(section, site); }
            catch (Exception e) { Fail(section, site, e); }
        }

        /// <summary>Runs the function unless the section is paused (then it returns the fallback immediately).</summary>
        public static T Run<T>(string section, string site, Func<T> f, T fallback)
        {
            if (IsPaused(section)) return fallback;
            try { var r = f(); Ok(section, site); return r; }
            catch (Exception e) { Fail(section, site, e); return fallback; }
        }

        static void Ok(string section, string site)
        {
            if (consecutive.Count == 0) return;
            var k = (section ?? KitSections.Core, site);
            if (consecutive.TryGetValue(k, out int c) && c != 0) consecutive[k] = 0;
        }

        /// <summary>Books a failure (use directly in catch blocks that need their own fallback logic).</summary>
        public static void Fail(string section, string site, Exception e)
        {
            section = section ?? KitSections.Core;
            TotalFailures++;
            bool first;
            lock (loggedSites) first = loggedSites.Add(section + "|" + site + "|" + e.GetType().Name);
            if (first) KitLog.Error(section, "Error in " + site + " (logged once per site)", e);
            var k = (section, site);
            int c = consecutive.TryGetValue(k, out int v) ? v + 1 : 1;
            consecutive[k] = c;
            if (c >= MaxConsecutive && section != KitSections.Core && paused.Add(section))
            {
                string reason = $"{KitSections.LabelOf(section)} paused after {c} errors in a row ({site}: {e.GetType().Name}: {e.Message})";
                pauseReason[section] = reason;
                KitLog.Error(section, reason);
                try
                {
                    if (Current.Game != null)
                    {
                        string name = Kit.Info?.Name ?? "Mod";
                        Find.LetterStack.ReceiveLetter(
                            KitText.K("Guard.PausedTitle", "{0}: feature paused", name),
                            KitText.K("Guard.PausedText", "A part of {0} ran into repeated errors and paused itself so your game keeps running.\n\n{1}\n\nYou can resume it in Mod settings → {0} (Resume button on the Overview page). Details are in {2}.",
                                name, reason, KitLog.FileName),
                            LetterDefOf.NegativeEvent);
                    }
                }
                catch { }
            }
        }

        /// <summary>Un-pauses a section and forgets its failure counters.</summary>
        public static void Resume(string section)
        {
            if (section == null) return;
            paused.Remove(section);
            pauseReason.Remove(section);
            foreach (var k in consecutive.Keys.Where(k => k.Item1 == section).ToList()) consecutive.Remove(k);
            KitLog.Info(section, "Section resumed by player");
        }

        /// <summary>Tests: forget all pauses and counters.</summary>
        public static void ResetAll()
        {
            paused.Clear(); pauseReason.Clear(); consecutive.Clear();
        }
    }

    /// <summary>
    /// Self-repair. The mod registers named checks (<see cref="KitRegistry.Caretaker"/>); the caretaker runs one check every 250 ticks
    /// (round robin) and all checks once after loading a save. A check returns how many problems it fixed and logs each fix itself via
    /// <see cref="Fixed"/>. Checks must only repair what is clearly broken. Needs the mod's KitGameBase subclass (it drives the ticks).
    /// </summary>
    internal static class KitCaretaker
    {
        internal sealed class Check
        {
            public string Name; public string Section; public Func<int> Run; public int TotalFixes; public int LastFixes; public int Runs;
        }
        public static readonly List<Check> Checks = new List<Check>();
        public static int TotalFixes;
        static int runFixed;

        public static void Register(string section, string name, Func<int> run)
        {
            if (run == null || Checks.Any(c => c.Name == name)) return;
            Checks.Add(new Check { Name = name, Section = section ?? KitSections.Core, Run = run });
        }

        /// <summary>Call from a check for every repair: logs a warning with what was repaired.</summary>
        public static void Fixed(string checkName, string what)
        {
            TotalFixes++; runFixed++;
            KitLog.Warn("Caretaker", checkName + ": repaired " + what);
        }

        static void RunCheck(Check c)
        {
            runFixed = 0;
            int n = KitGuard.Run(c.Section, "Caretaker." + c.Name, c.Run, 0);
            int reported = runFixed; runFixed = 0;
            if (n > reported) TotalFixes += n - reported; else n = reported;
            c.LastFixes = n; c.TotalFixes += n; c.Runs++;
        }

        /// <summary>Runs every check whose section is on. force = also checks of switched-off sections (still not paused or unavailable ones).</summary>
        public static void RunAll(bool force = false)
        {
            foreach (var c in Checks.ToArray())
            {
                if (!KitSections.Available(c.Section)) continue;
                if (!force && !KitSections.On(c.Section)) continue;
                RunCheck(c);
            }
        }

        internal static void Tick(int ticksGame)
        {
            if (Checks.Count == 0 || ticksGame % 250 != 17) return;
            var c = Checks[(ticksGame / 250) % Checks.Count];
            if (!KitSections.On(c.Section)) return;
            RunCheck(c);
        }
    }
}

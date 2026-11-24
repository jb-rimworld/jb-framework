// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Foundation/Guard.cs (ModuleId → string section id); one guard per mod.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// One mod's error barrier (public face: <see cref="KitMod.Run(string, string, Action)"/>). An exception is logged once per site
    /// with full stack, counted and swallowed. Three failures in a row at one site pause the section for this session: the section
    /// reads as off, a letter tells the player, the window offers Resume. <see cref="KitMod.Core"/> is never paused.
    /// A successful run resets that site's counter. Pauses of one mod never touch another mod.
    /// </summary>
    internal sealed class KitGuard
    {
        public const int MaxConsecutive = 3;
        readonly KitMod k;
        readonly Dictionary<(string, string), int> consecutive = new Dictionary<(string, string), int>();
        readonly HashSet<string> paused = new HashSet<string>();
        readonly Dictionary<string, string> pauseReason = new Dictionary<string, string>();
        readonly HashSet<string> loggedSites = new HashSet<string>();
        /// <summary>Total exceptions caught since start (all sections of this mod).</summary>
        public int TotalFailures;

        public KitGuard(KitMod owner) { k = owner; }

        public bool IsPaused(string section) => section != null && section != KitMod.Core && paused.Contains(section);
        public IEnumerable<string> Paused => paused;
        public string ReasonFor(string section) => section != null && pauseReason.TryGetValue(section, out var r) ? r : null;

        public void Run(string section, string site, Action a)
        {
            if (a == null || IsPaused(section)) return;
            try { a(); Ok(section, site); }
            catch (Exception e) { Fail(section, site, e); }
        }

        public T Run<T>(string section, string site, Func<T> f, T fallback)
        {
            if (f == null || IsPaused(section)) return fallback;
            try { var r = f(); Ok(section, site); return r; }
            catch (Exception e) { Fail(section, site, e); return fallback; }
        }

        void Ok(string section, string site)
        {
            if (consecutive.Count == 0) return;
            var key = (section ?? KitMod.Core, site);
            if (consecutive.TryGetValue(key, out int c) && c != 0) consecutive[key] = 0;
        }

        public void Fail(string section, string site, Exception e)
        {
            section = section ?? KitMod.Core;
            if (e == null) e = new Exception("(no exception)");
            TotalFailures++;
            bool first;
            lock (loggedSites) first = loggedSites.Add(section + "|" + site + "|" + e.GetType().Name);
            if (first) k.Log.Error(section, "Error in " + site + " (logged once per site)", e);
            var key = (section, site);
            int c = consecutive.TryGetValue(key, out int v) ? v + 1 : 1;
            consecutive[key] = c;
            if (c >= MaxConsecutive && section != KitMod.Core && paused.Add(section))
            {
                string reason = $"{k.Sections.LabelOf(section)} paused after {c} errors in a row ({site}: {e.GetType().Name}: {e.Message})";
                pauseReason[section] = reason;
                k.Log.Error(section, reason);
                try
                {
                    if (Current.Game != null)
                        Find.LetterStack.ReceiveLetter(
                            KitText.K("Guard.PausedTitle", "{0}: feature paused", k.Name),
                            KitText.K("Guard.PausedText", "A part of {0} ran into repeated errors and paused itself so your game keeps running.\n\n{1}\n\nYou can resume it in Mod settings → {0} (Resume button on the Overview page). Details are in {2}.",
                                k.Name, reason, k.Log.FileName),
                            LetterDefOf.NegativeEvent);
                }
                catch { }
            }
        }

        public void Resume(string section)
        {
            if (section == null) return;
            paused.Remove(section);
            pauseReason.Remove(section);
            foreach (var key in consecutive.Keys.Where(x => x.Item1 == section).ToList()) consecutive.Remove(key);
            k.Log.Info(section, "Section resumed by player");
        }

        /// <summary>Tests: forget all pauses and counters.</summary>
        public void ResetAll() { paused.Clear(); pauseReason.Clear(); consecutive.Clear(); }
    }

    /// <summary>
    /// One mod's self-repair checks (<see cref="KitRegistry.Caretaker"/>): one check every 250 ticks (round robin) and all checks once
    /// after loading a save, driven by the framework's game component.
    /// </summary>
    internal sealed class KitCaretaker
    {
        internal sealed class Check
        {
            public string Name; public string Section; public Func<int> Run; public int TotalFixes; public int LastFixes; public int Runs;
        }
        readonly KitMod k;
        public readonly List<Check> Checks = new List<Check>();
        public int TotalFixes;
        int runFixed;

        public KitCaretaker(KitMod owner) { k = owner; }

        public void Register(string section, string name, Func<int> run)
        {
            if (run == null || Checks.Any(c => c.Name == name)) return;
            Checks.Add(new Check { Name = name, Section = section ?? KitMod.Core, Run = run });
        }

        public void Fixed(string checkName, string what)
        {
            TotalFixes++; runFixed++;
            k.Log.Warn("Caretaker", checkName + ": repaired " + what);
        }

        void RunCheck(Check c)
        {
            runFixed = 0;
            int n = k.Guard.Run(c.Section, "Caretaker." + c.Name, c.Run, 0);
            int reported = runFixed; runFixed = 0;
            if (n > reported) TotalFixes += n - reported; else n = reported;
            c.LastFixes = n; c.TotalFixes += n; c.Runs++;
        }

        /// <summary>Runs every check whose section is on. force = also checks of switched-off sections (still not paused or unavailable ones).</summary>
        public void RunAll(bool force = false)
        {
            foreach (var c in Checks.ToArray())
            {
                if (!k.Sections.Available(c.Section)) continue;
                if (!force && !k.On(c.Section)) continue;
                RunCheck(c);
            }
        }

        internal void Tick(int ticksGame)
        {
            if (Checks.Count == 0 || ticksGame % 250 != 17) return;
            var c = Checks[(ticksGame / 250) % Checks.Count];
            if (!k.On(c.Section)) return;
            RunCheck(c);
        }
    }
}

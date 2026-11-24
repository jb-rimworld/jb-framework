// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Hearth.Core/Testing/HearthTests.cs (HearthTestRunner). Compiled only with -p:JBTests=true.
#if JB_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace JBKit.Testing
{
    /// <summary>Environment of a test run (set by tools/jbmod).</summary>
    internal static class KitTestEnv
    {
        /// <summary>Suites to run: "all", "a,b", "prefix.*", "section:&lt;id&gt;". Empty = no test run.</summary>
        public const string Test = "JB_TEST";
        /// <summary>Result file (default: &lt;config&gt;/&lt;ShortId&gt;_test_results.txt).</summary>
        public const string Out = "JB_TEST_OUT";
        /// <summary>packageId of the mod under test (pre-slot id). Only that mod's runner acts, so two kit mods with tests never both quit.</summary>
        public const string Mod = "JB_TEST_MOD";
        /// <summary>Name of the save this game was loaded from (two-phase runs), else unset.</summary>
        public const string Load = "JB_TEST_LOAD";
        /// <summary>Where kit.settingsdump writes its JSON.</summary>
        public const string Dump = "JB_TEST_DUMP";
        /// <summary>Copy of the old (pre-kit) settings file the slot profile started with (kit.migration).</summary>
        public const string Migration = "JB_TEST_MIGRATION";
        /// <summary>Red errors recorded by the probe mod (tools/probe), one per line.</summary>
        public const string ProbeErr = "JB_PROBE_ERR";

        public static string Get(string k) { try { return Environment.GetEnvironmentVariable(k); } catch { return null; } }

        /// <summary>True when JB_TEST is set and JB_TEST_MOD is unset or names this mod (its packageId, with or without the test-slot suffix).</summary>
        public static bool ForThisMod
        {
            get
            {
                if (string.IsNullOrEmpty(Get(Test))) return false;
                string want = Get(Mod);
                if (string.IsNullOrEmpty(want)) return true;
                string pid = (Kit.Mod?.Content?.PackageId ?? "").ToLowerInvariant();
                want = want.Trim().ToLowerInvariant();
                return pid == want || System.Text.RegularExpressions.Regex.Replace(pid, @"\.slot\d+$", "") == want;
            }
        }
    }

    /// <summary>
    /// The in-game test runner. Public abstract (the game creates the mod's concrete subclass by Type):
    /// <code>
    /// #if JB_TESTS
    /// public sealed class LOTests : JBKit.Testing.KitTestRunnerBase { public LOTests(Game game) : base(game) { } }
    /// #endif
    /// </code>
    /// Acts only when JB_TEST is set for this mod (<see cref="KitTestEnv.ForThisMod"/>): closes dialogs, runs the game at top speed,
    /// runs the selected suites one after the other, writes the result file and quits the game. Never part of a release build
    /// (tools/release.py scans the DLL for these names).
    /// </summary>
    public abstract class KitTestRunnerBase : GameComponent
    {
        sealed class RunState { public KitTestSuite Suite; public IEnumerator<int> It; public TestContext Ctx; public int Started; public int WaitUntil; public bool Done; public string Error; }
        readonly List<RunState> runs = new List<RunState>();
        int current = -1, startDelay = 300;
        bool finished, written;
        int kitErrorsAtStart, redMarkAtStart;
        readonly List<string> unknownSuites = new List<string>();

        protected KitTestRunnerBase(Game game) { }

        internal static bool Active => KitTestEnv.ForThisMod;

        /// <summary>Called from KitBoot.InstallTesting: a test run without a declared runner would silently do nothing.</summary>
        internal static void CheckDeclared()
        {
            if (!Active) return;
            bool has = KitPatches.SafeTypes(typeof(KitTestRunnerBase).Assembly).Any(t => typeof(KitTestRunnerBase).IsAssignableFrom(t) && !t.IsAbstract);
            if (!has)
                KitLog.Error("Test", "JB_TEST is set but this mod declares no KitTestRunnerBase subclass; no suite will run. Declare '#if JB_TESTS public sealed class <Name>Tests : JBKit.Testing.KitTestRunnerBase { public <Name>Tests(Game g) : base(g) { } } #endif'.");
            else
                KitLog.Info("Test", "Test run requested: " + KitTestEnv.Get(KitTestEnv.Test));
        }

        public sealed override void LoadedGame()
        {
            if (!Active || SaveLoadKit.LoadedSave == null) return;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try { SaveLoadKit.AfterLoad = SaveFingerprint.Take(); SaveLoadKit.AfterLoadTick = Find.TickManager.TicksGame; }
                catch (Exception e) { Log.Error("[JBTEST] fingerprint after load failed: " + e); }
            });
        }

        public sealed override void GameComponentUpdate()
        {
            if (!Active) return;
            var ws = Find.WindowStack;
            if (ws != null)
                foreach (var w in ws.Windows.ToList())
                    if (w is Dialog_MessageBox || w is Dialog_NodeTree || (w.forcePause && !(w is Page)))
                    { Log.Message("[JBTEST] closing window " + w.GetType().Name); ws.TryRemove(w, false); }
            var tm = Find.TickManager;
            if (tm != null && !finished && (tm.Paused || tm.CurTimeSpeed != TimeSpeed.Ultrafast)) tm.CurTimeSpeed = TimeSpeed.Ultrafast;
            if (finished && !written) { written = true; WriteResults(); LongEventHandler.ExecuteWhenFinished(() => Root.Shutdown()); }
        }

        public sealed override void GameComponentTick()
        {
            if (!Active || finished) return;
            int now = Find.TickManager.TicksGame;
            if (runs.Count == 0 && current == -1)
            {
                if (--startDelay > 0) return;
                SelectSuites();
                kitErrorsAtStart = KitLog.ErrorCount;
                redMarkAtStart = KitTestLog.Mark();
                current = 0;
                if (runs.Count == 0) { finished = true; return; }
            }
            if (current >= runs.Count) { finished = true; return; }
            var r = runs[current];
            if (r.It == null)
            {
                NeutralizeGame(r.Suite.KeepsLoadedGame);
                r.Started = now; r.WaitUntil = now;
                r.Ctx.Log("start at tick " + now);
                try { r.It = r.Suite.Run(r.Ctx).GetEnumerator(); } catch (Exception e) { r.Error = e.ToString(); r.Done = true; }
            }
            if (!r.Done && now >= r.WaitUntil)
            {
                try
                {
                    if (r.It.MoveNext()) r.WaitUntil = now + Math.Max(0, r.It.Current);
                    else r.Done = true;
                }
                catch (Exception e) { r.Error = e.ToString(); r.Done = true; }
            }
            if (!r.Done && now - r.Started > r.Suite.MaxTicks) { r.Error = "timeout after " + r.Suite.MaxTicks + " ticks"; r.Done = true; }
            if (r.Done)
            {
                try { r.It?.Dispose(); } catch (Exception e) { r.Ctx.Log("dispose threw: " + e.Message); }
                if (r.Error != null) { r.Ctx.Failures++; r.Ctx.Log("EXCEPTION/TIMEOUT: " + r.Error); }
                r.Ctx.Log($"end: {r.Ctx.Checks} checks, {r.Ctx.Failures} failures, {now - r.Started} ticks");
                current++;
            }
        }

        public sealed override void ExposeData() { }

        void SelectSuites()
        {
            var wanted = (KitTestEnv.Get(KitTestEnv.Test) ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var all = new List<KitTestSuite>();
            foreach (var t in KitPatches.SafeTypes(typeof(KitTestSuite).Assembly).Where(t => typeof(KitTestSuite).IsAssignableFrom(t) && !t.IsAbstract))
            {
                try { all.Add((KitTestSuite)Activator.CreateInstance(t, true)); }
                catch (Exception e) { unknownSuites.Add(t.Name + " (could not be created: " + e.Message + ")"); }
            }
            all = all.OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
            bool Picked(KitTestSuite s)
            {
                if (wanted.Contains(s.Name)) return true;
                if (s.ExplicitOnly) return false;
                if (wanted.Contains("all")) return true;
                if (wanted.Any(w => w.EndsWith(".*") && s.Name.StartsWith(w.Substring(0, w.Length - 1), StringComparison.Ordinal))) return true;
                return wanted.Any(w => w.StartsWith("section:") && s.Section == w.Substring(8));
            }
            foreach (var s in all.Where(Picked)) runs.Add(new RunState { Suite = s, Ctx = new TestContext { Suite = s.Name } });
            // a misspelled suite name must fail the run, not silently pass with fewer suites
            foreach (var w in wanted)
                if (w != "all" && !w.EndsWith(".*") && !w.StartsWith("section:") && !all.Any(s => s.Name == w)) unknownSuites.Add(w);
            Log.Message($"[JBTEST] {Kit.Info?.Name}: starting {runs.Count} suites: {string.Join(", ", runs.Select(r => r.Suite.Name))} (available: {string.Join(", ", all.Select(s => s.Name))})");
        }

        /// <summary>The quicktest colony is a live game. Before every suite: no storyteller incidents; unless the suite checks a loaded save,
        /// also clear weather, end game conditions, calm mental breaks and fill colonists' rest and food.</summary>
        static void NeutralizeGame(bool keepLoadedGame)
        {
            try
            {
                Find.Storyteller?.storytellerComps?.Clear();
                if (keepLoadedGame) return;
                var map = Find.CurrentMap; if (map == null) return;
                if (map.weatherManager.curWeather != WeatherDefOf.Clear) { map.weatherManager.curWeather = WeatherDefOf.Clear; map.weatherManager.lastWeather = WeatherDefOf.Clear; }
                foreach (var gc in map.gameConditionManager.ActiveConditions.ToList())
                    try { gc.End(); } catch (Exception e) { Log.Warning("[JBTEST] could not end game condition " + gc.def.defName + ": " + e.Message); }
                foreach (var p in map.mapPawns.AllPawnsSpawned.Where(x => x.Faction == Faction.OfPlayer && x.InMentalState).ToList())
                    try { p.MentalState.RecoverFromState(); } catch (Exception e) { Log.Warning("[JBTEST] could not end a mental state: " + e.Message); }
                foreach (var c in map.mapPawns.FreeColonistsSpawned)
                {
                    if (c.needs?.rest != null) c.needs.rest.CurLevel = 1f;
                    if (c.needs?.food != null) c.needs.food.CurLevel = 1f;
                }
            }
            catch (Exception e) { Log.Warning("[JBTEST] could not neutralize the game: " + e.Message); }
        }

        void WriteResults()
        {
            var sb = new StringBuilder();
            int fails = runs.Sum(r => r.Ctx.Failures) + unknownSuites.Count, checks = runs.Sum(r => r.Ctx.Checks);
            int newKitErrors = KitLog.ErrorCount - kitErrorsAtStart;
            var oursRed = KitTestLog.Since(redMarkAtStart, true);
            bool pass = fails == 0 && runs.Count > 0 && newKitErrors == 0 && oursRed.Count == 0;
            sb.AppendLine($"RESULT {(pass ? "PASS" : "FAIL")} mod={Kit.Mod?.Content?.PackageId} suites={runs.Count} checks={checks} failures={fails} kitLogErrors={newKitErrors} ourRedErrors={oursRed.Count} unknownSuites={unknownSuites.Count}");
            sb.AppendLine($"kit {KitInfo.KitVersion} ({KitInfo.KitCommit}, tier {KitInfo.KitTier}) · {Kit.Info?.Name} {Kit.Info?.Version}");
            foreach (var u in unknownSuites) sb.AppendLine($"## {u}: FAIL (unknown suite name)");
            foreach (var e in oursRed.Take(30)) sb.AppendLine("  our red error during the suites: " + KitTestLog.Format(e));
            if (newKitErrors > 0) foreach (var l in KitLog.RecentSnapshot().Where(l => l.Contains("| ERROR |")).Take(20)) sb.AppendLine("  kit log error: " + l.Split('\n')[0]);
            var probe = SaveLoadKit.ProbeErrors();
            if (KitTestEnv.Get(KitTestEnv.ProbeErr) != null) sb.AppendLine($"red errors in the game log (probe, all mods): {probe.Count}");
            foreach (var r in runs)
            {
                sb.AppendLine($"## {r.Suite.Name}: {(r.Ctx.Failures == 0 ? "PASS" : "FAIL")} ({r.Ctx.Checks} checks, {r.Ctx.Failures} failures)");
                foreach (var l in r.Ctx.Lines) sb.AppendLine(l);
            }
            string outPath = KitTestEnv.Get(KitTestEnv.Out);
            if (string.IsNullOrEmpty(outPath)) outPath = Path.Combine(GenFilePaths.ConfigFolderPath, (Kit.Info?.ShortId ?? "JBKit") + "_test_results.txt");
            try { File.WriteAllText(outPath, sb.ToString()); } catch (Exception e) { Log.Error("[JBTEST] could not write results: " + e.Message); }
            Log.Message("[JBTEST] " + sb.ToString().Split('\n')[0]);
        }
    }
}

namespace JBKit
{
    internal static partial class KitBoot
    {
        static partial void InstallTesting()
        {
            Testing.KitTestLog.Install();
            Testing.KitTestRunnerBase.CheckDeclared();
        }
    }
}
#endif

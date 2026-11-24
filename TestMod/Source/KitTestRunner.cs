// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Hearth.Core/Testing/HearthTests.cs (HearthTestRunner). Part of JBFramework.Testing.dll (local test mod).
// One runner for every mod: it picks the mod under test by JB_TEST_MOD and finds the suites in the harness and in that mod's assemblies.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;

namespace JBFramework.Testing
{
    /// <summary>Environment of a test run (set by tools/jbmod).</summary>
    public static class KitTestEnv
    {
        /// <summary>Suites to run: "all", "a,b", "prefix.*", "section:&lt;id&gt;". Empty = no test run.</summary>
        public const string Test = "JB_TEST";
        /// <summary>Result file (default: &lt;config&gt;/jb_test_results.txt).</summary>
        public const string Out = "JB_TEST_OUT";
        /// <summary>packageId of the mod under test (without the test-slot suffix). The framework's own id tests the framework.</summary>
        public const string Mod = "JB_TEST_MOD";
        /// <summary>Name of the save this game was loaded from (two-phase runs), else unset.</summary>
        public const string Load = "JB_TEST_LOAD";
        /// <summary>Where kit.settingsdump writes its JSON.</summary>
        public const string Dump = "JB_TEST_DUMP";
        /// <summary>Copy of the old (pre-framework) settings file the slot profile started with (kit.migration).</summary>
        public const string Migration = "JB_TEST_MIGRATION";
        /// <summary>Red errors recorded by the probe mod (tools/probe), one per line.</summary>
        public const string ProbeErr = "JB_PROBE_ERR";
        /// <summary>Folder for screenshots of kit.window (set "1" or a folder; tools/jbmod passes a scratch folder).</summary>
        public const string Shots = "JB_UI_SHOTS";
        /// <summary>Other local mods of this run (packageIds without slot suffix, comma separated): fw.* suites find their tenants here.</summary>
        public const string Extra = "JB_TEST_EXTRA";

        /// <summary>An environment variable or null.</summary>
        public static string Get(string k) { try { var v = Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? null : v; } catch { return null; } }

        /// <summary>True when JB_TEST is set.</summary>
        public static bool Active => Get(Test) != null;

        /// <summary>packageId without Workshop postfix and test-slot suffix, lower case.</summary>
        public static string Plain(string pid) => Regex.Replace(KitCompat.Strip(pid ?? "").ToLowerInvariant(), @"\.slot\d+$", "");

        /// <summary>Any context (dependent mod or the framework) by packageId, slot suffix ignored; null if not loaded.</summary>
        public static KitMod Find(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return null;
            string want = Plain(packageId);
            return Framework.AllContexts().FirstOrDefault(k => Plain(k.PackageId) == want);
        }

        /// <summary>The context of the mod under test: JB_TEST_MOD, or the framework itself when unset.</summary>
        public static KitMod Target => Find(Get(Mod)) ?? (Get(Mod) == null ? Framework.Self : null);
    }

    /// <summary>The test mod's Mod class: starts recording red errors as early as possible. No settings entry.</summary>
    public sealed class TestingMod : Mod
    {
        /// <summary>Created by RimWorld.</summary>
        public TestingMod(ModContentPack content) : base(content)
        {
            KitTestLog.Install();
            if (KitTestEnv.Active) Log.Message("[JBTEST] harness loaded; test run requested: " + KitTestEnv.Get(KitTestEnv.Test) + " for " + (KitTestEnv.Get(KitTestEnv.Mod) ?? "(framework)"));
        }
    }

    /// <summary>
    /// The in-game test runner (one GameComponent of the harness assembly). Acts only when JB_TEST is set: closes dialogs (and remembers
    /// which), runs the game at top speed, runs the selected suites one after the other against the mod under test, writes the result
    /// file and quits the game. Never in a save (SaveLoadKit leaves it out) and never in a release (the harness is a separate local mod).
    /// </summary>
    public sealed class KitTestRunner : GameComponent
    {
        sealed class RunState { public KitTestSuite Suite; public IEnumerator<int> It; public TestContext Ctx; public int Started; public int WaitUntil; public bool Done; public string Error; }
        readonly List<RunState> runs = new List<RunState>();
        int current = -1, startDelay = 300;
        bool finished, written;
        int logErrorsAtStart, redMarkAtStart;
        readonly List<string> unknownSuites = new List<string>();
        KitMod target;

        /// <summary>Text of every window the runner closed ("Type: text"), for suites that check that a dialog was shown.</summary>
        internal static readonly List<string> ClosedWindows = new List<string>();

        /// <summary>Created by RimWorld for every game.</summary>
        public KitTestRunner(Game game) { }

        internal static bool Active => KitTestEnv.Active;

        /// <summary>Takes the after-load fingerprint for two-phase suites.</summary>
        public override void LoadedGame()
        {
            if (!Active || SaveLoadKit.LoadedSave == null) return;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try { SaveLoadKit.AfterLoad = SaveFingerprint.Take(KitTestEnv.Target); SaveLoadKit.AfterLoadTick = Find.TickManager.TicksGame; }
                catch (Exception e) { Log.Error("[JBTEST] fingerprint after load failed: " + e); }
            });
        }

        /// <summary>Closes dialogs, keeps the speed up, writes the results at the end.</summary>
        public override void GameComponentUpdate()
        {
            if (!Active) return;
            CloseDialogs();
            var tm = Find.TickManager;
            if (tm != null && !finished && (tm.Paused || tm.CurTimeSpeed != TimeSpeed.Ultrafast)) tm.CurTimeSpeed = TimeSpeed.Ultrafast;
            if (finished && !written) { written = true; WriteResults(); LongEventHandler.ExecuteWhenFinished(() => Root.Shutdown()); }
        }

        internal static void CloseDialogs()
        {
            var ws = Find.WindowStack;
            if (ws == null) return;
            foreach (var w in ws.Windows.ToList())
                if (w is Dialog_MessageBox || w is Dialog_NodeTree || (w.forcePause && !(w is Page)))
                {
                    string text = "";
                    try { if (w is Dialog_MessageBox mb) text = (mb.title ?? "") + " | " + mb.text.ToString(); } catch { }
                    lock (ClosedWindows) ClosedWindows.Add(w.GetType().Name + ": " + text);
                    Log.Message("[JBTEST] closing window " + w.GetType().Name);
                    ws.TryRemove(w, false);
                }
        }

        /// <summary>Drives the selected suites.</summary>
        public override void GameComponentTick()
        {
            if (!Active || finished) return;
            int now = Find.TickManager.TicksGame;
            if (runs.Count == 0 && current == -1)
            {
                if (--startDelay > 0) return;
                target = KitTestEnv.Target;
                SelectSuites();
                logErrorsAtStart = LogErrors();
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

        /// <summary>Nothing is saved (the runner is left out of saves anyway).</summary>
        public override void ExposeData() { }

        /// <summary>Errors written through the logs of the framework and the mod under test.</summary>
        int LogErrors() => (Framework.Self?.Log.ErrorCount ?? 0) + (target != null && !target.IsFramework ? target.Log.ErrorCount : 0);

        /// <summary>Suites in the harness and in the assemblies of the mod under test.</summary>
        static List<Assembly> SuiteAssemblies(KitMod k)
        {
            var list = new List<Assembly> { typeof(KitTestRunner).Assembly };
            if (k == null || k.IsFramework) return list;
            foreach (var a in k.Content?.assemblies?.loadedAssemblies ?? new List<Assembly>()) if (a != null && !list.Contains(a)) list.Add(a);
            if (k.Mod != null && !list.Contains(k.Mod.GetType().Assembly)) list.Add(k.Mod.GetType().Assembly);
            return list;
        }

        void SelectSuites()
        {
            var wanted = (KitTestEnv.Get(KitTestEnv.Test) ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var all = new List<KitTestSuite>();
            if (target == null) unknownSuites.Add("mod under test '" + KitTestEnv.Get(KitTestEnv.Mod) + "' is not loaded (no framework context)");
            foreach (var a in SuiteAssemblies(target))
                foreach (var t in KitPatches.SafeTypes(a).Where(t => typeof(KitTestSuite).IsAssignableFrom(t) && !t.IsAbstract))
                {
                    try { all.Add((KitTestSuite)Activator.CreateInstance(t, true)); }
                    catch (Exception e) { unknownSuites.Add(t.Name + " (could not be created: " + e.Message + ")"); }
                }
            all = all.GroupBy(s => s.Name).Select(g => g.First()).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
            bool Picked(KitTestSuite s)
            {
                if (wanted.Contains(s.Name)) return true;
                if (wanted.Any(w => w.EndsWith(".*") && s.Name.StartsWith(w.Substring(0, w.Length - 1), StringComparison.Ordinal)))
                    return !s.ExplicitOnly || !s.Name.StartsWith("kit.", StringComparison.Ordinal);
                if (s.ExplicitOnly) return false;
                if (wanted.Contains("all")) return true;
                return wanted.Any(w => w.StartsWith("section:") && s.Section == w.Substring(8));
            }
            foreach (var s in all.Where(Picked)) runs.Add(new RunState { Suite = s, Ctx = new TestContext(s.Name, target) });
            // a misspelled suite name must fail the run, not silently pass with fewer suites
            foreach (var w in wanted)
                if (w != "all" && !w.EndsWith(".*") && !w.StartsWith("section:") && !all.Any(s => s.Name == w)) unknownSuites.Add(w + " (unknown suite name)");
            Log.Message($"[JBTEST] {target?.Name}: starting {runs.Count} suites: {string.Join(", ", runs.Select(r => r.Suite.Name))} (available: {string.Join(", ", all.Select(s => s.Name))})");
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
            int newLogErrors = LogErrors() - logErrorsAtStart;
            var oursRed = KitTestLog.Since(redMarkAtStart, true);
            bool pass = fails == 0 && runs.Count > 0 && newLogErrors == 0 && oursRed.Count == 0;
            sb.AppendLine($"RESULT {(pass ? "PASS" : "FAIL")} mod={target?.PackageId} suites={runs.Count} checks={checks} failures={fails} logErrors={newLogErrors} ourRedErrors={oursRed.Count} unknownSuites={unknownSuites.Count}");
            sb.AppendLine($"{Framework.Name} {Framework.Version} (API level {Framework.ApiLevel}) · {target?.Name} {target?.Version} · tenants: {string.Join(", ", Framework.Mods.Select(m => m.Name))}");
            foreach (var u in unknownSuites) sb.AppendLine($"## {u}: FAIL");
            foreach (var e in oursRed.Take(30)) sb.AppendLine("  our red error during the suites: " + KitTestLog.Format(e));
            if (newLogErrors > 0)
                foreach (var k in new[] { Framework.Self, target }.Where(x => x != null).Distinct())
                    foreach (var l in k.Log.RecentSnapshot().Where(l => l.Contains("| ERROR |")).Take(20)) sb.AppendLine("  log error (" + k.Name + "): " + l.Split('\n')[0]);
            var probe = SaveLoadKit.ProbeErrors();
            if (KitTestEnv.Get(KitTestEnv.ProbeErr) != null) sb.AppendLine($"red errors in the game log (probe, all mods): {probe.Count}");
            foreach (var r in runs)
            {
                sb.AppendLine($"## {r.Suite.Name}: {(r.Ctx.Failures == 0 ? "PASS" : "FAIL")} ({r.Ctx.Checks} checks, {r.Ctx.Failures} failures)");
                foreach (var l in r.Ctx.Lines) sb.AppendLine(l);
            }
            string outPath = KitTestEnv.Get(KitTestEnv.Out);
            if (string.IsNullOrEmpty(outPath)) outPath = Path.Combine(GenFilePaths.ConfigFolderPath, "jb_test_results.txt");
            try { File.WriteAllText(outPath, sb.ToString()); } catch (Exception e) { Log.Error("[JBTEST] could not write results: " + e.Message); }
            Log.Message("[JBTEST] " + sb.ToString().Split('\n')[0]);
        }
    }
}

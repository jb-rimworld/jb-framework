// SPDX-License-Identifier: MIT
// fw.perf: what the framework costs per tick and per frame with its tenants (steward pass 2, release check d).
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace JBFramework.Testing
{
    /// <summary>
    /// fw.perf (explicit): times the framework's game component (tick: caretaker checks + every tenant's OnTick; frame: change delivery,
    /// component drops, welcome-card check) and the hot read path mods call from patches (On, Get, GetBool), with every tenant that is
    /// loaded. Notes the numbers in microseconds and checks generous ceilings (the cost must be negligible next to a 16 ms frame).
    /// </summary>
    internal sealed class FwPerfSuite : KitTestSuite
    {
        public override string Name => "fw.perf";
        public override int MaxTicks => 6000;
        public override bool ExplicitOnly => true;

        static double Us(Stopwatch sw, int n) => sw.Elapsed.TotalMilliseconds * 1000.0 / n;

        public override IEnumerable<int> Run(TestContext t)
        {
            var fg = FrameworkGame.Instance;
            t.Check(fg != null, "the framework's game component runs");
            if (fg == null) yield break;
            var tenants = Framework.Mods.Where(k => !k.Blocked).ToList();
            t.Note("tenants: " + string.Join(", ", tenants.Select(k => k.Name)) + " (" + tenants.Count + ")");
            yield return 60;

            const int N = 20000;
            for (int i = 0; i < 500; i++) fg.GameComponentTick();   // warm up (JIT)
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < N; i++) fg.GameComponentTick();
            sw.Stop();
            double tick = Us(sw, N);

            for (int i = 0; i < 500; i++) { fg.GameComponentUpdate(); fg.GameComponentOnGUI(); }
            sw = Stopwatch.StartNew();
            for (int i = 0; i < N; i++) { fg.GameComponentUpdate(); fg.GameComponentOnGUI(); }
            sw.Stop();
            double frame = Us(sw, N);

            // the hot path of a Harmony patch: section switch and a setting read
            var k = tenants.FirstOrDefault(x => x.Sections.All.Count > 0 && x.SettingsReg.All.Count > 0);
            double read = 0;
            if (k != null)
            {
                string sec = k.Sections.All[0].Id, key = k.SettingsReg.All[0].Key;
                float sink = 0;
                for (int i = 0; i < 1000; i++) { if (k.On(sec)) sink += k.Get(key); }
                const int M = 200000;
                sw = Stopwatch.StartNew();
                for (int i = 0; i < M; i++) { if (k.On(sec)) sink += k.Get(key); }
                sw.Stop();
                read = Us(sw, M);
                t.Note("read path of " + k.Name + ": On(\"" + sec + "\") + Get(\"" + key + "\") = " + read.ToString("0.000") + " µs per call pair (" + sink.ToString("0") + ")");
            }
            t.Note("per tick: " + tick.ToString("0.000") + " µs (all tenants' OnTick and caretaker checks included); 60 ticks/s at speed 1, ~900/s at top speed = "
                   + (tick * 900 / 1000.0).ToString("0.000") + " ms per second");
            t.Note("per frame: " + frame.ToString("0.000") + " µs (update + OnGUI of the framework's component) = " + (frame / 16667.0 * 100).ToString("0.0000") + " % of a 60 fps frame");
            t.Check(tick < 25.0, "per-tick cost below 25 µs (" + tick.ToString("0.000") + " µs)");
            t.Check(frame < 25.0, "per-frame cost below 25 µs (" + frame.ToString("0.000") + " µs)");
            if (k != null) t.Check(read < 2.0, "On + Get below 2 µs (" + read.ToString("0.000") + " µs)");
            KitSuiteUtil.NoRedErrors(t, 0, "no red error of ours");
        }
    }
}

namespace JBFramework.Testing
{
    /// <summary>
    /// fw.modspage (explicit): the framework's own settings window (Mods, General, Diagnosis, About) with the tenants of the run, at
    /// 1280x720 and 1920x1080, screenshots fw_modspage_&lt;size&gt;_&lt;page&gt;.png; checks that every tenant is listed and nothing threw.
    /// </summary>
    internal sealed class FwModsPageSuite : KitTestSuite
    {
        public override string Name => "fw.modspage";
        public override int MaxTicks => 20000;
        public override bool ExplicitOnly => true;

        public override IEnumerable<int> Run(TestContext t)
        {
            var self = Framework.Self;
            t.Check(self != null, "the framework's context exists");
            if (self == null) yield break;
            bool sup = KitFirstStartCard.Suppressed; KitFirstStartCard.Suppressed = true;
            int g0 = self.Guard.TotalFailures, l0 = self.Log.ErrorCount, red0 = KitTestLog.Mark();
            string report = Framework.DiagnosisReport();
            foreach (var k in Framework.Mods) t.Check(report.Contains(k.Name), "the combined report names " + k.Name);
            foreach (int h in new[] { 720, 1080 })
            {
                using (t.ScreenHeight(h))
                {
                    foreach (var y in t.Frames(3)) yield return y;
                    var dlg = KitUiTest.NoPause(new RimWorld.Dialog_ModSettings(self.Mod));
                    Find.WindowStack.Add(dlg);
                    foreach (string page in new[] { "page:mods", "general", "diagnosis", "about" })
                    {
                        self.Window.SelectPage(self, page);
                        foreach (var y in t.Frames(3)) yield return y;
                        t.Shot("fw_modspage_" + UI.screenWidth + "x" + UI.screenHeight + "_" + page.Replace(":", "_"));
                        foreach (var y in t.Frames(2)) yield return y;
                        t.Check(self.Window.Last.Page == page, h + ": " + page + " drawn");
                    }
                    dlg.Close(false);
                    foreach (var y in t.Frames(2)) yield return y;
                }
            }
            KitFirstStartCard.Suppressed = sup;
            t.Check(self.Guard.TotalFailures == g0 && self.Log.ErrorCount == l0, "nothing threw");
            t.CheckNoRedErrors(red0, "no red error of ours");
        }
    }
}

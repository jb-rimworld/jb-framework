// SPDX-License-Identifier: MIT
// JB.Kit sample B: tier core with the test harness, per save, a GameComponent with its own saved fields and one own suite.
// The test bed of src/Testing and tools/jbmod (test, plain, saveload, removal, addition, migrate-test).
using System.Collections.Generic;
using JBKit;
using Verse;

namespace KitSampleB
{
    /// <summary>The mod class. Its type name decides the settings file name (Mod_&lt;folder&gt;_KitSampleBMod.xml): never rename it.</summary>
    public sealed class KitSampleBMod : KitModBase
    {
        public KitSampleBMod(ModContentPack content) : base(content, new KitInfo
        {
            Name = "JB.Kit Sample B",
            ShortId = "KSB",
            KeyPrefix = "KSB",
            Version = "1.0.0",
            PerSave = true,
            LicenseLine = "MIT",
        }) { }

        internal override void Register(KitRegistry r)
        {
            r.Section("gamma", "Gamma", "The counter area: adds the daily bonus to the save's counter every 2500 ticks.", legacyEnabled: "Gamma_enabled");
            r.Int("gamma", "Counter", "ksb.dailyBonus", 1, 0, 10, "Bonus per step", "How much the counter grows every 2500 ticks.", legacyScribe: "dailyBonus");
            r.Float("gamma", "Counter", "ksb.growth", 1f, 0.1f, 5f, "Growth", "A plain decimal number (migrated from the old field 'growth').", legacyScribe: "growth");
            r.Bool(KitSections.Core, "General", "ksb.loud", false, "Loud log", "Writes a log line at every counter step.", legacyScribe: "loud");
            r.Percent(KitSections.Core, "General", "ksb.share", 0.5f, 0f, 1f, "A share", "Stored as 0..1, shown as percent.");
            r.Preset("quiet", "Quiet", "Bonus 0, loud log off.", p => { p.Set("ksb.dailyBonus", 0f); p.Set("ksb.loud", false); });
        }

        internal override IEnumerable<string> Diagnosis()
        {
            yield return "Sample B: counter " + (KitSampleBGame.Running?.counter.ToString() ?? "-") + ", gamma " + (Kit.On("gamma") ? "on" : "off");
        }
    }

    /// <summary>Settings class (keeps the pre-kit name; the old flat fields are migrated through legacyScribe/legacyEnabled).</summary>
    public sealed class KitSampleBSettings : KitSettingsBase { }

    /// <summary>The per-save component. The Game constructor is required by RimWorld.</summary>
    public sealed class KitSampleBGame : KitGameBase
    {
        public int counter;
        public List<string> history = new List<string>();

        public KitSampleBGame(Game game) : base(game) { }

        internal static KitSampleBGame Running => Instance as KitSampleBGame;

        internal override void ExposeExtra()
        {
            Scribe_Values.Look(ref counter, "counter");
            Scribe_Collections.Look(ref history, "history", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && history == null) history = new List<string>();
        }

        internal override void Tick(int ticksGame)
        {
            if (ticksGame % 2500 != 0 || !Kit.On("gamma")) return;
            counter += Kit.GetInt("ksb.dailyBonus");
            history.Add(ticksGame + ":" + counter);
            if (history.Count > 5) history.RemoveAt(0);
            if (Kit.GetBool("ksb.loud")) KitLog.Info("Sample", "counter " + counter);
        }
    }

#if JB_TESTS
    /// <summary>The test runner (only in test builds).</summary>
    public sealed class KitSampleBTests : JBKit.Testing.KitTestRunnerBase
    {
        public KitSampleBTests(Game game) : base(game) { }
    }

    /// <summary>ksb.counter: the counter grows by the bonus while Gamma is on and stands still while it is off.</summary>
    sealed class CounterSuite : JBKit.Testing.KitTestSuite
    {
        public override string Name => "ksb.counter";
        public override string Section => "gamma";
        public override int MaxTicks => 9000;

        public override IEnumerable<int> Run(JBKit.Testing.TestContext t)
        {
            var g = KitSampleBGame.Running;
            t.Check(g != null, "the sample's game component runs");
            if (g == null) yield break;
            var p = Kit.Current;
            bool wasOn = p.SectionOn("gamma");
            float wasBonus = Kit.Get("ksb.dailyBonus");
            p.SetSection("gamma", true);
            Kit.Set("ksb.dailyBonus", 3f);
            int c0 = g.counter;
            yield return 2600;
            t.Check(g.counter == c0 + 3, "on: one step of +3 (" + c0 + " → " + g.counter + ")");
            p.SetSection("gamma", false);
            int c1 = g.counter;
            yield return 2600;
            t.Check(g.counter == c1, "off: the counter stands still (" + c1 + " → " + g.counter + ")");
            p.SetSection("gamma", wasOn);
            Kit.Set("ksb.dailyBonus", wasBonus);
        }
    }
#endif
}

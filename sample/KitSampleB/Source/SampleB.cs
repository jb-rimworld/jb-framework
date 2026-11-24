// SPDX-License-Identifier: MIT
// JB Framework sample B: per save, a counter stored through KitRegistry.SaveData (inside the framework's game component, so removing
// the mod leaves no missing-class error), legacy migration of an old settings file, one own test suite.
// The test bed of the harness (JBFramework.Testing) and tools/jbmod (test, plain, saveload, removal, addition, migrate-test).
using System.Collections.Generic;
using JBFramework;
using Verse;

namespace KitSampleB
{
    /// <summary>The mod class. Its type name decides the settings file name (Mod_&lt;folder&gt;_KitSampleBMod.xml): never rename it.</summary>
    public sealed class KitSampleBMod : KitModBase
    {
        public KitSampleBMod(ModContentPack content) : base(content, new KitInfo
        {
            Name = "JB Framework Sample B",
            ShortId = "KSB",
            KeyPrefix = "KSB",
            Version = "1.0.0",
            PerSave = true,
            LicenseLine = "MIT",
        }) { }

        protected override void Register(KitRegistry r)
        {
            r.Section("gamma", "Gamma", "The counter area: adds the daily bonus to the save's counter every 2500 ticks.", legacyEnabled: "Gamma_enabled");
            r.Int("gamma", "Counter", "ksb.dailyBonus", 1, 0, 10, "Bonus per step", "How much the counter grows every 2500 ticks.", legacyScribe: "dailyBonus");
            r.Float("gamma", "Counter", "ksb.growth", 1f, 0.1f, 5f, "Growth", "A plain decimal number (migrated from the old field 'growth').", legacyScribe: "growth");
            r.Bool(KitMod.Core, "General", "ksb.loud", false, "Loud log", "Writes a log line at every counter step.", legacyScribe: "loud");
            r.Percent(KitMod.Core, "General", "ksb.share", 0.5f, 0f, 1f, "A share", "Stored as 0..1, shown as percent.");
            r.Preset("quiet", "Quiet", "Bonus 0, loud log off.", p => { p.Set("ksb.dailyBonus", 0f); p.Set("ksb.loud", false); });
            r.SaveData(SampleBData.Expose, SampleBData.Reset);
        }

        protected override void OnTick(int ticksGame)
        {
            if (ticksGame % 2500 != 0 || !Kit.On("gamma")) return;
            SampleBData.counter += Kit.GetInt("ksb.dailyBonus");
            SampleBData.history.Add(ticksGame + ":" + SampleBData.counter);
            if (SampleBData.history.Count > 5) SampleBData.history.RemoveAt(0);
            if (Kit.GetBool("ksb.loud")) Kit.Log.Info("Sample", "counter " + SampleBData.counter);
        }

        protected override IEnumerable<string> Diagnosis()
        {
            yield return "Sample B: counter " + SampleBData.counter + ", gamma " + (Kit.On("gamma") ? "on" : "off");
        }
    }

    /// <summary>Settings class (keeps the pre-framework name; the old flat fields are migrated through legacyScribe/legacyEnabled).</summary>
    public sealed class KitSampleBSettings : KitSettingsBase { }

    /// <summary>The mod's per-save data: plain static fields, saved inside the framework's game component under this mod's packageId.</summary>
    internal static class SampleBData
    {
        public static int counter;
        public static List<string> history = new List<string>();

        public static void Reset() { counter = 0; history = new List<string>(); }

        public static void Expose()
        {
            Scribe_Values.Look(ref counter, "counter");
            Scribe_Collections.Look(ref history, "history", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && history == null) history = new List<string>();
        }
    }

#if JB_TESTS
    // Ported to the framework API; the harness types (JBFramework.Testing) come from the test assembly the harness agent builds.
    /// <summary>ksb.counter: the counter grows by the bonus while Gamma is on and stands still while it is off.</summary>
    sealed class CounterSuite : JBFramework.Testing.KitTestSuite
    {
        public override string Name => "ksb.counter";
        public override string Section => "gamma";
        public override int MaxTicks => 9000;

        public override IEnumerable<int> Run(JBFramework.Testing.TestContext t)
        {
            var k = Framework.Of<KitSampleBMod>();
            t.Check(k != null, "the sample's context exists");
            if (k == null) yield break;
            var p = k.Current;
            bool wasOn = p.SectionOn("gamma");
            float wasBonus = k.Get("ksb.dailyBonus");
            p.SetSection("gamma", true);
            k.Set("ksb.dailyBonus", 3f);
            int c0 = SampleBData.counter;
            yield return 2600;
            t.Check(SampleBData.counter == c0 + 3, "on: one step of +3 (" + c0 + " → " + SampleBData.counter + ")");
            p.SetSection("gamma", false);
            int c1 = SampleBData.counter;
            yield return 2600;
            t.Check(SampleBData.counter == c1, "off: the counter stands still (" + c1 + " → " + SampleBData.counter + ")");
            p.SetSection("gamma", wasOn);
            k.Set("ksb.dailyBonus", wasBonus);
        }
    }
#endif
}

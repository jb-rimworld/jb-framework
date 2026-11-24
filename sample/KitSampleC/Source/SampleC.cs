// SPDX-License-Identifier: MIT
// JB Framework sample C: a mod that does not use the framework. Its release build references nothing of the framework; its test build
// (tools/jbmod passes -p:JBTests=true, build/JBTests.props) compiles the suites below against JBFramework.Testing. No stand-in KitModBase.
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace KitSampleC
{
    /// <summary>A plain Verse.Mod with its own Harmony patch.</summary>
    public sealed class KitSampleCMod : Mod
    {
        internal static int Ticks;
        public KitSampleCMod(ModContentPack content) : base(content) { new Harmony("juliusberger00.jbkitsamplec").PatchAll(typeof(KitSampleCMod).Assembly); }
    }

    /// <summary>Counts game ticks (the suite checks that the mod's own patch runs).</summary>
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    static class Patch_CountTicks
    {
        static void Postfix() => KitSampleCMod.Ticks++;
    }

#if JB_TESTS
    /// <summary>ksc.basics: the harness runs a frameworkless mod's suite; TestContext.Mod is null, Content is the mod.</summary>
    sealed class BasicsSuite : JBFramework.Testing.KitTestSuite
    {
        public override string Name => "ksc.basics";
        public override int MaxTicks => 3000;

        public override IEnumerable<int> Run(JBFramework.Testing.TestContext t)
        {
            t.Check(t.Mod == null, "no framework context (TestContext.Mod is null)");
            t.Check(t.Content != null && t.Content.PackageId.StartsWith("juliusberger00.jbkitsamplec"), "TestContext.Content is the mod: " + t.Content?.PackageId);
            t.Check(DefDatabase<ThingDef>.GetNamedSilentFail("KSC_Pebble") != null, "the mod's def loaded");
            bool bio = ModsConfig.BiotechActive;
            t.Check((DefDatabase<ThingDef>.GetNamedSilentFail("KSC_BiotechPebble") != null) == bio, "the conditional Biotech folder loaded exactly with Biotech (" + bio + ")");
            int mark = t.Mark(), n0 = KitSampleCMod.Ticks;
            yield return 120;
            t.Check(KitSampleCMod.Ticks >= n0 + 100, "the mod's own Harmony patch runs (" + (KitSampleCMod.Ticks - n0) + " ticks counted)");
            t.CheckNoRedErrors(mark, "no red error of the mod");
        }
    }
#endif
}

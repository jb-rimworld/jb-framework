// SPDX-License-Identifier: MIT
// JB.Kit sample A: the smallest complete adoption (tier core, per save). Living example for README/API.md and the kit.identity test.
using System.Collections.Generic;
using JBKit;
using Verse;

namespace KitSampleA
{
    public enum SampleMode { Gentle = 0, Normal = 1, Wild = 2 }

    /// <summary>The mod class. Its type name decides the settings file name (Mod_&lt;folder&gt;_KitSampleAMod.xml): never rename it.</summary>
    public sealed class KitSampleAMod : KitModBase
    {
        public KitSampleAMod(ModContentPack content) : base(content, new KitInfo
        {
            Name = "JB.Kit Sample A",
            ShortId = "KSA",
            KeyPrefix = "KSA",
            Version = "1.0.0",
            PerSave = true,
            LicenseLine = "MIT",
        }) { }

        internal override void Register(KitRegistry r)
        {
            r.Section("alpha", "Alpha", "The first sample area. On by default.");
            r.SectionFor("ludeon.rimworld.biotech", "Biotech", "beta", "Beta (Biotech)", "Exists only with Biotech. Off by default.", defaultOn: false);

            r.Bool("alpha", "Switches", "ksa.flag", true, "A flag", "A plain on/off value.", legacyScribe: "oldFlag");
            r.Int("alpha", "Numbers", "ksa.count", 5, 0, 20, "A count", "Whole numbers from 0 to 20 on the slider, up to 1000 typed.", hardMax: 1000);
            r.Float("alpha", "Numbers", "ksa.speed", 1f, 0.1f, 5f, "A speed", "A plain decimal number.", legacyScribe: "oldSpeed");
            r.Percent("alpha", "Numbers", "ksa.chance", 0.25f, 0f, 1f, "A chance", "Stored as 0..1, shown as percent.");
            r.LogFloat("beta", "Scale", "ksa.factor", 1f, 0.1f, 100f, "A factor", "Log-scale slider from 0.1 to 100.", nerd: true);
            r.Enum("beta", "Scale", "ksa.mode", SampleMode.Normal, "A mode", "One of three named choices.");
            r.Silver(KitSections.Core, "Economy", "ksa.price", 100, 0, 1000, "A price", "Whole silver; typed values stop at 10 × the silver stack limit.")
                .RequiresOn("alpha", "Needs the Alpha section.");

            r.Preset("calm", "Calm", "Low numbers, Beta off.", p => { p.Set("ksa.count", 2f); p.Set("ksa.speed", 0.5f); p.SetSection("beta", false); });
            r.Preset("loud", "Loud", "High numbers, Beta on.", p => { p.Set("ksa.count", 15f); p.Set("ksa.speed", 3f); p.SetSection("beta", true); });

            r.Compat("Biotech", "ludeon.rimworld.biotech", "Unlocks the Beta section.");
            r.Compat("Missing Sample Mod", "juliusberger00.jbkit.missing", "Patch class Patch_MissingMod is applied only when this mod is active.");
        }

        internal override void OnSettingsChanged(string key) => KitLog.Debug("Sample", "Changed: " + key);

        internal override IEnumerable<string> Diagnosis()
        {
            yield return "Sample A: ksa.count = " + Kit.GetInt("ksa.count") + ", alpha " + (Kit.On("alpha") ? "on" : "off");
        }
    }

    /// <summary>Settings class: keep the old class name when adopting the kit. Extra fields go through ExposeExtra.</summary>
    public sealed class KitSampleASettings : KitSettingsBase
    {
        public string note = "";
        internal override void ExposeExtra() => Scribe_Values.Look(ref note, "note", "");
    }

    /// <summary>The per-save component (PerSave = true). The Game constructor is required by RimWorld.</summary>
    public sealed class KitSampleAGame : KitGameBase
    {
        public KitSampleAGame(Game game) : base(game) { }
    }

    /// <summary>Soft integration example: skipped silently (not a failure) because the named mod is not active.</summary>
    [HarmonyLib.HarmonyPatch]
    [KitCompat("juliusberger00.jbkit.missing")]
    static class Patch_MissingMod
    {
        static System.Reflection.MethodBase TargetMethod() => HarmonyLib.AccessTools.Method("MissingMod.Type:Method");
        static void Postfix() { }
    }
}

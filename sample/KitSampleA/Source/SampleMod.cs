// SPDX-License-Identifier: MIT
// JB Framework sample A: a complete dependent mod (per save, theme, sections, every setting kind, presets, compatibility entries,
// a custom page, the welcome card). Living example for README.md and API.md.
using System.Collections.Generic;
using JBFramework;
using UnityEngine;
using Verse;

namespace KitSampleA
{
    public enum SampleMode { Gentle = 0, Normal = 1, Wild = 2 }

    /// <summary>The mod class. Its type name decides the settings file name (Mod_&lt;folder&gt;_KitSampleAMod.xml): never rename it.</summary>
    public sealed class KitSampleAMod : KitModBase
    {
        public KitSampleAMod(ModContentPack content) : base(content, new KitInfo
        {
            Name = "JB Framework Sample A",
            ShortId = "KSA",
            KeyPrefix = "KSA",
            Version = "1.0.0",
            PerSave = true,
            LicenseLine = "MIT",
            FirstStartPitch = "A sample of the JB Framework window. Pick a start below; everything can be changed later in Mod settings.",
            Theme = new KitTheme
            {
                Accent = new Color(0.36f, 0.62f, 0.86f),        // steel blue
                MarkTex = "UI/KSA/Mark",                         // not shipped: the window must simply draw no mark
                Icons = { ["page:notes"] = "UI/KSA/Notes" },     // not shipped either: no icon, no pink square
            },
        }) { }

        protected override void Register(KitRegistry r)
        {
            r.Section("alpha", "Alpha", "The first sample area. On by default.");
            r.SectionFor("ludeon.rimworld.biotech", "Biotech", "beta", "Beta (Biotech)", "Exists only with Biotech. Off by default.", defaultOn: false);
            r.SectionFor("ceteam.combatextended", "Combat Extended", "gamma", "Gamma (Combat Extended)", "Exists only with Combat Extended: shows the \"needs X (not installed)\" row.");
            r.Section("delta", "Delta", "A section with a removal step and a diagnosis line.",
                diagnosis: () => new[] { "Delta diagnosis line" },
                prepareRemoval: report => report.Add("Delta: nothing was placed in this save (sample)."));

            r.Bool("alpha", "Switches", "ksa.flag", true, "A flag", "A plain on/off value.", legacyScribe: "oldFlag");
            r.Int("alpha", "Numbers", "ksa.count", 5, 0, 20, "A count", "Whole numbers from 0 to 20 on the slider, up to 1000 typed.", hardMax: 1000);
            r.Float("alpha", "Numbers", "ksa.speed", 1f, 0.1f, 5f, "A speed", "A plain decimal number.", legacyScribe: "oldSpeed");
            r.Percent("alpha", "Numbers", "ksa.chance", 0.25f, 0f, 1f, "A chance", "Stored as 0..1, shown as percent.");
            r.LogFloat("beta", "Scale", "ksa.factor", 1f, 0.1f, 100f, "A factor", "Log-scale slider from 0.1 to 100.", nerd: true);
            r.Enum("beta", "Scale", "ksa.mode", SampleMode.Normal, "A mode", "One of three named choices.");
            r.Silver(KitMod.Core, "Economy", "ksa.price", 100, 0, 1000, "A price", "Whole silver; typed values stop at 10 × the silver stack limit.")
                .RequiresOn("alpha", "Needs the Alpha section.");
            r.Bool("delta", "Switches", "ksa.restartFlag", false, "A restart flag", "Shows the \"after restart\" tag.").NeedsRestart();
            r.Float(KitMod.Core, "Interface", "ksa.uiPref", 1f, 0.5f, 2f, "A global preference", "Global: the same in every save, shows the \"all games\" tag.").AsGlobal();

            r.Preset("calm", "Calm", "Low numbers, Beta off.", p => { p.Set("ksa.count", 2f); p.Set("ksa.speed", 0.5f); p.SetSection("beta", false); });
            r.Preset("loud", "Loud", "High numbers, Beta on.", p => { p.Set("ksa.count", 15f); p.Set("ksa.speed", 3f); p.SetSection("beta", true); });

            r.Compat("Biotech", "ludeon.rimworld.biotech", "Unlocks the Beta section.");
            r.Compat("Missing Sample Mod", "juliusberger00.jbkit.missing", "Patch class Patch_MissingMod is applied only when this mod is active.");
            r.Incompatible("Imaginary Conflict", "juliusberger00.jbkit.conflict", "Would fight over the same setting. Use only one of the two.");

            r.Page("notes", "Notes", order: 50, draw: NotesPage.Draw, section: "alpha");
        }

        protected override void OnSettingsChanged(string key) => Kit.Log.Debug("Sample", "Changed: " + key);

        protected override IEnumerable<string> Diagnosis()
        {
            yield return "Sample A: ksa.count = " + Kit.GetInt("ksa.count") + ", alpha " + (Kit.On("alpha") ? "on" : "off");
        }
    }

    /// <summary>A custom page: draws the mod's own (non-numeric) data, here the free text note from the settings file.</summary>
    static class NotesPage
    {
        internal static void Draw(Rect r)
        {
            var st = Framework.Of<KitSampleAMod>()?.Settings as KitSampleASettings;
            if (st == null) return;
            Widgets.Label(new Rect(r.x, r.y, r.width, 26f), "A custom page. This note is saved in the settings file (ExposeExtra):");
            st.note = Widgets.TextArea(new Rect(r.x, r.y + 30f, r.width, Mathf.Min(200f, r.height - 34f)), st.note ?? "");
        }
    }

    /// <summary>Settings class: keep the old class name when adopting the framework. Extra fields go through ExposeExtra.</summary>
    public sealed class KitSampleASettings : KitSettingsBase
    {
        public string note = "";
        protected override void ExposeExtra() => Scribe_Values.Look(ref note, "note", "");
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

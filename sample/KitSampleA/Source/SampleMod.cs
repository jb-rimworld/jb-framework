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
            // sections: plain, only with a DLC (off by default), only with a mod, with a diagnosis line and a removal step
            r.Section("alpha", "Alpha", "The first sample area. On by default.");
            r.Section("beta", "Beta (Biotech)", "Exists only with Biotech. Off by default.").OnlyWith("ludeon.rimworld.biotech", "Biotech").OffByDefault();
            r.Section("gamma", "Gamma (Combat Extended)", "Exists only with Combat Extended: shows the \"needs X (not installed)\" row.").OnlyWith("ceteam.combatextended");
            r.Section("delta", "Delta", "A section with a removal step and a diagnosis line.")
                .WithDiagnosis(() => new[] { "Delta diagnosis line" })
                .WithRemoval(report => report.Add("Delta: nothing was placed in this save (sample)."));

            // one setting of every kind: section, key, default, (slider range,) label, description; then modifiers
            r.Bool("alpha", "ksa.flag", true, "A flag", "A plain on/off value.").InGroup("Switches").Legacy("oldFlag");
            // a value that only matters while another one is on (greyed with "Only while ... is on."), with a live preview line under it
            r.Float("alpha", "ksa.flagBoost", 2f, 1f, 10f, "Flag boost", "Only used while \"A flag\" is on.").InGroup("Switches")
                .RequiresSetting("ksa.flag", true)
                .WithPreview(p => "Result: " + (p.Get("ksa.count") * p.Get("ksa.flagBoost")).ToString("0.#") + " (count × boost)");
            r.Int("alpha", "ksa.count", 5, 0, 20, "A count", "Whole numbers from 0 to 20 on the slider, up to 1000 typed.").InGroup("Numbers").HardLimits(0, 1000);
            r.Float("alpha", "ksa.speed", 1f, 0.1f, 5f, "A speed", "A plain decimal number.").InGroup("Numbers").Legacy("oldSpeed");
            r.Percent("alpha", "ksa.chance", 0.25f, 0f, 1f, "A chance", "Stored as 0..1, shown as percent.").InGroup("Numbers").Legacy("oldChancePercent", v => v / 100f);
            r.LogFloat("beta", "ksa.factor", 1f, 0.1f, 100f, "A factor", "Log-scale slider from 0.1 to 100.").InGroup("Scale").AsNerd();
            r.Enum("beta", "ksa.mode", SampleMode.Normal, "A mode", "One of three named choices.").InGroup("Scale");
            r.Silver(KitMod.Core, "ksa.price", 100, 0, 1000, "A price", "Whole silver; typed values stop at 10 × the silver stack limit.")
                .InGroup("Economy").RequiresOn("alpha", "Needs the Alpha section.");
            r.Bool("delta", "ksa.restartFlag", false, "A restart flag", "Shows the \"after restart\" tag.").InGroup("Switches").NeedsRestart();
            r.Float(KitMod.Core, "ksa.uiPref", 1f, 0.5f, 2f, "A global preference", "Global: the same in every save, shows the \"all games\" tag.").InGroup("Interface").AsGlobal();

            // presets set only what they name; an enum is set as its number
            r.Preset("calm", "Calm", "Low numbers, gentle mode, Beta off.", p => { p.Set("ksa.count", 2f); p.Set("ksa.speed", 0.5f); p.Set("ksa.mode", (float)(int)SampleMode.Gentle); p.SetSection("beta", false); });
            r.Preset("loud", "Loud", "High numbers, wild mode, Beta on.", p => { p.Set("ksa.count", 15f); p.Set("ksa.speed", 3f); p.Set("ksa.mode", (float)(int)SampleMode.Wild); p.SetSection("beta", true); });

            r.Compat("Biotech", "ludeon.rimworld.biotech", "Unlocks the Beta section. Can be switched off here.").Switchable().HealthyWhenType("Verse.Gene");
            // an integration whose patch class finds nothing to patch in the installed version: shown as "inactive: patch target not found"
            r.Compat("Royalty", "ludeon.rimworld.royalty", "Patch_RoyaltyMissingTarget looks for a method this version does not have.");
            // settings that exist only with a DLC or mod: hidden (not greyed) without it
            r.Bool("alpha", "ksa.ideologyFlag", false, "An Ideology flag", "Only shown with Ideology (OnlyWith).").InGroup("Switches").OnlyWith("ludeon.rimworld.ideology");
            r.Bool("alpha", "ksa.hiddenFlag", false, "A hidden flag", "Only with a mod that does not exist: never shown.").InGroup("Switches").OnlyWith("juliusberger00.jbkit.missing");
            r.Compat("Missing Sample Mod", "juliusberger00.jbkit.missing", "Patch class Patch_MissingMod is applied only when this mod is active.").HealthyWhen(() => true);
            r.Incompatible("Imaginary Conflict", "juliusberger00.jbkit.conflict", "Would fight over the same setting. Use only one of the two.");

            r.Page("notes", "Notes", NotesPage.Draw).WithOrder(50).InSection("alpha");
            // a page drawn INSIDE the Alpha section's page (no second sidebar entry), with a list that is the same in every game
            r.Page("alphaList", "Alpha list", rect => { EmbeddedDraws++; KitStyle.Paragraph(rect.x, rect.y, rect.width, "An embedded page (KitPage.EmbedInSection), the same in every game (KitPage.AsGlobal).", KitStyle.Grey); })
                .InSection("alpha").EmbedInSection(60f).AsGlobal();

            // the welcome card: a drawn part above the presets and a one-click action (save state, not a setting)
            r.WelcomeCard()
                .WithDraw(rect => KitStyle.Paragraph(rect.x, rect.y, rect.width, "This line is drawn by the sample itself (KitCard.WithDraw).", KitStyle.Grey))
                .WithAction("mark", "Mark this colony", "Adds a line to this save's list (shown on the Notes page).",
                    () => Kit.GetList("ksa.marks").Count > 0, () => Kit.SetList("ksa.marks", new[] { "marked at tick " + Find.TickManager.TicksGame }))
                .WhenClosed(joined => Kit.Log.Info("Sample", "welcome card closed (joined " + joined + ")"));
        }

        /// <summary>How often the embedded page was drawn (fw.steward2).</summary>
        internal static int EmbeddedDraws;
        /// <summary>Counts how often the settings window closed (fw.steward checks the hook).</summary>
        internal static int WindowClosedCount;
        protected override void OnSettingsWindowClosed() => WindowClosedCount++;

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
            // a per-save list value (KitMod.GetList/SetList): plain strings in the save's profile
            var k = Framework.Of<KitSampleAMod>();
            var marks = k?.GetList("ksa.marks");
            if (marks != null) KitStyle.Paragraph(r.x, r.y + Mathf.Min(240f, r.height - 30f), r.width, "Marks of this save: " + (marks.Count == 0 ? "none" : string.Join(", ", marks)), KitStyle.Grey);
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

    /// <summary>A [KitCompat] class whose target is missing in this version: Prepare() says so, the entry shows "inactive".</summary>
    [HarmonyLib.HarmonyPatch]
    [KitCompat("ludeon.rimworld.royalty")]
    static class Patch_RoyaltyMissingTarget
    {
        static System.Reflection.MethodBase target;
        static bool Prepare() { target = HarmonyLib.AccessTools.Method("RimWorld.Pawn_RoyaltyTracker:MethodThatDoesNotExist"); return target != null; }
        static System.Reflection.MethodBase TargetMethod() => target;
        static void Postfix() { }
    }
}

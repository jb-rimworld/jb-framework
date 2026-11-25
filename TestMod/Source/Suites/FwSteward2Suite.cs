// SPDX-License-Identifier: MIT
// fw.steward2: the additions of the second steward pass (25.11.2026), with sample A as the mod under test and sample B as a tenant.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBFramework.Testing
{
    /// <summary>
    /// fw.steward2 (explicit; jbmod sample/KitSampleA test fw.steward2 &lt;label&gt; sample/KitSampleB): settings hidden without a DLC/mod
    /// (OnlyWith), embedded and global pages, compat entries whose patch class found no target, HealthyWhenType/Def, LegacyInverted and
    /// LegacyDisabled, DLC names in RequiresOn reasons, resets once per Game/Map (legacy merges survive), KeepOpen, OpenWelcomeCard,
    /// ExpectRedError (the probe counts it as expected), RemoveGenes/RemoveThoughts, the German-safe "This save"/"Template" chips.
    /// </summary>
    internal sealed class FwSteward2Suite : KitTestSuite
    {
        public override string Name => "fw.steward2";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 20000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var a = t.Mod; var b = FwUtil.B;
            t.Check(a != null && FwUtil.A == a, "sample A is the mod under test");
            if (a == null) yield break;
            int red0 = t.Mark();
            bool sup = KitFirstStartCard.Suppressed; KitFirstStartCard.Suppressed = true;

            // ---- OnlyWith on a setting: hidden, not greyed; value still readable
            var hidden = a.SettingsReg.Find("ksa.hiddenFlag");
            var ideo = a.SettingsReg.Find("ksa.ideologyFlag");
            t.Check(hidden != null && !hidden.Shown, "ksa.hiddenFlag (OnlyWith a missing mod) is hidden");
            t.Check(ideo != null && ideo.Shown == ModsConfig.IdeologyActive, "ksa.ideologyFlag is shown exactly when Ideology is active (" + ModsConfig.IdeologyActive + ")");
            t.Check(a.GetBool("ksa.hiddenFlag") == false, "a hidden setting is still readable");

            // ---- embedded + global page
            var pages = KitSettingsWindow.BuildPages(a, a.Current);
            t.Check(pages.All(p => p.Id != "page:alphaList") && pages.Any(p => p.Id == "page:notes"), "the embedded page has no sidebar entry; the normal page keeps its own");
            var dlg = KitUiTest.NoPause(new Dialog_ModSettings(a.Mod));
            Find.WindowStack.Add(dlg);
            a.Window.SelectPage(a, "section:alpha");
            int hits0 = KitUiProbe.Count("custompage:alphaList");
            foreach (var y in t.Frames(4)) yield return y;
            t.Check(KitUiProbe.Count("custompage:alphaList") > hits0, "the embedded page is drawn on the Alpha section's page");
            t.Shot("fw_steward2_embedded_" + LanguageDatabase.activeLanguage?.folderName?.Split(' ')[0]);
            foreach (var y in t.Frames(2)) yield return y;
            a.Window.SelectPage(a, "general");
            a.Window.EditTemplate = false;
            foreach (var y in t.Frames(3)) yield return y;
            var chips = a.Window.Last.Chips;
            string l1 = KitText.K("Side.ThisSave", "This save"), l2 = KitText.K("Side.Template", "Template");
            bool stacked = chips.height > 40f;
            float chipW = stacked ? chips.width : (chips.width - 6f) / 2f;
            t.Check(a.HasSaveProfile && chips.width > 0f && KitUI.TextW(l1) + 20f <= chipW && KitUI.TextW(l2) + 20f <= chipW, "the chips \"" + l1 + "\" / \"" + l2 + "\" fit at the normal font (" + (stacked ? "stacked" : "side by side") + ", " + chipW.ToString("0") + " px)");
            using (t.ScreenHeight(720))
            {
                foreach (var y in t.Frames(4)) yield return y;
                t.Shot("fw_steward2_chips_1280x720_" + LanguageDatabase.activeLanguage?.folderName?.Split(' ')[0]);
                foreach (var y in t.Frames(2)) yield return y;
            }
            dlg.Close(false);
            foreach (var y in t.Frames(2)) yield return y;

            // ---- compat: patch class without target, HealthyWhenType
            var roy = a.Compat.Find("ludeon.rimworld.royalty");
            if (ModsConfig.RoyaltyActive)
                t.Check(roy != null && roy.State == KitCompatState.Inactive && roy.StateLabel.Contains("Patch_RoyaltyMissingTarget"), "Royalty entry: " + roy?.StateLabel);
            else t.Check(roy != null && roy.State == KitCompatState.NotInstalled, "Royalty entry without Royalty: " + roy?.StateLabel);
            var bio = a.Compat.Find("ludeon.rimworld.biotech");
            t.Check(bio != null && (!ModsConfig.BiotechActive || bio.State == KitCompatState.Active), "Biotech entry with HealthyWhenType(\"Verse.Gene\"): " + bio?.StateLabel);
            var probe = new KitCompatEntry { Name = "x", PackageId = "brrainz.harmony", Owner = a };
            probe.HealthyWhenType("No.Such.Type");
            t.Check(probe.State == KitCompatState.Unhealthy, "HealthyWhenType of a missing type: unhealthy (" + probe.StateLabel + ")");
            var probe2 = new KitCompatEntry { Name = "y", PackageId = "brrainz.harmony", Owner = a };
            probe2.HealthyWhenDef<StatDef>("MoveSpeed").HealthyWhen(() => true);
            t.Check(probe2.State == KitCompatState.Active, "HealthyWhenDef<StatDef>(\"MoveSpeed\") + HealthyWhen(true): active");

            // ---- legacy shapes
            var tmp = new KitSetting { Kind = KitSettingKind.Bool };
            tmp.LegacyInverted("oldDisable");
            t.Check(tmp.LegacyScribe == "oldDisable" && tmp.LegacyScribeConvert(1f) == 0f && tmp.LegacyScribeConvert(0f) == 1f, "LegacyInverted: True → off, False → on");
            var sec = new KitSection().LegacyDisabled("oldDisableAlpha");
            t.Check(sec.LegacyEnabledField == "oldDisableAlpha" && sec.LegacyEnabledInverted, "LegacyDisabled marks an inverted old switch");
            t.Check(KitSetting.DlcName("ludeon.rimworld.ideology") == "Ideology" && KitSetting.DlcName("x.y") == null, "a DLC packageId reads as its name in RequiresOn reasons");

            // ---- resets once per Game and per Map: a merge from a legacy component survives a second component creation
            if (b != null)
            {
                var counter = FwUtil.Field(b, "KitSampleB.SampleBData", "counter");
                if (counter != null)
                {
                    int was = (int)counter.GetValue(null);
                    counter.SetValue(null, 4242);
                    // what the constructor of a second FrameworkGame (FillComponents after the saved components were read) runs
                    FrameworkGame.ResetOnce(Current.Game);
                    t.Check((int)counter.GetValue(null) == 4242, "a second FrameworkGame for the same Game does not reset B's SaveData (counter " + counter.GetValue(null) + ")");
                    counter.SetValue(null, was);
                }
                var map = t.Map;
                var byMap = FwUtil.Field(b, "KitSampleB.SampleBMaps", "byMap")?.GetValue(null) as System.Collections.IDictionary;
                if (map != null && byMap != null)
                {
                    bool had = byMap.Contains(map.uniqueID);
                    FrameworkMap.ResetOnce(map);   // what a FrameworkMap constructor for an already loaded map runs
                    t.Check(byMap.Contains(map.uniqueID) == had, "a second FrameworkMap for the same Map does not reset B's map data");
                }
            }

            // ---- KeepOpen: a message box stays for a screenshot
            var box = Dialog_MessageBox.CreateConfirmation("fw.steward2: this confirmation stays open for a screenshot (TestContext.KeepOpen).", () => { });
            Find.WindowStack.Add(box); t.KeepOpen(box);
            foreach (var y in t.Frames(4)) yield return y;
            t.Check(box.IsOpen, "a kept message box is still open after 4 frames");
            t.Shot("fw_steward2_keepopen");
            foreach (var y in t.Frames(2)) yield return y;
            box.Close(false);

            // ---- OpenWelcomeCard
            bool doneWas = a.SaveProfile?.FirstStartCardDone ?? false;
            var card = t.OpenWelcomeCard();
            foreach (var y in t.Frames(4)) yield return y;
            t.Check(card != null && card.IsOpen, "OpenWelcomeCard opens the card");
            t.Shot("fw_steward2_welcome");
            foreach (var y in t.Frames(2)) yield return y;
            card?.Close(false);
            if (a.SaveProfile != null) a.SaveProfile.FirstStartCardDone = doneWas;

            // ---- ExpectRedError: a provoked, fenced failure is not ours (here and in jbmod's probe report)
            const string frag = "provoked by fw.steward2 7731";
            t.ExpectRedError(frag);
            int m = t.Mark();
            a.Fail("alpha", "steward2.provoke", new InvalidOperationException(frag));
            yield return 2;
            t.Check(t.RedErrorsSince(m).Count == 0 && KitTestLog.ExpectedCount() > 0, "the provoked error is recorded as expected, not ours");
            foreach (var y in t.Frames(3)) yield return y;
            t.Check(Find.WindowStack.WindowOfType<LudeonTK.EditWindow_Log>() == null, "the log window the expected error opened is closed");
            a.Resume("alpha");

            // ---- removal building blocks on a live map (nothing matches: zero, no error)
            var rep = new List<string>();
            KitRemoval.RemoveGenes(d => d.defName == "NoSuchGene_fw", rep);
            KitRemoval.RemoveThoughts(d => d.defName == "NoSuchThought_fw", rep);
            KitRemoval.RemoveThings(d => d.defName == "NoSuchThing_fw", rep, false);
            t.Check(rep.Count == 3 && rep.All(x => !x.StartsWith("Error")), "RemoveGenes / RemoveThoughts / RemoveThings run: " + string.Join("; ", rep));

            KitFirstStartCard.Suppressed = sup;
            t.CheckNoRedErrors(red0, "no red error of ours");
        }
    }
}

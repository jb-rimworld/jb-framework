// SPDX-License-Identifier: MIT
// fw.steward3: the fixes of the third steward pass (25.11.2026), with sample A as the mod under test and sample B as a tenant.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBFramework.Testing
{
    /// <summary>
    /// fw.steward3 (explicit; jbmod sample/KitSampleA test fw.steward3 &lt;label&gt; sample/KitSampleB): TestContext.SetCompat, KitRemoval
    /// takes a packed thing out of an inventory WITH its package, a save entry without &lt;data&gt; runs the mod's callback in no load pass,
    /// the unavailable-section dash and the measured Diagnosis button drawn without error, the welcome card opens from Update.
    /// </summary>
    internal sealed class FwSteward3Suite : KitTestSuite
    {
        public override string Name => "fw.steward3";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 20000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var a = t.Mod; var b = FwUtil.B;
            t.Check(a != null && FwUtil.A == a && b != null, "sample A under test, sample B as tenant");
            if (a == null || b == null) yield break;
            int red0 = t.Mark();

            // ---- SetCompat (SMD 16): a suite switches an integration like the player's checkbox
            var e = a.Compat.Find("ludeon.rimworld.biotech");
            bool was = e?.SwitchedOn ?? true;
            t.Check(t.SetCompat("ludeon.rimworld.biotech", false) && e != null && !e.SwitchedOn && !a.CompatOn("ludeon.rimworld.biotech"), "SetCompat off: the Biotech entry reads off");
            t.Check(t.SetCompat("ludeon.rimworld.biotech", true) && e.SwitchedOn && a.CompatOn("ludeon.rimworld.biotech") == ModsConfig.BiotechActive, "SetCompat on again");
            t.Check(!t.SetCompat("no.such.mod", false), "SetCompat on an unknown entry returns false");
            if (!was) t.SetCompat("ludeon.rimworld.biotech", false);

            // ---- RemoveThings: a packed bed in a colonist's inventory goes with its package (HBG2 4)
            var p = Find.CurrentMap?.mapPawns?.FreeColonistsSpawned?.FirstOrDefault();
            t.Check(p != null, "a colonist to carry the packed bed");
            if (p != null)
            {
                var bed = ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
                var pkg = bed.MakeMinified();
                bool added = pkg != null && p.inventory.innerContainer.TryAdd(pkg);
                t.Check(added, "a minified bed in " + p.LabelShort + "'s inventory");
                var rep = new List<string>();
                KitRemoval.RemoveThings(d => d == ThingDefOf.Bed, rep, false);
                t.Note("report: " + string.Join(" | ", rep));
                bool empty = p.inventory.innerContainer.Any(x => x is MinifiedThing m && m.InnerThing == null);
                t.Check(pkg.Destroyed && bed.Destroyed && !empty, "package and bed destroyed, no empty package left (package " + pkg.Destroyed + ", bed " + bed.Destroyed + ", empty packages " + empty + ")");
                int mark = t.Mark();
                yield return 300;
                t.Check(!KitTestLog.Since(mark, false).Any(x => x.Text.Contains("null InnerThing")), "no \"MinifiedThing with null InnerThing\" in the 300 ticks after");
            }

            // ---- a save entry without <data> (RimWard 20): sample B's SaveData callback runs in no load pass; with <data> in all three
            var calls = new Dictionary<LoadSaveMode, int>();
            var saveExpose = b.SaveExpose;
            b.SaveExpose = () => { calls.TryGetValue(Scribe.mode, out int n); calls[Scribe.mode] = n + 1; };
            string dir = Path.Combine(GenFilePaths.SaveDataFolderPath, "jbtest_steward3");
            try
            {
                Directory.CreateDirectory(dir);
                foreach (bool withData in new[] { false, true })
                {
                    calls.Clear();
                    string f = Path.Combine(dir, withData ? "with.xml" : "without.xml");
                    File.WriteAllText(f, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<savegame><entry><id>" + FrameworkGame.SaveKey(b) + "</id>"
                        + (withData ? "<data><counter>3</counter></data>" : "") + "</entry></savegame>\n");
                    KitSaveEntry entry = null;
                    Scribe.loader.InitLoading(f);
                    try { Scribe_Deep.Look(ref entry, "entry"); Scribe.loader.FinalizeLoading(); }
                    catch (Exception ex) { t.Check(false, "loading the hand-made entry threw " + ex.Message); Scribe.ForceStop(); }
                    string got = string.Join(", ", calls.Select(kv => kv.Key + " " + kv.Value));
                    if (withData) t.Check(calls.Count == 3, "entry WITH <data>: the callback runs in LoadingVars, ResolvingCrossRefs and PostLoadInit (" + got + ")");
                    else t.Check(calls.Count == 0, "entry WITHOUT <data>: the callback runs in no pass (" + (got.Length == 0 ? "none" : got) + ")");
                }
            }
            finally { b.SaveExpose = saveExpose; try { Directory.Delete(dir, true); } catch { } }

            // ---- the settings window: Overview (unavailable section = dash) and Diagnosis (measured button) drawn without error
            var dlg = KitUiTest.NoPause(new Dialog_ModSettings(a.Mod));
            Find.WindowStack.Add(dlg);
            int g0 = a.Guard.TotalFailures, l0 = a.Log.ErrorCount;
            foreach (var page in new[] { "overview", "diagnosis" })
            {
                a.Window.SelectPage(a, page);
                foreach (var y in t.Frames(3)) yield return y;
                t.Shot("fw_steward3_" + page + "_" + UI.screenWidth + "x" + UI.screenHeight + "_" + LanguageDatabase.activeLanguage?.folderName?.Split(' ')[0]);
                foreach (var y in t.Frames(2)) yield return y;
            }
            using (t.ScreenHeight(720))
            {
                foreach (var y in t.Frames(2)) yield return y;
                dlg.Close(false); dlg = KitUiTest.NoPause(new Dialog_ModSettings(a.Mod)); Find.WindowStack.Add(dlg);
                a.Window.SelectPage(a, "diagnosis");
                foreach (var y in t.Frames(4)) yield return y;
                t.Shot("fw_steward3_diagnosis_1280x720_" + LanguageDatabase.activeLanguage?.folderName?.Split(' ')[0]);
                foreach (var y in t.Frames(2)) yield return y;
                a.Window.SelectPage(a, "overview");
                foreach (var y in t.Frames(3)) yield return y;
                t.Shot("fw_steward3_overview_1280x720_" + LanguageDatabase.activeLanguage?.folderName?.Split(' ')[0]);
                foreach (var y in t.Frames(2)) yield return y;
                dlg.Close(false);
            }
            t.Check(a.Guard.TotalFailures == g0 && a.Log.ErrorCount == l0, "Overview and Diagnosis drawn without error");

            // ---- welcome card from Update (1.6 hazard 1): it opens without any OnGUI call of the framework
            var p0 = a.SaveProfile;
            if (p0 != null)
            {
                bool done = p0.FirstStartCardDone, sup = KitFirstStartCard.Suppressed;
                p0.FirstStartCardDone = false; KitFirstStartCard.Suppressed = false; KitFirstStartCard.ResetForGame();
                int opened0 = Dialog_KitFirstStartCard.OpenedCount;
                for (int i = 0; i < 400 && Dialog_KitFirstStartCard.OpenedCount == opened0; i++) foreach (var y in t.Frames(1)) yield return y;
                var card = Find.WindowStack.WindowOfType<Dialog_KitFirstStartCard>();
                t.Check(Dialog_KitFirstStartCard.OpenedCount > opened0, "the welcome card opened from the game component's Update after its delay");
                if (card != null) { t.KeepOpen(card); card.Close(false); }
                p0.FirstStartCardDone = true; KitFirstStartCard.Suppressed = sup;
            }
            t.CheckNoRedErrors(red0, "fw.steward3");
        }
    }
}

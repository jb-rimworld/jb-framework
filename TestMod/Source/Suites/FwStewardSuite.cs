// SPDX-License-Identifier: MIT
// fw.steward: the members added in the 1.0 steward pass (24.11.2026), against sample A (mod under test) with sample B as a tenant.
// Part of JBFramework.Testing.dll (local test mod).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBFramework.Testing
{
    /// <summary>A game component of the harness that fw.steward removes with KitRemoval.RemoveComponents (exists only in test runs).</summary>
    public sealed class FwRemovableComponent : GameComponent
    {
        /// <summary>Created by RimWorld.</summary>
        public FwRemovableComponent(Game game) { }
        /// <summary>A field so the component writes a node of its own.</summary>
        public int marker = 42;
        /// <summary>Saves the marker.</summary>
        public override void ExposeData() => Scribe_Values.Look(ref marker, "marker", 42);
    }

    /// <summary>
    /// fw.steward (explicit; jbmod sample/KitSampleA test fw.steward &lt;label&gt; sample/KitSampleB): RequiresSetting, WithPreview, compat
    /// switches, text and list values, preset access, the settings-window-closed hook, welcome-card parts and actions, KitStyle.Using,
    /// public KitUI helpers, def edits of comps and list elements, slot-suffix detection, PrepareRemoval, RemoveComponents, legacy
    /// components and per-map data.
    /// </summary>
    internal sealed class FwStewardSuite : KitTestSuite
    {
        public override string Name => "fw.steward";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 20000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var a = t.Mod; var b = FwUtil.B;
            t.Check(a != null && FwUtil.A == a, "sample A is the mod under test");
            t.Check(b != null, "sample B is loaded as a tenant");
            if (a == null || b == null) yield break;
            var snap = KitSuiteUtil.Snapshot.Take(a);
            var red0 = t.Mark();
            try
            {
                // ---- RequiresSetting + WithPreview
                var boost = a.SettingsReg.Find("ksa.flagBoost");
                t.Check(boost != null, "ksa.flagBoost registered");
                if (boost != null)
                {
                    a.Set("ksa.flag", true);
                    t.Check(boost.RequiresReasonIn(a.Current) == null, "flag on: the boost row is live");
                    a.Set("ksa.flag", false);
                    string why = boost.RequiresReasonIn(a.Current);
                    t.Check(why != null && why.Contains(a.SettingsReg.Find("ksa.flag").Label), "flag off: greyed with \"" + why + "\"");
                    t.Check(a.Get("ksa.flagBoost") == 2f, "the greyed value is still readable (" + a.Get("ksa.flagBoost") + ")");
                    a.Set("ksa.count", 4f); a.Set("ksa.flagBoost", 3f);
                    string pv = boost.PreviewIn(a.Current);
                    t.Check(pv == "Result: 12 (count × boost)", "preview line follows the profile: " + pv);
                    t.Check(KitRow.MatchesSearch(boost, "count × boost"), "the search finds the preview text");
                    t.Check(boost.PreviewIn(a.Template) != null, "preview against the template works too");
                }

                // ---- compat switch
                string bio = KitDetect.BiotechId;
                if (KitDetect.Biotech)
                {
                    t.Check(a.CompatOn(bio), "Biotech integration on by default");
                    t.Check(a.SetCompat(bio, false) && !a.CompatOn(bio), "switched off: CompatOn false");
                    t.Check(a.HasPendingChanges, "the switch is delivered as a change (compat:" + KitDetect.Normalize(bio) + ")");
                    var e = a.Compat.Find(bio);
                    t.Check(e != null && e.State == KitCompatState.SwitchedOff, "Compatibility page state: " + e?.StateLabel);
                    if (a.HasSaveProfile) { a.SaveAsTemplate(); t.Check(!a.CompatOn(bio), "'Save as template' keeps the switch (template only)"); }
                    t.Check(!a.SaveProfile.Store.Keys.Any(x => x.StartsWith(KitMod.CompatKeyPrefix)), "the save's profile holds no compat switch");
                    a.SetCompat(bio, true);
                    t.Check(a.CompatOn(bio), "switched on again");
                    a.FlushChanges();
                }
                else t.Note("Biotech not active (JB_DLCS): compat switch on an active mod not checked");
                t.Check(!a.CompatOn("juliusberger00.jbkit.missing"), "a missing mod's integration is never on");
                t.Check(!a.SetCompat("juliusberger00.jbkit.missing", false), "the missing-mod entry is not switchable");

                // ---- text and list values
                a.SetText("ksa.note2", "hello");
                t.Check(a.GetText("ksa.note2") == "hello" && a.Current.GetText("ksa.note2") == "hello", "SetText / GetText in Current");
                t.Check(a.HasSaveProfile && a.Template.GetText("ksa.note2") == null, "per save: the template has no such text");
                a.SetList("ksa.list2", new[] { "x", "y" });
                t.Check(a.GetList("ksa.list2").SequenceEqual(new[] { "x", "y" }), "SetList / GetList keeps order");
                var copy = a.Current.CloneForNewGame();
                t.Check(copy.GetText("ksa.note2") == "hello" && copy.GetList("ksa.list2").Count == 2, "a new game's copy carries texts and lists");
                a.SetText("ksa.note2", null); a.SetList("ksa.list2", null);
                t.Check(a.GetText("ksa.note2") == null && a.GetList("ksa.list2").Count == 0, "null removes them");

                // ---- presets
                t.Check(a.ApplyPreset("calm") && a.MatchingPreset()?.Id == "calm", "ApplyPreset(calm) → MatchingPreset calm");
                t.Check(!a.ApplyPreset("noSuchPreset"), "an unknown preset is refused");
                a.FlushChanges();
            }
            finally { snap.Restore(); a.FlushChanges(); }

            // ---- settings window closed hook
            var closedF = FwUtil.Field(a, "KitSampleA.KitSampleAMod", "WindowClosedCount");
            int closed0 = closedF?.GetValue(null) is int c0 ? c0 : -1;
            var dlg = FwUtil.Open(a);
            foreach (var y in t.Frames(3)) yield return y;
            dlg.Close(false);
            foreach (var y in t.Frames(2)) yield return y;
            int closed1 = closedF?.GetValue(null) is int c1 ? c1 : -1;
            t.Check(closed0 >= 0 && closed1 == closed0 + 1, "OnSettingsWindowClosed ran once (" + closed0 + " → " + closed1 + ")");

            // ---- welcome card: drawn part, action, close callback
            bool doneBefore = a.SaveProfile.FirstStartCardDone;
            bool supp = KitFirstStartCard.Suppressed; KitFirstStartCard.Suppressed = true;
            var card = KitFirstStartCard.OpenNow(a);
            t.Check(card != null, "welcome card opened");
            if (card != null)
            {
                KitUiTest.NoPause(card);
                foreach (var y in t.Frames(4)) yield return y;
                t.Shot("fw_steward_card");
                foreach (var y in t.Frames(2)) yield return y;
                t.Check(a.Card != null && a.Card.PartHeights.Count == 1 && a.Card.PartHeights[0] > 0f, "the mod's drawn part was drawn (" + a.Card?.PartHeights.FirstOrDefault() + " px)");
                var act = a.Card.Actions.FirstOrDefault();
                t.Check(act != null && !act.Done(), "the action is offered (not done yet)");
                act?.Act();
                t.Check(act != null && act.Done(), "after the click the action shows as done");
                foreach (var y in t.Frames(2)) yield return y;
                t.Shot("fw_steward_card_done");
                foreach (var y in t.Frames(2)) yield return y;
                card.Close(false);
                foreach (var y in t.Frames(2)) yield return y;
                t.Check(a.SaveProfile.FirstStartCardDone, "closing the card stores 'done' in the save's profile");
                t.Check(a.Log.RecentSnapshot().Any(l => l.Contains("welcome card closed")), "WhenClosed ran");
            }
            a.SetList("ksa.marks", null);
            a.SaveProfile.FirstStartCardDone = doneBefore;
            KitFirstStartCard.Suppressed = supp;

            // ---- style and UI helpers
            var amber = KitStyle.Accent;
            using (KitStyle.Using(a)) t.Check(KitStyle.Accent == KitStyle.AccentOf(a) && KitStyle.AccentOf(a) != amber, "KitStyle.Using(A): Accent is A's colour");
            t.Check(KitStyle.Accent == amber, "after the scope the default accent is back");
            t.Check(KitUI.TextH("a long line of text that wraps", 60f) > KitUI.TextH("x", 600f) && KitUI.TextW("abc") > 0f && KitUI.Cols(new Rect(0, 0, 100, 10), 4f, 1f, 1f).Length == 2, "KitUI.TextH / TextW / Cols are usable");

            // ---- def edits: comp field and a value inside a list element
            var ed = KitDefEdits.For(a);
            var lamp = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d => d.comps != null && d.comps.Any(x => x is CompProperties_Glower));
            if (lamp != null)
            {
                var glow = lamp.comps.OfType<CompProperties_Glower>().First();
                float r0 = glow.glowRadius;
                t.Check(ed.SetComp<CompProperties_Glower>(lamp, "glowRadius", r0 + 5f, "steward") && Math.Abs(glow.glowRadius - (r0 + 5f)) < 1e-4f, lamp.defName + ": SetComp glowRadius " + r0 + " → " + glow.glowRadius);
                t.Check(ed.IsEditedComp<CompProperties_Glower>(lamp, "glowRadius"), "IsEditedComp");
                t.Check(!ed.SetComp<CompProperties_Refuelable>(ThingDefOf.Wall, "fuelCapacity", 1f, "steward"), "a def without that comp is refused");
                ed.Restore("steward");
                t.Check(Math.Abs(glow.glowRadius - r0) < 1e-4f, "Restore puts the comp field back (" + glow.glowRadius + ")");
            }
            var hd = DefDatabase<HediffDef>.AllDefsListForReading.FirstOrDefault(d => d.stages != null && d.stages.Count >= 2);
            if (hd != null)
            {
                float pf = hd.stages[1].painFactor;
                t.Check(ed.Set(hd, "stages.1.painFactor", pf + 0.5f, "steward") && Math.Abs(hd.stages[1].painFactor - (pf + 0.5f)) < 1e-4f, hd.defName + ": Set stages.1.painFactor");
                t.Check(!ed.Set(hd, "stages.99.painFactor", 1f, "steward"), "an index out of range is refused");
                var caps = hd.stages[0].capMods;
                int n0 = caps?.Count ?? 0;
                var more = (caps ?? new List<PawnCapacityModifier>()).ToList(); more.Add(new PawnCapacityModifier { capacity = PawnCapacityDefOf.Moving, offset = -0.1f });
                t.Check(ed.Set(hd, "stages.0.capMods", more, "steward") && hd.stages[0].capMods.Count == n0 + 1, "a whole list in a list element replaced (capMods " + n0 + " → " + hd.stages[0].capMods.Count + ")");
                ed.Restore("steward");
                t.Check(Math.Abs(hd.stages[1].painFactor - pf) < 1e-4f && (hd.stages[0].capMods?.Count ?? 0) == n0, "Restore: painFactor and capMods back");
            }
            t.Check(ed.Count == 0, "no edit left");

            t.Check(KitMaterialMenu.Active, "KitMaterialMenu.Active: the dropdown patch is in place");

            // ---- detection of jbmod slot copies
            t.Check(a.PackageId.Contains(".slot") && KitDetect.IsActive(FwUtil.SampleA) && Framework.IsModActive(FwUtil.SampleB), "a slot copy (" + a.PackageId + ") counts as its mod for KitDetect / IsModActive");
            t.Check(KitDetect.Normalize("Some.Mod.slot3_steam") == "some.mod" && KitDetect.Same("x.y.slot2", "X.Y"), "Normalize / Same drop '.slotN'");
            t.Check(KitDetect.ActiveName(FwUtil.SampleB) == b.Content.Name, "ActiveName finds the slot copy: " + KitDetect.ActiveName(FwUtil.SampleB));

            // ---- PrepareRemoval from code
            var lines = a.PrepareRemoval("delta");
            t.Check(lines.Any(l => l.Contains("nothing was placed")) && !a.Current.SectionOn("delta"), "PrepareRemoval(delta) ran its step and switched it off: " + string.Join(" | ", lines));
            a.Current.SetSection("delta", true);
            t.Check(a.PrepareRemoval("noSuchSection").Count == 0, "an unknown section gives no report");

            // ---- legacy components of B are gone from the running game; per-map data exists
            var legacyG = b.Mod.GetType().Assembly.GetType("KitSampleB.LegacyCounterComponent");
            var legacyM = b.Mod.GetType().Assembly.GetType("KitSampleB.LegacyMapMarks");
            t.Check(legacyG != null && KitComponentStrip.Has(legacyG) && KitComponentStrip.Has(legacyM), "B's legacy component types are registered");
            t.Check(!Current.Game.components.Any(x => x.GetType() == legacyG) && !t.Map.components.Any(x => x.GetType() == legacyM), "their instances were dropped after the new game started");
            yield return 2600;   // B's per-map counter steps once

            // ---- RemoveComponents + a save that names no stripped class
            var rem = Current.Game.components.FirstOrDefault(x => x is FwRemovableComponent);
            t.Check(rem != null, "the harness test component exists before");
            var rep = new List<string>();
            KitRemoval.RemoveComponents(ty => ty == typeof(FwRemovableComponent), rep);
            t.Check(!Current.Game.components.Any(x => x is FwRemovableComponent) && rep.Count == 1 && rep[0].Contains("FwRemovableComponent"), "RemoveComponents took it out: " + rep.FirstOrDefault());
            const string save = "jb_fw_steward_strip";
            GameDataSaveLoader.SaveGame(save);
            string text = FwUtil.SaveText(save);
            t.Check(text != null, "saved " + save);
            if (text != null)
            {
                t.Check(!text.Contains(nameof(FwRemovableComponent)), "the save does not name the removed component (a fresh copy was not written either)");
                t.Check(!text.Contains("KitSampleB.Legacy"), "the save names no legacy component of B");
                t.Check(!text.Contains("\"KitSampleB."), "the save names no class of B at all");
                int ids = System.Text.RegularExpressions.Regex.Matches(text, "<id>" + FwUtil.SampleB + "</id>").Count;
                t.Check(text.Contains("\"" + typeof(FrameworkMap).FullName + "\"") && ids >= 2, "B's entry is in the game component and in the map component (" + ids + " ids)");
                t.Check(text.Contains("<visits>") && text.Contains("<firstColonist>"), "B's per-map node holds its values and a reference");
                try { File.Delete(GenFilePaths.FilePathForSavedGame(save)); } catch { }
            }
            t.Check(!Current.Game.components.Any(x => x is FwRemovableComponent), "no fresh copy came back after saving");
            t.CheckNoRedErrors(red0, "no red error of ours during fw.steward");
            t.Check(t.RedErrorsSince(red0).Count == 0 && t.Phase == 1 && t.LoadedSave == null, "TestContext: RedErrorsSince, Phase, LoadedSave");
        }
    }
}

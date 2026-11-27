// SPDX-License-Identifier: MIT
// fw.cards: the welcome cards of several JB mods in one new colony, driven with real (injected) clicks and keys. Regression test for the
// Steam report of 25.11.2026 (BigCat): "you can't configure them after the popup ... you click on a button and nothing happens, to close
// the window I needed to hit escape" and "the popup windows overlap and conflict". Part of JBFramework.Testing.dll.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBFramework.Testing
{
    /// <summary>A plain window with one button: the control that proves an injected click reaches a Widgets button at all.</summary>
    internal sealed class ClickProbeWindow : Window
    {
        public int Clicks; public Rect ButtonScreen;
        public override Vector2 InitialSize => new Vector2(500f, 300f);
        public ClickProbeWindow() { forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false; }
        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "fw.cards click probe");
            var r = new Rect(20f, 60f, 200f, 40f);
            ButtonScreen = KitUI.ToScreen(r);
            if (Widgets.ButtonText(r, "Probe")) Clicks++;
        }
    }

    /// <summary>
    /// fw.cards (run it with at least two mods that have a welcome card, e.g. <c>jbmod RimWard test fw.cards cards LuciferiumOverhaul
    /// BetterWorkbenchDefaults HospitalBedsGen2</c>): every card is armed again, the game shows them by itself, and the suite answers them
    /// with injected mouse clicks and key presses on the real window stack (pausing windows pause; nothing is closed by the runner).
    /// First a control (a click on a plain pausing window's button), then: no card while a vanilla message box (as the scenario's intro)
    /// is open, a click on a card's checkbox, "Open settings" (the settings window opens and takes clicks on its page list and chips, no other
    /// card covers it, Escape closes it), Escape on the next card, "Don't show welcome cards again" plus "Start" on the next, the close X
    /// on a reopened card. Throughout: never two cards at once, never a card over another pausing window, every way of closing marks the
    /// card done and keeps the switches, nothing threw.
    /// </summary>
    internal sealed class FwCardsSuite : KitTestSuite
    {
        public override string Name => "fw.cards";
        public override bool ExplicitOnly => true;
        public override bool DrivenByFrames => true;
        public override int MaxFrames => 60000;

        int maxCards, overlapFrames, frames;
        string lastStack, firstOverlap;
        TestContext T;

        static readonly FieldInfo modField = typeof(Dialog_ModSettings).GetField("mod", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        static Mod ModOf(Dialog_ModSettings d) { try { return modField?.GetValue(d) as Mod; } catch { return null; } }

        static string Describe(Window w)
        {
            if (w is Dialog_KitFirstStartCard c) return "Card[" + c.Owner?.Info.ShortId + "]";
            if (w is Dialog_ModSettings d) return "ModSettings[" + (ModOf(d) as KitModBase)?.Kit?.Info.ShortId + "]";
            return w.GetType().Name + (w.forcePause ? "(pause)" : "") + (w.absorbInputAroundWindow ? "(absorb)" : "");
        }

        static Dialog_KitFirstStartCard Card() => Find.WindowStack.Windows.OfType<Dialog_KitFirstStartCard>().LastOrDefault();
        static Dialog_ModSettings Settings() => Find.WindowStack.Windows.OfType<Dialog_ModSettings>().LastOrDefault();

        /// <summary>Once per frame: how many cards are open, and whether a card shares the screen with another pausing window.</summary>
        void Observe()
        {
            frames++;
            var ws = Find.WindowStack.Windows;
            int cards = ws.Count(w => w is Dialog_KitFirstStartCard);
            if (cards > maxCards) maxCards = cards;
            if (cards > 0 && ws.Any(w => !(w is Dialog_KitFirstStartCard) && (w.forcePause || w.absorbInputAroundWindow || w is Dialog_ModSettings)))
            {
                overlapFrames++;
                if (firstOverlap == null) { firstOverlap = string.Join(" < ", ws.Select(Describe)); T.Note("OVERLAP: a card shares the screen with another pausing window: " + firstOverlap); KitUiTest.Shot("fw_cards_overlap"); }
            }
            string stack = string.Join(" < ", ws.Select(Describe));
            if (stack != lastStack) { T.Note("frame " + Time.frameCount + " windows: " + (stack.Length == 0 ? "(none)" : stack)); lastStack = stack; }
        }

        IEnumerable<int> Wait(int n) { for (int i = 0; i < n; i++) { Observe(); yield return 1; } }
        IEnumerable<int> WaitSeconds(float s) { float end = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < end) { Observe(); yield return 1; } }
        IEnumerable<int> WaitFor(Func<bool> cond, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!cond() && Time.realtimeSinceStartup < end) { Observe(); yield return 1; }
            Observe();
        }
        IEnumerable<int> Click(Vector2 p) { foreach (var y in KitUiInput.Click(p)) { Observe(); yield return y; } }
        IEnumerable<int> Press(string key) { foreach (var y in KitUiInput.Press(key)) { Observe(); yield return y; } }

        /// <summary>A labelled checkbox drawn near its text takes clicks only on the text and the box: aim at the start of the label.</summary>
        static Vector2 OnLabel(Rect r) => new Vector2(r.x + 30f, r.center.y);

        static Dictionary<string, bool> Switches(KitMod k) => k.Sections.All.ToDictionary(s => s.Id, s => k.SaveProfile.SectionOn(s.Id));
        static bool Same(Dictionary<string, bool> a, Dictionary<string, bool> b) => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out bool v) && v == kv.Value);

        public override IEnumerable<int> Run(TestContext t)
        {
            T = t;
            var tenants = Framework.Mods.Where(k => KitFirstStartCard.Wants(k) && k.SaveProfile != null).ToList();
            t.Note("mods with a welcome card in this game: " + string.Join(", ", tenants.Select(k => k.Name)) + "; all tenants: " + string.Join(", ", Framework.Mods.Select(k => k.Name)));
            t.Check(tenants.Count >= 2, "at least two mods with a welcome card are loaded (" + tenants.Count + ")");
            if (tenants.Count < 2) yield break;
            var calib = new List<string>();
            foreach (var y in KitUiInput.Calibrate(calib)) yield return y;
            foreach (var l in calib) t.Note(l);
            t.Check(KitUiInput.Available, "the engine's input injector is reachable" + (KitUiInput.Available ? "" : ": " + KitUiInput.Problem));
            if (!KitUiInput.Available) yield break;

            var self = Framework.Self; var wcSetting = self?.SettingsReg.Find(Framework.WelcomeCardsKey);
            float wcWas = wcSetting != null ? self.SettingsReg.Value(wcSetting, self.Template) : 1f;
            var doneWas = tenants.ToDictionary(k => k, k => k.SaveProfile.FirstStartCardDone);
            bool supWas = KitFirstStartCard.Suppressed;
            int red0 = t.Mark(), guard0 = Framework.AllContexts().Sum(k => k.Guard.TotalFailures), log0 = Framework.AllContexts().Sum(k => k.Log.ErrorCount);
            var speedWas = Find.TickManager.CurTimeSpeed;
            try
            {
                foreach (var w in Find.WindowStack.Windows.ToList()) if (w.forcePause || w.absorbInputAroundWindow) Find.WindowStack.TryRemove(w, false);
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                if (wcSetting != null) self.SettingsReg.Set(wcSetting, self.Template, 1f);
                foreach (var k in tenants) k.SaveProfile.FirstStartCardDone = false;
                KitFirstStartCard.ResetForGame();
                KitFirstStartCard.Suppressed = false;

                // 0a. control: an injected click reaches a plain pausing window's button (so a failure below is the card's, not the harness's)
                var probe = new ClickProbeWindow();
                Find.WindowStack.Add(probe);
                foreach (var y in Wait(4)) yield return y;
                foreach (var y in Click(probe.ButtonScreen.center)) yield return y;
                t.Check(probe.Clicks == 1, "control: an injected click presses a plain button (" + probe.Clicks + " clicks at " + KitUiTest.R(probe.ButtonScreen) + ")");
                probe.Close(false);
                foreach (var y in Wait(2)) yield return y;
                overlapFrames = 0; firstOverlap = null;

                // 0b. while another pausing window is open (a message box stands for the scenario's intro), no card opens on top of it
                var box = new Dialog_MessageBox("fw.cards: a pausing window that is not a card (as the scenario's intro at game start)");
                Find.WindowStack.Add(box);
                foreach (var y in WaitSeconds(KitFirstStartCard.Delay * 2f + 1f)) yield return y;
                t.Check(Card() == null && box.IsOpen, "no welcome card opens while another pausing window is open (" + (Card() == null ? "none" : Describe(Card())) + ")");
                box.Close(false);

                // 1. the first card opens by itself, after its delay; its own controls take clicks
                foreach (var y in WaitFor(() => Card() != null, KitFirstStartCard.Delay + 15f)) yield return y;
                var c1 = Card();
                t.Check(c1 != null, "a welcome card opened by itself" + (c1 != null ? " (" + c1.Owner.Name + ")" : ""));
                if (c1 == null) yield break;
                foreach (var y in Wait(4)) yield return y;
                KitUiTest.Shot("fw_cards_1_first");
                foreach (var y in Click(OnLabel(c1.UseForAllScreen))) yield return y;
                bool ticked = c1.UseForAll;
                foreach (var y in Click(OnLabel(c1.UseForAllScreen))) yield return y;
                t.Check(ticked && !c1.UseForAll, "card 1 (" + c1.Owner.Name + "): a click on \"Use these choices for all new colonies\" ticks it, a second click unticks it");

                // 2. "Open settings": the card closes, that mod's settings window opens and is not covered by the next card
                var k1 = c1.Owner; var sw1 = Switches(k1);
                foreach (var y in Click(c1.OpenScreen.center)) yield return y;
                foreach (var y in Wait(3)) yield return y;
                var set = Settings();
                t.Check(!c1.IsOpen && k1.SaveProfile.FirstStartCardDone && Same(sw1, Switches(k1)), "card 1: \"Open settings\" closes the card, marks it done and keeps its switches");
                t.Check(set != null && ModOf(set) == k1.Mod, "card 1: \"Open settings\" opens " + k1.Name + "'s settings window (" + (set == null ? "none" : Describe(set)) + ")");
                foreach (var y in WaitSeconds(KitFirstStartCard.Delay * 2f + 1f)) yield return y;
                KitUiTest.Shot("fw_cards_2_settings_after_wait");
                var top = Find.WindowStack.Windows.LastOrDefault();
                t.Check(Card() == null, "no other welcome card opens while the settings window is open (" + (Card() == null ? "none" : Describe(Card())) + ")");
                t.Check(set != null && top == set, "the settings window is the top window (top: " + (top == null ? "none" : Describe(top)) + ")");

                // 3. the settings window takes clicks: a page in its list, the This save / Template chips
                if (set != null && set.IsOpen)
                {
                    foreach (var y in Wait(2)) yield return y;
                    string pageWas = k1.Window.PageId;
                    string want = pageWas == "about" ? "general" : "about";
                    bool haveRow = k1.Window.PageRowsScreen.TryGetValue(want, out Rect row);
                    if (haveRow) foreach (var y in Click(row.center)) yield return y;
                    t.Check(haveRow && k1.Window.PageId == want, "settings window: a click on the page \"" + want + "\" opens it (page now " + k1.Window.PageId + ")");
                    var chips = k1.Window.Last.ChipsScreen;
                    bool chipsOk = false;
                    if (chips.width > 0f)
                    {
                        foreach (var y in Click(new Vector2(chips.xMax - 12f, chips.yMax - 8f))) yield return y;
                        bool toTemplate = k1.Window.EditTemplate;
                        foreach (var y in Click(new Vector2(chips.xMin + 12f, chips.yMin + 8f))) yield return y;
                        chipsOk = toTemplate && !k1.Window.EditTemplate;
                    }
                    t.Check(chipsOk, "settings window: clicks on the Template and This save chips switch the profile it edits");
                    KitUiTest.Shot("fw_cards_3_settings_clicked");
                    foreach (var y in Press("Escape")) yield return y;
                    foreach (var y in Wait(2)) yield return y;
                    t.Check(!set.IsOpen, "settings window: Escape closes it");
                    if (set.IsOpen) set.Close(false);
                }
                // whatever a broken build left open: close it so the next steps start clean (the checks above already failed)
                foreach (var w in Find.WindowStack.Windows.OfType<Dialog_ModSettings>().ToList()) w.Close(false);
                if (Card() != null) { t.Note("a card was already open when the settings window closed: " + Describe(Card())); }

                // 4. the next card comes only now, alone; Escape closes it and keeps its switches
                foreach (var y in WaitFor(() => Card() != null, KitFirstStartCard.Delay + 10f)) yield return y;
                var c2 = Card();
                t.Check(c2 != null && c2.Owner != k1, "the next card opens after the settings window closed" + (c2 != null ? " (" + c2.Owner.Name + ")" : ""));
                if (c2 != null)
                {
                    foreach (var y in Wait(4)) yield return y;
                    KitUiTest.Shot("fw_cards_4_second");
                    var k2 = c2.Owner; var sw2 = Switches(k2);
                    foreach (var y in Press("Escape")) yield return y;
                    foreach (var y in Wait(2)) yield return y;
                    t.Check(!c2.IsOpen && k2.SaveProfile.FirstStartCardDone && Same(sw2, Switches(k2)), "card 2 (" + k2.Name + "): Escape closes it, marks it done and keeps its switches");
                    if (c2.IsOpen) c2.Close(false);
                }

                // 5. a third card (when there is one): "Don't show welcome cards again" by click, then Start
                if (tenants.Count >= 3)
                {
                    foreach (var y in WaitFor(() => Card() != null, KitFirstStartCard.Delay + 10f)) yield return y;
                    var c3 = Card();
                    t.Check(c3 != null && c3.Owner != k1 && c3.Owner != c2?.Owner, "the third card opens after the second closed" + (c3 != null ? " (" + c3.Owner.Name + ")" : ""));
                    if (c3 != null)
                    {
                        foreach (var y in Wait(4)) yield return y;
                        foreach (var y in Click(OnLabel(c3.NeverScreen))) yield return y;
                        t.Check(c3.NeverAgain, "card 3 (" + c3.Owner.Name + "): a click ticks \"Don't show welcome cards again\"");
                        foreach (var y in Click(c3.StartScreen.center)) yield return y;
                        foreach (var y in Wait(2)) yield return y;
                        t.Check(!c3.IsOpen && c3.Owner.SaveProfile.FirstStartCardDone, "card 3: Start closes it and marks it done");
                        t.Check(!Framework.WelcomeCards, "\"Don't show welcome cards again\" switched the cards off for every JB mod");
                        if (c3.IsOpen) c3.Close(false);
                        // with the cards off, an armed card stays closed and the save is marked done silently
                        foreach (var k in tenants) k.SaveProfile.FirstStartCardDone = false;
                        KitFirstStartCard.ResetForGame();
                        foreach (var y in WaitSeconds(KitFirstStartCard.Delay * 2f + 1f)) yield return y;
                        t.Check(Card() == null && tenants.All(k => k.SaveProfile.FirstStartCardDone), "with welcome cards off no card opens and every save is marked done silently");
                        if (wcSetting != null) self.SettingsReg.Set(wcSetting, self.Template, 1f);
                    }
                }

                // 6. the visible close button: a reopened card closes by a click on its X and keeps its switches
                KitFirstStartCard.Suppressed = true;
                foreach (var w in Find.WindowStack.Windows.OfType<Dialog_KitFirstStartCard>().ToList()) w.Close(false);
                var c4 = KitFirstStartCard.OpenNow(tenants[0]);
                foreach (var y in Wait(4)) yield return y;
                t.Check(c4 != null && c4.doCloseX, "the card shows a close button (X)");
                if (c4 != null)
                {
                    var sw4 = Switches(c4.Owner);
                    var wr = c4.windowRect;
                    KitUiTest.Shot("fw_cards_5_close_x");
                    foreach (var y in Click(new Vector2(wr.xMax - 4f - 9f, wr.y + 4f + 9f))) yield return y;
                    foreach (var y in Wait(2)) yield return y;
                    t.Check(!c4.IsOpen && Same(sw4, Switches(c4.Owner)), "a click on the X closes the card and keeps its switches");
                    if (c4.IsOpen) c4.Close(false);
                }

                t.Check(maxCards <= 1, "never two welcome cards at once (at most " + maxCards + " in " + frames + " frames)");
                t.Check(overlapFrames == 0, "a welcome card never shares the screen with another pausing window (" + overlapFrames + " frames" + (firstOverlap != null ? ", first: " + firstOverlap : "") + ")");
                t.Check(Framework.AllContexts().Sum(k => k.Guard.TotalFailures) == guard0 && Framework.AllContexts().Sum(k => k.Log.ErrorCount) == log0, "nothing threw in the cards or the settings window (fences and logs)");
                t.CheckNoRedErrors(red0, "no red error of ours while clicking through the cards");
            }
            finally
            {
                foreach (var w in Find.WindowStack.Windows.ToList()) if (w is Dialog_KitFirstStartCard || w is Dialog_ModSettings) w.Close(false);
                foreach (var kv in doneWas) kv.Key.SaveProfile.FirstStartCardDone = kv.Value;
                if (wcSetting != null) self.SettingsReg.Set(wcSetting, self.Template, wcWas);
                KitFirstStartCard.Suppressed = supWas;
                KitFirstStartCard.ResetForGame();
                Find.TickManager.CurTimeSpeed = speedWas;
            }
        }
    }
}

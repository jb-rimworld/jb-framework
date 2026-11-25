// SPDX-License-Identifier: MIT
// kit.stable: the settings window never changes a stored value by being looked at (steward pass 2, Luciferium Overhaul FW request 10:
// the percent slider rewrote 0.67 % to 1 % while its page was drawn). Part of JBFramework.Testing.dll.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBFramework.Testing
{
    /// <summary>
    /// kit.stable: writes awkward values straight into the stores of the mod under test (template and this save): 0.67 %, 1/3, values
    /// outside the slider range as an old settings file has them, a Bool of 0.3; in the template of a per-save mod (not in play) also
    /// negative zero, int.MinValue/MaxValue and enum values that do not exist; every other number row in typed entry. Then it draws every page in both profiles (nerd rows and rows
    /// outside the viewport included), searches, switches "This save"/"Template", resizes, changes the screen size, closes and reopens
    /// the window, and checks that every stored value is bit for bit what was written, that no change was marked (no
    /// OnSettingsChanged) and that the framework's own preferences stayed. Restores the mod's values at the end.
    /// </summary>
    internal sealed class KitStableSuite : KitTestSuite
    {
        public override string Name => "kit.stable";
        public override int MaxTicks => 60000;

        sealed class Snap
        {
            public Dictionary<string, int> Values = new Dictionary<string, int>();   // float bits: -0 and 0 differ
            public Dictionary<string, bool> Free = new Dictionary<string, bool>();
            public Dictionary<string, bool> Sections = new Dictionary<string, bool>();
            public bool Card;
        }

        static Snap Take(KitProfile p)
        {
            var s = new Snap();
            if (p == null) return s;
            foreach (var key in p.Store.Keys.ToList()) if (p.Store.TryGet(key, out float v)) s.Values[key] = BitConverter.ToInt32(BitConverter.GetBytes(v), 0);
            foreach (var kv in p.Store.freeText) s.Free[kv.Key] = kv.Value;
            foreach (var kv in p.sections) s.Sections[kv.Key] = kv.Value;
            s.Card = p.FirstStartCardDone;
            return s;
        }

        static float Bits(int b) => BitConverter.ToSingle(BitConverter.GetBytes(b), 0);

        static void Put(KitProfile p, Snap s)
        {
            if (p == null) return;
            foreach (var key in p.Store.Keys.ToList()) p.Store.Remove(key);
            foreach (var kv in s.Values) p.Store.Set(kv.Key, Bits(kv.Value));
            p.Store.freeText.Clear();
            foreach (var kv in s.Free) p.Store.freeText[kv.Key] = kv.Value;
            p.sections.Clear();
            foreach (var kv in s.Sections) p.sections[kv.Key] = kv.Value;
            p.FirstStartCardDone = s.Card;
        }

        static List<string> Diff(string what, Snap a, Snap b)
        {
            var d = new List<string>();
            foreach (var key in a.Values.Keys.Union(b.Values.Keys))
            {
                bool ha = a.Values.TryGetValue(key, out int va), hb = b.Values.TryGetValue(key, out int vb);
                if (ha != hb || va != vb)
                    d.Add(what + " " + key + ": " + (ha ? KitSuiteUtil.F(Bits(va)) : "(none)") + " → " + (hb ? KitSuiteUtil.F(Bits(vb)) : "(none)"));
            }
            foreach (var key in a.Free.Keys.Union(b.Free.Keys))
                if (a.Free.TryGetValue(key, out bool x) != b.Free.TryGetValue(key, out bool y) || x != y) d.Add(what + " typed-entry flag " + key);
            foreach (var key in a.Sections.Keys.Union(b.Sections.Keys))
                if (a.Sections.TryGetValue(key, out bool x) != b.Sections.TryGetValue(key, out bool y) || x != y) d.Add(what + " section " + key);
            if (a.Card != b.Card) d.Add(what + " welcome card flag");
            return d;
        }

        /// <summary>
        /// Values a control cannot show exactly, per kind (round r picks a different one for each row). <paramref name="extreme"/> (only
        /// for a profile the game does not play with: the template of a per-save mod) adds int.MinValue/MaxValue, values below the
        /// slider, negative zero and invalid enum values; the profile in play gets only values the mod's own code can live with while
        /// the suite runs (inside or just above the slider range, not representable by the slider).
        /// </summary>
        static float Awkward(KitSetting s, int i, bool extreme)
        {
            float lo = s.SliderMin, hi = s.SliderMax, span = Math.Max(1e-6f, hi - lo);
            float above = hi + span * 0.37f + (s.Kind == KitSettingKind.Int || s.Kind == KitSettingKind.Silver ? 1f : 0.0123f);
            float[] v;
            switch (s.Kind)
            {
                case KitSettingKind.Bool: v = extreme ? new[] { 0.3f, 0.7f, 2f, -0f } : new[] { 0.3f, 0.7f }; break;
                case KitSettingKind.Enum: v = extreme ? new[] { 1.5f, 99f, -1f, 0.4f } : new[] { (float)Math.Round(lo) + 0.4f }; break;
                case KitSettingKind.Int:
                case KitSettingKind.Silver: v = extreme ? new[] { (float)int.MaxValue, (float)int.MinValue, above, lo - 3f, lo + 0.5f } : new[] { above, (float)Math.Round(lo + span / 3f) + 0.4f }; break;
                case KitSettingKind.Percent:
                    v = extreme ? new[] { 0.0066667f, 1f / 3f, above, lo - 0.5f, -0f }
                                : new[] { lo <= 0.0066667f && hi >= 0.0066667f ? 0.0066667f : lo + span / 3f, lo + span / 3f, above }; break;
                case KitSettingKind.LogFloat: v = extreme ? new[] { (lo + hi) / 3f, hi * 10f, Math.Max(1e-6f, lo) / 7f, 1f / 3f } : new[] { (lo + hi) / 3f, hi * 1.37f }; break;
                default: v = extreme ? new[] { 1f / 3f, -0f, hi * 3f + 0.123f, lo - 1.7f, lo + span / 3f } : new[] { lo + span / 3f, above, lo <= 0f && hi >= 0f ? -0f : lo + span / 7f }; break;
            }
            return v[Math.Abs(i) % v.Length];
        }

        static bool IsNumber(KitSetting s) => s.Kind != KitSettingKind.Bool && s.Kind != KitSettingKind.Enum;

        public override IEnumerable<int> Run(TestContext t)
        {
            var k = t.Mod;
            if (k?.Mod == null) { t.Check(false, "no mod under test"); yield break; }
            var self = Framework.Self;

            // what the rows show for awkward values (display only)
            var pctProbe = k.SettingsReg.All.FirstOrDefault(s => s.Kind == KitSettingKind.Percent);
            if (pctProbe != null) t.Check(pctProbe.Format(0.0066667f) == "0.67 %", "0.67 % is shown as \"0.67 %\" (" + pctProbe.Format(0.0066667f) + ")");
            var anyNum = k.SettingsReg.All.FirstOrDefault(s => s.Kind == KitSettingKind.Float);
            if (anyNum != null) t.Check(anyNum.Format(-0f) == "0", "negative zero is shown as \"0\" (" + anyNum.Format(-0f) + ")");

            var profiles = new List<(string name, KitProfile p)> { ("template", k.Template) };
            if (k.HasSaveProfile && !ReferenceEquals(k.SaveProfile, k.Template)) profiles.Add(("save", k.SaveProfile));
            var originals = profiles.Select(x => Take(x.p)).ToList();
            var selfOrig = Take(self?.Template);
            bool suppressed = KitFirstStartCard.Suppressed; KitFirstStartCard.Suppressed = true;
            var nerdWas = Framework.ShowNerdOverride; Framework.ShowNerdOverride = true;
            bool forceWas = KitRow.ForceDrawAll; KitRow.ForceDrawAll = true;
            var oldPage = k.Window.PageId; bool oldEdit = k.Window.EditTemplate;
            int red0 = KitTestLog.Mark(), g0 = k.Guard.TotalFailures;
            int rounds = 0, pagesDrawn = 0;
            try
            {
                for (int round = 0; round < 3; round++)
                {
                    // awkward values straight into the stores, as an old file or an older version could leave them
                    foreach (var (name, p) in profiles)
                    {
                        int i = round + (name == "save" ? 1 : 0);
                        // the profile the game plays with (and global settings, read in play) gets only values its code can live with
                        bool inPlay = ReferenceEquals(p, k.Current);
                        foreach (var s in k.SettingsReg.All)
                        {
                            if (s.Global && !ReferenceEquals(p, k.Template)) { i++; continue; }
                            p.Store.Set(s.Key, Awkward(s, i, !inPlay && !s.Global));
                            if (IsNumber(s)) { if ((i + round) % 2 == 0) p.Store.freeText[s.Key] = true; else p.Store.freeText.Remove(s.Key); }
                            i++;
                        }
                    }
                    KitRow.ClearBuffers();
                    var before = profiles.Select(x => Take(x.p)).ToList();
                    var selfBefore = Take(self?.Template);
                    int changes0 = k.ChangeCount, selfChanges0 = self?.ChangeCount ?? 0;

                    var dlg = KitUiTest.NoPause(new Dialog_ModSettings(k.Mod));
                    Find.WindowStack.Add(dlg);
                    foreach (var y in KitUiTest.Frames(3)) yield return y;
                    var targets = k.HasSaveProfile ? new[] { false, true } : new[] { true };
                    foreach (bool editTemplate in targets)
                    {
                        k.Window.EditTemplate = editTemplate;
                        foreach (var pg in KitSettingsWindow.BuildPages(k, KitSettingsWindow.Target(k)))
                        {
                            if (!dlg.IsOpen) break;
                            k.Window.SelectPage(k, pg.Id); k.Window.EditTemplate = editTemplate;
                            foreach (var y in KitUiTest.Frames(2)) yield return y;
                            pagesDrawn++;
                        }
                        // searches: everything with an "a", a label word, nothing
                        foreach (var q in new[] { "a", k.SettingsReg.All.FirstOrDefault()?.Label.Split(' ').OrderByDescending(w => w.Length).FirstOrDefault() ?? "x", "zzzqqq" })
                        {
                            k.Window.Search = q;
                            foreach (var y in KitUiTest.Frames(2)) yield return y;
                        }
                        k.Window.Search = "";
                    }
                    // resize the window, change the screen size, switch the profile back and forth while a page is open
                    if (dlg.IsOpen)
                    {
                        var wr = dlg.windowRect;
                        dlg.windowRect = new Rect(wr.x, wr.y, Mathf.Max(400f, wr.width * 0.7f), Mathf.Max(300f, wr.height * 0.7f));
                        foreach (var y in KitUiTest.Frames(2)) yield return y;
                        dlg.windowRect = wr;
                        using (KitUiTest.AtScreenHeight(720)) { foreach (var y in KitUiTest.Frames(3)) yield return y; }
                        if (k.HasSaveProfile)
                            for (int n = 0; n < 4; n++) { k.Window.EditTemplate = !k.Window.EditTemplate; foreach (var y in KitUiTest.Frames(1)) yield return y; }
                    }
                    dlg.Close(false);   // vanilla calls WriteSettings: the file is written, nothing may change
                    foreach (var y in KitUiTest.Frames(2)) yield return y;
                    var again = KitUiTest.NoPause(new Dialog_ModSettings(k.Mod));
                    Find.WindowStack.Add(again);
                    foreach (var y in KitUiTest.Frames(2)) yield return y;
                    again.Close(false);
                    foreach (var y in KitUiTest.Frames(2)) yield return y;

                    var diffs = new List<string>();
                    for (int n = 0; n < profiles.Count; n++) diffs.AddRange(Diff(profiles[n].name, before[n], Take(profiles[n].p)));
                    diffs.AddRange(Diff("framework preferences", selfBefore, Take(self?.Template)));
                    t.Check(diffs.Count == 0, "round " + (round + 1) + ": no stored value changed by drawing, searching, switching, resizing, closing" + (diffs.Count == 0 ? "" : " (" + diffs.Count + ": " + string.Join("; ", diffs.Take(8)) + ")"));
                    t.Check(k.ChangeCount == changes0 && (self?.ChangeCount ?? 0) == selfChanges0, "round " + (round + 1) + ": no change marked (" + (k.ChangeCount - changes0) + " for the mod, " + ((self?.ChangeCount ?? 0) - selfChanges0) + " for the framework)");
                    rounds++;
                }
                // a real edit still works: one Set marks one change and stores the value as given
                var real = k.SettingsReg.All.FirstOrDefault(s => s.Kind == KitSettingKind.Percent && !s.Global) ?? k.SettingsReg.All.FirstOrDefault(s => IsNumber(s) && !s.Global);
                if (real != null)
                {
                    var target = k.Current;
                    int c0 = k.ChangeCount;
                    float want = real.Clamp(real.SliderMin + (real.SliderMax - real.SliderMin) * 0.37f);
                    k.SettingsReg.Set(real, target, want);
                    t.Check(k.ChangeCount == c0 + 1 && k.SettingsReg.Value(real, target) == want, "a real edit of " + real.Key + " is stored as given and marked once");
                }
                t.Check(KitRow.RoundToStep(new KitSetting { Kind = KitSettingKind.Percent, SliderMin = 0f, SliderMax = 1f }, 0.6667f) == 0.67f, "a dragged 0..100 % slider rounds to whole percents (0.6667 → 0.67)");
            }
            finally
            {
                for (int n = 0; n < profiles.Count; n++) Put(profiles[n].p, originals[n]);
                Put(self?.Template, selfOrig);
                KitRow.ClearBuffers();
                k.WriteSettings();
                KitFirstStartCard.Suppressed = suppressed;
                Framework.ShowNerdOverride = nerdWas;
                KitRow.ForceDrawAll = forceWas;
                k.Window.SelectPage(k, oldPage); k.Window.EditTemplate = oldEdit;
                k.FlushChanges();
            }
            t.Check(rounds == 3 && pagesDrawn > 0, rounds + " rounds, " + pagesDrawn + " page draws");
            t.Check(k.Guard.TotalFailures == g0, "nothing threw while drawing awkward values");
            KitSuiteUtil.NoRedErrors(t, red0, "no red error of ours");
        }
    }
}

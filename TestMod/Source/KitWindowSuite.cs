// SPDX-License-Identifier: MIT
// kit.window: the mod's settings window at three screen sizes, every page and a search drawn (after Hearth & Harbor's ui.layout suite,
// Source/Hearth/UI/UiLayoutTests.cs). Part of JBFramework.Testing.dll.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBFramework.Testing
{
    /// <summary>Window helpers of the UI suites.</summary>
    internal static class KitUiTest
    {
        /// <summary>The runner closes forcePause windows every frame; test windows must not pause.</summary>
        public static T NoPause<T>(T w) where T : Window { w.forcePause = false; w.absorbInputAroundWindow = false; w.closeOnClickedOutside = false; return w; }

        /// <summary>Screenshot folder (JB_UI_SHOTS: a folder, or "1" = the game's screenshot folder), or null = no screenshots.</summary>
        public static string ShotDir
        {
            get
            {
                var v = KitTestEnv.Get(KitTestEnv.Shots);
                if (v == null || v == "0") return null;
                if (v == "1") { try { return GenFilePaths.ScreenshotFolderPath; } catch { return null; } }
                return v;
            }
        }

        public static int Shots;
        /// <summary>Saves a screenshot of the whole (virtual) screen as &lt;name&gt;.png when JB_UI_SHOTS is set. Captured at the end of the frame.</summary>
        public static void Shot(string name)
        {
            string dir = ShotDir;
            if (dir == null) return;
            try
            {
                Directory.CreateDirectory(dir);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
                Shots++;
            }
            catch (Exception e) { Log.Warning("[JBTEST] screenshot failed: " + e.Message); }
        }

        /// <summary>Waits until <paramref name="frames"/> more frames were drawn (ticks run many per frame at top speed). Gives up after 3000 ticks.</summary>
        public static IEnumerable<int> Frames(int frames)
        {
            int until = Time.frameCount + frames, guard = 0;
            while (Time.frameCount < until && guard++ < 3000) yield return 1;
        }

        /// <summary>
        /// Real layout for a virtual screen <paramref name="height"/> px high: Prefs.UIScale = physical height / height (vanilla
        /// UI.ApplyUIScale divides every frame). Dispose puts the scale back. Takes effect from the next frame.
        /// </summary>
        public static IDisposable AtScreenHeight(int height)
        {
            float was = Prefs.UIScale;
            float phys = Screen.height > 0 ? Screen.height : 1080f;
            Prefs.UIScale = Mathf.Clamp(phys / Math.Max(240, height), 0.25f, 4f);
            return new Restore(() => Prefs.UIScale = was);
        }

        sealed class Restore : IDisposable
        {
            Action a; public Restore(Action a) { this.a = a; }
            public void Dispose() { var x = a; a = null; try { x?.Invoke(); } catch (Exception e) { Log.Warning("[JBTEST] restoring the UI scale failed: " + e.Message); } }
        }

        public static bool Inside(Rect inner, Rect outer, float slack = 0.5f)
            => inner.xMin >= outer.xMin - slack && inner.yMin >= outer.yMin - slack && inner.xMax <= outer.xMax + slack && inner.yMax <= outer.yMax + slack;

        public static string R(Rect r) => $"({r.x:0},{r.y:0} {r.width:0}x{r.height:0})";

        public static string Safe(string s) => new string((s ?? "").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    }

    /// <summary>
    /// kit.window: opens the mod's settings window on real virtual screens of 1280x720, 1920x1080 and 2560x1440 (Prefs.UIScale 1.5 / 1 /
    /// 0.75 on the 1920x1080 test screen: the game lays out and draws at that size), draws every page (in a game with a per-save profile: for this save and for the
    /// template) and a search, and checks that the window fits the screen, keeps its minimum, is resizable, that the parts of each page lie
    /// inside the window without overlapping, and that nothing threw. With JB_UI_SHOTS it leaves kit_window_&lt;id&gt;_&lt;size&gt;_&lt;page&gt;.png.
    /// </summary>
    internal sealed class KitWindowSuite : KitTestSuite
    {
        public override string Name => "kit.window";
        public override int MaxTicks => 60000;

        static bool InsideScreen(Vector2 size, Vector2 scr)
        {
            var r = new Rect((scr.x - size.x) / 2f, (scr.y - size.y) / 2f, size.x, size.y);
            return r.x >= -0.5f && r.y >= -0.5f && r.xMax <= scr.x + 0.5f && r.yMax <= scr.y + 0.5f;
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            var k = t.Mod;
            if (k?.Mod == null) { t.Check(false, "no mod under test"); yield break; }
            var screens = new[] { new Vector2(1280f, 720f), new Vector2(1920f, 1080f), new Vector2(2560f, 1440f) };
            t.Note("test screen " + Screen.width + "x" + Screen.height + ", window size preference " + Framework.WindowScale.ToString("0.00") + ", screenshots " + (KitUiTest.ShotDir != null ? "on" : "off"));
            int guard0 = k.Guard.TotalFailures, selfGuard0 = Framework.Self?.Guard.TotalFailures ?? 0;
            int log0 = k.Log.ErrorCount, selfLog0 = Framework.Self?.Log.ErrorCount ?? 0, red0 = KitTestLog.Mark();
            bool suppressed = KitFirstStartCard.Suppressed;
            KitFirstStartCard.Suppressed = true;
            var oldPage = k.Window.PageId; bool oldEdit = k.Window.EditTemplate;
            int drawnPages = 0, frames0 = KitUiProbe.Count("settings");
            string sid = KitUiTest.Safe(k.Info.ShortId);
            try
            {
                foreach (var want in screens)
                {
                    // a real virtual screen of that height (Prefs.UIScale on the test screen): real layout, real screenshots
                    using (KitUiTest.AtScreenHeight((int)want.y))
                    {
                    foreach (var y in KitUiTest.Frames(3)) yield return y;
                    var scr = new Vector2(UI.screenWidth, UI.screenHeight);
                    var real = scr;
                    string tag = (int)want.x + "x" + (int)want.y;
                    t.Note(tag + ": UI scale " + Prefs.UIScale.ToString("0.###") + " gives a virtual screen of " + scr.x + "x" + scr.y);
                    var size = new Dialog_ModSettings(k.Mod).InitialSize;
                    t.Check(InsideScreen(size, scr), tag + ": settings window " + size.x.ToString("0") + "x" + size.y.ToString("0") + " inside the screen");
                    t.Check(size.x >= Mathf.Min(KitUI.SettingsMinW, scr.x - 20f) - 0.5f && size.y >= Mathf.Min(KitUI.SettingsMinH, scr.y - 55f) - 0.5f, tag + ": keeps its minimum size");
                    if (scr.y >= 1000f) t.Check(size.x > 900f && size.y > 700f, tag + ": larger than the vanilla 900x700");
                    t.Check(new Dialog_ModSettings(k.Mod).resizeable, tag + ": resizable");
                    var otherMod = LoadedModManager.ModHandles.FirstOrDefault(m => !(m is KitModBase));
                    if (otherMod != null) t.Check(new Dialog_ModSettings(otherMod).InitialSize == new Vector2(900f, 700f), tag + ": other mods' settings windows stay vanilla (" + otherMod.GetType().Name + ")");

                    var dlg = KitUiTest.NoPause(new Dialog_ModSettings(k.Mod));
                    Find.WindowStack.Add(dlg);
                    foreach (var y in KitUiTest.Frames(4)) yield return y;
                    t.Check(dlg.IsOpen, tag + ": window opened");
                    var targets = new List<bool> { false };
                    if (k.HasSaveProfile) targets.Add(true);
                    foreach (bool editTemplate in targets)
                    {
                        if (!dlg.IsOpen) break;
                        k.Window.EditTemplate = editTemplate;
                        var target = KitSettingsWindow.Target(k);
                        string mode = k.HasSaveProfile ? (editTemplate ? "template" : "save") : "global";
                        var pages = KitSettingsWindow.BuildPages(k, target);
                        t.Check(pages.Count > 0 && pages.Select(p => p.Id).Distinct().Count() == pages.Count, tag + " " + mode + ": " + pages.Count + " pages, ids unique (" + string.Join(", ", pages.Select(p => p.Id)) + ")");
                        foreach (var p in pages)
                        {
                            k.Window.SelectPage(k, p.Id);
                            k.Window.EditTemplate = editTemplate;
                            int g0 = k.Guard.TotalFailures, l0 = k.Log.ErrorCount;
                            foreach (var y in KitUiTest.Frames(3)) yield return y;
                            int f0 = Time.frameCount;
                            if (editTemplate == false || targets.Count == 1) KitUiTest.Shot("kit_window_" + sid + "_" + tag + "_" + KitUiTest.Safe(p.Id));
                            foreach (var y in KitUiTest.Frames(2)) yield return y;
                            var L = k.Window.Last;
                            bool drawn = L.Page == p.Id && L.Frame >= f0 - 1;
                            if (drawn) drawnPages++;
                            t.Check(drawn, tag + " " + mode + " " + p.Id + ": drawn (frame " + L.Frame + ")");
                            t.Check(k.Guard.TotalFailures == g0 && k.Log.ErrorCount == l0, tag + " " + mode + " " + p.Id + ": nothing threw");
                            t.Check(KitUiTest.Inside(L.Side, L.In) && KitUiTest.Inside(L.Main, L.In) && L.Side.xMax <= L.Main.xMin && KitUiTest.Inside(L.Content, L.Main),
                                tag + " " + mode + " " + p.Id + ": page list " + KitUiTest.R(L.Side) + " and page " + KitUiTest.R(L.Main) + " inside the window " + KitUiTest.R(L.In));
                            bool headOk = KitUiTest.Inside(L.Title, L.Main, 1f) && (L.Search.width <= 0f || (KitUiTest.Inside(L.Search, L.Main, 1f) && L.Title.xMax <= L.Search.xMin + 0.5f))
                                && (L.Nerd.width <= 0f || (L.Title.xMax <= L.Nerd.xMin + 0.5f && L.Nerd.xMax <= L.Search.xMin + 0.5f));
                            t.Check(headOk, tag + " " + mode + " " + p.Id + ": title " + KitUiTest.R(L.Title) + ", nerd switch " + KitUiTest.R(L.Nerd) + " and search " + KitUiTest.R(L.Search) + " do not overlap");
                            t.Check(L.Main.width >= 560f, tag + " " + mode + " " + p.Id + ": page area wide enough (" + L.Main.width.ToString("0") + " px)");
                            var wr = dlg.windowRect;
                            t.Check(wr.x >= -0.5f && wr.y >= -0.5f && wr.xMax <= real.x + 0.5f && wr.yMax <= real.y + 0.5f, tag + " " + mode + " " + p.Id + ": window rect " + KitUiTest.R(wr) + " inside the real screen");
                            if (!dlg.IsOpen) { t.Check(false, tag + ": the window closed itself on " + p.Id); break; }
                        }
                    }
                    // a search over all pages
                    var first = k.SettingsReg.All.FirstOrDefault(s => !s.Nerd && k.Sections.Available(s.SectionId));
                    if (first != null && dlg.IsOpen)
                    {
                        k.Window.EditTemplate = false;
                        k.Window.SelectPage(k, "general");
                        string word = first.Label.Split(' ').OrderByDescending(w => w.Length).First();
                        k.Window.Search = word;
                        int hits0 = KitUiProbe.Count(k.Window.PageId + "+search");
                        int g0 = k.Guard.TotalFailures, l0 = k.Log.ErrorCount;
                        foreach (var y in KitUiTest.Frames(3)) yield return y;
                        KitUiTest.Shot("kit_window_" + sid + "_" + tag + "_search");
                        foreach (var y in KitUiTest.Frames(2)) yield return y;
                        t.Check(KitUiProbe.Count("page:" + k.Window.PageId + "+search") > 0 || KitUiProbe.Count("page:general+search") > 0, tag + ": search \"" + word + "\" drawn");
                        t.Check(k.Guard.TotalFailures == g0 && k.Log.ErrorCount == l0 && k.Window.Last.ContentH > 30f, tag + ": search results drawn without error (height " + k.Window.Last.ContentH.ToString("0") + ")");
                        k.Window.Search = "";
                    }
                    dlg.Close(false);
                    foreach (var y in KitUiTest.Frames(2)) yield return y;
                    }
                }

                // the global "Window size" preference never pushes the window off a small screen
                var self = Framework.Self; var ws = self?.SettingsReg.Find(Framework.WindowScaleKey);
                if (ws != null)
                {
                    float was = self.SettingsReg.Value(ws, self.Template);
                    try
                    {
                        foreach (float f in new[] { 0.6f, 2f })
                        {
                            self.SettingsReg.Set(ws, self.Template, f);
                            foreach (var scr in screens)
                            {
                                KitUI.TestScreen = scr;
                                var size = new Dialog_ModSettings(k.Mod).InitialSize;
                                t.Check(InsideScreen(size, scr), (int)scr.x + "x" + (int)scr.y + " at window size " + f.ToString("0.0") + ": " + size.x.ToString("0") + "x" + size.y.ToString("0") + " inside the screen");
                            }
                        }
                    }
                    finally { self.SettingsReg.Set(ws, self.Template, was); }
                }
            }
            finally
            {
                KitUI.TestScreen = null;
                KitFirstStartCard.Suppressed = suppressed;
                k.Window.SelectPage(k, oldPage); k.Window.EditTemplate = oldEdit;
            }
            t.Check(drawnPages > 0 && KitUiProbe.Count("settings") > frames0, "real frames reached the window (" + drawnPages + " pages drawn, " + (KitUiProbe.Count("settings") - frames0) + " frames)");
            t.Check(k.Guard.TotalFailures == guard0 && (Framework.Self?.Guard.TotalFailures ?? 0) == selfGuard0, "no fenced failure while drawing");
            t.Check(k.Log.ErrorCount == log0 && (Framework.Self?.Log.ErrorCount ?? 0) == selfLog0, "no error in the logs while drawing");
            KitSuiteUtil.NoRedErrors(t, red0, "no red error of ours while sizing and drawing");
            if (KitUiTest.ShotDir != null) t.Note(KitUiTest.Shots + " screenshots in " + Path.GetFileName(KitUiTest.ShotDir.TrimEnd('/')));
        }
    }
}

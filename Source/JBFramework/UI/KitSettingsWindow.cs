// SPDX-License-Identifier: MIT
// Decoupled from Hearth & Harbor Hearth/UI/SettingsWindow.cs: difficulty, underworld, content intensity, AI panel, scenario mode and
// tutorial removed; presets row, section switches with availability, custom pages and the Compatibility page added.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// The mod settings window (spec 1.6). Sidebar: mark, "This save / Template" chips (per-save mods in a game), page list with icons
    /// (dimmed while a section is off), "Save as template" / "Apply template". Main: header with icon, title, nerd switch and search;
    /// pages Overview (presets, section switches), General, one per section with settings, custom pages, Compatibility, Diagnosis, About.
    /// Everything is drawn against an explicit profile, so the template can be edited while a game runs. Each page draws inside its own
    /// exception fence, so a bad row never breaks the window.
    /// </summary>
    internal static class KitSettingsWindow
    {
        internal sealed class PageEntry
        {
            public string Id, Label; public bool Dim; public string DimReason; public Texture2D Icon;
        }

        /// <summary>The profile the window edits: the template, or the running save's profile ("This save").</summary>
        public static KitProfile Target(KitMod k) => k.Window.EditTemplate || !k.HasSaveProfile ? k.Template : k.SaveProfile;
        static bool InGame(KitMod k) => k.HasSaveProfile;
        static bool ShowNerd => Framework.ShowNerd;
        static bool AnyNerd(KitMod k) => k.SettingsReg.All.Any(s => s.Nerd);

        // Pages ---------------------------------------------------------------------------------------------------------
        static bool SectionHasRows(KitMod k, string id) => k.SettingsReg.All.Any(s => s.SectionId == id && s.Shown) || k.Pages.All.Any(p => p.Embedded && p.Section == id);

        public static List<PageEntry> BuildPages(KitMod k, KitProfile target)
        {
            var list = new List<PageEntry>();
            // Overview only when there is something to switch or pick (the framework's own window starts with its Mods page)
            if (!k.IsFramework || k.Sections.All.Count > 0 || k.Presets.All.Count > 0)
                list.Add(new PageEntry { Id = "overview", Label = KitText.K("Page.Overview", "Overview") });
            foreach (var p in k.Pages.All.Where(x => x.Order < 0)) AddPage(k, target, list, p);
            list.Add(new PageEntry { Id = "general", Label = KitText.K("Page.General", "General") });
            foreach (var s in k.Sections.All)
            {
                if (!k.Sections.Available(s.Id) || !SectionHasRows(k, s.Id)) continue;
                bool on = target.SectionOn(s.Id) && !k.Guard.IsPaused(s.Id);
                list.Add(new PageEntry { Id = "section:" + s.Id, Label = s.Label, Dim = !on, DimReason = k.Guard.IsPaused(s.Id) ? KitText.K("Side.Paused", "paused") : KitText.K("Side.Off", "off") });
            }
            foreach (var p in k.Pages.All.Where(x => x.Order >= 0)) AddPage(k, target, list, p);
            list.Add(new PageEntry { Id = "compat", Label = KitText.K("Page.Compat", "Compatibility") });
            list.Add(new PageEntry { Id = "diagnosis", Label = KitText.K("Page.Diagnosis", "Diagnosis") });
            list.Add(new PageEntry { Id = "about", Label = KitText.K("Page.About", "About") });
            foreach (var e in list) e.Icon = KitIcons.Page(k, e.Id);
            return list;
        }

        static void AddPage(KitMod k, KitProfile target, List<PageEntry> list, KitPage p)
        {
            if (p.Embedded && !string.IsNullOrEmpty(p.Section)) return;   // drawn inside its section's page
            bool dim = false;
            if (!string.IsNullOrEmpty(p.Section))
            {
                if (!k.Sections.Available(p.Section)) return;
                dim = !target.SectionOn(p.Section) || k.Guard.IsPaused(p.Section);
            }
            list.Add(new PageEntry { Id = "page:" + p.Id, Label = p.Label, Dim = dim, DimReason = KitText.K("Side.Off", "off") });
        }

        /// <summary>Draws mod <paramref name="k"/>'s settings window (called from KitModBase.DoSettingsWindowContents). Fenced per mod.</summary>
        public static void Draw(KitMod k, Rect inRect)
        {
            var prev = KitMod.Drawing;
            KitMod.Drawing = k;
            try
            {
                if (k.Blocked) { DrawBlocked(k, inRect); return; }
                k.Guard.Run(KitMod.Core, "UI.SettingsWindow", () => DrawInner(k, inRect));
            }
            finally { KitMod.Drawing = prev; }
        }

        static void DrawBlocked(KitMod k, Rect r)
        {
            KitStyle.Heading(new Rect(r.x, r.y, r.width, 34f), k.Name);
            KitStyle.Paragraph(r.x, r.y + 44f, Mathf.Min(r.width, 760f), k.BlockedReason ?? "", KitStyle.Bad);
        }

        /// <summary>Width of the page list: a fifth of the window, 200 to 260 px.</summary>
        public static float SideWidth(float windowW) => Mathf.Clamp(windowW * 0.2f, 200f, 260f);

        static void DrawInner(KitMod k, Rect inRect)
        {
            KitUiProbe.Hit("settings");
            if (!InGame(k)) k.Window.EditTemplate = true;
            var target = Target(k);
            var pages = BuildPages(k, target);
            if (pages.All(p => p.Id != k.Window.PageId)) k.Window.PageId = pages[0].Id;

            var side = new Rect(inRect.x, inRect.y, SideWidth(inRect.width), inRect.height);
            var main = new Rect(side.xMax + 12f, inRect.y, inRect.width - side.width - 12f, inRect.height);
            k.Window.Last.In = inRect; k.Window.Last.Side = side; k.Window.Last.Main = main; k.Window.Last.Page = k.Window.PageId; k.Window.Last.Frame = Time.frameCount;
            DrawSidebar(k, side, pages);
            Widgets.DrawBoxSolid(new Rect(side.xMax + 5f, inRect.y, 1f, inRect.height), KitStyle.Line);
            DrawMain(k, main, pages, target);
        }

        // Sidebar -------------------------------------------------------------------------------------------------------
        static void DrawSidebar(KitMod k, Rect r, List<PageEntry> pages)
        {
            float y = r.y;
            var mark = KitIcons.Mark(k);
            string name = k.Cfg?.Name ?? "";
            float mx = r.x;
            if (mark != null) { KitIcons.Draw(new Rect(r.x, y, 32f, 32f), mark); mx = r.x + 38f; }
            KitStyle.LabelColored(new Rect(mx, y, r.xMax - mx, 32f), name.Truncate(r.xMax - mx), KitStyle.Accent, TextAnchor.MiddleLeft);
            if (KitUI.TextW(name) > r.xMax - mx) TooltipHandler.TipRegion(new Rect(r.x, y, r.width, 32f), name);
            y += 38f;

            if (InGame(k))
            {
                string l1 = KitText.K("Side.ThisSave", "This save"), l2 = KitText.K("Side.Template", "Template");
                float hw = (r.width - 6f) / 2f;
                // side by side when both labels fit at the normal font ("Dieser Spielstand" does not in a narrow sidebar): else stacked
                bool stack = Mathf.Max(KitUI.TextW(l1), KitUI.TextW(l2)) + 24f > hw;
                var c1 = stack ? new Rect(r.x, y, r.width, 30f) : new Rect(r.x, y, hw, 32f);
                var c2 = stack ? new Rect(r.x, y + 34f, r.width, 30f) : new Rect(r.x + hw + 6f, y, hw, 32f);
                if (KitStyle.Chip(c1, l1, !k.Window.EditTemplate, KitText.K("Side.ThisSaveTip", "Edit the settings of the running game. They are saved with it."))) k.Window.EditTemplate = false;
                if (KitStyle.Chip(c2, l2, k.Window.EditTemplate, KitText.K("Side.TemplateTip", "Edit the template that new games start from. The running game is not changed."))) k.Window.EditTemplate = true;
                k.Window.Last.Chips = new Rect(r.x, y, r.width, c2.yMax - y);
                y += stack ? 72f : 40f;
            }
            else
            {
                string note = k.Cfg != null && k.Cfg.PerSave
                    ? KitText.K("Side.TemplateOnly", "Template for new games. Each new colony gets its own copy.")
                    : KitText.K("Side.Global", "These settings apply to every game.");
                y += KitStyle.Paragraph(r.x, y, r.width, note, KitStyle.Grey, GameFont.Tiny) + 6f;
            }

            float bottomH = InGame(k) ? 76f : 0f;
            var listR = new Rect(r.x, y, r.width, r.height - (y - r.y) - bottomH);
            const float pageRowH = 34f;
            float contentH = pages.Count * (pageRowH + 2f);
            var view = new Rect(0, 0, listR.width - (contentH > listR.height ? 16f : 0f), contentH);
            bool anyIcon = pages.Any(p => p.Icon != null);
            Widgets.BeginScrollView(listR, ref k.Window.ScrollSide, view);
            float py = 0;
            foreach (var p in pages)
            {
                var row = new Rect(0, py, view.width, pageRowH);
                bool sel = p.Id == k.Window.PageId;
                if (sel) { Widgets.DrawBoxSolid(row, KitStyle.AccentSoft); Widgets.DrawBoxSolid(new Rect(row.x, row.y, 3f, row.height), KitStyle.Accent); }
                else if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                float lx = row.x + 10f;
                if (anyIcon) { KitIcons.Draw(new Rect(row.x + 8f, row.y + 5f, 24f, 24f), p.Icon, p.Dim); lx = row.x + 38f; }
                string plabel = p.Label + (p.Dim ? "  (" + p.DimReason + ")" : "");
                var plr = new Rect(lx, row.y, row.xMax - lx - 2f, row.height);
                KitStyle.LabelColored(plr, plabel.Truncate(plr.width), p.Dim ? KitStyle.DimGrey : Color.white, TextAnchor.MiddleLeft);
                if (KitUI.TextW(plabel) > plr.width) TooltipHandler.TipRegion(row, plabel);
                if (Widgets.ButtonInvisible(row)) { k.Window.PageId = p.Id; k.Window.Search = ""; KitRow.Unfocus(); k.Window.ScrollMain = Vector2.zero; k.Window.MainViewH = 1000f; }
                py += pageRowH + 2f;
            }
            Widgets.EndScrollView();

            if (InGame(k))
            {
                float by = r.yMax - bottomH + 6f;
                var b1 = new Rect(r.x, by, r.width, 32f);
                if (Widgets.ButtonText(b1, KitText.K("Side.SaveAsTemplate", "Save as template")))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        KitText.K("Side.SaveAsTemplateConfirm", "Replace the template for new games with the settings of this save?"), () => { k.SaveAsTemplate(); AfterBulkChange(k); }));
                TooltipHandler.TipRegion(b1, KitText.K("Side.SaveAsTemplateTip", "Copies the section switches and all settings of this save into the template for new games."));
                var b2 = new Rect(r.x, by + 36f, r.width, 32f);
                if (Widgets.ButtonText(b2, KitText.K("Side.ApplyTemplate", "Apply template")))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        KitText.K("Side.ApplyTemplateConfirm", "Replace the section switches and all settings of this save with the template? This cannot be undone."), () => { k.ApplyTemplateToSave(); AfterBulkChange(k); }, true));
                TooltipHandler.TipRegion(b2, KitText.K("Side.ApplyTemplateTip", "Replaces the settings of this save with the template. Preferences that apply to all games stay."));
            }
        }

        static void AfterBulkChange(KitMod k)
        {
            KitRow.ClearBuffers();
            k.Window.PresetCacheTime = -1f;
        }

        // Main area -----------------------------------------------------------------------------------------------------
        static void DrawMain(KitMod k, Rect r, List<PageEntry> pages, KitProfile target)
        {
            var page = pages.First(p => p.Id == k.Window.PageId);
            float tx = r.x;
            var picon = string.IsNullOrEmpty(k.Window.Search) ? page.Icon : null;
            if (picon != null) { KitIcons.Draw(new Rect(r.x, r.y + 3f, 28f, 28f), picon); tx += 36f; }
            bool searchable = k.Window.PageId != "diagnosis" && k.Window.PageId != "about";
            float rightW = 0f;
            Rect sr = Rect.zero, nr = Rect.zero;
            bool nerdToggle = searchable && AnyNerd(k);
            string nerdLabel = KitText.K("Header.Nerd", "Nerd settings");
            if (searchable)
            {
                float sw = Mathf.Clamp(r.width * 0.26f, 180f, 300f);
                sr = new Rect(r.xMax - sw, r.y + 2f, sw, 30f);
                float clearW = k.Window.Search.NullOrEmpty() ? 0f : 34f;
                rightW = r.xMax - sr.x + clearW + 12f;
                if (nerdToggle)
                {
                    float nw = Mathf.Clamp(KitUI.TextW(nerdLabel) + 40f, 120f, 240f);
                    nr = new Rect(sr.x - clearW - nw - 12f, r.y + 4f, nw, 26f);
                    rightW = r.xMax - nr.x + 12f;
                }
            }
            string headTitle = string.IsNullOrEmpty(k.Window.Search) ? page.Label : KitText.K("Header.SearchResults", "Search results");
            Text.Font = GameFont.Medium;
            float titleW = Mathf.Max(120f, r.width - rightW - (tx - r.x));
            k.Window.Last.Title = new Rect(tx, r.y, Mathf.Min(titleW, KitUI.TextW(headTitle, GameFont.Medium)), 34f); k.Window.Last.Search = sr; k.Window.Last.Nerd = nr;
            Widgets.Label(new Rect(tx, r.y, titleW, 34f), headTitle.Truncate(titleW));
            Text.Font = GameFont.Small;
            Widgets.DrawBoxSolid(new Rect(r.x, r.y + 35f, Mathf.Min(r.width, 180f), 2f), KitStyle.Accent);
            if (searchable)
            {
                k.Window.Search = Widgets.TextField(sr, k.Window.Search) ?? "";
                if (k.Window.Search.NullOrEmpty())
                {
                    // one line only: a long translation is cut with "..." instead of wrapping to a second line
                    var phR = new Rect(sr.x + 6f, sr.y, sr.width - 8f, sr.height);
                    KitStyle.LabelColored(phR, KitText.K("Header.Search", "Search all settings...").Truncate(phR.width), KitStyle.DimGrey, TextAnchor.MiddleLeft);
                }
                else if (Widgets.ButtonText(new Rect(sr.x - 32f, sr.y, 28f, 30f), "x")) k.Window.Search = ""; KitRow.Unfocus();
                if (nerdToggle)
                {
                    bool nerd = ShowNerd, nb = nerd;
                    Widgets.CheckboxLabeled(nr, nerdLabel, ref nb, false, null, null, true);
                    TooltipHandler.TipRegion(nr, KitText.K("Header.NerdTip", "Shows the technical settings that most players never need. Everything else is unchanged."));
                    if (nb != nerd) SetGlobalPref(Framework.ShowNerdKey, nb);
                }
            }
            float top = r.y + 44f;
            var shownPage = k.Window.PageId.StartsWith("page:") && string.IsNullOrEmpty(k.Window.Search) ? k.Pages.Find(k.Window.PageId.Substring(5)) : null;
            if (shownPage != null && shownPage.IsGlobal && InGame(k))
            {
                KitStyle.LabelColored(new Rect(r.x, top, r.width, 22f), KitText.K("Header.GlobalPage", "Applies to every game: this page is not saved per save."), KitStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
                top += 26f;
            }
            else if (InGame(k) && k.Window.EditTemplate)
            {
                KitStyle.LabelColored(new Rect(r.x, top, r.width, 22f), KitText.K("Header.EditingTemplate", "Editing the template for new games. The running game is not changed."), KitStyle.Accent, TextAnchor.UpperLeft, GameFont.Tiny);
                top += 26f;
            }
            var outR = new Rect(r.x, top, r.width, r.yMax - top);
            k.Window.Last.Content = outR; k.Window.Last.ContentH = 0f;
            KitUiProbe.Hit("page:" + k.Window.PageId + (string.IsNullOrEmpty(k.Window.Search) ? "" : "+search"));

            // custom pages draw into the whole area themselves (their own scrolling)
            if (string.IsNullOrEmpty(k.Window.Search) && k.Window.PageId.StartsWith("page:"))
            {
                var cp = k.Pages.Find(k.Window.PageId.Substring(5));
                if (cp?.Draw != null)
                {
                    float y0 = outR.y;
                    if (page.Dim)
                    {
                        string off = KitText.K("Page.CustomOff", "The section \"{0}\" is switched off. This page has no effect until it is on.", k.Sections.LabelOf(cp.Section));
                        if (KitStyle.Banner(outR.x, y0, outR.width, off, KitStyle.Accent, KitText.K("Section.SwitchOn", "Switch on"), out float bh) && k.Sections.Available(cp.Section))
                            target.SetSection(cp.Section, true);
                        y0 += bh + 8f;
                    }
                    var area = new Rect(outR.x, y0, outR.width, outR.yMax - y0);
                    KitUiFence.DrawPage(k, cp, area);
                }
                return;
            }

            var view = new Rect(0, 0, outR.width - 18f, k.Window.MainViewH);
            var visible = new Rect(0, k.Window.ScrollMain.y, view.width, outR.height);
            Widgets.BeginScrollView(outR, ref k.Window.ScrollMain, view);
            float h = 40f;
            try
            {
                if (searchable && !string.IsNullOrEmpty(k.Window.Search)) h = DrawSearch(k, view.width, target, visible);
                else if (k.Window.PageId == "overview") h = DrawOverview(k, view.width, target);
                else if (k.Window.PageId == "general") h = DrawGeneral(k, view.width, target, visible);
                else if (k.Window.PageId == "compat") h = DrawCompat(k, view.width);
                else if (k.Window.PageId == "diagnosis") h = KitDiagnosis.Draw(k, view.width);
                else if (k.Window.PageId == "about") h = DrawAbout(k, view.width);
                else if (k.Window.PageId.StartsWith("section:")) h = DrawSectionPage(k, k.Window.PageId.Substring(8), view.width, target, visible);
            }
            catch (Exception e) { k.Log.Once("settingsPage:" + k.Window.PageId + e.GetType().Name, KitLogLevel.Error, "UI", "Settings page " + k.Window.PageId + " failed: " + e); }
            k.Window.MainViewH = Mathf.Max(h + 20f, 40f);
            k.Window.Last.ContentH = h;
            Widgets.EndScrollView();
        }

        static bool Visible(KitSetting s) => (ShowNerd || !s.Nerd) && s.Shown && s.Owner.Sections.Available(s.SectionId);

        /// <summary>Writes one of the framework's global preferences (they live in the framework's own settings file).</summary>
        static void SetGlobalPref(string key, bool on)
        {
            var self = Framework.Self;
            var ns = self?.SettingsReg.Find(key);
            if (ns == null) return;
            self.SettingsReg.Set(ns, self.Template, on ? 1f : 0f);
            self.WriteSettings();
            Framework.RefreshGlobals();
        }

        // Overview ------------------------------------------------------------------------------------------------------
        static float DrawOverview(KitMod k, float w, KitProfile target)
        {
            float y = 0f;
            string intro = k.Cfg == null || !k.Cfg.PerSave ? KitText.K("Overview.IntroGlobal", "These settings apply to every game. Switch whole sections on or off here; the pages on the left hold the details.")
                : ReferenceEquals(target, k.Template) ? KitText.K("Overview.IntroTemplate", "The template for new games. Each new colony gets its own copy, and you can change it there later.")
                : KitText.K("Overview.IntroSave", "The settings of this save. The template for new games stays as it is.");
            y += KitStyle.Paragraph(0f, y, w, intro, KitStyle.Grey) + 10f;

            foreach (var id in k.Guard.Paused.ToList())
            {
                string text = KitText.K("Overview.Paused", "{0} paused itself after repeated errors.", k.Sections.LabelOf(id));
                if (KitStyle.Banner(0f, y, w, text, KitStyle.Bad, KitText.K("Diag.Resume", "Resume"), out float bh)) k.Guard.Resume(id);
                y += bh + 6f;
            }

            if (k.Presets.All.Count > 0) y = DrawPresets(k, w, target, y) + 8f;

            if (k.Sections.All.Count > 0)
            {
                y += KitStyle.Section(0f, y, w, KitText.K("Overview.Sections", "Sections"));
                foreach (var s in k.Sections.All) y += DrawSectionRow(k, 0f, y, w, s, target) + 2f;
            }
            else if (k.Presets.All.Count == 0)
            {
                y += KitStyle.Paragraph(0f, y, w, KitText.K("Overview.NoSections", "This mod has no switchable sections. All its settings are on the General page."), KitStyle.DimGrey) + 6f;
            }
            return y + 6f;
        }

        // presets are compared by applying them to a scratch copy: cached for a short while, the window draws every frame

        static KitPreset MatchingPreset(KitMod k, KitProfile target)
        {
            float now = Time.realtimeSinceStartup;
            if (!ReferenceEquals(target, k.Window.PresetCacheTarget) || k.Window.PresetCacheTime < 0f || now - k.Window.PresetCacheTime > 0.4f || k.HasPendingChanges)
            {
                k.Window.PresetCacheTarget = target; k.Window.PresetCacheTime = now;
                k.Window.PresetCacheMatch = k.Presets.Matching(target);
            }
            return k.Window.PresetCacheMatch;
        }

        static float DrawPresets(KitMod k, float w, KitProfile target, float y)
        {
            y += KitStyle.Section(0f, y, w, KitText.K("Overview.Presets", "Presets"));
            var match = MatchingPreset(k, target);
            var chips = k.Presets.All.Select(p => (preset: p, label: p.Label)).ToList();
            string custom = KitText.K("Overview.Custom", "Custom");
            float x = 0f, chipH = 32f;
            void Next(float cw) { if (x > 0f && x + cw > w) { x = 0f; y += chipH + 6f; } }
            foreach (var (p, label) in chips)
            {
                float cw = Mathf.Clamp(KitUI.TextW(label) + 40f, 110f, 240f);
                Next(cw);
                string tip = p.Desc;
                if (KitStyle.Chip(new Rect(x, y, cw, chipH), label, ReferenceEquals(p, match), tip)) ChoosePreset(k, p, target);
                x += cw + 6f;
            }
            float ccw = Mathf.Clamp(KitUI.TextW(custom) + 40f, 110f, 240f);
            Next(ccw);
            KitStyle.Chip(new Rect(x, y, ccw, chipH), custom, match == null, KitText.K("Overview.CustomTip", "Your own mix: no preset matches the current values."), enabled: false);
            y += chipH + 4f;
            string hint = KitText.K("Overview.PresetHint", "A preset changes only the values it names; everything else stays as you set it.");
            y += KitStyle.Paragraph(0f, y, w, hint, KitStyle.DimGrey, GameFont.Tiny) + 4f;
            return y;
        }

        static void ChoosePreset(KitMod k, KitPreset p, KitProfile target)
        {
            var over = k.Presets.WouldOverwrite(p, target);
            void Apply() { k.Presets.Apply(p, target); AfterBulkChange(k); }
            if (over.Count == 0) { Apply(); return; }
            var names = over.Select(key => key.StartsWith("section:") ? k.Sections.LabelOf(key.Substring(8)) : (k.SettingsReg.Find(key)?.Label ?? key)).ToList();
            string list = string.Join(", ", names.Take(8)) + (names.Count > 8 ? ", ..." : "");
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                KitText.K("Overview.PresetConfirm", "Apply the preset \"{0}\"? It overwrites {1} values you changed: {2}", p.Label, names.Count, list), Apply, true));
        }

        /// <summary>A switch that cannot be used now (DLC or mod missing): a grey dash in the checkbox's place.</summary>
        internal static void UnavailableDash(Rect r)
        {
            KitStyle.LabelColored(r, "–", KitStyle.DimGrey, TextAnchor.MiddleCenter, GameFont.Medium);
        }

        /// <summary>One section switch row: checkbox, icon, label, description or why it is not available, paused state, page link.</summary>
        static float DrawSectionRow(KitMod k, float x, float y, float w, KitSection s, KitProfile target)
        {
            bool avail = k.Sections.Available(s.Id);
            bool paused = k.Guard.IsPaused(s.Id);
            float lw = Mathf.Clamp(w * 0.24f, 160f, 300f);
            bool hasPage = avail && SectionHasRows(k, s.Id);
            float rightW = (paused ? 96f : 0f) + (hasPage ? 34f : 0f);
            // "(not installed)" only for a missing mod or DLC (OnlyWith); an AvailableWhen reason is shown as written
            string descText = avail ? s.Desc
                : !string.IsNullOrEmpty(s.RequiresPackageId) ? KitText.K("Overview.NotInstalled", "{0} (not installed)", s.RequiresLabel ?? KitText.K("Section.NotAvailable", "Not available in this game."))
                : s.RequiresLabel ?? KitText.K("Section.NotAvailable", "Not available in this game.");
            float descW = w - lw - 74f - rightW;
            float h = Mathf.Max(44f, KitStyle.CachedHeight(descText, descW, GameFont.Tiny) + 12f);
            var row = new Rect(x, y, w, h);
            if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
            bool on = target.SectionOn(s.Id), nb = on;
            // unavailable: a dash, not a greyed tick of the stored value (the value stays for when the DLC or mod comes back; DNDM F1)
            if (avail) Widgets.Checkbox(new Vector2(row.x + 4f, row.y + (h - 24f) / 2f), ref nb, 24f);
            else UnavailableDash(new Rect(row.x + 4f, row.y + (h - 24f) / 2f, 24f, 24f));
            if (avail && nb != on) { target.SetSection(s.Id, nb); k.Window.PresetCacheTime = -1f; }
            KitIcons.Draw(new Rect(row.x + 34f, row.y + (h - 24f) / 2f, 24f, 24f), KitIcons.Section(k, s.Id), !avail || !nb);
            KitStyle.LabelColored(new Rect(row.x + 66f, row.y, lw, h), s.Label, avail ? (nb ? Color.white : KitStyle.Grey) : KitStyle.DimGrey, TextAnchor.MiddleLeft);
            KitStyle.LabelColored(new Rect(row.x + 70f + lw, row.y, descW, h), descText, avail ? KitStyle.Grey : KitStyle.DimGrey, TextAnchor.MiddleLeft, GameFont.Tiny);
            float rx = row.xMax;
            if (hasPage)
            {
                rx -= 30f;
                var pr = new Rect(rx, row.y + (h - 26f) / 2f, 28f, 26f);
                if (Widgets.ButtonText(pr, "»")) { k.Window.PageId = "section:" + s.Id; k.Window.Search = ""; KitRow.Unfocus(); k.Window.ScrollMain = Vector2.zero; }
                TooltipHandler.TipRegion(pr, KitText.K("Overview.OpenPage", "Open the settings of this section"));
                rx -= 4f;
            }
            if (paused)
            {
                rx -= 92f;
                if (Widgets.ButtonText(new Rect(rx, row.y + (h - 26f) / 2f, 90f, 26f), KitText.K("Diag.Resume", "Resume"))) k.Guard.Resume(s.Id);
                KitStyle.LabelColored(new Rect(row.x + 66f, row.yMax - 14f, lw, 14f), KitText.K("Diag.Paused", "paused after errors"), KitStyle.Bad, TextAnchor.UpperLeft, GameFont.Tiny);
            }
            TooltipHandler.TipRegion(row, s.Desc + (avail ? "" : "\n\n" + descText));
            return h;
        }

        // Section page --------------------------------------------------------------------------------------------------
        static float DrawSectionPage(KitMod k, string id, float w, KitProfile target, Rect visible)
        {
            var s = k.Sections.Find(id);
            if (s == null) return 40f;
            float y = 0f;
            string rl = KitText.K("Section.Reset", "Reset section");
            float rbw = Mathf.Clamp(KitUI.TextW(rl) + 32f, 140f, 240f);
            float dh = Mathf.Max(36f, KitStyle.CachedHeight(s.Desc, w - rbw - 16f, GameFont.Small));
            KitStyle.LabelColored(new Rect(0f, y, w - rbw - 12f, dh), s.Desc, KitStyle.Grey);
            if (Widgets.ButtonText(new Rect(w - rbw, y, rbw, 30f), rl))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    KitText.K("Section.ResetConfirm", "Reset every setting of {0} to its default?", s.Label), () => { k.SettingsReg.ResetSection(id, target); AfterBulkChange(k); k.Log.Info("UI", "Settings of section " + id + " reset"); }, true));
            y += dh + 6f;

            if (k.Guard.IsPaused(id))
            {
                if (KitStyle.Banner(0f, y, w, k.Guard.ReasonFor(id) ?? KitText.K("Diag.Paused", "paused after errors"), KitStyle.Bad, KitText.K("Diag.Resume", "Resume"), out float bh)) k.Guard.Resume(id);
                y += bh + 8f;
            }
            else if (!target.SectionOn(id))
            {
                string off = ReferenceEquals(target, k.Template) && k.Cfg != null && k.Cfg.PerSave
                    ? KitText.K("Section.IsOffTemplate", "This section is off in the template. New games start without it.")
                    : KitText.K("Section.IsOff", "This section is switched off. Its settings have no effect.");
                if (KitStyle.Banner(0f, y, w, off, KitStyle.Accent, KitText.K("Section.SwitchOn", "Switch on"), out float bh)) { target.SetSection(id, true); k.Window.PresetCacheTime = -1f; }
                y += bh + 8f;
            }

            // only against the running game's own profile (PrepareRemoval switches the section off in k.Current)
            if (s.PrepareRemoval != null && Current.ProgramState == ProgramState.Playing && ReferenceEquals(target, k.Current))
            {
                string pr = KitText.K("Section.PrepareRemoval", "Prepare for removal");
                float pw = Mathf.Clamp(KitUI.TextW(pr) + 32f, 180f, 300f);
                var b = new Rect(0f, y, pw, 30f);
                if (Widgets.ButtonText(b, pr))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        KitText.K("Section.PrepareRemovalConfirm", "Prepare this game for living without {0}? The section is switched off and what it placed in the save is cleaned up. Save a backup first.", s.Label),
                        () => KitDiagnosis.RunPrepareRemoval(k, id), true));
                TooltipHandler.TipRegion(b, KitText.K("Section.PrepareRemovalTip", "Switches the section off and removes what it put into this save, so the save also loads without it."));
                y += 38f;
            }

            float before = y;
            y = DrawRows(k, k.SettingsReg.OfSection(id), w, target, visible, y);
            var embedded = k.Pages.All.Where(p => p.Embedded && p.Section == id).ToList();
            if (y == before && embedded.Count == 0)
                y += KitStyle.Paragraph(0f, y, w, KitText.K("Section.OnlyNerd", "Every setting of this section is a nerd setting. Switch on \"Nerd settings\" at the top to see them."), KitStyle.DimGrey) + 6f;
            // pages that belong into this section (KitPage.EmbedInSection): below the settings, under their own heading
            foreach (var p in embedded)
            {
                y += 8f;
                y += KitStyle.Section(0f, y, w, p.Label);
                if (p.IsGlobal && InGame(k))
                    y += KitStyle.Paragraph(0f, y, w, KitText.K("Header.GlobalPage", "Applies to every game: this page is not saved per save."), KitStyle.Grey, GameFont.Tiny) + 4f;
                KitUiFence.DrawPage(k, p, new Rect(0f, y, w, p.EmbedHeight));
                y += p.EmbedHeight + 6f;
            }
            return y;
        }

        /// <summary>Rows grouped by category (a header per category), nerd filter applied.</summary>
        static float DrawRows(KitMod k, IEnumerable<KitSetting> settings, float w, KitProfile target, Rect visible, float y)
        {
            string last = null;
            // one header per group: rows of a group registered later join the group's first place (stable otherwise)
            var list = settings.ToList();
            var firstOf = new Dictionary<string, int>();
            for (int i = 0; i < list.Count; i++) { string c = list[i].Category ?? ""; if (!firstOf.ContainsKey(c)) firstOf[c] = i; }
            var ordered = list.Select((x, i) => (x, i)).OrderBy(p => firstOf[p.x.Category ?? ""]).ThenBy(p => p.i).Select(p => p.x);
            foreach (var s in ordered)
            {
                if ((!ShowNerd && s.Nerd) || !s.Shown) continue;
                if (s.Category != last)
                {
                    y += 6f;
                    if (!string.IsNullOrEmpty(s.Category)) y += KitStyle.Section(0f, y, w, s.CategoryLabel);
                    last = s.Category;
                }
                y += KitRow.Draw(0f, y, w, s, target, visible);
            }
            return y;
        }

        // General -------------------------------------------------------------------------------------------------------
        static float DrawGeneral(KitMod k, float w, KitProfile target, Rect visible)
        {
            float y = 0f;
            y += KitStyle.Paragraph(0f, y, w, KitText.K("General.Intro", "Settings of the whole mod and of this window."), KitStyle.Grey) + 4f;
            var own = k.SettingsReg.OfSection(KitMod.Core).ToList();
            y = DrawRows(k, own.OrderBy(s => s.Category ?? "").ThenBy(s => s.Order).ToList(), w, target, visible, y);
            y += 6f;
            if (k.IsFramework)
            {
                y += KitStyle.Paragraph(0f, y, w, KitText.K("General.ScaleNote", "A new window size applies the next time a window opens."), KitStyle.DimGrey, GameFont.Tiny) + 6f;
                return y;
            }
            if (own.Count == 0)
                y += KitStyle.Paragraph(0f, y, w, KitText.K("General.NoOwn", "This mod has no general settings."), KitStyle.DimGrey) + 6f;
            // window size, detailed log, welcome cards and nerd settings are shared by every JB mod: one place, in the framework's page
            string note = KitText.K("General.GlobalNote", "Window size, detailed log, welcome cards and nerd settings are the same for every mod built on {0}: they live in Mod settings, {0}.", Framework.Name);
            y += KitStyle.Paragraph(0f, y, w, note, KitStyle.Grey, GameFont.Tiny) + 6f;
            string open = KitText.K("General.OpenFramework", "Open {0} settings", Framework.Name);
            if (Framework.Self != null && Widgets.ButtonText(new Rect(0f, y, Mathf.Clamp(KitUI.TextW(open) + 36f, 200f, 360f), 30f), open))
            {
                var self = Framework.Self;
                Find.WindowStack.TryRemove(typeof(Dialog_ModSettings), true);
                self.OpenSettings("general");
            }
            y += 38f;
            return y;
        }

        // Search --------------------------------------------------------------------------------------------------------
        static float DrawSearch(KitMod k, float w, KitProfile target, Rect visible)
        {
            float y = 0f; int n = 0;
            string q = k.Window.Search.ToLowerInvariant();
            var secs = k.Sections.All.Where(s => (s.Label ?? "").ToLowerInvariant().Contains(q) || (s.Desc ?? "").ToLowerInvariant().Contains(q) || s.Id.Contains(q)).ToList();
            if (secs.Count > 0)
            {
                y += KitStyle.Section(0f, y, w, KitText.K("Overview.Sections", "Sections"));
                foreach (var s in secs) { y += DrawSectionRow(k, 0f, y, w, s, target) + 2f; n++; }
                y += 8f;
            }
            var groups = k.SettingsReg.All.Where(s => Visible(s) && KitRow.MatchesSearch(s, k.Window.Search)).GroupBy(s => s.SectionId)
                .OrderBy(g => g.Key == KitMod.Core ? -1 : k.Sections.All.FindIndex(x => x.Id == g.Key));
            foreach (var g in groups)
            {
                string title = g.Key == KitMod.Core ? KitText.K("Page.General", "General") : k.Sections.LabelOf(g.Key);
                y += KitStyle.Section(0f, y, w, title);
                foreach (var s in g) { y += KitRow.Draw(0f, y, w, s, target, visible); n++; }
                y += 8f;
            }
            if (n == 0)
            {
                string none = ShowNerd ? KitText.K("Search.NoMatchNerd", "No setting matches.")
                    : KitText.K("Search.NoMatch", "No setting matches. Nerd settings are hidden unless you switch them on.");
                KitStyle.LabelColored(new Rect(0f, 0f, w, 30f), none, KitStyle.Grey);
                y = 36f;
            }
            return y;
        }

        // Compatibility -------------------------------------------------------------------------------------------------
        static float DrawCompat(KitMod k, float w)
        {
            float y = 0f;
            y += KitStyle.Paragraph(0f, y, w, KitText.K("Compat.Intro", "What this mod detected in your game, and which integrations with other mods are working."), KitStyle.Grey) + 8f;

            y += KitStyle.Section(0f, y, w, KitText.K("Compat.Dlc", "DLC"));
            foreach (var d in KitCompat.Dlcs)
            {
                bool on = KitCompat.IsModActive(d.Id);
                KitStyle.LabelColored(new Rect(4f, y, 220f, 24f), d.Name, on ? Color.white : KitStyle.DimGrey, TextAnchor.MiddleLeft);
                KitStyle.LabelColored(new Rect(230f, y, w - 230f, 24f), on ? KitText.K("Compat.Active", "active") : KitText.K("Compat.NotInstalled", "not installed"), on ? KitStyle.Good : KitStyle.DimGrey, TextAnchor.MiddleLeft);
                y += 24f;
            }
            y += 8f;

            var integrations = k.Compat.All.Where(c => !c.Incompatible).ToList();
            var conflicts = k.Compat.All.Where(c => c.Incompatible).ToList();
            y += KitStyle.Section(0f, y, w, KitText.K("Compat.Integrations", "Integrations"));
            if (integrations.Count == 0) { KitStyle.LabelColored(new Rect(4f, y, w, 24f), KitText.K("Compat.None", "No integrations with other mods."), KitStyle.DimGrey); y += 26f; }
            foreach (var c in integrations) y += CompatRow(k, c, w, y);
            if (conflicts.Count > 0)
            {
                y += 8f;
                y += KitStyle.Section(0f, y, w, KitText.K("Compat.Conflicts", "Known conflicts"));
                foreach (var c in conflicts) y += CompatRow(k, c, w, y);
            }
            float yy = y + 8f;
            KitHooks.Each(KitHooks.CompatPage, k, "CompatPage", h => yy = h(k, w, yy));
            if (yy > y + 8f) y = yy;
            return y + 6f;
        }

        static Color StateColor(KitCompatState st)
        {
            switch (st)
            {
                case KitCompatState.Active: case KitCompatState.IncompatibleAbsent: return KitStyle.Good;
                case KitCompatState.Failed: case KitCompatState.IncompatiblePresent: return KitStyle.Bad;
                case KitCompatState.Unhealthy: case KitCompatState.SwitchedOff: case KitCompatState.Inactive: return KitStyle.Accent;
                default: return KitStyle.DimGrey;
            }
        }

        static float CompatRow(KitMod k, KitCompatEntry c, float w, float y)
        {
            var st = c.State;
            float nameW = Mathf.Clamp(w * 0.3f, 180f, 300f);
            string stateText = c.StateLabel;
            float stH = Mathf.Max(22f, KitStyle.CachedHeight(stateText, w - nameW - 10f, GameFont.Small));
            string what = c.What;
            float whatH = KitStyle.CachedHeight(what, w - nameW - 10f, GameFont.Tiny);
            float h = Mathf.Max(28f, stH + whatH + 6f);
            var row = new Rect(0f, y, w, h);
            if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
            bool present = st != KitCompatState.NotInstalled && st != KitCompatState.IncompatibleAbsent;
            float nx = 4f;
            if (c.CanSwitch && !c.Incompatible)
            {
                // the integration's switch (PLAN G13c): a preference of the installation, greyed while the other mod is missing
                bool on = c.SwitchedOn, nb = on;
                if (present) Widgets.Checkbox(new Vector2(4f, y + 1f), ref nb, 24f);
                else UnavailableDash(new Rect(4f, y + 1f, 24f, 24f));
                if (present && nb != on) k.SetCompat(c.PackageId, nb);
                TooltipHandler.TipRegion(new Rect(4f, y + 1f, 24f, 24f), KitText.K("Compat.SwitchTip", "Use this integration. Applies to every game (a preference of this installation)."));
                nx = 34f;
            }
            KitStyle.LabelColored(new Rect(nx, y + 2f, nameW - nx - 4f, 24f), c.Name, present ? Color.white : KitStyle.Grey, TextAnchor.UpperLeft);
            KitStyle.LabelColored(new Rect(nameW, y + 2f, w - nameW - 10f, stH), stateText, StateColor(st));
            if (whatH > 0f) KitStyle.LabelColored(new Rect(nameW, y + 2f + stH, w - nameW - 10f, whatH), what, KitStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
            TooltipHandler.TipRegion(row, c.PackageId + (c.PatchesApplied > 0 ? "\n" + KitText.K("Compat.Patches", "{0} patch classes applied", c.PatchesApplied) : ""));
            return h + 2f;
        }

        // About ---------------------------------------------------------------------------------------------------------
        static float DrawAbout(KitMod k, float w)
        {
            var info = k.Cfg;
            float y = 0f, ax = 0f;
            var mark = KitIcons.Mark(k);
            if (mark != null) { KitIcons.Draw(new Rect(0f, y, 64f, 64f), mark); ax = 76f; }
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(ax, y + (ax > 0 ? 6f : 0f), w - ax, 34f), info?.Name ?? ""); Text.Font = GameFont.Small;
            y += ax > 0 ? 40f : 36f;
            string rw; try { rw = VersionControl.CurrentVersionString; } catch { rw = "?"; }
            KitStyle.LabelColored(new Rect(ax, y, w - ax, 24f), (info?.Version ?? "?") + "  ·  RimWorld " + rw, KitStyle.Grey);
            y += ax > 0 ? 34f : 30f;
            float bw = Mathf.Min(w, 700f);
            if (!string.IsNullOrEmpty(k.PitchText)) y += KitStyle.Paragraph(0f, y, bw, k.PitchText, Color.white) + 14f;

            float bx = 0f;
            if (!string.IsNullOrEmpty(info?.WorkshopId))
            {
                string l = KitText.K("About.Workshop", "Steam Workshop page");
                float lw = Mathf.Clamp(KitUI.TextW(l) + 36f, 200f, 320f);
                if (Widgets.ButtonText(new Rect(bx, y, lw, 30f), l)) Application.OpenURL("https://steamcommunity.com/sharedfiles/filedetails/?id=" + info.WorkshopId);
                bx += lw + 10f;
            }
            if (!string.IsNullOrEmpty(info?.Url))
            {
                string l = KitText.K("About.Project", "Project page");
                float lw = Mathf.Clamp(KitUI.TextW(l) + 36f, 160f, 300f);
                if (Widgets.ButtonText(new Rect(bx, y, lw, 30f), l)) Application.OpenURL(info.Url);
                bx += lw + 10f;
            }
            if (bx > 0f) y += 40f;
            if (!string.IsNullOrEmpty(info?.LicenseLine))
            {
                KitStyle.LabelColored(new Rect(0f, y, w, 22f), KitText.K("About.License", "Licence: {0}", info.LicenseLine), KitStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
                y += 22f;
            }
            KitStyle.LabelColored(new Rect(0f, y, w, 22f), KitText.K("About.Framework", "Built on {0} {1}, the settings foundation from Hearth & Harbor.", Framework.Name, Framework.Version), KitStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            return y + 30f;
        }
    }
}

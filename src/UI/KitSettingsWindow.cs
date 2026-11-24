// SPDX-License-Identifier: MIT
// Decoupled from Hearth & Harbor Hearth/UI/SettingsWindow.cs: difficulty, underworld, content intensity, AI panel, scenario mode and
// tutorial removed; presets row, section switches with availability, custom pages and the Compatibility page added.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBKit
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
            public string Id, Label; public bool Dim; public string DimReason;
            public Texture2D Icon => KitIcons.Page(Id);
        }

        static string pageId = "overview";
        static bool editTemplate;
        static string search = "";
        static Vector2 scrollSide, scrollMain;
        static float mainViewH = 1000f;

        /// <summary>Where the last frame put the window's parts (read by the kit.window test to check that they fit and do not overlap).</summary>
        internal struct Layout { public Rect In, Side, Main, Title, Search, Nerd, Content; public float ContentH; public string Page; public int Frame; }
        internal static Layout Last;

        public static string SelectedPage { get => pageId; set => SelectPage(value); }
        public static string Search { get => search; set => search = value ?? ""; }
        public static bool EditTemplate { get => editTemplate; set => editTemplate = value; }

        /// <summary>The profile the window edits: the template, or the running save's profile ("This save").</summary>
        public static KitProfile Target => editTemplate || !Kit.HasSaveProfile ? Kit.Template : Kit.SaveProfile;
        static bool InGame => Kit.HasSaveProfile;
        static bool ShowNerd => Kit.GetBool(KitBuiltins.ShowNerdKey);
        static bool AnyNerd => KitSettings.All.Any(s => s.Nerd);

        /// <summary>Hooked into KitHooks (KitBoot.InstallUI).</summary>
        internal static void Install()
        {
            KitHooks.DrawSettings = Draw;
            KitHooks.SelectPage = SelectPage;
            KitHooks.WindowClosed += OnClosed;
        }

        public static void SelectPage(string id)
        {
            pageId = string.IsNullOrEmpty(id) ? "overview" : id;
            search = ""; scrollMain = Vector2.zero; mainViewH = 1000f;
            if (InGame) editTemplate = false;
        }

        static void OnClosed()
        {
            KitRow.ClearBuffers();
            presetCacheTime = -1f;
        }

        // Pages ---------------------------------------------------------------------------------------------------------
        static bool SectionHasRows(string id) => KitSettings.All.Any(s => s.Section == id);

        public static List<PageEntry> BuildPages(KitProfile target)
        {
            var list = new List<PageEntry>
            {
                new PageEntry { Id = "overview", Label = KitText.K("Page.Overview", "Overview") },
                new PageEntry { Id = "general", Label = KitText.K("Page.General", "General") },
            };
            foreach (var s in KitSections.All)
            {
                if (!KitSections.Available(s.Id) || !SectionHasRows(s.Id)) continue;
                bool on = target.SectionOn(s.Id) && !KitGuard.IsPaused(s.Id);
                list.Add(new PageEntry { Id = "section:" + s.Id, Label = s.Label, Dim = !on, DimReason = KitGuard.IsPaused(s.Id) ? KitText.K("Side.Paused", "paused") : KitText.K("Side.Off", "off") });
            }
            foreach (var p in KitPages.All)
            {
                bool dim = false;
                if (!string.IsNullOrEmpty(p.Section))
                {
                    if (!KitSections.Available(p.Section)) continue;
                    dim = !target.SectionOn(p.Section) || KitGuard.IsPaused(p.Section);
                }
                list.Add(new PageEntry { Id = "page:" + p.Id, Label = p.Label, Dim = dim, DimReason = KitText.K("Side.Off", "off") });
            }
            list.Add(new PageEntry { Id = "compat", Label = KitText.K("Page.Compat", "Compatibility") });
            list.Add(new PageEntry { Id = "diagnosis", Label = KitText.K("Page.Diagnosis", "Diagnosis") });
            list.Add(new PageEntry { Id = "about", Label = KitText.K("Page.About", "About") });
            return list;
        }

        public static void Draw(Rect inRect)
        {
            KitGuard.Run(KitSections.Core, "UI.SettingsWindow", () => DrawInner(inRect));
        }

        /// <summary>Width of the page list: a fifth of the window, 200 to 260 px.</summary>
        public static float SideWidth(float windowW) => Mathf.Clamp(windowW * 0.2f, 200f, 260f);

        static void DrawInner(Rect inRect)
        {
            KitUiProbe.Hit("settings");
            if (!InGame) editTemplate = true;
            var target = Target;
            var pages = BuildPages(target);
            if (pages.All(p => p.Id != pageId)) pageId = pages[0].Id;

            var side = new Rect(inRect.x, inRect.y, SideWidth(inRect.width), inRect.height);
            var main = new Rect(side.xMax + 12f, inRect.y, inRect.width - side.width - 12f, inRect.height);
            Last.In = inRect; Last.Side = side; Last.Main = main; Last.Page = pageId; Last.Frame = Time.frameCount;
            DrawSidebar(side, pages);
            Widgets.DrawBoxSolid(new Rect(side.xMax + 5f, inRect.y, 1f, inRect.height), KitStyle.Line);
            DrawMain(main, pages, target);
        }

        // Sidebar -------------------------------------------------------------------------------------------------------
        static void DrawSidebar(Rect r, List<PageEntry> pages)
        {
            float y = r.y;
            var mark = KitIcons.Mark;
            string name = Kit.Info?.Name ?? "";
            float mx = r.x;
            if (mark != null) { KitIcons.Draw(new Rect(r.x, y, 32f, 32f), mark); mx = r.x + 38f; }
            KitStyle.LabelColored(new Rect(mx, y, r.xMax - mx, 32f), name.Truncate(r.xMax - mx), KitStyle.Accent, TextAnchor.MiddleLeft);
            if (KitUI.TextW(name) > r.xMax - mx) TooltipHandler.TipRegion(new Rect(r.x, y, r.width, 32f), name);
            y += 38f;

            if (InGame)
            {
                float hw = (r.width - 6f) / 2f;
                if (KitStyle.Chip(new Rect(r.x, y, hw, 32f), KitText.K("Side.ThisSave", "This save"), !editTemplate,
                    KitText.K("Side.ThisSaveTip", "Edit the settings of the running game. They are saved with it."))) editTemplate = false;
                if (KitStyle.Chip(new Rect(r.x + hw + 6f, y, hw, 32f), KitText.K("Side.Template", "Template"), editTemplate,
                    KitText.K("Side.TemplateTip", "Edit the template that new games start from. The running game is not changed."))) editTemplate = true;
                y += 40f;
            }
            else
            {
                string note = Kit.Info != null && Kit.Info.PerSave
                    ? KitText.K("Side.TemplateOnly", "Template for new games. Each new colony gets its own copy.")
                    : KitText.K("Side.Global", "These settings apply to every game.");
                y += KitStyle.Paragraph(r.x, y, r.width, note, KitStyle.Grey, GameFont.Tiny) + 6f;
            }

            float bottomH = InGame ? 76f : 0f;
            var listR = new Rect(r.x, y, r.width, r.height - (y - r.y) - bottomH);
            const float pageRowH = 34f;
            float contentH = pages.Count * (pageRowH + 2f);
            var view = new Rect(0, 0, listR.width - (contentH > listR.height ? 16f : 0f), contentH);
            bool anyIcon = pages.Any(p => p.Icon != null);
            Widgets.BeginScrollView(listR, ref scrollSide, view);
            float py = 0;
            foreach (var p in pages)
            {
                var row = new Rect(0, py, view.width, pageRowH);
                bool sel = p.Id == pageId;
                if (sel) { Widgets.DrawBoxSolid(row, KitStyle.AccentSoft); Widgets.DrawBoxSolid(new Rect(row.x, row.y, 3f, row.height), KitStyle.Accent); }
                else if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                float lx = row.x + 10f;
                if (anyIcon) { KitIcons.Draw(new Rect(row.x + 8f, row.y + 5f, 24f, 24f), p.Icon, p.Dim); lx = row.x + 38f; }
                string plabel = p.Label + (p.Dim ? "  (" + p.DimReason + ")" : "");
                var plr = new Rect(lx, row.y, row.xMax - lx - 2f, row.height);
                KitStyle.LabelColored(plr, plabel.Truncate(plr.width), p.Dim ? KitStyle.DimGrey : Color.white, TextAnchor.MiddleLeft);
                if (KitUI.TextW(plabel) > plr.width) TooltipHandler.TipRegion(row, plabel);
                if (Widgets.ButtonInvisible(row)) { pageId = p.Id; search = ""; scrollMain = Vector2.zero; mainViewH = 1000f; }
                py += pageRowH + 2f;
            }
            Widgets.EndScrollView();

            if (InGame)
            {
                float by = r.yMax - bottomH + 6f;
                var b1 = new Rect(r.x, by, r.width, 32f);
                if (Widgets.ButtonText(b1, KitText.K("Side.SaveAsTemplate", "Save as template")))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        KitText.K("Side.SaveAsTemplateConfirm", "Replace the template for new games with the settings of this save?"), () => { Kit.SaveAsTemplate(); AfterBulkChange(); }));
                TooltipHandler.TipRegion(b1, KitText.K("Side.SaveAsTemplateTip", "Copies the section switches and all settings of this save into the template for new games."));
                var b2 = new Rect(r.x, by + 36f, r.width, 32f);
                if (Widgets.ButtonText(b2, KitText.K("Side.ApplyTemplate", "Apply template")))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        KitText.K("Side.ApplyTemplateConfirm", "Replace the section switches and all settings of this save with the template? This cannot be undone."), () => { Kit.ApplyTemplateToSave(); AfterBulkChange(); }, true));
                TooltipHandler.TipRegion(b2, KitText.K("Side.ApplyTemplateTip", "Replaces the settings of this save with the template. Preferences that apply to all games stay."));
            }
        }

        static void AfterBulkChange()
        {
            KitRow.ClearBuffers();
            presetCacheTime = -1f;
        }

        // Main area -----------------------------------------------------------------------------------------------------
        static void DrawMain(Rect r, List<PageEntry> pages, KitProfile target)
        {
            var page = pages.First(p => p.Id == pageId);
            float tx = r.x;
            var picon = string.IsNullOrEmpty(search) ? page.Icon : null;
            if (picon != null) { KitIcons.Draw(new Rect(r.x, r.y + 3f, 28f, 28f), picon); tx += 36f; }
            bool searchable = pageId != "diagnosis" && pageId != "about";
            float rightW = 0f;
            Rect sr = Rect.zero, nr = Rect.zero;
            bool nerdToggle = searchable && AnyNerd;
            string nerdLabel = KitText.K("Header.Nerd", "Nerd settings");
            if (searchable)
            {
                float sw = Mathf.Clamp(r.width * 0.26f, 180f, 300f);
                sr = new Rect(r.xMax - sw, r.y + 2f, sw, 30f);
                float clearW = search.NullOrEmpty() ? 0f : 34f;
                rightW = r.xMax - sr.x + clearW + 12f;
                if (nerdToggle)
                {
                    float nw = Mathf.Clamp(KitUI.TextW(nerdLabel) + 40f, 120f, 240f);
                    nr = new Rect(sr.x - clearW - nw - 12f, r.y + 4f, nw, 26f);
                    rightW = r.xMax - nr.x + 12f;
                }
            }
            string headTitle = string.IsNullOrEmpty(search) ? page.Label : KitText.K("Header.SearchResults", "Search results");
            Text.Font = GameFont.Medium;
            float titleW = Mathf.Max(120f, r.width - rightW - (tx - r.x));
            Last.Title = new Rect(tx, r.y, Mathf.Min(titleW, KitUI.TextW(headTitle, GameFont.Medium)), 34f); Last.Search = sr; Last.Nerd = nr;
            Widgets.Label(new Rect(tx, r.y, titleW, 34f), headTitle.Truncate(titleW));
            Text.Font = GameFont.Small;
            Widgets.DrawBoxSolid(new Rect(r.x, r.y + 35f, Mathf.Min(r.width, 180f), 2f), KitStyle.Accent);
            if (searchable)
            {
                search = Widgets.TextField(sr, search) ?? "";
                if (search.NullOrEmpty()) KitStyle.LabelColored(new Rect(sr.x + 6f, sr.y, sr.width - 8f, sr.height), KitText.K("Header.Search", "Search all settings..."), KitStyle.DimGrey, TextAnchor.MiddleLeft);
                else if (Widgets.ButtonText(new Rect(sr.x - 32f, sr.y, 28f, 30f), "x")) search = "";
                if (nerdToggle)
                {
                    bool nerd = ShowNerd, nb = nerd;
                    Widgets.CheckboxLabeled(nr, nerdLabel, ref nb, false, null, null, true);
                    TooltipHandler.TipRegion(nr, KitText.K("Header.NerdTip", "Shows the technical settings that most players never need. Everything else is unchanged."));
                    if (nb != nerd) { var ns = KitSettings.Find(KitBuiltins.ShowNerdKey); if (ns != null) KitSettings.Set(ns, Kit.Template, nb ? 1f : 0f); }
                }
            }
            float top = r.y + 44f;
            if (InGame && editTemplate)
            {
                KitStyle.LabelColored(new Rect(r.x, top, r.width, 22f), KitText.K("Header.EditingTemplate", "Editing the template for new games. The running game is not changed."), KitStyle.Accent, TextAnchor.UpperLeft, GameFont.Tiny);
                top += 26f;
            }
            var outR = new Rect(r.x, top, r.width, r.yMax - top);
            Last.Content = outR; Last.ContentH = 0f;
            KitUiProbe.Hit("page:" + pageId + (string.IsNullOrEmpty(search) ? "" : "+search"));

            // custom pages draw into the whole area themselves (their own scrolling)
            if (string.IsNullOrEmpty(search) && pageId.StartsWith("page:"))
            {
                var cp = KitPages.Find(pageId.Substring(5));
                if (cp?.Draw != null)
                {
                    float y0 = outR.y;
                    if (page.Dim)
                    {
                        string off = KitText.K("Page.CustomOff", "The section \"{0}\" is switched off. This page has no effect until it is on.", KitSections.LabelOf(cp.Section));
                        if (KitStyle.Banner(outR.x, y0, outR.width, off, KitStyle.Accent, KitText.K("Section.SwitchOn", "Switch on"), out float bh) && KitSections.Available(cp.Section))
                            target.SetSection(cp.Section, true);
                        y0 += bh + 8f;
                    }
                    var area = new Rect(outR.x, y0, outR.width, outR.yMax - y0);
                    KitGuard.Run(string.IsNullOrEmpty(cp.Section) ? KitSections.Core : cp.Section, "UI.Page." + cp.Id, () => cp.Draw(area));
                }
                return;
            }

            var view = new Rect(0, 0, outR.width - 18f, mainViewH);
            var visible = new Rect(0, scrollMain.y, view.width, outR.height);
            Widgets.BeginScrollView(outR, ref scrollMain, view);
            float h = 40f;
            try
            {
                if (searchable && !string.IsNullOrEmpty(search)) h = DrawSearch(view.width, target, visible);
                else if (pageId == "overview") h = DrawOverview(view.width, target);
                else if (pageId == "general") h = DrawGeneral(view.width, target, visible);
                else if (pageId == "compat") h = DrawCompat(view.width);
                else if (pageId == "diagnosis") h = KitDiagnosis.Draw(view.width);
                else if (pageId == "about") h = DrawAbout(view.width);
                else if (pageId.StartsWith("section:")) h = DrawSectionPage(pageId.Substring(8), view.width, target, visible);
            }
            catch (Exception e) { KitLog.Once("settingsPage:" + pageId + e.GetType().Name, KitLogLevel.Error, "UI", "Settings page " + pageId + " failed: " + e); }
            mainViewH = Mathf.Max(h + 20f, 40f);
            Last.ContentH = h;
            Widgets.EndScrollView();
        }

        static bool Visible(KitSetting s) => (ShowNerd || !s.Nerd) && KitSections.Available(s.Section);

        // Overview ------------------------------------------------------------------------------------------------------
        static float DrawOverview(float w, KitProfile target)
        {
            float y = 0f;
            string intro = Kit.Info == null || !Kit.Info.PerSave ? KitText.K("Overview.IntroGlobal", "These settings apply to every game. Switch whole sections on or off here; the pages on the left hold the details.")
                : ReferenceEquals(target, Kit.Template) ? KitText.K("Overview.IntroTemplate", "The template for new games. Each new colony gets its own copy, and you can change it there later.")
                : KitText.K("Overview.IntroSave", "The settings of this save. The template for new games stays as it is.");
            y += KitStyle.Paragraph(0f, y, w, intro, KitStyle.Grey) + 10f;

            foreach (var id in KitGuard.Paused.ToList())
            {
                string text = KitText.K("Overview.Paused", "{0} paused itself after repeated errors.", KitSections.LabelOf(id));
                if (KitStyle.Banner(0f, y, w, text, KitStyle.Bad, KitText.K("Diag.Resume", "Resume"), out float bh)) KitGuard.Resume(id);
                y += bh + 6f;
            }

            if (KitPresets.All.Count > 0) y = DrawPresets(w, target, y) + 8f;

            if (KitSections.All.Count > 0)
            {
                y += KitStyle.Section(0f, y, w, KitText.K("Overview.Sections", "Sections"));
                foreach (var s in KitSections.All) y += DrawSectionRow(0f, y, w, s, target) + 2f;
            }
            else if (KitPresets.All.Count == 0)
            {
                y += KitStyle.Paragraph(0f, y, w, KitText.K("Overview.NoSections", "This mod has no switchable sections. All its settings are on the General page."), KitStyle.DimGrey) + 6f;
            }
            return y + 6f;
        }

        // presets are compared by applying them to a scratch copy: cached for a short while, the window draws every frame
        static float presetCacheTime = -1f;
        static KitProfile presetCacheTarget;
        static KitPreset presetCacheMatch;

        static KitPreset MatchingPreset(KitProfile target)
        {
            float now = Time.realtimeSinceStartup;
            if (!ReferenceEquals(target, presetCacheTarget) || presetCacheTime < 0f || now - presetCacheTime > 0.4f || Kit.HasPendingChanges)
            {
                presetCacheTarget = target; presetCacheTime = now;
                presetCacheMatch = KitPresets.Matching(target);
            }
            return presetCacheMatch;
        }

        static float DrawPresets(float w, KitProfile target, float y)
        {
            y += KitStyle.Section(0f, y, w, KitText.K("Overview.Presets", "Presets"));
            var match = MatchingPreset(target);
            var chips = KitPresets.All.Select(p => (preset: p, label: p.Label)).ToList();
            string custom = KitText.K("Overview.Custom", "Custom");
            float x = 0f, chipH = 32f;
            void Next(float cw) { if (x > 0f && x + cw > w) { x = 0f; y += chipH + 6f; } }
            foreach (var (p, label) in chips)
            {
                float cw = Mathf.Clamp(KitUI.TextW(label) + 40f, 110f, 240f);
                Next(cw);
                string tip = p.Desc;
                if (KitStyle.Chip(new Rect(x, y, cw, chipH), label, ReferenceEquals(p, match), tip)) ChoosePreset(p, target);
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

        static void ChoosePreset(KitPreset p, KitProfile target)
        {
            var over = KitPresets.WouldOverwrite(p, target);
            void Apply() { KitPresets.Apply(p, target); AfterBulkChange(); }
            if (over.Count == 0) { Apply(); return; }
            var names = over.Select(k => k.StartsWith("section:") ? KitSections.LabelOf(k.Substring(8)) : (KitSettings.Find(k)?.Label ?? k)).ToList();
            string list = string.Join(", ", names.Take(8)) + (names.Count > 8 ? ", ..." : "");
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                KitText.K("Overview.PresetConfirm", "Apply the preset \"{0}\"? It overwrites {1} values you changed: {2}", p.Label, names.Count, list), Apply, true));
        }

        /// <summary>One section switch row: checkbox, icon, label, description or why it is not available, paused state, page link.</summary>
        static float DrawSectionRow(float x, float y, float w, KitSection s, KitProfile target)
        {
            bool avail = KitSections.Available(s.Id);
            bool paused = KitGuard.IsPaused(s.Id);
            float lw = Mathf.Clamp(w * 0.24f, 160f, 300f);
            bool hasPage = avail && SectionHasRows(s.Id);
            float rightW = (paused ? 96f : 0f) + (hasPage ? 34f : 0f);
            string descText = avail ? s.Desc
                : KitText.K("Overview.NotInstalled", "{0} (not installed)", s.RequiresLabel ?? KitText.K("Section.NotAvailable", "Not available in this game."));
            float descW = w - lw - 74f - rightW;
            float h = Mathf.Max(44f, KitStyle.CachedHeight(descText, descW, GameFont.Tiny) + 12f);
            var row = new Rect(x, y, w, h);
            if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
            bool on = target.SectionOn(s.Id), nb = on;
            Widgets.Checkbox(new Vector2(row.x + 4f, row.y + (h - 24f) / 2f), ref nb, 24f, !avail);
            if (avail && nb != on) { target.SetSection(s.Id, nb); presetCacheTime = -1f; }
            KitIcons.Draw(new Rect(row.x + 34f, row.y + (h - 24f) / 2f, 24f, 24f), KitIcons.Section(s.Id), !avail || !nb);
            KitStyle.LabelColored(new Rect(row.x + 66f, row.y, lw, h), s.Label, avail ? (nb ? Color.white : KitStyle.Grey) : KitStyle.DimGrey, TextAnchor.MiddleLeft);
            KitStyle.LabelColored(new Rect(row.x + 70f + lw, row.y, descW, h), descText, avail ? KitStyle.Grey : KitStyle.DimGrey, TextAnchor.MiddleLeft, GameFont.Tiny);
            float rx = row.xMax;
            if (hasPage)
            {
                rx -= 30f;
                var pr = new Rect(rx, row.y + (h - 26f) / 2f, 28f, 26f);
                if (Widgets.ButtonText(pr, "»")) { pageId = "section:" + s.Id; search = ""; scrollMain = Vector2.zero; }
                TooltipHandler.TipRegion(pr, KitText.K("Overview.OpenPage", "Open the settings of this section"));
                rx -= 4f;
            }
            if (paused)
            {
                rx -= 92f;
                if (Widgets.ButtonText(new Rect(rx, row.y + (h - 26f) / 2f, 90f, 26f), KitText.K("Diag.Resume", "Resume"))) KitGuard.Resume(s.Id);
                KitStyle.LabelColored(new Rect(row.x + 66f, row.yMax - 14f, lw, 14f), KitText.K("Diag.Paused", "paused after errors"), KitStyle.Bad, TextAnchor.UpperLeft, GameFont.Tiny);
            }
            TooltipHandler.TipRegion(row, s.Desc + (avail ? "" : "\n\n" + descText));
            return h;
        }

        // Section page --------------------------------------------------------------------------------------------------
        static float DrawSectionPage(string id, float w, KitProfile target, Rect visible)
        {
            var s = KitSections.Find(id);
            if (s == null) return 40f;
            float y = 0f;
            string rl = KitText.K("Section.Reset", "Reset section");
            float rbw = Mathf.Clamp(KitUI.TextW(rl) + 32f, 140f, 240f);
            float dh = Mathf.Max(36f, KitStyle.CachedHeight(s.Desc, w - rbw - 16f, GameFont.Small));
            KitStyle.LabelColored(new Rect(0f, y, w - rbw - 12f, dh), s.Desc, KitStyle.Grey);
            if (Widgets.ButtonText(new Rect(w - rbw, y, rbw, 30f), rl))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    KitText.K("Section.ResetConfirm", "Reset every setting of {0} to its default?", s.Label), () => { KitSettings.ResetSection(id, target); AfterBulkChange(); KitLog.Info("UI", "Settings of section " + id + " reset"); }, true));
            y += dh + 6f;

            if (KitGuard.IsPaused(id))
            {
                if (KitStyle.Banner(0f, y, w, KitGuard.ReasonFor(id) ?? KitText.K("Diag.Paused", "paused after errors"), KitStyle.Bad, KitText.K("Diag.Resume", "Resume"), out float bh)) KitGuard.Resume(id);
                y += bh + 8f;
            }
            else if (!target.SectionOn(id))
            {
                string off = ReferenceEquals(target, Kit.Template) && Kit.Info != null && Kit.Info.PerSave
                    ? KitText.K("Section.IsOffTemplate", "This section is off in the template. New games start without it.")
                    : KitText.K("Section.IsOff", "This section is switched off. Its settings have no effect.");
                if (KitStyle.Banner(0f, y, w, off, KitStyle.Accent, KitText.K("Section.SwitchOn", "Switch on"), out float bh)) { target.SetSection(id, true); presetCacheTime = -1f; }
                y += bh + 8f;
            }

            // only against the running game's own profile (PrepareRemoval switches the section off in Kit.Current)
            if (s.PrepareRemoval != null && Current.ProgramState == ProgramState.Playing && ReferenceEquals(target, Kit.Current))
            {
                string pr = KitText.K("Section.PrepareRemoval", "Prepare for removal");
                float pw = Mathf.Clamp(KitUI.TextW(pr) + 32f, 180f, 300f);
                var b = new Rect(0f, y, pw, 30f);
                if (Widgets.ButtonText(b, pr))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        KitText.K("Section.PrepareRemovalConfirm", "Prepare this game for living without {0}? The section is switched off and what it placed in the save is cleaned up. Save a backup first.", s.Label),
                        () => KitDiagnosis.RunPrepareRemoval(id), true));
                TooltipHandler.TipRegion(b, KitText.K("Section.PrepareRemovalTip", "Switches the section off and removes what it put into this save, so the save also loads without it."));
                y += 38f;
            }

            float before = y;
            y = DrawRows(KitSettings.OfSection(id), w, target, visible, y);
            if (y == before)
                y += KitStyle.Paragraph(0f, y, w, KitText.K("Section.OnlyNerd", "Every setting of this section is a nerd setting. Switch on \"Nerd settings\" at the top to see them."), KitStyle.DimGrey) + 6f;
            return y;
        }

        /// <summary>Rows grouped by category (a header per category), nerd filter applied.</summary>
        static float DrawRows(IEnumerable<KitSetting> settings, float w, KitProfile target, Rect visible, float y)
        {
            string last = null;
            foreach (var s in settings)
            {
                if (!ShowNerd && s.Nerd) continue;
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
        static float DrawGeneral(float w, KitProfile target, Rect visible)
        {
            float y = 0f;
            y += KitStyle.Paragraph(0f, y, w, KitText.K("General.Intro", "Settings of the whole mod and of this window."), KitStyle.Grey) + 4f;
            var own = KitSettings.OfSection(KitSections.Core).Where(s => s.Category != KitBuiltins.Category).ToList();
            var iface = KitSettings.OfSection(KitSections.Core).Where(s => s.Category == KitBuiltins.Category).ToList();
            y = DrawRows(own.OrderBy(s => s.Category ?? "").ThenBy(s => s.Order).ToList(), w, target, visible, y);
            y = DrawRows(iface, w, target, visible, y);
            y += 4f;
            y += KitStyle.Paragraph(0f, y, w, KitText.K("General.ScaleNote", "A new window size applies the next time this window opens."), KitStyle.DimGrey, GameFont.Tiny) + 6f;
            return y;
        }

        // Search --------------------------------------------------------------------------------------------------------
        static float DrawSearch(float w, KitProfile target, Rect visible)
        {
            float y = 0f; int n = 0;
            string q = search.ToLowerInvariant();
            var secs = KitSections.All.Where(s => (s.Label ?? "").ToLowerInvariant().Contains(q) || (s.Desc ?? "").ToLowerInvariant().Contains(q) || s.Id.Contains(q)).ToList();
            if (secs.Count > 0)
            {
                y += KitStyle.Section(0f, y, w, KitText.K("Overview.Sections", "Sections"));
                foreach (var s in secs) { y += DrawSectionRow(0f, y, w, s, target) + 2f; n++; }
                y += 8f;
            }
            var groups = KitSettings.All.Where(s => Visible(s) && KitRow.MatchesSearch(s, search)).GroupBy(s => s.Section)
                .OrderBy(g => g.Key == KitSections.Core ? -1 : KitSections.All.FindIndex(x => x.Id == g.Key));
            foreach (var g in groups)
            {
                string title = g.Key == KitSections.Core ? KitText.K("Page.General", "General") : KitSections.LabelOf(g.Key);
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
        static float DrawCompat(float w)
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

            var integrations = KitCompat.All.Where(c => !c.Incompatible).ToList();
            var conflicts = KitCompat.All.Where(c => c.Incompatible).ToList();
            y += KitStyle.Section(0f, y, w, KitText.K("Compat.Integrations", "Integrations"));
            if (integrations.Count == 0) { KitStyle.LabelColored(new Rect(4f, y, w, 24f), KitText.K("Compat.None", "No integrations with other mods."), KitStyle.DimGrey); y += 26f; }
            foreach (var c in integrations) y += CompatRow(c, w, y);
            if (conflicts.Count > 0)
            {
                y += 8f;
                y += KitStyle.Section(0f, y, w, KitText.K("Compat.Conflicts", "Known conflicts"));
                foreach (var c in conflicts) y += CompatRow(c, w, y);
            }
            return y + 6f;
        }

        static Color StateColor(KitCompatState st)
        {
            switch (st)
            {
                case KitCompatState.Active: case KitCompatState.IncompatibleAbsent: return KitStyle.Good;
                case KitCompatState.Failed: case KitCompatState.IncompatiblePresent: return KitStyle.Bad;
                case KitCompatState.Unhealthy: return KitStyle.Accent;
                default: return KitStyle.DimGrey;
            }
        }

        static float CompatRow(KitCompatEntry c, float w, float y)
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
            KitStyle.LabelColored(new Rect(4f, y + 2f, nameW - 8f, 24f), c.Name, present ? Color.white : KitStyle.Grey, TextAnchor.UpperLeft);
            KitStyle.LabelColored(new Rect(nameW, y + 2f, w - nameW - 10f, stH), stateText, StateColor(st));
            if (whatH > 0f) KitStyle.LabelColored(new Rect(nameW, y + 2f + stH, w - nameW - 10f, whatH), what, KitStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
            TooltipHandler.TipRegion(row, c.PackageId + (c.PatchesApplied > 0 ? "\n" + KitText.K("Compat.Patches", "{0} patch classes applied", c.PatchesApplied) : ""));
            return h + 2f;
        }

        // About ---------------------------------------------------------------------------------------------------------
        static float DrawAbout(float w)
        {
            var info = Kit.Info;
            float y = 0f, ax = 0f;
            var mark = KitIcons.Mark;
            if (mark != null) { KitIcons.Draw(new Rect(0f, y, 64f, 64f), mark); ax = 76f; }
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(ax, y + (ax > 0 ? 6f : 0f), w - ax, 34f), info?.Name ?? ""); Text.Font = GameFont.Small;
            y += ax > 0 ? 40f : 36f;
            string rw; try { rw = VersionControl.CurrentVersionString; } catch { rw = "?"; }
            KitStyle.LabelColored(new Rect(ax, y, w - ax, 24f), (info?.Version ?? "?") + "  ·  RimWorld " + rw, KitStyle.Grey);
            y += ax > 0 ? 34f : 30f;
            float bw = Mathf.Min(w, 700f);
            if (!string.IsNullOrEmpty(info?.FirstStartPitch)) y += KitStyle.Paragraph(0f, y, bw, info.FirstStartPitch, Color.white) + 14f;

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
            KitStyle.LabelColored(new Rect(0f, y, w, 22f), KitText.K("About.Kit", "Built with JB.Kit {0}, the settings foundation from Hearth & Harbor.", KitInfo.KitVersion), KitStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            return y + 30f;
        }
    }
}

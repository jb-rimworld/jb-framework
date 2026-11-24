// SPDX-License-Identifier: MIT
// Generalised from Hearth & Harbor Hearth/UI/FirstStartCard.cs (hosting presets, tier, tutorial and underworld removed).
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// The welcome card of one mod: once per new colony, and once when the mod joins an existing save. Shows the mod mark, the pitch
    /// (KitInfo.FirstStartPitch), the presets and the section switches; choices apply live to the save's profile, so closing the card in
    /// any way keeps them. Needs PerSave (the "done" flag lives in the save's profile). The framework's global "Show welcome cards"
    /// preference switches all cards off ("Don't show welcome cards again").
    /// </summary>
    internal sealed class Dialog_KitFirstStartCard : Window
    {
        public static int OpenedCount;
        public readonly bool JoinedExisting;
        readonly KitMod k;
        readonly KitProfile profile;
        KitPreset preset;
        bool useForAll, neverAgain, applied, openSettings;
        Vector2 scroll; float bodyH = 400f;

        public override Vector2 InitialSize => KitUI.Size(860f, 620f, 680f, 480f, 0.94f, 0.92f);
        public bool UseForAll { get => useForAll; set => useForAll = value; }
        public bool NeverAgain { get => neverAgain; set => neverAgain = value; }
        public KitPreset Preset => preset;

        public Dialog_KitFirstStartCard(KitMod k, KitProfile profile, bool joinedExisting)
        {
            this.k = k;
            this.profile = profile;
            JoinedExisting = joinedExisting;
            forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false; doCloseX = false; doCloseButton = false;
            closeOnAccept = false; closeOnCancel = false; draggable = false; preventSave = false;
            try { preset = k.Presets.Matching(profile); } catch { preset = null; }
        }

        public override void PostOpen() { base.PostOpen(); OpenedCount++; }

        /// <summary>Applies a preset to the save's profile (changes only what it names).</summary>
        public void ApplyPreset(KitPreset p)
        {
            if (p == null || profile == null) return;
            if (k.Presets.Apply(p, profile)) preset = p;
        }

        /// <summary>Done flag, template copy, "never again", optional jump to the settings. Runs once, on close.</summary>
        public void Finish()
        {
            if (applied) return; applied = true;
            if (profile == null) return;
            profile.FirstStartCardDone = true;
            if (useForAll && ReferenceEquals(profile, k.SaveProfile)) k.SaveAsTemplate();
            if (neverAgain)
            {
                var self = Framework.Self;
                var s = self?.SettingsReg.Find(Framework.WelcomeCardsKey);
                if (s != null) { self.SettingsReg.Set(s, self.Template, 0f); self.WriteSettings(); }
            }
            k.Log.Info("UI", $"First-start card done: preset {preset?.Id ?? "custom"}, forAll {useForAll}, neverAgain {neverAgain}, joined {JoinedExisting}");
        }

        public override void PreClose() { base.PreClose(); Finish(); }

        public override void PostClose()
        {
            base.PostClose();
            if (openSettings) k.OpenSettings("overview");
        }

        public override void DoWindowContents(Rect inRect)
        {
            var prev = KitMod.Drawing;
            KitMod.Drawing = k;
            try { k.Guard.Run(KitMod.Core, "UI.FirstStartCard", () => Draw(inRect)); }
            finally { KitMod.Drawing = prev; }
        }

        void Draw(Rect inRect)
        {
            KitUiProbe.Hit("card");
            if (profile == null) { Close(); return; }
            string name = k.Name ?? "";
            float y = inRect.y;

            // header band: mark, title, pitch
            var mark = KitIcons.Mark(k);
            float tx = 20f + (mark != null ? 76f : 0f);
            string title = JoinedExisting ? KitText.K("Card.TitleJoined", "{0} joined this colony", name) : KitText.K("Card.Title", "Welcome to {0}", name);
            string sub = k.Info?.FirstStartPitch ?? "";
            float tw = inRect.width - tx - 20f;
            float th = KitUI.TextH(title, tw, GameFont.Medium), sh = KitUI.TextH(sub, tw);
            float headH = Mathf.Max(mark != null ? 96f : 80f, 16f + th + 6f + sh + 16f);
            var hr = new Rect(inRect.x, y, inRect.width, headH);
            Widgets.DrawBoxSolid(hr, new Color(0.13f, 0.12f, 0.11f));
            Widgets.DrawBoxSolid(new Rect(hr.x, hr.yMax - 3f, hr.width, 3f), KitStyle.Accent);
            if (mark != null) KitIcons.Draw(new Rect(hr.x + 16f, hr.y + (headH - 64f) / 2f, 64f, 64f), mark);
            Text.Font = GameFont.Medium; GUI.color = new Color(1f, 0.96f, 0.9f);
            Widgets.Label(new Rect(hr.x + tx, hr.y + 16f, tw, th), title);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(hr.x + tx, hr.y + 16f + th + 6f, tw, sh), sub);
            GUI.color = Color.white;
            y = hr.yMax + 12f;

            // sticky footer: use-for-all, never again, open settings, start
            float footH = 78f;
            var br = new Rect(inRect.x, inRect.yMax - footH, inRect.width, footH);
            Widgets.DrawBoxSolid(new Rect(br.x, br.y, br.width, 1f), KitStyle.Line);
            string start = KitText.K("Card.Start", "Start");
            string open = KitText.K("Card.OpenSettings", "Open settings");
            float startW = Mathf.Clamp(KitUI.TextW(start) + 60f, 160f, 260f), openW = Mathf.Clamp(KitUI.TextW(open) + 40f, 160f, 260f);
            float checksW = br.width - startW - openW - 30f;
            var useR = new Rect(br.x, br.y + 8f, checksW, 28f);
            Widgets.CheckboxLabeled(useR, KitText.K("Card.UseForAll", "Use these choices for all new colonies"), ref useForAll, false, null, null, true);
            TooltipHandler.TipRegion(useR, KitText.K("Card.UseForAllTip", "Also saves them as the template for new games (Mod settings, Template)."));
            var neverR = new Rect(br.x, br.y + 40f, checksW, 28f);
            Widgets.CheckboxLabeled(neverR, KitText.K("Card.NeverAgain", "Don't show welcome cards again"), ref neverAgain, false, null, null, true);
            TooltipHandler.TipRegion(neverR, KitText.K("Card.NeverAgainTip", "For every JB mod: new colonies then start from the templates without asking. You can switch the cards back on in Mod settings, {0}.", Framework.Name));
            if (Widgets.ButtonText(new Rect(br.xMax - startW - openW - 10f, br.y + 20f, openW, 36f), open)) { openSettings = true; Close(); }
            var sr = new Rect(br.xMax - startW, br.y + 20f, startW, 36f);
            Widgets.DrawBoxSolid(sr, KitStyle.AccentSoft);
            if (Widgets.ButtonText(sr, start)) Close();

            // scrolling body between header and footer
            var outer = new Rect(inRect.x, y, inRect.width, br.y - y - 6f);
            var view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(bodyH, outer.height - 2f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            float used = DrawBody(view.width);
            Widgets.EndScrollView();
            bodyH = used + 6f;
        }

        float DrawBody(float w)
        {
            float y = 0f;
            var ps = k.Presets.All;
            if (ps.Count > 0)
            {
                y += KitStyle.Section(0f, y, w, KitText.K("Card.Presets", "Pick a start"));
                int perRow = ps.Count >= 4 && w >= 720f ? 4 : Math.Min(3, Math.Max(1, ps.Count));
                if (w < 520f) perRow = Math.Min(perRow, 2);
                float gap = 12f, pw = (w - gap * (perRow - 1)) / perRow;
                float cardH = 72f;
                foreach (var p in ps)
                    cardH = Mathf.Max(cardH, 12f + KitUI.TextH(p.Label, pw - 24f) + 4f + KitUI.TextH(p.Desc, pw - 24f, GameFont.Tiny) + 12f);
                for (int i = 0; i < ps.Count; i++)
                {
                    var p = ps[i];
                    var r = new Rect((i % perRow) * (pw + gap), y + (i / perRow) * (cardH + gap), pw, cardH);
                    bool sel = ReferenceEquals(preset, p);
                    Widgets.DrawBoxSolid(r, sel ? KitStyle.AccentSoft : new Color(1f, 1f, 1f, 0.04f));
                    GUI.color = sel ? KitStyle.Accent : KitStyle.Line; Widgets.DrawBox(r, sel ? 2 : 1); GUI.color = Color.white;
                    if (Mouse.IsOver(r) && !sel) Widgets.DrawHighlight(r);
                    float plh = KitUI.TextH(p.Label, r.width - 24f);
                    KitStyle.LabelColored(new Rect(r.x + 12f, r.y + 10f, r.width - 24f, plh), p.Label, sel ? KitStyle.Accent : Color.white);
                    KitStyle.LabelColored(new Rect(r.x + 12f, r.y + 14f + plh, r.width - 24f, r.height - plh - 20f), p.Desc, KitStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
                    if (Widgets.ButtonInvisible(r)) ApplyPreset(p);
                }
                y += ((ps.Count + perRow - 1) / perRow) * (cardH + gap) + 4f;
            }

            var secs = k.Sections.All.Where(s => k.Sections.Available(s.Id)).ToList();
            if (secs.Count > 0)
            {
                y += KitStyle.Section(0f, y, w, KitText.K("Card.Sections", "What is switched on"));
                int cols = w >= 640f ? 3 : 2;
                float cw = w / cols;
                for (int i = 0; i < secs.Count; i++)
                {
                    var s = secs[i];
                    var r = new Rect((i % cols) * cw, y + (i / cols) * 32f, cw - 10f, 30f);
                    bool on = profile.SectionOn(s.Id), nb = on;
                    Widgets.CheckboxLabeled(r, s.Label, ref nb, false, null, null, true);
                    if (!string.IsNullOrEmpty(s.Desc)) TooltipHandler.TipRegion(r, s.Desc);
                    if (nb != on) { profile.SetSection(s.Id, nb); preset = k.Presets.Matching(profile); }
                }
                y += ((secs.Count + cols - 1) / cols) * 32f + 6f;
            }
            string hint = KitText.K("Card.Hint", "Everything here can be changed later in Mod settings. Closing this card keeps your choices.");
            y += KitStyle.Paragraph(0f, y, w, hint, KitStyle.DimGrey, GameFont.Tiny) + 6f;
            return y;
        }
    }

    /// <summary>Decides when a card opens: from the framework's game component, once the map is shown and nothing else is loading. One card at a time.</summary>
    internal static class KitFirstStartCard
    {
        /// <summary>Tests (or a mod) set this to keep every card from opening.</summary>
        public static bool Suppressed;
        static readonly HashSet<KitMod> shown = new HashSet<KitMod>();
        static float readySince = -1f;
        /// <summary>Seconds the map must be on screen before a card opens.</summary>
        public const float Delay = 1.5f;

        internal static void ResetForGame() { shown.Clear(); readySince = -1f; }

        static bool Wants(KitMod k) => !k.Blocked && k.Info.PerSave && !string.IsNullOrEmpty(k.Info.FirstStartPitch);

        /// <summary>The first mod whose card would open now if the delay had passed, or null.</summary>
        public static KitMod Due()
        {
            if (Suppressed) return null;
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || Find.WindowStack == null) return null;
            if (LongEventHandler.AnyEventNowOrWaiting) return null;
            if (Find.WindowStack.WindowOfType<Page>() != null || Find.WindowStack.WindowOfType<Dialog_KitFirstStartCard>() != null) return null;
            foreach (var k in Framework.Mods)
            {
                if (!Wants(k) || shown.Contains(k)) continue;
                var p = k.SaveProfile;
                if (p == null || p.FirstStartCardDone) continue;
                return k;
            }
            return null;
        }

        internal static void OnGUI()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            var k = Due();
            if (k == null) { readySince = -1f; return; }
            float now = Time.realtimeSinceStartup;
            if (readySince < 0f) { readySince = now; return; }
            if (now - readySince < Delay) return;
            readySince = -1f;
            shown.Add(k);
            var profile = k.SaveProfile;
            if (!Framework.WelcomeCards)
            {
                // cards off: the save keeps the template copy it already got, quietly
                profile.FirstStartCardDone = true;
                k.Log.Info("UI", "Welcome cards switched off: template used silently");
                return;
            }
            Find.WindowStack.Add(new Dialog_KitFirstStartCard(k, profile, k.Window.JoinedExistingSave));
        }

        /// <summary>Tests: open a mod's card now for the running game (ignores the delay and the "done" flag).</summary>
        public static Dialog_KitFirstStartCard OpenNow(KitMod k)
        {
            var p = k?.SaveProfile;
            if (p == null) return null;
            var d = new Dialog_KitFirstStartCard(k, p, k.Window.JoinedExistingSave);
            shown.Add(k);
            Find.WindowStack.Add(d);
            return d;
        }
    }
}

// SPDX-License-Identifier: MIT
using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// The fence around code a mod draws itself (custom pages). An exception never pauses a gameplay section and never reaches another
    /// mod: the page shows an error box with a Retry button instead (one frame of the broken drawing at most), the GUI state (font,
    /// anchor, colour, word wrap) is reset and the GUI groups and scroll views the page left open are closed again.
    /// </summary>
    internal static class KitUiFence
    {
        static MethodInfo clipCount;
        static bool clipLooked;

        /// <summary>Depth of Unity's GUI clip stack (BeginGroup/BeginScrollView), or -1 when the engine does not expose it.</summary>
        static int ClipDepth()
        {
            if (!clipLooked)
            {
                clipLooked = true;
                try { clipCount = typeof(GUI).Assembly.GetType("UnityEngine.GUIClip")?.GetMethod("Internal_GetCount", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public); }
                catch { clipCount = null; }
            }
            if (clipCount == null) return -1;
            try { return (int)clipCount.Invoke(null, null); } catch { return -1; }
        }

        internal static void DrawPage(KitMod k, KitPage page, Rect area)
        {
            if (k.Window.PageErrors.TryGetValue(page.Id, out string err)) { ErrorBox(k, page, area, err); return; }
            int clips = ClipDepth();
            int scrolls = Widgets.mouseOverScrollViewStack?.Count ?? 0;
            var font = Text.Font; var anchor = Text.Anchor; var wrap = Text.WordWrap; var color = GUI.color;
            KitUiProbe.Hit("custompage:" + page.Id);
            try { page.Draw(area); }
            catch (Exception e)
            {
                if (e is ExitGUIException) throw;
                // close what the page left open: scroll views first (they also hold a clip), then plain groups
                try
                {
                    while ((Widgets.mouseOverScrollViewStack?.Count ?? 0) > scrolls) Widgets.EndScrollView();
                    if (clips >= 0) { int guard = 0; while (ClipDepth() > clips && guard++ < 64) GUI.EndGroup(); }
                }
                catch { }
                Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color;
                k.Window.PageErrors[page.Id] = e.GetType().Name + ": " + e.Message;
                k.Guard.TotalFailures++;
                k.Log.Once("uiPage:" + page.Id + ":" + e.GetType().Name, KitLogLevel.Error, "UI", "Custom page '" + page.Id + "' failed while drawing (shown as an error box; no section was paused): " + e);
            }
        }

        static void ErrorBox(KitMod k, KitPage page, Rect area, string err)
        {
            string text = KitText.K("Page.Failed", "This page ran into an error and is not drawn: {0}\n\nDetails are in {1}. The rest of {2} keeps working.", err, k.Log.FileName, k.Name);
            if (KitStyle.Banner(area.x, area.y, area.width, text, KitStyle.Bad, KitText.K("Page.Retry", "Retry"), out _))
                k.Window.PageErrors.Remove(page.Id);
        }
    }
}

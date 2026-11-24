// SPDX-License-Identifier: MIT
using UnityEngine;

namespace JBFramework
{
    /// <summary>One mod's settings window state (page, search, scroll positions, preset cache): every mod keeps its own.</summary>
    internal sealed class KitWindowState
    {
        public string PageId = "overview";
        public bool EditTemplate;
        public string Search = "";
        public Vector2 ScrollSide, ScrollMain;
        public float MainViewH = 1000f;
        public float PresetCacheTime = -1f;
        public KitProfile PresetCacheTarget;
        public KitPreset PresetCacheMatch;
        /// <summary>The mod was added to the running save (welcome card variant).</summary>
        public bool JoinedExistingSave;
        /// <summary>Custom pages that threw while drawing (page id → message); shown as an error box until Retry or the window closes.</summary>
        public readonly System.Collections.Generic.Dictionary<string, string> PageErrors = new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>Where the last frame put the window's parts (read by the window test to check that they fit and do not overlap).</summary>
        internal struct Layout { public Rect In, Side, Main, Title, Search, Nerd, Content; public float ContentH; public string Page; public int Frame; }
        internal Layout Last;

        public void SelectPage(KitMod k, string id)
        {
            PageId = string.IsNullOrEmpty(id) ? "overview" : id;
            Search = ""; ScrollMain = Vector2.zero; MainViewH = 1000f;
            if (k.HasSaveProfile) EditTemplate = false;
        }

        public void OnClosed()
        {
            KitRow.ClearBuffers();
            PresetCacheTime = -1f;
            PageErrors.Clear();
        }
    }
}

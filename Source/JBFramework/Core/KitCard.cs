// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using UnityEngine;

namespace JBFramework
{
    /// <summary>
    /// The mod's own parts of its welcome card (<see cref="KitRegistry.WelcomeCard"/>): drawn areas, one-click actions and a callback when
    /// the card closes. The card itself stays the framework's: once per new colony and once when the mod joins a save (the "done" flag
    /// lives in the save's profile), never while the player switched welcome cards off (<see cref="Framework.WelcomeCards"/>), header with
    /// the mod mark and the pitch, the presets and section switches (unless hidden), the footer with "Use for all new colonies".
    /// Needs <see cref="KitInfo.PerSave"/>. Order in the body: the mod's draw parts, the presets, the actions, the section switches.
    /// </summary>
    public sealed class KitCard
    {
        internal KitCard() { }
        internal KitMod Owner;
        internal readonly List<Func<Rect, float>> Parts = new List<Func<Rect, float>>();
        internal readonly List<float> PartHeights = new List<float>();
        internal readonly List<CardAction> Actions = new List<CardAction>();
        internal bool NoPresets, NoSections;
        internal Action<bool> Closed;

        internal sealed class CardAction
        {
            public string Id, LabelEn, TipEn; public Func<bool> Done; public Action Act;
        }

        /// <summary>
        /// An area the mod draws in the card body (above the presets). <paramref name="draw"/> gets a rect at the part's top with the body
        /// width (its height is what the part returned last frame) and returns the height it used. Runs inside the card's error fence;
        /// use <see cref="KitStyle"/> and <see cref="KitUI.TextH(string, float)"/>. Several calls add several parts.
        /// </summary>
        public KitCard WithDraw(Func<Rect, float> draw) { if (draw != null) { Parts.Add(draw); PartHeights.Add(60f); } return this; }

        /// <summary>
        /// A one-click action under the presets ("Open the hospital now"): a button that runs <paramref name="act"/> (fenced); while
        /// <paramref name="done"/> returns true it shows as a green "done" chip instead (null = never done). Save state, not a setting.
        /// Texts: &lt;Prefix&gt;.Card.&lt;id&gt; and &lt;Prefix&gt;.Card.&lt;id&gt;.Tip, English fallbacks given here.
        /// </summary>
        public KitCard WithAction(string id, string label, string tip, Func<bool> done, Action act)
        {
            if (!string.IsNullOrEmpty(id) && act != null) Actions.Add(new CardAction { Id = id, LabelEn = label, TipEn = tip, Done = done, Act = act });
            return this;
        }

        /// <summary>The card shows no preset cards (the mod offers its own choice in a <see cref="WithDraw"/> part).</summary>
        public KitCard WithoutPresets() { NoPresets = true; return this; }

        /// <summary>The card shows no section switches.</summary>
        public KitCard WithoutSections() { NoSections = true; return this; }

        /// <summary>
        /// Called once when the card closes (any way), after the framework stored its choices; the argument is true when the mod joined an
        /// existing save. The save's profile is <see cref="KitMod.SaveProfile"/>. Fenced.
        /// </summary>
        public KitCard WhenClosed(Action<bool> closed) { Closed = closed; return this; }

        internal string Label(CardAction a) => Owner.Key("Card." + a.Id).TranslateOr(a.LabelEn ?? a.Id);
        internal string Tip(CardAction a) => Owner.Key("Card." + a.Id + ".Tip").TranslateOr(a.TipEn ?? "");
    }
}

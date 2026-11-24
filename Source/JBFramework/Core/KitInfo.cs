// SPDX-License-Identifier: MIT
// JB Framework: shared foundation of JB's RimWorld mods, lifted from Hearth & Harbor.
using System.Collections.Generic;
using UnityEngine;

namespace JBFramework
{
    /// <summary>
    /// What the framework needs to know about a mod. Passed once to the <see cref="KitModBase"/> constructor. Every field is optional;
    /// missing ones are filled from the mod's About.xml (name) or derived (ShortId from the name, KeyPrefix = ShortId).
    /// </summary>
    public sealed class KitInfo
    {
        /// <summary>Player-facing mod name ("Luciferium Overhaul"). Used in the log prefix, letters and the window title.</summary>
        public string Name;
        /// <summary>Short id for files and log channels ("LO"): the mod's log file is &lt;ShortId&gt;.log in the game's config folder.</summary>
        public string ShortId;
        /// <summary>Prefix of the translation keys of the mod's own registrations ("LO" → "LO.Setting.*", "LO.Section.*", "LO.Preset.*").</summary>
        public string KeyPrefix;
        /// <summary>Mod version shown on the About page and in the diagnosis report ("2.0.0").</summary>
        public string Version;
        /// <summary>True: setting values and section switches live in each save; the settings file holds the template for new games.</summary>
        public bool PerSave;
        /// <summary>Steam Workshop id (About/PublishedFileId.txt), shown on the About page. Optional.</summary>
        public string WorkshopId;
        /// <summary>Project or source link for the About page. Optional.</summary>
        public string Url;
        /// <summary>Licence line for the About page ("MIT"). Optional.</summary>
        public string LicenseLine;
        /// <summary>Harmony id for the mod's patch classes. Default (null): the mod's packageId as loaded.</summary>
        public string HarmonyId;
        /// <summary>Two-line pitch for the welcome card (needs <see cref="PerSave"/>). Null = no card.</summary>
        public string FirstStartPitch;
        /// <summary>
        /// Lowest framework API level the mod's code needs. 0 (default) = the level stamped by build/JBMod.props
        /// (<see cref="KitRequiresAttribute"/>), which is the level the mod was built against. Set it higher by hand if needed.
        /// </summary>
        public int MinApiLevel;
        /// <summary>Look of the mod's settings window (accent colour, mark, icons).</summary>
        public KitTheme Theme = new KitTheme();
    }

    /// <summary>Per-mod look. Textures are loaded lazily; a missing texture draws nothing (never a pink square).</summary>
    public sealed class KitTheme
    {
        /// <summary>Accent colour (headings, changed-row edge, selected chips). Default: Hearth &amp; Harbor amber.</summary>
        public Color Accent = new Color(0.95f, 0.72f, 0.30f);
        /// <summary>The mod's mark (a 128 px texture path such as "UI/LO/Mark"). Null = no mark.</summary>
        public string MarkTex;
        /// <summary>Optional icon per page id ("overview", "general", "compat", "diagnosis", "about", "section:&lt;id&gt;", "page:&lt;id&gt;").</summary>
        public Dictionary<string, string> Icons = new Dictionary<string, string>();
    }
}

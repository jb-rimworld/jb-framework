// SPDX-License-Identifier: MIT
// JB.Kit: shared foundation of JB's RimWorld mods, lifted from Hearth & Harbor. Compiled into each mod's own DLL (all types internal).
using System.Collections.Generic;
using UnityEngine;

namespace JBKit
{
    /// <summary>
    /// What the kit needs to know about the mod. Passed once to <see cref="KitModBase"/>'s constructor.
    /// The generated part (obj/1.7/JBKitInfo.g.cs, JBMod.targets) adds the constants KitVersion, KitCommit, KitTier, KitTests.
    /// </summary>
    internal sealed partial class KitInfo
    {
        /// <summary>Player-facing mod name ("Luciferium Overhaul"). Used in log prefix, letters, window title.</summary>
        public string Name;
        /// <summary>Short id for files and channels ("LO"): log file &lt;ShortId&gt;.log in the config folder.</summary>
        public string ShortId;
        /// <summary>Prefix of every translation key and texture path of this mod's kit parts ("LO" → "LO.Kit.*", "LO.Setting.*", "UI/LO/Kit/*").</summary>
        public string KeyPrefix;
        /// <summary>Mod version shown in About and the diagnosis report ("2.0.0").</summary>
        public string Version;
        /// <summary>True: setting values and section switches live in the save (the mod declares a KitGameBase subclass); the settings file holds the template for new games.</summary>
        public bool PerSave;
        /// <summary>Reserved for the optional gate (src/Gate, not part of kit 1.0).</summary>
        public bool UseGate;
        /// <summary>Steam Workshop id (About/PublishedFileId.txt), shown on the About page. Optional.</summary>
        public string WorkshopId;
        /// <summary>Project or source link. Optional.</summary>
        public string Url;
        /// <summary>Licence line for the About page ("MIT"). Optional.</summary>
        public string LicenseLine;
        /// <summary>Harmony id. Default (null): the mod's packageId as loaded (test slots rename it, that is intended).</summary>
        public string HarmonyId;
        /// <summary>Two-line pitch for the first-start card. Null = no card (default). The card itself is drawn by src/UI.</summary>
        public string FirstStartPitch;
        /// <summary>Look of the window (accent colour, mark, icons).</summary>
        public KitTheme Theme = new KitTheme();
    }

    /// <summary>Per-mod look. Textures are looked up lazily by src/UI; a missing texture draws nothing (vanilla fallback icons).</summary>
    internal sealed class KitTheme
    {
        /// <summary>Accent colour (headings, changed-row edge, selected chips). Default: H&amp;H amber.</summary>
        public Color Accent = new Color(0.95f, 0.72f, 0.30f);
        /// <summary>Mod mark texture path (128 px), e.g. "UI/LO/Kit/Mark". Null = no mark.</summary>
        public string MarkTex;
        /// <summary>Optional icon per page id ("overview", "general", "compat", "diagnosis", "about", "section:&lt;id&gt;", "page:&lt;id&gt;").</summary>
        public Dictionary<string, string> Icons = new Dictionary<string, string>();

        /// <summary>The accent at 35 % alpha (backgrounds of selected rows/chips).</summary>
        public Color AccentSoft => new Color(Accent.r, Accent.g, Accent.b, 0.35f);
    }
}

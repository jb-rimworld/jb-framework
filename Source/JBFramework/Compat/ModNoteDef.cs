// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// One entry of the framework's compatibility knowledge base: what JB mods (and anyone else) should know about another mod. Data
    /// only, in <c>Common/Defs/ModNotes/*.xml</c>; translations through DefInjected (<c>JBFramework.ModNoteDef</c>). Updating the
    /// framework updates the knowledge for every mod that queries it (<see cref="KitKnown"/>).
    /// <code>
    /// &lt;JBFramework.ModNoteDef&gt;
    ///   &lt;defName&gt;JBF_Note_NiceBillTab&lt;/defName&gt;
    ///   &lt;label&gt;Nice Bill Tab&lt;/label&gt;
    ///   &lt;description&gt;What the mod does, one or two sentences.&lt;/description&gt;
    ///   &lt;packageIds&gt;&lt;li&gt;Andromeda.NiceBillTab&lt;/li&gt;&lt;/packageIds&gt;
    ///   &lt;tags&gt;&lt;li&gt;changes-bills-ui&lt;/li&gt;&lt;/tags&gt;
    ///   &lt;pitfall&gt;What tends to break.&lt;/pitfall&gt;
    ///   &lt;advice&gt;What a mod (or the player) should do.&lt;/advice&gt;
    /// &lt;/JBFramework.ModNoteDef&gt;
    /// </code>
    /// The type's full name is fixed forever (it is in XML files of other mods that may add notes).
    /// </summary>
    public sealed class ModNoteDef : Def
    {
        /// <summary>Every packageId the mod was published under (forks, re-uploads, "Continued" versions), as in their About.xml.</summary>
        public List<string> packageIds = new List<string>();
        /// <summary>Tags (lower case, dash-separated; see <see cref="KitTags"/>) that mods query by.</summary>
        public List<string> tags = new List<string>();
        /// <summary>RimWorld versions listed in the About.xml files the note was made from (information only).</summary>
        public List<string> versions = new List<string>();
        /// <summary>Steam Workshop ids of the uploads the note was made from (information only).</summary>
        public List<string> workshopIds = new List<string>();
        /// <summary>Harmony ids the mod patches under, when they differ from its packageId (for the "who else patches this" report).</summary>
        public List<string> harmonyIds = new List<string>();
        /// <summary>Known pitfalls with other mods or with JB mods (player-facing, translated). Empty when none are known.</summary>
        [MustTranslate] public string pitfall;
        /// <summary>What to do about it (player-facing, translated). Empty when nothing needs doing.</summary>
        [MustTranslate] public string advice;
        /// <summary>Where the facts come from (not shown to players): About.xml paths, knowledge files, own tests.</summary>
        [NoTranslate] public string source;

        /// <summary>True when one of <see cref="packageIds"/> is active.</summary>
        public bool Active => packageIds.Any(KitDetect.IsActive);

        /// <summary>The active packageId, or null.</summary>
        public string ActivePackageId => packageIds.FirstOrDefault(KitDetect.IsActive);

        /// <summary>Has the note the tag (case-insensitive)?</summary>
        public bool HasTag(string tag) => !string.IsNullOrEmpty(tag) && tags.Any(t => string.Equals(t, tag, System.StringComparison.OrdinalIgnoreCase));

        /// <summary>Checks the note (missing packageIds, duplicate tags).</summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (packageIds == null || packageIds.Count == 0) yield return "ModNoteDef without packageIds";
            if (tags == null) tags = new List<string>();
            if (tags.Any(t => string.IsNullOrEmpty(t) || t != t.Trim().ToLowerInvariant())) yield return "ModNoteDef tags must be lower case without spaces around";
        }
    }

    /// <summary>
    /// Tags used in the knowledge base. Plain strings: a mod may use its own tags too; these are the ones the framework's notes use and
    /// that the framework's adapters care about.
    /// </summary>
    public static class KitTags
    {
        /// <summary>Custom humanoid races (Humanoid Alien Races and race mods built on it, Vanilla Races Expanded).</summary>
        public const string AlienRaces = "alien-races";
        /// <summary>Changes the bills tab or the bill list UI.</summary>
        public const string ChangesBillsUi = "changes-bills-ui";
        /// <summary>Changes how bills, workbenches or crafting jobs behave.</summary>
        public const string ChangesBills = "changes-bills";
        /// <summary>Adds pipe networks (VE PipeSystem, plumbing).</summary>
        public const string AddsPipeNetworks = "adds-pipe-networks";
        /// <summary>Replaces combat (ammo, armour, cover).</summary>
        public const string CombatOverhaul = "combat-overhaul";
        /// <summary>Replaces or extends construction (replace in place, blueprints over things).</summary>
        public const string ReplacesConstruction = "replaces-construction";
        /// <summary>Changes item or construction quality.</summary>
        public const string ChangesQuality = "changes-quality";
        /// <summary>Changes storage, stack sizes or hauling.</summary>
        public const string ChangesStorage = "changes-storage";
        /// <summary>Changes medicine, beds, surgery or healing.</summary>
        public const string ChangesMedical = "changes-medical";
        /// <summary>Adds or changes visitors and guests.</summary>
        public const string Guests = "guests";
        /// <summary>Adds robots or mechanoid workers.</summary>
        public const string Robots = "robots";
        /// <summary>Edits pawns or items directly (character editors).</summary>
        public const string Editor = "editor";
        /// <summary>Changes performance (optimisers, profilers).</summary>
        public const string Performance = "performance";
        /// <summary>Patches assemblies very early (before Harmony).</summary>
        public const string EarlyPatcher = "early-patcher";
        /// <summary>Multiplayer: needs deterministic code.</summary>
        public const string Multiplayer = "multiplayer";
        /// <summary>Calls an AI language model.</summary>
        public const string Ai = "ai";
        /// <summary>A code library other mods depend on.</summary>
        public const string Library = "library";
        /// <summary>Changes the architect menu or building lists.</summary>
        public const string ChangesArchitect = "changes-architect";
        /// <summary>Changes the work tab or work priorities.</summary>
        public const string ChangesWork = "changes-work";
        /// <summary>Adds furniture, buildings or floors.</summary>
        public const string AddsBuildings = "adds-buildings";
        /// <summary>Changes the economy: trade, prices, silver.</summary>
        public const string Economy = "economy";
        /// <summary>Changes psycasts or abilities.</summary>
        public const string Psycasts = "psycasts";
        /// <summary>Changes genes or xenotypes.</summary>
        public const string Genes = "genes";
        /// <summary>Changes the pawn inspect UI (HUDs, tabs).</summary>
        public const string ChangesPawnUi = "changes-pawn-ui";
        /// <summary>A JB mod (Julius Berger).</summary>
        public const string JbMod = "jb-mod";
    }
}

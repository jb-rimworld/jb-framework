// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using RimWorld;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// The framework as a whole: its identity and version, the list of mods that use it (tenants) and the combined report.
    /// Every dependent mod owns one <see cref="KitMod"/> (its <see cref="KitModBase.Kit"/>); this class only keeps the list.
    /// Nothing here depends on the order in which RimWorld creates mod classes.
    /// </summary>
    public static partial class Framework
    {
        static readonly object Lock = new object();
        static readonly List<KitMod> all = new List<KitMod>();
        static readonly Dictionary<Type, KitMod> byModType = new Dictionary<Type, KitMod>();
        // snapshots rebuilt on every Add (ticks and frames read them without allocating)
        static IReadOnlyList<KitMod> modsSnapshot = new KitMod[0];
        static KitMod[] allSnapshot = new KitMod[0];

        /// <summary>
        /// API level of the installed framework. It rises by one with every release that adds public API. A dependent mod states the
        /// level it needs (stamped automatically by build/JBMod.props as <see cref="KitRequiresAttribute"/>); when the installed level is
        /// lower, that mod's framework features stay off and the player is told in plain words to update the framework.
        /// </summary>
        public static int ApiLevel => IdApiLevel;

        /// <summary>Version of the installed framework ("1.0.0"), the same as its About.xml modVersion.</summary>
        public static string Version => IdVersion;

        /// <summary>Player-facing name of the framework ("JB Framework").</summary>
        public static string Name => IdName;

        /// <summary>packageId of the framework mod as published (without test-slot or Workshop suffixes).</summary>
        public static string PackageId => IdPackageId;

        /// <summary>The mods that use the framework, in the order they were created. The framework itself is not in this list.</summary>
        public static IReadOnlyList<KitMod> Mods => modsSnapshot;

        /// <summary>
        /// The context of a dependent mod by packageId (any case, with or without the Workshop "_steam" postfix), or null.
        /// </summary>
        public static KitMod Find(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return null;
            string want = KitCompat.Strip(packageId);
            lock (Lock) return all.FirstOrDefault(k => !k.IsFramework && string.Equals(KitCompat.Strip(k.PackageId), want, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The context of the mod whose <see cref="KitModBase"/> subclass is <typeparamref name="TMod"/>, or null before RimWorld created
        /// that mod class (static constructors of [StaticConstructorOnStartup] classes, defs and patches all run later, so from there it
        /// is safe; inside Register use <c>r.Kit</c>). Handy for patches: <c>var k = Framework.Of&lt;LOMod&gt;(); if (k?.On("walls") != true) return;</c>
        /// </summary>
        public static KitMod Of<TMod>() where TMod : KitModBase
        {
            lock (Lock) return byModType.TryGetValue(typeof(TMod), out var k) ? k : null;
        }

        /// <summary>Is a mod or DLC active? packageId with or without the Workshop "_steam" postfix, any case. Never throws.</summary>
        public static bool IsModActive(string packageIdOrDlc) => KitCompat.IsModActive(packageIdOrDlc);

        /// <summary>
        /// The combined diagnosis report of the framework and every dependent mod, as plain text for bug reports. Never contains
        /// personal paths. Never throws: a mod whose report fails shows the error line instead.
        /// </summary>
        public static string DiagnosisReport() => KitReport.Combined();

        // internal ---------------------------------------------------------------------------------------------------------

        /// <summary>The framework's own context (its settings hold the global preferences). Null until the framework mod was created.</summary>
        internal static KitMod Self { get; private set; }

        /// <summary>Every context, the framework's own included (snapshot).</summary>
        internal static KitMod[] AllContexts() => allSnapshot;

        internal static void Add(KitMod k)
        {
            lock (Lock)
            {
                all.Add(k);
                if (k.Mod != null) byModType[k.Mod.GetType()] = k;
                if (k.IsFramework) Self = k;
                allSnapshot = all.ToArray();
                modsSnapshot = new System.Collections.ObjectModel.ReadOnlyCollection<KitMod>(all.Where(x => !x.IsFramework).ToArray());
            }
        }

        /// <summary>The context whose mod assembly is <paramref name="a"/> (settings objects find their mod this way), or null.</summary>
        internal static KitMod ForAssembly(Assembly a)
        {
            if (a == null) return null;
            lock (Lock) return all.FirstOrDefault(k => k.Mod != null && k.Mod.GetType().Assembly == a);
        }

        /// <summary>Runs <paramref name="a"/> once per context, each in its own fence: one mod's exception never reaches another.</summary>
        internal static void ForEach(Action<KitMod> a, string site)
        {
            foreach (var k in AllContexts())
            {
                try { a(k); }
                catch (Exception e)
                {
                    try { k.Guard.Fail(KitMod.Core, site, e); } catch { }
                }
            }
        }

        // global preferences (the framework's own settings; defaults until the framework mod exists) ----------------------
        internal const string WindowScaleKey = "jbf.windowScale";
        internal const string VerboseLogKey = "jbf.verboseLog";
        internal const string ShowNerdKey = "jbf.showNerd";
        internal const string WelcomeCardsKey = "jbf.welcomeCards";
        internal const string PrefsCategory = "Interface";

        static float Pref(string key, float fallback)
        {
            var s = Self;
            if (s == null || s.SettingsReg.Find(key) == null) return fallback;
            try { return s.SettingsReg.Value(s.SettingsReg.Find(key), s.Template); } catch { return fallback; }
        }

        /// <summary>Window size factor chosen by the player (all JB windows).</summary>
        internal static float WindowScale => Pref(WindowScaleKey, 1f);
        /// <summary>Info and debug lines also in the game log (they always go to each mod's own log file).</summary>
        internal static bool VerboseLog;
        /// <summary>Show nerd settings in every JB settings window.</summary>
        internal static bool ShowNerd => ShowNerdOverride ?? Pref(ShowNerdKey, 0f) >= 0.5f;
        /// <summary>Tests: show (or hide) nerd settings without touching the stored preference.</summary>
        internal static bool? ShowNerdOverride;
        /// <summary>
        /// The player's global "Show welcome cards" preference (Mod settings, JB Framework): false = no mod shows its welcome card, new
        /// colonies start from the templates silently. A mod that draws its own start screen respects it too.
        /// </summary>
        public static bool WelcomeCards => Pref(WelcomeCardsKey, 1f) >= 0.5f;

        internal static void RefreshGlobals()
        {
            VerboseLog = Pref(VerboseLogKey, 0f) >= 0.5f;
        }

        /// <summary>Mods whose installed framework is too old (filled by the contexts and by the startup scan).</summary>
        internal static readonly List<(string Mod, int Need, string Built)> TooOldMessages = new List<(string, int, string)>();

        /// <summary>The too-old messages in the game's language (rendered when asked: languages load after the mod constructors).</summary>
        internal static List<string> TooOldTexts()
        {
            lock (TooOldMessages) return TooOldMessages.Distinct().Select(m => TooOldText(m.Mod, m.Need, m.Built)).ToList();
        }

        internal static string TooOldText(string modName, int needLevel, string builtWith)
        {
            return KitText.K("Framework.TooOld",
                "{0} needs a newer {1} than the one you have ({4}). {0} stays switched off until {1} is updated; your game and your other mods keep working.\n\nTo update: restart Steam, or unsubscribe from {1} and subscribe again. (Technical: {0} needs API level {2} and was built with {1} {3}; this {1} has level {5}.)",
                modName, Name, needLevel, string.IsNullOrEmpty(builtWith) ? "?" : builtWith, Version, ApiLevel);
        }

        /// <summary>
        /// API level a mod assembly needs: the <see cref="KitRequiresAttribute"/> stamped by build/JBMod.props, read without running
        /// any of the mod's code (0 = not stamped).
        /// </summary>
        internal static int RequiredLevel(Assembly a, out string builtWith)
        {
            builtWith = null;
            try
            {
                foreach (var d in a.GetCustomAttributesData())
                {
                    if (d.AttributeType.Name != nameof(KitRequiresAttribute)) continue;
                    var args = d.ConstructorArguments;
                    if (args.Count > 1) builtWith = args[1].Value as string;
                    if (args.Count > 0 && args[0].Value is int lv) return lv;
                }
            }
            catch { }
            return 0;
        }

        internal static string Header()
        {
            string rw; try { rw = VersionControl.CurrentVersionStringWithRev; } catch { rw = "?"; }
            return $"{Name} {Version} (API level {ApiLevel}, {IdCommit}, built for RimWorld {IdRimVersion}) · RimWorld {rw}";
        }
    }

    /// <summary>
    /// Stamped on a dependent mod's assembly by build/JBMod.props: the API level the mod needs and the framework version it was built
    /// with. The framework reads it without running the mod's code, so a too-old framework can say so before anything breaks.
    /// Write it by hand only when a mod is built without JBMod.props.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class KitRequiresAttribute : Attribute
    {
        /// <summary>Needs at least <paramref name="apiLevel"/>; <paramref name="frameworkVersion"/> is the version the mod was built with.</summary>
        public KitRequiresAttribute(int apiLevel, string frameworkVersion)
        {
            ApiLevel = apiLevel;
            FrameworkVersion = frameworkVersion;
        }

        /// <summary>Lowest framework API level the mod works with.</summary>
        public int ApiLevel { get; }

        /// <summary>Framework version the mod was built with (shown to the player when the installed one is too old).</summary>
        public string FrameworkVersion { get; }
    }
}

// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace JBKit
{
    /// <summary>
    /// The kit's front door for mod code. One per mod assembly (every kit type is compiled into each mod's own DLL).
    /// Reads go against <see cref="Current"/>: the save's profile when the mod is per-save and a game runs, else the template.
    /// </summary>
    internal static class Kit
    {
        public static KitInfo Info { get; private set; }
        public static KitModBase Mod { get; private set; }
        /// <summary>The mod's settings object (null when the mod declares no KitSettingsBase subclass: then nothing is saved).</summary>
        public static KitSettingsBase Settings { get; internal set; }
        static readonly KitProfile fallbackTemplate = new KitProfile();

        /// <summary>The template profile (settings file). Global settings always live in its store.</summary>
        public static KitProfile Template => Settings?.Template ?? fallbackTemplate;

        /// <summary>The save's profile, or null (no game, not per-save, or no game component).</summary>
        public static KitProfile SaveProfile => Info != null && Info.PerSave ? KitGameBase.CurrentProfile : null;

        /// <summary>The profile all reads and <see cref="Set"/> go against right now.</summary>
        public static KitProfile Current => SaveProfile ?? Template;

        /// <summary>True while a game with a per-save profile runs (the window then offers "This save" and "Template").</summary>
        public static bool HasSaveProfile => SaveProfile != null;

        internal static void Init(KitInfo info, KitModBase mod)
        {
            Info = info ?? new KitInfo();
            Mod = mod;
            if (string.IsNullOrEmpty(Info.Name)) Info.Name = mod?.Content?.Name ?? "Mod";
            if (string.IsNullOrEmpty(Info.ShortId)) Info.ShortId = new string(Info.Name.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrEmpty(Info.KeyPrefix)) Info.KeyPrefix = Info.ShortId;
            if (string.IsNullOrEmpty(Info.HarmonyId)) Info.HarmonyId = mod?.Content?.PackageId ?? ("jbkit." + Info.ShortId.ToLowerInvariant());
            if (Info.Theme == null) Info.Theme = new KitTheme();
        }

        // Reading and writing (current profile) ------------------------------------------------------------------------
        public static bool On(string section) => KitSections.On(section);
        public static float Get(string key) => KitSettings.Get(key);
        public static bool GetBool(string key) => KitSettings.GetBool(key);
        public static int GetInt(string key) => KitSettings.GetInt(key);
        public static T GetEnum<T>(string key) where T : struct => KitSettings.GetEnum<T>(key);
        public static int GetSilver(string key) => KitSettings.GetSilver(key);
        public static void Set(string key, float v) => KitSettings.Set(key, v);
        public static void Set(string key, bool v) => KitSettings.Set(key, v ? 1f : 0f);
        public static void Reset(string key) => KitSettings.Reset(key);

        // Templates ----------------------------------------------------------------------------------------------------
        /// <summary>Copies the save's switches and values into the template (global values stay) and writes the settings file.</summary>
        public static void SaveAsTemplate()
        {
            var sp = SaveProfile; if (sp == null) return;
            Template.CopySettingsFrom(sp);
            WriteSettingsSafe();
            KitLog.Info("Core", "This save's settings saved as the template for new games");
        }

        /// <summary>Copies the template's switches and values into the running save.</summary>
        public static void ApplyTemplateToSave()
        {
            var sp = SaveProfile; if (sp == null) return;
            sp.CopySettingsFrom(Template);
            KitLog.Info("Core", "Template applied to this save");
        }

        internal static void WriteSettingsSafe()
        {
            try { Settings?.Write(); }
            catch (Exception e) { KitLog.Warn("Core", "Could not write the settings file: " + e.Message); }
        }

        /// <summary>Opens the mod settings window, on the given page when src/UI is present (page ids: see API.md). Never throws.</summary>
        public static void OpenSettings(string pageId = null)
        {
            try
            {
                if (pageId != null) KitHooks.SelectPage?.Invoke(pageId);
                if (Mod != null) Find.WindowStack.Add(new RimWorld.Dialog_ModSettings(Mod));
            }
            catch (Exception e) { KitLog.Warn("Core", "Could not open the mod settings: " + e.Message); }
        }

        // Change notification (debounced) --------------------------------------------------------------------------------
        static readonly List<string> pending = new List<string>();
        static int suppress;
        /// <summary>True while presets are compared on a scratch profile: no notifications, no writes to the template's global values.</summary>
        internal static bool Comparing => suppress > 0;

        internal static void SuppressNotifications(Action a)
        {
            suppress++;
            try { a(); } finally { suppress--; }
        }

        /// <summary>Remembers that a key ("lo.regrowDays" or "section:&lt;id&gt;") changed. Delivered by <see cref="FlushChanges"/>.</summary>
        internal static void MarkChanged(string key)
        {
            if (suppress > 0 || key == null) return;
            if (!pending.Contains(key)) pending.Add(key);
        }

        /// <summary>True when changes wait for delivery.</summary>
        internal static bool HasPendingChanges => pending.Count > 0;

        /// <summary>
        /// Delivers pending changes: the setting's OnChanged, then the mod's OnSettingsChanged(key), each guarded. Called once per frame by
        /// the settings window (KitModBase.DoSettingsWindowContents), on window close (WriteSettings) and by KitGameBase.GameComponentUpdate.
        /// </summary>
        internal static void FlushChanges()
        {
            if (pending.Count == 0) return;
            var keys = pending.ToArray();
            pending.Clear();
            foreach (var k in keys)
            {
                var s = KitSettings.Find(k);
                if (s?.OnChanged != null)
                {
                    var setting = s;
                    KitGuard.Run(s.Section, "OnChanged." + k, () => setting.OnChanged(KitSettings.Get(setting)));
                }
                if (k == "kit.verboseLog") KitLog.Verbose = GetBool("kit.verboseLog");
                if (Mod != null) KitGuard.Run(KitSections.Core, "OnSettingsChanged", () => Mod.OnSettingsChanged(k));
            }
        }
    }

    /// <summary>
    /// Extension points filled by src/UI and src/Testing (both are optional per tier). Core never references their types;
    /// they install themselves in KitBoot's partial methods, called from the KitModBase constructor after registration.
    /// </summary>
    internal static class KitHooks
    {
        /// <summary>Draws the whole settings window content. Null (tier core): the minimal KitFallbackWindow draws.</summary>
        public static Action<Rect> DrawSettings;
        /// <summary>Jumps to a page ("overview", "general", "section:&lt;id&gt;", "page:&lt;id&gt;", "compat", "diagnosis", "about").</summary>
        public static Action<string> SelectPage;
        /// <summary>Called when the settings window closes (Mod.WriteSettings), before the file is written.</summary>
        public static Action WindowClosed;
        /// <summary>Called once after defs are loaded (StaticConstructorOnStartup), after the mod's OnDefsLoaded.</summary>
        public static Action DefsLoaded;
        /// <summary>Called when a game starts or loads (after the profile is in place): (isNewGame, joinedExistingSave).</summary>
        public static Action<bool, bool> GameInit;
        /// <summary>Called in GameComponentOnGUI of the mod's KitGameBase (first-start card).</summary>
        public static Action GameOnGUI;
        /// <summary>Extra lines for the core report (src/UI diagnosis, src/Testing smoke).</summary>
        public static Func<IEnumerable<string>> ExtraReportLines;
    }

    /// <summary>Boot sequence pieces. src/UI implements <c>static partial void InstallUI()</c>, src/Testing <c>static partial void InstallTesting()</c>.</summary>
    internal static partial class KitBoot
    {
        static partial void InstallUI();
        static partial void InstallTesting();

        internal static void RunInstallers()
        {
            try { InstallUI(); } catch (Exception e) { KitLog.Error("Core", "Installing the kit window failed", e); }
            try { InstallTesting(); } catch (Exception e) { KitLog.Error("Core", "Installing the test harness failed", e); }
        }
    }
}

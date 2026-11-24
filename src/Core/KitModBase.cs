// SPDX-License-Identifier: MIT
// Decoupled from Hearth & Harbor Foundation/HearthMod.cs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace JBKit
{
    /// <summary>
    /// Base of the mod's Verse.Mod class. Public abstract (a public class cannot derive from an internal one), but its constructor and
    /// hooks are internal, so only the carrying assembly can derive from it. The game finds the concrete subclass by Type, never by name.
    /// <code>
    /// public sealed class LOMod : JBKit.KitModBase
    /// {
    ///     public LOMod(ModContentPack c) : base(c, new JBKit.KitInfo { Name = "Luciferium Overhaul", ShortId = "LO", KeyPrefix = "LO", Version = "2.0.0" }) { }
    ///     internal override void Register(JBKit.KitRegistry r) { r.Section(...); r.Float(...); }
    /// }
    /// </code>
    /// Order in the constructor: kit init → log session header → kit's own settings → Register(r) → load settings file (incl. legacy
    /// migration) → install src/UI and src/Testing → Harmony patch classes one by one. After defs: OnDefsLoaded.
    /// </summary>
    public abstract class KitModBase : Mod
    {
        internal KitModBase(ModContentPack content, KitInfo info) : base(content)
        {
            Kit.Init(info, this);
            KitLog.SessionStart();
            KitLog.Info("Core", $"Loading {Kit.Info.Name} {Kit.Info.Version} from {content?.PackageId} ({content?.FolderName}); JB.Kit {KitInfo.KitVersion} ({KitInfo.KitCommit}, tier {KitInfo.KitTier}{(KitInfo.KitTests ? ", tests" : "")})");
            KitBuiltins.Register();
            try { Register(new KitRegistry()); }
            catch (Exception e) { KitLog.Error("Core", "Register() threw; settings registered so far stay", e); }
            LoadSettingsObject();
            KitLog.Verbose = Kit.GetBool("kit.verboseLog");
            KitBoot.RunInstallers();
            try { KitPatches.ApplyAll(content?.assemblies?.loadedAssemblies ?? new List<Assembly> { GetType().Assembly }, Kit.Info.HarmonyId); }
            catch (Exception e) { KitLog.Error("Core", "Patch application failed", e); }
            CheckDeclarations();
        }

        /// <summary>Registers sections, settings, presets, compat entries, pages and caretaker checks. Override with <c>internal override</c>.</summary>
        internal abstract void Register(KitRegistry r);
        /// <summary>After all defs are loaded (StaticConstructorOnStartup). Guarded.</summary>
        internal virtual void OnDefsLoaded() { }
        /// <summary>A setting ("lo.regrowDays") or section ("section:walls") changed. Debounced: once per key per frame, and on window close.</summary>
        internal virtual void OnSettingsChanged(string key) { }
        /// <summary>A new game started (profile copied from the template). Needs the mod's KitGameBase subclass. Guarded.</summary>
        internal virtual void OnGameStarted() { }
        /// <summary>A save was loaded; <paramref name="joinedExistingSave"/> = the mod was not in that save before. Needs KitGameBase. Guarded.</summary>
        internal virtual void OnGameLoaded(bool joinedExistingSave) { }
        /// <summary>Extra lines for the diagnosis report.</summary>
        internal virtual IEnumerable<string> Diagnosis() { yield break; }

        /// <summary>The window title in the mod list. Default: the kit name.</summary>
        public override string SettingsCategory() => Kit.Info?.Name ?? "";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            try
            {
                if (KitHooks.DrawSettings != null) KitHooks.DrawSettings(inRect);
                else KitFallbackWindow.Draw(inRect);
            }
            catch (Exception e) { KitGuard.Fail(KitSections.Core, "SettingsWindow", e); }
            Kit.FlushChanges();
        }

        public override void WriteSettings()
        {
            try { KitHooks.WindowClosed?.Invoke(); } catch (Exception e) { KitLog.Warn("Core", "WindowClosed hook threw: " + e.Message); }
            Kit.FlushChanges();
            if (Kit.Settings != null) base.WriteSettings();
            KitLog.Verbose = Kit.GetBool("kit.verboseLog");
        }

        void LoadSettingsObject()
        {
            Type t = null;
            try
            {
                t = KitPatches.SafeTypes(GetType().Assembly).FirstOrDefault(x => typeof(KitSettingsBase).IsAssignableFrom(x) && !x.IsAbstract);
                if (t == null)
                {
                    KitLog.Error("Core", "No KitSettingsBase subclass in " + GetType().Assembly.GetName().Name + ": settings are not saved. Declare 'public sealed class <Name>Settings : JBKit.KitSettingsBase { }' (keep the OLD settings class name when migrating).");
                    return;
                }
                var m = typeof(Mod).GetMethod(nameof(GetSettings)).MakeGenericMethod(t);
                Kit.Settings = (KitSettingsBase)m.Invoke(this, null);
            }
            catch (Exception e) { KitLog.Error("Core", "Loading the settings (" + t?.Name + ") failed; defaults are used", e.InnerException ?? e); }
        }

        void CheckDeclarations()
        {
            bool hasGame = KitPatches.SafeTypes(GetType().Assembly).Any(x => typeof(KitGameBase).IsAssignableFrom(x) && !x.IsAbstract);
            if (Kit.Info.PerSave && !hasGame)
                KitLog.Error("Core", "PerSave = true but no KitGameBase subclass is declared: values stay global. Declare 'public sealed class <Name>Game : JBKit.KitGameBase { public <Name>Game(Game g) : base(g) { } }'.");
            if (KitCaretaker.Checks.Count > 0 && !hasGame)
                KitLog.Warn("Core", "Caretaker checks are registered but no KitGameBase subclass is declared: they never run.");
        }
    }

    /// <summary>Settings every kit mod has (section core, category "Interface"; all global).</summary>
    internal static class KitBuiltins
    {
        public const string WindowScaleKey = "kit.windowScale";
        public const string VerboseLogKey = "kit.verboseLog";
        public const string ShowNerdKey = "kit.showNerd";
        public const string FirstStartCardKey = "kit.firstStartCard";
        public const string Category = "Interface";

        internal static void Register()
        {
            var r = new KitRegistry();
            r.Percent(KitSections.Core, Category, WindowScaleKey, 1f, 0.8f, 1.5f, "Window size",
                "All windows of this mod grow or shrink by this factor (on top of the automatic size for large screens).", hardMin: 0.6f, hardMax: 2f).AsGlobal();
            r.Bool(KitSections.Core, Category, VerboseLogKey, false, "Detailed log",
                "Writes info and debug lines to the game log too (they always go to the mod's own log file). Only for hunting problems.").AsGlobal();
            r.Bool(KitSections.Core, Category, ShowNerdKey, false, "Show nerd settings",
                "Shows the technical settings that most players never need.").AsGlobal();
            if (!string.IsNullOrEmpty(Kit.Info?.FirstStartPitch))
                r.Bool(KitSections.Core, Category, FirstStartCardKey, true, "Show the welcome card",
                    "Once per new colony (and once when the mod joins a save): a small card with the presets and a link to these settings.").AsGlobal();
        }
    }

    /// <summary>Runs the mod's OnDefsLoaded and the DefsLoaded hook once all defs exist; writes settings again if a legacy migration happened.</summary>
    [StaticConstructorOnStartup]
    internal static class KitStartup
    {
        static KitStartup()
        {
            if (Kit.Mod == null) return;
            KitGuard.Run(KitSections.Core, "OnDefsLoaded", () => Kit.Mod.OnDefsLoaded());
            try { KitHooks.DefsLoaded?.Invoke(); } catch (Exception e) { KitLog.Error("Core", "DefsLoaded hook threw", e); }
            if (Kit.Settings != null && Kit.Settings.MigratedThisSession)
            {
                // drop the old flat nodes from the file now, so the migration is one-way and runs only once
                Kit.WriteSettingsSafe();
                KitLog.Info("Core", "Settings file rewritten in the kit format after migration (backup: *.pre-kit)");
            }
            KitLog.Info("Core", $"Defs loaded. Sections: {KitSections.All.Count}, settings: {KitSettings.All.Count}, presets: {KitPresets.All.Count}, compat entries: {KitCompat.All.Count}, patch failures: {KitPatches.Failures.Count}");
        }
    }
}

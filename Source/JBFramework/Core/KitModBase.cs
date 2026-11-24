// SPDX-License-Identifier: MIT
// Decoupled from Hearth & Harbor Foundation/HearthMod.cs; multi-tenant since the framework became its own mod.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// Base of a dependent mod's <see cref="Verse.Mod"/> class. RimWorld finds the subclass by Type; its name decides the settings file
    /// name (Mod_&lt;folder&gt;_&lt;TypeName&gt;.xml), so an existing mod keeps its old class name.
    /// <code>
    /// public sealed class LOMod : KitModBase
    /// {
    ///     public LOMod(ModContentPack c) : base(c, new KitInfo { Name = "Luciferium Overhaul", ShortId = "LO", Version = "2.0.0", PerSave = true }) { }
    ///     protected override void Register(KitRegistry r) { r.Section("healing", "Healing"); r.Float("healing", "Regrowth", "lo.regrowDays", 10f, 0.1f, 120f, "Days to regrow a limb"); }
    /// }
    /// </code>
    /// The base constructor does everything in this order: context, log header, API-level check, <see cref="Register"/>, settings file
    /// (with legacy migration), the mod's Harmony patch classes one by one. Fields of the subclass are not set yet while Register runs.
    /// Every hook below runs inside the mod's error fence; an exception never reaches the game or another mod.
    /// </summary>
    public abstract class KitModBase : Mod
    {
        /// <summary>Creates the mod's context and runs the boot sequence (see the class summary).</summary>
        protected KitModBase(ModContentPack content, KitInfo info) : base(content)
        {
            Kit = KitMod.Boot(this, content, info);
        }

        /// <summary>This mod's framework context: settings, sections, log, error fences.</summary>
        public KitMod Kit { get; }

        /// <summary>Registers sections, settings, presets, compatibility entries, pages, caretaker checks and per-save data. Called once.</summary>
        protected abstract void Register(KitRegistry r);
        /// <summary>After all defs are loaded (StaticConstructorOnStartup).</summary>
        protected virtual void OnDefsLoaded() { }
        /// <summary>A setting ("lo.regrowDays"), section ("section:walls") or the whole profile ("profile:loaded") changed. Debounced: once per key per frame.</summary>
        protected virtual void OnSettingsChanged(string key) { }
        /// <summary>A new game started (per-save profile copied from the template).</summary>
        protected virtual void OnGameStarted() { }
        /// <summary>A save was loaded; <paramref name="joinedExistingSave"/> = the mod was not in that save before.</summary>
        protected virtual void OnGameLoaded(bool joinedExistingSave) { }
        /// <summary>Every game tick while a game runs. Keep it cheap; check your own intervals.</summary>
        protected virtual void OnTick(int ticksGame) { }
        /// <summary>Extra lines for the mod's diagnosis page and report.</summary>
        protected virtual IEnumerable<string> Diagnosis() { yield break; }

        /// <summary>The name in the mod settings list: <see cref="KitInfo.Name"/>.</summary>
        public override string SettingsCategory() => Kit?.Info?.Name ?? Content?.Name ?? "";

        /// <summary>Draws the framework's settings window for this mod (sidebar, pages, search). Fenced.</summary>
        public sealed override void DoSettingsWindowContents(Rect inRect)
        {
            if (Kit == null) return;
            try { KitSettingsWindow.Draw(Kit, inRect); }
            catch (Exception e) { Kit.Guard.Fail(KitMod.Core, "SettingsWindow", e); }
            Kit.FlushChanges();
        }

        /// <summary>Called by the game when the settings window closes: delivers pending changes and writes the settings file.</summary>
        public sealed override void WriteSettings()
        {
            if (Kit == null) return;
            try { Kit.Window.OnClosed(); } catch (Exception e) { Kit.Log.Warn("Core", "Closing the window threw: " + e.Message); }
            Kit.FlushChanges();
            if (Kit.Settings != null && !Kit.Blocked)
            {
                try { base.WriteSettings(); }
                catch (Exception e) { Kit.Log.Warn("Core", "Could not write the settings file: " + e.Message); }
            }
            if (Kit.IsFramework) Framework.RefreshGlobals();
        }

        // bridges for the framework (the hooks are protected) ----------------------------------------------------------------
        internal void CallRegister(KitRegistry r) => Register(r);
        internal void CallOnDefsLoaded() => OnDefsLoaded();
        internal void CallOnSettingsChanged(string key) => OnSettingsChanged(key);
        internal void CallOnGameStarted() => OnGameStarted();
        internal void CallOnGameLoaded(bool joined) => OnGameLoaded(joined);
        internal void CallOnTick(int t) => OnTick(t);
        internal IEnumerable<string> CallDiagnosis() => Diagnosis();
    }

    /// <summary>
    /// The framework's own mod class (one entry "JB Framework" in Mod settings): the mods found, their versions and areas, patch
    /// failures, the combined report and the global preferences (window size, detailed log, welcome cards, nerd settings).
    /// Created by RimWorld; mods never use it.
    /// </summary>
    public sealed class FrameworkMod : KitModBase
    {
        /// <summary>Created by RimWorld.</summary>
        public FrameworkMod(ModContentPack content) : base(content, new KitInfo
        {
            Name = Framework.Name,
            ShortId = "JBFramework",
            KeyPrefix = "JBF",
            Version = Framework.Version,
            LicenseLine = "MIT",
            Theme = new KitTheme { Accent = new Color(0.95f, 0.72f, 0.30f), MarkTex = "UI/JBFramework/Mark", Icons = { ["page:mods"] = "UI/JBFramework/Mods" } },
        }) { }

        /// <summary>The global preferences and the Mods page.</summary>
        protected override void Register(KitRegistry r)
        {
            r.Page("mods", "Mods", order: -10, draw: FrameworkPages.DrawMods);
            r.Percent(KitMod.Core, Framework.PrefsCategory, Framework.WindowScaleKey, 1f, 0.8f, 1.5f, "Window size",
                "Every window of every JB mod grows or shrinks by this factor (on top of the automatic size for large screens). Applies the next time a window opens.",
                hardMin: 0.6f, hardMax: 2f).AsGlobal();
            r.Bool(KitMod.Core, Framework.PrefsCategory, Framework.VerboseLogKey, false, "Detailed log",
                "Writes info and debug lines to the game log too (they always go to each mod's own log file). Only for hunting problems.").AsGlobal();
            r.Bool(KitMod.Core, Framework.PrefsCategory, Framework.ShowNerdKey, false, "Show nerd settings",
                "Shows the technical settings that most players never need, in every JB settings window.").AsGlobal();
            r.Bool(KitMod.Core, Framework.PrefsCategory, Framework.WelcomeCardsKey, true, "Show welcome cards",
                "A small card with presets and a link to the settings, once per new colony (and once when a mod joins a save), for every JB mod that has one.").AsGlobal();
        }

        /// <summary>One line per mod that uses the framework.</summary>
        protected override IEnumerable<string> Diagnosis() => KitReport.TenantSummary();

        /// <summary>Global preferences changed.</summary>
        protected override void OnSettingsChanged(string key) => Framework.RefreshGlobals();
    }

    /// <summary>The framework's own settings file (global preferences). Created by RimWorld.</summary>
    public sealed class FrameworkSettings : KitSettingsBase { }

    /// <summary>Runs every mod's OnDefsLoaded once all defs exist, rewrites migrated settings files, tells the player about a too-old framework.</summary>
    [StaticConstructorOnStartup]
    internal static class KitStartup
    {
        static KitStartup()
        {
            Framework.ForEach(k =>
            {
                if (k.Blocked || k.Mod == null) return;
                k.Guard.Run(KitMod.Core, "OnDefsLoaded", () => k.Mod.CallOnDefsLoaded());
                if (k.Settings != null && k.Settings.MigratedThisSession)
                {
                    // drop the old flat nodes from the file now, so the migration is one-way and runs only once
                    k.WriteSettings();
                    k.Log.Info("Core", "Settings file rewritten in the framework format after migration (backup: *.pre-kit)");
                }
                k.Log.Info("Core", $"Defs loaded. Sections: {k.Sections.All.Count}, settings: {k.SettingsReg.All.Count}, presets: {k.Presets.All.Count}, compat entries: {k.Compat.All.Count}, patch failures: {k.Patches.Failures.Count}");
            }, "Startup");
            KitHooks.Each(KitHooks.DefsLoaded, null, "DefsLoaded", h => h());
            ScanForTooNew();
            ShowTooOld();
        }

        /// <summary>Mods whose Mod class never reached the framework (constructor failed) but that need a newer API level.</summary>
        static void ScanForTooNew()
        {
            try
            {
                var known = new HashSet<System.Reflection.Assembly>(Framework.AllContexts().Where(k => k.Mod != null).Select(k => k.Mod.GetType().Assembly));
                foreach (var mcp in LoadedModManager.RunningMods)
                {
                    var asms = mcp?.assemblies?.loadedAssemblies;
                    if (asms == null) continue;
                    foreach (var a in asms)
                    {
                        if (a == null || known.Contains(a) || a == typeof(Framework).Assembly) continue;
                        int need = Framework.RequiredLevel(a, out string built);
                        if (need > Framework.ApiLevel)
                            lock (Framework.TooOldMessages) Framework.TooOldMessages.Add(Framework.TooOldText(mcp.Name, need, built));
                    }
                }
            }
            catch (Exception e) { Framework.Self?.Log.Warn("Core", "API level scan failed: " + e.Message); }
        }

        static void ShowTooOld()
        {
            List<string> msgs;
            lock (Framework.TooOldMessages) msgs = Framework.TooOldMessages.Distinct().ToList();
            if (msgs.Count == 0) return;
            foreach (var m in msgs) Framework.Self?.Log.Warn("Core", m);
            try
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    try { Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n\n", msgs), title: KitText.K("Framework.TooOldTitle", "{0} needs an update", Framework.Name))); }
                    catch (Exception e) { Log.Warning("[" + Framework.Name + "] " + string.Join(" ", msgs) + " (" + e.Message + ")"); }
                });
            }
            catch { }
        }
    }
}

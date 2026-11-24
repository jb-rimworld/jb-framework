// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// One dependent mod's context: its settings, sections, presets, compatibility entries, log, error fences and patch report.
    /// Created by the <see cref="KitModBase"/> constructor and reachable as <see cref="KitModBase.Kit"/> (or
    /// <see cref="Framework.Of{TMod}"/> / <see cref="Framework.Find"/> from patches). Nothing in it is shared with other mods.
    /// Reads go against <see cref="Current"/>: the save's profile when the mod is per-save and a game runs, else the template.
    /// </summary>
    public sealed class KitMod
    {
        /// <summary>The always-on pseudo section for general settings and framework code. Never paused, never switched off.</summary>
        public const string Core = "core";

        internal readonly KitSettingRegistry SettingsReg;
        internal readonly KitSectionRegistry Sections;
        internal readonly KitPresetRegistry Presets;
        internal readonly KitCompatRegistry Compat;
        internal readonly KitPageRegistry Pages;
        internal readonly KitGuard Guard;
        internal readonly KitCaretaker Caretaker;
        internal readonly KitPatches Patches;
        /// <summary>UI state of this mod's settings window (src UI; one window state per mod).</summary>
        internal readonly KitWindowState Window = new KitWindowState();
        /// <summary>Per-save expose callback (<see cref="KitRegistry.SaveData(System.Action, System.Action)"/>) and its reset.</summary>
        internal Action SaveExpose, SaveReset;
        /// <summary>Per-map expose callback (<see cref="KitRegistry.MapData(Action{Map})"/>) and its reset.</summary>
        internal Action<Map> MapExpose, MapReset;
        /// <summary>The mod's welcome-card parts (<see cref="KitRegistry.WelcomeCard"/>), or null.</summary>
        internal KitCard Card;
        internal readonly bool IsFramework;
        readonly KitProfile fallbackTemplate;
        readonly List<string> clashes = new List<string>();

        KitMod(KitModBase mod, ModContentPack content, KitInfo info)
        {
            Mod = mod;
            Content = content;
            Cfg = info?.Clone() ?? new KitInfo();
            IsFramework = mod is FrameworkMod;
            PackageId = (content?.PackageId ?? ("jbframework.unknown." + (mod?.GetType().Name ?? "mod"))).ToLowerInvariant();
            if (string.IsNullOrEmpty(Cfg.Name)) Cfg.Name = content?.Name ?? mod?.GetType().Name ?? "Mod";
            if (string.IsNullOrEmpty(Cfg.ShortId)) Cfg.ShortId = new string(Cfg.Name.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrEmpty(Cfg.ShortId)) Cfg.ShortId = "Mod";
            if (string.IsNullOrEmpty(Cfg.KeyPrefix)) Cfg.KeyPrefix = Cfg.ShortId;
            if (string.IsNullOrEmpty(Cfg.HarmonyId)) Cfg.HarmonyId = content?.PackageId ?? ("jbframework." + Cfg.ShortId.ToLowerInvariant());
            if (Cfg.Theme == null) Cfg.Theme = new KitTheme();
            // ShortId names the log file and KeyPrefix the translation keys: two mods must not share them
            foreach (var other in Framework.AllContexts())
            {
                if (string.Equals(other.Cfg.ShortId, Cfg.ShortId, StringComparison.OrdinalIgnoreCase))
                {
                    clashes.Add("ShortId '" + Cfg.ShortId + "' is also used by " + other.Name + "; this mod logs to " + Cfg.ShortId + "_" + Framework.AllContexts().Length + ".log instead");
                    Cfg.ShortId = Cfg.ShortId + "_" + Framework.AllContexts().Length;
                }
                if (string.Equals(other.Cfg.KeyPrefix, Cfg.KeyPrefix, StringComparison.Ordinal))
                    clashes.Add("KeyPrefix '" + Cfg.KeyPrefix + "' is also used by " + other.Name + ": translations of the two mods can mix. Give each mod its own KeyPrefix");
            }
            Log = new KitLog(this);
            SettingsReg = new KitSettingRegistry(this);
            Sections = new KitSectionRegistry(this);
            Presets = new KitPresetRegistry(this);
            Compat = new KitCompatRegistry(this);
            Pages = new KitPageRegistry(this);
            Guard = new KitGuard(this);
            Caretaker = new KitCaretaker(this);
            Patches = new KitPatches(this);
            fallbackTemplate = new KitProfile { Owner = this };
        }

        // Identity ---------------------------------------------------------------------------------------------------------
        /// <summary>The mod's packageId as loaded, lower case (a test slot or the Workshop may add a suffix).</summary>
        public string PackageId { get; }
        /// <summary>The mod's name (<see cref="KitInfo.Name"/>).</summary>
        public string Name => Cfg.Name;
        /// <summary>The mod's version (<see cref="KitInfo.Version"/>).</summary>
        public string Version => Cfg.Version;
        /// <summary>What the mod told the framework, filled with defaults. A copy: changing it has no effect.</summary>
        public KitInfo Info => Cfg.Clone();
        /// <summary>The framework's own copy of the mod's info (fixed after the constructor).</summary>
        internal readonly KitInfo Cfg;
        /// <summary>The mod's <see cref="Verse.Mod"/> class instance.</summary>
        public KitModBase Mod { get; }
        /// <summary>The mod's content pack (folders, packageId, assemblies).</summary>
        public ModContentPack Content { get; }
        /// <summary>The mod's log (own file &lt;ShortId&gt;.log, game log prefix "[&lt;Name&gt;]").</summary>
        public KitLog Log { get; }
        /// <summary>The mod's settings object, or null when the mod declares no <see cref="KitSettingsBase"/> subclass (nothing is saved).</summary>
        public KitSettingsBase Settings { get; internal set; }
        /// <summary>True when the installed framework is too old for this mod: its framework features stay off (see <see cref="BlockedReason"/>).</summary>
        public bool Blocked { get; private set; }
        /// <summary>Plain-words reason when <see cref="Blocked"/>, else null.</summary>
        public string BlockedReason { get; private set; }
        /// <summary>API level the mod needs (from <see cref="KitRequiresAttribute"/> or <see cref="KitInfo.MinApiLevel"/>; 0 = not stated).</summary>
        public int RequiredApiLevel { get; private set; }
        /// <summary>Framework version the mod was built with (from <see cref="KitRequiresAttribute"/>), or null.</summary>
        public string BuiltWithFramework { get; private set; }

        // Profiles ---------------------------------------------------------------------------------------------------------
        /// <summary>The template profile (settings file). Global settings always live in its store.</summary>
        public KitProfile Template => Settings?.Template ?? fallbackTemplate;

        /// <summary>The running save's profile, or null (no game, or the mod is not per-save).</summary>
        public KitProfile SaveProfile
        {
            get
            {
                if (!Cfg.PerSave || Blocked) return null;
                var g = saveGame;
                if (g == null) return null;
                try { return ReferenceEquals(g, Verse.Current.Game) ? saveProfile : null; } catch { return null; }
            }
        }
        Game saveGame; KitProfile saveProfile;
        internal void AttachSave(Game g, KitProfile p) { saveGame = g; saveProfile = p; if (p != null) p.Owner = this; }

        /// <summary>The profile all reads and <see cref="Set(string, float)"/> go against right now: <see cref="SaveProfile"/> ?? <see cref="Template"/>.</summary>
        public KitProfile Current => SaveProfile ?? Template;

        /// <summary>True while a game with a per-save profile runs (the settings window then offers "This save" and "Template").</summary>
        public bool HasSaveProfile => SaveProfile != null;

        // Reading and writing (current profile) ----------------------------------------------------------------------------
        /// <summary>
        /// True if the section is active right now: available (its DLC or mod is present), not paused by the error fence, and switched on
        /// in <see cref="Current"/>. Every patch of a section returns early when this is false. <see cref="Core"/> is always on.
        /// Always false while <see cref="Blocked"/>.
        /// </summary>
        public bool On(string section) => !Blocked && Sections.On(section);
        /// <summary>Why a section is off (plain words, translated), or null when it is on.</summary>
        public string WhyOff(string section) => Blocked ? BlockedReason : Sections.WhyOff(section);
        /// <summary>Current value of a setting (0 for unknown keys, logged once).</summary>
        public float Get(string key) => SettingsReg.Get(key);
        /// <summary>Current value of a Bool setting.</summary>
        public bool GetBool(string key) => SettingsReg.Get(key) >= 0.5f;
        /// <summary>Current value of an Int, Enum or Silver setting, rounded.</summary>
        public int GetInt(string key) => (int)Math.Round(SettingsReg.Get(key));
        /// <summary>Current value of an Enum setting.</summary>
        public T GetEnum<T>(string key) where T : struct => (T)Enum.ToObject(typeof(T), GetInt(key));
        /// <summary>Sets a setting in <see cref="Current"/> (clamped to its hard limits). Change callbacks follow, debounced.</summary>
        public void Set(string key, float value) => SettingsReg.Set(key, value);
        /// <summary>Sets a Bool setting in <see cref="Current"/>.</summary>
        public void Set(string key, bool value) => SettingsReg.Set(key, value ? 1f : 0f);
        /// <summary>Resets a setting in <see cref="Current"/> to its default.</summary>
        public void Reset(string key) => SettingsReg.Reset(key);

        /// <summary>Text value of a free key in <see cref="Current"/> (per save when the mod is per-save), or null. See <see cref="KitProfile.GetText"/>.</summary>
        public string GetText(string key) => Current.GetText(key);
        /// <summary>Sets a text value in <see cref="Current"/> (null removes it). OnSettingsChanged(key) follows.</summary>
        public void SetText(string key, string value) => Current.SetText(key, value);
        /// <summary>List value of a free key in <see cref="Current"/> (a copy; empty when unset). See <see cref="KitProfile.GetList"/>.</summary>
        public IReadOnlyList<string> GetList(string key) => Current.GetList(key);
        /// <summary>Sets a list value in <see cref="Current"/> (null removes it). OnSettingsChanged(key) follows.</summary>
        public void SetList(string key, IEnumerable<string> values) => Current.SetList(key, values);

        // Presets and compatibility switches ------------------------------------------------------------------------------
        /// <summary>The first preset that matches <see cref="Current"/> (applying it would change nothing, or its HighlightWhen says so), or null ("Custom").</summary>
        public KitPreset MatchingPreset() => Presets.Matching(Current);
        /// <summary>Applies the preset <paramref name="id"/> to <see cref="Current"/> (changes only what it names; callbacks follow). False for an unknown id.</summary>
        public bool ApplyPreset(string id) => Presets.Apply(Presets.Find(id), Current);

        /// <summary>
        /// Is the integration with <paramref name="packageIdOrDlc"/> on? True when that mod or DLC is active and, for an entry marked
        /// <see cref="KitCompatEntry.Switchable"/>, the player did not switch it off on the Compatibility page. Without a compat entry
        /// for that packageId: whether the mod is active. Cheap; ask it in the integration's patches.
        /// </summary>
        public bool CompatOn(string packageIdOrDlc)
        {
            if (Blocked || !KitCompat.IsModActive(packageIdOrDlc)) return false;
            var e = Compat.Find(packageIdOrDlc);
            return e == null || e.SwitchedOn;
        }

        /// <summary>Switches a switchable integration (template, written with the settings file). False when there is no such switchable entry.</summary>
        internal bool SetCompat(string packageIdOrDlc, bool on)
        {
            var e = Compat.Find(packageIdOrDlc);
            if (e == null || !e.CanSwitch) return false;
            bool was = e.SwitchedOn;
            if (on) Template.Store.Remove(e.SwitchKey); else Template.Store.Set(e.SwitchKey, 0f);
            if (was != on) { MarkChanged(e.SwitchKey); Log.Info("Compat", e.Name + " integration switched " + (on ? "on" : "off")); }
            return true;
        }

        /// <summary>Prefix of the compat switch keys in the template store and of their change keys.</summary>
        internal const string CompatKeyPrefix = "compat:";

        // Templates and window ---------------------------------------------------------------------------------------------
        /// <summary>Copies the save's switches and values into the template (global values stay) and writes the settings file.</summary>
        public void SaveAsTemplate()
        {
            var sp = SaveProfile; if (sp == null) return;
            Template.CopySettingsFrom(sp);
            WriteSettings();
            Log.Info("Core", "This save's settings saved as the template for new games");
        }

        /// <summary>Copies the template's switches and values into the running save.</summary>
        public void ApplyTemplateToSave()
        {
            var sp = SaveProfile; if (sp == null) return;
            sp.CopySettingsFrom(Template);
            Log.Info("Core", "Template applied to this save");
        }

        /// <summary>Writes the mod's settings file now. Never throws.</summary>
        public void WriteSettings()
        {
            if (Blocked) return;
            try { Settings?.Write(); }
            catch (Exception e) { Log.Warn("Core", "Could not write the settings file: " + e.Message); }
        }

        /// <summary>
        /// Opens the mod's settings window, optionally on a page ("overview", "general", "section:&lt;id&gt;", "page:&lt;id&gt;",
        /// "compat", "diagnosis", "about"). Never throws.
        /// </summary>
        public void OpenSettings() => OpenSettings(null);

        /// <summary>Opens the mod's settings window on a page ("overview", "general", "section:&lt;id&gt;", "page:&lt;id&gt;", "compat", "diagnosis", "about"). Never throws.</summary>
        public void OpenSettings(string pageId)
        {
            try
            {
                if (pageId != null) Window.SelectPage(this, pageId);
                if (Mod != null) Find.WindowStack.Add(new RimWorld.Dialog_ModSettings(Mod));
            }
            catch (Exception e) { Log.Warn("Core", "Could not open the mod settings: " + e.Message); }
        }

        // Error fences -----------------------------------------------------------------------------------------------------
        /// <summary>
        /// Runs <paramref name="action"/> inside the mod's error fence: an exception is logged once per site (full stack), counted and
        /// swallowed. Three failures in a row at one site pause the section for this session (<see cref="On"/> turns false, the player
        /// gets a letter and a Resume button). Does nothing while the section is paused. <see cref="Core"/> is never paused.
        /// </summary>
        public void Run(string section, string site, Action action) => Guard.Run(section, site, action);
        /// <summary>Like <see cref="Run(string, string, Action)"/> for a function; returns <paramref name="fallback"/> on error or while paused.</summary>
        public T Run<T>(string section, string site, Func<T> func, T fallback) => Guard.Run(section, site, func, fallback);
        /// <summary>Books a failure without running anything (for catch blocks with their own fallback logic).</summary>
        public void Fail(string section, string site, Exception e) => Guard.Fail(section, site, e);
        /// <summary>True when the section paused itself after repeated errors.</summary>
        public bool IsPaused(string section) => Guard.IsPaused(section);
        /// <summary>Un-pauses a section and forgets its failure counters.</summary>
        public void Resume(string section) => Guard.Resume(section);
        /// <summary>
        /// Runs a section's "Prepare for removal" on the running game, as the button does but without the dialogs (tests, own UI): its
        /// <see cref="KitSection.WithRemoval"/> steps, then the section is switched off in <see cref="Current"/>. Returns the report lines
        /// (empty without a game or for an unknown section).
        /// </summary>
        public IReadOnlyList<string> PrepareRemoval(string sectionId)
        {
            if (Blocked || Verse.Current.Game == null || Sections.Find(sectionId) == null) return new List<string>();
            return Sections.PrepareRemoval(sectionId);
        }

        /// <summary>
        /// Call from a caretaker check (<see cref="KitRegistry.Caretaker"/>) for every repair: counted (Diagnosis page and report) and
        /// logged at Info level (the mod's own log file; the game log only with "Detailed log"). Routine repairs never fill the log with warnings.
        /// </summary>
        public void CaretakerFixed(string checkName, string what) => Caretaker.Fixed(checkName, what);

        // Reports ----------------------------------------------------------------------------------------------------------
        /// <summary>"Type: reason" for every Harmony patch class of the mod that could not be applied.</summary>
        public IReadOnlyList<string> PatchFailures => Patches.Failures.ToList();
        /// <summary>The mod's diagnosis report (versions, load folders, sections, patches, compatibility, errors), no personal paths.</summary>
        public string DiagnosisReport() => KitReport.Text(this);

        /// <summary>The mod's name.</summary>
        public override string ToString() => Name + " (" + PackageId + ")";

        // internal ---------------------------------------------------------------------------------------------------------
        /// <summary>The welcome-card pitch in the game's language (key &lt;Prefix&gt;.FirstStartPitch), else <see cref="KitInfo.FirstStartPitch"/>; "" when none.</summary>
        internal string PitchText => string.IsNullOrEmpty(Cfg.FirstStartPitch) ? "" : Key("FirstStartPitch").TranslateOr(Cfg.FirstStartPitch);

        /// <summary>Translation key of the mod's own registrations: Key("Setting.x") → "LO.Setting.x".</summary>
        internal string Key(string suffix) => Cfg.KeyPrefix + "." + suffix;

        /// <summary>The context that is drawing a window right now (KitStyle.Accent and icons use it), or null.</summary>
        [ThreadStatic] internal static KitMod Drawing;

        // change notification (debounced) ------------------------------------------------------------------------------------
        readonly List<string> pending = new List<string>();
        int suppress;
        /// <summary>True while presets are compared on a scratch profile: no notifications, no writes to global values.</summary>
        internal bool Comparing => suppress > 0;

        internal void SuppressNotifications(Action a)
        {
            suppress++;
            try { a(); } finally { suppress--; }
        }

        /// <summary>Remembers that a key ("lo.regrowDays", "section:&lt;id&gt;", "profile:loaded") changed. Delivered by <see cref="FlushChanges"/>.</summary>
        internal void MarkChanged(string key)
        {
            if (suppress > 0 || key == null) return;
            lock (pending) if (!pending.Contains(key)) pending.Add(key);
        }

        internal bool HasPendingChanges => pending.Count > 0;

        /// <summary>
        /// Delivers pending changes: the setting's OnChanged, then the mod's OnSettingsChanged(key), each fenced. Called once per frame
        /// by this mod's settings window, on window close and by the framework's game component.
        /// </summary>
        internal void FlushChanges()
        {
            if (pending.Count == 0) return;
            string[] keys;
            lock (pending) { keys = pending.ToArray(); pending.Clear(); }
            foreach (var k in keys)
            {
                var s = SettingsReg.Find(k);
                if (s?.OnChanged != null)
                {
                    var setting = s;
                    Guard.Run(s.SectionId, "OnChanged." + k, () => setting.OnChanged(SettingsReg.Value(setting, Current)));
                }
                if (Mod != null && !Blocked) Guard.Run(Core, "OnSettingsChanged", () => Mod.CallOnSettingsChanged(k));
            }
        }

        // boot -----------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// Order: context → log header → API check → Register(r) → settings file (incl. legacy migration) → Harmony patch classes one by one.
        /// A blocked mod (framework too old) stops after the API check: nothing registered, no settings written, no patches.
        /// </summary>
        internal static KitMod Boot(KitModBase mod, ModContentPack content, KitInfo info)
        {
            var k = new KitMod(mod, content, info);
            if (mod != null) mod.Kit = k;   // before Register: the mod's Kit property is usable inside Register
            Framework.Add(k);
            try { k.BootInner(); }
            catch (Exception e) { k.Log.Error("Core", "Start-up failed; the framework features of this mod may be incomplete", e); }
            return k;
        }

        void BootInner()
        {
            Log.SessionStart();
            Log.Info("Core", $"Loading {Name} {Version} from {Content?.PackageId} ({Content?.FolderName}) with {Framework.Name} {Framework.Version} (API level {Framework.ApiLevel})");

            var asm = Mod?.GetType().Assembly;
            int stamped = 0;
            if (IsFramework) BuiltWithFramework = Framework.Version;
            else { stamped = Framework.RequiredLevel(asm, out string builtWith); BuiltWithFramework = builtWith; }
            RequiredApiLevel = Math.Max(stamped, Cfg.MinApiLevel);
            if (RequiredApiLevel > Framework.ApiLevel)
            {
                Blocked = true;
                BlockedReason = Framework.TooOldText(Name, RequiredApiLevel, BuiltWithFramework);
                lock (Framework.TooOldMessages) Framework.TooOldMessages.Add(BlockedReason);
                Log.Warn("Core", BlockedReason);
                return;
            }

            foreach (var c in clashes) Log.Warn("Core", c);
            if (!IsFramework) SelfCheck();

            try { Mod?.CallRegister(new KitRegistry(this)); }
            catch (Exception e) { Log.Error("Core", "Register() threw; what was registered so far stays", e); }
            LoadSettingsObject();
            if (IsFramework) Framework.RefreshGlobals();

            var asms = Content?.assemblies?.loadedAssemblies?.Where(a => a != null && (IsFramework || a != typeof(Framework).Assembly)).ToList()
                       ?? new List<Assembly>();
            if (asms.Count == 0 && asm != null) asms.Add(asm);
            try { Patches.ApplyAll(asms, Cfg.HarmonyId); }
            catch (Exception e) { Log.Error("Core", "Patch application failed", e); }

            if ((!string.IsNullOrEmpty(Cfg.FirstStartPitch) || Card != null) && !Cfg.PerSave)
                Log.Warn("Core", "A welcome card (FirstStartPitch or WelcomeCard()) is set but PerSave is off: the card needs a per-save profile and is not shown.");
            KitHooks.Each(KitHooks.ModBooted, this, "ModBooted", h => h(this));
        }

        static string SafeName(Assembly a) { try { return a.GetName().Name; } catch { return ""; } }

        /// <summary>Set-up mistakes a developer can make, each reported once in plain words (never blocks the mod).</summary>
        void SelfCheck()
        {
            try
            {
                var fwName = typeof(Framework).Assembly.GetName().Name;
                if (Content?.assemblies?.loadedAssemblies != null && Content.assemblies.loadedAssemblies.Any(a => a == typeof(Framework).Assembly || SafeName(a) == fwName))
                    Log.Error("Core", "This mod ships its own copy of " + fwName + ".dll. Remove it from the mod's Assemblies folder (reference it with Private=false, as build/JBMod.props does): a copy that loads first replaces the real framework for every mod.");
                var deps = Content?.ModMetaData?.Dependencies;
                if (deps != null && !deps.Any(d => d != null && KitCompat.Same(Regex.Replace(d.packageId ?? "", @"\.slot\d+$", ""), Framework.PackageId)))
                    Log.Warn("Core", "About.xml does not list " + Framework.Name + " (" + Framework.PackageId + ") under modDependencies: players without it get no download prompt. See the framework's README.");
                var asm = Mod?.GetType().Assembly;
                if (asm != null)
                {
                    var settings = KitPatches.SafeTypes(asm).Where(x => typeof(KitSettingsBase).IsAssignableFrom(x) && !x.IsAbstract).ToList();
                    if (settings.Count > 1)
                        Log.Error("Core", "More than one KitSettingsBase subclass in " + asm.GetName().Name + " (" + string.Join(", ", settings.Select(x => x.FullName)) + "): the framework uses " + settings[0].FullName + ". Keep exactly one.");
                    var mods = KitPatches.SafeTypes(asm).Where(x => typeof(KitModBase).IsAssignableFrom(x) && !x.IsAbstract).ToList();
                    if (mods.Count > 1)
                        Log.Error("Core", "More than one KitModBase subclass in " + asm.GetName().Name + " (" + string.Join(", ", mods.Select(x => x.FullName)) + "): RimWorld creates each of them. Keep exactly one.");
                }
            }
            catch (Exception e) { Log.Warn("Core", "Self-check failed: " + e.Message); }
        }

        void LoadSettingsObject()
        {
            if (Mod == null) return;
            Type t = null;
            try
            {
                t = KitPatches.SafeTypes(Mod.GetType().Assembly).FirstOrDefault(x => typeof(KitSettingsBase).IsAssignableFrom(x) && !x.IsAbstract);
                if (t == null)
                {
                    Log.Warn("Core", "No KitSettingsBase subclass in " + Mod.GetType().Assembly.GetName().Name + ": settings are not saved. Declare 'public sealed class <Name>Settings : JBFramework.KitSettingsBase { }' (keep the OLD settings class name when migrating).");
                    return;
                }
                var m = typeof(Verse.Mod).GetMethod(nameof(Verse.Mod.GetSettings)).MakeGenericMethod(t);
                var st = (KitSettingsBase)m.Invoke(Mod, null);
                if (st != null) { st.Owner = this; st.Template.Owner = this; }
                Settings = st;
            }
            catch (Exception e) { Log.Error("Core", "Loading the settings (" + t?.Name + ") failed; defaults are used", e.InnerException ?? e); }
        }
    }
}

# JB.Kit 1.0 API (src/Core)

Everything here is in namespace `JBKit`, compiled into each mod's own DLL. All types are `internal` except the three bases
`KitModBase`, `KitSettingsBase`, `KitGameBase` (`public abstract`, internal constructors/hooks). src/UI (K3) and src/Testing (K4)
may rely on every member listed here; anything not listed is an implementation detail. Statics are per mod assembly.

## Rules for src/UI and src/Testing

- Namespace `JBKit` (Testing may use `JBKit.Testing`), every type `internal`, except bases a mod must derive public classes from
  (e.g. `public abstract class KitTestRunnerBase : GameComponent` with an internal constructor/hooks, like `KitGameBase`).
- Nothing the game resolves by name (XML `Class=`, defs, comps, `DefModExtension`, concrete GameComponents/ModSettings).
- Player-facing text through `KitText.K("<Area>.<Key>", "English fallback", args…)` → key `<Prefix>.Kit.<Area>.<Key>`; add every key to
  `Languages/English/JBKit.xml` **and** `Languages/German/JBKit.xml` (tags `<{P}.Kit.…>`). Texture paths `UI/<Prefix>/Kit/…`, from
  `Kit.Info.Theme`; a missing texture draws nothing (vanilla fallback icons such as `TexButton`).
- Install yourself in a partial method of `KitBoot` (called from the KitModBase constructor, after `Register` and settings load):
  ```csharp
  internal static partial class KitBoot { static partial void InstallUI() { KitHooks.DrawSettings = KitSettingsWindow.Draw; … } }
  internal static partial class KitBoot { static partial void InstallTesting() { … } }   // file under #if JB_TESTS
  ```
- Kit `[HarmonyPatch]` classes (window size getter, ctor resizeable) are applied automatically by `KitPatches` like the mod's own;
  compare `dialogMod == Kit.Mod` and early-out for every other mod.
- Wrap draws in `KitGuard.Run(KitSections.Core, "UI.<Page>", …)`; never throw out of a page.

## KitInfo (sealed partial class) — given by the mod
| Member | Meaning |
|---|---|
| `string Name, ShortId, KeyPrefix, Version` | player name; file/channel id (`<ShortId>.log`); key/texture prefix; mod version. Defaults filled by `Kit.Init` (ShortId from Name, KeyPrefix = ShortId). |
| `bool PerSave` | values + section switches per save (needs a KitGameBase subclass) |
| `bool UseGate` | reserved (src/Gate, K6) |
| `string WorkshopId, Url, LicenseLine` | About page (optional) |
| `string HarmonyId` | default: `Content.PackageId` |
| `string FirstStartPitch` | non-null = first-start card wanted (UI draws it; registers `kit.firstStartCard`) |
| `KitTheme Theme` | `Color Accent`, `Color AccentSoft` (35 % alpha), `string MarkTex`, `Dictionary<string,string> Icons` (keys `overview`, `general`, `compat`, `diagnosis`, `about`, `section:<id>`, `page:<id>`) |
| `const string KitVersion, KitCommit, KitTier; const bool KitTests` | generated (obj/1.7/JBKitInfo.g.cs) |

## Kit (static) — front door
| Member | Meaning |
|---|---|
| `KitInfo Info`, `KitModBase Mod`, `KitSettingsBase Settings` | set in the KitModBase constructor (`Settings` null if the mod has no settings class) |
| `KitProfile Template` | the settings-file profile (global values always live here) |
| `KitProfile SaveProfile` | the running game's profile when PerSave, else null |
| `KitProfile Current` | `SaveProfile ?? Template`; all reads go here |
| `bool HasSaveProfile` | show "This save / Template" chips |
| `bool On(string section)` | = `KitSections.On` |
| `float Get(key)`, `bool GetBool(key)`, `int GetInt(key)`, `T GetEnum<T>(key)`, `int GetSilver(key)` | current values |
| `void Set(key, float)`, `Set(key, bool)`, `Reset(key)` | against Current |
| `void SaveAsTemplate()`, `void ApplyTemplateToSave()` | sidebar buttons (no-ops without a save profile) |
| `void OpenSettings(string pageId = null)` | opens Dialog_ModSettings, selects a page via `KitHooks.SelectPage` |
| `void FlushChanges()`, `bool HasPendingChanges`, `void MarkChanged(key)` (internal) | deferred change delivery (README decision 8) |
| `void WriteSettingsSafe()` | writes the settings file, never throws |

## KitHooks (static delegates) — filled by UI/Testing
| Hook | Called |
|---|---|
| `Action<Rect> DrawSettings` | by `KitModBase.DoSettingsWindowContents` (null → `KitFallbackWindow.Draw`); `Kit.FlushChanges()` runs right after |
| `Action<string> SelectPage` | by `Kit.OpenSettings(pageId)`. Page ids: `overview`, `general`, `section:<id>`, `page:<id>`, `compat`, `diagnosis`, `about` |
| `Action WindowClosed` | in `KitModBase.WriteSettings` (window close), before flush + file write |
| `Action DefsLoaded` | once, after the mod's `OnDefsLoaded` (StaticConstructorOnStartup) |
| `Action<bool,bool> GameInit` | (isNewGame, joinedExistingSave) after the profile is in place (needs the mod's KitGameBase) |
| `Action GameOnGUI` | `KitGameBase.GameComponentOnGUI` (first-start card) |
| `Func<IEnumerable<string>> ExtraReportLines` | appended to `KitReport.Lines()` |

## KitModBase (public abstract : Verse.Mod)
Constructor `internal KitModBase(ModContentPack content, KitInfo info)`. Hooks (override with `internal override`):
`abstract void Register(KitRegistry r)`, `virtual void OnDefsLoaded()`, `virtual void OnSettingsChanged(string key)` (keys: setting key,
`section:<id>`, `profile:loaded`), `virtual void OnGameStarted()`, `virtual void OnGameLoaded(bool joinedExistingSave)`,
`virtual IEnumerable<string> Diagnosis()`. Overrides (public): `SettingsCategory()` = `Info.Name`, `DoSettingsWindowContents`, `WriteSettings`.

## KitSettingsBase (public abstract : ModSettings)
`public sealed override ExposeData()`; hook `internal virtual void ExposeExtra()`. Members: `KitProfile Template`, `int KitDataVersion`,
`List<string> Migrated` ("key=value (from old)"), `bool MigratedThisSession`, `static string SettingsFilePath` (absolute; never print it).

## KitGameBase (public abstract : GameComponent)
Constructor `protected KitGameBase(Game game)`. Hooks: `internal virtual void ExposeExtra()`, `internal virtual void Tick(int ticksGame)`.
State: `KitProfile Profile`, `bool IsNewGame`, `bool JoinedExistingSave` (first-start card variant). Statics: `KitGameBase Instance`
(this mod's component of the running game, else null), `KitProfile CurrentProfile`. All GameComponent overrides are sealed; it runs the
caretaker, flushes changes in `GameComponentUpdate`, calls `KitHooks.GameOnGUI`.

## KitRegistry (passed to Register)
| Method | Returns |
|---|---|
| `Section(id, label, desc=null, icon=null, defaultOn=true, available=null, requires=null, order=0, onSwitched=null, diagnosis=null, prepareRemoval=null, legacyEnabled=null)` | KitSection |
| `SectionFor(packageId, modName, id, label, desc=null, icon=null, defaultOn=true, order=0)` | KitSection available only with that mod/DLC |
| `Bool(section, cat, key, def, label, desc=null, nerd=false, legacyScribe=null)` | KitSetting |
| `Int(section, cat, key, def, min, max, label, desc=null, nerd=false, hardMin=0, hardMax=1e6, legacyScribe=null)` | KitSetting |
| `Float(…, float def, float min, float max, …, hardMin=0, hardMax=1e6, legacyScribe=null)`, `Percent(…, hardMax=1)`, `LogFloat(…)` | KitSetting |
| `Enum<T>(section, cat, key, T def, label, desc=null, nerd=false, legacyScribe=null)` | KitSetting |
| `Silver(section, cat, key, int def, int min, int max, label, desc=null, nerd=false, legacyScribe=null)` | KitSetting (hard max 10 × silver stack limit) |
| `Preset(id, label, desc, Action<KitProfile> apply, Func<KitProfile,bool> matches=null)` | KitPreset |
| `Compat(name, packageIdOrDlc, what, Func<bool> healthy=null)`, `Incompatible(name, packageId, why)` | KitCompatEntry |
| `Page(id, label, icon=null, order=0, Action<Rect> draw=null, section=null)` | KitPage (custom page; null without draw) |
| `Caretaker(section, name, Func<int> check)` | void |

## KitSetting (class) + KitSettingKind { Bool, Int, Float, Percent, LogFloat, Enum, Silver }
Fields: `Key, Section, Category, Kind, Default, SliderMin, SliderMax, HardMin, HardMax, DynamicHardMax, HardLimitReason, Nerd, Global,
EnumType, LabelEn, DescEn, Step, Order, LegacyKey, LegacyToValue, LegacyScribe, OnChanged (Action<float>), Restart, Requires, RequiresText`.
Computed: `Label`, `Desc`, `CategoryLabel` (translated, keys `<P>.Setting.<Key>[.Desc]`, `<P>.SettingCat.<CatKey>`), `static CatLabel(cat)`,
`static CatKey(cat)`, `EffectiveHardMax`, `IsLog`, `SliderLo/SliderHi`, `ToSlider(v)`, `FromSlider(x)` (log sliders), `Clamp(v)`,
`Format(v)` (On/Off, %, silver, enum names `<P>.Enum.<Type>.<Name>`), `RequiresMet`, `RequiresReason`, `StoredIn(store)`, `IsStoredIn(store)`,
`TryParseLegacy(raw, out v)`. Fluent: `AsGlobal() AsNerd() NeedsRestart() WithStep(s) WithLimit(reason, dynMax) Legacy(old) LegacyStoreKey(old, convert) OnChange(a) RequiresOn(idOrPackage, reason)`.

## KitSettings (static)
`List<KitSetting> All` (registration order), `Add(s)`, `Find(key)`, `OfSection(id)`;
against an explicit profile (the window edits save or template): `StoreFor(s, profile)`, `Value(s, profile)`, `IsOverridden(s, profile)`,
`Set(s, profile, v)` (clamps, marks changed), `Reset(s, profile)` (also clears free-text flag), `ResetSection(id, profile)`;
against Current: `Get(key|s)`, `GetBool`, `GetInt`, `GetEnum<T>`, `GetSilver`, `Set(key, v)`, `Reset(key)`; tests: `ClearForTests()`.

## KitStore (IExposable)
`TryGet(k, out v)`, `Has(k)`, `Set(k, v)`, `Remove(k)`, `Count`, `Keys`, `Clone()`, `Dictionary<string,bool> freeText` (free-entry rows).

## KitProfile (IExposable)
`Dictionary<string,bool> sections`, `KitStore Store`, `int ProfileVersion`, `bool FirstStartCardDone`, `const CurrentVersion`.
`SectionOn(id)` (stored or default), `SectionStored(id)`, `SetSection(id, on)` (callbacks when current), `ResetSection(id)`,
`Get(key)`, `GetBool(key)`, `Set(key, float|bool)`, `Reset(key)`, `CloneForNewGame()`, `CopySettingsFrom(src)` (keeps own globals).

## KitSection / KitSections
KitSection: `Id, LabelEn, DescEn, Icon, DefaultOn, AvailableFunc, RequiresText, Order, OnSwitched, Diagnosis, PrepareRemoval, LegacyEnabled`;
`Label`, `Desc`, `RequiresLabel` (keys `<P>.Section.<id>`, `.Desc`, `.Requires`).
KitSections: `const string Core = "core"`, `List<KitSection> All` (by Order), `Add`, `Find(id)`, `Available(id)`, `On(id)`, `WhyOff(id)`
(null when on), `LabelOf(id)` (core → mod name), `PrepareRemoval(id)` → report lines (switches the section off), `ClearForTests()`.

## KitPreset / KitPresets
KitPreset: `Id, LabelEn, DescEn, Apply, Matches, Label, Desc` (keys `<P>.Preset.<id>[.Desc]`).
KitPresets: `All`, `Add`, `Find(id)`, `Changes(p, profile)` (keys incl. `section:<id>` it would change), `IsMatch(p, profile)`,
`Matching(profile)` (first match or null = "Custom"), `WouldOverwrite(p, profile)` (player-changed rows → ask first), `Apply(p, profile)`.

## KitCompat / KitCompatEntry / [KitCompat(packageId)]
Entry: `Name, PackageId, WhatEn, Incompatible, Healthy, Failures, PatchesApplied, What` (key `<P>.Compat.<CatKey(Name)>.What|.Why`),
`State` (KitCompatState: NotInstalled, Active, Failed, Unhealthy, IncompatiblePresent, IncompatibleAbsent), `StateLabel`.
KitCompat: `All`, `Dlcs` ((Id, Name)[5]), `Add`, `Find(packageId)`, `IsModActive(packageId)` (any case, `_steam` tolerant, never throws),
`ActiveDlcNames()`.

## KitPatches (static)
`List<string> Failures` ("Type: reason"), `List<string> SkippedInactive`, `int Applied`, `object HarmonyInstance`, `bool Done`,
`SharedTargets()` ("Type.Method: other owners" for the diagnosis page), `SafeTypes(asm)`.

## KitLog (static) + KitLogLevel { Debug, Info, Warn, Error }
`Debug/Info/Warn(channel, msg)`, `Error(channel, msg, Exception e = null)`, `Once(key, level, channel, msg)`, `bool Verbose`,
`int ErrorCount, WarnCount`, `string[] RecentSnapshot()` (last 300 lines), `string FileName` (print this), `string FilePath` (never print),
`string Prefix` ("[Name] "), `SessionStart()`.

## KitGuard / KitCaretaker (static)
KitGuard: `Run(section, site, Action)`, `T Run<T>(section, site, Func<T>, fallback)`, `Fail(section, site, e)`, `IsPaused(id)`, `Paused`,
`ReasonFor(id)`, `Resume(id)`, `int TotalFailures`, `const MaxConsecutive = 3`, `ResetAll()` (tests).
KitCaretaker: `List<Check> Checks` (`Name, Section, Run, TotalFixes, LastFixes, Runs`), `TotalFixes`, `Register(section, name, run)`,
`Fixed(checkName, what)`, `RunAll(force=false)`.

## KitPages / KitPage
KitPage: `Id, LabelEn, Icon, Order, Section, Draw (Action<Rect>), Label` (key `<P>.Page.<id>`). KitPages: `All` (by Order), `Find`, `Add`.

## KitText (static)
`TranslateOr(this string key, string fallback)`, `Prefix`, `Key(suffix)` → `<P>.<suffix>`, `K(sub, fallbackEn)`, `K(sub, fallbackEn, params object[])`
(string.Format on the translated text, falls back on bad formats).

## KitReport (static)
`List<string> LoadFolders()` (relative to the mod root), `List<string> Lines()`, `string Text()`: versions, DLCs, mod count, folders,
sections with WhyOff and section diagnosis, Harmony applied/skipped/shared targets, compat states, guard pauses, caretaker, mod lines,
`KitHooks.ExtraReportLines`. No absolute paths.

## Built-in settings (KitBuiltins, section core, category "Interface", all global)
`kit.windowScale` (Percent, 1.0, slider 0.8–1.5, hard 0.6–2), `kit.verboseLog`, `kit.showNerd`, `kit.firstStartCard` (only with a pitch).
Constants `KitBuiltins.WindowScaleKey, VerboseLogKey, ShowNerdKey, FirstStartCardKey, Category`.

## KitFallbackWindow (static)
`Draw(Rect)` — tier core only (used when `KitHooks.DrawSettings` is null).

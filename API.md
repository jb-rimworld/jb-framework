# JB Framework API (API level 1, framework 1.0.0)

Namespace `JBFramework`, assembly `JBFramework.dll` (identity: `build/Identity.props`). Part 1 is the public contract for mod authors.
Part 2 is the internal architecture and the contract for the people (and agents) who extend the framework: what exists, where to hook in,
which files belong to whom. The snapshot `api/PublicApi.txt` is the machine-checked list of every public line; this file explains it.

---

## Part 1: public API (for mods)

### Compatibility promise
- Additive only. A public type or member is never removed, renamed or changed (signature, parameter names, defaults). New behaviour =
  a new member, a new overload or a new optional registration. `tools/public_api.py check` fails on any removed/changed line and on
  added lines without a raised `JBFrameworkApiLevel`.
- `AssemblyVersion` stays `1.0.0.0`, so a mod built against framework 1.0 binds to every later framework DLL.
- `Framework.ApiLevel` (int) rises with every release that adds public API. build/JBMod.props stamps `[assembly: KitRequires(level, version)]`
  into each dependent mod (the level it was built against; override `-p:JBMinApiLevel=N`). `KitInfo.MinApiLevel` can raise it further.
  When the installed framework is older, the framework reads the attribute without running the mod's code, keeps that mod **blocked**
  (`KitMod.Blocked`: no registration, no settings written, no patches, every section off) and tells the player in plain words
  (startup dialog, Mod settings page, log). A mod whose constructor itself fails on a missing member is still reported by the startup scan.

### Types at a glance
| Type | Kind | What |
|---|---|---|
| `Framework` | static | `ApiLevel`, `Version`, `Name`, `PackageId`, `Mods` (dependent mods), `Find(packageId)`, `Of<TMod>()`, `IsModActive(id)`, `DiagnosisReport()` (combined, no personal paths) |
| `KitModBase` | public abstract : `Verse.Mod` | base of a mod's Mod class; `Kit` (its `KitMod`); protected hooks `Register`, `OnDefsLoaded`, `OnSettingsChanged(key)`, `OnGameStarted`, `OnGameLoaded(joined)`, `OnTick(ticks)`, `Diagnosis()`; `SettingsCategory()` overridable; window drawing and `WriteSettings` sealed |
| `KitMod` | sealed | one mod's context (below) |
| `KitInfo`, `KitTheme` | sealed, fields | what the mod tells the framework (name, ids, version, per save, links, pitch, MinApiLevel, theme: accent, mark, icons) |
| `KitRegistry` | sealed | passed to `Register`: sections, settings, presets, compat entries, pages, caretaker checks, `SaveData` |
| `KitSetting`, `KitSection`, `KitPreset`, `KitPage`, `KitCompatEntry` | sealed | registration results; read-only ids/labels; `KitSetting` has fluent modifiers |
| `KitProfile` | sealed : `IExposable` | one mod's values for one save or the template; presets receive it |
| `KitSettingsBase` | public abstract : `ModSettings` | base of the mod's settings class (keep the old class name); `ExposeExtra()` for own fields; `Kit` |
| `KitLog`, `KitLogLevel` | sealed / enum | a mod's log (`KitMod.Log`) |
| `KitCompatAttribute` | attribute | `[KitCompat("packageId")]` on a soft-integration `[HarmonyPatch]` class |
| `KitRequiresAttribute` | assembly attribute | stamped by JBMod.props (API level, framework version) |
| `KitUI` | static | `Size(w, h, minW, minH, maxFracW, maxFracH)`, `Scale` (screen × global "Window size") for a mod's own windows |
| `KitStyle` | static | colours (`Accent` = drawing mod's accent, `AccentSoft`, `Grey`, `DimGrey`, `Good`, `Bad`, `Line`) and helpers `Heading`, `Section`, `LabelColored`, `Paragraph`, `Chip`, `Banner`, `Bar` |
| `FrameworkMod`, `FrameworkSettings`, `FrameworkGame` | sealed | the framework's own Mod class, settings file and the one GameComponent. Created by RimWorld; their names are in settings files and saves, so they are public and never renamed. Mods do not use them. |

### KitMod (one per mod)
Reach it as `Kit` inside the Mod class, `Framework.Of<MyMod>()` in patches (cache it in a static after the first non-null), or
`Framework.Find("my.packageid")`. Reads go against `Current` = `SaveProfile ?? Template`.

| Member | Meaning |
|---|---|
| `PackageId`, `Name`, `Version`, `Info`, `Mod`, `Content`, `Log`, `Settings` | identity and parts (`PackageId` as loaded, lower case; test slots add `.slotN`) |
| `Blocked`, `BlockedReason`, `RequiredApiLevel`, `BuiltWithFramework` | API-level check result |
| `On(section)`, `WhyOff(section)` | section active right now (available, not paused, switched on); `KitMod.Core` = "core" is always on |
| `Get(key)`, `GetBool`, `GetInt`, `GetEnum<T>`, `Set(key, float/bool)`, `Reset(key)` | values in `Current` (clamped; changes delivered debounced) |
| `Template`, `SaveProfile`, `Current`, `HasSaveProfile` | profiles |
| `SaveAsTemplate()`, `ApplyTemplateToSave()`, `WriteSettings()`, `OpenSettings(pageId)` | page ids: `overview`, `general`, `section:<id>`, `page:<id>`, `compat`, `diagnosis`, `about` |
| `Run(section, site, Action)`, `Run<T>(section, site, Func<T>, fallback)`, `Fail(section, site, e)`, `IsPaused`, `Resume` | the mod's error fence: logged once per site, 3 in a row pause the section (letter + Resume button); core never pauses |
| `CaretakerFixed(check, what)` | count and log a repair from a caretaker check |
| `PatchFailures`, `DiagnosisReport()` | report |

### KitRegistry (inside `Register`)
- `Section(id, label, desc, icon, defaultOn, available, requires, order, onSwitched, diagnosis, prepareRemoval, legacyEnabled)`,
  `SectionFor(packageId, modName, id, label, …)`.
- Settings (section id first, then category = group header): `Bool`, `Int`, `Float`, `Percent` (0..1), `LogFloat`, `Enum<T>` (dropdown),
  `Silver` (hard max 10 × silver stack limit). Optional `nerd`, hard limits for free entry, `legacyScribe` (old field name, migrated once).
  Fluent on the result: `AsGlobal()` (installation preference), `AsNerd()`, `NeedsRestart()`, `WithStep(s)`, `WithLimit(reason, dynamicMax)`,
  `Legacy(old)`, `LegacyStoreKey(oldKey, convert)`, `OnChange(Action<float>)`, `RequiresOn(sectionOrPackageId, reason)`.
- `Preset(id, label, desc, Action<KitProfile> apply, Func<KitProfile,bool> matches = null)`: changes only what it sets.
- `Compat(name, packageIdOrDlc, what, healthy)`, `Incompatible(name, packageId, why)`.
- `Page(id, label, icon, order, Action<Rect> draw, section)`: order below 0 lists it before General.
- `Caretaker(section, name, Func<int> check)`: once after every load, then round robin every 250 ticks while the section is on.
- `SaveData(Action exposeData, Action reset)`: the mod's own per-save data inside the framework's game component, node of the mod's
  packageId; `reset` runs when a new Game object is created (new game, or before a load). Skipped silently when the mod is absent.

### Translation keys of a mod's registrations (prefix = `KitInfo.KeyPrefix`)
`<P>.Section.<id>`, `.Desc`, `.Requires` · `<P>.Setting.<key>`, `.Desc` · `<P>.SettingCat.<CategoryLettersAndDigits>` · `<P>.Preset.<id>`, `.Desc` ·
`<P>.Page.<id>` · `<P>.Compat.<NameLettersAndDigits>.What` / `.Why` · `<P>.Enum.<EnumType>.<Name>`. English text in code is the fallback.
The framework's own texts (`JBF.*`) exist once in `Common/Languages`; mods never copy them.

### Change keys for `OnSettingsChanged`
Setting keys (`"lo.regrowDays"`), `"section:<id>"`, and `"profile:loaded"` once after a game starts or loads. Debounced: delivered once per
key per frame (settings window), on window close and from `FrameworkGame.GameComponentUpdate`.

### Settings file and saves
- Settings file `Config/Mod_<folder>_<ModClassName>.xml` (vanilla rule), settings class = the mod's `KitSettingsBase` subclass found by
  reflection (exactly one per assembly). Layout: `<kit><dataVersion/><template>KitProfile</template></kit>` then `ExposeExtra`.
- Legacy migration: on load, each `legacyScribe`/`legacyEnabled` field the template lacks is read as a string, parsed (bool, invariant
  number, enum name), clamped, stored; the old file is copied once to `*.xml.pre-kit`; the file is rewritten without legacy nodes at startup.
- Save: one `JBFramework.FrameworkGame` component, `<mods><li><id>packageid</id><profile>…</profile><data>…SaveData…</data></li></mods>`.
  The key is the packageId without `_steam` and without `.slotN`. A removed mod's profile stays in the save (plain values, framework types
  only) and comes back when the mod returns; its `<data>` node is dropped at the next save (its types are never touched).
- "Joined existing save": `OnGameLoaded(true)` when the save had no entry for the mod (or no framework component at all).

### Global preferences (framework settings, one for all mods)
Window size (`KitUI.Scale`), detailed log (`KitLog` writes Info/Debug to the game log), nerd settings, welcome cards. Shown on the
framework's General page; every mod's General page links there.

---

## Part 2: architecture and contract for contributors

### Shape
```
Framework (static)                 list of contexts (snapshot arrays, no allocation per tick), identity, global prefs, too-old messages
 └─ KitMod (per mod)               created by KitModBase ctor → KitMod.Boot: context → Framework.Add → log header → API check →
     ├─ KitSettingRegistry           Register(r) → settings file (KitSettingsBase, legacy migration) → KitPatches.ApplyAll → KitHooks.ModBooted
     ├─ KitSectionRegistry, KitPresetRegistry, KitCompatRegistry, KitPageRegistry
     ├─ KitGuard, KitCaretaker, KitPatches, KitLog
     └─ KitWindowState (UI state of its settings window), SaveExpose/SaveReset
FrameworkMod : KitModBase          the framework's own context (Framework.Self): global prefs + "Mods" page (UI/FrameworkPages.cs)
FrameworkGame : GameComponent      per-save profiles + SaveData, ticks (caretaker, OnTick), change flush, welcome cards — each mod fenced
KitStartup [StaticConstructorOnStartup]  OnDefsLoaded per mod, migration rewrite, KitHooks.DefsLoaded, API-level scan + dialog
KitWindowPatches                   Dialog_ModSettings bigger/resizable for every KitModBase (applied once, by the framework)
```
Everything except Part 1 is `internal`. `[assembly: InternalsVisibleTo("JBFramework.Testing")]` is generated, so the test assembly may use
internals; nobody else may rely on them.

### Internal hook points (`Source/JBFramework/Core/KitHooks.cs`) — use these instead of editing Core/UI
| Hook | When | Signature |
|---|---|---|
| `KitHooks.ModBooted` | after each (non-blocked) mod's boot | `Action<KitMod>` (mods booted before you subscribed are in `Framework.AllContexts()`) |
| `KitHooks.DefsLoaded` | once, after all mods' OnDefsLoaded | `Action` |
| `KitHooks.ReportLines` | building a mod's diagnosis report | `Func<KitMod, IEnumerable<string>>` |
| `KitHooks.CompatPage` | drawing a mod's Compatibility page, after integrations/conflicts | `Func<KitMod, float width, float y, float newY>` |
All are fenced per mod (`KitHooks.Each`). Subscribe from a static constructor of your part or a `[StaticConstructorOnStartup]` class.
If you need a new hook, add it to KitHooks (one line) rather than editing a drawing method.

### Useful internals
`KitMod.SettingsReg` (`All`, `Find`, `Value(s, profile)`, `Set(s, profile, v)`, `Reset`, `StoreFor`, `IsOverridden`), `KitMod.Sections`
(`All`, `Find`, `Available`, `On`, `WhyOff`, `LabelOf`, `PrepareRemoval`), `KitMod.Presets` (`All`, `Matching`, `Changes`, `Apply`),
`KitMod.Compat` (`All`, `Find`, `ReportFailure`), `KitMod.Pages`, `KitMod.Guard` (`TotalFailures`, `Paused`, `ReasonFor`, `ResetAll`),
`KitMod.Caretaker` (`Checks`, `RunAll`), `KitMod.Patches` (`Failures`, `SkippedInactive`, `Applied`, `SharedTargets()`),
`KitMod.Log.RecentSnapshot()`, `KitMod.MarkChanged/FlushChanges`, `KitMod.Window` (`PageId`, `Search`, `EditTemplate`, `Last` layout),
`KitProfile.CloneForNewGame/CopySettingsFrom`, `FrameworkGame.Instance`, `FrameworkGame.SaveKey(k)`, `KitCompat.IsModActive/Strip/Dlcs`,
`KitReport.Text(k)/Lines(k)/Combined()/LoadFolders(k)`, `KitText.K(sub, english, args)` (framework text `JBF.<sub>`),
`KitUI.TestScreen`, `KitRow.ForceDrawAll`, `KitUiProbe` (hit counters "settings", "page:<id>", "diagnosis", "report", "card",
"framework:mods"), `KitFirstStartCard.Suppressed/OpenNow(k)/Due()`, `KitDiagnosis.LastRemovalReport`, `KitSettingsWindow.BuildPages(k, target)`.

### Ownership (who edits what)
| Files | Owner |
|---|---|
| `build/*`, `Source/JBFramework/JBFramework.csproj`, `Source/JBFramework/Core/*`, `Source/JBFramework/UI/*`, `api/*`, `tools/public_api.py`, `tools/PublicApi/`, `tools/api_scan.sh`, `tools/ApiScan/`, `tools/art/`, `sample/*`, `About`, `LoadFolders.xml`, `README.md`, this file | conversion (done). Others change them only through a hook, a one-line hook addition in KitHooks, or a clearly scoped fix. |
| `Common/Languages/*/Keyed/JBFramework.xml` | shared: append your keys in your own `JBF.<YourArea>.*` block, English and German both. Or add your own file next to it (`JBFramework_Compat.xml`). |
| new: `Source/JBFramework/Compat/` (detection helpers, `ModNoteDef`, KB queries, adapters), `Common/Defs/ModNotes/*.xml`, `Source/JBFramework/Shared/` (dropdown-to-material designator, one-architect-entry helper, tier gizmo base, safe def mutation with restore, removal preparation), `Source/JBFramework/Random/` (JRand from H&H) | **agent 2: compat knowledge base and shared functions** |
| `src/Testing/` → new project `Source/JBFramework.Testing/` (assembly `JBFramework.Testing`), a local unpublished test mod folder (suggested `testmod/`), `tools/jbmod`, `tools/probe/`, the `JB_TESTS` reference in `build/JBMod.props` | **agent 3: harness split and jbmod** |

### Agent 2 (compat knowledge base, shared functions, random): where to hook in
- Public types the game resolves by name (a `ModNoteDef : Def`, comps, place workers) must be `public` and live in namespace `JBFramework`;
  their full names are then fixed forever. Everything else stays `internal` until it is meant as API.
- New public API: add members/types, raise `JBFrameworkApiLevel` to 2 in `build/Identity.props`, rebuild both versions, run
  `python3 -I tools/public_api.py check` (must say "added"), then `generate` and commit `api/PublicApi.txt`. Every public member needs an
  XML doc comment (CS1591 is an error-free warning gate: keep 0 warnings).
- Compat page: draw the knowledge-base part through `KitHooks.CompatPage` (e.g. "Known mods in your list" for the mod's tags). Report lines
  through `KitHooks.ReportLines`. Per-mod compat registrations still go through `KitRegistry.Compat/Incompatible`; if mods need to state tags
  ("this mod changes the bills UI"), add a new `KitRegistry` method (additive) that stores into a new internal registry hanging on
  `KitMod` (add the field in `KitMod`; that one-line edit is allowed).
- Shared functions that need Harmony patches: put the `[HarmonyPatch]` classes in the framework assembly (applied once by `KitPatches` of the
  framework's own context, Harmony id = framework packageId) and make them early-out unless a mod opted in through a public registration.
- Must compile for 1.6 too (`-p:RimVersion=1.6`, use `#if RW17` shims) and pass `tools/api_scan.sh 1.6`.

### Agent 3 (harness and jbmod): what changed under you
`src/Testing` is **not compiled** any more; it still uses the old static kit. Port it into `Source/JBFramework.Testing/` (own csproj importing
`build/JBMod.props`-like settings or JBCommon.props, referencing `JBFramework.dll` with Private=false; `InternalsVisibleTo` already
grants internals). Mapping old → new:

| Old (source-shared kit) | New |
|---|---|
| `namespace JBKit` / `JBKit.Testing` | `JBFramework` / `JBFramework.Testing` |
| `Kit.Info`, `Kit.Mod`, `Kit.Settings`, `Kit.Template/SaveProfile/Current`, `Kit.Get/Set/On` | `k.Info`, `k.Mod`, `k.Settings`, `k.Template/…`, `k.Get/Set/On` for the mod under test `k` |
| `KitSettings.All/Find/Value/Set` | `k.SettingsReg.All/Find/Value/Set`; `KitSetting.Section` → `SectionId` |
| `KitSections.*`, `KitPresets.*`, `KitCompat.All`, `KitPages.*` | `k.Sections.*`, `k.Presets.*`, `k.Compat.All`, `k.Pages.*` |
| `KitGuard.*`, `KitCaretaker.*`, `KitPatches.*`, `KitLog.*` (static) | `k.Guard.*`, `k.Caretaker.*`, `k.Patches.*`, `k.Log.*` |
| `KitGameBase` (per-mod GameComponent), `KitGameBase.Instance/CurrentProfile` | `FrameworkGame.Instance`, `k.SaveProfile`; per-save mod data via `KitRegistry.SaveData` |
| `KitBoot.InstallTesting()` partial, `KitHooks.ExtraReportLines` | your own `[StaticConstructorOnStartup]` + `KitHooks.ModBooted`, `KitHooks.ReportLines` |
| `KitBuiltins.*` (per-mod kit.windowScale, kit.verboseLog, kit.showNerd, kit.firstStartCard) | gone; global prefs `Framework.WindowScaleKey` etc. on `Framework.Self` |
| `KitSettingsWindow.Last/SelectedPage/Search/EditTemplate`, `KitSettingsWindow.Target` | `k.Window.Last/PageId/Search/EditTemplate`, `KitSettingsWindow.Target(k)` |
| `KitIcons.Mark/Page(id)/AllPaths()`, `KitDiagnosis.Draw(w)/BuildText()` | `KitIcons.Mark(k)/Page(k, id)/AllPaths(k)`, `KitDiagnosis.Draw(k, w)/BuildText(k)` |
| `KitFirstStartCard.OpenNow()/Due()` (bool) | `KitFirstStartCard.OpenNow(k)`, `Due()` returns the `KitMod` whose card is due |
| `<P>.Kit.*` keys stamped per mod | `JBF.*` once in the framework |
| per-mod `KitTestRunnerBase : GameComponent` | suggestion: ONE runner GameComponent in the test assembly that picks the mod under test by `JB_TEST_MOD` (`Framework.Find`) and finds its suites by reflection over that mod's assembly (types deriving from `JBFramework.Testing.KitTestSuite`, compiled into the mod only with `JB_TESTS`) |

`build/JBMod.props` with `-p:JBTests=true` only defines `JB_TESTS`; add there the reference to `JBFramework.Testing.dll` (Private=false)
for test builds. `sample/KitSampleB/Source/SampleB.cs` keeps its `CounterSuite` under `#if JB_TESTS`, already ported to the new API except the
harness types themselves. jbmod must now also put the framework into the slot profile's ModsConfig (`juliusberger.jbframework`, loaded from
this folder or a slot copy) before the mod under test; the sample About files depend on it.

### Decisions taken in the conversion (simplest option)
1. Type names keep the `Kit` prefix (`KitMod`, `KitModBase`, …) in namespace `JBFramework`: short, already in every doc and sample.
2. Hooks are `protected` virtuals on `KitModBase`; the framework calls them through internal bridges, each fenced.
3. The settings object is found by reflection (one `KitSettingsBase` subclass per mod assembly); during the first load its context is
   found by assembly (`Framework.ForAssembly`), because RimWorld sets `ModSettings.Mod` only after reading the file.
4. Welcome cards: one global switch ("Show welcome cards") instead of one per mod; the card's "never again" box sets it for all JB mods.
5. The framework itself is a context (`Framework.Self`) and reuses the settings window: no Overview (no sections), a "Mods" page first.
6. Absent mods' profiles are kept forever (simplest silent option of FRAMEWORK.md 2.4); their SaveData nodes are dropped on the next save.
7. Name, packageId, assembly name, namespace, version and API level live in `build/Identity.props`. The namespace is necessarily also
   written in every `.cs` file; change it with a search-and-replace before the first upload (no tool yet).

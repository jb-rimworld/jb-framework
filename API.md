# JB Framework API (API level 2, framework 1.0.0)

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

### Compatibility knowledge base (API level 2)
What the framework knows about other mods, shipped as data and read by every mod that uses it. Updating the framework updates the knowledge.
- **`ModNoteDef : Def`** (`Common/Defs/ModNotes/*.xml`, German via `DefInjected/JBFramework.ModNoteDef`): `label`, `description`,
  `packageIds` (every upload/fork, exact case from About.xml), `tags`, `versions`, `workshopIds`, `harmonyIds`, `pitfall`, `advice`,
  `source` (not shown); `Active`, `ActivePackageId`, `HasTag(tag)`. Any mod may add its own notes in XML. The seed (60 notes) is built by
  `python3 -I tools/modnotes/build.py` from `tools/modnotes/notes.py`; it refuses a packageId that no real About.xml or
  `Rimling/ModKnowledge` file contains.
- **`KitTags`**: the tag strings the notes use (`alien-races`, `changes-bills-ui`, `changes-bills`, `adds-pipe-networks`,
  `combat-overhaul`, `replaces-construction`, `changes-quality`, `changes-storage`, `changes-medical`, `guests`, `robots`, `editor`,
  `performance`, `early-patcher`, `multiplayer`, `ai`, `library`, `changes-architect`, `changes-work`, `adds-buildings`, `economy`,
  `psycasts`, `genes`, `changes-pawn-ui`, `jb-mod`). Plain strings: own tags are allowed.
- **`KitKnown`** (static, after defs are loaded): `All`, `Note(packageId)`, `WithTag(tag)`, `Active()`, `ActiveWithTag(tag)`,
  `AnyActiveWithTag(tag)`, `Declare(k, tags…)` (the tags a mod cares about; call in Register or OnDefsLoaded), `DeclaredTags(k)`,
  `RelevantFor(k)`, `OwnerName(harmonyId)`.
- **Compatibility pages**: every mod's page shows, after its own integrations, "Known mods in your game" (active notes matching its
  declared tags, with pitfall and advice) and "Other mods changing the same game code" (Harmony co-owners of the methods it patches, by
  name). The framework's own page lists every active known mod. The same appears in the diagnosis reports.
- **`KitDetect`** (static, cached, never throws): `Royalty`, `Ideology`, `Biotech`, `Anomaly`, `Odyssey` and their `…Id` constants,
  `ActiveDlcNames`, `IsActive(id)`, `AnyActive(ids…)`, `FirstActive(ids…)`, `ActiveName(id)`, `ActivePackageIds`, `Normalize(id)`,
  `Same(a, b)`, `Refresh()`. Every id check ignores case and the Workshop `_steam` postfix.

### Adapters (API level 2)
One guarded class per other mod; reflection only, resolved once, a failure is one warning in the framework log (channel `Compat`) and a
safe default. Adding a mod = a ModNoteDef plus, at most, one class in `Source/JBFramework/Compat/Adapters/`.
| Class | What | Verified against |
|---|---|---|
| `KitCE` | `Active`, `PackageIds`, `AmmoSetOf(weapon)`, `AmmoTypesFor(weapon)`, `DefaultAmmoFor(weapon)`, `IsAmmo(def)` | CombatExtended.dll 1.6 (`ammoSet`, `ammoTypes`, `ammo`), as DNDM's CEAmmo |
| `KitHAR` | `Active`, `IsAlienRace(race)`, `IsAlien(pawn)`, `IsCustomBodied(race/pawn)` (humanlike, body ≠ Human), `HumanlikeRaces()` | AlienRace.dll 1.6 (`AlienRace.ThingDef_AlienRace`), LO's race modes |
| `KitNiceBillTab` | `Active`, `RequestRefresh()` (also the copy bundled in Nice Inventory Tab) | NiceBillTab.dll 1.6 (`TabBillsDrawer.shouldRefreshFilter`), BWD's fix |
| `KitHearth` | `HearthHarbor`, `Silverline`, `Any`, id constants | About.xml files |
| `KitVEPipes` | `Active`, `NetworksOf(def)`, `IsPipeDef(def)`, `IsOnPipeNet(thing)` (read-only) | VEF PipeSystem.dll **1.7** (`CompResource`, `CompProperties_Resource.pipeNet`) |
No Dubs Bad Hygiene adapter: no 1.7 build of it was available to check against (its note exists).

### Shared functions (API level 2)
- **`KitMaterialMenu`**: `AfterDropdownPick(k, applies, offerAllMaterials)` opens the material menu after a pick from any architect
  dropdown for the stuffed defs `applies` accepts (vanilla's dropdown never does). One framework patch on
  `Designator_Dropdown.GetDesignatorSelectAction`, however many mods register; no-op for other defs. `offerAllMaterials: false` = vanilla's
  menu (materials on the map), `true` = `Open(des)`: materials on the map first, the rest marked "none on the map". Replaces DNDM's
  `DNDMMaterialMenu` and HBG2's `Patch_DropdownStuffMenu` (remove those patches when a mod switches over, or the menu opens twice).
- **`KitArchitect`**: `OneEntryPerFamily(k, def → familyKey)` merges a family's buildables (and vanilla dropdown groups of it) into one
  dropdown per tab, sorted by uiOrder; keys are per mod. Postfix on `DesignationCategoryDef.ResolveDesignators` + a startup pass;
  registering later regroups at once. `RegroupAll()`.
- **`KitTiers`**: `Families(k, family, tier, allowed, afterSwap)` or `Family(k, id, defNames…)`; members get the "Change tier" gizmo
  (`CompProperties_KitTierSwap`/`CompKitTierSwap`/`Command_KitTierSwap`, save nothing). `Swap(thing, toDef)` keeps faction, material, HP
  ratio, paint, forbidden, storage settings, bills, battery charge, selection and `IKitTierState` comps. `SiblingsOf`, `TierOf`,
  `SiblingAt`, `Roman`. Register in Register/OnDefsLoaded: comps added later reach only things spawned afterwards.
- **`KitDefEdits.For(k)`**: reversible def edits. `Set(def, "field.path", value, group)`, `SetStat(def, stat, value, group)`,
  `SetConstructionSkill(def, skill, group)` (also the blueprint/frame copies), `Restore(group)` / `Restore()` / `RestoreDef(def)`,
  `IsEdited`, `Count`. The first edit records the original (lists copied, stat presence remembered).
- **`KitRemoval`**: building blocks for a section's `prepareRemoval`: `RemoveThings(defs, report, refund)` (maps, minified, blueprints,
  frames, inventories, equipment, apparel), `RemoveHediffs`, `RemoveBills`, `RemoveDesignations`; `AddStep(k, sectionId, step)` adds a step
  to an existing section. Each step writes one report line and never throws.

### Random streams (API level 2)
Deterministic, save-stable random numbers (from Hearth & Harbor's JRand), for any mod:
- **`KitRandom.Stream(k, name)`**: the named stream of the running save. Same name = same sequence after a reload and on every machine
  (multiplayer-safe), streams are independent, names are namespaced per mod (`<packageId>/<name>`). State: seed from the world seed,
  position of every used stream, stored in `FrameworkGame` (`<framework><random>`); a removed mod leaves a few numbers, no types.
  `Stream(globalName)` for code without a KitMod (prefix your packageId). `Vanilla()` = throw-away stream fed from Verse.Rand (re-rolls
  like vanilla). `SaveSeed`, `Persistent`. Without a game: a non-persistent clock-seeded stream.
- **`KitRandomStream`** (pure, no game types; `new KitRandomStream(seed, name or id, counter, algorithm)`): `NextULong`, `NextDouble`,
  `NextFloat`, `Chance(p)`, `Range(min, maxExcl)`, `RangeInclusive`, `Range(double, double)`, `Gaussian`, `Poisson`, `Shuffle(list)`
  (Fisher-Yates), `Pick(list)`, `WeightedIndex(weights)`, `PickWeighted(items, weight)`, `Clone`, `Counter` (settable), `Reseed`, static
  `Mix`, `HashString` (FNV-1a). `KitRandomAlgorithm`: SplitMix64 (default), Pcg32, Xoshiro256, ChaCha8 (values bit-identical to H&H's
  RngStream, checked for 1000 values each).
- **`KitShuffleBag`** (`IExposable`): every index 0…Count-1 once per round in shuffled order; `Next(stream)`, `Resize`, `Remaining`.
  Save it in your own `SaveData`.

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
| `KitHooks.GameExpose` | every Scribe pass of `FrameworkGame`, inside node `<framework>` (framework-wide per-save data, e.g. random streams) | `Action` |
| `KitHooks.GameReset` | a new `FrameworkGame` was created (new game or before a load; `Current.Game` is not the new game yet) | `Action` |
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
| `TestMod/` (local test mod, `JBFramework.Testing.dll`; agent 2 owns `TestMod/Source/Suites/`), `tools/jbmod`, `tools/probe/`, the `JB_TESTS` reference in `build/JBMod.props` | **agent 3: harness and jbmod (done)** |

### Agent 2 (compat knowledge base, shared functions, random): where to hook in
Status (API level 2): done in `Source/JBFramework/{Compat,Compat/Adapters,Shared,Random}/`, `Common/Defs/ModNotes/`,
`Common/Languages/*/Keyed/JBFramework_Compat.xml`, `Common/Languages/German (Deutsch)/DefInjected/JBFramework.ModNoteDef/`,
`tools/modnotes/`, suites `TestMod/Source/Suites/FwPartsSuites.cs` (`fw.known`, `fw.adapters`, `fw.random`, `fw.shared` (explicit only)).
Edits outside those paths: two hooks in `KitHooks` (`GameExpose`, `GameReset`) and their two call sites in `FrameworkGame` (ctor, ExposeData),
`JBFrameworkApiLevel` 1 → 2 in `build/Identity.props`, `api/PublicApi.txt` regenerated. Startup of these parts: `Shared/KitPartsStartup.cs`
(its order relative to `KitStartup` is undefined, so registrations after it are applied at once).
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

### Test harness (JBFramework.Testing) and tools/jbmod
The harness is its own assembly `JBFramework.Testing.dll` in the **local test mod** `TestMod/` (packageId `juliusberger.jbframework.testing`,
csproj `TestMod/Source/JBFramework.Testing.csproj`, RimWorld 1.7 only, output `TestMod/1.7/Assemblies/` git-ignored). It is never published
and never shipped: `tools/jbmod` puts it into test runs only, `tools/release.py` refuses it and every harness string in a release DLL.
- **Mod suites**: a dependent mod writes `sealed class X : JBFramework.Testing.KitTestSuite` under `#if JB_TESTS`; `-p:JBTests=true`
  (build/JBMod.props) defines `JB_TESTS` and references the harness DLL (Private=false). `TestContext t` gives `t.Mod` (the `KitMod`
  under test), `t.Check/Note`, `t.Map`, `t.Center`, `t.FreeCellNearCenter()`, `TestCells.*`. Selection `JB_TEST=all | a,b | prefix.* |
  section:<id>`; `ExplicitOnly` suites run only when named or by a non-`kit.` prefix (`fw.*`).
- **One runner** (`KitTestRunner : GameComponent`, `TestingMod : Mod` starts the red-error recorder before the mods under test): the mod
  under test is `JB_TEST_MOD` (packageId, slot suffix ignored; the framework's own id tests the framework itself); suites come from the
  harness and from that mod's assemblies. Result file `JB_TEST_OUT`, `RESULT PASS` only when every check passed, no log error of the
  framework or the mod, and no red error naming either (expected errors a suite provokes carry `[JBTEST-EXPECTED]`/`jbtest_expected`).
  Runner components are left out of every save the harness makes.
- **Built-in suites per tenant** (`TestMod/Source/KitSuites.cs`, `KitWindowSuite.cs`): `kit.smoke`, `kit.settings`, `kit.window` (settings
  window at 1280x720, 1920x1080, 2560x1440 via `KitUI.TestScreen`; every page in "this save" and "template", a search; fits the screen,
  keeps its minimum, parts inside the window and not overlapping, nothing threw; `JB_UI_SHOTS` leaves `kit_window_<id>_<size>_<page>.png`),
  `kit.defs`, `kit.migration`, `kit.saveload` (two phases, fingerprint of the mod's and the framework's save state), `kit.addition`
  (explicit), `kit.settingsdump`.
- **Framework suites** (`TestMod/Source/FrameworkSuites.cs`, explicit): `fw.tenants` (A and B together: separate contexts, no value leaks,
  both windows open and drawn at once, a throwing page of B is fenced on B while A keeps drawing, both per-save profiles and B's SaveData
  survive save/load), `fw.removal` (save with A+B, load with A only: zero red errors of the framework, B's profile kept, its data node
  dropped at the next save), `fw.apilevel` (A built with `-p:JBMinApiLevel=99`: blocked with the plain-words message, registers nothing,
  writes nothing, B unaffected), `fw.alone` (no tenant: one Mod settings entry, no red error). Agent 2's `fw.known/adapters/random/shared`
  live in `TestMod/Source/Suites/`.
- **jbmod**: every run gets a slot copy of the framework (`<fw>.slotN`; dependents' About files are pointed at it, so the player's Mods
  folder never holds a framework), the harness when suites run, extra mods (packageIds, or local folders built and copied like the mod
  under test) and the mod under test; the probe loads first. Commands: `build` (1.7, then 1.6 compile-only), `test`, `plain`, `saveload`,
  `removal` (the framework stays), `addition` (vanilla save without mod and framework), `compat`, `migrate-test`, `lang`, `release`,
  `desc-lint`, `freeze16`, and `fw tenants|removal|apilevel|alone|all`. `release.py` adds for the framework the 1.6 build,
  `public_api.py check` and a copy without `sample/ build/ api/ TestMod/ API.md`; for dependents "no `JBFramework.dll`, no type defined in
  the `JBFramework` namespace" (tools/PublicApi `--types`).

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

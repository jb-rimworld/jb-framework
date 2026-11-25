# JB Framework API (API level 1, framework 1.0.0)

Namespace `JBFramework`, assembly `JBFramework.dll` (identity: `build/Identity.props`). This is the public contract for mod authors;
the quick start is in [README.md](README.md), the internals for contributors in [docs/CONTRIBUTING.md](docs/CONTRIBUTING.md). The snapshot
`api/PublicApi.txt` is the machine-checked list of every public line (the 1.0 baseline); this file explains it.

---

### Compatibility promise
- Additive only. A public type or member is never removed, renamed or changed (signature, parameter names). New behaviour = a new
  member, a new overload or a new chained modifier. Public methods have **no optional parameters** (a default would be compiled into
  every caller and make a later overload ambiguous). `tools/public_api.py check` fails on any removed/changed line, on an optional
  parameter and on added lines without a raised `JBFrameworkApiLevel`.
- `AssemblyVersion` stays `1.0.0.0`, so a mod built against framework 1.0 binds to every later framework DLL.
- `Framework.ApiLevel` (int) rises with every release that adds public API (1 = the first release; `api/Since.txt` records the level
  of each later line). build/JBMod.props stamps `[assembly: KitRequires(level, version)]` into each dependent mod: level 1 by default,
  raise it with `-p:JBMinApiLevel=N` / `<JBMinApiLevel>` when the mod uses later members (`tools/public_api.py uses <mod.dll>` says
  which; `tools/release.py` fails a lower stamp). `KitInfo.MinApiLevel` can raise it further.
  When the installed framework is older, the framework reads the attribute without running the mod's code, keeps that mod **blocked**
  (`KitMod.Blocked`: no registration, no settings written, no patches, every section off) and tells the player in plain words
  (startup dialog, Mod settings page, log). A mod whose constructor itself fails on a missing member is still reported by the startup scan.

### Types at a glance
| Type | Kind | What |
|---|---|---|
| `Framework` | static | `ApiLevel`, `Version`, `Name`, `PackageId`, `Mods` (dependent mods), `Find(packageId)`, `Of<TMod>()`, `IsModActive(id)`, `DiagnosisReport()` (combined, no personal paths) |
| `KitModBase` | public abstract : `Verse.Mod` | base of a mod's Mod class; `Kit` (its `KitMod`); protected hooks `Register`, `OnDefsLoaded`, `OnSettingsChanged(key)`, `OnSettingsWindowClosed()`, `OnGameStarted`, `OnGameLoaded(joined)`, `OnTick(ticks)`, `Diagnosis()`; `SettingsCategory()` overridable; window drawing and `WriteSettings` sealed |
| `KitMod` | sealed | one mod's context (below) |
| `KitInfo`, `KitTheme` | sealed, properties | what the mod tells the framework (name, ids, version, per save, links, pitch, MinApiLevel, theme: accent, mark, icons); the framework keeps a copy |
| `KitRegistry` | sealed | passed to `Register`: sections, settings, presets, compat entries, pages, caretaker checks, `SaveData`, `MapData`, `LegacyComponent`, `WelcomeCard` |
| `KitSetting`, `KitSection`, `KitPreset`, `KitPage`, `KitCompatEntry`, `KitCard` | sealed | registration results; read-only ids/labels and chained modifiers |
| `KitProfile` | sealed : `IExposable` | one mod's values for one save or the template; presets receive it |
| `KitSettingsBase` | public abstract : `ModSettings` | base of the mod's settings class (keep the old class name); `ExposeExtra()` for own fields; `Kit` |
| `KitLog`, `KitLogLevel` | sealed / enum | a mod's log (`KitMod.Log`) |
| `KitCompatAttribute` | attribute | `[KitCompat("packageId")]` on a soft-integration `[HarmonyPatch]` class |
| `KitTransientAttribute` | attribute | `[KitTransient("why")]` on a cache field of a saved object: the in-game save test does not compare it (no runtime effect) |
| `KitRequiresAttribute` | assembly attribute | stamped by JBMod.props (API level, framework version) |
| `KitUI` | static | `Size(w, h)`, `Size(w, h, minW, minH, maxFracW, maxFracH)`, `Scale` (screen × global "Window size") for a mod's own windows; text measuring `TextH(text, w)` / `TextH(text, w, font)` (cached), `TextW(text)` / `TextW(text, font)`, `Cols(rect, gap, fractions…)` |
| `KitStyle` | static | colours (`Accent` = drawing mod's accent, `AccentSoft`, `Grey`, `DimGrey`, `Good`, `Bad`, `Line`), `AccentOf(k)`, `Using(k)` (an `IDisposable` scope: draw as that mod in its own windows) and helpers `Heading`, `Section`, `LabelColored`, `Paragraph`, `Chip`, `Banner`, `Bar` |
| `FrameworkMod`, `FrameworkSettings`, `FrameworkGame`, `FrameworkMap` | sealed | the framework's own Mod class, settings file, the one GameComponent and the one MapComponent. Created by RimWorld; their names are in settings files and saves, so they are public and never renamed. Mods do not use them. |

### KitMod (one per mod)
Reach it as `Kit` inside the Mod class (already set while `Register` runs; `r.Kit` is the same), `Framework.Of<MyMod>()` in patches
(null only before RimWorld created the mod class), or `Framework.Find("my.packageid")`. `Info` returns a copy of the mod's `KitInfo`. Reads go against `Current` = `SaveProfile ?? Template`.

| Member | Meaning |
|---|---|
| `PackageId`, `Name`, `Version`, `Info`, `Mod`, `Content`, `Log`, `Settings` | identity and parts (`PackageId` as loaded, lower case; test slots add `.slotN`, which every id check of the framework ignores) |
| `Blocked`, `BlockedReason`, `RequiredApiLevel`, `BuiltWithFramework` | API-level check result |
| `On(section)`, `WhyOff(section)` | section active right now (available, not paused, switched on); `KitMod.Core` = "core" is always on |
| `Get(key)`, `GetBool`, `GetInt`, `GetEnum<T>`, `Set(key, float/bool)`, `Reset(key)` | values in `Current` (clamped; changes delivered debounced) |
| `GetText(key)`, `SetText(key, value)`, `GetList(key)`, `SetList(key, values)` | free text and string-list values in `Current` (per save like the numbers; free keys "<shortid>.<name>", no registration; null removes) |
| `MatchingPreset()`, `ApplyPreset(id)` | the preset that matches `Current` (null = "Custom"); apply one by id |
| `CompatOn(packageIdOrDlc)` | the integration is on: that mod/DLC is active and a `Switchable()` entry was not switched off by the player |
| `PrepareRemoval(section)` | runs a section's "Prepare for removal" without dialogs (tests, own UI); returns the report lines |
| `Template`, `SaveProfile`, `Current`, `HasSaveProfile` | profiles |
| `SaveAsTemplate()`, `ApplyTemplateToSave()`, `WriteSettings()`, `OpenSettings()`, `OpenSettings(pageId)` | page ids: `overview`, `general`, `section:<id>`, `page:<id>`, `compat`, `diagnosis`, `about` |
| `Run(section, site, Action)`, `Run<T>(section, site, Func<T>, fallback)`, `Fail(section, site, e)`, `IsPaused`, `Resume` | the mod's error fence: logged once per site, 3 in a row pause the section (letter + Resume button); core never pauses |
| `CaretakerFixed(check, what)` | count a repair from a caretaker check (Diagnosis page and report) and log it at Info level (own log file; game log only with "Detailed log") |
| `PatchFailures`, `DiagnosisReport()` | report |

### KitRegistry (inside `Register`)
Every call takes only the essentials and returns an object; everything optional is a chained modifier on it.
- **Sections**: `Section(id, label)`, `Section(id, label, desc)` → `KitSection`: `Describe(desc)`, `WithIcon(tex)`, `OffByDefault()`,
  `OnlyWith(packageIdOrDlc)` / `OnlyWith(packageIdOrDlc, modName)` (exists only with that mod/DLC, "Needs X." otherwise),
  `AvailableWhen(func, whyNot)`, `WithOrder(n)`, `WhenSwitched(Action<bool>)`, `WithDiagnosis(Func<IEnumerable<string>>)`,
  `WithRemoval(Action<List<string>>)` (prepare-for-removal step; several calls chain), `LegacyEnabled(oldBoolField)`,
  `LegacyDisabled(oldBoolField)` (an old switch with the opposite meaning).
- **Settings** (section id first, then key, default, slider range, label, optional desc): `Bool`, `Int`, `Float`, `Percent` (stored
  0..1), `LogFloat`, `Enum<T>` (dropdown; set it in presets as `(float)(int)value`), `Silver` (typed max 10 × silver stack limit).
  Typed values may go from min(0, slider min) to max(1,000,000, slider max) (Percent: max(1, slider max)). → `KitSetting`:
  `Describe(desc)`, `InGroup(header)`, `HardLimits(min, max)`, `AsGlobal()` (installation preference), `AsNerd()`, `NeedsRestart()`,
  `WithStep(s)`, `WithLimit(reason)` / `WithLimit(reason, dynamicMax)`, `Legacy(oldField)` / `Legacy(oldField, convert)` (old settings
  field, migrated once; convert gets the unclamped old number), `LegacyInverted(oldBoolField)` (True → off), `LegacyStoreKey(oldKey, convert)`,
  `OnChange(Action<float>)`, `OnlyWith(packageIdOrDlc)` (the row is hidden, not greyed, without that mod/DLC),
  `RequiresOn(sectionOrPackageId)` / `RequiresOn(sectionOrPackageId, reason)` (reason key `<P>.Setting.<key>.Requires`),
  `RequiresSetting(boolKey, on)` (greyed "Only while "<label>" is on/off.", judged in the profile the window shows; combines with
  RequiresOn), `WithPreview(Func<KitProfile, string>)` (a computed grey line under the row, also searched; keep it cheap).
  Drawing never writes: a stored value changes only when the player drags a slider, types, clicks or picks; a value a control cannot
  show exactly (0.67 %, 1/3, beyond the slider from an old file) is shown rounded and stays untouched. `WithStep(s)` is the step a DRAG
  rounds to (default: whole numbers for Int/Silver, else three significant digits of the slider span, percent at most whole percents).
  Typed entry: the pencil at the right end of a number row (shown on hover) switches the row to a text field and back (kept per row).
- **Presets**: `Preset(id, label, desc, Action<KitProfile> apply)` (changes only what it sets) → `KitPreset.HighlightWhen(matches)`.
- **Compatibility**: `Compat(name, packageIdOrDlc, what)` → `KitCompatEntry.HealthyWhen(Func<bool>)`, `.HealthyWhenType("Their.Type")`,
  `.HealthyWhenDef<T>(defName)` (checks of the OTHER mod's piece; several checks must all pass), `.Switchable()` (a switch on the
  Compatibility page, a preference of the installation stored in the settings file; ask `KitMod.CompatOn(packageId)` in the integration;
  change key `compat:<packageId>`); `Incompatible(name, packageId, why)`. Tag the integration's Harmony patch classes with `[KitCompat(packageId)]`:
  a class that fails shows "failed: reason"; one that patches nothing (Prepare() false, target missing in the installed version) shows
  "installed, integration inactive: patch target not found" unless the entry has a HealthyWhen check (then the check decides).
- **Pages**: `Page(id, label, Action<Rect> draw)` → `KitPage`: `WithIcon(tex)`, `WithOrder(n)` (below 0: before General),
  `InSection(sectionId)` (hidden while unavailable, dimmed with a switch-on banner while off), `EmbedInSection(height)` (drawn inside its
  section's page under the settings, no own sidebar entry), `AsGlobal()` (says "Applies to every game" instead of the save/template hint). The draw runs inside a UI fence: an
  exception shows an error box with Retry on that page only and never pauses a section.
- `Caretaker(section, name, Func<int> check)`: once after every load, then round robin every 250 ticks while the section is on.
- `SaveData(exposeData)`, `SaveData(exposeData, reset)`: the mod's own per-save data inside the framework's game component, node of the
  mod's packageId; `reset` runs once per Game object (new game, or before a load; never again after a legacy component merged into it). While the mod is absent or blocked its
  node is kept unchanged (raw XML) and comes back with the mod.
- `MapData(Action<Map> exposeData)`, `MapData(exposeData, Action<Map> reset)`: the same per map, inside the framework's map component
  (`FrameworkMap`); references to things and pawns resolve; `reset(map)` runs when the map's component is created (new map or before a
  load: before the map's saved components are read) and when the map is removed; once per Map object. A mod then needs no Game-/World-/MapComponent class of its own.
- `LegacyComponent(Type)`: an OLD component class of the mod that existing saves contain. Keep the class (same name) and let its ExposeData
  read the old fields into SaveData/MapData; the framework never writes it again (stripped while the game, the world and each map write
  their components) and drops its instances once a load or new game finished. Old saves keep loading; new saves name no class of the mod.
- `WelcomeCard()` → `KitCard`: the mod's parts of its welcome card: `WithDraw(Func<Rect, float>)` (an area above the presets; returns the
  height used), `WithAction(id, label, tip, Func<bool> done, Action act)` (buttons under the presets; a done one shows a green chip;
  keys `<P>.Card.<id>`, `.Tip`), `WithoutPresets()`, `WithoutSections()`, `WhenClosed(Action<bool joined>)`. The card stays the framework's:
  once per new colony / once when the mod joins a save (flag in the save's profile), never while `Framework.WelcomeCards` is false.
  A per-save mod gets the card with a `FirstStartPitch` (translated through `<P>.FirstStartPitch`) or with this registration.

### Translation keys of a mod's registrations (prefix = `KitInfo.KeyPrefix`)
`<P>.FirstStartPitch` · `<P>.Section.<id>`, `.Desc`, `.Requires` · `<P>.Setting.<key>`, `.Desc`, `.Requires` · `<P>.SettingCat.<CategoryLettersAndDigits>` ·
`<P>.Preset.<id>`, `.Desc` · `<P>.Page.<id>` · `<P>.Compat.<NameLettersAndDigits>.What` / `.Why` · `<P>.Enum.<EnumType>.<Name>` ·
`<P>.Card.<actionId>`, `.Tip`. English text in code is the fallback; `jbmod <Mod> lang --refresh` writes every key into `<P>_Settings.xml`.
The framework's own texts (`JBF.*`) exist once in `Common/Languages`; mods never copy them.

### Change keys for `OnSettingsChanged`
Setting keys (`"lo.regrowDays"`), free text/list keys, `"section:<id>"`, `"compat:<packageId>"` (a compat switch), and `"profile:loaded"`
once after a game starts or loads. Debounced: delivered once per key per frame (settings window), on window close and from
`FrameworkGame.GameComponentUpdate`. Heavy work that should not run per slider frame goes into `OnSettingsWindowClosed()` (after the
last change of the window was delivered).

### Settings file and saves
- Settings file `Config/Mod_<folder>_<ModClassName>.xml` (vanilla rule), settings class = the mod's `KitSettingsBase` subclass found by
  reflection (exactly one per assembly). Layout: `<kit><dataVersion/><template>KitProfile</template></kit>` then `ExposeExtra`.
- Legacy migration: on load, each `Legacy(...)`/`LegacyEnabled(...)` field the template lacks is read as a string, parsed (bool, invariant
  number, enum name), converted when a conversion is given, clamped, stored (an unreadable value is logged and keeps the default); the old file is copied once to `*.xml.pre-kit`; the file is rewritten without legacy nodes at startup.
- Save: one `JBFramework.FrameworkGame` component, `<mods><li><id>packageid</id><profile>…</profile><data>…SaveData…</data></li></mods>`,
  and per map one `JBFramework.FrameworkMap` component, `<mods><li><id>packageid</id><data>…MapData…</data></li></mods>`.
  The key is the packageId without `_steam` and without `.slotN`. A removed (or blocked) mod's profile stays in the save (plain values,
  framework types only) and its `<data>` node is written back unchanged as raw XML; both come back when the mod returns (its types are
  never touched).
- "Joined existing save": `OnGameLoaded(true)` when the save had no entry for the mod (or no framework component at all).

### Global preferences (framework settings, one for all mods)
Window size (`KitUI.Scale`), detailed log (`KitLog` writes Info/Debug to the game log), nerd settings, welcome cards. Shown on the
framework's General page; every mod's General page links there.

### Compatibility knowledge base 
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
  `Same(a, b)`, `Refresh()`. Every id check ignores case, the Workshop `_steam` postfix and a jbmod test slot's `.slotN` suffix (so
  `OnlyWith`, compat entries and `KitDetect` see local extra mods in test runs).

### Adapters 
One guarded class per other mod; reflection only, resolved once, a failure is one warning in the framework log (channel `Compat`) and a
safe default. Adding a mod = a ModNoteDef plus, at most, one class in `Source/JBFramework/Compat/Adapters/`.
| Class | What | Verified against |
|---|---|---|
| `KitCE` | `Active`, `PackageIds`, `AmmoSetOf(weapon)`, `AmmoTypesFor(weapon)`, `DefaultAmmoFor(weapon)`, `IsAmmo(def)` | CombatExtended.dll 1.6 (`ammoSet`, `ammoTypes`, `ammo`), as DNDM's CEAmmo |
| `KitHAR` | `Active`, `IsAlienRace(race)`, `IsAlien(pawn)`, `IsCustomBodied(race/pawn)` (humanlike, body ≠ Human), `HumanlikeRaces()` | AlienRace.dll 1.6 (`AlienRace.ThingDef_AlienRace`), LO's race modes |
| `KitNiceBillTab` | `Active`, `RequestRefresh()` (also the copy bundled in Nice Inventory Tab); found by its type, so its 1.6 build running on 1.7 as an "incompatible version" mod counts | NiceBillTab.dll 1.6 (`TabBillsDrawer.shouldRefreshFilter`), BWD's fix |
| `KitHearth` | `HearthHarbor`, `Silverline`, `Any`, id constants | About.xml files |
| `KitVEPipes` | `Active`, `NetworksOf(def)`, `IsPipeDef(def)`, `IsOnPipeNet(thing)` (read-only) | VEF PipeSystem.dll **1.7** (`CompResource`, `CompProperties_Resource.pipeNet`) |
No Dubs Bad Hygiene adapter: no 1.7 build of it was available to check against (its note exists).

### Shared functions 
- **`KitMaterialMenu`**: `Active` (the framework's dropdown patch is in place; fall back to flat buttons when false),
  `AfterDropdownPick(k, applies)` / `AfterDropdownPick(k, applies, offerAllMaterials)` opens the material menu after a pick from any architect
  dropdown for the stuffed defs `applies` accepts (vanilla's dropdown never does). One framework patch on
  `Designator_Dropdown.GetDesignatorSelectAction`, however many mods register; no-op for other defs. Default = vanilla's menu
  (materials on the map), `offerAllMaterials` true = `Open(des)`: materials on the map first, the rest marked "none on the map". Replaces DNDM's
  `DNDMMaterialMenu` and HBG2's `Patch_DropdownStuffMenu` (remove those patches when a mod switches over, or the menu opens twice).
- **`KitArchitect`**: `OneEntryPerFamily(k, def → familyKey)` merges a family's buildables (and vanilla dropdown groups of it) into one
  dropdown per tab, sorted by uiOrder; keys are per mod. Postfix on `DesignationCategoryDef.ResolveDesignators` + a startup pass;
  registering later regroups at once. `RegroupAll()`.
- **`KitTiers`**: `Families(k, family, tier)` / `Families(k, family, tier, allowed, afterSwap)` or `Family(k, id, defNames…)`; members get the "Change tier" gizmo
  (`CompProperties_KitTierSwap`/`CompKitTierSwap`/`Command_KitTierSwap`, save nothing). `Swap(thing, toDef)` keeps faction, material, HP
  ratio, paint, forbidden, storage settings, bills, battery charge, selection and `IKitTierState` comps. `SiblingsOf`, `TierOf`,
  `SiblingAt`, `Roman`. Register in Register/OnDefsLoaded: comps added later reach only things spawned afterwards.
- **`KitDefEdits.For(k)`**: reversible def edits. `Set(def, "field.path", value[, group])` (paths take list indices:
  `"stages.0.painFactor"`, `"stages.1.capMods.0.offset"`; a whole list can be replaced with a modified copy), `SetComp<TProps>(thingDef,
  "field", value[, group])` (first comp of that type) with `IsEditedComp<TProps>`, `SetStat(def, stat, value[, group])`,
  `SetConstructionSkill(def, skill[, group])` (also the blueprint/frame copies), `Restore()` / `Restore(group)` / `RestoreDef(def)`,
  `IsEdited`, `Count`. One ledger per field for all mods: the first edit by any mod records the original (lists copied, stat presence
  remembered), the latest edit is live, a restore removes only this mod's edit and falls back to another mod's edit or the original.
- **`KitRemoval`**: building blocks for a section's `WithRemoval` step: `RemoveThings(defs, report)` / `RemoveThings(defs, report, refund)` (maps, minified, blueprints,
  frames, containers, carried things, caravan cargo, inventories, equipment, apparel), `RemoveHediffs`, `RemoveGenes`, `RemoveThoughts` (memories),
  `RemoveBills`, `RemoveDesignations`, `RemoveComponents(Func<Type, bool>, report)`
  (the mod's own Game-/World-/MapComponents out of the running game and out of every save for the rest of the session); `AddStep(k, sectionId, step)` adds a step
  to an existing section. Each step writes one report line and never throws.

### Random streams 
Deterministic, save-stable random numbers (from Hearth & Harbor's JRand), for any mod:
- **`KitRandom.Stream(k, name)`** (or `Stream(k, name, algorithm)`): the named stream of the running save. Valid wherever a game
  exists (world/map generation, OnGameStarted, ticks, UI); fetch it where you draw instead of keeping it in a field (new instances
  after a load). Same name = same sequence after a reload and on every machine
  (multiplayer-safe), streams are independent, names are namespaced per mod (`<packageId>/<name>`). State: seed from the world seed,
  position of every used stream, stored in `FrameworkGame` (`<framework><random>`); a removed mod leaves a few numbers, no types.
  `Stream(globalName)` for code without a KitMod (prefix your packageId). `Vanilla()` = throw-away stream fed from Verse.Rand (re-rolls
  like vanilla). `SaveSeed`, `Persistent`. Without a game: a non-persistent clock-seeded stream.
- **`KitRandomStream`** (pure, no game types; `new KitRandomStream(seed, name or id)` or `(seed, name or id, counter, algorithm)`): `NextULong`, `NextDouble`,
  `NextFloat`, `Chance(p)`, `Range(min, maxExcl)`, `RangeInclusive`, `Range(double, double)`, `Gaussian`, `Poisson`, `Shuffle(list)`
  (Fisher-Yates), `Pick(list)`, `WeightedIndex(weights)`, `PickWeighted(items, weight)`, `Clone`, `Counter` (settable), `Reseed`, static
  `Mix`, `HashString` (FNV-1a). `KitRandomAlgorithm`: SplitMix64 (default), Pcg32, Xoshiro256, ChaCha8 (values bit-identical to H&H's
  RngStream, checked for 1000 values each).
- **`KitShuffleBag`** (`IExposable`): every index 0…Count-1 once per round in shuffled order; `Next(stream)`, `Resize`, `Remaining`.
  Save it in your own `SaveData`.

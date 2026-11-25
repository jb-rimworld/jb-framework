# JB Framework: architecture and contract for contributors

Internal notes (not part of the public promise). Public reference: [../API.md](../API.md); outside modders start at [../README.md](../README.md).

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
Status (part of the 1.0 baseline, API level 1; it was level 2 during development): done in `Source/JBFramework/{Compat,Compat/Adapters,Shared,Random}/`, `Common/Defs/ModNotes/`,
`Common/Languages/*/Keyed/JBFramework_Compat.xml`, `Common/Languages/German (Deutsch)/DefInjected/JBFramework.ModNoteDef/`,
`tools/modnotes/`, suites `TestMod/Source/Suites/FwPartsSuites.cs` (`fw.known`, `fw.adapters`, `fw.random`, `fw.shared` (explicit only)).
Edits outside those paths: two hooks in `KitHooks` (`GameExpose`, `GameReset`) and their two call sites in `FrameworkGame` (ctor, ExposeData),
`api/PublicApi.txt` regenerated (the level was reset to 1 for the first release on 24.11.2026). Startup of these parts: `Shared/KitPartsStartup.cs`
(its order relative to `KitStartup` is undefined, so registrations after it are applied at once).
- Public types the game resolves by name (a `ModNoteDef : Def`, comps, place workers) must be `public` and live in namespace `JBFramework`;
  their full names are then fixed forever. Everything else stays `internal` until it is meant as API.
- New public API: add members/types (overloads or chained modifiers, never optional parameters), raise `JBFrameworkApiLevel` by one in `build/Identity.props`, rebuild both versions, run
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
  under test), `t.Content` (its content pack), `t.Check/Note`, `t.Map`, `t.Center`, `t.FreeCellNearCenter()`, `TestCells.*`; red errors
  `t.Mark()`, `t.RedErrorsSince(mark)`, `t.CheckNoRedErrors(mark, what)`, `t.ExpectRedError(fragment)` (announce before provoking: never
  ours in RESULT nor in jbmod's probe report, which reads `<probe file>.expected`; the log window it opens is closed); UI `t.Frames(n)`,
  `t.Shot(name)`, `t.KeepOpen(window)` (the runner leaves it open until the suite ends), `t.OpenWelcomeCard()`
  (JB_UI_SHOTS), `t.ScreenHeight(h)` (a real virtual screen through Prefs.UIScale; dispose restores); saves `t.Phase` (1 new game, 2
  loaded), `t.LoadedSave`, `t.SaveGame(name)` (with fingerprint, harness left out), `t.CheckSaveFingerprint()`,
  `t.DeclareTransient(pattern, why)` (and `[KitTransient("why")]` on fields in release code). Selection `JB_TEST=all | a,b | prefix.* |
  section:<id>`; names listed explicitly run in the order given, the rest by name; `ExplicitOnly` suites run only when named or by a
  non-`kit.` prefix (`fw.*`).
- **Mods without the framework** (Mud and Plasteel Floor, Pawn Compatibility, Rimling, QualityBuilder x Misc. Robots): import
  `build/JBTests.props` at the end of the csproj (adds `JB_TESTS` and the two references only when `JBTests=true`). The runner finds the
  mod by `JB_TEST_MOD` through its content pack (`t.Mod` is null, `t.Content` is the mod); `kit.smoke` runs a reduced form (load folders,
  assemblies, red errors), the other built-in `kit.*`/`fw.*` suites are skipped. No stand-in `KitModBase`. Example: `sample/KitSampleC`.
- **One runner** (`KitTestRunner : GameComponent`, `TestingMod : Mod` starts the red-error recorder before the mods under test): the mod
  under test is `JB_TEST_MOD` (packageId, slot suffix ignored; the framework's own id tests the framework itself); suites come from the
  harness and from that mod's assemblies. Result file `JB_TEST_OUT`, `RESULT PASS` only when every check passed, no log error of the
  framework or the mod, and no red error naming either (expected errors a suite provokes carry `[JBTEST-EXPECTED]`/`jbtest_expected`).
  Runner components are left out of every save the harness makes.
- **Built-in suites per tenant** (`TestMod/Source/KitSuites.cs`, `KitWindowSuite.cs`): `kit.smoke` (loaded folders = LoadFolders.xml v1.7
  with `IfModActive`/`IfModActiveAll`/`IfModNotActive` evaluated like the game), `kit.settings`, `kit.window` (settings window on real
  virtual screens of 1280x720, 1920x1080, 2560x1440: Prefs.UIScale 1.5 / 1 / 0.75 on the offscreen 1920x1080; every page in "this save" and "template", a search; fits the screen,
  keeps its minimum, parts inside the window and not overlapping, nothing threw; `JB_UI_SHOTS` leaves `kit_window_<id>_<size>_<page>.png`),
  `kit.defs`, `kit.migration` (an old file without fields is "nothing to migrate": the template must hold only defaults),
  `kit.saveload` (two phases, fingerprint of the mod's and the framework's save state; notes every class of the mod named in the save),
  `kit.addition` (explicit), `kit.settingsdump`.
- **Framework suites** (`TestMod/Source/FrameworkSuites.cs`, explicit): `fw.tenants` (A and B together: separate contexts, no value leaks,
  both windows open and drawn at once, a throwing page of B is fenced on B while A keeps drawing, both per-save profiles and B's SaveData
  survive save/load), `fw.removal` (save with A+B, load with A only: zero red errors of the framework, B's profile kept, its data node
  kept raw and still there after a new save, the same for B's per-map node, and no class of B in any save; phase 3 loads that save with
  B again and finds B's SaveData, per-map data and its cross-reference), `fw.steward` (A with B: the members added in the steward pass),
  `fw.apilevel` (A built with `-p:JBMinApiLevel=99`: blocked with the plain-words message, registers nothing,
  writes nothing, B unaffected), `fw.alone` (no tenant: one Mod settings entry, no red error). Agent 2's `fw.known/adapters/random/shared`
  live in `TestMod/Source/Suites/`.
- **jbmod**: every run gets a slot copy of the framework (`<fw>.slotN`; dependents' About files are pointed at it, so the player's Mods
  folder never holds a framework), the harness when suites run, extra mods (packageIds, or local folders built and copied like the mod
  under test) and the mod under test; the probe loads first. Commands: `build` (1.7, then 1.6 compile-only), `test`, `plain`
  (`--days N`, `--no-day`), `play <days>`, `saveload` (`--suites/--build/--load`), `twophase <label> <suites>`, `removal` (the framework
  and the extras stay; `--build`), `addition` (vanilla save without mod and framework; extras in both phases; `--suites`), `compat`,
  `migrate-test`, `lang`, `release`, `desc-lint`, `freeze16`, and `fw tenants|removal|apilevel|alone|all`. Env `JB_DLCS`, `GAME_LANG`
  (folder or first word), `JB_FRAMEWORK_DIR` (default: the script's repo, so a git worktree tests its own framework; every mod build gets
  `-p:JBFrameworkRoot=<that repo>/`). Build output per mod, kind and run label (`BUILDKEY` shared by the phases of one round trip) and
  one machine-wide build lock (`hh_tests/jbbuildlock`) around builds and slot copies. `release.py` adds for the framework the 1.6 build,
  `public_api.py check` and a copy without `sample/ build/ api/ TestMod/ API.md`; for dependents "no `JBFramework.dll`, no type defined in
  the `JBFramework` namespace" (tools/PublicApi `--types`).

### Decisions taken in the conversion (simplest option)
1. Type names keep the `Kit` prefix (`KitMod`, `KitModBase`, …) in namespace `JBFramework`: short, already in every doc and sample.
   Re-decided before the freeze (24.11.2026, outside-modder review asked): kept; "Kit" = the framework's toolkit types.
2. Hooks are `protected` virtuals on `KitModBase`; the framework calls them through internal bridges, each fenced.
3. The settings object is found by reflection (one `KitSettingsBase` subclass per mod assembly); during the first load its context is
   found by assembly (`Framework.ForAssembly`), because RimWorld sets `ModSettings.Mod` only after reading the file.
4. Welcome cards: one global switch ("Show welcome cards") instead of one per mod; the card's "never again" box sets it for all JB mods.
5. The framework itself is a context (`Framework.Self`) and reuses the settings window: no Overview (no sections), a "Mods" page first.
6. Absent mods' profiles are kept forever (simplest silent option of FRAMEWORK.md 2.4); their SaveData nodes are kept as raw XML and
   written back unchanged (review 24.11.2026: dropping them lost data when the framework was too old or a mod was removed for a while).
7. Name, packageId, assembly name, namespace, version and API level live in `build/Identity.props`. The namespace is necessarily also
   written in every `.cs` file; change it with a search-and-replace before the first upload (no tool yet).

### Decisions taken before the API freeze (24.11.2026, after two reviews)
8. **No optional parameters in public methods** (a default is compiled into every caller and makes a later overload ambiguous).
   Registrations take only the essentials and return an object with chained modifiers (`.InGroup()`, `.Describe()`, `.OffByDefault()`,
   `.OnlyWith()`, …). `tools/public_api.py check` fails on an optional parameter.
9. **API level 1 = the first release.** `JBMinApiLevel` defaults to 1 (never to the newest level, or every rebuilt mod would demand the
   newest framework). `tools/public_api.py uses <mod.dll>` lists what a mod binds to and the level that needs (`api/Since.txt`, written
   by `generate` for lines added after level 1); `tools/release.py` fails a dependent whose stamp is lower.
10. **`KitInfo`/`KitTheme` are property bags** (fields could never become properties later). The framework keeps its own copy
   (`KitMod.Cfg`); `KitMod.Info` returns a copy, so `PerSave` and the ids cannot change at runtime.
11. **Def edits share one ledger per field across mods** (`KitDefEdits`): original + every mod's edit in order; a restore removes only
   that mod's edit and falls back to the next most recent one, then the original.
12. **Custom pages and report functions use their own fences** (`UI/KitUiFence.cs`, `KitReport.SafeLines`): a throwing page shows an
   error box with Retry (GUI groups and scroll views it left open are closed), a throwing report shows an error line; neither pauses
   a gameplay section.
13. **Public `const` ids stay `const`** (DLC ids, `KitHearth`, `KitVEPipes.PackageId`, `KitTags`): they are used in attributes
   (`[KitCompat(KitDetect.BiotechId)]`), and the packageIds were verified against the real About.xml files. Tags are our own vocabulary.
14. **`InternalsVisibleTo("JBFramework.Testing")` stays unsigned**: any mod can reach internals by reflection or Harmony anyway, so the
   attribute grants nothing new; the test seams (`KitUiProbe`, `KitRow.ForceDrawAll`, `KitFirstStartCard.OpenNow`) are inert unless
   called. Signing would change the assembly identity, which is frozen.
15. **Copies of the framework DLL** in other mods are reported at startup (`KitStartup.ScanForCopies`, `KitMod.SelfCheck`); the build
   never copies it (`Private=false`) and release.py refuses it. RimWorld 1.7 loads mods with `Assembly.LoadFrom` in load order, so a copy
   in a mod that loads first would win: the error names that mod.
16. **Self-checks at boot** (log, never blocking): ShortId/KeyPrefix clashes between mods (the second mod's log file gets a suffix),
   more than one `KitSettingsBase` or `KitModBase` subclass in one assembly, About.xml without the framework dependency.
17. **Random seed** comes from the world seed (also `Current.CreatingWorld` during world generation); without one, a clock seed that is
   then stored in the save (never a constant).
18. **jbmod locks** carry the PID and a heartbeat (touch every 60 s): stale only when that process is gone; other tools' locks keep the
   25-minute rule. Acquiring a slot removes `_jb_*` links a hard-killed run left in that slot.

### Decisions of the steward pass (24.11.2026, requests of the seven adopting mods; still API level 1, the published baseline)
19. **Per-map data lives in a framework MapComponent** (`FrameworkMap`, one node per mod and map, raw XML kept for absent mods) instead of
   map-keyed entries in `FrameworkGame`: the map's Scribe pass hands the mod its `Map`, references resolve, and a removed map takes its
   data with it. Its name is in saves: never renamed.
20. **Legacy components are stripped, not converted**: the mod keeps its old class to read old saves; the framework removes instances of
   registered types while the game, the world and each map write their components (prefix + finalizer, the finalizer also drops the
   fresh copy that `FillComponents` makes while saving) and drops them from the running game after a load. Reading old saves stays the
   mod's own ExposeData (only it knows the fields). The same strip set serves `KitRemoval.RemoveComponents` for the rest of a session.
21. **Compat switches are installation preferences** (template store key `compat:<packageId>`, never in a save's profile, kept through
   "Save as template"), not per save: an integration is a property of the mod list, which is the same for every save.
22. **Text/list values are free keys** on the profile (no registration, no row): mods show them on their own pages. A text-field row
   (`r.Text`) was declined: every requesting mod already has a custom page for its lists.
23. **The welcome card stays the framework's** (once per colony/join, global off switch, header, footer); mods add parts (`WithDraw`),
   actions (`WithAction`) and a close callback, and may hide presets or sections. Replacing the whole card was declined (it would bypass
   the global switch and the done flag).
24. **`.slotN` is ignored by every id check** (`KitCompat.Strip`): jbmod renames slot copies of local mods so parallel slots never see
   duplicate packageIds in the shared Mods folder; keeping the real id was declined for that reason. `kit.smoke` still evaluates
   LoadFolders conditions exactly like the game (no suffix magic), so a conditional folder that names a local extra mod is not loaded in
   a test slot (the game's rule, documented).
25. **Testing API on `TestContext`**, not by making `KitUiTest`/`KitTestLog`/`SaveLoadKit` public: one discoverable object, internals free
   to change.
26. **Drawing never writes a setting** (pass 2, Luciferium Overhaul #10): vanilla `HorizontalSlider` rounds its return value to
   `roundTo` even when nobody drags, so the rows call it without `roundTo`, store only when the handle moved and round to the step then;
   the typed field stores only text the player changed. `kit.stable` (in every `test all`) writes awkward values straight into the stores
   and proves that drawing every page, searching, switching profiles, resizing, closing and reopening change no bit and mark no change.
27. **Resets once per Game/Map object** (pass 2, Set my Defaults #11): a loaded game creates `FrameworkGame` twice (Game constructor, then
   after the saved components were read); a map's resets run before its saved components are read. A legacy component's merge survives.
28. **Typed entry behind a pencil** (pass 2, Hospital Beds Gen 2 #7): the "Free" checkbox on every number row was clutter a new player could
   not read; the pencil appears while a row is hovered (or while typing is on) and has a tooltip. Per-row state is unchanged (`freeText`).
29. **A shared cross-save file helper** (Set my Defaults #15) was declined for 1.0: `LibraryDisk` is bound to Set my Defaults' library types;
   a generic `KitFile` would need its own design and tests. Additive later.


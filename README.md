# JB.Kit

Shared foundation for JB's smaller RimWorld mods (1.7 only), lifted from Hearth & Harbor (`HearthHarbor/Source/Hearth.Core/Foundation`,
`Hearth/UI`, `Hearth.Core/Testing`). Specification: `../../_Planung_Upgrade_2026-11-24/audit/SharedKit.md` (sections 1, 3, 4).
The kit is **source**: every adopting mod compiles `src/**` into its own DLL. There is no kit DLL and no Workshop dependency.
Licence MIT (each file carries `// SPDX-License-Identifier: MIT`); GPL code (RimWard) must never flow into the kit.

API reference for mod code and for the UI/Testing parts: **[API.md](API.md)**.

```
VERSION                  kit version (semver), baked into every DLL (KitInfo.KitVersion)
build/JBMod.props        the only file a mod imports (refs, net10, output, kit sources by tier, test switch)
build/JBMod.targets      obj/1.7/JBKitInfo.g.cs (version + git describe), JBReleaseGuard
src/Core/                always compiled (tiers core, lite, full)
src/UI/                  settings window, diagnosis, first-start card (tiers lite, full)   — K3
src/Testing/             in-game harness (only with -p:JBTests=true)                       — K4
src/Gate/                optional gate (-p:JBKitGate=true), not in kit 1.0                 — K6
Languages/<Lang>/JBKit.xml   keyed template with {P} = mod prefix (tools/kit_lang.py copies it into a mod)
sample/KitSampleA        complete adoption (tier full, per save, theme, custom page, first-start card); builds clean
tools/                   freeze16.py, jbmod, release/lint/lang tools (K5, other files)
```

## Build a mod with the kit

```xml
<!-- <Mod>/Source/<Mod>.csproj  or  <Mod>/Source/<Sub>/<Mod>.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>LuciferiumOverhaul</AssemblyName>
    <RootNamespace>LuciferiumOverhaul</RootNamespace>
    <JBKit>full</JBKit>                       <!-- none | core | lite | full -->
  </PropertyGroup>
  <Import Project="$(MSBuildProjectDirectory)/../../_Shared/JBKit/build/JBMod.props" />   <!-- one more ../ for Source/<Sub>/ -->
</Project>
```
```
export PATH="$HOME/.dotnet:$PATH"
dotnet build -c Release                       # → <Mod>/1.7/Assemblies/<AssemblyName>.dll
dotnet build -c Release -p:JBTests=true       # test build: JB_TESTS + src/Testing
dotnet build -c Release -p:JBRelease=true     # release build: refuses JBTests=true
```

| Tier | Sources | Defines | Meant for |
|---|---|---|---|
| `none` | none (mod's own code only) | – | QualityBuilder × Misc. Robots, Pawn Compatibility, Rimling |
| `core` | src/Core (minimal fallback window) | `JBKIT;JBKIT_CORE` | samples, mods that only want log/guard/settings |
| `lite` | src/Core + src/UI | `+JBKIT_UI` | HBG2, Mud & Plasteel (global settings) |
| `full` | src/Core + src/UI | `+JBKIT_UI;JBKIT_FULL` | DNDM, RimWard, Set my Defaults, Luci (per save, presets, card) |

All tiers also define `RW17;RW16PLUS`. `-p:JBTests=true` adds `JB_TESTS` (and compiles src/Core even with tier none, with a warning).

Props details: net10.0, references Assembly-CSharp, UnityStubs, Waylith.UnityCompat (game folder) and Harmony 1.7 (Workshop 2009463077),
no PDB, no deps file, `IntermediateOutputPath` `obj/1.7/`, compile excludes `obj/ obj17/ bin/ _frozen16/`. Override paths with
`-p:JBGameDir=…`, `-p:JBHarmonyDir=…`, `-p:JBModRoot=…`. Do **not** put a `Directory.Build.props` into `22_RimWorld/`.

## Adoption recipe (per mod)

1. Mod class: derive from `JBKit.KitModBase`, pass a `KitInfo`, move registrations into `internal override void Register(KitRegistry r)`,
   delete the old `DoSettingsWindowContents`/`SettingsCategory`. **Keep the Mod class name** (it names the settings file).
2. Settings class: **keep its name and namespace**, derive from `JBKit.KitSettingsBase`, remove the migrated `Scribe_Values` fields,
   give each registration `legacyScribe: "<old field name>"`; remaining own data goes into `internal override void ExposeExtra()`.
3. Per save (`PerSave = true`) or caretaker checks: declare `public sealed class XGame : JBKit.KitGameBase { public XGame(Game g) : base(g) { } }`.
4. Patches: keep `[HarmonyPatch]` classes; the kit applies them one by one (remove your own `PatchAll`). First line of every prefix/postfix:
   `if (!Kit.On("section")) return;`. Tag soft-integration patch classes with `[KitCompat("packageId")]`.
5. Wrap ticks/UI with `KitGuard.Run(section, "Site", …)`, log with `KitLog`.
6. `tools/kit_lang.py <Mod>`, German strings, then tests (`kit.smoke`, `kit.settings`, `kit.migration` …, K4).

## Decisions where the spec left a choice (simpler option taken)

1. **Hooks are `internal` (override with `internal override`).** `KitModBase`, `KitSettingsBase`, `KitGameBase` must be `public abstract`
   (a public class cannot derive from an internal one), and their hooks take internal kit types, so the hooks and the KitModBase constructor
   are `internal`. Only the carrying assembly can derive from them, which is what we want.
2. **The settings object is found by reflection.** KitModBase looks for the one non-abstract `KitSettingsBase` subclass in the mod
   assembly and calls `GetSettings<T>()` on it. No boilerplate; a missing class logs a red error and the mod runs on defaults (nothing saved).
3. **Order in the KitModBase constructor:** `Kit.Init` → log header → kit settings → `Register(r)` → load settings file (migration needs the
   registrations) → `KitBoot.InstallUI/InstallTesting` (partial methods) → Harmony patch classes → declaration checks.
4. **src/UI and src/Testing plug in through partial methods** (`KitBoot.InstallUI()`, `KitBoot.InstallTesting()`) and delegates in
   `KitHooks`. Core never names a UI or Testing type, so tier core compiles without them.
5. **Tier core gets a minimal window** (`KitFallbackWindow`: section checkboxes + one slider/checkbox per setting). lite and full compile the
   same sources; behaviour differences (per save, presets, first-start card) come from `KitInfo` and the registrations, not from the tier.
6. **Kit preferences are registered settings**, global, in section `core`, category `Interface`: `kit.windowScale` (1.0), `kit.verboseLog`,
   `kit.showNerd`, and `kit.firstStartCard` (only when `KitInfo.FirstStartPitch` is set). The UI draws them on the General page.
7. **Presets change only what they set.** "Matching" = applying the preset to a scratch copy changes nothing (custom matcher optional).
   `KitPresets.WouldOverwrite` lists rows the player had changed, so the UI can ask before applying.
8. **Change delivery is deferred.** Every effective change is queued (`Kit.MarkChanged`); `Kit.FlushChanges` delivers each key once to the
   setting's `OnChanged` and the mod's `OnSettingsChanged(key)`. Flushed after every settings-window frame, on window close and in
   `KitGameBase.GameComponentUpdate`. Keys: setting keys, `section:<id>`, and `profile:loaded` after a game starts/loads.
9. **Unknown section id in `Kit.On`** = programming error: logged once in red, treated as off (tests catch it).
10. **Harmony is touched only when the mod has patch classes**, so a kit mod without patches needs no Harmony. A `[KitCompat(id)]` class
    whose mod is inactive is skipped silently (counted as "not needed"), a failing one marks the compat entry "failed: reason".
11. **Settings file layout:** one `<kit>` node (`dataVersion`, `template` = KitProfile), then `ExposeExtra`. Save: `<kitProfile>` in the
    mod's GameComponent node, then the game's `ExposeExtra`. Migration rewrites the settings file once at startup (`KitStartup`), after a
    one-time copy to `Mod_<folder>_<ModClass>.xml.pre-kit`; legacy nodes are never written again.
12. **Global settings live in the template store**, also during a game; "Save as template" / "Apply template" copy only non-global values and
    the section switches (`KitProfile.CopySettingsFrom`).
13. **`KitInfo` is partial**: the generated `obj/1.7/JBKitInfo.g.cs` adds `KitVersion`, `KitCommit` (`git describe --always --dirty`),
    `KitTier`, `KitTests`. Without git the commit is `unknown`.
14. **Translations:** the English template does not contain `{P}.Setting.kit.*` (those come from the settings dump, kit_lang.py's
    `<P>_Settings.xml`); the German template carries their German text, to avoid duplicate English keys.

## Facts checked against the 1.7 game (ilspycmd on Assembly-CSharp 1.7)

The `_decomp17` folder named in the brief holds decompiled *mods* (Hospitality & co.), not vanilla; vanilla APIs were checked by decompiling
the 1.7 `Assembly-CSharp.dll` with `ilspycmd` instead. Relevant facts:
- Settings file: `Config/Mod_<Content.FolderName>_<Mod subclass name>.xml` (`LoadedModManager.GetSettingsFilename`), root `ModSettings`
  loaded with `Scribe_Deep` (settings class resolved by its full name from the `Class` attribute).
- `Mod` subclasses: `typeof(Mod).InstantiableDescendantsAndSelf()` → abstract kit base never instantiated.
- `GameComponent`s: `typeof(GameComponent).AllSubclassesNonAbstract()`, created with `Activator.CreateInstance(type, game)` → the Game constructor is required.
- `ScribeExtractor.CreateInstance` uses `BindingFlags.NonPublic`, so internal `IExposable`s (KitProfile, KitStore) load fine.
- `Dialog_ModSettings(Mod)`, `PreClose` → `mod.WriteSettings()`; `ModsConfig.IsActive(string)` lower-cases; `ModLister.GetActiveModWithIdentifier(id, ignorePostfix)`.

## Decisions in src/UI (K3) where the spec left a choice (simpler option taken)

1. **Enum settings are a dropdown** (button + FloatMenu), not a slider; Bool a checkbox; numbers a slider with "Free" entry (H&H).
   Free entry of a Percent setting is typed in percent (25 = 25 %).
2. **Section pages** exist only for sections that are available here and have settings. Unavailable sections appear only on the
   Overview, greyed, with "<requires text> (not installed)". A custom page with `section:` disappears when that section is unavailable
   and is dimmed with a "Switch on" banner when it is off.
3. **Custom pages draw into the whole main area** (below the header) and do their own scrolling; the header search still works there.
4. **Icons:** an icon column is reserved in the page list as soon as any page has an icon (labels stay aligned); vanilla fallback icons
   only for About (`TexButton.Info`) and Diagnosis (`TexButton.ToggleLog`). Missing textures draw nothing.
5. **Presets:** chips on the Overview plus a non-clickable "Custom" chip that lights up when nothing matches. A preset applies at once;
   a confirmation appears only when it would overwrite values the player changed (`KitPresets.WouldOverwrite`). Matching is recomputed
   at most every 0.4 s (it applies every preset to a scratch copy).
6. **"Reset section"** resets the section's values only (not its on/off switch, which lives on the Overview).
7. **"Prepare for removal"** is offered on the section page and on the Diagnosis page, only while a game runs and only against the
   running game's profile (not while editing the template); the result opens in the report dialog.
8. **Diagnosis report** = `KitReport.Text()` + the last 60 log lines (first line of each); "Show report" opens `Dialog_KitReport`
   (Copy button), "Copy report" copies directly. No personal paths (KitReport rule).
9. **First-start card needs `PerSave`** (its "done" flag lives in the save profile). It opens 1.5 s after the map is shown, when no long
   event runs and no `Page` window is open. "Don't show this card again" sets the global `kit.firstStartCard` off; "Use these choices for
   all new colonies" calls `Kit.SaveAsTemplate()`. Escape does not close it (as H&H); Start or Open settings do; choices are live.
10. **Nerd switch** in the header only when the mod has nerd settings; it writes the global `kit.showNerd`, which the General page also lists.
11. **Window size** (`kit.windowScale`) applies the next time a window opens (the size is read in `InitialSize`); the General page says so.
12. **Requires greying** (`KitSetting.RequiresOn`) is evaluated against the running profile (`Kit.Current`), also while the template is
    edited. Known limitation, kept simple.
13. **Glyphs:** only Latin-1 characters in code-side UI text (`»`, `·`), so every RimWorld font renders them.

## Status (kit 1.0.0, K1 + K2 + K3)

Done: repo, VERSION, props/targets, src/Core, src/UI (K3), sample A on tier full (builds clean in tiers core/lite/full and with tests,
0 warnings; only the three bases are public in the DLL).
Not yet: src/Testing (K4, other agent), tools jbmod/release (K5), KitSampleB + `kit.identity` (K7), KIT_ORIGIN.md, ADOPTION.md.
**Nothing of the kit has run inside RimWorld yet**: load, settings file round trip, migration, per-save behaviour and every pixel of the
window, diagnosis and first-start card are unverified until K4 (`kit.window` suite, screenshots).

# JB Framework

A RimWorld mod that other mods list as a dependency. It gives your mod, for free:

- a **settings window** (sidebar, search, Overview page, one page per feature area, presets, Compatibility, Diagnosis, About),
  scaled for large screens, in English and German;
- **sections**: feature areas the player switches on and off, globally or **per save** (with a template for new colonies);
- **typed settings** (on/off, numbers, percent, log sliders, enum dropdowns, silver) with legacy migration from your old settings file;
- **error fences**: an exception in your code is logged once, three in a row pause only that section (the player gets a letter and a
  Resume button), never the game and never another mod;
- a **compatibility knowledge base** (60+ known mods with tags, pitfalls and advice), detection helpers and small adapters
  (Combat Extended, Humanoid Alien Races, Nice Bill Tab, Vanilla Expanded pipe networks);
- **shared functions**: material menu after an architect dropdown pick, one architect entry per building family, a "change tier"
  gizmo, reversible def edits, removal preparation;
- **save-stable random streams** (same roll after a reload, independent named streams, fair shuffle bags);
- per-save and per-map data storage that never breaks a save when your mod is removed, and a way to retire your old
  Game-/World-/MapComponent classes without breaking old saves;
- a welcome card for new colonies that your mod can extend (own drawn parts, one-click actions);
- an in-game **test harness** and one command per mod (`tools/jbmod`) for builds, tests, save round trips, removal and addition, also for
  mods that do not use the framework.

On its own it adds nothing to a game except one entry in Mod settings. MIT licence. Author: Julius Berger.

**Built and tested on RimWorld 1.7. The 1.6 build is best effort and untested** (compiled against `Krafs.Rimworld.Ref 1.6.4871`; it
should start without errors, nothing more is promised). Depends only on Harmony.

## Quick start

```csharp
using JBFramework;
using Verse;

// 1) The mod class. Its type name names the settings file: keep your old class name when you switch an existing mod over.
public sealed class MyMod : KitModBase
{
    public MyMod(ModContentPack c) : base(c, new KitInfo { Name = "My Mod", ShortId = "MM", Version = "1.0.0", PerSave = true }) { }

    protected override void Register(KitRegistry r)            // runs inside the base constructor; use r.Kit (or Kit) here
    {
        r.Section("haul", "Fast hauling", "Pawns haul faster.");                      // a feature area, switchable (per save)
        r.Float("haul", "mm.speed", 1.5f, 1f, 5f, "Hauling speed", "Multiplier on hauling speed.")
            .InGroup("Speed");                                                         // section, key, default, slider min/max, label, desc
        r.Enum("haul", "mm.mode", HaulMode.Normal, "Mode").InGroup("Speed");           // a dropdown; labels MM.Enum.HaulMode.<Name>
        r.Preset("calm", "Calm", "Slow and gentle.", p => { p.Set("mm.speed", 1f); p.Set("mm.mode", (float)(int)HaulMode.Gentle); });
        r.Compat("Combat Extended", "ceteam.combatextended", "Ammo is ignored by fast hauling.");
    }
}
public enum HaulMode { Gentle = 0, Normal = 1 }

// 2) The settings class: empty is fine. Exactly one per assembly; the framework finds it by reflection (RimWorld names the settings
//    file after the Mod class and stores this class's full name), so an existing mod keeps its old settings class name.
public sealed class MyModSettings : KitSettingsBase { }

// 3) In a Harmony patch:
//    var k = Framework.Of<MyMod>();                  // null only before RimWorld created MyMod; safe in patches and defs-loaded code
//    if (k?.On("haul") != true) return;              // section on, available and not paused?
//    __result *= k.Get("mm.speed");                  // GetBool / GetInt / GetEnum<HaulMode> for the other kinds
// Random: KitRandom.Stream(k, "loot").Range(0, 100)   (save-stable; fetch it where you draw, do not keep it in a field)
```

Everything optional is a chained modifier on what a registration returns, so the API can grow without breaking a call:
`.Describe()`, `.InGroup()`, `.AsNerd()`, `.AsGlobal()`, `.HardLimits()`, `.Legacy("oldField")`, `.Legacy("oldPct", v => v / 100f)`,
`.RequiresOn()`, `.RequiresSetting("mm.useX", true)`, `.WithPreview(p => …)`, `.NeedsRestart()`, `.OnChange()` on settings;
`.OffByDefault()`, `.OnlyWith("ludeon.rimworld.biotech")`, `.WhenSwitched()`, `.WithRemoval()`, `.WithDiagnosis()`, `.LegacyEnabled()` on
sections; `.WithOrder()`, `.InSection()` on pages; `.HighlightWhen()` on presets; `.HealthyWhen()`, `.Switchable()` on compat entries;
`.WithDraw()`, `.WithAction()`, `.WhenClosed()` on the welcome card (`r.WelcomeCard()`). Full reference: **[API.md](API.md)**; every public member has an XML
doc comment (`JBFramework.xml` next to the DLL, so your IDE shows it).

### Names and keys
- `ShortId` ("MM") names your log file (`<config>/MM.log`) and defaults the `KeyPrefix`. Both must be unique among framework mods
  (a clash is reported in the log). Default: the letters of `Name`.
- `KeyPrefix` starts every translation key of your registrations: `MM.Section.haul`, `MM.Setting.mm.speed`, `MM.Setting.mm.speed.Desc`,
  `MM.SettingCat.Speed`, `MM.Preset.calm`, `MM.Page.<id>`, `MM.Compat.<NameLetters>.What`, `MM.Enum.HaulMode.Gentle`. English in code
  is the fallback; add only the languages you want.
- Setting keys (`"mm.speed"`) are storage keys: lower-case short id, a dot, camelCase. Never rename one (add a new key with
  `.LegacyStoreKey(old, convert)` instead). Section ids are short lower-case words; `KitMod.Core` ("core") is for general settings.

### Per save
With `PerSave = true` every setting value and section switch lives in each save; the settings file holds the **template** that a new
colony (or a save the mod joins later) starts from. The window offers "This save" and "Template". `k.On(section)` and `k.Get(key)`
always read the running save's values (the template in the main menu). Global preferences (`.AsGlobal()`) live only in the settings file.
Text and list values that belong to a save (exclusion lists, names) go through `k.GetText/SetText/GetList/SetList` (no registration;
show them on a custom page).

### Your own save data
`r.SaveData(expose, reset)` (per save) and `r.MapData(map => …, map => …)` (per map) store your data inside the framework's own
components, under your packageId: a save made with your mod then names none of your classes, loads cleanly after your mod was removed,
and your data comes back when it returns. Old saves that still hold your former GameComponent/MapComponent: keep that class, let it read
its old fields into the new data, and register it with `r.LegacyComponent(typeof(OldComponent))`: it is read, never written again.
"Prepare for removal" steps can take your remaining components out of the running game with `KitRemoval.RemoveComponents(...)`.

### Soft integrations
Register the entry with `r.Compat(name, packageId, what)` and put `[KitCompat("their.packageid")]` on the Harmony patch classes of that
integration: they are applied only while that mod is active (no error otherwise), and a failed one shows "failed: reason" on your
Compatibility page instead of breaking your mod. `.Switchable()` gives the entry a switch on that page; ask `k.CompatOn(packageId)` in
the integration's code. `KitDetect.IsActive(id)` and `KitKnown` answer the rest.

## Depend on it

About.xml of your mod:
```xml
<modDependencies>
  <li>
    <packageId>brrainz.harmony</packageId>
    <displayName>Harmony</displayName>
    <steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl>
  </li>
  <li>
    <packageId>juliusberger.jbframework</packageId>
    <displayName>JB Framework</displayName>
    <steamWorkshopUrl>steam://url/CommunityFilePage/JBFRAMEWORK_WORKSHOP_ID</steamWorkshopUrl>  <!-- the framework's Workshop id -->
  </li>
</modDependencies>
<loadAfter><li>brrainz.harmony</li><li>juliusberger.jbframework</li></loadAfter>
```

Reference the DLL at build time and **never ship it** (`Private=false`). If a mod ships its own copy and loads first, that copy replaces
the framework for every mod; the framework reports such a copy in the log.

- **From the Workshop** (any mod): reference
  `…/steamapps/workshop/content/294100/<framework id>/1.7/Assemblies/JBFramework.dll` (or `1.6/…`):
  ```xml
  <Reference Include="JBFramework"><HintPath>$(RimWorldWorkshop)/<framework id>/1.7/Assemblies/JBFramework.dll</HintPath><Private>False</Private></Reference>
  ```
- **From this repository** (clone it next to your mods): import `build/JBMod.props`, which also references the game, Harmony and the
  framework, picks the output folder and stamps the API level:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup><AssemblyName>MyMod</AssemblyName><RootNamespace>MyMod</RootNamespace></PropertyGroup>
    <Import Project="$(MSBuildProjectDirectory)/../../JBFramework/build/JBMod.props" />
  </Project>
  ```
  ```
  dotnet build -c Release                       # RimWorld 1.7 → <Mod>/1.7/Assemblies (net10.0)
  dotnet build -c Release -p:RimVersion=1.6     # RimWorld 1.6 → <Mod>/1.6/Assemblies (net472, best effort)
  ```
  The framework DLL must be built first (`dotnet build Source/JBFramework -c Release [-p:RimVersion=1.6]`), or pass
  `-p:JBFrameworkDll=<path>`.

Working examples: **[sample/KitSampleA](sample/KitSampleA/Source/SampleMod.cs)** (every feature),
**[sample/KitSampleB](sample/KitSampleB/Source/SampleB.cs)** (per-save and per-map data, legacy components, migration, an own test
suite) and **[sample/KitSampleC](sample/KitSampleC/Source/SampleC.cs)** (a mod WITHOUT the framework that runs its in-game suites on the
framework's harness: import `build/JBTests.props`, which adds the harness to test builds only).

## Versioning promise

- The public API is **additive only**: a public type or member is never removed, renamed or changed. New things are new members or
  new overloads (public methods have no optional parameters, so an overload never makes a call ambiguous). `api/PublicApi.txt` lists
  every public line and is checked at every release.
- `AssemblyVersion` stays `1.0.0.0` forever, so a mod built against framework 1.0 runs on every later one.
- `Framework.ApiLevel` rises by one with every release that adds API (1 = this first release; `api/Since.txt` records the level of
  every later addition). Your mod states the lowest level it needs (default 1; `-p:JBMinApiLevel=N` or `KitInfo.MinApiLevel`).
  When the installed framework is older, your mod's framework features stay off and the player is told in plain words to update the
  framework; nothing crashes. `python3 -I tools/public_api.py uses <MyMod.dll>` tells you the level your DLL really needs.
- Saves: your per-save values and `SaveData` are kept when the player removes your mod or the framework is too old, and come back
  when it returns. Removing your mod never leaves a missing-class error from the framework.

## Repository

`Source/JBFramework` the framework · `build/` build props for the framework and for dependent mods · `api/` public API snapshot and doc
XML · `sample/` dependent sample mods · `TestMod/` the local in-game test harness (never published) · `tools/` build, test (`jbmod`),
release and API tools · `Common/` texts, icons and the known-mods data. How the framework is built inside, the hook points, the test
harness and the release checks: **[docs/CONTRIBUTING.md](docs/CONTRIBUTING.md)**.

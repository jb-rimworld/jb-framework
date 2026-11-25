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

<a id="install"></a>
## Install without Steam

On Steam you never need this page: subscribing to a mod that needs JB Framework subscribes you to the framework too, and a mod you
already had that was updated to need it shows a short message at the main menu with an **Install JB Framework and restart** button.

Without Steam (GOG, a DRM-free copy, an offline PC), or when the automatic install did not work:

1. Download this repository as a ZIP (green **Code** button → **Download ZIP**, or the newest release under **Releases**).
2. Unpack it and put the folder into RimWorld's **Mods** folder, named `JBFramework`:
   - Windows: `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods` (or wherever RimWorld is installed, then `Mods`)
   - macOS: `~/Library/Application Support/Steam/steamapps/common/RimWorld/Mods` (or next to `RimWorld.app`)
   - Linux: `~/.steam/steam/steamapps/common/RimWorld/Mods`
   The message in the game shows the exact folder of your installation and has an **Open the Mods folder** button.
3. Start RimWorld, open **Mods**, switch on **JB Framework** and drag it **directly above** the first mod that needs it (below Harmony).
   Save the mod list and restart when the game asks.

JB Framework adds nothing of its own to a game, so there is nothing to set up after that: the mods that need it simply work again.

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
        r.Enum("haul", "mm.mode", HaulMode.Normal, "Mode", "How careful the haulers are.")
            .InGroup("Speed");                                                         // a dropdown; labels MM.Enum.HaulMode.<Name>
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
`.LegacyInverted("oldDisableX")`, `.RequiresOn()`, `.RequiresSetting("mm.useX", true)`, `.OnlyWith("ludeon.rimworld.ideology")` (hidden
without it), `.WithPreview(p => …)`, `.WithStep()`, `.NeedsRestart()`, `.OnChange()` on settings;
`.OffByDefault()`, `.OnlyWith("ludeon.rimworld.biotech")`, `.WhenSwitched()`, `.WithRemoval()`, `.WithDiagnosis()`, `.LegacyEnabled()`,
`.LegacyDisabled()` on sections; `.WithOrder()`, `.InSection()`, `.EmbedInSection(height)`, `.AsGlobal()` on pages; `.HighlightWhen()` on
presets; `.HealthyWhen()`, `.HealthyWhenType("Their.Type")`, `.HealthyWhenDef<StatDef>("TheirStat")`, `.Switchable()` on compat entries;
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

### Removable without a single red error
A save names your mod through (1) its classes (Thing/Building/Projectile subclasses from `thingClass`, Hediff and Gene classes, Bill
subclasses, Game/World/Map components and the IExposable classes inside them) and (2) its defs (things, hediffs, genes, thoughts,
recipes, designations, …). ThingComp and HediffComp data needs nothing: comps are rebuilt from the defs and their fields are skipped
when your defs are gone. What to do for "Prepare for removal" = zero red errors after removal:
1. Keep every piece of your own state in `r.SaveData` / `r.MapData` (never a component of your own). Old components: `r.LegacyComponent(typeof(Old))`.
2. Give one section `.WithRemoval(report => { … })` that calls, with `d => d.modContentPack == Content` (or your own test):
   `KitRemoval.RemoveThings` (spawned, minified, blueprints and frames, in containers, inventories, equipment, apparel, caravan cargo),
   `RemoveHediffs`, `RemoveGenes`, `RemoveThoughts`, `RemoveBills`, `RemoveDesignations`, and
   `KitRemoval.RemoveComponents(t => t.Assembly == typeof(MyMod).Assembly, report)` for anything left.
3. Not covered by the framework (do it yourself if your mod has them): pawns of your own races or kinds, things of vanilla defs made of
   YOUR material (stuff), research projects, abilities, precepts, factions, world objects, quests.
4. Prove it: `jbmod MyMod saveload` (a save with your content; own suites with `--build`), then `jbmod MyMod removal`: the answer
   must be "0 red error(s) naming the mod or the framework"; the end save and every colonist who died or left are listed.

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

### Ship the guard with your mod

Steam installs a dependency only when a player subscribes to a mod. A player who already has your mod gets your update **without** the
framework. Without a guard the game then loads your DLL without the framework: red errors, missing defs, and a colony saved in that
state loses your mod's things for good. The guard makes that impossible: without the framework your mod loads **nothing but a tiny
guard assembly** (it references only the game), and one window at the main menu (shared by every guarded mod) explains it in plain words
and offers **Install JB Framework and restart** (Steam: subscribe, download, switch it on directly above your mod, restart), the
Workshop page, Quit, or a small **Continue without** with the warning repeated. Without Steam it opens [Install](#install).

1. In your csproj, **before** the `JBMod.props` import:
   ```xml
   <JBGuard>true</JBGuard>
   <JBGuardLoss>its hospital beds</JBGuardLoss>              <!-- "a colony saved now would lose …" -->
   <JBGuardLossDe>ihre Krankenhausbetten</JBGuardLossDe>     <!-- German (other languages fall back to English) -->
   ```
2. `LoadFolders.xml`: every folder only **with** the framework, the `Guard` folders only **without** it (and `IfModActiveAll` for a folder
   that also needs a DLC or another mod):
   ```xml
   <loadFolders>
     <v1.7>
       <li IfModActive="juliusberger.jbframework">Common</li>
       <li IfModActive="juliusberger.jbframework">1.7</li>
       <li IfModActiveAll="juliusberger.jbframework,ludeon.rimworld.biotech">Common/Biotech</li>
       <li IfModNotActive="juliusberger.jbframework">Guard</li>
       <li IfModNotActive="juliusberger.jbframework">Guard/1.7</li>
     </v1.7>
   </loadFolders>
   ```
   Do the same in `<v1.6>` when your 1.6 build uses the framework (`Guard/1.6`). A frozen 1.6 of your old, frameworkless version stays
   as it was. The game compares packageIds without case and without Steam's `_steam` suffix; a framework that is installed but switched
   off counts as missing (the guard then offers **Switch on JB Framework and restart**). RimWorld 1.6 knows `IfModNotActive` too.
3. Build as usual (`dotnet build -c Release`, and `-p:RimVersion=1.6`): the build writes `Guard/<version>/Assemblies/<Mod>.Guard.dll`
   and `Guard/Languages/{English,German (Deutsch)}/Keyed/<Mod>_Guard.xml`. Commit and ship the whole `Guard` folder.
4. Prove it: `jbmod MyMod noframework` (0 red errors and 0 warnings of yours, one window, a faked Steam install leaves the framework
   directly above your mod; `JB_UI_SHOTS=1` for screenshots, `GAME_LANG=German` for German) and `jbmod MyMod test kit.smoke` (with the
   framework the guard does not load). `kit.smoke` fails when a 1.7 folder would load without the framework; `tools/release.py` also
   fails without a guard.

If a player continues without the framework and loads a save that has your things, RimWorld logs its usual missing-def errors for
them; the guard has warned before. The framework's Workshop id is read at build time from the framework's `About/PublishedFileId.txt`;
until the framework is uploaded the install button takes the manual route.

### Test it

From the folder that holds both your mod and the framework repository (RimWorld 1.7 installed; the game runs without a window and muted,
in its own throw-away profile; your own saves and settings are never touched):
```
JBFramework/tools/jbmod MyMod build              # 1.7 build into MyMod/1.7/Assemblies (and 1.6 when About lists it)
JBFramework/tools/jbmod MyMod lang --refresh     # once, and after new settings: writes Common/Languages/English/Keyed/MM_Settings.xml
JBFramework/tools/jbmod MyMod test all           # every built-in check (settings, window at three screen sizes, saves, …): RESULT PASS
JBFramework/tools/jbmod MyMod saveload           # save with the mod, load it again: values and your data survive
JBFramework/tools/jbmod MyMod removal            # load that save WITHOUT the mod: no red error
JBFramework/tools/jbmod MyMod noframework        # the mod WITHOUT the framework: only the guard loads, one window, no red error
JBFramework/tools/jbmod MyMod load my.rws --suites mm.a   # a given save (an older build's, a player's) with the current build
```
`jbmod` without arguments lists every command (own suites, other mods next to yours, no DLCs, German, screenshots).

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

`Source/JBFramework` the framework · `guard/` the missing-framework guard every dependent mod compiles in · `build/` build props for the framework and for dependent mods · `api/` public API snapshot and doc
XML · `sample/` dependent sample mods · `TestMod/` the local in-game test harness (never published) · `tools/` build, test (`jbmod`),
release and API tools · `Common/` texts, icons and the known-mods data. How the framework is built inside, the hook points, the test
harness and the release checks: **[docs/CONTRIBUTING.md](docs/CONTRIBUTING.md)**.

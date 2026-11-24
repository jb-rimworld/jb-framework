# JB Framework

A RimWorld mod that other mods list as a dependency: the settings window from Hearth & Harbor (sidebar, search, presets, switchable
areas, values per save), error fences, logs, a diagnosis report, a Compatibility page, all shared by every mod that uses it.
On its own it adds nothing to a game except one entry in Mod settings. MIT licence. Author Julius Berger.

**Built and tested on RimWorld 1.7. The 1.6 build is best effort and untested** (compiled against `Krafs.Rimworld.Ref 1.6.4871`).

Binding design: `../_Planung_Upgrade_2026-11-24/FRAMEWORK.md`. API reference and the contract for contributors: **[API.md](API.md)**.
Public API snapshot: [api/PublicApi.txt](api/PublicApi.txt) (additive only).

## Quick start for a mod (10 lines)

```csharp
using JBFramework;
public sealed class MyMod : KitModBase
{
    public MyMod(ModContentPack c) : base(c, new KitInfo { Name = "My Mod", ShortId = "MM", Version = "1.0.0", PerSave = true }) { }
    protected override void Register(KitRegistry r)
    {
        r.Section("fast", "Fast hauling", "Pawns haul faster.");
        r.Float("fast", "Speed", "mm.speed", 1.5f, 1f, 5f, "Hauling speed", "Multiplier on hauling speed.");
    }
}
public sealed class MyModSettings : KitSettingsBase { }      // the settings file (keep an existing class name)
```
In a patch: `var k = Framework.Of<MyMod>(); if (!k.On("fast")) return; __result *= k.Get("mm.speed");`

About.xml of the mod:
```xml
<modDependencies>
  <li><packageId>brrainz.harmony</packageId><displayName>Harmony</displayName><steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl></li>
  <li><packageId>juliusberger.jbframework</packageId><displayName>JB Framework</displayName></li>
</modDependencies>
<loadAfter><li>brrainz.harmony</li><li>juliusberger.jbframework</li></loadAfter>
```

csproj of the mod (`<Mod>/Source/<Mod>.csproj` or `<Mod>/Source/<Sub>/<Mod>.csproj`):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><AssemblyName>MyMod</AssemblyName><RootNamespace>MyMod</RootNamespace></PropertyGroup>
  <Import Project="$(MSBuildProjectDirectory)/../../JBFramework/build/JBMod.props" />
</Project>
```
```
export PATH="$HOME/.dotnet:$PATH"
dotnet build -c Release                       # RimWorld 1.7 → <Mod>/1.7/Assemblies (net10.0)
dotnet build -c Release -p:RimVersion=1.6     # RimWorld 1.6 → <Mod>/1.6/Assemblies (net472, best effort)
dotnet build -c Release -p:JBTests=true       # defines JB_TESTS (test suites; never in a release)
```
The framework DLL is referenced with `Private=false` (never copied into the mod) from the framework folder next to `JBMod.props`,
or from `-p:JBFrameworkDll=<path>`. A dependent mod's `LoadFolders.xml` lists `Common` plus the version folder (see `sample/`).

## This repository (it is the mod folder)

```
About/About.xml              generated from build/About.template.xml (name, packageId, author, version from build/Identity.props)
LoadFolders.xml              v1.6: Common, 1.6 · v1.7: Common, 1.7
Common/Languages/            the framework's texts, once (English + German, keys JBF.*)
Common/Textures/UI/JBFramework/   the framework's icons (tools/art/build.py; every page falls back to vanilla or no icon)
1.7/Assemblies/JBFramework.dll    built, tested in game
1.6/Assemblies/JBFramework.dll    built against Krafs refs, compile-only
Source/JBFramework/          the framework (Core/, UI/), JBFramework.csproj
build/Identity.props         THE one place for name, packageId, assembly name, namespace, version, API level
build/JBFramework.props      build of the framework itself (+ generated identity, AssemblyVersion 1.0.0.0 forever)
build/JBMod.props            build of a dependent mod (imports JBCommon.props; stamps [assembly: KitRequires(level, version)])
build/JBCommon.props         RimVersion 1.7 (net10.0, game DLLs) / 1.6 (net472, Krafs.Rimworld.Ref 1.6.4871), shared switches
api/PublicApi.txt            public API snapshot (tools/public_api.py generate|check), api/JBFramework.xml (doc comments)
sample/KitSampleA, KitSampleB    dependent sample mods (A: every feature; B: SaveData counter, migration, harness test bed)
src/Testing/                 the old in-game harness, NOT compiled now (next step: its own assembly JBFramework.Testing)
tools/                       jbmod (per-mod build/test/release), probe, release.py, freeze16.py, desc_lint.py, changelog.py,
                             kit_lang.py, i18n_delta.py, public_api.py + PublicApi/, api_scan.sh + ApiScan/, art/
```

## Build the framework

```
dotnet build Source/JBFramework -c Release                     # 1.7/Assemblies/JBFramework.dll, About/About.xml, api/JBFramework.xml
dotnet build Source/JBFramework -c Release -p:RimVersion=1.6   # 1.6/Assemblies/JBFramework.dll
python3 -I tools/public_api.py check                           # public API unchanged or only added (with a raised API level)
tools/api_scan.sh 1.6 [dependent DLLs...]                      # every Harmony target exists in RimWorld 1.6 (Krafs refs)
uv run --with resvg-py --with pillow python3 -I tools/art/build.py   # icons
```
Both builds must stay at 0 warnings and 0 errors. CS1591 is on for the framework: every public member has an XML doc comment.

## Rules (short; FRAMEWORK.md section 2 is binding)

1. Multi-tenant: no per-mod static state. Each mod has one `KitMod`; the framework keeps the list. Nothing depends on mod load order.
2. Binary compatibility forever: `AssemblyVersion` 1.0.0.0, real version in file/informational version and `Framework.ApiLevel`. The public
   API only grows; a new member needs a higher `JBFrameworkApiLevel`. Never add a parameter to a public method: add an overload.
3. A broken mod never breaks another mod or the game: every hook, tick, page, patch application and report line is fenced per mod.
4. Saves hold plain values: one `FrameworkGame` component stores every mod's profile by packageId; mods with own per-save data use
   `KitRegistry.SaveData`, which is skipped (types untouched) when the mod is absent.
5. Settings files stay per mod (the mod's own `KitSettingsBase` subclass); legacy fields migrate once, the old file is kept as `*.pre-kit`.
6. Test code never ships: suites only under `JB_TESTS`; the harness becomes its own unpublished assembly.
7. Texts exist once in the framework (English + German); mods translate only their own labels.

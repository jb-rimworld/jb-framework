# JB.Kit release tooling (Python, no dependencies, Python 3.9+)

Run every tool as `python3 -I tools/<tool>.py ...` (`-I`: no stray modules from the working directory). Only `release.py` loads its siblings
(`changelog.py`, `desc_lint.py`) by file path, and `freeze16.py` is called as a subprocess. Nothing here uploads or pushes.

| Tool | What | Spec |
|---|---|---|
| `freeze16.py <Mod> [--only17] [--check]` | freeze 1.6, `--check` verifies it | 2.4 |
| `release.py <Mod> [--dry] ...` | build, scans, guards, lint, copy to `_Release/Upload`, WPub job, WPub dry run | 4.1 |
| `changelog.py <Mod> note\|block\|inject\|check` | CHANGELOG.md -> change note / description block | 4.2 |
| `desc_lint.py <file>... [--mod M]` | description vs `_Planung_Marketing_2026-11-24/desc/STYLE.md` | 4.2 |
| `kit_lang.py <Mod> [--check]` | `<Prefix>_Kit.xml` / `<Prefix>_Settings.xml`, missing-key check | 1.5.12, 4.3 |
| `i18n_delta.py <Mod> extract\|plan\|apply` | incremental translation, deletes only known keys | 1.5.12 |

Each tool prints its full usage with no arguments (module docstring). Exit 0 = pass, 1 = check failed, 2 = usage.

## Choices where the spec left room (simplest option taken)

- **Release root** = `<ModDir>/../_Release` (i.e. `22_RimWorld/_Release`), overridable with `--release-root`; `Upload/<Mod>` and
  `Jobs/<date>-<Mod>/job.json` hang below it. The destination of `rsync --delete` must not be the mod or inside it.
- **`--dry`** runs the build (unless `--no-build`) and all checks, but writes nothing under the release root and starts no WPub dry run.
  A real run (no `--dry`) also runs `_Tools/WPub/run_dryrun.sh` (needs one of the 3 shared test slots; `--no-wpub-dry` skips it). The real
  upload (`run_real.sh`) is only printed, never run. The `_Tools/WPub` folder is found relative to the kit (`../../_Tools/WPub`).
- **csproj discovery** (until `jbmod` exists): `--csproj`, else under `Source/` the one importing `JBMod.props`, else the `*.rw17.csproj`,
  else the only csproj; several candidates = error. Build = `dotnet build <csproj> -c Release -p:JBTests=false`, `~/.dotnet` on PATH.
- **Version**: `--version`, else About `modVersion`, else the top CHANGELOG entry. About `modVersion` must equal it (missing = error with
  `--version`, warning without); the top CHANGELOG entry must equal it. CHANGELOG.md is required (change note = top entry) unless
  `--changenote TEXT` is passed. CHANGELOG.md is shipped (harmless).
- **Job fields**: `id` (number, from `About/PublishedFileId.txt`; a mod without one is an error, first upload is a manual step), `name`,
  `changenote`, `content`, `tags` = `Mod` + About supportedVersions newest first (DNDM -> Mod, 1.7). `--desc` adds `description`
  (= `<Mod>/Workshop/description.bbcode`, linted first). `--gallery` adds `preview`, `images`, `visibility` and `--title` adds `title`, all read
  from the optional `<Mod>/Workshop/job_extra.json`: `{"title": "...", "preview": "p.png", "images": [{"key": "art", "path": "a.jpg"}], "visibility": "public"}`
  (relative paths resolve against `Workshop/`).
- **Copy excludes**: `/Source /Workshop /tools`, `obj/ bin/ .git/ _frozen16/`, `*.csproj *.pre-kit *.pdb .DS_Store .gitignore .gitattributes`. The
  copy is verified (no excluded names, every `1.6/` file hash-identical, DLL scan again).
- **DLL scan strings** (ASCII and UTF-16): KitTestRunnerBase, KitTestSuite, KitSuites, SaveFingerprint, JB_TEST, JB_UI_SHOTS, JB_PROBE, HearthTestRunner.
- **Defs lint**: duplicates are keyed by (def tag, defName) per version view from LoadFolders.xml. The 1.7 view fails the release; the frozen 1.6
  view only warns (nothing there can be fixed). Textures are checked for `texPath`, `uiIconPath`, `iconPath`: a missing file next to existing
  mod textures, or a case-only mismatch (breaks on Linux), is an error; a path with no mod textures nearby is a warning ("assumed vanilla"),
  because vanilla textures are packed in the game's asset bundles and cannot be checked from disk.
- **Preview limit**: `< 1,000,000` bytes (stricter than 1 MiB, to be safe against either reading).
- **desc_lint**: order is features, Settings, Compatibility, FAQ, More from JB, Thanks, Changelog; More from JB and Changelog are errors when
  missing, Compatibility/FAQ/Thanks warnings. `[[IMG:key]]` keys are only checked when the job's keys are known (`--keys`, `--job`, or
  `release.py --gallery`). Facts (numbers, requirements, compat claims) are not and cannot be linted.
- **Key prefix** (`kit_lang.py`, `i18n_delta.py`): `--prefix XX`, else `KeyPrefix = "XX"` in the mod's C# (the `KitInfo` of spec 1.5.1).
- **kit template**: `JBKit/Languages/<Lang>/JBKit.xml` where `{P}` stands for the prefix (even inside tag names; substituted as text before
  parsing). Target folder = the mod's existing language folder (`German` matches `German (Deutsch)`), else H&H's long name.
- **kit.settingsdump contract** (what the test suite must write for `kit_lang.py --dump`, default `<Mod>/Source/settings_dump.json`):
  `{"prefix": "XX", "keyed": {"XX.Set.mood.label": "Mood bonus", "XX.Set.mood.desc": "...", "XX.Section.healing.label": "Healing"}}`.
  The key names are whatever the registry uses for translation; the tool only copies them into `<XX>_Settings.xml` (English) and checks German.
- **i18n_delta work files** live in `<Mod>/Source/i18n/` (`src/`, `src_prev/`, `delta/`, `delta_out/`, `known.json`). Source of truth =
  the English Keyed files plus def fields of `Common/Defs` and `1.7/Defs`. A key is deleted from a language only if it is in `known.json`
  (everything the previous successful `apply` saw) and gone from the English source; hand-written keys are never touched. Default languages:
  German plus whatever language folders the mod already has.

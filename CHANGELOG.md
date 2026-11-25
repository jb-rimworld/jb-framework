# Changelog

## 1.0.0 (2026-11-25)
- First public release: the settings window from Hearth and Harbor (sidebar, search, presets, switchable areas, values per save with a template for new games) for every mod that depends on the framework.
- Error fences: a feature that keeps failing pauses itself with a letter and a Resume button; the game and every other mod keep running.
- Compatibility page and knowledge base of more than 60 known mods; soft integrations that are applied only when the other mod is there and say plainly when they failed or found nothing to patch.
- Per-save and per-map data that never breaks a save when a mod is removed, a way to retire old game and map components, and building blocks for "prepare for removal".
- Save-stable random streams, reversible def edits, material menu, one architect entry per building family, tier swap gizmo.
- Settings are never changed by looking at them: a value a slider cannot show exactly (0.67 %, a value from an old settings file) is shown rounded and stays as stored until you move the control.
- A plain message when a mod needs a newer framework; that mod's features stay off and nothing else breaks.
- Built and tested on RimWorld 1.7. The 1.6 build is best effort and untested.

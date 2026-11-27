# Changelog

## 1.0.1 (2026-11-27)
- Fixed: welcome cards could cover a settings window and make it ignore every click. With several JB mods, "Open settings" on one mod's card opened its settings, and a moment later the next mod's card opened on top of them; the settings window then took no clicks and Escape did nothing. Now the next card waits until the settings window (or any other window that pauses the game, such as the scenario's intro) is closed, and only one card is ever on screen.
- Welcome cards now have a close button (X) and close with Escape. Closing a card any way keeps your choices, exactly like Start.
- With "Don't show welcome cards again" ticked, the remaining mods skip their cards at once instead of one after another.
- Nothing changes for mods built on the framework: no rebuild or update of them needed.

## 1.0.0 (2026-11-25)
- First public release: the settings window from Hearth and Harbor (sidebar, search, presets, switchable areas, values per save with a template for new games) for every mod that depends on the framework.
- Error fences: a feature that keeps failing pauses itself with a letter and a Resume button; the game and every other mod keep running.
- Compatibility page and knowledge base of more than 60 known mods; soft integrations that are applied only when the other mod is there and say plainly when they failed or found nothing to patch.
- Per-save and per-map data that never breaks a save when a mod is removed, a way to retire old game and map components, and building blocks for "prepare for removal".
- Save-stable random streams, reversible def edits, material menu, one architect entry per building family, tier swap gizmo.
- Settings are never changed by looking at them: a value a slider cannot show exactly (0.67 %, a value from an old settings file) is shown rounded and stays as stored until you move the control.
- A plain message when a mod needs a newer framework; that mod's features stay off and nothing else breaks.
- A missing-framework guard for the mods that need it: when such a mod is updated but the framework is missing or switched off, the mod loads nothing but a tiny guard (no red errors, a colony saved now loses nothing of it), and one window at the main menu says so in plain words and installs the framework through Steam, switches it on directly above that mod and restarts the game. Without Steam it shows where to download it and which folder to put it in.
- Built and tested on RimWorld 1.7. The 1.6 build is best effort: compiled against the 1.6 reference assemblies and checked statically, never run in a 1.6 game.

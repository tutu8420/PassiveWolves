# Changelog

## 2.1.0

- Vanilla animals now use the game's own translation in the HUD (Chinese clients see 狼 / 平原牦牛, English clients see Wolf / Lox).
- Creatures added by other mods are shown exactly as the prefab name typed in `Custom Mod Prefabs`.
- Localization type is resolved at runtime across all loaded assemblies, with a safe fallback to prefab names.

## 2.0.0

- Renamed to the ChineseCanFly publisher prefix: GUID `ChineseCanFly.PassiveWolves`, plugin folder `ChineseCanFly-PassiveWolves`.
- All text moved into `PassiveWolves.English.yml` and `PassiveWolves.Chinese.yml` so anyone can add a language.
- New side panel showing affected species and counts, with position, font size and visibility settings.
- `Scan Interval` setting for crowded bases.

## 1.3.0

- Rewrote the scan to be allocation free: per-creature name and original AI values are cached, config name lists are rebuilt only when they change, target clearing no longer happens every scan.
- Distance check uses squared magnitude.

## 1.2.0

- Split scope into vanilla animals and a custom prefab list for modded creatures.
- Added whitelist and blacklist.

## 1.1.0

- Added `Max Distance`, `Start Passive`, `Scan Interval` and clearer descriptions.

## 1.0.0

- First version: one hotkey that makes tamed wolves stand down and restores them.

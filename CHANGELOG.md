# Changelog

## 2.2.0

- New always-on section `[Behaviour]`, no hotkey needed:
  - `Follow Distance` — a following tame stops earlier than the game's hard-coded 3 m (patch on `BaseAI.Follow`).
  - `Chase Range` — give up a fight once the target is further than N metres from you (0 = vanilla).
  - `Ignore Targets` — prefab names these tames never treat as enemies.
  - `Ignore Taming Targets` — never attack a wild creature whose taming has not finished, unless it is already attacking you.
  - `Assist Only` — only fight back against something already attacking you or one of your tames.
- Patches are applied one class at a time and each failure is logged on its own, so a game update can only disable the affected behaviour instead of the whole mod.
- Hotkey, calm radius and panel behave exactly as in 2.1.0.

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

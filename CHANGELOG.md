# Changelog

Path of Wuxia (侠隐阁) English Mod: our fixes to the English Patch, the mod loader (ModAPI) and the translation files.
Newest first, from the first of these releases (23 October 2026).

## Next release (since 29 October 2026)

### Text
- **"New" badges** in English on every screen: library tabs and books, forge, alchemy, medicine, shop, commissions, save / load tabs and help entries (only the inventory had them before).
- **Life menu "NEW" badges** in English, the word in the middle of its diamond. Other images the game only shows later are replaced too (evaluation grade glow, the strike-back popup in combat).
- **Secondary objectives in combat:** the status stays in one piece after the objective ("Zhao Zilong survives" / "(Not Achieved)") instead of breaking the line after the character's name.
- **Battle defeat title in English:** "DEFEAT" in place of 战斗失败, in the two pieces the game slides apart (the victory title was already "VICTORY").
- **Two dialogue lines in English again:** "Providing Information" (`t5202091_100`) and `t3070203_102` had a sentence pasted over their ID, so the game showed its Chinese line.

### Fonts
- **`MessageFont`** setting: the messages popping up on screen (items acquired or lost, money, party members, stats and skills increased, notebook) can use another font than the game's DFKai-SB.
- **`EndingFont`** setting: the font of the ending screens, `Font` when empty (the ending mod used Arial only).

### Troubleshooting
- **Diagnostic log**, on by default (`DiagnosticLog` setting): `BepInEx\EnglishPatch_Diagnostics.log` lists the Windows language, the mod files and stray copies of them, the loaded plugins, the translation folder, the fonts and the game controllers, with every controller axis or button that moves.
- **`BepInEx\LogOutput.log`** is written again, its first lines give the running build and whether the diagnostic log is on (or why it could not be written).
- The diagnostic log does not slow the game: controllers are looked up every 2 seconds and only watched while one is plugged in (an earlier test build made loading a save slow).
- **Release packing:** `Output\PackRelease.bat` builds the plugins in Release before packing and stops if a build fails, so a release can no longer ship old DLLs; the release includes the default `plugins.englishpatch.cfg` and writes `LogOutput.log`.
- Known game behaviour: any device Windows lists as a game controller (rudder pedals, wheel, flight stick) is taken for a gamepad, and a pedal at rest walks the character like a held W key. Unplug it while playing.

## 29 October 2026

### Text
- Chinese commas (，) shown as normal commas; Mantra effect texts no longer cut at a comma.
- No double space in names renamed by cutscenes; the name space sits on the surname, like the character table.
- Short texts no longer vanish with Arial.
- Book names read "Name : Vol I", "Vol" kept next to its number.
- Gear "Suitable for" lists the characters with ", " and a space after the colon.
- Quest result screen: "Press any key" translated and shown whole, rewards aligned with the criteria.
- Element names in English in combat.

### Combat
- Skill bar: key numbers in the middle of their badges, skill names on one line.
- "Turn 1" centred under the crossed swords, the number as big as "Turn", in the CombatFont.
- Victory and failure conditions centred in their area, the area grows for long texts.
- Buff labels right under their icons, sharp when zoomed, outlined, the dark tab behind them hidden (`HideBuffLabelBackground`).
- Element tip and status names fit their boxes.

### Dialogue
- Talk box text between the portrait and the mountains of the box, justified, even margins.
- `DialogueFont` and `DialogueFontSize` settings.

### Life mode
- Main life screen, Socialize page and money counter in the English font; menu labels in the middle of their banners.
- Life screen bars: values on the line of their labels; the bonus tip stays inside its box.
- Life screen tips (Training, Socialize, Errands...): evenly spaced rows, long lines on two justified lines, gains and costs with + and -, one text size.
- Status tab: trait names and descriptions centred, the name on the line of the "Traits" title.
- Skills tab: lines closer together, smaller level next to the icons, skill effects justified.
- Item lists with a scroll button: wider names on at most two lines, smaller level and count icon.
- Gear selection names as wide as skill names; library book names use the whole row.
- Refine Medicine bubble: even margins, justified; "Effect" heading lower.
- Save / load tabs: the same gap between icon and name.
- Quest screen: criteria and deadline on the line of their labels, long criteria smaller instead of crossing the row line, list times clear of their icon.

### Other
- `LayoutLog` setting (`[Debug]`): `BepInEx\EnglishPatch_Layout.log` for misplaced texts.
- Lighter per-frame work in the English Patch.

## 26 October 2026

- **Fonts:** `Font` setting for the English texts (any installed Windows font, Arial by default, similar names listed in the log when a font is missing) and `CombatFont` for the combat texts in the game's own font.
- **Christmas title** on the main menu only in December, `ChristmasTitle` setting (`Auto`, `Always`, `Never`).
- **Text fitting** on many screens: elective screen (long skill names, Qi Usage label), Gear, Items and Medicine tabs (Info and Effect boxes grow to fit, even margins and gaps, justified with coloured words), Browse Medications title, relationship screen (name, hobby, buttons, abilities), training screen buttons, status traits, pawn shop tabs and effect, Refine Medicine heading and bubble.
- **Gear and skill selection popups:** Effect text under its heading, long Info inside its band, skill names the width of their row, Damage / Distance / Area / Cooldown rows raised, "Lv 10" keeps its size.
- Western date order, tracked quest text fitted, the Energy label translated.
- Reward counter, Four Arts tiers, book texts, "New" badge text and NPC names fixed.
- Resource redirects accept backslash paths and cache assets per type.
- Projects converted to SDK style (a branch keeps the old-style projects).

## 23 October 2026

- **Item icons no longer disappear:** replaced prefab images get their own texture instead of overwriting a shared sprite atlas.
- **Block popup** in English ("Block" art in the style of the Parry art); parry popup translated.
- **Player name:** a space between the surname and the name ("Zhao Zilong", not "ZhaoZilong"); dialogue still uses the surname alone. Older saves are fixed when loaded.
- Character bios fit the relationship Info box.
- ModAPI applies its hooks one by one, so one broken hook cannot block the others.
- ModAPI and English Patch code cleaned up.

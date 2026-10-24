# Dev tools (not part of the mod pack)

`FrosthavenDevTools/` is a BepInEx plugin for testing on the author's machine. It is never packed or released
(`tools/pack_zip.py` only packs `plugins/`). It works in solo games only:
- **Ctrl+F4** retires the selected character (open their sheet in the outpost first): their personal quest is
  completed with the game's own cheat and the retirement starts as the Retire button would; the game's own screens
  follow. Permanent in that save, so save first.
- **Ctrl+F9** gives each party member +100 gold and +10 lumber, metal and hide, and the Frosthaven supply +10 of
  each herb. It's one synced command (outpost only).
- **Ctrl+F10** wins the current scenario (the game's own `win` debug command).
- **Ctrl+F11** skips the rest of the prologue (`skip_prologue`). It only works on the map between prologue missions,
  and lands the campaign in the outpost (week 2, downtime). Use it on a throwaway test campaign.

Install: build it, then copy `bin/Release/frosthaven.devtools.dll` to `<game>\BepInEx\plugins\FrosthavenDevTools\`.
The pack installer doesn't track it, so remove that folder by hand when you're done.

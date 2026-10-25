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

## Gloomhaven digital (the other game)

`GloomhavenDevTools/` is a BepInEx plugin for **Gloomhaven digital** (Steam app 780290, `GH.exe`), used to play every
Gloomhaven class at the top level in its own game and compare its cards, effects and sounds with the Frosthaven port.
Gloomhaven has no BepInEx of its own: Frosthaven's loader was copied over (`winhttp.dll`, `doorstop_config.ini`,
`BepInEx\core\`; BepInEx 5.4.23 works for both Unity builds). Solo games only, on the map screen of a campaign or
guildmaster game:
- **Ctrl+F8** unlocks every class, with the game's own debug action (an unlock reward per class, as a retirement or
  quest would grant). Campaign: create characters of them at the roster. Guildmaster: a mercenary of each joins the
  guild. Saved.
- **Ctrl+F9** maxes out the party (the characters in the party slots): level 9 with the experience to match, every card
  of the class owned, perk points for every open perk, and 1000 gold. Pick the hand and the perks in the game's own
  screens. Saved. The recommended scenario level follows the party's levels, so lower it in the scenario setup if the
  fights should stay easy.
- **F1** opens the game's own developer menu (offline games; on the map or in a scenario): spawn monsters, win or lose,
  items, XP, gold, perk checks, conditions, reveal rooms and more. The retail build ships the menu but its provider
  destroys itself; the plugin keeps it (or adds one) and turns on the game's dev mode (what the `-debug` launch option
  sets), which also enables the game's own keys F2 (hide GUI), F3 (pause), F4 (free camera), F5 (wall fade), F6 (reset
  camera), F7 (regenerate scenery), PageUp/PageDown (speed) and [ ] (rotate). `GameDevMode = false` in
  `BepInEx\config\gloomhaven.devtools.cfg` turns all of that off and keeps only Ctrl+F8/Ctrl+F9.

Build: `dotnet build devtools/GloomhavenDevTools -c Release`; copy `bin/Release/gloomhaven.devtools.dll` to
`<Gloomhaven>\BepInEx\plugins\GloomhavenDevTools\`. Log: `<Gloomhaven>\BepInEx\LogOutput.log`. To remove everything,
delete `winhttp.dll`, `doorstop_config.ini` and the `BepInEx` folder from the Gloomhaven folder (or verify the game's
files in Steam).

# Frosthaven mod pack

Mods for the digital Frosthaven (Snapshot Games, Steam app 2347080), made for a group of friends.

> **Co-op:** everyone in a session must run the same mod pack version. The game only checks the game version
> on join and then compares game-state hashes during play, so mismatched mods show up as a desync mid-scenario,
> not as a join error.

Status: the pipeline is built, but no release has shipped yet. The first step is confirming in game that a
modified `Base.ruleset` loads (the `TestBannerSpearHP` mod).

---

## How the mods work

All game rules (characters, ability cards, monsters, items, perks, scenarios, ...) live in one archive,
`Frosthaven_Data/StreamingAssets/Rulebase/Base.ruleset`, as plain-text YML. A mod here is a set of small patches
against those files. Patches are applied on each player's own PC to their own copy of the game file, so:

- no game files are redistributed, only the changed lines plus a little context;
- after a game update, patches reapply to the new file, and one that no longer fits fails with a clear message
  instead of silently undoing the game's changes.

Mod layout, `mods/<Name>/`:
- `mod.json`: `{"name", "description", "enabled"}` (disabled mods are left out of release builds)
- `patches/<path>.diff`: a unified diff against `<path>` inside the ruleset
- `files/<path>`: a new file added to the ruleset (its first line `Parser: <Type>` decides where it loads)

## Tools (Python 3, stdlib only)

`tools/fh_mods.py`: the mod pipeline
```
python tools/fh_mods.py edit <Mod> <path/in/ruleset.yml>...   # copies vanilla files into work/<Mod>/ to edit
python tools/fh_mods.py diff <Mod>                            # work/<Mod>/ -> mods/<Mod>/patches + files
python tools/fh_mods.py install [--mod NAME ...]              # build + install into the game (vanilla backed up)
python tools/fh_mods.py restore                               # put vanilla back
python tools/fh_mods.py status
python tools/fh_mods.py build <out.ruleset> [--mod NAME ...]
```
Install keeps the vanilla file in `backup/Base.ruleset.<steam build id>`. It takes a fresh backup when Steam
updates the game, and refuses to touch a `Base.ruleset` it can't account for. If that happens, run Steam's
"Verify integrity of game files".

`tools/fh_ruleset.py`: low-level archive tool (`info`, `verify`, `unpack`, `pack`). The vanilla extract used for
authoring is made with `python tools/fh_ruleset.py unpack <Base.ruleset> extracted/Base`. It stays on this machine.

## Ruleset format

Reverse-engineered from `FH.YML.RulebaseFile` in `Assembly-CSharp.dll`:
- Header, 16 bytes: `char[12] "02392fe1"` (a fixed tag, not a checksum) + `uint32 count`
- Entry, 144 bytes: `char[128] name`, `int32 parserType` (`YMLLoading.EYMLParserType`), `int32 isBinary`,
  `uint32 offset`, `uint32 size`
- Data: raw file bodies back to back. No compression or encryption, and every Base entry is text (or `.lvldat`).

The game loads entries in index order, batched by runs of the same parser type. Enum order = load order.

## Notes

- Engine: Unity 2022.3.71f1, Mono. BepInEx code mods are possible later, using the same installer approach as
  the Stolen Realm pack.
- The Gloomhaven Digital mod system (rulesets, Workshop, Level Editor) is still in the code but not enabled in
  Frosthaven (`IsModdingEnabled`).
- Planned: a one-click installer exe that checks this repo's latest GitHub release (as in the Stolen Realm pack),
  with uninstall/restore built in.

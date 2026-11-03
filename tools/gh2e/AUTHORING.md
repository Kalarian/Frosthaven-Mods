# Writing a 2nd edition class's card files

The 2e pack is built from Gloomhaven-digital-format YAML that we write by hand, one file per card, in
`tools/gh2e/cards/<Class>/`. `tools/gh2e/build.py` turns those into a "virtual Gloomhaven install" and
`tools/gh_port.py --edition 2` converts them exactly as it converts Gloomhaven digital's own classes (layout restyle,
enhancement slot shapes, icons, packcheck, rulecheck). `python tools/gh2e/check.py <Class>` runs the whole chain in
folders of its own and must end with `CHECK PASSED`.

Internal class names (folders, IDs): Bruiser, Cragheart2, Mindthief2, SilentKnife, Spellweaver2, Tinkerer2,
Berserker2, Doomstalker2, Elementalist2, Nightshroud2, Plagueherald2, Quartermaster2, Sawbones2, Sunkeeper2,
Soothsinger2, Soultether, Wildfury (see `CLASSES` in build.py; the sheet name is without the "2").

## What decides

1. **The class sheet** `tools/gh2e/sheets/<Sheet>.json` (readable: `docs/gh2e/<Sheet>.md`). It was read from the card
   images and is the deciding source: values, enhancement marks, lost/persistent/round icons, XP, text rows.
   `docs/gh2e/README.md` explains its notation; `docs/gh2e/class-notes.md` + `tools/gh2e/classes.json` hold the class
   mat rules (tokens, class mechanics).
2. **Frosthaven's own cards** (`extracted/Base/Global/Characters/<FH class>/AbilityCards/*.yml`): when Frosthaven has a
   card doing the same thing, copy its shape. That is the shape known to work in this engine.
3. **The 1st edition port of the same character**: Gloomhaven's raw files `C:\Claude\Frosthaven\local\gh2e-data\gh1e_yml\AbilityCard\`
   (the same format you write) and the converted, play-tested result `mods/gloomhaven/files/Global/Characters/<Model>/`.
   Many 2e cards are 1e cards with new numbers. `CARD_FIXES` in `tools/gh_port.py` lists engine traps the 1e port
   found and how each was solved: read it before writing anything unusual (merged move-attacks, sub-abilities around a
   parent's targets, Damage with an area, AutoTrigger, ...).
4. **Starting points, never authorities:** Xipe's YAML (already imported into the six starting classes' folders by
   `import_xipe.py`; simplified on purpose, wrong initiatives and marks in places) and Gloomhaven Secretariat's action
   trees (`tools/gh2e/sources/<Sheet>.json`, field `ghs`; good area strings, some wrong values).

`docs/mechanics/gh-to-fh-map.md` maps Gloomhaven mechanics to Frosthaven ones; `fh-card-halves.md` /
`gh-card-halves.md` catalogue every card half of both games.

## One card file

`tools/gh2e/cards/<Class>/<NN>_<CardName>.yml`, NN = the card's position in the sheet (1-based, two digits). The
build **writes the header itself** (Parser, Name, Character, ID, Level, Initiative from the sheet): don't write those
keys. The file starts with the card's exact sheet name:

```yaml
# card: Trample
Discard: [Discard,Lost]          # [top, bottom]: Discard / Lost / Permanently Lost (the stack-with-red-X icon)
Data:
    Top:
        Abilities:
            TAttack:
                Attack:
                    Amount: 3
                    Enhancements: 1      # one per mark; the converter picks Frosthaven's usual shape for the ability
                Pierce: 3
    Bottom:
        Abilities:
            BMove:
                Move:
                    Amount: 4
                    Jump: True
                    Enhancements: 1
            BAttack:
                Attack: 3
                AllTargetsOnMovePath: True
                Targeting: All
                Target: -1
                XPPerTarget: 1
Layout:
    Top:
        - Row: "<b><size=130%>$Attack$ <sprite name=Attack> *TAttack* <size=100%><voffset=1>@TAttack@"
        - Row: "$Pierce$ <sprite name=Pierce> *TAttack.Pierce*"
    Bottom:
        - Row: "<b><size=130%>$Move$ <sprite name=Move> *BMove* <size=100%><voffset=1>@BMove@"
        - Row: "$Jump$ <sprite name=Jump>"
        - Row: "<size=50%> "
        - Row: "<b><size=130%>$Attack$ <sprite name=Attack> *BAttack* <size=100%><voffset=1>@BAttack@"
        - Row: "$GH2E_Bruiser_01_B$"
```

- Ability names start with T (top) or B (bottom) and are unique on the card.
- **Enhancement mark shapes:** 2e prints the shape (the sheet's `[square]`, `[circle]`, `[diamond]`, `[diamond+]`).
  Plain `Enhancements: N` gets the converter's default for the ability (attack diamond, move circle, heal diamond-plus,
  conditions diamond / diamond-plus, the rest square). When the printed shape differs, write the slot key itself:
  `SquareEnhancements: 1`, `CircleEnhancements: 1`, `DiamondEnhancements: 1`, `DiamondPlusEnhancements: 1` (the
  converter leaves those as written).
- XP printed in a half's corner: `XP: N` at the half's level (beside `Abilities:`), as in Gloomhaven's files.
- Round / persistent bonuses: an `ActiveBonus:` block (`Duration: Round` / `Persistent`), as Gloomhaven and
  Frosthaven write them. Use slots: `XPTracker: [..]`, one entry per slot (XP on that slot: 1, else 0). Slot 1 is
  the dashed first slot, where the token starts (2e rulebook p.28).
- Elements: `Infuse: [Earth]` on the half or the ability; consume: a half-level `Consumes:` block
  (`TCAir: / Elements: Air / AbilityOverrides: / <name>: / ParentName: TAttack / Attack: {Amount: 1, Pierce: 1}`; XP
  for consuming as `XP: 1` inside it), as Gloomhaven's Skewer (gh1e_yml AbilityCard/11_*) writes it. A mixed element (one of two) = an element picker
  (`Infuse: [Fire, Ice]` + `ShowElementPicker: True`, as `AMC_FireLight_P1` in gh_crossover).
- Areas: `AreaEffect: "x,y,G|x,y,R|x,y,E"` (G attacker, R target hex, E an enhanceable empty hex for a hex mark),
  in Gloomhaven's doubled coordinates. Secretariat's area strings use another system: translate, and compare with the
  1e card if it has the same area.
- Layout: Gloomhaven's own style (one row per ability, the word then the icon, one centred row per modifier, a
  `<size=50%> ` spacer between abilities); gh_layout restyles it to Frosthaven's. Keep it plain: it's redrawn anyway.
  For use slots, `- XPTracker: <ability>`; for actives, an `- Active:` element (see Xipe's Warding Strength).
- **Text keys:** every rule written as text gets a key `GH2E_<Class>_<NN>_T` / `_B` (`_T2`, `_B2`... for more) with
  its English in `tools/gh2e/cards/<Class>/strings.tsv` (`key<TAB>text`, `\n` for a line break). Use Frosthaven's
  sprite markup for icons (`<sprite name=Attack>`, `<sprite name=Shield>`, conditions by name). Gloomhaven's own
  generic keys (`$Attack$`, `$Jump$`, `$Target_AllEnemiesMovedThrough$`, `$XP_PerEnemy$`...) may be used as they are.
  Xipe's keys were imported as `GH2E_X_*`: keep them, or replace them with your own when the wording must change.
- Frosthaven icon names for actives: an `Icon:` must be one of Frosthaven's (`Positive_Shield`, `Positive_Retaliate`,
  `Positive_AttackPlus`, ... see gh_layout.ICONS and Frosthaven's own cards); a class-named icon (`Bruiser`, `Brute`)
  draws a white box. gh_port warns on any unknown icon. Per-class icon substitutions may also go in
  `tools/gh2e/cards/<Class>/icons.json` (`{"BActive": "Positive_..."}`, gh_port's CLASS_ICONS).

## House rules for the cards (decided by the user in the 1st edition port)

- **Standard confirm on every half:** never `AutoTrigger: True` on a half's first ability (packcheck enforces it).
- **Self is the default** for heal, shield, retaliate, invisible: never print "Self" after them in a text.
- **No empty lists** (`[]`): Frosthaven's YAML reader rejects the whole file.
- Damage abilities can't take an `AreaEffect` (Frosthaven can't aim them; see Catastrophic Bomb in CARD_FIXES).
- A merged move-attack picks its targets before the walk: "attack everything moved through" is a plain Move, then
  its own Attack with `Targeting: All` (Trample in CARD_FIXES).
- Sub-abilities aimed around a parent's targets need `TreatAsNonSubAbility: True` (Virulent Strain, Negative Energy).
- Unknown ability keys are rejected by packcheck/rulecheck: only keys Frosthaven's parser knows (find them on
  Frosthaven's or the converted 1e cards).

## When the engine can't do it

Translate every special rule (the sheet card's `custom` list). The user's rule (2026-11-02): "Plugin support. If we
can change the game to support cards, we should. I want to avoid changing card functionality as much as possible."
In order of preference:
1. Frosthaven's own shape for the same rule (search `extracted/Base/Global/Characters` for a similar text).
2. The 1st edition port's solution (the same character often had the same rule), or an existing GH* plugin
   (`plugins/FrosthavenMods/src`).
3. A new plugin rule that makes the engine do what the card prints (a Harmony patch in the mods plugin, inert without
   the packs; a misc var or requirement on the card marks where it applies).
Never ship an approximation or a text-only rule as finished: if the plugin isn't written yet, the card plays its plain
abilities, the rule is marked **NEEDS PLUGIN** in NOTES.md, and it counts as open work until the plugin exists.

Known 2e systems not in Frosthaven yet (each wants a plugin): **Safeguard** (blocks the next negative condition), Tear
tokens, Plague tokens, Prescriptions, Supplies + the Quartermaster's item deck, Notes + songs, permanent Invisible
(Nightshroud).

## Summons

`tools/gh2e/cards/<Class>/summons/<ID without "ID">.yml` (never a 1st edition summon's file name: build.py refuses it), Gloomhaven HeroSummon format (see `C:\Claude\Frosthaven\local\gh2e-data\gh1e_yml\HeroSummon`).
ID `<Name>2ID` when the 1st edition has a summon of that name with other stats, else `<Name>ID`; `LocKey: $GH2E_SUMMON_<Name>$`
with its English in strings.tsv. `Model:` must be a summon model the 1st edition pack already ships (the Model value of
a file in gh1e_yml\HeroSummon), so it shows in game; for a new 2e creature pick the closest one as a stand-in and say
which in NOTES.md. A summon with no range: `Range: 0`.

## Perks

`tools/gh2e/cards/<Class>/perks.py`, gh_crossover's table format (read `tools/gh_crossover.py`: `BRUTE_PERKS`):

```python
PERKS = [   # (checkboxes, name, description in sprite markup, {perk fields})
    (2, "Precision", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
]
MASTERIES = []    # optional: (name, description, BattleGoalCondition YAML) only where an existing pattern fits
NEW_CARDS = {}    # attack modifiers Frosthaven has no file for: {"AMC_GH2E_<Class>_<what>": "MathModifier: ..."}
STRINGS = {}      # extra text keys the perks name
```

Perks come from the class's perk sheet (`classes.json` "perks"; Secretariat's structured perks in
`C:\Claude\Frosthaven\local\gh2e-data\ghs\character\<stem>.json` help). Modifier names: Frosthaven's own
`extracted/Base/Global/AttackModifiers/*.yml` first; new ones in NEW_CARDS, named `AMC_GH2E_...`. A perk Frosthaven
can't express: as close as possible, noted in NOTES.md. Perk names: short, in the style of BRUTE_PERKS (the 2e sheet
prints no names: make one up from the effect).

## Class extras

- `affinities.txt`: the class's ElementalAffinities, e.g. `[ Fire ]`, or an empty file for none. Rule (gh_port
  AFFINITY_MIN): the elements its cards infuse 4+ times. gh_port warns when it disagrees.
- `companion.txt`: the companion's summon ID (the Wildfury's bear) when the class has one.

## NOTES.md (required)

`tools/gh2e/cards/<Class>/NOTES.md`: one line per card whose translation needed a decision (approximations, NEEDS
PLUGIN, a rule question settled, where you deviated from Xipe or Secretariat and why), plus "Shared changes wanted"
for anything outside the class's folder (converter, plugin) that would make a card exact. Then the check result.

## Don't

- Don't edit files outside `tools/gh2e/cards/<Class>/` (shared code is changed by the lead after reading NOTES.md).
- Don't run the game or install anything. Don't commit.
- Don't copy worldhaven assets.

## Safeguard (2e rulebook p.26)

"The next time the figure would gain one or more negative conditions, prevent one of the conditions" (a curse card
added to the deck counts; a drawn curse doesn't). Safeguard is a real condition in the pack since 2026-11-02, built by
the plugin (`plugins/FrosthavenMods/src/GHSafeguard.cs`): the token is the pack-wide character resource `GH2ESafeguard`
(`tools/gh2e/shared/resources.yml`, MaxAmount 1: no stacking), shown on the figure's status row with the icon
`Positive_DamagePrevent`; the plugin removes it when it blocks the next negative condition (CActor.ApplyCondition,
curses added to the deck included; immunity first). It is not tied to the card: the card goes where it is printed to go.

The one shape, for every class: a Ward ability tagged `GH2ESafeguard` (Ward is only the carrier; the plugin turns its
Ward into Safeguard). Standalone ("Safeguard, self" / "target all within range 1"):

    BSafeguard:
        Ward:
            DiamondPlusEnhancements: 1     # only where the card prints the mark; otherwise `Ward: True`
        Filter: Self                       # or [Self, Ally] with Range / Target
        AnimOverload: PowerUp
        MiscVars:
            GH2ESafeguard: True

On another ability's targets ("Heal 2, Range 3, Safeguard"): the same ability as a sub-ability with `UseParentTargets:
True`, `SkipAnim: True`, `AnimOverload: None`. The card's layout writes Safeguard as a plain row (it is not an active
bonus): the shared text `GH2E_SAFEGUARD_NAME` ("Safeguard") or the class's own key, with `@<ability>@` for a mark.
Never the old data stand-in (a GenericOnNegativeCondition + PreventCondition bonus).

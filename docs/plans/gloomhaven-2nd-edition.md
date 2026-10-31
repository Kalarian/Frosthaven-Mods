# Plan: Gloomhaven 2nd edition classes (content pack)

Researched 2026-10-31. Nothing built yet.

## What 2nd edition is

Same characters, new decks. Gloomhaven 2nd edition (Cephalofair, 2024-25) keeps the 17 classes, adds Bladeswarm
(1st edition's envelope X, never in Gloomhaven digital), and renames four. The decks are redesigned, some lightly,
some reworked. The classes were brought in line with Frosthaven, which matters most for us: the cards use
Frosthaven's iconography, enhancement marks and rules. That covers use slots, advantage, summons acting before
their summoner, traits, 18-box perks and two masteries. The official 2e FAQ (section 10.0) even covers playing 2e
classes in Frosthaven.

| 1st edition | 2nd edition | Hand | 3D model (Gloomhaven digital) |
|---|---|---|---|
| Brute | **Bruiser** | 10 | yes |
| Tinkerer | Tinkerer | 12 | yes |
| Spellweaver | Spellweaver | 8 | yes |
| Scoundrel | **Silent Knife** | 9 | yes |
| Cragheart | Cragheart | 11 | yes |
| Mindthief | Mindthief | 10 | yes |
| Sunkeeper | Sunkeeper | 11 | yes |
| Quartermaster | Quartermaster | 11 | yes |
| Summoner | **Soultether** | 12 | yes |
| Nightshroud | Nightshroud | 9 | yes |
| Plagueherald | Plagueherald | 10 | yes |
| Berserker | Berserker | 9 | yes |
| Soothsinger | Soothsinger | 11 | yes |
| Doomstalker | Doomstalker | 11 | yes |
| Sawbones | Sawbones | 10 | yes |
| Elementalist | Elementalist | 10 | yes |
| Beast Tyrant | **Wildfury** | 10 | yes |
| (envelope X) | Bladeswarm | 11 | **no**: only its class icon exists |

The first six are the starting classes. Each deck is the hand size in level-1 cards, plus 3 level-X cards, plus two
per level from 2 to 9: 27-31 cards per class (Sawbones adds 6 Medical Packs).

## What we already have, and what's new

**Reusable as is (the expensive half of the 1st edition port):** every 3D model, animation, sound, visual effect,
portrait and icon (`gh_model`, `gh_art`, `gh_vfx`, `gh_sound`), the card restyle (`GHCardStyle.cs`), envelopes,
the save strip, the co-op pack check, the test harness and bench. Most summon models.

**New:**
- **Card data.** There is no digital 2e, so no game files to convert. Our converter (`gh_port.convert_card`)
  rewrites Gloomhaven digital card YAML as text, so 2e needs a reader that writes that YAML from another source.
- **About 500 class-specific effects.** These are free-text rules, 22-64 per class: "gain 1 XP per enemy
  targeted", "the next four sources of damage are negated" and so on. Each needs a translation into abilities,
  often one the 1st edition port already solved for the same class.
- **New mechanics.**
  - Safeguard: a new condition that blocks the next negative condition, including a curse. Frosthaven's code
    doesn't have it.
  - Reworked class mechanics:
    - Soothsinger: songs cost tokens.
    - Quartermaster: supply tokens and a 9-card class item deck.
    - Sawbones: Prescriptions.
    - Tinkerer: Chimeric Formula.
    - Berserker: Blood Pact.
    - Mindthief: augments reworked.
    - Plagueherald: a plague condition.
    - Bruiser: a perk card that goes into the active area.
- **Perks and masteries.** 2e has its own perks and masteries, so there's no crossover sheet to copy. 2e masteries
  can be completed across several scenarios, unlike Frosthaven's.

## Sources

| Source | What it gives | Use |
|---|---|---|
| **Gloomhaven Secretariat** (github.com/Lurkars/gloomhavensecretariat, `data/gh2e`, AGPL-3.0) | Structured card actions for **12 classes**: Bruiser, Cragheart, Mindthief, Silent Knife, Spellweaver, Tinkerer, Doomstalker, Nightshroud, Soothsinger, Sawbones, Quartermaster, Elementalist. For all 18: HP, hand size, traits, structured perks, mastery texts, summon stats | **Main source** |
| **gloomhavencards.com/gh2** (data: github.com/cmlenius/gloomhaven-card-browser, no license) | Card name, level and initiative for all 18 classes, plus 750x1050 card images; images only, no text | Checking by eye; layout targets; transcription source for the missing decks |
| **Xipe's "Gloomhaven: Second Edition" mod** for Gloomhaven digital (Steam Workshop 3587771315, already downloaded on this PC; no license) | Gloomhaven digital YAML for 7 classes (Bruiser, Cragheart, Mindthief, Silent Knife, Spellweaver, Tinkerer, Voidwarden). Deliberately simplified ("close through level 5 or so") | Reference only: how someone mapped 2e cards onto the engine. Nothing copied without his OK |
| Official 2e FAQ (cephalofairgames.github.io/gloomhaven2e-faq), rulebook text | Rules; section 10.0 covers 2e classes in Frosthaven | Rules questions |
| gloomhavencards.com/fh | Physical Frosthaven card images | Learn how physical Frosthaven cards became digital ones (we have the digital shots), and apply the same to 2e |

Missing decks, from any structured source: Berserker, Plagueherald, Sunkeeper, Soultether, Wildfury, Bladeswarm.
Secretariat adds decks over time (its last two arrived in 2026-06).

## Decisions

**Agreed already** (2026-10-31): a separate pack, one edition per campaign. The second switch refuses while the
first is on, and "Copy without content packs" is the way between them.

**To decide:**
1. **Permission.** The community OK covered 1st edition, which players already own digitally. 2e is a physical
   product Cephalofair still sells, so ask again on the official Discords, specifically for 2e, before anything
   goes public. Also ask Lurkars (Secretariat) as a courtesy, with credit in the guide and README. AGPL: our repo
   is public, which covers the source. Ask Xipe only if we want to borrow from his mod.
2. **Jaws of the Lion four.** They're in neither core box and must work with either edition. Recommended: move
   them out of the 1st edition pack into a shared part that either edition's switch turns on. Class IDs stay
   unchanged, so campaigns that use them are unaffected.
3. **Bladeswarm.** There's no model. Recommended: leave it out until a stand-in is chosen (another class's model,
   recoloured?), as an explicit decision.
4. **The 5 classes with no data.** Recommended: release the 12 first, as "[WIP]". Then do the 5 when Secretariat
   has them, or transcribe them from the card images (slower; every card checked twice).

## Build

### Step 1: pack plumbing (plugin + tools), before any card

**Engine finding (2026-10-31): Frosthaven already supports several classes on one model.**
- `CCharacterClass` sets `ModelInstanceID = model + 1000 x (classes already loaded with that model)`.
- `CharacterClassManager.GetCharacterIDFromModelInstanceID` maps it back through the same list.

So the 2e classes (Bruiser on the Brute model, and so on) can sit in the ruleset next to the 1st edition ones, and
co-op control IDs stay distinct. ModelInstanceID depends on class load order, so:
- the 1st edition pack loads first, which keeps its classes' IDs exactly as today;
- every PC has the same packs, which the companion guarantees.

The model-keyed visual lookups (art, colour, skin, 3D model entries, sounds) are the same character in both
editions. They can stay keyed by model, with the 2e pack borrowing the 1st edition pack's visuals.

The plugin assumes one pack per model (`GHClasses.PackOf(ECharacter)`, `PackClasses()` by model, one merged
`Strings` table, `PackGate` sending one pack, rule bits 14/15).
- A 2e rule bit plus its own "all open at once" bit, mutually exclusive with 15/14 (refuse, like the content pack
  gate).
- Pack class lookups by class ID and the active pack.
- Strings per pack.
- PackGate sends the active pack's stamp.
- The card ID offset per pack (2e = 30000+; the hard-coded 20000-30000 range in `GHStatusIcons.cs` and
  `GHSingleTargetOverride.cs` goes).
- Distinct class IDs where names repeat (e.g. `Tinkerer2ID`), with the Gloomhaven model enum shared. That's safe
  because the two packs never meet in one campaign.
- Shared assets: the 2e pack points at the 1st edition pack's bundles, art and sounds instead of copying ~1.3 GB.
- The Jaws of the Lion split (decision 2).
- `publish.sh`, `packcheck` and `rulecheck` loop over packs instead of `mods/gloomhaven`.
- **Check:** a 1st edition campaign loads and plays unchanged; co-op join still checks the pack.

### Step 2: the 2e reader and a pilot class

**Design (2026-10-31, after reading `gh_port.main`):** `gh_port` takes a dict of Gloomhaven ruleset files
(`files = gh_files(gh)`: `Character/<cls>.yml`, `AbilityCard/*.yml`, `HeroSummon/*.yml`) and converts them as
text. So the 2e reader writes a **virtual Gloomhaven install**: Gloomhaven-format YAML in `tools/gh2e/build/`, and
`gh_port` gets a `--source gh2e` switch that reads `files` from there instead of the Steam install. Assets
(models, art, sounds) still come from the Steam install by model name.

How the parts fit:
- **Card IDs:** the reader numbers 2e cards 10001+, so `convert_card`'s +20000 gives 30001+. That's a range of
  its own, with no change to `convert_card`.
- **Class files:** `Character/Bruiser.yml` with `ID: BruiserID`, `Model: Brute`. Classes whose names repeat get
  an ID suffix (`Tinkerer2ID`).
- **Where the card YAML comes from, per class:**
  - **Xipe's YAML** for the six classes he made, corrected by the sheet's `xipe` differences. It's already in the
    format and has layouts.
  - **Generated from Secretariat's action tree** for the other six with Secretariat data. Card values come from the
    sheet wherever it disagrees.
  - **Hand-written** for the five image-only classes, from their double-read sheets.
- **Special rules:** one table per class (`tools/gh2e/effects/<Class>.py`), the 2e version of `CARD_FIXES`. Each
  sheet `custom` entry gets a YAML translation and a note.
- **Pack constants** become per-pack, and the 2e pack builds into `mods/gloomhaven2e/`: `PACK`, `PACK_TITLE`,
  `PACK_VERSION`, `OUT`, `STARTING_CLASSES`, `CLASS_EXTRAS`, `CLASS_ICONS`.

- `tools/gh2e_import.py`: Secretariat JSON -> Gloomhaven digital AbilityCard YAML (abilities, enhancement marks,
  lost/persistent, layout rows from its grouping), so `convert_card`, `gh_layout` and `GHCardStyle` work unchanged.
  This needs `gh_port.main()` split into reading, converting and assembling.
- `tools/gh2e_effects.py`: a hand-written table, one entry per custom effect text -> abilities (like
  `CARD_FIXES`). Each entry notes its 1st edition equivalent when there is one.
- A 2e column in the mechanics map (`tools/mechanics.py`): every 2e action type and custom effect marked as
  Frosthaven has it, solved in the 1st edition port, or needs the plugin.
- Perks from Secretariat's structured perks (the `gh_crossover` pattern); the text-only perks and the masteries by
  hand.
- Retirement events: reuse the 1st edition class events (same characters).
- Codex: new texts from the 2e cards.
- **Pilot: Bruiser.** It has the fewest custom effects (24) and is among the least changed. Full bench + effects
  pass, then the user's play test.

### Step 3: the other 11 data classes, in groups
Ordered by custom effects (fewest first) and by new mechanics:
- Spellweaver 22, Elementalist 32, Tinkerer 34, Silent Knife 38, Sawbones 39.
- Nightshroud 43, Cragheart 44, Mindthief 45, Doomstalker 53.
- Soothsinger 64, Quartermaster 64 (song tokens, supplies and the class item deck: the most plugin work).

Each group: packcheck, rulecheck, bench, then the effects pass, as with the 1st edition groups.

### Step 4: Safeguard and the class mechanics
Built when their first class needs them. Each one is checked against the game's code first, because Frosthaven's
code kept many Gloomhaven mechanics that no card uses.

### Step 5: release
"Gloomhaven classes (2nd edition) [WIP]", plus guide, README, companion and #known-issues entries.

### Later
- The 5 missing decks.
- Bladeswarm (decision 3).

## Risks
- Secretariat's data is a community transcription. Spot-check every card against its image.
- Custom effect translations are judgement calls. Each one is noted, as `CARD_FIXES` are now.
- Two packs in one `Base.ruleset`: every path and key must be distinct. packcheck gets a cross-pack collision check.
- Permission for 2e may be refused. Then it stays a private build.

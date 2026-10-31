# Gloomhaven 2nd edition: port groups

Built from the card sheets (`docs/gh2e/<Class>.md`) and class rules (`docs/gh2e/class-notes.md`), 2026-10-31.
Same rhythm as the 1st edition port: one pilot, then groups of four. Each group runs packcheck and rulecheck,
then the bench and effects pass, then the user's play test. A group brings in the new systems its classes need, so
later groups reuse them.

"Special rules" = card rules that aren't plain abilities: the hand-translation list. Summon models: **have** = a
1st edition summon model we already ported. **Stand-in** = a new 2e creature, which needs a decision: borrow
another summon model, recoloured or scaled, or one of Frosthaven's own monster models.

## Pilot: Bruiser

| Class | Cards | Special rules | Summons | New systems |
|---|---|---|---|---|
| Bruiser | 29 | 24 | none | none: no new conditions, no tokens |

It proves the whole 2e path end to end with the fewest unknowns:
- the Secretariat reader;
- the custom-effect table;
- the two-pack plumbing;
- 2e perks and masteries;
- a 2e class next to the 1st edition Brute on the same model.

Its special rules are mostly turn-history X values (hexes moved, damage dealt), triggered persistents and push
collision damage.

## Group 1: no summons, nothing new to build

| Class | Cards | Special rules | Summons | Main mechanics |
|---|---|---|---|---|
| Spellweaver | 27 | 24 | none | "next lost action this round" triggers, play a card's lost action from the lost/discard pile, Ward |
| Sunkeeper | 30 | 36 | none | grant abilities to allies, recover cards for allies, modifier rewrites, Ward/Bless (double-read) |
| Cragheart | 30 | 44 | none | obstacles (create, destroy, move: the 1st edition Cragheart did these), friendly fire, Ward |
| Berserker | 28 | 55 | none | self-damage for bonuses, half-HP thresholds, Blood Pact, Fire (double-read) |

Ward, use slots, advantage and obstacles are all Frosthaven-native or solved in the 1st edition port. The work is
translating special rules.

## Group 2: Safeguard, and summons that have models

| Class | Cards | Special rules | Summons (model) | Main mechanics |
|---|---|---|---|---|
| Tinkerer | 31 | 31 | Decoy, Battle Bot, Kill Bot (all **have**) | placed tokens (Enhancement Field, Teleportation Pad), trap creation, **Safeguard** |
| Elementalist | 29 | 44 | Chromatic Construct, Elemental Conduit (stand-in) | wild and split elements, element manipulation, **Safeguard** |
| Mindthief | 29 | 43 | Rat Swarm (Giant Rat), Rat Monstrosity (stand-in), Rat King (**have**) | augments (solved in 1st edition), controlling enemies |
| Silent Knife | 28 | 37 | none | isolation/flank bonuses, money tokens on the map, trap tricks, converting an enemy |

New here:
- **Safeguard**, the 2e condition Frosthaven lacks: it blocks the next negative condition, including a curse.
  It's built once here, then used by Elementalist, Sawbones, Nightshroud and the Medical Packs.
- **Map tokens** that aren't traps: the Tinkerer's fields and pads, the Silent Knife's money tokens.

## Group 3: class tokens

| Class | Cards | Special rules | Summons (model) | Main mechanics |
|---|---|---|---|---|
| Doomstalker | 30 | 52 | Vicious Jackal, Battle Boar (**have**), Green Snake (Green Adder), War Raptor (War Hawk), Staunch Garralev, Horned Hristek, Great Archinotl (stand-ins) | Dooms (Frosthaven's code kept them; the 1st edition port used them), summon focus |
| Sawbones | 35 | 40 | none | Medical Packs (6 different cards handed to allies; the 1st edition supply cards), **Prescriptions** (cards put into an ally's active area), Safeguard |
| Nightshroud | 28 | 51 | none | **Tear tokens** (max 2 on the map, teleport targets, end your Invisible), **Invisible as a permanent class state** (can't attack while invisible) |
| Plagueherald | 29 | 53 | none | **Plague tokens** (up to 3 per enemy, 12 in all, scale damage and penalties), poisoning allies as a cost (double-read) |

New here: a token framework covering counters on enemies (Plague), class tokens on the map (Tears) and cards in
another character's active area (Prescriptions). Nightshroud's permanent Invisible is the riskiest rule in the
whole port: it changes how monsters focus.

## Group 4: the big systems

| Class | Cards | Special rules | Summons (model) | Main mechanics |
|---|---|---|---|---|
| Quartermaster | 30 | 62 | none | **Supplies** (track 0-50, spend to recover items), **class item deck** (9 one-copy items handed out by cards) |
| Soothsinger | 30 | 65 | none | **Songs** (15 persistent tops paid with High/Low Notes, max 2 each), Notes from cards |
| Soultether | 31 | 48 | 12: Iron Beast, Shadow Wolf, Thorn Shooter, Rock Colossus, Slime Spirit, Void Eater (**have**); Prismatic Unicorn (Black Unicorn); Rift Spirit, Floating Jellyfish, Covetous Imp, Ember Sprite, Fey Subjugator (stand-ins) | granting abilities to summons, summon death/kill triggers, X = number of summons |
| Wildfury | 29 | 26 | Bear companion (Beast Tyrant's bear, **have**); Wind Idol (Wind Totem), Monolith (**have**), Vital Force (stand-in): three immobile aura sigils | COMMAND halves the bear performs (13 cards), invocations on the bear's attacks, element-required halves, **Safeguard** |

Each brings a system of its own:
- the Quartermaster: an item supply and a resource track;
- the Soothsinger: a two-token song economy;
- the Soultether: the most summon models;
- the Wildfury: a companion that acts just before you.

These go last, when everything else is proven.

## Systems to build, in order

| System | First needed by | Also used by |
|---|---|---|
| 2e reader + custom-effect table + two-pack plumbing | Bruiser | all |
| Safeguard condition | Tinkerer (G2) | Elementalist, Sawbones, Nightshroud |
| Map tokens (non-trap) | Tinkerer, Silent Knife (G2) | Nightshroud (Tears) |
| Enemy counters (Plague) | Plagueherald (G3) | |
| Cards in an ally's active area (Prescriptions) | Sawbones (G3) | |
| Permanent Invisible class state | Nightshroud (G3) | |
| Supplies + class item deck | Quartermaster (G4) | |
| Notes + songs | Soothsinger (G4) | |
| Stand-in summon models (8 new creatures) | Elementalist, Mindthief (G2) | Doomstalker, Soultether |

## Rules questions collected from the readers
Each sheet's `unclear` list holds them. The common ones, checked against the 2e rulebook text:

**Settled:**
- **Use slots (p.28):** "When the card is placed in the active area, a character token is added to the first slot.
  Each time the bonus is triggered, the token advances by one slot … when the token passes an experience icon, the
  character gains that much experience … when the token leaves the last slot, the bonus expires." The dashed first
  slot is where the token starts. A persistent bonus with no removal condition lasts until the end of the scenario.
- **Stack-of-cards-with-red-X icon (p.34):** the lost card "cannot be recovered by any means until the end of the
  scenario". Frosthaven has the same rule.
- **Split (mixed) element (p.21):** one of the two, chosen at the end of the turn, never both.
- **"All" (p.19):** "all" follows the usual targeting: negative abilities target enemies, positive ones allies or
  yourself. "All figures" targets both.
- **Safeguard (p.26):** "The next time the figure would gain one or more negative conditions, prevent one of the
  conditions … can prevent a figure from adding a curse card to their deck, but does not prevent a drawn curse card
  from taking effect."

**Still open (decide per card when porting; note each decision like `CARD_FIXES`):**
- Range of "target all" heals with no printed range. The likely reading is melee range, as for attacks with no
  range (p.40).
- "Undamaged": full hit points, or not damaged this turn?
- Class-specific timing questions listed in each sheet's `unclear`.

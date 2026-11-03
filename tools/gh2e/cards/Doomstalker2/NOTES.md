# Doomstalker (2nd edition): translation notes

All 30 cards written from scratch from `tools/gh2e/sheets/Doomstalker.json` (no Xipe data; Secretariat only where the
sheet agrees). Doom shapes come from the play-tested 1st edition port (gh1e_yml 376-406, `mods/gloomhaven`), the rest
from Frosthaven's own cards (Boneshaper, Blinkblade, Pain Conduit, Snowdancer summons). Enhancement marks (count and
printed shape), lost / persistent / unrecoverable icons and corner XP were cross-checked against the sheet by script on
the converted files: all 30 match. Square marks on Attack (Relentless Offensive top, Dual Shot top) are written as
`SquareEnhancements`. Every Doom has its status-row key `GH2E_DOOM_127NN` in strings.tsv (14 dooms: cards 1, 2, 4, 7,
8, 11, 12, 16, 17, 22, 24, 25, 26, 29).

## Class-wide decisions

- **Doom target filter**: `TargetType: Enemy, EnemyType: [Normal, Elite, Boss, Object]` ("may target any enemy,
  including bosses and objectives") with `MiscVars: CheckLOS: True` (the class mat: "one enemy within your
  line-of-sight"; Targeting All alone ignores line of sight, Banner Spear At All Costs adds the flag). One Doom at a time
  is the engine's default doom slot (as in the 1st edition).
- **"Your attacks" in a Doom** = `BuffIncomingAttacks` filtered `PlayerClasses: Doomstalker2ID` (1st edition Rain of
  Arrows). **"Your summons"** = `ActorType: Summon` + `Summoner: [Doomstalker2ID]` (CAbilityFilter.CheckSummoner: the
  summon's summoner class), so other players' summons get nothing.
- **"Your summons focus on this enemy"** (The Hunt Begins, Give Chase, Walking Bastion, Charging Beast): text only.
  Frosthaven's summons are steered by the player, so the player can follow it. **NEEDS PLUGIN** for a real focus rule.
- **Granting summons abilities**: Frosthaven's Command the Wretched shape (ControlActor, `Range: 99`,
  `Filter: TargetType: OwnSummon`, `ControlActorType: Player`). "This attack must target the target of the
  Doomstalker's attack" = the Eye of the Void filter (`TargetTargetedByCasterPreviousAbility` + `SpecificAbilityNames`)
  on the granted attack (Feral Aid bottom, Relentless Offensive top). Unverified from a summon's granted attack.
- **"Play a card to perform its Doom action"** (Murderous Stalker top, Heart of Doom top and bottom, perk 9):
  Frosthaven's `SelectAndPlayCard` (Boneshaper Malicious Conversion / Bone Dagger, Blinkblade Reverse the Flow) with
  `IsActionValid: Action.Abilities.Any(obj.AbilityType == CAbility.EAbilityType.AddDoom)` (AddDoom is in the game's
  EAbilityType, checked in ScenarioRuleLibrary.dll). `CardPile: [Hand, Discarded]` uses the list form of Hail of
  Spears / Reverse the Flow. Unverified in play.
- **Summons**: melee summons `Range: 1` (as every melee summon in the 1st edition and the other 2e classes); all marks
  `SquareEnhancements` (2e prints squares; Horned Hristek's Move has none). Files are named `<Name>2.yml` where the
  1st edition has a file of that name, so the virtual install doesn't overwrite the 1st edition's HeroSummon file.

## Decisions per card

- **01 Rain of Arrows** top: the 1st edition shape, Attack 3 Range 4, slots [1,0,1,0]. "While there is another enemy
  within Range 4": the bonus Requirement (Forward Momentum's `ValidActorsInRangeOfAddedAbilityRange > 0`), so a
  death with nobody in range spends no slot. Bottom: Pierce 2 as the
  1st edition Expose doom's `AttackEffect`.
- **05 Murderous Stalker** top: kill XP as a `GainXP` sub-ability (Crashing Tide Submerge) under the kill requirement;
  the corner XP 1 is unconditional (the sheet has both).
- **06 A Moment's Peace** top: "reveal the top card of a monster's ability deck": CustomAbility.ShowTopMonsterCards on one
  enemy within range 5 (NullTargeting + PerformVars, the Nightshroud's Eyes of the Night / Shattersong Transparency).
  Bottom heal: `TargetType: [Self, OwnSummon]`, range 1 (same on 27 Feral Instincts).
- **07 The Hunt Begins** top: "undamaged" = full hit points (1st edition Fresh Kill, `Health: == 100%`).
- **08 Solid Bow** bottom: the 1st edition Sap Life doom, only on damage from you or your summons (bonus Vars on the
  damaging ability's actor, Boneshaper Unholy Prowess's form); the skippable Heal 2 goes to you or an ally adjacent to
  the doomed enemy (custom target list). Bench.
- **10 Forward Momentum** top: the 1st edition Frightening Curse transfer (aura over doomed enemies, on death
  `TransferDooms: -1`, Range 3 near the dead one's hex, only when an enemy is there), one slot, then the card is
  discarded. The plugin's GHDoomTransfer fix covers the transfer.
- **11 Hidden Danger**: Green Snake's "unaffected by difficult terrain" = `MoveOverride: IgnoreDifficultTerrain`
  (Leaper's MoveOverride form with Sturdy Boots' flag; unverified on a summon); "last in initiative order for monster
  focus" = Frosthaven's Snow Fox (`AdjustInitiative: 200`, `FocusInitiative`), exact.
- **12 Give Chase** top: "summons +2 Move and Jump; focus on this enemy" has no doom shape (doom bonuses sit on the
  monster). **NEEDS PLUGIN.** The doom carries a +0 summon-attack bonus only so it has an ability (token and status row
  work); the text is printed.
- **13 Feral Aid**: Attack 2 top / Attack 3 bottom as printed (Secretariat swaps them). Top conditional =
  Frosthaven's Decaying Will (`TargetAdjacentSummonsOfCaster > 0`), exact.
- **14 Fierce Fighter**: Battle Boar ignores Retaliate = Frosthaven's Flesh Fiend (`BlockRetaliate`). Bottom: round
  BuffAttack with `AttackHasAdvantage`.
- **15 Relentless Offensive** bottom: one control with Move +0, Attack +0, Move +0, Attack +0.
- **16 Dual Shot** bottom: "shuffle one Curse into the monster modifier deck when it dies" = on death a self
  NullTargeting whose end calls `CustomAbility.AddCurseModifiersToEnemyMonsterDecks(1)` (Pain Conduit Redemption).
- **17 Walking Bastion**: Ward on summoning in the summon's `OnSummonAbilities`. "Whenever you long rest, it gains
  Ward" = a second persistent bonus on the summon half (Unholy Prowess keeps extra bonuses on its summon half) with
  Boneshaper perk 10's `DuringActionAbilityOnLongRest`, warding `HeroSummonClasses: [StaunchGarralevID]`. Unverified:
  that this bonus ends when the summon dies (else the card could stay in the active area). Bottom: "once per round,
  after a summon attacks it, grant that summon Attack 1, Pierce 2" folded into the summon's attack: once per round
  (`Restriction: OncePerRound`) a summon's attack on it gains +1 Attack and Pierce 2 (approximation: one modifier draw).
- **19 Camouflage** top: Invisible only when adjacent to a wall (`TargetAdjacentToWalls` on yourself, the 1st edition
  Demolitionist filter; unverified on a self target). No corridor tiles in Frosthaven digital: dropped (user decision
  C). "Lose Invisible at the end of the round": `DecrementTrigger: Rounds` + a round bonus (Stick to the Shadows). Bottom: the Sunkeeper's round disadvantage
  aura (`AttackersGainDisadvantage`) on your summons within range 1.
- **20 Coated Arrows** top: "Poison, then Wound, then Immobilize, then Disarm on the next four ranged attacks": a
  different condition per slot has no shape. **NEEDS PLUGIN.** Approximation: a ranged-only (`AttackType: Ranged`)
  persistent BuffAttack, four slots (XP on the fourth), adding Poison and Wound to each of the four attacks.
- **22 Expose**: the 1st edition top without the toggle: the once-per-turn advantage lands by itself on the turn's first
  attack on a doomed enemy. Bottom
  doom: Wound + advantage on summons' attacks.
- **23 Wild Command** bottom / **30 Heart of Doom** top: "target one doomed enemy at any range" = the 1st edition
  Wild Command (`Targeting: All` + `IsDoomed`; one doomed enemy at a time anyway).
- **24 Impending End** top: the transfer of 10 as a one-use bonus for the turn; it must exist before the attack (a
  doom is discarded the moment its enemy dies), so it is the half's FIRST ability (standard confirm) and the attack
  second. Its Vars accept only a death caused by this card's TAttack (the cause-of-death ability). Bottom (3 slots): two doom bonuses,
  Immobilize (1 use) and +2 Attack (2 uses, XP on the last): the first attack gets Immobilize and +2, the second +2
  (same totals over two attacks instead of three; approximation).
- **25 Charging Beast**: Horned Hristek's trait (+2 Attack for the round after its first move): text only, **NEEDS
  PLUGIN**. "Whenever it kills, grant it Move +0": a persistent on-kill bonus on the summon (OnSummonAbilities, Drifter
  Fortitude's DuringActionAbilityOnKill, the killer must be the Hristek), Move 3 stacked next to its ability. Bench. Bottom: the 1st edition Nature's Hunger doom, Heal 3.
- **26 Darkened Skies** top: stack-with-red-X = `Permanently Lost`.
- **27 Feral Instincts** top: the granted attack may target only doomed enemies (`IsDoomed`); "the move must end
  adjacent to a doomed enemy" = `MoveRestrictionType: MustEndOnSpecificTiles` with the hexes adjacent to doomed
  enemies (Trapper Lure of the Snare).
- **29 Predator and Prey**: Great Archinotl's extra turn at initiative 100: text only. **NEEDS PLUGIN.** Bottom: the 1st
  edition doom (`TargetDistanceRangeDiff`), filtered to your attacks (the 1st edition buffed everyone's).
- **30 Heart of Doom** bottom: "any number of Dooms on the same target" = the 1st edition Inescapable Fate's
  `AddDoomSlot`, +4 (five dooms). "When discarded, discard all but one active Doom" is left to the engine's slot
  handling (unverified).

## Summon models (stand-ins for new 2e creatures)

| Summon | ID | Model |
|---|---|---|
| Vicious Jackal | ViciousJackal2ID | ViciousJackal (have) |
| Battle Boar | BattleBoar2ID | BattleBoar (have) |
| Green Snake | GreenSnakeID | GreenAdder |
| War Raptor | WarRaptorID | WarHawk |
| Staunch Garralev | StaunchGarralevID | **IronBeast** (stand-in: a heavy, armoured beast for the Ward/Pierce tank) |
| Horned Hristek | HornedHristekID | **BlackUnicorn** (stand-in: the only horned charger among the 1st edition summons) |
| Great Archinotl | GreatArchinotlID | **SwampLizard** (stand-in: a large forest predator) |

## Perks (perks.py)

- 2 Second Arrow: "Doomstalker performs: Attack 2, Range 5" has no Frosthaven modifier (an attack inside a modifier
  draw). New `AMC_GH2E_Doomstalker2_Attack2Range5_M1`: -1 with 2 damage on one enemy within range 5 the player picks
  (the AMC_Damage1Range1_P0 / Sunkeeper heal-card shape). Approximation (damage, no modifier, shields apply as damage).
- 4 Pack Hunt: new `AMC_GH2E_Doomstalker2_SummonAttack_P0` on Frosthaven's HIVE card AMC_P0_ControlSummonMove2 (a
  modifier granting a summon an action), Attack +0; "summons adjacent to the target" not checked (any of your summons).
- 8 Foresight: Frosthaven's Brummix pet shape: a one-use StartRoundAbility placed at scenario start whose NullTargeting
  calls `CustomAbility.ShowTopMonsterCards()` (every monster deck in play), so it fires at the start of round 1. Bench.
- 9 Restless Doom (linked): `ExecuteAbilitiesAfterLongRest` + `ExecuteAbilitiesAfterShortRest` (both flags in one
  perk) with SelectAndPlayCard from the hand, Doom actions only. Unverified in a rest.
- 10 Heel (linked): text only (summons are player-moved in Frosthaven).
- Masteries: none written (no existing pattern). See shared changes.

## Shared changes wanted

- **Plugin (NEEDS PLUGIN)**: summon focus on a doomed enemy (4 dooms); Give Chase's summon +2 Move / Jump from a doom;
  Coated Arrows' per-slot condition sequence; Horned Hristek's after-first-move +2 Attack; Great Archinotl's extra turn
  at initiative 100; Walking Bastion's real "grant that summon Attack 1, Pierce 2" after its
  attack.
- **GHMasteries.cs**: a total "kills of doomed enemies in a scenario" tag (`k.Doom`, the kill records
  DoomedKillsInTurn already reads, summed instead of per turn) for "Kill 20 enemies that have Doom"; a per-round attack
  count over the hero and one of its summons for "6 or more attack abilities in one round".
- **gh_port** (cosmetic): its summary line counts the class's new attack-modifier files as masteries ("10 perks and 2
  masteries") for a class with an empty MASTERIES list.

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Doomstalker2`: **CHECK PASSED Doomstalker2** (30 of 30 cards,
packcheck 0 problems / 0 warnings, rulecheck 50 ok, 7 summon definitions, 10 perks, no gh_port warnings, no missing
text keys).

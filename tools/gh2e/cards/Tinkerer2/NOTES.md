# Tinkerer2 (Tinkerer, 2nd edition): translation notes

All 31 cards rewritten against `tools/gh2e/sheets/Tinkerer.json` (the image reading). Xipe's files were the starting
point for 26 cards; Teleportation Pad, Harsh Stimulants, Gravity Bomb and Chimeric Formula were placeholders there and
are written from the sheet. Untested in game (no game runs for this work): every card below marked "untested shape"
should be in the first bench.

## Class-wide

- **Summons** (`summons/`): Decoy2ID (HP 10, Move 3, no attack; model Decoy), Battlebot2ID (HP 7, Move 3, Attack 3,
  melee; model Battlebot), Killbot2ID (HP 8, Move 4, Attack 3, melee, area attack "0,0,G|1,1,R|2,2,R" as an
  AttackOverride like Frosthaven's Arcing Generator; model **Killbot**, not Xipe's "Rat Monstrosity", which the 1st
  edition pack doesn't ship). IDs end in 2 because the 1st edition has summons of those names with other stats. Every
  printed [square] on summon stats is written as SquareEnhancements (Frosthaven's summon slot shape) so the converter
  doesn't turn Attack into a diamond and Move into a circle.
- **Xipe's key `KillBot`** (in no text table) replaced by `GH2E_SUMMON_KillBot` ("Kill Bot"); all summon names use
  `GH2E_SUMMON_*` keys. Xipe's `GH2E_X_*` keys and the 1st edition keys `$045_B$`, `$052_T_1$`, `$040_B$` are no longer
  used (own keys with the 2e wording).
- **Placed tokens** (Enhancement Field, Pernicious Fogger, Teleportation Pad): the closest working Frosthaven shape
  is a summon that is a token, not a figure: `Health 1, Move 0, Attack 0, Range 0, Untargetable, Invulnerable,
  DoesNotBlock` (the 1st edition Ward, which the G3 summon bench placed in a scenario with its aura working), with the
  token's effect as an `OnSummonAbilities` aura. Stand-in models from the 1st edition pack: Enhancement Field =
  SpiritBanner (the 1st edition Spirit Banner does the same job, +2 Attack aura), Pernicious Fogger = WindTotem,
  Teleportation Pad = Ward. How they look is to be judged in game.
  - Placement: the summon ability's `Range` (3 / 3 / 5, Gravity Bomb 4) is read by CAbilitySummon.
  - **Empty hexes** (class mat; GH2e FAQ: tokens "won't make a hex non-empty"; user decision A3): every token also has
    `IgnoreActorCollision: True` (Frosthaven's flag for a figure that shares a hex, the 1e Sand Devil's), so figures may
    end a move, be pushed / pulled / teleported onto it, and GHMapTokens.cs hides the four token IDs from the engine's
    position lookups (CScenario.FindActorAt / FindActorsAt), so a token hex is empty for summoning, spawning and
    placing, and the figure sharing it is the one found for targeting. Log `GHRule MapTokenEmptyHex`.
- **Trap damage slots** (Gas Canister top, Volatile Concoction bottom, Proximity Mine bottom, Cryonic Snare top; the
  card prints damage [square]): Frosthaven's own shape, `SquareEnhancements: 1` beside `Damage` inside the `Trap`
  block (the Trapper's Boar Catcher). Left out at first because packcheck called it a dead slot; restored once the lead
  added `Trap` to packcheck's PLUS_ONE. The trap ranges have their square slots too.
- **Enhancement shapes** follow the print (AUTHORING "Enhancement mark shapes"): plain `Enhancements` where the
  converter's default matches, the slot key written directly where it doesn't: Disorienting Flash bottom Heal
  [circle], Crank Bow bottom Heal [square], Dangerous Contraption Shield [diamond+], Cryonic Snare Retaliate
  [diamond+], Disintegration Beam bottom Attack [square], Harsh Stimulants top granted Move and Attack [square],
  Teleportation Pad Teleport [circle], every summon stat [square], the four trap damages [square].
- **Ally-granted round shields/retaliates** (Flamethrower, Dangerous Contraption, Cryonic Snare bottoms): Frosthaven's
  Hold the Line top shape (an ability with `Filter: Ally`, `Range: 1` and `ActiveBonus: Duration: Round`), i.e. given
  at play time, not a round aura (the printed rule; Xipe's and the 1e aura follow adjacency through the round).
- affinities.txt is empty: no element is infused 4+ times (Fire, Light, Dark, Earth, Air, Ice once each).

## Per card

- 01 Potent Potables bottom: Backup Ammunition's BuffTarget shape (`ValidAbilityType: Heal`, slots [1,1,1,0]); Xipe's
  `IsSingleTargetBonus` dropped (in Frosthaven it means "the bonus hits one target", not "single-target abilities
  only"); bonus Vars `ValidateIsSingleTarget: TriggerAbility.OriginalTargetCount == 1` (HIVE Aimed Assault) keep
  multi-target heals (Restorative Mist bottom, Crank Bow bottom) from taking a slot.
- 03 Flamethrower bottom: self Shield 1 (round) + Shield 1 round on every adjacent ally (Hold the Line).
- 04 Disorienting Flash: enhancement marks fixed (Stun diamond, Target square; no slot on Range).
- 08, 09 Reinvigorating Elixir / Restorative Mist: Xipe's Range slots removed (none printed).
- 11 Enhancement Field top: token summon with a persistent BuffAttack range-1 aura, AuraFilter Ally (1e Spirit Banner
  shape, Banner of Strength range keys; untested shape), placed within range 3. Bottom: a round +1 Attack on you and
  on each ally adjacent when played (user decision C: the allies adjacent at play time, as Flamethrower bottom).
- 13 Gas Canister / 14 Volatile Concoction bottom / 21 Proximity Mine bottom / 28 Cryonic Snare top: traps as Xipe and
  the 1e Tinkerer wrote them (gh_port renames the trap model); ranged traps take the Boar Catcher's `Range` +
  `RangeIsBase`.
- 14 Volatile Concoction top: Strengthen one ally range 3, sub-ability `RecoverDiscardedCards: 1` on the same target
  (`UseParentTargets`), level 1 cards only (`Vars: CardLevel: 1`, Metal Mosaic Memory Drive: CAbilityRecoverDiscardedCards
  filters on it).
- 15 Hook Gun top: Attack slot added (printed [diamond]).
- 16 Repulsor Gun bottom: stand-alone Push 2 range 2 (`Filter: Enemy`).
- 18 Teleportation Pad top: two token summons (TeleportationPadID). "You and all allies may treat the two token hexes as
  adjacent during move abilities": GHMapTokens.cs adds a cost-1 link between the two pad hexes (both ways) to every
  move of the Tinkerer or a figure allied to them while both pads stand: Frosthaven's own Deathwalker shadow links
  (CAbilityMove.RefreshShadowNavLinks / ClearShadowNavLinks), which the player's reachable hexes, an ally summon's AI
  path and the move cost all read; monsters never get them. Log `GHRule TeleportPad`. XP 3 / persistent / lost. Bottom: Frosthaven's
  `Teleport2: 4` with a circle slot (Blinkblade's Breakneck Speed); packcheck's old "no ability kind found" warning on it is
  gone since `Teleport2` was added to its KIND_KEYS.
- 19 Stamina Booster: `Discard: [Discard,Permanently Lost]` for the card-stack-with-red-X icon (as Xipe, the 1e file and
  Frosthaven's own cards write it).
- 20 Dangerous Contraption bottom: Shield 1 round on one adjacent ally (Hold the Line shape, Target 1).
- 21 Proximity Mine top: "all enemies adjacent to the target gain Muddle": the 1e Rot Maggots shape as fixed and played
  (Filter Enemy, TreatAsNonSubAbility, AllTargetsAdjacentToParentTarget), not Xipe's inline Damage 0 sub-ability (the
  inline form aimed at the attacked enemy's hex in the 1e Care Package bench).
- 22 Noxious Vials bottom: hex mark moved to 3,1 (right of the top red, as printed; Xipe had -1,1).
- 23 Disintegration Beam top: the 1e kill (Filter Health <= 5, XP per target, diagonal layout override) with four red
  hexes and no hex mark, as printed.
- 24 Invigorating Aerosol bottom: Heal **1** as the image prints (GHS 2, Xipe 3; the sheet's unclear note), Range 5,
  Strengthen + Bless + Ward each end of turn for 5 turns (EndTurnAbility, slots [1,0,1,0,1]). Safeguard: the
  pack's real condition (AUTHORING.md, Safeguard; GHSafeguard.cs) as a sub-ability of the end-of-turn heal
  (`UseParentTargets`), so each heal target also gains Safeguard. **Untested**: a sub-ability inside an AddActiveBonus
  ability. The class's only Safeguard.
- 25 Auto Turret top: lost (Xipe had it as a discard). "Perform the following twice: Attack 2, Range 5, Pierce 1": two
  attacks, each Target 1 with its own modifier draw (the same enemy may be hit twice): the second is a sub-ability
  attack with TreatAsNonSubAbility (Boldening Blow) inside the end-of-turn bonus (Invigorating Aerosol's benched shape). Bottom: Ward [diamond+] range 3
  (self or ally) and Poison [diamond] range 3 restored (Xipe had them commented out).
- 26 Murderous Contraption: Kill Bot as above.
- 27 Pernicious Fogger top: token summon with Frosthaven's Banner of Doom aura shape changed to `EndTurnAbility` and
  `AuraFilter: [Ally, Enemy]` (damage 2 to the figure whose turn ends within range 1; "allies" from the token's side
  include the Tinkerer and their summons, settling the sheet's rules question as the printed "all allies"). **Untested
  shape:** Frosthaven uses StartTurnAbility for this aura; whether EndTurnAbility auras fire on other figures' turn ends
  needs the bench. Bottom: Strengthen slot added (printed [diamond+]).
- 28 Cryonic Snare bottom: Retaliate 2 round on one adjacent ally (slot added, printed [diamond+]).
- 29 Harsh Stimulants (placeholder in Xipe): top = Frosthaven's grant (ControlActor, ControlActorType Player, one ally
  within range 3) commanding Damage 2 on itself (AutoTrigger, CanSkip False: the "!" box), Move 2, Attack 6
  (StrengthIsBase). Bottom = Frosthaven's Hijack control of one enemy within range 3 commanding Attack +2 (relative to
  the monster's attack: no StrengthIsBase), range 1, all targets; then a Stun limited to the enemy just controlled
  (`TargetTargetedByCasterPreviousAbility` + `SpecificAbilityNames: [BControl]`, the Eye of the Void fix). Whose
  enemies the controlled monster attacks is the engine's control rule (the sheet's rules question). **Untested shape.**
- 30 Gravity Bomb top: the GravityBombID token (stand-in model NailSphere) placed within range 4; the card stays active
  while it stands (persistent). At the end of the Tinkerer's turn in a later round (an extra turn the same round is
  not "your next turn"), GHMapTokens.cs queues the token's three abilities (carried in summons/GravityBomb.yml as
  OnDeathAbilities, never run on a death) as the token's own end-of-turn actions: Pull 2 on all enemies within range 2
  (a pull performed by the token is toward it; the token hex is empty, so an enemy can be pulled into it), damage 5 to
  the enemies in its hex (GHTokenHexOnly), damage 1 to those adjacent (GHTokenAdjacentOnly); then the token leaves the
  map and the card goes to the discard pile. Log `GHRule GravityBomb`. Bottom: the
  plain triangle attack, no hex mark (none printed).
- 31 Chimeric Formula (placeholder in Xipe): top Heal 3 + Strengthen + Bless on one ally within range 3, then "the
  target may immediately play up to two cards (one top, one bottom)" = Frosthaven's extra turn (ExtraTurn: BothActions)
  on the character the heal targeted (Bright Aegis's shape: TargetTargetedByCasterPreviousAbility + SpecificAbilityNames
  [THeal]). Bottom: RecoverLostCards 1 whose targets GHTinkerer.cs builds (GHAdjacentToAllyInRange): the characters
  adjacent to an ally (character or summon, not the Tinkerer) within range 3, the Tinkerer included (QUESTIONS.md C);
  the owner picks the card. Log `GHRule ChimericRecover`. `Discard: [Discard,Permanently Lost]`.

## Perks (perks.py)

All 10 perks / 18 checks from the perk sheet. Existing Frosthaven modifiers: AMC_Basic_M2/P0, AMC_Heal1_1Ally,
AMC_Wound_P1, AMC_Poison_P1, AMC_Immobilize_P1. New: `AMC_GH2E_Tinkerer2_Strengthen_Ally_P2` (+2, Strengthen one
ally; Frosthaven's AMC_Strengthen_Ally_P1 at +2). The linked two-box perk (long rest: Heal 2, Range 3) is one perk
with `Cost: 2` (the 1e Rejuvenating Vapor abilities). GHTinkerer.cs reads two passive perks by ID: "Spare Parts"
(Tinkerer209: a card action whose half has a lost icon gets a skippable Infuse [Any] with the element picker at its
end, log `GHRule SpareParts`) and "Shared Supplies" (Tinkerer208: at the start and at the end of an adjacent
character's turn, once per scenario, that player may use one of the Tinkerer's unspent potion small items; its
abilities are performed by that character and the item is not spent, log `GHRule SharedSupplies`; **limit**: offered
at the start and the end of the turn only, not between the two actions).
Masteries: "Perform 11 different lost actions" = the existing LossActionsRepeated tag (Value 11, Amount 1);
"each turn: negative condition on an enemy, positive condition / heal / shield on an ally" = the new tag
TinkererSupportEveryTurn (GHMasteries.TinkererSupportInRound: the 1e Field Medic test plus a Bless / Strengthen / Ward /
Regenerate / Invisible / Shield / Heal record aimed at another character or a summon).

## Shared changes wanted

1. ~~packcheck PLUS_ONE: accept `Trap`~~ **done** by the lead; the four trap damage slots (13 top, 14 bottom,
   21 bottom, 28 top) are restored.
2. ~~packcheck KIND_KEYS: add `Teleport2`~~ **done** (the Teleportation Pad bottom warning no longer appears).
3. ~~Plugin: summon placement range~~ **done in data**: CAbilitySummon reads the ability's Range (Enhancement Field and
   Pernicious Fogger 3, Teleportation Pad 5); bench one placement.
4. ~~Plugin: Teleportation Pad adjacency~~ **done** (GHMapTokens.cs).
5. ~~Plugin: Gravity Bomb~~ **done** (GHMapTokens.cs).
6. ~~Plugin: Chimeric Formula~~ **done** (top: extra turn in data; bottom: GHTinkerer.cs); Safeguard done (GHSafeguard.cs).
7. ~~Plugin: recover a level 1 card~~ **done** (`Vars: CardLevel: 1`).
8. ~~Mastery tag~~ **done** (TinkererSupportEveryTurn).
9. ~~Perk plugin hooks~~ **done** (GHTinkerer.cs; Shared Supplies offered at the start / end of the turn only).

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Tinkerer2` -> **CHECK PASSED Tinkerer2** (31 of 31 cards,
packcheck 0 problems / 0 warnings, rulecheck 49 ok, 6 summon definitions, 10 perks + 2 masteries).

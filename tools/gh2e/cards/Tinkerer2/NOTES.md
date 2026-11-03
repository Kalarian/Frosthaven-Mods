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
  - **Approximation (all three):** a summon is placed adjacent to the Tinkerer, not "in an empty hex within range 3/5".
    Frosthaven has no summon ability with a placement range (none of its cards or the 1e cards has one).
  - Tokens are untargetable, invulnerable, non-blocking summons: whether figures may end a move in a token's hex
    (the class mat says the hex stays empty) is to be seen in game.
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
- 18 Teleportation Pad top: **NEEDS PLUGIN.** "You and all allies may treat the two token hexes as adjacent during
  move abilities" has no engine shape. Written as two token summons (TeleportationPadID, no effect of their own, so a
  plugin can find the two hexes) + the rule as text; XP 3 / persistent / lost as printed. Bottom: Frosthaven's
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
- 25 Auto Turret top: lost (Xipe had it as a discard). "Perform the following twice: Attack 2, Range 5, Pierce 1" is
  **approximated** as one Attack 2 with Target 2 (Xipe's choice): two different enemies, never the same enemy twice
  (a sub-ability attack under an attack inside an AddActiveBonus is an untested shape). Bottom: Ward [diamond+] range 3
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
- 30 Gravity Bomb top: **APPROXIMATION + NEEDS PLUGIN.** The token, the delay to the end of your next turn and the
  pull toward the token have no engine shape. Done at once: Damage 5 on one enemy within range 4, then Damage 1 on
  every enemy adjacent to it (AllTargetsAdjacentToParentTargets + TreatAsNonSubAbility, the Rock Slide / Rot Maggots
  shape). No pull, not persistent (the card goes to the discard as the text says). Text left as printed. Bottom: the
  plain triangle attack, no hex mark (none printed).
- 31 Chimeric Formula (placeholder in Xipe): top Heal 3 + Strengthen + Bless on one ally within range 3. "The target
  may immediately play up to two cards (one top, one bottom)": **NEEDS PLUGIN**, text only (Frosthaven's
  SelectAndPlayCard plays the caster's own cards). Bottom: **approximation** RecoverLostCards 1 for one ally within
  range 3 from their own lost pile (the printed rule: from the lost pile of an ally adjacent to them: NEEDS PLUGIN);
  `Discard: [Discard,Permanently Lost]`.

## Perks (perks.py)

All 10 perks / 18 checks from the perk sheet. Existing Frosthaven modifiers: AMC_Basic_M2/P0, AMC_Heal1_1Ally,
AMC_Wound_P1, AMC_Poison_P1, AMC_Immobilize_P1. New: `AMC_GH2E_Tinkerer2_Strengthen_Ally_P2` (+2, Strengthen one
ally; Frosthaven's AMC_Strengthen_Ally_P1 at +2). The linked two-box perk (long rest: Heal 2, Range 3) is one perk
with `Cost: 2` (the 1e Rejuvenating Vapor abilities). **NEEDS PLUGIN** (passive flag + text only): "Shared Supplies"
(an adjacent ally uses your potion once per scenario) and "Spare Parts" (infuse any element on a lost action).
Masteries: "Perform 11 different lost actions" = the existing LossActionsRepeated tag (Value 11, Amount 1);
"each turn: negative condition on an enemy, positive condition / heal / shield on an ally" = the 1e Field Medic tag
HealOrDebuffEveryTurn, which doesn't count a positive condition or a shield given to an ally (stricter than printed).

## Shared changes wanted

1. ~~packcheck PLUS_ONE: accept `Trap`~~ **done** by the lead; the four trap damage slots (13 top, 14 bottom,
   21 bottom, 28 top) are restored.
2. ~~packcheck KIND_KEYS: add `Teleport2`~~ **done** (the Teleportation Pad bottom warning no longer appears).
3. ~~Plugin: summon placement range~~ **done in data**: CAbilitySummon reads the ability's Range (Enhancement Field and
   Pernicious Fogger 3, Teleportation Pad 5); bench one placement.
4. **Plugin: Teleportation Pad adjacency** (the two TeleportationPadID hexes count as adjacent for moves of the
   Tinkerer and allies).
5. **Plugin: Gravity Bomb** as printed (token, end of next turn: Pull 2 toward it on enemies within 2, damage 5 in
   the hex / 1 adjacent, then discard).
6. **Plugin: Chimeric Formula** (an ally plays one top + one bottom from hand at once; recover from an adjacent ally's
   lost pile) and **Safeguard** for Invigorating Aerosol's end-of-turn heal target.
7. ~~Plugin: recover a level 1 card~~ **done** (`Vars: CardLevel: 1`).
8. **Mastery tag** that also counts a positive condition or shield given to an ally (Field Medic, 2e wording).
9. **Perk plugin hooks**: Shared Supplies, Spare Parts.

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Tinkerer2` -> **CHECK PASSED Tinkerer2** (31 of 31 cards,
packcheck 0 problems / 0 warnings, rulecheck 49 ok, 6 summon definitions, 10 perks + 2 masteries).

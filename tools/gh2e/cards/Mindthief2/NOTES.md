# Mindthief2 (Gloomhaven 2nd edition Mindthief): translation notes

All 29 cards rewritten from `tools/gh2e/sheets/Mindthief.json` (values, marks, XP, lost/persistent/round icons); Xipe's
files were only the starting point. Every text key is our own (`GH2E_Mindthief2_*`); the `GH2E_X_*` keys are gone.
Enhancement marks follow the printed shapes: where the sheet's mark differs from the converter's default (a [square]
on an Attack, Move or Heal: 01, 03, 06, 08, 09, 10, 11, 12, 14, 15, 16, 20, 21, 22, 25, 26, 27 and the summons' Move
and Attack), the file writes `SquareEnhancements` itself.
"Unverified" below = the shape passes packcheck and rulecheck but has not been seen working in the game yet; each is
worth a bench run.

## Cards

- **01 Submissive Affliction** bottom: consume Ice "the controlled enemy suffers 2" = one more commanded ability added by
  the consume's override on the control (`Damage 2, Filter Self, AutoTrigger`: Gloomhaven's own Voidwarden Suggestion
  shape, which adds a commanded Stun the same way). Unverified.
- **02 Hidden in the Shadows** top: Invisible on a kill = Frosthaven's Morbid Camouflage top (sub-ability,
  `ActorsKilledByAbility > 0`); Xipe's AutoTrigger first ability removed (standard confirm). Bottom: "not moved this
  round" = Frosthaven's Giant Sword test (`TargetingActor.TilesMovedThisRound == 0`); "lose Invisible at the end of the
  round" = `Invisible: DecrementTrigger: Rounds` (Frosthaven's Accurate Strikes uses it for a round Advantage; for a
  condition unverified); "you cannot perform move abilities this round" = a round `ImmunityTo: [Move]` on yourself
  (Frosthaven's Giant Sword / Bronze Plating, icon Negative_MovePrevent), only when the Invisible was gained.
- **04 / 06 / 10 / 19 / 25 / 29 augments**: the 1st edition port's `Augment` shapes (Type Melee, AbilityOverrides or
  Abilities); the plugin's GHAugments fix covers them.
- **06 The Mind's Weakness / 10 Psychic Blade** "control the target of the attack ability": control sub-ability with
  `UseParentTargets` (the 1st edition Lead to Slaughter after its CARD_FIXES fix). The controlled Attack 2 keeps its mark.
- **07 Pilfer**: "If this attack does not kill the target, gain a money token": money tokens are loot tokens (Frosthaven
  FAQ section 10), so a sub-ability of the attack with the negated kill requirement (`ActorsKilledByAbility: "== 0"`)
  marked `GHMoneyGain` has the plugin (GHMoneyTokens) give her one loot token (one loot-deck draw). The printed Loot 1
  follows.
- **09 Empathetic Assault**: the Heal keeps its printed mark (Xipe removed it on purpose).
- **10 Psychic Blade** augment: approximated as `Range +1, TreatAsMelee` on every melee attack (Xipe's). The
  "single-target" restriction isn't enforced, and whether an augment override's Range adds (2) or sets (1) is unverified.
  Bottom: round melee-only Curse buff (Frosthaven's Plague Protocol shape: `Attack 0, Conditions [Curse]`, BuffAttack,
  `AttackType: Melee`; Xipe's applied to ranged attacks too).
- **11 Feedback Loop / 12 Possession / 22 Telepathic Command** "grant one of your summons": Frosthaven's Command the
  Wretched shape (`Target 1, Range 99, Filter TargetType OwnSummon`, the summon's own value `+0`), one summon, where Xipe
  granted all of them. The augment on 11 carries that control inside `Augment: Abilities:` (a control inside an augment
  is unprobed). 22's Invisible is the summon's own (`Filter: Self` inside the control, as Frosthaven's Flesh Shield).
  11 bottom = the 1st edition Feedback Loop bottom (`AllTargetsOnMovePathSameStartAndEnd`, parsed, not yet verified).
- **13 / 21 / 28 summons**: see Summons below.
- **15 Hostile Takeover** bottom: `ControlForOneTurn` with `ControlActorType: Player` (the Scoundrel's Long Con shape,
  bench-verified with GHControlTurn.cs), because the 2e card says "with you controlling its abilities" (Xipe and the 1st
  edition used `Ally`, the AI). Whether the engine also swaps the monster's sides for that turn under Player control is
  unverified. Infuse Dark added (Secretariat missed it).
- **16 Brain Leech** top: consume Ice adds Wound to the commanded attack by naming it (`ParentName: EnemyWillDoThis2`;
  Frosthaven's Flesh Shield overrides abilities inside its control the same way). Bottom: the Strengthen needs the attack
  performed (`StartAbilityRequirements: PreviousAbility, AbilityHasHappened`, Frosthaven's Precision Timing); Xipe's was
  unconditional and unmarked.
- **17 Cranium Overload** top: the designated hex is approximated: Attack 6, Pierce 5 on one enemy within range 4 (line
  of sight from you, and it's a ranged attack from you, not from the hex), then if it killed, Attack 2 on every enemy
  adjacent to that enemy's hex (the 1st edition card's follow-up, with Frosthaven's kill requirement). No marks (as
  printed). **NEEDS PLUGIN** for the real designated hex.
- **18 Frozen Shiv**: Pierce 1's [square] mark = Pierce inside the Attack mapping with `PierceEnhancements: 1`
  (Frosthaven's Infuser Gale Barrage; a Pierce mapping of its own fails rulecheck).
- **19 Silent Scream** bottom: the commanded "Attack -1" is relative to the enemy's own attack, in melee (no range
  printed; 2e rulebook p.40, attacks with no range are melee). Decision, not a print question.
- **20 Mass Hysteria** bottom: the 1st edition Corrupting Embrace bottom's shape (Poison along a jump's path, verified).
- **23 Dark Frenzy** bottom: two enemies each perform Move 1; consume Ice = Submissive Affliction's self-damage shape
  (1 each). Unverified (as 01).
- **24 Domination** top: two enemies within range 4 each perform Attack 3; "adjacent to each other" is not enforced
  (approximation; Xipe's `Filter: Ally` + `TransferAugments` was the 1st edition card's top). Bottom: the 1st edition Mass
  Hysteria bottom (`AddAugment: 1`, persistent), icon Positive_Cards; not lost (as printed).
- **25 Psychic Projection** top: Pull 2, range 3 is its own ability before the attack. Bottom: the designated unoccupied
  hex is approximated as Attack 3, Range 5, `TreatAsMelee` (so augments apply), line of sight from you. **NEEDS PLUGIN**
  for the real designated hex.
- **26 Shared Nightmare**: bottom Move 4 keeps its mark (Xipe dropped it).
- **27 Corrupting Embrace**: bottom Move 4 has no mark (Xipe had one); four use slots, XP on 2 and 4, melee only.
- **28 Many as One**: both halves persistent and lost, XP 2 each (Xipe's bottom was a one-round non-lost aura). Bottom
  **NEEDS PLUGIN**: "each round, the first of your allies who performs a melee attack ability gains the effects of each
  of your active augments" has no data shape; the card stays in the active area as an inert persistent bonus
  (`Attack 0` BuffAttack on your melee attacks, `BAugmentShare`) for a plugin to hook.
- **29 Phantasmal Killer** top: first augment = a control on one target of each melee attack (Attack 3, melee,
  `UseParentTargets`). Second augment "after each of your control abilities, you may consume Dark: one controlled enemy
  suffers 3": an augment ability marked `GHAfterControl` (a Consume Dark step holding Damage 3 on one of the enemies the
  control ability controlled, Custom targeting, no range); the plugin GHAfterControl lifts it out of the per-attack
  augment list and stacks it after each control ability of hers that controlled an enemy while the augment is active. The card's Attack 3
  has no mark (Xipe had one).

## Summons (`summons/`)

- **Rat Swarm** (`RatSwarmID`): HP 5, Move 2, Attack 2, Poison on its attacks. Model **PlagueRat** (the 1st edition
  Gnawing Horde summon is the same poison rat, with the poison-attack animation; groups.md had suggested Giant Rat).
- **Rat Monstrosity** (`RatMonstrosityID`): HP 8, Move 3, Attack 3. Model **GiantRat** as the stand-in (a new 2e
  creature; not the RatKing model, so it doesn't look like the Rat King when both are out). "+1 Attack if the target has
  a negative condition" = `AttackOverride` with `AttackEffect: BuffIfAnyConditionsOnTarget` over the negative conditions
  a figure can hold (Poison, Wound, Muddle, Immobilize, Disarm, Stun, Brittle, Bane, Impair; Curse isn't held by a
  figure). Xipe's per-condition buff was wrong. Unverified in a summon's AttackOverride.
- **Rat King** (`RatKing2ID`, the 1st edition has a RatKing with other stats): HP 10, Move 3, Attack X = half its current
  hit points (`StatIsBasedOnX PercentageOfCurrentHP 0.5`, the 1st edition shape). Model RatKing.
- Melee summons carry `Range: 1` as the 1st edition summon files do (not 0).

## Perks (`perks.py`, 10 perks, 18 checks)

- **Mind Tug** (2): new modifier `AMC_GH2E_Mindthief2_ControlMove1` (+0, then control the target: Move 1): Frosthaven's
  AMC_P0_ControlSummonMove2 shape aimed at the attack's target with `Vars: TargetThisActorAutomatically: TargetActor`
  (AMC_PushOrPull2_R). Unverified.
- **Prey on the Weak** (3): new modifier `AMC_GH2E_Mindthief2_P1_PerNegativeCondition` (+1, +1 per negative condition
  on the target): AMC_AddAttack1ForEachAllyAdjToTarget_P1's `StatIsBasedOnX AddTo` with `BasedOn: Vars,
  TargetActor.Tokens.CountNegativeConditions()`. Unverified that `TargetActor` is the attack's target in that context.
- Sharpened Claws (2), Hobbling Bite (1), Cold Snap (3), Vanish (2, AMC_Invisible_R), Unfazed (1: "ignore scenario
  effects" = IgnoreNegativeScenarioEffects + AMC_P1_R): Frosthaven's own modifiers.
- **Puppet Strings** (1): a hidden scenario-long `OverrideAbility` on ControlActor with
  `UseControllingActorModifierDeck` (Gloomhaven's Master Influence data). Always on: the perk says "may". Unverified.
- **Lying Low** (1): text-only passive, **NEEDS PLUGIN** (as the 1st edition's Lying Low).
- **Restless Mind** (2): after each rest, optional consume Ice or Dark (element picker) to control one enemy within
  range 5: Attack 1 (the 1st edition Voidwarden's Grave Defense shape).
- Masteries: only "Thought Thief" (the 1st edition's AugmentsTriggered tag, ActorID Mindthief2ID). "Kill 5 or more
  enemies with control abilities" needs a new GHMasteries tag, so it is left out.

## Affinities

`[ Ice, Dark ]`: Ice infused on 9 halves, Dark on 5 (gh_port agrees: no warning).

## Shared changes wanted

1. **Plugin**: augment sharing with the first ally's melee attack each round (28 Many as
   One bottom); a real
   designated hex for Cranium Overload (17) and Psychic Projection bottom (25); the "two adjacent enemies" restriction
   (24 top); the single-target restriction of Psychic Blade's augment (10); Lying Low (perk); a mastery tag for kills by
   control abilities.
2. **packcheck**: a `Pierce:` mapping (an enhancement on a pierce) could be flagged before rulecheck (18).

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Mindthief2` -> **CHECK PASSED Mindthief2** (29 cards,
packcheck 0 problems / 0 warnings, rulecheck 45 ok, no gh_port warnings, no missing text keys).

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
- **10 Psychic Blade** augment: `Range +1, TreatAsMelee` on her melee attacks (reach 2), single-target only: the override
  carries `MiscVars GHSingleTargetOnly` and the plugin (GHMindthief2.cs SingleTargetAugment) skips it on an attack with
  more than one target, Targeting All or an area. Bottom: round melee-only Curse buff (Frosthaven's Plague Protocol
  shape: `Attack 0, Conditions [Curse]`, BuffAttack, `AttackType: Melee`; Xipe's applied to ranged attacks too).
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
- **17 Cranium Overload** top: the designated hex is Frosthaven's own "perform from a hex" shape (Pyroclast Cloud of Ash,
  Shattersong Resonant Frequency): a NullHex pick of a hex holding an enemy within range 4 (line of sight from her), then
  the attacks performed from that hex (`Vars TargetingOverrideArrayIndex`: range, line of sight and adjacency count from
  the hex, rulebook p21 and decision B). They print no range, so they are melee: Attack 6, Pierce 5 on the enemy in the
  hex (custom targeting), then, if it killed, Attack 2 on every enemy adjacent to the hex (a sub-ability of the attack,
  queued by GHNestedSubAbilities; Frosthaven's kill requirement). No marks (as printed).
- **18 Frozen Shiv**: Pierce 1's [square] mark = Pierce inside the Attack mapping with `PierceEnhancements: 1`
  (Frosthaven's Infuser Gale Barrage; a Pierce mapping of its own fails rulecheck).
- **19 Silent Scream** bottom: the commanded "Attack -1" is relative to the enemy's own attack, in melee (no range
  printed; 2e rulebook p.40, attacks with no range are melee). Decision, not a print question.
- **20 Mass Hysteria** bottom: the 1st edition Corrupting Embrace bottom's shape (Poison along a jump's path, verified).
- **23 Dark Frenzy** bottom: two enemies each perform Move 1; consume Ice = Submissive Affliction's self-damage shape
  (1 each). Unverified (as 01).
- **24 Domination** top: two enemies within range 4 each perform Attack 3; "adjacent to each other" = `MiscVars
  GHTargetsAdjacent` on the control: once one enemy is picked, the plugin (TargetsAdjacent) refuses a second pick that is
  not adjacent to it. Bottom: the 1st edition Mass Hysteria bottom (`AddAugment: 1`, persistent), icon Positive_Cards; not
  lost (as printed). Its two choices are the plugin's (AugmentChoice): a third augment asks which active one is
  discarded (Frosthaven's own slot choice always dropped the oldest), and when the card leaves with two augments active
  she picks the one that goes (asked on the rules thread; from the main thread the engine's oldest-first trim stays).
- **25 Psychic Projection** top: Pull 2, range 3 is its own ability before the attack. Bottom: the designated
  unoccupied hex is a NullHex pick within range 4 (line of sight from her); the melee Attack 3 is performed as though she
  stood there (`Vars TargetingOverrideArrayIndex`, as Cranium Overload): range 1, line of sight and adjacency from that
  hex; her melee augments apply (it is her melee attack).
- **26 Shared Nightmare**: bottom Move 4 keeps its mark (Xipe dropped it).
- **27 Corrupting Embrace**: bottom Move 4 has no mark (Xipe had one); four use slots, XP on 2 and 4, melee only.
- **28 Many as One**: both halves persistent and lost, XP 2 each (Xipe's bottom was a one-round non-lost aura). Bottom:
  the marker bonus `BAugmentShare` (`MiscVars GHAugmentShare`); the plugin (AugmentShare) gives her active augments to the
  first melee attack ability of one of her allies each round (summons included, a controlled enemy not), the way the
  engine's TransferAugments gives them to a controlled figure, and takes them back when that attack ends.
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
  `UseControllingActorModifierDeck` (Gloomhaven's Master Influence data); "may": the plugin (PuppetStrings) asks before
  each controlled action with an attack whether the enemy draws from her deck or its own.
- **Lying Low** (1): Frosthaven's Flea-Bitten Shawl shape (AdjustInitiative with the FocusInitiative behaviour, read
  only by the monsters' focus sort), at 100: after every initiative a card can have.
- **Restless Mind** (2): after each rest, optional consume Ice or Dark (element picker) to control one enemy within
  range 5: Attack 1 (the 1st edition Voidwarden's Grave Defense shape).
- Masteries: 1 "Puppet Master" (kill 5 enemies with control abilities): the new tag FrosthavenMods_KillsByControl; the
  plugin counts enemies killed by abilities she controlled (`ControlledByClassID`) during the scenario (in memory: a
  reload in the middle of a scenario starts the count again). 2 "Thought Thief" (the 1st edition's AugmentsTriggered
  tag, ActorID Mindthief2ID).

## Affinities

`[ Ice, Dark ]`: Ice infused on 9 halves, Dark on 5 (gh_port agrees: no warning).

## Shared changes wanted

1. Done 2026-11-02 (2e card-accuracy pass, task wildfury-mindthief): augment sharing (28), the designated hex (17, 25,
   data only), adjacent control targets (24 top), the augment choices (24 bottom), the single-target augment (10), Lying
   Low (data), Puppet Strings' choice and the control-kills mastery tag (plugin GHMindthief2.cs).

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Mindthief2` -> **CHECK PASSED Mindthief2** (29 cards,
packcheck 0 problems / 0 warnings, rulecheck 45 ok, no gh_port warnings, no missing text keys).

# Nightshroud (2nd edition): translation notes

All 28 cards written from scratch from `tools/gh2e/sheets/Nightshroud.json` (no Xipe data; Secretariat only as a
cross-check). Shapes come from Frosthaven's own cards (Deathwalker shadows above all, Deepwraith, Banner Spear,
Shattersong, Infuser, Boneshaper, Crashing Tide) and the 1st edition port. Every enhancement mark is written as its slot
key; marks, lost icons and XP were compared with the sheet by script: all match except the one noted below (22). Each card file's header comment names the shape it copies.

## The two class systems

### Tear tokens = Deathwalker shadows (APPROXIMATION, NEEDS PLUGIN for the rest)

A Tear is Frosthaven's Deathwalker shadow (`CObjectShadow`, placed by `CreateShadow`): a prop in a hex that leaves the
hex empty and walkable, exactly the class mat's "hexes with only a Tear in them are empty and not negative". Engine
reading (ScenarioRuleLibrary, decompiled): `CAbilityCreateShadow` works for any player (only the Deathwalker's
shadow-marked-enemy death hook and `GetShadowTilesInRange(excludeUsed)` look for `DeathwalkerID`). Shapes used:

- "place a Tear within range N": `CreateShadow` + Range N, Target 1 (Deathwalker Eclipse / Call to the Abyss).
- "a Tear adjacent to the target / ally": `CreateShadow`, Targeting Custom on
  `CustomAbility.GetAdjacentTiles(<ability>.ActorsToTarget)` with `CustomAbility.FindAbilityInPhaseByName` (the
  Cragheart2 Dig Pit / Infuser Onyx Shards way).
- "a Tear in the killed enemy's hex": a DuringActionAbilityOnDeath aura placing a shadow on `TriggerActor.Tile`
  (Deathwalker When Your Time Comes bottom).
- "Teleport must end on a Tear": `Teleport2` with `MiscVars: IsValidTeleportTile: CustomAbility.SteppedOnShadow(Tile)`
  (the Crashing Tide Sodden Soil form).
- "on a Tear" tests: `CustomAbility.SteppedOnShadow(x.Tile)`; removing a Tear: `RemoveShadow` on a hex (Fluid Night).

What differs:
- **Limit**: the engine caps shadows at 5 (hard-coded in `CAbilityCreateShadow`, the player removes extras), the class
  mat at 2. A Deathwalker in the same party shares the pool. NEEDS PLUGIN (cap 2 for Nightshroud2ID).
- **Looks**: Tears look and are named like shadows (the help box says "place shadows":
  `FH/GUI_PLACE_SHADOWS_HELPBOX`; the card texts say "Tear").
- **Tear hexes in area diagrams** (01, 07, 17, 19, 26, 28): the engine can't aim a placement at one hex of a rotated
  area, so each Tear hex is a separate "you may place one Tear within range N", N = that hex's distance from you (2,
  or 1 for Into the Night); 28 places two. A text line on the card (`GH2E_Nightshroud2_TEARHEX*`) says so; the area
  diagram shows no Tear hex.
- **Class-mat Tear rule** "whenever you enter a hex with a Tear during an ability, you may remove it to lose Invisible
  at the end of the ability": NEEDS PLUGIN (see Invisible).

### Invisible as a permanent class state: NEEDS PLUGIN (no data shape)

Not in the data at all: no card is in play from the scenario start, and Frosthaven has no "can't attack while
Invisible" restriction. Today the class plays as a normal class: it is not Invisible at the start, can always attack,
and loses Invisible as usual. The plugin would need, for Nightshroud2ID:
1. Invisible at scenario start, not removed at the end of its turns;
2. no attack abilities and no door opening while Invisible (unless the Swallowed by Night bottom marker is active);
3. entering a Tear hex during an ability: may remove the Tear to lose Invisible at the end of that ability (this is
   also the trigger Prepare for the Kill waits for);
4. at the end of each round in which it attacked: gain Invisible (unless the Shadowstrike bottom marker is active);
5. an enemy ending its move in its hex: teleport to the closest empty hex + hazardous terrain damage;
6. when all other characters are exhausted or have escaped: lose Invisible for good.

The cards' own "if you have / don't have Invisible" branches are in the data (`TargetingActor.Tokens.HasKey(
CCondition.EPositiveCondition.Invisible)`, the Deepwraith's test): Seeping Tendrils, Claws of the Night, Swallowed by
Night. They work now, but without rule 1 the Nightshroud is rarely Invisible, so the "not Invisible" branches fire
almost always. Two markers exist for the plugin to read: Shadowstrike bottom's consume (a round NullTargeting bonus,
list text `GH2E_Nightshroud2_26_B`) and Swallowed by Night bottom (a persistent bonus with 5 round slots).

## Decisions per card

- **01 Spirit of the Night / 07 Silent Force / 24 Concealed Dominance**: area conditions (the 1st edition Soothsinger's
  Singing Arrow / Provoke Terror shape, melee area from G). 24's consume swaps the AreaEffect (the 1st edition
  Concealed Dominance override). Coordinates are the sheet's (y up, as the Bruiser's areas).
- **02 Slice Through** top: one-use kill trigger (XPTracker [0] = "discard this card"); the bonus Vars accept only a
  death caused by your own ability (TriggerAbility.TargetingActor == the caster). Bottom: consume Teleport2 4 first, then
  the 1st edition Swap (first target you, second an ally) at Range 8 with `IgnoreLOS` ("within eight hexes").
- **03 Eyes of the Night / 22 Dark Cloud** reveal: Shattersong's `CustomAbility.ShowTopMonsterCards(Targets)` on all
  enemies within range (NullTargeting, Target -1, `PerformVars: ApplyToActors`). Unverified on a top-level ability
  (Frosthaven uses it in sub-abilities); with no enemy in range it does nothing (the printed rule says the same).
- **04 Armor of the Night** top: PreventDamage 99 + PreventFirstDamage on one ally in range 3, one use (persistent):
  the active-bonus icon on the ally stands for the character token. Unverified: PreventFirstDamage placed on an ally.
- **05 Swallowed by Fear** top: a Kill on the attack's target when it is alive with 2 or fewer HP (normal, elite and
  boss: the card names no exception, user decision A9).
- **06 Dancing Shadows** top: the Geminate Voice of Salvation enemy aura (Attack -1, range 2, round).
- **08 Black Knives** top: "+1 Attack on the second attack if the first kills": text only, **NEEDS PLUGIN**.
- **09 Enervating Wound** bottom: a one-use death aura on enemies within range 2; the Heal 2 goes to
  `TriggerAbility.TargetingActor` (the killer, you or an ally; TriggerAbility is the cause of death in
  SetupTriggerContext). The bonus Vars let a death spend the card only when that killer is missing hit points.
- **10 Nightfall** top: Muddle + Curse when the pushed target **ends** the push on a Tear (a Tear passed through isn't
  seen); the hazardous terrain damage is text only (**NEEDS PLUGIN**: a scenario-level value and a push-path test).
- **11 Empowering Void** top: Deepwraith Crippling Terror's aura (BasedOnVarsCondition, distance to
  `BespokeBehaviour.m_Actor` <= 1).
- **12 Marked for Death** top: a persistent `BuffIncomingAttacks` bonus on the enemy (+2 Attack, advantage, only from
  Nightshroud2ID: the 1st edition Doomstalker's doom ability without the doom wrapper), one use. Unverified: an Attack
  bonus with an Enemy filter is placed on the enemy, not performed as an attack; fallback = the doom form (AddDoom).
  Bottom: Teleport must end on a Tear, with its printed square mark (as Frosthaven's Deepwraith Skewer the Flesh).
- **13 Unseen Dread** top: Banner Spear Set for the Charge's moved aura (range 2, Wound) without its
  NotInRangeToInRangeMove flag (a move inside the zone counts too: user decision C); consume = a round
  EndRoundAbility Damage 1 on every wounded enemy (Frozen in Fear targeting). Unverified that a round-duration
  EndRoundAbility fires before the round bonus expires.
- **14 Shroud in Shade**: Swift Pivot's one-use round prevent; disadvantage on you, consume adds an ally aura (range 2).
- **15 Terror Blade** top: Damage 2 on the target when no figure allied to it is adjacent after the push (Crashing Tide
  Powerful Pincer's adjacency test). Bottom: two Tears in range 3, the second from a custom tile list that leaves out the
  first Tear's hex and its neighbours; Teleport2 3 onto a Tear (with the two-Tear limit only the new ones remain), then "double the attack value" = the 1st edition Empowering Void's
  scalar buff (Attack 2, AbilityStrengthIsScalar) with two XP slots.
- **16 Prepare for the Kill** top: no trigger on removing a shadow exists: a once-only DuringTurnAbility toggle (the
  1st edition Wings of the Night fix shape): Strengthen + Damage 2 on adjacent enemies. **NEEDS PLUGIN** for the
  trigger (fire it when the Nightshroud removes a Tear).
- **17 Lurking Ruin** top: advantage aura vs poisoned targets (Barbaric Yawp + Crippling Terror's condition test).
  Bottom: Heal 1 for you/allies after each attack ability (DuringActionAbilityOnAttackAbilityFinished aura) whose
  targets include an enemy within 2 hexes of you (bonus Vars on TriggerAbility.ActorsTargeted).
- **18 Cursed Blades / 23 Messenger of Death** bottom: the consume gives a separate attack (no "+"). 23 top: plain Move
  then its own Attack (Trample fix).
- **19 Into the Night** top: the stun's targets gain Invisible (sub-ability, UseParentTargets, Filter Enemy).
  Unverified that a positive condition lands on an enemy this way. Bottom: grant (ControlActor) Teleport2 8 ending on a
  Tear, with its printed square mark.
- **20 Seeping Tendrils** top: Invisible / not-Invisible branches (see above). Bottom consume: Damage 1 to all adjacent
  enemies performed *before* the push (after it they aren't adjacent; same targets, same result).
- **21 Claws of the Night** top: APPROXIMATION: the delayed Attack 5 fires at the **start** of your next turn
  (StartTurnAbility), since an end-of-turn bonus would fire at the end of this turn already; skipped while Invisible
  (ActiveBonus Requirements). Bottom: Slice Through's kill trigger for the round, one use.
- **22 Dark Cloud** bottom: Safeguard [diamond+], self: the pack's real Safeguard condition since 2026-11-02
  (AUTHORING.md, Safeguard; plugin GHSafeguard.cs): a Ward carrier tagged `GH2ESafeguard`, which the plugin turns into
  the Safeguard token (a status-row icon; it blocks the next negative condition by itself, then ends; no stacking; the
  card is not kept active). The carrier holds the printed diamond+ mark.
- **21 Claws of the Night** top: the delayed Attack 5 at the **end** of your next turn (EndTurnAbility; the bonus
  Requirements skip the turn it is played, `CheckingBonus.InternalRoundNumber > 0`, and any turn you are Invisible).
  Bottom: Slice Through's kill trigger for the round, one use, your kills only.
- **22 Dark Cloud** bottom: Safeguard = the AUTHORING recipe (GenericOnNegativeCondition + PreventCondition,
  TriggerOnCaster, ValidateIsCaster, one prevention via XPTracker [0], list text `GH2E_SAFEGUARD`): the data stand-in
  until the plugin has a real Safeguard. **Mark**: the printed diamond+ on Safeguard is dropped (the bonus has no
  condition value to enhance).
- **25 Prey on the Weak** top: a second Attack 4 on the first attack's target when 2 x HP <= max HP (= HP <= half max
  rounded down), its XP 1 as AbilityXP.
- **26 Shadowstrike** top: area damage = NullTargeting area + Damage sub-ability on `ActorsOnRedHexes` (Banner Spear
  Boldening Blow; Damage can't take an area); the Teleport to the Tear hex = a Tear within range 2, then Teleport2 2
  onto a Tear (APPROXIMATION); consume = `SelectAndPlayCard` from the hand (Blinkblade Sand in the Hourglass).
  Unverified: where the played card goes afterwards. Bottom: Ward + Strengthen as one ability; consume = the marker.
- **27 Swallowed by Night** top: a Kill on the stun's target (normal only) when you aren't Invisible and it stands on a
  Tear; optional; removes that Tear and gives you Invisible; XP 1 (AbilityXP). Bottom: persistent marker, 5 slots
  advanced by EndRoundAbility (XP 1, 3, 5). "Exhausted when this card moves to the lost pile": text only, **NEEDS
  PLUGIN** (no card-leaves-active-area trigger; ExhaustSelf exists only as an ability flag).
- **28 Voice of the Night** top: NullTargeting area (Filter Ally) whose sub-ability grants Teleport2 3 to allies on the
  red hexes; two Tears within range 2. Bottom: Push 3 on an ally or enemy; APPROXIMATION: if the pushed figure ends on
  a Tear, you may give it Invisible, which removes the Tear.
- Every half's first ability waits for the standard confirm (no AutoTrigger). Texts follow the "self is default" rule.

## Perks (perks.py)

- 3 Shadow Hop: new rolling `AMC_GH2E_Nightshroud2_Teleport2_R`; APPROXIMATION: the Teleport 2 is yours only (not
  "one adjacent ally or self").
- 4 Calculated Risk: the null is `AMC_Basic_T0`, replaced by `AMC_Basic_M2_Shuffle`.
- 5 Lingering Doom: new `AMC_GH2E_Nightshroud2_KillCurse_P1` (+1, AddModifiersToMonsterDeck Curse when
  `TargetActor.IsDead`). Unverified: if the modifier's ability is checked before the damage lands, the curse never comes.
- 7 Torn Veil: new `AMC_GH2E_Nightshroud2_Tear_P0` (+0, a shadow adjacent to `TargetActor`). Unverified context var.
- 9 Restful Night: the Elementalist's long-rest infusion, Dark, offered (no AutoTrigger) so it may be skipped.
- 10 Rift Opener (linked, 2): gh_crossover's once-each-scenario toggle with a CreateShadow range 1.
- 11 Hexed Retribution (linked, 3): the 1st edition Baneful Hex's `DamageOnCurseModApplied` Damage 3 bonus for the
  whole scenario (a drawn curse always zeroes the attack, so this matches the rule).
- Masteries: Child of Darkness = `FrosthavenMods_ElementEveryTurn` (Dark). Relentless Shadow = `FrosthavenMods_DamageStreak`
  Value 5, Amount 1, Targets 0: APPROXIMATION (damage dealt each round for "an attack ability performed").
- `affinities.txt`: `[ Dark ]` (17 of 28 cards infuse Dark; gh_port agrees).

## Shared changes wanted

- **packcheck.py**: done by the lead (Teleport2 in PLUS_ONE; CreateShadow / RemoveShadow in KIND_KEYS); 12 and 19
  carry their printed squares again.
- **Plugin (NEEDS PLUGIN rules)**: the Invisible class state (rules 1-6 above, with the Shadowstrike and Swallowed by
  Night markers); the Tear cap of 2 (shadow cap is 5) and the enter-a-Tear removal; Prepare for the Kill's
  "on removing a Tear" trigger; Black Knives' second-target +1 after a kill; Nightfall's hazardous damage and push-path
  Tear test; Swallowed by Night's exhaust when it goes to the lost pile; a real Safeguard condition.
- **Looks**: a Tear help box text and, ideally, a Tear look for Nightshroud2ID's shadows (now Deathwalker shadows).

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Nightshroud2`: **CHECK PASSED Nightshroud2** (28 of 28 cards,
packcheck 0 problems / 0 warnings, rulecheck 43 ok, no gh_port warnings, no missing text keys).
- Perk Lingering Doom: modifier rewritten, `AMC_GH2E_Nightshroud2_KillCurse_P1` is now +1 with a self NullTargeting (Frosthaven's AMC_Resonance_P1) whose OnAbilityEnded PerformVars (as AMC_ShowNextAbilityCard_P0) calls the game's own `CustomAbility.AddCurseModifiersToEnemyMonsterDecks(1)` (Pain Conduit Redemption) only when the attack's target `IsDead` (an AddModifiersToMonsterDeck ability on a modifier card stops the game when drawn). Exact; bench: the curse comes on a kill and not otherwise.
- Perk Torn Veil: modifier rewritten, `AMC_GH2E_Nightshroud2_Tear_P0` is now +0 with a top-level Choose (Frosthaven's AMC_PushOrPull2_R) whose one option is the CreateShadow adjacent to the target (a CreateShadow ability on a modifier card stops the game when drawn; a Choose's chosen option runs as an ordinary ability, as AMC_ImproveTrapDamage2OrHeal2_P0's Null options). The Tear can be skipped (printed: mandatory). Help text GH2E_Nightshroud2_TEAR_HELP. Bench it.
- 2026-11-01: the perk modifiers built on a Choose inside a modifier card are plain Frosthaven cards for now (NEEDS PLUGIN): Sedation's self-Disarm option stopped the game ('Invalid targeting type', 2e rerun 10:47), and the same untested shape was in every Choose modifier. Each perk now gives only its number, never worse than not taking it.

## 2026-11-02: modifier abilities and Safeguard

- Perks (2026-11-02, supersede the lines above): Shadow Hop grants Teleport 2 to you or an adjacent ally (ControlActor, Banner Spear Explosive Epicenter shape); Torn Veil is the +0 Tear card (CreateShadow adjacent to the target, run after the attack by GHModifierAbilities.cs, no Choose; mandatory as printed). Bench both.

## 2026-11-02 (night): the class mat and the Tears are built (supersede the NEEDS PLUGIN / APPROXIMATION lines above)

Plugin files `GHNightshroud.cs` (the Invisible half of the mat) and `GHTears.cs` (Tears and the cards that read them);
both act only for Nightshroud2ID and her card IDs. Their summary comments hold the details; in short:

- **Invisible**: Invisible at scenario start; her Invisible never runs out at the end of her turns (Never trigger); no
  attack abilities while Invisible (cancelled when performed) unless Swallowed by Night's bottom bonus is active; closed
  doors are walls and enemies don't block her path while Invisible (she can't end on them); enemies may end their move
  in her hex (AI), she then gets a forced Teleport to the closest empty hex (ties: first in map order) and hazardous
  terrain damage; gains Invisible at the end of a round in which an attack of hers happened (not when Shadowstrike's
  consumed bottom was the only one); loses it and can't regain it once no other character is left (a solo Nightshroud
  included: the printed rule taken literally).
- **Tears**: owned shadows; her cap is 2 (the game's own remove-extras choice), a Deathwalker's 5 without her Tears.
  Entering one of her Tears during her own move (a jump: last hex; a teleport: landing hex) interrupts the move with an
  optional RemoveShadow ("may"); removed, Invisible goes just before her next ability or at her turn's end (FAQ: the end
  of the move/teleport). Offered only while Invisible or with Prepare for the Kill active.
- **Prepare for the Kill** (16): now a plain persistent marker; every removal of her Tears by her own ability (limit
  removals included, the approved default) offers Strengthen (skippable); taken: the card's bonus ends and adjacent
  enemies suffer Damage 2.
- **Tear hexes** (01, 07, 17, 19, 26, 28): blue hexes in the area (`x,y,B`, tag GH2ETearHexArea), so they turn with
  it; the placement (Custom targeting, tag GH2ETearHex) may use only those hexes and only when the area's ability was
  performed (FAQ); the area needs no ally in its blue hex and doesn't touch a figure there. Shadowstrike's teleport
  (tag GH2ETearHexTeleport) lands only on its Tear hex. Card text: "Blue hex: Tear hex. You may place one Tear in that
  hex." (`GH2E_Nightshroud2_TEARHEXB`).
- **Push paths** (10 Nightfall top, 28 Voice bottom): the pushed figure's temp vars `GH2EPushTear` /
  `GH2EPushTearTiles` say whether (and where first) the push entered one of her Tears; Nightfall adds the hazardous
  terrain damage (StatIsBasedOnX ScenarioHazardousTerrainDamage).
- **Black Knives** (08): +1 Attack on the second target when the first died (worked through the modifier's math).
- **Swallowed by Night** (27) bottom: exhausted when the card goes from the active area to the lost pile.
- **Relentless Shadow** mastery: `FrosthavenMods_AttackStreak` (rounds in a row with an attack ability that happened).
- Help boxes: `GH2E_Nightshroud2_TEAR_PLACE_HELP` ("Place one Tear") replaces the shadow text on every placement.
- Still open: the Tear look (Deathwalker shadow, the user's decision A5); enemies' teleports onto her hex and pushes of
  her through enemies follow the game's rules; entering a Tear through someone else's push doesn't offer the removal.

# Wildfury (2nd edition): translation notes

29 cards written from scratch against `tools/gh2e/sheets/Wildfury.json` (no Xipe or Secretariat card data exists for
this class), values, marks, lost/round/persistent, XP and text rows from the sheet. Model: the 1st edition Beast
Tyrant (class template), whose play-tested converted cards (`mods/gloomhaven/files/Global/Characters/BeastTyrant/`)
gave most shapes. Layouts are plain Gloomhaven style (gh_layout redraws them). Every printed rule has its own text key
`GH2E_Wildfury_NN_T/B` in strings.tsv. Nothing here has been run in the game.

## Class-wide decisions

- **The Bear** (`summons/WildfuryBear.yml`, ID `WildfuryBearID`, in `companion.txt`): HP 10..26 (8 + 2 x level),
  Move 3, Attack 2, Range 1 (melee, as the 1st edition bear file), Model `BeastTyrantBear`. Same stats as the 1st
  edition bear; its own ID keeps the editions apart. The engine's companion rules (summoned before round 1, acts
  before you, lose cards to negate its damage) come with `CompanionSummonData`, as for the Beast Tyrant.
- **COMMAND halves**: the 1st edition shape, a `ControlActor` on `Filter: Companion` (Target -1, Targeting All,
  ControlActorType Player) holding the Bear's abilities with `StrengthIsBase`/`RangeIsBase`; layout `- Command: True`
  (gh_layout's `_commands` turns it into the plugin's "Command" tag). Abilities that only buff the Bear sit outside the
  control as the Wildfury's own aura on the Bear (`AuraFilter: TargetType [OwnSummon], HeroSummonClasses
  [WildfuryBearID]`, Boneshaper Solid Bones' filter; the 1st edition Punch Through bottom's aura).
- **"You" on a Command half = the Bear** (the sheet's open question): Energizing Strike / Blood Hunger "if you
  performed the attack ability ... self", Punch Through bottom "your attacks", Blood Hunger bottom "you neither
  attacked nor were attacked". Unleashed Fury's "your ability cards" is the Wildfury's cards (their bonuses on the
  Bear's attack). Disorienting Roar's "within Range 2" and Spirit Swap's "adjacent" are measured from the Bear.
- **"If you performed the attack ability, perform:"**: `StartAbilityRequirements ActorHasPerformedValidAbilityTypesThisAction
  > 0, AbilityTypes [Attack]` (Frosthaven's Drifter Consume Stamina), inside the control (the Bear's action).
- **Required elements** ("! consume X:" at a half's top, the mat's "cannot perform without the element"): Frosthaven's
  own required-consume shape, Infuser Propulsive Tailwind / Boon of the Tempest: a `Consume` ability (a Null inside),
  then the half's real ability with `StartAbilityRequirements ActorHasPerformedValidAbilityTypesThisAction > 0,
  AbilityTypes [Consume]`. The summon cards' XP sits on the Consume (no element, no XP). "Air and Earth" =
  `Elements: [Air, Earth]` (both, as Infuser's `[Air, Any]`).
- **Split Air/Earth consumes** (one pill, either element):
  - as a bonus on an ability (Ancient Ward bottom): Geminate's pair of consume entries with `ConsumeOnlyOneElement`.
  - as a separate or required effect (Spirit Swap top, Invocation of Spirit top, perk 10): two `Consume` abilities,
    Earth first with a start requirement "Air is not infused" (`CustomAbility.IsElementInfused`, Crashing Tide Powerful
    Pincer's expression), then Air. At most one element is spent; when both are infused Air is always the one taken
    (APPROXIMATION: the player can't pick Earth then). **Untested** gating.
- **Split infusions** (Unbreakable Bond top, Spirit Sigil bottom, "!" = can't be skipped): an Infuse ability with
  `[Air, Earth]` + `ShowElementPicker` + `CanSkip: False`.
- **Invocations** ("after each of the Bear's next N attacks, triggering no more than once each round"): the 1st edition
  Blood Hunger top / Frosthaven Infuser Propulsive Tailwind shape: a persistent `DuringActionAbilityOnAttackAbilityFinished`
  aura on the Bear (`ValidAbilityTypes [Attack]`) whose `AddActiveBonus` the Bear performs, plus `Restriction:
  OncePerRound`. Use slots: `XPTracker` one entry per slot, XP on the dashed first slot ([1,0,0], Domination
  [1,0,0,0]); N slots = N triggers (AUTHORING.md / 2e rulebook p.28). **Untested:** OncePerRound on this behaviour
  (Frosthaven uses it on toggles and banners).
- **Safeguard** (Nature's Gift top, Ancient Ward bottom, Spirit Sigil bottom): the pack's real Safeguard condition
  since 2026-11-02 (AUTHORING.md, Safeguard; plugin GHSafeguard.cs): a Ward carrier tagged `GH2ESafeguard`, which the
  plugin turns into the Safeguard token (a status-row icon; it blocks the next negative condition by itself, then
  ends; no stacking; the card is not kept active). A sub-ability with `UseParentTargets` on the heal/bless.
- **Summons** (immobile aura sigils, Move/Attack/Range 0, persistent, not lost), Frosthaven's banner auras:
  - Wind Idol (`WindIdolID`, model stand-in `WindTotem`, the 1st edition Storm Sigil's summon): Pierce 1 aura range 3
    (Banner of Strength range aura + Boneshaper Solid Bones' `AttackEffect StandardBuff Pierce`).
  - Monolith (`Monolith2ID`: the 1st edition has a Monolith with other stats; model `Monolith`): Shield 1 aura range 3
    (Banner of Courage's BuffShield).
  - Vital Force (`VitalForceID`, model stand-in `SpiritBanner`, the 1st edition Tribal Sigil's +2 Attack banner): +2 on
    the first attack each round, range 3, Banner of Strength's single-target auto-toggled bonus with `OncePerTurn` +
    `RestrictPerActor` (= once a round for figures with one turn; under Unbreakable Bond the plugin keeps the Bear's use
    from its first turn for its second, GHUnbreakableBond.cs).
- affinities.txt `[ Air, Earth ]`: Air infused 6 times (Howling Bolts, Focused Aggression, Spirit Swap, Forest's
  Blessing x2, Invocation of Absorption), Earth 6 (Howling Bolts, Nature's Gift x2, Earthen Spikes, Forest's Blessing,
  Invocation of Domination); the 1st edition had `[ Earth ]`. gh_port agrees (no warning).
- No `$Self$` rows and no ", self" in texts (house rule).

## Per card

- 01 Concentrated Rage: the bottom's damage tokens are the Wildfury's character resource `WildfuryRage` (resources.yml,
  pack-wide: saved with the scenario, on her status row); the card keeps a marker bonus (Attack +0 aura on the Bear,
  `MiscVars GHRageTokens`). Plugin GHWildfury.cs RageTokens: +1 token per Bear attack (each target is an attack, 2e) and
  per Retaliate damage the Bear suffers; a Bear attack with 3+ tokens removes three for +3 Attack instead; the tokens go
  when the card's bonus ends.
- 03 Howling Bolts: bottom infuse Earth is the Wildfury's (half level) while the Bear heals.
- 05 Focused Aggression: grant Shield 1 (round) to one adjacent ally (1st edition Maul bottom). "If the target is the
  Bear, also Retaliate 1": a Retaliate sub-ability with `UseParentTargets` filtered to the Bear. **Untested**: whether
  a mismatched filter simply drops it (intended) or asks for another target.
- 06 Gnaw: Loot 1 performed by the Bear (1st edition Soaring Ally's commanded loot). "The Wildfury gains all looted
  tokens" is text; the engine credits a summon's loot to its summoner (TEST).
- 07 Disorienting Roar top: Muddle all within 2 of the Bear, then a round -1 Attack aura (`AuraFilter [Enemy]`, range 2)
  on the Bear, applying only to a muddled attacker (AttackEffect `BasedOnVarsCondition` -1 with the holder's Muddle
  test, Deepwraith Crippling Terror); it follows the Bear. **Untested** enemy
  BuffAttack aura from a commanded ability (fallback: text only). Bottom: invocation Move 3, Jump.
- 09 Spirit Swap top: the teleport is the Wildfury's own `Teleport2: 99` limited to hexes adjacent to the Bear
  (`MiscVars IsValidTeleportTile: Bear.GetDistanceTo(tile) == 1`, `Bear` from `AllHeroSummons.Find(obj.Class.ID ==
  "WildfuryBearID")`: HIVE Recall's / Infuser Formless Bladestorm's / Boneshaper's expressions combined), between the
  Bear's commanded move and attack (the command is two ControlActor steps around it, as printed). **Untested
  expression.** Bottom:
  the 1st edition Swap with `EnemyType [Normal, Elite]`.
- 10 Invocation of Endurance: invocation Heal 2 on the Bear. Bottom Move 3 [square] + Jump (Move map `Jump: True`).
- 11 Invocation of Spirit top: the trigger's Attack 2, range 3 is marked `GHBySummonerFromHex`; the plugin
  (BySummonerFromHex) stacks it for the Wildfury instead of the Bear, with Frosthaven's own `TargetingOverrideArrayIndex`
  set to the Bear's hex: her attack (her deck, her bonuses), range and line of sight from the Bear (rulebook p21).
  Bottom: "+1 Heal if the target is the Bear" is a target conditional override filtered to the Bear; Frosthaven reads
  per-target overrides only on attacks, so the plugin (TargetHealBonus) adds it to a pack heal on a matching figure.
- 14 Energizing Strike bottom: Heal 1 on all allies, no range (the 1st edition card; "allies" excludes the Wildfury).
- 16 Punch Through bottom: +1 on all the Bear's attacks this round (the 1st edition aura without `AttackType: Control`).
- 17 Stone Sigil bottom: "if you are adjacent to the Bear" = start requirement `AllHeroSummons.Any(obj.Class.ID ==
  "WildfuryBearID" && obj.IsAdjacentTo(TargetingActor))`. **Untested expression.**
- 21 Invocation of Domination top: **untested shape.** Per-target trigger (`DuringActionAbilityOnAttackFinished`, as
  Drifter Shockwave) on the Bear; the added ability is a `ControlActor` on `TriggerTarget` (Pyroclast Feed the Beast's
  custom targeting) with HIVE Hijack's enemy Move 2 / Attack 2, skipped when the target died. Bottom: grant Shield 1
  [square] (round) to an ally within 4; Poison every enemy adjacent to it (`AllTargetsAdjacentToParentTargets` +
  `TreatAsNonSubAbility`, CARD_FIXES Care Package / Virulent Strain).
- 22 Blood Hunger bottom: the persistent Shield 1 (BuffShield aura) + Retaliate 2 (Behaviour Retaliate aura) on the
  Bear; the "!" rule is the plugin's (LoseIfIdle, marker `GHLoseIfIdle` on BShield): at the end of a round in which the
  Bear neither attacked nor was attacked (or is gone), the card's bonuses finish and the card goes to the lost pile.
- 24 Invocation of Absorption top: the Bear's Damage 1 on all enemies within range 2 with a Heal sub-ability valued
  by `StatIsBasedOnX ActorsTargetedByParentAbility` (Blinkblade The Knife's Edge). **Untested** nested inside an
  AddActiveBonus; fallback: a Heal valued by a Vars count of enemies in range 2.
- 25 Unleashed Fury top: the commanded attack is marked `GHDoubleCardBonuses`; the plugin (UnleashedFury) adds every
  positive Attack bonus from her ability cards on that attack a second time (Punch Through bottom; Concentrated Rage's +3
  becomes +6). Not the Vital Force aura (the summon's, decision C), not modifiers. With the stacking house rule off only
  the largest plain bonus counts, so only that one is doubled when it is a card's.
- 26 Lash Out bottom: swap with the Bear = the 1st edition Primal Blessing Swap (Self / Companion).
- 27 Unbreakable Bond: top's second attack is the Wildfury's own Attack 3, range 3 performed from the Bear's hex (Vars
  `TargetingOverrideArrayIndex: Bear.Tile`, Frosthaven's Pyroclast shape; it needs the Bear on the map; the name
  TAllyAttack keeps the enhancement slot). Bottom (2026-11-02, task wildfury-hard): as printed, by plugin
  GHUnbreakableBond.cs on a persistent marker (MiscVars GHUnbreakableBond, slots [1,0,1], lost after): on the three
  rounds after the play round she picks no cards and has no turn (initiative 99, last for focus), the Bear takes two
  full turns at initiative 14 under her player's control, her other cards' slots don't move (their effects still
  happen), this card's slot moves at each of those rounds' end. GH2e FAQ readings in the plugin's summary.
- 28 Spirit Sigil bottom: Bless one ally within range 1 [square] with Safeguard as its sub-ability.
- 29 Jaws of Death top: the commanded Attack 6 is marked `GHRepeatOnKill`; each time it kills, the plugin (JawsRepeat)
  gives the Wildfury XP 1 and stacks copies of the commanded Move 4 and Attack 6 (enhancements kept) for the Bear, again
  after every repeat that kills (unlimited, as printed).

## Perks (perks.py)

All 10 perks from the "Vermling Wildfury" sheet, 18 checks; names made up in the 1st edition style.
- 1 "Lick Wounds": new modifier `AMC_GH2E_Wildfury_Heal2Bear_R` (rolling +0, Heal 2 on the Bear by Frosthaven's
  AMC_Heal1_TBoneshaper_R custom targeting). The Bear only (2026-11-02): with no Bear on the map the plugin draws the
  card without its heal (GHWildfury.cs LickWounds); the old fallback to the attacker is gone.
- 3 "Pack Bond": `AMC_GH2E_Wildfury_P2_Bond` is a plain +2 again; the plugin (PackBond) rewrites it to +3 at the game's
  modifier rewrite step while the Wildfury and the Bear are within 4 hexes (read in C#: the old expression override
  crashed when the Bear drew the card).
- 5 "Primal Call": new `AMC_GH2E_Wildfury_AirEarth_P1` (+1, Air or Earth picker; AMC_FireAir_P1 shape).
- 6 "Bowl Over": new `AMC_GH2E_Wildfury_Push2_P2` (+2 Push 2).
- 7 "Warded Path": **APPROXIMATION / NEEDS PLUGIN.** Ignore scenario effects + a plain +1; the card's "Safeguard,
  target 1 ally" is left out (no attack-modifier shape places a bonus on another figure).
- 8 "Thick Hide": the plugin (ThickHide) turns a 2x an enemy draws attacking the Bear into +0 (perk Wildfury08).
- 9 "Call of the Wild": once each scenario toggle commanding the Bear Move 4 (ControlActor on the Bear; `TargetType:
  [Companion]`). The rider: the move is marked `GHCallOfTheWild` and carries a Teleport sub-ability (Spirit Swap top's,
  any distance, ending adjacent to the Bear, skippable) that only the Wildfury can start; when the move ends and the
  Bear left a hex adjacent to her on the way, the plugin (CallOfTheWild) stacks it for her.
- 10 "Life Bond" (linked, Cost 2): two hidden long-rest bonuses, one at the start (PreRestBonus, Boneshaper perk 10) and
  one at the end; once per rest (temp var GHLifeBondUsed, cleared by the plugin when a long rest starts). Either element
  (element picker beside one Consume of [Air, Earth], Restless Mind's shape). The marked step `GHLifeBond`: the plugin
  asks for X (yes/no loop, 1 up to her hit points - 1), lowers her current hit points by X (not damage) and sets the
  temp var GHLifeBondX, which the commanded Bear heal reads (StatIsBasedOnX).
- Masteries: mastery 1 (Air or Earth infused or consumed on every turn) = the plugin's ElementEveryTurn tag with
  `Elements: [Air, Earth]`. Mastery 2 "Unleashed" (4 kills by Command attacks of 7+, after bonuses, before modifiers) =
  the new tag FrosthavenMods_CommandKillsAtValue (Value 4, Amount 7): the plugin records each commanded Bear attack's
  value before modifiers and counts the kills (in memory: a reload mid-scenario starts the count again).

## Shared changes wanted

1. **Plugin: the Concentrated Rage token counter** (tokens on the card per Bear attack / Retaliate damage on the Bear;
   at 3+ the Bear's next attack spends three for +3). Now +1 on every Bear attack.
2. **Plugin: "as if you occupied the Bear's hex"** attacks performed by the Wildfury (Invocation of Spirit top,
   Unbreakable Bond top). Now the Bear performs them.
3. **Plugin: Unbreakable Bond bottom** (skip card selection + turn for three rounds, other persistents frozen, the Bear
   acting twice at initiative 14). Now one extra controlled Bear turn at the start of each of your next three turns.
4. **Plugin: Unleashed Fury top** (double card Attack bonuses on one attack). Now plain Attack 3.
5. **Plugin: Blood Hunger bottom end-of-round loss** (lose the card when the Bear neither attacked nor was attacked).
6. **Plugin: unlimited Jaws of Death repeats** (now two).
7. **Plugin: Safeguard condition** (replaces the data stand-in on Nature's Gift, Ancient Ward, Spirit Sigil and perk 7's
   card, which is now a plain +1).
8. **Plugin: perk 8** (enemy 2x against the Bear = +0) and **perk 9's teleport rider**; perk 10's free choice of X.
9. **Masteries**: a "kills by Command attacks of printed value 7+" tag (counting card bonuses, before modifiers).
10. gh_port prints "10 perks and 4 masteries" for this class: the count includes the four new attack-modifier files
    (cosmetic, as the Bruiser noted).
11. Summon models: gh_port reports "models copied: none" for every 2e summon (by design for edition 2, which borrows the
    1st edition pack's models). The four summons use `Model:` values the 1st edition pack ships (BeastTyrantBear,
    WindTotem, Monolith, SpiritBanner); worth confirming that the plugin finds a 2e summon's model by its `Model:` value
    when its ID (WildfuryBear, WindIdol, Monolith2, VitalForce) differs from the 1st edition pack's `summon_models` key.

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Wildfury`: build 29 of 29 cards; packcheck 29 cards, 0
problems, 0 warnings; rulecheck 48 ok, 0 with problems; no missing text keys; no gh_port warnings. **CHECK PASSED
Wildfury.**

Bench these first (shapes no Frosthaven card uses this way): the invocations (07 bottom, 10, 11, 21, 24 top: aura on
the Bear + OncePerRound + the required-consume gate), Spirit Swap top teleport, Disorienting Roar top enemy aura,
Focused Aggression bottom Bear-only retaliate, Stone Sigil bottom adjacency, Jaws of Death repeats, Unbreakable Bond
bottom, the three Safeguards, perks 1, 3, 9, 10.
- Perk Pack Bond: its override read TargetingActor, null when the Bear draws the card (error screen in the 2e sweep, 2026-11-01 09:36). Since 2026-11-02 the +1 is the plugin's (see perk 3 above).

## 2026-11-02: modifier abilities and Safeguard

- Perk 7 Warded Path (2026-11-02, supersedes the line above): `AMC_GH2E_Wildfury_P1_Safeguard`, +1 and the pack's Safeguard on one ally at any distance (GHModifierAbilities.cs runs its Ward carrier, GHSafeguard.cs turns it into Safeguard). The three card Safeguards are the real condition too. Bench it.

## 2026-11-02: companion rules (2e card-accuracy pass, task wildfury-mindthief)

Plugin GHWildfury.cs builds shared changes 1, 2 (Invocation of Spirit; Unbreakable Bond top is data only), 4, 5, 6, 8
and 9 above, and Invocation of Spirit bottom's Bear heal. 3 (Unbreakable Bond bottom): GHUnbreakableBond.cs
(task wildfury-hard).

# Sawbones (2nd edition): translation notes

All 35 cards (29 ability cards + the 6 Medical Packs) written from scratch from `tools/gh2e/sheets/Sawbones.json`
(no Xipe data; Secretariat agrees with the sheet on every ability card). Shapes come from the 1st edition port
(gh1e_yml 407-446, converted `mods/gloomhaven/.../Sawbones`) and Frosthaven's own cards; engine behaviour was read in
the decompiled ScenarioRuleLibrary where a shape was new. Enhancement marks (explicit slot keys everywhere), XP and
lost icons were checked against the sheet by script: all match except the three marks dropped below.

## Class-wide decisions

- **Medical Packs** (30-35, card IDs 13230-13235): `SupplyCard: True`, one file per pack (six different cards, one copy
  each). Every "give a medical pack" names `GiveSupplyCard: 13230` (gh_port makes it 33230). The six share the name
  "Medical Pack", so the plugin's GHSupplyCards.FreeCopy hands out the lowest-ID pack nobody holds: one of the six, in
  a fixed order, not random (2e: "a random card from the supply"). Level "M" (sheet) is written as is; the rule library
  only TryParses levels, so it acts like the 1st edition's "X".
- **Prescriptions** (07 top, 08 top, 11 bottom, 16 bottom, 21 top, 25 bottom, 26 top): placed on one adjacent ally with
  `GiveAbilityCardToActor` (the 1st edition's verified Booster Shot / Regenerative Tissue shape): the card goes to the
  ally's active area and its bonus works on the ally ("you" = the holder), so it can't be played without an adjacent
  ally (class rule). Each Prescription ability carries `MiscVars: GH2E_Prescription: True`; "an ally has an active
  Prescription" is checked as `ScenarioManager.Scenario.PlayerActors.Any(obj.ActiveBonuses.Any(CustomAbility.GetMiscVarBool(obj.Ability, "GH2E_Prescription")))`
  (Frosthaven's expression language: `.Any(...)` with `obj`, CVarsContext). Unverified in game; an expression error
  reads as false (the bonus just doesn't apply).
- **Start-of-turn Prescriptions** (16, 21, 25): StartTurnAbility with use slots; the "turns where ..." conditions are
  `ActiveBonus.Requirements.Vars` on the holder (`Actor`), which CStartTurnAbilityActiveBonus.CanTriggerAbility checks
  before the slot moves, so a skipped turn keeps its slot.
- **Safeguard** (04 bottom, 05 top, 17 bottom, 28 top, Medical Pack 31): the data stand-in from AUTHORING.md, one shape
  for the whole class: a `NullTargeting` ability (CAbilityNullTargeting targets and puts the bonus on each target) with
  a persistent `GenericOnNegativeCondition` bonus, `XPTracker: [0]` (one prevention, Geminate perk 12),
  `TriggerTarget == ActiveBonus.Actor`, not when immune, only the ten negative conditions (Curse included: a curse
  added to the deck counts, 2e p.26). **Not a toggle** (Reject the Gift bottom's automatic shape), so it fires by itself
  as the rule says; Elementalist2 wrote its Safeguard as a toggle (`IsToggleBonus`) — the lead may want one shape for
  the pack. A Safeguard on a non-persistent half keeps that card in the active area until it is used (the token's
  stand-in). The Safeguard diamond-plus marks on 04 bottom and 05 top are dropped (nothing to enhance).
- **"Remove one negative condition"** (03, 23 bottom): `RemoveConditions: 1` over the removable list; the game picks
  which one (Frosthaven's Cure Potion comment). Frosthaven's own pick-one shape (a `Choose` block) is self-only, so
  Medical Pack 34 (self) uses it: the player picks.
- **"Damaged"**: `TargetMissingHP: "> 0"` (CAbilityFilter key, verified by the 1st edition port).

## Decisions per card

- **01 / 06 / 24** give a pack: GiveSupplyCard on one adjacent ally with the XP on the give ability itself (gained only
  when a pack is given).
- **03 Curative Mixture** top: the condition removal is the parent ability (it picks the target, range 1, you or an
  ally) and the heal its sub-ability on the same figure: removal before the heal, as printed.
- **04 Bloody Saw** top: self conditional override (Extra Decoration shape) on the Prescription check: +1 Attack, Wound,
  XP 1.
- **05 Prevention is Key** top: Wound + Disarm with no attack on all enemies within range 1 (the diamond on Disarm, as
  Secretariat). Bottom: a one-use round DuringActionAbilityOnActionEnded bonus whose Vars look for a Prescription
  ability (MiscVars GH2E_Prescription) in the action just performed; it gives Shield 1 for the round to you and the
  allies adjacent then.
- **09 Needle Gun** top: the Pierce [square] mark = Pierce inside the Attack mapping with `PierceEnhancements: 1`
  (Frosthaven's Infuser Gale Barrage; a Pierce mapping of its own fails rulecheck).
- **10 Vital Strikes**: persistent BuffAttack with an ActiveBonus filter on damaged targets (Scoundrel Single Out shape).
- **11 Vaccine** top: target conditional override (damaged) giving `Target: 1` + XP 1. **Unverified** whether an
  override found on the first target can still add a second target (the XP works either way). Bottom: the Safeguard
  bonus with 3 slots on the ally, only for conditions applied by an enemy (`CustomAbility.IsEnemy(TriggerTarget,
  TriggerAbility.TargetingActor)`).
- **12 Teamwork / 23 Return the Favor** top: grants are ControlActor (Frosthaven's grant). 23's grant has no range on
  the card: Range 99, filter = an ally holding an active Prescription (`target.ActiveBonuses.Any(...)`).
- **13 Prep for Surgery** bottom: the 1st edition's verified override (attack/move → heal +1, added effects dropped,
  range at least 1). A converted ranged attack keeps its range (the 2e card doesn't say).
- **15 Precaution** top: Shield 1 this round on you and every ally. "Move the token back one slot on a Prescription":
  Frosthaven's `MoveActiveBonusTracker` only moves bonuses from the holder's own class cards
  (GetActiveBonusesWithTrackers: card ClassID == holder's class), and a Prescription stays a Sawbones card: text only,
  **NEEDS PLUGIN**.
- **17 Blood Transfusion** top: Heal X = damage dealt by the attack (Drifter Consume Stamina shape). Bottom: Safeguard on
  one figure within range 1 with "remove all conditions" and "give a pack" as sub-abilities on the same target.
- **18 Experimental Stimulants** top: Retaliate 2 this round on an adjacent ally; the Prescription clause is a
  sub-ability (filter Vars on the target) giving XP 1 and a one-use PreventFirstDamage for the round. **Unverified**
  that a sub-ability's filter is checked against the parent's target.
- **19 Do No Harm** top: Attack 4, Wound, Stun + mandatory self-Disarm, lost. The character token ("the next three times
  they lose Stun, they gain Stun") = Frosthaven's ApplyConditionOnLoseCondition add-condition bonus on the target (a
  sub-ability of the attack, `ValidConditions: [Stun]`, three slots). Bench it (no Frosthaven card uses it from YAML).
- **20 Hack to Bits** top: the on-kill Wound is Deepwraith Slipping into Death's sub-ability (adjacent to the parent's
  target, `ActorsKilledByAbility > 0`).
- **21 Fortifying Injection** bottom: Sunkeeper2 Protective Blessing's damage-negation aura, allies only, this round.
- **22 Amputate** bottom: Heal 7 with `Filter: [Self, Ally, Enemy]` and the heal's own Stun (mandatory). **Unverified**
  that Frosthaven lets a heal target an enemy.
- **23 Return the Favor** bottom: one condition removed from an adjacent ally, XP 1 on the removal; "you may give one
  adjacent enemy the condition you removed" is text only, **NEEDS PLUGIN** (Pain Conduit's Line of Transference does it
  for your own conditions with a Choose block + `AddNegativeConditions`, self only).
- **26 Take Your Medicine** top: a start-of-turn give on the ally would come from the ally's class: CAbilityGiveSupplyCard
  takes `TargetingActor.CharacterClass.SupplyCards`, and the ally has none. **Approximation:** the three packs go to the
  adjacent ally at once (`GiveSupplyCard: [13230, 13230, 13230]`; FreeCopy turns the list into three free copies), not
  one per turn; it no longer counts as an active Prescription. Bottom: "each ally may immediately play one Medical Pack
  from their hand for one action": text only, **NEEDS PLUGIN** (candidate shape: Geminate's `SelectAndPlayCard` with
  `CardPile: Hand` and `IsCardValid: AbilityCard.SupplyCard`, targeted at each ally; not used, untested on another
  figure).
- **27 Euthanize** top: Kill within range 1, normal, elite and boss (user decision A9), damaged, `TargetNegativeConditionCount >= 2`,
  `Health <= 8`. Bottom: one Damage 1 carrying Wound, Poison, Muddle (same target, same result).
- **28 Secret of the Oak** top: remove all conditions first (the parent ability, range 1), then on the same figure
  Heal 6, Safeguard, Shield 2 this round + XP 1 if it holds a Prescription (sub filter; unverified as in 18).
  Bottom: a dummy persistent bonus (Shattersong Empowering Note's NullTargeting) keeps the card active and lost as
  printed; "+4 Attack or +4 Move after a heal on a Medical Pack card" is text only, **NEEDS PLUGIN**.
- **29 Grisly Trauma** bottom: Crushing Weight's melee-only BuffAttack with a `Target: 1` effect (HIVE Rapid Fire),
  one use, this round.
- **Medical Packs**: mandatory self-Poison (33) and self-Immobilize (35) are `AutoTrigger` + `CanSkip: False` second
  abilities; 34's removal comes before its heal, as printed.

## Perks (perks.py)

- 2 Emergency Supplies: new `AMC_GH2E_Sawbones2_Pack_M2` (-2, GiveSupplyCard 33230 to you or an adjacent ally).
  Unverified: a give from inside a modifier draw.
- 4 Sedation: "you may gain Disarm to add Stun" can't be a choice on a modifier: `AMC_GH2E_Sawbones2_DisarmStun_P1` always
  adds Stun and always disarms you. Approximation.
- 5 Follow-up Care: the conditional "+1 Attack if an ally has an active Prescription" needs the plugin: plain +1 cards
  until then.
- 6 Field Dressing: new rolling `AMC_GH2E_Sawbones2_Heal3Range1_R`.
- 7 Potent Remedies (linked, 2): text only, **NEEDS PLUGIN** (+1 Heal on Medical Pack heals).
- 8 Friend of the Oak (linked, 2): Frosthaven's outpost has no Sanctuary of the Great Oak donation: text only.
- 9 Restorative Rounds (linked, 3): after each long or short rest, if an ally holds a Prescription, you gain one Medical
  Pack (GiveSupplyCard to yourself, so it comes from your own supply). Moving a Prescription token back: **NEEDS PLUGIN**
  (same reason as Precaution).
- Masteries: "Constant Care" uses the 1st edition crossover's `FrosthavenMods_AllySupportEveryTurn` (broader: any ally
  help counts; long-rest rounds are not exempt unless the plugin skips them). "Never attack an undamaged enemy while a
  damaged monster lives, 10+ attacks" has no existing pattern: left out.

## Shared changes wanted

- **GHSupplyCards.FreeCopy** (plugin): when no copy is free it falls back to the named ID, so a seventh give adds a
  duplicate of a held card (the hand UI throws on duplicate IDs). With six single packs, three givers, Take Your
  Medicine's three and two perks, running out is likely: give nothing when none is free (2e: "may only be performed if
  at least one medical pack remains"). Optionally pick a random free copy instead of the lowest ID.
- **Plugin (NEEDS PLUGIN rules)**: moving a Prescription's token back on an ally (Precaution, perk 9; MoveActiveBonusTracker
  rejects cards of another class); Do No Harm's re-Stun token; Return the Favor's condition transfer to an enemy; Take
  Your Medicine's "allies play a Medical Pack now" and its per-turn pack (a supply give from an ally's bonus should use the
  bonus caster's supply); Secret of the Oak bottom (+4 after a Medical Pack heal); perk 5 conditional +1; perk 7 +1 Heal
  on Medical Pack heals.
- **GHMasteries.cs**: check `FrosthavenMods_AllySupportEveryTurn` accepts `Sawbones2ID` (it was written for SawbonesID).
- **Converter**: a Pierce enhancement slot (Needle Gun's [square]) if Frosthaven can take one anywhere.
- **Safeguard shape**: one shape across classes (this class: automatic; Elementalist2: toggle).

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Sawbones2`: **CHECK PASSED Sawbones2** (35 cards built,
packcheck 0 problems / 0 warnings, rulecheck 48 ok, no gh_port warnings, no missing text keys). The build line reads
"35 of 30 cards": the six Medical Packs share one sheet name, so the name-keyed total counts five fewer (cosmetic).
- Perk Emergency Supplies: modifier rewritten, `AMC_GH2E_Sawbones2_Pack_M2` is now -2 with a top-level Choose (Frosthaven's AMC_PushOrPull2_R) whose one option is the GiveSupplyCard 33230 to you or an adjacent ally (a GiveSupplyCard ability on a modifier card stops the game when drawn; a Choose's option runs as an ordinary ability). The give can be skipped (printed: mandatory). Bench it.
- Perk Sedation: modifier rewritten, `AMC_GH2E_Sawbones2_DisarmStun_P1` is now +1 with a Choose whose one option is Stun on the attack's target (NullTargeting + Conditions, aimed by AddValidActorInRange/AddActorToTarget: TargetActor) with a self-Disarm NullTargeting sub-ability; skipping the Choose = no Stun, no Disarm, so the printed "you may" is now real (a Disarm ability on a modifier card stops the game when drawn). Supersedes the always-stun approximation above. Bench both choices.
- 2026-11-01: the perk modifiers built on a Choose inside a modifier card are plain Frosthaven cards for now (NEEDS PLUGIN): Sedation's self-Disarm option stopped the game ('Invalid targeting type', 2e rerun 10:47), and the same untested shape was in every Choose modifier. Each perk now gives only its number, never worse than not taking it.

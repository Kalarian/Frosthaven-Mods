# Spellweaver2 (Gloomhaven 2nd edition Spellweaver): translation notes

All 27 cards written from `tools/gh2e/sheets/Spellweaver.json` (the 26 Xipe imports rewritten card by card; Spell Mastery
new). Xipe's seven `99080-99086_Spellweaver2_X.yml` files are Attack 3 / Move 3 stubs (levels 6-9) with no card data:
nothing taken from them. Nothing here has been seen in game yet: the shapes come from Frosthaven's own cards (named per
card below) and the engine code (decompiled ScenarioRuleLibrary), so every "verify" line is a bench item.

## Corrections against Xipe (values, marks, piles)

- Fire Orbs: Range 3 not enhanceable; XP 1 on the top. Flame Strike: Strengthen enhanceable. Aid from the Ether: bottom
  Move 3 (Xipe 2). Frost Strike: XP 1 on both consumes. Emberfrost / Cool Down: Pierce 1 enhanceable (PierceEnhancements).
  Elemental Rays: Range 3 not enhanceable. Cold Fire top / Frostflare Orbs bottom: area hex mark right of the top red hex
  (3,1). Dancing Gales: no Muddle on the bottom's second attack. Twin Beams: top is not lost (Xipe [Lost, ...]).
  Chromatic Explosion bottom: no enhancement marks (Xipe's enhanceable Range removed). Inferno bottom: no AutoTrigger.
  Etheric Echo: Xipe's placeholder replaced by the real card. Initiatives come from the sheet (build.py).
- Text keys: all Xipe GH2E_X_* and 1st edition keys ($063_T$, $069_T_1$...) replaced by GH2E_Spellweaver2_* keys with the
  card's own wording.

## Decisions per card

- **Reviving Ether** top: unrecoverable + lost = Permanently Lost (as the 1st edition card), so RecoverLostCards All never
  brings it back.
- **Aid from the Ether / Etheric Echo / Searing Glacier / Twin Beams** bottoms, "when you next perform an action with
  [lost] this round, X": Frosthaven's own lost-action trigger (Geminate perk 7: `DuringActionAbilityOnActionEnded`,
  `ValidateDidLoseCard` on the performed half's `CardPile`, also accepting PermanentlyLost) as a round bonus with one use
  (`XPTracker: [0]`, Frosthaven's Gale Barrage / Swift Pivot write Round + [0]). The effect comes when that action ends:
  exact for Twin Beams ("after the action"); for the infusion (end of turn anyway), Ward and the recovery it is a timing
  approximation (after rather than during the lost action). Verify on the bench that the trigger fires on a lost half and
  not on a discarded one.
- **Etheric Echo** top: Frosthaven's SelectAndPlayCard from the lost pile (HIVE Recall, Low Tide). `IsCardValid:
  AbilityCard.Level <= 1 && AbilityCard.ID != 32401` (level X cards count as level 1, as Frosthaven's Stamina Potion;
  32401 = Reviving Ether's converted ID, written by hand: if the 2e card IDs ever move, this number must follow).
  `IsActionValid: IsLoss` (the engine's own variable in CAbilitySelectAndPlayCard). The card returns to the lost pile
  (the ability's default). Verify on the bench.
- **Spell Mastery** (new): the same ability from the discard pile (Grave Digging, Reverse the Flow), lost halves only,
  skippable ("you may"); the played card goes to the lost pile (`MoveCardsToLostPile`, Low Tide). Rules question: the card
  doesn't print where the played card goes; GHS says the lost pile, and performing a lost action loses the card, so lost.
- **Frost Strike** bottom / **Cold Fire** bottom: the consume grants a whole round bonus on a Null placeholder (Implode's
  shape): advantage = Frosthaven's Death Spiral bottom (Attack 0 + AttackHasAdvantage, round BuffAttack); +1 Attack =
  round BuffAttack 1. No empty bonus shows when nothing is consumed (Xipe's Cold Fire made a +0 one).
- **Emberfrost** bottom: Frosthaven's ally shield for the round (Hold the Line: Shield, Filter Ally, round bonus) instead of
  Xipe's ControlActor.
- **Flameswell** top: persistent +2 Attack with `AttackEffect: Type: SingleTarget` (no bonus and no slot for an area or
  multi-target attack ability), Ranged, XPTracker [0,1,0,1].
  Bottom consume Ice: Ward through `Condition: Ward` (Frosthaven's Drive Recharge override shape), not Xipe's Shield 1.
- **Ice Armor**: Frosthaven's Scaled Armor shape (PreventFirstDamage + `Requirements` threshold on
  GameState.CurrentDamageData: `DamageSourceStrength >= 2`), XPTracker [1,0,1]. Top given to an ally within Range 3 as
  Frosthaven gives persistent bonuses to allies (Shifting Zephyr Barrier: GiveAbilityCardToActor). Verify: the threshold
  reads the source's damage (before shield), and who gets the XP of the slots when the card sits with the ally.
- **Freezing Nova** top: APPROXIMATION. "The 4 enemies closest to you within 5 hexes" = Range 5, Target 4 (the player
  picks, should pick the closest), TreatAsMelee (no range printed; the 1st edition Nova's shape). No Frosthaven targeting
  sorts by distance.
- **Heatwave** bottom: the "+2 on their next attack this round" is a sub-ability of the Strengthen on the same ally (round
  BuffAttack, XPTracker [0], Gale Barrage's shape; sub-ability targeting takes the parent's target when the filters
  match). Verify the bonus lands on the ally, not on the Spellweaver.
- **Dancing Gales** top: each consume grants a whole ability on a Null placeholder (Strengthen self before the attack; an
  extra Attack 2, Range 3, Muddle after it).
- **Warm Up** top: APPROXIMATION + NEEDS PLUGIN. One-use persistent toggle +1 Attack bonus (Tidal Blast's IsToggleBonus
  BuffAttack); the "next attack ability performed from an action with [lost]" condition is the player's to honour:
  Frosthaven's BuffAttack never checks requirements (CActiveBonus.RequirementsMet isn't called for attack buffs).
- **Elemental Rays** bottom: condition-only ability: Curse (the enhancement mark sits on it) with `Condition: Stun`
  (the 1st edition port's Lull into Oblivion writes a second condition that way), 3 targets, Range 3.
- **Chromatic Explosion** top: the heal is a plain sub-ability of the area attack with Filter Ally: the engine's
  sub-ability targeting copies the parent's area and takes every figure in it that passes the filter (CAbilityTargeting
  Restart, UseSubAbilityTargeting), so the allies in the red hexes get Heal 4, Strengthen, Bless. Rules question settled:
  "allies" excludes yourself (the area has no grey hex). Bottom: Shield 3 on one ally against the next attack this round
  (ShieldIncomingAttacks, one use, round). Consume Ice +3 Shield exact. Consume Fire: "the attacker suffers damage 3" = a
  second one-use ShieldIncomingAttacks (Shield 0, hidden) added on the same ally as a sub-ability by the consume
  override, whose pop on that next attack deals 3 to the attacker (Obsidian Shield's TriggerOnPopAbilities). Bench.
- **Freezing Vortex** top: the kill is a sub-ability of the area attack (the 1st edition port's Impending End: Kill, Filter
  Health "<= 2"); as above it takes every enemy in the area at 2 or fewer hit points after the attack. NEEDS PLUGIN: the
  1-hex obstacle in each killed enemy's featureless hex is text only (no Frosthaven shape creates an obstacle on every hex
  a multi-target kill emptied; Xipe's merged create filled the attack's own tiles). Bottom: the 1st edition Black Hole
  bottom's shape (Pull, Range 4, all targets, PullTowardsTile), Pull 3; whether the range-4 circle is measured from the
  designated hex as printed is the engine's PullTowardsTile behaviour (as Black Hole).
- **Inferno**: top "all enemies within 2 hexes" = Range 2, all targets, TreatAsMelee; the ally damage (a mandatory "!"
  row) auto-triggers after the attack (not the half's first ability). Bottom: room damage (the 1st edition Inferno's
  Targeting Room) with the standard confirm and CanSkip False (Catastrophic Bomb's decision).

## Perks (perks.py)

- 10 perks, 18 marks; the linked two-box perk is one perk with Cost 2 (as the Scoundrel's Cloak of Invisibility).
- Wild Weave: new AMC_GH2E_Spellweaver2_Wild_P0 (+0, infuse any with the picker, as AMC_ConsumeInfuseWild's infusion).
- Frostfire: new AMC_GH2E_Spellweaver2_FireIce_P2 (+2, Fire or Ice picker, as Frosthaven's AMC_FireAir_P1).
- Spent Power: NEEDS PLUGIN. AMC_GH2E_Spellweaver2_Lost_P1 is a plain +1; the "+2 more if drawn during an action with
  [lost]" needs the plugin (no attack modifier can see the current action's pile).
- Thunderclap uses gh_crossover's AMC_Stun_P1, Hex Frosthaven's AMC_Curse_P1.
- Vanishing Ward (once per scenario, negate damage by gaining Invisible + Stun): Frozen Fist perk 11's hidden optional
  PreventFirstDamage (one use) + Metal Mosaic perk 10's AbilityData (Conditions [Invisible, Stun]) performed on the trigger.
  Verify on the bench (optional prompt, conditions applied).
- Etheric Bond (short rest: recover Reviving Ether from the discard first): NEEDS PLUGIN. Flags [Passive] only; the plugin
  has no implementation for the 1st edition perk either (gh_crossover's comment says "answered by the plugin").
- Arcane Blessing (Bless at the end of a turn with a lost action): Geminate perk 7's lost-action trigger leaves a hidden
  round marker (MiscVars GH2E_LostActionThisTurn); a persistent EndTurnAbility bonus whose Requirements look for the
  marker gives the Bless at the end of that turn. Bench.
- Masteries: Deep Reserves (four different lost actions twice each) uses the plugin's FrosthavenMods_LossActionsRepeated
  tag with ActorID Spellweaver2ID. Mastery 1 ("consume both Fire and Ice during the same turn 6 times") has no tag: left
  out (shared change wanted below).
- strings.tsv carries the English names of the four attack modifiers the perks add (as Sunkeeper2 does).

## Shared changes wanted

- packcheck: add `SelectAndPlayCard` to KIND_KEYS (Etheric Echo and Spell Mastery tops give "no ability kind found"
  warnings; Frosthaven's own HIVE Recall / Low Tide are the same shape). The check passes with the two warnings.
- Plugin: Etheric Bond for both editions (2e: on a short rest, Reviving Ether 32401 from the discard pile first; 1e: its
  20063).
- Plugin: Spent Power's conditional +2 (AMC_GH2E_Spellweaver2_Lost_P1 drawn during an action whose half is lost).
- Plugin: Warm Up top's condition (the +1 applies only to an attack ability performed from a lost action, then ends).
- Plugin: Freezing Vortex top's obstacle in each killed enemy's featureless hex.
- Plugin mastery tag: "consume both Fire and Ice during the same turn" counted per turn (Spellweaver2 mastery 1).

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Spellweaver2`: CHECK PASSED (27 of 27 cards, packcheck 0
problems / 2 warnings (SelectAndPlayCard, above), rulecheck 42 ok, no gh_port warnings, no missing text keys).

## Card-accuracy pass, 2026-11-02 (plugin GHSpellweaver2e.cs; supersedes the NEEDS PLUGIN items above)
- Ice Armor top: any ally, an ally's summon included; the slots' XP goes to the Spellweaver ("GHRule IceArmor").
- Freezing Nova top: MiscVars GH2E_ClosestTargets 4: only the 4 nearest enemies (ties kept) are valid ("GHRule
  ClosestTargets"). Inferno top: Range 2 with line of sight is the approved reading (QUESTIONS.md B); unchanged.
- Warm Up top: no toggle; counts only on an attack ability of a lost action, on all its attacks, the slot spent at its
  end ("GHRule WarmUp").
- Freezing Vortex top: a 1-hex obstacle (FrozenFistObstacle) in each killed target's featureless hex ("GHRule
  FreezingVortex").
- Spent Power: the +1 becomes +3 during an action with a lost icon ("GHRule SpentPower"). Etheric Bond: Reviving Ether
  back to hand when her short rest starts ("GHRule EthericBond"; 2nd edition only, the 1st edition perk still has none).
- Mastery FireAndIceSameTurn written (Spellweaver2_MASTERY_2: the printed mastery 1, listed second so the shipped ID
  of Deep Reserves stays).
- Re-audit fixes (2026-11-02, fix-reaudit-B): Etheric Echo bottom gives Ward as the first ability of her next lost action
  STARTS (GHSpellweaver2e.WardAtLostStart, then the bonus ends; "GHRule EthericEcho"). Etheric Bond acts only on the
  rule library's own short-rest message (worker thread), not on the copy the UI builds online.

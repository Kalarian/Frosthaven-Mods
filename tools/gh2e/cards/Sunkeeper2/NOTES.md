# Sunkeeper (2nd edition): translation notes

All 30 cards written from scratch from `tools/gh2e/sheets/Sunkeeper.json` (no Xipe or Secretariat data for this class).
Shapes come from the 1st edition port (gh1e_yml 175-204 and the converted `mods/gloomhaven` cards) and Frosthaven's own
cards. Enhancement marks, lost / unrecoverable icons and XP were cross-checked against the sheet by script (all 30 match).
Where a mark's shape differs from the converter's default (Attack/Heal/Move with a square), the file names the slot
(`SquareEnhancements`) itself.

## Decisions per card

- **03 Empowering Command** top: no target text, only a range pill: Strengthen + Bless on one figure within range 3,
  you included (`[Self, Ally]`). The diamond+ mark sits on Bless.
- **05 Lay On Hands** top: Heal X = target's max HP written as the 1st edition's verified heal-to-full
  (`StatIsBasedOnX: TargetDamageTaken`): same result, except that it never "overheals", so the Shielding Light perk
  (Ward on overheal) never fires from this card. Approximation.
- **07 Brilliant Prayer / 25 Bright Aegis**: the stack-with-red-X icon = `Permanently Lost` (2e rulebook p.34, same as
  Frosthaven).
- **08 Soothing Light** top consume: "the target may recover one level 1 card" is a recover of its own (level 1 only:
  `Vars: CardLevel: 1`, as Frosthaven's Memory Drive / Stamina Potion) on one figure within range 2; the player picks the
  healed figure. Approximation (not tied to the heal's target). The CardLevel filter on an ally's recover is unverified.
- **09 Purifying Aura** top: "attacks performed by an adjacent enemy" = the 1st edition's melee filter
  (`AttackType: Melee`): a ranged attack from an adjacent enemy doesn't trigger it. Approximation.
- **10 Protective Blessing** bottom: the 1st edition's damage-negation aura, unchanged.
- **11 Defensive Stance** bottom: Shield 1 + mandatory -1 Move as the 1st edition card. "Reduce the Pierce of all
  attacks targeting you by 2": no Frosthaven ability, text only. **NEEDS PLUGIN.**
- **12 Daybreak** bottom: Frosthaven's Deflecting Maneuver disadvantage (`AttackersGainDisadvantage`, round) without its
  AutoTrigger (standard confirm). Top consume Dark: +1 Attack, Muddle, infuse Light, XP 1 as the 1st edition card.
- **13 Sanctify** top: a persistent attack aura on allies within range 2 (Birds In a Tempest aura shape) with three use
  slots. "+2 Attack instead if they have Ward" needs a per-attacker check: everyone gets +1. **NEEDS PLUGIN** (Ward
  part). Unverified: that an aura's XPTracker advances on each ally's attack.
- **14 Beacon of Light** top: "your next three single-target heals also target all allies within range 2 of the primary
  target". **NEEDS PLUGIN.** Approximation: Frosthaven's use-limited heal override (Gemstone Resonance's
  `OverrideAbility` on `[Heal]`, Death Spiral's XPTracker) adding +2 targets to your next three heal abilities, aimed
  from you inside the heal's own range; it also counts heals from items/perks and multi-target heals. Unverified whether
  the override's `Target: 2` adds or sets.
- **15 Holy Defense** bottom consume: "Heal 1, target all, range 1" includes you (2e p.19: positive "all" = allies or
  yourself).
- **16 Mobilizing Axiom** bottom: "grant all allies within range 5 and self: Move 4": the allies' move as a grant (it
  carries the enhancement), then your own plain Move 4 after theirs (no ControlActor on yourself; moving last keeps
  allies in range). Your move doesn't take the enhancement. Approximation.
- **17 Engulfing Radiance** top: "Heal 5, target all" with no range = you and all allies anywhere, as the 1st edition
  card. Rejected the "melee range" reading (docs/gh2e/groups.md, open question) because the same class prints "all,
  range 1" explicitly on Holy Defense.
- **21 Arresting March** top consume: "Heal 3, target the target of the grant ability" is a heal of its own on one ally
  within the grant's range 3 (the player picks the granted ally). Approximation.
- **22 Path of Glory** bottom: "Strengthen, Bless all allies moved through": a plain Move, then its own condition ability
  with `AllTargetsOnMovePath` + `Targeting: All` (the Trample fix in CARD_FIXES), not the 1st edition's conditions on the
  move. Unverified for a condition ability (the Trample fix was proven on an attack).
- **23 Divine Intervention** top: the 1st edition's redirect (`PreventAndApplyToActiveBonusCaster`). Bottom: "grant
  all allies within range 3 and self: Shield 1" = a round Shield 1 on you and allies within range 3. Consume Light:
  "enemies' positive and 2x modifier cards count as +0 this round" has no Frosthaven ability. **NEEDS PLUGIN.**
  Approximation: every attack targeting you or any ally gains disadvantage this round (the nearest engine effect; text
  left as printed).
- **24 Scales of Justice** bottom: **NEEDS PLUGIN** (null drawn counts as 2x; triggering on any Light consumed during an
  attack). Approximation: the 1st edition's Weapon of Purity toggle: a persistent attack bonus the player switches on
  for an attack, consuming Light for +1 Attack, Pierce 1. A Light spent by the attack card's own consume can't also pay
  for the toggle.
- **25 Bright Aegis** bottom consume: "that ally may immediately play up to two cards, one top and one bottom action" =
  the 1st edition's verified `ExtraTurn: BothActions` (Unwavering Mandate) on one adjacent ally (the player picks the ally
  who recovered). Its `PreviewEffectText` points at `FHS_GH2E_Sunkeeper2_25_B` (a key the pack writes).
- **26 Illuminate the Target** top: the designated hex = a melee attack on an adjacent enemy. "Strengthen all allies
  adjacent to you or the designated hex" = a Strengthen on allies adjacent to you, plus the attack's sub-ability on
  allies adjacent to its target (`AllTargetsAdjacentToParentTargets` + `TreatAsNonSubAbility`, as Care Package / Virulent
  Strain in CARD_FIXES). An ally adjacent to both gains it twice (no effect). Unverified.
- **28 Supportive Chant** top: read as the sheet says (each granted ally gains Ward, Strengthen, Bless; then each
  performs Attack 5): the conditions are applied by you to all allies within range 3 (same result), then a grant of
  Attack 5 to each. Bottom: the 1st edition Supportive Chant aura (round, all allies anywhere, you not included); the
  consume adds advantage to the aura (`AttackHasAdvantage` override). Unverified that the override reaches the aura.
- **29 Divine Ascension** top: Frosthaven's Crushing Weight shape (melee-only BuffAttack) with +4, advantage and
  `EntireAction` (whole ability counted once, the 1st edition's Angelic Ascension), five slots. "Gain Flying" = a second
  persistent bonus on the card (the 1st edition Winged Congregation's Move 0 with Fly). Unverified that Flying ends when
  the slot track runs out.
- **30 Solar Bastion** top consume: a recover of its own on one ally within the heal's range 3 (everyone in range was
  healed, so this matches the card except for summons).

## Perks (perks.py)

- 1 Radiance: the 2e sheet prints the two +0 Light cards without the rolling icon: a new non-rolling
  `AMC_GH2E_Sunkeeper2_Light_P0` (the 1st edition crossover used rolling `AMC_Light_R`).
- 4 Mending Light: new `AMC_GH2E_Sunkeeper2_Heal1Range3_P1` (+1, Heal 1 on you or an ally within range 3).
- 5 Second Wind: new `AMC_GH2E_Sunkeeper2_Recover1_M1` (-1, recover one level 1 discarded card, you or an adjacent
  ally). Unverified: a card-picking recover in the middle of an attack's modifier draw. Check on the bench.
- 9 Vigilant Entry (linked): Frosthaven's room-revealed perk (Perk_Snowdancer_09). It fires whenever any room is
  revealed, not only when you open a door on your turn. Approximation.
- 10 Restful Dawn: the Elementalist's long-rest infusion, Light only; "may" is automatic.
- 11 Shielding Light (linked): text only. The plugin answers the 1st edition's (perk ID Sunkeeper10); this one is
  `Sunkeeper211`.
- Masteries: Child of the Sun uses the plugin's `FrosthavenMods_ElementEveryTurn` (as the 1st edition crossover).
  "Deal 8 or more damage with an attack 3 or more times" uses the plugin's `FrosthavenMods_AttackActionsWithDamage`
  (Value 3, Amount 8), which counts 3 *different* attack actions: approximation.

## Shared changes wanted

- **GHPerks.cs**: Shielding Light for perk ID `Sunkeeper211` (the 2e class), as for `Sunkeeper10`.
- **Plugin (NEEDS PLUGIN rules)**: Pierce reduction (Defensive Stance); per-attacker Ward check for +2 (Sanctify);
  heal splash to allies within range 2 of the primary target (Beacon of Light); enemies' positive/2x modifiers as +0 for
  a round (Divine Intervention); null-as-2x and "any Light consumed during an attack" trigger (Scales of Justice).
- **GHMasteries.cs**: a per-attack damage tag (any N attacks dealing X+), for the second mastery.
- **1st edition pack** (seen while porting, not changed): Unwavering Mandate (20190) has `PreviewEffectText: 190_T`, but
  the 1e pack's strings.tsv only holds `FHS_GH_190_T`, so the preview probably shows the raw key.

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Sunkeeper2`: **CHECK PASSED Sunkeeper2** (30 of 30 cards,
packcheck 0 problems / 0 warnings, rulecheck 45 ok, no gh_port warnings, no missing text keys).
- Perk Second Wind: modifier rewritten, `AMC_GH2E_Sunkeeper2_Recover1_M1` is now -1 with a top-level Choose (Frosthaven's AMC_PushOrPull2_R) whose one option is the RecoverDiscardedCards 1 (level 1, you or an adjacent ally); skipping the Choose is the printed "may" (a RecoverDiscardedCards ability on a modifier card stops the game when drawn; a Choose's option runs as an ordinary ability). Help text GH2E_Sunkeeper2_RECOVER_HELP. Bench it.
- 2026-11-01: the perk modifiers built on a Choose inside a modifier card are plain Frosthaven cards for now (NEEDS PLUGIN): Sedation's self-Disarm option stopped the game ('Invalid targeting type', 2e rerun 10:47), and the same untested shape was in every Choose modifier. Each perk now gives only its number, never worse than not taking it.
- 21 Arresting March top: the consume's Null placeholder now comes before the grant (after it, switching Light on stopped the game: RefreshConsumeBar NullReference, 2026-11-01 12:34). The heal now happens before the ally's attack.

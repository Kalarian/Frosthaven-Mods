# Soothsinger (2nd edition): translation notes

All 30 cards written by hand from `tools/gh2e/sheets/Soothsinger.json` (no Xipe data; Secretariat's areas used for
Provoke Terror, Change Tempo and Captivating Performance). Shapes come from Frosthaven's own cards, the converted 1st
edition Soothsinger and the other 2e folders; each file's header comment names its source. Enhancement marks were
counted against the sheet by script (all 30 halves match; the area hex marks included).

## The class systems

**Notes = two character resources** (`resources.yml`): `SoothsingerHighNote` and `SoothsingerLowNote`, `MaxAmount: 2`
each (the mat's cap; `CCharacterResource.Amount` clamps to it).
- *Gaining*: a half with a clef gets a last ability `TNote`/`BNote` (NullTargeting on self, AutoTrigger,
  `ResourcesToAddOnAbilityEnd`), the Plagueherald2 supply's shape. A Null/NullTargeting ability always "happens", so the
  Note comes even when the half's main ability had no target.
- *Note of your choice* (start of scenario in `character_extra.yml`, Increase Cadence bottom, Grand Finale top, perk
  Lullaby): Frosthaven's `Choose` (Metal Mosaic Energy Conversion) between a High and a Low gain.
- *Spending*: every song half ends with `TSongNotes`, a hidden `Duration: Ability` bonus whose finish runs
  `RemoveAllInstancesOfResourcesOnFinish: [High, Low]` (the rule "whenever you perform a Song, remove all of your
  Notes"). `CClass.MarkAbilityActiveBonusesFinished` finishes it right after the ability, so a Note gained later the same
  turn (a bottom half) survives. **Unverified** in game.
- **NEEDS PLUGIN** (three rules not enforced): (1) a song half can be played without the Notes it costs (the cost is
  printed as text "Song: High Note, Low Note"; the player keeps the rule); (2) "once each round" for gaining a Note;
  (3) gaining is automatic, not "you may". Possible data gate for (1), not used because a failed check would waste the
  card and the expression is unbenched: `StartAbilityRequirements: {Type: ThisAbility, Vars: {ValidateNotes:
  Actor.GetCharacterResourceAmount('SoothsingerHighNote') >= 2 && ...}}` on TSong and TSongNotes (Shattersong-style
  requirement vars).

**Songs** are plain Frosthaven persistent bonuses, not Gloomhaven's PlaySong / OverrideAsSong: those go through
`CActor.AddAugmentOrSong` with song slots (one song, the next replaces it), while 2e songs stack. Consequence: no "Song"
tag on the card face and no music-note status icon (both keyed to PlaySong in the plugin; shared change 2).
- *End-of-turn songs* (Mobilizing Measure, Bewildering Instrumental, Wearying Chant, Dissonant Rhythm, Soothing
  Lullaby, Alluring Serenade, Captivating Performance): `Behaviour: EndTurnAbility` with the printed slots as
  `XPTracker` (Tinkerer2 Auto Turret). One trigger per turn = one slot per round, so N slots = N end-of-turn effects, as
  printed. (2e advances the token at the round's end; the song now leaves at the end of your N-th turn instead of the
  round's end, which changes nothing for an end-of-turn effect.)
- *Aura songs* (Power Ballad, Defensive Ditty, Echoing Aria, Ode to the Gambler, Thrilling Solo, Disorienting Dirge,
  Wistful Wounding, Toughening Trill): the effect bonus (persistent, no slots) plus `TSongTimer`, a Null
  `EndRoundAbility` bonus (Elementalist2 Lingering Essence) carrying the slots, advanced once at every round's end, with
  `EndAllActiveBonusesOnBaseCardSimultaneously` (the 1st edition Lull Into Oblivion) so its last slot ends the effect
  too: active from the play to the end of round N, as printed. **Unverified** (the flag on a Persistent EndRound bonus).
- "You and all allies" = an aura `AuraTargeting: All`, `AuraFilter: [Self, Ally]`, any range.
- Grand Finale / Captivating Performance / perk Fermata token moves: **NEEDS PLUGIN** (text only).

## Decisions per card

- 01 Mobilizing Measure top: "the ally closest to you": Custom targeting offers only the allies at the smallest distance
  (counted in hexes along a path around walls, figures ignored, CActor.GetDistanceTo(x, true)); ties are the player's
  choice. Bottom Retaliate 1 is a
  round bonus on one adjacent ally. Unverified: a ControlActor grant inside an EndTurnAbility bonus.
- 02 Bewildering Instrumental top: Custom targeting offers only the enemy monsters with the highest current hit points
  (ties: the player picks); no range or line of sight ("suffers").
- 03 Wearying Chant top: Custom targeting offers only the closest enemy monsters without Disarm or Stun (distance as 01;
  ties: the player picks).
- 04 Power Ballad: Shattersong Din of Battle's aura (+1 Attack, once per turn per figure, toggled by each figure on the
  attack it chooses), persistent. "First attack each turn" becomes "one attack each turn of the figure's choice".
- 05 Dissonant Rhythm top: no range printed = melee (rule B); the closest enemy of a melee attack is an adjacent one and
  all adjacent enemies tie at distance 1: Range 1, the player picks among them.
- 06 Increase Cadence bottom: a Note choice at the end of this and the next five turns (EndTurnAbility, 6 slots, XP on
  1/3/5, lost). Unverified: a Choose inside AddActiveBonus.
- 07 Intensifying Amplitude bottom: APPROXIMATION: +X (0..4 over the five attacks) is a flat +2 on each of the five,
  the same +10 in all; allies only, any range (Sunkeeper2 Sanctify's ally aura; unverified that the aura's slots advance
  on each ally attack).
- 08/09/19 Strengthen / Bless / Ward "Target 2, Range 3": you or allies (`[Self, Ally]`).
- 10/11/15/25/27 grants: ControlActor on an ally (1st edition Call to Action / Commanding Presence); a granted attack
  with no range is melee (`Range: 1, RangeIsBase`). The grant's own range mark is an enhancement on the ControlActor's
  Range, drawn in the "Grant one ally within" row. 25 Inspiring Anthem: no range printed = any ally (Range 99); the
  Strengthen is the ally's own (Filter Self inside the grant).
- 13 Echoing Aria bottom: Loot 1 is its own ability after the attack (the divider).
- 14 Provoke Terror top: Geminate Two-Pronged Entrapment's shape (NullTargeting area, Range 4, inline Push 2 sub with
  Stun). Area "0,1,E|2,1,R|4,1,R|1,0,R|3,0,R|5,0,E" from Secretariat's (0,0)E (0,1) (1,0) (1,1) (2,0) (2,1)E, translated
  as Elementalist2 Burial does (x -> 2x + odd row, rows flipped); the bottom pair sits half a hex left, as the sheet says.
- 16 Soothing Lullaby top: "Heal 1, target all" with no range = you and all allies anywhere (Sunkeeper2 Engulfing
  Radiance). Bottom: APPROXIMATION: the marked monster gains Disarm now (Range 3) instead of at the end of its turn this
  round. Same one disarmed turn when it has already acted; when it hasn't, its turn this round is the disarmed one.
- 17 Symphonic Medley bottom: `SelectAndPlayCard` from the hand, up to 3 (Banner Spear Hail of Spears), only actions
  containing `TSongNotes` (`IsActionValid: Action.Abilities.Any(obj.Name == 'TSongNotes')`). The 1-/2-/3-Note split
  isn't enforced. Unverified (the Name check, and a played song's own TSongNotes removes your Notes). Card stack with
  red X = Permanently Lost.
- 18 Ode to the Gambler: **NEEDS PLUGIN** (treat the first modifier as rolling). APPROXIMATION: once per turn you and
  each ally may switch on advantage for one attack (Geminate Dragonfly Surge's `AttackHasAdvantage` BuffAttack, as a
  toggled aura).
- 19 Hymn of the Oak bottom: "grant all allies: Shield 1" = a round Shield 1 aura on every ally (not you).
- 20 Alluring Serenade bottom: the Pull 3 is a sub-ability on the healed figure (`UseParentTargets`, Filter Ally), so it
  is skipped when you heal yourself. Unverified.
- 21 Change Tempo top: Blinkblade Experimental Adjustment's two-condition NullTargeting, area "2,1,R|1,0,R|3,0,R|4,1,E"
  (Secretariat (0,1) (1,0) (1,1) + (2,0) hex mark).
- 22 Thrilling Solo: **NEEDS PLUGIN** (repeat the whole attack ability, area/conditions included). APPROXIMATION: after
  your first attack ability each round (`DuringActionAbilityOnAttackAbilityFinished`, OncePerRound, Drifter Shockwave),
  one single-target attack with that ability's value +1 and its range (Vars from `TriggerAbility`, Shattersong
  Heartening Harmony's Range-from-Vars). Unverified; if `TriggerAbility` isn't the attack, the extra attack is 1, range 1.
  It also fires after item or modifier attacks.
- 23 Pull the Strings top: the control on the attack's target is the 1st edition Lead to Slaughter fix (sub-ability,
  UseParentTargets, Target 1); bottom = the 1st edition card (the enemy's own attack +0).
- 24 Disorienting Dirge top: APPROXIMATION: "all enemies gain disadvantage on all their attacks" = every attack
  targeting you or an ally gains disadvantage (Deathwalker The Night Takes Shape's aura on `[Self, Ally]`); enemy attacks
  on other targets (objectives) aren't covered. Bottom: the 1st edition Defensive Ditty bottom (one damage prevention
  among allies within range 2 this round).
- 26 Wistful Wounding: Crashing Tide Rancid Brine's Wound buff as Power Ballad's toggled once-per-turn aura. Bottom:
  round Retaliate 2, range 2 on one ally within range 3.
- 29 Grand Finale top: a plain Move 4 with Jump, then a control on every enemy moved through (the Trample fix:
  `AllTargetsOnMovePath` + `Targeting: All`), each performing a melee Attack 3 (enemy's base replaced); unverified for a
  control. The extra Note is a Note choice right away ("this round" not tracked). Bottom: SelectAndPlayCard from the
  hand, then from the discard pile (song actions only, up to 10 each); "skip moving your token this round" is
  **NEEDS PLUGIN**. Permanently Lost.
- 30 Captivating Performance top: end-of-turn Disarm in Secretariat's triangle "2,1,R|1,0,R|3,0,R", Range 3, no hex mark.
  Bottom: "move your tokens back one slot" is **NEEDS PLUGIN** (text only, the Move 4 works).
- Affinities: none (no card infuses an element; the mat prints none).

## Perks (`perks.py`, 18 boxes)

- Benediction: new AMC_GH2E_Soothsinger2_Bless2_M2 (-2, Bless two figures, you or allies, any range).
- Fermata (x3): **NEEDS PLUGIN**: a plain +0 for now (the "skip moving your tokens" text is printed only).
- Rousing Chord: Frosthaven's AMC_Strengthen_Ally_P1. Crescendo, Stunning Note, Dirge: Frosthaven's own cards.
- Call and Response (x3): new AMC_GH2E_Soothsinger2_GrantAlly_R. APPROXIMATION: one grant of Move 2 then Attack 2
  (the ally may skip either), not a choice of one; a Choose inside a modifier draw is unbenched.
- Storyteller: text only (the 1st edition's plugin answer keys on its perk ID; shared change 3).
- Harmonics (a Note when you perform a Song with Notes left): **NEEDS PLUGIN**, text only.
- Lullaby (linked, cost 2): on your long rest, a Note choice (ExecuteAbilitiesAfterLongRest; at the rest, not "during
  ordering of initiative"). The allies' -10 initiative is **NEEDS PLUGIN**.
- Masteries: none written. "Perform 7 different Song actions" and "create 20+ Notes and use each in a Song" need plugin
  tags (shared change 4).

## Shared changes wanted

1. **Note icons**: gh_port gives every Gloomhaven-file resource the sprite `GHClass_<owner>`, so both Notes show the same
   class icon. Wanted: two distinct status icons (treble / bass clef, or Frosthaven's Shattersong resonance style) for
   SoothsingerHighNote / SoothsingerLowNote, with the count, and the GHStatusIcons class-icon lookup for
   `GHClass_Soothsinger2` (check it resolves at all for a 2e class).
2. **Song tag + song status icon** for 2e songs: GHCardStyle's "Song" tag and GHStatusIcons' music note key on
   `PlaySong`. A 2e song half is recognisable by its `TSongNotes` ability (every song has it, nothing else does).
3. **Storyteller** perk: let the plugin's 1st edition answer (8 XP each at scenario end if the battle goal was met) also
   take perk ID `Soothsinger208`.
4. **Plugin rules** (in order of value): (a) refuse a song half / its TSongNotes when the Notes it costs aren't there
   (cost per card: the `_T` text key of each song) and cap Note gains at one per round; (b) song token skip / move back
   (Grand Finale bottom, Captivating Performance bottom, Fermata perk) = hold or step back `TSongTimer`'s / TSong's
   `TrackerIndex` on the Soothsinger's persistent song bonuses; (c) Ode to the Gambler's rolling first modifier;
   (d) Thrilling Solo's true repeat; (e) Harmonics perk; (f) mastery tags.

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Soothsinger2`: packcheck 30 cards, 0 problems, 0 warnings;
rulecheck 44 ok, 0 with problems; character resources SoothsingerHighNote, SoothsingerLowNote. **CHECK PASSED
Soothsinger2** (2026-11-01).
- Perk Benediction: modifier rewritten, `AMC_GH2E_Soothsinger2_Bless2_M2` blesses with a NullTargeting + `Conditions: [Bless]` (same filter, range 99, target 2, ControlTargetingActor) (a Bless ability on a modifier card stops the game when drawn). Exact.

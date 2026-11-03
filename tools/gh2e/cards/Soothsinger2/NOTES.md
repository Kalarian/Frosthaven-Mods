# Soothsinger (2nd edition): translation notes

All 30 cards written by hand from `tools/gh2e/sheets/Soothsinger.json` (no Xipe data; Secretariat's areas used for
Provoke Terror, Change Tempo and Captivating Performance). Shapes come from Frosthaven's own cards, the converted 1st
edition Soothsinger and the other 2e folders; each file's header comment names its source. Enhancement marks were
counted against the sheet by script (all 30 halves match; the area hex marks included).

## The class systems

**Notes = two character resources** (`resources.yml`): `SoothsingerHighNote` and `SoothsingerLowNote`, `MaxAmount: 2`
each (the mat's cap; `CCharacterResource.Amount` clamps to it).
- *Gaining*: a half with a clef gets a last ability `TNote`/`BNote` (NullTargeting on self, `ResourcesToAddOnAbilityEnd`),
  the Plagueherald2 supply's shape. A Null/NullTargeting ability always "happens", so the Note comes even when the
  half's main ability had no target. "Once each round ... you may": the Note ability is offered (CanSkip, no
  AutoTrigger) and places a hidden round marker bonus (MiscVars `GH2E_NoteThisRound`); its StartAbilityRequirements
  refuse a second gain while a marker is on you. The "in addition" gains (scenario start, Increase Cadence bottom,
  Grand Finale top, Harmonics, Lullaby) are Choose abilities without the marker.
- *Note of your choice* (start of scenario in `character_extra.yml`, Increase Cadence bottom, Grand Finale top, perk
  Lullaby): Frosthaven's `Choose` (Metal Mosaic Energy Conversion) between a High and a Low gain.
- *Spending*: every song half ends with `TSongNotes`, a hidden `Duration: Ability` bonus whose finish runs
  `RemoveAllInstancesOfResourcesOnFinish: [High, Low]` (the rule "whenever you perform a Song, remove all of your
  Notes"). `CClass.MarkAbilityActiveBonusesFinished` finishes it right after the ability, so a Note gained later the same
  turn (a bottom half) survives. **Unverified** in game.
- *Cost* (plugin GHSoothsinger, 2026-11-02): every song's TSongNotes carries its printed cost as MiscVars
  `GH2E_SongHigh` / `GH2E_SongLow`. The card hand greys out a song top's action (the basic Attack 2 stays) while the
  Notes on the mat don't cover it (FullAbilityCard.ToggleActionInteractivity after CardsActionControlller
  .DisableForgoneActions); Symphonic Medley / Grand Finale plays ignore it (a SelectAndPlayCard is running). When the
  song is paid, the Notes matching the cost are "used", the rest wasted (mastery 2, approved default; GH2e FAQ 8.13).

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
- Grand Finale bottom and perk Fermata hold every song token this round (plugin GHSoothsinger: the TSong / TSongTimer
  tracker neither advances nor awards slot XP at this round's trigger; all or nothing, GH2e FAQ). Captivating
  Performance's move back: data, see 30.
- Status rows: a 2e song shows the Soothsinger's music note on every figure it reaches (her, and each figure inside an
  aura song's aura; GHStatusIcons via GHSoothsinger.SongsReaching); the card face carries the "Song" tag (GHCardStyle
  keys on TSongNotes). Notes: Frosthaven's Shattersong icons, `Positive_Shattersong` (High) and `Positive_Resonance`
  (Low) (resources.yml "own sprite", which gh_port keeps).

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
- 04 Power Ballad: Shattersong Din of Battle's aura (+1 Attack, once per turn per figure), persistent, without the
  toggle (HIVE Heavy Metal: OncePerTurn single-target buff), so it lands on each figure's first attack of its turn.
- 06 Increase Cadence bottom: a Note choice at the end of this and the next five turns (EndTurnAbility, 6 slots, XP on
  1/3/5, lost). Unverified: a Choose inside AddActiveBonus.
- 07 Intensifying Amplitude bottom: +X = the slots passed (0..4), Snowdancer Snowball's BasedOnActiveBonusVarsStrength
  (`Strength: obj.TrackerIndex + TargetSelectionNumber - 1`, IsSingleTargetBonus) on an ally aura, any range (Sunkeeper2
  Sanctify's; unverified that the aura's slots advance on each ally attack: bench).
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
  Radiance). Bottom: the Disarm (MiscVars `GH2E_DisarmAtTurnEnd`) is held by the plugin as the token on the monster
  (a custom effect on its status row) and given at the end of that monster's turn this round (CPhaseEndTurn; applied
  during its own turn, so it lasts through its next turn, as a condition gained at a turn's end does); a monster that
  already acted this round gets nothing, the token goes at the round's end.
- 17 Symphonic Medley bottom: three `SelectAndPlayCard` from the hand, one card each (Banner Spear Hail of Spears):
  a 1-Note, a 2-Note and a 3-Note song (`IsActionValid` tests the song's TSongNotes MiscVar GH2E_Song1/2/3, which every
  song card carries with its printed cost; Captivating Performance is the only 4-Note song). Unverified (the Name check, and a played song's own TSongNotes removes your Notes). Card stack with
  red X = Permanently Lost.
- 18 Ode to the Gambler: Tome of Conflict's NullTargeting aura on you and all allies with the marker `GH2E_OdeRolling`;
  the plugin asks each figure (its player) on the first modifier card it draws each turn, after the card is drawn,
  whether to treat it as rolling (CActor.TreatFirstAttackModifierAsRolling, the engine's rolling copy).
- 19 Hymn of the Oak bottom: "grant all allies: Shield 1" = a round Shield 1 aura on every ally (not you).
- 20 Alluring Serenade bottom: the Pull 3 is a sub-ability on the healed figure (`UseParentTargets`, Filter Ally), so it
  is skipped when you heal yourself. Unverified.
- 21 Change Tempo top: Blinkblade Experimental Adjustment's two-condition NullTargeting, area "2,1,R|1,0,R|3,0,R|4,1,E"
  (Secretariat (0,1) (1,0) (1,1) + (2,0) hex mark).
- 22 Thrilling Solo: a marker persistent bonus (`GH2E_ThrillingSolo`); after your first attack ability from an ability
  card each round (the round is spent even before the song is out), the plugin injects a copy of the whole ability
  (area, targets, effects; Frosthaven's CRepeatAbilityBaseBehaviour copy) with +1 Attack, targets chosen anew, which
  can be skipped. Item and modifier attacks don't count.
- 23 Pull the Strings top: the control on the attack's target is the 1st edition Lead to Slaughter fix (sub-ability,
  UseParentTargets, Target 1); bottom = the 1st edition card (the enemy's own attack +0).
- 24 Disorienting Dirge top: "all enemies gain disadvantage on all their attacks" = a disadvantage BuffAttack aura on
  every enemy (Decanter of Mists: Attack 0, AttackHasDisadvantage, AuraFilter Enemy), objectives' attackers included. Bottom: the 1st edition Defensive Ditty bottom (one damage prevention
  among allies within range 2 this round).
- 26 Wistful Wounding: Crashing Tide Rancid Brine's Wound buff as Power Ballad's once-per-turn aura (no toggle: the
  first attack each turn). Bottom:
  round Retaliate 2, range 2 on one ally within range 3.
- 29 Grand Finale top: a plain Move 4 with Jump, then a control on every enemy moved through (the Trample fix:
  `AllTargetsOnMovePath` + `Targeting: All`), each performing a melee Attack 3 (enemy's base replaced); unverified for a
  control. The extra Note is a Note choice right away ("this round" not tracked). Bottom: one SelectAndPlayCard over
  the hand and the discard pile together (`CardPile: [Hand, Discarded]`, Expertise Potion; song actions only, up to 20); then `BSkipTokens` (marker `GH2E_SkipSongTokens`):
  the plugin holds every song token this round. Permanently Lost.
- 30 Captivating Performance top: end-of-turn Disarm in Secretariat's triangle "2,1,R|1,0,R|3,0,R", Range 3, no hex mark.
  Bottom: "move your tokens back one slot" = MoveActiveBonusTracker -1 with up to 15 cards picked (Drifter Fortitude,
  AMC_MoveTrackerBackOneSlot; own-class cards only); the non-song trackers (Increase Cadence and Intensifying Amplitude
  bottoms, Disorienting Dirge bottom) carry ImmuneMoveActiveBonusTracker (Lucky Dice). A song on its first slot can't
  go back. The player picks each song (a plugin could take them all at once).
- Affinities: none (no card infuses an element; the mat prints none).

## Perks (`perks.py`, 18 boxes)

- Benediction: new AMC_GH2E_Soothsinger2_Bless2_M2 (-2, Bless two figures, you or allies, any range).
- Fermata (x3): new AMC_GH2E_Soothsinger2_Fermata_P0, a +0 with a NullTargeting marker (`GH2E_SkipSongTokens`); the
  plugin holds the song tokens this round when it applies (CAbilityAttack.ProcessModifierAbilities).
- Rousing Chord: Frosthaven's AMC_Strengthen_Ally_P1. Crescendo, Stunning Note, Dirge: Frosthaven's own cards.
- Call and Response (x3): new AMC_GH2E_Soothsinger2_GrantAlly_R. APPROXIMATION: one grant of Move 2 then Attack 2
  (the ally may skip either), not a choice of one; a Choose inside a modifier draw is unbenched.
- Storyteller (plugin): at the scenario's end (results screen / EndCurrentScenario), when the holder's battle goal is
  complete, 8 XP to every character in the scenario.
- Harmonics (plugin): when a Song removes a Note it didn't need, a Note of your choice right after (outside the
  once-a-round limit; GH2e FAQ: "refunds" a Note that would otherwise be wasted).
- Lullaby (linked, cost 2; plugin): when initiative is ordered (GameState.SortIntoInitiativeAndIDOrder at the end of
  card selection) in a round the holder long rests: a Note of your choice, and each other character is asked whether
  to lower their initiative by 10 (the engine's own initiative bonus, this round).
- Masteries (plugin tags, names ours: the sheet prints none): Medley, `FrosthavenMods_SSDistinctSongs` Value 7 (songs
  performed, by card); Perfect Pitch, `FrosthavenMods_SSNotesAllUsed` Value 20 (Notes created = resource gains that
  placed a Note; none wasted by a Song; none left at the end). Counts live in the plugin for the scenario (a reload
  mid-scenario starts them again).

## Shared changes

All four done 2026-11-02 (GHSoothsinger.cs, GHStatusIcons, GHCardStyle, gh_port "own sprite").

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Soothsinger2`: packcheck 30 cards, 0 problems, 0 warnings;
rulecheck 44 ok, 0 with problems; character resources SoothsingerHighNote, SoothsingerLowNote. **CHECK PASSED
Soothsinger2** (2026-11-01).
- Perk Benediction: modifier rewritten, `AMC_GH2E_Soothsinger2_Bless2_M2` blesses with a NullTargeting + `Conditions: [Bless]` (same filter, range 99, target 2, ControlTargetingActor) (a Bless ability on a modifier card stops the game when drawn). Exact.
- Re-audit fixes (2026-11-02, fix-reaudit-B; supersede the Call and Response line above): Call and Response's grant keeps
  only the ability the drawer's player picks (Move 2 or Attack 2; GHSoothsinger.CallAndResponse). Captivating
  Performance bottom moves EVERY active song back whatever the picker held (EverySongBack, "GHRule SongTokenBack"). The
  masteries' counts are hidden resources on her (GH2ESongsSung bit mask, GH2ENotesMade, GH2ENotesWasted), saved with
  the scenario. Codex: "all your active Songs", "multiple Songs".

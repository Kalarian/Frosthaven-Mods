# Plan: level up between forced scenarios (house rule)

Status: researched 2026-10-27, not built. Pick up with the user at the computer.

## The idea

Frosthaven only levels characters in the outpost's Downtime phase. Forced (mandatory) chains skip the outpost, so
experience earned in the first scenario of a chain waits until the chain ends. The house rule lets the party level up
(new card, perk marks) between forced scenarios, using the game's own outpost screens. Own host switch, e.g.
**"Level up between forced scenarios"** (every mod has a switch).

## Where it matters (game data, vanilla ruleset)

Forced links (`UnlockMandatoryScenario`, 19 links into 16 scenarios): 2/3 -> 4, 11 -> 19, 36/37 -> 44, 43 -> 51,
49 -> 53, 50 -> 54, 63 -> 64, 67 -> 68, 72 -> 73, 75/76 -> 77, 87 -> 88, 90 -> 91, 96 -> 97 -> 98, 129 -> 130,
132 -> 133; 44/51 end in a forced choice (`ChooseUnlockMandatoryScenario`). Optional links (`UnlockLinkedScenario`,
continue or go home) don't need the rule: the party can go home and level.

## What the game does today (decompiled code)

- **Auto-start:** `FHMapView` (~line 1517): no active scenario and exactly one mandatory scenario -> the party leader
  runs `FHRPC.Campaign.Scenario.StartScenario` at once. No pause between the chained scenarios.
- **Level check:** `CharacterController.Action_LevelUpCheck(character)` raises the level while XP allows and adds card
  unlocks (`CardUnlocksPerLevel`) and perk marks (`PerkMarksPerLevel`). Called from `PhaseController.Downtime()` for
  party characters, and on XP gain only when `CanLevelUp`.
- **The gate:** `CharacterController.IsCharacterManagingPhase` is false while `ScenarioController.HasMandatoryScenario`,
  outside `OutpostPhase.Downtime`, after `CompletedDowntimeScenario`, or with an active scenario.
  - Gated by it: `CanLevelUp`, `CanUnlockCard` (-> `UnlockCard`), hiring/creating, `RemoveCharacterFromParty`,
    `DeleteCharacter`, `AddCharacterFromParty`.
  - NOT gated: `LearnPerk` (marks + availability only), `SelectCard` (hand size + disabled cards only).
- **Screens that already exist:** `UnlockCardsViewState` (new cards), `CharacterPerksViewState`,
  `HandManagementViewState`; `EditCharacterViewState` for "hero levelled up". The quick-access bar's New Cards tab is
  enabled by `CanLevelUp` (`QuickAccessBarViewService.IsSubCategoryInteractable`); Perks and Hand are always enabled.
  In the briefing (`PartyViewService.ManagePartySlot`), clicking a party slot opens Hand management when the hand is
  incomplete.

## The build

1. **Rule switch** (ModRules bit, campaign rule, host-controlled, synced like the others).
2. **Level check at the chain:** a postfix after the scenario-completion command (runs on every PC): if the switch is on
   and `HasMandatoryScenario`, call `Action_LevelUpCheck` for each party character.
3. **Relaxed gates, narrowly:** prefixes on `CanLevelUp` and `CanUnlockCard` only: allowed when the switch is on,
   a mandatory scenario is waiting and none is active. Hiring/retiring/party changes stay blocked.
4. **Hold the auto-start:** a prefix on the FHMapView auto-start: if anyone has unspent card unlocks or perk marks,
   open the game's New Cards / Perks screens instead (the outpost notifications' code shows how to open them for a
   character), with a Continue button; Continue (party leader) runs the game's own `StartScenario`.
5. **Hand:** picked in the briefing as normal (already works there).
6. **Mod guide + companion Mods tab** entries; hookcheck covers the new patches.

## To verify in game first (one launch, test save at the end of scenario 96)

- What `Outpost.Phase` and `CompletedDowntimeScenario` hold at that moment (so the relaxed gates match exactly this case).
- Whether the New Cards / Perks screens draw correctly between the two scenarios (map between travel states).
- Co-op: holding the leader's auto-start must not leave the other players on a different screen; the unlock/perk
  commands are the game's own `MultiplayerCommand`s (CharacterOwner) and must succeed on every PC with the patch.

## Risks

- The auto-start hold in co-op (main risk).
- `IsCharacterManagingPhase` also guards things we must keep blocked; never patch it wholesale.
- Rules purity: board-game rules level up in Downtime only; this is an "easier, not broken" house rule (you only get
  a level you already earned, one or two scenarios early).

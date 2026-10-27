# Plan: choose the order of characters who long rest in the same round

Status: researched 2026-10-27, not built. Implement later with the user.

## The rule the game skips

Frosthaven rulebook p.19, "Resolving ties": "If there is a tie in initiative between multiple characters, compare the
initiative values of those characters' second played cards to break the tie. If there is still a tie, **the party
decides the order** for those characters." Long resting sets initiative 99 with no second card, so two or more resters
are always a party decision. The digital game never asks.

## What the game does (decompiled code, ScenarioRuleLibrary)

- `CPlayerActor.Initiative()`: long rest (`CharacterClass.LongRest || IsLongRestSelected`) -> 99 (+ initiative bonus).
- `CPlayerActor.SubInitiative()`: long rest -> 99 as well (normally the second card's initiative).
- `GameState.ActorInitiativeComparer.CompareWithClass` order of tie-breaks: initiative, then player sub-initiative,
  summons next to their summoner, players before monsters, monster rules, and finally **actor ID**
  (`return iD.CompareTo(iD2)`). So two long-resters always go in actor-ID order: fixed, never the party's choice.
- `Compare` also respects `TurnOrderSoFar` (actors who already acted keep their place mid-round).
- The round's order: `GameState` (~line 3870) builds `s_InitiativeSortedActors` and sorts it with
  `s_ActorInitiativeComparer` (summons inserted before their summoner).
- **The same comparer decides monster focus ties** (`s_ActorAdjustedInitiativeComparer` in `CActorStatic` ~710/1016 and
  `CAIMoveNew` ~1132: "the earliest in initiative order"), and a few targeting orders (`CAbilityControlActor`,
  `CAbilityDamage`, `CustomAbility` ~2920). Reordering resters therefore also changes which rester a monster focuses
  when distances tie: that is the rule as written (the party's order is the initiative order), and the main reason
  players want the choice.

## The build

1. **Rule switch** (ModRules bit, campaign rule, host-controlled; every mod has a switch).
2. **State:** a per-round order of the long-resting characters (list of actor IDs), cleared at round end. Deterministic
   and identical on every PC.
3. **Rules patch:** prefix/postfix on `ActorInitiativeComparer.CompareWithClass`: when both actors are players with
   long rest (equal initiative and sub-initiative) and the chosen order lists both, compare by their place in it;
   otherwise the game's own answer. Affects the turn order and monster focus alike (by design, see above).
4. **When the choice is made:** after cards are revealed (the round's sort, ~GameState 3870), before the first turn,
   only when two or more characters long rest. The game re-sorts `s_InitiativeSortedActors` after the choice.
5. **UI, simplest first:** a small dialog listing the resting characters with up/down (or "who goes first?" when there
   are two). Default = the game's current order, so skipping the dialog changes nothing. Later option: drag on the
   initiative track (`InitiativeTrack.cs`; it already has Select/SelectNext/UpdateActors).
6. **Co-op:** the choice must be one command every PC applies (the party leader decides, or the host); the mods
   already send their own synced actions for house rules (see how GiveResources/GiveItems sync), and the round can't
   start its first turn until the choice arrives everywhere. CoopLog/desync export to check it in the co-op test.
7. Mod guide + companion Mods tab entries; hookcheck covers the comparer patch.

## To verify in game first

- Exactly when after card reveal the order is fixed (and that re-sorting `s_InitiativeSortedActors` then is safe).
- That the initiative track redraws in the new order (`InitiativeTrack.UpdateActors`).
- Long-rest heal/recover timing is unaffected (it happens on each rester's own turn).
- Co-op: both PCs show the same order and the same monster focus afterwards.

## Risks

- The comparer is used widely (focus, targeting orders); the patch must only touch the rester-vs-rester tie and only
  with a chosen order present.
- Mid-round changes (`TurnOrderSoFar`) must stay as the game handles them.
- Co-op sync of the choice (the main work).

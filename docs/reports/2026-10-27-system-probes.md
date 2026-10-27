# System probes: songs, dooms, augments in Frosthaven (2026-10-27)

Before Group 2 starts, one converted card for each Gloomhaven tracker system that Frosthaven's code keeps but none
of its own cards drive, played on the bench (harness `tools/probe.sh`: Soothsinger Power Ballad, Doomstalker Rain of
Arrows, Mindthief Feedback Loop, in a probe party with the Drifter). The companion (Beast Tyrant's bear) waits for the
summon-model step (G3). The classes were converted with placeholder extras (gh_port CLASS_EXTRAS) and are not part of
the pack that ships.

## What the probes found, in the order they fell

| # | Finding | Where | Fix | State |
|---|---|---|---|---|
| 1 | A `- Song:` or `- Doom:` card-layout element throws at load: Frosthaven's `misc_gui` bundle no longer has `gui/SongContainer.prefab` or `gui/DoomContainer.prefab` (the C# classes survive, the prefabs are gone; `AugmentContainer.prefab` is still there). Card generation runs for every card at load, so one such card takes the whole game to its error screen. | `CreateLayout.CreateSong/CreateDoom` | gh_layout.convert_trackers: a Song or Doom element becomes a plain Active element (the song/doom is a persistent bonus with the ability's own layout). | fixed in gh_port |
| 2 | Pilfer's `LootData: AdditionalLootEffect: ExtraGoldPerAdjacentEnemy` fails to parse (the value is unknown to Frosthaven), and one unparsable card file inside the game's parallel class loader took the whole load down (`Character 'QuartermasterID' has no ability cards`). | `AbilityData` / `CharacterClassManager.LoadCharacterClass` | gh_port CARD_FIXES 20163 drops the LootData block until the plugin does the extra gold. | fixed in gh_port |
| 3 | A card whose layout shows a summon is drawn from the summon's definition; the pack carried no `HeroSummon` defs, so `CreateLayout.CreateSummon` threw (NullReference) the moment such a card was generated. Hit both the Doomstalker's and the Mindthief's hands. | `CreateLayout.CreateSummon` | gh_port now copies the HeroSummon defs the class's cards name into `Global/HeroSummons/` (enhancement slots renamed, name strings added); packcheck fails a `Summon:` with no def. The summon's *model* is still the G3 step. | fixed in gh_port |
| 4 | **Songs work at the rules level.** Power Ballad's `PlaySong` started, performed and completed (event log), no error, the song left the hand as a persistent card. | — | Songs-only rerun: the song applies to the Soothsinger as her own active bonus (ApplyToActor PlaySong -> Soothsinger), the way Gloomhaven's songs work; Frosthaven's `UIActiveBonusBar` still handles `SongEffect`. No marker throws. Whether allies get the +1 is the Soothsinger port's effects pass. | **works** |
| 5 | **Dooms apply but their marker throws.** Rain of Arrows' `AddDoom` reached the target (ApplyToActor recorded); the next worldspace-UI refresh threw `NullReferenceException` in `WorldspaceUI.EffectsBar.ToggleDoomEffect` (the doom marker's asset is gone like the prefabs above) and the game showed "UNKNOWN ERROR". | `EffectsBar.ToggleDoomEffect` | The null is `UIInfoTools.GetPreviewEffectConfig("Doomstalker")`: Frosthaven's preview-effect config list has no entry for the class (`ToggleDoomEffect` reads `.previewEffectIcon` off it). Plugin: add a `PreviewEffectConfigUI` for each pack class (icon = the class icon) to `UIInfoTools.previewEffectConfigs` before the dictionary is built. Small; UI only, the rules are fine. | **plugin needed before the Doomstalker** |
| 6 | **Augments break the attack.** Feedback Loop's attack (an Augment card) threw `NullReferenceException` in `CAbilityAttack.StartAttackingActors`, inside the Scenario Rule Library: the augment path (`m_AbilityAugments` → `ProcessAbilityAugments` → `CPhaseAction.StackNextAbilities`) hits a null. The card itself draws (its layout element exists). | `CAbilityAttack.StartAttackingActors` | Lead: `m_AbilityAugments` is built in `CAbilityAttack.Start` (line ~627, from `targetingActor.Augments`) and read in `StartAttackingActors` (line ~4234, `.Count`); the NullReference there means the attack reached `StartAttackingActors` without that `Start` having run for this instance, or with `targetingActor.Augments` null. Next step: a prefix on `StartAttackingActors` logging the two fields, on the bench. Rules, not UI; harder than the doom marker. **Resolved the same day** with `tools/ilat` on the trace's IL offset: the statement is `m_AugmentAbilitiesTargetCount = m_MultiPassActors.Count`, and `m_MultiPassActors` is only created further down the same method, so the first pass of any multi-pass attack (the default for every attack; Frosthaven's area attacks set `MultiPassAttack: False`) with an ability augment throws. An engine bug Frosthaven never reaches. `GHAugments` builds the target list first, only when augments are pending. | **fixed in the plugin (GHAugments), bench pending** |

## What this means for the groups

- **G2 (Cragheart, Plagueherald, Nightshroud, Berserker)** is untouched: none of them uses a tracker system. The
  Group 2 trial conversion (this day) also passed: 4 classes convert, their crossover sheets are typed, the
  Cragheart's model exports after a prefab-name fix, packcheck is clean apart from icon warnings.
- **G4**: the Soothsinger stays (songs work; only the display is to confirm). The **Mindthief** carries a rules-level
  plugin item (6) and 3 summons: it is now the heaviest class of G4 and the candidate to slide to G5 if the augment
  null is not quick.
- **G5**: the Doomstalker needs the doom-marker patch (5), a contained UI fix.
- The three fixes in gh_port (1-3) apply to every later class: summon cards and tracker layouts no longer need to be
  discovered class by class.

## Harness notes

- `tools/probe.sh setup|run`: the probe party (base save + `unlockclasses` + heroes created + hands + scenario) and the
  three benches, each ending in a `bonuses` listing, a UI listing and a screenshot. The bench save is listed in
  `tools/bench.sh` (BENCHES).
- `setup.py`'s party step now verifies the party and goes round once more (three roster clicks had landed while Edit
  Party was closed).
- `effects.py` knows PlaySong/AddSong/AddDoom/AddAugment/Augment (applied abilities: they need an ApplyToActor record).

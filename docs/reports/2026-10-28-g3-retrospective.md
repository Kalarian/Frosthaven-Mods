# Group 3 retrospective (Hatchet, Red Guard, Voidwarden, Demolitionist)

Night of 2026-10-28, 23:55 to 06:33: port to 183 of 224 halves verified, plus 21 re-checks of the released groups. 58 game launches. The companion review of the harness itself (performance, driver, process) is in the harness repo: `docs/reports/2026-10-28-harness-review.md`.

## Did the Group 2 changes pay off?

| Change made after Group 2 | Result in Group 3 |
|---|---|
| `precedent.py`: bench the never-seen shapes alone first | 39 flagged. Of the 10 real problems found tonight, 8 were in the flagged set (Care Package, Lure of the Void, Eye of the Void, Executioner's Axe, Quick Turnaround, Explode, Rubble, Fire Bombs). The 2 misses: The Favorite (a reference to a Gloomhaven resource definition, not an ability shape) and Grave Defense (a perk; the census only reads cards). |
| `rulecheck` (built tonight, 00:05-00:12) | Would have saved the first 25 minutes (error screen, investigation). Now gates every port and release. |
| A look-list for you per group | Written before the first screenshot reached you. |
| Bench flagged halves before the full deck | The full deck then ran 127 halves in 2 h with no port bug found in it: the flagged benches had taken them all. |

## What cost the most

Time per stage: prep 35 min (two failed base set-ups), flagged benches 2 h 40 (three batches, two stopped for fixes), re-tests 40 min, released-group re-checks 58 min, full deck 1 h 53, investigations about 1 h (driver hitches 20 min, rulecheck 15 min, a wrong plugin patch 20 min).

1. **A failure costs a whole bench cycle, and most needed several.** Re-test cycles: Explode 4, Rubble 3, Executioner's Axe 3, Care Package 2, Lure 2, Eye of the Void 2, Sand Devil 2. About 18 cycles of 7-10 minutes, so roughly 2.5 of the 6.5 hours. Each cycle relaunches the game (1 min), re-reads hands, drives 4 rounds. *Fix:* retry in place. Keep the game up, reload the round-1 save with the driver's own `load` (about 20 s), replay the one half. Also: after a set-up change, bench the affected half with `--keep` and read the live valid-target list before launching a batch.

2. **One class per bench while three heroes idle.** 12 benches for 34 halves (84 min). The bench save holds all four heroes; a bench could carry 3 halves of each class per launch, so 3-4 launches instead of 12. *Fix:* `gpass.sh flagged` groups by launch, not by class.

3. **The driver's own hitches went unmeasured for three days.** Found at 01:17 because benches felt slow; fixed by 01:36 (214 to 17 hitches per 90 s). Every earlier pass paid it. *Fix:* `hitchprobe.sh` is now a tool; make it part of `selfcheck.py` so a pass never starts on a harness that hitches.

4. **Checker gaps found mid-pass.** Twenty ability kinds were silently skipped; Executioner's Axe sat as "unplayed" after it played, and the Group 2 re-check cost 58 min. *Fix:* `selfcheck.py` asserts that every ability kind in the pack is known to `effects.py`, so a new class's kinds are learned before its pass.

5. **Set-up rules written one failure at a time.** The Favorite chain took 3 iterations (resource, kill, loot), the rock rule 3 (beside the hero, beside an enemy, under the hero), the destroy prompt 2. *Fix:* `precedent.py` prints each flagged half's `StartAbilityRequirements` and filter keys, and `Cheats` reports "no set-up rule for requirement X" at plan time, so the rules are written before the bench, not after it fails.

6. **Process accidents.** A stale `STOP` file from the previous day aborted the first base set-up (10 min). An in-place edit of a running `gpass.sh` made the old process run its `run` branch an hour later, and its stray watcher wrote probes into the single command slot for an hour. A plugin patch written on a wrong assumption (`CAbilityDamage` does not derive from `CAbilityTargeting`) cost 20 min; the same-shape card Fire Bombs had already passed, which should have been checked first. *Fixes:* clear `STOP`, `pending.json`, `stuck.json`, `error.txt` at every launch; never edit a running script (copy, then `mv`); before patching the engine for a half, find a passing half of the same shape.

7. **The chain died on one prompt and waited for morning.** At 06:25 the full deck stopped on the "infuse any element" picker, which the driver cannot answer. Nothing excluded that half and restarted; 41 halves waited. *Fix:* an unknown-UI fallback (dump the clickable tree, screenshot, try the safe order Escape / skip / first option, mark the half "needs human") and an exclusion list the scheduler honours.

## What went well

- Port to first scenario in 35 minutes, including two failures that previously would only have shown in play.
- Care Package was fixed and verified within the hour; The Favorite's resource port took 25 minutes end to end.
- Both summons (Sand Devil, Ward) worked in play the first time: the summon pipeline built in advance held.
- The full deck verified 127 halves in 2 hours, about one per minute, with no stall until the picker.

## The biggest verification hole

A half whose only ability is an active bonus passes as "a bonus for later": the card was played, but the bonus's later trigger is never checked. Count: Group 1 46 of 310 halves, Group 2 28 of 234, Group 3 29 of 224. About 12% of every class's halves are verified only as "played". The game records `CActiveBonus` triggers; recording them and judging the bonus by its first trigger would close this. The harness review has the cost.

## Changes for Group 4 onward (this file's own list; the harness review ranks the rest)

1. Retry in place; no relaunch per re-test.
2. Multi-class benches.
3. `precedent.py` reads perks and masteries too, and flags references to pack-defined data (resources, summons, props).
4. Set-up rules from requirements and filters, written before the bench.
5. `selfcheck.py`: every pack ability kind known to the checker; the hitch probe passes.
6. Unknown-prompt fallback and a scheduler exclusion list.
7. Stale state cleared at launch; scripts never edited while running.
8. Active-bonus triggers recorded and judged.

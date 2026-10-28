# Group 2 retrospective (Cragheart, Plagueherald, Nightshroud, Berserker)

Shipped in v0.9.0 on 2026-10-27, about a day after the trial port. 233 of 234 card halves were seen to work in the game. A light co-op test went fine.

## What worked

- **System probes before the port.** Songs, dooms and augments were each tried on one card before any class depended on them. That took dooms and augments off G4's critical path early, instead of mid-pass.
- **Outcome checking (effects.py) instead of "it played".** "Verified" meant the effect was seen in the game's own event log. That's why 233/234 means something.
- **State first, then play.** Cheats put each half into its situation: the hero below half health, a poisoned enemy beside the hero, a second enemy beside the target. There was no waiting for the right situation to come up.
- **Prep in parallel.** While the passes ran, I did G3's crossover sheets, stories, class extras, the summon pipeline and the G4-G6 trial port.

## What cost the most

**1. Shapes Frosthaven has never run.** The expensive halves were the ones whose ability shape no Frosthaven card uses. Nothing in the engine had ever run those shapes:
- Wings of the Night's bonus threw an error and stopped a run.
- Rock Slide's damage hit its own rocks.
- Massive Boulder and Blind Destruction got stuck on a sub-ability that damages the figures next to the target.
- Growing Rage and Seeing Red have a health requirement that a full-health bench can't meet.
- Virulent Strain's poison is still open.

Each was found one bench at a time, and some took 3-4 benches. *Fix now built:* `tools/precedent.py` lists every half with a feature (parent/sub pair, bonus behaviour, targeting, requirement, filter) that no Frosthaven card shows. Run on G2, it flags 32 of 234 halves, including 10 of the 12 that cost the most retries. Those 32 get a bench alone, first, before the full pass.

**2. You found the visual problems; my checks found none.** The white cylinder, the static cone, the Nightshroud's white legs and arms, the missing idle particles, the magenta patches and the stats web all reached you first. My pass checked rules, not looks. *Fix:*
- A static material audit on the exported models and effects before anything is installed. It flags:
  - a shader with no Frosthaven stand-in;
  - a texture that won't resolve;
  - a distortion-only material (would draw white);
  - an untinted glow over 1;
  - particle renderers that are dropped.
- The new kinds of trouble you found (the cylinder came from an unknown shaft shader, the legs from a distortion shell) are now checks, not something to wait for you to spot.
- A UI parity list per class: the class screen web, the codex page and the card faces, each compared with a Frosthaven hero.

**3. Set-up rules arrived one failure at a time.** Fatal Fury took three rounds of fixes, and Growing Rage, Massive Boulder and Sentient Growth two or three each. Each fix meant install, wait for the batch, read, fix again. The census gives the list up front. For each flagged half, the set-up is decided and checked with a `--keep` bench before the batch, not after it fails.

**4. Harness accidents I caused:**
- Session pruning deleted 140 verifications.
- Two script chains ran at once (Git Bash's `ps` hides script names).
- CRLF broke a retest list.
- The single-slot command file lost a command.
- An empty material array crashed the game twice.
- The harness's own whole-scene scans made the game stutter every second.

Each became a tool or guard the same day (ledger, `killtree.sh`, `tr -d`, `say()`, the crash rule, the 15-s throttle). The lesson: measure the harness's own cost and failure modes before a long pass.

**5. Text needed two drafts.** The codex pages were too detailed at first. The style rule (Frosthaven's Frozen Fist / Blinkblade entries) is written down now, so G3's pages start in that style.

**6. Shipped with two known gaps.** Virulent Strain (bottom)'s poison doesn't land. The Empowering Night perk hasn't been tried in a game. Both are listed in the release reply; neither blocks play.

## Changes for Group 3 onward

1. Trial port, then `packcheck` and `precedent.py`, then a material audit, before any game launch.
2. Bench every flagged half alone with its set-up, then the full pass for the rest.
3. Write each visual check as a list for you, instead of waiting to hear about it (the stand-in shaders used, the effects to look at, one card half per effect).
4. Your decisions are gathered in one place at the start (rewards, approximations), with my default applied so the work doesn't wait.

"""Simulate Frosthaven's attack modifier deck as the digital game codes it, vs a correct implementation.

Game code (decompiled):
- build (CCharacterClass.ResetAttackModifierDeck): for each pool card in order, current.Insert(rng.Next(0, current.Count), card)
  -> Next(0, n) is 0..n-1, so a card is never inserted at the end: the pool's first card (a +0) stays at the bottom.
- draw (CActor.DrawAttackModifierCard): take current[0]; if the deck is empty, reshuffle first.
- reshuffle (CActor.ShuffleAttackModifierCards): each discarded card inserted at rng.Next(0, current.Count), then
  current.Shuffle() (Extensions.Shuffle): num--; j = rng.Next(num); swap(j, num)  -> Sattolo's algorithm (j never = num).
- the deck is reshuffled at the end of a round in which a null or x2 was drawn (shuffle flag).
"Correct" = uniform insertion (Next(0, n+1)) and Fisher-Yates (Next(num+1)).
"""
import random
import sys

POOL = ["+0"] * 6 + ["+1"] * 5 + ["+2"] + ["-1"] * 5 + ["-2"] + ["NULL"] + ["x2"]
SHUFFLE_CARDS = {"NULL", "x2"}


def insert_all(current, cards, rng, buggy):
    for c in cards:
        n = len(current)
        current.insert(rng.randrange(n) if (buggy and n > 0) else rng.randrange(n + 1), c)


def shuffle(lst, rng, buggy):
    num = len(lst)
    while num > 1:
        num -= 1
        j = rng.randrange(num) if buggy else rng.randrange(num + 1)
        lst[j], lst[num] = lst[num], lst[j]


def reshuffle(current, discard, rng, buggy):
    insert_all(current, discard, rng, buggy)
    discard.clear()
    shuffle(current, rng, buggy)


def run(buggy, scenarios, rounds, draws_per_round, seed):
    rng = random.Random(seed)
    counts, draws = {}, 0
    first_cycle_null = first_cycle_draws = 0
    for _ in range(scenarios):
        current, discard = [], []
        insert_all(current, POOL, rng, buggy)          # starting deck (no shuffle after the build)
        first_cycle = True
        for _r in range(rounds):
            flag = False
            for _d in range(draws_per_round):
                if not current:
                    reshuffle(current, discard, rng, buggy)
                    first_cycle = False
                card = current.pop(0)
                discard.append(card)
                counts[card] = counts.get(card, 0) + 1
                draws += 1
                if first_cycle:
                    first_cycle_draws += 1
                    first_cycle_null += card == "NULL"
                flag |= card in SHUFFLE_CARDS
            if flag:
                reshuffle(current, discard, rng, buggy)
                first_cycle = False
    return counts, draws, first_cycle_null, first_cycle_draws


def report(label, res):
    counts, draws, fcn, fcd = res
    print(f"{label}: {draws} draws")
    for card in ["NULL", "x2", "+0", "+1", "-1", "+2", "-2"]:
        per = counts.get(card, 0) / draws
        print(f"   {card:>4}: {per*100:6.3f}% of draws  (1 in {1/per:5.2f})" if per else f"   {card}: 0")
    print(f"   null in the first deck cycle (before any reshuffle): {fcn/fcd*100:.3f}%  (pure 1-in-20 = 5.000%)")


if __name__ == "__main__":
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 200000
    for dpr in (1, 2):
        print(f"\n=== {dpr} draw(s) per round, 10 rounds per scenario, {n} scenarios ===")
        report("GAME CODE (buggy)", run(True, n, 10, dpr, 1))
        report("CORRECT shuffle  ", run(False, n, 10, dpr, 1))

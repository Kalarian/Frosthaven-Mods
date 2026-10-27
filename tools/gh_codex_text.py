"""The codex "mechanics" text of each ported class (CODEX_HeroInfo_<Class>, also the class info screen), written from
the class's cards (docs/mechanics/gh-card-halves.md: every half read, 2026-10-27, the user: "look at every single card
half that class has and use that to describe it... the actual mechanics the class's cards lean in to"). gh_port.py puts
this between Gloomhaven digital's role/difficulty line and its strengths/weaknesses lists, in place of Gloomhaven's
play paragraph (flavour that didn't describe the class). Two short paragraphs each: what the cards do, then what they
reward; numbers are the cards' own. Icons use the game's sprite names, as the card text does.
"""

MECHANICS = {
    "Brute": (
        "Nearly every top is a melee attack, and the big ones are big: <sprite name=Attack> 6 and 8 single hits, "
        "<sprite name=Attack> 6 on every adjacent enemy, and five sweeping area attacks (Sweeping Blow, Leaping Cleave, "
        "Brute Force, Skewer, Whirlwind). Six cards <sprite name=Push> 1-3 and two <sprite name=Pull> enemies into reach; "
        "stuns, immobilizes and disarms ride on the hits, and three cards kill a normal enemy outright (one needs it stunned). "
        "The bottoms are movement with teeth: a jumping move that attacks everything it passes, a charge whose attack equals "
        "the hexes moved in a straight line, and Balanced Measure, which turns hexes moved into attack and damage dealt "
        "into movement.\n\n"
        "Between the hits he holds the line with round-long <sprite name=Shield> 1-2 and <sprite name=Retaliate> 1-2 "
        "on many bottoms, a persistent prevent-3 (Juggernaut) and a toggle shield against attacks (Warding Strength). "
        "Three loot abilities, a couple of self-heals, almost no elements (one Earth, one Air; Skewer eats Air for "
        "pierce). Play him by moving into the middle of a group and hitting several of them at once."
    ),
    "Scoundrel": (
        "A melee duellist who reads positions. Her attacks grow when the target is beside one of her allies "
        "(Single Out, Flanking Strike, Backstab, Open Wound, Cull the Weak, Gruesome Advantage, Sinister Opportunity: "
        "+2 or +3 <sprite name=Attack>, or <sprite name=Wound>) or, the other way round, when the target stands alone: "
        "a persistent +2 against enemies with no allies next to them (Single Out), a round of it (Duelist's Advance), "
        "and a straight kill of an isolated normal enemy that is next to a friend of hers (Visage of the Inevitable). "
        "Stealth is the second engine: three cards make her <sprite name=Invisible>, and Smoke Bomb's attacks get stronger "
        "while she is unseen.\n\n"
        "<sprite name=Poison> runs through the deck (Venom Shiv, Special Mixture, Crippling Poison's +2 against "
        "poisoned targets, Visage poisons every adjacent enemy, Watch It Burn adds poison and wound to every attack, "
        "Cull the Weak adds 2 against any afflicted target). Her moves are long (5-7) and five cards loot, most of "
        "them every adjacent coin. A handful of thrown attacks (range 3-4, two to four targets) and two trap tricks "
        "(disarm, spring a trap on an enemy) round it out; defence is thin: one retaliate, one damage prevention, two "
        "self-heals."
    ),
    "Sunkeeper": (
        "Everything runs on <sprite name=CreateLight>: thirteen cards infuse it and twelve consume it, and what Light "
        "buys is the class: +1 <sprite name=Attack> with advantage on her big melee swings (Hammer Blow, Defensive "
        "Stance, Path of Glory), an extra card recovered for an ally, an extra heal target, a longer strengthen, and "
        "Beacon of Light keeps Light lit at the end of every turn. She commands allies as much as she fights: five cards "
        "let an ally <sprite name=Move> 4 or <sprite name=Attack> 4-8 on her turn (Tactical Order, Empowering Command, "
        "Mobilizing Axiom for all of them, Illuminate the Target, Inspiring Sanctity) and Unwavering Mandate hands an "
        "ally a whole extra turn.\n\n"
        "Protection comes as auras and stances: round-long <sprite name=Shield> and <sprite name=Retaliate>, a "
        "persistent retaliate 2 and a persistent shield 1 (at the cost of a move), damage prevention for everyone "
        "adjacent, attackers of her allies at disadvantage, and Divine Intervention takes her allies' damage onto "
        "herself. Eight heals (3-5; Lay on Hands heals an ally's whole missing health for 3 of hers; Engulfing Radiance "
        "heals the whole party), <sprite name=Bless> and <sprite name=Strengthen> for allies, three ways to give allies "
        "cards back from discard or lost piles, and stuns on four of her own attacks."
    ),
    "Sawbones": (
        "The healer with a saw. Fourteen heals, 3 to 8, several of them paired with curing every negative condition "
        "(Curative Mixture, Research the Cure, Bedside Manner; Master Physician makes every heal cure first). He hands "
        "allies cards: Medical Packs (<sprite name=Heal> 4 or 8, self) through six cards, and persistent "
        "<sprite name=Shield> 1 or immunity to every condition that sit in the ally's active area (Booster Shot, Research "
        "the Cure, Vaccine). Two round-long modes turn his melee attacks into heals, and Triage heals 2 once a turn on "
        "top of whatever else he does.\n\n"
        "His attacks are few but nasty: 3-7 with <sprite name=Wound>, <sprite name=Immobilize>, <sprite name=Poison>, "
        "<sprite name=Stun> or <sprite name=Disarm> on almost all of them, and three outright kills: Do No Harm (then "
        "he is disarmed and immobilized), Euthanize (a normal enemy with two or more conditions), Grisly Trauma (an "
        "area, enemies missing 10 or more health). Immunity auras for the party, two cards that recover discards for "
        "allies, and Teamwork, which moves allies and recovers one card per ally beside him."
    ),
    "Quartermaster": (
        "Items are his resource. Ten cards refresh spent or consumed items, his own or the party's (Restock for "
        "everyone in range 2, Side Pouch all of an ally's, Bag of Holding the party's small items, Reinforced Steel "
        "every round's end, Refreshment on each long rest, Continual Supply by discarding a card), and Proficiency "
        "makes every attack +1 in an action where an item was used. Three loots (2, 1 and 3 coins) keep the gold coming.\n\n"
        "The weapons are the attacks: axe and spear sweeps (Cleaving Axe, Impaling Spear), a bow at range 3-4, a "
        "ballista that pierces everything for a round, a boomerang, and one-shot scrolls with the biggest effects in "
        "the deck: <sprite name=Attack> 5 at range 3, 2 damage to every enemy within 4 (and 2 to himself), a 10-hex "
        "blizzard that immobilizes, a kill, and Catastrophic Bomb, 7 damage to every adjacent normal enemy and 7 to "
        "himself. For the party: +1 <sprite name=Attack> and <sprite name=Shield> 1-2 auras, +1 target on allies' "
        "ranged attacks (Quiver of Arrows), heal 2 to adjacent allies at every turn's end (Fortified Position), and an "
        "ally's whole discard pile back (Refreshment)."
    ),
    "Cragheart": (
        "Earth and stone. <sprite name=CreateEarth> is infused by eleven cards and spent by eight for +1 to +4 "
        "<sprite name=Attack>, +3 <sprite name=Move>, range, push or <sprite name=Immobilize>. Obstacles are his "
        "toolkit: he creates them (Avalanche, Rock Slide with 2 damage around each, Petrify turns a killed normal "
        "enemy into rock, Meteor drops a triple rock and attacks), destroys them (Rock Tunnel, Explosive Punch hits "
        "everything adjacent, Rocky End attacks for each obstacle destroyed, Clear the Way moves one and clears traps "
        "along his path) and pushes enemies into them for 2 extra damage (Brutal Momentum, Heaving Swing, Pulverize). "
        "Twelve of his attacks hit several targets, many of them 4-7 hex areas.\n\n"
        "He is careless with friends: six cards deal 1-2 damage to every adjacent figure, allies included (Rumbling "
        "Advance, Massive Boulder, Crater, Unstable Upheaval, Blind Destruction, Rock Slide). Jumping moves (five "
        "cards; Rock Tunnel immobilizes what it passes), a persistent <sprite name=Retaliate> 2 and a ranged "
        "retaliate 3, five heals including a start-of-turn heal 2 for himself or an ally, a bear trap, and a ranged "
        "option that Backup Ammunition (+1 target) and Nature's Lift (+2 range) make real."
    ),
    "Plagueherald": (
        "A ranged caster whose attacks spread sickness. Fourteen of twenty-four attacks are ranged (3 is the usual "
        "range) and six are area attacks, most of them 7-hex clouds and one of 14, that <sprite name=Poison>, <sprite name=Immobilize>, "
        "<sprite name=Stun> or <sprite name=Muddle> everything in them. <sprite name=Poison> is the engine: he "
        "poisons on attacks, at range 5 (three targets), every enemy at once (Airborne Toxin), every adjacent figure "
        "after each move (Epidemic), and then collects: Succumb to the Gift kills a poisoned normal enemy, Accelerated "
        "End and Mass Extinction damage every poisoned enemy, Willing Sacrifice and Virulent Strain deal 3, Convert "
        "the Flock turns poisoned allies into attack bonus, and Spread the Plague adds poison to every attack for a "
        "price.\n\n"
        "<sprite name=Curse> is the second line: five attacks curse, Baneful Hex deals 3 whenever a curse is drawn, "
        "Mass Extinction curses and wounds everyone. <sprite name=CreateAir> (nine infuses) feeds Foul Wind's "
        "persistent +1 <sprite name=Attack> and Storm of Wings' line attack; <sprite name=CreateDark> on four cards. "
        "Pierce 2 on two attacks, +2 targets from Grim Bargain, a persistent fly, small heals for the party, and a "
        "thin defence of shield 1s and attacker disadvantage."
    ),
    "Nightshroud": (
        "<sprite name=CreateDark> is the whole class: thirteen cards infuse it, eighteen consume it, and Nightfall "
        "relights it at the end of every turn. Spending Dark buys <sprite name=Invisible>, instant kills (Spirit of "
        "the Night a normal enemy, Swallowed by Fear a normal or elite, Angel of Death every adjacent normal enemy and "
        "one more on the bottom), +2 <sprite name=Move> and +2 <sprite name=Attack> at once (Smoke Step), a heal 4, a "
        "second attack, a 7-hex area, a second target, and a teleport to any connected room (Concealed Dominance).\n\n"
        "Invisibility is the other half: four cards make him <sprite name=Invisible> (more with Dark), and six attacks "
        "pay out while he is unseen: +2 <sprite name=Attack> (Silent Force, Grim Sustenance, Quiet Frenzy), "
        "<sprite name=Stun> (Soulfire), poison + muddle + wound (Lurking Ruin), a bonus <sprite name=Attack> 2 after "
        "each attack (Black Knives) and a persistent stun on attacks (Voice of the Night). All of it is melee (a few "
        "range-3/4 throws with <sprite name=Curse>, <sprite name=Muddle> or <sprite name=Poison>), on long moves and "
        "jumps (6 twice), with attackers at disadvantage as the defence. Two loots."
    ),
    "Berserker": (
        "Her health is her resource. Attacks scale with the health she has lost (Resolute Stand), with lost cards "
        "(Growing Rage, Final Fight's move), with being below half health (+2 <sprite name=Attack> on Defiance of "
        "Death, +2 <sprite name=Move> on Break the Chains, Seeing Red's persistent +1 and <sprite name=Shield> 1); "
        "Glass Hammer attacks for her current health and drops her to 1, Blood Pact hits 6 and costs half her health, "
        "and Fatal Fury kills a normal enemy whose health is under half of what she has lost. Eight cards let her pay "
        "1-2 damage to herself for +2 attack, +1 move, <sprite name=Stun>, pierce, extra targets (Reckless "
        "Offensive, Flurry of Axes, The Maw of Madness) or <sprite name=Shield>.\n\n"
        "What keeps her alive is built in: prevent 3 only when the hit would kill (Defiance of Death), prevent 5 "
        "persistent (Immortality), overheal to 26 (Unbridled Power), +2 on every heal she receives (Cauterize), "
        "<sprite name=Attack> 3 back whenever she is damaged (Vengeful Barrage), immunity to every condition "
        "(Careless Charge), an improved short rest, four retaliates, and heals that grow with her missing health "
        "(From the Brink). <sprite name=CreateFire> (five infuses) is spent for +2 attack, an attack 3, a retaliate 2. "
        "<sprite name=Wound> on four cards, an area attack with wound (Devil Horns), and The Maw of Madness heals her "
        "2 for every enemy it hits."
    ),
}

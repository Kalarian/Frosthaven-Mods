"""The codex "mechanics" text of each ported class (CODEX_HeroInfo_<Class>, also the class info screen). gh_port.py puts
this between Gloomhaven digital's role/difficulty line and its strengths/weaknesses lists, in place of Gloomhaven's
play paragraph.

The style is Frosthaven's own (the user, 2026-10-27, after the Nightshroud draft): short and plain, written to the
player, like the Frozen Fist entry: a card or two by name, a couple of recommendations, the elements and conditions the
class works with; no card counts or lists. A class with a rule of its own (songs, dooms, augments, a companion, a
transformation, supply cards) gets that rule explained plainly, as the Blinkblade's and the Boneshaper's entries do.
Facts come from the class's card halves (docs/mechanics/gh-card-halves.md). Icons use the game's sprite names.
"""

MECHANICS = {
    "Brute": (
        "You are at your best in the middle of a crowd. Many of your attacks hit every adjacent enemy or sweep through "
        "an area, so move in where several enemies stand together and hit them all at once.\n\n"
        "Your hits can <sprite name=Push>, <sprite name=Pull>, <sprite name=Stun>, <sprite name=Immobilize> and "
        "<sprite name=Disarm>, and a few cards kill a normal enemy outright. Your bottom actions are mostly moves that "
        "attack along the way, like <i>Balanced Measure</i>, which turns movement into damage.\n\n"
        "You have plenty of health, and round-long <sprite name=Shield> and <sprite name=Retaliate> to hold the line, "
        "so you can afford to take the first hit for your party."
    ),
    "Scoundrel": (
        "Position is everything for you. Many of your attacks grow stronger when the target is next to one of your "
        "allies, and others when the target stands alone with no allies of its own nearby. Look at where your friends "
        "are before you pick a target, and work with them to flank enemies.\n\n"
        "You can also become <sprite name=Invisible>, and some attacks, like <i>Smoke Bomb</i>, hit harder while you are "
        "unseen. Your attacks often add <sprite name=Poison> and <sprite name=Wound>, and poisoned enemies take extra "
        "damage from several of your cards.\n\n"
        "You move far and loot well, but you are fragile, with little to protect yourself. Strike and step away."
    ),
    "Sunkeeper": (
        "Light is your fuel. Most of your cards create <sprite name=CreateLight> or spend it for stronger attacks, "
        "extra healing and longer buffs. <i>Beacon of Light</i> keeps Light glowing every turn.\n\n"
        "You lead from the front: several cards let an ally <sprite name=Move> or <sprite name=Attack> on your turn, and "
        "your heals, <sprite name=Bless> and <sprite name=Strengthen> keep the party going. You also give allies "
        "<sprite name=Shield> and <sprite name=Retaliate>, and can take damage meant for them.\n\n"
        "You are tough and can <sprite name=Stun> with your own attacks, so stand next to your allies where you can "
        "protect and command them at the same time."
    ),
    "Sawbones": (
        "Some of your abilities give a <b>Medical Pack</b> card to yourself or an ally. It goes into that character's "
        "hand, and they can play it on their turn to heal themselves. Hand them out before the fight gets rough.\n\n"
        "You are the party's doctor: strong heals that often also remove every negative condition, and cards that keep "
        "allies <sprite name=Shield>ed or immune to conditions for a while. <i>Triage</i> lets you heal a little every "
        "turn on top of your other actions.\n\n"
        "Your few attacks are nasty: they can <sprite name=Wound>, <sprite name=Poison>, <sprite name=Immobilize>, "
        "<sprite name=Stun> or <sprite name=Disarm>, and a couple of them kill an enemy outright when it is already in "
        "bad shape."
    ),
    "Quartermaster": (
        "Items are your resource. Many of your cards refresh spent items, for you or for the whole party, so do not be "
        "shy about using them. <i>Proficiency</i> makes your attacks stronger in any action where you used an item.\n\n"
        "Your weapons are your cards: axe and spear sweeps up close, a bow at range, and one-use scrolls with the biggest "
        "effects in your deck. Save those scrolls for the moments that matter, since they are lost once used.\n\n"
        "You also support the party with <sprite name=Attack> and <sprite name=Shield> bonuses, healing, and cards that "
        "give allies their discarded cards back."
    ),
    "Cragheart": (
        "You shape the battlefield with rock. Many of your cards create obstacles, destroy them for extra effects, or "
        "push enemies into them for more damage. <i>Rock Slide</i> drops boulders that hurt everyone standing next to "
        "them.\n\n"
        "<sprite name=CreateEarth> powers your bigger attacks, extra movement and <sprite name=Immobilize>. Many of your "
        "attacks hit several enemies at once, often in large areas.\n\n"
        "Be careful: several of your abilities also damage allies standing next to you, so warn your party before you "
        "use them. You are tough, jump well, and have a little healing for yourself and your friends."
    ),
    "Plagueherald": (
        "Sickness is your weapon. You attack from range, often in large clouds, and most of your attacks "
        "<sprite name=Poison>, <sprite name=Immobilize>, <sprite name=Stun> or <sprite name=Muddle> everything they touch.\n\n"
        "Spread <sprite name=Poison> first, then cash it in: cards like <i>Succumb to the Gift</i> kill a poisoned "
        "enemy outright, and others deal damage to every poisoned enemy at once. <sprite name=Curse> is your second "
        "tool, and <sprite name=CreateAir> powers a few of your strongest effects.\n\n"
        "You are fragile and have little protection, so keep your distance and let the poison do the work."
    ),
    "Nightshroud": (
        # the user, 2026-10-27: simpler, like Frosthaven's own Frozen Fist entry (a card or two by name, a couple of
        # recommendations, the elements and conditions), not card counts and lists
        "Darkness is your fuel. Most of your cards create <sprite name=CreateDark> or spend it, and spending it is what "
        "turns a good turn into a great one. <i>Nightfall</i> keeps Dark flowing every turn, so it is worth getting out early.\n\n"
        "Your other tool is <sprite name=Invisible>. Many of your attacks hit harder or add effects while you are unseen, "
        "so plan your turns around becoming invisible, striking, and slipping away before enemies can answer.\n\n"
        "Your attacks can <sprite name=Muddle>, <sprite name=Poison>, <sprite name=Curse> and <sprite name=Stun> enemies, "
        "and a few Dark-fuelled ones, like <i>Spirit of the Night</i>, kill a normal enemy outright. You fight up close "
        "and are fairly fragile, so use your long moves and jumps to pick your fights, and avoid crowds while you can be seen."
    ),
    "Berserker": (
        "Your health is a resource, not just a lifeline. Many of your attacks get stronger the more health you have "
        "lost, or once you drop below half, and several cards let you suffer a little damage to make an attack hit "
        "harder.\n\n"
        "Your defences are built for living on the edge: <i>Defiance of Death</i> softens a blow that would knock you "
        "out, and other cards boost the healing you receive or strike back whenever you are hurt. Keep a healer close, "
        "or plan your own heals.\n\n"
        "<sprite name=CreateFire> powers some of your attacks, and your blows often add <sprite name=Wound>."
    ),
    "Hatchet": (
        "Your signature is <b>The Favorite</b>, your best axe. Some attacks throw it at an enemy, where it stays: your "
        "attacks against that enemy are stronger while it carries the axe, and a few cards let you take it back.\n\n"
        "You fight from range, throwing axes at 3 or 4 hexes. <sprite name=CreateAir> extends your range and adds "
        "pushes, extra movement or a second attack.\n\n"
        "Killing an enemy fuels your next moves: several cards give you extra movement, healing or "
        "<sprite name=Strengthen> after a kill. Your attacks can also <sprite name=Wound>, and you have good loot."
    ),
    "RedGuard": (
        "You hold the front line with shield and sickle. Your sickle attacks reach two hexes and often "
        "<sprite name=Pull> enemies to you or <sprite name=Immobilize> them, and many cards give you "
        "<sprite name=Shield> or hurt every enemy standing next to you.\n\n"
        "You use two elements. <sprite name=CreateFire> adds <sprite name=Wound>, larger areas and stronger pulls; "
        "<sprite name=CreateLight> adds attack, shields, <sprite name=Muddle> and extra movement. Keep both flowing.\n\n"
        "Pull enemies into your reach and keep them busy so your allies do not have to."
    ),
    "Voidwarden": (
        "You rarely attack yourself. Instead you take control of enemies, making them move or attack their own allies "
        "for an action, and you command your allies to <sprite name=Attack> or <sprite name=Move> on your turn.\n\n"
        "Your power has a price. Many of your heals and blessings also <sprite name=Poison> the ally you help, and some "
        "cards <sprite name=Curse> you unless you spend <sprite name=CreateDark>. Plan who you heal and when.\n\n"
        "You create <sprite name=CreateDark> and <sprite name=CreateIce>; Ice makes your heals stronger. Your own few "
        "attacks pile on several conditions at once."
    ),
    "Demolitionist": (
        "Walls and obstacles are your playground. Many of your cards destroy obstacles, and each destruction pays off "
        "with damage, <sprite name=Strengthen>, <sprite name=Bless> or <sprite name=Shield>. Enemies standing next to a "
        "wall also take extra damage from several of your attacks.\n\n"
        "At level 5 you gain the <b>Mech Suit</b>, a lasting transformation: you change shape, gain extra health and a "
        "shield, and recover cards.\n\n"
        "<sprite name=CreateFire> and <sprite name=CreateEarth> add damage, <sprite name=Muddle>, <sprite name=Poison> "
        "and pushes. Your attacks are mostly up close, and some can <sprite name=Stun>."
    ),
    "Spellweaver": (
        "Your strongest spells are lost once you cast them. <i>Reviving Ether</i> brings every lost card back into your "
        "hand, so you can cast them all again; time it well, since it is lost too.\n\n"
        "You use every element. Create them with your spells, then spend them for stronger attacks, "
        "<sprite name=Wound>, <sprite name=Stun>, extra healing, or invisibility.\n\n"
        "Most of your attacks are at range and many hit whole areas. You are fragile, so stay back and let your spells "
        "do the work."
    ),
    "Elementalist": (
        "Almost every one of your abilities changes with the element you spend. The same attack can push, stun, pierce "
        "or hit harder depending on whether you spend <sprite name=CreateFire>, <sprite name=CreateIce>, "
        "<sprite name=CreateAir>, <sprite name=CreateEarth> or <sprite name=CreateDark>.\n\n"
        "Plan your turns around which elements are strong, and make the ones you need. <i>Formless Power</i> lets you "
        "create an element of your choice at the end of each turn.\n\n"
        "Without elements your attacks are weak, so keep them flowing. You can also heal and shield your allies, and "
        "summon helpers."
    ),
    "Soothsinger": (
        "You play <b>songs</b>. A song is a lasting card that helps every ally while it plays: more attack or range, "
        "healing each turn, <sprite name=Shield>, or making every enemy attack at disadvantage. Only one song plays at a "
        "time; playing a new one ends the old, unless a card lets you sing two at once.\n\n"
        "Your other cards work at range on whole groups: you <sprite name=Stun>, <sprite name=Disarm>, "
        "<sprite name=Immobilize> and <sprite name=Curse> enemies, <sprite name=Bless> allies, and command allies to "
        "attack or move on your turn.\n\n"
        "Your own attacks are weak, so stay out of reach and let your party do the hitting."
    ),
    "Mindthief": (
        "You use <b>augments</b>: lasting cards that add an effect to every melee attack you make, such as extra damage, "
        "healing, <sprite name=Shield>, <sprite name=Poison> or <sprite name=Stun>. Only a limited number can be active at "
        "once, so choose the ones that suit the fight.\n\n"
        "Your other strength is control. Several cards make an enemy attack itself or its neighbour, or move where you "
        "want it, and a few let you command an ally.\n\n"
        "Your attacks hit harder against enemies with negative conditions. <sprite name=CreateIce> and "
        "<sprite name=CreateDark> add stuns and extra attacks. You move fast and can summon rats, but you are fragile."
    ),
    "Tinkerer": (
        "You are the party's support from range. Your heals reach any ally within a few hexes, and other cards give allies "
        "their discarded or lost cards back, or even raise an ally's hand size.\n\n"
        "Your attacks are ranged and often hit areas, adding <sprite name=Wound>, <sprite name=Immobilize> or "
        "<sprite name=Poison>. You can also lay traps and mines, and summon gadgets like a decoy or a battlebot.\n\n"
        "Stay behind your allies: you are fragile, but you keep everyone else in the fight much longer."
    ),
    "Doomstalker": (
        "You place <b>dooms</b> on enemies. A doom is a lasting mark that stays on its target: your attacks against it "
        "are stronger, and when the doomed enemy dies, the doom pays off with healing, damage around it, or another "
        "effect. You can normally keep one doom out at a time.\n\n"
        "You are an archer who fights from far away, and several attacks hit harder against a doomed target. <i>Expose</i> "
        "gives you advantage against it.\n\n"
        "You can summon animal companions and set bear traps. Your own attacks rarely add conditions: your dooms do that "
        "work."
    ),
    "BeastTyrant": (
        "You fight alongside your <b>bear</b>. It joins you at the start of every scenario, and many of your cards command "
        "it to <sprite name=Attack>, <sprite name=Move>, heal or roar. Keep it alive, since much of your power comes "
        "through it.\n\n"
        "You can summon more animals and spirits, and give your summons <sprite name=Shield>, <sprite name=Retaliate> "
        "and attack bonuses.\n\n"
        "Your own attacks are small and ranged, adding <sprite name=Poison>, <sprite name=Disarm> or <sprite name=Stun>. "
        "You can heal your allies. <sprite name=CreateEarth> and <sprite name=CreateAir> boost a few cards."
    ),
    "Summoner": (
        "You fight through your <b>summons</b>. Most of your cards call a creature to fight beside you, and the rest "
        "protect them, heal them, or let them act on your turn. Summons stay until they are defeated; some cards let you "
        "bring lost summon cards back.\n\n"
        "Your own attacks are modest and mostly at range, adding <sprite name=Curse>, <sprite name=Immobilize>, "
        "<sprite name=Stun> or <sprite name=Wound> when you spend <sprite name=CreateDark>, <sprite name=CreateFire> or "
        "<sprite name=CreateAir>.\n\n"
        "Spread your summons around the enemies and let them take the hits while you stay back."
    ),
}

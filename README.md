# Frosthaven Mods

A fan-made mod pack for the digital edition of **Frosthaven** on Steam. It adds optional house rules,
quality-of-life touches, fixes for bugs in the game, and classes from **Gloomhaven** that you can play in your
Frosthaven campaign.

Every mod is a switch. Nothing changes until you turn it on, and your saves are never touched.

**[Download the latest version](https://github.com/Kalarian/Frosthaven-Mods/releases/latest)** ·
**[Mod guide (PDF)](docs/Frosthaven-Mods-Guide.pdf)**

---

## What's in it

### House rules and quality of life

| Mod | What it does | Changes the game? |
|---|---|---|
| Tradeable resources and equipment | Give gold, materials and items to another character in the outpost | Yes |
| Share duplicate loot | A second copy of an item you loot goes to a party member instead of being sold | Yes |
| Easier item reward window | Handing out item rewards takes fewer clicks | No |
| Reveal event outcomes | See what each event choice will do before you pick it | Information only |
| Show personal quest rewards | Quest cards show which building each quest unlocks when you retire | No |
| Reduced randomness | No nulls or x2s in modifier decks: a -2 and a +2 instead | Yes |
| Masteries carry over | A mastery earned once counts for every later character of that class | Yes |
| Dwarf's balance changes | Fixes for some of the campaign's rough spots, from Dwarf74's well-known campaign tweaks | Yes |
| More story events | Town events that unlock something come up more often | Yes |
| Game bug fixes | Fixes bugs in the game itself, including fairer deck shuffles and several co-op desyncs | Bug fixes |
| Milestone saves | Automatic backup saves at the start and end of every scenario | No |
| Battle stats | Many more stats after each scenario, plus campaign totals | No |
| Modifier deck odds | Shows the odds of your next attack modifier draw | No |
| Scenario map file | Saves your campaign progress for the companion app's scenario map | No |

### Gloomhaven classes

Play Gloomhaven's classes in Frosthaven, each with its own cards, 3D model, animations, effects, sounds and portraits.
Perks and masteries come from Cephalofair's official Gloomhaven-to-Frosthaven conversion sheets.

- **All 21:** the 17 Gloomhaven classes (Brute, Scoundrel, Spellweaver, Tinkerer, Cragheart, Mindthief, Sunkeeper,
  Quartermaster, Summoner, Nightshroud, Plagueherald, Berserker, Soothsinger, Doomstalker, Sawbones, Elementalist and
  Beast Tyrant) and the four from Jaws of the Lion (Demolitionist, Hatchet, Red Guard and Voidwarden).

The four Jaws of the Lion classes are open from the start. The others arrive gradually by default: each retirement in
your campaign lets the party pick one of two new Gloomhaven classes. If you'd rather have them all at once, there's a
switch for that.

You don't need to own Gloomhaven; the companion brings everything.

### Companion app (optional)

A small Windows app that runs next to the game:
- install, update and remove the mods;
- see which mods your campaign has on;
- a live scenario map with every scenario's rewards;
- detailed battle stats for your party.

It's on the release page as `Frosthaven-Companion-<version>.zip`. Unzip it anywhere and run it; there's nothing to
install. The mods work fine without it.

---

## Installing

1. Download **`Frosthaven-Companion-<version>.zip`** from the
   [latest release](https://github.com/Kalarian/Frosthaven-Mods/releases/latest) and unzip it anywhere.
2. Close Frosthaven.
3. Run **Frosthaven Companion.exe** and click **Install** in its Mods tab.

That's it. The companion finds the game through Steam. When a new version is out, its Mods tab says so: click
**Update the app**, then **Update**. Windows may warn about an unknown publisher the first time; choose **More info >
Run anyway**. (The standalone installer of earlier releases is gone: the companion does everything it did.)

## Turning mods on

Load a campaign and open **Options > Custom Rules**. Every mod has its own switch, and all of them start off.

- The host decides. The switches are saved with the campaign and sent to everyone who joins.
- You can change them at any time in the outpost.
- Mods that change how scenarios play can't be switched during an online scenario; change them in the outpost.

The [mod guide](docs/Frosthaven-Mods-Guide.pdf) explains each mod in a few lines.

## Playing together

**Everyone in a co-op session needs the same version of the mods.** The game doesn't check this when you join. A
mismatch shows up later as an "out of sync" error mid-scenario. When in doubt, everyone opens the companion before you play:
its Mods tab shows the installed version and offers any update.

## Removing the mods

In the companion's Mods tab, click **Remove the mods**. It puts the game's original files back. Saves are never touched.

One exception: a campaign that has used the **Gloomhaven classes** needs the pack to load. To keep such a campaign
playable without the mods, use **Copy without content packs** from the game's Save or Load window first. It makes a
copy of the campaign that loads in the unmodded game.

After a Steam update to Frosthaven, open the companion and click **Reinstall**.

## If something goes wrong

- **"Out of sync" error in co-op:** before closing the game, everyone presses **Ctrl + F7**. It saves a zip file to
  your desktop. Send the zips along with a note on what you were doing.
- **Something went badly in a scenario:** the host can load one of the "Mod backup" saves made by Milestone saves.
- **Anything else:** open an issue on this page or ask in the Discord.

---

Frosthaven Mods is a fan project. It isn't made or supported by Snapshot Games or Cephalofair Games. Frosthaven,
Gloomhaven and their content belong to their owners. The Gloomhaven classes are shared with the community's OK from
the official Frosthaven and Gloomhaven Discords. If the classes are ever officially brought to Frosthaven, the class
pack will step aside. Dwarf's balance changes are based on Dwarf74's Unofficial Frosthaven Campaign Tweaks.

The tools and patches are MIT-licensed. Developer notes, including how the mods are built: [docs/DEVELOPING.md](docs/DEVELOPING.md).

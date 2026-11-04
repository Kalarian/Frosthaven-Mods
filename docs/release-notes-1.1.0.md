# Frosthaven Mods: next release (draft)

Everyone in a co-op game must update. **Finish your current scenario before you update** (see Known issues).

## New

- **Stacking attack bonuses** (house rule, **on by default**): every +X Attack bonus on an attack adds up, the
  physical game's official rule. The digital game kept only the largest of the bonuses written for "your default
  attack". The host can switch it off to get the digital rule back.
- The mod guide's switch list now shows every switch, the two Gloomhaven packs included, and whether it starts on
  or off.

## Fixed (Gloomhaven classes, 1st edition)

- Elementalist, *Infernal Vortex* (top): hits only enemies at exactly range 2. It hit adjacent enemies too and could
  stop the game with an error screen.
- Voidwarden, *Withering Conviction*, *Freeze the Soul* and *Commanding Presence* (bottom): spending Dark instead of
  cursing yourself no longer freezes the turn. Switch Dark on, then press Confirm Action; skip it and nothing is spent.
- "Consume an element: do this" on its own line (59 halves in both packs) is now Frosthaven's own Consume step. Before,
  some of these never offered the element (*Boiling Arc* bottom froze the turn) or crashed the scenario.
  *Ethereal Manifestation* uses Frosthaven's element picker, once for each element on the board.
- Beast Tyrant, *Forceful Swipe* and *Unstoppable Beast* (top): enhancing their area at the Enhancer no longer takes
  all your gold.
- Beast Tyrant, *Jaws of Death* (top): the bear's kills now count for the repeats and the XP.
- Tinkerer, *Harsh Stimulants* (bottom): the damage to adjacent allies now happens.
- Red Guard, *Barbaric Instincts*, *Flying Sickle* and *Honorable Fury*: no more stray ", ," on the card text.
- A summon's long special text stays inside its box instead of running over the "Summon ..." title.
- A rare "Collection was modified" crash during attacks with Gloomhaven classes in the party.

## Gloomhaven classes (2nd edition) [WIP]: new pack

- All **17 classes** of Gloomhaven's second edition, **503 cards**, with their perks, masteries and class mats:
  Bruiser, Cragheart, Mindthief, Silent Knife, Spellweaver, Tinkerer, Berserker, Doomstalker, Elementalist,
  Nightshroud, Plagueherald, Quartermaster, Sawbones, Sunkeeper, Soothsinger, Soultether and Wildfury.
- Its own switch, **Gloomhaven classes (2nd edition) [WIP]**. A campaign uses one edition: it can't be on together
  with the 1st edition pack. Six classes open at once, the others arrive in envelopes, one per retirement.
- The class rules play as printed:
  - **Money tokens** (Silent Knife) are loot tokens, as the official FAQ says.
  - **Safeguard** is a real condition: it stops the next negative condition.
  - **Plague** (Plagueherald): up to 3 tokens per enemy, 12 for the class.
  - **Tears** and the **Nightshroud's class mat** (Invisible at the start, can't attack while Invisible).
  - **Quartermaster**: supplies, the nine class items as a shared supply.
  - **Tinkerer** devices are tokens on the map; the Teleportation Pads link two hexes.
  - **Songs and Notes** (Soothsinger).
  - **Controlled enemies**: when a card makes an enemy move or attack for you, you choose its path and targets.
  - The Wildfury's Bear, the Sawbones' Medical Packs and Prescriptions.
- Every card has been checked against the printed card and played by the test bot. The pack stays [WIP] until it has
  been played in co-op.

## Known issues

- **Safest to finish your current scenario before you update.** 59 Gloomhaven cards' consume steps changed shape, and a
  save made in the middle of a scenario that has one of those bonuses in play may not load cleanly. Saves made in the
  outpost are fine.
- The 2nd edition pack uses **stand-in models for 17 summons** (borrowed from the 1st edition: the Doomstalker's
  beasts, the Soultether's spirits, the Tinkerer's devices and others).
- Not tried yet: co-op with the 2nd edition pack; the 2nd edition masteries counted at the end of a scenario on a
  joined player's game; the Spellweaver's *Etheric Bond* perk (short rest) in co-op. Tell Kalarian how it goes.

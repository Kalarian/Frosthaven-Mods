"""Frosthaven's official crossover sheets for Gloomhaven classes: the perks and masteries a Gloomhaven class gets when
played in Frosthaven (Cephalofair's "Gloomhaven classes in Frosthaven" character sheets), as Frosthaven rule files.
Gloomhaven's own perks are not used: Frosthaven's sheets reworked them (fewer "add" perks, more "replace", a long-rest
perk) and added two masteries per class, which Gloomhaven never had. Used by gh_port.py: files(cls) -> [(path, text)],
strings(cls) -> {key: English}.

Perks are plain Frosthaven perk files (attack-modifier cards swapped, Flags for the passive ones; see Global/Perks in the
game's ruleset). The text uses the game's own sprite markup, copied from its perk descriptions.

Masteries are battle-goal conditions over the game's per-round statistics (Campaign/Masteries in the game's ruleset).
The Brute's first (12 retaliate damage in one round) is pure data: the DealDamage filter with AbilityTypes [Retaliate]
summed per round. The second (three consecutive rounds of six different cards and 6+ damage) has no built-in filter
for "in a row", so the plugin answers that condition itself (GHMasteries.cs): the file tags the target with a marker in
EnemyFamilies, a free-text list the game never validates, and the plugin's prefix computes the streak from the damage
and ability records. Without the plugin the condition reads as "play three rounds".
"""

BRUTE_PERKS = [
    # (available, name, description, {perk fields})  -- Perk_Brute_01 .. _10, 15 checks in all, as on the sheet
    (2, "Precision", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (2, "Shove", 'Replace one <sprite name="-1"> with three <sprite name="0"> <sprite name=Push> 1 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Push1_R, AMC_Push1_R, AMC_Push1_R]"}),
    (1, "Skewering Blows", 'Replace two <sprite name="0"> with two <sprite name="0"> <sprite name=Pierce> 3 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Pierce3_R, AMC_Pierce3_R]"}),
    (2, "Heavy Hands", 'Replace one <sprite name="0"> with two <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]"}),
    (2, "Wide Swing", 'Replace one <sprite name="0"> with one <sprite name="0"> "+1 <sprite name=Target>" <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_AddTarget_R]"}),
    (2, "Stunning Blow", 'Add one <sprite name="0"> <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (1, "Dirty Tricks", 'Add one <sprite name="0"> <sprite name=Disarm> <sprite name=Rolling> and one <sprite name="0"> <sprite name=Muddle> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Disarm_R, AMC_Muddle_R]"}),
    (1, "Overpower", 'Add one <sprite name="+3">',
     {"CardsToAdd": "[AMC_P3]"}),
    (1, "Hardened", 'Ignore item <sprite name="-1"> effects and add one <sprite name="+1"> <sprite name=Shield> 1',
     {"CardsToAdd": "[AMC_Shield1_P1]", "Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    (1, "Rested and Ready", 'Whenever you long rest, add +1 <sprite name=Move> to your first move ability the following round',
     {"Flags": "[Passive, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    TRested:
        Move: 1
        MiscVars:
            PrintedProperties: list("Move")
        Filter: Self
        SkipAnim: True
        ActiveBonus:
            Duration: Persistent
            Behaviour: BuffMove
            XPTracker: [0]
            ListLayoutOverride: [$PERK_Brute_10_Bonus$, Positive_MovePlus]"""}),
]

BRUTE_MASTERIES = [
    ("Living Wall",
     "Cause enemies to suffer a total of at least <sprite name=Damage> 12 from retaliate during attacks targeting you in a single round",
     """\
# the retaliate damage the Brute dealt, summed per round (DealDamage with AbilityTypes keeps only retaliate records)
BattleGoalCondition:
    - Targets:
        Filter: DealDamage
        SubFilter: RoundCharactersOnly
        AbilityTypes: [Retaliate]
        CharacterIDs:
            ActorID: BruteID
        Value: 12"""),
    ("Relentless Onslaught",
     "Across three consecutive rounds, play six different ability cards and cause enemies to suffer at least <sprite name=Damage> 6 on each of your turns",
     """\
# Three rounds in a row with at least 6 damage to enemies on each of the Brute's turns and six different ability cards
# played over the three. No built-in filter checks rounds in a row: the Frosthaven Mods plugin answers this target
# (the marker in EnemyFamilies, GHMasteries.cs) from the damage and ability records. Value = rounds in a row,
# Amount = damage per turn, Targets = different cards. Without the plugin it reads as "play three rounds".
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_DamageStreak]
        CharacterIDs:
            ActorID: BruteID
        Value: 3
        Amount: 6
        Targets: 6"""),
]

# ---------------------------------------------------------------- group 1 (crossover sheets read 2026-10-25)
# Texts in the digital game's own perk wording ("Replace one <sprite name="-1"> with one <sprite name="+1">", "Once
# each quest"), with its sprite names (Poison, Muddle, Pierce, Invisible, CreateLight, Shield, Stun, Heal, Wound,
# Immobilize, Refresh, Target, Ward, RecoverIcon, Rolling). A perk drawn with a tall single box costs two checks.


def _once_per_quest(desc_key, icon, inner):
    """A "once each quest, during your turn" perk, Frosthaven's own pattern (Perk_BannerSpear_10, the Deepwraith's Skull
    Collection): a persistent toggle bonus added at scenario start; its AddActiveBonus content is the ability performed
    when the player toggles it during their turn; XPTracker [0] gives it a single use. The toggle's button in the
    "activate" bar takes its picture from the perk icon list by perk ID, which has no entry for a ported perk (it drew
    a white box), so the bonus names an active-ability icon itself (MiscVars OverrideIcon, as Skull Collection does)."""
    return f"""\
    TActive:
        AutoTrigger: True
        AddActiveBonus:
{inner}
            Filter: [Self]
        ActiveBonus:
            Duration: Persistent
            Behaviour: DuringTurnAbility
            XPTracker: [0]
            IsToggleBonus: True
            Hidden: True
            ListLayoutOverride: [${desc_key}$, {icon}]
            MiscVars:
                OverrideIcon: "'{icon}'"
        AnimOverload: PowerUp"""


SCOUNDREL_PERKS = [
    (1, "Light Step", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (1, "Steady Hand", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (2, "Venom Coating", 'Replace one <sprite name="-1"> with two <sprite name="0"> <sprite name=Poison> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Poison_R, AMC_Poison_R]"}),
    (1, "Composure", 'Replace one <sprite name="-2"> with one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_Basic_P0]"}),
    (2, "Vital Strike", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (2, "Dirty Fighting", 'Replace two <sprite name="0"> with one <sprite name="0"> <sprite name=Muddle> <sprite name=Rolling> and one <sprite name="0"> <sprite name=Pierce> 3 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Muddle_R, AMC_Pierce3_R]"}),
    (2, "Flurry", 'Add two <sprite name="+1"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P1_R, AMC_P1_R]"}),
    (1, "Vanish", 'Add one <sprite name="0"> <sprite name=Invisible>, self <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Invisible_R]"}),
    (1, "Unfazed", "Ignore negative scenario effects",
     {"Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    (1, "Cloak of Invisibility", 'Once each quest, during your turn, perform <sprite name=Invisible>, self',
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _once_per_quest("PERK_Scoundrel_10_Desc", "Invisible", "            Invisible: True")}),
]

SCOUNDREL_MASTERIES = [
    ("Pack Hunter", "Kill at least six enemies that are adjacent to at least one of your allies",
     """\
# Kills of enemies that had another character next to them when they fell. No built-in filter looks at who stood next
# to the victim: the Frosthaven Mods plugin answers this target (the marker in EnemyFamilies, GHMasteries.cs) from the
# kill records, which carry the number of players adjacent to the dying enemy. Value = kills needed.
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_KillsNearAllies]
        CharacterIDs:
            ActorID: ScoundrelID
        Value: 6"""),
    ("Lone Wolf", "Kill at least six enemies that are adjacent to none of your allies",
     """\
# The opposite count, from the same records (GHMasteries.cs). Value = kills needed.
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_KillsAwayFromAllies]
        CharacterIDs:
            ActorID: ScoundrelID
        Value: 6"""),
]

SUNKEEPER_PERKS = [
    (1, "Focus", 'Remove four <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0, AMC_Basic_P0, AMC_Basic_P0]"}),
    (2, "Radiance", 'Replace one <sprite name="-1"> with two <sprite name="0"> <sprite name=CreateLight> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Light_R, AMC_Light_R]"}),
    (2, "Bulwark", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=Shield> 1 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Shield1_R]"}),
    (1, "Smite", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (2, "Zeal", 'Add two <sprite name="+1"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P1_R, AMC_P1_R]"}),
    (1, "Dazzle", 'Add one <sprite name="0"> <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (2, "Mending Light", 'Add two <sprite name="0"> <sprite name=Heal> 1, self <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Heal1_R, AMC_Heal1_R]"}),
    (1, "Well Armored", 'Ignore item <sprite name="-1"> effects and add two <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]", "Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    (1, "Unfazed", "Ignore negative scenario effects",
     {"Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # answered by the plugin (GHPerks.cs: the perk ID Sunkeeper10 on the healer)
    (1, "Shielding Light", "Whenever one of your heals would raise an ally's hit points above their maximum, that ally gains <sprite name=Ward>",
     {"Cost": 2, "Flags": "[Passive]"}),
]

SUNKEEPER_MASTERIES = [
    ("Unbreakable", "Reduce attacks targeting you by a total of <sprite name=Shield> 20 or more in a single round",
     """\
# The game's own filter: damage received that shields stopped, summed over a round (Amount = the total).
BattleGoalCondition:
    - Targets:
        Filter: ShieldDamage
        SubFilter: RoundCharactersOnly
        CharacterIDs:
            ActorID: SunkeeperID
        Amount: 20
        Value: 1"""),
    ("Child of the Sun", "Infuse or consume <sprite name=CreateLight> during each of your turns",
     """\
# Light infused or consumed on every turn the Sunkeeper takes. The game's filters count infusions or consumptions, not
# either: the Frosthaven Mods plugin answers this target (GHMasteries.cs) from the element records, for the element(s)
# in Elements, over the rounds with a turn of hers.
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_ElementEveryTurn]
        Elements: Light
        CharacterIDs:
            ActorID: SunkeeperID
        Value: 1"""),
]

SAWBONES_PERKS = [
    (1, "Steady Hands", 'Remove one <sprite name="-2">',
     {"CardsToRemove": "[AMC_Basic_M2]"}),
    (3, "Open Wounds", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=Wound> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Wound_R]"}),
    (2, "Precise Cuts", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (2, "Hamstring", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Immobilize>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Immobilize_P1]"}),
    (1, "Resupply", 'Add one <sprite name="0"> <sprite name=Refresh> one item',
     {"CardsToAdd": "[AMC_RefreshItem]"}),
    (2, "Surgical Strike", 'Add one <sprite name="+2"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P2_R]"}),
    (1, "Anesthetic", 'Add one <sprite name="0"> <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (2, "Self-Care", 'Add one <sprite name="0"> <sprite name=Heal> 3, self <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Heal3_R]"}),
    # answered by the plugin (GHPerks.cs: the perk ID Sawbones09 on a Sawbones in play)
    (1, "Revitalizing Medicine", "Whenever an ally performs the <sprite name=Heal> of Medical Pack or Large Medical Pack, that character first loses one negative condition",
     {"Flags": "[Passive]"}),
]

SAWBONES_MASTERIES = [
    ("Bedside Manner", "On each of your turns, give an ally a card, heal an ally, grant an ally <sprite name=Shield>, or place a card in an ally's active area",
     """\
# An ally helped on every turn the Sawbones takes: a heal, shield, supply card, buff or recovery ability of hers with
# another character or summon among its targets. No built-in filter combines them: the Frosthaven Mods plugin answers
# this target (GHMasteries.cs) from the ability records.
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AllySupportEveryTurn]
        CharacterIDs:
            ActorID: SawbonesID
        Value: 1"""),
    ("Grievous Harm", "Deal at least <sprite name=Damage> 20 with a single attack ability",
     """\
# The damage one attack ability of hers caused to enemies, all its targets together. The plugin answers this target
# (GHMasteries.cs) from the damage records, grouped by round, card and ability. Amount = the damage.
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_SingleAbilityDamage]
        CharacterIDs:
            ActorID: SawbonesID
        Amount: 20
        Value: 1"""),
]

QUARTERMASTER_PERKS = [
    (2, "Steady Supply", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (1, "Armor Piercing", 'Replace two <sprite name="0"> with two <sprite name="0"> <sprite name=Pierce> 3 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Pierce3_R, AMC_Pierce3_R]"}),
    (1, "Sweeping Blow", 'Replace one <sprite name="0"> with one <sprite name="0"> +1 <sprite name=Target>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_AddTarget]"}),
    (2, "Heavy Hands", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (3, "Resupply", 'Add one <sprite name="0"> <sprite name=Refresh> one item',
     {"CardsToAdd": "[AMC_RefreshItem]"}),
    (2, "Drilled", 'Add two <sprite name="+1"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P1_R, AMC_P1_R]"}),
    (1, "Concussion", 'Add one <sprite name="0"> <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (1, "Well Fitted", 'Ignore item <sprite name="-1"> effects and add two <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]", "Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    # the sheet also asks for a persistent Quartermaster card in the active area; not enforced here (noted in the guide)
    (1, "Well Supplied", 'Once each quest, during your turn, <sprite name=RecoverIcon> up to four cards from your discard pile',
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _once_per_quest("PERK_Quartermaster_09_Desc", "Recover", "            RecoverDiscardedCards: 4")}),
]

QUARTERMASTER_MASTERIES = [
    ("Supply Lines", "Spend, lose or refresh one or more items on each of your turns, without ever performing the top action of Reinforced Steel",
     """\
# An item used or refreshed on every turn the Quartermaster takes (the plugin answers this target from the item and
# ability records, GHMasteries.cs); failed the moment Reinforced Steel's top action (card 20223, abilities named T...)
# is performed (the plugin's second marker; Amount = the card).
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_ItemsEveryTurn]
        CharacterIDs:
            ActorID: QuartermasterID
        Value: 1
BattleGoalFailCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_UsedCardTop]
        CharacterIDs:
            ActorID: QuartermasterID
        Amount: 20223
        Value: 1"""),
    ("Pack Mule", "Loot six or more loot tokens in a single turn",
     """\
# The game's own filter: loot records in one round.
BattleGoalCondition:
    - Targets:
        Filter: Loot
        SubFilter: RoundCharactersOnly
        CharacterIDs:
            ActorID: QuartermasterID
        Value: 6"""),
]

CROSSOVER = {
    "Brute": {"perks": BRUTE_PERKS, "masteries": BRUTE_MASTERIES,
              "strings": {"PERK_Brute_10_Bonus": "Rested and Ready: +1 <sprite name=Move> to your next move ability"}},
    "Scoundrel": {"perks": SCOUNDREL_PERKS, "masteries": SCOUNDREL_MASTERIES},
    "Sunkeeper": {"perks": SUNKEEPER_PERKS, "masteries": SUNKEEPER_MASTERIES},
    "Sawbones": {"perks": SAWBONES_PERKS, "masteries": SAWBONES_MASTERIES},
    "Quartermaster": {"perks": QUARTERMASTER_PERKS, "masteries": QUARTERMASTER_MASTERIES},
}


def mastery_ids(cls):
    """The MasteryIDs list for the class's character file, or None when it has no crossover sheet."""
    sheet = CROSSOVER.get(cls)
    return [f"{cls}_MASTERY_{i + 1}" for i in range(len(sheet["masteries"]))] if sheet else None


def files(cls):
    """[(ruleset path, text)] for the class's crossover perks and masteries; empty without a sheet."""
    sheet = CROSSOVER.get(cls)
    if not sheet:
        return []
    out = []
    for i, (available, _name, _desc, fields) in enumerate(sheet["perks"], start=1):
        lines = ["Parser: Perk", f"ID: {cls}{i:02d}", f"Name: PERK_{cls}_{i:02d}_Name", f"Description: PERK_{cls}_{i:02d}_Desc",
                 f"CharacterID: {cls}ID", f"Available: {available}"]
        for key in ("Cost", "CardsToRemove", "CardsToAdd", "Flags"):
            if key in fields:
                lines.append(f"{key}: {fields[key]}")
        if "Abilities" in fields:
            lines.append("Abilities:")
            lines.append(fields["Abilities"])
        out.append((f"Global/Perks/{cls}/Perk_{cls}_{i:02d}.yml", "\n".join(lines) + "\n"))
    for i, (_name, _desc, body) in enumerate(sheet["masteries"], start=1):
        head = ["Parser: Mastery", f"ID: {cls}_MASTERY_{i}", f"LocalisedName: {cls}_MASTERY_{i}_Name",
                f"LocalisedDescription: {cls}_MASTERY_{i}_Desc", "PerkPoints: 1"]
        out.append((f"Campaign/Masteries/{cls}_Mastery_{i}.yml", "\n".join(head) + "\n" + body + "\n"))
    return out


def strings(cls):
    """{text key: English} for the class's crossover perks and masteries."""
    sheet = CROSSOVER.get(cls)
    if not sheet:
        return {}
    out = dict(sheet.get("strings", {}))
    for i, (_available, name, desc, _fields) in enumerate(sheet["perks"], start=1):
        out[f"PERK_{cls}_{i:02d}_Name"] = name
        out[f"PERK_{cls}_{i:02d}_Desc"] = desc
    for i, (name, desc, _body) in enumerate(sheet["masteries"], start=1):
        out[f"{cls}_MASTERY_{i}_Name"] = name
        out[f"{cls}_MASTERY_{i}_Desc"] = desc
    return out

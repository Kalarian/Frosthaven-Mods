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

CROSSOVER = {
    "Brute": {"perks": BRUTE_PERKS, "masteries": BRUTE_MASTERIES,
              "strings": {"PERK_Brute_10_Bonus": "Rested and Ready: +1 <sprite name=Move> to your next move ability"}},
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
        for key in ("CardsToRemove", "CardsToAdd", "Flags"):
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

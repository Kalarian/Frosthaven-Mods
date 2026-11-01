# Berserker (2nd edition) perks, from the perk sheet in tools/gh2e/classes.json ("Inox Berserker", 18 boxes).
# gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# Linked boxes (one perk bought with several checks) are Available 1 with Cost N, as gh_crossover writes them.

PERKS = [
    (1, "Iron Will", 'Remove one <sprite name="-2">',
     {"CardsToRemove": "[AMC_Basic_M2]"}),
    (3, "Second Wind", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=Heal> 1',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_GH2E_Berserker2_Heal1Self_P0]"}),
    # 2e: "+1, you may suffer 2 damage to add +2 Attack". A modifier can't offer that choice (its abilities run after
    # the attack's damage): a plain +1 for now (NOTES.md).
    (2, "Reckless Blows", 'Replace two <sprite name="0"> with one <sprite name="+1"> "You may suffer <sprite name=Damage> 2 to add +2 <sprite name=Attack>"',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (1, "Open Wounds", 'Replace one <sprite name="0"> with two <sprite name=Wound> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_R, AMC_Wound_R]"}),
    (2, "Burning Rage", 'Add one <sprite name="+2"> <sprite name=CreateFire>',
     {"CardsToAdd": "[AMC_Fire_P2]"}),
    (1, "Concussion", 'Add one <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (1, "Blood Frenzy", 'Add one <sprite name="2x"> "Suffer <sprite name=Damage> equal to half your current hit point value (rounded down)"',
     {"CardsToAdd": "[AMC_GH2E_Berserker2_SelfHalfHP_T2]"}),
    (1, "Thick Skin", 'Ignore item <sprite name="-1"> effects and add one <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1]", "Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    # Frosthaven's own after-long-rest perks (Perk_BannerSpear_08) run their abilities the same way (1e Rapid Recovery)
    (1, "Rapid Recovery", "Whenever you heal from a long rest, add +1 <sprite name=Heal>",
     {"Flags": "[Passive, ExecuteAbilitiesAfterLongRest]",
      "Abilities": """\
    AHeal:
        Heal: 1
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        CanUndo: False"""}),
    # Frosthaven's own "lethal damage sets you to 1 instead" (Perk_Boneshaper_06: PreventDamageSetTargetHealthTo, read by
    # the PreventAndDamageActiveBonusCaster behaviour), one use (XPTracker [0]), attacks only. The "Attack 2 on the
    # attacker" part has no Frosthaven shape yet (NOTES.md: NEEDS PLUGIN).
    (1, "Defy Death", "Once each scenario, when <sprite name=Damage> from an attack would reduce your current hit point value to less than 1, instead set your current hit point value to 1 and perform: <sprite name=Attack> 2, target the attacker",
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": """\
    TDefyDeath:
        AutoTrigger: True
        SkipAnim: True
        PreventDamage: 1
        PreventOnlyIfLethal: True
        PreventDamageSetTargetHealthTo: 1
        AttackSourcesOnly: True
        Filter: [Self]
        ActiveBonus:
            Duration: Persistent
            Behaviour: PreventAndDamageActiveBonusCaster
            XPTracker: [0]
            Hidden: True
            ListLayoutOverride: [$PERK_Berserker2_10_Desc$, Positive_DamagePrevent]"""}),
    # NEEDS PLUGIN: an end-of-turn control of every adjacent enemy, each attacking the Berserker (to trigger retaliate).
    # No ability yet: the perk can be bought and does nothing until the plugin does it (NOTES.md).
    (1, "Taunting Fury", "At the end of each of your turns, you may control all adjacent enemies: <sprite name=Attack> +0, target the Berserker",
     {"Cost": 3, "Flags": "[Passive]"}),
]

MASTERIES = []   # "Kill 20 enemies with your retaliate" / "end all your turns at half hit points or lower": no pattern yet

NEW_CARDS = {
    # +0, Heal 1 self (Frosthaven has the +1 one, AMC_Heal1_Self_P1, and rolling ones only)
    "AMC_GH2E_Berserker2_Heal1Self_P0": 'MathModifier: "+0"\nAbilities:\n    AHeal:\n        Heal: 1\n        Filter: Self\n        AutoTrigger: True\n',
    # 2x (no shuffle: a class card), then suffer damage equal to half the current hit points, rounded down
    "AMC_GH2E_Berserker2_SelfHalfHP_T2": ('MathModifier: "*2"\nAbilities:\n    ADamageSelf:\n        Damage: 0\n'
                                          '        Filter: Self\n        AutoTrigger: True\n        StatIsBasedOnX:\n'
                                          '            Strength:\n                BasedOn: PercentageOfCurrentHP\n'
                                          '                Multiplier: 0.5\n                RoundingType: ToFloor\n'),
}

STRINGS = {   # the new modifiers' names (the game looks a modifier's Name up as text)
    "AMC_GH2E_Berserker2_Heal1Self_P0": '<sprite name="0"> <sprite name=Heal> 1',
    "AMC_GH2E_Berserker2_SelfHalfHP_T2": '<sprite name="2x"> Suffer <sprite name=Damage> equal to half your current hit point value (rounded down)',
}

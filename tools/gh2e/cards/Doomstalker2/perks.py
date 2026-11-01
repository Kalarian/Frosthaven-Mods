# Doomstalker (2nd edition) perks: the perk sheet "Orchid Doomstalker" (tools/gh2e/classes.json, docs/gh2e/class-notes.md),
# 18 checks in all. gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# Linked perks (two boxes joined on the sheet) are one perk with Available 1, Cost 2, as gh_crossover writes them.

PERKS = [
    (1, "Steady Nerves", 'Replace one <sprite name="-2"> with one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_Basic_P0]"}),
    # "Doomstalker performs: Attack 2, Range 5": an attack inside an attack's modifier draw has no Frosthaven card; the
    # nearest is a modifier with its own damage ability (AMC_Damage1Range1_P0 shape), here 2 damage on one enemy within
    # range 5 that the player picks (NOTES.md)
    (3, "Second Arrow", 'Replace one <sprite name="-1"> with one <sprite name="-1"> "Doomstalker performs: <sprite name=Attack> 2, <sprite name=Range> 5"',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_GH2E_Doomstalker2_Attack2Range5_M1]"}),
    (2, "Keen Eye", 'Replace two <sprite name="0"> with two <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]"}),
    # Frosthaven's HIVE perk card AMC_P0_ControlSummonMove2 (a +0 that grants a summon an action), with an Attack +0;
    # "adjacent to the target" is not checked: any of your summons (NOTES.md)
    (2, "Pack Hunt", 'Add one <sprite name="0"> "Grant one of your summons adjacent to the target: <sprite name=Attack> +0"',
     {"CardsToAdd": "[AMC_GH2E_Doomstalker2_SummonAttack_P0]"}),
    (2, "Deadly Shot", 'Add one <sprite name="+3">',
     {"CardsToAdd": "[AMC_Basic_P3]"}),
    (2, "Stunning Shot", 'Add one <sprite name="0"> <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (1, "Unfazed", 'Ignore scenario effects and add one <sprite name="+1"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P1_R]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # revealing monster ability cards has no Frosthaven ability: text only (NEEDS PLUGIN, NOTES.md)
    (1, "Foresight", "At the start of each scenario, reveal the top card of each monster ability card deck in the scenario",
     {"Flags": "[Passive]"}),
    # Frosthaven's rest perks (ExecuteAbilitiesAfterLongRest / ...ShortRest, Perk_Boneshaper_10's two rest triggers in
    # one perk) with SelectAndPlayCard from the hand (Blinkblade Reverse the Flow), Doom actions only. Unverified: a card
    # played from the hand during a rest (NOTES.md)
    (1, "Restless Doom", "Whenever you rest, after you <sprite name=RecoverIcon> your cards from your discard pile, you may play one card from your hand to perform a Doom action of the card",
     {"Cost": 2, "Flags": "[Passive, ExecuteAbilitiesAfterLongRest, ExecuteAbilitiesAfterShortRest]", "Abilities": """\
    APlayDoom:
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        SelectAndPlayCard:
            CardPile: Hand
        MiscVars:
            IsActionValid: Action.Abilities.Any(obj.AbilityType == CAbility.EAbilityType.AddDoom)"""}),
    # Frosthaven's summons are moved by the player, who can already bring them back: text only (NOTES.md)
    (1, "Heel", "On each of your summons' turns, you may have them skip their turn and instead perform <sprite name=Move> +0 treating you as their focus",
     {"Cost": 2, "Flags": "[Passive]"}),
]

# Neither mastery fits an existing pattern: "Kill 20 enemies that have Doom" needs a total-kills-of-doomed-enemies tag
# (the plugin's FrosthavenMods_DoomedKillsInTurn counts kills in one turn), "you and one of your summons perform 6+
# attack abilities in one round" a per-round attack count over a hero and a summon (NOTES.md, shared changes).
MASTERIES = []

NEW_CARDS = {
    "AMC_GH2E_Doomstalker2_Attack2Range5_M1": ('MathModifier: "-1"\nControlTargetingActor: True\nAbilities:\n    ADamage:\n'
                                               '        Damage: 2\n        Filter: Enemy\n        Targeting: Range\n'
                                               '        Range: 5\n        Target: 1\n'),
    "AMC_GH2E_Doomstalker2_SummonAttack_P0": ('MathModifier: "+0"\nTryToPerformAsSummoner: True\nAbilities:\n    ControlAbility:\n'
                                              '        ControlActor:\n            Abilities:\n                ASummonAttack:\n'
                                              '                    Attack: +0\n                    Range: +0\n'
                                              '        Range: 99\n        Filter: OwnSummon\n        AnimOverload: Grant\n'),
}

STRINGS = {}

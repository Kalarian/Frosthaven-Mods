# Silent Knife (2nd edition) perks: the perk sheet "Human Silent Knife" (tools/gh2e/classes.json; Secretariat agrees),
# 18 checks in all. gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# A linked perk (two boxes joined on the sheet) is one perk with Available 1, Cost 2, as gh_crossover writes them.
# Decisions in NOTES.md.
from gh_crossover import _once_per_quest

PERKS = [
    (1, "Composure", 'Remove one <sprite name="-2">',
     {"CardsToRemove": "[AMC_Basic_M2]"}),
    (1, "Light Step", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (3, "Steady Hand", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    # "Gain one money token if this attack targeted an adjacent enemy": money tokens are loot tokens (Frosthaven FAQ
    # section 10), so a +1 card whose self step (MiscVars GHMoneyGainAdjacent) has the plugin (GHMoneyTokens) give her
    # one loot token (one loot-deck draw, one more money token) when the attack's target is adjacent to her
    (3, "Pickpocket", 'Replace one <sprite name="0"> with one <sprite name="+1"> "Gain one money token if this attack '
     'targeted an adjacent <sprite name=Enemy>"',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_GH2E_SilentKnife_Pickpocket_P1]"}),
    (2, "Vital Strike", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (2, "Vanish", 'Replace one <sprite name="+1"> with one <sprite name="+1"> "<sprite name=Invisible>, self" '
     '<sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P1]", "CardsToAdd": "[AMC_GH2E_SilentKnife_Invisible_P1_R]"}),
    (2, "Disarming Cut", 'Add one <sprite name="+1"> <sprite name=Disarm>',
     {"CardsToAdd": "[AMC_Disarm_P1]"}),
    # Frosthaven's after-rest perk hooks (both rests, as the 1e Plagueherald's Grave Defense). The cost: a step offered
    # only while she holds a money token (SilentKnifeMoney) picks an enemy within range 3; the plugin (GHMoneyTokens,
    # GHMoneyPlace) takes the token, puts one of her loot cards back on top of the loot deck and a loot token in that
    # enemy's hex; the Attack 1 then targets that enemy only (Frosthaven's Boldening Blow shape: a sub-ability aimed at
    # the parent step's target by Custom targeting). Skipping the step skips the attack.
    (1, "Parting Gift", "At the end of each of your rests, you may place one of your money tokens in the hex occupied by "
     "an <sprite name=Enemy> within <sprite name=Range> 3 to perform: <sprite name=Attack> 1, <sprite name=Target> that "
     "<sprite name=Enemy>, <sprite name=Range> 3",
     {"Flags": "[Passive, ExecuteAbilitiesAfterShortRest, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    BMoney:
        NullTargeting: True
        Range: 3
        Target: 1
        Filter: Enemy
        SkipAnim: True
        CanSkip: True
        CanUndo: False
        StartAbilityRequirements:
            Type: ThisAbility
            Vars:
                ValidateMoney: Actor.GetCharacterResourceAmount("SilentKnifeMoney") >= 1
        MiscVars:
            GHMoneyPlace: True
        SubAbilities:
            BAttack:
                Attack: 1
                Range: 3
                Target: 1
                Filter: Enemy
                TreatAsNonSubAbility: True
                CanUndo: False
                Targeting: Custom
                TargetingVars:
                    AddTilesInRange: ParentAbility.ActorsTargeted.Select(Tile)
                    AddValidActorsInRange: ParentAbility.ActorsTargeted"""}),
    # the plugin (GHSilentKnife.cs): Frosthaven's outpost events stand for city events (QUESTIONS.md A8); when one is
    # completed she draws from her modifier deck as an Attack 4 and gains the damage as gold
    (1, "Fence", "Whenever you complete a city event, draw an attack modifier card as though you were performing an "
     "<sprite name=Attack> 4. Gain an amount of gold equal to the <sprite name=Damage> the attack would have dealt",
     {"Flags": "[Passive]"}),
    # two linked boxes: the 1st edition Scoundrel's Cloak of Invisibility (Frosthaven's once-per-scenario toggle)
    (1, "Cloak of Invisibility", "Once each scenario, during your turn, you may perform: <sprite name=Invisible>, self",
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _once_per_quest("PERK_SilentKnife_10_Desc", "Invisible", "            Invisible: True")}),
]

MASTERIES = [
    ("Calculated Strikes", "For an entire scenario, only attack enemies that are adjacent to one of your allies or adjacent "
     "to none of their allies, and perform at least 10 attacks",
     """# Answered by the Frosthaven Mods plugin (GHSilentKnife.cs, FrosthavenMods_FlankedOrIsolatedOnly): every target she
# attacks (checked as each attack happens) stands next to one of her allies or next to none of its own allies, and Value
# attacks (one per target) or more.
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_FlankedOrIsolatedOnly]
        CharacterIDs:
            ActorID: SilentKnifeID
        Value: 10"""),
    ("Deep Pockets", "End 3 scenarios with 12 or more money tokens",
     """# Across scenarios: the Frosthaven Mods plugin (GHSilentKnife.cs) counts the scenarios she ends holding Amount or more
# money tokens in synced campaign state (not necessarily in a row) and completes this mastery from the scenario result
# at Value; this condition only shows the count.
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_MoneyTokensAtEnd]
        CharacterIDs:
            ActorID: SilentKnifeID
        Value: 3
        Amount: 12"""),
]

NEW_CARDS = {
    # Pickpocket: +1, "gain one money token if this attack targeted an adjacent enemy" (Frosthaven's AMC_Resonance_P1
    # self step: NullTargeting, Filter Self, AutoTrigger; the plugin reads the marker and the attack's target)
    "AMC_GH2E_SilentKnife_Pickpocket_P1": 'MathModifier: "+1"\nAbilities:\n    APickpocket:\n        Filter: Self\n'
                                          '        AutoTrigger: True\n        NullTargeting: True\n        SkipAnim: True\n'
                                          '        MiscVars:\n            GHMoneyGainAdjacent: True\n',
    # +1, Invisible self, rolling (Frosthaven's AMC_Invisible_R with +1, as AMC_P1_R is a rolling +1)
    "AMC_GH2E_SilentKnife_Invisible_P1_R": 'MathModifier: "+1"\nRolling: True\nAbilityOverrides:\n    NegCon:\n'
                                           '        ParentName: AMC_GH2E_SilentKnife_Invisible_P1_R\n'
                                           '        Conditions: [Invisible]\n',
}

STRINGS = {}

# Doomstalker (2nd edition) perks: the perk sheet "Orchid Doomstalker" (tools/gh2e/classes.json, docs/gh2e/class-notes.md),
# 18 checks in all. gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# Linked perks (two boxes joined on the sheet) are one perk with Available 1, Cost 2, as gh_crossover writes them.

PERKS = [
    (1, "Steady Nerves", 'Replace one <sprite name="-2"> with one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_Basic_P0]"}),
    # "Doomstalker performs: Attack 2, Range 5": a real Attack 2, Range 5 by the Doomstalker after the attack (NEW_CARDS)
    (3, "Second Arrow", 'Replace one <sprite name="-1"> with one <sprite name="-1"> "Doomstalker performs: <sprite name=Attack> 2, <sprite name=Range> 5"',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_GH2E_Doomstalker2_Attack2Range5_M1]"}),
    (2, "Keen Eye", 'Replace two <sprite name="0"> with two <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]"}),
    # Frosthaven's HIVE perk card AMC_P0_ControlSummonMove2 (a +0 that grants a summon an action), with an Attack +0, for
    # one of your summons adjacent to the attack's target (NEW_CARDS)
    (2, "Pack Hunt", 'Add one <sprite name="0"> "Grant one of your summons adjacent to the target: <sprite name=Attack> +0"',
     {"CardsToAdd": "[AMC_GH2E_Doomstalker2_SummonAttack_P0]"}),
    (2, "Deadly Shot", 'Add one <sprite name="+3">',
     {"CardsToAdd": "[AMC_Basic_P3]"}),
    (2, "Stunning Shot", 'Add one <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (1, "Unfazed", 'Ignore scenario effects and add one <sprite name="+1"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P1_R]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # revealing monster ability cards has no Frosthaven ability: text only (NEEDS PLUGIN, NOTES.md)
    # Frosthaven's Brummix pet (372): a one-use StartRoundAbility whose NullTargeting calls ShowTopMonsterCards() (every
    # monster class in play), placed at scenario start, so it fires at the start of round 1
    (1, "Foresight", "At the start of each scenario, reveal the top card of each monster ability card deck in the scenario",
     {"Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]", "Abilities": """\
    AForesight:
        AddActiveBonus:
            AutoTrigger: True
            NullTargeting: True
            Filter: Self
            MiscVars:
                DontCopy: True
            PerformVars:
                ApplyToActors:
                    ShowMonsterCards: CustomAbility.ShowTopMonsterCards()
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        ActiveBonus:
            Duration: Persistent
            Behaviour: StartRoundAbility
            Hidden: True
            XPTracker: [0]"""}),
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
    # Frosthaven's summons are AI-driven: plugin GHDoomstalker2e (Heel) asks the Doomstalker's player at the start of each
    # summon turn; yes = the summon's own move only, focused on the Doomstalker (benign), no attack
    (1, "Heel", "On each of your summons' turns, you may have them skip their turn and instead perform <sprite name=Move> +0 treating you as their focus",
     {"Cost": 2, "Flags": "[Passive]"}),
]

# Both masteries are answered by the plugin (GHMasteriesDoomstalker2.cs) from the statistics records.
MASTERIES = [
    ("Inevitable End", "Kill 20 enemies that have <nobr><b><color=#39BEED>Doom</color></b></nobr>",
     """# FrosthavenMods_DoomedKillsTotal: Value kills of doomed enemies by the Doomstalker itself (not its summons), within one
# scenario (the user's default for this mastery, QUESTIONS.md B).
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_DoomedKillsTotal]
        CharacterIDs:
            ActorID: Doomstalker2ID
        Value: 20"""),
    ("Pack Leader", "In a single round, you and one of your summons must perform a combined total of 6 or more attack abilities",
     """# FrosthavenMods_HeroPlusSummonAttacks: per round, your attack abilities that happened plus those of one summon (the
# busiest), Value or more.
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_HeroPlusSummonAttacks]
        CharacterIDs:
            ActorID: Doomstalker2ID
        Value: 6"""),
]

NEW_CARDS = {
    # "Doomstalker performs: Attack 2, Range 5": a real attack (Attack is a kind the game's modifier code runs: after the
    # attack as the Doomstalker's own ability, or, when one of the summons drew the card, stacked for the Doomstalker:
    # TryToPerformAsSummoner, as Frosthaven's AMC_P0_Attack2Rolling)
    "AMC_GH2E_Doomstalker2_Attack2Range5_M1": """\
MathModifier: "-1"
TryToPerformAsSummoner: True
Abilities:
    AAttack:
        Attack: 2
        Range: 5
        Target: 1
        Filter: Enemy
        AnimOverload: RangedAttack
""",
    # "Grant one of your summons adjacent to the target": the grant's candidates are your summons on the hexes around
    # the attack's target (TargetActor, set by the game's modifier code for a ControlActor; Frosthaven's AMC_CreateIce_P2
    # reads the same var, FrozenFist Frozen Over turns tiles into actors this way)
    "AMC_GH2E_Doomstalker2_SummonAttack_P0": """\
MathModifier: "+0"
TryToPerformAsSummoner: True
Abilities:
    ControlAbility:
        ControlActor:
            Abilities:
                ASummonAttack:
                    Attack: +0
                    Range: +0
        Target: 1
        Targeting: Custom
        TargetingVars:
            AddValidActorsInRange: CustomAbility.GetAdjacentTiles(TargetActor.Tile).Where(obj.LivingActor != null).Select(obj.LivingActor)
        Filter: OwnSummon
        AnimOverload: Grant
""",
}

STRINGS = {}

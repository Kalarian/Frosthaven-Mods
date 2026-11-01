# Bruiser (2nd edition) perks: the perk sheet "Inox Bruiser" (tools/gh2e/classes.json), 18 checks in all.
# gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# A linked perk (two boxes joined on the sheet) is one perk with Available 1, Cost 2, as gh_crossover writes them.
# Decisions in NOTES.md.
from gh_crossover import _once_per_quest

PERKS = [
    (2, "Precision", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (2, "Brace", 'Replace one <sprite name="-1"> with one <sprite name=Shield> 1 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Shield1_R]"}),
    # The sheet's card goes to the active area and gives Retaliate 2 on the next attack by an adjacent enemy;
    # Frosthaven's nearest card is its rolling +0 Retaliate 2 (Metal Mosaic's perk 4). Approximation, NOTES.md.
    (2, "Spiked Hide", 'Replace one <sprite name="0"> with one "Add this card to your active area. On the next attack '
     'targeting you performed by an adjacent <sprite name=Enemy>, discard this card to gain <sprite name=Retaliate> 2 for '
     'the attack" <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Retaliate2_P0_R]"}),
    (1, "Stunning Blow", 'Replace one <sprite name="0"> with one <sprite name="0"> <sprite name=Stun>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Stun_P0]"}),
    (2, "Second Wind", 'Add one <sprite name="+1"> "<sprite name=Heal> 2, self"',
     {"CardsToAdd": "[AMC_Heal2_Self_P1]"}),
    (2, "Shoulder Charge", 'Add one <sprite name="+2"> <sprite name=Push> 2',
     {"CardsToAdd": "[AMC_GH2E_Bruiser_Push2_P2]"}),
    (1, "Overpower", 'Add one <sprite name="+3">',
     {"CardsToAdd": "[AMC_P3]"}),
    (1, "Dirty Tricks", 'Add one <sprite name="0"> <sprite name=Disarm> <sprite name=Rolling> and one <sprite name="0"> '
     '<sprite name=Muddle> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Disarm_R, AMC_Muddle_R]"}),
    (1, "Hardened", 'Ignore item <sprite name="-1"> effects and add two <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]", "Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    # Frosthaven's once-per-scenario toggle (gh_crossover._once_per_quest): Loot 1, then recover one spent or lost body
    # item when that loot picked up a money token (the loot's own LootTokenCount). Verify on the bench.
    (1, "Scavenger", 'Once each scenario, during your turn, you may perform: <sprite name=Loot> 1, if this ability loots at '
     'least one money token, you may <sprite name=RecoverIcon> one <sprite name=BodyItem> item',
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _once_per_quest("PERK_Bruiser_10_Desc", "Loot", """\
            Loot: 1
            Target: -1
            AnimOverload: Loot
            SubAbilities:
                ARecoverBody:
                    RefreshItems: 1
                    SlotToRefresh: [Body]
                    SlotStateToRefresh: [Spent, Consumed]
                    Filter: Self
                    TreatAsNonSubAbility: True
                    StartAbilityRequirements:
                        Type: ThisAbility
                        Vars:
                            ValidateLootedMoney: Ability.ParentAbility.LootTokenCount > 0""")}),
    # the 1st edition Brute's crossover perk (gh_crossover BRUTE_PERKS "Rested and Ready"), word for word
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
            ListLayoutOverride: [$PERK_Bruiser_11_Bonus$, Positive_MovePlus]"""}),
    # Frosthaven's scenario-start advantage perk (Perk_CrashingTide_10, Perk_Deepwraith_12: a hidden persistent
    # BuffAttack with AttackHasAdvantage and a filter), as an aura on every character (Perk_Boneshaper_06's aura filter)
    # valid in round 1 only. APPROXIMATION: advantage on every attack in round 1, not only each character's first.
    (1, "War Cry", 'Each character gains advantage on their first attack during the first round of each scenario',
     {"Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]", "Abilities": """\
    TActive:
        AutoTrigger: True
        Filter: Self
        SkipAnim: True
        Attack: 0
        AttackHasAdvantage: True
        ActiveBonus:
            Duration: Persistent
            Hidden: True
            Behaviour: BuffAttack
            IsAura: True
            AuraTargeting: All
            AuraFilter:
                TargetType: [Self, Ally]
                ActorType: Player
            Filter:
                Vars:
                    ValidateFirstRound: ScenarioManager.CurrentScenarioState.RoundNumber == 1"""}),
]

MASTERIES = [
    # The sheet's second mastery is the 1st edition Brute's Relentless Onslaught (gh_crossover BRUTE_MASTERIES) with
    # 7 damage: the plugin's streak tag counts the damage on the Bruiser's own turns (the sheet: "during each of those
    # rounds"). The first mastery (in 3 different scenarios, kill an enemy you pushed or pulled that round) has no
    # pattern: not written (NOTES.md).
    ("Relentless Onslaught",
     "In a single scenario, across three consecutive rounds, play six different ability cards and cause enemies to "
     "suffer <sprite name=Damage> 7 or more during each of those rounds",
     """\
# Three rounds in a row with at least 7 damage to enemies on each of the Bruiser's turns and six different ability cards
# played over the three, answered by the Frosthaven Mods plugin (the marker in EnemyFamilies, GHMasteries.cs) from the
# damage and ability records. Value = rounds in a row, Amount = damage per turn, Targets = different cards.
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_DamageStreak]
        CharacterIDs:
            ActorID: BruiserID
        Value: 3
        Amount: 7
        Targets: 6"""),
]

NEW_CARDS = {
    "AMC_GH2E_Bruiser_Push2_P2": 'MathModifier: "+2"\nAbilities:\n    APush:\n        Push: 2\n',
}

STRINGS = {
    "PERK_Bruiser_11_Bonus": "Rested and Ready: +1 <sprite name=Move> to your next move ability",
}
